// UiLayout.cs
// Remake: one scaling model for every UI Toolkit panel (the main menu, the station, the flight HUD, the star map, the
// overlays), so text and controls come out at a readable size on a 27" monitor, a 720p laptop, a Steam Deck, a TV and a
// phone, at any aspect ratio from 1:1 to 32:9. The rules and the numbers behind them are in docs/UI-DESIGN.md
// ("Scaling"); keep the two in step.
//
// The panel is laid out in units of a 1920 x 1080 design ("panel units", what the USS px mean). Each screen is scaled by
// one factor S:
//  - the device class: desktop 1, handheld (Steam Deck, a small Windows / Linux screen) 1.25, TV (an Xbox) 1.3, phone 1.2
//    (1.5 until 2026-10-23: on a 20:9 phone the main menu ran off the screen and everything read too big, the user's report);
//  - a pixel floor: the smallest text (the caption, 16 units) is never drawn under 11 real pixels (a 720p or 540p screen);
//  - the player's Options > Graphics "Interface size" (80..150 %) on top.
// The panel then shows 1080 / S units of height (PanelScreenMatchMode.Expand), but never fewer than MinWidth units of
// width: a 1:1 or 4:3 screen shrinks the whole UI until the 16:9 layouts' narrowest variant fits, instead of cutting it.
// The root gets the layout classes the USS adapts to (layout-narrow / -square / -ultrawide, layout-phone / -handheld /
// -tv, ui-large).

using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class UiLayout
    {
        public enum Device { Desktop, Handheld, Phone, Tv }

        /// <summary>The design's reference height and the narrowest width a screen's layout is drawn at (panel units).</summary>
        public const float RefHeight = 1080f, MinWidth = 1280f;
        /// <summary>The smallest text of the type scale (docs/UI-DESIGN.md "Type scale") and the fewest real pixels it may
        /// take on screen.</summary>
        public const float CaptionUnits = 16f, CaptionMinPixels = 11f;
        /// <summary>The body text and the smallest it should be physically on a phone / handheld (2.15 mm), for ui-large.</summary>
        const float BodyUnits = 20f, BodyMinInches = 0.085f;

        /// <summary>The type scale's sizes (GoF2Common.uss --text-caption .. --text-display, docs/UI-DESIGN.md "Type scale").</summary>
        public static readonly float[] TypeSizes = { 16f, 18f, 20f, 22f, 24f, 28f, 34f, 64f };

        /// <summary>A font size set from code, snapped up to the type scale (never under the caption): what the USS's
        /// var(--text-*) are for the style sheets.</summary>
        public static float Type(float px)
        {
            foreach (var t in TypeSizes) if (px <= t + 0.01f) return Mathf.Round(t * TextScale);
            return Mathf.Round(px * TextScale);
        }

        /// <summary>Remake: the Original interface style's sheets (Resources/GoF2UI/OriginalStyle.uss), added after the sheets
        /// on the screen's root and on every element under it that has its own (a template's Dialogue.uss). UI Toolkit lets
        /// a sheet attached lower in the tree win over the panel's theme whatever the specificity, so from the theme alone
        /// the Original rules lost every property a screen's own sheet sets too (sizes, positions, images).</summary>
        static void AttachOriginalSheets(VisualElement root)
        {
            if (originalSheet == null) originalSheet = Resources.Load<StyleSheet>("GoF2UI/OriginalStyle");
            if (originalSheet == null) return;
            AttachLast(root);
            root.Query<VisualElement>().ForEach(e => { if (e != root && e.styleSheets.count > 0) AttachLast(e); });
        }

        static void AttachLast(VisualElement e)
        {
            var set = e.styleSheets;
            if (set.count > 0 && set[set.count - 1] == originalSheet) return;
            if (set.Contains(originalSheet)) set.Remove(originalSheet);
            set.Add(originalSheet);
        }

        static StyleSheet originalSheet;

        /// <summary>Set by UwpDisplay on an Xbox (a TV across the room), or by the debug override.</summary>
        public static bool TvDetected;

        /// <summary>Changes whenever something the layout depends on changes (the options, the device override): the screens
        /// compare it each frame like the screen size and lay out again.</summary>
        public static int Revision => Settings.UiScalePercent * 1000 + Settings.UiWidth * 100 + Mathf.RoundToInt(Settings.UiMarginPercent * 10f)
                                      + (TvDetected ? 7 : 0) + DebugDevice() * 13 + Settings.UiTextScalePercent * 100000
                                      + (Settings.HighContrast ? 1 : 0) * 30000000 + (Settings.ReduceMotion ? 1 : 0) * 60000000
                                      + (Settings.ColorblindMarkers ? 1 : 0) * 120000000
                                      + (Settings.OriginalUi ? 1 : 0) * 240000000 + UiFont.Revision + settle * 7919;

        /// <summary>Modified: the first layout of a run could come too early (in the Editor's Play mode the panel kept its old
        /// scale until the screen size or an option changed: going full screen or picking the font again fixed it). A panel
        /// configured in the first 1.2 s after a scene loads is checked 0.3 s and 1.2 s in (real time, ScheduleSettleCheck): drawn at
        /// another size than Configure asked for, this changes, so every screen lays out once more. Was a change at both
        /// points every time: each one also refilled the station's news ticker (StationMenu.UpdateLayout), from the start.</summary>
        static int settle;

        static float sceneStart;

        static readonly float[] SettleChecks = { 0.3f, 1.2f };

        /// <summary>Configure's settle check (see settle) on 'root', whose panel should be 'expected' units large.</summary>
        static void ScheduleSettleCheck(VisualElement root, Vector2 expected)
        {
            int scene = sceneLoads;
            float t = Time.realtimeSinceStartup - sceneStart;
            foreach (float at in SettleChecks)
            {
                if (at <= t) continue;
                root.schedule.Execute(() =>
                {
                    if (scene != sceneLoads) return;   // another scene since: its own checks
                    var size = root.panel?.visualTree.layout.size ?? Vector2.zero;
                    if (root.panel == null) return;
                    // Expand mode's rounding (the reference resolution is whole units) stays well under 2 units.
                    if (!(Mathf.Abs(size.x - expected.x) < 2f && Mathf.Abs(size.y - expected.y) < 2f)) settle++;
                }).ExecuteLater((long)((at - t) * 1000f) + 1);
            }
        }

        static int sceneLoads;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void HookSceneLoads()
        {
            sceneStart = Time.realtimeSinceStartup;
            settle = 0;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            sceneStart = Time.realtimeSinceStartup;
            sceneLoads++;
        }

        /// <summary>Options > Accessibility "Text size" as a factor (1..2): the root's ui-text-N class (GoF2Common.uss) for the
        /// style sheets, Type for code.</summary>
        public static float TextScale => Settings.UiTextScalePercent / 100f;

        /// <summary>The text size classes GoF2Common.uss defines (ui-text-105 ...), each redefining the --text-* tokens.
        /// Modified (#237): finer steps (5 % up to 150) than the first 115 / 130 / 150 / 175 / 200, which were all either too
        /// big or too small on a TV; Options > Accessibility "Text size" offers 100 and these.</summary>
        public static readonly int[] TextSteps = { 105, 110, 115, 120, 125, 130, 135, 140, 145, 150, 160, 175, 190, 200 };

        /// <summary>The step of 'steps' nearest to 'value' (a stored percentage between two steps snaps to one), the lower
        /// on a tie; -1 for none.</summary>
        public static int NearestStep(int[] steps, int value)
        {
            int best = -1;
            for (int i = 0; i < steps.Length; i++)
                if (best < 0 || Mathf.Abs(steps[i] - value) < Mathf.Abs(steps[best] - value)) best = i;
            return best;
        }

        /// <summary>PlayerPrefs "debug_ui_device": 0 = detect, 1 desktop, 2 handheld, 3 phone, 4 TV (testing the layouts in
        /// the Editor at any Game view size; MainMenu.simulatePhone still forces a phone too).</summary>
        static int DebugDevice() => PlayerPrefs.GetInt("debug_ui_device", 0);

        /// <summary>The screen's diagonal in inches, 20 when the dpi isn't known.</summary>
        public static float Inches(float w, float h) => Screen.dpi > 0f ? Mathf.Sqrt(w * w + h * h) / Screen.dpi : 20f;

        public static Device Detect(float w, float h, bool forcePhone = false)
        {
            int debug = DebugDevice();
            if (debug >= 1 && debug <= 4) return (Device)(debug - 1);
            if (forcePhone) return Device.Phone;
            // UnityEngine.Device: the Device Simulator's phone in the Editor (UnityEngine.Application stays the Editor there).
            if (UnityEngine.Device.Application.isMobilePlatform) return Inches(w, h) < 7.5f ? Device.Phone : Device.Desktop;
            if (TvDetected) return Device.Tv;
            // Steam sets SteamDeck=1 on the Deck (GOF2_STEAMDECK=1 once SteamDeckRelaunch has restarted the Linux player
            // without it, #232); any other small desktop screen whose dpi says so counts too.
            if (System.Environment.GetEnvironmentVariable("SteamDeck") == "1"
                || System.Environment.GetEnvironmentVariable("GOF2_STEAMDECK") == "1") return Device.Handheld;
            if (Application.platform == RuntimePlatform.LinuxPlayer && Screen.dpi > 150f && Inches(w, h) < 9f) return Device.Handheld;
            return Device.Desktop;
        }

        public static float DeviceScale(Device d) => d switch
        {
            Device.Handheld => 1.25f,
            Device.Tv => 1.3f,
            Device.Phone => 1.2f,
            _ => 1f,
        };

        /// <summary>The factor S for a w x h pixel screen (see the header).</summary>
        public static float Scale(float w, float h, Device d)
        {
            float s = DeviceScale(d);
            float floor = CaptionMinPixels * RefHeight / (CaptionUnits * Mathf.Max(1f, h));   // the caption >= 11 px
            s = Mathf.Max(s, floor);
            s *= Settings.UiScalePercent / 100f;
            return Mathf.Clamp(s, 0.6f, 2.4f);
        }

        /// <summary>What Configure worked out for a screen.</summary>
        public struct Result
        {
            public Device device;
            public float scale;          // S
            public Vector2 panel;        // the panel's size in units
            public float pixelsPerUnit;  // real pixels per panel unit
        }

        /// <summary>
        /// Scales 'settings' (a screen's own PanelSettings copy) for a w x h pixel screen and puts the layout classes on
        /// 'root'. Call it again when the screen size, the safe area or Revision changes.
        /// </summary>
        public static Result Configure(PanelSettings settings, VisualElement root, float w, float h, bool forcePhone = false)
        {
            w = Mathf.Max(1f, w);
            h = Mathf.Max(1f, h);
            var d = Detect(w, h, forcePhone);
            float s = Scale(w, h, d);
            float refH = RefHeight / s;
            if (settings != null)
            {
                settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                settings.referenceResolution = new Vector2Int(Mathf.RoundToInt(MinWidth), Mathf.RoundToInt(refH));
                settings.screenMatchMode = PanelScreenMatchMode.Expand;
            }
            // Expand: the scale that keeps both the reference height and the minimum width on screen.
            float ppu = Mathf.Min(w / MinWidth, h / refH);
            // The effective S is ppu * RefHeight / h: below the asked one where the width limits it.
            var r = new Result { device = d, scale = ppu * RefHeight / h, panel = new Vector2(w / ppu, h / ppu), pixelsPerUnit = ppu };
            float aspect = w / h;
            if (root != null)
            {
                // ui-root: the body size and colour set again here, under the text size class (an inherited font-size is the
                // parent's computed size: from the panel's top element it ignored the class).
                root.AddToClassList("ui-root");
                root.EnableInClassList("layout-narrow", aspect < 1.55f);
                root.EnableInClassList("layout-square", aspect < 1.2f);
                root.EnableInClassList("layout-ultrawide", aspect > 2.3f);
                // Less than 1800 units of width: the menus' two columns don't both fit (the main menu hides its column
                // while a panel is open).
                // Larger text needs the room sooner (Options > Accessibility "Text size").
                root.EnableInClassList("layout-compact", r.panel.x < 1800f * Mathf.Max(1f, TextScale * 0.85f));
                root.EnableInClassList("layout-phone", d == Device.Phone);
                root.EnableInClassList("layout-handheld", d == Device.Handheld);
                root.EnableInClassList("layout-tv", d == Device.Tv);
                root.EnableInClassList("ui-large", Large(ppu));
                // Modified: the UI drawn larger than the 1080 design (a small screen, a handheld, Options "Interface size"):
                // the shop's item and ship icons don't grow with it (HangarWindow.uss), they took too much of the lists.
                root.EnableInClassList("ui-scale-up", r.scale >= 1.15f);
                root.EnableInClassList("ui-scale-up2", r.scale >= 1.4f);
                // Accessibility (docs/UI-DESIGN.md): the text size class, high contrast, reduced motion, colour-blind markers.
                int text = Settings.UiTextScalePercent;
                // The nearest class: a percentage between two steps (an old or hand-edited setting) still gets one.
                int nearest = text < 103 ? -1 : NearestStep(TextSteps, text);
                for (int i = 0; i < TextSteps.Length; i++) root.EnableInClassList("ui-text-" + TextSteps[i], i == nearest);
                root.EnableInClassList("ui-high-contrast", Settings.HighContrast);
                root.EnableInClassList("ui-reduce-motion", Settings.ReduceMotion);
                root.EnableInClassList("ui-colorblind", Settings.ColorblindMarkers);
                // Remake: Options > Graphics "Interface style" Original (GoF2Original.uss).
                root.EnableInClassList("ui-style-original", Settings.OriginalUi);
                AttachOriginalSheets(root);
                UiFont.Apply(root);   // Options > Accessibility "Interface font"
                // Modified: soon after a scene loads, checked again once it has been drawn (settle).
                if (settings != null && Time.realtimeSinceStartup - sceneStart < SettleChecks[SettleChecks.Length - 1])
                    ScheduleSettleCheck(root, r.panel);
            }
            return r;
        }

        /// <summary>Configure for a panel without a screen controller of its own (the FPS counter, the story step, the event
        /// screens): now, and again whenever the screen size or Revision changes (checked four times a second).</summary>
        public static void Keep(PanelSettings settings, VisualElement root)
        {
            if (root == null) return;
            var last = new Vector2Int(-1, -1);
            int rev = -1;
            void Check()
            {
                var rt = settings != null ? settings.targetTexture : null;
                var size = rt != null ? new Vector2Int(rt.width, rt.height) : new Vector2Int(Screen.width, Screen.height);
                if (size == last && rev == Revision) return;
                last = size;
                rev = Revision;
                Configure(settings, root, size.x, size.y);
            }
            Check();
            root.schedule.Execute(Check).Every(250);
        }

        /// <summary>The body text would still be under BodyMinInches on this screen at 'pixelsPerUnit' (a phone or
        /// handheld with a dense screen): the lists and buttons take their large variant (ui-large).</summary>
        public static bool Large(float pixelsPerUnit)
        {
            float dpi = Screen.dpi;
            if (dpi <= 0f || !UnityEngine.Device.Application.isMobilePlatform && DebugDevice() == 0) return false;
            return BodyUnits * pixelsPerUnit / dpi < BodyMinInches;
        }

        /// <summary>The extra insets in pixels (x left, y right, z top, w bottom) the options ask for on a w x h screen whose
        /// device insets are 'device': the TV edge margin (Settings.UiMarginPercent, by default 5 % on a TV) and the
        /// interface width (Settings.UiWidth: 16:9 / 21:9 / full; the HUD and menus stay in a centred band on an
        /// ultrawide screen). Added to the safe area by SafeArea.Pixels.</summary>
        public static Vector4 OptionInsets(float w, float h, Vector4 device)
        {
            float m = Settings.UiMarginPercent / 100f;
            if (m < 0f) m = Detect(w, h) == Device.Tv ? 0.05f : 0f;
            var p = new Vector4(Mathf.Max(device.x, w * m), Mathf.Max(device.y, w * m), Mathf.Max(device.z, h * m), Mathf.Max(device.w, h * m));
            float cap = Settings.UiWidth switch { Settings.UiWidth16x9 => 16f / 9f, Settings.UiWidth21x9 => 21.5f / 9f, _ => 0f };
            if (cap > 0f)
            {
                float inner = w - p.x - p.y, allowed = (h - p.z - p.w) * cap;
                if (inner > allowed)
                {
                    float extra = (inner - allowed) * 0.5f;
                    p.x += extra;
                    p.y += extra;
                }
            }
            return p;
        }
    }
}
