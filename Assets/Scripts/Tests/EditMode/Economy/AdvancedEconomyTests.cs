using GoF2Remake.Modding;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class AdvancedEconomyTests
    {
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
