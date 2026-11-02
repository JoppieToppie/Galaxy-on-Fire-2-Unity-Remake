// StillLockTests.cs
// The remake's stationary-lock rule (Core/StillLock.cs; docs/notes/navigation.md "Fast-forward"): a locked object counts
// as still after HoldMs under UnitsPerMs, a move above that starts the hold over, a jitter below it doesn't, another
// object starts over, a pause (no time) changes nothing and Clear forgets the object.

using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class StillLockTests
    {
        const float Frame = 16f;

        /// <summary>'ms' of frames with 'obj' moving 'unitsPerMs' along x from 'from'; the last frame's answer.</summary>
        static bool Hold(StillLock s, object obj, float ms, float unitsPerMs = 0f, float from = 0f)
        {
            bool still = false;
            for (float t = 0f; t < ms; t += Frame) still = s.Update(obj, new Vector3(from + unitsPerMs * t, 0f, 0f), Frame);
            return still;
        }

        [Test]
        public void AnObjectHeldStillCountsAfterTheHold()
        {
            var s = new StillLock();
            var rock = new object();
            Assert.IsFalse(Hold(s, rock, StillLock.HoldMs - 3 * Frame), "not yet");
            Assert.IsTrue(Hold(s, rock, 4 * Frame), "held still for the hold");
        }

        [Test]
        public void AMovingObjectNeverCounts()
        {
            var s = new StillLock();
            var ship = new object();
            Assert.IsFalse(Hold(s, ship, 3000f, 0.2f), "a ship on its way");
            Assert.IsTrue(Hold(s, ship, StillLock.HoldMs + 2 * Frame, 0f, 600f), "until it stops");
            Assert.IsFalse(s.Update(ship, new Vector3(2000f, 0f, 0f), Frame), "a move starts the hold over");
        }

        [Test]
        public void JitterUnderTheLimitStaysStill()
        {
            var s = new StillLock();
            var proxy = new object();
            bool still = false;
            for (int i = 0; i < 60; i++) still = s.Update(proxy, new Vector3(i % 2 == 0 ? 0f : StillLock.UnitsPerMs * Frame * 0.9f, 0f, 0f), Frame);
            Assert.IsTrue(still);
        }

        [Test]
        public void AnotherObjectOrClearStartsOver()
        {
            var s = new StillLock();
            object a = new object(), b = new object();
            Assert.IsTrue(Hold(s, a, StillLock.HoldMs + 2 * Frame));
            Assert.IsFalse(s.Update(b, Vector3.zero, Frame), "another object");
            Assert.IsTrue(Hold(s, b, StillLock.HoldMs + 2 * Frame));
            s.Clear();
            Assert.IsFalse(s.Update(b, Vector3.zero, Frame), "cleared");
            Assert.IsFalse(s.Update(null, Vector3.zero, Frame), "nothing locked");
        }

        [Test]
        public void APauseChangesNothing()
        {
            var s = new StillLock();
            var rock = new object();
            Assert.IsTrue(Hold(s, rock, StillLock.HoldMs + 2 * Frame));
            Assert.IsTrue(s.Update(rock, new Vector3(5000f, 0f, 0f), 0f), "no time passed: no speed to judge");
            Assert.IsFalse(s.Update(rock, new Vector3(9000f, 0f, 0f), Frame), "then it moved");
        }
    }
}
