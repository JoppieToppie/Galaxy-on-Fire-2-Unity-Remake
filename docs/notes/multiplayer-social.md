# Multiplayer: squads, arena, trades

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Multiplayer (remake-only)

- **Arena matches** (`NetArena` server, `NetArenaClient` player, `ArenaView` HUD panel; chat commands, answered
  privately): `/duel <name> [voids]` (both docked; `/accept` / `/decline` within 60 s; first to 3 kills or 5 min) and
  `/ffa [voids]` (a queue per option, docked; starts 30 s after the 2nd pilot or at once with 8; first to 15 kills or
  10 min), `/leave`, `/arena`,
  `/top` (the profiles' leaderboard: wins, kills / deaths, `NetProfiles.AddArenaStats`). A match is a private copy of
  the Void's home orbit (`NetArena.Template` = `Session.VoidOrbit`: its sky, fog, music, Void crystal asteroids): its
  own orbit id `NetArena.OrbitBase` (100000) + the match, which
  `SpaceLevel.NetOrbitId` gives NetPlayer / NetOrbit, so the same-orbit checks keep it to its players (asteroids seeded
  by that id). SpaceLevel's arena mode: no station or gate (no docking, no jumps: `JumpsBlocked`), no traffic (passive)
  unless the match has the Void fighters (`voids`: the orbit's own traffic, run by the first player in the match's
  orbit like any orbit's NPCs, the dead ones back every 45 s by `Traffic.UpdateAlienAttackers`; a Void kill scores
  nothing, dying to one is a respawn), no wingmen, no mission / siege / spy, the asteroids at the centre, the player on a ring of 8 spawn points 2 km out
  facing the centre (a duel's two opposite; a respawn the point farthest from the others), no mining (`Mining.UpdateLock`).
  Flow: `ArenaStartRpc` (the profile uploaded, the equipment noted, the take-off, the arena loads) -> `ArenaReadyRpc`
  (all in, or 20 s: the missing ones sent home) -> 5 s countdown (controls and guns locked by `NetArenaClient.Tick`) ->
  the fight (`ArenaStateRpc`: phase, time left, kills, a kill-feed line; kills from `DestroyedByRpc`) -> the end
  (`ArenaEndRpc`: winner or draw and everyone's kills / deaths, shown 6 s) -> docked where the match began. Destroyed:
  no game over, the arena reloads 3.5 s later (repaired, the equipment and ammo as at the start). Nothing is at stake:
  no uploads during a match (`NetProfileClient.Upload`), the loadout and the cargo (Void loot) restored at the end. Leaving / disconnecting: out;
  a duel goes to the one who stays, a free-for-all ends below 2 players. Server console `arenas`; `list` shows
  "in arena match N". Not tested in a build yet.
- **Squads** (`NetSquad`, the host's `NetPlayer.SquadId`, NetState's RPCs): the station's pilot list (players docked
  there, the local player first as "(you)") has Invite; the invited player gets an Accept / Decline popup (45 s); accepting joins the inviter's squad (a new
  one if needed, leaving the old one); squads form only in a hangar (the popup shows only while docked, and the host
  refuses an acceptance unless both are docked at the same station); joining abandons the joiner's own bar mission (the
  popup warns, `NetMissions.AbandonWarning`) and the squad's active one (the inviter's, else a member's,
  `NetPlayer.MissionHeld`) is sent to them; Leave in the squad window; a squad of one dissolves (also after a disconnect).
  Squadmates are green markers; the players' weapons don't affect a squadmate (`Target.playerProof`: no damage, and their
  bullets pass through, `Gun.Ignores`; a squadmate's replayed shots pass through the squad too). UI: `SquadView`
  (`Resources/GoF2Net/Squad.uss`) in the flight HUD and the station menu: the collapsible squad window (right side, only in
  a squad: each member, where they are, a shield bar and a hull bar with the armor over it like the HUD's), the station's
  collapsible pilot list (at most `SquadView.MaxPilotRows` 6 rows, then "+N more": a busy hangar filled the screen), the
  invitation popup. On a small screen (`UiScale.Large`) both panels start folded and the panel is narrower
  (`.squad--large.squad` 26 % / 360 px): open, they took most of the right side, over the lounge's own lists. The
  invitation and trade popups (only while docked) are the interface rework's panel shape (docs/UI-DESIGN.md "Shapes":
  the panel fill with its cyan left edge, a `.panel-title` with the `.panel-accent` under it, the message as running
  text at `--text-body`, and the two answers as the shared button kinds, Accept primary, each with the controller's A /
  B inside it as a dialog's answers have; `SquadView.Popup` / `PopupButton`) and are the only squad buttons that take
  the focus: while one shows it is
  the station menu's navigation (`SquadView.PopupOpen` / `PopupButtons` in `StationMenu.OnNavigate`, like a dialog's Yes /
  No), its Accept takes the focus as it appears with keys or a controller (tried again over `PopupFocusTries` frames: the
  popup is laid out a frame after it shows and a focus set before that doesn't take) and the menu's selection comes back
  when it is answered, and Esc / B is its Decline (`SquadView.DeclinePopup` in `StationMenu.Back`); before, an invitation
  could only be answered with a mouse or a finger. Verified in a session (the Editor hosting, a Windows build joining on
  127.0.0.1, the client's invitation): the popup focused on Accept, left / right between Accept and Decline, Enter / A
  forming the squad, Esc declining; and in the window, the tab row walked, the Squad tab's Accept reached from it and
  pressed into a squad.
- **Trades between players** (remake, `NetTrade` on the server, `NetTradeClient` on each game, RPCs in `NetState`, rate
  kind `PlayerTrade`): only while both are docked at the same station. A request (the station pilot list's Trade, the
  Multiplayer window's Trade tab, `/trade <pilot>`) waits 60 s for Accept / Decline (a popup like the squad invitation's,
  `/trade accept | decline`); the trade then opens in the Multiplayer window's Trade tab for both: each side's offer is
  credits and cargo items (no ships, no mounted equipment, no story items `Session.Unsaleable`, no Courier containers or
  cabins 116 / 117; at most 16 kinds), sent whole 0.3 s after the last change and kept within the hold and the wallet;
  every change makes a new version and takes back both accepts, Accept carries the version it saw and waits 2 s after the
  other side's last change. Both accepted: the server asks each game to pay its own offer (`TradePayRpc`: checked, out of
  the wallet and the hold), and only once both paid sends each the other's (`TradeDeliverRpc`); a side that can't pay,
  doesn't answer in 15 s or leaves fails it and whoever paid gets their offer back (a late payment too). Either side
  cancelling, taking off or leaving the station ends it. Profiles: the recorded worth moves with what each gave and got
  (`NetProfiles.AdjustWorth`), so the upload check stays right; the credits move in the saved games (`MoveCredits`): each
  side's out as its payment arrives (`NetTrade.Debit`; at the end, a periodic upload sent while the other side still paid
  already had them out and they were taken twice), back if it fails (`Refund`, also for a side that left), the other
  side's in when it completes. Goods past the hold's capacity are allowed (a warning; the
  launch refuses as for the shop).
- **Bar missions** (the freelance missions, `NetMissions`): a squad has **one mission**, the same for every member
  (one squad id, `FreelanceMission.netId`), with shared goals. It can only be accepted with the whole squad docked at the
  agent's station (`NetMissions.AcceptRefusal`, in `Freelance.AcceptRefusal`); every member gets it (their own mission
  replaced); the return trip of Recovery / Salvage updates it for all. Progress is shared: anyone's kills count
  (`Target.killedByRemote`, the Challenge score too) and the mission's `status` is synced (`NetMissions.AddStatus`: ore a
  member unloads at the plant, the Hijacker's container captured by any member). Containers and passengers stay with the
  member who carries them, so only they deliver Courier / Passenger / a Recovery return trip (`NetMissions.CanDeliver`);
  Purchase / Stolen goods anyone with the goods. The mission orbit is built once: by the first squad member there
  (`NetMissions.ShouldRun`, `NetPlayer.MissionRun`), whose game runs it, and shows its ships and junk to everyone there
  (NetOrbit proxies every player's own NPCs; junk as `NetProxy` junk kind in `Target.RadarObjects`); mission ships are
  never taken over (`NpcShip.MissionShip`); the orbit stops when its runner loses the mission. A squadmate arriving
  there gets `FreelanceOrbit`'s follower mode (`SetupFollower`, `Running` false): nothing built, but the briefing, the
  runner's waypoint route, Junk removal timer and Challenge score (`NetPlayer.MissionRoute` / `MissionClock` /
  `MissionScore`), the return trip's message and the result as the mission's dialog (`NetMissions.ResultView`); when
  the runner leaves (docks, jumps, respawns, leaves the squad), the member there with the lowest client id takes over
  (`Promote`): the runner's mission ships go on as its own with their role and hull (`NetProxy.RoleFlags`, `specHull`),
  so do the junk, the mining plant and a loose container; with none left they count as done (inherited), with no orbit
  ever seen it is built anew; the score and the clock go on; the runner's own copies go (`NetProxy.TakenOverRpc`). Two
  runners at once: the higher client id stands down (`Demote`). A follower's Ore Mining unloads at a hidden local
  stand-in of the runner's plant (`NpcShip.LocalOnly`), the ore counted for the squad. Loot from mission ships and junk
  (`Crate.missionLoot`, the Hijacker's container too) is
  out of reach for players outside the mission's team (`NetCrate`: claimed by another for them). They spawn out of the
  other players' view (`NetOrbit.OutOfSight`: a point in a player's 65 deg cone within 60 km, or within 1.5 km, mirrored
  to behind them). Only the mission's team gets anything from the mission: players outside it never get the mission
  (briefing, waypoint, timer, reward, notices), see its ships as ordinary ships (no mission name on the lock plate,
  `NetProxy.missionShip`) and only get the normal kill credit. A success anywhere (in space or docked) pays every squad
  member an equal share (`Freelance.Succeed` -> `SplitReward`: standing +5 and missions completed for each; solo = all
  of it) and ends the mission for all; so does a failure, and a member's Discard (the Missions window warns: it ends it
  for the whole squad, `NetMissions.Abandon`). Joining a squad abandons the joiner's own mission and sends them the
  squad's (NetState.AcceptInviteRpc: the inviter's, else a member's, `NetPlayer.MissionHeld`); leaving a squad removes its
  mission for that player (`NetState.LeftSquadRpc` -> `NetMissions.OnLeftSquad`), and a member leaving with the
  containers / passengers aboard ends it for the squad. A member who disconnects doesn't end it: what they carried
  (`NetPlayer.MissionCargo`, a captured Recovery container too) goes to a squadmate holding the mission
  (`NetState.HandOverMission` from `NetPlayer.OnNetworkDespawn`: Netcode despawns the player object before the disconnect
  callback, `NetMissions.TakeCargo`). The host checks a new squad mission too (the squad all docked at the sender's
  station, no other member's in the last 3 s: else `MissionRefusedRpc`), ends each mission once (`endedMissions`: two
  members delivering at once), and a success pays a member who never got the mission their share as well; a lost
  Challenge's wager is shared like the reward. Mission dialogs act only on the mission they were about (a squadmate may
  have ended it meanwhile). A runner dying keeps running it (a result without its dialog). Informer: the spy is spawned
  by the team member when another player built the orbit's traffic (`SpaceLevel.SpawnInformerSpy`, not with a squadmate
  here, `NetMissions.TeamHere`), and its death (or a spoiling kill) reaches the squad (status 1 / 1000). An old
  invitation from a squadmate is dropped (accepting it would drop the squad's mission). The squadmates of the one who took
  a mission see which one was activated: the mission card (`EventScreen.ShowMissionCard`, `NetMissions.ShowCard`; a box in the
  upper middle: "New squad mission", its name, "Accepted by X", the client's face and name, the target, the reward; 9 s, a
  tap closes it, the message sound), besides the chat notice.
- **Distress calls** (`NetDistress`): a squad member in space calls for help (the squad window's own row: Distress call /
  End the call, or `/sos`; `NetPlayer.Distress`, owner-written; ends on docking, after 10 min or leaving the squad);
  the squadmates get a notice with where (`NetState.DistressRpc`), a red "⚠" name and a **Help** button in the squad
  window (or `/assist <name>`, both handled in the player's own game, `NetChat.Send`). Help programs the caller's
  station (`Session.ProgrammedStation`) and goes the fastest way: another system with a Khador Drive and the cells
  (`GalaxyMap.EnergyCells`): an instant jump (`Session.InstantJump`, SystemJump charges by itself); else the autopilot
  (`Navigation.ContinueToProgrammedStation`: the planet jump, or the gate route); docked: the launch first. Arriving in
  that orbit by travel, the helper comes out 1.5 km from the caller facing them (`NetDistress.ArrivalNear`,
  `SpaceLevel.SpawnPlayer`). The star map marks the squad when it opens (`StarMap.SquadMark`): a green "●N" by a
  system, "● names" under a planet, red with "⚠" for a call. In flight the Distress call button under the Multiplayer
  button (or the unbound "Distress call" binding; not in the quick menu, where a menu key picked it by accident), and
  the quick (actions) menu (E / D-pad left / the touch quick menu button; `Navigation.ActionEntries`, kind `Assist`):
  "Help <name>" for each squadmate calling, the answer as a HUD message; and a pulsing banner at the top
  (`TerritoryView`, under a siege's) with who calls for help and where. Not tested in a build yet.
- **Following a squadmate** (`NetFollow`, the user's request 2026-11-02): Follow / Stop following on each squadmate's
  row of the squad window (flight HUD and station, `SquadView`) and the Multiplayer window's Squad tab, the quick menu's
  "Follow <name>" / "Stop following <name>" (kind `Follow`, `SpaceHooks.FollowSquadmate` / `FollowedSquadmate`), or
  `/follow <name>` (`/follow` alone stops). While this pilot is in space and free (`NetFollow.Ready`: the launch camera
  over, no jump charging or under way, no arrival warp, cutscene, conversation, docking or object docking, not between
  the systems or in an arena), each time the followed pilot is placed (in space or docked) in another orbit, this game
  sets out after them the distress Help's way (`NetDistress.TravelTo`: the Khador Drive with the cells, else the
  autopilot to their planet or the gate route) and arrives beside them (`ArrivalNear`); docked it waits, the launch then
  goes after them. One trip per place they go (`goingTo`): the autopilot switched off on the way stays off, and a planet
  jump of one's own away from them isn't undone, until they move on. Ends: Stop following, a destination of one's own
  (`Session.ProgrammedStation` set to another station, e.g. the star map's pick), the followed pilot leaving the squad
  or the game, this pilot leaving the squad, the session (`NetGame` -> `NetFollow.Clear`). Squadmates only (a stranger
  can't be trailed across the galaxy in PvP), never into an arena match, not in the co-op campaign. The rule:
  `NetFollow.Decide` (`NetFollowTests`). Not tested with two games yet.
