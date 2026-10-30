// ManeuverTests.cs
// The dodge (CLAUDE.md "Recovered facts already implemented", the dodge; Maneuver.cs header from PlayerEgo::initManeuver /
// updateManeuver). Expected numbers by hand from the decompiled formulas: per frame, k = 1 - t / 1200, slide
// dt * 4 * k * H * 0.05 units toward the swipe, heading k * 0.006283186 rad per original frame away from it, yaw rate
// sin(pi t / 1200) * H * 0.05 * 750 * 0.4 (H 24 -> 360 at the middle), over at 1200 ms.

using GoF2Remake.Flight;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ManeuverTests
    {
        [Test]
        public void StartsOnlyDodgesAndOneAtATime()
        {
            var m = new Maneuver();
            Assert.IsFalse(m.Active);
            Assert.IsFalse(m.Start(0));
            Assert.IsFalse(m.Start(3), "the U-turn exists but nothing starts it");
            Assert.IsTrue(m.Start(1));
            Assert.AreEqual(1, m.Type);
            Assert.IsFalse(m.Start(2), "a dodge doesn't restart a running one");
            Assert.AreEqual(1, m.Type);
        }

        [Test]
        public void FirstFrameSlideAndTurn()
        {
            var m = new Maneuver();
            m.Start(1);
            m.Step(10f, 24f, out float slide, out float heading, out float yaw);
            Assert.AreEqual(10f * 4f * 24f * 0.05f, slide, 1e-4f);                 // 48 units to the left
            Assert.AreEqual(0.006283186f * 10f / OriginalFrame.Ms, heading, 1e-7f);    // nose to the right
            Assert.AreEqual(0f, yaw, 1e-5f);                                         // sin(0)
        }

        [Test]
        public void RightDodgeMirrorsLeft()
        {
            var l = new Maneuver(); l.Start(1);
            var r = new Maneuver(); r.Start(2);
            for (int i = 0; i < 60; i++)
            {
                l.Step(10f, 24f, out float ls, out float lh, out float ly);
                r.Step(10f, 24f, out float rs, out float rh, out float ry);
                Assert.AreEqual(-ls, rs, 1e-5f);
                Assert.AreEqual(-lh, rh, 1e-7f);
                Assert.AreEqual(-ly, ry, 1e-4f);
            }
        }

        [Test]
        public void YawRatePeaksAtTheMiddle()
        {
            var m = new Maneuver();
            m.Start(1);
            float yaw = 0f;
            for (int i = 0; i <= 60; i++) m.Step(10f, 24f, out _, out _, out yaw);   // the 61st frame starts at t = 600
            Assert.AreEqual(-24f * 0.05f * 750f * 0.4f, yaw, 1e-3f);                // -360: the model's rate, + = left
        }

        [Test]
        public void LastsTwelveHundredMs()
        {
            var m = new Maneuver();
            m.Start(2);
            for (int i = 0; i < 119; i++) m.Step(10f, 24f, out _, out _, out _);
            Assert.IsTrue(m.Active);
            m.Step(10f, 24f, out _, out _, out _);
            Assert.IsFalse(m.Active);
            m.Step(10f, 24f, out float slide, out float heading, out float yaw);
            Assert.AreEqual(0f, slide);
            Assert.AreEqual(0f, heading);
            Assert.AreEqual(0f, yaw);
        }

        [Test]
        public void TotalSlideAndTurn()
        {
            // sum over 120 frames of 10 ms: slide 48 * sum(1 - i / 120) = 48 * 60.5 = 2904 units, heading 0.006283186 per
            // original frame (OriginalFrame.Ms) x 10 ms x 60.5
            var m = new Maneuver();
            m.Start(1);
            float slide = 0f, heading = 0f;
            while (m.Active)
            {
                m.Step(10f, 24f, out float s, out float h, out _);
                slide += s; heading += h;
            }
            Assert.AreEqual(2904f, slide, 0.05f);
            Assert.AreEqual(0.006283186f * 10f / OriginalFrame.Ms * 60.5f, heading, 1e-5f);
        }

        [Test]
        public void SlideScalesWithHandling()
        {
            var a = new Maneuver(); a.Start(1);
            var b = new Maneuver(); b.Start(1);
            a.Step(10f, 24f, out float sa, out _, out _);
            b.Step(10f, 48f, out float sb, out _, out _);
            Assert.AreEqual(2f * sa, sb, 1e-4f);
        }

        [Test]
        public void CancelStops()
        {
            var m = new Maneuver();
            m.Start(1);
            m.Cancel();
            Assert.IsFalse(m.Active);
            Assert.IsTrue(m.Start(2));
        }
    }
}
