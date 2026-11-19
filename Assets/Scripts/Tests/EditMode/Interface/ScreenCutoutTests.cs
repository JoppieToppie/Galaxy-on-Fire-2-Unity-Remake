// SafeArea.Device with Options > Graphics "Camera cutout" (Settings.ScreenCutout): kept clear, both sides, full screen.
using GoF2Remake.Data;
using GoF2Remake.UI;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ScreenCutoutTests : SandboxTest
    {
        // An Android phone in landscape with the camera hole on the left: only that side is inset (y up).
        static readonly Rect PunchHoleLeft = new Rect(110f, 0f, 2290f, 1080f);
        // An iPhone in landscape: the Dynamic Island's inset on both sides, the home indicator's at the bottom.
        static readonly Rect IPhone = new Rect(186f, 63f, 2622f - 2f * 186f, 1206f - 63f);

        [Test]
        public void KeepClearIsTheDefaultAndInsetsTheCutoutSideOnly()
        {
            using var cutout = new SettingsScope<int>(() => Settings.ScreenCutout, v => Settings.ScreenCutout = v);
            Settings.Reset(new[] { "screenCutout" });
            Assert.AreEqual(Settings.ScreenCutoutClear, Settings.ScreenCutout, "keeping clear of the cutout stays the default");
            Assert.AreEqual(new Vector4(110f, 0f, 0f, 0f), SafeArea.Device(PunchHoleLeft, 2400f, 1080f, Settings.ScreenCutoutClear));
        }

        [Test]
        public void BothSidesMirrorsTheLargerSideInset()
        {
            Assert.AreEqual(new Vector4(110f, 110f, 0f, 0f), SafeArea.Device(PunchHoleLeft, 2400f, 1080f, Settings.ScreenCutoutBothSides));
            var flipped = new Rect(0f, 0f, 2290f, 1080f);   // the phone turned over: the hole on the right
            Assert.AreEqual(new Vector4(110f, 110f, 0f, 0f), SafeArea.Device(flipped, 2400f, 1080f, Settings.ScreenCutoutBothSides));
            // The top and bottom stay as reported (the home indicator isn't mirrored to the top).
            Assert.AreEqual(new Vector4(186f, 186f, 0f, 63f), SafeArea.Device(IPhone, 2622f, 1206f, Settings.ScreenCutoutBothSides));
        }

        [Test]
        public void FullScreenIgnoresTheDeviceInsets()
        {
            Assert.AreEqual(Vector4.zero, SafeArea.Device(PunchHoleLeft, 2400f, 1080f, Settings.ScreenCutoutFull));
            Assert.AreEqual(Vector4.zero, SafeArea.Device(IPhone, 2622f, 1206f, Settings.ScreenCutoutFull));
        }

        [Test]
        public void NonsenseSafeAreasAreStillIgnored()
        {
            var tiny = new Rect(0f, 0f, 500f, 1080f);
            Assert.AreEqual(Vector4.zero, SafeArea.Device(tiny, 2400f, 1080f, Settings.ScreenCutoutClear));
            var past = new Rect(-20f, 0f, 2500f, 1080f);   // reaching past the screen: clamped
            Assert.AreEqual(Vector4.zero, SafeArea.Device(past, 2400f, 1080f, Settings.ScreenCutoutBothSides));
        }

        [Test]
        public void ChangingTheOptionLaysTheScreensOutAgain()
        {
            using var cutout = new SettingsScope<int>(() => Settings.ScreenCutout, v => Settings.ScreenCutout = v);
            Settings.ScreenCutout = Settings.ScreenCutoutClear;
            int clear = UiLayout.Revision;
            Settings.ScreenCutout = Settings.ScreenCutoutFull;
            Assert.AreNotEqual(clear, UiLayout.Revision, "UiLayout.Revision follows the option, so every screen re-applies its insets");
            Settings.ScreenCutout = 7;
            Assert.AreEqual(Settings.ScreenCutoutFull, Settings.ScreenCutout, "clamped");
            CollectionAssert.Contains(Settings.GraphicsKeys, "screenCutout");
        }
    }
}
