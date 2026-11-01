# Multiplayer: servers

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Multiplayer (remake-only)

- **Server settings while running** (`NetServerSettings`; the operator guide is `SERVER.md`, keep it in step with the
  server's options and commands): name, password, maxplayers, allowdebug, freepvp, maxearn, claimcost,
  maxclaims, claimdays, siegecost, toll. Changed by admins (`/set <key> <value>`, `/settings`, the Admin tab's Server
  settings: a field or switch with Save each) or the console (`set`, `settings`); applied at once where possible
  (allowdebug / freepvp through `NetState.SetDebugAllowed` / `SetFreePvp`, the toll through `SetToll`; the name at the
  next listing, `DedicatedServer.ListName`; a higher player limit online after a restart) and saved to
  `server_settings.json` beside the profiles. A start applies the defaults, then the file, then the command line (an
  option given there wins: `FromCommandLine`, the window says "set by the launcher"). The password is never sent.
- **Moderation** (`NetModeration`, a dedicated server with profiles; its commands are `NetCommands` rows, so they show
  in /help and Tab completion; a profile's role is on `NetPlayer.StaffRole`, and admins / masters also get
  `NetCommands`' admin commands; on such a server `/kick` and `/admin` / `/unadmin` are this system's, elsewhere the
  session-only ones): roles on the profile (`Account.role`: 0 player, 1 op,
  2 admin, 3 master). The **master admin** (the owner) is claimed in the game: `/claimadmin <token>` (or the station
  window's Profile tab, "Claim this server"), the token from `-admintoken` / `GOF2_ADMIN_TOKEN`, else
  `admin_token.txt` beside the profiles (made at the first start; logged at every start; console `token`; a wrong try is
  logged, one check per 5 s), so a server needs no console. Masters make admins (`/admin`, `/unadmin`) and delete
  profiles (`/deleteprofile <id>`, not while online); admins make ops (`/op`, `/deop`), announce (`/say`, as
  "[admin] Name") and end factions (`/disband <TAG>`); the console can do everything and makes masters (`master` /
  `unmaster`). Nobody acts on a pilot of their own role or higher, or gives a role as high as their own. Ops: `/kick <pilot> [minutes] [reason]`
  (dropped; can't rejoin for the minutes, default 5, a
  temporary ban), `/tempban <pilot> <minutes> [reason]` (ops at most 24 h), `/unban <name|profile>`, `/bans`; admins
  also `/ban <pilot> [reason]` (for good). `/staff` for everyone. Nobody acts on their own rank or higher (the console
  on anyone). A pilot: a name online, `#<client id>` (the window's buttons) or a profile's id / last name offline. A ban
  holds the profile and every device label it signed in from (no new profile from the same device), checked in
  `NetProfiles.OnLogin` (dropped with the reason and the time left); every device of a banned profile online is
  dropped; kicks and bans are announced. `bans.json` beside the profiles; run-out bans are pruned. Console: `kick
  <id|name> [minutes] [reason]`, `ban`, `tempban`, `unban`, `bans`, `op`, `deop`, `admin`, `unadmin`, `staff`. The
  station window's Admin tab (ops and above: `NetPanel.State.role`, `bans`, each pilot's `role`): reason and minutes
  fields, Kick / Ban (minutes) / Ban for good / Make op / Remove op / Make admin (masters) per pilot, Unban per ban; for
  admins also the server's status line (`DedicatedServer.StatusText`) and an announcement field, the staff (remove op /
  admin), every profile (`NetProfiles.FillProfiles`: at most 200, the most recent first; a filter; Ban / Unban, roles,
  Delete for masters) and the factions (Disband). Dangerous buttons (Ban for good, Delete, Disband) ask twice. Not tested
  in a build yet.
- **Debug menu in multiplayer** (`NetGame.HostAllowsDebug` → `NetState.DebugAllowed`, a NetworkVariable set when the
  world spawns, fixed for the session): off by default. The Host card's **Debug menu** Off / Allowed segments
  (`mp_allow_debug`), a dedicated server's `-allowdebug` (the launchers' `ALLOWDEBUG=1`; `status` and the startup log
  say which). `Cheats.Allowed` = no session, or the session allows it: then every player's pause / station menu has the
  Debug page, whether that device ever opened the main menu's Debug panel or not (`Cheats.PageShown`; the station's
  entry follows a change while docked); otherwise the pages are gone, the Options "Debug tools" row is hidden, and every cheat flag reads off (`Cheats.On`), so
  toggles left on in single player don't carry into a session (their saved values are kept). Client-side only: a
  modified client can still cheat.
- **Server checks** (`NetGuard`, `NetRateLimit`): every client request goes through the server, which checks it and limits
  its rate before it acts or passes it on. The server's messages to the players are `InvokePermission = RpcInvokePermission.Server`
  (Netcode's default lets any client send any RPC, and relays it): hits, EMP, shots, blasts, jump effects and kill credits
  go up to the server (`HitUpRpc`, `ShotUpRpc`...) and down from it. Checks: finite numbers in range (positions within 1e6 m,
  damage up to 1e6, 1e8 with the debug menu allowed), stations / items / ships that exist, where the sender is (in that
  orbit, the same orbit, docked there), never a squadmate's weapon, payload caps (mission JSON and hangar snapshots 4096,
  commands 32 / 512). Invitations are recorded on the server (an acceptance needs one, at most 60 s old); a mission is
  recorded with its team (only team members report results, a share capped by the reward); dealer ships need a
  reservation; a kill notice needs a hit from that killer within 30 s; proxies / crates spawn only for a player in that
  orbit (at most 300 / 150 each); a client despawns only its own proxies and crates; a pose batch (NetPoses) only for
  objects the sender owns in the orbit it flies in (else the whole batch is dropped). A token bucket per client and
  request kind drops a flood (logged once a minute); 1500 weighted drops in a minute (shots 0.1, trades 0.2) kick the
  client. The host's own player is exempt. Still trusted (no server-side world): each player's own ship, credits, cargo,
  standing and position, damage within the cap, and the orbit authority's NPCs.
- **Dedicated server** (`DedicatedServer`, `NetGame.StartServer`): the normal Windows / Linux player started with `-server`
  (with `-batchmode -nographics`; `-relay` online with a join code, listed as `-name "..."` unless `-unlisted`;
  `-password`; `-maxplayers` (2..100, default 16); `-allowdebug` (the Debug menu, see "Debug menu in multiplayer");
  `-port`, default 7777; `-fps`, default 60; the `GOF2_SERVER` environment
  variable does the same in the Editor's Play mode ("relay" = -relay), commands through `DedicatedServer.Run`). Every
  Windows / Linux build gets a launcher next to the game (`DedicatedServerLaunchers`: `Start Dedicated Server.bat` /
  `start-server.sh`, the name, password, player limit and `ALLOWDEBUG` at the top, online and listed; `-password` only when one is
set: Unity drops an empty "" argument, so `-password ""` read the next option as the password; `NetGame.CommandLineValue`
now takes an option right after another (a dash and a letter) as no value). `Bootstrap` hands over before
  the first scene wakes and swaps in an empty scene; the main menu scene never runs: in the Editor it is loaded already
  and switched off at once, in a player it is still loading then, so `MainMenu.OnEnable` / `MenuBackground.Awake` call
  `DedicatedServer.ShutOff` (the whole scene off before the rest wakes) and it is unloaded once loaded (before this, a
  player build ran the menu, its live orbit and its music under the server, and the menu reset the password); the
  process is muted (AudioListener volume 0, paused); none of the Bootstrap extras (options, Discord, haptics, bloom,
  the screenshot key); vsync off at the frame cap.
  `NetGame.StartServer` = StartHost's world without a player of its own (no NetPlayer, no EnterWorld); clients ids start
  at 1 (`NetState.Dedicated`: "Player N" counts from 1); the session-ending checks count the others, not the host
  (`OthersConnected`); `OnServerStopped` (a transport failure: the network or the Relay connection lost) no longer
  quits: `DedicatedServer.ConnectionLost` starts the session again (`Reconnect`: 5 s, then 10, 20, 40 and every 60 s;
  a new Relay allocation, join code and listing, the world seed kept, `StartServer(port, keepSeed)`; the players join
  again), also when an online server starts without a network; an expired anonymous sign-in is signed out (session
  token kept) and in again (`SignInForOnline`); the others' NetPlayers build no model there. The console (Windows: its
  own window through `WinConsole` unless stdout is redirected to a file or pipe (a terminal's inherited console handle
  doesn't count: a GUI program isn't attached to it) or `-noconsole`; with `-logFile` the window still opens (Unity's
  stdout is then that file); the log is mirrored into the window, Unity prints it only to a stdout it starts with;
  Linux: the terminal): joins / leaves with the client ids,
  each player's moves, chat; commands help, status, admin (alone: the admins), every chat server command
  without the "/" (players / list, g / say, w, kick, tp, admin / unadmin; see "Chat commands"; a kick's reason is the
  player's popup), stop (`NetGame.StopServer`: the goodbye, then quit; Ctrl+C
  and closing the window too). In its own console window (Windows) or on a terminal (Linux) the input line is edited by
  `ConsoleInput` (keys one at a time: `WinConsole.RawInput` / `ReadKey`, `Console.ReadKey`; a "> " prompt, log lines above
  it): Tab completes and cycles the command names (Shift+Tab back, nothing typed: all), Up / Down the history, Esc clears;
  piped input stays line by line. Verified on Windows; the Linux terminal path is untested. Linux quits hard
  (`DedicatedServer.ExitNow` on `Application.quitting`: the
  web admin and Netcode down, the log flushed, the terminal's settings from the start put back with `tcsetattr`, then libc
  `_exit`): the key reader blocked in `Console.ReadKey` kept Unity's teardown from ever ending after "CodeReloadManager
  destroyed"; the exit code is `DedicatedServer.Quit`'s (1 = failed). Windows the same (`ExitProcess` after the flush,
  2026-10-12): the key reader in `ReadConsoleInputW` held IL2CPP's
  `Runtime::Shutdown` (AbortAllThreads -> Thread::Join), and a player's "Stopping the server..." was reported as a
  native crash. Verified: the Windows build headless with the Editor as the client (join, chat, say,
  kick, stop; over Relay, listed, with a password; the console window, no menu).
- **Persistent hosted world** (the Host card's World Fresh / Persistent segments, `mp_persistent`,
  `NetGame.HostWantsPersistent` -> `PersistentHost`): `StartHost` configures `NetProfiles` on
  `persistentDataPath/HostedWorld` (`SetUpHostedWorld`: the saved `server_settings.json` too, the card's password / player
  limit / Debug menu / name winning like a command line; `NetProfiles.Start` loads profiles, factions, bans, news), so
  `NetProfiles.Enabled` (`Dedicated || PersistentHost`) turns on everything a dedicated server with profiles has. The host
  signs in to its own profile (`NetProfileClient.Begin` instead of `EnterWorld`; its RPCs to itself run locally): always a
  profile, made the master admin on every login, never banned, uploads never doubted (`NetProfiles.IsHostClient`); on
  leaving its game is saved straight into the profile (`NetProfileClient.SaveHostNow` from `NetGame.Shutdown`). Fresh =
  unchanged (nothing kept, no profiles, so no factions or Admin tab). Online errors now list the inner exceptions
  (`NetGame.Describe`: Unity Services' "Some services couldn't be initialized" said nothing on its own). Not tested yet.
- **Message of the day** (`NetMotd`, `SERVER.md`): `motd.txt` beside the server's settings (`NetProfiles.Folder`, else
  `persistentDataPath`), read at the session's start and by `/motd reload`; `/motd set <text>` (`\n` new lines) /
  `clear` (admins, the console); every space kept, tabs 4 spaces, at most 60 lines / 4000 bytes (one RPC); `%player%`,
  `%players%`, `%server%` filled per player. A game entering the world asks (`NetGame.EnterWorld` ->
  `NetState.MotdRequestRpc`), the server answers that player (`MotdRpc`); shown in `EventScreen`'s MOTD window (the
  server's name, the text in JetBrains Mono (OFL, `Resources/GoF2Fonts`, a dynamic font asset made at runtime) so ASCII
  art lines up, no rich text, 14..22 by the longest line, scrolling both ways; OK / tap / Enter / Space / Esc / A / B /
  Menu close it; the other input rests, `EventScreen.QuestionOpen`) unless this server's same text was seen (PlayerPrefs
  `mp_motd_<server name>`, set on close); `/motd` always shows it.
- **Sector news** (`NetNews`): the station ticker's multiplayer items, before the game's own (`StationMenu.SetupTicker`;
  rebuilt when the strip wraps, `tickerNewsDirty`, and every minute for the ages). The server posts them and sends each to
  everyone (`NetState.NewsRpc`), a joining player gets the last 15 (`SendAll` from `NetPlayer.OnNetworkSpawn`); the last 40
  (3 days) kept in `news.json` with profiles. Kinds with a coloured "+++ KIND +++" kicker (BREAKING under 15 minutes old),
  the item at the docked station in bold, its age: territory (claims, give-ups, lapses), war (siege declared / begun,
  held), BREAKING a station taken, factions founded / disbanded, arena (a duel's score, a free-for-all's winner; no draws or
  forfeits), defence (`NetOrbit` counts the raiders the players downed, `Target.remoteKiller`; when none is left the orbit's
  authority sends `DefenseReportRpc`, 3+ kills, checked and once per orbit per 10 minutes; the Kaamo siege broken), new
  pilots, and an admin's `/news <text | clear>` (GalNet). Player text is made tag-free (`NetNews.Safe`). Not tested in a
  build yet.
- **Web admin** (`WebAdmin`, a dedicated server started with `-web [port]` / `-webport N`, `-webbind ADDRESS` default
  127.0.0.1; the launchers' `WEBPORT` / `WEBBIND`; the operator guide in `SERVER.md`): a minimal HTTP/1.1 server on a
  `TcpListener` (works in the IL2CPP player) serving one page (`Resources/GoF2Server/WebAdmin.html`: Tailwind CSS 4's
  browser build from jsDelivr, the script inline with a per-response CSP nonce, every server text set as textContent) and
  a JSON API (`/api/login`, `/logout`, `/state`, `/command`, `/log`) run on the main thread (`WebAdmin.Pump` from
  `DedicatedServer.Update`). Login like in the game: the admin token (`NetModeration.TokenMatches`, also without profiles:
  `EnsureToken`) = the console (`DedicatedServer.Run`), or a one-time code from `/web` (ops and up, 5 minutes, bound to the
  profile) = that profile's role, re-checked every request (`NetModeration.WebCommand`: moderation commands only). Session
  cookie HttpOnly / SameSite=Strict (12 h, 2 h idle, memory only), POSTs need the `X-GoF2-Admin` header, 5 wrong logins per
  address then 5 minutes' wait, every command logged. The state reuses `NetPanel.State` (`NetModeration.FillFor`,
  `NetFactions.FillPanel`); the log tab is a 1000-line ring of `logMessageReceivedThreaded`. Not tested in a build yet.
- **Player profiles** (`NetProfiles` server, `NetProfileClient` player; a dedicated server only, on unless `-noprofiles`;
  `-maxearn N` default 1 000 000, `-profiledir`): `<persistentDataPath>/ServerProfiles`
  holds `accounts.json` (the server's id, each account's devices with their token hashes, name, squad key, worth) and
  one `<account>.json` per profile, a `SaveData` without the shop memory (`SaveGame.ProfileJson`; loaded with
  `SaveGame.ApplyProfile`: the session's rules, no squad mission or Courier containers, docked where it was, an orbit
  without a station = Var Hastra). Files are written through `.tmp` with the previous one as `.bak`. Signing in: NetState
  carries `ProfilesOn` / the server id; a joining game doesn't enter the world at once but sends `LoginRpc` (its token
  for that server from PlayerPrefs `mp_token_<server>` (+ a -mpname hash), and a SHA-256 label of
  `SystemInfo.deviceUniqueIdentifier`), the server answers with a header (new token, role) and the gzipped profile in
  4000-byte chunks (Unity Transport's 6144-byte payload limit), then the game enters the world. No known token = a new
  profile; no limit on their number (the old `-maxprofiles`, default 50, turned new players into guests): a profile
  nobody signed in to for 30 days (`NetProfiles.PruneDays`, by lastSeen) is pruned at the start and hourly
  (`NetProfiles.Prune` from `Tick`; never online or staff), its file and its accounts.json entry moved to `Pruned/` (the
  newest 50 kept), leaving its faction; the console's `profile restore <id>` brings it back (`ConsoleRestore`). Console
  (and the web admin's `/api/command`, for control panels): `profile info | delete [force] | block [reason] |
  unblock <id|name>`, `profile blocked` (`ConsoleInfo`, `ConsoleDelete`: force signs the devices out and drops them first;
  block / unblock = `ban` / `unban`). Uploads (gzipped chunks, `UploadChunkRpc`): on every
  docking (`SaveGame.AutoSave`), every 60 s, when leaving (`NetGame.Shutdown`); each is checked like an imported save
  (`SaveGame.TryParse`), and without `-allowdebug` also turned away when the profile's worth (credits + ship prices +
  items at `minPrice`, `NetProfiles.Worth`) grew more than 2 000 000 + `-maxearn` per minute online since the last
  accepted one, or for hulls 13 / 14 / 15 (the player gets "The server didn't save your progress: ..."). The money log
  (`NetAudit`, `Server: audit` lines, SERVER.md "The money log"): uploads whose worth grew 100 000+ (per part, the item
  gains; a warning from 150 000 / min or a refusal), each player's shop runs per item and station (a unit price outside
  the item's range a warning), faction deposits / withdrawals / tolls. Not a server
  authority: the client still runs the economy (the plan's phase 2: server-priced trades, claimed rewards). Kaamo upgrades
  past `Session.MaxModLevel` per mod and hull (builds of 2026-10-08 stacked them uncapped; a player had ten of each) are cut
  back at sign-in (`SendProfile` rewrites the file) and on every upload (`NetProfiles.ClampMods`). A refused upload is logged
  once per profile per 10 minutes with the count since, and the online staff (op and up) get a notice as often. Reconnect
  loops (a profile, or a device without one, signing in more than 8 times in 10 minutes) are turned away for 10 minutes
  (`ReconnectRefusal`, before the world: no join in the log or the chat). The join log line carries the profile and a
  short device label (`NetProfiles.Describe`). Devices: one
  connection per device (signing in again drops the older one); several devices of one profile = the first controls it,
  the others are observers (`NetPlayer.Observer`, server-written; `NetProfileClient.Refusal`: the station menu's
  Hangar / Lounge / Map / Launch refused; their uploads ignored); a controller leaving promotes the next device online
  (it gets the latest profile and reloads the station). Chat commands (answered privately, never relayed): `/link` (a
  6-letter code for 5 minutes, controller only), `/link CODE [force]` (this device joins that profile with its own token;
  its own profile is deleted when it was the only device, `force` needed past 5 minutes of play), `/control` (an observer
  takes over once the controller is docked: the controller is demoted and uploads once more (`RequestUploadRpc`), then
  the observer gets the profile; 5 s timeout = the last saved one), `/profile`. Squads: the account keeps a squad key
  (set on `AcceptInviteRpc`, cleared on `LeaveSquadRpc`, kept through a disconnect); a controller signing in joins a
  squadmate online (`NetState.RestoreSquad`, anywhere, not only in a hangar). Server console: `profiles`,
  `profile delete <id>` (not while online), `list` marks observers. Not tested in a build yet.
