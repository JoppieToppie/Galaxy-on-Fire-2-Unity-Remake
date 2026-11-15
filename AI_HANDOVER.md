# AI handover — current baton pass

> # 🚨 IMPORTANT — PLAYER-BUILT STATIONS DO NOT EXIST YET
> **Do not implement player-built stations or station-attracted NPC population now.**
> Player-built stations are a future feature and are explicitly the **LAST implementation step** for this system-population workstream. Complete and verify the mod-controlled ambient population feature first. Only return to station-attracted traffic after a real player-built-station system exists, including authoritative placement, ownership, lifecycle and persistence. Do not substitute mobile stations, faction claims or other existing station systems.
>
> **This notice is for BOTH ChatGPT and Claude.** These repository notes are shared project handover material: every AI contributor must read them before work, preserve this sequencing, and update the notes when decisions or implementation status change.


> **Read this first when continuing the project.**
> Last updated: 2026-11-15 (Claude: launchRotation, ship classes, comparison with Joppie's repository)
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

## 3. Secondary weapon launch behaviour: implemented (needs Unity verification)

- **Design (owner):** `homingDelayMs` is an item property; the initial launch direction of a **secondary** is a property of the
  **hardpoint** that fires it (AI_PROJECT_CONTEXT 6.3).
- **Implemented (Claude):** `WeaponMount.launchRotation` (a new field, **separate from the visual `rotation`**, so existing mods
  that tilt a pod for looks are unchanged) -> `WeaponMount.LaunchTurn` -> `Gun.launchTurn`, set in `WeaponSystem.Setup` from the mount
  the secondary sits on; applied on the plain, salvo and cluster paths. Mines, beams, primaries, turrets and NPC / capital guns are
  untouched; no field = straight ahead (all 135 original secondary mounts). `homingDelayMs` (attr 172) stays on the item, clamped
  0..60000 ms (the wire field is a ushort). The item-level `launchPitchDeg` / `launchYawDeg` were removed. The Mod Builder project
  copy and export carry `launchRotation`; **no editor UI yet**.
- **Correction to an earlier revision:** the first hardpoint version reused `rotation`, which would have redirected the shots of
  every existing mod that tilts a secondary's model; it now uses `launchRotation`, as 6.3 asks.
- **Multiplayer:** audited: `NetShotData` carries velocity and homing delay, `NetShotMirror` injects by velocity, so a remote client
  sees the same flight without a protocol change. Not tested over a network.
- **Not done:** Unity compile and `GunLaunchTests` (no Editor in the environments the AIs have used). Hand-check: a mod ship with
  one secondary mount `"launchRotation": [-90, 0, 0]` (up) and another `[0, 90, 0]` (right), same missile.

## 4. Current implementation task: per-ship free-look orbit distance and free-look target locking

- **User request:** Add moddable per-ship control over the free-look orbit camera's default distance, and allow target locking while in free look.
- **Existing behaviour:** The normal chase camera already has per-ship `ships.json` `camera.distance` / height / look offsets. The separate free-look orbit used a universal 3800-unit start radius and 1500..20000 zoom range.
- **Implementation:** Add `camera.orbitDistance` (default 3800, clamped to 1500..20000); apply it when the player's ship is created and when swapped in flight. In free look, `CombatRadar` uses the camera viewport centre as the lock acquisition point and the HUD crosshair/lock ring move to that same centre; normal flight retains the ship-forward aim point.
- **Compatibility:** No change to chase-camera distance, docked orbit preset, scanner requirement, lock timing, target priority, sticky lock, weapon homing or multiplayer shot protocol.
- **Tests added:** Mod content tests for default, override, clamp and invalid values; target-lock test for free-look viewport-centre selection.
- **Verification:** Source changes committed on this branch. Unity compilation, EditMode tests and live multiplayer verification remain outstanding.

## 5. Turret hardpoint scaling and point-defence targeting

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

## 6. Ship classes, class-restricted equipment and class scanners: implemented (needs Unity verification)

The 14 recommended class ids are in `AI_PROJECT_CONTEXT.md` 6.2 (not base-game classifications; original ships stay unclassified;
mods assign them and may add more). **Implemented (Claude), all opt-in:**
- ships.json `shipClass` (id), `classTags` (free words), `className` (display text); items.json `allowedClasses`, `excludedClasses`
  (match class id or any tag). An **unclassified** ship (no class and no tags: every original one) takes every item, even one with
  an allow-list. Source: `Modding/ShipClassRules.cs`, `ShipData` / `ItemData` fields.
- Gates: `Hangar.CanMount` (`Result.WrongShipClass`, the Mount button says why), `Hangar.SwitchTo` (a ship swap: misfits go to the
  hold), `Hangar.FitToSlots` (docking), `NetProfiles.ClampClasses` (server, on upload and sign-in).
- Scanner integration (ChatGPT, 2026-11-15): retained project `scanClass` (143), and incorporated Joppie's `scanShip` (140),
  `scanHitpoints` (141), `scanWeapons` (142), and `radarShowsCargo` 2. Hull, hitpoints, weapons, cargo and class now share one
  readout; NPC hull identity is available to the lock plate, class identity resolves NPCs, remote NPC proxies and other players.
  A completed lock fires `on scanned` in event graphs/quests; multiplayer forwards it through a rate-limited, orbit-validated RPC.
  **Trust boundary:** the event's ship name is still client-reported; the server does not prove that the sender actually locked
  that target. Keep it as a lightweight script trigger, not a high-value reward authority, until a network target identity is verified.
- Added `ModScannerTests` for attribute numbers, cargo modes, combined readouts and lock-plate naming. **Not compiled, not run.**
  Still not covered: Kaamo Club hull gear checked on take-out; no Mod Builder UI for scanner stats.

## 7. Comparison with JoppieToppie's repository (checked 2026-11-15)

- Our `main` (`0b4428e8`) has the **same tree** as Joppie's `4cd3f860` (Nov 13). At the current check (2026-11-15), his `main`
  is `0be0f76c`, **8 commits ahead** of that shared baseline.
- The upstream additions since the shared baseline are: mod Lua scripting / Mod Builder Scripts tab; scanner readouts and the
  `on scanned` event; generated AI modding-reference updates; station/system music; beam and collector range display corrections;
  and the photo-mode screenshot notice fix (including the final PR merge).
- **Selectively ported to our feature branch:** scanner commit `d8aaa1f3` (hull label 140, hitpoints 141, weapons 142, cargo mode 2
  on attr 31), retaining our `scanClass` 143; the combined readout also resolves remote NPC proxies and other players. The
  `scanned` event is wired into event graphs and quests with a rate-limited, orbit-validated multiplayer RPC. Also ported the
  final beam/collector range display correction: attrs 51 and 53 display at x0.1, while targeting retains raw values.
- **Deliberately deferred:** Lua scripting is a large, independent 92-file feature; it touches `Database`, `Hangar` and
  `NetHooksInstaller`, so a later merge must preserve our class gates and scanner event hook. Station/system music is
  independent, but its `TrafficSpecials` edit shares a future integration surface with the mod-controlled NPC population work.
  The photo-mode fix is unrelated. The generated AI reference should be rebuilt only after the chosen upstream features are merged;
  the scanner/event/quest documentation has been updated manually in this branch.
- No cherry-pick or merge of Joppie's branch was made. These are selective ports on our feature branch only.
- **Naming:** upstream already has `turretScale` (a turret item's own scale, from `fx.turret.scale`); the per-mount scale in 6.4 should
  use a different JSON name to avoid two meanings.

## 8. Class-based station exterior docking and freighter boarding

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

## 9. Existing NPC population pipeline investigation

See `docs/notes/system-population.md` for the code-path notes. Key finding: the existing `systems.json` `traffic: "mod"` option changes the source of traffic, not its quantity; it does not suppress mod NPCs. Initial planning is in `TrafficPlan.Build`, while `TrafficSpecials` independently revives jumpers, locals, raiders and Void attackers. Story and mission spawns have separate paths, and multiplayer population creation belongs to the orbit authority. The audit is now sufficiently complete for the narrow core feature: ordinary ambient population is planned through `TrafficPlan.Build`, with matching revival paths in `TrafficSpecials`. The implementation should use explicit ambient-origin classification and a single optional per-system mod setting. Player-built-station work is explicitly deferred to the final phase.

## 10. Mod-controlled system NPC population

The user has specified a deliberately lightweight, **explicitly modded** system-population feature (2026-11-15). This is **design only**:

- Population control exists as an engine capability for modders; it is not enabled globally and must not affect the unmodified base game.
- Each system can be configured independently. The main use is to remove ambient NPC presence from selected systems by default, especially multiplayer-mod systems and new systems. Existing systems remain vanilla unless a mod explicitly opts them in.
- An optional future rule may let a player-built station attract a small amount of NPC activity into an otherwise empty / quiet system. It should be sparse, infrequent and diverse, not a return to normal busy-system population.
- Preserve story-/mission-scripted NPCs and encounters; distinguish them from ambient population so population suppression cannot accidentally break the base story.
- Population/spawn decisions should be authoritative and consistent across multiplayer clients.

The core feature is now scoped to one optional per-system enabled/disabled setting: absent configuration preserves current behaviour; explicit configuration suppresses ordinary ambient traffic. No density levels are needed for this first version. Exact JSON field name remains an implementation decision to be made against parser conventions.

**Feasibility:** low-to-moderate. The system mod parser, initial `TrafficPlan.Build` path and timed ambient revival paths are known. The main care point is an explicit ambient-vs-scripted origin marker; broad race/group filters are unsafe. Keep cruise encounters, pirate events, missions, sieges, authored special populations and decorative traffic separate.

No source, system data, mod schema, NPC spawning, networking or tests have been changed. See `docs/notes/system-population.md` §10 for the detailed assessment.

## 11. Claude review request: done (2026-11-15)

Claude reviewed the branch and the notes against the code. Result:
- The vision in `AI_PROJECT_CONTEXT.md` is represented accurately.
- Mount `rotation` exists (`WeaponMount.rotation`, Unity ship frame, used by `ModHardpoints.MountTurn` / `MuzzleOffset` and, for
  exhaust mounts, `EngineFx`); it was **not** reused for the launch, so mods that tilt a model keep firing straight: `launchRotation`.
- Firing paths traced: plain, salvo, cluster (changed), mines and beams (deliberately not), NPC guns, turrets, and the multiplayer
  mirror (velocity is on the wire). See section 3.
- GPT's code on this branch (per-ship `orbitDistance`, free-look lock acquisition, HUD crosshair) was read and found consistent with
  its notes; **it is uncompiled and untested** like the rest. One naming caution: `turretScale` already exists upstream (see 7).
- Two notes were stale or wrong and are corrected here and in `AI_PROJECT_CONTEXT.md`: the launch design (section 3) and the
  class design (6.2: tags and an exclude-list were added by the owner after the first allow-list-only note; unclassified ships take every item).
- Still true: do not merge or push to `main` without the owner's approval.

## 12. Working rules for the next contributor

- Read `AI_PROJECT_CONTEXT.md` and the relevant `docs/notes/*.md` before code changes.
- Inspect the exact branch head and full diff before editing.
- Do not discard Claude's original homing-delay work while correcting launch-angle ownership.
- Keep the user informed of design choices and unresolved product decisions.
- Update `AI_TASK_LOG.md`, `AI_CHANGELOG.md` and this handover after meaningful work.
- Report exact branch and commit SHA; do not claim compile/test/push status without verification.

## 13. Next actions — core system ambient suppression

1. Re-read the current branch head and exact diff before editing; preserve any concurrent Claude changes.
2. Inspect `SpawnSpec` construction and `NpcShip` metadata to settle a reliable ambient-origin classification.
3. Add one optional per-system boolean through `ModWorld` parsing/merging/validation; absent configuration must preserve existing behaviour.
4. Gate ordinary ambient planning and its matching revival paths only. Do not blanket-filter NPC creation or revival.
5. Add tests for absent/default behaviour, explicit suppression, scripted/event exemptions and multiplayer authority handover.
6. Verify in Unity when an Editor is available; otherwise report compilation and runtime testing as blocked/not run.
7. Update the shared notes and return the exact diff/commit for review. Do not merge to `main`.

**Explicitly not in this phase:** player-built stations and station-attracted NPC traffic. That is the final implementation step after a real player-built-station placement, ownership, lifecycle and persistence system exists.


## 14. Extended system population audit

The follow-up audit is recorded in `docs/notes/system-population.md` (section 9). Cruise-drive encounters are player-triggered, separately gated and should not be silently removed by ambient suppression. Pirate activity events, system-special actors, freelance mission ships/mining sites, siege garrisons/defenders and decorative traffic lanes are also separate categories. The key station finding: this branch has no player-built-station placement/ownership/destruction lifecycle to hook into. `MobileStation` is mod-authored and clock/route-driven; faction claims refer to existing stations; neither is player construction. Siege garrisons are separately tagged combat actors. Do not implement station attraction until the final implementation step, after a real player-station lifecycle/ownership model exists. The safe first scope remains ordinary ambient `TrafficPlan` ships plus their matching revival logic, with explicit ambient classification rather than broad group/race filtering.


## 15. Core system-population feasibility conclusion

The core mod-controlled ambient-suppression feature is assessed as feasible with low-to-moderate complexity. A single optional per-system setting can be resolved in `ModWorld`; the runtime work is concentrated around initial ordinary ambient planning and the corresponding timed revival paths. The main technical requirement is reliable ambient-origin classification so mission/event/story NPCs are not caught by broad group/race filters. See `docs/notes/system-population.md` §§8–10. No implementation has started.
