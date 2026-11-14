# AI handover — current baton pass

> **Read this first when continuing the project.**
> Last updated: 2026-11-15 (second update)
> Prepared by: ChatGPT, then updated by Claude after the corrections below
> Intended recipient: ChatGPT (review) and the user

## 1. Repository and branch

- **Repository:** `SpectreAnims/Galaxy-on-Fire-2-Unity-Remake`
- **Active branch:** `feature/secondary-homing-delay-launch-angle`
- **Base:** `main`
- **Branch state:** feature branch only; re-check the live branch head and comparison with `main` before further edits.
- **Integration instruction:** Do not merge or push this feature branch to `main`. The user wants Claude to review
  the changes before deciding what happens next.
- **Coordinator:** the user. They manually pass work between Claude and ChatGPT.

The four AI notes files and the `CLAUDE.md` discovery pointer are being added to this feature branch so
Claude can read the same project brief and handover. This does not authorize integration into `main`.

## 2. Product goal

The ultimate goal is a faithful Galaxy on Fire 2 Unity Remake expanded into a coherent, persistent
multiplayer universe. This is not a multiplayer-only rewrite. Preserve the existing single-player game,
its flight/combat/economy/content and modding architecture while making multiplayer deeper and more
durable: shared orbits, persistent player progression, squads/co-op, trade, factions, meaningful conflict,
and eventually player-owned/player-built stations and other lasting world structures.

Read `AI_PROJECT_CONTEXT.md` for the complete vision, player-facing pillars, architecture constraints,
ownership rules and collaboration process. It distinguishes long-term goals from already implemented features.

## 3. Active technical task: secondary weapon launch behaviour

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

## 6. Next actions

1. Revise the current hardpoint launch implementation to use a distinct optional, moddable mount field such as
   `launchRotation`, leaving existing visual `rotation` semantics unchanged. Apply it to **secondaries only**.
2. Confirm the intended projectile-path scope and add tests for default behaviour, mount-specific direction, and each
   supported secondary firing path.
3. Update modding documentation and, if practical, expose the setting in Mod Builder.
4. Independently review the implementation and verify the shot replication path.
5. Implement ship-class data, equipment restrictions and instant scanner identification as a separate feature task.
6. Return findings to the user for approval. Integration to `main` remains the user's decision.
