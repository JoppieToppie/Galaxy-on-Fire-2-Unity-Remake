// StationMenuInspect.cs
// Part of StationMenu: Inspect ship (the orbit view around the hangar ship) and the Customize screen of a
// customizable ship.

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using PointerType = UnityEngine.UIElements.PointerType;
using GoF2Remake.Flight;

namespace GoF2Remake.UI
{
    public partial class StationMenu
    {
        void SetupInspect()
        {
            inspectOverlay = new VisualElement { name = "inspectOverlay", pickingMode = PickingMode.Ignore };
            inspectOverlay.AddToClassList("inspect-overlay");
            inspectTitle = OrbitViewUi.Title();   // the overlay is shared with Action Freeze (OrbitViewUi, .orbit-view-* in GoF2Common.uss)
            inspectOverlay.Add(inspectTitle);
            var footer = inspectFooter = new VisualElement { pickingMode = PickingMode.Ignore };
            footer.AddToClassList("orbit-view-footer");
            footer.AddToClassList(SafeArea.EdgeClass);
            inspectBack = OrbitViewUi.Back(EndInspect, () => Play(buttonPush));
            footer.Add(inspectBack);
            inspectMessage = OrbitViewUi.Message();
            inspectOverlay.Add(inspectMessage);
            inspectHints = OrbitViewUi.Hints();
            inspectOverlay.Add(inspectHints);   // top right, the controls for the input in use (docs/UI-DESIGN.md "Button prompts")
            inspectOverlay.Add(footer);
            customize = new ShipCustomizePanel { ClickSound = () => Play(buttonRelease), StatsText = CustomStatsText };
            customize.Changed += ShowCustomBuild;
            customize.Applied += ApplyCustomize;
            customize.Cancelled += CancelCustomize;
            inspectOverlay.Add(customize.Root);
            inspectTools = new PhotoTools(inspectOverlay, push => Play(push ? buttonPush : buttonRelease));
            inspectTools.Changed += () => BuildInspectHints(InputMode.Current);
            inspectTools.Tapped += () => { if (inspectUiHidden) SetInspectUiHidden(false); };
            SafeArea.KeepClear(inspectOverlay);   // its title, hints, footer, Customize and settings panels clear of cutouts
            root.Add(inspectOverlay);   // the last child: over the blocker that keeps the menu's own input out meanwhile
        }

        void BeginInspect()
        {
            if (level == null || level.View != StationView.Hangar || ArrivalPending || UiBlocked || DialogOpen || SystemMenuOpen
                || HangarOpen || StarMap.IsOpen || (storyDialogue != null && storyDialogue.IsOpen)) return;
            if (!level.BeginInspect()) return;
            if (!customizing) level.InspectFrameX = 0.5f;   // re-entered while a Customize screen's turn is blending back
            inspectDragging = false;
            inspectFling = Vector2.zero;
            inspectPinch = -1f;
            inspectMessageMs = 0f;
            inspectMessage.text = "";
            SetInspectUiHidden(false);
            if (root.focusController?.focusedElement is VisualElement f) f.Blur();
            inspectTitle.text = GameNames.Ship(Session.ShipIndex).ToUpperInvariant();
            var insp = level.Inspect;
            inspectTools.Collide = level.RoomMove;   // Modified: the free camera stops at the hangar's walls too
            inspectTools.Begin(Camera.main, level.PlayerShip, () => insp.Centre, insp.Radius, insp.Radius * 6f, insp.FloorY,
                               () => (insp.OrbitPosition, insp.OrbitRotation), true);
            BuildInspectHints(InputMode.Current);
            if (inspectOverlay != null) inspectOverlay.AddToClassList("inspect-overlay--shown");
            root.AddToClassList("station-inspecting");   // the menu's dark shades fade out with it
        }

        void EndInspect()
        {
            if (customizing) { CancelCustomize(); return; }   // Back / Esc in the Customize screen: its Cancel
            if (level == null || !level.Inspect.Active) return;
            Play(buttonRelease);
            SetInspectUiHidden(false);
            inspectTools.End();   // the free pose, roll and lens stay with HangarInspect through the blend back
            level.EndInspect();
            if (inspectOverlay != null) inspectOverlay.RemoveFromClassList("inspect-overlay--shown");
            root.RemoveFromClassList("station-inspecting");
            Select(inspectButton);   // back where it was opened (keys / controller)
        }

        void RefreshCustomizeButton()
        {
            if (customizeButton == null) return;
            var shown = CanCustomize ? StyleKeyword.Null : (StyleEnum<DisplayStyle>)DisplayStyle.None;
            if (customizeButton.style.display != shown) customizeButton.style.display = shown;
        }

        void BeginCustomize()
        {
            if (!CanCustomize || customizing) return;
            int ship = Session.ShipIndex;
            var kit = ModShipKits.CustomizationOf(ship);
            var build = ModShipKits.PlayerBuild(ship);
            if (kit == null || build == null) return;
            BeginInspect();
            if (!level.Inspect.Active) return;
            inspectTools.End();   // the Customize screen has its own panel and keys: no photo tools
            level.Inspect.ClearFree();
            level.Inspect.RollDeg = 0f;
            level.Inspect.FovDeg = 0f;
            string key = ModShipKits.BuildKey(ship);
            customizeBefore = key != null && Session.ShipBuilds.TryGetValue(key, out var text) ? text : null;
            customizing = true;
            inspectTitle.text = "";
            inspectHints.style.display = DisplayStyle.None;   // the panel has its own keys
            inspectFooter.style.display = DisplayStyle.None;  // and its own Cancel: the footer's Back lay under its Apply
            customize.Show(GameNames.Ship(ship), kit, ModShipKits.TypeOf(ship), build, ship);
            level.PreviewEngines(true);   // the exhaust running, so its style and colours can be seen
        }

        /// <summary>A change in the panel: the turntable shows it at once (the build is the player's until Cancel).</summary>
        void ShowCustomBuild(ModShipKits.Build build)
        {
            ModShipKits.SetPlayerBuild(Session.ShipIndex, build);
            // A part not loaded yet (only the builds in use load at the start): the turntable keeps the last build until it has
            // (StationLevel rebuilds it on ModShips.ModelsChanged).
            if (ModShips.EnsureBuild(Session.ShipIndex, ModShipKits.PlayerBuild(Session.ShipIndex))) level.ReplacePlayerShip(Session.ShipIndex);
            else ShowToast(Localization.Extra("kitLoadingParts", "Loading parts..."), true);
        }

        void ApplyCustomize()
        {
            if (!customizing) return;
            EndCustomize();
            // The build's slots (ModShipKits): what no longer fits goes to the hold, like after buying a smaller ship.
            int moved = Hangar.FitToSlots(level.Db);
            if (moved > 0) level.ReplacePlayerShip(Session.ShipIndex);   // a turret taken off
            SaveGame.AutoSave();   // the build is part of the save (Session.ShipBuilds)
            ShowToast(moved > 0 ? string.Format(Localization.Extra("kitSavedMoved", "Ship build saved. {0} item(s) moved to the hold."), moved)
                                : Localization.Extra("kitSaved", "Ship build saved."));
        }

        /// <summary>The Customize panel's stats line: the ship as 'build' makes it, and what wouldn't fit its slots.</summary>
        string CustomStatsText(ModShipKits.Build build)
        {
            var kit = ModShipKits.CustomizationOf(Session.ShipIndex);
            var c = level.Db.Ship(Session.ShipIndex);   // a kit ship's data, or the game's / a mod's ship
            if (kit == null || c == null) return "";
            var e = ModShipKits.EffectOf(kit, build);
            int hull = Mathf.Max(1, Mathf.RoundToInt(c.armor * (1f + e.armor / 100f))) + 40 * Session.ModLevel(0);
            int cargo = Mathf.Max(0, Mathf.RoundToInt(c.cargo * (1f + e.cargo / 100f))) + 30 * Session.ModLevel(1);
            int handling = Mathf.RoundToInt(c.handling * (1f + e.handling / 100f)) + 20 * Session.ModLevel(3);
            int[] slots = { e.primary >= 0 ? e.primary : c.slots.primary, e.secondary >= 0 ? e.secondary : c.slots.secondary,
                            e.turret >= 0 ? e.turret : c.slots.turret, Mathf.Max(0, c.slots.equipment + e.equipment) + Session.ModLevel(2) };
            var used = new int[4];
            foreach (var st in Session.Equipment) { if (!Hangar.TakesSlot(level.Db, st)) continue; int t = level.Db.Item(st.item)?.TypeId ?? 4; if (t >= 0 && t < 4) used[t]++; }
            int over = 0;
            for (int t = 0; t < 4; t++) over += Mathf.Max(0, used[t] - slots[t]);
            string Sign(float v) => v == 0f ? "" : $" ({(v > 0 ? "+" : "")}{v:0}%)";
            string line = $"{Localization.Get(165)} {hull}{Sign(e.armor)}   {Localization.Get(166)} {cargo} t{Sign(e.cargo)}   {Localization.Get(164)} {handling}{Sign(e.handling)}\n"
                        + $"{Localization.Get(265)} {slots[0]}   {Localization.Get(266)} {slots[1]}   {Localization.Get(267)} {slots[2]}   {Localization.Get(269)} {slots[3]}";
            var f = e.flight;
            string flight = string.Join("   ", new[] { (Localization.Extra("kitSpeed", "Speed"), f.topSpeed), (Localization.Extra("kitTurn", "Turning"), f.turnRate),
                                                       (Localization.Extra("kitAccel", "Acceleration"), f.acceleration) }
                .Where(x => x.Item2 != 0f).Select(x => $"{x.Item1}{Sign(x.Item2)}"));
            if (flight.Length > 0) line += "\n" + flight;
            if (over > 0) line += "\n" + string.Format(Localization.Extra("kitOverflow", "{0} mounted item(s) won't fit: they go to the hold."), over);
            return line;
        }

        void CancelCustomize()
        {
            if (!customizing) return;
            string key = ModShipKits.BuildKey(Session.ShipIndex);
            if (key != null)
            {
                if (customizeBefore != null) Session.ShipBuilds[key] = customizeBefore;
                else Session.ShipBuilds.Remove(key);
            }
            level.ReplacePlayerShip(Session.ShipIndex);
            EndCustomize();
        }

        void EndCustomize()
        {
            customizing = false;
            level.PreviewEngineDrive(1f);
            level.PreviewEngines(false);
            customize.Hide();
            // InspectFrameX stays until the orbit has blended back (StationLevel resets it), so the view turns back with it.
            inspectHints.style.display = StyleKeyword.Null;
            inspectFooter.style.display = StyleKeyword.Null;
            EndInspect();
            Select(customizeButton);
        }

        /// <summary>Hide UI: the overlay's title, footer and message out of the way (and the FPS / story step labels).</summary>
        void SetInspectUiHidden(bool hidden)
        {
            inspectUiHidden = hidden;
            inspectOverlay?.EnableInClassList("orbit-view--clean", hidden);
            FpsCounter.Suppressed = hidden || inspectCapturing;
        }

        void TakeInspectScreenshot()
        {
            if (inspectCapturing || level == null || !level.Inspect.Active) return;
            StartCoroutine(CaptureInspect());
        }

        System.Collections.IEnumerator CaptureInspect()
        {
            inspectCapturing = true;
            bool saved = false;
            // The photo tools' capture: the overlay hidden, cut to the frame, twice the size if asked.
            yield return inspectTools.Capture(inspectOverlay, ok => saved = ok);
            inspectCapturing = false;
            FpsCounter.Suppressed = inspectUiHidden;
            inspectMessage.text = Localization.Get(saved ? 55 : 56);   // "Screenshot saved." / "Error saving screenshot."
            inspectMessageMs = 3000f;
        }

        void BuildInspectHints(InputKind kind)
        {
            OrbitViewUi.BuildHints(inspectHints, kind, new[] { "W", "A", "S", "D" },
                () => { Play(buttonRelease); SetInspectUiHidden(true); },
                () => { Play(buttonRelease); TakeInspectScreenshot(); },
                EndInspect, () => Play(buttonPush),
                customizing ? null : inspectTools.ToggleFree, customizing ? null : inspectTools.ToggleSettings, !customizing && inspectTools.Free);
        }

        /// <summary>A pointer on one of the footer's buttons (while they show): a click there isn't a drag (screen pixels, y up).</summary>
        bool OverInspectButton(Vector2 screen) => !inspectUiHidden && (OrbitViewUi.OverControl(root, inspectBack, inspectHints, screen)
                                                                       || (customize != null && customize.Contains(root, screen))
                                                                       || inspectTools.OverPanel(screen));

        /// <summary>The orbit camera's input; 'customize' (the Customize screen): its panel takes the arrows, the left stick,
        /// Enter / Esc and the face buttons, the camera keeps the drag, the wheel / pinch / triggers, the right stick (and
        /// W A S D only when the panel doesn't: never).</summary>
        void UpdateInspect(bool customize = false)
        {
            var insp = level.Inspect;
            float frames = Time.unscaledDeltaTime * 1000f / OriginalFrame.Ms;
            var kb = GoF2Remake.Multiplayer.NetChat.Keys;   // null while a multiplayer chat line is typed
            var pad = Gamepad.current;
            // The photo tools (not in the Customize screen): their keys first, the settings panel takes the arrows / Enter /
            // Esc while open; the free camera replaces the orbit.
            bool panelKeys = false, backTaken = false;
            if (!customize)
            {
                panelKeys = inspectTools.TickKeys(kb, pad, out backTaken);
                insp.RollDeg = PhotoSettings.Roll;
                insp.FovDeg = PhotoSettings.Fov;
                if (inspectTools.Free)
                {
                    inspectTools.TickFree(kb, pad, Time.unscaledDeltaTime, OverInspectButton, out _);
                    var (fp, fr) = inspectTools.FreePose;
                    insp.SetFreePose(fp, fr);
                }
                else insp.ClearFree();
                inspectTools.LateTick();
            }
            if (!customize && !panelKeys && !backTaken)
            {
                if ((kb != null && (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame || kb.iKey.wasPressedThisFrame || kb.digit6Key.wasPressedThisFrame))
                    || (pad != null && (pad.buttonEast.wasPressedThisFrame || pad.rightStickButton.wasPressedThisFrame)))
                { EndInspect(); return; }
                if ((kb != null && kb.hKey.wasPressedThisFrame) || (pad != null && pad.buttonNorth.wasPressedThisFrame))
                { Play(buttonRelease); SetInspectUiHidden(!inspectUiHidden); }
                if ((kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame))
                    || (pad != null && pad.buttonSouth.wasPressedThisFrame))
                    TakeInspectScreenshot();
            }
            if (inspectMessageMs > 0f && (inspectMessageMs -= Time.unscaledDeltaTime * 1000f) <= 0f) inspectMessage.text = "";
            if (!customize && inspectTools.Free) return;   // the free camera took the input above; the orbit rests
            if (panelKeys) { kb = null; pad = null; }      // the settings panel has the keys and the controller

            float scale = 1080f / Mathf.Max(1, Screen.height);   // PhotoMode's pixels: a 1080-high screen
            Vector2 delta = Vector2.zero;   // x: + takes the camera to its left (the ship turns with a drag right); y: + up
            bool held = false;
            var ts = Touchscreen.current;
            int down = 0;
            if (ts != null) foreach (var t in ts.touches) if (t.press.isPressed) down++;
            if (down >= 2)
            {
                // Two fingers: a pinch zooms (spreading = closer).
                Vector2 a = Vector2.zero, b = Vector2.zero; int n = 0;
                foreach (var t in ts.touches) if (t.press.isPressed) { if (n == 0) a = t.position.ReadValue(); else if (n == 1) b = t.position.ReadValue(); n++; }
                float span = (a - b).magnitude;
                if (inspectPinch > 1f && span > 1f) insp.Zoom(inspectPinch / span);
                inspectPinch = span;
                inspectDragging = false;
            }
            else
            {
                inspectPinch = -1f;
                Vector2? pos = null;
                if (down == 1) pos = ts.primaryTouch.position.ReadValue();
                else if (Mouse.current != null && Mouse.current.leftButton.isPressed) pos = Mouse.current.position.ReadValue();
                // A press that began on the settings panel (a slider) or a button stays theirs when the pointer leaves them.
                bool overUi = inspectTools.PressOverUi(pos, OverInspectButton);
                if (pos.HasValue && !overUi)
                {
                    if (!inspectDragging) { inspectDragging = true; inspectLast = pos.Value; inspectDragTravel = 0f; }
                    delta = (pos.Value - inspectLast) * scale;
                    delta.y = -delta.y;   // remake (players' request): a drag up takes the camera down, as in Action Freeze
                    inspectDragTravel += delta.magnitude;
                    inspectLast = pos.Value;
                    inspectFling = delta;
                    held = true;
                }
                else if (inspectDragging)
                {
                    inspectDragging = false;
                    if (inspectFling.magnitude <= 3f) inspectFling = Vector2.zero;
                    // The UI hidden: a short click / tap (no drag) brings it back.
                    if (inspectUiHidden && inspectDragTravel < 8f) { inspectFling = Vector2.zero; SetInspectUiHidden(false); }
                }
            }
            if (!held && inspectFling != Vector2.zero)
            {
                delta = inspectFling * frames;
                inspectFling *= Mathf.Pow(0.9f, frames);
                if (inspectFling.magnitude <= 1f) inspectFling = Vector2.zero;
            }
            // The arrows / W A S D move the camera that way (PhotoMode's 4 px a frame), the sticks 8.
            if (kb != null && !customize)
            {
                float left = kb.leftArrowKey.isPressed || kb.aKey.isPressed ? 1f : 0f, right = kb.rightArrowKey.isPressed || kb.dKey.isPressed ? 1f : 0f;
                float up = kb.upArrowKey.isPressed || kb.wKey.isPressed ? 1f : 0f, dn = kb.downArrowKey.isPressed || kb.sKey.isPressed ? 1f : 0f;
                delta += new Vector2(left - right, up - dn) * 4f * frames;
                float zoomKeys = (kb.minusKey.isPressed || kb.numpadMinusKey.isPressed || kb.pageDownKey.isPressed ? 1f : 0f)
                               - (kb.equalsKey.isPressed || kb.numpadPlusKey.isPressed || kb.pageUpKey.isPressed ? 1f : 0f);
                if (zoomKeys != 0f) insp.Zoom(Mathf.Pow(1.03f, zoomKeys * frames));
            }
            if (customize && kb != null)
            {
                float zoomKeys = (kb.minusKey.isPressed || kb.numpadMinusKey.isPressed || kb.pageDownKey.isPressed ? 1f : 0f)
                               - (kb.equalsKey.isPressed || kb.numpadPlusKey.isPressed || kb.pageUpKey.isPressed ? 1f : 0f);
                if (zoomKeys != 0f) insp.Zoom(Mathf.Pow(1.03f, zoomKeys * frames));
            }
            if (pad != null)
            {
                var s = (customize ? Vector2.zero : pad.leftStick.ReadValue()) + pad.rightStick.ReadValue();
                delta += new Vector2(-s.x, s.y) * 8f * frames;
                float trig = pad.leftTrigger.ReadValue() - pad.rightTrigger.ReadValue();   // RT closer, LT further
                if (Mathf.Abs(trig) > 0.05f) insp.Zoom(Mathf.Pow(1.03f, trig * frames));
            }
            if (Mouse.current != null && !inspectTools.OverPanel(Mouse.current.position.ReadValue()))
            {
                float steps = Mathf.Clamp(Mouse.current.scroll.ReadValue().y, -1f, 1f);   // a notch reads 1 or 120
                if (Mathf.Abs(steps) > 0f) insp.Zoom(Mathf.Pow(0.85f, steps));
            }
            delta = Settings.OrbitTurn(delta);   // #121: the camera views' own inversion
            insp.Turn(delta.x * 0.005f, delta.y * 0.005f);   // PhotoMode's 0.005 rad per pixel
        }
    }
}
