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

        internal static string BuyListing(NetPlayer by, int id, int listingIndex, int amount)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!NetGame.IsServer || by == null || !by.IsSpawned || !by.InHangar ||
                by.Station != record.stationIndex || !NetProfiles.Enabled || !NetProfiles.Controls(by.OwnerClientId))
                return "Dock at this station and use a controlled player profile to buy goods.";
            if (record.underConstruction) return "This station is not operational yet.";
            if (listingIndex < 0 || record.listings == null || listingIndex >= record.listings.Count)
                return "No listing exists at that index.";
            var listing = record.listings[listingIndex];
            if (listing == null || listing.amount < 1 || !NetGuard.Item(listing.item) ||
                !Hangar.IsSaleable(listing.item) || listing.unitPrice < 1 ||
                string.IsNullOrEmpty(listing.sellerProfile))
                return "That listing is invalid and cannot be purchased.";
            if (amount < 1 || amount > 500 || amount > listing.amount)
                return "Choose an amount between 1 and 500, up to the available listing stock.";

            string buyer = NetProfiles.AccountOf(by.OwnerClientId);
            string seller = listing.sellerProfile;
            if (string.IsNullOrEmpty(buyer)) return "Your player profile is unavailable.";
            if (string.Equals(buyer, seller, StringComparison.Ordinal))
                return "You cannot purchase your own listing.";
            if (listing.unitPrice > int.MaxValue / (long)amount)
                return "That purchase exceeds the marketplace's per-transaction credit limit.";
            long total = listing.unitPrice * amount;
            long credits = NetProfiles.CreditAmount(buyer);
            if (credits < total) return "Your saved profile does not have enough credits. Dock again to sync your balance.";
            int cargo = NetProfiles.CargoAmount(buyer, listing.item);
            if (cargo < 0 || (long)cargo + amount > int.MaxValue)
                return "Your saved cargo could not accept this purchase.";

            bool removeListing = amount == listing.amount;
            if (removeListing) record.listings.RemoveAt(listingIndex);
            else listing.amount -= amount;
            if (!CommitMarketplaceTransaction(buyer, seller, listing.item, amount, total))
            {
                if (removeListing) record.listings.Insert(listingIndex, listing);
                else listing.amount += amount;
                return "The purchase could not be journalled; no credits or goods were transferred.";
            }

            bool buyerIsHost = NetGame.PersistentHost && by.OwnerClientId == NetGame.LocalId;
            if (!buyerIsHost)
            {
                NetState.Instance?.SendAdmin(by.OwnerClientId, NetAdmin.Order.Credits, -(int)total, 0, 0, "", "Station marketplace");
                NetState.Instance?.SendAdmin(by.OwnerClientId, NetAdmin.Order.Give, listing.item, amount, 0, "", "Station marketplace");
            }

            foreach (ulong client in NetProfiles.ClientsOf(seller))
            {
                if (!NetProfiles.Controls(client)) continue;
                if (!(NetGame.PersistentHost && client == NetGame.LocalId))
                    NetState.Instance?.SendAdmin(client, NetAdmin.Order.Credits, (int)total, 0, 0, "", "Station marketplace");
                break;
            }

            string itemName = NetGame.Db?.Item(listing.item)?.name ?? ("Item " + listing.item);
            return "Purchased " + amount + " x " + itemName + " for " + total.ToString("N0") +
                " credits from " + record.name + ".";
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
            long stored = 0;
            foreach (var stack in record.storage) if (stack != null) stored += Math.Max(0, stack.amount);
            if (stock == null && record.storage.Count >= MaxStorageItemTypes)
                return "Storage has reached its item-type limit; free a slot before cancelling this listing.";
            if (stock != null && stock.amount > int.MaxValue - listing.amount)
                return "The returned item stack has reached its maximum supported quantity.";
            if (stored + listing.amount > capacity)
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
