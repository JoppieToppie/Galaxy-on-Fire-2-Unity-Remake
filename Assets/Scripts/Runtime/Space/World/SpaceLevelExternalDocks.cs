// SpaceLevelExternalDocks.cs
// Large ships attach to manually authored station points through ship slotType 8 hardpoints.
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class SpaceLevel
    {
        // Ship exterior docking points are loaded from stations.json orbit.externalDocks.
        readonly System.Collections.Generic.List<Transform> externalDockPoints = new System.Collections.Generic.List<Transform>();

        GoF2Remake.Data.ShipData PlayerShipData => db != null ? db.Ship(Session.ShipIndex) : null;

        /// <summary>The ship's class / role allows external docking, regardless of legacy hull-size or ship-index guesses.</summary>
        bool ExternalDockClass => Modding.ExternalDockingRules.CanDockExternally(PlayerShipData);

        /// <summary>Hangar docking: every ship the player can own but an external-role one, big mod hulls included (Discord
        /// 2026-11-21: the UNSC Savannah could no longer land after a 150 m limit came in with external docking).</summary>
        bool InternalDockEligible => Player != null && PlayerHull.PlayerShip
                                     && Modding.ExternalDockingRules.CanDockInternally(PlayerShipData);

        /// <summary>External docking requires both the designated large-ship class role and an authored slotType 8 hardpoint.</summary>
        bool ExternalDockEligible
        {
            get
            {
                if (Player == null || db == null || !PlayerHull.PlayerShip || !ExternalDockClass) return false;
                var mounts = db.MountsOf(Session.ShipIndex, 8);   // (a new list: asked last, the HUD's dock check runs every frame)
                return mounts != null && mounts.Count > 0;
            }
        }

        bool ExternalDockInRange => Station != null && StationShown && Player != null &&
            ((Player.transform.position - Station.transform.position).sqrMagnitude < DockMeters * DockMeters ||
                plannedDockPoints.Exists(point => point != null && (point.position - Player.transform.position).sqrMagnitude < DockMeters * DockMeters));

        readonly System.Collections.Generic.List<Transform> plannedDockPoints = new System.Collections.Generic.List<Transform>();
        string dockArmSnapshot;
        void UpdatePlannedDockPoints(int orbit)
        {
            if (Station == null || DockedInOrbit || Es2Busy || SpaceHooks.PlayerDockArmPoses == null) return;
            string snapshot = SpaceHooks.PlayerInfrastructureSnapshot?.Invoke();
            if (ReferenceEquals(snapshot, dockArmSnapshot) && plannedDockPoints.TrueForAll(point => point != null && externalDockPoints.Contains(point))) return;
            dockArmSnapshot = snapshot;
            foreach (var point in plannedDockPoints)
            {
                externalDockPoints.Remove(point);
                if (point != null) Destroy(point.gameObject);
            }
            plannedDockPoints.Clear();
            foreach (var pose in SpaceHooks.PlayerDockArmPoses(orbit))
            {
                var point = new GameObject("Player station docking arm").transform;
                point.SetParent(Station.transform, false);
                point.SetPositionAndRotation(pose.position, pose.rotation);
                plannedDockPoints.Add(point);
                externalDockPoints.Add(point);
            }
        }
        bool CanDockExternally => ExternalDockEligible && externalDockPoints.Count > 0;

        /// <summary>The range used by the selected class's docking route at the orbit's fixed station.</summary>
        bool AtFixedStationDockRange => Layout.hasStation && StationShown && leftDockRange
                                        && (ExternalDockClass ? ExternalDockInRange : InDockRange);

        /// <summary>The autopilot uses the external station radius for exterior roles, not an interior ES2 hangar's dock zone.</summary>
        bool DockingApproachRange => ExternalDockClass
            ? ExternalDockInRange
            : InDockRange || (Es2 == null && Collision != null && Collision.TouchingStation);

        /// <summary>Choose the closest ship hardpoint / station attachment pair. A ship may author more than one slotType 8
        /// mount; pairing by the actual mount position avoids always selecting the first mount on an asymmetric hull.</summary>
        bool FindExternalDockingPair(Transform onlyPoint, out Transform point, out WeaponMount mount)
        {
            point = null;
            mount = null;
            if (Player == null || db == null) return false;
            var mounts = db.MountsOf(Session.ShipIndex, 8);
            if (mounts == null || mounts.Count == 0) return false;
            float nearest = float.MaxValue;
            foreach (var candidateMount in mounts)
            {
                if (candidateMount == null) continue;
                Vector3 mountPosition = Player.transform.TransformPoint(WeaponSystem.MountToLocal(candidateMount));
                if (onlyPoint != null)
                {
                    float distance = (onlyPoint.position - mountPosition).sqrMagnitude;
                    if (distance < nearest) { nearest = distance; point = onlyPoint; mount = candidateMount; }
                    continue;
                }
                foreach (var candidatePoint in externalDockPoints)
                {
                    if (candidatePoint == null) continue;
                    float distance = (candidatePoint.position - mountPosition).sqrMagnitude;
                    if (distance < nearest) { nearest = distance; point = candidatePoint; mount = candidateMount; }
                }
            }
            return point != null && mount != null;
        }

        /// <summary>Build the manually authored station-side attachment transforms.</summary>
        void SpawnExternalDockPoints(int station)
        {
            externalDockPoints.Clear();
            if (Station == null) return;
            var specs = Modding.ModWorld.ExternalDockPoints(station);
            if (specs != null && specs.Count > 0)
            {
                // These points are authored in the station root's orbit frame.
                var stationFrame = Station.transform;
                for (int i = 0; i < specs.Count; i++)
                {
                    var spec = specs[i];
                    var point = new GameObject($"External docking point {i + 1}").transform;
                    point.SetParent(stationFrame, false);
                    point.localPosition = new Vector3(-spec.position.x, spec.position.y, spec.position.z) * M;
                    point.localRotation = Quaternion.Euler(spec.rotation);
                    externalDockPoints.Add(point);
                }
            }
            if (SpaceHooks.ExternalDockTestQueuedFor(station))
            {
                var starter = gameObject.AddComponent<ExternalDockTestStarter>();
                starter.Setup(this, station);
            }
        }

        /// <summary>Begin an exterior docking approach using the nearest station-point / ship-hardpoint pair.</summary>
        bool TryBeginExternalDocking()
        {
            if (!ExternalDockEligible || !leftDockRange || !ExternalDockInRange || externalDockPoints.Count == 0 || Es2Busy || DockedInOrbit || Leaving || DockingBlocked)
                return false;
            if (!FindExternalDockingPair(null, out var point, out var mount)) return false;
            var dock = Es2Dock.AttachExternal(Station, Session.StationIndex, Player, chase, point, mount);
            if (dock == null) return false;
            Navigation?.SetAutopilot(null);
            ActiveEs2 = dock;
            dock.TookOff -= OnTookOff;
            dock.TookOff += OnTookOff;
            dock.BeginExternalDocking(DockInOrbit);
            return true;
        }

        /// <summary>Begin the Mod Editor's immediate external docking spawn after the flight scene is fully initialized.</summary>
        public void StartExternalDockTest(int pointIndex)
        {
            if (pointIndex < 0 || pointIndex >= externalDockPoints.Count || Player == null)
            {
                Debug.LogError("Mod Builder: external docking test point is missing.");
                Modding.ModBuilder.EndPlayTest();
                return;
            }
            if (!ExternalDockEligible || !FindExternalDockingPair(externalDockPoints[pointIndex], out _, out var mount))
            {
                Debug.LogError("Mod Builder: the selected ship needs an external docking class or role and at least one external docking hardpoint.");
                Modding.ModBuilder.EndPlayTest();
                return;
            }
            var dock = Es2Dock.AttachExternal(Station, Session.StationIndex, Player, chase, externalDockPoints[pointIndex], mount);
            if (dock == null)
            {
                Debug.LogError("Mod Builder: couldn't attach the selected ship to the external docking point.");
                Modding.ModBuilder.EndPlayTest();
                return;
            }
            ActiveEs2 = dock;
            dock.TookOff -= OnTookOff;
            dock.TookOff += OnTookOff;
            dock.HoldExternalDocked();
            DockInOrbit();
        }

    }

    sealed class ExternalDockTestStarter : MonoBehaviour
    {
        SpaceLevel level;
        int station;
        public void Setup(SpaceLevel owner, int stationIndex) { level = owner; station = stationIndex; }
        void Start()
        {
            if (level != null && SpaceHooks.TakeDockTest(station, out int point))
                level.StartExternalDockTest(point);
        }
    }
}
