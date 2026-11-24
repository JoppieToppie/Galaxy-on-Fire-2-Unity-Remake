// NetFactions.cs
// Remake-only: factions, the lasting player groups of a dedicated server with profiles (NetProfiles). A squad (NetSquad)
// stays the session's quick group for flying together (shared missions, no friendly fire); a faction is kept between
// sessions by profile and is what will hold territory (home station, claims, bank: later phases).
//   A faction: a name (MaxNameLength), a tag of 2-4 letters / digits shown before its members' names ("[TAG] Name": the
//     lock plate, chat), a leader, officers and members (profile ids), a bank (credits, for the later phases) and a home
//     station (-1 = none yet).
//   Chat commands (answered to the sender only): /faction create TAG Name, /faction invite <pilot>, /faction join TAG,
//     /faction leave, /faction kick <pilot>, /faction promote / demote <pilot>, /faction leader <pilot>, /faction disband,
//     /faction info [TAG], /faction list; "/f <text>" talks to the faction members online.
//   Ranks: the leader does everything; officers invite and kick members; members talk. The leader leaves only by
//     handing over (/faction leader) or as the last member (the faction ends).
//   Storage: factions.json beside the profiles (NetProfiles.Folder), written like them (through .tmp, the old one as .bak).
//   A profile deleted (the console, a /link that replaces it) leaves its faction; a leaderless faction passes to its first
//   officer, else its first member.
// Phase 2, territory:
//   Bank: "/faction deposit N" takes N credits from the player (ChargeRpc: their game pays if it can and answers) into the
//     bank; "/faction withdraw N" (officers) pays N out to the player (GrantRpc). Both move the profile's worth with them
//     (NetProfiles.AdjustWorth), so the upload check neither trips on a withdrawal nor lets a deposit that wasn't paid
//     go unnoticed for long (the client still runs its credits: a modified game can lie about paying).
//   Claims: "/faction claim" (officers, docked at the station) claims it for ClaimCost from the bank, at most MaxClaims per
//     faction; not the Kaamo Club (108) or Loma (system 25). The first claim is the home; "/faction home" (officers, docked at
//     another claim) moves it; "/faction unclaim" (officers, docked there) gives it up. A claim no member has docked at for
//     LapseDays lapses. The claims reach every player (NetState.Claims: "station|TAG|Name|VAT" lines; NetFactionsClient), who
//     see the owner on the star map, the station's header and the orbit information.
//   Home: a member signing in starts docked at the faction's home, and a destroyed member respawns there.
// Phase 3, contest and benefits:
//   Siege: "/faction siege" (officers of another faction, in the orbit or docked there; SiegeCost from the bank as the
//     stake; one at a time; the faction needs room for another claim; not within ProtectionHours of the station's last
//     siege; not the defenders' home while they hold another station) is announced and armed SiegeDelaySeconds later.
//     Armed, it waits until pilots of both factions are alive in the orbit's space at once (real players only: NPCs never
//     start it). Meanwhile:
//       the station defenders escalate (NetOrbit.UpdateSiegeDefenders): DefenderCount / DefenderLevel by the
//         DefenderStepSeconds steps since the arming, frozen when the fight starts;
//       the claim drains: a defending pilot docking there (not a respawn after dying in the fight) halves its remaining
//         lapse days (rounded up) while more than LapseFloorDays are left, never resets it (HalveLapse); a claim lapsing
//         now goes to the attacker (the stake back);
//       the standoff clock: a side present while the other side has a pilot online elsewhere adds to the absent side's
//         standoff; StandoffSeconds of it = that side forfeits;
//       no fight by expiresAt (SiegeMeetSeconds from the arming) = one more day, at most MaxExtensions times, then it ends
//         with no fight: the defenders keep it and win the stake.
//     The fight (SiegeSeconds): the two factions' pilots may fire at each other there (NetFactionsClient.SiegePvp,
//     NetPlayer.PvpWith); every pilot of a side destroyed in the orbit scores for the other side (attKills / defKills); a
//     destroyed defender respawns docked at that station (NetFactionsClient.DefendsSiegeHere in FlightHud). A side whose
//     last pilot there left alive (docked, jumped, disconnected) and stays gone SiegeForfeitSeconds waves the white flag
//     and loses. At the end more kills wins; a tie = held. The stake goes to the winner. Called off, the stake back to the
//     attacker: the claim given up or the defenders disbanding. A fought or finished siege leaves the station protected for
//     ProtectionHours. A claim that lapses without a siege can't be claimed again for LapsedLockSeconds.
//     NetState.Sieges carries them to the players (the HUD banner, TerritoryView). "/faction sieges" lists them.
//   Defence: in a held station's orbit its own race's NPC fighters treat the holder's members as friends and the
//     members of other factions as enemies, unless that pilot paid the toll (Toll credits, asked on arrival by
//     TerritoryView; NetPlayer.TollStation) for this visit. Pilots without a faction are treated as always.
//   Trade cut: at a held station members buy items MemberDiscountPercent cheaper and pay no VAT.
//   VAT: each claim has the holder's rate (Claim.vat, 0..MaxVat %, "/faction vat <rate> [station]", officers; DefaultVat for
//     a new claim). Everyone else (other factions and pilots without one) buys items and dealer ships at the list price plus
//     a tenth of the rate (VatMarkup: 20 % -> +2 %); the rate's share of what they paid goes to the holder's bank
//     (OnPurchase from the shared stock's trades, OnShipBought from the dealer's). Selling there pays no VAT. Units players
//     sold into a station's stock carry no VAT when bought back (NetStock's resale count, cleared when the stock is rolled
//     again), so a buy-and-sell loop doesn't mint money. A higher rate thins the station's goods: each stock roll keeps
//     (100 - rate) % of every row and of the dealer ships (NetStock.Make).
//   Garrison (players' request: a station whose holders are offline fell to one attacker in 5 minutes): officers set a
//     claim's garrison, "/faction garrison <ships> <level> [station]" or the Faction tab: 0..MaxGarrison fighters of the
//     system's race at level 1..MaxGarrisonLevel (hull x(1 + 0.5 (level - 1)), gun x(1 + 0.25 (level - 1)), the dearer
//     fighters from level 3). Upkeep GarrisonUpkeep x ships x level a day from the bank, the first day when it is set
//     (not while the station's siege runs); a bank that can't pay disbands it. In an armed siege the server counts the
//     garrison (Siege.garrisonAlive); the orbit's authority flies that many (NetOrbit.UpdateGarrison: hostile to the attackers, friends of the holders, swarming in
//     front of the station) and reports each death (NetState.GarrisonKillRpc, OnGarrisonKill); a dead fighter comes back
//     GarrisonRespawnSeconds later. Siege.garrisonAlive / level go out with the sieges (PublishSieges).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static partial class NetFactions
    {
        public const int MaxNameLength = 24, MaxMembers = 50;
        const float InviteSeconds = 300f, ClaimTickSeconds = 60f, ChargeSeconds = 300f;   // a deposit's answer may lag (a phone in the background)
        public const int DefaultClaimCost = 500_000, DefaultMaxClaims = 3, DefaultLapseDays = 14;
        public const int DefaultSiegeCost = 250_000, DefaultToll = 10_000;
        public const int MemberDiscountPercent = 10, DefaultVat = 5, MaxVat = 20;

        /// <summary>The price a non-member pays at a station with VAT 'rate': the list price + a tenth of the rate.</summary>
        public static int VatMarkup(int price, int rate) => rate <= 0 || price <= 0 ? price : (int)Math.Min(int.MaxValue, (long)Math.Round(price * (1.0 + rate / 1000.0)));

        /// <summary>Server: the VAT rate of the station's claim (0 = no holder or none).</summary>
        public static int VatAt(int station) => list == null ? 0 : Mathf.Clamp(ClaimAt(station)?.vat ?? 0, 0, MaxVat);
        const long SiegeDelaySeconds = 600, SiegeSeconds = 900, ProtectionHours = 24;
        const long SiegeMeetSeconds = 2 * 86400, SiegeForfeitSeconds = 60, StandoffSeconds = 3600, LapsedLockSeconds = 48 * 3600;
        const int MaxExtensions = 4, LapseFloorDays = 3;
        const float SiegeTickSeconds = 5f;
        /// <summary>The free station defenders of an armed siege's orbit, on top of the garrison (NetOrbit): DefenderStart at
        /// DefenderStartLevel, + DefenderStepCount and + 1 level every DefenderStepSeconds until the fight, at most DefenderMax
        /// at MaxGarrisonLevel; from DefenderGunLevel their guns hit DefenderGunBonus harder.</summary>
        public const int DefenderStart = 16, DefenderStepCount = 4, DefenderMax = 48, DefenderStartLevel = 3, DefenderGunLevel = 4;
        public const long DefenderStepSeconds = 6 * 3600;
        public const float SiegeDefenderRespawnSeconds = 30f, DefenderGunBonus = 1.5f;
        public static int DefenderCount(int step) => Mathf.Min(DefenderStart + DefenderStepCount * Mathf.Max(0, step), DefenderMax);
        public static int DefenderLevel(int step) => Mathf.Min(DefenderStartLevel + Mathf.Max(0, step), MaxGarrisonLevel);
        public const int MaxGarrison = 12, MaxGarrisonLevel = 5, GarrisonUpkeep = 300;
        const long GarrisonRespawnSeconds = 90, DaySeconds = 86400;
        const float GarrisonKillWindow = 10f;   // at most GarrisonKillsPerWindow reports count in it (a modified game reporting a whole garrison)
        const int GarrisonKillsPerWindow = 6;

        /// <summary>A garrison's upkeep a day.</summary>
        public static int GarrisonCost(int ships, int level) => Mathf.Max(0, ships) * Mathf.Clamp(level, 1, MaxGarrisonLevel) * GarrisonUpkeep;

        /// <summary>A siege's price from the bank (-siegecost), the toll a pilot of another faction pays at a held station
        /// (-toll: credits, or "N%" = N percent of the pilot's credits, stored as -N; 0 = none; TollText / ParseToll).</summary>
        public static int SiegeCost { get; internal set; } = DefaultSiegeCost;
        public static int Toll { get; internal set; } = DefaultToll;

        /// <summary>"10000" = that many credits, "5%" = 5 % of the pilot's credits (1..100, stored as -5); null = not valid.</summary>
        public static int? ParseToll(string text)
        {
            text = (text ?? "").Trim().Replace(",", "").Replace(" ", "");
            if (text.EndsWith("%"))
                return int.TryParse(text.TrimEnd('%'), out int pct) && pct >= 0 && pct <= 100 ? -pct : (int?)null;
            return long.TryParse(text, out long n) && n >= 0 ? (int)Math.Min(n, int.MaxValue) : (int?)null;
        }

        /// <summary>The toll setting as typed: "10000" or "5%".</summary>
        public static string TollText(int toll) => toll < 0 ? $"{-toll}%" : toll.ToString();

        /// <summary>What a pilot with 'credits' pays for the toll setting 'toll' (0 = none).</summary>
        public static int TollFor(int toll, long credits)
        {
            if (toll >= 0) return toll;
            long balance = Math.Max(0L, credits), rate = Math.Min(100L, -(long)toll);
            // Split the multiplication to avoid overflowing a valid 64-bit credit balance.
            long amount = balance / 100 * rate + (balance % 100) * rate / 100;
            return (int)Math.Min(int.MaxValue, Math.Max(1L, amount));
        }
        const int KaamoStation = 108, LomaSystem = 25;

        /// <summary>A claim's price from the bank (-claimcost), the claims per faction (-maxclaims), the days without a member
        /// docking before a claim lapses (-claimdays).</summary>
        public static int ClaimCost { get; internal set; } = DefaultClaimCost;
        public static int MaxClaims { get; internal set; } = DefaultMaxClaims;
        public static int LapseDays { get; internal set; } = DefaultLapseDays;

        /// <summary>DedicatedServer.Boot: the command line's choices.</summary>
        public static void Configure(int claimCost, int maxClaims, int lapseDays, int siegeCost, int toll)
        {
            SiegeCost = Mathf.Max(0, siegeCost);
            Toll = Mathf.Max(-100, toll);   // -N = N % of the pilot's credits
            ClaimCost = Mathf.Max(0, claimCost);
            MaxClaims = Mathf.Clamp(maxClaims, 0, 100);
            LapseDays = Mathf.Max(1, lapseDays);
        }

        [Serializable]
        public class Faction
        {
            public string id, name, tag, leader, created;
            public List<string> officers = new List<string>(), members = new List<string>();   // members include everyone
            public long bank;
            public int home = -1;
        }

        [Serializable] public class Claim
        {
            public int station; public string faction, claimed, lastDock; public long protectedUntil;
            public int garrisonSize, garrisonLevel = 1; public long garrisonPaidUntil;   // the garrison (0 = none) and its upkeep
            public int vat = DefaultVat;                                                  // the holder's VAT rate (%)
        }

        /// <summary>A siege: times in Unix seconds. armed = waiting for both sides (from startsAt until expiresAt), started =
        /// the fight (until endsAt); stake = the attacker's SiegeCost, to the winner; noAttackersSince / noDefendersSince =
        /// when that side was last seen gone from the orbit during the fight (0 = there). See the header for the rest.</summary>
        [Serializable] public class Siege
        {
            public int station; public string attacker, defender; public long startsAt, endsAt, expiresAt, stake; public bool armed, started;
            public long noAttackersSince, noDefendersSince;
            public int extensions, attKills, defKills, fightStep = -1;   // the days added, the fight's score, the escalation step frozen at its start
            public float attStandoff, defStandoff;                       // seconds each side was absent while the other waited (standoff clock)
            public bool attLeftAlive, defLeftAlive;                      // that side's last pilot in the orbit left it alive (the white flag)
            [NonSerialized] public List<ulong> respawning = new List<ulong>();   // defenders destroyed in the fight: their next dock there is a respawn
            public int garrisonAlive, garrisonLevel = 1;                  // the defenders' fighters alive now (server-side count)
            public List<long> garrisonBack = new List<long>();            // when each dead one comes back (Unix seconds)
            [NonSerialized] public float killWindowStart = -100f;
            [NonSerialized] public int killsInWindow;
            // Each pilot of the two factions at the last tick (the fight's deaths and departures, the defenders' docks).
            [NonSerialized] public Dictionary<ulong, (bool attacker, bool inOrbit, bool alive, bool docked)> last = new Dictionary<ulong, (bool, bool, bool, bool)>();
        }

        [Serializable]
        class FactionList
        {
            public List<Faction> factions = new List<Faction>();
            public List<Claim> claims = new List<Claim>();
            public List<Siege> sieges = new List<Siege>();
            public List<Lapsed> lapsed = new List<Lapsed>();
        }

        /// <summary>A claim that lapsed without a siege: nobody claims it before 'until' (Unix seconds).</summary>
        [Serializable] public class Lapsed { public int station; public long until; }

        // Deposits waiting for the player's game to pay: by a token the server made (the amount is the server's own).
        static readonly Dictionary<int, (string account, string faction, long amount, float until)> charges = new Dictionary<int, (string, string, long, float)>();
        static int nextCharge = 1;
        static float claimTimer, siegeTimer;

        static FactionList list;
        static readonly Dictionary<string, (string faction, float until)> invites = new Dictionary<string, (string, float)>();   // by profile id

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            list = null; invites.Clear(); charges.Clear(); deferredStationTax.Clear(); claimTimer = siegeTimer = 0f; taxDirty = false;
            ClaimCost = DefaultClaimCost; MaxClaims = DefaultMaxClaims; LapseDays = DefaultLapseDays;
            SiegeCost = DefaultSiegeCost; Toll = DefaultToll;
        }

        static string PathOf => Path.Combine(NetProfiles.Folder, "factions.json");
        /// <summary>The file before the groups were called factions (read once, when factions.json doesn't exist yet).</summary>
        static string LegacyPath => Path.Combine(NetProfiles.Folder, "crews.json");

        /// <summary>NetProfiles.Start: the factions loaded.</summary>
        public static void Load()
        {
            invites.Clear();
            deferredStationTax.Clear();
            try
            {
                if (File.Exists(PathOf)) list = JsonUtility.FromJson<FactionList>(File.ReadAllText(PathOf));
                else if (File.Exists(LegacyPath))
                {
                    // A server from before the rename ("crews"): its groups, claims and sieges carry over (written as
                    // factions.json on the next save; crews.json stays as it was).
                    string old = File.ReadAllText(LegacyPath).Replace("\"crews\":", "\"factions\":").Replace("\"crew\":", "\"faction\":");
                    list = JsonUtility.FromJson<FactionList>(old);
                    Debug.Log($"Server: crews.json read as the factions ({list?.factions?.Count ?? 0}).");
                }
                else list = null;
            }
            catch (Exception e) { Debug.LogError($"NetFactions: factions.json unreadable ({e.Message}); starting a new list (the old file stays as .bak on the next write)."); list = null; }
            if (list == null) list = new FactionList();
            if (list.factions == null) list.factions = new List<Faction>();
            if (list.claims == null) list.claims = new List<Claim>();
            if (list.sieges == null) list.sieges = new List<Siege>();
            if (list.lapsed == null) list.lapsed = new List<Lapsed>();
            foreach (var c in list.factions) { c.officers ??= new List<string>(); c.members ??= new List<string>(); }
            foreach (var c in list.claims) { c.garrisonLevel = Mathf.Clamp(c.garrisonLevel, 1, MaxGarrisonLevel); c.vat = Mathf.Clamp(c.vat, 0, MaxVat); }
            foreach (var x in list.sieges)
            {
                x.garrisonBack ??= new List<long>();
                x.last ??= new Dictionary<ulong, (bool, bool, bool, bool)>();
                x.respawning ??= new List<ulong>();
                x.garrisonLevel = Mathf.Clamp(x.garrisonLevel, 1, MaxGarrisonLevel);
                if (x.expiresAt == 0) x.expiresAt = x.startsAt + SiegeMeetSeconds;   // a siege from before the meeting rule
                if (x.started) x.armed = true;
            }
            charges.Clear();
            PublishClaims();
        }

        static void Save() { if (list != null) NetProfiles.Write(PathOf, JsonUtility.ToJson(list, true)); }

        /// <summary>Find a faction by its stable ID, null = unknown.</summary>
        public static Faction FindById(string id) => string.IsNullOrEmpty(id) || list == null
            ? null : list.factions.Find(c => c != null && string.Equals(c.id, id, StringComparison.Ordinal));

        /// <summary>The faction a profile belongs to, null = none.</summary>
        public static Faction Of(string account) => account == null || list == null ? null : list.factions.Find(c => c.members.Contains(account));

        /// <summary>The faction of a connected player, null = none (or a guest).</summary>
        public static Faction OfClient(ulong client) => Of(NetProfiles.AccountOf(client));

        static Faction ByTag(string tag) => list?.factions.Find(c => string.Equals(c.tag, tag, StringComparison.OrdinalIgnoreCase));

        static bool IsOfficer(Faction c, string account) => c.leader == account || c.officers.Contains(account);

        // ---- the station's toll and the trade tax (phase 3) ---------------------------------------------

        static bool taxDirty;

    }
}
