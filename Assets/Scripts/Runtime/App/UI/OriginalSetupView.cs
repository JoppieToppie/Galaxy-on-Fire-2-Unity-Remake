// OriginalSetupView.cs
// Feature: docs/notes/player-assets.md "First start": the screen a build without the original's content shows on the
// startup splash before anything else (StartupSplash, when OriginalSetup.Needed): the player shows the game their copy of
// Galaxy on Fire 2 HD with the system's file picker (ModImport.Pick: Android's and iOS' document pickers, the desktops'
// dialogs), it is converted with its progress shown, then what came
// out (files per kind, what couldn't be read) and on to the logos and the menu. A copy made by an older game (GameContent
// .Stale) gets the same screen with its own title. Built in code into the splash's panel (GoF2Common.uss .original-setup*).
// Then, once (OriginalSetup.HdPromptDue), the HD resources: does the player want to supply the HD pack, and is it kept in a
// folder of its own (switchable) or written over the copy (less space)? The same window over Options (HdWindow) supplies,
// merges or removes them later (docs/notes/player-assets.md "HD resources").

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class OriginalSetupView
    {
        /// <summary>The screen on show (tests drive it).</summary>
        public static OriginalSetupView Current { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Current = null;

        static string T(string key, string english) => Localization.Extra("setup." + key, english);

        readonly VisualElement root, card;
        readonly List<string> chosen = new List<string>();
        Func<ModImport.Picked> picking;
        Action<ModImport.Picked> onPicked;
        OriginalSetup.Job job;
        bool done;
        string pickError;

        /// <summary>The conversion running (null before and after).</summary>
        public OriginalSetup.Job Job => job;

        public OriginalSetupView(VisualElement root)
        {
            this.root = root;
            card = new VisualElement();
            card.AddToClassList("original-setup");
            root.Add(card);
        }

        /// <summary>What the window starts with.</summary>
        public enum Start
        {
            /// <summary>Choosing the player's copy (then the HD question, once).</summary>
            Copy,
            /// <summary>The copy is installed and kept packs wait to be written into it (one added since): their progress.</summary>
            Packs,
            /// <summary>The HD resources: supply them, or (installed in their own folder) merge them into the copy or remove them.</summary>
            Hd,
        }

        /// <summary>Until a copy is installed (or the player quits); 'packsOnly': Start.Packs.</summary>
        public IEnumerator Run(bool packsOnly = false) => Run(packsOnly ? Start.Packs : Start.Copy);

        /// <summary>Until the window's work is done; 'hdPack' (Start.Hd): an HD pack picked already (Options' "Add a pack…"),
        /// straight to how it is installed; 'window': over a screen (HdWindow), its way out "Close" (else "Not now").</summary>
        public IEnumerator Run(Start start, OriginalPacks.Info hdPack = null, bool window = false)
        {
            Current = this;
            closeOnly = window;
            if (start == Start.Packs) StartPacks();
            else if (start == Start.Hd)
            {
                if (hdPack != null) HdHow(hdPack);
                else if (GameContent.HdInstalled) HdManage();
                else AskHd();
            }
            else Choose();
            while (!done)
            {
                if (picking != null)
                {
                    var r = picking();
                    if (r != null) { picking = null; (onPicked ?? Picked)(r); onPicked = null; }
                }
                if (job != null)
                {
                    if (job.Finished) { var j = job; job = null; OriginalSetup.Finish(j); Result(j); }
                    else Progress();
                }
                yield return null;
            }
            card.RemoveFromHierarchy();
            if (Current == this) Current = null;
        }

        // ---- choosing the copy

        void Choose()
        {
            card.Clear();
            Title(GameContent.Stale ? T("staleTitle", "Your copy needs converting again")
                                    : T("title", "Your copy of Galaxy on Fire 2"));
            Text(GameContent.Stale
                ? T("staleText", "This version of the game reads the original's files differently from the one that converted your copy. " +
                                 "Show it your copy once more and it is converted again.")
                : T("text", "This remake comes without the original game's graphics, sounds and text: it makes them from your own copy " +
                            "of Galaxy on Fire 2 HD. Show it the Android version (the .xapk, or the .obb and .apk), the iOS version " +
                            "(a decrypted .ipa) or the macOS app (zipped). It is converted once, on this device, in a few minutes."));
            if (chosen.Count > 0)
            {
                Heading(T("chosen", "To convert"));
                foreach (var p in chosen.ToList())
                    Row(p, T("remove", "Remove"), () => { chosen.Remove(p); Choose(); });
            }
            var packs = OriginalPacks.Kept(OriginalPacks.Folder);
            if (packs.Count > 0)
            {
                Heading(T("packs", "Packs (written over the converted copy)"));
                foreach (var pk in packs)
                {
                    var keep = pk;
                    Row(pk.file, T("remove", "Remove"), () => { try { File.Delete(keep.file); } catch (IOException) { } Choose(); }, pk.title ?? pk.name);
                }
            }
            if (pendingHd != null)
            {
                Heading(T("hdPending", "HD pack (installed after the conversion)"));
                Row(pendingHd.file, T("remove", "Remove"), () => { pendingHd = null; Choose(); }, pendingHd.title ?? pendingHd.name);
            }
            if (pickError != null) Problem(pickError);
            if (chosen.Count > 0)
                Hint(T("hint", "Several files: a later one's files replace an earlier one's (the iOS copy, then Android's for its " +
                               "higher-resolution textures)."));
            var buttons = Buttons();
            var pick = Button(buttons, T("pick", "Choose a file…"), StartPick);
            pick.SetEnabled(picking == null && ModImport.Available);
            var convert = Button(buttons, T("convert", "Convert"), Convert, primary: true);
            convert.SetEnabled(chosen.Count > 0 && picking == null);
            Button(buttons, T("quit", "Quit"), Quit);
            (chosen.Count > 0 ? convert : pick).Focus();
        }

        void StartPick()
        {
            pickError = null;
            onPicked = Picked;
            picking = ModImport.Pick(T("pickTitle", "Your copy of Galaxy on Fire 2 HD"), "Galaxy on Fire 2 HD", OriginalSetup.Extensions,
                                     copyIn: Application.isMobilePlatform);
            Choose();
        }

        void Picked(ModImport.Picked r)
        {
            if (r.error != null) pickError = r.error;
            else if (!r.cancelled && r.path != null)
            {
                string path = r.path;
                if (path.StartsWith(Application.temporaryCachePath, StringComparison.Ordinal))
                {
                    // a picker's copy ("picked.tmp", replaced by the next pick): kept under its own name until the conversion
                    try
                    {
                        Directory.CreateDirectory(OriginalSetup.PickedFolder);
                        string to = Path.Combine(OriginalSetup.PickedFolder, string.IsNullOrEmpty(r.name) ? "copy.zip" : Path.GetFileName(r.name));
                        if (File.Exists(to)) File.Delete(to);
                        File.Move(path, to);
                        path = to;
                    }
                    catch (IOException e) { pickError = e.Message; path = null; }
                }
                var pack = path != null ? OriginalPacks.Read(path) : null;
                if (pack != null && OriginalPacks.IsHd(pack)) pendingHd = pack;   // (installed after the conversion: HdHow)
                else if (pack != null)
                {
                    // a pack (gof2pack.json inside): kept to be written over the copy once it is converted (moved when it is a
                    // picker's copy anyway)
                    try { OriginalPacks.Keep(pack, OriginalPacks.Folder, OriginalSetup.IsImportCopy(path)); }
                    catch (IOException e) { pickError = e.Message; }
                }
                else if (path != null && !chosen.Contains(path)) chosen.Add(path);
            }
            Choose();
        }

        /// <summary>Adds a package and starts converting (tests).</summary>
        public void ConvertNow(params string[] packages)
        {
            chosen.Clear();
            chosen.AddRange(packages);
            Convert();
        }

        /// <summary>The result's Continue (tests): false while there is no successful result on show.</summary>
        public bool Continue()
        {
            if (!succeeded) return false;
            succeeded = false;
            AfterCopy();
            return true;
        }

        /// <summary>Closes the window whatever is on show (tests, the build's self-check).</summary>
        public void Close() => done = true;

        /// <summary>The HD question is on show (tests).</summary>
        public bool AskingHd { get; private set; }

        /// <summary>The HD question's "Not now" (tests): false while it isn't on show.</summary>
        public bool NotNow()
        {
            if (!AskingHd) return false;
            DeclineHd();
            return true;
        }

        bool succeeded;

        // ---- converting

        Label phase, entry, elapsed;
        VisualElement fill;
        float started;

        void Convert()
        {
            if (chosen.Count == 0 || job != null) return;
            back = Choose;
            ShowProgress(OriginalSetup.Convert(chosen), T("converting", "Converting your copy"),
                         T("convertingText", "This happens once. The game starts as soon as it is done."));
        }

        bool packsOnly, hdJob, closeOnly;
        OriginalPacks.Info pendingHd;
        string hdDoneTitle, hdDoneText;
        Action back;

        void StartPacks()
        {
            packsOnly = true;
            back = Choose;
            ShowProgress(OriginalSetup.ApplyPacks(), T("installingPacks", "Installing packs"),
                         T("installingPacksText", "Writing the new packs over your converted copy."));
        }

        void ShowProgress(OriginalSetup.Job j, string title, string text)
        {
            job = j;
            started = Time.realtimeSinceStartup;
            card.Clear();
            Title(title);
            Text(text);
            var bar = new VisualElement();
            bar.AddToClassList("original-setup-bar");
            fill = new VisualElement();
            fill.AddToClassList("original-setup-bar-fill");
            bar.Add(fill);
            phase = Text("");
            card.Add(bar);
            entry = Hint("");
            elapsed = Hint("");
            Progress();
        }

        void Progress()
        {
            string p = job.Phase;
            phase.text = p.StartsWith("read ") ? string.Format(T("reading", "Reading {0}"), p.Substring(5))
                       : p.StartsWith("pack ") ? string.Format(T("installingPack", "Installing {0}"), p.Substring(5))
                       : p == "tables" ? T("tables", "Making the text, the data tables and the interface images")
                       : p == "merge" ? T("hdMerging", "Moving the HD resources into your copy")
                       : p == "remove" ? T("hdRemoving", "Removing the HD resources")
                       : p == "swap" || p == "done" ? T("finishing", "Finishing")
                       : T("starting", "Starting");
            float f = job.Total > 0 ? Mathf.Clamp01(job.Done / (float)job.Total) : p == "tables" || p == "swap" ? 1f : 0f;
            fill.style.width = Length.Percent(f * 100f);
            entry.text = job.Entry ?? "";
            int s = (int)(Time.realtimeSinceStartup - started);
            elapsed.text = $"{s / 60}:{s % 60:00}";
        }

        // ---- the result

        void Result(OriginalSetup.Job j)
        {
            card.Clear();
            if (j.Error != null)
            {
                Title(T("failed", "That didn't work"));
                Text(string.Format(T("failedText", "Nothing was installed: {0}"), j.Error));
                var b = Buttons();
                Button(b, T("back", "Back"), back ?? Choose, primary: true).Focus();
                if (hdJob) Button(b, T("close", "Close"), () => done = true);
                else Button(b, T("quit", "Quit"), Quit);
                hdJob = false;
                return;
            }
            if (hdJob)
            {
                hdJob = false;
                Title(hdDoneTitle);
                if (!string.IsNullOrEmpty(hdDoneText)) Text(hdDoneText);
                Button(Buttons(), T("continue", "Continue"), () => done = true, primary: true).Focus();
                return;
            }
            succeeded = true;
            if (packsOnly) { succeeded = false; AfterCopy(); return; }   // (packs written: straight on)
            Title(T("done", "Your copy is ready"));
            string Kind(string k, string english) => j.Counts.TryGetValue(k, out var n) ? $"{n:N0} {T("kind." + k, english)}" : null;
            var kinds = new[] { Kind("Models", "models"), Kind("Textures", "textures"), Kind("Audio", "sounds"),
                                Kind("Localization", "languages"), Kind("Resources", "tables and images") }.Where(k => k != null);
            Text(string.Format(T("doneText", "{0:N0} files in {1:0} s: {2}."), j.Files, j.Seconds, string.Join(", ", kinds)));
            if (j.Problems.Count > 0)
            {
                Heading(string.Format(T("problems", "Not read ({0})"), j.Problems.Count));
                foreach (var p in j.Problems.Take(8)) Problem(p);
            }
            var buttons = Buttons();
            Button(buttons, T("continue", "Continue"), () => Continue(), primary: true).Focus();
        }

        // ---- the HD resources

        /// <summary>After the copy (or its packs): the HD question once, else on.</summary>
        void AfterCopy()
        {
            if (pendingHd != null && GameContent.Installed) { var p = pendingHd; pendingHd = null; HdHow(p); }
            else if (OriginalSetup.HdPromptDue) AskHd();
            else done = true;
        }

        static string Size(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : $"{Math.Max(0, bytes) / 1048576.0:N0} MB";

        void AskHd()
        {
            card.Clear();
            AskingHd = true;
            Title(T("hdTitle", "HD resources"));
            Text(T("hdText", "The HD pack brings higher-resolution textures, the upscaled skies and the 3D planets. It isn't part of " +
                             "your copy of the game: it is distributed separately (about 2.3 GB). Do you want to supply it?"));
            Hint(T("hdLater", "You can supply it later too, in Options > Graphics."));
            if (pickError != null) Problem(pickError);
            var buttons = Buttons();
            var pick = Button(buttons, T("hdPick", "Choose the HD pack…"), StartPickHd, primary: true);
            pick.SetEnabled(picking == null && ModImport.Available);
            Button(buttons, closeOnly ? T("close", "Close") : T("hdNotNow", "Not now"), DeclineHd);
            pick.Focus();
        }

        void DeclineHd()
        {
            AskingHd = false;
            PlayerPrefs.SetInt(OriginalSetup.HdAskedPref, 1);
            PlayerPrefs.Save();
            done = true;
        }

        void StartPickHd()
        {
            pickError = null;
            onPicked = PickedHd;
            picking = ModImport.Pick(T("hdPickTitle", "The HD pack"), "Galaxy on Fire 2 packs",
                                     new[] { OriginalPacks.Extension.TrimStart('.'), "zip" }, copyIn: Application.isMobilePlatform);
            AskHd();
        }

        void PickedHd(ModImport.Picked r)
        {
            if (r.error != null) pickError = r.error;
            else if (!r.cancelled && r.path != null)
            {
                var info = OriginalPacks.Read(r.path);
                if (info == null) pickError = Localization.Extra("packNotPack", "That isn't a Galaxy on Fire 2 pack (no gof2pack.json in it).");
                else if (!OriginalPacks.IsHd(info))
                    pickError = string.Format(T("hdNotHd", "That is the {0}, not the HD pack (Options > Graphics, Add a pack… takes it)."), info.title ?? info.name);
                else { HdHow(info); return; }
                if (OriginalSetup.IsImportCopy(r.path) || r.path.StartsWith(Application.temporaryCachePath, StringComparison.Ordinal))
                    try { File.Delete(r.path); } catch (IOException) { }
            }
            AskHd();
        }

        /// <summary>The HD pack picked: kept beside the originals (switchable) or written over them (less space).</summary>
        void HdHow(OriginalPacks.Info pack)
        {
            card.Clear();
            AskingHd = false;
            Title(T("hdHowTitle", "How should the HD resources be installed?"));
            long alongside = pack.bytes, over = pack.bytes;
            try { (alongside, over) = OriginalPacks.HdSpace(pack, GameContent.Root); }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException) { Problem(e.Message); }
            Choice(T("hdAlongside", "Keep the originals too"),
                   string.Format(T("hdAlongsideText", "In a folder of their own, used instead of your copy's files. Options > Graphics " +
                                                      "can turn them off again. Takes {0}."), Size(alongside)),
                   T("hdInstall", "Install"), () => InstallHd(pack, false), primary: true);
            Choice(T("hdOver", "Replace the originals"),
                   string.Format(T("hdOverText", "Written over your converted copy: takes {0}, {1} less. Turning them off then " +
                                                 "means converting your copy again."), Size(over), Size(alongside - over)),
                   T("hdReplace", "Replace"), () => InstallHd(pack, true));
            var buttons = Buttons();
            Button(buttons, T("back", "Back"), AskHd);
            Button(buttons, closeOnly ? T("close", "Close") : T("hdNotNow", "Not now"), DeclineHd);
        }

        void InstallHd(OriginalPacks.Info pack, bool over)
        {
            if (job != null) return;
            PlayerPrefs.SetInt(OriginalPacks.HdModePref, over ? 1 : 0);
            PlayerPrefs.SetInt(OriginalSetup.HdAskedPref, 1);
            PlayerPrefs.Save();
            hdJob = true;
            back = AskHd;
            hdDoneTitle = T("hdDone", "The HD resources are installed");
            hdDoneText = over ? T("hdDoneOver", "They are part of your converted copy now.")
                              : T("hdDoneAlongside", "Options > Graphics, \"Use the HD resources\" turns them off and on (from the next start).");
            ShowProgress(OriginalSetup.InstallHd(pack, over), T("hdInstalling", "Installing the HD resources"),
                         over ? T("hdInstallingOver", "Writing them over your converted copy.")
                              : T("hdInstallingAlongside", "Into a folder of their own beside your converted copy."));
        }

        /// <summary>Installed in their own folder: written over the copy (space), removed, or kept.</summary>
        void HdManage()
        {
            card.Clear();
            Title(T("hdTitle", "HD resources"));
            long size = 0, frees = 0;
            try
            {
                string hd = Path.GetFullPath(GameContent.HdRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                foreach (var f in Directory.GetFiles(hd, "*", SearchOption.AllDirectories))
                {
                    long n = new FileInfo(f).Length;
                    size += n;
                    var orig = new FileInfo(Path.Combine(GameContent.Root, f.Substring(hd.Length)));
                    if (orig.Exists) frees += orig.Length;
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Problem(e.Message); }
            Text(string.Format(T("hdInstalledText", "Installed in a folder of their own ({0}), used instead of your copy's files while " +
                                                    "\"Use the HD resources\" is on."), Size(size)));
            Choice(T("hdMerge", "Replace the originals"),
                   string.Format(T("hdMergeText", "Moves them over your converted copy and frees {0}. Turning them off then means " +
                                                  "converting your copy again."), Size(frees)),
                   T("hdReplace", "Replace"), () => RunHdJob(OriginalSetup.MergeHd(), 1));
            Choice(T("hdRemove", "Remove the HD resources"),
                   string.Format(T("hdRemoveText", "Frees {0}. You can supply them again later."), Size(size)),
                   T("hdRemoveButton", "Remove"), () => RunHdJob(OriginalSetup.RemoveHd(), 0));
            Button(Buttons(), T("close", "Close"), () => done = true, primary: true).Focus();
        }

        void RunHdJob(OriginalSetup.Job j, int mode)
        {
            if (job != null) return;
            PlayerPrefs.SetInt(OriginalPacks.HdModePref, mode);
            PlayerPrefs.Save();
            hdJob = true;
            back = HdManage;
            hdDoneTitle = mode == 1 ? T("hdMerged", "The HD resources are part of your copy now") : T("hdRemoved", "The HD resources are removed");
            hdDoneText = null;
            ShowProgress(j, T("hdTitle", "HD resources"), "");
        }

        /// <summary>One way to go: its title and what it means, its button at the right.</summary>
        void Choice(string title, string detail, string button, Action onClick, bool primary = false)
        {
            var row = new VisualElement();
            row.AddToClassList("original-setup-choice");
            var text = new VisualElement { style = { flexGrow = 1, flexShrink = 1 } };
            var name = new Label(title);
            name.AddToClassList("original-setup-file");
            text.Add(name);
            var d = new Label(detail);
            d.AddToClassList("original-setup-file-detail");
            text.Add(d);
            row.Add(text);
            var b = Button(row, button, onClick, primary);
            card.Add(row);
            if (primary) b.Focus();
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---- building blocks

        void Title(string s) => Add(s, "original-setup-title");
        Label Text(string s) => Add(s, "original-setup-text");
        Label Hint(string s) => Add(s, "original-setup-hint");
        void Heading(string s) => Add(s, "original-setup-heading");
        void Problem(string s) => Add(s, "original-setup-problem");

        Label Add(string s, string cls)
        {
            var l = new Label(s);
            l.AddToClassList(cls);
            card.Add(l);
            return l;
        }

        void Row(string path, string action, Action onClick, string title = null)
        {
            var row = new VisualElement();
            row.AddToClassList("original-setup-row");
            var text = new VisualElement { style = { flexGrow = 1, flexShrink = 1 } };
            var name = new Label(title ?? Path.GetFileName(path));
            name.AddToClassList("original-setup-file");
            text.Add(name);
            string size = "";
            try { size = $"{new FileInfo(path).Length / 1048576.0:N0} MB · "; } catch (IOException) { }
            var detail = new Label(size + (OriginalSetup.IsImportCopy(path) ? T("picked", "picked") : Path.GetDirectoryName(path)));
            detail.AddToClassList("original-setup-file-detail");
            text.Add(detail);
            row.Add(text);
            Button(row, action, onClick, compact: true);
            card.Add(row);
        }

        VisualElement Buttons()
        {
            var b = new VisualElement();
            b.AddToClassList("original-setup-buttons");
            card.Add(b);
            return b;
        }

        static Button Button(VisualElement parent, string text, Action onClick, bool primary = false, bool compact = false)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("gof-button");
            if (primary) b.AddToClassList("gof-button--primary");
            if (compact) b.AddToClassList("gof-button--compact");
            parent.Add(b);
            return b;
        }
    }
}
