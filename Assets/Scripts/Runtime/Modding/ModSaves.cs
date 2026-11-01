// ModSaves.cs
// Mods and save games. A save records the mods that were on (id, name, version) and the "mod:id" key, number and price
// of every modded entry (SaveData.mods / modKeys, version 12). Loading:
//   - the main menu / station asks first when the save names a mod that isn't on now (MissingMods);
//   - Fix runs before the save is applied: a key at another number now (a save from another device whose registry
//     numbered the mods differently) is moved to its number; a key whose mod isn't on is removed (equipment, cargo, the
//     Kaamo Club, the parked ship refunded at the saved price; shop rows, bar visitors and records dropped), and so is
//     anything at a placeholder's number. Turning the mod on again before loading keeps it all.

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;

namespace GoF2Remake.Modding
{
    [Serializable] public class SaveMod { public string id, name, version; }
    [Serializable] public class SaveModKey { public string kind, key; public int index, price; }

    public static class ModSaves
    {
        /// <summary>SaveGame.Capture: what this game's mods are.</summary>
        public static void Record(SaveData s, Database db)
        {
            s.mods = ModManager.UserMods.Select(m => new SaveMod { id = m.Id, name = m.Name, version = m.Version }).ToList();
            s.modKeys = new List<SaveModKey>();
            foreach (var kv in ModContent.ActiveItemKeys())
            {
                var it = db.Item(kv.Key);
                s.modKeys.Add(new SaveModKey { kind = "item", key = kv.Value, index = kv.Key, price = it != null ? (it.minPrice + it.maxPrice) / 2 : 0 });
            }
            foreach (var kv in ModContent.ActiveShipKeys())
                s.modKeys.Add(new SaveModKey { kind = "ship", key = kv.Value, index = kv.Key, price = db.Ship(kv.Key)?.price ?? 0 });
            foreach (var kv in ModWorld.ActiveSystemKeys()) s.modKeys.Add(new SaveModKey { kind = "system", key = kv.Value, index = kv.Key });
            foreach (var kv in ModWorld.ActiveStationKeys()) s.modKeys.Add(new SaveModKey { kind = "station", key = kv.Value, index = kv.Key });
        }

        /// <summary>The names of the mods the save was made with that aren't on now (empty = it loads as it was).</summary>
        public static List<string> MissingMods(SaveData s)
        {
            var l = new List<string>();
            if (s?.mods == null) return l;
            foreach (var m in s.mods)
                if (!ModManager.Active.Any(a => string.Equals(a.Id, m.id, StringComparison.OrdinalIgnoreCase)))
                    l.Add(string.IsNullOrEmpty(m.name) ? m.id : m.name);
            return l;
        }

        /// <summary>The save's item numbers put right for the mods on now (see the file comment). Returns the credits refunded.</summary>
        public static int Fix(SaveData s)
        {
            int original = ModContent.OriginalItemCount;
            if (original < 0) { Database.Load(); original = ModContent.OriginalItemCount; }
            var map = new Dictionary<int, int>();   // saved number -> number now (-1 = gone)
            var price = new Dictionary<int, int>();
            if (s.modKeys != null)
                foreach (var k in s.modKeys)
                {
                    if (k == null || k.kind != "item" || string.IsNullOrEmpty(k.key)) continue;
                    if (!ModManager.InSession) ModRegistry.Adopt(ModRegistry.Kind.Item, k.key, k.index, original);
                    int now = ModContent.ItemIndexOf(k.key);
                    map[k.index] = now;
                    price[k.index] = Math.Max(0, k.price);
                }
            bool Changed(int i) => i >= original && (!map.TryGetValue(i, out int n) ? ModContent.IsMissingItem(i) || ModContent.ItemKey(i) == null : n != i);
            int Map(int i) => i < original ? i : map.TryGetValue(i, out int n) ? n : ModContent.IsMissingItem(i) || ModContent.ItemKey(i) == null ? -1 : i;

            int refund = 0;
            void Stacks(List<ItemStack> l, bool pay)
            {
                if (l == null) return;
                for (int i = l.Count - 1; i >= 0; i--)
                {
                    var st = l[i];
                    if (st == null || !Changed(st.item)) continue;
                    int n = Map(st.item);
                    if (n >= 0) { st.item = n; continue; }
                    if (pay && price.TryGetValue(st.item, out int p)) refund += p * Math.Max(1, st.amount);
                    l.RemoveAt(i);
                }
            }
            void Ints(List<int> l)
            {
                if (l == null) return;
                for (int i = l.Count - 1; i >= 0; i--)
                {
                    if (!Changed(l[i])) continue;
                    int n = Map(l[i]);
                    if (n >= 0) l[i] = n; else l.RemoveAt(i);
                }
            }
            void Prices(List<SaveData.KnownPrice> l)
            {
                if (l == null) return;
                for (int i = l.Count - 1; i >= 0; i--)
                {
                    if (l[i] == null || !Changed(l[i].item)) continue;
                    int n = Map(l[i].item);
                    if (n >= 0) l[i].item = n; else l.RemoveAt(i);
                }
            }

            Stacks(s.equipment, true);
            Stacks(s.cargo, true);
            Stacks(s.kaamoItems, true);
            if (s.kaamoShips != null) foreach (var k in s.kaamoShips) if (k != null) Stacks(k.equipment, true);
            if (s.parkedShip != null) { Stacks(s.parkedShip.equipment, true); Stacks(s.parkedShip.cargo, true); }
            if (s.recentStations != null)
                foreach (var rs in s.recentStations)
                {
                    if (rs == null) continue;
                    Stacks(rs.items, false);
                    rs.agents?.RemoveAll(a => a != null && a.sellItem >= 0 && Changed(a.sellItem) && Map(a.sellItem) < 0);
                    if (rs.agents != null) foreach (var a in rs.agents) if (a != null && a.sellItem >= 0 && Changed(a.sellItem)) a.sellItem = Map(a.sellItem);
                }
            Ints(s.seenItems); Ints(s.unsaleable); Ints(s.unlockedBlueprints); Ints(s.oreTypesMined); Ints(s.coreTypesMined); Ints(s.boozeTypes);
            Prices(s.lowestPrices); Prices(s.highestPrices);
            s.blueprints?.RemoveAll(b => b == null || Changed(b.item) && Map(b.item) < 0);
            if (s.blueprints != null) foreach (var b in s.blueprints) if (Changed(b.item)) b.item = Map(b.item);
            s.pendingProducts?.RemoveAll(p => p == null || Changed(p.item) && Map(p.item) < 0);
            if (s.pendingProducts != null) foreach (var p in s.pendingProducts) if (Changed(p.item)) p.item = Map(p.item);
            if (s.selectedSecondary >= 0 && Changed(s.selectedSecondary)) s.selectedSecondary = Map(s.selectedSecondary);
            if (s.freelanceMission != null && s.freelanceMission.type == MissionType.Purchase && Changed(s.freelanceMission.good))
            {
                int n = Map(s.freelanceMission.good);
                if (n >= 0) s.freelanceMission.good = n; else s.freelanceMission = new FreelanceMission();   // the goods are gone
            }
            refund += FixShips(s);
            FixWorld(s);
            s.credits += refund;
            if (refund > 0) UnityEngine.Debug.Log($"Mods: the save's items from mods that aren't on were removed, {refund} credits refunded");
            return refund;
        }

        public const int FallbackShip = 10;   // the Phantom, a new game's ship

        /// <summary>Ships: moved to their numbers now, or (their mod off) gone: the flown one becomes a Phantom (refunded, its
        /// equipment into the hold, mountable again at the station the save docks at), stored / parked ones are refunded,
        /// dealer rows and bar sellers dropped.</summary>
        static int FixShips(SaveData s)
        {
            int original = ModContent.OriginalShipCount;
            var map = new Dictionary<int, int>();
            var price = new Dictionary<int, int>();
            if (s.modKeys != null)
                foreach (var k in s.modKeys)
                {
                    if (k == null || k.kind != "ship" || string.IsNullOrEmpty(k.key)) continue;
                    if (!ModManager.InSession) ModRegistry.Adopt(ModRegistry.Kind.Ship, k.key, k.index, original);
                    map[k.index] = ModContent.ShipIndexOf(k.key);
                    price[k.index] = Math.Max(0, k.price);
                }
            bool Changed(int i) => i >= original && (!map.TryGetValue(i, out int n) ? !ModContent.IsModShip(i) : n != i);
            int Map(int i) => i < original ? i : map.TryGetValue(i, out int n) ? n : ModContent.IsModShip(i) ? i : -1;
            int Price(int i) => price.TryGetValue(i, out int p) ? p : 0;
            int refund = 0;
            if (Changed(s.ship))
            {
                int n = Map(s.ship);
                if (n >= 0) s.ship = n;
                else
                {
                    refund += Price(s.ship);
                    UnityEngine.Debug.Log($"Mods: the save's ship {s.ship} came from a mod that isn't on: a Phantom instead, its equipment in the hold");
                    s.ship = FallbackShip;
                    s.cargo ??= new List<ItemStack>();
                    if (s.equipment != null) s.cargo.AddRange(s.equipment);
                    s.equipment = new List<ItemStack>();
                    s.shipMods = new List<int>();
                }
            }
            if (s.kaamoShips != null)
                for (int i = s.kaamoShips.Count - 1; i >= 0; i--)
                {
                    var k = s.kaamoShips[i];
                    if (k == null || !Changed(k.ship)) continue;
                    int n = Map(k.ship);
                    if (n >= 0) k.ship = n; else { refund += Price(k.ship); s.kaamoShips.RemoveAt(i); }
                }
            if (s.hasParkedShip && s.parkedShip != null && Changed(s.parkedShip.ship))
            {
                int n = Map(s.parkedShip.ship);
                if (n >= 0) s.parkedShip.ship = n; else { refund += Price(s.parkedShip.ship); s.parkedShip.ship = FallbackShip; }
            }
            if (s.recentStations != null)
                foreach (var rs in s.recentStations)
                {
                    if (rs == null) continue;
                    if (rs.ships != null)
                        for (int i = rs.ships.Count - 1; i >= 0; i--)
                            if (Changed(rs.ships[i])) { int n = Map(rs.ships[i]); if (n >= 0) rs.ships[i] = n; else rs.ships.RemoveAt(i); }
                    rs.shipMods?.RemoveAll(m => m == null || Changed(m.ship) && Map(m.ship) < 0);
                    if (rs.shipMods != null) foreach (var m in rs.shipMods) if (Changed(m.ship)) m.ship = Map(m.ship);
                    rs.agents?.RemoveAll(a => a != null && a.sellShip >= 0 && Changed(a.sellShip) && Map(a.sellShip) < 0);
                    if (rs.agents != null) foreach (var a in rs.agents) if (a != null && a.sellShip >= 0 && Changed(a.sellShip)) a.sellShip = Map(a.sellShip);
                }
            return refund;
        }

        /// <summary>Systems and stations: moved to their numbers now, or (their mod off) gone: a save docked at such a station
        /// docks at Var Hastra, its shop memory, bar visitors and records of it go, a bar mission there is dropped, the Most
        /// Wanted forget it, production waiting there waits at Var Hastra.</summary>
        static void FixWorld(SaveData s)
        {
            Func<int, int> Mapper(string kind, int original, Func<string, int> indexOf, Func<int, bool> isMod, ModRegistry.Kind regKind)
            {
                var map = new Dictionary<int, int>();
                if (s.modKeys != null)
                    foreach (var k in s.modKeys)
                    {
                        if (k == null || k.kind != kind || string.IsNullOrEmpty(k.key)) continue;
                        if (!ModManager.InSession) ModRegistry.Adopt(regKind, k.key, k.index, original);
                        map[k.index] = indexOf(k.key);
                    }
                // the number now (-1 = gone); an original number stays
                return i => i < original ? i : map.TryGetValue(i, out int n) ? n : isMod(i) ? i : -1;
            }
            var st = Mapper("station", ModWorld.OriginalStationCount, ModWorld.StationIndexOf, ModWorld.IsModStation, ModRegistry.Kind.Station);
            var sy = Mapper("system", ModWorld.OriginalSystemCount, ModWorld.SystemIndexOf, ModWorld.IsModSystem, ModRegistry.Kind.System);
            if (ModWorld.OriginalStationCount < 0) return;
            if (s.station != Session.VoidOrbit && st(s.station) < 0)
            {
                UnityEngine.Debug.Log($"Mods: the save's station {s.station} came from a mod that isn't on: docked at Var Hastra instead");
                s.station = ModWorld.RefugeStation;
                s.previousStation = -1;
            }
            else if (s.station >= 0) s.station = st(s.station);
            if (s.previousStation >= 0) s.previousStation = st(s.previousStation);
            void Ints(List<int> l) { if (l == null) return; for (int i = l.Count - 1; i >= 0; i--) { int n = st(l[i]); if (n >= 0) l[i] = n; else l.RemoveAt(i); } }
            Ints(s.visitedStations); Ints(s.attackedStations);
            // Step 59's targets: a negative entry marks a convoy destroyed there (Status+0x90), not a station: kept as it is.
            if (s.storyTargets != null)
                for (int i = s.storyTargets.Count - 1; i >= 0; i--)
                {
                    if (s.storyTargets[i] < 0) continue;
                    int n = st(s.storyTargets[i]);
                    if (n >= 0) s.storyTargets[i] = n; else s.storyTargets.RemoveAt(i);
                }
            s.recentStations?.RemoveAll(r => r == null || st(r.station) < 0);
            if (s.recentStations != null)
                foreach (var r in s.recentStations)
                {
                    r.station = st(r.station);
                    r.agents?.RemoveAll(a => a != null && (a.station >= 0 && st(a.station) < 0 || a.sellSystem >= 0 && sy(a.sellSystem) < 0));
                    if (r.agents != null) foreach (var a in r.agents) { if (a.station >= 0) a.station = st(a.station); if (a.sellSystem >= 0) a.sellSystem = sy(a.sellSystem); }
                }
            if (s.freelanceMission != null && (s.freelanceMission.clientStation >= 0 && st(s.freelanceMission.clientStation) < 0
                                               || s.freelanceMission.target >= 0 && st(s.freelanceMission.target) < 0))
                s.freelanceMission = new FreelanceMission();
            else if (s.freelanceMission != null)
            {
                if (s.freelanceMission.clientStation >= 0) s.freelanceMission.clientStation = st(s.freelanceMission.clientStation);
                if (s.freelanceMission.target >= 0) s.freelanceMission.target = st(s.freelanceMission.target);
            }
            if (s.wanted != null)
                foreach (var w in s.wanted)
                {
                    if (w == null) continue;
                    if (w.current >= 0) w.current = st(w.current);
                    if (w.travelsTo >= 0) w.travelsTo = st(w.travelsTo);
                    if (w.lastSeen >= 0) w.lastSeen = st(w.lastSeen);
                }
            if (s.pendingProducts != null) foreach (var p in s.pendingProducts) if (p.station >= 0) { int n = st(p.station); p.station = n >= 0 ? n : ModWorld.RefugeStation; }
            if (s.blueprints != null) foreach (var b in s.blueprints) if (b.station >= 0) b.station = st(b.station);
            void Prices(List<SaveData.KnownPrice> l) { if (l == null) return; foreach (var p in l) if (p != null && p.system >= 0) p.system = Math.Max(-1, sy(p.system)); }
            Prices(s.lowestPrices); Prices(s.highestPrices);
        }

        /// <summary>SaveGame.Check (imports): a modded number the save itself names is fine, whatever the tables hold now.</summary>
        public static bool Names(SaveData s, int item) => s.modKeys != null && s.modKeys.Exists(k => k != null && k.kind == "item" && k.index == item);

        public static bool NamesStation(SaveData s, int station) => s.modKeys != null && s.modKeys.Exists(k => k != null && k.kind == "station" && k.index == station);

        public static bool NamesShip(SaveData s, int ship) => s.modKeys != null && s.modKeys.Exists(k => k != null && k.kind == "ship" && k.index == ship);
    }
}
