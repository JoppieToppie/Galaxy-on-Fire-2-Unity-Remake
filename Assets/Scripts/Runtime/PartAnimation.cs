// PartAnimation.cs
// Plays the original keyframe animation stored in each model's .gof2mesh.json sidecar
// (station rings, rotating parts, animated FX). Attached automatically by "GoF2/Build Materials And Prefabs".
//
// Keyframe times are milliseconds. The original stores position in a Z-up layout that the engine swaps
// to Y-up (engine = (c0, c2, -c1)); engine -> Unity is (x, y, -z) once the import step has turned the
// models to face +Z (ModelOrientationPostprocessor). That import step bakes a 180 deg turn about Y into the vertices
// ((-x, y, -z)), so part offsets get the same flip (ImportFlip) or they slide mirrored (the auto turrets' ammo belts
// left their channel). Rotations: the same frame, so file X / Y / Z turn about Unity x / z / y with the Z one negated
// (rotationMap, applied in the file's Rx * Ry * Rz order; see the map's comment).
// applyMaterialChannels (opt-in: the sky layers, explosions): the `extra` channel (0..100, opacity) goes to the part
// renderer's _Fade, or for the GoF2 Shader Graphs (no _Fade) scales their _Color tint (rgb on additive, alpha otherwise),
// and `v5_0` (a UV scroll, assumed 100 = one texture width) to its _UVOffset.x, through a MaterialPropertyBlock.
// Without it an explosion's debris streaks never fade and hang in space fully stretched (long lines).

using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Visuals
{
    [Serializable] public class AnimationKey { public float t; public float v; }
    [Serializable] public class AnimationChannel { public string target; public AnimationKey[] keys; }
    [Serializable] public class Part
    {
        public string name, parent;
        public int vertices;
        public float[] pivot, bsphere;
        public bool hasUV, hasNormals, hasColors;
        public AnimationChannel[] channels;
    }
    [Serializable] public class MeshMeta
    {
        public string source;
        public int version, flags;
        public bool truncated;
        public Part[] parts;
    }

    /// <summary>Which recovered channel drives a Unity axis, and with which sign.</summary>
    [Serializable] public struct AxisMap { public int source; public float sign; }

    public class PartAnimation : MonoBehaviour
    {
        public TextAsset meta;
        public bool play = true;
        public bool loop = true;
        /// <summary>A looping animation skips its keys before this time (ms), at the start and at every wrap: the sky layers'
        /// first key is a one-off flash (the supernova flares 100 -> 50 over the first second, the storm parts all at 100
        /// for 33 ms) that looped from 0 blinked the whole sky.</summary>
        public float loopStartMs;
        /// <summary>A looping rotation swings back on every other loop instead of snapping back to its start (the Vossk
        /// hangar's ring lights sweep ~57 deg per 2.5 s loop, clear of the portal: looped as keyed they jumped back;
        /// carried on they swept through the portal).</summary>
        public bool pingPongRotation;
        public float speed = 1f;
        [Tooltip("Must match the model import scale (ImportSettings.ModelScale).")]
        public float metersPerUnit = 0.05f;

        [Header("Axis mapping (source channel 0/1/2 = file X/Y/Z)")]
        public AxisMap[] positionMap = { new AxisMap { source = 0, sign = 1 }, new AxisMap { source = 2, sign = 1 }, new AxisMap { source = 1, sign = 1 } };
        // The keys are Z-up file rotations Rx * Ry * Rz (Transform::InternUpdate 0x7e720 conjugates them into the engine
        // frame; Quaternion::Set 0x8ba6e / Convert 0x8be1a: ordinary right-handed angles). The part's frame is the file's
        // through (-x, z, -y) (the position map with ImportFlip), a mirror: file X -> Unity +x (angle kept), file Y -> Unity
        // +z (kept), file Z -> Unity +y (negated). The old map (-x, -z, -y) spun the wormhole, the Khador jump, the time
        // jump's hyper drive and the gates' rings backwards (rotY); rotZ (the hangar bots' heading) was already right.
        public AxisMap[] rotationMap = { new AxisMap { source = 0, sign = 1 }, new AxisMap { source = 2, sign = -1 }, new AxisMap { source = 1, sign = 1 } };
        public bool rotationInRadians = true;
        [Tooltip("Apply the `extra` (opacity) and `v5_0` (UV scroll) channels to the part renderers (_Fade / _UVOffset).")]
        public bool applyMaterialChannels;

        class Track { public Transform tr; public AnimationKey[][] pos = new AnimationKey[3][]; public AnimationKey[][] rot = new AnimationKey[3][]; public AnimationKey[][] scl = new AnimationKey[3][]; public float[] rotTimes; public Vector3 basePos; public Quaternion baseRot; public Vector3 baseScale; public AnimationKey[] extra, uv, uvY; public Renderer renderer; public MaterialPropertyBlock block; public int fadeMode; public Color baseColor; public int uvMode; public Vector4 baseST; public bool initialised; }
        readonly List<Track> tracks = new List<Track>();
        float timeMs, lengthMs;

        void Awake()
        {
            // The prefabs saved the old default map: the corrected one instead (an edited map is kept).
            if (rotationMap != null && rotationMap.Length == 3 && rotationMap[0].source == 0 && rotationMap[1].source == 2 && rotationMap[2].source == 1
                && rotationMap[0].sign < 0f && rotationMap[1].sign < 0f && rotationMap[2].sign < 0f)
                rotationMap = new[] { new AxisMap { source = 0, sign = 1 }, new AxisMap { source = 2, sign = -1 }, new AxisMap { source = 1, sign = 1 } };
            if (meta == null) return;
            var m = JsonUtility.FromJson<MeshMeta>(meta.text);
            if (m == null || m.parts == null) return;
            var byName = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (!byName.ContainsKey(t.name)) byName[t.name] = t;

            foreach (var p in m.parts)
            {
                if (p.channels == null || p.channels.Length == 0) continue;
                if (!byName.TryGetValue(p.name, out var tr)) continue;
                var tk = new Track { tr = tr, basePos = tr.localPosition, baseRot = tr.localRotation, baseScale = tr.localScale };
                foreach (var c in p.channels)
                {
                    if (c.keys == null || c.keys.Length == 0 || string.IsNullOrEmpty(c.target) || c.target.Length < 4) continue;
                    foreach (var key in c.keys) if (key.t > 0f) loadPoseMs = Mathf.Min(loadPoseMs, key.t);
                    if (c.target == "extra") { tk.extra = c.keys; continue; }   // not in the length: the transform channels set it
                    // The UV scrolls set the length too: the burning stations' fire and smoke have no other keys.
                    if (c.target == "v5_0" || c.target == "v5_1")
                    {
                        if (c.target == "v5_0") tk.uv = c.keys; else tk.uvY = c.keys;
                        lengthMs = Mathf.Max(lengthMs, c.keys[c.keys.Length - 1].t);
                        continue;
                    }
                    int axis = "XYZ".IndexOf(c.target[3]);
                    if (axis < 0) continue;
                    if (c.target.StartsWith("pos")) tk.pos[axis] = c.keys;
                    else if (c.target.StartsWith("rot")) tk.rot[axis] = c.keys;
                    else if (c.target.StartsWith("scl")) tk.scl[axis] = c.keys;
                    lengthMs = Mathf.Max(lengthMs, c.keys[c.keys.Length - 1].t);
                    if (c.keys.Length > 1) secondKeyMs = Mathf.Min(secondKeyMs, c.keys[1].t);
                }
                // The rotation keys' times (all three channels), for the per-key quaternions (RotationAt).
                var times = new SortedSet<float>();
                foreach (var k in tk.rot) if (k != null) foreach (var key in k) times.Add(key.t);
                if (times.Count > 0) { tk.rotTimes = new float[times.Count]; times.CopyTo(tk.rotTimes); }
                tracks.Add(tk);
            }
            enabled = tracks.Count > 0 && lengthMs > 0f;
        }

        static void InitMaterialTrack(Track tk)
        {
            if (tk.initialised) return;
            tk.initialised = true;
            tk.renderer = tk.tr.GetComponent<Renderer>();
            tk.block = new MaterialPropertyBlock();
            var mat = tk.renderer != null ? tk.renderer.sharedMaterial : null;
            // 0 = _Fade, 1 = _Color rgb (additive), 2 = _Color alpha, -1 = nothing to fade
            tk.fadeMode = mat == null ? -1 : mat.HasProperty("_Fade") ? 0 : !mat.HasProperty("_Color") ? -1
                        : mat.shader.name.Contains("Additive") ? 1 : 2;
            if (tk.fadeMode > 0) tk.baseColor = mat.GetColor("_Color");
            // 1 = _UVOffset (GoF2/SkyLayer), 2 = _MainTex_ST (the Shader Graphs' main texture tiling and offset), 0 = none
            tk.uvMode = mat == null ? 0 : mat.HasProperty("_UVOffset") ? 1 : mat.HasProperty("_MainTex_ST") ? 2 : 0;
            if (tk.uvMode == 2) { var sc = mat.mainTextureScale; var of = mat.mainTextureOffset; tk.baseST = new Vector4(sc.x, sc.y, of.x, of.y); }
        }

        /// <summary>How often the looping animation has wrapped (the storm sky re-rolls its rotation on each).</summary>
        public int Loops { get; private set; }

        float secondKeyMs = float.MaxValue, loadPoseMs = float.MaxValue;

        /// <summary>MeshCreateFromFile 0x75c60: SetAnimationRangeInTime starts the animation at timeBetweenFrames, the
        /// smallest positive key time (MeshReadData), then Update(0): a mesh never shows its t 0 keys, it is posed at its
        /// first positive key from the moment it loads. 0 without keys.</summary>
        public float LoadPoseMs => loadPoseMs < float.MaxValue ? loadPoseMs : 0f;

        /// <summary>The loop's real start when the file opens with a one-off key within 100 ms (the room layers: every
        /// part at the origin at t 0, the loop from the second key at 33 / 50 ms), else 0. For loopStartMs.</summary>
        public float OneOffStartMs => secondKeyMs <= 100f ? secondKeyMs : 0f;

        /// <summary>Length of the animation in ms (0 if it has no keyframes).</summary>
        public float LengthMs => lengthMs;

        /// <summary>Starts the animation over (muzzle flashes and impacts restart with every shot), at loopStartMs (0 unless
        /// a one-off first key is skipped: the Midorian bar prop).</summary>
        public void Restart()
        {
            timeMs = Mathf.Clamp(loopStartMs, 0f, Mathf.Max(0f, lengthMs - 1f));
            play = true;
            if (enabled) Update();
        }

        /// <summary>Shows the pose at 'atMs' and stops there (an object whose animation the original never advances);
        /// without a time the load pose (LoadPoseMs), where the original holds a mesh it never updates.</summary>
        public void Hold(float atMs = -1f)
        {
            if (atMs < 0f) atMs = LoadPoseMs;
            timeMs = Mathf.Clamp(atMs, 0f, lengthMs);
            if (tracks != null) Apply();
            play = false;
        }

        /// <summary>Stops where it is (the level script no longer calls Transform::Update on it).</summary>
        public void Pause() => play = false;

        /// <summary>Hold() on every part animation under 'root' (without a time: each one's load pose).</summary>
        public static void HoldAll(GameObject root, float atMs = -1f)
        {
            if (root == null) return;
            foreach (var a in root.GetComponentsInChildren<PartAnimation>(true)) a.Hold(atMs);
        }

        /// <summary>Hold() on every part animation under 'root' at its last frame: Transform::Update(dt = the length) once
        /// and never again (the Valkyrie battlestation after step 78: its arms unfolded, PlayerStation ctor 0x1473c2 /
        /// Level::createStaticObject 0xcdcee).</summary>
        public static void HoldAllAtEnd(GameObject root) => HoldAll(root, float.MaxValue);

        /// <summary>Hold() on every part animation under 'root' at its pose after a one-off first key (OneOffStartMs; t 0
        /// without one): a model whose rest pose only exists for the first 50 ms (the Void station: scale 1 at t 0, its
        /// real x10.065 from 50 ms).</summary>
        public static void HoldAllAfterOneOff(GameObject root)
        {
            if (root == null) return;
            foreach (var a in root.GetComponentsInChildren<PartAnimation>(true)) a.Hold(a.OneOffStartMs);
        }

        /// <summary>Plays every part animation under 'root' once from the start; returns the longest length in ms.</summary>
        public static float PlayOnce(GameObject root)
        {
            float longest = 0f;
            foreach (var a in root.GetComponentsInChildren<PartAnimation>(true))
            {
                a.loop = false;
                a.Restart();
                longest = Mathf.Max(longest, a.LengthMs);
            }
            return longest;
        }

        static float Eval(AnimationKey[] k, float t, float fallback)
        {
            if (k == null || k.Length == 0) return fallback;
            if (t <= k[0].t) return k[0].v;
            for (int i = 1; i < k.Length; i++)
                if (t <= k[i].t)
                {
                    float span = k[i].t - k[i - 1].t;
                    return span <= 0f ? k[i].v : Mathf.Lerp(k[i - 1].v, k[i].v, (t - k[i - 1].t) / span);
                }
            return k[k.Length - 1].v;
        }

        Quaternion KeyRotation(Track tk, float t)
        {
            var r = new[] { Eval(tk.rot[0], t, 0), Eval(tk.rot[1], t, 0), Eval(tk.rot[2], t, 0) };
            var v = Map(r, rotationMap) * (rotationInRadians ? Mathf.Rad2Deg : 1f);
            // The file's order Rx * Ry * Rz, its axes X, Y, Z being Unity x, z, y (Quaternion.Euler would apply z, x, y).
            return Quaternion.AngleAxis(v.x, Vector3.right) * Quaternion.AngleAxis(v.z, Vector3.forward) * Quaternion.AngleAxis(v.y, Vector3.up);
        }

        /// <summary>Transform::InternUpdate 0x7e720: each key's rotation becomes a quaternion (Quaternion(x, y, z)) and
        /// between keys AbyssEngine::Quaternion::Lerp(const&amp;, const&amp;, float) 0x8bbd8 blends them: a plain normalized
        /// lerp without the shortest-path flip. So a key more than a turn away wraps: a wreck part keyed 0 -> -12.04 rad
        /// over 10 s turns +0.53 rad, where lerping the angles spun it almost twice (the freighter wrecks looked fast).</summary>
        Quaternion RotationAt(Track tk, float t)
        {
            var times = tk.rotTimes;
            if (times.Length == 1 || t <= times[0]) return KeyRotation(tk, times[0]);
            if (t >= times[times.Length - 1]) return KeyRotation(tk, times[times.Length - 1]);
            int i = 1;
            while (t > times[i]) i++;
            float t0 = times[i - 1], t1 = times[i];
            var q0 = KeyRotation(tk, t0);
            var q1 = KeyRotation(tk, t1);
            float f = t1 > t0 ? (t - t0) / (t1 - t0) : 1f;
            var q = new Vector4(q0.x + (q1.x - q0.x) * f, q0.y + (q1.y - q0.y) * f, q0.z + (q1.z - q0.z) * f, q0.w + (q1.w - q0.w) * f);
            float len = q.magnitude;
            if (len < 1e-5f) return q0;   // a key exactly a full turn away (q1 = -q0): the same orientation
            q /= len;
            return new Quaternion(q.x, q.y, q.z, q.w);
        }

        /// <summary>ModelOrientationPostprocessor turned the vertices (-x, y, -z): offsets in the part's frame turn with them.</summary>
        static Vector3 ImportFlip(Vector3 v) => new Vector3(-v.x, v.y, -v.z);

        static Vector3 Map(float[] src, AxisMap[] map) =>
            new Vector3(src[map[0].source] * map[0].sign, src[map[1].source] * map[1].sign, src[map[2].source] * map[2].sign);

        void Update()
        {
            if (!play) return;
            timeMs += Time.deltaTime * 1000f * speed;
            float start = loop ? Mathf.Clamp(loopStartMs, 0f, lengthMs - 1f) : 0f;
            if (timeMs > lengthMs)
            {
                if (loop) Loops++;
                timeMs = loop ? start + (timeMs - lengthMs) % Mathf.Max(1f, lengthMs - start) : lengthMs;
            }
            if (timeMs < start) timeMs = start;
            Apply();
        }

        void Apply()
        {
            foreach (var tk in tracks)
            {
                if (tk.pos[0] != null || tk.pos[1] != null || tk.pos[2] != null)
                {
                    var p = new[] { Eval(tk.pos[0], timeMs, 0), Eval(tk.pos[1], timeMs, 0), Eval(tk.pos[2], timeMs, 0) };
                    tk.tr.localPosition = tk.basePos + ImportFlip(Map(p, positionMap)) * metersPerUnit;
                }
                if (tk.rot[0] != null || tk.rot[1] != null || tk.rot[2] != null)
                {
                    float rt = pingPongRotation && loop && (Loops & 1) == 1
                        ? Mathf.Clamp(loopStartMs, 0f, lengthMs - 1f) + lengthMs - timeMs : timeMs;
                    tk.tr.localRotation = tk.baseRot * RotationAt(tk, rt);
                }
                if (tk.scl[0] != null || tk.scl[1] != null || tk.scl[2] != null)
                {
                    var s = new[] { Eval(tk.scl[0], timeMs, 1), Eval(tk.scl[1], timeMs, 1), Eval(tk.scl[2], timeMs, 1) };
                    // Scale keys are in the mesh's own (engine) axis order, not the Z-up layout of the position keys: the ship
                    // explosion's debris streak (explosion_debris_anim_add) stretches sclZ 45x along its length (engine Z) while it
                    // flies about as far; swapped, the 45x went across its width and every streak became a 2.9 km line.
                    var v = new Vector3(s[0], s[1], s[2]);
                    tk.tr.localScale = Vector3.Scale(tk.baseScale, v);
                }
                // The UV scroll channels (v5_0 u, v5_1 v; 100 = one texture) run on every mesh, as the engine animates them
                // (the burning stations' fire and smoke, plasma beams and streams, projectiles, gas clouds), the clamped
                // effect atlases too: their scrolling cells stay inside the texture (the beams' strips repeat every scroll
                // period, sn_projectiles 207 / 222 / 228; the fireworks step down their column), so nothing smears; held
                // still, the transfusion / repair beams and the Raccoon's beam lost their flow. `extra` stays opt-in.
                bool uvAnimated = tk.uv != null || tk.uvY != null;
                if ((applyMaterialChannels && tk.extra != null) || uvAnimated)
                {
                    InitMaterialTrack(tk);
                    if (tk.renderer == null) continue;
                    tk.renderer.GetPropertyBlock(tk.block);
                    if (tk.extra != null && applyMaterialChannels)
                    {
                        float f = Mathf.Clamp01(Eval(tk.extra, timeMs, 100f) / 100f);
                        if (tk.fadeMode == 0) tk.block.SetFloat("_Fade", f);
                        else if (tk.fadeMode == 1) tk.block.SetColor("_Color", new Color(tk.baseColor.r * f, tk.baseColor.g * f, tk.baseColor.b * f, tk.baseColor.a));
                        else if (tk.fadeMode == 2) tk.block.SetColor("_Color", new Color(tk.baseColor.r, tk.baseColor.g, tk.baseColor.b, tk.baseColor.a * f));
                    }
                    if (uvAnimated)
                    {
                        float u = tk.uv != null ? Eval(tk.uv, timeMs, 0f) / 100f : 0f, v = tk.uvY != null ? Eval(tk.uvY, timeMs, 0f) / 100f : 0f;
                        // GoF2/SkyLayer subtracts _UVOffset; the Shader Graphs take _MainTex_ST the same way round.
                        if (tk.uvMode == 1) tk.block.SetVector("_UVOffset", new Vector4(u, v, 0f, 0f));
                        else if (tk.uvMode == 2) tk.block.SetVector("_MainTex_ST", new Vector4(tk.baseST.x, tk.baseST.y, tk.baseST.z - u, tk.baseST.w - v));
                    }
                    tk.renderer.SetPropertyBlock(tk.block);
                }
            }
        }
    }
}
