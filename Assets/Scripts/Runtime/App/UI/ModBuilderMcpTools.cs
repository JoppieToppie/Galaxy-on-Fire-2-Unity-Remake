// ModBuilderMcpTools.cs
// The Mod Builder MCP server's tools (part of ModBuilderMcp): what an AI assistant can do in the open builder.
//   builder_status   the project, the kind shown, a play test's game state (station, credits, missions and their objectives)
//   search_content   the game's, this project's and other mods' content by kind (ModRefs: the values the files use)
//   list_entries / get_entries / put_entry / delete_entry   the project's entries in the mod files' own JSON, through
//                    ModProject.CaptureState / RestoreState (an undo step; a removal asks the player)
//   patch_entry      small edits by path (EntryPatch: set / insert / remove / move / test), the rest of the entry untouched
//   list_files / read_file / write_file   the project's files (images, models, audio, texts) inside its folder only
//   compile_quest / validate / get_quest_schema   the game's own checks: QuestFile, EventRunner, the mod loaders
//   play_test / end_play_test   ModBuilderPlay; get_log, screenshot; undo / redo; show_kind
//   (seeing and driving the builder: ModBuilderMcpVision; the game's sounds and voice lines, .aem models, optimize,
//   import_mod, replace_game_character: ModBuilderMcpContent; the mod's own races: ModBuilderMcpRaces; the modding guide,
//   read_guide and the resources: ModBuilderMcpGuide)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GoF2Remake.Data;
using GoF2Remake.Events;
using GoF2Remake.Modding;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoF2Remake.UI
{
    public static partial class ModBuilderMcp
    {
        // ---- entry kinds ----------------------------------------------------------------------------------------

        /// <summary>The entry kinds: the CaptureState key, the mod file, how an entry is named, the builder kind that shows it.</summary>
        static readonly (string kind, string file, string ident, ModBuilder.Kind show)[] Kinds =
        {
            ("ships", "ships.json", "id or override (a game / other mod's ship number or key)", ModBuilder.Kind.Ships),
            ("items", "items.json", "id (a new item, with base) or override", ModBuilder.Kind.Items),
            ("characters", "characters.json", "id", ModBuilder.Kind.Characters),
            ("systems", "systems.json", "id or override", ModBuilder.Kind.Systems),
            ("stations", "stations.json", "id or override", ModBuilder.Kind.Stations),
            ("npcShips", "npcships.json", "id (a new capital ship, freighter or boss) or override (battleship, carrier, vossk_battleship, freighter, pirate_boss or a mod's \"mod:id\")", ModBuilder.Kind.NpcShips),
            ("races", "races.json", "id (a race of the mod's own; list_races / put_race / set_race / delete_race work on them too)", ModBuilder.Kind.Races),
            ("interiors", "interiors.json", "id", ModBuilder.Kind.Interiors),
            ("blueprints", "blueprints.json", "id", ModBuilder.Kind.Blueprints),
            ("kits", "shipkits.json", "id (a kit of ship parts; a ships.json ship flies it with kit / kitType / build)", ModBuilder.Kind.Kits),
            ("weaponEffects", "weaponfx.json", "id (a look and sound of its own, from its base weapon's; an item takes it with \"fx\": \"<id>\")", ModBuilder.Kind.WeaponFx),
            ("gameOptions", "gameoptions.json", "id", ModBuilder.Kind.Campaigns),
            ("missions", "events/<name>.gof2mission", "name (the file name: letters, digits, _)", ModBuilder.Kind.Quests),
            ("campaign", "campaign.json", "(one per mod)", ModBuilder.Kind.Campaigns),
            ("manifest", "mod.json", "(the mod itself: name, description, version, author, website, credits, dependencies)", ModBuilder.Kind.Ships),
        };

        static readonly string[] KindNames = Kinds.Select(k => k.kind).ToArray();
        /// <summary>search_content's kinds, in RefKind's order (the index is the RefKind).</summary>
        static readonly string[] RefKinds = { "item", "ship", "system", "station", "character", "mission", "music", "interior", "race" };
        static readonly string[] FileFolders = { "models", "textures", "icons", "portraits", "fx", "sky", "cards", "music", "sounds", "voices", "text", "events", "templates" };
        static readonly string[] FileExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".dds", ".aei", ".glb", ".aem", ".ogg", ".wav", ".mp3", ".json", ".gof2event", ".gof2mission", ".gof2quest", ".txt", ".md" };
        static readonly string[] TextExtensions = { ".json", ".gof2event", ".gof2mission", ".gof2quest", ".txt", ".md" };

        static JObject Enum(params string[] values) => new JObject { ["type"] = "string", ["enum"] = new JArray(values) };

        static string Ident(JObject o) => o["override"] != null ? "override:" + o["override"] : (string)o["id"];

        /// <summary>The open builder screen (not during a play test), else why not.</summary>
        static ModBuilderScreen Screen(out string why)
        {
            why = null;
            if (!ModBuilder.Active || ModBuilder.Project == null) { why = "The Mod Builder isn't open."; return null; }
            if (ModBuilder.PlayTesting) { why = "A play test is running: end_play_test first."; return null; }
            var s = ModBuilderScreen.Open;
            if (s == null || !s.Alive) { why = "The builder's screen is still loading: try again in a moment."; return null; }
            return s;
        }

        static ModProject Project => ModBuilder.Project;

        // ---- the table ------------------------------------------------------------------------------------------

        static List<Tool> tools;
        static List<Tool> Tools => tools ??= MakeTools().Concat(VisionTools()).Concat(ContentTools()).Concat(RaceTools()).Concat(GuideTools()).ToList();

        static List<Tool> MakeTools() => new List<Tool>
        {
            new Tool
            {
                name = "builder_status", title = "Builder status", readOnly = true,
                description = "The open project (id, name, its entries per kind), the kind the builder shows, whether a play test runs and, during one, the game's state (scene, station, credits, missions under way with their objectives).",
                input = Input(), run = (a, c) => c.Json(Status(), "status"),
            },
            new Tool
            {
                name = "search_content", title = "Search the game's and the mods' content", readOnly = true,
                description = "Finds content to refer to: the game's, this project's own and every other installed mod's (using another mod's makes it a dependency when the project is validated). 'value' is what the mod files and missions use (a game number, or a key \"mod_id:id\"). Kinds: item, ship, system, station, character (speakers too), mission, music (the game's tracks a music/<name> replaces, and new ones), interior, race (the game's races by word and the mods' own by key), sound (the game's sound effects a sounds/<name> replaces, and the project's new ones a mission's Sound step plays) and voice (the game's voice lines with their speaker and English line: voices/<clip> or voices/<language>/<clip> named like one replaces it, the story's included; and the project's own lines).",
                input = Input(("kind", Enum(RefKinds.Concat(AudioRefKinds).ToArray()), "what to search", true), ("query", "string", "words that must all appear (empty: all)", false),
                              ("limit", "integer", "at most this many (default 40)", false)),
                run = SearchContent,
            },
            new Tool
            {
                name = "list_entries", title = "List the project's entries", readOnly = true,
                description = "The project's entries of a kind with their identity (id / override / mission name) and name.",
                input = Input(("kind", Enum(KindNames), "the kind of entry", true)), run = ListEntries,
            },
            new Tool
            {
                name = "get_entries", title = "Read entries", readOnly = true,
                description = "Entries of a kind as their mod-file JSON (all, or those named in 'ids': an id, \"override:<n>\", or a mission's name). With one id and a 'path' (\"steps/2\", \"steps/3/then\", \"stats\"), only that part: what patch_entry's paths point at.",
                input = Input(("kind", Enum(KindNames), "the kind of entry", true), ("ids", new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" } }, "which (empty: all)", false),
                              ("path", "string", "with one id: only this part of it", false)),
                run = GetEntries,
            },
            new Tool
            {
                name = "put_entry", title = "Write an entry",
                description = "Adds an entry or replaces the one with the same identity, in the mod files' own JSON (Modding guide; missions: mission.schema.json). ships / items / systems / stations: \"id\" for new content, \"override\" for a change to the game's (or another mod's); characters, interiors, races, blueprints, gameOptions: \"id\"; missions: give 'name' and the .gof2mission object; campaign: the campaign.json object; manifest: mod.json's fields (the id stays). It becomes an undo step; the answer lists the problems the game finds.",
                input = Input(("kind", Enum(KindNames), "the kind of entry", true), ("entry", new JObject { ["type"] = "object" }, "the entry's JSON", true),
                              ("name", "string", "missions only: its name (the file name)", false)),
                run = PutEntry, timeoutSeconds = 60f, ask = a =>
                {
                    var e = a["entry"] as JObject;
                    string id = Str(a, "name") ?? (e != null ? (string)e["id"] ?? (e["override"] != null ? "override " + e["override"] : null) : null);
                    return $"write {Str(a, "kind") ?? "an entry"}{(id != null ? $" \"{id}\"" : "")} into the project";
                },
            },
            new Tool
            {
                name = "patch_entry", title = "Edit part of an entry",
                description = "Small edits of one entry in place, the rest left exactly as it is (a mission's graph layout too): prefer it to put_entry for a change to an existing entry. " +
                              "'ops' in order, all or none applied: {\"op\": \"set\", \"path\": \"steps/2/pages/0/text\", \"value\": ...} (replace, or add a key; \"-\" appends to a list), " +
                              "{\"op\": \"insert\", \"path\": \"steps/3\", \"value\": {step}} (into a list before that index, \"-\" at the end), {\"op\": \"remove\", \"path\": ...}, " +
                              "{\"op\": \"move\", \"from\": ..., \"path\": ...} (the target path read after the removal), {\"op\": \"test\", \"path\": ..., \"value\": ...} (fails the patch unless equal: guards against editing the wrong step). " +
                              "Paths are slash-separated keys and list indices into the entry (get_entries with 'path' reads a part). A mission's step inserted without \"pos\" gets a place in its column. One undo step; the answer lists the problems the game finds.",
                input = Input(("kind", Enum(KindNames), "the kind of entry", true), ("id", "string", "an id, \"override:<n>\", a mission's name (campaign / manifest: none)", false),
                              ("ops", new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "object" } }, "the operations", true)),
                run = PatchEntry, timeoutSeconds = 60f,
                ask = a => $"edit {Str(a, "kind") ?? "an entry"}{(Str(a, "id") != null ? $" \"{Str(a, "id")}\"" : "")} ({((a["ops"] as JArray)?.Count ?? 0)} change(s))",
            },
            new Tool
            {
                name = "delete_entry", title = "Remove an entry", destructive = true,
                description = "Removes an entry (asks the player in the builder first; an undo step).",
                input = Input(("kind", Enum(KindNames), "the kind of entry", true), ("id", "string", "an id, \"override:<n>\" or a mission's name", true)),
                run = DeleteEntry, timeoutSeconds = 30f, permitsItself = true,
            },
            new Tool
            {
                name = "list_files", title = "List the project's files", readOnly = true,
                description = "The project's files (models, textures, icons, portraits, audio, texts, events...) with their sizes.",
                input = Input(("folder", "string", "only this folder (e.g. textures)", false)), run = ListFiles,
            },
            new Tool
            {
                name = "read_file", title = "Read a text file", readOnly = true,
                description = "A text file of the project (JSON, .gof2mission, .gof2event, text/<lang>.json...).",
                input = Input(("path", "string", "its path in the project, e.g. text/de.json", true)), run = ReadFile,
            },
            new Tool
            {
                name = "write_file", title = "Write a file",
                description = "Writes a file into the project (folders: models, textures, icons, portraits, fx, sky, cards, music, sounds, voices, text, events; images png / jpg / tga / bmp / dds / aei, models .glb or the game's own .aem (converted to .glb: the answer gives the .glb's path and its length for modelLength), audio ogg / wav / mp3, texts json / txt). Give 'text' for a text file or 'base64' for a binary one. " +
                              "A texture named like a game texture (textures/<name>.png) replaces it everywhere, textures/<assembly>/<name>.png on that model only, textures/ships/<ship id>/<name>.png on a new ship of the project that uses a game model (\"gof2:...\") alone. music/<name>.ogg named like a game track (search_content kind music) replaces it, any other name is a new track. sounds/<name>.ogg named like a game sound (kind sound) replaces it everywhere, any other name is a new sound for a quest's Sound step. voices/<clip>.ogg is a voice line in every language, voices/<language>/<clip>.ogg in one (en, de, fr, es, it, nl, pl, ru, pt, zh, hi); named like a game line (kind voice) it replaces that line, the story's included. Entries (ships.json...) go through put_entry instead.",
                input = Input(("path", "string", "its path in the project, e.g. portraits/vega.png", true), ("text", "string", "a text file's content", false),
                              ("base64", "string", "a binary file's content", false)),
                run = WriteFile, timeoutSeconds = 60f, ask = a => $"write the file {Str(a, "path")} into the project",
            },
            new Tool
            {
                name = "compile_mission", title = "Compile a mission", readOnly = true,
                description = "Turns a mission (one of the project's by name, or a .gof2mission object) into the script the game runs and checks it with the game's own event runner: the problems, and the script.",
                input = Input(("name", "string", "a mission of the project", false), ("mission", new JObject { ["type"] = "object" }, "or a .gof2mission object", false)),
                run = CompileQuest,
            },
            new Tool
            {
                name = "validate", title = "Validate the project", readOnly = true,
                description = "The game reads the project again and reports every error and warning of its files, and every mission's and event graph's problems.",
                input = Input(), run = Validate, timeoutSeconds = 90f,
            },
            new Tool
            {
                name = "get_mission_schema", title = "The mission format", readOnly = true,
                description = "The .gof2mission format as a JSON Schema (made from the game's own step table; missions.md in the guide is the same as text).",
                input = Input(), run = (a, c) => c.Json(QuestSchema.Build(), "mission schema"),
            },
            new Tool
            {
                name = "play_test", title = "Start a play test",
                description = "Plays the project as a player would: 'station' (docked at the builder's station), 'mission' (with the named mission under way; a bar mission is offered in the station's Space Lounge instead, by its client: take it there as a player would) or 'campaign' (from the project's campaign start). Edits wait until end_play_test. Watch it with screenshot, builder_status and get_log.",
                input = Input(("mode", Enum("station", "mission", "campaign"), "how to start", true), ("mission", "string", "mode mission: the mission's name", false)),
                run = PlayTest, timeoutSeconds = 90f, ask = a => $"start a play test ({Str(a, "mode") ?? "station"}{(Str(a, "mission") != null ? ": " + Str(a, "mission") : "")})",
            },
            new Tool
            {
                name = "end_play_test", title = "End the play test",
                description = "Back to the builder (the game as the project opened).",
                input = Input(), run = EndPlayTest, timeoutSeconds = 90f, noAsk = true,
            },
            new Tool
            {
                name = "get_log", title = "Read the game's log", readOnly = true,
                description = "The game's log since the server started (newest last): mod loading problems, mission errors, what the game did.",
                input = Input(("lines", "integer", "how many (default 80)", false), ("level", Enum("all", "warning", "error"), "the least level (default all)", false),
                              ("contains", "string", "only lines with this text", false)),
                run = GetLog,
            },
            new Tool
            {
                name = "screenshot", title = "Screenshot", readOnly = true,
                description = "What the game shows now (the builder, a test flight or a play test), as an image; 'clean' hides the builder's panels for it (the model or room alone). To see a model from any side use view_model, to aim the builder's camera view.",
                input = Input(("maxWidth", "integer", "at most this wide (default 1280)", false), ("clean", "boolean", "without the builder's panels", false)),
                run = Screenshot, timeoutSeconds = 15f,
            },
            new Tool
            {
                name = "undo", title = "Undo",
                description = "Takes the builder's last change back (any change: yours or the player's).",
                input = Input(), run = (a, c) => UndoRedo(c, false), ask = a => "undo the builder's last change",
            },
            new Tool
            {
                name = "redo", title = "Redo",
                description = "Brings back the change undo took back.",
                input = Input(), run = (a, c) => UndoRedo(c, true), ask = a => "redo the change undo took back",
            },
            new Tool
            {
                name = "show_kind", title = "Show a kind in the builder",
                description = "Switches the builder to a kind of content, so the player sees what was made (it may go to the lounge, the map or space).",
                input = Input(("kind", Enum(KindNames.Where(k => k != "manifest").ToArray()), "the kind", true)), run = ShowKind, timeoutSeconds = 60f, noAsk = true,
            },
        };

        // ---- status -------------------------------------------------------------------------------------------------

        static JObject Status()
        {
            var o = new JObject { ["builderOpen"] = ModBuilder.Active, ["playTesting"] = ModBuilder.PlayTesting, ["testFlying"] = ModBuilder.TestFlying,
                                  ["inSpace"] = ModBuilder.InSpace, ["scene"] = SceneManager.GetActiveScene().name, ["permission"] = ModBuilderMcp.Permission.ToString() };
            var p = Project;
            if (p != null)
            {
                o["project"] = new JObject
                {
                    ["id"] = p.Id, ["name"] = p.Name, ["version"] = p.Version, ["folder"] = p.Folder,
                    ["entries"] = new JObject
                    {
                        ["ships"] = p.Ships.Count, ["items"] = p.Items.Count, ["characters"] = p.Characters.Count, ["systems"] = p.Systems.Count,
                        ["stations"] = p.Stations.Count, ["npcShips"] = p.NpcShips.Count, ["interiors"] = p.Interiors.Count, ["races"] = p.Races.Count, ["blueprints"] = p.Blueprints.Count, ["kits"] = p.Kits.Count,
                        ["weaponEffects"] = p.WeaponEffects.Count,
                        ["gameOptions"] = p.GameOptions.Count, ["missions"] = p.Quests.Count, ["campaign"] = p.Campaign != null,
                    },
                    ["dependencies"] = new JArray(p.Dependencies),
                };
                var mod = ModManager.BuilderProject;
                if (mod != null) { o["errors"] = new JArray(mod.Errors.Take(20)); o["warnings"] = new JArray(mod.Warnings.Take(20)); }
            }
            var screen = ModBuilderScreen.Open;
            if (!ModBuilder.PlayTesting && screen != null && screen.Alive) { o["kindShown"] = screen.CurrentKind.ToString(); o["shown"] = screen.ExternalSelection(); }
            if (ModBuilder.PlayTesting)
            {
                var db = Database.Shared;
                o["game"] = new JObject
                {
                    ["station"] = Session.StationIndex, ["stationName"] = db.Stations.Find(s => s.index == Session.StationIndex)?.name,
                    ["docked"] = SceneManager.GetActiveScene().name == "Station", ["credits"] = Session.Credits, ["ship"] = Session.ShipIndex,
                    ["missions"] = new JArray(EventRunner.Quests().Select(q => new JObject { ["name"] = q.name, ["title"] = q.title, ["objective"] = q.objective, ["target"] = q.target })),
                    ["questsDone"] = new JArray(Session.GraphQuestsDone),
                    ["cargo"] = new JArray(Session.Cargo.Select(c => new JObject { ["item"] = c.item, ["amount"] = c.amount })),
                };
            }
            return o;
        }

        // ---- content ----------------------------------------------------------------------------------------------

        static void SearchContent(JObject a, Call c)
        {
            string kind = Str(a, "kind");
            int k = Array.IndexOf(RefKinds, kind);
            if (k < 0 && Array.IndexOf(AudioRefKinds, kind) >= 0)
            {
                var aw = (Str(a, "query") ?? "").ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                int alimit = Mathf.Clamp(Int(a, "limit", 40), 1, 500);
                var afound = new JArray();
                int atotal = 0;
                foreach (var (value, name, about) in AudioRefs(kind))
                {
                    string hay = $"{value} {name} {about}".ToLowerInvariant();
                    if (aw.Any(w => !hay.Contains(w))) continue;
                    atotal++;
                    if (afound.Count < alimit) afound.Add(new JObject { ["value"] = value, ["name"] = name, ["about"] = about });
                }
                c.Json(new JObject { ["kind"] = kind, ["found"] = atotal, ["shown"] = afound.Count, ["results"] = afound }, $"{kind} \"{Str(a, "query")}\": {atotal}");
                return;
            }
            if (k < 0) throw new ArgumentException("kind: one of " + string.Join(", ", RefKinds.Concat(AudioRefKinds)));
            var refKind = (RefKind)k;
            var words = (Str(a, "query") ?? "").ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int limit = Mathf.Clamp(Int(a, "limit", 40), 1, 500);
            var found = new JArray();
            int total = 0;
            foreach (var e in ModRefs.List(refKind, Project))
            {
                string hay = $"{e.value} {e.name} {e.sub} {e.group}".ToLowerInvariant();
                if (words.Any(w => !hay.Contains(w))) continue;
                total++;
                if (found.Count >= limit) continue;
                var o = new JObject { ["value"] = e.value, ["name"] = e.name, ["from"] = e.group == ModRefs.GameGroup ? "game" : e.own ? "this project" : "mod " + e.modId };
                if (!string.IsNullOrEmpty(e.sub)) o["about"] = e.sub;
                found.Add(o);
            }
            c.Json(new JObject { ["kind"] = kind, ["found"] = total, ["shown"] = found.Count, ["results"] = found }, $"{kind} \"{Str(a, "query")}\": {total}");
        }

        static int KindIndex(JObject a)
        {
            string kind = Str(a, "kind");
            int i = Array.IndexOf(KindNames, kind);
            if (i < 0) throw new ArgumentException("kind: one of " + string.Join(", ", KindNames));
            return i;
        }

        static void ListEntries(JObject a, Call c)
        {
            int k = KindIndex(a);
            if (Project == null) { c.Fail("The Mod Builder isn't open."); return; }
            var state = Project.CaptureState();
            var list = new JArray();
            string kind = Kinds[k].kind;
            if (kind == "missions")
                foreach (var p in ((JObject)state["quests"]).Properties())
                    list.Add(new JObject { ["name"] = p.Name, ["title"] = p.Value["title"], ["kind"] = p.Value["kind"] ?? "quest", ["steps"] = (p.Value["steps"] as JArray)?.Count ?? 0 });
            else if (kind == "campaign") { if (state["campaign"] is JObject camp) list.Add(new JObject { ["name"] = camp["name"] }); }
            else if (kind == "manifest") list.Add(new JObject { ["id"] = Project.Id, ["name"] = Project.Name });
            else
                foreach (var e in ((JArray)state[kind]).OfType<JObject>())
                    list.Add(new JObject { ["id"] = Ident(e), ["name"] = e["name"] });
            c.Json(new JObject { ["kind"] = kind, ["file"] = Kinds[k].file, ["identity"] = Kinds[k].ident, ["entries"] = list }, $"{kind}: {list.Count}");
        }

        static void GetEntries(JObject a, Call c)
        {
            int k = KindIndex(a);
            if (Project == null) { c.Fail("The Mod Builder isn't open."); return; }
            var ids = (a["ids"] as JArray)?.Select(t => (string)t).Where(s => !string.IsNullOrEmpty(s)).ToList() ?? new List<string>();
            var state = Project.CaptureState();
            string kind = Kinds[k].kind;
            JToken result;
            if (kind == "missions")
            {
                var o = new JObject();
                foreach (var p in ((JObject)state["quests"]).Properties())
                    if (ids.Count == 0 || ids.Contains(p.Name)) o[p.Name] = p.Value;
                result = o;
            }
            else if (kind == "campaign") result = state["campaign"];
            else if (kind == "manifest") result = state["manifest"];
            else result = new JArray(((JArray)state[kind]).OfType<JObject>().Where(e => ids.Count == 0 || ids.Contains(Ident(e))));
            string path = Str(a, "path");
            if (!string.IsNullOrEmpty(path))
            {
                JToken one = kind == "missions" || kind == "campaign" || kind == "manifest"
                    ? (kind == "missions" ? (result as JObject)?.Properties().FirstOrDefault()?.Value : result)
                    : (result as JArray)?.FirstOrDefault();
                if (ids.Count != 1 && kind != "campaign" && kind != "manifest") { c.Fail("path: give exactly one id."); return; }
                if (one == null || one.Type == JTokenType.Null) { c.Fail($"No {kind} entry \"{ids.FirstOrDefault()}\"."); return; }
                var part = EntryPatch.Get(one, path, out string err);
                if (part == null) { c.Fail(err); return; }
                c.Json(new JObject { ["path"] = path, ["value"] = part }, $"{kind} {ids.FirstOrDefault()} {path}");
                return;
            }
            c.Json(result, $"{kind}{(ids.Count > 0 ? " " + string.Join(", ", ids) : "")}");
        }

        static readonly string[] ManifestFields = { "name", "description", "version", "author", "website", "credits", "dependencies", "preview" };

        static void PutEntry(JObject a, Call c)
        {
            int k = KindIndex(a);
            var screen = Screen(out string why);
            if (screen == null) { c.Fail(why); return; }
            if (!(a["entry"] is JObject entry)) throw new ArgumentException("entry: the entry's JSON object.");
            entry = (JObject)entry.DeepClone();
            string kind = Kinds[k].kind, name = Str(a, "name"), label = null;
            List<string> questProblems = null;
            string error = screen.ExternalEdit(state =>
            {
                switch (kind)
                {
                    case "missions":
                        if (string.IsNullOrEmpty(name) || !QuestFile.IsName(name)) return "name: the mission's name (letters, digits, _; it is the file name).";
                        entry["format"] = Events.QuestFile.Format;
                        if (!(entry["steps"] is JArray)) entry["steps"] = new JArray();
                        questProblems = new List<string>();
                        QuestFile.Compile(entry, questProblems);
                        ((JObject)state["quests"])[name] = entry;
                        label = $"mission {name}";
                        return null;
                    case "campaign":
                        state["campaign"] = entry;
                        label = "the campaign";
                        return null;
                    case "manifest":
                    {
                        var m = (JObject)state["manifest"];
                        foreach (var p in entry.Properties())
                        {
                            if (!ManifestFields.Contains(p.Name)) return $"mod.json: {p.Name} can't be set here (the fields: {string.Join(", ", ManifestFields)}).";
                            m[p.Name] = p.Value.DeepClone();
                        }
                        label = "mod.json";
                        return null;
                    }
                    default:
                    {
                        string id = Ident(entry);
                        if (string.IsNullOrEmpty(id)) return $"{kind}: the entry needs {Kinds[k].ident}.";
                        var list = (JArray)state[kind];
                        var old = list.OfType<JObject>().FirstOrDefault(e => Ident(e) == id);
                        if (old != null) old.Replace(entry); else list.Add(entry);
                        label = $"{kind} {id}{(old != null ? " changed" : " added")}";
                        return null;
                    }
                }
            }, kind == "missions" ? $"mission {name}" : $"{kind}");
            if (error != null) { c.Fail(error); return; }
            AnswerAfterReload(c, screen, label, kind == "missions" ? entry : null, questProblems);
        }

        /// <summary>The answer once the game has read the project again: its problems with the files, and a mission's own
        /// (QuestFile's, then the event runner's on the compiled script).</summary>
        static void AnswerAfterReload(Call c, ModBuilderScreen screen, string label, JObject quest, List<string> questProblems, JObject extra = null)
        {
            c.Wait(() => !screen.ReloadBusy, () =>
            {
                var o = new JObject { ["ok"] = true, ["done"] = label };
                if (extra != null) foreach (var p in extra.Properties()) o[p.Name] = p.Value;
                var mod = ModManager.BuilderProject;
                if (mod != null)
                {
                    if (mod.Errors.Count > 0) o["errors"] = new JArray(mod.Errors.Take(20));
                    if (mod.Warnings.Count > 0) o["warnings"] = new JArray(mod.Warnings.Take(20));
                }
                if (questProblems != null && questProblems.Count > 0) o["questProblems"] = new JArray(questProblems);
                if (quest != null && (questProblems == null || questProblems.Count == 0))
                {
                    string script = QuestFile.Compile(quest, new List<string>());
                    string runner = EventRunner.CheckScript(script);
                    if (runner != null) o["questProblems"] = new JArray(runner);
                }
                bool bad = o["errors"] != null || o["questProblems"] != null;
                if (bad) o["ok"] = false;
                c.Json(o, label + (bad ? " (with problems)" : ""));
            });
        }

        /// <summary>The steps after 'step' in its list moved down (with their blocks' steps) when it overlaps the next one.</summary>
        static void MakeRoomAfter(JObject step)
        {
            const float Height = 150f, Width = 260f;
            if (!(step.Parent is JArray list) || !QuestGraph.HasPos(step)) return;
            int i = list.IndexOf(step);
            if (i < 0 || i + 1 >= list.Count || !(list[i + 1] is JObject next) || !QuestGraph.HasPos(next)) return;
            var (x, y) = QuestGraph.Pos(step);
            var (nx, ny) = QuestGraph.Pos(next);
            if (Mathf.Abs(nx - x) >= Width || ny - y >= Height) return;
            float dy = y + Height - ny;
            void Shift(JObject st)
            {
                if (QuestGraph.HasPos(st)) { var (px, py) = QuestGraph.Pos(st); QuestGraph.SetPos(st, px, py + dy); }
                foreach (var (_, child) in QuestSteps.Children(st)) foreach (var c in child.OfType<JObject>()) Shift(c);
            }
            for (int k = i + 1; k < list.Count; k++) if (list[k] is JObject f) Shift(f);
        }

        static void PatchEntry(JObject a, Call c)
        {
            int k = KindIndex(a);
            var screen = Screen(out string why);
            if (screen == null) { c.Fail(why); return; }
            if (!(a["ops"] is JArray ops) || ops.Count == 0) throw new ArgumentException("ops: a list of operations.");
            string kind = Kinds[k].kind, id = Str(a, "id"), label = null;
            JObject patchedQuest = null;
            List<string> questProblems = null;
            string error = screen.ExternalEdit(state =>
            {
                // The entry and where it goes back.
                JToken target;
                Action<JToken> put;
                switch (kind)
                {
                    case "missions":
                    {
                        var qs = (JObject)state["quests"];
                        target = qs[id ?? ""];
                        if (target == null) return $"No mission \"{id}\" in the project.";
                        put = v => qs[id] = v;
                        break;
                    }
                    case "campaign":
                        target = state["campaign"];
                        if (!(target is JObject)) return "The project has no campaign (put_entry makes one).";
                        put = v => state["campaign"] = v;
                        break;
                    case "manifest":
                        target = state["manifest"];
                        put = v => state["manifest"] = v;
                        break;
                    default:
                    {
                        var list = (JArray)state[kind];
                        var e = list.OfType<JObject>().FirstOrDefault(x => Ident(x) == id);
                        if (e == null) return $"No {kind} entry \"{id}\" (list_entries lists them).";
                        target = e;
                        put = v => e.Replace(v);
                        break;
                    }
                }
                var patched = EntryPatch.Apply(target, ops, out string err);
                if (patched == null) return err;
                if (!(patched is JObject po)) return "The entry must stay an object.";
                // Its identity stays (a new id is a new entry: put_entry).
                if (kind != "missions" && kind != "campaign" && kind != "manifest" && Ident(po) != id) return "The id / override can't change by a patch: put_entry the new entry (and delete_entry the old one).";
                if (kind == "manifest")
                    foreach (var p in po.Properties())
                        if (!JToken.DeepEquals(p.Value, target[p.Name]) && !ManifestFields.Contains(p.Name)) return $"mod.json: {p.Name} can't be changed here (the fields: {string.Join(", ", ManifestFields)}).";
                if (kind == "missions")
                {
                    po["format"] = Events.QuestFile.Format;
                    // A step put in without a place gets one in its column (only a mission already laid out in the graph), and
                    // the steps after it in its list move down when it lands on them, as an insert in the graph looks.
                    if (QuestGraph.HasPos(po, "startPos"))
                    {
                        var fresh = QuestGraph.AllSteps(po).Where(st => !QuestGraph.HasPos(st)).ToList();
                        QuestGraph.Layout(po, false, _ => 90f);
                        foreach (var st in fresh) MakeRoomAfter(st);
                    }
                    questProblems = new List<string>();
                    QuestFile.Compile(po, questProblems);
                    patchedQuest = po;
                }
                put(po);
                label = $"{kind} {id ?? kind} patched ({ops.Count} change(s))";
                return null;
            }, kind == "missions" ? $"mission {id} edited" : $"{kind} {id} edited");
            if (error != null) { c.Fail(error); return; }
            AnswerAfterReload(c, screen, label, patchedQuest, questProblems);
        }

        static void DeleteEntry(JObject a, Call c)
        {
            int k = KindIndex(a);
            var screen = Screen(out string why);
            if (screen == null) { c.Fail(why); return; }
            string kind = Kinds[k].kind, id = Str(a, "id");
            if (kind == "manifest") { c.Fail("mod.json can't be removed."); return; }
            var state = Project.CaptureState();
            bool exists = kind == "missions" ? ((JObject)state["quests"])[id ?? ""] != null
                        : kind == "campaign" ? state["campaign"] is JObject
                        : ((JArray)state[kind]).OfType<JObject>().Any(e => Ident(e) == id);
            if (!exists) { c.Fail($"No {kind} entry \"{id}\"."); return; }
            string what = kind == "campaign" ? "the campaign" : $"{kind} \"{id}\"";
            ModBuilderMcp.Permit(c, "remove " + what + " from the project (Undo brings it back)", true, () =>
            {
                string error = screen.ExternalEdit(st =>
                {
                    if (kind == "missions") ((JObject)st["quests"]).Remove(id);
                    else if (kind == "campaign") st["campaign"] = JValue.CreateNull();
                    else ((JArray)st[kind]).OfType<JObject>().First(e => Ident(e) == id).Remove();
                    return null;
                }, "removed " + what);
                if (error != null) c.Fail(error); else c.Text("Removed " + what + ".");
            });
        }

        // ---- files --------------------------------------------------------------------------------------------------

        /// <summary>A path inside the project's folder, or why not.</summary>
        static string ProjectPath(string rel, bool write, out string why)
        {
            why = null;
            if (Project == null) { why = "The Mod Builder isn't open."; return null; }
            rel = (rel ?? "").Replace('\\', '/').Trim().TrimStart('/');
            if (rel.Length == 0 || rel.Contains("..") || rel.Contains(":")) { why = "path: a path inside the project, like portraits/vega.png."; return null; }
            string ext = Path.GetExtension(rel).ToLowerInvariant();
            if (write)
            {
                string folder = rel.Split('/')[0];
                if (!FileFolders.Contains(folder)) { why = $"Files go into one of: {string.Join(", ", FileFolders)} (entries such as ships.json through put_entry)."; return null; }
                if (!FileExtensions.Contains(ext)) { why = $"Not a file type a mod uses: {ext}"; return null; }
            }
            string full = Path.GetFullPath(Path.Combine(Project.Folder, rel));
            string root = Path.GetFullPath(Project.Folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) { why = "path: outside the project."; return null; }
            return full;
        }

        static void ListFiles(JObject a, Call c)
        {
            if (Project == null) { c.Fail("The Mod Builder isn't open."); return; }
            string folder = (Str(a, "folder") ?? "").Replace('\\', '/').Trim('/');
            string dir = folder.Length > 0 ? ProjectPath(folder, false, out _) : Project.Folder;
            if (dir == null) { c.Fail("folder: inside the project."); return; }
            var list = new JArray();
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(1000))
                    list.Add(new JObject { ["path"] = f.Substring(Project.Folder.Length).TrimStart('/', '\\').Replace('\\', '/'), ["bytes"] = new FileInfo(f).Length });
            c.Json(new JObject { ["files"] = list }, $"{list.Count} files");
        }

        static void ReadFile(JObject a, Call c)
        {
            string full = ProjectPath(Str(a, "path"), false, out string why);
            if (full == null) { c.Fail(why); return; }
            if (!File.Exists(full)) { c.Fail("No such file."); return; }
            if (!TextExtensions.Contains(Path.GetExtension(full).ToLowerInvariant())) { c.Fail("Only text files (json, gof2mission, gof2event, txt, md)."); return; }
            if (new FileInfo(full).Length > 2_000_000) { c.Fail("Over 2 MB."); return; }
            c.Text(File.ReadAllText(full));
        }

        static void WriteFile(JObject a, Call c)
        {
            var screen = Screen(out string why);
            if (screen == null) { c.Fail(why); return; }
            string rel = Str(a, "path");
            string full = ProjectPath(rel, true, out why);
            if (full == null) { c.Fail(why); return; }
            byte[] bytes;
            if (a["text"] is JValue t && t.Type == JTokenType.String) bytes = Encoding.UTF8.GetBytes((string)t);
            else if (a["base64"] is JValue b && b.Type == JTokenType.String)
            {
                try { bytes = Convert.FromBase64String((string)b); }
                catch (FormatException) { c.Fail("base64: not base64."); return; }
            }
            else { c.Fail("Give 'text' or 'base64'."); return; }
            if (bytes.Length > 64 * 1024 * 1024) { c.Fail("Over 64 MB."); return; }
            string ext = Path.GetExtension(full).ToLowerInvariant();
            if (ext == ".aem") { WriteAem(c, screen, rel, bytes); return; }   // the game's own model: kept as .glb
            if (ext == ".glb" && (bytes.Length < 4 || bytes[0] != 'g' || bytes[1] != 'l' || bytes[2] != 'T' || bytes[3] != 'F')) { c.Fail("Not a glTF binary (.glb)."); return; }
            if (ext == ".json" || ext == ".gof2event")
            {
                if (ext == ".json") try { JToken.Parse(Encoding.UTF8.GetString(bytes)); } catch (Exception e) { c.Fail("Not JSON: " + e.Message); return; }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, bytes);
            screen.ExternalFilesChanged("wrote " + rel);
            c.Wait(() => !screen.ReloadBusy, () =>
            {
                var o = new JObject { ["ok"] = true, ["path"] = rel.Replace('\\', '/'), ["bytes"] = bytes.Length };
                var mod = ModManager.BuilderProject;
                if (mod != null && mod.Errors.Count > 0) o["errors"] = new JArray(mod.Errors.Take(20));
                c.Json(o, "wrote " + rel);
            });
        }

        // ---- checks -------------------------------------------------------------------------------------------------

        static void CompileQuest(JObject a, Call c)
        {
            JObject quest = a["mission"] as JObject;
            string name = Str(a, "name");
            if (quest == null)
            {
                if (Project == null) { c.Fail("The Mod Builder isn't open."); return; }
                quest = Project.Quests.FirstOrDefault(q => q.Name == name)?.ToJson();
                if (quest == null) { c.Fail($"No mission \"{name}\" in the project (or give 'mission')."); return; }
            }
            var problems = new List<string>();
            string script = QuestFile.Compile(quest, problems);
            if (problems.Count == 0)
            {
                string runner = EventRunner.CheckScript(script);
                if (runner != null) problems.Add(runner);
            }
            c.Json(new JObject { ["ok"] = problems.Count == 0, ["problems"] = new JArray(problems), ["script"] = script }, (name ?? "mission") + (problems.Count == 0 ? " compiles" : ": " + problems[0]));
        }

        static void Validate(JObject a, Call c)
        {
            if (Project == null) { c.Fail("The Mod Builder isn't open."); return; }
            var screen = ModBuilder.PlayTesting ? null : Screen(out _);
            screen?.ExternalReload();
            c.Wait(() => screen == null || !screen.ReloadBusy, () =>
            {
                var mod = ModManager.BuilderProject;
                // The loaders that read their files when asked.
                try { ModCharacters.All(); ModGameOptions.All(); ModCampaigns.All(); ModBlueprints.All().ToList(); } catch (Exception) { }
                var o = new JObject
                {
                    ["errors"] = new JArray(mod?.Errors ?? new List<string>()),
                    ["warnings"] = new JArray(mod?.Warnings ?? new List<string>()),
                };
                var quests = new JObject();
                foreach (var q in Project.Quests)
                {
                    var problems = new List<string>();
                    string script = QuestFile.Compile(q.ToJson(), problems);
                    if (problems.Count == 0) { string r = EventRunner.CheckScript(script); if (r != null) problems.Add(r); }
                    if (problems.Count > 0) quests[q.Name] = new JArray(problems);
                }
                foreach (var g in Project.EventGraphs())
                {
                    string path = Project.EventGraphPath(g);
                    var problems = new List<EventGraphScript.Problem>();
                    if (path != null) EventGraphScript.FromFile(File.ReadAllText(path), problems);
                    var errs = problems.Where(p => !p.warning).Select(p => (p.node != null ? p.node + ": " : "") + p.message).ToList();
                    if (errs.Count > 0) quests[g + " (graph)"] = new JArray(errs);
                }
                if (quests.HasValues) o["missions"] = quests;
                o["ok"] = (mod == null || mod.Errors.Count == 0) && !quests.HasValues;
                c.Json(o, (bool)o["ok"] ? "valid" : $"{mod?.Errors.Count ?? 0} errors, {quests.Count} missions with problems");
            });
        }

        // ---- play tests -------------------------------------------------------------------------------------------

        static void PlayTest(JObject a, Call c)
        {
            var screen = Screen(out string why);
            if (screen == null) { c.Fail(why); return; }
            string mode = Str(a, "mode") ?? "station";
            screen.ExternalSaveNow();
            if (mode == "mission")
            {
                string name = Str(a, "mission");
                var q = Project.Quests.FirstOrDefault(x => x.Name == name);
                if (q == null && !Project.EventGraphs().Contains(name)) { c.Fail($"No mission \"{name}\" in the project."); return; }
                if (q != null)
                {
                    var problems = new List<string>();
                    QuestFile.Compile(q.ToJson(), problems);
                    if (problems.Count > 0) { c.Fail("The mission doesn't compile: " + problems[0]); return; }
                }
                ModBuilder.Reload();
                why = ModBuilderPlay.StartQuest(name);
            }
            else if (mode == "campaign") why = ModBuilderPlay.StartCampaign();
            else ModBuilderPlay.Start();
            if (why != null) { c.Fail(why); return; }
            float t0 = Time.realtimeSinceStartup;
            c.Wait(() => ModBuilder.PlayTesting && SceneManager.GetActiveScene().isLoaded && Time.realtimeSinceStartup - t0 > 3f,
                   () => c.Json(Status(), "play test (" + mode + ")"));
        }

        static void EndPlayTest(JObject a, Call c)
        {
            if (!ModBuilder.PlayTesting) { c.Fail("No play test is running."); return; }
            ModBuilderPlay.End();
            c.Wait(() => !ModBuilder.PlayTesting && ModBuilderScreen.Open != null && ModBuilderScreen.Open.Alive, () => c.Json(Status(), "back in the builder"));
        }

        // ---- the rest -------------------------------------------------------------------------------------------------

        static void GetLog(JObject a, Call c)
        {
            int n = Mathf.Clamp(Int(a, "lines", 80), 1, 600);
            string level = Str(a, "level") ?? "all", contains = Str(a, "contains");
            List<LogLine> lines;
            lock (log) lines = log.ToList();
            var picked = lines.Where(l => level == "all" || level == "warning" && l.kind != "info" || level == "error" && l.kind == "error")
                              .Where(l => string.IsNullOrEmpty(contains) || l.text.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0)
                              .Reverse().Take(n).Reverse();
            var sb = new StringBuilder();
            foreach (var l in picked) sb.Append(l.time).Append(' ').Append(l.kind == "info" ? "" : l.kind.ToUpperInvariant() + " ").Append(l.text).Append('\n');
            c.Text(sb.Length > 0 ? sb.ToString() : "(nothing logged)");
        }

        static void Screenshot(JObject a, Call c)
        {
            var pump = ModBuilderMcpPump.Instance;
            if (pump == null) { c.Fail("Not ready."); return; }
            if (a["clean"] is JValue cl && cl.Type == JTokenType.Boolean && (bool)cl && ModBuilderScreen.Open != null && ModBuilderScreen.Open.Alive)
            {
                Answer(c, true, true, $"{SceneManager.GetActiveScene().name}, the builder's panels hidden");
                return;
            }
            int w = Mathf.Clamp(Int(a, "maxWidth", 1280), 320, 3840);
            pump.Capture(w, (png, width, height) => c.Image(png, $"{width} x {height}, {SceneManager.GetActiveScene().name}"));
        }

        static void UndoRedo(Call c, bool redo)
        {
            var screen = Screen(out string why);
            if (screen == null) { c.Fail(why); return; }
            if (!screen.ExternalUndo(redo)) { c.Fail(redo ? "Nothing to redo." : "Nothing to undo."); return; }
            c.Text(redo ? "Redone." : "Undone.");
        }

        static void ShowKind(JObject a, Call c)
        {
            int k = KindIndex(a);
            var screen = Screen(out string why);
            if (screen == null) { c.Fail(why); return; }
            var show = Kinds[k].show;
            screen.ExternalSelect(show);
            // Another scene's kind loads it: answered once the builder is back.
            c.Wait(() => ModBuilderScreen.Open != null && ModBuilderScreen.Open.Alive && ModBuilderScreen.Open.CurrentKind == show,
                   () => c.Text($"The builder shows {show}."));
        }
    }
}
