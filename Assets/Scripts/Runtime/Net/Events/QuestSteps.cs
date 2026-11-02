// QuestSteps.cs
// Remake-only: every step a .gof2quest can hold (Events.QuestFile; Modding/README.md "Quests made in the Mod Builder"): its
// type word, its group in the Mod Builder's picker, its fields (the JSON keys, what kind of value each takes, defaults) and,
// for the plain ones, the script line it makes (EventRunner's line form, the same EventGraphScript writes for a node). The
// steps whose lines need more (a conversation's pages, a fight, blocks such as If / Ask) are written by QuestFile itself and
// only described here. The Mod Builder (ModBuilderViewQuests) builds its step pages from this table, so a step added here
// is editable there at once. Plain C#.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace GoF2Remake.Events
{
    /// <summary>What a step field holds (how the builder edits it).</summary>
    public enum QuestFieldKind
    {
        Text, LongText, Int, Float, Bool, Choice, Station, Ship, Item, Speaker, Quest, Music, Sound, Condition, Expression,
        /// <summary>A camera / ship order place: "player [right up forward]", "station [x y z]", "x y z", "ship &lt;name&gt; [x y z]".</summary>
        Place,
        /// <summary>Whom a step is for: "@a" (everyone the quest covers), "@trigger" (an On block's player), a selector.</summary>
        Who,
        /// <summary>"x y z" in game units.</summary>
        Xyz,
        /// <summary>A ship's name as a Spawn / Fight step gave it.</summary>
        ShipName,
        Race,
        Variable,
        /// <summary>An event graph's or quest's name (Start event).</summary>
        Event,
        /// <summary>A star system: its number, its name or a mod's key "mod_id:id".</summary>
        System,
        /// <summary>Remake: an NPC ship type of npcships.json (battleship, carrier, vossk_battleship, freighter, pirate_boss, a
        /// mod's "mod_id:id").</summary>
        NpcShip,
    }

    public sealed class QuestField
    {
        public string key, english, help;
        public QuestFieldKind kind;
        /// <summary>Choice: the values written; labels (English) beside them.</summary>
        public string[] values, labels;
        public JToken def;
        public float min = float.MinValue, max = float.MaxValue;
        /// <summary>Optional: no value writes nothing (the command's own default).</summary>
        public bool optional;
        /// <summary>Shown only while another field has one of these values ("action" = "flee").</summary>
        public string showKey;
        public string[] showValues;

        public bool Shown(JObject step)
        {
            if (showKey == null) return true;
            string v = step[showKey] is JValue j && j.Type != JTokenType.Null ? j.ToString(CultureInfo.InvariantCulture)
                     : fieldDefault(step, showKey);
            return showValues.Contains(v);
        }

        /// <summary>Set by QuestSteps: a sibling field's default (for Shown).</summary>
        internal Func<JObject, string, string> fieldDefault = (s, k) => "";
    }

    /// <summary>A nested list of steps inside a block step ("then", "else", "steps").</summary>
    public struct QuestBlock
    {
        public string key, english;
        public QuestBlock(string key, string english) { this.key = key; this.english = english; }
    }

    public sealed class QuestStepType
    {
        public string type, group, english, what;
        public QuestField[] fields = new QuestField[0];
        public QuestBlock[] blocks = new QuestBlock[0];
        /// <summary>Ask / Vote: "answers" [{ text, steps }]; Random: "branches" [{ weight, steps }].</summary>
        public bool answers, branches;
        /// <summary>The fields are edited by the builder's own page (a conversation's pages, a fight's ships...).</summary>
        public bool custom;
        /// <summary>The line for a plain step (null: QuestFile writes it).</summary>
        internal Func<QuestReader, string> line;
        /// <summary>Only on the quest's main line (not inside a block): a checkpoint.</summary>
        public bool topOnly;

        public QuestField Field(string key) => fields.FirstOrDefault(f => f.key == key);
    }

    /// <summary>One step's fields as a line's parts.</summary>
    public sealed class QuestReader
    {
        public readonly JObject step;
        readonly Action<string> problem;
        internal readonly Func<string, string> clean;
        readonly QuestStepType type;
        /// <summary>A value node's expression by its id (QuestGraph.Resolve); null: no graph.</summary>
        internal readonly Func<string, string> resolve;

        internal QuestReader(JObject step, QuestStepType type, Action<string> problem, Func<string, string> clean, Func<string, string> resolve = null)
        {
            this.step = step;
            this.type = type;
            this.problem = problem;
            this.clean = clean;
            this.resolve = resolve;
        }

        /// <summary>The expression a field wired to a value node gives (null: not wired).</summary>
        string Wired(string k)
        {
            string id = QuestGraph.WiredValue(step[k]);
            if (id == null) return null;
            if (resolve == null) { problem("a wired value outside a graph."); return "0"; }
            return resolve(id);
        }

        static bool Plain(string s) => System.Text.RegularExpressions.Regex.IsMatch(s ?? "", @"^[A-Za-z0-9_:'.\-]+$");

        /// <summary>An expression field (a condition, a value): typed, or the wired nodes'.</summary>
        public string E(string k)
        {
            string w = Wired(k);
            if (w != null) return w;
            var t = Value(k);
            return t is JValue v && v.Type != JTokenType.Null ? v.ToString(CultureInfo.InvariantCulture).Replace("\r", " ").Replace("\n", " ").Replace("#", "").Trim() : "";
        }

        public void Problem(string m) => problem(m);

        JToken Value(string k)
        {
            var t = step[k];
            if (t != null && t.Type != JTokenType.Null) return t;
            return type?.Field(k)?.def;
        }

        public bool Has(string k) => step[k] != null && step[k].Type != JTokenType.Null && !(step[k].Type == JTokenType.String && ((string)step[k]).Trim().Length == 0);

        public string S(string k)
        {
            string w = Wired(k);
            if (w != null)
            {
                var kind = type?.Field(k)?.kind;
                return kind == QuestFieldKind.Who || kind == QuestFieldKind.Station || kind == QuestFieldKind.Condition || kind == QuestFieldKind.Expression || Plain(w) ? w : "{" + w + "}";
            }
            var t = Value(k);
            if (t is JObject lang)
            {
                string l = Data.Localization.Language;
                t = lang[l] ?? lang["en"] ?? lang.Properties().FirstOrDefault()?.Value;
            }
            return t is JValue v && v.Type != JTokenType.Null ? clean(v.ToString(CultureInfo.InvariantCulture)) : "";
        }

        /// <summary>A number or an expression as an expression ("3", "minutes * 60"): for the lines the runner evaluates
        /// itself (wait, every, repeat, points).</summary>
        public string N(string k)
        {
            string a = Arg(k);
            return a.StartsWith("{") && a.EndsWith("}") ? a.Substring(1, a.Length - 2) : a;
        }

        /// <summary>A number, or an expression in { } (a command's argument: the runner fills it in).</summary>
        public string Arg(string k)
        {
            string w = Wired(k);
            if (w != null) return double.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? w : "{" + w + "}";
            var t = Value(k);
            if (t is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float))
                return ((double)v).ToString("0.###", CultureInfo.InvariantCulture);
            string s = t is JValue sv && sv.Type == JTokenType.String ? ((string)sv).Trim() : "";
            if (s.Length == 0) return "";
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d.ToString("0.###", CultureInfo.InvariantCulture);
            return s.StartsWith("{") && s.EndsWith("}") ? s.Replace("#", "") : "{" + s.Replace("#", "").Replace("{", "").Replace("}", "") + "}";
        }

        public bool B(string k) => Value(k) is JValue v && v.Type == JTokenType.Boolean && (bool)v;
        public string Who => S("who") is var w && w.Length > 0 ? w : "@a";
        /// <summary>A reference: a number or a key.</summary>
        public string Ref(string k) => Wired(k) is string w ? (Plain(w) ? w : "{" + w + "}") : Value(k) is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.String) ? clean(v.ToString(CultureInfo.InvariantCulture)) : "";

        /// <summary>"x y z" (three numbers) or "".</summary>
        public string Xyz(string k)
        {
            string s = S(k);
            var w = s.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (w.Length == 0) return "";
            if (w.Length != 3 || w.Any(x => !double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
            {
                Problem($"\"{s}\" isn't x y z.");
                return "";
            }
            return string.Join(" ", w);
        }

        /// <summary>A camera / ship order place; a ship's name of several words quoted.</summary>
        public string Place(string k)
        {
            string t = S(k).Trim();
            if (!t.StartsWith("ship ", StringComparison.OrdinalIgnoreCase) || t.Contains("\"")) return t;
            var w = t.Substring(5).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            var offset = new List<string>();
            if (w.Count >= 4 && w.Skip(w.Count - 3).All(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
            {
                offset = w.Skip(w.Count - 3).ToList();
                w = w.Take(w.Count - 3).ToList();
            }
            string name = string.Join(" ", w);
            return Join("ship", name.Contains(" ") ? "\"" + name + "\"" : name, string.Join(" ", offset));
        }

        public static string Join(params string[] parts) => string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
    }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]   // read-only tables
    public static class QuestSteps
    {
        /// <summary>The picker's groups, in order.</summary>
        public static readonly (string key, string english)[] Groups =
        {
            ("story", "Story"), ("flow", "Flow"), ("places", "Places and travel"), ("ships", "Ships"), ("player", "The player"),
            ("cutscene", "Cutscenes"), ("screen", "Sound and screen"), ("score", "Score"), ("other", "Other"),
        };

        static readonly string[] Races = { "", "pirate", "terran", "vossk", "nivelian", "midorian", "void" };
        static readonly string[] RaceLabels = { "Its maker", "Pirates", "Terran", "Vossk", "Nivelian", "Midorian", "Void" };
        static readonly string[] StandingSides = { "terran", "vossk", "nivelian", "midorian" };
        static readonly string[] StandingSideLabels = { "Terran", "Vossk", "Nivelian", "Midorian" };
        static readonly string[] Then = { "hold", "resume", "vanish", "jump" };
        static readonly string[] ThenLabels = { "Hold there", "Back to its route", "Vanish", "Jump out" };

        static QuestField F(string key, QuestFieldKind kind, string english, JToken def = null, string help = null, bool optional = false,
                            float min = float.MinValue, float max = float.MaxValue) =>
            new QuestField { key = key, kind = kind, english = english, def = def, help = help, optional = optional, min = min, max = max };

        static QuestField C(string key, string english, string[] values, string[] labels, string def = null) =>
            new QuestField { key = key, kind = QuestFieldKind.Choice, english = english, values = values, labels = labels, def = def ?? values[0] };

        static QuestField Who() => F("who", QuestFieldKind.Who, "For", "@a");

        static QuestField When(QuestField f, string key, params string[] values) { f.showKey = key; f.showValues = values; return f; }

        static QuestStepType Plain(string type, string group, string english, string what, Func<QuestReader, string> line, params QuestField[] fields) =>
            new QuestStepType { type = type, group = group, english = english, what = what, line = line, fields = fields };

        static QuestStepType Custom(string type, string group, string english, string what, params QuestField[] fields) =>
            new QuestStepType { type = type, group = group, english = english, what = what, custom = true, fields = fields };

        static QuestStepType Block(string type, string english, string what, QuestBlock[] blocks, params QuestField[] fields) =>
            new QuestStepType { type = type, group = "flow", english = english, what = what, blocks = blocks, fields = fields };

        static string ShipOrder(QuestReader r, string verb, params string[] rest)
        {
            string ship = r.S("ship");
            if (ship.Length == 0) { r.Problem("which ship (the name its Spawn or Fight step gave it)?"); return null; }
            return QuestReader.Join(new[] { "npc", r.Who, ship, verb }.Concat(rest).ToArray());
        }

        /// <summary>/spawn's "hull 2" / "guns 1.5" from a field (empty or 1: nothing).</summary>
        static string Factor(QuestReader r, string key) => r.Has(key) && r.Arg(key) is var a && a.Length > 0 && a != "1" ? key + " " + a : "";

        static string Opt(QuestReader r, string word, string key) => r.Has(key) && r.Arg(key).Length > 0 ? word + " " + r.Arg(key) : "";

        public static readonly List<QuestStepType> Types = new List<QuestStepType>
        {
            // ---- story ----
            Custom("dialog", "story", "Conversation", "Pages with portraits, like the story's", Who()),
            Custom("radio", "story", "Radio call", "A call in the flight HUD", Who()),
            Custom("objective", "story", "Objective", "The Missions window's line and the target on the map"),
            Plain("title", "story", "Title", "Big text on the screen", r =>
            {
                string t = r.S("title"), sub = r.S("subtitle");
                if (t.Length == 0 && sub.Length == 0) { r.Problem("write a title."); return null; }
                return QuestReader.Join("title", r.Who, t + (sub.Length > 0 ? " | " + sub : ""), Opt(r, "for", "seconds"));
            }, Who(), F("title", QuestFieldKind.Text, "Title"), F("subtitle", QuestFieldKind.Text, "Line under it", optional: true),
               F("seconds", QuestFieldKind.Float, "Shown for (seconds; empty: 4)", optional: true, min: 0.5f, max: 120f)),
            Plain("note", "story", "Note", "A comment for yourself: does nothing", r => "# " + r.step["text"]?.ToString().Replace("\r", " ").Replace("\n", " "),
               F("text", QuestFieldKind.LongText, "Note")),

            // ---- flow ----
            Plain("wait", "flow", "Wait", "Seconds", r => "wait " + (r.N("seconds") is var s && s.Length > 0 ? s : "1"),
               F("seconds", QuestFieldKind.Float, "Seconds", 3, min: 0f, max: 36000f)),
            Plain("waituntil", "flow", "Wait until", "A condition", r =>
            {
                string c = r.E("condition");
                if (c.Length == 0) { r.Problem("write the condition."); return null; }
                return "wait until " + c + (r.Has("timeout") ? " timeout " + r.N("timeout") : "");
            }, F("condition", QuestFieldKind.Condition, "Condition"), F("timeout", QuestFieldKind.Float, "Give up after (seconds)", optional: true, min: 0f)),
            Block("if", "If", "Steps only when a condition holds, others when it doesn't",
                new[] { new QuestBlock("then", "Then"), new QuestBlock("else", "Else") }, F("condition", QuestFieldKind.Condition, "Condition")),
            Block("while", "While", "Steps again and again as long as a condition holds",
                new[] { new QuestBlock("steps", "Do") }, F("condition", QuestFieldKind.Condition, "Condition")),
            Block("repeat", "Repeat", "Steps a number of times",
                new[] { new QuestBlock("steps", "Do") }, F("count", QuestFieldKind.Expression, "How many times", 3),
                F("as", QuestFieldKind.Variable, "Count in a variable (optional)", optional: true)),
            Block("parallel", "At the same time", "Steps that run beside the rest of the mission",
                new[] { new QuestBlock("steps", "Do") }),
            Block("every", "Every few seconds", "Steps again and again beside the rest, until the mission ends",
                new[] { new QuestBlock("steps", "Do") }, F("seconds", QuestFieldKind.Float, "Every (seconds)", 10, min: 0.25f)),
            Block("on", "When something happens", "Steps each time the player is destroyed, docks, kills a ship, gets near a point...",
                new[] { new QuestBlock("steps", "Do") },
                C("trigger", "When", new[] { "died", "docked", "launched", "kill", "cleared", "entered", "near", "respawned", "hacked", "pad", "joined", "left", "pvpkill" },
                    new[] { "The player is destroyed", "The player docks", "The player launches", "A spawned ship is destroyed", "Every spawned enemy is gone",
                            "The player enters an orbit", "The player gets near a point", "The player respawns", "The player hacks a wreck",
                            "The player docks at a station's pad outside", "A player joins", "A player leaves", "A player destroys another" }),
                When(F("station", QuestFieldKind.Station, "Only at (optional)", optional: true), "trigger", "docked", "launched", "entered", "died", "kill", "respawned", "hacked", "pad"),
                When(F("station", QuestFieldKind.Station, "In the orbit of"), "trigger", "near"),
                When(F("at", QuestFieldKind.Xyz, "The point (x y z)", "0 0 20000"), "trigger", "near"),
                When(F("radius", QuestFieldKind.Float, "Within (metres)", 500, min: 1f), "trigger", "near")),
            new QuestStepType { type = "ask", group = "flow", english = "Ask", what = "A question; each answer runs its own steps", answers = true,
                fields = new[] { Who(), F("speaker", QuestFieldKind.Speaker, "Asked by", optional: true), F("question", QuestFieldKind.LongText, "Question"),
                                 F("seconds", QuestFieldKind.Float, "Time to answer (seconds)", 30, min: 3f) } },
            new QuestStepType { type = "vote", group = "flow", english = "Vote", what = "Everyone picks an answer; the most picked one runs its steps", answers = true,
                blocks = new[] { new QuestBlock("none", "Nobody voted") },
                fields = new[] { Who(), F("speaker", QuestFieldKind.Speaker, "Asked by", optional: true), F("question", QuestFieldKind.LongText, "Question"),
                                 F("seconds", QuestFieldKind.Float, "Time to vote (seconds)", 30, min: 3f) } },
            new QuestStepType { type = "random", group = "flow", english = "Random", what = "One of several branches, by chance", branches = true },
            Plain("set", "flow", "Set a variable", "A number the mission remembers (its variables)", r =>
            {
                string v = r.S("variable").ToLowerInvariant(), e = r.E("value");
                if (!QuestFile.IsName(v)) { r.Problem("name the variable (letters, digits, _)."); return null; }
                if (e.Length == 0) { r.Problem("write its value."); return null; }
                return $"set {v} = {e}";
            }, F("variable", QuestFieldKind.Variable, "Variable"), F("value", QuestFieldKind.Expression, "Value (an expression)", "0")),
            new QuestStepType { type = "checkpoint", group = "flow", english = "Checkpoint", what = "A save goes on from here", topOnly = true, custom = true },
            Custom("startquest", "flow", "Start a mission", "Another mission beside this one", F("quest", QuestFieldKind.Quest, "Mission")),
            Plain("startevent", "flow", "Next event", "Ends this one and starts another event graph or mission", r =>
            {
                string e = r.S("event");
                if (e.Length == 0 || e.Contains(" ")) { r.Problem("pick the event."); return null; }
                return QuestReader.Join("startevent", e, r.S("settings"));
            }, F("event", QuestFieldKind.Event, "Event"), F("settings", QuestFieldKind.Text, "Its settings (name=value ...)", optional: true)),
            Custom("complete", "flow", "Complete", "The mission is done, the reward paid"),
            Custom("fail", "flow", "Fail", "Mission failed"),
            Plain("stop", "flow", "Stop", "Ends the mission without a result (an event: its end)", r => "stop"),

            // ---- places ----
            Custom("goto", "places", "Go to a station", "Waits until the player docks there (or reaches its orbit)"),
            Plain("system", "places", "Show / hide a system", "A system on or off the star map (its routes and jumps too), until reset", r =>
            {
                string system = r.Ref("system");
                if (system.Length == 0) { r.Problem("pick the system."); return null; }
                return QuestReader.Join("system", r.Who, system, r.S("action"));
            }, Who(), F("system", QuestFieldKind.System, "System"),
               new QuestField { key = "action", kind = QuestFieldKind.Choice, english = "", values = new[] { "show", "hide", "reset" },
                                labels = new[] { "Show it", "Hide it (a player in it is taken out)", "As the map has it again" }, def = "show" }),
            Plain("waypoint", "places", "Waypoint", "A point in an orbit the autopilot flies to", r =>
            {
                string st = r.Ref("station"), at = r.Xyz("at");
                return st.Length == 0 || at.Length == 0 ? QuestReader.Join("waypoint", r.Who, "off") : QuestReader.Join("waypoint", r.Who, st, at);
            }, Who(), F("station", QuestFieldKind.Station, "Station"), F("at", QuestFieldKind.Xyz, "At (x y z, game units from the station; empty: cleared)", optional: true)),
            Plain("wormhole", "places", "Wormhole", "A wormhole in an orbit; flying in takes the player to its destination", r =>
            {
                string st = r.Ref("station");
                if (st.Length == 0) return QuestReader.Join("wormhole", r.Who, "close");
                string to = r.B("void") ? "void" : r.Ref("to");
                if (to.Length == 0) { r.Problem("pick the destination (a station, or the Void)."); return null; }
                return QuestReader.Join("wormhole", r.Who, st, r.Xyz("at"), "to", to, r.Has("seconds") ? "for " + r.Arg("seconds") : "");
            }, Who(), F("station", QuestFieldKind.Station, "In the orbit of (empty: close it)", optional: true),
               F("at", QuestFieldKind.Xyz, "At (x y z; empty: 4 km out, beside the launch path)", optional: true),
               F("to", QuestFieldKind.Station, "Destination", optional: true), F("void", QuestFieldKind.Bool, "Into the Void instead", false),
               F("seconds", QuestFieldKind.Float, "Open for (seconds; empty: until closed)", optional: true, min: 1f)),
            Plain("target", "places", "Map target", "The gold story icon on the map and the HUD", r => "target " + (r.Ref("station") is var s && s.Length > 0 ? s : "off"),
               F("station", QuestFieldKind.Station, "Station (empty: none)", optional: true)),
            Plain("questorbit", "places", "Quiet orbit", "No normal traffic in an orbit (for the mission's own ships)", r =>
            {
                string s = r.Ref("station");
                if (s.Length == 0) { r.Problem("pick the station."); return null; }
                return "questorbit " + s + (r.step["on"] is JValue v && v.Type == JTokenType.Boolean && !(bool)v ? " off" : "");
            }, F("station", QuestFieldKind.Station, "Station"), F("on", QuestFieldKind.Bool, "Quiet (off: the traffic comes back)", true)),
            Plain("restrict", "places", "Restrict travel", "No jumps, no docking, both, or only no Khador Drive", r => QuestReader.Join("restrict", r.Who, r.S("what")),
               Who(), C("what", "Refuse", new[] { "jumps", "docking", "all", "khador", "off" },
                        new[] { "Jumps", "Docking", "Both", "The Khador Drive only", "Nothing (lift it)" })),
            Plain("respawn", "places", "Respawn point", "Where a destroyed ship comes back, in space", r =>
            {
                string st = r.Ref("station");
                if (st.Length == 0) return QuestReader.Join("respawn", r.Who, "off");
                return QuestReader.Join("respawn", r.Who, st, r.Xyz("at"), Opt(r, "spread", "spread"), Opt(r, "delay", "delay"));
            }, Who(), F("station", QuestFieldKind.Station, "In the orbit of (empty: none)", optional: true), F("at", QuestFieldKind.Xyz, "At (x y z; empty: in front of the station)", optional: true),
               F("spread", QuestFieldKind.Float, "Spread (units)", optional: true, min: 0f), F("delay", QuestFieldKind.Float, "After (seconds)", optional: true, min: 0f)),
            Plain("teleport", "places", "Teleport", "Moves the player to an orbit or into a hangar", r =>
            {
                string st = r.Ref("station");
                if (st.Length == 0) { r.Problem("pick the station."); return null; }
                return QuestReader.Join("tp", r.Who, st, r.B("dock") ? "dock" : r.Xyz("at"));
            }, Who(), F("station", QuestFieldKind.Station, "Station"), F("dock", QuestFieldKind.Bool, "Into its hangar", false),
               F("at", QuestFieldKind.Xyz, "At (x y z; empty: the launch spot)", optional: true)),

            // ---- ships ----
            Custom("fight", "ships", "Fight", "Ships in an orbit; waits until they are destroyed"),
            Plain("spawn", "ships", "Spawn ships", "Ships (or an object) beside the player, named for the ship orders", r =>
            {
                string ship = r.Ref("ship");
                if (ship.Length == 0) { r.Problem("pick the ship."); return null; }
                string name = r.S("name"), at = r.Xyz("at"), turrets = r.S("turrets").Replace(" ", "");
                return QuestReader.Join("spawn", r.Who, ship, r.S("race"), r.Has("count") ? r.Arg("count") : "", r.Has("behaviour") ? r.S("behaviour") : "", turrets.Length > 0 ? "turrets " + turrets : "",
                                        Factor(r, "hull"), Factor(r, "guns"), name.Length > 0 ? "named " + name : "", at.Length > 0 ? "at " + at : "");
            }, Who(), F("ship", QuestFieldKind.Ship, "Ship"), F("count", QuestFieldKind.Int, "How many (empty: 1)", optional: true, min: 1, max: 10),
               new QuestField { key = "race", kind = QuestFieldKind.Race, english = "Flown by", values = Races, labels = RaceLabels, def = "", optional = true },
               C("behaviour", "Toward the player", new[] { "enemy", "neutral", "friendly", "standing" }, new[] { "Enemy", "Neutral", "Friendly", "By the standing" }),
               F("name", QuestFieldKind.Text, "Their name (on the lock plate; the orders use it)", optional: true),
               F("at", QuestFieldKind.Xyz, "At (x y z in the orbit; empty: ahead of the player)", optional: true),
               F("turrets", QuestFieldKind.Text, "Turret items (numbers, a comma between)", optional: true),
               F("hull", QuestFieldKind.Float, "Hull x (empty: as usual; 2 = twice as tough)", optional: true, min: 0.1f, max: 50f),
               F("guns", QuestFieldKind.Float, "Gun damage x (empty: as usual)", optional: true, min: 0.1f, max: 50f)),
            // Remake (a modder's suggestion): an NPC ship type of the NPC ships kind, the pirate boss among the game's.
            Plain("spawntype", "ships", "Spawn an NPC ship type", "A capital ship, freighter or boss of the NPC ships kind (the pirate boss too), with its own hull, guns and escorts", r =>
            {
                string type = r.Ref("npc");
                if (type.Length == 0) { r.Problem("pick the NPC ship type."); return null; }
                string name = r.S("name"), at = r.Xyz("at");
                return QuestReader.Join("spawn", r.Who, "type", type, name.Length > 0 ? "named " + name : "", at.Length > 0 ? "at " + at : "");
            }, Who(), F("npc", QuestFieldKind.NpcShip, "NPC ship type", "pirate_boss"),
               F("name", QuestFieldKind.Text, "Its name (on the lock plate; the orders use it)", optional: true),
               F("at", QuestFieldKind.Xyz, "At (x y z in the orbit; empty: 3.5 km ahead of the player)", optional: true)),
            Plain("wreck", "ships", "Hackable wreck", "A damaged freighter the player can dock at and hack (\"When something happens\": the player hacks a wreck)", r =>
            {
                string name = r.S("name"), at = r.Xyz("at");
                return QuestReader.Join("spawn", r.Who, "wreck", r.S("race"), name.Length > 0 ? "named " + name : "", at.Length > 0 ? "at " + at : "");
            }, Who(),
               C("race", "Freighter", new[] { "", "terran", "vossk", "nivelian", "midorian" }, new[] { "Any", "Terran", "Vossk", "Nivelian", "Midorian" }),
               F("name", QuestFieldKind.Text, "Its name (on the HUD; %victim% when hacked)", optional: true),
               F("at", QuestFieldKind.Xyz, "At (x y z in the orbit; empty: 1.5 km ahead of the player)", optional: true)),
            Plain("shipgoto", "ships", "Ship: fly to", "A named ship flies somewhere", r =>
            {
                string to = r.Place("to");
                if (to.Length == 0) { r.Problem("where should it fly?"); return null; }
                return ShipOrder(r, "goto", to, Opt(r, "speed", "speed"), Opt(r, "radius", "radius"), r.B("fight") ? "fight" : "", "then " + r.S("then"));
            }, Who(), F("ship", QuestFieldKind.ShipName, "Ship"), F("to", QuestFieldKind.Place, "To", "station 0 0 20000"),
               F("speed", QuestFieldKind.Float, "Speed (units per ms; a fighter 2)", optional: true, min: 0.05f, max: 50f), F("radius", QuestFieldKind.Float, "Arrived within (units)", optional: true, min: 0f),
               F("fight", QuestFieldKind.Bool, "Fights on the way", false), C("then", "Then", Then, ThenLabels)),
            Plain("shiproute", "ships", "Ship: fly a route", "A named ship flies from place to place", r =>
            {
                var places = (r.S("route") ?? "").Split(new[] { '/', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (places.Count == 0) { r.Problem("the places, a / between them."); return null; }
                var quoted = places.Select(p => { var tmp = new QuestReader(new JObject { ["p"] = p }, null, r.Problem, r.clean); return tmp.Place("p"); });
                return ShipOrder(r, "route", string.Join(" / ", quoted), r.B("loop") ? "loop" : "", Opt(r, "speed", "speed"), Opt(r, "radius", "radius"), r.B("fight") ? "fight" : "", "then " + r.S("then"));
            }, Who(), F("ship", QuestFieldKind.ShipName, "Ship"), F("route", QuestFieldKind.Text, "Places (a / between them)", "station 0 0 20000 / station 20000 0 20000"),
               F("loop", QuestFieldKind.Bool, "Round and round", false), F("speed", QuestFieldKind.Float, "Speed (units per ms)", optional: true, min: 0.05f, max: 50f),
               F("radius", QuestFieldKind.Float, "Arrived within (units)", optional: true, min: 0f), F("fight", QuestFieldKind.Bool, "Fights on the way", false), C("then", "Then", Then, ThenLabels)),
            Plain("shipfollow", "ships", "Ship: follow", "A named ship flies beside the player or another ship", r =>
            {
                string offset = r.Xyz("offset");
                return ShipOrder(r, r.B("escort") ? "escort" : "follow", r.Place("leader") is var l && l.Length > 0 ? l : "player", offset.Length > 0 ? "offset " + offset : "", Opt(r, "speed", "speed"));
            }, Who(), F("ship", QuestFieldKind.ShipName, "Ship"), F("leader", QuestFieldKind.Place, "Follows", "player"),
               F("offset", QuestFieldKind.Xyz, "Where beside it (right up forward)", optional: true), F("escort", QuestFieldKind.Bool, "Fights what attacks it (escort)", true),
               F("speed", QuestFieldKind.Float, "Speed (units per ms)", optional: true, min: 0.05f, max: 50f)),
            Plain("shipattack", "ships", "Ship: attack", "A named ship attacks the player or another ship, whatever the standing", r =>
                ShipOrder(r, "attack", r.Place("target") is var t && t.Length > 0 ? t : "player"),
               Who(), F("ship", QuestFieldKind.ShipName, "Ship"), F("target", QuestFieldKind.Place, "Attacks", "player")),
            Plain("shipaction", "ships", "Ship: other order", "Hold, back to its route, jump out, dock, flee", r =>
                ShipOrder(r, r.S("action"), r.S("action") == "flee" ? "seconds " + (r.Arg("seconds") is var s && s.Length > 0 ? s : "20") : ""),
               Who(), F("ship", QuestFieldKind.ShipName, "Ship"),
               C("action", "Order", new[] { "hold", "resume", "jump", "dock", "flee" }, new[] { "Hold where it is", "Back to its route", "Jump out", "Dock at the station", "Flee, then jump out" }),
               When(F("seconds", QuestFieldKind.Float, "Flees for (seconds)", 20, min: 1f), "action", "flee")),
            Plain("provoke", "ships", "Make hostile", "The ships around the player turn on them", r =>
                QuestReader.Join("provoke", r.Who, r.S("race"), Opt(r, "within", "within")),
               Who(), new QuestField { key = "race", kind = QuestFieldKind.Race, english = "Only", values = Races, labels = new[] { "Every race" }.Concat(RaceLabels.Skip(1)).ToArray(), def = "", optional = true },
               F("within", QuestFieldKind.Float, "Within (metres; empty: the whole orbit)", optional: true, min: 1f)),

            // ---- the player ----
            Custom("reward", "player", "Reward", "Credits and items, in the reward box", Who()),
            Plain("give", "player", "Give an item", "Into the hold, without a box", r =>
            {
                string item = r.Ref("item");
                if (item.Length == 0) { r.Problem("pick the item."); return null; }
                return QuestReader.Join("give", r.Who, item, r.Arg("amount"), r.B("mount") ? "mount" : "");
            }, Who(), F("item", QuestFieldKind.Item, "Item", 22), F("amount", QuestFieldKind.Int, "Amount", 1, min: 1, max: 1000),
               F("mount", QuestFieldKind.Bool, "Mount it (when docked)", false)),
            // Remake (a modder's suggestion): /take, a hand-in; "mounted" takes the mounted ones too (a Khador Drive).
            Plain("take", "player", "Take an item", "Out of the hold (a hand-in), or off the ship too", r =>
            {
                string item = r.Ref("item");
                if (item.Length == 0) { r.Problem("pick the item."); return null; }
                return QuestReader.Join("take", r.Who, item, r.Has("amount") ? r.Arg("amount") : "all", r.B("mounted") ? "mounted" : "");
            }, Who(), F("item", QuestFieldKind.Item, "Item", 22), F("amount", QuestFieldKind.Int, "Amount (empty: all of them)", optional: true, min: 1, max: 100000),
               F("mounted", QuestFieldKind.Bool, "Mounted ones too (a Khador Drive, a gun...)", false)),
            Custom("cargo", "player", "Bring cargo", "Waits until the hold has it; handed in, it is taken"),
            Plain("credits", "player", "Credits", "Adds (or takes, below 0) credits without a box", r => QuestReader.Join("credits", r.Who, r.Arg("amount")),
               Who(), F("amount", QuestFieldKind.Int, "Credits", 1000, min: -999999999, max: 999999999)),
            Plain("ship", "player", "Change ship", "The player flies another hull (or their own again)", r =>
            {
                string s = r.B("own") ? "own" : r.Ref("hull");
                if (s.Length == 0) { r.Problem("pick the ship."); return null; }
                return QuestReader.Join("ship", r.Who, s);
            }, Who(), F("own", QuestFieldKind.Bool, "Their own ship again", false), When(F("hull", QuestFieldKind.Ship, "Ship"), "own", "False", "false", "")),
            Plain("heal", "player", "Repair", "Hull, shield and armor full", r => QuestReader.Join("heal", r.Who), Who()),
            Plain("ammo", "player", "Refill ammo", "Every secondary to 50", r => QuestReader.Join("ammo", r.Who), Who()),
            Plain("kill", "player", "Destroy the ship", "The player's ship is destroyed", r => QuestReader.Join("kill", r.Who), Who()),
            Plain("reveal", "player", "Reveal the map", "Every system on the star map", r => QuestReader.Join("reveal", r.Who), Who()),
            Plain("peace", "player", "Make peace", "Every race neutral again", r => QuestReader.Join("peace", r.Who), Who()),
            // Remake: /standing (NetAdminStanding): toward a game race or a mod's race (the builder lists the mod races too)
            Plain("standing", "player", "Standing", "Moves the standing toward a race (a mod's too), or sets it", r =>
            {
                string side = r.S("race");
                if (side.Length == 0) { r.Problem("pick the race."); return null; }
                return QuestReader.Join("standing", r.Who, side, r.Arg("amount"), r.B("set") ? "set" : "");
            }, Who(), new QuestField { key = "race", kind = QuestFieldKind.Race, english = "Toward", values = StandingSides, labels = StandingSideLabels, def = "terran" },
               F("amount", QuestFieldKind.Int, "Amount (below 0: liked less)", 10, min: -200, max: 200),
               F("set", QuestFieldKind.Bool, "Set it to the amount", false)),

            // ---- cutscenes ----
            Plain("cutscene", "cutscene", "Start a cutscene", "HUD and controls off, bars", r =>
                QuestReader.Join("cutscene", r.Who, "start", r.B("letterbox") ? "" : "nobars", r.B("freeze") ? "freeze" : "", r.B("invulnerable") ? "invulnerable" : ""),
               Who(), F("letterbox", QuestFieldKind.Bool, "Black bars", true), F("freeze", QuestFieldKind.Bool, "The ship stops", false), F("invulnerable", QuestFieldKind.Bool, "Invulnerable", true)),
            Plain("camera", "cutscene", "Camera shot", "The camera at a place, moving, looking at something", r =>
            {
                string from = r.Place("from");
                if (from.Length == 0) { r.Problem("where is the camera?"); return null; }
                string to = r.Place("to"), look = r.Place("look");
                string shot = QuestReader.Join(from, to.Length > 0 ? "to " + to : "", "over " + (r.Arg("seconds") is var s && s.Length > 0 ? s : "3"), look.Length > 0 ? "look " + look : "",
                                               r.B("follow") ? "follow" : "", Opt(r, "fov", "fov"), Opt(r, "shake", "shake"));
                if (!shot.Contains("{") && !EventCutscene.TryParseShot(shot, out _, out string why)) { r.Problem("the shot: " + why + "."); return null; }
                return QuestReader.Join("camera", r.Who, shot);
            }, Who(), F("from", QuestFieldKind.Place, "From", "player 0 600 -2500"), F("to", QuestFieldKind.Place, "Moves to (optional)", optional: true),
               F("seconds", QuestFieldKind.Float, "Over (seconds)", 3, min: 0f), F("look", QuestFieldKind.Place, "Looks at (optional)", "player", optional: true),
               F("follow", QuestFieldKind.Bool, "Keeps up with what it looks at", false), F("fov", QuestFieldKind.Float, "Field of view (degrees)", optional: true, min: 5f, max: 150f),
               F("shake", QuestFieldKind.Float, "Shake (0..1)", optional: true, min: 0f, max: 1f)),
            Plain("chasecamera", "cutscene", "Chase camera", "The normal camera behind the ship again", r => QuestReader.Join("camera", r.Who, "chase"), Who()),
            Plain("cutsceneend", "cutscene", "End the cutscene", "HUD, controls and the chase camera back", r => QuestReader.Join("cutscene", r.Who, "end"), Who()),
            Plain("fade", "cutscene", "Fade", "To or from a colour", r =>
            {
                string how = r.S("direction");
                if (how == "clear") return QuestReader.Join("fade", r.Who, "clear");
                string colour = r.S("colour").TrimStart('#').Trim().Replace("No.", "");
                return QuestReader.Join("fade", r.Who, how, r.Arg("seconds") is var s && s.Length > 0 ? s : "1", colour);
            }, Who(), C("direction", "Fade", new[] { "out", "in", "clear" }, new[] { "Out (to the colour)", "In (from it)", "Clear at once" }),
               When(F("seconds", QuestFieldKind.Float, "Seconds", 1, min: 0f, max: 60f), "direction", "out", "in"),
               When(F("colour", QuestFieldKind.Text, "Colour (rrggbb)", "000000", optional: true), "direction", "out", "in")),
            Plain("letterbox", "cutscene", "Black bars", "On or off, without a cutscene", r => QuestReader.Join("letterbox", r.Who, r.B("on") ? "on" : "off"),
               Who(), F("on", QuestFieldKind.Bool, "On", true)),

            // ---- sound and screen ----
            Plain("music", "screen", "Music", "A track, or stop", r => QuestReader.Join("music", r.Who, r.S("track") is var t && t.Length > 0 ? t : "stop"),
               Who(), F("track", QuestFieldKind.Music, "Track", "battle")),
            Plain("sound", "screen", "Sound", "A sound effect", r => QuestReader.Join("sound", r.Who, r.S("sound")),
               Who(), F("sound", QuestFieldKind.Sound, "Sound", "alarm")),
            Plain("timer", "screen", "Timer", "A countdown at the top of the screen", r =>
                r.B("stop") ? QuestReader.Join("timer", r.Who, "stop") : QuestReader.Join("timer", r.Who, r.Arg("seconds") is var s && s.Length > 0 ? s : "60", r.S("label")),
               Who(), F("stop", QuestFieldKind.Bool, "Take it away", false), When(F("seconds", QuestFieldKind.Float, "Seconds", 60, min: 1f), "stop", "False", "false", ""),
               When(F("label", QuestFieldKind.Text, "Label", optional: true), "stop", "False", "false", "")),

            // ---- score ----
            Plain("points", "score", "Points", "Points for the score", r => QuestReader.Join("points", r.Who, r.N("amount")),
               Who(), F("amount", QuestFieldKind.Expression, "Points", 1)),
            Plain("scoreboard", "score", "Scoreboard", "The top ten on every player's screen", r => r.B("show") ? QuestReader.Join("scoreboard on", r.S("title")) : "scoreboard off",
               F("show", QuestFieldKind.Bool, "Shown", true), When(F("title", QuestFieldKind.Text, "Title", optional: true), "show", "True", "true")),
            Plain("score", "score", "Score by", "Kills, time alive or points", r => "score " + r.S("mode"),
               C("mode", "Score", new[] { "kills", "time", "points" }, new[] { "Kills", "Time alive", "Points" })),
            Plain("winner", "score", "Winner", "Shows who won", r => ("winner " + r.S("mode")).TrimEnd(),
               C("mode", "By", new[] { "", "kills", "time", "points" }, new[] { "The score", "Kills", "Time alive", "Points" })),
            Plain("pvp", "score", "Free for all", "Every player an enemy of every other (multiplayer)", r => "pvp " + (r.B("on") ? "on" : "off"),
               F("on", QuestFieldKind.Bool, "On", true)),

            // ---- other ----
            Plain("command", "other", "Command", "Any event command (as typed in multiplayer)", r =>
            {
                string l = (r.step["line"]?.ToString() ?? "").Replace("\r", " ").Replace("\n", " ").Trim().TrimStart('/');
                return l.Length > 0 ? l : null;
            }, F("line", QuestFieldKind.Text, "Command")),
        };

        static QuestSteps()
        {
            foreach (var t in Types)
                foreach (var f in t.fields)
                {
                    var type = t;
                    f.fieldDefault = (s, k) => type.Field(k)?.def is JValue d && d.Type != JTokenType.Null ? d.ToString(CultureInfo.InvariantCulture) : "";
                }
        }

        public static QuestStepType Get(string type) => Types.FirstOrDefault(t => t.type == type);

        /// <summary>A new step of a type with its defaults filled in (and its empty blocks).</summary>
        public static JObject New(string type)
        {
            var st = new JObject { ["type"] = type };
            var t = Get(type);
            if (t == null) return st;
            foreach (var f in t.fields)
                if (!f.optional && f.def != null && f.key != "who" && st[f.key] == null) st[f.key] = f.def.DeepClone();
            foreach (var b in t.blocks) st[b.key] = new JArray();
            if (t.answers) st["answers"] = new JArray(new JObject { ["text"] = "Yes", ["steps"] = new JArray() }, new JObject { ["text"] = "No", ["steps"] = new JArray() });
            if (t.branches) st["branches"] = new JArray(new JObject { ["weight"] = 1, ["steps"] = new JArray() }, new JObject { ["weight"] = 1, ["steps"] = new JArray() });
            return st;
        }

        /// <summary>Every nested step list of a step (its blocks, answers, branches), with a label.</summary>
        public static IEnumerable<(string label, JArray list)> Children(JObject step)
        {
            var t = Get((string)step["type"] ?? "");
            if (t == null) yield break;
            if (t.answers && step["answers"] is JArray answers)
            {
                int k = 0;
                foreach (var a in answers.OfType<JObject>())
                {
                    k++;
                    if (!(a["steps"] is JArray s)) a["steps"] = s = new JArray();
                    yield return ($"{k}. {a["text"]}", s);
                }
            }
            if (t.branches && step["branches"] is JArray branches)
            {
                int k = 0;
                foreach (var b in branches.OfType<JObject>())
                {
                    k++;
                    if (!(b["steps"] is JArray s)) b["steps"] = s = new JArray();
                    yield return ($"{k}", s);
                }
            }
            foreach (var b in t.blocks)
            {
                if (!(step[b.key] is JArray s)) step[b.key] = s = new JArray();
                yield return (b.english, s);
            }
        }
    }
}
