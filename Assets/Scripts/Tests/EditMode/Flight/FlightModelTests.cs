// FlightModelTests.cs
// The player's flight model (CLAUDE.md "Recovered facts already implemented" (Flight), "Navigation" (autopilot bank),
// "UI and platforms" (brake, strafe), "Debug panel" (mass of the heavy hulls)).
// Expected numbers: the decompiled formulas quoted in CLAUDE.md and FlightModel's comments, checked against
// PlayerEgo::left / down in Reference/decompiled (target = (int)(v * 750 * H) / 63 as an integer division, ramp
// dt * H / ((3.3 - sens [x1.45 down / x1.25 up in tilt mode]) * 20)), worked out by hand (Python for the turn rate:
// 285 rate units = 51.6632 deg/s for H = 24). The mass scales are the remake's own: tested against CLAUDE.md's numbers
// (carrier 4.9 deg/s against 21.6) and as properties.

using GoF2Remake.Data;
using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class FlightModelTests
    {
        const float RateToRad = 2f * Mathf.PI / 65536f * 0.033f;   // handleShip: rate * 1.5258789e-05 * 2 pi * 0.033

        static FlightModel Model(float handling = 120f, int upgrades = 0, float agility = 0f,
                                 int boostSpeed = 0, int boostMs = 0, int rechargeMs = 0, bool original = true)
        {
            var m = new FlightModel { TrackAnalogInput = !original };
            m.Configure(new FlightStats
            {
                handling = handling, handlingUpgrades = upgrades, agility = agility,
                boostSpeed = boostSpeed, boostDurationMs = boostMs, boostRechargeMs = rechargeMs,
            });
            return m;
        }

        static FlightModel.FrameResult Fly(FlightModel m, Vector2 steer, float dtMs) => m.Step(steer, dtMs, Vector3.up, Vector3.right);

        static SettingsScope<bool> NoBoostCheat()
        {
            var s = new SettingsScope<bool>(() => Cheats.NoBoostCooldown, v => Cheats.NoBoostCooldown = v);
            Cheats.NoBoostCooldown = false;
            return s;
        }

        // ---- handling ----------------------------------------------------------------------------------------------

        [Test]
        public void HandlingIsShipHandlingTimes20()
        {
            // H = (handling / 100 + 0.2 upgrades) * (1 + agility / 100) * 20
            Assert.AreEqual(24f, Model(120f).Handling, 1e-4f);
            Assert.AreEqual(20f, Model(100f).Handling, 1e-4f);
        }

        [Test]
        public void HandlingUpgradesAddPointTwoEach()
        {
            Assert.AreEqual(32f, Model(120f, upgrades: 2).Handling, 1e-4f);
        }

        [Test]
        public void AgilityScalesHandling()
        {
            Assert.AreEqual(28.8f, Model(120f, agility: 20f).Handling, 1e-4f);
            Assert.AreEqual(30.8f, Model(120f, upgrades: 1, agility: 10f).Handling, 1e-4f);   // (1.4 + 0.14) * 20
        }

        [Test]
        public void CargoPenaltyOnlyWhenEnabled()
        {
            // H * 0.6 + H * (1 - load / max) * 0.4
            var m = new FlightModel();
            var s = new FlightStats { handling = 120f, cargoAffectsHandling = true, cargoLoad = 50f, cargoCapacity = 100f };
            m.Configure(s);
            Assert.AreEqual(19.2f, m.EffectiveHandling, 1e-4f);
            s.cargoLoad = 100f;
            Assert.AreEqual(14.4f, m.EffectiveHandling, 1e-4f);
            s.cargoLoad = 500f;   // overloaded: clamped at full
            Assert.AreEqual(14.4f, m.EffectiveHandling, 1e-4f);
            s.cargoAffectsHandling = false;
            Assert.AreEqual(24f, m.EffectiveHandling, 1e-4f);
        }

        // ---- turn rates ------------------------------------------------------------------------------------------------

        [Test]
        public void MaxTurnRateForBettyHandling()
        {
            // trunc(750 * 24) / 63 = 285 (integer division) -> 285 * 2 pi / 65536 * 0.033 rad/ms = 51.6632 deg/s
            Assert.AreEqual(51.6632f, Model(120f).MaxTurnRateDegPerSec, 1e-3f);
        }

        [Test]
        public void TurnTargetUsesIntegerDivision()
        {
            var m = Model(120f);
            for (int i = 0; i < 200; i++) Fly(m, new Vector2(1f, 0f), 10f);
            Assert.AreEqual(285f, m.YawRate, 1e-4f);   // not 285.71
            for (int i = 0; i < 400; i++) Fly(m, new Vector2(-1f, 0f), 10f);
            Assert.AreEqual(-285f, m.YawRate, 1e-4f);
        }

        [Test]
        public void TargetScalesWithHandling()
        {
            // H 28.8 -> 21600 / 63 = 342; H 32 -> 24000 / 63 = 380
            var a = Model(120f, agility: 20f);
            var b = Model(120f, upgrades: 2);
            for (int i = 0; i < 300; i++) { Fly(a, new Vector2(0f, 1f), 10f); Fly(b, new Vector2(0f, 1f), 10f); }
            Assert.AreEqual(342f, a.PitchRate, 1e-4f);
            Assert.AreEqual(380f, b.PitchRate, 1e-4f);
        }

        [Test]
        public void RampStepIsDtTimesHOverDenominator()
        {
            // dt * H / ((3.3 - 1) * 20) = 10 * 24 / 46
            var m = Model(120f);
            Fly(m, new Vector2(1f, 0f), 10f);
            Assert.AreEqual(240f / 46f, m.YawRate, 1e-4f);
            Fly(m, new Vector2(1f, 0f), 10f);
            Assert.AreEqual(480f / 46f, m.YawRate, 1e-4f);
        }

        [Test]
        public void SensitivityShortensTheRamp()
        {
            var m = Model(120f);
            m.Sensitivity = 2f;   // (3.3 - 2) * 20 = 26
            Fly(m, new Vector2(1f, 0f), 10f);
            Assert.AreEqual(240f / 26f, m.YawRate, 1e-4f);
        }

        [Test]
        public void TiltModePitchUsesDownAndUpScales()
        {
            // PlayerEgo::down: sens * 1.45 -> (3.3 - 1.45) * 20 = 37; up: sens * 1.25 -> 41
            var down = Model(120f); down.TiltMode = true;
            Fly(down, new Vector2(0f, 1f), 10f);
            Assert.AreEqual(240f / 37f, down.PitchRate, 1e-4f);
            var up = Model(120f); up.TiltMode = true;
            Fly(up, new Vector2(0f, -1f), 10f);
            Assert.AreEqual(-240f / 41f, up.PitchRate, 1e-4f);
            var stick = Model(120f);
            Fly(stick, new Vector2(0f, 1f), 10f);
            Assert.AreEqual(240f / 46f, stick.PitchRate, 1e-4f);
        }

        [Test]
        public void RatesDecayLinearlyWithoutInput()
        {
            // dt * H / 126 per frame, to zero and no further
            var m = Model(120f);
            for (int i = 0; i < 200; i++) Fly(m, new Vector2(1f, 0f), 10f);
            Fly(m, Vector2.zero, 10f);
            Assert.AreEqual(285f - 240f / 126f, m.YawRate, 1e-3f);
            for (int i = 0; i < 1000; i++) Fly(m, Vector2.zero, 10f);
            Assert.AreEqual(0f, m.YawRate);
        }

        [Test]
        public void OriginalRuleNeverSlowsAHeldTurn()
        {
            // PlayerEgo::left: the rate only grows toward the target while the direction is held
            var m = Model(120f, original: true);
            for (int i = 0; i < 200; i++) Fly(m, new Vector2(1f, 0f), 10f);
            for (int i = 0; i < 200; i++) Fly(m, new Vector2(0.5f, 0f), 10f);
            Assert.AreEqual(285f, m.YawRate, 1e-4f);
        }

        [Test]
        public void AnalogRuleTracksPartialInput()
        {
            // the remake's analog mode: trunc(0.5 * 750 * 24) / 63 = 142
            var m = Model(120f, original: false);
            for (int i = 0; i < 200; i++) Fly(m, new Vector2(1f, 0f), 10f);
            for (int i = 0; i < 400; i++) Fly(m, new Vector2(0.5f, 0f), 10f);
            Assert.AreEqual(142f, m.YawRate, 1e-4f);
        }

        [Test]
        public void TurnPerFrameFromTheRate()
        {
            var m = Model(120f);
            for (int i = 0; i < 200; i++) Fly(m, new Vector2(1f, 1f), 10f);
            var r = Fly(m, new Vector2(1f, 1f), 10f);
            float expected = 10f * 285f * RateToRad * Mathf.Rad2Deg;   // 0.51663 deg
            Assert.AreEqual(expected, r.yawDeg, 1e-4f);
            Assert.AreEqual(expected, r.pitchDeg, 1e-4f);
            Assert.AreEqual(0.516632f, r.yawDeg, 1e-4f);
        }

        [Test]
        public void VisualBankFollowsInput()
        {
            var original = Model(120f, original: true);
            Fly(original, new Vector2(0.3f, -0.2f), 10f);
            Assert.AreEqual(24f, original.VisualYawBank, 1e-4f);    // the input's sign x H
            Assert.AreEqual(-24f, original.VisualPitchBank, 1e-4f);
            var analog = Model(120f, original: false);
            Fly(analog, new Vector2(0.5f, 0f), 10f);
            Assert.AreEqual(12f, analog.VisualYawBank, 1e-4f);
            Fly(analog, Vector2.zero, 10f);
            Assert.AreEqual(0f, analog.VisualYawBank);
        }

        // ---- speed, throttle, brake --------------------------------------------------------------------------------

        [Test]
        public void ForwardIsDtTimesThrottleTimesBaseSpeed()
        {
            var m = Model(120f);
            Assert.AreEqual(200f, Fly(m, Vector2.zero, 100f).forwardUnits, 1e-3f);   // 2 units/ms
            m.SetThrottle(0.5f);
            Assert.AreEqual(100f, Fly(m, Vector2.zero, 100f).forwardUnits, 1e-3f);
        }

        [Test]
        public void ThrottleIsClamped()
        {
            var m = Model(120f);
            m.ChangeThrottle(5f);
            Assert.AreEqual(1f, m.Throttle);
            m.ChangeThrottle(-0.25f);
            Assert.AreEqual(0.75f, m.Throttle, 1e-6f);
            m.SetThrottle(-3f);
            Assert.AreEqual(0f, m.Throttle);
        }

        [Test]
        public void BrakeStopsTheEnginesButKeepsTheThrottle()
        {
            var m = Model(120f);
            m.SetThrottle(0.8f);
            m.Braking = true;
            Assert.AreEqual(0f, Fly(m, Vector2.zero, 100f).forwardUnits);
            Assert.AreEqual(0.8f, m.Throttle, 1e-6f);
            m.Braking = false;
            Assert.AreEqual(160f, Fly(m, Vector2.zero, 100f).forwardUnits, 1e-3f);
        }

        // ---- boost ----------------------------------------------------------------------------------------------------

        [TestCase(60, 3f)]    // Linear Boost: int(1.2) + 2 = 3 (1.5x, not the shop's 1.6x)
        [TestCase(80, 3f)]    // Cyclotron: int(1.6) + 2 = 3, equal to Linear
        [TestCase(100, 4f)]
        [TestCase(50, 3f)]
        [TestCase(40, 2f)]    // int(0.8) + 2 = 2: no faster than base
        public void BoostSpeedIsTruncated(int boostSpeed, float expected)
        {
            var m = Model(120f, boostSpeed: boostSpeed, boostMs: 5000, rechargeMs: 20000);
            m.Boost();
            Assert.IsTrue(m.IsBoosting);
            Assert.AreEqual(expected, m.CurrentSpeed, 1e-5f);
        }

        [Test]
        public void BoostSpeedUntruncatedWhenAsked()
        {
            var m = new FlightModel { TruncateBoostSpeed = false };
            m.Configure(new FlightStats { handling = 120f, boostSpeed = 60, boostDurationMs = 5000, boostRechargeMs = 20000 });
            m.Boost();
            Assert.AreEqual(3.2f, m.CurrentSpeed, 1e-5f);
        }

        [Test]
        public void NoBoosterNoBoost()
        {
            var m = Model(120f, boostSpeed: 60, boostMs: 5000, rechargeMs: 0);
            Assert.IsFalse(m.HasBooster);
            Assert.IsFalse(m.BoostReady);
            m.Boost();
            Assert.IsFalse(m.IsBoosting);
            Assert.AreEqual(FlightModel.BaseSpeed, m.CurrentSpeed);
        }

        [Test]
        public void BoostSetsFullThrottleAndFliesFaster()
        {
            var m = Model(120f, boostSpeed: 100, boostMs: 5000, rechargeMs: 20000);
            m.SetThrottle(0.3f);
            m.Boost();
            Assert.AreEqual(1f, m.Throttle);
            Assert.AreEqual(400f, Fly(m, Vector2.zero, 100f).forwardUnits, 1e-3f);
        }

        [Test]
        public void BoostLastsItsDurationThenRecharges()
        {
            using (NoBoostCheat())
            {
                var m = Model(120f, boostSpeed: 100, boostMs: 5000, rechargeMs: 20000);
                m.Boost();
                for (int i = 0; i < 50; i++) Fly(m, Vector2.zero, 100f);   // 5000 ms: not over yet (timer > duration)
                Assert.IsTrue(m.IsBoosting);
                Fly(m, Vector2.zero, 100f);
                Assert.IsFalse(m.IsBoosting);
                Assert.AreEqual(FlightModel.BaseSpeed, m.CurrentSpeed);
                Assert.IsFalse(m.BoostReady);
                Assert.AreEqual(0f, m.BoostRechargePercent, 1e-4f);
                m.Boost();
                Assert.IsFalse(m.IsBoosting, "no boost while recharging");
            }
        }

        [Test]
        public void BoostRechargeTakesTheRechargeTime()
        {
            using (NoBoostCheat())
            {
                var m = Model(120f, boostSpeed: 100, boostMs: 1000, rechargeMs: 20000);
                m.Boost();
                for (int i = 0; i < 11; i++) Fly(m, Vector2.zero, 100f);   // the boost ends
                Assert.IsFalse(m.IsBoosting);
                for (int i = 0; i < 100; i++) Fly(m, Vector2.zero, 100f);  // 10 s
                Assert.AreEqual(0.5f, m.BoostRechargePercent, 1e-3f);
                for (int i = 0; i < 90; i++) Fly(m, Vector2.zero, 100f);   // 19 s
                Assert.IsFalse(m.BoostReady);
                for (int i = 0; i < 10; i++) Fly(m, Vector2.zero, 100f);   // 20 s
                Assert.IsTrue(m.BoostReady);
                Assert.AreEqual(1f, m.BoostRechargePercent);
            }
        }

        [Test]
        public void BoostVisualRampsOverASixth()
        {
            var m = Model(120f, boostSpeed: 100, boostMs: 6000, rechargeMs: 20000);
            Assert.AreEqual(0f, m.BoostVisualPercent);
            m.Boost();
            m.TickBoost(500f);
            Assert.AreEqual(0.5f, m.BoostVisualPercent, 1e-4f);
            m.TickBoost(2500f);
            Assert.AreEqual(1f, m.BoostVisualPercent, 1e-4f);
            m.TickBoost(2500f);   // 5500: the last sixth
            Assert.AreEqual(0.5f, m.BoostVisualPercent, 1e-4f);
        }

        // ---- collision push, strafe --------------------------------------------------------------------------------

        [Test]
        public void CollisionPushDecaysByPointSevenPerFrame()
        {
            var m = Model(120f);
            m.AddCollisionPush(2f);
            Assert.AreEqual(20f, Fly(m, Vector2.zero, 10f).sidePushUnits, 1e-4f);
            Assert.AreEqual(14f, Fly(m, Vector2.zero, 10f).sidePushUnits, 1e-4f);
            for (int i = 0; i < 30; i++) Fly(m, Vector2.zero, 10f);   // 2 * 0.7^n drops below 0.01
            Assert.AreEqual(0f, Fly(m, Vector2.zero, 10f).sidePushUnits);
        }

        [Test]
        public void StrafeRampsByOneAndAHalfPerFrame()
        {
            // v = ramp * dir * min(H * 30 * 0.002, 2), ramp 0.1 x1.5 per original frame (OriginalFrame.Ms) up to 1
            const float frame = OriginalFrame.Ms;
            var m = Model(120f);   // H 24 -> 1.44
            m.Strafe(1, frame);
            Assert.AreEqual(0.144f, m.StrafeVelocity, 1e-5f);
            m.Strafe(1, frame);
            Assert.AreEqual(0.216f, m.StrafeVelocity, 1e-5f);
            for (int i = 0; i < 20; i++) m.Strafe(1, frame);
            Assert.AreEqual(1.44f, m.StrafeVelocity, 1e-4f);
            m.Strafe(-1, frame);
            Assert.AreEqual(-1.44f, m.StrafeVelocity, 1e-4f);
        }

        [Test]
        public void StrafeSpeedIsCappedAtTwo()
        {
            var m = Model(200f);   // H 40 -> 2.4, capped
            for (int i = 0; i < 20; i++) m.Strafe(1, 1000f / 30f);
            Assert.AreEqual(2f, m.StrafeVelocity, 1e-4f);
        }

        [Test]
        public void StrafeMoveDecaysAndResetsTheRamp()
        {
            const float frame = OriginalFrame.Ms;
            var m = Model(120f);
            m.Strafe(1, frame);
            Assert.AreEqual(0.144f * frame, m.StepStrafe(frame), 1e-3f);
            Assert.AreEqual(0.144f * 0.7f, m.StrafeVelocity, 1e-5f);
            for (int i = 0; i < 40; i++) m.StepStrafe(frame);
            Assert.AreEqual(0f, m.StrafeVelocity);
            m.Strafe(1, frame);   // the ramp is back at 0.1
            Assert.AreEqual(0.144f, m.StrafeVelocity, 1e-5f);
        }

        // ---- auto-level (PlayerEgo::roll) ------------------------------------------------------------------------------

        static void Frame(float rollDeg, out Vector3 up, out Vector3 right)
        {
            var q = Quaternion.AngleAxis(rollDeg, Vector3.forward);
            up = q * Vector3.up;
            right = q * Vector3.right;
        }

        static float LevelStep(FlightModel m, float rollDeg, float dtMs)
        {
            Frame(rollDeg, out var up, out var right);
            return m.Step(Vector2.zero, dtMs, up, right).rollDeg;
        }

        [Test]
        public void LevelRateFarFromLevel()
        {
            var m = Model(120f);
            m.LevelOutNow();
            Assert.AreEqual(0.00035f * 10f * Mathf.Rad2Deg, Mathf.Abs(LevelStep(m, 30f, 10f)), 1e-4f);   // |lean| 0.5 > 0.3
        }

        [Test]
        public void LevelRateNearLevel()
        {
            var m = Model(120f);
            m.LevelOutNow();
            Assert.AreEqual(0.00025f * 10f * Mathf.Rad2Deg, Mathf.Abs(LevelStep(m, 10f, 10f)), 1e-4f);   // |lean| 0.17
        }

        [Test]
        public void LevelRateUpsideDown()
        {
            var m = Model(120f);
            m.LevelOutNow();
            Assert.AreEqual(0.00075f * 10f * Mathf.Rad2Deg, Mathf.Abs(LevelStep(m, 150f, 10f)), 1e-4f);
        }

        [Test]
        public void LevelStepIsCappedAt60Ms()
        {
            var m = Model(120f);
            m.LevelOutNow();
            Assert.AreEqual(0.00035f * 60f * Mathf.Rad2Deg, Mathf.Abs(LevelStep(m, 30f, 100f)), 1e-4f);
        }

        [Test]
        public void LevelRollOpposesTheLean()
        {
            var a = Model(120f); a.LevelOutNow();
            var b = Model(120f); b.LevelOutNow();
            float left = LevelStep(a, 30f, 10f), right = LevelStep(b, -30f, 10f);
            Assert.AreNotEqual(0f, left);
            Assert.AreEqual(-left, right, 1e-6f);
        }

        [Test]
        public void LevelOvershootGoesFine()
        {
            // the lean changes sides: one step at 0.00035, then 0.0002 until level
            var m = Model(120f);
            m.LevelOutNow();
            LevelStep(m, 30f, 10f);
            Assert.AreEqual(0.00035f * 10f * Mathf.Rad2Deg, Mathf.Abs(LevelStep(m, -20f, 10f)), 1e-4f);
            Assert.AreEqual(0.0002f * 10f * Mathf.Rad2Deg, Mathf.Abs(LevelStep(m, -10f, 10f)), 1e-4f);
            Assert.AreEqual(0.0002f * 10f * Mathf.Rad2Deg, Mathf.Abs(LevelStep(m, -40f, 10f)), 1e-4f);
        }

        [Test]
        public void LevelEndsWhenLevel()
        {
            var m = Model(120f);
            m.LevelOutNow();
            Assert.IsTrue(m.IsLeveling);
            Assert.AreEqual(0f, LevelStep(m, 0.5f, 10f));   // |lean| 0.0087 < 0.015, up.y > 0
            Assert.IsFalse(m.IsLeveling);
        }

        [Test]
        public void UpsideDownIsNotLevel()
        {
            // #57: a ship exactly upside down has no sideways lean but up.y < 0
            var m = Model(120f);
            m.LevelOutNow();
            LevelStep(m, 180f, 10f);
            Assert.IsTrue(m.IsLeveling);
        }

        [Test]
        public void SteeringAfterNeutralStickEndsLevelOut()
        {
            Frame(30f, out var up, out var right);
            var m = Model(120f);
            m.LevelPitch = true;
            m.LevelOutNow();
            m.Step(new Vector2(1f, 0f), 10f, up, right);   // still held from the double tap: keeps leveling
            Assert.IsTrue(m.IsLeveling);
            m.Step(Vector2.zero, 10f, up, right);          // one neutral frame (between a double tap's taps): not armed yet
            m.Step(new Vector2(1f, 0f), 10f, up, right);
            Assert.IsTrue(m.IsLeveling, "a single neutral frame doesn't arm it");
            for (int i = 0; i < 16; i++) m.Step(Vector2.zero, 10f, up, right);   // 160 ms neutral: armed
            Assert.IsTrue(m.IsLeveling);
            m.Step(new Vector2(0.2f, 0f), 10f, up, right);
            Assert.IsTrue(m.IsLeveling, "a thumb resting just off centre doesn't end it");
            m.Step(new Vector2(1f, 0f), 10f, up, right);
            Assert.IsFalse(m.IsLeveling);
        }

        [Test]
        public void SteeringWithoutLettingGoEndsLevelOutAfterTheGrace()
        {
            Frame(30f, out var up, out var right);
            var m = Model(120f);
            m.LevelPitch = true;
            m.LevelOutNow();
            for (int i = 0; i < 25; i++) m.Step(new Vector2(1f, 0f), 10f, up, right);   // 250 ms held: the double tap's grace
            Assert.IsTrue(m.IsLeveling);
            for (int i = 0; i < 6; i++) m.Step(new Vector2(0.2f, 0f), 10f, up, right);  // a thumb resting off centre
            Assert.IsTrue(m.IsLeveling);
            m.Step(new Vector2(0f, 0.5f), 10f, up, right);                              // still steering past 300 ms
            Assert.IsFalse(m.IsLeveling);
        }

        [Test]
        public void SteeringEndsARollOnlyLevelOut()
        {
            Frame(30f, out var up, out var right);
            var m = Model(120f);
            m.LevelOutNow();
            for (int i = 0; i < 25; i++) m.Step(new Vector2(1f, 1f), 10f, up, right);   // the grace: still levelling
            Assert.IsTrue(m.IsLeveling);
            for (int i = 0; i < 10; i++) m.Step(new Vector2(1f, 1f), 10f, up, right);
            Assert.IsFalse(m.IsLeveling);
        }

        [Test]
        public void ASteepNoseEndsARollOnlyLevelOut()
        {
            // nose 80 deg up, rolled 40 deg: rolling about the nose can't make the ship upright, it rolled for good
            var q = Quaternion.AngleAxis(-80f, Vector3.right) * Quaternion.AngleAxis(40f, Vector3.forward);
            var m = Model(120f);
            m.LevelOutNow();
            Assert.AreEqual(0f, m.Step(Vector2.zero, 10f, q * Vector3.up, q * Vector3.right).rollDeg);
            Assert.IsFalse(m.IsLeveling);
        }

        [Test]
        public void ALevelOutRequestWaitsASecond()
        {
            Frame(30f, out var up, out var right);
            var m = Model(120f);
            m.AlignToHorizon();
            for (int i = 0; i < 99; i++) Assert.AreEqual(0f, m.Step(Vector2.zero, 10f, up, right).rollDeg);
            Assert.IsTrue(m.LevelRequested);
            Assert.IsFalse(m.IsLeveling);
            Assert.AreNotEqual(0f, m.Step(Vector2.zero, 10f, up, right).rollDeg);   // 1000 ms: levelling
            Assert.IsTrue(m.IsLeveling);
        }

        [Test]
        public void SteeringDropsALevelOutRequest()
        {
            Frame(30f, out var up, out var right);
            var m = Model(120f);
            m.AlignToHorizon();
            for (int i = 0; i < 20; i++) m.Step(new Vector2(0.4f, 0f), 10f, up, right);   // the double tap's second tap
            Assert.IsTrue(m.LevelRequested, "the tap that made the request keeps it");
            for (int i = 0; i < 20; i++) m.Step(new Vector2(0.4f, 0f), 10f, up, right);   // held on, steering
            Assert.IsFalse(m.LevelRequested);
            for (int i = 0; i < 150; i++) m.Step(Vector2.zero, 10f, up, right);
            Assert.IsFalse(m.IsLeveling, "dropped until a new request");

            m.AlignToHorizon();
            m.StopLeveling();                                                            // a manual roll
            Assert.IsFalse(m.LevelRequested);
        }

        [Test]
        public void AThumbRestingOnTheStickKeepsALevelOutRequest()
        {
            // the touch stick has no dead zone: a thumb left on it after the double tap reads a few pixels (squared)
            Frame(30f, out var up, out var right);
            var m = Model(120f);
            m.AlignToHorizon();
            for (int i = 0; i < 100; i++) m.Step(new Vector2(0.02f, -0.01f), 10f, up, right);
            Assert.IsTrue(m.IsLeveling);
        }

        [Test]
        public void StopLevelingCancels()
        {
            var m = Model(120f);
            m.LevelOutNow();
            m.StopLeveling();
            Assert.IsFalse(m.IsLeveling);
            Assert.AreEqual(0f, LevelStep(m, 30f, 10f));
        }

        static void Pitched(float noseUpRad, out Vector3 up, out Vector3 right)
        {
            up = new Vector3(0f, Mathf.Cos(noseUpRad), -Mathf.Sin(noseUpRad));
            right = Vector3.right;
        }

        [Test]
        public void LevelPitchBringsTheNoseDown()
        {
            // remake (#37): 0.0006 rad/ms beyond 10 deg-ish, a positive local-X turn lowers the nose
            Pitched(0.5f, out var up, out var right);
            var m = Model(120f);
            m.LevelPitch = true;
            m.LevelOutNow();
            var r = m.Step(Vector2.zero, 10f, up, right);
            Assert.AreEqual(0.006f * Mathf.Rad2Deg, r.pitchDeg, 1e-4f);
            Assert.IsTrue(m.IsLeveling);
            Pitched(-0.5f, out up, out right);
            var n = Model(120f);
            n.LevelPitch = true;
            n.LevelOutNow();
            Assert.AreEqual(-0.006f * Mathf.Rad2Deg, n.Step(Vector2.zero, 10f, up, right).pitchDeg, 1e-4f);
        }

        [Test]
        public void LevelPitchSlowsNearTheHorizon()
        {
            // rate x clamp01(|angle| / 0.17 + 0.25)
            Pitched(0.05f, out var up, out var right);
            var m = Model(120f);
            m.LevelPitch = true;
            m.LevelOutNow();
            float expected = 0.0006f * (0.05f / 0.17f + 0.25f) * 10f;
            Assert.AreEqual(expected * Mathf.Rad2Deg, m.Step(Vector2.zero, 10f, up, right).pitchDeg, 1e-4f);
        }

        [Test]
        public void LevelPitchDoneWithinPointZeroOneRad()
        {
            Pitched(0.005f, out var up, out var right);
            var m = Model(120f);
            m.LevelPitch = true;
            m.LevelOutNow();
            Assert.AreEqual(0f, m.Step(Vector2.zero, 10f, up, right).pitchDeg);
            Assert.IsFalse(m.IsLeveling);
        }

        [Test]
        public void LevelPitchOffKeepsTheNose()
        {
            Pitched(0.5f, out var up, out var right);
            var m = Model(120f);
            m.LevelOutNow();
            Assert.AreEqual(0f, m.Step(Vector2.zero, 10f, up, right).pitchDeg);
            Assert.IsFalse(m.IsLeveling, "the original levels the roll only");
        }

        [Test]
        public void SteepNoseComesDownBeforeTheRoll()
        {
            // a nose more than ~45 deg up: the roll waits
            var q = Quaternion.AngleAxis(-57.3f, Vector3.right) * Quaternion.AngleAxis(30f, Vector3.forward);
            var up = q * Vector3.up;
            var right = q * Vector3.right;
            Assume.That(Mathf.Abs(right.y), Is.GreaterThan(0.015f));
            var m = Model(120f);
            m.LevelPitch = true;
            m.LevelOutNow();
            var r = m.Step(Vector2.zero, 10f, up, right);
            Assert.AreEqual(0f, r.rollDeg);
            Assert.AreNotEqual(0f, r.pitchDeg);
        }

        // ---- autopilot bank, maneuver step ------------------------------------------------------------------------------

        [Test]
        public void AutopilotBankSaturatesAtOnePointTwoH()
        {
            // limit = 750 H / 63 * 1.2; the bank shown = rate / (750 / 63) = 1.2 H
            var m = Model(120f);
            for (int i = 0; i < 100; i++) m.AutopilotBank(1f, 33.333f, 33.333f);
            Assert.AreEqual(28.8f, m.VisualYawBank, 1e-3f);
            var n = Model(120f);
            for (int i = 0; i < 100; i++) n.AutopilotBank(-1f, 33.333f, 33.333f);
            Assert.AreEqual(-28.8f, n.VisualYawBank, 1e-3f);
        }

        [Test]
        public void AutopilotBankForASmallTurn()
        {
            // target = turn * limit * 15.139845 -> bank = turn * 15.139845 * 1.2 H, the turn per original frame (OriginalFrame.Ms):
            // a turn over a 33.333 ms step is half of one per 60 Hz frame
            var m = Model(120f);
            for (int i = 0; i < 100; i++) m.AutopilotBank(1e-4f, 33.333f, 33.333f);
            Assert.AreEqual(1e-4f * OriginalFrame.Ms / 33.333f * 15.139845f * 1.2f * 24f, m.VisualYawBank, 1e-4f);
        }

        [Test]
        public void AutopilotBankIndependentOfFrameRate()
        {
            var a = Model(120f);
            var b = Model(120f);
            for (int i = 0; i < 30; i++) a.AutopilotBank(1e-4f, 33.333f, 33.333f);
            for (int i = 0; i < 60; i++) b.AutopilotBank(0.5e-4f, 16.6665f, 16.6665f);
            Assert.AreEqual(a.VisualYawBank, b.VisualYawBank, 1e-4f);
        }

        [Test]
        public void AutopilotBankApproachesAtDtHOver81()
        {
            var m = Model(120f);
            m.AutopilotBank(1f, 10f, 33.333f);   // target at the limit, one step of 10 * 24 / 81
            Assert.AreEqual(10f * 24f / 81f / (750f / 63f), m.VisualYawBank, 1e-4f);
        }

        [Test]
        public void AutopilotBankReset()
        {
            var m = Model(120f);
            for (int i = 0; i < 20; i++) m.AutopilotBank(1f, 33.333f, 33.333f);
            m.ResetAutopilotBank();
            m.AutopilotBank(0f, 33.333f, 33.333f);
            Assert.AreEqual(0f, m.VisualYawBank);
        }

        [Test]
        public void ManeuverStepSetsYawRateAndFliesOn()
        {
            var m = Model(120f);
            var r = m.StepManeuver(100f, 360f);
            Assert.AreEqual(360f, m.YawRate);
            Assert.AreEqual(360f / (750f / 63f), m.VisualYawBank, 1e-4f);
            Assert.AreEqual(0f, m.VisualPitchBank);
            Assert.AreEqual(200f, r.forwardUnits, 1e-3f);
            Assert.AreEqual(0f, r.yawDeg);
        }

        // ---- mass (remake debug hulls) --------------------------------------------------------------------------------

        [Test]
        public void MassZeroIsTheOriginal()
        {
            var m = Model(120f);
            Assert.AreEqual(1f, m.TurnScale);
            Assert.AreEqual(1f, m.Inertia);
            Assert.AreEqual(1f, m.RollScale);
            Assert.AreEqual(1f, m.SideScale);
            Assert.AreEqual(0f, m.AccelMs);
        }

        [Test]
        public void FullMassScales()
        {
            var m = Model(120f);
            m.Mass = 1f;
            Assert.AreEqual(0.15f, m.TurnScale, 1e-5f);
            Assert.AreEqual(0.15f * 0.18f, m.Inertia, 1e-5f);
            Assert.AreEqual(0.25f, m.RollScale, 1e-5f);
            Assert.AreEqual(0.2f, m.SideScale, 1e-5f);
            Assert.AreEqual(7000f, m.AccelMs, 1e-3f);
        }

        [Test]
        public void CarrierTurnsAtAFifthOfAFighter()
        {
            // CLAUDE.md: the carrier (mass 0.91) 4.9 deg/s against 21.6
            var m = Model(120f);
            m.Mass = 0.91f;
            Assert.AreEqual(4.9f / 21.6f, m.TurnScale, 0.005f);
        }

        [Test]
        public void HeavyHullLowersTheTopRate()
        {
            var m = Model(120f);
            m.Mass = 1f;
            for (int i = 0; i < 2000; i++) Fly(m, new Vector2(1f, 0f), 10f);   // ramp x0.027: ~300 frames
            Assert.AreEqual(285f * 0.15f, m.YawRate, 1e-2f);
        }

        [Test]
        public void HeavyHullEasesItsSpeed()
        {
            var m = Model(120f);
            m.Mass = 1f;
            m.SetThrottle(0f);
            Fly(m, Vector2.zero, 1000f);
            Assert.AreEqual(2f - 2f / 7000f * 1000f, m.MoveSpeed, 1e-4f);
            for (int i = 0; i < 6; i++) Fly(m, Vector2.zero, 1000f);
            Assert.AreEqual(0f, m.MoveSpeed, 1e-4f);
            var light = Model(120f);
            light.SetThrottle(0f);
            Fly(light, Vector2.zero, 10f);
            Assert.AreEqual(0f, light.MoveSpeed);
        }

        [Test]
        public void MassMonotonic()
        {
            var m = Model(120f);
            float lastTurn = 2f, lastSide = 2f, lastRoll = 2f, lastInertia = 2f;
            for (int i = 0; i <= 10; i++)
            {
                m.Mass = i / 10f;
                Assert.Less(m.TurnScale, lastTurn);
                Assert.Less(m.SideScale, lastSide);
                Assert.Less(m.RollScale, lastRoll);
                Assert.Less(m.Inertia, lastInertia);
                Assert.LessOrEqual(m.Inertia, m.TurnScale);
                lastTurn = m.TurnScale; lastSide = m.SideScale; lastRoll = m.RollScale; lastInertia = m.Inertia;
            }
        }

        // The chase camera follows free flight that many times faster (ChaseCamera): an Inflict (handling 125, H 25) turns at
        // 125 deg/s x (25 / 20)^0.75 = 147.77, softly capped: 125 + 22.77 / 10 = 127.28 deg/s, against the original's full
        // stick 750 x 25 / 63 = 297.6 units = 53.95 deg/s.
        [Test]
        public void FreeTurnSpeedupIsTheFreeRateOverTheOriginal()
        {
            using var sens = new SettingsScope<float>(() => Settings.Sensitivity, v => Settings.Sensitivity = v);
            Settings.Sensitivity = 1f;
            var m = Model(handling: 125f);
            Assert.AreEqual(1f, m.FreeTurnSpeedup, 1e-6f, "the original flight");
            m.FreeFlight = true;
            Assert.AreEqual(127.28f / 53.95f, m.FreeTurnSpeedup, 0.01f);
        }

        // The free turn rate's soft cap (players' feedback): up to EVERSPACE 2's 125 deg/s (handling 100, H 20) by the
        // handling, past it a tenth of the rest. Read through FreeTurnSpeedup x the original's full-stick rate.
        [TestCase(80f, 125f * 0.84590f)]    // H 16: (16/20)^0.75 = 0.8459, below the cap: unchanged
        [TestCase(100f, 125f)]              // H 20: exactly EVERSPACE 2's
        [TestCase(200f, 125f * 1.06818f)]   // H 40: (40/20)^0.75 = 1.6818 -> 1 + 0.06818 (it was 210 deg/s)
        public void FreeTurnRateIsSoftlyCappedAtEverspace2s(float handling, float degPerSec)
        {
            using var sens = new SettingsScope<float>(() => Settings.Sensitivity, v => Settings.Sensitivity = v);
            Settings.Sensitivity = 1f;
            var m = Model(handling: handling);
            m.FreeFlight = true;
            float original = 750f * (handling / 5f) / 63f * RateToRad * 1000f * Mathf.Rad2Deg;
            Assert.AreEqual(degPerSec, Mathf.Max(1f, m.FreeTurnSpeedup) * original, 0.1f);
        }

        // ---- the multiplayer cruise mode (CLAUDE.md "Navigation", Fast-forward) ------------------------------------

        [Test]
        public void CruiseLooksLikeABoostWithoutBeingOne()
        {
            var m = Model(boostSpeed: 100, boostMs: 3000, rechargeMs: 5000);
            m.UpdateCruise(true, FlightModel.CruiseEaseMs / 2f);
            Assert.AreEqual(0.5f, m.BoostVisualPercent, 1e-4f, "eased in");
            m.UpdateCruise(true, FlightModel.CruiseEaseMs);
            Assert.AreEqual(1f, m.BoostVisualPercent, 1e-4f);
            Assert.IsTrue(m.LooksBoosting);
            Assert.IsFalse(m.IsBoosting, "missiles, the recharge and volatile goods keep the real boost");
            Assert.IsTrue(m.BoostReady);
            m.UpdateCruise(false, FlightModel.CruiseEaseMs);
            Assert.AreEqual(0f, m.BoostVisualPercent, 1e-4f, "eased out");
            Assert.IsFalse(m.LooksBoosting);
        }
    }
}
