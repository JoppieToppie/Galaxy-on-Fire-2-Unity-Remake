// IssueReport.cs
// Remake-only: what an in-game issue report carries (the pause menu's "Report an issue", PauseMenuReport), so a problem can
// be found without asking the player back and forth:
//   report.txt     the player's description, the build, the system (no device name or id), where the game was, and
//                  every installed mod: on / off, version, hash, its errors and warnings
//   screenshot.jpg the screen as it was under the pause menu
//   log.txt        the last LogEntries log lines of this run, errors with their stack (kept in memory from the start:
//                  a phone has no Player.log)
//   Player.log, Player-prev.log   the ends of Unity's own log files, where the platform writes them
//   settings.txt   every option's value (OptionsCatalog)
//   save.json      the running game as a save file (SaveGame.SnapshotJson: load it as a slot_NN.json), unless left out
// Collect builds the files, WriteZip keeps them as one zip in <data>/IssueReports (the newest KeepReports), and
// IssueReportUpload sends them to the project's Unity dashboard. Without a connection the zip is what the player attaches
// to an issue on GitHub (IssueUrl opens a filled-in one). The player's home folder is cut from every text (Scrub).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class IssueReport
    {
        public const string IssuesPage = "https://github.com/" + UpdateCheck.Repository + "/issues/new";
        /// <summary>The description's longest text (the form's field).</summary>
        public const int MaxDescription = 4000;
        /// <summary>The log lines kept in memory, and how much of one line and of its stack.</summary>
        public const int LogEntries = 400;
        const int MaxMessage = 2000, MaxStack = 4000;
        /// <summary>How much of the ends of Unity's own log files goes along.</summary>
        const int PlayerLogTail = 512 * 1024, PreviousLogTail = 256 * 1024;
        /// <summary>The zips kept in the IssueReports folder (the newest).</summary>
        public const int KeepReports = 20;
        /// <summary>A GitHub issue's address stays well under its limit: the description is cut to this in it.</summary>
        const int IssueBodyText = 1500;
        /// <summary>GitHub refuses an address much over 8000 characters.</summary>
        const int MaxIssueUrl = 7000;

        /// <summary>One file of a report.</summary>
        public sealed class Entry
        {
            public string name, mediaType;
            public byte[] data;
        }

        /// <summary>What the player filled in, and what the screen it was opened from adds.</summary>
        public sealed class Draft
        {
            public string description = "";
            /// <summary>The screenshot as a JPG, null without one.</summary>
            public byte[] screenshot;
            public bool includeSave = true;
            /// <summary>Lines about the scene the report was made in (the ship, its place), null for none.</summary>
            public string context;
        }

        public static string Folder => Path.Combine(GamePaths.Data, "IssueReports");

        // ---- the log kept in memory ------------------------------------------------------------------------------

        struct LogLine
        {
            public DateTime time;
            public LogType type;
            public string message, stack;
        }

        static readonly Queue<LogLine> log = new Queue<LogLine>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Application.logMessageReceivedThreaded -= OnLog;
            lock (log) log.Clear();
        }

        /// <summary>Starts keeping the log (Bootstrap, once per run).</summary>
        public static void Install()
        {
            Application.logMessageReceivedThreaded -= OnLog;
            Application.logMessageReceivedThreaded += OnLog;
        }

        static void OnLog(string message, string stack, LogType type) => AddLog(message, stack, type, DateTime.Now);

        /// <summary>One log line into the memory (any thread); the oldest goes once LogEntries are kept.</summary>
        public static void AddLog(string message, string stack, LogType type, DateTime time)
        {
            var line = new LogLine
            {
                time = time,
                type = type,
                message = Cut(message, MaxMessage),
                stack = type == LogType.Log ? null : Cut(stack, MaxStack),   // a plain line's stack says nothing
            };
            lock (log)
            {
                log.Enqueue(line);
                while (log.Count > LogEntries) log.Dequeue();
            }
        }

        static string Cut(string text, int max) =>
            string.IsNullOrEmpty(text) ? "" : text.Length <= max ? text : text.Substring(0, max) + " [...]";

        /// <summary>The kept log lines, oldest first: "HH:mm:ss.fff [Type] message", a warning's / error's stack indented.</summary>
        public static string LogText()
        {
            LogLine[] lines;
            lock (log) lines = log.ToArray();
            var sb = new StringBuilder();
            foreach (var l in lines)
            {
                sb.Append(l.time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append(" [").Append(l.type).Append("] ").Append(l.message).Append('\n');
                if (string.IsNullOrEmpty(l.stack) || l.type == LogType.Warning) continue;
                foreach (var s in l.stack.Split('\n'))
                    if (s.Trim().Length > 0) sb.Append("    ").Append(s.TrimEnd()).Append('\n');
            }
            return sb.ToString();
        }

        // ---- the texts -------------------------------------------------------------------------------------------

        /// <summary>The report's one-line title: the description's first line, cut to 80 characters.</summary>
        public static string Title(string description)
        {
            string first = (description ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
            if (first.Length == 0) return "Issue report";
            return first.Length <= 80 ? first : first.Substring(0, 77).TrimEnd() + "...";
        }

        /// <summary>'text' without the player's home folder (their account's name is in every path under it): "~" in its
        /// place, with either kind of slash.</summary>
        public static string Scrub(string text) => Scrub(text, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        public static string Scrub(string text, string home)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(home) || home.Length < 4) return text ?? "";
            foreach (var form in new[] { home, home.Replace('\\', '/'), home.Replace('/', '\\') }.Distinct())
                text = ReplaceIgnoreCase(text, form, "~");
            return text;
        }

        static string ReplaceIgnoreCase(string text, string what, string with)
        {
            int at = text.IndexOf(what, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return text;
            var sb = new StringBuilder(text.Length);
            int from = 0;
            while (at >= 0)
            {
                sb.Append(text, from, at - from).Append(with);
                from = at + what.Length;
                at = text.IndexOf(what, from, StringComparison.OrdinalIgnoreCase);
            }
            return sb.Append(text, from, text.Length - from).ToString();
        }

        /// <summary>The build and the system. No device name, no unique id.</summary>
        public static string SystemText()
        {
            var sb = new StringBuilder();
            void Line(string name, Func<string> value)
            {
                try { sb.Append(name).Append(": ").Append(value()).Append('\n'); }
                catch (Exception e) { sb.Append(name).Append(": ? (").Append(e.Message).Append(")\n"); }
            }
            Line("Version", () => $"{BuildVersion.Text} ({BuildVersion.Fingerprint}), Unity {Application.unityVersion}");
            Line("Platform", () => $"{Application.platform}, {SystemInfo.operatingSystem}");
            Line("Device", () => $"{SystemInfo.deviceModel} ({SystemInfo.deviceType})");
            Line("CPU", () => $"{SystemInfo.processorType}, {SystemInfo.processorCount} threads, {SystemInfo.systemMemorySize} MB memory");
            Line("GPU", () => $"{SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceVendor}), {SystemInfo.graphicsMemorySize} MB, "
                              + $"{SystemInfo.graphicsDeviceType}, {SystemInfo.graphicsDeviceVersion}");
            Line("Screen", () => $"{Screen.width} x {Screen.height} at {Screen.currentResolution.refreshRateRatio.value:0.#} Hz, {Screen.fullScreenMode}, {Screen.dpi:0} dpi");
            Line("Quality", () => $"{QualitySettings.names[QualitySettings.GetQualityLevel()]}, vsync {QualitySettings.vSyncCount}, frame cap {Application.targetFrameRate}");
            Line("Language", () => $"{Localization.Language} (system: {Application.systemLanguage})");
            Line("Input", () => $"{InputMode.Current}; controllers: "
                                + (UnityEngine.InputSystem.Gamepad.all.Count == 0 ? "none" : string.Join(", ", UnityEngine.InputSystem.Gamepad.all.Select(g => g.displayName))));
            Line("VR", () => Vr.VrMode.Enabled ? (Vr.VrMode.Headset ? "headset" : "simulated") : "off");
            return sb.ToString();
        }

        /// <summary>Where the game is: the scene, the campaign and its step, the rules, the session.</summary>
        public static string GameText()
        {
            var sb = new StringBuilder();
            void Line(string name, Func<string> value)
            {
                try { sb.Append(name).Append(": ").Append(value()).Append('\n'); }
                catch (Exception e) { sb.Append(name).Append(": ? (").Append(e.Message).Append(")\n"); }
            }
            Line("Scene", () => SceneManager.GetActiveScene().name);
            Line("Campaign", () => $"{Session.Campaign}, story step {Story.Index}" + (Session.FreePlay ? ", free play" : ""));
            Line("Rules", () => $"{Session.DifficultyName(Session.Difficulty)}, {Session.EconomyName(Session.Economy)}" + (Session.Hardcore ? ", hardcore" : ""));
            Line("Station", () => $"#{Session.StationIndex}");
            Line("Multiplayer", () => !Multiplayer.NetGame.Active ? "no" : Multiplayer.NetGame.IsServer ? "hosting" : "joined");
            Line("Debug page", () => Cheats.PageShown ? "shown" : "no");
            Line("Mod Builder", () => ModBuilder.TestFlying ? "test flight" : ModBuilder.PlayTesting ? "play test" : "no");
            return sb.ToString();
        }

        /// <summary>Every installed mod, the active ones first in their load order.</summary>
        public static string ModsText()
        {
            var sb = new StringBuilder();
            try
            {
                var active = ModManager.Active.Where(m => !m.IsBase).ToList();
                sb.Append("Active (in load order): ").Append(active.Count).Append('\n');
                foreach (var m in active) AppendMod(sb, m, "on ");
                var others = ModManager.Installed.Where(m => !m.IsBase && !active.Contains(m)).ToList();
                sb.Append("Not active: ").Append(others.Count).Append('\n');
                foreach (var m in others) AppendMod(sb, m, ModManager.IsEnabled(m.Id) ? "on, but: " + ModManager.InactiveReason(m) : "off");
            }
            catch (Exception e) { sb.Append("The mod list failed: ").Append(e.Message).Append('\n'); }
            return sb.ToString();
        }

        static void AppendMod(StringBuilder sb, ModInfo m, string state)
        {
            // The hash reads every file of the mod: only for the active ones, whose hash the game has made already.
            string hash = state.StartsWith("on ", StringComparison.Ordinal) ? "#" + m.Hash : "";
            sb.Append("  [").Append(state.Trim()).Append("] ").Append(m.Id).Append(" \"").Append(m.Name).Append("\" ").Append(m.Version).Append(' ').Append(hash).Append('\n');
            foreach (var e in m.Errors) sb.Append("      error: ").Append(e).Append('\n');
            foreach (var w in m.Warnings) sb.Append("      warning: ").Append(w).Append('\n');
        }

        /// <summary>Every option by its id with its value as the Options page shows it.</summary>
        public static string SettingsText()
        {
            var sb = new StringBuilder();
            List<OptionDef> all;
            try { all = OptionsCatalog.All(); }
            catch (Exception e) { return "The options list failed: " + e.Message + "\n"; }
            foreach (var d in all)
            {
                if (d == null || string.IsNullOrEmpty(d.id) || d.kind == OptionKind.Button || d.kind == OptionKind.Binding) continue;
                string value;
                try
                {
                    if (d.kind == OptionKind.Toggle) value = d.getBool != null ? (d.getBool() ? "on" : "off") : "?";
                    else if (d.kind == OptionKind.Choice)
                    {
                        int i = d.getIndex != null ? d.getIndex() : -1;
                        var names = d.choices?.Invoke();
                        value = names != null && i >= 0 && i < names.Length ? $"{names[i]} ({i})" : i.ToString();
                    }
                    else
                    {
                        float v = d.get != null ? d.get() : 0f;
                        value = d.format != null ? $"{d.format(v)} ({v.ToString("0.###", CultureInfo.InvariantCulture)})" : v.ToString("0.###", CultureInfo.InvariantCulture);
                    }
                }
                catch (Exception e) { value = "? (" + e.Message + ")"; }
                sb.Append(d.page).Append(" / ").Append(d.id).Append(" = ").Append(value).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>report.txt: the description first, then the build, the system and the game.</summary>
        public static string ReportText(Draft draft, DateTime now)
        {
            var sb = new StringBuilder();
            sb.Append("Galaxy on Fire 2 Remake issue report\n");
            sb.Append("Time: ").Append(now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(" (UTC")
              .Append(TimeZoneInfo.Local.GetUtcOffset(now).TotalHours.ToString("+0.#;-0.#", CultureInfo.InvariantCulture)).Append(")\n\n");
            sb.Append("---- What happened ----\n").Append((draft.description ?? "").Trim()).Append("\n\n");
            sb.Append("---- System ----\n").Append(SystemText()).Append('\n');
            sb.Append("---- Game ----\n").Append(GameText());
            if (!string.IsNullOrEmpty(draft.context)) sb.Append(draft.context.TrimEnd()).Append('\n');
            sb.Append("\n---- Mods ----\n").Append(ModsText());
            return Scrub(sb.ToString());
        }

        // ---- the files -------------------------------------------------------------------------------------------

        static Entry TextEntry(string name, string text, string mediaType = "text/plain") =>
            new Entry { name = name, mediaType = mediaType, data = Encoding.UTF8.GetBytes(text ?? "") };

        /// <summary>The last 'max' bytes of a file another process may still be writing (Unity's own log), as text from
        /// its first whole line on; null when there is none.</summary>
        public static string Tail(string path, int max)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                long from = Math.Max(0, f.Length - max);
                f.Seek(from, SeekOrigin.Begin);
                var bytes = new byte[f.Length - from];
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = f.Read(bytes, read, bytes.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                string text = Encoding.UTF8.GetString(bytes, 0, read);
                if (from > 0)
                {
                    int line = text.IndexOf('\n');
                    text = "[... the last " + max / 1024 + " KB]\n" + (line >= 0 ? text.Substring(line + 1) : text);
                }
                return text;
            }
            catch (Exception e) { return "(couldn't read " + Path.GetFileName(path) + ": " + e.Message + ")\n"; }
        }

        /// <summary>The report's files (see the header). Each part that fails leaves its file out, or says so inside it:
        /// a report must come out of a game that is already in trouble.</summary>
        public static List<Entry> Collect(Draft draft, DateTime now)
        {
            draft ??= new Draft();
            var files = new List<Entry>();
            void Add(Func<Entry> make)
            {
                try { var e = make(); if (e != null && e.data != null) files.Add(e); }
                catch (Exception e) { Debug.LogWarning("IssueReport: a part was left out (" + e.Message + ")"); }
            }
            Add(() => TextEntry("report.txt", ReportText(draft, now)));
            Add(() => draft.screenshot == null || draft.screenshot.Length == 0 ? null
                    : new Entry { name = "screenshot.jpg", mediaType = "image/jpeg", data = draft.screenshot });
            Add(() => TextEntry("log.txt", Scrub(LogText())));
            // Unity's own log files: the player's in a build (none on a phone); the Editor's is the whole Editor's, left out.
            if (!Application.isEditor)
            {
                string playerLog = Application.consoleLogPath;
                Add(() => { string t = Tail(playerLog, PlayerLogTail); return t == null ? null : TextEntry("Player.log", Scrub(t)); });
                Add(() =>
                {
                    if (string.IsNullOrEmpty(playerLog)) return null;
                    string prev = Path.Combine(Path.GetDirectoryName(playerLog) ?? "", Path.GetFileNameWithoutExtension(playerLog) + "-prev" + Path.GetExtension(playerLog));
                    string t = Tail(prev, PreviousLogTail);
                    return t == null ? null : TextEntry("Player-prev.log", Scrub(t));
                });
            }
            Add(() => TextEntry("settings.txt", Scrub(SettingsText())));
            if (draft.includeSave) Add(() => TextEntry("save.json", SaveGame.SnapshotJson(), "application/json"));
            return files;
        }

        /// <summary>The files as one zip in the IssueReports folder, "GoF2_issue_yyyyMMdd_HHmmss.zip"; its path, null when it
        /// couldn't be written. The folder keeps its newest KeepReports.</summary>
        public static string WriteZip(List<Entry> files, DateTime now)
        {
            if (files == null || files.Count == 0) return null;
            try
            {
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder, $"GoF2_issue_{now:yyyyMMdd_HHmmss}.zip");
                for (int n = 2; File.Exists(path); n++) path = Path.Combine(Folder, $"GoF2_issue_{now:yyyyMMdd_HHmmss}_{n}.zip");
                using (var stream = new FileStream(path, FileMode.CreateNew))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                    foreach (var f in files)
                    {
                        // A picture is compressed already.
                        var entry = zip.CreateEntry(f.name, f.name.EndsWith(".jpg", StringComparison.Ordinal) ? System.IO.Compression.CompressionLevel.NoCompression
                                                                                                         : System.IO.Compression.CompressionLevel.Optimal);
                        using var s = entry.Open();
                        s.Write(f.data, 0, f.data.Length);
                    }
                Prune();
                return path;
            }
            catch (Exception e)
            {
                Debug.LogWarning("IssueReport: the report couldn't be saved (" + e.Message + ")");
                return null;
            }
        }

        static void Prune()
        {
            try
            {
                var old = new DirectoryInfo(Folder).GetFiles("GoF2_issue_*.zip");
                Array.Sort(old, (a, b) => string.CompareOrdinal(b.Name, a.Name));   // the name carries the time: newest first
                for (int i = KeepReports; i < old.Length; i++) old[i].Delete();
            }
            catch (Exception e) { Debug.LogWarning("IssueReport: old reports not cleared (" + e.Message + ")"); }
        }

        // ---- GitHub ----------------------------------------------------------------------------------------------

        /// <summary>A new GitHub issue's address with the title and a body filled in: the description, the build and system
        /// in short, the active mods, and a line asking for the zip. The description is cut (to IssueBodyText, and
        /// further while the address would pass MaxIssueUrl: other scripts take up to nine characters a letter in it).</summary>
        public static string IssueUrl(string description, string zipName)
        {
            string text = (description ?? "").Trim();
            if (text.Length > IssueBodyText) text = text.Substring(0, IssueBodyText).TrimEnd() + " [...]";
            var tail = new StringBuilder();
            tail.Append("\n\n---\n");
            tail.Append("Version: ").Append(BuildVersion.Text).Append('\n');
            tail.Append("Platform: ").Append(Application.platform).Append(", ").Append(SystemInfo.operatingSystem).Append('\n');
            tail.Append("GPU: ").Append(SystemInfo.graphicsDeviceName).Append(" (").Append(SystemInfo.graphicsDeviceType).Append(")\n");
            string mods;
            try { mods = string.Join(", ", ModManager.UserMods.Select(m => m.Id + " " + m.Version)); }
            catch (Exception) { mods = "?"; }
            if (mods.Length > 600) mods = mods.Substring(0, 600) + " [...]";
            tail.Append("Mods: ").Append(mods.Length > 0 ? mods : "none").Append('\n');
            if (!string.IsNullOrEmpty(zipName)) tail.Append("\nPlease attach the report file here: ").Append(zipName).Append('\n');
            string start = IssuesPage + "?title=" + Uri.EscapeDataString(Title(description)) + "&body=", end = Uri.EscapeDataString(tail.ToString());
            string body = Uri.EscapeDataString(text);
            while (start.Length + body.Length + end.Length > MaxIssueUrl && text.Length > 0)
            {
                text = text.Substring(0, text.Length * 3 / 4).TrimEnd();
                body = Uri.EscapeDataString(text + " [...]");
            }
            return start + body + end;
        }
    }
}
