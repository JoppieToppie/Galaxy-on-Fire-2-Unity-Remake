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
    }
}
