// ModWorldMerging.cs
// ModWorld: merging and mobile stations (part of the partial ModWorld; its fields are in ModWorld.cs).

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
        // ---- merging -------------------------------------------------------------------------------------------------
        /// <summary>ModContent.Apply: the active mods' systems and stations into the freshly read tables.</summary>
        public static void Apply(Database db)
        {
            NoteOriginalCounts(db);
            EnsureMapping();
            systemSpaceMusic.Clear(); systemStationMusic.Clear(); stationMusic.Clear(); stationSubtitle.Clear(); stationLore.Clear();
            stationModels.Clear(); gateModels.Clear(); stationInterior.Clear(); planetTextures.Clear(); sunTextures.Clear(); sunColors.Clear(); skyboxes.Clear();
            mapTextures.Clear(); mapRings.Clear(); orbits.Clear(); mapPlaces.Clear();
            stationHangar.Clear(); stationBar.Clear(); mobiles.Clear();
            modTrafficSystems.Clear(); systemLocals.Clear(); systemRaiders.Clear(); systemPirateHeld.Clear();
            if (systemAt.Count == 0 && stationAt.Count == 0 && missingSystems.Count == 0 && missingStations.Count == 0
                && !ModManager.Active.Any(m => ModContent.Parse(m).systems.Count + ModContent.Parse(m).stations.Count > 0)) return;
            var gates = new List<(int a, int b)>();
            var gateStations = new Dictionary<int, string>();
            foreach (var mod in ModManager.Active)
            {
                var p = ModContent.Parse(mod);
                foreach (var d in p.systems)
                {
                    try
                    {
                        var o = d.json;
                        SystemData s;
                        if (d.overrideRef != null)
                        {
                            if (!TryResolveSystem(d.overrideRef, out int t) || (s = db.Systems.Find(x => x.index == t)) == null)
                            { Report(mod, $"{d.where}: \"override\": {d.overrideRef} is no system"); continue; }
                        }
                        else
                        {
                            int index = systemIndex[d.key];
                            var pos = (JArray)ModJson.Get(o, "position");
                            int race = RaceOf(o, d, mod);
                            int look = ModRaces.Look(race);   // a mod race's system: the template of its look race
                            var template = db.Systems.Find(x => x.index < originalSystems && x.raceId == look) ?? db.Systems[0];
                            s = new SystemData
                            {
                                index = index, raceId = race, race = template.race, initiallyVisible = true, jumpgateStation = -1,
                                mapPosition = new MapPos { x = (int)pos[0], y = (int)pos[1], z = (int)pos[2] },
                                textureIndex = template.textureIndex,
                                stations = new List<int>(), jumpRoutesTo = new List<int>(),
                                unknownTriple = new List<int>(template.unknownTriple ?? new List<int>()),
                                forbiddenGoodsOrUnknown = new List<int>(template.forbiddenGoodsOrUnknown ?? new List<int>()),
                            };
                            db.Systems.Add(s);
                        }
                        if (d.name != null) s.name = Text(d, "systems");
                        if (d.overrideRef != null && ModJson.Has(o, "race")) s.raceId = RaceOf(o, d, mod);
                        if (ModJson.Str(o, "traffic") == "mod") modTrafficSystems.Add(s.index);
                        else if (ModJson.Str(o, "traffic") == "all") modTrafficSystems.Remove(s.index);
                        if (ModJson.Has(o, "pirateHeld"))
                        {
                            if (ModJson.Bool(o, "pirateHeld", false, d.file)) systemPirateHeld.Add(s.index); else systemPirateHeld.Remove(s.index);
                        }
                        foreach (var (field, into) in new[] { ("locals", systemLocals), ("raiders", systemRaiders) })
                        {
                            if (!ModJson.Has(o, field)) continue;
                            var l = new List<int>();
                            foreach (var r in ModJson.Strings(o, field))
                                if (ModRaces.TryResolve(r, mod, out int rr)) l.Add(rr);
                                else Report(mod, $"{d.where}: {field}: \"{r}\" is no race");
                            if (l.Count > 0) into[s.index] = l;
                        }
                        s.securityLevel = Mathf.Clamp(ModJson.Int(o, "security", s.securityLevel, d.file), 0, 3);
                        s.initiallyVisible = ModJson.Bool(o, "visible", s.initiallyVisible, d.file);
                        s.textureIndex = Mathf.Clamp(ModJson.Int(o, "sky", s.textureIndex, d.file), 0, 18);
                        foreach (var g in ModJson.Strings(o, "gates"))
                        {
                            if (TryResolveSystem(g, out int other) && other != s.index) gates.Add((s.index, other));
                            else Report(mod, $"{d.where}: gate to \"{g}\": no such system");
                        }
                        if (ModJson.Str(o, "gateStation") is string gs) gateStations[s.index] = gs;
                        if (ModJson.Str(o, "spaceMusic") is string sm) systemSpaceMusic[s.index] = sm;
                        if (ModJson.Str(o, "stationMusic") is string stm) systemStationMusic[s.index] = stm;
                        if (ModJson.Str(o, "sunTexture") is string sun) sunTextures[s.index] = BackdropName(mod, sun);
                        if (ModJson.Get(o, "skybox") is JToken skyToken) skyboxes[s.index] = ParseSkybox(mod, skyToken, d.where);
                        if (ModJson.Get(o, "jumpgate") is JObject jgo && ModJson.Str(jgo, "model") is string gm)
                        {
                            var gate = new StationModel
                            {
                                mod = mod, station = GateKey(s.index), gateSystem = s.index, model = gm, label = (s.name ?? d.key) + " jumpgate",
                                size = GameModels.Is(gm) && !ModJson.Has(jgo, "size") ? 0f : Mathf.Max(1000f, ModJson.Float(jgo, "size", 15000f, d.file)),
                                yaw = ModJson.Float(jgo, "yaw", 0f, d.file), centre = true, collision = "none",
                            };
                            gate.gateRadius = Mathf.Max(0f, ModJson.Float(jgo, "radius", gate.size > 0f ? gate.size * 0.5f : 0f, d.file));
                            if (ModJson.Get(jgo, "materials") is JArray gmats)
                                foreach (var m in gmats) gate.materials.Add(JsonUtility.FromJson<CustomShipMaterial>(m.ToString()));
                            gateModels[s.index] = gate;
                            if (db.Assemblies.Find(a => a.name == gate.Assembly) == null)
                                db.Assemblies.Add(new AssemblyData { name = gate.Assembly, pack = ModContent.Pack, category = "level", origin = "mod " + mod.Id });
                        }
                        if (ModJson.Get(o, "sunColor") is JArray sc && sc.Count == 3)
                            sunColors[s.index] = new Color(Mathf.Clamp01((float)sc[0]), Mathf.Clamp01((float)sc[1]), Mathf.Clamp01((float)sc[2]));
                    }
                    catch (ModJsonException e) { Report(mod, e.Message); }
                }
            }
            foreach (var mod in ModManager.Active)
            {
                foreach (var d in ModContent.Parse(mod).stations)
                {
                    try
                    {
                        var o = d.json;
                        StationData st;
                        if (d.overrideRef != null)
                        {
                            if (!TryResolveStation(d.overrideRef, out int t) || (st = db.Stations.Find(x => x.index == t)) == null)
                            { Report(mod, $"{d.where}: \"override\": {d.overrideRef} is no station"); continue; }
                        }
                        else
                        {
                            int index = stationIndex[d.key];
                            if (!TryResolveSystem(ModJson.Str(o, "system"), out int sys) || db.Systems.Find(x => x.index == sys) is not SystemData sd)
                            { Report(mod, $"{d.where}: \"system\": {ModJson.Str(o, "system")} is no system"); continue; }
                            // A mobile station (MobileStations) has no planet: it isn't one of the system's stations.
                            var mobileSpec = ModJson.Get(o, "mobile") is JToken mt0 ? ParseMobile(mt0, d.where, d.file) : null;
                            if (mobileSpec == null && sd.stations.Count >= MaxStationsPerSystem)
                            { Report(mod, $"{d.where}: the system {sd.name} has {MaxStationsPerSystem} stations already (the most a system can hold)"); continue; }
                            st = new StationData { index = index, system = sys, systemName = sd.name, techLevel = 1, textureIndex = 0 };
                            db.Stations.Add(st);
                            if (mobileSpec == null) sd.stations.Add(index);
                            else { mobileSpec.mod = mod; mobileSpec.where = d.where; mobiles[index] = mobileSpec; }
                            int look = ModJson.Int(o, "looksLike", -1, d.file);
                            if (look < 0 || look >= originalStations)
                                look = db.Stations.Find(x => x.index < originalStations && db.Systems.Find(y => y.index == x.system)?.raceId == sd.raceId)?.index ?? 78;
                            stationLook[index] = look;
                        }
                        if (d.name != null) st.name = Text(d, "stations");
                        if (TextField(d, "stations", "subtitle") is string sub) stationSubtitle[st.index] = sub;
                        if (TextField(d, "stations", "description") is string lore) stationLore[st.index] = lore;
                        st.techLevel = Mathf.Clamp(ModJson.Int(o, "techLevel", st.techLevel, d.file), 0, 10);
                        st.textureIndex = ModJson.Int(o, "planet", st.textureIndex, d.file);
                        if (ModJson.Str(o, "music") is string mu) stationMusic[st.index] = mu;
                        int interior = InteriorOf(o);
                        if (interior >= 0) stationInterior[st.index] = interior;
                        if (ModJson.Str(o, "planetTexture") is string pt) planetTextures[st.index] = BackdropName(mod, pt);
                        if (ModJson.Str(o, "mapTexture") is string mt) mapTextures[st.index] = BackdropName(mod, mt);
                        if (ModJson.Bool(o, "mapRing", false, d.file)) mapRings.Add(st.index); else if (ModJson.Has(o, "mapRing")) mapRings.Remove(st.index);
                        if (ModJson.Get(o, "orbit") is JToken orbitToken)
                            orbits[st.index] = ParseOrbit(mod, orbitToken, d.where, r => ModContent.TryResolveItem(r, out int item) && db.Item(item) != null ? item : -1);
                        if (ModJson.Get(o, "mapPlace") is JToken placeToken) mapPlaces[st.index] = ParseMapPlace(placeToken, d.where);
                        // A custom room: "mod_id:interior_id", or this mod's own id.
                        if (ModJson.Str(o, "hangar") is string hk) stationHangar[st.index] = hk.Contains(":") ? hk : mod.Id + ":" + hk;
                        // ("bar": "none": the station has no Space Lounge, NoBar)
                        if (ModJson.Str(o, "bar") is string bk) stationBar[st.index] = bk.Contains(":") || bk == NoBarKey ? bk : mod.Id + ":" + bk;
                        if (ModJson.Str(o, "model") is string model)
                        {
                            var sm = new StationModel
                            {
                                mod = mod, station = st.index, model = model, label = st.name,
                                // A game model ("gof2:", GameModels) keeps its own size unless modelSize is given.
                                size = GameModels.Is(model) && !ModJson.Has(o, "modelSize") ? 0f : Mathf.Max(1000f, ModJson.Float(o, "modelSize", 40000f, d.file)),
                                yaw = ModJson.Float(o, "modelYaw", 0f, d.file),
                                centre = ModJson.Bool(o, "modelCentre", true, d.file),
                                collision = ModJson.Str(o, "collision", "box"),
                                volumes = ReadVolumes(o, d),
                            };
                            if (ModJson.Get(o, "materials") is JArray mats)
                                foreach (var m in mats) sm.materials.Add(JsonUtility.FromJson<CustomShipMaterial>(m.ToString()));
                            stationModels[st.index] = sm;
                            if (db.Assemblies.Find(a => a.name == sm.Assembly) == null)
                                db.Assemblies.Add(new AssemblyData { name = sm.Assembly, pack = ModContent.Pack, category = "stations", origin = "mod " + mod.Id });
                        }
                    }
                    catch (ModJsonException e) { Report(mod, e.Message); }
                }
            }
            // Gates both ways; a system with routes needs a gate station (its first station unless the entry names one).
            foreach (var (a, b) in gates)
            {
                var sa = db.Systems.Find(x => x.index == a);
                var sb = db.Systems.Find(x => x.index == b);
                if (sa == null || sb == null) continue;
                if (!sa.jumpRoutesTo.Contains(b)) sa.jumpRoutesTo.Add(b);
                if (!sb.jumpRoutesTo.Contains(a)) sb.jumpRoutesTo.Add(a);
            }
            foreach (var s in db.Systems)
            {
                if (gateStations.TryGetValue(s.index, out var gsRef) && TryResolveStation(gsRef, out int gst) && s.stations.Contains(gst)) s.jumpgateStation = gst;
                if (s.jumpRoutesTo.Count > 0 && (s.jumpgateStation < 0 || !s.stations.Contains(s.jumpgateStation)) && s.stations.Count > 0) s.jumpgateStation = s.stations[0];
            }
            // Mobile stations' routes: the stations they cruise past, in any systems (default all of their own system's);
            // their race; then the system of the orbit each is in now (MobileStations.HostAt), so the docked station's
            // header, prices and star map are where it is. A scene's own Load is taken when it starts: docked, the station
            // stays in the system it was docked in until the next scene.
            double now = MobileStations.Clock;
            foreach (var kv in mobiles)
            {
                var m = kv.Value;
                var st = db.Stations.Find(x => x.index == kv.Key);
                var sys = st != null ? db.Systems.Find(x => x.index == st.system) : null;
                m.system = st?.system ?? -1;
                m.route.Clear();
                m.roamSystems.Clear();
                if (m.roam)
                {
                    // "route": "any": every system with an orbit it may be in (MobileStations.RoamSkip: the story's and the
                    // Kaamo Club's), the same list in every game with these mods, so a session's players agree where it is.
                    var skip = new HashSet<int>(MobileStations.RoamSkip);
                    var skipSystems = new HashSet<int>();
                    foreach (var r in m.excludeRefs)
                        if (TryResolveSystem(r, out int xs)) skipSystems.Add(xs);
                        else Report(m.mod, $"{m.where}: \"exclude\" \"{r}\" is no system");
                    var onlySystems = new HashSet<int>();
                    foreach (var r in m.systemRefs)
                        if (TryResolveSystem(r, out int ys)) onlySystems.Add(ys);
                        else Report(m.mod, $"{m.where}: \"systems\" \"{r}\" is no system");
                    foreach (var sy in db.Systems.OrderBy(x => x.index))
                    {
                        if (skipSystems.Contains(sy.index) || IsMissingSystem(sy.index)) continue;
                        if (m.systemRefs.Count > 0 && !onlySystems.Contains(sy.index)) continue;
                        var orbits = sy.stations.Where(x => !skip.Contains(x) && !mobiles.ContainsKey(x) && !IsMissingStation(x)).ToList();
                        if (orbits.Count == 0) continue;
                        m.roamSystems.Add((sy.index, orbits));
                        m.route.AddRange(orbits);
                    }
                }
                else if (m.routeRefs.Count == 0 && sys != null) m.route.AddRange(sys.stations);
                foreach (var r in m.roam ? new List<string>() : m.routeRefs)
                {
                    if (!TryResolveStation(r, out int host) || mobiles.ContainsKey(host) || !db.Systems.Any(x => x.stations.Contains(host)))
                    { Report(m.mod, $"{m.where}: route stop \"{r}\" is no station with an orbit"); continue; }
                    if (!m.route.Contains(host)) m.route.Add(host);
                }
                if (m.route.Count == 0) Report(m.mod, $"{m.where}: a mobile station needs a system with stations to cruise past");
                m.race = m.raceToken >= 0 ? m.raceToken : stationInterior.TryGetValue(kv.Key, out int ir) ? ir : sys?.raceId ?? 0;
                int now0 = MobileStations.HostAt(m, kv.Key, now);
                var hs = now0 >= 0 ? db.Stations.Find(x => x.index == now0) : null;
                if (st != null && hs != null) st.system = hs.system;
            }
            // Station names carry their system's (StationData.systemName).
            foreach (var st in db.Stations)
                if (st.index >= originalStations || IsModSystem(st.system)) st.systemName = db.Systems.Find(x => x.index == st.system)?.name ?? st.systemName;
            foreach (var kv in missingSystems)
                if (db.Systems.Find(x => x.index == kv.Key) == null) db.Systems.Add(PlaceholderSystem(kv.Key, kv.Value));
            int maxSystem = db.Systems.Max(s => s.index);
            for (int i = originalSystems; i <= maxSystem; i++) if (db.Systems.Find(x => x.index == i) == null) db.Systems.Add(PlaceholderSystem(i, "?:" + i));
            foreach (var kv in missingStations)
                if (db.Stations.Find(x => x.index == kv.Key) == null) db.Stations.Add(PlaceholderStation(kv.Key, kv.Value));
            int maxStation = db.Stations.Max(s => s.index);
            for (int i = originalStations; i <= maxStation; i++) if (db.Stations.Find(x => x.index == i) == null) db.Stations.Add(PlaceholderStation(i, "?:" + i));
            db.Systems.Sort((a, c) => a.index.CompareTo(c.index));
            db.Stations.Sort((a, c) => a.index.CompareTo(c.index));
        }
        /// <summary>stations.json "subtitle" / "description" (a string or one per language, or text/<lang>.json's
        /// "stations.<id>.subtitle" / ".description"), in the game's language; null without one.</summary>
        static string TextField(Def d, string kind, string field)
        {
            string lang = Localization.Language;
            var own = ModJson.Text(d.json, field);
            var text = ModContent.Parse(d.mod).text;
            string key = $"{kind}.{d.localId ?? d.overrideRef}.{field}";
            if (own != null && own.TryGetValue(lang, out var s)) return s;
            if (text.TryGetValue(lang, out var table) && table.TryGetValue(key, out s)) return s;
            if (own != null && (own.TryGetValue("en", out s) || own.TryGetValue("", out s))) return s;
            return own?.Values.FirstOrDefault();
        }
        static string Text(Def d, string kind)
        {
            string lang = Localization.Language;
            var text = ModContent.Parse(d.mod).text;
            string key = $"{kind}.{d.localId ?? d.overrideRef}.name";
            if (d.name != null && d.name.TryGetValue(lang, out var s)) return s;
            if (text.TryGetValue(lang, out var table) && table.TryGetValue(key, out s)) return s;
            if (d.name != null && (d.name.TryGetValue("en", out s) || d.name.TryGetValue("", out s))) return s;
            return d.name?.Values.FirstOrDefault() ?? d.key;
        }
        static SystemData PlaceholderSystem(int index, string key) => new SystemData
        {
            index = index, name = "?", initiallyVisible = false, jumpgateStation = -1, raceId = 0, race = "Terran",
            mapPosition = new MapPos { x = -1000, y = -1000, z = -1000 }, stations = new List<int>(), jumpRoutesTo = new List<int>(),
            unknownTriple = new List<int>(), forbiddenGoodsOrUnknown = new List<int>(),
        };
        static StationData PlaceholderStation(int index, string key) =>
            new StationData { index = index, name = $"Missing station ({key})", system = -1, systemName = "", techLevel = 0 };
        /// <summary>A mobile station (stations.json "mobile").</summary>
        public static bool IsMobile(int station) { _ = Database.Shared; return mobiles.ContainsKey(station); }
        public static MobileSpec MobileOf(int station) { _ = Database.Shared; return mobiles.TryGetValue(station, out var m) ? m : null; }
        /// <summary>Every mobile station of the mods that are on.</summary>
        public static List<int> Mobiles() { _ = Database.Shared; return mobiles.Keys.OrderBy(k => k).ToList(); }
        static MobileSpec ParseMobile(JToken t, string where, string file)
        {
            if (t.Type == JTokenType.Boolean) return (bool)t ? new MobileSpec() : null;
            if (!(t is JObject o)) throw new ModJsonException($"{where}: \"mobile\" must be true or {{ \"route\": [stations], \"stayMinutes\": 15, \"speed\": 0.2 }}");
            var m = new MobileSpec
            {
                stayMinutes = Mathf.Clamp(ModJson.Float(o, "stayMinutes", 15f, file), 1f, 240f),
                speed = Mathf.Clamp(ModJson.Float(o, "speed", 0.2f, file), 0f, 2f),
            };
            if (ModJson.Get(o, "route") is JValue rv && rv.Type == JTokenType.String)
            {
                if (!string.Equals(((string)rv).Trim(), "any", StringComparison.OrdinalIgnoreCase))
                    throw new ModJsonException($"{where}: \"route\" must be a list of stations or \"any\"");
                m.roam = true;
            }
            else m.routeRefs.AddRange(ModJson.Strings(o, "route"));
            m.systemMinutes = Mathf.Clamp(ModJson.Float(o, "systemMinutes", Mathf.Max(90f, m.stayMinutes), file), m.stayMinutes, 24f * 60f);
            m.excludeRefs.AddRange(ModJson.Strings(o, "exclude"));
            m.systemRefs.AddRange(ModJson.Strings(o, "systems"));
            if (ModJson.Get(o, "engines") is JArray ea)
            {
                m.engines = new List<WeaponMount>();
                foreach (var e in ea)
                {
                    var wm = e is JObject eo ? eo.ToObject<WeaponMount>() : null;
                    if (wm?.position_engine == null || wm.position_engine.Length < 3)
                        throw new ModJsonException($"{where}: every \"engines\" entry needs a \"position_engine\": [x, y, z]");
                    wm.slotType = 3;
                    m.engines.Add(wm);
                }
            }
            if (ModJson.Get(o, "engineColor") is JArray ec && ec.Count >= 3) m.engineColor = new[] { (float)ec[0], (float)ec[1], (float)ec[2] };
            if (ModJson.Get(o, "engineFx") is JObject efx) m.engineFx = efx.ToObject<CustomEngineFx>();
            if (ModJson.Has(o, "race"))
            {
                m.raceToken = RaceToken(ModJson.Get(o, "race"));
                if (m.raceToken < 0) throw new ModJsonException($"{where}: \"race\" must be terran, vossk, nivelian, midorian or 0-3");
            }
            return m;
        }
        static void Report(ModInfo mod, string message)
        {
            if (mod.Warnings.Contains(message)) return;
            mod.Warnings.Add(message);
            Debug.LogWarning($"Mods: {mod.Id}: {message}");
        }
    }
}
