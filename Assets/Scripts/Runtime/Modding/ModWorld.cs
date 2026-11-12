// ModWorld.cs
// Mods' star systems and stations (systems.json / stations.json), merged into Database.Systems / Stations after the
// original 34 systems and 135 stations (ModContent.Apply calls Apply). See Modding/README.md.
//
// systems.json: { "id", "name", "race" (0 Terran, 1 Vossk, 2 Nivelian, 3 Midorian), "security" (0-3), "visible",
//   "position": [x, y, z] (the star map, the originals 15-94 / 2-96 / 10-90), "sky" (the nebula / sun set, 0-18 like
//   the originals' textureIndex), "gates": ["mod:id" | number, ...] (jumpgate routes, both ways: the other system gets
//   the route back), "gateStation" (the station orbit with the jumpgate; default its first station) } or
//   { "override": n | "mod:id", name, security, visible, gates (added) }; both may name "spaceMusic" / "stationMusic" (a
//   mod's track, ModMusic: the calm space music in the system's orbits, the music docked at its stations) and
//   "battleMusic" (its fights' music: one track, or [low, medium, full] for GoF2's three battle tracks by the hostiles
//   around, Traffic's music categories 1-3).
//   "race" may also name a mod's race (ModRaces: "mod_id:id" or the mod's own id): it owns the system (its locals,
//   lounge visitors, docking fines; its hangar and bar its "look"). Remake mods, also on overrides: "traffic": "mod" (only
//   mods' ships fly there: no game fighters, freighters or capital ships, no GoF2 pirate events; ModTraffic and
//   ModCapitalShips without the "Mod ships in traffic" chance), "locals": [races / mod races] (the local fighters' sides,
//   default the owner), "raiders": [races / mod races] (who raids it, default the owner's hostile enemies, else pirates).
//   Both may name "available": a condition (ModUnlocks, the event graphs' expressions): while it holds the system is on
//   the map, while it doesn't it is hidden, revealed or not (GalaxyMap.Visibility); a player in it is taken out.
// stations.json may name "music" too (that station's docked music).
// Looks (new or override): a station's own 3D model ("model": a glTF / GLB, "modelSize" its largest extent in game units
// (default 40000 = 2 km; the originals are 1-5 km), "modelYaw" degrees, "modelCentre" (default true: the bounds' centre at
// the station's origin), "materials" like ships.json's; its collision "collision": "box" (default: the model's bounds),
// "sphere", "none", or "volumes": [{ "box": [x, y, z, hx, hy, hz] } | { "sphere": [x, y, z, r] }] in game units around the
// station; built by ModStations, "looksLike" stands in while it loads or when it fails), "interior" (the hangar and bar:
// terran / vossk / nivelian / midorian or 0-3; default the system's race), "planetTexture" (a PNG with transparency: the
// station's planet, seen from its own orbit and the system's others), "mapTexture" (the star map's planet: an image wrapped
// round the map's sphere, a 2:1 one like a world map; without it a "planetTexture" faces the map's camera as a disc, else the
// game's planet for "planet"), "mapRing" (true: a ring round the map's planet, like the planet ring orbits'), "mapPlace"
// ({ "angle": degrees, "distance": map units }: the planet's place in the star map's system view, else the seeded one), "orbit"
// (the orbit level's layout over the seeded one, OrbitSpec: station shown / without docking / none, the visible jumpgate and
// its position, where travel arrives, docking pads outside the station ("pads": [{ "position", "approach", "facing" }] like
// npcships.json's: docked at like the story's objects, SpaceLevel.SpawnStationPads), the asteroid field (none, count, centre, spread, kind, ores), fog, the sun's and the
// orbit planet's direction and size); systems: "sunTexture" (a PNG), "sunColor" [r, g, b]
// 0..1 (the sun's light; default from "sky").
// stations.json: { "id", "system": "mod:id" | number, "name", "techLevel" (0-10), "planet" (its planet texture: 0-22 or
//   24-26 like the originals), "looksLike": a station number (its model and collision; default the first original
//   station of the system's race) } or { "override": n | "mod:id", name, techLevel }. Both may have "subtitle" (what the
//   place is, under its name on the star map) and "description" (its lore: the star map's planet panel, and items of its
//   system's news ticker), each a string or one per language like "name".
// A new station may be "mobile": true or { "route", "stayMinutes", "speed", "race" } (MobileStations, MobileSpec): no planet
//   (not one of any system's stations, not on the star map), it cruises through the orbits of its route's stations in turn
//   (any systems; default its own system's) and is docked at where it is: its "system" is its home, while each Database.Load
//   gives it the system of the orbit it is in (its header, prices, the star map's "you are here"); its race (its hangar,
//   the header's race) stays "race", else its interior's, else its home system's; its title has no "Station".
// A system takes at most 7 stations (OrbitLayout places 7 planet slots around the sun). Numbering as ModContent: the
// registry in single player (a mod turned off leaves placeholders: a hidden system without stations or routes, a
// station in no system; ModSaves moves a save docked there to Var Hastra), afresh in a session.

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static partial class ModWorld
    {
        public const string SystemsFile = "systems.json", StationsFile = "stations.json";
        public const int MaxStationsPerSystem = 7;
        public const int RefugeStation = 78;   // Var Hastra: where a save at a station that is gone docks

        public class Def
        {
            public ModInfo mod;
            public JObject json;
            public string where, localId, key, overrideRef, file;
            public Dictionary<string, string> name;
        }

        static int originalSystems = -1, originalStations = -1, mappedRevision = -1;
        /// <summary>EnsureMapping is loading the tables to learn the originals' counts.</summary>
        static bool loadingOriginals;
        static readonly Dictionary<string, int> systemIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        static readonly Dictionary<string, int> stationIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        static readonly Dictionary<int, Def> systemAt = new Dictionary<int, Def>(), stationAt = new Dictionary<int, Def>();
        static readonly Dictionary<int, string> missingSystems = new Dictionary<int, string>(), missingStations = new Dictionary<int, string>();
        static readonly Dictionary<int, int> stationLook = new Dictionary<int, int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { originalSystems = originalStations = mappedRevision = -1; }

        public static int OriginalSystemCount => originalSystems;
        public static int OriginalStationCount => originalStations;

        // ---- parsing -------------------------------------------------------------------------------------------------

        static readonly HashSet<string> SystemFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "id", "override", "name", "race", "security", "visible", "position", "sky", "gates", "gateStation", "spaceMusic", "stationMusic",
              "battleMusic", "sunTexture", "sunColor", "skybox", "available", "traffic", "locals", "raiders", "jumpgate", "pirateHeld" };
        static readonly HashSet<string> StationFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "id", "override", "name", "system", "techLevel", "planet", "looksLike", "music", "model", "modelSize", "modelYaw", "modelCentre",
              "materials", "collision", "volumes", "interior", "planetTexture", "mapTexture", "mapRing", "mapPlace", "orbit", "hangar", "bar", "mobile",
              "subtitle", "description", "life", "planetSphere" };

        /// <summary>ModContent.Parse: a mod's systems.json and stations.json into 'systems' / 'stations'.</summary>
        public static void Parse(ModInfo mod, List<Def> systems, List<Def> stations)
        {
            Read(mod, SystemsFile, SystemFields, systems, (o, d) =>
            {
                if (!(ModJson.Get(o, "race") is JToken rt) || rt.Type != JTokenType.String) ModJson.Int(o, "race", 0, d.file);   // (a mod race: resolved in Apply)
                ModJson.Int(o, "security", 0, d.file); ModJson.Int(o, "sky", 0, d.file);
                if (ModJson.Str(o, "traffic") is string tr && tr != "mod" && tr != "all")
                    throw new ModJsonException($"{d.where}: \"traffic\" must be \"mod\" (only mods' ships) or \"all\"");
                if (ModJson.Has(o, "sunColor") && !(ModJson.Get(o, "sunColor") is JArray c && c.Count == 3))
                    throw new ModJsonException($"{d.where}: \"sunColor\" must be [r, g, b] (0..1)");
                if (ModJson.Get(o, "skybox") is JToken sb) ParseSkybox(mod, sb, d.where);
                if (ModJson.Get(o, "jumpgate") is JToken jg && !(jg is JObject jo && ModJson.Str(jo, "model") != null))
                    throw new ModJsonException($"{d.where}: \"jumpgate\" must be {{ \"model\": \"models/gate.glb\", \"size\": 15000 }}");
                if (d.localId != null)
                {
                    if (!(ModJson.Get(o, "position") is JArray p) || p.Count != 3)
                        throw new ModJsonException($"{d.where}: a new system needs \"position\": [x, y, z] on the star map");
                    if (!(ModJson.Get(o, "race") is JToken r2) || r2.Type != JTokenType.String)
                    {
                        int race = ModJson.Int(o, "race", 0, d.file);
                        if (race < 0 || race > 3) throw new ModJsonException($"{d.where}: \"race\" must be 0-3 (Terran, Vossk, Nivelian, Midorian) or a mod race");
                    }
                }
            });
            Read(mod, StationsFile, StationFields, stations, (o, d) =>
            {
                ModJson.Int(o, "techLevel", 0, d.file); ModJson.Int(o, "planet", 0, d.file); ModJson.Int(o, "looksLike", -1, d.file);
                if (d.localId != null && !ModJson.Has(o, "system")) throw new ModJsonException($"{d.where}: a new station needs \"system\"");
                int planet = ModJson.Int(o, "planet", 0, d.file);
                if (planet < 0 || planet > 26 || planet == 23) throw new ModJsonException($"{d.where}: \"planet\" must be 0-22 or 24-26 (the game's planet textures)");
                if (ModJson.Has(o, "interior") && InteriorOf(o) < 0)
                    throw new ModJsonException($"{d.where}: \"interior\" must be terran, vossk, nivelian, midorian or 0-3");
                string collision = ModJson.Str(o, "collision", "box");
                if (collision != "box" && collision != "sphere" && collision != "none")
                    throw new ModJsonException($"{d.where}: \"collision\" must be box, sphere or none");
                ReadVolumes(o, d);   // checks the list
                if (ModJson.Has(o, "materials") && !(ModJson.Get(o, "materials") is JArray))
                    throw new ModJsonException($"{d.where}: \"materials\" must be a list like ships.json's");
                foreach (string image in new[] { "planetTexture", "mapTexture" })
                    if (ModJson.Str(o, image) is string file && !mod.Source.Exists(file))
                        throw new ModJsonException($"{d.where}: the {image} {file} isn't in the mod");
                ModJson.Bool(o, "mapRing", false, d.file);
                if (ModJson.Get(o, "orbit") is JToken orbitToken) ParseOrbit(mod, orbitToken, d.where, null);
                if (ModJson.Get(o, "life") is JToken lifeToken)
                {
                    if (!ModJson.Has(o, "model")) throw new ModJsonException($"{d.where}: \"life\" needs the station's own \"model\"");
                    ParseLife(mod, lifeToken, d.where);
                }
                if (ModJson.Get(o, "planetSphere") is JToken sphereToken) ParsePlanetSphere(mod, sphereToken, d.where, check: true);
                if (ModJson.Get(o, "mapPlace") is JToken placeToken) ParseMapPlace(placeToken, d.where);
                if (ModJson.Get(o, "mobile") is JToken mobileToken)
                {
                    if (d.localId == null) throw new ModJsonException($"{d.where}: only a new station can be \"mobile\"");
                    ParseMobile(mobileToken, d.where, d.file);
                }
            });

        }

        static void Read(ModInfo mod, string file, HashSet<string> fields, List<Def> into, Action<JObject, Def> check)
        {
            var token = ModJson.Read(mod.Source, file);
            if (token == null) return;
            if (!(token is JArray list)) throw new ModJsonException($"{file}: must be a list [ {{ ... }}, ... ]");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in list)
            {
                string where = ModJson.Where(t, file);
                if (!(t is JObject o)) throw new ModJsonException($"{where}: each entry must be an object {{ ... }}");
                var d = new Def { mod = mod, json = o, where = where, file = file, localId = ModJson.Str(o, "id"),
                                  overrideRef = ModJson.Str(o, "override"), name = ModJson.Text(o, "name") };
                if ((d.localId == null) == (d.overrideRef == null))
                    throw new ModJsonException($"{where}: an entry needs either \"id\" (a new one) or \"override\" (change an existing one)");
                if (d.localId != null)
                {
                    if (!ModManifest.ValidId(d.localId)) throw new ModJsonException($"{where}: the id \"{d.localId}\" may only use a-z, 0-9, _ and -");
                    if (!ids.Add(d.localId)) throw new ModJsonException($"{where}: the id \"{d.localId}\" is used twice");
                    if (d.name == null) throw new ModJsonException($"{where}: a new entry needs \"name\"");
                    d.key = mod.Id + ":" + d.localId;
                }
                foreach (var prop in o.Properties())
                    if (!fields.Contains(prop.Name)) mod.Warnings.Add($"{ModJson.Where(prop, file)}: unknown field \"{prop.Name}\" (ignored)");
                check(o, d);
                into.Add(d);
            }
        }

        // ---- numbering -----------------------------------------------------------------------------------------------

        /// <summary>A station's own 3D model (stations.json "model"), built by ModStations.</summary>
        public class StationModel
        {
            public ModInfo mod;
            public int station;
            public string model, collision = "box", label;
            public float size = 40000f, yaw;
            public bool centre = true;
            public List<CustomShipMaterial> materials = new List<CustomShipMaterial>();
            public List<(bool sphere, Vector3 centre, Vector3 half, float radius)> volumes;   // game units; null = from "collision"
            /// <summary>A system's own jumpgate (systems.json "jumpgate"): its system, else -1 (a station's model).</summary>
            public int gateSystem = -1;
            /// <summary>The gate's collision and approach radius in game units (0: the race gate's).</summary>
            public float gateRadius;
            public string Assembly => gateSystem >= 0 ? $"modgate_{gateSystem:000}" : $"modstation_{station:000}";
            /// <summary>Remake (2026-11-04): its station life (stations.json "life", World.StationLife), null = none.</summary>
            public LifeSpec life;
        }

        /// <summary>Remake (2026-11-04): a station model's life, EVERSPACE 2's worker drones (stations.json "life",
        /// World.StationLife): loops of drones round its structures that stop now and then to weld its hull. The drone is the
        /// model file's scene "scene" (its first node; nose on +z). Everything in the model's own frame: its glTF metres as
        /// the file has them, before modelYaw, modelSize and the centring (which the drones follow with the model).</summary>
        public sealed class LifeSpec
        {
            public string scene = "life";
            public List<LifeLoop> loops = new List<LifeLoop>();
        }

        public sealed class LifeLoop
        {
            /// <summary>The model's glTF metres, at least two.</summary>
            public List<Vector3> points = new List<Vector3>();
            public int count = 4;
            /// <summary>The model's metres a second (ES2's BaseSpeed 2000 cm/s: 20).</summary>
            public float speed = 20f, laneOffset = 5f;
            public bool loop = true, twoLanes = true;
            /// <summary>Where its drones weld: a spot on the hull and its outward normal (the model's glTF frame).</summary>
            public List<(Vector3 at, Vector3 normal)> spots = new List<(Vector3, Vector3)>();
        }

        static readonly Dictionary<int, StationModel> gateModels = new Dictionary<int, StationModel>();

        static readonly Dictionary<int, StationModel> stationModels = new Dictionary<int, StationModel>();
        static readonly Dictionary<int, int> stationInterior = new Dictionary<int, int>();
        static readonly Dictionary<int, string> stationHangar = new Dictionary<int, string>(), stationBar = new Dictionary<int, string>();

        const string NoBarKey = "none";
        /// <summary>Remake (2026-11-04): a station's planet drawn as a lit sphere (stations.json "planetSphere", World.Backdrop),
        /// after EVERSPACE 2's planet materials: 'look' the master it follows (es2Desert), its maps (mod paths) by role, its
        /// atmosphere's colour (linear RGB, null: none).</summary>
        public sealed class PlanetSphereSpec
        {
            public ModInfo mod;
            public string look;
            public readonly Dictionary<string, string> maps = new Dictionary<string, string>();
            public Color? atmosphere;
            /// <summary>2026-11-06, the gas giants: the master's two colours (linear, as ES2's material holds them: GasSmooth's
            /// Color / Color2, Gas's SecondaryColor / BaseColor) and the planet's Variation (0..1: its hue turned by x 2 pi).</summary>
            public Vector3? color1, color2;
            public float variation;
        }
        public static readonly string[] PlanetSphereLooks = { "es2Desert", "es2GasSmooth", "es2Gas", "es2Cratered", "es2Volcanic", "es2Ice", "es2Water", "es2Earthlike" };
        static readonly Dictionary<int, PlanetSphereSpec> planetSpheres = new Dictionary<int, PlanetSphereSpec>();

        static readonly Dictionary<int, string> planetTextures = new Dictionary<int, string>(), sunTextures = new Dictionary<int, string>(),
                                                mapTextures = new Dictionary<int, string>();
        static readonly HashSet<int> mapRings = new HashSet<int>();
        static readonly Dictionary<int, Color> sunColors = new Dictionary<int, Color>();

        /// <summary>A station's orbit level laid out by its mod (stations.json "orbit"), applied by OrbitLayout.Build over the
        /// seeded layout; null fields keep the game's. Positions in game units around the station (the orbit's centre).</summary>
        /// <summary>Remake (2026-11-03): an orbit's hazard volume (stations.json "orbit.hazards", Flight.Hazards).</summary>
        public enum HazardKind { Heat, Radiation, Frost, Corrosion, Damage }
        public sealed class HazardSpec
        {
            public HazardKind kind;
            public Vector3 centre;
            /// <summary>Game units.</summary>
            public float radius;
            /// <summary>Heat: the damage a second at the centre; damage: everywhere inside.</summary>
            public float dps;
            /// <summary>Heat (2026-11-04): the damage a second at the centre as that many pirate raiders' gunfire
            /// (NpcTables.RaiderGunDps; EVERSPACE 2's DamagePerSecondMax over its Outlaw raider's 55 DPS, both scaled by level
            /// alike); over 'dps' when given. Damage and corrosion the same way (corrosion: a tick's damage, default ES2's
            /// intensity 5 over the raider's 55 DPS).</summary>
            public float raiderDps;
        }

        /// <summary>Remake (2026-11-03): an orbit's traffic lane (stations.json "orbit.lanes", World.TrafficLanes).</summary>
        public sealed class LaneSpec
        {
            /// <summary>Game units, at least two.</summary>
            public List<Vector3> points;
            /// <summary>Ship references ("12", "mod:id") its ships look like, null = the system race's fighters.</summary>
            public List<string> ships;
            public int count = Flight.TrafficLane.DefaultCount;
            /// <summary>Unity metres a second.</summary>
            public float speed = Flight.TrafficLane.DefaultSpeedMps;
            public bool loop = true, twoLanes = true;
        }

        /// <summary>Remake (2026-11-05, EVERSPACE 2's ambience zones, BP_MusicTrigger's OverrideAmbience): a sound bed in an orbit
        /// (stations.json "orbit.ambience", World.OrbitAmbience): one of 'loops' looping, one of 'adds' now and then.</summary>
        public sealed class AmbienceSpec
        {
            /// <summary>Mod sound names (sounds/): one picked at random loops; 'adds' are one-shots every addMinMs..addMaxMs.</summary>
            public List<string> loops, adds;
            public float volume = 1f, addVolume = 0.3f, addMinMs = 8000f, addMaxMs = 15000f;
            /// <summary>Game units; null = the whole orbit. Full volume within 'radius', fading out over half of it more.</summary>
            public Vector3? centre;
            public float radius = 30000f;
        }

        /// <summary>Remake (2026-11-06, EVERSPACE 2's BP_LightningField): a lightning storm round the camera (stations.json
        /// "orbit.storm", World.OrbitStorm): 'frequency' flashes a second on a cylinder 'radius' game units out, each a glow
        /// and a bolt 'scale' x ES2's sizes, 'intensity' x its brightness; a 'thunder' sound (one of the mod's) at 'thunderChance'.</summary>
        public sealed class StormSpec
        {
            public float frequency = 1f, radius = 80000f, heightPercent = 1f, scale = 1f, intensity = 1f, thunderChance = 0.2f;
            public List<string> thunder;
        }

        /// <summary>Remake (2026-11-05, EVERSPACE 2's plasma fields): the orbit's gas clouds (stations.json "orbit.gasClouds",
        /// World.GasCloudField) instead of GoF2's roll: their plasma and places; still only with a spectral filter mounted.</summary>
        public sealed class GasCloudSpec
        {
            /// <summary>An item reference ("201".."204" GoF2's plasmas, or "mod:id"); null = GoF2's pick for the system.</summary>
            public string plasma;
            /// <summary>Game units, one a cloud.</summary>
            public List<Vector3> clouds;
        }

        public sealed class OrbitSpec
        {
            /// <summary>Remake (2026-11-05, EVERSPACE 2's plasma fields): null = GoF2's own roll.</summary>
            public GasCloudSpec gasClouds;
            /// <summary>Remake (2026-11-05, EVERSPACE 2's location_spawns): the orbit's local fighters' and raiders' races (race
            /// words, numbers or mod races, resolved when used: ModWorld.Locals / Raiders), beating the system's; null = its.</summary>
            public List<string> locals, raiders;
            /// <summary>The mod whose stations.json gave the orbit (its races' and models' references).</summary>
            public ModInfo owner;
            /// <summary>Remake (2026-11-05, EVERSPACE 2's ambience zones): null = none.</summary>
            public List<AmbienceSpec> ambience;
            /// <summary>Remake (2026-11-06): the orbit's lightning storm (World.OrbitStorm), null = none.</summary>
            public StormSpec storm;
            /// <summary>Remake (2026-11-03, EVERSPACE 2's spline traffic): null = none.</summary>
            public List<LaneSpec> lanes;
            /// <summary>Remake (2026-11-03, EVERSPACE 2's heat volumes and radiation fields): null = none.</summary>
            public List<HazardSpec> hazards;
            /// <summary>-1 the game's; 0 no station (an empty orbit); 1 a station to dock at; 2 its model without docking.</summary>
            public int station = -1;
            public bool? jumpgate;
            public Vector3? jumpgatePosition, arrival;
            /// <summary>Remake (a modder's suggestion, the Luur mission's arms): docking pads outside the station, approach and
            /// dock points (SpacePoints, set SpacePoints.StationPadBase + the station), null = none.</summary>
            public List<SpacePoints.Point> pads;
            /// <summary>Remake: pirates hold the station (Data.PirateHold), null = as its system says ("pirateHeld").</summary>
            public bool? pirateHeld;
            /// <summary>Remake (2026-11-03, EVERSPACE 2's wreck fields): this many salvageable wreck containers (0 = none),
            /// within wreckSpread (game units, a cube's side) of wreckCentre (World.WreckField).</summary>
            public int wreckCount;
            /// <summary>Remake (2026-11-04): the orbit's EVERSPACE 2 location level (1-30: its encounters' pay,
            /// CruiseEncounterSpawner.Es2Level); 0 = the station's tech level x 3.</summary>
            public int level;
            public Vector3? wreckCentre;
            public float wreckSpread = 60000f;
            /// <summary>Remake (2026-11-05, EVERSPACE 2's wreck meshes): glTF / GLB wreck hulls in the mod laid among the wreck
            /// containers (wreckHulks of them, one at random each; the largest model wreckSize game units at its largest side, the
            /// others in proportion as authored) with their "materials"; null = containers only.</summary>
            public List<string> wreckModels;
            public List<CustomShipMaterial> wreckMaterials = new List<CustomShipMaterial>();
            public int wreckHulks = -1;   // (-1: as many as the models x 2, at most 8)
            public float wreckSize = 8000f;
            /// <summary>Remake (2026-11-03, EVERSPACE 2's item containers): this many closed containers (0 = none) within
            /// containerSpread (game units, a cube's side) of containerCentre, containerBulletproof of them bulletproof
            /// (World.LootContainer).</summary>
            public int containerCount;
            public Vector3? containerCentre;
            public float containerSpread = 40000f, containerBulletproof = 0.3f;
            /// <summary>Remake (2026-11-05, EVERSPACE 2's unique loot): fixed items in its containers, each entry once a game
            /// (Session.FixedLootTaken); null = none.</summary>
            public List<(string item, int amount)> containerLoot;
            /// <summary>Remake (2026-11-05, EVERSPACE 2's container meshes): glTF / GLB files in the mod for its containers and its
            /// bulletproof ones (Modding.ModCrates, fitted to the race's container), null = the race's container.</summary>
            public string containerModel, containerProofModel;
            /// <summary>Their materials, like ships.json's (by the model's mesh / material names); empty = the model's own.</summary>
            public List<CustomShipMaterial> containerMaterials = new List<CustomShipMaterial>();
            /// <summary>false: no asteroid field.</summary>
            public bool? asteroids;
            public int? asteroidCount;
            public Vector3? asteroidCentre;
            /// <summary>The field's size (game units: the small asteroids' cube side; the big ones in 0.6 of it). Default 100000.</summary>
            public float? asteroidSpread;
            /// <summary>-1 the game's; 0 rock, 1 Void crystal, 2 ice, 3 magma.</summary>
            public int asteroidType = -1;
            /// <summary>The ores (item, chance weight), null: the system's by distance like the game's.</summary>
            public List<(int item, int chance)> ores;
            /// <summary>Remake (2026-11-05, EVERSPACE 2's asteroid meshes): glTF / GLB files in the mod drawn for its asteroids
            /// (one at random each, as big as the game's asteroid; mining unchanged) with their "materials"; null = the game's.</summary>
            public List<string> asteroidModels;
            public List<CustomShipMaterial> asteroidMaterials = new List<CustomShipMaterial>();
            public bool? fog;
            public Color? fogColor;
            public float? fogEnd;
            /// <summary>The sun's direction: yaw (degrees, 0 straight ahead along the launch, + to the right) and pitch (+ up).</summary>
            public Vector2? sun;
            /// <summary>The orbit's own planet: its size (x the game's), yaw / pitch (degrees, 0 = straight ahead as the game's).</summary>
            public float? planetSize, planetYaw, planetPitch;
            /// <summary>The sky's turn in this orbit ([x, y, z] Unity degrees, as the system's skybox "rotation"), over the
            /// system's "rotation" / "sunAligned" and the game's per-station turn; null: those.</summary>
            public Vector3? skyRotation;
            /// <summary>The orbit's own sky (the system skybox's fields), in place of the system's; null: the system's.</summary>
            public Skybox skybox;
            /// <summary>The second light (GoF2's LIGHT1, the planet's): its colour (gamma, 0..2, black = none) and direction
            /// (yaw / pitch as "sun"); null: the game's. The scene's ambient (gamma 0..1), null: 0.15 x the sun's.</summary>
            public Color? light2Color, ambient;
            public Vector2? light2Direction;
        }

        /// <summary>A planet's place in the star map's system view (stations.json "mapPlace").</summary>
        public sealed class MapPlace
        {
            /// <summary>Degrees round the sun (0..360), null: the seeded slot's.</summary>
            public float? angle;
            /// <summary>Map units from the sun (the game's planets lie about 8000 to 45000 out), null: the seeded one.</summary>
            public float? distance;
        }

        static readonly Dictionary<int, OrbitSpec> orbits = new Dictionary<int, OrbitSpec>();
        static readonly Dictionary<int, MapPlace> mapPlaces = new Dictionary<int, MapPlace>();

        static readonly HashSet<string> OrbitFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "station", "jumpgate", "jumpgatePosition", "arrival", "asteroids", "fog", "sun", "planet", "pads", "pirateHeld", "skyRotation", "skybox", "light2", "ambient", "wrecks", "hazards", "containers", "lanes", "level", "ambience", "gasClouds", "locals", "raiders", "storm" };
        static readonly HashSet<string> StormFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "frequency", "radius", "heightPercent", "scale", "intensity", "thunder", "thunderChance" };
        static readonly HashSet<string> WreckFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "count", "centre", "center", "spread", "models", "materials", "hulks", "size" };
        static readonly HashSet<string> AsteroidFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "count", "centre", "center", "spread", "type", "ores" , "models", "materials" };
        static readonly string[] AsteroidTypes = { "rock", "void", "ice", "magma" };

        /// <summary>A system's own sky (systems.json "skybox"; OrbitBuilder.SetupSky, GoF2/SpaceSky's mod images).</summary>
        public sealed class Skybox
        {
            /// <summary>Backdrop "mod:" names of the images (null: the game's layer); a 2:1 image is a panorama, a 6:1 one a strip
            /// of cube faces.</summary>
            public string nebula, stars;
            /// <summary>The game's star layer 0-2 instead (-1: the system's own, -2: none).</summary>
            public int starsLayer = -1;
            public float nebulaBrightness = 1f, starsBrightness = 1f;
            /// <summary>A fixed turn (Unity degrees); null: the game's own per-station turn, like the originals. With sunAligned
            /// it turns the image in its own frame first (its +Y stays toward the sun).</summary>
            public Vector3? rotation;
            /// <summary>The sky's +Y toward each orbit's sun (OrbitBuilder.SunAlignedSky, the supernova system's rule), so a
            /// painted sky's bright side lies behind the sun whatever the orbit.</summary>
            public bool sunAligned;
        }

        static readonly Dictionary<int, Skybox> skyboxes = new Dictionary<int, Skybox>();

        static readonly HashSet<string> SkyFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "nebula", "stars", "nebulaBrightness", "starsBrightness", "rotation", "sunAligned" };

        static readonly Dictionary<int, string> systemSpaceMusic = new Dictionary<int, string>(), systemStationMusic = new Dictionary<int, string>(),
                                                stationMusic = new Dictionary<int, string>();
        static readonly Dictionary<int, List<string>> systemBattleMusic = new Dictionary<int, List<string>>();
        static readonly Dictionary<int, string> stationSubtitle = new Dictionary<int, string>(), stationLore = new Dictionary<int, string>();
        static readonly HashSet<int> modTrafficSystems = new HashSet<int>();
        static readonly Dictionary<int, List<int>> systemLocals = new Dictionary<int, List<int>>(), systemRaiders = new Dictionary<int, List<int>>();
        /// <summary>systems.json "pirateHeld": pirates hold every station of the system (Data.PirateHold).</summary>
        static readonly HashSet<int> systemPirateHeld = new HashSet<int>();

        // ---- mobile stations (MobileStations) -----------------------------------------------------------------------

        /// <summary>stations.json "mobile": true or { "route": [stations] | "any", "stayMinutes": 15, "systemMinutes": 90,
        /// "exclude": [systems], "systems": [systems], "speed": 0.2, "race": "terran", "engines": [mounts], "engineColor": [r, g, b],
        /// "engineFx": {...} }:
        /// a station with no planet that cruises from orbit to orbit (MobileStations).</summary>
        public class MobileSpec
        {
            public ModInfo mod;
            public string where;
            public int system = -1;
            public readonly List<string> routeRefs = new List<string>();
            /// <summary>The orbits it visits, in order (station numbers, any systems).</summary>
            public readonly List<int> route = new List<int>();
            /// <summary>Its race (0-3), the same wherever it is: "race", else its interior's, else its home system's.</summary>
            public int race;
            internal int raceToken = -1;
            /// <summary>"route": "any": it roams every system (roamSystems), "systemMinutes" in each before it moves on to
            /// another, its orbits there "stayMinutes" each.</summary>
            public bool roam;
            public float systemMinutes = 90f;
            public readonly List<string> excludeRefs = new List<string>();
            /// <summary>"systems": roaming ("route": "any") only these systems (none: every system).</summary>
            public readonly List<string> systemRefs = new List<string>();
            public readonly List<(int system, List<int> orbits)> roamSystems = new List<(int, List<int>)>();
            /// <summary>"engines": its exhausts, mounts like ships.json's (position_engine in game units from its centre,
            /// glowSize, glowColor), lit while it cruises; "engineColor" [r, g, b] for every one.</summary>
            public List<WeaponMount> engines;
            public float[] engineColor;
            /// <summary>"engineFx": Galaxy on Fire 3's engine effect at the engines instead of the glow discs (ships.json's
            /// engineFx, EngineFx; each engine sized by its "fxScale").</summary>
            public CustomEngineFx engineFx;
            /// <summary>Minutes in each orbit (1..240).</summary>
            public float stayMinutes = 15f;
            /// <summary>Its cruising speed, game units per ms (the freighters' 1; 0..2).</summary>
            public float speed = 0.2f;
        }

        static readonly Dictionary<int, MobileSpec> mobiles = new Dictionary<int, MobileSpec>();

    }
}
