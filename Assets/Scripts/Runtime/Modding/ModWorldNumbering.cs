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
            if (originalSystems < 0) originalSystems = db.Systems.Count;
            if (originalStations < 0) originalStations = db.Stations.Count;
        }
        static void EnsureMapping()
        {
            if (mappedRevision == ModManager.Revision && originalSystems >= 0) return;
            if (originalSystems < 0) { Database.Load(); if (originalSystems < 0) return; }
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
            var s = new OrbitSpec();
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
            var sky = skyboxes.Values.SelectMany(s => new[] { s.nebula, s.stars }).Where(n => n != null);
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
        /// <summary>A mod's PNG as Backdrop's texture name.</summary>
        static string BackdropName(ModInfo mod, string path) => "mod:" + mod.Id + "|" + path;
        /// <summary>systems.json "traffic": "mod": only mods' ships fly in the system (no game fighters, freighters, capital
        /// ships or GoF2 pirate events).</summary>
        public static bool ModTrafficOnly(int system) => modTrafficSystems.Contains(system);
        /// <summary>systems.json "locals": the races / mod races of the system's local fighters (null: its owner).</summary>
        public static List<int> Locals(int system) => systemLocals.TryGetValue(system, out var l) ? l : null;
        /// <summary>systems.json "raiders": the races / mod races that raid the system (null: the game's rule).</summary>
        public static List<int> Raiders(int system) => systemRaiders.TryGetValue(system, out var l) ? l : null;
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
        /// <summary>The calm space music a mod gave the station's system (null: the game's).</summary>
        public static AudioClip SpaceMusic(int station)
        {
            if (systemSpaceMusic.Count == 0) return null;   // every music change asks: no table lookup without mod music
            var st = Database.Shared.Stations.Find(s => s.index == station);
            return st != null && systemSpaceMusic.TryGetValue(st.system, out string name) ? ModMusic.Track(name) : null;
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
