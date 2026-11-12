// HazardTests.cs
// Feature: Modding/README.md orbit "hazards", docs/notes/navigation.md "Hazard volumes": Flight.Hazards, after EVERSPACE 2's
// BP_HeatDamageVolume (200 dps x (1 - d / r)^2 each 0.25 s) and the radiated condition (the hull's maximum x 0.85 a token,
// at most 10, cleared by docking). Expected values: those sums by hand.

using GoF2Remake.Flight;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class HazardTests
    {
        [TearDown] public void Clear() => Hazards.ClearRadiation();

        [Test]
        public void HeatFallsOffWithTheSquareOfTheDistance()
        {
            Assert.AreEqual(50f, Hazards.HeatDamage(200f, 0f, 1000f), 1e-3f, "the centre: 200 a second over a quarter second");
            Assert.AreEqual(12.5f, Hazards.HeatDamage(200f, 500f, 1000f), 1e-3f, "halfway: a quarter of it");
            Assert.AreEqual(0f, Hazards.HeatDamage(200f, 1000f, 1000f), "the edge");
            Assert.AreEqual(0f, Hazards.HeatDamage(200f, 1500f, 1000f), "outside");
        }

        [Test]
        public void RadiationLowersTheHullsLimitUntilDocking()
        {
            Assert.AreEqual(1f, Hazards.RadiationFactor(0));
            Assert.IsTrue(Hazards.AddRadiation());
            Assert.AreEqual(0.85f, Hazards.RadiationFactor(Hazards.RadiationTokens), 1e-5f);
            for (int i = 0; i < 20; i++) Hazards.AddRadiation();
            Assert.AreEqual(Hazards.MaxRadiationTokens, Hazards.RadiationTokens, "at most 10");
            Assert.IsFalse(Hazards.AddRadiation());
            Assert.AreEqual(UnityEngine.Mathf.Pow(0.85f, 10), Hazards.RadiationFactor(Hazards.RadiationTokens), 1e-5f);
            Hazards.ClearRadiation();
            Assert.AreEqual(0, Hazards.RadiationTokens, "docked");
        }

        [Test]
        public void FrostBuildsForFiveSecondsAndDrainsOverSixty()
        {
            float b = Hazards.FrostStep(0f, 4000f, true, out bool frozen);
            Assert.IsFalse(frozen);
            Assert.AreEqual(4000f, b);
            b = Hazards.FrostStep(b, 12000f, false, out frozen);
            Assert.AreEqual(3000f, b, 1e-3f, "12 s outside drain a fifth of the 5 s (60 s for all)");
            b = Hazards.FrostStep(b, 2000f, true, out frozen);
            Assert.IsTrue(frozen, "5 s built: frozen");
            Assert.AreEqual(0f, b, "and it builds again");
            Assert.AreEqual(0f, Hazards.FrostStep(100f, 60000f, false, out frozen), "never below none");
        }

        [Test]
        public void TheHeatsBloomFollowsTheDepthInside()
        {
            var heat = new[] { new GoF2Remake.Modding.ModWorld.HazardSpec { kind = GoF2Remake.Modding.ModWorld.HazardKind.Heat, centre = UnityEngine.Vector3.zero, radius = 10000f } };
            float r = 10000f * 0.05f;   // 500 m
            Assert.AreEqual(1f, GoF2Remake.World.HazardLooks.HeatDepth(heat, UnityEngine.Vector3.zero), "the centre");
            Assert.AreEqual(1f, GoF2Remake.World.HazardLooks.HeatDepth(heat, new UnityEngine.Vector3(r * 0.85f, 0f, 0f)), 1e-4f, "85 % in");
            Assert.AreEqual(0.5f, GoF2Remake.World.HazardLooks.HeatDepth(heat, new UnityEngine.Vector3(r * 0.925f, 0f, 0f)), 1e-3f, "half way through the edge");
            Assert.AreEqual(0f, GoF2Remake.World.HazardLooks.HeatDepth(heat, new UnityEngine.Vector3(r * 1.2f, 0f, 0f)), "outside");
        }

        [Test]
        public void StormFlashesLieOnTheCylinder()
        {
            var o = GoF2Remake.World.OrbitStorm.FlashOffset(4000f, 4000f, 0.25f, 1f);
            Assert.AreEqual(4000f, new UnityEngine.Vector2(o.x, o.z).magnitude, 1e-2f, "on the cylinder's surface (ES2's VolumeSurfaceOnly)");
            Assert.AreEqual(2000f, o.y, 1e-3f, "at the top: half the height up");
            float total = 0f;
            UnityEngine.Random.InitState(7);
            for (int i = 0; i < 2000; i++) total += GoF2Remake.World.OrbitStorm.NextFlashMs(1f);
            Assert.AreEqual(1000f, total / 2000f, 100f, "a flash a second on average at frequency 1");
        }

        [Test]
        public void TheFieldsTakeEverspace2sColours()
        {
            Assert.AreEqual(new UnityEngine.Color(0.84f, 1f, 0.09f), GoF2Remake.World.HazardLooks.KindColor(GoF2Remake.Modding.ModWorld.HazardKind.Corrosion), "P_CorrosionField's StartColor");
            Assert.AreEqual(new UnityEngine.Color(0.19f, 0.77f, 1f), GoF2Remake.World.HazardLooks.KindColor(GoF2Remake.Modding.ModWorld.HazardKind.Frost), "NS_FrostField's Initial.Color");
            Assert.AreEqual(new UnityEngine.Color(1f, 1f, 0.39f), GoF2Remake.World.HazardLooks.KindColor(GoF2Remake.Modding.ModWorld.HazardKind.Radiation), "RadiationColor");
        }
    }
}
