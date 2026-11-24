using GoF2Remake.Modding;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModJumpRulesTests
    {
        static ModJumpFx.Spec Hyperdrive() => new ModJumpFx.Spec
        {
            failChance = 0.03f,
            failMax = 0.20f,
            failNoRoute = 0.20f,
            failHullDamage = 0.40f,
            failMinimumHull = 0.10f
        };

        [TestCase(1, false, 0.03f)]
        [TestCase(2, false, 0.06f)]
        [TestCase(6, false, 0.18f)]
        [TestCase(7, false, 0.20f)]
        [TestCase(1, true, 0.06f)]
        [TestCase(7, true, 0.20f)]
        public void ConnectedFailureUsesRouteSegmentsAndCap(int segments, bool extreme, float expected)
        {
            Assert.AreEqual(expected, ModJumpFx.FailureProbability(Hyperdrive(), segments, true, extreme), 0.00001f);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnconnectedFailureIsAlwaysTwentyPercent(bool extreme)
        {
            Assert.AreEqual(0.20f, ModJumpFx.FailureProbability(Hyperdrive(), 100, false, extreme), 0.00001f);
        }

        [TestCase(100, 60)]
        [TestCase(60, 20)]
        [TestCase(35, 10)]
        [TestCase(15, 10)]
        [TestCase(10, 10)]
        [TestCase(5, 5)]
        public void CatastrophicDamageNeverDropsBelowTenPercent(int before, int after)
        {
            Assert.AreEqual(after, ModJumpFx.HullAfterFailure(before, 100, 0.40f, 0.10f));
        }

        [Test]
        public void LowHullNeverReceivesFurtherDamage()
        {
            Assert.AreEqual(1, ModJumpFx.HullAfterFailure(1, 100, 1f, 0.10f));
            Assert.AreEqual(0, ModJumpFx.HullAfterFailure(0, 100, 1f, 0.10f));
        }
    }
}
