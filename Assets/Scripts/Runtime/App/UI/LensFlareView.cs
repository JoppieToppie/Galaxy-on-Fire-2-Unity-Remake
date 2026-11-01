// LensFlareView.cs
// The 2D lens flare (StarSystem::render2D 0x15dfc0 -> LensFlare::render2D 0x1421c0; Reference/research/space_backdrop.md,
// per-frame rendering): seven images from gof2_interface.png (ids 1288-1290: a big hexagon, a small hexagon, a glow)
// along the line from the screen centre through the sun, plus a full-screen glare, all tinted with the system's flare
// colour and alpha blended. No occlusion test (it shows behind stations and ships, like the original). Drawn under the
// HUD. Intensity I = K * (1 - d / (H / 2)) from Backdrop (K 64, 80 for colour 5 = Ginoya).
//   # image  t      size   alpha
//   1 img0   0.5    1.0w   70 + I
//   2 img0   0.25   0.75w  70 + I
//   3 img1   0.75   0.5w   70 + I
//   4 img0   0.125  1.25w  40 + I (only if I > -40)
//   5 img1   1/11   0.5w   70 + I
//   6 img2   -0.75  2.0w   70 + I
//   7 img2   -0.2   0.5w   70 + I
//   glare    full screen   int(I) (only if I > 0)
// w = 64 px of the original's 768-high HD canvas (scaled to the panel height here; the HD scaling is unverified).
// The original blends in gamma space; the remake's linear colour space blends the UI in linear, where the glare's 25 %
// white turned dark space into a 54 % grey. The alphas are raised to 2.2 (GammaAlpha) so over a dark sky they give the
// original's brightness.

using GoF2Remake.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public class LensFlareView
    {
        static readonly (int image, float t, float size, float alphaBase)[] Elements =
        {
            (0, 0.5f, 1f, 70f), (0, 0.25f, 0.75f, 70f), (1, 0.75f, 0.5f, 70f), (0, 0.125f, 1.25f, 40f),
            (1, 1f / 11f, 0.5f, 70f), (2, -0.75f, 2f, 70f), (2, -0.2f, 0.5f, 70f),
        };
        /// <summary>DAT_00258530 / 510 / 4f0 by colour index: blue, green, red, white, yellow, white (Ginoya).</summary>
        static readonly Color32[] Colors =
        {
            new Color32(200, 200, 255, 255), new Color32(200, 255, 200, 255), new Color32(255, 200, 200, 255),
            new Color32(255, 255, 255, 255), new Color32(255, 255, 200, 255), new Color32(255, 255, 255, 255),
        };
        const float ImageWidth = 64f, CanvasHeight = 768f;

        readonly VisualElement layer, glare;
        readonly VisualElement[] images = new VisualElement[Elements.Length];

        public LensFlareView(VisualElement root)
        {
            layer = new VisualElement { pickingMode = PickingMode.Ignore };
            layer.AddToClassList("lens-flare");
            glare = new VisualElement { pickingMode = PickingMode.Ignore };
            glare.AddToClassList("lens-flare-glare");
            layer.Add(glare);
            var tex = new[] { Tex("flare_0"), Tex("flare_1"), Tex("flare_2") };
            for (int i = 0; i < Elements.Length; i++)
            {
                var e = new VisualElement { pickingMode = PickingMode.Ignore };
                e.AddToClassList("lens-flare-image");
                if (tex[Elements[i].image] != null) e.style.backgroundImage = new StyleBackground(tex[Elements[i].image]);
                layer.Add(e);
                images[i] = e;
            }
            root.Insert(0, layer);   // under the HUD
        }

        static Texture2D Tex(string name) => Resources.Load<Texture2D>("GoF2Hud/" + name);

        public void Update(Backdrop backdrop, bool hidden)
        {
            bool show = !hidden && backdrop != null && backdrop.FlareVisible && layer.panel != null;
            layer.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            var size = layer.layout.size;
            if (size.x <= 0f || size.y <= 0f) return;
            float I = backdrop.FlareIntensity;
            var colour = (Color)Colors[Mathf.Clamp(backdrop.FlareColor, 0, Colors.Length - 1)];
            // The sun's screen position (pixels, origin bottom-left) in panel coordinates (origin top-left).
            var sun = RuntimePanelUtils.ScreenToPanel(layer.panel, new Vector2(backdrop.SunScreen.x, Screen.height - backdrop.SunScreen.y));
            sun -= layer.worldBound.position;
            var centre = size * 0.5f;
            float w = ImageWidth * size.y / CanvasHeight;
            for (int i = 0; i < Elements.Length; i++)
            {
                var el = Elements[i];
                var e = images[i];
                float alpha = el.alphaBase + I;
                bool on = alpha > 0f && (i != 3 || I > -40f);
                e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                if (!on) continue;
                float s = el.size * w;
                var p = centre + (sun - centre) * el.t;
                e.style.left = p.x - s * 0.5f;
                e.style.top = p.y - s * 0.5f;
                e.style.width = s;
                e.style.height = s;
                e.style.unityBackgroundImageTintColor = new Color(colour.r, colour.g, colour.b, GammaAlpha(Mathf.Min(alpha, 255f) / 255f) * backdrop.flareShown);
            }
            bool glareOn = I > 0f;
            glare.style.display = glareOn ? DisplayStyle.Flex : DisplayStyle.None;
            if (glareOn) glare.style.backgroundColor = new Color(colour.r, colour.g, colour.b, GammaAlpha((int)I / 255f) * backdrop.flareShown);
        }

        /// <summary>A gamma-space blend's alpha for the linear-space UI (the same result over black).</summary>
        static float GammaAlpha(float a) => QualitySettings.activeColorSpace == ColorSpace.Linear ? Mathf.Pow(a, 2.2f) : a;
    }
}
