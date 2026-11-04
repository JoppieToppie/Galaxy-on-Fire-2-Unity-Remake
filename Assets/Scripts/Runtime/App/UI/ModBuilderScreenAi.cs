// ModBuilderScreenAi.cs
// The Mod Builder's "AI tools" tab (part of ModBuilderScreen, so the hangar's and space's screens both have it): the MCP
// server (ModBuilderMcp) for the player's own AI assistant: on / off (off by default), where it listens, the token, the
// commands that connect Claude Code and Claude Desktop (any MCP client: the URL and the header), and what the AI did. Also
// the hooks the server's tools work through: an edit of the project's JSON (an undo step, the screen refreshed like after an
// undo), a question to the player (a removal), undo / redo, the kind shown.

using System;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public abstract partial class ModBuilderScreen
    {
        /// <summary>The builder screen open now (null / not Alive during a play test or outside the builder).</summary>
        public static ModBuilderScreen Open { get; private set; }
        // Play mode runs without a domain reload: a screen open when it ended isn't the next session's.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOpen() => Open = null;

        VisualElement aiPage, aiActivity;
        Button aiButton;
        ModBuilder.Kind kindBeforeAi = ModBuilder.Kind.Ships;
        Action aiChanged;
        Action<bool> pendingAsk;

        internal bool Alive => root.panel != null;
        internal ModBuilder.Kind CurrentKind => kind;
        internal bool ReloadBusy => reloadPending || reloading;

        void BuildAiPanels(VisualElement left, VisualElement right)
        {
            Open = this;
            Add(left, new Label(T("mbAiActivity", "What the AI did").ToUpperInvariant()), "mbv-heading", "gof-semibold");
            var scroll = Add(left, new ScrollView(ScrollViewMode.Vertical), "mbv-ship-scroll");
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            new DragScroll(scroll);
            aiActivity = scroll;
            aiPage = PagePanel(right);
            aiChanged = () =>
            {
                if (!Alive) return;
                aiButton?.EnableInClassList("mbv-ai-button--live", ModBuilderMcp.Running);
                if (kind == ModBuilder.Kind.AiTools) { BuildAiPage(); BuildAiActivity(); }
            };
            aiButton?.EnableInClassList("mbv-ai-button--live", ModBuilderMcp.Running);
            ModBuilderMcp.Changed += aiChanged;
            ModBuilderMcpPump.Ensure();
        }

        /// <summary>The screen goes: the tab stops listening for the server's news.</summary>
        protected void DisposeAi()
        {
            if (aiChanged != null) ModBuilderMcp.Changed -= aiChanged;
            if (Open == this) Open = null;
        }

        protected void ShowAiTools()
        {
            BuildAiPage();
            BuildAiActivity();
        }

        void BuildAiActivity()
        {
            aiActivity.Clear();
            var recent = ModBuilderMcp.Recent;
            if (recent.Count == 0)
            {
                Note(aiActivity, ModBuilderMcp.Running ? T("mbAiNothingYet", "Nothing yet: connect an AI app, then ask it to build something.")
                                                       : T("mbAiOff", "The MCP server is off."));
                return;
            }
            for (int i = recent.Count - 1; i >= 0; i--)
            {
                var a = recent[i];
                var row = Add(aiActivity, new VisualElement(), "mbv-page-box");
                Add(row, new Label($"{a.time:HH:mm:ss}  {a.tool}"), "mbv-inline-label", "gof-semibold");
                var l = Add(row, new Label(a.summary), "mbv-note");
                if (a.error) l.AddToClassList("mbv-warn");
            }
        }

        void BuildAiPage()
        {
            aiPage.Clear();
            Heading(aiPage, T("mbAiTitle", "AI tools (MCP)"));
            Note(aiPage, T("mbAiIntro", "Let your own AI assistant (an MCP app) build this project while the Mod Builder is open. The game sends nothing anywhere."));
            if (!ModBuilderMcp.Supported)
            {
                Note(aiPage, T("mbAiWindowsOnly", "The MCP server is only on Windows for now."));
                return;
            }
            Segments(aiPage, T("mbAiServer", "MCP server"), new[] { T("mbOff", "Off"), T("mbOn", "On") }, ModBuilderMcp.Enabled ? 1 : 0, n =>
            {
                ModBuilderMcp.Enabled = n == 1;
                sound(false);
            });
            string state = ModBuilderMcp.Problem ?? (ModBuilderMcp.Running ? string.Format(T("mbAiListening", "Listening on {0} (this computer only)."), ModBuilderMcp.Url)
                                                     : ModBuilderMcp.Enabled ? T("mbAiStarting", "Starting…") : T("mbAiStopped", "Off: no AI app can reach the builder."));
            var st = Note(aiPage, state);
            if (ModBuilderMcp.Problem != null) st.AddToClassList("mbv-warn");
            if (ModBuilderMcp.Running && ModBuilderMcp.ClientName != null)
                Note(aiPage, string.Format(T("mbAiClient", "Connected: {0}, last request {1:HH:mm:ss}."), ModBuilderMcp.ClientName, ModBuilderMcp.LastRequest));

            Segments(aiPage, T("mbAiPermissions", "Permissions"),
                new[] { T("mbAiPermRead", "Read only"), T("mbAiPermAsk", "Ask always"), T("mbAiPermRemove", "Ask to remove"), T("mbAiPermAll", "Yes to everything") },
                (int)ModBuilderMcp.Permission, n => { ModBuilderMcp.Permission = (ModBuilderMcp.Permissions)n; sound(false); });
            var pn = Note(aiPage, ModBuilderMcp.Permission switch
            {
                ModBuilderMcp.Permissions.ReadOnly => T("mbAiPermReadText", "The AI can look and check missions, but not change anything."),
                ModBuilderMcp.Permissions.AskEveryChange => T("mbAiPermAskText", "Every change waits for your Yes (5 minutes, then No)."),
                ModBuilderMcp.Permissions.AllowEverything => T("mbAiPermAllText", "The AI changes, tests and removes without asking. Undo still takes every change back."),
                _ => T("mbAiPermRemoveText", "The AI makes changes at once; removing an entry waits for your Yes."),
            });
            if (ModBuilderMcp.Permission == ModBuilderMcp.Permissions.AllowEverything) pn.AddToClassList("mbv-warn");

            Int(aiPage, T("mbAiPort", "Port"), ModBuilderMcp.Port, 1024, 65535, v => { if (v != ModBuilderMcp.Port) ModBuilderMcp.Port = v; });
            Caption(aiPage, T("mbAiToken", "Token (the AI app sends it; keep it to yourself)"));
            var trow = Row(aiPage);
            var tokenLabel = Add(trow, new Label(new string('•', 12)), "mbv-inline-label");
            Act(trow, T("mbAiShow", "Show"), () => tokenLabel.text = ModBuilderMcp.Token, "mbv-step");
            Act(trow, T("mbAiCopy", "Copy"), () => { GUIUtility.systemCopyBuffer = ModBuilderMcp.Token; Status(T("mbCopied", "Copied."), false); }, "mbv-step");
            Act(trow, T("mbAiNewToken", "New token"), () => Confirm(T("mbAiNewToken", "New token"),
                T("mbAiNewTokenText", "Connected AI apps stop working until they get the new token. Make one?"), ModBuilderMcp.NewToken), "mbv-step");

            Heading(aiPage, T("mbAiConnect", "Connect an AI app"));
            string url = $"http://127.0.0.1:{ModBuilderMcp.Port}/mcp", token = ModBuilderMcp.Token;
            CopyBlock(T("mbAiClaudeCode", "Claude Code (a terminal in the folder you work in)"),
                $"claude mcp add --transport http gof2-builder {url} --header \"Authorization: Bearer {token}\"");
            var desktop = new JObject
            {
                ["mcpServers"] = new JObject
                {
                    ["gof2-builder"] = new JObject
                    {
                        ["command"] = "npx",
                        ["args"] = new JArray("-y", "mcp-remote", url, "--header", "Authorization:${AUTH}"),
                        ["env"] = new JObject { ["AUTH"] = "Bearer " + token },
                    },
                },
            };
            CopyBlock(T("mbAiClaudeDesktop", "Claude Desktop (Settings > Developer > Edit config; needs Node.js)"),
                desktop.ToString(Newtonsoft.Json.Formatting.Indented));
            CopyBlock(T("mbAiOther", "Any other MCP app: Streamable HTTP, this URL and header"), $"{url}\nAuthorization: Bearer {token}");
            Note(aiPage, T("mbAiSafety", "Only this computer can connect, with the token. The AI writes inside this project only; every change can be undone."));
        }

        /// <summary>A setup text with Copy: the token is hidden on screen (a stream or a screenshot) and in the copy.</summary>
        void CopyBlock(string caption, string text)
        {
            Caption(aiPage, caption);
            var box = Add(aiPage, new VisualElement(), "mbv-page-box");
            string shown = text.Replace(ModBuilderMcp.Token, "<" + T("mbAiTokenHidden", "token: in the copy") + ">");
            Add(box, new TextField { value = shown, multiline = true, isReadOnly = true }, "mbv-field", "mbv-script");
            Act(box, T("mbAiCopy", "Copy"), () => { GUIUtility.systemCopyBuffer = text; Status(T("mbCopied", "Copied."), false); }, "mbv-step");
        }

        // ---- the tools' hooks ------------------------------------------------------------------------------------

        internal void ExternalSaveNow() => SaveNow();

        /// <summary>An edit of the project's JSON (ModProject.CaptureState): 'edit' changes the state or returns why not. Saved,
        /// an undo step, the screen refreshed like after an undo, the game reads the project again.</summary>
        internal string ExternalEdit(Func<JObject, string> edit, string what)
        {
            if (Project == null) return "No project is open.";
            SaveNow();
            var state = Project.CaptureState();
            string error = edit(state);
            if (error != null) return error;
            var restore = BeforeUndo();
            Project.RestoreState(state);
            Project.Save();
            ModBuilder.History?.Commit();
            restore?.Invoke();
            RequestReload(false);
            Status(string.Format(T("mbAiChanged", "AI: {0}"), what), false);
            return null;
        }

        /// <summary>Files of the project changed (write_file): read again, an undo step once saved.</summary>
        internal void ExternalFilesChanged(string what)
        {
            SaveNow();
            RequestReload(false);
            Status(string.Format(T("mbAiChanged", "AI: {0}"), what), false);
        }

        /// <summary>The game reads the project again (validate).</summary>
        internal void ExternalReload() { SaveNow(); RequestReload(false); }

        internal bool ExternalUndo(bool redo)
        {
            var h = ModBuilder.History;
            if (h == null || Project == null) return false;
            SaveNow();
            var restore = BeforeUndo();
            if (!(redo ? h.Redo() : h.Undo())) return false;
            restore?.Invoke();
            RequestReload(false);
            Status(redo ? T("mbRedone", "Redone.") : T("mbUndone", "Undone."), false);
            return true;
        }

        internal void ExternalSelect(ModBuilder.Kind k) => SelectKind(k);

        /// <summary>Asks the player (Yes / No; closing it is No; with 'allowAll' also Yes to everything).</summary>
        internal void ExternalAsk(string title, string text, Action<bool> answer, Action allowAll = null)
        {
            CloseModal();
            modal.Clear();
            var box = Add(modal, new VisualElement(), "mbv-modal-box", "mbv-modal-box--small", "mbv-panel");
            Heading(box, title);
            Add(box, new Label(text), "mbv-confirm-text");
            var row = Add(box, new VisualElement(), "mbv-row");
            pendingAsk = answer;
            Act(row, Localization.Get(134), () => { pendingAsk = null; CloseModal(); answer(true); }, "mbv-danger");
            Act(row, Localization.Get(135), () => { pendingAsk = null; CloseModal(); answer(false); });
            if (allowAll != null)
            {
                Act(row, T("mbAiYesAll", "Yes to everything"), () => { pendingAsk = null; CloseModal(); allowAll(); });
                Note(box, T("mbAiYesAllText", "Yes to everything: the AI won't ask again (change it in AI tools > Permissions)."));
            }
            modal.AddToClassList("mbv-modal--shown");
        }

        /// <summary>The question ran out of time: closed (a No).</summary>
        internal void ExternalCancelAsk() { if (pendingAsk != null) CloseModal(); }

        // ---- what the AI can drive (the screens override what they have) ------------------------------------------

        /// <summary>Selects an entry of the kind shown, as a click on its row would ('id': an id, "override:&lt;n&gt;", a quest's
        /// name, a skinned ship's number); 'options' per kind (a ship's tab, a quest's graph view). Null, or why not.</summary>
        internal virtual string ExternalSelectEntry(string kindName, string id, JObject options) => $"{kindName} can't be picked on this screen.";

        /// <summary>The camera from a named side of what is shown (ModelViewer.Angles' names) at 'zoom' (its unit per screen;
        /// 0 = kept); 'reset' back to the screen's own framing. Null, or why not.</summary>
        internal virtual string ExternalCamera(string angle, float zoom, bool reset) => "This view has no camera to aim (the lounge, the map).";

        /// <summary>The camera has settled (a blend under way, a scene still loading: false).</summary>
        internal virtual bool CameraSettled => true;

        internal virtual string ExternalEngines(float level) => "The engines are only on the hangar's turntable ship.";
        internal virtual string ExternalTestFly() => "Test fly is in the hangar's Ships, Items and Skins, and in NPC ships.";
        internal virtual string ExternalRange(bool fire) => "The weapon range is in space, in the Weapon fx kind.";
        internal virtual string ExternalHangar(string which, out string done) { done = null; return "The hangar is picked in the hangar's kinds."; }
        internal virtual string ExternalHardpoint(JObject a, out JObject done) { done = null; return "Hardpoints are edited in the Ships kind, in the hangar."; }
        internal virtual string ExternalLoadout(JObject a, out JObject done) { done = null; return "The test loadout is the Ships kind's Loadout tab, in the hangar."; }
        internal virtual string ExternalRoomMarker(JObject a, out JObject done) { done = null; return "Room markers are placed in the Hangars & bars kind."; }
        /// <summary>The Systems kind's star map centred on a system (its number, or a project system's id); "galaxy" back from the
        /// overview.</summary>
        internal virtual string ExternalMap(string what) => "The star map is the Systems kind's.";
        /// <summary>What the screen shows selected (kind, entry), for builder_status.</summary>
        internal virtual JObject ExternalSelection() => new JObject { ["kind"] = kind.ToString() };

        /// <summary>The panels hidden (a clean screenshot of the model or room), or shown again.</summary>
        internal void ExternalCleanView(bool on)
        {
            // Opacity, not visibility: a control's own visibility style showed through a hidden panel.
            StyleFloat hide = on ? new StyleFloat(0f) : new StyleFloat(StyleKeyword.Null);
            foreach (var e in new[] { uiTop, uiLeft, uiRight, status }) if (e != null) e.style.opacity = hide;
            foreach (var e in CleanViewHides()) if (e != null) e.style.opacity = hide;
        }

        protected virtual System.Collections.Generic.IEnumerable<VisualElement> CleanViewHides() { yield break; }

        /// <summary>A question closed without an answer is a No.</summary>
        void DropPendingAsk()
        {
            var ask = pendingAsk;
            pendingAsk = null;
            ask?.Invoke(false);
        }
    }
}
