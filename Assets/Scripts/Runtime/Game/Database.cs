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
        /// <summary>Remake mods (ships.json "flight": { "rules": { ... } }): this ship's FlightRules by name, over the mods'
        /// flight.json (Modding.ModFlight.RulesFor); null = none.</summary>
        [System.NonSerialized] public System.Collections.Generic.Dictionary<string, float> flightRules;

        /// <summary>Remake mods (Modding.ModShipView, ships.json "camera" / "cockpit" / "smoke" / "explosionScale"; null /
        /// 1 = the original's for every ship): the chase camera in game units (distance behind the pivot, height, the look
        /// point's height and how far ahead of the pivot it is: the original's 1338 / 600 / 600 / -650), the cockpit view's
        /// seat and the damage smoke's points (game units in position_engine's axes, like the mounts) and the explosion's
        /// size when the ship dies (x the fighters' 1).</summary>
        [System.NonSerialized] public float[] chaseCamera;
        /// <summary>Remake mods (ships.json \"camera.orbitDistance\"): the free-look orbit's default radius in game units.</summary>
        [System.NonSerialized] public float orbitCameraDistance = 3800f;
        [System.NonSerialized] public Vector3? cockpit;
        [System.NonSerialized] public List<Vector3> smokePoints;
        [System.NonSerialized] public float explosionScale = 1f;

        /// <summary>Remake mods (ships.json "dockingRole"): an explicit "internal" or "external" dock role; null preserves size-based legacy eligibility.</summary>
        [System.NonSerialized] public string dockingRole;
        /// <summary>Remake mods (Modding.ShipClassRules, ships.json "shipClass" / "classTags"): the ship's class (a free word a
        /// class scanner shows) and the words it answers to besides; none on every original ship.</summary>
        [System.NonSerialized] public string shipClass;
        [System.NonSerialized] public string shipClassLabel;
        [System.NonSerialized] public string[] classTags;
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

        /// <summary>The hitscan beam looks shared by display grouping and weapon behavior, including mod-derived items.</summary>
        public static bool IsBeamLook(int look) => look == 9 || look == 10 || look == 11 || look == 228;
        /// <summary>Remake mods (Modding.ShipClassRules, items.json "allowedClasses" / "excludedClasses"): the ship classes / tags
        /// that may / may not mount it; null = no list (every original item: it fits every ship).</summary>
        [System.NonSerialized] public string[] allowedClasses, excludedClasses;

        public bool HasAttr(int id) => System.Array.IndexOf(attrKeys, id) >= 0;
        public int Attr(int id, int fallback = 0)
        {
            int i = System.Array.IndexOf(attrKeys, id);
            return i >= 0 ? attrValues[i] : fallback;
        }

        /// <summary>CLAUDE.md "Mining": cores from a completed class-A asteroid; absent = one, negative = zero.</summary>
        public const int CoreYieldAttr = 189;   // (was 172: Flight.Gun.BeamRadiusAttr's)
        public int CoreYield => Mathf.Max(0, Attr(CoreYieldAttr, 1));

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
        /// <summary>Modding: systems.json population, the ambient NPC traffic multiplier (0 = none, 1 = unchanged).</summary>
        [System.NonSerialized] public float ambientPopulation = 1f;
        [System.NonSerialized] public bool derelictFreighters = true;
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
    /// shape (half width, half height, flame length in game units; none = the round glow of CustomShipData.engineGlowRadius);
    /// glowShape (remake, exhaust mounts) its outline in that box, Modding.EngineGlowShape: none / "round" an ellipse.</remarks>
    [System.Serializable] public class WeaponMount
    {
        public int slotType; public int[] position_engine; public float[] turretAngles; public bool upsideDown;
        public float[] glowColor, glowSize, turretUp;
        public string glowShape;
        /// <summary>Per-mount turret model scale; multiplies the weapon's own turret scale. 1 = unchanged, up to 1000.</summary>
        public float turretScale = 1f;
        public float TurretScale => turretScale <= 0f || float.IsNaN(turretScale) || float.IsInfinity(turretScale) ? 1f : Mathf.Clamp(turretScale, 0.01f, 1000f);

        /// <summary>A secondary mount's launchRotation in the ship model's Unity frame: Euler degrees [x, y, z] or
        /// quaternion [x, y, z, w]; identity when absent. Used only for player secondary launch direction.</summary>
        public Quaternion LaunchTurn
        {
            get
            {
                var r = launchRotation;
                if (r != null && r.Length >= 4) return new Quaternion(r[0], r[1], r[2], r[3]).normalized;
                if (r != null && r.Length >= 3) return Quaternion.Euler(r[0], r[1], r[2]);
                return Quaternion.identity;
            }
        }

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
        /// from straight back (Modding.EngineFx). An external docking hardpoint (slotType 8): its orientation
        /// (ExternalDockingRules).</summary>
        public float[] rotation;
        /// <summary>Remake mods, a secondary (slotType 1) mount: the direction its shots leave in, as a turn from the ship's
        /// nose in the ship model's frame. Euler degrees [x, y, z] or quaternion [x, y, z, w]; absent = straight ahead.
        /// Separate from visual rotation; primaries, turrets and NPC guns ignore it.</summary>
        public float[] launchRotation;
        /// <summary>Remake mods, a secondary (slotType 1) mount: milliseconds after launch before its homing shots begin
        /// steering toward their lock. Zero / absent = immediate homing; only player secondaries use this value.</summary>
        public int lockDelayMs;
        /// <summary>Remake mods, a primary / secondary mount: nodes of the ship's model (by name) that show only while this mount
        /// carries a weapon (a hardpoint's pylon; Modding.ModHardpoints.AttachWeapons).</summary>
        public string[] nodes;
        /// <summary>Remake mods, a primary / secondary mount of a kit ship: the node of the ship's model (by name) its weapon
        /// model rides on, so it moves with that part's animation (the Forsaken's gun platforms as its weapons deploy;
        /// Modding.ModHardpoints.AttachWeapons). Null = the ship itself.</summary>
        public string follow;
        /// <summary>Remake mods, an exhaust mount (slotType 3) of a ship with "engineFx": that effect's size here (GoF3's engine
        /// helper scale; 0 = 1).</summary>
        public float fxScale;
        /// <summary>Remake mods, an exhaust mount (slotType 3) or a booster point (slotType 5, EVERSPACE 2's SKT_Booster sockets):
        /// the socket's scale in EVERSPACE 2 (its largest axis; 0 = 1), which sizes the engine light, trail and glow there
        /// (Modding.Es2EngineVfx); a contrail point (slotType 6, EVERSPACE 2's SKT_Contrail sockets, Modding.Es2Contrails): the
        /// ribbon's width and, capped at 1, its opacity. A headlight fixture (slotType 7, EVERSPACE 2's SKT_HeadLight sockets,
        /// Modding.Es2Headlights) has a 'direction' like a jet's: the way it shines.</summary>
        public float socketScale;
    }
    [System.Serializable] public class WeaponMountSet { public int ship; public string shipName; public List<WeaponMount> mounts; }

    /// <summary>A mod's ship (a ships.json entry, Modding.ModContent; the format of PR #34's custom_ships.json): a ships.json
    /// entry plus what the original keeps in its fixed tables (race, hangar height), the weapon mounts and how to build its
    /// model at run time (Modding.ModShipBuilder). Paths are inside the mod.</summary>
    /// <summary>Remake: ships.json "animations": a model's glTF clips by name for what the ship is doing (ModShipAnimator).</summary>
    [System.Serializable] public class ShipAnimations
    {
        public string idle;                 // loops whenever nothing else plays
        public ShipStateAnimation drift;    // Chorus flight's drift
        public ShipStateAnimation travel;   // Chorus flight's travel boost
        public ShipStateAnimation braking;  // while the brake is held (Chorus's player capital ship's flaps)
        public ShipStateAnimation weapons;  // while the player fires: deploy (start), deployed (loop), undeploy (end)
        public string steerLeft, steerRight, thrust, brake, bankLeft, bankRight;   // poses laid over it by the input (each clip's last frame)
        public ShipStateAnimation dodgeLeft, dodgeRight;   // Chorus flight's dash: played once (start, then end)
        public string driftDodgeLeft, driftDodgeRight;      // the same out of a drift (played once)
    }

    [System.Serializable] public class ShipStateAnimation { public string start, loop, end; }

    [System.Serializable] public class CustomShipData : ShipData
    {
        public int race = -1;              // 0 Terran, 1 Vossk, 2 Nivelian, 3 Midorian, 8 pirate, 9 void (Shop.ShipRace)
        public int priceDefault;           // the Default Economy's price (0 = the same as 'price')
        public int hangarHeight = 250;     // StationTables.ShipY: pivot height above the hangar floor, game units
        public string assembly;            // set by ModContent: "ship_NNN_mod" (Database.ShipAssembly finds it by number)
        public List<WeaponMount> mounts;   // like weapons_hd.json: slotType 0 primary, 1 secondary, 2 turret, 3 exhaust, 4 manoeuvring jet, 8 external-docking hardpoint
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
        public ShipAnimations animations;  // Remake: the model's clips by what the ship does (Modding.ModShipAnimator); null = its first clip loops
        // Remake: keep the model's skinned meshes skinned, so clips that bend it (a creature's tentacles: Chorus's Guilt) play;
        // otherwise they are baked to their rest pose (ModShipBuilder.FlattenSkinned). Measuring and collision read the bind pose.
        public bool skinned;
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
        // Remake mods (2026-11-11, Chorus's warship barriers): the shield fields (shieldMaterial) block the way while they show.
        public bool shieldBlocks;
        // Remake mods (2026-11-11, Chorus's warships): the share of a direct hit on the hull it takes (after the shields); 0 = it is
        // hurt only through its weak spots (their passed-on hits and bonuses), Chorus's DefaultDamageMultiplier 0.
        public float hullDamage = 1f;
        public float hullFactor = 1f;     // remake mods: a mod capital ship's hull x this (Modding.ModCapitalShips.Spawn)
        /// <summary>Remake mods (Chorus's player capital ship): the player's hull mends by this many points a second
        /// (PlayerHealth, Hitpoints.RegenerateHull); 0 = only a repair bot's.</summary>
        public float hullRegen;
        /// <summary>"es2": EVERSPACE 2's engine effects (Modding.Es2EngineVfx: the glow, trails and lights at the exhaust and
        /// booster mounts) without flame materials of its own and without the glow discs (a capital ship's sockets).</summary>
        public string engineStyle;
        /// <summary>Which of EVERSPACE 2's engine effects an "es2" ship (or one with flames) shows (ships.json "es2Engine";
        /// null = all; Modding.Es2EngineVfx).</summary>
        public CustomEs2Engine es2Engine;
        /// <summary>A mod capital ship that jumps away when its hull is depleted instead of being destroyed (World.CapitalRetreat:
        /// EVERSPACE 2's Dreadnought), so an encounter with it can come again.</summary>
        public bool retreat;
        /// <summary>Remake mods: the loot container this ship leaves when it dies or is robbed (Modding.ModCrates): a glTF / GLB
        /// file in the mod, drawn in place of its race's container, as big as that container unless 'crateSize' (its largest
        /// extent, game units) says otherwise; none = the race's.</summary>
        public string crate;
        public float crateSize;
        /// <summary>Remake mods (2026-11-11, Chorus's wrecks): what the ship breaks into where it explodes (World.WreckBreakup);
        /// null = it explodes and is gone.</summary>
        public CustomWreck wreck;
        /// <summary>Remake mods (2026-11-11, Chorus's BP_Effect_Auxiliary_Shield_PlayerCapitalShip): the Shield Overcharge's look
        /// on this ship (Flight.ShieldShell): a shell model in the ship model's own frame, painted with the ship's materials (a
        /// "shield" block); null = the emergency system's bubble.</summary>
        public CustomOvercharge overcharge;
        /// <summary>Remake mods (2026-11-11, Chorus's explosions): a JSON file in the mod with the ship's own death effects
        /// (CustomFxFile, World.ModDeathFx) instead of the game's explosion; null = the game's.</summary>
        public string deathFx;
        /// <summary>Remake mods (2026-11-19, Chorus's Forsaken): the look of Chorus flight's drift on this ship, the player's
        /// (CustomDriftFx, World.ModDriftFx): effects on nodes of its model, switched on as the drift starts and off as it
        /// ends; null = none.</summary>
        public CustomDriftFx driftFx;
        /// <summary>Remake mods (2026-11-11, Chorus's warships): a capital ship's reactor going critical when its hull runs out
        /// (CustomDeathThroes, NpcShipDamage); null = it burns and explodes as the game's do.</summary>
        public CustomDeathThroes deathThroes;
        /// <summary>Remake mods: its NPCs materialize where they arrive in flight (World.HullWarpIn); null = they just appear.</summary>
        public CustomWarpIn warpIn;
        /// <summary>Remake mods (after Galaxy on Fire 3's wingmen): the device a hired wingman flying this hull uses: "repair",
        /// "ammo" or "charged" (Flight.WingmanDevice); none = none, like the game's wingmen.</summary>
        public string wingmanDevice;
    }

    /// <summary>Remake mods (ships.json "deathThroes", 2026-11-11, after Chorus's BP_NPC_Warship_Base: StartDeathThroes, its
    /// countdown and its blast): when the hull runs out, "{name}'s reactor is going critical" and 'seconds' of blasts over the
    /// hull ('blasts' of them, the ship's "dying" death effect where it has one), then the explosion hurts everything within
    /// 'radius' game units: 'damage' x a ship's maximum hull (shields first) at its middle, falling off to nothing at the
    /// edge; the player's camera shakes within twice that.</summary>
    /// <summary>Remake mods (2026-11-11, Chorus's Omen tissue warping in): an NPC ship of this type arriving in flight
    /// materializes over 'seconds', its hull cut away by noise that shrinks to nothing, the cut's 'edge' (a share of the noise)
    /// glowing in 'color' (HDR; World.HullWarpIn, the hull shader's wreck cut run backward).</summary>
    [System.Serializable] public class CustomWarpIn
    {
        public float seconds = 2f, edge = 0.02f;
        public float[] color;
    }

    [System.Serializable] public class CustomDeathThroes
    {
        public float seconds = 10f, radius = 16000f, damage = 1f;
        public int blasts = 12;
    }

    /// <summary>Remake mods (ships.json "deathFx", 2026-11-11, after Chorus's Cascade explosions): the ship's death effects,
    /// a JSON file in the mod: 'main' at the explosion, 'dying' 'dyingCount' times over its tumble (at points on the hull),
    /// in place of the game's explosion (its sound stays).</summary>
    [System.Serializable] public class CustomFxFile
    {
        public CustomFx main, dying;
        public int[] dyingCount;
    }

    /// <summary>One effect: its emitters, each a particle system, together.</summary>
    [System.Serializable] public class CustomFx
    {
        public CustomFxEmitter[] emitters;
    }

    /// <summary>Remake mods (ships.json "driftFx", 2026-11-19, after Chorus's Forsaken: BP_Player_Ship_Phoenix's "FX Drift
    /// Started" activates its four Cascade systems on the skeleton's SCK_VFX_Drift sockets, "FX Drift Recovery Started"
    /// deactivates them): the drift's look on the player's ship (World.ModDriftFx). Each part sits on a node of the ship's
    /// model, by name ('node'; none = the ship itself), in that node's frame: 'fx' its particle emitters (a "loop" one keeps
    /// emitting until the drift ends, then its particles live out), 'fields' its energy-field shells.</summary>
    [System.Serializable] public class CustomDriftFx
    {
        public CustomDriftPart[] parts;
    }

    [System.Serializable] public class CustomDriftPart
    {
        public string node;
        public CustomFx fx;
        public CustomDriftField[] fields;
    }

    /// <summary>One energy-field shell of the drift (Chorus's M_Drift_MagnetField on a mesh particle of SM_Drift_HalfSphere,
    /// emitted again every 0.2 s for as long as the drift lasts: a shell that stays): 'mesh' a JSON file in the mod
    /// (CustomFxMesh, made in its part's frame, game units), drawn on GoF2/ChorusDriftField with 'band' (T_ShockWave_Rect_G),
    /// 'mask' (T_VFX_Masks_Ellipse), 'distort' (T_Noise_Fractal_07), 'line' (T_Line_01) and 'background' (T_Noise_Tiled03),
    /// PNGs in the mod; 'color' the particle's colour (HDR), 'exposure' Chorus's brightness to the remake's. The other values
    /// are the material's parameters (Chorus's defaults): 'offset' Upositon, 'pan' Panning V, 'tiling' [Tiling U, Tiling V],
    /// 'core' [pow_core, mul_core], 'coreLine' [Panning_V_CoreLine, Tiling U_CoreLine, Tiling_V_CoreLine, mul_CoreLine],
    /// 'distortRing' Distort_Ring, 'tilingBg' [Tiling U_BG, Tiling V_BG], 'bg' [pow_BG, Mul_BG], 'textures' [Textures Exp,
    /// Textures Mult], 'fresnel' Fresnel_pow.</summary>
    [System.Serializable] public class CustomDriftField
    {
        public string mesh, band, mask, distort, line, background;
        public float[] color;
        public float exposure = 1f;
        public float offset = 0.5f, pan = 0.2f, distortRing = 0.06f, fresnel = 1.6f;
        public float[] tiling = { 1f, 0.12f }, core = { 1f, 5f }, coreLine = { 2f, 1f, 1f, 12f }, tilingBg = { 1.5f, 0.45f },
            bg = { 1.5f, 2f }, textures = { 1f, 4f };
    }

    /// <summary>A small mesh as JSON (a drift field's shell): 'positions' and 'normals' [x, y, z, ...] in game units (Unity's
    /// axes: x right, y up, z forward), 'uvs' [u, v, ...] (Unreal's: v down), 'triangles' three indices each.</summary>
    [System.Serializable] public class CustomFxMesh
    {
        public float[] positions, normals, uvs;
        public int[] triangles;
    }

    /// <summary>Remake mods (items.json fx "rite", 2026-11-13, after Chorus's Rites' particle systems and FMOD events): a Rite's
    /// looks, a JSON file in the mod (Flight.RiteLook, PlayerDevices): 'start' at the ship as it fires (its sound the fx's
    /// "shot"), 'vanish' where the Hunt leaves, 'appear' where it lands, 'hit' on each ship the Storm or the Star strikes
    /// (following it), 'loop' on the ship while the Star runs (its sound looping), 'end' where the Star ends; the upgrades':
    /// 'fury' on the ship while the Hunt's B lasts (its sound looping), 'absorb' as the Storm's B fills the shield, 'heal' as
    /// the Star's B repairs the hull; each with its sound (a file or several, one at random). Every part may be left out.</summary>
    [System.Serializable] public class CustomRiteFx
    {
        public CustomFx start, vanish, appear, hit, loop, end, fury, absorb, heal;
        public string[] vanishSound, appearSound, hitSound, loopSound, endSound, furySound, absorbSound, healSound;
    }

    /// <summary>A particle emitter (after a Cascade emitter's level 0): sizes and speeds in game units, times in seconds.
    /// 'image' a PNG in the mod (white on black, added; 'sheet' [columns, rows] of frames, 'frames' the frame over the
    /// particle's life as samples 0..1 of the sheet, else 'randomFrame' one at random), 'bursts' [time, count, ...], 'rate'
    /// a second over 'duration'; 'life' / 'size' / 'speed' (outward, or along the effect's forward with 'forward') / 'spin'
    /// (deg a second) [min, max]; 'sphere' the radius they start in; 'drag' as Unreal's (velocity lost a second x it);
    /// 'inherit' of the ship's motion; 'color' its brightest colour (HDR), 'colorOverLife' [r, g, b, ...] and
    /// 'alphaOverLife' samples 0..1 times it, 'sizeOverLife' samples times 'size'; 'stretch' > 0: drawn along its
    /// flight, that long for its width. Remake (2026-11-11): 'blend' "alpha" draws the image over what is behind instead of
    /// adding it (smoke, debris; its colour then a plain tint, 0..1); 'light' &gt; 0 makes it a light, no particles: a point
    /// light of that range (game units) and 'lightIntensity', the colour, colour / alpha / size over its life
    /// (World.FxFlash), from the first burst.</summary>
    [System.Serializable] public class CustomFxEmitter
    {
        public string image, blend;
        public float light, lightIntensity = 1f;
        /// <summary>Remake (2026-11-11, Chorus's SpawnPerUnit trails and VectorFieldLocal turbulence): 'trail' puffs left along each
        /// particle's flight (a particle without an 'image' is only the trail's source); 'noise' [strength game units a second,
        /// cell size game units] a turbulence on the particles.</summary>
        public CustomFxTrail trail;
        public float[] noise;
        /// <summary>Remake (2026-11-19, the drift's Cascade emitters): 'loop' emits again every 'duration' (its bursts) for as
        /// long as the effect runs (Cascade's EmitterLoops 0; a drift part's until the drift ends, World.ModDriftFx; a light's
        /// flash repeats); 'local' its particles move with the effect (bUseLocalSpace); 'at' [x, y, z] where they start, game
        /// units in the effect's frame (Unity's axes; Cascade's Location module).</summary>
        public bool loop, local;
        public float[] at;
        public int[] sheet;
        public float[] frames;
        public bool randomFrame, forward, randomRotation;
        public float[] bursts;
        public float rate, duration = 1f, sphere, drag, inherit = 1f, stretch;
        public float[] life, size, speed, spin, color, colorOverLife, alphaOverLife, sizeOverLife;
    }

    /// <summary>A death effect emitter's trail: puffs of 'image' (a 'sheet' with 'frames', or 'randomFrame') every 'every' game
    /// units along its particle's flight, each living 'life' [min, max] s, 'size' [min, max] game units, 'sizeOverLife',
    /// 'color' (HDR), 'colorOverLife', 'alphaOverLife', added (or "blend": "alpha").</summary>
    [System.Serializable] public class CustomFxTrail
    {
        public string image, blend;
        public int[] sheet;
        public float[] frames, life, size, sizeOverLife, color, colorOverLife, alphaOverLife;
        public bool randomFrame, randomRotation;
        public float every = 100f;
    }

    /// <summary>A ship's Shield Overcharge look (CustomShipData.overcharge; Chorus's P_Buff_Auxiliary_Shield_PlayerCapitalShip,
    /// one mesh particle): 'model' (a glTF / GLB in the mod, made in the ship model's frame and units, its materials from the
    /// ship's "materials": a "shield" block) shown 'delay' seconds after the overcharge starts for 'seconds', its tint 'color'
    /// (RGB, HDR) x 'colorOverLife' (brightness samples over its life, evenly spaced; none: 1) with 'alpha' x 'alphaOverLife'
    /// (likewise).</summary>
    [System.Serializable] public class CustomOvercharge
    {
        public string model;
        public float seconds = 4.8f, delay = 0.5f, alpha = 1f;
        public float[] color, colorOverLife, alphaOverLife;
    }

    /// <summary>Remake mods (ships.json "wreck", 2026-11-11, after Chorus's ExplosionPhysics wrecks): 'model' a glTF / GLB in the
    /// mod made in the ship model's own frame and units (it is placed exactly where the hull was), each node with a mesh one
    /// piece, painted with the ship's "materials"; the pieces fly apart from the wreck's middle at 'speed' (x a tenth of the
    /// ship's length a second) with the ship's own motion, turn at 'spin' (x 20-90 deg a second), slow down, burn and are
    /// gone after 'seconds' (shrinking away over the last quarter).</summary>
    [System.Serializable] public class CustomWreck
    {
        public string model;
        public float seconds = 6f, speed = 1f, spin = 1f;
        /// <summary>Remake (2026-11-11, Chorus's MF_WreckBurn / Dissolve): the pieces' glowing burn at first (0 none, 1
        /// Chorus's Burn_Amount), cooling over the first half; 'burnColor' its HDR colour (none: Chorus's 1500 K); 'dissolve'
        /// false: the pieces shrink away instead of disintegrating.</summary>
        public float burn = 1f;
        public float[] burnColor;
        public bool dissolve = true;
        /// <summary>Remake (2026-11-11, MF_WreckBurn's compiled base pass): Chorus's own burn: 'burnMap' (PNG, R the noise,
        /// over UV0 x 'burnTiling', + 'burnOffset'), sharpened by 'burnFalloff', glowing as a blackbody at 'burnTemperature'
        /// (K) x the burn; 'burnVertexRed' the vertex colour's red keeps parts from burning; 'burnUv1' the map over UV1 (the
        /// model's second UV set). None: the value-noise burn.</summary>
        public string burnMap;
        public float burnTiling = 1f, burnOffset, burnFalloff = 3f, burnTemperature = 1500f;
        public bool burnVertexRed, burnUv1;
    }

    /// <summary>Remake mods: EVERSPACE 2's per-ship switches of its engine effects (a ship blueprint's SpawnEngineGlow /
    /// SpawnEngineTrails; EVERSPACE 2's warships and freighters have them off, and EngineVFXEnabled false turns both off):
    /// 'glow' the camera-facing glow at each socket (an NPC's), 'trails' its trail (an NPC's) and the player's veils.</summary>
    [System.Serializable] public class CustomEs2Engine
    {
        public bool glow = true;
        public bool trails = true;
        public bool contrails = true;   // EVERSPACE 2's ContrailsEnabled: the wing-tip contrails while boosting (Modding.Es2Contrails)
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
        public Vector3 forward;   // remake (2026-11-11, Chorus's warship devices): a part's facing with 'up' (game axes); zero = by 'up' alone
        public int item = -1, hull = -1, stage;
        public bool shields;
        public float weakspot, bonus, scale = 1f, radius = -1f;
        public float shield;   // remake (2026-11-11, Chorus's shielded turret mounts): shield points = its hull x this, taken first, not recharged
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
        /// <summary>Remake (2026-11-03): its role in a fight (Flight.NpcRoles, EVERSPACE 2's drone types): "sniper", "detonator",
        /// "webber", "shieldCharger", "armorLink", "antiMissile", "jumpSuppressor", "lootDrone" (never a fighter: the loot drone encounter's,
        /// Modding.ModTraffic.PickLootDrone); empty = an ordinary fighter.</summary>
        public string role;
        /// <summary>Remake: "gof3" = Galaxy on Fire 3's fight, "es2" = EVERSPACE 2's fight, "smep" = the native SMEP fighter
        /// mode (ES2 aiming / flight plus SMEP range, strafing and fire rules), all while Options > Gameplay "NPC dogfighting"
        /// is "Mod hulls' own" (the default); else GoF2's. SMEP needs "mobileAi": true below to run on mobile platforms.</summary>
        public string ai;
        /// <summary>Remake: with "ai": "smep", explicitly allow SMEP on mobile platforms and against mobile multiplayer pilots; off by default.</summary>
        public bool mobileAi;
        /// <summary>Remake (2026-11-06): with "ai": "gof3", its evasive roll's base chance (GoF3's evadeChanceBase: scouts and T1-T2
        /// hulls 0, T3+ fighters 0.33, T3+ gunships 1, bosses 0.1), capped by the difficulty (Flight.Gof3Dogfight.EvadeChance);
        /// below 0 (unset) = a fighter's 0.33.</summary>
        public float evade = -1f;
        /// <summary>Remake (2026-11-05, GoF3's kamikazes): the devices its NPCs use when hostile to the player, as a Most
        /// Wanted criminal's (wanted.json "devices", Modding.ModWanted.ReadDevices); read apart from JsonUtility, null = none.</summary>
        [System.NonSerialized] public System.Collections.Generic.List<Flight.NpcDeviceSpec> devices;
        /// <summary>Remake (2026-11-04): what its NPCs drop besides their cargo (EVERSPACE 2's loot_npcs.csv): each entry by its
        /// chance, at most lootDrops of them (0 = any number); a Void ship's too, which carries no cargo. Null = none.</summary>
        public CustomLoot[] loot;
        public int lootDrops;
        /// <summary>Remake (2026-11-05, GoF3's NPC loadouts): a second weapon its NPCs may carry, the first entry carried
        /// (CustomSecondary.Carried) at its spawn (World.NpcShip); null = none, as the game's NPCs.</summary>
        public CustomSecondary[] secondaries;
        /// <summary>Remake (2026-11-11, Chorus's NPC weapons, BP_NPC_Weapon_*): the gun its NPCs fire instead of the game's NPC
        /// gun, the first entry carried (World.NpcShip.SetOwnPrimary); null = the game's. A Most Wanted criminal's weapon wins.</summary>
        public CustomPrimary[] primaries;
    }

    /// <summary>A ships.json "traffic.primaries" entry: as a secondary ('item' its look, carried by rank and share), its
    /// 'factor' the damage a second against the game's NPC gun at the same level (1 = as much), fired every 'reloadMs'
    /// (0 = the game's NPC interval: each shot then carries more or less to keep the factor).</summary>
    [System.Serializable] public class CustomPrimary : CustomSecondary
    {
        public float reloadMs;
    }

    /// <summary>A ships.json "traffic.secondaries" entry: 'item' (a reference, "31" or "mod:id"), carried while the player's
    /// rank is within minRank..maxRank (0 = no bound) by 'share' % of the hull's NPCs, its damage x 'factor'.</summary>
    [System.Serializable] public class CustomSecondary
    {
        public string item;
        public int minRank, maxRank;
        public float share = 100f, factor = 1f;

        public bool Carried(int rank, float roll01) =>
            (minRank <= 0 || rank >= minRank) && (maxRank <= 0 || rank <= maxRank) && roll01 * 100f < share;
    }

    /// <summary>A ships.json "traffic.loot" entry: 'item' (a reference, "155" or "mod:id"), its chance 0..1, min..max of it.</summary>
    [System.Serializable] public class CustomLoot
    {
        public string item;
        public float chance = 1f;
        public int min = 1, max = 1;
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
        // remake mods: the specular workflow (URP's specular setup) in place of metallic: 'specularGlossiness' a map of the
        // specular colour (RGB, sRGB) and smoothness (A, x 'smoothness'), or 'specularColor' (RGB) without one. Galaxy on
        // Fire 3's hulls light this way (a coloured specular over the diffuse, not a metal).
        public string specularGlossiness;
        public float[] specularColor;
        // With a specular map or colour, GoF3's own terms (GoF2/Hull's GoF3ShipShader, Modding/README.md "materials"): how much
        // the surroundings reflect (GoF3's Environment_Amount, default 1) and its rim light (default 1), GoF3's
        // Specular_Multiplier on the highlight (default 1) and 'multiplierInMap' when the map already carries it.
        public float environment = 1f, rim = 1f, highlightMultiplier = 1f;
        public bool multiplierInMap;
        // With a specular map: GoF3's own environment cube for it to reflect, a 6:1 strip of its faces in the mod (GoF3's generic
        // cube: four blurs in R G B A, the gloss choosing; ModMaterials.GoF3Environment); without one the place's surroundings.
        public string environmentCube;
        // With "additive" (a glow layer): GoF3's effect terms (ModGlbImages.Glow): 'angleFade' [min, max] (|N.E| faded in between),
        // 'pulse' [frequency, min, max, threshold, step 0/1, smooth 0/1, vertexOffset 0/1], 'billboard' (TEXCOORD_1 / UV1 the
        // corners' view-space offsets: sprites that face the camera).
        public float[] angleFade, pulse;
        public bool billboard;
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
        // An additive glow layer (GoF2/ModGlow): its emission (or diffuse) texture x emissionColor x emissionIntensity x the
        // mesh's vertex colours (not with ignoreVertexColors), added to what is behind it, scrolling by scroll [u, v] a second
        // (Galaxy on Fire 3's gate glows and electricity).
        public bool additive;
        public float[] scroll;
        public bool ignoreVertexColors;
        // Battle damage embers: a mask (PNG) set as this material's emission when it has none, glowing ember-orange with the
        // ship's hull damage (DamageEmbers: from a third of the hull lost, flickering).
        public string damageEmission;
        // Battle damage charring: a detail colour (PNG, its alpha the burn mask) and normal map blended in with the hull
        // damage (URP Lit's detail layer, DamageEmbers raising its strength); dark, matte-looking burn patches under the embers.
        public string damageDetail, damageNormal;
        // EVERSPACE 2's structure layers (GoF2/Hull's Es2StructureLayers, after M_Structures_Parent): with "wear" or "grunge" the
        // vertex alpha is the occlusion and the wear mask. 'wear' (PNG, linear: R colour, G roughness, B metalness, A height)
        // worn in where the vertex alpha is low, 'wearNoise' (R) breaking up its edge, 'wearNormal' its normal map; 'grunge' (R)
        // darkening by grungeAmount and roughening; 'baseRoughness' (0..1) remaps the roughness map (0.5 = as it is);
        // 'rimBoost' brightens the colour at grazing angles (x (1 + rimBoost (1 - N.V)^3); ES2's 5).
        public string wear, wearNormal, wearNoise, grunge;
        public float wearTiling = 1f, wearExponent = 0.5f, wearCap, wearWidth = 0.5f, wearBrightness = 0.5f, wearMetal = 1f;
        public float grungeTiling = 1f, grungeAmount, baseRoughness = -1f, rimBoost;
        // Chorus's trim sheets and platings (GoF2/Hull's ChorusLayers; ChorusLayersSpec): drawn from Chorus's own maps over the
        // model's UV sets 0, 1 and 3 instead of this entry's diffuse / normal / metallicSmoothness. Off without a "kind".
        public ChorusLayersSpec chorus;
        /// <summary>Remake (2026-11-11): Chorus's FTL portal or its glow (GoF2/ChorusPortal, ModMaterials.Portal).</summary>
        public ChorusPortalSpec portal;
        /// <summary>Remake (2026-11-11): Chorus's rift vortex (GoF2/ChorusRift, ModMaterials.Rift).</summary>
        public ChorusRiftSpec rift;
        /// <summary>Remake (2026-11-11): Chorus's flowing lava (M_BrokenPlanet_Core; GoF2/Hull LavaFlow, ModMaterials.LavaFlow).</summary>
        public ChorusLavaSpec lava;
        /// <summary>Remake (2026-11-11): Chorus's shield shell (M_Shield; GoF2/ChorusShield, ModMaterials.Shield; a ship's
        /// "overcharge" model, Flight.ShieldShell).</summary>
        public ChorusShieldSpec shield;
        /// <summary>Remake (2026-11-11): a glow that travels along a pattern (Chorus's M_Phoenix glow lines; GoF2/Hull GlowWave).</summary>
        public GlowWaveSpec wave;
        /// <summary>Remake (2026-11-11): the hull's flash where the player's ship is hit (Chorus's M_Phoenix MF_HitEffect; GoF2/Hull
        /// HitFlash, World.HullHitFlash).</summary>
        public HitFlashSpec hit;
        // An engine flame (GoF2/ModEngineFlame, Modding.EngineFlame): the surfaces are flame stubs whose UV u runs from the
        // nozzle (0) to the tip (1), stretched backward by the throttle; emissionColor x emissionIntensity is its colour.
        public bool flame;
        public float flameLength = 5f;     // model units at full throttle (the stub's own length comes on top)
        public float flameIdle = 0.15f;    // the length at idle, a share of flameLength
        public string flameNoise;          // a PNG in the mod (grey noise, tiling), streaked along the flame
        public float[] boostColor;         // RGB 0..1 while boosting (default a hot blue), x emissionIntensity
    }

    /// <summary>Chorus's trim sheets and platings on a mod material (a ships.json / station "materials" entry's "chorus";
    /// GoF2/Hull's ChorusLayers, after M_TrimLayersMaster / M_NPCShipTrimMaster / M_PlatingMaster's compiled base passes;
    /// Modding/README.md "Chorus layers"). The model carries UV0 (the trim sheet, or the plating), UV1 (the tiling layer and
    /// dirt) and UV3 (the palette: one swatch per face). 'palette': LayerColors (left half, its colours already linear) and
    /// LayerData (right half) side by side, read linear by texel. Trim: 'tilingDiffuse' / 'tilingRmo' / 'tilingNormal' over
    /// UV1 x 'tiling'; 'trimAo' (Trim AAO: R the accent mask, B occlusion x 'aoIntensity'), 'trimNormal', 'trimEmissive'
    /// (its channels x 'emissiveR' / 'G' / 'B' x 'emissiveAmount'; 'emissiveUvShift' only on faces shifted out of the
    /// palette) over UV0; 'accentColor' (null = none) with 'accentMetal' / 'accentRoughness' / 'accentNormal'. Plating:
    /// 'tilingDiffuse' / 'tilingRmo' / 'tilingNormal' are Albedo / ORM / Normal over UV0 x 'tiling'. 'dirt' / 'dirtMask' /
    /// 'dirtRoughness' (Dirt BaseColor x 'dirtTint', over UV1) by the vertex colour's red ('dirtAlpha': x its alpha too;
    /// 'dirtByAlpha': by its alpha instead of its red; 'dirtOverEmissive': the dirt covers the trim emissive too, the
    /// M_NPCShipTrimMaster's dirt).
    /// 'kickerColor' / 'kickerExp' (0 = none): the far rim, faded in past 'kickerFadeOffset' over 'kickerFadeLength' (world
    /// units), with the emissive x 'distanceBoost' there; 'nearEmission' the emissive's factor up close (M_NPCShipTrimMaster's
    /// 2). Every emission x 'emissionScale' (Chorus's HDR to the remake's bloom).</summary>
    /// <summary>A material's "portal" block: Chorus's FTL portal (M_FTL_Portal) or its glow (M_FTL_PortalGlow), drawn by
    /// GoF2/ChorusPortal. kind "portal": 'noise' (T_main) in polar coordinates x (1, tilingV), scrolling by panningV, ^
    /// structurePower x structureMult into the colour 'curve' (one row), 'height' (its bump offset by heightRatio and the
    /// refraction's mask), 'sphereNormal' / 'ringNormal' and 'ior' / 'pulseSpeed' / 'pulseB' / 'refraction' the scene
    /// bent behind it, x 'fade'. kind "glow": 'gradient' at -UV, lerp(color x g, the curve at g, lerpColor) x g. kind "core"
    /// (Guilt's, MP_Guilt_Core_Radial_02): 'noise' (the threads) in polar coordinates of the card's UVs pinched by 'pinch' x
    /// (tilingU, tilingV), read panned by (speedU, panningV) and (-speedU, panningV), added; the core 1 - e^-(2.333 edge)^2
    /// ^ corePower x coreMult; the colour the 'curve' at threads + core (x 'fade'); the alpha (threads + threads^threadsPower
    /// x threadsMult + core) x the core's edge - (1 - masterAlpha), faded within 'depthFade' (game units) of what is behind
    /// it; alpha-blended.</summary>
    [System.Serializable] public class ChorusPortalSpec
    {
        public string kind;   // "portal" or "glow"; empty = off
        public string noise, height, curve, sphereNormal, ringNormal, gradient;
        public float tilingV = 0.15f, panningV = 0.33f, structurePower = 1.5f, structureMult = 1f, heightRatio = -1f, fade = 1f;
        public float ior = 1.5f, pulseSpeed = -0.4f, pulseB = 3f, refraction = 0.05f;
        public float[] color;
        public float lerpColor = 1f;
        public float tilingU = 1f, speedU = 0.05f, pinch = -1.6f, corePower = 8.845f, coreMult = 1.2f, threadsPower = 1.5f;
        public float threadsMult = 5f, masterAlpha = 1f, depthFade;
        public bool On => kind == "portal" || kind == "core" ? !string.IsNullOrEmpty(noise) : kind == "glow" && !string.IsNullOrEmpty(gradient);
        public bool Glow => kind == "glow";
        public bool Core => kind == "core";

        /// <summary>Its maps: (path, linear, normal map).</summary>
        public System.Collections.Generic.IEnumerable<(string path, bool linear, bool normal)> Textures()
        {
            if (!On) yield break;
            foreach (var t in new[] { (noise, true, false), (height, true, false), (curve, true, false), (sphereNormal, true, false), (ringNormal, true, false), (gradient, true, false) })
                if (!string.IsNullOrEmpty(t.Item1)) yield return t;
        }
    }

    /// <summary>A material's "wave" block (GoF2/Hull's GlowWave; ModMaterials.GlowWave), after Chorus's M_Phoenix (its compiled
    /// base pass): the 'pattern' (R its strength, G and B its phase) over UV0 x 'tiling'; at each texel the phase
    /// frac(G - B - time x 'speed') makes a triangle wave t (0 at its ends, 1 in its middle) that runs along the pattern;
    /// the glow lerp('min', 'max', t) x t x R x the 'mask' image's 'channel' (0 R, 1 G, 2 B; over UV0) x 'multiplier', added to
    /// the emission. Both images read as data.</summary>
    /// <summary>Chorus's hit flash (M_Phoenix's compiled base pass): for 'duration' seconds after a hit, 'color' (HDR: HitColor x
    /// HitEmissiveStrength) fading out, in a ring growing from the hit to 'radius' (game units) and a fresnel^7 glow within it,
    /// x the pattern (the wave pattern's alpha: T_Lu_Noise_29 at 2 uv and at 5 uv turning, ((a + b) / 2)^4) with 'pattern'.</summary>
    [System.Serializable] public class HitFlashSpec
    {
        public float[] color;
        public float radius = 648f, duration = 0.04f;
        public bool pattern;
    }

    [System.Serializable] public class GlowWaveSpec
    {
        public string pattern, mask;
        public int channel = 1;
        public float tiling = 1f, speed = 1f, multiplier = 1f;
        public float[] min, max;
        public bool On => !string.IsNullOrEmpty(pattern) && !string.IsNullOrEmpty(mask);
    }

    /// <summary>A material's "rift" block: Chorus's rift vortex (M_PortalRift_2, on SM_Rift_Vortex), drawn added by
    /// GoF2/ChorusRift. Its maps (each read as data, R unless said): 'tiled' (T_HeroRift_Tileable), 'strip' (the bolts' 1D
    /// pulse), 'lightning' / 'cloudMask' (N x N flipbooks, 'frames' N, over UV0 / UV1), 'cloud' (R the clouds, G their
    /// height), 'rock' (the dark background). Each "...UV" is [tiling u, v, pan u, v a second]; the rest as the material
    /// instance's parameters (see the shader's header). 'exposure' scales the whole (Chorus's HDR to the remake's).</summary>
    [System.Serializable] public class ChorusRiftSpec
    {
        public string tiled, strip, lightning, cloudMask, cloud, rock;
        public float[] mainUV, detailUV, cloudsUV, rockUV, detailOffset, heightPan;
        public float mainPow = 0.7f, detailPow = 7f, detailMul = 1f, redCenter = 1794f, cloudsPow = 2f, heightRatio = 1.5f;
        public float cloudPow = 15f, cloudMul = 100f, boltSpeed = 0.1f, pulseRate1 = 0.05f, pulseRate2 = 0.02f, sequenceTime = 0.1f;
        public float frames = 4f, lightningMul = 100000f, lightningCloud = 200f, gradation = 4f;
        public float blendExp = 1f, blendMult = 1f, corePow = 35f, coreTint = 0.05f, exposure = 0.4f;
        public float[] inner, outer, core, brightness, background, clouds;
        /// <summary>Remake (2026-11-11): "bottom": Chorus's M_PortalRift_BOTTOM (the Rift seen from inside, LocRFA's vortex):
        /// one 'emissive' colour for the inner / outer blend, the rock in 'baseColor', the bolts and the cloud lightning not
        /// held to the red centre, the clouds in the emissive colour without the g^2; alpha-blended, fading out within
        /// 'depthFade' of what is behind it and nearer than 'fadeOffset' + 'fadeLength' to the camera (game units).</summary>
        public string mode;
        public float[] emissive, baseColor;
        public float depthFade, fadeOffset, fadeLength;
        /// <summary>The vertex shader's world position offset (M_PortalRift_2's / _BOTTOM's: the vertex along its normal by
        /// 'wpoMap' (R) at uv x tiling + t x pan, x saturate(5 g (1 - g)) x the strength): 'wpo' [strength in the model's
        /// own units, tiling u, v, pan u, v]; none = still.</summary>
        public string wpoMap;
        public float[] wpo;
        public bool Bottom => string.Equals(mode, "bottom", System.StringComparison.OrdinalIgnoreCase);
        public bool On => !string.IsNullOrEmpty(tiled);

        /// <summary>Its maps: (path, linear, normal map).</summary>
        public System.Collections.Generic.IEnumerable<(string path, bool linear, bool normal)> Textures()
        {
            if (!On) yield break;
            foreach (var t in new[] { tiled, strip, lightning, cloudMask, cloud, rock, wpoMap })
                if (!string.IsNullOrEmpty(t)) yield return (t, true, false);
        }
    }

    /// <summary>A material's "shield" block: Chorus's M_Shield (its compiled translucent pass on MI_Shield_PlayerCapital_Auxiliary),
    /// drawn added by GoF2/ChorusShield on both faces; the formula is in the shader's header. 'mask' (R, over UV0 x
    /// 'noiseScale', panned by 'pan' [u1, v1, u2, v2]), 'noise' (R, over UV1); lengths ('radius' [start, end], 'ring' [radius
    /// start, end, width start, end]) in the model's own units from its origin; 'cameraFade' [offset, length] and 'depthFade'
    /// in game units; 'exposure' scales it (Chorus's HDR to the remake's).</summary>
    /// <summary>A material's "lava" block: Chorus's flowing lava (M_BrokenPlanet_Core's compiled base pass) added to the
    /// glow of a GoF2/Hull material (HullForwardPass.hlsl LavaFlow, its formula there). Its three maps, read as data: 'emit'
    /// (R the first flow emissive, G the second), 'flow' (RG the first flowmap, BA the second), 'masks' (R / G the time
    /// offsets, B the part mask, A the core mask); 'a' [Magma Tiling, FlowSpeed, Flowmap Tiling, FlowIntensity], 'b'
    /// [TimeOffset Tiling, its intensity, Flowmap UV Texture Offset u, v], 'c' [2nd Magma Tiling, FlowSpeed 2, Flowmap Tiling
    /// 2, FlowIntensity 2], 'd' [TimeOffset 2 Tiling, 2nd core mask multiply, its exp, 2nd magma multiply], 'magma2Exp',
    /// 'tempMin' / 'tempMax' (K), 'mip' / 'mip2' the flowmaps' mip levels, 'glow' the glow's scale.</summary>
    [System.Serializable] public class ChorusLavaSpec
    {
        public string emit, flow, masks;
        public float[] a, b, c, d;
        public float magma2Exp = 1f, tempMin = 400f, tempMax = 3000f, mip, mip2, glow = 1f;
        public bool On => !string.IsNullOrEmpty(emit) && !string.IsNullOrEmpty(flow) && !string.IsNullOrEmpty(masks);
    }

    [System.Serializable] public class ChorusShieldSpec
    {
        public string mask, noise;
        public float[] pan, radius, ring, cameraFade;
        public float noiseScale = 4f, noiseTimeScalar = 0.01f, noisePanSpeed = 0.01f, timeExponent = 0.37f;
        public float edgeModulateAmount = 0.2f, edgeModulateIntensity = 10f, edgeModulateWidth = 0.08f, fresnelExponent = 1f;
        public float modulateColorAmount = 0.3f, timeModulationAmount = 0.2f, opacityNoise = 0.571429f;
        public float radiusExponent = 3f, radiusIntensity = 1f, ringExponent = 3f, ringIntensity = 1f;
        public float opacityStart = 1f, opacityEnd, opacityFacing = 1f, opacityBackface = 0.1f, depthFade, exposure = 1f;
        public bool On => !string.IsNullOrEmpty(mask);

        /// <summary>Its maps: (path, linear, normal map).</summary>
        public System.Collections.Generic.IEnumerable<(string path, bool linear, bool normal)> Textures()
        {
            if (!On) yield break;
            yield return (mask, true, false);
            if (!string.IsNullOrEmpty(noise)) yield return (noise, true, false);
        }
    }

    [System.Serializable] public class ChorusLayersSpec
    {
        public string kind;   // "trim" or "plating"; empty = off
        public string palette, tilingDiffuse, tilingRmo, tilingNormal, trimAo, trimNormal, trimEmissive, dirt, dirtMask, dirtRoughness;
        public float tiling = 1f, aoIntensity = 1f, emissionScale = 1f;
        public float[] accentColor;
        public float accentMetal, accentRoughness, accentNormal = 1f;
        public float[] emissiveR, emissiveG, emissiveB;
        public float emissiveAmount = 1f, distanceBoost = 1f, nearEmission = 1f;
        public bool emissiveUvShift;
        public float[] kickerColor;
        public float kickerExp, kickerFadeOffset, kickerFadeLength = 1f;
        public float[] dirtTint;
        public float dirtUvScale = 1f, dirtMaskUvScale = 1f, dirtAo = 1f, dirtContrast;
        public bool dirtAlpha, dirtByAlpha, dirtOverEmissive;
        // kind "shell" (Chorus's M_Guilt_Shell, its compiled base, depth and vertex passes): no palette; 'mask' (T_Guilt_A: R the
        // body, G the arms), 'noise' (the grey that flows), 'flow' (RG its flow vectors), 'phase' (R the flow's offset) over UV0.
        // A noise flowing at flowSpeed along the flow x flowStrength over UV0 x tiling (the phase's at UV0 x randomness) dissolves
        // it: ((mask.r + armsFade) x 0.5 + a dither - 0.5) x saturate(2 coverage - (1 - noise) x (1 - mask.g)^erode) - (1 -
        // masterAlpha) >= 1/3; pushes it out along its normals by push + pushNoise x (1 - noise) (model units); glows (the mask
        // flowing the same way - emissiveExponent) x emissiveColor x emissiveIntensity x saturate(1 - distance from the model's
        // origin / centerRadius)^2; base colour, metallic and roughness flat.
        public string mask, noise, flow, phase;
        // Remake (2026-11-11): 'glow' the map that flows for the glow (M_Guilt_Shell's: the obsidian rock's AO; none: the
        // mask); the noise map's red and green are also its normal (the base pass's, Unreal's green flipped).
        public string glow;
        public float randomness = 1f, flowSpeed = 1f, flowStrength = 1f, armsFade = 1f, coverage = 0.5f, erode = 1f, masterAlpha = 1f;
        public float push, pushNoise, baseColor, metal = 0.5f, roughness = 0.5f, emissiveExponent = 1f, emissiveIntensity, centerRadius = 1f;
        public float[] emissiveColor;
        // kind "crystal" (Chorus's M_JTU_Blend, its compiled base pass: the Omen crystal, the Prophet's crown; HullForwardPass.hlsl
        // ChorusCrystal): two surfaces A and B ('diffuseA' / 'normalA' / 'mroA' (R metal, G roughness) / 'maskA' (R), and B's),
        // each mapped along the mesh's tangent frame at 'scaleA' / 'scaleB' (Chorus centimetres; the model's units its metres),
        // blended by the vertex colour's red sharpened by a turbulence ('noiseScale', 'noiseContrast', 'noiseOffset') and the
        // breath, against maskA as a height ('transitionSoftness'); A tinted to 'macroColor' by maskA at x 'macroTiling'
        // ('macroAmount'; its roughness toward 'macroRoughness' by 'macroRoughnessAmount'); both overlaid with the inner
        // 'variation' (G), looked up at the position / 'innerScale' pushed along the view by the 'heightmap' (R) x
        // 'innerHeightRatio' and along the normal by 'innerRefraction'; the glow: variation x 'innerGlow' x the breath ('breath':
        // a curve row, R the glow, G added to the blend x 'breathAmount', at frac(time / 'breathInterval' - the distance from
        // the origin / 'pulsePeriod'); a breathInterval of 0 holds it at the row's start) x maskA x 'glowA', or maskB x
        // 'glowPulseB' x 'glowB'; normals x
        // 'normalIntensityA' / 'B', roughness + 'roughnessBiasA' / 'B'; x emissionScale.
        public string diffuseA, normalA, mroA, maskA, diffuseB, normalB, mroB, maskB, heightmap, variation, breath;
        public float scaleA = 25000f, scaleB = 30000f, normalIntensityA = 1f, normalIntensityB = 1f, roughnessBiasA, roughnessBiasB;
        public float[] macroColor, innerGlow;
        public float macroAmount = 1f, macroTiling = 0.05f, macroRoughness = 0.2f, macroRoughnessAmount = 0.5f;
        public float glowA = 2f, glowPulseB = 1f, glowB = 5f, innerRefraction = 1f, innerHeightRatio = 0.37f, innerScale = 150000f;
        public float noiseScale = 50000f, noiseContrast = 0.1f, noiseOffset, breathInterval = 8f, pulsePeriod = 4000000f, breathAmount = 1f;
        public float transitionSoftness = 0.44f;
        // kind "tissue" (Chorus's M_JTU_Faceless, its compiled base pass: the Omen's Faceless ships; GoF2/Hull's ChorusTissue): all
        // glow on a black base, over UV0: 'thickness' (R its thickness, G toward smooth), 'outerVariation' / 'innerVariation'
        // (R) through a parallax of (thickness - 0.5) x 'depth', 'dirt' (B) and the 'ice' mask (R, scrolling 0.1 a second) at x
        // 'noiseTiling', 'normal', 'ao'; lerp('outerGlow' x (dirt + outer), 'innerGlow' x (ice x the inverse fresnel + inner),
        // clamp((outer + inner) x saturate(1 / (thickness x 'strength')) + the breath, 0.5, 1)) (the breath as the crystal's:
        // 'breath', 'breathInterval', 'pulsePeriod'); roughness lerp('roughnessIntensity', 0.05, thickness G); x emissionScale.
        public string thickness, outerVariation, innerVariation, ice, normal, ao;
        public float[] outerGlow;   // (and 'innerGlow', 'dirt', 'breath' as the other kinds')
        public float noiseTiling = 1f, depth = 0.1f, strength = 1f, roughnessIntensity = 1f;
        // Its core (Enable Core: the Omen drones' and the bat swarms' cores): + m x (the glow + saturate(N.V)^'coreContrast' x
        // 'coreColor'), m = saturate(1 - the distance from 'sphereOffset' / 'sphereRadius' (object space, Chorus's cm)) x the
        // thickness x the inverse fresnel^5. No 'coreColor' or a 'coreContrast' of 0: none.
        public float[] coreColor, sphereOffset;
        public float coreContrast, sphereRadius = 1000000f;

        public bool On => !string.IsNullOrEmpty(kind) && (Shell ? !string.IsNullOrEmpty(mask) : Crystal ? !string.IsNullOrEmpty(diffuseA)
            : Tissue ? !string.IsNullOrEmpty(thickness) : !string.IsNullOrEmpty(palette));
        public bool Plating => kind == "plating";
        public bool Shell => kind == "shell";
        public bool Crystal => kind == "crystal";
        public bool Tissue => kind == "tissue";

        /// <summary>The PNGs it reads, and how (linear, normal map): ModMaterials.ChorusLayers, the preloads.</summary>
        public System.Collections.Generic.IEnumerable<(string path, bool linear, bool normal)> Textures()
        {
            if (!On) yield break;
            foreach (var t in new[] { (palette, true, false), (tilingDiffuse, false, false), (tilingRmo, true, false), (tilingNormal, true, true),
                                      (trimAo, true, false), (trimNormal, true, true), (trimEmissive, false, false), (dirt, false, false),
                                      (dirtMask, true, false), (dirtRoughness, true, false), (mask, true, false), (noise, true, false),
                                      (flow, true, false), (phase, true, false), (glow, true, false), (diffuseA, false, false), (normalA, true, true),
                                      (mroA, true, false), (maskA, true, false), (diffuseB, false, false), (normalB, true, true), (mroB, true, false),
                                      (maskB, true, false), (heightmap, true, false), (variation, true, false), (breath, true, false),
                                      (thickness, true, false), (outerVariation, true, false), (innerVariation, true, false), (ice, true, false),
                                      (normal, true, true), (ao, true, false) })
                if (!string.IsNullOrEmpty(t.Item1)) yield return t;
        }
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
        // Remake mods (Modding.ModWanted, a mod's wanted.json): its key ("mod_id:id"; null = the game's), its own texts and
        // face (a mod character), the systems it roams (null = anywhere), its trips' length in systems, a rocket launcher, and
        // when it is on its board ("available", a condition); its escorts' ships (null = the race's fighters), its own fight
        // music (a mod track) and what it says when uncovered; what it takes of the player's weapons' damage (null = all) and
        // its devices (Flight.NpcDeviceSpec, World.NpcDevices; null = none).
        [System.NonSerialized] public string key, description, character, available, availableWhere, music, taunt;
        [System.NonSerialized] public DamageTaken damageTaken;
        [System.NonSerialized] public List<NpcDeviceSpec> devices;
        [System.NonSerialized] public List<int> turrets;   // its turret items (null = its ship type's, Database.NpcTurrets)
        [System.NonSerialized] public string skin;         // remake mods: a kit skin its ship wears ("skin", ModTraffic.Assembly)
        [System.NonSerialized] public int[] systems, escorts;
        [System.NonSerialized] public int routeMin = 2, routeMax = 4, secondary = -1;
        [System.NonSerialized] public Modding.ModInfo mod;
        public bool IsMod => key != null;
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
        /// <summary>Remake mods (wingmen.json, Modding.ModWingmen): the crews the bars' wingmen offers may fly, each mod's.</summary>
        public List<WingmanCrew> WingmanCrews = new List<WingmanCrew>();
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
        static string sharedCampaign;
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
                if (shared == null || sharedEconomy != Session.Economy || sharedRevision != Modding.ModManager.Revision
                    || sharedCampaign != Session.ModCampaign)   // a mod campaign's scripts may change it for that campaign only (#313)
                {
                    sharedEconomy = Session.Economy;
                    sharedCampaign = Session.ModCampaign;
                    sharedRevision = Modding.ModManager.Revision;
                    shared = Load();
                    // (the load may have made the first scan of the Mods folders, which bumps the revision: it is this one's)
                    sharedRevision = Modding.ModManager.Revision;
                }
                return shared;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetShared()
        {
            shared = null; sharedRevision = -1; applying = null;
            GameContent.Refreshed -= ResetShared;
            GameContent.Refreshed += ResetShared;   // (the player's copy converted: the tables are read again)
        }

        /// <summary>The Cronus' price in the finished world (multiplayer): see Load.</summary>
        public const int CronusSessionPrice = 1_150_000;

        public static Database Load(string resourceFolder = "GoF2Data")
        {
            using var timing = ContentTimings.Measure("database");
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
                try
                {
                    Modding.ModContent.Apply(db);
                    Modding.ModScripts.Raise("database", db);   // the mod scripts' own changes to the tables, last
                    GameHooks.ApplyPlayerStations?.Invoke(db);   // persistent multiplayer stations follow static and mod stations
                }
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
            var text = GoF2Remake.Data.GameContent.Text(folder + "/" + file);   // (the player's converted copy, else Resources)
            if (text == null)
            {
                Debug.LogWarning("Database: missing Resources/" + folder + "/" + file + ".json");
                return new T();
            }
            return JsonUtility.FromJson<Wrapper<T>>("{\"list\":" + text + "}").list;
        }

        [System.Serializable] class AssemblyFile { public List<AssemblyData> entries; }

        static List<AssemblyData> ReadAssemblies(string folder)
        {
            var text = GoF2Remake.Data.GameContent.Text(folder + "/assemblies");
            return text != null ? JsonUtility.FromJson<AssemblyFile>(text).entries : new List<AssemblyData>();
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
            // Remake external docking: slotType 8 is a ship-authored docking hardpoint, not a build-part weapon socket.
            // Keep it on the ship definition even when a kit build supplies the weapon / exhaust mounts.
            if (slotType == 8)
            {
                var dockingSet = WeaponMounts.FirstOrDefault(m => m.ship == shipIndex);
                return dockingSet != null ? dockingSet.mounts.Where(m => m.slotType == 8).ToList() : new List<WeaponMount>();
            }
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
            fs.rules = Modding.ModFlight.RulesFor(ship);   // the mods' flight.json, then the ship's own "rules"
            // The single-style fields (the original's share) for whatever reads them directly.
            fs.boostSpeed = styles[0].boostSpeed; fs.boostDurationMs = styles[0].boostDurationMs;
            fs.boostRechargeMs = styles[0].boostRechargeMs; fs.agility = styles[0].agility;
            return fs;
        }
    }
}
