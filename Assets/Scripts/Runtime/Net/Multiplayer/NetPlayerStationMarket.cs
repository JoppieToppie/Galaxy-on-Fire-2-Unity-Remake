using System;
using System.Collections.Generic;
using GoF2Remake.Data;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetPlayerStations
    {
        const int MaxStationListings = 64;
        const long MaxStationUnitPrice = 1000000000L;

        const int MaxConstructionBuyOrders = 32;

        internal static string CreateConstructionBuyOrder(NetPlayer by, int id, int item, int amount, long unitPrice)
        {
            var record = Find(id);
            var materials = ProcurementMaterials(record);
            if (materials == null) return "That construction project is not accepting orders.";
            if (!NetGame.IsServer || by == null || !by.IsSpawned || !by.InHangar ||
                !NetProfiles.Enabled || !NetProfiles.Controls(by.OwnerClientId))
                return "Dock at any station using a controlled profile to create a construction buy order.";
            if (!NetGuard.Item(item) || amount < 1 || amount > 500 || unitPrice < 1 || unitPrice > MaxStationUnitPrice ||
                unitPrice > int.MaxValue / (long)Math.Max(1, amount))
                return "Use a valid material, 1–500 units, and a supported positive unit price.";
            var material = materials.Find(x => x != null && x.item == item);
            if (material == null || amount > material.required - material.delivered)
                return "The order exceeds the project's remaining requirement for that material.";
            long ordered = 0;
            foreach (var pending in record.buyOrders ?? new List<PlayerStationBuyOrder>())
                if (pending != null && pending.item == item && pending.remaining > 0) ordered += pending.remaining;
            if (amount + ordered > material.required - material.delivered)
                return "Existing buy orders already cover part or all of that requirement.";
            string account = NetProfiles.AccountOf(by.OwnerClientId);
            if (string.IsNullOrEmpty(account)) return "Your player profile is unavailable.";
            string fundingProfile = null, fundingFaction = null;
            long available;
            if (record.ownershipType == PlayerStationOwnershipType.Faction)
            {
                if (!IsFactionManager(record, account)) return "Only the owning faction leader or an officer may spend its treasury.";
                fundingFaction = record.ownerFactionId;
                available = NetFactions.BankAmount(fundingFaction);
            }
            else
            {
                if (!string.Equals(account, record.ownerProfile, StringComparison.Ordinal))
                    return "Only the individual station owner may fund its construction buy orders.";
                fundingProfile = record.ownerProfile;
                available = NetProfiles.CreditAmount(fundingProfile);
            }
            long reserve = unitPrice * amount;
            if (available < 0) return "The station funding account is unavailable.";
            if (available < reserve) return $"The order needs {reserve:N0} credits in escrow; only {available:N0} are available.";
            record.buyOrders ??= new List<PlayerStationBuyOrder>();
            if (record.buyOrders.Count >= MaxConstructionBuyOrders) return "This project has reached its active buy-order limit.";
            var order = new PlayerStationBuyOrder { item = item, remaining = amount, unitPrice = unitPrice, reservedCredits = reserve,
                fundedByProfile = fundingProfile, fundedByFactionId = fundingFaction, createdBy = account,
                createdUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            record.buyOrders.Add(order);
            var credits = fundingProfile != null ? new List<(string account, long delta)> { (fundingProfile, -reserve) } : null;
            var banks = fundingFaction != null ? new List<(string factionId, long delta)> { (fundingFaction, -reserve) } : null;
            if (!CommitProfileTransaction(null, credits, banks))
            {
                record.buyOrders.Remove(order);
                return "The order could not be journalled; no funds were reserved.";
            }
            if (fundingProfile != null)
                NotifyProfileCreditChange(fundingProfile, -reserve, "Construction buy order escrow");
            return $"Construction buy order #{record.buyOrders.Count - 1} created for {amount} x {NetGame.Db?.Item(item)?.name ?? ("Item " + item)} at {unitPrice:N0} each. {reserve:N0} credits escrowed.";
        }

        internal static string FulfilConstructionBuyOrder(NetPlayer by, int id, int orderIndex, int amount)
        {
            var record = Find(id);
            var materials = ProcurementMaterials(record);
            if (materials == null) return "That construction project is not accepting deliveries.";
            if (!NetGame.IsServer || by == null || !by.IsSpawned ||
                !NetProfiles.Enabled || !NetProfiles.Controls(by.OwnerClientId))
                return "Use a controlled profile to sell materials into a construction order.";
            if (!record.underConstruction && (!by.InHangar || by.Station != record.stationIndex))
                return "Dock at the station commissioning this module to sell its materials.";
            string locationError = ConstructionDeliveryError(by, record);
            if (locationError != null) return locationError;
            if (orderIndex < 0 || record.buyOrders == null || orderIndex >= record.buyOrders.Count)
                return "No construction buy order exists at that index.";
            var order = record.buyOrders[orderIndex];
            if (order == null || order.remaining < 1 || order.unitPrice < 1 || !NetGuard.Item(order.item) ||
                order.unitPrice > long.MaxValue / order.remaining ||
                order.reservedCredits < order.unitPrice * (long)order.remaining)
                return "That construction buy order is invalid.";
            if (amount < 1 || amount > 500 || amount > order.remaining) return "Choose 1–500 units, up to the order's remaining quantity.";
            var material = materials.Find(x => x != null && x.item == order.item);
            if (material == null || amount > material.required - material.delivered)
                return "The project no longer needs that quantity of this material.";
            string seller = NetProfiles.AccountOf(by.OwnerClientId);
            int cargo = NetProfiles.CargoAmount(seller, order.item);
            long total = order.unitPrice * amount, balance = NetProfiles.CreditAmount(seller);
            if (string.IsNullOrEmpty(seller) || cargo < amount) return "Your saved profile does not have enough of that material.";
            if (total > int.MaxValue || balance < 0 || balance > long.MaxValue - total) return "Your profile cannot receive this payment.";

            var oldOrders = new List<PlayerStationBuyOrder>();
            foreach (var pending in record.buyOrders)
                oldOrders.Add(pending == null ? null : new PlayerStationBuyOrder { item = pending.item, remaining = pending.remaining,
                    unitPrice = pending.unitPrice, reservedCredits = pending.reservedCredits, fundedByProfile = pending.fundedByProfile,
                    fundedByFactionId = pending.fundedByFactionId, createdBy = pending.createdBy, createdUtc = pending.createdUtc });
            int oldDelivered = material.delivered;
            Action rollbackCompletion = null;
            material.delivered += amount;
            order.remaining -= amount;
            order.reservedCredits -= total;
            if (order.remaining == 0) record.buyOrders.RemoveAt(orderIndex);
            bool stationProject = record.underConstruction;
            bool complete = true;
            foreach (var resource in materials)
                if (resource != null && resource.delivered < resource.required) { complete = false; break; }
            var credits = new List<(string account, long delta)> { (seller, total) };
            var banks = new List<(string factionId, long delta)>();
            var profileRefunds = new List<(string account, long amount)>();
            if (complete)
            {
                if (!StageConstructionEscrowRefunds(record.buyOrders, credits, banks, profileRefunds))
                {
                    material.delivered = oldDelivered;
                    record.buyOrders = oldOrders;
                    return "An outstanding buy order has invalid escrow. The order was preserved for administrator recovery.";
                }
                rollbackCompletion = stationProject ? CompleteStationConstruction(record) :
                    CompleteModuleConstruction(record, record.moduleConstructionKind);
                record.buyOrders.Clear();
            }
            if (!CommitProfileTransaction(new List<(string account, int item, int delta)> { (seller, order.item, -amount) }, credits, banks))
            {
                material.delivered = oldDelivered;
                rollbackCompletion?.Invoke();
                record.buyOrders = oldOrders;
                return "The sale could not be journalled; no cargo or credits were transferred.";
            }
            if (complete && stationProject) Apply(NetGame.Db);

            if (!(NetGame.PersistentHost && by.OwnerClientId == NetGame.LocalId))
                NetState.Instance?.SendAdmin(by.OwnerClientId, NetAdmin.Order.Take, order.item, amount, 0, "", "Construction buy order");
            NotifyProfileCreditChange(seller, total, "Construction buy order");
            foreach (var refund in profileRefunds)
                NotifyProfileCreditChange(refund.account, refund.amount, "Construction buy order escrow refund");
            return $"Sold {amount} x {NetGame.Db?.Item(order.item)?.name ?? ("Item " + order.item)} into construction order #{orderIndex} for {total:N0} credits." +
                (complete ? " Construction is complete; remaining order escrow was refunded." : " Materials were added to the construction project.");
        }

        static bool StageConstructionEscrowRefunds(List<PlayerStationBuyOrder> orders,
            List<(string account, long delta)> credits, List<(string factionId, long delta)> banks,
            List<(string account, long amount)> profileRefunds)
        {
            if (orders == null) return true;
            foreach (var order in orders)
            {
                if (order == null || order.reservedCredits == 0) continue;
                bool personal = !string.IsNullOrEmpty(order.fundedByProfile);
                bool faction = !string.IsNullOrEmpty(order.fundedByFactionId);
                if (order.reservedCredits < 0 || personal == faction) return false;
                if (personal)
                {
                    credits.Add((order.fundedByProfile, order.reservedCredits));
                    profileRefunds.Add((order.fundedByProfile, order.reservedCredits));
                }
                else banks.Add((order.fundedByFactionId, order.reservedCredits));
            }
            return true;
        }

        internal static string CancelConstructionBuyOrder(NetPlayer by, int id, int orderIndex)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (record.offeredForClaimUtc > 0) return "Construction orders are paused until the station is claimed.";
            if (!NetGame.IsServer || by == null || !by.IsSpawned || !by.InHangar ||
                !NetProfiles.Enabled || !NetProfiles.Controls(by.OwnerClientId))
                return "Dock at any station using a controlled profile to cancel a construction order.";
            if (orderIndex < 0 || record.buyOrders == null || orderIndex >= record.buyOrders.Count)
                return "No construction buy order exists at that index.";
            string account = NetProfiles.AccountOf(by.OwnerClientId);
            if (record.ownershipType == PlayerStationOwnershipType.Faction
                ? !IsFactionManager(record, account)
                : !string.Equals(account, record.ownerProfile, StringComparison.Ordinal))
                return "Only the station owner, or the owning faction's leader/officer, may cancel this order.";
            var order = record.buyOrders[orderIndex];
            if (order == null || order.reservedCredits < 0)
                return "That order has invalid escrow and cannot be cancelled automatically.";

            // A positive escrow balance must have exactly one recorded funding identity. Legacy orders
            // without one (or malformed orders naming both) stay intact for administrator recovery.
            bool hasProfileFunder = !string.IsNullOrEmpty(order.fundedByProfile);
            bool hasFactionFunder = !string.IsNullOrEmpty(order.fundedByFactionId);
            if (order.reservedCredits > 0 && hasProfileFunder == hasFactionFunder)
                return "This order's escrow owner cannot be identified safely. The order and its escrow were preserved for administrator recovery.";

            record.buyOrders.RemoveAt(orderIndex);
            var credits = hasProfileFunder && order.reservedCredits > 0
                ? new List<(string account, long delta)> { (order.fundedByProfile, order.reservedCredits) } : null;
            var banks = hasFactionFunder && order.reservedCredits > 0
                ? new List<(string factionId, long delta)> { (order.fundedByFactionId, order.reservedCredits) } : null;
            if (!CommitProfileTransaction(null, credits, banks))
            {
                record.buyOrders.Insert(orderIndex, order);
                return "The cancellation could not be journalled; the order and escrow remain active.";
            }
            if (!string.IsNullOrEmpty(order.fundedByProfile) && order.reservedCredits > 0)
                NotifyProfileCreditChange(order.fundedByProfile, order.reservedCredits, "Construction buy order refund");
            return $"Construction buy order cancelled. {order.reservedCredits:N0} unspent escrow credits were returned.";
        }


        internal static string SetMarketplaceTax(NetPlayer by, int id, int percent)
        {
            var station = Find(id);
            if (percent < 0 || percent > 10) return "Choose a marketplace tax from 0% to 10%.";
            if (!IsStationContext(by, station, out string account) || !IsOwnershipAuthority(station, account))
                return "Dock at the station as its owner or owning faction leader to set marketplace tax.";
            if (station.marketplaceTaxPercent == percent) return $"Marketplace tax is already {percent}%.";
            int previous = station.marketplaceTaxPercent;
            station.marketplaceTaxPercent = percent;
            if (Save()) return $"Marketplace tax is now {percent}%.";
            station.marketplaceTaxPercent = previous;
            return "The new tax rate could not be saved.";
        }

        internal static string MarketStatus(int id)
        {
            if (!NetGame.Active) return "Join a multiplayer session first.";
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (record.offeredForClaimUtc > 0) return "This facility is closed until the station is claimed.";
            var lines = new List<string> { "Marketplace at " + record.name + ":",
                $"Sales tax: {Math.Clamp(record.marketplaceTaxPercent, 0, 10)}% added at checkout." };
            if (record.buyOrders != null && record.buyOrders.Count > 0)
            {
                lines.Add("Construction buy orders:");
                for (int i = 0; i < record.buyOrders.Count; i++)
                {
                    var order = record.buyOrders[i];
                    if (order == null || order.remaining <= 0) continue;
                    string name = NetGame.Db?.Item(order.item)?.name ?? ("Item " + order.item);
                    lines.Add($"Buy #{i}: {name} x{order.remaining:N0} at {order.unitPrice:N0} each ({order.reservedCredits:N0} credits escrowed)");
                }
            }
            AddShipMarketplaceListings(record, lines);
            if ((record.listings?.Count ?? 0) == 0 && (record.shipListings?.Count ?? 0) == 0)
                lines.Add("No active listings.");
            for (int i = 0; i < (record.listings?.Count ?? 0); i++)
            {
                var listing = record.listings[i];
                if (listing == null || listing.amount <= 0) continue;
                string itemName = NetGame.Db?.Item(listing.item)?.name ?? ("Item " + listing.item);
                lines.Add($"#{i}: {itemName} x{listing.amount:N0} — {listing.unitPrice:N0} credits each" +
                    (!string.IsNullOrEmpty(listing.payoutFactionId) ? " (faction treasury)" : " (seller)"));
            }
            return string.Join("\n", lines);
        }

        internal static string CreateListing(NetPlayer by, int id, int item, int amount, long unitPrice, bool payFactionTreasury = false)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!CanManageStorage(by, record, out string account))
                return "Dock at this station using an authorised profile to list your own stored goods.";
            if (record.underConstruction) return "Finish station construction before opening listings.";
            if (StorageCapacity(record) <= 0) return "Install a storage module before listing station goods.";
            if (!NetGuard.Item(item) || !Hangar.IsSaleable(item) || amount < 1 || amount > 500 || unitPrice < 1 || unitPrice > MaxStationUnitPrice)
                return "Use a saleable item, 1–500 units, and a unit price from 1 to 1,000,000,000 credits.";
            record.listings ??= new List<PlayerStationListing>();
            if (record.listings.Count >= MaxStationListings) return "This station has reached its listing limit.";
            NormalizeItemListingIds(record);
            if (record.nextItemListingId >= int.MaxValue) return "The item listing ID limit has been reached.";
            string payoutFactionId = null;
            if (payFactionTreasury)
            {
                var sellerFaction = NetFactions.Of(account);
                if (sellerFaction == null) return "Join a faction before directing listing proceeds to its treasury.";
                payoutFactionId = sellerFaction.id;
            }
            record.storage ??= new List<PlayerStationStorage>();
            EnsureStorageLots(record);
            var stock = record.storage.Find(x => x != null && x.item == item);
            if (stock == null || stock.amount < amount) return "The station does not have enough stock to escrow that listing.";
            if (OwnedStorageAmount(record, account, item) < amount)
                return "Those goods belong to another contributor; only their owner can list them.";

            var previousLots = CloneStorageLots(record);
            RemoveStorageLots(record, account, item, amount);
            stock.amount -= amount;
            if (stock.amount == 0) record.storage.Remove(stock);
            int listingId = record.nextItemListingId++;
            record.listings.Add(new PlayerStationListing { id = listingId, item = item, amount = amount, unitPrice = unitPrice, sellerProfile = account, payoutFactionId = payoutFactionId });
            if (!Save())
            {
                record.listings.RemoveAt(record.listings.Count - 1);
                record.nextItemListingId = listingId;
                record.storageLots = previousLots;
                if (stock.amount == 0) record.storage.Add(stock);
                stock.amount += amount;
                return "The listing could not be saved; the goods were returned to storage where possible.";
            }
            return "Listing #" + (record.listings.Count - 1) + " created. " + amount + " units are held in marketplace escrow.";
        }

        static void NormalizeItemListingIds(PlayerStationRecord station)
        {
            if (station?.listings == null) return;
            int next = Math.Max(1, station.nextItemListingId);
            foreach (var listing in station.listings)
                if (listing != null && listing.id >= next) next = listing.id == int.MaxValue ? int.MaxValue : listing.id + 1;
            var used = new HashSet<int>();
            foreach (var listing in station.listings)
            {
                if (listing == null) continue;
                if (listing.id > 0 && used.Add(listing.id)) continue;
                if (next < int.MaxValue) { listing.id = next++; used.Add(listing.id); }
                else listing.id = 0;
            }
            station.nextItemListingId = next;
        }

        internal static string BuyItemListingById(NetPlayer by, int stationId, int listingId, int amount, bool cancel)
        {
            var station = Find(stationId);
            if (listingId <= 0) return "Choose an active item listing.";
            int index = station?.listings?.FindIndex(listing => listing != null && listing.id == listingId) ?? -1;
            if (index < 0) return "That listing is no longer available. Refresh the marketplace.";
            return cancel ? CancelListing(by, stationId, index) : BuyListing(by, stationId, index, amount);
        }

        [Serializable] internal sealed class MarketUiRow
        {
            public int id, item, amount;
            public long price;
            public bool hull, mine;
        }
        [Serializable] internal sealed class MarketUiCatalogue
        {
            public int stationId, page, pageCount;
            public List<MarketUiRow> rows = new List<MarketUiRow>();
        }
        internal static MarketUiCatalogue MarketCatalogueFor(NetPlayer pilot, int stationId, int page)
        {
            var result = new MarketUiCatalogue { stationId = stationId };
            var station = Find(stationId);
            if (!IsStationContext(pilot, station, out string account) || station.underConstruction) return result;
            if (station.listings != null)
                foreach (var listing in station.listings)
                    if (listing != null && listing.id > 0 && listing.amount > 0)
                        result.rows.Add(new MarketUiRow { id = listing.id, item = listing.item, amount = listing.amount,
                            price = listing.unitPrice, mine = string.Equals(account, listing.sellerProfile, StringComparison.Ordinal) });
            if (station.shipListings != null)
                foreach (var listing in station.shipListings)
                    if (listing?.hull != null && listing.id > 0)
                        result.rows.Add(new MarketUiRow { id = listing.id, item = listing.hull.ship, amount = 1,
                            price = listing.price, hull = true, mine = string.Equals(account, listing.sellerProfile, StringComparison.Ordinal) });
            const int pageSize = 64;
            result.pageCount = Math.Max(1, (result.rows.Count + pageSize - 1) / pageSize);
            result.page = Math.Clamp(page, 0, result.pageCount - 1);
            int start = result.page * pageSize;
            result.rows = result.rows.GetRange(start, Math.Min(pageSize, result.rows.Count - start));
            return result;
        }

        internal static string BuyListing(NetPlayer by, int id, int listingIndex, int amount)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (record.offeredForClaimUtc > 0) return "This facility is closed until the station is claimed.";
            if (!IsStationContext(by, record, out string buyer))
                return "Dock at this station with visitor permission and use a controlled player profile to buy goods.";
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

            string seller = listing.sellerProfile;
            if (string.IsNullOrEmpty(buyer)) return "Your player profile is unavailable.";
            if (string.Equals(buyer, seller, StringComparison.Ordinal))
                return "You cannot purchase your own listing.";
            if (listing.unitPrice > int.MaxValue / (long)amount)
                return "That purchase exceeds the marketplace's per-transaction credit limit.";
            long total = listing.unitPrice * amount;
            long tax = (total * Math.Clamp(record.marketplaceTaxPercent, 0, 10) + 99) / 100;
            long charge = total + tax;
            if (charge > int.MaxValue) return "The purchase including tax exceeds the per-transaction credit limit.";
            long credits = NetProfiles.CreditAmount(buyer);
            if (credits < charge) return "Your saved profile does not have enough credits for the purchase and tax.";
            int cargo = NetProfiles.CargoAmount(buyer, listing.item);
            if (cargo < 0 || (long)cargo + amount > int.MaxValue)
                return "Your saved cargo could not accept this purchase.";
            // Inventory limits are not the same as physical hold capacity.
            // Validate before touching escrow, credits, or the station registry.
            if (NetProfiles.WreckSalvageCargoRoom(by, buyer) < amount)
                return "Your ship does not have enough free cargo space for this purchase.";

            bool removeListing = amount == listing.amount;
            if (removeListing) record.listings.RemoveAt(listingIndex);
            else listing.amount -= amount;
            bool paidToFaction = !string.IsNullOrEmpty(listing.payoutFactionId);
            var payments = new List<(string account, long delta)> { (buyer, -charge) };
            var banks = new List<(string factionId, long delta)>();
            if (paidToFaction) banks.Add((listing.payoutFactionId, total));
            else payments.Add((seller, total));
            bool factionTax = record.ownershipType == PlayerStationOwnershipType.Faction;
            if (tax > 0)
            {
                if (factionTax) banks.Add((record.ownerFactionId, tax));
                else payments.Add((record.ownerProfile, tax));
            }
            bool committed = CommitProfileTransaction(
                new List<(string account, int item, int delta)> { (buyer, listing.item, amount) }, payments, banks);
            if (!committed)
            {
                if (removeListing) record.listings.Insert(listingIndex, listing);
                else listing.amount += amount;
                return "The purchase could not be journalled; no credits or goods were transferred.";
            }

            bool buyerIsHost = NetGame.PersistentHost && by.OwnerClientId == NetGame.LocalId;
            if (!buyerIsHost)
            {
                NetState.Instance?.SendAdmin(by.OwnerClientId, NetAdmin.Order.Credits,
                    -(int)(charge - (!factionTax && record.ownerProfile == buyer ? tax : 0)), 0, 0, "", "Station marketplace");
                NetState.Instance?.SendAdmin(by.OwnerClientId, NetAdmin.Order.Give, listing.item, amount, 0, "", "Station marketplace");
            }

            foreach (ulong client in (paidToFaction ? new List<ulong>() : NetProfiles.ClientsOf(seller)))
            {
                if (!NetProfiles.Controls(client)) continue;
                if (!(NetGame.PersistentHost && client == NetGame.LocalId))
                    NetState.Instance?.SendAdmin(client, NetAdmin.Order.Credits, (int)total, 0, 0, "", "Station marketplace");
                break;
            }

            if (tax > 0 && !factionTax && record.ownerProfile != buyer)
                NotifyProfileCreditChange(record.ownerProfile, tax, "Station marketplace tax");
            string itemName = NetGame.Db?.Item(listing.item)?.name ?? ("Item " + listing.item);
            return "Purchased " + amount + " x " + itemName + " for " + charge.ToString("N0") +
                (tax > 0 ? $" credits including {tax:N0} tax" : " credits") +
                " from " + record.name + (paidToFaction ? "; proceeds credited to faction treasury." : "; proceeds credited to the seller.") ;
        }

        internal static string CancelListing(NetPlayer by, int id, int listingIndex)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (record.offeredForClaimUtc > 0) return "This facility is closed until the station is claimed.";
            if (!IsStationContext(by, record, out string account))
                return "Dock at this station using an authorised controlled profile to cancel a listing.";
            if (listingIndex < 0 || record.listings == null || listingIndex >= record.listings.Count)
                return "No listing exists at that index.";
            var listing = record.listings[listingIndex];
            if (listing == null || !string.Equals(listing.sellerProfile, account, StringComparison.Ordinal))
                return "Only the original seller can cancel and reclaim this listing.";
            if (listing.amount <= 0 || !NetGuard.Item(listing.item))
                return "That listing is invalid and cannot be returned automatically.";
            EnsureStorageLots(record);
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

            var previousLots = CloneStorageLots(record);
            if (stock == null) record.storage.Add(stock = new PlayerStationStorage { item = listing.item });
            stock.amount += listing.amount;
            AddStorageLot(record, account, listing.item, listing.amount);
            record.listings.RemoveAt(listingIndex);
            if (!Save())
            {
                stock.amount -= listing.amount;
                record.storageLots = previousLots;
                if (stock.amount == 0) record.storage.Remove(stock);
                record.listings.Insert(listingIndex, listing);
                return "The cancellation could not be saved; the listing remains active.";
            }
            return "Listing cancelled and " + listing.amount + " units returned to station storage.";
        }
    }
}
