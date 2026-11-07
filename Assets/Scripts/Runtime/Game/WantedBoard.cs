// WantedBoard.cs
// The Most Wanted boards of the Supernova add-on (Reference/research/wingmen_wanted.md 2): 25 criminals (wanted.json) on
// four race boards (0 Terran, 1 Vossk, 2 Nivelian, 3 Midorian). Plain C#; the state is Session.Wanted / CollectedBounties.
//   Status::wantedBoardAccessible 0xba1f8  a board of the station's system race has an entry with requiredMission &lt;= the
//                                          campaign, not in the alien orbit, not at Kaamo (108) -> the Missions window's
//                                          "Most Wanted" tab (3219)
//   WantedWindow::init 0xf4b4c             the list: the station race's board, Terran from campaign 128, the others from 162
//   Status::activateNewWanted 0xba2ac      on docking (and at step 128): entries of the station's board with their campaign
//                                          step reached (the storyline 0 / 1 only at exactly 128 / 130) and enough bounties
//                                          on the board become active on a random gate route (2-4 systems for the storyline,
//                                          else (i - 1) % 6 / 3 + 2 .. (i - 1) % 6 / 2 + 4) between visible systems outside
//                                          6, 25, 26, 27, 28; "Departed from" = the route's start, "Travelling to" = its end,
//                                          the criminal somewhere on it
//   Status::moveWanted 0xb6858             every real orbit change (not launching from the docked station, not to / from the
//                                          Void): a criminal in the orbit being entered or at the programmed station stays;
//                                          one that had arrived picks a new trip; else it moves one system along the route
//                                          (to that system's jumpgate station), into the destination on the last step
//   killing it                             whoever kills it: the bounty (Layout::showMissionRewardMessage, 3206), +1 on its
//                                          board, terminated; the storyline 0 / 1 surrender at a third of their hull instead,
//                                          which wins campaign 128 / 130
//   Generator::getShipBuyList 0xa0eb8      Quineros (107) sells ship 45 / 46 / 47 / 48 once wanted 6 / 12 / 18 / 24 is dead (3232)
// Movement uses UnityEngine.Random (the original's generator is time-seeded).
// Remake mods (Modding.ModWanted, wanted.json): their criminals follow db.Wanted's 25 with a key; their state is by that key
// (Session.ModWanted), they are listed while their "available" holds (free play too) at the stations of their "systems",
// else of their board's race, become active once enough of that board's mod criminals are dead (Session.ModBounties), and
// travel "route" systems between stations of any usable system, the mods' too (or of their "systems").

using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace GoF2Remake.Data
{
    /// <summary>A criminal's state (Wanted +0x40 .. +0x4d).</summary>
    [Serializable]
    public class WantedState
    {
        public bool active, terminated;
        public int current = -1, travelsTo = -1, lastSeen = -1;
    }

    /// <summary>A mod criminal's state in a save (by its key: Session.ModWanted).</summary>
    [Serializable]
    public class ModWantedSave
    {
        public string key;
        public WantedState state;

        public static List<ModWantedSave> From(Dictionary<string, WantedState> d)
        {
            var list = new List<ModWantedSave>();
            foreach (var kv in d) list.Add(new ModWantedSave { key = kv.Key, state = kv.Value });
            return list;
        }

        public static Dictionary<string, WantedState> To(List<ModWantedSave> list)
        {
            var d = new Dictionary<string, WantedState>();
            if (list != null) foreach (var e in list) if (!string.IsNullOrEmpty(e?.key) && e.state != null) d[e.key] = e.state;
            return d;
        }
    }

    public static class WantedBoard
    {
        public const int StorylineFirst = 128, StorylineSecond = 130, AllBoards = 162;
        static readonly int[] Excluded = { 6, 25, 26, 27, 28 };

        /// <summary>The game's entries (they come first; a mod's follow with a key).</summary>
        static int Originals(Database db)
        {
            int n = 0;
            while (n < db.Wanted.Count && !db.Wanted[n].IsMod) n++;
            return n;
        }

        static void Ensure(Database db)
        {
            int n = Originals(db);
            while (Session.Wanted.Count < n) Session.Wanted.Add(new WantedState());
        }

        public static WantedState State(Database db, int index)
        {
            if (index < 0 || index >= db.Wanted.Count) return null;
            var w = db.Wanted[index];
            if (w.IsMod)
            {
                if (!Session.ModWanted.TryGetValue(w.key, out var s)) Session.ModWanted[w.key] = s = new WantedState();
                return s;
            }
            Ensure(db);
            return index < Session.Wanted.Count ? Session.Wanted[index] : null;
        }

        static bool Off(int station) => station == Session.VoidOrbit || station == 108;

        /// <summary>Remake mods: a mod criminal is on this station's board now (its systems', else its board race's; its
        /// "available" holding; free play too).</summary>
        public static bool ModListed(Database db, WantedData w, int station)
        {
            if (w == null || !w.IsMod || Off(station)) return false;
            var sys = SystemOf(db, station);
            if (sys == null) return false;
            bool here = w.systems != null ? Array.IndexOf(w.systems, sys.index) >= 0 : sys.raceId == w.board;
            return here && Modding.ModUnlocks.Holds(w.mod, w.available, w.availableWhere);
        }

        /// <summary>The game's criminals count now (the Supernova add-on's: never in free play).</summary>
        static bool OriginalsOn => !Session.FreePlay;

        static int SystemRace(Database db, int station)
        {
            var st = db.Stations.Find(s => s.index == station);
            var sys = st != null ? db.Systems.Find(s => s.index == st.system) : null;
            return sys != null ? sys.raceId : -1;
        }

        /// <summary>Status::isStorylineWanted: entries 0 and 1 (Pal Tyyrt, Kehnor).</summary>
        public static bool IsStoryline(int index) => index == 0 || index == 1;

        /// <summary>Status::wantedBoardAccessible.</summary>
        public static bool Accessible(Database db, int station)
        {
            if (Off(station)) return false;
            int race = SystemRace(db, station);
            return db.Wanted.Exists(w => w.IsMod ? ModListed(db, w, station) : OriginalsOn && w.board == race && w.requiredMission <= Session.CampaignMission);
        }

        /// <summary>WantedWindow::init: the rows of the station's board.</summary>
        public static List<WantedData> ListFor(Database db, int station)
        {
            int race = SystemRace(db, station);
            int campaign = Session.CampaignMission;
            var list = new List<WantedData>();
            foreach (var w in db.Wanted)
                if (w.IsMod ? ModListed(db, w, station) : OriginalsOn && w.board == race && (w.board == 0 ? campaign >= StorylineFirst : campaign >= AllBoards)) list.Add(w);
            return list;
        }

        /// <summary>The storyline row the board marks (entry 0 at 128, entry 1 at 130), -1 = none.</summary>
        public static int StorylineRow => Session.CampaignMission == StorylineFirst ? 0 : Session.CampaignMission == StorylineSecond ? 1 : -1;

        static (int lo, int hi) PathBounds(Database db, int i) => db.Wanted[i].IsMod ? (db.Wanted[i].routeMin, db.Wanted[i].routeMax)
            : IsStoryline(i) ? (2, 4) : ((i - 1) % 6 / 3 + 2, (i - 1) % 6 / 2 + 4);

        static bool Usable(Database db, SystemData sys, bool[] visible) =>
            sys != null && sys.jumpRoutesTo != null && sys.jumpRoutesTo.Count > 0 && sys.index < visible.Length && visible[sys.index]
            && Array.IndexOf(Excluded, sys.index) < 0 && sys.stations != null && sys.stations.Count > 0;

        static SystemData SystemOf(Database db, int station)
        {
            var st = db.Stations.Find(s => s.index == station);
            return st != null ? db.Systems.Find(s => s.index == st.system) : null;
        }

        /// <summary>Globals::getRandomStation with the route rules: a random station (nextInt(135)) of a usable system.</summary>
        /// <summary>Remake mods: a random station of a usable system of the criminal's "systems", else of any (the mods' too).</summary>
        static int RandomModStation(Database db, bool[] visible, WantedData w, int notSystem)
        {
            var pool = new List<SystemData>();
            foreach (var sys in db.Systems)
                if (sys.index != notSystem && Usable(db, sys, visible) && (w.systems == null || Array.IndexOf(w.systems, sys.index) >= 0)) pool.Add(sys);
            if (pool.Count == 0) return -1;
            var pick = pool[Random.Range(0, pool.Count)];
            return pick.stations[Random.Range(0, pick.stations.Count)];
        }

        static int RandomStation(Database db, bool[] visible, int notSystem, WantedData w)
            => w != null && w.IsMod ? RandomModStation(db, visible, w, notSystem) : RandomStation(db, visible, notSystem);

        static int RandomStation(Database db, bool[] visible, int notSystem)
        {
            for (int guard = 0; guard < 2000; guard++)
            {
                int s = Random.Range(0, 135);
                var sys = SystemOf(db, s);
                if (sys != null && sys.index != notSystem && Usable(db, sys, visible)) return s;
            }
            return -1;
        }

        /// <summary>Status::activateNewWanted: how many became active at this station.</summary>
        public static int ActivateNew(Database db, int station)
        {
            if (Off(station)) return 0;
            Ensure(db);
            int race = SystemRace(db, station);
            int campaign = Session.CampaignMission;
            var visible = GalaxyMap.Visibility(db);
            int count = 0;
            for (int i = 0; i < db.Wanted.Count; i++)
            {
                var w = db.Wanted[i];
                var st = State(db, i);
                if (w.IsMod)
                {
                    if (!ModListed(db, w, station) || st.active || st.terminated || w.requiredBounties > Session.ModBounties[Mathf.Clamp(w.board, 0, 3)]) continue;
                }
                else
                {
                    if (!OriginalsOn || w.board != race || w.requiredMission > campaign || (IsStoryline(i) && campaign > w.requiredMission)) continue;
                    // Remake: a save whose storyline criminal died without giving up (before Traffic.WantedKilled won the
                    // step on that blow too) waits at 128 / 130 for a dead man: he is back on his route.
                    if (IsStoryline(i) && st.terminated) st.terminated = false;
                    if (st.active || st.terminated || w.requiredBounties > Session.CollectedBounties[Mathf.Clamp(w.board, 0, 3)]) continue;
                }
                var (lo, hi) = PathBounds(db, i);
                List<int> path = null;
                int s1 = -1, s2 = -1;
                for (int guard = 0; guard < 500 && path == null; guard++)
                {
                    s1 = RandomStation(db, visible, -1, w);
                    if (s1 < 0) break;
                    s2 = RandomStation(db, visible, SystemOf(db, s1).index, w);
                    if (s2 < 0) break;
                    var p = GalaxyMap.SystemPath(db, SystemOf(db, s1).index, SystemOf(db, s2).index);
                    if (p != null && p.Count >= lo && p.Count <= hi) path = p;
                }
                if (path == null) continue;
                st.active = true;
                st.lastSeen = s1;
                st.travelsTo = s2;
                var sys = db.Systems.Find(x => x.index == path[Random.Range(0, path.Count)]);
                st.current = sys != null && sys.stations.Count > 0 ? sys.stations[Random.Range(0, sys.stations.Count)] : s1;
                count++;
            }
            return count;
        }

        /// <summary>Status::moveWanted, on entering 'entering' (the orbit change is real: see the header).</summary>
        public static void Move(Database db, int entering, int programmed)
        {
            if (db.Wanted.Count == 0) return;
            Ensure(db);
            var visible = GalaxyMap.Visibility(db);
            for (int i = 0; i < db.Wanted.Count; i++)
            {
                if (!db.Wanted[i].IsMod && !OriginalsOn) continue;
                var st = State(db, i);
                if (st == null) continue;
                if (!st.active || st.terminated || st.current < 0) continue;
                if (st.current == entering || st.current == programmed) continue;   // you find it there
                var curSys = SystemOf(db, st.current);
                var toSys = SystemOf(db, st.travelsTo);
                if (curSys == null) continue;
                if (st.current == st.travelsTo || toSys == null)
                {
                    // Arrived last time: a new trip (the criminal stays put this step).
                    st.lastSeen = st.current;
                    var (lo, hi) = PathBounds(db, i);
                    for (int guard = 0; guard < 500; guard++)
                    {
                        int s = RandomStation(db, visible, curSys.index, db.Wanted[i]);
                        if (s < 0) break;
                        var p = GalaxyMap.SystemPath(db, curSys.index, SystemOf(db, s).index);
                        if (p != null && p.Count >= lo && p.Count <= hi) { st.travelsTo = s; break; }
                    }
                }
                else if (curSys.index == toSys.index) st.current = st.travelsTo;
                else
                {
                    var p = GalaxyMap.SystemPath(db, curSys.index, toSys.index);
                    var next = p != null && p.Count > 1 ? db.Systems.Find(x => x.index == p[1]) : null;
                    if (next != null && next.jumpgateStation >= 0) st.current = next.jumpgateStation;
                }
            }
        }

        /// <summary>Status::getWantedInCurrentOrbit 0xb916c: an active criminal at this station (the lowest requiredBounties).</summary>
        public static WantedData InOrbit(Database db, int station)
        {
            if (db.Wanted.Count == 0) return null;
            Ensure(db);
            WantedData best = null;
            for (int i = 0; i < db.Wanted.Count; i++)
            {
                if (!db.Wanted[i].IsMod && !OriginalsOn) continue;
                var st = State(db, i);
                if (st == null || !st.active || st.terminated || st.current != station) continue;
                if (best == null || db.Wanted[i].requiredBounties < best.requiredBounties) best = db.Wanted[i];
            }
            return best;
        }

        /// <summary>PlayerFighter::update's death branch: terminated, +1 on the board, the bounty paid. Returns the bounty.</summary>
        public static int OnKilled(Database db, int index)
        {
            var st = State(db, index);
            if (st == null || st.terminated || index >= db.Wanted.Count) return 0;
            var w = db.Wanted[index];
            st.terminated = true;
            st.active = false;
            if (w.IsMod) Session.ModBounties[Mathf.Clamp(w.board, 0, 3)]++;   // remake mods: their own tally
            else Session.CollectedBounties[Mathf.Clamp(w.board, 0, 3)]++;
            Session.Credits += w.reward;
            return w.reward;
        }

        /// <summary>Level::almostKillWanted for the storyline 0 / 1: inactive (not terminated, no bounty), and the campaign
        /// mission won in this orbit (Mission(4, 0, station) marked won: here a script flag the success check picks up).</summary>
        public static void OnSurrender(Database db, int index, int station)
        {
            var st = State(db, index);
            if (st != null) st.active = false;
            int expected = index == 0 ? StorylineFirst : StorylineSecond;
            if (Session.FreePlay || Session.CampaignMission != expected) return;
            Session.StoryMission = new StoryMission { type = StoryType.ScriptFlag, station = station, value = 1, visible = false };
        }

        /// <summary>Generator::getShipBuyList for Quineros: the dead board bosses' ships (45 / 46 / 47 / 48 for 6 / 12 / 18 / 24).</summary>
        public static List<int> QuinerosShips(Database db)
        {
            var list = new List<int>();
            int[] bosses = { 6, 12, 18, 24 };
            for (int k = 0; k < bosses.Length; k++)
            {
                var st = State(db, bosses[k]);
                if (st != null && st.terminated) list.Add(45 + k);
            }
            return list;
        }
    }
}
