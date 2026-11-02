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
                "'fog': { 'color': [0.2, 0.4, 0.6], 'end': 70000 }, 'sun': [0, 30], 'planet': { 'size': 2, 'yaw': 45, 'pitch': 10 } } } ]"));
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

        [Test] public void PadsAreAList() => StringAssert.Contains("\"pads\" must be a list", WorldError("stations.json", "[ { 'override': 1, 'orbit': { 'pads': 3 } } ]"));
        [Test] public void APadNeedsAPosition() => StringAssert.Contains("a pad needs \"position\"", WorldError("stations.json", "[ { 'override': 1, 'orbit': { 'pads': [ { 'facing': [0, 0, 1] } ] } } ]"));
    }
}
