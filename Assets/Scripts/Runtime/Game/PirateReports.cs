// PirateReports.cs
// Remake (players' suggestion "Better Pirates", 2026-10; part of the option "Pirate outposts and bosses",
// Settings.PirateEvents): the Missions window's "Pirate activity" board. Instead of only rolling a pirate event at random
// on entering an orbit (TrafficPlan.AddPirateEvent), the authorities report sightings: a pirate outpost or a named pirate
// boss somewhere in a system (the board names the system, never the station: the player still has to search it), with a
// bounty set when it is reported (the event's own, a quarter more: it is a contract), a threat tier (1 Minor .. 5
// Extremely dangerous, RollTier: Dangerous systems lean high; the strength in TrafficPlan.AddPirateEventShips, the bounty
// x0.5 .. x2.5) and a time it stays on the board. The sighting waits at its hidden station: entering that orbit
// spawns it (TrafficPlan), destroying it pays the board's bounty (Traffic.PirateEventDone) and marks it done; it leaves the
// board when it runs out or at the next docking after it was dealt with. With the board the unannounced roll drops to a
// third. Warnings: the station ticker reports the sightings in this system and its gate neighbours (Ticker), and the
// system's authorities radio a warning once per sighting on entering its system (Traffic).
// Multiplayer: one board for the session. The server (the host or a dedicated server) keeps it (ServerTick: the same
// rules, the bounties at rank 10, the times on the session's server clock) and publishes it through NetState
// (pirateBoard, ApplyShared mirrors it into Session.PirateSightings on every game), so the orbit's authority spawns the
// same reported outpost or named boss everyone reads about. Its destruction goes to the server (NetState.PirateSightingDoneRpc,
// checked against the board) and the bounty to the game of whoever killed it (NetState.Grant for another player's kill).
// Nothing of it is saved in a session.
// Boss names are combined from preset lists (an epithet, a first name, a surname in one of several patterns): proper
// names, the same in every language.

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Random = UnityEngine.Random;

namespace GoF2Remake.Data
{
    /// <summary>A reported pirate outpost or boss (saved: Session.PirateSightings).</summary>
    [Serializable]
    public class PirateSighting
    {
        public int id;
        public bool boss;
        public int tier = 3;              // threat 1 Minor .. 5 Extremely dangerous (TierName)
        public int station = -1, system = -1;
        public string name;               // a boss's name (empty for an outpost)
        public int bounty;
        public float reportedAt, expiresAt;   // Session.PlaySeconds
        public bool done;                 // destroyed: off the board at the next docking
        public bool warned;               // its system's authorities radioed the warning
    }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class PirateReports
    {
        /// <summary>Sightings on the board at once: 3, one more per 8 ranks, at most 5.</summary>
        static int Wanted => Mathf.Min(3 + Session.Rank / 8, 5);
        /// <summary>Seconds of playing time a sighting stays reported.</summary>
        const float LifeMin = 40f * 60f, LifeMax = 90f * 60f;
        /// <summary>A board contract pays a quarter more than an unannounced event.</summary>
        const float ContractFactor = 1.25f;

        /// <summary>The board runs: the option on (in a session the session's rule).</summary>
        public static bool Enabled => Settings.PirateEvents;

        static bool InSession => NetSession.Active;

        /// <summary>The board's clock: the playing time, in a session the server's clock (every game reads the same).</summary>
        public static float Now
        {
            get
            {
                double t = InSession ? NetSession.ServerTime : -1;
                return t >= 0 ? (float)t : Session.PlaySeconds;
            }
        }

        /// <summary>A session's warnings already radioed on this game (the shared reports carry no per-player flag).</summary>
        static readonly HashSet<int> warnedHere = new HashSet<int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => warnedHere.Clear();

        /// <summary>The active (not done, not expired) sighting at 'station', null = none.</summary>
        public static PirateSighting At(int station)
        {
            if (!Enabled) return null;
            float now = Now;
            return Session.PirateSightings.Find(s => s.station == station && !s.done && s.expiresAt > now);
        }

        /// <summary>The sightings to list: the active ones first (soonest to run out last), then the ones dealt with.</summary>
        public static List<PirateSighting> Board()
        {
            var list = new List<PirateSighting>();
            if (!Enabled) return list;
            float now = Now;
            foreach (var s in Session.PirateSightings) if (s.done || s.expiresAt > now) list.Add(s);
            list.Sort((a, b) => a.done != b.done ? (a.done ? 1 : -1) : b.expiresAt.CompareTo(a.expiresAt));
            return list;
        }

        public static PirateSighting Find(int id) => id < 0 ? null : Session.PirateSightings.Find(s => s.id == id);

        /// <summary>Destroyed: its bounty (the board's) and off the board at the next docking. -1 = not a reported one.</summary>
        public static int Done(int id)
        {
            var s = Find(id);
            if (s == null || s.done) return -1;
            s.done = true;
            return s.bounty;
        }

        /// <summary>On docking: the old ones off the board, new reports up to the board's size (in a session the server's
        /// work, ServerTick).</summary>
        public static void Refresh(Database db)
        {
            if (!Enabled || InSession) return;
            Fill(db, Session.PirateSightings, Wanted, Session.Rank, Session.StationIndex, false, () => Session.NextPirateSightingId++);
        }

        static void Fill(Database db, List<PirateSighting> board, int want, int rank, int notAt, bool anyRank, Func<int> nextId)
        {
            float now = Now;
            board.RemoveAll(s => s.done || s.expiresAt <= now || !db.Stations.Exists(x => x.index == s.station));   // (a mod's station gone)
            if (board.Count >= want) return;
            var candidates = Candidates(db, notAt, anyRank);
            while (board.Count < want && candidates.Count > 0)
            {
                int k = Random.Range(0, candidates.Count);
                var (station, system) = candidates[k];
                candidates.RemoveAt(k);
                // One per system at a time, so a report always names one place to search.
                if (board.Exists(s => s.system == system.index)) continue;
                board.Add(Make(nextId(), station, system, now, rank));
            }
        }

        static List<(int station, SystemData system)> Candidates(Database db, int notAt, bool anyRank)
        {
            var list = new List<(int, SystemData)>();
            foreach (var st in db.Stations)
            {
                if (st.index == notAt) continue;   // not where the player is docked
                var sys = db.Systems.Find(s => s.index == st.system);
                if (sys == null || !GalaxyMap.IsVisible(db, sys.index)) continue;
                if (!PirateEventRules.Allowed(db, st.index, sys, anyRank)) continue;
                list.Add((st.index, sys));
            }
            return list;
        }

        static PirateSighting Make(int id, int station, SystemData system, float now, int playerRank)
        {
            bool boss = Random.value < 0.5f;
            int rank = Mathf.Clamp(playerRank, 0, 20);
            int tier = RollTier(system.securityLevel, rank);
            return new PirateSighting
            {
                id = id, boss = boss, tier = tier, station = station, system = system.index,
                bounty = Bounty(!boss, rank, tier, true),
                name = boss ? BossName() : "", reportedAt = now, expiresAt = now + Random.Range(LifeMin, LifeMax),
            };
        }

        // ---- threat tiers ----------------------------------------------------------------------------------------

        public const int MaxTier = 5;
        /// <summary>The bounty by tier against the old single event's (outpost 10 000 / boss 4000 + 1500 per rank).</summary>
        static readonly float[] TierBounty = { 0.5f, 0.8f, 1f, 1.5f, 2.5f };
        /// <summary>The tiers' weights in a Risky / Dangerous system (index = tier - 1).</summary>
        static readonly float[] RiskyWeights = { 30f, 30f, 25f, 12f, 3f }, DangerousWeights = { 8f, 17f, 30f, 27f, 18f };

        /// <summary>An event's threat tier: Dangerous systems lean high, Risky ones low; below rank 4 no tier 5 and tier 4 a
        /// third as often, from rank 12 tier 1 half as often and tier 5 half as often again.</summary>
        public static int RollTier(int security, int rank)
        {
            var w = (float[])(security == 0 ? DangerousWeights : RiskyWeights).Clone();
            if (rank < 4) { w[3] /= 3f; w[4] = 0f; }
            else if (rank >= 12) { w[0] /= 2f; w[4] *= 1.5f; }
            float sum = 0f;
            foreach (var x in w) sum += x;
            float r = Random.value * sum;
            for (int i = 0; i < w.Length; i++) { if (r < w[i]) return i + 1; r -= w[i]; }
            return 3;
        }

        /// <summary>An event's bounty: the old single event's by kind and rank, by tier; a board contract a quarter more
        /// (rounded to 100).</summary>
        public static int Bounty(bool outpost, int rank, int tier, bool contract)
        {
            float b = ((outpost ? 10000 : 4000) + 1500 * Mathf.Clamp(rank, 0, 20)) * TierBounty[Mathf.Clamp(tier, 1, MaxTier) - 1];
            if (contract) b *= ContractFactor;
            return Mathf.Max(100, Mathf.RoundToInt(b / 100f) * 100);
        }

        /// <summary>"Minor" .. "Extremely dangerous".</summary>
        public static string TierName(int tier)
        {
            switch (Mathf.Clamp(tier, 1, MaxTier))
            {
                case 1: return Localization.Extra("pirateTier1", "Minor");
                case 2: return Localization.Extra("pirateTier2", "Low");
                case 3: return Localization.Extra("pirateTier3", "Moderate");
                case 4: return Localization.Extra("pirateTier4", "High");
                default: return Localization.Extra("pirateTier5", "Extremely dangerous");
            }
        }

        // ---- warnings ---------------------------------------------------------------------------------------------

        /// <summary>The station ticker's reports: the active sightings in 'system' and its gate neighbours.</summary>
        public static string Ticker(Database db, int system)
        {
            if (!Enabled) return "";
            var sys = db.Systems.Find(s => s.index == system);
            float now = Now;
            var sb = new StringBuilder();
            foreach (var s in Session.PirateSightings)
            {
                if (s.done || s.expiresAt <= now) continue;
                if (s.system != system && (sys == null || !sys.jumpRoutesTo.Contains(s.system))) continue;
                sb.Append(Report(db, s)).Append(NewsTicker.Separator);
            }
            return sb.ToString();
        }

        /// <summary>"Pirate boss X spotted in the Y system. Bounty: N$" / "Pirate outpost reported in the Y system. Bounty: N$".</summary>
        public static string Report(Database db, PirateSighting s)
        {
            string system = SystemName(db, s.system);
            string text = s.boss
                ? string.Format(Localization.Extra("pirateTickerBoss", "Pirate boss {0} spotted in the {1} system. Threat: {3}. Bounty: {2}"), s.name, system, ItemInfoCredits(s.bounty), TierName(s.tier))
                : string.Format(Localization.Extra("pirateTickerOutpost", "Pirate outpost reported in the {0} system. Threat: {2}. Bounty: {1}"), system, ItemInfoCredits(s.bounty), TierName(s.tier));
            return text;
        }

        /// <summary>The authorities' warning on entering a system with an active sighting not warned of yet (it is marked).</summary>
        public static string TakeWarning(Database db, int system)
        {
            if (!Enabled) return null;
            float now = Now;
            var s = Session.PirateSightings.Find(x => x.system == system && !x.done && !x.warned && !warnedHere.Contains(x.id) && x.expiresAt > now);
            if (s == null) return null;
            s.warned = true;
            warnedHere.Add(s.id);
            return s.boss
                ? string.Format(Localization.Extra("pirateWarningBoss", "Attention, pilots: the pirate {0} has been spotted in this system. There is a bounty on their head. Fly carefully."), s.name)
                : Localization.Extra("pirateWarningOutpost", "Attention, pilots: a pirate outpost has been reported in this system. Expect raids and fly carefully.");
        }

        // ---- multiplayer: the session's board -------------------------------------------------------------------

        [Serializable] class Shared { public List<PirateSighting> items = new List<PirateSighting>(); }

        /// <summary>Server: the session's board (not the host player's own Session list, which mirrors it like everyone's).</summary>
        static readonly List<PirateSighting> serverBoard = new List<PirateSighting>();
        static int serverNextId;

        /// <summary>Server, every few seconds: the board kept full and published when it changed. 'publish' writes NetState.</summary>
        public static void ServerTick(Database db, Action<string> publish)
        {
            if (!Enabled)
            {
                if (serverBoard.Count > 0) { serverBoard.Clear(); publish(""); }
                return;
            }
            int before = serverBoard.Count;
            bool changed = serverBoard.RemoveAll(s => s.done || s.expiresAt <= Now) > 0;
            Fill(db, serverBoard, 4, 10, -1, true, () => serverNextId++);   // rank 10's bounties: the session's pilots differ
            if (changed || serverBoard.Count != before) publish(JsonUtility.ToJson(new Shared { items = serverBoard }));
        }

        /// <summary>Server: a session's board starts empty.</summary>
        public static void ServerReset() { serverBoard.Clear(); serverNextId = 0; }

        /// <summary>Server: report 'id' destroyed by a game in its orbit; its bounty (-1 = no such active report).</summary>
        public static int ServerDone(int id, Func<int, bool> inOrbit, Action<string> publish)
        {
            var s = serverBoard.Find(x => x.id == id);
            if (s == null || s.done || s.expiresAt <= Now || !inOrbit(s.station)) return -1;
            s.done = true;
            publish(JsonUtility.ToJson(new Shared { items = serverBoard }));
            return s.bounty;
        }

        /// <summary>Every game: the session's board (NetState) into Session.PirateSightings.</summary>
        public static void ApplyShared(string json)
        {
            var list = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<Shared>(json)?.items;
            Session.PirateSightings = list ?? new List<PirateSighting>();
        }

        public static string SystemName(Database db, int system) => db.Systems.Find(x => x.index == system)?.name ?? "?";

        static string ItemInfoCredits(long n) => GameNames.Credits(n);

        // ---- boss names ------------------------------------------------------------------------------------------

        static readonly string[] Epithets =
        {
            "Red", "Black", "Grim", "Mad", "Iron", "Silent", "Bloody", "Lucky", "Crazy", "Cold", "Rusty", "Scarred", "One-Eyed",
            "Dead-Eye", "Butcher", "Ghost", "Viper", "Jackal", "Raven", "Wolf", "Shark", "Cobra", "Hound", "Vulture", "Hammer",
            "Razor", "Blade", "Fang", "Claw", "Spike", "Brick", "Smoke", "Ash", "Ember", "Frost", "Storm", "Thunder", "Void",
            "Dusty", "Greasy", "Slick", "Twitch", "Patch", "Hook", "Tusk", "Skull", "Bones", "Grinder", "Wrecker", "Scrapper",
            "Gutter", "Cutthroat", "Smiling", "Laughing", "Gentle", "Polite", "Old", "Young", "Big", "Little", "Long", "Quick",
            "Nine-Lives", "Two-Guns", "Half-Moon", "Starless", "Hollow", "Crooked", "Golden", "Copper", "Neon", "Static",
        };

        static readonly string[] FirstNames =
        {
            "Jax", "Vex", "Kara", "Dren", "Morg", "Silas", "Rook", "Talon", "Vira", "Korr", "Brann", "Zed", "Nyx", "Grell", "Hask",
            "Mira", "Orin", "Drax", "Sable", "Kiro", "Lena", "Rhett", "Ivo", "Tamsin", "Bram", "Cass", "Dagny", "Ezra", "Fenn",
            "Greta", "Hugo", "Ilsa", "Jory", "Kai", "Lyra", "Magnus", "Nell", "Otto", "Petra", "Quinn", "Rosa", "Sven", "Tilda",
            "Ulrik", "Vesna", "Wade", "Xander", "Yara", "Zora", "Anton", "Bex", "Corvin", "Dash", "Elko", "Faro", "Gideon", "Hale",
            "Isko", "Juno", "Kestrel", "Lazlo", "Marek", "Nadia", "Odo", "Pike", "Ragna", "Soren", "Tova", "Ursa", "Vance", "Wren",
            "Yuri", "Zane", "Arlo", "Brix", "Cyra", "Delmar", "Esk", "Flint", "Gage", "Hollis", "Inka", "Jett", "Kael", "Lux",
            "Mags", "Niko", "Oskar", "Pell", "Remy", "Saska", "Thane",
        };

        static readonly string[] Surnames =
        {
            "Voss", "Kade", "Marrow", "Blackwood", "Crane", "Steele", "Rask", "Holt", "Vane", "Draven", "Korsk", "Quill", "Mercer",
            "Graves", "Thorne", "Slade", "Varga", "Kell", "Ruin", "Hollow", "Ashby", "Bleak", "Cinder", "Dunmore", "Ebon", "Falk",
            "Gorr", "Harrow", "Irons", "Jaeger", "Krell", "Lomax", "Mordaunt", "Nox", "Orlov", "Penrose", "Quarry", "Rourke",
            "Sorrow", "Tallis", "Umber", "Vantor", "Wexley", "Yorke", "Zarek", "Abbot", "Brask", "Corso", "Dravik", "Esterly",
            "Fenwick", "Galloway", "Hask", "Ingram", "Jarrow", "Kovac", "Larkin", "Mallory", "Norquist", "Oduya", "Pryce",
            "Ravel", "Strand", "Toller", "Ulven", "Valko", "Whitlock", "Xerxes", "Yancy", "Zoltan", "Barrow", "Cutter", "Dagger",
            "Grimsby", "Hatch", "Kettle", "Lockjaw", "Mudd", "Pitch", "Rattigan", "Scorch", "Tarrant", "Vickers", "Wolfe",
            "Brandt", "Castellan", "Drummond", "Faraday", "Kowalski", "Okafor", "Tanaka",
        };

        /// <summary>A pirate boss's name: an epithet, a first name and a surname combined in one of several patterns.</summary>
        public static string BossName()
        {
            string e = Pick(Epithets), f = Pick(FirstNames), l = Pick(Surnames);
            switch (Random.Range(0, 7))
            {
                case 0: return $"{e} {f}";               // Red Vex
                case 1: return $"{e} {l}";               // Grim Holt
                case 2: return $"{f} \"{e}\" {l}";       // Kara "Dead-Eye" Thorne
                case 3: return $"\"{e}\" {f}";             // "Butcher" Brann
                case 4: return $"{e} {f} {l}";           // Iron Silas Marrow
                case 5: return $"{f} {l}";               // Zora Kovac
                default: return $"\"{e}\" {l}";          // "Smoke" Kettle
            }
        }

        static string Pick(string[] list) => list[Random.Range(0, list.Length)];
    }
}
