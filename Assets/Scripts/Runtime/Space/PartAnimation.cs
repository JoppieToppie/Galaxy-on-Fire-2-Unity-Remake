// PartAnimation.cs
// Plays the original keyframe animation stored in each model's .gof2mesh.json sidecar
// (station rings, rotating parts, animated FX). Attached automatically by "GoF2/Build/Materials And Prefabs".
//
// Keyframe times are milliseconds. The original stores position in a Z-up layout that the engine swaps
// to Y-up (engine = (c0, c2, -c1)); engine -> Unity is (x, y, -z) once the import step has turned the
// models to face +Z (ModelOrientationPostprocessor). That import step bakes a 180 deg turn about Y into the vertices
// ((-x, y, -z)), so part offsets get the same flip (ImportFlip) or they slide mirrored (the auto turrets' ammo belts
// left their channel). Rotations: the same frame, so file X / Y / Z turn about Unity x / z / y with the Z one negated
// (rotationMap, applied in the file's Rx * Ry * Rz order; see the map's comment).
// applyMaterialChannels (opt-in: the sky layers, explosions): the `extra` channel (0..100, opacity) goes to the part
// renderer's _Fade, or for the GoF2 Shader Graphs (no _Fade) scales their _Color tint (rgb on additive, alpha otherwise),
// and `v5_0` (a UV scroll, assumed 100 = one texture width) to its _UVOffset.x, through a MaterialPropertyBlock; with
// ownMaterials (GunRig: the projectiles, muzzle flashes and impacts, pooled) through a copy of the part's material instead,
// made once: a renderer with a property block falls out of the SRP Batcher (2026-10-22, a fleet battle: ~150 of its ~200
// draws the batcher couldn't take were these).
// Without it an explosion's debris streaks never fade and hang in space fully stretched (long lines). An animation with
// nothing but `extra` keys (blinking lights: the Kaamo Club's, the plasma array stages') applies them by itself and runs
// for their length (it had no length and stayed off); on an opaque material `extra` scales the colour (_BaseColor on URP
// Lit), a light switched off going dark.

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

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class PartAnimation : MonoBehaviour
    {
        public TextAsset meta;
        public bool play = true;
        public bool loop = true;
        /// <summary>Where the animation starts and every loop wraps back to (ms). Awake sets it to the original's range start:
        /// MeshCreateFromFile 0x75c60 / Transform::InitAnimationRangeInTime 0x7e31c start every mesh at timeBetweenFrames
        /// (LoadPoseMs, the smallest positive key), SetAnimationState(3) restarts there and Transform::Update 0x7e388 wraps a
        /// loop to it, so a file's t 0 keys never show. 226 of the 243 animated files open with such a one-off key (t 0, the
        /// animation from 33 / 50 / 100 ms; in 125 the t 0 pose differs: parts at the origin or elsewhere, the sky layers'
        /// flashes), which played from 0 jumped on every wrap (the bars, the hangars, the gates, the sky). Code may set
        /// another start after Awake (an absolute time, so it never stacks on the default).</summary>
        public float loopStartMs;
        /// <summary>A looping rotation swings back on every other loop instead of snapping back to its start (the Vossk
        /// hangar's ring lights sweep ~57 deg per 2.5 s loop, clear of the portal: looped as keyed they jumped back;
        /// carried on they swept through the portal).</summary>
        public bool pingPongRotation;
        /// <summary>Modified: the rotation keys turn heading first (Rz * Ry * Rx: the file's Z outermost, X about the part's
        /// own axis) instead of the release files' Rx * Ry * Rz. The Terran hangar's beta bots (hangar_terran_bot_*) are keyed
        /// that way: bot_c's arms and body pitch down (-101 / -36 deg) as it flies off, where Rx outermost rolled them over.</summary>
        public bool headingFirst;
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
        [Tooltip("Write the channels into a copy of each part's material (made once) instead of a MaterialPropertyBlock, so the SRP Batcher keeps batching them.")]
        public bool ownMaterials;
        [Tooltip("Pose the parts only while one of their renderers is in a camera's view (the time runs on): weapon effects.")]
        public bool onlyWhenSeen;
        Renderer[] seenBy;
        bool poseNext = true;   // the first frame after it is shown or restarted is always posed: its renderers' view is from before

        static readonly int ColorId = Shader.PropertyToID("_Color"), BaseColorId = Shader.PropertyToID("_BaseColor"),
            FadeId = Shader.PropertyToID("_Fade"), UVOffsetId = Shader.PropertyToID("_UVOffset"), MainTexStId = Shader.PropertyToID("_MainTex_ST");

        // The parsed sidecars, shared by every instance of a model (read only): each Awake parsed its keyframes again, ~300 KB
        // of garbage and ~1.5 ms per ship explosion (the blast and its 3-9 streaks), and every ship spawned.
        static readonly Dictionary<TextAsset, MeshMeta> parsed = new Dictionary<TextAsset, MeshMeta>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => parsed.Clear();

        static MeshMeta Parse(TextAsset meta)
        {
            if (!parsed.TryGetValue(meta, out var m)) parsed[meta] = m = JsonUtility.FromJson<MeshMeta>(meta.text);
            return m;
        }

        class Track { public Transform tr; public AnimationKey[][] pos = new AnimationKey[3][]; public AnimationKey[][] rot = new AnimationKey[3][]; public AnimationKey[][] scl = new AnimationKey[3][]; public float[] rotTimes; public Vector3 basePos; public Quaternion baseRot; public Vector3 baseScale; public AnimationKey[] extra, uv, uvY; public bool signedExtra, offsetExtra; public Renderer renderer; public MaterialPropertyBlock block; public int fadeMode; public int fadeProperty = ColorId; public Color baseColor; public int uvMode; public Vector4 baseST; public bool initialised; public Material own, from; }
        readonly List<Track> tracks = new List<Track>();
        float timeMs, lengthMs, extraMs;

        void Awake()
        {
            // The prefabs saved the old default map: the corrected one instead (an edited map is kept).
            if (rotationMap != null && rotationMap.Length == 3 && rotationMap[0].source == 0 && rotationMap[1].source == 2 && rotationMap[2].source == 1
                && rotationMap[0].sign < 0f && rotationMap[1].sign < 0f && rotationMap[2].sign < 0f)
                rotationMap = new[] { new AxisMap { source = 0, sign = 1 }, new AxisMap { source = 2, sign = -1 }, new AxisMap { source = 1, sign = 1 } };
            if (meta == null) return;
            var m = Parse(meta);
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
                    if (c.target == "extra") { tk.extra = c.keys; extraMs = Mathf.Max(extraMs, c.keys[c.keys.Length - 1].t); tk.signedExtra = IsSignedExtra(c.keys); tk.offsetExtra = IsOffsetExtra(c.keys); continue; }   // not in the length: the transform channels set it
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
            // Remake: opacity keys alone (blinking lights) are the whole animation: their length, applied by themselves.
            if (lengthMs <= 0f && extraMs > 0f) { lengthMs = extraMs; applyMaterialChannels = true; }
            enabled = tracks.Count > 0 && lengthMs > 0f;
            // The original's range start (see loopStartMs); kept when something set one before (a prefab's own value).
            if (loopStartMs <= 0f) loopStartMs = Mathf.Min(LoadPoseMs, lengthMs);
            timeMs = loopStartMs;
        }

        /// <summary>The renderers' materials were swapped (Flight.WeaponTint): their fade colours are read again.</summary>
        public void RefreshMaterials()
        {
            foreach (var tk in tracks) tk.initialised = false;
        }

        void OnDestroy()
        {
            if (tracks == null) return;
            foreach (var tk in tracks) if (tk.own != null) { Destroy(tk.own); tk.own = null; }
        }

        /// <summary>ownMaterials: the part's own copy of its material, made again when someone else put another material on
        /// it (a weapon tint, RefreshMaterials); null where a copy can't stand in (several materials: the property block).</summary>
        static Material OwnMaterial(Track tk)
        {
            var r = tk.renderer;
            if (r == null) return null;
            var cur = r.sharedMaterial;
            if (cur != null && cur == tk.own) return tk.own;
            if (cur == null || r.sharedMaterials.Length != 1) return null;
            if (tk.own != null) Destroy(tk.own);
            tk.from = cur;
            tk.own = new Material(cur) { name = cur.name + " (fade)" };
            r.sharedMaterial = tk.own;
            if (r.HasPropertyBlock()) r.SetPropertyBlock(null);
            tk.initialised = false;
            InitMaterialTrack(tk);
            return tk.own;
        }

        static void InitMaterialTrack(Track tk)
        {
            if (tk.initialised) return;
            tk.initialised = true;
            tk.renderer = tk.tr.GetComponent<Renderer>();
            tk.block ??= new MaterialPropertyBlock();
            var mat = tk.renderer == null ? null : tk.own != null && tk.renderer.sharedMaterial == tk.own ? tk.from : tk.renderer.sharedMaterial;
            // 0 = _Fade, 1 = _Color rgb (additive), 2 = _Color alpha, -1 = nothing to fade
            tk.fadeMode = mat == null ? -1 : mat.HasProperty("_Fade") ? 0 : !mat.HasProperty("_Color") ? -1
                        : mat.shader.name.Contains("Additive") ? 1 : 2;
            // Opaque (no blending): its alpha shows nothing, so the colour is scaled instead (URP Lit's own is _BaseColor).
            if (tk.fadeMode == 2 && mat.renderQueue < 2450) { tk.fadeMode = 1; if (mat.HasProperty(BaseColorId)) tk.fadeProperty = BaseColorId; }
            if (tk.fadeMode > 0) tk.baseColor = mat.GetColor(tk.fadeProperty);
            // 1 = _UVOffset (GoF2/SkyLayer), 2 = _MainTex_ST (the Shader Graphs' main texture tiling and offset), 0 = none
            tk.uvMode = mat == null ? 0 : mat.HasProperty("_UVOffset") ? 1 : mat.HasProperty("_MainTex_ST") ? 2 : 0;
            if (tk.uvMode == 2) { var sc = mat.mainTextureScale; var of = mat.mainTextureOffset; tk.baseST = new Vector4(sc.x, sc.y, of.x, of.y); }
        }

        /// <summary>Remake: an `extra` channel keyed in -100..0 instead of 0..100 (only hangar_nivelian_add's light, every
        /// 2 s: -100 -> 0 -> 0 -> -100). Transform::InternUpdate 0x6ea7c turns the key into a colour through v * 255: on iOS
        /// (fcvtzs, signed) a negative key wraps into the colour byte (-100 -> 1, -50 -> 129, just under 0 -> 254, 0 -> 0): the
        /// light fades up, goes dark at 0, comes back bright and fades down. On Android (vcvt.u32.f32) every key saturates
        /// to 0 and the light never changed. The remake shows it the iOS way.</summary>
        static bool IsSignedExtra(AnimationKey[] keys)
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (var k in keys) { min = Mathf.Min(min, k.v); max = Mathf.Max(max, k.v); }
            return min < 0f && max <= 0f;
        }

        /// <summary>Remake: an `extra` channel keyed a hundred too high, 199.5 -> 99.5 (only the Kothar / Beidan ice
        /// asteroid's explosion cloud, v_asteroid_ice_explosion_anim_alpha part 0; Discord report 2026-11-03: the cloud
        /// stayed at full strength and vanished at once). Transform::InternUpdate 0x7e720's colour (int)(v * 2.55) * 0x1010101
        /// keeps the low byte, so the original wraps past 255 and the cloud fades out like the other asteroids'. Channels
        /// that only overshoot 100 by a key (the EMP blast, the Mining Plant's fire) aren't this.</summary>
        public static bool IsOffsetExtra(AnimationKey[] keys)
        {
            if (keys == null || keys.Length == 0) return false;
            float min = float.MaxValue, max = float.MinValue;
            foreach (var k in keys) { min = Mathf.Min(min, k.v); max = Mathf.Max(max, k.v); }
            return min >= 99f && max > 150f && max <= 200f;
        }

        /// <summary>The opacity (0..1) of an `extra` value: 0..100 clamped; a signed channel (IsSignedExtra) as the iOS
        /// build's colour byte wraps; an offset one (IsOffsetExtra) as every build's wraps past 255, the remake leaving out
        /// the last hundredth under 100.4 (back to full strength for the animation's last ~90 ms, a flash of the whole cloud
        /// before it is gone).</summary>
        public static float ExtraOpacity(float e, bool signedExtra, bool offsetExtra)
        {
            if (signedExtra && e < 0f) return Mathf.Clamp01((256f + e * 2.55f) / 255f);
            if (offsetExtra) { int n = (int)(e * 2.55f); return n >= 256 ? Mathf.Clamp01((n & 0xff) / 255f) : 0f; }
            return Mathf.Clamp01(e / 100f);
        }

        /// <summary>Remake: some part's `extra` channel is keyed in -100..0 (IsSignedExtra): a light the original's
        /// Android build never faded, to be applied (applyMaterialChannels) where the room's other layers aren't.</summary>
        public bool HasSignedExtra
        {
            get
            {
                if (tracks != null) foreach (var tk in tracks) if (tk.extra != null && tk.signedExtra) return true;
                return false;
            }
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

        /// <summary>Starts the animation over (muzzle flashes and impacts restart with every shot), at loopStartMs (the
        /// original's range start: SetAnimationState(3)).</summary>
        public void Restart()
        {
            timeMs = Mathf.Clamp(loopStartMs, 0f, Mathf.Max(0f, lengthMs - 1f));
            play = true;
            poseNext = true;
            if (enabled) Update();
        }

        /// <summary>Remake: 'part' rests at 'localPosition' (its animated offset added to it, like the model's own place): a
        /// part placed again after the model was built (OrbitBuilder: the Kaamo Club's lights). False when no track of this
        /// animation moves it (it is then only placed).</summary>
        public bool SetBasePosition(Transform part, Vector3 localPosition)
        {
            if (part == null) return false;
            part.localPosition = localPosition;
            foreach (var tk in tracks)
                if (tk.tr == part) { tk.basePos = localPosition; return true; }
            return false;
        }

        /// <summary>Remake: 'part' stays at local x 'x' (its X keys dropped, its other channels still played): a part whose
        /// keyed place is wrong in the remake (StationLevel: the Valkyrie hangar's v_hangar_battlestation_add_part32 / 33). False
        /// when no track of this animation moves it (it is then only placed).</summary>
        public bool PinLocalX(Transform part, float x)
        {
            if (part == null) return false;
            part.localPosition = new Vector3(x, part.localPosition.y, part.localPosition.z);
            foreach (var tk in tracks)
                if (tk.tr == part) { tk.pos[0] = null; tk.basePos.x = x; return true; }
            return false;
        }

        /// <summary>Remake: 'part's keyed turn becomes a steady spin (Spin) at the same average rate: the keys' blend
        /// (a normalized lerp) isn't even, and a turn that isn't a whole step of the part's symmetry snaps back a little
        /// when the loop wraps (the Terran gate's light ring: 44 deg per loop on a 45 deg pattern). False when no track of
        /// this animation turns it.</summary>
        public bool SpinContinuously(Transform part)
        {
            if (part == null || tracks == null) return false;
            foreach (var tk in tracks)
            {
                if (tk.tr != part || tk.rotTimes == null) continue;
                float start = Mathf.Clamp(loopStartMs, 0f, lengthMs), span = lengthMs - start;
                if (span <= 0f) return false;
                var a = new Vector3(Eval(tk.rot[0], start, 0), Eval(tk.rot[1], start, 0), Eval(tk.rot[2], start, 0));
                var b = new Vector3(Eval(tk.rot[0], lengthMs, 0), Eval(tk.rot[1], lengthMs, 0), Eval(tk.rot[2], lengthMs, 0));
                var perSecond = Map(b - a, rotationMap) * ((rotationInRadians ? Mathf.Rad2Deg : 1f) * 1000f * speed / span);
                part.localRotation = tk.baseRot * RotationAt(tk, Mathf.Clamp(timeMs, start, lengthMs));
                tk.rot[0] = tk.rot[1] = tk.rot[2] = null;   // the keys no longer set it
                tk.rotTimes = null;
                var spin = part.GetComponent<Spin>();
                if (spin == null) spin = part.gameObject.AddComponent<Spin>();
                // Spin turns about x, y, z in that order; a one-axis turn (the rings) is the same either way.
                spin.degreesPerSecond = new Vector3(perSecond.x, perSecond.y, perSecond.z);
                return true;
            }
            return false;
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

        /// <summary>Plays every part animation under 'root' once from its start (loopStartMs); returns the longest playing
        /// time in ms (from the start to the end).</summary>
        public static float PlayOnce(GameObject root) =>
            PlayOnce(root.GetComponentsInChildren<PartAnimation>(true), root.GetComponentsInChildren<Modding.ModFxPart>(true));

        /// <summary>PlayOnce on parts collected beforehand (GunRig's muzzle flashes and impacts: per shot / per hit, where
        /// the two hierarchy walks and their arrays added up).</summary>
        public static float PlayOnce(PartAnimation[] anims, Modding.ModFxPart[] modParts)
        {
            float longest = 0f;
            foreach (var a in anims)
            {
                a.loop = false;
                a.Restart();
                longest = Mathf.Max(longest, a.LengthMs - Mathf.Clamp(a.loopStartMs, 0f, a.LengthMs));
            }
            return Mathf.Max(longest, Modding.ModFxPart.RestartAll(modParts));   // a mod weapon's sprites / models
        }

        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f) && Mathf.Abs(f) < 1e18f;

        /// <summary>Modified: Eval for a UV scroll channel, where a sawtooth's reset is one original frame long (the wreck's
        /// glow, sn_cargo_001_midorian_wrecked_glow_anim_add part0: 12.5 -> 0 over ~830 ms, then back to 12.5 in 33 ms). At the
        /// original's 30 fps that reset is a jump; interpolated at the remake's frame rate it showed as a quick scroll back, the
        /// loop visible (a player's report). A segment of at most a frame turning against the next one is taken as a jump.</summary>
        static float EvalScroll(AnimationKey[] k, float t, float fallback)
        {
            if (k == null || k.Length == 0) return fallback;
            if (t <= k[0].t) return k[0].v;
            for (int i = 1; i < k.Length; i++)
                if (t <= k[i].t)
                {
                    float span = k[i].t - k[i - 1].t;
                    if (span <= 0f) return k[i].v;
                    if (span <= 40f && i + 1 < k.Length)   // a key frame of the files' 30 fps (33.3 ms)
                    {
                        float d = k[i].v - k[i - 1].v, next = k[i + 1].v - k[i].v;
                        if (d * next < 0f) return t - k[i - 1].t < span * 0.5f ? k[i - 1].v : k[i].v;   // the reset: a jump
                    }
                    return Mathf.Lerp(k[i - 1].v, k[i].v, (t - k[i - 1].t) / span);
                }
            return k[k.Length - 1].v;
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
            var r = new Vector3(Eval(tk.rot[0], t, 0), Eval(tk.rot[1], t, 0), Eval(tk.rot[2], t, 0));
            var v = Map(r, rotationMap) * (rotationInRadians ? Mathf.Rad2Deg : 1f);
            if (headingFirst)   // Modified: Rz * Ry * Rx (see headingFirst)
                return Quaternion.AngleAxis(v.y, Vector3.up) * Quaternion.AngleAxis(v.z, Vector3.forward) * Quaternion.AngleAxis(v.x, Vector3.right);
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

        int loopsWhole = -1;   // Modified: RotationLoopsWhole's answer, worked out once
        /// <summary>Modified: every rotation track ends where the loop starts (within 1 deg: a whole number of turns), so the
        /// loop wraps seamlessly and pingPongRotation has nothing to hide.</summary>
        bool RotationLoopsWhole()
        {
            if (loopsWhole >= 0) return loopsWhole == 1;
            loopsWhole = 1;
            float start = Mathf.Clamp(loopStartMs, 0f, lengthMs);
            foreach (var tk in tracks)
                if ((tk.rot[0] != null || tk.rot[1] != null || tk.rot[2] != null) && tk.rotTimes != null && tk.rotTimes.Length > 1
                    && Quaternion.Angle(RotationAt(tk, start), RotationAt(tk, lengthMs)) > 1f) { loopsWhole = 0; break; }
            return loopsWhole == 1;
        }

        /// <summary>ModelOrientationPostprocessor turned the vertices (-x, y, -z): offsets in the part's frame turn with them.</summary>
        static Vector3 ImportFlip(Vector3 v) => new Vector3(-v.x, v.y, -v.z);

        // A Vector3, not a float[]: this runs per animated part per frame (an array each was most of the flight's garbage).
        static Vector3 Map(Vector3 src, AxisMap[] map) =>
            new Vector3(src[map[0].source] * map[0].sign, src[map[1].source] * map[1].sign, src[map[2].source] * map[2].sign);

        void Update()
        {
            if (!play) return;
            timeMs += Time.deltaTime * 1000f * speed;
            // Transform::Update 0x7e388: past the end a loop goes on from the range start, a one-shot stops at the end; a
            // one-shot never shows the keys before its start either. A range that starts at its end (one key only: the
            // magma asteroid, the wrecked freighter's lights) holds that pose.
            float start = Mathf.Clamp(loopStartMs, 0f, lengthMs);
            if (timeMs > lengthMs)
            {
                if (loop) Loops++;
                float span = lengthMs - start;
                timeMs = loop && span > 0f ? start + (timeMs - lengthMs) % span : lengthMs;
            }
            if (timeMs < start) timeMs = start;
            if (onlyWhenSeen && !poseNext && !Seen()) { if (!loop && timeMs >= lengthMs) play = false; return; }
            poseNext = false;
            Apply();
            // A finished one-shot holds its last pose: applied once, not again every frame for the rest of the level (the
            // freighter wrecks); Restart plays it again.
            if (!loop && timeMs >= lengthMs) play = false;
        }

        void OnEnable() => poseNext = true;

        /// <summary>onlyWhenSeen: one of the parts' renderers was in a camera's view last frame (Renderer.isVisible).</summary>
        bool Seen()
        {
            seenBy ??= GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < seenBy.Length; i++) if (seenBy[i] != null && seenBy[i].isVisible) return true;
            return false;
        }

        void Apply()
        {
            foreach (var tk in tracks)
            {
                if (tk.pos[0] != null || tk.pos[1] != null || tk.pos[2] != null)
                {
                    var p = new Vector3(Eval(tk.pos[0], timeMs, 0), Eval(tk.pos[1], timeMs, 0), Eval(tk.pos[2], timeMs, 0));
                    tk.tr.localPosition = tk.basePos + ImportFlip(Map(p, positionMap)) * metersPerUnit;
                }
                if (tk.rot[0] != null || tk.rot[1] != null || tk.rot[2] != null)
                {
                    // Modified: not a rotation that ends where its loop starts (a full turn, the remodelled Kothar hangar's
                    // v_hangar_deep_science_anim_add rings): swung back it turned the other way on every other loop.
                    float rt = pingPongRotation && loop && (Loops & 1) == 1 && !RotationLoopsWhole()
                        ? Mathf.Clamp(loopStartMs, 0f, lengthMs - 1f) + lengthMs - timeMs : timeMs;
                    tk.tr.localRotation = tk.baseRot * RotationAt(tk, rt);
                }
                if (tk.scl[0] != null || tk.scl[1] != null || tk.scl[2] != null)
                {
                    // Scale keys are in the mesh's own (engine) axis order, not the Z-up layout of the position keys: the ship
                    // explosion's debris streak (explosion_debris_anim_add) stretches sclZ 45x along its length (engine Z) while it
                    // flies about as far; swapped, the 45x went across its width and every streak became a 2.9 km line.
                    var v = new Vector3(Eval(tk.scl[0], timeMs, 1), Eval(tk.scl[1], timeMs, 1), Eval(tk.scl[2], timeMs, 1));
                    // Modified: a part keyed with NaN / infinite scale (sn_cargo_001_midorian_wrecked part11) is shown at none:
                    // Unity refused the assignment with an error every frame (thousands in the Console at 91).
                    if (!Finite(v.x) || !Finite(v.y) || !Finite(v.z)) v = Vector3.zero;
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
                    var own = ownMaterials ? OwnMaterial(tk) : null;
                    if (own == null) tk.renderer.GetPropertyBlock(tk.block);
                    if (tk.extra != null && applyMaterialChannels)
                    {
                        float e = Eval(tk.extra, timeMs, 100f);
                        // A signed channel (IsSignedExtra) as the iOS build shows it: the colour byte wraps, so -100 is
                        // almost dark, it brightens toward 0 and is dark again at 0 exactly; an offset one (IsOffsetExtra)
                        // wraps past 100.
                        float f = ExtraOpacity(e, tk.signedExtra, tk.offsetExtra);
                        if (tk.fadeMode == 0) { if (own != null) own.SetFloat(FadeId, f); else tk.block.SetFloat(FadeId, f); }
                        else if (tk.fadeMode > 0)
                        {
                            var c = tk.fadeMode == 1 ? new Color(tk.baseColor.r * f, tk.baseColor.g * f, tk.baseColor.b * f, tk.baseColor.a)
                                : new Color(tk.baseColor.r, tk.baseColor.g, tk.baseColor.b, tk.baseColor.a * f);
                            if (own != null) own.SetColor(tk.fadeProperty, c); else tk.block.SetColor(tk.fadeProperty, c);
                        }
                    }
                    if (uvAnimated && tk.uvMode != 0)
                    {
                        float u = tk.uv != null ? EvalScroll(tk.uv, timeMs, 0f) / 100f : 0f, v = tk.uvY != null ? EvalScroll(tk.uvY, timeMs, 0f) / 100f : 0f;
                        // GoF2/SkyLayer subtracts _UVOffset; the Shader Graphs take _MainTex_ST the same way round.
                        var uvValue = tk.uvMode == 1 ? new Vector4(u, v, 0f, 0f) : new Vector4(tk.baseST.x, tk.baseST.y, tk.baseST.z - u, tk.baseST.w - v);
                        int uvId = tk.uvMode == 1 ? UVOffsetId : MainTexStId;
                        if (own != null) own.SetVector(uvId, uvValue); else tk.block.SetVector(uvId, uvValue);
                    }
                    if (own == null) tk.renderer.SetPropertyBlock(tk.block);
                }
            }
        }
    }
}
