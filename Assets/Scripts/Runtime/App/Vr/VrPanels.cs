// VrPanels.cs
// Remake VR: every UI Toolkit panel (the scene's menu or HUD, the star map's overlay, the multiplayer screen texts, the FPS
// counter) renders into its own RenderTexture of the window's size instead of the screen, shown on a quad of the floating
// screen (VrRig places it); stacked by the panels' sorting order. The textures keep the window's size so the UI's own
// screen <-> panel conversions (markers projected through the scene camera) stay exactly as on the desktop, and a laser
// hit at (u, v) on the screen is the window pixel (u * width, v * height) (VrPad's virtual mouse).
// PanelSettings assets get their target back when the scene ends (the Editor would keep the change otherwise).

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.Vr
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class VrPanels
    {
        sealed class Entry
        {
            public PanelSettings settings;
            public RenderTexture texture;
            public RenderTexture previous;
            public bool previousClear;
            public Color previousClearColor;
            public GameObject quad;
            public Material material;
            public Rect crop = new Rect(0f, 0f, 1f, 1f);
            public PanelRenderer renderer;
        }

        readonly Transform screen;
        readonly int layer;
        readonly List<Entry> entries = new List<Entry>();
        Vector2Int size;
        float scanTimer;

        /// <param name="screen">the floating screen: a unit quad's frame (VrRig scales it)</param>
        public VrPanels(Transform screen, int layer)
        {
            this.screen = screen;
            this.layer = layer;
        }

        /// <summary>Each frame: new panels picked up (twice a second), the textures follow the window's size.</summary>
        public void Update()
        {
            var now = new Vector2Int(Mathf.Max(64, Screen.width), Mathf.Max(64, Screen.height));
            if (now != size)
            {
                size = now;
                foreach (var e in entries) Resize(e);
            }
            // A panel shows only while its renderer runs: its texture keeps the last frame drawn (#205: the star map's
            // overlay, whose object goes when the map closes, stayed over the main menu).
            foreach (var e in entries)
            {
                bool shown = Runs(e.renderer);
                if (e.quad != null && e.quad.activeSelf != shown) e.quad.SetActive(shown);
            }
            if ((scanTimer -= Time.unscaledDeltaTime) > 0f) return;
            scanTimer = 0.5f;
            foreach (var r in Object.FindObjectsByType<PanelRenderer>())
            {
                if (r == null || r.panelSettings == null) continue;
                var known = entries.Find(e => e.settings == r.panelSettings);
                if (known == null) { Add(r.panelSettings); known = entries[entries.Count - 1]; }
                if (known.renderer == null || (!Runs(known.renderer) && Runs(r))) known.renderer = r;
            }
            entries.RemoveAll(e => e.settings == null);
            foreach (var e in entries)
                if (e.quad != null) e.quad.transform.localPosition = new Vector3(e.crop.center.x - 0.5f, e.crop.center.y - 0.5f, -0.001f * Order(e));
        }

        /// <summary>The renderer draws its panel (a Renderer, not a Behaviour: no isActiveAndEnabled).</summary>
        static bool Runs(PanelRenderer r) => r != null && r.enabled && r.gameObject.activeInHierarchy;

        static void Gone(Object o) { if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }

        static int Order(Entry e) => e.settings != null ? Mathf.Clamp((int)e.settings.sortingOrder, -1000, 1000) : 0;

        void Add(PanelSettings settings)
        {
            var e = new Entry
            {
                settings = settings,
                previous = settings.targetTexture,
                previousClear = settings.clearColor,
                previousClearColor = settings.colorClearValue,
            };
            e.quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            e.quad.name = "VR Panel " + settings.name;
            Gone(e.quad.GetComponent<Collider>());
            e.quad.layer = layer;
            e.quad.transform.SetParent(screen, false);
            e.material = ScreenMaterial(3100 + Order(e));
            e.quad.GetComponent<MeshRenderer>().sharedMaterial = e.material;
            e.quad.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            entries.Add(e);
            Resize(e);
            if (crops.TryGetValue(settings, out var uv)) { e.crop = uv; ApplyCrop(e); }
        }

        void Resize(Entry e)
        {
            if (e.settings == null) return;
            var old = e.texture;
            e.texture = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32) { name = "VR " + e.settings.name };
            e.texture.Create();
            e.settings.clearColor = true;
            e.settings.colorClearValue = new Color(0f, 0f, 0f, 0f);
            e.settings.targetTexture = e.texture;
            if (e.material != null) e.material.SetTexture("_BaseMap", e.texture);
            if (old != null) { old.Release(); Gone(old); }
        }

        /// <summary>A material made from Resources/GoF2Vr/'name' (a template asset: its shader and keywords are in every
        /// build), else from the shader by name. #115: Shader.Find("Universal Render Pipeline/Unlit") was null in the Windows
        /// build (nothing else uses Unlit, so it was stripped): VrRig.Init threw and the VR UI never appeared.</summary>
        public static Material FromTemplate(string name, string shader)
        {
            var template = Resources.Load<Material>("GoF2Vr/" + name);
            if (template != null) return new Material(template);
            var s = Shader.Find(shader);
            if (s == null) { Debug.LogError($"VR: no material template {name} and no shader {shader} in this build"); s = Shader.Find("Hidden/InternalErrorShader"); }
            return new Material(s);
        }

        /// <summary>A screen quad's material: URP Unlit, transparent (alpha blended), no depth writes, drawn in 'queue' order
        /// (the template VrScreen is set up that way already; set again for the shader-by-name fallback).</summary>
        public static Material ScreenMaterial(int queue)
        {
            var m = FromTemplate("VrScreen", "Universal Render Pipeline/Unlit");
            m.SetFloat("_Surface", 1f);   // transparent
            m.SetFloat("_Blend", 0f);     // alpha
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = queue;
            return m;
        }

        /// <summary>The texture 'settings' renders into (null = not one of these panels).</summary>
        public RenderTexture TextureOf(PanelSettings settings)
        {
            var e = entries.Find(x => x.settings == settings);
            return e != null ? e.texture : null;
        }

        /// <summary>The panel's quad is on the screen.</summary>
        internal bool Shows(PanelSettings settings)
        {
            var e = entries.Find(x => x.settings == settings);
            return e != null && e.quad != null && e.quad.activeSelf;
        }

        /// <summary>Shows only 'uv' (0..1, bottom-left origin) of that panel, in its place on the screen (the cockpit's canopy
        /// HUD: the corners are on the displays).</summary>
        public void Crop(PanelSettings settings, Rect uv)
        {
            crops[settings] = uv;   // kept for a panel not picked up yet
            var e = entries.Find(x => x.settings == settings);
            if (e == null || e.quad == null) return;
            e.crop = uv;
            ApplyCrop(e);
        }

        readonly Dictionary<PanelSettings, Rect> crops = new Dictionary<PanelSettings, Rect>();

        static void ApplyCrop(Entry e)
        {
            var uv = e.crop;
            var p = e.quad.transform.localPosition;
            e.quad.transform.localPosition = new Vector3(uv.center.x - 0.5f, uv.center.y - 0.5f, p.z);
            e.quad.transform.localScale = new Vector3(uv.width, uv.height, 1f);
            e.material.SetTextureScale("_BaseMap", uv.size);
            e.material.SetTextureOffset("_BaseMap", uv.position);
        }

        /// <summary>A UI element (not a full-screen backdrop) is under (u, v) of the screen: the laser clicks the UI there,
        /// else it points past the screen into the world (VrStation: the ship, the visitors).</summary>
        public bool UiAt(Vector2 uv)
        {
            foreach (var e in entries)
            {
                var root = e.settings != null ? VisualTreeOf(e.settings) : null;
                var panel = root != null ? root.panel : null;
                if (panel == null || !e.quad.activeInHierarchy) continue;
                // Inside this panel's shown part only (the cropped canopy HUD).
                if (!e.crop.Contains(uv)) continue;
                var size = panel.visualTree.layout.size;
                if (size.x <= 0f || size.y <= 0f) continue;
                var picked = panel.Pick(new Vector2(uv.x * size.x, (1f - uv.y) * size.y));
                if (picked == null || picked == panel.visualTree) continue;
                var b = picked.worldBound;
                if (b.width > size.x * 0.9f && b.height > size.y * 0.9f) continue;   // a full-screen layer (gesture zone, backdrop)
                return true;
            }
            return false;
        }

        // PanelSettings' runtime panel root (internal: PanelSettings.visualTree), for picking under the laser.
        static readonly System.Reflection.PropertyInfo visualTreeProperty =
            typeof(PanelSettings).GetProperty("visualTree", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        static VisualElement VisualTreeOf(PanelSettings settings)
        {
            try { return visualTreeProperty != null ? visualTreeProperty.GetValue(settings) as VisualElement : null; }
            catch (System.Exception) { return null; }
        }

        /// <summary>The screen's aspect (the window's).</summary>
        public float Aspect => size.y > 0 ? (float)size.x / size.y : 16f / 9f;

        /// <summary>The scene ends: the panels draw to the screen again, the textures go.</summary>
        public void Release()
        {
            foreach (var e in entries)
            {
                if (e.settings != null)
                {
                    e.settings.targetTexture = e.previous;
                    e.settings.clearColor = e.previousClear;
                    e.settings.colorClearValue = e.previousClearColor;
                }
                if (e.texture != null) { e.texture.Release(); Gone(e.texture); }
                if (e.material != null) Gone(e.material);
            }
            entries.Clear();
        }
    }
}
