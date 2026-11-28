# Lua-to-C# optimization port audit

Scope: the open SMEP Mod Builder project, reviewed against JoppieToppie's `main` at `9c876732279a4986590ea793038afa48b794c621`.

## Ported

| Lua source | Optimization | Native C# destination | Status |
|---|---|---|---|
| `scripts/main.lua` | Cache the filtered `QuestGraphs()` list and invalidate it when `AllGraphs()` returns a replacement list. This avoids allocating a new `FindAll` result on every once-per-second quest-start check. | `Assets/Scripts/Runtime/Net/Events/EventRunnerQuests.cs` | Implemented; regression test added; Lua patch removed after the C# replacement was added. |

The native cache keys off the identity of the list returned by `AllGraphs()`. That method already replaces its list when mod revisions or event-file changes invalidate the graph cache, so the filtered view follows the same invalidation boundary without patching either method at runtime.

Regression coverage: `QuestGraphTests.QuestGraphFilterIsCachedUntilAllGraphsIsInvalidated`.

The removed Lua block is backed up in the open SMEP project at `scripts/backups/main_pre_native_quest_graph_cache_20261123.lua`.

## Already native; do not duplicate

| Lua area | Existing C# implementation |
|---|---|
| `scripts/main.lua` quest graph loading cache | `EventRunnerQuests.AllGraphs()` already caches compiled graph details and checks mod revision / event folder changes. The port above only adds the missing filtered-list cache. |
| `scripts/zz_fighter_ai.lua` target motion sampling | `TurretTargeting.VelocityOf` samples target motion once per frame and reuses it across turrets. |
| `scripts/zz_fighter_ai.lua` / collision broadphases | `Target.BroadSphere`, `TargetGrid`, and `Obstacle.MayTouch` already provide per-frame broadphase data and spatial filtering. |
| Fighter obstacle avoidance | `NpcShipSteering.Avoid` uses `Obstacle.MayTouch` before volume tests. |
| Mesh collision query buffers | `HullCollision` uses non-allocating physics queries and reusable static buffers; `PlayerCollision` reuses its overlap buffer and caches its probe size per visual model. |

## Reviewed but intentionally not ported

- **`scripts/main.lua` frame diagnostics:** instrumentation, not an optimization; retaining it in the core runtime would add ongoing work.
- **`scripts/main.lua` scanner / project-item caches:** these serve SMEP-only HUD and equipment rules. They are not general engine behavior and should remain with the mod.
- **`scripts/zz_fighter_ai.lua` mobile-player, fighter-eligibility, hull-turn-rate, and firing caches:** these support SMEP-specific targeting and firing behavior. Porting them alone would either duplicate native state or require porting gameplay behavior, which is outside this optimization-only pass.
- **`scripts/zz_fighter_ai.lua` reciprocal player/NPC mesh-contact snapshots and swept-AABB rejection:** these optimize a separate reciprocal-contact behavior. They are not an isolated improvement to the existing native avoidance path; keep them out of the core until that behavior is separately approved.
- **`scripts/zzzz_asteroid_volume_interaction.lua`:** the file is currently disabled behind `if false`; it is not an active runtime optimization to migrate in this pass.
- **`scripts/zz_capital_obstacles.lua`:** adds SMEP class-specific obstacle behavior; this is functionality, not a standalone performance optimization.
- **`scripts/zz_turret_legacy_prediction.lua`:** acceleration-aware prediction changes aiming behavior. Native `TurretTargeting` already caches velocity samples; acceleration lead is a separate accuracy feature, not a performance port.
- **`scripts/population.lua`:** system population and lounge-race rules are SMEP-specific. Its normalized rule lookup is already precomputed in Lua; moving the feature into the game core would be a scope expansion.

## Verification state

- SMEP Mod Builder project validation after removing the quest-graph Lua patch: **no errors or warnings**.
- C# regression test is committed, but Unity EditMode execution has not yet been observed. Do not treat the C# change as runtime-verified until CI or a Unity test run reports success.
