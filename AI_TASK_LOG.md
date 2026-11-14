## 2026-11-15 — Implement per-ship free-look orbit distance and target locking in free look

- **Contributor:** ChatGPT
- **Branch:** `feature/extensive-multiplayer-expansion`
- **User request:** Per-ship moddable default orbit-camera distance and the ability to lock targets while in free look.
- **Inspected:** Current branch handover and task log; `ModShipView`, `ShipData`, `ChaseCamera`, `FreeLookCamera`, `CombatRadar`, `SpaceLevelWormholes`, modding docs and existing edit-mode tests.
- **Findings:** Normal chase-camera offsets were already moddable per ship through `ships.json` `camera.distance`, `height`, `lookHeight`, and `lookAhead`. The free-look orbit's separate default radius was hard-coded to 3800 game units (zoom range 1500..20000).
- **Design:** Add `camera.orbitDistance`, default 3800, clamped to the existing free-look zoom range. Keep docked orbit presets and normal chase-camera offsets unchanged. During free look, target acquisition uses the active camera viewport centre; standard flight continues using the ship's forward aim point. Existing scanner, lock timing, priority, sticky-lock and homing behaviour remain intact.
- **Files:** `Database.cs`, `ModShipView.cs`, `FreeLookCamera.cs`, `CombatRadar.cs`, `SpaceLevelWormholes.cs`, `ModContentTests.cs`, `TargetLockRulesTests.cs`, both modding guides, and `docs/notes/flight-model.md`.
- **Tests added:** Mod content default / override / clamp / validation coverage; free-look crosshair viewport-centre coverage.
- **Status:** Source and documentation changes committed on the feature branch. Unity compile, EditMode tests and live multiplayer verification not run.

---

## 2026-11-15 — Assess feasibility and narrow the core system-population implementation

- **Contributor:** ChatGPT
- **Branch:** `feature/extensive-multiplayer-expansion`
- **User request:** Touch up shared notes and assess how feasible/easy it is to remove natural traffic from selected systems.
- **Inspected:** shared project context, handover, task log, changelog and `docs/notes/system-population.md` on the active feature branch.
- **Conclusion:** feasible, low-to-moderate complexity. The existing per-system mod parsing path can host one optional boolean; the runtime change is concentrated in ordinary `TrafficPlan.Build` population and matching ambient revival paths.
- **Key implementation risk:** reliable ambient-vs-scripted classification. Do not use broad race/group filters or blanket `NpcShip.Revive` suppression.
- **Scope clarified:** core ambient suppression only. Cruise encounters, pirate events, mission/story actors, special populations, siege garrisons and decorative lanes remain separate.
- **Station sequencing:** player-built stations and station-attracted NPC traffic remain explicitly deferred to the final implementation phase, after a real player-built-station lifecycle exists.
- **Status:** investigated/documented only. No runtime source, JSON schema, system data, tests or build changed/run.

---

## 2026-11-15 — Clarify player-built station implementation is last

- **Contributor:** ChatGPT
- **Branch:** `feature/extensive-multiplayer-expansion`
- **Decision:** Player-built stations do not exist yet. Player-built-station construction and station-attracted NPC population are explicitly deferred to the **last implementation step** of this system-population workstream. Complete and verify mod-controlled ambient population suppression first. Revisit station attraction only after a real player-built-station system has authoritative placement, ownership, lifecycle and persistence. Mobile stations and faction claims are not substitutes.
- **Collaboration:** These notes are shared handover material for both ChatGPT and Claude. Both contributors must read and maintain them and preserve this implementation order.
- **Documentation:** Added prominent notices to `docs/notes/system-population.md`, `AI_PROJECT_CONTEXT.md` and `AI_HANDOVER.md`.
- **Status:** documentation / sequencing clarification only; no runtime code or schema changed.

## 2026-11-15 — Extend system population audit to encounters and stations

- **Contributor:** ChatGPT
- **Branch:** `feature/extensive-multiplayer-expansion`
- **Additional paths inspected:** `CruiseEncounters`, `CruiseEncounterRules`, `CruiseEncounterSpawner`, `EncounterWatch`, `RiftEncounter`, `TrafficPlanPirateEvents`, `TrafficPlanSpecials`, `FreelanceOrbit`, `TrafficLanes`, `MobileStation`, `MobileStations`, `StationLife`, `HangarTraffic`, `NetOrbit`, and faction territory / siege / garrison code.
- **Findings:** Cruise encounters are player-triggered and separately gated; pirate events and system-special actors are event/authored content; freelance mission actors/sites are mission-owned; faction siege garrisons/defenders are separately event-tagged; traffic lanes are decorative models rather than NPCs. None should be silently removed by ambient suppression. The branch has no player-built-station placement/ownership/destruction lifecycle: mobile stations are mod-authored and clock-driven, faction claims refer to existing stations, and station worker drones are visual only.
- **Design refinement:** First implementation should suppress only ordinary ambient `TrafficPlan` population and its corresponding revival paths, with explicit ambient metadata/predicate. Broad `NpcGroup` or race filtering is unsafe because groups are reused by events, missions and siege actors. Station attraction is blocked on a genuine player-station lifecycle/ownership model being available.
- **Documentation:** Expanded `docs/notes/system-population.md` section 9 with the extended source classification, station findings and refined implementation boundary.
- **Status:** investigation/documentation only; no runtime code, schema, station system or tests changed. No Unity build/tests run.

## 2026-11-15 — Inspect the existing system NPC population pipeline

- **Contributor:** ChatGPT
- **Branch:** `feature/extensive-multiplayer-expansion`
- **Inspected:** `SpaceLevel`, `Traffic`, `TrafficPlan`, `TrafficPlanNpcShips`, `TrafficSpecials`, `ModWorld` parsing/merging/lookup, `NetOrbit`, `NetState` / orbit handover, and the NPC / multiplayer notes and modding guide.
- **Findings:** Normal free-flight population is planned by `TrafficPlan.Build` and instantiated by `Traffic`. Existing `systems.json` `traffic: "mod"` selects mod-only traffic; it is not an empty-system switch. `locals` / `raiders` use null/empty fallback semantics, so empty arrays are not a safe suppression mechanism. Ambient respawns are separate: jumpers every 10 s, locals and eligible raider waves every 45 s, plus a separate Void attacker revival path. Story/campaign and mission/event actors use additional spawn paths and must not be globally suppressed.
- **Multiplayer:** One orbit authority builds and runs traffic; NPCs are proxied to other players and adopted on authority handover. Station-attracted spawn scheduling/state must therefore be authoritative and handover-safe.
- **Documentation:** Added `docs/notes/system-population.md` with pipeline map, existing mod controls, respawn paths, story/mission boundaries, multiplayer implications and an implementation checklist.
- **Status:** investigation and documentation only. No runtime source, mod schema, system data or tests changed; no Unity build/tests run.
- **Next:** Audit all remaining ambient/event spawn paths and inspect player-built-station lifecycle before proposing the smallest schema and exact spawn policy.

## 2026-11-15 — Define opt-in per-system NPC population control

- **Contributor:** ChatGPT
- **Branch:** `feature/extensive-multiplayer-expansion`
- **User decision:** System population control is an engine capability used explicitly by mods, not a new vanilla default. Each system can be configured independently; selected systems can have ambient NPC presence removed by default, especially in multiplayer mods and newly added systems. Existing systems and the base story remain unchanged unless a mod opts in.
- **Optional station response:** A player-built station may optionally attract a small amount of ambient NPC activity. Any return should be light, infrequent and varied rather than restoring a busy population.
- **Compatibility boundary:** Keep story-/mission-scripted NPCs separate from ambient population control so required story actors and encounters are not suppressed.
- **Open implementation details:** Mod data schema, population levels vs simple suppression, eligible NPC categories, spawn cadence/caps and persistence remain to be researched against the existing spawn pipeline.
- **Status:** design/documentation only. No source, system data, NPC spawning, multiplayer state or tests changed; no Unity build/tests run.

## 2026-11-15 — Rename active feature branch to reflect the wider expansion

- **Contributor:** ChatGPT
- **Previous branch:** `feature/secondary-homing-delay-launch-angle`
- **New branch:** `feature/extensive-multiplayer-expansion`
- **Reason:** The branch now carries broader multiplayer expansion design and documentation, not only secondary weapon launch work.
- **Status:** New branch will point to the same latest commit; no source history is rewritten.

## 2026-11-15 — Define class-based station exterior docking and freighter boarding

- **Contributor:** ChatGPT
- **Branch:** `feature/secondary-homing-delay-launch-angle`
- **User decisions:** Every station supports interior and external docking. Docking mode is class-controlled; Fighters and
  Interceptors are interior-only. Unclassified ships retain normal interior docking but are completely barred from external
  station docking. The full mode mapping for other classes remains open.
- **External docking:** the ship remains alongside the station in the shared space scene and visible to server members,
  following moving-station docking behaviour. The docking camera stays in space; Space Lounge is a separate explicit
  interior transition. Eventual attachment uses a ship hull docking point and station-side arm/point, but initial behaviour
  needs no visible docking hardware or animation. A missing ship docking point denies external docking.
- **Freighters:** no external docking. Close-range hack-boarding is a separate interaction available only to ships smaller
  than a Frigate; Frigate-sized and larger ships cannot board/hack them. Remote hacking is not in the initial design.
- **Status:** design/documentation only. No docking code, class schema, docking-point data, camera changes, multiplayer
  replication or tests were implemented or run.

## 2026-11-15 — Turret hardpoint scaling and point-defence targeting decisions

- **Contributor:** ChatGPT
- **Branch:** `feature/secondary-homing-delay-launch-angle`
- **User decisions:** Turret size should be independently adjustable per turret hardpoint, not via ship-kit hull scale.
  Point-defence targeting uses three profiles: `ships`, `missiles`, and `missiles_priority`; explicitly no `balanced`.
- **Design distinction:** Existing `fx.turret.scale` is on turret weapon item models, not Ship Kits' turret hardpoint definitions.
  The requested addition is a hardpoint-owned scale (candidate JSON name `turretScale`), with absent field preserving
  existing size. Final field name and transform semantics require code-path inspection.
- **Targeting:** profiles belong to turret item configuration and are independent of damage. Missile-priority turrets may
  engage ships when no missile target is available; their anti-ship damage can remain low. Reuse existing missile point-defence
  logic, with multiplayer authority and duplicate-interception behaviour audited.
- **Status:** design/documentation only. No gameplay source, schemas, Mod Builder controls or tests changed.
  No Unity build/tests run.

## 2026-11-15 — Clarify ship classes are mod-only, with unrestricted vanilla fallback

- **Contributor:** ChatGPT
- **Branch:** `feature/secondary-homing-delay-launch-angle`
- **User decision:** The 14 named classes are a mod-author vocabulary, not default classifications for original ships.
  Vanilla ships remain unclassified; the classification feature is dormant unless a mod assigns a class.
- **Compatibility rule:** A missing/unclassified ship class behaves as unrestricted: it accepts all equipment, even items
  that declare `allowedClasses`. Class allow-lists are enforced only for ships with an explicit mod-assigned class.
  Do not persist or expose a vanilla `default` class; an internal sentinel, if needed, is not a real class.
- **Scanner:** capable scanners instantly reveal an explicitly assigned class; unclassified targets have no class readout.
- **Status:** documentation/design only. No vanilla ship data, mod data, scanner, equipment validation or runtime source changed.
  No Unity build/tests were run.

## 2026-11-15 — Define standard ship-class taxonomy and Privateer identity

- **Contributor:** ChatGPT
- **Branch:** `feature/secondary-homing-delay-launch-angle`
- **User's decision:** Standard classes are Light Fighter, Heavy Fighter, Light Bomber, Heavy Bomber, Interceptor,
  Stealth Bomber, Hauler, Light Privateer, Privateer, Heavy Privateer, Frigate, Freighter, Cruiser and Carrier.
- **Privateer design:** all Privateers are multi-purpose vessels with a decently sized cargo hold relative to hull size,
  a balanced amount of primary and secondary weaponry, and handling that balances combat capability with utility.
  Light / standard / heavy variants express different scales of this philosophy.
- **Design clarification:** the 14 are the project's standard taxonomy, not a closed list; mods may add classes. Stable
  machine IDs are recommended separately from display labels. Exact numerical class thresholds remain open and should
  be based on surveying the actual ship roster rather than arbitrary guesses.
- **Repository inspection:** the base `Assets/Resources/GoF2Data/ships.json` contains cargo capacity, primary / secondary / turret
  slot counts, armor, handling and descriptive text. The base `ShipData` schema has no direct hull-volume field, so relative
  cargo judgements need model / kit scale and qualitative review as well as the numeric stats.
- **Status:** design recorded only. No ship data, scanner, equipment validation or runtime source was changed or tested.

# AI task log

Chronological record of substantial work by ChatGPT, Claude or other contributors.
Newest entries go at the top. Link commits/PRs where available.

---


## 2026-11-15 — Secondary launch direction moved from the item to the hardpoint

- **Contributor:** Claude
- **Branch:** `feature/secondary-homing-delay-launch-angle`
- **User's outcome:** `homingDelayMs` an item property; a secondary's initial launch direction a property of its hardpoint,
  for secondaries only (not primaries, turrets, exhausts).
- **Inspected:** `Gun.TryFire` (plain / salvo / cluster paths), `Gun` item ctor, `WeaponSystem.Setup` (slot -> mount mapping
  `slot % mounts.Count`, the same mount used for the muzzle offset), `WeaponMount`, `ModHardpoints.MountTurn` / `MuzzleOffset`,
  `NetShotData` / `NetShotSender` / `NetShotMirror`, `weapons_hd.json`.
- **Findings:** mount `rotation` already exists (Euler or quaternion, Unity ship frame, the frame `MuzzleOffset` uses), so no
  conversion is needed. None of the original game's 135 secondary mounts has `rotation` or `upsideDown`, so defaults are
  unchanged. `MountTurn` adds an `upsideDown` half turn for the model; reusing it for velocity would send a mod's hung secondary
  backwards, so the launch uses the explicit rotation only. Shot data carries velocity and homing delay on the wire, and the
  mirror injects by velocity, so remote clients follow without a protocol change.
- **Changed:** removed item attrs `launchPitchDeg` / `launchYawDeg` (173 / 174) from `ItemStats`, `Gun` and the docs; added
  `WeaponMount.RotationTurn` (`MountTurn` now builds on it, same result); `WeaponSystem.Setup` sets `gun.launchTurn` for
  secondaries from their mount; `Gun.TryFire` applies it on the plain, salvo and cluster paths (mines and beams untouched).
  `GunLaunchTests` rewritten around the mount. Docs: `Modding/README.md`, the AI modding guide, `docs/notes/weapons.md`.
- **Not changed:** primaries, turrets, NPC / capital guns, the network protocol, saves. Default behaviour is identical.
- **Tests:** written, **not run**; Unity could not be opened in this environment. **Not compiled.**
- **Open:** the mount rotation is applied only on mounts the player's `WeaponSystem` builds; other players' ships are
  mirrored from the shot's velocity. Hand-check in Unity: a mod ship with a secondary mount `rotation` of `[-90, 0, 0]`.

---

## 2026-11-15 — Establish shared AI collaboration notes

- **Contributor:** ChatGPT
- **Branch:** `feature/secondary-homing-delay-launch-angle`
- **Task:** Establish a durable shared project brief and handover system for Claude and ChatGPT.
- **Status:** Documentation authored and committed to this feature branch; Unity build/tests not applicable.
- **Work performed:**
  - Recorded the long-term goal: a faithful GOF2 remake expanded into a coherent persistent multiplayer universe.
  - Documented player-facing pillars, architecture constraints, multiplayer correctness requirements and workflow.
  - Established four shared files: project context, task log, changelog and current handover.
  - Added a pointer from `CLAUDE.md` so a Claude Code session can discover the collaboration notes.
- **Important boundary:** This documentation task does not implement the secondary weapon fix. The existing
  branch's source implementation remains as it was when this note was written.
- **Review requested:** Claude should read the context and handover, check the design against the actual code,
  and call out any inaccurate architecture assumptions or missing multiplayer implications.

## Earlier work before this log existed

- The user established that ChatGPT and Claude should work as separate contributors, with the user manually
  passing work between them. No automated assistant-to-assistant communication is expected.
- The user identified `SpectreAnims/Galaxy-on-Fire-2-Unity-Remake` as their fork. Do not use a different fork.
- Claude created the branch `feature/secondary-homing-delay-launch-angle`.
- A review found the branch's initial launch pitch/yaw were read from item attributes, despite the intended
  behaviour being hardpoint-specific. The launch angle tests also encoded the item-level approach.
- Existing ship/kit mount documentation describes a per-mount `rotation` (Euler or quaternion) that turns the
  weapon model and muzzle. It currently documents shots as continuing along the ship's nose. This existing
  rotation is a strong candidate for the source of launch orientation, but the implementation path must be
  inspected before deciding how to use it for projectile velocity.
- No source correction to that branch has been made by this documentation task.


## 2026-11-15 — Product decisions: instant class scan and moddable secondary launch orientation

- **Contributor:** ChatGPT (design clarification)
- **Branch:** `feature/secondary-homing-delay-launch-angle`
- **User decisions:** Ship-class scanning is instant on target lock, not progressive. This is a tactical requirement so large ships
  (especially cruisers) can quickly identify specialised anti-capital threats and prioritise them. Launch-direction adjustment
  applies to secondary weapons only; there is no reason to alter primary firing behaviour.
- **Design recommendation recorded:** Keep launch direction mount-owned and moddable. Prefer a new optional mount field such as
  `launchRotation`, separate from existing visual `rotation`, to avoid breaking mods that use visual rotation without intending
  to redirect projectiles. Absent `launchRotation` means the existing straight-ahead behaviour. Keep `homingDelayMs` item-owned.
- **Status:** Product direction agreed; this entry records design only. The existing branch implementation currently uses
  `WeaponMount.rotation` for launch direction and therefore still needs review/correction against the separate-field recommendation.
  Scanner/classification remains unimplemented. No Unity compile or tests were run as part of this documentation update.
