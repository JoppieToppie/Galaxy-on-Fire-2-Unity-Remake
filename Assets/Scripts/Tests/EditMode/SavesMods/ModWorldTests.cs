// ModWorldTests.cs
// Feature: CLAUDE.md "Mods (remake-only)" > Systems and stations / Station models, planets, suns / Skies, and the example
// Modding/Examples/frontier_systems (ModWorld.cs): new systems after the original 34 and stations after the 135 (at most 7
// per system), "looksLike" = the original station whose model and collision it uses, jumpgate routes both ways, a gate
// station (the named one, else the first), overrides, placeholders for a mod that is off (never on the map, revealed or not). Expected values from the
// example's systems.json / stations.json (Kepler: Terran, security 2, sky 4, position 8 / 27 / 83, gates to 4 and 17,
// gate station rho_freeport; Kepler Prime tech 7, planet 12, looks like 40, a 50000-unit model; Rho Freeport tech 4,
// planet 6, looks like 41, the Nivelian interior; sun colour 1 / 0.8 / 0.55; skybox nebula, star layer 1, brightness 0.8).

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ModWorldTests : SavesModsTest
    {
        Database Frontier()
        {
            CopyExample("frontier_systems");
            ScanAndEnable("frontier_systems");
            var m = Installed("frontier_systems");
            Assert.IsFalse(m.Broken, AllMessages(m));
            return Database.Load();
        }

        [Test]
        public void TheExampleSystemIsAdded()
        {
            var db = Frontier();
            Assert.AreEqual(35, db.Systems.Count);
            var k = db.Systems[34];
            Assert.AreEqual(34, k.index);
            Assert.AreEqual("Kepler", k.name);
            Assert.AreEqual(0, k.raceId);
            Assert.AreEqual(2, k.securityLevel);
            Assert.IsTrue(k.initiallyVisible);
            Assert.AreEqual(4, k.textureIndex);
            Assert.AreEqual(8, k.mapPosition.x);
            Assert.AreEqual(27, k.mapPosition.y);
            Assert.AreEqual(83, k.mapPosition.z);
            Assert.AreEqual(34, ModWorld.SystemIndexOf("frontier_systems:kepler"));
            Assert.IsTrue(ModWorld.IsModSystem(34));
            Assert.AreSame(Installed("frontier_systems"), ModWorld.SystemOwner(34));
            Assert.IsNull(ModWorld.SystemOwner(4));
            Assert.AreEqual(34, ModWorld.OriginalSystemCount);
        }

        [Test]
        public void GatesGoBothWays()
        {
            var db = Frontier();
            CollectionAssert.IsSubsetOf(new[] { 4, 17 }, db.Systems[34].jumpRoutesTo);
            CollectionAssert.Contains(db.Systems[4].jumpRoutesTo, 34);
            CollectionAssert.Contains(db.Systems[17].jumpRoutesTo, 34);
            Assert.AreEqual(ModWorld.StationIndexOf("frontier_systems:rho_freeport"), db.Systems[34].jumpgateStation, "the named gate station");
        }

        [Test]
        public void TheExampleStationsAreAdded()
        {
            var db = Frontier();
            Assert.AreEqual(137, db.Stations.Count);
            int prime = ModWorld.StationIndexOf("frontier_systems:kepler_prime");
            int rho = ModWorld.StationIndexOf("frontier_systems:rho_freeport");
            Assert.AreEqual(135, prime);
            Assert.AreEqual(136, rho);
            var p = db.Stations[135];
            Assert.AreEqual("Kepler Prime", p.name);
            Assert.AreEqual(34, p.system);
            Assert.AreEqual("Kepler", p.systemName);
            Assert.AreEqual(7, p.techLevel);
            Assert.AreEqual(12, p.textureIndex);
            Assert.AreEqual(Localization.Language == "de" ? "Freihafen Rho" : "Rho Freeport", db.Stations[136].name);
            Assert.AreEqual(4, db.Stations[136].techLevel);
            Assert.AreEqual(6, db.Stations[136].textureIndex);
            CollectionAssert.AreEqual(new[] { 135, 136 }, db.Systems[34].stations);
            Assert.AreEqual(40, ModWorld.StationLook(135));
            Assert.AreEqual(41, ModWorld.StationLook(136));
            Assert.AreEqual(78, ModWorld.StationLook(78), "an original station looks like itself");
            Assert.IsTrue(ModWorld.IsModStation(135));
            Assert.IsFalse(ModWorld.IsModStation(78));
            for (int i = 0; i < db.Stations.Count; i++) Assert.AreEqual(i, db.Stations[i].index);
        }

        [Test]
        public void TheExamplesLooksAreRead()
        {
            Frontier();
            Assert.AreEqual(-1, ModWorld.InteriorRace(135));
            Assert.AreEqual(2, ModWorld.InteriorRace(136), "nivelian");
            var model = ModWorld.ModelOf(135);
            Assert.IsNotNull(model);
            Assert.AreEqual("models/kepler_station.glb", model.model);
            Assert.AreEqual(50000f, model.size);
            Assert.AreEqual("box", model.collision);
            Assert.IsTrue(model.centre);
            Assert.AreEqual("modstation_135", model.Assembly);
            Assert.IsNull(ModWorld.ModelOf(136));
            Assert.AreEqual("frontier_systems:kepler_hangar", ModWorld.HangarKey(135));
            Assert.AreEqual("frontier_systems:kepler_bar", ModWorld.BarKey(135));
            Assert.IsNull(ModWorld.HangarKey(136));
            Assert.AreEqual("mod:frontier_systems|textures/kepler_prime_planet.png", ModWorld.PlanetTexture(135));
            Assert.AreEqual("mod:frontier_systems|textures/kepler_sun.png", ModWorld.SunTexture(34));
            Assert.IsTrue(ModWorld.SunColor(34, out Color c));
            Assert.AreEqual(1f, c.r, 1e-6f);
            Assert.AreEqual(0.8f, c.g, 1e-6f);
            Assert.AreEqual(0.55f, c.b, 1e-6f);
            Assert.IsFalse(ModWorld.SunColor(4, out _));
            var sky = ModWorld.SkyOf(34);
            Assert.IsNotNull(sky);
            Assert.AreEqual("mod:frontier_systems|sky/kepler_nebula.png", sky.nebula);
            Assert.AreEqual(1, sky.starsLayer);
            Assert.AreEqual(0.8f, sky.nebulaBrightness, 1e-6f);
            Assert.AreEqual(1f, sky.starsBrightness);
            Assert.IsNull(sky.rotation);
            Assert.IsNull(sky.stars);
        }

        [Test]
        public void TheModelsAssemblyIsAModAssembly()
        {
            var db = Frontier();
            var a = db.AssemblyByName("modstation_135");
            Assert.IsNotNull(a);
            Assert.AreEqual("mod", a.pack);
            Assert.AreEqual("stations", a.category);
        }

        // ---- inline mods --------------------------------------------------------------------------------------------------

        const string SystemsJson = "[ { 'id': 'home', 'name': 'Home', 'race': 2, 'security': 9, 'sky': 40, 'position': [50, 50, 50] } ]";

        Database World(string stationsJson, string systemsJson = SystemsJson)
        {
            WriteMod("w", "t_world", "1.0", "", ("systems.json", systemsJson), ("stations.json", stationsJson));
            ScanAndEnable("t_world");
            return Database.Load();
        }

        [Test]
        public void AGasGiantsSphereTakesItsColoursAndVariation()
        {
            World("[ { 'id': 'a', 'system': 't_world:home', 'name': 'A', 'planetSphere': { 'look': 'es2GasSmooth', 'colors': [[0.02, 0.07, 0.1], [0.17, 0.49, 0.5]], 'variation': 1.25 } }, "
                + "{ 'id': 'b', 'system': 't_world:home', 'name': 'B', 'planetSphere': { 'look': 'es2Desert' } } ]");
            var gas = ModWorld.PlanetSphereOf(135);
            Assert.AreEqual("es2GasSmooth", gas.look);
            Assert.AreEqual(new Vector3(0.02f, 0.07f, 0.1f), gas.color1.Value);
            Assert.AreEqual(new Vector3(0.17f, 0.49f, 0.5f), gas.color2.Value);
            Assert.AreEqual(0.25f, gas.variation, 1e-5f, "a whole turn more is the same hue");
            Assert.AreEqual(1, System.Array.IndexOf(ModWorld.PlanetSphereLooks, "es2GasSmooth"), "GoF2/Es2Planet's _Look 1");
            Assert.AreEqual(2, System.Array.IndexOf(ModWorld.PlanetSphereLooks, "es2Gas"));
            Assert.AreEqual(3, System.Array.IndexOf(ModWorld.PlanetSphereLooks, "es2Cratered"));
            Assert.AreEqual(4, System.Array.IndexOf(ModWorld.PlanetSphereLooks, "es2Volcanic"));
            Assert.AreEqual(6, System.Array.IndexOf(ModWorld.PlanetSphereLooks, "es2Water"));
            Assert.AreEqual(7, System.Array.IndexOf(ModWorld.PlanetSphereLooks, "es2Earthlike"));
            Assert.IsNull(ModWorld.PlanetSphereOf(136).color1, "the desert has none");
        }

        [Test]
        public void ValuesAreClampedAndDefaulted()
        {
            var db = World("[ { 'id': 'a', 'system': 't_world:home', 'name': 'A', 'techLevel': 15 } ]");
            var s = db.Systems[34];
            Assert.AreEqual(3, s.securityLevel, "security 0-3");
            Assert.AreEqual(18, s.textureIndex, "sky 0-18");
            Assert.AreEqual(2, s.raceId);
            Assert.AreEqual(-1, s.jumpgateStation, "no routes, no gate");
            Assert.AreEqual(0, s.jumpRoutesTo.Count);
            Assert.AreEqual(10, db.Stations[135].techLevel, "tech level 0-10");
            int look = ModWorld.StationLook(135);
            Assert.Less(look, OriginalStations);
            Assert.AreEqual(2, db.Systems[db.Stations[look].system].raceId, "without looksLike: an original station of the system's race");
        }

        [Test]
        public void ASystemWithGatesGetsItsFirstStationAsGateStation()
        {
            var db = World("[ { 'id': 'a', 'system': 't_world:home', 'name': 'A' }, { 'id': 'b', 'system': 't_world:home', 'name': 'B' } ]",
                "[ { 'id': 'home', 'name': 'Home', 'race': 0, 'position': [1, 2, 3], 'gates': [4] } ]");
            Assert.AreEqual(135, db.Systems[34].jumpgateStation);
        }

        [Test]
        public void ASystemHoldsAtMostSevenStations()
        {
            Assert.AreEqual(7, ModWorld.MaxStationsPerSystem);
            string stations = "[" + string.Join(", ", Enumerable.Range(0, 8).Select(i => "{ 'id': 's" + i + "', 'system': 't_world:home', 'name': 'S" + i + "' }")) + "]";
            var db = World(stations);
            Assert.AreEqual(7, db.Systems[34].stations.Count);
            Assert.IsTrue(Installed("t_world").Warnings.Any(w => w.Contains("7 stations already")), AllMessages(Installed("t_world")));
        }

        [Test]
        public void AnOverrideRenamesAnOriginal()
        {
            var db = World("[ { 'override': 78, 'name': 'New Hastra', 'techLevel': 9, 'interior': 'vossk' } ]",
                "[ { 'override': 15, 'name': 'New Mido', 'security': 1, 'visible': false } ]");
            Assert.AreEqual("New Hastra", db.Stations[78].name);
            Assert.AreEqual(9, db.Stations[78].techLevel);
            Assert.AreEqual(1, ModWorld.InteriorRace(78));
            Assert.AreEqual("New Mido", db.Systems[15].name);
            Assert.AreEqual(1, db.Systems[15].securityLevel);
            Assert.IsFalse(db.Systems[15].initiallyVisible);
            Assert.AreEqual(OriginalSystems, db.Systems.Count);
        }

        [Test]
        public void AWorldModTurnedOffLeavesPlaceholders()
        {
            World("[ { 'id': 'a', 'system': 't_world:home', 'name': 'A' } ]");
            ModManager.SetEnabled("t_world", false);
            var db = Database.Load();
            Assert.AreEqual(35, db.Systems.Count);
            Assert.AreEqual(136, db.Stations.Count);
            Assert.IsFalse(db.Systems[34].initiallyVisible);
            Assert.AreEqual(0, db.Systems[34].stations.Count);
            Assert.AreEqual(-1, db.Stations[135].system);
            Assert.IsTrue(ModWorld.IsMissingSystem(34));
            Assert.IsTrue(ModWorld.IsMissingStation(135));
        }

        [Test]
        public void ARevealedSystemOfAModTurnedOffIsntOnTheMap()
        {
            // The placeholder lies at -1000 on every axis: shown, the map's overview zoomed out to frame it.
            var saved = Session.SystemVisible;
            try
            {
                var db = World("[ { 'id': 'a', 'system': 't_world:home', 'name': 'A' } ]");
                Session.SystemVisible = null;
                GalaxyMap.Visibility(db);
                Session.SystemVisible[34] = true;
                Assert.IsTrue(GalaxyMap.IsVisible(db, 34), "the mod's system, revealed");

                ModManager.SetEnabled("t_world", false);
                db = Database.Load();
                Assert.IsTrue(ModWorld.IsMissingSystem(34));
                Assert.IsFalse(GalaxyMap.IsVisible(db, 34), "its placeholder");
                Assert.IsTrue(Session.SystemVisible[34], "still revealed for when the mod is back");
                Assert.IsFalse(ModWorld.IsMissingSystem(33), "an original system");
            }
            finally { Session.SystemVisible = saved; }
        }

        string WorldError(string file, string json)
        {
            string dir = WriteMod("e", "t_err", "1.0", "", (file, json));
            var m = OpenParsed(dir);
            Assert.IsTrue(m.Broken, "accepted: " + json);
            return m.Errors[0];
        }

        [Test] public void ANewSystemNeedsAPosition() => StringAssert.Contains("needs \"position\"", WorldError("systems.json", "[ { 'id': 'x', 'name': 'X' } ]"));
        [Test] public void ASystemsRaceIs0To3() => StringAssert.Contains("\"race\" must be 0-3", WorldError("systems.json", "[ { 'id': 'x', 'name': 'X', 'race': 8, 'position': [1, 1, 1] } ]"));
        [Test] public void ANewEntryNeedsAName() => StringAssert.Contains("needs \"name\"", WorldError("systems.json", "[ { 'id': 'x', 'position': [1, 1, 1] } ]"));
        [Test] public void SunColorIsThreeNumbers() => StringAssert.Contains("\"sunColor\" must be [r, g, b]", WorldError("systems.json", "[ { 'override': 1, 'sunColor': [1, 1] } ]"));
        [Test] public void ANewStationNeedsASystem() => StringAssert.Contains("needs \"system\"", WorldError("stations.json", "[ { 'id': 'x', 'name': 'X' } ]"));
        [Test] public void Planet23IsntAPlanet() => StringAssert.Contains("\"planet\" must be 0-22 or 24-26", WorldError("stations.json", "[ { 'override': 1, 'planet': 23 } ]"));
        [Test] public void Planet27IsntAPlanet() => StringAssert.Contains("\"planet\"", WorldError("stations.json", "[ { 'override': 1, 'planet': 27 } ]"));
        [Test] public void AnUnknownInterior() => StringAssert.Contains("\"interior\"", WorldError("stations.json", "[ { 'override': 1, 'interior': 'klingon' } ]"));
        [Test] public void AnUnknownCollision() => StringAssert.Contains("\"collision\" must be box, sphere or none", WorldError("stations.json", "[ { 'override': 1, 'collision': 'mesh' } ]"));
        [Test] public void ABrokenVolume() => StringAssert.Contains("a volume is", WorldError("stations.json", "[ { 'override': 1, 'volumes': [ { 'box': [1, 2, 3] } ] } ]"));
        [Test] public void AnEntryNeedsIdOrOverride() => StringAssert.Contains("either \"id\"", WorldError("stations.json", "[ { 'name': 'X' } ]"));

        [Test]
        public void AMissingSkyImageIsAnError()
        {
            StringAssert.Contains("isn't in the mod", WorldError("systems.json", "[ { 'override': 1, 'skybox': { 'nebula': 'sky/none.png' } } ]"));
        }

        [Test]
        public void AStarLayerOrNone()
        {
            WriteMod("w", "t_world", "1.0", "", ("systems.json", "[ { 'override': 1, 'skybox': { 'stars': 'none', 'rotation': [10, 20, 30] } }, { 'override': 2, 'skybox': { 'stars': 7 } } ]"));
            ScanAndEnable("t_world");
            Database.Load();
            Assert.AreEqual(-2, ModWorld.SkyOf(1).starsLayer);
            Assert.AreEqual(new Vector3(10, 20, 30), ModWorld.SkyOf(1).rotation.Value);
            Assert.AreEqual(2, ModWorld.SkyOf(2).starsLayer, "clamped to 0-2");
            Assert.IsNull(ModWorld.SkyOf(3));
        }

        [Test]
        public void AnOrbitMayHaveItsOwnSky()
        {
            WriteMod("w", "t_world", "1.0", "", ("sky/h2.png", "x"),
                ("stations.json", "[ { 'override': 78, 'orbit': { 'skybox': { 'nebula': 'sky/h2.png', 'stars': 1, 'nebulaBrightness': 0.5 } } } ]"));
            ScanAndEnable("t_world");
            Database.Load();
            var own = ModWorld.OrbitOf(78).skybox;
            Assert.AreEqual("mod:t_world|sky/h2.png", own.nebula);
            Assert.AreEqual(1, own.starsLayer);
            Assert.AreEqual(0.5f, own.nebulaBrightness);
            Assert.IsNull(ModWorld.OrbitOf(78).skyRotation);
            CollectionAssert.Contains(ModWorld.BackdropTextures().ToList(), "mod:t_world|sky/h2.png", "decoded with the other backdrops");
        }

        [Test]
        public void TheExamplesMapPlanetAndRing()
        {
            Frontier();
            Assert.AreEqual("mod:frontier_systems|textures/kepler_prime_map.png", ModWorld.MapTexture(135));
            Assert.IsTrue(ModWorld.MapRing(135));
            Assert.IsNull(ModWorld.MapTexture(136), "no map image: the game's planet");
            Assert.IsFalse(ModWorld.MapRing(136));
            CollectionAssert.Contains(ModWorld.BackdropTextures().ToList(), "mod:frontier_systems|textures/kepler_prime_map.png", "decoded with the other backdrops");
        }

        [Test]
        public void AnOriginalStationGetsAMapRing()
        {
            WriteMod("w", "t_world", "1.0", "", ("stations.json", "[ { 'override': 78, 'mapRing': true } ]"));
            ScanAndEnable("t_world");
            Database.Load();
            Assert.IsTrue(ModWorld.MapRing(78));
            Assert.IsFalse(ModWorld.MapRing(79));
        }

        [Test]
        public void AMissingMapImageIsAnError() =>
            StringAssert.Contains("the mapTexture textures/none.png isn't in the mod", WorldError("stations.json", "[ { 'override': 1, 'mapTexture': 'textures/none.png' } ]"));

        [Test]
        public void ASkyFollowsTheSun()
        {
            WriteMod("w", "t_world", "1.0", "", ("systems.json", "[ { 'override': 1, 'skybox': { 'sunAligned': true } }, { 'override': 2, 'skybox': { 'stars': 1 } } ]"));
            ScanAndEnable("t_world");
            Database.Load();
            Assert.IsTrue(ModWorld.SkyOf(1).sunAligned);
            Assert.IsFalse(ModWorld.SkyOf(2).sunAligned);
            // OrbitBuilder.SunAlignedSky: the sky's +Y toward the orbit's sun, whatever its direction (also along game x).
            foreach (var toSun in new[] { new Vector3(0.3f, 0.5f, -0.8f), Vector3.right, new Vector3(-1f, 0f, 0f), Vector3.up })
            {
                var layout = new GoF2Remake.World.OrbitLayout { lightDirection = toSun.normalized };
                var up = GoF2Remake.World.OrbitBuilder.SunAlignedSky(layout) * Vector3.up;
                var want = GoF2Remake.World.OrbitLayout.DirToUnity(toSun.normalized).normalized;
                Assert.Less(Vector3.Angle(up, want), 0.01f, toSun.ToString());
            }
        }

        // stations.json "orbit" over the seeded layout (OrbitLayout.ApplyMod) and "mapPlace" (GalaxyMap.SystemLayout).
        [Test]
        public void AnOrbitLaidOutByItsMod()
        {
            WriteMod("w", "t_world", "1.0", "", ("stations.json", "[ { 'override': 78, 'mapPlace': { 'angle': 90, 'distance': 30000 }, 'orbit': { " +
                "'station': 'nodock', 'jumpgate': true, 'jumpgatePosition': [1000, 2000, 3000], 'arrival': [0, 5000, -80000], " +
                "'asteroids': { 'count': 20, 'centre': [-40000, 0, 50000], 'spread': 50000, 'type': 'ice', 'ores': [154, { 'item': 155, 'chance': 3 }, 'nope:x'] }, " +
                "'fog': { 'color': [0.2, 0.4, 0.6], 'end': 70000 }, 'sun': [0, 30], 'planet': { 'size': 2, 'yaw': 45, 'pitch': 10 }, 'skyRotation': [10, 20, 30], " +
                "'light2': { 'color': [0.2, 0.3, 3], 'yaw': 90, 'pitch': 0 }, 'ambient': [0.1, 0.2, 0.3] } } ]"));
            ScanAndEnable("t_world");
            var db = Database.Load();
            var l = GoF2Remake.World.OrbitLayout.Build(db, 78);
            Assert.IsFalse(l.hasStation, "no docking");
            Assert.IsTrue(l.stationObject, "the model stays");
            Assert.IsTrue(l.hasJumpgate);
            Assert.AreEqual(new Vector3(1000, 2000, 3000), l.jumpgate);
            Assert.AreEqual(new Vector3(0, 5000, -80000), l.arrival.Value);
            Assert.AreEqual(20, l.asteroidCount);
            Assert.AreEqual(new Vector3(-40000, 0, 50000), l.asteroidCentre);
            Assert.AreEqual(50000f, l.asteroidSpread);
            Assert.AreEqual(2, l.asteroidType, "ice");
            CollectionAssert.AreEqual(new[] { (154, 1), (155, 3) }, l.modOres, "the unknown ore left out");
            StringAssert.Contains("ore \"nope:x\" is no item", AllMessages(Installed("t_world")));
            CollectionAssert.AreEqual(new[] { (154, 1), (155, 3) }, GoF2Remake.World.OrbitLayout.OreProbabilities(db, l));
            Assert.IsTrue(l.fog);
            Assert.AreEqual(0.4f, l.fogColor.g, 1e-6f);
            Assert.AreEqual(70000f, l.fogEnd);
            Assert.Greater(l.lightDirection.y, 0.49f, "the sun 30 degrees up");
            Assert.AreEqual(0f, l.lightDirection.x, 1e-5f);
            Assert.AreEqual(new Vector3(10, 20, 30), l.modSkyRotation.Value, "the orbit's own sky turn");
            Assert.IsFalse(GoF2Remake.World.OrbitLayout.Build(db, 79).modSkyRotation.HasValue, "another orbit keeps the system's");
            Assert.AreEqual(new Color(0.2f, 0.3f, 2f), l.planetLightColor, "the second light's colour, clamped to 2");
            Assert.Less(Vector3.Angle(GoF2Remake.World.OrbitBuilder.ToLight2(l), Vector3.left), 0.01f, "yaw 90: to a launching ship's right, game -x");
            Assert.AreEqual(new Color(0.1f, 0.2f, 0.3f, 1f), GoF2Remake.World.OrbitBuilder.SceneAmbient(l));
            Assert.AreEqual(Vector3.forward, GoF2Remake.World.OrbitBuilder.ToLight2(GoF2Remake.World.OrbitLayout.Build(db, 79)), "the game's: toward the orbit planet");
            var own = l.planets.Find(p => p.isOrbitPlanet);
            var placed = -GoF2Remake.World.OrbitLayout.Direction(own.pitch, own.yaw);   // where the backdrop puts it
            Assert.Less(Vector3.Angle(placed, GoF2Remake.World.OrbitLayout.ModDirection(45f, 10f)), 0.01f);
            Assert.Greater(placed.y, 0.17f, "10 degrees up");
            // Past 90 degrees of yaw "up" stays up (the game's own angles tip the pitch over there).
            var behind = GoF2Remake.World.OrbitLayout.ModDirection(120f, 20f);
            Assert.AreEqual(Mathf.Sin(20f * Mathf.Deg2Rad), behind.y, 1e-5f);
            Assert.Less(behind.x, 0f, "to a launching ship's right is game -x");
            Assert.Greater(behind.z, 0f, "past 90 degrees: ahead of it (game +z)");
            var map = GalaxyMap.SystemLayout(db, db.Systems.Find(s => s.index == 15)).Find(p => p.station == 78);
            Assert.AreEqual(16384, map.angle, "90 degrees of 65536");
            Assert.AreEqual(30000, map.radius);
        }

        [Test]
        public void AnOrbitWithoutStationOrAsteroids()
        {
            WriteMod("w", "t_world", "1.0", "", ("stations.json", "[ { 'override': 79, 'orbit': { 'station': false, 'jumpgate': false, 'asteroids': false, 'fog': false } } ]"));
            ScanAndEnable("t_world");
            var db = Database.Load();
            var l = GoF2Remake.World.OrbitLayout.Build(db, 79);
            Assert.IsFalse(l.hasStation);
            Assert.IsFalse(l.stationObject);
            Assert.IsFalse(l.hasJumpgate);
            Assert.AreEqual(0, l.asteroidCount);
            Assert.IsFalse(l.fog);
            Assert.IsNull(l.arrival);
            var plain = GoF2Remake.World.OrbitLayout.Build(db, 80);
            Assert.AreEqual(100000f, plain.asteroidSpread, "a station without one keeps the game's");
            Assert.IsNull(plain.modOres);
        }

        [Test]
        public void AStationsSubtitleAndLore()
        {
            WriteMod("w", "t_world", "1.0", "", ("systems.json",
                "[ { 'id': 'far', 'name': 'Far', 'race': 0, 'position': [1, 2, 3] } ]"), ("stations.json",
                "[ { 'id': 'post', 'system': 't_world:far', 'name': 'Post', 'subtitle': 'Trading Post', 'description': { 'en': 'An old post.', 'de': 'Ein alter Posten.' } }, " +
                "  { 'id': 'bare', 'system': 't_world:far', 'name': 'Bare' } ]"));
            ScanAndEnable("t_world");
            var db = Database.Load();
            Assert.IsTrue(GoF2Remake.Modding.ModWorld.TryResolveStation("t_world:post", out int post));
            Assert.IsTrue(GoF2Remake.Modding.ModWorld.TryResolveStation("t_world:bare", out int bare));
            Assert.AreEqual("Trading Post", GoF2Remake.Modding.ModWorld.Subtitle(post));
            Assert.AreEqual("An old post.", GoF2Remake.Modding.ModWorld.Description(post));
            Assert.IsNull(GoF2Remake.Modding.ModWorld.Description(bare));
            int system = db.Stations.Find(s => s.index == post).system;
            CollectionAssert.AreEqual(new[] { post }, GoF2Remake.Modding.ModWorld.StationsWithLore(db, system));
            // the ticker of that system runs the lore in place of the game's random items
            StringAssert.Contains("Post (Trading Post): An old post.", NewsTicker.Build(db, system, 0));
        }

        [Test]
        public void ASystemsOwnJumpgate()
        {
            WriteMod("w", "t_world", "1.0", "", ("systems.json",
                "[ { 'id': 'far', 'name': 'Far', 'race': 1, 'position': [1, 2, 3], 'jumpgate': { 'model': 'models/gate.glb', 'size': 20000 } }, " +
                "  { 'id': 'near', 'name': 'Near', 'race': 1, 'position': [4, 5, 6] } ]"), ("stations.json",
                "[ { 'id': 'post', 'system': 't_world:far', 'name': 'Post' }, { 'id': 'other', 'system': 't_world:near', 'name': 'Other' } ]"));
            ScanAndEnable("t_world");
            var db = Database.Load();
            Assert.IsTrue(GoF2Remake.Modding.ModWorld.TryResolveSystem("t_world:far", out int far));
            Assert.IsTrue(GoF2Remake.Modding.ModWorld.TryResolveSystem("t_world:near", out int near));
            var gate = GoF2Remake.Modding.ModWorld.GateModelOf(far);
            Assert.IsNotNull(gate);
            Assert.AreEqual($"modgate_{far:000}", gate.Assembly);
            Assert.AreEqual(10000f, gate.gateRadius, "half its size unless 'radius' is given");
            Assert.IsNotNull(db.Assemblies.Find(a => a.name == gate.Assembly), "registered for AssembledObject.LoadPrefab");
            Assert.IsNull(GoF2Remake.Modding.ModWorld.GateModelOf(near), "the race's gate");
            // Models() loads the tables again, which makes new model objects: compare by assembly
            CollectionAssert.Contains(GoF2Remake.Modding.ModWorld.Models().Select(m => m.Assembly).ToList(), gate.Assembly);
        }

        [Test] public void ABadJumpgate() => StringAssert.Contains("\"jumpgate\" must be", WorldError("systems.json", "[ { 'override': 1, 'jumpgate': 'gate.glb' } ]"));

        [Test] public void ABadOrbitStation() => StringAssert.Contains("orbit \"station\" is true, false", WorldError("stations.json", "[ { 'override': 1, 'orbit': { 'station': 'maybe' } } ]"));
        [Test] public void ABadAsteroidType() => StringAssert.Contains("asteroids \"type\" is rock, void, ice or magma", WorldError("stations.json", "[ { 'override': 1, 'orbit': { 'asteroids': { 'type': 'cheese' } } } ]"));
        [Test] public void ABadOrbitPosition() => StringAssert.Contains("\"arrival\" must be [x, y, z]", WorldError("stations.json", "[ { 'override': 1, 'orbit': { 'arrival': [1, 2] } } ]"));

        [Test]
        public void VolumesAreRead()
        {
            string dir = WriteMod("w", "t_world", "1.0", "", ("stations.json",
                "[ { 'override': 78, 'model': 'st.glb', 'modelSize': 500, 'collision': 'sphere', 'volumes': [ { 'box': [1, 2, 3, -4, 5, -6] }, { 'sphere': [7, 8, 9, -10] } ] } ]"));
            WriteFile(dir, "st.glb", Dummy);
            ScanAndEnable("t_world");
            Database.Load();
            var m = ModWorld.ModelOf(78);
            Assert.AreEqual(1000f, m.size, "at least 1000 game units");
            Assert.AreEqual("sphere", m.collision);
            Assert.AreEqual(2, m.volumes.Count);
            Assert.IsFalse(m.volumes[0].sphere);
            Assert.AreEqual(new Vector3(1, 2, 3), m.volumes[0].centre);
            Assert.AreEqual(new Vector3(4, 5, 6), m.volumes[0].half, "half sizes are positive");
            Assert.IsTrue(m.volumes[1].sphere);
            Assert.AreEqual(10f, m.volumes[1].radius);
        }

        // Remake (a modder's suggestions): a station's docking pads outside ("orbit.pads", SpacePoints.StationPadBase) and
        // stations held by pirates (systems.json "pirateHeld", a station's "orbit.pirateHeld" wins; Data.PirateHold).
        [Test]
        public void StationPadsAndPirateHeldStations()
        {
            WriteMod("w", "t_world", "1.0", "", ("systems.json", "[ { 'override': 15, 'pirateHeld': true } ]"), ("stations.json",
                "[ { 'override': 78, 'orbit': { 'pirateHeld': false, 'pads': [ { 'position': [100, 4000, -200] }, " +
                "{ 'position': [0, 0, 9000], 'approach': [0, 0, 12000], 'facing': [0, 0, -2] } ] } } ]"));
            ScanAndEnable("t_world");
            var db = Database.Load();
            var pads = ModWorld.StationPads(78);
            Assert.AreEqual(4, pads.Count, "an approach and a dock point per pad");
            Assert.AreEqual(SpacePoints.Approach, pads[0].type);
            Assert.AreEqual(new Vector3(100, 6000, -200), pads[0].engine, "2000 units above the pad by default");
            Assert.AreEqual(new Vector3(100, 4000, -200), pads[1].engine);
            Assert.AreEqual(Vector3.forward, pads[1].dir);
            Assert.AreEqual(new Vector3(0, 0, 12000), pads[2].engine);
            Assert.AreEqual(Vector3.back, pads[3].dir, "facing normalized");
            Assert.AreEqual(4, SpacePoints.Set(SpacePoints.StationPadBase + 78).Count);
            Assert.IsEmpty(ModWorld.StationPads(79));
            Assert.IsEmpty(SpacePoints.Set(SpacePoints.StationPadBase + 79));

            int other = db.Stations.First(s => s.system == 15 && s.index != 78).index;
            Assert.IsFalse(PirateHold.Marked(78, 15), "the station's own answer wins");
            Assert.IsTrue(PirateHold.Marked(other, 15), "the system's");
            Assert.IsFalse(PirateHold.Marked(70, db.Stations.Find(s => s.index == 70).system), "another system");
            Assert.IsFalse(PirateHold.Marked(KaamoClub.Station, 15), "never the Kaamo Club (its own siege)");
            Assert.IsTrue(PirateHold.Held(other, 15));
            PirateHold.Freed(other);
            Assert.IsFalse(PirateHold.Held(other, 15), "driven out for a while");
            Assert.IsTrue(PirateHold.Marked(other, 15));
        }

        // Remake (2026-11-03, EVERSPACE 2's wreck fields): a station's "orbit.wrecks" (World.WreckField).
        [Test]
        public void AnOrbitsWreckField()
        {
            WriteMod("w", "t_world", "1.0", "", ("stations.json",
                "[ { 'override': 78, 'orbit': { 'wrecks': { 'count': 20, 'centre': [1000, 0, 5000], 'spread': 30000 } } }, { 'override': 79, 'orbit': { 'wrecks': true } }, " +
                "{ 'override': 80, 'orbit': { 'wrecks': { 'count': 500 } } } ]"));
            ScanAndEnable("t_world");
            Database.Load();
            var a = ModWorld.OrbitOf(78);
            Assert.AreEqual(20, a.wreckCount);
            Assert.AreEqual(new Vector3(1000, 0, 5000), a.wreckCentre);
            Assert.AreEqual(30000f, a.wreckSpread);
            Assert.AreEqual(12, ModWorld.OrbitOf(79).wreckCount, "true: the default field");
            Assert.IsNull(ModWorld.OrbitOf(79).wreckCentre);
            Assert.AreEqual(60, ModWorld.OrbitOf(80).wreckCount, "at most 60");
            Assert.AreEqual(0, ModWorld.OrbitOf(81)?.wreckCount ?? 0, "none unless asked for");
            Assert.IsNotEmpty(GoF2Remake.World.WreckField.Loot(null), "an empty roll: Space Waste");
        }

        [Test]
        public void AnOrbitsHazards()
        {
            WriteMod("w", "t_world", "1.0", "", ("stations.json",
                "[ { 'override': 78, 'orbit': { 'hazards': [ { 'kind': 'heat', 'centre': [0, 0, 20000], 'radius': 8000, 'dps': 15 }, { 'kind': 'Radiation', 'centre': [5000, 0, 0] }, { 'kind': 'heat', 'centre': [0, 0, 0], 'raiderDps': 3.6 } ] } } ]"));
            ScanAndEnable("t_world");
            Database.Load();
            var h = ModWorld.OrbitOf(78).hazards;
            Assert.AreEqual(3, h.Count);
            Assert.AreEqual(3.6f, h[2].raiderDps, 1e-5f, "as raiders' gunfire");
            Assert.AreEqual(0f, h[0].raiderDps);
            Assert.AreEqual(ModWorld.HazardKind.Heat, h[0].kind);
            Assert.AreEqual(8000f, h[0].radius);
            Assert.AreEqual(15f, h[0].dps);
            Assert.AreEqual(ModWorld.HazardKind.Radiation, h[1].kind, "any case");
            Assert.AreEqual(20000f, h[1].radius, "the default radius");
        }

        [Test]
        public void AnOrbitsFrostCorrosionAndDamageFields()
        {
            WriteMod("w", "t_world", "1.0", "", ("stations.json",
                "[ { 'override': 78, 'orbit': { 'hazards': [ { 'kind': 'frost', 'centre': [0, 0, 20000] }, { 'kind': 'corrosion', 'centre': [5000, 0, 0] }, { 'kind': 'damage', 'centre': [0, 0, 0], 'raiderDps': 0.36 } ] } } ]"));
            ScanAndEnable("t_world");
            Database.Load();
            var h = ModWorld.OrbitOf(78).hazards;
            Assert.AreEqual(ModWorld.HazardKind.Frost, h[0].kind);
            Assert.AreEqual(ModWorld.HazardKind.Corrosion, h[1].kind);
            Assert.AreEqual(GoF2Remake.Flight.RiftRun.CorrosionTickSeconds, h[1].raiderDps, 1e-5f, "ES2's intensity 5 over the raider's 55 DPS by default");
            Assert.AreEqual(ModWorld.HazardKind.Damage, h[2].kind);
            Assert.AreEqual(0.36f, h[2].raiderDps, 1e-5f);
        }

        [Test]
        public void AnOrbitsAmbienceZones()
        {
            WriteMod("w", "t_world", "1.0", "", ("stations.json",
                "[ { 'override': 78, 'orbit': { 'ambience': [ { 'loops': ['outlaw_1', 'outlaw_2'], 'centre': [0, 0, 20000], 'radius': 10000, 'volume': 0.85 }, " +
                "  { 'loop': 'factory', 'adds': ['clank_1', 'clank_2'], 'addVolume': 0.1, 'addEveryMs': [4000, 10000], 'echo': 1 }, { 'adds': ['x'] } ] } } ]"));
            ScanAndEnable("t_world");
            Database.Load();
            var a = ModWorld.OrbitOf(78).ambience;
            Assert.AreEqual(2, a.Count, "a zone without loops left out");
            Assert.AreEqual(new[] { "outlaw_1", "outlaw_2" }, a[0].loops.ToArray());
            Assert.AreEqual(0.85f, a[0].volume, 1e-5f);
            Assert.AreEqual(new[] { "factory" }, a[1].loops.ToArray(), "\"loop\": one");
            Assert.IsNull(a[1].centre, "no centre: the whole orbit");
            Assert.AreEqual(4000f, a[1].addMinMs);
            Assert.AreEqual(10000f, a[1].addMaxMs);
            Assert.AreEqual(1f, GoF2Remake.World.OrbitAmbience.Weight(a[1], new Vector3(99999f, 0f, 0f)), "everywhere");
            Assert.AreEqual(1f, GoF2Remake.World.OrbitAmbience.Weight(a[0], new Vector3(0f, 0f, 25000f)), "within its radius");
            Assert.AreEqual(0.5f, GoF2Remake.World.OrbitAmbience.Weight(a[0], new Vector3(0f, 0f, 32500f)), 1e-4f, "halfway through the fade");
            Assert.AreEqual(0f, GoF2Remake.World.OrbitAmbience.Weight(a[0], new Vector3(0f, 0f, 40000f)), "past it");
            string messages = AllMessages(Installed("t_world"));
            StringAssert.Contains("unknown ambience field \"echo\"", messages);
            StringAssert.Contains("without \"loops\"", messages);
        }

        [Test]
        public void AnOrbitsGasClouds()
        {
            WriteMod("w", "t_world", "1.0", "", ("stations.json",
                "[ { 'override': 78, 'orbit': { 'gasClouds': { 'plasma': 202, 'clouds': [ [40000, 0, 60000], [-30000, 5000, 90000], [1, 2] ], 'colour': 'blue' } } }, " +
                "  { 'override': 79, 'orbit': { 'gasClouds': { 'clouds': [] } } } ]"));
            ScanAndEnable("t_world");
            Database.Load();
            var g = ModWorld.OrbitOf(78).gasClouds;
            Assert.AreEqual("202", g.plasma, "a number: the GoF2 plasma item");
            Assert.AreEqual(2, g.clouds.Count, "the malformed one left out");
            Assert.AreEqual(new Vector3(-30000f, 5000f, 90000f), g.clouds[1]);
            Assert.IsNull(ModWorld.OrbitOf(79)?.gasClouds, "no clouds: GoF2's own roll");
            string messages = AllMessages(Installed("t_world"));
            StringAssert.Contains("unknown gasClouds field \"colour\"", messages);
            StringAssert.Contains("a gas cloud is [x, y, z]", messages);
        }

        [Test]
        public void AnOrbitsOwnLocalsAndRaiders()
        {
            WriteMod("w", "t_world", "1.0", "", ("stations.json",
                "[ { 'override': 78, 'orbit': { 'locals': ['vossk', 2], 'raiders': ['pirates'] } }, { 'override': 79, 'orbit': { 'raiders': ['nobody'] } } ]"));
            ScanAndEnable("t_world");
            Database.Load();
            int system = Database.Shared.Stations.Find(s => s.index == 78).system;
            Assert.AreEqual(new[] { 1, 2 }, ModWorld.Locals(system, 78).ToArray(), "race words and numbers");
            Assert.AreEqual(new[] { 8 }, ModWorld.Raiders(system, 78).ToArray());
            Assert.IsNull(ModWorld.Locals(system, 77), "another orbit: its system's (none here)");
            Assert.IsNull(ModWorld.Raiders(Database.Shared.Stations.Find(s => s.index == 79).system, 79), "no race resolved: the system's");
        }

        [Test]
        public void AnOrbitsFixedContainerLoot()
        {
            WriteMod("w", "t_world", "1.0", "", ("stations.json",
                "[ { 'override': 78, 'orbit': { 'containers': { 'count': 3, 'loot': [ { 'item': 155, 'amount': 4 }, '201', { 'amount': 2 } ] } } } ]"));
            ScanAndEnable("t_world");
            Database.Load();
            var l = ModWorld.OrbitOf(78).containerLoot;
            Assert.AreEqual(2, l.Count, "an entry without an item left out");
            Assert.AreEqual(("155", 4), l[0]);
            Assert.AreEqual(("201", 1), l[1], "a bare reference: one");
            Assert.AreEqual("78:1", GoF2Remake.World.LootContainer.FixedLootKey(78, 1));
            StringAssert.Contains("a container loot entry is", AllMessages(Installed("t_world")));
        }

        [Test]
        public void AnOrbitsContainerModels()
        {
            string dir = WriteMod("w", "t_world", "1.0", "", ("stations.json",
                "[ { 'override': 78, 'orbit': { 'containers': { 'count': 2, 'model': 'box.glb', 'bulletproofModel': 'vault.glb' } } } ]"));
            WriteFile(dir, "box.glb", Dummy);
            ScanAndEnable("t_world");
            Database.Load();
            var o = ModWorld.OrbitOf(78);
            Assert.AreEqual("box.glb", o.containerModel);
            Assert.IsNull(o.containerProofModel, "not in the mod: the race's container");
            Assert.AreEqual("t_world", o.owner.Id);
            Assert.IsTrue(ModWorld.ContainerModels().Exists(m => m.mod == o.owner && m.path == "box.glb"), "loaded with the mod crates");
            StringAssert.Contains("\"bulletproofModel\" \"vault.glb\" isn't in the mod", AllMessages(Installed("t_world")));
        }

        [Test]
        public void AnOrbitsAsteroidModels()
        {
            string dir = WriteMod("w", "t_world", "1.0", "", ("stations.json",
                "[ { 'override': 78, 'orbit': { 'asteroids': { 'count': 40, 'models': ['rock_a.glb', 'rock_b.glb', 'gone.glb'], 'materials': [ { 'material': 'Rock', 'color': [0.5, 0.5, 0.5] } ] } } } ]"));
            WriteFile(dir, "rock_a.glb", Dummy);
            WriteFile(dir, "rock_b.glb", Dummy);
            ScanAndEnable("t_world");
            Database.Load();
            var o = ModWorld.OrbitOf(78);
            Assert.AreEqual(new[] { "rock_a.glb", "rock_b.glb" }, o.asteroidModels.ToArray(), "the missing one left out");
            Assert.AreEqual(1, o.asteroidMaterials.Count);
            Assert.AreEqual(2, ModWorld.ContainerModels().FindAll(m => m.path.StartsWith("rock_")).Count, "loaded with the mod crates");
            StringAssert.Contains("asteroids model \"gone.glb\" isn't in the mod", AllMessages(Installed("t_world")));
        }

        [Test]
        public void AnOrbitsWreckModels()
        {
            string dir = WriteMod("w", "t_world", "1.0", "", ("stations.json",
                "[ { 'override': 78, 'orbit': { 'wrecks': { 'count': 10, 'models': ['hull_a.glb', 'gone.glb'], 'hulks': 4, 'size': 12000, 'materials': [ { 'material': 'Hull', 'color': [0.5, 0.5, 0.5] } ] } } } ]"));
            WriteFile(dir, "hull_a.glb", Dummy);
            ScanAndEnable("t_world");
            Database.Load();
            var o = ModWorld.OrbitOf(78);
            Assert.AreEqual(new[] { "hull_a.glb" }, o.wreckModels.ToArray(), "the missing one left out");
            Assert.AreEqual(4, o.wreckHulks);
            Assert.AreEqual(12000f, o.wreckSize);
            Assert.AreEqual(1, o.wreckMaterials.Count);
            Assert.AreEqual(1, ModWorld.ContainerModels().FindAll(m => m.path == "hull_a.glb").Count, "loaded with the mod crates");
            StringAssert.Contains("wrecks model \"gone.glb\" isn't in the mod", AllMessages(Installed("t_world")));
            Assert.IsFalse(AllMessages(Installed("t_world")).Contains("unknown wrecks field"));
        }

        [Test]
        public void AnOrbitsStorm()
        {
            WriteMod("w", "t_world", "1.0", "", ("stations.json",
                "[ { 'override': 78, 'orbit': { 'storm': { 'frequency': 2, 'thunder': ['t1', 't2'], 'sparks': 1 } } }, { 'override': 77, 'orbit': { 'storm': true } } ]"));
            ScanAndEnable("t_world");
            Database.Load();
            var s = ModWorld.OrbitOf(78).storm;
            Assert.AreEqual(2f, s.frequency);
            Assert.AreEqual(new[] { "t1", "t2" }, s.thunder.ToArray());
            Assert.AreEqual(0.2f, s.thunderChance, 1e-5f, "ES2's ThunderSoundSpawnChance");
            Assert.AreEqual(80000f, ModWorld.OrbitOf(77).storm.radius, "ES2's 400 000 cm");
            StringAssert.Contains("unknown storm field \"sparks\"", AllMessages(Installed("t_world")));
        }

        [Test]
        public void AnOrbitsProps()
        {
            string dir = WriteMod("w", "t_world", "1.0", "", ("stations.json",
                "[ { 'override': 78, 'orbit': { 'props': [ { 'model': 'rock.glb', 'at': [1, 2, 3], 'rotation': [10, 20, 30], 'size': 5000, 'mass': 1 }, " +
                "{ 'model': 'gone.glb', 'at': [0, 0, 0] } ], 'propMaterials': [ { 'mesh': 'rock' } ] } } ]"));
            WriteFile(dir, "rock.glb", Dummy);
            ScanAndEnable("t_world");
            Database.Load();
            var o = ModWorld.OrbitOf(78);
            Assert.AreEqual(1, o.props.Count, "the missing model left out");
            Assert.AreEqual(new UnityEngine.Vector3(10, 20, 30), o.props[0].rotation);
            Assert.AreEqual(5000f, o.props[0].size);
            Assert.AreEqual(1, o.propMaterials.Count);
            Assert.AreEqual(1, ModWorld.ContainerModels().FindAll(m => m.path == "rock.glb").Count, "loaded with the mod crates");
            var msgs = AllMessages(Installed("t_world"));
            StringAssert.Contains("prop model \"gone.glb\" isn't in the mod", msgs);
            StringAssert.Contains("unknown prop field \"mass\"", msgs);
        }

        [Test] public void APropNeedsAModelAndAPlace() => StringAssert.Contains("a prop needs \"model\" and \"at\"", WorldError("stations.json", "[ { 'override': 1, 'orbit': { 'props': [ { 'model': 'x.glb' } ] } } ]"));

        [Test]
        public void AnOrbitsAcidPouches()
        {
            WriteMod("w", "t_world", "1.0", "", ("stations.json", "[ { 'override': 78, 'orbit': { 'acidSacks': [ [1, 2, 3], [4] ] } } ]"));
            ScanAndEnable("t_world");
            Database.Load();
            Assert.AreEqual(new[] { new UnityEngine.Vector3(1, 2, 3) }, ModWorld.OrbitOf(78).acidSacks.ToArray());
            StringAssert.Contains("an acid pouch's place is [x, y, z]", AllMessages(Installed("t_world")));
        }

        [Test]
        public void AnOrbitsMinefields()
        {
            WriteMod("w", "t_world", "1.0", "", ("stations.json",
                "[ { 'override': 78, 'orbit': { 'minefields': [ { 'kind': 'cryo', 'centre': [100, 0, 200], 'radius': 3000, 'count': 99, 'fuse': 1 }, " +
                "{ 'centre': [0, 0, 0], 'minRank': 0, 'share': 1, 'maxSecurity': 3, 'raiderSeconds': 1 } ] } } ]"));
            ScanAndEnable("t_world");
            Database.Load();
            var m = ModWorld.OrbitOf(78).minefields;
            Assert.AreEqual(2, m.Count);
            Assert.AreEqual(ModWorld.MinefieldSpec.Kind.Cryo, m[0].kind);
            Assert.AreEqual(new UnityEngine.Vector3(100, 0, 200), m[0].centre);
            Assert.AreEqual(40, m[0].count, "ES2's MaxMineAmount");
            Assert.AreEqual(4, m[0].minRank, "a danger: rank 4 or more by default");
            Assert.AreEqual(1, m[0].maxSecurity, "risky or dangerous systems by default");
            Assert.AreEqual(0.5f, m[0].share, 1e-5f);
            Assert.AreEqual(-1f, m[0].raiderSeconds, "by its kind");
            Assert.AreEqual(ModWorld.MinefieldSpec.Kind.Kinetic, m[1].kind);
            Assert.AreEqual(1f, m[1].raiderSeconds);
            StringAssert.Contains("unknown minefield field \"fuse\"", AllMessages(Installed("t_world")));
        }

        [Test]
        public void AnOrbitsCloakedContainers()
        {
            WriteMod("w", "t_world", "1.0", "", ("stations.json",
                "[ { 'override': 78, 'orbit': { 'containers': { 'count': 0, 'cloaked': [ [100, 0, -200], [1, 2], 'x', [5, 6, 7] ] } } } ]"));
            ScanAndEnable("t_world");
            Database.Load();
            var o = ModWorld.OrbitOf(78);
            Assert.AreEqual(0, o.containerCount);
            Assert.AreEqual(new[] { new UnityEngine.Vector3(100, 0, -200), new UnityEngine.Vector3(5, 6, 7) }, o.containerCloaked.ToArray());
            StringAssert.Contains("a cloaked container's place is [x, y, z]", AllMessages(Installed("t_world")));
        }

        [Test]
        public void AnOrbitsSecureContainers()
        {
            WriteMod("w", "t_world", "1.0", "", ("stations.json",
                "[ { 'override': 78, 'orbit': { 'containers': { 'count': 0, 'secure': [ { 'at': [1, 2, 3], 'triggers': [ [10, 0, 0], [0, 10, 0], [5] ], 'ordered': true, 'retrigger': 3, 'doors': 2 }, " +
                "{ 'at': [0, 0, 0], 'triggers': [] } ] } } } ]"));
            ScanAndEnable("t_world");
            Database.Load();
            var s = ModWorld.OrbitOf(78).containerSecure;
            Assert.AreEqual(1, s.Count, "one without triggers left out");
            Assert.AreEqual(new UnityEngine.Vector3(1, 2, 3), s[0].at);
            Assert.AreEqual(2, s[0].triggers.Count);
            Assert.IsTrue(s[0].ordered);
            Assert.AreEqual(3f, s[0].retriggerS);
            var msgs = AllMessages(Installed("t_world"));
            StringAssert.Contains("unknown secure container field \"doors\"", msgs);
            StringAssert.Contains("a shot trigger's place is [x, y, z]", msgs);
            StringAssert.Contains("a secure container without shot triggers", msgs);
        }

        [Test]
        public void ASecureContainersEnergySockets()
        {
            WriteMod("w", "t_world", "1.0", "", ("stations.json",
                "[ { 'override': 78, 'orbit': { 'containers': { 'count': 0, 'secure': [ { 'at': [0, 0, 0], 'sockets': [ { 'at': [10, 0, 0], 'orb': [20, 0, 0] }, " +
                "{ 'at': [0, 10, 0], 'coil': [0, 20, 0] }, { 'at': [1, 1, 1] }, { 'at': [2, 2, 2], 'orb': [3, 3, 3], 'coil': [4, 4, 4] } ] } ] } } } ]"));
            ScanAndEnable("t_world");
            Database.Load();
            var s = ModWorld.OrbitOf(78).containerSecure[0];
            Assert.AreEqual(0, s.triggers.Count, "sockets alone lock it");
            Assert.AreEqual(2, s.sockets.Count);
            Assert.IsFalse(s.sockets[0].coil);
            Assert.AreEqual(new UnityEngine.Vector3(20, 0, 0), s.sockets[0].source);
            Assert.IsTrue(s.sockets[1].coil);
            StringAssert.Contains("a socket is { \"at\"", AllMessages(Installed("t_world")));
        }

        [Test]
        public void ASecureContainersConductors()
        {
            WriteMod("w", "t_world", "1.0", "", ("stations.json",
                "[ { 'override': 78, 'orbit': { 'containers': { 'count': 0, 'secure': [ { 'at': [0, 0, 0], 'conductors': { 'sources': [ [0, 0, 100] ], 'portables': [ [0, 50, 0, 3000], [1] ], " +
                "'receivers': [ [0, 0, -100] ], 'pipes': 1 } }, { 'at': [5, 5, 5], 'conductors': { 'sources': [ [0, 0, 0] ] } } ] } } } ]"));
            ScanAndEnable("t_world");
            Database.Load();
            var s = ModWorld.OrbitOf(78).containerSecure;
            Assert.AreEqual(1, s.Count, "conductors without receivers lock nothing");
            Assert.AreEqual(1, s[0].conductors.portables.Count);
            Assert.AreEqual(4000f, s[0].conductors.radius, "ES2's 20 000 cm: 200 m");
            Assert.AreEqual(3000f, s[0].conductors.portables[0].w, "its own sphere");
            Assert.AreEqual(0f, s[0].conductors.sources[0].w, "the default");
            var msgs = AllMessages(Installed("t_world"));
            StringAssert.Contains("unknown conductors field \"pipes\"", msgs);
            StringAssert.Contains("conductors need \"sources\" and \"receivers\"", msgs);
        }

        [Test]
        public void AnOrbitsPuzzleSounds()
        {
            WriteMod("w", "t_world", "1.0", "", ("stations.json",
                "[ { 'override': 78, 'orbit': { 'containers': { 'count': 1, 'sounds': { 'grab': 'g', 'unlock': 'u', 'beep': 'b' } } } } ]"));
            ScanAndEnable("t_world");
            Database.Load();
            var s = ModWorld.OrbitOf(78).containerSounds;
            Assert.AreEqual("g", s["grab"]);
            Assert.AreEqual("u", s["Unlock"], "roles in any case");
            Assert.AreEqual(2, s.Count);
            StringAssert.Contains("unknown containers sound \"beep\"", AllMessages(Installed("t_world")));
        }

        [Test] public void ASecureContainerNeedsAPlace() => StringAssert.Contains("a secure container needs \"at\"", WorldError("stations.json", "[ { 'override': 1, 'orbit': { 'containers': { 'secure': [ { 'triggers': [ [0, 0, 0] ] } ] } } } ]"));

        [Test] public void CloakedContainersAreAList() => StringAssert.Contains("containers \"cloaked\" is a list", WorldError("stations.json", "[ { 'override': 1, 'orbit': { 'containers': { 'cloaked': true } } } ]"));

        [Test] public void AMinefieldNeedsACentre() => StringAssert.Contains("a minefield needs \"centre\"", WorldError("stations.json", "[ { 'override': 1, 'orbit': { 'minefields': [ { 'kind': 'web' } ] } } ]"));
        [Test] public void AMinefieldKindIsNotANumber() => StringAssert.Contains("a minefield's \"kind\" is", WorldError("stations.json", "[ { 'override': 1, 'orbit': { 'minefields': [ { 'kind': '2', 'centre': [0, 0, 0] } ] } } ]"));

        [Test] public void AHazardKindIsNotANumber() => StringAssert.Contains("a hazard's \"kind\" is", WorldError("stations.json", "[ { 'override': 1, 'orbit': { 'hazards': [ { 'kind': '1', 'centre': [0, 0, 0] } ] } } ]"));
        [Test] public void AHazardNeedsAKnownKind() => StringAssert.Contains("\"kind\" is \"heat\", \"radiation\", \"frost\", \"corrosion\" or \"damage\"", WorldError("stations.json", "[ { 'override': 1, 'orbit': { 'hazards': [ { 'kind': 'lava', 'centre': [0, 0, 0] } ] } } ]"));

        // Remake (2026-11-03, EVERSPACE 2's item containers): a station's "orbit.containers" (World.LootContainer).
        [Test]
        public void AnOrbitsContainers()
        {
            WriteMod("w", "t_world", "1.0", "", ("stations.json",
                "[ { 'override': 78, 'orbit': { 'containers': { 'count': 9, 'centre': [1000, 0, 5000], 'spread': 20000, 'bulletproof': 0.5 } } }, " +
                "{ 'override': 79, 'orbit': { 'containers': true } }, { 'override': 80, 'orbit': { 'containers': { 'count': 500, 'bulletproof': 3 } } } ]"));
            ScanAndEnable("t_world");
            Database.Load();
            var a = ModWorld.OrbitOf(78);
            Assert.AreEqual(9, a.containerCount);
            Assert.AreEqual(new Vector3(1000, 0, 5000), a.containerCentre);
            Assert.AreEqual(20000f, a.containerSpread);
            Assert.AreEqual(0.5f, a.containerBulletproof);
            Assert.AreEqual(6, ModWorld.OrbitOf(79).containerCount, "true: the default six");
            Assert.AreEqual(0.3f, ModWorld.OrbitOf(79).containerBulletproof, "a third of them bulletproof by default");
            Assert.AreEqual(40, ModWorld.OrbitOf(80).containerCount, "at most 40");
            Assert.AreEqual(1f, ModWorld.OrbitOf(80).containerBulletproof, "a share, at most all");
            Assert.AreEqual(0, ModWorld.OrbitOf(81)?.containerCount ?? 0, "none unless asked for");
            Assert.AreEqual(360f, GoF2Remake.World.LootContainer.OpenRangeM, 1e-3f, "ES2's 100 m interact radius x 3.6");
            Assert.AreEqual((1, 3), (GoF2Remake.World.LootContainer.RollsMin, GoF2Remake.World.LootContainer.RollsMax), "ES2's loot_types.CSV Container");
            Assert.AreEqual((2, 4), (GoF2Remake.World.LootContainer.BulletproofRollsMin, GoF2Remake.World.LootContainer.BulletproofRollsMax), "... RareContainer");
        }

        // Remake (2026-11-03, EVERSPACE 2's spline traffic): a station's "orbit.lanes" (World.TrafficLanes).
        [Test]
        public void AnOrbitsTrafficLanes()
        {
            WriteMod("w", "t_world", "1.0", "", ("stations.json",
                "[ { 'override': 78, 'orbit': { 'lanes': [ { 'points': [[0, 0, 0], [0, 0, 20000], [20000, 0, 20000]], 'ships': ['3', 'x:y'], 'count': 6, 'speed': 120, 'loop': false, 'twoLanes': false }, " +
                "{ 'points': [[0, 0, 0], [5000, 0, 0]], 'count': 99 } ] } } ]"));
            ScanAndEnable("t_world");
            Database.Load();
            var l = ModWorld.OrbitOf(78).lanes;
            Assert.AreEqual(2, l.Count);
            Assert.AreEqual(3, l[0].points.Count);
            Assert.AreEqual(new Vector3(20000, 0, 20000), l[0].points[2]);
            CollectionAssert.AreEqual(new[] { "3", "x:y" }, l[0].ships);
            Assert.AreEqual(6, l[0].count);
            Assert.AreEqual(120f, l[0].speed);
            Assert.IsFalse(l[0].loop);
            Assert.IsFalse(l[0].twoLanes);
            Assert.AreEqual(40, l[1].count, "at most 40 ships a lane");
            Assert.IsNull(l[1].ships, "the system race's fighters");
            Assert.AreEqual(80f, l[1].speed, "ES2's 8000 cm/s");
            Assert.IsTrue(l[1].loop && l[1].twoLanes, "ES2's ClosedLoop and TwoLanes");
        }

        // Remake (2026-11-04, EVERSPACE 2's worker drones): a station model's "life" (World.StationLife).
        [Test]
        public void AStationModelsLife()
        {
            WriteMod("w", "t_world", "1.0", "", ("stations.json",
                "[ { 'override': 78, 'model': 'st.glb', 'life': { 'loops': [ { 'points': [[1, 2, 3], [4, 5, 6], [7, 8, 9]], 'count': 3, 'speed': 12, 'loop': false, " +
                "'laneOffset': 2, 'spots': [[1, 1, 1, 0, 0, 2]] }, { 'points': [[0, 0, 0], [10, 0, 0]], 'count': 99 } ] } }, " +
                "{ 'override': 79, 'model': 'st.glb' } ]"), ("st.glb", "x"));
            ScanAndEnable("t_world");
            Database.Load();
            var life = ModWorld.ModelOf(78).life;
            Assert.AreEqual("life", life.scene, "the model's scene \"life\" by default");
            Assert.AreEqual(2, life.loops.Count);
            var a = life.loops[0];
            Assert.AreEqual(new Vector3(7, 8, 9), a.points[2]);
            Assert.AreEqual((3, 12f, 2f), (a.count, a.speed, a.laneOffset));
            Assert.IsFalse(a.loop);
            Assert.IsTrue(a.twoLanes);
            Assert.AreEqual(1, a.spots.Count);
            Assert.AreEqual(new Vector3(0, 0, 1), a.spots[0].normal, "the normal made a unit");
            var b = life.loops[1];
            Assert.AreEqual(ModWorld.MaxLifeDrones, b.count, "at most 40 drones a loop");
            Assert.AreEqual((20f, 5f), (b.speed, b.laneOffset), "ES2's BaseSpeed 2000 cm/s and LaneOffset 500 cm");
            Assert.IsTrue(b.loop && b.spots.Count == 0);
            Assert.IsNull(ModWorld.ModelOf(79).life, "none unless given");
            Assert.AreEqual(new Vector3(-1, 2, 3), GoF2Remake.World.StationLife.FromGltf(new Vector3(1, 2, 3)), "glTFast's frame mirrors x");
        }

        [Test] public void LifeNeedsTheStationsOwnModel() => StringAssert.Contains("\"life\" needs the station's own \"model\"", WorldError("stations.json", "[ { 'override': 1, 'life': { 'loops': [] } } ]"));
        [Test] public void AWeldSpotHasSixNumbers() => StringAssert.Contains("a weld spot is [x, y, z, nx, ny, nz]", WorldError("stations.json", "[ { 'override': 1, 'model': 'a.glb', 'life': { 'loops': [ { 'points': [[0, 0, 0], [1, 0, 0]], 'spots': [[0, 0, 0]] } ] } } ]"));

        // Remake (2026-11-04): an orbit's EVERSPACE 2 location level (its encounters' pay, CruiseEncounterSpawner.Es2Level).
        [Test]
        public void AnOrbitsLevel()
        {
            WriteMod("w", "t_world", "1.0", "", ("stations.json", "[ { 'override': 78, 'orbit': { 'level': 17 } }, { 'override': 79, 'orbit': { 'level': 99 } } ]"));
            ScanAndEnable("t_world");
            Database.Load();
            Assert.AreEqual(17, ModWorld.OrbitOf(78).level);
            Assert.AreEqual(30, ModWorld.OrbitOf(79).level, "ES2's MaxPlayerLevel at most");
            Assert.AreEqual(1500, GoF2Remake.World.CruiseEncounterSpawner.BattlePay(5), "800 + 4200 x 5 / 30, down to 50");
        }

        [Test] public void ALaneNeedsTwoPoints() => StringAssert.Contains("a lane needs \"points\"", WorldError("stations.json", "[ { 'override': 1, 'orbit': { 'lanes': [ { 'points': [[0, 0, 0]] } ] } } ]"));

        [Test]
        public void ALanesPathRunsThroughItsPoints()
        {
            var lane = new GoF2Remake.Flight.TrafficLane(new System.Collections.Generic.List<Vector3> { Vector3.zero, new Vector3(0, 0, 100) }, true);
            Assert.IsFalse(lane.Loop, "two points: an open path");
            Assert.AreEqual(100f, lane.Length, 1e-3f);
            Assert.AreEqual(95f, lane.Position(10f, 95f, out bool back), 1e-3f, "turns over at the end");
            Assert.IsTrue(back);
            var sq = new GoF2Remake.Flight.TrafficLane(new System.Collections.Generic.List<Vector3> { Vector3.zero, new Vector3(100, 0, 0), new Vector3(100, 0, 100), new Vector3(0, 0, 100) }, true);
            Assert.IsTrue(sq.Loop);
            sq.Sample(sq.Length, out var end, out _);
            Assert.AreEqual(0f, end.magnitude, 1e-3f, "a loop ends where it began");
        }

        [Test] public void ContainersAreAFieldOrAFlag() => StringAssert.Contains("\"containers\" is true, false or", WorldError("stations.json", "[ { 'override': 1, 'orbit': { 'containers': 3 } } ]"));

        [Test] public void WrecksAreAFieldOrAFlag() => StringAssert.Contains("\"wrecks\" is true, false or", WorldError("stations.json", "[ { 'override': 1, 'orbit': { 'wrecks': 3 } } ]"));

        [Test] public void PadsAreAList() => StringAssert.Contains("\"pads\" must be a list", WorldError("stations.json", "[ { 'override': 1, 'orbit': { 'pads': 3 } } ]"));
        [Test] public void APadNeedsAPosition() => StringAssert.Contains("a pad needs \"position\"", WorldError("stations.json", "[ { 'override': 1, 'orbit': { 'pads': [ { 'facing': [0, 0, 1] } ] } } ]"));
    }
}
