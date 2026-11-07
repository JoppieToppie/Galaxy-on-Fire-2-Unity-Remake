// OriginalSaveConvert.cs
// Remake: an original game's save slot (OriginalRecord, OriginalSave) into the remake's SaveData and back. The remake's
// Session was modelled on the original's Status, so most fields map one to one (their Status offsets in Session's
// comments); the original's record order and offsets are in OriginalSaveData / RecordHandler::recordStoreWrite.
//
// Import (ToSave): the story step, its mission (rebuilt from story.json, the original's progress value kept), credits, the
// ship with its equipment and hold (unsaleable items kept so), the 3-station shop memory (the bar visitors come again on
// the next docking), the stations visited and attacked, the systems shown, the known prices, the standings, medals, the
// statistics, blueprints and products waiting, wingmen, the parked ship, the Kaamo Club, the Most Wanted board, the story
// agents whose offer was taken, the hints. Not carried: the visitors in the bars (re-rolled), a few counters the remake
// doesn't keep (Status+0xb4, +0xdc, +0x10c, the 0x84 Void return station), the options.
// Export (ToRecord): the same the other way; the remake's own things don't exist in the original and are left out with a
// note: mod items and ships, Kaamo upgrade levels above one, items past a ship's slots (the remake's cloak bay), event
// graph quests, the pirate board, the remake's bar missions. The bars of the 3 remembered stations are empty in the
// exported game until it rolls them again; the story agents (Status::getAgents) are written from agents.json, their offer
// text empty (SpaceLounge::startChat writes it on every chat). Only the Android version's tables (135 stations, 233 items,
// 25 blueprints): the PC Full HD version's are smaller and its loader indexes them by the record's counts.

using System;
using System.Collections.Generic;
using System.Linq;

namespace GoF2Remake.Data
{
    public static class OriginalSaveConvert
    {
        // The original's ore / core type lists (Status+0x94 / +0x98): Gold .. Void Crystals, their cores.
        const int FirstOre = 154, FirstCore = 165, FirstBooze = 132;

        /// <summary>A file read: the record, how (signed with the community key, which economy) or why not.</summary>
        public class ReadResult
        {
            public OriginalRecord record;
            public Economy economy;
            public bool signedFile, signatureValid;
            public string problem;
        }

        /// <summary>Reads a gof2_save_game_N file of any known kind: the Android versions' (signed; the community builds' key
        /// or a device's), the PC Full HD version's (unsigned); the economy is the one whose blueprint recipes read the whole
        /// file (Android first). 'recipes': OriginalSave.IngredientCounts per economy.</summary>
        public static ReadResult Read(byte[] file, IReadOnlyList<int> androidRecipes, IReadOnlyList<int> defaultRecipes)
        {
            var res = new ReadResult();
            if (file == null || file.Length < 200) { res.problem = "not a Galaxy on Fire 2 save (too short)"; return res; }
            res.signatureValid = OriginalSave.SignatureValid(file);
            string last = "not a Galaxy on Fire 2 save";
            foreach (bool signed in new[] { true, false })
                foreach (var (economy, recipes) in new[] { (Economy.Android, androidRecipes), (Economy.Default, defaultRecipes) })
                {
                    try
                    {
                        var r = OriginalSave.Read(file, recipes, signed);
                        if (r.trailing.Length != 0) { last = $"{r.trailing.Length} bytes left over"; continue; }
                        if (!Plausible(r, out string why)) { last = why; continue; }
                        res.record = r; res.economy = economy; res.signedFile = signed;
                        return res;
                    }
                    catch (Exception e) { last = e.Message; }
                }
            res.problem = last;
            return res;
        }

        /// <summary>A stream that parsed but isn't a save (another file read by chance, the wrong economy's recipes).</summary>
        static bool Plausible(OriginalRecord r, out string why)
        {
            why = null;
            if (r.visitedStations.Length < 50 || r.visitedStations.Length > 200) why = "an unexpected station count";
            else if (r.campaignMission < 0 || r.campaignMission > Story.LastIndex) why = "an unknown story step";
            else if (r.medals.Length > 64 || r.itemsObtained.Length > 400) why = "unexpected tables";
            else if (float.IsNaN(r.difficulty) || r.difficulty < 0f || r.difficulty > 2f) why = "an unknown difficulty";
            return why == null;
        }

        // ---- import -----------------------------------------------------------------------------------------------

        /// <summary>The record as the remake's save (checked again by SaveGame.TryParse before it is written). 'notes': what
        /// didn't come across (English, for the log and the notice).</summary>
        public static SaveData ToSave(OriginalRecord r, Database db, Economy economy, List<string> notes)
        {
            bool Item(int i) => i >= 0 && i < db.Items.Count && db.Item(i) != null;
            bool Ship(int i) => i >= 0 && i < db.Ships.Count && db.Ship(i) != null;
            bool Station(int i) => i >= 0 && i < db.Stations.Count;
            var unsaleable = new HashSet<int>();
            int dropped = 0;
            List<ItemStack> Stacks(IEnumerable<(int item, int amount, bool unsaleable)> list)
            {
                var l = new List<ItemStack>();
                foreach (var (item, amount, u) in list)
                {
                    if (!Item(item)) { dropped++; continue; }
                    l.Add(new ItemStack(item, Math.Max(1, amount)));
                    if (u) unsaleable.Add(item);
                }
                return l;
            }
            List<ItemStack> Equip(List<OriginalEquipment> l) => Stacks(l.Where(e => e != null && e.item >= 0).Select(e => (e.item, e.amount, e.unsaleable)));
            List<ItemStack> Goods(List<OriginalItem> l) => Stacks(l.Where(e => e != null).Select(e => (e.item, e.amount, e.unsaleable)));
            List<int> Indices(bool[] a, int offset, Func<int, bool> ok) => Enumerable.Range(0, a?.Length ?? 0).Where(i => a[i] && ok(i + offset)).Select(i => i + offset).ToList();

            int index = Math.Max(0, Math.Min(Story.LastIndex, r.campaignMission));
            var stack = r.stations ?? new OriginalStation[0];
            var current = stack.Length > 0 ? stack[stack.Length - 1] : null;
            int station = current != null && Station(current.index) ? current.index
                        : stack.FirstOrDefault(x => x != null && Station(x.index))?.index ?? 78;
            var s = new SaveData
            {
                savedAtUtc = DateTime.UtcNow.ToString("o"),
                playSeconds = Math.Max(0f, r.playingTimeMs / 1000f),
                campaign = (int)(index >= Story.Dlc1WonIndex ? Campaign.Supernova : index >= Story.GameWonIndex ? Campaign.Valkyrie : Campaign.GalaxyOnFire2),
                difficulty = r.difficulty,
                economy = (int)economy,
                campaignMission = index,
                station = station,
                previousStation = stack.Length > 1 && stack[1] != null && Station(stack[1].index) ? stack[1].index : -1,
                lastDockedStation = station,
                ship = Ship(r.ship) ? r.ship : 0,
                credits = Math.Max(0, r.credits),
                equipment = Equip(r.equipment),
                cargo = Goods(r.cargo),
                jumpgatesUsed = r.jumpgatesUsed, kills = r.kills, pirateKills = r.pirateKills,
                standing = r.standings.Length == 2 ? r.standings.Select(v => Math.Max(-100, Math.Min(100, v))).ToArray() : new[] { 30, 0 },
                freePlay = false,
                storyStepStart = Math.Max(0f, r.status100 / 1000f),
                freelanceCompleted = r.missionCount, storyCounter = r.status174,
                voidInvasionSystem = r.status7c, voidInvasionStation = r.status80, invasionDepartures = r.status88,
                passengers = r.passengers,
                goodsProduced = r.goodsProduced,
                // Status+0x94 / +0x98 ore / core types, +0x9c .. +0xa8, +0xac booze types, +0xb0 junk, +0xb8 passengers delivered
                oreTypesMined = Indices(r.oreTypesMined, FirstOre, Item), coreTypesMined = Indices(r.coreTypesMined, FirstCore, Item),
                containersDelivered = r.courierGoods, oreMined = r.oresMined, coresMined = r.coresMined, boozeBought = r.boozeBought,
                boozeTypes = Indices(r.boozeTypes, FirstBooze, Item), junkDestroyed = r.spaceJunk, passengersDelivered = r.passengersCarried,
                cloakMs = r.cloakMs,
                // Status+0xc8 .. +0xec: nukes, alien remains, agents talked to, wingmen hired, asteroids, (+0xdc), declines,
                // repeats, accepted without asking the risk / for the map
                bombsDetonated = C8(r, 0), alienRemainsCollected = C8(r, 1), agentsTalkedTo = C8(r, 2), wingmenHired = C8(r, 3),
                asteroidsDestroyed = C8(r, 4), offersDeclined = C8(r, 6), offersRepeated = C8(r, 7), acceptedBlindRisk = C8(r, 8),
                acceptedBlindMap = C8(r, 9),
                battleshipsDestroyed = r.status118,
                cratesSalvaged = r.capturedCrates,
                highestCredits = Math.Max(0, r.credits),
                kaamoState = r.kaamoState,
                kaamoItems = Goods(r.kaamoItems),
                kaamoShips = r.kaamoShips.Where(k => Ship(k.ship)).Select(k => new StoredShip(k.ship, k.race, k.mods.ToList())).ToList(),
                pirateBaseDestroyed = Bools(r.status4c, 4),
                hints = Enumerable.Range(0, r.hints.Length).Where(h => r.hints[h]).ToList(),
                lomaTollPaid = r.status110, lomaTollRefused = r.status111,
                shipMods = r.shipMods.ToList(),
                storyTargets = r.storyTargets.ToList(),
                collectedBounties = r.collectedBounties.Length == 4 ? r.collectedBounties.ToArray() : new int[4],
                hiddenBlueprintsFound = Enumerable.Range(0, r.status58.Length).Where(i => r.status58[i]).Sum(i => 1 << i),
                storyAgentsAccepted = r.agents.Where(a => a.index >= 0 && a.acceptedOffer).Select(a => a.index).ToList(),
            };
            if (r.medals.Length > 0) { s.medals = new int[45]; for (int i = 0; i < 45 && i < r.medals.Length; i++) s.medals[i] = r.medals[i]; }
            // hints[0x2a] (601, the Terran board) and 0x33-0x36 (3232) as WantedHints' bits 0 / 2-5.
            s.wantedHints = (Hint(r, 0x2a) ? 1 : 0) | Enumerable.Range(0, 4).Where(k => Hint(r, 0x33 + k)).Sum(k => 4 << k);

            // Galaxy::getVisited, the station memory (Status+0x19c: newest first, the current station last)
            s.visitedStations = Indices(r.visitedStations, 0, Station);
            if (!s.visitedStations.Contains(station)) s.visitedStations.Add(station);
            s.recentStations = new List<StationStock>();
            for (int i = stack.Length - 2; i >= -1; i--)
            {
                var st = i >= 0 ? stack[i] : current;
                if (st == null || !Station(st.index)) continue;
                s.recentStations.RemoveAll(x => x.station == st.index);
                var stock = new StationStock { station = st.index, items = Goods(st.items), ships = st.ships.Where(x => Ship(x.ship)).Select(x => x.ship).ToList() };
                foreach (var sh in st.ships) if (sh.mods.Length > 0 && Ship(sh.ship)) stock.shipMods.Add(new StoredShip(sh.ship, sh.race, sh.mods.ToList()));
                s.recentStations.Add(stock);   // agents empty: the next docking rolls the bar (Shop.EnterStation)
            }
            while (s.recentStations.Count > Shop.RecentStationCount) s.recentStations.RemoveAt(0);
            s.attackedStations = stack.Where(x => x != null && x.attackedFriends && Station(x.index)).Select(x => x.index).Distinct().ToList();
            s.seenItems = Indices(r.itemsObtained, 0, Item);
            s.systemVisible = r.systemsVisible.Length > 0 ? Enumerable.Range(0, db.Systems.Count).Select(i => i < r.systemsVisible.Length && r.systemsVisible[i]).ToArray() : null;
            s.highestPrices = Prices(r.status40, r.status48, Item);
            s.lowestPrices = Prices(r.status3c, r.status44, Item);

            // The story step's mission (story.json), with the original's progress where it is the same step's.
            var step = StoryMission.From(StoryTable.Step(index));
            var om = r.storyMission;
            if (om != null && !om.Empty && om.type == step.type)
            {
                step.value = om.status;
                step.visible = om.visible;
                if (om.goodAmount > 0) step.goodsAmount = om.goodAmount;
            }
            s.storyMission = step;
            s.freelanceMission = Freelance(r.freelanceMission, db, notes);

            // Blueprints (Status+0x18: the products in item order) and the products waiting (+0x1c)
            var products = db.Items.Where(i => !i.modded && i.blueprint != null && i.blueprint.Count > 0).OrderBy(i => i.index).Select(i => i.index).ToList();
            s.unlockedBlueprints = new List<int>();
            s.blueprints = new List<BlueprintState>();
            for (int i = 0; i < r.blueprints.Count && i < products.Count; i++)
            {
                var b = r.blueprints[i];
                int item = products[i];
                if (b.unlocked) s.unlockedBlueprints.Add(item);
                var recipe = db.Item(item).blueprint.Select(p => p.amount).ToList();
                bool touched = b.field4 > 0 || b.fieldC > 0 || b.station >= 0 || !b.invested.SequenceEqual(recipe);
                if (touched)
                    s.blueprints.Add(new BlueprintState { item = item, remaining = b.invested.ToList(), moneySpent = b.field4, timesProduced = b.fieldC,
                                                          station = Station(b.station) ? b.station : -1 });
            }
            s.pendingProducts = r.pendingProducts.Where(p => Item(p.a) && Station(p.c))
                                 .Select(p => new PendingProduct { item = p.a, quantity = p.b, station = p.c }).ToList();

            // Wingmen (Status+0x24 names, +0x2c race, +0x30 contract ms, +0x28 the hiring agent's portrait)
            if (r.wingmen != null && r.wingmen.names.Count > 0)
            {
                s.wingmen = r.wingmen.names.ToList();
                s.wingmanRace = r.wingmen.status2c;
                s.wingmanContractMs = Math.Max(0, r.wingmen.status30);
                s.wingmanPortrait = r.wingmen.status28.Length == 5 ? r.wingmen.status28.ToArray() : new int[5];
            }
            else s.wingmen = new List<string>();

            // Status+0x8c: the own ship parked while a story loaner is flown
            if (r.parkedShip != null && Ship(r.parkedShip.ship))
            {
                s.hasParkedShip = true;
                s.parkedShip = new ParkedShip { ship = r.parkedShip.ship, equipment = Equip(r.parkedShip.equipment), cargo = Goods(r.parkedShip.cargo),
                                                mods = r.parkedShipMods.ToList() };
            }

            // Status+0x00: the Most Wanted board, by wanted.json index
            s.wanted = new List<WantedState>();
            foreach (var w in r.wanted.OrderBy(w => w.index))
            {
                if (w.index < 0 || w.index >= db.Wanted.Count) continue;
                while (s.wanted.Count < w.index) s.wanted.Add(new WantedState());
                s.wanted.Add(new WantedState { active = w.active, terminated = w.terminated, current = Station(w.location) ? w.location : -1,
                                               travelsTo = Station(w.travelsTo) ? w.travelsTo : -1, lastSeen = Station(w.lastSeen) ? w.lastSeen : -1 });
            }

            s.unsaleable = unsaleable.ToList();
            if (dropped > 0) notes?.Add($"{dropped} item stacks this game doesn't know were left out");
            if (r.visitedStations.Length != db.Stations.Count) notes?.Add($"a save of a version with {r.visitedStations.Length} stations (this game has {db.Stations.Count})");
            return s;
        }

        static int C8(OriginalRecord r, int i) => i < r.statusC8.Length ? r.statusC8[i] : 0;
        static bool Hint(OriginalRecord r, int h) => h < r.hints.Length && r.hints[h];
        static bool[] Bools(bool[] a, int n) { var b = new bool[n]; for (int i = 0; i < n && i < (a?.Length ?? 0); i++) b[i] = a[i]; return b; }

        static List<SaveData.KnownPrice> Prices(int[] price, int[] system, Func<int, bool> item)
        {
            var l = new List<SaveData.KnownPrice>();
            for (int i = 0; i < price.Length && i < system.Length; i++)
                if (price[i] > 0 && item(i)) l.Add(new SaveData.KnownPrice { item = i, price = price[i], system = system[i] });
            return l;
        }

        /// <summary>The bar mission under way (Status missions[1]), its offer text the generic reward line (764).</summary>
        static FreelanceMission Freelance(OriginalMission m, Database db, List<string> notes)
        {
            if (m == null || m.Empty) return new FreelanceMission();
            if (m.type < 0 || m.type > 15) { notes?.Add("an unknown bar mission was left out"); return new FreelanceMission(); }
            int clientStation = m.agent != null && m.agent.station >= 0 && m.agent.station < db.Stations.Count ? m.agent.station : -1;
            return new FreelanceMission
            {
                type = m.type, clientName = m.clientName ?? "", clientRace = m.clientRace, clientMale = m.agent?.male ?? true,
                clientPortrait = m.clientImage != null && m.clientImage.Length == 5 ? m.clientImage.ToArray() : new int[5],
                clientStation = clientStation, target = m.targetStation >= 0 && m.targetStation < db.Stations.Count ? m.targetStation : -1,
                difficulty = m.difficulty, good = m.goodIndex, amount = m.goodAmount, reward = m.reward, bonus = m.bonus, costs = m.costs,
                promisedBonus = m.bonus, targetName = m.targetName ?? "", status = m.status,
                textIds = new List<int> { -1, -1, -1, -1, 764, -1 },
            };
        }

        // ---- export -----------------------------------------------------------------------------------------------

        /// <summary>A remake save as the original's record (Android versions' tables). 'notes': what was left out.</summary>
        public static OriginalRecord ToRecord(SaveData s, Database db, List<string> notes)
        {
            bool OrigItem(int i) => i >= 0 && i < db.Items.Count && db.Item(i) != null && !db.Item(i).modded;
            bool OrigShip(int i) => i >= 0 && i < db.Ships.Count && db.Ship(i) != null && i < 64;
            int modItems = 0;
            var unsaleable = new HashSet<int>(s.unsaleable ?? new List<int>());
            var r = new OriginalRecord();
            int items = db.Items.Count(i => !i.modded);

            r.visitedStations = new bool[db.Stations.Count];
            foreach (int v in s.visitedStations ?? new List<int>()) if (v >= 0 && v < r.visitedStations.Length) r.visitedStations[v] = true;
            r.credits = (int)System.Math.Min(int.MaxValue, System.Math.Max(0L, s.credits));   // the original's int (remake: 64-bit)
            r.rating = 0;
            r.playingTimeMs = (long)(s.playSeconds * 1000.0);
            r.kills = s.kills; r.missionCount = s.freelanceCompleted; r.goodsProduced = s.goodsProduced;
            r.stationsVisited = r.visitedStations.Count(v => v);
            r.campaignMission = s.freePlay ? Session.FreePlayMission : s.campaignMission;
            r.level = LevelOf(s);
            r.freelanceMission = FreelanceRecord(s.freelanceMission, db, notes);
            r.storyMission = StoryRecord(s.freePlay ? null : s.storyMission, db);
            r.jumpgatesUsed = s.jumpgatesUsed; r.capturedCrates = s.cratesSalvaged; r.pirateKills = s.pirateKills;
            r.status80 = s.voidInvasionStation; r.status7c = s.voidInvasionSystem; r.voidReturnStation = 10; r.status88 = s.invasionDepartures;
            r.oreTypesMined = Flags(s.oreTypesMined, FirstOre, 11);
            r.coreTypesMined = Flags(s.coreTypesMined, FirstCore, 11);
            r.courierGoods = s.containersDelivered; r.oresMined = s.oreMined; r.coresMined = s.coresMined; r.boozeBought = s.boozeBought;
            r.boozeTypes = Flags(s.boozeTypes, FirstBooze, 22);
            r.spaceJunk = s.junkDestroyed;
            r.statusB4 = new bool[db.Systems.Count];
            foreach (int v in s.visitedStations ?? new List<int>())
            {
                int sys = db.Stations.Find(x => x.index == v)?.system ?? -1;
                if (sys >= 0 && sys < r.statusB4.Length) r.statusB4[sys] = true;
            }
            r.passengersCarried = s.passengersDelivered;
            r.cloakMs = s.cloakMs;
            r.statusC8 = new[] { s.bombsDetonated, s.alienRemainsCollected, s.agentsTalkedTo, s.wingmenHired, s.asteroidsDestroyed, 0,
                                 s.offersDeclined, s.offersRepeated, s.acceptedBlindRisk, s.acceptedBlindMap };
            r.medals = new int[45];
            if (s.medals != null) for (int i = 0; i < 45 && i < s.medals.Length; i++) r.medals[i] = s.medals[i];

            // The ship: a mod's ship can't go; the Phantom then, and its gear into the hold
            int ship = s.ship;
            if (!OrigShip(ship)) { notes?.Add("the ship is a mod's: a Phantom in its place"); ship = 10; }
            r.ship = ship;
            r.shipRace = ship < Shop.ShipRace.Length ? Shop.ShipRace[ship] : 0;
            var cargo = (s.cargo ?? new List<ItemStack>()).Where(c => c != null).Select(c => c.Clone()).ToList();
            r.equipment = Slots(db, ship, s.equipment?.Where(e => !e.builtIn).ToList(), s.shipMods, unsaleable, cargo, notes, ref modItems);   // built-ins are the remake mods' ships'
            r.cargo = Goods(cargo, db, s.station, unsaleable, ref modItems);

            // The station memory: the 3 stations, newest first, then the current one
            var recent = (s.recentStations ?? new List<StationStock>()).Where(x => x != null && x.station >= 0 && x.station < db.Stations.Count).ToList();
            var stack = new OriginalStation[4];
            var newestFirst = Enumerable.Reverse(recent).ToList();
            for (int i = 0; i < 3 && i < newestFirst.Count; i++) stack[i] = StationRecord(newestFirst[i], db, s, ref modItems);
            var cur = recent.LastOrDefault(x => x.station == s.station);
            stack[3] = cur != null ? StationRecord(cur, db, s, ref modItems)
                     : s.station >= 0 && s.station < db.Stations.Count ? new OriginalStation { index = s.station } : stack[0];
            if (stack[3] != null && stack[0] != null && stack[0].index == stack[3].index) stack[3] = stack[0];
            r.stations = stack;
            r.standings = s.standing != null && s.standing.Length == 2 ? s.standing.ToArray() : new[] { 30, 0 };

            // Blueprints: every product in item order (BluePrint::BluePrint: the recipe as what is still missing)
            var products = db.Items.Where(i => !i.modded && i.blueprint != null && i.blueprint.Count > 0).OrderBy(i => i.index).ToList();
            var unlocked = new HashSet<int>(s.unlockedBlueprints ?? new List<int>());
            foreach (var p in products)
            {
                var st = s.blueprints?.Find(b => b != null && b.item == p.index);
                var recipe = p.blueprint.Select(x => x.amount).ToArray();
                var invested = st != null && st.remaining != null && st.remaining.Count == recipe.Length ? st.remaining.ToArray() : recipe;
                int station = st?.station ?? -1;
                r.blueprints.Add(new OriginalBlueprint
                {
                    invested = invested, field4 = st?.moneySpent ?? 0, unlocked = unlocked.Contains(p.index), fieldC = st?.timesProduced ?? 0,
                    station = station, stationName = station >= 0 ? StationName(db, station) : "",
                });
            }
            foreach (var pp in s.pendingProducts ?? new List<PendingProduct>())
                if (pp != null && OrigItem(pp.item))
                    r.pendingProducts.Add(new OriginalPendingProduct { a = pp.item, b = pp.quantity, c = pp.station, name = StationName(db, pp.station) });

            if (s.wingmen != null && s.wingmen.Count > 0)
                r.wingmen = new OriginalWingmen { names = s.wingmen.ToList(), status2c = s.wingmanRace, status30 = (int)s.wingmanContractMs,
                                                  status28 = s.wingmanPortrait != null && s.wingmanPortrait.Length == 5 ? s.wingmanPortrait.ToArray() : new int[5] };
            r.passengers = s.passengers;
            r.systemsVisible = s.systemVisible != null && s.systemVisible.Length > 0 ? s.systemVisible.Take(db.Systems.Count).ToArray()
                             : db.Systems.Select(x => x.initiallyVisible).ToArray();
            // Status+0x3c..+0x48: a price and its system per item (0 = unknown, Status::resetGame)
            r.status40 = new int[items]; r.status48 = new int[items]; r.status3c = new int[items]; r.status44 = new int[items];
            foreach (var p in s.highestPrices ?? new List<SaveData.KnownPrice>()) if (p.item >= 0 && p.item < items) { r.status40[p.item] = p.price; r.status48[p.item] = p.system; }
            foreach (var p in s.lowestPrices ?? new List<SaveData.KnownPrice>()) if (p.item >= 0 && p.item < items) { r.status3c[p.item] = p.price; r.status44[p.item] = p.system; }
            r.status4c = Bools(s.pirateBaseDestroyed, 4);

            // Status::getAgents: the story agents (agents.json), the ones whose offer was taken marked (Agent+0x74)
            var accepted = new HashSet<int>(s.storyAgentsAccepted ?? new List<int>());
            foreach (var sa in AgentData.StoryAgents)
                r.agents.Add(new OriginalAgent
                {
                    index = sa.index, name = sa.name ?? "", station = sa.station, system = sa.system, race = sa.race, male = sa.male,
                    sellSystem = sa.sellItemSystem, sellBlueprint = sa.sellBlueprint, sellMod = sa.sellMod, sellItemPrice = sa.sellItemPrice,
                    offer = sa.sellBlueprint >= 0 ? 3 : sa.sellItemSystem >= 0 ? 4 : sa.sellMod >= 0 ? 8 : sa.index == 26 ? 10 : 9,
                    imageParts = sa.portraitParts != null && sa.portraitParts.Length == 5 ? sa.portraitParts.ToArray() : new int[5],
                    acceptedOffer = accepted.Contains(sa.index),
                });

            var hints = new HashSet<int>(s.hints ?? new List<int>());
            if ((s.wantedHints & 1) != 0) hints.Add(0x2a);
            for (int k = 0; k < 4; k++) if ((s.wantedHints & (4 << k)) != 0) hints.Add(0x33 + k);
            for (int h = 0; h < r.hints.Length; h++) r.hints[h] = hints.Contains(h);
            r.difficulty = s.difficulty;
            r.status100 = (long)(s.storyStepStart * 1000.0);
            if (s.hasParkedShip && s.parkedShip != null && OrigShip(s.parkedShip.ship))
            {
                var pc = (s.parkedShip.cargo ?? new List<ItemStack>()).Select(c => c.Clone()).ToList();
                r.parkedShip = new OriginalParkedShip { ship = s.parkedShip.ship, race = s.parkedShip.ship < Shop.ShipRace.Length ? Shop.ShipRace[s.parkedShip.ship] : 0 };
                r.parkedShip.equipment = Slots(db, s.parkedShip.ship, s.parkedShip.equipment, s.parkedShip.mods, unsaleable, pc, notes, ref modItems);
                r.parkedShip.cargo = Goods(pc, db, s.station, unsaleable, ref modItems);
                r.parkedShipMods = OneOfEach(s.parkedShip.mods, notes);
            }
            r.storyTargets = (s.storyTargets ?? new List<int>()).ToArray();
            r.status110 = s.lomaTollPaid; r.status111 = s.lomaTollRefused;
            r.kaamoState = s.kaamoState;
            r.kaamoItems = Goods(s.kaamoItems ?? new List<ItemStack>(), db, 108, unsaleable, ref modItems);
            foreach (var k in s.kaamoShips ?? new List<StoredShip>())
            {
                if (k == null || !OrigShip(k.ship)) { notes?.Add("a mod's ship in the Kaamo Club was left out"); continue; }
                r.kaamoShips.Add(new OriginalShip { ship = k.ship, race = k.race, mods = OneOfEach(k.mods, notes) });
                foreach (var e in k.equipment ?? new List<ItemStack>())   // the original's stored hulls are bare: their gear into the storage
                    if (e != null && OrigItem(e.item) && Hangar.UnitsOf(db, e) > 0)
                        r.kaamoItems.Add(new OriginalItem { item = e.item, amount = Hangar.UnitsOf(db, e), price = db.Item(e.item).maxPrice });
            }
            r.itemsObtained = new bool[items];
            foreach (int i in s.seenItems ?? new List<int>()) if (i >= 0 && i < items) r.itemsObtained[i] = true;

            r.extended = true;
            r.shipMods = OneOfEach(s.shipMods, notes);
            r.wanted = new List<OriginalWanted>();
            for (int i = 0; i < db.Wanted.Count; i++)
            {
                var w = db.Wanted[i];
                if (w.IsMod) break;   // the original's 25 only (a mod's criminals follow them)
                var st = s.wanted != null && i < s.wanted.Count ? s.wanted[i] : new WantedState();
                r.wanted.Add(new OriginalWanted
                {
                    active = st.active, terminated = st.terminated, location = st.current, travelsTo = st.travelsTo, lastSeen = st.lastSeen,
                    name = w.name ?? "", index = w.index, board = w.board, race = w.race, male = w.male, ship = w.ship, weapon = w.weapon,
                    hitpoints = w.hitpoints, loot = w.loot, lootAmount = w.lootAmount, reward = w.reward, requiredBounties = w.requiredBounties,
                    requiredMission = w.requiredMission, wingmen = w.numWingmen,
                    imageParts = w.portraitParts != null && w.portraitParts.Length == 5 ? w.portraitParts.ToArray() : new int[5],
                });
            }
            r.collectedBounties = s.collectedBounties != null && s.collectedBounties.Length == 4 ? s.collectedBounties.ToArray() : new int[4];
            r.status174 = s.storyCounter;
            r.status58 = Enumerable.Range(0, 5).Select(k => (s.hiddenBlueprintsFound & (1 << k)) != 0).ToArray();
            r.status118 = s.battleshipsDestroyed;

            if (modItems > 0) notes?.Add($"{modItems} mod item stacks were left out");
            if (s.graphQuests != null && s.graphQuests.Count > 0) notes?.Add("the event graph quests under way were left out");
            if (!string.IsNullOrEmpty(s.modCampaign)) notes?.Add("a mod's campaign: it plays as the original's story step");
            if (s.freePlay) notes?.Add("a free-play game: the original has no free play, it continues at story step 20");
            return r;
        }

        /// <summary>The slot list's preview (recordStoreWritePreview).</summary>
        public static OriginalPreview Preview(OriginalRecord r, Database db)
        {
            int station = r.stations != null && r.stations.Length > 0 && r.stations[r.stations.Length - 1] != null ? r.stations[r.stations.Length - 1].index : 78;
            var st = db.Stations.Find(x => x.index == station);
            return new OriginalPreview
            {
                playingTimeMs = r.playingTimeMs, credits = r.credits, station = st?.name ?? "", system = st != null ? db.Systems.Find(x => x.index == st.system)?.name ?? "" : "",
                campaignMission = r.campaignMission, level = r.level, difficulty = r.difficulty, ship = r.ship,
            };
        }

        /// <summary>Status::getLevel: the rank from the XP (Session.Xp's formula on the save's values).</summary>
        static int LevelOf(SaveData s)
        {
            int[] rankXp = { 0, 7, 21, 42, 70, 105, 147, 196, 252, 315, 385, 462, 546, 637, 735, 840, 952, 1071, 1197, 1330, 1650 };
            int xp = s.oreMined / 50 + s.kills + s.wingmenHired / 3 + s.coresMined + 2 * s.freelanceCompleted
                   + (s.freePlay ? 0 : s.campaignMission) + (s.visitedStations?.Count ?? 0);
            int level = 0;
            for (int i = 0; i < rankXp.Length; i++) if (xp >= rankXp[i]) level = i;
            return level;
        }

        static string StationName(Database db, int station) => db.Stations.Find(x => x.index == station)?.name ?? "";

        static bool[] Flags(IEnumerable<int> items, int first, int count)
        {
            var a = new bool[count];
            if (items != null) foreach (int i in items) if (i - first >= 0 && i - first < count) a[i - first] = true;
            return a;
        }

        /// <summary>Ship::getMods: each mod once (the remake's stacked levels can't go).</summary>
        static int[] OneOfEach(List<int> mods, List<string> notes)
        {
            if (mods == null) return new int[0];
            var one = mods.Where(m => m >= 0 && m <= 3).Distinct().ToArray();
            if (one.Length < mods.Count) notes?.Add("Kaamo upgrades above level one were left out");
            return one;
        }

        /// <summary>The equipment array as the original keeps it: every slot by type (primary, secondary, turret, equipment;
        /// the equipment slot mod adds one), an empty one -1; what doesn't fit goes to the hold.</summary>
        static List<OriginalEquipment> Slots(Database db, int ship, List<ItemStack> mounted, List<int> mods, HashSet<int> unsaleable,
                                             List<ItemStack> cargo, List<string> notes, ref int modItems)
        {
            var slots = db.Ship(ship)?.slots ?? new ShipSlots();
            int[] counts = { slots.primary, slots.secondary, slots.turret, slots.equipment + (mods != null && mods.Contains(2) ? 1 : 0) };
            var list = new List<OriginalEquipment>();
            int moved = 0;
            for (int type = 0; type < 4; type++)
            {
                var ofType = (mounted ?? new List<ItemStack>()).Where(e => e != null && (db.Item(e.item)?.TypeId ?? 4) == type).ToList();
                for (int k = 0; k < counts[type]; k++)
                {
                    if (k >= ofType.Count) { list.Add(new OriginalEquipment()); continue; }
                    var e = ofType[k];
                    if (db.Item(e.item).modded) { modItems++; list.Add(new OriginalEquipment()); continue; }
                    int units = Hangar.UnitsOf(db, e);   // an empty secondary leaves its slot empty, not 1 round
                    if (units <= 0) { list.Add(new OriginalEquipment()); continue; }
                    list.Add(new OriginalEquipment { item = e.item, amount = units, unsaleable = unsaleable.Contains(e.item) });
                }
                for (int k = counts[type]; k < ofType.Count; k++)
                {
                    int n = Hangar.UnitsOf(db, ofType[k]);
                    if (n > 0) cargo.Add(new ItemStack(ofType[k].item, n));
                    moved++;
                }
            }
            if (moved > 0) notes?.Add($"{moved} items past the ship's slots (the remake's cloak bay or upgrades) went to the hold");
            return list;
        }

        static List<OriginalItem> Goods(List<ItemStack> stacks, Database db, int station, HashSet<int> unsaleable, ref int modItems)
        {
            var keep = new List<ItemStack>();
            foreach (var c in stacks)
            {
                if (c == null) continue;
                var it = db.Item(c.item);
                if (it == null || it.modded) { modItems++; continue; }
                var same = keep.Find(k => k.item == c.item);
                if (same != null) same.amount += c.amount; else keep.Add(new ItemStack(c.item, c.amount));
            }
            var prices = station >= 0 && station < db.Stations.Count ? Shop.PriceList(db, station, keep.Select(k => k.item).ToList()) : null;
            return keep.Select((k, i) => new OriginalItem
            {
                item = k.item, amount = Math.Max(1, k.amount), price = prices != null && prices[i] > 0 ? prices[i] : db.Item(k.item).maxPrice,
                unsaleable = unsaleable.Contains(k.item),
            }).ToList();
        }

        static OriginalStation StationRecord(StationStock st, Database db, SaveData s, ref int modItems)
        {
            var o = new OriginalStation
            {
                index = st.station,
                items = Goods(st.items ?? new List<ItemStack>(), db, st.station, new HashSet<int>(), ref modItems),
                attackedFriends = s.attackedStations != null && s.attackedStations.Contains(st.station),
            };
            foreach (int ship in st.ships ?? new List<int>())
            {
                if (ship < 0 || ship >= 64 || db.Ship(ship) == null) continue;
                var mods = st.ModsOf(ship);
                o.ships.Add(new OriginalShip { ship = ship, race = ship < Shop.ShipRace.Length ? Shop.ShipRace[ship] : 0,
                                               mods = (mods ?? new List<int>()).Where(m => m >= 0 && m <= 3).Distinct().ToArray() });
            }
            return o;   // no bar visitors: the original rolls them when the station is new to its stack
        }

        static OriginalMission StoryRecord(StoryMission m, Database db)
        {
            if (m == null || m.IsEmpty) return new OriginalMission();
            var st = db.Stations.Find(x => x.index == m.station);
            return new OriginalMission
            {
                type = m.type, campaign = true, targetStation = m.station, reward = m.reward, goodIndex = m.goodsItem, goodAmount = m.goodsAmount,
                status = m.value, visible = m.visible, difficulty = 1,
                targetStationName = st?.name ?? "", targetSystemName = st != null ? db.Systems.Find(x => x.index == st.system)?.name ?? "" : "",
            };
        }

        static OriginalMission FreelanceRecord(FreelanceMission m, Database db, List<string> notes)
        {
            if (m == null || m.IsEmpty) return new OriginalMission();
            if (m.type > 14) { notes?.Add("a bar mission the original doesn't offer (Ore Mining) was left out"); return new OriginalMission(); }
            var st = db.Stations.Find(x => x.index == m.target);
            return new OriginalMission
            {
                type = m.type, clientName = m.clientName ?? "", targetName = m.targetName ?? "", campaign = false,
                targetStationName = st?.name ?? "", targetSystemName = st != null ? db.Systems.Find(x => x.index == st.system)?.name ?? "" : "",
                clientImage = m.clientPortrait != null && m.clientPortrait.Length == 5 ? m.clientPortrait.ToArray() : null,
                clientRace = m.clientRace, costs = m.costs, bonus = m.promisedBonus >= 0 ? m.promisedBonus : m.bonus, reward = m.reward,
                targetStation = m.target, difficulty = m.difficulty, goodIndex = m.good, goodAmount = m.amount, status = m.status, visible = true,
            };
        }
    }
}
