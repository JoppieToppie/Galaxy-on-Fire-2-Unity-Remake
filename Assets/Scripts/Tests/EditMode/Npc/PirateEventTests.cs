// PirateEventTests.cs
// Feature: CLAUDE.md "NPCs and combat" > Pirate events (remake, GitHub #6), Threat tiers, and the event part of the Pirate
// activity board (the reported sighting spawning in its orbit, the bounty by tier): PirateEventRules.Allowed and its
// chances, TrafficPlan.AddPirateEventShips / Build's event roll, PirateReports.RollTier / Bounty.
// Expected values: CLAUDE.md (remake-only, so the documentation is the source): 15 % Dangerous / 7 % Risky, 5 % / 2 % with
// the board, never Average / Secure, not below rank 2, not before campaign 0x10, Mido in free play, the storylines'
// stations (story.json's), the special orbits, systems 25 / 27 / 32 / 33, a pirate base's system; outposts 110-160 km out,
// x0.4 .. x2 hull, guards base -3 / -1 / 0 / +3 / +6, turrets 0 / 0 / 2 / 4 / 6, crates by tier (half the base's goods /
// those / + 2-4 t rare goods / a piece of equipment by rank + 3-6 t Implants or Vossk Organs / two of the best + 8-12 t);
// the boss: (15 rank + 1680) x difficulty, x0.35 .. x2.4, gun x1 .. x3, 0 / 1 / 2-3 / 4-5 / 6-7 escorts, 1-2 .. 10-16 t,
// speed 3.5 (4 at tier 5), 30 km further out than the raiders, ships Darkzov / Mantis / Wasp / Tyrion; bounties
// (outpost 10 000 / boss 4000 + 1500 per rank) x0.5 / 0.8 / 1 / 1.5 / 2.5, a report x1.25, in hundreds; tiers: below rank
// 4 never 5; Dangerous systems lean high. Players' feedback 2026-10-22: a boss's stash rolls 0 / 0 / 1 / 2 / 3 and from
// tier 4 the outpost's equipment, both kinds' munitions stack from tier 4, the add-ons' gear in the finished world only. The outpost hull is kaamo_club.md 3.2's 5 x (4 cm + 15 rank + 20). Where only
// the code defines a number (the middle tiers' factors) the tests check order (monotonic) instead.

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class PirateEventTests : NpcTestBase
    {
        static bool Allowed(int station, bool anyRank = false) => PirateEventRules.Allowed(Db, station, SystemOf(station), anyRank);

        // ---- where -----------------------------------------------------------------------------------------------

        [Test]
        public void EventChances()
        {
            CollectionAssert.AreEqual(new[] { 15, 7 }, PirateEventRules.EventChance);
            CollectionAssert.AreEqual(new[] { 5, 2 }, PirateEventRules.UnreportedChance);
        }

        [Test]
        public void OffWithoutTheOption()
        {
            InFreePlay(5);
            Assert.IsFalse(Allowed(20));
            Settings.PirateEvents = true;
            Assert.IsTrue(Allowed(20));
            Assert.IsFalse(PirateEventRules.Allowed(Db, 20, null));
        }

        [Test]
        public void DangerousAndRiskySystemsOnly()
        {
            Settings.PirateEvents = true;
            InFreePlay(5);
            Assert.IsTrue(Allowed(20), "Eanya, Dangerous");
            Assert.IsTrue(Allowed(5), "Pan, Risky");
            Assert.IsFalse(Allowed(15), "V'ikka, Average");
            Assert.IsFalse(Allowed(71), "Magnetar, Secure");
        }

        [Test]
        public void NotBelowRank2UnlessReported()
        {
            Settings.PirateEvents = true;
            InFreePlay(1);
            Assert.IsFalse(Allowed(20));
            Assert.IsTrue(Allowed(20, true));
            InFreePlay(2);
            Assert.IsTrue(Allowed(20));
        }

        [Test]
        public void NotBeforeCampaign0x10InTheStory()
        {
            Settings.PirateEvents = true;
            InStory(0x0f, 2);
            Assert.IsFalse(Allowed(20));
            InStory(0x10, 2);
            Assert.IsTrue(Allowed(20));
        }

        [Test]
        public void MidoOnlyOnceKeithHasLeft()
        {
            Settings.PirateEvents = true;
            InFreePlay(5);
            Assert.IsFalse(Allowed(75), "free play keeps Mido quiet");
            InStory(20, 2);
            Assert.IsTrue(Allowed(75));
        }

        [Test]
        public void NotAtTheStorylinesStations()
        {
            // story.json sends the player to 22 (Eanya) and 30 (Behén) at some step; 20 / 21 are never a step's station.
            Settings.PirateEvents = true;
            InFreePlay(5);
            Assert.IsTrue(PirateEventRules.InStoryline(22));
            Assert.IsFalse(Allowed(22));
            Assert.IsFalse(PirateEventRules.InStoryline(21));
            Assert.IsTrue(Allowed(21));
            Session.VoidInvasionStation = 21;
            Assert.IsFalse(Allowed(21), "the Void-attacked station");
            Session.VoidInvasionStation = -1;
            Session.StoryTargets.Add(21);
            Assert.IsFalse(Allowed(21), "step 59's targets");
        }

        [TestCase(10)]
        [TestCase(100)]
        [TestCase(101)]
        [TestCase(102)]
        [TestCase(103)]
        [TestCase(104)]
        [TestCase(108)]
        [TestCase(105)]
        [TestCase(106)]
        [TestCase(107)]
        [TestCase(132)]
        [TestCase(133)]
        [TestCase(134)]
        public void NotInTheSpecialOrbits(int station)
        {
            // the empty orbits, the dropship orbits, Shima (Kaamo), Loma (25), the pirate loot systems (32 / 33)
            Settings.PirateEvents = true;
            InFreePlay(5);
            Assert.IsFalse(Allowed(station));
            Assert.IsTrue(Allowed(115), "Talidor (115), Dangerous, no storyline: allowed for comparison");
        }

        [Test]
        public void NotInTheSupernovaSystem()
        {
            Settings.PirateEvents = true;
            InFreePlay(5);
            var ginoya = Db.Systems.Find(s => s.index == 27);
            foreach (int st in ginoya.stations) Assert.IsFalse(PirateEventRules.Allowed(Db, st, ginoya));
        }

        [Test]
        public void NotInAPirateBaseSystemUntilTheBaseFalls()
        {
            Settings.PirateEvents = true;
            InFreePlay(5);
            Assert.IsFalse(Allowed(31), "Behén has a base");
            Session.PirateBaseDestroyed[1] = true;
            Assert.IsTrue(Allowed(31));
        }

        // ---- how often (Build) -------------------------------------------------------------------------------------

        /// <summary>Events among the visits without a raider group ('visits' counts those; an unreported event never comes
        /// with raiders).</summary>
        static (int outposts, int bosses, int visits) CountEvents(int station, int n, int seed)
        {
            int o = 0, b = 0, v = 0;
            Repeat(station, n, seed, list =>
            {
                bool raiders = list.Any(x => x.group == NpcGroup.Raider);
                bool outpost = list.Any(x => x.pirateEvent == SpawnSpec.EventOutpost), boss = list.Any(x => x.pirateEvent == SpawnSpec.EventBoss);
                if (raiders) { Assert.IsFalse(outpost || boss, "an unreported event on top of a raider group"); return; }
                v++;
                if (outpost) o++;
                if (boss) b++;
            });
            return (o, b, v);
        }

        [Test]
        public void UnannouncedEventsWithTheBoard()
        {
            // the board is part of the option: Dangerous 5 %, Risky 2 % of the visits without raiders, half outposts, half
            // bosses.
            Settings.PirateEvents = true;
            InFreePlay(5);
            var d = CountEvents(20, 18000, 1);
            Assert.AreEqual(0.05, (d.outposts + d.bosses) / (double)d.visits, 0.015);
            Assert.AreEqual(0.5, d.outposts / (double)(d.outposts + d.bosses), 0.15);
            var r = CountEvents(5, 12000, 2);
            Assert.AreEqual(0.02, (r.outposts + r.bosses) / (double)r.visits, 0.008);
            var none = CountEvents(15, 2000, 3);
            Assert.AreEqual(0, none.outposts + none.bosses, "Average");
        }

        [Test]
        public void NoEventsWithTheOptionOff()
        {
            InFreePlay(5);
            var c = CountEvents(20, 2000, 4);
            Assert.AreEqual(0, c.outposts + c.bosses);
        }

        static PirateSighting Sighting(int station, bool boss, int tier) => new PirateSighting
        {
            id = 7, boss = boss, tier = tier, station = station, system = SystemOf(station).index, name = boss ? "Test Boss" : null,
            reportedAt = 0f, expiresAt = 1e9f, bounty = 1000,
        };

        [Test]
        public void AReportedBossSpawnsWhateverTheRank()
        {
            Settings.PirateEvents = true;
            InFreePlay(0);
            Session.PirateSightings.Add(Sighting(20, true, 4));
            Random.InitState(5);
            for (int i = 0; i < 20; i++)
            {
                var list = Build(20);
                var boss = list.Single(x => x.pirateEvent == SpawnSpec.EventBoss);
                Assert.AreEqual(7, boss.sightingId);
                Assert.AreEqual(4, boss.eventTier);
                Assert.AreEqual("Test Boss", boss.name);
                Assert.IsFalse(list.Any(x => x.pirateEvent == SpawnSpec.EventOutpost));
            }
        }

        [Test]
        public void NoEventInAnInformerOrbit()
        {
            // A pirate killed in the Informer's orbit spoiled the mission; the orbit is the spy and the police alone.
            Settings.PirateEvents = true;
            InFreePlay(0);
            Session.PirateSightings.Add(Sighting(20, true, 4));
            Session.FreelanceMission = new FreelanceMission { type = MissionType.Informer, target = 20 };
            Random.InitState(5);
            for (int i = 0; i < 20; i++) Assert.IsFalse(Build(20).Any(x => x.pirateEvent != 0));
            Session.FreelanceMission = new FreelanceMission();
            Assert.IsTrue(Build(20).Any(x => x.pirateEvent == SpawnSpec.EventBoss), "the report waits for a later visit");
        }

        [Test]
        public void AReportedEventComesEvenWithRaiders()
        {
            // Only the unannounced roll gives way to a raider group (players' report 2026-10-16); a report is a contract.
            Settings.PirateEvents = true;
            InFreePlay(5);
            Session.PirateSightings.Add(Sighting(20, true, 3));
            Random.InitState(8);
            int withRaiders = 0;
            for (int i = 0; i < 200; i++)
            {
                var list = Build(20);
                Assert.IsTrue(list.Any(x => x.pirateEvent == SpawnSpec.EventBoss));
                if (list.Any(x => x.group == NpcGroup.Raider)) withRaiders++;
            }
            Assert.Greater(withRaiders, 0);
        }

        [Test]
        public void AReportedOutpostSpawnsItsTier()
        {
            Settings.PirateEvents = true;
            InFreePlay(0);
            Session.PirateSightings.Add(Sighting(21, false, 2));
            Random.InitState(6);
            var outpost = Build(21).Single(x => x.pirateEvent == SpawnSpec.EventOutpost);
            Assert.AreEqual(7, outpost.sightingId);
            Assert.AreEqual(2, outpost.eventTier);
        }

        [Test]
        public void ExpiredOrDoneReportsSpawnNothing()
        {
            Settings.PirateEvents = true;
            InFreePlay(0);   // rank 0: no unannounced roll either
            var s = Sighting(20, true, 3);
            s.expiresAt = -1f;
            Session.PirateSightings.Add(s);
            Assert.AreEqual(0, CountEvents(20, 200, 7).bosses);
            s.expiresAt = 1e9f;
            s.done = true;
            Assert.AreEqual(0, CountEvents(20, 200, 8).bosses);
            s.done = false;
            Settings.PirateEvents = false;
            Assert.AreEqual(0, CountEvents(20, 200, 9).bosses, "the option off");
        }

        // ---- the outpost by tier -----------------------------------------------------------------------------------

        static List<SpawnSpec> Event(bool outpost, int tier)
        {
            var list = new List<SpawnSpec>();
            TrafficPlan.AddPirateEventShips(list, outpost, tier);
            return list;
        }

        [TestCase(1, 350, 2, 0)]
        [TestCase(2, -1, 4, 0)]
        [TestCase(3, 875, 5, 2)]
        [TestCase(4, -1, 8, 4)]
        [TestCase(5, 1750, 11, 6)]
        public void OutpostByTier(int tier, int hull, int guards, int turrets)
        {
            // rank 5, campaign 20, Normal: the outpost's 5 x (80 + 75 + 20) = 875, x0.4 / x1 / x2 at tiers 1 / 3 / 5;
            // guards 5 + (-3 / -1 / 0 / +3 / +6), turrets 0 / 0 / 2 / 4 / 6.
            InFreePlay(5);
            Assert.AreEqual(875, KaamoClub.OutpostHull());
            Random.InitState(10 + tier);
            for (int k = 0; k < 10; k++)
            {
                var list = Event(true, tier);
                var o = list[0];
                Assert.AreEqual(SpawnSpec.EventOutpost, o.pirateEvent);
                Assert.AreEqual(NpcGroup.Outpost, o.group);
                Assert.AreEqual(tier, o.eventTier);
                Assert.AreEqual("station_pirates", o.fixedObject);
                Assert.AreEqual(1002, o.collisionId);
                Assert.AreEqual(441, o.nameText);
                Assert.IsTrue(o.asleep && o.alwaysEnemy && o.stationary);
                if (hull > 0) Assert.AreEqual(hull, o.hitpoints);
                float r = o.position.magnitude;
                Assert.That(r >= 109999f && r <= 160001f, r.ToString());
                var g = list.Where(x => x.group == NpcGroup.Guard).ToList();
                Assert.AreEqual(guards, g.Count);
                Assert.That(g.All(x => x.asleep && x.guard && x.race == Standing.Pirate));
                var t = list.Where(x => x.pirateEvent == SpawnSpec.EventTurret).ToList();
                Assert.AreEqual(turrets, t.Count);
                foreach (var x in t)
                {
                    Assert.AreEqual(Standing.Pirate, x.race);
                    Assert.IsTrue(x.alwaysEnemy);
                    Assert.AreEqual("turret_002_static", x.turretAssembly);
                    Assert.Less((x.position - o.position).magnitude, 12000f, "on the outpost");
                }
            }
        }

        [Test]
        public void OutpostHullGrowsWithTheTier()
        {
            InFreePlay(5);
            var hulls = Enumerable.Range(1, 5).Select(t => Event(true, t)[0].hitpoints).ToArray();
            for (int i = 1; i < 5; i++) Assert.Greater(hulls[i], hulls[i - 1]);
        }

        [TestCase(Session.DifficultyEasy, 1, 2, 8)]
        [TestCase(Session.DifficultyHard, 4, 7, 13)]
        [TestCase(Session.DifficultyExtreme, 7, 10, 16)]
        public void OutpostGuardsFollowTheDifficulty(float difficulty, int tier1, int tier3, int tier5)
        {
            // the pirate base's int((d - 0.5) * 5 + 5) = 2 / 7 / 10, then - 3 / + 0 / + 6; at least 1.
            InFreePlay(5);
            Session.Difficulty = difficulty;
            Assert.AreEqual(tier1, Event(true, 1).Count(x => x.group == NpcGroup.Guard));
            Assert.AreEqual(tier3, Event(true, 3).Count(x => x.group == NpcGroup.Guard));
            Assert.AreEqual(tier5, Event(true, 5).Count(x => x.group == NpcGroup.Guard));
        }

        /// <summary>The pirates' stash after the tier's own 'tierStacks': freighter cargo, each kind once, not the tier's.</summary>
        static int Stash(SpawnSpec outpost, int tierStacks)
        {
            var kinds = outpost.extraLoot.Select(x => x.item).Append(outpost.lootItem).ToList();
            CollectionAssert.AllItemsAreUnique(kinds);
            int tons = 0;
            for (int i = tierStacks; i < outpost.extraLoot.Count; i++) { Assert.Greater(outpost.extraLoot[i].amount, 0); tons += outpost.extraLoot[i].amount; }
            return tons;
        }

        [Test]
        public void OutpostCratesByTier()
        {
            InFreePlay(5);
            Random.InitState(20);
            var baseLoot = PirateBases.Loot.Select(l => l.item).ToArray();
            var stash = new int[5];
            for (int k = 0; k < 40; k++)
            {
                var t1 = Event(true, 1)[0];
                int i1 = System.Array.IndexOf(baseLoot, t1.lootItem);
                Assert.GreaterOrEqual(i1, 0);
                Assert.GreaterOrEqual(t1.lootAmount, Mathf.Max(1, PirateBases.Loot[i1].amount / 2), "half the base's goods (and a stash roll of it)");
                stash[0] += Stash(t1, 0);

                var t2 = Event(true, 2)[0];
                Assert.Contains(t2.lootItem, baseLoot);
                stash[1] += Stash(t2, 0);

                var t3 = Event(true, 3)[0];
                Assert.Contains(t3.lootItem, baseLoot);
                stash[2] += Stash(t3, 1);
                CollectionAssert.Contains(new[] { 113, 108, 107, 102, 101 }, t3.extraLoot[0].item);
                Assert.That(t3.extraLoot[0].amount >= 2 && t3.extraLoot[0].amount <= 4);

                var t4 = Event(true, 4)[0];   // rank 5: the lower table
                CollectionAssert.Contains(Low, t4.lootItem);
                Assert.GreaterOrEqual(t4.lootAmount, 1);
                stash[3] += Stash(t4, 2);
                CollectionAssert.Contains(new[] { 113, 108 }, t4.extraLoot[0].item);
                Assert.That(t4.extraLoot[0].amount >= 3 && t4.extraLoot[0].amount <= 6);
                AssertMunitions(t4.extraLoot[1], false);

                var t5 = Event(true, 5)[0];   // the best equipment whatever the rank, two different pieces
                CollectionAssert.Contains(High, t5.lootItem);
                stash[4] += Stash(t5, 3);
                CollectionAssert.Contains(High, t5.extraLoot[0].item);
                Assert.AreNotEqual(t5.lootItem, t5.extraLoot[0].item);
                CollectionAssert.Contains(new[] { 113, 108 }, t5.extraLoot[1].item);
                Assert.That(t5.extraLoot[1].amount >= 8 && t5.extraLoot[1].amount <= 12);
                AssertMunitions(t5.extraLoot[2], true);
            }
            // a player's report 2026-10-16 (an outpost gave 1 t of something): a freighter's cargo rolls by tier, 1 / 2 / 2 / 3 / 4
            CollectionAssert.AreEqual(new[] { 1, 2, 2, 3, 4 }, TrafficPlan.OutpostStashRolls);
            for (int t = 0; t < 5; t++) Assert.Greater(stash[t] / 40f, 8f * TrafficPlan.OutpostStashRolls[t] * 0.8f, "tier " + (t + 1));
            Assert.Greater(stash[4], stash[0]);
            InFreePlay(10);
            for (int k = 0; k < 20; k++) CollectionAssert.Contains(High, Event(true, 4)[0].lootItem, "rank 10: the upper table");
        }

        [Test]
        public void HighTierGuardsFlyTheDearerPirates()
        {
            InFreePlay(5);
            var dearer = Pool(8).OrderByDescending(s => Db.Ship(s).price).Take(4).ToArray();
            Random.InitState(21);
            for (int k = 0; k < 20; k++)
                foreach (var g in Event(true, 4).Where(x => x.group == NpcGroup.Guard)) CollectionAssert.Contains(dearer, g.ship);
        }

        [Test]
        public void TiersAreClamped()
        {
            InFreePlay(5);
            Assert.AreEqual(1, Event(true, 0)[0].eventTier);
            Assert.AreEqual(5, Event(true, 9)[0].eventTier);
            Assert.AreEqual(5, PirateReports.MaxTier);
        }

        // ---- the boss by tier ---------------------------------------------------------------------------------------

        [TestCase(1, 614, 1f, 0, 0, 1, 2)]
        [TestCase(3, 1755, -1f, 2, 3, -1, -1)]
        [TestCase(5, 4212, 3f, 6, 7, 10, 16)]
        public void BossByTier(int tier, int hull, float gun, int minEscorts, int maxEscorts, int minLoot, int maxLoot)
        {
            // rank 5, Normal: (15 x 5 + 1680) = 1755, x0.35 / x1 / x2.4; gun x1 .. x3; escorts 0 / 2-3 / 6-7; loot 1-2 .. 10-16.
            InFreePlay(5);
            Random.InitState(30 + tier);
            for (int k = 0; k < 30; k++)
            {
                var list = Event(false, tier);
                var b = list[0];
                Assert.AreEqual(SpawnSpec.EventBoss, b.pirateEvent);
                Assert.AreEqual(tier, b.eventTier);
                Assert.AreEqual(hull, b.hitpoints);
                if (gun > 0) Assert.AreEqual(gun, b.gunFactor, 1e-5f);
                Assert.AreEqual(1606, b.nameText);
                Assert.IsTrue(b.alwaysEnemy);
                Assert.AreEqual(Standing.Pirate, b.race);
                CollectionAssert.Contains(new[] { 60, 29, 32, 25 }, b.ship);
                Assert.AreEqual(15, b.gunItem, "rank 5..9: the H'nookk");
                Assert.AreEqual(tier == 5 ? 4f : 3.5f, b.speed);
                // the raiders' point (+-50000, 0, 50000..100000) + 30000 further out
                Assert.That(b.position.x >= -50000 && b.position.x < 50000 && b.position.y == 0f && b.position.z >= 80000 && b.position.z < 130000, b.position.ToString());
                if (minLoot > 0) Assert.That(b.lootAmount >= minLoot && b.lootAmount <= maxLoot, b.lootAmount.ToString());
                CollectionAssert.Contains(tier >= 4 ? new[] { 113, 108 } : new[] { 113, 108, 107, 102, 101 }, b.lootItem);
                var esc = list.Skip(1).ToList();
                Assert.That(esc.Count >= minEscorts && esc.Count <= maxEscorts, esc.Count.ToString());
                foreach (var e in esc)
                {
                    Assert.AreEqual(NpcGroup.Special, e.group);
                    Assert.IsTrue(e.alwaysEnemy);
                    Assert.AreEqual(Standing.Pirate, e.race);
                    Assert.Less((e.position - b.position).magnitude, 20000f * 1.74f);
                }
            }
        }

        [Test]
        public void BossEscortsLootHullAndGunGrowWithTheTier()
        {
            InFreePlay(5);
            Random.InitState(40);
            int[] escorts = new int[5], hulls = new int[5], lootMax = new int[5];
            float[] guns = new float[5];
            for (int t = 1; t <= 5; t++)
                for (int k = 0; k < 40; k++)
                {
                    var list = Event(false, t);
                    escorts[t - 1] = Mathf.Max(escorts[t - 1], list.Count - 1);
                    hulls[t - 1] = list[0].hitpoints;
                    guns[t - 1] = list[0].gunFactor;
                    lootMax[t - 1] = Mathf.Max(lootMax[t - 1], list[0].lootAmount);
                }
            Assert.AreEqual(1, escorts[1], "tier 2: one escort");
            Assert.AreEqual(5, escorts[3], "tier 4: 4-5");
            for (int i = 1; i < 5; i++)
            {
                Assert.Greater(hulls[i], hulls[i - 1]);
                Assert.Greater(guns[i], guns[i - 1]);
                Assert.Greater(escorts[i], escorts[i - 1]);
                Assert.Greater(lootMax[i], lootMax[i - 1]);
            }
        }

        // ---- the equipment and munitions (players' feedback 2026-10-22) ---------------------------------------------

        static readonly int[] Low = { 8, 21, 30, 27, 53, 25 }, High = { 11, 58, 59, 49, 54, 67, 90 };
        static readonly int[] AddOnLow = { 177, 180 }, AddOnHigh = { 178, 183, 193, 182, 224, 223, 198, 199 };

        static void AssertMunitions(ItemStack m, bool high, bool addOns = false)
        {
            // Amour Rocket / Intelli Jet 20-40; from rank 10 or tier 5 Intelli Jet 30-50 / AMR Extinctor 15-30; the finished
            // world adds the SG-100 3-6, from 10 the Shock Blast 5-10 / SG-400 3-6.
            var (min, max) = m.item switch
            {
                33 when !high => (20, 40), 37 when !high => (20, 40), 211 when !high && addOns => (3, 6),
                37 when high => (30, 50), 46 when high => (15, 30), 226 when high && addOns => (5, 10), 212 when high && addOns => (3, 6),
                _ => (-1, -1),
            };
            Assert.Greater(min, 0, $"munitions {m.item} (high {high}, add-ons {addOns})");
            Assert.That(m.amount >= min && m.amount <= max, $"{m.item} x{m.amount}");
        }

        [Test]
        public void BossLootByTier()
        {
            // Tier 1 / 2 goods alone; tier 3 a stash; tier 4 / 5 the outpost's equipment (one / two pieces) and munitions, then
            // the stash (one roll fewer than an outpost's).
            InFreePlay(5);
            CollectionAssert.AreEqual(new[] { 0, 0, 1, 2, 3 }, TrafficPlan.BossStashRolls);
            Random.InitState(50);
            for (int k = 0; k < 40; k++)
            {
                for (int t = 1; t <= 2; t++) Assert.IsNull(Event(false, t)[0].extraLoot, "tier " + t);
                var t3 = Event(false, 3)[0];
                Assert.IsNotNull(t3.extraLoot);
                Assert.Greater(Stash(t3, 0), 0);

                var t4 = Event(false, 4)[0];
                CollectionAssert.Contains(Low, t4.extraLoot[0].item);
                Assert.AreEqual(1, t4.extraLoot[0].amount);
                AssertMunitions(t4.extraLoot[1], false);
                Assert.Greater(Stash(t4, 2), 0);

                var t5 = Event(false, 5)[0];
                CollectionAssert.Contains(High, t5.extraLoot[0].item);
                CollectionAssert.Contains(High, t5.extraLoot[1].item);
                Assert.AreNotEqual(t5.extraLoot[0].item, t5.extraLoot[1].item);
                AssertMunitions(t5.extraLoot[2], true);
                Assert.Greater(Stash(t5, 3), 0);
            }
        }

        [Test]
        public void TheAddOnsGearDropsInTheFinishedWorldOnly()
        {
            // A single player meets the Valkyrie / Supernova gear in those campaigns: only multiplayer's finished world
            // (Session.CompletedWorld) adds it to the tables, and never the Kaamo Club's own or a time extender.
            InFreePlay(5);
            Random.InitState(60);
            var seen = new HashSet<int>();
            for (int k = 0; k < 200; k++)
            {
                seen.UnionWith(TrafficPlan.OutpostGear(Db, 4, 5));
                seen.UnionWith(TrafficPlan.OutpostGear(Db, 5, 5));
                AssertMunitions(TrafficPlan.Munitions(4, 5), false);
                AssertMunitions(TrafficPlan.Munitions(5, 5), true);
            }
            CollectionAssert.AreEquivalent(Low.Concat(High), seen);
            Session.CompletedWorld = true;
            try
            {
                seen.Clear();
                var munitions = new HashSet<int>();
                for (int k = 0; k < 400; k++)
                {
                    seen.UnionWith(TrafficPlan.OutpostGear(Db, 4, 5));
                    seen.UnionWith(TrafficPlan.OutpostGear(Db, 5, 5));
                    var m4 = TrafficPlan.Munitions(4, 5);
                    var m5 = TrafficPlan.Munitions(5, 5);
                    AssertMunitions(m4, false, true);
                    AssertMunitions(m5, true, true);
                    munitions.Add(m4.item); munitions.Add(m5.item);
                }
                CollectionAssert.AreEquivalent(Low.Concat(High).Concat(AddOnLow).Concat(AddOnHigh), seen);
                CollectionAssert.AreEquivalent(new[] { 33, 37, 211, 46, 226, 212 }, munitions);
                foreach (int i in seen)
                {
                    Assert.AreNotEqual(Shop.TimeExtenderCategory, Db.Item(i).categoryId);
                    CollectionAssert.DoesNotContain(new[] { 200, 220, 208, 213, 216, 228, 229, 230, 231 }, i, "the Kaamo Club's own");
                }
            }
            finally { Session.CompletedWorld = false; }
        }

        [TestCase(0, 23)]
        [TestCase(4, 23)]
        [TestCase(5, 15)]
        [TestCase(10, 26)]
        [TestCase(15, 21)]
        [TestCase(20, 21)]
        public void BossGunByRank(int rank, int item)
        {
            // Micro Gun MKII (23), H'nookk (15), Scram Cannon (26), Tyrfing (21) at < 5 / < 10 / < 15 / else.
            InFreePlay(rank);
            Assert.AreEqual(item, Event(false, 3)[0].gunItem);
        }

        [Test]
        public void BossHullScalesWithDifficulty()
        {
            InFreePlay(5);
            Session.Difficulty = Session.DifficultyExtreme;
            Assert.AreEqual(3510, Event(false, 3)[0].hitpoints, "1755 x2");
        }

        [Test]
        public void ReportedBossKeepsItsName()
        {
            InFreePlay(5);
            var list = new List<SpawnSpec>();
            TrafficPlan.AddPirateEventShips(list, false, 3, new PirateSighting { id = 3, boss = true, name = "Vex", tier = 3 });
            Assert.AreEqual("Vex", list[0].name);
            Assert.AreEqual(3, list[0].sightingId);
            Assert.AreEqual(-1, Event(false, 3)[0].sightingId);
            Assert.IsNull(Event(false, 3)[0].name);
        }

        // ---- tiers and bounties ------------------------------------------------------------------------------------

        static int[] Tiers(int security, int rank, int n, int seed)
        {
            Random.InitState(seed);
            var c = new int[6];
            for (int i = 0; i < n; i++)
            {
                int t = PirateReports.RollTier(security, rank);
                Assert.That(t >= 1 && t <= 5, t.ToString());
                c[t]++;
            }
            return c;
        }

        static double Mean(int[] c) => Enumerable.Range(1, 5).Sum(t => t * c[t]) / (double)c.Sum();

        [Test]
        public void NoTierFiveBelowRank4()
        {
            Assert.AreEqual(0, Tiers(0, 3, 4000, 50)[5]);
            Assert.AreEqual(0, Tiers(1, 0, 4000, 51)[5]);
            Assert.Greater(Tiers(0, 4, 4000, 52)[5], 0);
        }

        [Test]
        public void DangerousSystemsLeanHigh()
        {
            Assert.Greater(Mean(Tiers(0, 8, 6000, 53)), Mean(Tiers(1, 8, 6000, 54)) + 0.4);
        }

        [Test]
        public void BelowRank4TierFourIsRarer()
        {
            var low = Tiers(0, 3, 8000, 55);
            var mid = Tiers(0, 8, 8000, 56);
            Assert.Less(low[4] / 8000.0, mid[4] / 8000.0 * 0.6);
        }

        [Test]
        public void FromRank12TierOneIsRarerAndTierFiveCommoner()
        {
            var mid = Tiers(1, 8, 8000, 57);
            var high = Tiers(1, 12, 8000, 58);
            Assert.Less(high[1], mid[1]);
            Assert.Greater(high[5], mid[5]);
        }

        [TestCase(true, 0, 1, false, 5000)]
        [TestCase(true, 10, 3, false, 25000)]
        [TestCase(true, 10, 5, false, 62500)]
        [TestCase(false, 10, 5, false, 47500)]
        [TestCase(false, 0, 2, false, 3200)]
        [TestCase(false, 20, 4, false, 51000)]
        [TestCase(true, 4, 3, true, 20000)]
        [TestCase(true, 30, 3, false, 40000)]
        public void BountyByTier(bool outpost, int rank, int tier, bool contract, int bounty)
        {
            // (outpost 10 000 / boss 4000 + 1500 x min(rank, 20)) x 0.5 / 0.8 / 1 / 1.5 / 2.5, a report x1.25, in hundreds.
            Assert.AreEqual(bounty, PirateReports.Bounty(outpost, rank, tier, contract));
        }
    }
}
