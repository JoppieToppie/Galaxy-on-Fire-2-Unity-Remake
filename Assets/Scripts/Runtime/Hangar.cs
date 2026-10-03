// Hangar.cs
// One opening of the station's Hangar window (HangarWindow 0x171630) as plain C#: prices for this station, buying and
// selling one unit at a time, mounting / demounting, and buying ships. Works on Session (credits, cargo, mounted
// equipment, ship) and the station's StationStock. Rules: Reference/research/shop.md sections 3, 5, 6.
//   Item::transaction 0xf4074        buy needs stock and credits (no cargo check); sell pays the same price; anything
//                                    sold joins the station's stock
//   HangarWindow::mountItem 0x178824 first free slot of the item's type; secondaries move the whole stack (ammo)
//   HangarWindow::selectItem         one-per-ship categories swap instead (text 287)
//   autoEquipSecondaryWeapons 0x1761c4  bought missiles of a mounted type join the mounted stack
//   HangarWindow::selectItem / OnTouchEnd (ships): trade-in at full price, equipment moves to the new ship's slots,
//                                    the rest to cargo; the dealer then sells your old ship
// Kaamo Club (Reference/research/kaamo_club.md 6): at the owned club (storage mode, HangarWindow+0x11d) transfers are
// free (Item::transaction(.., free = true)); stored hulls can be used (332 / 333: cargo and equipment move over, the old
// hull takes the row) or sold (330 / 334, the ship's price, mods add nothing). Buying a ship elsewhere while owning the
// club can keep the old hull (327 -> 331 Keep: 328 when that type is already stored, else the full price).

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GoF2Remake.Data
{
    public class Hangar
    {
        public enum Result { Ok, NoStock, NoCredits, NothingToSell, NoFreeSlot, Swap, NotMountable, SameShip, NotSaleable, Passengers, AlreadyStored }

        readonly Database db;
        readonly Dictionary<int, int> prices = new Dictionary<int, int>();
        public readonly int Station, SystemIndex;
        public readonly StationStock Stock;

        public Hangar(Database db, StationStock stock)
        {
            this.db = db;
            Stock = stock;
            Station = stock.station;
            SystemIndex = Shop.SystemOf(db, Station);
            // HangarWindow::initialize -> Status::calcCargoPrices: each list priced with its own Random(station); the
            // station's price wins in the merged list (Item::mixItems).
            AddPrices(Session.Equipment.Select(e => e.item).ToList());
            AddPrices(Session.Cargo.Select(e => e.item).ToList());
            AddPrices(stock.items.Select(e => e.item).ToList());
            RecordKnownPrices();
        }

        void AddPrices(List<int> items)
        {
            var p = Shop.PriceList(db, Station, items);
            for (int i = 0; i < items.Count; i++) prices[items[i]] = p[i];
        }

        /// <summary>Status+0x3c..0x48: the lowest and highest price seen per item (shown in the item details); not in the
        /// black market (HangarWindow::initialize: Loma's maximum prices aren't recorded).</summary>
        void RecordKnownPrices()
        {
            if (SystemIndex == 25) return;
            foreach (var kv in prices)
            {
                if (!Session.LowestKnownPrice.TryGetValue(kv.Key, out var lo) || kv.Value < lo.price)
                    Session.LowestKnownPrice[kv.Key] = (kv.Value, SystemIndex);
                if (!Session.HighestKnownPrice.TryGetValue(kv.Key, out var hi) || kv.Value > hi.price)
                    Session.HighestKnownPrice[kv.Key] = (kv.Value, SystemIndex);
            }
        }

        // ---- queries -----------------------------------------------------------------------------------------

        public ShipData Ship => db.Ship(Session.ShipIndex);
        /// <summary>HangarWindow+0x11d: the owned Kaamo Club's storage (free transfers, no prices).</summary>
        public bool Storage => KaamoClub.StorageAt(Station);
        public int PriceOf(int item)
        {
            // An item that joined the list after this opening (multiplayer: another player's sale) gets its price now.
            if (!prices.ContainsKey(item)) AddPrices(new List<int> { item });
            return Story.AdjustPrice(Station, item, prices.TryGetValue(item, out int p) ? p : 0);
        }
        /// <summary>Item::isSaleable: story items (Gunant's Drill, the Alien Remains...) can't be sold or demounted (323).</summary>
        public static bool IsSaleable(int item) => !Session.Unsaleable.Contains(item);
        public int StockOf(int item) => Stock.items.Where(s => s.item == item).Sum(s => s.amount);
        public int CargoOf(int item) => Session.Cargo.Where(s => s.item == item).Sum(s => s.amount);
        public bool IsMounted(int item) => Session.Equipment.Any(e => e.item == item);
        /// <summary>Units of 'item' mounted: a secondary's stack is its remaining ammo.</summary>
        public int MountedOf(int item) => Session.Equipment.Where(e => e.item == item).Sum(e => Mathf.Max(1, e.amount));

        /// <summary>Ship::getCurrentLoad: every unit in cargo weighs 1 t; mounted items weigh nothing.</summary>
        public int Load => Shop.CargoLoad();

        /// <summary>Ship::getMaxLoad: base cargo + (int)(base * sum of mounted compression (attr 22) % / 100). No ship mods yet.</summary>
        public int MaxLoad => Shop.MaxLoad(db);

        public bool Overloaded => Load > MaxLoad;

        /// <summary>Item::mixItems: everything in cargo or in stock, by item index.</summary>
        public List<int> ShopItems() =>
            Session.Cargo.Where(s => s.amount > 0).Select(s => s.item)
                .Concat(Stock.items.Where(s => s.amount > 0).Select(s => s.item)).Distinct().OrderBy(i => i).ToList();

        public int SlotCount(int type)
        {
            var s = Ship?.slots;
            if (s == null) return 0;
            return type switch { 0 => s.primary, 1 => s.secondary, 2 => s.turret, 3 => s.equipment + (Session.HasMod(2) ? 1 : 0), _ => 0 };   // mod 2: +1 equipment slot
        }

        public int TypeOf(int item) => db.Item(item)?.TypeId ?? 4;

        /// <summary>Indices into Session.Equipment of the items mounted in slots of this type, in slot order.</summary>
        public List<int> MountedOfType(int type)
        {
            var list = new List<int>();
            for (int i = 0; i < Session.Equipment.Count; i++) if (TypeOf(Session.Equipment[i].item) == type) list.Add(i);
            return list;
        }

        // ---- trading -------------------------------------------------------------------------------------------

        /// <summary>Buy one unit (Item::transaction(true)). 'need' = missing credits on NoCredits.</summary>
        public Result Buy(int item, out int need)
        {
            need = 0;
            var row = Stock.items.Find(s => s.item == item && s.amount > 0);
            if (row == null) return Result.NoStock;
            int price = Storage || Cheats.FreeShopping ? 0 : PriceOf(item);
            if (Session.Credits < price) { need = price - Session.Credits; return Result.NoCredits; }
            row.amount--;
            if (row.amount <= 0) Stock.items.Remove(row);
            if (!Storage) GoF2Remake.Multiplayer.NetStock.ItemChanged(Station, item, -1, price);   // multiplayer: the shared stock
            AddToCargo(item, 1);
            if (price > 0) ChangeCredits(-price);
            Session.SeenItems.Add(item);
            if (Session.IsBooze(item)) Session.BoozeTypes.Add(item);   // HangarWindow::transaction: Status+0xac
            return Result.Ok;
        }

        /// <summary>Sell one unit (Item::transaction(false)): same price as buying, the unit joins the station's stock.</summary>
        public Result Sell(int item)
        {
            var stack = Session.Cargo.Find(s => s.item == item && s.amount > 0);
            if (stack == null) return Result.NothingToSell;
            if (!IsSaleable(item)) return Result.NotSaleable;
            stack.amount--;
            if (stack.amount <= 0) Session.Cargo.Remove(stack);
            var row = Stock.items.Find(s => s.item == item);
            if (row != null) row.amount++;
            else
            {
                int at = Stock.items.FindIndex(s => s.item > item);
                Stock.items.Insert(at < 0 ? Stock.items.Count : at, new ItemStack(item, 1));   // the stock stays in index order
            }
            if (!Storage) GoF2Remake.Multiplayer.NetStock.ItemChanged(Station, item, 1);   // multiplayer: the shared stock
            if (!Storage) ChangeCredits(PriceOf(item));
            Session.SeenItems.Add(item);
            if (Session.IsBooze(item)) Session.BoozeTypes.Add(item);   // HangarWindow::selectItem: a committed booze trade
            return Result.Ok;
        }

        /// <summary>Status::changeCredits: ignores absurd changes, never below 0.</summary>
        static void ChangeCredits(int delta)
        {
            if (Mathf.Abs(delta) > 1000000000) return;
            Session.Credits = Mathf.Max(0, Session.Credits + delta);
        }

        static void AddToCargo(int item, int amount) => Shop.AddToCargo(item, amount);

        // ---- mounting ------------------------------------------------------------------------------------------

        /// <summary>Can a cargo item be mounted? Swap: a one-per-ship item of the same category is mounted at 'swapWith'.</summary>
        public Result CanMount(int item, out int swapWith)
        {
            swapWith = -1;
            var it = db.Item(item);
            if (it == null || CargoOf(item) <= 0) return Result.NothingToSell;
            int type = it.TypeId;
            if (type > 3) return Result.NotMountable;
            if (!Shop.CanInstallMultiple(it.categoryId))
            {
                swapWith = Session.Equipment.FindIndex(e => db.Item(e.item)?.categoryId == it.categoryId);
                if (swapWith >= 0) return Result.Swap;
            }
            return MountedOfType(type).Count < SlotCount(type) ? Result.Ok : Result.NoFreeSlot;
        }

        /// <summary>mountItem: secondaries move the whole stack (the amount is the ammo), everything else one unit.</summary>
        public bool Mount(int item)
        {
            if (CanMount(item, out _) != Result.Ok) return false;
            MountFromCargo(item);
            return true;
        }

        /// <summary>Text 287: demount the mounted one-per-ship item and mount the cargo one.</summary>
        public bool Swap(int equipmentIndex, int item)
        {
            if (equipmentIndex < 0 || equipmentIndex >= Session.Equipment.Count || CargoOf(item) <= 0) return false;
            Demount(equipmentIndex);
            MountFromCargo(item);
            return true;
        }

        void MountFromCargo(int item)
        {
            var stack = Session.Cargo.Find(s => s.item == item);
            int amount = TypeOf(item) == 1 ? stack.amount : 1;
            stack.amount -= amount;
            if (stack.amount <= 0) Session.Cargo.Remove(stack);
            Session.Equipment.Add(new ItemStack(item, amount));
            Session.SeenItems.Add(item);
        }

        /// <summary>demountItem 0x178674: to cargo, secondaries with their ammo.</summary>
        public void Demount(int equipmentIndex)
        {
            if (equipmentIndex < 0 || equipmentIndex >= Session.Equipment.Count) return;
            var e = Session.Equipment[equipmentIndex];
            Session.Equipment.RemoveAt(equipmentIndex);
            AddToCargo(e.item, Mathf.Max(1, e.amount));
        }

        /// <summary>autoEquipSecondaryWeapons: bought missiles of a mounted type join the mounted stack. Returns the items moved.</summary>
        public List<int> AutoEquipSecondaries()
        {
            var moved = new List<int>();
            foreach (var stack in Session.Cargo.ToList())
            {
                if (TypeOf(stack.item) != 1) continue;
                var mounted = Session.Equipment.Find(e => e.item == stack.item);
                if (mounted == null) continue;
                mounted.amount += stack.amount;
                Session.Cargo.Remove(stack);
                moved.Add(stack.item);
            }
            return moved;
        }

        // ---- ships ---------------------------------------------------------------------------------------------

        public int ShipPrice(int ship) => Shop.ShipPrice(db, ship, Station);

        /// <summary>HangarWindow::selectItem (ship row): 'need' = missing credits after trading in the current ship.</summary>
        public Result CanBuyShip(int ship, out int need)
        {
            need = 0;
            if (Session.Passengers > 0) return Result.Passengers;   // 336
            if (ship == Session.ShipIndex) return Result.SameShip;
            int cost = Cheats.FreeShopping ? 0 : ShipPrice(ship) - ShipPrice(Session.ShipIndex);
            if (Session.Credits < cost) { need = cost - Session.Credits; return Result.NoCredits; }
            return Result.Ok;
        }

        /// <summary>Trade-in: credits += current - new; mounted items move to the first free slot of their type in the new
        /// ship (Ship::addEquipment), the rest to cargo; the dealer's row becomes the old ship. Mods belong to the hull
        /// (HangarWindow::OnTouchEnd 0x176d94): the new ship gets the bought row's mods, the row that takes the old ship gets
        /// the old ship's, so a ship sold and bought back keeps its Kaamo upgrades.</summary>
        public bool BuyShip(int ship)
        {
            if (CanBuyShip(ship, out _) != Result.Ok) return false;
            int old = Session.ShipIndex;
            var oldMods = new List<int>(Session.ShipMods ?? new List<int>());
            if (!Cheats.FreeShopping) ChangeCredits(ShipPrice(old) - ShipPrice(ship));
            SwitchTo(ship, Stock.TakeMods(ship));
            int row = Stock.ships.IndexOf(ship);
            if (row >= 0) Stock.ships[row] = old; else Stock.ships.Add(old);
            Stock.PutMods(old, oldMods);
            GoF2Remake.Multiplayer.NetStock.ShipChanged(Station, ship, old);   // multiplayer: the shared dealer list
            return true;
        }

        /// <summary>The new hull becomes the flown ship: every mounted item moves to the first free slot of its type (in
        /// slot order, secondaries with their ammo), the rest to the hold; the cargo stays with the player.</summary>
        void SwitchTo(int ship, List<int> mods)
        {
            var mounted = Session.Equipment;
            Session.ShipIndex = ship;
            Session.ShipMods = mods != null ? new List<int>(mods) : new List<int>();
            Session.Equipment = new List<ItemStack>();
            foreach (var e in mounted)
            {
                int type = TypeOf(e.item);
                if (MountedOfType(type).Count < SlotCount(type)) Session.Equipment.Add(e);
                else AddToCargo(e.item, Mathf.Max(1, e.amount));
            }
        }

        // ---- remake: a lounge seller's ship (AgentOffer.SellShip) ---------------------------------------------------
        // Like the dealer's trade-in, at the seller's 'price': the seller keeps the old hull (no dealer row takes it, and
        // its mods go with it); with the Kaamo Club owned "Keep" sends it there and the new ship costs the full price.

        public Result CanBuyShipFor(int ship, int price, out int need)
        {
            need = 0;
            if (Session.Passengers > 0) return Result.Passengers;   // 336
            if (ship == Session.ShipIndex) return Result.SameShip;
            int cost = Cheats.FreeShopping ? 0 : price - ShipPrice(Session.ShipIndex);
            if (Session.Credits < cost) { need = cost - Session.Credits; return Result.NoCredits; }
            return Result.Ok;
        }

        public bool BuyShipFor(int ship, int price)
        {
            if (CanBuyShipFor(ship, price, out _) != Result.Ok) return false;
            if (!Cheats.FreeShopping) ChangeCredits(ShipPrice(Session.ShipIndex) - price);
            SwitchTo(ship, null);
            return true;
        }

        public Result CanKeepAndBuyShipFor(int ship, int price, out int need)
        {
            need = 0;
            if (Session.Passengers > 0) return Result.Passengers;
            if (ship == Session.ShipIndex) return Result.SameShip;
            if (KaamoClub.HasShip(Session.ShipIndex)) return Result.AlreadyStored;   // 328
            int cost = Cheats.FreeShopping ? 0 : price;
            if (Session.Credits < cost) { need = cost - Session.Credits; return Result.NoCredits; }
            return Result.Ok;
        }

        public bool KeepAndBuyShipFor(int ship, int price)
        {
            if (CanKeepAndBuyShipFor(ship, price, out _) != Result.Ok) return false;
            int old = Session.ShipIndex;
            var oldMods = Session.ShipMods;
            if (!Cheats.FreeShopping) ChangeCredits(-price);
            SwitchTo(ship, null);
            KaamoClub.Store(old, 0, oldMods);
            return true;
        }

        // ---- Kaamo Club ------------------------------------------------------------------------------------------

        /// <summary>327 -> 331 "Keep": the old hull goes to the club, the new one costs its full price.</summary>
        public Result CanKeepAndBuyShip(int ship, out int need)
        {
            need = 0;
            if (KaamoClub.HasShip(Session.ShipIndex)) return Result.AlreadyStored;   // 328 (the old ship's type)
            int cost = Cheats.FreeShopping ? 0 : ShipPrice(ship);
            if (Session.Credits < cost) { need = cost - Session.Credits; return Result.NoCredits; }
            return Result.Ok;
        }

        public bool KeepAndBuyShip(int ship)
        {
            if (CanKeepAndBuyShip(ship, out _) != Result.Ok) return false;
            int old = Session.ShipIndex;
            var oldMods = Session.ShipMods;
            if (!Cheats.FreeShopping) ChangeCredits(-ShipPrice(ship));
            SwitchTo(ship, Stock.TakeMods(ship));   // the bought row's mods (OnTouchEnd: getMods of the row, both branches)
            Stock.ships.Remove(ship);   // the bought row is gone
            GoF2Remake.Multiplayer.NetStock.ShipChanged(Station, ship, -1);
            KaamoClub.Store(old, 0, oldMods);   // a bare hull (makeShip(old) + its mods; Ship::clone resets the race to 0)
            return true;
        }

        /// <summary>Row button 1 "Use" (332): 336 with passengers, 329 for the same type, else 333 asks.</summary>
        public Result CanUseStored(int index)
        {
            if (index < 0 || index >= Session.KaamoShips.Count) return Result.NoStock;
            if (Session.Passengers > 0) return Result.Passengers;
            if (Session.KaamoShips[index].ship == Session.ShipIndex) return Result.SameShip;
            return Result.Ok;
        }

        /// <summary>333 -> Yes: the stored hull (its own mods) becomes the flown ship; the old hull takes its row.</summary>
        public bool UseStored(int index)
        {
            if (CanUseStored(index) != Result.Ok) return false;
            var stored = Session.KaamoShips[index];
            var old = new StoredShip(Session.ShipIndex, 0, Session.ShipMods);
            SwitchTo(stored.ship, stored.mods);
            Session.KaamoShips[index] = old;
            return true;
        }

        /// <summary>The stored hull's sell value (ListItem::getPrice = the adjusted Ship::getPrice; mods add nothing).</summary>
        public int StoredPrice(int index) => index >= 0 && index < Session.KaamoShips.Count ? ShipPrice(Session.KaamoShips[index].ship) : 0;

        /// <summary>Row button 10 "Sell" (330) -> 334 -> Yes.</summary>
        public bool SellStored(int index)
        {
            if (index < 0 || index >= Session.KaamoShips.Count) return false;
            ChangeCredits(StoredPrice(index));
            Session.KaamoShips.RemoveAt(index);
            return true;
        }
    }
}
