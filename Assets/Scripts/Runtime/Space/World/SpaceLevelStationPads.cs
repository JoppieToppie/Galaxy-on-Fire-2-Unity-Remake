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
}
