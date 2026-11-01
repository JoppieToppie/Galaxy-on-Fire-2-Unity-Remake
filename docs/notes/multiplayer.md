# Multiplayer

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Multiplayer (remake-only)

Netcode for GameObjects 3.0 (`com.unity.netcode.gameobjects`, which now also brings Netcode for Entities, Entities, Burst and
Collections) over Unity Transport: online through Unity Relay with a join code and the Unity Lobby server browser
(`com.unity.services.multiplayer`, project linked to Unity Cloud, Relay and Lobby on in the dashboard, anonymous Unity
Authentication), or on the local network by direct IP, port 7777. Code in `Scripts/Runtime/Net/Multiplayer` (`GoF2Remake.Multiplayer`);
the network prefabs in `Resources/GoF2Net` (**GoF2 > Build > Network Prefabs**: NetPlayer / NetProxy / NetState / NetCrate, each a
NetworkObject; it re-saves them so each gets its own GlobalObjectIdHash, and switches Android's internet permission on).
Single player is untouched: every multiplayer path runs only while `NetGame.Active`.

- **One shared world, no scene synchronisation** (`EnableSceneManagement = false`): every player starts a fresh free-play
  game in the finished game's world (below) docked at Dis (70) and plays it like single player, their own scenes, economy, map,
  jumps and docking. A new pilot starts in Betty with 10 000 credits and a simple loadout (Nirai Impulse EX 1, Targe Shield,
  Telta Quickscan, IMT Extract 1.3; `NetGame.StartShip` / `StartCredits` / `StartEquipment` in `PrepareSession`), not
  resetGame's Phantom, so they work up to better ships; a server profile replaces it once it loads. The network objects
  live in DontDestroyOnLoad and each player shows only what is where they are.
  The host spawns NetState and its own NetPlayer and loads `Station`; a client connects (10 x 1 s) and loads `Station` when
  NetState reaches it (`NetGame.EnterWorld`); each connecting player gets a NetPlayer. Nothing is saved in a session
  (`SaveGame.Save` refuses), so it never touches the single-player saves; a dedicated server keeps player profiles
  instead (see **Player profiles**).
- **Co-op campaign** (the Host card's World: Co-op): the host's story for 2-4 players instead of the finished world; see
  docs/notes/multiplayer-coop.md.
- **The finished game's world** (`Session.UseCompletedWorld`, `Session.CompletedWorld`; set by `NetGame.PrepareSession` and
  `SaveGame.ApplyProfile`): free play (no story steps) at campaign index 162, so everything that reads the index sees the
  won game: the full shop stock and dealer ships (Kothar's 37 / 38 / 40, the Vossk 39 / 41, the carrier and Vossk battleship
  in traffic, Mido's dealers), and the medal / Most Wanted gates count as met (Thynome's VoidX, Katashán's Specter,
  Quineros' 45-48); the add-ons' revealed systems on the map (`Story.RevealedSystems`, `GalaxyMap.Visibility`); Ginoya after
  the supernova (normal sun, no flares, its traffic and shops); no wormhole, Void invasion or Kappa's free EMP GL I. Remake
  picks: no storms anywhere (`SkyLayers`); Naneroh, Valpatro and Luur intact and open for docking (`OrbitLayout.IsRebuiltGinoya`;
  Luur its pre-supernova model, Naneroh / Valpatro borrow Midantha's / Tergalon's model and volumes, `OrbitBuilder.StationLook`);
  sessions start docked at Dis (70, `NetGame.Station`), whose shop always sells the Khador Drive (85). The Cronus (37), 0 in the Android
  economy (step 77 sells it for nothing), costs 1 150 000 there (`Database.CronusSessionPrice`, applied by `Database.Load`
  while `Session.CompletedWorld`; `PrepareSession` resets `NetGame.Db`): a hull of its stats (~500 000) plus its Khador
  Drive. The world's look and menus read `Session.WorldIndex` (free play's 20, else the index);
  NPC hulls / reloads and the Kaamo outposts use the won game's capped 45 / 180. Naneroh keeps the original's
  gamma rays after the story, 1/s (and the gamma blaze with a gamma shield), like single player (#80; first the remake had none).
- **Menu**: the main menu's Multiplayer button (between New game and Load) opens the Multiplayer panel (a fixed 84 %
  high panel: the title with the "Experimental" badge and the pilot name `mp_name`, a one-line intro, then the server
  browser and the host column). **Pilot name required**: without one (`NetGame.PlayerName` empty, no `-mpname`) opening
  the panel asks for it in the menu's dialog (`MainMenu.NeedsName`: its `dialogField`, OK refuses an empty name, Back
  closes), and so do Host, Join and a server browser row before they start (`WithName`). **Server browser**
  (`NetLobby.Query` every 5 s while the panel is open; rows rebuilt
  only when the list changed, so a controller's focus stays): every listed game of every version, this version's first
  and the fullest first; two lines per game: its name with SERVER (dedicated) / PASSWORD / MODDED tags and PVP / PVE
  (`NetLobby` key `pvp`, kept current like the player count; none for older listings) and at its right
  the region and ping (remake, players' request 2026-10-22; `NetPing`): the host publishes its Relay allocation's region
  (`NetGame.HostRegion` = `Allocation.Region`, lobby key `region`), shown short ("europe-west4" EU West, `NetPing.RegionName`:
  the area and side, the data centre's number left out; untranslated, like place names), then a dot (green up to 80 ms,
  amber up to 160, red above, `NetPing.Quality`) and "N ms" ("..." until measured): the round trip to that region from
  Unity's QoS service (`com.unity.services.qos`, which the Multiplayer package brings; `GoF2.Net` references it), one
  measurement per region for every game in it, at most once a minute, the regions of the listed games only, written into
  the rows in place (`NetPing.Revision`, not part of the row key, so a reading never rebuilds the list under the focus);
  the player's leg to the relay, not the whole path through the host; none for a listing from before the key. Measured in
  the Editor (2026-10-22, from the Netherlands): all 11 Relay regions, EU West 9 ms .. Australia Southeast 319 ms. Then the host, players /
  limit ("full") and the version (another version: amber "needs X", not joinable, a tap says why); a tap joins by its
  join code (a password game with the password field empty asks for it first); "N games" by the title. Under it one
  row: the code-or-address field (`mp_address`), the password field (this run only; `-mppassword` for testing), Join.
  **Host a game**: Public / Invite only / Local network (`mp_mode`); online = a Relay session (Public also listed under
  the Game name field, `mp_session_name`, default "<pilot>'s universe"); the password field (`mp_host_password`,
  optional, every mode) and Max players beside it (`mp_max_players`, `NetGame.MaxPlayers`: 2..100, the host included,
  default 16; Relay's connections are that minus a player host, the browser's limit is it, and the connection approval
  turns away a player past it in every mode, "The game is full (N players)."); **Combat** PvE / PvP (`mp_pvp`, PvE by
  default: `NetGame.FreePvp`, which NetState carries; PvE = players fight only in arena matches and faction sieges, PvP
  = anywhere; a dedicated server's `-freepvp`, the launchers' `PVP=1`, the server settings' "freepvp"); Local network
  shows every address of this device others could join on, named by adapter (`NetGame.LocalAddresses`: Ethernet / Wi-Fi
  first, then VPNs like Hamachi,
  ZeroTier, Radmin, Tailscale; not down, loopback, link-local or virtual-machine adapters; Android's Linux names
  mapped: wlan = Wi-Fi, swlan / ap = Hotspot, rndis / usb = USB, tun = VPN, mobile data (rmnet, ccmni) left out; Windows'
  mobile hotspot = Hotspot; none = a "connect to Wi-Fi" line, never 127.0.0.1), a tap copies it (with
  ":port" when not 7777), and the port field (`mp_port`, `NetGame.HostPort`, default 7777); the host listens on all
  adapters (0.0.0.0). A port in use is caught before the fade (`NetGame.CanHost`: UDP bind test), the panel says so and
  puts the next free port in the field. The address field takes a join code (6 letters / digits, `NetGame.IsJoinCode`)
  or "host" / "host:port" (a name is looked up, `NetGame.ParseAddress`); the status line under the cards gives the last
  session's end reason. The host card's settings (text, addresses / port, name, password / player limit, the Debug / Combat /
  World / Mods switches two to a row) sit in `mpHostScroll` between the mode tabs and Host: at 16:9 they fit; when they
  don't (three or more adapters, short screens) they scroll by wheel / touch / focus with an amber "More options" hint
  under them (`MainMenu.SetupHostMore`; the scroll bars are hidden everywhere). Before, the four switch rows squeezed the
  local network's address list and port field flat. Touch presses don't take the focus in the menu, except in a text field (the on-screen keyboard
  needs it). Keys / controller (`MainMenu.NavigateMultiplayer`, a Discord report: the host card couldn't be reached from
  the server browser): the neighbour on screen (`SpatialNav`), up / down inside the card first (the two cards' rows
  don't line up), then out to the name field / Back, left / right across the cards; a controller's D-pad never counts
  as typing in a field (`TextFieldKeys.IsTyping` with `InputMode` Gamepad: on the Xbox a key the system keyboard left
  "pressed" kept the focus in the fields).
- **Online (Relay / Lobby)** (`NetGame.PrepareOnlineHost` / `StartClientOnline`, `NetLobby`): hosting online signs in
  (anonymous; a profile per `-mpname`, so two games on one machine are two players), reserves a Relay allocation (`NetGame.MaxPlayers`
  connections, one less for a player host; a dedicated server's `-maxplayers`) and its join code before the fade, then StartHost
  / StartServer connect through it (`RelayServerData`, "dtls"); the code goes to the clipboard and shows on its own plate
  under the station's system information (Copy; `SquadView`); a client joins the allocation by its code and connects. A listed game
  is a public lobby (`NetLobby.Publish`: name, host, players (N1), dedicated, password, version (S1), the join code;
  nobody joins the lobby, it is only the listing): a heartbeat every 15 s, the player count updated as it changes,
  deleted with the session (a game that died drops out after Lobby's 30 s). Errors in plain words (`OnlineError`: no
  game with that code, no internet, else the service's message).
- **Version check and passwords** (Netcode's connection approval, `NetGame.Approve`): every connecting game sends
  `NetGame.Protocol` (the code's fingerprint, `BuildVersion.Fingerprint`: the Editor's `BuildFingerprint` hashes the runtime
  scripts, `Resources/GoF2Net` prefabs, `Resources/GoF2Data` JSON and the resolved versions of the multiplayer packages
  (Netcode, Unity Transport, Multiplayer Services, from `packages-lock.json`; not the rest of the package list: a build-only
  package such as the Linux toolchain mustn't split players), paths sorted, line endings normalised, SHA-256's first 12
  hex digits; `BuildVersionStamp` writes it into `Resources/GoF2Build/BuildFingerprint.txt`
  for the build, git-ignored and deleted afterwards), its password and its shown version (`ConnectionData`); builds of the
  same code play together whenever and for whichever platform they were built (verified: two Windows builds 2 minutes
  apart), the server browser and its sort use the fingerprint too (lobby key `protocol`; "another build" for the same shown
  version with other code); only the same fingerprint gets in ("This game runs version X, yours is Y..."; the Editor as
  server takes any version, and every build
  lets the Editor in, for testing: it skips only the version check, not the password), then the session's password if it
  has one (`HostPassword`: "needs a password" / "Wrong
  password."); the reason is the player's popup. The server checks it: the lobby's join code is public. Builds from
  before the check don't use approval, which is part of Netcode's config hash, so they fail its handshake. Verified with
  a Windows build and the Editor: the version refusal, no / wrong / right password over Relay, the listing and its count.
- **NetPlayer** (one per player): the owner writes where they are (station; `Place` Space / Hangar / Departing, from the
  scene it is in and `StationLevel.PlayerDeparting`), ship and name; in space its pose, hull and look go in the pose
  stream to the players in that orbit and the server only (`NetPoses`, docs/notes/multiplayer-sync.md "Bandwidth"). The others in the same
  orbit (`SharesOrbit`) see the ship model with the pilot name, lockable, a sphere `Obstacle` (1.6 x the model's bounding
  radius) the local ship slides along; elsewhere it is hidden. Remote motion is smoothed (`NetSmoothing`: velocity
  estimate, at most 250 ms ahead, eased, snapped beyond 250 m). It also writes its shield / armor fractions (-1 = none),
  which its copy hands its Target (`netShield` / `netArmor`) for the HUD marker (`CombatView.DrawPools`, players' request):
  near, a second frame of the faction's bar 11 px above the hull bar with the shield in the status bar's cyan, and the hull
  bar's fill in the status bar's red with the yellow armor over it (shield, armor, then hull drain, `Player::damage`); a
  pool of -1 is left out, NPC markers (both -1) are unchanged.
- **Players against players**: another player is a yellow (neutral) marker until one of the two attacks the other
  (weapons or EMP, 3 hits or 50 damage within 10 s, so a stray shot doesn't count: the victim's game counts in
  `NetPlayer.HitRpc` / `EmpRpc`, the attacker's game in `RemoteDamage` / `RemoteEmp`): then both are enemies to each
  other for 120 s from the last hit either way, like an NPC that turned (`NetAggression`): red markers, and the auto
  turrets, a debug hull's turrets and the sentry guns fire (they read `hostileToPlayer`); forgotten when either is
  destroyed or the session ends. Hits on their ship (guns, missiles, blasts,
  NPC shots from the authority's side) go to their game (`NetPlayer.HitRpc`, `Target.RemoteDamage` with the NPC flag),
  which applies them to its own ship. Destroyed by a player: "X was destroyed by Y." for everyone
  (`NetState.DestroyedByRpc`). A destroyed player's game over says "Tap to respawn at the station." and docks them at the
  station they last docked at (`Session.LastDockedStation`, set by `StationLevel`, saved as `lastDockedStation`), repaired
  (no save is loaded). Players can only hurt each other in an arena match (below) or on a
  server started with `-freepvp` (`NetState.FreePvp`, `NetGame.FreePvp`): elsewhere another player's ship is
  `playerProof` (shots pass) and the owner's `HitRpc` / `EmpRpc` drop a player's hit unless the sender is in the same
  orbit and may fight (`NetPlayer.PvpWith`).
- **Join / leave notices** (`NetPlayer`): a join shows in the others' chat only after the player has stayed 15 s, and not
  when they left under 2 minutes before (a relog); a leave only when their join was told, once per 2 minutes per name.
- **Leaving**: closing the game leaves the session (`Application.quitting`); a player whose game died is dropped after 5 s
  (`DisconnectTimeoutMS`); a leaving player's ship goes at once. The host leaving with players connected first tells
  them why (`NetState.SessionEndingRpc`, "The host ended the session.") and closes 0.35 s later (`NetDelayedShutdown`;
  closing the window waits for it through `Application.wantsToQuit`, then quits). A session ending while playing takes a
  client back to the main menu's Multiplayer panel with the reason in a popup (`NetGame.PopupPending`, taken when the
  menu opens, `MainMenu.ShowNotice`); Netcode's own "[Disconnect Event] ..." texts become "The connection to the host was
  lost." / "No host found at that address." ("The host ended the session." when the host answered first); `-mpjoin`
  clients then keep trying to rejoin. The flight HUD's and the station menu's Back to Main Menu end the session. Netcode
  stopping a session by itself (`OnClientStopped`, a transport failure) is handled like a lost host. While the host
  closes, `NetGame.Active` is false and new connections are turned away with the reason. `NetGame.SessionGame`: a
  session's game (from PrepareSession until the menu opens after it) is never saved, even once the connection went, and
  a Space / Station scene loaded after the session ended goes to the menu (`NetGame.SessionLost`). The main menu always
  unmutes the listener (a pause menu open when a session ended). Every session plays on Normal difficulty.
- **Other players' ships look and sound like theirs**: the owner writes its engine state (the glow part showing: off
  while mining, docking at an object, dead), boost (`BoostVisualPercent`) and cloak percentage; the others' copy drives
  the glow parts (`AssembledObject.SetExhaust`), the exhaust particles (`ShipExhaust.AttachRemote`), a 3D engine loop
  (`HangarFlight.AddEngine`, pitched up while boosting) and the cloak (`NpcCloak.Show`: the hull dissolves, lights and
  glow hide from 25 %; `Target.cloaked` and `untargetable` from the moment it engages: off the radar, the markers and
  the lock, a held lock dropped (`CombatRadar`), the missiles on it shaken off). Their jumps too (`SystemJump.ChargeStarted` /
  `JumpFxStarted` -> `NetPlayer.ChargeRpc` / `JumpFxRpc`): the Khador Drive's charge (sound 33) and its khador_jump fx with
  sound 32, a jumpgate's `_jump_anim_add` (back to idle afterwards) with sound 31; the ship goes when theirs vanishes
  (`NetPlayer.visible`, the owner's model shown), with an explosion when it was destroyed (the death's model hiding).
- **EMP**: the player's EMP on another game's ship is relayed (`Target.RemoteEmp`): on an NPC proxy its owner's ship
  takes it (`NpcShip.OnRemoteEmp`: past a third of its EMP points it turns on that player, like `OnPlayerEmp`), an
  EMP-disabled proxy shows its lightning to everyone (`EmpSparks`); on another player (remake pick: players have no EMP
  pool) it drains that much shield and shows the lightning on their ship for 1.5 s. Squadmates are exempt.
- **Standings per player**: each player shares their standing axes and signature (`Standing.*With`), and their
  standings toward mods' races (2026-10-28, `NetModRaceStandings`: "key=value;" in a `FixedString4096Bytes`
  `NetPlayer` variable, packed again only when `ModRaces.StandingRevision` or the Session table changes; before, an
  authority judged everyone by its own mod race standings); an orbit
  authority's NPCs are hostile to another player whose own standing makes the race an enemy, and each player's markers
  use their own standing. Another player's hits turn a ship on their squad by the single-player friendly-fire rule (half
  its hull, a quarter on Extreme; `NpcShip.OnRemoteHit`), and whoever destroys another game's NPC gets the kill in their
  own session (`KillCreditRpc`: `Standing.ApplyKill`, the kill counted when it was after them, pirate kills).
- **Joining**: the menu stays up while connecting ("Connecting to ..."), it fades only once connected; `-mpjoin`
  clients open the Multiplayer panel and keep retrying quietly.
- **Gameplay rules** (`NetRules`, NetState's `rules` bit mask): the Gameplay options that change how the game plays are
  one rule for the whole session, since every orbit's NPCs, loot and missions run in one player's game: cloak shakes off
  pursuers, pirate outposts and bosses, capital ship enhancements, the informer missions' rule, stackable Kaamo Club
  upgrades, stored ships keep their equipment, the cloak bay, EMP breaks cloaks, smarter NPC targeting, the ship
  collision mode. `Settings`' getters return the session's value while a session runs (the
  host's `NetRules.Host`, a client NetState's); a player host takes its own options at hosting and changing one while
  hosting applies to everyone at once (`NetRules.HostChanged`); a dedicated server starts at the defaults, each flag
  (`-cloakhides`, `-capitalships`, `-informeroriginal`, `-nopirateevents`, `-nokaamostacking`, `-nokaamoequipment`,
  `-nocloakbay`, `-noempcloak`, `-smarttargeting`) turns
  one the other way (the ship collision mode is `-shipcollision exact|mixed|original`, two bits set as one setting,
  `NetRules.SetCollision`), and the server settings (`cloakhides` ...) change them while running. The Kaamo Club upgrade
  limit
  is a number beside the bits (`NetRules.HostKaamoLevel`, `NetState.KaamoLevel`, `GameHooks.SessionKaamoLevel`;
  `-kaamomaxlevel N`, `/set kaamomaxlevel`, 2..20, default 5). So is the flight model (2026-10-16; players flying free
  flight beside the original in PvP had other turn rates, strafing and drift): `NetRules.HostFlight` (`SessionRules`:
  FlightStyles.Original "gof2", Free "es2", or `PlayerFlight` -1 "player" / "any" / "unrestricted" = each player's own
  option, the default since 2026-10-17 (the user: the players choose; it was gof2); no Host card switch, the user found
  "free" confusing there): a player host's session starts at player, a dedicated server's
  `-flightmodel gof2 | es2 | player` (the launchers' `FLIGHT`), and admins' `/flightmodel [gof2 | es2 | player]` (no
  argument shows it; `NetCommands.FlightModel` -> `NetServerSettings.Set`, saved where the server keeps its settings) or
  `/set flightmodel`; `NetState.FlightModel` (sent as style + 2, 0 = not
  arrived), `GameHooks.SessionFlightStyle` -> `ModFlight.Decide` right after an event's style (an admin's /flightstyle still
  wins), before a mod ship's or campaign's; the Options row then says "Now set by the multiplayer session". The other players' rows are
  hidden (`OptionsCatalog.SessionRule`, `NetRules.ClientLocked`). Options that only change what one player sees or how
  they steer (target lock, salvage markers, cameras, dialogue, hints) stay each player's own.
- **Medals** are off in sessions (`Achievements.Check` / `Elite` award nothing, the Status window hides the medal column).
- **Off while a session runs**: the game pausing (the pause menu and dialogs keep `Time.timeScale` at 1; fast-forward is a cruise mode, see "Navigation";
  the flight controls stop instead while a menu, conversation or map is open, `Navigation.InputHalted`; the pause menu
  doesn't mute the sound), the Time Extender (the world can't slow for one player; nothing sells or drops it in a
  session, `Shop.NotInSession`), saving and loading (the station's
  Save / Load entries are hidden, `SaveGame`).
- Testing: the Editor as host (set `Application.runInBackground = true` during Play, or Play mode stalls unfocused; an
  edited script recompiles and ends the session) and a Windows development build (the Windows build profile, into
  `Build/Windows`) as client: `GoF2Remake.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -mpjoin 127.0.0.1
  -mpname Pilot2` (`-mpjoin`: the menu skips its intro and joins, again every 2 s until a host answers; development
  builds also take `-mpdock` (docks once, a few seconds into the first flight after launching), `-mplaunch N` (launches
from the hangar once, N s after docking: the bandwidth measurement's client) and `-mpaccept` (accepts squad invitations
  while docked) and `-mptrade` (accepts trade requests while docked, offers 500 credits, accepts once the other side
  has) and `-mphost` (hosts from the menu), so the real hangar / squad flows run without a hand on the client
  (a phone: `adb shell "am start -n com.joppietoppie.gof2remake/com.unity3d.player.GoF2GameActivity -e unity
  '-mphost -mpname Phone'"`). **Switch the active build profile before building another platform**: URP picks the shader
  variants to keep from the *active* build target's quality levels (`ShaderBuildPreprocessor`), so an Android APK built
  while Windows was active stripped URP's post-processing shaders (UberPost) and rendered lit geometry black;
  `BuildTargetGuard` (Editor) now fails such a build with that message. (The URP assets' prefiltering fields change
  with every build: harmless, URP rewrites them.) `-mpname`: this
  process's pilot name, not saved; explicit `-screen-*` options win over the window-mode option). The player has its own
  PlayerPrefs. Or the phone's development build joining the PC's LAN address. Multiplayer Play Mode (Editor clones)
  doesn't work in 6000.7.0b2: the clones fail to load URP's package shaders ("Host type is not matching any asset type"),
  so the package isn't installed.
- Play mode without a domain reload doubled Netcode's static message-type list on the second Play ("Allowed types is not
  equal to the number of message type indices"): `NetPlayModeReset` (Editor only) clears it at SubsystemRegistration.
- Netcode for Entities' automatic bootstrap is replaced (`NoEntitiesBootstrap`): only an empty default world, no client /
  server worlds, nothing in the player loop, so single player runs as without the package.
