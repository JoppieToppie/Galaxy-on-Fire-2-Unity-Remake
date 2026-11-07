// FreeFlightTests.cs
// Free flight's velocity step (FlightModel.StepFree), rebuilt from EVERSPACE 2's decompiled
// UShipMovementComponent::ApplyControlInputToVelocity and BP_Ship_Player's tick (Reference/research/
// es2_flight_measurements.md, "The decompiled movement"). Expected numbers: the game's law integrated by hand (Python,
// 1 ms steps): dv/dt = A (1 - (v / V)^3), A / V = 132 / 95.3 per second forward (the measured Sentinel), so at a 100 m/s
// top speed 64.2 / 80.9 / 90.8 / 95.8 / 98.1 m/s after 0.5 / 0.7 / 0.9 / 1.1 / 1.3 s (the gauge's points scaled);
// braking at a quarter of A (34.6 m/s^2): 100 m/s to rest in 2.89 s.

using GoF2Remake.Data;
using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class FreeFlightTests
    {
        const float MetresPerSecond = 50f;   // 1 unit / ms = 50 m/s (0.05 m per unit)
        const float Step = 10f;              // ms

        static FlightModel Model(int boostSpeed = 0, int boostMs = 0)
        {
            var m = new FlightModel { FreeFlight = true };
            m.Configure(new FlightStats { handling = 100f, boostSpeed = boostSpeed, boostDurationMs = boostMs, boostRechargeMs = 10000 });
            return m;
        }

        static float Fly(FlightModel m, float ms, FlightModel.FreeInput input, Quaternion rotation)
        {
            for (float t = 0; t < ms - 0.01f; t += Step) m.StepFree(input, Step, rotation, rotation * Vector3.up, rotation * Vector3.right);
            return m.FreeVelocity.magnitude * MetresPerSecond;
        }

        static float Fly(FlightModel m, float ms, FlightModel.FreeInput input) => Fly(m, ms, input, Quaternion.identity);

        static FlightModel.FreeInput Thrust(float thrust = 1f, float strafe = 0f, bool dampeners = true) =>
            new FlightModel.FreeInput { thrust = thrust, strafe = strafe, dampeners = dampeners };

        [Test]
        public void ForwardThrustEasesOutAsEverspace2Does()
        {
            var m = Model();
            float elapsed = 0f;
            foreach (var (t, expected) in new[] { (500f, 64.2f), (700f, 80.9f), (900f, 90.8f), (1100f, 95.8f), (1300f, 98.1f) })
            {
                float speed = Fly(m, t - elapsed, Thrust());
                elapsed = t;
                Assert.AreEqual(expected, speed, 1.2f, $"after {t} ms");
            }
            Assert.AreEqual(100f, Fly(m, 3000f, Thrust()), 0.6f, "the top speed");
        }

        [Test]
        public void NothingHeldBrakesAtAQuarterOfTheAcceleration()
        {
            var m = Model();
            Fly(m, 5000f, Thrust());
            Assert.AreEqual(100f - 34.6f, Fly(m, 1000f, Thrust(0f)), 1f, "linear: 34.6 m/s^2");
            Assert.AreEqual(0f, Fly(m, 2000f, Thrust(0f)), 1e-3f, "at rest within 2.9 s");
        }

        [Test]
        public void TheDampenersTurnTheVelocityWithTheNose()
        {
            var m = Model();
            Fly(m, 5000f, Thrust());
            var turned = Quaternion.Euler(0f, 90f, 0f);
            Fly(m, 1500f, Thrust(), turned);
            Assert.Less(Vector3.Angle(m.FreeVelocity, turned * Vector3.forward), 5f, "the turning boost pulls it round");

            // Dampeners off: coasting, the ship keeps its heading's old velocity; thrusting only steers it (no faster than
            // the top speed), so the velocity comes round more slowly than with the turning boost.
            var drifting = Model();
            Fly(drifting, 5000f, Thrust(dampeners: false));
            Fly(drifting, 1500f, Thrust(0f, dampeners: false), turned);
            Assert.AreEqual(90f, Vector3.Angle(drifting.FreeVelocity, turned * Vector3.forward), 0.1f, "coasting");
            var dampened = Model();
            Fly(dampened, 5000f, Thrust());
            Fly(drifting, 300f, Thrust(dampeners: false), turned);
            Fly(dampened, 300f, Thrust(), turned);
            Assert.Greater(Vector3.Angle(drifting.FreeVelocity, turned * Vector3.forward), Vector3.Angle(dampened.FreeVelocity, turned * Vector3.forward));
            Assert.AreEqual(100f, drifting.FreeVelocity.magnitude * MetresPerSecond, 0.6f, "never faster than the top speed");
        }

        [Test]
        public void ThrustAndStrafeShareOneTopSpeedInTheirDirection()
        {
            // The input (1, 1) is clamped to length 1: (0.707 x 50, 0.707 x 100) m/s = 79.1 m/s, not 50 and 100 apart.
            var m = Model();
            float speed = Fly(m, 6000f, Thrust(1f, 1f));
            Assert.AreEqual(79.1f, speed, 0.8f);
            var v = m.FreeVelocity * MetresPerSecond;
            Assert.AreEqual(35.4f, v.x, 1f);
            Assert.AreEqual(70.7f, v.z, 1f);
        }

        [Test]
        public void DampenersOffKeepTheMomentum()
        {
            var m = Model();
            Fly(m, 5000f, Thrust(dampeners: false));
            Assert.AreEqual(100f, Fly(m, 3000f, Thrust(0f, dampeners: false)), 1e-3f);
        }

        [Test]
        public void ABoostRunsDownFastAboveTheTopSpeed()
        {
            // The Me'al (x3.2 in free flight): after the boost the braking grows with (speed / top)^1.67, so it sheds
            // two thirds of the gap in a second (320 -> 174 m/s, by hand) where the plain 34.6 m/s^2 would take six.
            using var cheat = new SettingsScope<bool>(() => Cheats.NoEquipmentCooldown, v => Cheats.NoEquipmentCooldown = v);
            Cheats.NoEquipmentCooldown = false;
            var m = Model(boostSpeed: 200, boostMs: 20000);
            Fly(m, 3000f, Thrust());
            m.BoostHeld = true;
            float boosted = Fly(m, 8000f, Thrust());
            Assert.AreEqual(320f, boosted, 6f, "x3.2 the top speed");
            m.BoostHeld = false;
            Fly(m, 50f, Thrust(0f));   // the boost ends
            Assert.AreEqual(174f, Fly(m, 1000f, Thrust(0f)), 8f);
        }

        [Test]
        public void ABoostThatRunsDryReportsItForOneFrameAndWaitsForAFreshPress()
        {
            // EVERSPACE 2's tick: the energy at 0 while boosting stops the boost (TryToBoostWithEnergyDepletedSound); its
            // StartBoosting returns silently on an empty pool; here the pool restarts from a fifth full on a fresh press.
            using var cheat = new SettingsScope<bool>(() => Cheats.NoEquipmentCooldown, v => Cheats.NoEquipmentCooldown = v);
            Cheats.NoEquipmentCooldown = false;
            var m = Model(boostSpeed: 200, boostMs: 1000);
            Assert.IsTrue(m.CanStartBoost, "a full pool");
            m.BoostHeld = true;
            int dryFrames = 0;
            for (int i = 0; i < 120; i++)
            {
                m.StepFree(Thrust(), Step, Quaternion.identity, Vector3.up, Vector3.right);
                if (m.BoostRanDry) dryFrames++;
            }
            Assert.AreEqual(1, dryFrames, "reported on the one frame the pool empties (1 s of boost in 1.2 s of frames)");
            Assert.IsFalse(m.IsBoosting);
            Assert.IsFalse(m.CanStartBoost, "spent while the key is still held");
            m.BoostHeld = false;
            Fly(m, 3000f, Thrust());   // recharges 0.3 of 10 s: past the fifth it restarts from
            Assert.IsTrue(m.CanStartBoost, "released and a fifth full again");
            m.BoostHeld = true;
            Fly(m, 50f, Thrust());
            Assert.IsTrue(m.IsBoosting);
        }

        // ---- EVERSPACE 2's auto-roll (FlightModel.AutoRoll: GenerateAutoRollInput's numbers from the component's constructor:
        // at most 0.15 of the roll rate once the bank passes 15 deg, until within 3 deg, held back 2 s after a steep pitch)

        /// <summary>The bank as the model measures it: the right wing's drop against the horizon, degrees, 0 level.</summary>
        static float BankDeg(Quaternion rotation)
        {
            var right = rotation * Vector3.right; var up = rotation * Vector3.up;
            return Mathf.Atan2(-right.y, up.y) * Mathf.Rad2Deg;
        }

        /// <summary>Flies 'ms' with the input, turning the ship as ShipController does (Rotate(pitch, -yaw, roll) in its own
        /// frame); returns the rotation and the largest roll of one step (degrees).</summary>
        static (Quaternion rotation, float maxRollStep) Turn(FlightModel m, float ms, FlightModel.FreeInput input, Quaternion rotation)
        {
            float maxRoll = 0f;
            for (float t = 0; t < ms - 0.01f; t += Step)
            {
                var r = m.StepFree(input, Step, rotation, rotation * Vector3.up, rotation * Vector3.right);
                rotation = rotation * Quaternion.Euler(r.pitchDeg, -r.yawDeg, r.rollDeg);
                maxRoll = Mathf.Max(maxRoll, Mathf.Abs(r.rollDeg));
            }
            return (rotation, maxRoll);
        }

        [Test]
        public void AutoRollLevelsABankedShipAtAFifteenthOfTheRollRate()
        {
            var m = Model();
            m.AutoRoll = true;
            var banked = Quaternion.AngleAxis(40f, Vector3.forward);
            Assert.AreEqual(40f, Mathf.Abs(BankDeg(banked)), 0.01f, "a 40 deg bank to start");
            var (rotation, maxRollStep) = Turn(m, 6000f, Thrust(0f), banked);
            Assert.LessOrEqual(Mathf.Abs(BankDeg(rotation)), 3.5f, "within the 3 deg tolerance");
            // 0.15 x 125 deg/s at handling 100 = 18.75 deg/s: 0.1875 deg per 10 ms step at most
            Assert.LessOrEqual(maxRollStep, 0.19f, "never faster than MaxAutoRollInput 0.15 of the roll rate");
            Assert.Greater(maxRollStep, 0.15f, "and close to it while the bank is past 15 deg");
        }

        [Test]
        public void AutoRollIsOffByDefaultAndRestsInsideTheThreshold()
        {
            var m = Model();
            var (rotation, _) = Turn(m, 3000f, Thrust(0f), Quaternion.AngleAxis(40f, Vector3.forward));
            Assert.AreEqual(40f, Mathf.Abs(BankDeg(rotation)), 0.01f, "off: the bank stays");
            m.AutoRoll = true;
            (rotation, _) = Turn(m, 3000f, Thrust(0f), Quaternion.AngleAxis(10f, Vector3.forward));
            Assert.AreEqual(10f, Mathf.Abs(BankDeg(rotation)), 0.01f, "a 10 deg lean is under RollAngleThreshold 15: no roll");
        }

        [Test]
        public void AutoRollEasesInAgainTwoSecondsAfterAPitchInput()
        {
            var m = Model();
            m.AutoRoll = true;
            var banked = Quaternion.AngleAxis(60f, Vector3.forward);
            // a full pitch input (beyond PitchInputThreshold 0.9) resets the ease-in timer to EaseInTime 2 s
            var (rotation, _) = Turn(m, 300f, new FlightModel.FreeInput { aim = new Vector2(0f, 1f), dampeners = true }, banked);
            float before = Mathf.Abs(BankDeg(rotation));
            (rotation, _) = Turn(m, 500f, Thrust(0f), rotation);
            float early = before - Mathf.Abs(BankDeg(rotation));
            (rotation, _) = Turn(m, 1000f, Thrust(0f), rotation);
            float mid = Mathf.Abs(BankDeg(rotation));
            (rotation, _) = Turn(m, 500f, Thrust(0f), rotation);
            float late = mid - Mathf.Abs(BankDeg(rotation));
            Assert.Less(early, late * 0.5f, $"the first half second ({early:0.00} deg) rolls far less than the one after 1.5 s ({late:0.00} deg)");
            (rotation, _) = Turn(m, 6000f, Thrust(0f), rotation);
            Assert.LessOrEqual(Mathf.Abs(BankDeg(rotation)), 3.5f, "level in the end");
        }

        // ---- EVERSPACE 2's full-stop and low-speed passives (FlightBonus.fullStopDamage / lowSpeedResistance)

        static FlightModel ModelWith(float fullStopDamage, float lowSpeedResistance)
        {
            var s = new FlightStats { handling = 100f, boostRechargeMs = 10000 };
            s.original = s.ForStyle(false);
            s.free = s.ForStyle(true);
            s.free.bonus.fullStopDamage = s.original.bonus.fullStopDamage = fullStopDamage;
            s.free.bonus.lowSpeedResistance = s.original.bonus.lowSpeedResistance = lowSpeedResistance;
            var m = new FlightModel { FreeFlight = true };
            m.Configure(s);
            return m;
        }

        [Test]
        public void FullStopRaisesThePrimaryDamageOnlyWhileStandingStill()
        {
            var m = ModelWith(30f, 0f);
            Fly(m, 100f, Thrust(0f));
            Assert.IsTrue(m.AtFullStop);
            Assert.AreEqual(1.3f, m.WeaponDamageFactor, 1e-4f);
            Fly(m, 500f, Thrust());
            Assert.IsFalse(m.AtFullStop);
            Assert.AreEqual(1f, m.WeaponDamageFactor, 1e-4f);
            Fly(m, 5000f, Thrust(0f));   // braked to rest again
            Assert.AreEqual(1.3f, m.WeaponDamageFactor, 1e-4f);
        }

        [Test]
        public void LowSpeedResistanceComesASecondAfterDroppingBelowThirtyMetresASecond()
        {
            var m = ModelWith(0f, 20f);
            Fly(m, 5000f, Thrust());
            Assert.AreEqual(1f, m.DamageTakenFactor, 1e-4f, "at 100 m/s");
            // braking at 34.6 m/s^2 from 100 m/s: below 30 m/s after ~2.0 s, the resistance a second later
            Fly(m, 2500f, Thrust(0f));
            Assert.AreEqual(1f, m.DamageTakenFactor, 1e-4f, "below 30 m/s for only half a second");
            Fly(m, 1000f, Thrust(0f));
            Assert.AreEqual(0.8f, m.DamageTakenFactor, 1e-4f, "20 % less damage taken after a second");
            Fly(m, 2000f, Thrust());
            Assert.AreEqual(1f, m.DamageTakenFactor, 1e-4f, "gone at once above 30 m/s");
        }
    }
}
