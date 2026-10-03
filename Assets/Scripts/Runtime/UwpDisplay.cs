// UwpDisplay.cs
// UWP only: render at the display's real pixel size. A UWP window's size is in view pixels (DIPs), and the player sized its
// back buffer by them: on an Xbox with a 4K TV (the CoreWindow is always 1920 x 1080 view pixels there) and on a scaled
// desktop display the game rendered at 1920 x 1080 and was stretched ("Resolution: Switching to 1920 x 1080" in the
// log). The real size comes from WinRT on the UI thread: the window bounds x DisplayInformation.RawPixelsPerViewPixel,
// and on an Xbox the HDMI output mode (HdmiDisplayInformation). Xbox consoles below the One X / Series X keep 1080p
// (too slow for 4K). Applied on the app thread with Screen.SetResolution, which on UWP sizes the back buffer only; again
// when the window changes size. The Render scale option still scales the 3D on top of it.

#if UNITY_WSA && ENABLE_WINMD_SUPPORT && !UNITY_EDITOR
using UnityEngine;

namespace GoF2Remake
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class UwpDisplay
    {
        static bool hooked;

        public static void Install()
        {
            UnityEngine.WSA.Application.InvokeOnUIThread(() =>
            {
                if (!hooked)
                {
                    hooked = true;
                    try
                    {
                        Windows.UI.Core.CoreWindow.GetForCurrentThread().SizeChanged += (s, e) => Measure();
                        Windows.Graphics.Display.DisplayInformation.GetForCurrentView().DpiChanged += (s, e) => Measure();
                    }
                    catch (System.Exception ex) { Debug.LogWarning($"UwpDisplay: no window events ({ex.Message})"); }
                }
                Measure();
            }, false);
        }

        /// <summary>UI thread: the window's size in raw pixels, or the HDMI mode on an Xbox.</summary>
        static void Measure()
        {
            int w = 0, h = 0;
            string source = "window";
            try
            {
                var bounds = Windows.UI.Core.CoreWindow.GetForCurrentThread().Bounds;
                double scale = Windows.Graphics.Display.DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;
                w = (int)System.Math.Round(bounds.Width * scale);
                h = (int)System.Math.Round(bounds.Height * scale);
            }
            catch (System.Exception ex) { Debug.LogWarning($"UwpDisplay: no display information ({ex.Message})"); }

            string device = "";
            try { device = Windows.System.Profile.AnalyticsInfo.DeviceForm ?? ""; } catch { }
            bool xbox = device.StartsWith("Xbox");
            if (xbox)
            {
                try
                {
                    var mode = Windows.Graphics.Display.Core.HdmiDisplayInformation.GetForCurrentView()?.GetCurrentDisplayMode();
                    if (mode != null && (int)mode.ResolutionWidthInRawPixels * (int)mode.ResolutionHeightInRawPixels > w * h)
                    {
                        w = (int)mode.ResolutionWidthInRawPixels;
                        h = (int)mode.ResolutionHeightInRawPixels;
                        source = "HDMI mode";
                    }
                }
                catch (System.Exception ex) { Debug.LogWarning($"UwpDisplay: no HDMI information ({ex.Message})"); }
                // Xbox One / One S / Series S: 4K is beyond their GPU, they stay at 1080p.
                bool fourK = device == "Xbox One X" || device == "Xbox Series X";
                if (!fourK && h > 1080) { w = w * 1080 / h; h = 1080; source += ", capped to 1080p"; }
            }

            if (w <= 0 || h <= 0) return;
            int width = w, height = h;
            string from = source, form = device;
            UnityEngine.WSA.Application.InvokeOnAppThread(() => Apply(width, height, from, form), false);
        }

        /// <summary>App thread: the real size, when the player's own differs.</summary>
        static void Apply(int w, int h, string source, string device)
        {
            if (Screen.width == w && Screen.height == h) return;
            Debug.Log($"UwpDisplay: {Screen.width} x {Screen.height} -> {w} x {h} ({source}, device {device})");
            Screen.SetResolution(w, h, Screen.fullScreenMode);
        }
    }
}
#endif
