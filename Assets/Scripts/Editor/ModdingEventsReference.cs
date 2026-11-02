// ModdingEventsReference.cs
// Part of GoF2 > Build > Modding AI Reference: Modding/ai/gof2-modding/events.md, every event graph node (EventGraphNodes)
// with its options and ports as the Editor shows them (category, name, type, enum choices, default, tooltip), and every
// command (NetCommands.Reference: the Command node and the chat / console line) with the player selectors, so an assistant
// can give a node-by-node recipe with real names.
// The nodes are read from EventGraphNodes.cs itself (a small tokenizer: the Node attributes, the consts, OnDefineOptions /
// DefineInputs / OnDefinePorts with base calls and the port helpers, the Exits / HasIn / Word overrides): Graph Toolkit's
// definition contexts can't be stood in for (self-referencing generic builders), and DispatchProxy isn't in the Editor's
// runtime. Keep the node classes in that file's style (calls on the context, the NumberIn / TextIn / ConditionIn /
// PlayersIn helpers) and this file follows them.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GoF2Remake.EditorTools
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]   // classes: made again by every run
    public static class ModdingEventsReference
    {
        const string NodesSource = "Assets/Scripts/Editor/Events/EventGraphNodes.cs";

        class Entry { public string kind, name, display, type, tooltip, defaultValue; }

        class ClassInfo
        {
            public string name, baseName, category, display;
            public readonly Dictionary<string, List<string>> consts = new Dictionary<string, List<string>>();   // name -> value tokens
            public readonly Dictionary<string, List<string>> methods = new Dictionary<string, List<string>>();  // name -> body tokens
            public readonly Dictionary<string, List<string>> properties = new Dictionary<string, List<string>>();
            public bool isAbstract;
        }

        static Dictionary<string, ClassInfo> classes;

        // ---- tokenizer ----

        static List<string> Tokenize(string src)
        {
            var t = new List<string>();
            int i = 0, n = src.Length;
            while (i < n)
            {
                char c = src[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '/' && i + 1 < n && src[i + 1] == '/') { while (i < n && src[i] != '\n') i++; continue; }
                if (c == '/' && i + 1 < n && src[i + 1] == '*') { i = src.IndexOf("*/", i + 2, StringComparison.Ordinal); i = i < 0 ? n : i + 2; continue; }
                if (c == '@' && i + 1 < n && src[i + 1] == '"')
                {
                    var sb = new StringBuilder("\""); i += 2;
                    while (i < n) { if (src[i] == '"') { if (i + 1 < n && src[i + 1] == '"') { sb.Append('"'); i += 2; continue; } i++; break; } sb.Append(src[i++]); }
                    t.Add(sb.ToString()); continue;
                }
                if (c == '"')
                {
                    var sb = new StringBuilder("\""); i++;
                    while (i < n && src[i] != '"')
                    {
                        if (src[i] == '\\' && i + 1 < n) { char e = src[i + 1]; sb.Append(e == 'n' ? ' ' : e); i += 2; continue; }
                        sb.Append(src[i++]);
                    }
                    i++; t.Add(sb.ToString()); continue;
                }
                if (c == '\'') { int j = src.IndexOf('\'', i + 1 + (src[i + 1] == '\\' ? 1 : 0)); t.Add(src.Substring(i, j - i + 1)); i = j + 1; continue; }
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    int j = i; while (j < n && (char.IsLetterOrDigit(src[j]) || src[j] == '_' || (src[j] == '.' && char.IsDigit(c)))) j++;
                    t.Add(src.Substring(i, j - i)); i = j; continue;
                }
                if (c == '=' && i + 1 < n && src[i + 1] == '>') { t.Add("=>"); i += 2; continue; }
                t.Add(c.ToString()); i++;
            }
            return t;
        }

        static bool IsString(string tok) => tok.Length > 0 && tok[0] == '"';
        static bool IsIdent(string tok) => tok.Length > 0 && (char.IsLetter(tok[0]) || tok[0] == '_');

        /// <summary>The index of the bracket closing the one at 'open'.</summary>
        static int Match(List<string> t, int open)
        {
            string o = t[open], c = o == "(" ? ")" : o == "{" ? "}" : o == "[" ? "]" : ">";
            int depth = 0;
            for (int i = open; i < t.Count; i++)
            {
                if (t[i] == o) depth++;
                else if (t[i] == c && --depth == 0) return i;
            }
            return t.Count - 1;
        }

        static void Parse(string src)
        {
            classes = new Dictionary<string, ClassInfo>();
            var t = Tokenize(src);
            string pendingCategory = null, pendingDisplay = null;
            for (int i = 0; i < t.Count; i++)
            {
                if (t[i] == "[" && i + 2 < t.Count && t[i + 1] == "Node" && t[i + 2] == "(")
                {
                    int end = Match(t, i + 2);
                    var args = t.GetRange(i + 3, end - i - 3).Where(IsString).Select(s => s.Substring(1)).ToList();
                    pendingCategory = args.Count > 0 ? args[0] : null;
                    pendingDisplay = args.Count > 1 ? args[1] : null;
                    i = end; continue;
                }
                if (t[i] != "class" || i + 1 >= t.Count) continue;
                var ci = new ClassInfo { name = t[i + 1], category = pendingCategory, display = pendingDisplay };
                ci.isAbstract = i > 0 && (t[i - 1] == "abstract" || (i > 1 && t[i - 2] == "abstract"));
                pendingCategory = pendingDisplay = null;
                int j = i + 2;
                if (t[j] == ":") ci.baseName = t[j + 1];
                while (t[j] != "{") j++;
                int close = Match(t, j);
                ReadMembers(ci, t, j + 1, close);
                classes[ci.name] = ci;
                i = close;
            }
        }

        static void ReadMembers(ClassInfo ci, List<string> t, int from, int to)
        {
            for (int i = from; i < to; i++)
            {
                if (t[i] == "const" && t[i + 1] == "string")
                {
                    int j = i + 2;
                    while (j < to)
                    {
                        string name = t[j]; j += 2;   // name =
                        var value = new List<string>();
                        int depth = 0;
                        while (j < to && !(depth == 0 && (t[j] == "," || t[j] == ";"))) { if (t[j] == "(") depth++; if (t[j] == ")") depth--; value.Add(t[j++]); }
                        ci.consts[name] = value;
                        if (t[j] == ";") break;
                        j++;
                    }
                    i = j; continue;
                }
                if (t[i] == "{") { i = Match(t, i); continue; }
                if (!IsIdent(t[i])) continue;
                // A method: Name ( params ) => expr ;  or  Name ( params ) { body }
                if (t[i + 1] == "(" && i > from && (IsIdent(t[i - 1]) || t[i - 1] == ">" || t[i - 1] == "]"))
                {
                    int close = Match(t, i + 1);
                    if (close + 1 >= to) continue;
                    if (t[close + 1] == "=>")
                    {
                        int end = close + 2, depth = 0;
                        while (end < to && !(depth == 0 && t[end] == ";")) { if (t[end] == "(" || t[end] == "{") depth++; if (t[end] == ")" || t[end] == "}") depth--; end++; }
                        ci.methods[t[i]] = t.GetRange(close + 2, end - close - 1);
                        i = end; continue;
                    }
                    if (t[close + 1] == "{")
                    {
                        int end = Match(t, close + 1);
                        ci.methods[t[i]] = t.GetRange(close + 2, end - close - 2);
                        i = end; continue;
                    }
                }
                // A property: Name => expr ;
                if (t[i + 1] == "=>" && t[i - 1] != ")")
                {
                    int end = i + 2;
                    while (end < to && t[end] != ";") end++;
                    ci.properties[t[i]] = t.GetRange(i + 2, end - i - 2);
                    i = end;
                }
            }
        }

        static IEnumerable<ClassInfo> Chain(ClassInfo c)
        {
            for (var x = c; x != null; x = x.baseName != null && classes.TryGetValue(x.baseName, out var b) ? b : null) yield return x;
        }

        static ClassInfo Parent(ClassInfo c) => c.baseName != null && classes.TryGetValue(c.baseName, out var b) ? b : null;

        /// <summary>A string expression: literals and consts joined by '+', a number, true / false, an enum member's name.</summary>
        static string Eval(ClassInfo c, List<string> toks)
        {
            // cond ? a : b, with a numeric comparison (a loop's counter already put in as a number)
            int q = toks.IndexOf("?"), colon = q < 0 ? -1 : toks.IndexOf(":", q);
            if (q > 0 && colon > q) return Compare(toks.GetRange(0, q)) ? Eval(c, toks.GetRange(q + 1, colon - q - 1)) : Eval(c, toks.GetRange(colon + 1, toks.Count - colon - 1));
            var sb = new StringBuilder();
            for (int i = 0; i < toks.Count; i++)
            {
                string tok = toks[i];
                if (tok == "+" || tok == "(" || tok == ")") continue;
                if (IsString(tok)) { sb.Append(tok.Substring(1)); continue; }
                if (tok.Length > 0 && char.IsDigit(tok[0])) { sb.Append(tok.TrimEnd('f', 'F', 'd', 'D', 'm', 'M')); continue; }
                if (tok == "true") { sb.Append("yes"); continue; }
                if (tok == "false") { sb.Append("no"); continue; }
                if (tok == "-") { sb.Append('-'); continue; }
                if (i + 2 < toks.Count && toks[i + 1] == "." && IsIdent(toks[i + 2])) { sb.Append(toks[i + 2]); i += 2; continue; }   // Enum.Member
                var owner = Chain(c).FirstOrDefault(x => x.consts.ContainsKey(tok));
                sb.Append(owner != null ? Eval(owner, owner.consts[tok]) : tok);
            }
            return sb.ToString();
        }

        static bool Compare(List<string> t)
        {
            int op = t.FindIndex(x => x == "<" || x == ">" || x == "=" || x == "!");
            if (op <= 0) return false;
            string o = t[op] + (op + 1 < t.Count && t[op + 1] == "=" ? "=" : "");
            double.TryParse(t[op - 1], out double a);
            double.TryParse(t[op + o.Length], out double b);
            return o switch { "<" => a < b, "<=" => a <= b, ">" => a > b, ">=" => a >= b, "==" => a == b, "!=" => a != b, _ => false };
        }

        static List<List<string>> SplitArgs(List<string> t, int open, int close)
        {
            var args = new List<List<string>>();
            var cur = new List<string>();
            int depth = 0;
            for (int i = open + 1; i < close; i++)
            {
                if (t[i] == "(" || t[i] == "{" || t[i] == "[") depth++;
                if (t[i] == ")" || t[i] == "}" || t[i] == "]") depth--;
                if (depth == 0 && t[i] == ",") { args.Add(cur); cur = new List<string>(); continue; }
                cur.Add(t[i]);
            }
            if (cur.Count > 0) args.Add(cur);
            return args;
        }

        /// <summary>What a method of a class defines (the nearest override, base calls resolved).</summary>
        static List<Entry> Resolve(ClassInfo c, string method)
        {
            var owner = Chain(c).FirstOrDefault(x => x.methods.ContainsKey(method));
            if (owner == null) return new List<Entry>();
            if (owner.name == "EventFlowNode" && method == "OnDefinePorts") return FlowPorts(c);
            return Interpret(c, owner, owner.methods[method]);
        }

        static List<Entry> FlowPorts(ClassInfo leaf)
        {
            var list = new List<Entry>();
            var hasIn = Chain(leaf).FirstOrDefault(x => x.properties.ContainsKey("HasIn"));
            if (hasIn == null || !hasIn.properties["HasIn"].Contains("false")) list.Add(new Entry { kind = "flow in", name = "In", type = "Flow" });
            var exits = Chain(leaf).First(x => x.properties.ContainsKey("Exits"));
            var tok = exits.properties["Exits"];
            int open = tok.IndexOf("{");
            if (open >= 0)
                foreach (var a in SplitArgs(tok, open, Match(tok, open)))
                    list.Add(new Entry { kind = "flow out", name = Eval(exits, a), type = "Flow" });
            list.AddRange(Resolve(leaf, "DefineInputs"));
            return list;
        }

        /// <summary>The calls in a method body: context.AddOption / AddInputPort / AddOutputPort chains, the port helpers,
        /// base calls. 'leaf' resolves base calls and consts; 'owner' is the class the body belongs to.</summary>
        static List<Entry> Interpret(ClassInfo leaf, ClassInfo owner, List<string> t)
        {
            var list = new List<Entry>();
            for (int i = 0; i < t.Count; i++)
            {
                if (t[i] == "for" && i + 1 < t.Count && t[i + 1] == "(")
                {
                    // for (int k = A; k <= B; k++) <statement or block>: the body once per value, the counter put in.
                    int hClose = Match(t, i + 1);
                    var h = t.GetRange(i + 2, hClose - i - 2);
                    string var = h[1];
                    int.TryParse(h[3], out int from);
                    int lt = h.IndexOf("<");
                    int to = from;
                    if (lt >= 0)
                    {
                        bool inclusive = h[lt + 1] == "=";
                        int.TryParse(h[lt + (inclusive ? 2 : 1)], out to);
                        if (!inclusive) to--;
                    }
                    int bStart = hClose + 1, bEnd;
                    if (t[bStart] == "{") { bEnd = Match(t, bStart); bStart++; }
                    else { bEnd = bStart; int d = 0; while (bEnd < t.Count && !(d == 0 && t[bEnd] == ";")) { if (t[bEnd] == "(") d++; if (t[bEnd] == ")") d--; bEnd++; } }
                    var body = t.GetRange(bStart, bEnd - bStart + (t[hClose + 1] == "{" ? 0 : 1));
                    for (int v = from; v <= to; v++)
                        list.AddRange(Interpret(leaf, owner, body.Select(x => x == var ? v.ToString() : x).ToList()));
                    i = bEnd; continue;
                }
                if (t[i] == "base" && i + 2 < t.Count && t[i + 1] == ".")
                {
                    var parent = Parent(owner);
                    if (parent != null)
                    {
                        string m = t[i + 2];
                        var o = Chain(parent).FirstOrDefault(x => x.methods.ContainsKey(m));
                        if (o != null) list.AddRange(o.name == "EventFlowNode" && m == "OnDefinePorts" ? FlowPorts(leaf) : Interpret(leaf, o, o.methods[m]));
                    }
                    i += 2; continue;
                }
                if (t[i] == "." && i + 2 < t.Count && (t[i + 1] == "AddOption" || t[i + 1] == "AddInputPort" || t[i + 1] == "AddOutputPort") && t[i + 2] == "<")
                {
                    int gt = Match(t, i + 2);
                    var e = new Entry
                    {
                        kind = t[i + 1] == "AddOption" ? "option" : t[i + 1] == "AddInputPort" ? "input" : "output",
                        type = string.Join("", t.GetRange(i + 3, gt - i - 3)),
                    };
                    int open = gt + 1, close = Match(t, open);
                    e.name = Eval(owner, t.GetRange(open + 1, close - open - 1));
                    int j = close + 1;
                    while (j + 2 < t.Count && t[j] == "." && t[j + 2] == "(")
                    {
                        int argClose = Match(t, j + 2);
                        var arg = t.GetRange(j + 3, argClose - j - 3);
                        switch (t[j + 1])
                        {
                            case "WithDisplayName": e.display = Eval(owner, arg); break;
                            case "WithTooltip": e.tooltip = Eval(owner, arg); break;
                            case "WithDefaultValue": e.defaultValue = Eval(owner, arg); break;
                        }
                        j = argClose + 1;
                    }
                    if (e.type != "Flow") list.Add(e);
                    else list.Add(new Entry { kind = e.kind == "input" ? "flow in" : "flow out", name = e.display ?? e.name, type = "Flow" });
                    i = j - 1; continue;
                }
                if (IsIdent(t[i]) && i + 1 < t.Count && t[i + 1] == "(" && (i == 0 || t[i - 1] != "."))
                {
                    int close = Match(t, i + 1);
                    var args = SplitArgs(t, i + 1, close);
                    switch (t[i])
                    {
                        case "NumberIn":
                            list.Add(new Entry { kind = "input", type = "float", name = Eval(owner, args[1]), defaultValue = Eval(owner, args[2]), tooltip = Eval(owner, args[3]) });
                            i = close; continue;
                        case "TextIn":
                            list.Add(new Entry { kind = "input", type = "string", name = Eval(owner, args[1]), defaultValue = Eval(owner, args[2]), tooltip = Eval(owner, args[3]) });
                            i = close; continue;
                        case "ConditionIn":
                            list.Add(new Entry { kind = "input", type = "bool", name = Eval(owner, args[1]), tooltip = Eval(owner, args[2]) });
                            i = close; continue;
                        default:
                            // A helper of the chain (PlayersIn, ShipIn...): inline its body, a default argument as written there.
                            var helperOwner = Chain(leaf).FirstOrDefault(x => x.methods.ContainsKey(t[i]) && t[i] != "OnDefinePorts" && t[i] != "DefineInputs" && t[i] != "OnDefineOptions");
                            if (helperOwner != null)
                            {
                                var body = Interpret(leaf, helperOwner, helperOwner.methods[t[i]]);
                                if (t[i] == "PlayersIn" && body.Count > 0) body[0].defaultValue = args.Count > 1 ? Eval(owner, args[1]) : "@a";
                                list.AddRange(body);
                                i = close; continue;
                            }
                            break;
                    }
                }
            }
            return list;
        }

        // ---- output ----

        static string Clean(string s) => (s ?? "").Replace("|", "/").Replace("\r", "").Replace("\n", " ").Trim();

        static Type FindType(string name)
        {
            // A menu tool run on demand, between reloads; only the game's own assemblies, by name.
#pragma warning disable UAC0005
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
#pragma warning restore UAC0005
            {
                if (!asm.GetName().Name.StartsWith("GoF2") && !asm.GetName().Name.StartsWith("Assembly-CSharp")) continue;
                Type[] types;
                try { types = asm.GetTypes(); } catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types.Where(x => x != null).ToArray(); }
                var found = types.FirstOrDefault(x => x.Name == name);
                if (found != null) return found;
            }
            return null;
        }

        static string TypeName(string t)
        {
            switch (t)
            {
                case "float": case "int": return "Number";
                case "string": return "Text";
                case "bool": return "Yes/No";
                case "Flow": return "Flow";
            }
            var type = FindType(t);
            if (type != null && type.IsEnum) return "Choice: " + string.Join(" / ", Enum.GetNames(type));
            return t;
        }

        public static string Build()
        {
            Parse(File.ReadAllText(NodesSource));

            var sb = new StringBuilder();
            sb.AppendLine("# Galaxy on Fire 2 Remake: event graph nodes and commands");
            sb.AppendLine();
            sb.AppendLine("Generated by GoF2 > Build > Modding AI Reference from the node classes. Quests, bar missions, cutscenes and multiplayer");
            sb.AppendLine("events are `.gof2event` graphs built in the Unity Editor (Assets > Create > GoF2 > Event Graph; templates under Event");
            sb.AppendLine("Graph From Template): give the user a recipe of nodes with these exact names, option values and wires. What the nodes do");
            sb.AppendLine("together is explained in `guide.md` (\"Quests and bar missions\" and the multiplayer \"Events\"); this file lists every node,");
            sb.AppendLine("option and port.");
            sb.AppendLine();
            sb.AppendLine("Wires: the **Flow** arrows run the steps in order (a node's unnamed flow output is Next, named ones are branches; a");
            sb.AppendLine("node without a flow input starts a flow of its own). Data wires carry Number / Text / Yes/No values, **Players** (who a");
            sb.AppendLine("command acts on; or type a selector into the Players port) and **Orbit** (a station, from Get Orbit). Options are set on");
            sb.AppendLine("the node itself; an input port is typed into or wired. The blackboard's Number variables are read by dragging them in");
            sb.AppendLine("and set with Set Variable (Input variables are the event's settings). Texts may hold `{expression}` parts, `%player%`,");
            sb.AppendLine("`%trigger%`, `%victim%` and `[voice <clip>]` (clips in voices.md).");
            sb.AppendLine();
            sb.AppendLine("Expressions (If, While, Wait Until, Set Variable, `{...}` in texts, Starts when, `available`): numbers, variables, + - * /,");
            sb.AppendLine("== != < <= > >=, and / or / not, random(a, b), min, max, floor, count(<players>), alive(ship name), distance(ship name, x,");
            sb.AppendLine("y, z), the state words enemies, ships, players, inspace, docked, dead, time, toppoints, missionstation, and this game's");
            sb.AppendLine("campaign, credits, rank, station, system, ship, kills, cargo(item), has(item), visited(station), quest(name),");
            sb.AppendLine("option(mod:id), won(main | valkyrie | supernova), systemsvisited, medals(gold | silver | bronze | any | allgold |");
            sb.AppendLine("allsupernova), bought(ship).");
            sb.AppendLine();

            var nodes = classes.Values.Where(c => !c.isAbstract && c.category != null).ToList();
            foreach (var group in nodes.GroupBy(c => c.category).OrderBy(g => g.Key))
            {
                sb.AppendLine($"## {group.Key}");
                sb.AppendLine();
                foreach (var c in group.OrderBy(c => c.display))
                {
                    var options = Resolve(c, "OnDefineOptions");
                    var ports = Resolve(c, "OnDefinePorts");
                    sb.AppendLine($"### {c.display}");
                    sb.AppendLine();
                    var wordOwner = Chain(c).FirstOrDefault(x => x.properties.ContainsKey("Word"));
                    if (wordOwner != null) { sb.AppendLine($"Runs the command `/{Eval(wordOwner, wordOwner.properties["Word"])}` (see Commands)."); sb.AppendLine(); }
                    bool flowIn = ports.Any(p => p.kind == "flow in");
                    var exits = ports.Where(p => p.kind == "flow out").Select(p => p.name).ToList();
                    if (flowIn || exits.Count > 0)
                    {
                        sb.AppendLine($"Flow: {(flowIn ? "in" : "no input (starts a flow of its own)")}; out: {(exits.Count > 0 ? string.Join(", ", exits) : "none (ends the flow)")}.");
                        sb.AppendLine();
                    }
                    var rows = options.Concat(ports.Where(p => p.type != "Flow")).ToList();
                    if (rows.Count > 0)
                    {
                        sb.AppendLine("| Kind | Name | Type | Default | What it is |");
                        sb.AppendLine("|---|---|---|---|---|");
                        foreach (var e in rows)
                            sb.AppendLine($"| {e.kind} | {Clean(string.IsNullOrEmpty(e.display) ? e.name : e.display)} | {Clean(TypeName(e.type))} | {(string.IsNullOrEmpty(e.defaultValue) ? "" : "`" + Clean(e.defaultValue) + "`")} | {Clean(e.tooltip)} |");
                        sb.AppendLine();
                    }
                }
            }

            sb.AppendLine("## Commands");
            sb.AppendLine();
            sb.AppendLine("The Command node (and the Commands nodes above) run these lines; in multiplayer an admin types them in the chat with a");
            sb.AppendLine("leading `/`, the dedicated server's console without it. `[players]` is a player's name or a selector; `<...>` required,");
            sb.AppendLine("`[...]` optional. Names of ships, items and stations are the ones in reference.md (or a mod's `mod_id:id`).");
            sb.AppendLine();
            sb.AppendLine("| Command | Usage | What it does |");
            sb.AppendLine("|---|---|---|");
            foreach (var (name, usage, description) in GoF2Remake.Multiplayer.NetCommands.Reference())
                sb.AppendLine($"| /{name} | {Clean(usage)} | {Clean(description)} |");
            sb.AppendLine();
            sb.AppendLine("### Player selectors");
            sb.AppendLine();
            foreach (var (selector, description) in GoF2Remake.Multiplayer.NetCommands.SelectorReference())
                sb.AppendLine($"- `{selector}`: {Clean(description)}");
            sb.AppendLine("- Any selector narrowed to one orbit: `@alive[orbit=78]`, `@r[orbit=Var Hastra]` (in its space or docked at its station).");
            sb.AppendLine("- In single player every selector is the player.");
            return sb.ToString();
        }
    }
}
