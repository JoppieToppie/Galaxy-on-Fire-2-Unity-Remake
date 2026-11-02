# Event graphs

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Multiplayer (remake-only)

- **Where the event graphs live** (they began as multiplayer events and now run single player's quests and bar missions too):
  `Scripts/Runtime/Net/Events`, namespace `GoF2Remake.Events` (was `Multiplayer`: `NetEvents` is `EventRunner`, the `NetEvent*`
  helpers `Event*`, `NetScreen` `EventScreen`); graph files `.gof2event` (the old `.gof2netevent` is still read by the game:
  `EventRunner.LegacyExtension`, the Events folders and mods' `events/`; imported into the project, `EventGraphUpgrade`
  renames one with `AssetDatabase.MoveAsset` (its GUID kept) and rewrites its enum option types `GoF2Remake.Multiplayer.Event*`
  -> `GoF2Remake.Events.Event*`, which Graph Toolkit stores by name; the game's reader ignores them); the built-ins and the
  event audio in `Resources/GoF2Events`. The chat command layer they run (`NetCommands`, `NetAdmin`, `NetTeleport`) stays in
  Multiplayer. The enum type names carry their assembly too: graphs saved before the layered assemblies named
  Assembly-CSharp (the enums are in GoF2.Net), so Graph Toolkit couldn't resolve them and the import failed ("Type
  provided must be an Enum", hidden by cached imports until an iOS platform switch reimported them, 2026-10-17; the
  built-in graphs were then missing from Resources); `EventGraphUpgrade.FixAssemblies` writes the right assembly into
  every imported graph, and `EventGraphImporter` turns a Graph Toolkit failure into a warning (the game's own reader
  doesn't use those names). Verified: every graph re-imported without a reader / Graph Toolkit mismatch, a template
  opened and compiled by
  Graph Toolkit, an old-format graph converted on import, a mod's old .gof2netevent and the built-ins loaded at runtime.
- **Events** (`EventRunner`, `/event <name [setting=value ...] | stop | list>`, admins and the console only): node graphs run
  on the server: `<name>.gof2event` (see Event graphs below) in an Events folder (`persistentDataPath/Events`,
  or next to the game / dedicated server's executable), else a mod's (`events/`, the mods that are on), else a built-in one
  (`Resources/GoF2Events`: waves, survival, the bar mission pirate_hideout); one global event at a time (/event:
  everyone) and any number of bar missions beside it
  (each run its own `EventRunner.EventRun`: threads, handlers, questions, batches, points, scoreboard, what it turned on);
  `/event list` shows each one's settings. There are no script files: the graph compiles to an internal line form, which is
  what runs. Its lines: any server command without the "/" (run as the server, "{expression}" parts filled in), `wait <s>`,
  `wait until <condition> [timeout <s>]`, `if` / `else` / `end`, `while` / `end`, `repeat <n> [as <var>]` / `end`,
  `set <var> = <expression>`, `param <var> = <default>` (a setting), `parallel` / `end` and `every <s>` / `end` (threads, 16 at
  most; the event ends with its main flow), `on <trigger> [station]` / `end` (a handler: died, docked, launched, joined, left,
  entered, respawned, kill (an event ship destroyed, its killer), pvpkill (a player destroyed another: the killer, `%victim%`
  the other; from `NetState.DestroyedByRpc`), cleared (every spawned enemy gone), hacked (the player hacked a wreck, its name
  as `%victim%`: `SpaceLevel`'s `Docking.HackWon` -> `SpaceHooks.WreckHacked` -> `EventRunner.OnWreckHacked`, in a session
  through `NetState.WreckHackedRpc`, checked against the sender's orbit); each firing runs the block as a thread with
  that player as `@trigger` and `%trigger%`; polled every 0.25 s, `DetectTriggers`), `ask <players> <s> | [speaker :] question
  | answers` with `answer <k>` / `end` blocks (each answer runs its block as a thread for that player; the flow waits for all
  answers or the time), `vote ...` with `choice <k>` / `end` blocks (the most picked answer's block, a tie at random, `choice 0`
  when nobody voted), `startevent <name> [setting=value ...]` (ends this event, starts that one with the same starter; 5 a
  second at most), `points <players> <n>`,
  `scoreboard on [title] / off` (every player's screen, `NetAdmin.Order.Scoreboard` -> `EventScreen`, the top 10 by the score
  mode, sent when it changes and every 5 s), `stop`, `score <kills | time | points>`, `winner [kills | time | points]` ("The
  winner is X with N kills" on screen and in the chat). Each line may end in `#@<node id>` (the live view). Expressions:
  numbers, variables, arithmetic, comparisons, and / or / not, random(a, b), min, max, floor, count(<players>) (a selector's
  players), and the state: `enemies` / `ships` (the event's living spawned ships: each spawn order gets a batch tag,
  `SpawnSpec.eventTag`, written on its NetProxy with the enemy bit, `NetProxy.EventTag`; a just-sent batch counts until its
  ships show, 6 s at most), `players`, `inspace`, `docked`, `dead`, `time`, `toppoints`, `missionstation` (where a bar
  mission was taken, -1 in an /event). Results: an event ship's killer
  (`NetProxy.Killer`: its own game's player, or the shooter of a remote hit) counts once per ship (by owner and traffic index:
  proxies are remade and Netcode reuses ids); time alive in space from the first spawn until a player's first death; points.
  Once it has spawned ships, an event ends by itself when no player has been alive in space in its ships' orbits for 3 s
  ("Event over" with the winner; `set autostop = 0` turns it off); its music, free for all, respawn points and travel
  restrictions end with it. Ticked by `NetState.Update` on the server; reset per session.
  **Questions** (ask / vote; `NetAdmin.Order.Ask` -> `EventScreen`): the Space Lounge chat's look (header with the speaker's name
  and the seconds left, the portrait beside the text, the answers as stacked station buttons; inline styles, the speaker
  resolved on the server as /dialog's, `NetAdmin.ResolveSpeaker`); answered by a click / tap, 1-4, a controller's A B X Y, Esc /
  View skips (`NetState.AnswerRpc` -> `EventRunner.OnAnswer`); while it shows `EventScreen.QuestionOpen` rests the flight controls
  (`Navigation.InputHalted`), the keys (`NetChat.Keys`) and the menus (`GameControls.BlocksMenus`).
  **Free for all** (`/pvp on | off`, admins; the Free For All node): `NetState.FreeForAll` (a NetworkVariable) makes every
  other player an enemy (`NetAggression.IsHostile`; squadmates excepted). **Respawn points** (`/respawn [players] <station
  [x y z] [spread <units>] [delay <s>] | off>`; Set / Clear Respawn Point): a destroyed ship comes back in space, repaired,
  after the delay (`EventRespawn`, `PlayerHealth` after the explosion, `NetTeleport.Respawn`; default 3 km in front of the
  station, `NetTeleport.RespawnSpot`: the launch spot is inside the bigger stations) instead of "Tap to respawn at the
  station". **Travel restrictions** (`/restrict [players] <jumps | docking | all | khador | off>`; Restrict Travel): `EventRules`,
  in `Navigation.JumpsBlocked` (planet jumps, Khador Drive), `SystemJump.GateBlocked` and `SpaceLevel.DockingBlocked` (525);
  remake (2026-11-02, a modder's suggestion: the Valkyrie's last mission) "khador" (`EventRules.Khador`, the node's No Khador
  Drive, the step's "The Khador Drive only") refuses the Khador Drive and the slipstream alone (`Navigation.KhadorBlocked`,
  `SpaceHooks.NoKhador`), the gates and planet jumps open; "docking" also refuses a station's pads outside.
  **Hand-ins** (same day): `/take [players] <item> [amount | all] [mounted]` (`NetAdmin.Order.Take` 35, `Cheats.TakeItem`: out
  of the hold, all by default; "mounted" the mounted ones too, a secondary by its ammo, never a built-in item; the story's
  unsaleable flag goes with the last one, as at step 78; a changed mount in space rebuilds the loadout, `SwapPlayerShip`);
  the step Take an item, Bring cargo's "Hand it in" (`take @a <item> <amount>` after its wait), the node Take Item.
  **Stronger spawns and NPC types** (same day): `/spawn ... hull <x> guns <x>` (0.1..50, `DebugSpawner.Scale`: the hull
  points, a fighter's gun damage through its race's gun, `SetGun`; in the order's text after '^') and `/spawn [players] type
  <key> [named <name>] [at x y z]` (`NetAdmin.Order.NpcType` 36, `DebugSpawner.SpawnNpcType`: an npcships.json type, the
  game's five keys too, so `pirate_boss` is the pirate boss by rank; its escorts carry the event batch); the steps' hull /
  guns and Spawn an NPC ship type (`spawntype`, `QuestFieldKind.NpcShip`, picker `RefKind.NpcShip`), the nodes' Hull / Guns
  ports and Spawn NPC Type. **"on pad"** (same day): docked at a station's pad outside (`SpaceHooks.PadDocked`, in a
  session `NetState.PadDockedRpc`, `EventTrigger.PlayerDocksAtPad`), the station as its orbit.
  **Make Hostile** (`/provoke [players] [race] [within <m>]`): the NPC ships around each player (NetProxy positions on the
  server) turn on that player and their squad: the ship's own game adds them to `NpcShip.aggressors` (neutral spawns too;
  `NetOrbit.HostileToLocalBySquad` counts the local player's own id). **Radio** (`/radio [players] <speaker> : <text>`):
  a line in the flight HUD's radio box (`Traffic.Say`, queued after the waiting ones), speakers as /dialog's; docked: a chat
  line. **Waypoints** (`/waypoint [players] <station x y z | off>`; Set / Clear Waypoint): `EventWaypoint`, a one-point
  `Route` through `Navigation.SetEventWaypoint` (lockable "Waypoint", on a bar mission graph its title, the autopilot menu's
  entry), a slot of its own beside the campaign / freelance route (`SetRoute`), so both show and clearing one keeps the
  other (a Discord report: the Pirate Hideout's waypoint replaced a lounge mission's and its Clear Waypoint removed both;
  `NavigationWaypointTests`), applied whenever the player is in that orbit, cleared on reaching it and at the event's
  end; the On node's Player Arrives (`on near <station> <x y z> <metres>`) fires
  when a player alive in space comes that close (server-side, NetPlayer positions). Spawned objects get collision
  (`DebugSpawner.SpawnObject`: `station_pirates` the original's volumes 1002 unrotated, others a box around the model) and
  exist only in the games they were sent to: spawn scenery for `@a[orbit=...]` at fixed coordinates.
  **Wormholes** (remake, 2026-10-23, the user's request: "wormhole spawning nodes to Missions, with variable destination";
  `/wormhole [players] <station> [x y z] to <station | void> [for <seconds>] | close`, NetAdmin order Wormhole 33,
  `NetAdmin.TryParseWormhole` (a station by number, name, key, `void` or -1, so a variable's number works; without x y z
  `NetAdmin.WormholeDefaultAt` (50000, 15000, 60000): its 2 km pull clear of the launch path along game +z, which a
  first test at (0, 0, 60000) caught every launching ship from); the mission step `wormhole`
  (station, at, to (a Station field: a variable or value node wires in) / void, seconds; no station = close; `QuestFile`
  reads the line back), the graph's Open / Close Wormhole nodes (Orbit and Destination take Get Orbit)): `EventWormhole`
  (Net/Events, like `EventWaypoint`; one per player, a new order replaces it; game-time seconds, 0 = until closed; closed at
  the event's end, `EventRun.eventWormhole`; a resumed mission runs the line again) has the `SpaceLevel` of that orbit open
  it (`OpenEventWormhole`: `Wormhole.held`, open until `Close()`, never relocated, no mission lock; its own
  `PlayerCollision.eventWormhole` pull without the story's rules, `Navigation.SetEventWormhole`'s "Wormhole" marker); inside
  it `RideEventWormhole` travels to the destination (`VoidReturnStation` set for the Void) as a stream-out arrival with
  `Session.ArrivedByWormhole`: `PlaceArrivalWormhole` puts a closing wormhole 10000 units behind the player. Tests
  `NetCommandsTests.AWormholeOrder`, `QuestGraphTests.AWormholeStepTakesAVariableDestinationAndComesBack`. Verified in Play
  mode (single player, a throwaway .gof2mission at Kepler Prime with `"to": "{dest}"`): the wormhole opening with its marker,
  the ride to Var Hastra arriving 4.2 km out facing the station with the closing wormhole behind. Not tested in multiplayer.
  **Ship orders** (`EventShipOrders`, `NpcOrder`; NetAdmin order Npc 29, `/npc [players] <ship> <order>`; the server checks
  the text with `EventShipOrders.TryParse`, each target's game applies it to its own traffic ships of that name (Target.
  displayName, "Name N" of a numbered spawn too; proxies just mirror)): goto / route (places "/"-separated, loop), follow /
  escort (leader player | ship, offset right up forward in the leader's frame), attack (player | ship, regardless of
  standing), flee (from the player, then jump out), dock (into the station, vanish), hold (parked), resume, jump, speed;
  options speed / radius / fight / then hold | resume | vanish | jump / seconds; places through `EventCutscene.ParsePlace` /
  `ResolvePlace` (read when the order arrives; follow / attack keep their Transform / Target). `NpcShip.SetOrder` /
  `UpdateOrder` (before the freighter and targeting branches, after the story's scriptedSpeed): steering through the AI's own
  `Steer` / `Avoid` (Dock skips the landmark avoidance), `drift` cleared; a group spreads (`Spread`: 900 to the side, 600 back
  per rank); Follow aims 3000 units ahead along the leader's heading and closes the gap by speed (boost beyond 6000);
  remake: slower while facing away from the goal (x0.35..1 by alignment, not under 0.6 u/ms) so the original's fixed turn
  rate (a circle of speed / 0.00073 units) stays tight; escorts run `UpdateTargeting` and fight first. An Attack order
  sets the order's speed each frame only while the ship isn't boosting (`if (!boosting) speed = fly;`, #179, for every
  graph): before, the boost never built up, so an attacking ship never fled at 40 % hull loss nor took its 5 % boost roll.
  `NpcShip.Revive` clears the order (review of #179): a revived ship is back on its own AI. Exits by order
  (`JumpOut`, `LeaveQuietly`) set `Target.killedByNpc`, so no event credits a kill; `Traffic.UpdateOrbit` /
  `UpdateAlienAttackers` / the jumpers never revive a ship with an `eventTag`. Expressions `alive(name)` and
  `distance(name, x, y, z)` (`EventRunner.ShipState`) read `EventHost.Ship.name` / `position` (game units; NetProxy's label
  and pose in a session). Graph nodes (category Ships): Fly To, Fly Route, Follow (Escort), Attack, Ship Action
  (`EventShipThen`, `EventShipAction`; base `EventShipOrderNode`: "EventShipNode" is Change Ship's saved name). Verified in
  Play mode (single player): a pair flown ahead of the player and held, following in formation, docking (gone, no kill, not
  relaunched), a drone fleeing and jumping out, the expressions.
  **Cutscenes** (`EventCutscene`, its own DontDestroyOnLoad singleton; NetAdmin orders Cutscene 25 / Camera 26 / Fade 27 /
  Letterbox 28; commands `/cutscene [players] <start [nobars] [freeze] [invulnerable] | end>`, `/camera [players] <place [to
  place] [over s] [look place] [follow] [fov deg] [shake 0-1] | chase>`, `/fade [players] <out | in> [seconds] [rrggbb] | clear`,
  `/letterbox [players] <on | off>`; the server checks the text with `EventCutscene.TryParseShot`): places `player [right up
  forward]` (game units in the ship's own frame, unscaled), `station [x y z]` / `x y z` (orbit game coordinates), `ship <name>
  [x y z]` (a `Target.displayName` from Spawn's "named", "Name 1" numbering accepted; "quoted" names with spaces). Cinematic
  mode: `SpaceLevel.Cutscene` is also `EventCutscene.Cinematic` (the HUD's `hud-cinematic`, touch PauseOnly, no free look /
  photo mode), `Health.invulnerable` with "invulnerable", the player's `inputLocked` and `Weapons.Blocked` (released only when
  it set them and the launch camera is over), "freeze" = `externalControl` at speed 0, the autopilot off once; it survives a
  scene change (re-applied to the new level), a shot doesn't (bound to the scene it was sent in). The shot runs in LateUpdate
  (`DefaultExecutionOrder(5000)`: after the chase camera, before `VrRig`) with the ChaseCamera off (`scriptCamera`): position
  lerped from / to with smoothstep over the seconds, LookRotation to the look place every frame (world up), FOV through
  `Aspect.VerticalFov`, shake = look-point jitter × `Settings.CameraShake` + `Haptics.Rumble`; `camera chase` / `cutscene
  end` snap the chase camera back. Fade and bars (11 % of the height each, 0.6 s, smoothstep) on a panel of their own at the
  shared panel settings' sorting − 1, under the HUD, so the radio and conversations show over a black screen; docked too.
  All of it is cleared when `EventHost.ScreensActive` goes false; a run that sent one (`NoteCutsceneSet` / `NoteFadeSet`)
  sends `cutscene end` / `fade clear` at its End. Graph nodes (category Cutscene): Start Cutscene (Letterbox, Freeze ship,
  Invulnerable), Camera Shot (From / To / Look At text, Seconds, Fov, Shake, Follow; the compiler quotes a ship name with
  spaces, `EventGraphScript.Place`, and reports a bad shot on the node), Chase Camera, End Cutscene, Fade (`EventFade` Out /
  In / Clear, Seconds, Colour: the "#" is dropped, it starts a comment in the line form), Letterbox. Verified in Play mode
  (single player, line-form runs): the bars, the hidden HUD, the fades, a moving followed shot around the player, a station
  shot, a shot on a spawned "Pirate Boss", freeze and invulnerable, everything restored at the end; the nodes through
  `EventGraphScript.Compile` on an in-memory graph.
  **Sound and music** (`/sound [players] <sound>`, `/music [players] <track | stop>`, admins; the graphs' Play Sound / Play
  Music / Stop Music): `EventAudio` (`Resources/GoF2Events/EventAudio`, **GoF2 > Build > Event Audio**, the original FMOD
  events: alarm 162, warning 35, success 36, explosions, jumpgate, Khador...; tracks battle, boss 151, void, specters, intro,
  outro, the race themes...); a track loops on `EventScreen`'s own source and the scene's music fades out meanwhile
  (`EventScreen.SceneMusic` in `Traffic` and `StationLevel`).
  **Event graphs** (`.gof2event`, Unity's Graph Toolkit module, built in since 6000.4; Editor side in
  `Scripts/Editor/Events`): the only form of an event. Assets > Create > GoF2 > Event Graph, or Event Graph From Template
  (King of the Hill, Boss Fight, Free For All, Pirate Base, Pirate Hideout (a bar mission), Quiz, Race, Vote For The Next
  Event, Waves, Survival in
  `Scripts/Editor/Events/Templates`: copied with every graph / node /
  variable id renewed, `EventGraphMenus.NewIds`: a plain copy keeps Graph Toolkit's ids). Nodes: Flow (Start, Wait, Wait Until,
  Wait Forever, If, While, Repeat, Run In Parallel, Every, Random Branch with weights, Ask, Vote, Set Variable, Stop, Comment),
  Triggers (On: no flow in, its Do runs on each firing; Orbit limits it; Player / Name / Victim outputs into Players / texts),
  Event (Score, Winner, Add Points, Scoreboard, Mission Complete, Mission Failed, Start Event (with a picker of the project's
  events), Free For All, Set / Clear
  Respawn Point, Restrict Travel, Make Hostile, Set / Clear Waypoint), Commands (one per event command, e.g. Standing for
  `/standing` (2026-10-28, `EventStandingNode`: Toward a race word or a mod race's id, Amount, Set; the quests' Standing
  step writes the same line, `QuestFile` reads it back), Radio, Play Sound /
  Music, Stop Music, Command for any other line), Ships (Fly To, Fly Route, Follow, Attack, Ship Action), Cutscene
  (Start / End Cutscene, Camera Shot, Chase Camera, Fade, Letterbox),
  Players (Get Orbit: a station by number, name, void or a wired number -> Get Players (who, optionally in that orbit) -> any
  command's Players, written as `@alive[orbit=Var Hastra]`; Count Players), values (Game State, Math, Random, Floor, Compare,
  Logic, Not, Expression), the blackboard's Number variables (set to their defaults at the start; Input ones are the event's
  settings). Local sub-graphs (Graph Toolkit's "Create Local Subgraph"; stored in the same file) are inlined where their node
  is: give them Flow input / output variables (the flow enters by an input, goes on after the node from the output it
  reaches) and Number inputs (what is wired into or typed on the sub-graph's node); asset sub-graphs can't run on a server
  ("Unpack To Local Subgraph"). Name pickers under Spawn / Give Item / Reward / Change Ship / Get Orbit / Teleport
  (`EventGraphPickers`, NodeViews with searchable lists of the server's names, `EventNames`); typed names the server wouldn't
  know are warnings on the node. The live view (`EventGraphLiveView`, Play mode): the running event's graph (by name) shows
  each flow's current step (Graph Toolkit's GraphVisualization: an animated accent, a wait's progress as the fill) and the
  variables' values on their nodes (`EventRunner.ActiveSteps` / `Variables`). The server reads the file itself:
  `EventGraphFile` (a small reader of Graph Toolkit's serialized model, Unity YAML: nodes with their port / option values,
  wires, variables (m_Modifiers 0 local / 1 input / 2 output), local sub-graphs; node ids as Graph Toolkit's Hash128) and
  `EventGraphScript` (the one compiler, graph -> the line form above; the Editor's checks and Run In Play Mode feed it the
  live graph through `EventGraphCompiler`). The importer depends on the GoF2Data JSON files `Database.Load` reads
  (`ctx.DependsOnArtifact`: in a fresh Library the
  graphs were checked against empty tables) and keeps the file's text as its TextAsset (so `Resources/GoF2Events`
  graphs are the built-ins) and reads every graph both ways, warning if the game's reader disagrees with Graph Toolkit (a
  Unity update changing the format), and about a name with spaces (/event takes one word). Node kinds, port and option names
  are the Editor classes' (`EventGraphNodes`), the option enums the game's. The right-click "Event" menu (and Assets > GoF2 >
  Event Graph): export the graph (a file or the game's Events folder), run it in a hosted Play-mode session
  (`EventRunner.StartText`, server only). Graph Toolkit's toolbar API isn't public, hence the right-click menu.
  Admins: the host's own player, or players the host or the
  dedicated server's console made admins (`NetPlayer.IsAdmin`, server-written, for the session only: names aren't
  verified). Players are named whole, any case (the longest name the arguments start with), by a client id as the first
  word, or by a Minecraft-style selector (`NetCommands.FindTargets`): @a everyone, @s yourself, @p the nearest other player
  (same orbit by distance, else the same station), @r a random other player, and by state @alive (in space, not
  destroyed), @space, @docked, @dead, @survivors (an event's players never destroyed since its fight began,
  `EventRunner.Survived`: "reward @survivors {wave * 1000}"), each narrowed to one orbit by `[orbit=<station>]` (in its
  space or docked at its station: `@alive[orbit=78]`, `@r[orbit=Var Hastra]`); a command on several players runs for
  each
  ("/tp @a Player1", "/kick @r"); a destination is one player. Tab offers the selectors too. Typing "/" lists the
  matching commands over the line, after a command that
  takes a player the matching players; Tab completes the first and cycles through them (Shift+Tab back, "/" alone cycles
  all; before the channel key, Tab by default; `ChatView.Complete`, `NetCommands.Completions`);
  `/netstats` shows / hides the network stats (`NetStats`, PlayerPrefs `mp_netstats`, top left in the flight HUD, the
  station menu's top right under its Menu button, `ChatView`): host / client (dedicated server), Relay or direct, the client's ping, jitter and packet loss
  (Unity Transport's `GetConnectionStatistics` on the host connection), the host's ping per player (the first 3, then "+N more"), the data in / out per
  second and in all (`NetTransport`, UnityTransport counting Netcode's payloads, without the transport's / Relay's
  headers) and the players. While typing the game's keys
  are off: the code-made InputActions are disabled and the direct keyboard reads go through `NetChat.Keys` (null then;
  FlightHud, ShipController, Mining, SpaceLevel, StationMenu, PauseMenu), the station menu's navigation too.
- **Event graph quests** (single player; `EventHost`, `LocalPilot`, `LocalEvents`, `Session.GraphQuests`; later the main and
  add-on stories are to become graphs too, so quests behave like the story): the event system runs without a session through
  `EventHost` (`IPilot`: NetPlayer in a session, the one `LocalPilot` (id 0, Keith T. Maxwell, `%player%`) without;
  orders applied at once with `NetAdmin.Apply`, the local traffic's ships as `EventHost.EventShips`, notices as a HUD
  message / station toast, a clock that stops while the game is paused) and `LocalEvents` (DontDestroyOnLoad, made after
  the first scene) ticks it. The Start node's **Kind** (`EventKind`: Event, Bar Mission, Quest; "mission kind" line; old
  graphs = Event, a bar mission when titled) and **Starts when** ("mission starts <expr>"; empty = at once, "never" = only
  `startquest`). Quests start by themselves in a game scene (Space / Station, not a session's game, not the ending; while
  not paused) once their condition holds (`EventRunner.CheckQuestStarts`, every second; not the built-in graphs, which are
  multiplayer examples) and are lasting runs: a `GraphQuestState` record (save v13 `graphQuests` / `graphQuestsDone`)
  written at the start and at each `checkpoint <name>` (top level only, `Step.depth`; the variables, objective, target, quiet
  orbits), restored after a load from its last checkpoint (`EventRunner.RestorePending` → `RestoreLocal` →
  `EventRun.Resume`: the top-level handlers and parallel / every blocks before it again, the music / waypoint / respawn /
  restrict commands before it again). New lines: `objective [text]`, `target <station | off>` (the gold story icon on the
  map and the HUD: `EventRunner.IsQuestTarget`, the Missions window's Show on map), `questorbit <station> [off]` (no normal
  traffic there: `SpaceLevel` passes it as a story orbit), `startquest <name>`; nodes Checkpoint, Set Objective, Quest
  Orbit, Start Quest. `complete` in a quest pays and marks it done (`Session.GraphQuestsDone`); `fail` is the story's
  failure (`EventHost.QuestFailed` → `StorySpace.FailQuest` / the station dialog → the auto-save). The Missions window's Story
  panel lists the quests (title + objective) after the campaign's objective (`EventRunner.Quests`). Expressions gained this
  game's state (after the variables): campaign, credits, rank, station, system, ship, kills; cargo(item), has(item),
  visited(station), quest(name). `Session.ResetNewGame` ends single player's runs (`EventRunner.ResetLocal`).
  Single-player bar missions: `EventMissions.RequestOffers` builds the offers locally (mods' / Events folder graphs for
  one pilot, not the built-ins), Okay starts a lasting run (`AcceptLocal`, record kind Mission with the offer); it is the
  player's one mission (`Freelance.AcceptRefusal` refuses a freelance mission meanwhile and vice versa), shows in the
  Missions window's Freelance panel (client, offer, objective, Show on map for its target, Discard =
  `EventRunner.AbandonLocalMission`), a success counts like a freelance one (`OnEnded` success flag: missions +1, standing
  +5). Verified in Play mode: a mod quest starting after a load (Keith's dialogue with a mod voice clip), the Missions window,
  a save at its checkpoint restored without repeating the dialogue, docking at the target completing it (+5000, done); a mod
  bar mission offered, accepted, saved, restored and discarded; a mod's replacement station theme and a new track by
  /music; hosting and /event waves after the refactor.
  **Repeatable** story missions: `.gof2mission` `"repeat"` minutes (`mission repeat`, `MissionInfo.repeatMinutes`; the
  Start node's RepeatMinutes, the builder's `ProjectQuest.Repeat`): `EventRunner.QuestDone` puts it in
  `Session.GraphQuestRepeats` (name -> `PlaySeconds` it may start again; saved as `graphQuestRepeats` "name|seconds", no
  version bump) instead of `GraphQuestsDone`; `CheckQuestStarts` starts it again once that time has passed and its
  condition holds; `quest(name)` reads 2 meanwhile and StartQuest refuses it. Mission tools for roaming encounters:
  `randomstation()` (`EventRunner. RandomStation`: a station of a visible system, not the player's, 10 / 100-104 / 108,
  systems 25 / 27 / 32 / 33, a pirate base system, an empty orbit, `PirateEventRules.InStoryline`, a mobile or missing
  mod station; -1 none) and the text fills `{stationname(x)}` / `{systemname(x)}` (`Fill`, `PlaceName`;
  `QuestFile.StateWords` keeps them).
- **Event graph bar missions** (`EventMissions`): an event graph whose Start node has a Mission title (its Mission
  settings: offer text, client (a story character or "race name" like /dialog's speakers; empty: someone of the station's
  race), reward, offered at (stations; empty: every one), min / max pilots, offer chance % (`mission chance`, default 35,
  `MissionInfo.DefaultChance`; players' report 2026-10-23: the Pirate Hideout sat in every bar and could be taken again at
  once); compiled to `mission <key> <value>` lines) is offered in the multiplayer Space Lounges (not every bar: the server
  rolls each mission per station for 30 minutes, `EventMissions.Offered` / `OfferEntry`, keeping the offer, so re-docking
  rolls nothing and a generated client keeps its face; Who can take it, `mission takers universe | station | any`
  (`EventMissionTakers`): one squad in the universe, the default (offered nowhere while a team is on it and
  `EventRunner.StartMission` refuses a second, `MissionTaken`, Extra `mpEventMissionTakenBy`), one per station (taken out of
  that bar for 30 minutes, `Taken`; the universe mode does that too), or any number (never taken out); the
  visitor's plate names the mission only once talked to, like a freelance agent's, `LoungeChat.Plate`,
  `EventMissions.MarkTalked`; the end sends `Outcome` Success / Failure / Stopped and the team's games show the client's
  freelance-style message for the first two, 373-377 + 216 or 384-388 + 392 with the client's face and voice,
  `EventMissions.Finished`): the docking game asks the server (`NetState.RequestEventOffers`, from
  `StationLevel.BuildBar`) and each offer becomes a visitor on a free slot (`StationLevel.AddVisitor`; `LoungePanel` rebuilds its
  plates; `AgentOffer.EventMission` = 11 in `LoungeChat`: the offer, the reward, the pilots; Okay checks the whole squad docked
  here and the pilot count, confirms, `NetState.AcceptEventMission`). The server checks again and runs the event for that team
  (`EventRunner.StartMission`, the run's `team`: while it ticks `NetCommands.Scope` limits every selector to the team, @team names
  them; its counts, triggers, scoreboard and notices cover only them); every member holds it (`EventMissions.Active`: the
  Missions window's left panel shows it in a session), the others get the mission card. Mission Complete (`complete [reward] [|
  title]`: the reward (else the mission's) split evenly across the team in the reward box) and Mission Failed (`fail [title]`)
  end it; so do the team leaving its ships' orbits for 3 s (failed) or the session. One mission per player at a time.
  Verified in Play mode (hosted, solo): the offer at Var Hastra, the chat, accepting, the hideout's pirates, the reward.
  Stolen goods: the documents are only in the team's shop (not the shared stock); one member buying them takes them for
  the squad (the status). The host relays the results to the squad and anyone holding
  that mission (`NetState.MissionTeam`). In sessions the generator also rolls **15 Ore Mining** (never rolled by the
  original's generator, freelance_missions.md 2.3 / 4.1; offer text 801): one of the offering station's top 3 asteroid
  ores, 30-119 t; the orbit has int((int(0.2d)+1)hc) enemies of the client's enemy race on a route, the mining plant
  (`Traffic.MiningPlant`, docking type 1) and 2 client-race haulers looping 30 km that never fire; docking at the plant
  unloads the ore (`ObjectDocking`, 1 t per second, counted in the mission's `status`); won at the amount. Remake
  (players' feedback, 2026-10-17: 5-6 km in front of the station it felt out of place): all of it at a remote site 9-11 km
  out (`MiningSite`, plain C#: from the mission's seed, `MiningSite.Seed` = the orbit's shared seed with the squad's
  `netId`, else the mission's own values, so every game of the squad builds the same; first 40-60 km, too long a flight
  with a session's fast-forward at x1; the direction is drawn again until its field keeps 140 000 units from the orbit's
  field and the plant 50 000 from a jumpgate), with an asteroid field of its own
  beyond the plant (`OrbitBuilder.SpawnField`, `FreelanceOrbit.SiteAsteroids`: 5-7 big + 18-25 small rocks, all of the
  mission's ore, none within 15 000 units of the plant or 9000 of the way in) built in the runner's and the followers'
  games (`BuildSite`); the enemies asleep beside it (awake from afar they blocked the fast-forward all the way there); the
  player's route is the lockable "Mining site" waypoint 12 000 units short of the plant (`Route.name`, which the HUD, the
  autopilot menu and its "Target:" notice show instead of 548 / 573), and the briefing names it (Extra
  `miningSiteBriefing`). The site field's asteroids are indexed from `MiningSite.IndexBase` (1000 + 100 per slot, past the
  orbit's own field: `NetOrbit.AddField`), so NetState's destroyed lists and the drilling / landed indices keep them apart;
  players outside the team don't have the field and ignore those indices. A follower's stand-in plant and a takeover
  (`Promote`) use the site's own plant position.
