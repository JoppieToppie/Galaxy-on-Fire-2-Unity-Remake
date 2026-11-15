// Explosion.cs
// The original's Explosion (Explosion::Explosion 0xb4a90, Reference/research/ship_combat.md 5.3, weapons_special.md 3.4):
//   type 0   explosion_anim_lookat_alpha (+ its _add child), 4500 ms, always facing the camera, plus 3..9
//            explosion_debris_anim_add streaks for ships (addFireStreaks: random rotation, scale 0.5..0.99; bombs and mines
//            have none), sound 18 or 19 at random (weapons: their own sound). setScaling(s) scales it (freighter wrecks
//            x6, the battleship x8, the Pirate Outposts x8)
//   type 7   explosion_emp_anim_lookat_add, 2000 ms (EMP bombs)
//   type 8-10 v_scattergun_000_explosion_lookat_anim_add (scatter guns), random roll 0..3.14 rad, scale 0.6..0.99 and
//            animation speed 0.7..1.29 (Explosion::setScaling / start); each type its own material (meshes 16806-16808:
//            20151 / 20152 / 20153, CombatAssets.scatterMaterials)
//   type 11  the shock blast: sn_shock_blast_glow (camera-facing) + sn_shock_blast_sphere facing the ship's direction,
//            setScaling(50000), played at half speed
//   type 13  sn_fireworks_lookat_anim_add, setScaling(0.25)
// During the first 2000 ms it rumbles the camera: p = (1 - min(d, 30000) / 30000) * (1 - t / 2000)
// (Explosion::update 0xb5768 -> TargetFollowCamera::setRumblePercentage).
// Remake: pooled. Each explosion was 2-11 Instantiates (the blast, its streaks); a finished one now gives its parts back
// (per prefab and material, stripped and their animations collected once) and its root waits for the next Spawn. The pool
// lives in the active scene and goes with it.

using System.Collections.Generic;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class Explosion : MonoBehaviour
    {
        const float M = 0.05f, RumbleMs = 2000f, RumbleRange = 30000f;

        /// <summary>One pooled part: a pivot holding the instance (Add's camera-facing turn), or a bare streak.</summary>
        sealed class Part
        {
            public Transform root;
            public GameObject instance;
            public PartAnimation[] anims;
            public Modding.ModFxPart[] mods;
            public (GameObject, Material, bool) key;
        }

        static readonly Dictionary<(GameObject, Material, bool), Stack<Part>> freeParts = new Dictionary<(GameObject, Material, bool), Stack<Part>>();
        static readonly Stack<Explosion> freeRoots = new Stack<Explosion>();
        static Transform pool;   // inactive holder in the active scene; null again once its scene is gone

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { freeParts.Clear(); freeRoots.Clear(); pool = null; }

        readonly List<Transform> lookAt = new List<Transform>();
        readonly List<Part> parts = new List<Part>();
        float lengthMs = 4500f, ageMs;
        bool rumble = true;
        int serial;

        public float LengthMs => lengthMs;
        public bool Finished => ageMs >= lengthMs;
        /// <summary>This Spawn's number: the root is used again once it has played out (Done).</summary>
        public int Serial => serial;
        /// <summary>The explosion of Spawn 'spawnSerial' has played out (or the root has moved on to another one).</summary>
        public bool Done(int spawnSerial) => spawnSerial != serial || Finished;

        /// <summary>Spawns a type-0 ship explosion (null when the combat assets are missing).</summary>
        public static Explosion Spawn(Vector3 position, float scale = 1f, bool sound = true)
        {
            var assets = CombatAssets.Load();
            var clip = sound && assets != null ? (Random.value < 0.5f ? CombatAssets.Pick(assets.explosionBig) : CombatAssets.Pick(assets.explosionMid)) : null;
            return Spawn(0, position, Vector3.forward, scale, clip, true);
        }

        /// <summary>An explosion of the given type; 'forward' orients the shock-blast sphere; 'debris' = the type-0 fire streaks.</summary>
        public static Explosion Spawn(int type, Vector3 position, Vector3 forward, float scale, AudioClip sound, bool debris)
        {
            var assets = CombatAssets.Load();
            if (assets == null) return null;
            var e = TakeRoot(type);
            e.transform.position = position;
            e.lengthMs = 0f;
            e.ageMs = 0f;
            float speed = 1f;
            switch (type)
            {
                case 7:
                    e.Add(assets.explosionEmp, scale, true, speed);
                    break;
                case 8: case 9: case 10:
                {
                    // Explosion::start: random roll and scale; setScaling: random animation speed.
                    speed = 0.7f + Random.Range(0, 60) / 100f;
                    // Each gun's own colour: meshes 16806 / 16807 / 16808 share the model but not the material (20151-20153).
                    var mats = assets.scatterMaterials;
                    var mat = mats != null && type - 8 < mats.Length ? mats[type - 8] : null;
                    var t = e.Add(assets.explosionScatter, scale * (0.6f + Random.Range(0, 40) / 100f), true, speed, mat);
                    if (t != null) t.GetChild(0).localRotation = Quaternion.Euler(0f, 0f, Random.Range(0, 3141) / 1000f * Mathf.Rad2Deg);
                    break;
                }
                case 11:
                    e.Add(assets.shockGlow, 50000f * scale, true, 0.5f);
                    var sphere = e.Add(assets.shockSphere, 50000f * scale, false, 0.5f);
                    if (sphere != null) sphere.rotation = Quaternion.LookRotation(forward, Vector3.up);
                    break;
                case 13:
                    e.Add(assets.fireworksBurst, 0.25f * scale, true, speed);
                    break;
                default:
                    e.Add(assets.explosion, scale, true, speed);
                    if (debris && assets.debris != null)
                    {
                        int n = 3 + Random.Range(0, 7);
                        for (int i = 0; i < n; i++)
                        {
                            var d = e.TakePart(assets.debris, null, false);
                            // Explosion::addFireStreaks 0xb52ca: setRotation of three nextInt(360) angles (the decompile drops
                            // the middle one; the third was left at 0 here).
                            d.root.localRotation = Quaternion.Euler(Random.Range(0, 360), Random.Range(0, 360), Random.Range(0, 360));
                            // addFireStreaks: 0.5..0.99, never scaled again (Explosion::setScaling 0xb4ee0 scales only the
                            // blast and its _add, the streaks just get its animation speed): a freighter's x6 made them huge lines.
                            d.root.localScale = assets.debris.transform.localScale * (Random.Range(50, 100) * 0.01f);
                            foreach (var a in d.anims) a.applyMaterialChannels = true;   // fade as they stretch
                            e.lengthMs = Mathf.Max(e.lengthMs, PartAnimation.PlayOnce(d.anims, d.mods));
                        }
                    }
                    break;
            }
            if (e.lengthMs <= 0f) e.lengthMs = 2000f;
            e.rumble = type != 8 && type != 9 && type != 10;   // the scatter bursts are updated without a camera
            if (sound != null) Sfx.PlayAt(sound, position);
            return e;
        }

        /// <summary>One part: a pivot (for the camera-facing turn) holding the mesh, its animation at 'speed'; 'material' (if
        /// any) replaces the prefab's on every renderer before the animation reads it.</summary>
        Transform Add(GameObject prefab, float scale, bool faceCamera, float speed, Material material = null)
        {
            if (prefab == null) return null;
            var p = TakePart(prefab, material, true);
            p.instance.transform.localScale = prefab.transform.localScale * scale;
            foreach (var a in p.anims) { a.speed = speed; a.applyMaterialChannels = true; }   // their `extra` fade-out
            float len = PartAnimation.PlayOnce(p.anims, p.mods) / Mathf.Max(0.05f, speed);
            lengthMs = Mathf.Max(lengthMs, len);
            if (faceCamera) lookAt.Add(p.root);
            return p.root;
        }

        static Transform Pool()
        {
            if (pool != null) return pool;
            // A new scene: the old one took the pooled objects with it.
            freeParts.Clear();
            freeRoots.Clear();
            var go = new GameObject("Explosion pool");
            go.SetActive(false);
            pool = go.transform;
            return pool;
        }

        static Explosion TakeRoot(int type)
        {
            Pool();
            while (freeRoots.Count > 0)
            {
                var e = freeRoots.Pop();
                if (e == null) continue;
                e.serial++;
                e.gameObject.SetActive(true);
                return e;
            }
            return new GameObject($"Explosion {type}").AddComponent<Explosion>();
        }

        /// <summary>A part from the pool (a new instance when none is free) under this explosion at its prefab's pose;
        /// 'pivot': inside a pivot of its own (Add's camera-facing turn), else bare (the streaks).</summary>
        Part TakePart(GameObject prefab, Material material, bool pivot)
        {
            var key = (prefab, material, pivot);
            Part p = null;
            if (freeParts.TryGetValue(key, out var stack))
                while (p == null && stack.Count > 0)
                {
                    p = stack.Pop();
                    if (p.root == null) p = null;
                }
            if (p == null)
            {
                // Made under this (active) explosion, so PartAnimation.Awake runs now, as Instantiate always did.
                p = new Part { key = key };
                var holder = pivot ? new GameObject(prefab.name).transform : null;
                if (holder != null) holder.SetParent(transform, false);
                p.instance = Instantiate(prefab, holder != null ? holder : transform, false);
                p.root = holder != null ? holder : p.instance.transform;
                if (material != null) foreach (var r in p.instance.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = material;
                GunRig.StripForFx(p.instance);
                p.anims = p.instance.GetComponentsInChildren<PartAnimation>(true);
                p.mods = p.instance.GetComponentsInChildren<Modding.ModFxPart>(true);
            }
            p.root.SetParent(transform, false);
            var inst = p.instance.transform;
            inst.localPosition = prefab.transform.localPosition;
            inst.localRotation = prefab.transform.localRotation;
            inst.localScale = prefab.transform.localScale;
            if (pivot)
            {
                p.root.localPosition = Vector3.zero;
                p.root.localRotation = Quaternion.identity;
            }
            parts.Add(p);
            return p;
        }

        /// <summary>Played out: the parts back to the pool, the root waiting for the next Spawn.</summary>
        void Release()
        {
            var holder = Pool();
            foreach (var p in parts)
            {
                if (p.root == null) continue;
                p.root.SetParent(holder, false);
                if (!freeParts.TryGetValue(p.key, out var stack)) freeParts[p.key] = stack = new Stack<Part>();
                stack.Push(p);
            }
            parts.Clear();
            lookAt.Clear();
            gameObject.SetActive(false);
            freeRoots.Push(this);
        }

        void LateUpdate()
        {
            ageMs += Time.deltaTime * 1000f;
            var cam = Camera.main;
            if (cam != null) foreach (var t in lookAt) if (t != null) t.rotation = cam.transform.rotation;   // "_lookat" meshes
            if (rumble && cam != null && ageMs < RumbleMs)
            {
                float d = (cam.transform.position - transform.position).magnitude / M;
                float p = (1f - Mathf.Min(d, RumbleRange) / RumbleRange) * (1f - ageMs / RumbleMs);
                var chase = cam.GetComponent<ChaseCamera>();
                if (chase != null) chase.Rumble(p);
            }
            if (ageMs >= lengthMs + 200f) Release();
        }
    }
}
