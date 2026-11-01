// ModBuilderView.cs
// Remake-only: the Mod Builder's screen over the hangar (Modding.ModBuilder), in place of the station menu (StationMenu
// hides its own and hands Update over). Built in code with Resources/GoF2ModBuilder/ModBuilder.uss:
//   top bar      the project, the kinds of content (Ships, Items, Characters; the rest greyed out, "Soon"), Save and Exit;
//                a kind swaps the two side panels (Items / Characters: ModBuilderViewItems / ModBuilderViewCharacters,
//                partial parts of this class); Items keeps the hangar, its turntable ship and Test fly; Characters is
//                edited in the station's Space Lounge (no ship, Test fly or Engines there); Skins (ModBuilderViewSkins)
//                puts the skinned game ship on the turntable and flies it;
//   left         the project's ships: new ones from a .glb model (the system's file picker, ModImport.Pick) or changes
//                to one of the game's ships (a picker of the originals), Remove;
//   right        the selected ship's tabs: Ship (name, description, race, armor, cargo, handling, price, equipment
//                slots, hangar height, model, length, turn), Hardpoints (add primary / secondary / turret / engine
//                points by clicking the hull, their list, the selected one's coordinates, mirror, delete, slot counts,
//                the turret's upside-down and the engine's flame size), Loadout (the test flight's weapons and
//                equipment per slot, Auto-fill, god mode and infinite ammo);
//   over the view the Engines slider (off at the left, the throttle, a boost at the right end: the glow and exhaust as in
//   flight);
//   bottom right TEST FLY: saves, lets the game read the project again and takes off into Dis' orbit (ModBuilder).
// The 3D view in between is the hangar with World.ModBuilderGizmo: a drag on a hardpoint or its arrows moves it, a drag
// elsewhere (or the right mouse button, one finger) orbits the camera (the hangar's Inspect ship orbit, HangarInspect),
// the wheel / a pinch / + - zoom, W A S D and the arrows turn, Delete removes the selected point, M mirrors it, Ctrl snaps
// to 10 units, Esc cancels. Changes are written at once; what changes the model (a new model, its size, the engine
// points, the hangar height) makes the game read the project again (ModBuilder.Reload) and the turntable ship is
// rebuilt when its model is ready. Plain C#.

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class ModBuilderView : ModBuilderScreen
    {
        const float M = 0.05f;
        static readonly string[] TypeNames = { "Primary", "Secondary", "Turret", "Engine" };
        static readonly string[] TypeShort = { "P", "S", "T", "E" };
        static readonly int[] Races = { 0, 1, 2, 3, 8, 9 };
        static readonly string[] RaceNames = { "Terran", "Vossk", "Nivelian", "Midorian", "Pirate", "Void" };

        readonly StationLevel level;
        VisualElement viewport, labels;
        readonly VisualElement ship, hardpoints, loadout, shipList;
        readonly Label placeHint;
        readonly Button testFly;
        readonly Button[] tabs = new Button[3];
        readonly ModBuilderGizmo gizmo;
        int tab;
        ProjectShip Current => ModBuilder.Ship;
        // the Races kind (ModBuilderViewRaces)
        VisualElement modRaceList, modRacePage;
        ProjectRace currentModRace;

        // reloading the project (the model changes)
        bool launchPending;
        int boundShip = -2;
        Transform boundTransform;

        // placing a new hardpoint (Add): the next click on the hull
        int placing = -1;
        // the pointer over the 3D view
        enum Drag { None, Gizmo, Camera, Pan }
        Drag drag;
        Vector2 last, downAt;
        float travel;
        readonly TwoFingerTouch touch2 = new TwoFingerTouch();
        Vector2 fling;
        // The drag's turn not yet applied (1080p pixels): pointer moves come at the mouse's rate, not the frame's (0, 1 or 2
        // in a frame), so turning by each one as it came stepped unevenly and felt like dropped frames; the camera now
        // takes it frame by frame, eased over ~25 ms (OrbitEaseMs).
        Vector2 pendingTurn;
        const float OrbitEaseMs = 25f;

        protected override void BuildViewport(VisualElement root)
        {
            viewport = Add(root, new VisualElement { name = "mbvViewport" }, "mbv-viewport");
            viewport.RegisterCallback<PointerDownEvent>(OnPointerDown);
            viewport.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            viewport.RegisterCallback<PointerUpEvent>(OnPointerUp);
            viewport.RegisterCallback<PointerCaptureOutEvent>(_ => EndDrag());
            viewport.RegisterCallback<WheelEvent>(e => { OnWheel(e.delta.y); e.StopPropagation(); });
            labels = Add(root, new VisualElement { pickingMode = PickingMode.Ignore }, "mbv-labels");
        }

        protected override void AddTopButtons(VisualElement top)
        {
            TopButton(top, $"{T("mbHangar", "Hangar")}: {HangarTypeName(Session.StationIndex)}", PickHangar, "mbv-hangar-button");
            TopButton(top, T("mbPlay", "Play"), ShowPlayMenu, "mbv-play-button");
        }

        /// <summary>The play tests (ModBuilderPlay): the game as a player has it, with the project on: from the builder's
        /// station, from the campaign's start, or with one of its quests under way.</summary>
        void ShowPlayMenu()
        {
            void Run(Func<string> start)
            {
                SaveNow();
                ModBuilder.Reload();
                afterReload.Clear();
                string why = start();
                if (why != null) Status(why, true);
            }
            var choices = new List<(string, Texture2D, Action)>
            {
                (T("mbPlayHere", "Play from this station") + "\n" + T("mbPlayHereSub", "Play the game with your mod in it"), null,
                 () => Run(() => { ModBuilderPlay.Start(); return null; })),
            };
            if (Project.Campaign != null)
                choices.Add((T("mbPlayCampaign", "Play the campaign") + "\n" + T("mbPlayCampaignSub", "From its start: its station, ship and story mission"), null, () => Run(ModBuilderPlay.StartCampaign)));
            foreach (var q in Project.Quests)
            {
                var quest = q;
                choices.Add((string.Format(T("mbPlayQuestN", "Play \"{0}\""), q.Title ?? q.Name) + "\n" + T("mbPlayQuestSub", "With the mission under way from its first step"), null,
                             () => Run(() => ModBuilderPlay.StartQuest(quest.Name))));
            }
            ShowPicker(T("mbPlay", "Play"), choices);
        }

        public ModBuilderView(StationLevel level, VisualElement stationRoot, Action<bool> sound) : base(stationRoot, sound)
        {
            this.level = level;

            // ---- left: the ships (or the kind's list) ----
            var left = leftGroups[(int)ModBuilder.Kind.Ships];
            Add(left, new Label(T("mbShips", "Ships").ToUpperInvariant()), "mbv-heading", "gof-semibold");
            var shipScroll = Add(left, new ScrollView(ScrollViewMode.Vertical), "mbv-ship-scroll");
            shipScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            shipScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            new DragScroll(shipScroll);
            shipList = shipScroll;
            Action importAction = ModImport.Available ? ImportModel : null;
            var newShip = Act(left, T("mbNewShipModel", "+ New ship from .glb / .aem"), () => importAction?.Invoke(), "mbv-add");
            newShip.SetEnabled(ModImport.Available);
            Act(left, T("mbNewShipGame", "+ New ship on a game model"), () => PickGameShip(T("mbNewShipGameTitle", "New ship on a game model"), NewShipOnGameModel), "mbv-add");
            Act(left, T("mbNewShipKit", "+ New ship from a kit"), NewShipFromKit, "mbv-add");
            Act(left, T("mbChangeShip", "Change a game ship"), PickOriginal, "mbv-add", "mbv-change");
            Act(left, T("mbRemoveShip", "Remove ship"), AskRemove, "mbv-danger", "mbv-remove");

            // ---- right: the selected ship ----
            var right = rightGroups[(int)ModBuilder.Kind.Ships];
            var tabRow = Add(right, new VisualElement(), "mbv-tabs");
            string[] tabNames = { T("mbTabShip", "Ship"), T("mbTabHardpoints", "Hardpoints"), T("mbTabLoadout", "Loadout") };
            for (int i = 0; i < 3; i++)
            {
                int n = i;
                tabs[i] = Act(tabRow, tabNames[i], () => SelectTab(n), "mbv-tab");
            }
            var scroll = Add(right, new ScrollView(ScrollViewMode.Vertical), "mbv-right-scroll");
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            ship = Add(scroll, new VisualElement(), "mbv-page");
            hardpoints = Add(scroll, new VisualElement(), "mbv-page");
            loadout = Add(scroll, new VisualElement(), "mbv-page");
            BuildItemsPanels(leftGroups[(int)ModBuilder.Kind.Items], rightGroups[(int)ModBuilder.Kind.Items]);
            BuildCharactersPanels(leftGroups[(int)ModBuilder.Kind.Characters], rightGroups[(int)ModBuilder.Kind.Characters]);
            BuildSkinsPanels(leftGroups[(int)ModBuilder.Kind.Skins], rightGroups[(int)ModBuilder.Kind.Skins]);
            BuildBlueprintsPanels(leftGroups[(int)ModBuilder.Kind.Blueprints], rightGroups[(int)ModBuilder.Kind.Blueprints]);
            BuildKitsPanels(leftGroups[(int)ModBuilder.Kind.Kits], rightGroups[(int)ModBuilder.Kind.Kits]);
            BuildCampaignPanels(leftGroups[(int)ModBuilder.Kind.Campaigns], rightGroups[(int)ModBuilder.Kind.Campaigns]);
            BuildInteriorsPanels(leftGroups[(int)ModBuilder.Kind.Interiors], rightGroups[(int)ModBuilder.Kind.Interiors]);
            BuildQuestsPanels(leftGroups[(int)ModBuilder.Kind.Quests], rightGroups[(int)ModBuilder.Kind.Quests]);
            BuildAudioPanels(leftGroups[(int)ModBuilder.Kind.Audio], rightGroups[(int)ModBuilder.Kind.Audio]);
            BuildSystemsPanels(leftGroups[(int)ModBuilder.Kind.Systems], rightGroups[(int)ModBuilder.Kind.Systems]);
            BuildRacesPanels(leftGroups[(int)ModBuilder.Kind.Races], rightGroups[(int)ModBuilder.Kind.Races]);

            // ---- bottom ----
            placeHint = Add(root, new Label { pickingMode = PickingMode.Ignore }, "mbv-place-hint", SafeArea.EdgeClass);
            // The engines: the turntable ship's engine glow and exhaust as in flight, a slider from off through the throttle to
            // a boost at its right end.
            // The view panel (the user's request, 2026-10-22: tidier and toggleable): a header that folds it to one bar (kept for
            // the next runs, PlayerPrefs mb_view_panel), then the engines and the camera as two sections.
            // SafeArea.EdgeClass (with the hint and Test fly): inside the interface's band like the side panels beside it
            // (Interface width 21:9 on a wider screen, the edge margin, a notch); without it the VIEW panel lay over the
            // ships list and Test fly reached past the right panel.
            var engines = uiEngines = Add(root, new VisualElement(), "mbv-engines", "mbv-panel", SafeArea.EdgeClass);
            var head = Add(engines, new Button { focusable = false }, "mbv-view-head");
            Add(head, new Label(T("mbView", "View").ToUpperInvariant()) { pickingMode = PickingMode.Ignore }, "mbv-view-head-title", "gof-semibold");
            var fold = Add(head, new Label { pickingMode = PickingMode.Ignore }, "mbv-view-head-fold");
            var body = Add(engines, new VisualElement(), "mbv-view-body");
            void SetOpen(bool open)
            {
                body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                engines.EnableInClassList("mbv-engines--closed", !open);
                fold.text = open ? "↑" : "↓";
                head.tooltip = open ? T("mbViewHide", "Hide the view panel") : T("mbViewShow", "Show the view panel");
            }
            head.clicked += () =>
            {
                sound(false);
                bool open = body.style.display == DisplayStyle.None;
                PlayerPrefs.SetInt("mb_view_panel", open ? 1 : 0);
                SetOpen(open);
            };
            SetOpen(PlayerPrefs.GetInt("mb_view_panel", 1) == 1);
            var engineRow = Add(body, new VisualElement(), "mbv-engines-row");
            Add(engineRow, new Label(T("mbEngines", "Engines").ToUpperInvariant()), "mbv-engines-title", "gof-semibold");
            engineValue = Add(engineRow, new Label(), "mbv-engines-value");
            engineSlider = Add(body, new Slider(0f, 1f) { value = 0f, focusable = false }, "mbv-engines-slider");
            engineSlider.RegisterValueChangedCallback(e => { engineLevel = e.newValue; ApplyEngines(); });
            ApplyEngines();
            // The camera: the ship from the front, back, sides and above (ModelViewer.Angles, the AI tools' angles), centred.
            Add(body, new Label(T("mbCamera", "Camera").ToUpperInvariant()), "mbv-engines-title", "mbv-views-title", "gof-semibold");
            var views = Add(body, new VisualElement(), "mbv-views");
            foreach (var (angle, key, english) in ViewButtons)
            {
                var b = Add(views, new Button(() => ShowAngle(angle)) { text = T(key, english).ToUpperInvariant(), focusable = false },
                            "mbv-view-button", "gof-semibold");
                b.RegisterCallback<PointerDownEvent>(_ => sound(true), TrickleDown.TrickleDown);
            }
            // Orbit: back around the ship's centre after a pan (the middle click's; touch had no way back from two fingers).
            var orbit = Add(views, new Button(CentreOrbit) { text = T("mbViewOrbit", "Orbit").ToUpperInvariant(), focusable = false },
                            "mbv-view-button", "gof-semibold");
            orbit.tooltip = T("mbViewOrbitHint", "Back around the ship's centre after a pan");
            orbit.RegisterCallback<PointerDownEvent>(_ => sound(true), TrickleDown.TrickleDown);
            testFly = Add(root, new Button(StartTestFlight) { text = T("mbTestFly", "Test fly").ToUpperInvariant() }, "mbv-testfly", "gof-semibold",
                          SafeArea.EdgeClass);
            testFly.RegisterCallback<PointerDownEvent>(_ => sound(true), TrickleDown.TrickleDown);
            FinishLayout();
            // Over everything while the panels fly in or out: nothing takes a click meanwhile.
            uiBlocker = Add(root, new VisualElement { pickingMode = PickingMode.Position }, "mbv-blocker");
            uiBlocker.style.display = DisplayStyle.None;

            var go = new GameObject("Mod Builder gizmo");
            gizmo = go.AddComponent<ModBuilderGizmo>();
            gizmo.cam = level.MainCamera;
            gizmo.Moved += _ => OnMountMoved();

            // Back from a test flight: the flight's wear undone.
            if (ModBuilder.TestFlying) ModBuilder.OnDocked();
            SelectTab(0);
            RebuildShips();
            RebuildPages();
            SelectKind(ModBuilder.OpenKind);
            // The models of a project just opened may still be loading: the turntable shows the right one once they are.
            reloading = !ModShips.Ready;
            ShowProblems();
        }

        void OnWheel(float delta)
        {
            if (ModalOpen) return;
            if (kind == ModBuilder.Kind.Interiors) InteriorWheel(delta);
            else if (InHangar && ShipKind(kind)) level.Inspect.Zoom(Mathf.Pow(0.85f, -Mathf.Sign(delta)));
        }

        // ---- pages ----------------------------------------------------------------------------------------------

        static readonly (string role, string english)[] TextureRoles =
        {
            ("diffuse", "Base colour"), ("normal", "Normal map"), ("metallicSmoothness", "Metallic / smoothness"),
        };

        bool autoHeightAfterReload;

        IntegerField px, py, pz;

        /// <summary>The turret facings offered (game frame: x toward the pilot's left, y up, z forward).</summary>
        static readonly (string key, string label, Vector3 dir)[] TurretFacings =
        {
            ("mbFaceUp", "Up", Vector3.up), ("mbFaceDown", "Down", Vector3.down), ("mbFaceLeft", "Left", Vector3.right),
            ("mbFaceRight", "Right", Vector3.left), ("mbFaceFront", "Front", Vector3.forward), ("mbFaceBack", "Back", Vector3.back),
        };

        // ---- ships ---------------------------------------------------------------------------------------------------

        /// <summary>The kinds of hangar by StationTables.HangarIndex (null: none).</summary>
        static readonly string[] RoomNames = { "Terran", "Vossk", "Nivelian", "Midorian", null, null, null, "Deep science", "Battlestation" };

        // ---- per frame ---------------------------------------------------------------------------------------

        public void Update()
        {
            // Exit pressed: the project is closed and the main menu is loading; nothing here is rebuilt any more.
            if (!ModBuilder.Active) return;
            UpdateRemoveButtons();
            float dt = Time.unscaledDeltaTime * 1000f;
            bool flying = level.PlayerFlying;
            UpdateUiAnimation(flying);
            if (flying || uiShown < 1f)
            {
                nowMs += dt;
                UpdateLayoutClasses();
                if (!flying) return;   // landed, the panels still flying in: no input until they are in place
                if (SpaceLevel.PlayerTriedToFly()) level.SkipPlayerFlight();
                labels.Clear();
                status.style.display = placeHint.style.display = DisplayStyle.None;   // inline styles beat the hiding rule
                return;
            }
            if (!UpdateCommon(dt)) return;
            if (!reloading && !reloadPending && boundShip != Session.ShipIndex) BindShip();
            else if (boundShip >= 0 && level.PlayerShip != null && level.PlayerShip != boundTransform) RebindShip();
            if (launchPending && !reloading && !reloadPending) Launch();
            placeHint.text = placing >= 0 ? string.Format(T("mbPlaceHint", "Click on the hull to place the new {0} point.  Esc cancels."),
                                                          T("mbType" + TypeNames[placing], TypeNames[placing]).ToLowerInvariant())
                           : PlaceHintOfKind();
            placeHint.style.display = placeHint.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            // Amber only while a point is being placed (a mode to finish or cancel); a kind's standing tip is a quiet line
            // sized to its text, which the amber bar across the map was too much for.
            placeHint.EnableInClassList("mbv-place-hint--quiet", placing < 0);
            ReadKeys(dt);
            UpdateCamera(dt);
            UpdateLabels();
            UpdateKind(dt);
        }

        // The panels fly out as the ship takes off and back in once it has landed, like the station menu
        // (StationMenu.UpdateUiAnimation: 450 ms, ease-out cubic, run backwards going away): the top bar up, the ships panel
        // left, the ship panel, Test fly and the engines to their edges, each fading, one after another (the last one first
        // going away). The state at the first frame is taken as it is: back from a test flight the ship flies in with them away.
        const float UiAnimMs = 450f, UiPartMs = 300f;
        float uiShown = 1f;   // 0 away .. 1 in place
        bool uiStateTaken;
        VisualElement uiEngines, uiBlocker;

        float engineLevel;
        readonly Slider engineSlider;
        readonly Label engineValue;
        /// <summary>The slider's right end past this is the boost (the throttle is full there).</summary>
        const float BoostFrom = 0.9f;

        // The view buttons: ModelViewer.Angles' name, the text's key and its English. No view from below: the orbit stays
        // above the pad (HangarInspect), and "top" stops at its pitch limit, ~83 deg.
        static readonly (string angle, string key, string english)[] ViewButtons =
        {
            ("front", "mbViewFront", "Front"), ("back", "mbViewBack", "Back"), ("left", "mbViewLeft", "Left"),
            ("right", "mbViewRight", "Right"), ("top", "mbViewTop", "Top"),
        };

    }
}
