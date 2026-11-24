using GoF2Remake.Modding;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ModJumpRulesTests
    {
        [Test]
        public void EmptyCinematicPreservesDefaultCamera()
        {
            Assert.IsNull(ModJumpFx.ReadCinematic(null));
            var c = ModJumpFx.ReadCinematic(new JObject());
            Assert.IsFalse(c.HasCamera);
            Assert.IsTrue(c.allowShipTurning);
            Assert.AreEqual(0f, c.MinimumDuration);
            Assert.AreEqual(new Vector3(-5, 2, -5) * 100f, c.Position(100f, Vector3.zero, new Vector3(-5, 2, -5)));
        }

        [Test]
        public void CinematicSortsKeysAndLastDuplicateWins()
        {
            var c = ModJumpFx.ReadCinematic(JObject.Parse(
                @"{""durationMs"":100,""cameraKeyframes"":[{""ms"":2000,""offset"":[20,0,0]},{""ms"":0,""offset"":[0,0,0]},{""ms"":2000,""offset"":[40,0,0]}]}"));
            Assert.AreEqual(2, c.keys.Count);
            Assert.AreEqual(2000f, c.MinimumDuration);
            Assert.AreEqual(new Vector3(20, 0, 0), c.Position(1000, Vector3.zero));
            Assert.AreEqual(new Vector3(40, 0, 0), c.Position(3000, Vector3.zero));
        }

        [Test]
        public void CinematicRejectsMalformedValuesAndClampsLimits()
        {
            var c = ModJumpFx.ReadCinematic(JObject.Parse(
                @"{""cameraFov"":999,""shipSpeed"":-5,""cameraOffset"":[1,""bad"",3],""durationMs"":999999,""cameraLookAt"":""unknown""}"));
            Assert.AreEqual(150f, c.fov.Value);
            Assert.AreEqual(0f, c.shipSpeed.Value);
            Assert.IsFalse(c.offset.HasValue);
            Assert.AreEqual(120000f, c.MinimumDuration);
            Assert.AreEqual("ship", c.lookAt);
        }

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
