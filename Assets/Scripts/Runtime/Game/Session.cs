// Session.cs
// Choices made in the main menu that the game scenes read (MenuTouchWindow: startGOF2 / startValkyrie /
// startSupernova, difficulty stored at options+0x2c: Easy 0, Normal 0.5, Hard 1, Extreme 1.5), and the player's state (the
// original's Status). A new game starts like Status::resetGame 0xba78c: 0 credits, ship 10 "Phantom" at station 78
// "Var Hastra" (system 15 Mido) with the starting equipment, empty cargo. Saving and loading: SaveGame.

using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Data
{
    public enum Campaign { GalaxyOnFire2, Valkyrie, Supernova }

    /// <summary>The item and ship tables a game uses (Database.Load): Android = the Android OBB's (the remake's data files),
    /// Default = the macOS / Windows / iPhone ones (economy_default.json, Reference/tools/shop/build_default_economy.py).</summary>
    public enum Economy { Android, Default }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class Session
    {
        // options+0x2c. The phone menu offers only Normal and Extreme (MenuTouchWindow::OnTouchEnd); Easy and Hard are the
        // PC / Mac Full HD version's other two levels (texts 518 / 520), still handled by the phone code: the cloak cooldown
        // (PlayerEgo ctor: <= 0 / <= 0.6 / <= 1.1 / else), Loma's toll (MGame::OnUpdate: == 0 / 0.5 / 1.0 / else), every
        // "x + x * (difficulty - 0.5)" formula and the "difficulty < 1.0" / "> 0.7" tests.
        public const float DifficultyEasy = 0f;
        public const float DifficultyNormal = 0.5f;
        public const float DifficultyHard = 1f;
        public const float DifficultyExtreme = 1.5f;

        public static Campaign Campaign = Campaign.GalaxyOnFire2;
        public static float Difficulty = DifficultyNormal;
        /// <summary>Chosen with the difficulty when a game starts and kept with the save; every Database.Load reads it.</summary>
        public static Economy Economy = Economy.Default;

        /// <summary>The economy's short name for the menus and the save slots.</summary>
        public static string EconomyName(Economy e) => e == Economy.Android
            ? Localization.Extra("economyAndroid", "Android economy")
            : Localization.Extra("economyDefault", "PC economy");
        /// <summary>Status::hardCoreMode 0xb9148: difficulty == 1.5. The economy, mining, standing and shop rules of Extreme
        /// only; the level-scaled formulas use DifficultyFactor.</summary>
        public static bool IsExtreme => Difficulty > 1.25f;
        /// <summary>The original's "x + x * (difficulty - 0.5)": x0.5 Easy, x1 Normal, x1.5 Hard, x2 Extreme (NPC hulls and
        /// guns, raider and mission enemy counts, static objects, the Wanted and the Kaamo outposts).</summary>
        public static float DifficultyFactor => 1f + (Difficulty - 0.5f);

        /// <summary>The difficulty's name (518 Easy / 519 Normal / 520 Hard / 25 Extreme).</summary>
        public static string DifficultyName(float d) =>
            Localization.Get(d > 1.25f ? 25 : d > 0.75f ? 520 : d > 0.25f ? 519 : 518);

        /// <summary>
        /// Status+0x1e8, the campaign index (Story): one value per story step, 0..162. Shop stock, ship dealers, NPC
        /// strength and several locks depend on it.
        /// </summary>
        public static int CampaignMission;

        /// <summary>Status slot 0: the current step's mission (Story).</summary>
        public static StoryMission StoryMission = new StoryMission();
        /// <summary>Status+0x100: playing time (s) when the current step started.</summary>
        public static float StoryStepStart;
        /// <summary>Status+0x178: a story radio call is due in space (indices 93 / 111 / 143).</summary>
        public static bool StoryRadioPending;
        /// <summary>Status+0x1c4: freelance missions completed.</summary>
        public static int FreelanceCompleted;
        /// <summary>Status missions[1]: the freelance mission (Freelance), type -1 = none.</summary>
        public static FreelanceMission FreelanceMission = new FreelanceMission();
        /// <summary>Status+0x34: passengers aboard (Passenger missions).</summary>
        public static int Passengers;
        /// <summary>Status+0x50: freelance types already offered (every type once before repeats).</summary>
        public static bool[] UsedMissionTypes = new bool[15];
        /// <summary>Status+0xf0 / +0xf1: the Informer mission's spy is dead / another ship died in its orbit.</summary>
        public static bool InformerKilled, InformerFailed;
        /// <summary>Bar statistics: Status+0xd0 agents talked to, +0xe0 declines, +0xe4 repeats, +0xe8 / +0xec accepted
        /// without asking the risk / for the map, +0x9c containers and +0xb8 passengers delivered.</summary>
        public static int AgentsTalkedTo, OffersDeclined, OffersRepeated, AcceptedBlindRisk, AcceptedBlindMap, ContainersDelivered, PassengersDelivered;
        /// <summary>Status+0xd4 / +0x2c / +0x30: hired wingmen (the agent and its friends), their race and the contract time
        /// left (ms); Wingmen.</summary>
        public static List<string> Wingmen = new List<string>();
        public static int WingmanRace;
        public static float WingmanContractMs;
        /// <summary>Status+0x28: the hiring agent's portrait (the goodbye dialogue); +0xd4 wingmen hired (statistic, medal 27);
        /// +0xf8 the menu shows "Use EMP blaster" (reset on every station visit, not saved).</summary>
        public static int[] WingmanPortrait = new int[5];
        public static int WingmenHired;
        public static bool WingmanShowEmp = true;
        /// <summary>Blueprints the player owns (BluePrint::unlock), by product item index.</summary>
        public static HashSet<int> UnlockedBlueprints = new HashSet<int>();
        /// <summary>Story agents (agents.json index) whose one-time offer was taken: the original keeps its story agents
        /// in Status (Status::getAgents, saved by RecordHandler::writeAgent) with Agent+0x74 set, so a blueprint or
        /// coordinate seller never offers again (Generator::createAgents only resets offers 9 and 10).</summary>
        public static HashSet<int> StoryAgentsAccepted = new HashSet<int>();
        /// <summary>Remake: the single-player event graph quests and bar missions under way, as of their last checkpoint
        /// (EventRunner), and the quests finished (by graph name).</summary>
        public static List<GraphQuestState> GraphQuests = new List<GraphQuestState>();
        public static HashSet<string> GraphQuestsDone = new HashSet<string>();
        /// <summary>Remake: the repeatable quests finished ("mission repeat"), each with the playing time (PlaySeconds) from which
        /// it may start again (EventRunner.CheckQuestStarts).</summary>
        public static Dictionary<string, float> GraphQuestRepeats = new Dictionary<string, float>();
        /// <summary>Status+0x18: each touched blueprint's progress (Blueprints); +0x1c products waiting at a station;
        /// +0x1d4 goods produced (completed runs).</summary>
        public static List<BlueprintState> Blueprints = new List<BlueprintState>();
        public static List<PendingProduct> PendingProducts = new List<PendingProduct>();
        public static int GoodsProduced;

        /// <summary>Achievements: the 45 medal grades (0 none, 1 gold, 2 silver, 3 bronze; Achievements).</summary>
        public static int[] Medals = new int[45];
        /// <summary>Statistics for the Status window and the medals: Status+0xd8 asteroids destroyed, +0xa0 / +0xa4 ore /
        /// cores mined (t) and the types (+0x94 / +0x98), crates salvaged (t), +0xb0 junk destroyed, +0x118 battleships
        /// destroyed, the highest credits, the hull % on the last arrival at a station (Survivor).</summary>
        public static int AsteroidsDestroyed, OreMined, CoresMined, CratesSalvaged, JunkDestroyed, BattleshipsDestroyed, HighestCredits;
        public static int LastArrivalHullPercent = 100;
        /// <summary>Remake (World.CapitalShips): the playing time (PlaySeconds) from which a carrier / Vossk battleship carries
        /// loot again (an hour after the last one the player destroyed with its crate).</summary>
        public static float CapitalLootReadyAt;
        /// <summary>Medal 22 (Harum-Scarum): docked with no weapon or no equipment item mounted, taken when the station loads
        /// (the original checks the medals at docking, before the hangar can change the loadout; the remake's check waits for
        /// the landing and the conversations, and stripping the ship meanwhile earned it, #46). Not saved: a load docks anew.</summary>
        public static bool ArrivedWithoutGear;
        /// <summary>#122: Khador's Cronus at Kothar is a gift once (ModStation::OnTouchEnd 0xea4ec, step 77:
        /// Station::addShip(Ship::makeShip(ships[0x25], 0)), a dealer row priced 0 whatever the table says; the Default economy
        /// lists the Cronus at 1 200 000). Set as that row appears, cleared when it is bought (Hangar.BuyShipPrice): a traded-
        /// back Cronus is a row at the table price again, as in the original. Saved.</summary>
        public static bool KhadorCronusGift;
        public static HashSet<int> OreTypesMined = new HashSet<int>(), CoreTypesMined = new HashSet<int>();
        /// <summary>Status+0xa8 (medal 8 Personal Need): the booze tonnes gained per hangar visit (ModStation::OnKeyPress /
        /// OnTouchEnd); Status+0xac (medal 9 Barkeeper): the booze types traded (items 132-153); Status+0xcc (medal 21
        /// Alien Hunter): tonnes captured from Void crates (KIPlayer::captureCrate).</summary>
        public static int BoozeBought, AlienRemainsCollected;
        public static HashSet<int> BoozeTypes = new HashSet<int>();
        public static bool IsBooze(int item) => item >= 132 && item <= 153;
        /// <summary>Ship+0x78 (Ship::addMod, each at most once; Reference/research/blueprints_mods.md 2): the current ship's
        /// mods: 0 +40 hull, 1 +30 t cargo, 2 +1 equipment slot, 3 handling +0.2. They belong to the hull (cleared when
        /// the ship is traded).</summary>
        public static List<int> ShipMods = new List<int>();
        /// <summary>Remake: with Settings.KaamoStacking a mod can be fitted again (a level is how many times it is in the
        /// list), up to MaxModLevel; the original's rule is one of each.</summary>
        public static void AddShipMod(int mod)
        {
            if (mod >= 0 && (!ShipMods.Contains(mod) || (Settings.KaamoStacking && ModLevel(mod) < MaxModLevel))) ShipMods.Add(mod);
        }
        /// <summary>Remake: the highest level of one Kaamo upgrade with stacking: Settings.KaamoMaxLevel (default 5; players'
        /// feedback 2026-10: uncapped, a Cronus reached 45 equipment slots), the session's in multiplayer, where a server with
        /// profiles cuts them back to it (NetProfiles.ClampMods). Levels fitted before a lower limit stay in single player.</summary>
        public static int MaxModLevel => Settings.KaamoMaxLevel;
        public static bool HasMod(int mod) => ShipMods.Contains(mod);
        /// <summary>How many times the mod is fitted (its level; 0 = none).</summary>
        public static int ModLevel(int mod)
        {
            int n = 0;   // the List itself, not the IEnumerable overload: that boxed its enumerator (MaxLoad, every HUD frame)
            if (ShipMods != null) foreach (int m in ShipMods) if (m == mod) n++;
            return n;
        }
        public static int ModLevel(IEnumerable<int> mods, int mod)
        {
            int n = 0;
            if (mods != null) foreach (int m in mods) if (m == mod) n++;
            return n;
        }

        /// <summary>Status+0x114 (Reference/research/kaamo_club.md 2): the Kaamo Club, 0 not owned, 1 Mkkt Bkkt's call
        /// heard, 2 purchasable, 3 owned.</summary>
        public static int KaamoState;
        /// <summary>Status+0xf4: the selected secondary weapon item (-1 = none), kept while it stays mounted.</summary>
        public static int SelectedSecondary = -1;
        /// <summary>Status+200: nukes detonated (medal 20).</summary>
        public static int BombsDetonated;
        /// <summary>Status+0x4c: the four pirate bases destroyed (PirateBases.Stations order).</summary>
        public static bool[] PirateBaseDestroyed = new bool[4];
        /// <summary>Status+0xf9: an outpost was destroyed; the next docking pays the reward (not saved, like the original).</summary>
        public static bool PirateBaseRewardPending;
        /// <summary>The radio hints already given (0x23 / 0x24: the Nivelian pirate hints).</summary>
        public static HashSet<int> Hints = new HashSet<int>();
        /// <summary>Status+0x13c best: kills during one emergency-system bubble (medal 43 "Grave Riser").</summary>
        public static int GraveRiserKills;
        /// <summary>The elite medals' in-flight counters: Status+0x124 mining runs to the last layer in a row (38 Ore Athlete;
        /// reset by a failed run and on docking), +0x11c kills without a scanner (40 Blindfolded Killer; reset on docking and
        /// at a level start with a scanner), +0x12c asteroids destroyed by rockets / missiles until one runs out (41 Asteroid
        /// Hazard), +0x134 ships EMP-disabled at once (42 Jammer), +0x144 asteroids destroyed by one Liberator (44 Hot Shot);
        /// EliteFlags = the medals whose flag (+0x128 / +0x120 / +0x130 / +0x138 / +0x148) is set.</summary>
        public static int OreStreak, BlindKills, RocketAsteroids, EmpDisabledNow, LiberatorAsteroids;
        public static HashSet<int> EliteFlags = new HashSet<int>();
        /// <summary>Status+0x110 / +0x111: Loma's pirate toll paid / refused (cleared when entering another system).</summary>
        public static bool LomaTollPaid, LomaTollRefused;
        /// <summary>Status+0xc0: ms spent cloaked (medal 19 "Ninja", minutes).</summary>
        public static long CloakMs;
        /// <summary>Status+0x68: the gamma pool (0..100) carried between the supernova orbits, -1 = full.</summary>
        public static float PlayerGamma = -1f;
        /// <summary>Status+0x14c, the storage station: its goods (station 108's stock while owned) and parked hulls.</summary>
        public static List<ItemStack> KaamoItems = new List<ItemStack>();
        public static List<StoredShip> KaamoShips = new List<StoredShip>();
        /// <summary>Status+0x174: the counter of story types 0xa8 / 0xb8.</summary>
        public static int StoryCounter;
        /// <summary>Status+0x8c: the own ship parked while a story loaner is flown (null = none).</summary>
        public static ParkedShip ParkedShip;
        /// <summary>Status+0x90: the target stations of a type-0xa3 step (step 59's convoys); -1 = that one is done.</summary>
        public static List<int> StoryTargets = new List<int>();
        /// <summary>Status+0x00: the Most Wanted criminals' state (WantedBoard), by wanted.json index.</summary>
        public static List<WantedState> Wanted = new List<WantedState>();
        /// <summary>Status+0x04: bounties collected per board (0 Terran, 1 Vossk, 2 Nivelian, 3 Midorian).</summary>
        public static int[] CollectedBounties = new int[4];
        /// <summary>Remake: the Pirate activity board's reports (PirateReports) and the next report's id.</summary>
        public static List<PirateSighting> PirateSightings = new List<PirateSighting>();
        public static int NextPirateSightingId;
        /// <summary>hints[0x2a] (601, the Terran board), hints 613 (the other boards) and 0x33-0x36 (3232, ships 45-48 at
        /// Quineros) shown, as bits 0 / 1 / 2-5.</summary>
        public static int WantedHints;
        /// <summary>Status+0x58 bool[5]: the Supernova wrecks' hidden blueprints found (bit k, TrafficPlan.HiddenBlueprints).</summary>
        public static int HiddenBlueprintsFound;
        /// <summary>Status+0x7c / +0x80: the Void-invasion system and station (-1 none, -10 never again).</summary>
        public static int VoidInvasionSystem = -1, VoidInvasionStation = -1;
        /// <summary>Status+0x88: departures to other stations since the invasion station was rolled (index 32-44, re-rolled at 10).</summary>
        public static int InvasionDepartures;
        /// <summary>The alien orbit (Status+0x78, the Void's home orbit): StationIndex while the player is there.</summary>
        public const int VoidOrbit = -1;
        public static bool InVoidOrbit => StationIndex == VoidOrbit;
        /// <summary>Status+0x84: the station a wormhole ride out of the Void returns to (not saved: only set in flight).</summary>
        public static int VoidReturnStation = 10;
        /// <summary>Level::comingFromAlienWorld: the next level starts with a wormhole closing behind the player.</summary>
        public static bool ComingFromVoid;
        /// <summary>Remake events: the next level starts with a wormhole closing behind the player (an event's wormhole ride).</summary>
        public static bool ArrivedByWormhole;
        /// <summary>Level::initStreamOutPositionAfterCutscene: index 143 won in space at Var Lupra, so the 144 cutscene's reload
        /// into the 145 attack is a stream-out arrival (LevelScript::process 0x90). Not saved, like the original's static.</summary>
        public static bool StreamOutAfterCutscene;
        /// <summary>Level::lastMissionFreighterHitpoints: Errkt's freighter's hull carried from index 40 into 41 (-1 none).</summary>
        public static int LastFreighterHull = -1;
        /// <summary>Globals::lastCampaignMissionFailed / FailCount: the campaign index that failed and how often in a row (globals:
        /// they survive loading a save, unlike the rest of Session).</summary>
        public static int LastFailedMission = -1, FailCount;
        /// <summary>The ending (ModStation::OnTouchEnd at index 43) is to be shown by the main menu's backdrop.</summary>
        public static bool EndingPending;
        /// <summary>Items the story made unsaleable (Item::setUnsaleable: Gunant's Drill, the Alien Remains ...).</summary>
        public static HashSet<int> Unsaleable = new HashSet<int>();

        /// <summary>
        /// Remake-only free play: no story steps. Plays as if past the tutorial (index 20: hangar, map, lounge and the Mido
        /// ship dealer unlocked), with the remake's help for leaving gateless Mido (GalaxyMap.HasJumpDrive, Var Hastra's
        /// drill and energy cells). Saves from before the story load as free play.
        /// </summary>
        public static bool FreePlay;
        /// <summary>Remake mods: the mod whose campaign this game is (Modding.ModCampaigns; "": a GoF2 game).</summary>
        public static string ModCampaign = "";
        /// <summary>Remake: a hardcore (permadeath) game, the new game's choice (MainMenu): it saves only to the auto-save, and
        /// dying deletes every save of the run (SaveGame.DeleteRun by RunId) and goes to the main menu (FlightHud). Not the
        /// original's "hardcore mode", which is the Extreme difficulty (IsExtreme).</summary>
        public static bool Hardcore;
        /// <summary>This game's identity (a new one at every new game; saved), so a hardcore death finds every slot of the run.</summary>
        public static string RunId = "";
        /// <summary>Remake mods: the mods' new-game options switched on for this game ("mod_id:option_id",
        /// Modding.ModGameOptions; saved).</summary>
        public static HashSet<string> ModGameOptions = new HashSet<string>();

        /// <summary>Remake mods: the player's build of each customizable ship (Modding.ModShipKits), by the ship's "mod_id:ship_id"
        /// key; build text "slot=part;...;tier=N". Saved.</summary>
        public static Dictionary<string, string> ShipBuilds = new Dictionary<string, string>();
        public const int FreePlayMission = 20;

        /// <summary>
        /// Remake multiplayer: the session's world is the finished game (every campaign won, index 162) while the player
        /// still plays free (no story steps): full shop stock and dealer ships, the add-ons' revealed systems, Ginoya after
        /// the supernova. Set by UseCompletedWorld, cleared by a new game or a loaded save.
        /// </summary>
        public static bool CompletedWorld;

        /// <summary>The campaign index the world's look and rules read: free play's 20, else the campaign index (162 in a
        /// completed world).</summary>
        public static int WorldIndex => FreePlay && !CompletedWorld ? FreePlayMission : CampaignMission;

        /// <summary>Remake multiplayer: free play in the finished game's world (CompletedWorld). Medals and the Most Wanted
        /// board stay off (free play); the gates that need them (the VoidX, the Specter, the criminals' ships) count as met.</summary>
        public static void UseCompletedWorld()
        {
            FreePlay = true;
            CompletedWorld = true;
            CampaignMission = Story.LastIndex;
        }

        /// <summary>Current station (Status::getStation): one flight level = one station orbit.</summary>
        public static int StationIndex = 78;

        /// <summary>Status' station stack [1]: where the player came from (the planet-jump arrival point), -1 = none.</summary>
        public static int PreviousStationIndex = -1;

        /// <summary>Remake: the station the player last docked at (StationLevel), -1 = none yet. Multiplayer starts a signed-in
        /// pilot and respawns a destroyed one there (it was the faction's home: a free trip home, saving energy cells and
        /// time).</summary>
        public static int LastDockedStation = -1;

        /// <summary>Player ship index (ships.json), Status+0x18c.</summary>
        public static int ShipIndex = 10;

        /// <summary>Mounted items (Ship+0x6c), one entry per occupied slot; secondaries carry their ammo as the amount.</summary>
        public static List<ItemStack> Equipment = StartEquipment();

        /// <summary>Cargo hold (Ship+0x70) in acquisition order; every unit weighs 1 t.</summary>
        public static List<ItemStack> Cargo = new List<ItemStack>();

        /// <summary>Status+0x1ac.</summary>
        public static int Credits;

        /// <summary>Status+0x19c: the last 3 visited stations with their stock (newest last).</summary>
        public static List<StationStock> RecentStations = new List<StationStock>();

        /// <summary>Status+0x70: play time (s, realtime) at the last departure; -1 = never departed.</summary>
        public static float LastDepartureTime = -1f;

        /// <summary>Status+0x54: items the player has inspected, bought or sold (the shop marks the others as new).</summary>
        public static HashSet<int> SeenItems = new HashSet<int>();

        /// <summary>Status+0x3c..0x48: lowest / highest price seen per item and where (system index), -1 = unknown.</summary>
        public static Dictionary<int, (int price, int system)> LowestKnownPrice = new Dictionary<int, (int, int)>();
        public static Dictionary<int, (int price, int system)> HighestKnownPrice = new Dictionary<int, (int, int)>();

        /// <summary>Level::initStreamOutPosition: false = undocking from the station, true = arriving by travel.</summary>
        public static bool ArrivedByTravel;

        /// <summary>Set by the station's launch button: the flight level starts with the launch camera
        /// (LevelScript: a fixed camera 9000 units ahead watches the ship fly past for 7 s).</summary>
        public static bool LaunchedFromStation;

        /// <summary>Remake: the player docked from space (SpaceLevel.Dock), so the station opens with the hangar fly-in.</summary>
        public static bool DockedFromSpace;

        /// <summary>Remake mods: the station's launch at a station docked EVERSPACE 2's way (World.Es2Dock): the flight level
        /// starts on its pad with ES2's take-off instead of the launch camera.</summary>
        public static bool Es2TakeOff;
        /// <summary>Remake mods: the station scene was loaded on its own for a station docked EVERSPACE 2's way (a save, a story
        /// move): the flight level starts docked on its pad and loads the station on top (World.Es2Dock).</summary>
        public static bool Es2StartDocked;
        /// <summary>The flight level found no dock to start on (its model not built): the station opens on its own.</summary>
        public static bool Es2NoRedirect;

        /// <summary>The station was opened by loading a save (ModStation::setGameLoaded, +0xac): no docking fine then
        /// (StationMenu.CheckDockingFine takes and clears it; SpaceLevel clears it too).</summary>
        public static bool ArrivedByLoad;

        /// <summary>Arrived through a jumpgate or the Khador Drive: the arrival camera shows the orbit information
        /// (Hud::drawOrbitInformation, not after a planet jump).</summary>
        public static bool ArrivedBySystemJump;

        /// <summary>Status+0x38: which systems the star map shows (null = not set up yet, filled from systems.json
        /// initiallyVisible by GalaxyMap.Visibility).</summary>
        public static bool[] SystemVisible;
        /// <summary>Remake mods: systems an event / quest showed (true) or hid (false) with "system ... show | hide", over the
        /// map's revealed state and a mod's "available" condition until "reset" (GalaxyMap.Visibility). Saved.</summary>
        public static Dictionary<int, bool> SystemShown = new Dictionary<int, bool>();

        /// <summary>Galaxy::getVisited: stations the player has docked at (the star map's "Already visited" tick).</summary>
        public static HashSet<int> VisitedStations = new HashSet<int> { 78 };

        /// <summary>Level::programmedStation: the destination picked on the star map, -1 = none. The autopilot routes to it
        /// after the launch / arrival camera (LevelScript::setAutoPilotToProgrammedStation).</summary>
        public static int ProgrammedStation = -1;
        /// <summary>Remake (jumpgate route planning): the programmed station a gate hop kept, the route under way toward it; while
        /// ProgrammedStation is still that one, its next gate jumps on without asking (SystemJump). -1 = none; never saved.</summary>
        public static int RouteDestination = -1;
        /// <summary>Remake: the route to the programmed station goes round the dangerous systems (picked in the travel dialog,
        /// StarMapResult.safestRoute), not the fewest jumps: every gate on the way takes GalaxyMap.NextHop's safest hop.</summary>
        public static bool RouteSafest;

        /// <summary>Level doInstantJump / energyCellsForNextJump: the Khador Drive charges for the programmed station
        /// (another system) 5 s into the level, using that many energy cells.</summary>
        public static bool InstantJump;
        public static int EnergyCellsForNextJump;
        /// <summary>Remake: the next Space scene is interstellar space (InterstellarSpace), not the orbit of StationIndex. Read
        /// once by SpaceLevel, never saved: a load is always the orbit the ship left.</summary>
        public static bool EnterInterstellar;
        /// <summary>Remake: the next Space scene is an arrival out of interstellar space, at speed, with no camera of its own and
        /// the warp easing out (Visuals.WarpSky). Read once by SpaceLevel, never saved.</summary>
        public static bool WarpArrival;
        /// <summary>... from a cruise to the planet of a station in the same system (Navigation.RunPlanet): it faces that planet.</summary>
        public static bool WarpLocal;
        /// <summary>Remake: that arrival's ship against its course (Navigation.Leave), so the view doesn't turn at the swap.</summary>
        public static Quaternion WarpHeading = Quaternion.identity;
        /// <summary>Remake: the ship came into interstellar space through the Khador Slipstream's hole: no light to fade out,
        /// the frame was already this level's (World.KhadorFx).</summary>
        public static bool RiftEntry;
        /// <summary>Remake: and the chase view in the ship's frame, so the ship stays where it is on the screen.</summary>
        public static Vector3 WarpView;
        public static Quaternion WarpViewTurn = Quaternion.identity;
        /// <summary>Remake: the alien orbit's arrival point (game units) its approach was built around (World.InterstellarField):
        /// the orbit draws it at random (OrbitLayout landmark 2), so the arrival takes this one. Read once by SpaceLevel.</summary>
        public static Vector3? WarpArrivalPoint;

        /// <summary>Status+0x1dc (Status::jumpgateUsed).</summary>
        public static int JumpgatesUsed;

        /// <summary>Standing: [0] Terran (+) / Vossk (-), [1] Nivelian (+) / Midorian (-), -100..100 (Standing);
        /// a new game starts at 30 / 0.</summary>
        public static int[] Standing = { 30, 0 };

        /// <summary>Remake mods: the player's standing toward each mod race (Modding.ModRaces), by its key ("mod_id:id"),
        /// -100..100; a mod race not in it stands at its own "standing".</summary>
        public static Dictionary<string, int> ModRaceStanding = new Dictionary<string, int>(System.StringComparer.Ordinal);

        /// <summary>Stations whose race the player attacked (Station::setAttackedFriends): on the next visit at least 7
        /// hostile local fighters wait there.</summary>
        public static HashSet<int> AttackedStations = new HashSet<int>();

        /// <summary>Status+0x19c as a stack of orbits (Status::departStation / addStationToStack): the last 3 stations flown
        /// to, oldest first; one already on it keeps its place. A station pushed off is loaded afresh on the next visit, its
        /// attacked-friends flag (Station::setAttackedFriends) gone. Not saved: after a load it fills up again.</summary>
        public static List<int> RecentOrbits = new List<int>();

        /// <summary>Status::departStation on arrival in an orbit (SpaceLevel): pushes the station; the attacks on stations no
        /// longer on the stack are forgotten (only once it is full, so a load's empty stack forgets nothing early).</summary>
        public static void ArriveInOrbit(int station)
        {
            if (station < 0 || RecentOrbits.Contains(station)) return;
            RecentOrbits.Add(station);
            while (RecentOrbits.Count > Shop.RecentStationCount) RecentOrbits.RemoveAt(0);
            if (RecentOrbits.Count >= Shop.RecentStationCount) AttackedStations.RemoveWhere(s => !RecentOrbits.Contains(s));
        }

        /// <summary>Status+0x1c0 kills (also XP), +0x1d8 pirate kills.</summary>
        public static int Kills, PirateKills;

        /// <summary>Status+0x64 / +0x5c / +0x60: the player's hull, shield and armor between levels (-1 = full).</summary>
        public static int PlayerHull = -1, PlayerArmor = -1;
        public static float PlayerShield = -1f;

        /// <summary>DAT_00252b0c: XP needed per rank 0..20 (Status::checkForLevelUp).</summary>
        static readonly int[] RankXp = { 0, 7, 21, 42, 70, 105, 147, 196, 252, 315, 385, 462, 546, 637, 735, 840, 952, 1071, 1197, 1330, 1650 };

        /// <summary>Status::checkForLevelUp 0xb9978: XP = ore mined / 50 (+0xa0) + kills (+0x1c0) + wingmen hired / 3 (+0xd4)
        /// + cores mined (+0xa4) + 2 x missions completed (+0x1c4) + the campaign index (+0x1e8) + stations visited (+0x1d0);
        /// the rank (Status::getLevel) is the highest threshold reached.</summary>
        public static int Xp => OreMined / 50 + Kills + WingmenHired / 3 + CoresMined + 2 * FreelanceCompleted
                              + (FreePlay ? 0 : CampaignMission) + VisitedStations.Count;

        public static int Rank
        {
            get
            {
                int xp = Xp, r = 0;
                for (int i = 0; i < RankXp.Length; i++) if (xp >= RankXp[i]) r = i;
                return r;
            }
        }

        /// <summary>Status::resetGame: 2x Nirai Charged Pulse, 6 Edo missiles, Fluxed Matter Shield, T'yol,
        /// Telta Ecoscan, Synchrotron Boost.</summary>
        static List<ItemStack> StartEquipment() => new List<ItemStack>
        {
            new ItemStack(2, 1), new ItemStack(2, 1), new ItemStack(36, 6),
            new ItemStack(54, 1), new ItemStack(59, 1), new ItemStack(82, 1), new ItemStack(73, 1),
        };

        // ---- playing time (Status::getPlayingTime, shown in the save previews) ------------------------------------

        static float playBase, playSince;
        public static float PlaySeconds
        {
            get => playBase + (Time.realtimeSinceStartup - playSince);
            set { playBase = value; playSince = Time.realtimeSinceStartup; }
        }

        // ---- auto-save (slot 0, see SaveGame) ---------------------------------------------------------------

        public static bool HasAutosave => SaveGame.Exists(SaveGame.AutoSaveSlot);

        /// <summary>ModStation::autosave 0xe9eb4: save slot 0 when docking.</summary>
        public static void Autosave() => SaveGame.AutoSave();

        /// <summary>GameRecord::load(last save): back to the docked state of the auto-save.</summary>
        public static bool LoadAutosave() => SaveGame.Load(SaveGame.AutoSaveSlot);

        public static void ResetNewGame()
        {
            PlaySeconds = 0f;
            StationIndex = 78;
            PreviousStationIndex = -1;
            LastDockedStation = -1;
            ShipIndex = 10;
            Equipment = StartEquipment();
            Cargo = new List<ItemStack>();
            Credits = 0;
            CampaignMission = 0;
            StoryMission = new StoryMission();
            StoryStepStart = 0f;
            StoryRadioPending = false;
            FreelanceCompleted = 0;
            FreelanceMission = new FreelanceMission();
            Passengers = 0;
            UsedMissionTypes = new bool[15];
            InformerKilled = InformerFailed = false;
            Wingmen = new List<string>();
            WingmanRace = 0;
            WingmanContractMs = 0f;
            WingmanPortrait = new int[5];
            WingmenHired = 0;
            WingmanShowEmp = true;
            UnlockedBlueprints = new HashSet<int>();
            StoryAgentsAccepted = new HashSet<int>();
            GraphQuests = new List<GraphQuestState>();
            ModCampaign = "";
            Hardcore = false;
            RunId = System.Guid.NewGuid().ToString("N");
            ModGameOptions = new HashSet<string>();
            ShipBuilds = new Dictionary<string, string>();
            GraphQuestsDone = new HashSet<string>();
            GraphQuestRepeats = new Dictionary<string, float>();
            GameHooks.ResetLocalEvents?.Invoke();   // single player's graph runs go with the game
            Blueprints = new List<BlueprintState>();
            PendingProducts = new List<PendingProduct>();
            GoodsProduced = 0;
            Medals = new int[45];
            AsteroidsDestroyed = OreMined = CoresMined = CratesSalvaged = JunkDestroyed = BattleshipsDestroyed = HighestCredits = 0;
            LastArrivalHullPercent = 100;
            CapitalLootReadyAt = 0f;
            ArrivedWithoutGear = false;
            KhadorCronusGift = false;
            OreTypesMined = new HashSet<int>();
            BoozeBought = AlienRemainsCollected = 0;
            BoozeTypes = new HashSet<int>();
            CoreTypesMined = new HashSet<int>();
            ShipMods = new List<int>();
            KaamoState = 0;
            SelectedSecondary = -1;
            BombsDetonated = 0;
            PirateBaseDestroyed = new bool[4];
            PirateBaseRewardPending = false;
            Hints = new HashSet<int>();
            GraveRiserKills = 0;
            OreStreak = BlindKills = RocketAsteroids = EmpDisabledNow = LiberatorAsteroids = 0;
            EliteFlags = new HashSet<int>();
            LomaTollPaid = LomaTollRefused = false;
            CloakMs = 0;
            PlayerGamma = -1f;
            KaamoItems = new List<ItemStack>();
            KaamoShips = new List<StoredShip>();
            AgentsTalkedTo = OffersDeclined = OffersRepeated = AcceptedBlindRisk = AcceptedBlindMap = ContainersDelivered = PassengersDelivered = 0;
            StoryCounter = 0;
            ParkedShip = null;
            StoryTargets = new List<int>();
            Wanted = new List<WantedState>();
            CollectedBounties = new int[4];
            PirateSightings = new List<PirateSighting>();
            NextPirateSightingId = 0;
            WantedHints = 0;
            HiddenBlueprintsFound = 0;
            VoidInvasionSystem = VoidInvasionStation = -1;
            InvasionDepartures = 0;
            VoidReturnStation = 10;
            ComingFromVoid = false;
            ArrivedByWormhole = false;
            StreamOutAfterCutscene = false;
            LastFreighterHull = -1;
            EndingPending = false;
            SupernovaChallenge.End();   // a new game or a load ends the challenge (its Start sets it again after)
            Unsaleable = new HashSet<int>();
            FreePlay = false;
            CompletedWorld = false;
            RecentStations = new List<StationStock>();
            LastDepartureTime = -1f;
            SeenItems = new HashSet<int>();
            LowestKnownPrice = new Dictionary<int, (int, int)>();
            HighestKnownPrice = new Dictionary<int, (int, int)>();
            ArrivedByTravel = false;
            LaunchedFromStation = false;
            DockedFromSpace = false;
            Es2TakeOff = false;
            Es2StartDocked = false;
            Es2NoRedirect = false;
            ArrivedByLoad = false;
            ArrivedBySystemJump = false;
            SystemVisible = null;
            SystemShown = new Dictionary<int, bool>();
            VisitedStations = new HashSet<int> { 78 };
            ProgrammedStation = -1;
            RouteDestination = -1;
            RouteSafest = false;
            InstantJump = false;
            EnergyCellsForNextJump = 0;
            EnterInterstellar = false;
            WarpArrival = WarpLocal = false;
            WarpHeading = WarpViewTurn = Quaternion.identity;
            RiftEntry = false;
            WarpView = Vector3.zero;
            WarpArrivalPoint = null;
            JumpgatesUsed = 0;
            Standing = new[] { 30, 0 };
            ModRaceStanding = new Dictionary<string, int>(System.StringComparer.Ordinal);
            AttackedStations = new HashSet<int>();
            RecentOrbits = new List<int>();
            Kills = PirateKills = 0;
            PlayerHull = PlayerArmor = -1;
            PlayerShield = -1f;
        }
    }
}
