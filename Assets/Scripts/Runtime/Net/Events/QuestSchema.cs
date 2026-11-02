// QuestSchema.cs
// Remake-only: the .gof2mission format as a JSON Schema (draft 2020-12) and as a Markdown reference, made from the same tables
// the game compiles with (QuestSteps' step types and fields, QuestGraph's value nodes), so what an AI assistant is told can't
// drift from what the game reads. GoF2 > Build > Modding AI Reference writes them into Modding/ai/gof2-modding
// (mission.schema.json, missions.md); the Mod Builder's MCP server hands the schema out (get_mission_schema). The fields of the
// steps the builder edits on its own page (a conversation's pages, a fight's ships...) are listed here by hand. Plain C#.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace GoF2Remake.Events
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]   // read-only tables
    public static class QuestSchema
    {
        /// <summary>The fields of the custom steps (QuestSteps.custom: written by QuestFile itself): key, kind of value, meaning.</summary>
        static readonly Dictionary<string, (string key, string type, string what)[]> CustomFields = new Dictionary<string, (string, string, string)[]>
        {
            ["dialog"] = new[] { ("pages", "pages", "the pages in order: [{ speaker, text, voice }]") },
            ["radio"] = new[] { ("speaker", "speaker", "who calls"), ("text", "text", "the line"), ("voice", "string", "a voice clip (voices.md or the mod's voices/)") },
            ["objective"] = new[] { ("text", "text", "the Missions window's line"), ("station", "ref", "the gold target on the map (optional)") },
            ["goto"] = new[] { ("station", "ref", "the station"), ("dock", "boolean", "true (default): docked there; false: in its orbit"),
                               ("objective", "text", "the objective meanwhile (optional)") },
            ["fight"] = new[] { ("station", "ref", "the orbit (empty: wherever the player is)"),
                                ("ships", "ships", "[{ ship, count (1-10), race, behaviour (enemy / neutral / friendly) }]"),
                                ("name", "string", "their name on the lock plate (ship orders use it)"), ("quiet", "boolean", "no other traffic there (default true)"),
                                ("objective", "text", "the objective meanwhile (optional)") },
            ["cargo"] = new[] { ("item", "ref", "the item"), ("amount", "integer", "how many tonnes"), ("station", "ref", "docked there (optional)"),
                                ("objective", "text", "the objective meanwhile (optional)"), ("take", "boolean", "handed in: taken from the hold once there") },
            ["reward"] = new[] { ("credits", "number", "credits (or an expression)"), ("items", "items", "[{ item, amount }]"), ("title", "text", "the reward box's title") },
            ["checkpoint"] = new[] { ("name", "string", "its name (optional; one word)") },
            ["startquest"] = new[] { ("quest", "string", "the story mission's name (its file name)") },
            ["complete"] = new[] { ("reward", "integer", "credits (default: the mission's reward)"), ("title", "text", "the reward box's title") },
            ["fail"] = new[] { ("title", "text", "what the player is told") },
        };

        static JObject Str(string description = null) => Desc(new JObject { ["type"] = "string" }, description);
        static JObject Desc(JObject o, string d) { if (!string.IsNullOrEmpty(d)) o["description"] = d; return o; }
        static JObject Ref(string d) => Desc(new JObject { ["type"] = new JArray("integer", "string") }, d);
        static JObject Wire => new JObject { ["$ref"] = "#/$defs/wire" };
        static JObject StepsRef => new JObject { ["$ref"] = "#/$defs/steps" };
        static JObject Text(string d) => Desc(new JObject { ["$ref"] = "#/$defs/text" }, d);
        static JObject TextDef => new JObject { ["anyOf"] = new JArray(new JObject { ["type"] = "string" },
            new JObject { ["type"] = "object", ["description"] = "one per language: { \"en\": ..., \"de\": ... }", ["additionalProperties"] = new JObject { ["type"] = "string" } }) };
        const string WhoNote = "@a everyone the mission is for (default), @trigger the player an \"on\" block is about, @alive, @docked, @survivors, @r, or @alive[orbit=<station>]";

        static JObject FieldSchema(QuestField f)
        {
            string d = f.english + (f.help != null ? " (" + f.help + ")" : "");
            JObject s;
            switch (f.kind)
            {
                case QuestFieldKind.Bool: s = Desc(new JObject { ["type"] = "boolean" }, d); break;
                case QuestFieldKind.Int:
                case QuestFieldKind.Float:
                    s = Desc(new JObject { ["type"] = new JArray("number", "string") }, d + "; a number, or an expression as a string"); break;
                case QuestFieldKind.Station: case QuestFieldKind.Ship: case QuestFieldKind.Item: case QuestFieldKind.System:
                    s = Ref(d + "; the game's number, its name, or a key \"mod_id:id\""); break;
                case QuestFieldKind.NpcShip:
                    s = Str(d + ": battleship, carrier, vossk_battleship, freighter, pirate_boss, or a mod's NPC ship key \"mod_id:id\""); break;
                case QuestFieldKind.Choice: case QuestFieldKind.Race:
                    s = Desc(new JObject { ["type"] = "string", ["enum"] = new JArray(f.values) }, d + ": " + string.Join(", ", f.values.Select((v, i) => (v.Length == 0 ? "\"\"" : v) + " = " + f.labels[i]))); break;
                case QuestFieldKind.LongText: case QuestFieldKind.Text: s = Text(d); break;
                case QuestFieldKind.Who: s = Desc(new JObject { ["$ref"] = "#/$defs/who" }, d); break;
                default: s = Str(d + KindNote(f.kind)); break;
            }
            if (f.def != null) s["default"] = f.def.DeepClone();
            // Fields a value node can be wired into.
            if (f.kind == QuestFieldKind.Condition || f.kind == QuestFieldKind.Expression || f.kind == QuestFieldKind.Int || f.kind == QuestFieldKind.Float
                || f.kind == QuestFieldKind.Station)
                return new JObject { ["anyOf"] = new JArray(s, Wire), ["description"] = s["description"] };
            return s;
        }

        static string KindNote(QuestFieldKind k) => k switch
        {
            QuestFieldKind.Condition => "; an expression (guide.md \"Conditions\"), e.g. kills >= 5",
            QuestFieldKind.Expression => "; an expression",
            QuestFieldKind.Place => "; a place: player [right up forward] | station [x y z] | x y z | ship <name> [x y z] (game units)",
            QuestFieldKind.Xyz => "; \"x y z\" in game units (20 = 1 m)",
            QuestFieldKind.ShipName => "; the name a spawn / fight step gave the ships",
            QuestFieldKind.Speaker => "; Keith, a story character, \"mod_id:character\", \"vossk K'ekki\", player",
            QuestFieldKind.Music => "; a game track word (battle, boss, void, terran...), a mod track's name, or stop",
            QuestFieldKind.Sound => "; " + string.Join(", ", System.Enum.GetNames(typeof(EventSound)).Select(n => n.ToLowerInvariant())) + ", or a mod sound's name",
            QuestFieldKind.Variable => "; a variable's name (letters, digits, _)",
            QuestFieldKind.Event => "; an event graph's or quest's name",
            QuestFieldKind.Quest => "; a quest's name",
            _ => "",
        };

        static JObject CustomFieldSchema(string type, string what)
        {
            switch (type)
            {
                case "pages":
                    return Desc(new JObject
                    {
                        ["type"] = "array", ["minItems"] = 1,
                        ["items"] = new JObject
                        {
                            ["type"] = "object", ["required"] = new JArray("text"),
                            ["properties"] = new JObject
                            {
                                ["speaker"] = Str("Keith, a story character, \"mod_id:character\", \"vossk K'ekki\", player"),
                                ["text"] = Text("the page; %player% is the player's name, {variable} a variable"),
                                ["voice"] = Str("a voice clip (voices.md or the mod's voices/)"),
                            },
                        },
                    }, what);
                case "ships":
                    return Desc(new JObject
                    {
                        ["type"] = "array", ["minItems"] = 1,
                        ["items"] = new JObject
                        {
                            ["type"] = "object", ["required"] = new JArray("ship"),
                            ["properties"] = new JObject
                            {
                                ["ship"] = Ref("the ship"), ["count"] = new JObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 10 },
                                ["race"] = new JObject { ["type"] = "string", ["enum"] = new JArray("", "pirate", "terran", "vossk", "nivelian", "midorian", "void") },
                                ["behaviour"] = new JObject { ["type"] = "string", ["enum"] = new JArray("enemy", "neutral", "friendly") },
                            },
                        },
                    }, what);
                case "items":
                    return Desc(new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "object", ["required"] = new JArray("item"), ["properties"] = new JObject { ["item"] = Ref("the item"), ["amount"] = new JObject { ["type"] = "integer", ["minimum"] = 1 } } },
                    }, what);
                case "ref": return Ref(what);
                case "text": return Text(what);
                case "speaker": return Str(what + "; Keith, a story character, \"mod_id:character\", \"vossk K'ekki\", player");
                case "boolean": return Desc(new JObject { ["type"] = "boolean" }, what);
                case "integer": return Desc(new JObject { ["type"] = "integer" }, what);
                case "number": return Desc(new JObject { ["type"] = new JArray("number", "string") }, what);
                default: return Str(what);
            }
        }

        static JObject StepSchema(QuestStepType t)
        {
            var props = new JObject { ["type"] = new JObject { ["const"] = t.type } };
            var required = new JArray("type");
            foreach (var f in t.fields)
                if (props[f.key] == null) props[f.key] = FieldSchema(f);
            if (CustomFields.TryGetValue(t.type, out var custom))
                foreach (var (key, type, what) in custom) props[key] = CustomFieldSchema(type, what);
            foreach (var b in t.blocks) props[b.key] = Desc(StepsRef, "the steps of \"" + b.english + "\"");
            if (t.answers)
                props["answers"] = new JObject
                {
                    ["type"] = "array", ["minItems"] = 2, ["maxItems"] = 4, ["description"] = "each answer runs its own steps",
                    ["items"] = new JObject { ["type"] = "object", ["required"] = new JArray("text"), ["properties"] = new JObject { ["text"] = Text("the answer"), ["steps"] = StepsRef } },
                };
            if (t.branches)
                props["branches"] = new JObject
                {
                    ["type"] = "array", ["minItems"] = 1, ["description"] = "one runs, the bigger its weight the likelier",
                    ["items"] = new JObject { ["type"] = "object", ["properties"] = new JObject { ["weight"] = new JObject { ["type"] = "number", ["minimum"] = 0 }, ["steps"] = StepsRef } },
                };
            props["pos"] = new JObject { ["type"] = "array", ["description"] = "the node's place in the builder's graph view [x, y] (optional)" };
            return new JObject
            {
                ["type"] = "object", ["title"] = t.english,
                ["description"] = t.what + (t.topOnly ? " (only on the mission's own line of steps, not inside a block)" : ""),
                ["properties"] = props, ["required"] = required,
            };
        }

        static JObject ValueSchema(QuestGraph.ValueType v)
        {
            var props = new JObject { ["id"] = Str("its id, which a wire names"), ["type"] = new JObject { ["const"] = v.type } };
            foreach (var (key, kind, label, def) in v.inputs)
                props[key] = new JObject { ["anyOf"] = new JArray(new JObject { ["type"] = new JArray("number", "string") }, Wire), ["description"] = label + " (" + kind.ToString().ToLowerInvariant() + ")" };
            foreach (var (key, label, choices, def) in v.options)
                props[key] = choices.Length > 0 ? new JObject { ["type"] = "string", ["enum"] = new JArray(choices), ["description"] = label }
                                                : new JObject { ["type"] = new JArray("string", "number"), ["description"] = label };
            props["pos"] = new JObject { ["type"] = "array" };
            return new JObject
            {
                ["type"] = "object", ["title"] = v.english, ["description"] = "gives " + v.output.ToString().ToLowerInvariant(),
                ["properties"] = props, ["required"] = new JArray("id", "type"),
            };
        }

        /// <summary>The whole .gof2mission format as a JSON Schema.</summary>
        public static JObject Build()
        {
            var steps = new JArray(QuestSteps.Types.Select(StepSchema));
            var values = new JArray(QuestGraph.ValueTypes.Select(ValueSchema));
            return new JObject
            {
                ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
                ["$id"] = "https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/Modding/ai/gof2-modding/mission.schema.json",
                ["title"] = "Galaxy on Fire 2 Remake mission (.gof2mission)",
                ["description"] = "events/<name>.gof2mission in a mod: a story mission, bar mission or event as a tree of steps (Modding/README.md \"Missions made in the Mod Builder\"). The file name is its name. Files from before the rename (.gof2quest, format \"gof2quest\") are read too.",
                ["type"] = "object",
                ["required"] = new JArray("format", "steps"),
                ["properties"] = new JObject
                {
                    ["format"] = new JObject { ["const"] = "gof2mission" },
                    ["title"] = Text("the title (story and bar missions need one)"),
                    ["kind"] = new JObject { ["type"] = "string", ["enum"] = new JArray("quest", "mission", "event"), ["default"] = "quest",
                                             ["description"] = "quest: a story mission, starts by itself when startsWhen holds; mission: a bar mission offered in the Space Lounges; event: started by /event or another's startevent step" },
                    ["startsWhen"] = Str("a story mission's start: \"\" at once, \"never\" only by a startquest step, else a condition (visited(78), quest(other) == 2, campaign >= 20...)"),
                    ["reward"] = new JObject { ["type"] = "integer", ["minimum"] = 0, ["description"] = "credits paid at complete" },
                    ["repeat"] = new JObject { ["type"] = "number", ["minimum"] = 0, ["description"] = "a story mission that comes again: this many minutes of playing time after it was done, as soon as its startsWhen holds then (add a chance to it, random(1, 300) == 1, for a random moment; 0 / left out: once)" },
                    ["offer"] = Text("a bar mission: what the visitor says"),
                    ["client"] = Str("a bar mission's client: a story character, \"mod_id:character\", \"vossk K'ekki\"; empty: someone of the station's race"),
                    ["stations"] = new JObject { ["type"] = "array", ["items"] = Ref("a station"), ["description"] = "a bar mission is offered at (empty: everywhere)" },
                    ["chance"] = new JObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 100, ["description"] = "a bar mission: the percentage of its stations' bars offering it (default 35; each station decides for 30 minutes and drops it for 30 once taken; 100: always)" },
                    ["takers"] = new JObject { ["type"] = "string", ["enum"] = new JArray("universe", "station", "any"), ["description"] = "a bar mission: who can take it: universe (default) one squad at a time anywhere, offered nowhere meanwhile; station: one per station, other stations still offer it; any: any number of squads, at one station too" },
                    ["players"] = new JObject { ["type"] = "array", ["minItems"] = 2, ["maxItems"] = 2, ["items"] = new JObject { ["type"] = "integer" }, ["description"] = "a bar mission's pilots [min, max] (multiplayer)" },
                    ["variables"] = new JObject
                    {
                        ["type"] = "array", ["description"] = "numbers the mission keeps; {name} in a text shows one",
                        ["items"] = new JObject { ["type"] = "object", ["required"] = new JArray("name"), ["properties"] = new JObject
                        {
                            ["name"] = Str("letters, digits, _"), ["value"] = new JObject { ["type"] = new JArray("number", "string"), ["description"] = "its start (an expression)" },
                            ["setting"] = new JObject { ["type"] = "boolean", ["description"] = "/event name x=3 can set it" },
                        } },
                    },
                    ["autoComplete"] = new JObject { ["type"] = "boolean", ["default"] = true, ["description"] = "completes after its last step when no complete / fail ended it" },
                    ["steps"] = Desc(StepsRef, "the steps in order"),
                    ["loose"] = new JObject { ["type"] = "array", ["items"] = StepsRef, ["description"] = "the builder's graph: chains wired to nothing (they don't run)" },
                    ["values"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["$ref"] = "#/$defs/value" }, ["description"] = "value nodes, wired into fields as { \"$value\": id }" },
                    ["startPos"] = new JObject { ["type"] = "array", ["description"] = "the graph's Start node [x, y]" },
                },
                ["$defs"] = new JObject
                {
                    ["steps"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["$ref"] = "#/$defs/step" } },
                    ["step"] = new JObject { ["oneOf"] = steps },
                    ["value"] = new JObject { ["oneOf"] = values },
                    ["text"] = TextDef,
                    ["who"] = new JObject { ["anyOf"] = new JArray(new JObject { ["type"] = "string" }, Wire), ["description"] = "whom the step is for: " + WhoNote },
                    ["wire"] = new JObject
                    {
                        ["type"] = "object", ["required"] = new JArray("$value"), ["additionalProperties"] = false,
                        ["properties"] = new JObject { ["$value"] = Str("a value node's id (in \"values\")") },
                        ["description"] = "a value node wired into the field",
                    },
                },
            };
        }

        /// <summary>The steps and value nodes as a Markdown reference (missions.md).</summary>
        public static string Markdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Galaxy on Fire 2 Remake: mission steps (.gof2mission)");
            sb.AppendLine();
            sb.AppendLine("Generated by GoF2 > Build > Modding AI Reference from the game's own step table (the same the game compiles and the");
            sb.AppendLine("Mod Builder's graph edits). The machine-readable form is `mission.schema.json` beside this file. A mission is");
            sb.AppendLine("`events/<name>.gof2mission`: `{ \"format\": \"gof2mission\", \"title\", \"kind\", ..., \"steps\": [ { \"type\": ..., fields } ] }`.");
            sb.AppendLine("Every step that acts on players also takes `who` (wirable): " + WhoNote + ". Blocks hold their own");
            sb.AppendLine("step lists. A field marked (wirable) can hold `{ \"$value\": \"v1\" }` instead, wired to a value node in `values`.");
            sb.AppendLine();
            foreach (var (gk, gEnglish) in QuestSteps.Groups)
            {
                var types = QuestSteps.Types.Where(t => t.group == gk).ToList();
                if (types.Count == 0) continue;
                sb.AppendLine($"## {gEnglish}");
                sb.AppendLine();
                foreach (var t in types)
                {
                    sb.AppendLine($"### `{t.type}`: {t.english}");
                    sb.AppendLine();
                    sb.AppendLine(t.what + (t.topOnly ? " Only on the mission's own line of steps." : ""));
                    sb.AppendLine();
                    var schema = StepSchema(t);
                    foreach (var p in ((JObject)schema["properties"]).Properties())
                    {
                        if (p.Name == "type" || p.Name == "pos" || p.Name == "who") continue;
                        string d = (string)p.Value["description"] ?? "";
                        bool wirable = p.Value["anyOf"] is JArray any && any.Any(x => (string)x["$ref"] == "#/$defs/wire");
                        string def = p.Value["default"] != null ? $" Default `{p.Value["default"]}`." : "";
                        if (p.Value["anyOf"] is JArray a2 && a2.Count > 0 && a2[0]["default"] != null) def = $" Default `{a2[0]["default"]}`.";
                        sb.AppendLine($"- `{p.Name}`{(wirable ? " (wirable)" : "")}: {d}.{def}".Replace("..", "."));
                    }
                    sb.AppendLine();
                }
            }
            sb.AppendLine("## Value nodes (`values`)");
            sb.AppendLine();
            foreach (var v in QuestGraph.ValueTypes)
            {
                var parts = v.inputs.Select(i => $"`{i.key}` {i.label.ToLowerInvariant()} ({i.kind.ToString().ToLowerInvariant()})")
                             .Concat(v.options.Select(o => $"`{o.key}` {o.label.ToLowerInvariant()}" + (o.choices.Length > 0 ? ": " + string.Join(" / ", o.choices) : "")));
                sb.AppendLine($"- `{v.type}` ({v.english}, gives {v.output.ToString().ToLowerInvariant()}): {string.Join("; ", parts)}");
            }
            sb.AppendLine();
            sb.AppendLine("Example: `\"values\": [ { \"id\": \"v1\", \"type\": \"state\", \"what\": \"kills\" }, { \"id\": \"v2\", \"type\": \"compare\", \"op\": \">=\", \"a\": { \"$value\": \"v1\" }, \"b\": 5 } ]`");
            sb.AppendLine("and an `if` step with `\"condition\": { \"$value\": \"v2\" }` is `if (kills >= 5)`. Writing the expression as text (`\"condition\": \"kills >= 5\"`)");
            sb.AppendLine("works the same; value nodes are what the builder's graph draws.");
            return sb.ToString().Replace("\r\n", "\n");
        }
    }
}
