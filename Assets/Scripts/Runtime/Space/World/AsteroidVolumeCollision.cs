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
        readonly List<AsteroidHullContact.Box> worldBoxes = new List<AsteroidHullContact.Box>(8);
        readonly List<(Vector3 centre, float radius)> worldSpheres = new List<(Vector3, float)>();
        Vector3 lastPosition;
        bool hasLastPosition;
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
            if (owner == null || !owner.Alive || obstacle == null || !obstacle.Active)
            { hasLastPosition = false; return; }
            Vector3 position = transform.position;
            Vector3 movement = hasLastPosition ? position - lastPosition : Vector3.zero;
            if (movement.sqrMagnitude > 300f * 300f) movement = Vector3.zero;
            lastPosition = position;
            hasLastPosition = true;

            // Asteroids are stationary in normal flight. Refresh the broadphase candidate set every
            // 30 frames; the per-frame work is then limited to nearby rocks and actual volume tests.
            if (Time.frameCount - refreshFrame >= 30 ||
                (transform.position - refreshPosition).sqrMagnitude >=
                    MaxTravelBeforeRefresh * MaxTravelBeforeRefresh)
            {
                RefreshCandidates(movement);
                refreshFrame = Time.frameCount;
                refreshPosition = transform.position;
            }

            if (nearby.Count == 0) return;
            BuildWorldVolumes(position);
            for (int i = nearby.Count - 1; i >= 0; i--)
            {
                var asteroid = nearby[i];
                if (asteroid == null || !asteroid.isActiveAndEnabled || !asteroid.Alive || !asteroid.isAsteroid)
                {
                    nearby.RemoveAt(i);
                    continue;
                }

                if (!Overlaps(asteroid, movement)) continue;

                // Match the native player/asteroid path: normal asteroid death, mining loot and effects.
                // NPC capital ships do not receive player collision damage and are never pushed out.
                asteroid.Damage(9999f);
                if (!asteroid.Alive) nearby.RemoveAt(i);
            }
        }

        void RefreshCandidates(Vector3 movement)
        {
            nearby.Clear();
            AsteroidTargetGrid.Query(transform.position - movement * 0.5f,
                reach + movement.magnitude * 0.5f, nearby);
        }

        void BuildWorldVolumes(Vector3 position)
        {
            worldBoxes.Clear();
            worldSpheres.Clear();
            Quaternion rotation = obstacle.rotates
                ? transform.rotation * Quaternion.Inverse(obstacle.baseRotation) : Quaternion.identity;
            foreach (var volume in obstacle.volumes)
            {
                if (volume == null) continue;
                Vector3 centre = position + rotation * volume.centre;
                if (volume.sphere) worldSpheres.Add((centre, volume.radius));
                else worldBoxes.Add(new AsteroidHullContact.Box(centre, volume.half, rotation));
            }
        }

        bool Overlaps(Target asteroid, Vector3 movement)
        {
            foreach (var box in worldBoxes)
                if (AsteroidHullContact.Touches(box, asteroid, movement, out _)) return true;
            foreach (var sphere in worldSpheres)
                if (AsteroidHullContact.TouchesSphere(sphere.centre, sphere.radius, asteroid)) return true;
            return false;
        }
    }
}
