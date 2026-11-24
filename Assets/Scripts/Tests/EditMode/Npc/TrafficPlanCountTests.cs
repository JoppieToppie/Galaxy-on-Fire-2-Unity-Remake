// TrafficPlanCountTests.cs
// Feature: CLAUDE.md "NPCs and combat" > Traffic (Level::createMission's free-flight counts): local fighters by security,
// jumpers, freighters, raiders 90 / 65 / 35 / 10 %, pirates 75 %, one model per raider group, Var Hastra, the special
// orbits (102-104, 100 / 101 / 108 / 10, Loma, systems 32 / 33), Extreme, the big battle, attacked stations, the Mido rule,
// the supernova system, the pirate ambush on arrival; TrafficPlan.Build's positions.
// Expected values: Reference/tools/npc/npc_tables.py "orbit <station> <level>" (run with python -I, 20000 Monte-Carlo
// draws, normal difficulty, campaign 20), e.g. "orbit 20 0": local 0.78, jumpers 0.50, freighters 2.02, raiders 1.35
// (P 68 %); and Reference/research/npc_traffic_ai.md 2.2 / 2.3 (the formulas and boxes). The remake draws with
// UnityEngine.Random: fixed seeds, statistical tolerances of several standard errors.

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class TrafficPlanCountTests : NpcTestBase
    {
        const int N = 4000;

        struct Stats
        {
            public double local, jumpers, freighters, raiders, raidersAny, localAny;
            public int maxRaiders, maxLocal, minRaidersNonZero;
        }

        static Stats Measure(int station, int seed, int n = N, Vector3 player = default)
        {
            var s = new Stats { minRaidersNonZero = int.MaxValue };
            Repeat(station, n, seed, list =>
            {
                var c = CountGroups(list);
                s.local += c.local; s.jumpers += c.jumpers; s.freighters += c.freighters; s.raiders += c.raiders;
                if (c.raiders > 0) { s.raidersAny++; s.minRaidersNonZero = Mathf.Min(s.minRaidersNonZero, c.raiders); }
                if (c.local > 0) s.localAny++;
                s.maxRaiders = Mathf.Max(s.maxRaiders, c.raiders);
                s.maxLocal = Mathf.Max(s.maxLocal, c.local);
            }, player);
            s.local /= n; s.jumpers /= n; s.freighters /= n; s.raiders /= n; s.raidersAny /= n; s.localAny /= n;
            return s;
        }

        // ---- against npc_tables.py orbit -------------------------------------------------------------------------

        [Test]
        public void DangerousMidorianOrbitMatchesTheReference()
        {
            // python -I npc_tables.py orbit 20 0 (Eanya, security 0, Midorian)
            InFreePlay(0);
            var s = Measure(20, 1);
            Assert.AreEqual(0.78, s.local, 0.08);
            Assert.AreEqual(0.63, s.localAny, 0.04);
            Assert.AreEqual(0.50, s.jumpers, 0.04);
            Assert.AreEqual(2.02, s.freighters, 0.08);
            Assert.AreEqual(1.35, s.raiders, 0.08);
            Assert.AreEqual(0.68, s.raidersAny, 0.03);
            Assert.AreEqual(3, s.maxRaiders);
            Assert.LessOrEqual(s.maxLocal, 4);
        }

        [Test]
        public void RaidersGrowWithRank()
        {
            // orbit 20 10: raiders mean 2.71, max 5 (rank / 4 = 2 added to a non-empty group)
            InFreePlay(10);
            var s = Measure(20, 2);
            Assert.AreEqual(2.71, s.raiders, 0.12);
            Assert.AreEqual(0.68, s.raidersAny, 0.03);
            Assert.AreEqual(5, s.maxRaiders);
            Assert.AreEqual(3, s.minRaidersNonZero);
        }

        [Test]
        public void AverageNivelianOrbitMatchesTheReference()
        {
            // orbit 120 0 (Par'ah, security 2): local 2.70 (always some), raiders 0.52 (P 26 %); orbit 120 8: raiders 1.04
            InFreePlay(0);
            var s = Measure(120, 3);
            Assert.AreEqual(2.70, s.local, 0.08);
            Assert.AreEqual(1.0, s.localAny, 1e-9);
            Assert.AreEqual(0.50, s.jumpers, 0.04);
            Assert.AreEqual(1.98, s.freighters, 0.08);
            Assert.AreEqual(0.52, s.raiders, 0.06);
            Assert.AreEqual(0.26, s.raidersAny, 0.03);
            InFreePlay(8);
            Assert.AreEqual(1.04, Measure(120, 4).raiders, 0.1);
        }

        [Test]
        public void SecureOrbitHasRareSmallRaids()
        {
            // orbit 70 0 (Magnetar, security 3): local 3.70, raiders 0.15 (P 10 %, max 2): rnd(2) + 1 whatever the rank.
            InFreePlay(12);
            var s = Measure(70, 5);
            Assert.AreEqual(3.70, s.local, 0.08);
            Assert.AreEqual(0.10, s.raidersAny, 0.02);
            Assert.AreEqual(0.15, s.raiders, 0.04);
            Assert.AreEqual(2, s.maxRaiders, "1-2 raiders in a secure system, rank 12 adds nothing");
        }

        [Test]
        public void VarHastraHasNoFreightersOrJumpers()
        {
            // orbit 78 0: local 1.27 (P 32 %: only the "nothing else spawned" 4), no jumpers or freighters, raiders P 68 %.
            InFreePlay(0);
            var s = Measure(78, 6);
            Assert.AreEqual(0.0, s.jumpers);
            Assert.AreEqual(0.0, s.freighters);
            Assert.AreEqual(0.32, s.localAny, 0.03);
            Assert.AreEqual(1.27, s.local, 0.12);
            Assert.AreEqual(0.68, s.raidersAny, 0.03);
            Assert.AreEqual(4, s.maxLocal);
        }

        [Test]
        public void NothingAtAllMeansFourLocals()
        {
            // "if jumpers + local + freighters + raiders + escorts == 0: local = 4": at Var Hastra every build without raiders.
            InFreePlay(0);
            Repeat(78, 1000, 7, list =>
            {
                var c = CountGroups(list);
                Assert.AreEqual(c.raiders == 0 ? 4 : 0, c.local);
            });
        }

        [Test]
        public void LomaOnlyPirateRaiders()
        {
            // orbit 105 0: raiders 6-9 (mean 7.5), nothing else; all pirates.
            InFreePlay(0);
            var s = Measure(105, 8, 1500);
            Assert.AreEqual(7.5, s.raiders, 0.1);
            Assert.AreEqual(6, s.minRaidersNonZero);
            Assert.AreEqual(9, s.maxRaiders);
            Assert.AreEqual(0.0, s.local + s.jumpers + s.freighters);
            Repeat(105, 200, 9, list => Assert.That(list.Where(x => x.group == NpcGroup.Raider).All(x => x.race == Standing.Pirate)));
        }

        [Test]
        public void LomaOnExtreme()
        {
            // hardcore: nextInt(3) + 2 n, n = nextInt(4) + 6 -> 12..20
            InFreePlay(0);
            Session.Difficulty = Session.DifficultyExtreme;
            var s = Measure(105, 10, 1500);
            Assert.GreaterOrEqual(s.minRaidersNonZero, 12);
            Assert.LessOrEqual(s.maxRaiders, 20);
        }

        [Test]
        public void PirateLootOrbits()
        {
            // systems 32 / 33 (orbit 132 0): 10-13 pirates (mean 11.5) with double hull, speed 3.5, around the player.
            InFreePlay(5);
            var player = new Vector3(0f, 0f, 500000f);
            var s = Measure(132, 11, 1000, player);
            Assert.AreEqual(11.5, s.raiders, 0.12);
            Assert.AreEqual(10, s.minRaidersNonZero);
            Assert.AreEqual(13, s.maxRaiders);
            Assert.AreEqual(0.0, s.local + s.jumpers + s.freighters);
            Repeat(133, 100, 12, list =>
            {
                foreach (var r in list.Where(x => x.group == NpcGroup.Raider))
                {
                    Assert.AreEqual(Standing.Pirate, r.race);
                    Assert.AreEqual(3.5f, r.speed);
                    Assert.AreEqual(2 * NpcTables.Hull(0, r.ship), r.hitpoints);
                    // player + (20000 +- rnd 50000, 10000 +- rnd 50000, 20000 +- rnd 50000)
                    var d = r.position - player;
                    Assert.That(d.x > -30001 && d.x < 70000 && d.y > -40001 && d.y < 60000 && d.z > -30001 && d.z < 70000, d.ToString());
                }
            }, player);
        }

        [TestCase(102)]
        [TestCase(103)]
        [TestCase(104)]
        public void DropshipOrbitsOnlyPirates(int station)
        {
            // stations 102-104: local = jumpers = freighters = 0, raiders nextInt(5) + 3 pirates (orbit 103 0: mean 5.01, max 7).
            InFreePlay(0);
            var s = Measure(station, 13, 1500);
            Assert.AreEqual(5.0, s.raiders, 0.12);
            Assert.AreEqual(3, s.minRaidersNonZero);
            Assert.AreEqual(7, s.maxRaiders);
            Assert.AreEqual(0.0, s.local + s.jumpers + s.freighters);
        }

        [TestCase(10)]
        [TestCase(100)]
        [TestCase(101)]
        [TestCase(108)]
        public void EmptySpecialOrbits(int station)
        {
            // stations 100, 101, 108 and 10: no traffic at all
            InFreePlay(5);
            Repeat(station, 300, 14, list =>
            {
                var c = CountGroups(list);
                Assert.AreEqual(0, c.local + c.jumpers + c.freighters + c.raiders);
            });
        }

        [Test]
        public void LowPopulationDoesNotRestoreTheFourPatrolFallback()
        {
            InFreePlay(0);
            var system = SystemOf(78);
            system.ambientPopulation = 0.01f;
            system.derelictFreighters = false;
            Repeat(78, 500, 1900, list =>
            {
                Assert.LessOrEqual(Count(list, NpcGroup.Local), 1,
                    "a scaled-down non-empty plan must not become the four-patrol fallback");
            });
        }

        [Test]
        public void ZeroPopulationDoesNotRestoreTheEmptyOrbitPatrolFallback()
        {
            InFreePlay(0);
            var system = SystemOf(78);
            system.ambientPopulation = 0f;
            system.derelictFreighters = false;
            Repeat(78, 100, 1901, list =>
            {
                var counts = CountGroups(list);
                Assert.AreEqual(0, counts.local + counts.jumpers + counts.freighters + counts.raiders);
            });
        }

        [Test]
        public void ZeroPopulationSuppressesRandomLateCampaignSpecterRaids()
        {
            InStory(120, 0);
            var system = SystemOf(20);
            system.ambientPopulation = 0f;
            system.derelictFreighters = false;
            int specters = 0;
            Repeat(20, 500, 1904, list => specters += list.Count(s => s.race == Standing.Specter));
            Assert.AreEqual(0, specters);
        }

        [Test]
        public void DerelictFreightersRemainIndependentOfAmbientPopulation()
        {
            InFreePlay(0);
            var system = SystemOf(20);
            system.ambientPopulation = 0f;
            system.derelictFreighters = true;
            Settings.DerelictWrecks = true;
            int wrecks = 0;
            Repeat(20, 500, 1902, list =>
            {
                wrecks += list.Count(s => s.derelict);
                var counts = CountGroups(list);
                Assert.AreEqual(0, counts.local + counts.jumpers + counts.freighters + counts.raiders,
                    "zero ambient population must not restore ordinary traffic");
            });
            Assert.Greater(wrecks, 0, "the independent per-system derelict toggle should still permit random wrecks");

            system.derelictFreighters = false;
            Repeat(20, 100, 1903, list => Assert.IsFalse(list.Exists(s => s.derelict)));
        }

        // ---- difficulty ----------------------------------------------------------------------------------------

        [Test]
        public void ExtremeLowersTheSecurityAndDoublesTheRaiders()
        {
            // Par'ah (security 2) on Extreme: secEff 1 -> 65 %, raiders (nextInt(6) + 2) x 2 + rank / 4 when nextInt(4) > 0.
            InFreePlay(0);
            Session.Difficulty = Session.DifficultyExtreme;
            int withRaiders = 0;
            Repeat(120, N, 15, list =>
            {
                var c = CountGroups(list);
                if (c.raiders == 0) return;
                withRaiders++;
                Assert.That(c.raiders >= 4 && c.raiders <= 14 && c.raiders % 2 == 0, c.raiders.ToString());
            });
            Assert.AreEqual(0.65 * 0.75, withRaiders / (double)N, 0.03);
        }

        [Test]
        public void HardRaidersTimesOnePointFive()
        {
            // raiders = int(nextInt(4) x 1.5) + rank / 4: 1, 3, 4 at rank 0 (Hard keeps the security level)
            InFreePlay(0);
            Session.Difficulty = Session.DifficultyHard;
            var seen = new HashSet<int>();
            int withRaiders = 0;
            Repeat(120, N, 16, list => { int r = Count(list, NpcGroup.Raider); if (r > 0) { seen.Add(r); withRaiders++; } });
            CollectionAssert.AreEquivalent(new[] { 1, 3, 4 }, seen.ToArray());
            Assert.AreEqual(0.35 * 0.75, withRaiders / (double)N, 0.03);
        }

        [Test]
        public void SecureSystemOnHardKeepsItsRaidSize()
        {
            // "secEff == 3 and raidersOn and difficulty < 1": Easy / Normal 1-2; Hard (difficulty 1) not.
            InFreePlay(0);
            Session.Difficulty = Session.DifficultyEasy;
            Repeat(70, 2000, 17, list => Assert.LessOrEqual(Count(list, NpcGroup.Raider), 2));
            Session.Difficulty = Session.DifficultyHard;
            int max = 0;
            Repeat(70, 3000, 18, list => max = Mathf.Max(max, Count(list, NpcGroup.Raider)));
            Assert.AreEqual(4, max, "int(3 x 1.5) at rank 0");
        }

        // ---- the raider group ----------------------------------------------------------------------------------

        [Test]
        public void RaiderGroupsAreOneModelMostlyPirates()
        {
            // one S = getRandomEnemyFighter(raiderRace) for the whole group; raiderRace = 75 % pirates else enemyRace(3) = 2.
            InFreePlay(8);
            int groups = 0, pirates = 0;
            Repeat(20, N, 19, list =>
            {
                var raiders = list.Where(x => x.group == NpcGroup.Raider).ToList();
                if (raiders.Count == 0) return;
                groups++;
                Assert.AreEqual(1, raiders.Select(r => r.ship).Distinct().Count(), "one model");
                Assert.AreEqual(1, raiders.Select(r => r.race).Distinct().Count());
                int race = raiders[0].race;
                Assert.That(race == Standing.Pirate || race == 2, "Eanya is Midorian: the enemy race is Nivelian");
                CollectionAssert.Contains(Pool(race), raiders[0].ship);
                if (race == Standing.Pirate) pirates++;
            });
            Assert.AreEqual(0.75, pirates / (double)groups, 0.03);
        }

        [Test]
        public void RaidersSpawnInFrontOfTheStation()
        {
            // raiderSpawn = (nextInt(100000) - 50000, 0, nextInt(50000) + 50000), each +-20000 (createShip's jitter)
            InFreePlay(4);
            Repeat(20, 1000, 20, list =>
            {
                foreach (var r in list.Where(x => x.group == NpcGroup.Raider))
                    Assert.That(r.position.x >= -70000 && r.position.x < 70000 && r.position.y >= -20000 && r.position.y < 20000
                                && r.position.z >= 30000 && r.position.z < 120000, r.position.ToString());
            });
        }

        [Test]
        public void PirateAmbushOnArrival()
        {
            // initStreamOutPosition: arriving by travel, nextInt(100) < rank + 20 moves the pirates' point toward the player
            // (the remake: a quarter of the way). Player at z 800000 -> the point at z 200000 instead of 50000..100000.
            Assert.AreEqual(0.25f, TrafficPlan.AmbushShare);
            InFreePlay(20);
            Session.ArrivedByTravel = true;
            var player = new Vector3(0f, 0f, 800000f);
            int pirateGroups = 0, ambushed = 0;
            Repeat(20, N, 21, list =>
            {
                var raiders = list.Where(x => x.group == NpcGroup.Raider).ToList();
                if (raiders.Count == 0 || raiders[0].race != Standing.Pirate) return;
                pirateGroups++;
                if (raiders[0].position.z > 150000f) { ambushed++; Assert.AreEqual(200000f, raiders[0].position.z, 20000f); }
            }, player);
            Assert.AreEqual(0.40, ambushed / (double)pirateGroups, 0.04);
            Session.ArrivedByTravel = false;
            ambushed = 0;
            Repeat(20, 1000, 22, list => { if (list.Any(x => x.group == NpcGroup.Raider && x.position.z > 150000f)) ambushed++; }, player);
            Assert.AreEqual(0, ambushed, "only travellers");
        }

        [Test]
        public void BigBattleNineAgainstNine()
        {
            // raidersOn and campaign > 0x1f and nextInt(100) < 8: raiders = local = 9 (Eanya: 0.9 x 0.08 = 7.2 %).
            InCompletedWorld(0);
            int battles = 0;
            Repeat(20, N, 23, list =>
            {
                var c = CountGroups(list);
                if (c.raiders == 9) { battles++; Assert.AreEqual(9, c.local); }
            });
            Assert.AreEqual(0.072, battles / (double)N, 0.015);
            InFreePlay(0);   // campaign 20: never
            Repeat(20, 1500, 24, list => Assert.Less(Count(list, NpcGroup.Raider), 9));
        }

        // ---- local fighters, jumpers, freighters -------------------------------------------------------------------

        [Test]
        public void LocalFightersOfTheSystemRaceAroundOnePoint()
        {
            // wpLocal = (+-10000, +-10000, 20000..50000), each +-20000.
            InFreePlay(0);
            Repeat(120, 600, 25, list =>
            {
                var locals = list.Where(x => x.group == NpcGroup.Local).ToList();
                foreach (var l in locals)
                {
                    Assert.AreEqual(2, l.race);
                    CollectionAssert.Contains(Pool(2), l.ship);
                    Assert.That(l.position.x >= -30000 && l.position.x < 30000 && l.position.y >= -30000 && l.position.y < 30000
                                && l.position.z >= 0 && l.position.z < 70000, l.position.ToString());
                }
                for (int i = 1; i < locals.Count; i++)
                    Assert.Less((locals[i].position - locals[0].position).magnitude, 80000f * 1.75f, "one shared point");
            });
        }

        [Test]
        public void JumpersStartDeadWithAFarRoute()
        {
            // jumpers: system-race fighters, setDead, one-point route (nextInt(400000) - 200000, nextInt(200000) - 100000,
            // nextInt(100000) + 50000), not looping.
            InFreePlay(0);
            int seen = 0;
            Repeat(20, 400, 26, list =>
            {
                foreach (var j in list.Where(x => x.group == NpcGroup.Jumper))
                {
                    seen++;
                    Assert.IsTrue(j.startsDead);
                    Assert.AreEqual(3, j.race);
                    CollectionAssert.Contains(Pool(3), j.ship);
                    Assert.IsNotNull(j.route);
                    Assert.IsFalse(j.route.loop);
                    Assert.AreEqual(1, j.route.points.Count);
                    var p = j.route.points[0];
                    Assert.That(p.x >= -200000 && p.x < 200000 && p.y >= -100000 && p.y < 100000 && p.z >= 50000 && p.z < 150000, p.ToString());
                }
            });
            Assert.Greater(seen, 100);
        }

        [Test]
        public void FreightersBesideTheStation()
        {
            // (s * (nextInt(60000) - 80000), nextInt(40000) - 20000, nextInt(160000) - 80000), s = +-1; Midorian freighters.
            InFreePlay(0);
            Repeat(20, 600, 27, list =>
            {
                foreach (var f in list.Where(x => x.group == NpcGroup.Freighter))
                {
                    Assert.IsTrue(f.freighter);
                    Assert.AreEqual((15, 3), (f.ship, f.race));
                    float ax = Mathf.Abs(f.position.x);
                    Assert.That(ax > 20000 && ax <= 80000 && f.position.y >= -20000 && f.position.y < 20000
                                && f.position.z >= -80000 && f.position.z < 80000, f.position.ToString());
                }
                Assert.LessOrEqual(Count(list, NpcGroup.Freighter), 4);
            });
        }

        [Test]
        public void AttackedStationHasAtLeastSevenLocals()
        {
            // station.hasAttackedFriends(): local = max(local, 7)
            InFreePlay(0);
            Session.AttackedStations.Add(20);
            Repeat(20, 500, 28, list => Assert.GreaterOrEqual(Count(list, NpcGroup.Local), 7));
            Repeat(21, 500, 29, list => Assert.Less(Count(list, NpcGroup.Local), 7), default);
        }

        // ---- the story's orbits ---------------------------------------------------------------------------------

        [Test]
        public void MidoBeforeCampaign16HasNoRaidersAndFewLocals()
        {
            // tutorial = system 15 and campaign < 16: no raiders (not hardcore), local base nextInt(2) instead of the security.
            InStory(5);
            int localAny = 0;
            Repeat(77, 2000, 30, list =>
            {
                var c = CountGroups(list);
                Assert.AreEqual(0, c.raiders);
                if (c.local > 0) localAny++;
                Assert.LessOrEqual(c.local, 4);
            });
            Assert.Greater(localAny, 0);
            Session.Difficulty = Session.DifficultyExtreme;
            int raided = 0;
            Repeat(77, 1000, 31, list => { if (Count(list, NpcGroup.Raider) > 0) raided++; });
            Assert.Greater(raided, 400, "Extreme ignores the tutorial rule");
        }

        [Test]
        public void MidoAfterCampaign16IsNormal()
        {
            InStory(20);
            int raided = 0;
            Repeat(77, 2000, 32, list => { if (Count(list, NpcGroup.Raider) > 0) raided++; });
            Assert.AreEqual(0.68, raided / 2000.0, 0.04);
        }

        [Test]
        public void SupernovaSystemIsEmptyBeforeItsEnd()
        {
            // the supernova system (27) before campaign 0x9e: no local / jumpers / freighters / raiders
            InStory(90);
            Repeat(111, 500, 33, list => Assert.AreEqual(0, list.Count(x => x.group == NpcGroup.Local || x.group == NpcGroup.Jumper
                                                                            || x.group == NpcGroup.Freighter || x.group == NpcGroup.Raider)));
            InStory(0x9e);
            int any = 0;
            Repeat(111, 300, 34, list => any += list.Count(x => x.group == NpcGroup.Local));
            Assert.Greater(any, 0);
        }

        [Test]
        public void SkolptorrAtCampaign0x24HasNoLocalsOrRaiders()
        {
            InStory(0x24);
            Repeat(26, 500, 35, list => Assert.AreEqual(0, Count(list, NpcGroup.Local) + Count(list, NpcGroup.Raider)));
        }

        [Test]
        public void Campaign0x2aHasNoRaiders()
        {
            InStory(0x2a);
            Repeat(20, 500, 36, list => Assert.AreEqual(0, Count(list, NpcGroup.Raider)));
        }
        [Test]
        public void ZeroPopulationSuppressesRandomAmbientTrafficAndSpecialGroups()
        {
            InFreePlay(0);
            var stations = new List<int> { 20, 102, 103, 104, 105, 132, 133 };
            int pirateBaseStation = Db.Stations.First(s => PirateBases.SystemHasBase(Db, s.system)).index;
            stations.Add(pirateBaseStation);

            foreach (int station in stations)
            {
                SystemOf(station).ambientPopulation = 0f;
                var list = Build(station);
                Assert.AreEqual(0, Count(list, NpcGroup.Local), "locals at " + station);
                Assert.AreEqual(0, Count(list, NpcGroup.Jumper), "jumpers at " + station);
                Assert.AreEqual(0, Count(list, NpcGroup.Freighter), "freighters at " + station);
                Assert.AreEqual(0, Count(list, NpcGroup.Raider), "raiders at " + station);
                Assert.AreEqual(0, Count(list, NpcGroup.Escort), "ambient escorts at " + station);
            }
        }

        [Test]
        public void FractionalPopulationScalesSpecialPirateOrbitGroups()
        {
            InFreePlay(0);
            SystemOf(105).ambientPopulation = 0.5f; // Loma: vanilla mean 7.5 raiders
            double total = 0;
            Repeat(105, 2000, 831, list =>
            {
                int count = Count(list, NpcGroup.Raider);
                Assert.That(count, Is.InRange(3, 5));
                total += count;
            });
            Assert.AreEqual(3.75, total / 2000, 0.12);
        }

        [Test]
        public void PerSystemDerelictToggleIsIndependentOfAmbientTrafficAndGlobalOption()
        {
            InFreePlay(0);
            var system = SystemOf(120);
            system.ambientPopulation = 1f;
            system.derelictFreighters = false;
            Settings.DerelictWrecks = true;
            Repeat(120, 500, 832, list => Assert.IsFalse(list.Exists(s => s.derelict), "disabled for this system"));

            system.derelictFreighters = true;
            system.ambientPopulation = 1f;
            Settings.DerelictWrecks = true;
            int wrecks = 0;
            Repeat(120, 1000, 833, list => { if (list.Exists(s => s.derelict)) wrecks++; });
            Assert.Greater(wrecks, 0, "derelict toggle independently disables this spawn category");

            system.ambientPopulation = 1f;
            Settings.DerelictWrecks = false;
            Repeat(120, 500, 834, list => Assert.IsFalse(list.Exists(s => s.derelict), "global gameplay option is the master switch"));
        }

    }
}
