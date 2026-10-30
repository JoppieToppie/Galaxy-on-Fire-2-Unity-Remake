// ChaseCameraTests.cs
// #114: the chase camera's damping against the original's (TargetFollowCamera::update 0x186e40). Per frame the original
// moves the camera position / look point by k(dt) of the way to their goals, k(dt) = c0 / dt + c1 + c2 dt + c3 dt^2 + c4 dt^3
// with the c's fitted polynomials in the coefficient x (TargetFollowCamera::aproximateCooefficientsForAproximationOfDamping
// Funktion 0x186a10; the two terms near x / 2 are the c2 candidates, the decompile's register order leaves which is which
// open, and both agree within 1 %). ChaseCamera.Ease is 1 - e^(-x dt / 2); the camera itself follows at ChaseCamera.Follow,
// 1 - e^(-x dt), twice that rate (2026.10.09's tighter follow, back after #114's original rate). Also the model's bank
// (PlayerEgo::update 0xaa5f4): roll = -yawRate * 2 pi / 4096 rad, tilt = pitchRate * 0.25 * 2 pi / 4096.

using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ChaseCameraTests
    {
        // The five polynomials of aproximateCooefficientsForAproximationOfDampingFunktion, in the order it writes them.
        static double[] Coefficients(double x)
        {
            var d = new double[9];
            d[0] = 1; for (int i = 1; i < 9; i++) d[i] = d[i - 1] * x;
            double p3 = d[1] * 1.2102111341477596e-09 + d[3] * 0.0422027858310806 + d[5] * 43.75888381977114 + d[7] * 15119.074437016332
                      + d[8] * -103298.31437796567 + d[6] * -969.134661736397 + d[4] * -1.7133856245383816 + d[2] * -1.233489837107569e-06;
            double p4 = -(d[1] * 1.4781876353802752e-07 + d[2] * 0.16651597434517493 + d[3] * 0.1488423578964816 + d[5] * 4448.097529049415
                      + d[7] * 1803725.9838256645 + d[8] * -12480406.26454869 + d[6] * -111015.302667393 + d[4] * -128.52917290808267
                      - 4.5716904903627806e-11);
            double p5 = d[1] * 0.5000009352113304 + d[3] * 0.3778121387254571 + d[5] * 112294.22315362946 + d[7] * 26011080.905412003
                      + d[8] * -149924392.42871693 + d[6] * -2122499.1658084425 + d[4] * -3466.199114885294 + d[2] * -0.0009647307184731492
                      - 2.8527534035103244e-10;
            double p6 = d[1] * 0.4999743866523157 + d[2] * 0.19280203402657284 + d[4] * 39522.4565731161 + d[6] * 31896097.579336446
                      + d[8] * 2837989977.8564253 + d[7] * -448847784.8572755 + d[5] * -1451302.48192741 + d[3] * -11.25222010529228
                      + 7.913368945635026e-09;
            double p7 = -(d[2] * 0.00422802001700341 + d[4] * 89689.50184512888 + d[6] * 56126838.41706436 + d[8] * 3091464099.7803183
                      + d[7] * -609933689.8511872 + d[5] * -3103835.9217881793 + d[3] * -1.6589554694294697 + d[1] * -4.3754915810647766e-06
                      + 1.4969893482859894e-09);
            return new[] { p3, p4, p5, p6, p7 };
        }

        static double Original(double x, double dt, bool swapLinear)
        {
            var c = Coefficients(x);
            double linear = swapLinear ? c[3] : c[2], inverse = swapLinear ? c[2] : c[3];
            return inverse / dt + c[4] + linear * dt + c[1] * dt * dt + c[0] * dt * dt * dt;
        }

        // The handling-dependent coefficients run 0.003 .. ~0.0065 for the ships' handling (H 0..50); 0.005 / 0.006 the touch ones.
        [TestCase(0.003f), TestCase(0.005f), TestCase(0.006f)]
        public void EaseFollowsTheOriginalsDamping(float x)
        {
            foreach (float dt in new[] { 16f, 33f, 50f })
                foreach (bool swap in new[] { false, true })
                {
                    double original = Original(x, dt, swap), remake = ChaseCamera.Ease(x, dt);
                    Assert.AreEqual(1.0, remake / original, 0.03, $"x {x}, dt {dt}: original {original:0.00000}, remake {remake:0.00000}");
                }
        }

        [Test]
        public void EaseIsFrameRateIndependent()
        {
            // Two 16.5 ms steps leave as much of the gap as one 33 ms step.
            float x = 0.005f, two = (1f - ChaseCamera.Ease(x, 16.5f)) * (1f - ChaseCamera.Ease(x, 16.5f));
            Assert.AreEqual(1f - ChaseCamera.Ease(x, 33f), two, 1e-5f);
            Assert.AreEqual(1f - Mathf.Exp(-0.5f * x * 33f), ChaseCamera.Ease(x, 33f), 1e-6f);
        }

        [Test]
        public void TheCameraFollowsAtTwiceTheOriginalsRate()
        {
            // One frame of Follow leaves as much of the gap as two of the original's damping over the same time.
            float x = 0.005f;
            float original = (1f - ChaseCamera.Ease(x, 16.5f)) * (1f - ChaseCamera.Ease(x, 16.5f)) * (1f - ChaseCamera.Ease(x, 16.5f)) * (1f - ChaseCamera.Ease(x, 16.5f));
            Assert.AreEqual(1f - ChaseCamera.Follow(x, 33f), original, 1e-5f);
            Assert.AreEqual(1f - Mathf.Exp(-x * 33f), ChaseCamera.Follow(x, 33f), 1e-6f);
        }

        [Test]
        public void TheModelBanksByTheTurnRate()
        {
            // A full stick at handling 20 turns at 285 (FlightModelTests): 285 / 4096 of a turn, ~25 degrees of roll.
            Assert.AreEqual(285f * 360f / 4096f, 285f * ShipController.RateToDegrees, 1e-4f);
            Assert.AreEqual(25.05f, 285f * ShipController.RateToDegrees, 0.01f);
            Assert.AreEqual(285f * 2f * Mathf.PI / 4096f * Mathf.Rad2Deg, 285f * ShipController.RateToDegrees, 1e-3f);
        }

        [Test]
        public void TheShakeKeepsItsDirectionForAnOriginalFrame()
        {
            // Players' reports (2026-10-18): a new random jolt every rendered frame shook wildly at 144 fps; the original draws
            // one per frame of its own (OriginalFrame.Ms, 60 Hz on iOS: two 144 fps frames hold it).
            Vector3 dir = Vector3.zero;
            float age = ChaseCamera.JitterMs;
            var first = ChaseCamera.HeldJitter(ref dir, ref age, 7f);
            Assert.AreNotEqual(Vector3.zero, first);
            Assert.IsTrue(Mathf.Abs(first.x) <= 1f && Mathf.Abs(first.y) <= 1f && Mathf.Abs(first.z) <= 1f);
            // The first draw carries the 7 ms past the frame's end into the next one, so the held frames are those with
            // 14, 21, ... ms still short of JitterMs: one at 16.7 ms (three at the old 33.3).
            int held = Mathf.FloorToInt((ChaseCamera.JitterMs - 1e-3f) / 7f) - 1;
            for (int i = 0; i < held; i++) Assert.AreEqual(first, ChaseCamera.HeldJitter(ref dir, ref age, 7f), "held through a 144 fps frame " + i);
            int changed = 0;
            for (int i = 0; i < 20; i++)
            {
                var before = dir;
                if (ChaseCamera.HeldJitter(ref dir, ref age, ChaseCamera.JitterMs) != before) changed++;
            }
            Assert.Greater(changed, 15, "a new one each original frame");
        }
    }
}
