// PirateReportsTests.cs
// Feature: CLAUDE.md "NPCs and combat", Threat tiers and the Pirate activity board (PirateReports, remake-only, part of the
// option "Pirate outposts and bosses"): tiers by security and rank, the bounty (outpost 10 000 / boss 4000 + 1500 per rank,
// x0.5 / 0.8 / 1 / 1.5 / 2.5 by tier, a board contract x1.25), 3-5 sightings (3 + rank / 8), one per system, 40-90 min on
// the board, Done, the ticker and the warning, the session's server board.
// Expected values: CLAUDE.md (the remake defines this feature: numbers quoted there); PirateEventRules for where events may
// happen (Dangerous / Risky systems, rank 2+, not the pirate base systems, Mido in free play, the special orbits or the
// storylines' stations). Tier draws are statistical properties of the rules described there.

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class PirateReportsTests : StoryAreaTestBase
    {
        SettingsScope<bool> pirateEvents;

        [SetUp]
        public void EventsOn()
        {
            pirateEvents = new SettingsScope<bool>(() => Settings.PirateEvents, v => Settings.PirateEvents = v);
            Settings.PirateEvents = true;
            PirateReports.ServerReset();
        }

        [TearDown]
        public void EventsBack()
        {
            PirateReports.ServerReset();
            pirateEvents.Dispose();
        }

        static PirateSighting Sighting(int id, int station, int system, bool boss = false, float life = 3000f, int tier = 3) =>
            new PirateSighting { id = id, station = station, system = system, boss = boss, tier = tier, name = boss ? "Red Vex" : "",
                                 bounty = 10000 + id, reportedAt = PirateReports.Now, expiresAt = PirateReports.Now + life };

        // ---- bounties and tiers -------------------------------------------------------------------------------

        [TestCase(true, 0, 3, false, 10000)]
        [TestCase(false, 0, 3, false, 4000)]
        [TestCase(true, 4, 3, false, 16000)]
        [TestCase(false, 10, 5, false, 47500)]
        [TestCase(true, 4, 2, true, 16000)]
        [TestCase(true, 0, 1, false, 5000)]
        [TestCase(false, 1, 2, false, 4400)]
        [TestCase(false, 3, 1, true, 5300)]
        [TestCase(true, 20, 4, false, 60000)]
        [TestCase(true, 25, 3, false, 40000)]
        [TestCase(true, -3, 3, false, 10000)]
        [TestCase(true, 0, 0, false, 5000)]
        [TestCase(true, 0, 9, false, 25000)]
        public void BountyFormula(bool outpost, int rank, int tier, bool contract, int expected)
        {
            Assert.AreEqual(expected, PirateReports.Bounty(outpost, rank, tier, contract));
        }

        [Test]
        public void BountiesAreRoundedTo100()
        {
            for (int rank = 0; rank <= 20; rank++)
                for (int tier = 1; tier <= 5; tier++)
                {
                    Assert.AreEqual(0, PirateReports.Bounty(true, rank, tier, true) % 100);
                    Assert.AreEqual(0, PirateReports.Bounty(false, rank, tier, false) % 100);
                    Assert.Greater(PirateReports.Bounty(true, rank, tier, false), PirateReports.Bounty(false, rank, tier, false), "an outpost pays more");
                }
        }

        static float[] TierShares(int security, int rank, int n = 6000)
        {
            var c = new float[6];
            for (int k = 0; k < n; k++) c[PirateReports.RollTier(security, rank)]++;
            return c.Select(x => x / n).ToArray();
        }

        [Test]
        public void TiersRange()
        {
            for (int k = 0; k < 2000; k++)
                Assert.That(PirateReports.RollTier(k % 2, k % 21), Is.InRange(1, PirateReports.MaxTier));
        }

        [Test]
        public void NoTierFiveBelowRank4()
        {
            for (int rank = 0; rank < 4; rank++)
                for (int k = 0; k < 1500; k++) Assert.AreNotEqual(5, PirateReports.RollTier(0, rank));
        }

        [Test]
        public void DangerousSystemsLeanHigh()
        {
            float Mean(float[] s) => Enumerable.Range(1, 5).Sum(t => t * s[t]);
            Assert.Greater(Mean(TierShares(0, 8)), Mean(TierShares(1, 8)) + 0.5f);
        }

        [Test]
        public void LowRanksSeeTierFourLessAndHighRanksTierOneLess()
        {
            var low = TierShares(0, 2);
            var mid = TierShares(0, 8);
            var high = TierShares(0, 15);
            Assert.Less(low[4], mid[4] * 0.6f, "tier 4 a third as often below rank 4");
            Assert.Less(high[1], mid[1] * 0.75f, "tier 1 half as often from rank 12");
            Assert.Greater(high[5], mid[5], "tier 5 more often from rank 12");
        }

        [Test]
        public void TierNames()
        {
            var names = Enumerable.Range(1, 5).Select(PirateReports.TierName).ToList();
            Assert.AreEqual(5, names.Distinct().Count());
            Assert.IsTrue(names.TrueForAll(n => !string.IsNullOrEmpty(n)));
            Assert.AreEqual(PirateReports.TierName(1), PirateReports.TierName(0), "clamped");
            Assert.AreEqual(PirateReports.TierName(5), PirateReports.TierName(9));
        }

        [Test]
        public void BossNames()
        {
            for (int k = 0; k < 200; k++)
            {
                string n = PirateReports.BossName();
                Assert.IsFalse(string.IsNullOrWhiteSpace(n));
                Assert.That(n.Split(' ').Length, Is.InRange(2, 4), n);
            }
        }

        // ---- the board ----------------------------------------------------------------------------------------

        [TestCase(2)]
        [TestCase(8)]
        [TestCase(16)]
        [TestCase(20)]
        public void RefreshFillsTheBoard(int rank)
        {
            SetRankFreePlay(rank);
            Session.StationIndex = 78;
            PirateReports.Refresh(Db);
            var board = Session.PirateSightings;
            int size = board.Count;
            Assert.Greater(size, 0);
            Assert.LessOrEqual(board.Count(s => !s.boss), PirateReports.MaxOutposts);
            Assert.LessOrEqual(board.Count(s => s.boss), PirateReports.MaxShipBounties);
            Assert.AreEqual(size, board.Select(s => s.system).Distinct().Count(), "one per system");
            Assert.AreEqual(size, board.Select(s => s.id).Distinct().Count());
            Assert.AreEqual(size, Session.NextPirateSightingId);
            foreach (var s in board)
            {
                Assert.AreNotEqual(78, s.station, "not where the player is docked");
                var sys = Db.Systems.Find(x => x.index == s.system);
                Assert.AreEqual(sys.index, Db.Stations.Find(x => x.index == s.station).system);
                Assert.That(sys.securityLevel, Is.InRange(0, 1), "Dangerous or Risky");
                Assert.IsTrue(Visible(sys.index));
                Assert.IsTrue(PirateEventRules.Allowed(Db, s.station, sys));
                Assert.That(s.tier, Is.InRange(1, 5));
                Assert.AreEqual(PirateReports.Bounty(!s.boss, rank, s.tier, true), s.bounty, "the contract's bounty");
                Assert.That(s.expiresAt - s.reportedAt, Is.InRange(40f * 60f, 90f * 60f));
                Assert.AreEqual(s.boss, !string.IsNullOrEmpty(s.name), "bosses are named");
                Assert.IsFalse(s.done || s.warned);
            }
        }

        [Test]
        public void NoReportsBelowRank2()
        {
            SetRankFreePlay(1);
            PirateReports.Refresh(Db);
            Assert.AreEqual(0, Session.PirateSightings.Count);
        }

        [Test]
        public void NothingWhileTheOptionIsOff()
        {
            SetRankFreePlay(8);
            Session.PirateSightings.Add(Sighting(500, 21, 4));
            Settings.PirateEvents = false;
            PirateReports.Refresh(Db);
            Assert.AreEqual(1, Session.PirateSightings.Count, "no refresh");
            Assert.IsNull(PirateReports.At(21));
            Assert.AreEqual(0, PirateReports.Board().Count);
            Assert.AreEqual("", PirateReports.Ticker(Db, 4));
            Assert.IsNull(PirateReports.TakeWarning(Db, 4));
        }

        [Test]
        public void RefreshDropsTheDoneAndTheExpiredAndRefills()
        {
            SetRankFreePlay(2);
            var done = Sighting(900, 21, 4); done.done = true;
            var expired = Sighting(901, 26, 5, life: -1f);
            var live = Sighting(902, 36, 7);
            Session.PirateSightings.AddRange(new[] { done, expired, live });
            Session.NextPirateSightingId = 903;
            PirateReports.Refresh(Db);
            Assert.IsFalse(Session.PirateSightings.Contains(done));
            Assert.IsFalse(Session.PirateSightings.Contains(expired));
            Assert.IsTrue(Session.PirateSightings.Contains(live));
            Assert.LessOrEqual(Session.PirateSightings.Count(s => !s.boss), PirateReports.MaxOutposts);
            Assert.LessOrEqual(Session.PirateSightings.Count(s => s.boss), PirateReports.MaxShipBounties);
            Assert.AreEqual(1, Session.PirateSightings.Count(s => s.system == 7), "the live one keeps its system");
        }

        [Test]
        public void DonePaysOnce()
        {
            var s = Sighting(910, 21, 4);
            Session.PirateSightings.Add(s);
            Assert.AreEqual(10910, PirateReports.Done(910));
            Assert.IsTrue(s.done);
            Assert.AreEqual(-1, PirateReports.Done(910), "already dealt with");
            Assert.AreEqual(-1, PirateReports.Done(4711), "not a reported one");
            Assert.AreEqual(-1, PirateReports.Done(-1));
            Assert.AreSame(s, PirateReports.Find(910));
        }

        [Test]
        public void AtFindsTheActiveSightingOfAStation()
        {
            var s = Sighting(920, 21, 4);
            Session.PirateSightings.Add(s);
            Session.PirateSightings.Add(Sighting(921, 26, 5, life: -10f));
            Assert.AreSame(s, PirateReports.At(21));
            Assert.IsNull(PirateReports.At(26), "expired");
            Assert.IsNull(PirateReports.At(22));
            s.done = true;
            Assert.IsNull(PirateReports.At(21), "done");
        }

        [Test]
        public void BoardListsTheActiveFirstThenTheDone()
        {
            var soon = Sighting(930, 21, 4, life: 1000f);
            var late = Sighting(931, 26, 5, life: 5000f);
            var done = Sighting(932, 36, 7); done.done = true;
            var gone = Sighting(933, 66, 13, life: -5f);
            Session.PirateSightings.AddRange(new[] { done, soon, gone, late });
            CollectionAssert.AreEqual(new[] { late, soon, done }, PirateReports.Board());
        }

        [Test]
        public void TheTickerReportsThisSystemAndItsGateNeighbours()
        {
            // Eanya (4) routes to Nesla (17) and Ginoya (27).
            var here = Sighting(940, 21, 4, boss: true);
            var neighbour = Sighting(941, 87, 17);
            var far = Sighting(942, 59, 20);
            Session.PirateSightings.AddRange(new[] { here, neighbour, far });
            string t = PirateReports.Ticker(Db, 4);
            StringAssert.Contains(PirateReports.Report(Db, here), t);
            StringAssert.Contains(PirateReports.Report(Db, neighbour), t);
            StringAssert.DoesNotContain(PirateReports.SystemName(Db, 20), t);
            StringAssert.Contains("Red Vex", PirateReports.Report(Db, here), "the boss's name");
            StringAssert.Contains(PirateReports.SystemName(Db, 17), PirateReports.Report(Db, neighbour), "the system, never the station");
        }

        [Test]
        public void TheWarningComesOncePerSighting()
        {
            int id = 1000000 + System.Environment.TickCount % 100000;   // the session's warned list is static
            var s = Sighting(id, 21, 4, boss: true);
            Session.PirateSightings.Add(s);
            Assert.IsNull(PirateReports.TakeWarning(Db, 5));
            string w = PirateReports.TakeWarning(Db, 4);
            Assert.IsNotNull(w);
            StringAssert.Contains("Red Vex", w);
            Assert.IsTrue(s.warned);
            Assert.IsNull(PirateReports.TakeWarning(Db, 4), "once");
        }

        [Test]
        public void TheSharedBoardRoundTrips()
        {
            Session.PirateSightings.Add(Sighting(950, 21, 4, boss: true, tier: 5));
            string json = UnityEngine.JsonUtility.ToJson(new SharedBoard { items = Session.PirateSightings });
            Session.PirateSightings = new List<PirateSighting>();
            PirateReports.ApplyShared(json);
            Assert.AreEqual(1, Session.PirateSightings.Count);
            var s = Session.PirateSightings[0];
            Assert.AreEqual(950, s.id); Assert.AreEqual(5, s.tier); Assert.AreEqual("Red Vex", s.name); Assert.IsTrue(s.boss);
            PirateReports.ApplyShared("");
            Assert.AreEqual(0, Session.PirateSightings.Count);
            PirateReports.ApplyShared(null);
            Assert.IsNotNull(Session.PirateSightings);
        }

        [System.Serializable] class SharedBoard { public List<PirateSighting> items = new List<PirateSighting>(); }

        [Test]
        public void TheServerRespectsSeparateReportLimitsAtRank10()
        {
            Session.FreePlay = true;
            string published = null;
            PirateReports.ServerTick(Db, j => published = j);
            Assert.IsNotNull(published);
            PirateReports.ApplyShared(published);
            var board = Session.PirateSightings;
            Assert.Greater(board.Count, 0);
            Assert.LessOrEqual(board.Count(s => !s.boss), PirateReports.MaxOutposts);
            Assert.LessOrEqual(board.Count(s => s.boss), PirateReports.MaxShipBounties);
            Assert.AreEqual(board.Count, board.Select(s => s.system).Distinct().Count());
            foreach (var s in board) Assert.AreEqual(PirateReports.Bounty(!s.boss, 10, s.tier, true), s.bounty);

            published = null;
            PirateReports.ServerTick(Db, j => published = j);
            Assert.IsNull(published, "unchanged: nothing published");

            int id = board[0].id;
            Assert.AreEqual(-1, PirateReports.ServerDone(id, st => false, j => published = j), "not in its orbit");
            Assert.AreEqual(board[0].bounty, PirateReports.ServerDone(id, st => st == board[0].station, j => published = j));
            Assert.IsNotNull(published);
            Assert.AreEqual(-1, PirateReports.ServerDone(id, st => true, j => { }), "once");
        }

        [Test]
        public void TheServerBoardEmptiesWhenTheOptionGoesOff()
        {
            Session.FreePlay = true;
            PirateReports.ServerTick(Db, j => { });
            Settings.PirateEvents = false;
            string published = null;
            PirateReports.ServerTick(Db, j => published = j);
            Assert.AreEqual("", published);
        }

        [Test]
        public void TheBoardSurvivesASaveAndLoad()
        {
            Session.StationIndex = 78;
            Session.PirateSightings.Add(Sighting(960, 21, 4, boss: true, tier: 4));
            Session.NextPirateSightingId = 961;
            Assert.IsTrue(SaveGame.Save(5));
            Session.ResetNewGame();
            Assert.IsTrue(SaveGame.Load(5));
            Assert.AreEqual(1, Session.PirateSightings.Count);
            Assert.AreEqual(4, Session.PirateSightings[0].tier);
            Assert.AreEqual(961, Session.NextPirateSightingId);
        }
    }
}
