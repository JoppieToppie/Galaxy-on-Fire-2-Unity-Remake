using System;
using System.Collections.Generic;
using GoF2Remake.Data;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetPlayerStations
    {
        const int MaxStationListings = 64;
        const long MaxStationUnitPrice = 1000000000L;

        internal static string MarketStatus(int id)
        {
            if (!NetGame.Active) return "Join a multiplayer session first.";
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            var lines = new List<string> { "Marketplace at " + record.name + ":" };
            if (record.listings == null || record.listings.Count == 0)
            {
                lines.Add("No active listings.");
                return string.Join("\n", lines);
            }
            for (int i = 0; i < record.listings.Count; i++)
            {
                var listing = record.listings[i];
                if (listing == null || listing.amount <= 0) continue;
                string itemName = NetGame.Db?.Item(listing.item)?.name ?? ("Item " + listing.item);
                lines.Add($"#{i}: {itemName} x{listing.amount:N0} — {listing.unitPrice:N0} credits each");
            }
            return string.Join("\n", lines);
        }

        internal static string CreateListing(NetPlayer by, int id, int item, int amount, long unitPrice)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!CanManage(by, record, out string account))
                return "Only the station owner can create listings while docked at the station.";
            if (record.underConstruction) return "Finish station construction before opening listings.";
            if (StorageCapacity(record) <= 0) return "Install a storage module before listing station goods.";
            if (!NetGuard.Item(item) || !Hangar.IsSaleable(item) || amount < 1 || amount > 500 || unitPrice < 1 || unitPrice > MaxStationUnitPrice)
                return "Use a saleable item, 1–500 units, and a unit price from 1 to 1,000,000,000 credits.";
            record.listings ??= new List<PlayerStationListing>();
            if (record.listings.Count >= MaxStationListings) return "This station has reached its listing limit.";
            record.storage ??= new List<PlayerStationStorage>();
            var stock = record.storage.Find(x => x != null && x.item == item);
            if (stock == null || stock.amount < amount) return "The station does not have enough stock to escrow that listing.";

            stock.amount -= amount;
            if (stock.amount == 0) record.storage.Remove(stock);
            record.listings.Add(new PlayerStationListing { item = item, amount = amount, unitPrice = unitPrice, sellerProfile = account });
            if (!Save())
            {
                record.listings.RemoveAt(record.listings.Count - 1);
                if (stock.amount == 0) record.storage.Add(stock);
                stock.amount += amount;
                return "The listing could not be saved; the goods were returned to storage where possible.";
            }
            return "Listing #" + (record.listings.Count - 1) + " created. " + amount + " units are held in marketplace escrow.";
        }

        internal static string CancelListing(NetPlayer by, int id, int listingIndex)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!CanManage(by, record, out _))
                return "Only the station owner can cancel listings while docked at the station.";
            if (listingIndex < 0 || record.listings == null || listingIndex >= record.listings.Count)
                return "No listing exists at that index.";
            var listing = record.listings[listingIndex];
            if (listing == null || listing.amount <= 0 || !NetGuard.Item(listing.item))
                return "That listing is invalid and cannot be returned automatically.";
            record.storage ??= new List<PlayerStationStorage>();
            var stock = record.storage.Find(x => x != null && x.item == listing.item);
            int capacity = StorageCapacity(record);
            int stored = 0;
            foreach (var stack in record.storage) if (stack != null) stored += Math.Max(0, stack.amount);
            if (stock == null && record.storage.Count >= MaxStorageItemTypes)
                return "Storage has reached its item-type limit; free a slot before cancelling this listing.";
            if ((long)stored + listing.amount > capacity)
                return "Storage has insufficient room to return this listing; withdraw or clear space first.";

            if (stock == null) record.storage.Add(stock = new PlayerStationStorage { item = listing.item });
            stock.amount += listing.amount;
            record.listings.RemoveAt(listingIndex);
            if (!Save())
            {
                stock.amount -= listing.amount;
                if (stock.amount == 0) record.storage.Remove(stock);
                record.listings.Insert(listingIndex, listing);
                return "The cancellation could not be saved; the listing remains active.";
            }
            return "Listing cancelled and " + listing.amount + " units returned to station storage.";
        }
    }
}
