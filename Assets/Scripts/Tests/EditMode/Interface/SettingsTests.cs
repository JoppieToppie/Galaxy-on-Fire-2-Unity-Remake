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
            Assert.IsTrue(Settings.OriginalTargetLock, "target lock: the original's (the Original experience)");
            Assert.IsTrue(Settings.KeepEnemyLock, "the Smart lock's hold on an enemy: on, as before it was its own option");
            Assert.IsFalse(Settings.KeyAutofire, "double-press autofire: off");
            Assert.IsFalse(Settings.LevelPitch, "level out levels the roll only, like the original");
            Assert.IsFalse(Settings.FloatingTouchStick, "the touch stick stays in its place");
            Assert.IsFalse(Settings.GyroSteering, "controller gyro: off");
            Assert.IsFalse(Settings.VrGrabControls, "VR grab: off");
            Assert.AreEqual(-1f, Settings.TouchLeftHeight, "-1 = the original's place");
            Assert.AreEqual(-1f, Settings.TouchRightHeight);
            Assert.AreEqual("auto", Settings.VoiceLanguage, "voices as the text");
            Assert.IsTrue(Settings.HangarRobots, "the hangars' service bots: on");
            Assert.IsTrue(Settings.BarRobots, "the bars' robots: on");
        }

        /// <summary>The Hangar robots / Bar robots toggles (2026-11-05): stored, reset with the Graphics tab, rows there.</summary>
        [Test]
        public void RobotOptions()
        {
            AllDefaults();
            Settings.HangarRobots = false;
            Settings.BarRobots = false;
            Assert.IsFalse(Settings.HangarRobots);
            Assert.IsFalse(Settings.BarRobots);
            CollectionAssert.IsSubsetOf(new[] { "hangarRobots", "barRobots" }, Settings.GraphicsKeys, "Default settings resets them with the Graphics tab");
            Settings.Reset(Settings.GraphicsKeys);
            Assert.IsTrue(Settings.HangarRobots && Settings.BarRobots, "back on after Default settings");
            var rows = GoF2Remake.UI.OptionsCatalog.All().Where(o => o.id == "hangarRobots" || o.id == "barRobots").ToList();
            Assert.AreEqual(2, rows.Count);
            Assert.IsTrue(rows.All(o => o.page == GoF2Remake.UI.OptionPage.Graphics && o.kind == GoF2Remake.UI.OptionKind.Toggle));
        }

        /// <summary>FSR 4 Lite (2026-11-16): off by default, stored, reset with the Graphics tab; its row is a Graphics toggle
        /// in builds with the upscaler framework, hidden where FSR 4 doesn't run through ffx-vk-linux (the Editor's tests).</summary>
        [Test]
        public void Fsr4LiteOption()
        {
            AllDefaults();
            Assert.IsFalse(Settings.Fsr4Lite, "the GPU's own FSR 4 model by default");
            Settings.Fsr4Lite = true;
            Assert.IsTrue(Settings.Fsr4Lite);
            CollectionAssert.Contains(Settings.GraphicsKeys, "fsr4Lite", "Default settings resets it with the Graphics tab");
            Settings.Reset(Settings.GraphicsKeys);
            Assert.IsFalse(Settings.Fsr4Lite, "off again after Default settings");
            var row = GoF2Remake.UI.OptionsCatalog.All().FirstOrDefault(o => o.id == "fsr4Lite");
            if (!UpscalerFramework.Compiled) { Assert.IsNull(row, "no row without the upscaler framework"); return; }
            Assert.IsNotNull(row);
            Assert.IsTrue(row.page == GoF2Remake.UI.OptionPage.Graphics && row.kind == GoF2Remake.UI.OptionKind.Toggle);
            Settings.Upscaler = Settings.UpscalerOff;
            Assert.IsFalse(row.visible(), "hidden while FSR 4 isn't the upscaler");
        }

        /// <summary>Frame generation and Reflex (2026-11-20): both off by default, stored within their ranges, reset with the
        /// Graphics tab; their rows are Graphics choices, hidden where the plugin isn't (the Editor). The Frame generation
        /// row offers Off and 2x until Streamline names more, keeps a stored multiplier on show, and has Dynamic only while it
        /// is the stored choice or the driver runs it.</summary>
        [Test]
        public void FrameGenerationAndReflexOptions()
        {
            AllDefaults();
            Assert.AreEqual(0, Settings.FrameGeneration, "off by default");
            Assert.AreEqual(0, Settings.Reflex, "off by default");
            Settings.FrameGeneration = 99; Assert.AreEqual(Settings.FrameGenerationDynamic, Settings.FrameGeneration);
            Settings.FrameGeneration = -3; Assert.AreEqual(0, Settings.FrameGeneration);
            Settings.Reflex = 9; Assert.AreEqual(2, Settings.Reflex);
            CollectionAssert.IsSubsetOf(new[] { "frameGeneration", "reflex" }, Settings.GraphicsKeys, "Default settings resets them with the Graphics tab");
            Settings.FrameGeneration = 3;
            Settings.Reset(Settings.GraphicsKeys);
            Assert.AreEqual(0, Settings.FrameGeneration);
            Assert.AreEqual(0, Settings.Reflex);

            var rows = GoF2Remake.UI.OptionsCatalog.All();
            var frames = rows.FirstOrDefault(o => o.id == "frameGeneration");
            var reflex = rows.FirstOrDefault(o => o.id == "reflex");
            Assert.IsNotNull(frames);
            Assert.IsNotNull(reflex);
            Assert.IsTrue(frames.page == GoF2Remake.UI.OptionPage.Graphics && frames.kind == GoF2Remake.UI.OptionKind.Choice);
            Assert.IsFalse(frames.visible(), "no plugin in the Editor: the row is hidden");
            Assert.IsFalse(reflex.visible());
            Assert.IsFalse(FrameGeneration.Running);
            Assert.AreEqual(1f, FrameGeneration.PresentedPerFrame, "the frame rate counter shows the rendered frames alone");

            Assert.AreEqual(2, FrameGeneration.Choices().Length, "Off and 2x until Streamline says what the GPU runs");
            FrameGeneration.SetChoice(1);
            Assert.AreEqual(1, Settings.FrameGeneration);
            Assert.AreEqual(1, FrameGeneration.ChoiceIndex());
            Settings.FrameGeneration = 3;
            Assert.AreEqual(4, FrameGeneration.Choices().Length, "a stored 4x stays on show");
            Assert.AreEqual("4×", FrameGeneration.Choices()[3]);
            Assert.AreEqual(3, FrameGeneration.ChoiceIndex());
            Settings.FrameGeneration = Settings.FrameGenerationDynamic;
            Assert.AreEqual(FrameGeneration.Choices().Length - 1, FrameGeneration.ChoiceIndex(), "Dynamic is the last choice while stored");
            FrameGeneration.SetChoice(0);
            Assert.AreEqual(0, Settings.FrameGeneration);
            Assert.AreEqual(2, FrameGeneration.Choices().Length);
            Assert.IsFalse(string.IsNullOrEmpty(FrameGeneration.Help()));
            Assert.IsFalse(string.IsNullOrEmpty(FrameGeneration.ReflexHelp()));
        }

        /// <summary>The Graphics tab's categories (2026-11-20): every Graphics row has a place of its own choosing
        /// (OptionsCatalog.HasGraphicsPlace), the preset row stays above them, and a category's rows are together in the
        /// list, the categories in their order (the keys walk the rows in list order). The DLSS preset, neural rendering
        /// and HDR options: off / default by default, stored within their ranges, reset with the tab.</summary>
        [Test]
        public void GraphicsCategoriesAndNewOptions()
        {
            AllDefaults();
            var rows = GoF2Remake.UI.OptionsCatalog.All().Where(o => o.page == GoF2Remake.UI.OptionPage.Graphics).ToList();
            Assert.AreEqual("graphicsPreset", rows[0].id, "the preset above the categories");
            Assert.AreEqual(GoF2Remake.UI.OptionSection.None, rows[0].section);
            foreach (var row in rows) Assert.IsTrue(GoF2Remake.UI.OptionsCatalog.HasGraphicsPlace(row.id), row.id + " has no category of its own");
            var seen = new System.Collections.Generic.List<GoF2Remake.UI.OptionSection>();
            foreach (var row in rows.Skip(1))
            {
                Assert.AreNotEqual(GoF2Remake.UI.OptionSection.None, row.section, row.id);
                if (seen.Count == 0 || seen[seen.Count - 1] != row.section)
                {
                    Assert.IsFalse(seen.Contains(row.section), row.section + " is split in the list");
                    seen.Add(row.section);
                }
            }
            CollectionAssert.AreEqual(seen.OrderBy(s => (int)s).ToList(), seen, "the categories in their order");
            Assert.IsFalse(string.IsNullOrEmpty(GoF2Remake.UI.OptionsCatalog.SectionTitle(GoF2Remake.UI.OptionSection.GraphicsUpscaling)));
            Assert.AreEqual(GoF2Remake.UI.OptionSection.GraphicsUpscaling, rows.First(o => o.id == "frameGeneration").section);
            Assert.AreEqual(GoF2Remake.UI.OptionSection.GraphicsDisplay, rows.First(o => o.id == "frameRate").section);

            Assert.AreEqual(0, Settings.DlssPreset);
            Settings.DlssPreset = -4; Assert.AreEqual(0, Settings.DlssPreset);
            CollectionAssert.Contains(UpscalerFramework.DlssPresets(Settings.UpscalerQuality), 0, "the default is always offered");
            Assert.IsFalse(Settings.NeuralRendering);
            Assert.IsFalse(FrameGeneration.NeuralSupported, "no plugin in the Editor");
            Assert.IsFalse(FrameGeneration.WantsNeural);
            Settings.NeuralRenderingIntensity = 3f; Assert.AreEqual(1f, Settings.NeuralRenderingIntensity);
            Settings.NeuralRenderingQuality = 9; Assert.AreEqual(2, Settings.NeuralRenderingQuality);
            Assert.IsFalse(Settings.Hdr);
            Assert.IsFalse(Bootstrap.HdrAvailable, "not in the Editor");
            Settings.HdrPaperWhite = 5000f; Assert.AreEqual(400f, Settings.HdrPaperWhite);
            Settings.HdrPeak = 1f; Assert.AreEqual(400f, Settings.HdrPeak);
            Settings.HdrTonemapping = 7; Assert.AreEqual(2, Settings.HdrTonemapping);
            CollectionAssert.IsSubsetOf(new[] { "dlssPreset", "neuralRendering", "neuralRenderingIntensity", "hdr", "hdrPaperWhite", "hdrPeak", "hdrTonemapping" },
                Settings.GraphicsKeys);
            Settings.Reset(Settings.GraphicsKeys);
            Assert.AreEqual(200f, Settings.HdrPaperWhite);
            Assert.AreEqual(1, Settings.HdrTonemapping);
            Assert.AreEqual(2, Settings.NeuralRenderingQuality);
        }

        /// <summary>The later additions of 2026-11-20: the frame rate limits (the first five keep their stored numbers, the
        /// option shows them all in order), Anti-Lag 2, Image Scaling's sharpening and Dynamic Vibrance, XeSS, FSR frame
        /// generation's provider: their defaults, ranges, keys and rows (hidden in the Editor: no plugin).</summary>
        [Test]
        public void FrameRateLimitsAndVendorOptions()
        {
            AllDefaults();
            Assert.AreEqual(0, (int)FrameRate.Fps30); Assert.AreEqual(1, (int)FrameRate.Fps60); Assert.AreEqual(2, (int)FrameRate.Fps120);
            Assert.AreEqual(3, (int)FrameRate.Uncapped); Assert.AreEqual(4, (int)FrameRate.VSync);
            CollectionAssert.AllItemsAreUnique(Settings.FrameRateChoices);
            Assert.AreEqual(System.Enum.GetValues(typeof(FrameRate)).Length, Settings.FrameRateChoices.Length, "every frame rate is offered");
            int before = 0;
            foreach (var rate in Settings.FrameRateChoices)
            {
                int limit = Settings.FrameRateLimit(rate);
                if (rate == FrameRate.Uncapped || rate == FrameRate.VSync) { Assert.AreEqual(0, limit); continue; }
                Assert.Greater(limit, before, rate + " in rising order");
                before = limit;
            }
            Assert.AreEqual(144, Settings.FrameRateLimit(FrameRate.Fps144));
            Settings.FrameRate = FrameRate.Fps165;
            Assert.AreEqual(FrameRate.Fps165, Settings.FrameRate);
            Assert.AreEqual(165, Bootstrap.FrameRateCap);
            var all = GoF2Remake.UI.OptionsCatalog.All();
            var frameRate = all.First(o => o.id == "frameRate");
            var choices = frameRate.choices();
            Assert.AreEqual("165", choices[frameRate.getIndex()]);
            frameRate.setIndex(System.Array.IndexOf(choices, "40"));
            Assert.AreEqual(FrameRate.Fps40, Settings.FrameRate);
            Settings.Reset(Settings.GraphicsKeys);
            Assert.AreEqual(FrameRate.VSync, Settings.FrameRate);

            Assert.IsFalse(Settings.AntiLag);
            Assert.AreEqual(0f, Settings.NisSharpness);
            Assert.IsFalse(Settings.DynamicVibrance);
            Assert.AreEqual(0.5f, Settings.DynamicVibranceIntensity, 1e-4f);
            Assert.AreEqual(0.25f, Settings.DynamicVibranceSaturation, 1e-4f);
            Assert.AreEqual(Settings.FrameGenerationDlss, Settings.FrameGenerationProvider);
            Assert.AreEqual(0, Settings.FrameGenerationTarget);
            Settings.NisSharpness = 4f; Assert.AreEqual(1f, Settings.NisSharpness);
            Settings.DynamicVibranceIntensity = -1f; Assert.AreEqual(0f, Settings.DynamicVibranceIntensity);
            Settings.FrameGenerationProvider = 9; Assert.AreEqual(Settings.FrameGenerationFsr, Settings.FrameGenerationProvider);
            CollectionAssert.IsSubsetOf(new[] { "antiLag", "nisSharpness", "dynamicVibrance", "dynamicVibranceIntensity", "dynamicVibranceSaturation",
                                                "frameGenerationProvider", "frameGenerationTarget" }, Settings.GraphicsKeys);
            Settings.Reset(Settings.GraphicsKeys);
            Assert.AreEqual(0f, Settings.NisSharpness);
            Assert.AreEqual(Settings.FrameGenerationDlss, Settings.FrameGenerationProvider);

            foreach (string id in new[] { "antiLag", "nisSharpness", "dynamicVibrance", "dynamicVibranceIntensity", "frameGenerationProvider", "frameGenerationTarget" })
            {
                var row = all.FirstOrDefault(o => o.id == id);
                Assert.IsNotNull(row, id);
                Assert.IsFalse(row.visible(), id + ": no plugin in the Editor, the row is hidden");
            }
            Assert.IsFalse(FrameGeneration.AntiLagOffered);
            Assert.IsFalse(FrameGeneration.FsrOffered);
            Assert.IsFalse(FrameGeneration.UsesFsr);
            Assert.IsFalse(FrameGeneration.WantsPicture);
            Assert.IsFalse(FrameGeneration.MenuOpen);
            Assert.AreEqual(0f, FrameGeneration.LatencyMs);

            // XeSS: an upscaler choice of its own; a device without it gets a temporal upscaler it has, or none
            Settings.Upscaler = Settings.UpscalerXess;
            Assert.AreEqual(Settings.UpscalerXess, Settings.Upscaler);
            Assert.IsTrue(Bootstrap.IsTemporal(Settings.UpscalerXess));
            if (!Bootstrap.XessSupported) Assert.AreNotEqual(Settings.UpscalerXess, Bootstrap.ActiveUpscaler, "not offered: not active");
            Settings.Reset(Settings.GraphicsKeys);
        }

        /// <summary>The Graphics tab's Apply (2026-11-20): while an options screen is open its Graphics rows stage their changes
        /// (the row shows what was picked, the game keeps the applied value, nothing is told to the appliers), Apply writes
        /// them with one Changed, Discard and a closed screen drop them, a default restored is not left waiting; the other
        /// tabs' rows, and every row without an open screen, apply at once.</summary>
        [Test]
        public void GraphicsOptionsWaitForApply()
        {
            AllDefaults();
            var rows = GoF2Remake.UI.OptionsCatalog.All();
            var lensFlare = rows.First(o => o.id == "lensFlare");
            var bloom = rows.First(o => o.id == "bloom");
            var music = rows.First(o => o.id == "music");
            Assert.IsTrue(lensFlare.staged && bloom.staged, "the Graphics tab's rows");
            Assert.IsFalse(music.staged, "the Sound tab applies as you drag");
            int changes = 0;
            System.Action count = () => changes++;
            Settings.Changed += count;
            try
            {
                // No options screen open: at once, as before.
                bool was = Settings.LensFlare;
                lensFlare.setBool(!was);
                Assert.AreEqual(!was, Settings.LensFlare);
                Assert.IsFalse(Settings.HasStaged);
                lensFlare.setBool(was);

                Settings.BeginStaging();
                changes = 0;
                lensFlare.setBool(!was);
                Assert.AreEqual(was, Settings.LensFlare, "the game keeps the applied value");
                Assert.AreEqual(!was, lensFlare.getBool(), "the row shows what was picked");
                Assert.IsTrue(Settings.HasStaged);
                Assert.AreEqual(0, changes, "nothing applied yet");
                lensFlare.setBool(was);
                Assert.IsFalse(Settings.HasStaged, "back at the applied value: nothing to apply");

                int bloomWas = Settings.BloomStyle, bloomNew = bloomWas == 0 ? 1 : 0;
                lensFlare.setBool(!was);
                bloom.setIndex(bloomNew);
                float volume = Settings.MusicVolume > 0.5f ? 0.25f : 0.75f;
                music.set(volume);
                Assert.AreEqual(volume, Settings.MusicVolume, 1e-4f, "another tab's row applies at once");
                changes = 0;
                Settings.ApplyStaged();
                Assert.AreEqual(!was, Settings.LensFlare);
                Assert.AreEqual(bloomNew, Settings.BloomStyle);
                Assert.IsFalse(Settings.HasStaged);
                Assert.AreEqual(1, changes, "one Changed for the lot");

                lensFlare.setBool(was);
                Settings.DiscardStaged();
                Assert.AreEqual(!was, Settings.LensFlare, "discarded: the applied value stays");
                Assert.AreEqual(!was, lensFlare.getBool());

                bloom.setIndex(bloomWas);
                Settings.Reset(Settings.GraphicsKeys);
                Assert.IsFalse(Settings.HasStaged, "Default settings leaves nothing waiting");

                // A preset stages every option it sets.
                var preset = rows.First(o => o.id == "graphicsPreset");
                int presetWas = preset.getIndex();
                preset.setIndex(presetWas == Settings.PresetLow ? Settings.PresetHigh : Settings.PresetLow);
                Assert.IsTrue(Settings.HasStaged);
                Assert.AreNotEqual(presetWas, preset.getIndex(), "the row shows the picked preset");
                using (Settings.ViewApplied()) Assert.AreEqual(presetWas, preset.getIndex(), "the applied one is unchanged");

                Settings.EndStaging();
                Assert.IsFalse(Settings.HasStaged, "the screen closed: dropped");
                Assert.IsFalse(Settings.Staging);
                Assert.AreEqual(presetWas, preset.getIndex());
            }
            finally
            {
                Settings.Changed -= count;
                Settings.EndStaging();
            }
        }

        /// <summary>The in-game Options page's Apply (2026-11-20): a stop for the keys only while a Graphics change waits; it
        /// applies and the page stays; Back with a change waiting asks first (Back again = Cancel), Discard leaves without it.</summary>
        [Test]
        public void OptionsPageApplyAndLeavePrompt()
        {
            AllDefaults();
            int backs = 0;
            try
            {
                Settings.BeginStaging();
                var view = new GoF2Remake.UI.OptionsView(() => backs++, false);
                view.StepTab(1);   // Graphics
                Assert.IsFalse(view.FooterButtons.Contains(view.ApplyButton), "nothing waits: Apply is skipped");
                var row = view.NavItems().Select(view.RowOf).First(r => r != null && r.def.id == "lensFlare");
                bool was = Settings.LensFlare;
                row.def.setBool(!was);
                Assert.IsTrue(view.FooterButtons.Contains(view.ApplyButton));

                Assert.IsTrue(view.CloseResetPrompt(), "Back: the question first");
                Assert.IsTrue(view.LeavePromptOpen);
                Assert.AreEqual(0, backs);
                Assert.AreEqual(3, view.FooterButtons.Count, "Cancel, Discard, Apply");
                Assert.IsTrue(view.CloseResetPrompt(), "Back again: Cancel");
                Assert.IsFalse(view.LeavePromptOpen);
                Assert.IsTrue(Settings.HasStaged, "the change still waits");

                view.Activate(view.ApplyButton);
                Assert.AreEqual(!was, Settings.LensFlare, "applied");
                Assert.AreEqual(0, backs, "the page stays open");
                Assert.IsFalse(view.CloseResetPrompt(), "nothing waits: the host leaves the page");

                row.def.setBool(was);
                view.Activate(view.BackButton);
                Assert.IsTrue(view.LeavePromptOpen);
                view.Activate(view.LeaveDiscardButton);
                Assert.AreEqual(1, backs, "left");
                Assert.AreEqual(!was, Settings.LensFlare, "without the discarded change");
                Assert.IsFalse(Settings.HasStaged);

                row.def.setBool(was);
                view.Activate(view.BackButton);
                view.Activate(view.LeaveApplyButton);
                Assert.AreEqual(2, backs);
                Assert.AreEqual(was, Settings.LensFlare, "applied on the way out");
            }
            finally { Settings.EndStaging(); }
        }

        /// <summary>Autopilot takes jumpgates (Discord, 2026-11-04): on by default, reset with the Gameplay tab, a row there; the
        /// gate jumps on toward a destination elsewhere unasked while it is on (SystemJump.GateJumpsUnasked), and off only on
        /// a route under way, as before.</summary>
        [Test]
        public void AutoJumpgateOption()
        {
            AllDefaults();
            Assert.IsTrue(Settings.AutoJumpgate, "on by default");
            Assert.IsFalse(Settings.KaamoKeepsEquipment, "stored equipment is opt-in");
            Settings.AutoJumpgate = false;
            Assert.IsFalse(Settings.AutoJumpgate);
            CollectionAssert.Contains(Settings.GameplayKeys, "autoJumpgate");
            Settings.Reset(Settings.GameplayKeys);
            Assert.IsTrue(Settings.AutoJumpgate, "back on after Default settings");
            var row = GoF2Remake.UI.OptionsCatalog.All().Single(o => o.id == "autoJumpgate");
            Assert.AreEqual(GoF2Remake.UI.OptionPage.Gameplay, row.page);
            Assert.AreEqual(GoF2Remake.UI.OptionKind.Toggle, row.kind);

            Assert.IsTrue(GoF2Remake.World.SystemJump.GateJumpsUnasked(30, 4, -1, true), "the first gate, option on");
            Assert.IsFalse(GoF2Remake.World.SystemJump.GateJumpsUnasked(30, 4, -1, false), "the first gate, option off: it asks");
            Assert.IsTrue(GoF2Remake.World.SystemJump.GateJumpsUnasked(30, 4, 30, false), "a later gate on the route: unasked as ever");
            Assert.IsFalse(GoF2Remake.World.SystemJump.GateJumpsUnasked(-1, 4, -1, true), "no destination: the gate's map");
            Assert.IsFalse(GoF2Remake.World.SystemJump.GateJumpsUnasked(4, 4, 4, true), "the destination is here: the gate's map");
        }

        [Test]
        public void SessionRuleDefaults()
        {
            AllDefaults();
            Assert.IsFalse(Settings.CloakLosesPursuers, "off by default");
            // The Original experience by default (2026-11-04): every remake rule off.
            Assert.IsFalse(Settings.PirateEvents, "off by default");
            Assert.IsFalse(Settings.CapitalShips, "off by default");
            Assert.IsTrue(Settings.InformerOriginalRule, "the original's by default");
            Assert.IsFalse(Settings.KaamoStacking, "off by default");
            Assert.AreEqual(5, Settings.KaamoMaxLevel, "upgrade limit 5 by default");
            Assert.IsFalse(Settings.KaamoKeepsEquipment, "off by default");
            Assert.IsFalse(Settings.CloakBay, "off by default");
            Assert.IsFalse(Settings.EmpBreaksCloak, "off by default");
            Assert.IsFalse(Settings.SmarterNpcTargeting, "the original's targeting by default (#121)");
            Assert.AreEqual(SessionRules.CollisionOriginal, Settings.ShipCollision, "Original by default");
            Assert.AreEqual(SessionRules.DogfightModHulls, Settings.NpcDogfight, "GoF3's dogfighting on the hulls that ask for it");
            Assert.AreEqual(SessionRules.WithCollision(SessionRules.InformerOriginalRule, SessionRules.CollisionOriginal), Settings.LocalRules());
        }

        [Test]
        public void TheDogfightingModeTravelsInThreeBits()
        {
            AllDefaults();
            Settings.NpcDogfight = SessionRules.DogfightOriginalMode;
            Assert.AreEqual(SessionRules.DogfightOriginal, Settings.LocalRules() & (SessionRules.DogfightOriginal | SessionRules.DogfightEvery));
            Settings.NpcDogfight = SessionRules.DogfightAll;
            Assert.AreEqual(SessionRules.DogfightEvery, Settings.LocalRules() & (SessionRules.DogfightOriginal | SessionRules.DogfightEvery));
            Assert.AreEqual(SessionRules.DogfightAll, SessionRules.DogfightOf(Settings.LocalRules()));
            Assert.AreEqual(SessionRules.DogfightModHulls, SessionRules.DogfightOf(0), "a session without the bits: the default");
            Assert.AreEqual(SessionRules.DogfightOriginalMode, SessionRules.DogfightOf(SessionRules.WithDogfight(SessionRules.DogfightEvery, 0)));
            Settings.NpcDogfight = SessionRules.DogfightEs2All;
            Assert.AreEqual(SessionRules.DogfightEs2, Settings.LocalRules() & (SessionRules.DogfightOriginal | SessionRules.DogfightEvery | SessionRules.DogfightEs2));
            Assert.AreEqual(SessionRules.DogfightEs2All, SessionRules.DogfightOf(Settings.LocalRules()));
            Settings.NpcDogfight = 7;
            Assert.AreEqual(SessionRules.DogfightEs2All, Settings.NpcDogfight, "clamped");
        }

        [Test]
        public void Es2AimingIsAnOptionOfItsOwn()
        {
            AllDefaults();
            Assert.IsFalse(Settings.Es2Aiming, "GoF2's aim by default");
            Settings.Es2Aiming = true;
            Assert.AreEqual(SessionRules.Es2Aiming, Settings.LocalRules() & SessionRules.Es2Aiming);
            Assert.AreEqual(SessionRules.DogfightModHulls, SessionRules.DogfightOf(Settings.LocalRules()), "the dogfighting untouched");
            Settings.Es2Aiming = false;
            Assert.AreEqual(0, Settings.LocalRules() & SessionRules.Es2Aiming);
            Assert.IsFalse(Settings.Es2NpcFlight, "GoF2's NPC flight by default");
            Settings.Es2NpcFlight = true;
            Assert.AreEqual(SessionRules.Es2NpcFlight, Settings.LocalRules() & (SessionRules.Es2NpcFlight | SessionRules.Es2Aiming), "a bit of its own");
            Settings.Es2NpcFlight = false;
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
            Assert.AreEqual(SessionRules.CollisionOriginal, Flight.HullCollision.Mode, "Original by default");
            Settings.ShipCollision = SessionRules.CollisionMixed;
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
            Settings.Upscaler = 99; Assert.AreEqual(Settings.UpscalerXess, Settings.Upscaler);
            Settings.Upscaler = -2; Assert.AreEqual(Settings.UpscalerOff, Settings.Upscaler);
            Settings.UpscalerQuality = 9; Assert.AreEqual(4, Settings.UpscalerQuality);
        }

        [Test]
        public void HintSizeAndTheComboAndMedalSoundsAreOptions()
        {
            // #171: the hint rows' own size, 50..100 %, full size by default (Gameplay); #165: the Supernova Challenge's
            // combo sounds and the medal sound, as now by default (Sound), each back with its tab's "Default settings".
            AllDefaults();
            Assert.AreEqual(100, Settings.HintScalePercent);
            Assert.IsTrue(Settings.ChallengeComboSounds, "the original's combo sounds play");
            Assert.AreEqual(Settings.MedalSoundChime, Settings.MedalSound, "the remake's chime");
            Settings.HintScalePercent = 20; Assert.AreEqual(50, Settings.HintScalePercent);
            Settings.HintScalePercent = 130; Assert.AreEqual(100, Settings.HintScalePercent);
            Settings.MedalSound = 7; Assert.AreEqual(Settings.MedalSoundOff, Settings.MedalSound);
            Settings.MedalSound = -1; Assert.AreEqual(Settings.MedalSoundChime, Settings.MedalSound);

            Settings.HintScalePercent = 60;
            Settings.ChallengeComboSounds = false;
            Settings.MedalSound = Settings.MedalSoundOriginal;
            Settings.Reset(Settings.CosmeticsKeys);
            Assert.AreEqual(100, Settings.HintScalePercent, "Cosmetics' Default settings");
            Settings.Reset(Settings.SoundKeys);
            Assert.IsTrue(Settings.ChallengeComboSounds, "Sound's Default settings");
            Assert.AreEqual(Settings.MedalSoundChime, Settings.MedalSound);
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
        public void AndroidAtOneHundredPercentDrawsFarEnoughBelowItForUrp()
        {
            // #163: Android's 100 % is drawn at 94 % (URP's render-scale-1 path left part of the screen black), MSAA as picked.
            Assert.AreEqual(Bootstrap.AndroidFullScale, Bootstrap.AppliedRenderScale(1f, true, false));
            Assert.GreaterOrEqual(Mathf.Abs(1f - Bootstrap.AppliedRenderScale(1f, true, false)), 0.05f);
            Assert.AreEqual(1, Bootstrap.AppliedMsaa(1, 1f, true, false));
            Assert.AreEqual(1.25f, Bootstrap.AppliedRenderScale(1.25f, true, false));
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

        // The strength slider: 100 % by default, 25..200 %, offered only while the remake's bloom is drawn.
        [Test]
        public void BloomStrengthScalesTheRemakeBloomOnly()
        {
            Assert.AreEqual(1f, Settings.BloomStrength);
            Settings.BloomStrength = 5f; Assert.AreEqual(2f, Settings.BloomStrength);
            Settings.BloomStrength = 0f; Assert.AreEqual(0.25f, Settings.BloomStrength);
            var row = GoF2Remake.UI.OptionsCatalog.All().First(o => o.id == "bloomStrength");
            Settings.BloomStyle = Settings.BloomOff; Assert.IsFalse(row.visible());
            Settings.BloomStyle = Settings.BloomOriginal; Assert.IsFalse(row.visible());
            Settings.BloomStyle = Settings.BloomRemake; Assert.IsTrue(row.visible());
            Settings.BloomStyle = Settings.BloomBoth; Assert.IsTrue(row.visible());
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
            var groups = new[] { Settings.SoundKeys, Settings.GraphicsKeys, Settings.ControlsKeys, Settings.GameplayKeys, Settings.CosmeticsKeys, Settings.LanguageKeys };
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
        [Test]
        public void CameraShakeDefaultsToTheOriginalsAndTheRemakeLooksSoftenIt()
        {
            AllDefaults();
            Assert.AreEqual(1f, Settings.CameraShake, 1e-6f, "the ported shakes at full strength (2026-11-19)");
            Settings.CameraShake = 0.75f;
            DataUiTestKit.ClearSettingsCache();
            Assert.AreEqual(0.75f, Settings.CameraShake, 1e-6f, "existing choices are kept");
            Settings.Reset(Settings.GraphicsKeys);
            Assert.AreEqual(1f, Settings.CameraShake, 1e-6f, "Graphics reset: the default");
            Settings.ApplyScopedExperience(true, 1);
            Assert.AreEqual(0.3f, Settings.CameraShake, 1e-6f, "Cosmetic Experience Remake: #342's 30 %");
            Settings.ApplyScopedExperience(true, 0);
            Assert.AreEqual(1f, Settings.CameraShake, 1e-6f, "Cosmetic Experience Original");
        }

        [Test]
        public void EveryAdditionStartsOffAndTheOriginalPresetsTurnItOff()
        {
            AllDefaults();
            Assert.IsFalse(Settings.RebuiltPlanets);
            Assert.IsFalse(Settings.RemasteredEffects);
            Assert.IsFalse(Settings.TurretLeadAim);
            Assert.IsFalse(Settings.RemakeFreeLook);
            Assert.IsFalse(Settings.BlueprintAutocomplete);
            Assert.IsFalse(Settings.HostileAutopilotFastForward);
            Assert.AreEqual(Settings.ExperienceOriginal, Settings.CurrentExperience(), "the defaults are the Original experience");

            Settings.ApplyExperience(Settings.ExperienceRemake);
            Assert.IsTrue(Settings.RebuiltPlanets && Settings.RemasteredEffects && Settings.TurretLeadAim && Settings.RemakeFreeLook
                          && Settings.BlueprintAutocomplete && Settings.HostileAutopilotFastForward, "Remake turns every addition on");
            Settings.ApplyScopedExperience(false, 0);
            Assert.IsFalse(Settings.TurretLeadAim || Settings.RemakeFreeLook || Settings.BlueprintAutocomplete || Settings.HostileAutopilotFastForward,
                           "Gameplay Experience Original");
            Assert.IsTrue(Settings.RebuiltPlanets && Settings.RemasteredEffects, "the gameplay preset leaves the looks");
            Settings.ApplyScopedExperience(true, 0);
            Assert.IsFalse(Settings.RebuiltPlanets || Settings.RemasteredEffects, "Cosmetic Experience Original: the graphics page's looks too");
            Assert.AreEqual(0, Settings.CurrentScopedExperience(true));
            Assert.AreEqual(0, Settings.CurrentScopedExperience(false));
        }

    }
}
