// MouseWheel.cs
// The mouse wheel as notches, for everything that steps with it (the throttle, the free-look, photo mode and Inspect ship
// zooms). A notch reads 1 (the Input System's uniform scroll) or 120 (raw): one step either way.
// Remake (Discord, Egg, 2026-10-30): a USB mouse on a Samsung tablet goes through the sec_touchpad driver, which sends a
// stream of fractional scroll events while the mouse merely moves (at most 0.625 in their log of 1,045, every real notch
// 1 or more), so the throttle drifted with the mouse steering. On phones and tablets a frame's scroll under 0.75 is no
// notch; a smooth-scrolling wheel or a touchpad's scroll there steps only once it gathers a frame's worth. Desktops keep
// every scroll.

using UnityEngine;
using UnityEngine.InputSystem;

namespace GoF2Remake.UI
{
    public static class MouseWheel
    {
        /// <summary>The smallest frame's scroll on a phone or tablet that counts as a notch.</summary>
        public const float MobileNotch = 0.75f;

        /// <summary>This frame's wheel, -1..1 (0 without a mouse or a notch).</summary>
        public static float Steps(Mouse mouse) =>
            mouse == null ? 0f : Steps(mouse.scroll.ReadValue().y, Application.isMobilePlatform);

        /// <summary>A frame's vertical scroll as steps, -1..1: on mobile, under MobileNotch is none.</summary>
        public static float Steps(float scroll, bool mobile) =>
            mobile && Mathf.Abs(scroll) < MobileNotch ? 0f : Mathf.Clamp(scroll, -1f, 1f);
    }
}
