// TouchFreeFlightTests.cs
// Free flight's touch layout (TouchControlsFreeFlight): the aim drag's floating stick (dead zone, linear to the radius,
// clamped, y up).

using GoF2Remake.UI;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class TouchFreeFlightTests
    {
        const float R = TouchControls.AimRadius, Dz = TouchControls.AimDeadZone;

        [Test]
        public void InsideTheDeadZoneDoesNotAim()
        {
            Assert.AreEqual(Vector2.zero, TouchControls.FreeAim(new Vector2(Dz * 0.9f, 0f), Dz));
            Assert.AreEqual(Vector2.zero, TouchControls.FreeAim(new Vector2(0f, TouchControls.FireAimDeadZone - 1f), TouchControls.FireAimDeadZone));
        }

        [Test]
        public void GrowsLinearlyToFullAtTheRadius()
        {
            float half = TouchControls.FreeAim(new Vector2(Dz + (R - Dz) / 2f, 0f), Dz).x;
            Assert.AreEqual(0.5f, half, 1e-4f);
            Assert.AreEqual(1f, TouchControls.FreeAim(new Vector2(R, 0f), Dz).x, 1e-4f);
        }

        [Test]
        public void ClampedBeyondTheRadiusAndKeepsTheDirection()
        {
            var a = TouchControls.FreeAim(new Vector2(3f * R, 3f * R), Dz);
            Assert.AreEqual(1f, a.magnitude, 1e-4f);
            Assert.AreEqual(a.x, -a.y, 1e-4f);   // panel y runs down, the aim's up
        }

        [Test]
        public void DraggingUpAimsUp()
        {
            Assert.Greater(TouchControls.FreeAim(new Vector2(0f, -R), Dz).y, 0.99f);
            Assert.Less(TouchControls.FreeAim(new Vector2(-R, 0f), Dz).x, -0.99f);
        }
    }
}
