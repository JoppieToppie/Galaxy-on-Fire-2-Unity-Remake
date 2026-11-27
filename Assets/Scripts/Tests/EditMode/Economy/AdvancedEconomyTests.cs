using GoF2Remake.Modding;
using GoF2Remake.Multiplayer;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class AdvancedEconomyTests
    {
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
