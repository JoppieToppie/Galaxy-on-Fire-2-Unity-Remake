using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Multiplayer;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class SpaceLevel
    {
        bool playerStationDockPointsSpawned;
        int visiblePlayerStationState = -1;
        Crate stationSalvageCrate;
        IReadOnlyList<PlayerStationMaterial> renderedStationSalvage;
        Crate jumpgateSalvageCrate;
        IReadOnlyList<PlayerStationMaterial> renderedJumpgateSalvage;

        void AttachStationHull()
        {
            int orbit = Layout.stationIndex;
            bool combatStation = NetPlayerStations.IsPlayerStation(orbit);
            var hull = HullCollision.Attach(Station, null, Station.GetComponent<Obstacle>(), forceExact: combatStation);
            if (hull != null && combatStation) hull.playerStationOrbit = orbit;
        }

        void SpawnPlayerStationDockPoints(int orbit)
        {
            SpawnStationPads(orbit);
            SpawnExternalDockPoints(orbit);
            playerStationDockPointsSpawned = true;
        }

        void UpdateWreckSalvage(int orbit)
        {
            UpdateSalvageCrate(orbit, false, NetPlayerStations.StationSalvageAt(orbit),
                ref stationSalvageCrate, ref renderedStationSalvage);
            UpdateSalvageCrate(orbit, true, NetPlayerStations.JumpgateSalvageAt(orbit),
                ref jumpgateSalvageCrate, ref renderedJumpgateSalvage);
        }

        void UpdateSalvageCrate(int orbit, bool jumpgate, IReadOnlyList<PlayerStationMaterial> manifest,
            ref Crate crate, ref IReadOnlyList<PlayerStationMaterial> rendered)
        {
            if (ReferenceEquals(manifest, rendered) &&
                (crate != null || manifest == null)) return;
            if (manifest == null || manifest.Count == 0)
            {
                if (crate != null) Destroy(crate.gameObject);
                crate = null;
                rendered = manifest;
                return;
            }

            bool same = crate != null && crate.loot.Count == manifest.Count;
            if (same)
            {
                for (int i = 0; i < manifest.Count; i++)
                {
                    var part = manifest[i];
                    var item = crate.loot[i];
                    if (part != null && item.item == part.item && item.amount == part.required) continue;
                    same = false;
                    break;
                }
            }
            rendered = manifest;
            if (same) return;

            var loot = new List<ItemStack>();
            foreach (var material in manifest)
                if (material != null && material.required > 0) loot.Add(new ItemStack(material.item, material.required));
            if (loot.Count == 0) return;
            if (crate == null)
            {
                var anchor = jumpgate ? NetPlayerStations.JumpgateSalvagePosition(orbit) : NetPlayerStations.StationSalvagePosition(orbit);
                crate = Crate.Spawn(anchor, loot, Layout.raceId, Crate.LookContainer);
                crate.keep = true;
                crate.remote = true;
                crate.CollectStationSalvage = room => NetPlayer.Local?.RequestWreckSalvage(orbit, room, jumpgate);
            }
            else
            {
                crate.loot.Clear();
                crate.loot.AddRange(loot);
            }
        }

        void UpdatePlayerStationLifecycle()
        {
            if (Layout == null || Navigation == null) return;
            int orbit = Layout.stationIndex;
            UpdateWreckSalvage(orbit);
            if (!NetPlayerStations.IsPlayerStation(orbit)) return;
            bool modelPresent = NetPlayerStations.StationSceneryPresent(orbit);
            bool operational = modelPresent && !NetPlayerStations.StationClosedByCombat(orbit);
            int state = operational ? 2 : modelPresent ? 1 : 0;
            bool moved = modelPresent && Station != null &&
                Station.transform.position != NetPlayerStations.StationPosition(orbit);
            if (visiblePlayerStationState == state && !moved) return;
            visiblePlayerStationState = state;
            Layout.stationObject = modelPresent;
            Layout.hasStation = operational;

            if ((!modelPresent || moved) && Station != null)
            {
                Station.SetActive(false);
                Destroy(Station);
                Station = null;
                playerStationDockPointsSpawned = false;
            }
            if (modelPresent && Station == null)
            {
                var refreshed = OrbitLayout.Build(db, orbit);
                if (refreshed == null) return;
                Layout.stationReach = refreshed.stationReach;
                Station = OrbitBuilder.SpawnStation(db, Layout);
                if (Station != null)
                {
                    OrbitBuilder.AddObstacles(Layout, Station, null);
                    AttachStationHull();
                }
            }

            if (operational && Station != null)
            {
                if (Es2 == null) Es2 = Es2Dock.Attach(Station, orbit, Player, chase);
                if (!playerStationDockPointsSpawned) SpawnPlayerStationDockPoints(orbit);
            }
            var station = db.Stations.Find(value => value != null && value.index == orbit);
            Navigation.StationObject = Station;
            Navigation.RefreshPlayerStation(orbit, operational, station?.name);
        }
    }
}
