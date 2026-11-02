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
// Remake (the Mod Builder's Systems kind, OpenBuilder / Builder): every system is shown, a tap reports the system (and in its
// view the station) to the builder, the builder's own systems are dragged to another place (moved), nothing is travelled to;
// the map draws under the builder's panels (a lower panel sort order) and its own header and footer hide.
// Remake choices: hidden systems' suns aren't drawn (the original draws all 34, uncertainty 3); the galaxy sun shrinks to
// the system-view sun size while zooming instead of a second sun with the flight sun texture; the reveal's sun grows to
// the normal 0.012 (the original's 0.002 literal, uncertainty 10); the reveal map is view only.

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using GoF2Remake.Visuals;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public partial class StarMap : MonoBehaviour
    {
        const float M = 0.05f;
        const int Layer = 31;
        const float TapRadius = 60f, DragScale = 0.325f, ZoomMs = 32768f / 15f;
        const float HeaderBottom = 58f, FooterHeight = 74f;

        public static StarMap Current { get; private set; }

        /// <summary>The Mod Builder's map (OpenBuilder): what it is told and what it may move.</summary>
        public sealed class Builder
        {
            /// <summary>A system the builder's project owns: it can be dragged.</summary>
            public Func<int, bool> movable;
            /// <summary>A system tapped (-1: none).</summary>
            public Action<int> picked;
            /// <summary>A system dragged to a new place: its star map position (systems.json "position").</summary>
            public Action<int, Vector3> moved;
            /// <summary>A station's planet tapped in a system's view.</summary>
            public Action<int> stationPicked;
        }

        Builder builder;

        /// <summary>The Mod Builder's map over the station (its Systems kind): the galaxy with every system, no travel. 'db' is a
        /// copy of the tables (the drags move its systems).</summary>
        public static StarMap OpenBuilder(Database db, Builder hooks)
        {
            var map = Open(db, StarMapMode.Mission, true, null);
            if (map != null) map.builder = hooks;
            return map;
        }

        /// <summary>The builder's map centres a system (picked in the builder's list).</summary>
        public void FocusSystem(int system)
        {
            if (cam == null) { pendingFocus = system; return; }   // not built yet: once it is (Update)
            if (system < 0 || system >= suns.Length || suns[system] == null) return;
            if (systemView || zoomDir != 0) return;
            if (overview) SetOverview(false);
            Select(system);
            autoCentre = true;
            centred = -1;
        }

        /// <summary>The galaxy view's centre as a star map position (where a new system goes).</summary>
        public Vector3 CentrePosition()
        {
            var g = GalaxyCameraGame;
            return MapPositionOf(new Vector3(g.x, g.y, 3000f));
        }

        /// <summary>GalaxyMap.SunPosition the other way: a sun's game position to the systems' map position.</summary>
        static Vector3 MapPositionOf(Vector3 sun) =>
            new Vector3(100f - (sun.x + 10000f) / 14000f * 100f, 100f - (sun.y + 9000f) / 13000f * 100f, 100f - (sun.z - 1000f) / 6000f * 100f);

        int draggingSystem = -1, pendingFocus = -1;

        /// <summary>The builder's map shows every visible system at once ('on'), or the galaxy view again (the Mod Builder's
        /// MCP view tool).</summary>
        public void BuilderOverview(bool on)
        {
            if (cam == null) return;
            SetOverview(on);
        }

        /// <summary>The builder closes its map.</summary>
        public void CloseBuilder() => Close(new StarMapResult { station = -1 });

        /// <summary>A system's view is shown (or the zoom runs): Esc / B is the map's (back to the galaxy).</summary>
        public bool InSystemView => systemView || zoomDir != 0 || overview || DialogOpen;
        public static bool IsOpen => Current != null;
        /// <summary>The map's own camera (VR draws it into a texture on the floating screen).</summary>
        public Camera MapCamera => cam;

        class Item
        {
            public VisualElement root, ring, pulse, raceIcon, visited, gate, story, freelance, products;
            public Label name, line1, line2;
        }

        Database db;
        StarMapAssets assets;
        StarMapMode mode;
        public StarMapMode Mode => mode;
        bool jumpDrive;
        /// <summary>Remake (players' suggestion): a Mission map that can travel: confirming a station programs it like the
        /// station's map, and a target past the gate neighbours is reached along the gate route (GalaxyMap.NextHop).</summary>
        bool travel;
        /// <summary>Remake (jumpgate route planning, players' suggestion 2026-10-17): the station's and the gate's maps (and a
        /// travelling Mission map) take any system the gates reach, not only the gate neighbours: the yellow route shows the
        /// way, and the ship goes gate by gate (SystemJump, GalaxyMap.NextHop); with a drive the gates are the other choice.</summary>
        bool GateRoutes => travel || mode == StarMapMode.Station || mode == StarMapMode.Gate;
        /// <summary>The route to the selected system the map draws, outside a Mission map (that one keeps its target's).</summary>
        bool PlansRoute => GateRoutes && mode != StarMapMode.Mission;
        /// <summary>The safest route to the selection (or a travelling Mission map's target) where it isn't routePath, else null.</summary>
        List<int> safeRoutePath;
        Action<StarMapResult> onClosed;
        int promptStation = -1, focusStation = -1;
        bool askVoid;

        int currentStation, currentSystem;
        SystemData current;
        bool[] visible;

        // 3D
        GameObject world;
        Camera cam;
        // Remake: the supernova system's sun on the map once the supernova is contained: sn_sun_011, the flight sun the orbit
        // shows from then on, in place of the supernova glow. From 0x9e, as OrbitLayout (Status::inSupernovaSystem: system 27
        // before 0x9e); it switched one step later, so step 158 had the calm sun in flight and the glow on the map.
        const int SupernovaSystem = 27, SupernovaContainedStep = 0x9e;
        static bool SupernovaContained => Session.WorldIndex >= SupernovaContainedStep;   // on the world's index, as OrbitLayout
        Material containedSun;

        /// <summary>A copy of the map sun material 'from' showing sn_sun_011 whole (the supernova material shows a corner of the
        /// map atlas); null without the texture.</summary>
        Material ContainedSunMaterial(Material from)
        {
            if (containedSun != null) return containedSun;
            if (from == null) return null;
            var tex = Resources.Load<Material>("GoF2Backdrop/sn_sun_011")?.mainTexture;
            if (tex == null) return null;
            containedSun = new Material(from) { name = "sn_sun_011 (map)" };
            containedSun.SetTexture("_MainTex", tex);
            containedSun.SetTextureScale("_MainTex", Vector2.one);
            containedSun.SetTextureOffset("_MainTex", Vector2.zero);
            return containedSun;
        }

        /// <summary>Modified: the suns' glow with its dim tail faded out like the original's (Hidden/GoF2/SunGamma, the texture's
        /// colour ^ SunGlowPower): added in linear space with _Glow 2.5 the last few percent of the image showed out to its
        /// edge, a ring round every sun. Copies of the map's sun materials, made once per material for this map.</summary>
        const float SunGlowPower = 1.2f;
        readonly Dictionary<Material, Material> gammaSuns = new Dictionary<Material, Material>();
        readonly List<UnityEngine.Object> gammaMade = new List<UnityEngine.Object>();
        Material sunGammaBlit;

        Material GammaSun(Material from)
        {
            if (from == null) return null;
            if (gammaSuns.TryGetValue(from, out var made)) return made;
            var tex = from.mainTexture;
            if (sunGammaBlit == null)
            {
                var shader = Resources.Load<Shader>("GoF2StarMap/SunGamma");
                if (shader == null) { Debug.LogWarning("StarMap: the Hidden/GoF2/SunGamma shader is missing"); return gammaSuns[from] = from; }
                sunGammaBlit = new Material(shader);
                gammaMade.Add(sunGammaBlit);
            }
            if (tex == null) return gammaSuns[from] = from;
            var rt = new RenderTexture(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = tex.name + " (map glow)", useMipMap = true, autoGenerateMips = true,
                wrapMode = tex.wrapMode, filterMode = tex.filterMode, anisoLevel = tex.anisoLevel,
            };
            BlitGammaSun(tex, rt);
            gammaBlits.Add((rt, tex));
            var copy = new Material(from) { name = from.name + " (map glow)" };
            copy.mainTexture = rt;
            gammaMade.Add(rt);
            gammaMade.Add(copy);
            return gammaSuns[from] = copy;
        }

        /// <summary>The glow copies' render textures with their source, made again when their contents are lost (an Android
        /// app sent to the background with the map open: RenderTexture.IsCreated false), else the suns showed blank.</summary>
        readonly List<(RenderTexture rt, Texture source)> gammaBlits = new List<(RenderTexture, Texture)>();

        void BlitGammaSun(Texture source, RenderTexture rt)
        {
            sunGammaBlit.SetFloat("_Power", SunGlowPower);
            Graphics.Blit(source, rt, sunGammaBlit);
        }

        void RestoreGammaSuns()
        {
            foreach (var (rt, source) in gammaBlits)
                if (rt != null && source != null && sunGammaBlit != null && !rt.IsCreated()) BlitGammaSun(source, rt);
        }

        Transform[] suns = new Transform[64];   // by system number (more with mods' systems: BuildGalaxy grows it)
        float sunScale = 0.012f;
        /// <summary>Modified: the suns' size against their prefab's, one size for every sun in every view: the old system-view
        /// sun (the prefab's third) x 2.3, matched by eye to the original (Shima from Eanya's system view).</summary>
        const float SunSize = 2.3f / 3f;
        /// <summary>Modified: the zoomed system's sun in the system view and all along the zoom: the same size as in the galaxy
        /// view (the original swaps it for the system's own, smaller sun; the remake keeps one size).</summary>
        float SystemSunScale => sunScale;
        /// <summary>StarMap::StarMap: the early-warning wormhole at the system the Void attack (index > 0x1f).</summary>
        Transform wormhole;
        Vector3 wormholeScale = Vector3.one;
        GameObject systemRoot;
        static Shader planetShader;
        static readonly Dictionary<Material, Material> planetMaterials = new Dictionary<Material, Material>();
        static readonly int SunPosId = Shader.PropertyToID("_StarMapSunPos");
        readonly List<GalaxyMap.Planet> planets = new List<GalaxyMap.Planet>();
        readonly List<Transform> planetObjects = new List<Transform>(), orbitObjects = new List<Transform>();
        readonly List<Vector3> planetScales = new List<Vector3>();
        readonly List<Transform> planetDiscs = new List<Transform>();   // remake mods: a station's planetTexture facing the camera
        static readonly Dictionary<string, Material> modPlanetMaterials = new Dictionary<string, Material>();
        static int modPlanetRevision = -1;
        static readonly Dictionary<float, Mesh> wrappedSpheres = new Dictionary<float, Mesh>();
        static Mesh discQuad;
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
        VisualElement root, touch, labels, systemHeader, keyBox, hints, dialog, mapSafe, sideButtons, planetLore;
        /// <summary>The screen size and safe area the map's corners were last kept clear for (SafeArea).</summary>
        Rect lastSafeArea;
        int lastLayout = -1;
        Vector2Int lastSafeScreen;
        Label energyLine;
        Button backButton, keyButton, zoomButton, voidButton, dialogYes, dialogNo;
        /// <summary>Remake: the dialog's third answer between Yes and No (the gates beside the drive), made at runtime.</summary>
        Button dialogAlt;
        Action dialogAction, dialogNoAction, dialogAltAction;
        bool dialogInfo;
        /// <summary>Back (Esc / B) closes the dialog without answering: its No is a choice, not a cancel (the drive or the gates).</summary>
        bool dialogBackCancels;
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

        /// <summary>The map's panel settings asset's own sorting order (the station UI's), before the map's copy raised it.</summary>
        float baseSortingOrder;

        void OnEnable()
        {
            panelRenderer = GetComponent<PanelRenderer>();
            panelRenderer.RegisterUIReloadCallback(OnUIReload);
            if (runtimePanel == null && panelRenderer.panelSettings != null)
            {
                baseSortingOrder = panelRenderer.panelSettings.sortingOrder;   // the shared asset's, before the copy takes its place
                runtimePanel = Instantiate(panelRenderer.panelSettings);
                runtimePanel.sortingOrder = panelRenderer.panelSettings.sortingOrder + 20;   // above the HUD / station menu
                panelRenderer.panelSettings = runtimePanel;   // scaled by UiLayout in UpdateSafeArea
            }
            InputMode.Changed += ApplyInputMode;
        }

        void OnDisable()
        {
            CancelPointer();
            panelRenderer?.UnregisterUIReloadCallback(OnUIReload);
            InputMode.Changed -= ApplyInputMode;
        }

        void OnDestroy()
        {
            RestoreScene();
            if (world != null) Destroy(world);
            if (runtimePanel != null) Destroy(runtimePanel);
            foreach (var o in gammaMade) if (o != null) Destroy(o);   // Modified: the map glow copies (GammaSun)
            if (Current == this) Current = null;
        }

        // ---- overlay ---------------------------------------------------------------------------------------

        int loreStation = int.MinValue;

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

        // ---- dialog (ChoiceWindow: Yes preselected) ---------------------------------------------------------

        string pendingHint;

        string helpText;

        string stationTitle;

        // ---- mission route / reveal / volatile goods ---------------------------------------------------------------
        int routeFrom = -1, revealSystem = -1, routeSegment;
        List<int> routePath;
        float revealMs, lastRouteT = 2f;
        bool revealing;
        const float RevealMs = 4000f;

        void OnApplicationFocus(bool focused)
        {
            if (!focused) CancelPointer();
        }

        // ---- per frame (StarMap::update) -------------------------------------------------------------------

        /// <summary>Remake: the header, system block, key legend and footer buttons inside the device's safe area (the
        /// iPhone's Dynamic Island / notch and home indicator, cutouts); the map itself stays full screen.</summary>
        void UpdateSafeArea()
        {
            if (mapSafe == null) return;
            var rt = runtimePanel != null ? runtimePanel.targetTexture : null;
            var size = rt != null ? new Vector2Int(rt.width, rt.height) : new Vector2Int(Screen.width, Screen.height);
            if (size == lastSafeScreen && Screen.safeArea == lastSafeArea && lastLayout == UiLayout.Revision) return;
            lastSafeScreen = size;
            lastSafeArea = Screen.safeArea;
            lastLayout = UiLayout.Revision;
            UiLayout.Configure(runtimePanel, root, Mathf.Max(1, size.x), Mathf.Max(1, size.y));   // docs/UI-DESIGN.md "Scaling"
            SafeArea.Apply(root, mapSafe, Mathf.Max(1, size.x), Mathf.Max(1, size.y), rt != null, SafeArea.Mode.Position);
        }

        void Update()
        {
            if (root == null) return;
            UpdateSafeArea();
            RestoreGammaSuns();
            if (pendingFocus >= 0 && cam != null) { int focus = pendingFocus; pendingFocus = -1; FocusSystem(focus); }
            float dtMs = Time.unscaledDeltaTime * 1000f;
            float f = dtMs / (1000f / 60f);   // the original's per-frame factors, at 60 fps
            timeMs += dtMs;
            if (!revealing) HandleKeys(dtMs);
            if (world == null || this == null) return;
            UpdateReveal(dtMs);
            overviewT = Mathf.MoveTowards(overviewT, overview ? 1f : 0f, dtMs / OverviewMs);
            // Modified: Full map / Close up clickable again once the camera is there.
            if (zoomButton != null && zoomButton.enabledSelf == OverviewMoving) zoomButton.SetEnabled(!OverviewMoving);

            if (zoomDir != 0)
            {
                zoomMs += zoomDir * dtMs;
                float e = ZoomEase;
                fade = 255f * (1f - e);
                // Modified: no shrink along the zoom: the system's sun keeps one size (SystemSunScale).
                if (suns[zoomSystem] != null) suns[zoomSystem].localScale = Vector3.one * SystemSunScale;
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

    }
}
