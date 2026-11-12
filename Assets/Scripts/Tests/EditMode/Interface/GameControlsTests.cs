// GameControlsTests.cs
// CLAUDE.md "UI and platforms", Key bindings / Keyboard: the flight controls are one code-made InputActionMap of 31 rows,
// each with two keyboard / mouse slots and a controller slot (none for mouse steering and the chat's send / channel keys).
// The defaults typed below are CLAUDE.md's (the PC / Mac version's keys: arrows steer, W boost, S brake, ] and / thrust,
// Space / LMB fire, R / RMB secondary, A / D strafe, 1 / 3 roll, F (and Enter) action, Q autopilot, E actions, Tab
// fast-forward, T view, V wingmen, K Khador Drive, C cloak, M / middle mouse mouse steering, 2 level out, G switch secondary,
// X time extender, Y auto turret, U unlock target, F12 screenshot, F1 toggle HUD, B chat, Enter / keypad Enter send, Tab channel, N multiplayer window,
// the distress call unbound; the controller: LS steer, RB / LB throttle, A boost, Y level out and fast-forward, RT / LT
// fire, D-pad right / up / down switch secondary / camera / auto turret, B unlock target, X action, View autopilot menu, D-pad left
// actions menu, RS / LS press cloak / time extender, RS left / right dodge; the drill: arrows, W A S D second, LS). The
// overrides are kept in PlayerPrefs "controls_bindings" as { version 2, bindings [action, index, path] } (an empty path
// unbinds); every other version is ignored. Short names: the Xbox labels (pinned; PadGlyphStyleTests has the PlayStation ones).

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Flight;
using GoF2Remake.Data;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class GameControlsTests
    {
        DataUiTestKit.PrefsSnapshot prefs;
        DataUiTestKit.TextLanguage english;

        [SetUp]
        public void Defaults()
        {
            prefs = new DataUiTestKit.PrefsSnapshot();
            english = new DataUiTestKit.TextLanguage("en");
            // The GoF2 key layout: a player who chose free flight in Options has its own layout (GameControls.ApplyStyle).
            Settings.FlightStyle = FlightStyles.Original;
            Settings.PadGlyphs = Settings.PadGlyphsXbox;   // the Xbox names, whatever controller the player last used
            GameControls.ApplyStyle();
            GameControls.ResetToDefaults();
        }

        [TearDown]
        public void Restore()
        {
            english?.Dispose();
            prefs?.Dispose();
            GameControls.ApplyStyle();   // the player's own layout again (the option is back from the snapshot)
        }

        static readonly string[] RowIds =
        {
            "steer", "throttle", "brake", "boost", "roll", "levelOut", "strafeLeft", "strafeRight", "dodgeLeft", "dodgeRight", "drill",
            "firePrimary", "fireSecondary", "switchSecondary", "camera", "cockpitView", "autoTurret", "unlockTarget", "action", "autopilotMenu", "actionsMenu",
            "fastForward", "wingmen", "khadorDrive", "cloak", "timeExtender", "mouseSteering", "chat", "chatSend", "chatChannel",
            "screenshot", "hudToggle", "multiplayerWindow", "distressCall",
            "thrust", "strafe", "hover", "dampeners", "freeLookHold",   // free flight (PR #87)
            "device1", "device2", "device3",   // the active devices (PlayerDevices)
        };

        [Test]
        public void ThereAreFortyTwoFlightRows()
        {
            Assert.AreEqual(42, GameControls.Rows.Count);
            CollectionAssert.AreEqual(RowIds, GameControls.Rows.Select(r => r.id).ToArray(), "in the options' order (the overrides are kept by action name)");
            foreach (var r in GameControls.Rows)
            {
                Assert.IsNotNull(r.label, r.id);
                Assert.IsFalse(string.IsNullOrWhiteSpace(r.label()), r.id + " label");
                Assert.IsTrue(r.HasSlot(BindSlot.Key1) && r.HasSlot(BindSlot.Key2), r.id + ": two keyboard slots");
            }
        }

        [Test]
        public void OnlyTheKeyboardOnlyRowsLackAControllerSlot()
        {
            var noPad = GameControls.Rows.Where(r => !r.HasSlot(BindSlot.Pad)).Select(r => r.id).ToArray();
            CollectionAssert.AreEquivalent(new[] { "mouseSteering", "chatSend", "chatChannel" }, noPad);
        }

        [Test]
        public void CompositeRowsHaveTheirParts()
        {
            Assert.AreEqual(4, DataUiTestKit.Row("steer").slots[0].Length, "steer: up / down / left / right");
            Assert.AreEqual(1, DataUiTestKit.Row("steer").slots[2].Length, "steer's controller slot: a whole stick");
            Assert.AreEqual("Vector2", DataUiTestKit.Row("steer").padType);
            Assert.AreEqual(2, DataUiTestKit.Row("throttle").slots[0].Length, "faster / slower");
            Assert.AreEqual(2, DataUiTestKit.Row("throttle").slots[2].Length, "two controller buttons");
            Assert.AreEqual(2, DataUiTestKit.Row("roll").slots[2].Length);
            Assert.AreEqual(4, DataUiTestKit.Row("drill").slots[1].Length, "W A S D as the drill's second keys");
            Assert.AreEqual(4, DataUiTestKit.Row("steer").partLabels.Length);
            Assert.AreEqual("Button", DataUiTestKit.Row("boost").padType);
        }

        static IEnumerable<TestCaseData> Defaults_()
        {
            TestCaseData D(string row, BindSlot slot, params string[] paths) => new TestCaseData(row, slot, paths).SetName($"Default_{row}_{slot}");
            yield return D("steer", BindSlot.Key1, "<Keyboard>/upArrow", "<Keyboard>/downArrow", "<Keyboard>/leftArrow", "<Keyboard>/rightArrow");
            yield return D("steer", BindSlot.Key2, "", "", "", "");
            yield return D("steer", BindSlot.Pad, "<Gamepad>/leftStick");
            yield return D("throttle", BindSlot.Key1, "<Keyboard>/rightBracket", "<Keyboard>/slash");
            yield return D("throttle", BindSlot.Pad, "<Gamepad>/rightShoulder", "<Gamepad>/leftShoulder");
            yield return D("brake", BindSlot.Key1, "<Keyboard>/s");
            yield return D("brake", BindSlot.Pad, "");
            yield return D("boost", BindSlot.Key1, "<Keyboard>/w");
            yield return D("boost", BindSlot.Pad, "<Gamepad>/buttonSouth");
            yield return D("roll", BindSlot.Key1, "<Keyboard>/1", "<Keyboard>/3");
            yield return D("roll", BindSlot.Pad, "", "");
            yield return D("levelOut", BindSlot.Key1, "<Keyboard>/2");
            yield return D("levelOut", BindSlot.Pad, "<Gamepad>/buttonNorth");
            yield return D("strafeLeft", BindSlot.Key1, "<Keyboard>/a");
            yield return D("strafeRight", BindSlot.Key1, "<Keyboard>/d");
            yield return D("dodgeLeft", BindSlot.Key1, "");
            yield return D("dodgeLeft", BindSlot.Pad, "<Gamepad>/rightStick/left");
            yield return D("dodgeRight", BindSlot.Key1, "");
            yield return D("dodgeRight", BindSlot.Pad, "<Gamepad>/rightStick/right");
            yield return D("drill", BindSlot.Key1, "<Keyboard>/upArrow", "<Keyboard>/downArrow", "<Keyboard>/leftArrow", "<Keyboard>/rightArrow");
            yield return D("drill", BindSlot.Key2, "<Keyboard>/w", "<Keyboard>/s", "<Keyboard>/a", "<Keyboard>/d");
            yield return D("drill", BindSlot.Pad, "<Gamepad>/leftStick");
            yield return D("firePrimary", BindSlot.Key1, "<Keyboard>/space");
            yield return D("firePrimary", BindSlot.Key2, "<Mouse>/leftButton");
            yield return D("firePrimary", BindSlot.Pad, "<Gamepad>/rightTrigger");
            yield return D("fireSecondary", BindSlot.Key1, "<Keyboard>/r");
            yield return D("fireSecondary", BindSlot.Key2, "<Mouse>/rightButton");
            yield return D("fireSecondary", BindSlot.Pad, "<Gamepad>/leftTrigger");
            yield return D("switchSecondary", BindSlot.Key1, "<Keyboard>/g");
            yield return D("switchSecondary", BindSlot.Pad, "<Gamepad>/dpad/right");
            yield return D("camera", BindSlot.Key1, "<Keyboard>/t");
            yield return D("camera", BindSlot.Pad, "<Gamepad>/dpad/up");
            yield return D("autoTurret", BindSlot.Key1, "<Keyboard>/y");
            yield return D("autoTurret", BindSlot.Pad, "<Gamepad>/dpad/down");
            yield return D("unlockTarget", BindSlot.Key1, "<Keyboard>/u");
            yield return D("unlockTarget", BindSlot.Pad, "<Gamepad>/buttonEast");
            yield return D("action", BindSlot.Key1, "<Keyboard>/f");
            yield return D("action", BindSlot.Key2, "<Keyboard>/enter");
            yield return D("action", BindSlot.Pad, "<Gamepad>/buttonWest");
            yield return D("autopilotMenu", BindSlot.Key1, "<Keyboard>/q");
            yield return D("autopilotMenu", BindSlot.Pad, "<Gamepad>/select");
            yield return D("actionsMenu", BindSlot.Key1, "<Keyboard>/e");
            yield return D("actionsMenu", BindSlot.Pad, "<Gamepad>/dpad/left");
            yield return D("fastForward", BindSlot.Key1, "<Keyboard>/tab");
            yield return D("fastForward", BindSlot.Pad, "<Gamepad>/buttonNorth");
            yield return D("wingmen", BindSlot.Key1, "<Keyboard>/v");
            yield return D("khadorDrive", BindSlot.Key1, "<Keyboard>/k");
            yield return D("cloak", BindSlot.Key1, "<Keyboard>/c");
            yield return D("cloak", BindSlot.Pad, "<Gamepad>/rightStickPress");
            yield return D("timeExtender", BindSlot.Key1, "<Keyboard>/x");
            yield return D("timeExtender", BindSlot.Pad, "<Gamepad>/leftStickPress");
            yield return D("mouseSteering", BindSlot.Key1, "<Keyboard>/m");
            yield return D("mouseSteering", BindSlot.Key2, "<Mouse>/middleButton");
            yield return D("chat", BindSlot.Key1, "<Keyboard>/b");
            yield return D("chatSend", BindSlot.Key1, "<Keyboard>/enter");
            yield return D("chatSend", BindSlot.Key2, "<Keyboard>/numpadEnter");
            yield return D("chatChannel", BindSlot.Key1, "<Keyboard>/tab");
            yield return D("screenshot", BindSlot.Key1, "<Keyboard>/f12");
            yield return D("hudToggle", BindSlot.Key1, "<Keyboard>/f1");
            yield return D("hudToggle", BindSlot.Pad, "");
            yield return D("multiplayerWindow", BindSlot.Key1, "<Keyboard>/n");
            yield return D("distressCall", BindSlot.Key1, "");
            yield return D("distressCall", BindSlot.Key2, "");
            yield return D("distressCall", BindSlot.Pad, "");
        }

        [TestCaseSource(nameof(Defaults_))]
        public void DefaultBinding(string row, BindSlot slot, string[] paths)
        {
            var got = GameControls.Paths(DataUiTestKit.Row(row), slot);
            Assert.AreEqual(paths.Length, got.Length, $"{row} {slot}: parts");
            for (int i = 0; i < paths.Length; i++)
                Assert.AreEqual(paths[i].ToLowerInvariant(), got[i].ToLowerInvariant(), $"{row} {slot} part {i}");
        }

        [Test]
        public void UnboundSlotsAreEmptyAndBoundOnesAreText()
        {
            Assert.IsFalse(GameControls.IsBound(DataUiTestKit.Row("distressCall"), BindSlot.Key1), "the distress call: unbound");
            Assert.AreEqual("", GameControls.SlotText(DataUiTestKit.Row("distressCall"), BindSlot.Key1));
            Assert.IsFalse(GameControls.IsBound(DataUiTestKit.Row("roll"), BindSlot.Pad), "roll: no controller default");
            Assert.AreEqual("↑ ↓ ← →", GameControls.SlotText(DataUiTestKit.Row("steer"), BindSlot.Key1));
            Assert.AreEqual("RB LB", GameControls.SlotText(DataUiTestKit.Row("throttle"), BindSlot.Pad));
            Assert.AreEqual("LS", GameControls.SlotText(DataUiTestKit.Row("steer"), BindSlot.Pad));
            Assert.AreEqual("LMB", GameControls.SlotText(DataUiTestKit.Row("firePrimary"), BindSlot.Key2));
            CollectionAssert.IsEmpty(GameControls.Paths(DataUiTestKit.Row("chatSend"), BindSlot.Pad), "no controller slot");
        }

        [TestCase("<Gamepad>/buttonSouth", "A")]
        [TestCase("<Gamepad>/buttonEast", "B")]
        [TestCase("<Gamepad>/buttonWest", "X")]
        [TestCase("<Gamepad>/buttonNorth", "Y")]
        [TestCase("<Gamepad>/leftShoulder", "LB")]
        [TestCase("<Gamepad>/rightShoulder", "RB")]
        [TestCase("<Gamepad>/leftTrigger", "LT")]
        [TestCase("<Gamepad>/rightTrigger", "RT")]
        [TestCase("<Gamepad>/leftStick", "LS")]
        [TestCase("<Gamepad>/rightStickPress", "RS")]
        [TestCase("<Gamepad>/rightStick/left", "RS ←")]
        [TestCase("<Gamepad>/leftStick/up", "LS ↑")]
        [TestCase("<Gamepad>/dpad", "D-PAD")]
        [TestCase("<Gamepad>/dpad/left", "D-PAD ←")]
        [TestCase("<Gamepad>/start", "MENU")]
        [TestCase("<Gamepad>/select", "VIEW")]
        [TestCase("<Mouse>/leftButton", "LMB")]
        [TestCase("<Mouse>/rightButton", "RMB")]
        [TestCase("<Mouse>/middleButton", "MMB")]
        [TestCase("<Mouse>/backButton", "MB4")]
        [TestCase("<Mouse>/forwardButton", "MB5")]
        [TestCase("<Keyboard>/upArrow", "↑")]
        [TestCase("<Keyboard>/leftArrow", "←")]
        [TestCase("<Keyboard>/space", "SPACE")]
        [TestCase("<Keyboard>/enter", "ENTER")]
        [TestCase("<Keyboard>/escape", "ESC")]
        [TestCase("<Keyboard>/tab", "TAB")]
        [TestCase("<Keyboard>/leftShift", "L-SHIFT")]
        [TestCase("<Keyboard>/rightCtrl", "R-CTRL")]
        [TestCase("", "")]
        public void ShortNames(string path, string expected)
        {
            Assert.AreEqual(expected, GameControls.ShortName(path));
        }

        [Test]
        public void ShortNamesAreCaseInsensitive()
        {
            Assert.AreEqual("A", GameControls.ShortName("<gamepad>/BUTTONSOUTH"));
            Assert.AreEqual("TAB", GameControls.ShortName("<KEYBOARD>/Tab"));
        }

        [Test]
        public void KeyTextNamesTheFirstBoundSlot()
        {
            Assert.AreEqual("SPACE", DataUiTestKit.KeyText("FirePrimary", false));
            Assert.AreEqual("RT", DataUiTestKit.KeyText("FirePrimary", true));
            Assert.AreEqual("TAB", DataUiTestKit.KeyText("FastForward", false));
            Assert.AreEqual("", DataUiTestKit.KeyText("Wingmen", true), "no controller default");
            Assert.AreEqual("", DataUiTestKit.KeyText("ChatSend", true), "no controller slot");
            // the first keyboard slot unbound: the second
            GameControls.Clear(DataUiTestKit.Row("firePrimary"), BindSlot.Key1);
            Assert.AreEqual("LMB", DataUiTestKit.KeyText("FirePrimary", false));
        }

        [Serializable] class SavedOverride { public string action; public int index; public string path; }
        [Serializable] class SavedOverrides { public int version; public List<SavedOverride> bindings; }

        [Test]
        public void ClearingASlotSavesAnEmptyOverride()
        {
            var boost = DataUiTestKit.Row("boost");
            GameControls.Clear(boost, BindSlot.Key1);
            Assert.IsFalse(GameControls.IsBound(boost, BindSlot.Key1));
            Assert.IsTrue(GameControls.IsBound(boost, BindSlot.Pad), "the other slots stay");
            var saved = JsonUtility.FromJson<SavedOverrides>(PlayerPrefs.GetString(DataUiTestKit.ControlsPrefsKey, ""));
            Assert.IsNotNull(saved);
            Assert.AreEqual(2, saved.version);
            Assert.AreEqual(1, saved.bindings.Count);
            Assert.AreEqual("boost", saved.bindings[0].action);
            Assert.AreEqual(boost.slots[0][0], saved.bindings[0].index);
            Assert.AreEqual("", saved.bindings[0].path, "an empty override unbinds");
        }

        [Test]
        public void ClearingACompositeSlotUnbindsEveryPart()
        {
            var steer = DataUiTestKit.Row("steer");
            GameControls.Clear(steer, BindSlot.Key1);
            CollectionAssert.AreEqual(new[] { "", "", "", "" }, GameControls.Paths(steer, BindSlot.Key1));
            var saved = JsonUtility.FromJson<SavedOverrides>(PlayerPrefs.GetString(DataUiTestKit.ControlsPrefsKey, ""));
            Assert.AreEqual(4, saved.bindings.Count);
            Assert.IsTrue(saved.bindings.All(b => b.action == "steer"));
        }

        [Test]
        public void SavedOverridesLoadByActionAndIndex()
        {
            var boost = DataUiTestKit.Row("boost");
            var fire = DataUiTestKit.Row("firePrimary");
            string json = JsonUtility.ToJson(new SavedOverrides
            {
                version = 2,
                bindings = new List<SavedOverride>
                {
                    new SavedOverride { action = "boost", index = boost.slots[0][0], path = "<Keyboard>/j" },
                    new SavedOverride { action = "firePrimary", index = fire.slots[2][0], path = "" },
                },
            });
            PlayerPrefs.SetString(DataUiTestKit.ControlsPrefsKey, json);
            DataUiTestKit.ReloadBindings();
            Assert.AreEqual("<keyboard>/j", GameControls.Paths(boost, BindSlot.Key1)[0].ToLowerInvariant());
            Assert.IsFalse(GameControls.IsBound(fire, BindSlot.Pad));
            Assert.AreEqual("<keyboard>/space", GameControls.Paths(fire, BindSlot.Key1)[0].ToLowerInvariant(), "untouched");
        }

        [Test]
        public void BadOverridesAreIgnored()
        {
            var steer = DataUiTestKit.Row("steer");
            string json = JsonUtility.ToJson(new SavedOverrides
            {
                version = 2,
                bindings = new List<SavedOverride>
                {
                    new SavedOverride { action = "noSuchAction", index = 0, path = "<Keyboard>/j" },
                    new SavedOverride { action = "boost", index = 99, path = "<Keyboard>/j" },
                    new SavedOverride { action = "boost", index = -1, path = "<Keyboard>/j" },
                    new SavedOverride { action = "steer", index = steer.slots[0][0] - 1, path = "<Keyboard>/j" },   // the composite itself
                },
            });
            PlayerPrefs.SetString(DataUiTestKit.ControlsPrefsKey, json);
            Assert.DoesNotThrow(DataUiTestKit.ReloadBindings);
            Assert.AreEqual("<keyboard>/w", GameControls.Paths(DataUiTestKit.Row("boost"), BindSlot.Key1)[0].ToLowerInvariant());
            Assert.AreEqual("↑ ↓ ← →", GameControls.SlotText(steer, BindSlot.Key1));
        }

        [Test]
        public void OldOrBrokenOverrideFilesAreIgnored()
        {
            // Version 1 was the Input System's own JSON (binding ids that no longer exist).
            PlayerPrefs.SetString(DataUiTestKit.ControlsPrefsKey, "{\"version\":1,\"bindings\":[{\"action\":\"boost\",\"index\":0,\"path\":\"<Keyboard>/j\"}]}");
            DataUiTestKit.ReloadBindings();
            Assert.AreEqual("<keyboard>/w", GameControls.Paths(DataUiTestKit.Row("boost"), BindSlot.Key1)[0].ToLowerInvariant());
            PlayerPrefs.SetString(DataUiTestKit.ControlsPrefsKey, "not json at all");
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("GameControls: bindings not loaded"));
            DataUiTestKit.ReloadBindings();
            Assert.AreEqual("<keyboard>/w", GameControls.Paths(DataUiTestKit.Row("boost"), BindSlot.Key1)[0].ToLowerInvariant());
        }

        [Test]
        public void ResetToDefaultsDropsEveryOverride()
        {
            GameControls.Clear(DataUiTestKit.Row("boost"), BindSlot.Key1);
            GameControls.Clear(DataUiTestKit.Row("steer"), BindSlot.Pad);
            Assert.IsTrue(PlayerPrefs.HasKey(DataUiTestKit.ControlsPrefsKey));
            GameControls.ResetToDefaults();
            Assert.IsFalse(PlayerPrefs.HasKey(DataUiTestKit.ControlsPrefsKey));
            Assert.IsTrue(GameControls.IsBound(DataUiTestKit.Row("boost"), BindSlot.Key1));
            Assert.AreEqual("LS", GameControls.SlotText(DataUiTestKit.Row("steer"), BindSlot.Pad));
        }

        [Test]
        public void ChangedFiresOnClearAndReset()
        {
            int fired = 0;
            void Count() => fired++;
            GameControls.Changed += Count;
            try
            {
                GameControls.Clear(DataUiTestKit.Row("boost"), BindSlot.Key1);
                Assert.AreEqual(1, fired);
                GameControls.ResetToDefaults();
                Assert.AreEqual(2, fired);
            }
            finally { GameControls.Changed -= Count; }
        }
    }
}
