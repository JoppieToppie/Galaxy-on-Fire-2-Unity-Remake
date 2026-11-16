// SpaceLevelStationPads.cs
// SpaceLevel: a station's docking pads outside (part of the partial SpaceLevel; its fields are in SpaceLevel.cs). Remake mods
// (a modder's suggestion: dockable arms like the Luur mission's): stations.json "orbit.pads" become one docking target, a
// port without a model of its own placed on the station (its frame is the orbit's: the pads are orbit game coordinates),
// docked at like the story's objects (ObjectDocking.StationPad: the nearest free pad, no transfer). Docked, the events' and
// quests' "on pad" fires (SpaceHooks.PadDocked). Every game builds its own (NpcShip.LocalOnly: never a proxy), nobody's
// target (SpawnSpec.stationPort, Target.untargetable), an event's Restrict Travel "docking" refuses it (ObjectDocking.Refusal).

using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class SpaceLevel
    {
        /// <summary>The fixed object name of a station's port: no assembly of that name, so it has no model.</summary>
        public const string StationPortObject = "station_pads";

        // Large-ship external docking is deliberately separate from NPC station pads.
        readonly System.Collections.Generic.List<Transform> externalDockPoints = new System.Collections.Generic.List<Transform>();

        GoF2Remake.Data.ShipData PlayerShipData => db != null ? db.Ship(Session.ShipIndex) : null;

        /// <summary>The ship's class / role allows external docking, regardless of legacy hull-size or ship-index guesses.</summary>
        bool ExternalDockClass => Modding.ShipClassRules.CanDockExternally(PlayerShipData);

        /// <summary>Internal docking remains available to fighter / interceptor / bomber roles and legacy small hulls.</summary>
        bool InternalDockEligible => Player != null && PlayerHull.PlayerShip
                                     && Modding.ShipClassRules.CanDockInternally(PlayerShipData, PlayerHull.Big);

        /// <summary>External docking requires both the designated large-ship class role and an authored slotType 8 hardpoint.</summary>
        bool ExternalDockEligible
        {
            get
            {
                var mounts = db != null ? db.MountsOf(Session.ShipIndex, 8) : null;
                return Player != null && ExternalDockClass && mounts != null && mounts.Count > 0;
            }
        }

        bool ExternalDockInRange => Station != null && StationShown && Player != null && Player.transform.position.sqrMagnitude < DockMeters * DockMeters;
        bool CanDockExternally => ExternalDockEligible && externalDockPoints.Count > 0;

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

        /// <summary>Build the manually authored station-side attachment transforms; never derived from orbit.pads.</summary>
        void SpawnExternalDockPoints(int station)
        {
            externalDockPoints.Clear();
            if (Station == null) return;
            var specs = Modding.ModWorld.ExternalDockPoints(station);
            if (specs != null && specs.Count > 0)
            {
                // The Mod Editor places these in the station root's orbit frame (the same frame as orbit.pads).
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
            if (Modding.ModBuilderPlay.ExternalDockTestQueuedFor(station))
            {
                var starter = gameObject.AddComponent<ExternalDockTestStarter>();
                starter.Setup(this, station);
            }
        }

        /// <summary>Begin an exterior docking approach using the nearest station point and the ship's first docking hardpoint.</summary>
        bool TryBeginExternalDocking()
        {
            if (!ExternalDockEligible || !leftDockRange || !ExternalDockInRange || externalDockPoints.Count == 0 || Es2Busy || DockedInOrbit || Leaving)
                return false;
            if (!FindExternalDockingPair(null, out var point, out var mount)) return false;
            var dock = Es2Dock.AttachExternal(Station, Session.StationIndex, Player, chase, point, mount);
            if (dock == null) return false;
            Navigation?.SetAutopilot(null);
            ActiveEs2 = dock;
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
                Debug.LogError("Mod Builder: the selected ship needs a frigate, cruiser or carrier class and at least one external docking hardpoint.");
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
            dock.HoldExternalDocked();
            DockInOrbit();
        }

        /// <summary>The port of 'station''s pads (stations.json "orbit.pads"), none without pads.</summary>
        void SpawnStationPads(int station)
        {
            if (Traffic == null || Modding.ModWorld.StationPads(station).Count == 0) return;
            var st = db.Stations.Find(s => s.index == station);
            int race = db.Systems.Find(s => s.index == Layout.systemIndex)?.raceId ?? 0;
            var port = Traffic.SpawnShip(new SpawnSpec
            {
                group = NpcGroup.Special, race = race, ship = 15, fixedObject = StationPortObject, stationPort = true,
                position = Vector3.zero, rotation = new Vector3(0f, Mathf.PI, 0f),   // the station's own pose (OrbitBuilder.SpawnStation)
                stationary = true, alwaysFriend = true, noLoot = true, hitpoints = 9999999, hitRadius = 1f,
                dockingType = ObjectDocking.StationPad, spacePoints = SpacePoints.StationPadBase + station,
                name = string.Format(Localization.Extra("stationPadsName", "{0}: docking pads"), st?.name ?? ""),
            });
            if (port == null) return;
            port.LocalOnly = true;
            port.Target.untargetable = true;
        }
    }

    /// <summary>Runs after SpaceLevel.Awake has built the player, station and navigation systems.</summary>
    sealed class ExternalDockTestStarter : MonoBehaviour
    {
        SpaceLevel level;
        int station;
        public void Setup(SpaceLevel owner, int stationIndex) { level = owner; station = stationIndex; }
        void Start()
        {
            if (level != null && Modding.ModBuilderPlay.TakeExternalDockTest(station, out int point))
                level.StartExternalDockTest(point);
        }
    }
}