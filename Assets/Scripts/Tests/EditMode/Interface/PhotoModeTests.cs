// PhotoModeTests.cs
// Feature: the photo tools of Action Freeze and Inspect ship (UI.PhotoTools, players' suggestion "Everspace-style Action
// Freeze", 2026-10-17): the free camera (World.FreeCamera) flies anywhere within reach of the ship and above the floor; the
// frame cuts the screenshot (PhotoSettings.FrameRect); Reset (the bloom, lens flare, white balance, chromatic
// aberration and lens distortion too); Action Freeze's lens flare setting over the Graphics option; the hangar orbit takes the free pose, roll and field of view
// and keeps them through the blend back (World.HangarInspect); the composition guides (PhotoGuides) and the roll's full
// turn with its level and quarter-turn snaps (PhotoTools.SnapRoll); the white balance's temperature in kelvin
// (PhotoSettings.Kelvin) and a value typed into a row's box (OptionControl.TryNumber / EditText).

using GoF2Remake.Data;
using GoF2Remake.UI;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class PhotoModeTests
    {
        [Test]
        public void TheFreeCameraStartsWhereTheCameraIsAndLooksTheSameWay()
        {
            var cam = new FreeCamera();
            var rot = Quaternion.LookRotation(new Vector3(1f, 0.5f, 1f));
            cam.Begin(new Vector3(10f, 2f, 3f), rot, Vector3.zero, 100f);
            Assert.That(Vector3.Distance(cam.Position, new Vector3(10f, 2f, 3f)), Is.LessThan(1e-4f));
            Assert.That(Vector3.Angle(cam.Forward, rot * Vector3.forward), Is.LessThan(0.01f));
        }

        [Test]
        public void ItMovesInItsOwnFrameAndUpAlongTheWorlds()
        {
            var cam = new FreeCamera();
            cam.Begin(Vector3.zero, Quaternion.LookRotation(Vector3.right), Vector3.zero, 1000f);   // facing +x
            cam.Move(new Vector3(0f, 0f, 1f), 10f);
            Assert.That(Vector3.Distance(cam.Position, new Vector3(10f, 0f, 0f)), Is.LessThan(1e-3f), "forward");
            cam.Move(new Vector3(1f, 0f, 0f), 5f);
            Assert.That(Vector3.Distance(cam.Position, new Vector3(10f, 0f, -5f)), Is.LessThan(1e-3f), "right of +x is -z");
            cam.Move(new Vector3(0f, 1f, 0f), 3f);
            Assert.AreEqual(3f, cam.Position.y, 1e-3f, "up along the world's up");
        }

        [Test]
        public void ItStaysInReachOfTheShipAndAboveTheFloor()
        {
            var cam = new FreeCamera();
            cam.Begin(new Vector3(0f, 5f, 0f), Quaternion.identity, Vector3.zero, 50f, floor: 2f);
            cam.Move(new Vector3(0f, 0f, 1f), 500f);
            Assert.That(cam.Position.magnitude, Is.LessThanOrEqualTo(50.001f));
            cam.Move(new Vector3(0f, -1f, 0f), 500f);
            Assert.That(cam.Position.y, Is.GreaterThanOrEqualTo(2f));
        }

        [Test]
        public void ItNeverLooksStraightUpOrDown()
        {
            var cam = new FreeCamera();
            cam.Begin(Vector3.zero, Quaternion.identity, Vector3.zero, 10f);
            cam.Look(0f, 10f);
            Assert.AreEqual(FreeCamera.MaxPitch, cam.Pitch, 1e-5f);
            cam.Look(0f, -20f);
            Assert.AreEqual(-FreeCamera.MaxPitch, cam.Pitch, 1e-5f);
        }

        [TestCase(1920, 1080, 0f, 0, 0, 1920, 1080)]
        [TestCase(1920, 1080, 1f, 420, 0, 1080, 1080)]          // 1:1 in a wide picture: the middle square
        [TestCase(1920, 1080, 21f / 9f, 0, 128, 1920, 823)]     // wider than the picture: bars above and below
        [TestCase(1920, 1080, 9f / 16f, 656, 0, 608, 1080)]     // portrait
        public void TheFrameCutsTheMiddleOfThePicture(int w, int h, float aspect, int x, int y, int fw, int fh)
        {
            var r = PhotoSettings.FrameRect(w, h, aspect);
            Assert.AreEqual(new RectInt(x, y, fw, fh), r);
        }

        [Test]
        public void ResetPutsTheLookBack()
        {
            PhotoSettings.DefaultFov = 50f;
            PhotoSettings.Fov = 20f;
            PhotoSettings.Roll = 30f;
            PhotoSettings.Vignette = 0.5f;
            PhotoSettings.Dof = PhotoSettings.DofMode.Manual;
            PhotoSettings.Frame = 3;
            PhotoSettings.Reset();
            Assert.AreEqual(50f, PhotoSettings.Fov);
            Assert.AreEqual(0f, PhotoSettings.Roll);
            Assert.AreEqual(0f, PhotoSettings.Vignette);
            Assert.AreEqual(PhotoSettings.DofMode.Game, PhotoSettings.Dof);
            Assert.AreEqual(0, PhotoSettings.Frame);
        }

        [Test]
        public void ResetPutsTheBloomFlareAndLensBack()
        {
            PhotoSettings.Bloom = PhotoSettings.BloomMode.Custom;
            PhotoSettings.BloomAmount = 2.5f;
            PhotoSettings.Flare = PhotoSettings.FlareMode.Off;
            PhotoSettings.Temperature = 40f;
            PhotoSettings.Tint = -20f;
            PhotoSettings.Aberration = 0.5f;
            PhotoSettings.Distortion = -0.4f;
            PhotoSettings.Reset();
            Assert.AreEqual(PhotoSettings.BloomMode.Game, PhotoSettings.Bloom, "the game's bloom");
            Assert.AreEqual(1f, PhotoSettings.BloomAmount);
            Assert.AreEqual(PhotoSettings.FlareMode.Game, PhotoSettings.Flare);
            Assert.AreEqual(0f, PhotoSettings.Temperature);
            Assert.AreEqual(0f, PhotoSettings.Tint);
            Assert.AreEqual(0f, PhotoSettings.Aberration);
            Assert.AreEqual(0f, PhotoSettings.Distortion);
        }

        [Test]
        public void TheLensFlareFollowsTheOptionUntilActionFreezeSaysOtherwise()
        {
            bool option = Settings.LensFlare;
            var mode = PhotoSettings.Flare;
            try
            {
                Settings.LensFlare = true;
                PhotoSettings.Flare = PhotoSettings.FlareMode.Game;
                Assert.IsTrue(PhotoSettings.FlareShown(true), "Default: the Graphics option");
                PhotoSettings.Flare = PhotoSettings.FlareMode.Off;
                Assert.IsFalse(PhotoSettings.FlareShown(true), "off for the photo");
                Assert.IsTrue(PhotoSettings.FlareShown(false), "outside Action Freeze the option rules");
                Settings.LensFlare = false;
                PhotoSettings.Flare = PhotoSettings.FlareMode.On;
                Assert.IsTrue(PhotoSettings.FlareShown(true), "on for the photo though the option is off");
                Assert.IsFalse(PhotoSettings.FlareShown(false));
            }
            finally
            {
                Settings.LensFlare = option;
                PhotoSettings.Flare = mode;
            }
        }

        [Test]
        public void TheFocusDistanceRunsFromAMetreToThreeKilometres()
        {
            float before = PhotoSettings.Focus;
            try
            {
                PhotoSettings.Focus = 0f;
                Assert.AreEqual(1f, PhotoSettings.FocusMetres, 1e-3f);
                PhotoSettings.Focus = 1f;
                Assert.AreEqual(PhotoSettings.MaxFocusMetres, PhotoSettings.FocusMetres, 0.5f);
            }
            finally { PhotoSettings.Focus = before; }
        }

        [Test]
        public void APressThatBeganOnTheUiNeverTurnsTheCamera()
        {
            // A slider dragged off the settings panel: the press began on it, so the camera ignores it until release.
            var tools = new PhotoTools(new UnityEngine.UIElements.VisualElement(), null);
            bool overUi = true;
            System.Func<Vector2, bool> ui = _ => overUi;
            Assert.IsTrue(tools.PressOverUi(new Vector2(10f, 10f), ui), "pressed on the slider");
            overUi = false;   // dragged off it
            Assert.IsTrue(tools.PressOverUi(new Vector2(900f, 10f), ui), "still the slider's press");
            Assert.IsFalse(tools.PressOverUi(null, ui), "released");
            Assert.IsFalse(tools.PressOverUi(new Vector2(900f, 10f), ui), "a new press on the view turns the camera");
            overUi = true;    // that press passing over the UI stays the camera's
            Assert.IsFalse(tools.PressOverUi(new Vector2(10f, 10f), ui));
        }

        [TestCase(1920, 1080, 0, 1)]
        [TestCase(1920, 1080, 1, 2)]
        [TestCase(1920, 1080, 2, 4)]
        [TestCase(3840, 2160, 2, 4)]      // 15360 px wide: within a texture's 16384
        [TestCase(5120, 1440, 2, 3)]      // a super-wide screen at x4 would pass it: x3
        public void TheScreenshotSizeStaysWithinATexture(int w, int h, int size, int expected)
        {
            int before = PhotoSettings.Size;
            try { PhotoSettings.Size = size; Assert.AreEqual(expected, PhotoSettings.SuperSize(w, h)); }
            finally { PhotoSettings.Size = before; }
        }

        [Test]
        public void TheHangarOrbitTakesTheFreePoseAndKeepsItThroughTheBlendBack()
        {
            var insp = new HangarInspect();
            insp.Begin(new Vector3(0f, 10f, -100f), new Bounds(Vector3.zero, new Vector3(20f, 6f, 30f)));
            insp.Tick(5000f);   // fully blended in
            var go = new GameObject("cam");
            try
            {
                var pose = new Vector3(5f, 3f, -12f);
                insp.SetFreePose(pose, Quaternion.identity);
                insp.RollDeg = 20f;
                insp.Apply(go.transform, Vector3.zero, Quaternion.identity, 1f);
                Assert.That(Vector3.Distance(go.transform.position, pose), Is.LessThan(1e-3f));
                Assert.AreEqual(20f, go.transform.eulerAngles.z, 0.01f, "the roll about the view");
                insp.End();
                insp.Tick(300f);   // part way back: still the free pose's
                Assert.IsTrue(insp.Free);
                insp.Tick(5000f);  // back at the hangar camera: gone
                Assert.IsFalse(insp.Free);
                Assert.AreEqual(0f, insp.RollDeg);
                Assert.AreEqual(0f, insp.FovDeg);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheGridGuidesSitAtThirdsQuartersAndTheGoldenRatio()
        {
            var thirds = PhotoGuides.Lines(PhotoGuides.Mode.Thirds, 900f, 600f, 0);
            Assert.AreEqual(4, thirds.Count);
            Assert.AreEqual(300f, thirds[0][0].x, 1e-3f);
            Assert.AreEqual(200f, thirds[1][0].y, 1e-3f);
            Assert.AreEqual(400f, thirds[3][0].y, 1e-3f);
            Assert.AreEqual(6, PhotoGuides.Lines(PhotoGuides.Mode.Quarters, 900f, 600f, 0).Count);
            var golden = PhotoGuides.Lines(PhotoGuides.Mode.GoldenRatio, 1000f, 1000f, 0);
            Assert.AreEqual(381.966f, golden[0][0].x, 0.01f);
            Assert.AreEqual(618.034f, golden[2][0].x, 0.01f);
            Assert.IsEmpty(PhotoGuides.Lines(PhotoGuides.Mode.Off, 900f, 600f, 0));
        }

        [Test]
        public void TheGoldenSpiralIsOneCurveWindingTowardItsCorner()
        {
            for (int corner = 0; corner < 4; corner++)
            {
                var faint = new System.Collections.Generic.List<Vector2[]>();
                var lines = PhotoGuides.Lines(PhotoGuides.Mode.GoldenSpiral, 1618f, 1000f, corner, faint);
                Assert.AreEqual(1, lines.Count, "one curve");
                Assert.AreEqual(PhotoGuides.SpiralTurns, faint.Count, "a cut per quarter arc");
                var c = lines[0];
                for (int i = 1; i < c.Length; i++) Assert.Less(Vector2.Distance(c[i - 1], c[i]), 80f, "no jump between the arcs");
                foreach (var p in c) Assert.IsTrue(p.x >= -0.01f && p.x <= 1618.01f && p.y >= -0.01f && p.y <= 1000.01f, "inside the frame");
                // Corner 0 winds in toward the lower right (y down), bit 0 mirrors it left, bit 1 up.
                var end = c[c.Length - 1];
                Assert.AreEqual((corner & 1) == 0, end.x > 809f, $"corner {corner}: left / right");
                Assert.AreEqual((corner & 2) == 0, end.y > 500f, $"corner {corner}: top / bottom");
            }
            // A portrait frame is cut along its long side too: the first cut is a horizontal line.
            var portrait = new System.Collections.Generic.List<Vector2[]>();
            PhotoGuides.Lines(PhotoGuides.Mode.GoldenSpiral, 600f, 970f, 0, portrait);
            Assert.AreEqual(portrait[0][0].y, portrait[0][1].y, 1e-3f);
        }

        [Test]
        public void TheGoldenTrianglesMeetTheDiagonalAtARightAngleOnTheScreen()
        {
            foreach (int corner in new[] { 0, 1 })
            {
                var l = PhotoGuides.Lines(PhotoGuides.Mode.GoldenTriangles, 1920f, 1080f, corner);
                Assert.AreEqual(3, l.Count);
                var diagonal = l[0][1] - l[0][0];
                for (int i = 1; i < 3; i++) Assert.AreEqual(0f, Vector2.Dot(diagonal, l[i][1] - l[i][0]), 1e-1f, $"corner {corner}, line {i}");
            }
        }

        [Test]
        public void TheRollTurnsAllTheWayAndHoldsLevelAndTheQuarterTurns()
        {
            Assert.AreEqual(180f, PhotoTools.SnapRoll(240f));
            Assert.AreEqual(-180f, PhotoTools.SnapRoll(-179.5f));
            Assert.AreEqual(90f, PhotoTools.SnapRoll(90.4f));
            Assert.AreEqual(0f, PhotoTools.SnapRoll(-0.5f));
            Assert.AreEqual(135f, PhotoTools.SnapRoll(135f));
        }
    
        [Test]
        public void TheTemperatureIsShownInKelvinAndTypedBack()
        {
            Assert.AreEqual(6504f, PhotoSettings.Kelvin(0f), 5f, "URP's neutral white point is D65");
            Assert.AreEqual("6500 K", PhotoSettings.KelvinText(0f));
            Assert.That(PhotoSettings.Kelvin(-100f), Is.InRange(2400f, 2800f), "cooler light corrected for: warmer below");
            Assert.That(PhotoSettings.Kelvin(100f), Is.InRange(30000f, 40000f));
            for (float t = -100f; t < 100f; t += 5f) Assert.Less(PhotoSettings.Kelvin(t), PhotoSettings.Kelvin(t + 5f), "rises with the slider");
            foreach (float k in new[] { 3200f, 5000f, 6500f, 9000f, 20000f })
                Assert.AreEqual(k, PhotoSettings.Kelvin(PhotoSettings.TemperatureOfKelvin(k)), k * 0.001f);
            Assert.AreEqual(-100f, PhotoSettings.TemperatureOfKelvin(1000f), "clamped to the range");
        }

        [Test]
        public void TheShutterAngleIsAFilmCamerasAt24FramesASecond()
        {
            Assert.AreEqual(0f, PhotoSettings.ExposureOf(0f));
            Assert.AreEqual(1f / 48f, PhotoSettings.ExposureOf(180f), 1e-6f, "180 deg: half a 24 fps frame");
            Assert.AreEqual(1f / 24f, PhotoSettings.ExposureOf(360f), 1e-6f);
            Assert.AreEqual("Off", PhotoSettings.ShutterText(0f, "Off"));
            Assert.AreEqual("180° · 1/48 s", PhotoSettings.ShutterText(180f, "Off"));
            Assert.AreEqual("45° · 1/192 s", PhotoSettings.ShutterText(45f, "Off"));
            Assert.IsTrue(OptionControl.TryNumber("180° · 1/48 s", out float typed));
            Assert.AreEqual(180f, typed, "the box edits the angle");
        }

        [Test]
        public void PastAFullTurnTheShutterIsALongExposureUpToTwoSeconds()
        {
            float lastFrame = PhotoSettings.ExposureOf(PhotoSettings.LongFrom);
            Assert.AreEqual(1f / 24f, lastFrame, 1e-6f, "it carries on from 360 deg without a jump");
            Assert.AreEqual(PhotoSettings.MaxLongSeconds, PhotoSettings.ExposureOf(PhotoSettings.MaxShutter), 1e-4f);
            Assert.AreEqual(PhotoSettings.MaxLongSeconds, PhotoSettings.ExposureOf(10000f), 1e-4f, "never past the longest");
            float halfway = PhotoSettings.ExposureOf((PhotoSettings.LongFrom + PhotoSettings.MaxShutter) / 2f);
            Assert.AreEqual(Mathf.Sqrt(lastFrame * PhotoSettings.MaxLongSeconds), halfway, 1e-4f, "even in photographic stops");
            float previous = 0f;
            for (float s = 0f; s <= PhotoSettings.MaxShutter; s += 10f)
            {
                float e = PhotoSettings.ExposureOf(s);
                Assert.GreaterOrEqual(e, previous, "longer all the way along the slider");
                previous = e;
            }
            Assert.AreEqual("1/24 s", PhotoSettings.ShutterText(361f, "Off"));
            Assert.AreEqual("1/3 s", PhotoSettings.ShutterText(540f, "Off"));
            Assert.AreEqual("2 s", PhotoSettings.ShutterText(720f, "Off"));
            Assert.AreEqual("360° · 1/24 s", PhotoSettings.ShutterText(360f, "Off"));
            PhotoSettings.Shutter = 90f;
            PhotoSettings.Zoom = 0.5f;
            PhotoSettings.Reset();
            Assert.AreEqual(0f, PhotoSettings.Shutter, "Reset turns the motion blur off");
            Assert.AreEqual(0f, PhotoSettings.Zoom, "and the zoom blur");
        }

        [Test]
        public void TheLooksGradeOverTheSlidersAndResetClearsThem()
        {
            var none = PhotoSettings.LookOf(PhotoSettings.LookMode.None);
            Assert.IsFalse(none.Splits); Assert.IsFalse(none.Mixes);
            Assert.AreEqual(0f, none.saturation); Assert.AreEqual(0f, none.contrast); Assert.AreEqual(0f, none.fade);
            var noir = PhotoSettings.LookOf(PhotoSettings.LookMode.Noir);
            Assert.IsTrue(noir.Mixes, "black and white through the mixer, which the Saturation slider comes after");
            Assert.AreEqual(noir.mixRed, noir.mixGreen); Assert.AreEqual(noir.mixRed, noir.mixBlue);
            Assert.AreEqual(100f, noir.mixRed.x + noir.mixRed.y + noir.mixRed.z, 1e-3f, "the brightness kept");
            var sepia = PhotoSettings.LookOf(PhotoSettings.LookMode.Sepia);
            Assert.Greater(sepia.mixRed.magnitude, sepia.mixBlue.magnitude, "warmer than grey");
            Assert.IsTrue(PhotoSettings.LookOf(PhotoSettings.LookMode.TealOrange).Splits);
            foreach (PhotoSettings.LookMode m in System.Enum.GetValues(typeof(PhotoSettings.LookMode)))
            {
                var d = PhotoSettings.LookOf(m);
                Assert.That(d.saturation, Is.InRange(-100f, 100f), m.ToString());
                Assert.That(d.contrast, Is.InRange(-100f, 100f), m.ToString());
                Assert.That(d.fade, Is.InRange(0f, 1f), m.ToString());
            }
            PhotoSettings.Look = PhotoSettings.LookMode.Vintage;
            PhotoSettings.Shadows = 20f; PhotoSettings.Highlights = -20f; PhotoSettings.Fade = 0.3f; PhotoSettings.Hue = 90f;
            PhotoSettings.Reset();
            Assert.AreEqual(PhotoSettings.LookMode.None, PhotoSettings.Look);
            Assert.AreEqual(0f, PhotoSettings.Shadows + PhotoSettings.Highlights + PhotoSettings.Fade + PhotoSettings.Hue);
        }

        [Test]
        public void TheBlurStopsAt300PercentAndLooksAsItDidBefore()
        {
            Assert.AreEqual(3f, PhotoSettings.MaxBlur);
            float saved = PhotoSettings.Blur;
            try
            {
                PhotoSettings.Blur = 1f; Assert.AreEqual(0f, PhotoSettings.ExtraBlur);
                PhotoSettings.Blur = 3f; Assert.AreEqual(0.5f, PhotoSettings.ExtraBlur, 1e-6f, "300 % is halfway to the old 500 %");
            }
            finally { PhotoSettings.Blur = saved; }
        }

        [Test]
        public void ATypedValueIsReadInTheBoxsUnit()
        {
            Assert.IsTrue(OptionControl.TryNumber("46°", out float deg)); Assert.AreEqual(46f, deg);
            Assert.IsTrue(OptionControl.TryNumber("+0.5 EV", out float ev)); Assert.AreEqual(0.5f, ev);
            Assert.IsTrue(OptionControl.TryNumber("-12", out float neg)); Assert.AreEqual(-12f, neg);
            Assert.IsTrue(OptionControl.TryNumber("x1,5", out float comma)); Assert.AreEqual(1.5f, comma);
            Assert.IsTrue(OptionControl.TryNumber("1.2 km", out float km)); Assert.AreEqual(1200f, km, 1e-3f);
            Assert.IsTrue(OptionControl.TryNumber("−30 %", out float minus)); Assert.AreEqual(-30f, minus);
            Assert.IsFalse(OptionControl.TryNumber("abc", out _));
            Assert.AreEqual("46", OptionControl.EditText("46°"));
            Assert.AreEqual("0.5", OptionControl.EditText("+0.5 EV"));
            Assert.AreEqual("1200", OptionControl.EditText("1.2 km"));
            Assert.AreEqual("6500", OptionControl.EditText("6500 K"));
            Assert.AreEqual(0.5f, PhotoSettings.FocusOfMetres(PhotoSettings.FocusMetresOf(0.5f)), 1e-4f);
        }
    }
}
