## 2026-11-15 — Record opt-in system NPC population design (ChatGPT)

**Branch:** `feature/extensive-multiplayer-expansion`

Recorded the user's direction for a mod-only, per-system NPC population control capability:
- Mods can suppress ambient NPC presence in selected systems; absent configuration preserves current behaviour.
- Existing systems and the base story are unaffected unless explicitly configured by a mod.
- A player-built station may optionally attract sparse, infrequent and varied ambient NPC traffic.
- Story-/mission-scripted NPCs must remain distinct from ambient population control.

Design/documentation only. No gameplay source, system data, mod schema, networking or tests changed.

## 2026-11-15 — Rename active feature branch (ChatGPT)

The active development branch is being renamed from `feature/secondary-homing-delay-launch-angle` to `feature/extensive-multiplayer-expansion` to reflect the wider multiplayer expansion scope. No gameplay code changed.

## 2026-11-15 — Record class-based station docking and freighter boarding design (ChatGPT)

**Branch:** `feature/secondary-homing-delay-launch-angle`

Recorded the user's docking decisions:
- All stations support interior and external docking; Fighters and Interceptors are interior-only.
- Unclassified ships cannot externally dock, while retaining ordinary interior docking.
- External docking keeps the ship present and visible in space; the camera stays in space unless the player explicitly
  enters the Space Lounge.
- The future physical arrangement uses ship and station docking points, but initial behaviour needs no visible hardware.
- Hackable freighters do not support external docking; boarding is limited to ships smaller than a Frigate.

Design/documentation only. No docking or gameplay implementation and no tests.

## 2026-11-15 — Record turret hardpoint scaling and point-defence profiles (ChatGPT)

**Branch:** `feature/secondary-homing-delay-launch-angle`

Recorded the user's decisions:
- Add independent per-turret-hardpoint scaling, separate from ship-kit hull scale and turret item `fx.turret.scale`.
- Point-defence profiles: `ships`, `missiles`, and `missiles_priority`; no `balanced` profile.
- Keep targeting eligibility separate from weapon damage and extend the existing missile interception system.

Documentation/design only. No source, schemas, Mod Builder UI or tests changed.

## 2026-11-15 — Clarify ship classification is opt-in mod content (ChatGPT)

**Branch:** `feature/secondary-homing-delay-launch-angle`

Updated project context and handover to make ship classification strictly opt-in:
- Original/vanilla ships remain unclassified; no standard classes are assigned in base data.
- The 14 agreed names are a recommended mod-author vocabulary, not base-game ship classes.
- Missing class means unrestricted equipment compatibility, including for items with `allowedClasses`; restrictions only
  apply when a mod explicitly assigns a class.
- Instant scanner readout applies only to targets with an explicit mod-assigned class.

Documentation/design only. No gameplay source or vanilla ship data changed; no tests/build run.

## 2026-11-15 — Record standard ship classes and Privateer design (ChatGPT)

**Branch:** `feature/secondary-homing-delay-launch-angle`

Updated the project context and handover with the user's 14 standard ship classes and the shared multi-purpose design
criteria for Light Privateer, Privateer and Heavy Privateer. The standard taxonomy remains extensible to mod-defined
classes; numeric thresholds are deferred pending a survey of the ship roster.

Documentation/design only. No gameplay source, ship data, scanner implementation or tests were changed.

# AI change log

This file records actual repository changes made by AI contributors. It is not a design wishlist.
Newest entries go at the top.

---

## 2026-11-15 — Secondary launch direction from the hardpoint (Claude)

**Branch:** `feature/secondary-homing-delay-launch-angle`

Changed:
- Removed item attributes `launchPitchDeg` / `launchYawDeg` (173 / 174) and their docs. `homingDelayMs` (172) stays on the item.
- `WeaponMount.RotationTurn` (new): the mount's `rotation` as a turn, without `upsideDown`. `ModHardpoints.MountTurn` uses it (same result).
- `WeaponSystem.Setup`: a secondary's `Gun.launchTurn` comes from its mount; `Gun.TryFire` applies it on the plain, salvo and cluster paths.
- `GunLaunchTests` rewritten (same missile on two mounts, default straight ahead, upsideDown excluded, ship axes, speed kept).
- Docs: `Modding/README.md`, `Modding/ai/gof2-modding/guide.md`, `docs/notes/weapons.md`.

Not compiled; tests not run. No network protocol, save or primary / turret change.

---

## 2026-11-15 — Add shared AI collaboration documentation

**Branch:** `feature/secondary-homing-delay-launch-angle`

Added:
- `AI_PROJECT_CONTEXT.md` — product vision, engineering principles, architecture, ownership rules and collaboration protocol.
- `AI_TASK_LOG.md` — chronological task history and the current documentation task.
- `AI_CHANGELOG.md` — this record of repository documentation changes.
- `AI_HANDOVER.md` — current branch/task status and review instructions for Claude.
- A short collaboration-notes pointer in `CLAUDE.md`.

No gameplay code, network protocol, item schema, hardpoint schema, save data or tests were changed by this
documentation task. The secondary weapon launch-direction implementation has not yet been corrected.
No merge to `main` was performed.


## 2026-11-15 — Record instant scanner and secondary launch-orientation decisions (ChatGPT)

**Branch:** `feature/secondary-homing-delay-launch-angle`

Updated the shared project context and handover to record:
- Class scanners identify a locked target immediately, supporting rapid threat prioritisation by large ships.
- Launch-direction adjustment is secondary-only; primaries remain unchanged.
- The preferred moddable design is a separate optional mount-level `launchRotation`, preserving existing visual `rotation` semantics and straight-ahead defaults.

This is a documentation/design update only. No gameplay source, tests, Mod Builder UI or scanner implementation was changed.
