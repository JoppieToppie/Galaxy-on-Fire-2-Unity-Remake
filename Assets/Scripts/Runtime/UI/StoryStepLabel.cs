// StoryStepLabel.cs
// Remake-only: a small line at the bottom right of every in-game scene naming the current story step ("Step 23: <title>",
// StepSummaries; "Free play" without a story), the campaign and the station (its name and index), so a bug report's
// screenshot shows where the player was. Not in the main menu, nor in photo mode / Action Freeze (FpsCounter.Suppressed).
// Created by Bootstrap and kept for the whole run; its own panel like FpsCounter (the star map's panel settings, sorted
// over the HUDs and menus), never picking (taps go through to the touch controls under it).

using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class StoryStepLabel : MonoBehaviour
    {
        const float RefreshSeconds = 0.5f;

        static StoryStepLabel instance;
        PanelRenderer panelRenderer;
        PanelSettings runtimePanel;
        VisualTreeAsset emptyTree;
        Label label;
        float nextRefresh;
        /// <summary>The station names, cached by index (from the level's own Database: no second load).</summary>
        readonly System.Collections.Generic.Dictionary<int, string> stationNames = new System.Collections.Generic.Dictionary<int, string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        public static void Install()
        {
            if (instance != null) return;
            var assets = StarMapAssets.Load();
            if (assets == null || assets.panelSettings == null) return;
            var go = new GameObject("StoryStepLabel");
            go.SetActive(false);
            DontDestroyOnLoad(go);
            instance = go.AddComponent<StoryStepLabel>();
            instance.runtimePanel = Instantiate(assets.panelSettings);
            instance.runtimePanel.sortingOrder = assets.panelSettings.sortingOrder + 99;   // over the HUDs, menus and the map
            instance.runtimePanel.referenceResolution = new Vector2Int(1920, 1080);
            instance.runtimePanel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            instance.runtimePanel.match = 1f;
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
        }

        void OnDisable() => panelRenderer?.UnregisterUIReloadCallback(OnUIReload);

        void OnDestroy()
        {
            if (runtimePanel != null) Destroy(runtimePanel);
            if (emptyTree != null) Destroy(emptyTree);
            if (instance == this) instance = null;
        }

        void OnUIReload(PanelRenderer renderer, VisualElement root, int version)
        {
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            label = new Label { pickingMode = PickingMode.Ignore };
            var s = label.style;
            s.position = Position.Absolute;
            s.right = 4;
            s.bottom = 2;
            s.fontSize = 12;
            s.color = new Color(0.85f, 0.92f, 0.97f, 0.6f);
            s.backgroundColor = new Color(0f, 0f, 0f, 0.35f);
            s.paddingLeft = s.paddingRight = 4;
            s.paddingTop = s.paddingBottom = 1;
            s.borderTopLeftRadius = s.borderTopRightRadius = s.borderBottomLeftRadius = s.borderBottomRightRadius = 2;
            s.unityTextAlign = TextAnchor.MiddleRight;
            s.display = DisplayStyle.None;
            root.Add(label);
            nextRefresh = 0f;
        }

        /// <summary>In a game scene (not the main menu) and not hidden for a photo.</summary>
        static bool Wanted() => !FpsCounter.Suppressed && SceneManager.GetActiveScene().buildIndex > 0;

        string Text()
        {
            string step;
            if (Session.FreePlay) step = Localization.Extra("storyStepFreePlay", "Free play");
            else
            {
                int i = Story.Index;
                string title = StepSummaries.Get(i)?.title;
                step = string.IsNullOrEmpty(title) ? string.Format(Localization.Extra("storyStepIndex", "Step {0}"), i)
                                                   : string.Format(Localization.Extra("storyStepTitled", "Step {0}: {1}"), i, title);
                if (Session.Campaign != Campaign.GalaxyOnFire2) step = $"{Session.Campaign} · {step}";
            }
            int station = Session.StationIndex;
            string stationName = StationName(station);
            string where = string.IsNullOrEmpty(stationName) ? $"#{station}" : $"{stationName} #{station}";
            return $"{step}  ·  {where}";
        }

        string StationName(int station)
        {
            if (stationNames.TryGetValue(station, out string cached)) return cached;
            var space = FindAnyObjectByType<World.SpaceLevel>();
            var db = space != null ? space.Database : FindAnyObjectByType<World.StationLevel>()?.Database;
            if (db == null) return null;
            string name = db.Stations.Find(st => st.index == station)?.name;
            stationNames[station] = name;
            return name;
        }

        void Update()
        {
            if (label == null) return;
            var want = Wanted() ? DisplayStyle.Flex : DisplayStyle.None;
            if (label.style.display != want) label.style.display = want;
            if (want == DisplayStyle.None || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            string text = Text();
            if (label.text != text) label.text = text;
        }
    }
}
