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
            if (station.addons?.Exists(addon => addon != null && addon.kind == kind && addon.level > 0) == true)
                return ModuleName(kind) + " is already installed.";
            int reservedSlot = !string.IsNullOrEmpty(station.moduleConstructionKind) && station.moduleConstructionKind != kind ? 1 : 0;
            if ((station.addons?.Count(addon => addon != null && addon.level > 0) ?? 0) + reservedSlot >= MaxAddonsPerStation)
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

        // Shared construction donations belong to the station, not to the donating pilot's personal storage.
        // Once the active module can be fully funded, consume the stock and start its normal build timer.
        static bool TryFundModuleFromDonations(PlayerStationRecord station, out bool ready)
        {
            ready = false;
            if (station.underConstruction || station.moduleBuildCompletesUtc > 0 ||
                string.IsNullOrEmpty(station.moduleConstructionKind) || station.moduleConstructionMaterials == null ||
                station.moduleConstructionMaterials.Count == 0 || station.donatedConstructionStock == null) return false;
            var required = new Dictionary<int, int>();
            foreach (var material in station.moduleConstructionMaterials)
            {
                if (material == null || material.delivered < 0 || material.required < material.delivered) return false;
                int remaining = material.required - material.delivered;
                if (remaining == 0) continue;
                required.TryGetValue(material.item, out int previous);
                if (previous > int.MaxValue - remaining) return false;
                required[material.item] = previous + remaining;
            }
            foreach (var pair in required)
                if ((station.donatedConstructionStock.Find(stack => stack != null && stack.item == pair.Key)?.amount ?? 0) < pair.Value)
                    return false;
            ready = true; // An eligible project must be retried if persistence or escrow settlement fails.
            var credits = new List<(string account, long delta)>();
            var banks = new List<(string factionId, long delta)>();
            var refunds = new List<(string account, long amount)>();
            if (!StageConstructionEscrowRefunds(station.buyOrders, credits, banks, refunds)) return false;
            var oldStock = station.donatedConstructionStock.ConvertAll(stack => stack == null ? null :
                new PlayerStationStorage { item = stack.item, amount = stack.amount });
            var oldOrders = station.buyOrders;
            var materials = station.moduleConstructionMaterials;
            var oldDelivered = materials.Select(material => material.delivered).ToList();
            foreach (var pair in required)
            {
                var stack = station.donatedConstructionStock.Find(entry => entry != null && entry.item == pair.Key);
                stack.amount -= pair.Value;
                if (stack.amount == 0) station.donatedConstructionStock.Remove(stack);
            }
            foreach (var material in materials) material.delivered = material.required;
            station.buyOrders = new List<PlayerStationBuyOrder>();
            string kind = station.moduleConstructionKind;
            var rollback = FundModuleConstruction(station, kind);
            bool saved = credits.Count > 0 || banks.Count > 0 ? CommitProfileTransaction(null, credits, banks) : Save();
            if (!saved)
            {
                rollback();
                station.donatedConstructionStock = oldStock;
                station.buyOrders = oldOrders;
                for (int i = 0; i < materials.Count; i++) materials[i].delivered = oldDelivered[i];
                return false;
            }
            if (!ProfileWritesBlocked)
                foreach (var refund in refunds) NotifyProfileCreditChange(refund.account, refund.amount, "Construction buy order escrow refund");
            Debug.Log($"Server: audit station {station.id}: {kind} funded from donated construction stock.");
            return true;
        }

        static Action CompleteModuleConstruction(PlayerStationRecord station, string kind)
        {
            long previousDeadline = station.moduleBuildCompletesUtc;
            station.moduleBuildCompletesUtc = 0;
            string previousKind = station.moduleConstructionKind;
            var previousMaterials = station.moduleConstructionMaterials;
            station.addons ??= new List<PlayerStationAddon>();
            int oldIndex = station.addons.FindIndex(addon => addon != null && addon.kind == kind && addon.level <= 0 && StructuralModuleKind(kind));
            var oldModule = oldIndex < 0 ? null : station.addons[oldIndex];
            if (oldModule != null) station.addons.RemoveAt(oldIndex);
            var module = new PlayerStationAddon { kind = kind, level = 1 };
            if (StructuralModuleKind(kind))
            {
                module.integrityInitialized = true;
                module.integrity = module.integrityCapacity = ModuleIntegrityCapacity(kind);
            }
            station.addons.Add(module);
            long paused = station.dryDockPausedUtc, started = station.dryDockStartedUtc, completes = station.dryDockCompletesUtc;
            if (kind == "drydock" && paused > 0)
            {
                long downtime = Math.Max(0L, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - paused);
                if (station.dryDockBuilding)
                {
                    station.dryDockStartedUtc += downtime;
                    station.dryDockCompletesUtc += downtime;
                }
                station.dryDockPausedUtc = 0;
            }
            if (kind == previousKind)
            {
                station.moduleConstructionKind = null;
                station.moduleConstructionMaterials = null;
            }
            return () =>
            {
                station.addons.Remove(module);
                if (oldModule != null) station.addons.Insert(oldIndex, oldModule);
                station.dryDockPausedUtc = paused;
                station.dryDockStartedUtc = started;
                station.dryDockCompletesUtc = completes;
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

        internal static bool TryDefenseSlot(string kind, out int slot)
        {
            slot = -1;
            if (kind == null || !kind.StartsWith("defense-", StringComparison.Ordinal) ||
                !int.TryParse(kind.Substring(8), out int number) || number < 1 ||
                number > ModPlayerInfrastructure.Current.OrbitalDefenseLimit) return false;
            slot = number - 1;
            return true;
        }

        static string PlanDefensePlacement(PlayerStationRecord station, string kind, int slot, Vector3Int offset, string presetId, Vector3Int? rotation)
        {
            var preset = ModPlayerInfrastructure.Current.OrbitalStructures.FirstOrDefault(p => p.Defense && p.Id == presetId && OrbitalPresetAvailable(p, OrbitalSponsorRace(station)));
            if (preset == null) return "Choose an available defense model.";
            if (registry.orbitalStructures.Exists(p => p != null && p.orbit == station.stationIndex && p.defense && p.slot == slot))
                return "This defense slot already has a construction project or operational defense.";
            var point = Vector3Int.RoundToInt(StationPosition(station.stationIndex)) + offset;
            string error = OrbitalPlacementError(station.stationIndex, GoF2Remake.World.OrbitLayout.Build(NetGame.Db, station.stationIndex), point, preset.HitRadius);
            if (error != null) return error;
            var previous = station.placements;
            var updated = previous == null ? new List<PlayerStationPlacement>() : new List<PlayerStationPlacement>(previous);
            updated.RemoveAll(p => p?.kind == kind);
            foreach (var other in updated)
            {
                if (other == null || !TryDefenseSlot(other.kind, out _)) continue;
                var otherPreset = ModPlayerInfrastructure.Current.OrbitalStructures.FirstOrDefault(p => p.Id == other.preset && p.Defense);
                float clearance = Math.Max(200f, preset.HitRadius + (otherPreset?.HitRadius ?? 0f));
                if ((new Vector3(other.x, other.y, other.z) - offset).sqrMagnitude < clearance * clearance)
                    return "This position overlaps " + other.kind + ".";
            }
            updated.Add(new PlayerStationPlacement { kind = kind, preset = presetId, x = offset.x, y = offset.y, z = offset.z,
                hasRotation = rotation.HasValue, pitch = rotation?.x ?? 0, yaw = rotation?.y ?? 0, roll = rotation?.z ?? 0 });
            station.placements = updated;
            if (Save()) return $"Defense {slot + 1} planned placement saved.";
            station.placements = previous;
            return "The placement could not be saved.";
        }

        internal static string PlanModulePlacement(NetPlayer pilot, int id, string kind, int x, int y, int z, string presetId, Vector3Int? rotation = null)
        {
            var station = Find(id);
            if (!CanManageConstruction(pilot, station, out _) || station.underConstruction)
                return "Dock at the operational station with construction authority.";
            if (rotation.HasValue && (Math.Abs((long)rotation.Value.x) > 360 || Math.Abs((long)rotation.Value.y) > 360 || Math.Abs((long)rotation.Value.z) > 360))
                return "Rotation angles must be between -360 and 360 degrees.";
            if (TryDefenseSlot(kind, out int defenseSlot)) return PlanDefensePlacement(station, kind, defenseSlot, new Vector3Int(x, y, z), presetId, rotation);
            int key = Array.IndexOf(PlacementKinds, kind);
            if (key < 0) return "Choose a refinery, factory, dry dock or docking arm.";
            var offset = new Vector3(x, y, z);
            if (offset.sqrMagnitude < 1f || offset.sqrMagnitude > 12000f * 12000f)
                return "Choose a placement within 12 km of the station.";
            var preset = ModuleExteriorChoices(station, kind).FirstOrDefault(entry => entry.Id == presetId);
            if (preset == null)
                return "Choose an exterior model available to this station.";
            if (StructuralModuleKind(kind))
            {
                var layout = GoF2Remake.World.OrbitLayout.Build(NetGame.Db, station.stationIndex);
                string error = OrbitalPlacementError(station.stationIndex, layout, StationPosition(station.stationIndex) + offset, preset.HitRadius);
                if (error != null) return error;
            }
            var previous = station.placements;
            var updated = previous == null ? new List<PlayerStationPlacement>() : new List<PlayerStationPlacement>(previous);
            string module = kind.StartsWith("dockarm-", StringComparison.Ordinal) ? "dockarm" : kind;
            if (updated.Exists(entry => entry?.kind == kind) &&
                station.addons?.Exists(addon => addon != null && addon.kind == module && addon.level > 0) == true)
                return "That installed structure already has a fixed position.";
            updated.RemoveAll(entry => entry?.kind == kind);
            if (StructuralModuleKind(kind))
                foreach (var entry in updated)
                {
                    if (entry == null) continue;
                    var other = SavedModuleExterior(entry.preset);
                    float separation = Math.Max(150f, preset.HitRadius + (other?.HitRadius ?? 0f));
                    if ((new Vector3(entry.x, entry.y, entry.z) - offset).sqrMagnitude < separation * separation)
                        return "Place the structure clear of other module exteriors.";
                }
            if (updated.Exists(entry => entry != null && (new Vector3(entry.x, entry.y, entry.z) - offset).sqrMagnitude < 150f * 150f))
                return "Choose a position at least 150 m from the other planned modules.";
            updated.Add(new PlayerStationPlacement { kind = kind, preset = presetId, x = x, y = y, z = z,
                hasRotation = rotation.HasValue, pitch = rotation?.x ?? 0, yaw = rotation?.y ?? 0, roll = rotation?.z ?? 0 });
            station.placements = updated;
            if (Save()) return ModuleName(module) + " placement saved.";
            station.placements = previous;
            return "The placement could not be saved.";
        }

        // Negative exterior IDs are orbit-local and never identify independent orbital projects.
        internal static bool StructuralModuleKind(string kind) => kind is "drydock" or "refinery";
        internal static string StructuralModuleContactKind(int id) => id switch
        {
            -1 => "refinery",
            -2 => "drydock",
            _ => null
        };
        internal static float ModuleIntegrityCapacity(string kind) => kind == "drydock"
            ? ModPlayerInfrastructure.Current.DryDockIntegrity : ModPlayerInfrastructure.Current.RefineryIntegrity;

        internal static float ModuleRemainingIntegrity(PlayerStationAddon module)
        {
            if (module == null || module.level <= 0 || !StructuralModuleKind(module.kind)) return 0f;
            float capacity = ModuleIntegrityCapacity(module.kind);
            // Legacy saves start intact. Changing mod balance preserves the fraction already damaged.
            return module.integrityInitialized && module.integrityCapacity > 0f
                ? Mathf.Clamp01(module.integrity / module.integrityCapacity) * capacity : capacity;
        }

        internal static bool StructuralModuleDestroyed(int orbit, int id)
        {
            string kind = StructuralModuleContactKind(id);
            return kind != null && StationAt(orbit)?.addons?.Exists(a => a != null && a.kind == kind && a.level <= 0 && a.integrityInitialized) == true;
        }

        internal static bool TryGetStructuralModuleContact(int orbit, int id, out Vector3 position, out float radius)
        {
            position = default;
            radius = 0f;
            if (!NetGame.IsServer || !sessionActive || StructuralModuleContactKind(id) == null) return false;
            foreach (var model in PlacedModuleModels(NetGame.Db, orbit))
            {
                if (model.id != id || string.IsNullOrEmpty(model.assembly)) continue;
                position = model.position;
                radius = model.radius;
                return radius > 0f;
            }
            return false;
        }

        internal static bool DamageStructuralModule(NetPlayer pilot, int orbit, int id, float damage)
        {
            if (!NetGame.IsServer || !sessionActive || pilot == null || pilot.Station != orbit ||
                !NetGuard.Finite(damage) || damage <= 0f) return false;
            var station = StationAt(orbit);
            string kind = StructuralModuleContactKind(id);
            if (kind == null || !OperationalSponsor(station)) return false;
            var module = station.addons?.Find(a => a != null && a.kind == kind && a.level > 0);
            if (module == null) return false;
            float remaining = ModuleRemainingIntegrity(module);
            int oldLevel = module.level;
            bool oldInitialized = module.integrityInitialized;
            float oldIntegrity = module.integrity, oldCapacity = module.integrityCapacity;
            long oldPause = station.dryDockPausedUtc;
            module.integrityInitialized = true;
            module.integrityCapacity = ModuleIntegrityCapacity(kind);
            module.integrity = Mathf.Max(0f, remaining - damage);
            if (module.integrity <= 0f)
            {
                module.level = 0;
                if (kind == "drydock" && station.dryDockBuilding && station.dryDockPausedUtc == 0)
                    station.dryDockPausedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }
            if (Save())
            {
                NoteOrbitalAggression(pilot, orbit);
                return true;
            }
            module.level = oldLevel;
            module.integrityInitialized = oldInitialized;
            module.integrity = oldIntegrity;
            module.integrityCapacity = oldCapacity;
            station.dryDockPausedUtc = oldPause;
            return false;
        }

        internal static IEnumerable<(int id, string name, string assembly, Vector3 position, float scale, float radius)>
            PlacedModuleModels(Database db, int orbit)
        {
            var station = StationAt(orbit);
            if (!OperationalSponsor(station)) yield break;
            var placements = station.placements ?? new List<PlayerStationPlacement>();
            foreach (var placement in placements)
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
            // Older installations and modules built without a manual plan still need a physical combat target.
            // Put the stock exteriors on opposite vertical sides, clear of the station's bounding radius.
            for (int key = 0; key < 2; key++)
            {
                string kind = PlacementKinds[key];
                if (placements.Exists(entry => entry?.kind == kind) ||
                    station.addons?.Exists(addon => addon != null && addon.kind == kind && addon.level > 0) != true) continue;
                var preset = ModuleExteriorDefaults[key];
                float distance = OrbitalStationClearance(GoF2Remake.World.OrbitLayout.Build(db, orbit), preset.HitRadius) + 1000f;
                Vector3 position = StationPosition(orbit) + Vector3.up * (key == 0 ? distance : -distance);
                yield return (-1 - key, ModuleName(kind), OrbitalStructureAssembly(preset.Assembly, preset.StationModel, db),
                    position, preset.ModelScale, preset.HitRadius);
            }
        }

        internal static Quaternion PlacedModelRotation(int orbit, int id)
        {
            if (id > 0)
            {
                var structure = FindOrbitalStructure(id);
                return structure != null && structure.hasRotation ? Quaternion.Euler(structure.pitch, structure.yaw, structure.roll) : Quaternion.identity;
            }
            int key = -1 - id;
            if (key < 0 || key >= PlacementKinds.Length) return Quaternion.identity;
            return StationAt(orbit)?.placements?.Find(p => p?.kind == PlacementKinds[key])?.GetRotation() ?? Quaternion.identity;
        }

        // One calculation for the runtime docking point and placement preview. Old saves keep their radial pose.
        internal static (Vector3 position, Quaternion rotation) DockArmPose(Vector3 offset, float radius, Quaternion? rotation)
        {
            var direction = offset.sqrMagnitude > 0f ? offset.normalized : Vector3.forward;
            Quaternion turn = rotation ?? Quaternion.LookRotation(direction);
            return (offset + (rotation.HasValue ? turn * Vector3.forward : direction) * radius, turn);
        }

        internal static IEnumerable<Vector3> PlacedDockArms(int orbit) => PlacedDockArmPoses(orbit).Select(p => p.position);

        internal static IEnumerable<(Vector3 position, Quaternion rotation)> PlacedDockArmPoses(int orbit)
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
                    var pose = DockArmPose(offset, preset.HitRadius, entry.hasRotation ? (Quaternion?)entry.GetRotation() : null);
                    yield return (StationPosition(orbit) + pose.position, pose.rotation);
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
