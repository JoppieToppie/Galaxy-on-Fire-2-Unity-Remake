// NetFactionsTests.cs
// Feature: CLAUDE.md "Multiplayer" > Factions: lasting player groups kept by profile in factions.json (a tag of 2-4
// letters / digits, a name of at most 24, at most 50 members); a deleted profile leaves its faction, a leaderless faction
// passes to its first officer, else its first member, an empty one ends (and its claims with it); a faction's home is a
// station it claims; claims cost 500 000 from the bank, at most 3 per faction, lapse after 14 days, not the Kaamo Club
// (108) or Loma (system 25); sieges cost 250 000, the toll is 10 000 (or -N = N % of the pilot's credits); at a held
// station members pay 10 % less and everyone else pays the claim's VAT (default 5 %, at most 20 %): the list price plus a
// tenth of the rate, the rate's share of the paid price going to the holder's bank; the console disbands by tag.
// Expected values: CLAUDE.md's numbers; the VAT worked out from VatMarkup (5 % on a 1000 list price: 1005 paid, 50 of VAT).
// A dedicated server's profile folder with a factions.json is set up in the sandbox (NetServerFixture).

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Multiplayer;
using NUnit.Framework;
using P = GoF2Remake.Tests.NetServerFixture.Pilot;

namespace GoF2Remake.Tests
{
    public class NetFactionsTests : SandboxTest
    {
        const int AlphaStation = 10, SoloStation = 11;

        static string FactionsJson() =>
            "{\"factions\":[" +
            "{\"id\":\"f1\",\"name\":\"Alpha Wing\",\"tag\":\"ABC\",\"leader\":\"a\",\"created\":\"\",\"officers\":[\"b\"],\"members\":[\"a\",\"b\",\"c\"],\"bank\":0,\"home\":" + AlphaStation + "}," +
            "{\"id\":\"f2\",\"name\":\"Lone Star\",\"tag\":\"SOLO\",\"leader\":\"s\",\"created\":\"\",\"officers\":[],\"members\":[\"s\"],\"bank\":1000,\"home\":" + SoloStation + "}," +
            "{\"id\":\"f3\",\"name\":\"Xylo\",\"tag\":\"XY\",\"leader\":\"x\",\"created\":\"\",\"officers\":[],\"members\":[\"x\",\"y\",\"z\"],\"bank\":0,\"home\":-1}" +
            "],\"claims\":[" +
            "{\"station\":" + AlphaStation + ",\"faction\":\"f1\",\"claimed\":\"\",\"lastDock\":\"\",\"protectedUntil\":0}," +
            "{\"station\":" + SoloStation + ",\"faction\":\"f2\",\"claimed\":\"\",\"lastDock\":\"\",\"protectedUntil\":0}" +
            "],\"sieges\":[]}";

        [SetUp]
        public void StartServer()
        {
            var pilots = new[] { "a", "b", "c", "s", "x", "y", "z" }.Select(id => new P(id, "Pilot " + id.ToUpperInvariant(), 0));
            NetServerFixture.Start(Sandbox, pilots, FactionsJson());
        }

        [TearDown]
        public void StopServer() => NetServerFixture.Stop();

        [TestCase(3, 0, 3d, 0d, true)]
        [TestCase(3, 1, 3d, 1.499d, true)]
        [TestCase(3, 1, 1.5d, 1.5d, false)]
        [TestCase(5, 1, 5d, 1.6d, false)]
        [TestCase(5, 1, 1.6d, 5d, false)]
        [TestCase(1, 1, 1d, 0d, false)]
        [TestCase(0, 0, 0d, 0d, false)]
        public void CaptureRewardsExcludeDoubleSidedDamage(int winning, int losing, double defenders, double attackers, bool eligible)
        {
            Assert.AreEqual(eligible, NpcFactionWar.QualifiesForCapture(winning, losing, defenders, attackers));
        }

        [TestCase(50, 0.5d, 0.5d, false)]
        [TestCase(50, 0.499d, 1d, true)]
        [TestCase(200, 1.5d, 2d, true)]
        [TestCase(200, 2d, 2d, false)]
        [TestCase(0, 10d, 10d, true)]
        public void CaptureRewardsRespectConfiguredDoubleSidedThreshold(int threshold, double defenders, double attackers, bool eligible)
        {
            Assert.AreEqual(eligible, NpcFactionWar.QualifiesForCapture(3, 1, defenders, attackers, threshold));
            Assert.IsFalse(NpcFactionWar.QualifiesForCapture(1, 1, defenders, attackers, threshold),
                "the normal contribution requirement remains");
        }

        [Test]
        public void MaximumFactionFleetHasDistinctSlotsAndTags()
        {
            int count = NpcFactionWar.MaxShipsPerSide;
            Assert.AreEqual(100, count);
            var a = new NpcFactionWar.Battle(1, 11, 0, 0, 1, count, 1000);
            var b = new NpcFactionWar.Battle(2, 12, 1, 1, 0, count, 1000);
            var tags = new HashSet<int>();
            for (int slot = 0; slot < 2 * count; slot++)
            {
                Assert.IsTrue(tags.Add(a.Tag(slot)));
                Assert.IsTrue(tags.Add(b.Tag(slot)));
                Assert.AreEqual(slot, a.Slot(a.Tag(slot)));
                Assert.IsTrue(NpcFactionWar.IsWarTag(a.Tag(slot)));
            }
            Assert.AreEqual(400, tags.Count);
        }

        [Test]
        public void MaximumFactionFleetSurvivesMapSnapshot()
        {
            var campaign = NpcFactionWar.Campaign.Read("{\"active\":[{\"id\":1,\"orbit\":11,\"system\":0,\"defender\":0,\"attacker\":1,\"shipsPerSide\":100,\"defendersLost\":65,\"attackersLost\":99,\"expires\":1000,\"pausedSeconds\":-1}]}");
            var restored = NpcFactionWar.Campaign.Read(campaign.Snapshot(true));
            Assert.IsTrue(restored.TryGetStatus(0, 11, 1, out var status));
            Assert.AreEqual(35, status.DefendersAlive);
            Assert.AreEqual(1, status.AttackersAlive);
        }

        [Test]
        public void FactionWarAuxiliariesBelongToBattleButNeverCountAsCasualties()
        {
            var a = new NpcFactionWar.Battle(1, 11, 0, 0, 1, 100, 1000, 1, 2);
            var b = new NpcFactionWar.Battle(2, 12, 1, 1, 0, 100, 1000);
            int tag = NpcFactionWar.AuxiliaryTag(a.Tag(100));
            Assert.AreEqual(tag, NpcFactionWar.AuxiliaryTag(a.Tag(0)));
            Assert.IsTrue(NpcFactionWar.IsAuxiliaryTag(tag));
            Assert.IsTrue(a.OwnsTag(tag));
            Assert.IsFalse(a.IsRosterTag(tag));
            var campaign = NpcFactionWar.Campaign.Read("{\"active\":[{\"id\":1,\"orbit\":11,\"system\":0,\"defender\":0,\"attacker\":1,\"shipsPerSide\":100,\"expires\":1000,\"pausedSeconds\":-1}]}");
            Assert.IsFalse(campaign.RecordCasualty(a, tag, 0, 100, out _));
            Assert.IsTrue(campaign.TryGetStatus(0, 11, 100, out var status));
            Assert.AreEqual(100, status.DefendersAlive);
            Assert.IsFalse(b.OwnsTag(tag));
            Assert.AreEqual(123, NpcFactionWar.AuxiliaryTag(123));
        }

        [Test]
        public void FactionWarHullSelectionSurvivesPauseAndMapSnapshot()
        {
            var campaign = NpcFactionWar.Campaign.Read("{\"active\":[{\"id\":1,\"orbit\":11,\"system\":0,\"defender\":0,\"attacker\":1,\"shipsPerSide\":4,\"expires\":1000,\"pausedSeconds\":-1,\"defenderHull\":1,\"attackerHull\":2}]}");
            Assert.IsTrue(campaign.TryGetBattle(11, 100, out var battle));
            Assert.IsTrue(campaign.SetPaused(battle, true, 100, out var paused));
            Assert.AreEqual(1, paused.defenderHull);
            Assert.AreEqual(2, paused.attackerHull);
            var restored = NpcFactionWar.Campaign.Read(campaign.Snapshot(true));
            Assert.IsTrue(restored.TryGetStatus(0, 11, 100, out var status));
            Assert.AreEqual(1, status.defenderHull);
            Assert.AreEqual(2, status.attackerHull);
            Assert.IsTrue(status.Paused);
            Assert.IsFalse(NpcFactionWar.Campaign.Read(campaign.Snapshot()).TryGetBattle(11, 100, out _));
        }

        [Test]
        public void MissingFactionKeysSurvivePrivateSaveButAreNotClaimable()
        {
            const string missing = "missing_test_mod:race";
            var campaign = NpcFactionWar.Campaign.Read("{\"held\":[{\"orbit\":\"11\",\"race\":32,\"raceKey\":\"" + missing + "\"}],\"rewards\":[{\"id\":\"missing-reward\",\"account\":\"profile:a\",\"orbitKey\":\"11\",\"race\":32,\"raceKey\":\"" + missing + "\",\"credits\":100}],\"participation\":[{\"account\":\"profile:a\",\"race\":32,\"raceKey\":\"" + missing + "\",\"count\":5}]}");
            string saved = campaign.Snapshot();
            StringAssert.Contains(missing, saved);
            StringAssert.Contains("missing-reward", saved);
            StringAssert.Contains("\"count\":5", saved);
            Assert.IsNull(campaign.RewardAt("profile:a", 11));
            StringAssert.DoesNotContain(missing, campaign.Snapshot(true));
            StringAssert.Contains("missing-reward", NpcFactionWar.Campaign.Read(saved).Snapshot());
        }

        [Test]
        public void VictoryCreditPoolIsConserved()
        {
            Assert.AreEqual(100000L, NpcFactionWar.Campaign.CreditShare(100000, 1, 0));
            Assert.AreEqual(33334L, NpcFactionWar.Campaign.CreditShare(100000, 3, 0));
            Assert.AreEqual(33333L, NpcFactionWar.Campaign.CreditShare(100000, 3, 1));
            Assert.AreEqual(33333L, NpcFactionWar.Campaign.CreditShare(100000, 3, 2));
            Assert.AreEqual(0L, NpcFactionWar.Campaign.CreditShare(100000, 0, 0));
        }

        [Test]
        public void VictoryRewardsArePrivateAndReplacedOnRecapture()
        {
            var campaign = new NpcFactionWar.Campaign();
            var reward = new NpcFactionWar.VictoryReward { id = "battle-reward", account = "profile:a", race = 0, credits = 50000 };
            campaign.ReplaceOrbitRewards(AlphaStation, new[] { reward });
            Assert.AreSame(reward, campaign.RewardAt("profile:a", AlphaStation));
            Assert.IsNull(campaign.RewardAt("profile:b", AlphaStation));
            Assert.IsNull(campaign.RewardAt("profile:a", SoloStation));
            Assert.IsNull(NpcFactionWar.Campaign.Read(campaign.Snapshot(true)).RewardAt("profile:a", AlphaStation));
            var restored = NpcFactionWar.Campaign.Read(campaign.Snapshot());
            Assert.AreEqual("battle-reward", restored.RewardAt("profile:a", AlphaStation).id);
            restored.ReplaceOrbitRewards(AlphaStation, new NpcFactionWar.VictoryReward[0]);
            Assert.IsNull(restored.RewardAt("profile:a", AlphaStation));
        }

        [Test]
        public void TheDocumentedNumbers()
        {
            Assert.AreEqual(24, NetFactions.MaxNameLength);
            Assert.AreEqual(50, NetFactions.MaxMembers);
            Assert.AreEqual(500_000, NetFactions.DefaultClaimCost);
            Assert.AreEqual(3, NetFactions.DefaultMaxClaims);
            Assert.AreEqual(14, NetFactions.DefaultLapseDays);
            Assert.AreEqual(250_000, NetFactions.DefaultSiegeCost);
            Assert.AreEqual(10_000, NetFactions.DefaultToll);
            Assert.AreEqual(10, NetFactions.MemberDiscountPercent);
            Assert.AreEqual(5, NetFactions.DefaultVat);
            Assert.AreEqual(20, NetFactions.MaxVat);
        }

        [Test]
        public void ConfigureClampsTheServersChoices()
        {
            NetFactions.Configure(-1, 500, 0, -5, -10);
            Assert.AreEqual(0, NetFactions.ClaimCost);
            Assert.AreEqual(100, NetFactions.MaxClaims);
            Assert.AreEqual(1, NetFactions.LapseDays);
            Assert.AreEqual(0, NetFactions.SiegeCost);
            Assert.AreEqual(-10, NetFactions.Toll);   // 10 % of the pilot's credits
            NetFactions.Configure(0, 3, 14, 0, -500);
            Assert.AreEqual(-100, NetFactions.Toll);   // at most all of them
            NetFactions.Configure(750_000, 5, 30, 100_000, 2500);
            Assert.AreEqual(750_000, NetFactions.ClaimCost);
            Assert.AreEqual(5, NetFactions.MaxClaims);
            Assert.AreEqual(30, NetFactions.LapseDays);
            Assert.AreEqual(100_000, NetFactions.SiegeCost);
            Assert.AreEqual(2500, NetFactions.Toll);
        }

        [Test]
        public void MembersKnowTheirFaction()
        {
            Assert.AreEqual("ABC", NetFactions.Of("a").tag);
            Assert.AreEqual("ABC", NetFactions.Of("c").tag);
            Assert.AreEqual("SOLO", NetFactions.Of("s").tag);
            Assert.IsNull(NetFactions.Of("nobody"));
            Assert.IsNull(NetFactions.Of(null));
            Assert.IsNull(NetFactions.OfClient(99), "a guest");
        }

        [Test]
        public void TheHomeIsAClaimedStation()
        {
            Assert.AreEqual(AlphaStation, NetFactions.HomeOf("c"));
            Assert.AreEqual(-1, NetFactions.HomeOf("x"), "no home set");
            Assert.AreEqual(-1, NetFactions.HomeOf("nobody"));
        }

        [Test]
        public void ALeaderlessFactionPassesToItsFirstOfficer()
        {
            NetFactions.OnAccountDeleted("a");
            var f = NetFactions.Of("b");
            Assert.AreEqual("b", f.leader);
            Assert.IsFalse(f.officers.Contains("b"), "the new leader is no longer an officer");
            Assert.IsFalse(f.members.Contains("a"));
            CollectionAssert.AreEqual(new[] { "b", "c" }, f.members);
        }

        [Test]
        public void WithoutOfficersItPassesToTheFirstMember()
        {
            NetFactions.OnAccountDeleted("x");
            Assert.AreEqual("y", NetFactions.Of("y").leader);
        }

        [Test]
        public void AMemberLeavingKeepsTheLeader()
        {
            NetFactions.OnAccountDeleted("c");
            var f = NetFactions.Of("a");
            Assert.AreEqual("a", f.leader);
            CollectionAssert.AreEqual(new[] { "b" }, f.officers);
            Assert.IsNull(NetFactions.Of("c"));
        }

        [Test]
        public void AnEmptyFactionEndsWithItsClaims()
        {
            NetFactions.OnAccountDeleted("s");
            Assert.IsNull(NetFactions.Of("s"));
            StringAssert.DoesNotContain("SOLO", NetFactions.ConsoleList());
            var s = new NetPanel.State { dockedStation = SoloStation };
            NetFactions.FillPanel(99, s);
            Assert.AreEqual("", s.stationHolder, "its claim is free again");
            Assert.IsFalse(s.factions.Any(r => r.tag == "SOLO"));
        }

        [Test]
        public void ChangesAreSaved()
        {
            NetFactions.OnAccountDeleted("a");
            NetFactions.Load();
            Assert.AreEqual("b", NetFactions.Of("b").leader, "factions.json was written");
        }

        [Test]
        public void TheConsoleDisbandsByTag()
        {
            StringAssert.Contains("ABC", NetFactions.ConsoleDisband("abc"));
            Assert.IsNull(NetFactions.Of("a"));
            Assert.IsNull(NetFactions.Of("b"));
            Assert.AreEqual(-1, NetFactions.HomeOf("c"));
            StringAssert.Contains("NOPE", NetFactions.ConsoleDisband("NOPE"));
        }

        [Test]
        public void TheConsoleLists()
        {
            string list = NetFactions.ConsoleList();
            StringAssert.StartsWith("3 faction(s):", list);
            StringAssert.Contains("[ABC] Alpha Wing: 3 member(s)", list);
            StringAssert.Contains("[SOLO] Lone Star: 1 member(s)", list);
        }

        [Test]
        public void TheOtherFactionsPayVatToTheHolder()
        {
            Assert.AreEqual(1005, NetFactions.VatMarkup(1000, 5));   // a tenth of the rate on top
            Assert.AreEqual(1020, NetFactions.VatMarkup(1000, 20));
            Assert.AreEqual(1000, NetFactions.VatMarkup(1000, 0));
            int item = PricedItem(out int max);
            int paid = NetFactions.VatMarkup(max, 5);
            long vat = (long)System.Math.Round(paid / 1.005 * 0.05);
            Assert.Greater(vat, 0);
            var alpha = NetFactions.Of("a");
            long before = alpha.bank;
            NetFactions.OnPurchase(99, AlphaStation, item, paid, 1);
            Assert.AreEqual(before + vat, alpha.bank);
            NetFactions.OnPurchase(99, AlphaStation, item, paid, 2);
            Assert.AreEqual(before + 3 * vat, alpha.bank);
            NetFactions.OnPurchase(99, AlphaStation, item, int.MaxValue, 1);   // a claimed price is capped at the list maximum
            Assert.AreEqual(before + 4 * vat, alpha.bank);
        }

        /// <summary>An item of the session's tables with a list price high enough for its VAT to round above 0.</summary>
        static int PricedItem(out int max)
        {
            var items = NetGame.Db.Items;
            for (int i = 0; i < items.Count; i++)
                if (items[i] != null && items[i].maxPrice >= 1000) { max = items[i].maxPrice; return i; }
            Assert.Fail("no item with a list price of 1000 or more");
            max = 0;
            return -1;
        }

        [Test]
        public void NoTaxWhereNobodyHoldsTheStation()
        {
            var alpha = NetFactions.Of("a");
            var solo = NetFactions.Of("s");
            long a = alpha.bank, s = solo.bank;
            int item = PricedItem(out int max);
            NetFactions.OnPurchase(99, 78, item, max, 1);
            NetFactions.OnPurchase(99, AlphaStation, item, 0, 1);
            NetFactions.OnPurchase(99, AlphaStation, item, -10, 1);
            NetFactions.OnPurchase(99, AlphaStation, item, max, 0);
            Assert.AreEqual(a, alpha.bank);
            Assert.AreEqual(s, solo.bank);
        }

        [Test]
        public void ThePanelListsTheFactionsAndTheStationsHolder()
        {
            var s = new NetPanel.State { dockedStation = AlphaStation };
            NetFactions.FillPanel(99, s);
            Assert.AreEqual(3, s.factions.Count);
            var abc = s.factions.Single(r => r.tag == "ABC");
            Assert.AreEqual(3, abc.members);
            Assert.AreEqual(1, abc.claims);
            Assert.AreEqual("ABC", s.stationHolder);
            Assert.IsFalse(s.inFaction, "a guest");
            Assert.AreEqual(NetFactions.ClaimCost, s.claimCost);
            Assert.AreEqual(NetFactions.MaxClaims, s.maxClaims);
        }

        [Test]
        public void TheKaamoClubAndLomaCantBeClaimed()
        {
            var kaamo = new NetPanel.State { dockedStation = 108 };
            NetFactions.FillPanel(99, kaamo);
            Assert.IsFalse(kaamo.stationClaimable, "the Kaamo Club (108)");
            var loma = Db.Stations.First(st => st.system == 25);
            var atLoma = new NetPanel.State { dockedStation = loma.index };
            NetFactions.FillPanel(99, atLoma);
            Assert.IsFalse(atLoma.stationClaimable, "Loma (system 25)");
            var varHastra = new NetPanel.State { dockedStation = 78 };
            NetFactions.FillPanel(99, varHastra);
            Assert.IsTrue(varHastra.stationClaimable);
            Assert.AreEqual("", varHastra.stationHolder);
        }

        [Test]
        public void ACommandWithoutAProfile()
        {
            Assert.IsNull(NetFactions.Command(99, "/hello"), "not a faction command");
            Assert.IsNotNull(NetFactions.Command(99, "/faction create NEW New Faction"), "a guest has no faction");
            Assert.IsNull(NetFactions.Of("nobody"));
            Assert.AreEqual(3, NetFactions.ConsoleList().Split('\n').Length - 1, "nothing was created");
        }

        [Test]
        public void NoSiegesYet()
        {
            Assert.AreEqual(Localization.Extra("mpSiegeNone", "No sieges."), NetFactions.ConsoleSieges());
        }

        [Test]
        public void OutsideASessionPricesAndRelationsAreTheGamesOwn()
        {
            // NetFactionsClient reads the claims NetState carries: none without a session.
            Assert.IsFalse(NetFactionsClient.Owner(AlphaStation, out _, out _));
            Assert.AreEqual("", NetFactionsClient.OwnerText(AlphaStation));
            Assert.AreEqual(1000, NetFactionsClient.BuyPrice(AlphaStation, 1000));
            Assert.AreEqual(1000, NetFactionsClient.SellPrice(AlphaStation, 1000));
            Assert.AreEqual(0, NetFactionsClient.Relation(AlphaStation, "ABC", -1));
            Assert.IsFalse(NetFactionsClient.SiegePvp(AlphaStation, "ABC", "XY"));
            Assert.IsNull(NetFactionsClient.SiegeAt(AlphaStation));
        }

        [Test]
        public void WithoutProfilesThereAreNoFactions()
        {
            NetServerFixture.Stop();
            StringAssert.Contains("-noprofiles", NetFactions.ConsoleList());
            Assert.IsNull(NetFactions.Of("a"));
        }
    }
}
