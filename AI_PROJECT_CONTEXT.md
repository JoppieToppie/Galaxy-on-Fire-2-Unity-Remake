# AI project context — Galaxy on Fire 2 multiplayer expansion

> # 🚨 IMPORTANT — PLAYER-BUILT STATIONS DO NOT EXIST YET
> **Do not implement player-built stations or station-attracted NPC population now.**
> Player-built stations are a future feature and are explicitly the **LAST implementation step** for this system-population workstream. Complete and verify the mod-controlled ambient population feature first. Only return to station-attracted traffic after a real player-built-station system exists, including authoritative placement, ownership, lifecycle and persistence. Do not substitute mobile stations, faction claims or other existing station systems.
>
> **This notice is for BOTH ChatGPT and Claude.** These repository notes are shared project handover material: every AI contributor must read them before work, preserve this sequencing, and update the notes when decisions or implementation status change.


> **Audience:** Claude, ChatGPT and any human contributor joining this work.
> **Canonical repository:** `SpectreAnims/Galaxy-on-Fire-2-Unity-Remake`.
> **Coordinator / product owner:** the repository owner (the user). The user makes product decisions and approves integration.
> **Status:** living design brief. Update it when project goals or architecture decisions materially change.

## 1. The ultimate goal

The long-term goal is to turn the existing Galaxy on Fire 2 Unity Remake into a convincing, durable,
feature-rich multiplayer space game while retaining the identity, mechanics, content and stability of
Galaxy on Fire 2.

This is an expansion of the remake, not a replacement game and not a separate multiplayer prototype.
The multiplayer layer should feel like a natural extension of the existing single-player game: players
should fly the familiar ships, use familiar weapons and equipment, visit familiar stations and systems,
trade, take missions, fight, explore and progress using the game's established rules.

The intended destination is a shared and persistent universe in which players can meaningfully inhabit
the same world. They should be able to meet in space and stations, cooperate, compete, form social and
economic groups, affect the world, and build lasting things in it. Multiplayer should have enough depth
to support emergent player stories rather than being limited to a lobby, a deathmatch mode or a simple
ship-to-ship synchronisation demo.

The project should preserve the original game's atmosphere and recognizable gameplay while adding the
systems needed to make a persistent multiplayer universe coherent, fair, performant and maintainable.

### 1.1 Player-facing pillars

1. **A shared universe**
   - Players can encounter one another in the same star systems, orbits, stations and relevant activities.
   - The world should behave consistently for everyone who shares it.
   - A player's actions should have understandable consequences for other players.
   - Travel, docking, combat, mining, trading and social interactions should work together as one world.

2. **Persistent identity and progression**
   - Players retain their ships, equipment, credits, cargo, reputation, faction relationships and other
     intended progression across sessions.
   - A dedicated server or persistent host can maintain a world when an individual player leaves.
   - A reconnecting player should return to a coherent state rather than finding their progress reset.
   - Single-player saves and multiplayer progression must remain safely separated unless an explicit
     feature says otherwise.

3. **Cooperation and social play**
   - Squads and friends can travel, fight, trade and undertake activities together.
   - Co-op activities should respect the existing mission and story systems where practical.
   - Social features should support longer-term groups, not only momentary matchmaking.

4. **Player-driven conflict and competition**
   - PvP should be governed by clear world/session rules, not accidental damage or ambiguous hostility.
   - Arena matches and any permitted open-world conflict should coexist with protected areas and
     non-combat gameplay.
   - Factions, territorial conflict, sieges and defensive structures should create meaningful objectives.

5. **Player-shaped world and economy**
   - Players should be able to establish a lasting presence in the universe.
   - A major long-term direction is player-built / player-owned stations and other persistent structures.
   - Such structures should have a coherent place in the economy and world simulation: ownership,
     construction, resources, services, access, upkeep or vulnerability should be designed as connected
     systems rather than isolated UI features.
   - The exact station-construction rules, costs, limits, placement rules and persistence model remain
     product decisions to be specified before implementation.

6. **Moddability and extensibility**
   - Existing mod support should remain useful in multiplayer where technically and safely possible.
   - New systems should prefer data-driven definitions and reuse existing modding patterns.
   - Multiplayer compatibility, version/content agreement and trust boundaries must be explicit.

7. **Faithfulness and quality**
   - The original game's recognizable flight, combat, weapons, ships, stations, missions and economy remain
     the foundation.
   - Additions should be opt-in or governed by established game/session settings when appropriate.
   - The project must not trade correctness, stability or maintainability for a quick visual demonstration.

### 1.2 What success looks like

A successful end state is not merely "two clients can see each other". It is a coherent multiplayer
ecosystem where:
- players can join or host a persistent world;
- the same orbit and its contents remain coherent as players enter, leave and reconnect;
- players can form groups, trade, cooperate and compete under clear rules;
- progression and important world changes persist;
- player-owned or player-built structures can become meaningful parts of the universe;
- weapons, damage, equipment, missions, NPCs, factions and economy interact correctly across clients;
- modded content can be supported without silently desynchronising clients;
- the game remains playable as a faithful single-player remake;
- the code remains understandable, testable and compatible with the project's layered architecture.

This is an aspirational product direction, not a claim that every listed capability is already implemented.

## 2. Project identity and repository rules

- Work in **`SpectreAnims/Galaxy-on-Fire-2-Unity-Remake`**, the user's fork.
- Do not confuse it with `DuIslingr` or any other fork.
- Upstream baseline is **`JoppieToppie/Galaxy-on-Fire-2-Unity-Remake`**.
- `main` is the baseline/integration branch. Feature work belongs on focused feature branches.
- Never merge, push feature work into `main`, or change branch protection/integration policy without the user's
  explicit instruction.
- The user coordinates work between assistants and reviews the resulting changes.
- Claude and ChatGPT are independent contributors. The user manually passes work between them; do not assume
  an automated assistant-to-assistant channel exists.
- Do not overwrite, discard, rebase away or silently reinterpret another contributor's work. Inspect the
  current branch and history first; preserve existing commits and explain any conflict.
- Keep commits focused and descriptive. Prefer a reviewable branch/PR over broad unrelated changes.

## 3. Collaboration protocol

The files in this small note system have distinct jobs:

| File | Purpose | Update rule |
|---|---|---|
| `AI_PROJECT_CONTEXT.md` | Shared product vision, principles, architecture constraints and durable decisions | Update only when durable context changes |
| `AI_TASK_LOG.md` | Chronological record of investigations and task outcomes | Add an entry for each meaningful task |
| `AI_CHANGELOG.md` | Concise, user-facing record of actual repository changes | Record only changes that were really made |
| `AI_HANDOVER.md` | Current baton-pass: active branch, task, findings, risks and next actions | Replace/update at each handover |

At the start of a task:
1. Read this file and `AI_HANDOVER.md`.
2. Read the latest relevant entries in `AI_TASK_LOG.md` and `AI_CHANGELOG.md`.
3. Check the current branch, working tree / remote head, recent commits and exact diff.
4. Read `CLAUDE.md`, `Assets/Scripts/Runtime/README.md` and relevant `docs/notes/*.md`.
5. Identify the intended behaviour and acceptance criteria before editing.
6. Ask the user only when a product decision is genuinely missing; do not invent requirements.

At the end of a task:
1. Record what changed, why, and what was deliberately not changed.
2. Record tests actually run and their actual results. Distinguish not run, passed, failed and blocked.
3. Update `AI_HANDOVER.md` with the next concrete actions and review questions.
4. State the exact branch and commit(s). Never imply a push, build, test or merge happened unless confirmed.

### 3.1 Required task record

For non-trivial work, record:
- Task ID / date / contributor / branch.
- User's requested outcome.
- Relevant existing implementation and files inspected.
- Design decision and rationale.
- Files changed and behavioural impact.
- Compatibility implications (single player, multiplayer, mods, saves, platforms).
- Tests added and tests actually run.
- Known limitations, risks and unresolved questions.
- Handover / reviewer checklist.

### 3.2 Status vocabulary

Use these words precisely:
- **Proposed:** design suggested; no code change.
- **Investigated:** relevant code and behaviour inspected.
- **Implemented:** source changes made.
- **Committed:** a commit exists on a named branch.
- **Pushed:** the remote branch was verified to contain the commit.
- **Compiled:** Unity/C# compilation completed successfully in the stated environment.
- **Tested:** named tests or manual scenarios were run and results recorded.
- **Reviewed:** another contributor inspected the exact commit/diff.
- **Integrated:** user-approved merge/cherry-pick into the chosen integration branch.

Never promote a task to a later status based on intention or assumption.

## 4. Engineering principles

1. **Investigate before editing.** Follow the actual call path and data ownership; do not infer architecture from a filename or a partial snippet.
2. **Preserve the remake.** Existing single-player behaviour is a compatibility requirement unless the user explicitly approves a change.
3. **Multiplayer correctness is end-to-end.** A local visual effect is not complete if authoritative simulation, replication, remote presentation or reconnect/handover omits it.
4. **One source of truth.** Store each setting on the entity that owns its meaning (for example, weapon behaviour on the item; physical muzzle orientation on the mount).
5. **Data-driven where appropriate.** Prefer existing JSON/mod schemas and established parsers/normalizers over special cases.
6. **Keep layers clean.** Follow the assembly dependency direction and hooks described in `Assets/Scripts/Runtime/README.md`.
7. **Test plain logic separately.** Keep game rules in testable plain C# where practical; add edit-mode tests for deterministic behaviour.
8. **Avoid unrelated refactors.** A feature patch should change only what is needed, with separate commits for independent cleanup.
9. **Compatibility before convenience.** Consider old data, mod content, save formats, network protocol/versioning, and default values.
10. **Document decisions, not noise.** Put area-specific implementation notes in `docs/notes/`; use this AI note system for cross-assistant coordination.

## 5. Existing architecture and constraints

The project already has substantial multiplayer functionality. Treat the current code and notes as the source
of truth; do not assume the game is a blank slate.

### 5.1 Assembly layers

The runtime is layered, lowest to highest:

```
GoF2.Core  <-  GoF2.Game  <-  GoF2.Space  <-  GoF2.Net  <-  GoF2.App
```

- **Core:** pure rules and utilities.
- **Game:** game state, rules, database, saves, settings and mod loading.
- **Space:** flight, combat, NPCs, stations, world objects and visual/gameplay adapters.
- **Net:** multiplayer and event graph networking.
- **App:** UI, bootstrap and top-level wiring.

A lower layer must not directly reference a higher layer. Use the owning layer's hook class and have the
higher layer's installer supply the implementation. See `Assets/Scripts/Runtime/README.md`.

Only the Net and App layers should reference Netcode directly, as documented in the runtime README.

### 5.2 Multiplayer model already present

Existing code/documentation describes:
- Relay / Lobby connectivity and dedicated servers;
- connection approval, version fingerprints and session rules;
- player identity and replicated ship state;
- shared orbits and an orbit-authority model;
- NPC proxies, crates, ownership handover and orbit persistence;
- replicated shots and damage paths;
- squads, trades, arena matches, factions, claims, sieges and server administration;
- co-op campaign support and multiplayer event graphs.

These systems are non-trivial and interconnected. Before adding a networked feature, read at least:
- `docs/notes/multiplayer.md`
- `docs/notes/multiplayer-sync.md`
- the more specific multiplayer note for the feature area.

### 5.3 Relevant combat / ship / mod notes

For weapons, projectiles and launch behaviour:
- `docs/notes/weapons.md`
- `docs/notes/npcs-and-combat.md`
- `docs/notes/multiplayer-sync.md`

For modded ships, hardpoints, mount transforms and weapon models:
- `docs/notes/mods-ship-kits.md`
- `docs/notes/mods.md`
- `Modding/README.md`

For the current secondary homing-delay / launch-direction work, see the active handover in
`AI_HANDOVER.md`.

## 6. Design ownership and feature boundaries

The following ownership rule is important across the project:

- **Item/weapon definition:** damage, projectile speed, lifetime/range, reload, ammo, homing behaviour,
  homing delay and other intrinsic weapon characteristics.
- **Ship / hardpoint / mount definition:** where a weapon is mounted, its muzzle position, physical mount
  orientation and any mount-specific launch transform.
- **Ship/player state:** which item is equipped in which slot, current ammo, cooldown and owner.
- **World/session authority:** which simulation instance is authoritative for a target, hit, damage,
  destruction, persistence and ownership handover.
- **Presentation:** model rotation, muzzle flash, trails and remote visual effects must represent the same
  physical shot, but presentation alone must not become the source of gameplay truth.

Do not put a property on an item merely because it is convenient to read from `Gun`. Put it on the
definition/entity that actually owns the property.

### 6.1 Mod-gated features (owner's standing rule)

New systems for multiplayer / modding must have **no effect, or the most minimal one, on the existing single-player and
co-op game unless a mod opts in to them**. Every new field is optional and absent = today's behaviour; the original game's
data stays untouched; plain-logic tests should prove an unclassed ship / unrestricted item behaves exactly as before.

### 6.2 Ship classes, class-restricted equipment and class scanners (owner's decisions, 2026-11-15; implemented on `feature/extensive-multiplayer-expansion`, not compiled)

- **Mod-only taxonomy:** the 14 names below are a recommended shared vocabulary for mod authors, **not vanilla/base-game
  ship classifications**. Do not assign any of them to original ships or ship data by default. In an unmodded game, ships have
  no gameplay class and class-based equipment restrictions / scanner readouts remain dormant.
  - Fighters: `light_fighter` (Light Fighter), `heavy_fighter` (Heavy Fighter).
  - Bombers: `light_bomber` (Light Bomber), `heavy_bomber` (Heavy Bomber), `stealth_bomber` (Stealth Bomber).
  - Specialist combat: `interceptor` (Interceptor).
  - Civilian / utility: `hauler` (Hauler).
  - Multi-purpose: `light_privateer` (Light Privateer), `privateer` (Privateer), `heavy_privateer` (Heavy Privateer).
  - Capital ships: `frigate` (Frigate), `freighter` (Freighter), `cruiser` (Cruiser), `carrier` (Carrier).
- **Extensibility:** this is a suggested shared vocabulary, not a closed enum or mandatory list. Mods may assign any of these
  classes to their own ships and may define additional classes. Store stable machine IDs separately from display labels; do not
  use translated/display text as a save or network identifier. A ship with no mod-assigned class remains unclassified.
- **Unclassified/default compatibility:** do not add a visible or persisted vanilla `default` ship class. Treat a missing class
  as an implicit unrestricted compatibility state: every item is allowed on an unclassified ship, including items that declare
  `allowedClasses`. This preserves all existing vanilla loadouts and means class restrictions only engage when a mod explicitly
  assigns a class to a ship. If an implementation needs an internal sentinel, it must not be exposed as a real mod class or
  written into original ship data.
- **Privateer identity:** all three Privateer classes describe multi-purpose ships, not merely a weapon category. Their design
  balances (1) a decently sized cargo hold relative to the ship's overall size, (2) a reasonably balanced mix/count of primary
  and secondary weapon mounts, and (3) handling that sits between a dedicated combat craft and a less agile cargo-focused hull.
  Light / standard / heavy Privateers are scale variants of this shared philosophy. Exact numeric thresholds are deliberately
  unspecified until the existing ship roster and modded hull ranges have been surveyed; do not invent arbitrary cut-offs.
- **Other class identity:** the names establish the intended broad roles, but precise stat/loadout thresholds for Light vs Heavy
  fighters and bombers, Interceptors, Stealth Bombers, Haulers and capital classes still need a roster-based pass. Do not
  infer a ship's class from one stat alone or hard-code guessed thresholds.
- **Classification evidence in the current data:** `Assets/Resources/GoF2Data/ships.json` already provides per-ship
  cargo, primary / secondary / turret slot counts, armor and handling values, plus descriptions. It does not expose a direct
  physical hull-volume field in the base `ShipData` schema. For cargo-relative-to-size judgements (especially Privateers),
  survey those stats alongside actual model / kit scale and ship descriptions; do not treat raw cargo capacity as size-adjusted
  without that context.
- **Free-form tags and per-item lists (owner's decision, 2026-11-15, after the first allow-list-only note):** a ship has an
  optional `shipClass` (a stable machine id) and optional `classTags` (free words, e.g. `small`, shareable across mods). An item
  has an optional `allowedClasses` and an optional `excludedClasses` that match a ship's class id **or** any of its tags. A ship
  with neither class nor tags is **unclassified** and takes every item (see above), so the lists only engage on classified ships.
  On a classified ship: no lists = fits; an exclude-list blocks what it names; an allow-list refuses what it doesn't name; with
  both, the ship must match the allow-list and not the exclude-list. Case-insensitive. (This supersedes the earlier "no exclude-list"
  line: the owner asked for tags with allow and exclude lists so a mod can exclude e.g. every `small` ship of any mod.)
- **Class id vs display text:** `shipClass` is the id used for matching, saves and the network; `className` (a word, a per-language
  object, or `shipclass.<id>.name` in `text/<lang>.json`) is only displayed; without it the id is shown capitalised.
- **Ship swap:** equipment that does not fit the new ship's class goes to the **hold** (like a built-in drive does now); the
  swap is not blocked. `Hangar.CanMount` is the existing mount gate (Khador Drive, cloak bay) and the natural place for the
  refusal; also audit buy-and-mount, saves / start loadouts, the Kaamo club, cheats and kit ships.
- **Scanner:** **one scanner attribute**, `scanClass` (attr **143**, next to Joppie's `scanShip` 140 / `scanHitpoints` 141 /
  `scanWeapons` 142), reveals a locked ship's explicitly mod-assigned class **as soon as the lock completes**, with no extra scan
  step; an unclassified ship has no class to reveal. This is a tactical identification tool: large ships, especially cruisers, need
  to identify specialised anti-capital threats quickly so they can prioritise targets. Only mod scanners carry the capability. The
  class shows on **other players' ships** as well as NPCs (another player's ship comes from the replicated ship index,
  `Target.netShipIndex`). Joppie's `scanShip` names the hull, not the class: see `AI_HANDOVER.md` for how the two relate.
- **Multiplayer:** the client's game runs the economy and the server only checks uploads (`NetProfiles`), so the class rule is
  enforced where the server has a say: `NetProfiles.ClampClasses` moves equipment a ship's class can't mount to the hold on every
  upload and sign-in (like `ClampMods`). The client enforces it in the hangar (`Hangar.CanMount`, `SwitchTo`, `FitToSlots`).
  The class of a remote ship is derived from the ship index every client already knows. The same stable class id is used by mod
  definitions, saves and network state.

### 6.4 Moddable turret hardpoint scaling and targeting profiles (owner's decisions, 2026-11-15; Proposed)

- **Hardpoint-owned scale:** modders should be able to scale each turret hardpoint's mounted turret independently, directly
  through the hardpoint definition. This is not ship-kit hull scaling and not the existing turret weapon item's
  `fx.turret.scale` (which controls that item's turret model). Prefer a clearly named optional per-mount field such as
  `turretScale`; absent means the current size. Do not implement this as a ship-wide scale or require editing ship-kit
  overall scale. Verify the hardpoint data format and all turret construction/presentation paths before choosing the final
  JSON spelling and transform application.
- **Targeting profile belongs to turret weapon/item configuration**, independently of weapon damage and projectile type.
  The three agreed profiles are:
  - `ships`: conventional ship targeting.
  - `missiles`: incoming missile interception only.
  - `missiles_priority`: prioritise incoming missiles; engage ships when no eligible missile target is available.
- **Explicitly excluded:** no `balanced` profile.
- Keep the design extensible to additional target categories / priority rules later, without requiring them in the first
  implementation. A missile-focused turret may still have optional light anti-ship damage when its profile permits ship
  targeting; targeting eligibility must not dictate damage values.
- Reuse and extend the existing missile point-defence logic where possible rather than creating a competing interceptor
  system. Audit target acquisition, claim/duplicate interception handling, projectile lead, range, authority and multiplayer
  replication before implementation.
- **Status:** design decisions only. No hardpoint schema, turret item schema, Mod Builder UI, targeting logic or gameplay
  source has been changed for this feature.

### 6.3 Secondary hardpoint launch orientation (owner's decisions, 2026-11-15; implemented as `launchRotation`, not compiled)

- Launch-direction adjustment is for **secondary weapons only**. Primary guns, turrets, NPC/capital guns and unrelated mount
  types retain their existing firing behaviour.
- The launch direction belongs to the hardpoint/mount, not the weapon item. The same secondary item can therefore launch
  differently from two mounts without changing its own speed, damage, homing delay or other intrinsic stats.
- It should be moddable per mount. Prefer a new optional mount field such as `launchRotation` (Euler or quaternion, matching
  the established mount-rotation representation) rather than silently changing the meaning of the existing `rotation` field.
  Existing `rotation` controls the mounted model/muzzle presentation; preserving that meaning avoids breaking mods that
  visually cant a weapon without intending to redirect its projectile.
- If `launchRotation` is absent, preserve today's straight-ahead secondary launch behaviour. Do not infer launch direction
  from visual `rotation` unless a later compatibility decision explicitly authorises that fallback.
- Keep `homingDelayMs` as an item-level weapon property. Apply the mount launch transform to the supported secondary
  projectile paths only, and explicitly document any intentional exclusions (e.g. beams/mines) after auditing them.
- Mod Builder support for the mount-level launch setting is desirable, but the underlying data format and runtime behaviour
  take priority; do not block a correct moddable implementation on UI tooling.
- Multiplayer must use the authoritative shot's resulting velocity/direction so remote clients reproduce the same launch.
  Verify the actual replication path and test it; do not assume local correctness proves network correctness.
- **Implemented (Claude, 2026-11-15):** `WeaponMount.launchRotation` (Euler or quaternion, separate from `rotation`) ->
  `WeaponMount.LaunchTurn` -> `Gun.launchTurn`, set by `WeaponSystem.Setup` for secondaries only and applied on the plain, salvo and
  cluster paths; mines, beams, primaries, turrets and NPC guns are untouched; no field = straight ahead. The Mod Builder
  project copy and JSON export carry the field (its editor UI is not added). Multiplayer was audited: the shot data carries the
  resulting velocity and the homing delay, so remote clients reproduce the flight (not tested over a network).

### 6.5 Class-based station docking and freighter boarding (owner's decisions, 2026-11-15; Proposed)

- **Two station docking modes:** interior docking (the existing transition into the station interior) and external docking
  (the ship remains physically present alongside the station in the space scene). Every station supports both modes;
  station support is universal and does not need a per-station opt-in.
- **Class-controlled mode:** docking mode is determined by the ship's explicitly assigned mod class / class configuration.
  Fighters and Interceptors are specifically intended to use **interior docking only**. Other class-to-mode assignments
  have not yet been exhaustively specified; do not assume every Frigate-or-larger class is automatically external-only
  until the user confirms the complete class mapping.
- **Unclassified ships:** a ship with no explicitly assigned class may use its normal interior docking, but is completely
  ineligible for external station docking. Do not give unclassified ships external docking as a fallback. This preserves
  vanilla behaviour and makes external docking an explicitly granted modded capability.
- **External docking behaviour:** the ship remains in the space scene, docked alongside the station and visible to other
  server members, following the existing moving-station docking model. The docking player's camera does not enter the
  station. Entering the Space Lounge is a separate, explicit interior transition; it must not remove the docked ship from
  the shared space scene. The ship remains docked until undocking.
- **Docking points and visuals:** the ship should eventually define a functional docking attachment point on its hull
  (typically on a side), which aligns with a station-side docking arm / attachment point. The station provides the
  complementary docking location. For the first implementation, use functional transforms / positioning only; no visible
  arm, clamps, collar, docking animation or other visual schema is required yet. A classified ship configured for external
  docking must have a valid hull docking point; if it is missing, deny external docking rather than inventing a fallback.
- **Hackable freighters are different:** freighters do not support this external docking mode. External station docking
  must not be repurposed as freighter boarding. Boarding / hacking remains a separate close-range interaction and is
  limited to ships **smaller than a Frigate**; ships the size of a Frigate or larger cannot hack-board freighters. Remote
  hacking is not part of the agreed initial design.
- **Compatibility and authority:** preserve ordinary interior docking for unclassified vanilla ships. Enforce class and
  size eligibility authoritatively in multiplayer, and ensure docked ship state / position remains coherent for all clients.
- **Status:** product design only. No docking code, class schema, station/freighter interaction, docking-point data, camera
  behaviour or multiplayer replication has been implemented or tested.

### 6.6 Mod-controlled system NPC population (owner's decisions, 2026-11-15; Proposed)

> **🚨 IMPLEMENTATION ORDER — PLAYER-BUILT STATIONS DO NOT EXIST YET.** Station-attracted NPC population is the **LAST implementation step** in this feature area. Do not implement it during the initial system-population work. First finish and verify mod-controlled ambient population suppression. Only revisit station attraction after a real player-built-station system and its authoritative placement, ownership, lifecycle and persistence exist. Do not treat mobile stations or faction station claims as player-built stations. This sequencing is mandatory for ChatGPT and Claude.


- **Explicit opt-in:** system population control is an engine capability for modders, not a new default game rule. It must have no effect on an unmodified game or on any existing system unless a mod explicitly configures that system. Existing story content, scripted encounters and vanilla system populations must remain unchanged by default.
- **Per-system control:** modders should be able to configure NPC presence independently for each system. The primary intended use is to make selected systems empty of ambient NPC traffic / presence by default, particularly for multiplayer-focused mods and newly added systems. This is not a requirement to redesign the NPC simulation or population economy in depth.
- **Optional player-station response:** a mod may optionally allow a player-built station in a low- or no-population system to attract a small amount of ambient NPC presence. This should be light, infrequent and varied rather than restoring a busy vanilla population or creating a constant stream of traffic. Station presence is a possible trigger for limited ambient activity, not an automatic requirement for every system or every station.
- **Compatibility boundary:** absent system configuration means today's behaviour. Do not modify original system data or silently apply a global population override. Keep story-/mission-scripted NPCs distinct from ambient population control so the feature cannot inadvertently suppress required story actors or encounters.
- **Multiplayer:** the server / world authority should own population decisions and spawn/despawn state so clients see a consistent system. Persistence, reconnects, player-station ownership/lifecycle and mod-content compatibility need to be considered when the implementation is designed.
- **Still to specify during implementation:** exact mod data location/schema, whether population is a simple enabled/disabled setting or supports a small set of levels, what ambient NPC categories can return around player-built stations, spawn caps/cadence and persistence rules. Do not invent a complex simulation or hard-code arbitrary population rates before inspecting the existing system/NPC spawn pipeline.
- **Status:** product direction recorded; design only. No system data, NPC spawning logic, multiplayer state or tests have been changed.

For NPC population control and the existing ambient traffic pipeline, see `docs/notes/system-population.md`.

## 7. Non-goals and guardrails

- Do not replace the remake's single-player game with a multiplayer-only fork.
- Do not redesign existing gameplay wholesale when a narrow extension will work.
- Do not add a parallel data schema when an established schema can express the requirement cleanly.
- Do not claim network safety because a local host test looked correct.
- Do not claim a feature works in Unity until it has compiled and the relevant tests/scenarios have run.
- Do not merge work into `main` without the user's explicit approval.
- Do not treat speculative future features in this document as already implemented.

## 8. Current project focus

See `AI_HANDOVER.md` for the exact active task and branch. The current work concerns the secondary weapon
launch behaviour and a correction to the ownership of launch orientation: item-specific homing delay remains
an item property; initial launch orientation should be defined by the corresponding hardpoint/mount, not
duplicated as per-item pitch/yaw attributes.
