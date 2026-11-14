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
