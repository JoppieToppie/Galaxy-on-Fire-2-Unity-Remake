// Obstacle.cs
// Something with bounding volumes (CollisionVolume) that the player slides along and NPC fighters steer out of:
// the station, the visible jumpgate and freighters (their wreck after the death animation). Fighters and the player
// have none: the player flies through fighters, and fighters through each other (ship_combat.md 2.9,
// npc_traffic_ai.md 5.7). Asteroids are handled separately (touching one destroys it, PlayerCollision).
//   PlayerStation::outerCollide 0x147f40       contact = inside the cube +-(bounding radius + 5000 units) around the
//                                              station and inside one of its volumes
//   PlayerStaticFar::collide 0x141498          jumpgate: contact = inside the cube +-radius around the gate; the push
//                                              only moves the player out of the sphere (in the cube's corners the
//                                              camera shakes without a push, like the original)
//   PlayerFixedObject::outerCollide 0x18017a   freighters: inside one of the boxes (the wreck boxes once dead)
//   *::getProjectionVector                     NPC avoidance direction: away from the touched volume's centre
//                                              (stations, gates) or from the ship's position (freighters)

using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class Obstacle : MonoBehaviour
    {
        public static readonly List<Obstacle> All = new List<Obstacle>();

        /// <summary>Level::getLandmarks (station, jumpgate): checked before ships.</summary>
        public bool landmark;
        /// <summary>PlayerStation: touching it with the autopilot to the station docks (MGame::dockEvent).</summary>
        public bool isStation;
        /// <summary>The jumpgate (PlayerStaticFar): contact is the cube +-cubeHalf itself.</summary>
        public bool cubeIsContact;
        /// <summary>Metres: the station's prefilter cube / the jumpgate's contact cube; 0 = none.</summary>
        public float cubeHalf;
        /// <summary>The avoidance direction starts at the touched volume's centre (else at the owner's position).</summary>
        public bool projectFromVolume = true;
        public List<CollisionVolume> volumes = new List<CollisionVolume>();
        /// <summary>Remake: the player slides along this hull's MeshColliders instead of the volumes (HullCollision); NPC
        /// fighters still steer by the volumes. Null = none.</summary>
        [System.NonSerialized] public HullBody hull;
        /// <summary>The player collides with the hull, not the volumes.</summary>
        public bool UsesHull => hull != null && hull.Collides;   // not taken by the ship collision mode (Mixed: another player's ship; Original: any): its volumes

        public bool Active => isActiveAndEnabled && volumes != null && volumes.Count > 0;

        // Remake (performance, 2026-10-22): the NPC fighters' avoidance (NpcShip.Avoid) tested every obstacle's volumes from
        // every fighter twice a frame; a sphere around them, measured once a frame, rules out the far ones first.
        int broadFrame = -1;
        Vector3 broadCentre;
        float broadReach;
        bool broadActive;

        /// <summary>'world' may touch the volumes (or the contact cube): within their bounding sphere and the obstacle active.
        /// Measured once per 'frame'.</summary>
        public bool MayTouch(Vector3 world, int frame)
        {
            if (broadFrame != frame)
            {
                broadFrame = frame;
                broadActive = Active;
                broadCentre = transform.position;
                float reach = 0f;
                if (cubeHalf > 0f) reach = cubeHalf * 1.7321f;   // Touches is false outside the cube
                else if (volumes != null)
                    foreach (var v in volumes) reach = Mathf.Max(reach, v.centre.magnitude + (v.sphere ? v.radius : v.half.magnitude));
                broadReach = reach + 50f;   // what the obstacle can move in the rest of the frame, generously
            }
            return broadActive && (world - broadCentre).sqrMagnitude <= broadReach * broadReach;
        }

        /// <summary>Remake (a fleet battle's capital ships turn, CapitalShip): the volumes were made for 'baseRotation' and
        /// turn with the owner from there (the original's are never rotated).</summary>
        public bool rotates;
        public Quaternion baseRotation = Quaternion.identity;

        /// <summary>'world' in the volumes' frame (relative to the owner, turned back to the base rotation).</summary>
        Vector3 Local(Vector3 world)
        {
            var p = world - transform.position;
            return rotates ? baseRotation * (Quaternion.Inverse(transform.rotation) * p) : p;
        }

        Vector3 ToWorld(Vector3 local) => transform.position + (rotates ? transform.rotation * (Quaternion.Inverse(baseRotation) * local) : local);

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>outerCollide: 'world' touches this object; 'index' = the volume touched (-1 = only the gate cube).</summary>
        public bool Touches(Vector3 world, out int index)
        {
            index = -1;
            var p = Local(world);
            if (cubeHalf > 0f && (Mathf.Abs(p.x) >= cubeHalf || Mathf.Abs(p.y) >= cubeHalf || Mathf.Abs(p.z) >= cubeHalf)) return false;
            for (int i = 0; i < volumes.Count; i++)
                if (volumes[i].Contains(p)) { index = i; return true; }
            return cubeIsContact;
        }

        /// <summary>projectCollisionOnSurface: 'world' moved out of every volume containing it.</summary>
        public Vector3 PushOut(Vector3 world) => ToWorld(CollisionVolume.ProjectOut(Local(world), volumes));

        /// <summary>Remake (Flight.LineOfFire, without hull meshes: the Original ship collision): a ray from 'from' along 'dir'
        /// (unit) runs into these volumes: it enters one it isn't in, sampled along its way, or it heads inward through the face
        /// of the one it starts in nearest its start (a turret on the hull firing into its own ship; along the face or out of it
        /// is free).</summary>
        public bool RayInto(Vector3 from, Vector3 dir)
        {
            if (!Active) return false;
            var p0 = Local(from);
            var d = rotates ? baseRotation * (Quaternion.Inverse(transform.rotation) * dir) : dir;
            float reach = 0f;
            foreach (var v in volumes) reach = Mathf.Max(reach, (v.centre - p0).magnitude + (v.sphere ? v.radius : v.half.magnitude));
            int start = -1;
            for (int i = 0; i < volumes.Count; i++)
                if (volumes[i].Contains(p0)) { start = i; if (Vector3.Dot(d, OutwardNormal(volumes[i], p0)) < -0.1f) return true; }
            const int Samples = 24;
            for (int k = 1; k <= Samples; k++)
            {
                var p = p0 + d * (reach * k / Samples);
                for (int i = 0; i < volumes.Count; i++)
                    if (i != start && volumes[i].Contains(p) && !volumes[i].Contains(p0)) return true;
            }
            return false;
        }

        /// <summary>Remake (the cruise drive's look ahead): how far along the ray from 'from' (world) along 'dir' (unit) the
        /// first of these volumes is entered, in metres; 0 from inside one, float.MaxValue when none lies on the ray ahead or
        /// the obstacle is inactive.</summary>
        public float RayDistance(Vector3 from, Vector3 dir)
        {
            if (!Active) return float.MaxValue;
            var p0 = Local(from);
            var d = rotates ? baseRotation * (Quaternion.Inverse(transform.rotation) * dir) : dir;
            float best = float.MaxValue;
            for (int i = 0; i < volumes.Count; i++)
            {
                float t = volumes[i].RayEntry(p0, d);
                if (t < best) best = t;
            }
            return best;
        }

        /// <summary>The outward normal of the face of 'v' nearest 'p' (a point in it), a sphere's radial direction.</summary>
        static Vector3 OutwardNormal(CollisionVolume v, Vector3 p)
        {
            var q = p - v.centre;
            if (v.sphere) return q.sqrMagnitude > 1e-8f ? q.normalized : Vector3.up;
            var f = new Vector3(v.half.x > 0f ? q.x / v.half.x : 0f, v.half.y > 0f ? q.y / v.half.y : 0f, v.half.z > 0f ? q.z / v.half.z : 0f);
            float ax = Mathf.Abs(f.x), ay = Mathf.Abs(f.y), az = Mathf.Abs(f.z);
            if (ax >= ay && ax >= az) return new Vector3(Mathf.Sign(f.x), 0f, 0f);
            if (ay >= az) return new Vector3(0f, Mathf.Sign(f.y), 0f);
            return new Vector3(0f, 0f, Mathf.Sign(f.z));
        }

        /// <summary>getProjectionVector: the unit vector away from the touched volume (or the owner).</summary>
        public Vector3 ProjectionVector(Vector3 world, int index)
        {
            var from = projectFromVolume && index >= 0 && index < volumes.Count ? ToWorld(volumes[index].centre) : transform.position;
            var d = world - from;
            return d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.zero;
        }

        /// <summary>Editor aid: the volumes as wire boxes / spheres.</summary>
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
            if (volumes == null) return;
            // Remake: an object with its real hull (HullCollision) shows that (its MeshColliders) instead; the volumes still
            // steer the NPC fighters round it, but the player and the shots no longer touch them.
            if (GetComponentInChildren<HullBody>(true) != null) return;
            foreach (var v in volumes)
            {
                if (v.sphere) Gizmos.DrawWireSphere(transform.position + v.centre, v.radius);
                else Gizmos.DrawWireCube(transform.position + v.centre, v.half * 2f);
            }
        }
    }
}
