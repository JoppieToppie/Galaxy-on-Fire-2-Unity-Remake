// WorldDataTests.cs
// CLAUDE.md "Data" / "Star map and system travel" / "NPCs and combat" (Collisions): the universe tables. Every station
// lies in a system that lists it, every system's stations exist (at most 7 per system: CLAUDE.md "Mods", systems.json),
// the gate routes go both ways and a system without a jumpgate has none (Mido 15, Y'mirr 10, the add-on systems reached
// otherwise); the visible gate stands at one of the system's stations. Collision volumes (CollisionVolume, from
// PlayerStation::PlayerStation): collision.json by station index, no entry = the Vossk volumes 1000, 109 / 110 the static
// 2002, the alien orbit 1001 / 1003; static objects 1002 (pirate outposts), 2000-2006 (static_collisions.json); wrecks 0-5
// (wreck_collisions.json); the file format: a count, then per volume 1 = box (7 ints) or 0 = sphere (5 ints).

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class WorldDataTests : SandboxTest
    {
        [Test]
        public void EveryStationLiesInASystemThatListsIt()
        {
            for (int i = 0; i < 135; i++)
            {
                var st = Db.Stations[i];
                Assert.That(st.system, Is.InRange(0, 33), $"station {i}");
                var sy = Db.Systems[st.system];
                CollectionAssert.Contains(sy.stations, i, $"system {sy.name} lists station {i}");
                Assert.AreEqual(sy.name, st.systemName, $"station {i}'s system name");
                Assert.That(st.techLevel, Is.InRange(0, 10), $"station {i} tech level");
                Assert.IsFalse(string.IsNullOrWhiteSpace(st.name), $"station {i} name");
            }
        }

        [Test]
        public void EverySystemsStationsExist()
        {
            var seen = new HashSet<int>();
            for (int s = 0; s < 34; s++)
            {
                var sy = Db.Systems[s];
                Assert.IsNotNull(sy.stations, sy.name);
                Assert.That(sy.stations.Count, Is.InRange(1, 7), $"{sy.name}: stations");
                foreach (int st in sy.stations)
                {
                    Assert.That(st, Is.InRange(0, 134), sy.name);
                    Assert.AreEqual(s, Db.Stations[st].system, $"{sy.name}: station {st}");
                    Assert.IsTrue(seen.Add(st), $"station {st} in two systems");
                }
                Assert.AreEqual(sy.stations.Count, Db.StationsIn(s).Count(), $"{sy.name}: StationsIn");
            }
            Assert.AreEqual(135, seen.Count, "every station belongs to a system");
        }

        [Test]
        public void GateRoutesGoBothWays()
        {
            for (int s = 0; s < 34; s++)
            {
                var sy = Db.Systems[s];
                foreach (int to in sy.jumpRoutesTo)
                {
                    Assert.That(to, Is.InRange(0, 33), sy.name);
                    Assert.AreNotEqual(s, to, sy.name + " routes to itself");
                    CollectionAssert.Contains(Db.Systems[to].jumpRoutesTo, s, $"{sy.name} -> {Db.Systems[to].name} has no way back");
                }
            }
        }

        [Test]
        public void ASystemHasGateRoutesExactlyWhenItHasAJumpgate()
        {
            for (int s = 0; s < 34; s++)
            {
                var sy = Db.Systems[s];
                if (sy.jumpgateStation >= 0)
                {
                    CollectionAssert.Contains(sy.stations, sy.jumpgateStation, $"{sy.name}: the gate's station");
                    Assert.Greater(sy.jumpRoutesTo.Count, 0, $"{sy.name} has a gate but no route");
                }
                else Assert.AreEqual(0, sy.jumpRoutesTo.Count, $"{sy.name} has routes but no gate");
            }
            Assert.Less(Db.Systems[15].jumpgateStation, 0, "Mido (15) has no jumpgate (CLAUDE.md: gateless Mido)");
            Assert.AreEqual(108, Db.Systems[26].jumpgateStation, "Shima's gate at the Kaamo Club (108)");
            CollectionAssert.AreEquivalent(new[] { 11, 7, 26 }, Db.Systems[25].jumpRoutesTo, "Loma: gates to Aquila, Union and Shima");
        }

        [Test]
        public void SystemsHaveARaceAndSecurityLevel()
        {
            for (int s = 0; s < 34; s++)
            {
                var sy = Db.Systems[s];
                Assert.That(sy.raceId, Is.InRange(0, 9), sy.name);
                Assert.That(sy.securityLevel, Is.InRange(0, 3), sy.name + " (texts 402-405)");
                Assert.IsNotNull(sy.mapPosition, sy.name);
                Assert.That(sy.textureIndex, Is.InRange(0, 18), sy.name + ": nebula / reflection cubemap 0..18");
            }
        }

        [System.Serializable] class Entry { public int id; public int[] values; }
        [System.Serializable] class EntryList { public List<Entry> items; }

        static List<Entry> Volumes(string file) =>
            JsonUtility.FromJson<EntryList>("{\"items\":" + GameContent.Text("GoF2Data/" + file) + "}").items;

        [TestCase("collision")]
        [TestCase("static_collisions")]
        [TestCase("wreck_collisions")]
        [TestCase("v_collisions")]
        public void CollisionFilesFollowTheVolumeFormat(string file)
        {
            var list = Volumes(file);
            Assert.Greater(list.Count, 0, file);
            var ids = new HashSet<int>();
            foreach (var e in list)
            {
                Assert.IsTrue(ids.Add(e.id), $"{file}: id {e.id} twice");
                var v = e.values;
                Assert.IsNotNull(v);
                int count = v[0], i = 1;
                Assert.Greater(count, 0, $"{file} {e.id}: volumes");
                for (int n = 0; n < count; n++)
                {
                    Assert.Less(i, v.Length, $"{file} {e.id}: volume {n} missing");
                    if (v[i] == 1) i += 7;
                    else if (v[i] == 0) i += 5;
                    else Assert.Fail($"{file} {e.id}: volume type {v[i]}");
                }
                Assert.AreEqual(v.Length, i, $"{file} {e.id}: length");
            }
        }

        [Test]
        public void StationsHaveTheirOwnVolumesUnlessCollisionMdSaysOtherwise()
        {
            var own = new HashSet<int>(Volumes("collision").Select(e => e.id));
            // Fallbacks the original uses: the Vossk stations 1000, 109 / 110 the static 2002, the alien orbit 1001 / 1003.
            foreach (int id in new[] { 1000, 1001, 1002, 1003, 1005 }) Assert.IsTrue(own.Contains(id), "collision.json " + id);
            // The empty orbits (OrbitLayout.IsEmptyOrbit: 102-104, 109, 110, 132-134) need none of their own.
            var empty = new HashSet<int> { 102, 103, 104, 109, 110, 132, 133, 134 };
            for (int i = 0; i < 135; i++)
            {
                var race = Db.Systems[Db.Stations[i].system].raceId;
                if (race == 1 || empty.Contains(i)) continue;
                Assert.IsTrue(own.Contains(i), $"station {i} ({Db.Stations[i].name}) has no collision.json entry");
            }
        }

        [Test]
        public void EveryStationGetsCollisionVolumes()
        {
            for (int i = 0; i < 135; i++)
            {
                var list = CollisionVolume.ForStation(i, false);
                Assert.Greater(list.Count, 0, $"station {i}");
                foreach (var v in list)
                    if (v.sphere) Assert.Greater(v.radius, 0f, $"station {i}");
                    else Assert.IsTrue(v.half.x > 0f && v.half.y > 0f && v.half.z > 0f, $"station {i}: a flat box");
            }
            Assert.Greater(CollisionVolume.ForStation(0, true).Count, 0, "the Void station (1001)");
            Assert.Greater(CollisionVolume.ForStation(0, true, battlestation: true).Count, 0, "the battlestation (1003)");
        }

        [Test]
        public void StaticObjectsAndWrecksHaveVolumes()
        {
            // CLAUDE.md "Static-object volumes": pirate outposts 1002, the mining plant 2000, the plasma array 2001, 0x5279
            // 2002, the secure containers 2003, the carrier 2005, the Vossk battleship 2006; Kothar damaged 1005.
            foreach (int id in new[] { 1002, 1005, 2000, 2001, 2002, 2003, 2004, 2005, 2006 })
                Assert.Greater(CollisionVolume.ForStaticObject(id).Count, 0, "static object " + id);
            // Globals::getWreckCollision: 0 battleship, 1 Midorian, 2 Nivelian, 3 Terran, 4 Vossk, 5 the pirate outpost
            for (int id = 0; id <= 5; id++) Assert.Greater(CollisionVolume.ForWreckId(id).Count, 0, "wreck " + id);
        }

        [Test]
        public void VoidStationExtrasAreBoxes()
        {
            var extra = CollisionVolume.VoidStationExtras();
            Assert.Greater(extra.Count, 0, "void_station_extra.json (GoF2 > Build > Void Station Collision)");
            foreach (var v in extra)
            {
                Assert.IsFalse(v.sphere);
                Assert.IsTrue(v.half.x > 0f && v.half.y > 0f && v.half.z > 0f);
            }
        }
    }
}
