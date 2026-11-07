// ModShipKits.cs
// Remake mods: customizable ships, assembled from parts the player picks in the hangar (EVERSPACE 2's ship modules).
// A mod's shipkits.json lists kits, each a set of parts by slot:
//   [ { "id": "medium", "name": "Medium hulls",
//       "slots": ["base", "cockpit", "body", "rear", "wings"]   (the build's slots, in assembly order; default: the parts')
//       "scale": 1.0,            Unity metres per model unit (the parts share one origin, so one scale for every build)
//       "yaw": 0,                degrees about Unity y when the parts' nose doesn't face +Z
//       "materials": [ ... ],    like a ship's: applied to every part's renderers it names (ModMaterials.FromSpec)
//       "parts": [
//         { "id": "nemesis", "slot": "wings", "name": "Nemesis" | { "en": ..., "de": ... }, "model": "parts/wings_009.glb",
//           "types": ["sentinel"],   which ship types may fit it (the ships' "kitType"; none = every type)
//           "tiers": ["parts/wings_009_t1.glb", ...],   extension meshes shown at build tier 1, 2, ... (none = no extension)
//           "mounts": [ { "slotType": 0, "position_engine": [x, y, z] }, ... ],   the part's weapon / turret / exhaust mounts,
//           "tierMounts": [ [ ... ], ... ],   and its tier extensions' (like a ship's "mounts", ship-relative game units)
//           "anchors": { "cockpit": [x, y, z] },   points this part offers others (model units, the GLB's glTF coordinates)
//           "anchor": "cockpit",   this part (and its tier models) sits on that point of the build's part offering it
//                                  (EVERSPACE 2's bodies carry the canopy at a socket of their own)
//           "stats": { "armor": 10, "cargo": -5, "handling": 8 (%), "equipment": 1 (slots), "topSpeed", "turnRate",
//                      "acceleration", "strafeSpeed" (% flight bonuses, both flight styles) },
//           "slots": { "primary": 2, "secondary": 1, "turret": 1 } }
//     A build adds up its parts' stats (the hull, cargo and handling in % of the ship's own) and, per slot type, its parts'
//     slots (the ship's own count when none of its parts gives that type): BuildEffect; the hull (Shop.BaseHp, PlayerHealth,
//     StationLevel), cargo (Shop.MaxLoad), slots (Hangar.SlotCount / FitToSlots) and flight (Database.BuildFlightStats) of the
//     player's ship follow its build. The tier is cosmetic (its extensions' looks and mounts).
//     A build's mounts of each slot type are its parts' (and its tier extensions'), or the ship's own "mounts" of that type
//     when no part has any (MountsFor; Database.MountsOf: the player's build for the player's ship, else the default build).
//       ] } ]
//       "palettes": { "main": [ { "id": "white", "name": "Snow White", "color": "#F9F9F9", "metallic": 0, "smoothness": 0.5 }, ... ] },
//       "colorSlots": [ { "id": "hull1", "name": "Main colour", "palette": "main", "default": "white",
//                         "targets": [ { "material": "Paint1", "property": "base" | "emission" | "boost" | "engine" | "livery" | "decal1..3", "intensity": 1 } ] } ],
//       "presets": [ { "id": "scheme_4", "name": "Scheme 4", "colors": { "hull1": "#699CBF/1/0.6", "lights": "white", ... } } ]
//     The player picks a colour per slot from its palette (or a preset sets them all); each target recolours the materials whose
//     name contains 'material' (the model's own, or a kit "materials" entry's 'material' filter): base = the base colour (x
//     intensity; a value's metallic / smoothness too), emission = the emission colour x intensity, boost = an engine flame's
//     boost colour x intensity (its boost cards; the manoeuvring jets' material is "lateral thruster"), engine = the engine glow,
//     livery = the livery's colour (and a value's metallic / smoothness) where a livery shows, decal1 / decal2 / decal3 = a
//     decal's colour 1, 2 and border.
//     A colour value: a palette entry's id, or "#RRGGBB" with an optional "/metallic/smoothness".
//     A strength slot { "id", "name", "steps": [0, 0.25, 0.5, 0.75, 1], "default": "0.5", "scales": "<colour slot id>" } has no
//     palette: its value (a number) scales that colour slot's emission targets (EVERSPACE 2's Emissive 3 intensity).
//       "hardpoints": { "turret": "...glb", "weapons": [ { "slot", "model", "items", "categories" } ] }: the weapons' and the
//     turret's models on the build's mounts (ModHardpoints).
//       "skins": [ { "id": "gold", "name": "Gold", "types": ["groza"],   (none = every type)
//                    "engineColor": [1, 0.8, 0.5],   the exhausts' glow in this skin (none = the mounts' own)
//                    "materials": [ { "material": "ship_terran_000_body", "diffuse": "skins/groza_gold.jpg",
//                                     "emission": "...png", "metallicSmoothness": "...png", "normal": "...png",
//                                     "color": [r, g, b], "emissionColor": [r, g, b], "emissionIntensity": 1 } ] } ]
//     Paint jobs as texture sets (Galaxy on Fire 3's paint schemes): a build's skin swaps the textures of copies of the
//     materials whose name contains 'material' (the model's own glTF material names; ModShips.ApplySkin); a field left out
//     keeps the model's own. No skin = the model's own look. A skin's textures load only when a build shows it.
//     A skin may also carry "colors" (a preset's id, or { slot: value }: picked with the skin in the Customize screen and on
//     the NPCs' random builds) and a "livery" (below): EVERSPACE 2's skins are a colour scheme and a livery.
//       "liveries": [ { "id": "blossom", "name": "Nebula Blossom", "texture": "liveries/blossom.png", "icon": "...png",
//                       "types": [...], "color": "#FFF5F4", "metallic": 0, "smoothness": 0.4, "opacity": 1,
//                       "mirror": [false, false], "scale": [1, 1], "offset": [0, 0], "tiling": false, "clipAngle": 90 } ],
//       "liveryAxes": { "forward": "x", "right": "z", "up": "y" }   (the parts' glTF axes, signed; default EVERSPACE 2's)
//     A livery (EVERSPACE 2's projection decals, M_ShipModules_Hull_Parent's ProjectionDecal: its compiled shader) is a mask
//     projected from above onto the materials marked "livery": true: the ship's (forward, right) over twice its largest
//     half-extent, the centre at the image's middle (the nose to the right), mirrored at the middle per axis, scaled and
//     offset, repeated outside the image only with "tiling"; mask^(1 + 1.2 luminance(colour)) x opacity lerps the base
//     colour toward 'color' (keeping the map's shading), the metallic and smoothness toward its own, except on faces within
//     (90 - clipAngle) degrees of vertical (GoF2/Hull: _Livery*). Build.livery ("livery=id", "none") picks it, else the
//     skin's.
//       "decals": [ { "id": "ramen_cat", "name": "Ramen Cat", "insignia": "decals/ramen_cat_insignia.png",
//                     "type": "decals/ramen_cat_type.png", "icon": "...png", "types": [...] } ]
//     A decal (EVERSPACE 2's ship decals, M_ShipModules_Decals_Parent's compiled shader): two masks, the insignia and the
//     type (lettering), drawn on the materials marked "decal": "insignia" / "type" (the parts' decal faces): R x colour 1 +
//     G x colour 2 + B x the border colour, over A (GoF2/Hull's decal mode, _Decal*). The colours are colour slots with
//     the properties "decal1", "decal2", "decal3". Build.decal ("decal=id", "none") picks it, else the skin's; none = the
//     decal faces left empty.
//       "trails": [ { "id": "glitter", "name": "Stardust", "kind": "glitter", "icon": "...png", "curves": [[...], ...],
//                     "texture": "trails/barcode.png", "mask": "...png", "normal": "...png",
//                     "mesh": { "positions": [...], "normals": [...], "uv": [...], "indices": [...] } } ]
//     EVERSPACE 2's customization engine trails (DT_ShipExhaustParticles, Es2Trails): "kind" one of default (the veil
//     Es2EngineVfx draws by itself), glitter, smoke, bubbles, sparks, darktrail, firetrail, lines, lightning, rings,
//     squares, triangles, veils; "curves" the systems' curve tables over the normalized age (Es2Trails says which), the
//     textures the materials sample (sRGB masks, a linear "normal"), "mesh" the shape trails' cross section (EVERSPACE 2's
//     centimetres and axes: x forward, y right, z up). Build.trail / Build.boostTrail ("trail=id", "boosttrail=id") pick
//     the engine sockets' and the booster sockets' trail (none: the Default veil); only the player's ship shows them.
//   Every kit ship also has a weapon colour (Build.weaponColor, "weaponcolor=<id or #RRGGBB>"; none = the weapons' own look):
//   the energy weapons' shots in that colour (Flight.WeaponTint), picked from the kit's "weapons" palette, else WeaponPalette.
//   A kit whose "hardpoints" have "toggle": true lets the player hide its weapon models, the mounts' pylons and its turret
//   model (Build.weapons, "weapons=off"; ships.json's "build" may start a ship with "weapons": false): the guns then fire from
//   the mounts themselves (ModHardpoints.Hidden).
// Customizations of other ships (the game's own, and mod ships built from one model): a ships.json entry ("override": n or
// "mod:id", or a new ship without "kit") with "skins" (the format above, for that ship) and / or "weaponColors" (a palette list,
// or true for the built-in one). Such a ship gets a kit of its own with no parts (CustomizationOf: the skins of every mod that
// is on, merged), so the Customize screen, builds (saved by BuildKey: "gof2:<n>" for the game's ships), the weapon colour and
// other players' views work as for kit ships; its skins show on a copy of its model (ModShips.SkinnedTemplate).
// With exhaust styles in the mods that are on (ModExhausts, exhausts.json) every ship has a Customize screen: a ship without
// other customizations gets a kit with nothing but them (no weapon colour row). A build's exhaust ("exhaust=mod_id:id",
// "exhaustcolor=", "exhaustcolor2=", "exhaustsize=") is put on the model's instance (ModExhausts.Apply), not into its assembly.
// ships.json: a new ship with "kit": "<kit id>" (this mod's) or "mod_id:kit_id" instead of "model", "kitType": "sentinel",
// and "build": { "wings": "nemesis", "body": "body_a", ..., "tier": 1 } (its default: what the dealers sell and NPCs fly;
// a slot left out takes the first part the type may fit). The player's build of each kit ship is Session.ShipBuilds (by the
// ship's key, saved), shown by PlayerHull.Assembly as the variant assembly "ship_NNN_mod#<build>" (ModShips.Template
// assembles it from the loaded parts the first time). Builds are text: "body=body_a;rear=rear_1;wings=nemesis;tier=1".

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static partial class ModShipKits
    {
        public const string File = "shipkits.json";

        static readonly HashSet<string> KitFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "id", "name", "slots", "scale", "yaw", "materials", "parts", "palettes", "colorSlots", "presets", "hardpoints", "skins", "liveries", "liveryAxes", "decals", "trails" };
        static readonly HashSet<string> SkinFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "id", "name", "types", "engineColor", "materials", "colors", "livery", "decal" };
        static readonly HashSet<string> SkinMaterialFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "material", "diffuse", "emission", "metallicSmoothness", "specularGlossiness", "normal", "color", "emissionColor", "emissionIntensity" };
        static readonly HashSet<string> PartFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "id", "slot", "name", "model", "types", "tiers", "mounts", "tierMounts", "stats", "slots", "anchors", "anchor" };

        public sealed class Part
        {
            public string id, slot, model;
            public Dictionary<string, string> name;
            public List<string> types = new List<string>(), tiers = new List<string>();
            public List<WeaponMount> mounts = new List<WeaponMount>();
            public List<List<WeaponMount>> tierMounts = new List<List<WeaponMount>>();
            public float armor, cargo, handling, topSpeed, turnRate, acceleration, strafeSpeed;   // % (stats)
            public int equipment;                                                                  // slots (stats)
            public int primarySlots = -1, secondarySlots = -1, turretSlots = -1;                  // -1 = not given
            /// <summary>Named points this part offers others (model units, the GLB's own glTF coordinates), and the anchor
            /// this part sits on (its model and tier models moved to that point of the build's part that offers it).</summary>
            public Dictionary<string, Vector3> anchors = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
            public string anchor;
            public string Name => ModManifest.Pick(name) ?? id;
            public bool Allows(string type) => types.Count == 0 || types.Any(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>A palette entry: a colour with an optional finish (metallic / smoothness, -1 = the material's own).</summary>
        public sealed class ColorChoice
        {
            public string id;
            public Dictionary<string, string> name;
            public Color color = Color.white;
            public float metallic = -1f, smoothness = -1f;
            public string Name => ModManifest.Pick(name) ?? id;
        }

        public sealed class ColorTarget
        {
            public string material, property = "base";
            public float intensity = 1f;
        }

        public sealed class ColorSlot
        {
            public string id, palette, defaultValue;
            public Dictionary<string, string> name;
            public List<ColorTarget> targets = new List<ColorTarget>();
            /// <summary>A strength slot (no palette): the values the player steps through (0..1), scaling the emission
            /// targets of the slot 'scales' names (EVERSPACE 2's Emissive 3 intensity slider).</summary>
            public float[] steps;
            public string scales;
            public bool IsStrength => steps != null && steps.Length > 0;
            public string Name => ModManifest.Pick(name) ?? id;
        }

        public sealed class Preset
        {
            public string id;
            public Dictionary<string, string> name;
            public Dictionary<string, string> colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public string Name => ModManifest.Pick(name) ?? id;
        }

        /// <summary>One material's textures in a skin (paths in the mod; null = the model's own).</summary>
        public sealed class SkinMaterial
        {
            public string material, diffuse, emission, metallicSmoothness, normal;
            public string specularGlossiness;   // URP's specular setup's map (RGB specular, sRGB; A smoothness), on a material that has it
            public float[] color, emissionColor;
            public float emissionIntensity = 1f;
        }

        /// <summary>A paint job: texture sets for the materials it names (ModShips.ApplySkin).</summary>
        public sealed class Skin
        {
            public ModInfo mod;   // the mod whose files it uses (a ship's customizations can come from several)
            public string id;
            public Dictionary<string, string> name;
            public List<string> types = new List<string>();
            public float[] engineColor;
            public List<SkinMaterial> materials = new List<SkinMaterial>();
            /// <summary>Colour slot values picked with the skin (empty = none).</summary>
            public Dictionary<string, string> colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            /// <summary>A preset's id its colours come from (resolved into 'colors' once the kit's presets are read).</summary>
            public string colorPreset;
            /// <summary>The livery it shows unless the build picks one (a kit livery's id; null = none).</summary>
            public string livery;
            /// <summary>The decal it shows unless the build picks one (a kit decal's id; null = none).</summary>
            public string decal;
            public string Name => ModManifest.Pick(name) ?? id;
            public bool Allows(string type) => types.Count == 0 || types.Any(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase));

            /// <summary>The image files it uses, with how they are read (linear, normal map).</summary>
            public IEnumerable<(string path, bool linear, bool normal)> Textures()
            {
                foreach (var m in materials)
                {
                    if (!string.IsNullOrEmpty(m.diffuse)) yield return (m.diffuse, false, false);
                    if (!string.IsNullOrEmpty(m.emission)) yield return (m.emission, false, false);
                    if (!string.IsNullOrEmpty(m.metallicSmoothness)) yield return (m.metallicSmoothness, true, false);
                    if (!string.IsNullOrEmpty(m.specularGlossiness)) yield return (m.specularGlossiness, false, false);
                    if (!string.IsNullOrEmpty(m.normal)) yield return (m.normal, true, true);
                }
            }
        }

        /// <summary>A livery: a mask projected onto the ship from above (EVERSPACE 2's projection decals).</summary>
        public sealed class Livery
        {
            public ModInfo mod;
            public string id, texture, icon;
            public Dictionary<string, string> name;
            public List<string> types = new List<string>();
            public Color color = new Color(1f, 0.909f, 0.901f);
            public float metallic, smoothness = 0.4f, opacity = 1f, clipAngle = 90f;
            public bool tiling, mirrorX, mirrorY;
            public Vector2 scale = Vector2.one, offset;
            public string Name => ModManifest.Pick(name) ?? id;
            public bool Allows(string type) => types.Count == 0 || types.Any(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>A decal: the insignia and type masks for the parts' decal faces (EVERSPACE 2's ship decals).</summary>
        public sealed class Decal
        {
            public ModInfo mod;
            public string id, insignia, type, icon;
            public Dictionary<string, string> name;
            public List<string> types = new List<string>();
            public string Name => ModManifest.Pick(name) ?? id;
            public bool Allows(string t) => types.Count == 0 || types.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>An engine trail (EVERSPACE 2's customization trails: Es2Trails runs it).</summary>
        public sealed class EngineTrail
        {
            public ModInfo mod;
            public string id, kind, icon, texture, mask, normal;
            public Dictionary<string, string> name;
            public readonly List<float[]> curves = new List<float[]>();
            public TrailMesh mesh;
            public string Name => ModManifest.Pick(name) ?? id;
            /// <summary>EVERSPACE 2's Default: the veil Es2EngineVfx draws by itself.</summary>
            public bool IsDefault => !Es2Trails.Known(kind);

            /// <summary>Its textures (sRGB masks, a linear normal map).</summary>
            public IEnumerable<(string path, bool linear, bool normal)> Textures()
            {
                if (!string.IsNullOrEmpty(texture)) yield return (texture, false, false);
                if (!string.IsNullOrEmpty(mask)) yield return (mask, false, false);
                if (!string.IsNullOrEmpty(normal)) yield return (normal, true, true);
            }
        }

        /// <summary>A shape trail's cross section, EVERSPACE 2's centimetres and axes (x forward, y right, z up).</summary>
        public sealed class TrailMesh
        {
            public Vector3[] positions, normals;
            public Vector2[] uv;
            public int[] indices;
        }

        public sealed class Kit
        {
            public readonly Dictionary<string, List<ColorChoice>> palettes = new Dictionary<string, List<ColorChoice>>(StringComparer.OrdinalIgnoreCase);
            public readonly List<ColorSlot> colorSlots = new List<ColorSlot>();
            public readonly List<Preset> presets = new List<Preset>();

            public List<ColorChoice> PaletteOf(ColorSlot slot) => slot != null && slot.palette != null && palettes.TryGetValue(slot.palette, out var l) ? l : new List<ColorChoice>();

            public ModInfo mod;
            public string localId;
            public Dictionary<string, string> name;
            public List<string> slots = new List<string>();
            public float scale = 1f, yaw;
            public List<CustomShipMaterial> materials = new List<CustomShipMaterial>();
            public List<Part> parts = new List<Part>();
            public ModHardpoints.Set hardpoints;   // the weapon / turret models (ModHardpoints), null = none
            public readonly List<Skin> skins = new List<Skin>();
            public readonly List<Livery> liveries = new List<Livery>();
            public readonly List<Decal> decals = new List<Decal>();
            public readonly List<EngineTrail> trails = new List<EngineTrail>();
            /// <summary>The parts' axes the liveries are projected along, in Unity's object space (glTFast mirrors x): forward,
            /// right, up. Default EVERSPACE 2's export (glTF x forward, z right, y up).</summary>
            public Vector3 liveryForward = new Vector3(-1f, 0f, 0f), liveryRight = new Vector3(0f, 0f, 1f), liveryUp = new Vector3(0f, 1f, 0f);
            /// <summary>The Customize screen offers the weapon colour (false: a ship whose only customizations are the mods'
            /// exhaust styles).</summary>
            public bool weaponColorRow = true;
            public string Key => mod.Id + ":" + localId;
            public string Name => ModManifest.Pick(name) ?? localId;
            public int MaxTier => parts.Count == 0 ? 0 : parts.Max(p => p.tiers.Count);

            public Part Find(string id) => parts.Find(p => string.Equals(p.id, id, StringComparison.OrdinalIgnoreCase));

            public Skin FindSkin(string id) => string.IsNullOrEmpty(id) ? null : skins.Find(k => string.Equals(k.id, id, StringComparison.OrdinalIgnoreCase));

            /// <summary>The skins a ship type may wear, in the file's order.</summary>
            public List<Skin> SkinsFor(string type) => skins.Where(k => k.Allows(type)).ToList();

            public Livery FindLivery(string id) => string.IsNullOrEmpty(id) ? null : liveries.Find(l => string.Equals(l.id, id, StringComparison.OrdinalIgnoreCase));
            public List<Livery> LiveriesFor(string type) => liveries.Where(l => l.Allows(type)).ToList();

            public EngineTrail FindTrail(string id) => string.IsNullOrEmpty(id) ? null : trails.Find(t => string.Equals(t.id, id, StringComparison.OrdinalIgnoreCase));

            /// <summary>The trail a build gives its engine sockets (or its booster sockets); null = the Default veil.</summary>
            public EngineTrail TrailOf(Build b, bool booster) => FindTrail(booster ? b?.boostTrail : b?.trail) is EngineTrail t && !t.IsDefault ? t : null;

            public Decal FindDecal(string id) => string.IsNullOrEmpty(id) ? null : decals.Find(d => string.Equals(d.id, id, StringComparison.OrdinalIgnoreCase));
            public List<Decal> DecalsFor(string type) => decals.Where(d => d.Allows(type)).ToList();

            /// <summary>The decal a build shows: its own, else its skin's (none = "none" or neither).</summary>
            public Decal DecalOf(Build b) => b == null || b.decal == Build.NoSkin ? null : FindDecal(b.decal) ?? FindDecal(FindSkin(b.skin)?.decal);

            /// <summary>The livery a build shows: its own, else its skin's (none = "none" or neither).</summary>
            public Livery LiveryOf(Build b) => b == null || b.livery == Build.NoSkin ? null : FindLivery(b.livery) ?? FindLivery(FindSkin(b.skin)?.livery);

            /// <summary>Whether the Customize screen has anything for a ship type: a slot with more than one part, a tier, colours
            /// or skins.</summary>
            public bool HasChoices(string type) => true;   // the weapon colour, on every kit ship

            /// <summary>The weapon colours: the kit's palette "weapons", else the built-in WeaponPalette.</summary>
            public List<ColorChoice> WeaponColors => palettes.TryGetValue("weapons", out var w) && w.Count > 0 ? w : WeaponPalette;

            /// <summary>The parts of a slot a ship type may fit, in the file's order.</summary>
            public List<Part> PartsFor(string slot, string type) =>
                parts.Where(p => string.Equals(p.slot, slot, StringComparison.OrdinalIgnoreCase) && p.Allows(type)).ToList();
        }

        static readonly List<Kit> kits = new List<Kit>();
        static int parsedRevision = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { kits.Clear(); parsedRevision = -1; shipCustom.Clear(); exhaustOnly.Clear(); customRevision = -1; }

        /// <summary>The kits of the mods that are on.</summary>
        public static List<Kit> All()
        {
            if (parsedRevision == ModManager.Revision) return kits;
            parsedRevision = ModManager.Revision;
            kits.Clear();
            foreach (var mod in ModManager.Active)
            {
                try { Read(mod, kits); }
                catch (ModJsonException e) { Warn(mod, e.Message); }
            }
            return kits;
        }

        /// <summary>A kit by "mod_id:kit_id", or by its kit id alone within 'mod' (a ship's own mod).</summary>
        public static Kit Find(string key, ModInfo mod = null)
        {
            if (string.IsNullOrEmpty(key)) return null;
            int colon = key.IndexOf(':');
            foreach (var k in All())
            {
                if (colon > 0 ? string.Equals(k.Key, key, StringComparison.OrdinalIgnoreCase)
                              : mod != null && k.mod == mod && string.Equals(k.localId, key, StringComparison.OrdinalIgnoreCase))
                    return k;
            }
            return null;
        }

        [Serializable] sealed class MaterialList { public List<CustomShipMaterial> list; }

        /// <summary>A build's mounts: for each slot type its parts' mounts (base and tier extension, in the kit's slot order),
        /// else the ship's own of that type.</summary>
        public static List<WeaponMount> MountsFor(CustomShipData c, Kit kit, Build build)
        {
            var fromParts = new List<WeaponMount>();
            foreach (string slot in kit.slots)
            {
                if (!build.parts.TryGetValue(slot, out var id)) continue;
                var part = kit.Find(id);
                if (part == null) continue;
                fromParts.AddRange(part.mounts);
                if (build.tier > 0 && build.tier <= part.tierMounts.Count) fromParts.AddRange(part.tierMounts[build.tier - 1]);
            }
            var all = new List<WeaponMount>();
            foreach (int type in new[] { 0, 1, 2, 3, 4, 5 })
            {
                var own = fromParts.Where(m => m.slotType == type).ToList();
                all.AddRange(own.Count > 0 ? own : (c.mounts ?? new List<WeaponMount>()).Where(m => m.slotType == type));
            }
            return all;
        }

        public static KitEffect EffectOf(Kit kit, Build build)
        {
            var e = new KitEffect();
            foreach (string slot in kit.slots)
            {
                if (!build.parts.TryGetValue(slot, out var id)) continue;
                var p = kit.Find(id);
                if (p == null) continue;
                e.armor += p.armor; e.cargo += p.cargo; e.handling += p.handling; e.equipment += p.equipment;
                e.flight.topSpeed += p.topSpeed; e.flight.turnRate += p.turnRate; e.flight.acceleration += p.acceleration; e.flight.strafeSpeed += p.strafeSpeed;
                if (p.primarySlots >= 0) e.primary = Math.Max(0, e.primary) + p.primarySlots;
                if (p.secondarySlots >= 0) e.secondary = Math.Max(0, e.secondary) + p.secondarySlots;
                if (p.turretSlots >= 0) e.turret = Math.Max(0, e.turret) + p.turretSlots;
            }
            return e;
        }

        /// <summary>The build effect of the player's ship (null: not a kit ship); 'ship' another than the player's: its default build.</summary>
        public static KitEffect BuildEffect(int ship)
        {
            var c = KitShip(ship);
            var kit = KitOf(c);
            if (kit == null) return null;
            return EffectOf(kit, ship == Session.ShipIndex ? PlayerBuild(ship) : DefaultBuild(c, kit));
        }

        /// <summary>A ship's hull, cargo, handling and slots with its build: KitRules (GoF2.Game).</summary>
        public static int Armor(ShipData s, int fallback) => KitRules.Armor(s, fallback);
        public static int Cargo(ShipData s, int fallback) => KitRules.Cargo(s, fallback);
        public static float Handling(ShipData s) => KitRules.Handling(s);
        public static ShipSlots Slots(ShipData s) => KitRules.Slots(s);

        /// <summary>The ship as its build makes it (a copy with the hull, cargo, handling and slots adjusted; the ship itself
        /// when it isn't a kit ship): for the stat rows (ItemInfo, ItemInfoWindow).</summary>
        public static ShipData Effective(ShipData s)
        {
            if (s == null || BuildEffect(s.index) == null) return s;
            float handling = Handling(s);
            return new ShipData { index = s.index, name = s.name, description = s.description, price = s.price, armor = Armor(s, s.armor),
                                  cargo = Cargo(s, s.cargo), handling = handling, handlingMultiplier = handling / 100f, slots = Slots(s) };
        }

        /// <summary>Database.MountsOf for a kit ship: the player's build for the player's ship, else its default build; null:
        /// not a kit ship.</summary>
        /// <summary>The mounts of a type a model of 'ship' in 'build' shows (another player's build, a hangar ship's): a kit
        /// ship's for that build (normalized; null = its default build), any other ship's Database.MountsOf.</summary>
        public static List<WeaponMount> MountsShown(Database db, int ship, int slotType, Build build)
        {
            var c = KitShip(ship);
            var kit = KitOf(c);
            if (kit == null) return db.MountsOf(ship, slotType);
            var b = build != null ? Normalize(kit, c.kitType, build, DefaultBuild(c, kit)) : DefaultBuild(c, kit);
            return MountsFor(c, kit, b).Where(m => m.slotType == slotType).ToList();
        }

        /// <summary>The build an assembly shows: "ship_NNN_mod#<build>" (VariantAssembly) -> that build, else null.</summary>
        public static Build BuildOfAssembly(string assembly)
        {
            int hash = assembly?.IndexOf(VariantSeparator) ?? -1;
            return hash > 0 ? Build.Parse(assembly.Substring(hash + 1)) : null;
        }

        /// <summary>Another player's build of their kit ship from its text (NetPlayer.BuildText), normalized against what this
        /// game's kit has (unknown parts: the default build's); the default build for an empty text; null = no kit ship.</summary>
        public static Build RemoteBuild(int ship, string text)
        {
            var kit = CustomizationOf(ship);
            return kit == null ? null : Normalize(kit, TypeOf(ship), string.IsNullOrEmpty(text) ? null : Build.Parse(text), DefaultBuildOf(ship, kit));
        }

        public static List<WeaponMount> BuildMounts(int ship, int slotType)
        {
            var c = KitShip(ship);
            var kit = KitOf(c);
            if (kit == null) return null;
            var build = ship == Session.ShipIndex ? PlayerBuild(ship) : DefaultBuild(c, kit);
            return MountsFor(c, kit, build).Where(m => m.slotType == slotType).ToList();
        }

        /// <summary>The built-in weapon colours (bright, so the bolts glow in them).</summary>
        public static readonly List<ColorChoice> WeaponPalette = new List<ColorChoice>
        {
            Choice("red", "Red", "#FF3030"), Choice("orange", "Orange", "#FF8A1A"), Choice("yellow", "Yellow", "#FFE135"),
            Choice("lime", "Lime", "#9CFF2E"), Choice("green", "Green", "#2EFF6A"), Choice("cyan", "Cyan", "#2EF6FF"),
            Choice("blue", "Blue", "#3D7BFF"), Choice("violet", "Violet", "#9B4DFF"), Choice("magenta", "Magenta", "#FF3DE8"),
            Choice("pink", "Pink", "#FF7AB8"), Choice("white", "White", "#F4F8FF"),
        };

        /// <summary>A build's weapon colour (null = the weapons' own look).</summary>
        public static Color? WeaponColor(Kit kit, Build build) =>
            kit != null && build != null && ResolveWeaponColor(kit, build.weaponColor, out var c) ? c : (Color?)null;

        static readonly HashSet<string> LiveryFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "id", "name", "texture", "icon", "types", "color", "metallic", "smoothness", "opacity", "mirror", "scale", "offset", "tiling", "clipAngle" };

        static readonly HashSet<string> DecalFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "id", "name", "insignia", "type", "icon", "types" };

        static readonly HashSet<string> TrailFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "id", "name", "kind", "icon", "curves", "texture", "mask", "normal", "mesh" };

        // ---- builds ----------------------------------------------------------------------------------------------------

        /// <summary>One ship's choice of parts (slot -> part id) and its tier (which extension meshes show).</summary>
        public sealed class Build
        {
            public readonly SortedDictionary<string, string> parts = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            /// <summary>A colour value per colour slot (written "color.slot=value").</summary>
            public readonly SortedDictionary<string, string> colors = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public int tier;
            /// <summary>The skin's id (written "skin=id"); null (or NoSkin) = the model's own look.</summary>
            public string skin;
            /// <summary>The model's own look chosen where the default build has a skin.</summary>
            public const string NoSkin = "none";
            /// <summary>The weapon colour (written "weaponcolor=id" or "#RRGGBB"; null or "none" = the weapons' own).</summary>
            public string weaponColor;
            /// <summary>The exhaust style (ModExhausts, written "exhaust=mod_id:id"; null or "none" = the ship's own) and its two
            /// colours ("exhaustcolor=" / "exhaustcolor2=": a palette id of the style or "#RRGGBB"; null = the style's own).</summary>
            public string exhaust, exhaustColor, exhaustColor2;
            /// <summary>The exhaust style's size ("exhaustsize=1.2": x its fitted size, ModExhausts.MinSize .. MaxSize; null = 1).</summary>
            public string exhaustSize;
            /// <summary>The kit's weapon models, pylons and turret shown or not (written "weapons=off" / "on"; null = the default
            /// build's choice, else shown): only for a kit whose "hardpoints" have "toggle": true (ModHardpoints.Hidden).</summary>
            public string weapons;
            /// <summary>The livery (written "livery=id"; null = the skin's, "none" = none).</summary>
            public string livery;
            /// <summary>The decal (written "decal=id"; null = the skin's, "none" = none).</summary>
            public string decal;
            /// <summary>The engine sockets' and the booster sockets' trails (written "trail=id" / "boosttrail=id"; null = the
            /// Default veil).</summary>
            public string trail, boostTrail;
            public const string WeaponsOff = "off", WeaponsOn = "on";
            public bool WeaponsHidden => string.Equals(weapons, WeaponsOff, StringComparison.OrdinalIgnoreCase);
            const string ColorPrefix = "color.", SkinKey = "skin", WeaponColorKey = "weaponcolor", LiveryKey = "livery", DecalKey = "decal", TrailKey = "trail", BoostTrailKey = "boosttrail";
            const string ExhaustKey = "exhaust", ExhaustColorKey = "exhaustcolor", ExhaustColor2Key = "exhaustcolor2", ExhaustSizeKey = "exhaustsize", WeaponsKey = "weapons";

            /// <summary>Names a kit can't use for a slot: the build text's own keys.</summary>
            public static bool Reserved(string slot) => string.Equals(slot, "tier", StringComparison.OrdinalIgnoreCase)
                                                        || string.Equals(slot, "ship", StringComparison.OrdinalIgnoreCase)
                                                        || string.Equals(slot, SkinKey, StringComparison.OrdinalIgnoreCase)
                                                        || string.Equals(slot, WeaponColorKey, StringComparison.OrdinalIgnoreCase)
                                                        || string.Equals(slot, WeaponsKey, StringComparison.OrdinalIgnoreCase)
                                                        || string.Equals(slot, LiveryKey, StringComparison.OrdinalIgnoreCase)
                                                        || string.Equals(slot, DecalKey, StringComparison.OrdinalIgnoreCase)
                                                        || string.Equals(slot, TrailKey, StringComparison.OrdinalIgnoreCase)
                                                        || string.Equals(slot, BoostTrailKey, StringComparison.OrdinalIgnoreCase)
                                                        || slot.StartsWith(ExhaustKey, StringComparison.OrdinalIgnoreCase)
                                                        || slot.StartsWith(ColorPrefix, StringComparison.OrdinalIgnoreCase);

            public static Build Parse(string text)
            {
                var b = new Build();
                if (string.IsNullOrEmpty(text)) return b;
                foreach (var pair in text.Split(';'))
                {
                    int eq = pair.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = pair.Substring(0, eq).Trim(), v = pair.Substring(eq + 1).Trim();
                    if (k == "tier") int.TryParse(v, out b.tier);
                    else if (k == SkinKey) b.skin = v.Length > 0 ? v : null;
                    else if (k == WeaponColorKey) b.weaponColor = v.Length > 0 ? v : null;
                    else if (k == ExhaustKey) b.exhaust = v.Length > 0 ? v : null;
                    else if (k == ExhaustColorKey) b.exhaustColor = v.Length > 0 ? v : null;
                    else if (k == ExhaustColor2Key) b.exhaustColor2 = v.Length > 0 ? v : null;
                    else if (k == ExhaustSizeKey) b.exhaustSize = v.Length > 0 ? v : null;
                    else if (k == WeaponsKey) b.weapons = v.Length > 0 ? v.ToLowerInvariant() : null;
                    else if (k == LiveryKey) b.livery = v.Length > 0 ? v : null;
                    else if (k == DecalKey) b.decal = v.Length > 0 ? v : null;
                    else if (k == TrailKey) b.trail = v.Length > 0 ? v : null;
                    else if (k == BoostTrailKey) b.boostTrail = v.Length > 0 ? v : null;
                    else if (k.StartsWith(ColorPrefix)) { if (v.Length > 0) b.colors[k.Substring(ColorPrefix.Length)] = v; }
                    else if (v.Length > 0) b.parts[k] = v;
                }
                return b;
            }

            /// <summary>ships.json's "build" object.</summary>
            public static Build FromJson(JObject o)
            {
                var b = new Build();
                if (o == null) return b;
                foreach (var prop in o.Properties())
                {
                    if (string.Equals(prop.Name, "tier", StringComparison.OrdinalIgnoreCase)) { b.tier = prop.Value.Type == JTokenType.Integer ? (int)prop.Value : 0; continue; }
                    if (string.Equals(prop.Name, SkinKey, StringComparison.OrdinalIgnoreCase)) { b.skin = prop.Value.Type == JTokenType.String ? (string)prop.Value : null; continue; }
                    if (string.Equals(prop.Name, "weaponColor", StringComparison.OrdinalIgnoreCase)) { b.weaponColor = prop.Value.Type == JTokenType.String ? (string)prop.Value : null; continue; }
                    if (string.Equals(prop.Name, LiveryKey, StringComparison.OrdinalIgnoreCase)) { b.livery = prop.Value.Type == JTokenType.String ? (string)prop.Value : null; continue; }
                    if (string.Equals(prop.Name, DecalKey, StringComparison.OrdinalIgnoreCase)) { b.decal = prop.Value.Type == JTokenType.String ? (string)prop.Value : null; continue; }
                    if (string.Equals(prop.Name, TrailKey, StringComparison.OrdinalIgnoreCase)) { b.trail = prop.Value.Type == JTokenType.String ? (string)prop.Value : null; continue; }
                    if (string.Equals(prop.Name, "boostTrail", StringComparison.OrdinalIgnoreCase)) { b.boostTrail = prop.Value.Type == JTokenType.String ? (string)prop.Value : null; continue; }
                    if (string.Equals(prop.Name, ExhaustKey, StringComparison.OrdinalIgnoreCase)) { b.exhaust = prop.Value.Type == JTokenType.String ? (string)prop.Value : null; continue; }
                    if (string.Equals(prop.Name, "exhaustColor", StringComparison.OrdinalIgnoreCase)) { b.exhaustColor = prop.Value.Type == JTokenType.String ? (string)prop.Value : null; continue; }
                    if (string.Equals(prop.Name, "exhaustColor2", StringComparison.OrdinalIgnoreCase)) { b.exhaustColor2 = prop.Value.Type == JTokenType.String ? (string)prop.Value : null; continue; }
                    if (string.Equals(prop.Name, "exhaustSize", StringComparison.OrdinalIgnoreCase))
                    {
                        b.exhaustSize = prop.Value.Type == JTokenType.Float || prop.Value.Type == JTokenType.Integer
                            ? ((float)prop.Value).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                            : prop.Value.Type == JTokenType.String ? (string)prop.Value : null;
                        continue;
                    }
                    if (string.Equals(prop.Name, WeaponsKey, StringComparison.OrdinalIgnoreCase))
                    {
                        // "weapons": false / "off" hides them, true / "on" shows them.
                        b.weapons = prop.Value.Type == JTokenType.Boolean ? ((bool)prop.Value ? WeaponsOn : WeaponsOff)
                                  : prop.Value.Type == JTokenType.String ? ((string)prop.Value).ToLowerInvariant() : null;
                        continue;
                    }
                    if (string.Equals(prop.Name, "colors", StringComparison.OrdinalIgnoreCase) && prop.Value is JObject co)
                    {
                        foreach (var c in co.Properties()) if (c.Value.Type == JTokenType.String) b.colors[c.Name] = (string)c.Value;
                        continue;
                    }
                    if (prop.Value.Type == JTokenType.String) b.parts[prop.Name] = (string)prop.Value;
                }
                return b;
            }

            public Build Clone()
            {
                var b = new Build { tier = tier, skin = skin, weaponColor = weaponColor, exhaust = exhaust, exhaustColor = exhaustColor, exhaustColor2 = exhaustColor2, exhaustSize = exhaustSize, weapons = weapons, livery = livery, decal = decal, trail = trail, boostTrail = boostTrail };
                foreach (var kv in parts) b.parts[kv.Key] = kv.Value;
                foreach (var kv in colors) b.colors[kv.Key] = kv.Value;
                return b;
            }

            public override string ToString() => string.Join(";", parts.Select(kv => kv.Key + "=" + kv.Value)
                .Concat(colors.Select(kv => ColorPrefix + kv.Key + "=" + kv.Value))
                .Concat(string.IsNullOrEmpty(skin) ? Enumerable.Empty<string>() : new[] { SkinKey + "=" + skin })
                .Concat(string.IsNullOrEmpty(weaponColor) ? Enumerable.Empty<string>() : new[] { WeaponColorKey + "=" + weaponColor })
                .Concat(string.IsNullOrEmpty(exhaust) ? Enumerable.Empty<string>() : new[] { ExhaustKey + "=" + exhaust })
                .Concat(string.IsNullOrEmpty(exhaustColor) ? Enumerable.Empty<string>() : new[] { ExhaustColorKey + "=" + exhaustColor })
                .Concat(string.IsNullOrEmpty(exhaustColor2) ? Enumerable.Empty<string>() : new[] { ExhaustColor2Key + "=" + exhaustColor2 })
                .Concat(string.IsNullOrEmpty(exhaustSize) ? Enumerable.Empty<string>() : new[] { ExhaustSizeKey + "=" + exhaustSize })
                .Concat(string.IsNullOrEmpty(weapons) ? Enumerable.Empty<string>() : new[] { WeaponsKey + "=" + weapons })
                .Concat(string.IsNullOrEmpty(livery) ? Enumerable.Empty<string>() : new[] { LiveryKey + "=" + livery })
                .Concat(string.IsNullOrEmpty(decal) ? Enumerable.Empty<string>() : new[] { DecalKey + "=" + decal })
                .Concat(string.IsNullOrEmpty(trail) ? Enumerable.Empty<string>() : new[] { TrailKey + "=" + trail })
                .Concat(string.IsNullOrEmpty(boostTrail) ? Enumerable.Empty<string>() : new[] { BoostTrailKey + "=" + boostTrail }).Append("tier=" + tier));
        }

        // ---- the ships -------------------------------------------------------------------------------------------------

        public const char VariantSeparator = KitRules.VariantSeparator;

        public const string ShipToken = KitRules.ShipToken;

        // ---- customizations of ships without a kit ---------------------------------------------------------------------

        static readonly Dictionary<int, Kit> shipCustom = new Dictionary<int, Kit>();
        static int customRevision = -1;

        static readonly Dictionary<int, Kit> exhaustOnly = new Dictionary<int, Kit>();

    }
}
