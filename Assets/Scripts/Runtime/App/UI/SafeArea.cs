// SafeArea.cs
// Remake-only: the device's safe area (Screen.safeArea) as insets of a UI Toolkit panel, shared by every screen that keeps
// its controls clear of the display cutouts: the iPhone's notch / Dynamic Island (landscape: the same inset on the left and
// the right, whichever side the island is on, iOS reports both), its home indicator (the bottom inset), the rounded
// corners, Android's punch holes. Screen.safeArea is in pixels with y up; the insets come out in panel units.
// Screen.safeArea can reach past Screen.width / height on some Android phones (display cutouts / insets): it is clamped to
// the screen, and a safe area under half the screen either way is taken for nonsense and ignored.

using System;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class SafeArea
    {
        /// <summary>The insets in pixels of a w x h screen: x left, y right, z top, w bottom: the device's cutouts, the TV
        /// edge margin and the interface width's side bands (UiLayout.OptionInsets). None for a panel drawn into a texture
        /// (VR, the item window): that texture has no cutouts.</summary>
        public static Vector4 Pixels(float w, float h, bool offscreen)
        {
            if (offscreen || w <= 0f || h <= 0f) return Vector4.zero;
            return UiLayout.OptionInsets(w, h, Device(w, h));
        }

        /// <summary>The device's own insets alone (the notch, the home indicator, punch holes), as Options > Graphics
        /// "Camera cutout" (Settings.ScreenCutout) asks.</summary>
        public static Vector4 Device(float w, float h) => Device(Screen.safeArea, w, h, Settings.ScreenCutout);

        /// <summary>The insets of the safe area 'raw' (pixels, y up) on a w x h screen for a Settings.ScreenCutout value:
        /// kept clear (as reported), mirrored (left and right both the larger: Android insets only the camera hole's side,
        /// which put the HUD's left half off centre), or none (the whole screen).</summary>
        public static Vector4 Device(Rect raw, float w, float h, int cutout)
        {
            if (cutout == Settings.ScreenCutoutFull) return Vector4.zero;
            var sa = Rect.MinMaxRect(Mathf.Clamp(raw.xMin, 0f, w), Mathf.Clamp(raw.yMin, 0f, h),
                                     Mathf.Clamp(raw.xMax, 0f, w), Mathf.Clamp(raw.yMax, 0f, h));
            if (sa.width < w * 0.5f || sa.height < h * 0.5f) return Vector4.zero;   // nonsense: ignore
            var p = new Vector4(sa.xMin, w - sa.xMax, h - sa.yMax, sa.yMin);
            if (cutout == Settings.ScreenCutoutBothSides) p.x = p.y = Mathf.Max(p.x, p.y);
            return p;
        }

        /// <summary>The insets in 'root''s panel units (root laid out over the whole w-pixel-wide screen); zero while it has
        /// no size yet.</summary>
        public static Vector4 Panel(VisualElement root, float w, float h, bool offscreen)
        {
            if (root == null || !(root.layout.width > 0f) || w <= 0f) return Vector4.zero;   // also catches NaN
            return Pixels(w, h, offscreen) * (root.layout.width / w);
        }

        /// <summary>How Apply puts the insets on its target.</summary>
        public enum Mode
        {
            /// <summary>The left / right / top / bottom of an absolutely placed element (the HUD's and the station's).</summary>
            Position,
            /// <summary>The margins of a flex child stretched across its parent (the main menu's, which keeps a 100 % width
            /// and a max width for its 21:9 centring while there are no insets). Not padding: absolutely placed children
            /// are laid out against their parent's padding box, so padding never moved them.</summary>
            Margins,
        }

        /// <summary>
        /// Puts the insets on 'target' once 'root' is laid out (its size is NaN on the first frame: until then it retries
        /// every frame; a NaN inset collapses the UI into the top-left corner). 'applied' gets the pixel insets used.
        /// </summary>
        public static void Apply(VisualElement root, VisualElement target, float w, float h, bool offscreen, Mode mode,
                                 Action<Vector4> applied = null)
        {
            if (root == null || target == null) return;
            // The pixels -> units conversion reads the root's laid-out width, which a new scale (a resolution change, the
            // Interface size option) only reaches a layout pass later: done again whenever the root's size changes, or the
            // insets were the old scale's (a TV's 5 % margin came out three times too wide after a 32:9 screen).
            if (!pendingByRoot.TryGetValue(root, out var state))
            {
                state = new Pending();
                pendingByRoot.Add(root, state);
                root.RegisterCallback<GeometryChangedEvent>(e =>
                {
                    if (e.oldRect.width != e.newRect.width && state.again != null) state.again();
                });
            }
            state.again = () => Apply(root, target, w, h, offscreen, mode, applied);
            root.schedule.Execute(() =>
            {
                if (!(root.layout.width > 0f)) { Apply(root, target, w, h, offscreen, mode, applied); return; }
                var px = Pixels(w, h, offscreen);
                var p = px * (root.layout.width / w);
                var s = target.style;
                if (mode == Mode.Margins)
                {
                    s.marginLeft = p.x;
                    s.marginRight = p.y;
                    s.marginTop = p.z;
                    s.marginBottom = p.w;
                    // With insets the box stretches between them (its USS width of 100 % would overflow by the margins);
                    // without, the USS's own width and centring apply again.
                    bool inset = p != Vector4.zero;
                    s.width = inset ? new StyleLength(StyleKeyword.Auto) : new StyleLength(StyleKeyword.Null);
                    s.alignSelf = inset ? new StyleEnum<Align>(Align.Stretch) : new StyleEnum<Align>(StyleKeyword.Null);
                }
                else
                {
                    s.left = p.x;
                    s.right = p.y;
                    s.top = p.z;
                    s.bottom = p.w;
                }
                applied?.Invoke(px);
            }).ExecuteLater(1);
        }

        sealed class Pending { public Action again; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, Pending> pendingByRoot =
            new System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, Pending>();

        /// <summary>The class an edge-anchored element of a full-screen overlay carries to be kept clear of the cutouts
        /// (KeepClear): the overlay itself stays full screen (photo mode's frame bars must match the screenshot).</summary>
        public const string EdgeClass = "safe-edge";

        /// <summary>
        /// Keeps the EdgeClass descendants of a full-screen overlay (photo mode, Inspect ship: their titles, hints, footers
        /// and side panels) clear of the cutouts through their margins, which absolutely placed elements are offset by;
        /// checked when the overlay is laid out and twice a second (the safe area turns with the device, elements come and go). None in VR (the
        /// panels are drawn into textures there).
        /// </summary>
        public static void KeepClear(VisualElement overlay)
        {
            if (overlay == null) return;
            void Refresh()
            {
                if (overlay.panel == null || !(overlay.layout.width > 0f)) return;
                var p = Panel(overlay, Screen.width, Screen.height, Vr.VrMode.Enabled);
                // Every time (elements added since get theirs too); an unchanged inline value isn't laid out again.
                overlay.Query(className: EdgeClass).ForEach(e =>
                {
                    var s = e.style;
                    s.marginLeft = p.x;
                    s.marginRight = p.y;
                    s.marginTop = p.z;
                    s.marginBottom = p.w;
                });
            }
            overlay.RegisterCallback<GeometryChangedEvent>(_ => Refresh());
            overlay.schedule.Execute(Refresh).Every(500);
        }
    }
}
