// Shop.cs
// The station shop's rules as plain C# (Reference/research/shop.md; price reference code tools/shop/prices.py):
//   Status::calcCargoPrices 0xb9d70   prices per station: java.util.Random(station), min + distance factor * (max - min)
//                                     +- 2 %, reseeded for each list (cargo, mounted, stock); selling pays the same price
//   Generator::getItemBuyList 0xa05c4 random stock by tech level, occurrence and campaign progress
//   Generator::getShipBuyList 0xa0eb8 0..5 ships of the system's race (sometimes another race)
//   Generator::computerTradeGoods     re-entering after > 30 s nibbles 0..2 units off each stock row
//   Status::departStation / addStationToStack: stock is kept for the last 3 visited stations
//   Ship::adjustPrice 0x1a433c         ship price -1 % in systems of its race
// The time-seeded parts (stock, ships) use UnityEngine.Random, like the rest of the per-visit randomness.
// Hardcore = Extreme difficulty. Both add-ons count as owned (their assets are part of the remake).
// Not ported: DLC-won and supernova extras, Kaamo Club storage, black-market signatures, blueprints.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class Shop
    {
        public const int RecentStationCount = 3;
        public const float TradeGoodsDelaySeconds = 30f;   // Generator::computerTradeGoods after > 30000 ms

        /// <summary>DAT_00254990 = DAT_0025cee4: race per ship (0 Terran, 1 Vossk, 2 Nivelian, 3 Midorian, 8 pirate, 9 void).</summary>
        public static readonly int[] ShipRace =
        {
            3, 0, 8, 3, 2, 0, 3, 0, 9, 1, 0, 8, 2, 0, 0, 0, 2, 0, 2, 3, 3, 2, 0, 8, 8, 8, 0, 0, 0, 8, 3, 2,
            8, 0, 0, 2, 0, 0, 0, 1, 0, 1, 1, 2, 1, 3, 3, 3, 3, 1, 1, 0, 8, 1, 1, 0, 3, 2, 0, 0, 8, 1, 3, 1,
        };

        /// <summary>The race the shop and the ship window name under a ship (remake: the original names none), -1 = none.
        /// ShipRace is the price rule's table (Ship::adjustPrice, Globals::getRandomEnemyFighter) and differs from what the
        /// ships are: Vossk for Trunt Harval's Nivelian Specter (44) / Scimitar (49) and the Nivelian Ghost (61), pirate for
        /// the Terran Gryphon (52), Midorian for the Terran Dark Angel (62); the ship descriptions (977 + index) settle them.
        /// The Vol Noor (42) is a Grey ship; the one-off Bloodstar (45) and Blue Fyre (46, "Nivelian and Terran design") name
        /// none; the Amboss (48) is its pirate maker's.</summary>
        public static int ShipMakerRace(int ship)
        {
            switch (ship)
            {
                case 42: return 7;
                case 44: case 49: case 61: return 2;
                case 52: case 62: return 0;
                case 45: case 46: return -1;
                case 48: return 8;
            }
            return ship >= 0 && ship < ShipRace.Length ? ShipRace[ship] : CustomShips.Get(ship)?.race ?? -1;
        }

        /// <summary>ShipRace for every ship: the original table, then the custom ships' own race (-1 = none).</summary>
        public static int RaceOfShip(int ship) => ship >= 0 && ship < ShipRace.Length ? ShipRace[ship] : CustomShips.Get(ship)?.race ?? -1;

        /// <summary>DAT_00254930 (Item::canBeInstalledMultipleTimes): categories a ship can mount only once.</summary>
        static readonly HashSet<int> OnePerShip = new HashSet<int> { 8, 9, 10, 13, 14, 15, 16, 17, 18, 19, 21, 26, 27, 28, 29, 33, 35, 37, 38, 41, Flight.CruiseDrive.Kind };
        /// <summary>'category': an item's one-per-ship kind (KindOf), its category for the originals.</summary>
        public static bool CanInstallMultiple(int category) => !OnePerShip.Contains(category);

        /// <summary>Remake: a cruise drive (Flight.CruiseDrive), a mod item made from the Khador Drive with cruiseSpeed (attr 133).</summary>
        public static bool IsCruiseDrive(ItemData it) => it != null && it.TypeId == 3 && it.Attr(Flight.CruiseDrive.SpeedAttr) > 0;

        /// <summary>The one-per-ship kind an item belongs to (Hangar's install and swap rules, FirstMounted): its category, or
        /// a remake kind of its own past the categories: a cruise drive keeps its base's category 18 (the Khador Drive's icon
        /// and look) but is fitted beside a Khador Drive, not in its place.</summary>
        public static int KindOf(ItemData it) => it == null ? -1 : IsCruiseDrive(it) ? Flight.CruiseDrive.Kind : it.categoryId;

        /// <summary>The mounted cruise drive, null without one.</summary>
        public static ItemData MountedCruiseDrive(Database db) => FirstMounted(db, Flight.CruiseDrive.Kind);

        /// <summary>The original's top tech level, of its items and its stations alike (stations 0-10, items 1-10).</summary>
        public const int TopTech = 10;
        /// <summary>Remake mods: the highest tech level a mod item may have (ModContent warns and clamps).</summary>
        public const int MaxItemTech = 20;

        /// <summary>Remake mods: items.json "stock" (attributes 125 / 126, least and most), how many units of the item a
        /// shop holds, and the highest it may be.</summary>
        public const int StockMinAttr = 125, StockMaxAttr = 126, MaxStock = 9999;

        /// <summary>Remake mods: the units a shop stocks of a mod's item ("stock"), in place of getItemBuyList's amount by
        /// item type (goods and secondaries 5..19, a good multiplied by the distance to its cheapest system, weapons and
        /// equipment 1..3). False = that original rule. The row is also left out of the re-docking nibble, so an exact
        /// "stock" stays exact.</summary>
        public static bool ModStock(ItemData it, out int min, out int max)
        {
            min = max = 0;
            if (it == null || !it.HasAttr(StockMinAttr)) return false;
            min = Mathf.Clamp(it.Attr(StockMinAttr), 1, MaxStock);
            max = Mathf.Clamp(it.Attr(StockMaxAttr, min), min, MaxStock);
            return true;
        }

        /// <summary>A station of tech level 'stationTech' may stock an item of 'itemTech' (getItemBuyList: techI &lt;= techS).
        /// Remake mods: no station's tech level is above 10, so a station of the top tech level stocks the items above it too
        /// (a mod's tech level 11+ gear), the item's occurrence alone setting how often; the original items never reach it.</summary>
        public static bool StationStocks(int stationTech, int itemTech) => itemTech <= stationTech || stationTech >= TopTech;

        /// <summary>Remake mods: weapons and equipment above the original's top tech level that may be found now: a mod's,
        /// priced, stocked somewhere (occurrence), no blueprint product, saleable, its "available" condition met and allowed
        /// by a mod campaign. Empty without mods, so the loot tables that draw on it (a tier-5 pirate outpost) stay the
        /// original's.</summary>
        public static List<ItemData> AboveTopTech(Database db)
        {
            var list = new List<ItemData>();
            if (db == null) return list;
            foreach (var it in db.Items)
                if (it.modded && it.techLevel > TopTech && it.TypeId <= 3 && it.maxPrice > 0 && it.occurrence > 0 && it.blueprint.Count == 0
                    && Hangar.IsSaleable(it.index) && !Modding.ModContent.IsMissingItem(it.index)
                    && Modding.ModUnlocks.ItemAvailable(it.index) && Modding.ModCampaigns.ItemAllowed(db, it.index)
                    && !NotInSession(db, it.index))
                    list.Add(it);
            return list;
        }

        /// <summary>One of 'items', weighted by occurrence (null for none).</summary>
        public static ItemData PickByOccurrence(List<ItemData> items)
        {
            if (items == null || items.Count == 0) return null;
            int total = 0;
            foreach (var it in items) total += Mathf.Max(1, it.occurrence);
            int r = Random.Range(0, total);
            foreach (var it in items)
            {
                r -= Mathf.Max(1, it.occurrence);
                if (r < 0) return it;
            }
            return items[items.Count - 1];
        }

        /// <summary>DAT_00251d10: Kaamo Club specials, never in a normal shop.</summary>
        static readonly HashSet<int> KaamoSpecials = new HashSet<int> { 200, 220, 208, 213, 216, 228, 229, 230, 231 };
        static readonly HashSet<int> SaoPerulaGoods = new HashSet<int> { 101, 102, 103, 107, 108, 109, 114, 124 };

        // ---- cargo (Ship::getCurrentLoad / getMaxLoad) -----------------------------------------------------------

        /// <summary>Every unit in cargo weighs 1 t; mounted items weigh nothing.</summary>
        public static int CargoLoad()
        {
            int load = 0;   // a loop: LINQ's Sum allocated on every call (the HUD's cargo readout, every frame)
            foreach (var s in Session.Cargo) load += s.amount;
            return load;
        }

        /// <summary>Ship::refreshValue 0x1a33f4 (Ship+0x48, getFirePower): over the mounted items of sorts 0-3, 8 and 25,
        /// attr 9 x (1 + attr 40 / 100) / (attr 11 x (1 - attr 39 / 100)) x 1000; the factors are the sort-28 weapon mod's.</summary>
        public static float FirePower(Database db)
        {
            var mod = FirstMounted(db, 28);
            float reloadF = mod != null ? 1f - mod.Attr(39) / 100f : 1f, damageF = mod != null ? 1f + mod.Attr(40) / 100f : 1f;
            float sum = 0f;
            foreach (var e in Session.Equipment)
            {
                var it = db.Item(e.item);
                if (it == null) continue;
                int sort = it.categoryId;
                if (sort > 25 || ((1 << sort) & 0x200010F) == 0) continue;
                float reload = it.Attr(11);
                if (reload <= 0f) continue;
                sum += it.Attr(9) * damageF / (reload * reloadF) * 1000f;
            }
            return sum;
        }

        /// <summary>Ship::getBaseHP: the hull (+40 per mod 0).</summary>
        public static int BaseHp(Database db) => Modding.KitRules.Armor(db.Ship(Session.ShipIndex), 0) + 40 * Session.ModLevel(0);   // a kit ship's build too

        /// <summary>Ship::getCombinedHP: the hull + the shield (attr 18) + the armor (attr 20).</summary>
        public static int CombinedHp(Database db)
        {
            var shield = FirstMounted(db, 9);
            var armor = FirstMounted(db, 10);
            return BaseHp(db) + (shield != null ? shield.Attr(18) : 0) + (armor != null ? armor.Attr(20) : 0);
        }

        /// <summary>Base cargo (+30 with mod 1, Ship::refreshValue, before compression) + (int)(base * sum of mounted
        /// compression (attr 22, category 12) % / 100).</summary>
        public static int MaxLoad(Database db)
        {
            int b = Modding.KitRules.Cargo(db.Ship(Session.ShipIndex), 0) + 30 * Session.ModLevel(1), pct = 0;   // a kit ship's build too
            foreach (var e in Session.Equipment) { var it = db.Item(e.item); if (it != null && it.categoryId == 12) pct += it.Attr(22); }
            return b + (int)(b * pct / 100f);
        }

        public static int FreeCargo(Database db) => MaxLoad(db) - CargoLoad();

        /// <summary>Units of an item in the hold.</summary>
        public static int CargoOf(int item) { int n = 0; foreach (var s in Session.Cargo) if (s.item == item) n += s.amount; return n; }

        public static void AddToCargo(int item, int amount)
        {
            if (amount <= 0) return;
            // Remake: a number the item table doesn't have never goes into the hold (it ended up in a player's hold or stock
            // and the hangar threw on every trade, 2026-10-12): refused, with the caller logged to find where it came from.
            if (Database.Shared.Item(item) == null)
            {
                Debug.LogWarning($"Shop: {amount} t of item {item}, which isn't in the item table, not added to the hold\n{System.Environment.StackTrace}");
                return;
            }
            var stack = Session.Cargo.Find(s => s.item == item);
            if (stack != null) stack.amount += amount;
            else Session.Cargo.Add(new ItemStack(item, amount));
        }

        /// <summary>Removes up to 'amount' units of an item from the cargo hold.</summary>
        public static void RemoveFromCargo(int item, int amount)
        {
            for (int i = Session.Cargo.Count - 1; i >= 0 && amount > 0; i--)
            {
                var s = Session.Cargo[i];
                if (s.item != item) continue;
                int take = Mathf.Min(amount, s.amount);
                s.amount -= take;
                amount -= take;
                if (s.amount <= 0) Session.Cargo.RemoveAt(i);
            }
        }

        /// <summary>Adds a row to a station's stock, keeping item index order.</summary>
        public static void InsertStock(StationStock stock, ItemStack row) => InsertSorted(stock.items, row);

        /// <summary>Ship::getFirstEquipmentOfSort: the first mounted item of a category (a one-per-ship kind, KindOf: a cruise
        /// drive made from the Khador Drive isn't a jump drive here), or null.</summary>
        public static ItemData FirstMounted(Database db, int category)
        {
            foreach (var e in Session.Equipment) { var it = db.Item(e.item); if (it != null && KindOf(it) == category) return it; }
            return null;
        }

        // ---- geometry ----------------------------------------------------------------------------------------

        public static int SystemOf(Database db, int station) => db.Stations.Find(s => s.index == station)?.system ?? 0;
        public static int RaceOfSystem(Database db, int system) => db.Systems.Find(s => s.index == system)?.raceId ?? 0;

        /// <summary>Galaxy::distancePercent 0x1a4e40: (int)sqrt(dx^2 + dy^2) on the galaxy map.</summary>
        public static int Distance(Database db, int systemA, int systemB)
        {
            var a = db.Systems.Find(s => s.index == systemA)?.mapPosition;
            var b = db.Systems.Find(s => s.index == systemB)?.mapPosition;
            if (a == null || b == null) return 0;
            float dx = b.x - a.x, dy = b.y - a.y;
            return (int)Mathf.Sqrt(dy * dy + dx * dx);
        }

        // ---- prices ------------------------------------------------------------------------------------------

        /// <summary>Status::calcCargoPrices: the prices of 'items' at 'station': min + the distance factor x (max - min),
        /// +- 2 %. The original draws the +- 2 % in list order from a fresh java.util.Random(station), so an item's price
        /// depended on its place in the list: the hold's, the mounted items' and the stock's lists priced it differently,
        /// and a unit bought from the stock sold for more once it was the hold's (players' report, multiplayer: Garuda
        /// missiles bought at Thynome for 724, sold for 746, again and again). Remake, in a multiplayer session only: the
        /// +- 2 % comes from java.util.Random(station x 10007 + item), the same for an item at a station whatever list it is
        /// in. Single player keeps the original's prices (Reference/tools/shop/prices.py; a docking's prices are pinned
        /// there, Hangar.PinPrices).</summary>
        public static int[] PriceList(Database db, int station, IList<int> items)
        {
            int system = SystemOf(db, station);
            var rnd = NetSession.Active ? null : new JavaRandom(station);
            var prices = new int[items.Count];
            for (int n = 0; n < items.Count; n++)
            {
                var it = db.Item(items[n]);
                if (it == null) continue;
                if (system == 25)   // Loma: the maximum; the iOS 1.1.19 build charges x 1.05 (Status::calcCargoPrices), the Android
                {                   // 2.0.16 build exactly the maximum: the Default economy (the iOS / PC tables) takes the iOS rule
                    prices[n] = Session.Economy == Economy.Default ? (int)(it.maxPrice * 1.05f) : it.maxPrice;
                    continue;
                }
                float span = Distance(db, it.lowestPriceSystem, it.highestPriceSystem);
                float cur = Distance(db, it.lowestPriceSystem, system);
                float f = 100f / span * cur / 100f;                         // span 0 -> inf / NaN -> 1
                if (!(f < 1f)) f = 1f;
                int b = it.minPrice + (int)(f * (it.maxPrice - it.minPrice));
                int d = Mathf.Max(1, (int)(b * 0.02f));
                prices[n] = b - d + (rnd ?? new JavaRandom(station * 10007L + items[n])).NextInt(2 * d + 1);
            }
            return prices;
        }

        /// <summary>Ship::adjustPrice: ships.json price, 1 % less in a system of the ship's race.</summary>
        public static int ShipPrice(Database db, int ship, int station)
        {
            var s = db.Ship(ship);
            if (s == null) return 0;
            int race = RaceOfSystem(db, SystemOf(db, station));
            return RaceOfShip(ship) == race ? (int)(s.price * 0.99f) : s.price;
        }

        // ---- docking: stock kept for the last 3 stations ------------------------------------------------------

        /// <summary>Status::departStation on arriving in an orbit (StarMap::depart, the jump scenes, MGame's level
        /// switches): the station goes on the 3-station stack (addStationToStack: one already on it keeps its place), its
        /// stock, dealer and lounge made when it is new to it. The remake only pushed on docking, so a station's stock (an
        /// empty dealer, getShipBuyList's 1 in 6) stayed until the player had docked at 3 other stations; flying through
        /// orbits counts in the original (a player's report: Katashán's dealer never refreshed). Not in a multiplayer session,
        /// whose stock the host keeps (NetStock).</summary>
        public static void ArriveInOrbit(Database db, int station)
        {
            if (station < 0 || NetSession.Active || Session.RecentStations.Exists(s => s.station == station)) return;
            PushStation(db, station);
        }

        /// <summary>A station new to the stack: its stock (getItemBuyList / getShipBuyList / createAgents); the oldest goes.</summary>
        static StationStock PushStation(Database db, int station)
        {
            var recent = Session.RecentStations;
            var stock = new StationStock { station = station, items = GenerateItems(db, station), ships = GenerateShips(db, station),
                                           agents = AgentGenerator.CreateAgents(db, station) };
            recent.Add(stock);
            while (recent.Count > RecentStationCount) recent.RemoveAt(0);
            return stock;
        }

        /// <summary>Status::departStation (on arrival) + ModStation::OnInitialize: the station's stock, generated when the
        /// station isn't among the last 3 visited, otherwise nibbled by computerTradeGoods after > 30 s away.</summary>
        public static StationStock EnterStation(Database db, int station)
        {
            Modding.ModBlueprints.UnlockAvailable();   // remake mods: blueprints.json "unlocked": known once available
            var stock = Session.RecentStations.Find(s => s.station == station);
            if (stock == null) stock = PushStation(db, station);
            else if (Session.LastDepartureTime >= 0f && Time.realtimeSinceStartup - Session.LastDepartureTime > TradeGoodsDelaySeconds
                     && station != 108 && !(GameHooks.StockShared?.Invoke(station) ?? false))   // multiplayer: the host's stock resets instead
            {
                foreach (var row in stock.items)
                {
                    if (ModStock(db.Item(row.item), out _, out _)) continue;   // a mod's own stock amount stays as it is
                    int r = Random.Range(0, 3);
                    if (r < row.amount) row.amount -= r;
                }
            }
            if (stock.agents == null || (stock.agents.Count == 0 && station != 108)) stock.agents = AgentGenerator.CreateAgents(db, station);   // saves from before the bar
            // An evacuated supernova station's lounge is empty (AgentGenerator.CreateAgents): visitors a save kept from before go.
            if (InSupernovaSystem(SystemOf(db, station), station)) stock.agents.Clear();
            // The Kaamo Club never has a dealer (getShipBuyList); a save from a test build that gave it one is cleared.
            if (station == 108 && stock.ships != null) stock.ships.Clear();
            // Status::departStation: at the owned club the storage is the station's stock (one shared list here).
            if (KaamoClub.StorageAt(station)) stock.items = Session.KaamoItems;
            Freelance.OnEnterStation(stock);
            GameHooks.StockEntered?.Invoke(stock);   // multiplayer: the stock everyone shares replaces it
            return stock;
        }

        /// <summary>Generator::getItemBuyList 0xa05c4 (see shop.md 4.3).</summary>
        /// <summary>The station's stock (getItemBuyList); remake mods: a campaign with only mods' items drops the rest.</summary>
        public static List<ItemStack> GenerateItems(Database db, int station)
        {
            var list = GenerateItemsOriginal(db, station);
            list.RemoveAll(s => !Modding.ModCampaigns.ItemAllowed(db, s.item) || !Modding.ModUnlocks.ItemAvailable(s.item)
                                || NotInSession(db, s.item));
            return list;
        }

        /// <summary>Remake, multiplayer (the user's request): the time extender (category 26, the Rhoda Vortex 184 and a
        /// mod's) does nothing in a session (TimeExtender.Attach: the shared world can't slow down for one player), so while a
        /// session runs no shop stocks it (GenerateItems, the host's shared lists NetStock.HostItem: a pilot's sold one is
        /// paid for and not put on sale), no loot, capital ship crate or armory has it (NpcTables.RollLoot,
        /// CapitalShips), no bar agent sells it and no Purchase mission asks for it (AgentGenerator), and no tier-5 loot
        /// draws it (AboveTopTech). One a pilot already has stays theirs (it can be sold).</summary>
        public static bool NotInSession(Database db, int item) =>
            NetSession.Active && db?.Item(item) is ItemData it && it.categoryId == TimeExtenderCategory;

        public const int TimeExtenderCategory = 26;

        /// <summary>getItemBuyList's factor on an item's occurrence chance, capped at 1.5: iOS 1.1.19 (taken with the Default
        /// economy) (step + 10) / 45; Android 2.0.16 (step + 25) / 45.</summary>
        internal static float StockFactor(int mission) =>
            Mathf.Min(1.5f, (mission + (Session.Economy == Economy.Default ? 10 : 25)) / 45f);

        static List<ItemStack> GenerateItemsOriginal(Database db, int station)
        {
            var list = new List<ItemStack>();
            int mission = Session.CampaignMission;
            if (station == 78 && mission < 7)   // tutorial: the free starter gear only
            {
                list.Add(new ItemStack(0, 1)); list.Add(new ItemStack(22, 1)); list.Add(new ItemStack(55, 1));
                return list;
            }
            if (station == 108) return list;
            if (InSupernovaSystem(SystemOf(db, station), station)) return list;   // getItemBuyList: nothing to buy there

            var st = db.Stations.Find(s => s.index == station);
            int techS = st?.techLevel ?? 1;
            int system = st?.system ?? 0;
            int race = RaceOfSystem(db, system);
            bool hardcore = Session.IsExtreme;
            float k = StockFactor(mission);
            int lowTech = station == 105 || station == 107 ? 0 : techS < 4 ? 1 : techS / 2;

            foreach (var it in db.Items)
            {
                int idx = it.index, type = it.TypeId, sort = it.categoryId, techI = it.techLevel, occ = it.occurrence;
                bool specialty = idx >= 132 && idx <= 153;
                bool exclusive = it.Attr(61, -1) == station && !((idx == 196 || (idx >= 198 && idx <= 200)) && mission < 142);
                // Remake mods: a mod item's "alwaysSoldAt" station always stocks it, also where the original's station filters
                // below allow only one kind (Var Destro 105 weapons, Quineros 107 equipment, Valkyrie 101 no equipment, Sao
                // Perula 106 goods). getItemBuyList applies them to its own attr-61 items too; a player's report: a mod's 13
                // shields, cloaks and gear at Var Destro were never sold.
                bool modAlways = exclusive && it.modded;
                if (station == 106 && !modAlways && !(SaoPerulaGoods.Contains(idx) || specialty)) continue;

                // Owned add-ons give items without an occurrence one (Valkyrie for idx < 196, Supernova above); a mod item's 0 means never.
                if (occ == 0 && !it.modded && !exclusive && type != 4 && idx != 85 && it.blueprint.Count == 0 && !(idx == 181 && mission < 59)
                    && !((sort >= 33 && sort <= 35 || sort == 43) && mission < 142) && sort != 36 && (sort != 29 || system == 25)   // signatures: only the black market
                    && idx != 209 && idx != 210 && idx != 217 && idx != 218 && !(idx == 205 && mission < 94) && !KaamoSpecials.Contains(idx))
                    occ = Random.Range(0, 30) + (int)((1f - techI / 10f) * 30f);

                if (!exclusive)
                {
                    if (it.blueprint.Count > 0 || idx == 217 || idx == 218 || idx == 164 || idx == 175) continue;
                    if (!StationStocks(techS, techI) || occ == 0 || it.maxPrice == 0) continue;
                    if (it.Attr(60) == 1 && race != 1) continue;                            // Vossk-only gear
                    if (specialty && idx != 132 + system && station != 106) continue;       // one specialty per system
                }
                if (hardcore && (sort == 23 || sort == 24)) continue;
                if (!modAlways)
                {
                    if (station == 107 && type != 3) continue;
                    if (station == 105 && !(type <= 2 || sort == 28)) continue;
                    if (station == 101 && type > 2) continue;
                    if (station == 106 && type != 4) continue;
                }

                if (!exclusive)
                {
                    if (!((StationStocks(techS, techI) || specialty) && Random.Range(0, 100) < (int)(k * occ))) continue;
                    if (techI < lowTech && idx != 122 && !(Random.Range(0, 100) < 61)) continue;
                }

                int r = Random.Range(0, 15) + 5;   // 5..19
                int amount;
                // Remake mods: items.json "stock" says the units outright, in place of the amount by item type below.
                if (ModStock(it, out int stockMin, out int stockMax)) amount = Random.Range(stockMin, stockMax + 1);
                else if (idx == 109) amount = Mathf.Max(1, r / 2);
                else if (type == 1) amount = r;
                else if (type == 4)
                {
                    amount = r;
                    int inv = 100 - Distance(db, system, it.lowestPriceSystem);
                    if (inv > 50) amount = r * Mathf.Max(1, (int)((inv - 50) / 50f * (hardcore ? 2 : 20)));
                    if (idx == 110) amount = Mathf.Min(amount, Random.Range(0, 10) + 10);
                }
                else amount = Mathf.Max(1, r / 5);   // weapons, turrets, equipment: 1..3
                list.Add(new ItemStack(idx, amount));
            }
            // Remake-only, in free play (no story): the starting station always sells the cheapest drill (IMT Extract 1.3,
            // normally a 70 % chance there) to keep mining reachable, and energy cells, which the free-play Khador jump out of
            // gateless Mido needs (GalaxyMap.HasJumpDrive).
            // getItemBuyList's story extras: the Luur hazard suit (190) first at 139 in system 25, the volatile Void Essence
            // (209) at 126 (one at 117).
            if (!Session.FreePlay && mission == 139 && system == 25 && !list.Exists(s => s.item == 190)) list.Insert(0, new ItemStack(190, 1));
            // iOS 1.1.19 (the Default economy): during steps 17..24 every shop has a 40 % chance of one AB-1 Retractor (68), in
            // place of the Android build's step-23 seller at Thynome (AgentGenerator.CreateAgents).
            if (Session.Economy == Economy.Default && !Session.FreePlay && mission >= 17 && mission <= 24 && Random.Range(0, 100) < 40
                && !list.Exists(s => s.item == 68)) list.Insert(0, new ItemStack(68, 1));
            if (station == 126 && !list.Exists(s => s.item == 209)) InsertSorted(list, new ItemStack(209, mission == 117 ? 1 : Random.Range(0, 10) + 1));
            if (Session.FreePlay && station == 78 && !list.Any(s => db.Item(s.item)?.categoryId == 19)) InsertSorted(list, new ItemStack(86, 1));
            if (Session.FreePlay && station == 78 && !list.Any(s => s.item == GalaxyMap.EnergyCellItem))
                InsertSorted(list, new ItemStack(GalaxyMap.EnergyCellItem, Random.Range(0, 15) + 5));
            // Remake multiplayer: the completed world's start station (Dis) sells the Khador Drive (85; never in a normal
            // stock: the blueprint makes it).
            if (Session.CompletedWorld && station == NetSession.StartStation && !list.Exists(s => s.item == 85))
                InsertSorted(list, new ItemStack(85, Random.Range(0, 3) + 2));
            return list;
        }

        /// <summary>Keeps the stock in item index order.</summary>
        static void InsertSorted(List<ItemStack> list, ItemStack row)
        {
            int at = list.FindIndex(s => s.item > row.item);
            list.Insert(at < 0 ? list.Count : at, row);
        }

        /// <summary>Generator::getShipBuyList 0xa0eb8 (shop.md 4.4, without the DLC-won and supernova extras).</summary>
        /// <summary>The station's dealer ships (getShipBuyList); remake mods: a campaign with only mods' ships drops the rest.</summary>
        public static List<int> GenerateShips(Database db, int station)
        {
            var ships = GenerateShipsOriginal(db, station);
            ships.RemoveAll(s => !Modding.ModCampaigns.ShipAllowed(s) || !Modding.ModUnlocks.ShipAvailable(s));
            return ships;
        }

        static List<int> GenerateShipsOriginal(Database db, int station)
        {
            var ships = new List<int>();
            int system = SystemOf(db, station);
            int mission = Session.CampaignMission;
            if (station == 101 || station == 108 || (system == 15 && mission < 16)) return ships;
            if (InSupernovaSystem(system, station)) return ships;                           // the evacuated supernova system
            if (station == 100 && Story.Dlc1Won) return new List<int> { 37, 38, 40 };      // Kothar: Khador's jump-drive ships
            if (station == 107)                                                             // Quineros: the pirate yard
            {
                ships.AddRange(new[] { 2, 11, 23, 24, 25, 29, 32 });
                // ... and a criminal's ship once its Most Wanted entry 6 / 12 / 18 / 24 is terminated.
                int[] entries = { 6, 12, 18, 24 };
                for (int k = 0; k < 4; k++)
                    if (Session.CompletedWorld || (entries[k] < Session.Wanted.Count && Session.Wanted[entries[k]].terminated)) ships.Add(45 + k);
                return ships;
            }
            if (station == 10 && (Achievements.GotAllGoldMedals || Session.CompletedWorld)) return new List<int> { 8 };   // Thynome: the VoidX
            int race = RaceOfSystem(db, system);
            // Android 2.0.16: rnd 6 ships (+1 at 41), 1 in 6 an empty dealer, a duplicate roll dropped; iOS 1.1.19 (the Default
            // economy): rnd 4 + 2 (+1 at 41), never empty, a duplicate rolled again.
            bool ios = Session.Economy == Economy.Default;
            int n = (ios ? Random.Range(0, 4) + 2 : Random.Range(0, 6)) + (station == 41 ? 1 : 0);
            if (n == 0) return ships;   // getShipBuyList: no rolls, no extras either
            for (int i = 0; i < n; i++)
            {
                int ship, tries = 0;
                do
                {
                    if (i == 0 && station == 41) ship = 10;          // Phantom
                    else if (i == 0 && station == 78) ship = 0;      // Betty
                    else
                    {
                        int r = race;
                        if (n > 1 && Random.value < 0.22f) { r = Random.Range(0, 5); if (r == race || r == 4) r = 8; }
                        ship = RandomFighter(r);
                    }
                } while (ios && ships.Contains(ship) && ++tries < 50);
                if (!ships.Contains(ship)) ships.Add(ship);
            }
            if (race == 0 && Random.Range(0, 7) == 0) ships.Add(62);
            if (race == 1 && Random.Range(0, 5) == 0) ships.Add(63);
            if (race == 2 && Random.Range(0, 8) == 0) ships.Add(61);
            // Valkyrie won: the Vossk dealers may carry the S'Kanarr and the K'Suukk.
            if (race == 1 && Story.Dlc1Won) { if (Random.Range(0, 2) == 0) ships.Add(39); if (Random.Range(0, 2) == 0) ships.Add(41); }
            if (race == 1 && Random.Range(0, 4) == 0) ships.Add(54);
            // Katashán (120) after the Supernova story: the Specter (Extreme or every medal) and the Scimitar.
            if (station == 120 && mission > 158)
            {
                if (Session.IsExtreme || Achievements.GotAllSupernovaMedals || Session.CompletedWorld) ships.Add(44);
                ships.Add(49);
            }
            if (race == 0 && Random.Range(0, 8) == 0) ships.Add(51);
            if (system == 17) foreach (int s in new[] { 42, 43, 52 }) if (Random.Range(0, 3) == 0) ships.Add(s);
            Modding.ModUnlocks.AddDealerShips(db, station, race, ships);   // remake mods: ships.json "dealer"
            return ships.Distinct().ToList();
        }

        /// <summary>Globals::getRandomEnemyFighter 0xf9034 (NpcTables.RandomFighter).</summary>
        public static int RandomFighter(int race) => Flight.FighterPool.Random(race);

        /// <summary>Status::inSupernovaSystem: system 27 before campaign 158, not the Void's orbit.</summary>
        public static bool InSupernovaSystem(int system, int station) =>
            system == 27 && Session.CampaignMission < 158 && station != Session.VoidOrbit;
    }
}
