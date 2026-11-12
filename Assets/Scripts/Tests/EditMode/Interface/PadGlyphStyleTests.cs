// PadGlyphStyleTests.cs
// CLAUDE.md "UI and platforms", Input modes: the controller hints show Xbox or PlayStation buttons (InputMode.Style).
// On Auto (Settings.PadGlyphs) the controller last used picks it: Sony's pads (the DualShock / DualSense layouts, or a pad
// reporting Sony as its maker) PlayStation, any other Xbox; the option can force either. PadGlyph and GameControls' short
// names (binding cells, #KEY_* tokens) follow it; a DualShock 4's Create button is Share.

using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.UI;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class PadGlyphStyleTests
    {
        DataUiTestKit.PrefsSnapshot prefs;
        DataUiTestKit.TextLanguage english;
        InputKind mode;

        [SetUp]
        public void Defaults()
        {
            prefs = new DataUiTestKit.PrefsSnapshot();
            english = new DataUiTestKit.TextLanguage("en");
            Settings.FlightStyle = FlightStyles.Original;
            GameControls.ApplyStyle();
            GameControls.ResetToDefaults();
            mode = InputMode.Current;
        }

        [TearDown]
        public void Restore()
        {
            DataUiTestKit.SetInputMode(mode);
            english?.Dispose();
            prefs?.Dispose();
            GameControls.ApplyStyle();
        }

        [TestCase("DualSenseGamepadHID", "", "", PadStyle.PlayStation)]
        [TestCase("DualShock4GamepadHID", "", "", PadStyle.PlayStation)]
        [TestCase("DualShock3GamepadHID", "", "", PadStyle.PlayStation)]
        [TestCase("DualShockGamepad", "", "", PadStyle.PlayStation)]
        [TestCase("AndroidGamepad", "Sony Interactive Entertainment", "Wireless Controller", PadStyle.PlayStation)]
        [TestCase("Gamepad", "", "DualSense Wireless Controller", PadStyle.PlayStation)]
        [TestCase("XInputControllerWindows", "", "", PadStyle.Xbox)]
        [TestCase("Gamepad", "", "", PadStyle.Xbox)]
        [TestCase("SwitchProControllerHID", "Nintendo", "Pro Controller", PadStyle.Switch)]
        [TestCase("SwitchProControllerHID", "", "", PadStyle.Switch)]
        [TestCase("AndroidGamepad", "Nintendo", "Pro Controller", PadStyle.Switch)]
        [TestCase("AndroidGamepad", "", "Pro Controller", PadStyle.Switch)]
        [TestCase("Gamepad", "", "Joy-Con (L/R)", PadStyle.Switch)]
        [TestCase("XInputControllerWindows", "PDP", "Wired Pro Controller for Xbox", PadStyle.Xbox)]
        [TestCase("AndroidGamepad", "", "Wireless Controller", PadStyle.Xbox)]
        [TestCase("NoSuchLayout", null, null, PadStyle.Xbox)]
        [TestCase(null, null, null, PadStyle.Xbox)]
        public void SonyPadsArePlayStationNintendoSwitchAnyOtherXbox(string layout, string maker, string product, PadStyle style)
        {
            Assert.AreEqual(style, InputMode.StyleOf(layout, maker, product));
        }

        [Test]
        public void ARealDeviceIsDetected()
        {
            Assert.AreEqual(PadStyle.Xbox, InputMode.StyleOf((InputDevice)null));
            var sense = InputSystem.AddDevice("DualSenseGamepadHID");
            var ds4 = InputSystem.AddDevice("DualShock4GamepadHID");
            var plain = InputSystem.AddDevice<Gamepad>();
            try
            {
                Assert.AreEqual(PadStyle.PlayStation, InputMode.StyleOf(sense));
                Assert.AreEqual(PadStyle.PlayStation, InputMode.StyleOf(ds4));
                Assert.AreEqual(PadStyle.Xbox, InputMode.StyleOf(plain));
                Assert.IsTrue(InputMode.IsDualShock4(ds4), "Share, not Create");
                Assert.IsFalse(InputMode.IsDualShock4(sense));
                Assert.IsFalse(InputMode.IsDualShock4(plain));
            }
            finally
            {
                InputSystem.RemoveDevice(sense);
                InputSystem.RemoveDevice(ds4);
                InputSystem.RemoveDevice(plain);
            }
        }

        [Test]
        public void TheOptionForcesAStyleAndDefaultsToAuto()
        {
            Settings.PadGlyphs = Settings.PadGlyphsXbox;
            Assert.AreEqual(PadStyle.Xbox, InputMode.Style);
            Settings.PadGlyphs = Settings.PadGlyphsPlayStation;
            Assert.AreEqual(PadStyle.PlayStation, InputMode.Style);
            Settings.PadGlyphs = Settings.PadGlyphsAuto;
            Assert.AreEqual(InputMode.DetectedStyle, InputMode.Style, "Auto: the controller last used");
            Settings.PadGlyphs = Settings.PadGlyphsSwitch;
            Assert.AreEqual(PadStyle.Switch, InputMode.Style);
            Settings.PadGlyphs = 7;
            Assert.AreEqual(Settings.PadGlyphsSwitch, Settings.PadGlyphs, "clamped");
            CollectionAssert.Contains(Settings.ControlsKeys, "padGlyphs", "Default settings (Controls) resets it");
            Settings.Reset(Settings.ControlsKeys);
            Assert.AreEqual(Settings.PadGlyphsAuto, Settings.PadGlyphs);
        }

        [Test]
        public void TheOptionIsAControlsRow()
        {
            var row = OptionsCatalog.All().Find(o => o.id == "padGlyphs");
            Assert.IsNotNull(row);
            Assert.AreEqual(OptionPage.Controls, row.page);
            CollectionAssert.AreEqual(new[] { "Auto", "Xbox", "PlayStation", "Switch" }, row.choices());
            Settings.PadGlyphs = Settings.PadGlyphsPlayStation;
            Assert.AreEqual(2, row.getIndex());
            row.setIndex(1);
            Assert.AreEqual(Settings.PadGlyphsXbox, Settings.PadGlyphs);
        }

        static string Ps(string path) => GameControls.ShortName(path, PadStyle.PlayStation);
        static string Xb(string path) => GameControls.ShortName(path, PadStyle.Xbox);

        [Test]
        public void ShortNamesFollowTheStyle()
        {
            Assert.AreEqual("×", Ps("<Gamepad>/buttonSouth"));
            Assert.AreEqual("○", Ps("<Gamepad>/buttonEast"));
            Assert.AreEqual("□", Ps("<Gamepad>/buttonWest"));
            Assert.AreEqual("△", Ps("<Gamepad>/buttonNorth"));
            Assert.AreEqual("L1", Ps("<Gamepad>/leftShoulder"));
            Assert.AreEqual("R2", Ps("<Gamepad>/rightTrigger"));
            Assert.AreEqual("L", Ps("<Gamepad>/leftStick"));
            Assert.AreEqual("R3", Ps("<Gamepad>/rightStickPress"));
            Assert.AreEqual("R ←", Ps("<Gamepad>/rightStick/left"));
            Assert.AreEqual("D-PAD ↑", Ps("<Gamepad>/dpad/up"));
            Assert.AreEqual("OPTIONS", Ps("<Gamepad>/start"));
            Assert.AreEqual(InputMode.DualShock4 ? "SHARE" : "CREATE", Ps("<Gamepad>/select"));
            Assert.AreEqual("TOUCHPAD", Ps("<DualSenseGamepadHID>/touchpadButton"), "a capture of a pad-only control names its layout");
            // Xbox unchanged
            Assert.AreEqual("A", Xb("<Gamepad>/buttonSouth"));
            Assert.AreEqual("RS", Xb("<Gamepad>/rightStickPress"));
            Assert.AreEqual("RS ←", Xb("<Gamepad>/rightStick/left"));
            Assert.AreEqual("VIEW", Xb("<Gamepad>/select"));
            Assert.AreEqual("F", Ps("<Keyboard>/f"), "keys are never pad names");
            Assert.IsNull(GameControls.PadControl("<Keyboard>/a"));
            Assert.AreEqual("buttonsouth", GameControls.PadControl("<XInputController>/buttonSouth"));
        }

        [Test]
        public void HintTokensAndBindingCellsUseTheStyle()
        {
            DataUiTestKit.SetInputMode(InputKind.Gamepad);
            Settings.PadGlyphs = Settings.PadGlyphsPlayStation;
            Assert.AreEqual("×", FlightHints.KeyTokens("#KEY_BOOST"));
            Assert.AreEqual("R2", FlightHints.KeyTokens("#KEY_PRIMARY"));
            Assert.AreEqual("R3", FlightHints.KeyTokens("#KEY_CLOAK"));
            Assert.AreEqual("R1 L1", GameControls.SlotText(DataUiTestKit.Row("throttle"), BindSlot.Pad));
            Settings.PadGlyphs = Settings.PadGlyphsXbox;
            Assert.AreEqual("A", FlightHints.KeyTokens("#KEY_BOOST"));
            Assert.AreEqual("RB LB", GameControls.SlotText(DataUiTestKit.Row("throttle"), BindSlot.Pad));
        }

        static string Text(VisualElement e) => ((Label)e).text;

        [TestCase(PadButton.A, PadGlyph.Shape.Cross, "pad-face--cross")]
        [TestCase(PadButton.B, PadGlyph.Shape.Circle, "pad-face--circle")]
        [TestCase(PadButton.X, PadGlyph.Shape.Square, "pad-face--square")]
        [TestCase(PadButton.Y, PadGlyph.Shape.Triangle, "pad-face--triangle")]
        public void PlayStationFaceButtonsAreDrawnSymbols(PadButton button, PadGlyph.Shape shape, string cls)
        {
            Settings.PadGlyphs = Settings.PadGlyphsPlayStation;
            var g = (PadGlyph)InputGlyph.Pad(button);
            Assert.AreEqual(shape, g.Symbol);
            Assert.AreEqual("", g.text, "the symbol is drawn, not text");
            Assert.IsTrue(g.ClassListContains("pad-face") && g.ClassListContains("pad-face--ps") && g.ClassListContains(cls));
            Assert.AreEqual(PickingMode.Ignore, g.pickingMode);
        }

        [TestCase(PadButton.LeftBumper, "L1", "pad-bumper")]
        [TestCase(PadButton.RightBumper, "R1", "pad-bumper")]
        [TestCase(PadButton.LeftTrigger, "L2", "pad-trigger")]
        [TestCase(PadButton.RightTrigger, "R2", "pad-trigger")]
        [TestCase(PadButton.LeftStick, "L", "pad-stick")]
        [TestCase(PadButton.RightStickPress, "R3", "pad-stick")]
        [TestCase(PadButton.Menu, "OPTIONS", "pad-menu")]
        [TestCase(PadButton.DPad, "D-PAD", "pad-menu")]
        public void PlayStationButtonsAreNamedLikeTheirs(PadButton button, string text, string cls)
        {
            Settings.PadGlyphs = Settings.PadGlyphsPlayStation;
            var g = InputGlyph.Pad(button);
            Assert.AreEqual(text, Text(g));
            Assert.IsTrue(g.ClassListContains(cls), cls);
        }

        [Test]
        public void AGlyphSwitchesInPlaceKeepingItsOwnersClasses()
        {
            Settings.PadGlyphs = Settings.PadGlyphsXbox;
            var g = (PadGlyph)InputGlyph.Pad(PadButton.A);
            g.AddToClassList("launch-glyph--pad");
            Assert.AreEqual("A", g.text);
            g.Apply(PadStyle.PlayStation);
            Assert.AreEqual(PadGlyph.Shape.Cross, g.Symbol);
            Assert.IsFalse(g.ClassListContains("pad-face--a"));
            g.Apply(PadStyle.Xbox);
            Assert.AreEqual("A", g.text);
            Assert.AreEqual(PadGlyph.Shape.None, g.Symbol);
            Assert.IsTrue(g.ClassListContains("pad-face--a"));
            Assert.IsFalse(g.ClassListContains("pad-face--ps") || g.ClassListContains("pad-face--cross"));
            Assert.IsTrue(g.ClassListContains("launch-glyph--pad"), "the owner's class stays");
        }

        [Test]
        public void HintsFromBindingsUseTheStyle()
        {
            Settings.PadGlyphs = Settings.PadGlyphsPlayStation;
            var fire = (System.Collections.Generic.List<VisualElement>)DataUiTestKit.CallStatic(typeof(InputGlyph), "For",
                DataUiTestKit.FlightAction("FirePrimary"), InputKind.Gamepad, false);
            Assert.AreEqual("R2", Text(fire[0]));
            var dodge = (System.Collections.Generic.List<VisualElement>)DataUiTestKit.CallStatic(typeof(InputGlyph), "For",
                DataUiTestKit.FlightAction("DodgeLeft"), InputKind.Gamepad, false);
            Assert.AreEqual("R ←", Text(dodge[0]));
            Assert.IsTrue(dodge[0].ClassListContains("pad-stick"));
            var boost = (System.Collections.Generic.List<VisualElement>)DataUiTestKit.CallStatic(typeof(InputGlyph), "For",
                DataUiTestKit.FlightAction("Boost"), InputKind.Gamepad, false);
            Assert.AreEqual(PadGlyph.Shape.Cross, ((PadGlyph)boost[0]).Symbol);
        }

        // ---- #162: Nintendo controllers ----

        /// <summary>InputMode's controller last used, as if 'pad' had just been pressed (its private UsedPad).</summary>
        static void Used(InputDevice pad) => DataUiTestKit.CallStatic(typeof(InputMode), "UsedPad", pad);

        static InputDevice LastPad() =>
            (InputDevice)typeof(InputMode).GetField("lastPad", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);

        static string Sw(string path) => GameControls.ShortName(path, PadStyle.Switch);

        [Test]
        public void ANintendoPadIsDetectedAndNamesItsButtonsWhereTheySit()
        {
            var before = LastPad();
            var pro = InputSystem.AddDevice("SwitchProControllerHID");
            try
            {
                Assert.IsTrue(InputMode.IsNintendo(pro));
                Assert.AreEqual(PadStyle.Switch, InputMode.StyleOf(pro));
                Used(pro);
                Assert.AreEqual(PadStyle.Switch, InputMode.DetectedStyle);

                // by position (the Input System's own mapping): south is the pad's B
                Settings.NintendoLayout = false;
                Assert.IsFalse(InputMode.NintendoSwap);
                Assert.AreEqual("B", Sw("<Gamepad>/buttonSouth"));
                Assert.AreEqual("A", Sw("<Gamepad>/buttonEast"));
                Assert.AreEqual("Y", Sw("<Gamepad>/buttonWest"));
                Assert.AreEqual("X", Sw("<Gamepad>/buttonNorth"));
                // Nintendo's layout (the default): A and B swapped, so buttonSouth (confirm) is the pad's A
                Settings.NintendoLayout = true;
                Assert.IsTrue(InputMode.NintendoSwap);
                Assert.AreEqual("A", Sw("<Gamepad>/buttonSouth"));
                Assert.AreEqual("B", Sw("<Gamepad>/buttonEast"));
                Assert.AreEqual("Y", Sw("<Gamepad>/buttonWest"), "X / Y aren't swapped");
                Assert.AreEqual("L", Sw("<Gamepad>/leftShoulder"));
                Assert.AreEqual("ZR", Sw("<Gamepad>/rightTrigger"));
                Assert.AreEqual("+", Sw("<Gamepad>/start"));
                Assert.AreEqual("−", Sw("<Gamepad>/select"));
                Assert.AreEqual("LS", Sw("<Gamepad>/leftStickPress"), "the sticks keep LS / RS: L / R are the shoulders");
                Assert.AreEqual("RS ←", Sw("<Gamepad>/rightStick/left"));
                Assert.AreEqual("D-PAD ↑", Sw("<Gamepad>/dpad/up"));
            }
            finally
            {
                Used(before);
                InputSystem.RemoveDevice(pro);
            }
            Assert.IsFalse(InputMode.IsNintendo((InputDevice)null));
        }

        [Test]
        public void TheSwapExchangesANintendoPadsSouthAndEastInItsEvents()
        {
            var pro = (Gamepad)InputSystem.AddDevice("SwitchProControllerHID");
            try
            {
                using (StateEvent.From(pro, out var e))
                {
                    pro.buttonEast.WriteValueIntoEvent(1f, e);   // the pad's A pressed (right)
                    pro.buttonSouth.WriteValueIntoEvent(0f, e);
                    InputMode.SwapFaceButtons(e, pro);
                    Assert.AreEqual(1f, pro.buttonSouth.ReadValueFromEvent(e), "A now reads as the confirm button");
                    Assert.AreEqual(0f, pro.buttonEast.ReadValueFromEvent(e));
                    pro.buttonWest.WriteValueIntoEvent(1f, e);
                    InputMode.SwapFaceButtons(e, pro);
                    Assert.AreEqual(1f, pro.buttonEast.ReadValueFromEvent(e), "swapping twice puts them back");
                    Assert.AreEqual(1f, pro.buttonWest.ReadValueFromEvent(e), "the others are left alone");
                }
            }
            finally { InputSystem.RemoveDevice(pro); }
        }

        [Test]
        public void SwitchFaceButtonsAreDarkWithNintendosLetters()
        {
            var before = LastPad();
            var pro = InputSystem.AddDevice("SwitchProControllerHID");
            try
            {
                Used(pro);
                Settings.NintendoLayout = true;
                Settings.PadGlyphs = Settings.PadGlyphsSwitch;
                var a = (PadGlyph)InputGlyph.Pad(PadButton.A);
                Assert.AreEqual("A", a.text);
                Assert.AreEqual(PadGlyph.Shape.None, a.Symbol);
                Assert.IsTrue(a.ClassListContains("pad-face") && a.ClassListContains("pad-face--switch"));
                Assert.IsFalse(a.ClassListContains("pad-face--a"), "not Xbox's green");
                Assert.AreEqual("X", Text(InputGlyph.Pad(PadButton.Y)));
                Assert.AreEqual("ZL", Text(InputGlyph.Pad(PadButton.LeftTrigger)));
                Assert.AreEqual("R", Text(InputGlyph.Pad(PadButton.RightBumper)));
                Assert.AreEqual("+", Text(InputGlyph.Pad(PadButton.Menu)));
                Settings.NintendoLayout = false;
                a.Apply();
                Assert.AreEqual("B", a.text, "by position the confirm button is the pad's B");
            }
            finally
            {
                Used(before);
                InputSystem.RemoveDevice(pro);
            }
        }

        [Test]
        public void TheNintendoLayoutIsAControlsOptionOnByDefault()
        {
            var row = OptionsCatalog.All().Find(o => o.id == "nintendoLayout");
            Assert.IsNotNull(row);
            Assert.AreEqual(OptionPage.Controls, row.page);
            CollectionAssert.Contains(Settings.ControlsKeys, "nintendoLayout");
            Settings.NintendoLayout = false;
            Settings.Reset(Settings.ControlsKeys);
            Assert.IsTrue(Settings.NintendoLayout);
        }
    }
}
