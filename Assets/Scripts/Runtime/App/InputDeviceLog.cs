// InputDeviceLog.cs
// Remake (#194: "Controller not working" on a Steam Deck in Game Mode, nothing more to go on): the player's log says which
// input devices the Input System sees and as what (a Gamepad is what the game reads; a device that came up as a plain
// Joystick, as an SDL device on Linux can, is never read), as they come and go, and when the window gains or loses the
// focus (the Input System drops a controller's input while the window is unfocused: InputSettings.backgroundBehavior).
// Also the Steam Deck / gamescope environment (SteamDeck, XDG_CURRENT_DESKTOP, GAMESCOPE_WAYLAND_DISPLAY). Players only:
// not in the Editor, not on a dedicated server.

using UnityEngine;
using UnityEngine.InputSystem;

namespace GoF2Remake
{
    public static class InputDeviceLog
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            if (Application.isEditor || Multiplayer.DedicatedServer.Enabled) return;
            string Env(string name) => System.Environment.GetEnvironmentVariable(name) ?? "-";
            Debug.Log($"Input: {Application.platform}, Steam Deck {Env("SteamDeck")} (relaunched {Env(SteamDeckRelaunch.Marker)}), desktop {Env("XDG_CURRENT_DESKTOP")}, " +
                      $"gamescope {Env("GAMESCOPE_WAYLAND_DISPLAY")}, run in background {Application.runInBackground}, " +
                      $"unfocused {InputSystem.settings.backgroundBehavior}, focused {Application.isFocused}");
            foreach (var d in InputSystem.devices) Log("present", d);
            InputSystem.onDeviceChange += (d, change) =>
            {
                if (change == InputDeviceChange.Added || change == InputDeviceChange.Removed || change == InputDeviceChange.Disabled
                    || change == InputDeviceChange.Enabled || change == InputDeviceChange.Reconnected)
                    Log(change.ToString().ToLowerInvariant(), d);
            };
            Application.focusChanged += focused => Debug.Log($"Input: window {(focused ? "focused" : "unfocused")}");
        }

        static void Log(string what, InputDevice d)
        {
            if (d is Keyboard || d is Mouse) return;   // every computer has them
            var desc = d.description;
            string kind = d is Gamepad ? "a gamepad" : d is Joystick ? "a joystick, not read as a controller" : d.GetType().Name;
            Debug.Log($"Input: {what} {d.layout} \"{desc.product}\" ({desc.interfaceName}, {desc.manufacturer}), {kind}");
        }
    }
}
