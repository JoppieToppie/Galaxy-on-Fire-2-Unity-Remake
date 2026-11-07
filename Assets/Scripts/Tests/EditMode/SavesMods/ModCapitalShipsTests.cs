// ModCapitalShipsTests.cs
// Feature: CLAUDE.md "Mods (remake-only)" > Capital ships and mobile stations (Modding.ModCapitalShips, Modding.MobileStations,
// World.MobileStation): ships.json "traffic": { "capital": "battleship" | "freighter" } (never a fighter), their collision
// boxes cut from the model along its longest axis; stations.json "mobile": a station with no planet (not one of its system's
// stations, so not on the map and not counted in the 7) that cruises through its system's orbits in turn, "stayMinutes" in
// each, on a course that passes sideways 6-8 km in front of the orbit's station.

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ModCapitalShipsTests : SavesModsTest
    {
        [TearDown] public void ClockBack() => MobileStations.ClockOverride = null;

        [Test]
        public void ACapitalShipsKindAndNoFighter()
        {
            string dir = WriteMod("c", "t_cap", "1.0", "", ("ships.json",
                "[ { 'id': 'big', 'name': 'Big', 'race': 0, 'model': 'big.glb', 'traffic': { 'capital': 'battleship' } }," +
                "  { 'id': 'hauler', 'name': 'Hauler', 'race': 1, 'model': 'big.glb', 'traffic': { 'capital': 'Freighter' } }," +
                "  { 'id': 'fighter', 'name': 'F', 'race': 0, 'model': 'big.glb' } ]"));
            WriteFile(dir, "big.glb", Dummy);
            ScanAndEnable("t_cap");
            Database.Load();
            int big = ModContent.ShipIndexOf("t_cap:big"), hauler = ModContent.ShipIndexOf("t_cap:hauler"), fighter = ModContent.ShipIndexOf("t_cap:fighter");
            Assert.AreEqual(ModCapitalShips.Battleship, ModCapitalShips.KindOf(big));
            Assert.AreEqual(ModCapitalShips.Freighter, ModCapitalShips.KindOf(hauler), "any case");
            Assert.IsNull(ModCapitalShips.KindOf(fighter));
            Assert.IsNull(ModCapitalShips.KindOf(14), "the game's battleship isn't a mod's");
            var effects = CustomShips.Get(big).es2Engine;
            Assert.IsTrue(effects == null || (effects.glow && effects.trails), "no \"es2Engine\": every engine effect");
            // A parked battleship's engines idle (EngineDrive.Idle by its "ship_NNN_mod" model), a freighter's follow its speed
            Assert.AreEqual(big, ModShips.ShipOf($"ship_{big:000}_mod"));
            Assert.AreEqual(hauler, ModShips.ShipOf($"ship_{hauler:000}_mod#body=b"), "a kit build's variant too");
            Assert.AreEqual(-1, ModShips.ShipOf("station_070_terran"));
            Assert.Greater(ModCapitalShips.ParkedDrive, 0f);
            var spec = ModCapitalShips.Spawn(big, 0, new Vector3(0, 0, 100000));
            Assert.IsTrue(spec.freighter && spec.stationary && spec.modCapital, "a battleship parks on the freighters' path");
            Assert.AreEqual(NpcTables.Hull(1, 14), spec.hitpoints, "the Terran battleship's hull");
            Assert.AreEqual("Big", spec.name);
            var cruiser = ModCapitalShips.Spawn(hauler, 1, Vector3.zero);
            Assert.IsTrue(cruiser.freighter && !cruiser.stationary && cruiser.modCapital, "a freighter cruises");
            Assert.AreEqual(NpcGroup.Freighter, cruiser.group);
            // Not built (a dummy model): no boxes of its own, the race's freighter boxes stand in.
            Assert.IsNull(ModCapitalShips.HullVolumes(big, 0));
            Assert.AreEqual(CollisionVolume.ForFreighter(13, 1).Count, CollisionVolume.ForFreighter(hauler, 1).Count);
        }

        // Remake (2026-11-04, EVERSPACE 2's loot_npcs.csv): a ship's own drops, "traffic": { "loot", "lootDrops" } (NpcShip.AddOwnLoot).
        [Test]
        public void ATrafficHullsOwnDevices()
        {
            string dir = WriteMod("c", "t_kam", "1.0", "", ("ships.json",
                "[ { 'id': 'kamikaze', 'name': 'K', 'race': 0, 'model': 'big.glb', 'traffic': { 'role': 'detonator', 'devices': [ " +
                "    { 'type': 'mineLayer', 'bombEveryMs': 4000, 'range': [3420, 28500] }, { 'type': 'mineLayer', 'layBehind': -114 }, " +
                "    { 'type': 'frontShield', 'blockPercent': 67, 'glow': 1 }, " +
                "    { 'type': 'cloud', 'slowPercent': 0, 'shieldPercent': 70, 'minRank': 8, 'maxRank': 15, 'share': 40 } ] } }," +
                "  { 'id': 'plain', 'name': 'P', 'race': 0, 'model': 'big.glb', 'traffic': { 'role': 'sniper', " +
                "    'secondaries': [ { 'item': '31', 'minRank': 6, 'maxRank': 17, 'share': 25 }, { 'item': '33', 'minRank': 18 } ] } } ]"));
            WriteFile(dir, "big.glb", Dummy);
            ScanAndEnable("t_kam");
            Database.Load();
            var t = CustomShips.Get(ModContent.ShipIndexOf("t_kam:kamikaze")).traffic;
            Assert.AreEqual("detonator", t.role, "the rest of the traffic block stays");
            Assert.AreEqual(4, t.devices.Count);
            Assert.AreEqual(0f, t.devices[3].slowShare, "a cloud that doesn't slow");
            Assert.AreEqual(70f, t.devices[3].shieldPercent);
            Assert.AreEqual(8, t.devices[3].minRank);
            Assert.AreEqual(15, t.devices[3].maxRank);
            Assert.AreEqual(0.4f, t.devices[3].share, 1e-4f);
            Assert.AreEqual(Flight.NpcDeviceSpec.Kind.MineLayer, t.devices[0].kind);
            Assert.AreEqual(4000f, t.devices[0].bombEveryMs);
            Assert.AreEqual(28500f, t.devices[0].rangeMax);
            Assert.AreEqual(-114f, t.devices[1].layBehind, "below 0: thrown ahead");
            Assert.AreEqual(0.67f, t.devices[2].blockShare, 1e-4f);
            var plain = CustomShips.Get(ModContent.ShipIndexOf("t_kam:plain")).traffic;
            Assert.IsNull(plain.devices);
            Assert.AreEqual(2, plain.secondaries.Length);
            Assert.AreEqual("31", plain.secondaries[0].item);
            Assert.IsFalse(plain.secondaries[0].Carried(5, 0f), "below its ranks");
            Assert.IsTrue(plain.secondaries[0].Carried(6, 0.2f));
            Assert.IsFalse(plain.secondaries[0].Carried(6, 0.3f), "outside its quarter");
            Assert.IsTrue(plain.secondaries[1].Carried(20, 0.99f), "100 % by default");
            Assert.AreEqual(1f, plain.secondaries[1].factor);
            StringAssert.Contains("unknown device field \"glow\"", AllMessages(Installed("t_kam")));
            StringAssert.Contains("ships.json", AllMessages(Installed("t_kam")), "the warning names the ships file");
        }

        [Test]
        public void AShipsOwnDrops()
        {
            string dir = WriteMod("c", "t_loot", "1.0", "", ("ships.json",
                "[ { 'id': 'okkar', 'name': 'O', 'race': 1, 'model': 'big.glb', 'traffic': { 'lootDrops': 2, " +
                "    'loot': [ { 'item': '155', 'chance': 0.2, 'min': 1, 'max': 2 }, { 'item': 't_loot:none', 'chance': 1 } ] } }," +
                "  { 'id': 'plain', 'name': 'P', 'race': 1, 'model': 'big.glb' } ]"));
            WriteFile(dir, "big.glb", Dummy);
            ScanAndEnable("t_loot");
            Database.Load();
            var t = CustomShips.Get(ModContent.ShipIndexOf("t_loot:okkar")).traffic;
            Assert.AreEqual(2, t.lootDrops);
            Assert.AreEqual(2, t.loot.Length);
            Assert.AreEqual(("155", 0.2f, 1, 2), (t.loot[0].item, t.loot[0].chance, t.loot[0].min, t.loot[0].max));
            Assert.AreEqual(1f, t.loot[1].chance);
            Assert.IsNull(CustomShips.Get(ModContent.ShipIndexOf("t_loot:plain")).traffic?.loot is { Length: > 0 } ? "has" : null, "none unless given");
        }

        [Test]
        public void ACapitalShipsSubsystemsAndShields()
        {
            string dir = WriteMod("c", "t_sub", "1.0", "", ("ships.json",
                "[ { 'id': 'fort', 'name': 'Fort', 'race': 8, 'model': 'big.glb', 'traffic': { 'capital': 'battleship', 'weight': 0 }," +
                "    'shieldMaterial': 'energyfield|', 'shieldedDamage': 0.1, 'hullFactor': 3, 'engineStyle': 'es2', 'es2Engine': { 'glow': false }," +
                "    'subsystems': [" +
                "      { 'kind': 'turret', 'name': 'Gun', 'position_engine': [100, 200, 300], 'item': 48, 'stage': 2, 'turretUp': [1, 0, 0] }," +
                "      { 'kind': 'object', 'name': 'Emitter', 'position_engine': [0, 0, 0], 'shields': true, 'stage': 1, 'hull': 900, 'model': 'gof2:v_station_battlestation_shield' }," +
                "      { 'kind': 'object', 'name': 'Weak', 'position_engine': [1, 2, 3], 'upsideDown': true, 'weakspot': 2.5, 'bonus': 0.08 }," +
                "      { 'kind': 'hangar', 'model': 'none', 'position_engine': [5, 0, 0], 'ships': [ 2, 't_sub:fort' ], 'every': 10, 'perWave': 3, 'maxAlive': 5, 'total': 9 }," +
                "      { 'kind': 'turret', 'position_engine': [0, 0, 0] }," +
                "      { 'kind': 'turret', 'position_engine': [0, 0, 0], 'item': 0 }," +
                "      { 'kind': 'laser', 'position_engine': [0, 0, 0] }," +
                "      { 'kind': 'hangar', 'position_engine': [0, 0, 0] } ] } ]"));
            WriteFile(dir, "big.glb", Dummy);
            ScanAndEnable("t_sub");
            var db = Database.Load();
            int fort = ModContent.ShipIndexOf("t_sub:fort");
            var subs = db.ShipSubsystems[fort];
            Assert.AreEqual(4, subs.Count, "the four good ones; a turret without a turret item, a non-turret item, an unknown kind and a hangar without ships are left out");
            Assert.AreEqual(4, ModManager.Find("t_sub").Warnings.Count(w => w.Contains("subsystem") || w.Contains("\"item\"") || w.Contains("\"kind\"")));
            Assert.AreEqual("turret", subs[0].kind); Assert.AreEqual(48, subs[0].item); Assert.AreEqual(2, subs[0].stage);
            Assert.AreEqual(new Vector3(100, 200, 300), subs[0].position); Assert.AreEqual(Vector3.right, subs[0].up);
            Assert.IsTrue(subs[1].shields); Assert.AreEqual(900, subs[1].hull); Assert.AreEqual("gof2:v_station_battlestation_shield", subs[1].model);
            Assert.AreEqual(Vector3.down, subs[2].up, "upsideDown");
            Assert.AreEqual(2.5f, subs[2].weakspot); Assert.AreEqual(0.08f, subs[2].bonus, 1e-5f);
            Assert.AreEqual(new[] { "2", "t_sub:fort" }, subs[3].ships.ToArray());
            Assert.AreEqual(10000f, subs[3].everyMs); Assert.AreEqual(3, subs[3].perWave); Assert.AreEqual(5, subs[3].maxAlive); Assert.AreEqual(9, subs[3].total);
            var c = CustomShips.Get(fort);
            Assert.AreEqual("energyfield|", c.shieldMaterial); Assert.AreEqual(0.1f, c.shieldedDamage, 1e-5f); Assert.AreEqual("es2", c.engineStyle);
            Assert.IsFalse(c.es2Engine.glow, "EVERSPACE 2's SpawnEngineGlow off"); Assert.IsTrue(c.es2Engine.trails, "left out: on");
            Assert.AreEqual(Mathf.RoundToInt(NpcTables.Hull(1, 14) * 3f), ModCapitalShips.Spawn(fort, 8, Vector3.zero).hitpoints, "hullFactor");
            Assert.IsEmpty(ModCapitalShips.Candidates(8, ModCapitalShips.Battleship), "weight 0: never in the traffic (the encounter brings it)");
        }

        [Test]
        public void ShieldsAndWeakSpotsOnTheTargets()
        {
            var hullGo = new GameObject("hull"); var spotGo = new GameObject("spot");
            try
            {
                var hull = hullGo.AddComponent<Target>(); hull.hitpoints = new Hitpoints(1000); hull.hp = 1000;
                var spot = spotGo.AddComponent<Target>(); spot.hitpoints = new Hitpoints(100); spot.hp = 100;
                hull.damageFactor = 0f;
                hull.Damage(500f);
                Assert.AreEqual(1000, hull.hitpoints.hull, "shielded: no damage");
                spot.forwardTo = hull; spot.forwardFactor = 2f;
                hull.damageFactor = 1f;
                spot.Damage(40f);
                Assert.AreEqual(60, spot.hitpoints.hull);
                Assert.AreEqual(920, hull.hitpoints.hull, "the weak spot's hit x 2 on the hull");
                spot.Damage(9999f);
                Assert.IsFalse(spot.Alive);
                Assert.AreEqual(800, hull.hitpoints.hull, "only what the weak spot had left (60) x 2, not the overkill");
            }
            finally { Object.DestroyImmediate(hullGo); Object.DestroyImmediate(spotGo); }
        }

        [Test]
        public void ATurretDoesntFireIntoItsOwnHullsVolumes()
        {
            var go = new GameObject("host");
            try
            {
                var o = go.AddComponent<Obstacle>();
                o.volumes = new System.Collections.Generic.List<CollisionVolume> { CollisionVolume.Box(Vector3.zero, new Vector3(20f, 10f, 100f)) };
                var top = new Vector3(0f, 9.5f, 0f);   // a turret on the hull's back, inside its box
                Assert.IsTrue(o.RayInto(top, Vector3.down), "down into its own ship");
                Assert.IsFalse(o.RayInto(top, Vector3.up), "up and away");
                Assert.IsFalse(o.RayInto(top, Vector3.forward), "along the back");
                Assert.IsFalse(o.RayInto(top, (Vector3.forward + Vector3.up * 0.3f).normalized));
                Assert.IsTrue(o.RayInto(top, (Vector3.forward - Vector3.up).normalized), "down at a slant");
                o.volumes.Add(CollisionVolume.Box(new Vector3(0f, 40f, 0f), new Vector3(5f, 5f, 5f)));   // a tower over it
                Assert.IsTrue(o.RayInto(top, Vector3.up), "into the tower above");
                Assert.IsFalse(o.RayInto(new Vector3(0f, 200f, 0f), Vector3.up), "outside, away from it all");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheHullIsCutIntoBoxesAlongItsLength()
        {
            var root = new GameObject("hull");
            try
            {
                for (int i = 0; i < 8; i++)   // a 400 m hull in 50 m sections (vertices along its length)
                {
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.transform.SetParent(root.transform, false);
                    cube.transform.localScale = new Vector3(20f, 10f, 50f);
                    cube.transform.localPosition = new Vector3(0f, 0f, -175f + 50f * i);
                }
                var glow = GameObject.CreatePrimitive(PrimitiveType.Cube);
                glow.name = "engine_glow";
                glow.transform.SetParent(root.transform, false);
                glow.transform.localPosition = new Vector3(0f, 0f, -1000f);   // left out: not hull
                var boxes = ModCapitalShips.Measure(root, Quaternion.identity);
                Assert.AreEqual(8, boxes.Count);
                float lo = boxes.Min(b => b.centre.z - b.half.z), hi = boxes.Max(b => b.centre.z + b.half.z);
                Assert.AreEqual(-200f, lo, 0.5f);
                Assert.AreEqual(200f, hi, 0.5f);
                foreach (var b in boxes) { Assert.AreEqual(10f, b.half.x, 0.01f); Assert.AreEqual(5f, b.half.y, 0.01f); }
                // An NPC's root faces game +Z (Euler 0, 180, 0): the boxes turn with it.
                var turned = ModCapitalShips.Measure(root, Quaternion.Euler(0f, 180f, 0f));
                Assert.AreEqual(-boxes[0].centre.z, turned[0].centre.z, 0.01f);
            }
            finally { Object.DestroyImmediate(root); }
        }

        string MobileMod(string mobile = "{ 'stayMinutes': 10, 'speed': 0.2 }") =>
            WriteMod("m", "t_mob", "1.0", "", ("stations.json",
                "[ { 'id': 'mother', 'system': 14, 'name': 'Mother', 'techLevel': 9, 'mobile': " + mobile + " } ]"));

        [Test]
        public void AMobileStationHasNoPlanet()
        {
            MobileMod();
            ScanAndEnable("t_mob");
            var db = Database.Load();
            int mother = ModWorld.StationIndexOf("t_mob:mother");
            Assert.IsTrue(ModWorld.IsMobile(mother));
            Assert.AreEqual(14, db.Stations.Find(s => s.index == mother).system);
            CollectionAssert.DoesNotContain(db.Systems.Find(s => s.index == 14).stations, mother, "no planet: not one of the system's");
            var spec = ModWorld.MobileOf(mother);
            CollectionAssert.AreEqual(new[] { 70, 71, 72, 73, 74 }, spec.route, "every station of its system by default");
            Assert.AreEqual(10f, spec.stayMinutes);
            Assert.AreEqual(0.2f, spec.speed, 1e-5f);
            Assert.IsFalse(ModWorld.IsMobile(70));
        }

        [Test]
        public void ItCruisesFromOrbitToOrbit()
        {
            MobileMod("{ 'route': [72, 70, '74'], 'stayMinutes': 10 }");
            ScanAndEnable("t_mob");
            Database.Load();
            int mother = ModWorld.StationIndexOf("t_mob:mother");
            var route = ModWorld.MobileOf(mother).route;
            CollectionAssert.AreEqual(new[] { 72, 70, 74 }, route, "in order");
            var seen = new System.Collections.Generic.List<int>();
            for (int stay = 0; stay < 6; stay++) seen.Add(MobileStations.HostAt(mother, stay * 600.0 + 1.0));
            for (int i = 1; i < 6; i++) Assert.AreEqual(route[(route.IndexOf(seen[0]) + i) % 3], seen[i], "the next stop each stay");
            Assert.AreEqual(seen[0], MobileStations.HostAt(mother, 599.0), "the same stop through its stay");
            MobileStations.ClockOverride = () => 1.0;
            CollectionAssert.AreEqual(new[] { mother }, MobileStations.AtOrbit(seen[0], 1.0));
            Assert.AreEqual(0, MobileStations.AtOrbit(73, 1.0).Count, "not on its route");
            Assert.AreEqual(seen[0], MobileStations.HostNow(mother));
        }

        [Test]
        public void ItRoamsOtherSystemsAsItsOwnRace()
        {
            // Home Magnetar (14), a route through Union (55 Kappa) and V'ikka (15), a Terran ship wherever it is.
            MobileMod("{ 'route': [70, 55, 15], 'stayMinutes': 10, 'race': 'terran' }");
            ScanAndEnable("t_mob");
            int mother = -1;
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int stay = 0; stay < 3; stay++)
            {
                double at = stay * 600.0 + 1.0;
                MobileStations.ClockOverride = () => at;
                var db = Database.Load();
                mother = ModWorld.StationIndexOf("t_mob:mother");
                int host = MobileStations.HostAt(mother, at);
                var st = db.Stations.Find(s => s.index == mother);
                Assert.AreEqual(db.Stations.Find(s => s.index == host).system, st.system, "the system of the orbit it is in");
                Assert.AreEqual(db.Systems.Find(s => s.index == st.system).name, st.systemName);
                Assert.AreEqual(0, ModWorld.MobileOf(mother).race);
                Assert.AreEqual(0, World.OrbitLayout.Build(db, mother).raceId, "its own race, in V'ikka too");
                seen.Add(st.system);
            }
            CollectionAssert.AreEquivalent(new[] { 14, 11, 3 }, seen);
            CollectionAssert.DoesNotContain(Database.Load().Systems.Find(s => s.index == 3).stations, mother, "still no planet");
        }

        [Test]
        public void ItsCoursePassesSidewaysClearOfTheStation()
        {
            MobileMod();
            ScanAndEnable("t_mob");
            Database.Load();
            int mother = ModWorld.StationIndexOf("t_mob:mother");
            for (int stay = 0; stay < 200; stay++)
            {
                double start = stay * 600.0;
                MobileStations.CourseAt(mother, start + 300.0, out var middle, out var heading);
                Assert.AreEqual(0f, heading.y, 1e-5f, "level");
                Assert.AreEqual(1f, heading.magnitude, 1e-4f);
                Assert.Greater(middle.z, 0f, "in front of the station");
                float flat = new Vector2(middle.x, middle.z).magnitude;
                Assert.That(flat, Is.InRange(119999f, 160001f), "6-8 km out");
                Assert.AreEqual(0f, Vector3.Dot(new Vector3(middle.x, 0f, middle.z).normalized, heading), 1e-3f, "crossed sideways");
                MobileStations.CourseAt(mother, start + 1.0, out var first, out _);
                MobileStations.CourseAt(mother, start + 599.0, out var last, out _);
                Assert.AreEqual(0.2f * 598000f, (last - first).magnitude, 50f, "at its speed");
            }
        }

        [Test] public void OnlyANewStationIsMobile() =>
            StringAssert.Contains("only a new station", OpenBroken("[ { 'override': 70, 'mobile': true } ]"));
        [Test]
        public void ItRoamsEverySystemAWhileInEach()
        {
            MobileMod("{ 'route': 'any', 'stayMinutes': 20, 'systemMinutes': 60, 'exclude': [15], " +
                      "'engines': [ { 'position_engine': [0, 0, -9000], 'glowSize': [500, 500, 1500], 'fxScale': 12 } ], 'engineColor': [1, 0.6, 0.2], " +
                      "'engineFx': { 'meshes': 'fx/engines/large.json', 'meshScale': 53.5 } }");
            ScanAndEnable("t_mob");
            var db = Database.Load();
            int mother = ModWorld.StationIndexOf("t_mob:mother");
            var spec = ModWorld.MobileOf(mother);
            Assert.IsTrue(spec.roam);
            Assert.AreEqual(1, spec.engines.Count);
            Assert.AreEqual(3, spec.engines[0].slotType);
            Assert.AreEqual(12f, spec.engines[0].fxScale, "each engine sized by its own fxScale");
            Assert.IsNotNull(spec.engineFx, "GoF3's engine effect instead of the discs");
            Assert.AreEqual("fx/engines/large.json", spec.engineFx.meshes);
            Assert.AreEqual(53.5f, spec.engineFx.meshScale, 1e-4f);
            Assert.AreEqual(1f, spec.engineFx.meshGlow, "the rest at the effect's defaults");
            int SystemOf(int st) => db.Stations.Find(s => s.index == st).system;
            var systems = new System.Collections.Generic.HashSet<int>();
            int last = -1;
            for (long visit = 0; visit < 300; visit++)
            {
                int sys = -1;
                for (int stay = 0; stay < 3; stay++)
                {
                    double at = (visit * 3 + stay) * 1200.0 + 5.0;
                    int host = MobileStations.HostAt(mother, at);
                    CollectionAssert.DoesNotContain(MobileStations.RoamSkip, host, "never a story or Kaamo orbit");
                    if (stay == 0) sys = SystemOf(host);
                    else Assert.AreEqual(sys, SystemOf(host), "an hour in one system, its orbits in turn");
                    Assert.AreEqual(3600.0 - stay * 1200.0 - 5.0, MobileStations.LeavesSystemIn(mother, at), 0.01);
                }
                Assert.AreNotEqual(15, sys, "excluded");
                Assert.AreNotEqual(last, sys, "never the same system twice in a row");
                last = sys;
                systems.Add(sys);
            }
            Assert.Greater(systems.Count, 20, "every system in turn");
        }

        [Test] public void ARouteIsAListOrAny() =>
            StringAssert.Contains("\"route\" must be", OpenBroken("[ { 'id': 'x', 'name': 'X', 'system': 14, 'mobile': { 'route': 'everywhere' } } ]"));
        [Test] public void ARaceIsARace() =>
            StringAssert.Contains("\"race\" must be", OpenBroken("[ { 'id': 'x', 'name': 'X', 'system': 14, 'mobile': { 'race': 'klingon' } } ]"));
        [Test] public void MobileIsTrueOrAnObject() =>
            StringAssert.Contains("\"mobile\" must be true or", OpenBroken("[ { 'id': 'x', 'name': 'X', 'system': 14, 'mobile': 5 } ]"));

        string OpenBroken(string stations)
        {
            string dir = WriteMod("e", "t_err", "1.0", "", ("stations.json", stations));
            var m = OpenParsed(dir);
            Assert.IsTrue(m.Broken, "accepted: " + stations);
            return m.Errors[0];
        }
    }
}
