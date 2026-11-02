// StarMapRules.cs
// StarMap: selection rules, the whoosh, the dialog, the mission route, reveal and volatile goods (part of the partial StarMap; its fields are in StarMap.cs).

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
            PlanRoute(system);
        }
        /// <summary>Remake (jumpgate route planning): the yellow route to a selected system past the gate neighbours (the
        /// neighbours keep the original's pulse), drawn as the Mission map's.</summary>
        void PlanRoute(int system)
        {
            if (!PlansRoute) return;
            routePath = system >= 0 && !GalaxyMap.IsInRoutes(current, system) ? GalaxyMap.SystemPath(db, currentSystem, system) : null;
            routeSegment = 0;
        }
        /// <summary>The gates reach the system from here: a gate neighbour, or (GateRoutes) any system on a gate route.</summary>
        bool GateReachable(int system) =>
            GalaxyMap.IsInRoutes(current, system) || (GateRoutes && GalaxyMap.SystemPath(db, currentSystem, system) != null);
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
        /// visible system with a drive (SolarSystem::systemIsInSystemRoutes), else 420. Remake: the station's and the gate's maps
        /// any system on a gate route too (GateRoutes).</summary>
        void TryZoomIn()
        {
            if (selected < 0) return;
            if (!jumpDrive && mode != StarMapMode.Mission && !GateReachable(selected)) { ShowDialog(T(420), null, null, true); return; }
            Play(assets.zoomIn);
            zoomSystem = selected;
            BuildSystem(selected);
            if (suns[selected] != null) suns[selected].localScale = Vector3.one * SystemSunScale;   // Modified: see Update's zoom
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
            if (DialogOpen) { if (dialogBackCancels) CloseDialog(); else AnswerDialog(false); return; }
            whooshOn = false;   // StarMap::OnTouchEnd: the back button stops 0x66
            if (zoomDir != 0) return;
            if (overview) { SetOverview(false); return; }
            if (systemView && GalaxyAllowed) ZoomOut();
            else if (builder == null) Close(new StarMapResult { station = -1 });
        }
        /// <summary>StarMap::OnTouchEnd mode 3, the second tap on the front planet.</summary>
        void Confirm(int k)
        {
            int station = planets[k].station;
            if (builder != null) { builder.stationPicked?.Invoke(station); return; }
            if (mode == StarMapMode.Mission && !travel) { ShowDialog(StationName(station), null, null, true); return; }   // view only
            if (station == currentStation) { ShowDialog(T(419), null, null, true); return; }
            // Modified: StarMap::OnTouchEnd's campaign checks for the target station (the cabins of 91 / 94, step 142's plasma
            // kit, ...: Story.RequirementRefusal, also the planet jump's): the map let a step start without them (a player's
            // report: 91 with 5 seats of 10 could never finish).
            int refusal = Story.RequirementRefusal(db, station);
            if (refusal >= 0) { ShowDialog(T(refusal), null, null, true); return; }
            // Modified (review of #186): StarMap::OnTouchEnd's step 0x18 check: the target needs a scanner (sort 0xd) and a
            // tractor beam (0x11), else 532 (only the planet jump had it, StorySpace.RefusePlanetJump).
            if (!Session.FreePlay && Story.Index == 24 && Story.Mission != null && station == Story.Mission.station
                && (Shop.FirstMounted(db, 13) == null || Shop.FirstMounted(db, 17) == null)) { ShowDialog(T(532), null, null, true); return; }
            int system = SystemOf(station);
            bool otherSystem = system != currentSystem;
            // Remake: the mission map views any system, and the station's and the gate's maps take any system the gates reach
            // (GateRoutes): without a drive a target past the gate neighbours needs a gate route.
            if (GateRoutes && !jumpDrive && otherSystem && !GateReachable(system)) { ShowDialog(T(420), null, null, true); return; }
            int gateJumps = GateRoutes && otherSystem ? GalaxyMap.GateJumps(db, currentSystem, system) : 0;
            string travelTo = DestinationText(station, gateJumps);
            if (jumpDrive && otherSystem && GalaxyMap.HasVolatileGoods)
            {
                // Ship::hasVolatileGoods: no Khador Drive; a gate neighbour (remake: any system on a gate route) is still
                // reached through the jumpgate.
                if (!GateReachable(system)) { ShowDialog(T(612), null, null, true); return; }
                ShowDialog(travelTo, () => Choose(station, false), null);
                return;
            }
            if (jumpDrive && otherSystem)
            {
                // Remake: where the gates reach the target they are the drive's alternative, so 582 (the jumpgate instead) for
                // any of them, not only a gate neighbour.
                if (cellsInCargo < cells && cells != 1 && gateJumps == 0) { ShowDialog(T(579), null, null, true); return; }
                if (cellsInCargo < cells)
                {
                    ShowDialog(gateJumps > 1 ? $"{T(582)}\n{RouteJumpsText(gateJumps)}" : T(582), () => Choose(station, false), null);
                    return;
                }
                if (noGate && cellsInCargo < 2 * cells) { ShowDialog(T(581), () => Choose(station, true), null); return; }
                // Remake (jumpgate route planning, "or when you don't want to use one"): the drive or the gates, No cancels.
                if (gateJumps > 0)
                {
                    ShowDialog(travelTo, () => Choose(station, true), null, yesText: T(1359).ToUpperInvariant(),
                               altText: Localization.Extra("mapRouteGates", "Jumpgates").ToUpperInvariant(), onAlt: () => Choose(station, false));
                    return;
                }
            }
            ShowDialog(travelTo, () => Choose(station, jumpDrive && otherSystem), null);
        }
        /// <summary>"Destination: X / Travel to this station?", with the gate jumps between when the route takes more than one
        /// (remake).</summary>
        string DestinationText(int station, int gateJumps) =>
            gateJumps > 1 ? $"{T(574)}: {StationName(station)}\n{RouteJumpsText(gateJumps)}\n{T(421)}" : $"{T(574)}: {StationName(station)}\n{T(421)}";
        static string RouteJumpsText(int jumps) => string.Format(Localization.Extra("mapRouteJumps", "Jumpgate jumps: {0}"), jumps);
        void Choose(int station, bool instant) =>
            Close(new StarMapResult { station = station, instantJump = instant && SystemOf(station) != currentSystem, cells = cells });
        bool DialogOpen => dialog != null && dialog.ClassListContains("map-dialog-backdrop--shown");
        /// <summary>Layout::initHelpWindow over the map (the station's first-visit hints 628 / 631). Asked right after Open, the
        /// overlay isn't built yet (PanelRenderer builds it in its reload callback): the hint waits for it. It threw there,
        /// and the half-opened map left the station's HUD on.</summary>
        public void ShowHint(string text)
        {
            if (root == null || dialog == null) { pendingHint = text; return; }
            ShowDialog(text, null, null, true);
        }
        /// <summary>Modified: the station's title in the header with "| MAP" after it (null: the map's own title).</summary>
        public void SetStationTitle(string title)
        {
            stationTitle = string.IsNullOrEmpty(title) ? null : title;
            RefreshStationTitle();
        }
        void RefreshStationTitle()
        {
            var title = root?.Q<Label>("mapTitle");
            var view = root?.Q<Label>("mapViewTitle");
            if (title == null || view == null) return;
            bool station = stationTitle != null && builder == null;
            title.text = station ? stationTitle : T(177).ToUpperInvariant();
            view.text = station ? T(177).ToUpperInvariant() : "";
            view.style.display = station ? DisplayStyle.Flex : DisplayStyle.None;
        }
        /// <summary>Modified: the help text the header's "?" shows (null: no "?").</summary>
        /// <summary>Modified: what the "?" shows: the text SetHelp gave, else the map's own help (the station's first-visit
        /// hint 628 / 631, its key variant off touch), so the flight's map (K, the Khador Drive) has it too.</summary>
        string HelpText => helpText ?? DefaultHelp;
        static string DefaultHelp
        {
            get
            {
                int text = Session.WorldIndex > 15 ? 628 : 631;
                return World.FlightHints.KeyTokens(Localization.Get(InputMode.Current != InputKind.Touch ? text + 1 : text));
            }
        }
        public void SetHelp(string text)
        {
            helpText = string.IsNullOrEmpty(text) ? null : text;
            RefreshHelp();
        }
        void RefreshHelp()
        {
            var help = root?.Q(name: "mapHelp");
            if (help != null) help.style.display = HelpText != null ? DisplayStyle.Flex : DisplayStyle.None;
        }
        /// <param name="noFirst">No has the keyboard / controller focus when it opens (else Yes).</param>
        /// <param name="yesText">Remake: Yes named for what it does (null: Yes).</param>
        /// <param name="altText">Remake: a third answer between Yes and No (null: none), running onAlt; Back then closes the
        /// dialog unanswered.</param>
        void ShowDialog(string text, Action onYes, Action onNo, bool info = false, bool noFirst = false,
                        string yesText = null, string altText = null, Action onAlt = null)
        {
            dialogAction = onYes;
            dialogNoAction = onNo;
            dialogAltAction = onAlt;
            dialogInfo = info;
            dialogBackCancels = altText != null && !info;
            root.Q<Label>("dialogText").text = text;
            dialogYes.text = info ? Localization.Extra("ok", "OK") : yesText ?? T(134).ToUpperInvariant();
            dialogNo.style.display = info ? DisplayStyle.None : DisplayStyle.Flex;
            if (dialogAlt != null)
            {
                dialogAlt.text = altText ?? "";
                dialogAlt.style.display = dialogBackCancels ? DisplayStyle.Flex : DisplayStyle.None;
            }
            dialog.AddToClassList("map-dialog-backdrop--shown");
            dialogFocus = noFirst && !info ? DialogButtons.Count - 1 : 0;
            HighlightDialog();
            Play(assets.infoSound);
            BuildHints(InputMode.Current);
        }
        void AnswerDialog(bool yes)
        {
            if (!DialogOpen) return;
            var a = yes && !dialogInfo ? dialogAction : dialogNoAction;
            if (dialogInfo) a = dialogAction ?? dialogNoAction;
            Answer(a);
        }
        void AnswerAlt()
        {
            if (DialogOpen) Answer(dialogAltAction);
        }
        /// <summary>Closes the dialog without an answer (Back on a dialog whose No is a choice).</summary>
        void CloseDialog()
        {
            if (DialogOpen) Answer(null);
        }
        void Answer(Action a)
        {
            Play(assets.buttonRelease);
            dialog.RemoveFromClassList("map-dialog-backdrop--shown");
            dialogAction = dialogNoAction = dialogAltAction = null;
            BuildHints(InputMode.Current);
            a?.Invoke();
        }
        /// <summary>The dialog's answers in their order on screen (Yes, the third answer while shown, No while shown).</summary>
        List<Button> DialogButtons
        {
            get
            {
                var list = new List<Button> { dialogYes };
                if (!dialogInfo && dialogBackCancels && dialogAlt != null) list.Add(dialogAlt);
                if (!dialogInfo) list.Add(dialogNo);
                return list;
            }
        }
        /// <summary>Enter / A on the focused answer.</summary>
        void AnswerFocused()
        {
            var b = DialogButtons[Mathf.Clamp(dialogFocus, 0, DialogButtons.Count - 1)];
            if (b == dialogAlt) AnswerAlt();
            else AnswerDialog(b == dialogYes);
        }
        void MoveDialogFocus(int step)
        {
            int n = DialogButtons.Count;
            dialogFocus = (dialogFocus + step + n) % n;
            HighlightDialog();
        }
        void HighlightDialog()
        {
            bool keys = InputMode.Current != InputKind.Touch;
            var buttons = DialogButtons;
            foreach (var b in new[] { dialogYes, dialogAlt, dialogNo })
                b?.EnableInClassList("map-button--focus", keys && buttons.IndexOf(b) == dialogFocus);
        }
        // ---- pointer input (StarMap::OnTouchBegin / OnTouchMove / OnTouchEnd) -------------------------------
        bool InputBlocked => DialogOpen || zoomDir != 0 || autoRotate || cam == null || revealing;
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
            if (e.button != 0 || pointer >= 0 || InputBlocked) return;
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
                if (builder != null)
                {
                    builder.picked?.Invoke(selected);
                    draggingSystem = selected >= 0 && builder.movable != null && builder.movable(selected) && !InOverview ? selected : -1;
                }
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
            // A release outside the window can be missed. Hovering afterwards must not keep dragging or select a system.
            if (!UiPointerButtons.PrimaryHeld(e)) { OnPointerUp(e.pointerId, false); return; }
            var p = (Vector2)e.localPosition;
            var d = p - lastPos;
            lastPos = p;
            bool far = Mathf.Abs(p.x - downPos.x) > 3f || Mathf.Abs(p.y - downPos.y) > 3f;
            if (!systemView && (InOverview || pinch.Count > 1)) { if (far) dragged = true; return; }
            if (!systemView && draggingSystem >= 0)
            {
                // The builder's own system follows the pointer on its depth's plane.
                if (far) dragged = true;
                if (dragged) MoveSun(draggingSystem, p);
                return;
            }
            if (!systemView)
            {
                pan += d * DragScale;
                vel = d * DragScale;
                AddWhoosh(Mathf.Min(Mathf.Abs(vel.x + vel.y), 10f));
                if (far && !dragged) { selected = centred = -1; cells = 0; PlanRoute(-1); }
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
            if (id < 0 || id != pointer) return;
            // Clear the state before releasing capture: PointerCaptureOut can arrive synchronously.
            pointer = -1;
            pinch.Remove(id);
            if (touch != null && touch.HasPointerCapture(id)) touch.ReleasePointer(id);
            if (!release) { vel = Vector2.zero; yawVel = pitchVel = 0f; }
            if (draggingSystem >= 0)
            {
                int moved = draggingSystem;
                draggingSystem = -1;
                if (dragged)
                {
                    var sys = db.Systems.Find(x => x.index == moved);
                    if (sys != null) builder?.moved?.Invoke(moved, new Vector3(sys.mapPosition.x, sys.mapPosition.y, sys.mapPosition.z));
                    selected = centred = moved;
                    return;
                }
            }
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
        void CancelPointer()
        {
            OnPointerUp(pointer, false);
            pinch.Clear();
            vel = Vector2.zero;
            yawVel = pitchVel = 0f;
        }
        /// <summary>The builder's drag: the sun at the pointer on the plane of its depth, the system's map position from it
        /// (whole numbers, as systems.json writes them).</summary>
        void MoveSun(int system, Vector2 panel)
        {
            var sys = db.Systems.Find(x => x.index == system);
            if (sys == null || cam == null || suns[system] == null) return;
            var size = PanelSize;
            var ray = cam.ViewportPointToRay(new Vector3(panel.x / size.x, 1f - panel.y / size.y, 0f));
            var plane = new Plane(cam.transform.forward, suns[system].position);
            if (!plane.Raycast(ray, out float d)) return;
            var w = ray.GetPoint(d);
            var game = new Vector3(w.x / M, w.y / M, -w.z / M);
            var map = MapPositionOf(game);
            sys.mapPosition.x = Mathf.RoundToInt(map.x);
            sys.mapPosition.y = Mathf.RoundToInt(map.y);
            suns[system].localPosition = U(GalaxyMap.SunPosition(sys));
            touch.MarkDirtyRepaint();
        }
    }
}
