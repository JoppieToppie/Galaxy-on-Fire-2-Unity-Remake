// BenchmarkRunner.cs
// Remake (2026-11-20, the user's request; docs/notes/options.md "Benchmark"): the Graphics tab's "Benchmark" (main menu
// only). It flies a fixed list of scenes in a throwaway game (Data.Benchmark.Active: nothing is saved, god mode, the frame
// rate limit lifted), each from the same seed with the ship flown by the runner along a path set by the clock (as a
// cutscene moves it: ShipController.externalControl, whatever the flight style), so two runs show the same thing:
// a station's orbit, a dogfight of 10 against 10 and one of 50 against 50, a fleet battle and three at once, an effects
// run (160 ships blown up row by row around the ship: explosions, wrecks, crates) and a hangar. In the Editor the Game
// view's own V-Sync is off for the run too (it held a run at the display's rate). A scene loads, settles for WarmupSeconds, then every frame's
// time is kept (the real clock) with Unity's frame timings (FrameTimingManager: main thread, render thread, GPU) while a
// box in the corner shows the numbers live. Back in the main menu the results page: the averages and lows, what limited
// the frames, the system, the graphics settings, the frame rate over the run and a row per scene; it is laid out to be
// the picture to share ("Save image": the page without its buttons, to the pictures folder). The numbers are also written
// to <data>/Benchmarks as JSON with every frame's time as CSV. Esc / B ends a run early (nothing kept).
// The page also suggests option changes for a higher frame rate (the user's request), from what limited the scenes:
// the GPU: an upscaler (DLSS, FSR, XeSS at Quality), else a lower render scale and no MSAA, or the next upscaler
// quality down; the CPU: a shorter detail distance, levels of detail on, fire and smoke off. "Apply suggestions"
// sets them; "Apply and run again" also flies the run again with the same runner. None when every scene already ran
// faster than the display.
// The player controls nothing meanwhile (the user's request): the game's controls are off (GameControls.Suspend), the
// flight input waits (Navigation.InputHalted while Benchmark.Active: steering, guns, the free-look camera), the menus
// ignore their keys (GameControls.ModalOpen) and the runner's own panel, above everything like the FPS counter, takes
// the pointer. Only Esc / B is read, from the devices.
// Memory (the user's request): the most the process had in use during a scene, from Unity's memory counter
// (ProfilerRecorder "System Used Memory"), and its video memory (VideoMemory: Windows' count; Unity's "Gfx Used Memory"
// reads 0 in a release build), read four times a second with the live box: the counters run for that one frame only
// (running, Unity gathers its memory numbers at every frame's end, which would be part of what is measured).
// Scripted runs: the -benchmark argument starts a run at the main menu and quits when its results are written.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.World;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class BenchmarkRunner : MonoBehaviour
    {
        const float WarmupSeconds = 3f;
        const int Seed = 20101027;
        const string MenuScene = "MainMenu";

        sealed class Step
        {
            public string key, english, scene;
            public int station;
            public float seconds;
            /// <summary>The ship's speed along its path, m/s.</summary>
            public float speed;
            public Action<SpaceLevel> spawn;
            /// <summary>Called four times a second while the scene is measured, with the seconds since its start.</summary>
            public Action<SpaceLevel, float> tick;
        }

        // Dis (Terran), Kallsta Omba (Midorian), Nehébru (Nivelian): plain station orbits under three skies.
        static readonly Step[] Steps =
        {
            new Step { key = "benchOrbit", english = "Station orbit", scene = "Space", station = 70, seconds = 20f, speed = 150f },
            new Step { key = "benchDogfight", english = "Dogfight", scene = "Space", station = 20, seconds = 20f, speed = 40f, spawn = level => Dogfight(level, 1) },
            new Step { key = "benchDogfightLarge", english = "Large dogfight", scene = "Space", station = 20, seconds = 20f, speed = 40f, spawn = level => Dogfight(level, 5) },
            new Step { key = "benchFleet", english = "Fleet battle", scene = "Space", station = 0, seconds = 25f, speed = 100f, spawn = level => Fleets(level, false) },
            new Step { key = "benchFleetLarge", english = "Large fleet battle", scene = "Space", station = 0, seconds = 25f, speed = 100f, spawn = level => Fleets(level, true) },
            new Step { key = "benchEffects", english = "Effects", scene = "Space", station = 70, seconds = 20f, speed = 60f, spawn = Effects,
                       tick = (level, seconds) => DebugSpawner.KillAround(level, 300f + seconds * 200f, DebugSpawner.KillFilter.Ships) },
            new Step { key = "benchHangar", english = "Hangar", scene = "Station", station = 70, seconds = 15f },
        };

        /// <summary>'rows' lines of ten pirates to the right and ten Terran fighters to the left, from 400 m ahead, each line
        /// behind and above the last (the pirates shoot at the player too: god mode).</summary>
        static void Dogfight(SpaceLevel level, int rows)
        {
            var p = level.Player.transform;
            for (int row = 0; row < rows; row++)
            {
                var centre = p.position + p.forward * (400f + row * 120f) + p.up * (40f + row * 50f);
                DebugSpawner.SpawnShip(level, Standing.Pirate, 2, DebugSpawner.Behaviour.Hostile, 10, centre + p.right * 330f);
                DebugSpawner.SpawnShip(level, 0, 0, DebugSpawner.Behaviour.Friendly, 10, centre - p.right * 330f);
            }
        }

        /// <summary>A fleet battle (two capital ships and their wings) 1.8 km ahead, flown into after 18 s; 'large': two more
        /// behind it to the left and right.</summary>
        static void Fleets(SpaceLevel level, bool large)
        {
            var p = level.Player.transform;
            DebugSpawner.SpawnFleetBattle(level, p.position + p.forward * 1800f);
            if (!large) return;
            DebugSpawner.SpawnFleetBattle(level, p.position + p.forward * 4500f - p.right * 3500f);
            DebugSpawner.SpawnFleetBattle(level, p.position + p.forward * 4500f + p.right * 3500f + p.up * 600f);
        }

        // (hulls of the races' fighters; 13-15 are freighters and the battleship)
        static readonly int[] EffectHulls = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 16, 17, 18 };

        /// <summary>The effects run: 16 rows of ten neutral ships, 250 m apart from 400 m ahead; the scene's tick destroys
        /// what is inside a range that grows 200 m a second, about a row a second: explosions, wrecks and crates all the way.</summary>
        static void Effects(SpaceLevel level)
        {
            var p = level.Player.transform;
            for (int row = 0; row < EffectHulls.Length; row++)
                DebugSpawner.SpawnShip(level, row % 4, EffectHulls[row], DebugSpawner.Behaviour.Neutral, 10,
                                       p.position + p.forward * (400f + row * 250f) + p.up * ((row % 3 - 1) * 80f));
        }

        static BenchmarkRunner instance;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly Color Cyan = new Color(0.24f, 0.78f, 1f), Amber = new Color(1f, 0.65f, 0.19f), Ink = new Color(0.85f, 0.93f, 0.97f),
                              Dim = new Color(0.85f, 0.93f, 0.97f, 0.6f), Line = new Color(0.24f, 0.78f, 1f, 0.25f), Back = new Color(0.015f, 0.04f, 0.07f, 1f);

        PanelRenderer panelRenderer;
        PanelSettings runtimePanel;
        VisualTreeAsset emptyTree;
        VisualElement root, live, results, footer, suggestBox;
        Label liveTitle, liveFps, liveDetail, status;
        VisualElement liveBar;
        FpsGraph liveGraph;
        readonly List<Button> buttons = new List<Button>();
        readonly List<Action> actions = new List<Action>();
        readonly List<float> liveFpsValues = new List<float>();
        int selected;

        int step;
        bool flying, inScene, resultsPending, savingImage;
        readonly List<float> sceneMs = new List<float>(16384), allMs = new List<float>(65536);
        readonly List<int> sceneStarts = new List<int>();   // where each scene's frames start in allMs (the CSV)
        readonly FrameTiming[] timing = new FrameTiming[1];
        readonly int[] sceneLimits = new int[6], allLimits = new int[6];
        double sceneCpu, sceneGpu, allCpu, allGpu, sceneShown, sceneRender, scenePresentWait, sceneStartWait, allStartWait;
        int sceneTimed, allTimed;
        ProfilerRecorder ramRecorder, vramRecorder;
        float sceneRam, sceneVram;
        bool memoryAsked;
        string display;
        Benchmark.Report report;
        string reportPath, imagePath;

        sealed class Suggestion { public string text; public Action apply; }
        readonly List<Suggestion> suggestions = new List<Suggestion>();
        string suggestionNote;
        bool suggested, applied;

        static string X(string key, string english) => Localization.Extra(key, english);
        static string F(float value, string format = "0") => value.ToString(format, Inv);

        static string Gb(float mb) => mb > 0f ? F(mb / 1024f, "0.0") + " GB" : "–";
        static float Mb(ProfilerRecorder recorder) => recorder.Valid ? recorder.LastValue / 1048576f : 0f;

        /// <summary>The memory counters on for one frame: its end fills them, the next frame reads them (TakeMemory).</summary>
        void AskMemory()
        {
            ramRecorder.Start();
            vramRecorder.Start();
            memoryAsked = true;
        }

        void TakeMemory()
        {
            memoryAsked = false;
            sceneRam = Mathf.Max(sceneRam, Mb(ramRecorder));
            // (Windows' own count of the process's video memory; elsewhere Unity's counter, which only the Editor and
            // development builds fill)
            float vram = VideoMemory.UsedMb();
            sceneVram = Mathf.Max(sceneVram, vram > 0f ? vram : Mb(vramRecorder));
            ramRecorder.Stop();
            vramRecorder.Stop();
        }

        /// <summary>A run is on or its results are up.</summary>
        public static bool Open => instance != null;

        /// <summary>The -benchmark argument: the main menu starts a run by itself and the game quits when its results are
        /// written (<data>/Benchmarks), for runs from a script.</summary>
        public static readonly bool AutoRun = Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, "-benchmark", StringComparison.OrdinalIgnoreCase));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        /// <summary>Starts a run from the main menu: the throwaway game of the first scene is set up; returns the scene to
        /// load (null when a run is on already or the panel can't be made).</summary>
        public static string Begin()
        {
            if (instance != null) return null;
            var assets = StarMapAssets.Load();
            if (assets == null || assets.panelSettings == null) return null;
            var go = new GameObject("BenchmarkRunner");
            go.SetActive(false);
            DontDestroyOnLoad(go);
            instance = go.AddComponent<BenchmarkRunner>();
            instance.runtimePanel = Instantiate(assets.panelSettings);
            instance.runtimePanel.sortingOrder = assets.panelSettings.sortingOrder + 90;   // over the menus, under the FPS counter
            instance.emptyTree = ScriptableObject.CreateInstance<VisualTreeAsset>();
            var pr = go.AddComponent<PanelRenderer>();
            pr.panelSettings = instance.runtimePanel;
            pr.visualTreeAsset = instance.emptyTree;
            go.SetActive(true);
            Benchmark.Active = true;
            Bootstrap.ApplyFrameRate();
            GameControls.ModalOpen = true;
            GameControls.Suspend(true);
            EditorVSync(false);
            Time.timeScale = 1f;
            Debug.Log("Benchmark: started");
            return instance.Setup(0);
        }

        void OnEnable()
        {
            panelRenderer = GetComponent<PanelRenderer>();
            panelRenderer.RegisterUIReloadCallback(OnUIReload);
            SceneManager.sceneLoaded += OnSceneLoaded;
            ramRecorder = new ProfilerRecorder(ProfilerCategory.Memory, "System Used Memory");
            vramRecorder = new ProfilerRecorder(ProfilerCategory.Memory, "Gfx Used Memory");
        }

        void OnDisable()
        {
            panelRenderer?.UnregisterUIReloadCallback(OnUIReload);
            SceneManager.sceneLoaded -= OnSceneLoaded;
            ramRecorder.Dispose();
            vramRecorder.Dispose();
        }

        void OnDestroy()
        {
            if (runtimePanel != null) Destroy(runtimePanel);
            if (emptyTree != null) Destroy(emptyTree);
            if (instance != this) return;
            instance = null;
            // (also the Editor leaving Play mode in the middle of a run)
            if (Benchmark.Active) { Benchmark.Active = false; Cheats.ClearGranted(); }
            GameControls.ModalOpen = false;
            GameControls.Suspend(false);
            EditorVSync(true);
        }

        static bool editorVSyncWas;

        /// <summary>The Editor's Game view has a V-Sync of its own ("VSync (Game view only)"), which the game's frame rate
        /// option doesn't reach: off for a run (it held one at the display's 144 Hz), then as it was.</summary>
        static void EditorVSync(bool restore)
        {
#if UNITY_EDITOR
            var type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            var vsync = type?.GetProperty("vSyncEnabled", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (vsync == null) return;
            foreach (var view in Resources.FindObjectsOfTypeAll(type))
            {
                if (!restore) editorVSyncWas = (bool)vsync.GetValue(view);
                vsync.SetValue(view, restore && editorVSyncWas);
            }
#endif
        }

        // ---- the run --------------------------------------------------------------------------------------------

        /// <summary>The throwaway game of scene 'index' (as the Mod Builder's: free play in the finished game's world);
        /// returns the Unity scene to load.</summary>
        string Setup(int index)
        {
            step = index;
            var s = Steps[index];
            Session.ResetNewGame();
            Session.UseCompletedWorld();
            Session.Difficulty = Session.DifficultyNormal;
            Session.Economy = Economy.Android;
            Session.StationIndex = s.station;
            Session.LaunchedFromStation = Session.ArrivedByTravel = Session.ArrivedBySystemJump = Session.DockedFromSpace = false;
            for (int hint = 0; hint < 64; hint++) Session.Hints.Add(hint);   // no hint windows over the scene
            Cheats.ClearGranted();
            Cheats.Grant("godMode", true);
            UnityEngine.Random.InitState(Seed);
            return s.scene;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            StopAllCoroutines();
            flying = false;
            if (resultsPending && scene.name == MenuScene) { resultsPending = false; ShowResults(); return; }
            if (Benchmark.Active && scene.name == Steps[step].scene) { inScene = true; StartCoroutine(Fly()); return; }
            report = null;   // the game went somewhere else by itself: no run
            End();
        }

        IEnumerator Fly()
        {
            var s = Steps[step];
            flying = true;
            ShowLive(s, true);
            float until = Time.realtimeSinceStartup + WarmupSeconds;
            while (Time.realtimeSinceStartup < until) yield return null;
            SpaceLevel level = s.scene == "Space" ? FindFirstObjectByType<SpaceLevel>() : null;
            if (s.spawn != null && level != null)
            {
                UnityEngine.Random.InitState(Seed);
                s.spawn(level);
                yield return new WaitForSecondsRealtime(1f);   // (the spawns' own loading is no part of the scene)
            }
            display = $"{Screen.width} x {Screen.height}, {Screen.currentResolution.refreshRateRatio.value:0} Hz";

            sceneMs.Clear();
            Array.Clear(sceneLimits, 0, sceneLimits.Length);
            sceneCpu = sceneGpu = sceneShown = sceneRender = scenePresentWait = sceneStartWait = 0;
            sceneTimed = 0;
            sceneRam = sceneVram = 0f;
            AskMemory();
            ShowLive(s, false);
            var ship = level != null ? level.Player : null;
            var heading = ship != null ? ship.transform.rotation : Quaternion.identity;
            float flown = 0f, tickAt = 0f;
            double start = Time.realtimeSinceStartupAsDouble, last = start, shownAt = start;
            int shownFrames = 0;
            float cpuNow = 0f, gpuNow = 0f;
            while (last - start < s.seconds)
            {
                // The ship's path: a slow weave about the heading it had, by the game's clock.
                if (ship != null)
                {
                    ship.externalControl = ship.inputLocked = true;
                    ship.ExternalSpeedMetersPerSecond = s.speed;
                    flown += Time.deltaTime;
                    var t = ship.transform;
                    t.rotation = heading * Quaternion.Euler(Mathf.Sin(flown * 0.35f) * 6f, Mathf.Sin(flown * 0.25f) * 25f, 0f);
                    t.position += t.forward * (s.speed * Time.deltaTime);
                }
                if (s.tick != null && level != null && last - start >= tickAt) { tickAt += 0.25f; s.tick(level, (float)(last - start)); }
                FrameTimingManager.CaptureFrameTimings();
                yield return null;
                double now = Time.realtimeSinceStartupAsDouble;
                float ms = (float)((now - last) * 1000.0);
                last = now;
                sceneMs.Add(ms);
                sceneShown += FrameGeneration.PresentedPerFrame;
                if (memoryAsked) TakeMemory();
                // (a GPU time of hours: Unity now and then hands out one such frame, which alone set a scene's average,
                // "GPU 124805800.0 ms" in a build's dogfight: that frame's timings are left out)
                if (FrameTimingManager.GetLatestTimings(1, timing) == 1 && timing[0].cpuFrameTime > 0 && timing[0].gpuFrameTime < 10000.0)
                {
                    var t = timing[0];
                    // Reflex's wait at the frame's start is in Unity's main thread time: not the game's work (this
                    // frame's wait against a timing a few frames old: they agree over a scene).
                    float wait = Mathf.Min(FrameGeneration.StartWaitMs, (float)t.cpuMainThreadFrameTime);
                    cpuNow = (float)t.cpuMainThreadFrameTime - wait;
                    gpuNow = (float)t.gpuFrameTime;
                    sceneCpu += t.cpuMainThreadFrameTime;
                    sceneStartWait += wait;
                    sceneGpu += gpuNow;
                    sceneRender += t.cpuRenderThreadFrameTime;
                    scenePresentWait += t.cpuMainThreadPresentWaitTime;
                    sceneTimed++;
                    sceneLimits[(int)Benchmark.Classify((float)t.cpuFrameTime, (float)t.cpuMainThreadFrameTime, (float)t.cpuRenderThreadFrameTime, gpuNow,
                                                        (float)t.cpuMainThreadPresentWaitTime, wait)]++;
                }
                shownFrames++;
                if (now - shownAt < 0.25) continue;
                float fps = (float)(shownFrames / (now - shownAt));
                shownAt = now;
                shownFrames = 0;
                AskMemory();
                float done = (step + (float)((now - start) / s.seconds)) / Steps.Length;
                UpdateLive(fps, (float)(sceneMs.Count / (now - start)), cpuNow, gpuNow, done);
            }
            flying = false;

            var scene = Benchmark.Measure(X(s.key, s.english), sceneMs, sceneCpu, sceneGpu, sceneTimed, sceneLimits,
                                          sceneMs.Count > 0 ? (float)(sceneShown / sceneMs.Count) : 1f, sceneStartWait);
            scene.ramMb = sceneRam;
            scene.vramMb = sceneVram;
            report ??= new Benchmark.Report();
            report.scenes.Add(scene);
            sceneStarts.Add(allMs.Count);
            allMs.AddRange(sceneMs);
            allCpu += sceneCpu;
            allGpu += sceneGpu;
            allStartWait += sceneStartWait;
            allTimed += sceneTimed;
            for (int i = 0; i < allLimits.Length; i++) allLimits[i] += sceneLimits[i];
            Debug.Log($"Benchmark: {s.english}: {F(scene.average)} fps, 1% low {F(scene.low1)}, CPU {F(scene.cpuMs, "0.0")} ms, "
                      + $"GPU {F(scene.gpuMs, "0.0")} ms, {scene.limit} ({scene.frames} frames), RAM {Gb(scene.ramMb)}, VRAM {Gb(scene.vramMb)}; "
                      + $"render thread {F((float)(sceneRender / Math.Max(1, sceneTimed)), "0.00")} ms, present wait {F((float)(scenePresentWait / Math.Max(1, sceneTimed)), "0.00")} ms, "
                      + $"frame start wait {F(scene.waitMs, "0.00")} ms");

            if (step + 1 < Steps.Length) { SceneManager.LoadScene(Setup(step + 1)); yield break; }
            Finish();
        }

        void Update()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            if (report != null && results != null) { ResultsKeys(kb, pad); return; }
            if (!Benchmark.Active || !inScene) return;   // (not during the main menu's fade into the first scene)
            bool cancel = (kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame);
            if (cancel) { Debug.Log("Benchmark: cancelled"); report = null; End(); }
        }

        /// <summary>The run is over: the report is filled and written, the main menu shows it.</summary>
        void Finish()
        {
            report.overall = Benchmark.Overall(X("benchmarkOverall", "Whole run"), report.scenes, allMs, allCpu, allGpu, allTimed, allLimits, allStartWait);
            report.date = DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv);
            report.version = $"{Application.productName} {BuildVersion.Text}";
            report.cpu = $"{SystemInfo.processorType.Trim()} ({SystemInfo.processorCount} threads)";
            report.gpu = $"{SystemInfo.graphicsDeviceName} ({F(SystemInfo.graphicsMemorySize / 1024f, "0.#")} GB)";
            report.memory = $"{F(SystemInfo.systemMemorySize / 1024f)} GB";
            report.system = SystemInfo.operatingSystem;
            report.display = display;
            report.api = $"{SystemInfo.graphicsDeviceType}, {SystemInfo.graphicsDeviceVersion}";
            report.settings = SettingsShown();
            reportPath = Save(report);
            if (AutoRun) { Application.Quit(); return; }
            resultsPending = true;
            LeaveGame();
            SceneManager.LoadScene(MenuScene);
        }

        /// <summary>A run cut short (Esc, or the game left the scene by itself): back to the main menu, nothing kept.</summary>
        void End()
        {
            bool inGame = Benchmark.Active && SceneManager.GetActiveScene().name != MenuScene;
            LeaveGame();
            Destroy(gameObject);
            if (inGame) SceneManager.LoadScene(MenuScene);
        }

        void LeaveGame()
        {
            if (!Benchmark.Active) return;
            Benchmark.Active = false;
            Session.ResetNewGame();
            Cheats.ClearGranted();
            Bootstrap.ApplyFrameRate();
        }

        /// <summary>The graphics options as their rows show them (the rows shown on this device only).</summary>
        static List<string> SettingsShown()
        {
            var ids = new[] { "graphicsPreset", "displayMode", "renderScale", "upscaler", "upscalerQuality", "frameGeneration", "reflex",
                              "msaa", "postAa", "textureQuality", "textureFiltering", "detailDistance", "lods", "bloom", "fireSmoke", "hdr" };
            var lines = new List<string>();
            var all = OptionsCatalog.All();
            foreach (string id in ids)
            {
                var d = all.Find(o => o.id == id);
                try
                {
                    if (d == null || d.label == null || (d.visible != null && !d.visible())) continue;
                    string value = d.kind == OptionKind.Toggle ? (d.getBool() ? X("on", "On") : X("off", "Off"))
                                 : d.kind == OptionKind.Slider ? (d.format != null ? d.format(d.get()) : F(d.get(), "0.##"))
                                 : d.kind == OptionKind.Choice ? d.choices()[Mathf.Clamp(d.getIndex(), 0, d.choices().Length - 1)] : null;
                    if (!string.IsNullOrEmpty(value)) lines.Add($"{d.label()}: {value}");
                }
                catch (Exception e) { Debug.LogWarning($"Benchmark: option {id}: {e.Message}"); }
            }
            return lines;
        }

        /// <summary>The report as JSON and every frame's time as CSV, in <data>/Benchmarks; the JSON's path (null: not written).</summary>
        string Save(Benchmark.Report r)
        {
            try
            {
                string folder = Path.Combine(GamePaths.Data, "Benchmarks");
                Directory.CreateDirectory(folder);
                string name = "benchmark_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", Inv);
                string path = Path.Combine(folder, name + ".json");
                File.WriteAllText(path, JsonUtility.ToJson(r, true));
                var csv = new StringBuilder("scene,frame_ms\n");
                int scene = 0;
                for (int i = 0; i < allMs.Count; i++)
                {
                    while (scene + 1 < sceneStarts.Count && i >= sceneStarts[scene + 1]) scene++;
                    csv.Append(Steps[scene].english).Append(',').Append(allMs[i].ToString("0.###", Inv)).Append('\n');
                }
                File.WriteAllText(Path.Combine(folder, name + "_frames.csv"), csv.ToString());
                Debug.Log("Benchmark: written to " + path);
                return path;
            }
            catch (Exception e) { Debug.LogWarning("Benchmark: results not written: " + e.Message); return null; }
        }

        // ---- suggestions ----------------------------------------------------------------------------------------

        /// <summary>Option changes that should raise the frame rate, from what limited the scenes (see the file's head);
        /// each is a row of the options screen set to another of its values, shown as the row shows them.</summary>
        void Suggest()
        {
            suggestions.Clear();
            suggestionNote = null;
            float refresh = (float)Screen.currentResolution.refreshRateRatio.value;
            bool fast = refresh > 1f, gpu = false, cpu = false, known = false;
            foreach (var scene in report.scenes)
            {
                fast &= scene.average * scene.shownPerFrame >= refresh;
                gpu |= scene.limit == nameof(Benchmark.Limit.Gpu);
                cpu |= scene.limit == nameof(Benchmark.Limit.Cpu);
                known |= scene.limit != nameof(Benchmark.Limit.Unknown);
            }
            if (fast)
            {
                suggestionNote = string.Format(X("benchmarkFastEnough", "Every scene ran faster than the display's {0} Hz: nothing to change."), F(refresh));
                return;
            }
            if (!known) gpu = cpu = true;   // no GPU times on this device: both kinds

            var all = OptionsCatalog.All();
            OptionDef Row(string id)
            {
                var d = all.Find(o => o.id == id);
                return d != null && (d.visible == null || d.visible()) && (d.enabled == null || d.enabled()) ? d : null;
            }
            void Add(OptionDef d, string from, string to, Action apply) => suggestions.Add(new Suggestion { text = $"{d.label()}: {from}  →  {to}", apply = apply });
            void Pick(string id, Func<string[], int, int> target)   // a choice row to another of its choices
            {
                var d = Row(id);
                if (d == null || d.kind != OptionKind.Choice) return;
                var names = d.choices();
                int now = d.getIndex();
                if (now < 0 || now >= names.Length) return;
                int to = target(names, now);
                if (to >= 0 && to < names.Length && to != now) Add(d, names[now], names[to], () => d.setIndex(to));
            }
            void Flip(string id, bool to)
            {
                var d = Row(id);
                if (d != null && d.kind == OptionKind.Toggle && d.getBool() != to)
                    Add(d, X(to ? "off" : "on", to ? "Off" : "On"), X(to ? "on" : "off", to ? "On" : "Off"), () => d.setBool(to));
            }

            if (gpu)
            {
                if (Bootstrap.IsTemporal(Bootstrap.ActiveUpscaler))
                    Pick("upscalerQuality", (names, now) => now < 3 ? now + 1 : now);   // down to Performance at most
                else
                {
                    int best = Bootstrap.DlssSupported ? Settings.UpscalerDlss : Bootstrap.FsrTemporalSupported ? Settings.UpscalerFsrTemporal
                             : Bootstrap.XessSupported ? Settings.UpscalerXess : -1;
                    var upscaler = Row("upscaler");
                    if (best >= 0 && upscaler != null)
                    {
                        string name = best == Settings.UpscalerDlss ? "DLSS" : best == Settings.UpscalerXess ? "XeSS" : UpscalerFramework.BestFsrLabel ?? "FSR";
                        var names = upscaler.choices();
                        Add(upscaler, names[Mathf.Clamp(upscaler.getIndex(), 0, names.Length - 1)], name,
                            () => { Settings.Upscaler = best; Settings.UpscalerQuality = Mathf.Max(Settings.UpscalerQuality, 1); });
                    }
                    else
                    {
                        // No such upscaler here: the render scale to 80 % (the row's choices are percentages).
                        Pick("renderScale", (names, now) =>
                        {
                            int Percent(int i) => int.TryParse(names[i].Replace("%", "").Trim(), out int v) ? v : 0;
                            int to = now;
                            for (int i = 0; i < names.Length; i++) if (Percent(i) <= 80 && Percent(i) > (to == now ? 0 : Percent(to))) to = i;
                            return Percent(now) > 80 ? to : now;
                        });
                        Pick("msaa", (names, now) => 0);
                    }
                }
            }
            if (cpu)
            {
                Pick("detailDistance", (names, now) => now - 1);
                Flip("lods", true);
                Flip("fireSmoke", false);
            }
            if (suggestions.Count == 0) suggestionNote = X("benchmarkNoSuggest", "None of the options checked would raise the frame rate here.");
        }

        void ApplySuggestions(bool rerun)
        {
            foreach (var suggestion in suggestions)
            {
                try { suggestion.apply(); }
                catch (Exception e) { Debug.LogWarning($"Benchmark: {suggestion.text}: {e.Message}"); }
            }
            if (Settings.Staging && Settings.HasStaged) Settings.ApplyStaged();
            applied = true;
            Debug.Log($"Benchmark: {suggestions.Count} suggestion(s) applied" + (rerun ? ", running again" : ""));
            if (rerun) Rerun(); else ShowResults();
        }

        /// <summary>The run again with this runner (the controls stay off): its numbers dropped, the first scene loaded.</summary>
        void Rerun()
        {
            report = null;
            results = live = null;
            root?.Clear();
            suggested = applied = inScene = resultsPending = false;
            allMs.Clear();
            sceneStarts.Clear();
            Array.Clear(allLimits, 0, allLimits.Length);
            allCpu = allGpu = allStartWait = 0;
            allTimed = 0;
            Benchmark.Active = true;
            Bootstrap.ApplyFrameRate();
            SceneManager.LoadScene(Setup(0));
        }

        // ---- the panel ------------------------------------------------------------------------------------------

        void OnUIReload(PanelRenderer renderer, VisualElement rootElement, int version)
        {
            root = rootElement;
            UiLayout.Keep(runtimePanel, root);
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            root.pickingMode = PickingMode.Position;   // the pointer stays off the menus under it
            live = results = null;
            if (report != null && !resultsPending && !Benchmark.Active) ShowResults();
            else if (flying) ShowLive(Steps[step], false);
        }

        static Label Say(VisualElement into, string text, float size, Color color, bool bold = false)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.style.fontSize = UiLayout.Type(size);
            l.style.color = color;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginLeft = l.style.marginRight = l.style.marginTop = l.style.marginBottom = 0;
            l.style.paddingLeft = l.style.paddingRight = l.style.paddingTop = l.style.paddingBottom = 0;
            if (bold) l.AddToClassList("gof-semibold");
            into.Add(l);
            return l;
        }

        static VisualElement Box(VisualElement into, bool row = false)
        {
            var v = new VisualElement { pickingMode = PickingMode.Ignore };
            v.style.flexDirection = row ? FlexDirection.Row : FlexDirection.Column;
            into?.Add(v);
            return v;
        }

        /// <summary>The corner box of a running scene: its name, the frame rate now, the average so far, the frame's CPU and
        /// GPU time, the scene's graph and the run's progress.</summary>
        void ShowLive(Step s, bool warmup)
        {
            if (root == null) return;
            if (live == null)
            {
                root.Clear();
                live = Box(root);
                var st = live.style;
                st.position = Position.Absolute;
                st.right = 24;
                st.top = Length.Percent(36f);   // (clear of the flight HUD's and the station menu's corners)
                st.width = 460;
                st.paddingLeft = st.paddingRight = 18;
                st.paddingTop = st.paddingBottom = 14;
                st.backgroundColor = new Color(0.015f, 0.04f, 0.07f, 0.82f);
                st.borderTopWidth = 3;
                st.borderTopColor = Amber;
                liveTitle = Say(live, "", 18, Cyan, true);
                liveFps = Say(live, "", 34, Ink, true);
                liveDetail = Say(live, "", 18, Dim);
                liveGraph = new FpsGraph { compact = true };
                liveGraph.style.height = 90;
                liveGraph.style.marginTop = 8;
                live.Add(liveGraph);
                var track = Box(live);
                track.style.height = 4;
                track.style.marginTop = 10;
                track.style.backgroundColor = Line;
                liveBar = Box(track);
                liveBar.style.height = 4;
                liveBar.style.backgroundColor = Cyan;
                Say(live, "Esc / B: " + Localization.Get(425), 16, Dim).style.marginTop = 8;   // Cancel
            }
            liveTitle.text = $"{X("benchmark", "Benchmark")}  {step + 1} / {Steps.Length}  ·  {X(s.key, s.english)}".UiCaps();
            liveFps.text = warmup ? X("benchmarkWarmup", "Warming up…") : "";
            liveDetail.text = "";
            liveFpsValues.Clear();
            liveGraph.Show(null, null, null);
            liveBar.style.width = Length.Percent(100f * step / Steps.Length);
        }

        void UpdateLive(float fps, float average, float cpuMs, float gpuMs, float done)
        {
            if (live == null) return;
            float shown = FrameGeneration.PresentedPerFrame;
            liveFps.text = shown > 1.05f ? $"{F(fps * shown)} FPS  ({F(fps)} × {F(shown, "0.#")})" : $"{F(fps)} FPS";
            string detail = $"{X("benchmarkAverage", "Average")} {F(average)}  ·  {F(1000f / Mathf.Max(fps, 0.001f), "0.0")} ms";
            if (cpuMs > 0f) detail += $"\nCPU {F(cpuMs, "0.0")} ms" + (gpuMs > 0f ? $"  ·  GPU {F(gpuMs, "0.0")} ms" : "");
            if (sceneRam > 0f) detail += $"\nRAM {Gb(sceneRam)}" + (sceneVram > 0f ? $"  ·  VRAM {Gb(sceneVram)}" : "");
            liveDetail.text = detail;
            liveBar.style.width = Length.Percent(100f * Mathf.Clamp01(done));
            liveFpsValues.Add(fps);
            liveGraph.Show(liveFpsValues.ToArray(), null, null);
        }

        // ---- the results page -----------------------------------------------------------------------------------

        string LimitText(Benchmark.Scene s)
        {
            Enum.TryParse(s.limit, out Benchmark.Limit limit);
            float share = limit == Benchmark.Limit.Gpu ? s.gpuShare : limit == Benchmark.Limit.Cpu ? s.cpuShare
                        : limit == Benchmark.Limit.Present ? s.presentShare : limit == Benchmark.Limit.Pacing ? s.pacingShare
                        : 1f - s.gpuShare - s.cpuShare - s.presentShare - s.pacingShare;
            // Pacing: the frame's start waited in Reflex's sleep for the GPU side: with frame generation the generated
            // frames and their pacing (not in Unity's GPU time), without it the graphics card.
            string name = limit == Benchmark.Limit.Pacing ? (s.shownPerFrame > 1.05f ? X("frameGeneration", "Frame generation") + " (GPU)" : X("benchmarkGpu", "Graphics card (GPU)"))
                        : limit == Benchmark.Limit.Gpu ? X("benchmarkGpu", "Graphics card (GPU)")
                        : limit == Benchmark.Limit.Cpu ? X("benchmarkCpu", "Processor (CPU)")
                        : limit == Benchmark.Limit.Present ? X("benchmarkPresent", "Frame rate limit or display")
                        : limit == Benchmark.Limit.Balanced ? X("benchmarkBalanced", "Balanced (CPU and GPU)")
                        : X("benchmarkUnknown", "Not measured");
            return limit == Benchmark.Limit.Unknown ? name : $"{name}, {F(share * 100f)} %";
        }

        void ShowResults()
        {
            if (root == null || report == null) return;
            root.Clear();
            live = null;
            buttons.Clear();
            actions.Clear();
            var o = report.overall;
            results = Box(root);
            var st = results.style;
            st.position = Position.Absolute;
            st.left = st.top = st.right = st.bottom = 0;
            st.backgroundColor = Back;
            st.paddingLeft = st.paddingRight = 56;
            st.paddingTop = 36;
            st.paddingBottom = 28;

            var head = Box(results, true);
            head.style.justifyContent = Justify.SpaceBetween;
            head.style.alignItems = Align.FlexEnd;
            head.style.borderBottomWidth = 1;
            head.style.borderBottomColor = Line;
            head.style.paddingBottom = 10;
            Say(head, $"{Application.productName}  ·  {X("benchmark", "Benchmark")}".UiCaps(), 28, Cyan, true).style.letterSpacing = 4;
            Say(head, $"{report.version}  ·  {report.date}", 18, Dim);

            var body = Box(results, true);
            body.style.flexGrow = 1;
            body.style.marginTop = 20;

            // Left: the run's numbers, the system, the settings, the suggestions: blocks spread over the column's height, so
            // the picture (without the suggestions) is filled as the page is.
            var left = Box(body);
            left.style.width = Length.Percent(34f);
            left.style.paddingRight = 32;
            left.style.justifyContent = Justify.SpaceBetween;
            var block = Box(left);
            var big = Box(block, true);
            big.style.alignItems = Align.FlexEnd;
            Say(big, F(o.average), 64, Ink, true);
            Say(big, "  FPS  " + X("benchmarkAverage", "Average").ToLowerInvariant(), 22, Dim).style.marginBottom = 12;
            Say(block, $"{X("benchmarkLow1", "1% low")} {F(o.low1)}  ·  {X("benchmarkLow01", "0.1% low")} {F(o.low01)}", 22, Ink);
            if (o.shownPerFrame > 1.05f)
                Say(block, string.Format(X("benchmarkShown", "{0} FPS shown with frame generation"), F(o.average * o.shownPerFrame)), 18, Amber);
            block = Box(left);
            Section(block, X("benchmarkLimitedBy", "Limited by"));
            Say(block, LimitText(o), 20, Ink);
            if (o.cpuMs > 0f) Say(block, $"CPU {F(o.cpuMs, "0.0")} ms" + (o.gpuMs > 0f ? $"  ·  GPU {F(o.gpuMs, "0.0")} ms" : ""), 18, Dim);
            if (o.ramMb > 0f) Say(block, $"RAM {Gb(o.ramMb)}" + (o.vramMb > 0f ? $"  ·  VRAM {Gb(o.vramMb)}" : ""), 18, Dim);
            block = Box(left);
            Section(block, X("benchmarkSystem", "System"));
            foreach (string line in new[] { report.cpu, report.gpu, $"{report.memory} RAM  ·  {report.display}", report.system, report.api })
                Say(block, line, 18, Ink);
            block = Box(left);
            Section(block, X("benchmarkSettings", "Graphics settings"));
            var settings = Box(block, true);
            settings.style.flexWrap = Wrap.Wrap;
            foreach (string line in report.settings) Say(settings, line, 18, Ink).style.width = Length.Percent(50f);
            Say(block, X("benchmarkUncapped", "The frame rate limit and V-Sync were off for the run."), 16, Dim).style.marginTop = 8;
            if (!suggested) { suggested = true; Suggest(); }
            suggestBox = Box(left);
            Section(suggestBox, X("benchmarkSuggest", "Suggestions for a higher frame rate"));
            foreach (var suggestion in suggestions) Say(suggestBox, suggestion.text, 18, Ink);
            if (suggestionNote != null) Say(suggestBox, suggestionNote, 16, Dim);
            if (applied) Say(suggestBox, X("benchmarkApplied", "Applied."), 16, Amber);

            // Right: the frame rate over the run, a row for each scene
            var right = Box(body);
            right.style.flexGrow = 1;
            var graph = new FpsGraph();
            graph.style.flexGrow = 1;
            graph.style.minHeight = 260;
            var marks = new int[report.scenes.Count];
            var names = new string[report.scenes.Count];
            for (int i = 0, at = 0; i < report.scenes.Count; at += report.scenes[i].fps.Length, i++) { marks[i] = at; names[i] = report.scenes[i].name; }
            graph.Show(o.fps, marks, names);
            right.Add(graph);
            var table = Box(right);
            table.style.marginTop = 16;
            Row(table, Cyan, X("benchmarkScene", "Scene"), X("benchmarkAverage", "Average"), X("benchmarkLow1", "1% low"), "CPU", "GPU", "RAM", "VRAM", X("benchmarkLimitedBy", "Limited by"));
            foreach (var s in report.scenes)
                Row(table, Ink, s.name, F(s.average) + " FPS", F(s.low1), s.cpuMs > 0f ? F(s.cpuMs, "0.0") + " ms" : "–",
                    s.gpuMs > 0f ? F(s.gpuMs, "0.0") + " ms" : "–", Gb(s.ramMb), Gb(s.vramMb), LimitText(s));

            footer = Box(results, true);
            footer.style.marginTop = 16;
            footer.style.alignItems = Align.Center;
            footer.style.flexWrap = Wrap.Wrap;
            footer.style.justifyContent = Justify.FlexEnd;
            status = Say(footer, reportPath != null ? reportPath : "", 16, Dim);
            status.style.flexGrow = 1;
            status.style.flexShrink = 1;
            FooterButton(X("benchmarkSaveImage", "Save image"), SaveImage, true);
            if (PhotoMode.CanShowInFolder && reportPath != null) FooterButton(X("benchmarkShowFile", "Show results file"), () => PhotoMode.ShowInFolder(reportPath), false);
            if (suggestions.Count > 0 && !applied)
            {
                FooterButton(X("benchmarkApply", "Apply suggestions"), () => ApplySuggestions(false), false);
                FooterButton(X("benchmarkApplyRerun", "Apply and run again"), () => ApplySuggestions(true), false);
            }
            FooterButton(X("hdrCalibrationDone", "Done"), Close, false);
            selected = 0;
            Mark();
            ShowExtras(!savingImage);   // (the page rebuilt while its picture is taken: still without them)
        }

        /// <summary>The page's buttons and suggestions: not in the picture, which is the run's data alone and uses the whole
        /// image (the user's requests): out of the layout, so the graph and the columns take their room.</summary>
        void ShowExtras(bool shown)
        {
            var display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (footer != null) footer.style.display = display;
            if (suggestBox != null) suggestBox.style.display = display;
        }

        static void Section(VisualElement into, string title)
        {
            var l = Say(into, title.UiCaps(), 16, Cyan, true);
            l.style.marginTop = 18;
            l.style.marginBottom = 4;
            l.style.letterSpacing = 2;
        }

        static readonly float[] Columns = { 19f, 11f, 8f, 9f, 9f, 8f, 9f, 27f };

        static void Row(VisualElement table, Color color, params string[] cells)
        {
            var row = Box(table, true);
            row.style.paddingTop = row.style.paddingBottom = 5;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = Line;
            for (int i = 0; i < cells.Length; i++) Say(row, cells[i], 18, color, i == 0).style.width = Length.Percent(Columns[i]);
        }

        void FooterButton(string text, Action action, bool primary)
        {
            void Press() { UiSounds.Confirm(); action(); }
            var b = new Button(Press) { text = text.UiCaps(), focusable = false };
            b.AddToClassList("gof-button");
            if (primary) b.AddToClassList("gof-button--primary");
            b.AddToClassList("gof-semibold");
            b.style.marginLeft = 12;
            footer.Add(b);
            buttons.Add(b);
            actions.Add(Press);
        }

        void Mark()
        {
            for (int i = 0; i < buttons.Count; i++) buttons[i].EnableInClassList("options-selected", i == selected);
        }

        /// <summary>Left / right pick a button, Enter / A presses it, Esc / B closes.</summary>
        void ResultsKeys(Keyboard kb, Gamepad pad)
        {
            int move = 0;
            bool press = false, close = false;
            if (kb != null)
            {
                close = kb.escapeKey.wasPressedThisFrame;
                press = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
                if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) move = -1;
                if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame) move = 1;
            }
            if (pad != null)
            {
                close |= pad.buttonEast.wasPressedThisFrame;
                press |= pad.buttonSouth.wasPressedThisFrame;
                if (pad.dpad.left.wasPressedThisFrame || pad.leftStick.left.wasPressedThisFrame) move = -1;
                if (pad.dpad.right.wasPressedThisFrame || pad.leftStick.right.wasPressedThisFrame) move = 1;
            }
            if (close) { Close(); return; }
            if (move != 0 && buttons.Count > 0)
            {
                selected = (selected + move + buttons.Count) % buttons.Count;
                UiSounds.Move();
                Mark();
            }
            if (press && selected < actions.Count) actions[selected]();
        }

        /// <summary>The page as a picture in the pictures folder (the photo mode's), without its buttons and suggestions (the
        /// panel reloads when its target changes, which builds the page again: ShowResults keeps them out). The panel is drawn
        /// into a texture of its own for it (as PlayerAssetsCheck.PanelShot): 1920 x 1080 whatever the window, and the page's
        /// own colours (a screenshot of an HDR screen comes out washed).</summary>
        void SaveImage()
        {
            if (footer != null && !savingImage) StartCoroutine(SaveImageRoutine());
        }

        IEnumerator SaveImageRoutine()
        {
            savingImage = true;
            ShowExtras(false);
            const int Width = 1920, Height = 1080;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            runtimePanel.targetTexture = rt;
            for (int i = 0; i < 6; i++) yield return null;   // (laid out at that size and drawn)
            string path = null;
            Texture2D shot = null;
            try
            {
                var was = RenderTexture.active;
                RenderTexture.active = rt;
                shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                shot.Apply();
                RenderTexture.active = was;
                path = PhotoMode.StoreAt(shot.EncodeToPNG(), $"GoF2_benchmark_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            }
            catch (Exception e) { Debug.LogWarning("Benchmark: image: " + e.Message); }
            runtimePanel.targetTexture = null;
            if (shot != null) Destroy(shot);
            rt.Release();
            Destroy(rt);
            savingImage = false;
            ShowExtras(true);
            imagePath = path;
            if (status != null) status.text = path != null ? $"{Localization.Get(55)}  {path}" : Localization.Get(56);   // "Screenshot saved." / the error
            if (path != null && PhotoMode.CanShowInFolder) PhotoMode.ShowInFolder(path);
        }

        void Close()
        {
            if (savingImage) return;
            UiSounds.Back();
            Destroy(gameObject);
        }

        // ---- the graph ------------------------------------------------------------------------------------------

        /// <summary>The frame rate over time as a line on a grid: 'marks' are the steps where a scene starts, with its name.
        /// The numbers and names are labels of the panel (text drawn with the mesh context kept its size on a smaller
        /// screen, and the names ran into each other): each name is cut to its own scene's width.</summary>
        sealed class FpsGraph : VisualElement
        {
            public bool compact;
            float[] values;
            int[] marks;
            string[] names;
            readonly List<Label> labels = new List<Label>();
            float max = 1f, stepSize = 1f;
            Rect plot;
            Vector2 laidOut;

            public FpsGraph()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Draw;
                RegisterCallback<GeometryChangedEvent>(e => { if (e.newRect.size != laidOut) Layout(); });
            }

            public void Show(float[] fps, int[] sceneMarks, string[] sceneNames)
            {
                values = fps;
                marks = sceneMarks;
                names = sceneNames;
                Layout();
                MarkDirtyRepaint();
            }

            float Px(int i) => plot.x + (values.Length > 1 ? i / (float)(values.Length - 1) : 0.5f) * plot.width;

            /// <summary>The scale, the plot's rectangle and the labels around it, for the element's size now.</summary>
            void Layout()
            {
                foreach (var label in labels) label.RemoveFromHierarchy();
                labels.Clear();
                var r = contentRect;
                laidOut = r.size;
                plot = default;
                if (values == null || values.Length == 0 || float.IsNaN(r.width) || r.width < 20f || r.height < 20f) return;
                float top = 0f;
                foreach (float v in values) top = Mathf.Max(top, v);
                // The scale's top: the next round number over the highest value, a few steps to it.
                float unit = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(Mathf.Max(top, 1f))));
                stepSize = top / unit <= 2f ? unit * 0.5f : top / unit <= 4f ? unit : top / unit <= 8f ? unit * 2f : unit * 2.5f;
                max = Mathf.Max(stepSize, Mathf.Ceil(top / stepSize) * stepSize);
                float labelWidth = compact ? 0f : 52f, labelHeight = compact || marks == null ? 0f : 26f;
                plot = new Rect(r.x + labelWidth, r.y + labelHeight, r.width - labelWidth, r.height - labelHeight - 2f);
                if (compact) return;
                for (float v = 0f; v <= max + 0.01f; v += stepSize)
                    Place(v.ToString("0", Inv), r.x, plot.yMax - v / max * plot.height - 10f, labelWidth - 10f).style.unityTextAlign = TextAnchor.UpperRight;
                if (marks == null || names == null) return;
                for (int i = 0; i < marks.Length && i < names.Length; i++)
                {
                    float x = Px(marks[i]), next = i + 1 < marks.Length ? Px(marks[i + 1]) : plot.xMax;
                    Place(names[i], x + 6f, r.y, Mathf.Max(0f, next - x - 12f));
                }
            }

            Label Place(string text, float x, float y, float width)
            {
                var label = new Label(text) { pickingMode = PickingMode.Ignore };
                var st = label.style;
                st.position = Position.Absolute;
                st.left = x;
                st.top = y;
                st.width = width;
                st.fontSize = 15;
                st.color = Dim;
                st.whiteSpace = WhiteSpace.NoWrap;
                st.overflow = Overflow.Hidden;
                st.textOverflow = TextOverflow.Ellipsis;
                st.marginLeft = st.marginRight = st.marginTop = st.marginBottom = 0;
                st.paddingLeft = st.paddingRight = st.paddingTop = st.paddingBottom = 0;
                Add(label);
                labels.Add(label);
                return label;
            }

            void Draw(MeshGenerationContext ctx)
            {
                if (values == null || values.Length == 0 || plot.width < 1f || plot.height < 1f) return;
                var p = ctx.painter2D;
                p.lineWidth = 1f;
                p.strokeColor = Line;
                for (float v = 0f; v <= max + 0.01f; v += stepSize)
                {
                    float y = plot.yMax - v / max * plot.height;
                    p.BeginPath();
                    p.MoveTo(new Vector2(plot.x, y));
                    p.LineTo(new Vector2(plot.xMax, y));
                    p.Stroke();
                }
                if (marks != null)
                    for (int i = 1; i < marks.Length; i++)
                    {
                        float x = Px(marks[i]);
                        p.BeginPath();
                        p.MoveTo(new Vector2(x, plot.y));
                        p.LineTo(new Vector2(x, plot.yMax));
                        p.Stroke();
                    }
                p.strokeColor = Cyan;
                p.lineWidth = 2f;
                p.lineJoin = LineJoin.Round;
                p.BeginPath();
                for (int i = 0; i < values.Length; i++)
                {
                    var point = new Vector2(Px(i), plot.yMax - Mathf.Clamp01(values[i] / max) * plot.height);
                    if (i == 0) p.MoveTo(point); else p.LineTo(point);
                }
                p.Stroke();
            }
        }
    }
}
