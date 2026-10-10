// NetState.cs
// The multiplayer world (NetGame: the host spawns one; it lives in DontDestroyOnLoad for the whole session):
//   the world seed: reaching a client it starts that player's game (NetGame.EnterWorld); every orbit's asteroid field is
//     built from it (NetGame.OrbitSeed), so the players in one orbit share it;
//   destroyed asteroids per station, kept for the session: a player arriving in an orbit gets its list (the asteroids stay
//     gone), and a destruction reaches everyone (each applies it to its own orbit);
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
    public sealed class NetState : NetworkBehaviour
    {
        const float SweepSeconds = 0.5f, PruneSeconds = 10f;
        /// <summary>The asteroid indices an orbit's field may have (OrbitBuilder.SpawnAsteroids makes far fewer).</summary>
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
        readonly NetworkVariable<int> toll = new NetworkVariable<int>();   // NetFactions.Toll
        readonly NetworkVariable<int> rules = new NetworkVariable<int>();   // NetRules: the session's gameplay options
        readonly NetworkVariable<FixedString64Bytes> serverId = new NetworkVariable<FixedString64Bytes>();   // their key on the client
        readonly NetworkVariable<bool> freeForAll = new NetworkVariable<bool>();     // /pvp, an event's Free For All: every player an enemy
        readonly NetworkVariable<Unity.Collections.FixedString4096Bytes> sessionMods = new NetworkVariable<Unity.Collections.FixedString4096Bytes>();   // NetMods.SessionList
        readonly Dictionary<int, HashSet<int>> destroyed = new Dictionary<int, HashSet<int>>();
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

        /// <summary>The factions' claimed stations, "station|TAG|Name" per line (NetFactions, NetFactionsClient).</summary>
        public string Claims => claims.Value.ToString();

        /// <summary>Server: the claims (cut at a whole line to fit the network variable's 4 KB).</summary>
        internal void SetClaims(string text)
        {
            if (!IsServer) return;
            text = Fit(text);
            if (claims.Value.ToString() != text) claims.Value = text;
        }

        /// <summary>The sieges, "station|attacker|defender|started|seconds left|control" per line (NetFactions).</summary>
        public string Sieges => sieges.Value.ToString();

        internal void SetSieges(string text)
        {
            if (!IsServer) return;
            text = Fit(text);
            if (sieges.Value.ToString() != text) sieges.Value = text;
        }

        /// <summary>The toll a pilot of another faction pays at a held station (NetFactions.Toll; 0 = none).</summary>
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
                seed.Value = pendingSeed;
                dedicated.Value = pendingDedicated;
                debugAllowed.Value = NetGame.HostAllowsDebug;
                profilesOn.Value = NetProfiles.Enabled;
                serverId.Value = NetProfiles.ServerId;
                freePvp.Value = NetGame.FreePvp;
                rules.Value = NetRules.Host;
                NetFactions.OnStateSpawned();   // the claims, now that this object exists
                sessionMods.Value = NetMods.SessionList;
                proxyPrefab = Resources.Load<GameObject>($"{NetGame.PrefabFolder}/NetProxy");
                cratePrefab = Resources.Load<GameObject>($"{NetGame.PrefabFolder}/NetCrate");
                NetRateLimit.Reset();
                NetTrade.Reset();
            }
            else
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

        /// <summary>Nobody else runs this orbit's NPCs: a player there may take the old authority's ships over (NetOrbit).</summary>
        public static bool IsOrbitAuthority(int station)
        {
            foreach (var p in NetPlayer.All)
                if (p != null && !p.IsOwner && p.IsSpawned && p.InSpace && p.Station == station && p.OrbitAuthority) return false;
            return true;
        }

        // ---- hits (NetPlayer / NetProxy relays) ----------------------------------------------------------------

        /// <summary>Server: who hit what when (the checked hits): a kill notice or a kill credit needs a hit a moment before.</summary>
        readonly Dictionary<(ulong target, ulong shooter), float> hits = new Dictionary<(ulong, ulong), float>();

        /// <summary>Server: 'shooter' hit the network object 'target' (a NetPlayer or a NetProxy) now.</summary>
        internal void NoteHit(ulong target, ulong shooter)
        {
            if (IsServer) hits[(target, shooter)] = Time.unscaledTime;
        }

        /// <summary>Server: 'shooter' hit 'target' within the last 'seconds'.</summary>
        internal bool HitRecently(ulong target, ulong shooter, float seconds) =>
            hits.TryGetValue((target, shooter), out float t) && Time.unscaledTime - t <= seconds;

        // ---- asteroids --------------------------------------------------------------------------------------

        /// <summary>A player destroyed asteroid 'index' of 'station' (shot, mined, rammed): only a player flying there (else
        /// one client could empty every field of the session).</summary>
        [Rpc(SendTo.Server)]
        public void AsteroidDestroyedRpc(int station, int index, bool mined, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Asteroid)) return;
            if (index < 0 || index >= MaxAsteroids || !NetGuard.InOrbit(client, station)) { NetRateLimit.Reject(client, $"asteroid {index} of orbit {station}"); return; }
            if (!destroyed.TryGetValue(station, out var set)) destroyed[station] = set = new HashSet<int>();
            var by = NetSquad.Find(client);
            if (set.Add(index)) AsteroidGoneRpc(station, index, by != null ? by.DisplayName : "", mined);
        }

        /// <summary>'by': the pilot who destroyed it, 'mined': drilled out (else shot / rammed): a miner's message (Mining).</summary>
        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void AsteroidGoneRpc(int station, int index, string by, bool mined) => NetOrbit.Current?.OnAsteroidGone(station, index, by, mined);

        /// <summary>A player arrived in 'station': the asteroids already gone there.</summary>
        [Rpc(SendTo.Server)]
        public void RequestDestroyedRpc(int station, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Request) || !NetGuard.Orbit(station)) return;
            var list = destroyed.TryGetValue(station, out var set) ? new List<int>(set).ToArray() : new int[0];
            DestroyedListRpc(station, list, RpcTarget.Single(client, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void DestroyedListRpc(int station, int[] indices, RpcParams rpc = default) => NetOrbit.Current?.OnDestroyedList(station, indices);

        // ---- chat (NetChat) ---------------------------------------------------------------------------------

        /// <summary>A player's chat line: the host adds who and where, then everyone gets it (NetChat keeps what is theirs).</summary>
        [Rpc(SendTo.Server)]
        public void SendChatRpc(string text, bool global, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Chat)) return;
            if (text == null || text.Length > NetChat.MaxLength * 4) { NetRateLimit.Reject(client, "an overlong chat line"); return; }
            text = NetChat.Clean(text);
            if (text.Length == 0) return;
            var sender = NetSquad.Find(client);
            if (sender == null) return;
            // A command (the station window's buttons send them as chat lines, NetPanel.Command): run like a typed one
            // (NetCommands), answered to the sender only, never shown to the others.
            if (text[0] == '/')
            {
                string body = text.Substring(1).Trim();
                int space = body.IndexOf(' ');
                string name = space < 0 ? body : body.Substring(0, space);
                string reply = NetCommands.RunOnServer(name, space < 0 ? "" : body.Substring(space + 1), sender)
                               ?? string.Format(Localization.Extra("mpCmdUnknown", "Unknown command /{0}. Type /help for the commands you can use."), name);
                if (reply.Length > 0) NoticeTo(sender, reply);
                return;
            }
            Chat(sender, text, global);
        }

        /// <summary>Server: a chat line from 'sender' (null = the server itself, always global), stamped with its name and
        /// location (/g, /l and the console's say: NetCommands).</summary>
        internal void Chat(NetPlayer sender, string text, bool global)
        {
            text = NetChat.Clean(text);
            if (!IsServer || text.Length == 0) return;
            if (sender != null && NetAdmin.IsMuted(sender.OwnerClientId, out string mutedText)) { NoticeTo(sender, mutedText); return; }   // /mute
            if (sender == null) ServerChat(NetCommands.IssuerName(null), text);
            else ChatRpc(sender.OwnerClientId, sender.TaggedName, text, global, sender.Station, sender.InSpace, sender.InHangar);
        }

        /// <summary>Server: a global chat line from the server itself (the dedicated server's say command).</summary>
        public void ServerChat(string from, string text)
        {
            text = NetChat.Clean(text);
            if (IsServer && text.Length > 0) ChatRpc(NetworkManager.ServerClientId, from, text, true, -1, false, false);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void ChatRpc(ulong sender, string from, string text, bool global, int station, bool inSpace, bool inHangar)
            => NetChat.Receive(sender, from, text, global, station, inSpace, inHangar);

        /// <summary>Server: /w, a private message passed on to that player only, and a copy back to the sender (null = the
        /// server's console, which logs it).</summary>
        internal void Whisper(NetPlayer from, NetPlayer to, string text)
        {
            text = NetChat.Clean(text);
            if (!IsServer || to == null || text.Length == 0) return;
            if (from != null && NetAdmin.IsMuted(from.OwnerClientId, out string mutedText)) { NoticeTo(from, mutedText); return; }   // /mute
            WhisperedRpc(NetCommands.IssuerName(from), text, false, RpcTarget.Single(to.OwnerClientId, RpcTargetUse.Temp));
            if (from != null) WhisperedRpc(to.DisplayName, text, true, RpcTarget.Single(from.OwnerClientId, RpcTargetUse.Temp));
            else Debug.Log($"Server: [To {to.DisplayName}] {text}");
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void WhisperedRpc(string other, string text, bool own, RpcParams rpc = default) => NetChat.ReceiveWhisper(other, text, own);

        // ---- server commands (NetCommands) -----------------------------------------------------------------

        /// <summary>A server command typed in the chat (/kick, /tp, /tphere, /admin, /unadmin): run on the server by the same
        /// code as the dedicated server's console (NetCommands.RunOnServer), which checks the sender's rights; the answer goes
        /// back to the sender.</summary>
        [Rpc(SendTo.Server)]
        public void ServerCommandRpc(string name, string args, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Command)) return;
            if (name == null || name.Length > NetGuard.MaxCommandName || (args != null && args.Length > NetGuard.MaxCommandArgs))
            {
                NetRateLimit.Reject(client, "an overlong command");
                return;
            }
            var from = NetSquad.Find(client);
            if (from == null) return;
            string reply = NetCommands.RunOnServer(name, args, from);
            if (!string.IsNullOrEmpty(reply)) NoticeToRpc(reply, RpcTarget.Single(from.OwnerClientId, RpcTargetUse.Temp));
        }

        /// <summary>Server: a notice in everyone's chat.</summary>
        internal void NoticeAll(string text) => NoticeRpc(text);

        /// <summary>Server: a notice in one player's chat.</summary>
        internal void NoticeTo(NetPlayer p, string text)
        {
            if (p != null && !string.IsNullOrEmpty(text)) NoticeToRpc(text, RpcTarget.Single(p.OwnerClientId, RpcTargetUse.Temp));
        }

        /// <summary>Server: an admin's order for player 'who''s game (NetAdmin: /kill, /heal, /give, /credits, /spawn, /ship...).</summary>
        internal void SendAdmin(ulong who, NetAdmin.Order order, int a, int b, int c, string text, string by) =>
            AdminRpc((byte)order, a, b, c, text ?? "", by, RpcTarget.Single(who, RpcTargetUse.Temp));

        /// <summary>Only the server sends these (checked there: the admin's rights, the arguments).</summary>
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void AdminRpc(byte order, int a, int b, int c, string text, string by, RpcParams rpc = default)
            => NetAdmin.Apply((NetAdmin.Order)order, a, b, c, text, by);

        /// <summary>Server: player 'who' goes to 'd' (their game moves its own ship, NetTeleport.Go). 'by' = the admin.</summary>
        internal void SendTeleport(ulong who, NetTeleport.Destination d, string by) =>
            TeleportToRpc((byte)d.kind, d.player, d.station, d.hasPos, d.pos, by, RpcTarget.Single(who, RpcTargetUse.Temp));

        /// <summary>Only the server teleports (an admin's /tp, checked there): before, any client could move any player.</summary>
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void TeleportToRpc(byte kind, ulong player, int station, bool hasPos, Vector3 pos, string by, RpcParams rpc = default)
            => NetTeleport.Go(new NetTeleport.Destination { kind = (NetTeleport.Kind)kind, player = player, station = station, hasPos = hasPos, pos = pos }, by);

        /// <summary>Server: a player's admin rights (the host's /admin, the dedicated server's admin command).</summary>
        public void SetAdmin(NetPlayer p, bool on)
        {
            if (!IsServer || p == null) return;
            p.SetAdmin(on);
            Debug.Log($"Server: {p.DisplayName} ({p.OwnerClientId}) is {(on ? "now an admin" : "no longer an admin")}.");
        }

        // ---- squads (NetSquad) ------------------------------------------------------------------------------

        int nextSquad = 1;
        /// <summary>Server: the invitations sent (inviter, invited) and when: joining a squad needs one (before, any player
        /// docked beside a squad could join it, and with it get its mission and its shares).</summary>
        readonly Dictionary<(ulong from, ulong to), float> invites = new Dictionary<(ulong, ulong), float>();
        const float InviteGraceSeconds = 15f;   // past NetSquad.InviteSeconds: the popup's answer on the way

        [Rpc(SendTo.Server)]
        public void InviteRpc(ulong target, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Invite)) return;
            Invite(NetSquad.Find(client), NetSquad.Find(target));
        }

        /// <summary>Server: 'from' invites 'to' into their squad (the pilot list's Invite, /invite): only while both are docked
        /// at the same station, like the pilot list shows them.</summary>
        internal void Invite(NetPlayer from, NetPlayer to)
        {
            if (!IsServer || from == null || to == null || NetSquad.Same(from, to)) return;
            if (!from.InHangar || !to.InHangar || from.Station != to.Station) return;
            invites[(from.OwnerClientId, to.OwnerClientId)] = Time.unscaledTime;
            InvitedRpc(from.OwnerClientId, from.DisplayName, RpcTarget.Single(to.OwnerClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void InvitedRpc(ulong from, string name, RpcParams rpc = default) => NetSquad.OnInvited(from, name);

        /// <summary>The invited player said yes: into the inviter's squad (a new one if the inviter had none).</summary>
        [Rpc(SendTo.Server)]
        public void AcceptInviteRpc(ulong inviter, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Squad)) return;
            var leader = NetSquad.Find(inviter);
            var joiner = NetSquad.Find(client);
            if (leader == null || joiner == null || leader == joiner || NetSquad.Same(leader, joiner)) return;   // squadmates already
            // Only an invitation this player got (and not an old one).
            if (!invites.TryGetValue((inviter, client), out float sent) || Time.unscaledTime - sent > NetSquad.InviteSeconds + InviteGraceSeconds)
            {
                NetRateLimit.Reject(client, $"joining {leader.DisplayName}'s squad without an invitation");
                return;
            }
            // Squads form only in a hangar: both docked at the same station.
            if (!leader.InHangar || !joiner.InHangar || leader.Station != joiner.Station)
            {
                NoticeToRpc(Localization.Extra("mpSquadHangarOnly", "Squads can only be formed while docked in the same hangar."),
                            RpcTarget.Single(joiner.OwnerClientId, RpcTargetUse.Temp));
                return;
            }
            invites.Remove((inviter, client));
            JoinSquad(leader, joiner);
            NetProfiles.SquadJoined(leader.OwnerClientId, joiner.OwnerClientId);   // both profiles remember it
        }

        /// <summary>'joiner' into 'leader''s squad (a new one if the leader has none).</summary>
        void JoinSquad(NetPlayer leader, NetPlayer joiner)
        {
            if (leader.SquadId == 0) leader.SetSquad(nextSquad++);
            joiner.SetSquad(leader.SquadId);
            DissolveSingles();
            // The joiner's own mission goes; the squad's active one (the inviter's first) comes to them.
            JoinedSquadRpc(RpcTarget.Single(joiner.OwnerClientId, RpcTargetUse.Temp));
            NetPlayer holder = leader.MissionHeld != 0 ? leader : null;
            if (holder == null)
                foreach (var p in NetPlayer.All)
                    if (p != null && p != joiner && p.IsSpawned && p.SquadId == leader.SquadId && p.MissionHeld != 0) { holder = p; break; }
            if (holder != null) SendMissionToRpc(joiner.OwnerClientId, RpcTarget.Single(holder.OwnerClientId, RpcTargetUse.Temp));
            SquadNoticeRpc(leader.SquadId, string.Format(Localization.Extra("mpSquadJoined", "{0} joined the squad."), joiner.DisplayName));
        }

        /// <summary>NetProfiles: a player signing in goes back into the squad their profile remembers ('mate' is online and
        /// in it). Unlike an invitation this works anywhere, docked or not.</summary>
        internal void RestoreSquad(NetPlayer mate, NetPlayer joiner)
        {
            if (!IsServer || mate == null || joiner == null || mate == joiner || NetSquad.Same(mate, joiner)) return;
            JoinSquad(mate, joiner);
        }

        [Rpc(SendTo.Server)]
        public void LeaveSquadRpc(RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (NetRateLimit.Allow(client, NetRateLimit.Kind.Squad)) LeaveSquad(NetSquad.Find(client));
        }

        /// <summary>Server: 'p' leaves their squad (the squad window's Leave, /leave).</summary>
        internal void LeaveSquad(NetPlayer p)
        {
            if (!IsServer || p == null || p.SquadId == 0) return;
            int id = p.SquadId;
            p.SetSquad(0);
            NetProfiles.SquadLeft(p.OwnerClientId);
            LeftSquadRpc(RpcTarget.Single(p.OwnerClientId, RpcTargetUse.Temp));   // the squad's mission leaves with them
            SquadNoticeRpc(id, string.Format(Localization.Extra("mpSquadLeft", "{0} left the squad."), p.DisplayName));
            DissolveSingles();
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void SquadNoticeRpc(int squad, string text)
        {
            if (NetSquad.LocalSquad == squad || (NetPlayer.Local != null && text.Contains(NetPlayer.Local.DisplayName))) NetChat.Notice(text);
        }

        /// <summary>A squad of one is no squad (the others left or disconnected).</summary>
        void DissolveSingles()
        {
            var count = new Dictionary<int, int>();
            foreach (var p in NetPlayer.All)
                if (p != null && p.IsSpawned && p.SquadId != 0) count[p.SquadId] = count.TryGetValue(p.SquadId, out int c) ? c + 1 : 1;
            foreach (var p in NetPlayer.All)
                if (p != null && p.IsSpawned && p.SquadId != 0 && count[p.SquadId] < 2) p.SetSquad(0);
        }

        // ---- trades between players (NetTrade on the server, NetTradeClient on each game) ---------------------------

        /// <summary>A player asks another docked at the same station to trade (the pilot list's Trade, /trade).</summary>
        [Rpc(SendTo.Server)]
        public void TradeRequestRpc(ulong target, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Invite)) return;
            Notify(client, NetTrade.Request(NetSquad.Find(client), NetSquad.Find(target)));
        }

        internal void TradeRequested(ulong to, ulong from, string name)
        {
            if (IsServer) TradeRequestedRpc(from, name, RpcTarget.Single(to, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void TradeRequestedRpc(ulong from, string name, RpcParams rpc = default) => NetTradeClient.OnRequested(from, name);

        [Rpc(SendTo.Server)]
        public void TradeAcceptRpc(ulong from, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (NetRateLimit.Allow(client, NetRateLimit.Kind.PlayerTrade)) NetTrade.Accept(client, from);
        }

        [Rpc(SendTo.Server)]
        public void TradeDeclineRpc(ulong from, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (NetRateLimit.Allow(client, NetRateLimit.Kind.PlayerTrade)) NetTrade.Decline(client, from);
        }

        /// <summary>A side's whole offer: credits, then item / amount pairs (NetTrade checks it).</summary>
        [Rpc(SendTo.Server)]
        public void TradeOfferRpc(int id, int[] offer, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (NetRateLimit.Allow(client, NetRateLimit.Kind.PlayerTrade)) NetTrade.SetOffer(client, id, offer);
        }

        [Rpc(SendTo.Server)]
        public void TradeConfirmRpc(int id, int version, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (NetRateLimit.Allow(client, NetRateLimit.Kind.PlayerTrade)) NetTrade.Confirm(client, id, version);
        }

        [Rpc(SendTo.Server)]
        public void TradeCancelRpc(int id, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (NetRateLimit.Allow(client, NetRateLimit.Kind.PlayerTrade)) NetTrade.Cancel(client, id);
        }

        /// <summary>The game paid its offer (or couldn't).</summary>
        [Rpc(SendTo.Server)]
        public void TradePaidRpc(int id, bool ok, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (NetRateLimit.Allow(client, NetRateLimit.Kind.PlayerTrade)) NetTrade.Paid(client, id, ok);
        }

        internal void TradeState(ulong to, int id, ulong partner, string partnerName, int station, int version, int[] mine, int[] theirs,
                                 bool myAccepted, bool theirAccepted, byte phase)
        {
            if (IsServer && NetSquad.Find(to) != null) TradeStateRpc(id, partner, partnerName, station, version, mine, theirs, myAccepted, theirAccepted, phase, RpcTarget.Single(to, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void TradeStateRpc(int id, ulong partner, string partnerName, int station, int version, int[] mine, int[] theirs,
                           bool myAccepted, bool theirAccepted, byte phase, RpcParams rpc = default) =>
            NetTradeClient.OnState(id, partner, partnerName, station, version, mine, theirs, myAccepted, theirAccepted, phase);

        internal void TradePay(ulong to, int id, int[] offer)
        {
            if (IsServer && NetSquad.Find(to) != null) TradePayRpc(id, offer, RpcTarget.Single(to, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void TradePayRpc(int id, int[] offer, RpcParams rpc = default) => NetTradeClient.OnPay(id, offer);

        internal void TradeDeliver(ulong to, int id, int[] offer, bool refund, string partnerName)
        {
            if (IsServer && NetSquad.Find(to) != null) TradeDeliverRpc(id, offer, refund, partnerName ?? "", RpcTarget.Single(to, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void TradeDeliverRpc(int id, int[] offer, bool refund, string partnerName, RpcParams rpc = default) =>
            NetTradeClient.OnDeliver(id, offer, refund, partnerName);

        internal void TradeEnded(ulong to, int id, string reason)
        {
            if (IsServer && NetSquad.Find(to) != null) TradeEndedRpc(id, reason ?? "", RpcTarget.Single(to, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void TradeEndedRpc(int id, string reason, RpcParams rpc = default) => NetTradeClient.OnEnded(id, reason);

        /// <summary>A player's ship was destroyed by another player: everyone hears of it (only of a player who hit them a
        /// moment ago: no notices blaming anyone at will).</summary>
        [Rpc(SendTo.Server)]
        public void DestroyedByRpc(ulong killer, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Kill)) return;
            var victim = NetSquad.Find(client);
            var by = NetSquad.Find(killer);
            if (victim == null || by == null || !HitRecently(victim.NetworkObjectId, killer, 30f)) return;
            NetArena.OnKill(victim.OwnerClientId, by.OwnerClientId);   // an arena match's score
            NoticeRpc(string.Format(Localization.Extra("mpDestroyedBy", "{0} was destroyed by {1}."), victim.DisplayName, by.DisplayName));
            EventRunner.OnPlayerKilled(by, victim);   // an event's "on pvpkill"
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void NoticeRpc(string text) => NetChat.Notice(text);

        /// <summary>A player's answer to an event's question (EventScreen): checked and run by the server (EventRunner.OnAnswer).</summary>
        internal void SendAnswer(int ask, int choice) => AnswerRpc(ask, choice);

        [Rpc(SendTo.Server)]
        void AnswerRpc(int ask, int choice, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Command) || choice < 0 || choice > 4) return;
            EventRunner.OnAnswer(client, ask, choice);
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void NoticeToRpc(string text, RpcParams rpc = default) => NetChat.Notice(text);

        /// <summary>Server: a notice in one player's chat.</summary>
        internal void Notify(ulong client, string text)
        {
            if (IsServer && !string.IsNullOrEmpty(text)) NoticeToRpc(text, RpcTarget.Single(client, RpcTargetUse.Temp));
        }

        // ---- player profiles (NetProfiles / NetProfileClient) ----------------------------------------------

        int profileSeq;

        /// <summary>A joining game on a server with profiles: its token for this server ("" = none) and device label.</summary>
        [Rpc(SendTo.Server)]
        public void LoginRpc(string token, string device, string name, RpcParams rpc = default)
        {
            if (NetRateLimit.Allow(rpc.Receive.SenderClientId, NetRateLimit.Kind.Request)) NetProfiles.OnLogin(rpc.Receive.SenderClientId, token, device, name);
        }

        /// <summary>Server: a profile to one player: the header (its new token, if any; its role), then the gzipped chunks
        /// (none = a fresh start). Reliable RPCs to one client arrive in order.</summary>
        internal void SendProfile(ulong client, string token, bool controller, bool guest, string json, int home)
        {
            if (!IsServer) return;
            var parts = NetProfiles.Pack(json);
            int seq = ++profileSeq;
            ProfileHeaderRpc(seq, parts.Count, token ?? "", controller, guest, home, RpcTarget.Single(client, RpcTargetUse.Temp));
            for (int i = 0; i < parts.Count; i++) ProfileChunkRpc(seq, i, parts[i], RpcTarget.Single(client, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void ProfileHeaderRpc(int seq, int count, string token, bool controller, bool guest, int home, RpcParams rpc = default)
            => NetProfileClient.OnProfileHeader(seq, count, token, controller, guest, home);

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void ProfileChunkRpc(int seq, int part, byte[] data, RpcParams rpc = default) => NetProfileClient.OnProfileChunk(seq, part, data);

        /// <summary>A player's profile, a chunk at a time (NetProfileClient.Upload).</summary>
        [Rpc(SendTo.Server)]
        public void UploadChunkRpc(int seq, int part, int count, byte[] data, RpcParams rpc = default)
            => NetProfiles.OnUploadChunk(rpc.Receive.SenderClientId, seq, part, count, data);

        /// <summary>Server: this player's device controls its profile now, or watches.</summary>
        internal void SendRole(ulong client, bool controller)
        {
            if (IsServer) RoleRpc(controller, RpcTarget.Single(client, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void RoleRpc(bool controller, RpcParams rpc = default) => NetProfileClient.OnRole(controller);

        /// <summary>Server: the old controller sends its profile once more before another device takes over (/control).</summary>
        internal void RequestUpload(ulong client)
        {
            if (IsServer) RequestUploadRpc(RpcTarget.Single(client, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void RequestUploadRpc(RpcParams rpc = default) => NetProfileClient.Upload(true);

        // ---- factions (NetFactions / NetFactionsClient) -------------------------------------------------------------

        /// <summary>Server: the player's game is asked to pay a faction deposit ('token' answers it).</summary>
        internal void Charge(ulong client, int token, int amount)
        {
            if (IsServer) ChargeRpc(token, amount, RpcTarget.Single(client, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void ChargeRpc(int token, int amount, RpcParams rpc = default) => NetFactionsClient.OnCharge(token, amount);

        /// <summary>The player's game paid the deposit 'token' (or had too few credits). Its own bucket (Mission), not the
        /// panel's Request one: the window's snapshots share that, and a dropped answer lost credits the game had already
        /// taken. Only a token the server made does anything.</summary>
        [Rpc(SendTo.Server)]
        public void ChargedRpc(int token, bool paid, RpcParams rpc = default)
        {
            if (NetRateLimit.Allow(rpc.Receive.SenderClientId, NetRateLimit.Kind.Mission)) NetFactions.OnCharged(rpc.Receive.SenderClientId, token, paid);
        }

        /// <summary>Server: credits from the faction bank to the player's game.</summary>
        internal void Grant(ulong client, int amount)
        {
            if (IsServer) GrantRpc(amount, RpcTarget.Single(client, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void GrantRpc(int amount, RpcParams rpc = default) => NetFactionsClient.OnGrant(amount);

        /// <summary>A pilot calls their squad for help ('on') or is safe again: a notice to the squadmates with where (NetDistress).</summary>
        [Rpc(SendTo.Server)]
        public void DistressRpc(bool on, RpcParams rpc = default)
        {
            if (!NetRateLimit.Allow(rpc.Receive.SenderClientId, NetRateLimit.Kind.Request)) return;
            var from = NetSquad.Find(rpc.Receive.SenderClientId);
            if (from == null || from.SquadId == 0) return;
            var st = NetGame.Db.Stations.Find(s => s.index == from.Station);
            string text = on
                ? string.Format(Localization.Extra("mpDistressNews", "⚠ {0} calls for help at {1} ({2})! The squad window's Help, or /assist {0}."),
                                from.DisplayName, st?.name ?? "?", st?.systemName ?? "?")
                : string.Format(Localization.Extra("mpDistressOver", "{0} is safe again."), from.DisplayName);
            foreach (var p in NetPlayer.All)
                if (p != null && p.IsSpawned && p != from && p.SquadId == from.SquadId) Notify(p.OwnerClientId, text);
            Debug.Log($"Server: {from.DisplayName} {(on ? "calls for help" : "is safe again")} at {st?.name}.");
        }

        /// <summary>A pilot of another faction paid the toll at 'station' (their game took the credits).</summary>
        [Rpc(SendTo.Server)]
        public void TollPaidRpc(int station, RpcParams rpc = default)
        {
            if (NetRateLimit.Allow(rpc.Receive.SenderClientId, NetRateLimit.Kind.Request)) NetFactions.OnTollPaid(rpc.Receive.SenderClientId, station);
        }

        /// <summary>Server: a notice for everyone (a claim).</summary>
        internal void Announce(string text)
        {
            if (IsServer && !string.IsNullOrEmpty(text)) NoticeRpc(text);
        }

        // ---- the station's Faction / Arena / Profile window (NetPanel) ----------------------------------------

        /// <summary>The window asks for its snapshot.</summary>
        [Rpc(SendTo.Server)]
        public void PanelRequestRpc(RpcParams rpc = default)
        {
            if (NetRateLimit.Allow(rpc.Receive.SenderClientId, NetRateLimit.Kind.Request)) NetPanel.Send(rpc.Receive.SenderClientId);
        }

        /// <summary>Server: a chunk of the snapshot to one player.</summary>
        internal void PanelChunk(ulong client, int seq, int part, int count, byte[] data)
        {
            if (IsServer) PanelChunkRpc(seq, part, count, data, RpcTarget.Single(client, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void PanelChunkRpc(int seq, int part, int count, byte[] data, RpcParams rpc = default) => NetPanel.OnChunk(seq, part, count, data);

        // ---- arena matches (NetArena / NetArenaClient) -----------------------------------------------------

        /// <summary>Server: the player goes into match 'id' (their game loads the arena).</summary>
        internal void ArenaStart(ulong client, int id, byte kind, int orbitId, int template, bool voids, int slot, int killLimit, float seconds)
        {
            if (IsServer) ArenaStartRpc(id, kind, orbitId, template, voids, slot, killLimit, seconds, RpcTarget.Single(client, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void ArenaStartRpc(int id, byte kind, int orbitId, int template, bool voids, int slot, int killLimit, float seconds, RpcParams rpc = default)
            => NetArenaClient.OnStart(id, kind, orbitId, template, voids, slot, killLimit, seconds);

        /// <summary>A player's game has the arena loaded.</summary>
        [Rpc(SendTo.Server)]
        public void ArenaReadyRpc(int id, RpcParams rpc = default)
        {
            if (NetRateLimit.Allow(rpc.Receive.SenderClientId, NetRateLimit.Kind.Request)) NetArena.OnReady(rpc.Receive.SenderClientId, id);
        }

        /// <summary>Server: the match's phase, seconds left, scores and a kill-feed line ("" = none) to one player.</summary>
        internal void ArenaState(ulong client, int id, byte phase, float left, ulong[] players, int[] kills, string feed)
        {
            if (IsServer) ArenaStateRpc(id, phase, left, players, kills, feed, RpcTarget.Single(client, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void ArenaStateRpc(int id, byte phase, float left, ulong[] players, int[] kills, string feed, RpcParams rpc = default)
            => NetArenaClient.OnState(id, phase, left, players, kills, feed);

        /// <summary>Server: the match is over (the result's text); the player's game goes home after showing it.</summary>
        internal void ArenaEnd(ulong client, int id, string text)
        {
            if (IsServer) ArenaEndRpc(id, text, RpcTarget.Single(client, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void ArenaEndRpc(int id, string text, RpcParams rpc = default) => NetArenaClient.OnEnd(id, text);

        /// <summary>Server: the player left their match (/leave): home at once.</summary>
        internal void ArenaLeave(ulong client)
        {
            if (IsServer) ArenaLeaveRpc(RpcTarget.Single(client, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void ArenaLeaveRpc(RpcParams rpc = default) => NetArenaClient.OnLeave();

        // ---- the event graphs' bar missions (EventMissions) ----

        /// <summary>This player docked at 'station': the event missions offered there.</summary>
        internal void RequestEventOffers(int station) => EventOffersUpRpc(station);

        [Rpc(SendTo.Server)]
        void EventOffersUpRpc(int station, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Command) || !NetGuard.Station(station)) return;
            string offers = EventMissions.Offers(station);
            if (offers.Length > 0 && offers.Length < 16000) EventOffersRpc(station, offers, RpcTarget.Single(client, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void EventOffersRpc(int station, string offers, RpcParams rpc = default) => EventMissions.ReceiveOffers(station, offers);

        /// <summary>The lounge's Okay on an event mission: the server starts it for the squad.</summary>
        internal void AcceptEventMission(string name, int station) => AcceptEventMissionRpc(name ?? "", station);

        [Rpc(SendTo.Server)]
        void AcceptEventMissionRpc(string name, int station, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Command) || name.Length == 0 || name.Length > 64 || !NetGuard.Station(station)) return;
            EventMissions.OnAccept(client, name, station);
        }

        /// <summary>Server: a team member's mission started ('payload': the offer and who took it) or ended ('payload': its name).</summary>
        internal void SendEventMission(ulong client, bool started, string payload) =>
            EventMissionRpc(started, payload ?? "", RpcTarget.Single(client, RpcTargetUse.Temp));

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void EventMissionRpc(bool started, string payload, RpcParams rpc = default)
        {
            if (started) EventMissions.OnStarted(payload);
            else EventMissions.OnEnded(payload);
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void JoinedSquadRpc(RpcParams rpc = default) => NetMissions.OnJoinedSquad();

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void LeftSquadRpc(RpcParams rpc = default) => NetMissions.OnLeftSquad();

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void SendMissionToRpc(ulong client, RpcParams rpc = default) => NetMissions.SendTo(client);

        /// <summary>A squad member's mission for one player (the squad's new member): that member joins the mission's team.</summary>
        [Rpc(SendTo.Server)]
        public void ShareMissionToRpc(string json, ulong client, RpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(sender, NetRateLimit.Kind.Mission)) return;
            var from = NetSquad.Find(sender);
            var to = NetSquad.Find(client);
            if (from == null || to == null || from == to || from.SquadId == 0 || to.SquadId != from.SquadId) return;
            if (!ParseMission(json, out var m)) { NetRateLimit.Reject(sender, "a malformed mission"); return; }
            var record = Register(m, from);   // a mission the holder had alone becomes the squad's
            if (record == null || record.ended) return;
            record.members.Add(client);
            ReceiveMissionRpc(json, from.DisplayName, true, RpcTarget.Single(client, RpcTargetUse.Temp));
        }

        /// <summary>The host is closing the session: its reason, before the connection goes (NetGame.Shutdown).</summary>
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        public void SessionEndingRpc(string reason) => NetGame.OnHostEnding(reason);

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

        /// <summary>The mission 'netId' whose team 'client' belongs to, null = none.</summary>
        MissionRecord RecordOf(long netId, ulong client)
        {
            if (netId == 0 || !missions.TryGetValue(netId, out var list)) return null;
            foreach (var r in list) if (r.members.Contains(client)) return r;
            return null;
        }

        /// <summary>The record of 'from's mission 'm', made now with 'from' in its team if there is none.</summary>
        MissionRecord Register(FreelanceMission m, NetPlayer from)
        {
            var record = RecordOf(m.netId, from.OwnerClientId);
            if (record != null) return record;
            if (!missions.TryGetValue(m.netId, out var list)) missions[m.netId] = list = new List<MissionRecord>();
            if (list.Count >= 8) return null;   // ids are random: more teams on one id is a client making them up
            int reward = Mathf.Clamp(m.reward, 0, MaxMissionPay);
            record = new MissionRecord
            {
                reward = reward, wager = m.type == MissionType.Challenge ? reward : 0, amount = Mathf.Clamp(m.amount, 0, 1023),
            };
            record.members.Add(from.OwnerClientId);
            list.Add(record);
            return record;
        }

        /// <summary>A mission as a client sends it: bounded in length, readable, with an id.</summary>
        static bool ParseMission(string json, out FreelanceMission m)
        {
            m = null;
            if (string.IsNullOrEmpty(json) || json.Length > NetGuard.MaxMissionJson) return false;
            try { m = JsonUtility.FromJson<FreelanceMission>(json); } catch (System.Exception) { return false; }
            return m != null && !m.IsEmpty && m.netId != 0;
        }

        /// <summary>A player's mission for their squadmates (a new one or an update).</summary>
        [Rpc(SendTo.Server)]
        public void ShareMissionRpc(string json, bool isNew, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Mission)) return;
            var from = NetSquad.Find(client);
            if (from == null || from.SquadId == 0) return;
            if (!ParseMission(json, out var m)) { NetRateLimit.Reject(client, "a malformed mission"); return; }
            long netId = m.netId;
            if (isNew)
            {
                // The host checks the acceptance too: the whole squad docked at the sender's station, and no other member's
                // new mission a moment ago (two accepting at once would swap missions): else the sender's is refused.
                string refusal = null;
                foreach (var p in NetPlayer.All)
                    if (p != null && p != from && p.IsSpawned && p.SquadId == from.SquadId && (p.Where != NetPlayer.Place.Hangar || p.Station != from.Station))
                        refusal = Localization.Extra("mpMissionSquadHere", "The whole squad must be docked at this station to accept a mission.");
                if (squadAccepts.TryGetValue(from.SquadId, out var last) && last.netId != netId && Time.unscaledTime - last.time < 3f)
                    refusal = string.Format(Localization.Extra("mpMissionAtOnce", "{0} accepted a squad mission at the same moment."), last.who);
                var record = refusal == null ? Register(m, from) : null;
                if (refusal == null && record == null) refusal = Localization.Extra("mpMissionRefused", "The squad mission could not be shared.");
                if (refusal != null) { MissionRefusedRpc(netId, refusal, RpcTarget.Single(from.OwnerClientId, RpcTargetUse.Temp)); return; }
                squadAccepts[from.SquadId] = (netId, Time.unscaledTime, from.DisplayName);
                // The squad, every member of which gets it now, is its team.
                foreach (var p in NetPlayer.All)
                    if (p != null && p.IsSpawned && p.SquadId == from.SquadId) record.members.Add(p.OwnerClientId);
            }
            else
            {
                // An update (the return trip): only of a mission the sender's team has.
                var record = RecordOf(netId, client);
                if (record == null || record.ended) return;
            }
            foreach (var p in NetPlayer.All)
                if (p != null && p != from && p.IsSpawned && p.SquadId == from.SquadId)
                    ReceiveMissionRpc(json, from.DisplayName, isNew, RpcTarget.Single(p.OwnerClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void ReceiveMissionRpc(string json, string from, bool isNew, RpcParams rpc = default) => NetMissions.Receive(json, from, isNew);

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void MissionRefusedRpc(long netId, string reason, RpcParams rpc = default) => NetMissions.OnRefused(netId, reason);

        /// <summary>The squad's mission (the sender's squad, and whoever of its team still holds that mission) other than the
        /// sender. (Before, anyone whose MissionHeld matched: a modified client could read a stranger's mission id and fail
        /// or abandon it for them.)</summary>
        List<NetPlayer> MissionTeam(NetPlayer from, long netId, MissionRecord record)
        {
            var list = new List<NetPlayer>();
            foreach (var p in NetPlayer.All)
                if (p != null && p != from && p.IsSpawned
                    && ((from.SquadId != 0 && p.SquadId == from.SquadId) || (record.members.Contains(p.OwnerClientId) && p.MissionHeld == netId)))
                    list.Add(p);
            return list;
        }

        /// <summary>A squad mission's end (NetMissions.Result: success with each member's share, failure, abandoned) for the
        /// rest of the squad: only from its team, once, the share bounded by the mission (a success's by its reward, a
        /// failure's by its Challenge wager, an abandon's none), so nobody can pay or charge other players at will.</summary>
        [Rpc(SendTo.Server)]
        public void MissionResultRpc(long netId, int result, int share, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Mission)) return;
            var from = NetSquad.Find(client);
            var record = from != null ? RecordOf(netId, client) : null;
            if (record == null || record.ended) return;   // a mission held alone (nobody else has it), or ended already
            switch (result)
            {
                case NetMissions.Success: share = Mathf.Clamp(share, 0, record.MaxShare); break;
                case NetMissions.Failure: share = Mathf.Clamp(share, 0, record.wager); break;
                case NetMissions.Abandoned: share = 0; break;
                default: NetRateLimit.Reject(client, $"mission result {result}"); return;
            }
            record.ended = true;
            record.endedAt = Time.unscaledTime;
            foreach (var p in MissionTeam(from, netId, record))
                MissionResultToRpc(netId, result, share, from.DisplayName, RpcTarget.Single(p.OwnerClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void MissionResultToRpc(long netId, int result, int share, string from, RpcParams rpc = default)
            => NetMissions.OnResult(netId, result, share, from);

        /// <summary>The squad mission's shared progress ('status' + delta: delivered ore, the captured container, the Informer's
        /// 1 / 1000): only from its team, in the steps a game sends.</summary>
        [Rpc(SendTo.Server)]
        public void MissionStatusRpc(long netId, int delta, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Mission)) return;
            var from = NetSquad.Find(client);
            var record = from != null ? RecordOf(netId, client) : null;
            if (record == null || record.ended) return;
            if (delta < 1 || delta > 1000) { NetRateLimit.Reject(client, $"mission progress {delta}"); return; }
            foreach (var p in MissionTeam(from, netId, record))
                MissionStatusToRpc(netId, delta, RpcTarget.Single(p.OwnerClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void MissionStatusToRpc(long netId, int delta, RpcParams rpc = default) => NetMissions.OnStatus(netId, delta);

        /// <summary>Host: 'gone' disconnects; what they carried for the mission (containers, passengers) goes to a member of its
        /// team still holding it (their squad first), so the squad keeps a mission it can finish. Each count at most the
        /// mission's amount (MissionCargo is the leaving game's word).</summary>
        public void HandOverMission(NetPlayer gone)
        {
            if (!IsServer || gone == null || gone.MissionHeld == 0 || gone.MissionCargo == 0) return;
            long netId = gone.MissionHeld;
            var record = RecordOf(netId, gone.OwnerClientId);
            if (record == null || record.ended) return;
            NetPlayer to = null;
            foreach (var p in NetPlayer.All)
                if (p != null && p != gone && p.IsSpawned && p.MissionHeld == netId && record.members.Contains(p.OwnerClientId)
                    && (to == null || (p.SquadId == gone.SquadId && to.SquadId != gone.SquadId))) to = p;
            if (to == null) return;
            int cap = Mathf.Clamp(record.amount, 1, 1023), cargo = gone.MissionCargo;
            int a = Mathf.Min(cargo & 1023, cap), b = Mathf.Min(cargo >> 10 & 1023, cap), c = Mathf.Min(cargo >> 20 & 1023, cap);
            TakeMissionCargoRpc(netId, a | b << 10 | c << 20, gone.DisplayName, RpcTarget.Single(to.OwnerClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void TakeMissionCargoRpc(long netId, int cargo, string from, RpcParams rpc = default) => NetMissions.TakeCargo(netId, cargo, from);

        // ---- the shared shop stock (NetStock) ------------------------------------------------------------------

        /// <summary>Host: the stations whose stock changed this frame: sent once to the players docked there (Update), not once
        /// per traded unit (a held arrow trades 5 units a frame).</summary>
        readonly HashSet<int> dirtyStock = new HashSet<int>();
        /// <summary>Host: the dealer ships each player reserved at each station and hasn't traded or given back yet: a trade
        /// may only take a reserved row off the list, and only put back that row or the player's own old hull.</summary>
        readonly Dictionary<(ulong client, int station), List<int>> reservations = new Dictionary<(ulong, int), List<int>>();

        /// <summary>A player docked at 'station': the host's stock there (made now if it has none).</summary>
        [Rpc(SendTo.Server)]
        public void StockRequestRpc(int station, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Request) || !NetGuard.Station(station)) return;   // no lists for made-up stations
            StockRpc(station, NetStock.HostGet(station), RpcTarget.Single(client, RpcTargetUse.Temp));
        }

        /// <summary>A player bought (-1, for 'price') or sold (+1) one unit of an item at 'station', docked there; a unit no
        /// longer there goes back.</summary>
        [Rpc(SendTo.Server)]
        public void StockItemRpc(int station, int item, int delta, int price, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Trade)) return;
            // One unit a message, or several at once (a shop's Buy all / Sell all: Hangar.EndBatch); 'price' is one unit's.
            if (delta == 0 || Mathf.Abs(delta) > MaxTradeUnits || price < 0 || !NetGuard.Station(station) || !NetGuard.Item(item) || !NetGuard.DockedAt(client, station))
            {
                NetRateLimit.Reject(client, $"a trade of {delta} x item {item} at {station}");
                return;
            }
            int done = NetStock.HostItem(station, item, delta);
            if (delta < 0 && done < -delta)
                ItemRefusedRpc(station, item, price, -delta - done, RpcTarget.Single(client, RpcTargetUse.Temp));
            if (delta < 0 && done > 0 && NetProfiles.Enabled) NetFactions.OnPurchase(client, station, price * done);   // a faction station's tax
            dirtyStock.Add(station);
        }

        /// <summary>The most units one trade message may move (a whole hold or stock row at once).</summary>
        const int MaxTradeUnits = 10000;

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void ItemRefusedRpc(int station, int item, int price, int count, RpcParams rpc = default) => NetStock.ItemRefused(station, item, price, count);

        /// <summary>A player docked at 'station' wants the dealer's ship 'ship': theirs if it is still there (then off the list).</summary>
        [Rpc(SendTo.Server)]
        public void ReserveShipRpc(int station, int ship, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Reserve)) return;
            if (!NetGuard.Station(station) || !NetGuard.Ship(ship) || !NetGuard.DockedAt(client, station))
            {
                NetRateLimit.Reject(client, $"a reservation of ship {ship} at {station}");
                return;
            }
            bool ok = NetStock.HostReserveShip(station, ship);
            if (ok)
            {
                if (!reservations.TryGetValue((client, station), out var list)) reservations[(client, station)] = list = new List<int>();
                list.Add(ship);
            }
            ReserveResultRpc(station, ship, ok, RpcTarget.Single(client, RpcTargetUse.Temp));
            if (ok) dirtyStock.Add(station);
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void ReserveResultRpc(int station, int ship, bool ok, RpcParams rpc = default) => NetStock.ReserveResult(station, ship, ok);

        /// <summary>A player's ship trade at 'station': the dealer's row 'removed' became 'added' (-1 = none). Only a row this
        /// player reserved goes (it is off the list already), and only that row (not bought after all) or the player's own old
        /// hull (a trade-in) comes: no dealer ships made up or wiped for everyone.</summary>
        [Rpc(SendTo.Server)]
        public void StockShipRpc(int station, int removed, int added, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Reserve)) return;
            var from = NetSquad.Find(client);
            if (from == null || !NetGuard.Station(station) || !NetGuard.DockedAt(client, station)
                || (removed != -1 && !NetGuard.Ship(removed)) || (added != -1 && !NetGuard.Ship(added)))
            {
                NetRateLimit.Reject(client, $"a ship trade {removed} -> {added} at {station}");
                return;
            }
            reservations.TryGetValue((client, station), out var reserved);
            bool ok;
            if (removed >= 0) ok = reserved != null && reserved.Remove(removed) && (added < 0 || added == from.ShipIndex || added == from.PreviousShip);
            else ok = added >= 0 && reserved != null && reserved.Remove(added);   // the reserved row back: not bought after all
            if (!ok) { NetRateLimit.Reject(client, $"an unreserved ship trade {removed} -> {added} at {station}"); return; }
            if (NetStock.HostShip(station, removed, added)) dirtyStock.Add(station);
        }

        /// <summary>Host: the stock of 'station' for every player docked there.</summary>
        internal void BroadcastStock(int station)
        {
            string text = NetStock.HostGet(station);
            foreach (var p in NetPlayer.All)
                if (p != null && p.IsSpawned && p.InHangar && p.Station == station)
                    StockRpc(station, text, RpcTarget.Single(p.OwnerClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void StockRpc(int station, string text, RpcParams rpc = default) => NetStock.Apply(station, text);

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

        /// <summary>An orbit authority shows its ship 'localId' (NetOrbit's list) of 'station' to the others.</summary>
        [Rpc(SendTo.Server)]
        public void SpawnProxyRpc(int station, int localId, RpcParams rpc = default) => RequestSpawn(rpc.Receive.SenderClientId, station, localId, false);

        /// <summary>An orbit authority shows its crate 'localId' of 'station' to the others.</summary>
        [Rpc(SendTo.Server)]
        public void SpawnCrateRpc(int station, int localId, RpcParams rpc = default) => RequestSpawn(rpc.Receive.SenderClientId, station, localId, true);

        /// <summary>A spawn request: limited per client (a flood of objects for everyone), only for the orbit the sender flies
        /// in (no objects planted in other orbits), at most MaxProxies / MaxCrates owned at once.</summary>
        void RequestSpawn(ulong client, int station, int localId, bool crate)
        {
            if (!NetRateLimit.Allow(client, crate ? NetRateLimit.Kind.CrateSpawn : NetRateLimit.Kind.ProxySpawn)) return;
            if (localId < 0 || !NetGuard.Orbit(station) || NetSquad.Find(client) == null) { NetRateLimit.Reject(client, $"a spawn {localId} in orbit {station}"); return; }
            owned.TryGetValue(client, out var n);
            if (crate ? n.crates >= MaxCrates : n.proxies >= MaxProxies) { NetRateLimit.Reject(client, crate ? "too many crates" : "too many ships"); return; }
            var request = new SpawnRequest { client = client, station = station, localId = localId, crate = crate, time = Time.unscaledTime };
            if (NetGuard.InOrbit(client, station)) Spawn(request);
            else if (pendingSpawns.Count < MaxPendingSpawns) pendingSpawns.Add(request);
        }

        void Spawn(SpawnRequest r)
        {
            owned.TryGetValue(r.client, out var n);
            if (r.crate)
            {
                if (cratePrefab == null) return;
                var go = Instantiate(cratePrefab);
                go.GetComponent<NetCrate>().Init(r.station, r.localId);
                go.GetComponent<NetworkObject>().SpawnWithOwnership(r.client, false);
                n.crates++;
            }
            else
            {
                if (proxyPrefab == null) return;
                var go = Instantiate(proxyPrefab);
                go.GetComponent<NetProxy>().Init(r.station, r.localId);
                var obj = go.GetComponent<NetworkObject>();
                obj.DontDestroyWithOwner = true;   // an owner who disconnects leaves it to the host: the others can take it over
                obj.SpawnWithOwnership(r.client, false);
                n.proxies++;
            }
            owned[r.client] = n;
        }

        /// <summary>The waiting spawn requests: their senders arrived in that orbit, or they go.</summary>
        void UpdatePendingSpawns()
        {
            if (pendingSpawns.Count == 0) return;
            float now = Time.unscaledTime;
            pendingSpawns.RemoveAll(r =>
            {
                if (NetGuard.InOrbit(r.client, r.station)) { Spawn(r); return true; }
                if (now - r.time < SpawnWaitSeconds) return false;
                NetRateLimit.Reject(r.client, $"a spawn in orbit {r.station}, where it isn't");
                return true;
            });
        }

        /// <summary>A player took a ship / junk / crate of another game over (NetOrbit, FreelanceOrbit.Promote): its old copy
        /// goes for everyone, and when its creator is still in that orbit their own ship goes too (TakenOverRpc). Only a
        /// player flying in that orbit takes its objects over, and from a creator still there only a squadmate the creator's
        /// mission objects (a mission orbit's takeover): no wiping other players' ships from anywhere.</summary>
        [Rpc(SendTo.Server)]
        public void AdoptedRpc(ulong objectId, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Adopt)) return;
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(objectId, out var obj) || obj == null || !obj.IsSpawned) return;
            var proxy = obj.GetComponent<NetProxy>();
            var crate = proxy == null ? obj.GetComponent<NetCrate>() : null;
            if (proxy == null && crate == null) { NetRateLimit.Reject(client, "a takeover of no ship or crate"); return; }
            int station = proxy != null ? proxy.Station : crate.Station;
            ulong creator = proxy != null ? proxy.Creator : obj.OwnerClientId;
            if (creator == client) return;
            if (!NetGuard.InOrbit(client, station)) { NetRateLimit.Reject(client, $"a takeover in orbit {station}, where it isn't"); return; }
            if (NetOrbit.InOrbit(creator, station) && obj.OwnerClientId == creator)
            {
                // A mission orbit's takeover (FreelanceOrbit.Promote): the runner's squadmate, or anyone once the runner no
                // longer runs that mission (it left the squad and with it the mission).
                var owner = NetSquad.Find(creator);
                bool missionObject = proxy != null ? proxy.IsMissionShip || proxy.IsJunk : crate.IsMissionCrate;
                if (!missionObject || owner == null || (!NetSquad.SameClient(client, owner) && owner.MissionRun != 0))
                {
                    NetRateLimit.Reject(client, "a takeover of a ship its game still runs");
                    return;
                }
                if (proxy != null) proxy.TakenOverRpc();
                else crate.TakenOverRpc();
                return;   // the creator's game drops its own (NetOrbit's scan despawns it)
            }
            obj.Despawn();
        }

        // ---- a hangar's NPC ships (NetHangar) -------------------------------------------------------------------

        /// <summary>The game running 'station's hangar (docked there): an NPC ship lands (key, ship, yaw) or takes off (key):
        /// the others docked there do the same.</summary>
        [Rpc(SendTo.Server)]
        public void HangarNpcRpc(int station, bool landing, int key, int ship, float yaw, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Hangar)) return;
            if (!NetGuard.Station(station) || !NetGuard.DockedAt(client, station) || (landing && (!NetGuard.Ship(ship) || !NetGuard.Finite(yaw))))
            {
                NetRateLimit.Reject(client, $"a hangar ship at {station}");
                return;
            }
            foreach (var p in NetPlayer.All)
                if (p != null && p.IsSpawned && p.OwnerClientId != client && p.InHangar && p.Station == station)
                    HangarNpcToRpc(landing, key, ship, yaw, RpcTarget.Single(p.OwnerClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void HangarNpcToRpc(bool landing, int key, int ship, float yaw, RpcParams rpc = default) => NetHangar.Current?.OnRemoteNpc(landing, key, ship, yaw);

        /// <summary>A player docked at 'station' wants its NPC ships as they are: the running game sends them.</summary>
        [Rpc(SendTo.Server)]
        public void HangarSnapshotRequestRpc(int station, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Request) || !NetGuard.Station(station)) return;
            NetPlayer runner = null;
            foreach (var p in NetPlayer.All)
                if (p != null && p.IsSpawned && p.OwnerClientId != client && p.InHangar && p.Station == station && p.HangarRun
                    && (runner == null || p.OwnerClientId < runner.OwnerClientId)) runner = p;
            if (runner != null) HangarSnapshotAskRpc(client, RpcTarget.Single(runner.OwnerClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void HangarSnapshotAskRpc(ulong requester, RpcParams rpc = default) => NetHangar.Current?.SendSnapshot(requester);

        /// <summary>The running game's answer: only for a player docked where the sender is, bounded in length.</summary>
        [Rpc(SendTo.Server)]
        public void HangarSnapshotRpc(ulong requester, string data, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Hangar)) return;
            var to = NetSquad.Find(requester);
            if (to == null || to.OwnerClientId == client || !to.InHangar || !NetGuard.DockedAt(client, to.Station)
                || (data != null && data.Length > NetGuard.MaxSnapshot))
            {
                NetRateLimit.Reject(client, "a hangar snapshot");
                return;
            }
            HangarSnapshotToRpc(data ?? "", RpcTarget.Single(requester, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void HangarSnapshotToRpc(string data, RpcParams rpc = default) => NetHangar.Current?.OnSnapshot(data);

        /// <summary>The Kaamo siege won in a player's game (flying in the Kaamo Club's orbit): the others there (their siege
        /// view) win it too.</summary>
        [Rpc(SendTo.Server)]
        public void SiegeWonRpc(int station, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Siege)) return;
            if (station != KaamoClub.Station || !NetGuard.InOrbit(client, station)) { NetRateLimit.Reject(client, $"a siege won at {station}"); return; }
            // The news: everyone fighting there broke it.
            var heroes = new List<string>();
            foreach (var p in NetPlayer.All)
                if (p != null && p.IsSpawned && p.InSpace && p.Station == station) heroes.Add(p.DisplayName);
            NetNews.Post(NetNews.Kind.Defense, $"{NetNews.Names(heroes)} {(heroes.Count == 1 ? "breaks" : "break")} the pirate siege of the Kaamo Club ({NetNews.Place(station)})",
                         station, "kaamo", 1800f);
            foreach (var p in NetPlayer.All)
                if (p != null && p.IsSpawned && p.OwnerClientId != client && p.InSpace && p.Station == station)
                    SiegeWonToRpc(RpcTarget.Single(p.OwnerClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void SiegeWonToRpc(RpcParams rpc = default) => World.KaamoSiege.Current?.OnRemoteWin();

        // ---- the sector's news (NetNews) ----------------------------------------------------------------------

        /// <summary>Server: a news item for everyone.</summary>
        internal void BroadcastNews(string packed)
        {
            if (IsServer) NewsRpc(packed);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void NewsRpc(string packed) => NetNews.OnReceive(packed, false);

        /// <summary>Server: a news item to one player ('reset': their list starts over first).</summary>
        internal void SendNews(ulong client, string packed, bool reset)
        {
            if (IsServer) NewsToRpc(packed ?? "", reset, RpcTarget.Single(client, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void NewsToRpc(string packed, bool reset, RpcParams rpc = default) => NetNews.OnReceive(packed, reset);

        /// <summary>An orbit's authority: its raiders are all down, 'killers' downed them (NetOrbit). Checked: the sender runs
        /// that orbit, the pilots exist and are there, the numbers are sane; one item per orbit and raid (NetNews).</summary>
        [Rpc(SendTo.Server)]
        public void DefenseReportRpc(int station, int race, int kills, ulong[] killers, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Kill)) return;
            var from = NetSquad.Find(client);
            if (from == null || !from.OrbitAuthority || !NetGuard.InOrbit(client, station) || NetArena.IsArenaOrbit(station)) return;
            if (kills < NetNews.MinDefenseKills || kills > 60 || killers == null || killers.Length == 0 || killers.Length > 16)
            {
                NetRateLimit.Reject(client, $"a defence report of {kills} kills");
                return;
            }
            var names = new List<string>();
            foreach (ulong k in killers)
            {
                var p = NetSquad.Find(k);
                if (p != null && p.Station == station && !names.Contains(p.DisplayName)) names.Add(p.DisplayName);
            }
            if (names.Count > 0) NetNews.Defended(station, race, kills, names);
        }

        /// <summary>Its owner is done with a NetProxy / NetCrate (the ship left or died for good, the crate was taken). Only
        /// those: a client's own player object (or anything else) isn't its to remove.</summary>
        [Rpc(SendTo.Server)]
        public void DespawnRpc(ulong objectId, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Despawn)) return;
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(objectId, out var obj) || obj == null || !obj.IsSpawned
                || obj.OwnerClientId != client) return;
            if (obj.GetComponent<NetProxy>() == null && obj.GetComponent<NetCrate>() == null) { NetRateLimit.Reject(client, "a despawn of no ship or crate"); return; }
            obj.Despawn();
        }

        void Update()
        {
            NetStock.Flush();   // the shared stock that arrived, applied between frames (every player)
            NetTradeClient.Tick();   // this player's trade offer goes out, kept within the hold
            if (!IsServer || !IsSpawned) return;
            NetTrade.Tick();      // trade requests and trades that ran out, sides that left the hangar
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
            EventRunner.Tick();   // a running event
            if ((sweepTimer -= Time.unscaledDeltaTime) > 0f) return;
            sweepTimer = SweepSeconds;
            NetRateLimit.Tick();   // the clients that kept flooding go
            NetStock.HostTick(this);   // the stock resets
            DissolveSingles();   // a squadmate who disconnected
            if ((pruneTimer -= SweepSeconds) <= 0f) { pruneTimer = PruneSeconds; Prune(); }
            // An orbit's objects go once their owner isn't in that orbit any more.
            var owners = new Dictionary<ulong, NetPlayer>();
            foreach (var p in NetPlayer.All) if (p != null && p.IsSpawned) owners[p.OwnerClientId] = p;
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
                bool orphan = proxy != null && proxy.Orphan;   // its owner left the session: the host holds it without a ship
                if (!orphan && owners.TryGetValue(obj.OwnerClientId, out var owner) && owner.InSpace && owner.Station == station) { staleSince.Remove(obj.NetworkObjectId); continue; }
                // Players still in that orbit take its ships over (NetOrbit): they keep the old proxies a few seconds.
                bool watched = false;
                foreach (var p in owners.Values) if (p.InSpace && p.Station == station) { watched = true; break; }
                if (!staleSince.TryGetValue(obj.NetworkObjectId, out float since)) staleSince[obj.NetworkObjectId] = since = Time.unscaledTime;
                if (!watched || Time.unscaledTime - since > 5f) stale.Add(obj);
            }
            foreach (var obj in stale) { staleSince.Remove(obj.NetworkObjectId); if (obj.IsSpawned) obj.Despawn(); }
        }

        /// <summary>Host, every PruneSeconds: the bookkeeping of players who left and of the past goes (the hits, invitations,
        /// reservations and mission records would otherwise grow for the whole session).</summary>
        void Prune()
        {
            float now = Time.unscaledTime;
            var oldHits = new List<(ulong, ulong)>();
            foreach (var kv in hits) if (now - kv.Value > 60f) oldHits.Add(kv.Key);
            foreach (var k in oldHits) hits.Remove(k);
            var oldInvites = new List<(ulong, ulong)>();
            foreach (var kv in invites) if (now - kv.Value > NetSquad.InviteSeconds + InviteGraceSeconds) oldInvites.Add(kv.Key);
            foreach (var k in oldInvites) invites.Remove(k);
            var goneReservations = new List<(ulong, int)>();
            foreach (var kv in reservations) if (kv.Value.Count == 0 || NetSquad.Find(kv.Key.client) == null) goneReservations.Add(kv.Key);
            foreach (var k in goneReservations) reservations.Remove(k);
            var goneMissions = new List<long>();
            foreach (var kv in missions)
            {
                // A record goes a minute after its result (a second result of the same mission is dropped meanwhile), or
                // once nobody of its team is in the session any more.
                kv.Value.RemoveAll(r =>
                {
                    if (r.ended) return now - r.endedAt > 60f;
                    foreach (var id in r.members) if (NetSquad.Find(id) != null) return false;
                    return true;
                });
                if (kv.Value.Count == 0) goneMissions.Add(kv.Key);
            }
            foreach (var k in goneMissions) missions.Remove(k);
        }
    }
}
