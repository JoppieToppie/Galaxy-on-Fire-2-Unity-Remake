// EventRunnerCompile.cs
// Part of EventRunner: the compiler from the line form into the runner's steps (Compile, IsName), and EventRun's
// expressions: filling {expression} parts in, evaluating and naming values (Fill, Eval, Value; its Parser stays in EventRunner.cs).

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
                int first = steps.Count, depthHere = open.Count;
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
                    case "checkpoint":
                    {
                        if (open.Count > 0) { error = Err(X("mpEventCheckpointBlock", "a checkpoint goes on the main flow, outside If / While / Repeat / parallel / On blocks.")); return null; }
                        string cp = rest.Length > 0 ? rest : marker ?? ("line " + ln);
                        if (steps.Exists(x => x.op == Op.Checkpoint && x.a == cp)) { error = Err(string.Format(X("mpEventCheckpointTwice", "two checkpoints are called \"{0}\"."), cp)); return null; }
                        steps.Add(new Step { op = Op.Checkpoint, a = cp, line = ln });
                        break;
                    }
                    case "objective":
                        steps.Add(new Step { op = Op.Objective, a = rest.Replace("\\n", "\n"), line = ln });
                        break;
                    case "target":
                        if (rest.Length == 0) { error = Err(X("mpEventBadTarget", "target <station | off>.")); return null; }
                        steps.Add(new Step { op = Op.Target, a = rest, line = ln });
                        break;
                    case "questorbit":
                        if (rest.Length == 0) { error = Err(X("mpEventBadQuestOrbit", "questorbit <station> [off].")); return null; }
                        steps.Add(new Step { op = Op.QuietOrbit, a = rest, line = ln });
                        break;
                    case "startquest":
                        if (rest.Length == 0 || rest.Contains(" ")) { error = Err(X("mpEventBadStartQuest", "startquest <name>.")); return null; }
                        steps.Add(new Step { op = Op.StartQuest, a = rest, line = ln });
                        break;
                    case "event":
                        error = Err(X("mpEventNested", "an event can't start another one."));
                        return null;
                    default:
                        steps.Add(new Step { op = Op.Command, a = line, line = ln });
                        break;
                }
                for (int k = first; k < steps.Count; k++) { steps[k].node ??= marker; steps[k].depth = depthHere; }
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

        sealed partial class EventRun
        {
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
                    string inner = line.Substring(open + 1, close - open - 1).Trim();
                    // {stationname(<expression>)} / {systemname(<expression>)}: a station's name / its system's (a station picked
                    // by randomstation() in a variable), not a number
                    var named = System.Text.RegularExpressions.Regex.Match(inner, @"^(stationname|systemname)\s*\((.*)\)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (named.Success) sb.Append(PlaceName((int)Math.Round(Eval(named.Groups[2].Value)), named.Groups[1].Value.ToLowerInvariant() == "systemname"));
                    else sb.Append(Format(Eval(inner)));
                    i = close + 1;
                }
                return sb.ToString();
            }

            /// <summary>A station's name or its system's ("" for none).</summary>
            static string PlaceName(int station, bool system)
            {
                var db = NetGame.Db;
                var st = db.Stations.Find(x => x.index == station);
                if (st == null) return "";
                return system ? db.Systems.Find(x => x.index == st.system)?.name ?? st.systemName ?? "" : st.name ?? "";
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
                    case "time": return EventHost.Now - startTime;
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
                        foreach (var p in EventHost.Pilots)
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
                switch (name)
                {
                    case "campaign": return Session.FreePlay ? -1 : Session.CampaignMission;
                    case "credits": return Session.Credits;
                    case "rank": return Session.Rank;
                    case "station": return Session.StationIndex;
                    case "system": return NetGame.Db.Stations.Find(s => s.index == Session.StationIndex)?.system ?? -1;
                    case "ship": return Session.ShipIndex;
                    case "kills": return Session.Kills;
                    case "systemsvisited": return VisitedSystems();   // remake: 2+ = the player has left the starting system
                    case "freeflight": return FlightStyles.Current == FlightStyles.Free ? 1 : 0;   // the flight style in use (ModFlight)
                    case "chorusflight": return FlightStyles.Current == FlightStyles.Chorus ? 1 : 0;   // remake, 2026-11-11
                }
                throw new Exception(string.Format(X("mpEventNoVar", "no variable \"{0}\" (set it first)."), name));
            }
        }
    }
}
