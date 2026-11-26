using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Multiplayer;
using GoF2Remake.Data;
using GoF2Remake.World;
using GoF2Remake.Visuals;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public sealed partial class StationManagementWindow
    {
        void DestroyPlacementPreview()
        {
            if (placementRoot != null)
            {
                placementRoot.SetActive(false);
                UnityEngine.Object.Destroy(placementRoot);
            }
            placementRoot = null;
            placementDragging = false;
            if (placementTexture != null)
            {
                placementTexture.Release();
                UnityEngine.Object.Destroy(placementTexture);
            }
            placementTexture = null;
        }

        static string PlacementSnapshotOf(PlayerStationRecord station, int orbit)
        {
            if (station == null) return "";
            var modules = station.placements?.Where(p => p != null)
                .Select(p => $"{p.kind}:{p.preset}:{p.x}:{p.y}:{p.z}:{p.hasRotation}:{p.pitch}:{p.yaw}:{p.roll}") ?? Enumerable.Empty<string>();
            var orbital = NetPlayerStations.OrbitalStructures?.Where(p => p != null && p.orbit == orbit)
                .Select(p => $"{p.id}:{p.operational}:{p.placementX}:{p.placementY}:{p.placementZ}:{p.hasRotation}:{p.pitch}:{p.yaw}:{p.roll}") ?? Enumerable.Empty<string>();
            return string.Join("|", modules.Concat(orbital));
        }

        void BuildOrbitPlacement()
        {
            var db = NetGame.Db;
            var station = selected;
            int firstControl = body.contentContainer.childCount;
            var types = new List<(string, string)>();
            for (int i = 1; i <= Modding.ModPlayerInfrastructure.Current.OrbitalDefenseLimit; i++) types.Add(("defense-" + i, "Defense " + i));
            types.AddRange(new[] { ("factory", "Factory"), ("refinery", "Refinery"), ("drydock", "Dry dock") });
            for (int i = 1; i <= 4; i++) types.Add(("dockarm-" + i, "Docking arm " + i));
            AddChoice("placementType", "Project", types, _ => drafts.Remove("placementPreset"));
            string kind = Value("placementType");
            bool orbital = NetPlayerStations.TryDefenseSlot(kind, out int defenseSlot);
            var system = db.Systems.Find(entry => entry.index == station.system);
            int previewOrbit = station.stationIndex;
            placementOrbit = previewOrbit;
            placementSnapshot = PlacementSnapshotOf(station, previewOrbit);
            var layout = OrbitLayout.Build(db, previewOrbit);
            if (layout == null) { AddInfo("Orbit editor", "The orbit preview is unavailable."); return; }
            int race = system?.raceId ?? -1;
            var settings = Modding.ModPlayerInfrastructure.Current;
            var presets = (orbital ? settings.OrbitalStructures.Where(preset =>
                preset.Defense && NetPlayerStations.OrbitalPresetAvailable(preset, race) &&
                settings.OrbitalDefenseLimit > 0) :
                NetPlayerStations.ModuleExteriorChoices(station, kind)).ToList();
            var existing = station.placements?.Find(entry => entry?.kind == kind);
            var builtDefense = orbital ? NetPlayerStations.OrbitalStructures?.FirstOrDefault(entry => entry != null && entry.orbit == previewOrbit && entry.defense && entry.slot == defenseSlot) : null;
            if (builtDefense != null) { AddInfo("Defense " + (defenseSlot + 1), builtDefense.name + " · " + (builtDefense.operational ? "Operational" : "Construction ordered")); AddInfo("Placement", "Fixed once construction is ordered."); return; }
            if (!drafts.ContainsKey("placementPreset") && existing != null) drafts["placementPreset"] = existing.preset;
            AddChoice("placementPreset", "Exterior model", presets.Select(preset => (preset.Id, preset.Name)).ToList());
            var preset = presets.Find(value => value.Id == Value("placementPreset"));
            if (preset == null) return;

            Vector3 stationPosition = NetPlayerStations.StationPosition(previewOrbit);
            string draftKey = station.id + ":" + previewOrbit + ":" + kind;
            bool hasDraft = placementDrafts.TryGetValue(draftKey, out Vector3Int position);
            if (!hasDraft)
                position = existing != null ? new Vector3Int(existing.x, existing.y, existing.z) :
                    (orbital ? Vector3Int.RoundToInt(Quaternion.Euler(0, defenseSlot * 360f / Math.Max(1, settings.OrbitalDefenseLimit), 0) * new Vector3(0, 0, 8000)) : new Vector3Int(0, 0, Mathf.Clamp(Mathf.RoundToInt(layout.stationReach * OrbitLayout.MetersPerUnit * layout.StationScale), 300, 9000)));

            bool dockingArm = kind.StartsWith("dockarm-", StringComparison.Ordinal);
            if (!placementRotationDrafts.TryGetValue(draftKey, out var angles))
                angles = existing?.hasRotation == true ? new Vector3Int(existing.pitch, existing.yaw, existing.roll) :
                    dockingArm && position.sqrMagnitude > 0 ? Vector3Int.RoundToInt(Quaternion.LookRotation((Vector3)position).eulerAngles) : Vector3Int.zero;
            Quaternion Rotation() => Quaternion.Euler(angles);
            Vector3Int Absolute(Vector3Int point) => Vector3Int.RoundToInt(stationPosition) + point;
            string PlacementError(Vector3Int point)
            {
                var error = NetPlayerStations.OrbitalPlacementError(previewOrbit, layout, Absolute(point), preset.HitRadius);
                if (error != null) return error;
                foreach (var other in station.placements ?? new List<PlayerStationPlacement>())
                {
                    if (other == null || other.kind == kind || !NetPlayerStations.TryDefenseSlot(other.kind, out _)) continue;
                    var otherPreset = settings.OrbitalStructures.FirstOrDefault(p => p.Defense && p.Id == other.preset);
                    float separation = Math.Max(200f, preset.HitRadius + (otherPreset?.HitRadius ?? 0f));
                    if ((new Vector3(other.x, other.y, other.z) - point).sqrMagnitude < separation * separation) return "Overlaps " + other.kind;
                }
                return null;
            }

            bool TryNextClearPosition(Vector3Int start, out Vector3Int next)
            {
                float angle = Mathf.Atan2(start.x, start.z) * Mathf.Rad2Deg;
                float baseRadius = Mathf.Max(8000f, NetPlayerStations.OrbitalStationClearance(layout, preset.HitRadius) + 500f);
                const int positionsPerRing = 12;
                for (int ring = 0; ring < 5; ring++)
                {
                    float radius = baseRadius + ring * Mathf.Max(2500f, preset.HitRadius * 2f + 500f);
                    for (int slot = 1; slot <= positionsPerRing; slot++)
                    {
                        Vector3 offset = Quaternion.Euler(0, angle + slot * (360f / positionsPerRing), 0) * new Vector3(0, 0, radius);
                        next = Vector3Int.RoundToInt(offset);
                        if (next != start && PlacementError(next) == null) return true;
                    }
                }
                next = start;
                return false;
            }

            int slotLimit = preset.Defense ? settings.OrbitalDefenseLimit : settings.OtherOrbitalStructureLimit;
            int slotsTaken = orbital ? NetPlayerStations.OrbitalStructures?.Count(entry =>
                entry != null && entry.orbit == previewOrbit && entry.defense == preset.Defense) ?? 0 : 0;
            if (orbital && PlacementError(position) != null && TryNextClearPosition(position, out var initialClear))
                position = initialClear;

            var configuration = body.contentContainer.Children().Skip(firstControl).ToList();
            AddSection("ORBIT PLACEMENT", orbital
                ? "Save each numbered defense position before ordering construction. Amber markers show saved positions and existing projects. Drag empty space to rotate, scroll to zoom, and hold Ctrl to snap."
                : "Drag the marker or its arrows to move; enable rotation mode to turn the selected axis. Drag empty space to orbit the camera, scroll to zoom, and hold Ctrl to snap.");
            var editor = new VisualElement(); editor.AddToClassList("player-station-orbit-layout"); body.Add(editor);
            var scene = new VisualElement(); scene.AddToClassList("player-station-orbit-scene"); editor.Add(scene);
            var controls = new VisualElement(); controls.AddToClassList("player-station-orbit-controls"); editor.Add(controls);
            foreach (var element in configuration) { element.RemoveFromHierarchy(); controls.Add(element); }
            if (orbital)
            {
                var reserved = NetPlayerStations.OrbitalStructures?.Where(entry => entry != null && entry.orbit == previewOrbit && entry.defense == preset.Defense).ToList();
                int built = reserved?.Count(entry => entry.operational) ?? 0;
                AddInfoTo(controls, preset.Defense ? "Defense slots" : "Structure slots", $"{slotsTaken} / {slotLimit} reserved");
                AddInfoTo(controls, "Operational", built.ToString());
                AddInfoTo(controls, "Unbuilt / planned", (slotsTaken - built).ToString());
                if (reserved != null)
                    foreach (var entry in reserved.OrderBy(entry => entry.slot))
                        AddInfoTo(controls, $"Slot {entry.slot + 1}", entry.name + " · " + (entry.operational ? "Operational" : entry.completesUtc > 0 ? "Building" : "Awaiting materials"));
            }
            var cameraControls = new VisualElement(); cameraControls.style.flexDirection = FlexDirection.Row; cameraControls.style.flexWrap = Wrap.Wrap; scene.Add(cameraControls);
            var snap = new Toggle("Snap to 100 m") { value = Value("placementSnap") == "true" }; snap.AddToClassList("player-station-field"); controls.Add(snap);
            snap.RegisterValueChangedCallback(change => drafts["placementSnap"] = change.newValue ? "true" : "false");
            var placementActions = new VisualElement();
            placementActions.style.flexDirection = FlexDirection.Row;
            placementActions.style.flexWrap = Wrap.Wrap;
            controls.Add(placementActions);
            var viewport = new VisualElement();
            viewport.AddToClassList("player-station-orbit-viewport");
            viewport.style.height = compact ? 300 : Mathf.Clamp(panel.resolvedStyle.height * 0.55f, 360f, 720f);
            viewport.style.flexShrink = 0;
            scene.Add(viewport);
            var coordinateLabel = new Label();
            coordinateLabel.AddToClassList("player-station-note");
            controls.Add(coordinateLabel);
            var placementStatus = new Label();
            placementStatus.AddToClassList("player-station-note");
            placementStatus.style.whiteSpace = WhiteSpace.Normal;
            controls.Add(placementStatus);

            const int previewLayer = 31;
            placementRoot = new GameObject("Station placement preview");
            placementRoot.transform.position = new Vector3(0, -1000000f, 0);
            var frame = new GameObject("Placement coordinates").transform;
            frame.SetParent(placementRoot.transform, false);
            var model = OrbitBuilder.SpawnStation(db, layout, frame);
            if (model != null) model.transform.localPosition = Vector3.zero;
            var ghost = OrbitBuilder.Spawn(db, NetPlayerStations.OrbitalStructureAssembly(preset.Assembly, preset.StationModel, db),
                Vector3.zero, Quaternion.identity, "Proposed structure", placementRoot.transform);
            if (ghost != null) ghost.transform.localScale *= preset.ModelScale;
            void SetLayer(Transform rootTransform)
            {
                foreach (var part in rootTransform.GetComponentsInChildren<Transform>(true))
                    if (part.gameObject.layer != ModBuilderGizmo.PickLayer) part.gameObject.layer = previewLayer;
            }
            SetLayer(placementRoot.transform);
            foreach (var collider in frame.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            var cameraObject = new GameObject("Placement camera");
            cameraObject.transform.SetParent(placementRoot.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.025f, .045f, .07f);
            camera.cullingMask = 1 << previewLayer;
            camera.nearClipPlane = 1;
            camera.farClipPlane = 240000;
            placementTexture = new RenderTexture(1024, 512, 24);
            placementTexture.Create();
            camera.targetTexture = placementTexture;
            viewport.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(placementTexture));
            var lightObject = new GameObject("Placement light");
            lightObject.transform.SetParent(placementRoot.transform, false);
            lightObject.transform.rotation = Quaternion.Euler(35, -45, 0);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.cullingMask = 1 << previewLayer;
            light.intensity = 1.4f;

            int[] Units(Vector3 point) => new[] { Mathf.RoundToInt(-point.x / OrbitLayout.MetersPerUnit),
                Mathf.RoundToInt(point.y / OrbitLayout.MetersPerUnit), Mathf.RoundToInt(point.z / OrbitLayout.MetersPerUnit) };
            var mounts = new List<WeaponMount>();
            foreach (var entry in station.placements ?? new List<PlayerStationPlacement>())
                if (previewOrbit == station.stationIndex && entry != null && entry.kind != kind)
                    mounts.Add(new WeaponMount { slotType = 8, position_engine = Units(new Vector3(entry.x, entry.y, entry.z)) });
            if (NetPlayerStations.OrbitalStructures != null)
                foreach (var structure in NetPlayerStations.OrbitalStructures)
                    if (structure != null && structure.orbit == previewOrbit)
                        mounts.Add(new WeaponMount { slotType = 1, position_engine = Units(NetPlayerStations.OrbitalStructurePosition(structure) - stationPosition) });
            int active = mounts.Count;
            mounts.Add(new WeaponMount { slotType = orbital ? 2 : 8, position_engine = Units(position) });
            var handles = new GameObject("Placement handles");
            handles.transform.SetParent(placementRoot.transform, false);
            var gizmo = handles.AddComponent<ModBuilderGizmo>();
            gizmo.cam = camera;
            gizmo.Selected = active;
            gizmo.Bind(frame, mounts);
            SetLayer(handles.transform);
            if (placementCameraKey != draftKey)
            {
                placementCameraKey = draftKey;
                placementDistance = Mathf.Max(1800f, position.magnitude * 1.8f);
                placementYaw = 34;
                placementPitch = 29;
            }
            // Rotation controls operate on the same transform that will be saved by the server.
            var rotateMode = new Toggle("Drag marker to rotate"); controls.Add(rotateMode);
            var localAxes = new Toggle("Local axes") { value = true }; controls.Add(localAxes);
            var axes = new List<string> { "Pitch (X)", "Yaw (Y)", "Roll (Z)" };
            var axisChoice = new PopupField<string>("Rotation axis", axes, 1); controls.Add(axisChoice);
            var steps = new List<string> { "1", "15", "45", "90" };
            var stepChoice = new PopupField<string>("Rotation step (degrees)", steps, 1); controls.Add(stepChoice);
            var pitchField = new IntegerField("Pitch (X)"); var yawField = new IntegerField("Yaw (Y)"); var rollField = new IntegerField("Roll (Z)");
            foreach (var field in new[] { pitchField, yawField, rollField }) { field.isDelayed = true; field.AddToClassList("player-station-field"); controls.Add(field); }
            GameObject dockShip = null;
            WeaponMount previewMount = null;
            var previewStatus = new Label(); previewStatus.style.whiteSpace = WhiteSpace.Normal;
            void SetCamera()
            {
                camera.transform.position = frame.position + Quaternion.Euler(placementPitch, placementYaw, 0) * new Vector3(0, 0, -placementDistance);
                camera.transform.LookAt(frame.position);
            }
            void CameraPreset(string title, float pitch, float yaw)
            {
                var button = new Button(() => { placementPitch = pitch; placementYaw = yaw; SetCamera(); }) { text = title };
                button.AddToClassList("player-station-action"); cameraControls.Add(button);
            }
            CameraPreset("Perspective", 29f, 34f);
            CameraPreset("Top", 89f, 0f);
            CameraPreset("Side", 0f, 90f);
            var orderButton = new Button { text = "Save planned placement" };
            void AddPlacementControl(Button button)
            {
                StyleChoice(button, false);
                button.AddToClassList("player-station-action");
                button.style.flexGrow = 1;
                button.clicked += () => menu?.PlayRelease();
                placementActions.Add(button);
            }
            void UpdatePoint()
            {
                position = Vector3Int.RoundToInt(frame.InverseTransformPoint(gizmo.WorldOf(mounts[active])));
                placementDrafts[draftKey] = position;
                placementRotationDrafts[draftKey] = angles;
                pitchField.SetValueWithoutNotify(angles.x); yawField.SetValueWithoutNotify(angles.y); rollField.SetValueWithoutNotify(angles.z);
                if (ghost != null) ghost.transform.SetPositionAndRotation(frame.TransformPoint(position), Rotation());
                if (dockShip != null && previewMount != null)
                {
                    var pose = NetPlayerStations.DockArmPose(position, preset.HitRadius, Rotation());
                    var shipRotation = Es2Dock.ExternalDockRotation(pose.rotation, previewMount);
                    dockShip.transform.SetPositionAndRotation(Es2Dock.ExternalDockPosition(frame.TransformPoint(pose.position), shipRotation, previewMount), shipRotation);
                    bool overlap = model != null && dockShip.GetComponentsInChildren<Renderer>().Any(shipPart =>
                        model.GetComponentsInChildren<Renderer>().Any(stationPart => shipPart.bounds.Intersects(stationPart.bounds)));
                    previewStatus.text = overlap ? "Warning: preview hull bounds overlap the station. Check clearance." : "Docked pose preview · hull bounds clear of station.";
                }
                coordinateLabel.text = $"X {position.x:N0} m   Y {position.y:N0} m   Z {position.z:N0} m";
                if (!orbital) { placementStatus.text = "Save the planned position and rotation before construction."; return; }
                string error = PlacementError(position);
                placementStatus.text = placementAwaitingSnapshot ? "Placement saved. Synchronizing reserved slots…" : error ?? "Position clear. Save this plan without ordering construction.";
                orderButton.SetEnabled(error == null && !placementOrderPending);
            }
            int RotationStep() => int.Parse(stepChoice.value);
            Vector3 RotationAxis() => axisChoice.index == 0 ? Vector3.right : axisChoice.index == 1 ? Vector3.up : Vector3.forward;
            void Rotate(float amount)
            {
                Quaternion delta = Quaternion.AngleAxis(amount, RotationAxis());
                angles = Vector3Int.RoundToInt((localAxes.value ? Rotation() * delta : delta * Rotation()).eulerAngles);
                UpdatePoint();
            }
            void ReadAngles() { angles = new Vector3Int(Mathf.Clamp(pitchField.value, -360, 360), Mathf.Clamp(yawField.value, -360, 360), Mathf.Clamp(rollField.value, -360, 360)); UpdatePoint(); }
            pitchField.RegisterValueChangedCallback(_ => ReadAngles()); yawField.RegisterValueChangedCallback(_ => ReadAngles()); rollField.RegisterValueChangedCallback(_ => ReadAngles());
            AddPlacementControl(new Button(() => Rotate(-RotationStep())) { text = "Rotate −" });
            AddPlacementControl(new Button(() => Rotate(RotationStep())) { text = "Rotate +" });
            AddPlacementControl(new Button(() => { angles = Vector3Int.zero; UpdatePoint(); }) { text = "Reset rotation" });
            if (dockingArm)
            {
                var ships = db.Ships.Where(s => s != null && Modding.ExternalDockingRules.CanDockExternally(s) && db.MountsOf(s.index, 8)?.Count > 0).ToList();
                if (ships.Count > 0)
                {
                    var names = ships.Select(s => s.name + " #" + s.index).ToList();
                    var shipChoice = new PopupField<string>("Docked preview ship", names, 0); controls.Add(shipChoice);
                    var mountRow = new VisualElement(); controls.Add(mountRow); controls.Add(previewStatus);
                    void PreviewShip()
                    {
                        if (dockShip != null) { dockShip.SetActive(false); UnityEngine.Object.Destroy(dockShip); }
                        dockShip = null; previewMount = null; mountRow.Clear();
                        var ship = ships[shipChoice.index];
                        var shipMounts = db.MountsOf(ship.index, 8);
                        var prefab = AssembledObject.LoadPrefab(db.ShipAssembly(ship.index));
                        if (prefab == null) { previewStatus.text = "Ship model unavailable."; return; }
                        dockShip = UnityEngine.Object.Instantiate(prefab, frame);
                        dockShip.name = "External docking preview ship";
                        SetLayer(dockShip.transform);
                        foreach (var collider in dockShip.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                        var mountChoice = new PopupField<string>("Ship attachment", shipMounts.Select((m, i) => "Hardpoint " + (i + 1)).ToList(), 0);
                        mountRow.Add(mountChoice); previewMount = shipMounts[0];
                        mountChoice.RegisterValueChangedCallback(_ => { previewMount = shipMounts[mountChoice.index]; UpdatePoint(); });
                        UpdatePoint();
                    }
                    shipChoice.RegisterValueChangedCallback(_ => PreviewShip()); PreviewShip();
                }
                else controls.Add(new Label("No compatible external docking ship is available."));
            }
            if (orbital)
                AddPlacementControl(new Button(() =>
                {
                    if (TryNextClearPosition(position, out var clear))
                    {
                        mounts[active].position_engine = Units(clear);
                        UpdatePoint();
                    }
                    else placementStatus.text = "No clear position found nearby. Try moving the marker manually.";
                }) { text = "Next clear position" });
            AddPlacementControl(new Button(() =>
            {
                placementDistance = Mathf.Max(1800f, position.magnitude * 1.8f);
                SetCamera();
            }) { text = "Frame orbit" });
            AddPlacementControl(orderButton);
            orderButton.clicked += () =>
            {
                if (orbital && (placementOrderPending || PlacementError(position) != null)) return;
                Vector3Int point = position;
                string command = $"/station place {station.id} {kind}";
                string request = $"{command} {point.x} {point.y} {point.z} {preset.Id} {angles.x} {angles.y} {angles.z}";
                if (!orbital) { Send(request); return; }
                int version = ++placementRequestVersion;
                string submittedSnapshot = PlacementSnapshotOf(station, previewOrbit);
                placementAwaitingSnapshot = false;
                placementOrderPending = true;
                placementRequestDeadline = Time.unscaledTime + 10f;
                orderButton.SetEnabled(false);
                if (!Dispatch(request, result =>
                {
                    if (version != placementRequestVersion) return;
                    bool accepted = result?.EndsWith("planned placement saved.", StringComparison.Ordinal) == true;
                    // The reply and replicated registry may arrive in either order. Do not allow
                    // another confirmation against stale slots or stale collision geometry.
                    var live = NetPlayerStations.StationAt(CurrentOrbit);
                    placementAwaitingSnapshot = accepted && PlacementSnapshotOf(live, previewOrbit) == submittedSnapshot;
                    placementOrderPending = placementAwaitingSnapshot;
                    if (placementAwaitingSnapshot) placementRequestDeadline = Time.unscaledTime + 10f;
                    if (live?.id == selected?.id) selected = live;
                    if (managementPage == "placement" && !placementDragging) RebuildBody();
                }))
                {
                    placementOrderPending = false;
                    UpdatePoint();
                }
            };
            Vector2 ScreenPoint(Vector2 panelPosition)
            {
                Vector2 p = viewport.WorldToLocal(panelPosition);
                return new Vector2(p.x / viewport.layout.width * camera.pixelWidth,
                    (1f - p.y / viewport.layout.height) * camera.pixelHeight);
            }
            SetCamera();
            UpdatePoint();
            bool orbiting = false, movingInPlane = false, rotating = false;
            float rotationDrag = 0f;
            Plane dragPlane = default;
            Vector3 grabOffset = Vector3.zero;
            viewport.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                var point = ScreenPoint(evt.position);
                bool axis = gizmo.PickAxis(point) >= 0;
                bool marker = gizmo.PickMarker(point) == active;
                rotating = rotateMode.value && (axis || marker);
                rotationDrag = 0f;
                if (!rotating && orbital && marker && !axis)
                {
                    var markerWorld = gizmo.WorldOf(mounts[active]);
                    dragPlane = new Plane(camera.transform.forward, markerWorld);
                    var ray = camera.ScreenPointToRay(point);
                    movingInPlane = dragPlane.Raycast(ray, out float hit);
                    if (movingInPlane) grabOffset = markerWorld - ray.GetPoint(hit);
                }
                orbiting = !rotating && !movingInPlane && (!(axis || marker) || !gizmo.BeginDrag(point));
                placementDragging = true;
                viewport.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            viewport.RegisterCallback<PointerMoveEvent>(evt =>
            {
                var point = ScreenPoint(evt.position);
                if (rotating)
                {
                    rotationDrag += evt.deltaPosition.x * .6f;
                    float step = evt.ctrlKey ? RotationStep() : 1f;
                    float turn = Mathf.Round(rotationDrag / step) * step;
                    if (Mathf.Abs(turn) >= step) { Rotate(turn); rotationDrag -= turn; }
                }
                else if (movingInPlane)
                {
                    var ray = camera.ScreenPointToRay(point);
                    if (dragPlane.Raycast(ray, out float hit))
                    {
                        Vector3 desired = frame.InverseTransformPoint(ray.GetPoint(hit) + grabOffset);
                        if (evt.ctrlKey || snap.value) desired = new Vector3(Mathf.Round(desired.x / 100f), Mathf.Round(desired.y / 100f), Mathf.Round(desired.z / 100f)) * 100f;
                        mounts[active].position_engine = Units(desired);
                        UpdatePoint();
                    }
                }
                else if (gizmo.Dragging)
                {
                    gizmo.Drag(point, evt.ctrlKey || snap.value, Mathf.RoundToInt(100f / OrbitLayout.MetersPerUnit));
                    UpdatePoint();
                }
                else if (orbiting)
                {
                    placementYaw += evt.deltaPosition.x * .4f;
                    placementPitch = Mathf.Clamp(placementPitch - evt.deltaPosition.y * .4f, -80, 80);
                    SetCamera();
                }
                else gizmo.Hover(point);
            });
            viewport.RegisterCallback<PointerUpEvent>(evt =>
            {
                gizmo.EndDrag();
                orbiting = movingInPlane = rotating = placementDragging = false;
                UpdatePoint();
                if (viewport.HasPointerCapture(evt.pointerId)) viewport.ReleasePointer(evt.pointerId);
            });
            viewport.RegisterCallback<PointerCaptureOutEvent>(_ => { gizmo.EndDrag(); orbiting = movingInPlane = rotating = placementDragging = false; });
            viewport.RegisterCallback<WheelEvent>(evt =>
            {
                placementDistance = Mathf.Clamp(placementDistance * Mathf.Pow(1.12f, evt.delta.y), 300, 170000);
                SetCamera();
                evt.StopPropagation();
            });
        }

    }
}
