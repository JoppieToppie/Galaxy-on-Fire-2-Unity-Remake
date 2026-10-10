// DedicatedServer.cs
// Remake-only: the normal Windows / Linux player as a dedicated multiplayer server, so a session runs without anyone
// playing on the hosting machine. Started with -server on the command line (Unity's -batchmode -nographics recommended:
// no window, no rendering), or the GOF2_SERVER environment variable (the Editor's Play mode, for testing). Options:
//   -relay           online through Unity Relay: the console shows the join code, no port forwarding (else players join
//                    on this machine's address); listed in the server browser (NetLobby) unless -unlisted
//   -name "..."      the server browser's name for it
//   -password X      players need it to join (NetGame's connection approval)
//   -maxplayers N    the player limit (default 16, 2..100: Relay's connections, the browser, the approval)
//   -allowdebug      the players may use the Debug menu (cheats, items, spawns); off by default (NetGame.HostAllowsDebug)
//   -allowmods       the session runs every mod in this game's Mods folders (NetMods); off by default
//   -port N          the local port (default 7777); -fps N the server's frame rate (default 60)
//   -freepvp         players may fight anywhere (else only in arena matches, NetArena)
//   -cloakhides -capitalships -informeroriginal -nopirateevents -nokaamostacking -nokaamoequipment
//                    the session's gameplay options (NetRules), each the other way from the game's default
//   -noprofiles      no player profiles (NetProfiles; on by default: credits, ships, cargo, Kaamo Club, squad kept
//                    per player between sessions; no limit, one unused for 30 days is pruned); -maxearn N (worth a profile may gain
//                    per minute online without -allowdebug, default 1 000 000), -profiledir PATH (default
//                    <persistentDataPath>/ServerProfiles)
//   -claimcost N     a faction's station claim from its bank (default 500 000); -maxclaims N per faction (default 3);
//   -claimdays N     the days without a member docking before a claim lapses (default 14) (NetFactions)
//   -siegecost N     a siege on another faction's station, from the bank (default 250 000); -toll N what another faction's
//                    pilot pays to be spared by a held station's defence (default 10 000, 0 = no toll)
//   -web [port]      the web admin (WebAdmin: a browser page for the console, players, bans, settings, the log), default
//                    port 8080 (-webport N too); -webbind ADDRESS what it listens on (default 127.0.0.1, this machine only;
//                    0.0.0.0 every adapter: then put it behind a TLS reverse proxy)
// Bootstrap calls Boot before the first scene wakes and swaps in an empty scene. The main menu scene never runs: in the
// Editor its objects are already loaded and are switched off at once; in a player the scene is still loading then, so
// MainMenu / MenuBackground call ShutOff as they wake (the scene's objects off before the rest wake: no menu, music or
// live orbit backdrop), and the scene is unloaded once loaded. The process is muted (AudioListener volume 0, paused).
// Losing the network (the router restarting, the Relay connection gone) stops Netcode's server: instead of quitting, the
// server starts its session again (Reconnect: after 5 s, then 10, 20, 40 and every 60 s until it is back), online with a
// new Relay allocation, join code and listing, the world seed kept. The players were dropped with the connection; they
// join again (from the server browser, or with the new code) and the profiles bring their progress back.
// Then NetGame.StartServer runs the session's world (NetState: the seed, the shared stock, squads, missions, crate
// claims, the chat relay) without a player of its own; every player's game runs its orbits as with a host (the first
// player in an orbit runs its NPCs).
// The console: the log (joins and leaves with the client ids, where each player is, chat, errors) and commands (help,
// status, list, say, kick, stop). Windows players are GUI programs: the server opens its own console window (WinConsole)
// unless its output is redirected to a file or pipe (or -noconsole); the log is mirrored into that window (Unity prints
// its log only to a standard output it starts with). Linux uses the terminal's stdin / stdout (-logFile - prints the log
// there). Ctrl+C or closing the window stops the server like "stop": the players hear why first (NetGame.StopServer).
// In its own console window (Windows) or on a terminal (Linux) the input line is edited by ConsoleInput: Tab completes
// and cycles the command names (CommandNames), Up / Down the lines run before; piped input is read line by line.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class DedicatedServer : MonoBehaviour
    {
        public const string EnvironmentSwitch = "GOF2_SERVER";
        const float TrackSeconds = 1f;
        /// <summary>The commands Tab completes in the console (ConsoleInput), in help's order.</summary>
        static readonly string[] CommandNames = new[] { "help", "status", "list", "say", "admin", "stop" }.Concat(NetCommands.ServerCommandNames).ToArray();

        /// <summary>This process runs as a dedicated server (-server, GOF2_SERVER set, or a Dedicated Server build).</summary>
        public static bool Enabled { get; private set; } = Detect();

        static readonly ConcurrentQueue<string> commands = new ConcurrentQueue<string>();
        static TextWriter console;
        static bool consoleLog;
        static DedicatedServer instance;
        static ushort port;
        static bool relay;
        static float startedAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Enabled = Detect();
            while (commands.TryDequeue(out _)) { }
            instance = null;
            reconnecting = false;
        }

        static bool Detect() =>
#if UNITY_SERVER
            true;   // the Linux Dedicated Server build (GoF2 > Build > Linux Dedicated Server): always a server
#else
            HasFlag("-server") || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnvironmentSwitch));
#endif

        static bool HasFlag(string flag) =>
            Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

        static string Value(string flag) => NetGame.CommandLineValue(flag);

        /// <summary>Bootstrap (BeforeSceneLoad): the first scene's objects are loaded but not awake yet. They are switched off
        /// and replaced by an empty scene, and the server starts.</summary>
        public static void Boot()
        {
            if (instance != null) return;
            int fps = int.TryParse(Value("-fps"), out int f) ? Mathf.Clamp(f, 10, 240) : 60;
            port = ushort.TryParse(Value("-port"), out ushort p) && p >= 1024 ? p : NetGame.DefaultPort;
            NetGame.MaxPlayers = int.TryParse(Value("-maxplayers"), out int mp) ? mp : NetGame.DefaultMaxPlayers;   // 2..100
            NetGame.HostPassword = NetGame.CleanPassword(Value("-password"));
            NetGame.HostAllowsDebug = HasFlag("-allowdebug");
            NetMods.HostAllowsMods = HasFlag("-allowmods");
            NetGame.FreePvp = HasFlag("-freepvp");
            NetRules.Host = NetRules.Defaults;   // the session's gameplay options: the defaults, a flag turns one the other way
            foreach (var r in NetRules.All) if (HasFlag(r.flag)) NetRules.Host ^= r.bit;
            NetProfiles.Configure(!HasFlag("-noprofiles"),
                int.TryParse(Value("-maxearn"), out int earn) ? earn : NetProfiles.DefaultEarnPerMinute,
                Value("-profiledir"));
            NetFactions.Configure(int.TryParse(Value("-claimcost"), out int cost) ? cost : NetFactions.DefaultClaimCost,
                int.TryParse(Value("-maxclaims"), out int maxClaims) ? maxClaims : NetFactions.DefaultMaxClaims,
                int.TryParse(Value("-claimdays"), out int days) ? days : NetFactions.DefaultLapseDays,
                int.TryParse(Value("-siegecost"), out int siegeCost) ? siegeCost : NetFactions.DefaultSiegeCost,
                int.TryParse(Value("-toll"), out int toll) ? toll : NetFactions.DefaultToll);
            ListName = Value("-name") ?? DefaultListName;
            // The settings saved by the admins (server_settings.json) for whatever the command line didn't give.
            NetServerSettings.Load(HasFlag);
            relay = HasFlag("-relay") || Environment.GetEnvironmentVariable(EnvironmentSwitch) == "relay";
            Application.runInBackground = true;
#if UNITY_STANDALONE_LINUX && !UNITY_EDITOR
            SaveTerminal();
            Application.quitting += ExitNow;
#endif
#if !UNITY_EDITOR
            // The log (-logFile -: the terminal) without a call stack under every info line and warning ("NetLobby: listed
            // ..." came with ~100 lines of async frames); errors and exceptions keep theirs.
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
#endif
#if UNITY_EDITOR
            int editorVSync = QualitySettings.vSyncCount;
            Application.quitting += () => QualitySettings.vSyncCount = editorVSync;   // QualitySettings.asset keeps its own
#endif
            QualitySettings.vSyncCount = 0;   // batch mode has no display to sync to: the cap keeps the CPU idle
            Application.targetFrameRate = fps;
            AudioListener.volume = 0f;   // a server makes no sound, with or without -nographics
            AudioListener.pause = true;

            var first = SceneManager.GetActiveScene();
            serverScene = SceneManager.CreateScene("DedicatedServer");
            SceneManager.SetActiveScene(serverScene);
            // The Editor: the first scene is loaded already, its objects not awake: off and gone now. A player loads it
            // after this (ShutOff as it wakes, OnSceneLoaded unloads it).
            if (first.IsValid() && first.isLoaded && first != serverScene)
            {
                foreach (var root in first.GetRootGameObjects()) root.SetActive(false);
                SceneManager.UnloadSceneAsync(first);
            }
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;

            var go = new GameObject("DedicatedServer");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<DedicatedServer>();
        }

        static Scene serverScene;

        /// <summary>MainMenu / MenuBackground as they wake: on a dedicated server their scene is switched off at once (true =
        /// do nothing more); OnSceneLoaded unloads it.</summary>
        public static bool ShutOff(Scene scene)
        {
            if (!Enabled) return false;
            foreach (var root in scene.GetRootGameObjects()) if (root.activeSelf) root.SetActive(false);
            return true;
        }

        /// <summary>Any other scene that loads on a server (the first scene in a player): off and unloaded.</summary>
        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!Enabled || scene == serverScene || !scene.IsValid()) return;
            foreach (var root in scene.GetRootGameObjects()) if (root.activeSelf) root.SetActive(false);
            if (serverScene.IsValid() && serverScene.isLoaded) SceneManager.SetActiveScene(serverScene);
            SceneManager.UnloadSceneAsync(scene);
        }

        readonly Dictionary<ulong, (string name, string where, float since)> known = new Dictionary<ulong, (string, string, float)>();
        float trackTimer;

        async void Start()
        {
            OpenConsole();
            startedAt = Time.unscaledTime;
            Log($"Galaxy on Fire 2 Unity Remake dedicated server, build {UI.BuildVersion.Text} (fingerprint {NetGame.Protocol})");
            if (!await StartSession(false))
            {
                // Online without a network yet (the machine came up before its router): keep trying, like after a lost
                // connection. A local server's failure (the port in use) won't go away by waiting.
                if (!relay) { Fail(); return; }
                Debug.LogWarning("Server: " + NetGame.Status);
                Reconnect();
            }
            Log($"Up to {NetGame.MaxPlayers} players (-maxplayers).");
            if (NetGame.HasPassword) Log("Players need the password (-password) to join.");
            Log(NetGame.HostAllowsDebug ? "The Debug menu is allowed (-allowdebug)." : "The Debug menu is off (-allowdebug allows it).");
            Log(NetMods.HostAllowsMods
                ? (NetMods.SessionList.Length > 0 ? $"Mods (-allowmods): {NetMods.SessionNames}." : "Mods are allowed (-allowmods), but the Mods folder has none.")
                : "No mods (-allowmods runs every mod in the Mods folder).");
            if (!HasFlag("-noprofiles"))
                Log(NetGame.HostAllowsDebug ? "Player profiles: uploads are taken as the players' games send them (-allowdebug)."
                                            : $"Player profiles: uploads are checked (at most {NetProfiles.EarnPerMinute:N0} worth gained per minute: -maxearn).");
            else Log("Player profiles are off (-noprofiles): nothing is saved.");
            Log(NetGame.FreePvp ? "Players may fight anywhere (-freepvp)." : "Players fight only in arena matches (/duel, /ffa; -freepvp allows it anywhere).");
            WebAdmin.StartFromCommandLine();
            Log("Type \"help\" for the commands.");
        }

        /// <summary>Starts the session (online: a Relay allocation and listing first); 'again' = after a lost connection, the
        /// world seed kept. False = NetGame.Status says why.</summary>
        static async Task<bool> StartSession(bool again)
        {
            if (relay)
            {
                Log("Reserving an online session (Unity Relay)...");
                string listed = HasFlag("-unlisted") ? null : ListName;
                if (!await NetGame.PrepareOnlineHost(listed)) return false;
            }
            if (!NetGame.StartServer(port, again)) return false;
            if (NetGame.JoinCode != null)
                Log($"Online through Unity Relay. Join code: {NetGame.JoinCode}");
            else
            {
                Log($"Listening on port {port} (UDP, every network adapter). Players join on this machine's address{(port != NetGame.DefaultPort ? ":" + port : "")}.");
                foreach (var (name, address) in NetGame.LocalAddresses()) Log($"  {name}: {address}");
            }
            return true;
        }

        static bool reconnecting;

        public const string DefaultListName = "Galaxy on Fire 2 server";

        /// <summary>The server browser's name (-name, NetServerSettings' "name"; used at the next listing).</summary>
        public static string ListName { get; set; } = DefaultListName;

        /// <summary>NetGame: Netcode's server stopped by itself (the network went). True = the server starts again
        /// (Reconnect); false = no server here, the caller quits.</summary>
        public static bool ConnectionLost()
        {
            if (instance == null) return false;
            if (!reconnecting) instance.Reconnect();
            return true;
        }

        async void Reconnect()
        {
            reconnecting = true;
            float delay = 5f;   // the old NetworkManager is destroyed a moment after the stop (NetGame.ShutdownNow)
            Log($"Offline; starting the session again in {delay:0} s.");
            while (instance == this)
            {
                await Task.Delay(TimeSpan.FromSeconds(delay));   // Unity's synchronisation context: back on the main thread
                if (instance != this || NetGame.Active) break;
                if (await StartSession(true))
                {
                    Log(NetGame.JoinCode != null ? "Back online. The players need to join again (the server browser, or the new join code)."
                                                 : "Back online. The players need to join again.");
                    break;
                }
                delay = Mathf.Min(delay * 2f, 60f);
                Log($"Still offline ({NetGame.Status}); trying again in {delay:0} s.");
            }
            reconnecting = false;
        }

        static void Fail()
        {
            Debug.LogError("Server: " + NetGame.Status);
            Quit(1);
        }

        /// <summary>The exit code of the quit under way (ExitNow).</summary>
        static int exitCode;

        /// <summary>Quits with 'code' (0 = stopped on purpose, 1 = failed).</summary>
        public static void Quit(int code)
        {
            exitCode = code;
            Application.Quit(code);
        }

#if UNITY_STANDALONE_LINUX && !UNITY_EDITOR
        // glibc's real file name: IL2CPP has no Mono-style "libc" mapping, and libc.so is only a linker script on most distros.
        const string LibC = "libc.so.6";

        [System.Runtime.InteropServices.DllImport(LibC, EntryPoint = "_exit")]
        static extern void LibcExit(int status);

        [System.Runtime.InteropServices.DllImport(LibC, EntryPoint = "fflush")]
        static extern int LibcFlush(IntPtr stream);

        [System.Runtime.InteropServices.DllImport(LibC, EntryPoint = "tcgetattr")]
        static extern int TcGetAttr(int fd, byte[] termios);

        [System.Runtime.InteropServices.DllImport(LibC, EntryPoint = "tcsetattr")]
        static extern int TcSetAttr(int fd, int optionalActions, byte[] termios);

        /// <summary>The terminal's settings at the start (struct termios, 60 bytes on glibc; the buffer is roomier), put back
        /// before the hard exit: Console.ReadKey switches echo and line mode off and only its own exit handler restores them.</summary>
        static byte[] savedTerminal;

        static void SaveTerminal()
        {
            try
            {
                var t = new byte[256];
                if (TcGetAttr(0, t) == 0) savedTerminal = t;
            }
            catch (Exception) { }
        }

        /// <summary>Linux: the process ends here. Unity's own teardown never finished: the console thread sits in
        /// Console.ReadKey (a blocking read of the terminal) and the runtime waited for it for good after "CodeReloadManager
        /// destroyed". What the server keeps is already on disk (profiles, factions, settings are written as they change), so
        /// the network goes down, the log is flushed, the terminal gets its settings back (SaveTerminal) and the process exits
        /// at once.</summary>
        static void ExitNow()
        {
            try { WebAdmin.Stop(); } catch (Exception) { }
            try { NetGame.ShutdownNow(); } catch (Exception) { }
            Debug.Log($"Server: exiting ({exitCode}).");
            try { Console.Out.Flush(); Console.Error.Flush(); } catch (Exception) { }
            try { LibcFlush(IntPtr.Zero); } catch (Exception) { }   // the native log's buffered stdout (_exit skips it)
            try { if (savedTerminal != null) TcSetAttr(0, 0, savedTerminal); } catch (Exception) { }   // TCSANOW: echo back
            LibcExit(exitCode);
        }
#endif

        void Update()
        {
            while (commands.TryDequeue(out string line))
            {
                string answer = Run(line);
                if (!string.IsNullOrEmpty(answer)) Answer(answer);
            }
            WebAdmin.Pump();   // the web admin's requests, on the main thread
            if ((trackTimer -= Time.unscaledDeltaTime) <= 0f)
            {
                trackTimer = TrackSeconds;
                TrackPlayers();
            }
        }

        void OnDestroy()
        {
            if (instance == this) { instance = null; WebAdmin.Stop(); }
            if (consoleLog) Application.logMessageReceivedThreaded -= Mirror;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        // ---- players ----------------------------------------------------------------------------------------

        /// <summary>Joins, leaves and moves of the players, logged once their name and place are known.</summary>
        void TrackPlayers()
        {
            var seen = new HashSet<ulong>();
            foreach (var p in NetPlayer.All)
            {
                if (p == null || !p.IsSpawned) continue;
                ulong id = p.OwnerClientId;
                seen.Add(id);
                string where = Where(p);
                if (!known.TryGetValue(id, out var k))
                {
                    if (p.Where == NetPlayer.Place.None) continue;   // still loading: their name and place come with their scene
                    known[id] = (p.DisplayName, where, Time.unscaledTime);
                    Log($"{p.DisplayName} (client {id}) joined, {where}. {Count()}");
                }
                else if (k.where != where || k.name != p.DisplayName)
                {
                    if (k.name != p.DisplayName) Log($"{k.name} (client {id}) is now called {p.DisplayName}.");
                    if (k.where != where) Log($"{p.DisplayName}: {where}");
                    known[id] = (p.DisplayName, where, k.since);
                }
            }
            var gone = new List<ulong>();
            foreach (var pair in known) if (!seen.Contains(pair.Key)) gone.Add(pair.Key);
            foreach (ulong id in gone)
            {
                var k = known[id];
                known.Remove(id);
                Log($"{k.name} (client {id}) left after {Duration(Time.unscaledTime - k.since)}. {Count()}");
            }
        }

        string Count()
        {
            int n = NetGame.ClientIds.Count;
            return n == 1 ? "1 player online." : $"{n} players online.";
        }

        internal static string Where(NetPlayer p)
        {
            if (NetArena.IsArenaOrbit(p.Station) && p.InSpace) return $"in arena match {p.Station - NetArena.OrbitBase}";
            string station = StationName(p.Station);
            switch (p.Where)
            {
                case NetPlayer.Place.Space: return $"in space at {station}";
                case NetPlayer.Place.Hangar: return $"docked at {station}";
                case NetPlayer.Place.Departing: return $"taking off from {station}";
                default: return "loading";
            }
        }

        internal static string StationName(int index)
        {
            if (index == Session.VoidOrbit) return "the Void";
            var s = NetGame.Db.Stations.Find(x => x.index == index);
            return s != null && !string.IsNullOrEmpty(s.name) ? s.name : $"station {index}";
        }

        static string Duration(float seconds)
        {
            var t = TimeSpan.FromSeconds(Mathf.Max(0f, seconds));
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds:00}s" : $"{t.Seconds}s";
        }

        // ---- commands ---------------------------------------------------------------------------------------

        /// <summary>Runs one console command and returns its answer (also for the Editor: DedicatedServer.Run("list")).</summary>
        public static string Run(string line)
        {
            line = (line ?? "").Trim();
            if (line.Length == 0) return "";
            int space = line.IndexOf(' ');
            string cmd = (space < 0 ? line : line.Substring(0, space)).ToLowerInvariant();
            string rest = space < 0 ? "" : line.Substring(space + 1).Trim();
            switch (cmd)
            {
                case "help": case "?":
                    return "Commands:\n" +
                           "  status              join code / port, uptime, players\n" +
                           "  list                the players (= players)\n" +
                           "  say <text>          a chat line to everyone, from \"Server\" (= g)\n" +
                           "  admin               lists the admins (with profiles: the staff)\n" +
                           "  master / unmaster <name|profile>   the master admin role; token (the /claimadmin token)\n" +
                           "  arenas              the arena matches and queues\n" +
                           "  settings, set <key> <value>   the settings that change while running (saved; the command line wins at a start)\n" +
                           "  factions               the factions (tag, name, members, leader, bank)\n" +
                           "  faction disband <TAG>  ends a faction\n" +
                           "  sieges              the factions' sieges\n" +
                           "  profiles            the player profiles (id, name, devices, worth, who is online)\n" +
                           "  profile delete <id> deletes a profile (not while it is online; its file is kept as .bak)\n" +
                           "  profile restore <id> brings back a profile pruned after 30 days unused (Pruned/)\n" +
                           "  stop                tells the players and shuts the server down (also quit, exit, Ctrl+C)\n" +
                           "The chat's commands, run by the same code (players by name or client id; with profiles kick, tempban, ban, unban,\n" +
                           "bans, op, deop, admin, unadmin take a profile's id or name too):" + NetCommands.ServerCommandHelp();
                case "status":
                    return $"{(NetGame.Active ? "Running" : "Not running")} {(NetGame.JoinCode != null ? $"online, join code {NetGame.JoinCode}" : $"on port {port}")}, up {Duration(Time.unscaledTime - startedAt)}, " +
                           $"{NetGame.ClientIds.Count} player(s), world seed {NetGame.Seed}, {Application.targetFrameRate} fps, " +
                           $"Debug menu {(NetGame.HostAllowsDebug ? "allowed" : "off")}, mods {(NetMods.SessionList.Length > 0 ? NetMods.SessionNames : "none")}.";
                case "list": case "who":
                    return NetCommands.RunOnServer("players", rest, null);
                case "say":
                    return NetCommands.RunOnServer("g", rest, null);   // the chat line itself is logged
                case "admin" when rest.Length == 0:
                    return NetProfiles.Enabled ? NetModeration.ConsoleCommand("staff", "") : Admins();
                case "master": case "unmaster": case "token":
                    return NetModeration.ConsoleCommand(cmd, rest);
                case "arenas":
                    return NetArena.ConsoleList();
                case "settings":
                    return NetServerSettings.ListText();
                case "set":
                {
                    int sp = rest.IndexOf(' ');
                    return sp < 0 ? "set <key> <value> (\"settings\" lists them)" : NetServerSettings.Set(rest.Substring(0, sp), rest.Substring(sp + 1), "Server");
                }
                case "factions":
                    return NetFactions.ConsoleList();
                case "sieges":
                    return NetFactions.ConsoleSieges();
                case "faction":
                    if (rest.StartsWith("disband ", StringComparison.OrdinalIgnoreCase)) return NetFactions.ConsoleDisband(rest.Substring(8).Trim());
                    return "faction disband <TAG>";
                case "profiles":
                    return NetProfiles.ConsoleList();
                case "profile":
                    if (rest.StartsWith("delete ", StringComparison.OrdinalIgnoreCase)) return NetProfiles.ConsoleDelete(rest.Substring(7).Trim());
                    if (rest.StartsWith("restore ", StringComparison.OrdinalIgnoreCase)) return NetProfiles.ConsoleRestore(rest.Substring(8).Trim());
                    return "profile delete <id> | profile restore <id>";
                case "stop": case "quit": case "exit": case "shutdown":
                    Log("Stopping the server...");
                    NetGame.StopServer();
                    return "";
                default:
                    // The chat's server commands (kick, tp, admin...): the same code as for an admin's chat line.
                    return NetCommands.RunOnServer(cmd, rest, null) ?? $"Unknown command \"{cmd}\". Type \"help\".";
            }
        }

        /// <summary>How long the server has run ("2h 05m").</summary>
        internal static string Uptime => Duration(Time.unscaledTime - startedAt);

        /// <summary>The local port (-port).</summary>
        internal static ushort Port => port;

        /// <summary>The admins' window: the server in one line.</summary>
        public static string StatusText() =>
            $"{(NetGame.JoinCode != null ? $"Online, join code {NetGame.JoinCode}" : $"Port {(Enabled ? port : NetGame.HostPort)}")}  ·  " +
            $"{(Enabled ? $"up {Duration(Time.unscaledTime - startedAt)}" : NetGame.PersistentHost ? "hosted from the game, persistent world" : "hosted from the game, fresh world")}  ·  " +
            $"{NetGame.ClientIds.Count} / {NetGame.MaxPlayers} players  ·  version {Application.version}  ·  Debug menu {(NetGame.HostAllowsDebug ? "on" : "off")}  ·  " +
            $"{(NetGame.FreePvp ? "free PvP" : "PvP in arenas and sieges")}";

        static string Admins()
        {
            var names = new List<string>();
            foreach (var p in NetPlayer.All) if (p != null && p.IsSpawned && p.IsAdmin) names.Add($"{p.DisplayName} ({p.OwnerClientId})");
            return names.Count == 0 ? "No admins. admin <id|name> makes one." : "Admins: " + string.Join(", ", names);
        }

        // ---- console ----------------------------------------------------------------------------------------

        /// <summary>A server line: the log (Player.log) and the console.</summary>
        static void Log(string text) => Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "Server: {0}", text);

        /// <summary>A command's answer: only the console (the log needn't keep lists).</summary>
        static void Answer(string text)
        {
            if (console != null) Write(text);
            else Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", text);
        }

        static void Write(string text)
        {
            try
            {
                if (ConsoleInput.Active) ConsoleInput.WriteLine(text);   // above the line being typed
                else lock (console) console.WriteLine(text);
            }
            catch (Exception) { }
        }

        /// <summary>Every log message on the console, with its time (errors and exceptions with their first trace line).</summary>
        static void Mirror(string message, string stackTrace, LogType type)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] " + (type == LogType.Log ? "" : type == LogType.Warning ? "Warning: " : "Error: ") + message;
            if ((type == LogType.Exception || type == LogType.Error) && !string.IsNullOrEmpty(stackTrace))
            {
                int nl = stackTrace.IndexOf('\n');
                line += "\n    " + (nl < 0 ? stackTrace : stackTrace.Substring(0, nl)).Trim();
            }
            Write(line);
        }

        /// <summary>The console's own line editing (Tab completion): only in this server's console window (Windows) or on a
        /// terminal (Linux), not with piped input or output; false = the plain line reader.</summary>
        static bool StartLineEditing(bool ownWindow)
        {
            try
            {
#if UNITY_STANDALONE_WIN
                if (!ownWindow || !WinConsole.RawInput()) return false;
                ConsoleInput.Start(WinConsole.ReadKey, console, l => commands.Enqueue(l), CommandNames, WinConsole.EnableVt());
#else
                if (Console.IsInputRedirected || Console.IsOutputRedirected) return false;
                _ = Console.KeyAvailable;   // throws without a terminal
                ConsoleInput.Start(ReadTerminalKey, console, l => commands.Enqueue(l), CommandNames, true);
#endif
                return true;
            }
            catch (Exception) { return false; }
        }

#if !UNITY_STANDALONE_WIN
        static ConsoleInput.Key? ReadTerminalKey()
        {
            var k = Console.ReadKey(true);
            return new ConsoleInput.Key
            {
                character = k.KeyChar,
                enter = k.Key == ConsoleKey.Enter, backspace = k.Key == ConsoleKey.Backspace, escape = k.Key == ConsoleKey.Escape,
                tab = k.Key == ConsoleKey.Tab, up = k.Key == ConsoleKey.UpArrow, down = k.Key == ConsoleKey.DownArrow,
                shift = (k.Modifiers & ConsoleModifiers.Shift) != 0,
            };
        }
#endif

        void OpenConsole()
        {
            if (Application.isEditor || HasFlag("-noconsole")) return;   // the Editor: the Console window and Run()
            // Unity prints its log to a standard output it was started with (redirected, a terminal, -logFile -): only a
            // console window opened here gets the log mirrored; elsewhere the console adds only the command answers.
            bool mirror = false;
            Stream input = null, output = null;
            try
            {
#if UNITY_STANDALONE_WIN
                // A GUI program: without redirected output, a console window of its own (a parent's console would share its
                // input with the shell that started it).
                // Its own window unless the output goes to a file or pipe (with -logFile, Unity's stdout is that log file:
                // the window then still opens).
                if (Value("-logFile") != null || !WinConsole.HasOutput())
                {
                    mirror = true;
                    WinConsole.Open($"{Application.productName} server (port {port})");
                    WinConsole.OnClose(() =>
                    {
                        commands.Enqueue("stop");
                        Thread.Sleep(1500);   // closing the window: the goodbye goes out before Windows ends the process
                    });
                }
                input = WinConsole.Input();
                output = WinConsole.Output();
#else
                input = Console.OpenStandardInput();
                output = Console.OpenStandardOutput();
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; commands.Enqueue("stop"); };
#endif
            }
            catch (Exception e)
            {
                Debug.LogWarning("Server: no console (" + e.Message + "); the log only.");
            }
            if (output != null)
            {
                console = TextWriter.Synchronized(new StreamWriter(output, new UTF8Encoding(false)) { AutoFlush = true });
                if (mirror)
                {
                    consoleLog = true;
                    Application.logMessageReceivedThreaded += Mirror;
                }
            }
            if (input != null && console != null && StartLineEditing(mirror)) return;
            if (input != null)
            {
                var reader = new StreamReader(input, Encoding.UTF8);
                var thread = new Thread(() =>
                {
                    try
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null) commands.Enqueue(line);
                    }
                    catch (Exception) { }
                }) { IsBackground = true, Name = "Server console" };
                thread.Start();
            }
        }
    }
}
