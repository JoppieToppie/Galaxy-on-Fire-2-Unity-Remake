// CruiseEncounters.cs
// Remake: what a cruise drive's flight runs into (CruiseDrive; docs/notes/navigation.md "Cruise drive"), as plain rules.
// While the drive cruises, every CheckIntervalMs a roll of ChancePercent picks one entry of the table by weight among
// those the orbit allows (rank, the system's security level, free play, a quest hook); nothing in the first MinCruiseMs
// of a cruise, nothing for CooldownMs after an encounter, at most MaxPerOrbit per orbit visit. An entry is either an
// ambush (the encounter appears ahead and the drive drops: Stop.Encounter) or a visible signal (a waypoint the pilot may
// fly to or ignore; it spawns on arrival, NavigationCruiseDrive). The built-in table: pirates (an ambush), a trade convoy,
// a debris field, a distress call (signals), a flyby (an ambush that doesn't stop the drive). Mods add entries and
// change the built-ins' weights (Modding.ModEncounters, encounters.json). Modelled on No Man's Sky's pulse encounters
// (the weighted table with conditions, the interruption) and EVERSPACE 2's signals (the drop-in you may ignore).

using System;
using System.Collections.Generic;

namespace GoF2Remake.Flight
{
    public class CruiseEncounters
    {
        public const string Pirates = "pirates", Trader = "trader", Derelict = "derelict", Distress = "distress", Flyby = "flyby", Quest = "quest";
        public static readonly string[] Kinds = { Pirates, Trader, Derelict, Distress, Flyby, Quest };

        public class Entry
        {
            public string id, kind;
            public int weight = 10;
            /// <summary>A signal the pilot may fly to (a waypoint) instead of an ambush that appears at once.</summary>
            public bool visible;
            public int minRank;
            /// <summary>The system security levels it happens in (0 Dangerous .. 3 Secure), null = any.</summary>
            public int[] security;
            public bool freePlayOnly;
            /// <summary>Kind "quest": the quest graph (single player) started when it comes.</summary>
            public string quest;
            /// <summary>How many ships (pirates, escorts, the flyby's wing), -1 = the kind's own count by rank.</summary>
            public int ships = -1;
            /// <summary>The signal's name on the HUD (null = the kind's).</summary>
            public string name;
            public bool StopsTheDrive => kind != Flyby && kind != Quest;
        }

        public const float CheckIntervalMs = 5000f, MinCruiseMs = 6000f, CooldownMs = 90000f;
        public const int ChancePercent = 10, MaxPerOrbit = 2;

        /// <summary>The game's own table (ids a mod may override: weight, visible, minRank, security).</summary>
        public static List<Entry> BuiltIn() => new List<Entry>
        {
            new Entry { id = Pirates, kind = Pirates, weight = 30, security = new[] { 0, 1, 2 } },
            new Entry { id = Trader, kind = Trader, weight = 20, visible = true },
            new Entry { id = Derelict, kind = Derelict, weight = 20, visible = true },
            new Entry { id = Distress, kind = Distress, weight = 20, visible = true, minRank = 2, security = new[] { 0, 1, 2 } },
            new Entry { id = Flyby, kind = Flyby, weight = 10 },
        };

        readonly List<Entry> table;
        float cruiseMs, sinceCheckMs, cooldownMs;
        public int Count { get; private set; }

        public CruiseEncounters(List<Entry> table) => this.table = table ?? BuiltIn();

        public IReadOnlyList<Entry> Table => table;

        /// <summary>'allowed' says whether an entry may come in this orbit now (CanHappen does the entry's own conditions);
        /// 'random' gives 0..1. Returns the encounter that comes this frame, null for none.</summary>
        public Entry Update(float dtMs, bool cruising, Func<Entry, bool> allowed, Func<float> random)
        {
            dtMs = Math.Max(0f, dtMs);
            if (cooldownMs > 0f) cooldownMs = Math.Max(0f, cooldownMs - dtMs);
            if (!cruising) { cruiseMs = 0f; sinceCheckMs = 0f; return null; }
            float before = cruiseMs;
            cruiseMs += dtMs;
            if (cruiseMs < MinCruiseMs || cooldownMs > 0f || Count >= MaxPerOrbit) return null;
            sinceCheckMs += cruiseMs - Math.Max(before, MinCruiseMs);   // only the time past the first MinCruiseMs counts
            if (sinceCheckMs < CheckIntervalMs) return null;
            sinceCheckMs -= CheckIntervalMs;
            if (random() * 100f >= ChancePercent) return null;
            var e = Pick(table, allowed, random());
            if (e == null) return null;
            Count++;
            cooldownMs = CooldownMs;
            return e;
        }

        /// <summary>An entry's own conditions against the orbit: the rank, the system's security level, free play.</summary>
        public static bool CanHappen(Entry e, int rank, int security, bool freePlay)
        {
            if (e == null || e.weight <= 0 || rank < e.minRank || (e.freePlayOnly && !freePlay)) return false;
            if (e.security != null && e.security.Length > 0 && Array.IndexOf(e.security, security) < 0) return false;
            return true;
        }

        /// <summary>One of 'table' by weight among those 'allowed' accepts ('r' 0..1), null when none may come.</summary>
        public static Entry Pick(IReadOnlyList<Entry> table, Func<Entry, bool> allowed, float r)
        {
            int total = 0;
            foreach (var e in table) if (e != null && e.weight > 0 && (allowed == null || allowed(e))) total += e.weight;
            if (total <= 0) return null;
            float at = Math.Min(Math.Max(r, 0f), 0.999999f) * total;
            foreach (var e in table)
            {
                if (e == null || e.weight <= 0 || (allowed != null && !allowed(e))) continue;
                if (at < e.weight) return e;
                at -= e.weight;
            }
            return null;
        }
    }
}
