// ModBuilderViewInteriors.cs
// The Mod Builder's Hangars & bars kind (part of ModBuilderView), edited in the room itself: the project's interiors.json
// (ProjectInterior, Modding/README.md "Hangars and bars"). Left: the rooms, a new hangar or bar from a .glb or on one of the
// game's rooms ("gof2:hangar_terran": its markers filled in from the game's tables), Remove. Right: the model and its size,
// the camera's lens, the light and fog, a hangar's flights and parked ships, the markers (each from the model's own nodes or
// placed here: click in the room, or "From the view" for a camera; their coordinates), and which of the project's stations
// use it. The station scene shows the room (ModBuilder.PreviewInterior: StationLevel builds it in place of the station's,
// in the hangar or the Space Lounge); a free camera orbits it (StationLevel.FreeCamera: drag turns, the middle button or two fingers pan (a pinch zooms),
// the wheel zooms) and the markers show as coloured balls with their names; "Game camera" shows the room through its
// "camera" marker as the game will. A change rebuilds the room (the game reads the project, then the scene loads again).

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using GoF2Remake.Flight;

namespace GoF2Remake.UI
{
    public partial class ModBuilderView
    {
        VisualElement interiorList, interiorPage, interiorLabels;
        ProjectInterior currentInterior;
        /// <summary>The marker the next click in the room places (null: none).</summary>
        string placingMarker;
        bool gameCamera;
        const int RoomLayer = 21;
        readonly List<MeshCollider> roomColliders = new List<MeshCollider>();
        readonly Dictionary<string, GameObject> markerBalls = new Dictionary<string, GameObject>();
        Material markerMaterial;
        // the free camera: the point it orbits, yaw / pitch (rad), distance (m)
        Vector3 roomPivot;
        float roomYaw, roomPitch, roomDistance = 300f, roomRadius = 300f;
        bool roomDragging, roomPanning, roomTwoFingers;
        readonly TwoFingerTouch roomTouch2 = new TwoFingerTouch();
        Vector2 roomLast;
        float roomTravel;

        static readonly Color[] MarkerColours = { new Color(1f, 0.75f, 0.2f), new Color(0.3f, 0.8f, 1f), new Color(0.5f, 1f, 0.5f), new Color(1f, 0.4f, 0.4f) };

        void BuildInteriorsPanels(VisualElement left, VisualElement right)
        {
            interiorList = ListPanel(left, T("mbInteriors", "Hangars & bars"));
            Act(left, T("mbNewHangarGlb", "+ New hangar from .glb"), () => NewInterior(false), "mbv-add").SetEnabled(ModImport.Available);
            Act(left, T("mbNewBarGlb", "+ New bar from .glb"), () => NewInterior(true), "mbv-add").SetEnabled(ModImport.Available);
            Act(left, T("mbNewRoomGame", "+ New room on a game room"), PickGameRoom, "mbv-add");
            Act(left, T("mbRemoveRoom", "Remove room"), AskRemoveInterior, "mbv-danger", "mbv-remove");
            interiorPage = PagePanel(right);
            interiorLabels = Add(root, new VisualElement { pickingMode = PickingMode.Ignore }, "mbv-labels");
        }

        string InteriorKey(ProjectInterior r) => r != null ? Project.Key(r.Id) : null;

        void RebuildInteriors()
        {
            interiorList.Clear();
            if (Project == null) return;
            if (currentInterior != null && !Project.Interiors.Contains(currentInterior)) currentInterior = null;
            currentInterior ??= Project.Interiors.FirstOrDefault(r => InteriorKey(r) == ModBuilder.PreviewInterior) ?? Project.Interiors.FirstOrDefault();
            foreach (var room in Project.Interiors)
            {
                var r = room;
                string sub = (r.IsBar ? T("mbBarRoom", "Space Lounge bar") : T("mbHangarRoom", "Hangar")) + "  ·  "
                           + (GameModels.Is(r.Model, out string gm) ? gm : System.IO.Path.GetFileName(r.Model ?? ""));
                ListRow(interiorList, r.Id, sub, r == currentInterior, () => PickInterior(r), null, null);
            }
            if (Project.Interiors.Count == 0)
                Note(interiorList, T("mbNoRooms", "No rooms yet. A station can have its own hangar and Space Lounge."));
        }

        /// <summary>Another room picked: the scene shows it (rebuilt when it isn't the one shown).</summary>
        void PickInterior(ProjectInterior r)
        {
            currentInterior = r;
            RebuildInteriors();
            BuildInteriorPage();
            ShowInteriorInScene();
        }

        /// <summary>The scene shows the current room (its type's view); a room not shown yet loads the scene again.</summary>
        void ShowInteriorInScene()
        {
            var r = currentInterior;
            if (r == null) return;
            string key = InteriorKey(r);
            var shown = r.IsBar ? level.CustomBar : level.CustomHangar;
            bool previewing = ModBuilder.PreviewInterior == key && ModBuilder.InteriorIsBar == r.IsBar;
            if (previewing && (shown != null && shown.def.key == key || ModInteriors.Built(key) == null))
            {
                level.SetView(r.IsBar ? StationView.Lounge : StationView.Hangar);
                if (shown != null && shown.def.key == key) BeginInteriorView();
                else { Status(T("mbRoomFailed", "The room couldn't be built: see the message above (a missing marker?)."), true); ShowProblems(); }   // no reload loop
                return;
            }
            ModBuilder.PreviewInterior = key;
            ModBuilder.InteriorIsBar = r.IsBar;
            ModBuilder.RoomViewSet = false;
            RebuildRoom();
        }

        /// <summary>The game reads the project, builds the room again, and the scene loads with it.</summary>
        void RebuildRoom()
        {
            SaveRoomView();
            RequestReload(false);
            afterReload.Add(() =>
            {
                if (kind != ModBuilder.Kind.Interiors) return;
                if (currentInterior != null && ModInteriors.Built(InteriorKey(currentInterior)) == null)
                {
                    Status(T("mbRoomFailed", "The room couldn't be built: see the message above (a missing marker?)."), true);
                    ShowProblems();
                }
                ModBuilder.ReloadStation();
            });
            Status(T("mbRoomRebuilding", "Building the room…"), false);
        }

        void NewInterior(bool bar) => PickGlb(bar ? T("mbPickBar", "Choose a bar model") : T("mbPickHangar", "Choose a hangar model"), path =>
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(pickedName ?? (bar ? "bar" : "hangar"));
            string id = Project.InteriorIdFor(name);
            var r = Project.AddInterior(id, bar, Project.AddModel(path, id));
            currentInterior = r;
            MarkDirty();
            RebuildInteriors();
            BuildInteriorPage();
            ShowInteriorInScene();
            Status(T("mbRoomNeedsMarkers", "Place its markers: named nodes in the model count, or click them in."), false);
        });

        static readonly (string asm, string name, bool bar)[] GameRooms =
        {
            ("hangar_terran", "Terran hangar", false), ("hangar_vossk", "Vossk hangar", false), ("hangar_nivelian", "Nivelian hangar", false),
            ("hangar_midorian", "Midorian hangar", false), ("hangar_deep_science", "Deep science hangar", false), ("hangar_battlestation", "Battlestation hangar", false),
            ("bar_terran", "Terran bar", true), ("bar_vossk", "Vossk bar", true), ("bar_nivelian", "Nivelian bar", true), ("bar_midorian", "Midorian bar", true),
        };

        void PickGameRoom()
        {
            var choices = new List<(string, Texture2D, Action)>();
            foreach (var (asm, name, bar) in GameRooms)
            {
                string a = asm;
                bool b = bar;
                choices.Add((T("mbRoom_" + asm, name), null, () =>
                {
                    string id = Project.InteriorIdFor("my_" + a);
                    var r = Project.AddInterior(id, b, GameModels.Ref(a));
                    // Its markers from the game's tables, as a start (the game room has no marker nodes).
                    var marks = StationLevel.GameRoomMarkers(a);
                    if (marks != null) foreach (var kv in marks) r.SetMarker(kv.Key, kv.Value);
                    if (!b) { r.Fov = 46f; }
                    currentInterior = r;
                    MarkDirty();
                    RebuildInteriors();
                    BuildInteriorPage();
                    ShowInteriorInScene();
                }));
            }
            ShowPicker(T("mbGameRoomTitle", "Start from a game room"), choices);
        }

        void AskRemoveInterior()
        {
            var r = currentInterior;
            if (r == null) return;
            Confirm(T("mbRemoveRoom", "Remove room"), string.Format(T("mbRemoveText", "Remove \"{0}\" from the project?"), r.Id), () =>
            {
                Project.Interiors.Remove(r);
                string key = InteriorKey(r);
                foreach (var st in Project.Stations) { if (st.Hangar == key) st.Hangar = null; if (st.Bar == key) st.Bar = null; }
                if (!string.IsNullOrEmpty(r.Model) && !GameModels.Is(r.Model) && !Project.Interiors.Any(o => o.Model == r.Model))
                    try { System.IO.File.Delete(System.IO.Path.Combine(Project.Folder, r.Model)); } catch { }
                currentInterior = null;
                if (ModBuilder.PreviewInterior == key) ModBuilder.PreviewInterior = null;
                MarkDirty();
                RebuildInteriors();
                BuildInteriorPage();
                RebuildRoom();
            });
        }

        void RoomEdited(bool rebuild = true)
        {
            MarkDirty();
            BuildInteriorPage();
            if (rebuild) RebuildRoom();
        }

        /// <summary>The markers a room of its type has: the required first, then the numbered ones it has.</summary>
        List<(string name, bool required)> MarkerList(ProjectInterior r)
        {
            var l = new List<(string, bool)>();
            if (!r.IsBar)
                foreach (var n in ModInteriors.HangarMarkers) l.Add((n, n == "pad" || n == "camera"));
            else
                foreach (var n in ModInteriors.BarMarkers) l.Add((n, n == "camera" || n == "camera_target"));
            var numbered = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            string prefix = r.IsBar ? "visitor_" : "parked_";
            foreach (var n in r.MarkerNames) if (n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) numbered.Add(n);
            var built = ModInteriors.Built(InteriorKey(r));
            if (built != null) foreach (var n in built.markers.Keys) if (n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) numbered.Add(n);
            foreach (var n in numbered.OrderBy(n => int.TryParse(n.Substring(prefix.Length), out int k) ? k : 999)) l.Add((n, r.IsBar && n == "visitor_1"));
            return l;
        }

        /// <summary>A marker's place: the one placed here, else the model's own (from the built room), else none.</summary>
        Vector3? MarkerAt(ProjectInterior r, string name, out bool placed)
        {
            var own = r.Marker(name);
            placed = own != null;
            if (own != null) return own;
            var built = ModInteriors.Built(InteriorKey(r));
            return built != null && built.markers.TryGetValue(name, out var v) ? v : (Vector3?)null;
        }

        void BuildInteriorPage()
        {
            interiorPage.Clear();
            var r = currentInterior;
            if (r == null) { Note(interiorPage, T("mbPickRoom", "Add a room on the left to start.")); return; }
            JsonButton(interiorPage, r.Json, () => { SaveNow(); RequestReload(false); RebuildInteriors(); BuildInteriorPage(); },
                       T("mbJsonRoomNote", "Also in interiors.json: materials, gateSpan, cruise, near / far, markers."));
            Heading(interiorPage, r.IsBar ? T("mbBarRoom", "Space Lounge bar") : T("mbHangarRoom", "Hangar"));
            Note(interiorPage, string.Format(T("mbRoomKey", "Stations use it as \"{0}\"."), InteriorKey(r)));

            var vrow = Row(interiorPage);
            var free = Act(vrow, T("mbFreeCamera", "Free camera"), () => { gameCamera = false; BuildInteriorPage(); }, "mbv-seg");
            free.EnableInClassList("mbv-seg--on", !gameCamera);
            var game = Act(vrow, T("mbGameCamera", "Game camera"), () => { gameCamera = true; BuildInteriorPage(); }, "mbv-seg");
            game.EnableInClassList("mbv-seg--on", gameCamera);
            Act(vrow, T("mbRebuildRoom", "Rebuild"), RebuildRoom);

            // ---- markers ----
            Heading(interiorPage, T("mbMarkers", "Markers"));
            Note(interiorPage, r.IsBar ? T("mbBarMarkersNote", "Needs camera, camera_target and a visitor spot. Click Place, then the room.")
                                       : T("mbHangarMarkersNote", "Needs pad (where your ship stands) and camera. Click Place, then the room."));
            int ci = 0;
            foreach (var (name, required) in MarkerList(r))
            {
                string n = name;
                var at = MarkerAt(r, n, out bool placed);
                var row = Row(interiorPage);
                var dot = Add(row, new VisualElement { pickingMode = PickingMode.Ignore }, "mbv-dot");
                dot.style.backgroundColor = MarkerColour(n, ci++);
                string state = at == null ? (required ? T("mbMarkerMissing", "missing (required)") : T("mbMarkerNone", "none"))
                             : placed ? $"{at.Value.x:0.#}, {at.Value.y:0.#}, {at.Value.z:0.#}" : T("mbMarkerModel", "in the model");
                var label = Add(row, new Label($"{n}: {state}"), "mbv-inline-label");
                if (at == null && required) label.AddToClassList("mbv-warn");
                var place = Act(row, placingMarker == n ? T("mbClickRoom", "Click the room…") : T("mbPlace", "Place"), () => { placingMarker = placingMarker == n ? null : n; BuildInteriorPage(); }, "mbv-step");
                place.EnableInClassList("mbv-toggle--on", placingMarker == n);
                if (n.StartsWith("camera")) Act(row, T("mbFromView", "Use view"), () => SetMarkerFromView(r, n), "mbv-step");
                if (placed) Act(row, "×", () => { r.SetMarker(n, null); RoomEdited(); }, "mbv-step");
                if (placed)
                {
                    var xyz = Row(interiorPage, "mbv-xyz");
                    var v = at.Value;
                    Float(xyz, "X", v.x, -100000f, 100000f, f => { var p = r.Marker(n) ?? v; p.x = f; r.SetMarker(n, p); RoomEdited(); });
                    Float(xyz, "Y", v.y, -100000f, 100000f, f => { var p = r.Marker(n) ?? v; p.y = f; r.SetMarker(n, p); RoomEdited(); });
                    Float(xyz, "Z", v.z, -100000f, 100000f, f => { var p = r.Marker(n) ?? v; p.z = f; r.SetMarker(n, p); RoomEdited(); });
                }
            }
            string prefix = r.IsBar ? "visitor_" : "parked_";
            int next = 1;
            while (MarkerAt(r, prefix + next, out _) != null) next++;
            Act(interiorPage, r.IsBar ? T("mbAddVisitor", "+ Visitor spot") : T("mbAddParked", "+ Parked ship pad"), () => { placingMarker = prefix + next; BuildInteriorPage(); }, "mbv-add");
            Note(interiorPage, T("mbMarkersUnits", "Room metres from the model's origin (Y up)."));

            // ---- model ----
            Heading(interiorPage, T("mbModel", "Model"));
            bool gameModel = GameModels.Is(r.Model, out string gm);
            Note(interiorPage, gameModel ? string.Format(T("mbGameRoomNote", "The game's {0}, as it is."), gm) : r.Model ?? "");
            if (!gameModel)
            {
                var mrow = Row(interiorPage);
                if (ModImport.Available)
                    Act(mrow, T("mbReplaceModel", "Replace model…"), () => PickGlb(T("mbPickModel", "Choose a model"), path =>
                    {
                        string old = r.Model;
                        r.Model = Project.AddModel(path, r.Id);
                        if (old != null && old != r.Model) Project.DeleteFile(old, Project.Interiors.Any(o => o != r && o.Model == old));
                        RoomEdited();
                    }));
                Float(interiorPage, T("mbScaleTimes", "Size (x the model's metres)"), r.Scale, 0.01f, 100f, v => { r.Scale = v; RoomEdited(); });
            }

            // ---- camera, light, fog ----
            Heading(interiorPage, T("mbLens", "Camera"));
            var lrow = interiorPage;
            Float(lrow, T("mbFov", "Fov°"), r.Fov ?? (r.IsBar ? 69f : 46f), 10f, 120f, v => { r.Fov = v; RoomEdited(); });
            Float(lrow, T("mbNear", "Near m"), r.Near ?? 10f, 0.1f, 1000f, v => { r.Near = v; RoomEdited(); });
            Float(lrow, T("mbFar", "Far m"), r.Far ?? (r.IsBar ? 2400f : 5000f), 10f, 100000f, v => { r.Far = v; RoomEdited(); });
            Heading(interiorPage, T("mbLight", "Light"));
            Float(interiorPage, T("mbLightIntensity", "Main light"), r.LightIntensity ?? 1f, 0f, 10f, v => { r.LightIntensity = v; RoomEdited(); });
            ColourRow(interiorPage, T("mbAmbient", "Base light"), r.Ambient, c => { r.Ambient = c; RoomEdited(); });
            var fog = r.Fog;
            FlagButton(interiorPage, T("mbFog", "Fog"), fog != null, v => r.Fog = v ? (new Color(0.02f, 0.1f, 0.05f), 1500f) : ((Color, float)?)null, () => RoomEdited());
            if (fog != null)
            {
                ColourRow(interiorPage, T("mbFogColour", "Fog colour"), fog.Value.colour, c => { r.Fog = (c ?? Color.black, r.Fog?.end ?? 1500f); RoomEdited(); }, false);
                Float(interiorPage, T("mbFogEnd", "Thick at (m)"), fog.Value.end, 10f, 100000f, v => { r.Fog = (r.Fog?.colour ?? Color.black, v); RoomEdited(); });
            }

            if (!r.IsBar)
            {
                Heading(interiorPage, T("mbHangarShips", "Ships"));
                Float(interiorPage, T("mbStartYaw", "Your ship faces (degrees)"), r.StartYaw ?? 0f, -360f, 360f, v => { r.StartYaw = v; RoomEdited(); });
                Int(interiorPage, T("mbParkedMax", "Parked ships at most"), r.ParkedMax ?? -1, -1, 20, v => { r.ParkedMax = v < 0 ? (int?)null : v; RoomEdited(); });
                var frow = Row(interiorPage);
                FlagButton(frow, T("mbFlights", "Fly in and out"), r.Flights, v => r.Flights = v, () => RoomEdited());
                Float(interiorPage, T("mbCruise", "Flights cross at (m over the pad)"), r.Cruise ?? 40f, 0f, 1000f, v => { r.Cruise = v; RoomEdited(); });
            }

            // ---- where it is used ----
            Heading(interiorPage, T("mbUsedAt", "Used at"));
            string key = InteriorKey(r);
            if (Project.Stations.Count == 0) Note(interiorPage, T("mbNoStationsForRoom", "Make or change a station (Stations kind) to use this room there."));
            foreach (var st in Project.Stations)
            {
                var s = st;
                bool on = (r.IsBar ? s.Bar : s.Hangar) == key;
                string sname = s.Name ?? (s.IsOverride ? RefLabel(RefKind.Station, s.OverrideRef, s.OverrideRef) : s.LocalId);
                FlagButton(interiorPage, sname, on, v => { if (r.IsBar) s.Bar = v ? key : null; else s.Hangar = v ? key : null; }, () => BuildInteriorPage());
            }
        }

        /// <summary>A colour as three 0..1 fields ('optional': a × clears it to the default).</summary>
        void ColourRow(VisualElement parent, string label, Color? value, Action<Color?> set, bool optional = true)
        {
            Caption(parent, label + (value == null ? "  (" + T("mbDefault", "default") + ")" : ""));
            var row = Row(parent, "mbv-xyz");
            var c = value ?? new Color(0.3f, 0.3f, 0.3f);
            Float(row, "R", c.r, 0f, 4f, v => set(new Color(v, c.g, c.b)));
            Float(row, "G", c.g, 0f, 4f, v => set(new Color(c.r, v, c.b)));
            Float(row, "B", c.b, 0f, 4f, v => set(new Color(c.r, c.g, v)));
            if (optional && value != null) Act(row, "×", () => set(null), "mbv-step");
        }

        static Color MarkerColour(string name, int i) =>
            name == "pad" || name.StartsWith("parked") || name.StartsWith("visitor") ? MarkerColours[0]
            : name.StartsWith("camera") ? MarkerColours[1] : name.StartsWith("gate") ? MarkerColours[2] : MarkerColours[3];

        void SetMarkerFromView(ProjectInterior r, string name)
        {
            var room = RoomTransform;
            if (room == null || gameCamera) { Status(T("mbFromViewFree", "Use the free camera: its place becomes the camera's, its pivot the target."), false); return; }
            var cam = level.MainCamera.transform;
            bool target = name.EndsWith("_target");
            var world = target ? roomPivot : cam.position;
            r.SetMarker(name, room.InverseTransformPoint(world));
            RoomEdited();
        }

        // ---- the room in the scene ----------------------------------------------------------------------------

        Transform RoomTransform => currentInterior == null ? null : currentInterior.IsBar ? level.BarRoom : level.HangarRoom;

        /// <summary>The kind is shown: the room's colliders for the clicks, the free camera, the marker balls.</summary>
        void BeginInteriorView()
        {
            EndInteriorView();
            var room = RoomTransform;
            if (room == null || currentInterior == null) return;
            var shown = currentInterior.IsBar ? level.CustomBar : level.CustomHangar;
            if (shown == null || shown.def.key != InteriorKey(currentInterior)) return;   // not this room (being rebuilt)
            var bounds = new Bounds(room.position, Vector3.zero);
            bool any = false;
            foreach (var mf in room.GetComponentsInChildren<MeshFilter>())
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null) continue;
                if (!any) { bounds = mr.bounds; any = true; } else bounds.Encapsulate(mr.bounds);
                try
                {
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    roomColliders.Add(mc);
                    mf.gameObject.layer = RoomLayer;
                }
                catch (Exception) { }   // a mesh that can't be read: no clicks on it
            }
            roomRadius = Mathf.Max(20f, bounds.extents.magnitude);
            if (ModBuilder.RoomViewSet)
            {
                roomPivot = ModBuilder.RoomPivot;
                roomYaw = ModBuilder.RoomOrbit.x; roomPitch = ModBuilder.RoomOrbit.y; roomDistance = ModBuilder.RoomOrbit.z;
            }
            else
            {
                roomPivot = bounds.center;
                roomYaw = 0.6f;
                roomPitch = 0.45f;
                roomDistance = roomRadius * 1.1f;
            }
            level.FreeCamera = !gameCamera;
            ApplyRoomCamera();
            interiorLabels.style.display = DisplayStyle.Flex;
        }

        void SaveRoomView()
        {
            if (kind != ModBuilder.Kind.Interiors) return;
            ModBuilder.RoomPivot = roomPivot;
            ModBuilder.RoomOrbit = new Vector3(roomYaw, roomPitch, roomDistance);
            ModBuilder.RoomViewSet = true;
        }

        void EndInteriorView()
        {
            foreach (var c in roomColliders) if (c != null) UnityEngine.Object.Destroy(c);
            roomColliders.Clear();
            foreach (var b in markerBalls.Values) if (b != null) UnityEngine.Object.Destroy(b);
            markerBalls.Clear();
            interiorLabels?.Clear();
            if (level != null) level.FreeCamera = false;
            placingMarker = null;
        }

        void ApplyRoomCamera()
        {
            var dir = new Vector3(Mathf.Sin(roomYaw) * Mathf.Cos(roomPitch), Mathf.Sin(roomPitch), Mathf.Cos(roomYaw) * Mathf.Cos(roomPitch));
            var pos = roomPivot + dir * roomDistance;
            level.FreeCameraPose = new Pose(pos, Quaternion.LookRotation(roomPivot - pos, Vector3.up));
            level.FreeCameraFov = 55f;
        }

        void UpdateInteriorView(float dt)
        {
            var r = currentInterior;
            var room = RoomTransform;
            if (r != null && room != null && roomColliders.Count == 0 && !reloading && !reloadPending) BeginInteriorView();   // the room came after the screen
            level.FreeCamera = r != null && room != null && !gameCamera && roomColliders.Count > 0;
            // Touch: two fingers on the room pinch to zoom and move the camera (TwoFingerTouch); never a marker's click.
            roomTwoFingers = roomTouch2.Read(out float zoom, out var pan) && (roomDragging || roomPanning) && level.FreeCamera;
            if (roomTwoFingers)
            {
                roomTravel = 1000f;
                roomDistance = Mathf.Clamp(roomDistance * zoom, 1f, roomRadius * 5f);
                var view = level.MainCamera != null ? level.MainCamera.transform : null;
                float k = roomDistance * 0.0012f;
                if (view != null) roomPivot -= view.right * pan.x * k + view.up * pan.y * k;
            }
            if (level.FreeCamera) ApplyRoomCamera();
            ReadRoomKeys(dt);
            // The markers: a ball each (its size by the room's), their names over them.
            if (r == null || room == null) { interiorLabels.Clear(); return; }
            var names = MarkerList(r);
            var seen = new HashSet<string>();
            int i = 0;
            float size = Mathf.Clamp(roomRadius * 0.012f, 1.5f, 12f);
            var cam = level.MainCamera;
            while (interiorLabels.childCount < names.Count) Add(interiorLabels, new Label { pickingMode = PickingMode.Ignore }, "mbv-point-tag");
            while (interiorLabels.childCount > names.Count) interiorLabels.RemoveAt(interiorLabels.childCount - 1);
            float w = root.layout.width, h = root.layout.height;
            foreach (var (name, _) in names)
            {
                var tag = (Label)interiorLabels[i];
                var at = MarkerAt(r, name, out _);
                var colour = MarkerColour(name, i);
                i++;
                if (at == null) { tag.style.display = DisplayStyle.None; continue; }
                seen.Add(name);
                if (!markerBalls.TryGetValue(name, out var ball) || ball == null)
                {
                    ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    UnityEngine.Object.Destroy(ball.GetComponent<Collider>());
                    ball.name = "Marker " + name;
                    var mr = ball.GetComponent<MeshRenderer>();
                    markerMaterial ??= Vr.VrPanels.FromTemplate("VrGlass", "Universal Render Pipeline/Unlit");   // Unlit is stripped from builds without a template (#115)
                    var mat = new Material(markerMaterial);
                    mat.SetColor("_BaseColor", colour);
                    mr.sharedMaterial = mat;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    markerBalls[name] = ball;
                }
                var world = room.TransformPoint(at.Value);
                ball.transform.position = world;
                ball.transform.localScale = Vector3.one * size;
                ball.SetActive(!gameCamera);
                if (cam == null || float.IsNaN(w) || w <= 0f || gameCamera) { tag.style.display = DisplayStyle.None; continue; }
                var sp = cam.WorldToScreenPoint(world);
                if (sp.z <= 0f) { tag.style.display = DisplayStyle.None; continue; }
                tag.style.display = DisplayStyle.Flex;
                tag.text = name;
                tag.style.color = colour;
                tag.style.translate = new Translate(sp.x / Screen.width * w + 10f, (Screen.height - sp.y) / Screen.height * h - 24f);
                tag.EnableInClassList("mbv-point-tag--on", name == placingMarker);
            }
            foreach (var k in markerBalls.Keys.Where(k => !seen.Contains(k)).ToList()) { UnityEngine.Object.Destroy(markerBalls[k]); markerBalls.Remove(k); }
        }

        void ReadRoomKeys(float dt)
        {
            var kb = Keyboard.current;
            if (kb == null || ModalOpen || gameCamera) return;
            var focused = root.panel?.focusController?.focusedElement as VisualElement;
            if (focused != null && TextFieldKeys.InTextField(focused)) return;
            float frames = dt / OriginalFrame.Ms;
            float l = kb.leftArrowKey.isPressed || kb.aKey.isPressed ? 1f : 0f, r = kb.rightArrowKey.isPressed || kb.dKey.isPressed ? 1f : 0f;
            float u = kb.upArrowKey.isPressed || kb.wKey.isPressed ? 1f : 0f, d = kb.downArrowKey.isPressed || kb.sKey.isPressed ? 1f : 0f;
            roomYaw += (r - l) * 0.03f * frames;
            roomPitch = Mathf.Clamp(roomPitch + (u - d) * 0.03f * frames, -1.45f, 1.45f);
            float zoom = (kb.minusKey.isPressed || kb.numpadMinusKey.isPressed ? 1f : 0f) - (kb.equalsKey.isPressed || kb.numpadPlusKey.isPressed ? 1f : 0f);
            if (zoom != 0f) roomDistance = Mathf.Clamp(roomDistance * Mathf.Pow(1.03f, zoom * frames), 1f, roomRadius * 5f);
        }

        string InteriorHint() => placingMarker != null
            ? string.Format(T("mbPlaceMarkerHint", "Click in the room to place \"{0}\".  Esc cancels."), placingMarker)
            : gameCamera || currentInterior == null ? "" : T("mbRoomHint", "Drag to turn, middle button to move, wheel to zoom.");

        bool EscapeInterior()
        {
            if (placingMarker == null) return false;
            placingMarker = null;
            BuildInteriorPage();
            return true;
        }

        bool InteriorPointerDown(PointerDownEvent e)
        {
            if (gameCamera || roomColliders.Count == 0) return true;
            roomLast = ScreenPos();
            roomTravel = 0f;
            roomDragging = e.button == 0 || e.button == 1;
            roomPanning = e.button == 2;
            viewport.CapturePointer(e.pointerId);
            return true;
        }

        bool InteriorPointerMove(PointerMoveEvent e)
        {
            if (!viewport.HasPointerCapture(e.pointerId)) return true;
            if (!UiPointerButtons.AnyHeld(e)) { viewport.ReleasePointer(e.pointerId); roomDragging = roomPanning = false; return true; }
            var p = ScreenPos();
            var delta = (p - roomLast) * (1080f / Mathf.Max(1, Screen.height));
            roomLast = p;
            roomTravel += delta.magnitude;
            if (roomPanning)
            {
                var cam = level.MainCamera.transform;
                float k = roomDistance * 0.0012f;
                roomPivot -= cam.right * delta.x * k + cam.up * delta.y * k;
            }
            else if (roomDragging && !roomTwoFingers)
            {
                var turn = Settings.OrbitTurn(delta);   // #121: the camera views' own inversion
                roomYaw += turn.x * 0.005f;
                roomPitch = Mathf.Clamp(roomPitch - turn.y * 0.005f, -1.45f, 1.45f);
            }
            return true;
        }

        bool InteriorPointerUp(PointerUpEvent e)
        {
            if (!viewport.HasPointerCapture(e.pointerId)) return true;
            viewport.ReleasePointer(e.pointerId);
            bool click = roomTravel < 6f && e.button == 0;
            roomDragging = roomPanning = false;
            if (click && placingMarker != null) PlaceMarkerAt(ScreenPos());
            return true;
        }

        void InteriorWheel(float delta)
        {
            if (gameCamera) return;
            roomDistance = Mathf.Clamp(roomDistance * Mathf.Pow(0.85f, -Mathf.Sign(delta)), 1f, roomRadius * 5f);
        }

        /// <summary>The marker being placed goes where the click hits the room (a camera a little off the surface).</summary>
        void PlaceMarkerAt(Vector2 screen)
        {
            var r = currentInterior;
            var room = RoomTransform;
            var cam = level.MainCamera;
            if (r == null || room == null || cam == null) return;
            var ray = cam.ScreenPointToRay(screen);
            if (!Physics.Raycast(ray, out var hit, 100000f, 1 << RoomLayer, QueryTriggerInteraction.Ignore))
            {
                Status(T("mbMissedRoom", "Click on the room's surfaces."), false);
                return;
            }
            var world = hit.point;
            if (placingMarker.StartsWith("camera") && !placingMarker.EndsWith("_target") || placingMarker == "light" || placingMarker == "gate_out")
                world += hit.normal * Mathf.Clamp(roomRadius * 0.02f, 2f, 20f);
            r.SetMarker(placingMarker, room.InverseTransformPoint(world));
            placingMarker = null;
            RoomEdited();
        }
    }
}
