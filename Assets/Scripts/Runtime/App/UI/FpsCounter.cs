// FpsCounter.cs
// Remake-only: the frame rate and frame time (Options > Graphics "Show FPS", Settings.ShowFps), over every scene and menu,
// in a corner or the middle of a side (Settings.FpsPosition, top centre by default; #137), clear of the safe area's insets. Averaged over half a second of real time, so a paused game still counts. Created by Bootstrap and
// kept for the whole run; its own panel (the star map's panel settings, sorted above everything else).

using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class FpsCounter : MonoBehaviour
    {
        const float WindowSeconds = 0.5f;

        static FpsCounter instance;
        /// <summary>Hidden for now (Action Freeze: the screenshot shows the scene only).</summary>
        public static bool Suppressed;
        PanelRenderer panelRenderer;
        PanelSettings runtimePanel;
        VisualTreeAsset emptyTree;
        Label label;
        VisualElement bar, root;
        int placedAt = -1;
        Vector4 placedInsets;
        int frames;
        float elapsed;

        public static void Install()
        {
            if (instance != null) return;
            var assets = StarMapAssets.Load();
            if (assets == null || assets.panelSettings == null) return;
            var go = new GameObject("FpsCounter");
            go.SetActive(false);
            DontDestroyOnLoad(go);
            instance = go.AddComponent<FpsCounter>();
            instance.runtimePanel = Instantiate(assets.panelSettings);
            instance.runtimePanel.sortingOrder = assets.panelSettings.sortingOrder + 100;   // over the HUDs, menus and the map
            instance.emptyTree = ScriptableObject.CreateInstance<VisualTreeAsset>();
            var pr = go.AddComponent<PanelRenderer>();
            pr.panelSettings = instance.runtimePanel;
            pr.visualTreeAsset = instance.emptyTree;
            go.SetActive(true);
        }

        void OnEnable()
        {
            panelRenderer = GetComponent<PanelRenderer>();
            panelRenderer.RegisterUIReloadCallback(OnUIReload);
            Settings.Changed += Apply;
        }

        void OnDisable()
        {
            panelRenderer?.UnregisterUIReloadCallback(OnUIReload);
            Settings.Changed -= Apply;
        }

        void OnDestroy()
        {
            if (runtimePanel != null) Destroy(runtimePanel);
            if (emptyTree != null) Destroy(emptyTree);
            if (instance == this) instance = null;
        }

        void OnUIReload(PanelRenderer renderer, VisualElement rootElement, int version)
        {
            root = rootElement;
            UiLayout.Keep(runtimePanel, root);   // docs/UI-DESIGN.md "Scaling"
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.style.position = Position.Absolute;
            root.Add(bar);
            placedAt = -1;
            label = new Label { pickingMode = PickingMode.Ignore };
            var s = label.style;
            s.fontSize = UiLayout.Type(15);
            s.color = new Color(0.85f, 1f, 0.85f);
            s.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            s.paddingLeft = s.paddingRight = 6;
            s.paddingTop = s.paddingBottom = 2;
            s.borderTopLeftRadius = s.borderTopRightRadius = s.borderBottomLeftRadius = s.borderBottomRightRadius = 3;
            bar.Add(label);
            Apply();
        }

        void Apply()
        {
            if (label != null) label.style.display = Settings.ShowFps && !Suppressed ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Gap to the screen's (safe area's) edge, panel units.</summary>
        const float Edge = 2f;

        /// <summary>The bar spans the screen (or its height, on the sides) and aligns the label to the chosen spot.</summary>
        void Place()
        {
            int at = Settings.FpsPosition;
            var insets = SafeArea.Panel(root, Screen.width, Screen.height, false);
            if (at == placedAt && insets == placedInsets) return;
            placedAt = at;
            placedInsets = insets;
            var s = bar.style;
            s.left = insets.x + Edge;
            s.right = insets.y + Edge;
            bool top = at <= Settings.FpsTopRight, bottom = at >= Settings.FpsBottomLeft;
            s.top = top || !bottom ? insets.z + Edge : StyleKeyword.Auto;
            s.bottom = bottom || !top ? insets.w + Edge : StyleKeyword.Auto;
            bool left = at == Settings.FpsTopLeft || at == Settings.FpsLeft || at == Settings.FpsBottomLeft;
            bool right = at == Settings.FpsTopRight || at == Settings.FpsRight || at == Settings.FpsBottomRight;
            s.alignItems = left ? Align.FlexStart : right ? Align.FlexEnd : Align.Center;
            s.justifyContent = !top && !bottom ? Justify.Center : Justify.FlexStart;   // the sides: the middle of the height
        }

        void Update()
        {
            if (label == null) return;
            var want = Settings.ShowFps && !Suppressed ? DisplayStyle.Flex : DisplayStyle.None;
            if (label.style.display != want) label.style.display = want;
            if (want == DisplayStyle.None) return;
            Place();
            frames++;
            elapsed += Time.unscaledDeltaTime;
            if (elapsed < WindowSeconds) return;
            float fps = frames / elapsed;
            // With frame generation the frames on the screen, then the rendered ones and how many are shown for each.
            float shown = FrameGeneration.PresentedPerFrame;
            string text = shown > 1.05f
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0} FPS ({1:0} × {2:0.#})  {3:0.0} ms", fps * shown, fps, shown, 1000f / Mathf.Max(fps, 0.001f))
                : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0} FPS  {1:0.0} ms", fps, 1000f / Mathf.Max(fps, 0.001f));
            // And the PC latency Reflex measures (simulation start to the end of the GPU's work), where it reports one.
            float latency = FrameGeneration.LatencyMs;
            if (latency > 0.05f) text += string.Format(System.Globalization.CultureInfo.InvariantCulture, "  PCL {0:0} ms", latency);
            label.text = text;
            frames = 0;
            elapsed = 0f;
        }
    }
}
