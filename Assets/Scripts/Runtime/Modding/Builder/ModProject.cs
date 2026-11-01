// ModProject.cs
// Remake-only: a Mod Builder project (ModBuilder). A project is a mod's folder under Application.persistentDataPath/
// ModProjects/<id>, written in the mods' own format (mod.json, ships.json, models/*.glb; Modding/README.md), so the game
// reads it like any installed mod while it is being built (ModManager.SetBuilderProject) and Export only zips it into
// the Mods folder. Beside the mod's files it keeps builder.json (never exported): each ship's test-flight loadout, the
// test cheats and the ship last worked on.
// The ships are ProjectShip: their ships.json entry as a JObject (fields the builder doesn't know, from a hand edit, are
// kept), with typed accessors for what the builder edits and the hardpoints as WeaponMounts. Items (items.json) and
// characters (characters.json) are ProjectItem / ProjectCharacter (ProjectContent.cs) the same way; their files are written
// only once the project has some (or had the file). So are the systems / stations / rooms (ProjectWorld.cs), blueprints,
// new-game options and the campaign (ProjectGame.cs) and the quests made in the builder (events/<name>.gof2quest,
// ProjectQuest). Remake: the mod's own races, races.json (ProjectRace, ProjectWorld.cs; an older project's factions.json is
// read and written back as races.json) the same way; adding and removing them, with what names them, in ModProjectRaces.cs.
// Its audio is plain files: music/, sounds/ and voices/ (AudioFiles). What other mods it uses is its
// manifest's "dependencies" (AddDependency: ModRefs adds one when another mod's content is picked).
// A project can also start from a mod made elsewhere (an installed one, a downloaded .zip): CopyMod copies its files as
// they are and keeps its id, so Export replaces the installed copy; what the builder doesn't edit stays as it was.
// Plain C#.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using GoF2Remake.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public partial class ModProject
    {
        public const string BuilderFile = "builder.json";
        public const string ModelsFolder = "models";

        /// <summary>Where the projects live (created on first use).</summary>
        public static string Root => Path.Combine(GoF2Remake.GamePaths.Data, "ModProjects");

        public string Folder { get; private set; }
        public string Id { get; private set; }
        public string Name { get => Str(manifest, "name") ?? Id; set => manifest["name"] = value; }
        public string Description { get => Str(manifest, "description") ?? ""; set => manifest["description"] = value; }
        public string Author { get => Str(manifest, "author") ?? ""; set => manifest["author"] = value; }
        public string Version { get => Str(manifest, "version") ?? "1.0"; set => manifest["version"] = value; }
        public string Website { get => Str(manifest, "website") ?? ""; set { if (string.IsNullOrWhiteSpace(value)) manifest.Remove("website"); else manifest["website"] = value.Trim(); } }
        /// <summary>mod.json "credits": who made what (the Mods screen shows it).</summary>
        public string Credits { get => Str(manifest, "credits") ?? ""; set { if (string.IsNullOrWhiteSpace(value)) manifest.Remove("credits"); else manifest["credits"] = value; } }
        /// <summary>The Mods screen's picture (mod.json "preview", default preview.png): its path in the project, or null.</summary>
        public string PreviewImage
        {
            get
            {
                string p = Str(manifest, "preview") ?? "preview.png";
                return File.Exists(Path.Combine(Folder, p)) ? p : null;
            }
        }

        public void SetPreviewImage(string sourcePng)
        {
            File.Copy(sourcePng, Path.Combine(Folder, "preview.png"), true);
            manifest.Remove("preview");
        }

        // ---- translations (text/<lang>.json) ------------------------------------------------------------------

        public const string TextFolder = "text";
        public static readonly string[] TextLanguages = { "de", "fr", "es", "it", "nl", "pl", "ru", "pt", "zh", "hi", "ja", "ko", "ar" };

        public DateTime Modified { get; private set; }

        public readonly List<ProjectShip> Ships = new List<ProjectShip>();
        public readonly List<ProjectItem> Items = new List<ProjectItem>();
        public readonly List<ProjectCharacter> Characters = new List<ProjectCharacter>();
        public readonly List<ProjectSystem> Systems = new List<ProjectSystem>();
        public readonly List<ProjectStation> Stations = new List<ProjectStation>();
        public readonly List<ProjectInterior> Interiors = new List<ProjectInterior>();
        /// <summary>races.json: the mod's own races (ModRaces).</summary>
        public readonly List<ProjectRace> Races = new List<ProjectRace>();
        public readonly List<ProjectBlueprint> Blueprints = new List<ProjectBlueprint>();
        /// <summary>shipkits.json: the customizable ships' kits of parts (ModShipKits).</summary>
        public readonly List<ProjectKit> Kits = new List<ProjectKit>();
        /// <summary>weaponfx.json: the mod's own weapon effects (ModWeapons).</summary>
        public readonly List<ProjectWeaponEffect> WeaponEffects = new List<ProjectWeaponEffect>();
        public readonly List<ProjectGameOption> GameOptions = new List<ProjectGameOption>();
        public readonly List<ProjectQuest> Quests = new List<ProjectQuest>();
        /// <summary>campaign.json, null = the mod has no campaign.</summary>
        public ProjectCampaign Campaign;
        public const string CampaignFile = "campaign.json", InteriorsFile = "interiors.json";
        JObject manifest = new JObject();
        JObject builder = new JObject();
        /// <summary>A content file couldn't be read (a hand edit gone wrong): shown, and the project isn't saved over it.</summary>
        public string LoadError { get; private set; }

        // ---- undo / redo (ModProjectHistory) ----------------------------------------------------------------------

        /// <summary>builder.json keys an undo never takes back: where the builder sits (its hangar: switching reloads the
        /// scene), the ship last worked on, the ids this project had (a rename is done on the projects page).</summary>
        static readonly string[] KeptBuilderKeys = { "station", "lastShip", "previousIds" };

        // ---- ships ------------------------------------------------------------------------------------------------

        /// <summary>Paths AddFile handed out this run: never again, even once deleted (ModMaterials keeps a texture by its
        /// path until the project is read again, so a reused name would show the old picture).</summary>
        static readonly HashSet<string> issued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ---- skins ---------------------------------------------------------------------------------------------

        /// <summary>Where the builder saves the game's textures to paint over (never exported).</summary>
        public const string TemplatesFolder = "templates";
        static readonly string[] ImageExtensions = ImageImport.FileExtensions;   // every format the game reads

        // ---- audio: plain files ---------------------------------------------------------------------------------

        public const string MusicFolder = "music", SoundsFolder = "sounds", VoicesFolder = "voices";
        public static readonly string[] AudioExtensions = { ".ogg", ".wav", ".mp3" };
    }

    /// <summary>One ships.json entry of a project: a new ship ("id") or an override of an original ship ("override").</summary>
    public class ProjectShip
    {
        readonly JObject json;
        /// <summary>The hardpoints (ships.json "mounts"): slotType 0 primary, 1 secondary, 2 turret, 3 engine exhaust.</summary>
        public readonly List<WeaponMount> Mounts = new List<WeaponMount>();
        /// <summary>A new ship whose default hardpoints haven't been placed yet (done once its model is built).</summary>
        public bool NeedsLayout;

        public ProjectShip(JObject o)
        {
            json = o;
            ReadMounts();
        }

        /// <summary>The hardpoints read again from the JSON (after Edit as JSON).</summary>
        public void ReadMounts()
        {
            Mounts.Clear();
            if (json["mounts"] is JArray a)
                foreach (var m in a.OfType<JObject>())
                {
                    try { Mounts.Add(JsonUtility.FromJson<WeaponMount>(m.ToString())); } catch { }
                }
        }

        /// <summary>The entry's JSON itself (Edit as JSON); the less common fields read and written raw.</summary>
        public JObject Json => json;
        public JToken Get(string k) => json[k];
        public void Set(string k, JToken v) { if (v == null || v.Type == JTokenType.Null) json.Remove(k); else json[k] = v; }

        public static WeaponMount Copy(WeaponMount m) => new WeaponMount
        {
            slotType = m.slotType, upsideDown = m.upsideDown,
            position_engine = m.position_engine != null ? (int[])m.position_engine.Clone() : new int[3],
            turretAngles = m.turretAngles != null ? (float[])m.turretAngles.Clone() : null,
            glowColor = m.glowColor != null ? (float[])m.glowColor.Clone() : null,
            glowSize = m.glowSize != null ? (float[])m.glowSize.Clone() : null, glowShape = m.glowShape,
            turretUp = m.turretUp != null ? (float[])m.turretUp.Clone() : null,
            outline = m.outline != null ? (float[])m.outline.Clone() : null, builtIn = m.builtIn,
            direction = m.direction != null ? (float[])m.direction.Clone() : null,
            rotation = m.rotation != null ? (float[])m.rotation.Clone() : null,
            nodes = m.nodes != null ? (string[])m.nodes.Clone() : null, fxScale = m.fxScale, socketScale = m.socketScale,
        };

        public bool IsOverride => json["override"] != null;
        public int OverrideIndex => IsOverride && int.TryParse(json["override"].ToString(), out int i) ? i : -1;
        public string LocalId => json["id"] is JValue v ? (string)v : null;
        /// <summary>The key builder.json files it under: "id:<id>" / "override:<n>".</summary>
        public string Ref => IsOverride ? "override:" + OverrideIndex : "id:" + LocalId;

        string S(string k) => json[k] is JValue v && v.Type == JTokenType.String ? (string)v : null;
        int I(string k, int d) => json[k] is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) ? Mathf.RoundToInt((float)v) : d;
        float F(string k, float d) => json[k] is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) ? (float)v : d;

        public string Name { get => S("name"); set { if (string.IsNullOrEmpty(value)) json.Remove("name"); else json["name"] = value; } }
        public string Description { get => S("description") ?? ""; set => json["description"] = value ?? ""; }
        public string Model { get => S("model"); set { if (string.IsNullOrEmpty(value)) json.Remove("model"); else json["model"] = value; } }
        /// <summary>The shop icon (a 180 x 88 PNG in the project), null = none.</summary>
        public string Icon { get => S("icon"); set { if (string.IsNullOrEmpty(value)) json.Remove("icon"); else json["icon"] = value; } }
        /// <summary>The game race that builds it (0-3, 8 pirate, 9 Void); -1 when a mod's race does (ModRace). Setting it
        /// replaces a mod race.</summary>
        public int Race
        {
            get => json["race"] is JValue v && v.Type == JTokenType.String ? ModRaces.GameRaceWord((string)v) : I("race", 0);
            set => json["race"] = value;
        }

        /// <summary>Remake: the mod race that builds it (ships.json "race" as a mod race's id or "mod_id:id", ModRaces), null =
        /// a game race. Setting null leaves "race" alone (pick a game race with Race).</summary>
        public string ModRace
        {
            get => json["race"] is JValue v && v.Type == JTokenType.String && ModRaces.GameRaceWord((string)v) < 0 ? (string)v : null;
            set { if (!string.IsNullOrEmpty(value)) json["race"] = value; }
        }

        /// <summary>Remake: "traffic" "races": the game and mod races whose NPC fighters fly it (numbers stay numbers, mod
        /// races and race words strings), empty = its own race. The rest of "traffic" (weight, boss, capital...) stays.</summary>
        public List<string> TrafficRaces
        {
            get => json["traffic"] is JObject t && t["races"] is JArray a ? a.Select(ProjectEntry.RefText).Where(x => x != null).ToList() : new List<string>();
            set
            {
                if (value == null || value.Count == 0)
                {
                    if (json["traffic"] is JObject old) { old.Remove("races"); if (!old.HasValues) json.Remove("traffic"); }
                    return;
                }
                if (!(json["traffic"] is JObject t)) json["traffic"] = t = new JObject();
                t["races"] = new JArray(value.Select(ProjectEntry.RefToken));
            }
        }
        public int Armor { get => I("armor", 100); set => json["armor"] = value; }
        public int Cargo { get => I("cargo", 0); set => json["cargo"] = value; }
        public int Handling { get => I("handling", 100); set => json["handling"] = value; }
        public int Price { get => I("price", 0); set => json["price"] = value; }
        public int HangarHeight { get => I("hangarHeight", 250); set => json["hangarHeight"] = value; }
        public float ModelLength { get => F("modelLength", 1000f); set => json["modelLength"] = Mathf.Round(value * 100f) / 100f; }   // hundredths: a turn keeps the size (ModBuilderView.TurnModel)
        public float ModelYaw { get => F("modelYaw", 0f); set => json["modelYaw"] = Mathf.Round(value); }
        public float ModelPitch { get => F("modelPitch", 0f); set => SetOrRemove("modelPitch", Mathf.Round(value)); }
        public float ModelRoll { get => F("modelRoll", 0f); set => SetOrRemove("modelRoll", Mathf.Round(value)); }

        void SetOrRemove(string key, float v) { if (v == 0f) json.Remove(key); else json[key] = v; }

        /// <summary>modelOffset (game units, x y z in the ship's frame); zeros = none.</summary>
        public Vector3Int ModelOffset
        {
            get => json["modelOffset"] is JArray a && a.Count >= 3
                ? new Vector3Int(Mathf.RoundToInt((float)a[0]), Mathf.RoundToInt((float)a[1]), Mathf.RoundToInt((float)a[2])) : Vector3Int.zero;
            set { if (value == Vector3Int.zero) json.Remove("modelOffset"); else json["modelOffset"] = new JArray(value.x, value.y, value.z); }
        }

        JObject Slots
        {
            get
            {
                if (!(json["slots"] is JObject o)) json["slots"] = o = new JObject();
                return o;
            }
        }

        /// <summary>Slot count by kind: 0 primary, 1 secondary, 2 turret, 3 equipment.</summary>
        public int SlotCount(int kind)
        {
            string k = SlotKey(kind);   // read without making a "slots" object (FillFromGame looks for a missing one)
            return json["slots"] is JObject o && o[k] is JValue v && v.Type == JTokenType.Integer ? (int)v : 0;
        }

        public void SetSlotCount(int kind, int count) => Slots[SlotKey(kind)] = Mathf.Clamp(count, 0, 99);

        /// <summary>The builder's hull textures: one "materials" entry for every mesh (ships.json; ModMaterials.FromSpec), its
        /// 'role' "diffuse", "normal" or "metallicSmoothness" (null = none). False when the entry list is more than the builder
        /// manages (several entries, or one limited to some meshes: a hand edit), which it then leaves alone.</summary>
        public bool HullTexturesEditable => !(json["materials"] is JArray a) || a.Count == 0
                                            || a.Count == 1 && a[0] is JObject o && string.IsNullOrEmpty((string)o["mesh"]) && o["submesh"] == null;

        public string HullTexture(string role) =>
            json["materials"] is JArray a && a.Count == 1 && a[0] is JObject o && o[role] is JValue v ? (string)v : null;

        public void SetHullTexture(string role, string path)
        {
            if (!HullTexturesEditable) return;
            var o = json["materials"] is JArray a && a.Count == 1 && a[0] is JObject e ? e : new JObject { ["smoothness"] = 1 };
            if (string.IsNullOrEmpty(path)) o.Remove(role); else o[role] = path;
            bool any = o["diffuse"] != null || o["normal"] != null || o["metallicSmoothness"] != null;
            if (any) json["materials"] = new JArray(o); else json.Remove("materials");
        }

        static string SlotKey(int kind) => kind == 0 ? "primary" : kind == 1 ? "secondary" : kind == 2 ? "turret" : "equipment";

        /// <summary>ships.json "addParts": the original's additive layers beside the hull (CustomAddPart: model, role,
        /// texture), in the order they are listed.</summary>
        public List<(string model, string role, string texture)> AddParts =>
            json["addParts"] is JArray a
                ? a.OfType<JObject>().Select(o => ((string)o["model"], (string)o["role"], (string)o["texture"])).ToList()
                : new List<(string, string, string)>();

        /// <summary>Adds an add part (CustomAddPart.Roles); 'texture' null = the game's material for the role.</summary>
        public void AddPart(string role, string model, string texture = null)
        {
            if (!(json["addParts"] is JArray a)) json["addParts"] = a = new JArray();
            var o = new JObject { ["model"] = model, ["role"] = role };
            if (!string.IsNullOrEmpty(texture)) o["texture"] = texture;
            a.Add(o);
        }

        /// <summary>The texture the add part at 'index' is drawn with (null: its model's own, else the game's for the role).</summary>
        public void SetPartTexture(int index, string texture)
        {
            if (!(json["addParts"] is JArray a) || index < 0 || index >= a.Count || !(a[index] is JObject o)) return;
            if (string.IsNullOrEmpty(texture)) o.Remove("texture"); else o["texture"] = texture;
        }

        /// <summary>Removes the add part at 'index' (the list goes when it is empty).</summary>
        public void RemovePart(int index)
        {
            if (!(json["addParts"] is JArray a) || index < 0 || index >= a.Count) return;
            a.RemoveAt(index);
            if (a.Count == 0) json.Remove("addParts");
        }

        public int MountCount(int slotType) => Mounts.Count(m => m.slotType == slotType);

        // ---- a customizable ship (ModShipKits) ----

        /// <summary>The kit it flies ("kit": this mod's kit id, or "mod_id:kit_id"), null = a ship with a model.</summary>
        public string Kit { get => S("kit"); set { if (string.IsNullOrEmpty(value)) json.Remove("kit"); else json["kit"] = value; } }
        public bool IsKitShip => !string.IsNullOrEmpty(Kit);
        /// <summary>Which of the kit's parts it may fit (the parts' "types").</summary>
        public string KitType { get => S("kitType") ?? ""; set => json["kitType"] = value ?? ""; }

        /// <summary>The default build's part in a slot (null = the slot's first part the type may fit).</summary>
        public string BuildPart(string slot) => json["build"] is JObject b && b[slot] is JValue v && v.Type == JTokenType.String ? (string)v : null;

        public void SetBuildPart(string slot, string part)
        {
            if (!(json["build"] is JObject b)) json["build"] = b = new JObject();
            if (string.IsNullOrEmpty(part)) b.Remove(slot); else b[slot] = part;
        }

        public int BuildTier
        {
            get => json["build"] is JObject b && b["tier"] is JValue v && v.Type == JTokenType.Integer ? (int)v : 0;
            set { if (!(json["build"] is JObject b)) json["build"] = b = new JObject(); b["tier"] = Mathf.Max(0, value); }
        }

        /// <summary>The default build's parts as build text ("slot=part;...;tier=N"), for a preview of another part.</summary>
        public string BuildText()
        {
            if (!(json["build"] is JObject b)) return "tier=0";
            var parts = b.Properties().Where(p => p.Value.Type == JTokenType.String && p.Name != "skin" && p.Name != "weaponColor"
                                                  && !p.Name.StartsWith("exhaust")).Select(p => p.Name + "=" + (string)p.Value);
            return string.Join(";", parts.Append("tier=" + BuildTier));
        }

        /// <summary>The ship flies a kit: its model's own fields go.</summary>
        public void UseKit(string kit, string kitType)
        {
            foreach (var k in new[] { "model", "modelLength", "modelYaw", "modelPitch", "modelRoll", "modelOffset", "materials", "hangarHeight", "engineGlowRadius", "addParts" })
                json.Remove(k);
            Kit = kit;
            KitType = kitType;
            if (!(json["build"] is JObject)) json["build"] = new JObject { ["tier"] = 0 };
            NeedsLayout = false;
        }

        /// <summary>The ship flies one of the game's own models ("gof2:<name>", GameModels), not a file.</summary>
        public bool HasGameModel => GameModels.Is(Model);

        /// <summary>Remake (GameModels): the ship flies one of the game's models ('assembly', no file): the file model's sizing,
        /// turns, offset, hull textures and hangar height go (the game model sits as made, on the pads like its ship), and
        /// the hardpoints become 'mounts' (that ship's, copied; empty for a model that is no ship's). 'from': that ship's
        /// stats, slots and race for a new ship (null = keep them).</summary>
        public void UseGameModel(string assembly, ShipData from, IEnumerable<WeaponMount> mounts, int race = -1)
        {
            Model = GameModels.Ref(assembly);
            foreach (var k in new[] { "modelLength", "modelYaw", "modelPitch", "modelRoll", "modelOffset", "materials", "hangarHeight", "engineGlowRadius", "addParts" })
                json.Remove(k);
            Mounts.Clear();
            if (mounts != null) Mounts.AddRange(mounts.Select(Copy));
            NeedsLayout = false;   // the model's own hardpoints, not the builder's first guess from its box
            if (from == null) return;
            Armor = from.armor;
            Cargo = from.cargo;
            Handling = Mathf.RoundToInt(from.handling);
            Price = from.price;
            int[] counts = { from.slots?.primary ?? 0, from.slots?.secondary ?? 0, from.slots?.turret ?? 0, from.slots?.equipment ?? 0 };
            for (int k = 0; k < 4; k++) SetSlotCount(k, counts[k]);
            if (race >= 0) Race = race;
        }

        /// <summary>An override's stats and slots the entry doesn't give, from the game's ship (they are the game's anyway):
        /// the builder shows and counts what the ship really has (a hand-written override without "slots" had none).</summary>
        public void FillFromGame(ShipData d)
        {
            if (d == null) return;
            if (json["armor"] == null) json["armor"] = d.armor;
            if (json["cargo"] == null) json["cargo"] = d.cargo;
            if (json["handling"] == null) json["handling"] = Mathf.RoundToInt(d.handling);
            if (json["price"] == null) json["price"] = d.price;
            var slots = Slots;
            int[] counts = { d.slots?.primary ?? 0, d.slots?.secondary ?? 0, d.slots?.turret ?? 0, d.slots?.equipment ?? 0 };
            for (int k = 0; k < 4; k++) if (slots[SlotKey(k)] == null) slots[SlotKey(k)] = counts[k];
        }

        /// <summary>The entry as written to ships.json (the hardpoints from Mounts).</summary>
        public JObject ToJson()
        {
            var o = (JObject)json.DeepClone();
            o["mounts"] = new JArray(Mounts.Select(MountJson));
            return o;
        }

        /// <summary>A hardpoint as ships.json / shipkits.json write it (the remake's mod fields only when set: a mount the
        /// builder doesn't edit keeps them).</summary>
        public static JObject MountJson(WeaponMount m)
        {
            JArray Floats(float[] a, int digits) => new JArray(a.Select(f => (float)Math.Round(f, digits)));
            var e = new JObject { ["slotType"] = m.slotType, ["position_engine"] = new JArray(m.position_engine ?? new int[3]) };
            if (m.turretAngles != null && m.turretAngles.Length > 0) e["turretAngles"] = Floats(m.turretAngles, 3);
            // A turret's facing: turretUp, but a plain top / under-hull one as before (none / upsideDown).
            var up = m.TurretUpLocal;
            if (m.slotType == 2 && m.turretUp != null && m.turretUp.Length >= 3 && up.y < 0.9999f && up.y > -0.9999f)
                e["turretUp"] = Floats(m.turretUp, 3);
            else if (m.slotType == 2 && up.y <= -0.9999f) e["upsideDown"] = true;
            else if (m.slotType != 2 && m.upsideDown) e["upsideDown"] = true;
            if (m.glowColor != null && m.glowColor.Length >= 3) e["glowColor"] = Floats(m.glowColor, 3);
            if (m.glowSize != null && m.glowSize.Length >= 3) e["glowSize"] = Floats(m.glowSize, 1);
            if (m.slotType == 3 && EngineGlowShape.Normalize(m.glowShape) != "round") e["glowShape"] = EngineGlowShape.Normalize(m.glowShape);
            if (m.outline != null && m.outline.Length >= 6) e["outline"] = Floats(m.outline, 1);
            if (m.builtIn) e["builtIn"] = true;
            if (m.direction != null && m.direction.Length >= 3) e["direction"] = Floats(m.direction, 3);
            if (m.rotation != null && m.rotation.Length >= 3) e["rotation"] = Floats(m.rotation, 3);
            if (m.nodes != null && m.nodes.Length > 0) e["nodes"] = new JArray(m.nodes);
            if (m.fxScale > 0f) e["fxScale"] = (float)Math.Round(m.fxScale, 3);
            if (m.socketScale > 0f) e["socketScale"] = (float)Math.Round(m.socketScale, 3);
            return e;
        }
    }
}
