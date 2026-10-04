// EventGraphCompiler.cs
// The Editor's live EventGraph -> EventGraphData (nodes by class name, every input port's and option's value as invariant
// text, wires, number variables) -> the game's EventGraphScript, the compiler the server uses on the saved file. So the
// graph window's checks and "Run In Play Mode" use exactly what the server will run.

using System;
using System.Collections.Generic;
using System.Globalization;
using GoF2Remake.Multiplayer;
using Unity.GraphToolkit.Editor;

namespace GoF2Remake.EditorTools
{
    public static class EventGraphCompiler
    {
        /// <summary>The graph's script.</summary>
        public static string Compile(Graph graph, List<EventGraphScript.Problem> problems = null) =>
            EventGraphScript.Compile(Data(graph, out _), problems);

        /// <summary>The graph's script; nodes maps the problems' node ids back to the graph's nodes.</summary>
        public static string Compile(Graph graph, List<EventGraphScript.Problem> problems, out Dictionary<string, INode> nodes) =>
            EventGraphScript.Compile(Data(graph, out nodes), problems);

        public static EventGraphData Data(Graph graph, out Dictionary<string, INode> nodes)
        {
            nodes = new Dictionary<string, INode>();
            var data = new EventGraphData { mainGraph = graph.ID.ToString() };
            var seen = new HashSet<Graph>();
            Add(graph, true, data, nodes, seen, GraphDatabase.GetGraphAssetPath(graph));
            return data;
        }

        /// <summary>One graph's nodes, wires and variables into 'data', then its sub-graphs' (as EventGraphFile reads them).</summary>
        static void Add(Graph graph, bool main, EventGraphData data, Dictionary<string, INode> nodes, HashSet<Graph> seen, string mainPath)
        {
            if (graph == null || !seen.Add(graph)) return;
            string gid = graph.ID.ToString();
            var ids = new Dictionary<INode, string>();
            var subgraphs = new List<Graph>();
            foreach (var n in graph.GetNodes())
            {
                string id = n.ID.ToString();
                ids[n] = id;
                nodes[id] = n;
                var node = new EventGraphData.Node { id = id, graph = gid };
                if (n is IVariableNode variable)
                {
                    node.kind = EventGraphData.VariableKind;
                    node.values[EventGraphData.VariableName] = variable.Variable?.Name ?? "";
                    node.values[EventGraphData.VariableId] = variable.Variable != null ? variable.Variable.ID.ToString() : "";
                }
                else
                {
                    if (n is ISubgraphNode subgraphNode)
                    {
                        var sub = subgraphNode.GetSubgraph();
                        node.kind = EventGraphData.SubgraphKind;
                        node.values[EventGraphData.SubgraphGraph] = sub != null ? sub.ID.ToString() : "";
                        string path = sub != null ? GraphDatabase.GetGraphAssetPath(sub) : null;
                        // An asset sub-graph is another file (the server can't load it); a local one is in this graph's file.
                        node.values[EventGraphData.SubgraphAsset] = !string.IsNullOrEmpty(path) && path != mainPath ? path : "";
                        node.values[EventGraphData.SubgraphName] = sub?.Name ?? "";
                        if (sub != null) subgraphs.Add(sub);
                    }
                    else node.kind = n.GetType().Name;
                    foreach (var port in n.GetInputPorts())
                        if (port.DataType != typeof(Flow) && Text(port, out string v)) node.values[port.Name] = v;
                    foreach (var option in n.NodeOptions)
                        if (Text(option, out string v)) node.values[option.Name] = v;
                }
                data.nodes.Add(node);
            }
            var connected = new List<IPort>();
            foreach (var n in graph.GetNodes())
                foreach (var output in n.GetOutputPorts())
                {
                    connected.Clear();
                    output.GetConnectedPorts(connected);
                    foreach (var input in connected)
                    {
                        var to = input.GetNode();
                        if (to == null || !ids.ContainsKey(to)) continue;
                        data.wires.Add(new EventGraphData.Wire { fromNode = ids[n], fromPort = output.Name, toNode = ids[to], toPort = input.Name });
                    }
                }
            foreach (var v in graph.GetVariables())
            {
                bool flow = v.DataType == typeof(Flow);
                if (!flow && v.DataType != typeof(float)) continue;
                float value = 0f;
                if (!flow) v.TryGetDefaultValue(out value);
                int kind = v.VariableKind == VariableKind.Input ? 1 : v.VariableKind == VariableKind.Output ? 2 : 0;
                data.variables.Add(new EventGraphData.Variable
                {
                    name = v.Name, value = value, id = v.ID.ToString(), graph = gid, kind = kind, flow = flow, setting = main && kind == 1 && !flow,
                });
            }
            foreach (var sub in subgraphs) Add(sub, false, data, nodes, seen, mainPath);
        }

        // A value as the file stores it: numbers invariant, bools 1 / 0, enums their index.
        static bool Text(IPort port, out string text)
        {
            text = null;
            var type = port.DataType;
            if (type == typeof(float)) { if (port.TryGetValue(out float v)) text = v.ToString("R", CultureInfo.InvariantCulture); }
            else if (type == typeof(bool)) { if (port.TryGetValue(out bool v)) text = v ? "1" : "0"; }
            else if (type == typeof(string)) { if (port.TryGetValue(out string v)) text = v ?? ""; }
            return text != null;
        }

        static bool Text(INodeOption option, out string text)
        {
            text = null;
            var type = option.DataType;
            if (type == typeof(float)) { if (option.TryGetValue(out float v)) text = v.ToString("R", CultureInfo.InvariantCulture); }
            else if (type == typeof(bool)) { if (option.TryGetValue(out bool v)) text = v ? "1" : "0"; }
            else if (type == typeof(string)) { if (option.TryGetValue(out string v)) text = v ?? ""; }
            else if (type != null && type.IsEnum) { if (option.TryGetValue(out Enum v) && v != null) text = Convert.ToInt32(v).ToString(CultureInfo.InvariantCulture); }
            return text != null;
        }
    }
}
