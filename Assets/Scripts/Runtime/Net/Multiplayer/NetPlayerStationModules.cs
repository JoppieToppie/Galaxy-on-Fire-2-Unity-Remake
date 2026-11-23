using System;
using System.Collections.Generic;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetPlayerStations
    {
        static readonly string[] ModuleKinds = { "storage", "refinery", "factory", "hangar", "drydock" };

        static string ModuleName(string kind) => kind == "hangar" ? "Ship hangar" :
            kind == "drydock" ? "Dry dock" : char.ToUpperInvariant(kind[0]) + kind.Substring(1);

        static string ModuleInstallationError(NetPlayer pilot, PlayerStationRecord station, string kind, out string account)
        {
            account = null;
            if (station == null) return "No player station with that id exists.";
            if (Array.IndexOf(ModuleKinds, kind) < 0) return "Choose storage, refinery, factory, hangar or drydock.";
            if (!CanManageConstruction(pilot, station, out account))
                return "Dock here as the builder or authorised construction manager to install modules.";
            if (station.underConstruction) return "Finish station construction before installing modules.";
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

        static Action CompleteModuleConstruction(PlayerStationRecord station, string kind)
        {
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
            var rollback = CompleteModuleConstruction(station, kind);
            bool saved = changes.Count == 0 ? Save() : CommitCargoTransaction(account, changes.ToArray());
            if (!saved)
            {
                rollback();
                return "The module installation could not be saved. Your cargo was kept.";
            }
            if (!(NetGame.PersistentHost && pilot.OwnerClientId == NetGame.LocalId))
                foreach (var change in changes)
                    NetState.Instance?.SendAdmin(pilot.OwnerClientId, NetAdmin.Order.Take, change.item, -change.delta, 0, "", "Station module");
            return ModuleName(kind) + " installed at " + station.name + ".";
        }

        static List<PlayerStationMaterial> ProcurementMaterials(PlayerStationRecord station)
        {
            if (station == null || station.offeredForClaimUtc > 0 || station.destroyed || station.wreckClaimOpen || station.retired)
                return null;
            return station.underConstruction ? station.materials :
                string.IsNullOrEmpty(station.moduleConstructionKind) ? null : station.moduleConstructionMaterials;
        }
    }
}
