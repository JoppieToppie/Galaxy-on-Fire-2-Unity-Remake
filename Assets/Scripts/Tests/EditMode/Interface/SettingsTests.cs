// SettingsTests.cs
// CLAUDE.md "UI and platforms" / "Options": the player options (Settings, PlayerPrefs). Defaults from the original's
// Globals::init (Reference/research/mainmenu_notes.md 2.6: quality High, brightness the middle one, sensitivity 1.0, the
// tilt calibration 0.6 / 0.6 and touch steering) and from CLAUDE.md's statements of the remake's options (frame rate
// V-Sync, field of view the original's 1.22 rad, hangar shadows "All ships", hangar depth of field off, vibration 100 %,
// mouse dead zone 8 %, tutorials off, story step on, auto-play dialogue on, the rules' "on / off by default"...); the stick
// dead zone is the Input System's own default (0.125). Clamps: the setters' ranges, checked as invariants. The reset
// groups: "Default settings" per tab (Settings' key lists) and for every tab (ResetToDefaults, the language kept). A row's
// own default without changing it (Settings.TryDefault, OptionControl's reset).
// Every PlayerPrefs value is put back afterwards (DataUiTestKit.PrefsSnapshot).

using System.Linq;
using GoF2Remake.Data;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class SettingsTests
    {
        DataUiTestKit.PrefsSnapshot prefs;

        [SetUp] public void Save() => prefs = new DataUiTestKit.PrefsSnapshot();
        [TearDown] public void Restore() => prefs?.Dispose();

        static void AllDefaults()
        {
            Settings.Reset(DataUiTestKit.SettingFloatKeys.Concat(Settings.LanguageKeys));
        }

        [Test]
        public void OriginalOptionDefaults()
        {
            AllDefaults();
            Assert.IsTrue(Settings.FireSmoke && Settings.Fog, "options+0x28 = 1.0: the original's Quality High");
            Assert.AreEqual(2, Settings.DetailDistance);
            Assert.AreEqual(1, Settings.Brightness, "options+0x24 = 0.5: the middle of Dark / Medium / Bright");
            Assert.AreEqual(0f, Settings.BrightnessExposure);
            Assert.AreEqual(1f, Settings.Sensitivity, "options+0x14 = 1.0");
            Assert.AreEqual(1f, Settings.TiltSensitivity, "options+0x18 = 1.0");
            Assert.AreEqual(0.6f, Settings.TiltCalX, 1e-6f, "options+0x1c = 0.6");
            Assert.AreEqual(0.6f, Settings.TiltCalZ, 1e-6f, "options+0x20 = 0.6");
            Assert.IsFalse(Settings.TiltSteering, "options[0x11] = 1: touch steering");
            Assert.AreEqual(1.22f, Settings.FieldOfView * Mathf.Deg2Rad, 1e-4f, "MGame::reset's 1.22 rad");
            Assert.AreEqual(Settings.OriginalFov, Settings.FieldOfView);
        }

        [Test]
        public void RemakeOptionDefaults()
        {
            AllDefaults();
            Assert.AreEqual(FrameRate.VSync, Settings.FrameRate, "default V-Sync");
            Assert.AreEqual(Settings.HangarShadowsPlayer, Settings.HangarShadows, "default Player ship only");
            Assert.IsFalse(Settings.HangarDepthOfField, "off by default");
            Assert.AreEqual(1f, Settings.HapticsIntensity, "vibration 100 %");
            Assert.AreEqual(0.08f, Settings.MouseDeadzone, 1e-6f, "mouse dead zone 8 %");
            Assert.AreEqual(1f, Settings.MouseSensitivity, "the mouse's own movement, as before the option");
            Assert.AreEqual(0.125f, Settings.StickDeadzone, 1e-6f, "the Input System's defaultDeadzoneMin");
            Assert.IsFalse(Settings.TutorialHints, "tutorials off by default");
            Assert.IsTrue(Settings.ShowStoryStep, "on by default since 2026-10-11");
            Assert.IsTrue(Settings.AutoAdvanceDialogue, "AUTO on by default");
            Assert.IsTrue(Settings.NpcPlayerEngines, "other ships' engines like yours: on");
            Assert.IsTrue(Settings.DyingCargoMarkers, "salvage markers: on");
            Assert.IsFalse(Settings.OriginalTargetLock, "target lock: Smart");
            Assert.IsTrue(Settings.KeepEnemyLock, "the Smart lock's hold on an enemy: on, as before it was its own option");
            Assert.IsFalse(Settings.KeyAutofire, "double-press autofire: off");
            Assert.IsFalse(Settings.LevelPitch, "level out levels the roll only, like the original");
            Assert.IsFalse(Settings.FloatingTouchStick, "the touch stick stays in its place");
            Assert.IsFalse(Settings.GyroSteering, "controller gyro: off");
            Assert.IsFalse(Settings.VrGrabControls, "VR grab: off");
            Assert.AreEqual(-1f, Settings.TouchLeftHeight, "-1 = the original's place");
            Assert.AreEqual(-1f, Settings.TouchRightHeight);
            Assert.AreEqual("auto", Settings.VoiceLanguage, "voices as the text");
        }

        [Test]
        public void SessionRuleDefaults()
        {
            AllDefaults();
            Assert.IsFalse(Settings.CloakLosesPursuers, "off by default");
            Assert.IsTrue(Settings.PirateEvents, "on by default");
            Assert.IsFalse(Settings.CapitalShips, "off by default");
            Assert.IsFalse(Settings.InformerOriginalRule, "Remake by default");
            Assert.IsTrue(Settings.KaamoStacking, "on by default");
            Assert.AreEqual(5, Settings.KaamoMaxLevel, "upgrade limit 5 by default");
            Assert.IsTrue(Settings.KaamoKeepsEquipment, "on by default");
            Assert.IsTrue(Settings.CloakBay, "on by default");
            Assert.IsTrue(Settings.EmpBreaksCloak, "on by default");
            Assert.IsFalse(Settings.SmarterNpcTargeting, "the original's targeting by default (#121)");
            Assert.AreEqual(SessionRules.CollisionMixed, Settings.ShipCollision, "Mixed by default");
            int expected = SessionRules.PirateEvents | SessionRules.KaamoStacking | SessionRules.KaamoKeepsEquipment
                         | SessionRules.CloakBay | SessionRules.EmpBreaksCloak;
            Assert.AreEqual(expected, Settings.LocalRules());
        }

        [Test]
        public void TheCameraViewsInvertOnTheirOwn()
        {
            // #121: free look, Action Freeze, Inspect ship and the Mod Builder's views have their own inversion, apart from
            // the flight's steering.
            AllDefaults();
            var d = new Vector2(3f, -2f);
            Assert.AreEqual(d, Settings.OrbitTurn(d), "not inverted by default");
            Settings.InvertPitch = true;
            Settings.InvertYaw = true;
            Assert.AreEqual(d, Settings.OrbitTurn(d), "the flight's inversion doesn't turn the camera views");
            Settings.InvertOrbitY = true;
            Assert.AreEqual(new Vector2(3f, 2f), Settings.OrbitTurn(d));
            Settings.InvertOrbitX = true;
            Assert.AreEqual(new Vector2(-3f, 2f), Settings.OrbitTurn(d));
        }

        [Test]
        public void ShipCollisionModesTakeTheirHulls()
        {
            // Options > Gameplay "Ship collision" (Flight.HullCollision.Mode / Takes, HullBody.flyableShip / Collides).
            AllDefaults();
            Assert.AreEqual(SessionRules.CollisionMixed, Flight.HullCollision.Mode, "Mixed by default");
            Assert.IsFalse(Flight.HullCollision.Takes(true), "Mixed: the flyable ships' cube and sphere");
            Assert.IsTrue(Flight.HullCollision.Takes(false), "Mixed: the rest on their real shape");
            Settings.ShipCollision = SessionRules.CollisionExact;
            Assert.IsTrue(Flight.HullCollision.Takes(true), "Exact: every real shape");
            Assert.IsTrue(Flight.HullCollision.Takes(false));
            Settings.ShipCollision = SessionRules.CollisionOriginal;
            Assert.IsFalse(Flight.HullCollision.Takes(true), "Original: the original's volumes everywhere");
            Assert.IsFalse(Flight.HullCollision.Takes(false));
            Settings.ShipCollision = 7;
            Assert.AreEqual(SessionRules.CollisionOriginal, Settings.ShipCollision, "clamped");
            foreach (int ship in new[] { 0, 10, 28, 44, 64 }) Assert.IsTrue(Flight.HullCollision.FlyableShip(ship), ship + " flyable");
            foreach (int ship in new[] { -1, 13, 14, 15 }) Assert.IsFalse(Flight.HullCollision.FlyableShip(ship), ship + ": freighter, battleship, none");
        }

        [Test]
        public void LocalRulesFollowTheOptions()
        {
            AllDefaults();
            Settings.CapitalShips = true;
            Settings.PirateEvents = false;
            Settings.CloakLosesPursuers = true;
            Settings.InformerOriginalRule = true;
            Settings.ShipCollision = SessionRules.CollisionOriginal;
            int m = Settings.LocalRules();
            Assert.AreEqual(SessionRules.CollisionOriginal, SessionRules.CollisionOf(m), "Original ship collision");
            Settings.ShipCollision = SessionRules.CollisionExact;
            Assert.AreEqual(SessionRules.CollisionExact, SessionRules.CollisionOf(Settings.LocalRules()));
            Settings.ShipCollision = SessionRules.CollisionMixed;
            Assert.AreEqual(0, Settings.LocalRules() & (SessionRules.PreciseShipCollision | SessionRules.OriginalCollision), "Mixed: neither bit");
            Assert.AreNotEqual(0, m & SessionRules.CapitalShips);
            Assert.AreEqual(0, m & SessionRules.PirateEvents);
            Assert.AreNotEqual(0, m & SessionRules.CloakLosesPursuers);
            Assert.AreNotEqual(0, m & SessionRules.InformerOriginalRule);
            // the bits are distinct powers of two
            int[] bits = { SessionRules.CloakLosesPursuers, SessionRules.PirateEvents, SessionRules.CapitalShips, SessionRules.InformerOriginalRule,
                           SessionRules.KaamoStacking, SessionRules.KaamoKeepsEquipment, SessionRules.CloakBay, SessionRules.EmpBreaksCloak,
                           SessionRules.SmarterNpcTargeting, SessionRules.PreciseShipCollision, SessionRules.OriginalCollision };
            Assert.AreEqual(bits.Length, bits.Distinct().Count());
            foreach (int b in bits) Assert.AreEqual(0, b & (b - 1), "a single bit");
        }

        [Test]
        public void KaamoUpgradeLimitIsClampedAndResets()
        {
            AllDefaults();
            Settings.KaamoMaxLevel = 99;
            Assert.AreEqual(SessionRules.MaxKaamoLevel, Settings.KaamoMaxLevel);
            Settings.KaamoMaxLevel = 0;
            Assert.AreEqual(SessionRules.MinKaamoLevel, Settings.KaamoMaxLevel, "1 would be stacking off: that's the toggle");
            Settings.KaamoMaxLevel = 3;
            Assert.AreEqual(3, Settings.LocalKaamoLevel());
            CollectionAssert.Contains(Settings.GameplayKeys, "kaamoMaxLevel");
            Settings.Reset(Settings.GameplayKeys);
            Assert.AreEqual(SessionRules.DefaultKaamoLevel, Settings.KaamoMaxLevel, "Default settings on the Gameplay tab");
        }

        [Test]
        public void VolumesAreFractions()
        {
            AllDefaults();
            foreach (float v in new[] { Settings.MasterVolume, Settings.MusicVolume, Settings.SfxVolume, Settings.VoiceVolume, Settings.CameraShake })
                Assert.That(v, Is.InRange(0f, 1f));
            Settings.MusicVolume = 2f;
            Assert.AreEqual(1f, Settings.MusicVolume);
            Settings.SfxVolume = -1f;
            Assert.AreEqual(0f, Settings.SfxVolume);
            Settings.VoiceVolume = 0.25f;
            Assert.AreEqual(0.25f, Settings.VoiceVolume);
            Settings.MasterVolume = 1.5f;
            Assert.AreEqual(1f, Settings.MasterVolume);
        }

        [Test]
        public void SettersClampToTheirRanges()
        {
            Settings.FieldOfView = 200f; Assert.AreEqual(95f, Settings.FieldOfView);
            Settings.FieldOfView = 10f; Assert.AreEqual(55f, Settings.FieldOfView);
            Settings.Sensitivity = 5f; Assert.AreEqual(2.2f, Settings.Sensitivity, 1e-6f);
            Settings.Sensitivity = -1f; Assert.AreEqual(0f, Settings.Sensitivity);
            Settings.MouseDeadzone = 1f; Assert.AreEqual(0.3f, Settings.MouseDeadzone, 1e-6f);
            Settings.MouseSensitivity = 0f; Assert.AreEqual(0.1f, Settings.MouseSensitivity, 1e-6f);
            Settings.MouseSensitivity = 9f; Assert.AreEqual(3f, Settings.MouseSensitivity, 1e-6f);
            Settings.StickDeadzone = 0f; Assert.AreEqual(0.05f, Settings.StickDeadzone, 1e-6f);
            Settings.StickDeadzone = 1f; Assert.AreEqual(0.4f, Settings.StickDeadzone, 1e-6f);
            Settings.GyroSensitivity = 10f; Assert.AreEqual(3f, Settings.GyroSensitivity);
            Settings.GyroSensitivity = 0f; Assert.AreEqual(0.25f, Settings.GyroSensitivity);
            Settings.TiltSensitivity = 3f; Assert.AreEqual(1f, Settings.TiltSensitivity);
            Settings.HapticsIntensity = -0.5f; Assert.AreEqual(0f, Settings.HapticsIntensity);
            Settings.CameraShake = 4f; Assert.AreEqual(1f, Settings.CameraShake);
            Settings.DetailDistance = 7; Assert.AreEqual(3, Settings.DetailDistance);
            Settings.DetailDistance = -3; Assert.AreEqual(0, Settings.DetailDistance);
            Settings.TextureFiltering = 9; Assert.AreEqual(3, Settings.TextureFiltering);
            Settings.Brightness = 9; Assert.AreEqual(2, Settings.Brightness);
            Settings.HangarShadows = 9; Assert.AreEqual(2, Settings.HangarShadows);
            Settings.HangarShadows = -1; Assert.AreEqual(0, Settings.HangarShadows);
            Settings.Upscaler = 99; Assert.AreEqual(Settings.UpscalerMetalFxTemporal, Settings.Upscaler);
            Settings.Upscaler = -2; Assert.AreEqual(Settings.UpscalerOff, Settings.Upscaler);
            Settings.UpscalerQuality = 9; Assert.AreEqual(4, Settings.UpscalerQuality);
        }

        [Test]
        public void MouseSensitivityScalesTheMouseMovementOnly()
        {
            // Discord "Mouse Sensitivity seems to do nothing": Sensitivity (the ramp, not for the mouse) left a high-DPI mouse
            // as fast as ever; the mouse's own option scales its movement.
            Settings.Sensitivity = 0.2f;
            Assert.AreEqual(new Vector2(100f, -40f), Settings.MouseMove(new Vector2(100f, -40f)), "Sensitivity leaves the mouse alone");
            Settings.MouseSensitivity = 0.25f;
            Assert.AreEqual(new Vector2(25f, -10f), Settings.MouseMove(new Vector2(100f, -40f)));
            Assert.Contains("mouseSensitivity", Settings.ControlsKeys, "reset with the Controls page");
        }

        [Test]
        public void RenderScaleZeroMeansThePlatformDefault()
        {
            Settings.RenderScale = 0f; Assert.AreEqual(0f, Settings.RenderScale);
            Settings.RenderScale = -1f; Assert.AreEqual(0f, Settings.RenderScale);
            Settings.RenderScale = 0.1f; Assert.AreEqual(0.5f, Settings.RenderScale);
            Settings.RenderScale = 3f; Assert.AreEqual(2f, Settings.RenderScale);
            Settings.RenderScale = 0.75f; Assert.AreEqual(0.75f, Settings.RenderScale);
        }

        [Test]
        public void AndroidAtOneHundredPercentGetsMsaa()
        {
            // MSAA off -> 2x on Android at 100 %, the scale left alone.
            Assert.AreEqual(2, Bootstrap.AppliedMsaa(1, 1f, true, false));
            Assert.AreEqual(1f, Bootstrap.AppliedRenderScale(1f, true, false));
            // The player's own MSAA, other scales, other platforms: as picked.
            Assert.AreEqual(4, Bootstrap.AppliedMsaa(4, 1f, true, false));
            Assert.AreEqual(1, Bootstrap.AppliedMsaa(1, 0.9f, true, false));
            Assert.AreEqual(1, Bootstrap.AppliedMsaa(1, 1.25f, true, false));
            Assert.AreEqual(1, Bootstrap.AppliedMsaa(1, 1f, false, false));
            Assert.AreEqual(1f, Bootstrap.AppliedRenderScale(1f, false, true));
        }

        [Test]
        public void OldAdrenoPhonesStartOnOpenGlEs()
        {
            // CLAUDE.md "Android Vulkan device filter": Adreno 5xx / 6xx on Android 10 or older are denied Vulkan, and the
            // build keeps GLES3 to fall back to (the filter fields are ECMAScript regexes, matched as the Editor does).
            var apis = UnityEditor.PlayerSettings.GetGraphicsAPIs(UnityEditor.BuildTarget.Android);
            CollectionAssert.Contains(apis, UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3);
            var lists = UnityEditor.PlayerSettings.Android.androidVulkanDeviceFilterListAsset;
            Assert.IsNotNull(lists);
            var deny = lists.vulkanDeviceDenyFilters.Single();
            bool Matches(string pattern, string value) => System.Text.RegularExpressions.Regex.IsMatch(value, pattern,
                System.Text.RegularExpressions.RegexOptions.ECMAScript);
            Assert.IsTrue(Matches(deny.deviceName, "Adreno (TM) 640"));
            Assert.IsTrue(Matches(deny.deviceName, "Adreno (TM) 540"));
            Assert.IsFalse(Matches(deny.deviceName, "Adreno (TM) 740"));
            Assert.IsFalse(Matches(deny.deviceName, "Mali-G76"));
            foreach (var old in new[] { "7", "8.1", "9", "10" }) Assert.IsTrue(Matches(deny.androidOsVersionString, old), old);
            foreach (var newer in new[] { "11", "12", "15" }) Assert.IsFalse(Matches(deny.androidOsVersionString, newer), newer);
        }

        [Test]
        public void AndroidUnderATemporalUpscalerDrawsOneHundredPercentFarEnoughBelowItForUrp()
        {
            Assert.AreEqual(1, Bootstrap.AppliedMsaa(1, 1f, true, true));   // temporal upscalers need MSAA off
            Assert.AreEqual(Bootstrap.AndroidFullScale, Bootstrap.AppliedRenderScale(1f, true, true));
            // URP's kRenderScaleThreshold: closer than 0.05 to 1 is rendered as 1.
            Assert.GreaterOrEqual(Mathf.Abs(1f - Bootstrap.AppliedRenderScale(1f, true, true)), 0.05f);
            Assert.AreEqual(0.9f, Bootstrap.AppliedRenderScale(0.9f, true, true));
        }

        [Test]
        public void TouchHeightsAreFractionsOrTheDefault()
        {
            Settings.TouchLeftHeight = -5f; Assert.AreEqual(-1f, Settings.TouchLeftHeight);
            Settings.TouchLeftHeight = 2f; Assert.AreEqual(1f, Settings.TouchLeftHeight);
            Settings.TouchRightHeight = 0.3f; Assert.AreEqual(0.3f, Settings.TouchRightHeight, 1e-6f);
            Settings.TouchRightHeight = -0.01f; Assert.AreEqual(-1f, Settings.TouchRightHeight);
        }

        [Test]
        public void BrightnessMovesTheExposure()
        {
            Settings.Brightness = 0; Assert.AreEqual(-0.35f, Settings.BrightnessExposure, 1e-6f);
            Settings.Brightness = 2; Assert.AreEqual(0.35f, Settings.BrightnessExposure, 1e-6f);
        }

        [Test]
        public void BloomStylesPickTheBloomPasses()
        {
            Settings.BloomStyle = Settings.BloomOff; Assert.IsFalse(Settings.Bloom); Assert.IsFalse(Settings.ClassicBloom);
            Settings.BloomStyle = Settings.BloomRemake; Assert.IsTrue(Settings.Bloom); Assert.IsFalse(Settings.ClassicBloom);
            Settings.BloomStyle = Settings.BloomOriginal; Assert.IsFalse(Settings.Bloom); Assert.IsTrue(Settings.ClassicBloom);
            Settings.BloomStyle = Settings.BloomBoth; Assert.IsTrue(Settings.Bloom); Assert.IsTrue(Settings.ClassicBloom);
        }

        [Test]
        public void OldSavesBloomToggleBecomesTheStyle()
        {
            // An old player's "bloom" toggle and no "bloomStyle": the style follows it.
            Settings.Reset(new[] { "bloom", "bloomStyle" });
            Assert.AreEqual(Settings.BloomRemake, Settings.BloomStyle, "no toggle saved: on");
            PlayerPrefs.SetFloat("gof2.bloom", 0f);
            DataUiTestKit.ClearSettingsCache();
            Assert.AreEqual(Settings.BloomOff, Settings.BloomStyle, "the toggle saved off");
        }

        [Test]
        public void GermanVoicesFollowTheVoiceLanguage()
        {
            string language = Settings.Language;
            try
            {
                Settings.VoiceLanguage = "de"; Assert.IsTrue(Settings.GermanVoices);
                Settings.VoiceLanguage = "en"; Assert.IsFalse(Settings.GermanVoices);
                Settings.VoiceLanguage = "auto";
                Settings.Language = "de"; Assert.IsTrue(Settings.GermanVoices, "auto: German voices with the German text");
                Settings.Language = "fr"; Assert.IsFalse(Settings.GermanVoices, "auto: English voices otherwise");
            }
            finally { Settings.Language = language; }
        }

        [Test]
        public void ResetOfOneGroupLeavesTheOthers()
        {
            AllDefaults();
            float music = Settings.MusicVolume;
            Settings.MusicVolume = music > 0.5f ? 0.1f : 0.9f;
            Settings.DetailDistance = 0;
            Settings.InvertPitch = true;
            Settings.TutorialHints = true;
            Settings.Reset(Settings.SoundKeys);
            Assert.AreEqual(music, Settings.MusicVolume, 1e-6f, "Sound reset");
            Assert.AreEqual(0, Settings.DetailDistance, "Graphics kept");
            Assert.IsTrue(Settings.InvertPitch, "Controls kept");
            Assert.IsTrue(Settings.TutorialHints, "Gameplay kept");
            Settings.Reset(Settings.ControlsKeys);
            Assert.IsFalse(Settings.InvertPitch);
            Assert.AreEqual(0, Settings.DetailDistance);
        }

        [Test]
        public void ResetToDefaultsResetsEveryTabButTheLanguage()
        {
            string language = Settings.Language;
            try
            {
                AllDefaults();
                bool fps = Settings.ShowFps, flights = Settings.HangarFlights;
                float sfx = Settings.SfxVolume;
                Settings.Language = "nl";
                Settings.DetailDistance = 0;
                Settings.ShowFps = !fps;
                Settings.InvertYaw = true;
                Settings.HangarFlights = !flights;
                Settings.VoiceLanguage = "de";
                Settings.SfxVolume = sfx > 0.5f ? 0.2f : 0.8f;
                Settings.ResetToDefaults();
                Assert.AreEqual(2, Settings.DetailDistance);
                Assert.AreEqual(fps, Settings.ShowFps);
                Assert.IsFalse(Settings.InvertYaw);
                Assert.AreEqual(flights, Settings.HangarFlights);
                Assert.AreEqual("auto", Settings.VoiceLanguage);
                Assert.AreEqual(sfx, Settings.SfxVolume, 1e-6f);
                Assert.AreEqual("nl", Settings.Language, "the text language is kept");
            }
            finally { Settings.Language = language; }
        }

        [Test]
        public void TheResetGroupsDontOverlap()
        {
            var groups = new[] { Settings.SoundKeys, Settings.GraphicsKeys, Settings.ControlsKeys, Settings.GameplayKeys, Settings.LanguageKeys };
            var all = groups.SelectMany(g => g).ToList();
            Assert.AreEqual(all.Count, all.Distinct().Count(), "a key in two tabs: " + string.Join(", ", all.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key)));
            CollectionAssert.DoesNotContain(all, "language", "the text language isn't reset");
            CollectionAssert.DoesNotContain(all, "tiltCalX", "the calibration is the device's, not an option");
        }

        [Test]
        public void ChangedFiresOnlyOnARealChange()
        {
            int fired = 0;
            void Count() => fired++;
            Settings.Changed += Count;
            try
            {
                Settings.CameraShake = 0.4f;
                Settings.CameraShake = 0.5f;
                int after = fired;
                Assert.Greater(after, 0);
                Settings.CameraShake = 0.5f;
                Assert.AreEqual(after, fired, "the same value: no event");
                Settings.CameraShake = 0.6f;
                Assert.AreEqual(after + 1, fired);
            }
            finally { Settings.Changed -= Count; }
        }
    
        [Test]
        public void ARowsDefaultIsReadWithoutChangingTheOption()
        {
            Settings.MasterVolume = 0.3f;
            Settings.FieldOfView = 80f;
            Assert.IsTrue(Settings.TryDefault(() => Settings.MasterVolume, out float master));
            Assert.AreEqual(1f, master);
            Assert.IsTrue(Settings.TryDefault(() => Settings.FieldOfView, out float fov));
            Assert.AreEqual(1.22f * Mathf.Rad2Deg, fov, 0.5f, "the original's 1.22 rad");
            Assert.AreEqual(0.3f, Settings.MasterVolume, 1e-5f, "the option itself unchanged");
            Assert.AreEqual(80f, Settings.FieldOfView, 1e-5f);
            Assert.IsFalse(Settings.TryDefault(() => 42f, out _), "a getter that reads no option has no default");
        }

        [Test]
        public void TheFieldOfViewAtItsDefaultShowsNoResetArrow()
        {
            var fov = GoF2Remake.UI.OptionsCatalog.All().Find(d => d.id == "fov");
            var row = new GoF2Remake.UI.OptionControl(fov);
            row.ResetToDefault();
            Assert.IsTrue(row.IsDefault(), "70° stored for the original's 69.9° is the default");
            Settings.FieldOfView = 80f;
            Assert.IsFalse(row.IsDefault());
            row.ResetToDefault();
            Assert.IsTrue(row.IsDefault());
            Assert.AreEqual(70f, Settings.FieldOfView, 1e-4f);
        }
    }
}
