// BarFlybys.cs
// Remake-only: now and then a ship (or a small formation) flies past outside the Space Lounge's windows. The original's
// bar shows only the system's sky behind the room (Level::createSpace for the station levels); nothing moves there.
//   measuring  once, at the first lounge visit, from the bar camera's rest pose (B), in a small view: the room twice with
//              the sky and the backdrop hidden, against two background colours (the pixels that change are see-through:
//              open arches, glass, tinted glass too), and the room's nearest surface per pixel (Hidden/GoF2/BarDistance;
//              the visitors left out); a pixel's limit is the farthest of those within the camera's sway (SwayPixels, the
//              rest pose's yaw swing of 5 / 35 rad), the window pixels' own (what lies outside) left out: wherever the
//              camera turns, a point beyond it is behind the surface in front of it, or out in a window (the nearest
//              surface alone let a ship behind a pillar show inside the Nivelian bar once the camera swayed)
//   flybys     every 6-20 s (the first 3-8 s after entering): a ship picked like the hangar's parked ones (mostly the
//              system's race), 30 % two or three in a loose formation, through a see-through pixel, 20-120 m beyond the
//              wall around that window (10-40 m in the Vossk bar's linear fog, at most 200 m out): across the view
//              (sideways, a little up or down, toward or away), or (40 %, not in the fog) in from 400 m further along the
//              line of sight, growing in the same window, then banking off sideways; at 80-180 m/s with its engine glow
//              on. A path is only flown when no point of it (the formation included) would be drawn in front of the room
//              from the rest pose (or just past the screen edges, for the camera's sway): the walls hide it, the windows
//              show it, it is never inside the room. Silent.
//   fog        the Vossk bar's linear fog ends at 250 m and its arches are 150-290 m out: there the flybys are on their own
//              layer (FlybyLayer), drawn by an overlay camera stacked on the bar camera (its depth kept, so the walls still
//              hide them) with the fog off while it renders; through the main camera they were a fog-green smear
// Created by StationLevel with the bar; runs only while the lounge is shown.

using System;
using System.Collections.Generic;
using GoF2Remake.Visuals;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Random = UnityEngine.Random;

namespace GoF2Remake.World
{
    public class BarFlybys : MonoBehaviour
    {
        const int MaskWidth = 160, MaskHeight = 90;
        const float MinBeyond = 20f, MaxBeyond = 120f, FoggedMaxDistance = 245f, ApproachFrom = 400f;   // m
        const float MinSpeed = 80f, MaxSpeed = 180f, FormationSpacing = 30f, WallMargin = 15f;            // m/s, m
        const int PathSamples = 48, Tries = 8, SwayPixels = 14;
        const int FlybyLayer = 27;   // the fogged bar's flybys (VrRig uses 28 / 29, the overlays 30 / 31)   // the sway's 8.2 deg at the bar's 101 deg wide view

        StationLevel level;
        Transform root;
        Func<int> pickShip;
        Func<int, GameObject> spawn;
        Vector3 cameraPos;
        Quaternion cameraRot;
        bool fogged, measured;
        Camera probe;                                     // the rest pose, for the rays and the projections
        Camera clear;                                     // the fogged bar: the flybys without the fog
        bool fogWas;
        bool[] open;                                      // see-through per pixel
        float[] limit;                                    // how far out a point there is surely outside the room (0 = unknown)
        readonly List<int> windows = new List<int>();     // the see-through pixels
        float nextMs = -1f;

        class Flyby
        {
            public GameObject go;
            public Vector3 a, b, c;   // a quadratic Bezier (a straight line: b halfway)
            public float t, duration, roll;
        }
        readonly List<Flyby> flying = new List<Flyby>();

        public void Setup(StationLevel stationLevel, Transform barRoot, Vector3 restPosition, Quaternion restRotation, bool fogged,
                          Func<int> ship, Func<int, GameObject> spawnShip)
        {
            level = stationLevel;
            root = barRoot;
            cameraPos = restPosition;
            cameraRot = restRotation;
            this.fogged = fogged;
            pickShip = ship;
            spawn = spawnShip;
        }

        void Update()
        {
            if (level == null || level.View != StationView.Lounge)
            {
                if (flying.Count > 0) Clear();
                nextMs = -1f;
                return;
            }
            if (!measured) { Measure(); if (fogged) SetupClearCamera(); }
            if (nextMs < 0f || level.IntroPlaying) nextMs = Mathf.Max(nextMs, Random.Range(3000f, 8000f));   // not from the intro's spot
            if (windows.Count > 0 && (nextMs -= Time.deltaTime * 1000f) <= 0f)
            {
                nextMs = Random.Range(6000f, 20000f);
                Launch();
            }
            for (int i = flying.Count - 1; i >= 0; i--)
            {
                var f = flying[i];
                f.t += Time.deltaTime / f.duration;
                if (f.go == null || f.t >= 1f) { if (f.go != null) Destroy(f.go); flying.RemoveAt(i); continue; }
                f.go.transform.SetPositionAndRotation(Bezier(f, f.t), Heading(f, f.t) * Quaternion.Euler(0f, 0f, f.roll));
            }
        }

        static Vector3 Bezier(Flyby f, float u) => Bezier(f.a, f.b, f.c, u);

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float u)
        {
            float w = 1f - u;
            return w * w * a + 2f * w * u * b + u * u * c;
        }

        static Quaternion Heading(Flyby f, float u)
        {
            var tangent = 2f * (1f - u) * (f.b - f.a) + 2f * u * (f.c - f.b);
            return tangent.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(tangent, Vector3.up) : f.go.transform.rotation;
        }

        void OnDisable() => Clear();

        void OnDestroy()
        {
            Clear();
            if (probe != null) Destroy(probe.gameObject);
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            RenderPipelineManager.endCameraRendering -= AfterCamera;
            if (clear != null) Destroy(clear.gameObject);
        }

        // ---- the fogged bar's overlay camera -------------------------------------------------------------------------

        void SetupClearCamera()
        {
            var cam = Camera.main;
            var data = cam != null ? cam.GetUniversalAdditionalCameraData() : null;
            if (data == null || data.renderType != CameraRenderType.Base) return;
            clear = new GameObject("BarFlybyCamera").AddComponent<Camera>();
            clear.transform.SetParent(cam.transform, false);
            clear.cullingMask = 1 << FlybyLayer;
            var clearData = clear.GetUniversalAdditionalCameraData();
            clearData.renderType = CameraRenderType.Overlay;
            // The room's depth kept (the inspector's Clear Depth, read-only in code): the walls hide the ships.
            typeof(UniversalAdditionalCameraData).GetField("m_ClearDepth", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(clearData, false);
            clearData.renderShadows = false;
            data.cameraStack.Add(clear);
            cam.cullingMask &= ~(1 << FlybyLayer);
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
            RenderPipelineManager.endCameraRendering += AfterCamera;
        }

        void LateUpdate()
        {
            if (clear == null) return;
            var cam = clear.transform.parent != null ? clear.transform.parent.GetComponent<Camera>() : null;
            if (cam == null) return;
            clear.fieldOfView = cam.fieldOfView;
            clear.nearClipPlane = cam.nearClipPlane;
            clear.farClipPlane = cam.farClipPlane;
        }

        void BeforeCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam != clear) return;
            fogWas = RenderSettings.fog;
            RenderSettings.fog = false;
        }

        void AfterCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam == clear) RenderSettings.fog = fogWas;
        }

        void Clear()
        {
            foreach (var f in flying) if (f.go != null) Destroy(f.go);
            flying.Clear();
        }

        // ---- measuring the room --------------------------------------------------------------------------------------

        void Measure()
        {
            measured = true;
            var cam = Camera.main;
            var shader = Resources.Load<Shader>("GoF2Station/BarDistance");
            var roomRoot = root != null ? root.Find("Room") : null;
            if (cam == null || shader == null || roomRoot == null) return;

            probe = new GameObject("BarFlybyProbe").AddComponent<Camera>();
            probe.enabled = false;
            probe.CopyFrom(cam);
            probe.transform.SetParent(transform, false);
            probe.transform.SetPositionAndRotation(cameraPos, cameraRot);
            probe.clearFlags = CameraClearFlags.SolidColor;
            probe.GetUniversalAdditionalCameraData().renderPostProcessing = false;

            // The sky, the backdrop and (for the distances) everything but the room out of the way.
            var roomRenderers = new HashSet<Renderer>(roomRoot.GetComponentsInChildren<Renderer>());
            var hidden = new List<Renderer>();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                string sh = r.enabled && r.sharedMaterial != null ? r.sharedMaterial.shader.name : "";
                if (sh == "GoF2/Backdrop" || sh == "GoF2/SkyLayer") { r.enabled = false; hidden.Add(r); }
            }
            var sky = RenderSettings.skybox;
            RenderSettings.skybox = null;
            Color[] a = null, b = null, d = null;
            var swapped = new List<(Renderer r, Material[] mats)>();
            var others = new List<Renderer>();
            var distance = new Material(shader);
            try
            {
                a = Render(new Color(1f, 0f, 1f), RenderTextureFormat.ARGB32);
                b = Render(new Color(0f, 1f, 0f), RenderTextureFormat.ARGB32);
                foreach (var r in root.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled) continue;
                    if (!roomRenderers.Contains(r)) { r.enabled = false; others.Add(r); continue; }
                    var mats = r.sharedMaterials;
                    swapped.Add((r, mats));
                    var dm = new Material[mats.Length];
                    for (int i = 0; i < dm.Length; i++) dm[i] = distance;
                    r.sharedMaterials = dm;
                }
                d = Render(Color.clear, RenderTextureFormat.ARGBFloat);
            }
            finally
            {
                foreach (var (r, mats) in swapped) if (r != null) r.sharedMaterials = mats;
                foreach (var r in others) if (r != null) r.enabled = true;
                foreach (var r in hidden) if (r != null) r.enabled = true;
                RenderSettings.skybox = sky;
                Destroy(distance);
            }
            if (a == null || b == null || d == null) return;

            int n = MaskWidth * MaskHeight;
            open = new bool[n];
            var near = new float[n];
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                float diff = Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b);
                open[i] = diff > 0.35f;
                near[i] = open[i] ? 0f : d[i].r;   // through a window: whatever lies outside doesn't count
                any |= near[i] > 0f;
            }
            if (!any) { Debug.LogWarning("BarFlybys: no room distances (float read-back unsupported?): no flybys"); return; }
            limit = new float[n];
            for (int i = 0; i < n; i++)
            {
                limit[i] = MaxAround(near, i, SwayPixels);
                for (int r = SwayPixels * 2; limit[i] <= 0f && r <= MaskWidth; r *= 2) limit[i] = MaxAround(near, i, r);   // a wide window
                if (open[i] && limit[i] > 0f) windows.Add(i);
            }
            Debug.Log($"BarFlybys: {windows.Count} of {n} pixels see-through");
        }

        Color[] Render(Color background, RenderTextureFormat format)
        {
            probe.backgroundColor = background;
            var rt = RenderTexture.GetTemporary(MaskWidth, MaskHeight, 24, format);
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            Color[] pixels = null;
            if (RenderPipeline.SupportsRenderRequest(probe, request))
            {
                RenderPipeline.SubmitRenderRequest(probe, request);
                var active = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(MaskWidth, MaskHeight, format == RenderTextureFormat.ARGBFloat ? TextureFormat.RGBAFloat : TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, MaskWidth, MaskHeight), 0, 0);
                tex.Apply();
                pixels = tex.GetPixels();
                Destroy(tex);
                RenderTexture.active = active;
            }
            RenderTexture.ReleaseTemporary(rt);
            return pixels;
        }

        /// <summary>From the rest pose, 'p' is behind the room or in a window (true), or drawn in front of the room's
        /// surfaces, i.e. inside the room (false).</summary>
        bool Hidden(Vector3 p)
        {
            var v = probe.WorldToViewportPoint(p);
            if (v.z <= 0f) return true;   // behind the camera: out of view
            // Far off screen; just past an edge (the camera sways a few degrees around the rest pose) like the edge.
            if (v.x < -0.15f || v.x > 1.15f || v.y < -0.15f || v.y > 1.15f) return true;
            int x = Mathf.Clamp(Mathf.FloorToInt(v.x * MaskWidth), 0, MaskWidth - 1), y = Mathf.Clamp(Mathf.FloorToInt(v.y * MaskHeight), 0, MaskHeight - 1);
            int i = y * MaskWidth + x;
            return limit[i] > 0f && Vector3.Distance(p, cameraPos) > limit[i] + WallMargin;
        }

        /// <summary>The largest value within 'radius' pixels of pixel 'i'.</summary>
        static float MaxAround(float[] values, int i, int radius)
        {
            int cx = i % MaskWidth, cy = i / MaskWidth;
            float best = 0f;
            for (int y = Mathf.Max(0, cy - radius); y <= Mathf.Min(MaskHeight - 1, cy + radius); y++)
                for (int x = Mathf.Max(0, cx - radius); x <= Mathf.Min(MaskWidth - 1, cx + radius); x++)
                    best = Mathf.Max(best, values[y * MaskWidth + x]);
            return best;
        }

        // ---- the flybys ----------------------------------------------------------------------------------------------

        void Launch()
        {
            for (int attempt = 0; attempt < Tries; attempt++)
                if (TryLaunch()) return;
        }

        bool TryLaunch()
        {
            int pixel = windows[Random.Range(0, windows.Count)];
            var ray = probe.ViewportPointToRay(new Vector3((pixel % MaskWidth + 0.5f) / MaskWidth, (pixel / MaskWidth + 0.5f) / MaskHeight, 0f));
            var dir = ray.direction;
            float near = limit[pixel];
            if (near <= 0f) return false;
            // The Vossk bar's linear fog (0 to 250 m): as close behind the wall as the margin allows.
            float distance = near + (fogged ? Random.Range(WallMargin + 5f, 40f) : Random.Range(MinBeyond, MaxBeyond));
            if (fogged) distance = Mathf.Min(distance, FoggedMaxDistance);
            if (distance < near + WallMargin) return false;
            var p = cameraPos + dir * distance;

            var side = Vector3.Cross(Vector3.up, dir);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
            side.Normalize();
            if (Random.value < 0.5f) side = -side;
            var travel = (side + Vector3.up * Random.Range(-0.2f, 0.2f) + dir * Random.Range(-0.3f, 0.3f)).normalized;
            float half = distance * 1.6f + 100f;   // well past both sides of the view
            Vector3 a0, b0 = p, c0 = p + travel * half;
            a0 = !fogged && Random.value < 0.4f ? p + dir * ApproachFrom : p - travel * half;

            int count = Random.value < 0.3f ? Random.Range(2, 4) : 1;
            var back = -(b0 - a0).normalized * FormationSpacing;
            var wing = Vector3.Cross((c0 - a0).normalized, Vector3.up);
            wing = (wing.sqrMagnitude > 1e-4f ? wing.normalized : Vector3.right) * FormationSpacing;
            var offsets = new Vector3[count];
            for (int n = 1; n < count; n++) offsets[n] = back * n + wing * (n % 2 == 1 ? 1f : -1f) * n;
            foreach (var o in offsets)
                for (int k = 0; k <= PathSamples; k++)
                    if (!Hidden(Bezier(a0 + o, b0 + o, c0 + o, k / (float)PathSamples))) return false;

            float speed = Random.Range(MinSpeed, MaxSpeed);
            float duration = (Vector3.Distance(a0, b0) + Vector3.Distance(b0, c0)) / speed;
            float roll = Random.Range(-12f, 12f);
            int ship = pickShip();
            for (int n = 0; n < count; n++)
            {
                var go = spawn(n == 0 || Random.value < 0.4f ? ship : pickShip());
                if (go == null) continue;
                go.transform.SetParent(root, true);
                go.transform.position = a0 + offsets[n];
                if (clear != null) foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = FlybyLayer;
                var asm = go.GetComponent<AssembledObject>();
                if (asm != null) asm.SetExhaust(true, !asm.HasNpcExhaust);
                flying.Add(new Flyby { go = go, a = a0 + offsets[n], b = b0 + offsets[n], c = c0 + offsets[n], duration = duration, roll = roll });
            }
            return true;
        }
    }
}
