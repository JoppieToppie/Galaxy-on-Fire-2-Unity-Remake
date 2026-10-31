// Database.cs
// Loads the JSON exported from the original game data (see README) and turns ship + equipment
// into FlightStats for ShipController.
//
// The JSON lives in Assets/Resources/GoF2Data/ and is parsed with Unity's built-in JsonUtility
// (no extra packages needed).   var db = Database.Load();  var betty = db.ShipByName("Betty");

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using GoF2Remake.Flight;

namespace GoF2Remake.Data
{
    [System.Serializable] public class ShipSlots { public int primary, secondary, turret, equipment; }

    [System.Serializable] public class ShipData
    {
        public int index;
        public string name, description;
        public int armor, cargo, price;
        public ShipSlots slots;
        public float handling;            // UI value, e.g. 120
        public float handlingMultiplier;  // handling / 100

        /// <summary>Remake mods (Modding.ModFlight, ships.json "flightStyle" / "flight"): the flight style this ship always
        /// flies with (-1 = the player's), and its bonuses for both styles / the original / free flight (null = none).</summary>
        [System.NonSerialized] public int flightStyle = -1;
        [System.NonSerialized] public FlightBonus flightBoth, flightOriginal, flightFree;

        /// <summary>Remake mods (Modding.ModShipView, ships.json "camera" / "cockpit" / "smoke" / "explosionScale"; null /
        /// 1 = the original's for every ship): the chase camera in game units (distance behind the pivot, height, the look
        /// point's height and how far ahead of the pivot it is: the original's 1338 / 600 / 600 / -650), the cockpit view's
        /// seat and the damage smoke's points (game units in position_engine's axes, like the mounts) and the explosion's
        /// size when the ship dies (x the fighters' 1).</summary>
        [System.NonSerialized] public float[] chaseCamera;
        [System.NonSerialized] public Vector3? cockpit;
        [System.NonSerialized] public List<Vector3> smokePoints;
        [System.NonSerialized] public float explosionScale = 1f;
    }

    [System.Serializable] public class StatEntry { public string key; public int value; }

    [System.Serializable] public class BlueprintPart { public int item; public string name; public int amount; }

    [System.Serializable] public class ItemData
    {
        public int index;
        public string name, description;
        public string type;       // primary | secondary | turret | equipment | commodity
        public string category;   // Laser, Blaster, Shield, Booster, ...
        public int categoryId;
        public int techLevel, occurrence, minPrice, maxPrice, lowestPriceSystem, highestPriceSystem;
        public List<StatEntry> statList = new List<StatEntry>();   // e.g. damage, range, boostSpeed ...
        public List<BlueprintPart> blueprint = new List<BlueprintPart>();

        /// <summary>Original attribute pairs (Item+0x30), from item_attributes.json; see Reference/research/shop.md 2.4.</summary>
        [System.NonSerialized] public int[] attrKeys = new int[0], attrValues = new int[0];

        /// <summary>A mod's item (Modding.ModContent): its "mod:id" key, and the original item it looks and behaves like
        /// (icon, weapon fx, sounds, the special rules the code keys on the item's number). Not modded = its own.</summary>
        [System.NonSerialized] public bool modded;
        [System.NonSerialized] public string modKey;
        [System.NonSerialized] public int lookIndex;
        public int Look => modded ? lookIndex : index;

        public bool HasAttr(int id) => System.Array.IndexOf(attrKeys, id) >= 0;
        public int Attr(int id, int fallback = 0)
        {
            int i = System.Array.IndexOf(attrKeys, id);
            return i >= 0 ? attrValues[i] : fallback;
        }

        /// <summary>Attribute 1: 0 primary, 1 secondary, 2 turret, 3 equipment, 4 commodity.</summary>
        public int TypeId => Attr(1, type switch { "primary" => 0, "secondary" => 1, "turret" => 2, "equipment" => 3, _ => 4 });

        public int Stat(string key, int fallback = 0)
        {
            if (statList != null) foreach (var s in statList) if (s.key == key) return s.value;
            return fallback;
        }
    }

    [System.Serializable] public class MapPos { public int x, y, z; }

    [System.Serializable] public class SystemData
    {
        public int index;
        public string name;
        public int securityLevel;
        public bool initiallyVisible;
        public string race;
        public int raceId;
        public MapPos mapPosition;
        public int jumpgateStation;
        public int textureIndex;
        public List<int> stations, jumpRoutesTo, unknownTriple, forbiddenGoodsOrUnknown;
    }

    [System.Serializable] public class StationData
    {
        public int index;
        public string name;
        public int system;
        public string systemName;
        public int techLevel;
        public int textureIndex;
    }

    /// <summary>Weapon mounts of one ship (weapons_hd.json). slotType 0 = primary, 1 = secondary, 2 = turret,
    /// 3 = engine exhaust points (not a turret, see weapons.md); remake mods: 4 = a manoeuvring jet, 5 = a booster point
    /// (EVERSPACE 2's). position_engine is game space, ship-relative.</summary>
    /// <remarks>upsideDown (remake, turret mounts only): the turret hangs under the hull, turned 180 deg about the ship's
    /// length (PlayerTurret); none of the original's mounts set it. turretUp (remake, turret mounts): the direction the
    /// turret's base faces, game space like position_engine (x toward the pilot's left, y up, z forward; any length), so a
    /// turret sits on the sides, the nose or at any angle; it wins over upsideDown (= [0, -1, 0]). glowColor / glowSize (remake, a mod ship's exhaust mounts):
    /// the engine flame's colour (RGB 0..1, ModShipBuilder's tinted glow and the nearest-coloured exhaust particle cell) and
    /// shape (half width, half height, flame length in game units; none = the round glow of CustomShipData.engineGlowRadius).</remarks>
    [System.Serializable] public class WeaponMount
    {
        public int slotType; public int[] position_engine; public float[] turretAngles; public bool upsideDown;
        public float[] glowColor, glowSize, turretUp;

        /// <summary>turretUp (else upsideDown) in the Unity ship frame ((-x, y, z), the models are mirrored): a unit vector,
        /// Vector3.up for a plain mount.</summary>
        public Vector3 TurretUpLocal
        {
            get
            {
                var up = turretUp != null && turretUp.Length >= 3 ? new Vector3(-turretUp[0], turretUp[1], turretUp[2])
                    : upsideDown ? Vector3.down : Vector3.up;
                return up.sqrMagnitude < 1e-8f ? Vector3.up : up.normalized;
            }
        }

        /// <summary>The turret's turn on this mount in the ship's frame: its up onto TurretUpLocal by the shortest turn, and
        /// straight down as a half turn about the ship's length (the upsideDown turret's, so its rest heading stays forward).</summary>
        public Quaternion TurretTurn
        {
            get
            {
                var up = TurretUpLocal;
                return up.y < -0.9999f ? Quaternion.Euler(0f, 0f, 180f) : Quaternion.FromToRotation(Vector3.up, up);
            }
        }

        /// <summary>The turret's base faces below the ship's horizon: its turret view hangs the camera under it, upright.</summary>
        public bool TurretHangs => TurretUpLocal.y < -0.01f;

        /// <summary>Sets turretUp (game frame), normalised and rounded; upsideDown dropped (turretUp says it). The Mod Builder:
        /// with 'snap' a direction within 15 deg of one of the ship's axes takes that axis (a hull's surface is seldom exact).</summary>
        public void SetTurretUp(Vector3 game, bool snap = false)
        {
            if (game.sqrMagnitude < 1e-8f) return;
            game.Normalize();
            if (snap)
                foreach (var axis in new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
                    if (Vector3.Dot(game, axis) > 0.9659f) { game = axis; break; }   // cos 15 deg
            turretUp = new[] { Mathf.Round(game.x * 1000f) / 1000f, Mathf.Round(game.y * 1000f) / 1000f, Mathf.Round(game.z * 1000f) / 1000f };
            upsideDown = false;
        }
        /// <summary>Remake mods: an exhaust mount's nozzle outline, [x0, y0, x1, y1, ...] in game units around the mount
        /// (position_engine's axes, the mount's plane): the exhaust styles take its shape (Modding.ModExhausts).</summary>
        public float[] outline;
        /// <summary>Remake mods: a turret mount whose turret is part of the ship's own model (a customizable ship's turret
        /// socket): the turret item's gun aims and fires from here, no turret model of the game is put on it (PlayerTurret).</summary>
        public bool builtIn;
        /// <summary>Remake mods, slotType 4 (a manoeuvring jet, EVERSPACE 2's lateral thrusters, Modding.LateralThrusters):
        /// the way it blows, in position_engine's convention.</summary>
        public float[] direction;
        /// <summary>Remake mods, a primary / secondary mount of a customizable ship: how its weapon model (Modding.ModHardpoints)
        /// is turned on the hull, in the ship model's frame (x right, y up, z forward): Euler degrees [x, y, z] (Unity's order)
        /// or a quaternion [x, y, z, w]; none = upright (upsideDown still turns it over). Only the look and the muzzle: the
        /// shots still fly along the nose. An exhaust mount (slotType 3) of a ship with "engineFx": the way it blows, turned
        /// from straight back (Modding.EngineFx).</summary>
        public float[] rotation;
        /// <summary>Remake mods, a primary / secondary mount: nodes of the ship's model (by name) that show only while this mount
        /// carries a weapon (a hardpoint's pylon; Modding.ModHardpoints.AttachWeapons).</summary>
        public string[] nodes;
        /// <summary>Remake mods, an exhaust mount (slotType 3) of a ship with "engineFx": that effect's size here (GoF3's engine
        /// helper scale; 0 = 1).</summary>
        public float fxScale;
        /// <summary>Remake mods, an exhaust mount (slotType 3) or a booster point (slotType 5, EVERSPACE 2's SKT_Booster sockets):
        /// the socket's scale in EVERSPACE 2 (its largest axis; 0 = 1), which sizes the engine light, trail and glow there
        /// (Modding.Es2EngineVfx).</summary>
        public float socketScale;
    }
    [System.Serializable] public class WeaponMountSet { public int ship; public string shipName; public List<WeaponMount> mounts; }

    /// <summary>A mod's ship (a ships.json entry, Modding.ModContent; the format of PR #34's custom_ships.json): a ships.json
    /// entry plus what the original keeps in its fixed tables (race, hangar height), the weapon mounts and how to build its
    /// model at run time (Modding.ModShipBuilder). Paths are inside the mod.</summary>
    [System.Serializable] public class CustomShipData : ShipData
    {
        public int race = -1;              // 0 Terran, 1 Vossk, 2 Nivelian, 3 Midorian, 8 pirate, 9 void (Shop.ShipRace)
        public int priceDefault;           // the Default Economy's price (0 = the same as 'price')
        public int hangarHeight = 250;     // StationTables.ShipY: pivot height above the hangar floor, game units
        public string assembly;            // set by ModContent: "ship_NNN_mod" (Database.ShipAssembly finds it by number)
        public List<WeaponMount> mounts;   // like weapons_hd.json: slotType 0 primary, 1 secondary, 2 turret, 3 exhaust, 4 manoeuvring jet
        public string model;               // the glTF / GLB file in the mod, or "gof2:<model>" (Modding.GameModels)
        public string icon;                // the shop icon (PNG, 180 x 88 like the originals); none = the Phantom's
        /// <summary>Remake (Modding.GameModels): with a "gof2:" model, the original ship that model belongs to (its hangar
        /// height and icon when the entry gives none), else -1; hangarHeight -1 = take that ship's.</summary>
        [System.NonSerialized] public int gameModelShip = -1;
        public float modelLength = 1000f;  // nose to tail in game units (0.05 m each) after scaling
        public float modelYaw;             // degrees about Unity y, when the model's nose doesn't face +Z
        public float modelPitch, modelRoll; // degrees about Unity x / z, for a model lying on its side or nose up in its file
        public float[] modelOffset;        // game units (x, y, z in the ship's frame like the mounts): the sized model moved off the pivot
        public float engineGlowRadius = 24f;   // game units, the glow disc at each exhaust mount
        public float[] engineGlowColor;    // RGB 0..1: every exhaust's flame colour (a mount's glowColor wins); none = the original glow
        public List<CustomShipMaterial> materials;   // replace the model's materials on the renderers / submeshes they name
        public CustomThrottleGlow throttleGlow;   // a glow on part of the hull that follows the throttle (no mask = none)
        public List<CustomThrottleGlow> extraGlows;   // more of them (each its own mask, colour, levels and trail)
        public CustomEngineFx engineFx;    // remake mods: Galaxy on Fire 3's engine effect at every exhaust (Modding.EngineFx); null = none
        public List<CustomAddPart> addParts;   // remake mods: the original's additive layers (lights, engine glows) beside the hull
        public CustomLoungeSeller lounge;  // a lounge visitor who sells it (AgentGenerator.AddCustomShipSellers); null = none
        public CustomDealer dealer;        // remake mods: ship dealers may stock it (Modding.ModUnlocks.AddDealerShips); null = never
        public CustomTraffic traffic;      // remake mods: how NPC fighters fly it (Modding.ModTraffic); null = its race's, weight 1
        public string kit, kitType;        // remake mods: a customizable ship (Modding.ModShipKits): its kit ("mod_id:kit_id") and type
        [System.NonSerialized] public string defaultBuild;   // its "build" as build text (ModShipKits.Build)
        // Remake mods: a capital ship's subsystems (ships.json "subsystems", World.CapitalSubsystems; resolved into
        // Database.ShipSubsystems): while any part with "shields" stands the hull takes 'shieldedDamage' of every hit (0 =
        // none) and its renderers with a material named like 'shieldMaterial' show (its energy fields), hidden once they fall.
        public string shieldMaterial;
        public float shieldedDamage;
        public float hullFactor = 1f;     // remake mods: a mod capital ship's hull x this (Modding.ModCapitalShips.Spawn)
        /// <summary>"es2": EVERSPACE 2's engine effects (Modding.Es2EngineVfx: the glow, trails and lights at the exhaust and
        /// booster mounts) without flame materials of its own and without the glow discs (a capital ship's sockets).</summary>
        public string engineStyle;
        /// <summary>Which of EVERSPACE 2's engine effects an "es2" ship (or one with flames) shows (ships.json "es2Engine";
        /// null = all; Modding.Es2EngineVfx).</summary>
        public CustomEs2Engine es2Engine;
        /// <summary>A mod capital ship that jumps away when its hull is depleted instead of being destroyed (World.CapitalRetreat:
        /// EVERSPACE 2's Dreadnought), so an encounter with it can come again.</summary>
        public bool retreat;
    }

    /// <summary>Remake mods: EVERSPACE 2's per-ship switches of its engine effects (a ship blueprint's SpawnEngineGlow /
    /// SpawnEngineTrails; EVERSPACE 2's warships and freighters have them off, and EngineVFXEnabled false turns both off):
    /// 'glow' the camera-facing glow at each socket (an NPC's), 'trails' its trail (an NPC's) and the player's veils.</summary>
    [System.Serializable] public class CustomEs2Engine
    {
        public bool glow = true;
        public bool trails = true;
    }

    /// <summary>Remake mods: one subsystem of a capital ship (ships.json "subsystems"; World.CapitalSubsystems): a target of
    /// its own on the hull that dies with it. 'kind' "turret" (a turret object of turret item 'item'), "object" (a fixed object
    /// of 'model': "gof2:<assembly>" or a ship, a number or a mod's "mod:id", whose model it shows) or "hangar" (an object
    /// that launches 'ships' (numbers or "mod:id", resolved when they launch) while its capital ship fights the player). 'position' game units in the ship's frame like a mount's
    /// position_engine, 'up' the way it faces (turretUp's axes). 'shields': the hull is shielded while it stands; 'weakspot'
    /// &gt; 0: hits on it also go to the hull x that, and its death takes 'bonus' of the hull's maximum; 'stage' &gt; 0: it can't
    /// be harmed while a part of a lower stage stands. 'hull' its hit points (-1: by kind).</summary>
    public class CapitalSubsystem
    {
        public string kind = "object", name, model;
        public Vector3 position, up = Vector3.up;
        public int item = -1, hull = -1, stage;
        public bool shields;
        public float weakspot, bonus, scale = 1f, radius = -1f;
        public List<string> ships = new List<string>();
        public float everyMs = 25000f;
        public int perWave = 2, maxAlive = 6, total = -1;
    }

    /// <summary>Remake mods: one of the original's additive layers a ship model brings beside its hull (ships.json "addParts";
    /// Modding.ModShipBuilder), in the hull model's own frame (the original's parts share its origin), drawn like the game's
    /// ships draw theirs: 'role' "lights" (the *_lights_add: always shown, the race's glow material 3460x), "engineGlow" (the
    /// player's *_engine_glow_add: with the engines, in place of the generated glow discs, mat_34813) or "engine" (the NPCs'
    /// *_engine_add: the NPC variant, the race's glow material). 'texture' (a PNG / .aei in the mod) replaces the material's
    /// texture; none = the game's (which skins of that texture still replace).</summary>
    [System.Serializable] public class CustomAddPart
    {
        public string model;
        public string role;
        public string texture;

        public const string Lights = "lights", EngineGlow = "engineGlow", Engine = "engine";
        public static readonly string[] Roles = { Lights, EngineGlow, Engine };
        /// <summary>The game's name ending for the role's part ("_lights_add" ...), which the hull-only code skips.</summary>
        public static string Suffix(string role) => role == EngineGlow ? "_engine_glow_add" : role == Engine ? "_engine_add" : "_lights_add";
    }

    /// <summary>Remake mods: how often NPC fighters fly a mod ship (Modding.ModTraffic, the "Mod ships in traffic" option):
    /// 'weight' against the race's other mod ships (0 = never), 'races' the races whose fighters fly it (none = its own),
    /// 'randomBuild' whether a kit ship's NPCs fly random builds of it.</summary>
    [System.Serializable] public class CustomTraffic
    {
        public float weight = 1f;
        public int[] races;
        /// <summary>A customizable (kit) ship: each NPC flies a random build of it (parts, tier, colours, skin); false = the
        /// ship's default build.</summary>
        public bool randomBuild = true;
        /// <summary>A boss hull: the pirate bosses (TrafficPlan's pirate events) fly it now and then (ModTraffic.PickBoss), and
        /// it never flies as an ordinary fighter. Up to ModTraffic.MaxBossLength long.</summary>
        public bool boss;
        /// <summary>A capital ship (Modding.ModCapitalShips): "battleship" = a parked warship in the orbits' capital ship special
        /// (instead of the Terran battleship / the Vossk battleship, and in Nivelian / Midorian systems, which have none),
        /// "freighter" = it cruises among the orbits' freighters. Either way it never flies as a fighter. Empty = a fighter.</summary>
        public string capital;
    }

    /// <summary>Remake mods: a mod ship in the ordinary ship dealers' lists (Shop.GenerateShips): each time a station's dealer
    /// list is made, 'chance' % (0.1 steps; 100 = always) at stations of 'systemRace' systems (-1 = any) with a tech level
    /// of at least 'minTechLevel'.</summary>
    [System.Serializable] public class CustomDealer
    {
        public float chance = 2f;
        public int systemRace = -1;
        public int minTechLevel;
    }

    /// <summary>When a lounge may have a visitor selling a custom ship (AgentOffer.SellShip): each time a station's bar is
    /// generated (Generator::createAgents: a station not among the last 3 visited), 'chance' % in systems of 'systemRace'
    /// (-1 = any), from campaign step 'minCampaign' (free play: from rank 'minRank'), unless the player flies or stores it.</summary>
    [System.Serializable] public class CustomLoungeSeller
    {
        public int systemRace = -1;
        public int minCampaign;
        public int minRank;
        public int chance = 10;
    }

    /// <summary>A URP Lit material for the renderers whose name contains 'mesh' (empty = any) and, when 'submesh' >= 0,
    /// only for that submesh (one mesh with several materials); paths inside the mod. 'color' (RGB) tints the
    /// diffuse (or is the colour without one); 'metallic' >= 0 sets the metalness without a mask. 'emission' is an
    /// emission map and / or 'emissionColor' (RGB) its colour, x emissionIntensity (> 1 blooms); 'alphaClip' > 0 cuts the
    /// diffuse's alpha below it (decals). 'detailAlbedo' (linear, 0.5 = neutral) / 'detailNormal' are URP's detail maps,
    /// tiled 'detailTiling' times over the UVs (e.g. brushed metal).</summary>
    [System.Serializable] public class CustomShipMaterial
    {
        public string mesh, diffuse, normal, metallicSmoothness, emission, detailAlbedo, detailNormal;
        public string material;   // remake mods: only where the model's own material's name contains this (empty = any)
        public int submesh = -1;
        public float[] color, emissionColor;
        public float smoothness = 1f, metallic = -1f, emissionIntensity = 1f, alphaClip, normalScale = 1f;
        public float detailTiling = 1f, detailNormalScale = 1f;
        public float tiling = 1f;  // remake mods: the main textures repeat this many times over the UVs (EVERSPACE 2's plating 'Tiling')
        public float opacity;   // 0 / 1 = opaque; below 1 a see-through surface (glass: premultiplied, so reflections stay bright)
        public bool doubleSided;   // both faces drawn (models with one-sided panels the camera can see from behind)
        public bool engine;        // its emission shows only while the engines run (dark on a ship parked in a hangar)
        // With "engine": its share of the glow at throttle 0 (0..1); 1 = steady like the game's own engine glow, lit
        // whenever the engines run (dark on a parked ship, full through the take-off and all flight).
        public float engineIdle;
        public bool boost;         // its emission shows only while boosting (a boost vent; dark otherwise)
        public bool livery;        // a kit's liveries are projected onto it (EVERSPACE 2's hull and tech details; ModShips.ApplyLivery)
        public string decal;       // "insignia" / "type": a kit decal's mask goes on it (the parts' decal faces; ModShips.ApplyDecals)
        // A hologram projector (GoF2/ModHologram): additive rings whose emission pattern turns and which bob; UV1 x a ring's
        // spin (-1..1), y its share of the hover; emissionColor x emissionIntensity and emission (the pattern) as usual.
        public bool hologram;
        // A nozzle shimmer (GoF2/ModNozzleShimmer): an additive layer over the nozzle's glowing faces, the glow pattern
        // (emission) times flameNoise scrolling along it; with "engine" it follows the engines like the glow.
        public bool shimmer;
        // Battle damage embers: a mask (PNG) set as this material's emission when it has none, glowing ember-orange with the
        // ship's hull damage (DamageEmbers: from a third of the hull lost, flickering).
        public string damageEmission;
        // Battle damage charring: a detail colour (PNG, its alpha the burn mask) and normal map blended in with the hull
        // damage (URP Lit's detail layer, DamageEmbers raising its strength); dark, matte-looking burn patches under the embers.
        public string damageDetail, damageNormal;
        // An engine flame (GoF2/ModEngineFlame, Modding.EngineFlame): the surfaces are flame stubs whose UV u runs from the
        // nozzle (0) to the tip (1), stretched backward by the throttle; emissionColor x emissionIntensity is its colour.
        public bool flame;
        public float flameLength = 5f;     // model units at full throttle (the stub's own length comes on top)
        public float flameIdle = 0.15f;    // the length at idle, a share of flameLength
        public string flameNoise;          // a PNG in the mod (grey noise, tiling), streaked along the flame
        public float[] boostColor;         // RGB 0..1 while boosting (default a hot blue), x emissionIntensity
    }

    /// <summary>A mod ship's throttle-driven glow (Modding.ModShipBuilder, ThrottleGlow): the
    /// hull triangles of the renderers whose name contains 'mesh' (empty = any) and of 'submesh' (-1 = all) whose UVs
    /// touch the lit part of 'mask' (a PNG in the mod; a plain white mask takes them all), copied
    /// 'offset' game units out along their normals and drawn additive with the mask, tinted 'color' (RGB); the glow
    /// intensity runs from 'idle' (throttle 0) to 'full' (throttle 100 %) and up to 'boost' while boosting.</summary>
    [System.Serializable] public class CustomThrottleGlow
    {
        public int submesh = -1;
        public string mask, mesh;
        public float[] color;
        public float idle = 0.35f, full = 4f, boost = 7f, offset = 0.5f;
        public float off = -1f;           // remake mods: the level while parked in a hangar (engines off); below 0 = idle
        public float trailWidth;          // game units; > 0: a trail in the glow's colour from the glow's rear ends while
        public float trailTime = 0.6f;    // boosting or travelling (planet jump, jumpgate, Khador Drive), 'trailTime' s long
        public float trailBrightness = 0.3f;   // the trail x the glow's level
        public int trailCount;            // 0 = one trail at each side's rear end; > 0 = that many along the glow's whole
                                          // rear edge, shaped like an ellipse across it: 'trailWidth' / 'trailTime' /
                                          // brightness in the middle, less toward the ends (the Millennium Falcon's band)
    }

    /// <summary>One assembled prefab (assemblies.json): Resources/Assembled/{pack}/{category}/{name}.prefab.</summary>
    /// <summary>Remake mods: an engine effect like Galaxy on Fire 3's at every exhaust mount (slotType 3; Modding.EngineFx), in
    /// place of the glow disc and the exhaust particles. Up to three parts, each a PNG in the mod (additive: black shows
    /// nothing), in the mount's glowColor (else the ship's engineGlowColor, else 'color'); sizes in game units. Each follows the
    /// engines like the other engine effects (EngineDrive: off while the engine part is, longer with the throttle and boost).</summary>
    /// <summary>Remake mods: Galaxy on Fire 3's engine effect at every exhaust (ships.json / an exhaust style's / a mobile
    /// station's "engineFx", Modding.EngineFx), as GoF3 draws it: its engine scene's parts and its Fork particles.</summary>
    [System.Serializable] public class CustomEngineFx
    {
        /// <summary>The nozzle radius an engine is drawn for when its "nozzle" isn't given (game units: GoF3's Nivelian
        /// exhaust, 1.62 GoF3 units x 53.5).</summary>
        public const float DefaultNozzle = 87f;
        public string meshes;                // GoF3's engine scene (Modding.EngineModel: its parts' meshes, textures and shader
                                             // options, from the GoF3 importer)
        public float meshScale = 1f;         // game units per unit of the meshes (GoF3's: 53.5)
        public float meshGlow = 1f;          // x the meshes' brightness (GoF3 draws them into an 8-bit frame: 1 = never past
                                             // white, more blooms)
        public float nozzle;                 // the nozzle radius the effect is drawn for (game units; 0 = DefaultNozzle):
                                             // exhaust styles are sized against it, on this ship and as a style (Modding.ModExhausts)
        public CustomForkEmitter trailParticles;     // GoF3's engine trail as its Fork particles, at each exhaust
        public CustomForkEmitter contrailParticles;  // GoF3's contrails, at the "contrails" points
        public CustomForkEmitter boostParticles;     // GoF3's booster flash at each exhaust, emitted only while boosting
        public float[] contrails;            // [x, y, z, ...] game units from the ship's centre (position_engine's axes)
        public bool Any => !string.IsNullOrEmpty(meshes) || trailParticles?.Any == true || contrailParticles?.Any == true || boostParticles?.Any == true;
        public float NozzleRadius => nozzle > 0f ? nozzle : DefaultNozzle;
    }

    /// <summary>Remake mods: one Fork Particle Studio emitter of Galaxy on Fire 3 (its .psb's EData, the GoF3 importer's
    /// tools/fork_psb.py), as GoF3's Fork runtime runs it (Modding.EngineFx): particles born at the emitter at 'rate' a second,
    /// spread along the path it moved, still where they were born (speed 0: a wake), each living 'life' seconds, sized
    /// start -> mid (at 'sizeMidTime' of its life) -> end, coloured through four keys at 'colorTimes', drawn additively
    /// with 'texture' stretched along the wake. Sizes in the units of the engineFx's "meshScale".</summary>
    [System.Serializable] public class CustomForkEmitter
    {
        public string texture;               // a PNG in the mod
        public float[] life;                 // [min, max] seconds
        public float[] sizeStart, sizeMid, sizeEnd;   // [min, max]
        public float sizeMidTime = 0.5f;
        public float[] colors;               // 4 x rgba (0..1, gamma as GoF3 draws them)
        public float[] colorTimes;           // 4 shares of the life (0, a, b, 1)
        public float rate = 30f;             // particles a second
        public int max = 100;                // the most alive per emitter
        public bool local;                   // the particles ride with the emitter (else they stay where they were born)
        public bool Any => !string.IsNullOrEmpty(texture) && life != null && life.Length >= 1 && life[0] > 0f;
    }

    [System.Serializable] public class AssemblyData
    {
        public string name, pack, category, origin;
        public int root;
    }

    /// <summary>A Most Wanted criminal (wanted.json = FileRead::loadWanted, Wanted 0x14805c; wingmen_wanted.md 2.1).</summary>
    [System.Serializable] public class WantedData
    {
        public int index;
        public string name;
        public int board, race;
        public bool male;
        public int ship, weapon, hitpoints, loot, lootAmount, reward, requiredBounties, requiredMission, numWingmen;
        public int[] portraitParts;
    }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class Database
    {
        public List<ShipData> Ships = new List<ShipData>();
        public List<ItemData> Items = new List<ItemData>();
        public List<SystemData> Systems = new List<SystemData>();
        public List<StationData> Stations = new List<StationData>();
        public List<AssemblyData> Assemblies = new List<AssemblyData>();

        /// <summary>Globals::getShipGroup: a ship's assembled object, the main pack's "ship_NNN_*" first, else the add-ons'
        /// "v_ship_NNN_*" / "sn_ship_NNN_*" (ships 39-41 and 44+); the variant ending in 'raceName' when there is one.
        /// 'original': the game's own model even when a mod replaces it (the stand-in while the mod's is built).</summary>
        public AssemblyData ShipAssembly(int ship, string raceName = null, bool original = false)
        {
            // A mod's ship on one of the game's own models (Modding.GameModels), else a mod's model for this ship (a new mod
            // ship, or an original's replaced: ModContent.ModelOverrides).
            // With textures of its own (ShipOwnTextures) it shows a copy of that model of its own, "<model>#ship=N", which
            // Modding.ModTextures gives the ship's textures/ships/<id>/ files: the game's ship of that model keeps its look.
            if (!original && ShipModels.TryGetValue(ship, out var gameModel) && AssemblyByName(gameModel) is AssemblyData g)
                return ShipOwnTextures.Contains(ship)
                    ? new AssemblyData { name = g.name + Modding.KitRules.VariantSeparator + Modding.KitRules.ShipToken + ship, pack = g.pack,
                                         category = g.category, root = g.root, origin = "skin" }
                    : g;
            string modName = $"ship_{ship:000}_mod";
            var modded = original ? null : Assemblies.Find(a => a.pack == "mod" && a.name == modName);
            if (modded != null) return modded;
            string p = $"ship_{ship:000}_";
            bool Match(AssemblyData a, bool main) =>
                a.category == "ships" && (main ? a.name.StartsWith(p) : a.name.StartsWith("v_" + p) || a.name.StartsWith("sn_" + p));
            foreach (bool main in new[] { true, false })
            {
                if (raceName != null) { var r = Assemblies.Find(a => Match(a, main) && a.name.EndsWith(raceName)); if (r != null) return r; }
                var any = Assemblies.Find(a => Match(a, main));
                if (any != null) return any;
            }
            // Globals::getShipGroup's special branches: 13 the Vossk freighter, 14 the Terran battleship (Level::createShip
            // builds battleship_terran, NpcShip), 15 the freighter by race (Vossk = 13's).
            return ship switch
            {
                13 => AssemblyByName("cargo_004_vossk"),
                14 => AssemblyByName("battleship_terran"),
                15 => AssemblyByName(raceName switch
                {
                    "nivelian" => "cargo_002_nivelian", "midorian" => "cargo_001_midorian", "vossk" => "cargo_004_vossk", _ => "cargo_003_terran",
                }),
                _ => null,
            };
        }
        public List<WeaponMountSet> WeaponMounts = new List<WeaponMountSet>();
        public List<WantedData> Wanted = new List<WantedData>();
        /// <summary>Remake, for mods (ships.json "npcTurrets", Modding.ModContent): the turret items the NPC ships of a type carry
        /// on its turret mounts (World.NpcMountedTurrets, the hangar's parked ships), by ship index. The original's NPC ships
        /// carry none.</summary>
        [System.NonSerialized] public Dictionary<int, List<int>> NpcTurrets = new Dictionary<int, List<int>>();
        /// <summary>Remake mods (ships.json "subsystems", Modding.ModContent): a capital ship's subsystems by ship index
        /// (World.CapitalSubsystems builds them on the hull).</summary>
        [System.NonSerialized] public Dictionary<int, List<CapitalSubsystem>> ShipSubsystems = new Dictionary<int, List<CapitalSubsystem>>();

        /// <summary>Remake (Modding.GameModels): ships that fly one of the game's own models, by ship index (a mod's new ship
        /// or an override with "model": "gof2:<model>"); ShipAssembly hands it out before anything else.</summary>
        [System.NonSerialized] public Dictionary<int, string> ShipModels = new Dictionary<int, string>();

        /// <summary>Remake (Modding.ModContent): the ShipModels ships whose mod has textures of their own for them
        /// (textures/ships/&lt;ship id&gt;/): ShipAssembly hands out a copy of the model for each.</summary>
        [System.NonSerialized] public HashSet<int> ShipOwnTextures = new HashSet<int>();

        /// <summary>Ship::hasJumpDriveIntegrated: the ships with a Khador Drive built in (37 Cronus, 38 Typhon, 40 Nemesis);
        /// remake mods add or remove them (ships.json "integrated": "khadorDrive", Modding.ModContent).</summary>
        [System.NonSerialized] public HashSet<int> IntegratedDrives = new HashSet<int>(DefaultIntegratedDrives);
        /// <summary>Ship::hasCloakIntegrated: the ships with a cloak built in, by ship index the cloak item whose stats it has
        /// (44 Specter and 49 Scimitar: the U'tool, 95); remake mods add or remove them ("integrated": "cloak").</summary>
        [System.NonSerialized] public Dictionary<int, int> IntegratedCloaks = new Dictionary<int, int>(DefaultIntegratedCloaks);
        public static readonly int[] DefaultIntegratedDrives = { 37, 38, 40 };
        public static readonly Dictionary<int, int> DefaultIntegratedCloaks = new Dictionary<int, int> { { 44, 95 }, { 49, 95 } };
        public const int IntegratedCloakItem = 95;
        /// <summary>Remake mods (ships.json "integrated": "items"): the items a ship has built in, by ship index (a secondary
        /// with its starting ammo); Hangar.SyncBuiltIn mounts them on the flown ship as built-in stacks.</summary>
        [System.NonSerialized] public Dictionary<int, List<ItemStack>> IntegratedItems = new Dictionary<int, List<ItemStack>>();

        /// <summary>The items 'ship' has built in (IntegratedItems), empty = none.</summary>
        public List<ItemStack> BuiltInItems(int ship) =>
            IntegratedItems != null && IntegratedItems.TryGetValue(ship, out var list) ? list : NoItems;
        static readonly List<ItemStack> NoItems = new List<ItemStack>();

        public bool HasIntegratedDrive(int ship) => IntegratedDrives != null && IntegratedDrives.Contains(ship);

        /// <summary>The cloak item a ship has built in (IntegratedCloaks), -1 = none.</summary>
        public int IntegratedCloak(int ship) => IntegratedCloaks != null && IntegratedCloaks.TryGetValue(ship, out int item) ? item : -1;

        /// <summary>The turret items NPC ships of 'shipIndex' carry (NpcTurrets), empty = none.</summary>
        public List<int> NpcTurretsOf(int shipIndex) => NpcTurrets != null && NpcTurrets.TryGetValue(shipIndex, out var l) ? l : new List<int>();

        [System.Serializable] class ItemAttributes { public int index; public int[] keys, values; }

        /// <summary>economy_default.json: the Default Economy's complete values of every item / ship that differs.</summary>
        [System.Serializable] class EconomyItem
        {
            public int index, techLevel, occurrence, minPrice, maxPrice, lowestPriceSystem, highestPriceSystem;
            public int[] keys, values;
            public List<StatEntry> statList;
            public List<BlueprintPart> blueprint;
        }
        [System.Serializable] class EconomyShip { public int index, armor, cargo, price, primary, secondary, turret, equipment, handling; }
        [System.Serializable] class EconomyFile { public List<EconomyItem> items; public List<EconomyShip> ships; }

        /// <summary>The item and ship tables this instance holds (Session.Economy when it was loaded).</summary>
        public Economy Economy { get; private set; } = Economy.Android;

        /// <summary>The macOS / Windows / iPhone tables (KiritoJPK's Default Economy items.bin, the iOS 1.1.19 ships.bin) over the
        /// Android ones: prices, tech levels, occurrences, attributes (and so the stats) and blueprint recipes; ship prices and
        /// slots; and the iOS build's weapon mount sets where they differ (weapons_hd_default.json: the exhaust anchors of the
        /// VoidX, H'Soc, Specter and Scimitar, Reference/tools/shop/build_default_mounts.py).</summary>
        void ApplyDefaultEconomy(string folder)
        {
            var file = Read<EconomyFile>(folder, "economy_default");
            if (file == null || file.items == null) return;
            foreach (var e in file.items)
            {
                var it = Item(e.index);
                if (it == null) continue;
                it.techLevel = e.techLevel; it.occurrence = e.occurrence; it.minPrice = e.minPrice; it.maxPrice = e.maxPrice;
                it.lowestPriceSystem = e.lowestPriceSystem; it.highestPriceSystem = e.highestPriceSystem;
                it.attrKeys = e.keys ?? new int[0]; it.attrValues = e.values ?? new int[0];
                it.statList = e.statList ?? new List<StatEntry>();
                it.blueprint = e.blueprint ?? new List<BlueprintPart>();
            }
            if (file.ships != null)
                foreach (var e in file.ships)
                {
                    var sh = Ship(e.index);
                    if (sh == null) continue;
                    sh.armor = e.armor; sh.cargo = e.cargo; sh.price = e.price;
                    sh.slots = new ShipSlots { primary = e.primary, secondary = e.secondary, turret = e.turret, equipment = e.equipment };
                    sh.handling = e.handling; sh.handlingMultiplier = e.handling / 100f;
                }
            var mounts = Read<List<WeaponMountSet>>(folder, "weapons_hd_default");
            if (mounts != null && WeaponMounts != null)
                foreach (var set in mounts)
                {
                    if (set == null || set.mounts == null) continue;
                    int i = WeaponMounts.FindIndex(m => m.ship == set.ship);
                    if (i >= 0) WeaponMounts[i] = set; else WeaponMounts.Add(set);
                }
            Economy = Economy.Default;
        }

        static Database shared;
        static Economy sharedEconomy;
        static int sharedRevision = -1;
        /// <summary>The database a Load is applying the mods to: what Shared answers meanwhile. Code the mods' entries run
        /// (a kit ship's mounts, read through Shared) would otherwise load Shared, which applies the mods again, and so on: a
        /// stack overflow that took the Editor down (a kit ship with "npcTurrets", 2026-10-24).</summary>
        [System.ThreadStatic] static Database applying;

        /// <summary>One loaded game database for read-only lookups (names, stations, systems), made again only when the
        /// economy or the mods change. Load() parses every table again (several MB of garbage, a hitch): callers that only
        /// read and run during play (Discord's status every 2 s, the mods' music on every music change) use this. Never
        /// change what it returns.</summary>
        public static Database Shared
        {
            get
            {
                if (applying != null) return applying;
                if (shared == null || sharedEconomy != Session.Economy || sharedRevision != Modding.ModManager.Revision)
                {
                    sharedEconomy = Session.Economy;
                    sharedRevision = Modding.ModManager.Revision;
                    shared = Load();
                    // (the load may have made the first scan of the Mods folders, which bumps the revision: it is this one's)
                    sharedRevision = Modding.ModManager.Revision;
                }
                return shared;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetShared() { shared = null; sharedRevision = -1; applying = null; }

        /// <summary>The Cronus' price in the finished world (multiplayer): see Load.</summary>
        public const int CronusSessionPrice = 1_150_000;

        public static Database Load(string resourceFolder = "GoF2Data")
        {
            var db = new Database
            {
                Ships = Read<List<ShipData>>(resourceFolder, "ships"),
                Items = Read<List<ItemData>>(resourceFolder, "items"),
                Systems = Read<List<SystemData>>(resourceFolder, "systems"),
                Stations = Read<List<StationData>>(resourceFolder, "stations"),
                Assemblies = ReadAssemblies(resourceFolder),
                WeaponMounts = Read<List<WeaponMountSet>>(resourceFolder, "weapons_hd"),
                Wanted = Read<List<WantedData>>(resourceFolder, "wanted"),
            };
            // item_attributes.json (Reference/tools/shop/build_item_attributes.py): items.json keeps them in a dictionary.
            foreach (var a in Read<List<ItemAttributes>>(resourceFolder, "item_attributes"))
            {
                var item = db.Items.Find(i => i.index == a.index);
                if (item != null && a.keys != null) { item.attrKeys = a.keys; item.attrValues = a.values; }
            }
            Modding.ModContent.NoteOriginalCounts(db);
            if (Session.Economy == Economy.Default) db.ApplyDefaultEconomy(resourceFolder);
            // Remake multiplayer (the finished world): the Cronus (37) costs 0 in the Android economy, Khador's story ship
            // that step 77 has the player take at Kothar for nothing, but every session's Kothar dealer sells it after the
            // Valkyrie add-on: a free top-tier hull, its Kaamo upgrades free too (LoungeChat.ModPrice). Priced like the
            // ships of its stats (Velasco 456 200, Nuyang II 626 467: ~500 000 for 190 hull / 95 t / 120 handling and only
            // 2 / 2 / 7 slots) plus its built-in Khador Drive (item 85, 624 803..662 923); the Default Economy has 1 200 000.
            if (Session.CompletedWorld)
            {
                var cronus = db.Ship(37);
                if (cronus != null && cronus.price <= 0) cronus.price = CronusSessionPrice;
            }
            // The active mods' new and changed entries (Modding.ModContent), after the economy so their prices stand.
            if (resourceFolder == "GoF2Data")
            {
                var outer = applying;
                applying = db;
                try { Modding.ModContent.Apply(db); }
                finally { applying = outer; }
            }
            return db;
        }

        static List<WeaponMountSet> gameMounts;

        /// <summary>An original ship's hardpoints as the game has them (weapons_hd.json, no mod's changes), never null; the
        /// Mod Builder's game models take copies.</summary>
        public static List<WeaponMount> GameMounts(int ship)
        {
            gameMounts ??= Read<List<WeaponMountSet>>("GoF2Data", "weapons_hd");
            return gameMounts?.Find(m => m.ship == ship)?.mounts ?? new List<WeaponMount>();
        }

        // The search is a loop of its own: a lambda capturing 'index' allocated its closure on every call, the fast path too
        // (the HUD's cargo readout calls these every frame).
        public ItemData Item(int index) => index >= 0 && index < Items.Count && Items[index].index == index ? Items[index] : FindItem(index);
        public ShipData Ship(int index) => index >= 0 && index < Ships.Count && Ships[index].index == index ? Ships[index] : FindShip(index);

        ItemData FindItem(int index)
        {
            foreach (var i in Items) if (i.index == index) return i;
            return null;
        }

        ShipData FindShip(int index)
        {
            foreach (var s in Ships) if (s.index == index) return s;
            return null;
        }

        [System.Serializable] class Wrapper<W> { public W list; }

        static T Read<T>(string folder, string file) where T : new()
        {
            var ta = Resources.Load<TextAsset>(folder + "/" + file);
            if (ta == null)
            {
                Debug.LogWarning("Database: missing Resources/" + folder + "/" + file + ".json");
                return new T();
            }
            return JsonUtility.FromJson<Wrapper<T>>("{\"list\":" + ta.text + "}").list;
        }

        [System.Serializable] class AssemblyFile { public List<AssemblyData> entries; }

        static List<AssemblyData> ReadAssemblies(string folder)
        {
            var ta = Resources.Load<TextAsset>(folder + "/assemblies");
            return ta != null ? JsonUtility.FromJson<AssemblyFile>(ta.text).entries : new List<AssemblyData>();
        }

        public AssemblyData AssemblyByName(string name)
        {
            // Remake mods: a customizable ship's build ("ship_NNN_mod#<build>", Modding.ModShipKits) or a ship in a mod's skin
            // ("<model>#ship=N;skin=id") isn't in the table: its model's pack and category (a skinned game ship stays the game's).
            int hash = name?.IndexOf(Modding.KitRules.VariantSeparator) ?? -1;
            if (hash > 0)
            {
                var own = Assemblies.FirstOrDefault(a => a.name == name.Substring(0, hash));
                return own == null ? null : new AssemblyData { name = name, pack = own.pack, category = own.category, root = own.root,
                                                               origin = own.pack == Modding.ModContent.Pack ? "kit build" : "skin" };
            }
            return Assemblies.FirstOrDefault(a => a.name == name);
        }

        /// <summary>Mount positions of a slot type for a ship, in the order of Ship::getSlotPos.</summary>
        public List<WeaponMount> MountsOf(int shipIndex, int slotType)
        {
            // Remake mods: a customizable ship's mounts come with its build's parts (Modding.ModShipKits).
            var built = Modding.KitRules.BuildMounts(shipIndex, slotType);
            if (built != null) return built;
            var set = WeaponMounts.FirstOrDefault(m => m.ship == shipIndex);
            return set != null ? set.mounts.Where(m => m.slotType == slotType).ToList() : new List<WeaponMount>();
        }

        public ShipData ShipByName(string name) => Ships.FirstOrDefault(s => s.name == name);
        public ItemData ItemByName(string name) => Items.FirstOrDefault(i => i.name == name);
        public IEnumerable<StationData> StationsIn(int systemIndex) => Stations.Where(s => s.system == systemIndex);

        /// <summary>
        /// Builds flight stats the same way the original combines Ship + installed equipment
        /// (Ship::refreshValue / PlayerEgo ctor): booster = category "Booster", agility = "Steering nozzle".
        /// </summary>
        public static FlightStats BuildFlightStats(ShipData ship, IEnumerable<ItemData> equipment, int handlingUpgrades = 0)
        {
            var fs = new FlightStats { handling = Modding.KitRules.Handling(ship), handlingUpgrades = handlingUpgrades };   // a kit ship's build too
            // Remake, for mods (Modding.ModFlight): each flight style's share; an item's flightScope (attr 110) keeps its
            // booster / nozzle attributes to one style, its bonuses (111-121) go to both, the original or free flight.
            var styles = new[] { new FlightTuning(), new FlightTuning() };   // [0] original, [1] free flight
            // The ship's first, so an item's freeBoostFactor (the booster's) wins over the ship's.
            styles[0].bonus.Add(ship.flightBoth).Add(ship.flightOriginal);
            styles[1].bonus.Add(ship.flightBoth).Add(ship.flightFree);
            if (Modding.KitRules.BuildEffect(ship.index) is Modding.KitEffect build)   // a kit ship's parts: both styles
            {
                styles[0].bonus.Add(build.flight);
                styles[1].bonus.Add(build.flight);
            }
            foreach (var item in equipment ?? Enumerable.Empty<ItemData>())
            {
                int scope = item.Attr(Modding.ModFlight.ScopeAttr);
                for (int k = 0; k < 2; k++)
                {
                    if ((k == 0 && scope == Modding.ModFlight.ScopeFree) || (k == 1 && scope == Modding.ModFlight.ScopeOriginal)) continue;
                    switch (item.categoryId)
                    {
                        case 14: // Booster
                            styles[k].boostSpeed = item.Stat("boostSpeed");
                            styles[k].boostDurationMs = item.Stat("boostDurationMs");
                            styles[k].boostRechargeMs = item.Stat("boostRechargeMs");
                            break;
                        case 16: // Steering nozzle
                            styles[k].agility = item.Stat("agility");
                            break;
                    }
                }
                Modding.ModFlight.AddItemBonuses(item, styles[0].bonus, styles[1].bonus);
            }
            fs.original = styles[0];
            fs.free = styles[1];
            // The single-style fields (the original's share) for whatever reads them directly.
            fs.boostSpeed = styles[0].boostSpeed; fs.boostDurationMs = styles[0].boostDurationMs;
            fs.boostRechargeMs = styles[0].boostRechargeMs; fs.agility = styles[0].agility;
            return fs;
        }
    }
}
