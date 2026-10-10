// NetGame.cs
// Remake-only multiplayer (Netcode for GameObjects over Unity Transport): one shared game world. Online sessions go
// through Unity Relay (Multiplayer Services: the host gets a join code, the others join with it, no address or port
// forwarding; anonymous Unity Authentication), listed in the server browser unless the host keeps it to its code
// (NetLobby, Unity Lobby); local ones by direct IP, port 7777.
// The main menu's Multiplayer panel hosts or joins; every player starts a fresh free-play game docked at Dis (70) and then
// plays it like single player: their own scenes (Space for their orbit, Station for their hangar), economy, jumps and
// docking. No scene synchronisation: the network objects live in DontDestroyOnLoad and each player shows only what is
// where they are.
//   NetState      the world (spawned by the host): the world seed (every orbit's asteroid field), destroyed asteroids per
//                 station, the spawn / despawn requests of the players who run an orbit.
//   NetPlayer     one per player: where they are (station; in space, in the hangar, taking off), their ship, pose, name,
//                 hull; in space the others there see the ship, in a hangar the others docked there see it land, park
//                 and take off (NetHangar, HangarTraffic's guests).
//   NetOrbit      a player's own Space level: the first player in an orbit runs its NPC traffic and crates (the orbit
//                 authority) and shows them to the others there as NetProxy / NetCrate objects it owns; the others' levels
//                 build no traffic. Asteroids: the same field for everyone, destruction shared.
//   Shots, hits on proxies and crate claims go between the players in the same orbit (NetShotSender / NetShotMirror,
//   NetProxy, NetCrate). Not yet: handing an orbit's NPCs over when its authority leaves (they go with it),
//   player-versus-player damage, NPCs attacking other players than the authority. The players can't die (game over
//   would load a save), nothing is saved to the single-player slots (SaveGame), and the game never pauses
//   (Time.timeScale stays 1). A dedicated server keeps player profiles instead (NetProfiles / NetProfileClient).
// Network prefabs: Resources/GoF2Net (GoF2 > Build > Network Prefabs).

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using GoF2Remake.Data;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetGame
    {
        public const ushort DefaultPort = 7777;

        /// <summary>The port this device hosts on (the menu's port field, PlayerPrefs "mp_port").</summary>
        public static ushort HostPort
        {
            get { int p = PlayerPrefs.GetInt("mp_port", DefaultPort); return p >= 1024 && p <= 65535 ? (ushort)p : DefaultPort; }
            set => PlayerPrefs.SetInt("mp_port", value);
        }
        /// <summary>Where every session game starts docked (and a profile without a station goes): Dis (70), the
        /// Supernova add-on's start.</summary>
        public const int Station = 70;
        public const string PrefabFolder = "GoF2Net";
        public static readonly string[] PrefabNames = { "NetPlayer", "NetProxy", "NetState", "NetCrate" };
        const string StationScene = "Station", MenuScene = "MainMenu";

        static NetworkManager manager;
        static readonly HashSet<ulong> playersSpawned = new HashSet<ulong>();
        static bool worldEntered, transportConnected, sessionGame;

        /// <summary>A session runs (hosting, or a client connecting / connected); not while the host is closing it.</summary>
        public static bool Active => manager != null && manager.IsListening && !closing;

        /// <summary>The game in memory is a session's (from PrepareSession until the main menu opens after it), even once the
        /// connection is gone: SaveGame never saves it, and a level loaded after the session ended goes to the menu.</summary>
        public static bool SessionGame => sessionGame || Active;

        /// <summary>The session is gone but its game is still loaded (SpaceLevel / StationLevel then go to the main menu).</summary>
        public static bool SessionLost => sessionGame && !Active;

        /// <summary>The main menu opened: a session that has ended leaves nothing behind.</summary>
        public static void OnMainMenu()
        {
            if (!Active) { sessionGame = false; NetMods.End(); NetArenaClient.Reset(); }   // single player's mods back, no arena match
        }

        // Play mode without a domain reload keeps statics: a fresh start (also builds, where it changes nothing).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            manager = null;
            closing = quitAfter = worldEntered = transportConnected = sessionGame = lostHandled = false;
            Dedicated = false;
            hostEndReason = null;
            JoinCode = null;
            hostAllocation = null;
            listPending = false;
            playersSpawned.Clear();
        }
        public static bool IsServer => Active && manager.IsServer;
        public static ulong LocalId => Active ? manager.LocalClientId : 0;
        /// <summary>Connected to the host (or hosting).</summary>
        public static bool Connected => Active && (manager.IsServer || manager.IsConnectedClient);
        static Database db;
        /// <summary>The game data, loaded once for the multiplayer code's look-ups (turrets, models).</summary>
        internal static Database Db => db ??= Database.Load();

        /// <summary>The session's mods changed (NetMods): the tables are read again on next use.</summary>
        internal static void ResetDb() => db = null;

        /// <summary>The world's seed (the host picks it, NetState carries it to the clients).</summary>
        public static int Seed { get; private set; }

        /// <summary>An orbit's asteroid seed: the same field for every player there, a different one per station.</summary>
        public static int OrbitSeed(int station) => unchecked(Seed * 31 + (station + 1000) * 7919);
        public const int MaxNameLength = 20;

        /// <summary>The player's name, shown to the others (lock plate, NetPlayer); the menu's name field, PlayerPrefs
        /// "mp_name". Empty = "Player N".</summary>
        public static string PlayerName
        {
            get => NameOverride ?? PlayerPrefs.GetString("mp_name", "");
            set => PlayerPrefs.SetString("mp_name", Clean(value));
        }

        /// <summary>A name for this process only (not saved), null = PlayerName's own: -mpname on the command line.</summary>
        public static string NameOverride = CommandLineValue("-mpname");

        /// <summary>-mpjoin &lt;address&gt; on the command line (testing): the main menu skips its intro and joins, null = none.</summary>
        public static readonly string AutoJoinAddress = CommandLineValue("-mpjoin");

        /// <summary>Testing, development builds only: -mpdock docks this player once, a few seconds into their first flight;
        /// -mpaccept accepts squad invitations while docked (NetPlayer), so the real squad flow runs without a hand on it;
        /// -mphost hosts from the main menu (a phone: adb shell am start ... -e unity "-mphost");
        /// -mptrade answers trades (NetTradeClient): accepts a request while docked, offers 500 credits, accepts once the other
        /// side has.</summary>
        public static readonly bool TestDock = TestFlag("-mpdock"), TestAccept = TestFlag("-mpaccept"), TestHost = TestFlag("-mphost"),
                                    TestTrade = TestFlag("-mptrade");

        static bool TestFlag(string flag) =>
            Debug.isDebugBuild && Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

        /// <summary>The value after 'flag' on the command line, null = none. Unity drops empty arguments (a launcher's
        /// -password "" arrives as -password -maxplayers ...), so another option right after it (a dash and a letter) means
        /// no value; a lone "-" (-logFile -) is a value.</summary>
        internal static string CommandLineValue(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) continue;
                string next = args[i + 1];
                return next.Length > 1 && next[0] == '-' && char.IsLetter(next[1]) ? null : next;
            }
            return null;
        }

        /// <summary>Trimmed, at most MaxNameLength characters, no rich-text tags (the lock plate is a rich-text Label).</summary>
        public static string Clean(string name)
        {
            name = (name ?? "").Replace("<", "").Replace(">", "").Trim();
            return name.Length > MaxNameLength ? name.Substring(0, MaxNameLength) : name;
        }

        /// <summary>A session ended while playing: the main menu opens the Multiplayer panel with a popup of Status.</summary>
        public static bool PopupPending { get; private set; }

        /// <summary>The main menu: the pending popup's text, once.</summary>
        public static bool TakePopup(out string text)
        {
            text = Status;
            bool pending = PopupPending;
            PopupPending = false;
            return pending && !string.IsNullOrEmpty(text);
        }

        /// <summary>Why the last session ended or failed ("" = none), shown by the menu.</summary>
        public static string Status { get; private set; } = "";

        // ---- Unity Relay (online sessions) ------------------------------------------------------------------

        /// <summary>The default and the largest session (players, a host's own included; Relay takes at most 100).</summary>
        public const int DefaultMaxPlayers = 16, MaxPlayersLimit = 100;

        /// <summary>The session's size, a host's own player included (the Host card, -maxplayers): Relay's connections, the
        /// server browser's "x / max", and the connection approval turns away a player past it (a local session too).</summary>
        public static int MaxPlayers
        {
            get => maxPlayers;
            set => maxPlayers = Mathf.Clamp(value, 2, MaxPlayersLimit);
        }
        static int maxPlayers = DefaultMaxPlayers;

        /// <summary>The game's version as shown (the build's date and time, BuildVersion); "editor" in the Editor.</summary>
        public static string Version => Application.isEditor ? "editor" : Application.version;

        /// <summary>What the sessions compare (the connection approval, the server browser): the code's fingerprint, the same
        /// for every build of the same code whenever it was built (BuildVersion.Fingerprint); "editor" in the Editor.</summary>
        public static string Protocol => GoF2Remake.UI.BuildVersion.Fingerprint;

        // The listing PrepareOnlineHost asked for (StartHost / StartServer publish it once running).
        static bool listPending;
        static string listName;
        const string RelayConnection = "dtls";   // encrypted UDP (WSS is only for web players)

        /// <summary>The online session's join code (the host's, and the code a client joined with), null = a local session.</summary>
        public static string JoinCode { get; private set; }
        static Allocation hostAllocation;

        /// <summary>"ABC123": a Relay join code (6 letters / digits, no dots or colons), not an address.</summary>
        public static bool IsJoinCode(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length != 6) return false;
            foreach (char c in text) if (!char.IsLetterOrDigit(c) || c > 'z') return false;
            return true;
        }

        /// <summary>Unity Services up and this player signed in anonymously (a profile per -mpname, so two games on one
        /// machine are two players).</summary>
        public static async Task SignInForOnline()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                var options = new InitializationOptions();
                string profile = Profile();
                if (profile != null) options.SetProfile(profile);
                await UnityServices.InitializeAsync(options);
            }
            while (UnityServices.State == ServicesInitializationState.Initializing) await Task.Yield();
            // A sign-in that expired while offline (its refresh failed): signed out first, the cached session token kept,
            // so the same anonymous player signs in again (a dedicated server starting again after a lost connection).
            if (AuthenticationService.Instance.IsExpired) AuthenticationService.Instance.SignOut(false);
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        static string Profile()
        {
            string name = DedicatedServer.Enabled ? "server" : NameOverride;
            if (string.IsNullOrEmpty(name)) return null;
            var sb = new System.Text.StringBuilder();
            foreach (char c in name) if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_') sb.Append(c);
            return sb.Length == 0 ? null : sb.ToString(0, Math.Min(sb.Length, 30));
        }

        /// <summary>Before hosting online: signs in, reserves a Relay allocation and its join code (StartHost / StartServer
        /// then use it) for MaxPlayers; 'listName' non-null = listed in the server browser under that name. False = Status
        /// says why.</summary>
        public static async Task<bool> PrepareOnlineHost(string listName = null)
        {
            Status = "";
            hostAllocation = null;
            JoinCode = null;
            listPending = listName != null;
            NetGame.listName = listName;
            try
            {
                await SignInForOnline();
                // Relay's connections are the others: a player host is one of the session's players.
                int connections = DedicatedServer.Enabled ? MaxPlayers : MaxPlayers - 1;
                var allocation = await RelayService.Instance.CreateAllocationAsync(Mathf.Clamp(connections, 1, MaxPlayersLimit));
                JoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                hostAllocation = allocation;
                return true;
            }
            catch (Exception e)
            {
                Status = OnlineError(e, null);
                Debug.LogWarning("NetGame: Relay allocation failed: " + e);
                JoinCode = null;
                return false;
            }
        }

        /// <summary>Joins an online session by its join code: signs in, joins its Relay allocation, connects. False = Status
        /// says why; true = connecting (as StartClient).</summary>
        public static async Task<bool> StartClientOnline(string code)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            PrepareSession();
            hostEndReason = null;
            JoinAllocation join;
            try
            {
                await SignInForOnline();
                join = await RelayService.Instance.JoinAllocationAsync(code);
            }
            catch (Exception e)
            {
                Status = OnlineError(e, code);
                Debug.LogWarning("NetGame: Relay join failed: " + e);
                sessionGame = false;   // no session started
                return false;
            }
            var m = EnsureManager();
            Transport.SetRelayServerData(join.ToRelayServerData(RelayConnection));
            m.NetworkConfig.ConnectionData = Payload();
            if (!m.StartClient())
            {
                Status = Localization.Extra("mpJoinFailed", "Could not connect.");
                Shutdown();
                return false;
            }
            JoinCode = code;
            return true;
        }

        /// <summary>NetLobby: a failed request's reason for the menu.</summary>
        internal static void SetOnlineError(Exception e) => Status = OnlineError(e, null);

        /// <summary>The name an online host lists its session under: the pilot's.</summary>
        public static string DefaultSessionName()
        {
            string n = Clean(PlayerName);
            return string.Format(Localization.Extra("mpSessionName", "{0}'s universe"), n.Length > 0 ? n : Localization.Extra("mpAPilot", "A pilot"));
        }

        /// <summary>Running online and asked to be listed: into the server browser.</summary>
        static void PublishIfListed(bool dedicated)
        {
            bool list = listPending && JoinCode != null;
            listPending = false;
            if (!list) return;
            string host = dedicated ? Localization.Extra("mpDedicated", "Dedicated server") : (Clean(PlayerName).Length > 0 ? Clean(PlayerName) : "Player 1");
            // The lobby's size counts every player, a host's own too (like the browser's player count).
            NetLobby.Publish(listName ?? DefaultSessionName(), host, JoinCode, MaxPlayers, dedicated);
        }

        static string OnlineError(Exception e, string code)
        {
            if (e is RelayServiceException r && code != null
                && (r.Reason == RelayExceptionReason.JoinCodeNotFound || r.Reason == RelayExceptionReason.EntityNotFound || r.Reason == RelayExceptionReason.AllocationNotFound))
                return string.Format(Localization.Extra("mpNoCode", "No game found with join code {0}."), code);
            if (e is RequestFailedException f && (f.ErrorCode == CommonErrorCodes.TransportError || f.ErrorCode == CommonErrorCodes.Timeout || f.ErrorCode == CommonErrorCodes.ServiceUnavailable))
                return Localization.Extra("mpOffline", "Can't reach Unity's servers: online play needs an internet connection.");
            return string.Format(Localization.Extra("mpOnlineFailed", "Online play failed: {0}"), Describe(e));
        }

        /// <summary>An exception's message with its inner ones (Unity Services' "Some services couldn't be initialized. Look
        /// at inner exceptions" says nothing on its own).</summary>
        static string Describe(Exception e)
        {
            var parts = new List<string>();
            void Add(Exception x, int depth)
            {
                if (x == null || depth > 4) return;
                string m = (x.Message ?? "").Trim();
                if (m.Length > 0 && !parts.Contains(m)) parts.Add(m);
                if (x is AggregateException agg) foreach (var inner in agg.InnerExceptions) Add(inner, depth + 1);
                else Add(x.InnerException, depth + 1);
            }
            Add(e, 0);
            string text = string.Join(" → ", parts);
            return text.Length > 400 ? text.Substring(0, 400) + "…" : text;
        }

        /// <summary>Hosts: online when PrepareOnlineHost reserved an allocation just before, else on this device's port.</summary>
        public static bool StartHost()
        {
            var relay = hostAllocation;
            hostAllocation = null;
            PrepareSession();
            SetUpHostedWorld();
            if (!Dedicated) NetRules.Host = NetRules.FromOptions();   // a player host's own Gameplay options (a server: Boot)
            NetMods.BeginHost();   // the host's mods when modded content is allowed, else none
            Seed = Environment.TickCount & 0x7fffffff;
            ushort port = HostPort;
            var m = EnsureManager();
            // Online through the Relay allocation; locally listening on every adapter (LAN, Wi-Fi, a VPN like Hamachi): any
            // address of this device reaches it.
            if (relay != null) Transport.SetRelayServerData(relay.ToRelayServerData(RelayConnection));
            else { JoinCode = null; Transport.SetConnectionData("127.0.0.1", port, "0.0.0.0"); }
            if ((relay == null && !CanHost()) || !m.StartHost())
            {
                if (Status.Length == 0) Status = string.Format(Localization.Extra("mpHostFailed", "Could not start hosting on port {0}."), port);
                Shutdown();
                return false;
            }
            var state = UnityEngine.Object.Instantiate(Resources.Load<GameObject>($"{PrefabFolder}/NetState"));
            state.GetComponent<NetState>().SetSeed(Seed);
            state.GetComponent<NetworkObject>().Spawn(false);
            SpawnPlayer(NetworkManager.ServerClientId);
            if (JoinCode != null) GUIUtility.systemCopyBuffer = JoinCode;   // ready to paste to friends
            PublishIfListed(false);
            // A persistent world: the host signs in to its own profile like any player (NetProfileClient: the world is
            // entered once the profile has arrived), else a fresh game at once.
            if (PersistentHost) NetProfileClient.Begin(NetProfiles.ServerId, Seed);
            else EnterWorld();
            return true;
        }

        /// <summary>The Host card's World choice (PlayerPrefs "mp_persistent"): the hosted session keeps every player's profile,
        /// the factions, bans, staff, news and server settings on this device (HostedWorldFolder), like a dedicated
        /// server; else a fresh game nothing of which is kept.</summary>
        public static bool HostWantsPersistent { get; set; }

        /// <summary>The session this game hosts is a persistent world (StartHost with HostWantsPersistent).</summary>
        public static bool PersistentHost { get; private set; }

        /// <summary>Where a persistent hosted world keeps its files (the dedicated server's ServerProfiles layout).</summary>
        public static string HostedWorldFolder => System.IO.Path.Combine(Application.persistentDataPath, "HostedWorld");

        /// <summary>StartHost, before the NetState spawns: the persistent world's profiles and settings, or none.</summary>
        static void SetUpHostedWorld()
        {
            PersistentHost = HostWantsPersistent;
            NetProfiles.Configure(PersistentHost, NetProfiles.DefaultEarnPerMinute, PersistentHost ? HostedWorldFolder : null);
            if (!PersistentHost) return;
            // The Host card's choices win over the saved settings (they are its "command line").
            NetServerSettings.Load(option => option == "-password" || option == "-maxplayers" || option == "-allowdebug" || option == "-name");
            NetProfiles.Start();   // the profiles, factions, bans and news (before NetState: it carries the world's id)
            Debug.Log($"NetGame: hosting a persistent world in {HostedWorldFolder}.");
        }

        /// <summary>A dedicated server (DedicatedServer, the -server command line): the session's world without a player of
        /// its own: no NetPlayer, no scene, listening on every adapter. The host's bookkeeping (NetState, the shared stock,
        /// squads, missions, crate claims, the chat relay) runs as with a host; the orbits are run by the players in them.</summary>
        public static bool StartServer(ushort port, bool keepSeed = false)
        {
            var relay = hostAllocation;
            hostAllocation = null;
            PrepareSession();
            NetMods.BeginHost();
            Dedicated = true;
            NetProfiles.Start();   // the player profiles (before NetState: it carries the server's id)
            if (!keepSeed || Seed == 0) Seed = Environment.TickCount & 0x7fffffff;   // a restart keeps the world's asteroid fields
            var m = EnsureManager();
            if (relay != null) Transport.SetRelayServerData(relay.ToRelayServerData(RelayConnection));
            else { JoinCode = null; Transport.SetConnectionData("0.0.0.0", port, "0.0.0.0"); }
            if ((relay == null && !CanHost(port)) || !m.StartServer())
            {
                if (Status.Length == 0) Status = string.Format(Localization.Extra("mpHostFailed", "Could not start hosting on port {0}."), port);
                Shutdown();
                return false;
            }
            var state = UnityEngine.Object.Instantiate(Resources.Load<GameObject>($"{PrefabFolder}/NetState"));
            state.GetComponent<NetState>().SetSeed(Seed, true);
            state.GetComponent<NetworkObject>().Spawn(false);
            PublishIfListed(true);
            return true;
        }

        /// <summary>This process is a dedicated server (StartServer): no player, no scenes, no visuals of the others.</summary>
        public static bool Dedicated { get; private set; }

        /// <summary>The players connected to this server, not counting a host's own.</summary>
        static int OthersConnected => manager == null ? 0 : manager.ConnectedClientsIds.Count - (manager.IsHost ? 1 : 0);

        /// <summary>The dedicated server: players connected (the host's own isn't one).</summary>
        public static IReadOnlyList<ulong> ClientIds => manager != null && manager.IsServer ? manager.ConnectedClientsIds : Array.Empty<ulong>();

        /// <summary>Server: drops a player with a reason (their game shows it, like the host ending the session).</summary>
        public static bool Kick(ulong clientId, string reason)
        {
            if (manager == null || !manager.IsServer || clientId == NetworkManager.ServerClientId || !manager.ConnectedClients.ContainsKey(clientId)) return false;
            manager.DisconnectClient(clientId, reason);
            return true;
        }

        /// <summary>The dedicated server stops: the players hear why first (Shutdown), then the process quits.</summary>
        public static void StopServer()
        {
            quitAfter = true;
            Shutdown();
            if (!closing) Application.Quit();
        }

        public static bool StartClient(string address)
        {
            PrepareSession();
            hostEndReason = null;
            if (!ParseAddress(address, out string ip, out ushort port))
            {
                Status = string.Format(Localization.Extra("mpBadAddress", "\"{0}\" is not an address (like 192.168.1.20 or 192.168.1.20:7778)."), address.Trim());
                sessionGame = false;   // no session started
                return false;
            }
            var m = EnsureManager();
            Transport.SetConnectionData(ip, port);
            m.NetworkConfig.ConnectionData = Payload();
            if (!m.StartClient())
            {
                Status = Localization.Extra("mpJoinFailed", "Could not connect.");
                Shutdown();
                return false;
            }
            return true;   // NetState reaching this client (EnterWorld) starts the game
        }

        /// <summary>The world's state is here (the host at once, a client when NetState spawns): the seed, then the game
        /// starts docked at Dis (no fly-in: the others see the ship appear on a pad), a new free-play game.</summary>
        internal static void EnterWorld(int seed = -1)
        {
            if (worldEntered) return;
            worldEntered = true;
            if (seed >= 0) Seed = seed;
            Session.DockedFromSpace = false;
            // The session's mods' ship models first (NetMods: a joining game has just turned the session's mods on).
            Modding.ModShips.WhenReady(() => { if (Active) SceneManager.LoadScene(StationScene); });
        }

        static bool closing, quitAfter, lostHandled;
        static string hostEndReason;

        /// <summary>Ends the session (leaving to the main menu, a failed connection). The host with players connected tells
        /// them why first and closes a moment later (NetDelayedShutdown), so the reason reaches them.</summary>
        public static void Shutdown()
        {
            // Leaving a server that keeps profiles: the game as it is now goes up first (queued before the disconnect).
            if (manager != null && !manager.IsServer && manager.IsConnectedClient && !closing) NetProfileClient.Upload();
            // A persistent hosted world: the host's own game straight into its profile (no network in between).
            if (manager != null && manager.IsHost && PersistentHost && worldEntered && !closing) NetProfileClient.SaveHostNow();
            NetChat.Clear();
            NetSquad.Clear();
            NetTradeClient.Clear();
            worldEntered = false;
            if (manager == null || closing) return;
            if (manager.IsServer && manager.IsListening && OthersConnected > 0 && NetState.Instance != null && NetState.Instance.IsSpawned)
            {
                closing = true;
                NetState.Instance.SessionEndingRpc(Localization.Extra("mpHostLeft", "The host ended the session."));
                manager.gameObject.AddComponent<NetDelayedShutdown>().Run(0.35f, quitAfter);
                return;
            }
            ShutdownNow();
        }

        /// <summary>Closing the game while hosting others: the quit waits for their goodbye (Shutdown), then goes on.</summary>
        static bool WantsToQuit()
        {
            if (manager == null || closing || !manager.IsServer || !manager.IsListening || OthersConnected == 0) return true;
            quitAfter = true;
            Shutdown();
            return !closing;
        }

        /// <summary>NetState: the host announced the end (the next disconnect's reason).</summary>
        internal static void OnHostEnding(string reason) => hostEndReason = reason;

        /// <summary>Closes the session at once (Shutdown, NetDelayedShutdown).</summary>
        public static void ShutdownNow()
        {
            closing = false;
            quitAfter = false;
            playersSpawned.Clear();
            if (manager == null) return;
            manager.OnClientConnectedCallback -= OnClientConnected;
            manager.OnClientDisconnectCallback -= OnClientDisconnect;
            manager.OnClientStopped -= OnStopped;
            manager.OnServerStopped -= OnServerStopped;
            if (manager.IsListening) manager.Shutdown();
            // A moment later: Netcode finishes its shutdown at the end of the frame (destroyed at once, its OnDestroy shut the
            // transport down a second time: "DisconnectRemoteClient should only be called on a listening server!").
            UnityEngine.Object.Destroy(manager.gameObject, 0.25f);
            manager = null;
            JoinCode = null;   // the Relay allocation goes with the host's connection
            listPending = false;
            NetLobby.Unpublish();
            if (SceneManager.GetActiveScene().name == MenuScene) sessionGame = false;   // already back in the menu
            // The session's objects live in DontDestroyOnLoad: gone with it (after Netcode's own shutdown, like the manager).
            foreach (var n in UnityEngine.Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include))
                if (n != null) UnityEngine.Object.Destroy(n.gameObject, 0.3f);
        }

        /// <summary>The host port is free (the menu asks before its fade); else Status says so and SuggestedPort is the next
        /// free one.</summary>
        public static bool CanHost() => CanHost(HostPort);

        static bool CanHost(ushort port)
        {
            SuggestedPort = 0;
            if (PortFree(port)) return true;
            int free = FreePortFrom(port + 1);
            Status = free > 0
                ? string.Format(Localization.Extra("mpPortInUse", "Port {0} is in use (another program, or the game hosting already). Try port {1}."), port, free)
                : string.Format(Localization.Extra("mpPortInUseNone", "Port {0} is in use (another program, or the game hosting already)."), port);
            SuggestedPort = free;
            return false;
        }

        /// <summary>A port the last host attempt found free after the one in use (the menu fills it in), 0 = none.</summary>
        public static int SuggestedPort { get; private set; }

        /// <summary>No other program holds 'port' (UDP, what Unity Transport uses).</summary>
        public static bool PortFree(int port)
        {
            try { using (new UdpClient(port)) return true; }
            catch (Exception) { return false; }
        }

        /// <summary>The first free port from 'port' on (20 tried), 0 = none.</summary>
        public static int FreePortFrom(int port)
        {
            for (int p = port; p < port + 20 && p <= 65535; p++) if (PortFree(p)) return p;
            return 0;
        }

        /// <summary>"host", "host:port" (a name is looked up; an IPv6 address has no port part here) -> the IPv4 address and
        /// port to connect to (DefaultPort without one).</summary>
        public static bool ParseAddress(string text, out string ip, out ushort port)
        {
            ip = null;
            port = DefaultPort;
            text = (text ?? "").Trim();
            if (text.Length == 0) return false;
            int colon = text.LastIndexOf(':');
            if (colon > 0 && text.IndexOf(':') == colon)
            {
                if (!ushort.TryParse(text.Substring(colon + 1), out port) || port == 0) return false;
                text = text.Substring(0, colon).Trim();
            }
            if (IPAddress.TryParse(text, out var parsed)) { ip = parsed.ToString(); return true; }
            try
            {
                foreach (var a in Dns.GetHostAddresses(text))
                    if (a.AddressFamily == AddressFamily.InterNetwork) { ip = a.ToString(); return true; }
            }
            catch (Exception) { }
            return false;
        }

        /// <summary>Every address of this device the others could join on, named by its adapter: the network cards (Ethernet,
        /// Wi-Fi, a hotspot this device runs, USB tethering) first, then VPNs meant for playing together (Hamachi, ZeroTier,
        /// Radmin, Tailscale...). Left out: adapters that are down, loopback, link-local (169.254), the virtual ones of
        /// virtual machines (Hyper-V, WSL, VirtualBox, VMware) and a phone's mobile data (the carrier's shared addresses
        /// can't be reached). Android names its adapters the Linux way (wlan0, swlan0 / ap0, rndis0, tun0, rmnet*): named
        /// here like the desktop ones. Empty = no network (never 127.0.0.1: nobody else could join on it).</summary>
        public static List<(string name, string address)> LocalAddresses()
        {
            var cards = new List<(string, string)>();
            var vpns = new List<(string, string)>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    // Android often reports Unknown for a working adapter: only the ones known to be down are left out.
                    var status = nic.OperationalStatus;
                    if (status == OperationalStatus.Down || status == OperationalStatus.NotPresent || status == OperationalStatus.LowerLayerDown
                        || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    string desc = (nic.Description ?? "") + " " + (nic.Name ?? "");
                    string vpn = VpnName(desc);
                    string name = vpn ?? AdapterName(nic);
                    if (name == null || (vpn == null && IsVirtual(desc))) continue;
                    foreach (var a in nic.GetIPProperties().UnicastAddresses)
                    {
                        var ip = a.Address;
                        if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip)) continue;
                        var b = ip.GetAddressBytes();
                        if (b[0] == 169 && b[1] == 254) continue;   // no network behind it
                        (vpn != null || name == "VPN" ? vpns : cards).Add((name, ip.ToString()));
                    }
                }
            }
            catch (Exception) { }
            cards.AddRange(vpns);
            return cards;
        }

        static string VpnName(string desc)
        {
            foreach (var n in new[] { "Hamachi", "ZeroTier", "Radmin", "Tailscale", "WireGuard", "OpenVPN", "NordLynx" })
                if (desc.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return n;
            return null;
        }

        static bool IsVirtual(string desc)
        {
            // (Windows' own mobile hotspot runs on a "Wi-Fi Direct Virtual Adapter": that one is named Hotspot, not left out.)
            if (desc.IndexOf("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            foreach (var n in new[] { "Hyper-V", "vEthernet", "WSL", "VirtualBox", "VMware", "Virtual Adapter", "Loopback", "Bluetooth", "Npcap", "TAP-Windows" })
                if (desc.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>An adapter's name for the list; null = one nobody can join on (mobile data, Android's internal ones).</summary>
        static string AdapterName(NetworkInterface nic)
        {
            string n = (nic.Name ?? "").ToLowerInvariant();
            string d = nic.Description ?? "";
            var platform = Application.platform;
            if (platform == RuntimePlatform.Android || platform == RuntimePlatform.LinuxPlayer || platform == RuntimePlatform.LinuxEditor)
            {
                // Android / Linux names.
                if (n == "lo") return null;
                foreach (var skip in new[] { "rmnet", "ccmni", "clat", "v4-", "pdp", "seth", "wwan", "dummy", "p2p", "ifb", "sit", "ip6" })
                    if (n.StartsWith(skip)) return null;
                if (n.StartsWith("swlan") || n.StartsWith("ap") || n.StartsWith("softap") || n == "wlan1") return "Hotspot";
                if (n.StartsWith("wlan")) return "Wi-Fi";
                if (n.StartsWith("rndis") || n.StartsWith("usb") || n.StartsWith("ncm")) return "USB";
                if (n.StartsWith("tun") || n.StartsWith("ppp") || n.StartsWith("ipsec")) return "VPN";
                if (n.StartsWith("eth") || n.StartsWith("en")) return "Ethernet";
            }
            // Windows / macOS.
            if (d.IndexOf("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase) >= 0) return "Hotspot";
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) return "Wi-Fi";
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet || nic.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet) return "Ethernet";
            return string.IsNullOrEmpty(nic.Name) ? "Network" : nic.Name;
        }

        /// <summary>This device's LAN address, for the host to tell the others.</summary>
        public static string LocalAddress()
        {
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var a in nic.GetIPProperties().UnicastAddresses)
                        if (a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address)) return a.Address.ToString();
                }
            }
            catch (Exception) { }
            try
            {
                foreach (var a in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)) return a.ToString();
            }
            catch (Exception) { }
            return "127.0.0.1";
        }

        static UnityTransport Transport => (UnityTransport)manager.NetworkConfig.NetworkTransport;

        /// <summary>Status::resetGame as free play (no story) in the finished game's world, docked at Dis (Station).</summary>
        static void PrepareSession()
        {
            Status = "";
            playersSpawned.Clear();   // statics outlive a session (and, with domain reload off, Play mode)
            worldEntered = transportConnected = lostHandled = false;
            closing = quitAfter = false;
            sessionGame = true;
            Dedicated = false;
            PersistentHost = false;
            NetMods.BeginClient();   // no mods until the session's arrive (NetState) or the host picks its own (BeginHost)
            NetStock.Reset();
            NetArena.Reset();
            NetArenaClient.Reset();
            NetStats.Reset();
            NetNews.Reset();
            Session.ResetNewGame();
            Session.Difficulty = Session.DifficultyNormal;   // every session plays on Normal (the shared stock, NPCs, rewards)
            Session.Economy = Economy.Android;               // and on one economy (the shared stock's prices)
            Session.UseCompletedWorld();   // free play in the finished game's world (every dealer ship, Ginoya after the supernova)
            Session.StationIndex = Station;
            Session.LaunchedFromStation = false;   // the session starts docked (EnterWorld)
            // Remake: a modest start instead of resetGame's well-equipped Phantom, so the pilot works up to better ships (a
            // dedicated server or persistent host keeps their progress in the profile, which replaces this when it loads).
            Session.ShipIndex = StartShip;
            Session.Equipment = StartEquipment();
            Session.Credits = StartCredits;
        }

        /// <summary>A new multiplayer pilot's ship (Betty), credits and loadout.</summary>
        public const int StartShip = 0, StartCredits = 10000;

        /// <summary>Betty's slots (1 primary, 1 secondary, 3 equipment): Nirai Impulse EX 1 (0), Targe Shield (50), Telta
        /// Quickscan (81), IMT Extract 1.3 (86); all saleable, the secondary slot empty.</summary>
        static List<ItemStack> StartEquipment() => new List<ItemStack>
        {
            new ItemStack(0, 1), new ItemStack(50, 1), new ItemStack(81, 1), new ItemStack(86, 1),
        };

        static NetworkManager EnsureManager()
        {
            if (manager != null) return manager;
            var go = new GameObject("NetworkManager");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var transport = go.AddComponent<NetTransport>();   // UnityTransport with a byte count (NetStats)
            transport.ConnectTimeoutMS = 1000;
            transport.MaxConnectAttempts = 10;   // a wrong address gives up after about 10 s
            transport.DisconnectTimeoutMS = 5000;   // a player whose game closed without leaving is gone after 5 s (default 30)
            manager = go.AddComponent<NetworkManager>();
            // No scene management: every player loads their own scenes (the shared world, see the header).
            // Connection approval: every connecting game sends its version (ConnectionData) and only the exact same one gets
            // in (Approve). Builds from before the check don't use approval, which is part of Netcode's config hash: they
            // fail its handshake ("NetworkConfig mismatch") before anything else.
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport, EnableSceneManagement = false, ConnectionApproval = true,
                ConnectionData = Payload(),
            };
            manager.ConnectionApprovalCallback = Approve;
            foreach (var name in PrefabNames)
            {
                var prefab = Resources.Load<GameObject>($"{PrefabFolder}/{name}");
                if (prefab != null) manager.AddNetworkPrefab(prefab);
                else Debug.LogError($"NetGame: missing network prefab Resources/{PrefabFolder}/{name} (GoF2 > Build > Network Prefabs)");
            }
            manager.OnClientConnectedCallback += OnClientConnected;
            manager.OnClientDisconnectCallback += OnClientDisconnect;
            manager.OnClientStopped += OnStopped;   // Netcode ending the session by itself (a transport failure)
            manager.OnServerStopped += OnServerStopped;   // the same for a dedicated server (no client part)
            Application.quitting -= Shutdown;
            Application.quitting += Shutdown;   // closing the game leaves the session (the others see it at once)
            Application.wantsToQuit -= WantsToQuit;
            Application.wantsToQuit += WantsToQuit;   // hosting: the others hear why first
            return manager;
        }

        public const int MaxPasswordLength = 32;

        /// <summary>The session's password, empty = none: set before hosting (the Host card, -password); the connection
        /// approval checks every joining game against it (the server's own check: the lobby's join code is public).</summary>
        public static string HostPassword { get; set; } = "";

        /// <summary>The password this game joins with (the Join card), empty = none.</summary>
        public static string JoinPassword { get; set; } = CommandLineValue("-mppassword") ?? "";   // -mppassword: testing with -mpjoin

        /// <summary>The connection data: the fingerprint, the password, the shown version (for the refusal's text). Builds from
        /// before the fingerprint send their version first and their password: refused, their version is the first line.</summary>
        static byte[] Payload() => System.Text.Encoding.UTF8.GetBytes(Protocol + "\n" + CleanPassword(JoinPassword) + "\n" + Version + "\n" + NetMods.InstalledList);

        public static string CleanPassword(string text)
        {
            text = (text ?? "").Trim();
            return text.Length > MaxPasswordLength ? text.Substring(0, MaxPasswordLength) : text;
        }

        /// <summary>Server: a game connecting gets in only with this game's exact version (testing: the Editor takes any, and
        /// every build takes the Editor) and the session's password, if it has one. Turned away = the reason is
        /// their popup. The host's own client always gets in.</summary>
        static void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            string payload = request.Payload != null && request.Payload.Length > 0 && request.Payload.Length <= 512 + NetMods.MaxListLength
                ? System.Text.Encoding.UTF8.GetString(request.Payload) : "";
            var lines = payload.Split('\n');
            string theirs = lines[0];
            string password = lines.Length > 1 ? lines[1] : "";
            string theirVersion = lines.Length > 2 && lines[2].Length > 0 ? lines[2] : theirs;
            response.CreatePlayerObject = false;   // NetGame spawns the NetPlayer itself
            response.Pending = false;
            response.Approved = true;
            if (request.ClientNetworkId == NetworkManager.ServerClientId) return;   // a host's own client
            if (closing)
            {
                response.Approved = false;
                response.Reason = Localization.Extra("mpHostLeft", "The host ended the session.");
                return;
            }
            bool same = theirs == Protocol || Application.isEditor || theirs == "editor";   // the Editor always joins (testing)
            if (!same)
            {
                response.Approved = false;
                response.Reason = theirVersion == Version
                    ? string.Format(Localization.Extra("mpWrongBuild",
                        "This game runs a different build of version {0}. Both need the same game files to play together."), Version)
                    : string.Format(Localization.Extra("mpWrongVersion",
                        "This game runs version {0}, yours is {1}. Both need the same version to play together."),
                        Version, theirVersion.Length > 0 ? theirVersion : Localization.Extra("mpOlderVersion", "an older one"));
                Debug.Log($"NetGame: turned away client {request.ClientNetworkId}, version '{theirVersion}' / '{theirs}' (this one {Version} / {Protocol})");
                return;
            }
            string expected = CleanPassword(HostPassword);
            if (expected.Length > 0 && password != expected)
            {
                response.Approved = false;
                response.Reason = password.Length == 0
                    ? Localization.Extra("mpNeedsPassword", "This game has a password: enter it under Join, then join again.")
                    : Localization.Extra("mpWrongPassword", "Wrong password.");
                Debug.Log($"NetGame: turned away client {request.ClientNetworkId}: {(password.Length == 0 ? "no password" : "wrong password")}");
                return;
            }
            // Mods (NetMods): the session's mods, the same files; the 4th line lists what the joining game has.
            if (NetMods.Refusal(lines.Length > 3 ? lines[3] : "") is string modsMissing)
            {
                response.Approved = false;
                response.Reason = modsMissing;
                Debug.Log($"NetGame: turned away client {request.ClientNetworkId}: missing mods");
                return;
            }
            // Full: the players in the session (a host's own included) at MaxPlayers.
            if (manager != null && manager.ConnectedClientsIds.Count >= MaxPlayers)
            {
                response.Approved = false;
                response.Reason = string.Format(Localization.Extra("mpServerFull", "The game is full ({0} players)."), MaxPlayers);
                Debug.Log($"NetGame: turned away client {request.ClientNetworkId}: full ({MaxPlayers})");
            }
        }

        /// <summary>The Debug menu (cheats, items, spawns, other hulls) is allowed in the session this game hosts: set before
        /// hosting (the Host card's Debug menu switch, PlayerPrefs "mp_allow_debug"; a dedicated server's -allowdebug). Off by
        /// default. NetState carries it to every player.</summary>
        public static bool HostAllowsDebug { get; set; }

        /// <summary>The running session allows the Debug menu (NetState's flag, false until it has arrived); outside a
        /// session nothing restricts it (Cheats.Allowed).</summary>
        public static bool DebugAllowed => NetState.Instance != null && NetState.Instance.DebugAllowed;

        /// <summary>Players may shoot each other anywhere, not only in arena matches and faction sieges: PvP instead of PvE
        /// (the Host card's Combat switch, PlayerPrefs "mp_pvp"; a dedicated server's -freepvp; the server settings' "freepvp").
        /// Off (PvE) by default. NetState carries it to every player; the server browser shows it (NetLobby).</summary>
        public static bool FreePvp { get; set; }

        /// <summary>The session has a password (the server browser's tag).</summary>
        internal static bool HasPassword => CleanPassword(HostPassword).Length > 0;

        static void OnClientConnected(ulong clientId)
        {
            if (manager == null) return;
            if (!manager.IsServer) { if (clientId == manager.LocalClientId) transportConnected = true; return; }
            // The host is closing (its goodbye is out): a player connecting now is turned away with the reason.
            if (closing) { if (clientId != NetworkManager.ServerClientId) manager.DisconnectClient(clientId, Localization.Extra("mpHostLeft", "The host ended the session.")); return; }
            SpawnPlayer(clientId);
        }

        /// <summary>Netcode stopped this session by itself (not ShutdownNow, which unsubscribes first): like a lost host.</summary>
        static void OnStopped(bool wasHost)
        {
            if (manager == null) return;
            if (wasHost) { SessionEnded(Localization.Extra("mpStopped", "The session stopped (network error).")); return; }
            OnClientDisconnect(manager.LocalClientId);
        }

        /// <summary>A dedicated server stopped by itself (a transport failure: the network or the Relay connection went; a
        /// host's own stop comes through OnStopped): it starts again (DedicatedServer.ConnectionLost), else quits.</summary>
        static void OnServerStopped(bool wasHost)
        {
            if (manager == null || wasHost || !Dedicated) return;
            Debug.LogError("NetGame: the server stopped (network error).");
            ShutdownNow();
            if (!DedicatedServer.ConnectionLost()) DedicatedServer.Quit(1);
        }

        /// <summary>The session is over while playing: out of it, back to the Multiplayer panel with the reason.</summary>
        static void SessionEnded(string reason)
        {
            if (lostHandled) return;
            lostHandled = true;
            Status = reason;
            Shutdown();
            PopupPending = true;
            UI.MainMenu.OpenPanelOnStart = "multiplayerPanel";
            SceneManager.LoadScene(MenuScene);
        }

        static void SpawnPlayer(ulong clientId)
        {
            if (!playersSpawned.Add(clientId)) return;
            var player = UnityEngine.Object.Instantiate(Resources.Load<GameObject>($"{PrefabFolder}/NetPlayer"));
            player.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, false);
        }

        static void OnClientDisconnect(ulong clientId)
        {
            if (manager == null) return;
            if (manager.IsServer)
            {
                // That player's ship goes at once (NGO removes a player object with its owner; this also covers a late one),
                // and so does what they showed of their orbit (NGO destroys the objects a leaving client owns).
                playersSpawned.Remove(clientId);
                NetProfiles.OnDisconnect(clientId);   // its profile's control goes to its next device online
                NetArena.OnDisconnect(clientId);      // out of their queue or match
                NetTrade.OnDisconnect(clientId);      // their trade ends (whoever paid gets it back)
                NetRateLimit.Forget(clientId);
                // (Netcode has usually despawned the player object already: its mission cargo is handed over in
                // NetPlayer.OnNetworkDespawn.)
                foreach (var p in UnityEngine.Object.FindObjectsByType<NetPlayer>())
                    if (p.OwnerClientId == clientId && p.IsSpawned) p.NetworkObject.Despawn();
                return;
            }
            // This client lost the host (or never reached it): the host's own reason, else a plain one (not Netcode's
            // "[Disconnect Event] ... ProtocolTimeout" text).
            if (lostHandled) return;
            string reason = hostEndReason ?? manager.DisconnectReason;
            hostEndReason = null;
            bool wasConnected = worldEntered;
            if (string.IsNullOrEmpty(reason) || reason.StartsWith("[")) reason = null;
            // A host that answered but ended the session before this player was in: not "no host".
            Status = reason ?? (wasConnected ? Localization.Extra("mpLost", "The connection to the host was lost.")
                                : transportConnected ? Localization.Extra("mpHostLeft", "The host ended the session.")
                                : Localization.Extra("mpNoHost", "No host found at that address."));
            if (!wasConnected && SceneManager.GetActiveScene().name == MenuScene)
            {
                // A failed join: the panel shows it.
                lostHandled = true;
                Shutdown();
                sessionGame = false;
                return;
            }
            SessionEnded(Status);
        }
    }
}
