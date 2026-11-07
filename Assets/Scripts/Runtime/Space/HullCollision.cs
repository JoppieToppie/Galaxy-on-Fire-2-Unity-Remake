// HullCollision.cs
// Remake: collision on the objects' real shapes with Unity's physics, instead of the original's axis-aligned boxes and
// spheres (CollisionVolume, ship_combat.md 2.9) and its +-1000-unit hit cubes:
//   Attach      every hull mesh of a model (LOD 0, not the additive / light / engine layers) gets a MeshCollider on its own
//               child on HullLayer, under one kinematic Rigidbody on the model's root (HullBody); the models are imported
//               with Read/Write for this (AssetImport.IsHullModel), mods' glTF models are readable already. A mesh is
//               cooked once (Physics.BakeMesh) and shared by every collider using it.
//   Cast        shots (Gun): a sphere cast along the shot's path this frame, the nearest hull a shot may hit
//   Overlap     the player's ship (PlayerCollision): the hulls touching its probe sphere, pushed out of with
//               Physics.ComputePenetration; Sweep: the probe swept from last frame's place, so a fast ship (boost,
//               fast-forward) stops at a thin hull instead of passing through it between two frames
//   Shown       a part that can't be seen (its renderer off, scaled to nothing, faded out by its animation's `extra`
//               channel) neither stops shots nor the ship
//   Suppressed  the main menu's backdrop builds no hulls (nobody flies there; the cooking would only cost loading)
// Sweep, Shown and Suppressed follow a contributor's precise-collision version (2026-10-11).
// Ship collision (Options > Gameplay, Settings.ShipCollision, a session rule; players found the fighters hard to hit on
// their real shapes, 2026-10-17; HullBody.Collides, Mode): Exact as above; Mixed (default) leaves the flyable ships' hulls
// out (HullBody.flyableShip): shots hit fighters, the player's ship and other players' ships on the original's hit cube,
// and the player meets another player's ship on its sphere, while freighters, the battleship, the capital ships,
// stations, gates, turrets and objects keep their hulls; Original leaves every hull out: the original's boxes, spheres and
// cubes everywhere (CollisionVolume obstacles, Target cubes / boxes). Under Original no hull is built (Attach only notes
// the model, `pending`: no cooking at the spawns, no kinematic bodies to sync every frame; Original is Android's default,
// 2026-10-22); the mode leaving Original builds the noted ones (Mode), so switching is still at once.
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
        static readonly MaterialPropertyBlock shownBlock = new MaterialPropertyBlock();
        static readonly int FadeId = Shader.PropertyToID("_Fade"), ColorId = Shader.PropertyToID("_Color"), BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>No hulls are made while set (MenuBackground: the main menu's live orbit).</summary>
        public static bool Suppressed;

        static int modeFrame = -1;
        static int mode = GoF2Remake.Data.SessionRules.DefaultCollision;
        static readonly List<(GameObject model, Target target, Obstacle obstacle, bool flyableShip)> pending =
            new List<(GameObject, Target, Obstacle, bool)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            bodies.Clear();
            baked.Clear();
            syncedFrame = -1;
            Suppressed = false;
            modeFrame = -1;
            pending.Clear();
        }

        /// <summary>Options > Gameplay "Ship collision" (SessionRules.CollisionExact / Mixed / Original; the session's in
        /// multiplayer). Read once a frame: every shot asks.</summary>
        public static int Mode
        {
            get
            {
                if (modeFrame != Time.frameCount || !Application.isPlaying)
                {
                    modeFrame = Time.frameCount;
                    mode = GoF2Remake.Data.Settings.ShipCollision;
                    if (mode != GoF2Remake.Data.SessionRules.CollisionOriginal && pending.Count > 0)
                    {
                        var noted = pending.ToArray();
                        pending.Clear();
                        foreach (var p in noted) if (p.model != null) Attach(p.model, p.target, p.obstacle, p.flyableShip);
                    }
                }
                return mode;
            }
        }

        /// <summary>A hull takes part under the current mode: Exact every one, Mixed all but the flyable ships', Original
        /// none.</summary>
        public static bool Takes(bool flyableShip)
        {
            int m = Mode;
            return m == GoF2Remake.Data.SessionRules.CollisionExact || (m == GoF2Remake.Data.SessionRules.CollisionMixed && !flyableShip);
        }

        /// <summary>A ship number a player can fly: not the freighter (13), the battleship (14) or 15 (the debug capital
        /// hulls fly on those); a mod's ship is.</summary>
        public static bool FlyableShip(int ship) => ship >= 0 && ship != 13 && ship != 14 && ship != 15;

        /// <summary>Some hull exists (a quick skip for the shot tests).</summary>
        public static bool Any => bodies.Count > 0;

        /// <summary>The hull of 'model' (its root gets the HullBody); 'target' what shots on it hit, 'obstacle' what the
        /// player slides along (either may be null); 'flyableShip' a ship a player can fly (HullBody.flyableShip: Mixed
        /// ship collision leaves it out). Null when the model has no readable hull mesh.</summary>
        public static HullBody Attach(GameObject model, Target target, Obstacle obstacle, bool flyableShip = false)
        {
            if (model == null || Suppressed) return null;
            var body = model.GetComponent<HullBody>();
            if (body == null && Application.isPlaying && Mode == GoF2Remake.Data.SessionRules.CollisionOriginal)
            {
                pending.RemoveAll(p => p.model == null || p.model == model);
                pending.Add((model, target, obstacle, flyableShip));
                return null;
            }
            if (body == null)
            {
                lowLods.Clear();
                foreach (var g in model.GetComponentsInChildren<LODGroup>(true))
                {
                    var lods = g.GetLODs();
                    // Only what the lower levels alone show: a mesh kept in every level (AutoLods' levels of a mod hull keep
                    // its small parts) is LOD 0's too.
                    var first = lods.Length > 0 ? new HashSet<Renderer>(lods[0].renderers) : new HashSet<Renderer>();
                    for (int i = 1; i < lods.Length; i++)
                        foreach (var r in lods[i].renderers) if (r != null && !first.Contains(r)) lowLods.Add(r);
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
            body.flyableShip = flyableShip;
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
            if (n.Contains("_add") || n.Contains("_lights") || LowerLodName(n) || n.Contains("glow")) return false;
            var mat = r.sharedMaterial;
            if (mat != null && mat.shader != null && (mat.shader.name.IndexOf("Additive", StringComparison.OrdinalIgnoreCase) >= 0
                || mat.shader.name.StartsWith("GoF2/Mod", StringComparison.Ordinal))) return false;   // a mod model's glow, hologram, shimmer, flame
            return true;
        }

        /// <summary>A lower level of detail by its mesh name (lower case): "_lod_" and a number above 0 (the game's
        /// "battleship_terran_lod_1"), or AutoLods' generated levels. LOD 0 by name is a hull: Galaxy on Fire 3's meshes are all "..._lod_000_body", and
        /// skipping every "_lod_" left the GoF3 Systems mod's stations, gates and traffic without a hull, so the player met
        /// the box round the whole model (Rings of Rhea's ~3.3 x 1.7 x 7 km).</summary>
        public static bool LowerLodName(string n)
        {
            if (n.Contains(GoF2Remake.Visuals.AutoLods.Marker)) return true;   // the remake's own levels ("<name>_lod_gen1")
            for (int at = n.IndexOf("_lod_", StringComparison.Ordinal); at >= 0; at = n.IndexOf("_lod_", at + 1, StringComparison.Ordinal))
            {
                int i = at + 5, level = 0, digits = 0;
                for (; i < n.Length && n[i] >= '0' && n[i] <= '9'; i++, digits++) level = level * 10 + (n[i] - '0');
                if (digits > 0 && level > 0) return true;
            }
            return false;
        }

        internal static void Forget(Collider c) => bodies.Remove(c);

        /// <summary>No hull for 'target' any more (its colliders gone): what the original never hits (a fighter's turret,
        /// radius 0).</summary>
        public static void Detach(Target target)
        {
            if (target != null) pending.RemoveAll(p => p.target == target);
            var b = target != null ? target.hull : null;
            if (b == null) return;
            foreach (var c in b.colliders) if (c != null) { bodies.Remove(c); UnityEngine.Object.Destroy(c.gameObject); }
            b.colliders.Clear();
            target.hull = null;
            if (b.obstacle != null && b.obstacle.hull == b) b.obstacle.hull = null;
        }

        public static HullBody BodyOf(Collider c) => c != null && bodies.TryGetValue(c, out var b) ? b : null;

        /// <summary>The part this collider shapes can be seen: its renderer (on the collider's parent, the mesh's object)
        /// enabled, not scaled to nothing, not faded out by PartAnimation's `extra` channel (_Fade, or the alpha / colour of
        /// _Color / _BaseColor through the renderer's property block). Inactive parts have their colliders off already.</summary>
        public static bool Shown(Collider c)
        {
            if (c == null || !c.enabled) return false;
            var t = c.transform.parent;
            var r = t != null ? t.GetComponent<Renderer>() : null;
            if (r == null || !r.enabled) return false;
            var sc = t.lossyScale;
            if (Mathf.Abs(sc.x) < 1e-4f || Mathf.Abs(sc.y) < 1e-4f || Mathf.Abs(sc.z) < 1e-4f) return false;
            if (!r.HasPropertyBlock()) return true;
            r.GetPropertyBlock(shownBlock);
            if (shownBlock.HasFloat(FadeId) && shownBlock.GetFloat(FadeId) < 0.01f) return false;
            if (shownBlock.HasColor(ColorId) && Faded(shownBlock.GetColor(ColorId))) return false;
            if (shownBlock.HasColor(BaseColorId) && Faded(shownBlock.GetColor(BaseColorId))) return false;
            return true;
        }

        static bool Faded(Color c) => c.a < 0.01f || c.maxColorComponent < 0.01f;

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
                if (b == null || b.target == null || !b.Collides || !valid(b.target) || !Shown(hits[i].collider)) continue;
                best = hits[i].distance;
                target = b.target;
                point = hits[i].distance > 0f ? hits[i].point : from;
            }
            return target != null;
        }

        /// <summary>A sphere of 'radius' swept from 'from' to 'to': the distance to the first shown hull 'valid' accepts that
        /// it runs into on the way (not one it starts inside: the penetration push takes those); false = none.</summary>
        public static bool Sweep(Vector3 from, Vector3 to, float radius, Func<HullBody, bool> valid, out float distance)
        {
            distance = 0f;
            var d = to - from;
            float len = d.magnitude;
            if (len < 1e-4f) return false;
            Sync();
            int n = Physics.SphereCastNonAlloc(from, radius, d / len, hits, len, HullMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (hits[i].distance <= 0f || hits[i].distance >= best) continue;
                var b = BodyOf(hits[i].collider);
                if (b == null || !b.Collides || !valid(b) || !Shown(hits[i].collider)) continue;
                best = hits[i].distance;
            }
            if (best == float.MaxValue) return false;
            distance = best;
            return true;
        }

        /// <summary>Remake (Flight.LineOfFire): a ray from 'from' along 'dir' (unit) meets 'body''s shown hull within 'reach'
        /// metres, past the colliders under 'ignore' (a turret's own meshes on its host's hull). The hull is built whatever the
        /// ship collision mode takes for shots (Mixed leaves fighters' out of the shot tests, not out of this). Modified (review
        /// of #168): 'body''s own colliders are asked one by one (Collider.Raycast), not the scene's shared hit buffer, whose
        /// 64 unsorted hits could all be other hulls in a crowded orbit.</summary>
        public static bool RayHits(Vector3 from, Vector3 dir, float reach, HullBody body, Transform ignore)
        {
            if (body == null || !body.Active) return false;
            Sync();
            var ray = new Ray(from, dir);
            foreach (var c in body.colliders)
            {
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                if (ignore != null && c.transform.IsChildOf(ignore)) continue;
                if (!c.Raycast(ray, out var hit, reach) || hit.distance <= 0.05f || !Shown(c)) continue;
                return true;
            }
            return false;
        }

        /// <summary>Modified (the docked free look's camera, FreeLookCamera.ClampCamera): a sphere of 'radius' metres swept from
        /// 'from' along 'dir' (unit) up to 'reach': the distance to the first shown hull it runs into, not a flyable ship's
        /// (fighters passing by don't push the camera) nor one under 'ignore' (the player's own ship), whatever the ship
        /// collision mode takes for the player; float.MaxValue = none.</summary>
        public static float CameraHit(Vector3 from, Vector3 dir, float reach, float radius, Transform ignore)
        {
            if (bodies.Count == 0 || reach <= 0f) return float.MaxValue;
            Sync();
            int n = Physics.SphereCastNonAlloc(from, radius, dir, hits, reach, HullMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (hits[i].distance <= 0f || hits[i].distance >= best) continue;
                var c = hits[i].collider;
                var b = BodyOf(c);
                if (b == null || !b.Active || b.flyableShip || (ignore != null && c.transform.IsChildOf(ignore)) || !Shown(c)) continue;
                best = hits[i].distance;
            }
            return best;
        }

        /// <summary>The hull colliders within 'radius' of 'centre' into 'buffer'; their count.</summary>
        public static int Overlap(Vector3 centre, float radius, Collider[] buffer)
        {
            Sync();
            return Physics.OverlapSphereNonAlloc(centre, radius, buffer, HullMask, QueryTriggerInteraction.Ignore);
        }
    }
}
