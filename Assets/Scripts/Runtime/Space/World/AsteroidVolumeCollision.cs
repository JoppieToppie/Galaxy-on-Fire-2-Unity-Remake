// AsteroidVolumeCollision.cs
// Native asteroid destruction for explicitly classified large NPC ships. The ship's existing
// Obstacle volumes are authoritative; asteroid targets use their native Target.Damage death path.
// Candidate discovery is staggered (30 frames) and queries the shared asteroid spatial grid.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using UnityEngine;

namespace GoF2Remake.World
{
    [DefaultExecutionOrder(60)]
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class AsteroidVolumeCollision : MonoBehaviour
    {
        // These are the canonical large-ship classes. Fighters and privateers are intentionally excluded.
        static readonly string[] EnabledClasses =
        {
            ShipClassRules.Frigate,
            ShipClassRules.Freighter,
            ShipClassRules.Cruiser,
            ShipClassRules.Carrier
        };

        readonly List<Target> nearby = new List<Target>();
        Target owner;
        Obstacle obstacle;
        float reach;
        int refreshFrame = -30;
        Vector3 refreshPosition;
        const float MaxTravelBeforeRefresh = 10f;

        public static void Attach(GameObject host, Database database, int shipIndex, Target target, Obstacle volumes)
        {
            if (host == null || database == null || target == null || volumes == null || !volumes.Active) return;
            var ship = database.Ship(shipIndex);
            // Unclassified ships remain untouched; only a mod-assigned canonical class opts in.
            if (!ShipClassRules.Matches(ship, EnabledClasses)) return;

            var collision = host.AddComponent<AsteroidVolumeCollision>();
            collision.owner = target;
            collision.obstacle = volumes;
            collision.MeasureReach();
        }

        void MeasureReach()
        {
            reach = 0f;
            if (obstacle == null || obstacle.volumes == null) return;
            foreach (var volume in obstacle.volumes)
            {
                if (volume == null) continue;
                reach = Mathf.Max(reach, volume.centre.magnitude +
                    (volume.sphere ? volume.radius : volume.half.magnitude));
            }
        }

        void LateUpdate()
        {
            if (owner == null || !owner.Alive || obstacle == null || !obstacle.Active) return;

            // Asteroids are stationary in normal flight. Refresh the broadphase candidate set every
            // 30 frames; the per-frame work is then limited to nearby rocks and actual volume tests.
            if (Time.frameCount - refreshFrame >= 30 ||
                (transform.position - refreshPosition).sqrMagnitude >=
                    MaxTravelBeforeRefresh * MaxTravelBeforeRefresh)
            {
                RefreshCandidates();
                refreshFrame = Time.frameCount;
                refreshPosition = transform.position;
            }

            for (int i = nearby.Count - 1; i >= 0; i--)
            {
                var asteroid = nearby[i];
                if (asteroid == null || !asteroid.isActiveAndEnabled || !asteroid.Alive || !asteroid.isAsteroid)
                {
                    nearby.RemoveAt(i);
                    continue;
                }

                if (!Overlaps(asteroid)) continue;

                // Match the native player/asteroid path: normal asteroid death, mining loot and effects.
                // NPC capital ships do not receive player collision damage and are never pushed out.
                asteroid.Damage(9999f);
                if (!asteroid.Alive) nearby.RemoveAt(i);
            }
        }

        void RefreshCandidates()
        {
            nearby.Clear();
            AsteroidTargetGrid.Query(transform.position, reach, nearby);
        }

        bool Overlaps(Target asteroid)
        {
            Vector3 worldDelta = asteroid.transform.position - transform.position;
            Quaternion worldToVolume = obstacle.rotates
                ? obstacle.baseRotation * Quaternion.Inverse(transform.rotation)
                : Quaternion.identity;
            Vector3 localCentre = worldToVolume * worldDelta;

            // The asteroid hit cube is world-axis aligned. Rotate its half extents into the
            // obstacle's volume frame to preserve an AABB-vs-volume overlap test for turning ships.
            float radius = Mathf.Max(0f, asteroid.radius);
            Vector3 axisX = worldToVolume * Vector3.right;
            Vector3 axisY = worldToVolume * Vector3.up;
            Vector3 axisZ = worldToVolume * Vector3.forward;
            Vector3 half = new Vector3(
                radius * (Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x)),
                radius * (Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y)),
                radius * (Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z)));

            var volumes = obstacle.volumes;
            for (int i = 0; i < volumes.Count; i++)
            {
                var volume = volumes[i];
                if (volume == null) continue;
                Vector3 delta = localCentre - volume.centre;
                if (volume.sphere)
                {
                    Vector3 closest = new Vector3(
                        Mathf.Clamp(delta.x, -half.x, half.x),
                        Mathf.Clamp(delta.y, -half.y, half.y),
                        Mathf.Clamp(delta.z, -half.z, half.z));
                    if ((delta - closest).sqrMagnitude <= volume.radius * volume.radius) return true;
                }
                else if (Mathf.Abs(delta.x) <= volume.half.x + half.x &&
                         Mathf.Abs(delta.y) <= volume.half.y + half.y &&
                         Mathf.Abs(delta.z) <= volume.half.z + half.z)
                    return true;
            }
            return false;
        }
    }
}
