// FlightHudInputMode.cs
// FlightHud: the input mode (keys, controller, touch, mouse) (part of the partial FlightHud; its fields are in FlightHud.cs).

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
        void ApplyInputMode()
        {
            if (root == null) return;
            var kind = InputMode.Current;
            missileWarning?.RefreshHint();
            root.EnableInClassList("input-touch", kind == InputKind.Touch);
            root.EnableInClassList("input-keyboard", kind == InputKind.KeyboardMouse);
            root.EnableInClassList("input-gamepad", kind == InputKind.Gamepad);
            root.EnableInClassList("hud-vr", Vr.VrMode.Enabled);   // VR: the HUD's middle is the canopy HUD, its corners the cockpit's displays
            if (kind != InputKind.Touch) { touch?.Reset(); weapons?.SetPrimaryHeld(false); }
            // PlayerEgo::update: handling-dependent damping only with the mouse cursor, else resetShipHandling's constants.
            if (chase != null) chase.handlingDependent = kind == InputKind.KeyboardMouse;
            BuildHints(kind);
            SetPromptGlyph(kind);
        }
        /// <summary>The action prompt's key: GameControls.Action (F, the PC version's Dock key: dock, the autopilot to a locked
        /// station, the planet jump, texts 587 / 1731; Enter too; controller X), as bound.</summary>
        void SetPromptGlyph(InputKind kind)
        {
            dockGlyph.Clear();
            foreach (var g in InputGlyph.For(GameControls.Action, kind)) dockGlyph.Add(g);
        }
        /// <summary>A menu entry straight from its key (V Wingmen, K Khador Drive): the autopilot menu opens on it; nothing
        /// when the entry isn't offered.</summary>
        void OpenMenuEntry(Navigation.Kind kind)
        {
            if (nav == null || nav.MenuOpen || !nav.CanOpenActions) return;
            var entry = nav.MenuEntries(true).Find(t => t.kind == kind && !t.disabled);
            if (entry == null) return;
            OpenAutopilotMenu(true);
            if (!nav.MenuOpen) return;
            int i = menuButtons.FindIndex(b => b.target != null && b.target.kind == kind);
            if (i < 0) return;
            menuIndex = i;
            HighlightMenu();
            ChooseMenuTarget(menuButtons[i].target);
        }
        /// <summary>The ship's first auto turret (remake: one of several), null without one.</summary>
        PlayerTurret FirstAuto()
        {
            if (level == null || level.Turrets == null) return null;
            foreach (var t in level.Turrets) if (t != null && t.IsAuto) return t;
            return null;
        }
        /// <summary>Any turret with a turret view (a manual turret or a plasma collector).</summary>
        bool HasManualTurret()
        {
            if (level == null || level.Turrets == null) return false;
            foreach (var t in level.Turrets) if (t != null && !t.IsAuto) return true;
            return false;
        }
        void BuildHints(InputKind kind)
        {
            hints.Clear();
            hintKind = kind;
            if (kind == InputKind.Touch) return;
            bool pad = kind == InputKind.Gamepad;
            string T(string key, string english) => Localization.Extra(key, english);
            // The menus' own keys are fixed (arrows / D-pad, Enter / A, Esc / B / Menu); the rest are GameControls' bindings.
            var menuKey = pad ? InputGlyph.Pad(PadButton.Menu) : InputGlyph.Key("ESC");
            if (carrierShop != null && carrierShop.IsOpen)
            {
                // The carrier's resupply window (CarrierShopWindow): its own keys.
                if (!pad)
                {
                    Hint(T("hudSelect", "SELECT"), InputGlyph.Key("↑"), InputGlyph.Key("↓"));
                    Hint(T("shopBuy", "BUY"), InputGlyph.Key("→"), InputGlyph.Key("ENTER", true));
                    Hint(T("hudUndock", "UNDOCK"), InputGlyph.Key("ESC"));
                }
                else
                {
                    Hint(T("hudSelect", "SELECT"), InputGlyph.Pad(PadButton.DPad));
                    Hint(T("shopBuy", "BUY"), InputGlyph.Pad(PadButton.A));
                    Hint(T("hudUndock", "UNDOCK"), InputGlyph.Pad(PadButton.B));
                }
                return;
            }
            if (nav != null && nav.MenuOpen)
            {
                if (!pad)
                {
                    Hint(T("hudSelect", "SELECT"), InputGlyph.Key("↑"), InputGlyph.Key("↓"));
                    Hint(T("hudConfirm", "CONFIRM"), InputGlyph.Key("ENTER", true), InputGlyph.Key("1-8", true));
                    var back = new List<VisualElement> { InputGlyph.Key("ESC") };
                    back.AddRange(InputGlyph.For(nav.MenuIsActions ? GameControls.ActionsMenu : GameControls.AutopilotMenu, kind));
                    Hint(T("hudBack", "BACK"), back.ToArray());
                }
                else
                {
                    Hint(T("hudSelect", "SELECT"), InputGlyph.Pad(PadButton.DPad));
                    Hint(T("hudConfirm", "CONFIRM"), InputGlyph.Pad(PadButton.A));
                    Hint(T("hudBack", "BACK"), InputGlyph.Pad(PadButton.B));
                }
                return;
            }
            string fire = T("hudFire", "FIRE"), throttle = T("hudThrottle", "THROTTLE");
            var turretNow = level != null ? level.Turret : null;
            if (turretNow != null && turretNow.InTurretView)
            {
                // PlayerEgo::setTurretMode: the stick aims the turret, fire fires it, the ship flies straight.
                Hint(T("hudAimTurret", "AIM TURRET"), GameControls.Steer);
                Hint(throttle, GameControls.Throttle);
                Hint(fire, GameControls.FirePrimary);
                Hint(T("hudTurretExit", "CHASE VIEW"), GameControls.Camera);
                return;
            }
            // Multiplayer: the held button is a cruise mode (Navigation.Cruise), not a fast-forward.
            // Between the systems it is the drive's.
            string ff = (NetSession.Active || (nav != null && nav.Interstellar) ? T("hudCruise", "CRUISE") : T("hudFastForward", "FAST FORWARD")) + " (" + T("hudHold", "HOLD") + ")";
            if (lastAutopilot)
            {
                // Autopilot: throttle, boost and guns still work; fast-forward is held. Off: the autopilot key, or the
                // action button on a controller (the prompt says Autopilot off).
                Hint(throttle, GameControls.Throttle);
                Hint(ff, GameControls.FastForward);
                Hint(T("hudAutopilotOff", "AUTOPILOT OFF"), pad ? GameControls.Action : GameControls.AutopilotMenu);
                Hint(fire, GameControls.FirePrimary);
                Hint(T("hudMenu", "MENU"), menuKey);
                return;
            }
            if (lastPhase != Mining.Phase.Idle)
            {
                // Autopilot to an asteroid / mining: only the drill and the action prompt matter.
                bool drilling = lastPhase == Mining.Phase.Mining;
                if (drilling) Hint(T("hudDrill", "DRILL"), GameControls.Drill);
                if (lastPhase == Mining.Phase.Approaching) Hint(ff, GameControls.FastForward);
                Hint(drilling ? T("hudMiningStop", "STOP MINING") : T("hudMiningAbort", "ABORT"), GameControls.FirePrimary, GameControls.Action);
                return;
            }
            if (FlightStyles.Current == FlightStyles.Free)
            {
                // Free flight (EVERSPACE 2's layout, ShipController.FreeFlightActive): the held movement axes, the held boost.
                if (!pad) Hint(T("hudAim", "AIM"), InputGlyph.Key(T("hudMouse", "MOUSE")));
                Hint(pad ? T("hudAim", "AIM") : T("hudSteer", "STEER"), GameControls.Steer);
                Hint(T("hudThrust", "THRUST"), GameControls.Thrust);
                Hint(T("hudStrafe", "STRAFE"), GameControls.StrafeAxis);
                Hint(T("hudHover", "UP / DOWN"), GameControls.Hover);
                Hint(T("hudRoll", "ROLL"), GameControls.Roll);
                Hint(T("hudBoost", "BOOST") + " (" + T("hudHold", "HOLD") + ")", GameControls.Boost);
                if (nav != null && nav.Drive != null) Hint(T("hudCruiseDrive", "CRUISE") + " (" + T("hudHold", "HOLD") + ")", GameControls.FastForward);   // remake: a cruise drive
                Hint(T("hudDampeners", "DAMPENERS"), GameControls.Dampeners);
                Hint(fire, GameControls.FirePrimary);
                Hint(T("hudMissile", "MISSILE"), GameControls.FireSecondary);
                if (weapons != null && weapons.CanCycleSecondary) Hint(T("hudSwitchSecondary", "SWITCH"), GameControls.SwitchSecondary);
                if (radar != null && radar.Locked != null) Hint(T("hudUnlock", "UNLOCK"), GameControls.UnlockTarget);
                Hint(T("hudFreeLook", "FREE LOOK"), GameControls.FreeLookHold);
                if (FirstAuto() != null) Hint(Localization.Get(37).ToUpperInvariant(), GameControls.AutoTurret);
                if (HasManualTurret()) Hint(T("hudTurretView", "TURRET VIEW"), GameControls.Camera);
                CockpitHint(pad);
                if (!SupernovaChallenge.Active) Hint(Localization.Get(571).ToUpperInvariant(), GameControls.AutopilotMenu);   // no autopilot menu there (0xb7)
                if (nav != null && nav.MenuEntries(true).Count > 0) Hint(T("hudActions", "ACTIONS"), GameControls.ActionsMenu);
                Hint(T("hudMenu", "MENU"), menuKey);
                return;
            }
            // The PC version's defaults (Galaxy on Fire 2 Full HD), or the player's own bindings.
            Hint(T("hudSteer", "STEER"), GameControls.Steer);
            Hint(throttle, GameControls.Throttle);
            Hint(T("hudBrake", "BRAKE"), GameControls.Brake);
            Hint(T("hudBoost", "BOOST"), GameControls.Boost);
            if (nav != null && nav.Drive != null) Hint(T("hudCruiseDrive", "CRUISE") + " (" + T("hudHold", "HOLD") + ")", GameControls.FastForward);   // remake: a cruise drive
            Hint(fire, GameControls.FirePrimary);
            Hint(T("hudMissile", "MISSILE"), GameControls.FireSecondary);
            if (weapons != null && weapons.CanCycleSecondary) Hint(T("hudSwitchSecondary", "SWITCH"), GameControls.SwitchSecondary);
            if (radar != null && radar.Locked != null) Hint(T("hudUnlock", "UNLOCK"), GameControls.UnlockTarget);
            Hint(T("hudStrafe", "STRAFE"), GameControls.StrafeLeft, GameControls.StrafeRight);
            Hint(T("hudDodge", "DODGE"), GameControls.DodgeLeft, GameControls.DodgeRight);   // left out while unbound
            Hint(T("hudRoll", "ROLL"), GameControls.Roll);
            Hint(T("hudLevel", "LEVEL"), GameControls.LevelOut);
            // Remake: a ship may carry an auto turret and a manual one at once: a hint for each kind it has.
            if (FirstAuto() != null) Hint(Localization.Get(37).ToUpperInvariant(), GameControls.AutoTurret);
            if (HasManualTurret()) Hint(T("hudTurretView", "TURRET VIEW"), GameControls.Camera);
            CockpitHint(pad);
            if (!SupernovaChallenge.Active) Hint(Localization.Get(571).ToUpperInvariant(), GameControls.AutopilotMenu);   // no autopilot menu there (0xb7)
            if (nav != null && nav.MenuEntries(true).Count > 0) Hint(T("hudActions", "ACTIONS"), GameControls.ActionsMenu);
            Hint(T("hudMenu", "MENU"), menuKey);
        }
        /// <summary>The cockpit view's own key (F2), or with a controller the camera button held (FreeLookCamera.ToggleCockpit;
        /// it left the camera cycle). Not in VR, which flies from the seat.</summary>
        void CockpitHint(bool pad)
        {
            if (VrFlags.Enabled) return;
            string T(string key, string english) => Localization.Extra(key, english).ToUpperInvariant();
            if (pad) Hint(T("hudCockpit", "COCKPIT") + " (" + T("hudHold", "HOLD") + ")", GameControls.Camera);
            else Hint(T("hudCockpit", "COCKPIT"), GameControls.CockpitView);
        }
        /// <summary>A hint with the keys the actions are bound to for the current input kind; left out when none is bound.</summary>
        void Hint(string label, params UnityEngine.InputSystem.InputAction[] actions)
        {
            var glyphs = new List<VisualElement>();
            foreach (var a in actions) glyphs.AddRange(InputGlyph.For(a, hintKind));
            if (glyphs.Count > 0) Hint(label, glyphs.ToArray());
        }
        void Hint(string label, params VisualElement[] glyphs)
        {
            var h = new VisualElement { pickingMode = PickingMode.Ignore };
            h.AddToClassList("hint");
            // Modified: the glyphs in a row of their own (hint-keys), the label under them (GoF2InputGlyphs.uss).
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
