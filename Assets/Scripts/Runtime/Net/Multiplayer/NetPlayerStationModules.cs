using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetPlayerStations
    {
        static readonly string[] ModuleKinds = { "storage", "refinery", "factory", "hangar", "drydock", "dockarm" };

        static PlayerStationAddon AddStarterStorage(PlayerStationRecord station)
        {
            if (station.addons?.Exists(module => module?.kind == "storage") == true) return null;
            station.addons ??= new List<PlayerStationAddon>();
            var storage = new PlayerStationAddon { kind = "storage", level = 1 };
            station.addons.Add(storage);
            return storage;
        }

        static string ModuleName(string kind) => kind == "hangar" ? "Ship hangar" :
            kind == "drydock" ? "Dry dock" : kind == "dockarm" ? "Docking arms" : char.ToUpperInvariant(kind[0]) + kind.Substring(1);

        static string ModuleInstallationError(NetPlayer pilot, PlayerStationRecord station, string kind, out string account)
        {
            account = null;
            if (station == null) return "No player station with that id exists.";
            if (Array.IndexOf(ModuleKinds, kind) < 0) return "Choose storage, refinery, factory, hangar, drydock or dockarm.";
            if (!CanManageConstruction(pilot, station, out account))
                return "Dock here as the builder or authorised construction manager to install modules.";
            if (station.underConstruction) return "Finish station construction before installing modules.";
            if (station.moduleBuildCompletesUtc > 0) return "A station module is already being assembled.";
            if (!string.IsNullOrEmpty(station.moduleConstructionKind) && station.moduleConstructionKind != kind &&
                ModPlayerInfrastructure.Current.ModuleBuildSeconds(kind) > 0)
                return "Finish the active module project before starting a timed installation.";
            if ((kind == "factory" || kind == "refinery") && StorageCapacity(station) <= 0)
                return "Install storage first.";
            if (station.addons?.Exists(addon => addon != null && addon.kind == kind) == true)
                return ModuleName(kind) + " is already installed.";
            int reservedSlot = !string.IsNullOrEmpty(station.moduleConstructionKind) && station.moduleConstructionKind != kind ? 1 : 0;
            if ((station.addons?.Count ?? 0) + reservedSlot >= MaxAddonsPerStation)
                return "This station has reached its module limit.";
            return null;
        }

        internal static string StartModuleProject(NetPlayer pilot, int id, string kind)
        {
            var station = Find(id);
            kind = (kind ?? "").Trim().ToLowerInvariant();
            string error = ModuleInstallationError(pilot, station, kind, out _);
            if (error != null) return error;
            if (!string.IsNullOrEmpty(station.moduleConstructionKind) || station.buyOrders?.Count > 0)
                return "Finish the active module project before starting another.";
            station.moduleConstructionKind = kind;
            station.moduleConstructionMaterials = ConstructionCost(kind);
            if (Save()) return ModuleName(kind) + " project opened. Its materials can now be bought through construction orders.";
            station.moduleConstructionKind = null;
            station.moduleConstructionMaterials = null;
            return "The module project could not be saved.";
        }

        internal static string CancelModuleProject(NetPlayer pilot, int id)
        {
            var station = Find(id);
            if (string.IsNullOrEmpty(station?.moduleConstructionKind)) return "There is no active module project.";
            if (!CanManageConstruction(pilot, station, out _)) return "Dock here as the builder or authorised construction manager.";
            if (station.moduleBuildCompletesUtc > 0) return "This project is fully funded and already being assembled.";
            if (station.buyOrders?.Count > 0 || station.moduleConstructionMaterials?.Exists(material => material != null && material.delivered > 0) == true)
                return "Cancel outstanding orders first. A project with delivered materials can be finished with cargo or buy orders.";
            string kind = station.moduleConstructionKind;
            var materials = station.moduleConstructionMaterials;
            station.moduleConstructionKind = null;
            station.moduleConstructionMaterials = null;
            if (Save()) return ModuleName(kind) + " project cancelled.";
            station.moduleConstructionKind = kind;
            station.moduleConstructionMaterials = materials;
            return "The module project's cancellation could not be saved.";
        }

        static Action FundModuleConstruction(PlayerStationRecord station, string kind)
        {
            int seconds = ModPlayerInfrastructure.Current.ModuleBuildSeconds(kind);
            if (seconds == 0) return CompleteModuleConstruction(station, kind);
            string previousKind = station.moduleConstructionKind;
            long previousDeadline = station.moduleBuildCompletesUtc;
            station.moduleConstructionKind = kind;
            station.moduleBuildCompletesUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + seconds;
            return () =>
            {
                station.moduleConstructionKind = previousKind;
                station.moduleBuildCompletesUtc = previousDeadline;
            };
        }

        static void TickConstruction(long now)
        {
            var projects = registry;
            foreach (var station in projects.stations)
            {
                if (!ReferenceEquals(registry, projects)) return;
                if (station == null || station.destroyed || station.retired || station.wreckClaimOpen ||
                    station.offeredForClaimUtc > 0) continue;
                if (station.underConstruction && station.stationBuildCompletesUtc > 0 && station.stationBuildCompletesUtc <= now)
                {
                    var rollback = CompleteStationConstruction(station);
                    if (Save()) Apply(NetGame.Db);
                    else rollback();
                }
                else if (!station.underConstruction && station.moduleBuildCompletesUtc > 0 &&
                    station.moduleBuildCompletesUtc <= now && !string.IsNullOrEmpty(station.moduleConstructionKind))
                {
                    var rollback = CompleteModuleConstruction(station, station.moduleConstructionKind);
                    if (!Save()) rollback();
                }
            }
        }

        static Action CompleteModuleConstruction(PlayerStationRecord station, string kind)
        {
            long previousDeadline = station.moduleBuildCompletesUtc;
            station.moduleBuildCompletesUtc = 0;
            string previousKind = station.moduleConstructionKind;
            var previousMaterials = station.moduleConstructionMaterials;
            station.addons ??= new List<PlayerStationAddon>();
            var module = new PlayerStationAddon { kind = kind, level = 1 };
            station.addons.Add(module);
            if (kind == previousKind)
            {
                station.moduleConstructionKind = null;
                station.moduleConstructionMaterials = null;
            }
            return () =>
            {
                station.addons.Remove(module);
                station.moduleBuildCompletesUtc = previousDeadline;
                station.moduleConstructionKind = previousKind;
                station.moduleConstructionMaterials = previousMaterials;
            };
        }

        static string InstallModuleFromCargo(NetPlayer pilot, int id, string kind)
        {
            var station = Find(id);
            string error = ModuleInstallationError(pilot, station, kind, out string account);
            if (error != null) return error;
            bool useProject = station.moduleConstructionKind == kind;
            if (useProject && station.buyOrders?.Count > 0)
                return "Cancel the active buy orders before finishing this module with cargo.";
            var materials = useProject ? station.moduleConstructionMaterials : ConstructionCost(kind);
            if (materials == null || materials.Count == 0) return "The module's material list is unavailable.";
            var changes = new List<(int item, int delta)>();
            foreach (var material in materials)
            {
                if (material == null || material.delivered < 0 || material.delivered > material.required)
                    return "The module has an invalid material requirement.";
                int outstanding = material.required - material.delivered;
                if (outstanding == 0) continue;
                if (NetProfiles.CargoAmount(account, material.item) < outstanding)
                    return "Insufficient " + ConstructionItemName(material.item) + " in cargo.";
                changes.Add((material.item, -outstanding));
            }
            var rollback = FundModuleConstruction(station, kind);
            bool saved = changes.Count == 0 ? Save() : CommitCargoTransaction(account, changes.ToArray());
            if (!saved)
            {
                rollback();
                return "The module installation could not be saved. Your cargo was kept.";
            }
            if (!(NetGame.PersistentHost && pilot.OwnerClientId == NetGame.LocalId))
                foreach (var change in changes)
                    NetState.Instance?.SendAdmin(pilot.OwnerClientId, NetAdmin.Order.Take, change.item, -change.delta, 0, "", "Station module");
            return ModuleName(kind) + (station.moduleBuildCompletesUtc > 0 ? " construction started." : " installed at " + station.name + ".");
        }

        internal static string InstallModuleFromStorage(NetPlayer pilot, int id, string kind)
        {
            var station = Find(id);
            kind = (kind ?? "").Trim().ToLowerInvariant();
            string error = ModuleInstallationError(pilot, station, kind, out string account);
            if (error != null) return error;
            if (!CanManageStorage(pilot, station, out _) || !AllowsFacilityUse(station, account, "storage"))
                return FacilityUseRefusal(station, account, "storage", "You need storage access to fund a module from storage.");
            bool activeProject = station.moduleConstructionKind == kind;
            if (activeProject && station.buyOrders?.Count > 0)
                return "Cancel active buy orders before finishing this module from storage.";
            var materials = activeProject ? station.moduleConstructionMaterials : ConstructionCost(kind);
            if (materials == null || materials.Count == 0) return "The module's material list is unavailable.";
            EnsureStorageLots(station);
            var required = new Dictionary<int, int>();
            foreach (var material in materials)
            {
                if (material == null || material.delivered < 0 || material.delivered > material.required)
                    return "The module has an invalid material requirement.";
                int remaining = material.required - material.delivered;
                if (remaining == 0) continue;
                required.TryGetValue(material.item, out int prior);
                required[material.item] = checked(prior + remaining);
            }
            foreach (var pair in required)
                if (OwnedStorageAmount(station, account, pair.Key) < pair.Value)
                    return "Insufficient " + ConstructionItemName(pair.Key) + " in your station storage.";

            var previousLots = CloneStorageLots(station);
            var previousStorage = station.storage.ConvertAll(stack => stack == null ? null :
                new PlayerStationStorage { item = stack.item, amount = stack.amount });
            foreach (var pair in required)
            {
                RemoveStorageLots(station, account, pair.Key, pair.Value);
                var stack = station.storage.Find(row => row != null && row.item == pair.Key);
                stack.amount -= pair.Value;
                if (stack.amount == 0) station.storage.Remove(stack);
            }
            var rollback = FundModuleConstruction(station, kind);
            if (Save()) return ModuleName(kind) + (station.moduleBuildCompletesUtc > 0 ? " construction funded from storage." : " installed using your station storage.");
            rollback();
            station.storage = previousStorage;
            station.storageLots = previousLots;
            return "The module installation could not be saved. Your stored goods were kept.";
        }

        internal static readonly string[] PlacementKinds = { "refinery", "drydock", "dockarm-1", "dockarm-2", "dockarm-3", "dockarm-4", "factory" };

        static readonly ModPlayerInfrastructure.OrbitalDefinition[] ModuleExteriorDefaults =
        {
            new ModPlayerInfrastructure.OrbitalDefinition { Id = "builtin:refinery", Name = "Refinery exterior",
                Assembly = "station_055_terran", ModelScale = .25f, HitRadius = 400f },
            new ModPlayerInfrastructure.OrbitalDefinition { Id = "builtin:drydock", Name = "Dry dock exterior",
                Assembly = "station_068_terran", ModelScale = .35f, HitRadius = 650f },
            new ModPlayerInfrastructure.OrbitalDefinition { Id = "builtin:dockarm", Name = "Docking arm exterior",
                Assembly = "container_003_terran", ModelScale = 1f, HitRadius = 100f },
            new ModPlayerInfrastructure.OrbitalDefinition { Id = "builtin:factory", Name = "Factory exterior",
                Assembly = "station_055_terran", ModelScale = .25f, HitRadius = 400f }
        };

        internal static IEnumerable<ModPlayerInfrastructure.OrbitalDefinition> ModuleExteriorChoices(PlayerStationRecord station, string kind)
        {
            int key = Array.IndexOf(PlacementKinds, kind);
            if (key < 0) yield break;
            var stock = ModuleExteriorDefaults[kind == "factory" ? 3 : Math.Min(key, 2)];
            if (NetGame.Db?.AssemblyByName(stock.Assembly) != null) yield return stock;
            int race = OrbitalSponsorRace(station);
            foreach (var preset in ModPlayerInfrastructure.Current.OrbitalStructures)
                if (!preset.Defense && OrbitalPresetAvailable(preset, race)) yield return preset;
        }

        static ModPlayerInfrastructure.OrbitalDefinition SavedModuleExterior(string id) =>
            Array.Find(ModuleExteriorDefaults, preset => preset.Id == id) ??
                ModPlayerInfrastructure.Current.OrbitalStructures.FirstOrDefault(preset => !preset.Defense && preset.Id == id);

        internal static string PlanModulePlacement(NetPlayer pilot, int id, string kind, int x, int y, int z, string presetId)
        {
            var station = Find(id);
            if (!CanManageConstruction(pilot, station, out _) || station.underConstruction)
                return "Dock at the operational station with construction authority.";
            int key = Array.IndexOf(PlacementKinds, kind);
            if (key < 0) return "Choose a refinery, factory, dry dock or docking arm.";
            var offset = new Vector3(x, y, z);
            if (offset.sqrMagnitude < 1f || offset.sqrMagnitude > 12000f * 12000f)
                return "Choose a placement within 12 km of the station.";
            var preset = ModuleExteriorChoices(station, kind).FirstOrDefault(entry => entry.Id == presetId);
            if (preset == null)
                return "Choose an exterior model available to this station.";
            var previous = station.placements;
            var updated = previous == null ? new List<PlayerStationPlacement>() : new List<PlayerStationPlacement>(previous);
            string module = kind.StartsWith("dockarm-", StringComparison.Ordinal) ? "dockarm" : kind;
            if (updated.Exists(entry => entry?.kind == kind) &&
                station.addons?.Exists(addon => addon != null && addon.kind == module && addon.level > 0) == true)
                return "That installed structure already has a fixed position.";
            updated.RemoveAll(entry => entry?.kind == kind);
            if (updated.Exists(entry => entry != null && (new Vector3(entry.x, entry.y, entry.z) - offset).sqrMagnitude < 150f * 150f))
                return "Choose a position at least 150 m from the other planned modules.";
            updated.Add(new PlayerStationPlacement { kind = kind, preset = presetId, x = x, y = y, z = z });
            station.placements = updated;
            if (Save()) return ModuleName(module) + " placement saved.";
            station.placements = previous;
            return "The placement could not be saved.";
        }

        internal static IEnumerable<(int id, string name, string assembly, Vector3 position, float scale, float radius)>
            PlacedModuleModels(Database db, int orbit)
        {
            var station = StationAt(orbit);
            if (!OperationalSponsor(station) || station.placements == null) yield break;
            foreach (var placement in station.placements)
            {
                int key = Array.IndexOf(PlacementKinds, placement?.kind);
                if (key < 0) continue;
                string module = placement.kind.StartsWith("dockarm-", StringComparison.Ordinal) ? "dockarm" : placement.kind;
                if (station.addons?.Exists(addon => addon != null && addon.level > 0 && addon.kind == module) != true) continue;
                var preset = SavedModuleExterior(placement.preset);
                if (preset == null) continue;
                yield return (-1 - key, ModuleName(module), OrbitalStructureAssembly(preset.Assembly, preset.StationModel, db),
                    StationPosition(orbit) + new Vector3(placement.x, placement.y, placement.z), preset.ModelScale, preset.HitRadius);
            }
        }

        internal static IEnumerable<Vector3> PlacedDockArms(int orbit)
        {
            var station = StationAt(orbit);
            if (!OperationalSponsor(station) || station.placements == null ||
                station.addons?.Exists(addon => addon?.kind == "dockarm" && addon.level > 0) != true) yield break;
            foreach (var entry in station.placements)
                if (entry?.kind is "dockarm-1" or "dockarm-2" or "dockarm-3" or "dockarm-4")
                {
                    var preset = SavedModuleExterior(entry.preset);
                    if (preset == null || OrbitalStructureAssembly(preset.Assembly, preset.StationModel, NetGame.Db) == null) continue;
                    var offset = new Vector3(entry.x, entry.y, entry.z);
                    yield return StationPosition(orbit) + offset + offset.normalized * preset.HitRadius;
                }
        }

        static List<PlayerStationMaterial> ProcurementMaterials(PlayerStationRecord station)
        {
            if (station == null || station.offeredForClaimUtc > 0 || station.destroyed || station.wreckClaimOpen || station.retired)
                return null;
            return station.underConstruction ? station.stationBuildCompletesUtc > 0 ? null : station.materials :
                station.moduleBuildCompletesUtc > 0 || string.IsNullOrEmpty(station.moduleConstructionKind) ? null : station.moduleConstructionMaterials;
        }
    }
}
