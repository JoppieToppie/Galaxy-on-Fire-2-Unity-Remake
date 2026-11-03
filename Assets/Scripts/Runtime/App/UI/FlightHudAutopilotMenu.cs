// FlightHudAutopilotMenu.cs
// FlightHud: the autopilot menu (Hud::initHudMenu(3)) (part of the partial FlightHud; its fields are in FlightHud.cs).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class FlightHud
    {
        void OpenAutopilotMenu(bool actions = false)
        {
            nav.OpenMenu(actions);
            if (!nav.MenuOpen) return;
            touch?.ReleaseAll();
            weapons?.SetPrimaryHeld(false);
            autopilotMenuItems.Clear();
            menuButtons.Clear();
            menuActions.Clear();
            root.Q<Label>("autopilotMenuTitle").text = Localization.Get(actions ? 172 : 571).UiCaps();   // Menu / Autopilot
            foreach (var t in nav.MenuEntries(actions))
            {
                var target = t;
                var b = new Button { text = MenuLabel(menuButtons.Count, t.name) };
                b.AddToClassList("autopilot-menu-item");
                b.AddToClassList("gof-semibold");
                b.focusable = false;
                b.clicked += () => ChooseMenuTarget(target);
                if (t.disabled) b.AddToClassList("autopilot-menu-item--disabled");
                autopilotMenuItems.Add(b);
                menuButtons.Add((b, target));
            }
            if (actions) AddEnergyCellCount();
            menuIndex = 0;
            lastMenuMove = 0;
            menuOpenedFrame = Time.frameCount;
            menuPointer = false;   // Modified: opened with a key / button: its first entry is highlighted
            autopilotMenu.EnableInClassList("autopilot-menu--actions", actions);   // Modified: the actions menu (E) on the right
            autopilotMenu.AddToClassList("autopilot-menu--shown");
            if (autopilotMenuScroll != null) autopilotMenuScroll.scrollOffset = Vector2.zero;
            HighlightMenu();
            BuildHints(InputMode.Current);
        }
        /// <summary>Remake: the menu no taller than the space between its top and the bottom of its container (16 units
        /// spare); its entries' ScrollView as tall as the entries within that and scrolls the rest. Modified: the ScrollView's
        /// height set from its entries; left to the layout it shrank to ~40 units, one entry and a half in view.</summary>
        void FitAutopilotMenu()
        {
            if (autopilotMenu?.parent == null) return;
            float room = autopilotMenu.parent.layout.height;
            if (float.IsNaN(room) || room <= 0f) return;
            float y = autopilotMenu.layout.y;
            if (float.IsNaN(y) || y < 0f) y = 0f;
            float max = Mathf.Max(120f, room - y - 16f);
            var current = autopilotMenu.style.maxHeight;
            if (current.keyword != StyleKeyword.Undefined || Mathf.Abs(current.value.value - max) >= 0.5f) autopilotMenu.style.maxHeight = max;
            if (autopilotMenuScroll == null || autopilotMenuItems == null) return;
            float entries = autopilotMenuItems.layout.height, chrome = autopilotMenu.layout.height - autopilotMenuScroll.layout.height;
            if (float.IsNaN(entries) || float.IsNaN(chrome)) return;
            float h = Mathf.Max(0f, Mathf.Min(entries, max - chrome));
            if (Mathf.Abs(autopilotMenuScroll.resolvedStyle.height - h) > 0.5f) autopilotMenuScroll.style.height = h;
        }
        /// <summary>Hud::drawMenu: under the quick menu (type 0), with a cloak or a jump drive on the ship, the energy cells in the
        /// hold (Hud+0x21c, cargo 122, counted by Hud::initHudMenu(0)) as the cell's icon and "X <count>": what the cloak and the
        /// drives pay with. Remake: shown when the menu holds the cloak, the Khador Drive or the slipstream drive. In the
        /// items' list, which the secondary and wingmen pages clear.</summary>
        void AddEnergyCellCount()
        {
            if (!menuButtons.Exists(m => m.target != null && (m.target.kind == Navigation.Kind.Cloak || m.target.kind == Navigation.Kind.KhadorDrive
                                                               || m.target.kind == Navigation.Kind.Slipstream))) return;
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.justifyContent = Justify.Center;
            row.style.marginTop = 10;
            var icon = ItemInfo.ItemIcon(GalaxyMap.EnergyCellItem);
            if (icon != null)
            {
                var image = new VisualElement { pickingMode = PickingMode.Ignore };
                image.style.width = 44;
                image.style.height = 44;
                image.style.marginRight = 8;
                image.style.backgroundImage = new StyleBackground(icon);
                row.Add(image);
            }
            var count = new Label("X " + Shop.CargoOf(GalaxyMap.EnergyCellItem)) { pickingMode = PickingMode.Ignore };
            count.AddToClassList("gof-semibold");
            count.style.color = Color.white;
            count.style.fontSize = 26;
            row.Add(count);
            autopilotMenuItems.Add(row);
        }
        /// <summary>An entry's text; with keys and mouse numbered "1." to "8." like the PC version's menus (its "Menu select 1-8"
        /// keys; the station menu's entries are numbered the same way).</summary>
        static string MenuLabel(int index, string text)
        {
            text = text.UiCaps();
            // Interface style Original: numbered with every input, as the original's menu windows.
            return (InputMode.Current == InputKind.KeyboardMouse || Settings.OriginalUi) && index < 8 ? $"{index + 1}. {text}" : text;
        }
        void CloseAutopilotMenu()
        {
            nav?.CloseMenu();
            HideAutopilotMenu();
        }
        /// <summary>Remake (CapitalShips): the resupply window of the carrier or the Vossk battleship (CarrierShopWindow, the
        /// hangar shop's look) while docked at it; the game waits under it like under a menu (Navigation.OpenMenu(force)).</summary>
        void OpenCarrierShop()
        {
            if (nav == null || level == null) return;
            nav.OpenMenu(false, true);
            if (!nav.MenuOpen) return;
            touch?.ReleaseAll();
            weapons?.SetPrimaryHeld(false);
            carrierShop ??= new CarrierShopWindow(safeArea ?? root, level.Database, health, PlayButton, PlayUi,
                                                  text => miningView?.ShowMessage(text), OnCarrierShopClosed) { HoverSound = PlayHover };
            carrierShop.FromMenu = dockedMenu != null && dockedMenu.IsOpen;
            carrierShop.Open(docking != null ? docking.Target : null);
            BuildHints(InputMode.Current);
            StartShopAmbience();
        }
        /// <summary>Inside the carrier: the station hangar's ambience under the window (it plays through the paused game).</summary>
        void StartShopAmbience()
        {
            if (shopAmbience != null && shopAmbience.isPlaying) return;   // Remake: already under the docked menu
            var atmo = CombatAudio.Load()?.hangarAtmo;
            if (atmo != null)
            {
                if (shopAmbience == null)
                {
                    shopAmbience = gameObject.AddComponent<AudioSource>();
                    shopAmbience.playOnAwake = false;
                    shopAmbience.loop = true;
                    shopAmbience.spatialBlend = 0f;
                    shopAmbience.ignoreListenerPause = true;
                }
                shopAmbience.clip = GoF2Remake.Modding.ModSounds.Get(atmo);
                shopAmbience.volume = Mathf.Min(1f, 0.331f * Sfx.EventGain) * Settings.SfxVolume;   // event 95's volume (CycleSound.Hangar)
                shopAmbience.Play();
            }
        }
        /// <summary>Leaving the resupply window undocks; the guns stay blocked until the controls come back
        /// (ObjectDocking.Release), so nothing is fired into the carrier's deck on the way out.</summary>
        void OnCarrierShopClosed()
        {
            // Remake: from the docked menu, back to it (its Undock undocks).
            if (dockedMenu != null && dockedMenu.IsOpen) { BuildHints(InputMode.Current); return; }
            if (shopAmbience != null) shopAmbience.Stop();
            nav?.CloseMenu();
            docking?.Undock();
            if (weapons != null) weapons.Blocked = true;
            BuildHints(InputMode.Current);
        }
        /// <summary>Remake: the docked menu in the station menu's look (DockedMenu) while docked at an object with options:
        /// the object's name, the carrier's Resupply (the resupply window, CarrierShopWindow) or the object's own options
        /// (ObjectDocking.Options: the Mining Plant's pages), Inspect ship (the photo tools, PauseMenu.OpenInspect), Undock.
        /// The game waits under it like under the resupply window (Navigation.OpenMenu(force); multiplayer runs on).</summary>
        void OpenDockedMenu(bool atCarrier, List<(string label, System.Action open)> options)
        {
            if (nav == null || level == null || docking == null || docking.Target == null) return;
            nav.OpenMenu(false, true);
            if (!nav.MenuOpen) return;
            touch?.ReleaseAll();
            weapons?.SetPrimaryHeld(false);
            dockedMenu ??= new DockedMenu(safeArea ?? root, PlayButton, PlayHover);
            var entries = new List<(string label, System.Action action)>();
            if (atCarrier) entries.Add((Localization.Extra("hudResupply", "Resupply"), OpenCarrierShop));
            if (options != null) foreach (var o in options) entries.Add((o.label, o.open));
            if (pauseMenu != null && pauseMenu.Photo != null) entries.Add((Localization.Extra("stationInspect", "INSPECT SHIP").OgCase(), () => pauseMenu.OpenInspect(level)));
            entries.Add((Localization.Extra("hudUndock", "UNDOCK").OgCase(), () => CloseDockedMenu(true)));
            string name = atCarrier ? CapitalShips.HostName(docking.Target) : docking.Target.Target != null ? docking.Target.Target.displayName : null;
            dockedMenu.Open(string.IsNullOrEmpty(name) ? Localization.Extra("hudDocked", "Docked") : name, entries);
            dockedSubShown = false;
            BuildHints(InputMode.Current);
            if (atCarrier) StartShopAmbience();
        }
        /// <summary>The docked menu closes: 'undock' from its Undock (the guns blocked on the way out, as from the resupply
        /// window), otherwise the docking ended another way and every window over it goes too.</summary>
        void CloseDockedMenu(bool undock)
        {
            dockedMenu?.Hide();
            carrierShop?.Hide();
            if (!undock && pauseMenu != null && pauseMenu.IsOpen) pauseMenu.Close();
            if (shopAmbience != null) shopAmbience.Stop();
            nav?.CloseMenu();
            if (undock)
            {
                docking?.Undock();
                if (weapons != null) weapons.Blocked = true;
            }
            BuildHints(InputMode.Current);
        }
        void HideAutopilotMenu()
        {
            autopilotMenu.RemoveFromClassList("autopilot-menu--shown");
            BuildHints(InputMode.Current);
        }
        void HighlightMenu()
        {
            bool keys = InputMode.Current != InputKind.Touch;
            // Modified: with the mouse only the entry under it (the keys go on from there).
            int under = menuPointer && menuHover != null ? menuButtons.FindIndex(m => m.button == menuHover) : -1;
            if (under >= 0) menuIndex = under;
            int marked = !keys ? -1 : menuPointer ? under : menuIndex;
            for (int i = 0; i < menuButtons.Count; i++) menuButtons[i].button.EnableInClassList("autopilot-menu-item--selected", i == marked);
            // Remake: the keys' / controller's highlight scrolled into view (not the mouse's: the list would move under it).
            if (autopilotMenuScroll != null && marked >= 0 && marked < menuButtons.Count && !menuPointer && !DragScroll.PointerActive)
            {
                var b = menuButtons[marked].button;
                autopilotMenuScroll.schedule.Execute(() => { if (b.panel != null && b.parent == autopilotMenuItems) autopilotMenuScroll.ScrollTo(b); });   // after the layout of fresh entries
            }
        }
        VisualElement MenuButtonAt(VisualElement t)
        {
            for (var v = t; v != null && v != autopilotMenu; v = v.parent)
                if (menuButtons.Exists(m => m.button == v)) return v;
            return null;
        }
        /// <summary>Modified: the menus' hover sound (the station's: the button push, softer).</summary>
        void PlayHover() => PlayUi(CombatAudio.Load()?.buttonPush, 0.45f);
        /// <summary>While the menu is open (game paused): keys / D-pad pick, 1-8 choose an entry (the PC version's menu
        /// select), Esc / Q / E / B / View close.</summary>
        void UpdateAutopilotMenu()
        {
            var kb = GoF2Remake.Multiplayer.NetChat.Keys;
            var pad = Gamepad.current;
            // The press that opened the menu can still read as "pressed this frame" on the next frame (editor input
            // updates): ignore the toggle keys for two frames.
            if (Time.frameCount - menuOpenedFrame < 2) return;
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || GameControls.AutopilotMenu.WasPressedThisFrame() || GameControls.ActionsMenu.WasPressedThisFrame()
                || (pad != null && (pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame)))
            {
                CloseAutopilotMenu();
                return;
            }
            // Remake (#224): a tap or click outside the menu closes it (on a phone nothing else could: the touch actions
            // button is hidden while a HUD menu is open).
            if (autopilotMenu.ClassListContains("autopilot-menu--shown") && PressedOutsideMenu())
            {
                CloseAutopilotMenu();
                return;
            }
            int move = 0;
            if (kb != null && (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame)) move = -1;
            if (kb != null && (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame)) move = 1;
            if (pad != null)
            {
                var v = pad.dpad.ReadValue() + pad.leftStick.ReadValue();
                int dir = v.y > 0.5f ? -1 : v.y < -0.5f ? 1 : 0;
                if (dir != 0 && dir != lastMenuMove) move = dir;
                lastMenuMove = dir;
            }
            if (move != 0 && menuPointer)
            {
                // Modified: the keys take over from the mouse; over nothing the first press only shows the highlight.
                menuPointer = false;
                if (menuHover == null) { HighlightMenu(); PlayHover(); move = 0; }
            }
            if (move != 0 && menuButtons.Count > 0)
            {
                menuIndex = (menuIndex + move + menuButtons.Count) % menuButtons.Count;
                HighlightMenu();
                PlayHover();   // Modified: the arrows' sound
            }
            bool confirm = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                           || (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame));
            if (kb != null)
            {
                var digits = new[] { kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key, kb.digit5Key, kb.digit6Key, kb.digit7Key, kb.digit8Key };
                for (int d = 0; d < digits.Length; d++)
                    if (digits[d].wasPressedThisFrame && d < menuButtons.Count) { menuPointer = false; menuIndex = d; HighlightMenu(); confirm = true; break; }
            }
            if (confirm && menuPointer && menuHover == null) { menuPointer = false; HighlightMenu(); confirm = false; }   // Modified: nothing marked yet
            if (confirm && menuIndex < menuButtons.Count)
            {
                PlayButton(false);   // Modified: the entry's sound (a click has its own)
                if (menuIndex < menuActions.Count && menuActions[menuIndex] != null) menuActions[menuIndex]();
                else ChooseMenuTarget(menuButtons[menuIndex].target);
            }
        }
        /// <summary>A finger or the (free) mouse went down this frame somewhere other than the menu or the HUD's own buttons
        /// (the touch controls, the pause button, the autopilot button, the chat), which keep their own taps.</summary>
        bool PressedOutsideMenu()
        {
            if (root?.panel == null) return false;
            var ts = Touchscreen.current;
            if (ts != null)
                foreach (var t in ts.touches)
                    if (t.press.wasPressedThisFrame && OutsideMenu(t.position.ReadValue())) return true;
            var mouse = Mouse.current;
            return mouse != null && UnityEngine.Cursor.lockState != CursorLockMode.Locked
                   && mouse.leftButton.wasPressedThisFrame && OutsideMenu(mouse.position.ReadValue());
        }
        bool OutsideMenu(Vector2 screen)
        {
            var picked = root.panel.Pick(RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y)));
            for (var e = picked; e != null; e = e.parent)
                if (e == autopilotMenu || e.ClassListContains("touch-abs") || e.ClassListContains("touch-pause") || e.ClassListContains("nav-button")
                    || e.ClassListContains("chat") || e.ClassListContains("squad") || e.ClassListContains("squad-invite")) return false;
            return true;
        }
        void ChooseMenuTarget(Navigation.Target target)
        {
            if (target != null && target.disabled) return;   // TouchButton+0xa7: half-transparent, no taps
            if (target != null && target.kind == Navigation.Kind.Wingmen) { OpenWingmanMenu(); return; }
            if (target != null && target.kind == Navigation.Kind.Secondary) { OpenSecondaryMenu(); return; }
            nav.ChooseMenuEntry(target);
            HideAutopilotMenu();
        }
        /// <summary>Hud::initHudMenu(2): 307 Fire at will, 308 Attack my target, 309 Secure next waypoint, 310 / 311 Use laser /
        /// Use EMP blaster; a command goes to every living wingman, closes the menu and resumes the game.</summary>
        void OpenWingmanMenu()
        {
            var traffic = level != null ? level.Traffic : null;
            if (traffic == null) return;
            autopilotMenuItems.Clear();
            menuButtons.Clear();
            menuActions.Clear();
            root.Q<Label>("autopilotMenuTitle").text = Localization.Get(306).UiCaps();
            foreach (var (command, text) in new[] { (1, 307), (3, 308), (2, 309), (0, Session.WingmanShowEmp ? 311 : 310) })
            {
                int cmd = command;
                System.Action act = () =>
                {
                    traffic.CommandWingmen(cmd, level.Radar != null ? level.Radar.Locked : null, nav.PlayerRoute);
                    CloseAutopilotMenu();
                    root.Q<Label>("autopilotMenuTitle").text = Localization.Get(571).UiCaps();
                };
                var b = new Button { text = MenuLabel(menuButtons.Count, Localization.Get(text)) };
                b.AddToClassList("autopilot-menu-item");
                b.AddToClassList("gof-semibold");
                b.focusable = false;
                b.clicked += act;
                autopilotMenuItems.Add(b);
                menuButtons.Add((b, null));
                menuActions.Add(act);
            }
            menuIndex = 0;
            HighlightMenu();
        }
        /// <summary>Hud::initHudMenu(1): each mounted secondary as "<name> (<amount>)"; a pick selects it, closes the menu and
        /// resumes the game.</summary>
        void OpenSecondaryMenu()
        {
            if (weapons == null) return;
            autopilotMenuItems.Clear();
            menuButtons.Clear();
            menuActions.Clear();
            root.Q<Label>("autopilotMenuTitle").text = Localization.Get(266).UiCaps();
            int selected = 0;
            foreach (int item in weapons.SecondaryItems())
            {
                int it = item;
                System.Action act = () =>
                {
                    weapons.SelectSecondary(it);
                    CloseAutopilotMenu();
                    root.Q<Label>("autopilotMenuTitle").text = Localization.Get(571).UiCaps();
                };
                if (it == weapons.SelectedSecondary) selected = menuButtons.Count;
                var b = new Button { text = MenuLabel(menuButtons.Count, $"{ItemInfo.ItemName(it)} ({weapons.AmmoOf(it)})") };
                b.AddToClassList("autopilot-menu-item");
                b.AddToClassList("gof-semibold");
                b.focusable = false;
                b.clicked += act;
                autopilotMenuItems.Add(b);
                menuButtons.Add((b, null));
                menuActions.Add(act);
            }
            menuIndex = selected;
            HighlightMenu();
        }
        void OnMiningMessage(string text) => miningView?.ShowMessage(text);
        /// <summary>Remake: Unlock target (CombatRadar.Unlock): the ship lock dropped, that ship not relocked until it has
        /// left the crosshair box.</summary>
        void UnlockTarget()
        {
            if (radar == null || !radar.Unlock()) return;
            miningView?.ShowMessage(Localization.Extra("targetUnlocked", "Target unlocked"), 0, HudMessageKind.Target);   // the next lock's scan replaces it
            BuildHints(InputMode.Current);
        }
        /// <summary>Remake: scans, target notices and cargo gains replace each other on the plate (HudMessageQueue).</summary>
        void OnKindMessage(string text, HudMessageKind kind) => miningView?.ShowMessage(text, 0, kind);
        void OnRadarMessage(string text, int colour, HudMessageKind kind) => miningView?.ShowMessage(text, colour, kind);
        /// <summary>A ChoiceWindow in flight (MGame+0x90): the pause menu's choice page, the game paused.</summary>
        void OnChoiceRequested(string text, string yes, string no, System.Action onYes, System.Action onNo)
            => pauseMenu?.Ask(level, text, yes, no, onYes, onNo, traffic != null ? traffic.ChoiceTitle : null);   // Modified: its title
        /// <summary>Remake: the Mining Plant's waste sorting, on the pause menu's result page (WasteProcessing).</summary>
        void OnWasteSorted(WasteProcessing.Sorting sorting, Database db, System.Action closed) => pauseMenu?.ShowSorting(level, sorting, db, closed);
        /// <summary>The sorting page's soft tone as a lot shows (LotTones), pitched by its tier.</summary>
        void PlayLotTone(WasteProcessing.FindTier tier)
        {
            if (!lotTones.TryGetValue(tier, out var clip) || clip == null) lotTones[tier] = clip = LotTones.Clip(tier);
            PlayUi(clip, LotTones.Shape(tier).volume);
        }
        /// <summary>Remake: a menu of every option at once (the Mining Plant's), on the question window.</summary>
        void OnMenuRequested(string title, List<MenuSection> sections, List<(string label, System.Action action)> entries) =>
            pauseMenu?.Choose(level, title, sections, entries);
        /// <summary>The sorting page's one sound once every lot shows, the best find's: the fanfare (Exceptional), the
        /// hacking-solved sound (Rare), the sell sound (any other find), the question window's (nothing usable).</summary>
        void PlaySortingSound(WasteProcessing.FindTier best)
        {
            var a = CombatAudio.Load();
            if (a == null) return;
            PlayUi(best switch
            {
                WasteProcessing.FindTier.Exceptional => a.wasteExceptional,
                WasteProcessing.FindTier.Rare => a.wasteRare,
                WasteProcessing.FindTier.None => a.messageInfo,
                _ => a.wasteFound,
            });
        }
        void OnCombatMessage(string text, int colour) => miningView?.ShowMessage(text, colour);
    }
}
