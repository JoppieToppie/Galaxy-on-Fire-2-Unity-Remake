# Multiplayer: co-op campaign

The story for 2-4 players (remake-only, 2026-11-01). Code: `Game/CoopCampaign.cs` (the rules, plain C#, tested by
`CoopCampaignTests`), `Net/Multiplayer/NetCoop.cs` + `NetStateCoop.cs` (the session), `Space/World/CampaignLevelCoop.cs`
(the story levels), `CoopCameraMirror`, `CoopLevelState`; hooks `GameHooks.Coop*` / `SpaceHooks.Coop*`.

## Co-op campaign (remake-only)

- **The Co-op campaign screen** (2026-11-01, the user's request: the World switch's Co-op segment was easy to miss): the
  main menu's own **Co-op campaign** button (after Start new game) opens the multiplayer panel as the co-op screen
  (`MainMenu.coopPanelMode`, `SetPanelMode` / `ApplyPanelMode`): its title and intro, the browser's "Open campaigns" (co-op
  listings only, `NetLobby.Entry.coop`), "Host a campaign" with the saved campaign under it (`mpCoopSaved`: campaign, step,
  crew from coop.json), the co-op settings rows instead of World / Combat, Max players shown capped at 4, "Host campaign".
  The Multiplayer button opens the plain screen (World: Fresh / Persistent, every listing). The menu reopens the screen
  the last session came from (`NetCoop.LastSessionWasCoop`); Back returns to the button that opened it. Checked with
  `UiShots` at all ten sizes (main menu and both screens).
- **Hosting**: from the Co-op campaign screen (`NetGame.HostWantsCoop` while it is open); Host campaign then asks Continue (the saved campaign's step and crew, `NetCoop.ReadSummary`, coop.json) or
  New campaign (Galaxy on Fire 2, or Add-ons: Valkyrie / Supernova; `NetGame.CoopNewCampaign`). Max players is capped at
  4 (`NetGame.MaxPlayers`' getter, so Relay's allocation and the approval agree). Listed with a CO-OP tag (lobby key `coop`).
  Not on a dedicated server.
- **Difficulty and credits** (the host's settings, in the published State so every game uses them): enemy strength per
  player past the first (the Host card's "Enemies per player" Off / +25 / +50 / +100 %, PlayerPrefs `mp_coop_strength`, 50
  by default; `CoopCampaign.Strength`): story level enemies' hull x (1 + s x (players - 1)) (`HullFactor`), every hostile
  NPC's guns in a co-op session x (1 + s / 2 x (players - 1)) (`DamageFactor`, `NpcTables`' gun damage; not friends or
  wingmen); and the story credits (the Host card's "Story credits" Shared / Each in full, `mp_coop_split`, shared by
  default; `CoopCampaign.Share`): a step's reward (docked or in flight) and the game-won bonus of 45 are divided by the
  players in the session, rounded up, each game paying its own share; bar missions split as squads always do. The
  host changes them while playing with `/coop strength 0|25|50|100` / `/coop rewards shared|full` (no argument shows them;
  announced to everyone, kept for the next session). The player count is the session's at the moment: hull when a level
  spawns its ships, guns when a ship gets them, a reward when it is paid. A catch-up pays no step rewards.
- **Persistence**: a co-op session is a persistent hosted world in its own folder (`NetGame.CoopFolder`,
  persistentDataPath/CoopCampaign; `NetGame.CoopSession` makes `PersistentHost` true): every crew member's profile, the
  host's holding the story (`SaveGame.ApplyProfile` keeps the story's world: `CoopCampaign.AfterProfile`, not
  `UseCompletedWorld`). A new campaign moves the old folder to `CoopCampaign.old`. `NetCoop.WriteSummary` (every 30 s and
  when the host leaves) writes coop.json for the menu. Every session still plays on Normal and the Android economy.
- **Lobby** (a new campaign): the session opens with `NetState.CoopStarted` false (`NetCoop.LobbyWanted`), so
  `NetGame.EnterWorld` waits (`NetGame.InLobby`) and every game stays in the main menu, whose dialog lists the crew
  (`NetCoop.CrewNames`, the host first, the join code for the host), kept current every 0.5 s (`MainMenu.CoopLobby`). The
  host's Start (`NetCoop.StartCampaign`) sets the flag and everyone enters the world at once (`OnStarted`); Leave ends the
  session for that player (the host's: for all). A player joining after the Start enters at once; Continue has no lobby.
- **Starting**: the host's game is `CoopCampaign.NewCampaign`: the main game at step 0, the prologue in flight in the
  Dareius belt (`NetCoop.StartsInFlight`: EnterWorld loads Space, not Station), the add-ons docked; a saved campaign's
  host profile replaces it. A joining guest (`NetState.OnNetworkSpawn` -> `NetCoop.OnJoined` before its profile): a new
  game caught up from step 0 to the leader's (`PrepareGuest`: every step's side effects), the leader's own ship and
  loadout (the kit; its parked ship while it flies a loaner), where the leader last docked (in the belt at step 0); a
  returning guest's profile then replaces it.
- **The prologue and the rescue together** (steps 0 / 1, `IntroCutscenes`): the leader's game runs them as in single
  player, its three pirates and Gunant's salvager the crew's ships. A guest's level there is a follower that runs the same
  cutscenes and camera around its own Phantom (unkillable like the leader's), spawning nothing, a little beside the
  leader's (`IntroCutscenes.Slot`: left, right, below), paced by the leader's radio lines (its radio is the leader's), so
  everyone watches the same story from their own ship and fights the leader's pirates together; the follower can't skip
  (the leader's skip moves everyone). At the prologue's end the follower waits under the black until the leader's step 1
  reaches it, then loads the rescue (`CampaignLevel.FollowIntroOn`, `SpaceLevel.Reload`); the rescue's end docks everyone
  at Var Hastra (Gunant's conversation and Betty for each), the leader's docking taking the crew along.
- **The leader owns the story** (`CoopCampaign`): the host's step, mission and the step's shared counters (mission value,
  `StoryCounter`, step 59's `StoryTargets`, the Void invasion's station, step 13's crew missions, a fail serial) are
  published as JSON in `NetState.coopStory` whenever they change (`LeaderTick`). Every game still advances its own story
  (`Story.Advance`), so loaner ships, blueprints, revealed systems, cargo changes and rewards reach each player.
  - A guest behind the leader catches up silently (`CatchUp`: Advance without conversations), except the one step whose
    success conversation it is about to show (120 s at most); one ahead (it closed that conversation first) waits; ahead
    for over 120 s it is set to the leader's step. After a catch-up the docked station swaps the turntable ship and the
    menu's story locks (`CatchUpSerial`, `StationMenu.CheckStory`).
  - A guest's changes to the shared counters go to the leader as a `Progress` (`GuestTick`'s `Diff`) and come back merged
    (`Merge`: OR for the bit steps 116 / 125 / 148, else added; convoy targets marked done).
  - **Any crew member completes a step**: its game's check (`StationMenu.CheckStory` docked, `StorySpace.CheckSuccess` in
    flight) calls `CoopCampaign.ReportWon`; the host checks the step and tells everyone (`CoopStepWonRpc`), and each game
    opens that step's success conversation where it is (`RemoteWon`) and makes the step's moves (a launch into the story
    orbit, a reload, docked at Alioth...). So a guest docking at the target wins it for the crew.
  - A guest's own advance that wasn't a won step (the Khador misjump at 78, a level script, step 148's broker setting 151)
    goes to the leader, who follows it (`FollowGuestAdvance`) unless it won that step itself.
  - Step 13's three freelance missions count the crew's (`CrewMissions`; a squad mission once, by its `netId`).
  - Step 43, the ending, for everyone: each crew member's own success conversation of 43 (docked: `StationMenu`; in
    flight: `StorySpace` -> `SpaceLevel.PlayEnding`, which docks them at Thynome afterwards) loads the main menu scene with
    `Session.EndingPending` without leaving the session (`BackToMenu` would end it): the credits (`EndingCredits`) play
    over the menu's backdrop while the session runs on (`NetGame.OnMainMenu` leaves an active session alone; the menu
    shows only the credits), then the station and Keith's epilogue (44). A guest still in the credits when the leader's
    step reaches 44 catches up after 120 s and doesn't advance again; a session lost mid-credits clears the pending ending
    (`NetGame.SessionEnded`), so the menu opens normally.
- **The crew is one squad** (`NetCoop.KeepCrewSquad`: every player joins the host's, `NetState.RestoreSquad`; Leave is
  refused, "coopNoLeave"): no friendly fire, green markers, NPCs hostile to one are hostile to all, shared bar missions
  (taken with the whole crew docked at the agent's station, squad rules), distress calls.
- **Story orbits**: the leader runs the story level as in single player (`CampaignLevel.Setup`); its ships are
  `NpcShip.StoryShip` (shown to the crew like a freelance mission's ships, `NetProxy.StoryRole` in the role flags; never
  handed over: when the leader leaves the orbit the server despawns them, `NetState.HandOver`; never dropped with the orbit
  authority), hostile ones' hull x `CoopCampaign.HullFactor` (the Strength setting above). Another player's kill
  counts as the player's (`Target.killedByRemote` in `CampaignLevel.OnShipDied`).
  - A guest's level is a **follower** (`SpaceLevel`: `CoopCampaign.Guest` -> `CampaignLevel.SetupFollower`): nothing
    built or scripted; the leader sends 4 times a second its state (`CoopLevelState`: cutscene, invulnerable, hold, mission
    clock and time limit, route waypoint, fade; to that orbit's players, unreliable), its route (when it changes and every
    2 s), each radio line as it shows (`Radio.Shown` -> the follower's `Radio.ShowRemote`; a follower's radio tests no
    triggers, `Radio.Remote`), and during a cutscene its camera 10 times a second (`CoopCameraMirror` eases the follower's
    camera onto it, with the HUD and controls off through `SpaceLevel.Cutscene`); after the cutscene a follower over 2.5 km
    from the leader is put beside it. A guest arriving before the leader gets "coopWaitLeader" every 20 s.
  - **Moves**: the leader's level-driven moves (`SpaceLevel.TravelTo`, `RideWormhole`, `Dock(false)` while it runs a story
    level, `CoopLeaderMoved`) take the followers in that orbit along (`CoopTravelRpc` with the leader's step: the follower
    catches up first, `CoopCampaign.CatchUpTo`, so the next orbit is built as the story's).
  - **Hold**: while any crew member in the orbit has a story conversation open (`StorySpace.Open` -> `SpaceHooks.CoopDialog`,
    the server's `coopDialogs`, 180 s at most, cleared when a scene change cuts it off) the leader's level holds
    (`CampaignLevel.CoopHeld`: no clock, script or radio, its ships `frozen`, the crew invulnerable), as single player
    pauses the game. Not during a cutscene.
  - **Deaths and failure**: a guest destroyed there comes back 3 km behind the leader after 5 s (`NetCoop.RespawnDue` /
    `Respawn` beside `EventRespawn`, "Respawning in space...") while the leader lives there. The leader's death, or the
    level's fail objective, fails the crew's mission: the leader's step goes back to its checkpoint
    (`CoopCampaign.LeaderFailed` -> `Story.RepairCheckpoint`, 25 -> 24, 29 -> 28, 41 -> 39, 79 -> 78; the fail serial
    sets every guest there too), the followers there get "Mission failed!" + "coopFailBack" and dock at the station they
    last docked at (`SpaceLevel.ReturnToLastDocked`), anyone else a chat notice; the leader's game over docks it likewise.
- **Not shared**: the level's own music (a follower hears the orbit's), the mining minigame, object docking / hacking and
  the scripted flights that move the player's ship (the leader does those; the crew fights alongside or watches). Not
  tested with two games yet (2026-11-01: the rules by `CoopCampaignTests`; the session, the follower view and the
  cutscene camera untested in a build).
