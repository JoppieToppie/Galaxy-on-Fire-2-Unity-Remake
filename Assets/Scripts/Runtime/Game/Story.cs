// Story.cs
// The campaign's rules as plain C# (Reference/research/campaign_flow.md): the current step's mission, when it is complete,
// advancing to the next step with its side effects, and the restrictions a story mission puts on the player.
//   Status::missionCompleted 0x0b924c      IsComplete: the completion rule per objective type (§2.2)
//   Status::nextCampaignMission 0x0b6c98   Advance: index + 1 (52 -> 54, 128 -> 130), the new step's mission and its side
//                                          effects (§1.3, §4); index 93 / 111 / 143 flag a story radio call
//   Status::departStation 0x0b63e0         LevelMissionFor: the mission a launch builds its orbit around (§3.2)
//   MGame::dockEvent / Radar / UseKhadorDrive   BlocksDockingAndJumps: "Not possible on a mission." (525, §7)
//   ModStation::OnInitialize 0x0e8080      OnDocked: step-specific station tweaks (Betty at index 1, free EMP bombs...)
// State lives in Session (CampaignMission = the index, StoryMission = slot 0) and is saved by SaveGame.
// Side effects built: the main campaign (0-45), the Valkyrie add-on (46-84: the loaner ships parked in Status+0x8c,
// the systems it reveals, step 59's target stations, the Liberator / Disruptor blueprints, the mines, the Void Essence, the
// jump drive) and the Supernova add-on (85-162, cases 0x54-0xa1: the Luxury goods and the Gamma Shield I / repair beam /
// plasma kit handed over, systems 27-31 revealed, the evacuation / hacking counters, the mutagen, the Gamma Shield II and
// Chromo Plasma blueprints).

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GoF2Remake.Data
{
    /// <summary>Mission+0x8 values the campaign uses (campaign_flow.md 2.2).</summary>
    public static class StoryType
    {
        public const int Empty = -1, DockedAny = 0x00, Defense = 0x01, Level = 0x04, Wanted = 0x06, Purchase = 0x08,
            Intercept = 0x0a, Dock = 0x0b, Challenge = 0x0c, FreelanceCount = 0x96, CargoLoad = 0x9a, ReachOrbit = 0x9c,
            WeaponAndArmor = 0x9e, DelayedCall = 0xa0, VoidInvasion = 0xa1, TargetList = 0xa3, CallAfterLaunch = 0xa4,
            InOrbit = 0xa5, DeliverOrMount = 0xa6, Counter = 0xa8, ScriptFlag = 0xaa, Lounge = 0xab, LoungeWithGoods = 0xac,
            AmountReached = 0xae, Passengers = 0xb8, EquipCategory = 0xbd;
    }

    /// <summary>A campaign mission (the original's Mission object in Status slot 0).</summary>
    [System.Serializable]
    public class StoryMission
    {
        public int type = StoryType.Empty;
        public int reward;
        public int station = -1;
        public int value;
        public int goodsItem = -1, goodsAmount;
        public bool visible = true;
        public bool won;

        public bool IsEmpty => type == StoryType.Empty;

        public static StoryMission From(StoryStep s) => s == null ? new StoryMission() : new StoryMission
        {
            type = s.type, reward = s.reward, station = s.station, value = s.value,
            goodsItem = s.goodsItem, goodsAmount = s.goodsAmount, visible = s.visible,
        };
    }

    /// <summary>What IsComplete needs to know about the moment (Status::missionCompleted's arguments and reads).</summary>
    public struct StoryContext
    {
        public bool docked;           // in a station (else in space)
        public bool inLounge;         // docked, in the Space Lounge with its intro finished
        public float levelMs;         // in space: time since the level started
        public int station;           // the current station / orbit
    }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static partial class Story
    {
        public const int GameWonIndex = 45, Dlc1WonIndex = 84, LastIndex = 162;

        /// <summary>Every system a step reveals (Advance's RevealSystem calls): the finished game's map.</summary>
        public static readonly int[] RevealedSystems = { 6, 22, 23, 24, 25, 27, 28, 29, 30, 31 };

        public static int Index => Session.CampaignMission;
        public static StoryMission Mission => Session.StoryMission;
        public static StoryStep Step => StoryTable.Step(Index);

        /// <summary>Where the current mission is: its target station, or for type 0xa1 (index 40) the station the Void attack
        /// (Status+0x80, shown on the map by the early-warning wormhole). -1 = none / the alien orbit.</summary>
        public static int TargetStation => Mission == null ? -1
                                          : Mission.type == StoryType.VoidInvasion && Index < GameWonIndex ? Session.VoidInvasionStation : Mission.station;

        /// <summary>StarMap::drawOnScreenInfo: the stations the map marks with the story icon. Type 0xa3 (step 59, the arms
        /// convoy) marks every target of Status+0x90 not yet done (done ones are negative) instead of the mission's own
        /// station (101 Valkyrie / system 23, skipped at 0x3b); else the target station.
        /// StarMap::drawOnScreenInfo / Radar::draw also mark: at 116 the Pescal Inartu bars not searched yet (the mission
        /// value's bits, StationMenu.SpecialLounge), at 120 only Maissa (93), where the K'mirrk Toad Mutagen goes (the
        /// level's own orbit, Valadon, isn't marked), at 125 the freighter stations not scanned yet; and the target isn't
        /// marked at 52, 128, 130 and 148-151. The remake marked the plain target at every step until 2026-10.</summary>
        public static bool MapMarks(int station)
        {
            if (station < 0 || Mission == null) return false;
            int v = Mission.value;
            if (Index == 116)
            {
                int slot = station == 94 ? 3 : station >= 90 && station <= 92 ? station - 90 : -1;
                if (slot >= 0 && (v & (1 << slot)) == 0) return true;
            }
            if (Index == 120) return station == 93;
            if (Index == 125)
            {
                int bit = System.Array.IndexOf(FreighterStations, station);
                if (bit >= 0 && (v & (1 << bit)) == 0) return true;
            }
            if (Mission.type == StoryType.TargetList) return Session.StoryTargets.Contains(station);
            if (Index == 52 || Index == 128 || Index == 130 || (Index >= 148 && Index <= 151)) return false;
            return station == TargetStation;
        }

        /// <summary>Status::isFreighterMissionStation: the stations of step 125's decoy freighters, in the order of their bit
        /// in the mission value (the remake's order; StorySpace sets them).</summary>
        public static readonly int[] FreighterStations = { 15, 30, 40, 45, 60, 70, 80, 85, 95 };

        /// <summary>The station the Missions window's map centres on: step 59's first remaining convoy target, else the
        /// target station.</summary>
        public static int MapTarget
        {
            get
            {
                if (Mission != null && Mission.type == StoryType.TargetList)
                    foreach (int t in Session.StoryTargets) if (t >= 0) return t;
                if (Index == 120) return 93;   // Maissa, like the map's story icon (MapMarks)
                return TargetStation;
            }
        }

        /// <summary>Status::gameWon: index &gt; 44.</summary>
        public static bool GameWon => Index >= GameWonIndex;
        /// <summary>Status::dlc1Won: index &gt; 83.</summary>
        public static bool Dlc1Won => Index >= Dlc1WonIndex;

        /// <summary>Status::resetGame: index 0, the prologue mission (Mission(4, 0, 78)).</summary>
        public static void StartNewGame(int index = 0)
        {
            Session.CampaignMission = index;
            Session.StoryMission = StoryMission.From(StoryTable.Step(index));
            Session.StoryStepStart = Session.PlaySeconds;
        }

        /// <summary>MenuTouchWindow::startGOF2 / startValkyrie / startSupernova 0x1540f0 / 0x153ba4 / 0x153e14 after
        /// Status::resetGame. Returns the scene to load: the main game starts in flight with the prologue (index 0, the
        /// Phantom in the Dareius belt), the add-ons docked.</summary>
        public static string StartCampaign(Database db, Campaign campaign)
        {
            StartNewGame(0);
            switch (campaign)
            {
                case Campaign.Valkyrie:
                    while (Index < GameWonIndex) Advance(db);
                    Session.ShipIndex = 5;   // Inflict
                    Session.Equipment = new List<ItemStack> { new ItemStack(2, 1), new ItemStack(5, 1), new ItemStack(36, 10), new ItemStack(81, 1),
                                                                  new ItemStack(51, 1), new ItemStack(86, 1), new ItemStack(85, 1) };
                    Session.StationIndex = 91;   // Dima
                    RevealSystem(db, 6);
                    RevealSystem(db, 25);
                    Session.Kills = 197;
                    AddonStartHints(false);
                    break;
                case Campaign.Supernova:
                    while (Index < Dlc1WonIndex) Advance(db);   // not 84 calls: 52 -> 54 skips one, which landed on 85
                    Session.ShipIndex = 30;   // Berger CrossXT
                    Session.Equipment = new List<ItemStack> { new ItemStack(176, 1), new ItemStack(20, 1), new ItemStack(36, 20), new ItemStack(44, 20),
                                                                  new ItemStack(81, 1), new ItemStack(51, 1), new ItemStack(68, 1), new ItemStack(86, 1),
                                                                  new ItemStack(85, 1), new ItemStack(56, 1) };
                    Session.Cargo = new List<ItemStack> { new ItemStack(GalaxyMap.EnergyCellItem, 8) };
                    Session.StationIndex = 70;   // Dis
                    Session.Kills = 386;
                    AddonStartHints(true);
                    break;
                default:
                    Session.StationIndex = 78;
                    Session.LaunchedFromStation = Session.ArrivedByTravel = false;
                    return "Space";   // module 2: the prologue
            }
            return "Station";
        }

        /// <summary>ModStation::OnInitialize at index 1: Betty with Gunant's Drill (90) and a Telta Quickscan (81), both
        /// unsaleable, fully repaired.</summary>
        static void GiveBetty()
        {
            Session.ShipIndex = 0;
            Session.Equipment = new List<ItemStack> { new ItemStack(90, 1), new ItemStack(81, 1) };
            Session.Unsaleable.Add(90);
            Session.Unsaleable.Add(81);
            Session.PlayerHull = Session.PlayerArmor = -1;
            Session.PlayerShield = -1f;
        }

        // ---- the mission select (remake-only, the main menu's debug panel) -----------------------------------------

        /// <summary>How the player is in a place: docked, launched from its station (the launch camera), arrived from another
        /// orbit (the travel fly-in: planet jump, gate, wormhole, Khador Drive) or just there (departStation without a camera).</summary>
        enum Entry { Docked, Launch, Arrive, InOrbit }

        struct Place
        {
            public int station, from;   // from: the orbit an arrival came from (Session.PreviousStationIndex)
            public Entry how;
            public static Place Docked(int s) => new Place { station = s, from = s, how = Entry.Docked };
            public static Place Launch(int s) => new Place { station = s, from = s, how = Entry.Launch };
            public static Place Arrive(int s, int from) => new Place { station = s, from = from, how = Entry.Arrive };
            public static Place InOrbit(int s) => new Place { station = s, from = s, how = Entry.InOrbit };
            /// <summary>Into the orbit of 'to' in space: launched when docked there, the same place when already flying there,
            /// else an arrival from here.</summary>
            public Place FlyTo(int to) => to == station ? (how == Entry.Docked ? Launch(to) : this) : Arrive(to, station);
        }

        /// <summary>Mission types completed docked at the mission's station (Status::missionCompleted with ModStation): the
        /// player is still docked there when the next step begins.</summary>
        static readonly HashSet<int> DockedTypes = new HashSet<int>
        {
            StoryType.DockedAny, StoryType.Dock, StoryType.Purchase, StoryType.WeaponAndArmor, StoryType.DeliverOrMount,
            StoryType.Lounge, StoryType.LoungeWithGoods, StoryType.EquipCategory,
        };

        static readonly int[] TutorialHints = { 0x17, 8, 9, 10, 0x1c, 0x15, 0xd, 0x13, 0xe, 0xf, 0x1d, 0x1e, 0x20, 0x21, 0x22, 0x23, 0x24, 0x38 };

        // ---- completion (Status::missionCompleted 0x0b924c) ------------------------------------------------------

        public static int CargoLoad()
        {
            int t = 0;
            foreach (var s in Session.Cargo) t += s.amount;
            return t;
        }

        public static int CargoOf(int item)
        {
            int t = 0;
            foreach (var s in Session.Cargo) if (s.item == item) t += s.amount;
            return t;
        }

        static bool Mounted(Database db, System.Func<ItemData, bool> pred)
        {
            foreach (var e in Session.Equipment) { var it = db.Item(e.item); if (it != null && pred(it)) return true; }
            return false;
        }

        /// <summary>A scene starts (SpaceLevel / StationLevel): a mission still marked won here never had its success
        /// conversation (that runs within one scene and advances the step), so it is opened again and completes anew. #88:
        /// step 72's call completed, the scene changed before its conversation (a docking or jump in the 1.5 s before it
        /// opens) and the save kept the step won: no check reported it again, the Valkyrie story stopped there for good.</summary>
        public static void ReopenUnshownSuccess()
        {
            var m = Mission;
            if (m == null || !m.won || Session.FreePlay) return;
            m.won = false;
            Debug.Log($"Story: step {Index} was won without its success conversation; it completes again.");
        }

        /// <summary>Is the current campaign mission done? (null-safe; a mission already won is not reported again.)</summary>
        public static bool IsComplete(Database db, StoryContext c)
        {
            var m = Mission;
            if (m == null || m.IsEmpty || m.won) return false;
            bool atTarget = c.station == m.station;
            switch (m.type)
            {
                case StoryType.DockedAny:
                case StoryType.Dock: return c.docked && atTarget;
                // Index 143: the Chromo Plasma also counts while it waits at the target as a finished blueprint product
                // (Status+0x1c), docked or in the target's orbit.
                case StoryType.Purchase:
                    if (Index == 143 && atTarget && Session.PendingProducts.Exists(p => p.item == m.goodsItem && p.station == m.station)) return true;
                    return c.docked && atTarget && CargoOf(m.goodsItem) >= m.goodsAmount;
                // Remake co-op (CoopCampaign): the crew's missions since the step began (its value is the count to reach).
                case StoryType.FreelanceCount: return CoopCampaign.Active ? CoopCampaign.CrewMissions >= m.value : Session.FreelanceCompleted >= m.value;
                case StoryType.CargoLoad: return CargoLoad() >= m.value;
                case StoryType.ReachOrbit: return !c.docked && atTarget && c.levelMs >= 10000f;
                case StoryType.WeaponAndArmor:
                    return Mounted(db, it => it.TypeId == 0) && Mounted(db, it => it.categoryId == 10);
                // 0xa0: docked anywhere, or 10 s in space at another orbit than the target (-1 = the alien orbit: index 42
                // completes once out of the Void).
                case StoryType.DelayedCall: return c.docked || (c.levelMs >= 10000f && !atTarget);
                case StoryType.CallAfterLaunch: return !c.docked && c.levelMs > 10000f;
                case StoryType.InOrbit: return !c.docked && atTarget;   // 79 / 152: the alien orbit (-1)
                case StoryType.DeliverOrMount:
                    return c.docked && atTarget && (CargoOf(m.goodsItem) >= m.goodsAmount || Session.Equipment.Exists(e => e.item == m.goodsItem));
                case StoryType.Counter: return Session.StoryCounter >= m.value;
                // 0xa3: every target station of Status+0x90 done (negative), wherever the player is (step 59).
                case StoryType.TargetList: return Session.StoryTargets.Count > 0 && Session.StoryTargets.TrueForAll(t => t < 0);
                case StoryType.ScriptFlag: return m.value == 1;
                case StoryType.Lounge: return c.docked && atTarget && c.inLounge;
                case StoryType.LoungeWithGoods: return c.docked && atTarget && c.inLounge && CargoOf(m.goodsItem) >= m.goodsAmount;
                case StoryType.AmountReached: return m.value >= m.goodsAmount;
                case StoryType.Passengers: return m.value == 0 && Index != 92;   // 92: the level script (the Specter attack) ends it
                case StoryType.EquipCategory: return c.docked && Mounted(db, it => it.categoryId == m.value);
                default: return false;   // level-driven types (0x04, 0x01, 0x06, 0x0a, 0x0c, 0xa1, 0xa3 ...)
            }
        }

        // ---- the level mission and restrictions (Status::departStation, MGame::dockEvent) -------------------------

        static readonly HashSet<int> NotLevelTypes = new HashSet<int> { 0x96, 0x97, 0x99, 0x9b, 0xa1, 0xa2, 0xa3, 0xa4, 0xa6, 0xa7, 0xa9, 0xad, 0x08, 0x0e };

        /// <summary>Status::departStation: does the campaign mission build the orbit of 'station'? (Status+400)</summary>
        public static bool IsLevelMission(int station)
        {
            var m = Mission;
            if (m == null || m.IsEmpty) return false;
            if (m.type == StoryType.DelayedCall) return m.station != station;
            // Rule 2: index < 45, type 0xa1 in the orbit the Void attack (Status+0x80), not the alien orbit.
            if (m.type == StoryType.VoidInvasion)
                return Index < GameWonIndex && station == Session.VoidInvasionStation && station != Session.VoidOrbit;
            return m.station == station && !NotLevelTypes.Contains(m.type);
        }

        /// <summary>Status::departStation's Void-invasion bookkeeping: from index 32 to 44 every 10th departure to a station
        /// that is neither the campaign target nor the attacked one re-rolls the attacked station (a random visible system,
        /// not 10 or 15, then a random station of it); from index 45 on nothing is attacked any more (-10).</summary>
        public static void OnDepart(Database db, int station)
        {
            if (Session.FreePlay || station == Session.VoidOrbit) return;
            if (Index >= GameWonIndex) { Session.VoidInvasionSystem = Session.VoidInvasionStation = -10; return; }
            if (Index < 32 || Index > 44 || station == Mission.station || station == Session.VoidInvasionStation) return;
            if (++Session.InvasionDepartures < 10) return;
            Session.InvasionDepartures = 0;
            GalaxyMap.Visibility(db);
            var systems = new List<int>();
            for (int i = 0; i < db.Systems.Count; i++)
            {
                int s = db.Systems[i].index;
                if (s == 10 || s == 15) continue;
                if (Modding.ModWorld.IsModSystem(s) || Modding.ModWorld.IsMissingSystem(s)) continue;   // the story stays in the original galaxy
                if (Session.SystemVisible != null && s < Session.SystemVisible.Length && !Session.SystemVisible[s]) continue;
                if (db.Systems[i].stations == null || db.Systems[i].stations.Count == 0) continue;
                systems.Add(i);
            }
            if (systems.Count == 0) return;
            var sys = db.Systems[systems[Random.Range(0, systems.Count)]];
            Session.VoidInvasionSystem = sys.index;
            Session.VoidInvasionStation = sys.stations[Random.Range(0, sys.stations.Count)];
        }

        /// <summary>"On a mission" (campaign_flow.md 7): the level mission is not a docking / lounge type, so docking,
        /// planet jumps and the Khador Drive are refused with 525. Indices 49-54 also refuse docking except at Kanado.
        /// MGame::dockEvent reads Status::getMission (Status+400), the level mission Status::departStation picked on
        /// arrival: 'storyOrbit' = this orbit was built as the story's (SpaceLevel.IsStoryOrbit). A step that becomes a
        /// level mission here mid-flight (nextCampaignMission leaves Status+400 alone) doesn't lock the orbit: step 13
        /// won by a Challenge at Kernstal turns into step 14's level at Kernstal.</summary>
        public static bool BlocksDocking(int station, bool storyOrbit)
        {
            if (station == Session.VoidOrbit) return true;   // Level::collideStation: no docking at the Void station
            if (Index >= 49 && Index <= 54 && station != 74) return true;
            return BlocksJumps(station, storyOrbit);
        }

        public static bool BlocksJumps(int station, bool storyOrbit)
        {
            if (!storyOrbit || !IsLevelMission(station)) return false;
            int t = Mission.type;
            return t != 0x00 && t != 0x0b && t != 0x0d && t != 0xab && t != 0xac && t != 0xbd;
        }

        /// <summary>Remake (#84): at 42 in the Void the way out is the wormhole, which shows the mother ship's explosion; its
        /// 0xa0 mission isn't a level mission there (its target is that orbit), so the original's Khador Drive skipped the
        /// ending. The drive is refused with a hint to the wormhole (a player's report took "Not possible on a mission." for
        /// a bug).</summary>
        public static bool EscapesByWormhole(int station) => !Session.FreePlay && Index == 42 && station == Session.VoidOrbit;

        /// <summary>ModStation::OnInitialize menu buttons: Hangar from 5, Map / Missions from 9 (not at 15), Lounge from 12
        /// (not at 15, never at stations 100 / 101).</summary>
        /// <summary>ModStation::OnKeyPress (the menu buttons, 528 "Not available." otherwise): no Hangar in a loaner (48, 49,
        /// 56), no Lounge at 49, no Map at 48 / 49. Remake (#46): no Hangar while any loaner is flown (Session.ParkedShip): the
        /// K'Suukk docks at 54 and the S'Kanarr at 57, where the original opens the success conversation the same frame (the
        /// remake waits a second after the landing), and a quick tap on Hangar sold the loaner's guns or the loaner itself.</summary>
        public static bool HangarUnlocked => Session.FreePlay || (Index >= 5 && Index != 48 && Index != 49 && Index != 56 && Session.ParkedShip == null);
        public static bool MapUnlocked => Session.FreePlay || (Index >= 9 && Index != 15 && Index != 48 && Index != 49);
        public static bool LoungeUnlocked(int station) => station != 100 && station != 101 && (Session.FreePlay || (Index >= 13 && Index != 15 && Index != 49))
                                                          && !GoF2Remake.Modding.ModWorld.NoBar(station);   // remake mods: "bar": "none"
        /// <summary>ModStation::OnKeyPress, the Map at index 77: only in the Cronus Khador left in the hangar (326), else null.</summary>
        public static string MapRefusal => !Session.FreePlay && Index == 77 && Session.ShipIndex != 37 ? Localization.Get(326) : null;
        /// <summary>ModStation::OnInitialize: no autosave while imprisoned on Valkyrie (index 77 at station 101).</summary>
        public static bool AutosaveAllowed(int station) => Session.FreePlay || Index != 77 || station != 101;
        /// <summary>MGame::UseKhadorDrive's story cases (campaign_levels_b.md 78-80): at index 78 (escaping Valkyrie) the drive
        /// is always allowed and misjumps into the alien orbit, advancing the story (-> 79); in the alien orbit at index 80 it
        /// goes to Kothar (Status+0x84 = 100). Null = the normal star map.</summary>
        public static int? ForcedKhadorTarget(int station)
        {
            if (Session.FreePlay) return null;
            if (Index == 78 && station != Session.VoidOrbit) return Session.VoidOrbit;
            if (Index == 80 && station == Session.VoidOrbit) return 100;
            return null;
        }

        /// <summary>ModStation::OnTouchEnd: launching at index 48 goes straight into B'akrram's orbit (58) with the arrival
        /// fly-in (Taret Orskk chauffeurs the player); -1 = a normal launch.</summary>
        public static int LaunchStation => !Session.FreePlay && Index == 48 ? 58 : -1;
        /// <summary>Planet jumps are refused (HUD event 0x15, 525) before index 10 and at 48 (campaign_levels_a.md 1.7).</summary>
        public static bool PlanetJumpsAllowed => Session.FreePlay || (Index >= 10 && Index != 48);
        /// <summary>The autopilot is off at index 0-1 and 48; planet locks from index 2.</summary>
        public static bool AutopilotAllowed => Session.FreePlay || (Index > 1 && Index != 48);

        // ---- advancing (Status::nextCampaignMission 0x0b6c98) ----------------------------------------------------

        /// <summary>Index + 1 with the new step's mission and side effects. Returns the new index.</summary>
        public static int Advance(Database db)
        {
            int from = Index, next = from + 1;
            if (next == 53 || next == 129) next++;   // cases 52 / 128 run twice
            var previous = Session.StoryMission;
            Session.CampaignMission = next;
            Session.StoryStepStart = Session.PlaySeconds;
            if (next == 93 || next == 111 || next == 143) Session.StoryRadioPending = true;
            var step = StoryTable.Step(next);
            // Steps without a creating case (46, 107) keep the old mission object; 45, 84, 128, 130 and 162 get an empty one
            // (Mission(), hidden).
            if (step != null && step.type != -1 || next == GameWonIndex || next == Dlc1WonIndex || next == 128 || next == 130 || next >= LastIndex)
                Session.StoryMission = StoryMission.From(step);
            ApplyStepEffects(db, next, previous);
            CoopCampaign.OnAdvanced(from, next, previous != null && previous.won);   // remake co-op
            return next;
        }

        /// <summary>The side effects of the nextCampaignMission case that creates step 'n' (campaign_flow.md 4; the Valkyrie
        /// cases 0x2f-0x53 read from the decompile). 'previous' = the mission the step replaced.</summary>
        static void ApplyStepEffects(Database db, int n, StoryMission previous)
        {
            switch (n)
            {
                case 4: Session.Cargo.Clear(); break;                                   // Ship::setCargo(null)
                case 6: Session.Cargo.Clear(); break;                                   // Ship::removeAllCargo
                case 8: Session.Unsaleable.Clear(); break;                              // everything saleable again
                case 10:                                                                    // drill -> IMT Extract 1.3
                    for (int i = 0; i < Session.Equipment.Count; i++)
                        if (db.Item(Session.Equipment[i].item)?.categoryId == 19) Session.Equipment[i] = new ItemStack(86, 1);
                    // Remake (#58): Gunant's drill demounted into the hold before his conversation (the original only swaps a
                    // mounted drill) goes back too, the IMT Extract in its place.
                    int drills = Session.Cargo.Where(c => c.item == 90).Sum(c => c.amount);
                    if (drills > 0) { Shop.RemoveFromCargo(90, drills); Shop.AddToCargo(86, drills); }
                    Session.Unsaleable.Remove(90);
                    break;
                // Modified: three freelance missions before the Terran convoy arrives, as players remember the original
                // (Status::nextCampaignMission case 0xc in this binary sets the count + 1).
                case 13: Session.StoryMission.value = (CoopCampaign.Active ? 0 : Session.FreelanceCompleted) + StepThirteenMissions; break;
                case 23: RevealSystem(db, 6); break;                                        // Wolf-Reiser
                case 24: Session.VoidInvasionSystem = 9; Session.VoidInvasionStation = 48; break;
                case 25: Session.Unsaleable.Add(131); break;                            // Alien Remains
                case 26: Session.VoidInvasionSystem = Session.VoidInvasionStation = -1; break;
                case 28: Session.VoidInvasionSystem = 18; Session.VoidInvasionStation = 91; break;
                case 34: Shop.RemoveFromCargo(164, 50); Blueprints.UnlockFromStory(db, n); break;   // the Void Crystals -> the Khador blueprint
                case 58: Blueprints.UnlockFromStory(db, n); RestoreOwnShip(); break;              // Liberator; the own ship back
                case 72:   // case 0x47: the Disruptor blueprint (0xb7) unlocked and a Void Essence (175) in the hold (Ship::addCargo),
                    Blueprints.UnlockFromStory(db, n);   // both in UnlockFromStory (it added a second one here too)
                    break;
                case 104: case 141: Blueprints.UnlockFromStory(db, n); break;                       // Gamma II, Chromo Plasma
                case 42: Session.VoidInvasionSystem = Session.VoidInvasionStation = -10; break;
                case 45:
                    Session.Credits += CoopCampaign.CatchingUp ? 0 : CoopCampaign.Share(40000);   // co-op: shared out (a catch-up: none)
                    Session.VoidInvasionSystem = Session.VoidInvasionStation = -10;
                    break;
                // ---- Valkyrie (cases 0x2f-0x53) ----
                case 48:   // Taret Orskk's H'Soc (race 1) with a D'iol and a Hiroto Proscan; the own ship parked
                    ParkOwnShip();
                    LendShip(9, new ItemStack(58, 1), new ItemStack(83, 1));
                    Session.WingmanContractMs = 0f;   // case 47 sets Status+0x30 = 0: the wingmen leave at the next docking
                    break;
                case 49:   // the stolen K'Suukk: three FlaK 9-9, D'iol, Proscan, H'Belam (the H'Soc stays behind)
                    LendShip(41, new ItemStack(177, 1), new ItemStack(177, 1), new ItemStack(177, 1),
                             new ItemStack(58, 1), new ItemStack(83, 1), new ItemStack(52, 1));
                    break;
                case 55: RevealSystem(db, 23); RestoreOwnShip(); break;                    // Herjaza; the K'Suukk delivered
                case 56:   // the S'Kanarr for the turret test: Skuld AT XR, H'Belam, D'iol, Proscan
                    ParkOwnShip();
                    LendShip(39, new ItemStack(181, 1), new ItemStack(52, 1), new ItemStack(58, 1), new ItemStack(83, 1));
                    break;
                case 59:   // the rival convoys at Suttnar, Ohna and Dekato
                    // The original also calls BluePrint::lock(179) here, but lock (0x1a653c) writes the unlocked flag as
                    // unlock does (movs r1,#1): the Liberator blueprint stays unlocked.
                    Session.StoryTargets = new List<int> { 56, 45, 22 };
                    Session.StoryMission.value = 0;
                    break;
                case 60:   // 50 000 + 50 000 per convoy freighter the Liberator destroyed (Player::damage, weapon 0xb3)
                {
                    int v = previous != null ? previous.value : 0;
                    Session.StoryMission.reward = v * 50000 + 50000;
                    Session.StoryMission.value = v > 0 ? 1 : 0;
                    break;
                }
                case 62: RevealSystem(db, 22); break;                                       // Beidan (Kothar)
                case 63: RevealSystem(db, 24); break;                                       // Skavac
                case 67:   // Cornelius' mines: 5 each of AMR Saber, Neutha EMP and Ksann'k in this station's stock
                    Session.StoryMission.value = 0;
                    AddToCurrentStock(true, new ItemStack(60, 5), new ItemStack(61, 5), new ItemStack(62, 5));   // makeItem(5, 0): free
                    break;
                case 69: Shop.RemoveFromCargo(175, CargoOf(175)); break;                // the Void Essence handed to Netor
                case 75: RemoveCurrentDealerShips(); break;                                 // Station::removeShips
                case 77: SetJumpDriveSaleable(db, false); break;                            // the jump drive can't be sold
                case 78:   // imprisoned: the jump drive taken (mounted, else the one in the hold); Valkyrie's blueprints reset
                {
                    int drive = Session.Equipment.FindIndex(e => db.Item(e.item)?.categoryId == 18);
                    if (drive >= 0) Session.Equipment.RemoveAt(drive);
                    else Shop.RemoveFromCargo(GalaxyMap.KhadorDriveItem, CargoOf(GalaxyMap.KhadorDriveItem));   // Ship::removeCargo(Item*): the whole stack
                    // The original's unsaleable flag was on that Item: it goes with it (the remake's flag is per item
                    // number, so it stayed and locked every later Khador Drive).
                    Session.Unsaleable.Remove(GalaxyMap.KhadorDriveItem);
                    Blueprints.ResetAtStation(db, 101);
                    break;
                }
                case 84:   // dlc1Won: the jump drive saleable again, one more Khador Drive in the hold
                    Session.Unsaleable.Remove(GalaxyMap.KhadorDriveItem);   // SetJumpDriveSaleable skipped it with no drive aboard
                    Shop.AddToCargo(GalaxyMap.KhadorDriveItem, 1);
                    break;
                // ---- Supernova (cases 0x54-0xa1) ----
                case 89:   // the Luxury goods (104) delivered; Ginoya (27) and Talidor (28) on the map
                    Shop.RemoveFromCargo(104, Mathf.Min(10, CargoOf(104)));
                    RevealSystem(db, 27); RevealSystem(db, 28);
                    break;
                case 90: Session.VisitedStations.Remove(109); break;                      // Naneroh un-visited (Galaxy::getVisited)
                case 91: RevealSystem(db, 27); RevealSystem(db, 28); Session.StoryCounter = 0; break;   // 10 miners to rescue
                case 94: Shop.AddToCargo(205, 1); Session.StoryCounter = 0; break;         // Gamma Shield I; 83 to evacuate
                case 98: RevealSystem(db, 29); break;                                       // Paraah
                case 102: Shop.AddToCargo(207, 1); break;                                   // the repair beam; 1700 evacuees
                case 113: Shop.RemoveFromCargo(146, Mathf.Min(1, CargoOf(146))); break;   // the Magnetar Juice drunk
                case 117: RevealSystem(db, 30); break;                                      // Me'enkk
                case 119: if (CargoOf(209) > 0) Session.Unsaleable.Add(209); break;        // the K'mirkk Toad Mutagen kept
                case 122: Session.Unsaleable.Remove(209); Shop.RemoveFromCargo(209, Mathf.Min(1, CargoOf(209))); break;   // handed to Moonsprocket
                case 139: RevealSystem(db, 31); Session.StoryCounter = 0; break;           // Wah'norr; the cargo bots' counter
                case 142:   // Gunant's plasma kit: 15 Ion Lambda Mk1, a Spectral Filter SA-1, a PE Proton collector
                    Shop.AddToCargo(197, 15); Shop.AddToCargo(196, 1); Shop.AddToCargo(198, 1);
                    break;
                case 144: Shop.RemoveFromCargo(210, Mathf.Min(1, CargoOf(210))); break;   // the Chromo Plasma for the array
            }
        }

        // ---- the Valkyrie's loaner ships (Status+0x8c, Status::setShip) ---------------------------------------------

        /// <summary>Status+0x8c = the current ship (its equipment, cargo, mods and damage go with it).</summary>
        static void ParkOwnShip()
        {
            Session.ParkedShip = new ParkedShip
            {
                ship = Session.ShipIndex, equipment = Session.Equipment, cargo = Session.Cargo, mods = Session.ShipMods,
                hull = Session.PlayerHull, armor = Session.PlayerArmor, shield = Session.PlayerShield,
            };
        }

        /// <summary>Status::setShip(Ship::makeShip(ship)) with setEquipment: a fresh hull, empty hold, full health.</summary>
        static void LendShip(int ship, params ItemStack[] equipment)
        {
            Session.ShipIndex = ship;
            Session.Equipment = new List<ItemStack>(equipment);
            Session.Cargo = new List<ItemStack>();
            Session.ShipMods = new List<int>();
            Session.PlayerHull = Session.PlayerArmor = -1;
            Session.PlayerShield = -1f;
            Session.SelectedSecondary = -1;
        }

        /// <summary>setShip(Status+0x8c): the own ship back, the loaner gone.</summary>
        static void RestoreOwnShip()
        {
            var p = Session.ParkedShip;
            if (p == null) return;
            Session.ShipIndex = p.ship;
            Session.Equipment = p.equipment ?? new List<ItemStack>();
            Session.Cargo = p.cargo ?? new List<ItemStack>();
            Session.ShipMods = p.mods ?? new List<int>();
            Session.PlayerHull = p.hull;
            Session.PlayerArmor = p.armor;
            Session.PlayerShield = p.shield;
            Session.SelectedSecondary = -1;
            Session.ParkedShip = null;
        }

        /// <summary>Item::setUnsaleable on the first jump drive (mounted, else in the hold).</summary>
        static void SetJumpDriveSaleable(Database db, bool saleable)
        {
            if (Shop.FirstMounted(db, 18) == null && CargoOf(GalaxyMap.KhadorDriveItem) == 0) return;
            if (saleable) Session.Unsaleable.Remove(GalaxyMap.KhadorDriveItem); else Session.Unsaleable.Add(GalaxyMap.KhadorDriveItem);
        }

        /// <summary>Station::addItem on the station the player is docked at (Status+0x198). 'free': Item::makeItem(amount, 0),
        /// the units cost nothing (StationStock.freeUnits; #131: Cornelius' mines were sold at the full price). Remake: free
        /// also when the station already stocked the item (addItem merges into that row and its price, so there they cost).</summary>
        static void AddToCurrentStock(bool free, params ItemStack[] items)
        {
            var stock = Session.RecentStations.Find(r => r.station == Session.StationIndex);
            if (stock == null) return;
            foreach (var it in items)
            {
                var row = stock.items.Find(x => x.item == it.item);
                if (row != null) row.amount += it.amount; else Shop.InsertStock(stock, new ItemStack(it.item, it.amount));
                if (!free) continue;
                stock.freeUnits ??= new List<ItemStack>();
                var f = stock.freeUnits.Find(x => x != null && x.item == it.item);
                if (f != null) f.amount += it.amount; else stock.freeUnits.Add(new ItemStack(it.item, it.amount));
            }
        }

        static void RemoveCurrentDealerShips()
        {
            var stock = Session.RecentStations.Find(r => r.station == Session.StationIndex);
            stock?.ships.Clear();
            stock?.PruneShipMods();
        }

        /// <summary>Station::addShip on a station's dealer, once.</summary>
        public static void AddDealerShip(int station, int ship)
        {
            var stock = Session.RecentStations.Find(r => r.station == station);
            if (stock != null && !stock.ships.Contains(ship)) stock.ships.Add(ship);
        }

        static void RevealSystem(Database db, int system) => GalaxyMap.Reveal(db, system);

        /// <summary>ModStation::OnInitialize's per-step station tweaks (campaign_flow.md 3.1 6), when docking.</summary>
        public static void OnDocked(Database db, int station, StationStock stock)
        {
            // ModStation::enterStation: the medal streaks of 38 Ore Athlete / 40 Blindfolded Killer end with the flight.
            Session.OreStreak = Session.BlindKills = 0;
            // Index 1: the prologue's Phantom becomes Betty with Gunant's Drill and a Telta Quickscan, both unsaleable.
            if (Index == 1 && Session.ShipIndex != 0) GiveBetty();
            // Index 20 at Kappa: EMP GL I (41) free and 10 more in stock. Only on a docking from space: a load opens the
            // Station scene too, and the saved stock already holds them (each reload added another 10 free bombs).
            if (Index == 20 && station == 55 && stock != null && Session.DockedFromSpace)
            {
                var row = stock.items.Find(s => s.item == 41);
                if (row != null) row.amount += 10; else Shop.InsertStock(stock, new ItemStack(41, 10));
            }
            // Index 27 at the target: the Alien Remains are handed over.
            if (Index == 27 && station == Mission.station) { Session.Unsaleable.Remove(131); Shop.RemoveFromCargo(131, CargoOf(131)); }
            // Kothar (100): Khador's ships. Index 77: the Cronus (price 0); at 80-84 (and after the add-on) the Cronus, the
            // Typhon and the Nemesis.
            if (station == 100 && stock != null)
            {
                if (Index == 77) AddDealerShip(100, 37);
                if (Dlc1Won || (Index >= 80 && Index <= 84)) { AddDealerShip(100, 37); AddDealerShip(100, 38); AddDealerShip(100, 40); }
            }
            if (stock == null) return;
            // ModStation::OnInitialize, every docking: Thynome's VoidX for all gold medals ...
            if (station == 10 && Achievements.GotAllGoldMedals && (stock.ships == null || !stock.ships.Contains(8))) { stock.ships = new List<int> { 8 }; stock.PruneShipMods(); }
            // ... after the game is won, Void Crystals (the Khador Drive blueprint's ingredient) when the player has no drive ...
            if (station == 10 && GameWon && CargoOf(85) == 0 && !Session.Equipment.Exists(e => e.item == 85)
                && !stock.items.Exists(s => s.item == 85 || s.item == 164))
                Shop.InsertStock(stock, new ItemStack(164, 50));
            // ... and energy cells at the deep science stations and the battlestation for a nearly empty hold.
            if ((station == 10 || station == 100 || station == 101) && !stock.items.Exists(s => s.item == GalaxyMap.EnergyCellItem)
                && CargoOf(GalaxyMap.EnergyCellItem) < 6)
                Shop.InsertStock(stock, new ItemStack(GalaxyMap.EnergyCellItem, 10));
        }

        /// <summary>ModStation::OnTouchEnd after a docked success conversation, by the new index 'n' (campaign_flow.md 3.1 3):
        /// Khador's gifts at Kothar (77: the Cronus; 84: the Typhon, the Nemesis and a bottle of S'kolptorr Rum).</summary>
        public static void AfterDockedAdvance(int n, int station)
        {
            if (station != 100) return;
            if (n == 77) { AddDealerShip(100, 37); Session.KhadorCronusGift = true; }   // makeShip(37, 0): the row is free (#122)
            if (n == 84)
            {
                AddDealerShip(100, 38);
                AddDealerShip(100, 40);
                Shop.AddToCargo(137, 1);
            }
        }

        /// <summary>The docked success conversations after which the hangar shows the new (loaner / own) ship.</summary>
        public static bool ShipSwapped(int n) => n == 48 || n == 49 || n == 55 || n == 56 || n == 58;

        /// <summary>The price the shop charges ('price' = the normal one): the tutorial gear at Var Hastra before step 7 and the EMP
        /// bombs at Kappa in step 20 are free.</summary>
        public static int AdjustPrice(int station, int item, int price)
        {
            // Generator::getItemBuyList (shop.md 4.3): Var Hastra before step 7 stocks the tutorial gear 0 / 22 / 55 at price 0
            // (steps 5-6: "go and get yourself a weapon and some armor plating"); selling there pays the same 0.
            if (station == 78 && Session.CampaignMission < 7) return 0;
            return Index == 20 && station == 55 && item == 41 ? 0 : price;
        }

        /// <summary>Modified: the freelance missions step 13 asks for (the original binary: 1).</summary>
        public const int StepThirteenMissions = 3;

        /// <summary>MGame::OnTouchBegin's planet-jump gates for the campaign target (campaign_levels_b.md 3, campaign_levels_c.md
        /// 3.8 / 3.10 / 3.11): the text id of the refusal, -1 = allowed.</summary>
        public static int RequirementRefusal(Database db, int station)
        {
            if (Session.FreePlay || Mission == null || station != Mission.station) return -1;
            switch (Index)
            {
                // Modified: 91 seats all ten survivors (MGame::OnTouchBegin / StarMap::OnTouchEnd for 0x5b: getMaxPassengers
                // <= 9 refuses; a player's report: a Medium Cabin's 5 seats let the step start and it could never finish), 94
                // any cabin (0x5e: getMaxPassengers == 0).
                case 91: return Freelance.MaxPassengers(db) < 10 ? 3214 : -1;                   // passenger cabins for 10
                case 94: return Freelance.MaxPassengers(db) < 1 ? 3214 : -1;                    // passenger cabins
                case 105: return Session.Equipment.Exists(e => e.item == 206) ? -1 : 3217;      // Gamma Shield II
                case 135: return Shop.FirstMounted(db, 19) != null ? -1 : 3213;                 // a mining drill
                case 139:   // the Vol Noor (42), or a Vossk ship with the Vossk Signature (190)
                {
                    // Modified (review of #186): MGame::OnTouchBegin and StarMap::OnTouchEnd let ship 0x2a through without the
                    // signature (0xbe is asked only of the other Vossk ships, DAT_0025454c = race 1); it was asked of it too.
                    int ship = Session.ShipIndex;
                    if (ship == 42) return -1;
                    bool vossk = ship >= 0 && ship < Shop.ShipRace.Length && Shop.ShipRace[ship] == 1;
                    return vossk && Session.Equipment.Exists(e => e.item == 190) ? -1 : 3215;
                }
                case 142:   // a spectral filter, a plasma collector and 15 ionizing missiles; 1 t free cargo
                {
                    int ionizing = 0;
                    foreach (var e in Session.Equipment) if (db.Item(e.item)?.categoryId == 34) ionizing += e.amount;
                    if (Shop.FirstMounted(db, 33) == null || Shop.FirstMounted(db, 35) == null || ionizing < 15) return 3216;
                    return Shop.FreeCargo(db) < 1 ? 3218 : -1;
                }
            }
            return -1;
        }

        /// <summary>The Missions window's objective text for the current step ('#' = the target station).</summary>
        public static string ObjectiveText(Database db)
        {
            var step = Step;
            if (step == null || step.objectiveText < 0) return "";
            var target = db.Stations.Find(s => s.index == Mission.station);
            return Localization.Get(step.objectiveText).Replace("#", target?.name ?? "");
        }
    }
}
