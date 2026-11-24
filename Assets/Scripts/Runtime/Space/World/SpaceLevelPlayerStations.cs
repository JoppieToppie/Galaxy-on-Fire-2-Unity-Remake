using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class SpaceLevel
    {
        bool playerStationDockPointsSpawned;
        int visiblePlayerStationState = -1;
        int visiblePlayerStationModel = -1;
        Crate stationSalvageCrate;
        IReadOnlyList<(int item, int amount)> renderedStationSalvage;
        Crate jumpgateSalvageCrate;
        IReadOnlyList<(int item, int amount)> renderedJumpgateSalvage;

        void AttachStationHull()
        {
            int orbit = Layout.stationIndex;
            bool combatStation = GameHooks.IsPlayerStation?.Invoke(orbit) == true;
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
            UpdateSalvageCrate(orbit, false, SpaceHooks.PlayerWreckSalvage?.Invoke(orbit, false),
                ref stationSalvageCrate, ref renderedStationSalvage);
            UpdateSalvageCrate(orbit, true, SpaceHooks.PlayerWreckSalvage?.Invoke(orbit, true),
                ref jumpgateSalvageCrate, ref renderedJumpgateSalvage);
        }

        void UpdateSalvageCrate(int orbit, bool jumpgate, IReadOnlyList<(int item, int amount)> manifest,
            ref Crate crate, ref IReadOnlyList<(int item, int amount)> rendered)
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
                    if (item.item == part.item && item.amount == part.amount) continue;
                    same = false;
                    break;
                }
            }
            rendered = manifest;
            if (same) return;

            var loot = new List<ItemStack>();
            foreach (var material in manifest)
                if (material.amount > 0) loot.Add(new ItemStack(material.item, material.amount));
            if (loot.Count == 0) return;
            if (crate == null)
            {
                var anchor = SpaceHooks.PlayerWreckSalvagePosition?.Invoke(orbit, jumpgate) ?? Vector3.zero;
                crate = Crate.Spawn(anchor, loot, Layout.raceId, Crate.LookContainer);
                crate.keep = true;
                crate.remote = true;
                crate.CollectStationSalvage = room => SpaceHooks.CollectPlayerWreckSalvage?.Invoke(orbit, jumpgate, room);
            }
            else
            {
                crate.loot.Clear();
                crate.loot.AddRange(loot);
            }
        }

        readonly Dictionary<int, GameObject> orbitalStructureModels = new Dictionary<int, GameObject>();
        string renderedOrbitalSnapshot;
        float retryOrbitalModelsAt;
        bool orbitalModelsInitialized;

        void UpdateOrbitalStructures(int orbit)
        {
            string snapshot = SpaceHooks.PlayerInfrastructureSnapshot?.Invoke();
            if (orbitalModelsInitialized && ReferenceEquals(renderedOrbitalSnapshot, snapshot) &&
                (retryOrbitalModelsAt == 0f || Time.unscaledTime < retryOrbitalModelsAt)) return;
            retryOrbitalModelsAt = 0f;
            orbitalModelsInitialized = true;
            var structures = SpaceHooks.PlayerOrbitalModels?.Invoke(db, orbit);
            var activeIds = new HashSet<int>();
            if (structures != null)
                foreach (var structure in structures) activeIds.Add(structure.id);
            renderedOrbitalSnapshot = snapshot;
            var removed = new List<int>();
            foreach (var pair in orbitalStructureModels)
                if (!activeIds.Contains(pair.Key))
                {
                    if (pair.Value != null)
                    {
                        Explosion.Spawn(pair.Value.transform.position, 3f);
                        pair.Value.SetActive(false);
                        Destroy(pair.Value);
                    }
                    removed.Add(pair.Key);
                }
            foreach (int id in removed) orbitalStructureModels.Remove(id);
            if (structures == null) return;
            foreach (var structure in structures)
            {
                if (orbitalStructureModels.ContainsKey(structure.id)) continue;
                if (structure.assembly == null)
                {
                    retryOrbitalModelsAt = Time.unscaledTime + 2f;
                    continue;
                }
                var model = OrbitBuilder.Spawn(db, structure.assembly, Vector3.zero, Quaternion.identity, structure.name, transform);
                if (model == null)
                {
                    retryOrbitalModelsAt = Time.unscaledTime + 2f;
                    continue;
                }
                model.transform.position = structure.position;
                model.transform.localScale *= structure.scale;
                var obstacle = model.AddComponent<Obstacle>();
                obstacle.landmark = obstacle.cubeIsContact = true;
                obstacle.cubeHalf = structure.radius;
                obstacle.volumes.Add(CollisionVolume.Sphere(Vector3.zero, structure.radius));
                var hull = HullCollision.Attach(model, null, obstacle, forceExact: true);
                if (hull != null) hull.orbitalStructureId = structure.id;
                orbitalStructureModels.Add(structure.id, model);
            }
        }

        void UpdatePlayerStationLifecycle()
        {
            if (Layout == null || Navigation == null) return;
            int orbit = Layout.stationIndex;
            UpdateOrbitalStructures(orbit);
            UpdateWreckSalvage(orbit);
            if (GameHooks.IsPlayerStation?.Invoke(orbit) != true) return;
            bool modelPresent = GameHooks.StationSceneryPresent?.Invoke(orbit) == true;
            bool operational = modelPresent && GameHooks.StationClosedByCombat?.Invoke(orbit) != true;
            int state = operational ? 2 : modelPresent ? 1 : 0;
            bool moved = modelPresent && Station != null &&
                Station.transform.position != SpaceHooks.StationPosition(orbit);
            int model = GameHooks.PlayerStationModel?.Invoke(orbit) ?? -1;
            bool modelChanged = visiblePlayerStationState >= 0 && visiblePlayerStationModel != model;
            if (modelChanged && DockedInOrbit && visiblePlayerStationState == state && !moved) return;
            if (visiblePlayerStationState == state && !moved && !modelChanged) return;
            visiblePlayerStationState = state;
            visiblePlayerStationModel = model;
            Layout.stationObject = modelPresent;
            Layout.hasStation = operational;

            if ((!modelPresent || moved || modelChanged) && Station != null)
            {
                if (Es2 != null && Es2 != ActiveEs2)
                {
                    Destroy(Es2);
                    Es2 = null;
                }
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
