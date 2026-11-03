// VrPad.cs
// Remake VR (a headset): the two VR controllers as one virtual gamepad, so every game control, its rebinding and the menus'
// controller navigation work unchanged (GameControls' Gamepad bindings, InputMode Gamepad, the Xbox glyphs on Auto); and the
// right controller's laser as a virtual mouse for the floating UI screens (VrRig places it where the laser hits).
// Gamepad mapping (OpenXR's common controller names, so Touch / Index / Vive / WMR all map):
//   left stick         left thumbstick (Vive: trackpad)      right stick        right thumbstick
//   left / right trigger  the triggers                       LB / RB            the grips
//   A / B              right primary / secondary button       X / Y              left primary / secondary button
//   Menu (start)       left menu button                       View (select)      left thumbstick click (the autopilot menu)
//   D-pad left         right thumbstick click (the actions menu: secondaries, wingmen, cloak, Khador, time extender)
// Fed in InputSystem.onBeforeUpdate, so the game reads it in the same update.

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace GoF2Remake.Vr
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class VrPad
    {
        static InputActionMap map;
        static InputAction lStick, rStick, lTrigger, rTrigger, lGrip, rGrip, a, b, x, y, menu, lClick, rClick;
        static Gamepad pad;
        static Mouse pointer;
        static Vector2 pointerPosition;
        static bool pointerDown;
        static float pointerScroll;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            map = null;
            pad = null;
            pointer = null;
            GripsAsShoulders = true;
        }

        /// <summary>The right trigger (also the laser's click), 0..1.</summary>
        public static float RightTrigger => rTrigger != null ? rTrigger.ReadValue<float>() : 0f;
        /// <summary>The grips are the gamepad's LB / RB (the throttle steps); off while the cockpit's stick and throttle are
        /// grabbed with them (VrControls, the option).</summary>
        public static bool GripsAsShoulders = true;
        /// <summary>The left grip (VrControls: the throttle lever), 0..1.</summary>
        public static float LeftGrip => lGrip != null ? lGrip.ReadValue<float>() : 0f;
        /// <summary>The right grip (VrRig: grabbing with the laser), 0..1.</summary>
        public static float RightGrip => rGrip != null ? rGrip.ReadValue<float>() : 0f;
        /// <summary>Both thumbsticks clicked together (VrRig: recentre the view, #205).</summary>
        public static bool BothSticksClicked => lClick != null && rClick != null && lClick.IsPressed() && rClick.IsPressed();

        static InputAction Add(string name, string type, params string[] paths)
        {
            var action = map.AddAction(name, type == "Button" ? InputActionType.Button : InputActionType.Value, expectedControlLayout: type);
            foreach (var p in paths) action.AddBinding(p);
            return action;
        }

        public static void Install()
        {
            if (map != null) return;
            map = new InputActionMap("VrControllers");
            lStick = Add("lStick", "Vector2", "<XRController>{LeftHand}/thumbstick", "<XRController>{LeftHand}/trackpad", "<XRController>{LeftHand}/primary2DAxis");
            rStick = Add("rStick", "Vector2", "<XRController>{RightHand}/thumbstick", "<XRController>{RightHand}/trackpad", "<XRController>{RightHand}/primary2DAxis");
            lTrigger = Add("lTrigger", "Axis", "<XRController>{LeftHand}/trigger");
            rTrigger = Add("rTrigger", "Axis", "<XRController>{RightHand}/trigger");
            lGrip = Add("lGrip", "Axis", "<XRController>{LeftHand}/grip");
            rGrip = Add("rGrip", "Axis", "<XRController>{RightHand}/grip");
            a = Add("a", "Button", "<XRController>{RightHand}/primaryButton");
            b = Add("b", "Button", "<XRController>{RightHand}/secondaryButton");
            x = Add("x", "Button", "<XRController>{LeftHand}/primaryButton");
            y = Add("y", "Button", "<XRController>{LeftHand}/secondaryButton");
            menu = Add("menu", "Button", "<XRController>{LeftHand}/menu", "<XRController>{LeftHand}/menuButton");
            lClick = Add("lClick", "Button", "<XRController>{LeftHand}/thumbstickClicked", "<XRController>{LeftHand}/trackpadClicked", "<XRController>{LeftHand}/primary2DAxisClick");
            rClick = Add("rClick", "Button", "<XRController>{RightHand}/thumbstickClicked", "<XRController>{RightHand}/trackpadClicked", "<XRController>{RightHand}/primary2DAxisClick");
            map.Enable();
            pad = InputSystem.AddDevice<Gamepad>("VR Controllers");
            pointer = InputSystem.AddDevice<Mouse>("VR Pointer");
            InputSystem.onBeforeUpdate += Feed;
            Application.quitting += Uninstall;
        }

        static void Uninstall()
        {
            InputSystem.onBeforeUpdate -= Feed;
            Application.quitting -= Uninstall;
            if (map != null) { map.Disable(); map.Dispose(); map = null; }
            if (pad != null) { InputSystem.RemoveDevice(pad); pad = null; }
            if (pointer != null) { InputSystem.RemoveDevice(pointer); pointer = null; }
        }

        /// <summary>VrRig, each frame: where the laser hits the UI (screen pixels, origin bottom-left), the click and the
        /// scroll; 'hit' false = pointing at nothing (the virtual mouse stays where it was, buttons up).</summary>
        public static void SetPointer(bool hit, Vector2 screen, bool down, float scroll)
        {
            if (hit) pointerPosition = screen;
            pointerDown = hit && down;
            pointerScroll = hit ? scroll : 0f;
        }

        static float Read(InputAction action) => action != null ? action.ReadValue<float>() : 0f;
        static bool Pressed(InputAction action) => action != null && action.IsPressed();

        static void Feed()
        {
            if (pad == null || map == null) return;
            var s = new GamepadState
            {
                leftStick = lStick.ReadValue<Vector2>(),
                rightStick = rStick.ReadValue<Vector2>(),
                leftTrigger = Read(lTrigger),
                rightTrigger = Read(rTrigger),
            };
            s = s.WithButton(GamepadButton.LeftShoulder, GripsAsShoulders && Read(lGrip) > 0.55f)
                 .WithButton(GamepadButton.RightShoulder, GripsAsShoulders && Read(rGrip) > 0.55f)
                 .WithButton(GamepadButton.South, Pressed(a))
                 .WithButton(GamepadButton.East, Pressed(b))
                 .WithButton(GamepadButton.West, Pressed(x))
                 .WithButton(GamepadButton.North, Pressed(y))
                 .WithButton(GamepadButton.Start, Pressed(menu))
                 .WithButton(GamepadButton.Select, Pressed(lClick))
                 .WithButton(GamepadButton.DpadLeft, Pressed(rClick));
            InputSystem.QueueStateEvent(pad, s);
            if (pointer != null)
            {
                var m = new MouseState { position = pointerPosition, scroll = new Vector2(0f, pointerScroll) };
                m = m.WithButton(MouseButton.Left, pointerDown);
                InputSystem.QueueStateEvent(pointer, m);
            }
        }
    }
}
