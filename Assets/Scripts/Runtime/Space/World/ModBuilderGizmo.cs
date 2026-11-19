// ModBuilderGizmo.cs
// Remake-only: the Mod Builder's 3D hardpoint editor on the hangar's turntable ship (Modding.ModBuilder, UI.ModBuilderView).
// Each hardpoint (ships.json "mounts", WeaponMount) shows as a coloured ball drawn over the hull (primary red, secondary
// amber, turret cyan, engine exhaust violet with a short stub pointing back), the selected one larger with Unity-style move
// handles: three arrows along the ship's own axes (X red = game +x (the pilot's left), Y green = up, Z blue = forward), each dragged along its
// line (the closest point between the mouse ray and the axis), or the ball itself dragged over the hull (a ray against
// MeshColliders built on the ship's meshes, layer PickLayer). Under the ship a flat arrow on the floor points at its nose
// (game +z). Sizes follow the camera distance, so the handles keep their size on screen.
// Mount positions are game units in the ship's frame; the ship's Unity space has x mirrored (WeaponSystem.MountToLocal).
// Input comes from the view (screen pixels, origin bottom-left, like Camera.ScreenPointToRay). With touch input the balls
// and arrows are drawn larger and picked within a fingertip (Reach: the screen's dpi), the arrows from further out (a tap
// on the ball moves the ball).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    [DefaultExecutionOrder(1000)]   // after the hangar camera (StationLevel.LateUpdate)
    public sealed class ModBuilderGizmo : MonoBehaviour
    {
        public const int PickLayer = 22;
        const float M = 0.05f;
        const float MarkerScreen = 0.016f;   // ball diameter / camera distance
        const float ArrowScreen = 0.11f;     // handle length / camera distance
        const float PickPixels = 16f;        // at 1080 p

        public static readonly Color[] TypeColours =
        {
            new Color(1f, 0.32f, 0.25f), new Color(1f, 0.7f, 0.2f), new Color(0.25f, 0.85f, 1f), new Color(0.72f, 0.45f, 1f), new Color(0.3f, 0.95f, 0.65f),
        };
        static readonly Color[] AxisColours = { new Color(0.95f, 0.25f, 0.25f), new Color(0.35f, 0.9f, 0.3f), new Color(0.3f, 0.55f, 1f) };
        static readonly Color Hot = new Color(1f, 0.9f, 0.3f);

        public Camera cam;
        public Transform Ship { get; private set; }
        public List<WeaponMount> Mounts { get; private set; }
        public int Selected { get; set; } = -1;
        /// <summary>A hardpoint is being moved (live, every drag step).</summary>
        public event Action<int> Moved;

        enum Grab { None, AxisX, AxisY, AxisZ, Surface }
        Grab grab;
        int hoverAxis = -1;
        Vector3 grabStart;   // world position of the point when the drag began
        float grabT;         // the axis parameter where it began

        Shader shader;
        readonly List<(GameObject go, Material mat, GameObject stub)> markers = new List<(GameObject, Material, GameObject)>();
        readonly GameObject[] arrows = new GameObject[3];
        readonly Material[] arrowMats = new Material[3];
        GameObject floorArrow;
        readonly List<GameObject> picks = new List<GameObject>();
        Material floorMat;
        Mesh arrowMesh, floorMesh;
        Bounds localBounds;

        public bool Dragging => grab != Grab.None;

        void Awake()
        {
            shader = Resources.Load<Shader>("GoF2ModBuilder/BuilderGizmo") ?? Shader.Find("Hidden/GoF2/BuilderGizmo");
            arrowMesh = BuildArrowMesh();
            for (int i = 0; i < 3; i++)
            {
                arrowMats[i] = NewMaterial(AxisColours[i], true);
                arrows[i] = NewObject("Move " + "XYZ"[i], arrowMesh, arrowMats[i]);
            }
            floorMesh = BuildFloorArrowMesh();
            floorMat = NewMaterial(new Color(0.25f, 0.8f, 1f, 0.3f), false);
            floorArrow = NewObject("Forward arrow", floorMesh, floorMat);
        }

        void OnDestroy()
        {
            foreach (var m in markers) Destroy(m.mat);
            foreach (var m in arrowMats) if (m != null) Destroy(m);
            if (floorMat != null) Destroy(floorMat);
            if (arrowMesh != null) Destroy(arrowMesh);
            if (floorMesh != null) Destroy(floorMesh);
            foreach (var p in picks) if (p != null) Destroy(p);
        }

        Material NewMaterial(Color c, bool overHull)
        {
            var m = new Material(shader) { renderQueue = overHull ? 4000 : 3100 };
            m.SetColor("_Color", c);
            m.SetFloat("_ZTest", overHull ? (float)UnityEngine.Rendering.CompareFunction.Always : (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            return m;
        }

        GameObject NewObject(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.SetActive(false);
            return go;
        }

        // ---- binding ----------------------------------------------------------------------------------------------

        /// <summary>The turntable ship and its hardpoint list (the list is edited in place).</summary>
        public void Bind(Transform ship, List<WeaponMount> mounts)
        {
            Ship = ship;
            Mounts = mounts;
            grab = Grab.None;
            if (Selected >= (mounts?.Count ?? 0)) Selected = -1;
            BuildColliders();
            Refresh();
        }

        /// <summary>MeshColliders on the hull's meshes (not the glow layers) for the surface picks.</summary>
        void BuildColliders()
        {
            foreach (var p in picks) if (p != null) Destroy(p);
            picks.Clear();
            localBounds = new Bounds(Vector3.zero, Vector3.one * 10f);
            if (Ship == null) return;
            bool any = false;
            foreach (var mf in Ship.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                string n = mf.name.ToLowerInvariant();
                if (mf.sharedMesh == null || mr == null || n.Contains("_add") || n.Contains("glow") || n.Contains("engine_state")) continue;
                var mb = mf.sharedMesh.bounds;
                var toShip = Ship.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var p = toShip.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { localBounds = new Bounds(p, Vector3.zero); any = true; } else localBounds.Encapsulate(p);
                }
                if (!mf.sharedMesh.isReadable) continue;
                var go = new GameObject(mf.name + " pick") { layer = PickLayer };
                go.transform.SetParent(mf.transform, false);
                picks.Add(go);
                try { go.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh; }
                catch (Exception e) { Debug.LogWarning($"Mod Builder: no pick collider for {mf.name}: {e.Message}"); Destroy(go); }
            }
        }

        /// <summary>The hull's box in the ship's own Unity frame (the meshes, no glow layers).</summary>
        public Bounds LocalBounds => localBounds;

        /// <summary>The markers again for the list as it is (added / removed / retyped points).</summary>
        public void Refresh()
        {
            int n = Mounts?.Count ?? 0;
            while (markers.Count < n)
            {
                var mat = NewMaterial(Color.white, true);
                var go = NewObject("Hardpoint", Resources.GetBuiltinResource<Mesh>("Sphere.fbx"), mat);
                var stub = NewObject("Exhaust stub", arrowMesh, mat);
                stub.transform.SetParent(go.transform, false);
                markers.Add((go, mat, stub));
            }
            for (int i = 0; i < markers.Count; i++)
            {
                bool on = i < n && Ship != null;
                markers[i].go.SetActive(on);
                if (!on) continue;
                int type = Mounts[i].slotType == 8 ? 4 : Mathf.Clamp(Mounts[i].slotType, 0, 3);
                markers[i].mat.SetColor("_Color", i == Selected ? Color.Lerp(TypeColours[type], Color.white, 0.35f) : TypeColours[type]);
                markers[i].stub.SetActive(type == 3);
            }
        }

        // ---- coordinates ------------------------------------------------------------------------------------------

        public Vector3 WorldOf(WeaponMount m) => Ship.TransformPoint(WeaponSystem.MountToLocal(m));

        /// <summary>A world point as game units in the ship's frame.</summary>
        public Vector3Int GameOf(Vector3 world)
        {
            var l = Ship.InverseTransformPoint(world) / M;
            return new Vector3Int(Mathf.RoundToInt(-l.x), Mathf.RoundToInt(l.y), Mathf.RoundToInt(l.z));
        }

        /// <summary>A world direction as a unit vector in the ship's game frame (a turret mount's turretUp).</summary>
        public Vector3 GameDirOf(Vector3 worldDir)
        {
            var l = Ship.InverseTransformDirection(worldDir).normalized;
            return new Vector3(-l.x, l.y, l.z);
        }

        /// <summary>The ship's axes in world space, in game terms: +x (Unity's -x: the models are mirrored), up, forward.</summary>
        Vector3 Axis(int i) => i == 0 ? Ship.TransformDirection(Vector3.left) : i == 1 ? Ship.TransformDirection(Vector3.up) : Ship.TransformDirection(Vector3.forward);

        float Scale(Vector3 world) => cam != null ? Vector3.Distance(cam.transform.position, world) : 10f;

        static float PixelScale => Mathf.Max(1f, Screen.height) / 1080f;

        static bool Touch => GoF2Remake.UI.InputMode.Current == GoF2Remake.UI.InputKind.Touch;
        /// <summary>The balls and arrows larger with touch input.</summary>
        static float TouchSize => Touch ? 1.25f : 1f;

        /// <summary>How far from a handle a pointer still picks it (screen pixels): 'pixels' at 1080 p, and with touch input at
        /// least a fingertip, 'touchInches' at the screen's dpi (48 px at 1080 p when the dpi is unknown): 16 px on a phone's
        /// 1080 p screen is about a millimetre.</summary>
        static float Reach(float pixels, float touchInches)
        {
            float r = pixels * PixelScale;
            if (!Touch) return r;
            return Mathf.Max(r, Screen.dpi > 0f ? Screen.dpi * touchInches : 48f * PixelScale);
        }

        // ---- per frame --------------------------------------------------------------------------------------------

        void LateUpdate()
        {
            bool show = Ship != null && Ship.gameObject.activeInHierarchy && cam != null && Mounts != null;
            if (!show)
            {
                foreach (var m in markers) m.go.SetActive(false);
                foreach (var a in arrows) a.SetActive(false);
                floorArrow.SetActive(false);
                return;
            }
            for (int i = 0; i < Mounts.Count && i < markers.Count; i++)
            {
                var p = WorldOf(Mounts[i]);
                float s = Scale(p) * MarkerScreen * TouchSize * (i == Selected ? 1.5f : 1f);
                var t = markers[i].go.transform;
                t.SetPositionAndRotation(p, Ship.rotation);
                t.localScale = Vector3.one * s;
                if (markers[i].stub.activeSelf)
                {
                    // An exhaust points back: a stub along -z, three balls long.
                    markers[i].stub.transform.localRotation = Quaternion.LookRotation(Vector3.back);
                    markers[i].stub.transform.localScale = new Vector3(5f, 5f, 3f);
                }
            }
            bool sel = Selected >= 0 && Selected < Mounts.Count;
            for (int a = 0; a < 3; a++)
            {
                arrows[a].SetActive(sel);
                if (!sel) continue;
                var p = WorldOf(Mounts[Selected]);
                float len = Scale(p) * ArrowScreen * TouchSize;
                arrows[a].transform.SetPositionAndRotation(p, Quaternion.LookRotation(Axis(a), a == 1 ? Ship.forward : Ship.up));
                arrows[a].transform.localScale = new Vector3(len, len, len);
                arrowMats[a].SetColor("_Color", grab == (Grab)(a + 1) || grab == Grab.None && hoverAxis == a ? Hot : AxisColours[a]);
            }
            // The floor arrow: under the hull's bottom, along the nose, as long as the ship.
            floorArrow.SetActive(true);
            var fwd = Ship.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            var centre = Ship.TransformPoint(localBounds.center);
            float bottom = Ship.TransformPoint(new Vector3(0f, localBounds.min.y, 0f)).y + 0.15f;
            float shipLength = Mathf.Max(localBounds.size.z * Mathf.Abs(Ship.lossyScale.z), 5f);
            floorArrow.transform.SetPositionAndRotation(new Vector3(centre.x, bottom, centre.z), Quaternion.LookRotation(fwd.normalized, Vector3.up));
            floorArrow.transform.localScale = Vector3.one * shipLength * 0.8f;
        }

        // ---- picking ----------------------------------------------------------------------------------------------

        /// <summary>The hardpoint under the pointer (screen pixels), -1 = none; the nearest on screen within reach.</summary>
        public int PickMarker(Vector2 screen)
        {
            if (Ship == null || cam == null || Mounts == null) return -1;
            int best = -1;
            float bestD = Reach(PickPixels, 0.17f);
            for (int i = 0; i < Mounts.Count; i++)
            {
                var sp = cam.WorldToScreenPoint(WorldOf(Mounts[i]));
                if (sp.z <= 0f) continue;
                float d = Vector2.Distance(screen, sp);
                if (d < bestD || i == Selected && d <= bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>The selected point's move handle under the pointer: 0 x, 1 y, 2 z, -1 none.</summary>
        public int PickAxis(Vector2 screen)
        {
            if (Ship == null || cam == null || Selected < 0 || Selected >= (Mounts?.Count ?? 0)) return -1;
            var p = WorldOf(Mounts[Selected]);
            float len = Scale(p) * ArrowScreen * TouchSize;
            var a = (Vector2)cam.WorldToScreenPoint(p);
            int best = -1;
            float bestD = Reach(12f, 0.12f);
            for (int i = 0; i < 3; i++)
            {
                var bp = cam.WorldToScreenPoint(p + Axis(i) * len);
                if (bp.z <= 0f) continue;
                var b = (Vector2)bp;
                // Only the arrow's outer 80 % (the ball itself drags over the surface).
                var from = Vector2.Lerp(a, b, Touch ? 0.45f : 0.2f);
                float d = DistanceToSegment(screen, from, b);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude < 1e-4f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>The hull under the pointer (world point and normal).</summary>
        public bool RaycastHull(Vector2 screen, out Vector3 point, out Vector3 normal)
        {
            point = normal = default;
            if (cam == null) return false;
            if (!Physics.Raycast(cam.ScreenPointToRay(screen), out var hit, 5000f, 1 << PickLayer, QueryTriggerInteraction.Ignore)) return false;
            point = hit.point;
            normal = hit.normal;
            return true;
        }

        /// <summary>The hull's surface normal (world) at hardpoint 'index': short rays from just outside it (out from the hull's
        /// centre, then along the ship's axes), the hit nearest the point. False: no hull within 3 m of it.</summary>
        public bool HullNormalAt(int index, out Vector3 normal)
        {
            normal = default;
            if (Ship == null || Mounts == null || index < 0 || index >= Mounts.Count) return false;
            var point = WorldOf(Mounts[index]);
            var centre = Ship.TransformPoint(LocalBounds.center);
            var dirs = new List<Vector3> { (point - centre).normalized };
            foreach (var a in new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back }) dirs.Add(Ship.TransformDirection(a));
            float best = 3f;
            foreach (var d in dirs)
            {
                if (d.sqrMagnitude < 1e-6f) continue;
                if (!Physics.Raycast(point + d * 3f, -d, out var hit, 6f, 1 << PickLayer, QueryTriggerInteraction.Ignore)) continue;
                float off = Vector3.Distance(hit.point, point);
                if (off < best) { best = off; normal = hit.normal; }
            }
            return best < 3f;
        }

        /// <summary>The hover highlight on the handles (the view calls it while nothing is dragged).</summary>
        public void Hover(Vector2? screen) => hoverAxis = screen.HasValue ? PickAxis(screen.Value) : -1;

        // ---- dragging ---------------------------------------------------------------------------------------------

        /// <summary>A press: a handle of the selected point starts an axis move, a hardpoint is selected and starts a move over
        /// the hull. False: nothing of the gizmo there (the view orbits the camera instead).</summary>
        public bool BeginDrag(Vector2 screen)
        {
            int axis = PickAxis(screen);
            if (axis >= 0)
            {
                grab = (Grab)(axis + 1);
                grabStart = WorldOf(Mounts[Selected]);
                grabT = AxisParameter(screen, grabStart, Axis(axis));
                return true;
            }
            int marker = PickMarker(screen);
            if (marker < 0) return false;
            Selected = marker;
            grab = Grab.Surface;
            grabStart = WorldOf(Mounts[marker]);
            Refresh();
            return true;
        }

        /// <summary>The pointer moved while dragging; 'snap' rounds the point to 'snapUnits' game units.</summary>
        public void Drag(Vector2 screen, bool snap, int snapUnits = 10)
        {
            if (grab == Grab.None || Selected < 0 || Selected >= Mounts.Count) return;
            Vector3 world;
            if (grab == Grab.Surface)
            {
                if (!RaycastHull(screen, out world, out var normal)) return;   // off the hull: it stays
                // A turret slid over the hull faces out of it (the nearest ship axis within 15 deg).
                if (Mounts[Selected].slotType == 2) Mounts[Selected].SetTurretUp(GameDirOf(normal), true);
            }
            else
            {
                var axis = Axis((int)grab - 1);
                world = grabStart + axis * (AxisParameter(screen, grabStart, axis) - grabT);
            }
            var g = GameOf(world);
            if (snap) g = new Vector3Int(Snap(g.x, snapUnits), Snap(g.y, snapUnits), Snap(g.z, snapUnits));
            var m = Mounts[Selected];
            if (m.position_engine == null || m.position_engine.Length < 3) m.position_engine = new int[3];
            // An axis move keeps the other two coordinates exactly.
            if (grab == Grab.AxisX || grab == Grab.Surface) m.position_engine[0] = g.x;
            if (grab == Grab.AxisY || grab == Grab.Surface) m.position_engine[1] = g.y;
            if (grab == Grab.AxisZ || grab == Grab.Surface) m.position_engine[2] = g.z;
            Moved?.Invoke(Selected);
        }

        static int Snap(int v, int step) => Mathf.RoundToInt(v / (float)step) * step;

        public void EndDrag() => grab = Grab.None;

        /// <summary>Where the pointer's ray passes closest to the line origin + t * dir: t (world units).</summary>
        float AxisParameter(Vector2 screen, Vector3 origin, Vector3 dir)
        {
            var ray = cam.ScreenPointToRay(screen);
            var w0 = origin - ray.origin;
            float b = Vector3.Dot(dir, ray.direction), d = Vector3.Dot(dir, w0), e = Vector3.Dot(ray.direction, w0);
            float denom = 1f - b * b;   // both unit length
            return Mathf.Abs(denom) < 1e-5f ? 0f : (b * e - d) / denom;
        }

        // ---- meshes -----------------------------------------------------------------------------------------------

        /// <summary>A unit arrow along +z: a thin shaft and a cone tip.</summary>
        static Mesh BuildArrowMesh()
        {
            const int Sides = 12;
            var v = new List<Vector3>();
            var nrm = new List<Vector3>();
            var t = new List<int>();
            void Ring(float z, float r, Vector3 normalTilt)
            {
                for (int i = 0; i < Sides; i++)
                {
                    float a = i * Mathf.PI * 2f / Sides;
                    var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    v.Add(d * r + Vector3.forward * z);
                    nrm.Add((d + normalTilt).normalized);
                }
            }
            void Strip(int a, int b)
            {
                for (int i = 0; i < Sides; i++)
                {
                    int j = (i + 1) % Sides;
                    t.AddRange(new[] { a + i, b + i, b + j, a + i, b + j, a + j });
                }
            }
            const float Shaft = 0.025f, Head = 0.075f, HeadAt = 0.75f;
            Ring(0f, Shaft, Vector3.zero);        // 0
            Ring(HeadAt, Shaft, Vector3.zero);    // 12
            Strip(0, Sides);
            Ring(HeadAt, Head, Vector3.back);     // 24: the cone's base
            int tip = v.Count;
            v.Add(Vector3.forward); nrm.Add(Vector3.forward);
            for (int i = 0; i < Sides; i++) t.AddRange(new[] { 2 * Sides + i, tip, 2 * Sides + (i + 1) % Sides });
            int capCentre = v.Count;
            v.Add(Vector3.forward * HeadAt); nrm.Add(Vector3.back);
            for (int i = 0; i < Sides; i++) t.AddRange(new[] { 2 * Sides + (i + 1) % Sides, capCentre, 2 * Sides + i });
            // The shaft and cone are seen from outside: wind them so their faces point out (Cull Back).
            var mesh = new Mesh { name = "Builder arrow" };
            mesh.SetVertices(v);
            mesh.SetNormals(nrm);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            FixWinding(mesh);
            return mesh;
        }

        /// <summary>Turns every triangle whose face points against its vertex normals (one rule for both meshes).</summary>
        static void FixWinding(Mesh mesh)
        {
            var v = mesh.vertices;
            var n = mesh.normals;
            var t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                var face = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                if (Vector3.Dot(face, n[t[i]] + n[t[i + 1]] + n[t[i + 2]]) < 0f) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
            }
            mesh.triangles = t;
        }

        /// <summary>A flat arrow on the floor plane (y up), one unit long along +z, centred: a bar and a wide head.</summary>
        static Mesh BuildFloorArrowMesh()
        {
            const float Bar = 0.035f, Head = 0.11f, HeadFrom = 0.25f, Back = -0.45f, Tip = 0.55f;
            var v = new List<Vector3>
            {
                new Vector3(-Bar, 0f, Back), new Vector3(Bar, 0f, Back), new Vector3(Bar, 0f, HeadFrom), new Vector3(-Bar, 0f, HeadFrom),
                new Vector3(-Head, 0f, HeadFrom), new Vector3(Head, 0f, HeadFrom), new Vector3(0f, 0f, Tip),
            };
            var t = new List<int> { 0, 3, 2, 0, 2, 1, 4, 6, 5 };
            var mesh = new Mesh { name = "Builder floor arrow" };
            mesh.SetVertices(v);
            var up = new List<Vector3>();
            for (int i = 0; i < v.Count; i++) up.Add(Vector3.up);
            mesh.SetNormals(up);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            FixWinding(mesh);
            return mesh;
        }
    }
}
