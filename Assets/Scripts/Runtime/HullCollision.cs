// HullCollision.cs
// Remake: collision on the objects' real shapes with Unity's physics, instead of the original's axis-aligned boxes and
// spheres (CollisionVolume, ship_combat.md 2.9) and its +-1000-unit hit cubes:
//   Attach      every hull mesh of a model (LOD 0, not the additive / light / engine layers) gets a MeshCollider on its own
//               child on HullLayer, under one kinematic Rigidbody on the model's root (HullBody); the models are imported
//               with Read/Write for this (AssetImport.IsHullModel), mods' glTF models are readable already. A mesh is
//               cooked once (Physics.BakeMesh) and shared by every collider using it.
//   Cast        shots (Gun): a sphere cast along the shot's path this frame, the nearest hull a shot may hit
//   Overlap     the player's ship (PlayerCollision): the hulls touching its probe sphere, pushed out of with
//               Physics.ComputePenetration
// Untouched: NPC fighters steering round stations and freighters (Obstacle volumes), the original's rules for what can be
// hit (stations never, a ship's own shots never) and for what the player slides along (landmarks and freighters, not
// fighters), asteroids (their cube), crates. The physics simulation isn't used: the bodies are kinematic, the queries read
// the transforms synced once a frame (Sync).

using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class HullCollision
    {
        public const int HullLayer = 24, ProbeLayer = 23;
        public const int HullMask = 1 << HullLayer;

        static readonly Dictionary<Collider, HullBody> bodies = new Dictionary<Collider, HullBody>();
        static readonly HashSet<Mesh> baked = new HashSet<Mesh>();
        static readonly RaycastHit[] hits = new RaycastHit[64];
        static readonly List<Renderer> lowLods = new List<Renderer>();
        static int syncedFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            bodies.Clear();
            baked.Clear();
            syncedFrame = -1;
        }

        /// <summary>Some hull exists (a quick skip for the shot tests).</summary>
        public static bool Any => bodies.Count > 0;

        /// <summary>The hull of 'model' (its root gets the HullBody); 'target' what shots on it hit, 'obstacle' what the
        /// player slides along (either may be null). Null when the model has no readable hull mesh.</summary>
        public static HullBody Attach(GameObject model, Target target, Obstacle obstacle)
        {
            if (model == null) return null;
            var body = model.GetComponent<HullBody>();
            if (body == null)
            {
                lowLods.Clear();
                foreach (var g in model.GetComponentsInChildren<LODGroup>(true))
                {
                    var lods = g.GetLODs();
                    for (int i = 1; i < lods.Length; i++) lowLods.AddRange(lods[i].renderers);
                }
                var meshes = new List<MeshFilter>();
                foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true)) if (IsHullMesh(mf)) meshes.Add(mf);
                if (meshes.Count == 0) return null;
                body = model.AddComponent<HullBody>();
                var rb = model.GetComponent<Rigidbody>();
                if (rb == null) rb = model.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.interpolation = RigidbodyInterpolation.None;
                foreach (var mf in meshes)
                {
                    var mesh = mf.sharedMesh;
                    if (baked.Add(mesh)) Physics.BakeMesh(mesh.GetEntityId(), false);
                    var go = new GameObject("Hull") { layer = HullLayer };
                    go.transform.SetParent(mf.transform, false);
                    var c = go.AddComponent<MeshCollider>();
                    c.sharedMesh = mesh;
                    body.colliders.Add(c);
                    bodies[c] = body;
                }
            }
            body.target = target;
            body.obstacle = obstacle;
            if (obstacle != null) obstacle.hull = body;
            if (target != null) target.hull = body;
            return body;
        }

        /// <summary>LOD 0's shape: not a lower LOD, not an additive / light / engine-glow layer, readable.</summary>
        static bool IsHullMesh(MeshFilter mf)
        {
            var m = mf.sharedMesh;
            if (m == null || !m.isReadable || m.vertexCount == 0) return false;
            var r = mf.GetComponent<Renderer>();
            if (r == null || lowLods.Contains(r)) return false;
            string n = m.name.ToLowerInvariant();
            if (n.Contains("_add") || n.Contains("_lights") || n.Contains("_lod_") || n.Contains("glow")) return false;
            var mat = r.sharedMaterial;
            if (mat != null && mat.shader != null && mat.shader.name.IndexOf("Additive", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return true;
        }

        internal static void Forget(Collider c) => bodies.Remove(c);

        public static HullBody BodyOf(Collider c) => c != null && bodies.TryGetValue(c, out var b) ? b : null;

        /// <summary>The queries read the transforms as of this frame's first query (the hulls moved since the last physics
        /// step: their kinematic bodies aren't simulated).</summary>
        static void Sync()
        {
            if (syncedFrame == Time.frameCount) return;
            syncedFrame = Time.frameCount;
            Physics.SyncTransforms();
        }

        /// <summary>The nearest hull along 'from' -> 'to' (a sphere of 'radius' metres swept along it) whose target
        /// 'valid' accepts; false = none.</summary>
        public static bool Cast(Vector3 from, Vector3 to, float radius, Func<Target, bool> valid, out Target target, out Vector3 point)
        {
            target = null;
            point = to;
            var d = to - from;
            float len = d.magnitude;
            if (len < 1e-5f) return false;
            Sync();
            int n = Physics.SphereCastNonAlloc(from, radius, d / len, hits, len, HullMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (hits[i].distance >= best) continue;
                var b = BodyOf(hits[i].collider);
                if (b == null || b.target == null || !valid(b.target)) continue;
                best = hits[i].distance;
                target = b.target;
                point = hits[i].distance > 0f ? hits[i].point : from;
            }
            return target != null;
        }

        /// <summary>The hull colliders within 'radius' of 'centre' into 'buffer'; their count.</summary>
        public static int Overlap(Vector3 centre, float radius, Collider[] buffer)
        {
            Sync();
            return Physics.OverlapSphereNonAlloc(centre, radius, buffer, HullMask, QueryTriggerInteraction.Ignore);
        }
    }
}
