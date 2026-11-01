// SaveGame.cs
// Save games (the original's RecordHandler / GameRecord): one JSON file per slot in Application.persistentDataPath/Saves.
// Slot 0 is the auto-save (texts 486 / 489), written when docking like ModStation::autosave 0xe9eb4 (which skips a game
// with no playing time); slots 1..11 are manual saves (MenuTouchWindow::saveGame 0x14bcf8). The file format is the
// remake's own (the original writes Status field by field in RecordHandler::recordStoreWrite 0xdf760); the preview
// holds what RecordHandler::recordStoreWritePreview 0xe13e0 stores: playing time, credits, station, system, campaign
// mission, rank, difficulty and ship. Saves are only made while docked, so loading one opens the Station scene.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace GoF2Remake.Data
{
    [Serializable]
    public class SaveData
    {
        public const int CurrentVersion = 17;

        public int version = CurrentVersion;
        public string savedAtUtc;
        public float playSeconds;
        public int campaign;
        public float difficulty;
        public int campaignMission;
        public int station, previousStation, ship, credits;
        /// <summary>Remake: the station the player last docked at (Session.LastDockedStation), -1 = unknown (older saves).</summary>
        public int lastDockedStation = -1;
        /// <summary>#122: Session.KhadorCronusGift, 1 / 0; -1 = a save from before it (then given at step 77 to a player not in
        /// the Cronus: the row was added before the flag existed).</summary>
        public int khadorCronusGift = -1;
        public List<ItemStack> equipment, cargo;
        public List<StationStock> recentStations;
        public List<int> seenItems, visitedStations, attackedStations;
        public List<KnownPrice> lowestPrices, highestPrices;
        public bool[] systemVisible;
        /// <summary>Session.SystemShown: "system|1" shown / "system|0" hidden; a mod's system by its key ("mod_id:id"), so it
        /// stays the same system wherever its number lands.</summary>
        public List<string> systemShown;
        public int jumpgatesUsed, kills, pirateKills;
        public int[] standing;
        /// <summary>Session.ModRaceStanding: "mod_id:race|value" (remake mods' races; no save version of its own).</summary>
        public List<string> modRaceStanding;
        /// <summary>modRaceStanding's name before mod races were called so (read when a save has no modRaceStanding).</summary>
        public List<string> factionStanding;
        // version 2: the story
        public bool freePlay;
        public StoryMission storyMission;
        public float storyStepStart;
        public bool storyRadioPending;
        public int freelanceCompleted, storyCounter, voidInvasionSystem = -1, voidInvasionStation = -1, invasionDepartures;
        public List<int> unsaleable;
        // version 3: the bar (agents live in recentStations)
        public FreelanceMission freelanceMission;
        public int passengers;
        public bool[] usedMissionTypes;
        public bool informerKilled, informerFailed;
        public List<string> wingmen;
        public int wingmanRace;
        public float wingmanContractMs;
        public int[] wingmanPortrait;
        public int wingmenHired;
        public List<int> unlockedBlueprints, shipMods;
        public List<BlueprintState> blueprints;
        public List<PendingProduct> pendingProducts;
        public int goodsProduced;
        public int[] medals;
        public int asteroidsDestroyed, oreMined, coresMined, cratesSalvaged, junkDestroyed, battleshipsDestroyed, highestCredits, lastArrivalHullPercent = 100;
        public List<int> oreTypesMined, coreTypesMined;
        public float capitalLootReadyAt;   // remake: World.CapitalShips' loot cooldown (playing time)
        public int boozeBought, alienRemainsCollected;   // v8
        public List<int> boozeTypes;
        public int agentsTalkedTo, offersDeclined, offersRepeated, acceptedBlindRisk, acceptedBlindMap, containersDelivered, passengersDelivered;
        // version 4: the Kaamo Club
        public int kaamoState;
        public List<ItemStack> kaamoItems;
        public List<StoredShip> kaamoShips;
        // version 5: combat
        public int selectedSecondary = -1, bombsDetonated;
        public bool[] pirateBaseDestroyed;
        public int graveRiserKills;
        public long cloakMs;
        public List<int> hints;
        // version 6: the Valkyrie story (the parked own ship, step 59's target stations)
        public bool hasParkedShip;
        public ParkedShip parkedShip;
        public List<int> storyTargets;
        // version 7: the Supernova story (the Most Wanted boards)
        public List<WantedState> wanted;
        public int[] collectedBounties;
        public int wantedHints;
        public int hiddenBlueprintsFound;
        // version 9: the elite medals' flags and streaks (Status+0x120 .. +0x148)
        public List<int> eliteFlags;
        public int oreStreak, blindKills;
        public bool lomaTollPaid, lomaTollRefused;
        // version 10: the economy (Session.Economy; older saves played the Android tables)
        public int economy;
        // version 11: the story agents whose offer was taken (Agent+0x74)
        public List<int> storyAgentsAccepted;
        // version 12: the mods that were on and the numbers of their content (Modding.ModSaves)
        public List<Modding.SaveMod> mods;
        public List<Modding.SaveModKey> modKeys;
        // version 13: the event graph quests and bar missions under way, the quests finished (Session.GraphQuests)
        public List<GraphQuestState> graphQuests;
        public List<string> graphQuestsDone;
        // (remake, no version of its own: older saves have none) the repeatable quests finished, "name|playing seconds"
        public List<string> graphQuestRepeats;
        // version 14: a mod's campaign (Session.ModCampaign, Modding.ModCampaigns)
        public string modCampaign;
        // version 15: a hardcore (permadeath) game and the run it belongs to (Session.Hardcore / RunId)
        public bool hardcore;
        public string runId;
        // version 16: the mods' new-game options on in this game (Session.ModGameOptions, Modding.ModGameOptions)
        public List<string> modGameOptions;
        // version 17 (with the pirate board): the player's builds of the mods' customizable ships (Session.ShipBuilds, Modding.ModShipKits): "key|build"
        public List<string> shipBuilds;
        /// <summary>v17: the Pirate activity board (PirateReports).</summary>
        public List<PirateSighting> pirateSightings;
        public int nextPirateSightingId;

        [Serializable]
        public class KnownPrice { public int item, price, system; }
    }

    public static class SaveGame
    {
        public const int SlotCount = 12;
        public const int AutoSaveSlot = 0;

        static string Dir => Path.Combine(GoF2Remake.GamePaths.Data, "Saves");
        static string PathOf(int slot) => Path.Combine(Dir, $"slot_{slot:00}.json");
        /// <summary>The folder of the slot files (SaveTransfer).</summary>
        public static string Folder => Dir;
        /// <summary>A slot's file (SaveTransfer).</summary>
        public static string SlotPath(int slot) => PathOf(slot);

        public static bool Exists(int slot) => File.Exists(PathOf(slot));

        /// <summary>The slot's contents without loading it (null = empty or unreadable).</summary>
        public static SaveData Preview(int slot)
        {
            try { return Exists(slot) ? JsonUtility.FromJson<SaveData>(File.ReadAllText(PathOf(slot))) : null; }
            catch (Exception e) { Debug.LogWarning($"SaveGame: slot {slot} unreadable: {e.Message}"); return null; }
        }

        /// <summary>The most recently written slot (the main menu's Resume), -1 = none.</summary>
        public static int MostRecentSlot()
        {
            int best = -1; DateTime newest = DateTime.MinValue;
            for (int i = 0; i < SlotCount; i++)
            {
                if (!Exists(i)) continue;
                var t = File.GetLastWriteTimeUtc(PathOf(i));
                if (t > newest) { newest = t; best = i; }
            }
            return best;
        }

        public static bool Save(int slot)
        {
            // Multiplayer: a session is a separate free-play game; it never overwrites the single-player saves (also not
            // after its connection went, while its game is still loaded).
            if (NetSession.SessionGame) return false;
            if (Modding.ModManager.BuilderOpen) return false;   // the Mod Builder's throwaway game
            if (SupernovaChallenge.Active) return false;   // the Supernova Challenge's throwaway game (it never docks)
            // Hardcore: only the auto-save (no slot to go back to after a death).
            if (Session.Hardcore && slot != AutoSaveSlot) return false;
            var s = Capture();
            Modding.ModSaves.Record(s, Database.Load());
            try
            {
                Directory.CreateDirectory(Dir);
                string tmp = PathOf(slot) + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(s, true));
                if (File.Exists(PathOf(slot))) File.Delete(PathOf(slot));
                File.Move(tmp, PathOf(slot));
                SaveFolder.AfterLocalChange();   // remake: the chosen save folder catches up (a no-op without one)
                return true;
            }
            catch (Exception e) { Debug.LogError($"SaveGame: saving slot {slot} failed: {e.Message}"); return false; }
        }

        /// <summary>Remake (hardcore): deletes every slot of the run 'runId' (the death of a hardcore game); the slots deleted.</summary>
        public static int DeleteRun(string runId)
        {
            if (string.IsNullOrEmpty(runId)) return 0;
            int deleted = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                var s = Preview(i);
                if (s == null || s.runId != runId) continue;
                try { File.Delete(PathOf(i)); deleted++; }
                catch (Exception e) { Debug.LogWarning($"SaveGame: couldn't delete slot {i}: {e.Message}"); }
            }
            Debug.Log($"SaveGame: hardcore death, {deleted} save(s) of the run deleted");
            if (deleted > 0) SaveFolder.AfterLocalChange();   // gone from the save folder too
            return deleted;
        }

        /// <summary>Remake (players' suggestion): the menus' hold-to-delete (SaveSlotRow). The file is moved to Saves/Deleted
        /// (a time-stamped copy, the newest 20 kept) rather than destroyed, so a slip can still be undone by hand.</summary>
        public static bool Delete(int slot)
        {
            if (!Exists(slot)) return false;
            try
            {
                string bin = Path.Combine(Dir, "Deleted");
                Directory.CreateDirectory(bin);
                string to = Path.Combine(bin, $"slot_{slot:00}_{DateTime.Now:yyyyMMdd-HHmmss}.json");
                if (File.Exists(to)) File.Delete(to);
                File.Move(PathOf(slot), to);
                File.SetLastWriteTimeUtc(to, DateTime.UtcNow);   // the pruning keeps the newest deletions
                var old = new DirectoryInfo(bin).GetFiles("slot_*.json");
                Array.Sort(old, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                for (int i = 20; i < old.Length; i++) old[i].Delete();
                Debug.Log($"SaveGame: slot {slot} deleted (moved to {bin})");
                SaveFolder.AfterLocalChange();
                return true;
            }
            catch (Exception e) { Debug.LogWarning($"SaveGame: couldn't delete slot {slot}: {e.Message}"); return false; }
        }

        /// <summary>ModStation::autosave: slot 0, not for a game without playing time.</summary>
        public static void AutoSave()
        {
            // Multiplayer: a server that keeps profiles gets the docked game instead (NetProfileClient; a no-op without).
            if (NetSession.SessionGame) { GameHooks.ProfileUpload?.Invoke(); return; }
            if (Session.PlaySeconds <= 0f || Modding.ModManager.BuilderOpen || SupernovaChallenge.Active) return;
            Save(AutoSaveSlot);
        }

        public static bool Load(int slot)
        {
            if (NetSession.Active) return false;   // multiplayer: no single-player save into the session
            var s = Preview(slot);
            if (s == null) return false;
            Modding.ModSaves.Fix(s);   // modded items moved to their numbers now, or removed and refunded (mods turned off)
            DropUnknownItems(s, Database.Shared);
            Apply(s);
            Session.ArrivedByLoad = true;   // ModStation::setGameLoaded: the station asks no docking fine
            return true;
        }

        /// <summary>Remake: what is left of item numbers the item table doesn't have (after ModSaves.Fix: no mod accounts
        /// for them) goes, with a warning each: the hold, the equipment, the shop memory, the Kaamo Club, the parked ship,
        /// blueprints and the production queue. Crash reports (2026-10-10 / 12, Android and Windows) had the hangar throw on
        /// such an item at every trade; its source isn't known yet (Shop.AddToCargo now refuses one and logs the caller).</summary>
        static void DropUnknownItems(SaveData s, Database db)
        {
            bool Known(int item) => db.Item(item) != null;
            int Drop(List<ItemStack> list, string where)
            {
                if (list == null) return 0;
                int n = list.RemoveAll(st => st == null || !Known(st.item) && Warn(st.item, st.amount, where));
                return n;
            }
            bool Warn(int item, int amount, string where)
            {
                Debug.LogWarning($"SaveGame: {amount} of item {item} in the {where} isn't in the item table of {db.Items.Count}: dropped");
                return true;
            }
            if (Drop(s.equipment, "equipment") > 0) s.selectedSecondary = -1;
            Drop(s.cargo, "hold");
            Drop(s.kaamoItems, "Kaamo Club storage");
            if (s.kaamoShips != null) foreach (var k in s.kaamoShips) if (k != null) Drop(k.equipment, "Kaamo Club ship's equipment");
            if (s.parkedShip != null) { Drop(s.parkedShip.equipment, "parked ship's equipment"); Drop(s.parkedShip.cargo, "parked ship's hold"); }
            if (s.recentStations != null)
                foreach (var rs in s.recentStations)
                {
                    if (rs == null) continue;
                    Drop(rs.items, $"shop memory of station {rs.station}");
                    rs.agents?.RemoveAll(ag => ag != null && ag.sellItem != -1 && !Known(ag.sellItem) && Warn(ag.sellItem, 0, "bar's offers"));
                }
            s.blueprints?.RemoveAll(bp => bp == null || !Known(bp.item) && Warn(bp.item, 0, "blueprints"));
            s.pendingProducts?.RemoveAll(pp => pp == null || !Known(pp.item) && Warn(pp.item, pp.quantity, "production queue"));
        }

        /// <summary>Multiplayer (NetProfileClient): the game as a server profile: a save without the shop memory (the session's
        /// stock is the host's, NetStock; the bar visitors come again), compact.</summary>
        public static string ProfileJson()
        {
            var s = Capture();
            s.recentStations = null;
            return JsonUtility.ToJson(s, false);
        }

        /// <summary>Multiplayer: a server profile into the session's game (already checked by TryParse), under the session's
        /// rules (NetGame.PrepareSession: free play on Normal, the Android economy). A squad mission, its Courier containers
        /// and passengers don't outlive the session they belonged to; the game starts docked where the profile was (an orbit
        /// that isn't a station: Var Hastra).</summary>
        public static void ApplyProfile(SaveData s)
        {
            Apply(s);
            Session.Difficulty = Session.DifficultyNormal;
            Session.Economy = Economy.Android;
            // The session's world, whatever index the profile was saved with; a co-op campaign's is the story's own (its
            // guests follow the leader's step, CoopCampaign).
            if (CoopCampaign.Active) CoopCampaign.AfterProfile(NetSession.Db);
            else Session.UseCompletedWorld();
            Session.FreelanceMission = new FreelanceMission();
            Session.Passengers = 0;
            Session.Cargo.RemoveAll(c => c.item == Freelance.SecureContainer);   // a Courier's containers (unsaleable)
            // The game starts docked where the pilot last docked: a profile uploaded in flight names the orbit they were in,
            // and signing out there and in again would dock them on the spot (out of a fight, without the trip back).
            int stations = NetSession.Db.Stations.Count;
            if (Session.LastDockedStation >= 0 && Session.LastDockedStation < stations) Session.StationIndex = Session.LastDockedStation;
            if (Session.StationIndex < 0 || Session.StationIndex >= stations)
                Session.StationIndex = NetSession.StartStation;
            Session.ProgrammedStation = -1;
            Session.LaunchedFromStation = false;
            Session.DockedFromSpace = false;
        }

        static SaveData Capture()
        {
            static List<SaveData.KnownPrice> Prices(Dictionary<int, (int price, int system)> d)
            {
                var l = new List<SaveData.KnownPrice>();
                foreach (var kv in d) l.Add(new SaveData.KnownPrice { item = kv.Key, price = kv.Value.price, system = kv.Value.system });
                return l;
            }
            return new SaveData
            {
                savedAtUtc = DateTime.UtcNow.ToString("o"),
                playSeconds = Session.PlaySeconds,
                campaign = (int)Session.Campaign,
                difficulty = Session.Difficulty,
                economy = (int)Session.Economy,
                storyAgentsAccepted = new List<int>(Session.StoryAgentsAccepted),
                graphQuests = new List<GraphQuestState>(Session.GraphQuests),
                graphQuestsDone = new List<string>(Session.GraphQuestsDone),
                graphQuestRepeats = Session.GraphQuestRepeats.Select(kv => kv.Key + "|" + kv.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList(),
                modCampaign = Session.ModCampaign ?? "",
                hardcore = Session.Hardcore,
                runId = Session.RunId ?? "",
                modGameOptions = new List<string>(Session.ModGameOptions),
                shipBuilds = Session.ShipBuilds.Select(kv => kv.Key + "|" + kv.Value).ToList(),
                pirateSightings = new List<PirateSighting>(Session.PirateSightings), nextPirateSightingId = Session.NextPirateSightingId,
                campaignMission = Session.CampaignMission,
                station = Session.StationIndex,
                previousStation = Session.PreviousStationIndex,
                lastDockedStation = Session.LastDockedStation,
                khadorCronusGift = Session.KhadorCronusGift ? 1 : 0,
                ship = Session.ShipIndex,
                credits = Session.Credits,
                equipment = Session.Equipment.ConvertAll(e => e.Clone()),
                cargo = Session.Cargo.ConvertAll(e => e.Clone()),
                recentStations = Session.RecentStations,
                seenItems = new List<int>(Session.SeenItems),
                visitedStations = new List<int>(Session.VisitedStations),
                attackedStations = new List<int>(Session.AttackedStations),
                lowestPrices = Prices(Session.LowestKnownPrice),
                highestPrices = Prices(Session.HighestKnownPrice),
                systemVisible = Session.SystemVisible,
                systemShown = Session.SystemShown.Select(kv => (Modding.ModWorld.SystemKey(kv.Key) ?? kv.Key.ToString(System.Globalization.CultureInfo.InvariantCulture))
                                                                + "|" + (kv.Value ? "1" : "0")).ToList(),
                jumpgatesUsed = Session.JumpgatesUsed,
                kills = Session.Kills,
                pirateKills = Session.PirateKills,
                standing = (int[])Session.Standing.Clone(),
                modRaceStanding = Session.ModRaceStanding.Select(kv => kv.Key + "|" + kv.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList(),
                freePlay = Session.FreePlay,
                storyMission = Session.StoryMission,
                storyStepStart = Session.StoryStepStart,
                storyRadioPending = Session.StoryRadioPending,
                freelanceCompleted = Session.FreelanceCompleted,
                storyCounter = Session.StoryCounter,
                voidInvasionSystem = Session.VoidInvasionSystem,
                voidInvasionStation = Session.VoidInvasionStation,
                invasionDepartures = Session.InvasionDepartures,
                unsaleable = new List<int>(Session.Unsaleable),
                freelanceMission = Session.FreelanceMission,
                passengers = Session.Passengers,
                usedMissionTypes = Session.UsedMissionTypes,
                informerKilled = Session.InformerKilled,
                wingmen = Session.Wingmen, wingmanRace = Session.WingmanRace, wingmanContractMs = Session.WingmanContractMs,
                wingmanPortrait = Session.WingmanPortrait, wingmenHired = Session.WingmenHired,
                unlockedBlueprints = new List<int>(Session.UnlockedBlueprints), shipMods = Session.ShipMods,
                blueprints = Session.Blueprints, pendingProducts = Session.PendingProducts, goodsProduced = Session.GoodsProduced,
                medals = Session.Medals, asteroidsDestroyed = Session.AsteroidsDestroyed, oreMined = Session.OreMined, coresMined = Session.CoresMined,
                cratesSalvaged = Session.CratesSalvaged, junkDestroyed = Session.JunkDestroyed, battleshipsDestroyed = Session.BattleshipsDestroyed,
                highestCredits = Session.HighestCredits, lastArrivalHullPercent = Session.LastArrivalHullPercent, capitalLootReadyAt = Session.CapitalLootReadyAt,
                oreTypesMined = new List<int>(Session.OreTypesMined), coreTypesMined = new List<int>(Session.CoreTypesMined),
                boozeBought = Session.BoozeBought, alienRemainsCollected = Session.AlienRemainsCollected, boozeTypes = new List<int>(Session.BoozeTypes),
                informerFailed = Session.InformerFailed,
                agentsTalkedTo = Session.AgentsTalkedTo, offersDeclined = Session.OffersDeclined, offersRepeated = Session.OffersRepeated,
                acceptedBlindRisk = Session.AcceptedBlindRisk, acceptedBlindMap = Session.AcceptedBlindMap,
                containersDelivered = Session.ContainersDelivered, passengersDelivered = Session.PassengersDelivered,
                kaamoState = Session.KaamoState, kaamoItems = Session.KaamoItems, kaamoShips = Session.KaamoShips,
                selectedSecondary = Session.SelectedSecondary, bombsDetonated = Session.BombsDetonated,
                pirateBaseDestroyed = Session.PirateBaseDestroyed, hints = new List<int>(Session.Hints),
                graveRiserKills = Session.GraveRiserKills, cloakMs = Session.CloakMs,
                hasParkedShip = Session.ParkedShip != null, parkedShip = Session.ParkedShip, storyTargets = new List<int>(Session.StoryTargets),
                wanted = new List<WantedState>(Session.Wanted), collectedBounties = (int[])Session.CollectedBounties.Clone(),
                wantedHints = Session.WantedHints, hiddenBlueprintsFound = Session.HiddenBlueprintsFound,
                eliteFlags = new List<int>(Session.EliteFlags), oreStreak = Session.OreStreak, blindKills = Session.BlindKills,
                lomaTollPaid = Session.LomaTollPaid, lomaTollRefused = Session.LomaTollRefused,
            };
        }

        /// <summary>A save file's text from outside (SaveTransfer's import): parsed and checked so loading it can't break the
        /// game: a version this build knows, and every station, ship, item and system it names in the game's tables
        /// (an index out of range would throw wherever the game looks it up). 'problem' says what is wrong (English).</summary>
        public static bool TryParse(string json, Database db, out SaveData save, out string problem)
        {
            save = null;
            if (string.IsNullOrWhiteSpace(json) || json.TrimStart()[0] != '{') { problem = "not a save"; return false; }
            // JsonUtility keeps the field defaults (version = CurrentVersion) for what the text lacks: "{}" isn't a save.
            if (!json.Contains("\"version\"") || !json.Contains("\"ship\"") || !json.Contains("\"station\""))
            {
                problem = "not a save (fields missing)"; return false;
            }
            try { save = JsonUtility.FromJson<SaveData>(json); }
            catch (Exception e) { problem = "unreadable (" + e.Message + ")"; return false; }
            if (save == null) { problem = "unreadable"; return false; }
            try { problem = Check(save, db); }
            catch (Exception e) { problem = "unreadable (" + e.Message + ")"; }
            if (problem != null) { save = null; return false; }
            return true;
        }

        static string Check(SaveData s, Database db)
        {
            int stations = db.Stations.Count, ships = db.Ships.Count, items = db.Items.Count, systems = db.Systems.Count;
            bool Station(int i) => i >= 0 && i < stations || Modding.ModSaves.NamesStation(s, i);   // a mod's station: put right on loading
            bool StationOrNone(int i) => i == -1 || Station(i);
            bool Ship(int i) => i >= 0 && i < ships || Modding.ModSaves.NamesShip(s, i);   // a mod's ship: put right on loading
            bool Item(int i) => i >= 0 && i < items || Modding.ModSaves.Names(s, i);   // a modded item: put right on loading
            bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
            string Stacks(List<ItemStack> l, string what)
            {
                if (l == null) return null;
                foreach (var st in l)
                    if (st == null || !Item(st.item) || st.amount < 0) return $"an unknown item or a negative amount in the {what}";
                return null;
            }

            if (s.version < 1) return "not a save (no version)";
            if (s.version > SaveData.CurrentVersion) return "saved by a newer version of the game; update it first";
            if (s.campaign < 0 || s.campaign > (int)Campaign.Supernova) return "an unknown campaign";
            if (s.version >= 10 && s.economy != (int)Economy.Android && s.economy != (int)Economy.Default) return "an unknown economy";
            if (!Finite(s.playSeconds) || s.playSeconds < 0f) return "a broken playing time";
            if (!Finite(s.difficulty) || s.difficulty < 0f || s.difficulty > 2f) return "an unknown difficulty";
            if (!Finite(s.storyStepStart) || !Finite(s.wingmanContractMs)) return "a broken timer";
            if (s.campaignMission < 0 || s.campaignMission > Story.LastIndex) return "an unknown story mission";
            if (!Station(s.station) && s.station != Session.VoidOrbit) return "an unknown station";
            if (!StationOrNone(s.previousStation)) return "an unknown previous station";   // -1: none (also Session.VoidOrbit)
            if (s.lastDockedStation != -1 && !Station(s.lastDockedStation)) return "an unknown last docked station";
            if (!Ship(s.ship)) return "an unknown ship";
            if (s.credits < 0) return "negative credits";
            if (Stacks(s.equipment, "equipment") is string e1) return e1;
            if (Stacks(s.cargo, "cargo") is string e2) return e2;
            if (Stacks(s.kaamoItems, "Kaamo Club storage") is string e3) return e3;
            if (s.recentStations != null)
                foreach (var rs in s.recentStations)
                {
                    if (rs == null || !Station(rs.station)) return "an unknown station in the shop memory";
                    if (Stacks(rs.items, "shop memory") is string e4) return e4;
                    if (rs.ships != null && rs.ships.Exists(x => !Ship(x))) return "an unknown ship in the shop memory";
                    if (rs.shipMods != null && rs.shipMods.Exists(m => m == null || !Ship(m.ship))) return "an unknown modded ship in the shop memory";
                    if (rs.agents != null)
                        foreach (var a in rs.agents)
                            if (a == null || !StationOrNone(a.station) || a.sellItem != -1 && !Item(a.sellItem) || a.sellShip != -1 && !Ship(a.sellShip)
                                || a.sellSystem < -1 || a.sellSystem >= systems)
                                return "a broken bar visitor";
                }
            if (s.kaamoShips != null && s.kaamoShips.Exists(k => k == null || !Ship(k.ship))) return "an unknown ship in the Kaamo Club";
            if (s.kaamoShips != null)
                foreach (var k in s.kaamoShips)
                    if (Stacks(k.equipment, "Kaamo Club ships' equipment") is string e5) return e5;
            if (s.version >= 6 && s.hasParkedShip)
            {
                if (s.parkedShip == null || !Ship(s.parkedShip.ship)) return "an unknown parked ship";
                if (Stacks(s.parkedShip.equipment, "parked ship") is string e5) return e5;
                if (Stacks(s.parkedShip.cargo, "parked ship") is string e6) return e6;
            }
            if (s.storyMission != null && !StationOrNone(s.storyMission.station)) return "an unknown station in the story mission";
            if (s.freelanceMission != null && !StationOrNone(s.freelanceMission.clientStation)) return "an unknown station in the bar mission";
            if (s.wanted != null && s.wanted.Exists(w => w == null || !StationOrNone(w.current) || !StationOrNone(w.travelsTo) || !StationOrNone(w.lastSeen)))
                return "an unknown station on the Most Wanted board";
            // -1 none yet, -10 the game won (Story.OnDepart: nothing is attacked any more; #117 refused every finished game).
            if (s.voidInvasionStation != -10 && !StationOrNone(s.voidInvasionStation)) return "an unknown invaded station";
            if (s.voidInvasionSystem != -10 && (s.voidInvasionSystem < -1 || s.voidInvasionSystem >= systems)) return "an unknown invaded system";
            if (s.blueprints != null && s.blueprints.Exists(b => b == null || !Item(b.item))) return "an unknown blueprint";
            if (s.pendingProducts != null && s.pendingProducts.Exists(p => p == null || !Item(p.item) || !StationOrNone(p.station) || p.quantity < 0))
                return "an unknown item or station in the production queue";
            return null;
        }

        /// <summary>The save's economy: before version 10 every game used the Android tables.</summary>
        public static Economy SaveEconomy(SaveData s) => s.version >= 10 ? (Economy)s.economy : Economy.Android;

        static void Apply(SaveData s)
        {
            Session.CompletedWorld = false;   // a save is its own world (ApplyProfile sets the session's again)
            Session.KhadorCronusGift = false;   // set below from the save (not for the free play of a version-1 save)
            static Dictionary<int, (int, int)> Prices(List<SaveData.KnownPrice> l)
            {
                var d = new Dictionary<int, (int, int)>();
                if (l != null) foreach (var p in l) d[p.item] = (p.price, p.system);
                return d;
            }
            Session.ResetNewGame();
            Session.Hardcore = s.hardcore;
            if (!string.IsNullOrEmpty(s.runId)) Session.RunId = s.runId;   // an older save keeps the new id ResetNewGame made
            Session.PlaySeconds = s.playSeconds;
            Session.Campaign = (Campaign)s.campaign;
            Session.Difficulty = s.difficulty;
            Session.Economy = SaveEconomy(s);
            Session.CampaignMission = s.campaignMission;
            Session.StationIndex = s.station;
            Session.PreviousStationIndex = s.previousStation;
            Session.LastDockedStation = s.lastDockedStation;
            Session.ShipIndex = s.ship;
            Session.Credits = s.credits;
            Session.Equipment = s.equipment ?? new List<ItemStack>();
            Session.Cargo = s.cargo ?? new List<ItemStack>();
            Session.RecentStations = s.recentStations ?? new List<StationStock>();
            // Saves from before the multiplayer event missions took offer 11: a lounge ship seller was 11 then (event
            // mission agents are never saved and never sell a ship).
            foreach (var rs in Session.RecentStations)
                if (rs?.agents != null)
                    foreach (var a in rs.agents)
                        if (a != null && a.offer == 11 && a.sellShip >= 0) a.offer = AgentOffer.SellShip;
            Session.SeenItems = new HashSet<int>(s.seenItems ?? new List<int>());
            // JsonUtility reads a missing list as an empty one, not null: an old save without it counts its own station.
            Session.VisitedStations = new HashSet<int>(s.visitedStations != null && s.visitedStations.Count > 0 ? s.visitedStations : new List<int> { s.station });
            Session.AttackedStations = new HashSet<int>(s.attackedStations ?? new List<int>());
            Session.LowestKnownPrice = Prices(s.lowestPrices);
            Session.HighestKnownPrice = Prices(s.highestPrices);
            Session.SystemVisible = s.systemVisible != null && s.systemVisible.Length > 0 ? s.systemVisible : null;
            Session.SystemShown = new Dictionary<int, bool>();
            foreach (var e in s.systemShown ?? new List<string>())
            {
                int bar = e?.LastIndexOf('|') ?? -1;
                if (bar <= 0) continue;
                string key = e.Substring(0, bar);
                if (Modding.ModWorld.TryResolveSystem(key, out int system)) Session.SystemShown[system] = e.Substring(bar + 1) == "1";
            }
            // GameRecord::load: Loma (the Valkyrie add-on owned) and Shima always visible after loading.
            if (Session.SystemVisible != null && Session.SystemVisible.Length > GalaxyMap.ShimaSystem)
                Session.SystemVisible[GalaxyMap.LomaSystem] = Session.SystemVisible[GalaxyMap.ShimaSystem] = true;
            Session.JumpgatesUsed = s.jumpgatesUsed;
            Session.Kills = s.kills;
            Session.PirateKills = s.pirateKills;
            if (s.standing != null && s.standing.Length == 2) Session.Standing = s.standing;
            Session.ModRaceStanding = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var e in (s.modRaceStanding != null && s.modRaceStanding.Count > 0 ? s.modRaceStanding : s.factionStanding) ?? new List<string>())
            {
                int bar = e.LastIndexOf('|');
                if (bar > 0 && int.TryParse(e.Substring(bar + 1), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int v))
                    Session.ModRaceStanding[e.Substring(0, bar)] = Mathf.Clamp(v, -100, 100);
            }
            if (s.version < 2)
            {
                // Saved before the story existed: free play at index 20.
                Session.FreePlay = true;
                Session.CampaignMission = Session.FreePlayMission;
                return;
            }
            Session.FreePlay = s.freePlay;
            Session.KhadorCronusGift = s.khadorCronusGift >= 0 ? s.khadorCronusGift == 1
                : !s.freePlay && s.campaignMission == 77 && s.ship != 37;
            Session.ModCampaign = s.modCampaign ?? "";
            Session.ModGameOptions = new HashSet<string>(s.modGameOptions ?? new List<string>());
            Session.ShipBuilds = new Dictionary<string, string>();
            foreach (var e in s.shipBuilds ?? new List<string>())
            {
                int bar = e?.IndexOf('|') ?? -1;
                if (bar > 0) Session.ShipBuilds[e.Substring(0, bar)] = e.Substring(bar + 1);
            }
            // A session (a server profile) reads the session's board instead (NetState, PirateReports.ApplyShared).
            if (NetSession.Active) PirateReports.ApplyShared(GameHooks.SharedPirateBoard?.Invoke());
            else Session.PirateSightings = s.pirateSightings != null ? s.pirateSightings.FindAll(p => p != null) : new List<PirateSighting>();
            Session.NextPirateSightingId = s.nextPirateSightingId;
            Session.StoryMission = s.storyMission ?? new StoryMission();
            Session.StoryStepStart = s.storyStepStart;
            Session.StoryRadioPending = s.storyRadioPending;
            Session.FreelanceCompleted = s.freelanceCompleted;
            Session.StoryCounter = s.storyCounter;
            Session.VoidInvasionSystem = s.voidInvasionSystem;
            Session.VoidInvasionStation = s.voidInvasionStation;
            Session.InvasionDepartures = s.invasionDepartures;
            Session.Unsaleable = new HashSet<int>(s.unsaleable ?? new List<int>());
            // Remake fix (#218): only step 77 locks the jump drive (Story.SetJumpDriveSaleable). A save from before
            // 2026-10-10 kept that lock past the Valkyrie campaign, as the item number, on every Khador Drive bought or built
            // later; StoryMissionSelect drops it the same way.
            if (Session.CampaignMission != 77) Session.Unsaleable.Remove(GalaxyMap.KhadorDriveItem);
            if (s.version >= 3)
            {
                Session.FreelanceMission = s.freelanceMission ?? new FreelanceMission();
                Session.Passengers = s.passengers;
                if (s.usedMissionTypes != null && s.usedMissionTypes.Length == 15) Session.UsedMissionTypes = s.usedMissionTypes;
                Session.InformerKilled = s.informerKilled;
                Session.Wingmen = s.wingmen ?? new List<string>();
                Session.WingmanRace = s.wingmanRace;
                Session.WingmanContractMs = s.wingmanContractMs;
                if (s.wingmanPortrait != null && s.wingmanPortrait.Length == 5) Session.WingmanPortrait = s.wingmanPortrait;
                Session.WingmenHired = s.wingmenHired;
                Session.UnlockedBlueprints = new HashSet<int>(s.unlockedBlueprints ?? new List<int>());
                // Saves made while step 59 locked the Liberator blueprint again (the original's lock is a no-op): back,
                // for every game past 58 (a Supernova campaign advances through 58 too: startSupernova's 0x54 steps).
                if (Session.CampaignMission > 58) Session.UnlockedBlueprints.Add(179);
                Session.StoryAgentsAccepted = new HashSet<int>(s.storyAgentsAccepted ?? new List<int>());
                Session.GraphQuests = s.graphQuests ?? new List<GraphQuestState>();
                Session.GraphQuestsDone = new HashSet<string>(s.graphQuestsDone ?? new List<string>());
                Session.GraphQuestRepeats = new Dictionary<string, float>();
                foreach (var r in s.graphQuestRepeats ?? new List<string>())
                {
                    int bar = r.LastIndexOf('|');
                    if (bar > 0 && float.TryParse(r.Substring(bar + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float at))
                        Session.GraphQuestRepeats[r.Substring(0, bar)] = at;
                }
                GraphQuestState.RestorePending = Session.GraphQuests.Count > 0;   // they run again from their checkpoints
                if (s.version < 11)   // older saves: a blueprint seller whose blueprint is owned was bought from
                    foreach (var sa in AgentData.StoryAgents)
                        if (sa.sellBlueprint >= 0 && Session.UnlockedBlueprints.Contains(sa.sellBlueprint)) Session.StoryAgentsAccepted.Add(sa.index);
                // Coordinates bought while the map was overridden were lost (GalaxyMap.Reveal) and the seller had nothing
                // left: every paid coordinates seller's system is revealed.
                foreach (var sa in AgentData.StoryAgents)
                    if (sa.sellItemSystem >= 0 && Session.StoryAgentsAccepted.Contains(sa.index) && Session.SystemVisible != null
                        && sa.sellItemSystem < Session.SystemVisible.Length)
                        Session.SystemVisible[sa.sellItemSystem] = true;
                Session.ShipMods = s.shipMods ?? new List<int>();
                Session.Blueprints = s.blueprints ?? new List<BlueprintState>();
                Session.PendingProducts = s.pendingProducts ?? new List<PendingProduct>();
                Session.GoodsProduced = s.goodsProduced;
                if (s.medals != null && s.medals.Length == 45) Session.Medals = s.medals;
                Session.AsteroidsDestroyed = s.asteroidsDestroyed; Session.OreMined = s.oreMined; Session.CoresMined = s.coresMined;
                Session.CratesSalvaged = s.cratesSalvaged; Session.JunkDestroyed = s.junkDestroyed; Session.BattleshipsDestroyed = s.battleshipsDestroyed;
                Session.HighestCredits = s.highestCredits; Session.LastArrivalHullPercent = s.lastArrivalHullPercent;
                Session.CapitalLootReadyAt = s.capitalLootReadyAt;
                Session.OreTypesMined = new HashSet<int>(s.oreTypesMined ?? new List<int>());
                Session.BoozeBought = s.boozeBought; Session.AlienRemainsCollected = s.alienRemainsCollected;
                Session.BoozeTypes = new HashSet<int>(s.boozeTypes ?? new List<int>());
                Session.CoreTypesMined = new HashSet<int>(s.coreTypesMined ?? new List<int>());
                Session.InformerFailed = s.informerFailed;
                Session.AgentsTalkedTo = s.agentsTalkedTo; Session.OffersDeclined = s.offersDeclined; Session.OffersRepeated = s.offersRepeated;
                Session.AcceptedBlindRisk = s.acceptedBlindRisk; Session.AcceptedBlindMap = s.acceptedBlindMap;
                Session.ContainersDelivered = s.containersDelivered; Session.PassengersDelivered = s.passengersDelivered;
            }
            if (s.version >= 4)
            {
                Session.KaamoState = s.kaamoState;
                Session.KaamoItems = s.kaamoItems ?? new List<ItemStack>();
                Session.KaamoShips = s.kaamoShips ?? new List<StoredShip>();
            }
            if (s.version >= 5)
            {
                Session.SelectedSecondary = s.selectedSecondary;
                Session.BombsDetonated = s.bombsDetonated;
                if (s.pirateBaseDestroyed != null && s.pirateBaseDestroyed.Length == 4) Session.PirateBaseDestroyed = s.pirateBaseDestroyed;
                Session.Hints = new HashSet<int>(s.hints ?? new List<int>());
                Session.GraveRiserKills = s.graveRiserKills;
                Session.CloakMs = s.cloakMs;
            }
            if (s.version >= 6)
            {
                Session.ParkedShip = s.hasParkedShip ? s.parkedShip : null;   // JsonUtility writes an empty object for null
                Session.StoryTargets = s.storyTargets ?? new List<int>();
            }
            if (s.version >= 7)
            {
                Session.Wanted = s.wanted ?? new List<WantedState>();
                Session.CollectedBounties = s.collectedBounties != null && s.collectedBounties.Length == 4 ? s.collectedBounties : new int[4];
                Session.WantedHints = s.wantedHints;
                Session.HiddenBlueprintsFound = s.hiddenBlueprintsFound;
            }
            else { Session.Wanted = new List<WantedState>(); Session.CollectedBounties = new int[4]; Session.WantedHints = 0; Session.HiddenBlueprintsFound = 0; }
            Session.EliteFlags = new HashSet<int>(s.eliteFlags ?? new List<int>());
            Session.OreStreak = s.oreStreak;
            Session.BlindKills = s.blindKills;
            Session.LomaTollPaid = s.lomaTollPaid;
            Session.LomaTollRefused = s.lomaTollRefused;
            Story.RepairCheckpoint();
        }
    }
}
