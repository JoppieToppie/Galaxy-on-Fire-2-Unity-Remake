// EventGraphScript.cs
// Remake multiplayer: an event graph (the Editor's .gof2netevent, Unity's Graph Toolkit) -> the NetEvents text script. The game
// reads the graph file itself (EventGraphFile) and the Editor hands its live graph over in the same form (EventGraphData),
// so there is one compiler: the Start node's description as "#" lines, "set" for every blackboard variable (its default),
// then the steps along the flow wires from Start, blocks indented ("if" / "else" / "end"...). Each node kind writes its
// line(s) here; values become expressions with the fewest brackets (NetEvents.Parser's precedence), a number argument of a
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

namespace GoF2Remake.Multiplayer
{
    public enum EventScoreBy { Kills, Time, Points }
    public enum EventWinnerBy { Score, Kills, Time, Points }
    /// <summary>The On node's triggers, by NetEvents' words (TriggerWords).</summary>
    public enum EventTrigger { PlayerDestroyed, PlayerDocked, PlayerLaunched, PlayerJoined, PlayerLeft, PlayerEntersOrbit, EventShipDestroyed, EnemiesCleared, PlayerKilledPlayer, PlayerRespawned, PlayerArrives }
    public enum EventRace { Maker, Terran, Vossk, Nivelian, Midorian, Pirate, Void, Specter }
    public enum EventBehaviour { Enemy, Friendly, Neutral, Standing }
    public enum EventCheat { God, Ammo, Cooldown, Boost, OneHit, Locks, Shopping, Jumps }
    public enum EventGameValue { Enemies, Ships, Players, InSpace, Docked, Dead, Time, TopPoints, MissionStation }
    public enum EventMathOp { Add, Subtract, Multiply, Divide, Modulo, Min, Max }
    public enum EventCompareOp { Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual }
    public enum EventLogicOp { And, Or }
    public enum EventPlayersWho { Everyone, AliveInSpace, InSpace, Docked, Destroyed, Survivors, RandomOne }

    /// <summary>An event graph as plain data: nodes (kind, id, values by port / option name, as invariant text), wires,
    /// blackboard variables.</summary>
    public sealed class EventGraphData
    {
        public sealed class Node
        {
            public string id, kind, graph;   // graph: the (sub-)graph it is in
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
    public static class EventGraphScript
    {
        public struct Problem
        {
            public string nodeId, node, message;
            public bool warning;
        }

        /// <summary>A .gof2netevent file's text (Unity YAML with Graph Toolkit's model)?</summary>
        public static bool IsGraph(string text) => text != null && text.TrimStart().StartsWith("%YAML") && text.Contains("GraphModelImp");

        /// <summary>A .gof2netevent file -> its script ("" and a problem when it can't be read).</summary>
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

        // The script's precedence levels (NetEvents.Parser): or, and, not, comparisons, + -, * / %, unary minus, atoms.
        public const int Or = 1, And = 2, Not = 3, Compare = 4, Sum = 5, Product = 6, Unary = 7, Atom = 8;

        static readonly HashSet<string> StateNames = new HashSet<string> { "enemies", "ships", "players", "inspace", "docked", "dead", "time" };

        [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
        sealed class Writer
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
                // A bar mission's details (NetEvents' "mission" lines, NetEventMissions).
                string missionTitle = Clean(start, Get(start, "MissionTitle", false));
                if (missionTitle.Length > 0)
                {
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
                    string stations = Clean(start, Get(start, "MissionStations", false));
                    if (stations.Length > 0)
                    {
                        foreach (string st in stations.Split(',')) if (st.Trim().Length > 0) CheckName(start, st.Trim(), EventNames.CheckStation);
                        Raw("mission stations " + stations);
                    }
                    Raw("");
                }
                else if (graph.nodes.Any(n => n.kind == "EventMissionCompleteNode" || n.kind == "EventMissionFailedNode"))
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

            /// <summary>NetEvents' trigger words, by EventTrigger.</summary>
            static readonly string[] TriggerWords = { "died", "docked", "launched", "joined", "left", "entered", "kill", "cleared", "pvpkill", "respawned", "near" };

            // ---- the steps ----

            static bool IsStep(string kind) => kind != null && kind.StartsWith("Event") && !ValueKinds.Contains(kind);

            static readonly HashSet<string> ValueKinds = new HashSet<string>
            {
                "EventStateNode", "EventMathNode", "EventRandomNode", "EventFloorNode", "EventCompareNode", "EventLogicNode", "EventNotNode", "EventExpressionNode",
                "EventGetOrbitNode", "EventGetPlayersNode", "EventCountPlayersNode",
            };

            bool IsMain(EventGraphData.Node n) => graph.mainGraph == null || n.graph == null || n.graph == graph.mainGraph;

            EventGraphData.Node Following(EventGraphData.Node node, string exit) => outputs.TryGetValue((node.id, exit), out var n) ? n.node : null;
            bool Wired(EventGraphData.Node node, string exit) => outputs.ContainsKey((node.id, exit));

            void Chain(EventGraphData.Node from, string exit)
            {
                var node = Following(from, exit);
                while (node != null)
                {
                    if (node.kind == EventGraphData.SubgraphKind)
                    {
                        if (!visited.Add(node)) { Error(node, "Reached from two places: a step can follow only one other."); return; }
                        EnterSubgraph(node, outputs[(from.id, exit)].port);
                        return;   // the flow goes on from the sub-graph's flow output (LeaveSubgraph)
                    }
                    if (node.kind == EventGraphData.VariableKind)
                    {
                        LeaveSubgraph(node);
                        return;
                    }
                    if (!visited.Add(node)) { Error(node, "Reached from two places: a step can follow only one other."); return; }
                    if (!IsStep(node.kind)) { Error(node, "Not a step: only the Flow, Event and Command nodes go on the white arrows."); return; }
                    if (node.kind == "EventOnNode") { Error(node, "An On node starts its own flow: nothing leads into it."); return; }
                    var outer = marking;
                    marking = node;
                    Emit(node);
                    marking = outer;
                    from = node;
                    exit = "Next";
                    node = Following(node, "Next");
                }
            }

            /// <summary>A local sub-graph inlined: its flow from the input variable the flow came in by.</summary>
            void EnterSubgraph(EventGraphData.Node sub, string inPort)
            {
                if (!string.IsNullOrEmpty(Get(sub, EventGraphData.SubgraphAsset, false)))
                {
                    Error(sub, "A sub-graph saved as its own asset can't run on a server: use a local sub-graph (inside this graph).");
                    return;
                }
                if (callers.Contains(sub) || callers.Count > 16) { Error(sub, "A sub-graph inside itself."); return; }
                string subGraph = Get(sub, EventGraphData.SubgraphGraph, false);
                var entry = graph.nodes.FirstOrDefault(n => n.graph == subGraph && n.kind == EventGraphData.VariableKind
                                                            && Get(n, EventGraphData.VariableId, false) == inPort);
                if (entry == null) { Warning(sub, "Nothing inside the sub-graph uses that flow input."); return; }
                var exit = outputs.Keys.FirstOrDefault(k => k.Item1 == entry.id);
                if (exit.Item1 == null) { Warning(sub, "The sub-graph's flow input leads nowhere."); return; }
                callers.Push(sub);
                Chain(entry, exit.Item2);
                callers.Pop();
            }

            /// <summary>The flow reached a sub-graph's flow output: it goes on after the sub-graph's node, from that output.</summary>
            void LeaveSubgraph(EventGraphData.Node outNode)
            {
                string id = Get(outNode, EventGraphData.VariableId, false);
                if (callers.Count == 0 || !variablesById.TryGetValue(id, out var v) || !v.flow || v.kind != 2)
                {
                    Error(outNode, "Not a step: only a sub-graph's flow output ends its flow here.");
                    return;
                }
                var caller = callers.Pop();
                Chain(caller, id);
                callers.Push(caller);
            }

            void Block(EventGraphData.Node from, string exit)
            {
                var outer = marking;
                indent++;
                Chain(from, exit);
                indent--;
                marking = outer;
            }

            static string Join(params string[] parts) => string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));

            void Emit(EventGraphData.Node n)
            {
                switch (n.kind)
                {
                    case "EventCommentNode":
                        foreach (string line in Get(n, "Text").Replace("\r", "").Split('\n')) Line("# " + line.Trim());
                        break;
                    case "EventWaitNode":
                        Line("wait " + Expr(n, "Seconds"));
                        break;
                    case "EventWaitUntilNode":
                    {
                        string line = "wait until " + Expr(n, "Condition");
                        if (IsWired(n, "Timeout") || Num(n, "Timeout") > 0) line += " timeout " + Expr(n, "Timeout");
                        Line(line);
                        break;
                    }
                    case "EventIfNode":
                        Line("if " + Expr(n, "Condition"));
                        Block(n, "Then");
                        if (Wired(n, "Else")) { Line("else"); Block(n, "Else"); }
                        Line("end");
                        break;
                    case "EventWhileNode":
                        Line("while " + Expr(n, "Condition"));
                        Block(n, "Body");
                        Line("end");
                        break;
                    case "EventRepeatNode":
                    {
                        string counter = Get(n, "Counter");
                        Line("repeat " + Expr(n, "Count") + (string.IsNullOrWhiteSpace(counter) ? "" : " as " + VariableName(counter, n)));
                        Block(n, "Body");
                        Line("end");
                        break;
                    }
                    case "EventSetNode":
                    {
                        string name = VariableName(Get(n, "Variable"), n);
                        if (!variableNames.Contains(name)) Warning(n, $"No blackboard variable \"{name}\": nothing can read it.");
                        Line($"set {name} = {Expr(n, "Value")}");
                        break;
                    }
                    case "EventStopNode":
                        Line("stop");
                        break;
                    case "EventMissionCompleteNode":
                    {
                        string title = Text(n, "Title", false);
                        string reward = IsWired(n, "Reward") ? Expr(n, "Reward") : Num(n, "Reward") >= 0 ? Number(Num(n, "Reward")) : "";
                        Line(("complete " + reward).TrimEnd() + (title.Length > 0 ? " | " + title : ""));
                        break;
                    }
                    case "EventMissionFailedNode":
                        Line(("fail " + Text(n, "Title", false)).TrimEnd());
                        break;
                    case "EventWaitForeverNode":
                        Line("wait until 0");
                        break;
                    case "EventParallelNode":
                        Line("parallel");
                        if (!Wired(n, "Branch")) Warning(n, "Nothing on Branch.");
                        Block(n, "Branch");
                        Line("end");
                        break;
                    case "EventEveryNode":
                        Line("every " + Expr(n, "Seconds"));
                        if (!Wired(n, "Body")) Warning(n, "Nothing on Body.");
                        Block(n, "Body");
                        Line("end");
                        break;
                    case "EventRandomBranchNode":
                        RandomBranch(n);
                        break;
                    case "EventVoteNode":
                    case "EventAskNode":
                    {
                        bool vote = n.kind == "EventVoteNode";
                        string exit = vote ? "Choice " : "Answer ";
                        var answers = new List<string>();
                        for (int k = 1; k <= 4; k++)
                        {
                            string a = Text(n, "Answer " + k);
                            if (a.Length == 0) break;
                            answers.Add(a);
                        }
                        string question = Text(n, "Question"), speaker = Text(n, "Speaker", false);
                        if (question.Length == 0) Error(n, "Ask needs a question.");
                        if (speaker.Length > 0) question = speaker + " : " + question;
                        if (answers.Count < 2) Error(n, "Ask needs at least 2 answers (Answer 1, 2...).");
                        if ((question + string.Join("", answers)).IndexOf('|') >= 0) Error(n, "A question or answer can't hold |.");
                        for (int k = answers.Count + 1; k <= 4; k++)
                            if (Wired(n, exit + k)) Warning(n, $"Answer {k} has no text: its branch never runs.");
                        Line(Join(vote ? "vote" : "ask", Text(n, "Players"), Arg(n, "Seconds")) + " | " + question + " | " + string.Join(" | ", answers));
                        for (int k = 1; k <= answers.Count; k++)
                        {
                            if (!Wired(n, exit + k)) continue;
                            indent++;
                            Line((vote ? "choice " : "answer ") + k);
                            Block(n, exit + k);
                            Line("end");
                            indent--;
                        }
                        if (vote && Wired(n, "No Votes"))
                        {
                            indent++;
                            Line("choice 0");
                            Block(n, "No Votes");
                            Line("end");
                            indent--;
                        }
                        Line("end");
                        break;
                    }
                    case "EventAddPointsNode":
                        Line(Join("points", Text(n, "Players"), Expr(n, "Amount")));
                        break;
                    case "EventScoreboardNode":
                        Line(Bool(n, "Show") ? Join("scoreboard on", Text(n, "Title")) : "scoreboard off");
                        break;
                    case "EventPlaySoundNode":
                        Line(Join("sound", Text(n, "Players"), ((EventSound)Int(n, "Sound")).ToString().ToLowerInvariant()));
                        break;
                    case "EventPlayMusicNode":
                        Line(Join("music", Text(n, "Players"), ((EventMusic)Int(n, "Music")).ToString().ToLowerInvariant()));
                        break;
                    case "EventStopMusicNode":
                        Line(Join("music", Text(n, "Players"), "stop"));
                        break;
                    case "EventStartEventNode":
                    {
                        string name = Text(n, "Event");
                        if (name.Length == 0 || name.Contains(" ")) Error(n, "Start Event needs an event's name (one word, the graph's file name).");
                        Line(Join("startevent", name, Text(n, "Settings")));
                        if (Wired(n, "Next")) Warning(n, "Nothing after Start Event runs: this event ends there.");
                        break;
                    }
                    case "EventFreeForAllNode":
                        Line("pvp " + (Bool(n, "On") ? "on" : "off"));
                        break;
                    case "EventSetRespawnNode":
                    {
                        string station = inputs.TryGetValue((n.id, "Orbit"), out var orbit) && orbit.node.kind == "EventGetOrbitNode" ? Orbit(orbit.node) : "";
                        if (station.Length == 0) Error(n, "Wire a Get Orbit into Orbit: where the ships come back.");
                        Line(Join("respawn", Text(n, "Players"), station, Text(n, "At"), "spread " + Arg(n, "Spread"), "delay " + Arg(n, "Delay")));
                        break;
                    }
                    case "EventMakeHostileNode":
                    {
                        var race = (EventRace)Int(n, "Race");
                        string within = IsWired(n, "Within") || Num(n, "Within") > 0 ? "within " + Arg(n, "Within") : "";
                        Line(Join("provoke", Text(n, "Players"), race == EventRace.Maker ? "" : race.ToString().ToLowerInvariant(), within));
                        break;
                    }
                    case "EventSetWaypointNode":
                    {
                        string station = inputs.TryGetValue((n.id, "Orbit"), out var orbit) && orbit.node.kind == "EventGetOrbitNode" ? Orbit(orbit.node) : "";
                        string at = Text(n, "At");
                        if (station.Length == 0) Error(n, "Wire a Get Orbit into Orbit: the waypoint's orbit.");
                        if (at.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length != 3) Error(n, "At: the waypoint's game coordinates \"x y z\".");
                        Line(Join("waypoint", Text(n, "Players"), station, at));
                        break;
                    }
                    case "EventClearWaypointNode":
                        Line(Join("waypoint", Text(n, "Players"), "off"));
                        break;
                    case "EventClearRespawnNode":
                        Line(Join("respawn", Text(n, "Players"), "off"));
                        break;
                    case "EventRestrictTravelNode":
                    {
                        bool jumps = Bool(n, "NoJumps"), docking = Bool(n, "NoDocking");
                        Line(Join("restrict", Text(n, "Players"), jumps ? "jumps" : "", docking ? "docking" : "", !jumps && !docking ? "off" : ""));
                        break;
                    }
                    case "EventScoreNode":
                        Line("score " + ((EventScoreBy)Int(n, "By")).ToString().ToLowerInvariant());
                        break;
                    case "EventWinnerNode":
                    {
                        var by = (EventWinnerBy)Int(n, "By");
                        Line(by == EventWinnerBy.Score ? "winner" : "winner " + by.ToString().ToLowerInvariant());
                        break;
                    }

                    case "EventTitleNode":
                    {
                        string title = Text(n, "Title"), sub = Text(n, "Subtitle"), players = Text(n, "Players");
                        if (title.Length == 0 && sub.Length == 0) { Line(Join("title", players, "clear")); break; }
                        string line = Join("title", players, title);
                        if (sub.Length > 0) line += " | " + sub;
                        if (IsWired(n, "Seconds") || Num(n, "Seconds") > 0) line += " for " + Arg(n, "Seconds");
                        Line(line);
                        break;
                    }
                    case "EventTimerNode":
                        Line(Join("timer", Text(n, "Players"), Arg(n, "Seconds"), Text(n, "Label")));
                        break;
                    case "EventStopTimerNode":
                        Line(Join("timer", Text(n, "Players"), "stop"));
                        break;
                    case "EventSpawnNode":
                    {
                        string ship = Text(n, "Ship");
                        if (ship.Length == 0) Error(n, "Spawn needs a ship.");
                        else CheckName(n, ship, v => EventNames.CheckShip(v, true));
                        var race = (EventRace)Int(n, "Race");
                        string count = IsWired(n, "Count") || Math.Abs(Num(n, "Count") - 1) > 1e-4 ? Arg(n, "Count") : "";
                        string at = Text(n, "At");
                        bool scenery = false;
                        try { scenery = ship.IndexOf('{') < 0 && EventNames.IsObject(ship); }
                        catch (Exception) { }   // no game data here
                        if (scenery)   // an object: /spawn takes its whole name (no race, count or behaviour)
                            Line(Join("spawn", Text(n, "Players"), ship, at.Length > 0 ? "at " + at : ""));
                        else
                            Line(Join("spawn", Text(n, "Players"), ship, race == EventRace.Maker ? "" : race.ToString().ToLowerInvariant(), count,
                                ((EventBehaviour)Int(n, "Behaviour")).ToString().ToLowerInvariant(), at.Length > 0 ? "at " + at : ""));
                        break;
                    }
                    case "EventRewardNode":
                    {
                        string credits = IsWired(n, "Credits") || Num(n, "Credits") != 0 ? Arg(n, "Credits") : "";
                        string items = Text(n, "Items"), title = Text(n, "Title");
                        foreach (string part in items.Split('+'))
                        {
                            // "<item> [amount]": the amount off the end.
                            string item = part.Trim();
                            int sp = item.LastIndexOf(' ');
                            if (sp > 0 && int.TryParse(item.Substring(sp + 1), out _)) item = item.Substring(0, sp).Trim();
                            if (item.Length > 0) CheckName(n, item, EventNames.CheckItem);
                        }
                        string what = credits.Length > 0 && items.Length > 0 ? credits + " + " + items : credits + items;
                        if (what.Length == 0) Error(n, "A reward needs credits or items.");
                        Line(Join("reward", Text(n, "Players"), what) + (title.Length > 0 ? " | " + title : ""));
                        break;
                    }
                    case "EventHealNode": Line(Join("heal", Text(n, "Players"))); break;
                    case "EventAmmoNode": Line(Join("ammo", Text(n, "Players"))); break;
                    case "EventKillNode": Line(Join("kill", Text(n, "Players"))); break;
                    case "EventRevealNode": Line(Join("reveal", Text(n, "Players"))); break;
                    case "EventPeaceNode": Line(Join("peace", Text(n, "Players"))); break;
                    case "EventGiveNode":
                        CheckName(n, Text(n, "Item"), EventNames.CheckItem);
                        Line(Join("give", Text(n, "Players"), Text(n, "Item"), Arg(n, "Amount"), Bool(n, "Mount") ? "mount" : ""));
                        break;
                    case "EventCreditsNode":
                        Line(Join("credits", Text(n, "Players"), Arg(n, "Amount")));
                        break;
                    case "EventTeleportNode":
                        Line(Join("tp", Text(n, "Players"), Text(n, "Destination")));
                        break;
                    case "EventShipNode":
                        CheckName(n, Text(n, "Ship"), EventNames.CheckHull);
                        Line(Join("ship", Text(n, "Players"), Text(n, "Ship")));
                        break;
                    case "EventCheatNode":
                        Line(Join("cheat", Text(n, "Players"), ((EventCheat)Int(n, "Flag")).ToString().ToLowerInvariant(), Bool(n, "On") ? "on" : "off"));
                        break;
                    case "EventRadioNode":
                    {
                        string speaker = Text(n, "Speaker"), text = Text(n, "Text");
                        if (speaker.Length == 0) Error(n, "Radio needs a speaker (a story character, a race and a name, or player).");
                        if (text.Length == 0) Error(n, "Radio needs a text.");
                        Line(Join("radio", Text(n, "Players"), speaker) + " : " + text);
                        break;
                    }
                    case "EventChatNode":
                        Line("g " + Text(n, "Text"));
                        break;
                    case "EventDialogNode":
                    {
                        var pages = new List<string>();
                        foreach (string line in Get(n, "Pages").Replace("\r", "").Split('\n'))
                            if (line.Trim().Length > 0) pages.Add(Clean(n, line));
                        if (pages.Count == 0) Error(n, "A dialog needs a page.");
                        Line(Join("dialog", Text(n, "Players"), string.Join(" | ", pages)));
                        break;
                    }
                    case "EventRawCommandNode":
                    {
                        string line = Clean(n, Get(n, "Line"));
                        if (line.StartsWith("/")) line = line.Substring(1);
                        if (line.Length == 0) Warning(n, "An empty command does nothing.");
                        else Line(line);
                        break;
                    }
                    case "EventStartNode":
                        Error(n, "Only one Start node runs; this one is ignored.");
                        break;
                    default:
                        Error(n, $"Unknown node \"{n.kind}\" (made by a newer version?).");
                        break;
                }
            }

            // ---- values ----

            /// <summary>Random Branch: one of its wired options by weight: set _rbN = random(1, total), then nested ifs on the
            /// running sums.</summary>
            void RandomBranch(EventGraphData.Node n)
            {
                var options = new List<(string exit, string weight)>();
                for (int k = 1; k <= 4; k++)
                    if (Wired(n, "Option " + k)) options.Add(("Option " + k, Operand(n, "Weight " + k, Sum)));
                if (options.Count == 0) { Warning(n, "No option is wired."); return; }
                if (options.Count == 1) { Chain(n, options[0].exit); return; }
                string v = "_rb" + ++randomBranches;
                Line($"set {v} = random(1, {string.Join(" + ", options.ConvertAll(o => o.weight))})");
                string sum = "";
                int opened = 0;
                for (int k = 0; k < options.Count; k++)
                {
                    sum = sum.Length == 0 ? options[k].weight : sum + " + " + options[k].weight;
                    if (k < options.Count - 1)
                    {
                        Line($"if {v} <= {sum}");
                        Block(n, options[k].exit);
                        Line("else");
                        indent++;
                        opened++;
                    }
                    else Chain(n, options[k].exit);
                }
                for (int k = 0; k < opened; k++)
                {
                    indent--;
                    Line("end");
                }
            }

            /// <summary>A typed name the server wouldn't know: a warning on the node (not for {expressions} or %trigger%).</summary>
            void CheckName(EventGraphData.Node n, string value, Func<string, string> check)
            {
                if (string.IsNullOrWhiteSpace(value) || value.IndexOf('{') >= 0 || value.IndexOf('%') >= 0) return;
                string why;
                try { why = check(value.Trim()); }
                catch (Exception) { return; }   // no game data here
                if (why != null && checkedNames.Add((n.id, value))) Warning(n, why);
            }

            readonly HashSet<(string, string)> checkedNames = new HashSet<(string, string)>();

            /// <summary>A port's or option's stored value; a missing one is a name the Editor's nodes don't have (a bug).</summary>
            string Get(EventGraphData.Node n, string name, bool required = true)
            {
                if (n.values.TryGetValue(name, out string v)) return v ?? "";
                if (required) Error(n, $"No value \"{name}\" in the file.");
                return "";
            }

            double Num(EventGraphData.Node n, string name) =>
                double.TryParse(Get(n, name), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;

            int Int(EventGraphData.Node n, string name) => (int)Num(n, name);

            bool Bool(EventGraphData.Node n, string name)
            {
                string v = Get(n, name).Trim();
                return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
            }

            bool IsWired(EventGraphData.Node n, string port) => inputs.ContainsKey((n.id, port));

            static readonly HashSet<string> ConditionPorts = new HashSet<string> { "Condition" };

            /// <summary>An input's expression: the wired node's, else the port's own value (a condition port's as 1 / 0).</summary>
            string Expr(EventGraphData.Node n, string port) => Expr(n, port, out _);

            string Expr(EventGraphData.Node n, string port, out int precedence)
            {
                precedence = Atom;
                if (!inputs.TryGetValue((n.id, port), out var source))
                {
                    if (IsCondition(n, port)) return Bool(n, port) ? "1" : "0";
                    double v = Num(n, port);
                    if (v < 0) precedence = Unary;
                    return Number(v);
                }
                var from = source.node;
                if (from.kind == EventGraphData.VariableKind)
                {
                    // A sub-graph's input: what is wired into (or typed on) the sub-graph's node, read where that node is.
                    string vid = Get(from, EventGraphData.VariableId, false);
                    if (variablesById.TryGetValue(vid, out var v) && v.kind == 1 && !v.setting && callers.Count > 0)
                    {
                        var caller = callers.Pop();
                        string e = inputs.ContainsKey((caller.id, vid)) || caller.values.ContainsKey(vid) ? Expr(caller, vid, out precedence) : Number(v.value);
                        callers.Push(caller);
                        return e;
                    }
                    if (variablesById.TryGetValue(vid, out v) && v.kind == 2 && !v.flow) Warning(from, "A sub-graph's number output isn't supported: use a variable.");
                    return VariableName(Get(from, EventGraphData.VariableName), n);
                }
                if (!ValueKinds.Contains(from.kind)) { Error(n, $"{port}: can't use what is wired into it."); return "0"; }
                if (!evaluating.Add(from)) { Error(from, "A loop of value nodes."); return "0"; }
                string text = Value(from, out precedence);
                evaluating.Remove(from);
                return text;
            }

            /// <summary>The bool inputs: the steps' Condition ports and the Logic / Not nodes' inputs.</summary>
            static bool IsCondition(EventGraphData.Node n, string port) =>
                ConditionPorts.Contains(port) || n.kind == "EventLogicNode" || n.kind == "EventNotNode";

            string Operand(EventGraphData.Node n, string port, int need)
            {
                string e = Expr(n, port, out int p);
                return p < need ? "(" + e + ")" : e;
            }

            string Value(EventGraphData.Node n, out int precedence)
            {
                switch (n.kind)
                {
                    case "EventStateNode":
                        precedence = Atom;
                        return ((EventGameValue)Int(n, "State")).ToString().ToLowerInvariant();
                    case "EventMathNode":
                    {
                        var op = (EventMathOp)Int(n, "Op");
                        if (op == EventMathOp.Min || op == EventMathOp.Max)
                        {
                            precedence = Atom;
                            return $"{op.ToString().ToLowerInvariant()}({Expr(n, "A")}, {Expr(n, "B")})";
                        }
                        precedence = op == EventMathOp.Add || op == EventMathOp.Subtract ? Sum : Product;
                        string symbol = op switch { EventMathOp.Add => "+", EventMathOp.Subtract => "-", EventMathOp.Multiply => "*", EventMathOp.Divide => "/", _ => "%" };
                        return $"{Operand(n, "A", precedence)} {symbol} {Operand(n, "B", precedence + 1)}";
                    }
                    case "EventRandomNode":
                        precedence = Atom;
                        return $"random({Expr(n, "Min")}, {Expr(n, "Max")})";
                    case "EventFloorNode":
                        precedence = Atom;
                        return $"floor({Expr(n, "Input")})";
                    case "EventCompareNode":
                    {
                        precedence = Compare;
                        string symbol = (EventCompareOp)Int(n, "Op") switch
                        {
                            EventCompareOp.Equal => "==", EventCompareOp.NotEqual => "!=", EventCompareOp.Less => "<",
                            EventCompareOp.LessOrEqual => "<=", EventCompareOp.Greater => ">", _ => ">=",
                        };
                        return $"{Operand(n, "A", Sum)} {symbol} {Operand(n, "B", Sum)}";
                    }
                    case "EventLogicNode":
                    {
                        bool and = (EventLogicOp)Int(n, "Op") == EventLogicOp.And;
                        precedence = and ? And : Or;
                        return $"{Operand(n, "A", precedence)} {(and ? "and" : "or")} {Operand(n, "B", precedence + 1)}";
                    }
                    case "EventNotNode":
                        precedence = Not;
                        return "not " + Operand(n, "Input", Not);
                    case "EventCountPlayersNode":
                        precedence = Atom;
                        return $"count({Text(n, "Players")})";
                    case "EventOnNode":
                    case "EventGetOrbitNode":
                    case "EventGetPlayersNode":
                        precedence = Atom;
                        Error(n, "An orbit or players aren't a number: wire them into a command's Players (or Teleport's Destination).");
                        return "0";
                    case "EventExpressionNode":
                    {
                        precedence = Or;
                        string text = Clean(n, Get(n, "Text"));
                        if (text.Length == 0) { Error(n, "An empty expression."); return "0"; }
                        return text;
                    }
                }
                precedence = Atom;
                Error(n, $"Unknown value node \"{n.kind}\".");
                return "0";
            }

            /// <summary>A number argument of a command: the port's number, or "{expression}" when wired.</summary>
            string Arg(EventGraphData.Node n, string port) => IsWired(n, port) ? "{" + Expr(n, port) + "}" : Number(Num(n, port));

            /// <summary>A text input on one line; wired: Get Players' selector, Get Orbit's station, a number "{expression}".</summary>
            string Text(EventGraphData.Node n, string port, bool required = true)
            {
                if (!inputs.TryGetValue((n.id, port), out var source)) return Clean(n, Get(n, port, required));
                switch (source.node.kind)
                {
                    case "EventGetPlayersNode": return Players(source.node);
                    case "EventGetOrbitNode": return Orbit(source.node);
                    case "EventOnNode": return source.port == "Name" ? "%trigger%" : source.port == "Victim" ? "%victim%" : "@trigger";
                    case "EventAskNode": return source.port == "Name" ? "%trigger%" : "@trigger";   // the answering player
                }
                return "{" + Expr(n, port) + "}";
            }

            /// <summary>Get Orbit: its station (a number, a name, "void", or a wired number as "{expression}").</summary>
            string Orbit(EventGraphData.Node n)
            {
                string station = Text(n, "Station");
                if (station.Length == 0) Error(n, "Which station's orbit? (a number or a name)");
                else CheckName(n, station, EventNames.CheckStation);
                if (station.IndexOfAny(new[] { ']', ',' }) >= 0) Error(n, "A station can't hold ] or ,.");
                return station;
            }

            /// <summary>Get Players: a selector (NetCommands.FindTargets), narrowed to a wired orbit by [orbit=...].</summary>
            string Players(EventGraphData.Node n)
            {
                string selector = (EventPlayersWho)Int(n, "Who") switch
                {
                    EventPlayersWho.AliveInSpace => "@alive", EventPlayersWho.InSpace => "@space", EventPlayersWho.Docked => "@docked",
                    EventPlayersWho.Destroyed => "@dead", EventPlayersWho.Survivors => "@survivors", EventPlayersWho.RandomOne => "@r", _ => "@a",
                };
                if (inputs.TryGetValue((n.id, "Orbit"), out var orbit))
                {
                    if (orbit.node.kind == "EventGetOrbitNode") selector += "[orbit=" + Orbit(orbit.node) + "]";
                    else Error(n, "Orbit: only a Get Orbit node goes here.");
                }
                return selector;
            }

            string Clean(EventGraphData.Node n, string s)
            {
                s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
                if (s.IndexOf('#') >= 0) Warning(n, "\"#\" starts a comment in the script: the rest of the line is dropped.");
                return s;
            }

            /// <summary>A script variable's name from a blackboard name: lower case, letters, digits and _.</summary>
            string VariableName(string name, EventGraphData.Node n)
            {
                var b = new StringBuilder();
                foreach (char c in (name ?? "").Trim().ToLowerInvariant()) b.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
                if (b.Length == 0) { Error(n, "A variable without a name."); return "_"; }
                if (char.IsDigit(b[0])) b.Insert(0, '_');
                string v = b.ToString();
                if (StateNames.Contains(v)) Warning(n, $"\"{v}\" is a game value (Game State): the variable can't be read.");
                if (v == "and" || v == "or" || v == "not") Error(n, $"\"{v}\" can't be a variable name.");
                return v;
            }
        }
    }
}
