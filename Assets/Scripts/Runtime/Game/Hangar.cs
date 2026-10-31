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
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
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
            // HangarWindow::initialize -> Status::calcCargoPrices: each list priced (the station's price wins in the merged
            // list, Item::mixItems); remake: an item's price no longer depends on the list (Shop.PriceList).
            AddPrices(Session.Equipment.Select(e => e.item).ToList());
            AddPrices(Session.Cargo.Select(e => e.item).ToList());
            AddPrices(stock.items.Select(e => e.item).ToList());
            PinPrices();
            RecordKnownPrices();
        }

        // Remake: an item's price is fixed for the docking. Each list's prices come from a fresh Random(station), so an
        // item's price depends on its place in the lists, and a trade moving it between the stock and the hold could
        // shift it by up to +-2 %: in multiplayer the window is priced again after every trade (the host's shared stock),
        // and buying at 724 and selling at 746 at once made money with every click. The first price an item gets at
        // this docking is kept (NewDocking clears them; another station starts afresh).
        static readonly Dictionary<int, int> pinned = new Dictionary<int, int>();
        static int pinnedStation = -1;

        /// <summary>A new docking (StationLevel): its prices are worked out afresh.</summary>
        public static void NewDocking()
        {
            pinned.Clear();
            pinnedStation = -1;
        }

        void PinPrices()
        {
            if (pinnedStation != Station) { pinned.Clear(); pinnedStation = Station; }
            foreach (var item in prices.Keys.ToList())
            {
                if (pinned.TryGetValue(item, out int p)) prices[item] = p;
                else pinned[item] = prices[item];
            }
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
            if (!prices.ContainsKey(item)) { AddPrices(new List<int> { item }); PinPrices(); }
            return Story.AdjustPrice(Station, item, prices.TryGetValue(item, out int p) ? p : 0);
        }
        /// <summary>What buying one unit costs: 0 while the station has free units of it (StationStock.freeUnits), else
        /// PriceOf. Selling stays at PriceOf.</summary>
        public int BuyPriceOf(int item) => !Storage && Stock.FreeOf(item) > 0 ? 0 : PriceOf(item);

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

        /// <summary>Item::mixItems: everything in cargo or in stock, by item index. Remake: an item number the item table
        /// doesn't have is left out (logged once): the window reads every row's item, and one unknown number in the hold
        /// or the stock threw on every trade (crash reports from a Windows build, 2026-10-12, source not found yet).</summary>
        public List<int> ShopItems() =>
            Session.Cargo.Where(s => s.amount > 0).Select(s => s.item)
                .Concat(Stock.items.Where(s => s.amount > 0).Select(s => s.item)).Distinct().Where(Known).OrderBy(i => i).ToList();

        static readonly HashSet<int> reportedUnknown = new HashSet<int>();

        /// <summary>The item is in the item table; else false, with a warning the first time (where it sits).</summary>
        bool Known(int item)
        {
            if (db.Item(item) != null) return true;
            if (reportedUnknown.Add(item))
                Debug.LogWarning($"Hangar: item {item} (hold {CargoOf(item)} t, station {Station} stock {StockOf(item)} t) " +
                                 $"isn't in the item table of {db.Items.Count}: left out of the shop");
            return false;
        }

        public int SlotCount(int type)
        {
            var s = Modding.KitRules.Slots(Ship);   // a kit ship's build decides some (ModShipKits)
            if (s == null) return 0;
            // mod 2: +1 equipment slot per level; the cloak bay (remake option) holds a mounted cloak besides the slots
            return type switch { 0 => s.primary, 1 => s.secondary, 2 => s.turret, 3 => s.equipment + Session.ModLevel(2) + BaySlot(db, Session.ShipIndex, Session.Equipment), _ => 0 };
        }

        /// <summary>Remake option (Settings.CloakBay): the Specter (44) and the Scimitar (49) have the U'tool built in
        /// (Ship::hasCloakIntegrated, PlayerCloak), as has a mod ship with "integrated": "cloak"; a cloak mounted on them goes
        /// into that bay, replacing it, instead of taking one of the equipment slots (the original: a slot like any
        /// equipment).</summary>
        public static bool HasCloakBay(int ship) => Database.Shared.IntegratedCloak(ship) >= 0 && Settings.CloakBay;

        static bool IsCloak(Database db, int item) => db.Item(item)?.categoryId == 21;

        /// <summary>The bay's extra equipment place: 1 while a cloak is mounted on a ship with a cloak bay.</summary>
        public static int BaySlot(Database db, int ship, List<ItemStack> equipment) =>
            HasCloakBay(ship) && equipment != null && equipment.Exists(e => IsCloak(db, e.item)) ? 1 : 0;

        /// <summary>'item' goes into the empty cloak bay of 'ship' (a cloak, the bay free).</summary>
        public static bool BayTakes(Database db, int ship, int item, List<ItemStack> equipment) =>
            HasCloakBay(ship) && IsCloak(db, item) && BaySlot(db, ship, equipment) == 0;

        public int TypeOf(int item) => db.Item(item)?.TypeId ?? 4;

        /// <summary>The units a mounted stack is worth in the hold or a store: a secondary its rounds (none when it is empty:
        /// HangarWindow::demountItem), anything else one. Every place that moves mounted items off the ship goes through it
        /// (a player's video: an AMR Extinctor fired empty, demounted into the hold as 1 t and mounted again, over and over;
        /// #132 fixed the demount, the club's storage, a hull swap and the story's ship moves still made 1 of 0).</summary>
        public static int UnitsOf(Database db, ItemStack e) =>
            e == null ? 0 : (db?.Item(e.item)?.TypeId ?? 4) == 1 ? Mathf.Max(0, e.amount) : Mathf.Max(1, e.amount);

        /// <summary>Remake: the flown ship's equipment fitted to its slots on docking (StationLevel). A mod lowering a ship's
        /// slots, or a debug hull swap (PlayerHull), could leave more mounted than it has slots, and in flight each mounted
        /// weapon gets a gun (WeaponSystem reuses the mounts): what is past a type's slots goes to the hold, in slot order,
        /// like a bought ship's (SwitchTo); the story's unsaleable items keep their place. Returns how many moved.</summary>
        public static int FitToSlots(Database db)
        {
            var s = Modding.KitRules.Slots(db.Ship(Session.ShipIndex));   // a kit ship's build decides some (ModShipKits)
            if (s == null) return 0;
            int Slots(int type) => type switch { 0 => s.primary, 1 => s.secondary, 2 => s.turret, 3 => s.equipment + Session.ModLevel(2) + BaySlot(db, Session.ShipIndex, Session.Equipment), _ => int.MaxValue };
            int TypeOfItem(int item) => db.Item(item)?.TypeId ?? 4;
            var used = new int[5];
            foreach (var e in Session.Equipment) if (TakesSlot(db, e) && !IsSaleable(e.item)) used[Mathf.Clamp(TypeOfItem(e.item), 0, 4)]++;
            var keep = new List<ItemStack>();
            int moved = 0;
            foreach (var e in Session.Equipment)
            {
                int type = Mathf.Clamp(TypeOfItem(e.item), 0, 4);
                if (!TakesSlot(db, e)) { keep.Add(e); continue; }   // built in, or in a built-in one's place: no slot (SyncBuiltIn)
                if (!IsSaleable(e.item) || used[type] < Slots(type))
                {
                    if (IsSaleable(e.item)) used[type]++;
                    keep.Add(e);
                }
                else { int n = UnitsOf(db, e); if (n > 0) Shop.AddToCargo(e.item, n); moved++; }
            }
            if (moved > 0) Session.Equipment = keep;
            return moved;
        }

        /// <summary>Indices into Session.Equipment of the items mounted in slots of this type, in slot order.</summary>
        public List<int> MountedOfType(int type)
        {
            var list = new List<int>();
            for (int i = 0; i < Session.Equipment.Count; i++)
                if (TakesSlot(db, Session.Equipment[i]) && TypeOf(Session.Equipment[i].item) == type) list.Add(i);
            return list;
        }

        /// <summary>Indices into Session.Equipment of the flown ship's built-in items and of the fitted items in their places
        /// (InBuiltInPlace), in the order of the ship's list.</summary>
        public List<int> BuiltInStacks()
        {
            var list = new List<int>();
            for (int i = 0; i < Session.Equipment.Count; i++)
                if (Session.Equipment[i].builtIn || InBuiltInPlace(db, Session.Equipment[i])) list.Add(i);
            return list;
        }

        /// <summary>Remake mods: a built-in item a fitted one can take over from, like the Specter's cloak (the cloak bay):
        /// one-per-ship equipment (a shield, armor, booster, scanner, drill, cloak...). Built-in weapons and turrets add to
        /// the ship's slots instead.</summary>
        public static bool Replaceable(ItemData it) => it != null && it.TypeId == 3 && !Shop.CanInstallMultiple(Shop.KindOf(it));

        /// <summary>'e' is a fitted item in the place of one of the flown ship's built-in items (the same one-per-ship kind):
        /// it takes no slot, and the built-in one steps aside while it is there (SyncBuiltIn).</summary>
        public static bool InBuiltInPlace(Database db, ItemStack e)
        {
            if (e == null || e.builtIn || db == null) return false;
            var it = db.Item(e.item);
            if (!Replaceable(it)) return false;
            foreach (var b in db.BuiltInItems(Session.ShipIndex)) if (Shop.KindOf(db.Item(b.item)) == Shop.KindOf(it)) return true;
            return false;
        }

        /// <summary>Whether a mounted stack takes one of the ship's slots: not a built-in one, nor a fitted one in a built-in
        /// one's place.</summary>
        public static bool TakesSlot(Database db, ItemStack e) => e != null && !e.builtIn && !InBuiltInPlace(db, e);

        /// <summary>Remake mods: the flown ship's built-in items (ships.json "integrated": "items", Database.BuiltInItems) as
        /// built-in stacks first in Session.Equipment, so everything that reads the mounted items (stats, guns, devices) has
        /// them, while they take no slot (MountedOfType, FitToSlots) and can't be demounted, sold, stored or carried to another
        /// ship (SwitchTo, EquipmentToStore). A built-in stack of another ship goes, a missing one comes (a secondary with its
        /// starting ammo; one kept keeps what is left, the shops' missiles of its type join it); so an item used up (an
        /// emergency system) is back at the next docking. Called where the flown ship can change and as levels start.
        /// Returns whether anything changed.</summary>
        public static bool SyncBuiltIn(Database db)
        {
            if (db == null || Session.Equipment == null) return false;
            var want = db.BuiltInItems(Session.ShipIndex);
            bool changed = false;
            var matched = new bool[want.Count];
            // A fitted item of the same one-per-ship kind has taken a built-in one's place: that one steps aside (counted as
            // matched, so it isn't added; one present goes below).
            var replaced = new bool[want.Count];
            for (int j = 0; j < want.Count; j++)
            {
                var it = db.Item(want[j].item);
                replaced[j] = Replaceable(it) && Session.Equipment.Exists(e => !e.builtIn && Shop.KindOf(db.Item(e.item)) == Shop.KindOf(it));
            }
            for (int i = Session.Equipment.Count - 1; i >= 0; i--)
            {
                var e = Session.Equipment[i];
                if (!e.builtIn) continue;
                int k = -1;
                for (int j = 0; j < want.Count; j++) if (!matched[j] && want[j].item == e.item) { k = j; break; }
                if (k >= 0 && !replaced[k]) matched[k] = true;
                else { Session.Equipment.RemoveAt(i); changed = true; }
            }
            int at = 0;
            while (at < Session.Equipment.Count && Session.Equipment[at].builtIn) at++;
            for (int j = 0; j < want.Count; j++)
            {
                if (matched[j] || replaced[j]) continue;
                Session.Equipment.Insert(at++, new ItemStack(want[j].item, want[j].amount) { builtIn = true });
                changed = true;
            }
            if (changed) Session.SelectedSecondary = -1;
            return changed;
        }

        // ---- trading -------------------------------------------------------------------------------------------

        // A batch (BeginBatch / EndBatch: a shop's Buy all / Sell all): its units reach the multiplayer host as one message.
        bool batching;
        int batchItem = -1, batchDelta, batchPrice;

        /// <summary>The trades until EndBatch are of one item, one way: the shared stock hears of them as one change.</summary>
        public void BeginBatch() { batching = true; batchItem = -1; batchDelta = 0; batchPrice = 0; }

        public void EndBatch()
        {
            batching = false;
            if (batchItem >= 0 && batchDelta != 0) GameHooks.StockItemChanged?.Invoke(Station, batchItem, batchDelta, batchPrice);
            batchItem = -1;
            batchDelta = 0;
        }

        /// <summary>A unit bought (-1, at 'price') or sold (+1) for the shared stock: at once, or into the batch.</summary>
        void Shared(int item, int delta, int price)
        {
            if (!batching) { GameHooks.StockItemChanged?.Invoke(Station, item, delta, price); return; }
            if (batchItem >= 0 && batchItem != item) { EndBatch(); batching = true; }   // (never in a shop's batch: one item)
            batchItem = item;
            batchDelta += delta;
            batchPrice = price;
        }

        /// <summary>Buy one unit (Item::transaction(true)). 'need' = missing credits on NoCredits.</summary>
        public Result Buy(int item, out int need)
        {
            need = 0;
            var row = Stock.items.Find(s => s.item == item && s.amount > 0);
            if (row == null) return Result.NoStock;
            bool free = !Storage && Stock.FreeOf(item) > 0;   // step 67's gifted mines (StationStock.freeUnits)
            int price = Storage || Cheats.FreeShopping || free ? 0 : PriceOf(item);
            if (GameHooks.FactionBuyPrice != null && !free) price = GameHooks.FactionBuyPrice(Station, price);   // multiplayer: a faction station's cut / tax
            if (Session.Credits < price) { need = price - Session.Credits; return Result.NoCredits; }
            row.amount--;
            if (row.amount <= 0) Stock.items.Remove(row);
            if (free) Stock.TakeFree(item);
            if (!Storage) Shared(item, -1, price);   // multiplayer: the shared stock
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
            if (!Storage) Shared(item, 1, 0);   // multiplayer: the shared stock
            if (!Storage) ChangeCredits(GameHooks.FactionSellPrice != null ? GameHooks.FactionSellPrice(Station, PriceOf(item)) : PriceOf(item));   // multiplayer: no more than a member pays
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
            // A one-per-ship item the ship has built in (a shield, a scanner...): it takes the built-in one's place, no slot
            // (SyncBuiltIn sets the built-in one aside); one fitted there already is swapped as usual.
            if (InBuiltInPlace(db, new ItemStack(item, 1)))
            {
                swapWith = Session.Equipment.FindIndex(e => !e.builtIn && Shop.KindOf(db.Item(e.item)) == Shop.KindOf(it));
                return swapWith >= 0 ? Result.Swap : Result.Ok;
            }
            if (!Shop.CanInstallMultiple(Shop.KindOf(it)))
            {
                // Remake: turrets (8) and plasma collectors (35) are one per turret slot, not one per ship, so a ship with
                // two turret slots (a custom ship) takes a second one; the swap only once every turret slot is taken.
                bool perSlot = type == 2 && SlotCount(2) > 1 && MountedOfType(2).Count < SlotCount(2);
                swapWith = perSlot ? -1 : Session.Equipment.FindIndex(e => !e.builtIn && Shop.KindOf(db.Item(e.item)) == Shop.KindOf(it));
                if (swapWith >= 0) return Result.Swap;
            }
            if (BayTakes(db, Session.ShipIndex, item, Session.Equipment)) return Result.Ok;   // the cloak bay (remake option)
            return MountedOfType(type).Count < SlotCount(type) ? Result.Ok : Result.NoFreeSlot;
        }

        /// <summary>Remake (a player's suggestion): after a blueprint run finished at its production station, whether to ask
        /// "Mount it now?": only when the product can go into a free slot (not a swap, not a commodity; a secondary of a
        /// mounted type already joined that stack in Blueprints.Produce, so none is left in the hold).</summary>
        public bool OffersMountAfterBuild(int item) => CanMount(item, out _) == Result.Ok;

        /// <summary>mountItem: secondaries move the whole stack (the amount is the ammo), everything else one unit.</summary>
        public bool Mount(int item)
        {
            if (CanMount(item, out _) != Result.Ok) return false;
            MountFromCargo(item);
            SyncBuiltIn(db);   // in a built-in one's place: that one steps aside
            return true;
        }

        /// <summary>Text 287: demount the mounted one-per-ship item and mount the cargo one.</summary>
        public bool Swap(int equipmentIndex, int item)
        {
            if (equipmentIndex < 0 || equipmentIndex >= Session.Equipment.Count || CargoOf(item) <= 0 || Session.Equipment[equipmentIndex].builtIn) return false;
            Demount(equipmentIndex);
            MountFromCargo(item);
            SyncBuiltIn(db);
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
            if (e.builtIn) return;   // built into the ship (SyncBuiltIn)
            Session.Equipment.RemoveAt(equipmentIndex);
            // HangarWindow::demountItem: a secondary goes with its ammo, nothing when it is empty (#132: Max(1, ...) handed
            // out a free missile); other items as 1 unit.
            int amount = UnitsOf(db, e);
            if (amount > 0) AddToCargo(e.item, amount);
            SyncBuiltIn(db);   // a fitted item in a built-in one's place gone: the built-in one is back
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

        /// <summary>What a dealer ship costs here (multiplayer: a faction station's VAT markup for non-members); ShipPrice is
        /// its value (a trade-in, the club's storage).</summary>
        public int BuyShipPrice(int ship) => CronusGift(ship) ? 0
            : Storage ? ShipPrice(ship) : (GameHooks.FactionShipBuyPrice != null ? GameHooks.FactionShipBuyPrice(Station, ShipPrice(ship)) : ShipPrice(ship));

        /// <summary>#122: Khador's Cronus at Kothar during step 77, once (Session.KhadorCronusGift; ModStation::OnTouchEnd
        /// 0xea4ec's makeShip(37, 0)). Its value (ShipPrice, a later trade-in) stays the table's, as the original rebuilds the
        /// flown ship with makeShip(-1).</summary>
        bool CronusGift(int ship) => ship == 37 && Station == 100 && !Storage && Session.KhadorCronusGift
                                     && !Session.FreePlay && Session.CampaignMission == 77;

        /// <summary>HangarWindow::selectItem (ship row): 'need' = missing credits after trading in the current ship.</summary>
        public Result CanBuyShip(int ship, out int need)
        {
            need = 0;
            if (Session.Passengers > 0) return Result.Passengers;   // 336
            if (ship == Session.ShipIndex) return Result.SameShip;
            int cost = Cheats.FreeShopping ? 0 : BuyShipPrice(ship) - ShipPrice(Session.ShipIndex);
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
            if (!Cheats.FreeShopping) ChangeCredits(ShipPrice(old) - BuyShipPrice(ship));
            if (CronusGift(ship)) Session.KhadorCronusGift = false;   // the gift is taken: a row of it later is priced
            SwitchTo(ship, Stock.TakeMods(ship), dismount: true);
            int row = Stock.ships.IndexOf(ship);
            if (row >= 0) Stock.ships[row] = old; else Stock.ships.Add(old);
            Stock.PutMods(old, oldMods);
            GameHooks.StockShipChanged?.Invoke(Station, ship, old);   // multiplayer: the shared dealer list
            return true;
        }

        /// <summary>Remake mods: the ship blueprints' ships finished for this station (Blueprints.TakeBuiltShips), taken like
        /// bought ships: the old hull goes to the Kaamo Club when the club is owned and has none of its type, else it is traded
        /// in at its price into the dealer list; a skin's blueprint ("requiresShip", the hull flown) rebuilds the old hull
        /// instead (its Kaamo upgrades stay). The message for the player, null when none was waiting.</summary>
        public string DeliverBuiltShips()
        {
            var built = Blueprints.TakeBuiltShips(Station);
            if (built.Count == 0) return null;
            var text = new System.Text.StringBuilder();
            foreach (int deed in built)
            {
                int ship = Modding.ModBlueprints.ShipOf(deed);
                if (ship < 0 || db.Ship(ship) == null) continue;
                var bp = Modding.ModBlueprints.Of(deed);
                int old = Session.ShipIndex;
                var oldMods = new List<int>(Session.ShipMods ?? new List<int>());
                string oldName = GameNames.Ship(old);
                bool rebuilt = bp != null && bp.requiresShip >= 0 && bp.requiresShip == old;
                string how;
                if (rebuilt) how = string.Format(Localization.Extra("bpShipRebuilt", "Your {0} was rebuilt into it."), oldName);
                else if (KaamoClub.Owned && !KaamoClub.HasShip(old))
                {
                    KaamoClub.Store(old, 0, oldMods, EquipmentToStore());
                    how = string.Format(Localization.Extra("bpShipStored", "Your {0} is parked in the Kaamo Club."), oldName);
                }
                else
                {
                    int price = ShipPrice(old);
                    ChangeCredits(price);
                    if (!Stock.ships.Contains(old)) Stock.ships.Add(old);
                    Stock.PutMods(old, oldMods);
                    how = string.Format(Localization.Extra("bpShipTradedIn", "Your {0} was traded in for {1}."), oldName, GameNames.Credits(price));
                }
                SwitchTo(ship, rebuilt ? oldMods : null, dismount: !rebuilt);
                if (text.Length > 0) text.Append("\n\n");
                text.Append(string.Format(Localization.Extra("bpShipReady", "Your new {0} is ready in the hangar."), GameNames.Ship(ship))).Append(' ').Append(how);
            }
            return text.Length > 0 ? text.ToString() : null;
        }

        /// <summary>Remake (Settings.KaamoKeepsEquipment, players' suggestion): a hull going into the Kaamo Club keeps what is
        /// mounted on it; this takes it off the flown ship (the new hull then starts bare, or with what its own storage row
        /// kept). Null with the option off: the items move over as in the original. The story's unsaleable items (the jump
        /// drive, a mission's gear) never stay behind: they stay mounted and move to the new hull.</summary>
        static List<ItemStack> EquipmentToStore(bool moveToNewShip = false)
        {
            if (moveToNewShip || !Settings.KaamoKeepsEquipment) return null;
            var kept = Session.Equipment.Where(e => !e.builtIn && IsSaleable(e.item)).ToList();   // built-ins stay with the hull's type
            Session.Equipment = Session.Equipment.Where(e => !e.builtIn && !IsSaleable(e.item)).ToList();
            return kept;
        }

        /// <summary>Remake (#83): "Keep" asks whether the equipment moves to the new ship (Yes) or stays on the old hull in
        /// the club (No): only while the option keeps it there and something the player may move is mounted.</summary>
        public static bool KeepAsksAboutEquipment => Settings.KaamoKeepsEquipment && Session.Equipment.Any(e => !e.builtIn && IsSaleable(e.item));

        /// <summary>The new hull becomes the flown ship: every mounted item moves to the first free slot of its type (in
        /// slot order, secondaries with their ammo), the rest to the hold; the cargo stays with the player. 'mount' = the
        /// items to put on it instead of the ones mounted now (a stored hull's own, KaamoKeepsEquipment). Remake (players'
        /// report): 'dismount' = a bought hull (dealer, lounge seller, Kaamo "Keep", a blueprint's ship) starts bare: the
        /// saleable items go to the hold (secondaries with their ammo) for the player to mount again or sell; the story's
        /// unsaleable items stay mounted (they can't be demounted, 323). The original moves them onto the new hull, which
        /// single player keeps: the bare hull is a multiplayer session's rule only.</summary>
        void SwitchTo(int ship, List<int> mods, List<ItemStack> mount = null, bool dismount = false)
        {
            dismount &= NetSession.Active;
            var mounted = (mount ?? Session.Equipment).Where(e => !e.builtIn).ToList();   // the old hull's built-ins stay with it
            Session.ShipIndex = ship;
            Session.ShipMods = mods != null ? new List<int>(mods) : new List<int>();
            Session.Equipment = new List<ItemStack>();
            Session.SelectedSecondary = -1;
            foreach (var e in mounted)
            {
                int type = TypeOf(e.item);
                if ((!dismount || !IsSaleable(e.item)) && (MountedOfType(type).Count < SlotCount(type) || BayTakes(db, ship, e.item, Session.Equipment))) Session.Equipment.Add(e);
                else if (UnitsOf(db, e) > 0) AddToCargo(e.item, UnitsOf(db, e));
            }
            SyncBuiltIn(db);   // the new hull's own
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
            SwitchTo(ship, null, dismount: true);
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

        public bool KeepAndBuyShipFor(int ship, int price, bool moveEquipment = false)
        {
            if (CanKeepAndBuyShipFor(ship, price, out _) != Result.Ok) return false;
            int old = Session.ShipIndex;
            var oldMods = Session.ShipMods;
            if (!Cheats.FreeShopping) ChangeCredits(-price);
            var kept = EquipmentToStore(moveEquipment);
            SwitchTo(ship, null, dismount: !moveEquipment);
            KaamoClub.Store(old, 0, oldMods, kept);
            return true;
        }

        // ---- Kaamo Club ------------------------------------------------------------------------------------------

        /// <summary>327 -> 331 "Keep": the old hull goes to the club, the new one costs its full price.</summary>
        public Result CanKeepAndBuyShip(int ship, out int need)
        {
            need = 0;
            if (KaamoClub.HasShip(Session.ShipIndex)) return Result.AlreadyStored;   // 328 (the old ship's type)
            int cost = Cheats.FreeShopping ? 0 : BuyShipPrice(ship);
            if (Session.Credits < cost) { need = cost - Session.Credits; return Result.NoCredits; }
            return Result.Ok;
        }

        public bool KeepAndBuyShip(int ship, bool moveEquipment = false)
        {
            if (CanKeepAndBuyShip(ship, out _) != Result.Ok) return false;
            int old = Session.ShipIndex;
            var oldMods = Session.ShipMods;
            if (!Cheats.FreeShopping) ChangeCredits(-BuyShipPrice(ship));
            if (CronusGift(ship)) Session.KhadorCronusGift = false;
            var kept = EquipmentToStore(moveEquipment);
            SwitchTo(ship, Stock.TakeMods(ship), dismount: !moveEquipment);   // the bought row's mods (OnTouchEnd: getMods of the row, both branches)
            Stock.ships.Remove(ship);   // the bought row is gone
            GameHooks.StockShipChanged?.Invoke(Station, ship, -1);
            // A bare hull (makeShip(old) + its mods; Ship::clone resets the race to 0), or with its items (KaamoKeepsEquipment).
            KaamoClub.Store(old, 0, oldMods, kept);
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

        /// <summary>333 -> Yes: the stored hull (its own mods) becomes the flown ship; the old hull takes its row. With
        /// KaamoKeepsEquipment each hull keeps its own items (the old one's stay on it in storage, the stored one's are
        /// mounted); without it the mounted items move over (the original) and a stored hull's items go to the storage.</summary>
        public bool UseStored(int index)
        {
            if (CanUseStored(index) != Result.Ok) return false;
            var stored = Session.KaamoShips[index];
            var storedGear = stored.equipment ?? new List<ItemStack>();
            var kept = EquipmentToStore();
            var old = new StoredShip(Session.ShipIndex, 0, Session.ShipMods, kept);
            if (kept != null) SwitchTo(stored.ship, stored.mods, Session.Equipment.Concat(storedGear).ToList());   // the story's items first
            else
            {
                SwitchTo(stored.ship, stored.mods);
                KaamoClub.AddToStorage(storedGear);
            }
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
            KaamoClub.AddToStorage(Session.KaamoShips[index].equipment);   // remake: its items stay in the club
            Session.KaamoShips.RemoveAt(index);
            return true;
        }
    }
}
