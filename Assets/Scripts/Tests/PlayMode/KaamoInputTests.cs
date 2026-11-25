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
    public class KaamoInputTests : PlayModeTest
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
        public IEnumerator KaamoSearchRequiresExplicitEntryAndReturnsHangarKeys([Values(false, true)] bool originalUi)
        {
            using (new SettingsScope<bool>(() => Settings.OriginalUi, v => Settings.OriginalUi = v))
            using (new SettingsScope<int>(() => Settings.KaamoVipMode, v => Settings.KaamoVipMode = v))
            {
                Settings.OriginalUi = originalUi;
                Settings.KaamoVipMode = Settings.KaamoVipCard;
                FreePlayAt(KaamoClub.Station);
                Session.KaamoState = 3;
                Session.KaamoItems.Add(new ItemStack(109, 5));
                yield return Load("Station");
                var menu = Find<StationMenu>();
                yield return WaitFor(() => !Get<bool>(menu, "UiBlocked") && !Get<bool>(menu, "ArrivalPending"), 15f, "Kaamo controls");
                Invoke(menu, "OpenHangar");
                var hangar = Get<HangarWindow>(menu, "hangarWindow");
                hangar.SetTab(HangarWindow.Tab.Shop);
                for (int i = 0; i < 3; i++) yield return null;
                var field = Get<TextField>(hangar, "searchField");
                Assert.IsFalse(hangar.Searching);
                Assert.IsTrue(field.isReadOnly);
                MouseAt(ScreenPoint(field), false);
                yield return null;
                Assert.IsFalse(hangar.Searching, "hover alone cannot activate typing");
                field.Focus();
                yield return null;
                Assert.IsFalse(hangar.Searching, "incidental focus cannot activate typing");
                var itemRow = Get<System.Collections.IList>(hangar, "rows").Cast<object>()
                    .First(r => Get<int>(r, "item") == 109 && Get<object>(r, "kind").ToString() == "ShopItem");
                Invoke(hangar, "Select", itemRow, false);
                int cargo = Shop.CargoOf(109);
                yield return TapKey(Key.D);
                Assert.AreEqual(cargo + 1, Shop.CargoOf(109), "D takes an item before search is activated");
                yield return TapKey(Key.A);
                Assert.AreEqual(cargo, Shop.CargoOf(109), "A stores it again");
                Assert.AreEqual("", field.value, "transfer keys never enter an inactive search");
                Assert.IsNotNull(NetChat.Keys);

                // A new-game medal toast can overlap the search field; dismiss it before testing the field's click.
                Get<Queue<int>>(menu, "medalQueue").Clear();
                Invoke(menu, "ShowNextMedal");
                yield return null;
                yield return ClickElement(field);
                Assert.IsTrue(hangar.Searching, "primary click explicitly activates entry");
                // Runtime UI Toolkit consumes native text events rather than synthetic Keyboard.onTextInput.
                // Send a text key through UI Toolkit after the real mouse click has established text focus.
                var textInput = field.Query<TextElement>().ToList().First(v => v.focusable);
                using (var key = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, character = 'a', keyCode = KeyCode.A }))
                    textInput.SendEvent(key);
                yield return null;
                yield return null;
                Assert.AreEqual("a", field.value);
                using (var key = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, character = '\n', keyCode = KeyCode.Return }))
                    textInput.SendEvent(key);
                yield return TapKey(Key.Enter);
                Assert.IsFalse(hangar.Searching);
                Assert.IsTrue(field.isReadOnly);
                Assert.IsNotNull(NetChat.Keys);

                yield return TapKey(Key.F);
                yield return null;
                Assert.IsTrue(hangar.Searching, "Search shortcut remains available");
                using (var key = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape }))
                    textInput.SendEvent(key);
                yield return TapKey(Key.Escape);
                Assert.IsFalse(hangar.Searching);
                Assert.IsNotNull(NetChat.Keys);
                hangar.FocusSearch(false);
                yield return null;
                var sort = Get<Button>(hangar, "sortButton");
                yield return ClickElement(sort);
                yield return WaitFor(() => !hangar.Searching, 1f, "clicking elsewhere ends entry");
                hangar.FocusSearch(false);
                yield return null;
                hangar.SetTab(HangarWindow.Tab.Ships);
                yield return null;
                Assert.IsFalse(hangar.Searching, "switching tabs ends entry");
                Assert.IsTrue(field.isReadOnly);
                hangar.Close();
                hangar.Open();
                hangar.SetTab(HangarWindow.Tab.Shop);
                yield return null;
                Assert.IsFalse(hangar.Searching, "reopening starts inactive");
                hangar.FocusSearch(true);
                Assert.IsNotNull(PadKeyboard.Current, "explicit controller Search opens its keyboard");
                PadKeyboard.Current.Close(false);
            }
        }



    }
}
