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
        const int MaxFactionConstructionUnits = 10000;

        static List<PlayerStationStorage> FactionConstructionMaterials(string factionId) =>
            registry?.factionConstructionStock?.Find(entry => entry != null &&
                string.Equals(entry.factionId, factionId, StringComparison.Ordinal))?.materials;

        internal static string FactionConstructionStockStatus(NetPlayer pilot)
        {
            if (!NetGame.IsServer || pilot == null || !NetProfiles.Controls(pilot.OwnerClientId))
                return "Use a controlled profile to view your faction's construction stock.";
            var faction = NetFactions.Of(NetProfiles.AccountOf(pilot.OwnerClientId));
            if (faction == null) return "Join a faction to use its construction stock.";
            var stock = FactionConstructionMaterials(faction.id);
            var lines = new List<string> { $"{faction.name} construction stock" };
            if (stock != null)
                foreach (var stack in stock)
                    if (stack != null && stack.amount > 0)
                        lines.Add($"{NetGame.Db?.Item(stack.item)?.name ?? $"Item {stack.item}"} ×{stack.amount:N0} [{stack.item}]");
            if (lines.Count == 1) lines.Add("No materials deposited.");
            return string.Join("\n", lines);
        }

        internal static string DonateFactionConstructionMaterials(NetPlayer pilot, int item, int amount)
        {
            if (!NetGame.IsServer || !sessionActive || registry == null || pilot == null || !pilot.IsSpawned ||
                !pilot.InHangar || !NetProfiles.Controls(pilot.OwnerClientId))
                return "Dock at an operational station with a controlled profile to donate materials.";
            string account = NetProfiles.AccountOf(pilot.OwnerClientId);
            var faction = NetFactions.Of(account);
            var station = StationAt(pilot.Station);
            if (faction == null || station == null || station.underConstruction || StationClosedByCombat(pilot.Station) ||
                station.ownershipType != PlayerStationOwnershipType.Faction ||
                !string.Equals(station.ownerFactionId, faction.id, StringComparison.Ordinal))
                return "Dock at an operational station owned by your faction to donate materials.";
            if (amount < 1 || amount > 500 || !NetGuard.Item(item) || !RequiredJumpgateMaterials().Exists(part => part.item == item))
                return "Donate 1–500 units of a valid jumpgate construction material.";

            registry.factionConstructionStock ??= new List<PlayerFactionConstructionStock>();
            var stock = FactionConstructionMaterials(faction.id);
            var newEntry = stock == null ? new PlayerFactionConstructionStock { factionId = faction.id } : null;
            if (newEntry != null) stock = newEntry.materials;
            long used = 0;
            foreach (var material in stock)
                if (material != null) used += Math.Max(0, material.amount);
            if (used + amount > MaxFactionConstructionUnits) return "Your faction's construction stock is full.";
            var itemStock = stock.Find(material => material != null && material.item == item);
            bool addedStack = itemStock == null;
            if (addedStack)
            {
                itemStock = new PlayerStationStorage { item = item };
                stock.Add(itemStock);
            }
            var entries = registry.factionConstructionStock;
            if (newEntry != null) entries.Add(newEntry);
            itemStock.amount += amount;
            if (!CommitCargoTransaction(account, (item, -amount)))
            {
                itemStock.amount -= amount;
                if (addedStack) stock.Remove(itemStock);
                if (newEntry != null) entries.Remove(newEntry);
                return "The donation could not be saved. Your materials were kept.";
            }
            if (!(NetGame.PersistentHost && pilot.OwnerClientId == NetGame.LocalId))
                NetState.Instance?.SendAdmin(pilot.OwnerClientId, NetAdmin.Order.Take, item, amount, 0, "", "Faction construction donation");
            return $"Donated {amount:N0} units to {faction.name}'s construction stock.";
        }
    }
}
