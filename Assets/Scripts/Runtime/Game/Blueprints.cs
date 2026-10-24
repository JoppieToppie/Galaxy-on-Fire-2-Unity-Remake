// Blueprints.cs
// Blueprints (BluePrint 0x1a6044..0x1a6550, Status+0x18 / +0x1c; Reference/research/blueprints_mods.md 1). Plain C#:
//   the table        one blueprint per item with ingredients (items.json 'blueprint', 25 products), keyed by the product's
//                    item index; base quantity 10 for secondaries, else 1
//   state            remaining amount per ingredient, money spent (= "started"), times produced, the production station
//                    (Session.Blueprints); unlocked set Session.UnlockedBlueprints
//   Invest           BluePrint::addItem: remaining -= n (first matching slot), money spent += single price * n, the first
//                    investment fixes the production station
//   CompletionRate   the mean over the ingredients of invested / total (each ingredient weighs the same)
//   AutoCompletePrice int(baseQuantity * maxPrice * 1.25); Chromo Plasma (210): 2 000 000 + the remaining ingredients' value
//   Produce          complete: at the production station the product goes to the hold (211; secondaries join a mounted
//                    stack), elsewhere it waits there (210, Status::addPendingProduct); then reset (times produced +1,
//                    goods produced +1)
//   CollectPending   ModStation::checkPendingProducts 0xee258: products waiting at this station move to the hold (213)
//   Shipping         adding at another station than the production station costs 200 $ per unit (288); volatile 204 Red
//                    Plasma and 209 K'mirkk Toad Mutagen can't be shipped (289); 210 / 223 can't be started in a system
//                    without gate routes (528)
//   UnlockFromStory  the campaign's unlocks with their pre-invested ingredients (steps 34, 58, 72, 104, 141)

using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Data
{
    [Serializable]
    public class BlueprintState
    {
        public int item;
        public List<int> remaining = new List<int>();
        public int moneySpent, timesProduced, station = -1;
    }

    [Serializable]
    public class PendingProduct
    {
        public int item, quantity, station;
    }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class Blueprints
    {
        public const int ShippingPerUnit = 200;
        static readonly HashSet<int> Volatile = new HashSet<int> { 204, 209 };
        static readonly HashSet<int> NeedsRoutes = new HashSet<int> { 210, 223 };

        /// <summary>Every product (items with ingredients) in item-index order (Status::resetGame).</summary>
        public static List<ItemData> Products(Database db) => db.Items.FindAll(i => i.blueprint != null && i.blueprint.Count > 0);

        public static bool IsUnlocked(int product) => Session.UnlockedBlueprints.Contains(product);
        public static void Unlock(int product) => Session.UnlockedBlueprints.Add(product);

        /// <summary>BluePrint+0x20: 10 per run for secondaries, else 1.</summary>
        public static int BaseQuantity(ItemData product) => product != null && product.TypeId == 1 ? 10 : 1;

        public static bool IsVolatile(int item) => Volatile.Contains(item);

        /// <summary>The blueprint's state (created untouched on first use).</summary>
        public static BlueprintState State(Database db, int product)
        {
            var s = Session.Blueprints.Find(b => b.item == product);
            if (s != null) { Fit(db, s); return s; }
            s = new BlueprintState { item = product };
            var it = db.Item(product);
            if (it != null) foreach (var part in it.blueprint) s.remaining.Add(part.amount);
            Session.Blueprints.Add(s);
            return s;
        }

        /// <summary>Remake: the state's per-ingredient list made as long as the product's recipe (a new ingredient still
        /// needs its whole amount, one the recipe dropped goes). A state saved under another recipe (the Default economy's
        /// recipes, a mod's, one made while the product was unknown and so left empty) was shorter, and the Blueprints tab's
        /// details read past its end (an Android crash report, 2026-10-11).</summary>
        public static void Fit(Database db, BlueprintState s)
        {
            var parts = db.Item(s.item)?.blueprint;
            if (parts == null || s.remaining.Count == parts.Count) return;
            while (s.remaining.Count > parts.Count) s.remaining.RemoveAt(s.remaining.Count - 1);
            for (int k = s.remaining.Count; k < parts.Count; k++) s.remaining.Add(parts[k].amount);
        }

        public static bool IsEmpty(BlueprintState s) => s.moneySpent == 0;
        public static bool IsCompleted(BlueprintState s) => s.remaining.TrueForAll(r => r <= 0);

        public static int Total(Database db, int product, int slot)
        {
            var parts = db.Item(product)?.blueprint;
            return parts != null && slot >= 0 && slot < parts.Count ? parts[slot].amount : 0;
        }

        public static int Invested(Database db, BlueprintState s, int slot)
        {
            Fit(db, s);
            return slot >= 0 && slot < s.remaining.Count ? Total(db, s.item, slot) - s.remaining[slot] : 0;
        }

        /// <summary>BluePrint::getCompletionRate: mean of invested / total per ingredient (0..1).</summary>
        public static float CompletionRate(Database db, BlueprintState s)
        {
            var it = db.Item(s.item);
            if (it == null || it.blueprint.Count == 0) return 0f;
            Fit(db, s);
            float sum = 0f;
            for (int k = 0; k < it.blueprint.Count; k++)
            {
                int total = it.blueprint[k].amount;
                if (total > 0) sum += (total - s.remaining[k]) / (float)total;
            }
            return sum / it.blueprint.Count;
        }

        /// <summary>HangarWindow::refreshCargoAvailabilityForBlueprints: an ingredient still needed is in the hold.</summary>
        public static bool CargoHelps(Database db, BlueprintState s)
        {
            var it = db.Item(s.item);
            if (it == null) return false;
            Fit(db, s);
            for (int k = 0; k < it.blueprint.Count; k++)
                if (s.remaining[k] > 0 && Shop.CargoOf(it.blueprint[k].item) > 0) return true;
            return false;
        }

        /// <summary>BluePrint::reset on every started blueprint whose production station is 'station' (index 77 -> 78: what
        /// was invested on Valkyrie is lost).</summary>
        public static void ResetAtStation(Database db, int station)
        {
            foreach (var s in Session.Blueprints)
            {
                if (IsEmpty(s) || s.station != station) continue;
                var it = db.Item(s.item);
                s.remaining.Clear();
                if (it != null) foreach (var part in it.blueprint) s.remaining.Add(part.amount);
                s.moneySpent = 0;
                s.station = -1;
            }
        }

        /// <summary>BluePrint::addItem(item, n, station).</summary>
        public static void Invest(Database db, int product, int ingredient, int units, int station)
        {
            if (units <= 0) return;
            var s = State(db, product);
            var it = db.Item(product);
            int slot = it.blueprint.FindIndex(p => p.item == ingredient);
            if (slot < 0) return;
            s.remaining[slot] -= units;
            s.moneySpent += Mathf.Max(1, AgentGenerator.SinglePrice(db.Item(ingredient))) * units;
            if (station >= 0 && s.station < 0) s.station = station;
        }

        /// <summary>Manufacturing patch: withdraw committed ingredients only at their production station.</summary>
        public static bool Withdraw(Database db, int product, int slot, int units, int station)
        {
            if (units <= 0) return false;
            var s = State(db, product);
            var it = db.Item(product);
            if (it?.blueprint == null || s.station != station || slot < 0 || slot >= it.blueprint.Count) return false;
            int invested = Invested(db, s, slot);
            if (invested < units) return false;
            s.remaining[slot] += units;
            int price = Mathf.Max(1, AgentGenerator.SinglePrice(db.Item(it.blueprint[slot].item)));
            s.moneySpent = Mathf.Max(0, s.moneySpent - price * units);
            if (s.remaining.TrueForAll((r) => r >= 0) && IsEmptyOfIngredients(db, s))
            {
                s.moneySpent = 0;
                s.station = -1;
            }
            return true;
        }
        static bool IsEmptyOfIngredients(Database db, BlueprintState state)
        {
            var parts = db.Item(state.item)?.blueprint;
            if (parts == null) return true;
            for (int i = 0; i < parts.Count; i++) if (Invested(db, state, i) > 0) return false;
            return true;
        }
        /// <summary>210 / 223 can't be started in a system without gate routes (528).</summary>
        public static bool CanStartIn(Database db, int product, int station)
        {
            if (!NeedsRoutes.Contains(product)) return true;
            int sys = db.Stations.Find(x => x.index == station)?.system ?? -1;
            var routes = db.Systems.Find(x => x.index == sys)?.jumpRoutesTo;
            return routes != null && routes.Count > 0;
        }

        /// <summary>BluePrint::getAutoCompletionPrice 0x1a64c8.</summary>
        public static int AutoCompletePrice(Database db, int product)
        {
            var it = db.Item(product);
            if (it == null) return 0;
            // Remake mods: a mod blueprint's own price (blueprints.json "autocomplete": a number).
            if (Modding.ModBlueprints.AutocompleteOverride(product, out _, out int own) && own >= 0) return own;
            if (product == 210)
            {
                var s = State(db, product);
                int value = 0;
                for (int k = 0; k < it.blueprint.Count; k++)
                    value += Mathf.Max(0, s.remaining[k]) * AgentGenerator.SinglePrice(db.Item(it.blueprint[k].item));
                return 2000000 + value;
            }
            return (int)((float)(BaseQuantity(it) * it.maxPrice) * 1.25f);
        }

        /// <summary>Remake mods: the blueprint may be autocompleted (blueprints.json "autocomplete": false: never).</summary>
        public static bool CanAutocomplete(int product) => !Modding.ModBlueprints.AutocompleteOverride(product, out bool allowed, out _) || allowed;

        /// <summary>A completed run: to the hold here (true) or waiting at the production station (false); then reset.
        /// Remake mods: a ship blueprint's ship always waits at the production station (TakeBuiltShips), here too.</summary>
        public static bool Produce(Database db, int product, int currentStation)
        {
            var it = db.Item(product);
            if (it == null || it.blueprint == null) return false;   // not a product (-1 from a stale hangar row)
            var s = State(db, product);
            int qty = BaseQuantity(it);
            if (s.station < 0) s.station = currentStation;
            bool here = s.station == currentStation;
            bool ship = Modding.ModBlueprints.ShipOf(product) >= 0;
            if (here && !ship) GiveToCargo(db, product, qty);
            else
            {
                var pending = Session.PendingProducts.Find(p => p.item == product && p.station == s.station);
                if (pending != null) pending.quantity += qty;
                else Session.PendingProducts.Add(new PendingProduct { item = product, quantity = qty, station = s.station });
            }
            // BluePrint::reset
            s.timesProduced++;
            Session.GoodsProduced++;
            for (int k = 0; k < it.blueprint.Count; k++) s.remaining[k] = it.blueprint[k].amount;
            s.moneySpent = 0;
            s.station = -1;
            return here;
        }

        /// <summary>Item::makeItem into the hold (no space check, like buying); secondaries join a mounted stack.</summary>
        static void GiveToCargo(Database db, int item, int qty)
        {
            if (db.Item(item)?.TypeId == 1)
            {
                var mounted = Session.Equipment.Find(e => e.item == item);
                if (mounted != null) { mounted.amount += qty; return; }
            }
            Shop.AddToCargo(item, qty);
        }

        /// <summary>ModStation::checkPendingProducts: the products waiting here move to the hold.</summary>
        public static List<PendingProduct> CollectPending(Database db, int station)
        {
            var moved = Session.PendingProducts.FindAll(p => p.station == station && Modding.ModBlueprints.ShipOf(p.item) < 0);
            foreach (var p in moved)
            {
                Session.PendingProducts.Remove(p);
                GiveToCargo(db, p.item, p.quantity);
            }
            return moved;
        }

        /// <summary>Remake mods: the ship blueprints' finished ships waiting at 'station' (taken off the list; their blueprint
        /// items, Hangar.DeliverBuiltShips turns them into the ships).</summary>
        public static List<int> TakeBuiltShips(int station)
        {
            var list = new List<int>();
            foreach (var p in Session.PendingProducts.FindAll(p => p.station == station && Modding.ModBlueprints.ShipOf(p.item) >= 0))
            {
                Session.PendingProducts.Remove(p);
                for (int n = 0; n < Mathf.Max(1, p.quantity); n++) list.Add(p.item);
            }
            return list;
        }

        /// <summary>Status::nextCampaignMission's blueprint unlocks (the step reached), with pre-invested ingredients.</summary>
        public static void UnlockFromStory(Database db, int step)
        {
            switch (step)
            {
                case 34: Unlock(85); Invest(db, 85, 164, 50, 10); break;                  // the Void Crystals at Thynome
                case 58: Unlock(179); Invest(db, 179, 127, 5, 101); break;                // Liberator: 5 Microchips at Valkyrie
                case 72: Unlock(183); Shop.AddToCargo(175, 1); break;                 // Disruptor Laser + 1 Void Essence
                case 104: Unlock(206); Invest(db, 206, 163, 10, 10); break;               // Gamma Shield II: 10 Hypanium at Thynome
                case 141:                                                                 // Chromo Plasma at Var Lupra
                    Unlock(210);
                    Invest(db, 210, 201, 847, 112); Invest(db, 210, 202, 834, 112);
                    Invest(db, 210, 203, 861, 112); Invest(db, 210, 204, 892, 112);
                    break;
            }
        }
    }
}
