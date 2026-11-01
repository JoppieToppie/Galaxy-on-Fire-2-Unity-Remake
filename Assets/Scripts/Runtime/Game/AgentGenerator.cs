// AgentGenerator.cs
// The bar's agents and their freelance missions (Reference/research/freelance_missions.md 1.3-2.5; the Python port
// Reference/tools/missions/mission_tables.py). Plain C#:
//   CreateAgents        Generator::createAgents 0xa1fd0: story agents of the station (campaign > 16) + generic ones up to
//                       3-5 visitors (station 108: story agents only), one wingman offer per bar, diplomats for hostile
//                       races (35 %), 1 %: the first mission under 50 000 pays 10x (max 50 000)
//   CreateAgent         Generator::createAgent 0xa26c0: race (20 % any of 8), offer, gender, name, portrait, seller /
//                       wingmen data
//   CreateMission       Generator::createMission 0xa2a7c: target (generateStationIndex 0xa1e2c), type (every type once
//                       before repeats, Status+0x50), difficulty, parameters, reward / bonus / costs
// The original's single time-seeded java.util.Random isn't reproducible, so System.Random stands in.

using System;
using System.Collections.Generic;
using GoF2Remake.Flight;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class AgentGenerator
    {
        /// <summary>DAT_00251e70: stations never used as mission targets.</summary>
        static readonly HashSet<int> ExcludedTargets = new HashSet<int>
        {
            1, 10, 11, 15, 22, 27, 29, 30, 33, 38, 40, 47, 48, 55, 56, 58, 60, 65, 66, 70, 74, 76, 79, 80, 81, 82, 83, 85, 86,
            90, 91, 92, 93, 94, 95, 98, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 113, 121, 126, 131,
        };
        /// <summary>DAT_00251f60: the only types before campaign mission 16.</summary>
        static readonly int[] EarlyTypes = { 11, 0, 7, 4, 12 };
        /// <summary>DAT_00251d10: the Kaamo Club special items (agent 25).</summary>
        static readonly int[] KaamoItems = { 200, 220, 208, 213, 216, 228, 229, 230, 231 };
        static readonly HashSet<int> NotForSale = new HashSet<int> { 131, 164, 175, 217, 218 };
        static readonly HashSet<int> NotForPurchase = new HashSet<int> { 115, 116, 117, 131, 164, 175, 217, 218 };

        public static Random Rng = new Random();
        static int R(int n) => n > 0 ? Rng.Next(n) : 0;

        // ---- helpers ----------------------------------------------------------------------------------------

        /// <summary>The original's rounding to 50 credits: down, except a remainder of exactly 25 rounds up.</summary>
        public static int Round50(float value)
        {
            int x = (int)value, m = x % 50;
            return m == 0 || m == 25 ? x + m : x - m;
        }

        /// <summary>Standing::getMissionBonus 0x142b34: max(0, standing toward the race / 100), races 0..3.</summary>
        public static float MissionBonus(int race) => race >= 0 && race <= 3 ? Math.Max(0f, Standing.RawToward(race) / 100f) : 0f;

        static int SystemOf(Database db, int station) => db.Stations.Find(s => s.index == station)?.system ?? 0;
        static SystemData Sys(Database db, int system) => db.Systems.Find(s => s.index == system);

        /// <summary>Galaxy::distance 0x1a4e7c: map x, y and int(z / 10).</summary>
        public static float Distance(Database db, int systemA, int systemB)
        {
            if (systemA == systemB) return 0f;
            var a = Sys(db, systemA)?.mapPosition; var b = Sys(db, systemB)?.mapPosition;
            if (a == null || b == null) return 0f;
            float dx = a.x - b.x, dy = a.y - b.y, dz = a.z / 10 - b.z / 10;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>Item+0x18 "single price" = (min + max) / 2.</summary>
        public static int SinglePrice(ItemData it) => it == null ? 0 : (it.minPrice + it.maxPrice) / 2;

        /// <summary>Globals::getRandomName 0xf85a0: "first last" from the race's lists; Cyborg and Grey have first names only,
        /// Midorians pick Terran or Nivelian lists (two independent 50 % picks).</summary>
        public static string RandomName(int race, bool male)
        {
            race = Standing.Look(race);   // remake mods: a mod race's names are its look race's
            string first, last;
            switch (race)
            {
                case 0: first = male ? "terran_0_m" : "terran_0_w"; last = "terran_1"; break;
                case 3: first = R(2) == 0 ? "terran_0_m" : "nivelian_0"; last = R(2) == 0 ? "terran_1" : "nivelian_1"; break;
                case 1: first = "vossk_0"; last = "vossk_1"; break;
                case 2: first = "nivelian_0"; last = "nivelian_1"; break;
                case 4: first = "multipod_0"; last = "multipod_1"; break;
                case 5: first = "cyborg_0"; last = null; break;
                case 6: first = "bobolan_0"; last = "bobolan_1"; break;
                case 7: first = "grey_0"; last = null; break;
                default: return "";
            }
            var n = AgentData.Names;
            string Pick(string key) => n.TryGetValue(key, out var l) && l.Count > 0 ? l[R(l.Count)] : "";
            string f = Pick(first);
            return last == null ? f : f + " " + Pick(last);
        }

        /// <summary>ImageFactory::createChar 0x14173c: parts[0] = the set (Midorian 75 % Nivelian / 25 % Terran, Cyborg
        /// Terran, female Terran 10), parts[1..4] random within the set's counts.</summary>
        public static int[] CreatePortrait(bool male, int race)
        {
            race = Standing.Look(race);   // remake mods: a mod race's faces are its look race's
            int set = race;
            if (race == 3) set = R(100) < 75 ? 2 : 0;
            else if (race == 5) set = 0;
            if (race == 0 && !male) set = 10;
            var p = new int[5];
            p[0] = set;
            var counts = AgentData.PortraitPartCounts[Math.Min(set, AgentData.PortraitPartCounts.Length - 1)];
            for (int k = 0; k < 4; k++) p[k + 1] = R(counts[k]);
            return p;
        }

        // ---- agents -----------------------------------------------------------------------------------------

        /// <summary>Generator::createAgents 0xa1fd0 for 'station' (not in supernova systems).</summary>
        public static List<Agent> CreateAgents(Database db, int station)
        {
            var agents = new List<Agent>();
            // Generator::createAgents: nobody in the supernova system's lounges (Status::inSupernovaSystem, system 27 before
            // campaign 0x9e): the evacuated Ginoya stations (Var Lupra had visitors in the remake until 2026-10).
            if (Shop.InSupernovaSystem(SystemOf(db, station), station)) return agents;
            int campaign = Session.CampaignMission;
            var story = new List<StoryAgentData>();
            if (campaign > 16)
                foreach (var s in AgentData.StoryAgents)
                    // Generator::createAgents: Sao Perula's (106, Miguel Parham, the Liberator blueprint) only once the
                    // Valkyrie add-on is won (Status::dlc1Won).
                    if (s.station == station && (s.station != 106 || Story.Dlc1Won)) story.Add(s);

            int n = story.Count;
            if (station != 108) n = R(2) + story.Count + 3 < 5 ? story.Count + 3 + R(2) : 5;

            foreach (var s in story) agents.Add(StoryAgent(db, s));
            // Campaign 23 at station 10: an extra agent sells the AB-1 Retractor (item 68) for 100 000 (the Android build; the iOS
            // build, the Default economy, puts a 40 % AB-1 in every shop during steps 17..24 instead, Shop.GenerateItems).
            if (campaign == 23 && station == 10 && Session.Economy == Economy.Android)
                agents.Add(new Agent { name = RandomName(0, true), race = 0, station = station, offer = AgentOffer.SellItem,
                                           portrait = CreatePortrait(true, 0), sellItem = 68, sellQuantity = 1, sellPrice = 100000 });

            bool wingman = false;
            while (agents.Count < n)
            {
                var a = CreateAgent(db, station);
                if (a.offer == AgentOffer.Wingmen)
                {
                    if (wingman) a.offer = AgentOffer.SmallTalk;   // only the first wingman offer of a bar stays
                    wingman = true;
                }
                else if (a.offer == AgentOffer.Mission) a.mission = CreateMission(db, a, station, false);
                agents.Add(a);
            }

            // 35 %: a diplomat for each hostile race (DAT_00251f80 = 2, 3, 0, 1) replaces the first generic agent.
            if (R(100) < 35)
                foreach (int race in new[] { 2, 3, 0, 1 })
                {
                    if (!Standing.IsEnemy(race)) continue;
                    int i = agents.FindIndex(x => !x.IsStory && x.offer != AgentOffer.Diplomat);
                    if (i < 0) continue;
                    agents[i] = new Agent { name = RandomName(race, true), race = race, station = station, offer = AgentOffer.Diplomat,
                                                portrait = CreatePortrait(true, race), costs = DiplomatCosts(race) };
                }
            AddModRaceDiplomats(db, station, agents);

            // Campaign 23 at station 10, or 1 %: the first mission under 50 000 pays 10x (max 50 000); the Android build only.
            if (Session.Economy == Economy.Android && ((campaign == 23 && station == 10) || R(100) == 1))
            {
                var a = agents.Find(x => x.offer == AgentOffer.Mission && x.HasMission && x.mission.reward < 50000);
                if (a != null) a.mission.reward = Math.Min(a.mission.reward * 10, 50000);
            }
            AddCustomShipSellers(db, station, agents);
            AddModBlueprintSellers(db, station, agents);
            AddStoryBlueprintSellers(db, station, agents);
            return agents;
        }

        /// <summary>Remake (players' report): the blueprints only the campaign unlocks (Blueprints.UnlockFromStory: steps 34,
        /// 72, 104, 141) can't be had in free play, which runs no story steps (multiplayer's finished world, old free-play
        /// saves). There a visitor in a Loma lounge (system 25, the black market) sells it, at 1.5x the product's highest price,
        /// until it is known: (product, station): Var Destro (105), Sao Perula (106), Quineros (107).</summary>
        static readonly (int product, int station)[] StoryBlueprints = { (85, 105), (183, 105), (206, 107), (210, 107) };

        static void AddStoryBlueprintSellers(Database db, int station, List<Agent> agents)
        {
            if (!Session.FreePlay || station == 108 || station == 101 || Shop.InSupernovaSystem(SystemOf(db, station), station)) return;
            int systemRace = Sys(db, SystemOf(db, station))?.raceId ?? -1;
            foreach (var (product, at) in StoryBlueprints)
            {
                if (at != station || Blueprints.IsUnlocked(product) || db.Item(product) == null) continue;
                if (agents.Exists(a => a.offer == AgentOffer.SellBlueprint && a.sellBlueprint == product)) continue;
                int race = systemRace >= 0 && systemRace <= 3 ? systemRace : 0;
                bool male = race == 0 ? R(100) < 60 : true;
                var seller = new Agent
                {
                    name = RandomName(race, male), race = race, male = male, station = station, offer = AgentOffer.SellBlueprint,
                    portrait = CreatePortrait(male, race), sellBlueprint = product,
                    sellPrice = Round50(Math.Max(1000, db.Item(product).maxPrice) * 1.5f),
                };
                int i = agents.FindLastIndex(x => !x.IsStory && x.offer != AgentOffer.Diplomat && x.offer != AgentOffer.Wingmen
                                                  && x.offer != AgentOffer.SellShip && x.offer != AgentOffer.SellBlueprint);
                if (agents.Count >= 5 && i >= 0) agents[i] = seller;
                else if (agents.Count < 5) agents.Add(seller);
            }
        }

        /// <summary>Remake: a mod ship's lounge seller (ships.json "lounge", AgentOffer.SellShip). A visitor of the
        /// ship's race (Terran rules for gender) takes the place of the last generic visitor that offers neither a diplomat's
        /// nor the bar's wingmen deal, or joins a bar with room. Not at the Kaamo Club, the battlestation or in the evacuated
        /// supernova system.</summary>
        static void AddCustomShipSellers(Database db, int station, List<Agent> agents)
        {
            if (!CustomShips.Available) return;   // (always: a mod ship is there whenever its mod is on)
            if (station == 108 || station == 101 || Shop.InSupernovaSystem(SystemOf(db, station), station)) return;
            int systemRace = Sys(db, SystemOf(db, station))?.raceId ?? -1;
            foreach (var c in CustomShips.All)
            {
                var l = c.lounge;
                if (l == null || db.Ship(c.index) == null) continue;
                if (l.systemRace >= 0 && l.systemRace != systemRace) continue;
                if (Session.FreePlay ? Session.Rank < l.minRank : Session.CampaignMission < l.minCampaign) continue;
                if (Session.ShipIndex == c.index || KaamoClub.HasShip(c.index)) continue;
                if (!Modding.ModUnlocks.ShipAvailable(c.index)) continue;   // its "available" condition
                if (R(100) >= l.chance) continue;
                int race = c.race >= 0 && c.race <= 7 ? c.race : 0;
                bool male = race == 0 ? R(100) < 60 : true;   // only Terrans can be female
                var seller = new Agent
                {
                    name = RandomName(race, male), race = race, male = male, station = station, offer = AgentOffer.SellShip,
                    portrait = CreatePortrait(male, race), sellShip = c.index, sellPrice = Shop.ShipPrice(db, c.index, station),
                };
                // Not another custom ship's seller either (two in one bar: both stay).
                int i = agents.FindLastIndex(x => !x.IsStory && x.offer != AgentOffer.Diplomat && x.offer != AgentOffer.Wingmen && x.offer != AgentOffer.SellShip);
                if (agents.Count >= 5 && i >= 0) agents[i] = seller;
                else if (agents.Count < 5) agents.Add(seller);
            }
        }

        /// <summary>Remake mods: a mod blueprint's lounge seller (blueprints.json "lounge": chance %, price, systemRace) while
        /// the blueprint is available and not known: a local of the system (Terran rules for gender) in the place of the last
        /// generic visitor without a diplomat's, wingmen's or ship seller's deal, or joining a bar with room.</summary>
        static void AddModBlueprintSellers(Database db, int station, List<Agent> agents)
        {
            if (station == 108 || station == 101 || Shop.InSupernovaSystem(SystemOf(db, station), station)) return;
            int systemRace = Sys(db, SystemOf(db, station))?.raceId ?? -1;
            foreach (var d in Modding.ModBlueprints.Offerable(b => b.loungeChance > 0f))
            {
                if (!Modding.ModBlueprints.LoungeAllowedAt(d, station, systemRace)) continue;
                if (Rng.NextDouble() * 100.0 < d.loungeChance) PlaceModSeller(db, d, station, systemRace, agents);
            }
        }

        /// <summary>Remake mods: a named seller ("seller", at fixed stations) whose blueprint came on offer while this bar's
        /// visitors were kept (Shop.EnterStation: a station among the last 3 keeps them).</summary>
        public static void AddNamedSellers(Database db, int station, List<Agent> agents)
        {
            if (station == 108 || station == 101 || Shop.InSupernovaSystem(SystemOf(db, station), station)) return;
            foreach (var d in Modding.ModBlueprints.Offerable(b => b.seller != null && b.loungeChance > 0f && b.loungeStations.Contains(station)))
                PlaceModSeller(db, d, station, Sys(db, SystemOf(db, station))?.raceId ?? -1, agents);
        }

        static void PlaceModSeller(Database db, Modding.ModBlueprints.Def d, int station, int systemRace, List<Agent> agents)
        {
            if (agents.Exists(a => a.offer == AgentOffer.SellBlueprint && a.sellBlueprint == d.product)) return;
            var who = d.seller;
            int race = who != null ? who.race : systemRace >= 0 && systemRace <= 3 ? systemRace : 0;
            bool male = who != null || (race == 0 ? R(100) < 60 : true);
            int price = d.loungePrice > 0 ? d.loungePrice : Math.Max(1000, (db.Item(d.product)?.maxPrice ?? 0) / 2);
            var seller = new Agent
            {
                name = who?.name ?? RandomName(race, male), race = race, male = male, station = station, offer = AgentOffer.SellBlueprint,
                portrait = (who != null ? StoryTable.Portrait(who.speaker) : null) ?? CreatePortrait(male, race), sellBlueprint = d.product, sellPrice = price,
            };
            int i = agents.FindLastIndex(x => !x.IsStory && x.offer != AgentOffer.Diplomat && x.offer != AgentOffer.Wingmen
                                              && x.offer != AgentOffer.SellShip && x.offer != AgentOffer.SellBlueprint);
            if (agents.Count >= 5 && i >= 0) agents[i] = seller;
            else if (agents.Count < 5) agents.Add(seller);
        }

        /// <summary>SpaceLounge::startChat, diplomat: int(|standing axis| / 100 * 16000) (a mod race: its own standing).</summary>
        public static int DiplomatCosts(int race) =>
            (int)(Math.Abs(Modding.ModRaces.Is(race) ? Modding.ModRaces.Standing(race) : Session.Standing[race <= 1 ? 0 : 1]) / 100f * 16000f);

        /// <summary>Remake mods: in the lounges of a mod race's system, like the game's diplomats (35 %, replacing the first
        /// generic visitor), one for each mod race of that mod the player is hostile with whose "diplomats" allow it: talking
        /// to them restores the mod race's standing to neutral (LoungeChat). The visitor looks like the mod race's look race.</summary>
        static void AddModRaceDiplomats(Database db, int station, List<Agent> agents)
        {
            var owner = Modding.ModRaces.Get(Sys(db, SystemOf(db, station))?.raceId ?? -1);
            if (owner == null || R(100) >= 35) return;
            foreach (var f in Modding.ModRaces.All)
            {
                if (f.mod != owner.mod || !f.diplomats || f.hostile || !Standing.IsEnemy(f.race)) continue;
                int i = agents.FindIndex(x => !x.IsStory && x.offer != AgentOffer.Diplomat);
                if (i < 0) return;
                agents[i] = new Agent { name = RandomName(f.look, true), race = f.look, modRace = f.race, station = station, offer = AgentOffer.Diplomat,
                                        portrait = CreatePortrait(true, f.look), costs = DiplomatCosts(f.race) };
            }
        }

        static Agent StoryAgent(Database db, StoryAgentData s)
        {
            var a = new Agent
            {
                name = s.name, race = s.race, male = s.male, station = s.station, storyIndex = s.index,
                portrait = s.portraitParts != null && s.portraitParts.Length == 5 ? (int[])s.portraitParts.Clone() : new int[5],
                sellSystem = s.sellItemSystem, sellBlueprint = s.sellBlueprint, sellMod = s.sellMod, sellPrice = s.sellItemPrice,
            };
            a.offer = s.sellBlueprint >= 0 ? AgentOffer.SellBlueprint : s.sellItemSystem >= 0 ? AgentOffer.SellSystem
                    : s.sellMod >= 0 ? AgentOffer.SellMod : s.index == 25 ? AgentOffer.KaamoSpecial
                    : s.index == 26 ? AgentOffer.ShipDealer : AgentOffer.SmallTalk;
            // A taken offer stays taken (Agent+0x74 persists in Status); known too, so the chat opens with 858.
            if (a.offer != AgentOffer.KaamoSpecial && a.offer != AgentOffer.ShipDealer && Session.StoryAgentsAccepted.Contains(s.index))
                a.accepted = a.known = true;
            // Remake (players' report): a blueprint already owned (the story's Liberator, a hidden wreck's...) isn't offered
            // again: the seller has 858 "nothing left". The original sells it again for nothing (Generator::createAgents
            // never checks BluePrint::isUnlocked).
            if (a.offer == AgentOffer.SellBlueprint && Session.UnlockedBlueprints.Contains(s.sellBlueprint))
                a.accepted = a.known = true;
            if (a.offer == AgentOffer.KaamoSpecial)
            {
                int k = Session.CampaignMission < 0x8e ? R(7) + 2 : R(9);
                a.sellItem = KaamoItems[k];
                a.sellQuantity = k == 3 || k == 4 ? 10 : 1;
                a.sellPrice = SinglePrice(db.Item(a.sellItem)) * a.sellQuantity;
            }
            else if (a.offer == AgentOffer.ShipDealer)
            {
                // A random ship of DAT_00251f40 the player neither flies nor stores; price = the prototype's price (not
                // race-adjusted); none left = the offer counts as accepted ("nothing left").
                var ships = KaamoClub.DealerCandidates();
                if (ships.Count == 0) a.accepted = true;
                else { a.sellShip = ships[R(ships.Count)]; a.sellPrice = db.Ship(a.sellShip)?.price ?? 0; }
            }
            return a;
        }

        /// <summary>Generator::createAgent 0xa26c0.</summary>
        public static Agent CreateAgent(Database db, int station)
        {
            int campaign = Session.CampaignMission;
            int race = Standing.Look(Sys(db, SystemOf(db, station))?.raceId ?? 0);   // (a mod race's system: its look race's visitors)
            if (R(100) < 20) race = R(8);
            int offer;
            do offer = R(7); while ((race == 1 && offer == AgentOffer.Wingmen) || offer == 3 || offer == 4);   // Vossk never offer wingmen
            if (R(100) < 33) offer = AgentOffer.Mission;
            else if ((offer == AgentOffer.Purchase || offer == AgentOffer.Wingmen) && campaign < 16) offer = AgentOffer.Mission;
            bool male = race == 0 && offer != AgentOffer.Wingmen ? R(100) < 60 : true;   // only Terrans can be female

            var a = new Agent { race = race, male = male, station = station, offer = offer };
            a.name = RandomName(race, male);
            a.portrait = CreatePortrait(male, race);
            if (offer == AgentOffer.Wingmen)
            {
                int friends = R(3);
                for (int i = 0; i < friends; i++) a.wingmen.Add(RandomName(race, true));
                a.costs = (R(1300) + 700) * (friends + 1) * (Session.IsExtreme ? 7 : 1);
            }
            else if (offer == AgentOffer.SellItem)
            {
                ItemData it;
                int tries = 0;
                do it = db.Items[R(db.Items.Count)];
                while ((NotForSale.Contains(it.index) || it.blueprint.Count > 0 || SinglePrice(it) == 0 || it.occurrence == 0
                        || !Modding.ModCampaigns.ItemAllowed(db, it.index) || !Modding.ModUnlocks.ItemAvailable(it.index)
                        || Shop.NotInSession(db, it.index)) && ++tries < 5000);
                // remake mods: a campaign's own items, and a mod item only once its "available" condition holds
                a.sellItem = it.index;
                a.sellQuantity = it.TypeId <= 3 && it.TypeId != 1 ? 1 : R(15) + 5;   // primary, turret, equipment: 1
                a.sellPrice = (int)((R(120) + 40) / 100f * SinglePrice(it)) * a.sellQuantity;
            }
            return a;
        }

        // ---- missions ---------------------------------------------------------------------------------------

        /// <summary>Generator::generateStationIndex 0xa1e2c.</summary>
        public static int GenerateStationIndex(Database db, int agentStation, int currentStation)
        {
            int curSys = SystemOf(db, currentStation);
            var vis = GalaxyMap.Visibility(db);
            var cur = Sys(db, curSys);
            for (int guard = 0; guard < 10000; guard++)
            {
                int st;
                if (R(100) < 20) st = agentStation;
                else if (R(100) < 40) st = cur.stations[R(cur.stations.Count)];
                else st = Modding.ModCampaigns.ModGalaxy ? Modding.ModCampaigns.RandomStation(db) : R(135);   // remake mods: a campaign's own galaxy
                if (curSys == 15) st = cur.stations[R(cur.stations.Count)];   // Mido: always a station of Mido
                int sys = SystemOf(db, st);
                var sd = Sys(db, sys);
                if (sd == null || ExcludedTargets.Contains(st) || (st >= 109 && st <= 113)) continue;
                // The player's own system counts as shown (StarMap does the same): in a system hidden on purpose (a mod's
                // "available" condition, a mod campaign's galaxy) every pick failed all 10 000 tries, and the Courier /
                // Passenger / Stolen goods retries on top froze the level's Awake (Discord report, Mod Builder freeze).
                if (vis != null && sys < vis.Length && !vis[sys] && sys != curSys) continue;
                if ((sd.jumpRoutesTo == null || sd.jumpRoutesTo.Count == 0) && sys != curSys) continue;
                return st;
            }
            return currentStation;
        }

        /// <summary>The rnd(15) loop: every type once before repeats (Status+0x50, cleared when 14 are used); purchase only
        /// through offer 5, intercept only for races 0..3; before campaign 16 one of the early types.</summary>
        static int PickType(int agentRace)
        {
            var used = Session.UsedMissionTypes;
            int t = 0;
            // Multiplayer sessions also roll 15 Ore Mining (the original's generator stops at 14, freelance_missions.md 2.3;
            // Level::createMission supports it); outside the used-types rotation.
            bool session = NetSession.Active;
            for (int i = 0; i < 1000; i++)
            {
                t = R(session ? 16 : 15);
                if (t == MissionType.OreMining) break;
                if (t == MissionType.Purchase || (t == MissionType.Intercept && agentRace > 3)) continue;
                if (!used[t]) { used[t] = true; break; }
                int n = 0; foreach (bool u in used) if (u) n++;
                if (n >= 14) Array.Clear(used, 0, used.Length);
            }
            if (Session.CampaignMission < 16) t = EarlyTypes[R(5)];
            return t;
        }

        /// <summary>Generator::createMission 0xa2a7c. 'purchase' = offer 5 (created at the first chat).</summary>
        public static FreelanceMission CreateMission(Database db, Agent agent, int currentStation, bool purchase)
        {
            int target = GenerateStationIndex(db, agent.station, currentStation);
            if (SystemOf(db, currentStation) == 15) target = Sys(db, 15).stations[0] + R(4);
            int type = PickType(agent.race);
            if (type == MissionType.Challenge) target = agent.station;
            if (purchase) { type = MissionType.Purchase; target = agent.station; }
            else
            {
                if (type == MissionType.Courier || type == MissionType.Passenger || type == MissionType.StolenGoods)
                    for (int g = 0; g < 1000 && target == currentStation; g++) target = GenerateStationIndex(db, agent.station, currentStation);
                if (type == MissionType.Informer && agent.race < 4)
                {
                    var routes = Sys(db, SystemOf(db, agent.station))?.jumpRoutesTo;
                    if (routes != null && routes.Count > 0)
                        for (int g = 0; g < 1000 && Sys(db, SystemOf(db, target))?.raceId != agent.race; g++)
                            target = GenerateStationIndex(db, agent.station, currentStation);
                    else type = R(2) == 0 ? MissionType.Defense : MissionType.PirateHunting;
                }
            }

            var m = new FreelanceMission
            {
                type = type, target = target, clientName = agent.name, clientRace = agent.race, clientMale = agent.male,
                clientPortrait = (int[])agent.portrait.Clone(), clientStation = agent.station,
            };
            int diff = Session.CampaignMission < 16 ? R(2) : R(9);
            if (type == MissionType.Purchase)
            {
                ItemData it;
                do it = db.Items[97 + R(db.Items.Count - 97)];
                while (it.occurrence == 0 || SinglePrice(it) == 0 || NotForPurchase.Contains(it.index) || it.blueprint.Count > 0
                       || !Modding.ModUnlocks.ItemAvailable(it.index)    // remake mods: not a mod item no shop may stock yet
                       || Shop.NotInSession(db, it.index));             // remake: nor the time extender in multiplayer
                m.good = it.index;
                m.amount = R(15) + 5;
                diff = it.techLevel;
            }
            else
            {
                diff++;
                float d = diff / 10f;
                switch (type)
                {
                    case MissionType.Courier: m.good = R(7); m.amount = (int)(d * 95f) + 5; break;
                    case MissionType.Protection: m.amount = R(4) + 2; break;
                    case MissionType.Recovery: m.good = 116; m.amount = (int)(d * 8f) + 2; break;
                    case MissionType.Salvage: m.good = 117; m.amount = (int)(d * 8f) + 2; break;
                    case MissionType.Passenger: m.amount = (int)(d * 18f) + 2; break;
                    case MissionType.Wanted: m.targetName = RandomName(0, true); break;   // name args lost: Terran assumed
                    case MissionType.OreMining:
                        // One of this station's 3 asteroid ores (Galaxy::getAsteroidProbabilities, nextInt(3)), nextInt(90)+30 t.
                        m.good = World.OrbitLayout.OreProbabilities(db, World.OrbitLayout.Build(db, currentStation))[R(3)].item;
                        m.amount = R(90) + 30;
                        break;
                }
            }
            diff = Math.Min(diff, 10);
            m.difficulty = diff;

            float dist = Distance(db, SystemOf(db, currentStation), SystemOf(db, target));
            float rew = (int)(diff / 10f * 5500f) + 1500;
            rew *= dist / 1200f + 1f;
            switch (type)
            {
                case MissionType.Escort: rew *= 1.2f; break;
                case MissionType.JunkRemoval: rew *= 0.7f; break;
                case MissionType.Purchase: rew = m.amount * (db.Item(m.good)?.maxPrice ?? 0) * 1.7f; break;
                case MissionType.Recovery: case MissionType.Salvage: rew += rew; break;
                case MissionType.Passenger: rew = rew * 0.6f + m.amount * (rew * 0.6f / 5f); break;
            }
            int level = Math.Min(Session.Rank, 20);
            rew += 10 * level * level * level;
            m.bonus = type == MissionType.Purchase || type == MissionType.Challenge ? 0 : Round50(rew * MissionBonus(agent.race));
            m.reward = Round50(rew);
            int costs = (int)(m.reward / 10f + R(m.reward / 10));
            if (type == MissionType.Purchase) costs = (int)(costs * 0.5f);
            m.costs = Round50(costs);
            return m;
        }
    }
}
