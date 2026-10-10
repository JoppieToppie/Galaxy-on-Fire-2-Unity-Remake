// StarMap.cs
// The star map (StarMap, Reference/research/starmap_travel.md), one class for the station's Map button, the jumpgate and
// the Khador Drive. A fullscreen 3D scene with its own camera (layer 31, the level's cameras and lights are switched off
// while it is open) plus a UI Toolkit overlay. Created at runtime by Open(); the owner pauses its own game.
//   camera       StarMap::init: fov 1.1504 rad, near 200, far 64000, looks along game +Z (screen-right = game -X);
//                galaxy view at ((start + pan) * 20, 0), system view 500 units in front of the selected sun
//   galaxy view  one additive sun sprite per visible system (scale 0.012) over the map background; faint route lines
//                (alpha 34), a pulse line from the current system to its gate neighbours every 0.99 s (not with a drive);
//                tap within +-60 px selects (sound 103), the camera glides it to the centre (/30 per frame), a second tap
//                zooms in (2184.5 ms sine ease, sound 106) if allowed: the current system or a gate neighbour, any visible
//                system with a Khador Drive, else 420; drag pans 0.325 per px with 0.9 inertia and spring limits
//   system view  StarMap::initStarSystem: planets on orbits in a 1/128 container at the sun, rotated (-pi/8, pi/8, -pi/32);
//                drag rotates it (pitch +-45 deg); tap selects a planet (104), release turns it to the front (105, factor
//                0.25, pitch -3096); a second tap confirms; back zooms out (107)
//   overlay      StarMap::draw / drawOnScreenInfo: rings 0x48a / 0x48c, names (selected orange, others dimmed to 64),
//                race and security lines, "Tech level", visited tick, gate icon, the "you are here" pulse 0x4fd; header 177
//                "Map", the system block, "Energy cells needed: N / M" (578) with a drive, the Key legend (400)
//   confirm      StarMap::OnTouchEnd mode 3: 419 on the current station; with a drive 579 / 582 / 581 on the energy cells;
//                else "Destination: X / Travel to this station?" (574 + 421). The result goes to the owner.
// Input like the other screens: touch / mouse drag and tap; keyboard arrows select, WASD move / turn, Enter zoom in /
// confirm, K key, Esc back; controller D-pad select, left stick move / turn, A, Y key, B back.
//   mission map  StarMap(true, mission): view only, centred on the target; the yellow route (StarMap::draw, galaxy view) along
//                SystemPathFinder::getSystemPath from the current system (the Wanted window: setStart, the criminal's
//                last-seen system): finished segments opaque 0xFFFF00FF, the one being drawn grows from its start with alpha
//                255 * t on the pulse's 0.99 s cycle and the next starts when t wraps
//   reveal       StarMap(false, 0, true, sys) (the lounge's coordinates): the camera on the new system, which stays hidden (no
//                label, no lines, no input) for 4000 ms while its sun grows, then is selected
//   volatile     Ship::hasVolatileGoods (209 / 204 in the hold) with a drive: a target outside the gate routes gives 612, one
//                inside goes by the gate (no instant jump)
// Remake: the full-map overview, one zoom level further out that fits every visible system (the Full map button, Q, the
// controller's X, the mouse wheel or a pinch): no panning there; a tap or Enter on a system zooms back in on it. The zoom
// takes the galaxy <-> system zoom's 2184.5 ms, its sounds' length.
// Remake choices: hidden systems' suns aren't drawn (the original draws all 34, uncertainty 3); the galaxy sun shrinks to
// the system-view sun size while zooming instead of a second sun with the flight sun texture; the reveal's sun grows to
// the normal 0.012 (the original's 0.002 literal, uncertainty 10); the reveal map is view only.

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Visuals;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    /// <summary>Mission = StarMap(true, mission, ...): centred on a mission's target; view only in the original (remake: the
    /// Missions window's map travels like the station's, StarMap.Open's allowTravel).</summary>
    public enum StarMapMode { Station, Gate, Khador, Mission }

    /// <summary>What the player picked: station -1 = closed without a destination.</summary>
    public struct StarMapResult
    {
        public int station;
        public bool instantJump;   // Khador jump to another system
        public int cells;          // energy cells for that jump
        public bool toVoid;        // 422 answered Yes: the Khador jump into the Void (station is -1 then)
    }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class StarMap : MonoBehaviour
    {
        const float M = 0.05f;
        const int Layer = 31;
        const float TapRadius = 60f, DragScale = 0.325f, ZoomMs = 32768f / 15f;
        const float HeaderBottom = 58f, FooterHeight = 74f;

        public static StarMap Current { get; private set; }
        public static bool IsOpen => Current != null;
        /// <summary>The map's own camera (VR draws it into a texture on the floating screen).</summary>
        public Camera MapCamera => cam;

        class Item
        {
            public VisualElement root, ring, pulse, raceIcon, visited, gate, story, freelance;
            public Label name, line1, line2;
        }

        Database db;
        StarMapAssets assets;
        StarMapMode mode;
        public StarMapMode Mode => mode;
        bool jumpDrive;
        /// <summary>Remake (players' suggestion): a Mission map that can travel: confirming a station programs it like the
        /// station's map, and a target past the gate neighbours is reached along the gate route (SystemJump.NextHop).</summary>
        bool travel;
        Action<StarMapResult> onClosed;
        int promptStation = -1, focusStation = -1;
        bool askVoid;

        int currentStation, currentSystem;
        SystemData current;
        bool[] visible;

        // 3D
        GameObject world;
        Camera cam;
        Transform[] suns = new Transform[64];   // by system number (more with mods' systems: BuildGalaxy grows it)
        float sunScale = 0.012f;
        /// <summary>StarMap::StarMap: the early-warning wormhole at the system the Void attack (index > 0x1f).</summary>
        Transform wormhole;
        Vector3 wormholeScale = Vector3.one;
        GameObject systemRoot;
        Light sunLight;
        readonly List<GalaxyMap.Planet> planets = new List<GalaxyMap.Planet>();
        readonly List<Transform> planetObjects = new List<Transform>(), orbitObjects = new List<Transform>();
        readonly List<Vector3> planetScales = new List<Vector3>();
        readonly List<Camera> disabledCameras = new List<Camera>();
        readonly List<Light> disabledLights = new List<Light>();
        UnityEngine.Rendering.AmbientMode savedAmbientMode;
        Color savedAmbient;
        bool savedFog;

        // state
        bool systemView;
        int zoomDir;            // +1 zooming in, -1 zooming out
        float zoomMs, fade = 255f;
        Vector2 start, pan, vel;
        bool autoCentre, autoRotate;
        int selected = -1, centred = -1, zoomSystem = -1;
        int selectedPlanet = -1, frontPlanet = -1;
        float yaw = 4096f, pitch = -4096f, yawVel, pitchVel, spin, timeMs;
        int cells, cellsInCargo;
        bool noGate, keyShown;
        // Remake: the full-map overview (0 = the normal galaxy view, 1 = every visible system on screen).
        bool overview;
        float overviewT;
        Vector2 overviewMin, overviewMax;
        float overviewZ;
        // As long as the galaxy <-> system zoom (ZoomMs, the same sine ease and the same sounds, Map_Zoom_In 2.68 s / Map_Zoom_Out
        // 2.32 s): at 650 ms the move was over long before its sound.
        const float OverviewMs = ZoomMs;
        float OverviewEase => 0.5f - 0.5f * Mathf.Cos(Mathf.PI * overviewT);
        bool InOverview => overview || overviewT > 0f;
        readonly Dictionary<int, Vector2> pinch = new Dictionary<int, Vector2>();
        float pinchStart;

        // UI
        PanelRenderer panelRenderer;
        PanelSettings runtimePanel;
        VisualElement root, touch, labels, systemHeader, keyBox, hints, dialog;
        Label energyLine;
        Button backButton, keyButton, zoomButton, dialogYes, dialogNo;
        Action dialogAction, dialogNoAction;
        bool dialogInfo;
        int dialogFocus;
        readonly Dictionary<int, Item> systemItems = new Dictionary<int, Item>();
        readonly List<Item> planetItems = new List<Item>();
        int pointer = -1;
        Vector2 downPos, lastPos;
        bool dragged;
        int lastPadDir;

        static Texture2D Tex(string name) => Resources.Load<Texture2D>("GoF2Hud/" + name);
        static string T(int id) => Localization.Get(id);

        // ---- opening / closing ---------------------------------------------------------------------------

        /// <summary>Opens the map. promptStation >= 0 (jumpgate with a programmed station) first shows only "Destination: X /
        /// Travel to this station?": Yes returns that station, No opens the map.</summary>
        /// <param name="askVoid">StarMap::askForJumpIntoAlienWorld: first "Jump to the Void's system?" (422); Yes returns
        /// toVoid, No opens the map.</param>
        /// <param name="routeFromSystem">Mission map: the route's start system (StarMap::setStart; -1 = the current system).</param>
        /// <param name="revealSystem">The reveal animation of a newly visible system (-1 = none).</param>
        public static StarMap Open(Database db, StarMapMode mode, bool jumpDrive, Action<StarMapResult> closed, int promptStation = -1,
                                       int focusStation = -1, bool askVoid = false, int routeFromSystem = -1, int revealSystem = -1,
                                       bool allowTravel = false)
        {
            var assets = StarMapAssets.Load();
            if (assets == null || assets.layout == null || assets.panelSettings == null)
            {
                Debug.LogError("StarMap: run GoF2 > Build > Star Map Assets");
                return null;
            }
            if (Current != null) Current.Close(new StarMapResult { station = -1 });
            var go = new GameObject("Star Map");
            go.SetActive(false);
            var map = go.AddComponent<StarMap>();
            map.db = db;
            map.assets = assets;
            map.mode = mode;
            map.jumpDrive = jumpDrive;
            map.onClosed = closed;
            map.promptStation = promptStation;
            map.focusStation = focusStation;
            map.askVoid = askVoid;
            map.routeFrom = routeFromSystem;
            map.revealSystem = revealSystem;
            map.travel = allowTravel && mode == StarMapMode.Mission;
            var pr = go.AddComponent<PanelRenderer>();
            pr.panelSettings = assets.panelSettings;
            pr.visualTreeAsset = assets.layout;
            Current = map;
            go.SetActive(true);
            return map;
        }

        void OnEnable()
        {
            panelRenderer = GetComponent<PanelRenderer>();
            panelRenderer.RegisterUIReloadCallback(OnUIReload);
            if (runtimePanel == null && panelRenderer.panelSettings != null)
            {
                runtimePanel = Instantiate(panelRenderer.panelSettings);
                runtimePanel.sortingOrder = panelRenderer.panelSettings.sortingOrder + 20;   // above the HUD / station menu
                runtimePanel.referenceResolution = new Vector2Int(1920, 1080);
                runtimePanel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                runtimePanel.match = 1f;
                panelRenderer.panelSettings = runtimePanel;
            }
            InputMode.Changed += ApplyInputMode;
        }

        void OnDisable()
        {
            panelRenderer?.UnregisterUIReloadCallback(OnUIReload);
            InputMode.Changed -= ApplyInputMode;
        }

        void OnDestroy()
        {
            RestoreScene();
            if (world != null) Destroy(world);
            if (runtimePanel != null) Destroy(runtimePanel);
            if (Current == this) Current = null;
        }

        void Close(StarMapResult result)
        {
            if (Current == this) Current = null;
            RestoreScene();
            var cb = onClosed;
            onClosed = null;
            Destroy(gameObject);
            cb?.Invoke(result);
        }

        void OnUIReload(PanelRenderer renderer, VisualElement rootElement, int version)
        {
            root = rootElement;
            root.style.flexGrow = 1;
            root.pickingMode = PickingMode.Ignore;
            touch = root.Q("mapTouch");
            labels = root.Q("mapLabels");
            systemHeader = root.Q("systemHeader");
            keyBox = root.Q("keyBox");
            hints = root.Q("hints");

            InputGlyph.TrackHintsOption(hints);
            dialog = root.Q("dialog");
            energyLine = root.Q<Label>("energyLine");
            backButton = root.Q<Button>("backButton");
            keyButton = root.Q<Button>("keyButton");
            zoomButton = root.Q<Button>("zoomButton");
            dialogYes = root.Q<Button>("dialogYes");
            dialogNo = root.Q<Button>("dialogNo");
            root.Q<Label>("mapTitle").text = T(177).ToUpperInvariant();
            backButton.text = Localization.Extra("hudBack", "BACK");
            keyButton.text = T(400).ToUpperInvariant();
            dialogNo.text = T(135).ToUpperInvariant();
            foreach (var b in new[] { backButton, keyButton, zoomButton, dialogYes, dialogNo })
            {
                b.focusable = false;
                b.RegisterCallback<PointerDownEvent>(_ => Play(assets.buttonPush), TrickleDown.TrickleDown);
            }
            backButton.clicked += () => { Play(assets.buttonRelease); Back(); };
            keyButton.clicked += () => { Play(assets.buttonRelease); ToggleKey(); };
            zoomButton.clicked += () => { Play(assets.buttonRelease); SetOverview(!overview); };
            touch.RegisterCallback<WheelEvent>(e => { if (e.delta.y > 0.01f) SetOverview(true); else if (e.delta.y < -0.01f) SetOverview(false); e.StopPropagation(); });
            touch.RegisterCallback<PointerDownEvent>(e => PinchTrack(e.pointerId, e.localPosition, true), TrickleDown.TrickleDown);
            touch.RegisterCallback<PointerMoveEvent>(e => PinchTrack(e.pointerId, e.localPosition, false), TrickleDown.TrickleDown);
            touch.RegisterCallback<PointerUpEvent>(e => pinch.Remove(e.pointerId), TrickleDown.TrickleDown);
            touch.RegisterCallback<PointerCancelEvent>(e => pinch.Remove(e.pointerId), TrickleDown.TrickleDown);
            dialogYes.clicked += () => AnswerDialog(true);
            dialogNo.clicked += () => AnswerDialog(false);
            touch.RegisterCallback<PointerDownEvent>(OnPointerDown);
            touch.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            touch.RegisterCallback<PointerUpEvent>(e => OnPointerUp(e.pointerId, true));
            touch.RegisterCallback<PointerCancelEvent>(e => OnPointerUp(e.pointerId, false));
            touch.generateVisualContent += DrawLines;
            BuildKey();

            if (world == null)
            {
                currentStation = Session.StationIndex;
                currentSystem = db.Stations.Find(s => s.index == currentStation)?.system ?? 0;
                current = db.Systems.Find(s => s.index == currentSystem);
                visible = GalaxyMap.Visibility(db);
                if (askVoid)
                {
                    root.AddToClassList("map-dialog-only");
                    ShowDialog(T(422), () => Close(new StarMapResult { station = -1, toVoid = true }), RevealMap);
                }
                else if (promptStation >= 0)
                {
                    root.AddToClassList("map-dialog-only");
                    ShowDialog($"{T(574)}: {StationName(promptStation)}\n{T(421)}",
                               () => Close(new StarMapResult { station = promptStation }), RevealMap);
                }
                else RevealMap();
            }
            ApplyInputMode();
            if (pendingHint != null && dialog != null && !DialogOpen) { var hint = pendingHint; pendingHint = null; ShowHint(hint); }
        }

        /// <summary>StarMap::init 0xd6d20: StarMap+0xf4 = campaign mission > 15. Until then (Mido, the tutorial) the map
        /// opens in the current system's system view and Back closes it: no galaxy view (starmap_travel.md 2 / 3).</summary>
        static bool GalaxyAllowed => Session.FreePlay || Session.CampaignMission > 15;

        void RevealMap()
        {
            root.RemoveFromClassList("map-dialog-only");
            BuildWorld();
            BuildSystemItems();
            if (!GalaxyAllowed && revealSystem < 0 && currentSystem >= 0 && currentSystem < suns.Length)
            {
                selected = centred = zoomSystem = currentSystem;
                BuildSystem(currentSystem);
                FillSystemHeader(currentSystem);
                systemView = true;
                zoomDir = 0;
                fade = 0f;
                if (suns[currentSystem] != null) suns[currentSystem].localScale = Vector3.one * sunScale / 3f;
                UpdateCamera();
                BuildHints(InputMode.Current);
                ApplyInputMode();
                return;
            }
            if (mode == StarMapMode.Mission && focusStation >= 0)
            {
                int from = routeFrom >= 0 ? routeFrom : currentSystem, to = SystemOf(focusStation);
                routePath = to >= 0 && from != to ? GalaxyMap.SystemPath(db, from, to) : null;
                routeSegment = 0;
            }
            if (revealSystem >= 0 && revealSystem < suns.Length && suns[revealSystem] != null)
            {
                // StarMap::init with reveal: the camera starts on the system, which grows in over 4000 ms.
                var p = GalaxyMap.SunPosition(db.Systems.Find(s => s.index == revealSystem));
                start = new Vector2(p.x, p.y) / 20f;
                pan = Vector2.zero;
                UpdateCamera();
                revealMs = 0f;
                revealing = true;
                suns[revealSystem].localScale = Vector3.zero;
            }
            else if (focusStation >= 0)
            {
                // The mission map: the target's system selected and gliding to the centre.
                Select(SystemOf(focusStation));
                autoCentre = true;
            }
            ApplyInputMode();
        }

        /// <summary>StarMap::drawOnScreenInfo: the story / freelance icons on the player's own missions' targets
        /// (Status::getCampaignMission / getFreelanceMission), in mission mode too: the map's mission only gets the route.</summary>
        bool StoryShown => Session.StoryMission != null && Session.StoryMission.visible && !Session.FreePlay;
        /// <summary>The story icon on this station (step 59: every convoy target still to do, Story.MapMarks).</summary>
        bool StoryMarked(int station) => (StoryShown && Story.MapMarks(station)) || Events.EventRunner.IsQuestTarget(station);   // remake: event graph quests' targets too
        int FreelanceTarget => Freelance.Active ? Freelance.Mission.target
                             : Events.EventMissions.Active != null ? Events.EventRunner.LocalMissionTarget : -1;

        // ---- 3D --------------------------------------------------------------------------------------------

        static Vector3 U(Vector3 game) => new Vector3(game.x, game.y, -game.z) * M;
        static Vector3 OrbitLayoutDir(Vector3 game) => new Vector3(game.x, game.y, -game.z);

        /// <summary>A game rotation (Rx * Ry * Rz style matrix) to the Unity rotation of an imported model: S * R * M with the
        /// z mirror S and the import's model flip M = diag(-1, 1, 1) (see OrbitLayout.RotationToUnity).</summary>
        static Quaternion ModelRotation(Quaternion game) =>
            (Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.Rotate(game) * Matrix4x4.Scale(new Vector3(-1f, 1f, 1f))).rotation;

        static void SetLayer(GameObject go)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
        }

        GameObject Spawn(string assembly, Transform parent)
        {
            var prefab = AssembledObject.LoadPrefab(db.AssemblyByName(assembly));
            if (prefab == null) return null;
            var go = Instantiate(prefab, parent, false);
            SetLayer(go);
            return go;
        }

        void BuildWorld()
        {
            // StarMap::initLights / the owner's scene: only the map camera renders; no fog, flat ambient.
            foreach (var c in Camera.allCameras) if (c.enabled) { c.enabled = false; disabledCameras.Add(c); }
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude)) if (l.enabled) { l.enabled = false; disabledLights.Add(l); }
            savedAmbientMode = RenderSettings.ambientMode;
            savedAmbient = RenderSettings.ambientLight;
            savedFog = RenderSettings.fog;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.2f, 0.2f, 0.2f);
            RenderSettings.fog = false;

            world = new GameObject("Star Map 3D") { layer = Layer };
            var camGo = new GameObject("Star Map Camera") { layer = Layer };
            camGo.transform.SetParent(world.transform, false);
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 1 << Layer;
            cam.nearClipPlane = 200f * M;
            cam.farClipPlane = 64000f * M;
            cam.depth = 50f;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.transform.rotation = Quaternion.Euler(0f, 180f, 0f);   // game rotation (0, pi, 0): looks along game +Z

            var bg = Spawn("galaxymap_background", world.transform);
            if (bg != null) bg.transform.localPosition = U(new Vector3(-3000f, -2500f, 0f));

            int systemCount = 0;
            foreach (var s in db.Systems) systemCount = Mathf.Max(systemCount, s.index + 1);
            if (suns.Length < systemCount) suns = new Transform[systemCount];
            foreach (var s in db.Systems)
            {
                if (s.index >= suns.Length || !visible[s.index]) continue;
                var sun = Spawn("galaxymap_sun", world.transform);
                if (sun == null) continue;
                sun.name = "Sun " + s.name;
                sun.transform.localPosition = U(GalaxyMap.SunPosition(s));
                sunScale = sun.transform.localScale.x;
                var mat = s.textureIndex >= 0 && s.textureIndex < assets.sunMaterials.Length ? assets.sunMaterials[s.textureIndex] : null;
                if (mat != null) foreach (var r in sun.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;
                suns[s.index] = sun.transform;
            }
            // The wormhole (galaxymap_wormhole, looping animation, rotation (0, pi, 0)) at the attacked system's sun, turned to
            // the camera every frame; scale 0.02 - 0.014 * zoom; in that system's view it moves to the attacked station's planet.
            int wsys = Session.VoidInvasionSystem;
            if (!Session.FreePlay && Session.CampaignMission > 0x1f && wsys >= 0 && wsys < suns.Length && suns[wsys] != null)
            {
                var w = Spawn("galaxymap_wormhole", world.transform);
                if (w != null)
                {
                    w.name = "Wormhole";
                    wormhole = w.transform;
                    wormholeScale = w.transform.localScale;
                    foreach (var a in w.GetComponentsInChildren<GoF2Remake.Visuals.PartAnimation>(true)) a.loop = true;
                }
            }
            // The overview's frame: every visible system's sun (game units).
            overviewMin = new Vector2(float.MaxValue, float.MaxValue);
            overviewMax = new Vector2(float.MinValue, float.MinValue);
            float zSum = 0f; int zCount = 0;
            foreach (var s in db.Systems)
            {
                if (s.index >= suns.Length || suns[s.index] == null) continue;
                var g = GalaxyMap.SunPosition(s);
                overviewMin = Vector2.Min(overviewMin, new Vector2(g.x, g.y));
                overviewMax = Vector2.Max(overviewMax, new Vector2(g.x, g.y));
                zSum += g.z; zCount++;
            }
            if (zCount == 0) overviewMin = overviewMax = Vector2.zero;
            overviewZ = zCount > 0 ? zSum / zCount : 3000f;
            start = current != null ? new Vector2(GalaxyMap.SunPosition(current).x, GalaxyMap.SunPosition(current).y) / 20f : Vector2.zero;
            pan = Vector2.zero;
            UpdateCamera();
        }

        void UpdateWormhole()
        {
            if (wormhole == null || cam == null) return;
            int wsys = Session.VoidInvasionSystem;
            var pos = SunGame(wsys);
            float t = 0f;
            if (zoomSystem == wsys && (systemView || zoomDir != 0))
            {
                t = systemView && zoomDir == 0 ? 1f : ZoomEase;
                int k = planets.FindIndex(p => p.station == Session.VoidInvasionStation);
                if (k >= 0) pos = Vector3.Lerp(pos, PlanetGame(k), t);
            }
            wormhole.localPosition = U(pos);
            wormhole.localScale = wormholeScale * (0.02f - 0.014f * t);   // the prefab is unscaled
            var d = cam.transform.position - wormhole.position;
            if (d.sqrMagnitude > 1e-8f) wormhole.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
        }

        void RestoreScene()
        {
            foreach (var c in disabledCameras) if (c != null) c.enabled = true;
            foreach (var l in disabledLights) if (l != null) l.enabled = true;
            if (disabledCameras.Count > 0 || disabledLights.Count > 0)
            {
                RenderSettings.ambientMode = savedAmbientMode;
                RenderSettings.ambientLight = savedAmbient;
                RenderSettings.fog = savedFog;
            }
            disabledCameras.Clear();
            disabledLights.Clear();
        }

        Vector3 SunGame(int system)
        {
            var s = db.Systems.Find(x => x.index == system);
            return s != null ? GalaxyMap.SunPosition(s) : Vector3.zero;
        }

        /// <summary>The galaxy view's pan limits (camera / 20): the original's [-500, 120] x [-400, 140], widened to every
        /// visible sun (remake, see Update).</summary>
        Vector2 PanMin => Vector2.Min(new Vector2(-500f, -400f), overviewMin / 20f);
        Vector2 PanMax => Vector2.Max(new Vector2(120f, 140f), overviewMax / 20f);

        Vector3 GalaxyCameraGame => new Vector3((start.x + pan.x) * 20f, (start.y + pan.y) * 20f, 0f);
        Vector3 SystemCameraGame(int system) => SunGame(system) - new Vector3(0f, 0f, 500f);

        float ZoomEase => 0.5f - 0.5f * Mathf.Cos(Mathf.PI * Mathf.Clamp01(zoomMs / ZoomMs));

        /// <summary>Remake: the overview camera, back along game -Z until every visible sun (plus a margin for the labels) fits.</summary>
        Vector3 OverviewCameraGame()
        {
            var centre = (overviewMin + overviewMax) / 2f;
            var half = (overviewMax - overviewMin) / 2f + new Vector2(1500f, 1500f);
            float tanV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), tanH = tanV * Mathf.Max(0.1f, cam.aspect);
            // The header and the footer take about a fifth of the height.
            float d = Mathf.Max(half.y / (tanV * 0.8f), half.x / tanH);
            return new Vector3(centre.x, centre.y, Mathf.Min(0f, overviewZ - d));
        }

        Vector3 GalaxyViewGame => overviewT > 0f ? Vector3.Lerp(GalaxyCameraGame, OverviewCameraGame(), OverviewEase) : GalaxyCameraGame;

        void UpdateCamera()
        {
            if (cam == null) return;
            cam.fieldOfView = Aspect.VerticalFov(1.1504f * Mathf.Rad2Deg, cam.aspect);
            Vector3 pos;
            if (zoomDir != 0 || systemView) pos = Vector3.Lerp(GalaxyViewGame, SystemCameraGame(zoomSystem), systemView && zoomDir == 0 ? 1f : ZoomEase);
            else pos = GalaxyViewGame;
            cam.transform.localPosition = U(pos);
            cam.farClipPlane = Mathf.Max(64000f, overviewZ - pos.z + 12000f) * M;
        }

        /// <summary>Remake: in or out of the full-map overview (only in the galaxy view).</summary>
        void SetOverview(bool on)
        {
            if (on == overview || systemView || zoomDir != 0 || revealing || DialogOpen || cam == null) return;
            overview = on;
            vel = Vector2.zero;
            if (on) { autoCentre = false; Play(assets.zoomOut); }
            else Play(assets.zoomIn);
            BuildHints(InputMode.Current);
        }

        /// <summary>Remake: from the overview back to the normal view, centred on 'system' (a tap / Enter on it there).</summary>
        void FocusFromOverview(int system)
        {
            if (system < 0) return;
            Select(system);
            var g = SunGame(system);
            pan = new Vector2(g.x, g.y) / 20f - start;
            centred = -1;
            autoCentre = true;   // confirmed once the camera is back (the spring waits, as for any centring)
            SetOverview(false);
        }

        /// <summary>Remake: two fingers pinching in open the overview, spreading close it.</summary>
        void PinchTrack(int id, Vector3 at, bool down)
        {
            if (!down && !pinch.ContainsKey(id)) return;
            pinch[id] = at;
            if (pinch.Count != 2) { pinchStart = 0f; return; }
            Vector2 a = default, b = default;
            int n = 0;
            foreach (var v in pinch.Values) { if (n++ == 0) a = v; else b = v; }
            float d = Vector2.Distance(a, b);
            if (pinchStart <= 0f || down) { pinchStart = d; return; }
            if (d < pinchStart * 0.75f) { SetOverview(true); pinchStart = d; }
            else if (d > pinchStart * 1.33f) { SetOverview(false); pinchStart = d; }
        }

        /// <summary>StarMap::initStarSystem: planets, orbit rings and a light at the sun.</summary>
        void BuildSystem(int system)
        {
            DestroySystem();
            var sys = db.Systems.Find(s => s.index == system);
            systemRoot = new GameObject("System " + sys?.name) { layer = Layer };
            systemRoot.transform.SetParent(world.transform, false);
            planets.AddRange(GalaxyMap.SystemLayout(db, sys));
            foreach (var p in planets)
            {
                var planet = Spawn($"galaxymap_planet_{p.textureIndex:000}", systemRoot.transform) ?? Spawn("galaxymap_planet_000", systemRoot.transform);
                planetObjects.Add(planet != null ? planet.transform : null);
                planetScales.Add(planet != null ? planet.transform.localScale / 128f : Vector3.zero);
                var orbit = Spawn("galaxymap_orbit", systemRoot.transform);
                if (orbit != null) orbit.transform.localScale = Vector3.one * (2f * p.radius / 65536f / 128f);
                orbitObjects.Add(orbit != null ? orbit.transform : null);
            }
            // One light, direction (0, -1, -1), diffuse 2.0 (ambient 0.2 is the flat scene ambient set in BuildWorld).
            var lightGo = new GameObject("Sun light") { layer = Layer };
            lightGo.transform.SetParent(systemRoot.transform, false);
            lightGo.transform.rotation = Quaternion.LookRotation(OrbitLayoutDir(new Vector3(0f, -1f, -1f)));
            sunLight = lightGo.AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.intensity = 1.6f;
            sunLight.cullingMask = 1 << Layer;
            yaw = 4096f;
            pitch = -4096f;
            yawVel = pitchVel = 0f;
            selectedPlanet = frontPlanet = -1;
            autoRotate = false;
            BuildPlanetItems();
            UpdateSystemTransforms();
        }

        void DestroySystem()
        {
            if (systemRoot != null) Destroy(systemRoot);
            systemRoot = null;
            planets.Clear();
            planetObjects.Clear();
            orbitObjects.Clear();
            planetScales.Clear();
            foreach (var it in planetItems) it.root.RemoveFromHierarchy();
            planetItems.Clear();
        }

        Quaternion ContainerRotation() =>
            Quaternion.AngleAxis(pitch / 65536f * 360f, Vector3.right) * Quaternion.AngleAxis(yaw / 65536f * 360f, Vector3.up)
            * Quaternion.AngleAxis(-180f / 32f, Vector3.forward);

        Vector3 PlanetGame(int k)
        {
            var p = planets[k];
            var local = Quaternion.AngleAxis(p.angle / 65536f * 360f, Vector3.up) * new Vector3(0f, 0f, p.radius);
            return SunGame(zoomSystem) + ContainerRotation() * local / 128f;
        }

        void UpdateSystemTransforms()
        {
            if (systemRoot == null) return;
            var rc = ContainerRotation();
            var sun = SunGame(zoomSystem);
            for (int k = 0; k < planets.Count; k++)
            {
                var pl = planetObjects[k];
                if (pl != null)
                {
                    pl.localPosition = U(PlanetGame(k));
                    pl.localRotation = ModelRotation(rc * Quaternion.AngleAxis(spin * Mathf.Rad2Deg, Vector3.up));
                    pl.localScale = planetScales[k];
                }
                var orbit = orbitObjects[k];
                if (orbit != null)
                {
                    orbit.localPosition = U(sun);
                    orbit.localRotation = ModelRotation(rc * Quaternion.AngleAxis(planets[k].orbitYaw * Mathf.Rad2Deg, Vector3.up));
                }
            }
        }

        // ---- overlay ---------------------------------------------------------------------------------------

        Item MakeItem(bool planet)
        {
            var it = new Item { root = new VisualElement { pickingMode = PickingMode.Ignore } };
            it.root.AddToClassList("map-item");
            it.pulse = Img(it.root, "map-pulse", Tex("map_pulse"));
            it.ring = Img(it.root, "map-ring", Tex("map_ring"));
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("map-name-row");
            var wrap = new VisualElement { pickingMode = PickingMode.Ignore };
            wrap.AddToClassList("map-name-wrap");
            it.name = new Label { pickingMode = PickingMode.Ignore };
            it.name.AddToClassList("map-name");
            it.name.AddToClassList("gof-semibold");
            wrap.Add(it.name);
            if (!planet) { it.raceIcon = Img(wrap, "map-race-icon", null); }
            row.Add(wrap);
            it.root.Add(row);
            it.line1 = Line(it.root);
            it.line2 = Line(it.root);
            it.visited = Img(it.root, "map-icon", Tex("map_visited"));
            it.visited.style.left = 71.5f - 7f - 18f;
            it.visited.style.top = -71.5f + 10f - 35f + 143f;
            it.story = Img(it.root, "map-icon", Tex("map_story"));
            it.freelance = Img(it.root, "map-icon", Tex("map_freelance"));
            foreach (var icon in new[] { it.story, it.freelance })
            {
                icon.style.left = -71.5f - 7f + (icon == it.freelance ? -22f : 0f);
                icon.style.top = -71.5f + 10f;
                icon.style.display = DisplayStyle.None;
            }
            if (planet)
            {
                it.gate = Img(it.root, "map-icon", Tex("gate_icon"));
                it.gate.style.left = 71.5f - 7f;
                it.gate.style.top = -71.5f + 10f;
            }
            labels.Add(it.root);
            return it;
        }

        static VisualElement Img(VisualElement parent, string cls, Texture2D tex)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList(cls);
            if (tex != null)
            {
                e.style.backgroundImage = new StyleBackground(tex);
                if (cls == "map-icon") { e.style.width = tex.width; e.style.height = tex.height; }
            }
            parent.Add(e);
            return e;
        }

        static Label Line(VisualElement parent)
        {
            var l = new Label { pickingMode = PickingMode.Ignore };
            l.AddToClassList("map-line");
            parent.Add(l);
            return l;
        }

        void BuildSystemItems()
        {
            foreach (var s in db.Systems)
            {
                if (s.index >= suns.Length || suns[s.index] == null) continue;
                var it = MakeItem(false);
                it.name.text = s.name;
                // Multiplayer: a faction holding a station here (NetFactions).
                string faction = GoF2Remake.Multiplayer.NetFactionsClient.SystemTag(db, s.index);
                if (faction != null) it.name.text += $" [{faction}]";
                it.name.text += SquadMark(s.index, -1);   // multiplayer: squadmates here
                bool owned = GalaxyMap.HasOwner(s.index) && s.raceId >= 0 && s.raceId <= 3;
                if (owned) it.raceIcon.style.backgroundImage = new StyleBackground(Tex($"race_{s.raceId}"));
                it.raceIcon.style.display = owned ? DisplayStyle.Flex : DisplayStyle.None;
                if (s.index == KaamoClub.SystemIndex && KaamoClub.Owned)
                {
                    // 0x545, the orange house of the owned Kaamo Club, where an owner's race icon would be.
                    it.raceIcon.style.backgroundImage = new StyleBackground(Tex("map_home"));
                    it.raceIcon.AddToClassList("map-race-icon--home");
                    it.raceIcon.style.display = DisplayStyle.Flex;
                }
                bool fully = s.stations.Count > 0 && s.stations.TrueForAll(st => Session.VisitedStations.Contains(st));
                it.visited.style.display = fully ? DisplayStyle.Flex : DisplayStyle.None;
                it.pulse.style.display = s.index == currentSystem ? DisplayStyle.Flex : DisplayStyle.None;
                it.story.style.display = s.stations.Exists(StoryMarked) ? DisplayStyle.Flex : DisplayStyle.None;
                it.freelance.style.display = FreelanceTarget >= 0 && SystemOf(FreelanceTarget) == s.index ? DisplayStyle.Flex : DisplayStyle.None;
                systemItems[s.index] = it;
            }
        }

        void BuildPlanetItems()
        {
            foreach (var p in planets)
            {
                var it = MakeItem(true);
                it.name.text = StationName(p.station, false);
                if (GoF2Remake.Multiplayer.NetFactionsClient.Owner(p.station, out string faction, out _)) it.name.text = $"[{faction}] {it.name.text}";
                it.name.text += SquadMark(-1, p.station);   // multiplayer: squadmates at this station
                it.visited.style.display = Session.VisitedStations.Contains(p.station) ? DisplayStyle.Flex : DisplayStyle.None;
                it.gate.style.display = p.gate ? DisplayStyle.Flex : DisplayStyle.None;
                it.pulse.style.display = p.station == currentStation ? DisplayStyle.Flex : DisplayStyle.None;
                it.story.style.display = StoryMarked(p.station) ? DisplayStyle.Flex : DisplayStyle.None;
                it.freelance.style.display = p.station == FreelanceTarget ? DisplayStyle.Flex : DisplayStyle.None;
                planetItems.Add(it);
            }
        }

        void BuildKey()
        {
            keyBox.Clear();
            // StarMap::drawKey rows (drawn bottom-up there): Story, Freelance, Jumpgate, Already visited, Products finished.
            foreach (var (img, text) in new[] { ("map_story", 555), ("map_freelance", 556), ("gate_icon", 547), ("map_visited", 401), ("map_products", 274) })
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("key-row");
                var cell = new VisualElement { pickingMode = PickingMode.Ignore };
                cell.AddToClassList("key-icon-cell");
                Img(cell, "map-icon", Tex(img)).style.position = Position.Relative;
                row.Add(cell);
                var l = new Label(T(text)) { pickingMode = PickingMode.Ignore };
                l.AddToClassList("key-label");
                row.Add(l);
                keyBox.Add(row);
            }
        }

        void ToggleKey()
        {
            keyShown = !keyShown;
            keyBox.EnableInClassList("key-box--shown", keyShown);
        }

        bool Project(Vector3 unityWorld, out Vector2 panel)
        {
            panel = Vector2.zero;
            if (cam == null || root?.panel == null) return false;
            var v = cam.WorldToViewportPoint(unityWorld);
            if (v.z <= 0f) return false;
            var size = PanelSize;   // the map camera always fills the screen
            panel = new Vector2(v.x * size.x, (1f - v.y) * size.y);
            return true;
        }

        Vector2 PanelSize => root != null ? root.layout.size : new Vector2(1920f, 1080f);

        static void Place(VisualElement e, Vector2 p)
        {
            e.style.left = p.x;
            e.style.top = p.y;
        }

        void UpdateOverlay()
        {
            var size = PanelSize;
            float pulse = (Mathf.Sin(timeMs * 0.005f) + 1f) * 0.5f;
            float galaxyAlpha = fade / 255f, systemAlpha = 1f - galaxyAlpha;

            foreach (var kv in systemItems)
            {
                int i = kv.Key;
                var it = kv.Value;
                bool front = Project(suns[i].position, out var p);
                bool show = front && galaxyAlpha > 0.01f && p.x > -50f && p.y > -50f && p.x < size.x + 50f && p.y < size.y + 50f
                            && !(revealing && i == revealSystem);
                it.root.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show) continue;
                Place(it.root, p);
                bool sel = i == selected, other = selected >= 0 && !sel;
                it.root.style.opacity = galaxyAlpha;
                float labelScale = Mathf.Lerp(1f, 0.75f, OverviewEase);
                it.root.style.scale = new Scale(new Vector3(labelScale, labelScale, 1f));
                it.ring.EnableInClassList("map-ring--selected", sel);
                it.ring.style.backgroundImage = new StyleBackground(Tex(sel ? "map_ring_selected" : "map_ring"));
                it.ring.style.opacity = other ? 64f / 255f : 1f;
                it.name.EnableInClassList("map-name--selected", sel);
                it.name.style.opacity = other ? 64f / 255f : 1f;
                if (it.raceIcon != null) it.raceIcon.style.opacity = other ? 64f / 255f : 1f;
                it.pulse.style.opacity = pulse;
                var sys = db.Systems.Find(s => s.index == i);
                bool owned = GalaxyMap.HasOwner(i) && sys.raceId >= 0 && sys.raceId <= 3;
                float y = 68.5f + 30f;
                if (sel && owned) { it.line1.text = T(406 + sys.raceId); it.line1.style.top = y; y += 30f; }
                it.line1.style.display = sel && owned ? DisplayStyle.Flex : DisplayStyle.None;
                it.line2.style.display = sel ? DisplayStyle.Flex : DisplayStyle.None;
                if (sel) SetSecurity(it.line2, GalaxyMap.SecurityOf(sys), y);
                if (sel) it.root.BringToFront();
            }

            for (int k = 0; k < planetItems.Count; k++)
            {
                var it = planetItems[k];
                bool front = planetObjects[k] != null && Project(planetObjects[k].position, out var p) && systemAlpha > 0.01f;
                it.root.style.display = front ? DisplayStyle.Flex : DisplayStyle.None;
                if (!front) continue;
                Project(planetObjects[k].position, out p);
                Place(it.root, p);
                bool sel = k == selectedPlanet, other = selectedPlanet >= 0 && !sel;
                it.root.style.opacity = systemAlpha;
                it.ring.EnableInClassList("map-ring--selected", sel);
                it.ring.style.backgroundImage = new StyleBackground(Tex(sel ? "map_ring_selected" : "map_ring"));
                it.ring.style.opacity = other ? 64f / 255f : 1f;
                it.name.EnableInClassList("map-name--selected", sel);
                it.name.style.opacity = other ? 64f / 255f : 1f;
                it.pulse.style.opacity = pulse;
                int tech = db.Stations.Find(s => s.index == planets[k].station)?.techLevel ?? 0;
                it.line1.style.display = sel && tech > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                if (sel && tech > 0) { it.line1.text = $"{T(133)}: {tech}"; it.line1.style.top = 68.5f + 30f; }
                it.line2.style.display = DisplayStyle.None;
                if (sel) it.root.BringToFront();
            }

            // System block (system view) and the energy line (with a drive, another system selected).
            bool inSystem = systemView || zoomDir != 0;
            systemHeader.style.display = inSystem && zoomSystem >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
            systemHeader.style.opacity = systemAlpha;
            int energySystem = inSystem ? zoomSystem : selected;
            bool energy = jumpDrive && energySystem >= 0 && energySystem != currentSystem && cells > 0;
            energyLine.style.display = energy ? DisplayStyle.Flex : DisplayStyle.None;
            if (energy)
            {
                energyLine.text = $"{T(578)} {cells} / {cellsInCargo}";
                energyLine.EnableInClassList("energy-line--short", cellsInCargo < cells);
            }
            touch.MarkDirtyRepaint();
        }

        static void SetSecurity(Label l, int level, float top)
        {
            level = Mathf.Clamp(level, 0, 3);
            l.text = T(402 + level);
            l.style.color = (Color)GalaxyMap.SecurityColours[level];
            l.style.top = top;
        }

        void FillSystemHeader(int system)
        {
            var sys = db.Systems.Find(s => s.index == system);
            if (sys == null) return;
            bool owned = GalaxyMap.HasOwner(system) && sys.raceId >= 0 && sys.raceId <= 3;
            var logo = root.Q("systemLogo");
            var tex = owned ? Tex($"logo_{sys.raceId}") : null;
            logo.style.display = tex != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (tex != null) { logo.style.backgroundImage = new StyleBackground(tex); logo.style.width = tex.width; logo.style.height = tex.height; }
            root.Q<Label>("systemName").text = sys.name;
            var race = root.Q<Label>("systemRace");
            race.text = owned ? T(406 + sys.raceId) : "";
            race.style.display = owned ? DisplayStyle.Flex : DisplayStyle.None;
            SetSecurity(root.Q<Label>("systemSecurity"), GalaxyMap.SecurityOf(sys), 0f);
            root.Q<Label>("systemSecurity").style.top = StyleKeyword.Auto;
        }

        /// <summary>StarMap::draw lines: the route network (white, alpha 34) and the pulse toward the gate neighbours.</summary>
        void DrawLines(MeshGenerationContext ctx)
        {
            if (cam == null || fade <= 0f) return;
            var p2d = ctx.painter2D;
            p2d.lineWidth = 2f;
            p2d.lineCap = LineCap.Round;
            float a = Mathf.Min(fade, 34f) / 255f;
            p2d.strokeColor = new Color(1f, 1f, 1f, a);
            foreach (var s in db.Systems)
            {
                if (s.index >= suns.Length || suns[s.index] == null) continue;
                foreach (int to in s.jumpRoutesTo)
                {
                    if (to < 0 || to >= suns.Length || suns[to] == null) continue;
                    if (revealing && (to == revealSystem || s.index == revealSystem)) continue;
                    DrawSegment(p2d, suns[s.index].position, suns[to].position);
                }
            }
            float t = (101f - (timeMs / 10f) % 99f) / 100f;
            DrawMissionRoute(p2d, t);
            if (jumpDrive || current == null || suns[currentSystem] == null) return;
            p2d.strokeColor = new Color(1f, 1f, 1f, Mathf.Min(255f * t, fade) / 255f);
            foreach (int to in current.jumpRoutesTo)
            {
                if (to < 0 || to >= suns.Length || suns[to] == null) continue;
                var target = suns[to].position;
                DrawSegment(p2d, suns[currentSystem].position, target + t * (suns[currentSystem].position - target));
            }
        }

        /// <summary>StarMap::draw, mission mode: the route segment by segment on the pulse's cycle (+0xe8 advances when t wraps).</summary>
        void DrawMissionRoute(Painter2D p2d, float t)
        {
            if (routePath == null || routePath.Count < 2) return;
            if (t > lastRouteT) routeSegment = (routeSegment + 1) % (routePath.Count - 1);   // t wrapped: the next segment
            lastRouteT = t;
            float a = fade / 255f;
            p2d.lineWidth = 3f;
            p2d.strokeColor = new Color(1f, 1f, 0f, a);
            for (int i = 0; i < routeSegment; i++)
            {
                var from = suns[routePath[i]]; var to = suns[routePath[i + 1]];
                if (from != null && to != null) DrawSegment(p2d, from.position, to.position);
            }
            var s0 = suns[routePath[routeSegment]]; var s1 = suns[routePath[routeSegment + 1]];
            if (s0 != null && s1 != null)
            {
                p2d.strokeColor = new Color(1f, 1f, 0f, Mathf.Min(t, a));
                DrawSegment(p2d, s0.position, s1.position + t * (s0.position - s1.position));
            }
            p2d.lineWidth = 2f;
        }

        void DrawSegment(Painter2D p2d, Vector3 a, Vector3 b)
        {
            bool fa = Project(a, out var pa), fb = Project(b, out var pb);
            if (!fa && !fb) return;
            if (!fa || !fb)
            {
                // Clip the end behind the camera to just in front of the near plane.
                var near = cam.transform.position + cam.transform.forward * (cam.nearClipPlane * 1.01f);
                var n = cam.transform.forward;
                Vector3 front = fa ? a : b, back = fa ? b : a;
                float da = Vector3.Dot(front - near, n), db2 = Vector3.Dot(back - near, n);
                var clipped = Vector3.Lerp(front, back, da / (da - db2));
                if (!Project(clipped, out var pc)) return;
                if (fa) pb = pc; else pa = pc;
            }
            p2d.BeginPath();
            p2d.MoveTo(pa);
            p2d.LineTo(pb);
            p2d.Stroke();
        }

        /// <summary>Multiplayer: the squadmates in 'system' (a count) or at 'station' (their names), green; red with "⚠" when
        /// one of them calls for help (NetDistress). "" = none (or no session). Read when the map opens.</summary>
        string SquadMark(int system, int station)
        {
            if (!GoF2Remake.Multiplayer.NetGame.Active) return "";
            var me = GoF2Remake.Multiplayer.NetPlayer.Local;
            int count = 0;
            bool distress = false;
            var names = new System.Text.StringBuilder();
            foreach (var m in GoF2Remake.Multiplayer.NetSquad.Members())
            {
                if (m == null || m == me || GoF2Remake.Multiplayer.NetArena.IsArenaOrbit(m.Station)) continue;
                var st = db.Stations.Find(x => x.index == m.Station);
                if (st == null || (station >= 0 ? st.index != station : st.system != system)) continue;
                count++;
                distress |= m.Distress;
                if (names.Length > 0) names.Append(", ");
                names.Append(m.Distress ? "⚠ " : "").Append(m.DisplayName);
            }
            if (count == 0) return "";
            string colour = distress ? "#ff6655" : "#78e68c";
            return station >= 0 ? $"\n<color={colour}>● {names}</color>" : $" <color={colour}>{(distress ? "⚠" : "●")}{count}</color>";
        }

        // ---- selection rules -------------------------------------------------------------------------------

        string StationName(int station, bool suffix = true)
        {
            var st = db.Stations.Find(s => s.index == station);
            if (st == null) return "";
            return !suffix || st.index == 101 ? st.name : $"{st.name} {T(136)}";
        }

        int SystemOf(int station) => db.Stations.Find(s => s.index == station)?.system ?? -1;

        void Select(int system)
        {
            if (system != selected && system >= 0) Play(assets.selectSystem);
            selected = system;   // the centred system (+0x19c) is kept: a tap on it zooms in on release
            cells = 0;
            noGate = false;
            if (system >= 0) cells = GalaxyMap.EnergyCells(db, currentSystem, system, out noGate);
            cellsInCargo = GalaxyMap.CellsInCargo();
        }

        int SystemAt(Vector2 p)
        {
            foreach (var s in db.Systems)
            {
                if (s.index >= suns.Length || suns[s.index] == null) continue;
                if (!Project(suns[s.index].position, out var q)) continue;
                if (Mathf.Abs(q.x - p.x) <= TapRadius && Mathf.Abs(q.y - p.y) <= TapRadius) return s.index;
            }
            return -1;
        }

        int PlanetAt(Vector2 p)
        {
            for (int k = 0; k < planetObjects.Count; k++)
            {
                if (planetObjects[k] == null || !Project(planetObjects[k].position, out var q)) continue;
                if (Mathf.Abs(q.x - p.x) <= TapRadius && Mathf.Abs(q.y - p.y) <= TapRadius) return k;
            }
            return -1;
        }

        /// <summary>The second tap on the centred system: allowed for the current system and its gate neighbours, or any
        /// visible system with a drive (SolarSystem::systemIsInSystemRoutes), else 420.</summary>
        void TryZoomIn()
        {
            if (selected < 0) return;
            if (!jumpDrive && mode != StarMapMode.Mission && !GalaxyMap.IsInRoutes(current, selected)) { ShowDialog(T(420), null, null, true); return; }
            Play(assets.zoomIn);
            zoomSystem = selected;
            BuildSystem(selected);
            FillSystemHeader(selected);
            vel = Vector2.zero;
            autoCentre = false;
            zoomDir = 1;
            zoomMs = 0f;
            BuildHints(InputMode.Current);
        }

        void ZoomOut()
        {
            Play(assets.zoomOut);
            yawVel = pitchVel = 0f;
            autoRotate = false;
            systemView = false;
            zoomDir = -1;
            zoomMs = ZoomMs;
            BuildHints(InputMode.Current);
        }

        // ---- 102 Map_Whoosh (StarMap::OnTouchBegin / OnTouchMove / update, the FEV's LGCY data) ------------------
        // The event is a drag odometer: StarMap+0x1c0 (0..100, back to 0 past 100) grows by min(|dx + dy|, 10) per touch
        // move in the galaxy view and by min(|yaw + pitch velocity| * 0.01, 10) per frame in the system view, and is the
        // event's parameter Whoosh_Loop_Speed_Param. Its two layers hold oneshot Map_Click_01 regions at 0-8.07 /
        // 49.8-59.8 and 25-35 / 75-85: entering one plays a click (event volume 0.439). A touch-down restarts the event
        // (stop + play, the value kept), the back button stops it; a touch-up doesn't.
        static readonly Vector2[] WhooshRegions = { new Vector2(0f, 8.07f), new Vector2(25f, 35f), new Vector2(49.8f, 59.8f), new Vector2(75f, 85f) };
        const float WhooshVolume = 0.439f;
        float whoosh;
        bool whooshOn;

        static int WhooshRegion(float v)
        {
            for (int i = 0; i < WhooshRegions.Length; i++) if (v >= WhooshRegions[i].x && v <= WhooshRegions[i].y) return i;
            return -1;
        }

        void StartWhoosh()
        {
            whooshOn = true;
            if (WhooshRegion(whoosh) >= 0) PlayClick();
        }

        void AddWhoosh(float amount)
        {
            if (amount <= 0f) return;
            int before = WhooshRegion(whoosh);
            whoosh += amount;
            bool wrapped = whoosh > 100f;
            if (wrapped) whoosh = 0f;
            int now = WhooshRegion(whoosh);
            if (whooshOn && now >= 0 && (now != before || wrapped)) PlayClick();
        }

        void PlayClick()
        {
            if (assets.mapClick != null) Source().PlayOneShot(GoF2Remake.Modding.ModSounds.Get(assets.mapClick), Settings.SfxVolume * WhooshVolume);
        }

        void Back()
        {
            if (DialogOpen) { AnswerDialog(false); return; }
            whooshOn = false;   // StarMap::OnTouchEnd: the back button stops 0x66
            if (zoomDir != 0) return;
            if (overview) { SetOverview(false); return; }
            if (systemView && GalaxyAllowed) ZoomOut();
            else Close(new StarMapResult { station = -1 });
        }

        /// <summary>StarMap::OnTouchEnd mode 3, the second tap on the front planet.</summary>
        void Confirm(int k)
        {
            int station = planets[k].station;
            if (mode == StarMapMode.Mission && !travel) { ShowDialog(StationName(station), null, null, true); return; }   // view only
            if (station == currentStation) { ShowDialog(T(419), null, null, true); return; }
            bool otherSystem = SystemOf(station) != currentSystem;
            // Remake: the mission map views any system; without a drive a target past the gate neighbours needs a gate route.
            if (travel && !jumpDrive && otherSystem && !GalaxyMap.IsInRoutes(current, SystemOf(station))
                && GalaxyMap.SystemPath(db, currentSystem, SystemOf(station)) == null) { ShowDialog(T(420), null, null, true); return; }
            if (jumpDrive && otherSystem && GalaxyMap.HasVolatileGoods)
            {
                // Ship::hasVolatileGoods: no Khador Drive; a gate neighbour is still reached through the jumpgate.
                if (!GalaxyMap.IsInRoutes(current, SystemOf(station))) { ShowDialog(T(612), null, null, true); return; }
                ShowDialog($"{T(574)}: {StationName(station)}\n{T(421)}", () => Choose(station, false), null);
                return;
            }
            if (jumpDrive && otherSystem)
            {
                if (cellsInCargo < cells && cells != 1) { ShowDialog(T(579), null, null, true); return; }
                if (cellsInCargo < cells) { ShowDialog(T(582), () => Choose(station, false), null); return; }
                if (noGate && cellsInCargo < 2 * cells) { ShowDialog(T(581), () => Choose(station, true), null); return; }
            }
            ShowDialog($"{T(574)}: {StationName(station)}\n{T(421)}", () => Choose(station, jumpDrive && otherSystem), null);
        }

        void Choose(int station, bool instant) =>
            Close(new StarMapResult { station = station, instantJump = instant && SystemOf(station) != currentSystem, cells = cells });

        // ---- dialog (ChoiceWindow: Yes preselected) ---------------------------------------------------------

        bool DialogOpen => dialog != null && dialog.ClassListContains("map-dialog-backdrop--shown");

        /// <summary>Layout::initHelpWindow over the map (the station's first-visit hints 628 / 631). Asked right after Open, the
        /// overlay isn't built yet (PanelRenderer builds it in its reload callback): the hint waits for it. It threw there,
        /// and the half-opened map left the station's HUD on.</summary>
        public void ShowHint(string text)
        {
            if (root == null || dialog == null) { pendingHint = text; return; }
            ShowDialog(text, null, null, true);
        }

        string pendingHint;

        void ShowDialog(string text, Action onYes, Action onNo, bool info = false)
        {
            dialogAction = onYes;
            dialogNoAction = onNo;
            dialogInfo = info;
            root.Q<Label>("dialogText").text = text;
            dialogYes.text = info ? Localization.Extra("ok", "OK") : T(134).ToUpperInvariant();
            dialogNo.style.display = info ? DisplayStyle.None : DisplayStyle.Flex;
            dialog.AddToClassList("map-dialog-backdrop--shown");
            dialogFocus = 0;
            HighlightDialog();
            Play(assets.infoSound);
            BuildHints(InputMode.Current);
        }

        void AnswerDialog(bool yes)
        {
            if (!DialogOpen) return;
            Play(assets.buttonRelease);
            var a = yes && !dialogInfo ? dialogAction : dialogNoAction;
            if (dialogInfo) a = dialogAction ?? dialogNoAction;
            dialog.RemoveFromClassList("map-dialog-backdrop--shown");
            dialogAction = dialogNoAction = null;
            BuildHints(InputMode.Current);
            a?.Invoke();
        }

        void HighlightDialog()
        {
            bool keys = InputMode.Current != InputKind.Touch;
            dialogYes.EnableInClassList("map-button--focus", keys && dialogFocus == 0);
            dialogNo.EnableInClassList("map-button--focus", keys && dialogFocus == 1);
        }

        // ---- pointer input (StarMap::OnTouchBegin / OnTouchMove / OnTouchEnd) -------------------------------

        bool InputBlocked => DialogOpen || zoomDir != 0 || autoRotate || cam == null || revealing;

        // ---- mission route / reveal / volatile goods ---------------------------------------------------------------
        int routeFrom = -1, revealSystem = -1, routeSegment;
        List<int> routePath;
        float revealMs, lastRouteT = 2f;
        bool revealing;
        const float RevealMs = 4000f;

        /// <summary>StarMap::update's reveal: the sun grows, then the system is selected (the simulated centre tap).</summary>
        void UpdateReveal(float dtMs)
        {
            if (!revealing) return;
            revealMs += dtMs;
            float k = Mathf.Clamp01(revealMs / RevealMs);
            if (suns[revealSystem] != null) suns[revealSystem].localScale = Vector3.one * sunScale * k;
            if (revealMs < RevealMs) return;
            revealing = false;
            Select(revealSystem);
            centred = revealSystem;
        }

        void OnPointerDown(PointerDownEvent e)
        {
            if (pointer >= 0 || InputBlocked) return;
            var p = (Vector2)e.localPosition;
            if (p.y <= HeaderBottom || p.y >= PanelSize.y - FooterHeight) return;
            pointer = e.pointerId;
            touch.CapturePointer(pointer);
            downPos = lastPos = p;
            dragged = false;
            autoCentre = false;
            StartWhoosh();
            if (!systemView)
            {
                vel = Vector2.zero;
                Select(SystemAt(p));
            }
            else
            {
                yawVel = pitchVel = 0f;
                int k = PlanetAt(p);
                if (k >= 0) { selectedPlanet = k; Play(assets.planetPush); }
            }
        }

        void OnPointerMove(PointerMoveEvent e)
        {
            if (e.pointerId != pointer) return;
            var p = (Vector2)e.localPosition;
            var d = p - lastPos;
            lastPos = p;
            bool far = Mathf.Abs(p.x - downPos.x) > 3f || Mathf.Abs(p.y - downPos.y) > 3f;
            if (!systemView && (InOverview || pinch.Count > 1)) { if (far) dragged = true; return; }
            if (!systemView)
            {
                pan += d * DragScale;
                vel = d * DragScale;
                AddWhoosh(Mathf.Min(Mathf.Abs(vel.x + vel.y), 10f));
                if (far && !dragged) { selected = centred = -1; cells = 0; }
            }
            else
            {
                yaw = Mathf.Repeat(yaw + d.x * DragScale * 50f, 65536f);
                pitch = Mathf.Clamp(pitch - d.y * DragScale * 50f, -8192f, 8192f);
                yawVel = d.x * DragScale * 50f;
                pitchVel = -d.y * DragScale * 50f;
                if (far && !dragged) selectedPlanet = frontPlanet = -1;
            }
            if (far) dragged = true;
        }

        void OnPointerUp(int id, bool release)
        {
            if (id != pointer) return;
            if (touch.HasPointerCapture(pointer)) touch.ReleasePointer(pointer);
            pointer = -1;
            if (!release) return;
            if (!systemView && InOverview)
            {
                vel = Vector2.zero;
                if (overview && !dragged && selected >= 0) FocusFromOverview(selected);
                return;
            }
            if (!systemView)
            {
                if (vel.magnitude <= 3f) vel = Vector2.zero;
                if (selected >= 0)
                {
                    if (centred == selected) TryZoomIn();
                    else { autoCentre = true; centred = -1; }
                }
            }
            else
            {
                if (Mathf.Abs(yawVel) <= 150f && Mathf.Abs(pitchVel) <= 150f) yawVel = pitchVel = 0f;
                if (selectedPlanet >= 0)
                {
                    if (selectedPlanet == frontPlanet) Confirm(selectedPlanet);
                    else { Play(assets.planetRelease); autoRotate = true; }
                }
            }
        }

        // ---- per frame (StarMap::update) -------------------------------------------------------------------

        void Update()
        {
            if (root == null) return;
            float dtMs = Time.unscaledDeltaTime * 1000f;
            float f = dtMs / (1000f / 60f);   // the original's per-frame factors, at 60 fps
            timeMs += dtMs;
            if (!revealing) HandleKeys(dtMs);
            if (world == null || this == null) return;
            UpdateReveal(dtMs);
            overviewT = Mathf.MoveTowards(overviewT, overview ? 1f : 0f, dtMs / OverviewMs);

            if (zoomDir != 0)
            {
                zoomMs += zoomDir * dtMs;
                float e = ZoomEase;
                fade = 255f * (1f - e);
                if (suns[zoomSystem] != null) suns[zoomSystem].localScale = Vector3.one * Mathf.Lerp(sunScale, sunScale / 3f, e);
                if (zoomDir > 0 && zoomMs >= ZoomMs) { zoomDir = 0; systemView = true; fade = 0f; BuildHints(InputMode.Current); }
                else if (zoomDir < 0 && zoomMs <= 0f)
                {
                    zoomDir = 0;
                    fade = 255f;
                    DestroySystem();
                    if (suns[zoomSystem] != null) suns[zoomSystem].localScale = Vector3.one * sunScale;
                    zoomSystem = -1;
                    BuildHints(InputMode.Current);
                }
            }
            else if (!systemView && !InOverview)
            {
                if (autoCentre && selected >= 0 && suns[selected] != null && Project(suns[selected].position, out var sp))
                {
                    // 1/30 of the offset per 60 Hz frame, applied frame-rate independently.
                    var v = -(sp - PanelSize / 2f) / 30f;
                    if (Mathf.Abs(v.x) <= 2f && Mathf.Abs(v.y) <= 2f) { autoCentre = false; centred = selected; }
                    else pan += v * 30f * (1f - Mathf.Pow(29f / 30f, f));
                }
                else if (pointer < 0 && vel.sqrMagnitude > 0.25f)
                {
                    pan += vel * f;
                    vel *= Mathf.Pow(0.9f, f);
                    if (vel.magnitude <= 0.5f) vel = Vector2.zero;
                }
                // Pan limits: a spring back inside [-500, 120] x [-400, 140] (camera / 20). Not while centring a system:
                // StarMap::update's auto-centre overwrites the spring's velocity, so the camera reaches a sun past the limit
                // (Skor Terpa, system 32, is above it: the two settled ~90 px apart and it could never be zoomed into).
                // Remake: the limits also take in every visible sun (PanMin / PanMax). Six suns lie past the original's box
                // (Loma, Talidor, Ginoya and Skor Terpa above it, Skavac and Me'enkk below), so once centred the spring
                // dragged them back off the centre, up to ~280 px, and a second tap where the system had been missed it.
                var c = start + pan;
                var clamped = Vector2.Max(PanMin, Vector2.Min(PanMax, c));
                if (clamped != c && pointer < 0 && !autoCentre)
                {
                    pan += (clamped - c) * Mathf.Min(1f, 0.1f * f);
                    if (clamped.x != c.x) vel.x = 0f;
                    if (clamped.y != c.y) vel.y = 0f;
                }
            }
            else
            {
                if (autoRotate && selectedPlanet >= 0)
                {
                    float target = 32768f - planets[selectedPlanet].angle;
                    float dy = Mathf.DeltaAngle(yaw / 65536f * 360f, target / 65536f * 360f) / 360f * 65536f;
                    float dp = -3096f - pitch;
                    float k = 1f - Mathf.Pow(0.75f, f);
                    yaw = Mathf.Repeat(yaw + dy * k, 65536f);
                    pitch += dp * k;
                    if (Mathf.Abs(dy) < 11f && Mathf.Abs(dp) < 11f) { autoRotate = false; frontPlanet = selectedPlanet; }
                }
                AddWhoosh(Mathf.Min(Mathf.Abs(yawVel + pitchVel) * 0.01f, 10f) * f);
                if (pointer < 0 && (Mathf.Abs(yawVel) > 0.5f || Mathf.Abs(pitchVel) > 0.5f))
                {
                    yaw = Mathf.Repeat(yaw + yawVel * f, 65536f);
                    pitch = Mathf.Clamp(pitch + pitchVel * f, -8192f, 8192f);
                    float decay = Mathf.Pow(0.9f, f);
                    yawVel *= decay;
                    pitchVel *= decay;
                }
            }

            spin += dtMs * 0.0002f;
            UpdateCamera();
            UpdateSystemTransforms();
            UpdateWormhole();
            UpdateOverlay();
        }

        // ---- keyboard / controller -------------------------------------------------------------------------

        void HandleKeys(float dtMs)
        {
            var kb = GoF2Remake.Multiplayer.NetChat.Keys;   // null while a multiplayer chat line is typed
            var pad = Gamepad.current;
            bool Pressed(Func<Keyboard, bool> k, Func<Gamepad, bool> g) => (kb != null && k(kb)) || (pad != null && g(pad));

            if (Pressed(k => k.escapeKey.wasPressedThisFrame, g => g.buttonEast.wasPressedThisFrame)) { Play(assets.buttonRelease); Back(); return; }
            int padDir = 0;
            Vector2 dpad = pad != null ? pad.dpad.ReadValue() : Vector2.zero;
            if (dpad.x < -0.5f) padDir = 1; else if (dpad.x > 0.5f) padDir = 2; else if (dpad.y > 0.5f) padDir = 3; else if (dpad.y < -0.5f) padDir = 4;
            bool padNew = padDir != 0 && padDir != lastPadDir;
            lastPadDir = padDir;
            bool left = (kb != null && kb.leftArrowKey.wasPressedThisFrame) || (padNew && padDir == 1);
            bool right = (kb != null && kb.rightArrowKey.wasPressedThisFrame) || (padNew && padDir == 2);
            bool up = (kb != null && kb.upArrowKey.wasPressedThisFrame) || (padNew && padDir == 3);
            bool down = (kb != null && kb.downArrowKey.wasPressedThisFrame) || (padNew && padDir == 4);
            bool confirm = Pressed(k => k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame,
                                   g => g.buttonSouth.wasPressedThisFrame);

            if (DialogOpen)
            {
                if (!dialogInfo && (left || right || (kb != null && (kb.aKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame))))
                {
                    dialogFocus = 1 - dialogFocus;
                    HighlightDialog();
                }
                if (confirm) AnswerDialog(dialogFocus == 0);
                return;
            }
            if (world == null || zoomDir != 0) return;
            if (Pressed(k => k.kKey.wasPressedThisFrame, g => g.buttonNorth.wasPressedThisFrame)) { Play(assets.buttonRelease); ToggleKey(); }
            if (!systemView && Pressed(k => k.qKey.wasPressedThisFrame, g => g.buttonWest.wasPressedThisFrame)) SetOverview(!overview);
            if (!systemView && InOverview)
            {
                if (left || right || up || down) SelectInDirection(left ? Vector2.left : right ? Vector2.right : up ? Vector2.down : Vector2.up);
                if (confirm && overview)
                {
                    if (selected < 0) SelectInDirection(Vector2.zero);
                    else FocusFromOverview(selected);
                }
                return;
            }

            Vector2 move = Vector2.zero;
            if (kb != null)
            {
                if (kb.wKey.isPressed) move.y += 1f;
                if (kb.sKey.isPressed) move.y -= 1f;
                if (!systemView && kb.aKey.isPressed) move.x -= 1f;
                if (!systemView && kb.dKey.isPressed) move.x += 1f;
            }
            if (pad != null) { var s = pad.leftStick.ReadValue(); if (s.magnitude > 0.2f) move += s; }
            move = Vector2.ClampMagnitude(move, 1f);

            if (!systemView)
            {
                if (move != Vector2.zero)
                {
                    // Move the view like a drag of 1500 px/s (screen-right = camera toward game -X).
                    pan += new Vector2(-move.x, move.y) * DragScale * 1.5f * dtMs;
                    autoCentre = false;
                    vel = Vector2.zero;
                }
                if (left || right || up || down) SelectInDirection(left ? Vector2.left : right ? Vector2.right : up ? Vector2.down : Vector2.up);
                if (confirm)
                {
                    if (selected < 0) SelectInDirection(Vector2.zero);
                    else if (centred == selected) TryZoomIn();
                    else autoCentre = true;
                }
            }
            else
            {
                if (move != Vector2.zero)
                {
                    yaw = Mathf.Repeat(yaw - move.x * 16f * dtMs, 65536f);
                    pitch = Mathf.Clamp(pitch + move.y * 16f * dtMs, -8192f, 8192f);
                    frontPlanet = -1;
                    autoRotate = false;
                }
                int step = left || (kb != null && kb.aKey.wasPressedThisFrame) ? -1 : right || (kb != null && kb.dKey.wasPressedThisFrame) ? 1 : 0;
                if (step != 0 && planets.Count > 0 && !autoRotate)
                {
                    selectedPlanet = selectedPlanet < 0 ? NearestFrontPlanet() : (selectedPlanet + step + planets.Count) % planets.Count;
                    Play(assets.planetRelease);
                    autoRotate = true;
                }
                if (confirm && !autoRotate)
                {
                    if (selectedPlanet < 0) { selectedPlanet = NearestFrontPlanet(); Play(assets.planetRelease); autoRotate = selectedPlanet >= 0; }
                    else if (selectedPlanet == frontPlanet) Confirm(selectedPlanet);
                    else { Play(assets.planetRelease); autoRotate = true; }
                }
            }
        }

        /// <summary>Keys / D-pad: the nearest visible system on screen in that direction from the selection (or the screen
        /// centre); Vector2.zero = the one nearest the centre.</summary>
        void SelectInDirection(Vector2 dir)
        {
            Vector2 from = PanelSize / 2f;
            if (selected >= 0 && suns[selected] != null && Project(suns[selected].position, out var sp)) from = sp;
            int best = -1;
            float bestScore = float.MaxValue;
            foreach (var s in db.Systems)
            {
                if (s.index == selected || s.index >= suns.Length || suns[s.index] == null) continue;
                if (!Project(suns[s.index].position, out var q)) continue;
                var d = q - from;
                float dist = d.magnitude;
                float score;
                if (dir == Vector2.zero) score = dist;
                else
                {
                    float cos = dist > 0f ? Vector2.Dot(d / dist, dir) : 0f;
                    if (cos < 0.4f) continue;
                    score = dist * (2f - cos);
                }
                if (score < bestScore) { bestScore = score; best = s.index; }
            }
            if (best < 0) return;
            Select(best);
            autoCentre = true;
        }

        int NearestFrontPlanet()
        {
            int best = -1;
            float bestZ = float.MaxValue;
            for (int k = 0; k < planetObjects.Count; k++)
            {
                if (planetObjects[k] == null) continue;
                float z = cam.WorldToViewportPoint(planetObjects[k].position).z;
                if (z < bestZ) { bestZ = z; best = k; }
            }
            return best;
        }

        // ---- input mode / hints ----------------------------------------------------------------------------

        void ApplyInputMode()
        {
            if (root == null) return;
            var kind = InputMode.Current;
            root.EnableInClassList("input-touch", kind == InputKind.Touch);
            root.EnableInClassList("input-keyboard", kind == InputKind.KeyboardMouse);
            root.EnableInClassList("input-gamepad", kind == InputKind.Gamepad);
            if (DialogOpen) HighlightDialog();
            BuildHints(kind);
        }

        void BuildHints(InputKind kind)
        {
            if (hints == null) return;
            hints.Clear();
            string X(string key, string english) => Localization.Extra(key, english);
            string select = X("hudSelect", "SELECT"), back = X("hudBack", "BACK"), confirm = X("hudConfirm", "CONFIRM");
            string zoom = X("mapZoomIn", "ZOOM IN"), moveText = X("mapMove", "MOVE"), turn = X("mapTurn", "TURN");
            string key = T(400).ToUpperInvariant();
            if (DialogOpen)
            {
                if (kind == InputKind.KeyboardMouse)
                {
                    if (!dialogInfo) Hint(select, InputGlyph.Key("←"), InputGlyph.Key("→"));
                    Hint(confirm, InputGlyph.Key("ENTER", true));
                    Hint(back, InputGlyph.Key("ESC"));
                }
                else if (kind == InputKind.Gamepad)
                {
                    if (!dialogInfo) Hint(select, InputGlyph.Pad(PadButton.DPad));
                    Hint(confirm, InputGlyph.Pad(PadButton.A));
                    Hint(back, InputGlyph.Pad(PadButton.B));
                }
                return;
            }
            bool sys = systemView || zoomDir > 0;
            string fullMap = overview ? X("mapCloseUp", "CLOSE UP") : X("mapFullMap", "FULL MAP");
            if (zoomButton != null)
            {
                zoomButton.text = fullMap;
                bool zoomShown = !sys && !revealing;
                zoomButton.style.display = zoomShown ? DisplayStyle.Flex : DisplayStyle.None;
                // Both push to the right edge: the Key button sits right after the Full map button while that shows.
                keyButton.style.marginLeft = zoomShown ? new StyleLength(0f) : new StyleLength(StyleKeyword.Null);
            }
            if (kind == InputKind.KeyboardMouse)
            {
                Hint(select, InputGlyph.Key(sys ? "A" : "←"), InputGlyph.Key(sys ? "D" : "→"));
                if (!overview) Hint(sys ? turn : moveText, InputGlyph.Key("W"), InputGlyph.Key("S"));
                Hint(sys ? confirm : zoom, InputGlyph.Key("ENTER", true));
                if (!sys) Hint(fullMap, InputGlyph.Key("Q"));
                Hint(key, InputGlyph.Key("K"));
                Hint(back, InputGlyph.Key("ESC"));
            }
            else if (kind == InputKind.Gamepad)
            {
                Hint(select, InputGlyph.Pad(PadButton.DPad));
                if (!overview) Hint(sys ? turn : moveText, InputGlyph.Pad(PadButton.LeftStick));
                Hint(sys ? confirm : zoom, InputGlyph.Pad(PadButton.A));
                if (!sys) Hint(fullMap, InputGlyph.Pad(PadButton.X));
                Hint(key, InputGlyph.Pad(PadButton.Y));
                Hint(back, InputGlyph.Pad(PadButton.B));
            }
        }

        void Hint(string label, params VisualElement[] glyphs)
        {
            var h = new VisualElement { pickingMode = PickingMode.Ignore };
            h.AddToClassList("hint");
            foreach (var g in glyphs) h.Add(g);
            var l = new Label(label) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("hint-label");
            l.AddToClassList("gof-semibold");
            h.Add(l);
            hints.Add(h);
        }

        void Play(AudioClip clip)
        {
            if (clip == null) return;
            Source().PlayOneShot(GoF2Remake.Modding.ModSounds.Get(clip), Settings.SfxVolume);
        }

        AudioSource Source()
        {
            var src = GetComponent<AudioSource>();
            if (src == null) { src = gameObject.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 0f; src.ignoreListenerPause = true; }
            return src;
        }

        /// <summary>A one-shot 2D sound that survives the map closing (used by the owners for the jump sounds).</summary>
        public static void PlayDetached(AudioClip clip)
        {
            if (clip == null) return;
            var go = new GameObject("Sound " + clip.name);
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 0f;
            src.PlayOneShot(GoF2Remake.Modding.ModSounds.Get(clip), Settings.SfxVolume);
            Destroy(go, clip.length + 0.5f);
        }
    }
}
