# System NPC population: existing pipeline and design notes

**Status:** investigated / design notes only (2026-11-15). No runtime code or data schema changed.

## Scope of this investigation

The requested feature is an explicitly mod-enabled, per-system control over ambient NPC presence. A mod should be able to make a system quiet or empty without changing unmodified gameplay. A player-built station may optionally attract a small, varied amount of ambient traffic later.

The code paths below describe the current implementation on `feature/extensive-multiplayer-expansion`. They are not a claim that every story/event spawn path has been exhaustively audited.

## 1. Main free-flight pipeline

The normal orbit pipeline is:

1. `SpaceLevel` creates the orbit's `Traffic` component.
2. It determines whether this client is allowed to build/run ambient orbit traffic. In multiplayer, `NetState.OrbitEmpty` / `SpaceHooks.IsOrbitEmpty` determines the initial `NetAuthority`.
3. `Traffic.Setup(...)` resolves the current `SystemData`, station and orbit context.
4. For a normal non-story orbit, `Traffic.Setup` calls `TrafficPlan.Build(db, station, system, playerGame, wormholeGame)`.
5. The returned `SpawnSpec` list is queued and instantiated by `Traffic.Create` (one per frame when the arrival path requests staggered creation, otherwise during setup).
6. `ShipsPlaced` connects the NPCs' target lists and sets up orbit-specific follow-on behaviour.
7. `Traffic.Update` calls `UpdateOrbit`, `UpdateAlienAttackers`, pirate-event and other orbit behaviours every frame.

Key files:
- `Assets/Scripts/Runtime/Space/World/SpaceLevel.cs`
- `Assets/Scripts/Runtime/Space/World/Traffic.cs`
- `Assets/Scripts/Runtime/Space/TrafficPlan.cs`
- `Assets/Scripts/Runtime/Space/TrafficPlanNpcShips.cs`
- `Assets/Scripts/Runtime/Space/World/TrafficSpecials.cs`

## 2. What the initial traffic plan contains

`TrafficPlan.Build` is not just a count of local patrol fighters. Depending on the system, station, campaign state, player rank, security, difficulty and active mods, it can plan:

- Local system-race patrol fighters.
- Jumpers that later relaunch from the station.
- Freighters and special freighter replacements.
- Raiders, including pirate / hostile-race groups and occasional hostile capital ships.
- Pirate-base escorts and outpost guards.
- Special capital ships, carriers and fleet battles.
- Mod-defined NPC ship types from `npcships.json`.
- Late-campaign Specters and special Void activity.
- Special-case populations for pirate hideouts, loot orbits, the alien/Void orbit and other reserved systems.

The plan has many deliberate story and special-system branches. For example, it has special handling for Mido during the tutorial, the supernova system before its story transition, the Void invasion, pirate bases, loot orbits, and systems whose original traffic is intentionally empty or restricted.

This means a population override must be applied at a clearly defined point and must not casually erase these existing rules for vanilla systems.

## 3. Existing mod controls are adjacent, but not sufficient

The system-level mod schema is parsed in:
- `Assets/Scripts/Runtime/Modding/ModWorld.cs`
- `Assets/Scripts/Runtime/Modding/ModWorldMerging.cs`
- `Assets/Scripts/Runtime/Modding/ModWorldNumbering.cs`

Current `systems.json` fields include:

- `traffic: "mod"`: only mod ships fly there; it excludes ordinary game fighters/freighters/capital ships and GoF2 pirate events, while allowing mod traffic and the system's configured raiders.
- `traffic: "all"`: removes that mod-only override.
- `locals`: chooses the races of local patrols.
- `raiders`: chooses the races that raid the system.
- `pirateHideout`: intentionally replaces normal traffic with the hideout's substantial pirate population.
- Station-level `stations.json` orbit settings can override `locals` and `raiders` for an individual orbit.

Important distinction: `traffic: "mod"` is a ship-source/content selection, not a population suppression setting. It does not mean "no NPCs"; modded ships can still appear, and raider/population rules still run. `locals: []` / `raiders: []` are not currently a safe or supported empty-population mechanism: parsing only stores non-empty resolved lists, and null falls back to existing defaults.

The parser's recognised `SystemFields` set will need an explicit addition if a new system-population field is introduced. Validation and merge semantics belong in the same ModWorld pipeline rather than being read ad hoc from JSON during spawning.

## 4. Respawn and later ambient population

Setting the initial plan to empty would not by itself guarantee an empty system.

In `TrafficSpecials.UpdateOrbit`:
- Every 10 seconds, a gone jumper can be revived at the station.
- Every 45 seconds, gone local fighters (except the Most Wanted target) can be revived at the station.
- Raider groups can return in limited waves near the player, subject to security, race and wave limits.

In `TrafficSpecials.UpdateAlienAttackers`:
- Void attackers can be revived on a separate 45-second cadence (10 seconds for a particular campaign state), around the player or wormhole.

These revival paths operate on ships already in `Traffic.Ships`; they are independent of the initial plan. A genuine ambient-population suppression policy must govern both initial creation and ambient respawn/revival. It should not blindly suppress every call to `NpcShip.Revive`, because some are part of story, mission or event behaviour.

Other dynamic paths (pirate events, cruise-drive encounters, freelance missions, campaign levels and explicitly scripted spawns) need to be classified as ambient versus authored/scripted before deciding which should obey the system setting.

## 5. Story and mission separation

A story orbit is passed into `Traffic.Setup` as `storyOrbit`; the normal `TrafficPlan.Build` path is skipped there. Campaign-level code creates its own ships through `Traffic.SpawnShip(SpawnSpec)`.

Freelance missions, mission escorts, event ships, wanted targets, convoy actors and other authored encounters also have their own ownership/flags and spawn paths. Some normal-orbit mission/event actors may be added after the initial traffic plan.

Design implication:
- The new control should target **ambient population**, not act as a blanket "no NPCs" switch on the entire orbit.
- Story/campaign actors and required mission/event actors should remain unless a specific mod-authored rule explicitly says otherwise.
- The existing `SpawnSpec` / `NpcShip` metadata (e.g. event tags, mission/story flags and group) should be inspected before implementing a robust ambient-vs-scripted classification. Do not assume `NpcGroup` alone is a complete discriminator.

## 6. Multiplayer ownership and replication

The current multiplayer model is already built around one authority per orbit:

- Only the first player in an empty orbit initially builds its ambient traffic.
- That authority creates the NPCs and exposes them to other players as `NetProxy` objects; clients do not independently build a second population.
- The server selects the orbit runner. If the runner leaves, the server hands the orbit's NPC proxies to the next runner.
- The new runner adopts the existing NPCs from their replicated specs/status rather than resetting the orbit.

Relevant files:
- `Assets/Scripts/Runtime/Net/Multiplayer/NetOrbit.cs`
- `Assets/Scripts/Runtime/Net/Multiplayer/NetState.cs`
- `Assets/Scripts/Runtime/Net/Multiplayer/NetStateOrbits.cs`
- `docs/notes/multiplayer.md`
- `docs/notes/multiplayer-sync.md`

Design implication: evaluate the population policy on the authoritative orbit simulation, and ensure any station-triggered spawns are created there and replicated through the existing proxy lifecycle. Do not let every client independently roll station traffic. If station attraction has a timer, budget or persistent state, decide how that state survives authority handover and player departures; simply handing over the currently alive ships does not automatically hand over an unspawned schedule or counter.

## 7. Likely narrow design shape (not a final schema)

A small optional system-level setting in `systems.json` appears to fit the existing mod architecture. It should be absent by default and resolved through `ModWorld`.

The runtime concept should be separate from the existing `traffic: "mod"` field:

- A system's ambient population can be left at the game's current behaviour (no new field / default).
- A mod can explicitly suppress ambient NPC population for that system.
- A future optional player-station rule can enable a limited ambient response around qualifying player-built stations.

Do not settle on a JSON property name, numeric population levels, spawn counts, spawn intervals or NPC category lists until the implementation has traced every ambient spawn source and the player-station lifecycle. The user's stated preference is deliberately light/infrequent/diverse station traffic, not a new deep population simulation.

## 8. Recommended implementation investigation / acceptance checklist

1. Trace every ambient spawn source beyond `TrafficPlan.Build`: `TrafficSpecials`, `TrafficPirateEvents`, cruise encounters, station/orbit scripts, and player-station creation/ownership.
2. Identify a reliable ambient-vs-scripted classification on `SpawnSpec` / `NpcShip`; preserve mission, story and event actors by default.
3. Define and validate an optional system-level mod setting in `ModWorld`; absent setting must preserve current behaviour exactly.
4. Apply suppression to initial ambient planning and ambient respawn/revival, not to all NPC creation globally.
5. Define the player-built-station trigger and lifecycle: when it becomes active, when it stops attracting traffic, how destroyed/packed/unowned stations behave, and whether traffic is tied to the station or the system.
6. Keep station-attracted traffic sparse, capped, infrequent and selected from a varied eligible pool; avoid a continuous spawn loop.
7. Make the orbit authority own rolls, timers and spawn decisions; carry any necessary scheduler state through authority handover or make it safely reconstructible.
8. Add plain-logic tests for absent setting, explicit suppression, station response disabled/enabled, caps/cadence and scripted-spawn exemptions.
9. Add multiplayer tests for a single authoritative roll, proxy visibility and authority handover without duplicate or reset spawns.
10. Confirm unmodified single-player, co-op story and existing modded systems retain their current population.

## Files inspected

- `Assets/Scripts/Runtime/Space/World/SpaceLevel.cs`
- `Assets/Scripts/Runtime/Space/World/Traffic.cs`
- `Assets/Scripts/Runtime/Space/World/TrafficSpecials.cs`
- `Assets/Scripts/Runtime/Space/TrafficPlan.cs`
- `Assets/Scripts/Runtime/Space/TrafficPlanNpcShips.cs`
- `Assets/Scripts/Runtime/Modding/ModWorld.cs`
- `Assets/Scripts/Runtime/Modding/ModWorldMerging.cs`
- `Assets/Scripts/Runtime/Modding/ModWorldNumbering.cs`
- `Assets/Scripts/Runtime/Net/Multiplayer/NetOrbit.cs`
- `Assets/Scripts/Runtime/Net/Multiplayer/NetState.cs`
- `Assets/Scripts/Runtime/Net/Multiplayer/NetStateOrbits.cs`
- `Modding/README.md`
- `docs/notes/npcs-and-combat.md`
- `docs/notes/multiplayer.md`
- `docs/notes/multiplayer-sync.md`
- `Reference/research/npc_traffic_ai.md`


## 9. Extended audit: dynamic encounters and station-related systems (2026-11-15)

### Dynamic cruise encounters

The cruise-drive encounter system is a separate, player-triggered / travel-time encounter pipeline:

- `Core/CruiseEncounters.cs` rolls the encounter table while the player is cruising (after 6 seconds, a check every 5 seconds, 10% chance, 90-second cooldown, maximum two per orbit visit).
- Built-in entries include pirate ambushes, trade convoys, distress calls and flybys; mods can add or override entries through `encounters.json` (`ModEncounters`).
- `Game/CruiseEncounterRules.cs` excludes special/story-sensitive systems, the Void/supernova systems, pirate hideouts and `traffic: "mod"` systems.
- `SpaceLevel` only enables the orbit encounter roll when this player owns the orbit simulation; passive clients and scripted/special orbit modes do not independently roll encounters.
- `World/CruiseEncounterSpawner.cs` creates the ships through `Traffic.SpawnShip`. Some kinds create groups and follow-on waves (e.g. battle/high-risk/challenge); a rift delegates to `RiftEncounter`, which replenishes enemies as the stage runs.

**Classification:** these are not the ambient background population. They are discrete encounters initiated by the player's cruise activity (or a visible signal the player chooses to approach). The population switch should not silently disable them as a side effect. If modders later want empty systems to prohibit even these encounters, that should be a separate explicit encounter policy or an explicit opt-in on the encounter table, not an accidental consequence of ambient suppression.

### Pirate events and special traffic

- `TrafficPlanPirateEvents.cs` adds pirate outposts, guards, turrets, bosses and escorts as a reported/unreported pirate activity event on orbit entry. Event ships carry `pirateEvent` / `eventTag` metadata; `TrafficSpecials.UpdateOrbit` explicitly skips automatic respawn for nonzero `eventTag`.
- `TrafficPlanSpecials.cs` includes authored special populations such as pirate bases, capital/fleet battles and special orbit actors. The main `TrafficPlan.Build` also has dedicated story/system branches (including tutorial/supernova/Void/loot/pirate-hideout cases).
- These are authored system content or discrete events, not generic local traffic. A system population override should not remove them by default. A mod that wants to suppress one of these should use its existing event/system configuration or a future separate explicit switch.

### Freelance missions and mining sites

`FreelanceOrbit` is mission-owned content. It builds mission enemies/friends, routes, escorts, junk and the ore-mining site for the active freelance mission. In a multiplayer squad, a follower builds the site but does not duplicate the runner's mission ships; mission proxies are promoted/adopted separately.

**Classification:** mission actors and mission sites are not ambient population. They should remain available when ambient traffic is disabled.

### Player-built stations: important current-state finding

The audited branch does **not** currently expose a runtime player-built-station placement / ownership / destruction lifecycle that can be used as a population trigger.

There are related but different systems:

- `MobileStations` / `MobileStation` spawn **mod-authored mobile stations** from `stations.json`. Their location is derived deterministically from a UTC clock, route and stay duration. They move through configured station orbits and support docking. They are not constructed or owned by a player.
- `StationLife` creates visual worker drones on a mod station model's configured loops. These are visual effects, not NPC traffic or a player station.
- Multiplayer factions can claim existing station indices as territory. A claim changes station services / trade / faction ownership state; it does not construct a new station.
- Faction garrisons and siege defenders are spawned by `NetOrbit.UpdateGarrison` only while a station siege is armed. They carry dedicated event tags, are server-counted, respawn on siege-specific timers and leave when the siege ends. They are combat/event actors, not ambient population.
- `HangarTraffic` is station-interior hangar activity and is separate from ships flying in the system orbit.

Therefore the station-attraction idea is currently a **future integration point**, not something that can be wired into an existing player-station event. Before implementing it, the project needs the player-built station feature (or a clearly defined representation of a player-owned station) and authoritative lifecycle events: placed/activated, ownership changed, packed/moved, destroyed and restored after reconnect/server restart.

Do not use faction station claims or mod-authored mobile stations as a substitute for player-built station ownership. They have different semantics.

### Decorative / non-NPC traffic lanes

`TrafficLanes` reads `stations.json` orbit lane definitions and creates simple moving ship models along spline paths. They are not `NpcShip` actors, have no AI, no radar marker and no player targeting. A destroyed lane model is restored after its own short `TrafficLane.RespawnMs` timer.

**Classification:** this is decorative orbit traffic, not the ambient NPC population. Whether an empty-system setting should also hide these lane models is a separate visual-density choice. The user's original request concerns NPC presence; safest default is not to change lane visuals unless explicitly configured.

### Additional classification matrix

| Source | What it creates | Ambient suppression by default? |
|---|---|---|
| `TrafficPlan` ordinary locals, jumpers, freighters, raiders | Background NPC ships | **Yes** |
| `TrafficSpecials.UpdateOrbit` revival of those ambient groups | Returning background ships | **Yes** |
| `TrafficPlanPirateEvents` | Pirate activity events, guards, outposts, bosses | No; event content |
| `TrafficPlanSpecials` system-specific special actors | Authored special populations | No; preserve existing system logic |
| `CruiseEncounterSpawner` / `RiftEncounter` | Player-triggered cruise encounters and waves | No; separate encounter policy |
| `FreelanceOrbit` | Mission actors and mining sites | No; mission content |
| `NetOrbit.UpdateGarrison` | Siege garrisons and defenders | No; siege content |
| `TrafficLanes` | Decorative moving models | No; separate visual setting |
| `HangarTraffic` | Interior parked / landing / departing ships | No; unrelated to system orbit |

### Refined implementation boundary

The narrowest safe first implementation is to suppress only the ordinary ambient population planned by `TrafficPlan` and its matching ambient revival logic. The ambient category needs to be identified explicitly on each planned `SpawnSpec` (or by a carefully defined predicate); relying only on `NpcGroup` is insufficient because groups are reused by missions, events, garrisons and other systems.

The revival path needs the same classification. In particular, current code uses group checks for local / raider revivals and a separate Void revival loop. A blanket group or race filter would risk suppressing mission/event actors. The existing `eventTag` check already protects many event ships, but it is not a complete ambient classification.

For the later station response, first implement/identify the player-station lifecycle. Once it exists, the station can request a small ambient spawn budget from the orbit authority; it should not directly create ships on every client or reuse mission/event spawners.
