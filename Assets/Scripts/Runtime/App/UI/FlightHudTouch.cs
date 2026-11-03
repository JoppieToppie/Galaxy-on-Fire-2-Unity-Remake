// FlightHudTouch.cs
// FlightHud: the touch controls (TouchControls) (part of the partial FlightHud; its fields are in FlightHud.cs).

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
        void BuildTouchControls()
        {
            touch = new TouchControls(root.Q("touchLayer"), root, root.Q("navButtons"))
            {
                FirePressed = OnTouchFire,
                // MGame::OnTouchEnd: the secondary, boost and camera don't act while mining or docked at a point.
                SecondaryReleased = () => { if (!MiningOrDocked) weapons?.FireSecondary(); },
                // The booster also on the way to an asteroid (refused only while drilling, PlayerEgo::isMining).
                BoostReleased = () => { if (!Drilling && (docking == null || !docking.Busy)) ship?.Boost(); },
                CameraPressed = () => level?.FreeLook?.CameraDown(),
                CameraReleased = () => level?.FreeLook?.CameraUp(mining == null || mining.State == Mining.Phase.Idle),
                CameraCancelled = () => level?.FreeLook?.CameraUp(false),
                MenuReleased = OnActionsButton,
                TurretReleased = () => level?.Turret?.Toggle(),
                PausePressed = () => PlayButton(true),
                PauseReleased = () => { PlayButton(false); OpenPause(); },
                LevelOut = () => ship?.AlignToHorizon(),
                CycleSecondary = () => weapons?.CycleSecondary(),
                SecondaryList = () => weapons?.SecondaryItems(stripItems),
                SecondaryAmmo = item => weapons != null ? weapons.AmmoOf(item) : 0,
                SelectedSecondary = () => weapons != null ? weapons.SelectedSecondary : -1,
                SelectSecondary = item => weapons?.SelectSecondary(item),
                Dodge = side => { if (nav == null || !nav.MenuOpen) ship?.RequestDodge(side); },
                GetThrust = () => ship != null ? ship.Model.Throttle : 0f,
                SetThrust = t => ship?.SetThrottle(t),
                FreeLookDrag = (delta, held) => level?.FreeLook?.TouchDrag(delta, held),
                FreeLookPinch = span => level?.FreeLook?.TouchPinch(span),
            };
        }
        /// <summary>The cursor mode: the mouse is over one of the HUD's buttons (the touch controls, the autopilot pill, the
        /// secondary plate, the chat), so its click is that button's, not the fire binding's.</summary>
        bool MouseOverControls()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (!cursorMode || mouse == null || root?.panel == null) return false;
            var sp = mouse.position.ReadValue();
            var picked = root.panel.Pick(RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(sp.x, Screen.height - sp.y)));
            for (var e = picked; e != null && e != root; e = e.parent)
                if (e.ClassListContains("touch-abs") || e.ClassListContains("nav-button") || e == autopilotMenu || e == dockPrompt
                    || e.ClassListContains("chat") || e.ClassListContains("squad") || e.ClassListContains("squad-invite")) return true;
            return false;
        }
        bool Drilling => mining != null && mining.State == Mining.Phase.Mining;
        bool MiningOrDocked => (mining != null && mining.State != Mining.Phase.Idle) || (docking != null && docking.Busy);
        /// <summary>MGame::OnTouchBegin on the fire button: with a landmark, planet or docking target locked the autopilot /
        /// planet jump / docking (a running autopilot turns off), with an asteroid locked the approach, docked at a point
        /// the undocking; the press is used up. During the approach and mining, Mining reacts to the press itself.</summary>
        bool OnTouchFire()
        {
            if (mining != null && mining.State != Mining.Phase.Idle) return false;
            if (docking != null && docking.PromptText != null) { docking.Interact(); return true; }
            if (DockBeforeLock) { Dock(); return true; }
            // Remake: a refused action (a full hold, 525) doesn't use the press up: the original's did, so the autofire
            // latch could neither be set nor cleared while the lock lasted (#27).
            if (nav != null && nav.Locked != null && nav.PromptText != null && nav.Interact()) return true;
            if (mining != null && mining.Locked != null && mining.PromptText != null && mining.Interact()) return true;
            return false;
        }
        /// <summary>The top readout (cargo, timer) left of the touch pause button (0x4b8 at w - 121) while that shows, its right
        /// edge 19 units clear (the original's readout at w - 140): measured from the button where it actually is, not by the
        /// root's input-touch class (the .input-touch .hud-readout rule), which a phone showed the readout over the button
        /// without (a player's report). -1 = the style sheet's place.</summary>
        void KeepReadoutClearOfPause()
        {
            var box = readout != null ? readout.Root : null;
            if (box == null || box.parent == null) return;
            var pause = touch != null ? touch.PauseButton : null;
            float right = -1f;
            if (pause != null && pause.resolvedStyle.display == DisplayStyle.Flex && pause.worldBound.width > 0f)
                right = Mathf.Max(40f, box.parent.worldBound.xMax - pause.worldBound.xMin + 19f);
            if (Mathf.Abs(right - readoutRight) < 0.5f) return;
            readoutRight = right;
            box.style.right = right < 0f ? new StyleLength(StyleKeyword.Null) : new StyleLength(right);
        }
        /// <summary>The touch controls' state for this frame (Hud::draw's conditions, MGame::OnRender2D's hiding).</summary>
        void UpdateTouch()
        {
            if (touch == null) return;
            var f = new TouchControls.Frame { mode = TouchControls.Mode.Off, nextCamera = -1 };
            touchNavShown = false;
            bool touchMode = InputMode.Current == InputKind.Touch;
            // Phones and tablets too, with a mouse connected (a keyboard alone keeps the keycap hints).
            cursorMode = InputMode.Current == InputKind.KeyboardMouse && !Settings.MouseSteering
                         && (!Application.isMobilePlatform || Mouse.current != null);
            root.EnableInClassList("hud-cursor", cursorMode);
            bool flying = ship != null && level != null && !StarMap.IsOpen && !pauseMenu.IsOpen && !storyDialogue.IsOpen
                          && (health == null || !health.Dead);
            if ((touchMode || cursorMode) && flying)
            {
                f.cursor = cursorMode;
                bool cinematic = level.Cutscene || !level.LaunchCameraOver || (nav != null && nav.Jumping) || (jump != null && jump.Cinematic);
                f.cinematic = cinematic;
                f.mode = cinematic || (carrierShop != null && carrierShop.IsOpen) || (dockedMenu != null && dockedMenu.IsOpen) ? TouchControls.Mode.PauseOnly
                       : nav != null && nav.MenuOpen ? TouchControls.Mode.MenuOpen : TouchControls.Mode.Full;
                touchNavShown = f.mode != TouchControls.Mode.PauseOnly;
                var phase = mining != null ? mining.State : Mining.Phase.Idle;
                bool dockBusy = docking != null && docking.Busy;
                var turret = level.Turret;
                bool turretView = turret != null && turret.InTurretView;
                var model = ship.Model;
                f.hasBooster = model.HasBooster;
                f.boosting = model.IsBoosting;
                f.boostReady = model.BoostReady;
                f.boostRate = Mathf.Clamp01(model.BoostRechargePercent);
                int ammo = weapons != null ? weapons.SecondaryAmmo : -1;
                f.secondary = weapons != null && weapons.SelectedSecondary >= 0 && ammo > 0 && !turretView;
                f.secondaryText = f.secondary ? SecondaryText(ammo) : null;
                // Hud::checkIfQuickMenuIsEmpty: the quick menu's entries (the remake's one menu: Khador Drive, wingmen, cloak).
                if (Time.unscaledTime >= quickMenuCheck && nav != null)
                {
                    quickMenuCheck = Time.unscaledTime + 0.5f;
                    quickMenuEntries = nav.MenuEntries(true).Count > 0;
                }
                f.menu = quickMenuEntries;
                var fl = level.FreeLook;
                f.nextCamera = fl != null ? (int)fl.Next : -1;
                f.freeLook = fl != null && fl.FreeLookActive;
                f.standardCamera = fl == null || fl.ShipView;   // the cockpit view dodges too
                // Remake: with several turrets the button is the auto-fire switch whenever any of them is an auto turret.
                var autoTurret = FirstAuto();
                f.turret = autoTurret != null;
                f.turretOn = f.turret && autoTurret.AutoEnabled;
                f.actionArrow = phase == Mining.Phase.Idle && !dockBusy && !turretView
                                && ((nav != null && nav.Locked != null && nav.PromptText != null) || (mining != null && mining.Locked != null && mining.PromptText != null));
                bool tilt = TiltSteering.Active && (Session.FreePlay || Session.CampaignMission != 48);
                f.dimStick = tilt || (nav != null && nav.Autopilot) || phase == Mining.Phase.Approaching || dockBusy;
                f.dimNav = phase != Mining.Phase.Idle || dockBusy;
                f.mining = phase != Mining.Phase.Idle || dockBusy;
                f.steeringMissile = weapons != null && weapons.SteeringMissile;
                f.crosshairVisible = crosshair != null && !crosshair.ClassListContains("crosshair--hidden");
                if (f.crosshairVisible) f.crosshair = new Vector2(crosshair.style.left.value.value, crosshair.style.top.value.value);
            }
            if (!touchMode && flying && !level.Cutscene && level.LaunchCameraOver && (nav == null || !nav.Jumping))
            {
                // Keyboard / controller flight (and the cursor mode): the throttle gauge whenever the throttle moves (keys, wheel,
                // shoulder buttons, a boost; PlayerEgo::draw calls drawThrottle in every mode), and the secondary's plate 0x4c2
                // at the bottom centre (Hud::draw redraws it opaque while the mouse steers).
                f.gauge = true;
                f.crosshairVisible = crosshair != null && !crosshair.ClassListContains("crosshair--hidden");
                if (f.crosshairVisible) f.crosshair = new Vector2(crosshair.style.left.value.value, crosshair.style.top.value.value);
                int ammo = weapons != null ? weapons.SecondaryAmmo : -1;
                bool turretView = level.Turret != null && level.Turret.InTurretView;
                f.secondary = weapons != null && weapons.SelectedSecondary >= 0 && ammo > 0 && !turretView;
                f.secondaryText = f.secondary ? SecondaryText(ammo) : null;
                float thrust = ship.Model.Throttle;
                if (lastThrust >= 0f && !Mathf.Approximately(thrust, lastThrust)) touch.NotifyThrottle();
                lastThrust = thrust;
            }
            if (!f.gauge) lastThrust = -1f;
            touch.Update(f, Time.unscaledDeltaTime * 1000f);
            KeepReadoutClearOfPause();
        }
        static void HookPress(VisualElement button, System.Action down, System.Action up = null)
        {
            int pointer = -1;
            button.RegisterCallback<PointerDownEvent>(e =>
            {
                if (pointer >= 0) return;
                pointer = e.pointerId;
                button.CapturePointer(pointer);
                button.AddToClassList("touch-button--pressed");
                down?.Invoke();
                e.StopPropagation();
            });
            void Up(int id)
            {
                if (id != pointer) return;
                if (button.HasPointerCapture(pointer)) button.ReleasePointer(pointer);
                pointer = -1;
                button.RemoveFromClassList("touch-button--pressed");
                up?.Invoke();
            }
            button.RegisterCallback<PointerUpEvent>(e => Up(e.pointerId));
            button.RegisterCallback<PointerCancelEvent>(e => Up(e.pointerId));
        }
    }
}
