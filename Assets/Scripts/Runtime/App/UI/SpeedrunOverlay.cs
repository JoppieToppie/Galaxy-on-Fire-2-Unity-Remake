// SpeedrunOverlay.cs
// Remake-only (the speedrun timer, docs/notes/speedrun.md): the run on screen, LiveSplit-like: the category, a window of
// five splits around the running one (each finished split's lead / loss on the comparison, green ahead, red behind, gold
// for a best segment; the running one highlighted with its live delta and the comparison's time; the next ones with the
// comparison's times), the big timer (green / red against the comparison, gold for a new personal best) and the
// comparison's name. Shown while a run is on or just finished, in every scene but the startup logos, not in photo mode
// (FpsCounter.Suppressed); placed by Settings.SpeedrunPosition, clear of the safe area. It also runs the run's clocks
// (Speedrun.Tick: the frame after a scene load counts as loading, its delta being the load) and starts / stops
// LiveSplitLink with its option. Created by Bootstrap and kept for the whole run; its own panel like FpsCounter.

using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class SpeedrunOverlay : MonoBehaviour
    {
        const int Rows = 5;
        const float Edge = 2f, RowRefreshSeconds = 0.1f;
        static readonly Color Ahead = new Color(0.36f, 0.86f, 0.42f), Behind = new Color(0.95f, 0.35f, 0.3f),
                              Gold = new Color(1f, 0.8f, 0.2f), Text = new Color(0.9f, 0.94f, 0.98f), Dim = new Color(0.9f, 0.94f, 0.98f, 0.55f);

        static SpeedrunOverlay instance;
        PanelRenderer panelRenderer;
        PanelSettings runtimePanel;
        VisualTreeAsset emptyTree;
        VisualElement root, bar, box;
        Label category, timer, footer;
        readonly VisualElement[] rows = new VisualElement[Rows];
        readonly Label[] names = new Label[Rows], deltas = new Label[Rows], times = new Label[Rows];
        int placedAt = -1;
        Vector4 placedInsets;
        double lastTick = -1;
        bool sceneLoaded;
        float nextRows;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        public static void Install()
        {
            if (instance != null) return;
            Speedrun.CompareLabel = OptionsCatalog.SpeedrunCompareLabel;
            var assets = StarMapAssets.Load();
            if (assets == null || assets.panelSettings == null) return;
            var go = new GameObject("SpeedrunOverlay");
            go.SetActive(false);
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SpeedrunOverlay>();
            instance.runtimePanel = Instantiate(assets.panelSettings);
            instance.runtimePanel.sortingOrder = assets.panelSettings.sortingOrder + 98;   // over the HUDs, menus and the map
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
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDisable()
        {
            panelRenderer?.UnregisterUIReloadCallback(OnUIReload);
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnDestroy()
        {
            if (runtimePanel != null) Destroy(runtimePanel);
            if (emptyTree != null) Destroy(emptyTree);
            if (instance == this) instance = null;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => sceneLoaded = true;

        static Label MakeLabel(float size, Color color, TextAnchor align)
        {
            var l = new Label { pickingMode = PickingMode.Ignore };
            var s = l.style;
            s.fontSize = UiLayout.Type(size);
            s.color = color;
            s.unityTextAlign = align;
            s.marginLeft = s.marginRight = s.marginTop = s.marginBottom = 0;
            s.paddingLeft = s.paddingRight = s.paddingTop = s.paddingBottom = 0;
            return l;
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

            box = new VisualElement { pickingMode = PickingMode.Ignore };
            var b = box.style;
            b.width = 236;
            b.backgroundColor = new Color(0.02f, 0.04f, 0.07f, 0.7f);
            b.paddingLeft = b.paddingRight = 6;
            b.paddingTop = b.paddingBottom = 4;
            b.borderTopLeftRadius = b.borderTopRightRadius = b.borderBottomLeftRadius = b.borderBottomRightRadius = 3;
            b.display = DisplayStyle.None;
            bar.Add(box);

            category = MakeLabel(11, Dim, TextAnchor.MiddleLeft);
            category.style.marginBottom = 2;
            category.style.overflow = Overflow.Hidden;
            category.style.textOverflow = TextOverflow.Ellipsis;
            category.style.whiteSpace = WhiteSpace.NoWrap;
            box.Add(category);
            for (int i = 0; i < Rows; i++)
            {
                var row = rows[i] = new VisualElement { pickingMode = PickingMode.Ignore };
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.paddingLeft = row.style.paddingRight = 2;
                row.style.borderTopLeftRadius = row.style.borderTopRightRadius = row.style.borderBottomLeftRadius = row.style.borderBottomRightRadius = 2;
                names[i] = MakeLabel(12, Text, TextAnchor.MiddleLeft);
                names[i].style.flexGrow = 1;
                names[i].style.flexShrink = 1;
                names[i].style.overflow = Overflow.Hidden;
                names[i].style.textOverflow = TextOverflow.Ellipsis;
                names[i].style.whiteSpace = WhiteSpace.NoWrap;
                deltas[i] = MakeLabel(12, Text, TextAnchor.MiddleRight);
                deltas[i].style.width = 58;
                deltas[i].style.flexShrink = 0;
                times[i] = MakeLabel(12, Text, TextAnchor.MiddleRight);
                times[i].style.width = 62;
                times[i].style.flexShrink = 0;
                row.Add(names[i]);
                row.Add(deltas[i]);
                row.Add(times[i]);
                box.Add(row);
            }
            timer = MakeLabel(26, Text, TextAnchor.MiddleRight);
            timer.style.unityFontStyleAndWeight = FontStyle.Bold;
            timer.style.marginTop = 2;
            box.Add(timer);
            footer = MakeLabel(11, Dim, TextAnchor.MiddleLeft);
            footer.style.overflow = Overflow.Hidden;
            footer.style.textOverflow = TextOverflow.Ellipsis;
            footer.style.whiteSpace = WhiteSpace.NoWrap;
            box.Add(footer);
            nextRows = 0f;
        }

        /// <summary>Like FpsCounter.Place: a bar over the screen (or its height) aligns the box to the chosen spot.</summary>
        void Place()
        {
            int at = Settings.SpeedrunPosition;
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
            s.justifyContent = !top && !bottom ? Justify.Center : Justify.FlexStart;
        }

        void Update()
        {
            // The clocks: real time between frames; the first frame after a scene load holds the load (LoadScene finishes at
            // the start of the next frame, before this Update).
            double now = Time.realtimeSinceStartupAsDouble;
            if (lastTick >= 0) Speedrun.Tick((now - lastTick) * 1000.0, sceneLoaded);
            lastTick = now;
            sceneLoaded = false;

            if (Settings.SpeedrunTimer && Settings.SpeedrunLiveSplit) LiveSplitLink.Begin();
            else LiveSplitLink.Stop();

            if (box == null) return;
            bool want = Settings.SpeedrunTimer && Speedrun.Current != Speedrun.State.Off && Speedrun.Route != null && !FpsCounter.Suppressed
                        && SceneManager.GetActiveScene().name != "Splash";
            var display = want ? DisplayStyle.Flex : DisplayStyle.None;
            if (box.style.display != display) box.style.display = display;
            if (!want) return;
            Place();

            Set(timer, Speedrun.Format(Speedrun.Time), TimerColor());
            if (Time.unscaledTime < nextRows) return;
            nextRows = Time.unscaledTime + RowRefreshSeconds;
            Speedrun.RefreshComparison();
            var route = Speedrun.Route;
            Set(category, $"{route.Category} · {route.difficulty}", Dim);
            string clock = Speedrun.UsesGameTime ? X("speedrunTimingGame", "Without loading") : X("speedrunTimingReal", "Real time");
            string extra = Settings.SpeedrunLiveSplit && LiveSplitLink.Connected ? "  ·  LiveSplit" : "";
            Set(footer, Speedrun.NewPersonalBest ? $"{X("speedrunNewPb", "New personal best!")}  ·  {clock}"
                                                 : $"{Speedrun.ComparisonName}  ·  {clock}{extra}", Speedrun.NewPersonalBest ? Gold : Dim);
            int count = route.segments.Count, current = Speedrun.Segment;
            int first = Mathf.Clamp(current - (Rows - 2), 0, Mathf.Max(0, count - Rows));
            for (int r = 0; r < Rows; r++)
            {
                int i = first + r;
                if (i >= count) { rows[r].style.display = DisplayStyle.None; continue; }
                rows[r].style.display = DisplayStyle.Flex;
                bool running = i == current && Speedrun.Current == Speedrun.State.Running;
                rows[r].style.backgroundColor = running ? new Color(0.25f, 0.45f, 0.8f, 0.35f) : Color.clear;
                Set(names[r], route.segments[i].name, i < current || running ? Text : Dim);
                if (i < current)
                {
                    double? split = Speedrun.SplitAt(i), delta = Speedrun.Delta(i);
                    Set(deltas[r], delta != null ? Speedrun.FormatDelta(delta.Value) : "", Speedrun.IsGold(i) ? Gold : delta < 0 ? Ahead : Behind);
                    Set(times[r], split != null ? Speedrun.Format(split.Value) : "—", Text);
                }
                else
                {
                    double? live = running ? Speedrun.LiveDelta() : null, compare = Speedrun.CompareAt(i);
                    Set(deltas[r], live != null ? Speedrun.FormatDelta(live.Value) : "", live < 0 ? Ahead : Behind);
                    Set(times[r], compare != null ? Speedrun.Format(compare.Value) : "—", Dim);
                }
            }
        }

        static string X(string key, string english) => Localization.Extra(key, english);

        /// <summary>The timer's colour: gold for a new personal best, green / red against the comparison, else plain.</summary>
        static Color TimerColor()
        {
            if (Speedrun.Current == Speedrun.State.Finished)
            {
                if (Speedrun.NewPersonalBest) return Gold;
                double? d = Speedrun.Delta(Speedrun.Route.segments.Count - 1);
                return d == null ? Text : d < 0 ? Ahead : Behind;
            }
            double? compare = Speedrun.CompareAt(Speedrun.Segment);
            return compare == null ? Text : Speedrun.Time <= compare ? Ahead : Behind;
        }

        static void Set(Label label, string text, Color color)
        {
            if (label.text != text) label.text = text;
            if (label.style.color != color) label.style.color = color;
        }
    }
}
