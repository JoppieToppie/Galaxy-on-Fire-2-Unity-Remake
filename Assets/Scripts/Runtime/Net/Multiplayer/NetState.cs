// NetState.cs
// The multiplayer world (NetGame: the host spawns one; it lives in DontDestroyOnLoad for the whole session):
//   the world seed: reaching a client it starts that player's game (NetGame.EnterWorld); every orbit's asteroid field is
//     built from it (NetGame.OrbitSeed), so the players in one orbit share it;
//   destroyed asteroids per station, kept while someone is there: a player arriving in an orbit gets its list (the asteroids stay
//     gone), and a destruction reaches everyone (each applies it to its own orbit); an orbit left empty for
//     AsteroidRespawnSeconds gets its whole field back (RespawnEmptyAsteroidFields);
//   the spawn / despawn requests of an orbit's authority (NetOrbit): the NetProxy and NetCrate objects it shows the others
//     there are spawned by the host and owned by that player; the host also removes them once their owner is no longer in
//     that orbit (docked, jumped, gone);
//   the chat relay (NetChat): a player's line reaches everyone with the sender's name and location;
//   squads (NetSquad): invitations, joining and leaving, a squad of one dissolved; the players' kill notices;
//   squad missions (NetMissions): the shared missions, their progress and results, a disconnecting carrier's cargo;
//   the shared shop stock (NetStock): the host's list per station, the players' trades, the reset;
//   whether the session allows the Debug menu (NetGame.HostAllowsDebug, Cheats.Allowed);
//   a dedicated server's player profiles (NetProfiles / NetProfileClient): signing in, the profile to the player, their
//     uploads, handing control between a profile's devices (the chat's /link /control /profile: NetCommands);
//   moderation (NetModeration, through NetCommands): /kick /tempban /ban /unban /bans /op /deop /staff;
//   factions (NetFactions): the chat's /faction and /f commands, the claims (Claims), bank deposits and payouts;
//   arena matches (NetArena / NetArenaClient): the chat's /duel /accept /decline /ffa /leave /arena /top, a match's
//     start, state, end and kills; whether players may fight outside them (FreePvp, -freepvp).
// The server trusts no client further than its own game: every request is limited per client (NetRateLimit) and checked
// (NetGuard): the sender exists and acts where it is (spawns, asteroids and claims in its own orbit, trades and hangar
// ships at the station it is docked at), only on its own objects (despawns) or those it is entitled to (a takeover in its
// orbit, its squad's mission objects), only with an invitation it got (joining a squad), only on missions the server saw
// it get (MissionRecord: results, progress and handed-over cargo reach that mission's team alone, shares bounded by the
// mission's reward), with numbers, indices and texts in range. The messages for the players (notices, results, stock,
// teleports...) may only come from the server (InvokePermission.Server): a client can't send them to the others.

using System.Collections.Generic;
using GoF2Remake.Data;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using GoF2Remake.Events;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed partial class NetState : NetworkBehaviour
    {
        const float SweepSeconds = 0.5f, PruneSeconds = 10f;
        /// <summary>An orbit nobody has flown in for this long gets its asteroid field back (the destroyed list is cleared). Not
        /// at once: docking to sell ore and launching again would refill the field on every trip.</summary>
        const float AsteroidRespawnSeconds = 120f;      // Maybe add as a future server setting
        /// <summary>The asteroid indices an orbit may have: its own field from 0 (OrbitBuilder.SpawnAsteroids makes at most 79),
        /// an Ore Mining site's from MiningSite.IndexBase (1000 + 100 per slot, below 9000).</summary>
        const int MaxAsteroids = 10000;
        /// <summary>The NetProxy / NetCrate objects one player may own at once (a busy orbit's traffic, a mission's ships and
        /// junk, a battle's containers: well below), and how long a spawn request waits for its sender's place to arrive.</summary>
        const int MaxProxies = 300, MaxCrates = 150, MaxPendingSpawns = 2000;
        const float SpawnWaitSeconds = 5f;
        /// <summary>The most a squad mission pays one member (a 10x mission is far below; the docked guard is 1 000 001).</summary>
        const int MaxMissionPay = 10000000;

        readonly NetworkVariable<int> seed = new NetworkVariable<int>();
        readonly NetworkVariable<bool> dedicated = new NetworkVariable<bool>();
        readonly NetworkVariable<bool> debugAllowed = new NetworkVariable<bool>();   // the host's / server's choice (a server's admins may change it)
        readonly NetworkVariable<bool> profilesOn = new NetworkVariable<bool>();     // the server keeps player profiles (NetProfiles)
        readonly NetworkVariable<bool> freePvp = new NetworkVariable<bool>();        // players may fight anywhere (else only in arenas)
        readonly NetworkVariable<FixedString4096Bytes> claims = new NetworkVariable<FixedString4096Bytes>();   // NetFactions' territory
        readonly NetworkVariable<FixedString4096Bytes> sieges = new NetworkVariable<FixedString4096Bytes>();   // NetFactions' sieges
        readonly NetworkVariable<FixedString4096Bytes> pirateBoard = new NetworkVariable<FixedString4096Bytes>();   // PirateReports' board (JSON)
        readonly NetworkVariable<int> toll = new NetworkVariable<int>();   // NetFactions.Toll
        readonly NetworkVariable<int> rules = new NetworkVariable<int>();   // NetRules: the session's gameplay options
        readonly NetworkVariable<byte> kaamoLevel = new NetworkVariable<byte>();   // NetRules.HostKaamoLevel
        readonly NetworkVariable<byte> flightModel = new NetworkVariable<byte>();  // NetRules.HostFlight + 2 (0 = not arrived)
        readonly NetworkVariable<FixedString64Bytes> serverId = new NetworkVariable<FixedString64Bytes>();   // their key on the client
        readonly NetworkVariable<bool> freeForAll = new NetworkVariable<bool>();     // /pvp, an event's Free For All: every player an enemy
        readonly NetworkVariable<Unity.Collections.FixedString4096Bytes> sessionMods = new NetworkVariable<Unity.Collections.FixedString4096Bytes>();   // NetMods.SessionList
        readonly Dictionary<int, HashSet<int>> destroyed = new Dictionary<int, HashSet<int>>();
        readonly Dictionary<int, float> emptySince = new Dictionary<int, float>();   // orbit -> when its last player left (asteroid respawn)
        GameObject proxyPrefab, cratePrefab;
        int pendingSeed;
        bool pendingDedicated;
        float sweepTimer, pruneTimer;
        readonly Dictionary<ulong, float> staleSince = new Dictionary<ulong, float>();

        /// <summary>The session's world, null outside one.</summary>
        public static NetState Instance { get; private set; }

        /// <summary>Host, before spawning (written in OnNetworkSpawn, so it is in the clients' spawn data); 'server' = a
        /// dedicated server, no player of its own.</summary>
        public void SetSeed(int value, bool server = false) { pendingSeed = value; pendingDedicated = server; }

        /// <summary>The session runs on a dedicated server: the players' client ids start at 1.</summary>
        public bool Dedicated => dedicated.Value;

        /// <summary>The session allows the Debug menu (NetGame.HostAllowsDebug when it started; off by default).</summary>
        public bool DebugAllowed => debugAllowed.Value;

        /// <summary>The server keeps player profiles: a joining game signs in and waits for its profile (NetProfileClient).</summary>
        public bool ProfilesOn => profilesOn.Value;

        /// <summary>Players may shoot each other anywhere (NetGame.FreePvp, -freepvp); else only in an arena match.</summary>
        public bool FreePvp => freePvp.Value;

        /// <summary>The session's gameplay rules (NetRules bits).</summary>
        public int Rules => rules.Value;

        /// <summary>Server (NetServerSettings): the Debug menu / free PvP changed while running.</summary>
        internal void SetDebugAllowed(bool on) { if (IsServer && debugAllowed.Value != on) debugAllowed.Value = on; }
        internal void SetFreePvp(bool on) { if (IsServer && freePvp.Value != on) freePvp.Value = on; }
        internal void SetRules(int mask) { if (IsServer && rules.Value != mask) rules.Value = mask; }

        /// <summary>The session's Kaamo upgrade limit (NetRules.HostKaamoLevel; 0 before it has arrived).</summary>
        public int KaamoLevel => kaamoLevel.Value;
        internal void SetKaamoLevel(int level) { if (IsServer && kaamoLevel.Value != level) kaamoLevel.Value = (byte)level; }

        /// <summary>The session's flight model (NetRules.HostFlight; FlightKnown false before it has arrived).</summary>
        public int FlightModel => flightModel.Value - 2;
        public bool FlightKnown => flightModel.Value > 0;
        internal void SetFlightModel(int style) { if (IsServer && flightModel.Value != style + 2) flightModel.Value = (byte)(style + 2); }

        /// <summary>The factions' claimed stations, "station|TAG|Name|VAT %" per line (NetFactions, NetFactionsClient).</summary>
        public string Claims => claims.Value.ToString();

        /// <summary>Server: the claims (cut at a whole line to fit the network variable's 4 KB).</summary>
        internal void SetClaims(string text)
        {
            if (!IsServer) return;
            text = Fit(text);
            if (claims.Value.ToString() != text) claims.Value = text;
        }

        /// <summary>The sieges, "station|attacker|defender|phase|seconds left|stake|garrison alive|garrison level|station
        /// defenders|their level|attackers' kills|defenders' kills|days added" per line (NetFactions).</summary>
        public string Sieges => sieges.Value.ToString();

        internal void SetSieges(string text)
        {
            if (!IsServer) return;
            text = Fit(text);
            if (sieges.Value.ToString() != text) sieges.Value = text;
        }

        /// <summary>The toll a pilot of another faction pays at a held station (NetFactions.Toll: credits, or -N = N % of the
        /// pilot's credits; 0 = none). What one pilot pays: NetFactionsClient.TollAmount.</summary>
        public int Toll => toll.Value;

        internal void SetToll(int value) { if (IsServer && toll.Value != value) toll.Value = value; }

        static string Fit(string text)
        {
            text ??= "";
            while (System.Text.Encoding.UTF8.GetByteCount(text) > 4000)
            {
                int cut = text.LastIndexOf('\n', text.Length - 2);
                text = cut < 0 ? "" : text.Substring(0, cut + 1);
            }
            return text;
        }

        /// <summary>Free for all (/pvp, an event): every other player is an enemy (NetAggression.IsHostile), squadmates excepted.</summary>
        public static bool FreeForAll => Instance != null && Instance.IsSpawned && Instance.freeForAll.Value;

        /// <summary>Server: free for all on / off.</summary>
        internal void SetFreeForAll(bool on) { if (IsServer) freeForAll.Value = on; }

        public override void OnNetworkSpawn()
        {
            name = "NetState";
            DontDestroyOnLoad(gameObject);
            Instance = this;
            Cheats.ClearGranted();   // an admin's /cheat lasts one session
            NetAggression.Clear();   // and who attacked whom
            if (IsServer)
            {
                NetAdmin.Reset();
                EventRunner.Reset();
                NetNews.ServerStart();   // the saved news (a dedicated server with profiles)
                NetMotd.Load();          // motd.txt
                seed.Value = pendingSeed;
                dedicated.Value = pendingDedicated;
                debugAllowed.Value = NetGame.HostAllowsDebug;
                profilesOn.Value = NetProfiles.Enabled;
                serverId.Value = NetProfiles.ServerId;
                freePvp.Value = NetGame.FreePvp;
                rules.Value = NetRules.Host;
                kaamoLevel.Value = (byte)NetRules.HostKaamoLevel;
                flightModel.Value = (byte)(NetRules.HostFlight + 2);
                NetFactions.OnStateSpawned();   // the claims, now that this object exists
                PirateReports.ServerReset();   // the Pirate activity board starts empty (filled by the next sweep)
                sessionMods.Value = NetMods.SessionList;
                proxyPrefab = Resources.Load<GameObject>($"{NetGame.PrefabFolder}/NetProxy");
                cratePrefab = Resources.Load<GameObject>($"{NetGame.PrefabFolder}/NetCrate");
                NetRateLimit.Reset();
                NetTrade.Reset();
            }
            // The session's Pirate activity board on every game (the host's too), now and whenever the server changes it.
            pirateBoard.OnValueChanged += (_, now) => PirateReports.ApplyShared(now.ToString());
            PirateReports.ApplyShared(pirateBoard.Value.ToString());
            if (!IsServer)
            {
                NetMods.ApplyFromServer(sessionMods.Value.ToString());   // the session's mods before the world is built
                // A server with profiles: the world waits for this player's profile (NetProfileClient.Begin -> EnterWorld).
                if (profilesOn.Value) NetProfileClient.Begin(serverId.Value.ToString(), seed.Value);
                else NetGame.EnterWorld(seed.Value);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>No other player is in this orbit: the arriving player builds its traffic (only then: the players already
        /// there never see ships appear out of nowhere).</summary>
        public static bool OrbitEmpty(int station)
        {
            foreach (var p in NetPlayer.All)
                if (p != null && !p.IsOwner && p.IsSpawned && p.InSpace && p.Station == station) return false;
            return true;
        }

        /// <summary>The player the server made run this orbit's NPC traffic (UpdateOrbitRunners), null = nobody yet.</summary>
        public static NetPlayer RunnerOf(int station)
        {
            if (station < 0) return null;
            foreach (var p in NetPlayer.All)
                if (p != null && p.IsSpawned && p.RunsOrbit == station) return p;
            return null;
        }

        // ---- who runs each orbit (the server's word) ----------------------------------------------------------

        const float RunnerSeconds = 0.25f;
        /// <summary>Server: the player running each orbit's NPC traffic (client id), by orbit.</summary>
        readonly Dictionary<int, ulong> orbitRunners = new Dictionary<int, ulong>();
        float runnerTimer;

        // ---- hits (NetPlayer / NetProxy relays) ----------------------------------------------------------------

        /// <summary>Server: who hit what when (the checked hits): a kill notice or a kill credit needs a hit a moment before.</summary>
        readonly Dictionary<(ulong target, ulong shooter), float> hits = new Dictionary<(ulong, ulong), float>();

        // ---- squads (NetSquad) ------------------------------------------------------------------------------

        int nextSquad = 1;
        /// <summary>Server: the invitations sent (inviter, invited) and when: joining a squad needs one (before, any player
        /// docked beside a squad could join it, and with it get its mission and its shares).</summary>
        readonly Dictionary<(ulong from, ulong to), float> invites = new Dictionary<(ulong, ulong), float>();
        const float InviteGraceSeconds = 15f;   // past NetSquad.InviteSeconds: the popup's answer on the way

        // ---- player profiles (NetProfiles / NetProfileClient) ----------------------------------------------

        int profileSeq;

        // ---- the Pirate activity board (PirateReports) ------------------------------------------------------------

        const float PirateSeconds = 5f;
        float pirateTimer;

        // ---- squad missions (NetMissions) -------------------------------------------------------------------

        /// <summary>Host: a squad mission as the server saw it handed out: its team (the players it was shared with: only they
        /// may report on it, and its results, progress and a carrier's cargo reach only them), its reward and wager (a
        /// success's share is at most what the mission pays, a failure's at most its wager) and amount (the cargo a carrier
        /// can hand over). A mission held alone isn't recorded: nobody else has it. Mission ids are the clients' (random), so
        /// one id may have several records, one per team: a modified client claiming another squad's id gets a record of its
        /// own and reaches nobody of that squad.</summary>
        sealed class MissionRecord
        {
            public readonly HashSet<ulong> members = new HashSet<ulong>();
            public int reward, wager, amount;
            public bool ended;   // its result went out (two members delivering at once: the second is dropped)
            public float endedAt;
            public int MaxShare => (int)System.Math.Min(2L * reward + 50L, MaxMissionPay);   // the reward + a standing bonus of up to 100 %
        }

        readonly Dictionary<long, List<MissionRecord>> missions = new Dictionary<long, List<MissionRecord>>();
        /// <summary>Host: each squad's last new mission (ShareMissionRpc's check).</summary>
        readonly Dictionary<int, (long netId, float time, string who)> squadAccepts = new Dictionary<int, (long, float, string)>();

        // ---- the shared shop stock (NetStock) ------------------------------------------------------------------

        /// <summary>Host: the stations whose stock changed this frame: sent once to the players docked there (Update), not once
        /// per traded unit (a held arrow trades 5 units a frame).</summary>
        readonly HashSet<int> dirtyStock = new HashSet<int>();
        /// <summary>Host: the dealer ships each player reserved at each station and hasn't traded or given back yet: a trade
        /// may only take a reserved row off the list, and only put back that row or the player's own old hull.</summary>
        readonly Dictionary<(ulong client, int station), List<int>> reservations = new Dictionary<(ulong, int), List<int>>();

        /// <summary>The most units one trade message may move (a whole hold or stock row at once).</summary>
        const int MaxTradeUnits = 10000;

        // ---- the orbit authority's objects ------------------------------------------------------------------

        struct SpawnRequest
        {
            public ulong client;
            public int station, localId;
            public bool crate;
            public float time;
        }

        /// <summary>Host: spawn requests whose sender wasn't seen in that orbit yet (its place arrives at the tick, maybe after
        /// the request): spawned once it is, dropped after SpawnWaitSeconds.</summary>
        readonly List<SpawnRequest> pendingSpawns = new List<SpawnRequest>();
        /// <summary>Host: the NetProxy / NetCrate objects each player owns (counted at the sweep, plus the spawns since).</summary>
        readonly Dictionary<ulong, (int proxies, int crates)> owned = new Dictionary<ulong, (int, int)>();

        // ---- the sector's news (NetNews) ----------------------------------------------------------------------

        void Update()
        {
            NetStock.Flush();   // the shared stock that arrived, applied between frames (every player)
            NetTradeClient.Tick();   // this player's trade offer goes out, kept within the hold
            if (!IsServer || !IsSpawned) return;
            NetTrade.Tick();      // trade requests and trades that ran out, sides that left the hangar
            NetAudit.Tick();      // the shop runs that stopped, into the money log
            NetProfiles.Tick();   // link codes and handovers that ran out
            NetArena.Tick();      // queues, countdowns, time limits
            if (NetProfiles.Enabled) NetFactions.Tick();   // claims kept by docking members, lapses, stale deposits
            // The stations traded at this frame: their stock once for everyone docked there.
            if (dirtyStock.Count > 0)
            {
                foreach (int station in dirtyStock) BroadcastStock(station);
                dirtyStock.Clear();
            }
            UpdatePendingSpawns();   // their senders' places arrived
            UpdateOrbitRunners();    // who runs each orbit, the ships of one who left handed on
            EventRunner.Tick();   // a running event
            if ((sweepTimer -= Time.unscaledDeltaTime) > 0f) return;
            sweepTimer = SweepSeconds;
            NetRateLimit.Tick();   // the clients that kept flooding go
            NetStock.HostTick(this);   // the stock resets
            if ((pirateTimer -= SweepSeconds) <= 0f)
            {
                pirateTimer = PirateSeconds;
                PirateReports.ServerTick(Database.Shared, SetPirateBoard);   // the Pirate activity board kept full
            }
            DissolveSingles();   // a squadmate who disconnected
            if ((pruneTimer -= SweepSeconds) <= 0f) { pruneTimer = PruneSeconds; Prune(); }
            // An orbit's objects go once their owner isn't in that orbit any more.
            var owners = new Dictionary<ulong, NetPlayer>();
            foreach (var p in NetPlayer.All) if (p != null && p.IsSpawned) owners[p.OwnerClientId] = p;
            RespawnEmptyAsteroidFields(owners);
            var stale = new List<NetworkObject>();
            owned.Clear();
            foreach (var obj in NetworkManager.SpawnManager.SpawnedObjectsList)
            {
                int station;
                var proxy = obj.GetComponent<NetProxy>();
                var crate = proxy == null ? obj.GetComponent<NetCrate>() : null;
                if (proxy != null) station = proxy.Station;
                else if (crate != null) station = crate.Station;
                else continue;
                owned.TryGetValue(obj.OwnerClientId, out var n);
                if (proxy != null) n.proxies++; else n.crates++;
                owned[obj.OwnerClientId] = n;
                // A new one waits for its owner's position (sent at the tick, maybe after the spawn request).
                if (Time.unscaledTime - (proxy != null ? proxy.SpawnedAt : crate.SpawnedAt) < 3f) continue;
                // Its game flies there and runs it (an object the host inherited from a disconnected player waits for HandOver).
                bool inherited = proxy != null && proxy.Creator != obj.OwnerClientId && !proxy.HandedOver;
                if (!inherited && owners.TryGetValue(obj.OwnerClientId, out var owner) && owner.InSpace && owner.Station == station) { staleSince.Remove(obj.NetworkObjectId); continue; }
                // Players still in that orbit get it (HandOver: every kind but a player's wingmen, which go with their
                // player); nobody there any more: it goes (the next player builds the orbit anew).
                bool watched = false;
                foreach (var p in owners.Values) if (p.InSpace && p.Station == station) { watched = true; break; }
                if (!staleSince.TryGetValue(obj.NetworkObjectId, out float since)) staleSince[obj.NetworkObjectId] = since = Time.unscaledTime;
                float grace = proxy != null && proxy.IsWingman ? 5f : 20f;   // (a handover that didn't happen: never kept for ever)
                if (!watched || Time.unscaledTime - since > grace) stale.Add(obj);
            }
            foreach (var obj in stale) { staleSince.Remove(obj.NetworkObjectId); if (obj.IsSpawned) obj.Despawn(); }
        }

    }
}
