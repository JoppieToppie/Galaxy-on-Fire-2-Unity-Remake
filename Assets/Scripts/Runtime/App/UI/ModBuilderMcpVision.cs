// ModBuilderMcpVision.cs
// The Mod Builder MCP server's tools that let an AI see and drive the builder (part of ModBuilderMcp):
//   view_model      any model (a game assembly "gof2:<name>", a project or game ship with its hardpoints, a project or game
//                   station, a project .glb) rendered off-screen from named angles into one contact sheet (ModelViewer)
//   view_texture    a project image or a game texture (by name, optionally of a model) as an image: scaled, a normal map
//                   decoded to an ordinary RGB normal map, the alpha on a checkerboard, as a mask or kept
//   list_textures   a model's textures with their roles and sizes, and which the project replaces
//   select_entry    an entry of any kind picked in the builder (its page, the turntable / room / orbit / mission graph shows it)
//   view            the builder's camera from a named side, zoomed, or reset; with a screenshot
//   hardpoint       the Ships kind's hardpoints: listed, added onto the hull from a side, snapped onto it, selected,
//                   mirrored, removed
//   engines, test_fly / end_test_fly, weapon_range, switch_hangar
//   list_projects / create_project / open_project / export_mod
//   loadout         the Ships kind's test loadout (an item per slot, auto-fill) and the test cheats
//   room_marker     the Hangars & bars kind's markers: listed, placed where a line from a point inside the room meets its
//                   surface (or at a position), removed
//   press / steer / ui_buttons / ui_click   in a play test or test flight: keys, game actions and the stick held for a while,
//                   the UI's buttons by their text (McpInput), so a mission can be played through
//   view's 'map'    the Systems kind's star map centred on a system
// Each goes through the builder's own screens and methods (ModBuilderScreenAi's hooks); changes through the permission gate.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using GoF2Remake.Visuals;
using GoF2Remake.World;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GoF2Remake.UI
{
    public static partial class ModBuilderMcp
    {
        static readonly string[] SelectKinds = { "ships", "items", "characters", "skins", "blueprints", "kits", "gameOptions", "campaign", "interiors", "missions", "systems", "races", "stations", "weaponFx", "weaponEffects" };
        static readonly string[] AngleNames = ModelViewer.Angles.Select(a => a.name).ToArray();

        static JObject StrArray(string description) => new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = description };
        static JObject NumArray3() => new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "number" }, ["minItems"] = 3, ["maxItems"] = 3 };

        static IEnumerable<Tool> VisionTools() => new[]
        {
            new Tool
            {
                name = "view_model", title = "Look at a model", readOnly = true,
                description = "Renders a model off-screen (nothing on the game's screen changes) from named angles into one image: " +
                              "\"gof2:<assembly>\" (any game model, names in reference.md), \"ship:<id | override:N | game number>\" (with its hardpoints " +
                              "drawn as tagged dots: P primary red, S secondary amber, T turret cyan, E engine violet; hollow where the hull hides them), " +
                              "\"station:<id | override:... | game number>\", or a project file \"models/x.glb\". Angles: front, back, left, right " +
                              "(the pilot's), top, bottom, threequarter, rear (default left, top, front, threequarter). The caption gives its size.",
                input = Input(("model", "string", "what to draw", true), ("angles", StrArray("views, e.g. [\"left\", \"top\"]"), "the views", false),
                              ("size", "integer", "pixels per view (128-1024, default 512)", false),
                              ("background", Enum("grey", "dark", "light", "transparent"), "default grey", false),
                              ("hardpoints", "boolean", "draw a ship's hardpoints (default true)", false), ("glow", "boolean", "draw the engine glows too", false)),
                run = ViewModel, timeoutSeconds = 60f,
            },
            new Tool
            {
                name = "view_texture", title = "Look at a texture", readOnly = true,
                description = "A texture as an image: a project file (\"textures/hull.png\", any image the project has) or a game texture by name " +
                              "(as in reference.md's \"Models and their textures\", e.g. ship_028_terran_diffuse; give 'model' when it isn't found by its name). " +
                              "Scaled to maxSize; a normal map comes back as an ordinary RGB normal map; alpha on a checkerboard by default.",
                input = Input(("texture", "string", "a project path or a game texture's name", true), ("model", "string", "a model that uses it (\"gof2:<name>\" or \"ship:N\")", false),
                              ("maxSize", "integer", "at most this many pixels a side (default 1024)", false),
                              ("alpha", Enum("checker", "keep", "mask", "ignore"), "the alpha: over a checkerboard (default), kept, shown as a grey mask, or dropped", false)),
                run = ViewTexture, timeoutSeconds = 30f,
            },
            new Tool
            {
                name = "list_textures", title = "List a model's textures", readOnly = true,
                description = "The textures a model draws (\"gof2:<name>\", \"ship:...\", \"station:...\"): name, role (colour, normal map, metallic, glow / lights), size, and the project file replacing it if any. A skin replaces one with textures/<name>.png.",
                input = Input(("model", "string", "the model", true)), run = ListTextures, timeoutSeconds = 30f,
            },
            new Tool
            {
                name = "select_entry", title = "Show an entry in the builder",
                description = "Picks an entry as a click on its row would, going to its kind first: its page shows, and the turntable, room, map or orbit shows it. " +
                              "Kinds: " + string.Join(", ", SelectKinds) + " (skins: the game ship's number; weaponFx: an item, fired on the range; weaponEffects: an effect of weaponfx.json, fired on its base weapon). " +
                              "Options: ships {\"tab\": \"ship\" | \"hardpoints\" | \"loadout\"}, missions {\"graph\": true for the node graph, \"wide\": true}. 'capture' adds a screenshot.",
                input = Input(("kind", Enum(SelectKinds), "the kind", true), ("id", "string", "an id, \"override:<n>\", a mission's name or a skinned ship's number", true),
                              ("options", new JObject { ["type"] = "object" }, "per kind (see above)", false),
                              ("capture", "boolean", "answer with a screenshot (default true)", false), ("clean", "boolean", "the screenshot without the builder's panels", false)),
                run = SelectEntry, timeoutSeconds = 90f, noAsk = true,
            },
            new Tool
            {
                name = "view", title = "Aim the builder's camera",
                description = "The builder's camera looks at what it shows (the turntable ship, a room in Hangars & bars, a station in space) from a named side: " +
                              string.Join(", ", AngleNames) + "; 'zoom' in its size (ship radii in the hangar, about 1.2-4; default kept); 'reset' its own framing. " +
                              "The hangar floor keeps the camera above the pad (for a ship's underside use view_model). Answers with a screenshot unless 'capture' is false.",
                input = Input(("angle", Enum(AngleNames), "the side", false), ("zoom", "number", "how far, in the shown thing's size", false), ("reset", "boolean", "back to the default view", false),
                              ("map", "string", "the Systems kind's star map instead: centred on a system (its number or the project's id)", false),
                              ("capture", "boolean", "answer with a screenshot (default true)", false), ("clean", "boolean", "the screenshot without the builder's panels", false)),
                run = View, timeoutSeconds = 30f, noAsk = true,
            },
            new Tool
            {
                name = "hardpoint", title = "Place hardpoints on the hull",
                description = "The Ships kind's hardpoints on the selected ship (select_entry ships first): op 'list'; 'add' (type primary | secondary | turret | engine) " +
                              "where a line from a side ('from': an angle or [x, y, z] in the ship's game frame, x the pilot's left, y up, z the nose) through 'at' " +
                              "([x, y, z] game units; or 'offset' from the hull's centre; default the centre; op list gives the hull's box) first meets the hull (a turret faces out of it there); 'snap' (an existing point onto the " +
                              "hull along 'from', default straight out from the hull's centre); 'select' (its move handles show); 'mirror'; 'delete'. Points by 'index' " +
                              "(the list's) or 'tag' (\"P1\", \"E2\"). Each change is an undo step; the answer lists the hardpoints.",
                input = Input(("op", Enum("list", "add", "snap", "select", "mirror", "delete"), "what to do", true), ("type", Enum("primary", "secondary", "turret", "engine"), "add: its type", false),
                              ("from", new JObject { ["description"] = "an angle name or [x, y, z]" }, "the side the hull is reached from", false), ("at", NumArray3(), "add: a point the line goes through", false), ("offset", NumArray3(), "add: or that point as an offset from the hull's centre", false),
                              ("index", "integer", "an existing point's index", false), ("tag", "string", "or its tag, e.g. P1", false)),
                run = Hardpoint, timeoutSeconds = 30f,
                ask = a => ((string)a["op"] ?? "list") == "list" ? null : $"{(string)a["op"]} a hardpoint on the ship",
            },
            new Tool
            {
                name = "engines", title = "The turntable ship's engines",
                description = "The hangar's Engines slider: 0 off, up to 0.9 the throttle, above it the boost (the glow and exhaust as in flight).",
                input = Input(("level", "number", "0..1", true)), run = Engines, noAsk = true,
            },
            new Tool
            {
                name = "test_fly", title = "Test fly",
                description = "Takes off in the project's ship (Ships, Items or Skins kind; the loadout and the test cheats of the Loadout tab) into the builder station's orbit, where pirates keep coming. Watch with screenshot; end_test_fly lands back.",
                input = Input(), run = TestFly, timeoutSeconds = 90f, ask = a => "start a test flight",
            },
            new Tool
            {
                name = "end_test_fly", title = "End the test flight",
                description = "Back in the builder's hangar.",
                input = Input(), run = EndTestFly, timeoutSeconds = 90f, noAsk = true,
            },
            new Tool
            {
                name = "weapon_range", title = "The weapon fx range",
                description = "In space, the Weapon fx kind: a weapon of the project (its item id) fires from a parked ship at a target, again and again; 'fire' false pauses it. Answers with a screenshot unless 'capture' is false.",
                input = Input(("item", "string", "the item's id or \"override:<n>\", or \"effect:<id>\" for a weapon effect", true), ("fire", "boolean", "fire (default) or pause", false), ("capture", "boolean", "answer with a screenshot (default true)", false)),
                run = WeaponRange, timeoutSeconds = 90f, noAsk = true,
            },
            new Tool
            {
                name = "switch_hangar", title = "Pick the builder's hangar",
                description = "Moves the builder to another kind of hangar (terran, vossk, nivelian, midorian, deep science, battlestation, or a station number), whose orbit the test flights use.",
                input = Input(("which", "string", "the hangar kind or a station number", true)), run = SwitchHangar, timeoutSeconds = 90f,
                ask = a => $"move the builder to the {Str(a, "which")} hangar",
            },
            new Tool
            {
                name = "loadout", title = "The test flight's loadout",
                description = "The selected ship's test loadout in the Ships kind (select_entry ships first): op 'list'; 'set' an item into a slot ('kind' primary | secondary | turret | equipment, 'slot' from 0, 'item' a game number, a key or this project's item id, or \"empty\"); 'autofill' (the dearest fitting items, like the tab's Auto-fill); 'cheats' (godMode / infiniteAmmo true or false). test_fly flies it.",
                input = Input(("op", Enum("list", "set", "autofill", "cheats"), "what to do", true), ("kind", Enum("primary", "secondary", "turret", "equipment"), "set: the slot's kind", false),
                              ("slot", "integer", "set: the slot, from 0", false), ("item", "string", "set: the item, or \"empty\"", false),
                              ("godMode", "boolean", "cheats", false), ("infiniteAmmo", "boolean", "cheats", false)),
                run = (a, c) => ScreenEdit(c, a, (ModBuilderScreen s, out JObject d) => s.ExternalLoadout(a, out d), "loadout"),
                ask = a => ((string)a["op"] ?? "list") == "list" ? null : "change the test loadout",
            },
            new Tool
            {
                name = "room_marker", title = "A room's markers",
                description = "The Hangars & bars kind's room (select_entry interiors first): op 'list' (each marker, required or not, its position in metres in the room's frame, y up; and the room's box); 'place' a marker ('name': pad, camera, camera_target, parked_N, gate, gate_out, light; a bar's camera, camera_target, camera_start, camera_start_target, visitor_N, light) at 'position' [x, y, z], or where a line from 'at' (a point inside the room, default its centre) 'toward' a side (down, up, left, right, front, back or [x, y, z]; default down: the floor) first meets the room; 'remove'. The room is rebuilt with it.",
                input = Input(("op", Enum("list", "place", "remove"), "what to do", true), ("name", "string", "the marker", false), ("position", NumArray3(), "place: exactly here (room metres)", false),
                              ("at", NumArray3(), "place: the line's start (room metres)", false), ("toward", new JObject { ["description"] = "a side name or [x, y, z]" }, "place: the line's direction", false),
                              ("capture", "boolean", "answer with a screenshot too", false)),
                run = RoomMarker, timeoutSeconds = 90f,
                ask = a => ((string)a["op"] ?? "list") == "list" ? null : $"{(string)a["op"]} the room marker {(string)a["name"]}",
            },
            new Tool
            {
                name = "press", title = "Press keys in a play test",
                description = "During a play test or test flight only: holds game actions (by their id, following the player's bindings: action (dock, autopilot, jump, mine), boost, brake, firePrimary, fireSecondary, autopilotMenu, actionsMenu, levelOut, fastForward, camera, cloak, switchSecondary...) and / or keys (enter, escape, space, f, q, 1-8 for a menu's entries, upArrow...) for 'seconds' (default a tap). Answers with a screenshot after 'wait' seconds (default 0.6).",
                input = Input(("actions", StrArray("game action ids"), "actions to hold", false), ("keys", StrArray("key names"), "keys to hold", false),
                              ("seconds", "number", "how long (default 0.15)", false), ("wait", "number", "seconds before the screenshot (default 0.6)", false),
                              ("capture", "boolean", "answer with a screenshot (default true)", false)),
                run = Press, timeoutSeconds = 90f, noAsk = true,
            },
            new Tool
            {
                name = "steer", title = "Fly in a play test",
                description = "During a play test or test flight only: the stick held at x (right +) / y (nose up +), -1..1, for 'seconds', optionally boosting, firing or changing the throttle (\"up\" / \"down\") meanwhile. Answers with a screenshot at the end. Approach things with the autopilot (press autopilotMenu, then a number) where you can.",
                input = Input(("x", "number", "-1..1", false), ("y", "number", "-1..1", false), ("seconds", "number", "how long (default 1)", false),
                              ("boost", "boolean", "boost meanwhile", false), ("fire", "boolean", "fire the primaries meanwhile", false),
                              ("throttle", Enum("up", "down"), "change the throttle meanwhile", false), ("capture", "boolean", "answer with a screenshot (default true)", false)),
                run = Steer, timeoutSeconds = 120f, noAsk = true,
            },
            new Tool
            {
                name = "ui_buttons", title = "The buttons on screen", readOnly = true,
                description = "The texts of every button shown now (the station menu, a conversation's Next, a lounge chat's answers, the hangar window, dialogs...), topmost panel first: what ui_click takes.",
                input = Input(), run = UiButtons,
            },
            new Tool
            {
                name = "ui_click", title = "Click a button in a play test",
                description = "During a play test or test flight only: presses the shown button with that text (exact, else containing it; 'index' among several), as a controller's confirm does. Answers with a screenshot after 'wait' seconds (default 0.8).",
                input = Input(("text", "string", "the button's text (ui_buttons lists them)", true), ("index", "integer", "which of several (default 0)", false),
                              ("wait", "number", "seconds before the screenshot (default 0.8)", false), ("capture", "boolean", "answer with a screenshot (default true)", false)),
                run = UiClick, timeoutSeconds = 60f, noAsk = true,
            },
            new Tool
            {
                name = "list_projects", title = "The Mod Builder's projects", readOnly = true,
                description = "Every project of the Mod Builder (id, name, version, what it holds), and which is open.",
                input = Input(), run = ListProjects,
            },
            new Tool
            {
                name = "create_project", title = "New project",
                description = "Makes a new, empty project (and opens it with 'open': true).",
                input = Input(("name", "string", "its name (the mod id follows it)", true), ("open", "boolean", "open it now", false)),
                run = CreateProject, timeoutSeconds = 120f, ask = a => $"make a new project \"{Str(a, "name")}\"",
            },
            new Tool
            {
                name = "open_project", title = "Open another project",
                description = "Saves the open project and opens another (its id, from list_projects); the builder reloads in its hangar.",
                input = Input(("id", "string", "the project's id", true)), run = OpenProject, timeoutSeconds = 120f,
                ask = a => $"open the project \"{Str(a, "id")}\"",
            },
            new Tool
            {
                name = "export_mod", title = "Export as mod",
                description = "Zips the open project into the player's Mods folder as a mod (replacing an installed mod of its id asks first, like the projects page). The player turns it on in the Mod Browser.",
                input = Input(), run = ExportMod, timeoutSeconds = 120f, permitsItself = true,
            },
        };

        // ---- the screen the AI acts on ------------------------------------------------------------------------------

        /// <summary>The builder showing kind 'k' (it goes there first, a scene load when it is edited elsewhere); 'then' once it
        /// has settled.</summary>
        static void WithKind(Call c, ModBuilder.Kind k, Action<ModBuilderScreen> then)
        {
            var screen = Screen(out string why);
            if (screen == null) { c.Fail(why); return; }
            if (screen.CurrentKind != k) screen.ExternalSelect(k);
            int frame = Time.frameCount;
            c.Wait(() => ModBuilderScreen.Open != null && ModBuilderScreen.Open.Alive && ModBuilderScreen.Open.CurrentKind == k
                         && !ModBuilderScreen.Open.ReloadBusy && Time.frameCount > frame + 2,
                   () => then(ModBuilderScreen.Open));
        }

        /// <summary>Answers with a screenshot once the camera has settled ('clean': the panels hidden for it), or with text.</summary>
        static void Answer(Call c, bool capture, bool clean, string text, JObject extra = null)
        {
            if (!capture) { if (extra != null) { extra["done"] = text; c.Json(extra, text); } else c.Text(text); return; }
            int frame = Time.frameCount;
            c.Wait(() => (ModBuilderScreen.Open == null || ModBuilderScreen.Open.CameraSettled) && Time.frameCount > frame + 3, () =>
            {
                var screen = ModBuilderScreen.Open;
                if (clean) screen?.ExternalCleanView(true);
                // The panels' opacity eases over the screens' USS transitions: a moment for them to be gone.
                int f2 = Time.frameCount;
                float t2 = Time.realtimeSinceStartup;
                c.Wait(() => Time.frameCount > f2 + 1 && (!clean || Time.realtimeSinceStartup - t2 > 0.45f), () =>
                {
                    var pump = ModBuilderMcpPump.Instance;
                    if (pump == null) { screen?.ExternalCleanView(false); c.Fail("Not ready."); return; }
                    pump.Capture(1280, (png, w, h) =>
                    {
                        if (clean && screen != null && screen.Alive) screen.ExternalCleanView(false);
                        c.Image(png, text + (extra != null ? "\n" + extra.ToString(Newtonsoft.Json.Formatting.None) : ""));
                    });
                });
            });
        }

        static bool Flag(JObject a, string key, bool fallback) => a[key] is JValue v && v.Type == JTokenType.Boolean ? (bool)v : fallback;

        static float Num(JObject a, string key, float fallback) =>
            a[key] is JValue v && (v.Type == JTokenType.Float || v.Type == JTokenType.Integer) ? (float)v : fallback;

        // ---- view_model -------------------------------------------------------------------------------------------

        /// <summary>A model reference as a prefab to draw (and a ship's hardpoints); a .glb path as 'glbPath' (loaded by the
        /// caller). Null and 'error' when there is none.</summary>
        static GameObject ResolveModel(string model, out List<WeaponMount> mounts, out string label, out string glbPath, out string error)
        {
            mounts = null; label = model; glbPath = null; error = null;
            var db = Database.Shared;
            model = (model ?? "").Trim();
            if (GameModels.Is(model, out string asmName))
            {
                var entry = GameModels.Find(db, asmName);
                if (entry == null) { error = $"No game model \"{asmName}\" (the names are in reference.md's \"Models and their textures\")."; return null; }
                return AssembledObject.LoadPrefab(entry);
            }
            if (model.StartsWith("ship:", StringComparison.OrdinalIgnoreCase))
            {
                string r = model.Substring(5).Trim();
                int index;
                if (int.TryParse(r, out index))
                {
                    var own = Project?.Ships.FirstOrDefault(s => s.IsOverride && s.OverrideIndex == index);
                    mounts = own != null ? own.Mounts : db.WeaponMounts.Find(w => w.ship == index)?.mounts;
                    label = GameNames.Ship(index);
                }
                else
                {
                    string reference = r.StartsWith("override:") || r.StartsWith("id:") ? r : "id:" + r;
                    var s = Project?.Ships.FirstOrDefault(x => x.Ref == reference);
                    if (s == null) { error = $"No ship \"{r}\" in the project."; return null; }
                    index = ModBuilder.IndexOf(s);
                    if (index < 0) { error = "The game hasn't read this ship yet: validate (and fix its errors) first."; return null; }
                    mounts = s.Mounts;
                    label = string.IsNullOrEmpty(s.Name) ? r : s.Name;
                }
                var entry = db.ShipAssembly(index);
                if (entry == null) { error = $"Ship {index} has no model."; return null; }
                if (entry.pack == ModShips.Pack && !ModShips.Built.Any(b => b.ship == index)) { error = "Its model is still loading: try again in a moment."; return null; }
                return AssembledObject.LoadPrefab(entry);
            }
            if (model.StartsWith("station:", StringComparison.OrdinalIgnoreCase))
            {
                string r = model.Substring(8).Trim();
                int n = int.TryParse(r, out int k) ? k : ModBuilder.StationNumber(Project != null ? Project.Key(r.StartsWith("id:") ? r.Substring(3) : r) : r);
                if (n < 0 && r.StartsWith("override:")) n = ModBuilder.StationNumber(r.Substring(9));
                if (n < 0 || n >= db.Stations.Count) { error = $"No station \"{r}\" the game has read (validate first)."; return null; }
                string name = OrbitBuilder.StationAssembly(db, OrbitLayout.Build(db, n));
                var entry = name != null ? db.AssemblyByName(name) : null;
                if (entry == null) { error = $"Station {n} has no model."; return null; }
                label = db.Stations[n].name;
                return AssembledObject.LoadPrefab(entry);
            }
            if (model.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
            {
                glbPath = ProjectPath(model, false, out error);
                if (glbPath != null && !File.Exists(glbPath)) { error = "No such file in the project."; glbPath = null; }
                return null;
            }
            error = "model: \"gof2:<assembly>\", \"ship:<id | override:N | number>\", \"station:<id | number>\" or a project path \"models/x.glb\".";
            return null;
        }

        static List<ModelViewer.Marker> Markers(List<WeaponMount> mounts)
        {
            if (mounts == null) return null;
            var list = new List<ModelViewer.Marker>();
            var counts = new int[4];
            string[] letters = { "P", "S", "T", "E" };
            foreach (var m in mounts)
            {
                int type = Mathf.Clamp(m.slotType, 0, 3);
                list.Add(new ModelViewer.Marker { local = Flight.WeaponSystem.MountToLocal(m), type = type, label = letters[type] + ++counts[type] });
            }
            return list;
        }

        static void ViewModel(JObject a, Call c)
        {
            var prefab = ResolveModel(Str(a, "model"), out var mounts, out string label, out string glb, out string error);
            if (prefab == null && glb == null) { c.Fail(error); return; }
            var o = new ModelViewer.Options
            {
                angles = (a["angles"] as JArray)?.Select(t => (string)t).Where(s => !string.IsNullOrEmpty(s)).ToArray(),
                tile = Int(a, "size", 512),
                background = Str(a, "background") ?? "grey",
                glow = Flag(a, "glow", false),
                markers = Flag(a, "hardpoints", true) ? Markers(mounts) : null,
            };
            void Draw(GameObject source, bool instance)
            {
                var tex = ModelViewer.Render(source, o, instance, out string info, out string err);
                if (tex == null) { c.Fail(err); return; }
                var png = tex.EncodeToPNG();
                Object.Destroy(tex);
                c.Image(png, $"{label}: {info}" + (o.markers != null ? $"; {o.markers.Count} hardpoints" : ""));
            }
            if (prefab != null) { Draw(prefab, false); return; }
            // A project .glb: loaded with glTFast as the mods are, drawn, thrown away.
            byte[] bytes = File.ReadAllBytes(glb);
            var holder = new GameObject("view_model glb");
            holder.SetActive(false);
            var load = ModGltf.Load(ModManager.BuilderProject, bytes, new GLTFast.UninterruptedDeferAgent());
            System.Threading.Tasks.Task<bool> place = null;
            c.Wait(() =>
            {
                if (!load.IsCompleted) return false;
                if (place == null)
                {
                    if (load.IsFaulted || load.Result == null) return true;
                    place = ModGltf.InstantiateMain(load.Result, holder.transform);
                }
                return place.IsCompleted;
            }, () =>
            {
                try
                {
                    if (load.IsFaulted || load.Result == null || place == null || place.IsFaulted || !place.Result) { c.Fail("The .glb couldn't be read: " + (load.Exception?.InnerException?.Message ?? "not a usable glTF")); return; }
                    holder.SetActive(true);
                    Draw(holder, true);
                }
                finally
                {
                    Object.Destroy(holder);
                    if (load.IsCompletedSuccessfully) load.Result?.Dispose();
                }
            });
        }

        // ---- textures ---------------------------------------------------------------------------------------------

        static readonly (string slot, string role)[] TextureSlots =
        {
            ("_BaseMap", "colour"), ("_MainTex", "colour"), ("_BumpMap", "normal map"), ("_MetallicGlossMap", "metallic / smoothness"), ("_EmissionMap", "glow"),
        };

        /// <summary>The textures a prefab's materials draw: (texture, role, normal).</summary>
        static List<(Texture tex, string role, bool normal)> TexturesOf(GameObject prefab)
        {
            var list = new List<(Texture, string, bool)>();
            if (prefab == null) return list;
            var seen = new HashSet<Texture>();
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null) continue;
                    bool additive = m.shader.name.Contains("Additive");
                    foreach (var (slot, role) in TextureSlots)
                    {
                        if (!m.HasProperty(slot) || !(m.GetTexture(slot) is Texture t) || string.IsNullOrEmpty(t.name) || !seen.Add(t)) continue;
                        list.Add((t, additive ? "lights / glow (additive)" : role, slot == "_BumpMap"));
                    }
                }
            return list;
        }

        /// <summary>A game texture by its name: of 'model' when given, else among what is loaded, the sky / planet materials,
        /// and the models whose names start its name.</summary>
        static Texture FindGameTexture(string name, string model, out bool normal, out string error)
        {
            normal = name.Contains("_normal");
            error = null;
            if (!string.IsNullOrEmpty(model))
            {
                var prefab = ResolveModel(model, out _, out _, out _, out error);
                if (prefab == null) return null;
                var hit = TexturesOf(prefab).FirstOrDefault(t => t.tex.name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (hit.tex != null) { normal = hit.normal; return hit.tex; }
                error = $"{model} doesn't draw a texture \"{name}\" (list_textures lists them).";
                return null;
            }
            var loaded = Resources.FindObjectsOfTypeAll<Texture2D>().FirstOrDefault(t => t.name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (loaded != null) return loaded;
            var backdrop = Resources.Load<Material>("GoF2Backdrop/" + name);
            if (backdrop != null && backdrop.mainTexture != null) return backdrop.mainTexture;
            var db = Database.Shared;
            foreach (var entry in db.Assemblies.Where(x => x.pack != ModContent.Pack && name.StartsWith(x.name, StringComparison.OrdinalIgnoreCase))
                                               .OrderByDescending(x => x.name.Length).Take(6))
            {
                var hit = TexturesOf(AssembledObject.LoadPrefab(entry)).FirstOrDefault(t => t.tex.name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (hit.tex != null) { normal = hit.normal; return hit.tex; }
            }
            error = $"No game texture \"{name}\" found: give 'model' (a model that uses it, see reference.md's \"Models and their textures\").";
            return null;
        }

        static void ViewTexture(JObject a, Call c)
        {
            string name = Str(a, "texture");
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("texture: a project path or a game texture's name.");
            int max = Mathf.Clamp(Int(a, "maxSize", 1024), 32, 4096);
            string alphaMode = Str(a, "alpha") ?? "checker";
            Texture src;
            bool normal, linear, own = false;
            string label;
            Texture2D decoded = null;
            if (name.Contains("/") || name.Contains("."))
            {
                string full = ProjectPath(name, false, out string why);
                if (full == null) { c.Fail(why); return; }
                if (!File.Exists(full)) { c.Fail("No such file in the project."); return; }
                normal = name.ToLowerInvariant().Contains("normal");
                linear = normal || name.ToLowerInvariant().Contains("_metallic");
                decoded = ImageImport.Decode(File.ReadAllBytes(full), Path.GetFileName(full), linear, false, out string err);
                if (decoded == null) { c.Fail("The image couldn't be read: " + err); return; }
                src = decoded;
                own = true;
                label = name;
            }
            else
            {
                src = FindGameTexture(name, Str(a, "model"), out normal, out string err);
                if (src == null) { c.Fail(err); return; }
                linear = normal || name.Contains("_metallic") || name.Contains("_normal");
                label = src.name;
            }
            try
            {
                float s = Mathf.Min(1f, (float)max / Mathf.Max(src.width, src.height));
                int w = Mathf.Max(1, Mathf.RoundToInt(src.width * s)), h = Mathf.Max(1, Mathf.RoundToInt(src.height * s));
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
                var prev = RenderTexture.active;
                var copy = new Texture2D(w, h, TextureFormat.RGBA32, false, linear);
                try
                {
                    Graphics.Blit(src, rt);
                    RenderTexture.active = rt;
                    copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    copy.Apply();
                }
                finally { RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); }
                if (normal && !own) ModBuilderView.DecodeNormals(copy, (src as Texture2D)?.format ?? TextureFormat.RGBA32);
                var px = copy.GetPixels32();
                bool hasAlpha = px.Any(p => p.a < 250);
                if (normal) alphaMode = "ignore";
                for (int i = 0; i < px.Length; i++)
                {
                    var p = px[i];
                    switch (alphaMode)
                    {
                        case "mask": px[i] = new Color32(p.a, p.a, p.a, 255); break;
                        case "ignore": px[i] = new Color32(p.r, p.g, p.b, 255); break;
                        case "checker":
                        {
                            int x = i % w, y = i / w;
                            byte bg = ((x / 16 + y / 16) & 1) == 0 ? (byte)200 : (byte)140;
                            float al = p.a / 255f;
                            px[i] = new Color32((byte)(p.r * al + bg * (1 - al)), (byte)(p.g * al + bg * (1 - al)), (byte)(p.b * al + bg * (1 - al)), 255);
                            break;
                        }
                    }
                }
                copy.SetPixels32(px);
                copy.Apply();
                var png = copy.EncodeToPNG();
                Object.Destroy(copy);
                string format = src is Texture2D t2 ? t2.format.ToString() : src.GetType().Name;
                c.Image(png, $"{label}: {src.width} x {src.height} ({format}{(normal ? ", normal map shown as RGB" : "")}{(hasAlpha ? ", has alpha shown as " + alphaMode : "")}), sent at {w} x {h}");
            }
            finally { if (decoded != null) Object.Destroy(decoded); }
        }

        static void ListTextures(JObject a, Call c)
        {
            var prefab = ResolveModel(Str(a, "model"), out _, out string label, out string glb, out string error);
            if (glb != null) { c.Fail("A .glb's own textures are in the file: view_model draws it."); return; }
            if (prefab == null) { c.Fail(error); return; }
            var list = new JArray();
            foreach (var (tex, role, normal) in TexturesOf(prefab))
            {
                var o = new JObject { ["name"] = tex.name, ["role"] = role, ["size"] = $"{tex.width} x {tex.height}" };
                var mine = Project == null ? null : Directory.Exists(Path.Combine(Project.Folder, "textures"))
                    ? Directory.GetFiles(Path.Combine(Project.Folder, "textures"), tex.name + ".*", SearchOption.AllDirectories).FirstOrDefault() : null;
                if (mine != null) o["replacedBy"] = mine.Substring(Project.Folder.Length).TrimStart('/', '\\').Replace('\\', '/');
                list.Add(o);
            }
            c.Json(new JObject { ["model"] = label, ["textures"] = list }, $"{label}: {list.Count} textures");
        }

        // ---- the builder's screen --------------------------------------------------------------------------------------

        static ModBuilder.Kind KindOfSelect(string k) => k switch
        {
            "ships" => ModBuilder.Kind.Ships, "items" => ModBuilder.Kind.Items, "characters" => ModBuilder.Kind.Characters,
            "skins" => ModBuilder.Kind.Skins, "blueprints" => ModBuilder.Kind.Blueprints, "kits" => ModBuilder.Kind.Kits, "gameOptions" => ModBuilder.Kind.Campaigns,
            "campaign" => ModBuilder.Kind.Campaigns, "interiors" => ModBuilder.Kind.Interiors, "missions" => ModBuilder.Kind.Quests,
            "systems" => ModBuilder.Kind.Systems, "races" => ModBuilder.Kind.Races, "stations" => ModBuilder.Kind.Stations, "weaponFx" => ModBuilder.Kind.WeaponFx,
            "weaponEffects" => ModBuilder.Kind.WeaponFx,
            _ => throw new ArgumentException("kind: one of " + string.Join(", ", SelectKinds)),
        };

        static void SelectEntry(JObject a, Call c)
        {
            string k = Str(a, "kind"), id = Str(a, "id");
            var show = KindOfSelect(k);
            WithKind(c, show, screen =>
            {
                string err = screen.ExternalSelectEntry(k == "weaponFx" ? "items" : k, id, a["options"] as JObject);
                if (err != null) { c.Fail(err); return; }
                // A station in another orbit loads its scene: answered once the screen is back.
                int frame = Time.frameCount;
                c.Wait(() => ModBuilderScreen.Open != null && ModBuilderScreen.Open.Alive && !ModBuilderScreen.Open.ReloadBusy && Time.frameCount > frame + 2,
                       () => Answer(c, Flag(a, "capture", true), Flag(a, "clean", false), $"{k} {id} shown", ModBuilderScreen.Open.ExternalSelection()));
            });
        }

        static void View(JObject a, Call c)
        {
            var screen = Screen(out string why);
            if (screen == null) { c.Fail(why); return; }
            bool reset = Flag(a, "reset", false);
            string angle = Str(a, "angle"), map = Str(a, "map");
            if (map != null)
            {
                string mapErr = screen.ExternalMap(map);
                if (mapErr != null) { c.Fail(mapErr); return; }
                float t0 = Time.realtimeSinceStartup;
                c.Wait(() => Time.realtimeSinceStartup - t0 > 2.5f, () => Answer(c, Flag(a, "capture", true), Flag(a, "clean", false), "the map: " + map));   // the map's glide
                return;
            }
            if (!reset && angle == null) throw new ArgumentException("angle (or reset, or map).");
            string err = screen.ExternalCamera(angle, Num(a, "zoom", 0f), reset);
            if (err != null) { c.Fail(err); return; }
            Answer(c, Flag(a, "capture", true), Flag(a, "clean", false), reset ? "the default view" : $"from the {angle}");
        }

        static void Hardpoint(JObject a, Call c)
        {
            var screen = Screen(out string why);
            if (screen == null) { c.Fail(why); return; }
            string err = screen.ExternalHardpoint(a, out var done);
            if (err != null) { c.Fail(err); return; }
            if (((string)a["op"] ?? "list") != "list") screen.ExternalSaveNow();
            c.Json(done, $"hardpoint {(string)a["op"] ?? "list"}");
        }

        static void Engines(JObject a, Call c)
        {
            var screen = Screen(out string why);
            if (screen == null) { c.Fail(why); return; }
            string err = screen.ExternalEngines(Num(a, "level", 0f));
            if (err != null) c.Fail(err); else c.Text($"Engines at {Num(a, "level", 0f):0.##}.");
        }

        static void TestFly(JObject a, Call c)
        {
            var screen = Screen(out string why);
            if (screen == null) { c.Fail(why); return; }
            string err = screen.ExternalTestFly();
            if (err != null) { c.Fail(err); return; }
            c.Wait(() => ModBuilder.TestFlying && SceneManager.GetActiveScene().name == "Space" && SceneManager.GetActiveScene().isLoaded,
                   () => { int f = Time.frameCount; c.Wait(() => Time.frameCount > f + 30, () => c.Json(Status(), "test flight")); });
        }

        static void EndTestFly(JObject a, Call c)
        {
            if (!ModBuilder.TestFlying) { c.Fail("No test flight is running."); return; }
            ModBuilder.EndTestFlight(false);
            c.Wait(() => !ModBuilder.TestFlying && ModBuilderScreen.Open != null && ModBuilderScreen.Open.Alive, () => c.Json(Status(), "back in the builder"));
        }

        static void WeaponRange(JObject a, Call c)
        {
            string id = Str(a, "item");
            WithKind(c, ModBuilder.Kind.WeaponFx, screen =>
            {
                // "effect:<id>": one of the project's weapon effects, fired on its base weapon.
                bool effect = id != null && id.StartsWith("effect:");
                string err = screen.ExternalSelectEntry(effect ? "weaponEffects" : "items", effect ? id.Substring(7) : id, null) ?? screen.ExternalRange(Flag(a, "fire", true));
                if (err != null) { c.Fail(err); return; }
                int f = Time.frameCount;
                c.Wait(() => Time.frameCount > f + 45, () => Answer(c, Flag(a, "capture", true), false, Flag(a, "fire", true) ? $"{id} firing on the range" : "the range paused"));
            });
        }

        static void SwitchHangar(JObject a, Call c)
        {
            WithKind(c, ModBuilder.Kind.Ships, screen =>
            {
                string err = screen.ExternalHangar(Str(a, "which"), out string done);
                if (err != null) { c.Fail(err); return; }
                int f = Time.frameCount;
                c.Wait(() => Time.frameCount > f + 2 && ModBuilderScreen.Open != null && ModBuilderScreen.Open.Alive && !ModBuilderScreen.Open.ReloadBusy, () => c.Text(done));
            });
        }

        delegate string ScreenOp(ModBuilderScreen s, out JObject done);

        /// <summary>A screen hook that changes the project: run, saved, its answer as JSON.</summary>
        static void ScreenEdit(Call c, JObject a, ScreenOp op, string label)
        {
            var screen = Screen(out string why);
            if (screen == null) { c.Fail(why); return; }
            string err = op(screen, out var done);
            if (err != null) { c.Fail(err); return; }
            if (((string)a["op"] ?? "list") != "list") screen.ExternalSaveNow();
            c.Json(done, $"{label} {(string)a["op"] ?? "list"}");
        }

        static void RoomMarker(JObject a, Call c)
        {
            var screen = Screen(out string why);
            if (screen == null) { c.Fail(why); return; }
            string err = screen.ExternalRoomMarker(a, out var done);
            if (err != null) { c.Fail(err); return; }
            if (((string)a["op"] ?? "list") == "list") { c.Json(done, "room markers"); return; }
            screen.ExternalSaveNow();
            // The room is rebuilt with the marker (a scene reload): answered once it is back.
            int f = Time.frameCount;
            c.Wait(() => Time.frameCount > f + 3 && ModBuilderScreen.Open != null && ModBuilderScreen.Open.Alive && !ModBuilderScreen.Open.ReloadBusy,
                   () => Answer(c, Flag(a, "capture", false), false, $"room marker {(string)a["name"]} {(string)a["op"]}", done));
        }

        // ---- playing a play test -------------------------------------------------------------------------------------

        static bool Playing(Call c)
        {
            if (ModBuilder.PlayTesting || ModBuilder.TestFlying) return true;
            c.Fail("Only during a play test or a test flight (play_test / test_fly first); the builder's own controls are its tools.");
            return false;
        }

        /// <summary>Answers once nothing is held any more and 'wait' seconds have passed: a screenshot, or the status.</summary>
        static void AfterInput(Call c, JObject a, float wait, string text)
        {
            float t0 = -1f;
            c.Wait(() =>
            {
                if (McpInput.Busy) return false;
                if (t0 < 0f) t0 = Time.realtimeSinceStartup;
                return Time.realtimeSinceStartup - t0 >= wait;
            }, () =>
            {
                if (!Flag(a, "capture", true)) { c.Json(Status(), text); return; }
                var pump = ModBuilderMcpPump.Instance;
                if (pump == null) { c.Text(text); return; }
                pump.Capture(1280, (png, w, h) => c.Image(png, text + $" ({SceneManager.GetActiveScene().name})"));
            });
        }

        static void Press(JObject a, Call c)
        {
            if (!Playing(c)) return;
            float seconds = Mathf.Clamp(Num(a, "seconds", 0.15f), 0.05f, 30f);
            var done = new List<string>();
            foreach (var t in (a["actions"] as JArray) ?? new JArray())
            {
                string err = McpInput.HoldAction((string)t, seconds);
                if (err != null) { c.Fail(err); return; }
                done.Add((string)t);
            }
            foreach (var t in (a["keys"] as JArray) ?? new JArray())
            {
                if (!McpInput.TryKey((string)t, out var key)) { c.Fail($"No key \"{t}\" (names like enter, escape, space, f, 1, upArrow, f12)."); return; }
                McpInput.HoldKey(key, seconds);
                done.Add((string)t);
            }
            if (done.Count == 0) { c.Fail("Give 'actions' or 'keys'."); return; }
            AfterInput(c, a, Mathf.Clamp(Num(a, "wait", 0.6f), 0f, 20f), "pressed " + string.Join(" + ", done));
        }

        static void Steer(JObject a, Call c)
        {
            if (!Playing(c)) return;
            float seconds = Mathf.Clamp(Num(a, "seconds", 1f), 0.05f, 60f);
            McpInput.Stick(new Vector2(Mathf.Clamp(Num(a, "x", 0f), -1f, 1f), Mathf.Clamp(Num(a, "y", 0f), -1f, 1f)), seconds);
            if (Flag(a, "boost", false)) { string e = McpInput.HoldAction("boost", seconds); if (e != null) { c.Fail(e); return; } }
            if (Flag(a, "fire", false)) { string e = McpInput.HoldAction("firePrimary", seconds); if (e != null) { c.Fail(e); return; } }
            string th = Str(a, "throttle");
            if (th == "up" || th == "down")
            {
                var row = Flight.GameControls.Rows.FirstOrDefault(r => r.id == "throttle");
                var paths = row != null ? Flight.GameControls.Paths(row, Flight.BindSlot.Key1) : Array.Empty<string>();
                string path = paths.Length >= 2 ? paths[th == "up" ? 0 : 1] : null;
                if (path != null && path.StartsWith("<Keyboard>/") && McpInput.TryKey(path.Substring(11), out var k)) McpInput.HoldKey(k, seconds);
            }
            AfterInput(c, a, 0.1f, $"steered x {Num(a, "x", 0f):0.##}, y {Num(a, "y", 0f):0.##} for {seconds:0.#} s");
        }

        static void UiButtons(JObject a, Call c)
        {
            var list = new JArray();
            foreach (var (_, text) in McpInput.Buttons()) list.Add(text);
            c.Json(new JObject { ["scene"] = SceneManager.GetActiveScene().name, ["buttons"] = list }, $"{list.Count} buttons");
        }

        static void UiClick(JObject a, Call c)
        {
            if (!Playing(c)) return;
            string err = McpInput.Click(Str(a, "text"), Int(a, "index", 0), out string pressed);
            if (err != null) { c.Fail(err); return; }
            AfterInput(c, a, Mathf.Clamp(Num(a, "wait", 0.8f), 0f, 20f), "clicked " + pressed);
        }

        // ---- projects -------------------------------------------------------------------------------------------------

        static void ListProjects(JObject a, Call c)
        {
            var list = new JArray();
            foreach (var p in ModProject.List())
                list.Add(new JObject
                {
                    ["id"] = p.Id, ["name"] = p.Name, ["version"] = p.Version, ["open"] = Project != null && p.Folder == Project.Folder,
                    ["ships"] = p.Ships.Count, ["items"] = p.Items.Count, ["missions"] = p.Quests.Count, ["error"] = p.LoadError,
                });
            c.Json(new JObject { ["projects"] = list }, $"{list.Count} projects");
        }

        static void CreateProject(JObject a, Call c)
        {
            string name = Str(a, "name")?.Trim();
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name: the project's name.");
            ModProject p;
            try { p = ModProject.Create(name, Multiplayer.NetGame.PlayerName); }
            catch (Exception e) { c.Fail("The project couldn't be made: " + e.Message); return; }
            if (!Flag(a, "open", false)) { c.Json(new JObject { ["id"] = p.Id, ["name"] = p.Name, ["folder"] = p.Folder }, "made " + p.Name); return; }
            SwitchTo(c, p);
        }

        static void OpenProject(JObject a, Call c)
        {
            string id = Str(a, "id");
            var p = ModProject.List().FirstOrDefault(x => x.Id == id);
            if (p == null) { c.Fail($"No project \"{id}\" (list_projects lists them)."); return; }
            if (p.LoadError != null) { c.Fail("That project can't be read: " + p.LoadError); return; }
            if (Project != null && p.Folder == Project.Folder) { c.Text("It is open already."); return; }
            SwitchTo(c, p);
        }

        /// <summary>The open project saved, 'p' opened in its place: the builder's game made again for it, its mods loaded,
        /// then its hangar (the builder stays open throughout, so the server keeps running).</summary>
        static void SwitchTo(Call c, ModProject p)
        {
            if (ModBuilder.PlayTesting) { c.Fail("A play test is running: end_play_test first."); return; }
            if (ModBuilder.TestFlying) { c.Fail("A test flight is running: end_test_fly first."); return; }
            ModBuilderScreen.Open?.ExternalSaveNow();
            Project?.Save();
            ModBuilder.OpenKind = ModBuilder.Kind.Ships;
            ModBuilder.Begin(p);
            int f = Time.frameCount;
            float t0 = Time.realtimeSinceStartup;
            c.Wait(() => Time.frameCount > f + 2 && (!ModLoading.Busy || Time.realtimeSinceStartup - t0 > 60f), () =>
            {
                SceneManager.LoadScene("Station");
                c.Wait(() => ModBuilderScreen.Open != null && ModBuilderScreen.Open.Alive && ModBuilder.Project == p,
                       () => c.Json(Status(), "opened " + p.Name));
            });
        }

        static void ExportMod(JObject a, Call c)
        {
            if (Project == null) { c.Fail("The Mod Builder isn't open."); return; }
            ModBuilderScreen.Open?.ExternalSaveNow();
            Project.Save();
            var old = Project.InstalledMod;
            string what = old != null ? $"export the project, replacing the installed mod \"{old.Name}\" v{old.Version}" : "export the project into the Mods folder";
            Permit(c, what, old != null, () =>
            {
                string path = Project.Export(out string error);
                if (path == null) c.Fail("The mod couldn't be exported: " + error);
                else c.Json(new JObject { ["exported"] = path, ["id"] = Project.Id, ["note"] = "The player turns it on in the Mod Browser (main menu > Mods)." }, "exported " + Project.Name);
            });
        }
    }
}
