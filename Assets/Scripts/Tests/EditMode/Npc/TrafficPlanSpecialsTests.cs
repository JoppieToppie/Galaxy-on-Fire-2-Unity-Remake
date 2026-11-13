// TrafficPlanSpecialsTests.cs
// Feature: CLAUDE.md "NPCs and combat" > Traffic / Capital ships and pirate bases / Fleet battles, "Supernova add-on" >
// Most Wanted / Specters / Hacking (hidden-blueprint wrecks), "Space scene" > Alien orbit / Void invasion, "Bar and freelance"
// (Informer orbits, cargo-mission pirates), "Story" (step 59's arms convoy): the special groups TrafficPlan.Build adds.
// Expected values: Reference/research/npc_traffic_ai.md 2.1-2.3 (alien orbit count, the convoy, the Specter chance
// int(cm / 144 * 15 + 5) << hardcore, the informer's 7 / 6, the escorts int(d / 10 * 5)), npc_combat_specials.md 2.1-2.4,
// 3.2, 4 (capital-ship odds 30 % / 30 % past 103 / past 140, "4/5 x 30 % = 24 % per visit", turret counts, the pirate
// bases' guards int((d - 0.5) * 5 + 5), the outposts' table, the hidden-blueprint races [1, 3, 2, 0, 2]),
// Reference/research/wingmen_wanted.md 2.6 (the Wanted: h = 15 * min(rank, 20) + hitpoints + 4 * 45, x difficulty,
// escorts h / 2, speed 4.5, its weapon x4, ships 45-48 a G'liissk), Resources/GoF2Data/wanted.json (Qyrr Myfft: race 4,
// ship 45, 10000, 4 wingmen), kaamo_club.md 3.2 (the outpost hull 5 * (4 cm + 15 rank + 20) x difficulty), CLAUDE.md
// (fleet battles 3 %). Statistical checks use fixed UnityEngine.Random seeds.

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class TrafficPlanSpecialsTests : NpcTestBase
    {
        // ---- the alien orbit ----------------------------------------------------------------------------------------

        [Test]
        public void AlienOrbitTwoVoidFightersAtLowRank()
        {
            InFreePlay(0);
            Repeat(Session.VoidOrbit, 200, 1, list =>
            {
                Assert.AreEqual(2, list.Count);
                foreach (var s in list)
                {
                    Assert.AreEqual(Standing.Void, s.race);
                    Assert.AreEqual(8, s.ship);
                    Assert.IsTrue(s.alwaysEnemy);
                    // (nextInt(120000) - 60000, nextInt(80000) - 40000, nextInt(120000) - 60000)
                    Assert.That(Mathf.Abs(s.position.x) <= 60000 && Mathf.Abs(s.position.y) <= 40000 && Mathf.Abs(s.position.z) <= 60000);
                }
            });
        }

        [Test]
        public void AlienOrbitGrowsWithRank()
        {
            // n = level / 2 - 1 + nextInt(2) when that is >= 2: rank 10 -> 4 or 5.
            InFreePlay(10);
            var seen = new System.Collections.Generic.HashSet<int>();
            Repeat(Session.VoidOrbit, 300, 2, list => seen.Add(list.Count));
            CollectionAssert.AreEquivalent(new[] { 4, 5 }, seen.ToArray());
        }

        [TestCase(0x21)]
        [TestCase(0x44)]
        public void AlienOrbitStaysAtTwoInItsStorySteps(int campaign)
        {
            InStory(campaign, 10);
            Repeat(Session.VoidOrbit, 100, 3, list => Assert.AreEqual(2, list.Count));
        }

        // ---- the Void invasion ----------------------------------------------------------------------------------

        [Test]
        public void VoidRaidAtTheAttackedStation()
        {
            // freighters = max(freighters, 2); raiders = nextInt(4) + 2, race 9, at the wormhole (+-20000).
            InStory(30);
            Session.VoidInvasionStation = 20;
            var wormhole = new Vector3(0f, 0f, 300000f);
            UnityEngine.Random.InitState(4);
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < 300; i++)
            {
                var list = Build(20, default, wormhole);
                var raiders = list.Where(x => x.group == NpcGroup.Raider).ToList();
                seen.Add(raiders.Count);
                Assert.GreaterOrEqual(Count(list, NpcGroup.Freighter), 2);
                foreach (var r in raiders)
                {
                    Assert.AreEqual(Standing.Void, r.race);
                    Assert.LessOrEqual((r.position - wormhole).magnitude, 20000f * 1.74f);
                }
            }
            CollectionAssert.AreEquivalent(new[] { 2, 3, 4, 5 }, seen.ToArray());
        }

        [Test]
        public void NoVoidRaidAt42OrAfterTheWin()
        {
            Session.VoidInvasionStation = 20;
            InStory(42);
            Repeat(20, 300, 5, list => Assert.That(list.All(x => x.race != Standing.Void)));
            InStory(46);
            Repeat(20, 300, 6, list => Assert.That(list.All(x => x.race != Standing.Void)));
        }

        // ---- Specters ------------------------------------------------------------------------------------------

        [Test]
        public void SpecterRaidsLateInTheCampaign()
        {
            // 100 < cm < 0x91: p = int(cm / 144 * 15 + 5) % = 17 % at 120; (nextInt(3) + 2) ships near the player.
            InStory(120);
            const int n = 4000;
            int raids = 0;
            var player = new Vector3(100000f, 0f, 0f);
            Repeat(20, n, 7, list =>
            {
                var sp = list.Where(x => x.race == Standing.Specter).ToList();
                if (sp.Count == 0) return;
                raids++;
                Assert.That(sp.Count >= 2 && sp.Count <= 4, sp.Count.ToString());
                foreach (var s in sp)
                {
                    Assert.AreEqual(44, s.ship);
                    Assert.IsTrue(s.alwaysEnemy);
                    Assert.AreEqual(NpcGroup.Special, s.group);
                    var d = s.position - player;   // +-(20000..70000) +-20000 per axis on x / z
                    Assert.That(Mathf.Abs(d.x) < 90000 && Mathf.Abs(d.z) < 90000 && Mathf.Abs(d.y) < 80000, d.ToString());
                }
            }, player);
            Assert.AreEqual(0.17, raids / (double)n, 0.02);
        }

        [Test]
        public void SpecterRaidsDoubleOnExtreme()
        {
            InStory(120);
            Session.Difficulty = Session.DifficultyExtreme;
            const int n = 3000;
            int raids = 0;
            Repeat(20, n, 8, list =>
            {
                int c = list.Count(x => x.race == Standing.Specter);
                if (c == 0) return;
                raids++;
                Assert.That(c == 4 || c == 6 || c == 8, c.ToString());
            });
            Assert.AreEqual(0.34, raids / (double)n, 0.03);
        }

        [TestCase(100)]
        [TestCase(0x91)]
        public void NoSpectersOutsideTheirSteps(int campaign)
        {
            InStory(campaign);
            Repeat(20, 1000, 9, list => Assert.That(list.All(x => x.race != Standing.Specter)));
        }

        [Test]
        public void NoSpectersInFreePlay()
        {
            InFreePlay(10);
            Session.CampaignMission = 120;
            Repeat(20, 1000, 10, list => Assert.That(list.All(x => x.race != Standing.Specter)));
        }

        // ---- freelance orbits ----------------------------------------------------------------------------------

        [Test]
        public void InformerOrbitHasSevenLocalsAndNothingElse()
        {
            InFreePlay(0);
            Session.FreelanceMission = new FreelanceMission { type = MissionType.Informer, target = 20 };
            Assert.IsTrue(TrafficPlan.InformerOrbit(20));
            Repeat(20, 200, 11, list =>
            {
                var c = CountGroups(list);
                Assert.AreEqual(7, c.local);
                Assert.AreEqual(0, c.jumpers + c.freighters + c.raiders);
                var named = list.Where(x => x.nameText == 1663).ToList();
                Assert.AreEqual(1, named.Count, "one Informer (1663)");
                Assert.AreSame(list.First(x => x.group == NpcGroup.Local), named[0], "the first local fighter");
            });
            Session.InformerKilled = true;
            Repeat(20, 100, 12, list =>
            {
                Assert.AreEqual(6, Count(list, NpcGroup.Local));
                Assert.That(list.All(x => x.nameText != 1663));
            });
            Session.InformerKilled = false;
            Session.InformerFailed = true;
            Assert.IsFalse(TrafficPlan.InformerOrbit(20), "a spoiled Informer gets the normal traffic");
            Assert.IsFalse(TrafficPlan.InformerOrbit(21));
        }

        [TestCase(MissionType.Courier, 4, 2)]
        [TestCase(MissionType.Passenger, 6, 3)]
        [TestCase(MissionType.OreMining, 10, 5)]
        [TestCase(MissionType.Defense, 10, 0)]
        public void CargoMissionsDrawPirates(int type, int difficulty, int escorts)
        {
            // int(d / 10 * 5) pirates near the player for types 0, 0xb, 0xf; station 10's empty rule leaves them.
            InFreePlay(0);
            Session.FreelanceMission = new FreelanceMission { type = type, difficulty = difficulty, target = 55 };
            var player = new Vector3(0f, 0f, -300000f);
            Repeat(10, 50, 13, list =>
            {
                var pirates = list.Where(x => x.group == NpcGroup.Raider).ToList();
                Assert.AreEqual(escorts, pirates.Count);
                foreach (var p in pirates)
                {
                    Assert.AreEqual(Standing.Pirate, p.race);
                    var d = p.position - player;   // (nextInt(160000) - 80000, nextInt(100000) - 50000, nextInt(160000) - 80000)
                    Assert.That(Mathf.Abs(d.x) <= 80000 && Mathf.Abs(d.y) <= 50000 && Mathf.Abs(d.z) <= 80000, d.ToString());
                }
            }, player);
        }

        // ---- the Most Wanted -----------------------------------------------------------------------------------

        void PutWanted(int index, int station)
        {
            for (int i = 0; i < Db.Wanted.Count; i++) WantedBoard.State(Db, i).active = false;
            var st = WantedBoard.State(Db, index);
            st.active = true;
            st.terminated = false;
            st.current = station;
        }

        [TestCase(Session.DifficultyNormal, 10330)]
        [TestCase(Session.DifficultyExtreme, 20660)]
        public void WantedCriminalAndEscorts(float difficulty, int hull)
        {
            // Qyrr Myfft (wanted 6): race 4 -> pirates, ship 45, hitpoints 10000, weapon 27, 4 wingmen. Rank 10:
            // h = 150 + 10000 + 180 = 10330 (x2 Extreme), escorts h / 2.
            InStory(140, 10);
            Session.Difficulty = difficulty;
            var w = Db.Wanted[6];
            Assert.AreEqual((4, 45, 10000, 4), (w.race, w.ship, w.hitpoints, w.numWingmen), "wanted.json");
            PutWanted(6, 20);
            Repeat(20, 50, 14, list =>
            {
                var c = list.Single(x => x.wantedIndex == 6);
                Assert.AreSame(list[0], c, "the criminal comes first");
                Assert.AreEqual(Standing.Pirate, c.race);
                Assert.AreEqual(45, c.ship);
                Assert.AreEqual(hull, c.hitpoints);
                Assert.AreEqual(4.5f, c.speed);
                Assert.AreEqual(w.weapon, c.gunItem);
                Assert.AreEqual(4f, c.gunFactor);
                Assert.AreEqual(31, c.secondaryItem, "ships 45-48: a G'liissk");
                Assert.AreEqual(w.name, c.name);
                Assert.AreEqual(w.loot, c.lootItem);
                var escorts = list.Where(x => x.wantedEscortOf == 6).ToList();
                Assert.AreEqual(4, escorts.Count);
                foreach (var e in escorts)
                {
                    Assert.AreEqual(hull / 2, e.hitpoints);
                    Assert.AreEqual(Standing.Pirate, e.race);
                    CollectionAssert.Contains(Pool(8), e.ship);
                }
                // the police capped at 2 (besides the criminal and its escort); TrafficPlan.Build then applies "an orbit with nothing
                // at all gets 4", which can raise it again (the order the remake keeps; unverified against Level::createMission)
                Assert.LessOrEqual(list.Count(x => x.group == NpcGroup.Local && x.wantedIndex < 0 && x.wantedEscortOf < 0), 4);
            });
        }

        [Test]
        public void WantedOfAMainRaceKeepsItsRaceAndNoRocket()
        {
            // Gendol Ethor (wanted 2): Nivelian, ship 21, 1 wingman.
            InStory(140, 10);
            var w = Db.Wanted[2];
            PutWanted(2, 20);
            UnityEngine.Random.InitState(15);
            var list = Build(20);
            var c = list.Single(x => x.wantedIndex == 2);
            Assert.AreEqual(w.race, c.race);
            Assert.AreEqual(-1, c.secondaryItem);
            Assert.AreEqual(150 + w.hitpoints + 180, c.hitpoints);
            Assert.AreEqual(w.numWingmen, list.Count(x => x.wantedEscortOf == 2));
        }

        [Test]
        public void NoWantedInFreePlayOrElsewhere()
        {
            InStory(140, 10);
            PutWanted(6, 21);
            Repeat(20, 50, 16, list => Assert.That(list.All(x => x.wantedIndex < 0 && x.wantedEscortOf < 0)));
            InFreePlay(10);
            PutWanted(6, 20);
            Repeat(20, 50, 17, list => Assert.That(list.All(x => x.wantedIndex < 0)));
        }

        // ---- step 59's arms convoy -------------------------------------------------------------------------------

        [Test]
        public void ArmsConvoyAtATargetStation()
        {
            InStory(59);
            Session.StoryMission = new StoryMission { type = StoryType.TargetList };
            Session.StoryTargets.Add(20);
            Repeat(20, 100, 18, list =>
            {
                var f = list[0];
                Assert.AreEqual(SpawnSpec.ConvoyFreighter, f.convoyRole);
                Assert.AreEqual(1664, f.nameText);
                Assert.AreEqual(3, f.race);
                Assert.AreEqual(15, f.ship);
                Assert.IsTrue(f.stationary && f.freighter);
                CollectionAssert.Contains(new[] { 0, 1, 2, 0x24, 0x16, 0x17 }, f.lootItem);
                var c = f.position;   // (+-(80000..109999), -6000..-3001, 120000..169999)
                Assert.That(Mathf.Abs(c.x) >= 80000 && Mathf.Abs(c.x) < 110000 && c.y >= -6000 && c.y < -3000 && c.z >= 120000 && c.z < 170000, c.ToString());
                var escorts = list.Where(x => x.convoyRole == SpawnSpec.ConvoyEscort).ToList();
                Assert.AreEqual(5, escorts.Count);
                foreach (var e in escorts)
                {
                    Assert.IsTrue(e.route.loop);
                    Assert.AreEqual(c, e.route.points[0]);
                }
                var turrets = list.Where(x => x.convoyRole == SpawnSpec.ConvoyTurret).ToList();
                Assert.AreEqual(2, turrets.Count);
                foreach (var t in turrets)
                {
                    Assert.AreEqual(0.3f, t.scale);
                    Assert.AreEqual(1000, t.hitpoints);
                    Assert.AreEqual("v_station_battlestation_turret", t.turretAssembly);
                    Assert.Less((t.position - c).magnitude, 5000f);
                }
                Assert.AreEqual(0, CountGroups(list).raiders + CountGroups(list).freighters + CountGroups(list).jumpers, "nothing else");
            });
        }

        [Test]
        public void ConvoyOnlyAtItsTargets()
        {
            InStory(59);
            Session.StoryMission = new StoryMission { type = StoryType.TargetList };
            Session.StoryTargets.Add(21);
            Repeat(20, 100, 19, list => Assert.That(list.All(x => x.convoyRole == 0)));
        }

        // ---- pirate bases -----------------------------------------------------------------------------------------

        [TestCase(Session.DifficultyNormal, 2, 2)]
        [TestCase(Session.DifficultyExtreme, 4, 6)]
        public void PirateBaseSystemHasEscortsInsteadOfRaiders(float difficulty, int min, int max)
        {
            // Behén (system 2): 2 awake pirates near the player (hardcore 4..6), never a raider group.
            InFreePlay(5);
            Session.Difficulty = difficulty;
            Assert.IsTrue(PirateBases.SystemHasBase(Db, 2));
            var player = new Vector3(0f, 100000f, 0f);
            Repeat(30, 300, 20, list =>
            {
                Assert.AreEqual(0, Count(list, NpcGroup.Raider));
                var esc = list.Where(x => x.group == NpcGroup.Escort).ToList();
                Assert.That(esc.Count >= min && esc.Count <= max, esc.Count.ToString());
                foreach (var e in esc)
                {
                    Assert.AreEqual(Standing.Pirate, e.race);
                    var d = e.position - player;
                    Assert.That(Mathf.Abs(d.x) <= 80000 && Mathf.Abs(d.y) <= 50000 && Mathf.Abs(d.z) <= 80000, d.ToString());
                }
            }, player);
        }

        [TestCase(Session.DifficultyEasy, 2)]
        [TestCase(Session.DifficultyNormal, 5)]
        [TestCase(Session.DifficultyHard, 7)]
        [TestCase(Session.DifficultyExtreme, 10)]
        public void PirateBaseOutpostAndGuards(float difficulty, int guards)
        {
            // station 33 (base 1): the outpost at (50000, 200000, -100000) +-10000, asleep, crate Thermic o5 x1, guards
            // int((d - 0.5) * 5 + 5), asleep, at the table + (+-nextInt(20000) + 10000) per axis.
            InFreePlay(4);
            Session.Difficulty = difficulty;
            var table = new Vector3(50000, 200000, -100000);
            Repeat(33, 50, 21, list =>
            {
                var outpost = list.Single(x => x.group == NpcGroup.Outpost);
                Assert.AreEqual("station_pirates", outpost.fixedObject);
                Assert.AreEqual(441, outpost.nameText);
                Assert.AreEqual(1002, outpost.collisionId);
                Assert.AreEqual(7500f, outpost.hitRadius);
                Assert.IsTrue(outpost.asleep && outpost.alwaysEnemy && outpost.stationary);
                Assert.AreEqual((28, 1), (outpost.lootItem, outpost.lootAmount));
                // kaamo_club.md 3.2: 5 * (4 * 20 + 15 * 4 + 20) x difficulty
                Assert.AreEqual((int)(5 * (80 + 60 + 20) * Session.DifficultyFactor), outpost.hitpoints);
                var d = outpost.position - table;
                Assert.That(Mathf.Abs(d.x) <= 10000 && Mathf.Abs(d.y) <= 10000 && Mathf.Abs(d.z) <= 10000, d.ToString());
                var g = list.Where(x => x.group == NpcGroup.Guard).ToList();
                Assert.AreEqual(guards, g.Count);
                foreach (var x in g)
                {
                    Assert.IsTrue(x.asleep && x.guard);
                    Assert.AreEqual(Standing.Pirate, x.race);
                    var o = x.position - table;
                    Assert.That(o.x > -10001 && o.x < 30000 && o.y > -10001 && o.y < 30000 && o.z > -10001 && o.z < 30000, o.ToString());
                }
            });
        }

        [Test]
        public void DestroyedBaseIsGone()
        {
            InFreePlay(4);
            Session.PirateBaseDestroyed[1] = true;
            Assert.IsFalse(PirateBases.StationHasBase(33));
            Assert.IsFalse(PirateBases.SystemHasBase(Db, 2));
            int raided = 0;
            Repeat(33, 500, 22, list =>
            {
                Assert.AreEqual(0, list.Count(x => x.group == NpcGroup.Outpost || x.group == NpcGroup.Guard || x.group == NpcGroup.Escort));
                if (Count(list, NpcGroup.Raider) > 0) raided++;
            });
            Assert.Greater(raided, 0, "the system gets raider groups again");
        }

        [Test]
        public void BaseStationsAndTheirSystems()
        {
            // DAT_00251f90 = [1, 33, 47, 86] in Suteo, Behén, Weymire, Nesla.
            CollectionAssert.AreEqual(new[] { 1, 33, 47, 86 }, PirateBases.Stations);
            foreach (var (station, system) in new[] { (1, 0), (33, 2), (47, 9), (86, 17) })
            {
                Assert.IsTrue(PirateBases.StationHasBase(station));
                Assert.IsTrue(PirateBases.SystemHasBase(Db, system));
                Assert.AreEqual(system, Db.Stations.Find(s => s.index == station).system);
            }
            Assert.IsFalse(PirateBases.SystemHasBase(Db, 4));
            CollectionAssert.AreEqual(new[] { (45, 5), (28, 1), (4, 1), (17, 1) }, PirateBases.Loot);
        }

        // ---- hidden-blueprint wrecks -----------------------------------------------------------------------------

        [TestCase(132, 0, 1, 13, 14)]
        [TestCase(133, 1, 3, 15, 11)]
        [TestCase(134, 2, 2, 15, 12)]
        [TestCase(129, 3, 0, 15, 13)]
        [TestCase(123, 4, 2, 15, 12)]
        public void HiddenBlueprintWrecks(int station, int slot, int race, int ship, int points)
        {
            InFreePlay(0);
            UnityEngine.Random.InitState(23);
            var w = Build(station).Single(x => x.hiddenBlueprint >= 0);
            Assert.AreEqual(slot, w.hiddenBlueprint);
            Assert.AreEqual((race, ship, points), (w.race, w.ship, w.spacePoints));
            Assert.AreEqual(3, w.dockingType, "hackable until found");
            Assert.IsTrue(w.deadButSelectable && w.stationary && w.alwaysFriend && w.noLoot);
            Assert.AreEqual(NpcTables.FreighterAssembly(race), w.fixedObject);
            Session.HiddenBlueprintsFound = 1 << slot;
            Assert.AreEqual(0, Build(station).Single(x => x.hiddenBlueprint >= 0).dockingType, "found: no longer dockable");
            Session.HiddenBlueprintsFound = 0;
            Assert.IsFalse(Build(20).Any(x => x.hiddenBlueprint >= 0));
        }

        // ---- derelict freighters (Options > Gameplay, Settings.DerelictWrecks) -------------------------------------

        /// <summary>A plain orbit (no storyline, pirate base or special system) whose system's security fits.</summary>
        static int PlainStation(System.Func<int, bool> security) => Db.Stations.Find(s =>
            s.index < 100 && s.index != 10 && Db.Systems.Find(x => x.index == s.system) is var sys && sys != null && security(sys.securityLevel)
            && sys.index != 15 && sys.index != 25 && sys.index != 27 && sys.index != 32 && sys.index != 33
            && !PirateBases.SystemHasBase(Db, sys.index) && !PirateEventRules.InStoryline(s.index)).index;

        [Test]
        public void DerelictFreightersComeWithTheOption()
        {
            InFreePlay(4);
            int unsafeOrbit = PlainStation(sec => sec <= 1), secure = PlainStation(sec => sec == 3);
            Repeat(unsafeOrbit, 300, 31, list => Assert.IsFalse(list.Any(x => x.derelict), "off: the original's traffic"));
            Settings.DerelictWrecks = true;   // (NpcTestBase puts it back)
            foreach (int station in new[] { unsafeOrbit, secure })
            {
                int found = 0, guarded = 0;
                Repeat(station, 300, 31, list =>
                {
                    var w = list.Where(x => x.derelict).ToList();
                    Assert.LessOrEqual(w.Count, 1, "one an orbit");
                    if (w.Count == 0) return;
                    found++;
                    Assert.IsTrue(w[0].deadButSelectable && w[0].dockingType == 3 && w[0].noLoot, "a hackable wreck");
                    if (list.Any(x => x.group == NpcGroup.Raider && x.race == Standing.Pirate && (x.position - w[0].position).magnitude < 40000f)) guarded++;
                });
                Assert.Greater(found, 0, "station " + station);
                Assert.Less(found, 60, "rare");
                if (station == unsafeOrbit) Assert.AreEqual(found, guarded, "pirates wait at it in an unsafe system");
            }
            Repeat(132, 100, 31, list => Assert.IsFalse(list.Any(x => x.derelict), "not beside a hidden blueprint's wreck"));
            for (int i = 0; i < 100; i++) Assert.IsNotEmpty(GoF2Remake.World.WreckField.DerelictLoot(Db));
        }

        // ---- capital ships --------------------------------------------------------------------------------------

        static int Kind(System.Collections.Generic.List<SpawnSpec> list) => list.FirstOrDefault(x => x.capital != 0 && x.capitalHost == null)?.capital ?? 0;

        [Test]
        public void TerranBattleshipInAQuarterOfTerranOrbits()
        {
            // terran = sysRace 0 and freighters > 0 and nextInt(100) < 30: 4/5 x 30 % = 24 % (npc_combat_specials.md 2.1);
            // no carrier at campaign 20 (needs > 0x67). It replaces the first freighter; 7 turrets.
            InFreePlay(0);
            const int n = 4000;
            int battleships = 0;
            double freighters = 0;
            Repeat(70, n, 24, list =>
            {
                int kind = Kind(list);
                Assert.AreNotEqual(SpawnSpec.CapitalCarrier, kind);
                freighters += Count(list, NpcGroup.Freighter);
                if (kind != SpawnSpec.CapitalBattleship) { Assert.AreEqual(0, Count(list, NpcGroup.Turret)); return; }
                battleships++;
                freighters++;
                var host = list.Single(x => x.capital == SpawnSpec.CapitalBattleship);
                Assert.AreEqual(14, host.ship);
                Assert.AreEqual(0, host.race);
                Assert.IsTrue(host.freighter && host.stationary);
                Assert.AreEqual(7, list.Count(x => x.group == NpcGroup.Turret && x.capitalHost == host));
                // (nextInt(80000) - 40000, nextInt(10000) - 5000, nextInt(80000) + 40000)
                var p = host.position;
                Assert.That(p.x >= -40000 && p.x < 40000 && p.y >= -5000 && p.y < 5000 && p.z >= 40000 && p.z < 120000, p.ToString());
            });
            Assert.AreEqual(0.24, battleships / (double)n, 0.025);
            Assert.AreEqual(1.99, freighters / n, 0.08, "freighters + the replaced one = npc_tables.py's 1.99");
        }

        [Test]
        public void CarrierAndVosskBattleshipLateInTheGame()
        {
            // carrier: terran and nextInt(100) < 30 and campaign > 0x67 -> 0.24 x 0.3 = 7.2 %; Vossk: 24 % past 0x8c.
            InCompletedWorld(0);
            const int n = 4000;
            int carriers = 0, battleships = 0;
            Repeat(70, n, 25, list =>
            {
                int k = Kind(list);
                if (k == SpawnSpec.CapitalCarrier)
                {
                    carriers++;
                    var host = list.Single(x => x.capital == SpawnSpec.CapitalCarrier);
                    Assert.AreEqual("sn_carrier_terran_1", host.fixedObject);
                    Assert.AreEqual(2005, host.collisionId);
                    Assert.AreEqual(9999999, host.hitpoints);
                    Assert.AreEqual(8, list.Count(x => x.group == NpcGroup.Turret && x.capitalHost == host));
                }
                else if (k == SpawnSpec.CapitalBattleship) battleships++;
            });
            Assert.AreEqual(0.072, carriers / (double)n, 0.015);
            Assert.AreEqual(0.168, battleships / (double)n, 0.025);

            int vossk = 0;
            Repeat(15, n, 26, list =>
            {
                if (Kind(list) != SpawnSpec.CapitalVossk) return;
                vossk++;
                var host = list.Single(x => x.capital == SpawnSpec.CapitalVossk);
                Assert.AreEqual("sn_battleship_vossk", host.fixedObject);
                Assert.AreEqual(1667, host.nameText);
                Assert.AreEqual(2006, host.collisionId);
                Assert.AreEqual(5, list.Count(x => x.group == NpcGroup.Turret && x.capitalHost == host));
                // (nextInt(80000) - 40000, nextInt(40000) - 20000, nextInt(80000) + 60000)
                var p = host.position;
                Assert.That(p.x >= -40000 && p.x < 40000 && p.y >= -20000 && p.y < 20000 && p.z >= 60000 && p.z < 140000, p.ToString());
            });
            Assert.AreEqual(0.24, vossk / (double)n, 0.025);
        }

        [Test]
        public void NoVosskBattleshipBeforeCampaign0x8c()
        {
            InFreePlay(0);
            Repeat(15, 1500, 27, list => Assert.AreEqual(0, Kind(list)));
            InStory(0x8c);
            Repeat(15, 1500, 28, list => Assert.AreEqual(0, Kind(list)));
        }

        [Test]
        public void NoCapitalShipsInMidorianOrNivelianSystems()
        {
            InCompletedWorld(0);
            Repeat(20, 1000, 29, list => Assert.AreEqual(0, Kind(list)));
            Repeat(120, 1000, 30, list => Assert.AreEqual(0, Kind(list)));
        }

        [Test]
        public void CarrierUsesTheBattleshipsBox()
        {
            // Level::createMission: the carrier is placed around (+-40000, +-20000, 100000..180000), then moved like the
            // battleship by vtable 0x48, setPosition(x, y, z): (nextInt(80000) - 40000, nextInt(10000) - 5000,
            // nextInt(80000) + 40000) (PlayerFixedObject::setPosition(Vector&) forwards to that slot).
            InCompletedWorld(0);
            int carriers = 0;
            Repeat(70, 3000, 31, list =>
            {
                var host = list.FirstOrDefault(x => x.capital == SpawnSpec.CapitalCarrier);
                if (host == null) return;
                carriers++;
                var p = host.position;
                Assert.That(p.y >= -5000 && p.y < 5000 && p.z >= 40000 && p.z < 120000, p.ToString());
            });
            Assert.Greater(carriers, 0);
        }

        // ---- fleet battles (remake) ----------------------------------------------------------------------------

        [Test]
        public void FleetBattlesInThreePercentOfTerranOrbits()
        {
            InCompletedWorld(5);
            Settings.CapitalShips = true;
            const int n = 4000;
            int battles = 0;
            int union = Db.Systems.Find(s => s.index == 11).stations[0];   // Union, Average (Dis, 70, is in a Secure system)
            Repeat(union, n, 32, list =>
            {
                var hosts = list.Where(x => x.fleetBattle).ToList();
                if (hosts.Count == 0) return;
                battles++;
                Assert.AreEqual(2, hosts.Count);
                Assert.AreEqual(2, list.Count(x => x.capital != 0 && x.capitalHost == null), "no single capital ship besides");
                Assert.AreSame(hosts[1], hosts[0].battleFoe);
                Assert.AreSame(hosts[0], hosts[1].battleFoe);
            });
            Assert.AreEqual(0.03, battles / (double)n, 0.012);
        }

        [Test]
        public void NoFleetBattlesWithoutTheOptionOrBeforeStep103()
        {
            InCompletedWorld(5);
            Repeat(70, 2000, 33, list => Assert.That(list.All(x => !x.fleetBattle)), default);
            Settings.CapitalShips = true;
            InFreePlay(5);   // free play outside the finished world: never
            Repeat(70, 2000, 34, list => Assert.That(list.All(x => !x.fleetBattle)));
            InCompletedWorld(5);
            Repeat(20, 2000, 35, list => Assert.That(list.All(x => !x.fleetBattle)), default);   // Midorian
            Repeat(105, 500, 36, list => Assert.That(list.All(x => !x.fleetBattle)));   // Loma
        }

        [Test]
        public void NoFleetBattlesInSecureSystems()
        {
            // a player's report 2026-10-16: a war in the safe orbits. Magnetar (14) is a Secure Terran system.
            InCompletedWorld(5);
            Settings.CapitalShips = true;
            var magnetar = Db.Systems.Find(s => s.index == 14);
            Assert.AreEqual(TrafficPlan.SecureSystem, magnetar.securityLevel);
            foreach (int st in magnetar.stations)
                Repeat(st, 1500, 37, list => Assert.That(list.All(x => !x.fleetBattle)), default);
        }
    }
}
