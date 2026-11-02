// StationMenuInput.cs
// Part of StationMenu: input: dragging the hangar ship, input modes, focus and hover, keyboard / controller navigation
// and the hint row.

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using PointerType = UnityEngine.UIElements.PointerType;

namespace GoF2Remake.UI
{
    public partial class StationMenu
    {
        void HookDrag()
        {
            dragZone.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || level == null || dragPointer >= 0) return;
                if (level.View == StationView.Lounge) { level.SkipIntro(); return; }   // OnTouchEnd case 0: jump to B
                dragPointer = e.pointerId;
                dragZone.CapturePointer(e.pointerId);
                dragLastX = e.position.x;
                dragLastTime = Time.unscaledTime;
                dragVelocity = dragFrameRadians = 0f;
                level.RotateShip(0f);   // stops a fling
            });
            dragZone.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerId != dragPointer) return;
                // Lost release: stop without turning an unrelated hover into a fling (even if another button is held).
                if (!UiPointerButtons.PrimaryHeld(e)) { EndDrag(e.pointerId, false); return; }
                float rad = (e.position.x - dragLastX) / UnitsPerRadian;
                level.RotateShip(rad);
                dragFrameRadians += rad;
                dragLastX = e.position.x;
                dragLastTime = Time.unscaledTime;
            });
            dragZone.RegisterCallback<PointerUpEvent>(e => EndDrag(e.pointerId, true));
            dragZone.RegisterCallback<PointerCancelEvent>(e => EndDrag(e.pointerId, false));
            // The zone hidden mid-drag (the map, a window) loses the capture and never hears the release.
            dragZone.RegisterCallback<PointerCaptureOutEvent>(e => EndDrag(e.pointerId, false));
            dragZone.RegisterCallback<DetachFromPanelEvent>(_ => EndDrag(dragPointer, false));
        }

        void EndDrag(int pointerId, bool fling)
        {
            if (pointerId != dragPointer || pointerId < 0) return;
            dragPointer = -1;   // first: releasing the capture sends PointerCaptureOut back here
            if (dragZone.HasPointerCapture(pointerId)) dragZone.ReleasePointer(pointerId);
            // OnTouchEnd: fling only if the finger was still moving (the original: last dx > 3 px).
            bool moving = Time.unscaledTime - dragLastTime < 0.08f;
            float minSpeed = 3f / StationTables.TurntablePixelsPerRadian / 0.02f;
            if (fling && moving && Mathf.Abs(dragVelocity) > minSpeed) level.FlingShip(Mathf.Clamp(dragVelocity, -MaxFling, MaxFling));
        }

        // ---- input mode, focus and navigation -------------------------------------------------------------

        void ApplyInputMode()
        {
            if (root == null) return;
            var kind = InputMode.Current;
            root.EnableInClassList("input-touch", kind == InputKind.Touch);
            root.EnableInClassList("input-keyboard", kind == InputKind.KeyboardMouse);
            root.EnableInClassList("input-gamepad", kind == InputKind.Gamepad);
            SetTouchMode(kind == InputKind.Touch);
            if (kind == InputKind.Gamepad && !HangarOpen && root.focusController?.focusedElement == null) Select(DefaultFocus());
            BuildHints(kind);
            BuildInspectHints(kind);
        }

        // Touch has no hover and shouldn't leave a focus highlight on what the finger pressed (see MainMenu).
        void SetTouchMode(bool on)
        {
            touchMode = on;
            SyncHover();
            if (on && root.focusController?.focusedElement is VisualElement focused) focused.Blur();
        }

        /// <summary>Hover highlights only while the mouse is the thing in use (InputMode.MousePointing), not under a resting
        /// cursor while the keys or a controller move the selection.</summary>
        void SyncHover() => root?.EnableInClassList("can-hover", !touchMode && InputMode.MousePointing);

        void OnPointerDown(PointerDownEvent e)
        {
            if (UiHover.SilentPress(e.target)) pressFrame = Time.frameCount;   // Modified: see Play
            if (e.pointerType == PointerType.mouse) { pointerPos = e.position; SetTouchMode(false); pointerDriven = true; return; }
            // Don't focus what the finger presses, except a text field (the chat line, the multiplayer window's codes, amounts
            // and names): it needs the focus for the on-screen keyboard (MainMenu does the same).
            if (TextFieldKeys.InTextField(e.target)) { touchMode = true; root.EnableInClassList("can-hover", false); return; }
            SetTouchMode(true);
            root.focusController?.IgnoreEvent(e);
        }

        /// <summary>What a controller starts on: the dialog's Yes, the system menu's first entry, else the view's button.</summary>
        VisualElement DefaultFocus() =>
            DialogOpen ? dialogYes : SystemMenuOpen ? SystemMenuItems()[0] : level != null && level.View == StationView.Lounge ? loungeButton : hangarButton;

        void Select(VisualElement e)
        {
            if (touchMode) return;
            // Modified: with the mouse in use a menu or window opens with nothing marked (its first button, the Missions
            // window's Show on map, looked hovered); the keys and a controller still get their selection (KeysTakeOver).
            if (pointerDriven && InputMode.Current != InputKind.Gamepad)
            {
                if (root?.focusController?.focusedElement is Button b) b.Blur();
                return;
            }
            e?.Focus();
        }

        /// <summary>Modified: with the mouse in use, a button keeps its marked look only while the mouse is over it (it stayed
        /// marked after the mouse left).</summary>
        void UnmarkLeftButton()
        {
            if (!pointerDriven || InputMode.Current == InputKind.Gamepad || root?.panel == null) return;
            // Any marked item, not only buttons (the Debug rows: the item grids' amber frame, a toggle); not a text field.
            if (!(root.focusController?.focusedElement is VisualElement b) || UiHover.KeepFocus(b)) return;
            var picked = root.panel.Pick(pointerPos);
            if (picked == null || !(picked == b || b.Contains(picked))) b.Blur();
        }

        /// <summary>The title's "| view" after the station's name: the hangar 167, the lounge 398. Modified: also the window
        /// open from the main view: Missions 129, Status 169 (followed every frame: the windows open and close from several
        /// places). The map shows it in its own header (StarMap.SetStationTitle: here it flashed for a frame).</summary>
        void RefreshViewTitle()
        {
            if (viewTitle == null || level == null) return;
            string t = HangarOpen ? Localization.Get(167)
                : missions != null && missions.IsOpen ? Localization.Get(129)
                : status != null && status.IsOpen ? Localization.Get(169)
                : level.View == StationView.Lounge ? Localization.Get(398) : "";
            t = t.ToUpperInvariant();
            if (viewTitle.text == t) return;
            viewTitle.text = t;
            viewTitle.style.display = t.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Modified: what the keys' move sound is for: a button, a toggle, a slider, a choice (options, Debug).</summary>
        static bool IsHoverable(VisualElement v) => UiHover.IsHoverable(v);

        /// <summary>Modified: the mouse onto a button (MainMenu.HookFocusSound's push, softer).</summary>
        void PlayHover()
        {
            if (sfxSource != null && buttonPush != null) sfxSource.PlayOneShot(GoF2Remake.Modding.ModSounds.Get(buttonPush), Settings.SfxVolume * 0.45f);
        }

        /// <summary>Modified: an arrow / Tab after the mouse: the keys take over, and with nothing marked the view's first item is
        /// (the panel sends no navigation without a focused element).</summary>
        void KeysTakeOver()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (!pointerDriven || kb == null || HangarOpen) return;
            if (!(kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame
                  || kb.rightArrowKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame)) return;
            if (GoF2Remake.Multiplayer.NetChat.Typing) return;
            pointerDriven = false;
            if (root.focusController?.focusedElement != null) return;
            if (missions != null && missions.IsOpen) Select(missions.NavItems()[0]);
            else if (lounge != null && lounge.ChatOpen) lounge.FocusFirst();
            else Select(DefaultFocus());
        }

        /// <summary>A button the keys / controller can move to: enabled (the story locks grey some out) and shown.</summary>
        static bool Navigable(VisualElement v) => v != null && v.enabledInHierarchy && v.canGrabFocus && v.resolvedStyle.display != DisplayStyle.None;

        /// <summary>The next navigable item from index i in the step's direction (disabled ones are jumped over); from nothing
        /// focused the first navigable one. Null = none that way (the selection stays).</summary>
        static VisualElement NextNavigable(VisualElement[] items, int i, int step)
        {
            if (i < 0) step = 1;
            for (int k = i < 0 ? 0 : i + step; k >= 0 && k < items.Length; k += step)
                if (Navigable(items[k])) return items[k];
            return null;
        }

        /// <summary>Up/down walks the screen buttons and launch, jumping over the locked ones (left/right: yes/no in the
        /// dialog). On the main view (and with the lounge's visitors) a controller's right goes to Launch and left from
        /// Launch back to the button selected before (the ship turns with the right stick); on the keyboard left / right
        /// stay the ship's turning keys (A / D and the arrows).</summary>
        void OnNavigate(NavigationMoveEvent e)
        {
            if (GoF2Remake.Multiplayer.NetChat.Typing) return;   // the arrows move the chat line's cursor
            // A letter typed into a text field (the UI's Navigate takes W A S D too): the field keeps it and its focus. The
            // Mod Builder's fields sit in this root, and an S in a name moved the focus to the menu (a user's report).
            if (TextFieldKeys.IsTyping(e, root.focusController?.focusedElement as VisualElement))
            {
                e.StopPropagation();
                root.focusController?.IgnoreEvent(e);
                return;
            }
            // The multiplayer window walks its own tabs and buttons (MultiplayerWindow.OnNavigate), as Update yields its
            // keys to it: without this the arrows moved the menu behind it and its Squad tab could never be reached.
            // A navigation event only travels through what has the focus, so opened with the mouse (the focus still on a
            // menu button behind it) the first press steps into it.
            if (MultiplayerWindow.IsOpenAny)
            {
                if (MultiplayerWindow.FocusInside) return;
                MultiplayerWindow.FocusFirstAny();
                e.StopPropagation();
                root.focusController?.IgnoreEvent(e);
                return;
            }
            SetTouchMode(false);
            pointerDriven = false;
            bool vertical = e.direction == NavigationMoveEvent.Direction.Up || e.direction == NavigationMoveEvent.Direction.Down;
            bool horizontal = e.direction == NavigationMoveEvent.Direction.Left || e.direction == NavigationMoveEvent.Direction.Right;
            if (HangarOpen && !DialogOpen)
            {
                // Read in Update (HoldDirections): navigation events only arrive while a UI element has focus.
                e.StopPropagation();
                root.focusController?.IgnoreEvent(e);
                return;
            }
            var stationItems = new VisualElement[] { hangarButton, loungeButton, mapButton, missionsButton, statusButton, inspectButton, customizeButton, launchButton };
            VisualElement[] items;
            // A squad invitation / trade request (SquadView): its Accept and Decline, like a dialog's Yes and No.
            bool squadPopup = !DialogOpen && SquadView.PopupOpen;
            if (DialogOpen) items = new VisualElement[] { dialogYes, dialogNo };
            else if (squadPopup) items = SquadView.PopupButtons;
            else if (SystemMenuOpen) items = SystemMenuItems();
            else if (missions != null && missions.IsOpen) { items = missions.NavItems(); vertical |= horizontal; }
            else if (status != null && status.IsOpen) { items = status.NavItems(); vertical |= horizontal; }
            else if (lounge != null && lounge.ChatOpen) items = lounge.NavItems();
            else if (lounge != null && lounge.Active)
            {
                var l = new System.Collections.Generic.List<VisualElement>(lounge.NavItems());
                l.AddRange(stationItems);
                items = l.ToArray();
            }
            else items = stationItems;
            if (!DialogOpen && SystemMenuOpen && sysPage == SysPage.Options && optionsView != null && (horizontal || vertical))
            {
                // Like the main menu's Options: up / down walk the tab row, the tab's rows and the footer (stopping at the
                // ends, Default settings beside Back); left / right switch tabs on the tab row, step a row, or move
                // between Back and Default settings.
                var f = root.focusController?.focusedElement as VisualElement;
                var nav = optionsView.NavItems();
                int at = nav.IndexOf(f);
                if (horizontal)
                {
                    int dir = e.direction == NavigationMoveEvent.Direction.Left ? -1 : 1;
                    var row = optionsView.RowOf(f);
                    if (optionsView.IsTab(f)) optionsView.StepTab(dir);
                    else if (row != null) row.Step(dir);
                    else if (optionsView.IsFooter(f)) Select(optionsView.FooterStep(f, dir));   // Back / Default settings, or the prompt
                    else Select(optionsView.ActiveTab);
                }
                else
                {
                    int step = e.direction == NavigationMoveEvent.Direction.Up ? -1 : 1;
                    int next = at < 0 ? 0 : at + step;
                    // The footer is one row: down from it stays, up from any of its buttons goes to the item before it.
                    if (step > 0 && optionsView.IsFooter(f)) next = at;
                    if (step < 0 && optionsView.IsFooter(f)) next = optionsView.FooterStart(nav) - 1;
                    Select(nav[Mathf.Clamp(next, 0, nav.Count - 1)]);
                }
                e.StopPropagation();
                root.focusController?.IgnoreEvent(e);
                return;
            }
            if (!DialogOpen && SystemMenuOpen && DebugPage && (horizontal || vertical))
            {
                // Left / right step a value (a stepper, segments, a slider: OptionControl.Step, like the pause menu); else
                // the keys move on screen (SpatialNav): the toggles stand two to a row, the Give items buttons side by side
                // (a toggle flipped on left / right, and down went to the button beside).
                var f = root.focusController?.focusedElement as VisualElement;
                var c = stationOptions.Find(o => o.Field == f);
                // Modified: on the tab row left / right switch tabs (the new tab keeps the selection), down goes to the first row.
                if (f != null && f.ClassListContains("debug-tab"))
                {
                    if (horizontal)
                    {
                        SwitchDebugTab(e.direction == NavigationMoveEvent.Direction.Left ? -1 : 1);
                        Select(activeDebugTab);
                    }
                    else if (e.direction == NavigationMoveEvent.Direction.Down && stationOptions.Count > 0) Select(stationOptions[0].Field);
                    e.StopPropagation();
                    root.focusController?.IgnoreEvent(e);
                    return;
                }
                if (horizontal && c != null && c.StepsSideways) c.Step(e.direction == NavigationMoveEvent.Direction.Left ? -1 : 1);
                else if (vertical && CheatsCatalog.MoveVertical(f, e.direction == NavigationMoveEvent.Direction.Up ? -1 : 1)) { }
                else if (System.Array.IndexOf(items, f) < 0) NextNavigable(items, -1, 1)?.Focus();
                else
                {
                    int dx = !horizontal ? 0 : e.direction == NavigationMoveEvent.Direction.Left ? -1 : 1;
                    int dy = !vertical ? 0 : e.direction == NavigationMoveEvent.Direction.Up ? -1 : 1;
                    SpatialNav.Next(items, f, dx, dy, Navigable)?.Focus();
                }
                e.StopPropagation();
                root.focusController?.IgnoreEvent(e);
                return;
            }
            var focusedNow = root.focusController?.focusedElement as VisualElement;
            int index = System.Array.IndexOf(items, focusedNow);
            bool mainView = !DialogOpen && !squadPopup && !SystemMenuOpen && !(missions != null && missions.IsOpen) && !(status != null && status.IsOpen)
                            && !(lounge != null && lounge.ChatOpen);
            if (mainView && index >= 0 && focusedNow != launchButton) lastSideItem = focusedNow;
            if (mainView && horizontal && InputMode.Current == InputKind.Gamepad)
            {
                if (e.direction == NavigationMoveEvent.Direction.Right) { if (Navigable(launchButton)) launchButton.Focus(); }
                else if (focusedNow == launchButton)
                {
                    // Back to the button selected before Launch, or (locked meanwhile) the nearest one above it.
                    var back = Navigable(lastSideItem) && System.Array.IndexOf(items, lastSideItem) >= 0 ? lastSideItem
                                                                                                         : NextNavigable(items, index, -1);
                    back?.Focus();
                }
            }
            else if (DialogOpen || squadPopup ? horizontal : vertical)
            {
                int step = e.direction == NavigationMoveEvent.Direction.Up || e.direction == NavigationMoveEvent.Direction.Left ? -1 : 1;
                NextNavigable(items, index, step)?.Focus();
            }
            e.StopPropagation();
            root.focusController?.IgnoreEvent(e);
        }

        void BuildHints(InputKind kind)
        {
            if (hints == null) return;
            hints.Clear();
            string T(string key, string english) => Localization.Extra(key, english);
            // Modified: the system menu (MENU) shows no hints under it any more (its own Back, the keys as in every menu).
            if (SystemMenuOpen) return;
            if (HangarOpen)
            {
                bool store = hangarWindow != null && hangarWindow.StorageMode;   // the Kaamo Club's storage
                // Remake: a blueprint's ingredients take back / add (HangarWindow.EditingBlueprint rebuilds the hints).
                bool ingredients = hangarWindow != null && hangarWindow.EditingBlueprint;
                string select = T("hudSelect", "SELECT"), trade = ingredients ? $"{T("shopTake", "TAKE")} / {T("bpAdd", "ADD")}"
                                                                : store ? $"{T("shopStore", "STORE")} / {T("shopTake", "TAKE")}"
                                                                        : $"{T("shopSell", "SELL")} / {T("shopBuy", "BUY")}";
                string tabs = $"{Localization.Get(183)} / {Localization.Get(store ? 186 : 185)} / {Localization.Get(272)}".ToUpperInvariant(), confirm = T("hudConfirm", "CONFIRM");
                string sellShip = T("kaamoSellShip", "SELL SHIP");
                // Remake: the Ship tab's X moves a mounted item to its next slot (HangarWindow.SecondaryAction).
                bool shipTab = hangarWindow != null && hangarWindow.ShipTab;
                string move = T("shopMoveSlot", "MOVE");
                string info = Localization.Get(390).ToUpperInvariant();   // the details window's "Info" (ItemInfoWindow)
                if (kind == InputKind.KeyboardMouse)
                {
                    Hint(select, InputGlyph.Key("W"), InputGlyph.Key("S"));
                    Hint(trade, InputGlyph.Key("A"), InputGlyph.Key("D"));
                    Hint(confirm, InputGlyph.Key("ENTER", true));
                    Hint(tabs, InputGlyph.Key("Q"), InputGlyph.Key("E"));
                    Hint(info, InputGlyph.Key("I"));
                    if (store) Hint(sellShip, InputGlyph.Key("X"));
                    if (shipTab) Hint(move, InputGlyph.Key("X"));
                    Hint(T("hudBack", "BACK"), InputGlyph.Key("ESC"));
                }
                else if (kind == InputKind.Gamepad)
                {
                    Hint($"{select} / {trade}", InputGlyph.Pad(PadButton.DPad));
                    Hint(confirm, InputGlyph.Pad(PadButton.A));
                    Hint(trade, InputGlyph.Pad(PadButton.X), InputGlyph.Pad(PadButton.A));   // a shop row: X sells, A buys
                    Hint(tabs, InputGlyph.Pad(PadButton.LeftBumper), InputGlyph.Pad(PadButton.RightBumper));
                    Hint(info, InputGlyph.Pad(PadButton.Y));
                    if (store) Hint(sellShip, InputGlyph.Pad(PadButton.X));
                    if (shipTab) Hint(move, InputGlyph.Pad(PadButton.X));
                    Hint(T("hudBack", "BACK"), InputGlyph.Pad(PadButton.B));
                }
                return;
            }
            // The main view shows no hints: the buttons cover hangar / lounge / map / launch / menu (their keys still work:
            // 1 / 2 / M / L, LB / RB / Y / X). Modified: the lounge's Esc / B Back hint is gone too: its Back button (styled
            // as the star map's) is the way back; Esc / B still work.
        }

        void Hint(string label, params VisualElement[] glyphs)
        {
            var h = new VisualElement { pickingMode = PickingMode.Ignore };
            h.AddToClassList("hint");
            // Modified: the glyphs in a row of their own (hint-keys), so the hangar window can put the label under them.
            var keys = new VisualElement { pickingMode = PickingMode.Ignore };
            keys.AddToClassList("hint-keys");
            foreach (var g in glyphs) keys.Add(g);
            h.Add(keys);
            var l = new Label(label) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("hint-label");
            l.AddToClassList("gof-semibold");
            h.Add(l);
            hints.Add(h);
        }
    }
}
