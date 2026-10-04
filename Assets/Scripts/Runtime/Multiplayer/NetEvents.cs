// NetEvents.cs
// Remake multiplayer: events (game modes like waves of enemies), run on the server by an admin's /event <name> or the
// dedicated server's console ("event <name>"); one event at a time, "/event stop" ends it, "/event list" lists them.
// An event is a node graph made in the Editor: <name>.gof2netevent in an Events folder (the game's data folder,
// Application.persistentDataPath/Events, or next to the game / dedicated server's executable), else a built-in one
// (Resources/GoF2Net/Events: waves, survival). EventGraphFile reads the file, EventGraphScript compiles the graph into the
// steps' internal script form below, which runs here (it isn't a file format: there are no script files).
// Line by line, "#" starts a comment:
//   <command> [args]              any server command without the "/" (spawn, title, timer, heal, give, tp, g...), run as the
//                                 server (every right, named "Server"); {expression} parts are filled in first ("{wave*2}")
//   wait <seconds>                pauses the script (an expression)
//   wait until <condition> [timeout <seconds>]   pauses until the condition holds (checked 4 times a second)
//   if <condition> / else / end   runs a block or the other
//   while <condition> / end       repeats a block while the condition holds
//   repeat <count> [as <var>] / end   repeats a block; the variable counts 1, 2, 3...
//   set <var> [=] <expression>    a variable of the script
//   stop                          ends the event
//   score <kills | time | points> how the event's winner is decided (default time): the most event ships destroyed, the
//                                 longest alive in space since the fight began (the first spawn; a death ends a player's time),
//                                 or the most points
//   winner [kills | time | points]   "The winner is X" on everyone's screen (with the score) and in the chat
//   parallel / end                runs the block at the same time as what follows (its own thread; 16 at most)
//   every <seconds> / end         a thread that waits the seconds, runs the block, again and again until the event ends
//   param <var> = <expression>    a setting: the value given when starting ("/event waves waves=10"), else the expression
//   points <players> <amount>     adds points to each of those players (a selector, NetCommands.FindTargets)
//   scoreboard on [title] / off   the scoreboard on every player's screen (the players by the score mode, top 10)
//   on <trigger> [station] / end  a handler: each time the trigger happens its block runs as a new thread, with the player it
//                                 is about as "@trigger" (a selector) and "%trigger%" (the name in texts). Triggers: died
//                                 (destroyed in space), docked, launched, joined, left (the session), entered (an orbit, also
//                                 after a launch or jump), respawned (back in space after being destroyed), kill (an event
//                                 ship destroyed; the player = its killer), pvpkill (a player destroyed another: the player =
//                                 the killer, "%victim%" the other's name), cleared (every enemy the event spawned is gone;
//                                 no player). The station limits a player trigger to
//                                 that orbit or station. Handlers start working when the flow passes them (the graph puts them
//                                 first) and last until the event ends. A free for all (/pvp), the respawn points (/respawn)
//                                 and the travel restrictions (/restrict) the event set end with it.
//   ask <players> <seconds> | <question> | <answer 1> | <answer 2> [| 3 | 4]   then "answer <k>" / end blocks, then end:
//                                 the question on those players' screens (NetScreen; "<speaker> : <question>" shows a speaker as
//                                 /dialog takes them: a story character, a race and a name, player); each answer runs its block as a thread
//                                 with that player as @trigger / %trigger%; the flow waits until all have answered (or skipped,
//                                 or left) or the seconds ran out (0 = no limit)
//   vote <players> <seconds> | <question> | <answer 1> | <answer 2> [| 3 | 4]   then "choice <k>" / end blocks, then end:
//                                 the same question, but the flow waits, then runs the block of the answer most picked (a tie:
//                                 one of them at random; no answers: "choice 0") and goes on after the vote
//   startevent <name> [setting=value ...]   ends this event and starts that one (an Events folder's, else built in) with
//                                 the settings ({expression} parts filled in); its starter (and a mission's players) this one's
//   mission <key> <value>         a bar mission's details (the Start node's Mission settings; not run): title, offer (the
//                                 visitor's offer text, "\n" new lines), client (the speaker, as /dialog's), reward (credits,
//                                 an expression), players <min> <max>, stations (offered at: numbers / names, commas; none =
//                                 every station). An event with a title is offered in the multiplayer Space Lounges
//                                 (NetEventMissions) and runs for the players who took it
//   complete [reward] [| title]   pays the reward (an expression, else the mission's) split evenly across the run's players
//                                 (the reward box, title default "Mission accomplished!"), then ends the run
//   fail [title]                  the title (default "Mission failed!") on the run's players' screens, then ends the run
// Several runs side by side: one global event (/event: everyone) and any number of bar missions, each with its own players
// (its team): a mission's selectors (@a, @alive, @r..., NetCommands.Scope), counts, triggers, scoreboard and notices only
// cover its team; @team names them (everyone in a global event). A mission whose players are all gone from its ships'
// orbits fails; one whose players left the session ends.
// The event ends when its main flow ends (or at stop), its threads with it. A line may end in "#@<id>": the graph node it
// came from (the Editor's live view, ActiveSteps).
// Expressions: numbers, variables, + - * / %, ( ), == != < <= > >=, and, or, not, random(a, b) (whole numbers a..b),
// min(a, b), max(a, b), floor(x), count(<players>) (how many players a selector finds: "count(@alive[orbit=78])"), and the
// state:
//   enemies   this event's living ships spawned as enemies (a just-sent spawn counts until its ships show up, 6 s at most)
//   ships     all this event's living spawned ships
//   players   the players in the session; inspace / docked / dead: in space alive, docked, destroyed in space
//   time      seconds since the event started
//   toppoints the most points any player has
//   missionstation  a bar mission's station (where it was taken; -1 in an /event)
// Ending by itself: once it has spawned ships, an event ends when no player has been alive in space in its ships' orbits
// for 3 s (all destroyed, docked or gone): "Event over" with the winner on everyone's screen. "set autostop = 0" turns
// that off.
// The ships an event spawns carry its batch tag (SpawnSpec.eventTag, written on their NetProxy): the server sees every
// NPC of every player's game as a NetProxy, so it can count them. Ticked by NetState.Update on the server.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetEvents
    {
        const float CheckSeconds = 0.25f, PendingSeconds = 6f, EmptySeconds = 3f;
        const int MaxOpsPerTick = 500, MaxLines = 4000, MaxThreads = 16, ScoreboardRows = 10, MaxRuns = 32;

        enum Op { Command, Wait, WaitUntil, Set, JumpIfFalse, Jump, Stop, Score, Winner, Fork, EndThread, Param, Points, Scoreboard, Handler, Ask, Vote, StartEvent,
                   Mission, Complete, MissionFail }

        enum ScoreMode { Time, Kills, Points }

        sealed class Step
        {
            public Op op;
            public string a, b;     // command line / expression / variable / selector; second expression (timeout, value)
            public int target;      // jumps, a fork's continuation
            public int line;
            public string node;     // the graph node (the "#@" marker)
            public string[] parts;  // ask: the question and the answers
            public int[] bodies;    // ask: where each answer's block starts (-1: none)
        }

        /// <summary>A question out on players' screens: who still owes an answer, where each answer's block starts, until when.</summary>
        sealed class PendingAsk
        {
            public int id;
            public bool vote;            // a vote: no block per answer, the most picked one's afterwards
            public readonly int[] counts = new int[4];
            public int[] bodies;
            public readonly HashSet<ulong> waiting = new HashSet<ulong>();
            public float deadline;
        }

        /// <summary>One flow of the event: the main one, or a parallel / every block's.</summary>
        sealed class Thread
        {
            public int pc;
            public float waitStart, waitUntil = -1f, untilDeadline = -1f, checkTimer;
            public bool done;
            public int askWait;   // waiting for this question's answers
            public int[] voteBodies;  // a vote's blocks, run once it is decided
            public int voteEnd;
            public ulong subject = ulong.MaxValue;   // an "on" handler's player
            public string subjectName = "", otherName = "";   // otherName: pvpkill's victim
        }

        /// <summary>An "on" block: its trigger, the station it is limited to (-1 any), where its block starts.</summary>
        sealed class Handler
        {
            public string trigger;
            public int station = -1, body;
            public Vector3 point;    // near: the spot (Unity space in its orbit) and the radius in metres
            public float radius;
            public readonly HashSet<ulong> inside = new HashSet<ulong>();
        }

        struct PlayerState
        {
            public bool inSpace, dead, docked;
            public int station;
            public string name;
        }

        sealed class Batch
        {
            public int expected;
            public bool enemy;
            public float start;
            public readonly HashSet<long> seen = new HashSet<long>();
        }


        static readonly string[] Triggers = { "died", "docked", "launched", "joined", "left", "entered", "kill", "cleared", "pvpkill", "respawned", "near" };
        static readonly string[] MissionKeys = { "title", "offer", "client", "reward", "players", "stations" };

        /// <summary>A bar mission's details: the Start node's Mission settings (the "mission &lt;key&gt; &lt;value&gt;" lines).</summary>
        public sealed class MissionInfo
        {
            public string name = "", title = "", offer = "", client = "", reward = "";
            public readonly List<int> stations = new List<int>();   // offered at (none: at every station)
            public int minPlayers = 1, maxPlayers = 4;
            public bool IsMission => title.Length > 0;
        }

        // Several runs side by side: at most one global event (/event, the console, the Editor: everyone) and the bar missions
        // (EventRun.team: the players who took it). Questions and spawn batches are numbered across all of them.
        static readonly List<EventRun> runs = new List<EventRun>();
        static EventRun ticking;     // the run being ticked now (@survivors)
        static EventRun executing;   // the run whose command line runs now (NewBatch, the Note* calls)
        static int nextAsk = 1, nextTag = 1;
        static int switches;          // startevent this second (an event switching to itself without a wait)
        static float switchSecond;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Reset();

        /// <summary>A new session, or the server going: nothing runs.</summary>
        internal static void Reset()
        {
            foreach (var r in runs.ToArray()) r.End();
            runs.Clear();
            ticking = executing = null;
            NetCommands.Scope = null;
        }

        static string X(string key, string english) => Localization.Extra(key, english);

        /// <summary>The global event (null: none).</summary>
        static EventRun Global => runs.Find(r => r.team == null);

        /// <summary>A global event runs (/event).</summary>
        public static bool Running => Global != null;

        /// <summary>The run the Editor's live view shows: the global event, else the first mission.</summary>
        static EventRun Shown => Global ?? (runs.Count > 0 ? runs[0] : null);

        /// <summary>The shown run's event name (null when none): the Editor's live view matches its graph by it.</summary>
        public static string RunningName => Shown?.running;

        /// <summary>The Editor's live view: each flow's current step's graph node and how far its wait has gone (0..1, or
        /// -1 when not waiting).</summary>
        public static List<(string node, float progress)> ActiveSteps() => Shown?.ActiveSteps() ?? new List<(string, float)>();

        /// <summary>The Editor's live view: the event's variables (not the hidden "_" ones).</summary>
        public static Dictionary<string, double> Variables() => Shown?.Variables() ?? new Dictionary<string, double>();

        /// <summary>The Editor's live view: the players' points by name.</summary>
        public static Dictionary<string, double> PlayerPoints() => Shown?.PlayerPoints() ?? new Dictionary<string, double>();

        /// <summary>/pvp, /respawn, /restrict and /waypoint while an event's line runs them: undone when that event ends.</summary>
        internal static void NoteFreeForAll(bool on) { if (executing != null) executing.eventPvp = on; }
        internal static void NoteRespawnSet() { if (executing != null) executing.eventRespawn = true; }
        internal static void NoteRulesSet() { if (executing != null) executing.eventRules = true; }
        internal static void NoteWaypointSet() { if (executing != null) executing.eventWaypoint = true; }

        /// <summary>NetAdmin.Spawn, while a script's spawn line runs: a new batch tag of that run for 'count' ships to one
        /// player (0 when no event is running the command).</summary>
        internal static int NewBatch(int count, bool enemy) => executing != null ? executing.NewBatch(count, enemy) : 0;

        /// <summary>@survivors: a player alive in space who has been in the event's fight and never destroyed in it (the run
        /// being ticked; a command typed in the chat: the global event's).</summary>
        public static bool Survived(NetPlayer p)
        {
            var r = ticking ?? Global;
            return r != null && r.Survived(p);
        }

        /// <summary>A player's mission run (null: none).</summary>
        static EventRun MissionOf(ulong client) => runs.Find(r => r.team != null && !r.ended && r.team.Contains(client));

        /// <summary>The player is on a bar mission of an event graph.</summary>
        public static bool InMission(ulong client) => MissionOf(client) != null;

        /// <summary>Server, NetState.Update: every run, its selectors limited to its players while it runs.</summary>
        internal static void Tick()
        {
            if (runs.Count == 0) return;
            if (!NetGame.Active || NetState.Instance == null || !NetState.Instance.IsServer) { Reset(); return; }
            foreach (var r in runs.ToArray())
            {
                if (r.ended) continue;
                ticking = r;
                NetCommands.Scope = r.team != null ? r.InScope : (Func<NetPlayer, bool>)null;
                try { r.Tick(); }
                finally { ticking = null; NetCommands.Scope = null; }
            }
        }

        /// <summary>NetState.AnswerRpc (server): a player's answer (0 = skipped) to the run that asked.</summary>
        internal static void OnAnswer(ulong client, int id, int choice)
        {
            foreach (var r in runs)
                if (r.HasAsk(id)) { r.OnAnswer(client, id, choice); return; }
        }

        /// <summary>NetState.DestroyedByRpc (the server checked the kill): "on pvpkill" in every run the killer is in.</summary>
        internal static void OnPlayerKilled(NetPlayer killer, NetPlayer victim)
        {
            foreach (var r in runs.ToArray()) r.OnPlayerKilled(killer, victim);
        }

        // ---- bar missions (NetEventMissions) ----

        static List<MissionInfo> missionCache;
        static float missionCacheTime = -100f;

        /// <summary>Server: every event graph with mission details (its Start node's Mission title), read again after 30 s.</summary>
        internal static List<MissionInfo> MissionGraphs()
        {
            if (missionCache != null && Time.unscaledTime - missionCacheTime < 30f) return missionCache;
            missionCache = new List<MissionInfo>();
            missionCacheTime = Time.unscaledTime;
            foreach (string name in List())
            {
                string text = Load(name, out _, out string error);
                if (text == null || error != null) continue;
                var compiled = Compile(text, out _);
                if (compiled == null) continue;
                var info = Info(compiled);
                info.name = name;
                if (info.IsMission) missionCache.Add(info);
            }
            return missionCache;
        }

        /// <summary>Server: the mission event 'name' runs for 'team' (taken at 'station' by 'by'); the answer when it can't.</summary>
        internal static string StartMission(string name, HashSet<ulong> team, int station, NetPlayer by)
        {
            foreach (ulong id in team)
                if (MissionOf(id) != null) return X("mpEventMissionBusy", "Someone in your squad is already on a mission like this.");
            string text = Load(name, out string from, out string error);
            if (text == null) return string.Format(X("mpEventMissing", "No event \"{0}\". /event list shows them."), name);
            if (error != null) return string.Format(X("mpEventError", "{0}: {1}"), name, error);
            string answer = Start(name, text, from, by, new Dictionary<string, double>(), team, station);
            return MissionOf(by != null ? by.OwnerClientId : ulong.MaxValue) != null ? null : answer;
        }

        /// <summary>Server: a team member's run (the mission's name and info) for a player joining late / asking again.</summary>
        internal static MissionInfo MissionInfoOf(ulong client) => MissionOf(client)?.info;

        // ---- files --------------------------------------------------------------------------------------------

        static IEnumerable<string> Folders()
        {
            yield return Path.Combine(Application.persistentDataPath, "Events");
            string exeDir = Path.GetDirectoryName(Application.dataPath);
            if (!string.IsNullOrEmpty(exeDir)) yield return Path.Combine(exeDir, "Events");
        }

        /// <summary>The event graph &lt;name&gt;.gof2netevent compiled (EventGraphScript): from an Events folder, else a built-in
        /// one (Resources/GoF2Net/Events). error: why the graph can't run.</summary>
        static string Load(string name, out string from, out string error)
        {
            from = error = null;
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains("..")) return null;
            foreach (var dir in Folders())
            {
                string path = Path.Combine(dir, name + "." + GraphExtension);
                try { if (File.Exists(path)) { from = path; return Script(File.ReadAllText(path), out error); } }
                catch (Exception e) { Debug.LogWarning($"NetEvents: {path}: {e.Message}"); }
            }
            var asset = Resources.Load<TextAsset>(BuiltIn + "/" + name);
            if (asset != null) { from = X("mpEventBuiltIn", "built in"); return Script(asset.text, out error); }
            return null;
        }

        public const string GraphExtension = "gof2netevent";
        /// <summary>The built-in events' Resources folder.</summary>
        const string BuiltIn = "GoF2Net/Events";

        /// <summary>An event graph file compiled; error: why it can't run.</summary>
        static string Script(string text, out string error)
        {
            error = null;
            if (!EventGraphScript.IsGraph(text)) { error = X("mpEventNotGraph", "not an event graph."); return ""; }
            var problems = new List<EventGraphScript.Problem>();
            string script = EventGraphScript.FromFile(text, problems);
            foreach (var p in problems)
                if (!p.warning) { error = (p.node != null ? p.node + ": " : "") + p.message; break; }
            return script;
        }

        static List<string> List()
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in Folders())
            {
                try { if (Directory.Exists(dir)) foreach (var f in Directory.GetFiles(dir, "*." + GraphExtension)) names.Add(Path.GetFileNameWithoutExtension(f)); }
                catch (Exception) { }
            }
            foreach (var a in Resources.LoadAll<TextAsset>(BuiltIn)) names.Add(a.name);
            return new List<string>(names);
        }

        // ---- the command (server) ----------------------------------------------------------------------------

        /// <summary>/event &lt;name | stop | list&gt;: the issuer's answer.</summary>
        public static string Command(string args, NetPlayer by)
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
                string folder = Path.Combine(Application.persistentDataPath, "Events");
                return string.Format(X("mpEventList", "Events: {0}{1}Event graphs (.gof2netevent) go in {2} (or an Events folder next to the game)."), list,
                    Running ? string.Format(X("mpEventRunningNow", " (running: {0})"), Global.running) + " " : " ", folder);
            }
            if (args.Equals("stop", StringComparison.OrdinalIgnoreCase))
            {
                if (!Running) return X("mpEventNotRunning", "No event is running.");
                var global = Global;
                string was = global.running;
                global.End();
                Debug.Log($"Server: {NetCommands.IssuerName(by)} stopped the event {was}");
                NetState.Instance?.NoticeAll(string.Format(X("mpEventStopped", "The event {0} was stopped."), was));
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
            if (!NetGame.Active || NetState.Instance == null || !NetState.Instance.IsServer) return "Host a multiplayer session first.";
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
        static string Start(string name, string text, string from, NetPlayer by, Dictionary<string, double> given, HashSet<ulong> team = null, int station = -1)
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
            runs.Add(new EventRun(name, compiled, info, team, station, by, given));
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
                    case "stations":
                        foreach (string part in v.Split(','))
                            if (part.Trim().Length > 0 && NetTeleport.ParseStation(part.Trim(), out int st, out string left) && left.Length == 0 && st >= 0) info.stations.Add(st);
                        break;
                }
            }
            return info;
        }

        // ---- compiling ---------------------------------------------------------------------------------------

        static List<Step> Compile(string text, out string error)
        {
            error = null;
            var steps = new List<Step>();
            var open = new Stack<(string kind, int at, int line, string var)>();
            var lines = text.Replace("\r", "").Split('\n');
            if (lines.Length > MaxLines) { error = X("mpEventTooLong", "the event is too long."); return null; }
            int hidden = 0;
            for (int n = 0; n < lines.Length; n++)
            {
                string raw = lines[n];
                int hash = raw.IndexOf('#');
                string line = (hash >= 0 ? raw.Substring(0, hash) : raw).Trim();
                if (line.Length == 0) continue;
                string marker = hash >= 0 && hash + 1 < raw.Length && raw[hash + 1] == '@' ? raw.Substring(hash + 2).Trim() : null;
                int first = steps.Count;
                if (line.StartsWith("/")) line = line.Substring(1);
                int sp = line.IndexOf(' ');
                string word = (sp < 0 ? line : line.Substring(0, sp)).ToLowerInvariant();
                string rest = sp < 0 ? "" : line.Substring(sp + 1).Trim();
                int ln = n + 1;
                string Err(string what) => string.Format(X("mpEventLine", "line {0}: {1}"), ln, what);
                switch (word)
                {
                    case "wait":
                        if (rest.StartsWith("until ", StringComparison.OrdinalIgnoreCase))
                        {
                            string cond = rest.Substring(6).Trim(), timeout = null;
                            int t = cond.LastIndexOf(" timeout ", StringComparison.OrdinalIgnoreCase);
                            if (t >= 0) { timeout = cond.Substring(t + 9).Trim(); cond = cond.Substring(0, t).Trim(); }
                            if (cond.Length == 0) { error = Err(X("mpEventNoCondition", "a condition is missing.")); return null; }
                            steps.Add(new Step { op = Op.WaitUntil, a = cond, b = timeout, line = ln });
                        }
                        else
                        {
                            if (rest.Length == 0) { error = Err(X("mpEventNoSeconds", "wait needs seconds.")); return null; }
                            steps.Add(new Step { op = Op.Wait, a = rest, line = ln });
                        }
                        break;
                    case "set":
                    {
                        int eq = rest.IndexOf('=');
                        string name, value;
                        if (eq > 0 && (eq + 1 >= rest.Length || rest[eq + 1] != '=')) { name = rest.Substring(0, eq).Trim(); value = rest.Substring(eq + 1).Trim(); }
                        else { int s2 = rest.IndexOf(' '); name = s2 < 0 ? rest : rest.Substring(0, s2); value = s2 < 0 ? "" : rest.Substring(s2 + 1).Trim(); }
                        if (!IsName(name) || value.Length == 0) { error = Err(X("mpEventBadSet", "set <variable> = <expression>.")); return null; }
                        steps.Add(new Step { op = Op.Set, a = name.ToLowerInvariant(), b = value, line = ln });
                        break;
                    }
                    case "if":
                        if (rest.Length == 0) { error = Err(X("mpEventNoCondition", "a condition is missing.")); return null; }
                        open.Push(("if", steps.Count, ln, null));
                        steps.Add(new Step { op = Op.JumpIfFalse, a = rest, line = ln });
                        break;
                    case "else":
                    {
                        if (open.Count == 0 || open.Peek().kind != "if") { error = Err(X("mpEventElse", "else without if.")); return null; }
                        var o = open.Pop();
                        open.Push(("else", steps.Count, o.line, null));
                        steps.Add(new Step { op = Op.Jump, line = ln });
                        steps[o.at].target = steps.Count;
                        break;
                    }
                    case "while":
                        if (rest.Length == 0) { error = Err(X("mpEventNoCondition", "a condition is missing.")); return null; }
                        open.Push(("while", steps.Count, ln, null));
                        steps.Add(new Step { op = Op.JumpIfFalse, a = rest, line = ln });
                        break;
                    case "repeat":
                    {
                        string count = rest, var = "_r" + hidden;
                        int asAt = rest.LastIndexOf(" as ", StringComparison.OrdinalIgnoreCase);
                        if (asAt >= 0) { count = rest.Substring(0, asAt).Trim(); var = rest.Substring(asAt + 4).Trim().ToLowerInvariant(); }
                        if (count.Length == 0 || !IsName(var)) { error = Err(X("mpEventBadRepeat", "repeat <count> [as <variable>].")); return null; }
                        string limit = "_n" + hidden++;
                        steps.Add(new Step { op = Op.Set, a = limit, b = count, line = ln });
                        steps.Add(new Step { op = Op.Set, a = var, b = "0", line = ln });
                        open.Push(("repeat", steps.Count, ln, var));
                        steps.Add(new Step { op = Op.JumpIfFalse, a = $"{var} < {limit}", line = ln });
                        steps.Add(new Step { op = Op.Set, a = var, b = $"{var} + 1", line = ln });
                        break;
                    }
                    case "end":
                    {
                        if (open.Count == 0) { error = Err(X("mpEventEnd", "end without if, while or repeat.")); return null; }
                        var o = open.Pop();
                        if (o.kind == "while" || o.kind == "repeat")
                        {
                            steps.Add(new Step { op = Op.Jump, target = o.at, line = ln });
                            steps[o.at].target = steps.Count;
                        }
                        else if (o.kind == "answer")
                            steps.Add(new Step { op = Op.EndThread, line = ln });
                        else if (o.kind == "choice")
                            steps.Add(new Step { op = Op.Jump, target = -1, line = ln });   // to past the vote (set at its end)
                        else if (o.kind == "vote")
                        {
                            steps[o.at].target = steps.Count;
                            for (int k = o.at + 1; k < steps.Count; k++) if (steps[k].op == Op.Jump && steps[k].target == -1) steps[k].target = steps.Count;
                        }
                        else if (o.kind == "parallel" || o.kind == "on")
                        {
                            steps.Add(new Step { op = Op.EndThread, line = ln });
                            steps[o.at].target = steps.Count;   // the fork: the rest goes on after the block
                        }
                        else if (o.kind == "every")
                        {
                            steps.Add(new Step { op = Op.Jump, target = o.at + 1, line = ln });   // back to its wait
                            steps[o.at].target = steps.Count;
                        }
                        else steps[o.at].target = steps.Count;   // if: past the block; else: the jump over it
                        break;
                    }
                    case "stop":
                        steps.Add(new Step { op = Op.Stop, line = ln });
                        break;
                    case "score":
                    case "winner":
                    {
                        string how = rest.ToLowerInvariant();
                        if (how.Length == 0 && word == "winner") how = "-";
                        if (how != "kills" && how != "time" && how != "points" && how != "-") { error = Err(X("mpEventScore", "score / winner take kills, time or points.")); return null; }
                        steps.Add(new Step { op = word == "score" ? Op.Score : Op.Winner, a = how, line = ln });
                        break;
                    }
                    case "on":
                    {
                        int sp2 = rest.IndexOf(' ');
                        string trigger = (sp2 < 0 ? rest : rest.Substring(0, sp2)).ToLowerInvariant();
                        if (Array.IndexOf(Triggers, trigger) < 0)
                        { error = Err(string.Format(X("mpEventBadTrigger", "on <trigger>: {0}."), string.Join(", ", Triggers))); return null; }
                        open.Push(("on", steps.Count, ln, null));
                        steps.Add(new Step { op = Op.Handler, a = trigger, b = sp2 < 0 ? null : rest.Substring(sp2 + 1).Trim(), line = ln });
                        break;
                    }
                    case "ask":
                    {
                        // <players> <seconds> | question | answers...: the selector may hold spaces in its [orbit=...] filter.
                        int open1 = rest.IndexOf('['), space1 = rest.IndexOf(' ');
                        int end = rest.StartsWith("@") && open1 >= 0 && (space1 < 0 || open1 < space1) ? rest.IndexOf(']') + 1 : space1;
                        int bar = rest.IndexOf('|');
                        if (end <= 0 || bar < 0 || bar < end) { error = Err(X("mpEventBadAsk", "ask <players> <seconds> | <question> | <answer> | <answer>.")); return null; }
                        string seconds = rest.Substring(end, bar - end).Trim();
                        var parts = rest.Substring(bar + 1).Split('|');
                        for (int k = 0; k < parts.Length; k++) parts[k] = parts[k].Trim();
                        if (seconds.Length == 0 || parts.Length < 3 || parts.Length > 5) { error = Err(X("mpEventAskAnswers", "a question and 2 to 4 answers.")); return null; }
                        bool isVote = word == "vote";
                        open.Push((isVote ? "vote" : "ask", steps.Count, ln, null));
                        steps.Add(new Step { op = isVote ? Op.Vote : Op.Ask, a = rest.Substring(0, end).Trim(), b = seconds, parts = parts, bodies = new[] { -1, -1, -1, -1, -1 }, line = ln });
                        break;
                    }
                    case "vote":
                    case "answer":
                    case "choice":
                        if (word == "vote") goto case "ask";   // like ask; its blocks are "choice <k>"
                        {
                            string parent = word == "answer" ? "ask" : "vote";
                            if (open.Count == 0 || open.Peek().kind != parent || !int.TryParse(rest, out int k) || k < (word == "choice" ? 0 : 1)
                                || k > steps[open.Peek().at].parts.Length - 1)
                            { error = Err(string.Format(X("mpEventBadAnswer", "{0} <number> belongs inside an {1}."), word, parent)); return null; }
                            steps[open.Peek().at].bodies[k == 0 ? 4 : k - 1] = steps.Count;   // choice 0: no votes
                            open.Push((word, steps.Count, ln, null));
                            break;
                        }
                    case "startevent":
                    {
                        if (rest.Length == 0) { error = Err(X("mpEventBadStart", "startevent <name> [setting=value ...].")); return null; }
                        int sp3 = rest.IndexOf(' ');
                        steps.Add(new Step { op = Op.StartEvent, a = sp3 < 0 ? rest : rest.Substring(0, sp3), b = sp3 < 0 ? "" : rest.Substring(sp3 + 1).Trim(), line = ln });
                        break;
                    }
                    case "parallel":
                        open.Push(("parallel", steps.Count, ln, null));
                        steps.Add(new Step { op = Op.Fork, line = ln });
                        break;
                    case "every":
                        if (rest.Length == 0) { error = Err(X("mpEventNoSeconds", "every needs seconds.")); return null; }
                        open.Push(("every", steps.Count, ln, null));
                        steps.Add(new Step { op = Op.Fork, line = ln });
                        steps.Add(new Step { op = Op.Wait, a = rest, line = ln });
                        break;
                    case "param":
                    {
                        int eq = rest.IndexOf('=');
                        string name = eq > 0 ? rest.Substring(0, eq).Trim() : "", value = eq > 0 ? rest.Substring(eq + 1).Trim() : "";
                        if (!IsName(name) || value.Length == 0) { error = Err(X("mpEventBadParam", "param <variable> = <default>.")); return null; }
                        steps.Add(new Step { op = Op.Param, a = name.ToLowerInvariant(), b = value, line = ln });
                        break;
                    }
                    case "points":
                    {
                        // The selector may hold spaces in its [orbit=...] filter.
                        int open1 = rest.IndexOf('['), space1 = rest.IndexOf(' ');
                        int end = rest.StartsWith("@") && open1 >= 0 && (space1 < 0 || open1 < space1) ? rest.IndexOf(']') + 1 : space1;
                        if (end <= 0 || end >= rest.Length) { error = Err(X("mpEventBadPoints", "points <players> <amount>.")); return null; }
                        steps.Add(new Step { op = Op.Points, a = rest.Substring(0, end).Trim(), b = rest.Substring(end).Trim(), line = ln });
                        break;
                    }
                    case "scoreboard":
                    {
                        string how = rest.Split(' ')[0].ToLowerInvariant();
                        if (how != "on" && how != "off") { error = Err(X("mpEventBadScoreboard", "scoreboard on [title] / off.")); return null; }
                        steps.Add(new Step { op = Op.Scoreboard, a = how, b = rest.Length > how.Length ? rest.Substring(how.Length).Trim() : "", line = ln });
                        break;
                    }
                    case "mission":
                    {
                        int sp4 = rest.IndexOf(' ');
                        string key = (sp4 < 0 ? rest : rest.Substring(0, sp4)).ToLowerInvariant();
                        if (Array.IndexOf(MissionKeys, key) < 0)
                        { error = Err(string.Format(X("mpEventBadMission", "mission <key> <value>: {0}."), string.Join(", ", MissionKeys))); return null; }
                        steps.Add(new Step { op = Op.Mission, a = key, b = sp4 < 0 ? "" : rest.Substring(sp4 + 1).Trim(), line = ln });
                        break;
                    }
                    case "complete":
                    {
                        int bar = rest.IndexOf('|');
                        steps.Add(new Step { op = Op.Complete, a = (bar < 0 ? rest : rest.Substring(0, bar)).Trim(), b = bar < 0 ? "" : rest.Substring(bar + 1).Trim(), line = ln });
                        break;
                    }
                    case "fail":
                        steps.Add(new Step { op = Op.MissionFail, a = rest, line = ln });
                        break;
                    case "event":
                        error = Err(X("mpEventNested", "an event can't start another one."));
                        return null;
                    default:
                        steps.Add(new Step { op = Op.Command, a = line, line = ln });
                        break;
                }
                for (int k = first; k < steps.Count; k++) steps[k].node ??= marker;
            }
            if (open.Count > 0) { error = string.Format(X("mpEventLine", "line {0}: {1}"), open.Peek().line, X("mpEventOpen", "this block has no end.")); return null; }
            return steps;
        }

        static bool IsName(string s)
        {
            if (string.IsNullOrEmpty(s) || !(char.IsLetter(s[0]) || s[0] == '_')) return false;
            foreach (char c in s) if (!char.IsLetterOrDigit(c) && c != '_') return false;
            return true;
        }

        // ---- one run: an event, or a bar mission's ------------------------------------------------------------

        /// <summary>A running event: its program, flows, handlers, questions, the players' results and what it turned on for
        /// them. A bar mission's run has a team (null: a global event, everyone): its selectors (NetCommands.Scope while it
        /// ticks), counts, triggers, scoreboard and notices cover only those players.</summary>
        sealed class EventRun
        {
            public readonly string running;
            public readonly List<Step> program;
            public readonly MissionInfo info;
            public readonly HashSet<ulong> team;
            public readonly int station;   // a mission: where it was taken (-1: none)
            public bool ended;
            public bool eventPvp, eventRespawn, eventRules, eventWaypoint;   // it turned the free for all on / set respawn points / restricted travel / a waypoint
            readonly ulong starter;
            readonly Dictionary<string, double> settings;
            readonly Dictionary<int, PendingAsk> asks = new Dictionary<int, PendingAsk>();
            readonly List<Handler> handlers = new List<Handler>();
            Dictionary<ulong, PlayerState> lastPlayers;
            int lastEnemies;
            float triggerTimer;
            Thread current;   // the thread running now (Fill's @trigger / %trigger%)
            readonly List<Thread> threads = new List<Thread>();
            readonly float startTime;
            float emptySince = -1f, emptyCheck;
            readonly Dictionary<ulong, double> points = new Dictionary<ulong, double>();
            bool scoreboardOn;
            string scoreboardTitle = "", scoreboardSent;
            float scoreboardTimer, scoreboardResend;
            readonly Dictionary<string, double> vars = new Dictionary<string, double>();
            readonly Dictionary<int, Batch> batches = new Dictionary<int, Batch>();
            // The players' results: event ships destroyed, seconds alive in space since the fight began, out (destroyed once).
            readonly Dictionary<ulong, int> kills = new Dictionary<ulong, int>();
            readonly Dictionary<ulong, float> alive = new Dictionary<ulong, float>();
            readonly HashSet<ulong> outOfFight = new HashSet<ulong>();
            readonly HashSet<long> countedKills = new HashSet<long>();
            ScoreMode scoreMode;
            float statsTimer;

            public EventRun(string name, List<Step> program, MissionInfo info, HashSet<ulong> team, int station, NetPlayer by, Dictionary<string, double> given)
            {
                running = name;
                this.program = program;
                this.info = info;
                this.team = team;
                this.station = station;
                settings = new Dictionary<string, double>(given);
                starter = by != null ? by.OwnerClientId : ulong.MaxValue;
                threads.Add(new Thread());
                lastPlayers = Snapshot();
                startTime = Time.unscaledTime;
            }

            /// <summary>The run is about this player: a mission's team, else everyone.</summary>
            public bool InScope(NetPlayer p) => p != null && (team == null || team.Contains(p.OwnerClientId));

            /// <summary>The players in the session this run is about.</summary>
            List<NetPlayer> Players()
            {
                var list = new List<NetPlayer>();
                foreach (var p in NetPlayer.All) if (p != null && p.IsSpawned && InScope(p)) list.Add(p);
                return list;
            }

            /// <summary>A notice in the chat of the run's players.</summary>
            void NoticeScope(string text)
            {
                if (NetState.Instance == null || string.IsNullOrEmpty(text)) return;
                if (team == null) { NetState.Instance.NoticeAll(text); return; }
                foreach (var p in Players()) NetState.Instance.NoticeTo(p, text);
            }

            public bool HasAsk(int id) => asks.ContainsKey(id);

            /// <summary>Ends the run: its scoreboard, music, respawn points, travel restrictions, waypoint and free for all go
            /// for its players; its questions close.</summary>
            public void End()
            {
                if (ended) return;
                ended = true;
                runs.Remove(this);
                if (scoreboardOn || scoreboardSent != null) SendScoreboard("");   // off on every screen
                if (NetState.Instance != null && NetState.Instance.IsServer)
                {
                    foreach (var p in Players())
                    {
                        NetState.Instance.SendAdmin(p.OwnerClientId, NetAdmin.Order.Music, -1, 0, 0, "", "Server");
                        if (eventRespawn) NetState.Instance.SendAdmin(p.OwnerClientId, NetAdmin.Order.Respawn, -1, 0, 0, "off", "Server");
                        if (eventRules) NetState.Instance.SendAdmin(p.OwnerClientId, NetAdmin.Order.Rules, 0, 0, 0, "", "Server");
                        if (eventWaypoint) NetState.Instance.SendAdmin(p.OwnerClientId, NetAdmin.Order.Waypoint, 0, 0, 0, "off", "Server");
                    }
                    if (eventPvp) NetState.Instance.SetFreeForAll(false);
                }
                CloseAsks();
                threads.Clear();
                handlers.Clear();
                current = null;
                if (team != null) NetEventMissions.Ended(team, running);
            }

            /// <summary>A tick: the results, the automatic end, the scoreboard, the triggers, then every flow.</summary>
            public void Tick()
            {
                float now = Time.unscaledTime;
                if (team != null && Players().Count == 0)
                {
                    Debug.Log($"Server: the mission {running} ended: its players left");
                    End();
                    return;
                }
                UpdateStats();
                if (CheckEmpty(now)) return;
                UpdateScoreboard();
                DetectTriggers();
                for (int t = 0; t < threads.Count && !ended; t++) Run(threads[t], now);   // forks add threads: they run this tick too
                if (!ended) threads.RemoveAll(t => t.done);
            }

            public List<(string node, float progress)> ActiveSteps()
            {
                var list = new List<(string, float)>();
                float now = Time.unscaledTime;
                foreach (var t in threads)
                {
                    if (t.done) continue;
                    bool waiting = t.waitUntil >= 0f && t.pc > 0;
                    int at = waiting ? t.pc - 1 : t.pc;   // waiting: pc is already past the wait step
                    if (at < 0 || at >= program.Count) continue;
                    float progress = waiting && t.waitUntil > t.waitStart ? Mathf.Clamp01((now - t.waitStart) / (t.waitUntil - t.waitStart)) : -1f;
                    list.Add((program[at].node, progress));
                }
                return list;
            }

            public Dictionary<string, double> Variables()
            {
                var copy = new Dictionary<string, double>();
                foreach (var kv in vars) if (!kv.Key.StartsWith("_")) copy[kv.Key] = kv.Value;
                return copy;
            }

            public Dictionary<string, double> PlayerPoints()
            {
                var copy = new Dictionary<string, double>();
                foreach (var kv in points) { var p = NetSquad.Find(kv.Key); copy[p != null ? p.DisplayName : kv.Key.ToString()] = kv.Value; }
                return copy;
            }

            // ---- the end of a mission ----

            /// <summary>"complete [reward] [| title]": the reward (an expression; none: the mission's) split evenly across the
            /// run's players still in the session (a global event: everyone), each share in the reward box under the title
            /// (default "Mission accomplished!"); then the run ends.</summary>
            void Complete(string rewardText, string title)
            {
                string expr = (rewardText ?? "").Trim();
                if (expr.Length == 0) expr = info.reward.Length > 0 ? info.reward : "0";
                double reward = Math.Max(0, Eval(Fill(expr)));
                var players = Players();
                long share = players.Count > 0 ? (long)Math.Floor(reward / players.Count) : 0;
                title = NetChat.Clean(Fill(title ?? ""));
                if (title.Length == 0) title = Localization.Get(216);   // "Mission accomplished!"
                var was = executing;
                executing = this;
                try
                {
                    foreach (var p in players)
                    {
                        string id = p.OwnerClientId.ToString(CultureInfo.InvariantCulture);
                        if (share > 0) NetCommands.RunOnServer("reward", $"{id} {share} | {title}", null);
                        else NetCommands.RunOnServer("title", $"{id} {title} for 6", null);
                    }
                }
                finally { executing = was; }
                Debug.Log($"Server: {(team != null ? "mission" : "event")} {running} completed, {share} credits each for {players.Count} player(s)");
                End();
            }

            /// <summary>"fail [title]": the title (default "Mission failed!") on the run's players' screens, then the run ends.</summary>
            void FailMission(string title)
            {
                title = NetChat.Clean(Fill(title ?? ""));
                if (title.Length == 0) title = Localization.Get(392);   // "Mission failed!"
                var was = executing;
                executing = this;
                try
                {
                    foreach (var p in Players())
                        NetCommands.RunOnServer("title", $"{p.OwnerClientId.ToString(CultureInfo.InvariantCulture)} {title} for 6", null);
                }
                finally { executing = was; }
                Debug.Log($"Server: {(team != null ? "mission" : "event")} {running} failed");
                End();
            }
            /// <summary>One flow until it waits, ends or has run MaxOpsPerTick steps.</summary>
            void Run(Thread th, float now)
            {
                if (th.done) return;
                if (th.waitUntil >= 0f)
                {
                    if (now < th.waitUntil) return;
                    th.waitUntil = -1f;
                }
                if (th.askWait != 0)
                {
                    if (asks.TryGetValue(th.askWait, out var ask) && !AskDone(ask, now)) return;
                    if (ask != null && ask.vote) th.pc = Winner(ask, th.voteEnd);
                    CloseAsk(th.askWait);
                    th.askWait = 0;
                }
                current = th;
                try { RunSteps(th, now); }
                finally { current = null; }
            }

            void RunSteps(Thread th, float now)
            {
                for (int ops = 0; ops < MaxOpsPerTick && !ended; ops++)
                {
                    if (th.pc >= program.Count)
                    {
                        if (th == threads[0]) Finish();   // the main flow's end is the event's
                        else th.done = true;
                        return;
                    }
                    var s = program[th.pc];
                    try
                    {
                        switch (s.op)
                        {
                            case Op.Command:
                                th.pc++;
                                RunCommand(Fill(s.a));
                                break;
                            case Op.Wait:
                                th.pc++;
                                th.waitStart = now;
                                th.waitUntil = now + Mathf.Max(0f, (float)Eval(s.a));
                                return;
                            case Op.WaitUntil:
                                if (th.untilDeadline < 0f) th.untilDeadline = s.b != null ? now + Mathf.Max(0f, (float)Eval(s.b)) : float.PositiveInfinity;
                                if ((th.checkTimer -= Time.unscaledDeltaTime) > 0f && now < th.untilDeadline) return;
                                th.checkTimer = CheckSeconds;
                                if (Eval(s.a) != 0 || now >= th.untilDeadline) { th.pc++; th.untilDeadline = -1f; th.checkTimer = 0f; break; }
                                return;
                            case Op.Set:
                                vars[s.a] = Eval(s.b);
                                th.pc++;
                                break;
                            case Op.Param:
                                vars[s.a] = settings.TryGetValue(s.a, out double given) ? given : Eval(s.b);
                                th.pc++;
                                break;
                            case Op.JumpIfFalse:
                                th.pc = Eval(s.a) != 0 ? th.pc + 1 : s.target;
                                break;
                            case Op.Jump:
                                th.pc = s.target;
                                break;
                            case Op.Fork:
                                if (threads.Count >= MaxThreads) throw new Exception(X("mpEventThreads", "too many parallel branches (16 at most)."));
                                threads.Add(new Thread { pc = th.pc + 1 });
                                th.pc = s.target;
                                break;
                            case Op.EndThread:
                                th.done = true;
                                return;
                            case Op.Ask:
                            case Op.Vote:
                            {
                                th.pc = s.target;
                                int id = Ask(s, now);
                                if (id != 0) { th.askWait = id; th.voteEnd = s.target; return; }
                                break;
                            }
                            case Op.StartEvent:
                                th.pc++;
                                SwitchTo(Fill(s.a), Fill(s.b));
                                return;
                            case Op.Handler:
                            {
                                var h = new Handler { trigger = s.a, body = th.pc + 1 };
                                if (!string.IsNullOrEmpty(s.b))
                                {
                                    if (!NetTeleport.ParseStation(Fill(s.b), out h.station, out string left) || (left.Length > 0 && h.trigger != "near"))
                                        throw new Exception(string.Format(X("mpSelNoStation", "No station \"{0}\" (a number, a name or void)."), s.b));
                                    if (h.trigger == "near")
                                    {
                                        // near <station> <x y z> <radius>: game coordinates, metres.
                                        var c = left.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                        var v = new float[4];
                                        if (c.Length != 4) throw new Exception(X("mpEventBadNear", "on near <station> <x y z> <metres>."));
                                        for (int k = 0; k < 4; k++)
                                            if (!float.TryParse(c[k], NumberStyles.Float, CultureInfo.InvariantCulture, out v[k])) throw new Exception(X("mpEventBadNear", "on near <station> <x y z> <metres>."));
                                        h.point = World.OrbitLayout.ToUnity(new Vector3(v[0], v[1], v[2]));
                                        h.radius = Mathf.Max(1f, v[3]);
                                    }
                                }
                                else if (h.trigger == "near") throw new Exception(X("mpEventBadNear", "on near <station> <x y z> <metres>."));
                                handlers.Add(h);
                                th.pc = s.target;
                                break;
                            }
                            case Op.Stop:
                                Finish();
                                return;
                            case Op.Score:
                                scoreMode = Mode(s.a);
                                th.pc++;
                                break;
                            case Op.Winner:
                                th.pc++;
                                AnnounceWinner(s.a == "-" ? scoreMode : Mode(s.a), null);
                                break;
                            case Op.Points:
                                th.pc++;
                                AddPoints(Fill(s.a), Eval(s.b));
                                break;
                            case Op.Mission:
                                th.pc++;
                                break;
                            case Op.Complete:
                                th.pc++;
                                Complete(s.a, s.b);
                                return;
                            case Op.MissionFail:
                                th.pc++;
                                FailMission(s.a);
                                return;
                            case Op.Scoreboard:
                                th.pc++;
                                scoreboardOn = s.a == "on";
                                scoreboardTitle = Fill(s.b ?? "");
                                scoreboardTimer = 0f;
                                if (!scoreboardOn) SendScoreboard("");
                                break;
                        }
                    }
                    catch (Exception e)
                    {
                        Fail(string.Format(X("mpEventLine", "line {0}: {1}"), s.line, e.Message));
                        return;
                    }
                }
            }

            static ScoreMode Mode(string word) => word == "kills" ? ScoreMode.Kills : word == "points" ? ScoreMode.Points : ScoreMode.Time;

            void RunCommand(string line)
            {
                int sp = line.IndexOf(' ');
                string name = (sp < 0 ? line : line.Substring(0, sp)).ToLowerInvariant();
                string args = sp < 0 ? "" : line.Substring(sp + 1).Trim();
                var was = executing;
                executing = this;
                string answer;
                try { answer = NetCommands.RunOnServer(name, args, null); }
                finally { executing = was; }
                if (answer == null) throw new Exception(string.Format(X("mpEventUnknownCmd", "unknown command \"{0}\"."), name));
                if (answer.Length > 0) Debug.Log($"Server: event {running}: {name}: {answer.Replace('\n', ' ')}");
            }

            void Finish()
            {
                Debug.Log($"Server: the event {running} ended");
                Notify(string.Format(X("mpEventEnded", "The event {0} ended."), running));
                End();
            }

            void Fail(string why)
            {
                Debug.LogWarning($"Server: the event {running} stopped: {why}");
                Notify(string.Format(X("mpEventFailed", "The event {0} stopped: {1}"), running, why));
                End();
            }

            /// <summary>The admin who started it (when still here), else the server log only; a mission: its team.</summary>
            void Notify(string text)
            {
                if (team != null) { NoticeScope(text); return; }
                var p = starter != ulong.MaxValue ? NetSquad.Find(starter) : null;
                if (p != null) NetState.Instance?.NoticeTo(p, text);
            }

            /// <summary>The automatic end: this event's ships exist, and nobody has been alive in space in their orbits for 3 s.</summary>
            bool CheckEmpty(float now)
            {
                if (batches.Count == 0 || (vars.TryGetValue("autostop", out double auto) && auto == 0)) { emptySince = -1f; return false; }
                if ((emptyCheck -= Time.unscaledDeltaTime) > 0f) return false;
                emptyCheck = CheckSeconds;
                var stations = new HashSet<int>();
                foreach (var proxy in UnityEngine.Object.FindObjectsByType<NetProxy>())
                    if (proxy != null && proxy.IsSpawned && proxy.EventTag != 0 && batches.ContainsKey(proxy.EventTag)) stations.Add(proxy.Station);
                if (stations.Count == 0) { emptySince = -1f; return false; }   // nothing of the event's in space (yet)
                foreach (var p in NetPlayer.All)
                    if (p != null && p.IsSpawned && InScope(p) && p.InSpace && p.Hull > 0f && stations.Contains(p.Station)) { emptySince = -1f; return false; }
                if (emptySince < 0f) { emptySince = now; return false; }
                if (now - emptySince < EmptySeconds) return false;
                if (team != null) { FailMission(""); return true; }   // a mission: its players gone from its orbits
                string name = running;
                Debug.Log($"Server: the event {name} ended: no players left in its orbits");
                var was = executing;
                executing = this;
                try { AnnounceWinner(scoreMode, X("mpEventOverTitle", "Event over")); }
                finally { executing = was; }
                NoticeScope(string.Format(X("mpEventOverNotice", "The event {0} ended: no players left in its orbit."), name));
                End();
                return true;
            }

            // ---- questions ("ask") ----------------------------------------------------------------------------------

            /// <summary>Sends the question to the players the selector finds (NetAdmin.Order.Ask: "id US seconds US question US
            /// answers..."), returns its id (0: nobody to ask).</summary>
            int Ask(Step s, float now)
            {
                var players = NetCommands.FindTargets(Fill(s.a), null, out _, out string error);
                if (players.Count == 0 && error != null && error != NetCommands.NobodyMatches) throw new Exception(error);
                if (players.Count == 0) return 0;
                float seconds = Mathf.Max(0f, (float)Eval(Fill(s.b)));   // "20" or "{seconds}"
                var ask = new PendingAsk { id = nextAsk++, bodies = s.bodies, vote = s.op == Op.Vote, deadline = seconds > 0f ? now + seconds : float.PositiveInfinity };
                if (nextAsk > 1000000) nextAsk = 1;
                // The question may start with "<speaker> :" (NetAdmin.ResolveSpeaker, as /dialog): its spec travels with \u001e.
                string question = NetChat.Clean(Fill(s.parts[0])), speaker = "";
                int colon = question.IndexOf(':');
                if (colon > 0)
                {
                    string spec = NetAdmin.ResolveSpeaker(question.Substring(0, colon).Trim(), new Dictionary<string, string>());
                    if (spec != null) { speaker = spec.Replace('\u001f', '\u001e'); question = question.Substring(colon + 1).Trim(); }
                }
                var texts = new List<string> { ask.id.ToString(CultureInfo.InvariantCulture), Format(seconds), speaker, question.Replace('\u001f', ' ') };
                for (int k = 1; k < s.parts.Length; k++) texts.Add(NetChat.Clean(Fill(s.parts[k])).Replace('\u001f', ' '));
                string payload = string.Join("\u001f", texts);
                foreach (var p in players)
                {
                    ask.waiting.Add(p.OwnerClientId);
                    NetState.Instance.SendAdmin(p.OwnerClientId, NetAdmin.Order.Ask, ask.id, 0, 0, payload, "Server");
                }
                asks[ask.id] = ask;
                return ask.id;
            }

            /// <summary>Everyone answered (or left), or the time is up.</summary>
            bool AskDone(PendingAsk ask, float now)
            {
                ask.waiting.RemoveWhere(id => NetSquad.Find(id) == null);
                return ask.waiting.Count == 0 || now >= ask.deadline;
            }

            /// <summary>The question off the screens of those who didn't answer.</summary>
            void CloseAsk(int id)
            {
                if (!asks.TryGetValue(id, out var ask)) return;
                asks.Remove(id);
                if (NetState.Instance == null || !NetState.Instance.IsServer) return;
                foreach (var client in ask.waiting) NetState.Instance.SendAdmin(client, NetAdmin.Order.Ask, -id, 0, 0, "", "Server");
            }

            void CloseAsks()
            {
                foreach (var id in new List<int>(asks.Keys)) CloseAsk(id);
                asks.Clear();
            }

            /// <summary>NetState.AnswerRpc (server): a player's answer (0 = skipped): its block runs with them as @trigger.</summary>
            internal void OnAnswer(ulong client, int id, int choice)
            {
                if (ended || !asks.TryGetValue(id, out var ask) || !ask.waiting.Remove(client)) return;
                if (choice >= 1 && choice <= ask.counts.Length) ask.counts[choice - 1]++;
                if (ask.vote) return;   // a vote's block runs once, when it is decided
                if (choice < 1 || choice > ask.bodies.Length || ask.bodies[choice - 1] < 0) return;
                if (threads.Count >= MaxThreads) { Debug.LogWarning($"Server: event {running}: an answer's block skipped (16 threads running)"); return; }
                var p = NetSquad.Find(client);
                threads.Add(new Thread { pc = ask.bodies[choice - 1], subject = client, subjectName = p != null ? p.DisplayName : "" });
            }

            /// <summary>A decided vote: where its flow goes on (the most picked answer's block, a tie at random, else past it).</summary>
            int Winner(PendingAsk ask, int end)
            {
                int top = 0;
                var best = new List<int>();
                for (int k = 0; k < ask.counts.Length; k++)
                {
                    if (ask.counts[k] == 0) continue;
                    if (ask.counts[k] > top) { top = ask.counts[k]; best.Clear(); }
                    if (ask.counts[k] == top) best.Add(k);
                }
                Debug.Log($"Server: event {running}: vote {string.Join("/", ask.counts)}");
                if (best.Count == 0) return ask.bodies[4] >= 0 ? ask.bodies[4] : end;   // nobody voted: "choice 0"
                int pick = best[UnityEngine.Random.Range(0, best.Count)];
                return ask.bodies[pick] >= 0 ? ask.bodies[pick] : end;
            }

            /// <summary>startevent: this event ends and the named one starts with the settings, by the same starter.</summary>
            void SwitchTo(string name, string settingsText)
            {
                float now = Time.unscaledTime;
                if (now - switchSecond > 1f) { switchSecond = now; switches = 0; }
                if (++switches > 5) throw new Exception(X("mpEventSwitchLoop", "events start each other without a pause."));
                string text = Load(name, out string from, out string graphError);
                if (text == null) throw new Exception(string.Format(X("mpEventMissing", "No event \"{0}\". /event list shows them."), name));
                if (graphError != null) throw new Exception(name + ": " + graphError);
                var given = new Dictionary<string, double>();
                foreach (string w in settingsText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = w.IndexOf('=');
                    if (eq <= 0 || !double.TryParse(w.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                        throw new Exception(string.Format(X("mpEventSettingForm", "Settings are name=number: \"{0}\"."), w));
                    given[w.Substring(0, eq).ToLowerInvariant()] = v;
                }
                var by = starter != ulong.MaxValue ? NetSquad.Find(starter) : null;
                string was = running;
                Debug.Log($"Server: the event {was} starts {name}");
                End();
                string answer = NetEvents.Start(name, text, from, by, given, team, station);
                NoticeScope(answer);
            }

            // ---- triggers ("on" handlers) ----------------------------------------------------------------------------

            Dictionary<ulong, PlayerState> Snapshot()
            {
                var map = new Dictionary<ulong, PlayerState>();
                foreach (var p in NetPlayer.All)
                    if (p != null && p.IsSpawned && InScope(p))
                        map[p.OwnerClientId] = new PlayerState
                        {
                            inSpace = p.InSpace, dead = p.InSpace && p.Hull <= 0f, docked = p.InHangar, station = p.Station, name = p.DisplayName,
                        };
                return map;
            }

            /// <summary>Every 0.25 s: what changed for each player since the last look, and whether the event's enemies are all gone.</summary>
            void DetectTriggers()
            {
                if ((triggerTimer -= Time.unscaledDeltaTime) > 0f) return;
                triggerTimer = CheckSeconds;
                var now = Snapshot();
                foreach (var kv in now)
                {
                    var n = kv.Value;
                    if (!lastPlayers.TryGetValue(kv.Key, out var o)) { Fire("joined", kv.Key, n.name, n.station); continue; }
                    if (n.dead && !o.dead) Fire("died", kv.Key, n.name, n.station);
                    if (n.docked && !o.docked) Fire("docked", kv.Key, n.name, n.station);
                    if (n.inSpace && o.docked) Fire("launched", kv.Key, n.name, n.station);
                    if (n.inSpace && !n.dead && (!o.inSpace || o.station != n.station)) Fire("entered", kv.Key, n.name, n.station);
                    if (n.inSpace && !n.dead && o.dead) Fire("respawned", kv.Key, n.name, n.station);
                }
                foreach (var kv in lastPlayers)
                    if (!now.ContainsKey(kv.Key)) Fire("left", kv.Key, kv.Value.name, kv.Value.station);
                lastPlayers = now;
                // near: a player alive in space coming within the radius (again only after leaving it by a fifth more).
                foreach (var h in handlers)
                {
                    if (h.trigger != "near") continue;
                    foreach (var p in NetPlayer.All)
                    {
                        if (p == null || !p.IsSpawned || !InScope(p)) continue;
                        bool here = p.InSpace && p.Hull > 0f && p.Station == h.station;
                        float d = here ? (p.Position - h.point).magnitude : float.MaxValue;
                        if (d <= h.radius && h.inside.Add(p.OwnerClientId)) StartHandler(h, p.OwnerClientId, p.DisplayName, "");
                        else if (d > h.radius * 1.2f) h.inside.Remove(p.OwnerClientId);
                    }
                }
                int enemies = batches.Count > 0 ? CountShips(true) : 0;
                if (lastEnemies > 0 && enemies == 0) Fire("cleared", ulong.MaxValue, "", -1);
                lastEnemies = enemies;
            }

            /// <summary>NetState.DestroyedByRpc (the server checked the kill): "on pvpkill" with the killer, the victim as %victim%.</summary>
            internal void OnPlayerKilled(NetPlayer killer, NetPlayer victim)
            {
                if (ended || killer == null || victim == null || !InScope(killer)) return;
                Fire("pvpkill", killer.OwnerClientId, killer.DisplayName, victim.Station, victim.DisplayName);
            }

            /// <summary>A trigger happened: each handler listening for it (in that station, if limited) starts its block.</summary>
            void Fire(string trigger, ulong player, string name, int station, string other = "")
            {
                foreach (var h in handlers)
                {
                    if (h.trigger != trigger || (h.station >= 0 && h.station != station)) continue;
                    StartHandler(h, player, name, other);
                }
            }

            void StartHandler(Handler h, ulong player, string name, string other)
            {
                if (threads.Count >= MaxThreads) { Debug.LogWarning($"Server: event {running}: \"on {h.trigger}\" skipped (16 threads running)"); return; }
                threads.Add(new Thread { pc = h.body, subject = player, subjectName = name ?? "", otherName = other ?? "" });
            }

            // ---- points and the scoreboard -------------------------------------------------------------------------

            /// <summary>"points &lt;players&gt; &lt;amount&gt;": the amount added to each player the selector finds (none found: nothing).</summary>
            void AddPoints(string selector, double amount)
            {
                var list = NetCommands.FindTargets(selector, null, out _, out string error);
                if (list.Count == 0 && error != null && error != NetCommands.NobodyMatches) throw new Exception(error);
                foreach (var p in list)
                {
                    points.TryGetValue(p.OwnerClientId, out double v);
                    points[p.OwnerClientId] = v + amount;
                }
            }

            /// <summary>A player's score in a mode (points, kills, seconds alive).</summary>
            double ScoreOf(ulong id, ScoreMode mode)
            {
                switch (mode)
                {
                    case ScoreMode.Points: return points.TryGetValue(id, out double p) ? p : 0;
                    case ScoreMode.Kills: return kills.TryGetValue(id, out int k) ? k : 0;
                    default: return alive.TryGetValue(id, out float a) ? a : 0;
                }
            }

            static string ScoreText(double v, ScoreMode mode) => mode == ScoreMode.Time ? Clock((float)v) : Format(v);

            /// <summary>While on: every second the scoreboard (its title, then "name TAB score" rows, the best first) to every
            /// player when it changed, and every 5 s anyway (players who just joined).</summary>
            void UpdateScoreboard()
            {
                if (!scoreboardOn) return;
                if ((scoreboardTimer -= Time.unscaledDeltaTime) > 0f) return;
                scoreboardTimer = 1f;
                var rows = new List<(string name, double score)>();
                foreach (var p in NetPlayer.All)
                    if (p != null && p.IsSpawned && InScope(p)) rows.Add((p.DisplayName, ScoreOf(p.OwnerClientId, scoreMode)));
                rows.Sort((x, y) => y.score.CompareTo(x.score));
                var sb = new System.Text.StringBuilder(scoreboardTitle.Length > 0 ? scoreboardTitle : X("mpEventScoreboard", "Scoreboard"));
                for (int i = 0; i < rows.Count && i < ScoreboardRows; i++) sb.Append('\n').Append(rows[i].name).Append('\t').Append(ScoreText(rows[i].score, scoreMode));
                string text = sb.ToString();
                if (text == scoreboardSent && (scoreboardResend -= 1f) > 0f) return;
                scoreboardResend = 5f;
                SendScoreboard(text);
            }

            /// <summary>The scoreboard to every player ("" hides it).</summary>
            void SendScoreboard(string text)
            {
                scoreboardSent = text.Length > 0 ? text : null;
                if (NetState.Instance == null || !NetState.Instance.IsServer) return;
                foreach (var p in NetPlayer.All)
                    if (p != null && p.IsSpawned && InScope(p)) NetState.Instance.SendAdmin(p.OwnerClientId, NetAdmin.Order.Scoreboard, 0, 0, 0, text, "Server");
            }

            // ---- results and the winner -----------------------------------------------------------------------------

            /// <summary>One NPC whatever proxy shows it: its owner and its index in the owner's traffic (a ship's proxy can be made
            /// again, and Netcode reuses network ids).</summary>
            static long ShipKey(NetProxy p) => (long)p.OwnerClientId << 32 | (uint)p.LocalId;

            void ClearStats()
            {
                kills.Clear();
                alive.Clear();
                outOfFight.Clear();
                countedKills.Clear();
                points.Clear();
                scoreMode = ScoreMode.Time;
            }

            /// <summary>Every 0.25 s: the event ships destroyed since (each once, to its killer), and the time alive in space of
            /// every player still in the fight (from the event's first spawn; destroyed = out for good).</summary>
            void UpdateStats()
            {
                statsTimer += Time.unscaledDeltaTime;
                if (statsTimer < CheckSeconds) return;
                float dt = statsTimer;
                statsTimer = 0f;
                if (batches.Count == 0) return;   // the fight hasn't begun
                foreach (var proxy in UnityEngine.Object.FindObjectsByType<NetProxy>())
                {
                    if (proxy == null || !proxy.IsSpawned || proxy.EventTag == 0 || !batches.ContainsKey(proxy.EventTag)) continue;
                    if (proxy.Killer == ulong.MaxValue || !countedKills.Add(ShipKey(proxy))) continue;
                    kills.TryGetValue(proxy.Killer, out int k);
                    kills[proxy.Killer] = k + 1;
                    var killer = NetSquad.Find(proxy.Killer);
                    Fire("kill", proxy.Killer, killer != null ? killer.DisplayName : "", proxy.Station);
                }
                foreach (var p in NetPlayer.All)
                {
                    if (p == null || !p.IsSpawned || !InScope(p) || outOfFight.Contains(p.OwnerClientId) || !p.InSpace) continue;
                    if (p.Hull <= 0f) { if (alive.ContainsKey(p.OwnerClientId)) outOfFight.Add(p.OwnerClientId); continue; }
                    alive.TryGetValue(p.OwnerClientId, out float a);
                    alive[p.OwnerClientId] = a + dt;
                }
            }

            /// <summary>@survivors: a player alive in space who has been in this event's fight and never destroyed in it.</summary>
            public bool Survived(NetPlayer p) =>
                !ended && p != null && InScope(p) && p.InSpace && p.Hull > 0f && alive.ContainsKey(p.OwnerClientId) && !outOfFight.Contains(p.OwnerClientId);

            static string Clock(float seconds)
            {
                int s = Mathf.FloorToInt(seconds);
                return $"{s / 60}:{s % 60:00}";
            }

            /// <summary>"The winner is X" (the most kills, points, or the longest alive) on everyone's screen and in the chat;
            /// 'title' above it instead (the automatic end's "Event over").</summary>
            void AnnounceWinner(ScoreMode mode, string title)
            {
                var best = new List<ulong>();
                double top = 0;
                var ids = new HashSet<ulong>();
                if (mode == ScoreMode.Kills) foreach (var id in kills.Keys) ids.Add(id);
                else if (mode == ScoreMode.Points) foreach (var id in points.Keys) ids.Add(id);
                else foreach (var id in alive.Keys) ids.Add(id);
                foreach (var id in ids)
                {
                    double v = ScoreOf(id, mode);
                    if (v <= 0 || NetSquad.Find(id) == null) continue;
                    if (v > top + 0.001) { best.Clear(); top = v; }
                    if (Math.Abs(v - top) <= 0.001) best.Add(id);
                }
                string line;
                if (best.Count == 0) line = X("mpEventNoWinner", "No winner");
                else
                {
                    var names = new List<string>();
                    foreach (var id in best) names.Add(NetSquad.Find(id).DisplayName);
                    string who = string.Join(" & ", names);
                    line = mode == ScoreMode.Kills ? string.Format(X("mpEventWinnerKills", "The winner is {0} with {1} kills"), who, (int)top)
                        : mode == ScoreMode.Points ? string.Format(X("mpEventWinnerPoints", "The winner is {0} with {1} points"), who, Format(top))
                        : string.Format(X("mpEventWinnerTime", "The winner is {0}, alive for {1}"), who, Clock((float)top));
                }
                NetCommands.RunOnServer("title", "@a " + (title != null ? title + " | " + line : line) + " for 8", null);
                NoticeScope(line + ".");
                Debug.Log($"Server: event {running}: {line}");
            }

            // ---- spawned ships ------------------------------------------------------------------------------------

            /// <summary>NetAdmin.Spawn, while a script's spawn line runs: a new batch tag for 'count' ships to one player (0 when
            /// no event is running the command).</summary>
            internal int NewBatch(int count, bool enemy)
            {
                int tag = nextTag++;
                if (nextTag > 0x7ffff) nextTag = 1;
                batches[tag] = new Batch { expected = count, enemy = enemy, start = Time.unscaledTime };
                return tag;
            }

            /// <summary>This event's living spawned ships (only the enemies), the ones on their way included.</summary>
            int CountShips(bool enemiesOnly)
            {
                if (batches.Count == 0) return 0;
                int living = 0;
                foreach (var proxy in UnityEngine.Object.FindObjectsByType<NetProxy>())
                {
                    if (proxy == null || !proxy.IsSpawned || proxy.EventTag == 0 || !batches.TryGetValue(proxy.EventTag, out var b)) continue;
                    b.seen.Add(ShipKey(proxy));
                    if (enemiesOnly && !b.enemy) continue;
                    if (proxy.FlyingNow) living++;
                }
                float now = Time.unscaledTime;
                foreach (var b in batches.Values)
                    if ((!enemiesOnly || b.enemy) && now - b.start < PendingSeconds) living += Math.Max(0, b.expected - b.seen.Count);
                return living;
            }

            // ---- expressions -----------------------------------------------------------------------------------------

            /// <summary>A command line with its {expression} parts filled in.</summary>
            string Fill(string line)
            {
                if (line.IndexOf("%victim%", StringComparison.Ordinal) >= 0) line = line.Replace("%victim%", current?.otherName ?? "");
                if (line.IndexOf("trigger", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    bool has = current != null && current.subject != ulong.MaxValue;
                    if (!has && (line.Contains("@trigger") || line.Contains("%trigger%")))
                        throw new Exception(X("mpEventNoTrigger", "@trigger / %trigger% are only for an \"on\" handler about a player."));
                    if (has) line = line.Replace("%trigger%", current.subjectName).Replace("@trigger", current.subject.ToString(CultureInfo.InvariantCulture));
                }
                if (line.IndexOf('{') < 0) return line;
                var sb = new System.Text.StringBuilder();
                int i = 0;
                while (i < line.Length)
                {
                    int open = line.IndexOf('{', i);
                    if (open < 0) { sb.Append(line, i, line.Length - i); break; }
                    int close = line.IndexOf('}', open + 1);
                    if (close < 0) throw new Exception(X("mpEventBrace", "a { without its }."));
                    sb.Append(line, i, open - i);
                    sb.Append(Format(Eval(line.Substring(open + 1, close - open - 1))));
                    i = close + 1;
                }
                return sb.ToString();
            }

            static string Format(double v) =>
                Math.Abs(v - Math.Round(v)) < 1e-9 ? ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture) : v.ToString("0.##", CultureInfo.InvariantCulture);

            double Eval(string text)
            {
                var p = new Parser(this, text);
                double v = p.Or();
                p.SkipSpace();
                if (!p.AtEnd) throw new Exception(string.Format(X("mpEventExpr", "can't read \"{0}\"."), text));
                return v;
            }

            double Value(string name)
            {
                switch (name)
                {
                    case "enemies": return CountShips(true);
                    case "ships": return CountShips(false);
                    case "time": return Time.unscaledTime - startTime;
                case "missionstation": return station;   // a bar mission: where it was taken (-1: an /event)
                    case "toppoints":
                    {
                        double top = 0;
                        foreach (var pts in points.Values) top = Math.Max(top, pts);
                        return top;
                    }
                    case "players": case "inspace": case "docked": case "dead":
                    {
                        int n = 0;
                        foreach (var p in NetPlayer.All)
                        {
                            if (p == null || !p.IsSpawned || !InScope(p)) continue;
                            bool count = name == "players" || (name == "inspace" && p.InSpace && p.Hull > 0f)
                                         || (name == "docked" && p.InHangar) || (name == "dead" && p.InSpace && p.Hull <= 0f);
                            if (count) n++;
                        }
                        return n;
                    }
                }
                if (vars.TryGetValue(name, out double v)) return v;
                throw new Exception(string.Format(X("mpEventNoVar", "no variable \"{0}\" (set it first)."), name));
            }

            /// <summary>A small recursive-descent evaluator: or / and / not, comparisons, + -, * / %, unary -, ( ), numbers,
            /// names and the functions random, min, max.</summary>
            sealed class Parser
            {
                readonly EventRun run;
                readonly string s;
                int i;
                public Parser(EventRun run, string text) { this.run = run; s = text ?? ""; }
                public bool AtEnd => i >= s.Length;
                public void SkipSpace() { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

                bool Word(string w)
                {
                    SkipSpace();
                    if (string.Compare(s, i, w, 0, w.Length, StringComparison.OrdinalIgnoreCase) != 0) return false;
                    int end = i + w.Length;
                    if (end < s.Length && (char.IsLetterOrDigit(s[end]) || s[end] == '_')) return false;
                    i = end;
                    return true;
                }

                bool Sym(string sym)
                {
                    SkipSpace();
                    if (string.CompareOrdinal(s, i, sym, 0, sym.Length) != 0) return false;
                    i += sym.Length;
                    return true;
                }

                public double Or()
                {
                    double v = And();
                    while (Word("or") || Sym("||")) { double r = And(); v = v != 0 || r != 0 ? 1 : 0; }
                    return v;
                }

                double And()
                {
                    double v = Not();
                    while (Word("and") || Sym("&&")) { double r = Not(); v = v != 0 && r != 0 ? 1 : 0; }
                    return v;
                }

                double Not()
                {
                    if (Word("not")) return Not() == 0 ? 1 : 0;
                    if (Peek('!') && !PeekAt(1, '=')) { i++; return Not() == 0 ? 1 : 0; }
                    return Compare();
                }

                bool Peek(char c) { SkipSpace(); return i < s.Length && s[i] == c; }
                bool PeekAt(int k, char c) => i + k < s.Length && s[i + k] == c;

                double Compare()
                {
                    double v = Sum();
                    if (Sym("==")) return v == Sum() ? 1 : 0;
                    if (Sym("!=")) return v != Sum() ? 1 : 0;
                    if (Sym("<=")) return v <= Sum() ? 1 : 0;
                    if (Sym(">=")) return v >= Sum() ? 1 : 0;
                    if (Sym("<")) return v < Sum() ? 1 : 0;
                    if (Sym(">")) return v > Sum() ? 1 : 0;
                    if (Sym("=")) return v == Sum() ? 1 : 0;
                    return v;
                }

                double Sum()
                {
                    double v = Product();
                    while (true)
                    {
                        if (Sym("+")) v += Product();
                        else if (Sym("-")) v -= Product();
                        else return v;
                    }
                }

                double Product()
                {
                    double v = Unary();
                    while (true)
                    {
                        if (Sym("*")) v *= Unary();
                        else if (Sym("/")) { double d = Unary(); v = d == 0 ? 0 : v / d; }
                        else if (Sym("%")) { double d = Unary(); v = d == 0 ? 0 : v % d; }
                        else return v;
                    }
                }

                double Unary()
                {
                    if (Sym("-")) return -Unary();
                    if (Sym("+")) return Unary();
                    return Atom();
                }

                double Atom()
                {
                    SkipSpace();
                    if (Sym("(")) { double v = Or(); if (!Sym(")")) throw new Exception("\")\" missing."); return v; }
                    int start = i;
                    if (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.'))
                    {
                        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                        return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
                    }
                    if (i < s.Length && (char.IsLetter(s[i]) || s[i] == '_'))
                    {
                        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                        string name = s.Substring(start, i - start).ToLowerInvariant();
                        if (name == "count" && Peek('('))
                        {
                            // count(<players>): the selector as text up to its ")" ({expression} parts filled in).
                            Sym("(");
                            int from = i, depth = 0;
                            while (i < s.Length && (depth > 0 || s[i] != ')')) { if (s[i] == '{') depth++; else if (s[i] == '}') depth--; i++; }
                            if (i >= s.Length) throw new Exception("\")\" missing.");
                            string selector = run.Fill(s.Substring(from, i - from).Trim());
                            i++;
                            var found = NetCommands.FindTargets(selector, null, out _, out string error);
                            if (found.Count == 0 && error != null && error != NetCommands.NobodyMatches) throw new Exception(error);
                            return found.Count;
                        }
                        if (Sym("("))
                        {
                            var args = new List<double>();
                            if (!Sym(")"))
                            {
                                do args.Add(Or()); while (Sym(","));
                                if (!Sym(")")) throw new Exception("\")\" missing.");
                            }
                            return Call(name, args);
                        }
                        return run.Value(name);
                    }
                    throw new Exception(string.Format("can't read \"{0}\".", s));
                }

                static double Call(string name, List<double> a)
                {
                    switch (name)
                    {
                        case "random" when a.Count == 2:
                            int lo = (int)Math.Floor(Math.Min(a[0], a[1])), hi = (int)Math.Floor(Math.Max(a[0], a[1]));
                            return UnityEngine.Random.Range(lo, hi + 1);
                        case "min" when a.Count == 2: return Math.Min(a[0], a[1]);
                        case "max" when a.Count == 2: return Math.Max(a[0], a[1]);
                        case "floor" when a.Count == 1: return Math.Floor(a[0]);
                    }
                    throw new Exception(string.Format("no function {0} with {1} value(s).", name, a.Count));
                }
            }
        }
    }
}
