using System;
using System.Collections.Generic;
using GoF2Remake.Data;

namespace GoF2Remake.Multiplayer
{
    [Serializable]
    public sealed class PlayerStationShipListing
    {
        public int id;
        public StoredShip hull;
        public string sellerProfile;
        public long price;
    }

    public static partial class NetPlayerStations
    {
        static bool ShipMarketContext(NetPlayer pilot, PlayerStationRecord station, out string account, bool recoverProperty = false)
        {
            account = null;
            if (!NetGame.IsServer || station == null || station.underConstruction ||
                !IsStationContext(pilot, station, out account, recoverProperty)) return false;
            return true;
        }

        static void AddShipMarketplaceListings(PlayerStationRecord station, List<string> lines)
        {
            if (station.shipListings == null) return;
            foreach (var listing in station.shipListings)
            {
                if (listing?.hull == null) continue;
                string name = NetGame.Db?.Ship(listing.hull.ship)?.name ?? $"Hull {listing.hull.ship}";
                lines.Add($"Ship #{listing.id}: {name} for {listing.price:N0} credits.");
            }
        }

        internal static string ListMarketplaceShip(NetPlayer pilot, int stationId, int fleetIndex, long price)
        {
            var station = Find(stationId);
            if (!ShipMarketContext(pilot, station, out string seller) ||
                !HasStationPermission(station, seller, PlayerStationPermission.Marketplace))
                return "Dock at an operational station with marketplace permission.";
            if (!CanTradeAtMarket(station, seller)) return MarketRefusal(station, seller, "Station policy restricts marketplace listings.");
            if (price <= 0 || price > MaxStationUnitPrice) return "Choose a supported positive ship price.";
            station.shipListings ??= new List<PlayerStationShipListing>();
            if (station.shipListings.Count >= MaxStationListings) return "The station's ship marketplace is full.";
            var ships = NetProfiles.StoredShipsOf(seller, pilot.OwnerClientId);
            if (ships == null || fleetIndex < 1 || fleetIndex > ships.Count)
                return "Use /station fleet to choose a hull in your personal stored fleet.";
            var ship = ships[fleetIndex - 1];
            if (ship == null || NetGame.Db?.Ship(ship.ship) == null)
                return "This hull is not in the active ship catalogue.";
            var inventory = new Dictionary<int, long>();
            if (!CollectShipInventory(ship, inventory) || inventory.Count != 0)
                return "List a bare hull. Use the station hangar or dry dock to unload fitted equipment and cargo first.";

            var fleet = new List<StoredShip>(ships);
            fleet.RemoveAt(fleetIndex - 1);
            int previousNextId = station.nextShipListingId;
            int id = Math.Max(1, previousNextId);
            if (id == int.MaxValue) return "The ship listing ID limit has been reached.";
            station.nextShipListingId = id + 1;
            var escrow = new PlayerStationShipListing { id = id, hull = BareHull(ship), sellerProfile = seller, price = price };
            station.shipListings.Add(escrow);
            if (!CommitStoredShipTransaction(seller, fleet))
            {
                station.shipListings.Remove(escrow);
                station.nextShipListingId = previousNextId;
                return "The listing could not be saved. The ship remains in your fleet.";
            }
            if (!(NetGame.PersistentHost && pilot.OwnerClientId == NetGame.LocalId)) NetProfiles.RefreshProfile(pilot.OwnerClientId);
            return $"Ship #{escrow.id} listed at {station.name} for {price:N0} credits.";
        }

        internal static string SettleMarketplaceShip(NetPlayer pilot, int stationId, int listingId, bool cancel)
        {
            var station = Find(stationId);
            if (!ShipMarketContext(pilot, station, out string buyer, recoverProperty: cancel))
                return "Dock at the operational station with a controlled profile.";
            if (!cancel && !CanTradeAtMarket(station, buyer)) return MarketRefusal(station, buyer, "Station policy restricts marketplace purchases.");
            int index = station.shipListings?.FindIndex(value => value != null && value.id == listingId) ?? -1;
            if (listingId < 1 || index < 0) return "No ship listing with that number.";
            var listing = station.shipListings[index];
            if (listing?.hull == null || listing.price <= 0 || listing.price > MaxStationUnitPrice ||
                string.IsNullOrEmpty(listing.sellerProfile) || NetGame.Db?.Ship(listing.hull.ship) == null)
                return "The ship listing is unavailable.";
            bool seller = string.Equals(buyer, listing.sellerProfile, StringComparison.Ordinal);
            if (cancel != seller) return cancel ? "Only the seller can cancel this listing." : "You cannot buy your own listing.";
            var ships = NetProfiles.StoredShipsOf(buyer, pilot.OwnerClientId);
            if (ships == null || ships.Count >= 100) return "Your personal stored fleet is full or unavailable.";
            var target = new List<StoredShip>(ships) { listing.hull };

            long tax = cancel ? 0 : (listing.price * Math.Clamp(station.marketplaceTaxPercent, 0, 10) + 99) / 100;
            var (charge, proceeds, ownerRevenue) = cancel
                ? (0L, 0L, 0L) : StationMarketSettlement(station, buyer, listing.sellerProfile, listing.price, tax);
            tax = ownerRevenue;
            if (!cancel && NetProfiles.CreditAmount(buyer) < charge)
                return "Your profile does not have enough credits for the ship and tax.";
            var payments = new List<(string account, long delta)>();
            var banks = new List<(string factionId, long delta)>();
            bool factionTax = station.ownershipType == PlayerStationOwnershipType.Faction;
            if (!cancel)
            {
                payments.Add((buyer, -charge));
                if (proceeds > 0) payments.Add((listing.sellerProfile, proceeds));
                if (tax > 0)
                {
                    if (factionTax) banks.Add((station.ownerFactionId, tax));
                    else payments.Add((station.ownerProfile, tax));
                }
            }
            station.shipListings.RemoveAt(index);
            if (!CommitProfileTransaction(null, payments, banks, new List<(string account, List<StoredShip> ships)> { (buyer, target) }))
            {
                station.shipListings.Insert(index, listing);
                return "The ship transaction could not be saved. The hull remains in escrow.";
            }

            if (!(NetGame.PersistentHost && pilot.OwnerClientId == NetGame.LocalId)) NetProfiles.RefreshProfile(pilot.OwnerClientId);
            if (cancel) return "The listing was cancelled and the hull is back in your personal fleet.";
            NotifyProfileCreditChange(listing.sellerProfile, proceeds, "Station ship marketplace");
            if (tax > 0 && !factionTax && station.ownerProfile != buyer)
                NotifyProfileCreditChange(station.ownerProfile, tax, "Station ship marketplace tax");
            return $"Purchased the hull for {charge:N0} credits. The hull is in your personal fleet.";
        }
    }
}
