// EventRunnerFiles.cs
// Part of EventRunner: finding and loading the graph files (Events folders, mods, built-ins), the /event command,
// the Editor's checks, and starting a run.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GoF2Remake.Data;
using UnityEngine;
using GoF2Remake.Multiplayer;

namespace GoF2Remake.Events
{
    public static partial class EventRunner
    {
        // ---- files --------------------------------------------------------------------------------------------

        static IEnumerable<string> Folders()
        {
            yield return Path.Combine(GoF2Remake.GamePaths.Data, "Events");
            string exeDir = Path.GetDirectoryName(Application.dataPath);
            if (!string.IsNullOrEmpty(exeDir)) yield return Path.Combine(exeDir, "Events");
        }

        /// <summary>The event graph &lt;name&gt;.gof2event compiled (EventGraphScript): from an Events folder, else a mod's, else a
        /// built-in one (Resources/GoF2Events). The old .gof2netevent files are read too. error: why the graph can't run.</summary>
        static string Load(string name, out string from, out string error)
        {
            from = error = null;
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains("..")) return null;
            foreach (var dir in Folders())
            {
                foreach (string ext in Extensions)
                {
                    string path = Path.Combine(dir, name + "." + ext);
                    try { if (File.Exists(path)) { from = path; return Script(File.ReadAllText(path), out error); } }
                    catch (Exception e) { Debug.LogWarning($"EventRunner: {path}: {e.Message}"); }
                }
            }
            // The mods that are on (in a session: the session's), the later one in the load order winning.
            var mods = Modding.ModManager.Active;
            for (int m = mods.Count - 1; m >= 0; m--)
            {
                foreach (string ext in Extensions)
                {
                    string modText;
                    try { modText = mods[m].Source.ReadText("events/" + name + "." + ext); }
                    catch (Exception e) when (Modding.ModFileException.IsFileError(e)) { Debug.LogWarning($"EventRunner: {mods[m].Id}: {e.Message}"); continue; }
                    if (modText != null) { from = mods[m].Manifest.id; return Script(modText, out error); }
                }
            }
            var asset = Resources.Load<TextAsset>(BuiltIn + "/" + name);
            if (asset != null) { from = X("mpEventBuiltIn", "built in"); return Script(asset.text, out error); }
            return null;
        }

        /// <summary>An event graph file compiled; error: why it can't run.</summary>
        static string Script(string text, out string error)
        {
            error = null;
            if (QuestFile.Is(text))
            {
                // A quest made in the Mod Builder: its steps as the script (QuestFile).
                var issues = new List<string>();
                string quest = QuestFile.Compile(text, issues);
                if (issues.Count > 0) error = issues[0];
                return quest;
            }
            if (!EventGraphScript.IsGraph(text)) { error = X("mpEventNotGraph", "not an event graph."); return ""; }
            var problems = new List<EventGraphScript.Problem>();
            string script = EventGraphScript.FromFile(text, problems);
            foreach (var p in problems)
                if (!p.warning) { error = (p.node != null ? p.node + ": " : "") + p.message; break; }
            return script;
        }

        /// <summary>Checks a script (the line form) with the runner's own parser: why it can't run, or null.</summary>
        public static string CheckScript(string text)
        {
            var steps = Compile(text ?? "", out string error);
            return steps == null ? error ?? "the script can't be read." : null;
        }

        /// <summary>Every event / quest name the game finds (the Events folders, the mods that are on, the built-ins).</summary>
        public static List<string> List()
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in Folders())
            {
                try
                {
                    if (Directory.Exists(dir))
                        foreach (string ext in Extensions)
                            foreach (var f in Directory.GetFiles(dir, "*." + ext)) names.Add(Path.GetFileNameWithoutExtension(f));
                }
                catch (Exception) { }
            }
            foreach (var mod in Modding.ModManager.Active)
                foreach (string f in mod.Source.FilesIn("events", "." + GraphExtension, "." + LegacyExtension, "." + QuestFile.Extension, "." + QuestFile.LegacyExtension))
                    names.Add(Path.GetFileNameWithoutExtension(f));
            foreach (var a in Resources.LoadAll<TextAsset>(BuiltIn)) names.Add(a.name);
            return new List<string>(names);
        }

        // ---- the command (server) ----------------------------------------------------------------------------

        /// <summary>/event &lt;name | stop | list&gt;: the issuer's answer.</summary>
        public static string Command(string args, IPilot by)
        {
            args = (args ?? "").Trim();
            if (args.Length == 0 || args.Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                var names = List();
                var shown = new List<string>();
                foreach (string n in names)
                {
                    string t = Load(n, out _, out string e);
                    var set = e == null && t != null ? Settings(t) : null;
                    shown.Add(set == null || set.Count == 0 ? n : n + " [" + string.Join(" ", set.ConvertAll(x => x.name + "=" + x.value)) + "]");
                }
                string list = names.Count == 0 ? X("mpEventNone", "No event graphs found.") : string.Join(", ", shown);
                string folder = Path.Combine(GoF2Remake.GamePaths.Data, "Events");
                return string.Format(X("mpEventList", "Events: {0}{1}Event graphs (.gof2event) go in {2} (or an Events folder next to the game)."), list,
                    Running ? string.Format(X("mpEventRunningNow", " (running: {0})"), Global.running) + " " : " ", folder);
            }
            if (args.Equals("stop", StringComparison.OrdinalIgnoreCase))
            {
                if (!Running) return X("mpEventNotRunning", "No event is running.");
                var global = Global;
                string was = global.running;
                global.End();
                Debug.Log($"Server: {NetCommands.IssuerName(by)} stopped the event {was}");
                EventHost.NoticeAll(string.Format(X("mpEventStopped", "The event {0} was stopped."), was));
                return "";
            }
            if (Running) return string.Format(X("mpEventBusy", "The event {0} is running: /event stop first."), Global.running);
            var words = args.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string name = words[0];
            string text = Load(name, out string from, out string graphError);
            if (text == null) return string.Format(X("mpEventMissing", "No event \"{0}\". /event list shows them."), name);
            if (graphError != null) return string.Format(X("mpEventError", "{0}: {1}"), name, graphError);
            // Settings: "name=value" after the event's name.
            var given = new Dictionary<string, double>();
            for (int i = 1; i < words.Length; i++)
            {
                int eq = words[i].IndexOf('=');
                if (eq <= 0 || !double.TryParse(words[i].Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                    return string.Format(X("mpEventSettingForm", "Settings are name=number: \"{0}\"."), words[i]);
                given[words[i].Substring(0, eq).ToLowerInvariant()] = v;
            }
            return Start(name, text, from, by, given);
        }

        /// <summary>The script's first error ("line N: ..."), or null when it compiles (the Editor's event graphs).</summary>
        public static string Check(string text)
        {
            Compile(text, out string error);
            return error;
        }

        /// <summary>Runs a script given as text (the Editor's event graph window, in a hosted session's Play mode).</summary>
        public static string StartText(string name, string text)
        {
            if (!EventHost.ServerReady) return "Host a multiplayer session first.";
            if (Running) return string.Format(X("mpEventBusy", "The event {0} is running: /event stop first."), Global.running);
            return Start(name, text, "Editor", null, new Dictionary<string, double>());
        }

        /// <summary>The event's settings (its "param" lines): name and default expression (the Editor, /event list).</summary>
        public static List<(string name, string value)> Settings(string text)
        {
            var list = new List<(string, string)>();
            var compiled = Compile(text, out _);
            if (compiled != null) foreach (var s in compiled) if (s.op == Op.Param) list.Add((s.a, s.b));
            return list;
        }

        /// <summary>A new run of the event: a global one (team null), or a bar mission's for its team, taken at 'station'.</summary>
        static string Start(string name, string text, string from, IPilot by, Dictionary<string, double> given, HashSet<ulong> team = null, int station = -1,
                            GraphQuestState record = null, string resumeAt = null)
        {
            var compiled = Compile(text, out string error);
            if (compiled == null) return string.Format(X("mpEventError", "{0}: {1}"), name, error);
            var known = new List<string>();
            foreach (var s in compiled) if (s.op == Op.Param) known.Add(s.a);
            foreach (var key in given.Keys)
                if (!known.Contains(key))
                    return known.Count == 0 ? string.Format(X("mpEventNoSettings", "{0} has no settings."), name)
                        : string.Format(X("mpEventUnknownSetting", "{0} has no setting \"{1}\" (settings: {2})."), name, key, string.Join(", ", known));
            if (runs.Count >= MaxRuns) return X("mpEventTooMany", "Too many events and missions are running.");
            var info = Info(compiled);
            info.name = name;
            var run = new EventRun(name, compiled, info, team, station, by, given) { record = record };
            if (record != null && record.kind == GraphQuestState.KindQuest) run.SetVar("autostop", 0);   // a quest lasts
            if (resumeAt != null) run.Resume(resumeAt);
            runs.Add(run);
            LocalEvents.Ensure();   // single player: its runner ticks the runs
            Debug.Log($"Server: {NetCommands.IssuerName(by)} started the {(team != null ? "mission" : "event")} {name} ({from}, {compiled.Count} steps"
                      + (team != null ? $", players {string.Join(" ", team)}" : "") + ")");
            return string.Format(X("mpEventStarted", "Event {0} started."), name);
        }

        /// <summary>A compiled event's mission details (its "mission" lines; no title: not a mission).</summary>
        static MissionInfo Info(List<Step> compiled)
        {
            var info = new MissionInfo();
            foreach (var s in compiled)
            {
                if (s.op != Op.Mission) continue;
                string v = (s.b ?? "").Trim();
                switch (s.a)
                {
                    case "title": info.title = v; break;
                    case "offer": info.offer = v.Replace("\\n", "\n"); break;
                    case "client": info.client = v; break;
                    case "reward": info.reward = v; break;
                    case "players":
                    {
                        var f = v.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (f.Length > 0 && int.TryParse(f[0], out int lo)) info.minPlayers = Mathf.Clamp(lo, 1, 16);
                        info.maxPlayers = f.Length > 1 && int.TryParse(f[1], out int hi) ? Mathf.Clamp(hi, info.minPlayers, 16) : Mathf.Max(info.minPlayers, info.maxPlayers);
                        break;
                    }
                    case "kind":
                        info.kind = v.Equals("quest", StringComparison.OrdinalIgnoreCase) ? 2 : v.Equals("mission", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                        break;
                    case "starts": info.starts = v; break;
                    case "once": info.once = v.Length == 0 || v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                    case "repeat":
                        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double rm)) info.repeatMinutes = Mathf.Max(0f, (float)rm);
                        break;
                    case "takers":
                        info.takers = v.Equals("station", StringComparison.OrdinalIgnoreCase) ? EventMissionTakers.OnePerStation
                                    : v.Equals("any", StringComparison.OrdinalIgnoreCase) ? EventMissionTakers.Anyone : EventMissionTakers.OneInUniverse;
                        break;
                    case "chance":
                        if (double.TryParse(v.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out double pc)) info.chance = Mathf.Clamp((int)Math.Round(pc), 0, 100);
                        break;
                    case "stations":
                        foreach (string part in v.Split(','))
                            if (part.Trim().Length > 0 && NetTeleport.ParseStation(part.Trim(), out int st, out string left) && left.Length == 0 && st >= 0) info.stations.Add(st);
                        break;
                }
            }
            return info;
        }
    }
}
