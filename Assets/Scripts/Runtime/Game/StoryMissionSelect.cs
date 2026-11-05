// StoryMissionSelect.cs
// Story: the mission select (the main menu's debug panel) (part of the partial Story; its fields are in Story.cs).

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GoF2Remake.Data
{
    public static partial class Story
    {
        /// <summary>The step the mission select starts for 'index': the steps that are never current on their own start at
        /// the step that plays them. 53 / 129 are skipped (52 -> 54, 128 -> 130); 42 is the end of 41's level (Errkt
        /// finished, the mother ship's explosion, the ride out: MainCampaignLevels builds it only as 41's continuation); 46
        /// is passed straight through by the add-on entry call (45 -> 46 -> 47); 107 has no creating case (106's level
        /// advances twice, to 108); 149 / 150 are only the brokers' dialogue slots (148's lounge at Kalun Amir sets 151).</summary>
        public static int MissionSelectStart(int index) => index switch
        {
            42 => 41, 46 => 47, 53 => 54, 107 => 108, 129 => 130, 149 => 151, 150 => 151, _ => index,
        };
        /// <summary>Where the player is when the step after 'm' begins, by m's type: docked at the station of a docked type;
        /// in space after a call that comes after the launch (and the add-on entry calls, an empty mission); unchanged for the
        /// types done anywhere (freelance count, cargo load, the delayed call, step 59's convoy list); else in the orbit the
        /// level played in (the Void for -1, the attacked station for 0xa1).</summary>
        static Place PlaceAfter(StoryMission m, Place cur)
        {
            if (m == null || m.IsEmpty || m.type == StoryType.CallAfterLaunch) return cur.how == Entry.Docked ? Place.Launch(cur.station) : cur;
            switch (m.type)
            {
                case StoryType.FreelanceCount: case StoryType.CargoLoad: case StoryType.DelayedCall: case StoryType.TargetList: return cur;
                case StoryType.VoidInvasion: return cur.FlyTo(Session.VoidInvasionStation);
            }
            return DockedTypes.Contains(m.type) ? Place.Docked(m.station) : cur.FlyTo(m.station);
        }
        /// <summary>Where the original moves the player as step 'n' begins, when that isn't simply where the previous step was
        /// played (null): the moves after the success conversations (StorySpace.AfterSuccess: MGame::OnTouchEnd; StationMenu:
        /// ModStation::OnTouchEnd's launches into a story orbit without a camera at 144 / 160) and the level scripts' own
        /// (LevelScript: 14 arrested, 42's ride out of the Void, 81's dock at Kothar, 89's at Thynome).</summary>
        static Place? ExitTo(int n, Place cur) => n switch
        {
            15 => Place.Docked(98),                                                  // arrested, taken to Alioth
            22 => Place.Docked(55),                                                  // back into Kappa's station
            43 => Place.Arrive(Session.VoidReturnStation, Session.VoidOrbit),       // the ride out of the Void (Status+0x84)
            60 => Place.Arrive(22, cur.station),                                     // remake pick: the last convoy, at Dekato
            65 => Place.Arrive(100, cur.station),                                    // Khador freed: on to Kothar
            74 => Place.Docked(100),                                                 // the convoy taken: docked at Kothar
            82 => Place.Docked(100),                                                 // Alice stranded: back at Kothar
            90 => Place.Docked(10),                                                  // after the supernova: Thynome
            100 => Place.Docked(120), 110 => Place.Docked(10), 120 => Place.Docked(126), 134 => Place.Docked(112),
            144 => Place.InOrbit(112), 160 => Place.InOrbit(10),
            155 => Place.Arrive(Session.VoidReturnStation, Session.VoidOrbit),      // out of the Void
            162 => Place.Docked(93),
            _ => null,
        };
        /// <summary>The player can be docked at 'station' at step 'n': a real station with a station in its orbit
        /// (Status::inEmptyOrbit), and not one refusing docking at 49-54 (only Kanado takes the K'Suukk).</summary>
        static bool Dockable(int station, int n) =>
            station >= 0 && !World.OrbitLayout.IsEmptyOrbit(station, n) && !(n >= 49 && n <= 54 && station != 74);
        /// <summary>Remake-only (the main menu's mission select): a new game that starts at story step 'target' (through
        /// MissionSelectStart). The step's campaign starts as usual (main game, Valkyrie from 45, Supernova from 84), then
        /// every step up to the target is replayed: what the player does during it (PlayStep), its reward, then Advance (the
        /// side effects of nextCampaignMission: items, ships, revealed systems...), while following where the original's
        /// flow puts the player (PlaceAfter, ExitTo; entering the Void remembers the station as Status+0x84). The start: the
        /// step's story orbit (Status::departStation) when it has one, launched from its station when docked there, else
        /// arriving from where the previous step left the player; otherwise that place itself: docked, or in that orbit when
        /// the previous step ended in space or the station takes no ship (an empty orbit, 49-54). Returns the scene to load.</summary>
        public static string StartAtMission(Database db, int target)
        {
            target = MissionSelectStart(Mathf.Clamp(target, 0, LastIndex));
            var campaign = target >= Dlc1WonIndex ? Campaign.Supernova : target >= GameWonIndex ? Campaign.Valkyrie : Campaign.GalaxyOnFire2;
            Session.Campaign = campaign;
            string scene = StartCampaign(db, campaign);
            if (Index >= target) return scene;
            // The main game starts in the prologue's orbit, the add-ons docked (Dima, Dis).
            var place = campaign == Campaign.GalaxyOnFire2 ? Place.InOrbit(78) : Place.Docked(Session.StationIndex);
            while (Index < target)
            {
                var m = Mission;
                PlayStep(db, Index);
                if (m != null && !m.IsEmpty) Session.Credits += Mathf.Max(0, m.reward);   // the success dialogue's reward
                var next = PlaceAfter(m, place);
                int n = Advance(db);
                next = ExitTo(n, next) ?? next;
                if (next.station == Session.VoidOrbit && next.from >= 0) Session.VoidReturnStation = next.from;   // Status+0x84
                place = next;
                if (Index > target) break;   // 52 / 128 skip a step
            }
            GiveStepRequirements(db);
            Session.PlayerHull = Session.PlayerArmor = -1;
            Session.PlayerShield = -1f;
            // Status::nextCampaignMission flags the chapter call at 93 / 111 / 143; the replay has passed the earlier ones.
            Session.StoryRadioPending = Index == 93 || Index == 111 || Index == 143;

            // Where the step plays: its story orbit (Status::departStation), else where the previous step left the player.
            int n0 = Index;
            int orbit = Mission != null && Mission.type == StoryType.VoidInvasion ? Session.VoidInvasionStation : Mission != null ? Mission.station : -2;
            if (Mission != null && !Mission.IsEmpty && orbit >= -1 && IsLevelMission(orbit))
                place = place.station == orbit && place.how == Entry.Docked && DockedTypes.Contains(Mission.type) ? place : place.FlyTo(orbit);
            // A docked start needs a station that takes the ship, a launch a station to launch from: else in that orbit.
            if ((place.how == Entry.Docked && !Dockable(place.station, n0))
                || (place.how == Entry.Launch && (place.station < 0 || World.OrbitLayout.IsEmptyOrbit(place.station, n0))))
                place = Place.Arrive(place.station, place.from != place.station ? place.from : -1);
            if (place.station == Session.VoidOrbit && place.from >= 0) Session.VoidReturnStation = place.from;
            Session.StationIndex = place.station;
            Session.PreviousStationIndex = place.how == Entry.Arrive ? place.from : -1;
            Session.LaunchedFromStation = place.how == Entry.Launch;
            Session.ArrivedByTravel = place.how == Entry.Arrive;
            // Out of the Void by the wormhole (comingFromAlienWorld): the main game's way back; the add-ons jump or travel.
            Session.ComingFromVoid = place.how == Entry.Arrive && place.from == Session.VoidOrbit && n0 < GameWonIndex;
            return place.how == Entry.Docked ? "Station" : "Space";
        }
        /// <summary>The mission select's replay: what the player does during step 'k' in the original before it completes
        /// (bought, built, picked up, the level scripts' own changes), so the steps after it find it done. The
        /// nextCampaignMission side effects come from Advance.</summary>
        static void PlayStep(Database db, int k)
        {
            switch (k)
            {
                case 1: GiveBetty(); break;                                         // docked after the rescue (ModStation::OnInitialize)
                case 6: EnsureMounted(db, 0, 0); EnsureMounted(db, 10, 55); break; // Var Hastra's free Nirai Impulse EX 1 and E2 Exoclad
                case 8: foreach (int h in TutorialHints) Session.Hints.Add(h); break;   // the tutorial's hints shown
                case 20: EnsureMounted(db, 6, 41, 10); break;                       // Kappa's free EMP GL I (531: none, no launch at 21)
                case 50: Session.Standing[0] = 100; break;                          // LevelScript 0x32: Standing::setStanding(0, 100), the Vossk hostile
                case 58: Shop.AddToCargo(179, 10); break;                           // the 10 Liberators built (kept: 59's kill bonus)
                case 77: TakeShip(db, 37, 100, gift: true); break;                  // Khador's Cronus at Kothar (326: no launch in another ship), free
                case 91: Session.StoryCounter = 10; EnsureMounted(db, 20, 93); break;   // the 10 miners aboard, in a Large Cabin
                case 92: Session.StoryCounter = 0; break;                           // dropped at the transporter
                case 118: Shop.AddToCargo(209, 1); break;                           // the K'mirkk Toad Mutagen bought at Bak S'ondorr
            }
        }
        /// <summary>Mounted if the ship has nothing of that category (else at least 'amount' of it).</summary>
        static void EnsureMounted(Database db, int category, int item, int amount = 1)
        {
            var have = Session.Equipment.Find(e => db.Item(e.item)?.categoryId == category);
            if (have == null) Session.Equipment.Add(new ItemStack(item, amount));
            else if (have.amount < amount) have.amount = amount;
        }
        /// <summary>A dealer ship bought at 'station' like Hangar.BuyShip: the old hull traded in at its price there, every
        /// mounted item to the first free slot of its type, the rest to the hold; mods stay with the old hull.</summary>
        static void TakeShip(Database db, int ship, int station, bool gift = false)
        {
            if (Session.ShipIndex == ship || db.Ship(ship) == null) return;
            // A gift (the Cronus at 77, #122) costs nothing: the old ship's trade-in is credited whole.
            Session.Credits += Mathf.Max(0, Shop.ShipPrice(db, Session.ShipIndex, station) - (gift ? 0 : Shop.ShipPrice(db, ship, station)));
            var slots = db.Ship(ship).slots;
            var mounted = Session.Equipment.Where(e => !e.builtIn).ToList();   // the old hull's built-ins stay with it
            Session.ShipIndex = ship;
            Session.ShipMods = new List<int>();
            Session.Equipment = new List<ItemStack>();
            Session.SelectedSecondary = -1;
            foreach (var e in mounted)
            {
                int type = db.Item(e.item)?.TypeId ?? 4;
                int max = slots == null ? 0 : type switch { 0 => slots.primary, 1 => slots.secondary, 2 => slots.turret, 3 => slots.equipment, _ => 0 };
                if (type == 3) max += Hangar.BaySlot(db, ship, Session.Equipment);
                if (Session.Equipment.FindAll(x => (db.Item(x.item)?.TypeId ?? 4) == type).Count < max || Hangar.BayTakes(db, ship, e.item, Session.Equipment))
                    Session.Equipment.Add(e);
                else if (Hangar.UnitsOf(db, e) > 0) Shop.AddToCargo(e.item, Hangar.UnitsOf(db, e));
            }
            Hangar.SyncBuiltIn(db);
        }
        /// <summary>The debug start: the equipment the step's planet-jump checks ask for (RequirementRefusal, 532), so a jump
        /// straight into a step doesn't leave the player at its target without it (and the scanner / tractor beam of step 24
        /// stay for the salvage steps after it). Added as mounted if the ship has none of that kind.</summary>
        static void GiveStepRequirements(Database db)
        {
            void Ensure(int category, int item, int amount = 1) => EnsureMounted(db, category, item, amount);
            int n = Index;
            if (Session.Campaign == Campaign.GalaxyOnFire2 && n >= 24) { Ensure(17, 81); Ensure(13, 68); }   // Telta Quickscan, AB-1
            if (Session.Campaign != Campaign.Supernova) return;
            if (n >= 91 && n <= 94) Ensure(20, 93);                     // Large Cabin
            if (n >= 105) Ensure(38, 206);                              // Gamma Shield II
            if (n == 135) Ensure(19, 86);                               // IMT Extract 1.3
            if (n == 139)
            {
                bool vossk = Session.ShipIndex == 42 || (Session.ShipIndex < Shop.ShipRace.Length && Shop.ShipRace[Session.ShipIndex] == 1);
                if (!vossk) Session.ShipIndex = 42;
                Ensure(29, 190);                                        // Signature: Vossk
            }
            if (n >= 142) { Ensure(33, 196); Ensure(35, 198); Ensure(34, 197, 15); }   // filter, collector, 15 ionizing missiles
        }
        /// <summary>The mission select's line for step 'index': its objective text (the target's name filled in), else the
        /// step's mission type.</summary>
        public static string StepLabel(Database db, int index)
        {
            var step = StoryTable.Step(index);
            if (step == null) return "";
            if (step.objectiveText >= 0)
            {
                var target = db.Stations.Find(s => s.index == step.station);
                string t = Localization.Get(step.objectiveText).Replace("#", target?.name ?? "");
                if (!string.IsNullOrEmpty(t)) return t;
            }
            var st = db.Stations.Find(s => s.index == step.station);
            return st != null ? st.name : step.station == Session.VoidOrbit ? "Void" : "";
        }
        /// <summary>startValkyrie / startSupernova: the main game's tutorial hints count as shown (8-0xf, 0x13, 0x15, 0x17,
        /// 0x1c-0x1e, 0x20-0x24, 0x38; Supernova also 0x26, 0x31, 0x39), medal 23 bronze and 30 (game won) gold.</summary>
        static void AddonStartHints(bool supernova)
        {
            int[] shown = { 0x17, 8, 9, 10, 0x1c, 0x15, 0xd, 0x13, 0xe, 0xf, 0x1d, 0x1e, 0x20, 0x21, 0x22, 0x23, 0x24, 0x38 };
            foreach (int h in shown) Session.Hints.Add(h);
            if (supernova) { Session.Hints.Add(0x26); Session.Hints.Add(0x31); Session.Hints.Add(0x39); }
            if (Session.Medals == null || Session.Medals.Length != Achievements.Count) Session.Medals = new int[Achievements.Count];
            if (Session.NoMedals) return;   // remake: a game without medals gets none
            Session.Medals[23] = 3;
            Session.Medals[30] = 1;
        }
        /// <summary>GameRecord::load 0x180dc4: a save taken inside an in-space chain restarts at the chain's first step
        /// (25 -> 24, 29 -> 28, 41 -> 39; 35 with another target -> Ga'kkrr). Remake (#49): 79 completes on reaching the Void
        /// (where nothing docks), so a save at 79 is one the old step-78 Khador advance stranded (docked at the Valkyrie
        /// before the jump): back to 78, the escape from the Valkyrie.</summary>
        public static void RepairCheckpoint()
        {
            StoryMission M(int type, int station) => new StoryMission { type = type, station = station };
            switch (Index)
            {
                case 25:
                    Session.CampaignMission = 24; Session.StoryMission = M(0x04, 48);
                    Session.VoidInvasionSystem = 9; Session.VoidInvasionStation = 48;
                    break;
                case 29:
                    Session.CampaignMission = 28; Session.StoryMission = M(0x04, 91);
                    Session.VoidInvasionSystem = 18; Session.VoidInvasionStation = 91;
                    break;
                case 41: Session.CampaignMission = 39; Session.StoryMission = M(0x0b, 30); break;
                case 79: if (!Session.FreePlay) { Session.CampaignMission = 78; Session.StoryMission = M(0x04, 101); } break;
                case 35: if (Mission.station != 29) Session.StoryMission = M(0x0b, 29); break;
            }
            // Remake fix: step 77's unsaleable jump drive is the original's flag on that one Item, which goes with it at 78;
            // kept as the item number it locked every Khador Drive bought after the Valkyrie campaign (saves since then).
            if (Index != 77) Session.Unsaleable.Remove(GalaxyMap.KhadorDriveItem);
        }
    }
}
