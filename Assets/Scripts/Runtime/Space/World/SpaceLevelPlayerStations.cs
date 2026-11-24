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
        int visiblePlayerStationModel = -1;
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

        readonly Dictionary<int, GameObject> orbitalStructureModels = new Dictionary<int, GameObject>();
        string renderedOrbitalSnapshot;
        float retryOrbitalModelsAt;
        IReadOnlyList<PlayerOrbitalStructure> renderedOrbitalStructures;

        void UpdateOrbitalStructures(int orbit)
        {
            var structures = NetPlayerStations.OrbitalStructures;
            string snapshot = NetState.Instance?.PlayerStations;
            if (ReferenceEquals(renderedOrbitalStructures, structures) && ReferenceEquals(renderedOrbitalSnapshot, snapshot) &&
                (retryOrbitalModelsAt == 0f || Time.unscaledTime < retryOrbitalModelsAt)) return;
            retryOrbitalModelsAt = 0f;
            renderedOrbitalStructures = structures;
            renderedOrbitalSnapshot = snapshot;
            var removed = new List<int>();
            foreach (var pair in orbitalStructureModels)
                if (NetPlayerStations.FindOrbitalStructure(pair.Key)?.operational != true)
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
                if (structure == null || structure.orbit != orbit || !structure.operational ||
                    orbitalStructureModels.ContainsKey(structure.id)) continue;
                string assembly = NetPlayerStations.OrbitalStructureAssembly(structure.assembly, structure.stationModel, db);
                if (assembly == null)
                {
                    if (!string.IsNullOrEmpty(structure.stationModel)) retryOrbitalModelsAt = Time.unscaledTime + 2f;
                    continue;
                }
                var model = OrbitBuilder.Spawn(db, assembly, Vector3.zero, Quaternion.identity, structure.name, transform);
                if (model == null)
                {
                    retryOrbitalModelsAt = Time.unscaledTime + 2f;
                    continue;
                }
                model.transform.position = NetPlayerStations.OrbitalStructurePosition(structure);
                model.transform.localScale *= structure.modelScale;
                var obstacle = model.AddComponent<Obstacle>();
                obstacle.landmark = obstacle.cubeIsContact = true;
                obstacle.cubeHalf = structure.hitRadius;
                obstacle.volumes.Add(CollisionVolume.Sphere(Vector3.zero, structure.hitRadius));
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
            if (!NetPlayerStations.IsPlayerStation(orbit)) return;
            bool modelPresent = NetPlayerStations.StationSceneryPresent(orbit);
            bool operational = modelPresent && !NetPlayerStations.StationClosedByCombat(orbit);
            int state = operational ? 2 : modelPresent ? 1 : 0;
            bool moved = modelPresent && Station != null &&
                Station.transform.position != NetPlayerStations.StationPosition(orbit);
            int model = NetPlayerStations.LooksLike(orbit);
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
