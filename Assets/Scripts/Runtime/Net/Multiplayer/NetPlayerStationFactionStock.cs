using System;
using System.Collections.Generic;

namespace GoF2Remake.Multiplayer
{
    [Serializable]
    public sealed class PlayerFactionConstructionStock
    {
        public string factionId;
        public List<PlayerStationStorage> materials = new List<PlayerStationStorage>();
    }

    public static partial class NetPlayerStations
    {
        const int MaxStationConstructionUnits = 10000;

        static bool CanUseStationConstructionStock(PlayerStationRecord station, string account)
        {
            if (station == null || station.retired || station.offeredForClaimUtc > 0 || station.underConstruction || station.wreckClaimOpen ||
                station.destroyed || string.IsNullOrEmpty(account)) return false;
            return station.ownershipType == PlayerStationOwnershipType.Faction
                ? !string.IsNullOrEmpty(station.ownerFactionId) &&
                    string.Equals(NetFactions.Of(account)?.id, station.ownerFactionId, StringComparison.Ordinal)
                : string.Equals(station.ownerProfile, account, StringComparison.Ordinal);
        }

        // Legacy faction pools did not store their receiving station or intended gate.
        // Bind each pool to the oldest surviving station still owned by that faction.
        static void MigrateFactionConstructionStock()
        {
            if (registry?.factionConstructionStock == null) return;
            foreach (var legacy in registry.factionConstructionStock)
            {
                if (legacy?.materials == null || string.IsNullOrEmpty(legacy.factionId)) continue;
                PlayerStationRecord target = null;
                foreach (var station in registry.stations)
                    if (station != null && !station.destroyed && !station.retired &&
                        station.ownershipType == PlayerStationOwnershipType.Faction &&
                        string.Equals(station.ownerFactionId, legacy.factionId, StringComparison.Ordinal) &&
                        (target == null || station.id < target.id)) target = station;
                if (target == null) continue;
                target.donatedConstructionStock ??= new List<PlayerStationStorage>();
                foreach (var material in legacy.materials)
                {
                    if (material == null || material.amount <= 0) continue;
                    var existing = target.donatedConstructionStock.Find(stack => stack != null && stack.item == material.item);
                    if (existing == null)
                    {
                        existing = new PlayerStationStorage { item = material.item };
                        target.donatedConstructionStock.Add(existing);
                    }
                    int move = (int)Math.Min(material.amount, Math.Max(0L, (long)int.MaxValue - existing.amount));
                    existing.amount += move;
                    material.amount -= move;
                }
            }
            registry.factionConstructionStock.RemoveAll(legacy => legacy?.materials == null ||
                !legacy.materials.Exists(stack => stack != null && stack.amount > 0));
        }

        internal static string FactionConstructionStockStatus(NetPlayer pilot)
        {
            if (!NetGame.IsServer || pilot == null || !pilot.IsSpawned || !pilot.InHangar ||
                !NetProfiles.Controls(pilot.OwnerClientId))
                return "Dock at an operational player station to view its donated stock.";
            var station = StationAt(pilot.Station);
            if (!CanUseStationConstructionStock(station, NetProfiles.AccountOf(pilot.OwnerClientId)))
                return "Only the station owner or members of its owning faction can view its donated stock.";
            var lines = new List<string> { $"{station.name} construction stock" };
            if (station.donatedConstructionStock != null)
                foreach (var stack in station.donatedConstructionStock)
                    if (stack != null && stack.amount > 0)
                        lines.Add($"{NetGame.Db?.Item(stack.item)?.name ?? $"Item {stack.item}"} ×{stack.amount:N0} [{stack.item}]");
            if (lines.Count == 1) lines.Add("No materials deposited.");
            return string.Join("\n", lines);
        }

        static bool AcceptsConstructionStock(int item)
        {
            if (RequiredJumpgateMaterials().Exists(part => part.item == item)) return true;
            if (registry?.orbitalStructures?.Exists(structure => structure != null &&
                (structure.materials.Exists(material => material.item == item) ||
                structure.maintenanceMaterials?.Exists(material => material.item == item) == true)) == true) return true;
            foreach (var preset in Modding.ModPlayerInfrastructure.Current.OrbitalStructures)
                if (preset.Materials.Exists(material => material.item == item) ||
                    preset.MaintenanceMaterials.Exists(material => material.item == item)) return true;
            return false;
        }

        internal static string DonateFactionConstructionMaterials(NetPlayer pilot, int item, int amount)
        {
            if (!NetGame.IsServer || !sessionActive || registry == null || pilot == null || !pilot.IsSpawned ||
                !pilot.InHangar || !NetProfiles.Controls(pilot.OwnerClientId))
                return "Dock at an operational station with a controlled profile to donate materials.";
            string account = NetProfiles.AccountOf(pilot.OwnerClientId);
            var station = StationAt(pilot.Station);
            if (!CanUseStationConstructionStock(station, account))
                return "Dock at an operational station owned by you or your faction to donate materials.";
            if (amount < 1 || amount > 500 || !NetGuard.Item(item) || !AcceptsConstructionStock(item))
                return "Donate 1–500 units of a valid construction or maintenance material.";

            var stock = station.donatedConstructionStock ??= new List<PlayerStationStorage>();
            long used = 0;
            foreach (var material in stock)
                if (material != null) used += Math.Max(0, material.amount);
            if (used + amount > MaxStationConstructionUnits) return "This station's construction stock is full.";
            var itemStock = stock.Find(material => material != null && material.item == item);
            bool addedStack = itemStock == null;
            if (addedStack)
            {
                itemStock = new PlayerStationStorage { item = item };
                stock.Add(itemStock);
            }
            itemStock.amount += amount;
            if (!CommitCargoTransaction(account, (item, -amount)))
            {
                itemStock.amount -= amount;
                if (addedStack) stock.Remove(itemStock);
                return "The donation could not be saved. Your materials were kept.";
            }
            if (!(NetGame.PersistentHost && pilot.OwnerClientId == NetGame.LocalId))
                NetState.Instance?.SendAdmin(pilot.OwnerClientId, NetAdmin.Order.Take, item, amount, 0, "", "Faction construction donation");
            return $"Donated {amount:N0} units to {station.name}'s construction stock.";
        }
    }
}
