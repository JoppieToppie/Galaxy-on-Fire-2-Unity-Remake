// ChorusFlightTests.cs
// Chorus flight (FlightStyles.Chorus, FlightModel.StepChorus), rebuilt from Chorus's decompiled USteeringComponent and its
// player ship curves (Reference/research/chorus_flight.md), at the remake's 100 m/s top speed (Chorus's 140 scaled, every
// timing kept). Expected numbers: the curves integrated by hand, in shares f of the top speed per second:
//   accelerating df/dt = 0.5 + 1.5 f up to f = 0.5 (t = ln 2.5 / 1.5 = 0.611 s), then 1.25: f = 0.372 at 0.5 s, 0.986 at
//   1.0 s, the top speed at 1.011 s;
//   coasting (no input) 0.5 / s: 50 m/s after 1 s, at rest after 2 s;
//   braking 0.9375 + 0.375 (f - 0.5) above half speed, then 0.9375: f = 0.487 after 0.5 s, at rest after 1.02 s;
//   the boost toward f = 2 at 1.714 + 0.2944 f: 2 after 0.47 s; back to 1 after it, 0.5 / s below 1.5 and more above.
//   Turning: the top rate x0.5 at rest, x1 from half to full speed, x0.35 at twice it; the rate reached in 0.2 s, gone the
//   frame the input is released.

using GoF2Remake.Data;
using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class ChorusFlightTests
    {
        const float MetresPerSecond = 50f;   // 1 unit / ms = 50 m/s (0.05 m per unit)
        const float Step = 5f;               // ms

        static FlightModel Model(bool booster = true)
        {
            var m = new FlightModel { FreeFlight = true, ChorusFlight = true };
            m.Configure(new FlightStats { handling = 100f, boostSpeed = 50, boostDurationMs = 5000, boostRechargeMs = booster ? 10000 : 0 });
            return m;
        }

        static void Fly(FlightModel m, float ms, float thrust, Quaternion rotation, Vector2 aim = default)
        {
            var input = new FlightModel.FreeInput { thrust = thrust, aim = aim };
            for (float t = 0; t < ms - 0.01f; t += Step) m.StepFree(input, Step, rotation, rotation * Vector3.up, rotation * Vector3.right);
        }

        static float Fly(FlightModel m, float ms, float thrust, Vector2 aim = default)
        {
            Fly(m, ms, thrust, Quaternion.identity, aim);
            return Speed(m);
        }

        static float Speed(FlightModel m) => m.FreeVelocity.magnitude * MetresPerSecond;

        [Test]
        public void ForwardReachesTheTopSpeedInOneSecond()
        {
            var m = Model();
            Assert.AreEqual(37.2f, Fly(m, 500f, 1f), 1f, "after 0.5 s");
            Assert.AreEqual(98.6f, Fly(m, 500f, 1f), 1f, "after 1 s");
            Assert.AreEqual(100f, Fly(m, 100f, 1f), 0.05f, "at 1.1 s: the top speed, not beyond");
            Assert.AreEqual(100f, Fly(m, 2000f, 1f), 0.05f, "held");
        }

        [Test]
        public void HalfThrustReachesTheSameTopSpeedSlower()
        {
            var m = Model();
            Assert.AreEqual(100f, Fly(m, 2100f, 0.5f), 0.05f, "the input scales the rate, not the target");
            var half = Model();
            Assert.Less(Fly(half, 1000f, 0.5f), 70f);
        }

        [Test]
        public void LettingGoCoastsToRestInTwoSeconds()
        {
            var m = Model();
            Fly(m, 1500f, 1f);
            Assert.AreEqual(50f, Fly(m, 1000f, 0f), 1f, "0.5 of the top speed a second");
            Assert.AreEqual(0f, Fly(m, 1050f, 0f), 1e-3f, "at rest");
            Assert.IsTrue(m.EnginesOff, "coasting burns nothing");
        }

        [Test]
        public void TheBrakeStopsTheShipInOneSecondAndThereIsNoReverse()
        {
            var m = Model();
            Fly(m, 1500f, 1f);
            Assert.AreEqual(48.7f, Fly(m, 500f, -1f), 1f, "after 0.5 s");
            Assert.AreEqual(0f, Fly(m, 550f, -1f), 1e-3f, "at rest after 1.02 s");
            Assert.AreEqual(0f, Fly(m, 1000f, -1f), 1e-3f, "no reverse flight");
        }

        [Test]
        public void TheBoostDoublesTheTopSpeedAndRunsBackDownAfterwards()
        {
            var m = Model();
            Fly(m, 1500f, 1f);
            m.BoostHeld = true;
            Assert.AreEqual(200f, Fly(m, 500f, 1f), 1f, "x2 in 0.47 s");
            Assert.IsTrue(m.IsBoosting);
            Assert.AreEqual(200f, Fly(m, 5000f, 1f), 0.05f, "held: no energy, no end");
            m.BoostHeld = false;
            Assert.Greater(Fly(m, 1000f, 1f), 130f, "it runs down over about two seconds");
            Assert.AreEqual(100f, Fly(m, 1200f, 1f), 0.05f, "back at the top speed");
        }

        [Test]
        public void ATappedBoostRunsItsMinimumDurationAndNeedsABooster()
        {
            var m = Model();
            Fly(m, 1500f, 1f);
            m.BoostHeld = true;
            Fly(m, Step, 1f);
            m.BoostHeld = false;
            Fly(m, 550f, 1f);
            Assert.IsTrue(m.IsBoosting, "MinBoostDuration 0.6 s");
            Fly(m, 100f, 1f);
            Assert.IsFalse(m.IsBoosting);

            var none = Model(booster: false);
            none.BoostHeld = true;
            Assert.AreEqual(100f, Fly(none, 3000f, 1f), 0.05f, "no booster, no boost");
        }

        static float YawAfter(FlightModel m, float ms, float thrust)
        {
            Fly(m, ms, thrust, new Vector2(1f, 0f));
            return m.YawRate;
        }

        [Test]
        public void TheTurnRateFollowsTheSpeed()
        {
            var rest = Model();
            float atRest = YawAfter(rest, 400f, 0f);
            var cruise = Model();
            Fly(cruise, 1500f, 1f);
            float atTop = YawAfter(cruise, 400f, 1f);
            Assert.AreEqual(0.5f, atRest / atTop, 0.01f, "half the rate at rest");
            var boosted = Model();
            Fly(boosted, 1500f, 1f);
            boosted.BoostHeld = true;
            Fly(boosted, 2000f, 1f);
            Assert.AreEqual(0.35f, YawAfter(boosted, 600f, 1f) / atTop, 0.01f, "0.35 of it at twice the top speed");
        }

        [Test]
        public void TheTurnRampsInATenthOfASecondPerHalfAndStopsDeadOnRelease()
        {
            var m = Model();
            Fly(m, 1500f, 1f);
            float full = YawAfter(m, 400f, 1f);
            var ramp = Model();
            Fly(ramp, 1500f, 1f);
            Assert.AreEqual(0.5f, YawAfter(ramp, 100f, 1f) / full, 0.03f, "half after 0.1 s: the rate in 0.2 s");
            Fly(ramp, Step, 1f);   // the input let go
            Assert.AreEqual(0f, ramp.YawRate, "no deceleration: it stops at once");
            YawAfter(ramp, 400f, 1f);
            Fly(ramp, Step, 1f, Quaternion.identity, new Vector2(-1f, 0f));
            Assert.LessOrEqual(Mathf.Abs(ramp.YawRate), full * 0.03f, "reversed: from zero, not through the old rate");
        }

        [Test]
        public void ADriftKeepsTheVelocityWhileTheNoseTurnsThenRecovers()
        {
            var m = Model();
            Fly(m, 1500f, 1f);
            m.DriftHeld = true;
            var turned = Quaternion.Euler(0f, 90f, 0f);
            Fly(m, 1000f, 1f, turned);
            Assert.IsTrue(m.Drifting);
            Assert.AreEqual(0f, Vector3.Angle(m.FreeVelocity, Vector3.forward), 0.1f, "the velocity keeps its world direction");
            Assert.AreEqual(100f, Speed(m), 0.05f, "no thrust and no drag at the top speed");
            m.DriftHeld = false;
            Fly(m, Step, 1f, turned);
            Assert.IsTrue(m.DriftRecovering);
            Fly(m, 700f, 1f, turned);
            float forward = Vector3.Dot(m.FreeVelocity, turned * Vector3.forward) * MetresPerSecond;
            Assert.AreEqual(100f, forward, 0.5f, "the recovery's 1.57 tops a second brings the forward speed back in 0.64 s");
            Assert.IsFalse(m.DriftRecovering, "it ends there");
            Fly(m, 800f, 1f, turned);
            float sideways = Vector3.Dot(m.FreeVelocity, turned * Vector3.right) * MetresPerSecond;
            Assert.AreEqual(0f, sideways, 0.5f, "the slide has faded within 1.33 s");
        }

        [Test]
        public void DriftAndBoostEndEachOther()
        {
            var m = Model();
            Fly(m, 1500f, 1f);
            m.BoostHeld = true;
            Fly(m, 200f, 1f);
            m.DriftHeld = true;
            Fly(m, Step, 1f);
            Assert.IsTrue(m.Drifting);
            Assert.IsFalse(m.IsBoosting, "pressing the drift stops the boost");
            Fly(m, 200f, 1f);
            Assert.IsFalse(m.IsBoosting, "and it doesn't start while drifting");
            m.BoostHeld = false;
            Fly(m, Step, 1f);
            m.BoostHeld = true;
            Fly(m, Step, 1f);
            Assert.IsFalse(m.Drifting, "pressing the boost ends the drift");
            Assert.IsTrue(m.IsBoosting);
        }

        [Test]
        public void TheTravelBoostTogglesToFiveAndAHalfTimesOutOfCombatOnly()
        {
            var m = Model(booster: false);   // a ship feature in Chorus: no booster needed
            Fly(m, 1500f, 1f);
            m.ToggleTravelBoost();
            Assert.IsFalse(m.TravelBoosting, "not allowed (hostiles, a jump...): nothing");
            m.TravelBoostAllowed = true;
            m.ToggleTravelBoost();
            Assert.IsTrue(m.TravelBoosting);
            Assert.AreEqual(550f, Fly(m, 3000f, 0f), 0.05f, "x5.5, the throttle held full whatever is pressed");
            m.ToggleTravelBoost();
            Assert.IsFalse(m.TravelBoosting, "a second press ends it");

            m.ToggleTravelBoost();
            Fly(m, 500f, 0f);
            Fly(m, Step, -1f);
            Assert.IsFalse(m.TravelBoosting, "the brake ends it");
            m.ToggleTravelBoost();
            Fly(m, 500f, 0f);
            m.TravelBoostAllowed = false;
            Fly(m, Step, 0f);
            Assert.IsFalse(m.TravelBoosting, "hostiles end it");
        }

        [Test]
        public void TheDashSlidesTheShipSidewaysWithoutTouchingItsVelocity()
        {
            var m = Model();
            Fly(m, 1500f, 1f);
            var before = m.FreeVelocity;
            Assert.IsTrue(m.ChorusDash(-1));
            float side = 0f;
            var input = new FlightModel.FreeInput { thrust = 1f };
            for (float t = 0; t < 1100f; t += Step)
            {
                var r = m.StepFree(input, Step, Quaternion.identity, Vector3.up, Vector3.right);
                side += r.move.x;
            }
            // 70 m/s (half the top speed) in 0.15 s, held to 0.25 s, gone over 0.75 s: 0.5 x 100 x 0.55 s.
            Assert.AreEqual(-27.5f, side * 0.05f, 0.3f, "metres to the left");
            Assert.IsFalse(m.Dashing);
            Assert.AreEqual(before.x, m.FreeVelocity.x, 1e-6f, "the momentum is untouched");

            Assert.IsTrue(m.ChorusDash(1));
            Fly(m, 100f, 1f);
            Assert.IsFalse(m.ChorusDash(1), "too early to buffer");
            Fly(m, 750f, 1f);
            Assert.IsTrue(m.ChorusDash(-1), "within the last 0.2 s: buffered");
            Fly(m, 200f, 1f);
            Assert.IsTrue(m.Dashing, "the buffered dash follows at once");
        }

        [Test]
        public void TheStyleIsNamedChorusEverywhere()
        {
            Assert.IsTrue(Modding.ModFlight.TryParseStyle("chorus", out int style));
            Assert.AreEqual(FlightStyles.Chorus, style);
            Assert.AreEqual(FlightStyles.Chorus, SessionRules.ParseFlight(SessionRules.FlightText(FlightStyles.Chorus)));
            Assert.IsTrue(FlightStyles.IsFree(FlightStyles.Chorus) && FlightStyles.IsFree(FlightStyles.Free) && !FlightStyles.IsFree(FlightStyles.Original));
        }

        [Test]
        public void ChorusHasItsOwnKeyLayout()
        {
            var prefs = new DataUiTestKit.PrefsSnapshot();   // every layout's bindings and the option, put back after
            try
            {
                Settings.FlightStyle = FlightStyles.Chorus;
                GameControls.ApplyStyle();
                GameControls.ResetToDefaults();
                Assert.AreEqual(FlightStyles.Chorus, GameControls.LoadedStyle);
                Assert.AreEqual("<keyboard>/space", GameControls.Paths(DataUiTestKit.Row("drift"), BindSlot.Key1)[0].ToLowerInvariant());
                Assert.AreEqual("<gamepad>/leftshoulder", GameControls.Paths(DataUiTestKit.Row("drift"), BindSlot.Pad)[0].ToLowerInvariant());
                Assert.AreEqual("<keyboard>/a", GameControls.Paths(DataUiTestKit.Row("dodgeLeft"), BindSlot.Key1)[0].ToLowerInvariant());
                Assert.AreEqual("<keyboard>/leftctrl", GameControls.Paths(DataUiTestKit.Row("travelBoost"), BindSlot.Key1)[0].ToLowerInvariant());
                Assert.AreEqual("<keyboard>/w", GameControls.Paths(DataUiTestKit.Row("thrust"), BindSlot.Key1)[0].ToLowerInvariant(), "free flight's thrust");
                Assert.AreEqual("<keyboard>/leftshift", GameControls.Paths(DataUiTestKit.Row("boost"), BindSlot.Key1)[0].ToLowerInvariant());
                foreach (var p in GameControls.Paths(DataUiTestKit.Row("strafe"), BindSlot.Key1)) Assert.AreEqual("", p, "no strafe");
                Assert.IsTrue(DataUiTestKit.Row("drift").visible(), "the drift row shows in Chorus's bindings");
                Assert.IsFalse(DataUiTestKit.Row("dampeners").visible());

                Settings.FlightStyle = FlightStyles.Free;
                GameControls.ApplyStyle();
                Assert.IsFalse(DataUiTestKit.Row("drift").visible(), "nor in free flight's");
                Assert.AreEqual("", GameControls.Paths(DataUiTestKit.Row("drift"), BindSlot.Key1)[0], "unbound there");
            }
            finally { prefs.Dispose(); GameControls.ApplyStyle(); }
        }
    }
}
