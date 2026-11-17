// Exercise the real Input System -> EventSystem -> UI Toolkit path. Calling a menu's private handlers with fabricated
// pressedButtons values cannot catch a bridge that loses the held button between a press and subsequent movement.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.UI;
using GoF2Remake.Data;
using GoF2Remake.Multiplayer;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace GoF2Remake.Tests
{
    public class KaamoTabInputTests : PlayModeTest
    {
        Mouse mouse;
        Keyboard keyboard;
        readonly List<InputDevice> switchedOff = new List<InputDevice>();   // the real pointers, off during a test
        InputSettings.BackgroundBehavior background;
        InputSettings.EditorInputBehaviorInPlayMode editorInput;

        [SetUp]
        public void UseTestMouse()
        {
            background = InputSystem.settings.backgroundBehavior;
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            mouse = InputSystem.AddDevice<Mouse>();
            keyboard = InputSystem.AddDevice<Keyboard>();
            // The real mouse, pen and touch screen are switched off meanwhile: these frames go to the Game view with the
            // window's focus ignored, so pressing the physical mouse during a run took Mouse.current and broke the test.
            // (Not InputTestFixture: it swaps the whole input runtime out under the scene these tests load; this keeps the
            // path as the game runs it.)
            switchedOff.Clear();
            foreach (var d in InputSystem.devices)
                if (d != mouse && d != keyboard && d.enabled && (d is Pointer || d is Keyboard)) { InputSystem.DisableDevice(d); switchedOff.Add(d); }
            mouse.MakeCurrent();
            keyboard.MakeCurrent();
        }

        [TearDown]
        public void RestoreInput()
        {
            PadKeyboard.Current?.Close(false);
            NetChat.SetTyping(false);
            foreach (var d in switchedOff)
                if (d.added) InputSystem.EnableDevice(d);
            switchedOff.Clear();
            InputSystem.RemoveDevice(mouse);
            InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.backgroundBehavior = background;
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
        }

        static Vector2 ScreenPoint(VisualElement target)
        {
            var origin = RuntimePanelUtils.ScreenToPanel(target.panel, Vector2.zero);
            var end = RuntimePanelUtils.ScreenToPanel(target.panel, new Vector2(Screen.width, Screen.height));
            var p = target.worldBound.center;
            return new Vector2((p.x - origin.x) / (end.x - origin.x) * Screen.width,
                Screen.height - (p.y - origin.y) / (end.y - origin.y) * Screen.height);
        }

        void MouseAt(Vector2 point, bool held)
        {
            var state = new MouseState { position = point };
            if (held) state = state.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left);
            InputSystem.QueueStateEvent(mouse, state);
        }

        IEnumerator StationReady()
        {
            FreePlayAt(78);
            yield return Load("Station");
            var menu = Find<StationMenu>();
            yield return WaitFor(() => !Get<bool>(menu, "UiBlocked") && !Get<bool>(menu, "ArrivalPending"), 15f, "station controls");
        }

        IEnumerator TapKey(Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            yield return null;
            yield return null;
        }

        IEnumerator ClickElement(VisualElement target)
        {
            var point = ScreenPoint(target);
            MouseAt(point, false);
            yield return null;
            yield return null;
            yield return null;
            MouseAt(point, true);
            // Production mouse debounce is 30 ms; batch frames need not take that long (#301).
            yield return new WaitForSecondsRealtime(0.05f);
            MouseAt(point, false);
            yield return WaitFor(() => !mouse.leftButton.isPressed, 0.25f, "mouse release after debounce");
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator KaamoNumericTabsFollowVisibleOrder()
        {
            using (new SettingsScope<int>(() => Settings.KaamoVipMode, v => Settings.KaamoVipMode = v))
            {
                Settings.KaamoVipMode = Settings.KaamoVipAlways;
                FreePlayAt(KaamoClub.Station);
                Session.KaamoState = 3;
                yield return Load("Station");
                var menu = Find<StationMenu>();
                yield return WaitFor(() => !Get<bool>(menu, "UiBlocked") && !Get<bool>(menu, "ArrivalPending"), 15f, "Kaamo controls");
                Invoke(menu, "OpenHangar");
                var hangar = Get<HangarWindow>(menu, "hangarWindow");
                yield return TapKey(Key.Digit3);
                Assert.AreEqual(HangarWindow.Tab.Ships, Get<HangarWindow.Tab>(hangar, "tab"));
                yield return TapKey(Key.Digit4);
                Assert.AreEqual(HangarWindow.Tab.Blueprints, Get<HangarWindow.Tab>(hangar, "tab"));
            }
        }

        [UnityTest]
        public IEnumerator OrdinaryHangarKeepsBaseNumericTabs()
        {
            yield return StationReady();
            var menu = Find<StationMenu>();
            Invoke(menu, "OpenHangar");
            var hangar = Get<HangarWindow>(menu, "hangarWindow");
            yield return TapKey(Key.Digit1);
            Assert.AreEqual(HangarWindow.Tab.Ship, Get<HangarWindow.Tab>(hangar, "tab"));
            yield return TapKey(Key.Digit2);
            Assert.AreEqual(HangarWindow.Tab.Shop, Get<HangarWindow.Tab>(hangar, "tab"));
            yield return TapKey(Key.Digit3);
            Assert.AreEqual(HangarWindow.Tab.Blueprints, Get<HangarWindow.Tab>(hangar, "tab"));
            yield return TapKey(Key.Digit4);
            Assert.AreEqual(HangarWindow.Tab.Blueprints, Get<HangarWindow.Tab>(hangar, "tab"), "4 is inactive in ordinary hangars");
        }

    }
}
