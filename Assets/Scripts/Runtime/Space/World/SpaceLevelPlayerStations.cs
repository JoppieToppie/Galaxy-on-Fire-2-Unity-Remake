using GoF2Remake.Flight;
using GoF2Remake.Multiplayer;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class SpaceLevel
    {
        bool playerStationDockPointsSpawned;
        int visiblePlayerStationState = -1;

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

        void UpdatePlayerStationLifecycle()
        {
            if (Layout == null || Navigation == null || !NetPlayerStations.IsPlayerStation(Layout.stationIndex)) return;
            int orbit = Layout.stationIndex;
            bool modelPresent = NetPlayerStations.StationSceneryPresent(orbit);
            bool operational = modelPresent && !NetPlayerStations.StationClosedByCombat(orbit);
            int state = operational ? 2 : modelPresent ? 1 : 0;
            if (visiblePlayerStationState == state) return;
            visiblePlayerStationState = state;
            Layout.stationObject = modelPresent;
            Layout.hasStation = operational;

            if (!modelPresent && Station != null)
            {
                Station.SetActive(false);
                Destroy(Station);
                Station = null;
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
