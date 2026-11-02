// EventGraphScript.cs
// Remake events: an event graph (the Editor's .gof2event, Unity's Graph Toolkit) -> the EventRunner text script. The game
// reads the graph file itself (EventGraphFile) and the Editor hands its live graph over in the same form (EventGraphData),
// so there is one compiler: the Start node's description as "#" lines, "set" for every blackboard variable (its default),
// then the steps along the flow wires from Start, blocks indented ("if" / "else" / "end"...). Each node kind writes its
// line(s) here; values become expressions with the fewest brackets (EventRunner.Parser's precedence), a number argument of a
// command "{expression}" when wired. Problems name the node (the Editor shows them on it). Every line a node writes ends in
// "#@<node id>" so the server knows which node a step came from (the Editor's live view). Blackboard variables marked as
// inputs are the event's settings ("param": /event <name> <setting>=<value>); the "On" trigger nodes' handlers come first.
// Node kinds are the Editor's node class names (Scripts/Editor/Events/EventGraphNodes.cs), ports and options their names
// there; the enums below are the nodes' option types (stored as their index in the file).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GoF2Remake.Multiplayer;

namespace GoF2Remake.Events
{
    public enum EventScoreBy { Kills, Time, Points }
    public enum EventWinnerBy { Score, Kills, Time, Points }
    /// <summary>The On node's triggers, by EventRunner' words (TriggerWords).</summary>
    public enum EventTrigger { PlayerDestroyed, PlayerDocked, PlayerLaunched, PlayerJoined, PlayerLeft, PlayerEntersOrbit, EventShipDestroyed, EnemiesCleared, PlayerKilledPlayer, PlayerRespawned, PlayerArrives, WreckHacked, PlayerDocksAtPad }
    public enum EventRace { Maker, Terran, Vossk, Nivelian, Midorian, Pirate, Void, Specter }
    public enum EventBehaviour { Enemy, Friendly, Neutral, Standing }
    public enum EventCheat { God, Ammo, Cooldown, Boost, OneHit, Locks, Shopping, Jumps }
    public enum EventGameValue { Enemies, Ships, Players, InSpace, Docked, Dead, Time, TopPoints, MissionStation, Campaign, Credits, Rank, Station, System, Ship, Kills }
    /// <summary>The Start node's kind: an event (/event; a bar mission when it has a Mission title, as before), a bar
    /// mission, or a quest (single player, like the story: starts by itself, lasts across saves).</summary>
    public enum EventKind { Event, BarMission, StoryMission }
    /// <summary>A bar mission's Who can take it: one squad at a time in the whole universe, one per station (other stations'
    /// bars still offer it), or any number of squads (several at one station too).</summary>
    public enum EventMissionTakers { OneInUniverse, OnePerStation, Anyone }
    public enum EventMathOp { Add, Subtract, Multiply, Divide, Modulo, Min, Max }
    public enum EventCompareOp { Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual }
    public enum EventLogicOp { And, Or }
    public enum EventPlayersWho { Everyone, AliveInSpace, InSpace, Docked, Destroyed, Survivors, RandomOne }
    /// <summary>The Fade node: to the colour (held), from it, or gone at once.</summary>
    public enum EventFade { Out, In, Clear }
    /// <summary>The Set Flight Model node (/flightstyle): free flight, the original, or the player's own option again.</summary>
    public enum EventFlightModel { FreeFlight, Original, PlayersChoice }
    /// <summary>The Show / Hide System node (/system): on the map, off it, or as the map has it again.</summary>
    public enum EventSystemAction { Show, Hide, Reset }
    /// <summary>What a ship does when its Fly To / Fly Route order is done (NpcOrder.Then).</summary>
    public enum EventShipThen { Resume, Hold, Vanish, JumpOut }
    /// <summary>The Ship Action node.</summary>
    public enum EventShipAction { Hold, Resume, JumpOut, Dock, Flee }

    /// <summary>An event graph as plain data: nodes (kind, id, values by port / option name, as invariant text), wires,
    /// blackboard variables.</summary>
    public sealed class EventGraphData
    {
        public sealed class Node
        {
            public string id, kind, graph;   // graph: the (sub-)graph it is in
            public float x, y;               // its place on the canvas (m_Position)
            public readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        public struct Wire
        {
            public string fromNode, fromPort, toNode, toPort;
        }

        public const string VariableKind = "Variable", VariableName = "Name", VariableId = "VariableId";
        public const string SubgraphKind = "Subgraph", SubgraphGraph = "Graph", SubgraphAsset = "Asset", SubgraphName = "SubgraphName";

        /// <summary>The main graph's id (the other nodes' graph: local sub-graphs).</summary>
        public string mainGraph;

        public readonly List<Node> nodes = new List<Node>();
        public readonly List<Wire> wires = new List<Wire>();
        public struct Variable
        {
            public string name, id, graph;   // id: Graph Toolkit's variable id (a sub-graph node's port name)
            public double value;
            public int kind;        // 0 local, 1 input, 2 output
            public bool flow;       // a sub-graph's flow in / out
            public bool setting;    // a main-graph input: the event's setting
        }

        public readonly List<Variable> variables = new List<Variable>();
    }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static partial class EventGraphScript
    {
        public struct Problem
        {
            public string nodeId, node, message;
            public bool warning;
        }

        /// <summary>A .gof2event file's text (Unity YAML with Graph Toolkit's model)?</summary>
        public static bool IsGraph(string text) => text != null && text.TrimStart().StartsWith("%YAML") && text.Contains("GraphModelImp");

        /// <summary>A .gof2event file -> its script ("" and a problem when it can't be read).</summary>
        public static string FromFile(string yaml, List<Problem> problems = null)
        {
            problems ??= new List<Problem>();
            EventGraphData data;
            try { data = EventGraphFile.Read(yaml); }
            catch (Exception e)
            {
                problems.Add(new Problem { message = "can't read the graph file: " + e.Message });
                return "";
            }
            return Compile(data, problems);
        }

        public static string Compile(EventGraphData graph, List<Problem> problems = null) =>
            new Writer(graph, problems ?? new List<Problem>()).Run();

        /// <summary>A node kind's name for people: "EventWaitUntilNode" -> "Wait Until".</summary>
        public static string Display(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return "";
            string k = kind.StartsWith("Event") ? kind.Substring(5) : kind;
            if (k.EndsWith("Node")) k = k.Substring(0, k.Length - 4);
            var b = new StringBuilder();
            for (int i = 0; i < k.Length; i++)
            {
                if (i > 0 && char.IsUpper(k[i]) && !char.IsUpper(k[i - 1])) b.Append(' ');
                b.Append(k[i]);
            }
            return b.ToString();
        }

        public static string Number(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "0";
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }

        // The script's precedence levels (EventRunner.Parser): or, and, not, comparisons, + -, * / %, unary minus, atoms.
        public const int Or = 1, And = 2, Not = 3, Compare = 4, Sum = 5, Product = 6, Unary = 7, Atom = 8;

        static readonly HashSet<string> StateNames = new HashSet<string> { "enemies", "ships", "players", "inspace", "docked", "dead", "time" };

        [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
        sealed partial class Writer
        {
            readonly EventGraphData graph;
            readonly List<Problem> problems;
            readonly Dictionary<string, EventGraphData.Node> byId = new Dictionary<string, EventGraphData.Node>();
            readonly Dictionary<(string, string), (EventGraphData.Node node, string port)> inputs = new Dictionary<(string, string), (EventGraphData.Node, string)>();
            readonly Dictionary<(string, string), (EventGraphData.Node node, string port)> outputs = new Dictionary<(string, string), (EventGraphData.Node, string)>();
            readonly Dictionary<string, EventGraphData.Variable> variablesById = new Dictionary<string, EventGraphData.Variable>();
            readonly Stack<EventGraphData.Node> callers = new Stack<EventGraphData.Node>();   // the sub-graph nodes being inlined
            readonly HashSet<EventGraphData.Node> visited = new HashSet<EventGraphData.Node>();
            readonly HashSet<EventGraphData.Node> evaluating = new HashSet<EventGraphData.Node>();
            readonly HashSet<string> variableNames = new HashSet<string>();
            readonly StringBuilder sb = new StringBuilder();
            int indent, randomBranches;
            EventGraphData.Node marking;   // the node whose lines are being written (their "#@" marker)

            public Writer(EventGraphData graph, List<Problem> problems)
            {
                this.graph = graph;
                this.problems = problems;
                foreach (var n in graph.nodes) if (n.id != null) byId[n.id] = n;
                foreach (var w in graph.wires)
                {
                    if (!byId.TryGetValue(w.fromNode ?? "", out var from) || !byId.TryGetValue(w.toNode ?? "", out var to)) continue;
                    inputs[(w.toNode, w.toPort)] = (from, w.fromPort);
                    outputs[(w.fromNode, w.fromPort)] = (to, w.toPort);
                }
                foreach (var v in graph.variables) if (!string.IsNullOrEmpty(v.id)) variablesById[v.id] = v;
            }

            public string Run()
            {
                var starts = graph.nodes.Where(n => n.kind == "EventStartNode" && IsMain(n)).ToList();
                foreach (var n in graph.nodes)
                    if ((n.kind == "EventStartNode" || n.kind == "EventOnNode") && !IsMain(n))
                        Warning(n, "Start and On nodes only work in the main graph, not in a sub-graph.");
                if (starts.Count == 0) { Error(null, "No Start node: add one (Flow > Start)."); return ""; }
                foreach (var s in starts.Skip(1)) Error(s, "Only one Start node runs; this one is ignored.");
                var start = starts[0];

                string description = Get(start, "Description");
                if (!string.IsNullOrWhiteSpace(description))
                {
                    foreach (string line in description.Replace("\r", "").Split('\n')) Raw("# " + line.TrimEnd());
                    Raw("");
                }
                // A bar mission's details (EventRunner' "mission" lines, EventMissions).
                string missionTitle = Clean(start, Get(start, "MissionTitle", false));
                var kind = (EventKind)(int.TryParse(Get(start, "Kind", false), out int kindValue) ? kindValue : 0);   // graphs from before the option: an event
                if (kind == EventKind.BarMission && missionTitle.Length == 0) Error(start, "A bar mission needs its Mission title (what the visitor offers).");
                if (kind == EventKind.StoryMission)
                {
                    // A quest (EventRunner: "mission kind quest"): its title in the Missions window, when it starts.
                    if (missionTitle.Length > 0) Raw("mission title " + missionTitle);
                    Raw("mission kind quest");
                    string startsWhen = Clean(start, Get(start, "StartsWhen", false));
                    if (startsWhen.Length > 0) Raw("mission starts " + startsWhen);
                    double questReward = Num(start, "MissionReward");
                    if (questReward > 0) Raw("mission reward " + Number(questReward));
                    double repeat = start.values.ContainsKey("RepeatMinutes") ? Num(start, "RepeatMinutes") : 0;   // (graphs from before the option)
                    if (repeat > 0) Raw("mission repeat " + Number(repeat));
                    Raw("");
                }
                else if (missionTitle.Length > 0)
                {
                    if (kind == EventKind.BarMission) Raw("mission kind mission");
                    Raw("mission title " + missionTitle);
                    string offer = (Get(start, "MissionOffer", false) ?? "").Replace("\r", "").Trim().Replace("\n", "\\n");
                    if (offer.IndexOf('#') >= 0) Warning(start, "\"#\" starts a comment in the script: the offer text is cut there.");
                    if (offer.Length > 0) Raw("mission offer " + offer);
                    string client = Clean(start, Get(start, "MissionClient", false));
                    if (client.Length > 0) Raw("mission client " + client);
                    double reward = Num(start, "MissionReward");
                    if (reward > 0) Raw("mission reward " + Number(reward));
                    int min = Math.Max(1, Int(start, "MinPlayers")), max = Math.Max(min, Int(start, "MaxPlayers"));
                    if (!start.values.ContainsKey("MinPlayers")) min = 1;
                    if (!start.values.ContainsKey("MaxPlayers")) max = Math.Max(min, 4);
                    Raw($"mission players {min} {max}");
                    if (start.values.ContainsKey("MissionChance"))
                        Raw("mission chance " + Math.Max(0, Math.Min(100, (int)Math.Round(Num(start, "MissionChance")))));
                    var takers = (EventMissionTakers)(int.TryParse(Get(start, "MissionTakers", false), out int takersValue) ? takersValue : 0);
                    if (takers == EventMissionTakers.OnePerStation) Raw("mission takers station");
                    else if (takers == EventMissionTakers.Anyone) Raw("mission takers any");
                    string stations = Clean(start, Get(start, "MissionStations", false));
                    if (stations.Length > 0)
                    {
                        foreach (string st in stations.Split(',')) if (st.Trim().Length > 0) CheckName(start, st.Trim(), EventNames.CheckStation);
                        Raw("mission stations " + stations);
                    }
                    Raw("");
                }
                if (kind != EventKind.StoryMission && missionTitle.Length == 0
                    && graph.nodes.Any(n => n.kind == "EventCheckpointNode" || n.kind == "EventSetObjectiveNode" || n.kind == "EventQuestOrbitNode"))
                    Warning(start, "Checkpoints, objectives and mission orbits only matter in story and bar missions (the Start node's Kind).");
                if (missionTitle.Length == 0 && kind != EventKind.StoryMission && graph.nodes.Any(n => n.kind == "EventMissionCompleteNode" || n.kind == "EventMissionFailedNode"))
                    Warning(start, "Mission Complete / Failed without a Mission title: the event isn't offered in the bars (in an /event they end it for everyone).");
                int declared = 0;
                foreach (var v in graph.variables)
                {
                    // A sub-graph's inputs / outputs and the flows aren't script variables: its locals are (shared names).
                    if (v.flow || (v.kind != 0 && !v.setting)) continue;
                    string name = VariableName(v.name, start);
                    variableNames.Add(name);
                    Line($"{(v.setting ? "param" : "set")} {name} = {Number(v.value)}");
                    declared++;
                }
                if (declared > 0) Raw("");
                // The handlers first: they listen from the start.
                foreach (var on in graph.nodes)
                {
                    if (on.kind != "EventOnNode" || !IsMain(on)) continue;
                    visited.Add(on);
                    marking = on;
                    var trigger = (EventTrigger)Int(on, "Trigger");
                    string station = inputs.TryGetValue((on.id, "Orbit"), out var orbit)
                        ? (orbit.node.kind == "EventGetOrbitNode" ? Orbit(orbit.node) : Fail(on, "Orbit: only a Get Orbit node goes here."))
                        : "";
                    if (station.Length > 0 && trigger == EventTrigger.EnemiesCleared)
                        Warning(on, "Enemies Cleared isn't about an orbit: the orbit is ignored.");
                    string near = "";
                    if (trigger == EventTrigger.PlayerArrives)
                    {
                        string at = Text(on, "At", false);
                        if (station.Length == 0 || at.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length != 3)
                            Error(on, "Player Arrives needs an Orbit (Get Orbit) and At: the spot's game coordinates \"x y z\".");
                        near = " " + at + " " + Arg(on, "Radius");
                    }
                    Line(("on " + TriggerWords[(int)trigger] + " " + (trigger == EventTrigger.EnemiesCleared ? "" : station) + near).TrimEnd());
                    if (!Wired(on, "Do")) Warning(on, "Nothing on Do: the trigger does nothing.");
                    Block(on, "Do");
                    marking = on;
                    Line("end");
                    marking = null;
                }
                Chain(start, "Next");

                foreach (var n in graph.nodes)
                    if (IsStep(n.kind) && n != start && !visited.Contains(n)) Warning(n, "Not connected to Start or an On node: never runs.");
                return sb.ToString();
            }

            // ---- problems and output ----

            void Error(EventGraphData.Node node, string message) =>
                problems.Add(new Problem { nodeId = node?.id, node = node != null ? Display(node.kind) : null, message = message });

            void Warning(EventGraphData.Node node, string message) =>
                problems.Add(new Problem { nodeId = node?.id, node = node != null ? Display(node.kind) : null, message = message, warning = true });

            void Raw(string line) => sb.Append(line).Append('\n');

            void Line(string line)
            {
                sb.Append(' ', indent * 2).Append(line.TrimEnd());
                if (marking != null && !line.StartsWith("#")) sb.Append("  #@").Append(marking.id);
                sb.Append('\n');
            }

            string Fail(EventGraphData.Node n, string message)
            {
                Error(n, message);
                return "";
            }

            /// <summary>EventRunner' trigger words, by EventTrigger.</summary>
            static readonly string[] TriggerWords = { "died", "docked", "launched", "joined", "left", "entered", "kill", "cleared", "pvpkill", "respawned", "near", "hacked", "pad" };

            // ---- the steps ----

            static readonly HashSet<string> ValueKinds = new HashSet<string>
            {
                "EventStateNode", "EventMathNode", "EventRandomNode", "EventFloorNode", "EventCompareNode", "EventLogicNode", "EventNotNode", "EventExpressionNode",
                "EventGetOrbitNode", "EventGetPlayersNode", "EventCountPlayersNode",
            };

            // ---- values ----

            readonly HashSet<(string, string)> checkedNames = new HashSet<(string, string)>();

            static readonly HashSet<string> ConditionPorts = new HashSet<string> { "Condition" };

        }
    }
}
