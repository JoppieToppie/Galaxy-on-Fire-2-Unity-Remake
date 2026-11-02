// PhotoSettings.cs
// Remake-only: the photo modes' camera settings (Action Freeze in flight, Inspect ship in the hangar; UI.PhotoTools shows
// them in its Camera settings panel). Kept for the run, shared by both modes, so a look set up in flight is there in the
// hangar too; Reset puts them back. The field of view starts at the camera's own each time a photo mode opens. Plain C#.

using UnityEngine;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class PhotoSettings
    {
        /// <summary>Depth of field: the game's own (none in flight, the hangar's option there), none, sharp on the ship with the
        /// rest blurred, or a focus distance of the player's.</summary>
        public enum DofMode { Game, Off, Ship, Manual }

        /// <summary>Bloom: the game's own (the Graphics option), none at all, or the photo mode's own amount. Anything
        /// but Game also holds the original bloom pass back (Visuals.ClassicBloomPass.Suppressed), which no volume
        /// reaches.</summary>
        public enum BloomMode { Game, Off, Custom }

        /// <summary>The sun's lens flare (LensFlareView, flight only): the game's own option, off or on for the photo.</summary>
        public enum FlareMode { Game, Off, On }

        public const float MinFov = 10f, MaxFov = 110f;   // vertical, degrees
        public const float MaxFocusMetres = 3000f;
        public static readonly float[] FrameAspects = { 0f, 16f / 9f, 21f / 9f, 4f / 3f, 1f, 9f / 16f };
        public static readonly string[] FrameNames = { "", "16:9", "21:9", "4:3", "1:1", "9:16" };

        public static bool Free;
        public static float Fov, DefaultFov = 60f;
        public static float Roll;            // degrees, + counter-clockwise, -180..180
        public static float Speed = 1f;      // the free camera's, x
        public static DofMode Dof;
        public static float Focus = 0.5f;    // 0..1: 1 .. MaxFocusMetres, exponential (FocusMetres)
        public static float Blur = 0.5f;     // 0..MaxBlur: 1 = URP's depth of field at its most, past it Visuals.PhotoBlurPass
        public const float MaxBlur = 5f;
        public static float Exposure;        // EV
        public static float Contrast;        // -50..50
        public static float Saturation;      // -100..100
        public static float Vignette;        // 0..0.6
        public static float Grain;           // 0..1
        public static BloomMode Bloom;
        public static float BloomAmount = 1f;   // URP Bloom's intensity, over the game's
        public static FlareMode Flare;
        public static float Temperature;     // -100..100, warmer +
        public static float Tint;            // -100..100, magenta +
        public static float Aberration;      // 0..1
        public static float Distortion;      // -1..1, barrel +
        public static bool HideShip;
        public static PhotoGuides.Mode Guides;   // the composition guides over the frame
        public static int GuideCorner;       // the spiral's / triangles' direction (PhotoGuides.Lines)
        public static int Frame;             // FrameAspects
        public static int Size;              // the screenshot: 0 = the screen's size, 1 = twice it, 2 = four times

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Reset(); Free = false; DefaultFov = 60f; Fov = DefaultFov; }

        /// <summary>Everything back to the defaults (the mode, orbit or free, stays).</summary>
        public static void Reset()
        {
            Fov = DefaultFov;
            Roll = 0f;
            Speed = 1f;
            Dof = DofMode.Game;
            Focus = 0.5f;
            Blur = 0.5f;
            Exposure = Contrast = Saturation = Vignette = Grain = 0f;
            Bloom = BloomMode.Game;
            BloomAmount = 1f;
            Flare = FlareMode.Game;
            Temperature = Tint = Aberration = Distortion = 0f;
            HideShip = false;
            Guides = PhotoGuides.Mode.Off;
            GuideCorner = 0;
            Frame = 0;
            Size = 0;
        }

        /// <summary>Whether the sun's lens flare is drawn: the Graphics option, or the photo mode's own answer while
        /// one is open ('photo': Action Freeze is on).</summary>
        public static bool FlareShown(bool photo) =>
            photo && Flare != FlareMode.Game ? Flare == FlareMode.On : Data.Settings.LensFlare;

        /// <summary>The manual focus distance in metres.</summary>
        public static float FocusMetres => FocusMetresOf(Focus);

        /// <summary>A focus setting (0..1) in metres, and back.</summary>
        public static float FocusMetresOf(float focus) => Mathf.Max(1f, Mathf.Pow(MaxFocusMetres, Mathf.Clamp01(focus)));
        public static float FocusOfMetres(float metres) => Mathf.Clamp01(Mathf.Log(Mathf.Max(1f, metres)) / Mathf.Log(MaxFocusMetres));

        /// <summary>The white balance's temperature (URP's -100..100) as the kelvin a photo editor shows: the light the
        /// picture is corrected for, so a higher value is warmer. URP's ColorUtils.ColorBalanceToLMSCoeffs moves the white
        /// point's x to 0.31271 - t (t = temperature / 65, x 0.1 below 0, x 0.05 above) on the daylight locus (its
        /// StandardIlluminantY); McCamy's formula gives that point's colour temperature: 6504 K at 0 (D65), about 2600 K at
        /// -100 and 35 800 K at +100.</summary>
        public static float Kelvin(float temperature)
        {
            float t = temperature / 65f;
            float x = 0.31271f - t * (t < 0f ? 0.1f : 0.05f);
            float y = 2.87f * x - 3f * x * x - 0.27509507f;
            float n = (x - 0.3320f) / (0.1858f - y);
            return ((449f * n + 3525f) * n + 6823.3f) * n + 5520.33f;
        }

        /// <summary>The temperature (-100..100) of a kelvin value, the inverse of Kelvin (it rises with it).</summary>
        public static float TemperatureOfKelvin(float kelvin)
        {
            float lo = -100f, hi = 100f;
            if (kelvin <= Kelvin(lo)) return lo;
            if (kelvin >= Kelvin(hi)) return hi;
            for (int i = 0; i < 40; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Kelvin(mid) < kelvin) lo = mid; else hi = mid;
            }
            return (lo + hi) * 0.5f;
        }

        /// <summary>A kelvin value as the panel shows it: to 50 K, above 10 000 K to 100 K.</summary>
        public static string KelvinText(float temperature)
        {
            float k = Kelvin(temperature);
            float step = k >= 10000f ? 100f : 50f;
            return $"{Mathf.RoundToInt(Mathf.Round(k / step) * step)} K";
        }

        public const int MaxShotPixels = 16384;   // a texture's largest side

        /// <summary>ScreenCapture's superSize for Size on a w x h screen: 1, 2 or 4, less when the picture would pass
        /// MaxShotPixels (a 4K screen at x4).</summary>
        public static int SuperSize(int w, int h)
        {
            int want = Size >= 2 ? 4 : Size == 1 ? 2 : 1;
            int fit = Mathf.Max(1, MaxShotPixels / Mathf.Max(1, Mathf.Max(w, h)));
            return Mathf.Clamp(Mathf.Min(want, fit), 1, 4);
        }

        public static float FrameAspect => Frame > 0 && Frame < FrameAspects.Length ? FrameAspects[Frame] : 0f;

        /// <summary>The part of a w x h picture the frame keeps, centred (pixels; aspect 0 = all of it).</summary>
        public static RectInt FrameRect(int w, int h, float aspect)
        {
            if (aspect <= 0f || w <= 0 || h <= 0) return new RectInt(0, 0, Mathf.Max(0, w), Mathf.Max(0, h));
            if ((float)w / h > aspect)
            {
                int fw = Mathf.Clamp(Mathf.RoundToInt(h * aspect), 1, w);
                return new RectInt((w - fw) / 2, 0, fw, h);
            }
            int fh = Mathf.Clamp(Mathf.RoundToInt(w / aspect), 1, h);
            return new RectInt(0, (h - fh) / 2, w, fh);
        }
    }
}
