// Inventory.cs
// Plain data for what the player and the stations own (the original's Item amount fields and Station objects):
//   ItemStack         an item and an amount: a cargo stack, a mounted item (secondaries: amount = ammo), a stock row
//   StationStock  one station's shop stock, ships for sale and bar agents, kept while the station is among the last 3
//                     visited
//                     (Status+0x19c stack, Status::addStationToStack 0xb6140)
//   StoredShip        a hull parked at the Kaamo Club (Station::addShip on the storage: index, race, mods; no equipment)

using System;
using System.Collections.Generic;

namespace GoF2Remake.Data
{
    [Serializable]
    public class ItemStack
    {
        public int item;
        public int amount;
        /// <summary>Remake mods: built into the flown ship (ships.json "integrated": "items", Hangar.SyncBuiltIn): it works
        /// like a mounted item but takes no slot and can't be demounted, sold, stored or moved to another ship.</summary>
        public bool builtIn;
        /// <summary>A mounted stack's place among its type's slots, 1-based (Ship::getSlotPos + 1); 0 = none yet: it takes
        /// the first free one (old saves, starting gear). Hangar.SlotPositions reads it, Hangar.PinSlots writes it.</summary>
        public int slot;

        public ItemStack(int item, int amount) { this.item = item; this.amount = amount; }
        public ItemStack Clone() => new ItemStack(item, amount) { builtIn = builtIn, slot = slot };
    }

    [Serializable]
    public class StationStock
    {
        public int station;
        public List<ItemStack> items = new List<ItemStack>();   // Generator::getItemBuyList, in item index order
        public List<int> ships = new List<int>();               // Generator::getShipBuyList
        public List<Agent> agents = new List<Agent>();  // Generator::createAgents (the bar's visitors)
        /// <summary>Units of a row that cost nothing (Item::makeItem(amount, 0): step 67's mines from Cornelius; calcCargoPrices
        /// leaves a 0 price alone). Only the gifted units: a sold unit is priced as usual (in the original it joined the free
        /// row, an endless free-money loop). Bought first (Hangar.BuyPriceOf).</summary>
        public List<ItemStack> freeUnits = new List<ItemStack>();

        /// <summary>The free units of 'item' still in stock.</summary>
        public int FreeOf(int item)
        {
            var f = freeUnits?.Find(x => x != null && x.item == item);
            if (f == null || f.amount <= 0) return 0;
            int stock = 0;
            foreach (var s in items) if (s.item == item) stock += s.amount;
            return System.Math.Min(f.amount, stock);
        }

        /// <summary>One free unit of 'item' bought.</summary>
        public void TakeFree(int item)
        {
            var f = freeUnits?.Find(x => x != null && x.item == item);
            if (f == null) return;
            if (--f.amount <= 0) freeUnits.Remove(f);
        }

        /// <summary>The Kaamo mods of dealer rows that carry any: a traded-in ship keeps its mods in the row that takes it
        /// (HangarWindow::OnTouchEnd 0x176d94 adds the old ship's mods to the new row's Ship), and buying that row gives them
        /// to the new ship (getMods of the bought row). One entry per modded row, by ship index (the rows are ship indexes);
        /// entries whose ship has left the list are dropped (PruneShipMods).</summary>
        public List<StoredShip> shipMods = new List<StoredShip>();

        /// <summary>The mods of the dealer's 'ship' row (empty when it has none).</summary>
        public List<int> ModsOf(int ship)
        {
            var m = ships != null && ships.Contains(ship) ? shipMods?.Find(x => x != null && x.ship == ship) : null;
            return m != null && m.mods != null ? m.mods : new List<int>();
        }

        /// <summary>The 'ship' row is bought: its mods go with it.</summary>
        public List<int> TakeMods(int ship)
        {
            var mods = new List<int>(ModsOf(ship));
            int i = shipMods != null ? shipMods.FindIndex(x => x != null && x.ship == ship) : -1;
            if (i >= 0) shipMods.RemoveAt(i);
            return mods;
        }

        /// <summary>A traded-in 'ship' takes a row with its mods.</summary>
        public void PutMods(int ship, List<int> mods)
        {
            if (mods == null || mods.Count == 0) return;
            if (shipMods == null) shipMods = new List<StoredShip>();
            shipMods.Add(new StoredShip(ship, 0, mods));
        }

        /// <summary>The dealer list changed outside a trade (a story step, the multiplayer host's list): mods of rows gone go.</summary>
        public void PruneShipMods()
        {
            if (shipMods == null) { shipMods = new List<StoredShip>(); return; }
            shipMods.RemoveAll(x => x == null || ships == null || !ships.Contains(x.ship));
        }
    }

    /// <summary>Status+0x8c: the player's own ship while the story lends another (steps 48-54, 56-57): hull type, equipment,
    /// cargo, mods and damage, all given back together.</summary>
    [Serializable]
    public class ParkedShip
    {
        public int ship;
        public List<ItemStack> equipment = new List<ItemStack>(), cargo = new List<ItemStack>();
        public List<int> mods = new List<int>();
        public int hull = -1, armor = -1;
        public float shield = -1f;
        public string lentBy = "";   // remake mods: the event graph whose "lend" step lent the loaner ("": the story)
        public bool lentKeep;        // ... with "keep": the items aboard the loaner are the player's and go back with this ship
    }

    [Serializable]
    public class StoredShip
    {
        public int ship;
        public int race;
        public List<int> mods = new List<int>();
        /// <summary>Remake (Settings.KaamoKeepsEquipment): the items left mounted on the stored hull, secondaries with their
        /// ammo; null or empty = a bare hull (the original's, and saves from before).</summary>
        public List<ItemStack> equipment = new List<ItemStack>();

        public StoredShip(int ship, int race, List<int> mods, List<ItemStack> equipment = null)
        {
            this.ship = ship;
            this.race = race;
            this.mods = mods != null ? new List<int>(mods) : new List<int>();
            this.equipment = equipment != null ? new List<ItemStack>(equipment) : new List<ItemStack>();
        }
    }
}
