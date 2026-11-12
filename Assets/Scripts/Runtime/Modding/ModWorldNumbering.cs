// ModWorldNumbering.cs
// ModWorld: numbering (part of the partial ModWorld; its fields are in ModWorld.cs).

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    public static partial class ModWorld
    {
        public static void NoteOriginalCounts(Database db)
        {
            // An empty table is a load that found nothing (an asset import before Resources load), never the size (as
            // ModContent.NoteOriginalCounts): kept, it numbered every mod system and station from 0 (2026-11-02, a reimport in a
            // batch test run: 112 failures).
            if (originalSystems < 0 && db.Systems.Count > 0) originalSystems = db.Systems.Count;
            if (originalStations < 0 && db.Stations.Count > 0) originalStations = db.Stations.Count;
        }
        static void EnsureMapping()
        {
            if (mappedRevision == ModManager.Revision && originalSystems >= 0) return;
            if (originalSystems < 0)
            {
                if (loadingOriginals) return;   // Load's own Apply comes back here while the tables are missing
                loadingOriginals = true;
                try { Database.Load(); }
                finally { loadingOriginals = false; }
                if (originalSystems < 0) return;
            }
            mappedRevision = ModManager.Revision;
            systemIndex.Clear(); stationIndex.Clear(); systemAt.Clear(); stationAt.Clear(); missingSystems.Clear(); missingStations.Clear(); stationLook.Clear();
            bool session = ModManager.InSession;
            int nextSystem = originalSystems, nextStation = originalStations;
            foreach (var mod in ModManager.Active)
            {
                var p = ModContent.Parse(mod);
                foreach (var d in p.systems)
                {
                    if (d.key == null) continue;
                    int i = session ? nextSystem++ : ModRegistry.Assign(ModRegistry.Kind.System, d.key, originalSystems);
                    systemIndex[d.key] = i; systemAt[i] = d;
                }
                foreach (var d in p.stations)
                {
                    if (d.key == null) continue;
                    int i = session ? nextStation++ : ModRegistry.Assign(ModRegistry.Kind.Station, d.key, originalStations);
                    stationIndex[d.key] = i; stationAt[i] = d;
                }
            }
            if (!session)
            {
                foreach (var kv in ModRegistry.All(ModRegistry.Kind.System))
                    if (!systemIndex.ContainsKey(kv.Key) && kv.Value >= originalSystems) missingSystems[kv.Value] = kv.Key;
                foreach (var kv in ModRegistry.All(ModRegistry.Kind.Station))
                    if (!stationIndex.ContainsKey(kv.Key) && kv.Value >= originalStations) missingStations[kv.Value] = kv.Key;
            }
        }
        static bool Resolve(string reference, Dictionary<string, int> keys, int original, Dictionary<int, Def> mods, out int index)
        {
            index = -1;
            if (string.IsNullOrEmpty(reference)) return false;
            if (int.TryParse(reference, out index)) return index >= 0 && (index < original || mods.ContainsKey(index));
            return keys.TryGetValue(reference, out index);
        }
        public static bool TryResolveSystem(string reference, out int index) { EnsureMapping(); return Resolve(reference, systemIndex, originalSystems, systemAt, out index); }
        public static bool TryResolveStation(string reference, out int index) { EnsureMapping(); return Resolve(reference, stationIndex, originalStations, stationAt, out index); }
        public static bool IsModSystem(int i) { EnsureMapping(); return systemAt.ContainsKey(i); }
        /// <summary>The mod that adds the system (null: an original one, or a placeholder).</summary>
        public static ModInfo SystemOwner(int i) { EnsureMapping(); return systemAt.TryGetValue(i, out var d) ? d.mod : null; }
        public static bool IsModStation(int i) { EnsureMapping(); return stationAt.ContainsKey(i); }
        /// <summary>Remake: the tag of the mod that adds the system / station (ModContent.TagOf), null = none.</summary>
        public static string SystemTag(int i) { EnsureMapping(); return systemAt.TryGetValue(i, out var d) ? ModContent.TagOf(d.mod) : null; }
        public static string StationTag(int i) { EnsureMapping(); return stationAt.TryGetValue(i, out var d) ? ModContent.TagOf(d.mod) : null; }
        /// <summary>A placeholder system (Apply): past the original table and no mod that is on adds it, whether the registry
        /// names a mod that is off or gone, or it only fills a gap in the numbering.</summary>
        public static bool IsMissingSystem(int i) { EnsureMapping(); return originalSystems >= 0 && i >= originalSystems && !systemAt.ContainsKey(i); }
        public static bool IsMissingStation(int i) { EnsureMapping(); return missingStations.ContainsKey(i); }
        public static int SystemIndexOf(string key) { EnsureMapping(); return systemIndex.TryGetValue(key, out int i) ? i : -1; }
        /// <summary>A mod's system's key ("mod_id:id"), null for an original one.</summary>
        public static string SystemKey(int i) { EnsureMapping(); return systemAt.TryGetValue(i, out var d) ? d.key : null; }
        public static int StationIndexOf(string key) { EnsureMapping(); return stationIndex.TryGetValue(key, out int i) ? i : -1; }
        public static IEnumerable<KeyValuePair<int, string>> ActiveSystemKeys() { EnsureMapping(); return systemAt.Select(kv => new KeyValuePair<int, string>(kv.Key, kv.Value.key)).ToList(); }
        public static IEnumerable<KeyValuePair<int, string>> ActiveStationKeys() { EnsureMapping(); return stationAt.Select(kv => new KeyValuePair<int, string>(kv.Key, kv.Value.key)).ToList(); }
        /// <summary>The original station whose model and collision a station uses (a mod's: its "looksLike"; others itself).</summary>
        public static int StationLook(int station) { EnsureMapping(); return stationLook.TryGetValue(station, out int l) ? l : station; }
        /// <summary>A system's own jumpgate model (systems.json "jumpgate"), null: its race's gate.</summary>
        public static StationModel GateModelOf(int system) => gateModels.TryGetValue(system, out var m) ? m : null;
        /// <summary>The key a gate's model is kept under beside the stations' (ModStations: below every station number).</summary>
        public static int GateKey(int system) => -1000 - system;
        /// <summary>A station's custom hangar / bar (stations.json "hangar" / "bar": an interiors.json key; ModInteriors), null: none.</summary>
        public static string HangarKey(int station) => stationHangar.TryGetValue(station, out var k) ? k : null;
        public static string BarKey(int station) => stationBar.TryGetValue(station, out var k) && k != NoBarKey ? k : null;
        /// <summary>stations.json "bar": "none": the station has no Space Lounge (its button hidden, Story.LoungeUnlocked false).</summary>
        public static bool NoBar(int station) => stationBar.TryGetValue(station, out var k) && k == NoBarKey;
        /// <summary>The station's own model (null: an original's, "looksLike").</summary>
        public static StationModel ModelOf(int station) => stationModels.TryGetValue(station, out var m) ? m : null;
        /// <summary>Every station with its own model (ModStations loads them).</summary>
        public static IEnumerable<StationModel> Models() { Database.Load(); return stationModels.Values.Concat(gateModels.Values).ToList(); }
        /// <summary>The race whose hangar and bar a station uses (stations.json "interior"; -1: the system's).</summary>
        public static int InteriorRace(int station) => stationInterior.TryGetValue(station, out int r) ? r : -1;
        /// <summary>A station's own planet texture (Backdrop's "mod:" name, ModBackdrop), null: the game's.</summary>
        public static string PlanetTexture(int station) => planetTextures.TryGetValue(station, out var t) ? t : null;
        /// <summary>The station's planet as a lit sphere (stations.json "planetSphere"), null: its planetTexture's disc.</summary>
        public static PlanetSphereSpec PlanetSphereOf(int station) => planetSpheres.TryGetValue(station, out var p) ? p : null;
        /// <summary>A station's star map planet image (stations.json "mapTexture", wrapped round the map's sphere; Backdrop's
        /// "mod:" name), null: none (StarMap then shows its planetTexture as a disc, else the game's planet).</summary>
        public static string MapTexture(int station) => mapTextures.TryGetValue(station, out var t) ? t : null;
        /// <summary>The station's planet has a ring on the star map (stations.json "mapRing").</summary>
        public static bool MapRing(int station) => mapRings.Contains(station);
        /// <summary>The station's orbit layout from its mod, null: the game's.</summary>
        public static OrbitSpec OrbitOf(int station) => orbits.TryGetValue(station, out var o) ? o : null;
        /// <summary>A station's docking pads outside (stations.json "orbit.pads"), empty = none.</summary>
        public static List<SpacePoints.Point> StationPads(int station) => OrbitOf(station)?.pads ?? new List<SpacePoints.Point>();
        /// <summary>Pirates hold 'station' of 'system' (stations.json "orbit.pirateHeld", else systems.json "pirateHeld").</summary>
        public static bool PirateHeld(int station, int system) => OrbitOf(station)?.pirateHeld ?? systemPirateHeld.Contains(system);
        /// <summary>The station's planet place on the star map, null: the game's.</summary>
        public static MapPlace MapPlaceOf(int station) => mapPlaces.TryGetValue(station, out var p) ? p : null;
        static Vector3? Xyz(JObject o, string key, string where)
        {
            var t = ModJson.Get(o, key);
            if (t == null) return null;
            if (!(t is JArray a) || a.Count != 3 || a.Any(v => v.Type != JTokenType.Integer && v.Type != JTokenType.Float))
                throw new ModJsonException($"{where}: \"{key}\" must be [x, y, z] (game units)");
            var p = new Vector3((float)a[0], (float)a[1], (float)a[2]);
            if (Mathf.Abs(p.x) > 1e6f || Mathf.Abs(p.y) > 1e6f || Mathf.Abs(p.z) > 1e6f) throw new ModJsonException($"{where}: \"{key}\" lies too far out (at most 1000000 units)");
            return p;
        }
        /// <summary>stations.json "orbit" (see OrbitSpec). 'mod' reports the ores it can't resolve when 'resolve' is set
        /// (Apply; Parse only checks the shape).</summary>
        static OrbitSpec ParseOrbit(ModInfo mod, JToken t, string where, Func<string, int> resolve)
        {
            if (!(t is JObject o)) throw new ModJsonException($"{where}: \"orbit\" must be an object {{ \"station\": ..., \"asteroids\": ... }}");
            foreach (var p in o.Properties())
                if (!OrbitFields.Contains(p.Name)) Report(mod, $"{where}: unknown orbit field \"{p.Name}\" (ignored)");
            var s = new OrbitSpec { owner = mod };
            if (ModJson.Get(o, "station") is JToken st)
            {
                if (st.Type == JTokenType.Boolean) s.station = (bool)st ? 1 : 0;
                else if (st.Type == JTokenType.String && ((string)st).Equals("nodock", StringComparison.OrdinalIgnoreCase)) s.station = 2;
                else throw new ModJsonException($"{where}: orbit \"station\" is true, false (an empty orbit) or \"nodock\" (its model, no docking)");
            }
            if (ModJson.Get(o, "jumpgate") is JToken jg)
            {
                if (jg.Type != JTokenType.Boolean) throw new ModJsonException($"{where}: orbit \"jumpgate\" is true or false");
                s.jumpgate = (bool)jg;
            }
            s.jumpgatePosition = Xyz(o, "jumpgatePosition", where);
            s.arrival = Xyz(o, "arrival", where);
            if (ModJson.Get(o, "pirateHeld") is JToken held)
            {
                if (held.Type != JTokenType.Boolean) throw new ModJsonException($"{where}: orbit \"pirateHeld\" is true or false");
                s.pirateHeld = (bool)held;
            }
            if (ModJson.Get(o, "pads") is JToken padsToken)
            {
                // Like npcships.json's "docking.pads": the approach 2000 units above the pad unless given, facing game +z.
                if (!(padsToken is JArray padList)) throw new ModJsonException($"{where}: orbit \"pads\" must be a list [ {{ \"position\": [x, y, z] }} ]");
                s.pads = new List<SpacePoints.Point>();
                foreach (var pt in padList)
                {
                    if (!(pt is JObject po) || !(Xyz(po, "position", where) is Vector3 at)) throw new ModJsonException($"{where}: orbit \"pads\": a pad needs \"position\"");
                    var facing = Xyz(po, "facing", where) is Vector3 f && f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
                    var approach = Xyz(po, "approach", where) ?? at + new Vector3(0f, 2000f, 0f);
                    s.pads.Add(new SpacePoints.Point { type = SpacePoints.Approach, engine = approach, dir = facing });
                    s.pads.Add(new SpacePoints.Point { type = SpacePoints.Dock, engine = at, dir = facing });
                }
                if (s.pads.Count == 0) s.pads = null;
            }
            // Remake (2026-11-03): hazard volumes, EVERSPACE 2's heat volumes and radiation fields (World.HazardZones)
            if (ModJson.Get(o, "hazards") is JToken hz)
            {
                if (!(hz is JArray hzList)) throw new ModJsonException($"{where}: orbit \"hazards\" must be a list [ {{ \"kind\": \"heat\" | \"radiation\" | \"frost\" | \"corrosion\" | \"damage\", \"centre\", \"radius\" }} ]");
                s.hazards = new List<HazardSpec>();
                foreach (var e in hzList)
                {
                    if (!(e is JObject h)) throw new ModJsonException($"{where}: a hazard is {{ \"kind\", \"centre\", \"radius\" }}");
                    foreach (var p in h.Properties())
                        if (p.Name != "kind" && p.Name != "centre" && p.Name != "center" && p.Name != "radius" && p.Name != "dps" && p.Name != "raiderDps") Report(mod, $"{where}: unknown hazard field \"{p.Name}\" (ignored)");
                    var kindName = ModJson.Str(h, "kind") ?? "";
                    if (!Enum.TryParse(kindName, true, out HazardKind kind) || int.TryParse(kindName, out _))
                        throw new ModJsonException($"{where}: a hazard's \"kind\" is \"heat\", \"radiation\", \"frost\", \"corrosion\" or \"damage\"");
                    var centre = Xyz(h, "centre", where) ?? Xyz(h, "center", where);
                    if (!centre.HasValue) throw new ModJsonException($"{where}: a hazard needs \"centre\": [x, y, z]");
                    s.hazards.Add(new HazardSpec
                    {
                        kind = kind, centre = centre.Value,
                        radius = Mathf.Clamp(ModJson.Float(h, "radius", 20000f, StationsFile), 1000f, 400000f),
                        dps = Mathf.Max(0f, ModJson.Float(h, "dps", 10f, StationsFile)),
                        raiderDps = Mathf.Clamp(ModJson.Float(h, "raiderDps", kind == HazardKind.Corrosion ? Flight.RiftRun.CorrosionTickSeconds : 0f, StationsFile), 0f, 50f),
                    });
                }
                if (s.hazards.Count == 0) s.hazards = null;
            }
            // Remake (2026-11-05): who flies here, EVERSPACE 2's location_spawns (TrafficPlan, through Locals / Raiders)
            if (ModJson.Has(o, "locals")) s.locals = ModJson.Strings(o, "locals");
            if (ModJson.Has(o, "raiders")) s.raiders = ModJson.Strings(o, "raiders");
            // Remake (2026-11-05): gas clouds at their places, EVERSPACE 2's plasma fields (World.GasCloudField)
            if (ModJson.Get(o, "gasClouds") is JToken gc)
            {
                if (!(gc is JObject g)) throw new ModJsonException($"{where}: orbit \"gasClouds\" is {{ \"plasma\", \"clouds\": [ [x, y, z], ... ] }}");
                foreach (var p in g.Properties())
                    if (p.Name != "plasma" && p.Name != "clouds") Report(mod, $"{where}: unknown gasClouds field \"{p.Name}\" (ignored)");
                var spec = new GasCloudSpec { plasma = ModJson.Get(g, "plasma") is JToken plasmaRef ? (plasmaRef.Type == JTokenType.Integer ? ((int)plasmaRef).ToString() : (string)plasmaRef) : null, clouds = new List<Vector3>() };
                if (ModJson.Get(g, "clouds") is JArray cl)
                    foreach (var c in cl)
                        if (c is JArray v && v.Count == 3) spec.clouds.Add(new Vector3((float)v[0], (float)v[1], (float)v[2]));
                        else Report(mod, $"{where}: a gas cloud is [x, y, z] (left out)");
                if (spec.clouds.Count > 0) s.gasClouds = spec;
            }
            // Remake (2026-11-06): static props, EVERSPACE 2's placed unique asteroids (World.OrbitProps)
            if (ModJson.Get(o, "props") is JToken props)
            {
                if (!(props is JArray propList)) throw new ModJsonException($"{where}: orbit \"props\" is a list [ {{ \"model\", \"at\", \"rotation\", \"size\" }} ]");
                s.props = new List<PropSpec>();
                foreach (var e in propList)
                {
                    if (!(e is JObject po)) throw new ModJsonException($"{where}: a prop is {{ \"model\", \"at\", \"rotation\", \"size\" }}");
                    foreach (var p in po.Properties())
                        if (p.Name != "model" && p.Name != "at" && p.Name != "rotation" && p.Name != "size") Report(mod, $"{where}: unknown prop field \"{p.Name}\" (ignored)");
                    var model = ModJson.Str(po, "model");
                    var at = Xyz(po, "at", where);
                    if (model == null || !at.HasValue) throw new ModJsonException($"{where}: a prop needs \"model\" and \"at\": [x, y, z]");
                    if (!mod.Source.Exists(model)) { Report(mod, $"{where}: prop model \"{model}\" isn't in the mod (left out)"); continue; }
                    if (s.props.Count >= 60) { Report(mod, $"{where}: at most 60 props an orbit (the rest left out)"); break; }
                    s.props.Add(new PropSpec { model = model, at = at.Value, rotation = Xyz(po, "rotation", where) ?? Vector3.zero,
                                               size = Mathf.Clamp(ModJson.Float(po, "size", 2000f, StationsFile), 10f, 400000f) });
                }
                if (s.props.Count == 0) s.props = null;
            }
            if (ModJson.Get(o, "propMaterials") is JArray pmats)
                foreach (var m in pmats) s.propMaterials.Add(JsonUtility.FromJson<CustomShipMaterial>(m.ToString()));
            // Remake (2026-11-06): acid pouch bases, EVERSPACE 2's BP_AcidSack_Base (World.AcidSackBase)
            if (ModJson.Get(o, "acidSacks") is JToken acid)
            {
                if (!(acid is JArray acidList)) throw new ModJsonException($"{where}: orbit \"acidSacks\" is a list of places [ [x, y, z], ... ]");
                s.acidSacks = new List<Vector3>();
                foreach (var e in acidList)
                    if (e is JArray xyz && xyz.Count == 3 && xyz.All(v => v.Type == JTokenType.Integer || v.Type == JTokenType.Float))
                    { if (s.acidSacks.Count < 30) s.acidSacks.Add(new Vector3((float)xyz[0], (float)xyz[1], (float)xyz[2])); }
                    else Report(mod, $"{where}: an acid pouch's place is [x, y, z] (left out)");
                if (s.acidSacks.Count == 0) s.acidSacks = null;
            }
            // Remake (2026-11-06): minefields, EVERSPACE 2's BP_Minefield (World.OrbitMinefields)
            if (ModJson.Get(o, "minefields") is JToken mf)
            {
                if (!(mf is JArray mfList)) throw new ModJsonException($"{where}: orbit \"minefields\" must be a list [ {{ \"kind\", \"centre\", \"radius\", \"count\" }} ]");
                s.minefields = new List<MinefieldSpec>();
                foreach (var e in mfList)
                {
                    if (!(e is JObject m)) throw new ModJsonException($"{where}: a minefield is {{ \"kind\", \"centre\", \"radius\", \"count\" }}");
                    foreach (var p in m.Properties())
                        if (!MinefieldFields.Contains(p.Name)) Report(mod, $"{where}: unknown minefield field \"{p.Name}\" (ignored)");
                    var kindName = ModJson.Str(m, "kind") ?? "kinetic";
                    if (!Enum.TryParse(kindName, true, out MinefieldSpec.Kind kind) || int.TryParse(kindName, out _))
                        throw new ModJsonException($"{where}: a minefield's \"kind\" is \"kinetic\", \"web\", \"cryo\", \"thermal\" or \"corrosion\"");
                    var centre = Xyz(m, "centre", where) ?? Xyz(m, "center", where);
                    if (!centre.HasValue) throw new ModJsonException($"{where}: a minefield needs \"centre\": [x, y, z]");
                    s.minefields.Add(new MinefieldSpec
                    {
                        kind = kind, centre = centre.Value,
                        radius = Mathf.Clamp(ModJson.Float(m, "radius", 18000f, StationsFile), 500f, 100000f),
                        count = Mathf.Clamp(ModJson.Int(m, "count", 20, StationsFile), 1, 40),
                        raiderSeconds = ModJson.Float(m, "raiderSeconds", -1f, StationsFile) is float rs && rs >= 0f ? Mathf.Min(rs, 20f) : -1f,
                        minRank = Mathf.Clamp(ModJson.Int(m, "minRank", 4, StationsFile), 0, 20),
                        share = Mathf.Clamp01(ModJson.Float(m, "share", 0.5f, StationsFile)),
                        maxSecurity = Mathf.Clamp(ModJson.Int(m, "maxSecurity", 1, StationsFile), 0, 3),
                    });
                }
                if (s.minefields.Count == 0) s.minefields = null;
            }
            // Remake (2026-11-06): a lightning storm, EVERSPACE 2's BP_LightningField (World.OrbitStorm)
            if (ModJson.Get(o, "storm") is JToken stm)
            {
                if (stm.Type == JTokenType.Boolean) s.storm = (bool)stm ? new StormSpec() : null;
                else if (stm is JObject so)
                {
                    foreach (var p in so.Properties())
                        if (!StormFields.Contains(p.Name)) Report(mod, $"{where}: unknown storm field \"{p.Name}\" (ignored)");
                    s.storm = new StormSpec
                    {
                        frequency = Mathf.Clamp(ModJson.Float(so, "frequency", 1f, StationsFile), 0.05f, 10f),
                        radius = Mathf.Clamp(ModJson.Float(so, "radius", 80000f, StationsFile), 5000f, 400000f),
                        heightPercent = Mathf.Clamp(ModJson.Float(so, "heightPercent", 1f, StationsFile), 0f, 4f),
                        scale = Mathf.Clamp(ModJson.Float(so, "scale", 1f, StationsFile), 0.1f, 10f),
                        intensity = Mathf.Clamp(ModJson.Float(so, "intensity", 1f, StationsFile), 0f, 10f),
                        thunderChance = Mathf.Clamp01(ModJson.Float(so, "thunderChance", 0.2f, StationsFile)),
                        thunder = ModJson.Has(so, "thunder") ? ModJson.Strings(so, "thunder") : null,
                    };
                }
                else throw new ModJsonException($"{where}: orbit \"storm\" is true, false or {{ \"frequency\", \"radius\", \"scale\", \"intensity\", \"thunder\", \"thunderChance\" }}");
            }
            // Remake (2026-11-05): ambience zones, EVERSPACE 2's (World.OrbitAmbience)
            if (ModJson.Get(o, "ambience") is JToken amb)
            {
                if (!(amb is JArray ambList)) throw new ModJsonException($"{where}: orbit \"ambience\" must be a list [ {{ \"loops\", \"adds\", \"centre\", \"radius\" }} ]");
                s.ambience = new List<AmbienceSpec>();
                foreach (var e in ambList)
                {
                    if (!(e is JObject a)) throw new ModJsonException($"{where}: an ambience zone is {{ \"loops\", \"adds\", ... }}");
                    foreach (var p in a.Properties())
                        if (Array.IndexOf(AmbienceFields, p.Name) < 0) Report(mod, $"{where}: unknown ambience field \"{p.Name}\" (ignored)");
                    var loops = ModJson.Strings(a, "loops");
                    if (loops.Count == 0 && ModJson.Str(a, "loop") is string one) loops.Add(one);
                    if (loops.Count == 0) { Report(mod, $"{where}: an ambience zone without \"loops\" (left out)"); continue; }
                    var z = new AmbienceSpec { loops = loops, adds = ModJson.Strings(a, "adds") };
                    z.volume = Mathf.Clamp(ModJson.Float(a, "volume", 1f, StationsFile), 0f, 2f);
                    z.addVolume = Mathf.Clamp(ModJson.Float(a, "addVolume", 0.3f, StationsFile), 0f, 2f);
                    var every = ModJson.Get(a, "addEveryMs") as JArray;
                    if (every != null && every.Count == 2) { z.addMinMs = Mathf.Max(500f, (float)every[0]); z.addMaxMs = Mathf.Max(z.addMinMs, (float)every[1]); }
                    z.centre = Xyz(a, "centre", where) ?? Xyz(a, "center", where);
                    z.radius = Mathf.Clamp(ModJson.Float(a, "radius", z.radius, StationsFile), 1000f, 400000f);
                    s.ambience.Add(z);
                }
                if (s.ambience.Count == 0) s.ambience = null;
            }
            // Remake (2026-11-03): a wreck field, EVERSPACE 2's (World.WreckField)
            if (ModJson.Get(o, "wrecks") is JToken wr)
            {
                if (wr.Type == JTokenType.Boolean) s.wreckCount = (bool)wr ? 12 : 0;
                else if (wr is JObject w)
                {
                    foreach (var p in w.Properties())
                        if (!WreckFields.Contains(p.Name)) Report(mod, $"{where}: unknown wrecks field \"{p.Name}\" (ignored)");
                    s.wreckCount = Mathf.Clamp(ModJson.Int(w, "count", 12, StationsFile), 0, 60);
                    s.wreckCentre = Xyz(w, "centre", where) ?? Xyz(w, "center", where);
                    if (ModJson.Has(w, "spread")) s.wreckSpread = Mathf.Clamp(ModJson.Float(w, "spread", 60000f, StationsFile), 5000f, 400000f);
                    // Remake (2026-11-05): the field's own wreck hulls (World.WreckField)
                    foreach (var path in ModJson.Strings(w, "models"))
                    {
                        if (!mod.Source.Exists(path)) { Report(mod, $"{where}: wrecks model \"{path}\" isn't in the mod (left out)"); continue; }
                        (s.wreckModels ??= new List<string>()).Add(path);
                    }
                    if (ModJson.Get(w, "materials") is JArray wmats)
                        foreach (var m in wmats) s.wreckMaterials.Add(JsonUtility.FromJson<CustomShipMaterial>(m.ToString()));
                    if (ModJson.Has(w, "hulks")) s.wreckHulks = Mathf.Clamp(ModJson.Int(w, "hulks", 6, StationsFile), 0, 30);
                    if (ModJson.Has(w, "size")) s.wreckSize = Mathf.Clamp(ModJson.Float(w, "size", 8000f, StationsFile), 500f, 60000f);
                }
                else throw new ModJsonException($"{where}: orbit \"wrecks\" is true, false or {{ \"count\", \"centre\", \"spread\", \"models\"... }}");
            }
            if (ModJson.Has(o, "level")) s.level = Mathf.Clamp(ModJson.Int(o, "level", 0, StationsFile), 0, 30);   // remake: ES2's location level
            // Remake (2026-11-03): traffic lanes, EVERSPACE 2's spline traffic (World.TrafficLanes)
            if (ModJson.Get(o, "lanes") is JToken ln)
            {
                if (!(ln is JArray lnList)) throw new ModJsonException($"{where}: orbit \"lanes\" must be a list [ {{ \"points\": [[x, y, z], ...] }} ]");
                s.lanes = new List<LaneSpec>();
                foreach (var e in lnList)
                {
                    if (!(e is JObject l)) throw new ModJsonException($"{where}: a lane is {{ \"points\", \"ships\", \"count\", \"speed\", \"loop\", \"twoLanes\" }}");
                    foreach (var p in l.Properties())
                        if (p.Name != "points" && p.Name != "ships" && p.Name != "count" && p.Name != "speed" && p.Name != "loop" && p.Name != "twoLanes")
                            Report(mod, $"{where}: unknown lane field \"{p.Name}\" (ignored)");
                    if (!(ModJson.Get(l, "points") is JArray pts) || pts.Count < 2)
                        throw new ModJsonException($"{where}: a lane needs \"points\": at least two [x, y, z]");
                    var lane = new LaneSpec { points = new List<Vector3>() };
                    foreach (var pt in pts)
                    {
                        if (!(pt is JArray a) || a.Count != 3 || a.Any(v => v.Type != JTokenType.Integer && v.Type != JTokenType.Float))
                            throw new ModJsonException($"{where}: a lane's points are [x, y, z] (game units)");
                        var v3 = new Vector3((float)a[0], (float)a[1], (float)a[2]);
                        if (Mathf.Abs(v3.x) > 1e6f || Mathf.Abs(v3.y) > 1e6f || Mathf.Abs(v3.z) > 1e6f) throw new ModJsonException($"{where}: a lane's point lies too far out (at most 1000000 units)");
                        lane.points.Add(v3);
                    }
                    if (ModJson.Get(l, "ships") is JArray sh)
                    {
                        lane.ships = new List<string>();
                        foreach (var r in sh) if (r.Type == JTokenType.String || r.Type == JTokenType.Integer) lane.ships.Add(r.ToString());
                        if (lane.ships.Count == 0) lane.ships = null;
                    }
                    lane.count = Mathf.Clamp(ModJson.Int(l, "count", Flight.TrafficLane.DefaultCount, StationsFile), 1, 40);
                    lane.speed = Mathf.Clamp(ModJson.Float(l, "speed", Flight.TrafficLane.DefaultSpeedMps, StationsFile), 5f, 1000f);
                    lane.loop = ModJson.Bool(l, "loop", true, StationsFile);
                    lane.twoLanes = ModJson.Bool(l, "twoLanes", true, StationsFile);
                    s.lanes.Add(lane);
                }
                if (s.lanes.Count == 0) s.lanes = null;
                else if (s.lanes.Count > 12) throw new ModJsonException($"{where}: at most 12 lanes an orbit");
            }
            // Remake (2026-11-03): closed containers, EVERSPACE 2's (World.LootContainer)
            if (ModJson.Get(o, "containers") is JToken ct)
            {
                if (ct.Type == JTokenType.Boolean) s.containerCount = (bool)ct ? 6 : 0;
                else if (ct is JObject c)
                {
                    foreach (var p in c.Properties())
                        if (p.Name != "count" && p.Name != "centre" && p.Name != "center" && p.Name != "spread" && p.Name != "bulletproof" && p.Name != "loot"
                            && p.Name != "model" && p.Name != "bulletproofModel" && p.Name != "materials" && p.Name != "cloaked" && p.Name != "secure" && p.Name != "sounds")
                            Report(mod, $"{where}: unknown containers field \"{p.Name}\" (ignored)");
                    s.containerCount = Mathf.Clamp(ModJson.Int(c, "count", 6, StationsFile), 0, 40);
                    s.containerCentre = Xyz(c, "centre", where) ?? Xyz(c, "center", where);
                    if (ModJson.Has(c, "spread")) s.containerSpread = Mathf.Clamp(ModJson.Float(c, "spread", 40000f, StationsFile), 5000f, 400000f);
                    if (ModJson.Has(c, "bulletproof")) s.containerBulletproof = Mathf.Clamp01(ModJson.Float(c, "bulletproof", 0.3f, StationsFile));
                    foreach (var (field, set) in new (string, Action<string>)[] { ("model", v => s.containerModel = v), ("bulletproofModel", v => s.containerProofModel = v) })
                    {
                        if (!(ModJson.Str(c, field) is string path)) continue;
                        if (mod.Source.Exists(path)) set(path);
                        else Report(mod, $"{where}: containers \"{field}\" \"{path}\" isn't in the mod (the race's container)");
                    }
                    if (ModJson.Get(c, "cloaked") is JToken cl)
                    {
                        if (!(cl is JArray clList)) throw new ModJsonException($"{where}: containers \"cloaked\" is a list of places [ [x, y, z], ... ]");
                        s.containerCloaked = new List<Vector3>();
                        foreach (var e in clList)
                        {
                            if (e is JArray xyz && xyz.Count == 3 && xyz.All(v => v.Type == JTokenType.Integer || v.Type == JTokenType.Float))
                            {
                                if (s.containerCloaked.Count < 10) s.containerCloaked.Add(new Vector3((float)xyz[0], (float)xyz[1], (float)xyz[2]));
                                else { Report(mod, $"{where}: at most 10 cloaked containers (the rest left out)"); break; }
                            }
                            else Report(mod, $"{where}: a cloaked container's place is [x, y, z] (left out)");
                        }
                        if (s.containerCloaked.Count == 0) s.containerCloaked = null;
                    }
                    if (ModJson.Get(c, "sounds") is JObject snd)
                    {
                        s.containerSounds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var p in snd.Properties())
                        {
                            if (Array.IndexOf(PuzzleSoundRoles, p.Name) < 0) { Report(mod, $"{where}: unknown containers sound \"{p.Name}\" (ignored; {string.Join(", ", PuzzleSoundRoles)})"); continue; }
                            if (p.Value.Type == JTokenType.String) s.containerSounds[p.Name] = (string)p.Value;
                        }
                        if (s.containerSounds.Count == 0) s.containerSounds = null;
                    }
                    if (ModJson.Get(c, "secure") is JToken sec)
                    {
                        if (!(sec is JArray secList)) throw new ModJsonException($"{where}: containers \"secure\" is a list [ {{ \"at\", \"triggers\" }} ]");
                        s.containerSecure = new List<SecureContainerSpec>();
                        foreach (var e in secList)
                        {
                            if (!(e is JObject so)) throw new ModJsonException($"{where}: a secure container is {{ \"at\": [x, y, z], \"triggers\": [ [x, y, z], ... ] }}");
                            foreach (var p in so.Properties())
                                if (p.Name != "at" && p.Name != "triggers" && p.Name != "ordered" && p.Name != "retrigger" && p.Name != "sockets" && p.Name != "conductors")
                                    Report(mod, $"{where}: unknown secure container field \"{p.Name}\" (ignored)");
                            var at = Xyz(so, "at", where);
                            if (!at.HasValue) throw new ModJsonException($"{where}: a secure container needs \"at\": [x, y, z]");
                            var spec = new SecureContainerSpec { at = at.Value, ordered = ModJson.Bool(so, "ordered", false, StationsFile),
                                retriggerS = Mathf.Clamp(ModJson.Float(so, "retrigger", 0f, StationsFile), 0f, 60f) };
                            if (ModJson.Get(so, "triggers") is JArray tl)
                                foreach (var tp in tl)
                                    if (tp is JArray xyz && xyz.Count == 3 && xyz.All(v => v.Type == JTokenType.Integer || v.Type == JTokenType.Float))
                                    { if (spec.triggers.Count < 8) spec.triggers.Add(new Vector3((float)xyz[0], (float)xyz[1], (float)xyz[2])); }
                                    else Report(mod, $"{where}: a shot trigger's place is [x, y, z] (left out)");
                            if (ModJson.Get(so, "sockets") is JArray sl)
                                foreach (var sk in sl)
                                {
                                    var sko = sk as JObject;
                                    var sat = sko != null ? Xyz(sko, "at", where) : null;
                                    var orb = sko != null ? Xyz(sko, "orb", where) : null;
                                    var coil = sko != null ? Xyz(sko, "coil", where) : null;
                                    if (!sat.HasValue || orb.HasValue == coil.HasValue)
                                    { Report(mod, $"{where}: a socket is {{ \"at\": [x, y, z], \"orb\" or \"coil\": [x, y, z] }} (left out)"); continue; }
                                    if (spec.sockets.Count < 4) spec.sockets.Add(new SocketSpec { at = sat.Value, source = (orb ?? coil).Value, coil = coil.HasValue });
                                }
                            if (ModJson.Get(so, "conductors") is JObject co)
                            {
                                var cs = new ConductorSpec();
                                foreach (var p in co.Properties())
                                    if (p.Name != "sources" && p.Name != "statics" && p.Name != "portables" && p.Name != "receivers" && p.Name != "radius" && p.Name != "receiverRadius")
                                        Report(mod, $"{where}: unknown conductors field \"{p.Name}\" (ignored)");
                                foreach (var (field, list) in new[] { ("sources", cs.sources), ("statics", cs.statics), ("portables", cs.portables), ("receivers", cs.receivers) })
                                    if (ModJson.Get(co, field) is JArray arr)
                                        foreach (var ce in arr)
                                            if (ce is JArray xyz && (xyz.Count == 3 || xyz.Count == 4) && xyz.All(v => v.Type == JTokenType.Integer || v.Type == JTokenType.Float))
                                            { if (list.Count < 12) list.Add(new Vector4((float)xyz[0], (float)xyz[1], (float)xyz[2], xyz.Count == 4 ? Mathf.Clamp((float)xyz[3], 1f, 40000f) : 0f)); }
                                            else Report(mod, $"{where}: a conductor's place is [x, y, z] or [x, y, z, radius] (left out)");
                                cs.radius = Mathf.Clamp(ModJson.Float(co, "radius", 4000f, StationsFile), 100f, 40000f);
                                cs.receiverRadius = Mathf.Clamp(ModJson.Float(co, "receiverRadius", 40f, StationsFile), 1f, 40000f);
                                if (cs.sources.Count > 0 && cs.receivers.Count > 0) spec.conductors = cs;
                                else Report(mod, $"{where}: conductors need \"sources\" and \"receivers\" (left out)");
                            }
                            if (spec.triggers.Count == 0 && spec.sockets.Count == 0 && spec.conductors == null) { Report(mod, $"{where}: a secure container without shot triggers, sockets or conductors (left out)"); continue; }
                            if (s.containerSecure.Count < 6) s.containerSecure.Add(spec);
                            else { Report(mod, $"{where}: at most 6 secure containers (the rest left out)"); break; }
                        }
                        if (s.containerSecure.Count == 0) s.containerSecure = null;
                    }
                    if (ModJson.Get(c, "materials") is JArray cmats)
                        foreach (var m in cmats) s.containerMaterials.Add(JsonUtility.FromJson<CustomShipMaterial>(m.ToString()));
                    if (ModJson.Get(c, "loot") is JArray fixedLoot)
                    {
                        s.containerLoot = new List<(string, int)>();
                        foreach (var e in fixedLoot)
                        {
                            string item = e is JObject eo ? (ModJson.Get(eo, "item") is JToken it ? (it.Type == JTokenType.Integer ? ((int)it).ToString() : (string)it) : null)
                                        : e.Type == JTokenType.Integer ? ((int)e).ToString() : e.Type == JTokenType.String ? (string)e : null;
                            if (item == null) { Report(mod, $"{where}: a container loot entry is {{ \"item\", \"amount\" }} (left out)"); continue; }
                            s.containerLoot.Add((item, e is JObject ao ? Mathf.Max(1, ModJson.Int(ao, "amount", 1, StationsFile)) : 1));
                        }
                        if (s.containerLoot.Count == 0) s.containerLoot = null;
                    }
                }
                else throw new ModJsonException($"{where}: orbit \"containers\" is true, false or {{ \"count\", \"centre\", \"spread\", \"bulletproof\", \"loot\" }}");
            }
            var ast = ModJson.Get(o, "asteroids");
            if (ast != null && ast.Type == JTokenType.Boolean) s.asteroids = (bool)ast;
            else if (ast is JObject a)
            {
                foreach (var p in a.Properties())
                    if (!AsteroidFields.Contains(p.Name)) Report(mod, $"{where}: unknown asteroids field \"{p.Name}\" (ignored)");
                s.asteroids = true;
                if (ModJson.Has(a, "count")) s.asteroidCount = Mathf.Clamp(ModJson.Int(a, "count", 60, StationsFile), 0, 300);
                s.asteroidCentre = Xyz(a, "centre", where) ?? Xyz(a, "center", where);
                if (ModJson.Has(a, "spread")) s.asteroidSpread = Mathf.Clamp(ModJson.Float(a, "spread", 100000f, StationsFile), 5000f, 500000f);
                foreach (var path in ModJson.Strings(a, "models"))
                {
                    if (!mod.Source.Exists(path)) { Report(mod, $"{where}: asteroids model \"{path}\" isn't in the mod (left out)"); continue; }
                    (s.asteroidModels ??= new List<string>()).Add(path);
                }
                if (ModJson.Get(a, "materials") is JArray amats)
                    foreach (var m in amats) s.asteroidMaterials.Add(JsonUtility.FromJson<CustomShipMaterial>(m.ToString()));
                if (ModJson.Str(a, "type") is string type)
                {
                    s.asteroidType = Array.FindIndex(AsteroidTypes, x => x.Equals(type, StringComparison.OrdinalIgnoreCase));
                    if (s.asteroidType < 0) throw new ModJsonException($"{where}: asteroids \"type\" is rock, void, ice or magma");
                }
                if (ModJson.Get(a, "ores") is JToken ores)
                {
                    if (!(ores is JArray list) || list.Count == 0) throw new ModJsonException($"{where}: asteroids \"ores\" is a list of items (numbers or \"mod_id:id\"), or {{ \"item\", \"chance\" }}");
                    s.ores = new List<(int, int)>();
                    foreach (var e in list)
                    {
                        string item = e is JObject eo ? ModJson.Get(eo, "item")?.ToString() : e.ToString();
                        int chance = e is JObject eo2 ? Mathf.Max(1, ModJson.Int(eo2, "chance", 1, StationsFile)) : 1;
                        if (string.IsNullOrEmpty(item)) throw new ModJsonException($"{where}: an ore without \"item\"");
                        if (resolve == null) continue;
                        int n = resolve(item);
                        if (n < 0) { Report(mod, $"{where}: ore \"{item}\" is no item (left out)"); continue; }
                        s.ores.Add((n, chance));
                    }
                    if (resolve != null && s.ores.Count == 0) s.ores = null;
                }
            }
            else if (ast != null) throw new ModJsonException($"{where}: orbit \"asteroids\" is false or {{ \"count\", \"centre\", \"spread\", \"type\", \"ores\" }}");
            var fog = ModJson.Get(o, "fog");
            if (fog != null && fog.Type == JTokenType.Boolean) s.fog = (bool)fog;
            else if (fog is JObject f)
            {
                s.fog = true;
                if (ModJson.Get(f, "color") is JToken c)
                {
                    if (!(c is JArray ca) || ca.Count != 3) throw new ModJsonException($"{where}: fog \"color\" must be [r, g, b] (0..1)");
                    s.fogColor = new Color(Mathf.Clamp01((float)ca[0]), Mathf.Clamp01((float)ca[1]), Mathf.Clamp01((float)ca[2]));
                }
                if (ModJson.Has(f, "end")) s.fogEnd = Mathf.Clamp(ModJson.Float(f, "end", 100000f, StationsFile), 5000f, 1000000f);
            }
            else if (fog != null) throw new ModJsonException($"{where}: orbit \"fog\" is false or {{ \"color\": [r, g, b], \"end\": units }}");
            if (ModJson.Get(o, "sun") is JToken sun)
            {
                if (!(sun is JArray sa) || sa.Count != 2) throw new ModJsonException($"{where}: orbit \"sun\" must be [yaw, pitch] (degrees)");
                s.sun = new Vector2((float)sa[0], Mathf.Clamp((float)sa[1], -89f, 89f));
            }
            if (ModJson.Get(o, "light2") is JToken l2)
            {
                if (l2.Type == JTokenType.Boolean && !(bool)l2) s.light2Color = Color.black;
                else if (l2 is JObject lo)
                {
                    if (ModJson.Get(lo, "color") is JToken lc)
                    {
                        if (!(lc is JArray la) || la.Count != 3) throw new ModJsonException($"{where}: orbit light2 \"color\" must be [r, g, b] (0..2)");
                        s.light2Color = new Color(Mathf.Clamp((float)la[0], 0f, 2f), Mathf.Clamp((float)la[1], 0f, 2f), Mathf.Clamp((float)la[2], 0f, 2f));
                    }
                    if (ModJson.Has(lo, "yaw") || ModJson.Has(lo, "pitch"))
                        s.light2Direction = new Vector2(ModJson.Float(lo, "yaw", 0f, StationsFile), Mathf.Clamp(ModJson.Float(lo, "pitch", 0f, StationsFile), -89f, 89f));
                }
                else throw new ModJsonException($"{where}: orbit \"light2\" is false or {{ \"color\": [r, g, b], \"yaw\", \"pitch\" }}");
            }
            if (ModJson.Get(o, "ambient") is JToken am)
            {
                if (!(am is JArray aa) || aa.Count != 3) throw new ModJsonException($"{where}: orbit \"ambient\" must be [r, g, b] (0..1)");
                s.ambient = new Color(Mathf.Clamp01((float)aa[0]), Mathf.Clamp01((float)aa[1]), Mathf.Clamp01((float)aa[2]), 1f);
            }
            if (ModJson.Get(o, "skybox") is JToken sb) s.skybox = ParseSkybox(mod, sb, $"{where}: orbit");
            if (ModJson.Get(o, "skyRotation") is JToken sr)
            {
                if (!(sr is JArray ra) || ra.Count != 3 || ra.Any(v => v.Type != JTokenType.Integer && v.Type != JTokenType.Float))
                    throw new ModJsonException($"{where}: orbit \"skyRotation\" must be [x, y, z] (degrees)");
                s.skyRotation = new Vector3((float)ra[0], (float)ra[1], (float)ra[2]);
            }
            if (ModJson.Get(o, "planet") is JToken pl)
            {
                if (!(pl is JObject po)) throw new ModJsonException($"{where}: orbit \"planet\" must be {{ \"size\", \"yaw\", \"pitch\" }}");
                if (ModJson.Has(po, "size")) s.planetSize = Mathf.Clamp(ModJson.Float(po, "size", 1f, StationsFile), 0.05f, 5f);
                if (ModJson.Has(po, "yaw")) s.planetYaw = ModJson.Float(po, "yaw", 0f, StationsFile);
                if (ModJson.Has(po, "pitch")) s.planetPitch = Mathf.Clamp(ModJson.Float(po, "pitch", 0f, StationsFile), -89f, 89f);
            }
            return s;
        }
        /// <summary>stations.json "mapPlace": { "angle": degrees, "distance": map units }.</summary>
        static MapPlace ParseMapPlace(JToken t, string where)
        {
            if (!(t is JObject o)) throw new ModJsonException($"{where}: \"mapPlace\" must be {{ \"angle\": degrees, \"distance\": map units }}");
            var p = new MapPlace();
            if (ModJson.Has(o, "angle")) p.angle = Mathf.Repeat(ModJson.Float(o, "angle", 0f, StationsFile), 360f);
            if (ModJson.Has(o, "distance")) p.distance = Mathf.Clamp(ModJson.Float(o, "distance", 20000f, StationsFile), 3000f, 120000f);
            return p;
        }
        /// <summary>A system's own sun texture (Backdrop's "mod:" name), null: the game's.</summary>
        public static string SunTexture(int system) => sunTextures.TryGetValue(system, out var t) ? t : null;
        /// <summary>A system's own sun light colour.</summary>
        public static bool SunColor(int system, out Color c) => sunColors.TryGetValue(system, out c);
        /// <summary>Every mod planet / sun / sky texture (ModStations decodes them in the background with the models).</summary>
        public static IEnumerable<string> BackdropTextures()
        {
            Database.Load();
            var sky = skyboxes.Values.Concat(orbits.Values.Select(o => o.skybox).Where(b => b != null))
                .SelectMany(s => new[] { s.nebula, s.stars }).Where(n => n != null);
            return planetTextures.Values.Concat(sunTextures.Values).Concat(mapTextures.Values).Concat(sky).Distinct().ToList();
        }
        public static Skybox SkyOf(int system) => skyboxes.TryGetValue(system, out var s) ? s : null;
        /// <summary>"skybox": { "nebula": "sky/x.png", "stars": "sky/s.png" | 0-2 | "none", "nebulaBrightness", "starsBrightness",
        /// "rotation": [x, y, z], "sunAligned": true }.</summary>
        static Skybox ParseSkybox(ModInfo mod, JToken t, string where)
        {
            if (!(t is JObject o)) throw new ModJsonException($"{where}: \"skybox\" must be an object {{ \"nebula\": \"sky/....png\", ... }}");
            foreach (var p in o.Properties())
                if (!SkyFields.Contains(p.Name)) Report(mod, $"{where}: unknown skybox field \"{p.Name}\" (ignored)");
            var s = new Skybox
            {
                nebulaBrightness = Mathf.Max(0f, ModJson.Float(o, "nebulaBrightness", 1f, SystemsFile)),
                starsBrightness = Mathf.Max(0f, ModJson.Float(o, "starsBrightness", 1f, SystemsFile)),
                sunAligned = ModJson.Bool(o, "sunAligned", false, SystemsFile),
            };
            string nebula = ModJson.Str(o, "nebula");
            if (nebula != null)
            {
                if (!mod.Source.Exists(nebula)) throw new ModJsonException($"{where}: the skybox image {nebula} isn't in the mod");
                s.nebula = BackdropName(mod, nebula);
            }
            var stars = ModJson.Get(o, "stars");
            if (stars != null)
            {
                if (stars.Type == JTokenType.Integer) s.starsLayer = Mathf.Clamp((int)stars, 0, 2);
                else if (stars.Type == JTokenType.String && ((string)stars).Equals("none", StringComparison.OrdinalIgnoreCase)) s.starsLayer = -2;
                else if (stars.Type == JTokenType.String)
                {
                    if (!mod.Source.Exists((string)stars)) throw new ModJsonException($"{where}: the skybox image {(string)stars} isn't in the mod");
                    s.stars = BackdropName(mod, (string)stars);
                }
                else throw new ModJsonException($"{where}: skybox \"stars\" is an image, a star layer 0-2 or \"none\"");
            }
            if (ModJson.Get(o, "rotation") is JToken r)
            {
                if (!(r is JArray a) || a.Count != 3) throw new ModJsonException($"{where}: skybox \"rotation\" must be [x, y, z] degrees");
                s.rotation = new Vector3((float)a[0], (float)a[1], (float)a[2]);
            }
            return s;
        }
        static int InteriorOf(JObject o) => RaceToken(ModJson.Get(o, "interior"));
        /// <summary>A system's "race": 0-3, or a mod race ("mod_id:id", the mod's own id) by its race number.</summary>
        static int RaceOf(JObject o, Def d, ModInfo mod)
        {
            var t = ModJson.Get(o, "race");
            if (t == null) return 0;
            if (t.Type != JTokenType.String) return Mathf.Clamp(ModJson.Int(o, "race", 0, d.file), 0, 3);
            if (ModRaces.TryResolve((string)t, mod, out int race)) return race;
            Report(mod, $"{d.where}: \"race\": {(string)t} is no race (Terran instead)");
            return 0;
        }
        /// <summary>terran / vossk / nivelian / midorian or 0-3; -1 otherwise.</summary>
        static int RaceToken(JToken t)
        {
            if (t == null) return -1;
            if (t.Type == JTokenType.Integer) { int v = (int)t; return v >= 0 && v <= 3 ? v : -1; }
            switch (((string)t ?? "").Trim().ToLowerInvariant())
            {
                case "terran": return 0;
                case "vossk": return 1;
                case "nivelian": return 2;
                case "midorian": return 3;
            }
            return -1;
        }
        static List<(bool, Vector3, Vector3, float)> ReadVolumes(JObject o, Def d)
        {
            if (!ModJson.Has(o, "volumes")) return null;
            if (!(ModJson.Get(o, "volumes") is JArray list)) throw new ModJsonException($"{d.where}: \"volumes\" must be a list");
            var r = new List<(bool, Vector3, Vector3, float)>();
            foreach (var t in list)
            {
                string where = ModJson.Where(t, d.file);
                float[] F(JToken a, int n)
                {
                    if (!(a is JArray arr) || arr.Count != n) return null;
                    var v = new float[n];
                    for (int i = 0; i < n; i++) v[i] = (float)arr[i];
                    return v;
                }
                if (t is JObject vo && vo["box"] != null && F(vo["box"], 6) is float[] b)
                    r.Add((false, new Vector3(b[0], b[1], b[2]), new Vector3(Mathf.Abs(b[3]), Mathf.Abs(b[4]), Mathf.Abs(b[5])), 0f));
                else if (t is JObject so && so["sphere"] != null && F(so["sphere"], 4) is float[] s)
                    r.Add((true, new Vector3(s[0], s[1], s[2]), Vector3.zero, Mathf.Abs(s[3])));
                else throw new ModJsonException($"{where}: a volume is {{ \"box\": [x, y, z, hx, hy, hz] }} or {{ \"sphere\": [x, y, z, r] }} (game units)");
            }
            return r;
        }
        /// <summary>stations.json "life" (LifeSpec): { "scene", "loops": [ { "points", "count", "speed", "loop", "twoLanes",
        /// "laneOffset", "spots": [[x, y, z, nx, ny, nz], ...] } ] }, at most MaxLifeLoops loops.</summary>
        public const int MaxLifeLoops = 24, MaxLifeDrones = 40, MaxLifeSpots = 64;
        static LifeSpec ParseLife(ModInfo mod, JToken t, string where)
        {
            if (!(t is JObject o)) throw new ModJsonException($"{where}: \"life\" must be {{ \"scene\", \"loops\": [ ... ] }}");
            foreach (var p in o.Properties())
                if (p.Name != "scene" && p.Name != "loops") Report(mod, $"{where}: unknown life field \"{p.Name}\" (ignored)");
            var life = new LifeSpec { scene = ModJson.Str(o, "scene", "life") };
            if (!(ModJson.Get(o, "loops") is JArray list)) throw new ModJsonException($"{where}: \"life\" needs \"loops\": [ {{ \"points\": [[x, y, z], ...] }} ]");
            if (list.Count > MaxLifeLoops) throw new ModJsonException($"{where}: at most {MaxLifeLoops} life loops a station");
            Vector3 V(JToken a, int at, string what)
            {
                var arr = (JArray)a;
                var v = new Vector3((float)arr[at], (float)arr[at + 1], (float)arr[at + 2]);
                if (Mathf.Abs(v.x) > 1e5f || Mathf.Abs(v.y) > 1e5f || Mathf.Abs(v.z) > 1e5f) throw new ModJsonException($"{where}: a life loop's {what} lies too far out (at most 100000 m)");
                return v;
            }
            bool Numbers(JToken a, int n) => a is JArray arr && arr.Count == n && arr.All(v => v.Type == JTokenType.Integer || v.Type == JTokenType.Float);
            foreach (var e in list)
            {
                if (!(e is JObject l)) throw new ModJsonException($"{where}: a life loop is {{ \"points\", \"count\", \"speed\", \"loop\", \"twoLanes\", \"laneOffset\", \"spots\" }}");
                foreach (var p in l.Properties())
                    if (p.Name != "points" && p.Name != "count" && p.Name != "speed" && p.Name != "loop" && p.Name != "twoLanes" && p.Name != "laneOffset" && p.Name != "spots")
                        Report(mod, $"{where}: unknown life loop field \"{p.Name}\" (ignored)");
                if (!(ModJson.Get(l, "points") is JArray pts) || pts.Count < 2) throw new ModJsonException($"{where}: a life loop needs \"points\": at least two [x, y, z]");
                var loop = new LifeLoop();
                foreach (var pt in pts)
                {
                    if (!Numbers(pt, 3)) throw new ModJsonException($"{where}: a life loop's points are [x, y, z] (the model's metres)");
                    loop.points.Add(V(pt, 0, "point"));
                }
                loop.count = Mathf.Clamp(ModJson.Int(l, "count", 4, StationsFile), 1, MaxLifeDrones);
                loop.speed = Mathf.Clamp(ModJson.Float(l, "speed", 20f, StationsFile), 0.5f, 500f);
                loop.laneOffset = Mathf.Clamp(ModJson.Float(l, "laneOffset", 5f, StationsFile), 0f, 100f);
                loop.loop = ModJson.Bool(l, "loop", true, StationsFile);
                loop.twoLanes = ModJson.Bool(l, "twoLanes", true, StationsFile);
                if (ModJson.Get(l, "spots") is JToken sp)
                {
                    if (!(sp is JArray spots)) throw new ModJsonException($"{where}: a life loop's \"spots\" are [[x, y, z, nx, ny, nz], ...]");
                    foreach (var s in spots.Take(MaxLifeSpots))
                    {
                        if (!Numbers(s, 6)) throw new ModJsonException($"{where}: a weld spot is [x, y, z, nx, ny, nz] (the model's metres, its outward normal)");
                        var n = V(s, 3, "spot normal");
                        if (n.sqrMagnitude < 1e-6f) throw new ModJsonException($"{where}: a weld spot's normal is zero");
                        loop.spots.Add((V(s, 0, "spot"), n.normalized));
                    }
                }
                life.loops.Add(loop);
            }
            return life.loops.Count > 0 ? life : null;
        }
        /// <summary>stations.json "planetSphere": { "look": "es2Desert", "maps": { role: PNG, ... }, "atmosphere": [r, g, b] }; the
        /// maps must be in the mod ('check').</summary>
        static PlanetSphereSpec ParsePlanetSphere(ModInfo mod, JToken t, string where, bool check)
        {
            if (!(t is JObject o)) throw new ModJsonException($"{where}: \"planetSphere\" must be {{ \"look\", \"maps\", \"atmosphere\" }}");
            foreach (var p in o.Properties())
                if (p.Name != "look" && p.Name != "maps" && p.Name != "atmosphere" && p.Name != "colors" && p.Name != "variation") Report(mod, $"{where}: unknown planetSphere field \"{p.Name}\" (ignored)");
            var spec = new PlanetSphereSpec { mod = mod, look = ModJson.Str(o, "look") };
            if (Array.IndexOf(PlanetSphereLooks, spec.look) < 0)
                throw new ModJsonException($"{where}: planetSphere \"look\" is one of {string.Join(", ", PlanetSphereLooks)}");
            if (ModJson.Get(o, "maps") is JObject maps)
                foreach (var p in maps.Properties())
                {
                    if (p.Value.Type != JTokenType.String) throw new ModJsonException($"{where}: planetSphere map \"{p.Name}\" is a PNG in the mod");
                    string file = (string)p.Value;
                    if (check && !mod.Source.Exists(file)) throw new ModJsonException($"{where}: the planetSphere map {file} isn't in the mod");
                    spec.maps[p.Name] = file;
                }
            if (ModJson.Get(o, "atmosphere") is JArray a && a.Count >= 3) spec.atmosphere = new Color((float)a[0], (float)a[1], (float)a[2]);
            if (ModJson.Get(o, "colors") is JArray cs && cs.Count >= 2 && cs[0] is JArray c1 && c1.Count >= 3 && cs[1] is JArray c2 && c2.Count >= 3)
            {
                spec.color1 = new Vector3((float)c1[0], (float)c1[1], (float)c1[2]);
                spec.color2 = new Vector3((float)c2[0], (float)c2[1], (float)c2[2]);
            }
            if (ModJson.Has(o, "variation")) spec.variation = Mathf.Repeat(ModJson.Float(o, "variation", 0f, StationsFile), 1f);
            return spec;
        }
        /// <summary>A mod's PNG as Backdrop's texture name.</summary>
        static string BackdropName(ModInfo mod, string path) => "mod:" + mod.Id + "|" + path;
        /// <summary>systems.json "traffic": "mod": only mods' ships fly in the system (no game fighters, freighters, capital
        /// ships or GoF2 pirate events).</summary>
        public static bool ModTrafficOnly(int system) => modTrafficSystems.Contains(system);
        /// <summary>systems.json "locals": the races / mod races of the system's local fighters (null: its owner).</summary>
        public static List<int> Locals(int system) => systemLocals.TryGetValue(system, out var l) ? l : null;
        /// <summary>systems.json "raiders": the races / mod races that raid the system (null: the game's rule).</summary>
        public static List<int> Raiders(int system) => systemRaiders.TryGetValue(system, out var l) ? l : null;
        /// <summary>Every orbit's container, asteroid and wreck models (stations.json "orbit.containers" "model" /
        /// "bulletproofModel", "orbit.asteroids" / "orbit.wrecks" "models") with their mods and materials: ModCrates loads them.</summary>
        public static List<(ModInfo mod, string path, List<CustomShipMaterial> materials)> ContainerModels()
        {
            Database.Load();   // (the orbits are read with the database, as the station models are: Models)
            var l = new List<(ModInfo, string, List<CustomShipMaterial>)>();
            foreach (var o in orbits.Values)
            {
                if (o?.owner == null) continue;
                if (o.containerModel != null) l.Add((o.owner, o.containerModel, o.containerMaterials));
                if (o.containerProofModel != null) l.Add((o.owner, o.containerProofModel, o.containerMaterials));
                if (o.asteroidModels != null) foreach (var p in o.asteroidModels) l.Add((o.owner, p, o.asteroidMaterials));   // (and its asteroids')
                if (o.wreckModels != null) foreach (var p in o.wreckModels) l.Add((o.owner, p, o.wreckMaterials));   // (and its wrecks')
                if (o.props != null) foreach (var p in o.props) l.Add((o.owner, p.model, o.propMaterials));   // (and its props)
            }
            return l;
        }

        /// <summary>The station's orbit's "locals" (stations.json "orbit.locals"), else its system's.</summary>
        public static List<int> Locals(int system, int station) => OrbitRaces(station, true) ?? Locals(system);
        /// <summary>The station's orbit's "raiders" (stations.json "orbit.raiders"), else its system's.</summary>
        public static List<int> Raiders(int system, int station) => OrbitRaces(station, false) ?? Raiders(system);

        static List<int> OrbitRaces(int station, bool locals)
        {
            var o = OrbitOf(station);
            var refs = o == null ? null : locals ? o.locals : o.raiders;
            if (refs == null) return null;
            var l = new List<int>();
            foreach (var r in refs) if (ModRaces.TryResolve(r, o.owner, out int race)) l.Add(race);
            return l.Count > 0 ? l : null;
        }
        /// <summary>stations.json "subtitle": what the place is ("Satellite Ruins"), under its name on the star map; null without.</summary>
        public static string Subtitle(int station) => stationSubtitle.TryGetValue(station, out var s) ? s : null;
        /// <summary>stations.json "description": the place's lore, on the star map and in its system's news ticker; null without.</summary>
        public static string Description(int station) => stationLore.TryGetValue(station, out var s) ? s : null;
        /// <summary>The stations of 'system' with a description (the ticker's lore items).</summary>
        public static List<int> StationsWithLore(Database db, int system)
        {
            var r = new List<int>();
            if (stationLore.Count == 0) return r;
            foreach (var kv in stationLore)
                if (db.Stations.Find(x => x.index == kv.Key) is StationData st && st.system == system) r.Add(kv.Key);
            r.Sort();
            return r;
        }
        static readonly string[] AmbienceFields = { "loops", "loop", "adds", "volume", "addVolume", "addEveryMs", "centre", "center", "radius" };

        /// <summary>The calm space music a mod gave the station's system (null: the game's).</summary>
        public static AudioClip SpaceMusic(int station)
        {
            if (systemSpaceMusic.Count == 0) return null;   // every music change asks: no table lookup without mod music
            var st = Database.Shared.Stations.Find(s => s.index == station);
            return st != null && systemSpaceMusic.TryGetValue(st.system, out string name) ? ModMusic.Track(name) : null;
        }
        /// <summary>The fight music a mod gave the station's system for a battle category (1 low, 2 medium, 3 full: the
        /// hostiles around; null: the game's).</summary>
        public static AudioClip BattleMusic(int station, int category) => BattleMusicName(station, category) is string n ? ModMusic.Track(n) : null;
        /// <summary>BattleMusic's track name.</summary>
        public static string BattleMusicName(int station, int category)
        {
            if (systemBattleMusic.Count == 0 || category < 1 || category > 3) return null;
            var st = Database.Shared.Stations.Find(s => s.index == station);
            if (st == null || !systemBattleMusic.TryGetValue(st.system, out var l)) return null;
            return l[l.Count == 3 ? category - 1 : 0];
        }
        /// <summary>The docked music a mod gave the station or its system (null: the game's).</summary>
        public static AudioClip StationMusic(int station)
        {
            if (stationMusic.TryGetValue(station, out string name)) return ModMusic.Track(name);
            if (systemStationMusic.Count == 0) return null;
            var st = Database.Shared.Stations.Find(s => s.index == station);
            return st != null && systemStationMusic.TryGetValue(st.system, out name) ? ModMusic.Track(name) : null;
        }
    }
}
