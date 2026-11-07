// Es2ShipEffectsTests.cs
// Feature: CLAUDE.md "Free flight" > EVERSPACE 2's ship effects and rules ported 2026-11-02 (docs/notes/free-flight.md):
// the wing-tip contrails' opacity and life (Modding.Es2Contrails, after BP_ShipBase.UpdateContrails and
// P_Vehicle_Contrail), the headlights' bounce light numbers and the toggle's default (Flight.Headlights, after
// BP_Ship_Player.UpdateHeadLightBounce), the fixtures' shared flare opacity (Modding.Es2Headlights) and the collision
// damage (PlayerCollision.CollisionDamage, after BP_CollisionDamageComponent). Expected values: EVERSPACE 2's own numbers
// read out of its blueprints and assets (the contrail's 0.7..1 alignment range and 0.01 s floor, the 22 degree cone, the
// 50 000 cm divisor, 0.01 threshold and 1.5 exponent of the collision damage, the headlights on by default, the collision
// damage off).

using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class Es2ShipEffectsTests
    {
        // ---- contrails ----

        [Test]
        public void ContrailsShowOnlyWhileBoostingNoseFirst()
        {
            Assert.AreEqual(0f, Es2Contrails.Opacity(0f, 1f, 1f), "no boost: none");
            Assert.AreEqual(1f, Es2Contrails.Opacity(1f, 1f, 1f), 1e-6f, "full boost, straight ahead");
            Assert.AreEqual(0.5f, Es2Contrails.Opacity(0.5f, 1f, 1f), 1e-6f, "the eased boost level");
            Assert.AreEqual(0f, Es2Contrails.Opacity(1f, 0.7f, 1f), 1e-6f, "45 degrees off the velocity (cos 0.7): none");
            Assert.AreEqual(0.5f, Es2Contrails.Opacity(1f, 0.85f, 1f), 1e-6f, "halfway through the 0.7..1 range");
            Assert.AreEqual(0f, Es2Contrails.Opacity(1f, -1f, 1f), "flying backward: none");
        }

        [Test]
        public void ASmallContrailSocketIsFainterABigOneNotBrighter()
        {
            Assert.AreEqual(0.5f, Es2Contrails.Opacity(1f, 1f, 0.5f), 1e-6f);
            Assert.AreEqual(1f, Es2Contrails.Opacity(1f, 1f, 1.387f), 1e-6f, "min(scale, 1)");
        }

        [Test]
        public void AContrailParticleLivesItsOpacityInSecondsAtLeastAHundredth()
        {
            Assert.AreEqual(1f, Es2Contrails.Life(1f));
            Assert.AreEqual(0.3f, Es2Contrails.Life(0.3f), 1e-6f);
            Assert.AreEqual(0.01f, Es2Contrails.Life(0f), 1e-6f, "the lifetime distribution's MinOutput");
        }

        // ---- headlights ----

        [Test]
        public void TheBounceLightTakesTheConesRadiusWhereTheRayHits()
        {
            // sqrt((d / cos 22)^2 - d^2) = d tan 22
            Assert.AreEqual(100f * Mathf.Tan(22f * Mathf.Deg2Rad), Headlights.ConeRadius(100f), 1e-3f);
            Assert.AreEqual(0f, Headlights.ConeRadius(0f), 1e-6f);
        }

        [Test]
        public void TheBounceLightFadesWithTheHeadlightsFalloff()
        {
            Assert.AreEqual(10f, Headlights.Falloff(500f, 10f, 2f, 0f), 1e-6f, "at the source the base intensity");
            Assert.AreEqual(0f, Headlights.Falloff(500f, 10f, 2f, 500f), "nothing at the reach");
            Assert.AreEqual(0f, Headlights.Falloff(500f, 10f, 2f, 900f), "nothing beyond it");
            // halfway: (1 - 0.25)^2 = 0.5625
            Assert.AreEqual(5.625f, Headlights.Falloff(500f, 10f, 2f, 250f), 1e-4f);
            Assert.Less(Headlights.Falloff(500f, 10f, 4f, 250f), Headlights.Falloff(500f, 10f, 2f, 250f), "a higher exponent falls off sooner");
        }

        [Test]
        public void InterpToNeverPassesItsTarget()
        {
            Assert.AreEqual(0.4f, Headlights.InterpTo(0f, 1f, 0.1f, 4f), 1e-6f, "4/s over 0.1 s: 40 % of the way");
            Assert.AreEqual(1f, Headlights.InterpTo(0f, 1f, 1f, 4f), 1e-6f, "a long frame lands on it");
            Assert.AreEqual(1f, Headlights.InterpTo(0f, 1f, 0.1f, 0f), 1e-6f, "speed 0: at once (Unreal's rule)");
        }

        [Test]
        public void EachHeadlightFixtureSharesTheShipsFlareIntensity()
        {
            Assert.AreEqual(1f, Es2Headlights.FlareAlpha(1), 1e-6f);
            Assert.AreEqual(0.25f, Es2Headlights.FlareAlpha(4), 1e-6f, "HeadlightsIntensity 1 over four sockets");
            Assert.AreEqual(0f, Es2Headlights.FlareAlpha(0));
        }

        [Test]
        public void TheHeadlightsStartOnAndTheCollisionDamageOff()
        {
            using (new DataUiTestKit.PrefsSnapshot())
            {
                Assert.IsTrue(Settings.Headlights, "EVERSPACE 2's bHeadlightsEnabled starts on (its HUD warns while they are off)");
                Assert.IsFalse(Settings.CollisionDamage, "the original's collision rules unless asked for");
                Assert.IsFalse(Settings.BoostToggle, "the boost is held unless asked for (EVERSPACE 2's keyboard default)");
                Settings.Headlights = false;
                Assert.IsFalse(Headlights.On);
                Headlights.On = true;
                Assert.IsTrue(Settings.Headlights);
            }
        }

        // ---- collision damage ----

        [Test]
        public void NothingBelowFiveMetresASecondTheWholeHullAtFifty()
        {
            Assert.AreEqual(0f, PlayerCollision.CollisionDamage(4.9f, 1000f), "strength 0.0098 < MinDamagePercent 0.01");
            Assert.AreEqual(1000f, PlayerCollision.CollisionDamage(500f, 1000f), 1e-3f, "strength 1: the maximum hit points");
            Assert.AreEqual(0f, PlayerCollision.CollisionDamage(-20f, 1000f), "moving away: none");
        }

        [Test]
        public void CollisionDamageGrowsWithTheSpeedToThePowerOfOneAndAHalf()
        {
            // 50 m/s: strength 0.1, 0.1^1.5 = 0.0316 of the hull; 20 m/s: 0.04^1.5 = 0.008; 5 m/s: 0.01^1.5 = 0.001
            Assert.AreEqual(31.6228f, PlayerCollision.CollisionDamage(50f, 1000f), 1e-3f);
            Assert.AreEqual(8f, PlayerCollision.CollisionDamage(20f, 1000f), 1e-3f);
            Assert.AreEqual(1f, PlayerCollision.CollisionDamage(5f, 1000f), 1e-3f);
            Assert.AreEqual(PlayerCollision.CollisionDamage(50f, 1000f) * 2f, PlayerCollision.CollisionDamage(50f, 2000f), 1e-3f, "x the ship's maximum");
        }
    }
}
