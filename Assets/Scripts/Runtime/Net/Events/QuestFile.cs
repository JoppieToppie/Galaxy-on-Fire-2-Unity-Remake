// QuestFile.cs
// Remake-only: a quest, bar mission or event made in the Mod Builder (events/<name>.gof2quest, Modding.ProjectQuest) turned
// into an event graph's script (EventRunner's line form, the same EventGraphScript writes from a node graph), so it runs like
// any other: a JSON tree of steps instead of nodes, for modders without the Unity Editor. Every step type, its fields and the
// plain steps' lines are QuestSteps'; this writes the header, the variables, the steps that need more than one line or a
// block (a conversation, a fight, If / While / Ask / Random ...) and walks the nested lists.
//   header    title, kind (quest | mission | event), startsWhen and repeat (quests), reward, offer / client / stations / players
//             (missions), variables [{ name, value, setting }] (a setting: /event name=value can change it)
//   steps     [{ "type": ..., fields }], blocks holding their own lists ("then", "else", "steps", answers[].steps,
//             branches[].steps)
// FromScript goes the other way: an event graph's script (a Unity-made .gof2event, EventGraphScript.FromFile) as steps, the
// lines it doesn't know as Command steps, so a graph opens in the builder and runs the same.
// Text never carries the script's own marks: '#' starts a comment there, '|' separates pages / answers; '{ }' stays only
// around a variable or the game's state ({kills}); they are replaced. Plain C#.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace GoF2Remake.Events
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]   // read-only tables
    public static partial class QuestFile
    {
        /// <summary>The file's extension and its "format"; Legacy* = from when these were called quests (still read).</summary>
        public const string Extension = "gof2mission", LegacyExtension = "gof2quest", Format = "gof2mission", LegacyFormat = "gof2quest";

        /// <summary>A .gof2mission (or old .gof2quest) file's text?</summary>
        public static bool Is(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string t = text.TrimStart();
            return t.StartsWith("{") && (t.Contains("\"" + Format + "\"") || t.Contains("\"" + LegacyFormat + "\""));
        }

        /// <summary>The players a step speaks to by default: everyone the run covers (single player: the player; a bar
        /// mission: its team, NetCommands.Scope).</summary>
        const string Players = "@a";

        /// <summary>The words an expression may use besides the quest's variables (EventRunner.Eval).</summary>
        static readonly HashSet<string> StateWords = new HashSet<string>
        {
            "enemies", "ships", "time", "missionstation", "toppoints", "players", "inspace", "docked", "dead", "campaign", "credits",
            "rank", "station", "system", "ship", "kills", "systemsvisited", "random", "min", "max", "floor", "count", "alive", "distance",
            "cargo", "has", "visited", "quest", "option", "won", "inorbit", "and", "or", "not", "main", "valkyrie", "supernova",
            "randomstation", "stationname", "systemname", "standing",
        };

        public static bool IsName(string s) => !string.IsNullOrEmpty(s) && Regex.IsMatch(s, "^[A-Za-z_][A-Za-z0-9_]*$");

        /// <summary>The script, or "" with the problems (step numbers from 1, "3.2" inside a block).</summary>
        public static string Compile(string json, List<string> problems)
        {
            JObject o;
            try { o = JObject.Parse(json); }
            catch (Exception e) { problems.Add("can't read the quest file: " + e.Message); return ""; }
            return Compile(o, problems);
        }

        public static string Compile(JObject o, List<string> problems) => new Writer(o, problems).Run();

        sealed class Writer
        {
            readonly JObject o;
            readonly List<string> problems;
            readonly StringBuilder sb = new StringBuilder();
            readonly HashSet<string> names = new HashSet<string>(StateWords);
            int enemies, randoms, indent;
            bool ended;

            public Writer(JObject o, List<string> problems)
            {
                this.o = o;
                this.problems = problems;
            }

            void Line(string l) => sb.Append(' ', indent * 2).Append(l.TrimEnd()).Append('\n');

            string Clean(string s) => QuestFile.Clean(s, names);
            string Ref(JToken t) => t is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.String) ? Clean(v.ToString(CultureInfo.InvariantCulture)) : "";
            List<string> Refs(JToken t) => t is JArray a ? a.Select(Ref).Where(x => x.Length > 0).ToList() : new List<string>();
            /// <summary>A text the runner shows on several lines (an offer, an objective): its line breaks as \n.</summary>
            string Lines(string s) => Clean((s ?? "").Replace("\r", "").Replace("\n", "\\n"));

            public string Run()
            {
                string title = Clean(Str(o, "title"));
                string kind = Str(o, "kind").ToLowerInvariant();
                if (kind != "mission" && kind != "event") kind = "quest";
                int reward = Int(o, "reward", 0);
                foreach (var v in (o["variables"] as JArray ?? new JArray()).OfType<JObject>())
                    if (IsName(Str(v, "name"))) names.Add(Str(v, "name").ToLowerInvariant());
                CollectNames(o["steps"] as JArray);
                if (title.Length == 0 && kind != "event") problems.Add("The quest needs a title.");
                if (kind == "quest")
                {
                    if (title.Length > 0) Line("mission title " + title);
                    Line("mission kind quest");
                    string starts = Str(o, "startsWhen").Trim();
                    if (starts.Length > 0) Line("mission starts " + Expr(starts));
                    if (reward > 0) Line("mission reward " + reward);
                    if (o["repeat"] is JValue rp && (rp.Type == JTokenType.Integer || rp.Type == JTokenType.Float) && (double)rp > 0)
                        Line("mission repeat " + ((double)rp).ToString(CultureInfo.InvariantCulture));
                }
                else if (kind == "mission")
                {
                    Line("mission kind mission");
                    Line("mission title " + title);
                    string offer = Lines(Text(o, "offer"));
                    if (offer.Length > 0) Line("mission offer " + offer);
                    string client = Clean(Str(o, "client"));
                    if (client.Length > 0) Line("mission client " + client);
                    if (reward > 0) Line("mission reward " + reward);
                    int min = 1, max = 4;
                    if (o["players"] is JArray pl && pl.Count >= 2) { min = Math.Max(1, (int)pl[0]); max = Math.Max(min, (int)pl[1]); }
                    Line($"mission players {min} {max}");
                    if (o["chance"] is JValue ch && (ch.Type == JTokenType.Integer || ch.Type == JTokenType.Float))
                        Line("mission chance " + Math.Max(0, Math.Min(100, (int)Math.Round((double)ch))));
                    string takers = Str(o, "takers").ToLowerInvariant();
                    if (takers == "station" || takers == "any") Line("mission takers " + takers);
                    var stations = Refs(o["stations"]);
                    if (stations.Count > 0) Line("mission stations " + string.Join(", ", stations));
                }
                foreach (var v in (o["variables"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    string name = Str(v, "name").ToLowerInvariant(), value = Expr(Str(v, "value"));
                    if (!IsName(name)) { problems.Add($"The variable \"{name}\": a name of letters, digits and _."); continue; }
                    if (value.Length == 0) value = "0";
                    Line((v["setting"] is JValue s && s.Type == JTokenType.Boolean && (bool)s ? "param " : "set ") + name + " = " + value);
                }
                Line("");
                Steps(o["steps"] as JArray ?? new JArray(), "", 0);
                if (!ended && kind != "event" && Bool(o, "autoComplete", true)) Line("complete");
                return sb.ToString();
            }

            /// <summary>The variables Set / Repeat steps name (so their {name} in a text stays an expression).</summary>
            void CollectNames(JArray steps)
            {
                if (steps == null) return;
                foreach (var st in steps.OfType<JObject>())
                {
                    string type = Str(st, "type");
                    if (type == "set" && IsName(Str(st, "variable"))) names.Add(Str(st, "variable").ToLowerInvariant());
                    if (type == "repeat" && IsName(Str(st, "as"))) names.Add(Str(st, "as").ToLowerInvariant());
                    foreach (var (_, list) in QuestSteps.Children(st)) CollectNames(list);
                }
            }

            void Steps(JArray steps, string prefix, int depth)
            {
                int n = 0;
                foreach (var t in steps)
                {
                    n++;
                    string at = prefix + n;
                    if (!(t is JObject st)) { problems.Add($"Step {at}: not a step."); continue; }
                    Step(st, at, depth);
                }
            }

            void Block(JArray list, string at, int depth)
            {
                indent++;
                Steps(list ?? new JArray(), at + ".", depth + 1);
                indent--;
            }

            void Step(JObject st, string at, int depth)
            {
                string type = Str(st, "type").ToLowerInvariant();
                var def = QuestSteps.Get(type);
                void Problem(string m) => problems.Add($"Step {at} ({def?.english ?? type}): {m}");
                if (def == null) { Problem($"unknown step \"{type}\"."); return; }
                if (def.topOnly && depth > 0) { Problem("only on the quest's own line of steps, not inside a block."); return; }
                var r = new QuestReader(st, def, Problem, Clean, id => QuestGraph.Resolve(o, id, Problem));
                string who = r.Who;
                if (def.line != null)
                {
                    string l = def.line(r);
                    if (!string.IsNullOrEmpty(l)) Line(l);
                    return;
                }
                switch (type)
                {
                    case "dialog":
                    {
                        var pages = new List<string>();
                        foreach (var p in (st["pages"] as JArray ?? new JArray()).OfType<JObject>())
                        {
                            string speaker = Clean(Str(p, "speaker")), text = Clean(Text(p, "text"));
                            if (text.Length == 0) continue;
                            if (speaker.Length == 0) speaker = "player";
                            pages.Add(speaker + " : " + text + Voice(p));
                        }
                        if (pages.Count == 0) { Problem("a conversation needs a page with text."); break; }
                        Line($"dialog {who} " + string.Join(" | ", pages));
                        break;
                    }
                    case "radio":
                    {
                        string speaker = Clean(Str(st, "speaker")), text = Clean(Text(st, "text"));
                        if (text.Length == 0) { Problem("a radio call needs a text."); break; }
                        Line($"radio {who} {(speaker.Length > 0 ? speaker : "player")} : {text}{Voice(st)}");
                        break;
                    }
                    case "objective":
                        Objective(Lines(Text(st, "text")), r.Ref("station"));
                        break;
                    case "goto":
                    {
                        string station = r.Ref("station");
                        if (station.Length == 0) { Problem("pick the station."); break; }
                        string obj = Clean(Text(st, "objective"));
                        if (obj.Length > 0) Objective(obj, station);
                        Line($"wait until {(Bool(st, "dock", true) ? "docked" : "inorbit")}({station}) > 0");
                        break;
                    }
                    case "fight":
                    {
                        string station = r.Ref("station");
                        var ships = (st["ships"] as JArray ?? new JArray()).OfType<JObject>().ToList();
                        if (ships.Count == 0) { Problem("add the ships to fight."); break; }
                        string name = Clean(Str(st, "name"));
                        if (name.Length == 0) name = "Quest Enemy " + (++enemies);
                        string obj = Clean(Text(st, "objective"));
                        if (obj.Length > 0) Objective(obj, station);
                        bool quiet = station.Length > 0 && Bool(st, "quiet", true);
                        if (quiet) Line("questorbit " + station);
                        Line(station.Length > 0 ? $"wait until inorbit({station}) > 0" : "wait until inspace > 0");
                        Line("wait 3");
                        string where = station.Length > 0 ? $"@alive[orbit={station}]" : "@alive";
                        foreach (var sh in ships)
                        {
                            string ship = Ref(sh["ship"]);
                            if (ship.Length == 0) { Problem("a ship isn't picked."); continue; }
                            int count = Math.Max(1, Math.Min(10, Int(sh, "count", 1)));
                            string race = Clean(Str(sh, "race")).ToLowerInvariant();
                            string behaviour = Clean(Str(sh, "behaviour")).ToLowerInvariant();
                            if (behaviour.Length == 0) behaviour = "enemy";
                            Line(QuestReader.Join("spawn", where, ship, race, count.ToString(CultureInfo.InvariantCulture), behaviour, "named", name));
                        }
                        Line("wait 2");
                        Line($"wait until alive({name}) == 0");
                        if (quiet) Line($"questorbit {station} off");
                        break;
                    }
                    case "cargo":
                    {
                        string item = r.Ref("item"), station = r.Ref("station");
                        if (item.Length == 0) { Problem("pick the item."); break; }
                        int amount = Math.Max(1, Int(st, "amount", 1));
                        string obj = Clean(Text(st, "objective"));
                        if (obj.Length > 0) Objective(obj, station);
                        Line($"wait until cargo({item}) >= {amount}" + (station.Length > 0 ? $" and docked({station}) > 0" : ""));
                        if (Bool(st, "take", false)) Line($"take @a {item} {amount}");   // handed in (remake: QuestSteps "take")
                        break;
                    }
                    case "reward":
                    {
                        var parts = new List<string>();
                        string credits = r.Arg("credits");
                        if (credits.Length > 0 && credits != "0") parts.Add(credits);
                        foreach (var it in (st["items"] as JArray ?? new JArray()).OfType<JObject>())
                        {
                            string item = Ref(it["item"]);
                            if (item.Length > 0) parts.Add(item + " " + Math.Max(1, Int(it, "amount", 1)));
                        }
                        if (parts.Count == 0) { Problem("a reward needs credits or items."); break; }
                        string rt = Clean(Text(st, "title"));
                        Line($"reward {who} " + string.Join(" + ", parts) + (rt.Length > 0 ? " | " + rt : ""));
                        break;
                    }
                    case "checkpoint":
                    {
                        string name = Clean(Str(st, "name")).Replace(' ', '_');
                        Line("checkpoint " + (name.Length > 0 ? name : "step" + at.Replace('.', '_')));
                        break;
                    }
                    case "startquest":
                    {
                        string q = Clean(Str(st, "quest"));
                        if (q.Length == 0 || q.Contains(" ")) { Problem("pick the quest."); break; }
                        Line("startquest " + q);
                        break;
                    }
                    case "complete":
                    {
                        string rw = Num(st, "reward");
                        string ct = Clean(Text(st, "title"));
                        Line(("complete " + rw).TrimEnd() + (ct.Length > 0 ? " | " + ct : ""));
                        if (depth == 0) ended = true;
                        break;
                    }
                    case "fail":
                        Line(("fail " + Clean(Text(st, "title"))).TrimEnd());
                        if (depth == 0) ended = true;
                        break;

                    // ---- blocks ----
                    case "if":
                    {
                        string c = Cond(r, Problem);
                        if (c == null) break;
                        Line("if " + c);
                        Block(st["then"] as JArray, at, depth);
                        if (st["else"] is JArray els && els.Count > 0) { Line("else"); Block(els, at + "e", depth); }
                        Line("end");
                        break;
                    }
                    case "while":
                    {
                        string c = Cond(r, Problem);
                        if (c == null) break;
                        Line("while " + c);
                        Block(st["steps"] as JArray, at, depth);
                        Line("end");
                        break;
                    }
                    case "repeat":
                    {
                        string count = r.N("count"), var = Str(st, "as").Trim().ToLowerInvariant();
                        if (count.Length == 0) count = "1";
                        if (var.Length > 0 && !IsName(var)) { Problem("the variable: letters, digits and _."); break; }
                        Line("repeat " + count + (var.Length > 0 ? " as " + var : ""));
                        Block(st["steps"] as JArray, at, depth);
                        Line("end");
                        break;
                    }
                    case "parallel":
                        Line("parallel");
                        Block(st["steps"] as JArray, at, depth);
                        Line("end");
                        break;
                    case "every":
                    {
                        string s = r.N("seconds");
                        Line("every " + (s.Length > 0 ? s : "10"));
                        Block(st["steps"] as JArray, at, depth);
                        Line("end");
                        break;
                    }
                    case "on":
                    {
                        string trigger = r.S("trigger"), station = r.Ref("station");
                        if (trigger == "near")
                        {
                            string xyz = r.Xyz("at");
                            if (station.Length == 0 || xyz.Length == 0) { Problem("the orbit and the point."); break; }
                            Line($"on near {station} {xyz} {(r.Arg("radius") is var rad && rad.Length > 0 ? rad : "500")}");
                        }
                        else Line(QuestReader.Join("on", trigger, station));
                        Block(st["steps"] as JArray, at, depth);
                        Line("end");
                        break;
                    }
                    case "ask":
                    case "vote":
                    {
                        string question = Clean(Text(st, "question")), speaker = Clean(Str(st, "speaker"));
                        var answers = (st["answers"] as JArray ?? new JArray()).OfType<JObject>().ToList();
                        if (question.Length == 0) { Problem("write the question."); break; }
                        if (answers.Count < 2 || answers.Count > 4) { Problem("2 to 4 answers."); break; }
                        var texts = answers.Select(a => Clean(Text(a, "text"))).ToList();
                        if (texts.Any(x => x.Length == 0)) { Problem("an answer has no text."); break; }
                        string sec = r.Arg("seconds");
                        Line(QuestReader.Join(type, who, sec.Length > 0 ? sec : "30") + " | " + (speaker.Length > 0 ? speaker + " : " : "") + question + " | " + string.Join(" | ", texts));
                        indent++;
                        for (int k = 0; k < answers.Count; k++)
                        {
                            if (!(answers[k]["steps"] is JArray list) || list.Count == 0) continue;
                            Line((type == "vote" ? "choice " : "answer ") + (k + 1));
                            Block(list, at + "." + (k + 1), depth);
                            Line("end");
                        }
                        if (type == "vote" && st["none"] is JArray none && none.Count > 0)
                        {
                            Line("choice 0");
                            Block(none, at + ".0", depth);
                            Line("end");
                        }
                        indent--;
                        Line("end");
                        break;
                    }
                    case "random":
                    {
                        var branches = (st["branches"] as JArray ?? new JArray()).OfType<JObject>().ToList();
                        if (branches.Count == 0) { Problem("add a branch."); break; }
                        var weights = branches.Select(b => Math.Max(0.0, b["weight"] is JValue w && (w.Type == JTokenType.Integer || w.Type == JTokenType.Float) ? (double)w : 1.0)).ToList();
                        if (branches.Count == 1) { Steps(branches[0]["steps"] as JArray ?? new JArray(), at + ".1.", depth + 1); break; }
                        string v = "_qr" + ++randoms;
                        string W(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);
                        Line($"set {v} = random(1, {W(weights.Sum())})");
                        double sum = 0;
                        int opened = 0;
                        for (int k = 0; k < branches.Count; k++)
                        {
                            sum += weights[k];
                            var list = branches[k]["steps"] as JArray ?? new JArray();
                            if (k < branches.Count - 1)
                            {
                                Line($"if {v} <= {W(sum)}");
                                Block(list, at + "." + (k + 1), depth);
                                Line("else");
                                indent++;
                                opened++;
                            }
                            else Steps(list, at + "." + (k + 1) + ".", depth + 1);
                        }
                        for (int k = 0; k < opened; k++) { indent--; Line("end"); }
                        break;
                    }
                    default:
                        Problem($"unknown step \"{type}\".");
                        break;
                }
            }

            static string Cond(QuestReader r, Action<string> problem)
            {
                string c = r.E("condition");
                if (c.Length == 0) { problem("write the condition."); return null; }
                return c;
            }

            void Objective(string text, string station)
            {
                if (text.Length > 0) Line("objective " + text);
                Line("target " + (station.Length > 0 ? station : "off"));
            }

            string Voice(JObject p)
            {
                string v = Clean(Str(p, "voice"));
                return v.Length > 0 && !v.Contains(" ") ? " [voice " + v + "]" : "";
            }
        }

        static string Str(JObject o, string k) => o[k] is JValue v && v.Type != JTokenType.Null ? v.ToString(CultureInfo.InvariantCulture) : "";

        /// <summary>A text field: a string, or one per language ({"en": ...}): the player's language, else English.</summary>
        static string Text(JObject o, string k)
        {
            if (o[k] is JObject t)
            {
                string lang = Data.Localization.Language;
                return (string)(t[lang] ?? t["en"] ?? t.Properties().FirstOrDefault()?.Value) ?? "";
            }
            return Str(o, k);
        }

        static int Int(JObject o, string k, int d) => o[k] is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) ? (int)Math.Round((double)v) : d;
        static string Num(JObject o, string k) =>
            o[k] is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) && (double)v >= 0 ? ((double)v).ToString("0.###", CultureInfo.InvariantCulture) : "";
        static bool Bool(JObject o, string k, bool d) => o[k] is JValue v && v.Type == JTokenType.Boolean ? (bool)v : d;

        /// <summary>A reference (a number or a key) as the script names it.</summary>
        static string Ref(JToken t) => t is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.String) ? Clean(v.ToString(CultureInfo.InvariantCulture)) : "";

        static List<string> Refs(JToken t) => t is JArray a ? a.Select(Ref).Where(x => x.Length > 0).ToList() : new List<string>();

        /// <summary>Text without the script's own marks (one line).</summary>
        public static string Clean(string s) => Clean(s, null);

        /// <summary>Text without the script's own marks; "{name}" / "{kills * 100}" stays when every word in it is one of
        /// these (the quest's variables, the game's state).</summary>
        public static string Clean(string s, HashSet<string> keep)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var b = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '#': b.Append("No."); break;
                    case '|': b.Append('/'); break;
                    case '{':
                    {
                        int close = s.IndexOf('}', i + 1);
                        if (keep != null && close > i + 1 && KeepsExpression(s.Substring(i + 1, close - i - 1), keep))
                        {
                            b.Append(s, i, close - i + 1);
                            i = close;
                        }
                        else b.Append('(');
                        break;
                    }
                    case '}': b.Append(')'); break;
                    case '\r': break;
                    case '\n': b.Append(' '); break;
                    default: b.Append(c); break;
                }
            }
            return b.ToString().Trim();
        }

        static bool KeepsExpression(string e, HashSet<string> keep)
        {
            if (e.IndexOfAny(new[] { '{', '|', '#', '"' }) >= 0) return false;
            if (!Regex.IsMatch(e, @"^[A-Za-z0-9_ +\-*/%().,<>=!]+$")) return false;
            foreach (Match m in Regex.Matches(e, "[A-Za-z_][A-Za-z0-9_]*"))
                if (!keep.Contains(m.Value.ToLowerInvariant())) return false;
            return Regex.IsMatch(e, "[A-Za-z_]");
        }

        /// <summary>An expression as written (one line; '#' would cut it).</summary>
        static string Expr(string s) => (s ?? "").Replace("\r", " ").Replace("\n", " ").Replace("#", "").Trim();

        // ---- an event graph's script as steps ------------------------------------------------------------------------

        static readonly string[] RaceWords = { "pirate", "terran", "vossk", "nivelian", "midorian", "void" };
        static readonly string[] BehaviourWords = { "enemy", "neutral", "friendly", "standing" };

    }
}
