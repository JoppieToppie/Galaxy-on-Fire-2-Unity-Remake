// ModBuilderViewScripts.cs
// The Mod Builder's Scripts kind (part of ModBuilderView; docs/notes/mod-builder.md "Scripts kind"): the project's Lua
// files (scripts/*.lua, Modding.ModScripts runs them) edited in the builder. Left: the files (main.lua first, then the
// others that run at start, then the modules in subfolders), New / Rename / Remove. Right, wide (the 3D view isn't needed):
// the picked file in a plain text editor (a monospace system font, Tab = four spaces), under it the cursor's line and
// column and the Lua syntax check as it is typed (a click on an error goes to its line), Save & run (the project's scripts
// start afresh in the builder's game; they also restart by themselves whenever a saved file changed), the API reference,
// and the console: what the project's scripts printed and their errors (ModScripts.Lines).
// A typed change is written to the file 0.4 s after the last key (the builder's save point, so undo covers it).

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Modding;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class ModBuilderView
    {
        ScrollView scriptList, scriptConsole;
        VisualElement scriptPage;
        TextField scriptField;
        Label scriptTitle, scriptCheck, scriptCursor;
        string currentScript, scriptError;
        bool scriptDirty, scriptFilling;
        float scriptCheckAtMs = -1f, scriptSaveAtMs = -1f;
        int scriptCursorShown = -1;
        Action scriptLogged;
        static FontAsset codeFont;
        static bool codeFontTried;

        /// <summary>A monospace font of the system's for the editor (none found: the interface font).</summary>
        static FontAsset CodeFont()
        {
            if (codeFontTried) return codeFont;
            codeFontTried = true;
            foreach (string family in new[] { "Consolas", "Cascadia Mono", "Menlo", "DejaVu Sans Mono", "Liberation Mono", "Courier New", "Droid Sans Mono", "monospace" })
            {
                try { codeFont = FontAsset.CreateFontAsset(family, "Regular", 44); } catch (Exception) { codeFont = null; }
                if (codeFont != null) break;
            }
            return codeFont;
        }

        void BuildScriptsPanels(VisualElement left, VisualElement right)
        {
            scriptList = ListPanel(left, T("mbScripts", "Scripts"));
            Act(left, T("mbNewScript", "+ New script"), NewScript, "mbv-add");
            Act(left, T("mbRenameScript", "Rename"), RenameScript, "mbv-remove");
            Act(left, T("mbRemoveScript", "Remove script"), AskRemoveScript, "mbv-danger", "mbv-remove");

            scriptPage = Add(right, new VisualElement(), "mbv-code-page");
            var head = Add(scriptPage, new VisualElement(), "mbv-code-head");
            scriptTitle = Add(head, new Label(), "mbv-code-title", "gof-semibold");
            Act(head, T("mbScriptRun", "Save & run"), RunScripts, "mbv-code-button");
            Act(head, T("mbScriptApi", "API reference"), ShowScriptApi, "mbv-code-button");
            scriptField = Add(scriptPage, new TextField { multiline = true }, "mbv-field", "mbv-script", "mbv-code");
            scriptField.verticalScrollerVisibility = ScrollerVisibility.Auto;
            if (CodeFont() != null) scriptField.style.unityFontDefinition = new StyleFontDefinition(CodeFont());
            scriptField.RegisterValueChangedCallback(e =>
            {
                if (scriptFilling || currentScript == null) return;
                scriptDirty = true;
                scriptSaveAtMs = nowMs + 400f;
                scriptCheckAtMs = nowMs + 250f;
            });
            // Tab types four spaces instead of leaving the field.
            scriptField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Tab && e.character != '\t') return;
                e.StopImmediatePropagation();
                scriptField.focusController?.IgnoreEvent(e);
                if (e.keyCode != KeyCode.Tab) return;
                int at = Mathf.Clamp(Mathf.Min(scriptField.cursorIndex, scriptField.selectIndex), 0, scriptField.value.Length);
                int end = Mathf.Clamp(Mathf.Max(scriptField.cursorIndex, scriptField.selectIndex), 0, scriptField.value.Length);
                scriptField.value = scriptField.value.Substring(0, at) + "    " + scriptField.value.Substring(end);
                scriptField.cursorIndex = scriptField.selectIndex = at + 4;
            }, TrickleDown.TrickleDown);
            var foot = Add(scriptPage, new VisualElement(), "mbv-code-foot");
            scriptCursor = Add(foot, new Label(), "mbv-code-cursor");
            scriptCheck = Add(foot, new Label(), "mbv-code-check");
            scriptCheck.RegisterCallback<ClickEvent>(_ => GoToScriptError());
            var consoleHead = Add(scriptPage, new VisualElement(), "mbv-code-head");
            Add(consoleHead, new Label(T("mbScriptConsole", "Console").ToUpperInvariant()), "mbv-label", "mbv-code-title");
            Act(consoleHead, T("mbScriptClear", "Clear"), () => ModScripts.ClearLines(), "mbv-code-button");
            scriptConsole = Add(scriptPage, new ScrollView(ScrollViewMode.Vertical), "mbv-code-console");
            new DragScroll(scriptConsole);
            scriptLogged = () => { if (root.panel != null && kind == ModBuilder.Kind.Scripts) BuildScriptConsole(); };
            ModScripts.Logged += scriptLogged;
        }

        void DisposeScripts()
        {
            SaveScript();
            if (scriptLogged != null) ModScripts.Logged -= scriptLogged;
            scriptLogged = null;
        }

        void ShowScripts()
        {
            if (Project == null) return;
            var files = Project.ScriptFiles();
            if (currentScript == null || !files.Contains(currentScript)) currentScript = files.FirstOrDefault();
            RebuildScripts();
            LoadScript();
            BuildScriptConsole();
        }

        void RebuildScripts()
        {
            scriptList.Clear();
            if (Project == null) return;
            var files = Project.ScriptFiles();
            foreach (string f in files)
            {
                string name = f;
                bool module = f.Contains("/");
                string sub = module ? string.Format(T("mbScriptModule", "module: require(\"{0}\")"), f.Substring(0, f.Length - ModScripts.Extension.Length).Replace('/', '.'))
                    : T("mbScriptStart", "runs when the mod starts");
                ListRow(scriptList, f, sub, f == currentScript, () => PickScript(name), null, null);
            }
            if (files.Count == 0)
                Note(scriptList, T("mbScriptsNone", "No scripts yet. Scripts are Lua files: they can listen to the game's events, change its data and reach its classes. Players are asked before a mod with scripts runs."));
            UpdateRemoveButtons();
        }

        void PickScript(string name)
        {
            if (name == currentScript) return;
            SaveScript();
            currentScript = name;
            RebuildScripts();
            LoadScript();
        }

        /// <summary>The picked file into the editor.</summary>
        void LoadScript()
        {
            scriptFilling = true;
            scriptField.SetValueWithoutNotify(currentScript != null ? Project.ReadScript(currentScript).Replace("\r\n", "\n") : "");
            scriptFilling = false;
            scriptField.SetEnabled(currentScript != null);
            scriptTitle.text = currentScript != null ? ModScripts.Folder + "/" + currentScript : T("mbScriptNone", "No script picked");
            scriptDirty = false;
            scriptCursorShown = -1;
            CheckScript();
        }

        /// <summary>The editor's text into its file (when it changed): a save point of the builder, so an undo step.</summary>
        void SaveScript()
        {
            if (!scriptDirty || currentScript == null || Project == null) return;
            scriptDirty = false;
            if (Project.WriteScript(currentScript, scriptField.value ?? "")) MarkDirty();
        }

        void CheckScript()
        {
            scriptError = currentScript != null ? ModScripts.SyntaxError(scriptField.value, currentScript) : null;
            scriptCheck.text = currentScript == null ? "" : scriptError ?? T("mbScriptOk", "No syntax errors");
            scriptCheck.EnableInClassList("mbv-code-check--error", scriptError != null);
        }

        /// <summary>"main.lua:(12,5-9): ..." -> the cursor to line 12.</summary>
        void GoToScriptError()
        {
            if (scriptError == null) return;
            int open = scriptError.IndexOf(":(", StringComparison.Ordinal);
            if (open < 0) return;
            int i = open + 2, line = 0;
            while (i < scriptError.Length && char.IsDigit(scriptError[i])) line = line * 10 + (scriptError[i++] - '0');
            if (line < 1) return;
            string text = scriptField.value ?? "";
            int at = 0;
            for (int l = 1; l < line && at < text.Length; l++)
            {
                int next = text.IndexOf('\n', at);
                if (next < 0) break;
                at = next + 1;
            }
            scriptField.Focus();
            scriptField.cursorIndex = scriptField.selectIndex = at;
        }

        void UpdateScripts()
        {
            if (kind != ModBuilder.Kind.Scripts || scriptField == null) return;
            if (scriptCheckAtMs >= 0f && nowMs >= scriptCheckAtMs) { scriptCheckAtMs = -1f; CheckScript(); }
            if (scriptSaveAtMs >= 0f && nowMs >= scriptSaveAtMs) { scriptSaveAtMs = -1f; SaveScript(); }
            int cursor = scriptField.cursorIndex;
            if (cursor == scriptCursorShown) return;
            scriptCursorShown = cursor;
            string text = scriptField.value ?? "";
            int line = 1, column = 1;
            for (int i = 0; i < cursor && i < text.Length; i++)
                if (text[i] == '\n') { line++; column = 1; } else column++;
            scriptCursor.text = currentScript == null ? "" : string.Format(T("mbScriptCursor", "Line {0}, column {1}"), line, column);
        }

        /// <summary>Save & run: the file saved, the game reads the project again and its scripts start afresh.</summary>
        void RunScripts()
        {
            SaveScript();
            SaveNow();
            ModScripts.ClearLines();
            int starts = ModScripts.Starts;
            afterReload.Add(() =>
            {
                // A changed file restarted them when the game read the project again; an unchanged one starts them here.
                ModScripts.Sync(ModScripts.Starts == starts);
                BuildScriptConsole();
                bool failed = ModScripts.Lines.Any(l => l.error);
                Status(failed ? T("mbScriptRunFailed", "The scripts ran with errors: see the console.")
                    : Project.ScriptFiles().Count == 0 ? T("mbScriptRunNone", "The project has no scripts.") : T("mbScriptRan", "The scripts are running."), failed);
            });
            RequestReload(false);
        }

        void BuildScriptConsole()
        {
            if (scriptConsole == null) return;
            scriptConsole.Clear();
            string id = Project?.Id;
            var lines = ModScripts.Lines.Where(l => l.mod == id).ToList();
            foreach (var l in lines.Skip(Math.Max(0, lines.Count - 120)))
            {
                var label = Add(scriptConsole, new Label(l.text), "mbv-code-line");
                label.EnableInClassList("mbv-code-line--error", l.error);
                if (CodeFont() != null) label.style.unityFontDefinition = new StyleFontDefinition(CodeFont());
            }
            if (lines.Count == 0) Note(scriptConsole, T("mbScriptConsoleEmpty", "What your scripts print (print, game.log) and their errors show here."));
            scriptConsole.schedule.Execute(() => scriptConsole.scrollOffset = new Vector2(0f, float.MaxValue));
        }

        void NewScript()
        {
            if (Project == null) return;
            void Make(string typed)
            {
                SaveScript();
                string name = Project.AddScript(typed, out string error);
                if (name == null) { Status(error, true); return; }
                currentScript = name;
                MarkDirty();
                RebuildScripts();
                LoadScript();
            }
            // The first script is main.lua; the others are named ("lib/util" makes a module in a folder).
            if (Project.ScriptFiles().Count == 0) Make("");
            else AskText(T("mbNewScript", "+ New script").TrimStart('+', ' '), T("mbScriptName", "Name (lib/util = a module in a folder)"), "", Make, 60);
        }

        void RenameScript()
        {
            if (Project == null || currentScript == null) return;
            AskText(T("mbRenameScript", "Rename"), T("mbScriptName", "Name (lib/util = a module in a folder)"),
                currentScript.Substring(0, currentScript.Length - ModScripts.Extension.Length), typed =>
                {
                    SaveScript();
                    string name = Project.RenameScript(currentScript, typed, out string error);
                    if (name == null) { Status(error, true); return; }
                    currentScript = name;
                    MarkDirty();
                    RebuildScripts();
                    LoadScript();
                }, 60);
        }

        void AskRemoveScript()
        {
            if (Project == null || currentScript == null) return;
            string name = currentScript;
            Confirm(T("mbRemoveScript", "Remove script"), string.Format(T("mbRemoveScriptText", "Remove {0} from the project?"), name), () =>
            {
                scriptDirty = false;
                Project.RemoveScript(name);
                currentScript = null;
                MarkDirty();
                ShowScripts();
            });
        }

        /// <summary>The script API in short (the modding guide's "Scripts" has it in full): read only, to copy from.</summary>
        void ShowScriptApi()
        {
            modal.Clear();
            var box = Add(modal, new VisualElement(), "mbv-modal-box", "mbv-panel");
            Heading(box, T("mbScriptApi", "API reference"));
            var scroll = Add(box, new ScrollView(ScrollViewMode.Vertical), "mbv-right-scroll");
            new DragScroll(scroll);
            var text = Add(scroll, new TextField { value = ModScripts.ApiHelp, multiline = true, isReadOnly = true }, "mbv-field", "mbv-script", "mbv-code");
            if (CodeFont() != null) text.style.unityFontDefinition = new StyleFontDefinition(CodeFont());
            Act(box, T("mbClose", "Close"), CloseModal);
            modal.AddToClassList("mbv-modal--shown");
        }
    }
}
