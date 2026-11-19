// ModDeathFxTests.cs
// Feature: docs/notes/mods-models.md "Death effects": ships.json "deathFx" names a JSON file (CustomFxFile) of particle
// emitters; World.ModDeathFx builds each as a Unity particle system (bursts, life, size, speed, drag, a flipbook, the fade).

using GoF2Remake.Data;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ModDeathFxTests
    {
        const string File = "{ \"main\": { \"emitters\": [ { \"image\": \"fx/fireball.png\", \"sheet\": [16, 8], \"frames\": [0, 0.6, 1], " +
            "\"bursts\": [0, 1, 0.05, 1, 0.1, 1], \"duration\": 4, \"life\": [4, 4], \"size\": [1980, 1980], \"sizeOverLife\": [0.39, 1.1, 1.35], " +
            "\"speed\": [1150, 1420], \"forward\": true, \"sphere\": 900, \"drag\": 2, \"color\": [8, 4, 4], \"colorOverLife\": [1, 1, 1, 0, 0, 0], " +
            "\"alphaOverLife\": [1, 0] }, { \"image\": \"fx/spark.png\", \"bursts\": [0, 500], \"life\": [0.1, 1], \"size\": [22, 36], " +
            "\"speed\": [1800, 18000], \"stretch\": 4, \"color\": [7, 0.7, 0.7] } ] }, " +
            "\"dying\": { \"emitters\": [ { \"image\": \"fx/fireball.png\", \"bursts\": [0, 1], \"life\": [2, 2], \"size\": [900, 900] } ] }, \"dyingCount\": [3, 12] }";

        [Test]
        public void TheFileIsRead()
        {
            var f = JsonUtility.FromJson<CustomFxFile>(File);
            Assert.AreEqual(2, f.main.emitters.Length);
            Assert.AreEqual(new[] { 16, 8 }, f.main.emitters[0].sheet);
            Assert.IsTrue(f.main.emitters[0].forward);
            Assert.AreEqual(4f, f.main.emitters[1].stretch);
            Assert.AreEqual(1f, f.main.emitters[1].inherit, "the ship's motion by default");
            Assert.AreEqual(new[] { 3, 12 }, f.dyingCount);
            Assert.IsNull(JsonUtility.FromJson<CustomShipData>("{ \"id\": \"x\" }").deathFx, "none: the game's explosion");
        }

        [Test]
        public void AnEmitterBecomesAParticleSystem()
        {
            var f = JsonUtility.FromJson<CustomFxFile>(File);
            var root = new GameObject("fx");
            try
            {
                var fire = GoF2Remake.World.ModDeathFx.Build(null, f.main.emitters[0], root.transform, new Vector3(0f, 0f, 10f), 1f, out float lasts);
                Assert.AreEqual(3, fire.emission.burstCount);
                Assert.AreEqual(8f, lasts, 1e-4f, "its duration and the longest life");
                Assert.AreEqual(1980f * 0.05f, fire.main.startSize.constantMax, 1e-3f);
                Assert.AreEqual(ParticleSystemShapeType.Cone, fire.shape.shapeType, "along the effect's forward");
                Assert.IsTrue(fire.limitVelocityOverLifetime.enabled);
                Assert.AreEqual(16, fire.textureSheetAnimation.numTilesX);
                Assert.AreEqual(new Vector3(0f, 0f, 10f), fire.main.emitterVelocity, "the ship's motion");
                var sparks = GoF2Remake.World.ModDeathFx.Build(null, f.main.emitters[1], root.transform, Vector3.zero, 1f, out _);
                Assert.AreEqual(ParticleSystemRenderMode.Stretch, sparks.GetComponent<ParticleSystemRenderer>().renderMode);
                Assert.AreEqual(ParticleSystemShapeType.Sphere, sparks.shape.shapeType, "outward");
                Assert.GreaterOrEqual(sparks.main.maxParticles, 500);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void ALightEmitterIsAPointLightOverItsLife()
        {
            var e = JsonUtility.FromJson<CustomFxEmitter>("{ \"light\": 4000, \"lightIntensity\": 3, \"bursts\": [0.5, 1], \"life\": [1, 2], " +
                "\"color\": [8, 4, 2], \"colorOverLife\": [1, 1, 1, 0.5, 0.5, 0.5], \"alphaOverLife\": [1, 0], \"sizeOverLife\": [1, 2] }");
            var root = new GameObject("fx");
            try
            {
                var f = GoF2Remake.World.FxFlash.Spawn(e, root.transform, 1f);
                Assert.AreEqual(2.5f, f.Lasts, 1e-4f, "from its first burst for its longest life");
                Assert.IsFalse(f.Light.enabled, "dark before its burst");
                var (c, i, r) = f.At(0f);
                Assert.AreEqual(new Color(1f, 0.5f, 0.25f), c, "the colour normalised");
                Assert.AreEqual(3f, i, 1e-4f);
                Assert.AreEqual(4000f * 0.05f / 2f, r, 1e-3f, "its range x the size over life (at half its top)");
                var (_, half, r2) = f.At(0.5f);
                Assert.AreEqual(3f * 0.75f * 0.5f, half, 1e-4f, "dimmer with the colour and the alpha");
                Assert.AreEqual(4000f * 0.05f * 0.75f, r2, 1e-3f);
                f.Step(0.6f);
                Assert.IsTrue(f.Light.enabled);
                f.Step(2f);
                Assert.IsFalse(f.Light.enabled, "gone after its life");
                Assert.AreEqual(0.5f, GoF2Remake.World.FxFlash.Sample(new[] { 0f, 1f }, 0.5f));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void ATrailLeavesPuffsAlongItsParticlesAndNoiseShakesThem()
        {
            var e = JsonUtility.FromJson<CustomFxEmitter>("{ \"bursts\": [0, 10], \"life\": [1, 1], \"speed\": [2880, 5760], " +
                "\"noise\": [360, 36000], \"trail\": { \"image\": \"fx/fire.png\", \"every\": 72, \"life\": [0.5, 2], \"size\": [36, 216] } }");
            var root = new GameObject("fx");
            try
            {
                var ps = GoF2Remake.World.ModDeathFx.Build(null, e, root.transform, Vector3.zero, 1f, out float lasts);
                Assert.AreEqual(ParticleSystemRenderMode.None, ps.GetComponent<ParticleSystemRenderer>().renderMode, "no image: only the trails' source");
                Assert.AreEqual(1, ps.subEmitters.subEmittersCount);
                var child = ps.subEmitters.GetSubEmitterSystem(0);
                Assert.AreEqual(1f / (72f * 0.05f), child.emission.rateOverDistance.constant, 1e-4f, "a puff every 72 units of flight");
                Assert.AreEqual(ParticleSystemSubEmitterType.Birth, ps.subEmitters.GetSubEmitterType(0));
                Assert.IsTrue(ps.noise.enabled);
                Assert.AreEqual(360f * 0.05f, ps.noise.strength.constant, 1e-4f);
                Assert.GreaterOrEqual(lasts, 1f + 2f, "its life and the puffs' after it");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void AnAlphaEmitterIsDrawnOverWhatIsBehind()
        {
            var smoke = JsonUtility.FromJson<CustomFxEmitter>("{ \"image\": \"fx/smoke.png\", \"blend\": \"alpha\", \"bursts\": [0, 1], \"color\": [0.1, 0.1, 0.1] }");
            Assert.IsTrue(GoF2Remake.World.ModDeathFx.Alpha(smoke));
            Assert.IsFalse(GoF2Remake.World.ModDeathFx.Alpha(JsonUtility.FromJson<CustomFxEmitter>("{ \"image\": \"x.png\" }")), "added by default");
            var root = new GameObject("fx");
            try
            {
                var ps = GoF2Remake.World.ModDeathFx.Build(null, smoke, root.transform, Vector3.zero, 1f, out _);
                var r = ps.GetComponent<ParticleSystemRenderer>();
                StringAssert.Contains("AlphaBlend", r.sharedMaterial.shader.name);
                Assert.Greater(r.sortingFudge, 0f, "behind the fire");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
