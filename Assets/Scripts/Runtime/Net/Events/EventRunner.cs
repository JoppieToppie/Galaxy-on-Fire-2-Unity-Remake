// EventRunner.cs
// Remake events: events (game modes like waves of enemies), run on the server by an admin's /event <name> or the
// dedicated server's console ("event <name>"); one event at a time, "/event stop" ends it, "/event list" lists them.
// An event is a node graph made in the Editor: <name>.gof2event in an Events folder (the game's data folder,
// Application.persistentDataPath/Events, or next to the game / dedicated server's executable), else a built-in one
// (Resources/GoF2Events: waves, survival). EventGraphFile reads the file, EventGraphScript compiles the graph into the
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
//                                 no player), hacked (the player hacked a wreck: "spawn ... wreck", a mod's derelict, a
//                                 Supernova wreck; "%victim%" the wreck's name). The station limits a player trigger to
//                                 that orbit or station. Handlers start working when the flow passes them (the graph puts them
//                                 first) and last until the event ends. A free for all (/pvp), the respawn points (/respawn)
//                                 and the travel restrictions (/restrict) the event set end with it.
//   ask <players> <seconds> | <question> | <answer 1> | <answer 2> [| 3 | 4]   then "answer <k>" / end blocks, then end:
//                                 the question on those players' screens (EventScreen; "<speaker> : <question>" shows a speaker as
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
//                                 every station), chance (the percentage of those bars offering it, default 35; each
//                                 station decides for 30 minutes), takers universe | station | any (one team at a time in
//                                 the universe, the default; one per station, that bar dropping it for 30 minutes; or any
//                                 number, at one station too). An event with a
//                                 title is offered in the multiplayer Space Lounges
//                                 (EventMissions) and runs for the players who took it
//   complete [reward] [| title]   pays the reward (an expression, else the mission's) split evenly across the run's players
//                                 (the reward box, title default "Mission accomplished!"), then ends the run
//   fail [title]                  the title (default "Mission failed!") on the run's players' screens, then ends the run
// Single player's lasting runs (EventHost.Local): a graph whose Start node kind is Quest ("mission kind quest") is a quest
// like the story's chain: it starts by itself once its start condition holds ("mission starts <expression>"; none: at once;
// "never": only by another graph's startquest), keeps going across scenes and saves, and shows its objective in the
// Missions window; a bar mission taken in single player lasts the same way (and counts as the player's one mission):
//   checkpoint [name]             what a save keeps of the run: its variables, objective, target and quiet orbits as they
//                                 are here; loading the save starts the run again after its last checkpoint (on the main flow
//                                 only, outside blocks). The handlers and the parallel / every blocks before it start again,
//                                 the music, waypoint, respawn and travel restriction commands before it run again
//   objective [text]              the quest's objective in the Missions window (empty: none)
//   target <station | off>        the station of the story icon (map, HUD) and the window's Show on map
//   questorbit <station> [off]    that orbit is the quest's: no normal traffic is built there (like a story orbit)
//   startquest <name>             starts that quest graph beside this run (single player)
//   complete in a quest marks it done (it never starts again); fail in a quest is the story's failure: "Mission failed!",
//   then the last save is loaded (the run goes back to its checkpoint)
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
// and this game's own state (a variable of the same name wins): campaign (the story step, -1 in free play), credits, rank,
// station (where the player is), system, ship (the ship flown), kills; functions cargo(item) (tonnes in the hold), has(item)
// (in the hold or mounted), visited(station) (1 / 0), quest(name) (0 not started, 1 under way, 2 done); items and stations
// by number or name
// Ending by itself: once it has spawned ships, an event ends when no player has been alive in space in its ships' orbits
// for 3 s (all destroyed, docked or gone): "Event over" with the winner on everyone's screen. "set autostop = 0" turns
// that off.
// The ships an event spawns carry its batch tag (SpawnSpec.eventTag, written on their NetProxy): the server sees every
// NPC of every player's game as a NetProxy, so it can count them. Ticked by NetState.Update on the server.
// Single player (no session, mods' events and bar missions) runs the same code through EventHost: the one LocalPilot,
// orders applied at once, the local traffic's ships, ticked by LocalEvents, on a clock that stops while the game pauses.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GoF2Remake.Data;
using UnityEngine;
using GoF2Remake.Multiplayer;

namespace GoF2Remake.Events
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static partial class EventRunner
    {
        const float CheckSeconds = 0.25f, PendingSeconds = 6f, EmptySeconds = 3f;
        const int MaxOpsPerTick = 500, MaxLines = 4000, MaxThreads = 16, ScoreboardRows = 10, MaxRuns = 32;

        enum Op { Command, Wait, WaitUntil, Set, JumpIfFalse, Jump, Stop, Score, Winner, Fork, EndThread, Param, Points, Scoreboard, Handler, Ask, Vote, StartEvent,
                   Mission, Complete, MissionFail, Checkpoint, Objective, Target, QuietOrbit, StartQuest }

        enum ScoreMode { Time, Kills, Points }

        sealed class Step
        {
            public Op op;
            public string a, b;     // command line / expression / variable / selector; second expression (timeout, value)
            public int target;      // jumps, a fork's continuation
            public int line;
            public int depth;       // the blocks around it (0: the main flow, or a handler's "on" line itself)
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
            /// <summary>Seen dying or dead (destroyed, or an order's jump-out): the only way a seen ship stops counting.</summary>
            public readonly HashSet<long> down = new HashSet<long>();
            /// <summary>The orbits its ships were seen in, kept after they vanish (CheckEmpty).</summary>
            public readonly HashSet<int> stations = new HashSet<int>();
        }


        static readonly string[] Triggers = { "died", "docked", "launched", "joined", "left", "entered", "kill", "cleared", "pvpkill", "respawned", "near", "hacked", "pad" };
        static readonly string[] MissionKeys = { "title", "offer", "client", "reward", "players", "stations", "kind", "starts", "chance", "takers", "repeat" };

        /// <summary>A bar mission's details: the Start node's Mission settings (the "mission &lt;key&gt; &lt;value&gt;" lines).</summary>
        public sealed class MissionInfo
        {
            public const int DefaultChance = 35;

            public string name = "", title = "", offer = "", client = "", reward = "", starts = "";
            public readonly List<int> stations = new List<int>();   // offered at (none: at every station)
            public int minPlayers = 1, maxPlayers = 4;
            // "mission chance": the percentage of a station's bars offering it (EventMissions.Offered: decided for a while per
            // station, so docking again doesn't roll it anew); 100 = every bar, as before the setting.
            public int chance = DefaultChance;
            // "mission takers universe | station | any": one team at a time anywhere (EventMissions offers it nowhere while a team
            // is on it, StartMission refuses a second), one per station (taken out of that bar only), or any number of teams.
            public EventMissionTakers takers;
            public int kind = -1;   // "mission kind": 0 event, 1 bar mission, 2 quest (-1: not given, a bar mission when titled)
            public float repeatMinutes;   // "mission repeat": a quest that comes again this many minutes of playing time after it was done (0: once)
            public bool builtIn;    // one of the game's own examples (Resources/GoF2Events): multiplayer only
            public bool IsMission => kind == 1 || (kind < 0 && title.Length > 0);
            public bool IsQuest => kind == 2;
            public string DisplayTitle => title.Length > 0 ? title : name;
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
            EventMissions.ResetOffers();
        }

        /// <summary>Session.ResetNewGame (a new game, a load, the main menu): single player's runs go with the game they
        /// belonged to (a save's come back through RestorePending).</summary>
        public static void ResetLocal()
        {
            RestorePending = false;
            if (!EventHost.Local) return;
            foreach (var r in runs.ToArray()) { r.record = null; r.End(); }
            runs.Clear();
            ticking = executing = null;
            NetCommands.Scope = null;
            EventMissions.ResetLocal();
        }

        static string X(string key, string english) => Localization.Extra(key, english);

        /// <summary>The global event (null: none).</summary>
        static EventRun Global => runs.Find(r => r.team == null);

        /// <summary>A global event runs (/event).</summary>
        public static bool Running => Global != null;

        /// <summary>Any run, the global event or a bar mission (single player's LocalEvents ticks while one runs).</summary>
        public static bool AnyRun => runs.Count > 0;

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
        internal static void NoteFlightSet() { if (executing != null) executing.eventFlight = true; }
        internal static void NoteWaypointSet() { if (executing != null) executing.eventWaypoint = true; }
        internal static void NoteWormholeSet() { if (executing != null) executing.eventWormhole = true; }
        internal static void NoteCutsceneSet() { if (executing != null) executing.eventCutscene = true; }
        internal static void NoteFadeSet() { if (executing != null) executing.eventFade = true; }

        /// <summary>NetAdmin.Spawn, while a script's spawn line runs: a new batch tag of that run for 'count' ships to one
        /// player (0 when no event is running the command).</summary>
        internal static int NewBatch(int count, bool enemy) => executing != null ? executing.NewBatch(count, enemy) : 0;

        /// <summary>@survivors: a player alive in space who has been in the event's fight and never destroyed in it (the run
        /// being ticked; a command typed in the chat: the global event's).</summary>
        public static bool Survived(IPilot p)
        {
            var r = ticking ?? Global;
            return r != null && r.Survived(p);
        }

        /// <summary>A player's mission run (null: none).</summary>
        static EventRun MissionOf(ulong client) => runs.Find(r => r.team != null && !r.ended && r.team.Contains(client)
                                                                 && (r.record == null || r.record.kind == GraphQuestState.KindMission));   // not a quest

        /// <summary>The player is on a bar mission of an event graph.</summary>
        public static bool InMission(ulong client) => MissionOf(client) != null;

        /// <summary>Server, NetState.Update: every run, its selectors limited to its players while it runs.</summary>
        internal static void Tick()
        {
            if (runs.Count == 0) return;
            if (!EventHost.ServerReady) { Reset(); return; }
            foreach (var r in runs.ToArray())
            {
                if (r.ended) continue;
                ticking = r;
                NetCommands.Scope = r.team != null ? r.InScope : (Func<IPilot, bool>)null;
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
        internal static void OnPlayerKilled(IPilot killer, IPilot victim)
        {
            foreach (var r in runs.ToArray()) r.OnPlayerKilled(killer, victim);
        }

        /// <summary>A player hacked a wreck (SpaceHooks.WreckHacked; in a session NetState.WreckHackedRpc, checked): "on hacked"
        /// in every run the player is in, the wreck's name as %victim%.</summary>
        public static void OnWreckHacked(IPilot pilot, int station, string wreck)
        {
            foreach (var r in runs.ToArray()) r.OnWreckHacked(pilot, station, wreck);
        }

        /// <summary>Remake mods: a player docked at a station's pad outside (SpaceHooks.PadDocked; in a session
        /// NetState.PadDockedRpc, checked): "on pad" in every run the player is in.</summary>
        public static void OnPadDocked(IPilot pilot, int station)
        {
            foreach (var r in runs.ToArray()) r.OnPadDocked(pilot, station);
        }

        // ---- bar missions (EventMissions) ----

        static List<MissionInfo> graphCache;
        static float graphCacheTime = -100f;
        static int graphCacheMods = -1;

        // ---- single player's quests and bar missions (lasting runs, Session.GraphQuests) ----

        /// <summary>A save was loaded with runs under way: LocalEvents starts them again once a game scene is up.</summary>
        public static bool RestorePending { get => GraphQuestState.RestorePending; set => GraphQuestState.RestorePending = value; }

        static float questCheck;

        /// <summary>Single player: the bar mission of an event graph the player is on (its run; null: none).</summary>
        static EventRun LocalMissionRun => runs.Find(r => r.record != null && r.record.kind == GraphQuestState.KindMission && !r.ended);

        /// <summary>One quest for the Missions window: its title, objective and target station.</summary>
        public struct QuestView { public string name, title, objective; public int target; }

        /// <summary>The single-player bar mission's target (its "target" line; -1: none set).</summary>
        public static int LocalMissionTarget => LocalMissionRun?.target ?? -1;

        /// <summary>The single-player bar mission's objective (its "objective" line; "": none).</summary>
        public static string LocalMissionObjective => LocalMissionRun?.objective ?? "";

        /// <summary>An event graph file's extension; LegacyExtension = the files from when the graphs were multiplayer only
        /// (read everywhere, the Editor renames them, EventGraphImporter).</summary>
        public const string GraphExtension = "gof2event", LegacyExtension = "gof2netevent";
        /// <summary>The graphs' file extensions, the Mod Builder's missions (QuestFile, .gof2mission, the old .gof2quest) last.</summary>
        static readonly string[] Extensions = { GraphExtension, LegacyExtension, QuestFile.Extension, QuestFile.LegacyExtension };
        /// <summary>The built-in events' Resources folder.</summary>
        const string BuiltIn = "GoF2Events";

        // ---- one run: an event, or a bar mission's ------------------------------------------------------------

        /// <summary>A running event: its program, flows, handlers, questions, the players' results and what it turned on for
        /// them. A bar mission's run has a team (null: a global event, everyone): its selectors (NetCommands.Scope while it
        /// ticks), counts, triggers, scoreboard and notices cover only those players.</summary>
        sealed partial class EventRun
        {
            public readonly string running;
            public readonly List<Step> program;
            public readonly MissionInfo info;
            public readonly HashSet<ulong> team;
            public readonly int station;   // a mission: where it was taken (-1: none)
            public bool ended;
            public bool eventPvp, eventRespawn, eventRules, eventWaypoint, eventWormhole, eventCutscene, eventFade, eventFlight;   // it turned the free for all on / set respawn points / restricted travel / a waypoint
            public GraphQuestState record;   // single player's lasting run (a quest, a bar mission): what a save keeps (null: none)
            public string objective = "";
            public int target = -1;
            public readonly HashSet<int> quiet = new HashSet<int>();
            bool succeeded, failed;
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

            public EventRun(string name, List<Step> program, MissionInfo info, HashSet<ulong> team, int station, IPilot by, Dictionary<string, double> given)
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
                startTime = EventHost.Now;
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
                        if ((name == "cargo" || name == "has" || name == "visited" || name == "quest" || name == "option" || name == "won" || name == "standing") && Peek('('))
                        {
                            // An item / station / quest by number or name: the text up to its ")" ({expression} parts filled in).
                            Sym("(");
                            int from = i, depth = 0;
                            while (i < s.Length && (depth > 0 || s[i] != ')')) { if (s[i] == '{') depth++; else if (s[i] == '}') depth--; i++; }
                            if (i >= s.Length) throw new Exception("\")\" missing.");
                            string arg = run.Fill(s.Substring(from, i - from).Trim());
                            i++;
                            return GameState(name, arg);
                        }
                        if ((name == "docked" || name == "inorbit") && Peek('('))
                        {
                            // docked(<station>) / inorbit(<station>): how many of the event's players are docked at that station
                            // / alive in its orbit (the Mod Builder's quests: "go to", "fight there").
                            Sym("(");
                            int from = i, depth = 0;
                            while (i < s.Length && (depth > 0 || s[i] != ')')) { if (s[i] == '{') depth++; else if (s[i] == '}') depth--; i++; }
                            if (i >= s.Length) throw new Exception("\")\" missing.");
                            string arg = run.Fill(s.Substring(from, i - from).Trim());
                            i++;
                            return run.PilotsAt(arg, name == "docked");
                        }
                        if ((name == "alive" || name == "distance") && Peek('('))
                        {
                            // alive(<ship name>): how many of the event's ships of that name fly; distance(<ship name>, x, y, z):
                            // game units from the nearest of them to that point of its orbit (-1: none).
                            Sym("(");
                            int from = i, depth = 0;
                            while (i < s.Length && (depth > 0 || s[i] != ')')) { if (s[i] == '{') depth++; else if (s[i] == '}') depth--; i++; }
                            if (i >= s.Length) throw new Exception("\")\" missing.");
                            string arg = run.Fill(s.Substring(from, i - from).Trim());
                            i++;
                            return ShipState(name, arg);
                        }
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

                /// <summary>alive / distance of the event's named ships (EventHost.EventShips: names and positions).</summary>
                static double ShipState(string name, string arg)
                {
                    var parts = arg.Split(',');
                    string ship = parts[0].Trim().Trim('"');
                    var point = Vector3.zero;
                    if (name == "distance")
                    {
                        if (parts.Length != 4) throw new Exception("distance(ship name, x, y, z)");
                        point = new Vector3(Coord(parts[1]), Coord(parts[2]), Coord(parts[3]));
                    }
                    int alive = 0;
                    double best = -1;
                    foreach (var sh in EventHost.EventShips())
                    {
                        if (!sh.flying || string.IsNullOrEmpty(sh.name)) continue;
                        if (!string.Equals(sh.name, ship, StringComparison.OrdinalIgnoreCase) && !sh.name.StartsWith(ship + " ", StringComparison.OrdinalIgnoreCase)) continue;
                        alive++;
                        double d = Vector3.Distance(sh.position, point);
                        if (best < 0 || d < best) best = d;
                    }
                    return name == "alive" ? alive : best;
                }

                static float Coord(string s) => float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
                    ? v : throw new Exception($"\"{s.Trim()}\" isn't a number.");

                /// <summary>cargo / has / visited / quest with their argument.</summary>
                static double GameState(string name, string arg)
                {
                    if (name == "quest")
                        return Session.GraphQuestsDone.Contains(arg) || Session.GraphQuestRepeats.ContainsKey(arg) ? 2 : Session.GraphQuests.Exists(q => q.name == arg) ? 1 : 0;
                    // option(<mod id>:<option id>): a mod's new-game option is on in this game (Modding.ModGameOptions).
                    if (name == "option") return Modding.ModGameOptions.IsOn(arg) ? 1 : 0;
                    // won(main | valkyrie | supernova): that campaign's story is finished in this game (or the multiplayer
                    // session's finished world).
                    if (name == "won")
                    {
                        int need = arg.ToLowerInvariant() switch
                        {
                            "main" or "gof2" => Story.GameWonIndex,
                            "valkyrie" => Story.Dlc1WonIndex,
                            "supernova" => Story.LastIndex,
                            _ => throw new Exception($"won({arg}): main, valkyrie or supernova."),
                        };
                        return Session.CompletedWorld || (!Session.FreePlay && Session.CampaignMission >= need) ? 1 : 0;
                    }
                    // standing(<race|race>): the player's standing toward a game race(terran, vossk, nivelian, midorian) or a
                    // mod's race, -100..100 (Standing.RawToward); the always-hostile sides (pirates, the Void, Specters, a
                    // "hostile"race) -100.
                    if (name == "standing")
                    {
                        int race = Flight.Standing.RaceWord(arg);
                        if (race < 0) throw new Exception($"standing({arg}): terran, vossk, nivelian, midorian or a mod's race.");
                        bool fixedEnemy = Modding.ModRaces.Is(race) ? Modding.ModRaces.Get(race)?.hostile ?? true : race > 3;
                        return fixedEnemy ? -100 : Flight.Standing.RawToward(race);
                    }
                    if (name == "visited")
                    {
                        if (!NetTeleport.ParseStation(arg, out int st, out string left) || left.Length > 0)
                            throw new Exception(string.Format(X("mpSelNoStation", "No station \"{0}\" (a number, a name or void)."), arg));
                        return Session.VisitedStations.Contains(st) ? 1 : 0;
                    }
                    int item = NetAdmin.FindItem(arg, out string error);
                    if (item < 0) throw new Exception(error ?? arg);
                    int n = 0;
                    foreach (var c in Session.Cargo) if (c.item == item) n += c.amount;
                    if (name == "has") foreach (var e in Session.Equipment) if (e.item == item) n += Math.Max(1, e.amount);
                    return n;
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
                        case "randomstation" when a.Count == 0: return RandomStation();
                    }
                    throw new Exception(string.Format("no function {0} with {1} value(s).", name, a.Count));
                }
            }
        }
    }
}
