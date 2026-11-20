<!-- Made by GoF2 > Build > Modding AI Reference (ModdingScriptingReference) from the game's code: don't edit. -->

# Mod scripts (Lua): reference

A mod may carry Lua scripts in `scripts/` (Lua 5.2, run by MoonSharp). Where the JSON files and the mission
graphs describe content, a script is code: it hears the game's events, reads and changes the game's own objects
and can replace the functions the game's parts call each other through. `guide.md` "Scripts" explains it with
examples; this file is the reference. Rules that decide whether a script works:

- Every `.lua` directly in `scripts/` runs when the mod starts (`main.lua` first, the others by name). Files in
  subfolders run only through `require("lib.util")` (= `scripts/lib/util.lua`).
- One Lua state per mod. Globals are the mod's own; other mods are reached with `game.emit` / `game.on`.
- Scripts start in the main menu already and keep running through scene changes. They start again when the
  active mods change. Keep what must survive in `game.saved` (it is stored in the player's save file).
- C# members are used with their C# names: `Session.Credits`, `db:Item(5)` (`:` for methods of an object,
  `.` for statics and fields). Every class listed at the end is a global by its name. C# lists and
  dictionaries are objects, not Lua tables: `list.Count`, `game.get(list, 0)`, `game.table(list)` for a Lua
  copy. Arrays arrive as Lua tables (from 1). A Lua function works wherever a delegate is taken.
- The lists below are the public members. Private ones are reached with `game.field` / `game.setField` /
  `game.call` (`print(game.describe(x, true))` lists them). No `io`, no `os` but the clock, no .NET, no
  files but the mod's own.
- A script error never stops the game: it is a warning of the mod (the Mods screen, the Mod Builder's console).
- In multiplayer the scripts run in every player's game. `game.multiplayer()` says which one this is; only the
  host's game may run `game.command`.

## The game table

```
FILES
  scripts/main.lua        runs first when the mod starts
  scripts/other.lua       every .lua directly in scripts/ runs too (by name)
  scripts/lib/util.lua    a module: local util = require("lib.util")
                          a dependency's: require("othermod:lib.util")

THE GAME TABLE
  game.mod.id / .name / .version      this mod
  game.saved                          a table kept in the player's save file
                                      (numbers, texts, booleans, tables)
  game.config                         a table kept outside the saves (the
                                      mod's own settings); game.saveConfig()
  game.on(event, fn) -> handle        listen to an event (list below)
  game.after(seconds, fn) -> handle   once, in game time
  game.every(seconds, fn) -> handle   again and again
  game.off(handle)                    stop a handler, timer, hook or sequence
  game.emit(event, ...)               an event for every script mod (this game)
  game.log(...) or print(...)         to this console and the game's log
  game.message(text)                  a line on the player's screen
  game.command(line) -> answer        a chat / event command, as the server:
                                      game.command("title @a Welcome for 5")
  game.addCommand(name, fn)           a command of your own: fn(args, who)
                                      returns its answer; "/name" in the chat,
                                      an event graph's Command node, game.command
  game.multiplayer()                  "none", "host" or "client"
  game.scene()                        "MainMenu", "Space", "Station"
  game.space() / game.station()       the flight level / the docked level, or nil
  game.player()                       the player's ship in flight, or nil

SEQUENCES  (in any handler, timer or game.run; not in a hook or command)
  game.wait(seconds)                  this function goes on later, from here
  game.waitUntil(function() return Session.Credits > 5000 end)
  game.run(function() ... end) -> handle     starts a function that may wait
      game.on("space", function()
          game.wait(3)
          game.command("spawn @a Phantom pirate 2")
          game.waitUntil(function() return #game.find("NpcShip") == 0 end)
          game.message("All clear")
      end)

EVENTS  (game.on)
  update(dt), lateUpdate(dt) every frame; dt = 0 while paused
  scene(name)                a scene was loaded
  space(level)               the flight level stands (launch, jump, load)
  station(level)             docked: the hangar stands
  database(db)               the game's tables were loaded: change them here
  shipDestroyed(ship, byPlayer)
  playerDestroyed()
  trade(item, amount, price, station)   bought (+) or sold (-) in a shop
  missionAccepted(mission), missionSucceeded(mission, pay), missionFailed(mission)
  storyStep(step, previous)  the campaign moved on
  chat(from, text, own)      a chat line arrived (multiplayer)
  gameLoaded()               a save was loaded (game.saved is that save's)
  saving()                   just before game.saved is written
  stop()                     this mod's scripts are stopping

THE GAME'S CLASSES
  Every class of the game and of Unity's scene namespaces is a global by its
  name: Session, Database, Settings, Standing, GameHooks, Vector3, Mathf ...
  import("Full.Name") where two classes share a name.
  Class.__new(...)                    makes one: Vector3.__new(0, 1, 0)
  Session.Credits = Session.Credits + 1000
  print(game.describe(Session))       what a class or object offers scripts
  print(game.describe(Session, true)) ... its private members too
  game.classes("Ship")                the class names with that in them
  game.find("NpcShip")                the scene's components of a class
  game.findOne("SpaceLevel")
  game.component(object, "Class") / game.components / game.addComponent
  game.table(list)                    a Lua copy of a C# list / dictionary
  game.get(collection, key) / game.set(collection, key, value)
                                      lists count from 0
  Members that aren't public, and generic methods:
  game.field(target, "name") / game.setField(target, "name", value)
  game.call(target, "Method", ...)    returns its result, then its out values
  game.generic(target, "Method", { "Class" }, ...)
  A Lua function works wherever the game takes a delegate:
      SpaceHooks.WreckHacked = function(station, wreck) ... end

HOOKS
  game.hook(target, "Member", function(original, ...) ... end)
      replaces a delegate of the game; call original(...) to run the game's
      own code, return what the game should get (after it, the values of the
      delegate's out parameters):
      game.hook("GameHooks", "FactionBuyPrice", function(original, station, price)
          return math.floor(original(station, price) / 2)
      end)
  game.listen(target, "Member", function(...) ... end)
      adds to a C# event or delegate: game.listen("Settings", "Changed", fn)
  The hook classes GameHooks, SpaceHooks, UiHooks and NetSession hold most
  of what the game's layers tell each other.

PATCHES
  game.patch(target, "Method", function(original, self, ...) ... end)
      replaces any method of a game class, public or not (no self for a
      static method). original(self, ...) runs the code it replaced; return
      the result (then the method's ref / out values):
      game.patch("Shop", "GenerateItems", function(original, db, station)
          local list = original(db, station)
          list:RemoveAll(function(s) return db:Item(s.item).techLevel > 5 end)
          return list
      end)
  Overloads: name the parameter types as a 4th argument, "Database, int".
  game.describe(X, true) lists a class's methods. Not patchable: constructors,
  generic methods, lambdas, a struct's own methods, file / network methods.

YOUR MOD'S FILES AND CONTENT
  game.files("data")                  the mod's files (in a folder)
  game.readText("data/note.txt")      a text file of the mod, or nil
  game.readJson("data/prices.json")   ... as a table
  game.texture("icons/badge.png")     ... as a Texture2D
  game.sound("sounds/ping.ogg", function(clip) ... end)    ... as an AudioClip
  game.item("my_gun") / game.ship("other_mod:falcon") /
  game.system(...) / game.station(...)   content by its key -> its number here

OBJECTS AND INTERFACE
  game.behaviour(gameObject, {        a component written in Lua
      update = function(self, dt) ... end,     -- also lateUpdate, fixedUpdate,
      destroyed = function(self) ... end })    -- enabled, disabled,
                                               -- triggerEnter, triggerExit,
                                               -- collisionEnter
      self.gameObject, self.transform
  game.uiEvent(element, "ClickEvent", function(event) ... end) -> handle

OTHER PLAYERS  (multiplayer; in single player the message comes back to you)
  game.send("name", ...)              to every player's scripts (numbers,
                                      texts, booleans; through the host)
  game.onMessage("name", function(sender, ...) ... end)    sender: 0 = the host
  game.myId()                         this player's id
  Never trust another player's values: check them on the host.

LIMITS
  Lua 5.2 (MoonSharp). No io, no os but the clock, no .NET classes, no files
  but the mod's own: scripts reach the game and Unity's scene classes only.
  Structs are copies: set transform.position as a whole, not its .x.
  A call that runs too long is stopped; errors show here and in the mod
  browser, never crash the game. Players are asked before a mod with
  scripts runs.
```

## Hook classes

The game's layers call each other through these static delegates. `game.hook("Class", "Member", function(original, ...) end)`
replaces one (call `original(...)` for the game's own behaviour and return what the game should get);
`game.listen` adds a function beside it. Both are undone when the script stops.

### GameHooks

- `ResolveOriginal(arg1: Type, arg2: string): any` (not for scripts: an argument scripts can't hold): An original's material or prefab by its project path ("Materials/mat_20099_....mat", "Prefabs/....prefab"), made from the converted copy (OriginalContent.Resolve, GoF2.Space); unset: none.
- `SquadAcceptRefusal(): string`: Why the squad can't take a mission now (the whole squad docked here), null = it can.
- `MissionAccepted(obj: FreelanceMission)`: A mission was accepted: the squad gets it too.
- `MissionShared(arg1: FreelanceMission, arg2: bool)`: The mission changed (Recovery's return trip): the squad's copies follow.
- `CanDeliver(arg: FreelanceMission): bool`: Whether this player can deliver it (the containers / passengers aboard); unset: yes.
- `SplitReward(arg1: FreelanceMission, arg2: int): int`: This player's share of the pay; unset: all of it.
- `MissionFailed(arg1: FreelanceMission, arg2: int): int`: The mission failed: this player's share of a lost wager; unset: all of it.
- `StartQuest(arg1: string, arg2: bool): string`: Remake: starts a quest graph by name (single player; EventRunner.StartQuest: the reason it can't, null = started; 'quiet' = one under way or done is no error): a cruise drive encounter of kind "quest" (CruiseEncounters).
- `EventMissionAcceptRefusal(arg1: string, arg2: int): string`: Why a player or squad cannot accept an event graph bar mission (mission name, station); null = allowed.
- `ActiveEventMissionTitle(): string`: The title of the event graph bar mission this player is on, null = none.
- `Condition(expression: string, out error: string): bool` (a hook returns the out values after its result): An event graph expression on this game's state (EventRunner.Condition); unset: true.
- `Number(expression: string, out value: double, out error: string): bool` (a hook returns the out values after its result): An event graph expression's value on this game's state (EventRunner.Number: the mods' medal counters, Modding.ModMedals); unset: no value (false).
- `ResetLocalEvents()`: A new game: single player's graph runs end (EventRunner.ResetLocal).
- `StockShared(arg: int): bool`: The station's stock is the session's shared one (NetStock.Shared).
- `StockEntered(obj: StationStock)`: Docking: the shared stock replaces the rolled one (NetStock.OnEnterStation).
- `StockItemChanged(arg1: int, arg2: int, arg3: int, arg4: int)`: A unit bought / sold at the station: station, item, delta, price (NetStock.ItemChanged).
- `StockShipChanged(arg1: int, arg2: int, arg3: int)`: A dealer ship swapped: station, removed, added (NetStock.ShipChanged).
- `FactionBuyPrice(arg1: int, arg2: int): int`: A faction station's buying / selling price (NetFactionsClient); unset: the price.
- `FactionSellPrice(arg1: int, arg2: int): int`: A faction station's buying / selling price (NetFactionsClient); unset: the price.
- `FactionShipBuyPrice(arg1: int, arg2: int): int`: A faction station's buying / selling price (NetFactionsClient); unset: the price.
- `ProfileUpload()`: Save point in a session: the profile goes to the server instead (NetProfileClient.Upload).
- `SharedPirateBoard(): string`: The session's pirate board as JSON (NetState.PirateBoardJson), null = none.
- `SessionRule(bit: int, out on: bool): bool` (a hook returns the out values after its result): The session's value of a gameplay rule (NetRules.TryGet); false = no session, the player's own option.
- `RuleChangedByHost(arg1: int, arg2: bool)`: A player host changed a rule's option (NetRules.HostChanged).
- `SessionKaamoLevel(out value: int): bool` (a hook returns the out values after its result): The session's Kaamo upgrade limit (NetRules.TryGetKaamoLevel); false = no session, the player's own option.
- `KaamoLevelChangedByHost(obj: int)`: A player host changed the Kaamo upgrade limit (NetRules.HostKaamoLevelChanged).
- `SessionFlightStyle(out value: int): bool` (a hook returns the out values after its result): The session's flight model (NetRules.TryGetFlight: FlightStyles.Original / Free, SessionRules.PlayerFlight); false = no session, the player's own option.
- `CoopActive(): bool`: The session is a co-op campaign (NetState's flag); unset: no.
- `CoopLeader(): bool`: This game leads the co-op campaign (the host: its story is the crew's); unset: no.
- `CoopCrewSize(): int`: The crew's size (the players in the session); unset: 1.
- `CoopStepWon(obj: int)`: A crew member's game found the step won (CoopCampaign.ReportWon): the leader tells the crew.
- `CoopAdvanced(obj: int)`: A guest's game advanced the story by itself (a level's or a jump's own advance, not a won step): the leader follows (from index).
- `CoopProgress(obj: string)`: A guest's progress on the step (CoopCampaign.Progress as JSON) for the leader to merge.
- `CoopPublish(obj: string)`: The leader's shared story changed (CoopCampaign.State as JSON): out to the crew.
- `CoopFreelanceDone(obj: long)`: A crew member finished a freelance mission (its id): it counts toward step 13's three.
- `KitBuildEffect(arg: int): KitEffect`: A customizable ship's build effect (ModShipKits.BuildEffect), null = not a kit ship.
- `KitMounts(arg1: int, arg2: int): List<WeaponMount>`: A kit ship's mounts of a slot type in its build (ModShipKits.BuildMounts), null = not a kit ship.
- `ParseShipSounds(arg1: JObject, arg2: ModInfo, arg3: string): any` (not for scripts: an argument scripts can't hold): A ships.json entry's "sounds" parsed (ModShipSounds.ParseShip), null without one.
- `KitBuildText(arg: JObject): string` (not for scripts: an argument scripts can't hold): A ships.json "build" object as build text (ModShipKits.Build).
- `EventFlightStyle(): int`: The flight style an event sets (Events.EventFlight.Forced), -1 = none.
- `RepairLiveShip()`: Cheats.Repair: the live ship in flight too (hull, armor, shield, gamma).
- `ResetControls()`: Settings' "default settings": the key bindings too (GameControls.ResetToDefaults).
- `ModsScanned(obj: IEnumerable<string>)`: The mods found by a scan (every id, on or off): the caches of the others go (ShipShadowBaker.PruneCache).
- `FlightMessage(obj: string)`: A line on the flight HUD's message plate (Achievements.Progress, Hud::hudEventMedal); unset: not in flight.
- `Notice(arg: string): bool`: game.message: a line on screen wherever the player is (the flight HUD's message plate, else a station toast); false = nothing can show it now (the scene's interface isn't up yet: ModScripts tries again for a moment).
- `RunCommand(arg: string): string`: game.command: a chat / event command line ("spawn pirate 3") run as the server runs it (NetCommands.RunOnServer); its answer, which says so when this game isn't the server.
- `ScriptSend(arg1: string, arg2: string): bool`: game.send: a script's message (its name, its values as ModScripts packs them) for every player's scripts, through the host (NetState.ScriptMessageRpc); in single player this game's own (ModScripts.Receive). False = it couldn't be sent (the session isn't up).

### SpaceHooks

- `PeersProvider(): IEnumerable<IOrbitPeer>`
- `LocalPeerProvider(): IOrbitPeer`
- `ProxiesProvider(): IEnumerable<IOrbitProxy>`
- `CratesProvider(): IEnumerable<IOrbitCrate>`
- `SharedOrbitProvider(): ISharedOrbit`
- `InterceptMirrorShot(arg1: UInt64, arg2: UInt32)`: Point defense shot down another game's mirrored shot (source object, shot id): ask the server to cancel it (NetPlayer.InterceptMirrorShot), so the shot's later hit counts for nothing.
- `LevelBuilt(obj: SpaceLevel)`: The level is built (SpaceLevel.Start's end): multiplayer adds its NetOrbit.
- `StationBuilt(obj: StationLevel)`: The station is built: multiplayer adds its NetHangar.
- `VisitorRace(arg1: Agent, arg2: int): int`: The race used to choose a bar visitor's silhouette (agent, station index). Mods may override it before the prefab is selected; the default preserves the game's Midorian-with-Nivelian-face exception.
- `OrbitEmpty(arg: int): bool`: No other player is in that orbit (NetState.OrbitEmpty): this game builds its traffic.
- `OutOfSight(arg: Vector3): Vector3`: A point out of the other players' sight (NetOrbit.OutOfSight), game units.
- `OtherDockedHere(arg: int): bool`: Another player is docked at that station (NetHangar.OtherDockedHere).
- `SiegeWon(obj: int)`: The Kaamo siege at that station is won (NetState.SiegeWonRpc): the others there win it too.
- `PirateSightingDone(arg1: int, arg2: UInt64)`: A pirate sighting's event is destroyed (id, killer's client id; NetState.PirateSightingDoneRpc).
- `WreckHacked(arg1: int, arg2: string)`: The player hacked a wreck in this orbit (station, the wreck's name): an event's or quest's "on hacked" (EventRunner.OnWreckHacked; in a session through NetState.WreckHackedRpc).
- `PadDocked(obj: int)`: Remake mods: the player docked at a station's pad outside (station): an event's or quest's "on pad" (EventRunner.OnPadDocked; in a session through NetState.PadDockedRpc).
- `ShipScanned(arg1: int, arg2: string)`: Remake mods: the player's scanner locked a ship in this orbit (station, the ship's plate name): an event's or quest's "on scanned" (EventRunner.OnShipScanned; in a session through NetState.ShipScannedRpc).
- `Notice(obj: string)`: A line in the multiplayer chat as a notice (NetChat.Notice).
- `MissionResultView(arg1: int, arg2: int, arg3: string): bool`: A squad mission's result reached this game (result, share, from whom): the follower orbit's dialog; true = shown.
- `AddMissionStatus(arg1: FreelanceMission, arg2: int)`: Progress on the squad's mission (ore unloaded, a container captured...): the squad has it.
- `RunMissionHere(arg: int): bool`: This game builds the mission orbit at that station (no squadmate runs it there).
- `MissionTeamHere(arg: int): bool`: A member of this player's mission team is in that orbit.
- `LocalSquadProvider(): int`: The local player's squad (0 = none) and its members (this game's player included).
- `SquadProvider(): IEnumerable<IOrbitPeer>`
- `ToggleDistress(): string`: The distress call on / off (its message); help a squadmate (by client id; its message).
- `HelpSquadmate(arg: UInt64): string`
- `DistressArrival(station: int, out position: Vector3, out rotation: Quaternion): bool` (a hook returns the out values after its result): Arriving by travel at a squadmate's distress call: beside them.
- `FollowSquadmate(arg: UInt64): string`: Follow a squadmate from orbit to orbit, or stop when it is the one followed (by client id; its message); the one followed (ulong.MaxValue = nobody). Multiplayer.NetFollow.
- `FollowedSquadmate(): UInt64`
- `TeleportPose(out position: Vector3, out rotation: Quaternion): bool` (a hook returns the out values after its result): A teleport's pose for this arrival (NetTeleport.TakePose).
- `GuestAssembly(arg1: UInt64, arg2: int): AssemblyData`
- `GuestShipY(arg1: UInt64, arg2: int): float`
- `GuestBuild(arg1: UInt64, arg2: int): ModShipKits.Build`
- `ArenaProvider(): ArenaMatchInfo`
- `InArenaMatch(): bool`: In a match of the running session (NetArenaClient.InMatch).
- `ArenaSpawnPose(): Pose`
- `ArenaLevelReady()`
- `EventNoDocking(): bool`: An event's Restrict Travel.
- `EventNoJumps(): bool`: An event's Restrict Travel.
- `EventNoKhador(): bool`: An event's Restrict Travel "khador": the Khador Drive (and the slipstream) only.
- `EventCinematic(): bool`: An event's cutscene holds the camera / makes the player invulnerable.
- `EventInvulnerable(): bool`: An event's cutscene holds the camera / makes the player invulnerable.
- `EventQuestionOpen(): bool`: An event's question is on screen: the controls and keys rest.
- `EventSceneMusic(): float`: The scene's music volume factor while an event's track plays (1 = none).
- `QuietOrbit(arg: int): bool`: A quest keeps that orbit free of normal traffic.
- `RequestEventOffers(arg1: int, arg2: Func<Agent, bool>)`: The event graphs' bar missions offered at that station (their visitors).
- `RespawnDue(arg: float): bool`: An event's respawn point takes the destroyed player back (this long after the death, ms).
- `Respawn()`
- `CoopLevelState(obj: CoopLevelState)`: The leader's story level now (a few times a second while it runs): out to the crew in that orbit.
- `CoopRadioShown(arg1: int, arg2: int, arg3: int)`: The leader's level showed radio line 'line' of step 'index' in 'station'.
- `CoopRoute(arg1: int, arg2: List<Vector3>)`: The leader's level route changed (station, points in game units, the index reached).
- `CoopCamera(arg1: int, arg2: Vector3, arg3: Quaternion, arg4: float)`: The leader's cutscene camera (station, position, rotation, field of view).
- `CoopFx(arg1: int, arg2: int, arg3: Vector3)`: The leader's level played a one-off effect (station, CampaignLevel.CoopFx kind, position in Unity metres).
- `CoopTravel(arg1: int, arg2: int, arg3: bool, arg4: bool)`: The leader's level moved it on (station left, station reached, docked, by travel).
- `CoopLevelFailed(obj: int)`: The leader's level in 'station' failed (or the leader died in it): the crew's failure.
- `CoopDialog(obj: bool)`: This game's story conversation opened / closed (the leader holds its level meanwhile).
- `CoopHold(arg: int): bool`: The leader: a crew member in 'station' has a story conversation open (the level holds).
- `CoopLeaderHere(arg: int): bool`: The leader flies in 'station' (a follower's level waits for it).
- `CoopLeaderPose(station: int, out position: Vector3, out rotation: Quaternion): bool` (a hook returns the out values after its result): The leader's ship's pose in 'station' (false = not there).
- `MultiplayerWindowOpen(): bool`: The multiplayer window is open.
- `ExternalDockTestQueued(arg: int): bool`: The Mod Builder's external docking test (UI.ModBuilderPlay): one queued for this station; take it, with its dock point (SpaceLevel.StartExternalDockTest).
- `TakeExternalDockTest(station: int, out point: int): bool` (a hook returns the out values after its result)
- `StarMapOpenProvider(): bool`: The star map is open; open it (false = it couldn't).
- `OpenStarMap(db: Database, mode: StarMapMode, jumpDrive: bool, closed: Action<StarMapResult>, promptStation: int, askVoid: bool): bool`
- `VrEye(): Camera`: The VR eye camera (VrRig.Current.Eye), null without VR.
- `ServerShutOff(arg: Scene): bool`: A dedicated server switches the menu scene off (DedicatedServer.ShutOff): true = it did.
- `KeepHud()`: A level swap keeps the flight HUD (FlightHud.KeepForSwap): out of the old scene before it goes, the new scene's own HUD gone before it builds a panel, the kept one into the new scene once that is active.
- `DropNewHud()`: A level swap keeps the flight HUD (FlightHud.KeepForSwap): out of the old scene before it goes, the new scene's own HUD gone before it builds a panel, the kept one into the new scene once that is active.
- `SettleHud(obj: Scene)`
- `SetSceneFog(obj: bool)`: The scene's fog (Bootstrap.SetSceneFog: off below Quality High).
- `TemporalUpscaler(): bool`: A temporal upscaler (STP, DLSS, FSR 2+, MetalFX Temporal) is active.
- `KeysProvider(): Keyboard`: The keyboard unless the chat or an event's question takes it (NetChat.Keys).

### UiHooks

- `LatestDialogueProvider(): IDialogueWindow`: The newest dialogue window (DialogueView.Latest), null = none.
- `FlightMessage(arg: string): bool`: A line on the flight HUD's message line; false = no flight HUD in this scene.
- `StationToast(arg: string): bool`: A toast in the station menu; false = no station menu in this scene.
- `StationDialog(arg1: string, arg2: Action, arg3: bool): bool`: The station menu's dialog (text, on Yes, info = only OK); false = no station menu in this scene.
- `RefreshStationCredits()`: The station menu shows the credits / cargo again.
- `StationCargoChanged()`: The station menu shows the credits / cargo again.
- `OpenTrade()`: The multiplayer window's trade tab (docked).

### NetSession

- `ActiveProvider(): bool`
- `SessionGameProvider(): bool`
- `DebugAllowedProvider(): bool`
- `HostingProvider(): bool`
- `DbProvider(): Database`
- `ServerTimeProvider(): double`
- `LocalIdProvider(): UInt64`
- `OrbitSeedProvider(arg: int): int`

## The classes scripts use most

`static` members are used on the class (`Session.Credits`), the others on an object of it. `name: type` is a field or
property, `Name(arguments): result` a method. In a script, `print(game.describe("Class"))` prints the same for any class.

### Session (`GoF2Remake.Data.Session`)

```
static AcceptedBlindMap: int
static AcceptedBlindRisk: int
static AgentsTalkedTo: int
static AlienRemainsCollected: int
static ArrivedByLoad: bool
static ArrivedBySystemJump: bool
static ArrivedByTravel: bool
static ArrivedByWormhole: bool
static ArrivedWithoutGear: bool
static AsteroidsDestroyed: int
static AttackedStations: HashSet<int>
static BattleshipsDestroyed: int
static BlindKills: int
static Blueprints: List<BlueprintState>
static BombsDetonated: int
static BoozeBought: int
static BoozeTypes: HashSet<int>
static Campaign: Campaign
static CampaignMission: int
static CapitalLootReadyAt: float
static Cargo: List<ItemStack>
static CarriedEncounter: CruiseEncounters.Entry
static CloakMs: long
static CollectedBounties: int[]
static ComingFromVoid: bool
static CompletedWorld: bool
static ContainersDelivered: int
static CoreTypesMined: HashSet<int>
static CoresMined: int
static CratesSalvaged: int
static Difficulty: float
static DifficultyEasy: float = 0
static DifficultyExtreme: float = 1,5
static DifficultyHard: float = 1
static DifficultyNormal: float = 0,5
static DockedFromSpace: bool
static DriveArrival: bool
static DriveFailArrival: bool
static Economy: Economy
static EliteFlags: HashSet<int>
static EmpDisabledNow: int
static EndingPending: bool
static EnergyCellsForNextJump: int
static EnterInterstellar: bool
static Equipment: List<ItemStack>
static Es2NoRedirect: bool
static Es2StartDocked: bool
static Es2TakeOff: bool
static FailCount: int
static FixedLootTaken: HashSet<string>
static FreePlay: bool
static FreePlayMission: int = 20
static FreelanceCompleted: int
static FreelanceMission: FreelanceMission
static GoodsProduced: int
static GraphQuestRepeats: Dictionary<string, float>
static GraphQuests: List<GraphQuestState>
static GraphQuestsDone: HashSet<string>
static GraveRiserKills: int
static Hardcore: bool
static HiddenBlueprintsFound: int
static HighestCredits: long
static Hints: HashSet<int>
static InformerFailed: bool
static InformerKilled: bool
static InstantJump: bool
static InvasionDepartures: int
static JumpgatesUsed: int
static JunkDestroyed: int
static KaamoItems: List<ItemStack>
static KaamoShips: List<StoredShip>
static KaamoState: int
static KaamoVip: bool
static KhadorCronusGift: bool
static KillTally: Dictionary<string, int>
static Kills: int
static LastArrivalHullPercent: int
static LastDepartureTime: float
static LastDockedStation: int
static LastFailedMission: int
static LastFreighterHull: int
static LaunchedFromStation: bool
static LiberatorAsteroids: int
static LomaTollPaid: bool
static LomaTollRefused: bool
static MaxCredits: long = 9223372036854775807
static MaxRank: int = 20
static Medals: int[]
static MissionSelectGame: bool
static ModBounties: int[]
static ModCampaign: string
static ModGameOptions: HashSet<string>
static ModMedals: Dictionary<string, string>
static ModRaceStanding: Dictionary<string, int>
static ModWanted: Dictionary<string, WantedState>
static NextPirateSightingId: int
static NoMedals: bool
static OffersDeclined: int
static OffersRepeated: int
static OreMined: int
static OreStreak: int
static OreTypesMined: HashSet<int>
static ParkedShip: ParkedShip
static Passengers: int
static PassengersDelivered: int
static PendingProducts: List<PendingProduct>
static PirateBaseDestroyed: bool[]
static PirateBaseRewardPending: bool
static PirateKills: int
static PirateSightings: List<PirateSighting>
static PlayerArmor: int
static PlayerGamma: float
static PlayerHull: int
static PlayerShield: float
static PreviousStationIndex: int
static ProgrammedStation: int
static RecentOrbits: List<int>
static RecentStations: List<StationStock>
static RiftEntry: bool
static RocketAsteroids: int
static RouteDestination: int
static RouteSafest: bool
static RunId: string
static ScriptData: Dictionary<string, string>
static SeenItems: HashSet<int>
static SelectedSecondary: int
static ShipBuilds: Dictionary<string, string>
static ShipIndex: int
static ShipMods: List<int>
static ShipsBought: HashSet<string>
static SlipstreamAutopilot: bool
static SlipstreamDeparture: bool
static Standing: int[]
static StationIndex: int
static StoryAgentsAccepted: HashSet<int>
static StoryCounter: int
static StoryMission: StoryMission
static StoryRadioPending: bool
static StoryStepStart: float
static StoryTargets: List<int>
static StreamOutAfterCutscene: bool
static SystemShown: Dictionary<int, bool>
static SystemVisible: bool[]
static UnlockedBlueprints: HashSet<int>
static Unsaleable: HashSet<int>
static UsedMissionTypes: bool[]
static VisitedStations: HashSet<int>
static VoidInvasionStation: int
static VoidInvasionSystem: int
static VoidOrbit: int = -1
static VoidReturnStation: int
static Wanted: List<WantedState>
static WantedHints: int
static WarpArrival: bool
static WarpArrivalPoint: Vector3?
static WarpHeading: Quaternion
static WarpLocal: bool
static WarpView: Vector3
static WarpViewTurn: Quaternion
static WasteDry: int
static WasteSinceRare: int
static WasteTons: int
static WingmanContractMs: float
static WingmanPortrait: int[]
static WingmanRace: int
static WingmanShips: List<int>
static WingmanShowEmp: bool
static Wingmen: List<string>
static WingmenHired: int
static XpBonus: int
static Credits: long
static DifficultyFactor: float (read only)
static HasAutosave: bool (read only)
static InVoidOrbit: bool (read only)
static IsExtreme: bool (read only)
static MaxModLevel: int (read only)
static PlaySeconds: float
static Rank: int (read only)
static WorldIndex: int (read only)
static Xp: int (read only)
static AddCredits(amount: long)
static AddShipMod(mod: int)
static ArriveInOrbit(station: int)
static Autosave()
static DifficultyName(d: float): string
static EconomyName(e: Economy): string
static HasMod(mod: int): bool
static IsBooze(item: int): bool
static ItemRef(item: int): string
static LoadAutosave(): bool
static ModLevel(mod: int): int
static ModLevel(mods: IEnumerable<int>, mod: int): int
static RankStartXp(rank: int): int
static ResetNewGame()
static ResetStartShip()
static SaturatingAdd(a: long, b: long): long
static ShipRef(ship: int): string
static UseCompletedWorld()
```

### Database (`GoF2Remake.Data.Database`)

```
Assemblies: List<AssemblyData>
static CronusSessionPrice: int = 1150000
static DefaultIntegratedCloaks: Dictionary<int, int> (read only)
static DefaultIntegratedDrives: int[] (read only)
static IntegratedCloakItem: int = 95
IntegratedCloaks: Dictionary<int, int>
IntegratedDrives: HashSet<int>
IntegratedItems: Dictionary<int, List<ItemStack>>
Items: List<ItemData>
NpcTurrets: Dictionary<int, List<int>>
ShipModels: Dictionary<int, string>
ShipOwnTextures: HashSet<int>
ShipSubsystems: Dictionary<int, List<CapitalSubsystem>>
Ships: List<ShipData>
Stations: List<StationData>
Systems: List<SystemData>
Wanted: List<WantedData>
WeaponMounts: List<WeaponMountSet>
WingmanCrews: List<WingmanCrew>
Economy: Economy (read only)
static Shared: Database (read only)
__new()
AssemblyByName(name: string): AssemblyData
static BuildFlightStats(ship: ShipData, equipment: IEnumerable<ItemData>, handlingUpgrades: int?): FlightStats
BuiltInItems(ship: int): List<ItemStack>
static GameMounts(ship: int): List<WeaponMount>
HasIntegratedDrive(ship: int): bool
IntegratedCloak(ship: int): int
Item(index: int): ItemData
ItemByName(name: string): ItemData
MountsOf(shipIndex: int, slotType: int): List<WeaponMount>
NpcTurretsOf(shipIndex: int): List<int>
Ship(index: int): ShipData
ShipAssembly(ship: int, raceName: string?, original: bool?): AssemblyData
ShipByName(name: string): ShipData
StationsIn(systemIndex: int): IEnumerable<StationData>
```

### ItemData (`GoF2Remake.Data.ItemData`)

```
static CoreYieldAttr: int = 189
allowedClasses: string[]
attrKeys: int[]
attrValues: int[]
blueprint: List<BlueprintPart>
category: string
categoryId: int
description: string
excludedClasses: string[]
highestPriceSystem: int
index: int
lookIndex: int
lowestPriceSystem: int
maxPrice: int
minPrice: int
modKey: string
modded: bool
name: string
occurrence: int
statList: List<StatEntry>
techLevel: int
type: string
CoreYield: int (read only)
Look: int (read only)
TypeId: int (read only)
__new()
Attr(id: int, fallback: int?): int
HasAttr(id: int): bool
static IsBeamLook(look: int): bool
Stat(key: string, fallback: int?): int
```

### ShipData (`GoF2Remake.Data.ShipData`)

```
armor: int
cargo: int
chaseCamera: float[]
classTags: string[]
cockpit: Vector3?
description: string
dockingRole: string
explosionScale: float
flightBoth: FlightBonus
flightFree: FlightBonus
flightOriginal: FlightBonus
flightRules: Dictionary<string, float>
flightStyle: int
handling: float
handlingMultiplier: float
index: int
name: string
orbitCameraDistance: float
price: int
shipClass: string
shipClassLabel: string
slots: ShipSlots
smokePoints: List<Vector3>
__new()
```

### SystemData (`GoF2Remake.Data.SystemData`)

```
forbiddenGoodsOrUnknown: List<int>
index: int
initiallyVisible: bool
jumpRoutesTo: List<int>
jumpgateStation: int
mapPosition: MapPos
name: string
race: string
raceId: int
securityLevel: int
stations: List<int>
textureIndex: int
unknownTriple: List<int>
__new()
```

### StationData (`GoF2Remake.Data.StationData`)

```
index: int
name: string
system: int
systemName: string
techLevel: int
textureIndex: int
__new()
```

### Agent (`GoF2Remake.Data.Agent`)

```
static ChallengeNone: int = 0
static PlayerWon: int = 1
static RivalWon: int = 2
accepted: bool
challengeResult: int
character: string
costs: int
known: bool
male: bool
mission: FreelanceMission
modRace: int
name: string
offer: int
portrait: int[]
race: int
sellBlueprint: int
sellItem: int
sellMod: int
sellPrice: int
sellQuantity: int
sellShip: int
sellSystem: int
station: int
storyIndex: int
textIds: List<int>
wingmanShips: List<int>
wingmen: List<string>
HasMission: bool (read only)
IsStory: bool (read only)
Side: int (read only)
__new()
```

### FreelanceMission (`GoF2Remake.Data.FreelanceMission`)

```
amount: int
bonus: int
clientMale: bool
clientName: string
clientPortrait: int[]
clientRace: int
clientStation: int
costs: int
difficulty: int
failed: bool
good: int
netId: long
promisedBonus: int
reward: int
status: int
target: int
targetName: string
textIds: List<int>
type: int
won: bool
CurrentBonus: int (read only)
IsEmpty: bool (read only)
Name: string (read only)
Total: int (read only)
__new()
Clone(): FreelanceMission
```

### Settings (`GoF2Remake.Data.Settings`)

```
static AccessibilityKeys: string[] (read only)
static BloomBoth: int = 3
static BloomOff: int = 0
static BloomOriginal: int = 2
static BloomRemake: int = 1
static ControlsKeys: string[] (read only)
static CosmeticsKeys: string[] (read only)
static DefaultDeadzone: float = 0,125
static DefaultMouseDeadzone: float = 0,08
static ExperienceCosmetic: int = 1
static ExperienceCustom: int = 3
static ExperienceKeys: string[] (read only)
static ExperienceOriginal: int = 0
static ExperienceRemake: int = 2
static FarMarkerOriginal: int = 0
static FarMarkerRing: int = 1
static FarMarkerSmallDot: int = 2
static FpsBottom: int = 6
static FpsBottomLeft: int = 5
static FpsBottomRight: int = 7
static FpsLeft: int = 3
static FpsRight: int = 4
static FpsTop: int = 1
static FpsTopLeft: int = 0
static FpsTopRight: int = 2
static FrameGenerationDlss: int = 0
static FrameGenerationDynamic: int = 6
static FrameGenerationFsr: int = 1
static FrameRateChoices: FrameRate[] (read only)
static GameplayKeys: string[] (read only)
static GraphicsKeys: string[] (read only)
static HangarShadowsAll: int = 2
static HangarShadowsOff: int = 0
static HangarShadowsPlayer: int = 1
static HudFull: int = 0
static HudMinimal: int = 1
static HudOff: int = 2
static KaamoVipAlways: int = 1
static KaamoVipCard: int = 0
static KaamoVipOff: int = 2
static LanguageKeys: string[] (read only)
static MedalSoundChime: int = 0
static MedalSoundOff: int = 2
static MedalSoundOriginal: int = 1
static MouseSensitivityMax: float = 3
static MouseSensitivityMin: float = 0,1
static OriginalFov: float = 69,900856
static PadGlyphsAuto: int = 0
static PadGlyphsPlayStation: int = 2
static PadGlyphsSwitch: int = 3
static PadGlyphsXbox: int = 1
static PostAaFxaa: int = 1
static PostAaOff: int = 3
static PostAaSmaa: int = 0
static PostAaTaa: int = 2
static PresetCustom: int = 4
static PresetHigh: int = 2
static PresetKeys: string[] (read only)
static PresetLow: int = 0
static PresetMedium: int = 1
static PresetUltra: int = 3
static ScreenCutoutBothSides: int = 1
static ScreenCutoutClear: int = 0
static ScreenCutoutFull: int = 2
static SkyCombined: int = 3
static SkyEnhanced: int = 1
static SkyEs2: int = 2
static SkyOriginal: int = 0
static SoundKeys: string[] (read only)
static UiWidth16x9: int = 0
static UiWidth21x9: int = 1
static UiWidthFull: int = 2
static UpscalerDlss: int = 3
static UpscalerFsr: int = 1
static UpscalerFsrTemporal: int = 4
static UpscalerMetalFxSpatial: int = 5
static UpscalerMetalFxTemporal: int = 6
static UpscalerOff: int = 0
static UpscalerStp: int = 2
static UpscalerXess: int = 7
static AndroidDefaults: bool (read only)
static AnimatedDialogue: bool
static AntiLag: bool
static AppleUpscaler: bool (read only)
static AutoAdvanceDialogue: bool
static AutoJumpgate: bool
static AutoRoll: bool
static BarRobots: bool
static Bloom: bool (read only)
static BloomStrength: float
static BloomStyle: int
static BlueprintAutocomplete: bool
static BombArming: bool
static BoostToggle: bool
static Brightness: int
static BrightnessExposure: float (read only)
static CameraShake: float
static CapitalShips: bool
static ChallengeComboSounds: bool
static ChallengeScenery: bool
static ClassicBloom: bool (read only)
static CloakBay: bool
static CloakLosesPursuers: bool
static CockpitInCameraCycle: bool
static CollisionDamage: bool
static ColorblindMarkers: bool
static CursorSizePercent: int
static CustomCosmeticExperience: bool (read only)
static CustomGameplayExperience: bool (read only)
static DerelictWrecks: bool
static DetailDistance: int
static DetailDistanceFactor: float (read only)
static DialogueHighlights: bool
static DiscordPresence: bool
static DisplayMode: DisplayMode
static DlssPreset: int
static DyingCargoMarkers: bool
static DynamicVibrance: bool
static DynamicVibranceIntensity: float
static DynamicVibranceSaturation: float
static EachWeaponSound: bool
static EmpBreaksCloak: bool
static EnhancedSkies: bool (read only)
static Es2Aiming: bool
static Es2FreeFlightCamera: bool
static Es2NpcFlight: bool
static FarShipMarkers: int
static FastForwardCruise: bool
static FieldOfView: float
static FireSmoke: bool
static FlightStyle: int
static FloatingTouchStick: bool
static Fog: bool
static FpsPosition: int
static FrameGeneration: int
static FrameGenerationProvider: int
static FrameGenerationTarget: int
static FrameRate: FrameRate
static FreshInstall: bool (read only)
static Fsr4Lite: bool
static GameCursor: bool
static GermanVoices: bool (read only)
static GyroSensitivity: float
static GyroSteering: bool
static HangarDepthOfField: bool
static HangarFlights: bool
static HangarRobots: bool
static HangarShadows: int
static HapticsIntensity: float
static HasStaged: bool (read only)
static HdResources: bool
static Hdr: bool
static HdrAcesPreset: int
static HdrBlack: float
static HdrDetectLimits: bool
static HdrHueShift: float
static HdrPaperWhite: float
static HdrPeak: float
static HdrRangeReduction: int
static HdrTonemapping: int
static Headlights: bool
static HighContrast: bool
static HintScalePercent: int
static HostileAutopilotFastForward: bool
static HudMode: int
static HudOpacityPercent: int
static HudScalePercent: int
static InformerOriginalRule: bool
static InputHints: bool
static InterstellarDistance: bool
static InvertDrillX: bool
static InvertDrillY: bool
static InvertOrbitX: bool
static InvertOrbitY: bool
static InvertPitch: bool
static InvertYaw: bool
static IosDefaults: bool (read only)
static KaamoKeepsEquipment: bool
static KaamoMaxLevel: int
static KaamoSpecialUiFeatures: bool
static KaamoStacking: bool
static KaamoVipMode: int
static KeepEnemyLock: bool
static KeyAutofire: bool
static KhadorSlipstream: bool
static Language: string
static LaunchCamera: bool
static LensFlare: bool
static LevelPitch: bool
static LiveSkies: bool
static Lods: bool
static MarkerDistance: bool
static MasterVolume: float
static MedalSound: int
static ModShipTraffic: int
static ModShipTrafficChance: float (read only)
static MouseDeadzone: float
static MouseSensitivity: float
static MouseSteering: bool
static Msaa: int
static MusicVolume: float
static MuteInBackground: bool
static NebulaClouds: bool
static NeuralRendering: bool
static NeuralRenderingIntensity: float
static NeuralRenderingQuality: int
static NeuralRenderingSkin: float
static NeuralRenderingStructure: float
static NeuralRenderingTone: float
static NintendoLayout: bool
static NisSharpness: float
static NpcDogfight: int
static NpcPlayerEngines: bool
static OriginalChaseCamera: bool
static OriginalTargetLock: bool
static OriginalUi: bool
static PadGlyphs: int
static PirateEvents: bool
static PlayOnInConversations: bool
static PostAntialiasing: int
static RebuiltPlanets: bool
static ReduceMotion: bool
static Reflex: int
static RemakeFreeLook: bool
static RemasteredEffects: bool
static RenderScale: float
static Resolution: Vector2Int
static ScreenCutout: int
static Sensitivity: float
static SfxVolume: float
static ShipCollision: int
static ShopByCategory: bool
static ShowFps: bool
static ShowStoryStep: bool
static SkyStyle: int
static SmarterNpcTargeting: bool
static SpeedrunAllCampaigns: bool
static SpeedrunCompare: string
static SpeedrunEverySplit: bool
static SpeedrunGameTime: bool
static SpeedrunLiveSplit: bool
static SpeedrunPosition: int
static SpeedrunTimer: bool
static Staging: bool (read only)
static StationScale: float
static StickDeadzone: float
static SystemMapAnimationSpeed: int
static TextureFiltering: int
static TextureQuality: int
static TiltCalX: float
static TiltCalZ: float
static TiltCalibrated: bool
static TiltSensitivity: float
static TiltSteering: bool
static TouchLeftHeight: float
static TouchOpacity: float
static TouchRightHeight: float
static TurretLeadAim: bool
static TutorialHints: bool
static UiFont: string
static UiMarginPercent: float
static UiScalePercent: int
static UiTextScalePercent: int
static UiWidth: int
static Upscaler: int
static UpscalerQuality: int
static VoiceLanguage: string
static VoiceVolume: float
static VrGrabControls: bool
static WelcomeShown: bool
static ApplyExperience(experience: int)
static ApplyPreset(preset: int)
static ApplyScopedExperience(cosmetics: bool, choice: int)
static ApplyStaged()
static BeginStaging()
static CurrentExperience(): int
static CurrentScopedExperience(cosmetics: bool): int
static DiscardStaged()
static EndStaging()
static ExperienceValue(experience: int, key: string): float
static FrameRateLimit(rate: FrameRate): int
static LocalKaamoLevel(): int
static LocalRules(): int
static MigrateExperienceDefaults()
static MouseMove(delta: Vector2): Vector2
static MusicTrackOn(track: string): bool
static OrbitTurn(d: Vector2): Vector2
static OverrideAndroidDefaults(android: bool?)
static OverrideIosDefaults(ios: bool?)
static PresetValue(preset: int, key: string): float
static Reset(keys: IEnumerable<string>)
static ResetExperienceToRemake(cosmetics: bool)
static ResetToDefaults()
static SelectCustomExperience(cosmetics: bool)
static SelectCustomGameplayExperience()
static SetMusicTrackOn(track: string, on: bool)
static Unstage(keys: string[])
static ViewApplied(): Settings.StagedScope
static ViewStaged(): Settings.StagedScope
static event Changed()
static event StagedChanged()
```

### Localization (`GoF2Remake.Data.Localization`)

```
static ExtraFolder: string = GoF2Localization
static Count: int (read only)
static IsLoaded: bool (read only)
static Language: string (read only)
static Extra(key: string, english: string): string
static Get(id: int): string
static Load(language: string, table: TextAsset)
static SetExtra(language: string, key: string, text: string)
static event Changed()
```

### Standing (`GoF2Remake.Flight.Standing`)

```
static Pirate: int = 8
static Specter: int = 10
static Void: int = 9
static SignatureRace: int (read only)
static ApplyDelict(race: int, p: int)
static ApplyKill(race: int, systemRace: int)
static EnemyRaceOf(race: int): int
static InvalidateSignature(): bool
static IsEnemy(race: int): bool
static IsEnemyWith(race: int, axis0: int, axis1: int, signature: int, modRaces: IReadOnlyDictionary<string, int>?): bool
static IsFriend(race: int): bool
static IsFriendWith(race: int, axis0: int, axis1: int, signature: int, modRaces: IReadOnlyDictionary<string, int>?): bool
static Look(race: int): int
static Move(race: int, amount: int, set: bool)
static RaceName(race: int): string
static RaceWord(w: string): int
static RacesHostile(a: int, b: int): bool
static RawToward(race: int): int
static Toward(race: int): int
static TowardWith(race: int, axis0: int, axis1: int, signature: int, modRaces: IReadOnlyDictionary<string, int>?): int
```

### Achievements (`GoF2Remake.Data.Achievements`)

```
static BaseCount: int = 36
static Count: int = 45
static Enabled: bool (read only)
static GotAllGoldMedals: bool (read only)
static GotAllMedals: bool (read only)
static GotAllSupernovaMedals: bool (read only)
static SupernovaUnlocked: bool (read only)
static VoidXUnlocked: bool (read only)
static WingmenPayPlayer: bool (read only)
static Check(db: Database): List<int>
static Elite(medal: int, count: int)
static Grade(medal: int): int
static Has(medal: int): bool
static Hint(medal: int, progress: string?): string
static Name(medal: int): string
static NextGrade(db: Database, medal: int, out grade: int, out count: int, out target: int, out fraction: float): bool
static NextGradeOf(medal: int): int
static NoWeaponOrEquipment(db: Database): bool
static Progress(medal: int, count: int)
static ProgressText(medal: int, count: int): string
static Threshold(medal: int, grade: int): int
```

### SpaceLevel (`GoF2Remake.World.SpaceLevel`)

```
static DockRange: float = 16000
Mobiles: List<MobileStation> (read only)
static StationPortObject: string = station_pads
ambientIntensity: float
mainCamera: Camera
planetLight: Light
planetLightIntensity: float
stationOverride: int
stationScene: string
sunIntensityAt2: float
sunLight: Light
ActiveEs2: Es2Dock (read only)
Asteroids: Transform (read only)
Backdrop: Backdrop (read only)
Campaign: CampaignLevel (read only)
CanDock: bool (read only)
Challenge: ChallengeOrbit (read only)
Cloak: PlayerCloak (read only)
Collision: PlayerCollision (read only)
ComingFromVoid: bool (read only)
Cutscene: bool (read only)
Database: Database (read only)
Devices: PlayerDevices (read only)
Dialogue: bool (read only)
DockedInOrbit: bool (read only)
Docking: ObjectDocking (read only)
DockingBlocked: bool (read only)
DriveArrivalHidden: bool (read only)
DriveArriving: bool (read only)
Es2: Es2Dock (read only)
EventWormhole: Wormhole (read only)
Extender: TimeExtender (read only)
ExternalDocked: bool (read only)
Fog: SpaceDust (read only)
FreeLook: FreeLookCamera (read only)
FreelanceOrbit: FreelanceOrbit (read only)
GasClouds: GasCloudField (read only)
Health: PlayerHealth (read only)
Hints: FlightHints (read only)
HudShown: float (read only)
IsStoryOrbit: bool (read only)
Jumpgate: GameObject (read only)
LaunchCameraOver: bool (read only)
Layout: OrbitLayout (read only)
Leaving: bool (read only)
LocalArrival: bool (read only)
MainCamera: Camera (read only)
Mining: Mining (read only)
Navigation: Navigation (read only)
NetAuthority: bool (read only)
NetOrbitId: int (read only)
OrbitInfoVisible: bool (read only)
Player: ShipController (read only)
Radar: CombatRadar (read only)
Siege: KaamoSiege (read only)
StartSequenceOver: bool (read only)
Station: GameObject (read only)
StationDockRange: float (read only)
StationInfo: StationData (read only)
StorySpace: StorySpace (read only)
StreamOutArrival: bool (read only)
SystemJump: SystemJump (read only)
SystemJumpgateStation: int (read only)
Traffic: Traffic (read only)
Turret: PlayerTurret (read only)
Turrets: IReadOnlyList<PlayerTurret> (read only)
UndockPoint: Vector3 (read only)
WarpHolds: bool (read only)
Weapons: WeaponSystem (read only)
Wormhole: Wormhole (read only)
__new()
CloseEventWormhole()
Dock(flyIn: bool?)
DropNetAuthority()
EndStartSequence()
LightFor(orbit: OrbitLayout)
LoadLastSave()
MoveForTeleport(position: Vector3, rotation: Quaternion): bool
MovePlayer(position: Vector3, rotation: Quaternion)
OpenEventWormhole(game: Vector3, destination: int)
PlayEnding()
static PlayerTriedToFly(): bool
static PrepareArrival(asteroidSeed: int)
Reload()
RestoreOrbitSky()
ReturnToLastDocked()
RideWormhole()
SpawnNetworkAsteroids(seed: int)
StartExternalDockTest(pointIndex: int)
SwapPlayerShip(shipIndex: int)
static SwapScene(scene: string)
TakeOverNetAuthority()
TeleportOut(station: int, dock: bool): bool
TravelTo(station: int, streamOut: bool?)
UndockFromOrbit()
static VisitedInSpace(station: int): bool
WarpPlayer(position: Vector3, rotation: Quaternion)
event DevicesChanged()
and what MonoBehaviour has
```

### StationLevel (`GoF2Remake.World.StationLevel`)

```
ChatFrameX: float
InspectFrameX: float
static OffCentreShip: int = 8
VisitorPicked: Action<int>
glowMaterials: Material[]
hangarAdds: AudioClip[]
hangarAmbience: AudioClip
hangarAmbientScale: float
hangarLightIntensity: float
keyLight: Light
loungeAdds: AudioClip[]
loungeAmbience: AudioClip
mainCamera: Camera
mainViewAdds: AudioClip[]
mainViewAmbience: AudioClip
music: AudioClip[]
musicSource: AudioSource
shipOverride: int
spaceScene: string
stationOverride: int
sunIntensityAt2: float
visitorBobolan: GameObject
visitorGlow: GameObject
visitorGrey: GameObject
visitorMultipod: GameObject
visitorNivelian: GameObject
visitorShadow: GameObject
visitorTerranFemale: GameObject
visitorTerranMale: GameObject
visitorVossk: GameObject
ArrivedFlying: bool (read only)
BarRace: int (read only)
BarRoom: Transform (read only)
CustomBar: ModInteriors.Room (read only)
CustomHangar: ModInteriors.Room (read only)
Database: Database (read only)
Db: Database (read only)
DockedInOrbit: bool (read only)
FreeCamera: bool
FreeCameraFov: float
FreeCameraPose: Pose
HangarIndex: int (read only)
HangarRoom: Transform (read only)
Inspect: HangarInspect (read only)
IntroPlaying: bool (read only)
Layout: OrbitLayout (read only)
MainCamera: Camera (read only)
MusicHeld: bool
PlayerDeparting: bool (read only)
PlayerFlying: bool (read only)
PlayerShip: Transform (read only)
ShownShip: int (read only)
Station: StationData (read only)
Stock: StationStock (read only)
Traffic: HangarTraffic (read only)
View: StationView (read only)
VisitorAgentCount: int (read only)
VisitorCount: int (read only)
__new()
AddVisitor(agent: Agent): bool
BeginInspect(): bool
BeginInspect(minRadii: float): bool
Depart(then: Action)
EndInspect()
FlingShip(gameRadiansPerSecond: float)
FocusVisitor(index: int)
static GameRoomMarkers(assembly: string): Dictionary<string, Vector3>
Launch()
PreviewEngineDrive(drive: float)
PreviewEngines(on: bool)
RebuildHangarAs(index: int, raceId: int)
RefreshParkedShips()
ReplacePlayerShip(index: int)
RestoreHangar()
RoomMove(from: Vector3, to: Vector3): Vector3
RotateShip(gameRadians: float)
SetHangarWindowOpen(open: bool)
SetView(view: StationView, force: bool?)
SkipIntro()
SkipPlayerFlight()
VisitorAgent(i: int): Agent
VisitorFeet(i: int): Vector3
VisitorHead(i: int): Vector3
event ViewChanged()
and what MonoBehaviour has
```

### Traffic (`GoF2Remake.World.Traffic`)

```
LaunchCameraRunning: Func<bool>
LevelMissionActive: Func<bool>
LockedTarget: Func<Target>
MenuBackdrop: bool
PlayerSecondaryAmmo: Func<int>
RadarHidden: Func<bool>
RestockPlayer: Func<int, int>
Ships: List<NpcShip> (read only)
AttackRace: int (read only)
ChatterVisible: Traffic.Chatter (read only)
ChoiceTitle: string (read only)
ConvoyRoute: Route (read only)
FleetBattleRaging: bool (read only)
FriendCargoStolen: bool
HoldFire: bool
HostileCount: int (read only)
IsStoryOrbit: bool (read only)
LivingWingmen: List<NpcShip> (read only)
LomaTollPaid: bool (read only)
MusicMuted: bool
Player: Target (read only)
Security: int (read only)
StationIndex: int (read only)
StationPosition: Vector3 (read only)
SystemRace: int (read only)
VoidAttack: bool (read only)
WantedUncovered: bool (read only)
Wormhole: Wormhole (read only)
__new()
Adopt(spec: SpawnSpec, rotation: Quaternion, hullFraction: float): NpcShip
AlarmAllFriends(race: int, radio: bool)
Ask(text: string, yes: string, no: string, onYes: Action, onNo: Action)
Ask(title: string, text: string, yes: string, no: string, onYes: Action, onNo: Action)
static AssemblyOf(db: Database, spec: SpawnSpec): AssemblyData
AttackWanted(s: NpcShip, dmg: int)
CalmClip(): AudioClip
static Carried(spec: SpawnSpec): bool
CommandWingmen(command: int, locked: Target, playerRoute: Route)
ConnectPlayers(playerExemptRace: int?)
DestroyCapitalTurrets(host: SpawnSpec)
DestroyTurrets()
static Enemy(spec: SpawnSpec): bool
static FighterTurretAssembly(ship: int): string
static FighterTurretLocal(ship: int): Vector3
FriendTurnedEnemy(race: int)
HoldChatter(ms: float)
LomaPirateHit()
static MiningPlant(): SpawnSpec
OnHackWon(ship: NpcShip)
OnShipDied(ship: NpcShip, byPlayer: bool)
PayBounty(reward: int)
PirateEventDone(ship: NpcShip)
PirateStationAction(guardWoke: bool)
static PlasmaArray(layout: OrbitLayout, cm: int): SpawnSpec
QueueLine(text: int, speakerId: int, voice: string?)
RaceRadio(text: string, race: int)
RaceSpaceMusic(): AudioClip
ReleaseEnemies()
Say(line: Traffic.Chatter)
SetArrivalSize(size: float)
SetPassive(passive: bool)
Setup(database: Database, layout: OrbitLayout, player: Target, station: GameObject, storyOrbit: bool?, wormhole: Wormhole?, oneAFrame: bool?)
ShowSorting(sorting: WasteProcessing.Sorting, db: Database, closed: Action)
SpawnShip(spec: SpawnSpec): NpcShip
SpawnWingmen(player: Transform, unarmed: bool)
TakeEnemyOnTail(): bool
TakePlayerDown(hullShare: float): bool
Warn(text: string)
WingmanSay(text: string, name: string, face: int[], chance: float?): bool
event BountyCollected(obj: int)
event ChoiceRequested(arg1: string, arg2: string, arg3: string, arg4: Action, arg5: Action)
event ConvoyDone()
event Message(obj: string)
event ShipDied(arg1: NpcShip, arg2: bool)
event WasteSorted(arg1: WasteProcessing.Sorting, arg2: Database, arg3: Action)
and what MonoBehaviour has
```

### NpcShip (`GoF2Remake.World.NpcShip`)

```
AttachedTurret: NpcShip
static BurnRecords: int[] (read only)
ExtraGuns: List<Gun> (read only)
static HostileToLocalBySquad: Func<NpcShip, bool>
static HostileToRemote: Func<NpcShip, Target, bool>
static RemoteDockedAtObject: Func<Target, bool>
static RemotePlayers: IReadOnlyList<Target>
static TerritoryToLocal: Func<NpcShip, int>
static TurretModelPrefix: string = turret:
static WreckModelPrefix: string = wreck:
aggressors: HashSet<UInt64> (read only)
alwaysEnemy: bool
alwaysFriend: bool
alwaysNeutral: bool
detectRange: float
enemies: List<Target>
frozen: bool
noFriendlyFire: bool
roleSpeedFactor: float
scriptedFire: bool
scriptedSpeed: float
shootingEnabled: bool
turnedEnemy: bool
Asleep: bool (read only)
CanCloak: bool (read only)
CargoList: IReadOnlyList<ItemStack> (read only)
CloakPercent: float (read only)
Cloaked: bool (read only)
CloakingPossible: bool
Current: NpcShip.State (read only)
CurrentTarget: Target (read only)
DockIndex: int
DockingName: string
DockingOnly: bool (read only)
DockingType: int
Dying: bool (read only)
DyingWithCargo: bool (read only)
FighterExplosionScale: float (read only)
FreighterExplosionScale: float (read only)
Gone: bool (read only)
GunDps: float (read only)
GunItem: int (read only)
Guns: IEnumerable<Gun> (read only)
HasCargo: bool (read only)
Hidden: bool (read only)
HiddenByScript: bool (read only)
HitTargets: List<Target> (read only)
Hp: Hitpoints (read only)
Inactive: bool (read only)
IsFixed: bool (read only)
IsFreighter: bool (read only)
IsJumper: bool (read only)
IsTurret: bool (read only)
IsWingman: bool (read only)
static JumpSuppressed: bool (read only)
LocalOnly: bool
MindControlled: bool (read only)
MissionCrateTaken: bool (read only)
MissionShip: bool
Model: Transform (read only)
ModelPath: string
MountedTurrets: NpcMountedTurrets (read only)
Moving: bool (read only)
OffersDocking: bool (read only)
Order: NpcOrder (read only)
OwnSpeed: float (read only)
PlayerOwned: bool (read only)
Race: int (read only)
RadarHidden: bool
Rig: GunRig (read only)
Role: NpcRoles.Role (read only)
RouteIndex: int (read only)
ScriptEnemy: Target (read only)
ScriptRadarHidden: bool (read only)
SecondGunItem: int (read only)
ShipMarker: bool
SpacePointSet: int (read only)
Spec: SpawnSpec (read only)
StoryBurns: int (read only)
StoryShip: bool
StructureScale: float (read only)
Target: Target (read only)
Turret: TurretCore (read only)
__new()
AddOwnLoot(capital: bool?)
BoostAway()
BreakCloak()
Cloak(ms: float, instant: bool?)
CreateStealCrate(): Crate
Deactivate()
DestroyAsTurret()
HitByBlast(gun: Gun, t: Target, dmg: int, emp: int, center: Vector3)
InitPush(center: Vector3, radius: float)
static IsStructure(spec: SpawnSpec): bool
JumpOut()
static LocalBoxes(volumes: List<CollisionVolume>): Bounds[]
LootTaken()
MakePlayerTurret()
MakeWingman(slot: int, armed: bool)
MindControl(ms: float)
OnPlayerDisabled()
OnPlayerEmp(emp: int)
OnRemoteEmp(client: UInt64, emp: int)
OnRemoteHit(client: UInt64, dmg: int)
Place(position: Vector3, forward: Vector3)
static PrimaryShotFactor(p: CustomPrimary, gameReloadMs: float): float
RestoreState(unitsPerMs: float, routeIndex: int, asleep: bool, stillInactive: bool, turned: bool, empOff: bool, empElapsedMs: float)
Resume()
Revive(position: Vector3)
SetAlwaysEnemy()
SetEngineSound(on: bool)
SetExhaust(on: bool)
SetGun(itemIndex: int, damageFactor: float, reloadMs: float?)
SetHold()
SetHull(hull: int, alsoMax: bool?)
SetLoot(items: List<ItemStack>)
SetMoving(on: bool)
SetOnlyEnemy(t: Target)
SetOrder(o: NpcOrder)
SetRace(race: int)
SetRoute(r: Route)
SetSecondaryGun(itemIndex: int, damageFactor: float)
SetSpeed(unitsPerMs: float)
SetVisible(visible: bool)
Setup(owner: Traffic, database: Database, spec: SpawnSpec, prefab: GameObject, fxRoot: Transform)
ShiftRoute(gameDelta: Vector3)
StealFrom(item: int, amount: int)
StopStoryBurn(record: int)
StoryBurn(record: int): WreckBurn
Uncloak()
Vanish()
Wake()
WingmanCommand(command: int, locked: Target, playerRoute: Route)
WingmanSays(kind: string, chance: float?)
static WreckIndex(ship: int, race: int): int
static WreckPrefab(assets: CombatAssets, ship: int, race: int): GameObject
and what MonoBehaviour has
```

### SpawnSpec (`GoF2Remake.Flight.SpawnSpec`)

```
static CapitalBattleship: int = 1
static CapitalCarrier: int = 2
static CapitalMod: int = 4
static CapitalVossk: int = 3
static ConvoyEscort: int = 2
static ConvoyFreighter: int = 1
static ConvoyTurret: int = 3
static EventBoss: int = 2
static EventGuard: int = 4
static EventOutpost: int = 1
static EventTurret: int = 3
alwaysEnemy: bool
alwaysFriend: bool
alwaysNeutral: bool
asleep: bool
assembly: AssemblyData
ballisticResistance: float
battleFoe: SpawnSpec
capital: int
capitalEnhanced: bool
capitalHost: SpawnSpec
capitalPart: bool
collisionId: int
convoyRole: int
deadButSelectable: bool
deathMs: float
derelict: bool
dockingType: int
empPoints: int
energyResistance: float
escortLeader: SpawnSpec
escortOffset: Vector3
eventTag: int
eventTier: int
eventWreck: bool
explosionScale: float
extraLoot: List<ItemStack>
fixedObject: string
fleetBattle: bool
freighter: bool
group: NpcGroup
guard: bool
gunFactor: float
gunItem: int
hiddenBlueprint: int
hitRadius: float
hitpoints: int
inactive: bool
lootAmount: int
lootItem: int
missionCrate: int
modBlueprint: int
modCapital: bool
name: string
nameText: int
noDevices: bool
noFighterTurret: bool
noLoot: bool
npcType: NpcShipType
pirateEvent: int
placedOn: SpawnSpec
position: Vector3
race: int
radarHidden: bool
role: string
rotation: Vector3
route: Route
routeAround: SpawnSpec
scale: float
secondaryFactor: float
secondaryItem: int
shield: int
shieldRechargeMs: int
ship: int
sightingId: int
skin: string
spacePoints: int
speed: float
standIn: GameObject
standInAsleep: List<Behaviour>
startsDead: bool
stationPort: bool
stationary: bool
structure: bool
turretAssembly: string
turretHunter: bool
turretItem: int
turretItems: List<int>
wantedEscortOf: int
wantedIndex: int
wreckPrefab: GameObject
HullScale: float (read only)
__new()
Clone(): SpawnSpec
```

### PlayerHealth (`GoF2Remake.Flight.PlayerHealth`)

```
ArcMs: float[] (read only)
static HitRadiusUnits: float = 1200
invulnerable: bool
Dead: bool (read only)
static EmergencyActive: bool (read only)
EmergencyLeftMs: float (read only)
EmergencyLengthMs: float (read only)
GameOver: bool (read only)
Gamma: float (read only)
GammaLoopActive: bool (read only)
HasArmor: bool (read only)
HasShield: bool (read only)
Hp: Hitpoints (read only)
ShieldHitMs: float (read only)
Target: Target (read only)
__new()
Kill(force: bool?)
RefillGamma()
RefreshLoadout(db: Database, keepFractions: bool?)
ReloadFromSession(db: Database)
static RepairLive()
static RepairRates(repair: ItemData, out hullMs: float, out armorMs: float)
Setup(db: Database, controller: ShipController, chaseCamera: ChaseCamera, weaponSystem: WeaponSystem)
StoreToSession()
event GameOverStarted()
event Hit()
event Message(obj: string)
and what MonoBehaviour has
```

### ShipController (`GoF2Remake.Flight.ShipController`)

```
DriveMs: float
DriveTurnDegS: float
ExternalSpeedMetersPerSecond: float
static RateToDegrees: float = 0,087890625
static StickRadius: float = 0,5
autopilotTarget: Func<Vector3>
bankDegreesPerH: float
bankSmoothing: float
engineEarScale: float
externalControl: bool
impairment: float
inputLocked: bool
invertPitch: bool
invertYaw: bool
mass: float
metersPerUnit: float
modelHeld: bool
modelTumbling: bool
mouseSteering: bool
pitchTiltDegreesPerH: float
rollDegreesPerSecond: float
sensitivity: float
stats: FlightStats
steeringLocked: bool
throttleChangePerSecond: float
tiltMode: bool
turnOnly: bool
useBuiltInInput: bool
visualModel: Transform
ChorusFlightActive: bool (read only)
static Dampeners: bool (read only)
FreeFlightActive: bool (read only)
Maneuver: Maneuver (read only)
ManeuverSlide: Vector3 (read only)
Model: FlightModel (read only)
MouseInDeadzone: bool (read only)
MouseOffset: Vector2 (read only)
SpeedMetersPerSecond: float (read only)
SteerInput: Vector2 (read only)
StrafeSlide: Vector3 (read only)
TouchFree: bool (read only)
WebFactor: float (read only)
__new()
AlignToHorizon()
ApplyStats()
Boost()
static CapTurn(pitch: float, yaw: float, maxDeg: float)
CenterMouse()
OnHitPush(amount: float)
RequestDodge(type: int): bool
SetRoll(roll: float)
SetSteer(steer: Vector2)
SetThrottle(t: float)
SetTouchFree(move: Vector2, hover: float, boost: bool, drift: bool?)
ToggleDampeners()
static event DampenersChanged(obj: bool)
event DodgeRequested()
and what MonoBehaviour has
```

### Gun (`GoF2Remake.Flight.Gun`)

```
AutoAim: Func<Target>
static BeamRadiusAttr: int = 172
static BeamRangeUnits: float = 30000
static BeamReachAttr: int = 176
BeamSweeps: Func<Target, bool>
static BombArmingUnits: float = 2000
static ChargeMsAttr: int = 177
static DamageTypeAttr: int = 190
static DisruptorShieldPercent: int = 200
static DotDamageAttr: int = 168
static DotMsAttr: int = 169
static HeatCoolMsAttr: int = 174
static HeatShotsAttr: int = 173
Ignores: Func<Target, bool>
static MetersPerUnit: float = 0,05
Mirror: bool
static OverheatMsAttr: int = 175
static ShieldBypassAttr: int = 161
static ShieldDamageAttr: int = 124
static SlowFreezeAttr: int = 167
static SlowMsAttr: int = 162
static SlowPercentAttr: int = 163
static VulnerableMsAttr: int = 171
static VulnerablePercentAttr: int = 170
beamRadius: float (read only)
beamReach: float (read only)
bullets: Gun.Bullet[] (read only)
categoryId: int (read only)
chargeMs: float (read only)
damage: float
damageType: DamageType (read only)
dotDamage: float
dotMs: float
echo: GunEcho (read only)
emp: float
heatCoolMs: float (read only)
heatShots: int (read only)
homingDelayMs: float
isBeam: bool (read only)
isSecondary: bool (read only)
itemIndex: int (read only)
kind: Gun.Kind (read only)
launchTurn: Quaternion
lifetimeMs: float (read only)
lookIndex: int (read only)
magnitude: float (read only)
missiles: bool (read only)
mountLocal: Vector3 (read only)
overheatMs: float (read only)
owner: Target
reloadAcc: float
reloadMs: float
salvoCount: int (read only)
salvoGapMs: float (read only)
salvoShots: int (read only)
shieldFactor: float
slowFactor: float
slowFreeze: bool
slowMs: float
speedUnitsPerMs: float (read only)
spreadError: float
vulnerableMs: float
vulnerablePercent: float
BeamDir: Vector3 (read only)
BeamLengthUnits: float (read only)
BeamTarget: Transform (read only)
BombInFlight: bool (read only)
Charge01: float (read only)
Charged: bool (read only)
Guided: bool
Heat01: float (read only)
Homing: bool (read only)
IsBomb: bool (read only)
IsMissile: bool (read only)
LastLock: Target (read only)
Overheated: bool (read only)
Ready: bool (read only)
ReloadRate: float (read only)
SustainedReloadMs: float (read only)
WaitMs: float (read only)
__new(item: ItemData, mountLocal: Vector3, secondary: bool)
__new(visualItem: ItemData, damage: float, reloadMs: float, pool: int, lifetimeMs: float, speedUnitsPerMs: float, missiles: bool?)
static BombArmed(ageMs: float, speedUnitsPerMs: float, arming: bool): bool
Charge(dtMs: float, held: bool)
Cool(dtMs: float)
Detonate()
static FiresPlainShots(item: ItemData): bool
Inject(position: Vector3, velocity: Vector3, up: Vector3, lifeMs: float): bool
Inject(position: Vector3, velocity: Vector3, up: Vector3, lifeMs: float, shotId: UInt32, sourceObjectId: UInt64, playerSource: bool, out index: int): bool
IsActive(i: int): bool
static IsBeamLook(look: int): bool
RemoveAll()
SetAim(i: int, local: Vector3)
SetTarget(i: int, t: Target)
static ShieldFactor(item: ItemData): float
ShootDown(i: int): Vector3
SteerBullet(i: int, stick: Vector2, dtMs: float, rateRadPerMs: float, yawAxis: Vector3??, pitchAxis: Vector3??)
TagNetworkShot(index: int, shotId: UInt32, sourceObjectId: UInt64, playerSource: bool?)
TryFire(ship: Transform): int
TryFire(origin: Vector3, rotation: Quaternion, addForwardOffset: bool?): int
Update(dtMs: float, targets: IReadOnlyList<Target>, lockTarget: Target)
VisualScale(i: int): float
static WithinBeam(p: Vector3, a: Vector3, b: Vector3, radius: float): bool
event AreaHit(arg1: Target, arg2: int, arg3: int, arg4: Vector3)
static event Detonated(arg1: Gun, arg2: Vector3)
event Echoed(obj: Vector3)
event Expired(obj: int)
event Fired(arg1: Gun, arg2: int)
event Hit(arg1: int, arg2: Target, arg3: Vector3)
event Ignited(obj: Vector3)
static event LockShaken(arg1: Gun, arg2: Target)
event OverheatStarted(obj: Gun)
event SceneryHit(obj: Vector3)
```

### Navigation (`GoF2Remake.Flight.Navigation`)

```
static AboutToReachUnits: float = 20000
static AmbushAheadUnits: float = 22000
ApproachRuns: bool
Cloak: PlayerCloak
Devices: PlayerDevices
Docking: ObjectDocking
EncountersAllowed: bool
Extender: TimeExtender
FastForwardRadioBlocked: bool
HasWingmen: Func<bool>
HostilesPresent: bool
JumpRefusalText: Func<string>
JumpsBlocked: Func<bool>
KhadorBlocked: Func<bool>
static PlanetDiscTilt: float = 55
PlanetJumpRefused: Func<int, bool>
static PlanetSunUnits: float = 70000
static PlanetUnitsPerMapUnit: float = 6
static PullOutMs: float = 2500
RadarHidden: Func<bool>
static RunApproachUnits: float = 280000
ShipLockActive: bool
ShipLocked: Transform
Ships: List<NpcShip>
static SignalAheadUnits: float = 45000
static SignalLifeMs: float = 180000
static SignalLostUnits: float = 200000
static SignalReachUnits: float = 12000
static SignalSideUnits: float = 25000
SpawnEncounter: Action<CruiseEncounters.Entry, Vector3, Vector3>
StationObject: GameObject
StationReplaced: bool
Targets: List<Navigation.Target> (read only)
TravelBusy: Func<bool>
spaceScene: string
AboutToReach: bool (read only)
ArrivalHeld: bool
AsteroidField: Navigation.Target (read only)
Autopilot: bool (read only)
AutopilotTarget: Navigation.Target (read only)
BlocksAsteroidLock: bool (read only)
CanFastForward: bool (read only)
CanOpenActions: bool (read only)
CanOpenMenu: bool (read only)
CanUseDrive: bool (read only)
Candidate: Navigation.Target (read only)
Conversation: bool
static ConversationRuns: bool (read only)
Cruise: bool (read only)
static CruiseFactor: float (read only)
static Cruising: bool (read only)
Drive: CruiseDrive (read only)
DriveCruising: bool (read only)
FastForward: bool (read only)
GoingToGate: bool (read only)
GoingToMobile: Navigation.Target (read only)
GoingToStation: bool (read only)
static InputHalted: bool (read only)
Interstellar: bool (read only)
Jumping: bool (read only)
LandmarkLocked: bool (read only)
LeavingInterstellar: bool (read only)
LockFastForward: bool (read only)
LockFrame: int (read only)
LockTimeMs: int (read only)
LockTimer: float (read only)
Locked: Navigation.Target (read only)
MenuEmpty: bool (read only)
MenuIsActions: bool (read only)
MenuOpen: bool (read only)
static MusicPaused: bool (read only)
OutOfSlipstream: bool (read only)
PauseMenuOpen: bool
Paused: bool
PlayerRoute: Route (read only)
static PressesBlocked: bool (read only)
PromptText: string (read only)
PullOut: float (read only)
RadarOff: bool (read only)
ReachedGate: bool (read only)
RunGoal: Vector3 (read only)
RunPlanet: Navigation.Target (read only)
ShipLockDocking: NpcShip (read only)
SignalPlaced: bool (read only)
static TravelBoostAllowed: bool (read only)
VoidDive: Navigation.Target (read only)
VoidDiveMs: float
WorldScale: float (read only)
Zone: Navigation.Target (read only)
__new()
static ActsInAlienOrbit(kind: Navigation.Kind): bool
AddMobileStation(station: Transform, index: int, name: string, tech: int): Navigation.Target
ArrivalPoint(sun: Navigation.Target): Vector3
ChooseMenuEntry(target: Navigation.Target)
CloseMenu()
ContinueToProgrammedStation(): bool
static EncounterName(e: CruiseEncounters.Entry): string
static FormatDistance(units: float): string
Interact(): bool
InterruptDrive()
MenuEntries(actions: bool?): List<Navigation.Target>
OpenMenu(actions: bool?, force: bool?)
PlanetDisc(sun: Navigation.Target, out right: Vector3, out depth: Vector3): bool
PlanetsLaidOut(sun: Navigation.Target): bool
Post(text: string, kind: HudMessageKind?)
Refuse()
Refuse(text: string)
static ScriptedArrival(station: int): bool
SetAutopilot(target: Navigation.Target)
SetEventWaypoint(route: Route, name: string, freelance: bool)
SetEventWormhole(w: Wormhole)
SetFastForwardHeld(held: bool)
SetRoute(route: Route, freelance: bool?)
SetWormhole(wormhole: Wormhole)
Setup(database: Database, layout: OrbitLayout, backdrop: Backdrop, controller: ShipController, miningSystem: Mining, chaseCamera: ChaseCamera, weaponSystem: WeaponSystem)
StartSlipstream(): bool
static SunOf(t: Navigation.Target): Navigation.Target
static SyncMusic(music: AudioSource, held: bool)
event InterstellarRequested()
event KhadorRequested()
event Message(arg1: string, arg2: HudMessageKind)
event SystemReached(obj: int)
event WorldScaled(arg1: Vector3, arg2: float)
and what MonoBehaviour has
```

### ModManager (`GoF2Remake.Modding.ModManager`)

```
static BaseId: string = gof2
static PickedModsSub: string = Mods
static PickedProjectsSub: string = ModProjects
static Active: IReadOnlyList<ModInfo> (read only)
static ActiveSignature: string (read only)
static BaseContent: ModInfo (read only)
static BuilderOpen: bool (read only)
static BuilderProject: ModInfo (read only)
static Folders: IEnumerable<string> (read only)
static InSession: bool (read only)
static Installed: IReadOnlyList<ModInfo> (read only)
static MainFolder: string (read only)
static PickedFolder: string (read only)
static PickedFolderName: string (read only)
static PickedProblem: string (read only)
static Revision: int (read only)
static SinglePlayerActive: List<ModInfo> (read only)
static UserFolder: string (read only)
static UserMods: List<ModInfo> (read only)
static BeginSession(ids: IEnumerable<string>)
static EndSession()
static Find(id: string): ModInfo
static FolderOf(mod: ModInfo): string
static FromPickedFolder(mod: ModInfo): bool
static InactiveReason(m: ModInfo): string
static IsEnabled(id: string): bool
static Move(id: string, delta: int)
static NeededBy(id: string, enabled: List<string>?): List<ModInfo>
static OpenFolder(mod: ModInfo?)
static Scan()
static SetEnabled(id: string, on: bool)
static SetPickedFolder(tree: string, name: string)
static event Changed()
```

### ModInfo (`GoF2Remake.Modding.ModInfo`)

```
Errors: List<string> (read only)
Warnings: List<string> (read only)
Broken: bool (read only)
Hash: string (read only)
Id: string (read only)
IsBase: bool (read only)
Manifest: ModManifest (read only)
Name: string (read only)
NetHash: string (read only)
NetSignature: string (read only)
Preview: Texture2D (read only)
Signature: string (read only)
Version: string (read only)
LocalImage(name: string, ext: string, bytes: Byte[]): string
Release()
```

## Every class scripts can reach

By namespace; `import("Name")` (or the full name where two classes share one) gives the class. The game's:

- `GoF2Remake`: AppHooksInstaller, Bootstrap, FrameGeneration, InputDeviceLog, LinuxFsr, LinuxFsr2Upscaler, LinuxFsr3Upscaler, LinuxFsr4Upscaler, LinuxFsrContext, LinuxFsrOptions, LinuxFsrUpscaler, LogFilter, MouseButtonDebounce, PlayerAssetsCheck, StreamlineDlssContext, StreamlineDlssOptions, StreamlineDlssUpscaler, UpscalerFramework, VideoMemory, Xess, XessContext, XessOptions, XessUpscaler

- `GoF2Remake.Data`: Achievements, AeiImage, AemMesh, Agent, AgentData, AgentGenerator, AgentOffer, AssemblyData, Benchmark, BlueprintPart, BlueprintState, Blueprints, Campaign, CapitalSubsystem, Cheats, ChorusLavaSpec, ChorusLayersSpec, ChorusPortalSpec, ChorusRiftSpec, ChorusShieldSpec, ContentTimings, CoopCampaign, CruiseEncounterRules, CustomAddPart, CustomDealer, CustomDeathThroes, CustomDriftField, CustomDriftFx, CustomDriftPart, CustomEngineFx, CustomEs2Engine, CustomForkEmitter, CustomFx, CustomFxEmitter, CustomFxFile, CustomFxMesh, CustomFxTrail, CustomLoot, CustomLoungeSeller, CustomOvercharge, CustomPrimary, CustomRiteFx, CustomSecondary, CustomShipData, CustomShipMaterial, CustomShips, CustomThrottleGlow, CustomTraffic, CustomWarpIn, CustomWreck, DamageTaken, Database, DerivedImages, DialoguePage, DisplayMode, Dxt, Economy, Etc1, ExperiencePresets, FlightStyles, FrameRate, Freelance, FreelanceMission, FsbBank, GalaxyMap, GameContent, GameHooks, GameNames, GameTables, GenericVoice, GlowWaveSpec, GraphQuestState, Hangar, HitFlashSpec, InterstellarSpace, ItemData, ItemIconTable, ItemStack, JavaRandom, JsonObj, KaamoClub, LangFile, LangMaps, LanguageTables, Localization, LoungeChat, MapPos, MenuRequest, MenuRow, MenuSection, MiningPlant, MissionType, ModWantedSave, MusicTracks, NetSession, NewGamePlus, NewsTicker, OriginalAgent, OriginalAudio, OriginalBlueprint, OriginalConverter, OriginalEquipment, OriginalItem, OriginalMission, OriginalPacks, OriginalParkedShip, OriginalPendingProduct, OriginalPreview, OriginalRecord, OriginalRefs, OriginalSaveConvert, OriginalSetup, OriginalShip, OriginalStation, OriginalTextures, OriginalWanted, OriginalWingmen, ParkedShip, PendingProduct, PirateBases, PirateEventRules, PirateHold, PirateReports, PirateSighting, PngWriter, PortraitBody, PortraitLayer, PortraitOffset, PoseBatch, PoseEntry, PoseLook, PoseSchedule, Pvrtc, RadioLine, SaveData, SaveGame, Session, SessionRules, Settings, ShaderVariantRules, ShipAnimations, ShipData, ShipPresets, ShipSlots, ShipStateAnimation, Shop, SpacePoints, Speaker, Speedrun, SpeedrunRoute, SpeedrunSegment, SplitTime, StatEntry, StationData, StationStock, StepSummaries, StoredShip, Story, StoryAgentData, StoryAssets, StoryContext, StoryMission, StoryStep, StoryTable, StoryType, SupernovaAssets, SupernovaChallenge, SystemData, TableJson, TickerItem, VrFlags, WantedBoard, WantedData, WantedState, WarpBands, WasteProcessing, WeaponMount, WeaponMountSet, WingmanCrew, Wingmen, WireQuant, WireReader, WireWriter, ZipSlice

- `GoF2Remake.Events`: EventAudio, EventBehaviour, EventCheat, EventCompareOp, EventCutscene, EventFade, EventFlight, EventFlightModel, EventGameValue, EventGraphData, EventGraphFile, EventGraphScript, EventHost, EventKind, EventLogicOp, EventMathOp, EventMissionTakers, EventMissions, EventMusic, EventNames, EventPickups, EventPlayersWho, EventRace, EventRespawn, EventRules, EventRunner, EventScoreBy, EventScreen, EventShipAction, EventShipOrders, EventShipThen, EventSound, EventSystemAction, EventTrigger, EventWaypoint, EventWinnerBy, EventWormhole, IPilot, LocalEvents, LocalPilot, QuestBlock, QuestField, QuestFieldKind, QuestFile, QuestGraph, QuestReader, QuestSchema, QuestStepType, QuestSteps, SpeakerSpec

- `GoF2Remake.Flight`: AutofireLatch, BindSlot, CameraFacing, ChaseCamera, ChorusDogfight, Cloak, CloakGlow, CollisionVolume, CombatAssets, CombatAudio, CombatRadar, ConductorGraph, ControlRow, ControllerGyro, Crate, CruiseDrive, CruiseEncounters, DamageType, Debuffs, Device, DrillSound, EmergencyBubble, EmpSparks, EngineVoices, Es2Aim, Es2CameraShake, Es2ChaseRig, Es2Director, Es2Dogfight, Es2NpcFlight, Explosion, FighterPool, FlightBonus, FlightModel, FlightRules, FlightStats, FlightTuning, FreeLookCamera, GameControls, Gof3Dogfight, Gun, GunEcho, GunRig, HackingGame, HapticMixer, HapticPulse, Haptics, Hazards, Headlights, Hitpoints, HudMessageKind, HudMessageQueue, HullBody, HullCollision, IRadioWorld, IncomingMissiles, KhadorTimeline, Maneuver, Mining, MiningBeamExtraction, MiningBeamFx, MiningGame, Navigation, NpcDeviceSpec, NpcDeviceState, NpcGroup, NpcRoles, NpcTables, NpcTargetRules, ObjectDocking, Obstacle, OpaqueTexture, OriginalFrame, Overheat, PlayerCloak, PlayerCollision, PlayerDebuffs, PlayerDevices, PlayerEngine, PlayerHealth, PlayerTurret, PointDefense, ProxySpec, RadarJam, Radio, RepairBeam, RiftRun, RiteLook, RiteSenses, RocketTrail, Route, ScreenFlash, SentryGun, Sfx, SfxFollow, ShieldShell, ShipBurn, ShipController, ShipExhaust, ShipSmoke, ShotTriggerChain, ShotVoices, Slow, SpaceHooksInstaller, SpawnSpec, Standing, StillLock, Target, TargetDebuffs, TargetGrid, TargetLockRules, TiltSteering, TimeExtender, TrafficLane, TrafficPlan, TrailLook, TurretAim, TurretCore, TurretTargeting, VoidEnergy, VolatileCargo, WeaponFx, WeaponSystem, WeaponTint, WingmanDevice, WreckBurn

- `GoF2Remake.Modding`: DamageEmbers, EngineAnim, EngineChannel, EngineFlame, EngineFx, EngineGlowShape, EngineMaterialGlow, EngineModelFile, EngineModels, EnginePart, EntryPatch, Es2Contrails, Es2EngineVfx, Es2Headlights, Es2Trails, ExternalDockingRules, GameAudioIndex, GameModels, ItemStats, KitEffect, KitRules, LateralThrusters, LuaBehaviour, MeshSimplifier, MobileStations, ModAssets, ModAudio, ModBackdrop, ModBlueprints, ModBuilder, ModCampaigns, ModCapitalShips, ModCatalog, ModCharacters, ModContent, ModCrates, ModDlcs, ModEncounters, ModExhausts, ModFileException, ModFlight, ModFxPart, ModGameOptions, ModGlbImages, ModGltf, ModGltfMaterials, ModHardpoints, ModInfo, ModInteriors, ModJson, ModJsonException, ModJumpFx, ModLoading, ModManager, ModManifest, ModMaterials, ModMedals, ModMusic, ModNpcShips, ModRaceVisitors, ModRaces, ModRefs, ModSaves, ModShipAnimator, ModShipBuilder, ModShipKits, ModShipSounds, ModShipView, ModShips, ModShipsWaiter, ModSounds, ModStations, ModTextures, ModTraffic, ModUnlocks, ModVoices, ModWanted, ModWarp, ModWeapons, ModWingmen, ModWorld, NpcShipType, ProjectBlueprint, ProjectCampaign, ProjectCharacter, ProjectEntry, ProjectGameOption, ProjectInterior, ProjectItem, ProjectKit, ProjectKitPart, ProjectNpcShip, ProjectQuest, ProjectRace, ProjectShip, ProjectStack, ProjectStation, ProjectSystem, ProjectWeaponEffect, RefEntry, RefKind, SaveMod, SaveModKey, SaveScriptData, ShipClassRules, ShipIconMaker

- `GoF2Remake.Multiplayer`: INetPosed, NetAdmin, NetAggression, NetArena, NetArenaClient, NetAudit, NetChat, NetCommands, NetCoop, NetCrate, NetDelayedShutdown, NetDistress, NetFactions, NetFactionsClient, NetFollow, NetGame, NetGuard, NetHandOverRule, NetHangar, NetHooksInstaller, NetInterest, NetMissions, NetModRaceStandings, NetMods, NetMotd, NetNews, NetOrbit, NetPanel, NetPing, NetPlayModeReset, NetPlayer, NetPoses, NetProfileClient, NetProxy, NetProxyStatus, NetRateLimit, NetRules, NetServerSettings, NetShotData, NetShotMirror, NetShotRegistry, NetShotSender, NetShots, NetSmoothing, NetSquad, NetState, NetStats, NetStock, NetTeleport, NetTrade, NetTradeClient, NetTransport

- `GoF2Remake.UI`: AboutText, AlienText, ArenaView, BenchmarkRunner, BindingRow, BuildVersion, CarrierShopWindow, ChallengeView, ChatView, CheatsCatalog, ChoicePopup, ChoiceRow, CombatView, CooldownView, DialogueLine, DialogueView, DiscordPresence, DockedMenu, DpadTapNavigation, DragScroll, EndingCredits, FarShipMarkers, FlightHud, FpsCounter, GameCursor, GameRulesAccess, HackingView, HangarWindow, HdWindow, HdrCalibration, HeldRepeat, HeldTrade, HudReadout, IDialogueWindow, InputGlyph, InputKind, InputMode, IssueReportUpload, ItemInfo, ItemInfoWindow, KeyboardLift, LanguageFlags, LensFlareView, LotTones, LoungePanel, MacGameControllerGamepad, MacGameControllerRunner, MainMenu, McpInput, MenuConfirm, MenuFocus, MenuKeys, MiningView, MissileWarningView, MissionsWindow, ModBrowser, ModBuilderPlay, ModBuilderProjects, ModBuilderScreen, ModBuilderSpaceView, ModBuilderView, ModLoadingView, ModelViewer, MouseWheel, MultiplayerWindow, NavigationView, OptionControl, OptionDef, OptionGroup, OptionInfo, OptionKind, OptionPage, OptionSection, OptionSectionBlock, OptionsCatalog, OptionsView, OrbitViewUi, OriginalSetupView, PadButton, PadGlyph, PadKeyboard, PadStyle, PauseMenu, PhoneKeyboard, PhotoGuides, PhotoMode, PhotoSettings, Portrait, QuestGraphView, SafeArea, SaveSlotRow, ScreenshotKey, ScrollBars, SettingsConfirm, ShipCustomizePanel, ShipPreview, SpatialNav, SpeedrunOverlay, SplashMusic, SplashTheme, SquadView, StarMap, StarMapAssets, StarMapMode, StarMapResult, StartupSplash, StationMenu, StatusWindow, StickScroll, StoryStepLabel, TerritoryView, TextFieldKeys, TextIcons, TextMarquee, TextReveal, TouchControls, TwoFingerTouch, UiBusy, UiCase, UiFocus, UiFont, UiHooks, UiHover, UiImageSlots, UiLayout, UiPointerButtons, UiScale, UiSounds, UwpAppInstaller

- `GoF2Remake.Visuals`: AnimationChannel, AnimationKey, Aspect, AssembledObject, AutoLods, AxisMap, BackdropPass, ClassicBloomPass, CloakPass, EmissiveReflection, EndingDrift, EngineDrive, EnginePreview, FrameGenerationPass, FxFog, GameLods, LodMeshes, MenuBackground, MenuCamera, MeshMeta, MeshSmoother, NeuralRenderingPass, OriginalAsmEntry, OriginalAsmPart, OriginalAsmTable, OriginalAssemblies, OriginalBackdrop, OriginalContent, OriginalMaterials, OriginalModels, OriginalResMaterial, OriginalResMesh, OriginalResTable, OriginalSkies, Part, PartAnimation, PhotoBlurPass, PhotoMotionPass, PhotoZoomPass, PictureFiltersPass, PlanetMeshes, PreTransformGuard, RadialBlurPass, ResourceInfo, ScreenAntialiasing, Spin, StationModelContext, ThrottleGlow, WarpSky

- `GoF2Remake.Vr`: VrCockpit, VrControls, VrMode, VrPad, VrPanels, VrRadar, VrRig, VrStation

- `GoF2Remake.World`: AcidSackBase, ArenaMatchInfo, ArrivalScale, Backdrop, BarFlybys, CampaignLevel, CapitalRetreat, CapitalShip, CapitalShips, CapitalSubsystems, CarryPuzzle, ChallengeOrbit, ChallengeWatch, CloakedContainers, ConductorNet, CoopCameraMirror, CoopLevelState, CruiseEncounterSpawner, CutsceneCamera, CycleSound, DebugSpawner, EncounterMusic, EncounterWatch, EnergySocket, Es2Dock, Es2DockCurves, ExternalDockTestTaker, FlightHints, FreeCamera, FreelanceOrbit, FxFlash, GasCloudField, GoF2SkyLayers, HangarFlight, HangarInspect, HangarShipShadow, HangarTraffic, HazardLooks, HazardZones, HullHitFlash, HullWarpIn, IOrbitCrate, IOrbitPeer, IOrbitProxy, ISharedOrbit, InterstellarField, IntroCutscenes, KaamoSiege, KhadorFx, LootContainer, MainCampaignLevels, MinesweeperWatch, MiningSite, MobileStation, ModBuilderGizmo, ModBuilderRange, ModDeathFx, ModDriftFx, ModJumpEffect, NebulaClouds, NpcCloak, NpcCloud, NpcDevices, NpcMountedTurrets, NpcOrder, NpcShip, NpcWormhole, OrbDispenser, OrbitAmbience, OrbitBuilder, OrbitLayout, OrbitMine, OrbitMinefield, OrbitProps, OrbitScale, OrbitStorm, PlanetApproach, PlayerCapitalMissiles, PlayerHull, Portable, PortalRefraction, PoseQuery, PursuerFx, PuzzleSounds, RebuiltPlanetOwner, RebuiltPlanets, RiftBulb, RiftEncounter, RiftField, RiftHazards, RiftMine, RiftPortal, SeekerMine, ShipCorrosion, ShipInNeed, ShipShadowSet, ShotTrigger, ShuttleRoutes, SkyLayerAssets, SkyLayers, SkyReflection, SkySphereSmoothing, SkyStars, SpaceDust, SpaceHooks, SpaceLevel, StarMapOpener, StationLevel, StationLife, StationPoseQuery, StationTables, StationView, StorySpace, SupernovaLevels, SystemApproach, SystemJump, TerranHangarBots, Traffic, TrafficLanes, ValkyrieLevels, VosskBarBots, Wormhole, WreckBreakup, WreckField

Unity's: the public classes of these namespaces (https://docs.unity3d.com/ScriptReference/), except those that
reach outside the game (Application, PlayerPrefs, the clipboard, asset bundles, web requests, the camera and microphone):

- `UnityEngine` (739 classes)
- `UnityEngine.AI` (25 classes)
- `UnityEngine.Animations` (38 classes)
- `UnityEngine.Audio` (22 classes)
- `UnityEngine.EventSystems` (40 classes)
- `UnityEngine.Events` (5 classes)
- `UnityEngine.InputSystem` (69 classes)
- `UnityEngine.InputSystem.Controls` (17 classes)
- `UnityEngine.Playables` (27 classes)
- `UnityEngine.Rendering` (489 classes)
- `UnityEngine.Rendering.Universal` (227 classes)
- `UnityEngine.SceneManagement` (10 classes)
- `UnityEngine.TextCore` (12 classes)
- `UnityEngine.TextCore.Text` (25 classes)
- `UnityEngine.UI` (61 classes)
- `UnityEngine.UIElements` (440 classes)
- `UnityEngine.VFX` (18 classes)
