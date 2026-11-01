// MouseWheelTests.cs
// MouseWheel (CLAUDE.md "Mouse steering", the wheel on phones and tablets): a Samsung tablet's mouse sent fractional
// scroll (at most 0.625) as it moved; on mobile only 0.75 or more is a notch, desktops keep every scroll.

using GoF2Remake.UI;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class MouseWheelTests
    {
        [Test]
        public void MobileDropsTheTouchpadScroll()
        {
            Assert.AreEqual(0f, MouseWheel.Steps(0.625f, true));
            Assert.AreEqual(0f, MouseWheel.Steps(-0.3f, true));
        }

        [Test]
        public void MobileKeepsANotch()
        {
            Assert.AreEqual(1f, MouseWheel.Steps(1f, true));
            Assert.AreEqual(-1f, MouseWheel.Steps(-120f, true));
            Assert.AreEqual(0.75f, MouseWheel.Steps(0.75f, true));
        }

        [Test]
        public void DesktopKeepsEveryScroll()
        {
            Assert.AreEqual(0.3f, MouseWheel.Steps(0.3f, false), 1e-6f);
            Assert.AreEqual(1f, MouseWheel.Steps(120f, false));
            Assert.AreEqual(0f, MouseWheel.Steps(0f, false));
        }
    }
}
