// QuestFileFromScript.cs
// QuestFile: an event graph's script as steps (part of the partial QuestFile; its fields are in QuestFile.cs).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace GoF2Remake.Events
{
    public static partial class QuestFile
    {
        /// <summary>A script (EventRunner's line form: an event graph's, EventGraphScript) as a .gof2quest's JSON: the header,
        /// the settings as variables, the blocks as nested lists, the lines that make a step of their own as that step and the
        /// rest as Command steps (they run as they did). Notes: what changed on the way.</summary>
        public static JObject FromScript(string script, string fallbackTitle, List<string> notes) => FromScript(script, fallbackTitle, notes, null);
        /// <summary>The same, each step placed where its node was ('places': node id -> x, y; the script's "#@id" markers say
        /// which node a line came from).</summary>
        public static JObject FromScript(string script, string fallbackTitle, List<string> notes, Dictionary<string, (float x, float y)> places)
        {
            var placed = new HashSet<string>();
            var o = new JObject { ["format"] = Format };
            var variables = new JArray();
            var root = new JArray();
            // The open blocks: the list steps go into, what opened it ("if", "else", "ask" ...), its step.
            var open = new Stack<(JArray list, string kind, JObject step)>();
            var stray = new JArray();
            JArray Cur()
            {
                if (open.Count == 0) return root;
                if (open.Peek().list != null) return open.Peek().list;
                notes.Add("A line between a question and its answers was left out.");
                return stray;
            }
            string kind = "event";
            bool titled = false, sawStep = false;
            int commands = 0;
            var lines = (script ?? "").Replace("\r", "").Split('\n');
            for (int n = 0; n < lines.Length; n++)
            {
                string raw = lines[n];
                int hash = raw.IndexOf('#');
                string comment = hash >= 0 && (hash + 1 >= raw.Length || raw[hash + 1] != '@') ? raw.Substring(hash + 1).Trim() : null;
                string line = (hash >= 0 ? raw.Substring(0, hash) : raw).Trim();
                if (line.Length == 0)
                {
                    if (!string.IsNullOrEmpty(comment)) Cur().Add(new JObject { ["type"] = "note", ["text"] = comment });
                    continue;
                }
                if (line.StartsWith("/")) line = line.Substring(1);
                int sp = line.IndexOf(' ');
                string word = (sp < 0 ? line : line.Substring(0, sp)).ToLowerInvariant();
                string rest = sp < 0 ? "" : line.Substring(sp + 1).Trim();
                string marker = hash >= 0 && hash + 1 < raw.Length && raw[hash + 1] == '@' ? raw.Substring(hash + 2).Trim() : null;
                JObject Add(JObject st)
                {
                    Cur().Add(st);
                    sawStep = true;
                    // The node's place, once per node (a node may write several lines: its first step takes it).
                    if (marker != null && places != null && places.TryGetValue(marker, out var p) && placed.Add(marker)) QuestGraph.SetPos(st, p.x, p.y);
                    return st;
                }
                JObject Command() { commands++; return Add(new JObject { ["type"] = "command", ["line"] = line }); }
                JObject Push(JObject st, string k, string listKey)
                {
                    Add(st);
                    var list = new JArray();
                    st[listKey] = list;
                    open.Push((list, k, st));
                    return st;
                }
                switch (word)
                {
                    case "mission":
                    {
                        int s2 = rest.IndexOf(' ');
                        string key = (s2 < 0 ? rest : rest.Substring(0, s2)).ToLowerInvariant(), v = s2 < 0 ? "" : rest.Substring(s2 + 1).Trim();
                        switch (key)
                        {
                            case "title": o["title"] = v; titled = true; break;
                            case "kind": kind = v.ToLowerInvariant() == "quest" ? "quest" : v.ToLowerInvariant() == "mission" ? "mission" : "event"; break;
                            case "starts": o["startsWhen"] = v; break;
                            case "repeat":
                                if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double rp)) o["repeat"] = rp;
                                break;
                            case "reward": if (int.TryParse(v, out int rw)) o["reward"] = rw; else notes.Add("The reward \"" + v + "\" isn't a number: left out."); break;
                            case "offer": o["offer"] = v.Replace("\\n", "\n"); break;
                            case "client": o["client"] = v; break;
                            case "players":
                            {
                                var f = v.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                if (f.Length >= 1 && int.TryParse(f[0], out int lo)) o["players"] = new JArray(lo, f.Length > 1 && int.TryParse(f[1], out int hi) ? hi : lo);
                                break;
                            }
                            case "chance": if (int.TryParse(v.TrimEnd('%'), out int pc)) o["chance"] = pc; break;
                            case "takers": if (v == "station" || v == "any") o["takers"] = v; break;
                            case "stations":
                                o["stations"] = new JArray(v.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Select(x => int.TryParse(x, out int i) ? (JToken)i : x));
                                break;
                        }
                        break;
                    }
                    case "param":
                    {
                        int eq = rest.IndexOf('=');
                        if (eq > 0) variables.Add(new JObject { ["name"] = rest.Substring(0, eq).Trim(), ["value"] = rest.Substring(eq + 1).Trim(), ["setting"] = true });
                        break;
                    }
                    case "set":
                    {
                        int eq = rest.IndexOf('=');
                        string name = eq > 0 ? rest.Substring(0, eq).Trim() : "", value = eq > 0 ? rest.Substring(eq + 1).Trim() : "";
                        if (!sawStep && open.Count == 0 && IsName(name) && !name.StartsWith("_"))
                            variables.Add(new JObject { ["name"] = name, ["value"] = value });
                        else Add(new JObject { ["type"] = "set", ["variable"] = name, ["value"] = value });
                        break;
                    }
                    case "wait":
                        if (rest.StartsWith("until ", StringComparison.OrdinalIgnoreCase))
                        {
                            string cond = rest.Substring(6).Trim();
                            var st = new JObject { ["type"] = "waituntil" };
                            int t = cond.LastIndexOf(" timeout ", StringComparison.OrdinalIgnoreCase);
                            if (t >= 0) { st["timeout"] = NumOrText(cond.Substring(t + 9).Trim()); cond = cond.Substring(0, t).Trim(); }
                            st["condition"] = cond;
                            Add(st);
                        }
                        else Add(new JObject { ["type"] = "wait", ["seconds"] = NumOrText(rest) });
                        break;
                    case "if": Push(new JObject { ["type"] = "if", ["condition"] = rest, ["else"] = new JArray() }, "if", "then"); break;
                    case "else":
                        if (open.Count > 0 && open.Peek().kind == "if")
                        {
                            var (_, _, st) = open.Pop();
                            var list = new JArray();
                            st["else"] = list;
                            open.Push((list, "else", st));
                        }
                        else notes.Add($"Line {n + 1}: an else without its if, left out.");
                        break;
                    case "while": Push(new JObject { ["type"] = "while", ["condition"] = rest }, "while", "steps"); break;
                    case "repeat":
                    {
                        var st = new JObject { ["type"] = "repeat" };
                        int asAt = rest.LastIndexOf(" as ", StringComparison.OrdinalIgnoreCase);
                        st["count"] = NumOrText(asAt >= 0 ? rest.Substring(0, asAt).Trim() : rest);
                        if (asAt >= 0) st["as"] = rest.Substring(asAt + 4).Trim();
                        Push(st, "repeat", "steps");
                        break;
                    }
                    case "parallel": Push(new JObject { ["type"] = "parallel" }, "parallel", "steps"); break;
                    case "every": Push(new JObject { ["type"] = "every", ["seconds"] = NumOrText(rest) }, "every", "steps"); break;
                    case "on":
                    {
                        var w = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        var st = new JObject { ["type"] = "on", ["trigger"] = w.Length > 0 ? w[0].ToLowerInvariant() : "died" };
                        if (w.Length > 0 && w[0].ToLowerInvariant() == "near" && w.Length >= 6)
                        {
                            st["station"] = RefToken(string.Join(" ", w.Skip(1).Take(w.Length - 5)));
                            st["at"] = string.Join(" ", w.Skip(w.Length - 4).Take(3));
                            st["radius"] = NumOrText(w[w.Length - 1]);
                        }
                        else if (w.Length > 1) st["station"] = RefToken(string.Join(" ", w.Skip(1)));
                        Push(st, "on", "steps");
                        break;
                    }
                    case "ask":
                    case "vote":
                    {
                        int bar = rest.IndexOf('|');
                        if (bar < 0) { Command(); break; }
                        var head = rest.Substring(0, bar).Trim();
                        var parts = rest.Substring(bar + 1).Split('|').Select(x => x.Trim()).ToList();
                        int last = head.LastIndexOf(' ');
                        string who = last > 0 ? head.Substring(0, last).Trim() : "@a", secs = last > 0 ? head.Substring(last + 1) : head;
                        var st = new JObject { ["type"] = word, ["seconds"] = NumOrText(secs) };
                        if (who != "@a") st["who"] = who;
                        string q = parts.Count > 0 ? parts[0] : "";
                        int colon = q.IndexOf(" : ", StringComparison.Ordinal);
                        if (colon > 0) { st["speaker"] = q.Substring(0, colon).Trim(); q = q.Substring(colon + 3).Trim(); }
                        st["question"] = q;
                        st["answers"] = new JArray(parts.Skip(1).Select(a => new JObject { ["text"] = a, ["steps"] = new JArray() }));
                        if (word == "vote") st["none"] = new JArray();
                        Add(st);
                        open.Push((null, word, st));
                        break;
                    }
                    case "answer":
                    case "choice":
                    {
                        if (open.Count == 0 || (open.Peek().kind != "ask" && open.Peek().kind != "vote") || !int.TryParse(rest, out int k))
                        { notes.Add($"Line {n + 1}: \"{line}\" outside its question, left out."); break; }
                        var q = open.Peek().step;
                        JArray list;
                        if (k == 0) { list = new JArray(); q["none"] = list; }
                        else if (q["answers"] is JArray answers && k <= answers.Count) { list = new JArray(); answers[k - 1]["steps"] = list; }
                        else { notes.Add($"Line {n + 1}: no answer {k}, left out."); break; }
                        open.Push((list, word, q));
                        break;
                    }
                    case "end":
                        if (open.Count > 0) open.Pop();
                        else notes.Add($"Line {n + 1}: an end without its block, left out.");
                        break;
                    case "objective":
                        Add(new JObject { ["type"] = "objective", ["text"] = rest.Replace("\\n", "\n") });
                        break;
                    case "target":
                    {
                        // "objective X" + "target S" (what a Set Objective node writes) is one step.
                        var list = Cur();
                        if (list.Count > 0 && list[list.Count - 1] is JObject prev && (string)prev["type"] == "objective" && prev["station"] == null)
                        { if (rest != "off") prev["station"] = RefToken(rest); }
                        else Add(new JObject { ["type"] = "target", ["station"] = rest == "off" ? null : RefToken(rest) });
                        break;
                    }
                    case "questorbit":
                    {
                        var w = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                        bool off = w.Count > 1 && w[w.Count - 1].ToLowerInvariant() == "off";
                        if (off) w.RemoveAt(w.Count - 1);
                        Add(new JObject { ["type"] = "questorbit", ["station"] = RefToken(string.Join(" ", w)), ["on"] = !off });
                        break;
                    }
                    case "checkpoint": Add(new JObject { ["type"] = "checkpoint", ["name"] = rest }); break;
                    case "startquest": Add(new JObject { ["type"] = "startquest", ["quest"] = rest }); break;
                    case "startevent":
                    {
                        int s2 = rest.IndexOf(' ');
                        var st = new JObject { ["type"] = "startevent", ["event"] = s2 < 0 ? rest : rest.Substring(0, s2) };
                        if (s2 > 0) st["settings"] = rest.Substring(s2 + 1).Trim();
                        Add(st);
                        break;
                    }
                    case "complete":
                    {
                        int bar = rest.IndexOf('|');
                        string rw = (bar < 0 ? rest : rest.Substring(0, bar)).Trim(), title = bar < 0 ? "" : rest.Substring(bar + 1).Trim();
                        if (rw.Length > 0 && !int.TryParse(rw, out _)) { Command(); break; }
                        var st = new JObject { ["type"] = "complete" };
                        if (rw.Length > 0) st["reward"] = int.Parse(rw, CultureInfo.InvariantCulture);
                        if (title.Length > 0) st["title"] = title;
                        Add(st);
                        break;
                    }
                    case "fail": Add(rest.Length > 0 ? new JObject { ["type"] = "fail", ["title"] = rest } : new JObject { ["type"] = "fail" }); break;
                    case "stop": Add(new JObject { ["type"] = "stop" }); break;
                    case "score": Add(new JObject { ["type"] = "score", ["mode"] = rest }); break;
                    case "winner": Add(new JObject { ["type"] = "winner", ["mode"] = rest }); break;
                    case "scoreboard":
                        Add(rest.StartsWith("on") ? new JObject { ["type"] = "scoreboard", ["show"] = true, ["title"] = rest.Substring(2).Trim() } : new JObject { ["type"] = "scoreboard", ["show"] = false });
                        break;
                    case "pvp": Add(new JObject { ["type"] = "pvp", ["on"] = rest.Trim().ToLowerInvariant() == "on" }); break;
                    case "dialog":
                    {
                        if (!SplitWho(rest, out string who, out string body) || body.Split('|').Any(x => x.Trim().StartsWith("reward", StringComparison.OrdinalIgnoreCase))) { Command(); break; }
                        var pages = new JArray();
                        string last = "player";
                        bool ok = true;
                        foreach (var part in body.Split('|'))
                        {
                            string p = part.Trim();
                            int colon = p.IndexOf(" : ", StringComparison.Ordinal);
                            var page = new JObject();
                            if (colon > 0) { last = p.Substring(0, colon).Trim(); p = p.Substring(colon + 3).Trim(); }
                            else if (p.StartsWith(": ")) p = p.Substring(2);
                            var vm = Regex.Match(p, @"\s*\[voice ([^\]\s]+)\]");
                            if (vm.Success) { page["voice"] = vm.Groups[1].Value; p = p.Remove(vm.Index, vm.Length).Trim(); }
                            if (p.Length == 0) { ok = false; break; }
                            page["speaker"] = last;
                            page["text"] = p;
                            pages.Add(page);
                        }
                        if (!ok || pages.Count == 0) { Command(); break; }
                        var st = new JObject { ["type"] = "dialog", ["pages"] = pages };
                        if (who != "@a") st["who"] = who;
                        Add(st);
                        break;
                    }
                    case "radio":
                    {
                        if (!SplitWho(rest, out string who, out string body)) { Command(); break; }
                        int colon = body.IndexOf(" : ", StringComparison.Ordinal);
                        if (colon <= 0) { Command(); break; }
                        var st = new JObject { ["type"] = "radio", ["speaker"] = body.Substring(0, colon).Trim() };
                        string text = body.Substring(colon + 3).Trim();
                        var vm = Regex.Match(text, @"\s*\[voice ([^\]\s]+)\]");
                        if (vm.Success) { st["voice"] = vm.Groups[1].Value; text = text.Remove(vm.Index, vm.Length).Trim(); }
                        st["text"] = text;
                        if (who != "@a") st["who"] = who;
                        Add(st);
                        break;
                    }
                    case "music":
                    case "sound":
                    case "letterbox":
                    {
                        if (!SplitWho(rest, out string who, out string body) || body.Contains(" ")) { Command(); break; }
                        var st = word == "letterbox" ? new JObject { ["type"] = "letterbox", ["on"] = body.ToLowerInvariant() == "on" }
                               : new JObject { ["type"] = word, [word == "music" ? "track" : "sound"] = body };
                        if (who != "@a") st["who"] = who;
                        Add(st);
                        break;
                    }
                    case "cutscene":
                    {
                        if (!SplitWho(rest, out string who, out string body)) { Command(); break; }
                        var w = body.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        JObject st;
                        if (w.Length == 1 && w[0] == "end") st = new JObject { ["type"] = "cutsceneend" };
                        else if (w.Length > 0 && w[0] == "start")
                            st = new JObject { ["type"] = "cutscene", ["letterbox"] = !w.Contains("nobars"), ["freeze"] = w.Contains("freeze"), ["invulnerable"] = w.Contains("invulnerable") };
                        else { Command(); break; }
                        if (who != "@a") st["who"] = who;
                        Add(st);
                        break;
                    }
                    default:
                    {
                        SplitWho(rest, out string who, out string body);
                        var typed = body.StartsWith("@") ? null : Typed(word, who, body);
                        if (typed == null) { Command(); break; }
                        if (who != "@a") typed["who"] = who;
                        Add(typed);
                        break;
                    }
                }
            }
            if (open.Count > 0) notes.Add("A block had no end: it was closed at the end.");
            if (!titled && kind != "event") o["title"] = fallbackTitle;
            if (kind == "event" && titled) kind = "mission";   // an old graph with a Mission title is a bar mission (EventRunner.Info)
            o["kind"] = kind;
            if (variables.Count > 0) o["variables"] = variables;
            // A quest's script ends with "complete" by itself: not a step of its own. One that ends without it doesn't complete
            // after its last step.
            if (kind != "event")
            {
                var lastTop = root.Count > 0 ? root[root.Count - 1] as JObject : null;
                string lastType = (string)lastTop?["type"];
                if (lastType == "complete" && lastTop.Count == 1) lastTop.Remove();
                else if (lastType != "complete" && lastType != "fail") o["autoComplete"] = false;
            }
            o["steps"] = root;
            if (commands > 0) notes.Add(commands == 1 ? "1 line stays a Command step." : $"{commands} lines stay Command steps.");
            return o;
        }
        /// <summary>A line's words: spaces inside { } and [ ] don't split ("{random(2, 4)}", "@alive[orbit=Var Hastra]").</summary>
        static List<string> Words(string body)
        {
            var words = new List<string>();
            var cur = new StringBuilder();
            int depth = 0;
            foreach (char c in body)
            {
                if (c == '{' || c == '[') depth++;
                else if ((c == '}' || c == ']') && depth > 0) depth--;
                if (c == ' ' && depth == 0) { if (cur.Length > 0) { words.Add(cur.ToString()); cur.Clear(); } continue; }
                cur.Append(c);
            }
            if (cur.Length > 0) words.Add(cur.ToString());
            return words;
        }
        static bool IsNumberOrExpr(string w) => double.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out _) || w.StartsWith("{") && w.EndsWith("}");
        /// <summary>A command line the builder has a step for, as that step (null: it stays a Command step). Only the forms the
        /// step writes back the same way.</summary>
        static JObject Typed(string word, string who, string body)
        {
            var w = Words(body);
            JObject St(string type) => new JObject { ["type"] = type };
            switch (word)
            {
                case "heal": case "ammo": case "kill": case "reveal": case "peace":
                    return w.Count == 0 ? St(word) : null;
                case "title":
                {
                    var st = St("title");
                    var m = Regex.Match(body, @"\s+for\s+(\S+)$");
                    string t = body;
                    if (m.Success && IsNumberOrExpr(m.Groups[1].Value)) { st["seconds"] = NumOrText(m.Groups[1].Value); t = body.Substring(0, m.Index); }
                    int bar = t.IndexOf('|');
                    st["title"] = (bar < 0 ? t : t.Substring(0, bar)).Trim();
                    if (bar >= 0) st["subtitle"] = t.Substring(bar + 1).Trim();
                    return st;
                }
                case "timer":
                {
                    if (w.Count == 1 && w[0] == "stop") { var st = St("timer"); st["stop"] = true; return st; }
                    if (w.Count == 0 || !IsNumberOrExpr(w[0])) return null;
                    var t2 = St("timer");
                    t2["seconds"] = NumOrText(w[0]);
                    if (w.Count > 1) t2["label"] = string.Join(" ", w.Skip(1));
                    return t2;
                }
                case "points":
                    return w.Count == 1 ? new JObject { ["type"] = "points", ["amount"] = NumOrText(w[0]) } : null;
                case "credits":
                    return w.Count == 1 && IsNumberOrExpr(w[0]) ? new JObject { ["type"] = "credits", ["amount"] = NumOrText(w[0]) } : null;
                case "standing":
                {
                    // standing <race|race> <amount> [set] (QuestSteps "standing")
                    if ((w.Count != 2 && !(w.Count == 3 && w[2] == "set")) || !IsNumberOrExpr(w[1])) return null;
                    var st = new JObject { ["type"] = "standing", ["race"] = w[0], ["amount"] = NumOrText(w[1]) };
                    if (w.Count == 3) st["set"] = true;
                    return st;
                }
                case "restrict":
                    return w.Count == 1 && new[] { "jumps", "docking", "all", "khador", "off" }.Contains(w[0]) ? new JObject { ["type"] = "restrict", ["what"] = w[0] } : null;
                case "take":
                {
                    // take <item> <amount | all> [mounted] (QuestSteps "take")
                    int end = w.Count;
                    bool mounted = end > 0 && w[end - 1] == "mounted";
                    if (mounted) end--;
                    if (end < 2 || !(w[end - 1] == "all" || IsNumberOrExpr(w[end - 1]))) return null;
                    var st = new JObject { ["type"] = "take", ["item"] = RefToken(string.Join(" ", w.Take(end - 1))) };
                    if (w[end - 1] != "all") st["amount"] = NumOrText(w[end - 1]);
                    if (mounted) st["mounted"] = true;
                    return st;
                }
                case "wormhole":
                {
                    if (w.Count == 1 && (w[0] == "close" || w[0] == "off")) return St("wormhole");
                    int to = w.IndexOf("to");
                    if (to != 1 && to != 4) return null;
                    var st = new JObject { ["type"] = "wormhole", ["station"] = RefToken(w[0]) };
                    if (to == 4) { if (!w.Skip(1).Take(3).All(IsNumberOrExpr)) return null; st["at"] = string.Join(" ", w.Skip(1).Take(3)); }
                    int end = w.Count;
                    if (w.Count >= to + 4 && w[w.Count - 2] == "for" && IsNumberOrExpr(w[w.Count - 1])) { st["seconds"] = NumOrText(w[w.Count - 1]); end = w.Count - 2; }
                    if (end != to + 2) return null;
                    if (w[to + 1] == "void") st["void"] = true; else st["to"] = RefToken(w[to + 1]);
                    return st;
                }
                case "waypoint":
                    if (w.Count == 1 && w[0] == "off") return St("waypoint");
                    return w.Count == 4 && w.Skip(1).All(IsNumberOrExpr) ? new JObject { ["type"] = "waypoint", ["station"] = RefToken(w[0]), ["at"] = string.Join(" ", w.Skip(1)) } : null;
                case "respawn":
                {
                    if (w.Count == 1 && w[0] == "off") return St("respawn");
                    if (w.Count == 0) return null;
                    var st = new JObject { ["type"] = "respawn", ["station"] = RefToken(w[0]) };
                    int i = 1;
                    if (w.Count >= 4 && w.Skip(1).Take(3).All(IsNumberOrExpr)) { st["at"] = string.Join(" ", w.Skip(1).Take(3)); i = 4; }
                    for (; i + 1 < w.Count; i += 2)
                    {
                        if (w[i] != "spread" && w[i] != "delay") return null;
                        st[w[i]] = NumOrText(w[i + 1]);
                    }
                    return i == w.Count ? st : null;
                }
                case "provoke":
                {
                    var st = St("provoke");
                    int i = 0;
                    if (i < w.Count && RaceWords.Contains(w[i])) st["race"] = w[i++];
                    if (i + 1 < w.Count && w[i] == "within") { st["within"] = NumOrText(w[i + 1]); i += 2; }
                    return i == w.Count ? st : null;
                }
                case "tp":
                {
                    if (w.Count == 1) return new JObject { ["type"] = "teleport", ["station"] = RefToken(w[0]) };
                    if (w.Count == 2 && w[1] == "dock") return new JObject { ["type"] = "teleport", ["station"] = RefToken(w[0]), ["dock"] = true };
                    if (w.Count == 4 && w.Skip(1).All(IsNumberOrExpr)) return new JObject { ["type"] = "teleport", ["station"] = RefToken(w[0]), ["at"] = string.Join(" ", w.Skip(1)) };
                    return null;
                }
                case "reward":
                {
                    int bar = body.IndexOf('|');
                    var st = St("reward");
                    if (bar >= 0) st["title"] = body.Substring(bar + 1).Trim();
                    var items = new JArray();
                    foreach (var part in (bar < 0 ? body : body.Substring(0, bar)).Split('+').Select(x => x.Trim()).Where(x => x.Length > 0))
                    {
                        if (IsNumberOrExpr(part)) { if (st["credits"] != null) return null; st["credits"] = NumOrText(part); continue; }
                        var m = Regex.Match(part, @"^(.+?)\s+(\d+)$");
                        if (m.Success) items.Add(new JObject { ["item"] = RefToken(m.Groups[1].Value.Trim()), ["amount"] = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) });
                        else items.Add(new JObject { ["item"] = RefToken(part), ["amount"] = 1 });
                    }
                    if (items.Count > 0) st["items"] = items;
                    return st["credits"] != null || items.Count > 0 ? st : null;
                }
                case "spawn":
                {
                    if (w.Count == 0) return null;
                    var st = St("spawn");
                    int end = w.Count;
                    int at = w.LastIndexOf("at");
                    if (at >= 1 && at == w.Count - 4 && w.Skip(at + 1).All(IsNumberOrExpr)) { st["at"] = string.Join(" ", w.Skip(at + 1)); end = at; }
                    int named = w.IndexOf("named");
                    if (named >= 1 && named < end) { st["name"] = string.Join(" ", w.Skip(named + 1).Take(end - named - 1)); end = named; }
                    if (end == 2 && w[0] == "type")
                    {
                        // spawn type <key> [named ...] [at x y z] (QuestSteps "spawntype")
                        st["type"] = "spawntype";
                        st["npc"] = w[1];
                        return st;
                    }
                    foreach (string f in new[] { "guns", "hull" })
                        if (end >= 3 && w[end - 2] == f && IsNumberOrExpr(w[end - 1])) { st[f] = NumOrText(w[end - 1]); end -= 2; }
                    int tur = w.IndexOf("turrets");
                    if (tur >= 1 && tur == end - 2) { st["turrets"] = w[tur + 1]; end = tur; }
                    if (end >= 2 && BehaviourWords.Contains(w[end - 1])) { st["behaviour"] = w[end - 1]; end--; }
                    if (end >= 2 && IsNumberOrExpr(w[end - 1])) { st["count"] = NumOrText(w[end - 1]); end--; }
                    if (end >= 2 && RaceWords.Contains(w[end - 1])) { st["race"] = w[end - 1]; end--; }
                    if (end < 1) return null;
                    st["ship"] = RefToken(string.Join(" ", w.Take(end)));
                    return st;
                }
            }
            return null;
        }
        /// <summary>A Unity-made event graph (.gof2event) as a .gof2quest, its nodes where they were on the Unity canvas.
        /// Null with the problems when its graph has errors.</summary>
        public static JObject FromGraphFile(string yaml, string fallbackTitle, List<string> notes, List<EventGraphScript.Problem> problems)
        {
            EventGraphData data;
            try { data = EventGraphFile.Read(yaml); }
            catch (Exception e) { problems.Add(new EventGraphScript.Problem { message = "can't read the graph file: " + e.Message }); return null; }
            string script = EventGraphScript.Compile(data, problems);
            if (problems.Any(p => !p.warning) || script.Length == 0) return null;
            var places = new Dictionary<string, (float, float)>();
            foreach (var n in data.nodes) if (n.id != null) places[n.id] = (n.x, n.y);
            var json = FromScript(script, fallbackTitle, notes, places);
            var start = data.nodes.FirstOrDefault(n => n.kind == "EventStartNode" && (data.mainGraph == null || n.graph == data.mainGraph));
            if (start != null) QuestGraph.SetPos(json, start.x, start.y, "startPos");
            return json;
        }
        /// <summary>"@a rest" / "@alive[orbit=Var Hastra] rest" -> the selector and the rest (a command without one: "@a").</summary>
        static bool SplitWho(string rest, out string who, out string body)
        {
            who = "@a";
            body = rest.Trim();
            if (!body.StartsWith("@")) return body.Length > 0;
            int open = body.IndexOf('['), space = body.IndexOf(' ');
            int end = open >= 0 && (space < 0 || open < space) ? body.IndexOf(']') + 1 : space < 0 ? body.Length : space;
            if (end <= 0) return false;
            who = body.Substring(0, end).Trim();
            body = body.Substring(end).Trim();
            return body.Length > 0;
        }
        static JToken NumOrText(string s) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? (Math.Abs(d - Math.Round(d)) < 1e-9 ? (JToken)(long)Math.Round(d) : d) : s.Trim('{', '}');
        static JToken RefToken(string s) => int.TryParse(s, out int i) ? (JToken)i : s;
    }
}
