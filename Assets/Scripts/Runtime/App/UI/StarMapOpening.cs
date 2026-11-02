// StarMapOpening.cs
// StarMap: opening and closing (part of the partial StarMap; its fields are in StarMap.cs).

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
    public partial class StarMap
    {
        /// <summary>Opens the map. promptStation >= 0 (jumpgate with a programmed station) first shows only "Destination: X /
        /// Travel to this station?": Yes returns that station, No opens the map.</summary>
        /// <param name="askVoid">StarMap::askForJumpIntoAlienWorld: the Void is reachable. The original asks "Jump to the
        /// Void's system?" (422) first; remake: the footer's Jump to the Void button (V / RB) asks it, No preselected, and
        /// Yes returns toVoid (AskVoid).</param>
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
            ScrollBars.Watch(rootElement);   // Modified: the hangar's scrollbar on every scroll view that needs one
            root.style.flexGrow = 1;
            root.pickingMode = PickingMode.Ignore;
            touch = root.Q("mapTouch");
            labels = root.Q("mapLabels");
            mapSafe = root.Q("mapSafe");
            lastSafeScreen = default;   // insets applied again on the next frame
            // Modified: the layout (the interface size, the chosen font, the safe area) at once: UpdateSafeArea ran only in
            // the first Update, so the map's first frame drew its title at the default size and font and then switched.
            UpdateSafeArea();
            systemHeader = root.Q("systemHeader");
            // Remake mods: the selected planet's lore (stations.json "description"), under the system block.
            planetLore = mapSafe.Q(className: "planet-lore");
            if (planetLore == null)
            {
                planetLore = new VisualElement { pickingMode = PickingMode.Ignore };
                planetLore.AddToClassList("planet-lore");
                var head = new Label { pickingMode = PickingMode.Ignore };
                head.AddToClassList("planet-lore-title"); head.AddToClassList("gof-semibold");
                var body = new Label { pickingMode = PickingMode.Ignore };
                body.AddToClassList("planet-lore-text");
                planetLore.Add(head); planetLore.Add(body);
                mapSafe.Add(planetLore);
            }
            planetLore.style.display = DisplayStyle.None;
            keyBox = root.Q("keyBox");
            hints = root.Q("hints");

            InputGlyph.TrackHintsOption(hints);
            dialog = root.Q("dialog");
            energyLine = root.Q<Label>("energyLine");
            backButton = root.Q<Button>("backButton");
            keyButton = root.Q<Button>("keyButton");
            zoomButton = root.Q<Button>("zoomButton");
            voidButton = root.Q<Button>("voidButton");
            sideButtons = root.Q("mapSideButtons");
            dialogYes = root.Q<Button>("dialogYes");
            dialogNo = root.Q<Button>("dialogNo");
            // Remake: a third answer between Yes and No (ShowDialog's altText), hidden until a dialog has one.
            dialogAlt = root.Q<Button>("dialogAlt");
            if (dialogAlt == null && dialogNo != null)
            {
                dialogAlt = new Button { name = "dialogAlt" };
                foreach (var c in new[] { "map-button", "map-dialog-button", "gof-semibold" }) dialogAlt.AddToClassList(c);
                dialogNo.parent.Insert(dialogNo.parent.IndexOf(dialogNo), dialogAlt);
            }
            if (dialogAlt != null) dialogAlt.style.display = DisplayStyle.None;
            root.Q<Label>("mapTitle").text = T(177).ToUpperInvariant();
            // Modified: opened from a station's Map: "STATION | MAP" (SetStationTitle), as the station's views.
            var mapTitleLabel = root.Q<Label>("mapTitle");
            if (mapTitleLabel != null && mapTitleLabel.parent.Q<Label>("mapViewTitle") == null)
            {
                var view = new Label { name = "mapViewTitle", pickingMode = PickingMode.Ignore };
                view.AddToClassList("map-view-title");
                mapTitleLabel.parent.Insert(mapTitleLabel.parent.IndexOf(mapTitleLabel) + 1, view);
            }
            RefreshStationTitle();
            // Modified: the map's help (the station's first-visit hint) behind a "?" at the header's right, the size of
            // the title (OptionInfo: 1 s of the mouse on it, or a press). Only while there is one (SetHelp). The key hints are
            // the boxes along the bottom (Rework, docs/UI-DESIGN.md "Button prompts": they were an "i" here).
            var header = root.Q(className: "map-header");
            if (header != null && header.Q(name: "mapHelp") == null)
            {
                var help = OptionInfo.Icon(() => HelpText);
                help.name = "mapHelp";
                help.AddToClassList("map-help");
                help.style.position = Position.Relative;
                help.style.width = help.style.height = 40f;
                header.Add(help);
            }
            // Modified: an invisible copy of the station's Menu button (StarMap.uss .map-header-spacer), so the header is as
            // tall as the station's top bar and the title lines up with the station's name whatever the font.
            if (header != null && header.Q(name: "mapHeaderSpacer") == null)
            {
                // A no-break space: the line's height comes from the font, and no letters can show for the frame before
                // the style sheet applies (the text "MENU" flashed there for one frame). Hidden inline for the same reason.
                var spacer = new Label("\u00A0") { name = "mapHeaderSpacer", pickingMode = PickingMode.Ignore };
                spacer.style.visibility = Visibility.Hidden;
                spacer.style.width = 0f;
                spacer.AddToClassList("map-header-spacer");
                spacer.AddToClassList("gof-semibold");
                header.Add(spacer);
            }
            RefreshHelp();
            backButton.text = Localization.Extra("hudBack", "BACK");
            keyButton.text = T(400).ToUpperInvariant();
            voidButton.text = VoidButtonText;
            dialogNo.text = T(135).ToUpperInvariant();
            foreach (var b in new[] { backButton, keyButton, zoomButton, voidButton, dialogYes, dialogAlt, dialogNo })
            {
                b.focusable = false;
                b.RegisterCallback<PointerDownEvent>(_ => Play(assets.buttonPush), TrickleDown.TrickleDown);
            }
            backButton.clicked += () => { Play(assets.buttonRelease); Back(); };
            keyButton.clicked += () => { Play(assets.buttonRelease); ToggleKey(); };
            zoomButton.clicked += () => { Play(assets.buttonRelease); SetOverview(!overview); };
            keyButton.RegisterCallback<GeometryChangedEvent>(_ => FitSideButtons());
            voidButton.clicked += () => { Play(assets.buttonRelease); AskVoid(); };
            touch.RegisterCallback<WheelEvent>(e => { if (e.delta.y > 0.01f) SetOverview(true); else if (e.delta.y < -0.01f) SetOverview(false); e.StopPropagation(); });
            touch.RegisterCallback<PointerDownEvent>(e => PinchTrack(e.pointerId, e.localPosition, true), TrickleDown.TrickleDown);
            touch.RegisterCallback<PointerMoveEvent>(e => PinchTrack(e.pointerId, e.localPosition, false), TrickleDown.TrickleDown);
            touch.RegisterCallback<PointerUpEvent>(e => pinch.Remove(e.pointerId), TrickleDown.TrickleDown);
            touch.RegisterCallback<PointerCancelEvent>(e => pinch.Remove(e.pointerId), TrickleDown.TrickleDown);
            dialogYes.clicked += () => AnswerDialog(true);
            dialogNo.clicked += () => AnswerDialog(false);
            dialogAlt.clicked += AnswerAlt;
            touch.RegisterCallback<PointerDownEvent>(OnPointerDown);
            touch.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            touch.RegisterCallback<PointerUpEvent>(e => OnPointerUp(e.pointerId, true));
            touch.RegisterCallback<PointerCancelEvent>(e => OnPointerUp(e.pointerId, false));
            touch.RegisterCallback<PointerCaptureOutEvent>(e => OnPointerUp(e.pointerId, false));
            touch.RegisterCallback<DetachFromPanelEvent>(_ => CancelPointer());
            touch.generateVisualContent += DrawLines;
            BuildKey();

            if (world == null)
            {
                currentStation = Session.StationIndex;
                currentSystem = db.Stations.Find(s => s.index == currentStation)?.system ?? 0;
                current = db.Systems.Find(s => s.index == currentSystem);
                // A copy: the builder's map shows every system, and the player's own system is always shown (a mod's event can
                // hide it while they are there: GalaxyMap.ForcedHidden; they are taken out of it in flight).
                visible = (bool[])GalaxyMap.Visibility(db).Clone();
                if (currentSystem >= 0 && currentSystem < visible.Length) visible[currentSystem] = true;
                if (builder != null)
                {
                    // The Mod Builder's map: every system, under the builder's panels, without the map's own header / footer. Not
                    // the placeholders of mods that are off ("?", far out at -1000 on every axis): the map zoomed out to frame it.
                    for (int i = 0; i < visible.Length; i++) visible[i] = !Modding.ModWorld.IsMissingSystem(i);
                    // Under the station UI's panel (the shared asset's order): panelSettings is the runtime copy by now, 20 above
                    // it, so "its order - 1" kept the map over the builder's panels.
                    if (runtimePanel != null) runtimePanel.sortingOrder = baseSortingOrder - 1;
                    root.AddToClassList("map-builder");
                    foreach (var n in new[] { "mapTitle", "mapKeys", "mapHelp", "backButton", "keyButton", "zoomButton", "voidButton", "hints" })
                        if (root.Q(n) is VisualElement el) el.style.display = DisplayStyle.None;
                }
                // askVoid: the original asks 422 before the map shows; remake: the map opens and the footer's Jump to the Void
                // button asks it (AskVoid).
                if (promptStation >= 0)
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
        /// <summary>The Khador map's Jump to the Void button (remake, the user's request 2026-10-16; StarMap::askForJumpIntoAlienWorld
        /// asked 422 every time the drive's map opened outside the Void, Yes preselected, so Enter / A out of habit jumped
        /// into it): 422 on demand, No preselected.</summary>
        void AskVoid()
        {
            if (!askVoid || DialogOpen || world == null) return;
            ShowDialog(T(422), () => Close(new StarMapResult { station = -1, toVoid = true }), null, noFirst: true);
        }
        static string VoidButtonText => Localization.Extra("mapVoidJump", "Jump to the Void").ToUpperInvariant();
        /// <summary>Modified: the footer's right-hand column (Jump to the Void, Full map / Close up, Key) as wide as the widest
        /// label any of them can show, so the buttons keep their size when the Full map one turns into Close up or one of
        /// them hides.</summary>
        void FitSideButtons()
        {
            if (sideButtons == null || keyButton == null) return;
            float w = 0f;
            foreach (var text in new[] { VoidButtonText, Localization.Extra("mapFullMap", "FULL MAP"), Localization.Extra("mapCloseUp", "CLOSE UP"), T(400).ToUpperInvariant() })
                w = Mathf.Max(w, keyButton.MeasureTextSize(text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).x);
            if (!(w > 0f)) return;
            var s = keyButton.resolvedStyle;
            w += s.paddingLeft + s.paddingRight + s.borderLeftWidth + s.borderRightWidth + 4f;
            if (Mathf.Abs(sideButtons.resolvedStyle.width - w) > 0.5f) sideButtons.style.width = w;
        }
        /// <summary>Modified: the camera is still on its way in or out of the full-map overview (Full map / Close up wait).</summary>
        bool OverviewMoving => overviewT > 0f && overviewT < 1f;
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
                if (suns[currentSystem] != null) suns[currentSystem].localScale = Vector3.one * SystemSunScale;
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
        int FreelanceTarget => Freelance.Active ? Freelance.MarkedStation
                             : Events.EventMissions.Active != null ? Events.EventRunner.LocalMissionTarget : -1;
        /// <summary>The system with the freelance icon: the marked station's, or the system a Stolen goods briefing names.</summary>
        int FreelanceSystem => SystemOf(Freelance.Active ? Freelance.MapStation : FreelanceTarget);
    }
}
