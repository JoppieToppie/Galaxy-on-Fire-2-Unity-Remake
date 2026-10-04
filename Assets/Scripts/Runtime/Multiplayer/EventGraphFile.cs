// EventGraphFile.cs
// Remake multiplayer: reads an event graph file (.gof2netevent, saved by the Editor's Graph Toolkit) without the Editor, so the
// server runs graphs from its Events folder and the built-in ones (EventGraphImporter keeps the file's text) as they are.
// The file is Unity YAML: one MonoBehaviour whose "references" list holds Graph Toolkit's model by [SerializeReference]:
// GraphModelImp (the node and wire lists), UserNodeModelImp (a node: its guid, the stored value of every input port and
// option ("__option_<name>") as Constant / EnumConstant references, m_Node = the Editor node class, e.g. EventWaitNode),
// VariableNodeModelImp (a blackboard variable's node), WireModel (node guid + port name at each end),
// VariableDeclarationModel (a blackboard variable: name, type, default, m_Modifiers 0 local / 1 input / 2 output) and
// SubgraphNodeModelImp (a sub-graph's node: its ports are named by the sub-graph's input / output variables' ids; a local
// sub-graph is another GraphModelImp in the same file, listed in its parent's m_LocalSubgraphs). This is Graph Toolkit's internal format (as of
// Unity 6.7): the Editor's importer reads every graph both ways and warns when this reader disagrees with Graph Toolkit.
// The YAML reader covers what Unity writes: block mappings and sequences (a sequence may sit at its key's indent), flow
// mappings / sequences on one line, plain, single- and double-quoted scalars (also over several lines).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GoF2Remake.Multiplayer
{
    public static class EventGraphFile
    {
        /// <summary>The graph in a .gof2netevent file's text. Throws FormatException when it isn't one.</summary>
        public static EventGraphData Read(string yaml)
        {
            var root = new Yaml(yaml).Document();
            var refIds = List(Path(root, "MonoBehaviour", "references", "RefIds"));
            if (refIds == null) throw new FormatException("no graph data");

            var refs = new Dictionary<string, (string cls, object data)>();
            foreach (var item in refIds)
            {
                var m = item as Dictionary<string, object>;
                if (m == null) continue;
                refs[Str(Get(m, "rid"))] = (Str(Path(m, "type", "class")), Get(m, "data"));
            }

            object Data(object reference) => reference != null && refs.TryGetValue(Str(Get(reference, "rid")), out var r) ? r.data : null;
            string Class(object reference) => reference != null && refs.TryGetValue(Str(Get(reference, "rid")), out var r) ? r.cls : null;
            // The node's id as Graph Toolkit's INode.ID (a Hash128 of the two halves), so the Editor finds the server's nodes.
            string Guid(object guid) =>
                ulong.TryParse(Str(Get(guid, "m_Value0")), out ulong a) && ulong.TryParse(Str(Get(guid, "m_Value1")), out ulong b)
                    ? new UnityEngine.Hash128(a, b).ToString() : Str(Get(guid, "m_Value0")) + "-" + Str(Get(guid, "m_Value1"));

            // The main graph (the MonoBehaviour's m_GraphModel), then its local sub-graphs (m_LocalSubgraphs), recursively.
            object main = Data(Path(root, "MonoBehaviour", "m_GraphModel"));
            if (main == null) foreach (var r in refs.Values) if (r.cls == "GraphModelImp") { main = r.data; break; }
            if (main == null) throw new FormatException("no graph model");

            var graph = new EventGraphData();
            var seen = new HashSet<object>();
            void ReadGraph(object model, bool isMain)
            {
                if (model == null || !seen.Add(model)) return;
                string gid = Guid(Get(model, "m_Guid"));
                if (isMain) graph.mainGraph = gid;
                foreach (var reference in List(Get(model, "m_GraphNodeModels")) ?? new List<object>())
                {
                    string cls = Class(reference);
                    var data = Data(reference);
                    if (data == null) continue;
                    var node = new EventGraphData.Node { id = Guid(Get(data, "m_Guid")), graph = gid };
                    if (cls == "VariableNodeModelImp")
                    {
                        var decl = Data(Get(data, "m_DeclarationModel"));
                        node.kind = EventGraphData.VariableKind;
                        node.values[EventGraphData.VariableName] = Str(Get(decl, "m_Name"));
                        node.values[EventGraphData.VariableId] = Str(Path(decl, "m_HashGuid", "Hash"));
                    }
                    else if (cls == "UserNodeModelImp" || cls == "SubgraphNodeModelImp")
                    {
                        if (cls == "UserNodeModelImp") node.kind = Class(Get(data, "m_Node"));
                        else
                        {
                            // A sub-graph: local ones are in this file (m_GraphModelGuid); an asset sub-graph (another file) isn't.
                            node.kind = EventGraphData.SubgraphKind;
                            node.values[EventGraphData.SubgraphGraph] = Str(Path(data, "m_SubgraphReference", "m_GraphModelGuid", "Hash"));
                            node.values[EventGraphData.SubgraphAsset] = Str(Path(data, "m_SubgraphReference", "m_AssetGuidAsHash", "Hash")).Trim('0');
                            node.values[EventGraphData.SubgraphName] = Str(Get(data, "m_Tooltip"));
                        }
                        var keys = List(Path(data, "m_InputConstantsById", "m_KeyList")) ?? new List<object>();
                        var values = List(Path(data, "m_InputConstantsById", "m_ValueList")) ?? new List<object>();
                        for (int i = 0; i < keys.Count && i < values.Count; i++)
                        {
                            string key = Str(keys[i]);
                            if (key.StartsWith("__option_")) key = key.Substring(9);
                            string constant = Class(values[i]);
                            var value = Data(values[i]);
                            // Values of the field types only (text, numbers, bools, enums); Flow / Orbit / Players ports hold none.
                            if (constant == null || !(constant == "EnumConstant" || constant.Contains("[[System.String,") || constant.Contains("[[System.Single,")
                                                      || constant.Contains("[[System.Boolean,"))) continue;
                            node.values[key] = constant == "EnumConstant" ? Str(Path(value, "m_Value", "m_Value")) : Str(Get(value, "m_Value"));
                        }
                    }
                    else continue;   // Graph Toolkit's own nodes (constants...): not used by event graphs
                    graph.nodes.Add(node);
                }

                foreach (var reference in List(Get(model, "m_GraphWireModels")) ?? new List<object>())
                {
                    var data = Data(reference);
                    if (data == null) continue;
                    graph.wires.Add(new EventGraphData.Wire
                    {
                        fromNode = Guid(Path(data, "m_FromPortReference", "m_NodeModelGuid")),
                        fromPort = Str(Path(data, "m_FromPortReference", "m_UniqueId")),
                        toNode = Guid(Path(data, "m_ToPortReference", "m_NodeModelGuid")),
                        toPort = Str(Path(data, "m_ToPortReference", "m_UniqueId")),
                    });
                }

                // The blackboard in its order (its sections' items): number and flow variables.
                foreach (var section in List(Get(model, "m_SectionModels")) ?? new List<object>())
                    foreach (var item in List(Get(Data(section), "m_Items")) ?? new List<object>())
                    {
                        if (Class(item) != "VariableDeclarationModel") continue;
                        var decl = Data(item);
                        string type = Str(Path(decl, "m_DataType", "m_Identification"));
                        bool flow = type.Contains(".Flow,") || type.StartsWith("GoF2Remake.EditorTools.Flow");
                        if (!flow && !type.StartsWith("System.Single")) continue;
                        double.TryParse(Str(Get(Data(Get(decl, "m_InitializationValue")), "m_Value")), NumberStyles.Float, CultureInfo.InvariantCulture, out double value);
                        // m_Modifiers: 0 a local variable, 1 an Input (the main graph's: the event's setting), 2 an Output.
                        int.TryParse(Str(Get(decl, "m_Modifiers")), out int kind);
                        graph.variables.Add(new EventGraphData.Variable
                        {
                            name = Str(Get(decl, "m_Name")), value = value, id = Str(Path(decl, "m_HashGuid", "Hash")), graph = gid,
                            kind = kind, flow = flow, setting = isMain && kind == 1 && !flow,
                        });
                    }

                foreach (var sub in List(Get(model, "m_LocalSubgraphs")) ?? new List<object>()) ReadGraph(Data(sub), false);
            }

            ReadGraph(main, true);
            return graph;
        }

        // ---- reading the tree ----------------------------------------------------------------------------------------

        static object Get(object map, string key) =>
            map is Dictionary<string, object> m && m.TryGetValue(key, out var v) ? v : null;

        static object Path(object node, params string[] keys)
        {
            foreach (string k in keys) node = Get(node, k);
            return node;
        }

        static List<object> List(object node) => node as List<object>;
        static string Str(object node) => node as string ?? "";

        // ---- a small YAML reader -----------------------------------------------------------------------------------

        sealed class Yaml
        {
            readonly List<string> lines = new List<string>();
            int at;

            public Yaml(string text)
            {
                foreach (string raw in (text ?? "").Replace("\r", "").Split('\n')) lines.Add(raw);
            }

            /// <summary>The first document's content (the lines after its "---").</summary>
            public object Document()
            {
                while (at < lines.Count && (lines[at].StartsWith("%") || lines[at].Trim().Length == 0)) at++;
                if (at < lines.Count && lines[at].StartsWith("---")) at++;
                SkipBlank();
                if (at >= lines.Count) throw new FormatException("empty file");
                return Node(Indent(lines[at]));
            }

            static int Indent(string line)
            {
                int i = 0;
                while (i < line.Length && line[i] == ' ') i++;
                return i;
            }

            void SkipBlank()
            {
                while (at < lines.Count && lines[at].Trim().Length == 0) at++;
            }

            bool AtDocumentEnd => at >= lines.Count || lines[at].StartsWith("---") || lines[at].StartsWith("...");

            static bool IsItem(string trimmed) => trimmed == "-" || trimmed.StartsWith("- ");

            object Node(int indent)
            {
                SkipBlank();
                if (AtDocumentEnd) return null;
                return IsItem(lines[at].TrimStart()) ? Sequence(indent) : Mapping(indent);
            }

            List<object> Sequence(int indent)
            {
                var list = new List<object>();
                while (true)
                {
                    SkipBlank();
                    if (AtDocumentEnd) break;
                    string line = lines[at];
                    if (Indent(line) != indent || !IsItem(line.TrimStart())) break;
                    string content = line.TrimStart().Length > 1 ? line.TrimStart().Substring(2) : "";
                    if (content.Trim().Length == 0)
                    {
                        at++;
                        SkipBlank();
                        list.Add(!AtDocumentEnd && Indent(lines[at]) > indent ? Node(Indent(lines[at])) : null);
                    }
                    else if (KeyOf(content, out _, out _))
                    {
                        // "- key: value": a mapping whose keys sit two columns in.
                        lines[at] = new string(' ', indent + 2) + content;
                        list.Add(Mapping(indent + 2));
                    }
                    else
                    {
                        at++;
                        list.Add(Scalar(content.Trim(), indent));
                    }
                }
                return list;
            }

            Dictionary<string, object> Mapping(int indent)
            {
                var map = new Dictionary<string, object>(StringComparer.Ordinal);
                while (true)
                {
                    SkipBlank();
                    if (AtDocumentEnd) break;
                    string line = lines[at];
                    int ind = Indent(line);
                    if (ind != indent || IsItem(line.TrimStart())) break;
                    if (!KeyOf(line.Substring(ind), out string key, out string rest)) throw new FormatException($"line {at + 1}: not a key");
                    at++;
                    if (rest.Length == 0)
                    {
                        SkipBlank();
                        if (AtDocumentEnd) { map[key] = null; continue; }
                        int next = Indent(lines[at]);
                        if (next > indent) map[key] = Node(next);
                        else if (next == indent && IsItem(lines[at].TrimStart())) map[key] = Sequence(indent);
                        else map[key] = null;
                    }
                    else map[key] = Scalar(rest, indent);
                }
                return map;
            }

            /// <summary>"key: value" or "key:" (a plain key; values may hold ": " themselves).</summary>
            static bool KeyOf(string s, out string key, out string rest)
            {
                key = rest = null;
                if (s.Length == 0 || s[0] == '\'' || s[0] == '"' || s[0] == '{' || s[0] == '[') return false;
                int colon = s.IndexOf(": ", StringComparison.Ordinal);
                if (colon < 0 && s.EndsWith(":")) colon = s.Length - 1;
                if (colon <= 0) return false;
                key = s.Substring(0, colon).Trim();
                rest = colon + 1 < s.Length ? s.Substring(colon + 1).Trim() : "";
                return true;
            }

            /// <summary>A value after "key: " (the line already consumed); quoted and plain scalars may go on over the
            /// next lines (more indented than the key).</summary>
            object Scalar(string text, int indent)
            {
                if (text.StartsWith("{") || text.StartsWith("["))
                {
                    int i = 0;
                    return Flow(text, ref i);
                }
                if (text.StartsWith("'") || text.StartsWith("\""))
                {
                    char quote = text[0];
                    var sb = new StringBuilder(text);
                    while (!Closed(sb.ToString(), quote) && at < lines.Count) { sb.Append('\n').Append(lines[at]); at++; }
                    return Quoted(sb.ToString(), quote);
                }
                // A plain scalar: more-indented lines continue it (folded).
                var plain = new StringBuilder(text);
                while (at < lines.Count && (lines[at].Trim().Length == 0 ? NextContentDeeper(indent) : Indent(lines[at]) > indent))
                {
                    plain.Append(lines[at].Trim().Length == 0 ? "\n" : " " + lines[at].Trim());
                    at++;
                }
                return plain.ToString().Trim();
            }

            bool NextContentDeeper(int indent)
            {
                for (int i = at; i < lines.Count; i++) if (lines[i].Trim().Length > 0) return Indent(lines[i]) > indent;
                return false;
            }

            /// <summary>Whether the quoted scalar starting at s[0] ends in s.</summary>
            static bool Closed(string s, char quote)
            {
                for (int i = 1; i < s.Length; i++)
                {
                    if (quote == '"' && s[i] == '\\') { i++; continue; }
                    if (s[i] != quote) continue;
                    if (quote == '\'' && i + 1 < s.Length && s[i + 1] == '\'') { i++; continue; }
                    return true;
                }
                return false;
            }

            /// <summary>A quoted scalar's value: '' = ', double quotes' escapes, line folding (a line break = a space, an
            /// empty line = a line break; in double quotes a "\" at the line's end joins without a space).</summary>
            static string Quoted(string s, char quote)
            {
                var sb = new StringBuilder();
                int i = 1;
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == quote)
                    {
                        if (quote == '\'' && i + 1 < s.Length && s[i + 1] == '\'') { sb.Append('\''); i += 2; continue; }
                        break;
                    }
                    if (c == '\n')
                    {
                        // Fold: trailing spaces gone, the next line's indent gone; empty lines are line breaks.
                        while (sb.Length > 0 && sb[sb.Length - 1] == ' ') sb.Length--;
                        int breaks = 0;
                        while (i < s.Length && (s[i] == '\n' || s[i] == ' ' || s[i] == '\t')) { if (s[i] == '\n') breaks++; i++; }
                        sb.Append(breaks > 1 ? new string('\n', breaks - 1) : " ");
                        continue;
                    }
                    if (quote == '"' && c == '\\' && i + 1 < s.Length)
                    {
                        char e = s[i + 1];
                        i += 2;
                        switch (e)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            case 'r': sb.Append('\r'); break;
                            case '0': sb.Append('\0'); break;
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case ' ': sb.Append(' '); break;
                            case 'x': sb.Append((char)Convert.ToInt32(s.Substring(i, 2), 16)); i += 2; break;
                            case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                            case 'U': sb.Append(char.ConvertFromUtf32(Convert.ToInt32(s.Substring(i, 8), 16))); i += 8; break;
                            case '\n':
                                while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;   // escaped line break: joined
                                break;
                            default: sb.Append(e); break;
                        }
                        continue;
                    }
                    sb.Append(c);
                    i++;
                }
                return sb.ToString();
            }

            /// <summary>A flow mapping {a: b, c: d} or sequence [a, b] (nested, quoted parts allowed).</summary>
            static object Flow(string s, ref int i)
            {
                char open = s[i++];
                if (open == '[')
                {
                    var list = new List<object>();
                    while (true)
                    {
                        SkipSpaces(s, ref i);
                        if (i >= s.Length) break;
                        if (s[i] == ']') { i++; break; }
                        list.Add(FlowValue(s, ref i, ']'));
                        SkipSpaces(s, ref i);
                        if (i < s.Length && s[i] == ',') i++;
                    }
                    return list;
                }
                var map = new Dictionary<string, object>(StringComparer.Ordinal);
                while (true)
                {
                    SkipSpaces(s, ref i);
                    if (i >= s.Length) break;
                    if (s[i] == '}') { i++; break; }
                    int colon = s.IndexOf(':', i);
                    if (colon < 0) break;
                    string key = s.Substring(i, colon - i).Trim();
                    i = colon + 1;
                    SkipSpaces(s, ref i);
                    map[key] = i < s.Length && (s[i] == ',' || s[i] == '}') ? "" : FlowValue(s, ref i, '}');
                    SkipSpaces(s, ref i);
                    if (i < s.Length && s[i] == ',') i++;
                }
                return map;
            }

            static object FlowValue(string s, ref int i, char close)
            {
                if (s[i] == '{' || s[i] == '[') return Flow(s, ref i);
                if (s[i] == '\'' || s[i] == '"')
                {
                    char q = s[i];
                    int start = i;
                    i++;
                    while (i < s.Length)
                    {
                        if (q == '"' && s[i] == '\\') { i += 2; continue; }
                        if (s[i] == q)
                        {
                            if (q == '\'' && i + 1 < s.Length && s[i + 1] == '\'') { i += 2; continue; }
                            i++;
                            break;
                        }
                        i++;
                    }
                    return Quoted(s.Substring(start, i - start), q);
                }
                int from = i;
                while (i < s.Length && s[i] != ',' && s[i] != close) i++;
                return s.Substring(from, i - from).Trim();
            }

            static void SkipSpaces(string s, ref int i)
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            }
        }
    }
}
