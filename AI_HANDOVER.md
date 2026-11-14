# AI handover — current baton pass

> # 🚨 IMPORTANT — PLAYER-BUILT STATIONS DO NOT EXIST YET
> **Do not implement player-built stations or station-attracted NPC population now.**
> Player-built stations are a future feature and are explicitly the **LAST implementation step** for this system-population workstream. Complete and verify the mod-controlled ambient population feature first. Only return to station-attracted traffic after a real player-built-station system exists, including authoritative placement, ownership, lifecycle and persistence. Do not substitute mobile stations, faction claims or other existing station systems.
>
> **This notice is for BOTH ChatGPT and Claude.** These repository notes are shared project handover material: every AI contributor must read them before work, preserve this sequencing, and update the notes when decisions or implementation status change.


> **Read this first when continuing the project.**
> Last updated: 2026-11-15 (per-ship free-look orbit distance and free-look target locking implemented)
> Prepared by: ChatGPT, then updated by Claude after the corrections below
> Intended recipients: ChatGPT and Claude (shared AI handover), plus the user

## 1. Repository and branch

- **Repository:** `SpectreAnims/Galaxy-on-Fire-2-Unity-Remake`
- **Active branch:** `feature/extensive-multiplayer-expansion`
- **Base:** `main`
- **Branch state:** feature branch only; re-check the live branch head and comparison with `main` before further edits.
- **Integration instruction:** Do not merge or push this feature branch to `main`. The user wants Claude to review
  the changes before deciding what happens next.
- **Coordinator:** the user. They manually pass work between Claude and ChatGPT.

The four AI notes files and the `CLAUDE.md` discovery pointer are maintained on the active feature branch so
Claude can read the same project brief and handover. This does not authorize integration into `main`.

## 2. Product goal

The ultimate goal is a faithful Galaxy on Fire 2 Unity Remake expanded into a coherent, persistent
multiplayer universe. This is not a multiplayer-only rewrite. Preserve the existing single-player game,
its flight/combat/economy/content and modding architecture while making multiplayer deeper and more
durable: shared orbits, persistent player progression, squads/co-op, trade, factions, meaningful conflict,
and eventually player-owned/player-built stations and other lasting world structures.

Read `AI_PROJECT_CONTEXT.md` for the complete vision, player-facing pillars, architecture constraints,
ownership rules and collaboration process. It distinguishes long-term goals from already implemented features.

## 3. Previously active technical task: secondary weapon launch behaviour

### User's intended design

- Homing delay is an intrinsic property of a weapon item.
- Initial launch direction/orientation is a property of the hardpoint/mount that fires it.
- The same missile item equipped on two different hardpoints must be able to leave those mounts in
  different directions without changing the item's speed, damage, homing delay or other weapon stats.
- The initial direction must be based on the actual mount transform/orientation, not per-item pitch/yaw.
- The change must preserve existing behaviour for mounts with no custom orientation.
- Consider the full projectile lifecycle and all affected projectile types, not just the most obvious path.

### Current branch findings (investigated, not yet fixed)

The Claude branch is `feature/secondary-homing-delay-launch-angle`. Its one feature commit is ahead of
`main`. Before this documentation update, the compare showed these six changed paths:
- `Assets/Scripts/Runtime/Modding/ItemStats.cs`
- `Assets/Scripts/Runtime/Space/Gun.cs`
- `Assets/Scripts/Tests/EditMode/Flight/GunLaunchTests.cs`
- `Assets/Scripts/Tests/EditMode/Flight/GunLaunchTests.cs.meta`
- `Modding/README.md`
- `Modding/ai/gof2-modding/guide.md`

Review identified:
- `ItemStats.cs` adds item attributes `launchPitchDeg` and `launchYawDeg` (alongside `homingDelayMs`).
- `Gun` reads pitch/yaw from the weapon item and applies a launch turn.
- `GunLaunchTests` validates the item-level pitch/yaw design, so those tests must be replaced rather than
  merely retained.
- `WeaponSystem.Setup` knows which mount is being used and passes the mount position into `Gun`, but
  the reviewed setup path does not pass the mount's launch orientation.
- The modding guide already documents `rotation` on a primary/secondary mount. It rotates the mounted
  weapon model and muzzle in the ship model's frame, but current documentation says shots still fly along
  the ship nose. The correct implementation should investigate and reuse that established data rather
  than add duplicate item-level attributes or invent an unrelated schema.

**Important:** This is a review finding, not proof that every relevant firing path has been traced. Re-open
the current branch files and inspect the full call graph before editing.

### Desired correction

1. Remove the item-level `launchPitchDeg` and `launchYawDeg` attributes from the active implementation,
   tests and user-facing modding documentation.
2. Preserve `homingDelayMs` as item-level configuration, subject to a separate correctness review.
3. Use the corresponding hardpoint/mount orientation as the launch-direction source of truth. First inspect
   the real `WeaponMount`, `ModHardpoints`, `ModShipKits`, mount-building and muzzle-offset code. Existing
   mod-kit notes identify these concepts in `docs/notes/mods-ship-kits.md`.
4. Ensure the weapon slot-to-mount mapping is stable and matches the mount used for muzzle placement.
5. Apply the launch transform consistently to each supported projectile path. Audit standard projectiles,
   missiles, cluster/salvo, mines, bombs and special weapons; document deliberate exclusions.
6. Keep visual model rotation, muzzle position and projectile velocity consistent. Do not accidentally apply
   the mount rotation twice if the weapon model/muzzle is already transformed into world space.
7. Preserve default straight-ahead behaviour when a mount has no custom rotation.
8. Audit multiplayer shot mirroring/replication. Determine whether the authoritative shot velocity/direction
   is replicated or reconstructed remotely; ensure remote clients see the same trajectory and launch origin.
9. Replace item-level tests with tests proving two hardpoints using the same item can launch in distinct
   directions, plus default-orientation and projectile-path coverage.
10. Update `docs/notes/weapons.md`, `docs/notes/mods-ship-kits.md`, `Modding/README.md` and the AI handover
    where warranted. Keep the docs aligned with actual behaviour.
11. Run the relevant edit-mode tests and compile in Unity if an Editor/build environment is available. If not,
    state clearly that compilation/testing remains outstanding.

### Important design distinction

A mount's documented `rotation` currently turns its weapon model and muzzle. The desired feature extends
that physical hardpoint orientation to the projectile's initial launch vector. It must not simply use the
weapon model's local forward without checking coordinate spaces, parent transforms, muzzle transforms,
ship orientation, and any existing aim/targeting logic.

### Current status (updated by Claude)

- Item-level launch angle: **removed** (attrs 173 / 174, tests, docs). **Implemented, committed; not compiled.**
- Hardpoint-based launch: **Implemented**: `WeaponMount.RotationTurn` -> `Gun.launchTurn`, set in `WeaponSystem.Setup` for
  **secondaries only** (owner's instruction), applied on the plain, salvo and cluster paths. Mines and beams are deliberately
  untouched; primaries, turrets and NPC / capital guns are untouched. Mounts without `rotation` fly straight (all 135 original
  secondary mounts). `upsideDown` is deliberately **not** part of the launch turn (`MountTurn` adds it for the model only).
- Homing delay (`homingDelayMs`, attr 172): item property, read in the `Gun` item ctor, clamped 0..60000 ms (the wire field is a ushort).
- Multiplayer: **audited.** `NetShotData` carries velocity and homing delay, and `NetShotMirror` injects by velocity, so
  remote clients see the same flight without a protocol change. Not tested over a network.
- Unity compile / `GunLaunchTests`: **not run** (no Editor here). Hand-check suggestion: a mod ship whose secondary mount has
  `"rotation": [-90, 0, 0]` (up) and another with `[0, 90, 0]` (right), same missile.
- Corrections to the earlier handover: the `rotation` field exists on `WeaponMount` (`Database.cs`) and is in the Unity ship
  frame, the one `ModHardpoints.MuzzleOffset` uses, so no frame conversion was needed.

## 4. Current implementation task: per-ship free-look orbit distance and free-look target locking

- **User request:** Add moddable per-ship control over the free-look orbit camera's default distance, and allow target locking while in free look.
- **Existing behaviour:** The normal chase camera already has per-ship `ships.json` `camera.distance` / height / look offsets. The separate free-look orbit used a universal 3800-unit start radius and 1500..20000 zoom range.
- **Implementation:** Add `camera.orbitDistance` (default 3800, clamped to 1500..20000); apply it when the player's ship is created and when swapped in flight. In free look, `CombatRadar` uses the camera viewport centre as the lock acquisition point; normal flight retains the ship-forward aim point.
- **Compatibility:** No change to chase-camera distance, docked orbit preset, scanner requirement, lock timing, target priority, sticky lock, weapon homing or multiplayer shot protocol.
- **Tests added:** Mod content tests for default, override, clamp and invalid values; target-lock test for free-look viewport-centre selection.
- **Verification:** Source changes committed on this branch. Unity compilation, EditMode tests and live multiplayer verification remain outstanding.

## 3c. Turret hardpoint scaling and point-defence targeting

The user wants independent scaling on each turret hardpoint, configured at the hardpoint itself—not ship-kit hull scale and
not the existing turret weapon item's `fx.turret.scale`. An optional per-mount field such as `turretScale` is a candidate;
missing value preserves current size. Inspect hardpoint JSON parsing and every turret build / hangar / remote presentation path
before finalising the field and transform semantics.

Point-defence targeting is configured on turret items, independently of damage:
- `ships`: conventional ship targeting.
- `missiles`: incoming missile interception only.
- `missiles_priority`: missiles first, ships when no eligible missile target is available.
There is **no** `balanced` profile. Preserve optional light anti-ship damage for profiles that permit ship targeting.
Extend the existing point-defence system; audit authority, missile claims, lead, range and multiplayer replication.

Design only; no implementation or tests yet.

## 3b. Ship classes and class scanners

The user has defined 14 recommended mod-author class names in `AI_PROJECT_CONTEXT.md` §6.2: Light Fighter, Heavy Fighter,
Light Bomber, Heavy Bomber, Interceptor, Stealth Bomber, Hauler, Light Privateer, Privateer, Heavy Privateer, Frigate,
Freighter, Cruiser and Carrier. **These are not base-game classifications.** Original ships remain unclassified; mods
explicitly assign classes to their own ships and may add further class IDs. All Privateer variants are multi-purpose ships
with substantial cargo capacity relative to hull size, a balanced primary/secondary weapon fit, and balanced handling.
Precise numerical class thresholds remain a mod-author design matter; no vanilla roster assignment is planned.

Unclassified ships use an implicit unrestricted compatibility state: they accept all equipment, including items with
`allowedClasses`. Class allow-lists are checked only for ships explicitly assigned a mod class. Do not create a persisted
or user-visible vanilla `default` class. Capable scanners instantly reveal an explicitly assigned target class; unclassified
targets have no class readout. This remains a **design proposal only**; no scanner/classification source implementation
has been made on this branch.

## 3d. Class-based station exterior docking and freighter boarding

The user has now defined the following docking design (2026-11-15). This is **design only**; no implementation or tests:

- Every station supports both existing **interior docking** and **external docking**. This is not a per-station opt-in.
- Docking mode is class-controlled. **Fighters and Interceptors are explicitly interior-only.** The full mapping of every
  other standard/custom class to interior vs external has not yet been decided; do not assume all Frigate-and-larger
  classes are automatically external-only without confirmation.
- An **unclassified** ship retains ordinary interior docking but is completely unable to use external station docking.
  External docking is an explicitly granted capability, never a fallback for missing class data.
- External docking keeps the ship physically present alongside the station in the space scene and visible to other server
  members, following the moving-station docking model. The docking player's camera stays in space; it does not enter the
  station. Selecting Space Lounge is a separate explicit interior transition and must not make the docked ship disappear.
- The eventual physical arrangement uses a docking attachment point on the ship hull (typically on its side) and a
  complementary station-side arm / attachment point. Initial implementation needs only functional transforms and docking
  positioning; no visible docking hardware or animation is required yet. A ship configured for external docking must have
  a valid hull docking point; if absent, deny external docking rather than using an arbitrary fallback.
- Hackable freighters **do not** support external docking. Freighter boarding/hacking is a separate close-range interaction
  and is limited to ships **smaller than a Frigate**. A Frigate-sized ship or anything larger cannot hack-board a freighter.
  Remote hacking is not part of the initial design.
- Preserve normal interior docking for unclassified vanilla ships. Multiplayer authority and replicated docked-ship state
  must be considered from the start.

Implementation/research checklist:
1. Trace existing station docking, moving-station docking, Space Lounge transitions and camera ownership.
2. Trace hackable-freighter docking/hacking and establish the authoritative ship-size/class gate.
3. Define the mod class docking-mode field and the ship-kit hull docking-point representation; preserve absent-class defaults.
4. Implement station external docking as a distinct mode while keeping the ship replicated in the shared space scene.
5. Add tests for unclassified denial, interior-only classes, external-capable classes, missing docking points, and freighter
   boarding size boundary; test multi-client visibility and Space Lounge camera transitions in Unity.

## 3f. Existing NPC population pipeline investigation

See `docs/notes/system-population.md` for the code-path notes. Key finding: the existing `systems.json` `traffic: "mod"` option changes the source of traffic, not its quantity; it does not suppress mod NPCs. Initial planning is in `TrafficPlan.Build`, while `TrafficSpecials` independently revives jumpers, locals, raiders and Void attackers. Story and mission spawns have separate paths, and multiplayer population creation belongs to the orbit authority. The audit is now sufficiently complete for the narrow core feature: ordinary ambient population is planned through `TrafficPlan.Build`, with matching revival paths in `TrafficSpecials`. The implementation should use explicit ambient-origin classification and a single optional per-system mod setting. Player-built-station work is explicitly deferred to the final phase.

## 3e. Mod-controlled system NPC population

The user has specified a deliberately lightweight, **explicitly modded** system-population feature (2026-11-15). This is **design only**:

- Population control exists as an engine capability for modders; it is not enabled globally and must not affect the unmodified base game.
- Each system can be configured independently. The main use is to remove ambient NPC presence from selected systems by default, especially multiplayer-mod systems and new systems. Existing systems remain vanilla unless a mod explicitly opts them in.
- An optional future rule may let a player-built station attract a small amount of NPC activity into an otherwise empty / quiet system. It should be sparse, infrequent and diverse, not a return to normal busy-system population.
- Preserve story-/mission-scripted NPCs and encounters; distinguish them from ambient population so population suppression cannot accidentally break the base story.
- Population/spawn decisions should be authoritative and consistent across multiplayer clients.

The core feature is now scoped to one optional per-system enabled/disabled setting: absent configuration preserves current behaviour; explicit configuration suppresses ordinary ambient traffic. No density levels are needed for this first version. Exact JSON field name remains an implementation decision to be made against parser conventions.

**Feasibility:** low-to-moderate. The system mod parser, initial `TrafficPlan.Build` path and timed ambient revival paths are known. The main care point is an explicit ambient-vs-scripted origin marker; broad race/group filters are unsafe. Keep cruise encounters, pirate events, missions, sieges, authored special populations and decorative traffic separate.

No source, system data, mod schema, NPC spawning, networking or tests have been changed. See `docs/notes/system-population.md` §10 for the detailed assessment.

## 4. Claude review request

Please review these notes and the current branch. In particular:
- Confirm the overall project vision is represented accurately.
- Check the hardpoint rotation design against the actual code and data flow.
- Identify every launch/firing path and any network mirror/replication implications.
- Correct this handover if any technical statement is stale or incomplete.
- Do not assume the source correction has already been made.
- Do not merge or push to `main`; the user explicitly wants a review stage first.

## 5. Working rules for the next contributor

- Read `AI_PROJECT_CONTEXT.md` and the relevant `docs/notes/*.md` before code changes.
- Inspect the exact branch head and full diff before editing.
- Do not discard Claude's original homing-delay work while correcting launch-angle ownership.
- Keep the user informed of design choices and unresolved product decisions.
- Update `AI_TASK_LOG.md`, `AI_CHANGELOG.md` and this handover after meaningful work.
- Report exact branch and commit SHA; do not claim compile/test/push status without verification.

## 6. Next actions — core system ambient suppression

1. Re-read the current branch head and exact diff before editing; preserve any concurrent Claude changes.
2. Inspect `SpawnSpec` construction and `NpcShip` metadata to settle a reliable ambient-origin classification.
3. Add one optional per-system boolean through `ModWorld` parsing/merging/validation; absent configuration must preserve existing behaviour.
4. Gate ordinary ambient planning and its matching revival paths only. Do not blanket-filter NPC creation or revival.
5. Add tests for absent/default behaviour, explicit suppression, scripted/event exemptions and multiplayer authority handover.
6. Verify in Unity when an Editor is available; otherwise report compilation and runtime testing as blocked/not run.
7. Update the shared notes and return the exact diff/commit for review. Do not merge to `main`.

**Explicitly not in this phase:** player-built stations and station-attracted NPC traffic. That is the final implementation step after a real player-built-station placement, ownership, lifecycle and persistence system exists.


## 3g. Extended system population audit

The follow-up audit is recorded in `docs/notes/system-population.md` (section 9). Cruise-drive encounters are player-triggered, separately gated and should not be silently removed by ambient suppression. Pirate activity events, system-special actors, freelance mission ships/mining sites, siege garrisons/defenders and decorative traffic lanes are also separate categories. The key station finding: this branch has no player-built-station placement/ownership/destruction lifecycle to hook into. `MobileStation` is mod-authored and clock/route-driven; faction claims refer to existing stations; neither is player construction. Siege garrisons are separately tagged combat actors. Do not implement station attraction until the final implementation step, after a real player-station lifecycle/ownership model exists. The safe first scope remains ordinary ambient `TrafficPlan` ships plus their matching revival logic, with explicit ambient classification rather than broad group/race filtering.


## 3h. Core system-population feasibility conclusion

The core mod-controlled ambient-suppression feature is assessed as feasible with low-to-moderate complexity. A single optional per-system setting can be resolved in `ModWorld`; the runtime work is concentrated around initial ordinary ambient planning and the corresponding timed revival paths. The main technical requirement is reliable ambient-origin classification so mission/event/story NPCs are not caught by broad group/race filters. See `docs/notes/system-population.md` §§8–10. No implementation has started.
