# AI project context — Galaxy on Fire 2 multiplayer expansion

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

### 6.2 Ship classes, class-restricted equipment and class scanners (owner's decisions, 2026-11-15; Proposed, not implemented)

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
- **Restriction is an allow-list per item** (`allowedClasses`); no exclude-list. An item without the list fits every ship. A
  restricted item is still accepted by an unclassified ship; its allow-list is checked only when the ship has an explicit mod class.
- **Ship swap:** equipment that does not fit the new ship's class goes to the **hold** (like a built-in drive does now); the
  swap is not blocked. `Hangar.CanMount` is the existing mount gate (Khador Drive, cloak bay) and the natural place for the
  refusal; also audit buy-and-mount, saves / start loadouts, the Kaamo club, cheats and kit ships.
- **Scanner:** **one scanner attribute** that reveals a locked ship's explicitly mod-assigned class **immediately on lock**; an
  unclassified ship has no class to reveal. No progress bar or scan
  delay. This is a tactical identification tool: large ships, especially cruisers, need to identify specialised anti-capital
  threats quickly so they can prioritise targets. Only mod scanners carry the capability. The class shows on **other players'
  ships** as well as NPCs.
- **Multiplayer:** the class restriction is enforced by the server / authority, not trusted from the client; the class of a
  remote ship must be replicated or derivable from the ship every client already knows. The same stable class ID must be used
  consistently by save data, mod definitions and network state.

### 6.3 Secondary hardpoint launch orientation (owner's decisions, 2026-11-15)

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
