using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using GoF2Remake.Multiplayer;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class AdvancedEconomyTests
    {
        [TestCase(50, 100, 0, 0, true, 1500)]
        [TestCase(50, 100, 0, 0, false, 1350)]
        [TestCase(200, 100, 0, 0, true, 700)]
        [TestCase(100, 100, 100, 4, true, 1700)]
        [TestCase(0, 0, 50, 0, true, 1250)]
        [TestCase(0, 0, 0, 0, false, 900)]
        public void OtherGoodsAndHullsUseRealMarketSupply(int amount, int target, int pressure, int hops, bool buy, int expected)
        {
            Assert.AreEqual(expected, ModAdvancedEconomy.MarketUnitPrice(1000, amount, target, pressure, hops, 100, 30, 50, 5, 10, buy));
        }

        [Test]
        public void FreightDistressTitlesUseFactionThenDefaultThenLegacy()
        {
            Assert.AreEqual("Vossk command", ModAdvancedEconomy.SelectDistressTitle("Mayday", "Vossk command"));
            Assert.AreEqual("Mayday", ModAdvancedEconomy.SelectDistressTitle("Mayday", null));
            Assert.AreEqual("", ModAdvancedEconomy.SelectDistressTitle(null, null));
        }

        [Test]
        public void FreightDistressTemplatesSelectFactionAndExpandOnlyOnce()
        {
            var defaults = new[] { "Default {orbit}" };
            var faction = new[] { "First {orbit}", "Second {orbit}" };
            Assert.AreEqual("Default {orbit}", ModAdvancedEconomy.SelectDistressMessage(defaults, null, 200000004));
            Assert.AreEqual("Second {orbit}", ModAdvancedEconomy.SelectDistressMessage(defaults, faction, 200000004));
            Assert.AreEqual("Second {amount}", ModAdvancedEconomy.FormatDistressMessage(
                "Second {orbit}", new Dictionary<string, string> { ["orbit"] = "{amount}", ["amount"] = "400" }));
            Assert.AreEqual("400 t", ModAdvancedEconomy.FormatDistressMessage("{amount} t{rewardText}",
                new Dictionary<string, string> { ["amount"] = "400", ["rewardText"] = "" }));
        }

        [Test]
        public void FreightRescueRewardsRespectDistributionAndUniqueAccounts()
        {
            var accounts = new[] { "b", "a", "a" };
            var split = AdvancedEconomyLedger.FreightRescueShares(accounts, 20001, "split");
            Assert.AreEqual(10001, split["a"]); Assert.AreEqual(10000, split["b"]);
            var flat = AdvancedEconomyLedger.FreightRescueShares(accounts, 20000, "flat");
            Assert.AreEqual(20000, flat["a"]); Assert.AreEqual(20000, flat["b"]);
            var single = AdvancedEconomyLedger.FreightRescueShares(accounts, 20000, "single");
            Assert.AreEqual(1, single.Count); Assert.AreEqual(20000, single["a"]);
            var weighted = AdvancedEconomyLedger.FreightRescueShares(accounts, 20000, "weighted");
            Assert.AreEqual(13334, weighted["a"]); Assert.AreEqual(6666, weighted["b"]);
            Assert.AreEqual(0, AdvancedEconomyLedger.FreightRescueShares(accounts, 0, "split").Count);
            Assert.AreEqual(0, AdvancedEconomyLedger.FreightRescueShares(new string[0], 20000, "split").Count);
        }

        [Test]
        public void FreightDistressMeasuresLostShortageRelief()
        {
            Assert.AreEqual(40, AdvancedEconomyLedger.FreightLossShortfall(50, 100, 40));
            Assert.AreEqual(50, AdvancedEconomyLedger.FreightLossShortfall(50, 100, 100));
            Assert.AreEqual(0, AdvancedEconomyLedger.FreightLossShortfall(120, 100, 40));
        }

        [Test]
        public void FreightRescuePendingBalancesAreValidated()
        {
            const string start = "{\"version\":1,\"rows\":[],\"rescueRewards\":[";
            Assert.IsTrue(AdvancedEconomyLedger.ValidateJournalSnapshot(start + "{\"account\":\"a\",\"credits\":20000}]}"));
            Assert.IsFalse(AdvancedEconomyLedger.ValidateJournalSnapshot(start + "{\"account\":\"a\",\"credits\":-1}]}"));
            Assert.IsFalse(AdvancedEconomyLedger.ValidateJournalSnapshot(start + "{\"account\":\"a\",\"credits\":1},{\"account\":\"a\",\"credits\":1}]}"));
        }

        [Test]
        public void PirateRaidSuccessShrinksExponentiallyWithPlayerKills()
        {
            int[] successes = new int[4];
            for (int kills = 0; kills < 4; kills++)
                for (int roll = 0; roll < 10000; roll++)
                    if (ModAdvancedEconomy.FreightRaidOutcome(roll, 40, 30, 25, kills) != 0) successes[kills]++;
            CollectionAssert.AreEqual(new[] { 7000, 1750, 438, 0 }, successes);
            Assert.AreEqual(2, ModAdvancedEconomy.FreightRaidOutcome(0, 40, 30, 25, 0));
            Assert.AreEqual(1, ModAdvancedEconomy.FreightRaidOutcome(3000, 40, 30, 25, 0));
            Assert.AreEqual(0, ModAdvancedEconomy.FreightRaidOutcome(7000, 40, 30, 25, 0));
            Assert.IsTrue(ModAdvancedEconomy.IsFreightRaidTag(ModAdvancedEconomy.FreightRaidTagStart));
            Assert.IsFalse(ModAdvancedEconomy.IsFreightRaidTag(ModAdvancedEconomy.FreightRaidTagStart + 3));
            Assert.IsFalse(ModAdvancedEconomy.IsFreightTag(ModAdvancedEconomy.FreightRaidTagStart));
        }

        [Test]
        public void PhysicalFreightSurvivalAndDefeatedRaidersSkipAbstractLossRoll()
        {
            Assert.IsTrue(AdvancedEconomyLedger.UseAbstractFreightRaidOutcome(false, 0));
            Assert.IsTrue(AdvancedEconomyLedger.UseAbstractFreightRaidOutcome(false, 3));
            Assert.IsFalse(AdvancedEconomyLedger.UseAbstractFreightRaidOutcome(false, 7));
            Assert.IsFalse(AdvancedEconomyLedger.UseAbstractFreightRaidOutcome(true, 0));
        }

        [Test]
        public void FreightRaidSnapshotPreservesInterventionAndRejectsInvalidState()
        {
            const string start = "{\"version\":1,\"rows\":[],\"shipments\":[{\"key\":\"s|d|i\",\"amount\":100,\"resale\":0,\"ticks\":1,\"freightTag\":100000123,\"orbit\":1";
            Assert.IsTrue(AdvancedEconomyLedger.ValidateJournalSnapshot(start + "}]}"));
            const string raid = ",\"raidSystem\":1,\"raidBase\":200000000,\"raidDead\":3,\"raidPlayerKills\":1";
            Assert.IsTrue(AdvancedEconomyLedger.ValidateJournalSnapshot(start + raid.Replace("raidSystem", "raidOrbit") + "}]}"));
            Assert.IsFalse(AdvancedEconomyLedger.ValidateJournalSnapshot(start + ",\"raidOrbit\":2}]}"));
            Assert.IsTrue(AdvancedEconomyLedger.ValidateJournalSnapshot(start + raid + ",\"raidDistressPosted\":true}]}"));
            Assert.IsFalse(AdvancedEconomyLedger.ValidateJournalSnapshot(start + ",\"raidDistressPosted\":true}]}"));
            Assert.IsTrue(AdvancedEconomyLedger.ValidateJournalSnapshot(start + raid + ",\"raidObservationSeconds\":20,\"delayReason\":2,\"delaySeconds\":120}]}"));
            Assert.IsFalse(AdvancedEconomyLedger.ValidateJournalSnapshot(start + raid + ",\"raidObservationSeconds\":21}]}"));
            Assert.IsFalse(AdvancedEconomyLedger.ValidateJournalSnapshot(start + ",\"raidSystem\":1,\"raidBase\":200000000,\"raidDead\":1,\"raidPlayerKills\":2}]}"));
            Assert.IsFalse(AdvancedEconomyLedger.ValidateJournalSnapshot(start + ",\"raidSystem\":1,\"raidBase\":200000001}]}"));
        }

        [Test]
        public void FreightFleetSelectionUsesCapacityAndRelativeWeights()
        {
            var light = new ModAdvancedEconomy.FreightHull(36, "36", 400, 2, 125);
            var bulk = new ModAdvancedEconomy.FreightHull(51, "51", 2500, 1, 75);
            var native = new ModAdvancedEconomy.FreightHull(15, "native", 2500, 3, 100);
            var fleet = new[] { light, bulk, native };
            Assert.AreSame(light, ModAdvancedEconomy.PickFreightHull(fleet, 200, 0));
            Assert.AreSame(light, ModAdvancedEconomy.PickFreightHull(fleet, 200, 1));
            Assert.AreSame(bulk, ModAdvancedEconomy.PickFreightHull(fleet, 200, 2));
            Assert.AreSame(native, ModAdvancedEconomy.PickFreightHull(fleet, 200, 3));
            Assert.AreSame(bulk, ModAdvancedEconomy.PickFreightHull(fleet, 1000, 0));
            Assert.AreSame(native, ModAdvancedEconomy.PickFreightHull(fleet, 1000, 1));
            Assert.AreSame(native, ModAdvancedEconomy.PickFreightHull(fleet, 9000, 3));
            Assert.AreEqual(2500, ModAdvancedEconomy.PickFreightHull(fleet, 9000, 3).Capacity);
        }

        [Test]
        public void PhysicalDispatchThresholdsApplyToNativeAndCustomHulls()
        {
            var light = new ModAdvancedEconomy.FreightHull(36, "36", 400, 1, 125);
            var native = new ModAdvancedEconomy.FreightHull(15, "native", 2500, 1, 100);
            var fleet = new[] { light, native };
            Assert.IsNull(ModAdvancedEconomy.PickDispatchHull(fleet, 5, 400, 500, 50, 25, false, 0));
            Assert.IsNull(ModAdvancedEconomy.PickDispatchHull(fleet, 99, 400, 500, 50, 25, false, 0));
            Assert.AreSame(light, ModAdvancedEconomy.PickDispatchHull(fleet, 100, 400, 500, 50, 25, false, 1));
            Assert.AreSame(light, ModAdvancedEconomy.PickDispatchHull(fleet, 500, 1000, 1000, 50, 25, false, 0));
            Assert.AreSame(native, ModAdvancedEconomy.PickDispatchHull(fleet, 625, 1000, 1000, 50, 25, false, 0));
            Assert.IsNull(ModAdvancedEconomy.PickDispatchHull(new[] { native }, 399, 400, 500, 50, 25, false, 0));
            Assert.AreSame(native, ModAdvancedEconomy.PickDispatchHull(new[] { native }, 400, 400, 500, 50, 25, false, 0));
            Assert.AreSame(native, ModAdvancedEconomy.PickDispatchHull(new[] { native }, 30, 400, 30, 50, 25, false, 0));
            Assert.AreSame(light, ModAdvancedEconomy.PickDispatchHull(fleet, 5, 400, 500, 50, 25, true, 1));
            Assert.AreSame(native, ModAdvancedEconomy.PickDispatchHull(new[] { native }, 5, 400, 500, 50, 25, true, 0));
            Assert.AreSame(native, ModAdvancedEconomy.PickDispatchHull(new[] { native }, 5, 400, 500, 1, 0, false, 0));
            Assert.IsTrue(ModAdvancedEconomy.FreightStockCritical(10, 5, 100, 15));
            Assert.IsFalse(ModAdvancedEconomy.FreightStockCritical(10, 6, 100, 15));
        }

        [Test]
        public void MixedFreightMassConservesExactQuantity()
        {
            CollectionAssert.AreEqual(new[] { 25, 50, 25 }, AdvancedEconomyLedger.AllocateFreightMass(new[] { 100, 200, 100 }, 100));
            CollectionAssert.AreEqual(new[] { 0, 0, 1 }, AdvancedEconomyLedger.AllocateFreightMass(new[] { 1, 1, 1 }, 1));
            CollectionAssert.AreEqual(new[] { 5, 10 }, AdvancedEconomyLedger.AllocateFreightMass(new[] { 5, 10 }, 100));
            CollectionAssert.AreEqual(new[] { 0, 0 }, AdvancedEconomyLedger.AllocateFreightMass(new[] { 5, 10 }, 0));
            var native = new ModAdvancedEconomy.FreightHull(15, "native", 2500, 1, 100);
            Assert.IsNull(ModAdvancedEconomy.PickDispatchHull(new[] { native }, 5, 800, 800, 50, 25, false, 0));
            Assert.AreSame(native, ModAdvancedEconomy.PickDispatchHull(new[] { native }, 650, 800, 800, 50, 25, false, 0));
        }

        [Test]
        public void MixedFreightSnapshotValidatesIndividualBalancesAndRouteOwnership()
        {
            const string start = "{\"version\":2,\"rows\":[],\"shipments\":[";
            const string first = "{\"key\":\"s|d|a\",\"amount\":100,\"resale\":15,\"ticks\":1,\"cargo\":[{\"item\":\"a\",\"amount\":60,\"resale\":10},{\"item\":\"b\",\"amount\":40,\"resale\":5}]}";
            Assert.IsTrue(AdvancedEconomyLedger.ValidateJournalSnapshot(start + first + "]}"));
            Assert.IsFalse(AdvancedEconomyLedger.ValidateJournalSnapshot(start + first.Replace("\"amount\":40", "\"amount\":41") + "]}"));
            Assert.IsFalse(AdvancedEconomyLedger.ValidateJournalSnapshot(start + first.Replace("\"item\":\"b\"", "\"item\":\"a\"") + "]}"));
            Assert.IsFalse(AdvancedEconomyLedger.ValidateJournalSnapshot(start + first.Replace("\"resale\":5", "\"resale\":41") + "]}"));
            const string duplicate = "{\"key\":\"s|d|b\",\"amount\":10,\"resale\":0,\"ticks\":1,\"cargo\":[{\"item\":\"b\",\"amount\":10,\"resale\":0}]}";
            Assert.IsFalse(AdvancedEconomyLedger.ValidateJournalSnapshot(start + first + "," + duplicate + "]}"));
        }

        [Test]
        public void NativeFreightersRespectRaceAndDoNotShareWarTags()
        {
            Assert.AreEqual(13, ModAdvancedEconomy.NativeFreighter(1));
            Assert.AreEqual(15, ModAdvancedEconomy.NativeFreighter(0));
            Assert.AreEqual(15, ModAdvancedEconomy.NativeFreighter(2));
            Assert.AreEqual(15, ModAdvancedEconomy.NativeFreighter(3));
            int migrated = ModAdvancedEconomy.MigrateFreightTag(800000123);
            Assert.AreEqual(100000123, migrated);
            Assert.IsTrue(ModAdvancedEconomy.IsFreightTag(migrated));
            Assert.IsFalse(NpcFactionWar.IsWarTag(migrated));
            Assert.AreEqual(migrated, ModAdvancedEconomy.MigrateFreightTag(migrated));
        }

        [Test]
        public void FreightFleetIdentityIsOptionalForLegacyButValidatedWhenPresent()
        {
            const string start = "{\"version\":1,\"rows\":[],\"shipments\":[";
            const string ship = "{\"key\":\"s|d|i\",\"amount\":100,\"resale\":0,\"ticks\":1,\"freightTag\":100000123,\"orbit\":1";
            Assert.IsTrue(AdvancedEconomyLedger.ValidateJournalSnapshot(start + ship + "}]}"));
            Assert.IsTrue(AdvancedEconomyLedger.ValidateJournalSnapshot(start + ship + ",\"fleetRace\":\"vossk\",\"fleetHull\":\"native\",\"fleetSpeed\":100}]}"));
            Assert.IsFalse(AdvancedEconomyLedger.ValidateJournalSnapshot(start + ship + ",\"fleetRace\":\"vossk\"}]}"));
            Assert.IsFalse(AdvancedEconomyLedger.ValidateJournalSnapshot(start + ship + ",\"fleetRace\":\"vossk\",\"fleetHull\":\"native\",\"fleetSpeed\":501}]}"));
        }

        [Test]
        public void FreightLoadsAreBoundedAndSalvageConservesCargo()
        {
            Assert.AreEqual(2500, ModAdvancedEconomy.FreightCapacity(5000, 2500, 400));
            Assert.AreEqual(380, ModAdvancedEconomy.FreightCapacity(380, 2500, 400));
            Assert.AreEqual(0, ModAdvancedEconomy.FreightCapacity(-1, 2500, 400));
            int stolen = ModAdvancedEconomy.FreightClaimAmount(1200, 300, 500);
            Assert.AreEqual(300, stolen);
            Assert.AreEqual(225, ModAdvancedEconomy.FreightSalvageAmount(1200 - stolen, 25));
            Assert.AreEqual(50, ModAdvancedEconomy.FreightClaimAmount(225, 50, 225));
            Assert.AreEqual(0, ModAdvancedEconomy.FreightClaimAmount(0, 50, 225));
        }

        [Test]
        public void FreightProgressVisitsEverySystemInOrder()
        {
            Assert.AreEqual(0, AdvancedEconomyLedger.FreightLeg(4, 4, 4));
            Assert.AreEqual(1, AdvancedEconomyLedger.FreightLeg(4, 3, 4));
            Assert.AreEqual(2, AdvancedEconomyLedger.FreightLeg(4, 2, 4));
            Assert.AreEqual(3, AdvancedEconomyLedger.FreightLeg(4, 1, 4));
            Assert.AreEqual(3, AdvancedEconomyLedger.FreightLeg(4, 0, 4));
        }

        [Test]
        public void FreightTimingRangesAreBoundedAndOrdered()
        {
            Assert.AreEqual((30, 90), ModAdvancedEconomy.FreightInterval(30, 90));
            Assert.AreEqual((60, 60), ModAdvancedEconomy.FreightInterval(60, 10));
            Assert.AreEqual((1, 86400), ModAdvancedEconomy.FreightInterval(-1, int.MaxValue));
        }

        [Test]
        public void FreightCountdownSeparatesAbstractTravelPhysicalFlightAndUnloading()
        {
            int remaining = 30;
            for (int i = 0; i < 29; i++)
                remaining = AdvancedEconomyLedger.FreightCountdown(remaining, false, true, false);
            Assert.AreEqual(1, remaining);
            Assert.AreEqual(0, AdvancedEconomyLedger.FreightCountdown(remaining, false, true, false));
            Assert.AreEqual(30, AdvancedEconomyLedger.FreightCountdown(30, true, false, false));
            Assert.AreEqual(0, AdvancedEconomyLedger.FreightCountdown(30, true, true, false));
            Assert.AreEqual(15, AdvancedEconomyLedger.FreightCountdown(15, true, false, true));
            Assert.AreEqual(14, AdvancedEconomyLedger.FreightCountdown(15, true, true, true));
            Assert.AreEqual(0, AdvancedEconomyLedger.FreightCountdown(0, false, true, true));
        }

        [Test]
        public void FreightSnapshotSupportsLegacyAndIndependentCountdowns()
        {
            const string start = "{\"version\":1,\"rows\":[],\"shipments\":[";
            const string ship = "{\"key\":\"s|d|i\",\"amount\":100,\"resale\":0,\"ticks\":1,\"freightTag\":800000123,\"orbit\":1,\"totalTicks\":2";
            Assert.IsTrue(AdvancedEconomyLedger.ValidateJournalSnapshot(start + ship + "}]}"));
            Assert.IsTrue(AdvancedEconomyLedger.ValidateJournalSnapshot(start + ship + ",\"remainingSeconds\":30}]}"));
            Assert.IsTrue(AdvancedEconomyLedger.ValidateJournalSnapshot(start + ship + ",\"remainingSeconds\":0}]}"));
            Assert.IsFalse(AdvancedEconomyLedger.ValidateJournalSnapshot(start + ship + ",\"remainingSeconds\":-2}]}"));
            Assert.IsFalse(AdvancedEconomyLedger.ValidateJournalSnapshot(start + ship + ",\"remainingSeconds\":86401}]}"));
        }

        [Test]
        public void FreightSnapshotsRejectInvalidHullAndDuplicateLiveWreckIdentity()
        {
            const string start = "{\"version\":1,\"rows\":[],\"shipments\":[";
            const string ship = "{\"key\":\"s|d|i\",\"amount\":100,\"resale\":0,\"ticks\":1,\"freightTag\":800000123,\"orbit\":1,\"totalTicks\":2";
            Assert.IsTrue(AdvancedEconomyLedger.ValidateJournalSnapshot(start + ship + ",\"hull\":32768}]}"));
            Assert.IsFalse(AdvancedEconomyLedger.ValidateJournalSnapshot(start + ship + ",\"hull\":0}]}"));
            Assert.IsFalse(AdvancedEconomyLedger.ValidateJournalSnapshot(start + ship +
                "}],\"freightWrecks\":[{\"tag\":800000123,\"orbit\":1,\"amount\":25,\"race\":0,\"item\":\"i\",\"expires\":1,\"x\":0,\"y\":0,\"z\":0}]}"));
        }

        [Test]
        public void AbsoluteCargoReplayCannotDuplicateStock()
        {
            var cargo = new List<ItemStack> { new ItemStack(10, 7), new ItemStack(10, 3), new ItemStack(11, 4) };
            Assert.AreEqual(10, NetProfiles.SavedCargoAmount(cargo, 10));
            NetProfiles.ApplyAbsoluteCargoTarget(cargo, 10, 5);
            NetProfiles.ApplyAbsoluteCargoTarget(cargo, 10, 5);
            Assert.AreEqual(5, NetProfiles.SavedCargoAmount(cargo, 10));
            Assert.AreEqual(1, cargo.FindAll(x => x.item == 10).Count);
            Assert.AreEqual(4, NetProfiles.SavedCargoAmount(cargo, 11));
            NetProfiles.ApplyAbsoluteCargoTarget(cargo, 10, 0);
            NetProfiles.ApplyAbsoluteCargoTarget(cargo, 10, 0);
            Assert.AreEqual(0, NetProfiles.SavedCargoAmount(cargo, 10));
        }

        [Test]
        public void ProfileWorthCannotWrapNegative()
        {
            Assert.AreEqual(long.MaxValue, NetAudit.SumWorth(long.MaxValue, 500));
            Assert.AreEqual(60L, NetAudit.SumWorth(10, 20, 30));
        }

        [Test]
        public void AuthoritativeCreditDeltaMustSettleExactly()
        {
            Assert.IsTrue(NetProfiles.TryApplyCreditDelta(100, -100, out long empty));
            Assert.AreEqual(0L, empty);
            Assert.IsTrue(NetProfiles.TryApplyCreditDelta(100, 250, out long credited));
            Assert.AreEqual(350L, credited);
            Assert.IsFalse(NetProfiles.TryApplyCreditDelta(100, -101, out _));
            Assert.IsFalse(NetProfiles.TryApplyCreditDelta(long.MaxValue, 1, out _));
            Assert.IsFalse(NetProfiles.TryApplyCreditDelta(0, long.MinValue, out _));
            Assert.IsFalse(NetProfiles.TryApplyCreditDelta(-1, 1, out _));
            Assert.IsTrue(NetProfiles.TryApplyCreditDelta(long.MaxValue - 1, 1, out long max));
            Assert.AreEqual(long.MaxValue, max);
        }

        [Test]
        public void EconomyJournalRejectsInvalidSnapshotBeforeSettlement()
        {
            Assert.IsTrue(AdvancedEconomyLedger.ValidateJournalSnapshot("{\"version\":1,\"rows\":[],\"shipments\":[]}"));
            Assert.IsFalse(AdvancedEconomyLedger.ValidateJournalSnapshot("{\"version\":1,\"rows\":[{\"station\":\"s\",\"item\":\"i\",\"amount\":0,\"resale\":1}]}"));
        }

        [Test]
        public void BulkRoundTripCannotProfitFromItsOwnScarcity()
        {
            int buy = 0, sell = 0;
            for (int stock = 100; stock > 0; stock--)
                buy += ModAdvancedEconomy.TradeUnitPrice(100, stock, 100, 50, 2, 100, 30, 50, 5, 10, true);
            for (int stock = 0; stock < 100; stock++)
                sell += ModAdvancedEconomy.TradeUnitPrice(100, stock, 100, 50, 2, 100, 30, 50, 5, 10, false);
            Assert.Less(sell, buy);
            var quote = ModAdvancedEconomy.Quote(int.MaxValue, 100, 100, 0, 0, 100, 30, 50, 5, 10);
            Assert.Greater(quote.sell, 0);
            Assert.Less(quote.sell, quote.buy);
        }

        [Test]
        public void DynamicQuoteRespondsToScarcityWarAndFreight()
        {
            Assert.AreEqual((100, 90), ModAdvancedEconomy.Quote(100, 100, 100, 0, 0, 100, 30, 50, 5, 10));
            Assert.AreEqual((260, 234), ModAdvancedEconomy.Quote(100, 0, 100, 100, 2, 100, 30, 50, 5, 10));
            Assert.AreEqual((70, 63), ModAdvancedEconomy.Quote(100, 200, 100, 0, 0, 100, 30, 50, 5, 10));
        }

        [Test]
        public void RoutingDiscoversMultiHopAndHonoursDirection()
        {
            var graph = new AdvancedEconomyRoutes();
            graph.Add(1, 2);
            graph.Add(2, 3);
            graph.Add(1, 4);
            graph.Add(4, 5);
            graph.Add(5, 3);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, graph.Path(1, 3));
            Assert.IsNull(graph.Path(3, 1));
            graph.Add(1, 3);
            CollectionAssert.AreEqual(new[] { 1, 3 }, graph.Path(1, 3));
        }

        [Test]
        public void TradeUsesOnlySurplusAndDoesNotOversupplyTarget()
        {
            Assert.AreEqual(0, ModAdvancedEconomy.DispatchAmount(100, 100, 0, 100, 50));
            Assert.AreEqual(20, ModAdvancedEconomy.DispatchAmount(200, 100, 80, 100, 50));
            Assert.AreEqual(50, ModAdvancedEconomy.DispatchAmount(200, 100, 0, 100, 50));
            Assert.AreEqual(0, ModAdvancedEconomy.DispatchAmount(200, 100, 120, 100, 50));
        }

        [Test]
        public void ConstructionSupplyIsLimitedBySurplusOrderProjectAndBudget()
        {
            Assert.AreEqual(0, ModAdvancedEconomy.ConstructionSupplyAmount(100, 100, 50, 50, 100));
            Assert.AreEqual(12, ModAdvancedEconomy.ConstructionSupplyAmount(150, 100, 30, 12, 100));
            Assert.AreEqual(7, ModAdvancedEconomy.ConstructionSupplyAmount(150, 100, 30, 12, 7));
            Assert.AreEqual(500, ModAdvancedEconomy.ConstructionSupplyAmount(2000, 100, 900, 900, 900));
        }

        [Test]
        public void GateConsentDoesNotPassToNewOwner()
        {
            var gate = new PlayerJumpgateRecord { ownerProfile = "old", npcEconomyAccessOwner = "p:old" };
            Assert.IsTrue(gate.HasNpcEconomyConsent);
            Assert.IsTrue(gate.ToPublicRecord().npcEconomyAccess);
            Assert.IsNull(gate.ToPublicRecord().npcEconomyAccessOwner);
            gate.ownerProfile = "new";
            Assert.IsFalse(gate.HasNpcEconomyConsent);
            gate.ownershipType = PlayerStationOwnershipType.Faction;
            gate.ownerFactionId = "old";
            Assert.IsFalse(gate.HasNpcEconomyConsent);
        }

        [Test]
        public void RoutingCacheReusesUnchangedTopologyAndInvalidatesOnClosure()
        {
            var cache = new AdvancedEconomyRoutes.TopologyCache();
            var first = cache.Refresh(new[] { (1, 2), (2, 3) });
            Assert.AreEqual(2, first.Distance(1, 3));
            Assert.AreSame(first, cache.Refresh(new[] { (2, 3), (1, 2), (1, 2) }));
            var closed = cache.Refresh(new[] { (1, 2) });
            Assert.AreNotSame(first, closed);
            Assert.AreEqual(-1, closed.Distance(1, 3));
            var reopened = cache.Refresh(new[] { (1, 2), (2, 3), (3, 1) });
            Assert.AreEqual(2, reopened.Distance(1, 3));
            Assert.AreEqual(1, reopened.Distance(3, 1));
        }

        [Test]
        public void RouteDistanceRespectsDirectionAndInvalidation()
        {
            var graph = new AdvancedEconomyRoutes();
            graph.Add(1, 2); graph.Add(2, 3);
            Assert.AreEqual(2, graph.Distance(1, 3));
            Assert.AreEqual(-1, graph.Distance(3, 1));
            graph.Add(1, 3);
            Assert.AreEqual(1, graph.Distance(1, 3));
        }

        [Test]
        public void WarPressureScalesDemandAndShipmentLoss()
        {
            var rule = new ModAdvancedEconomy.Rule(1, 100, 100, 200, 0, 0, 40);
            Assert.AreEqual(100, rule.Advance(100, false, false, 0));
            Assert.AreEqual(80, rule.Advance(100, false, false, 50));
            Assert.AreEqual(60, rule.Advance(100, false, false, 100));
            Assert.AreEqual(100, ModAdvancedEconomy.SurvivingCargo(100, 40, 0));
            Assert.AreEqual(80, ModAdvancedEconomy.SurvivingCargo(100, 40, 50));
            Assert.AreEqual(60, ModAdvancedEconomy.SurvivingCargo(100, 40, 100));
        }

        [Test]
        public void LedgerPreservesZeroAndClampsResale()
        {
            var balance = new AdvancedEconomyLedger.Balance(10, 8, 100);
            Assert.IsTrue(balance.Set(0, 8, 100));
            Assert.AreEqual(0, balance.Amount);
            Assert.AreEqual(0, balance.Resale);
            Assert.IsFalse(balance.Set(0, 0, 100));
            Assert.IsTrue(balance.Set(200, 150, 100));
            Assert.AreEqual(100, balance.Amount);
            Assert.AreEqual(100, balance.Resale);
        }

        [Test]
        public void EmptyStockDoesNotAutomaticallyReturnToTarget()
        {
            var rule = new ModAdvancedEconomy.Rule(1, 100, 100, 200, 0, 0);
            Assert.AreEqual(0, rule.Advance(0, true, true));
            Assert.AreEqual(20, rule.Advance(20, true, true));
        }

        [Test]
        public void ProductionAndConsumptionAreIndependentOptIns()
        {
            var rule = new ModAdvancedEconomy.Rule(1, 100, 100, 200, 30, 10);
            Assert.AreEqual(100, rule.Advance(100, false, false));
            Assert.AreEqual(130, rule.Advance(100, true, false));
            Assert.AreEqual(90, rule.Advance(100, false, true));
            Assert.AreEqual(120, rule.Advance(100, true, true));
        }

        [Test]
        public void FlowNeverUnderflowsOrOverflowsStockBounds()
        {
            var rule = new ModAdvancedEconomy.Rule(1, 100, 100, 200, 30, 50);
            Assert.AreEqual(0, rule.Advance(2, false, true));
            Assert.AreEqual(200, rule.Advance(195, true, false));
            Assert.AreEqual(180, rule.Advance(200, true, true));
        }
    }
}
