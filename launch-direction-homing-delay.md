# Missile launch direction and homing delay (patch notes)

Status: written against the shipped source, **not compiled or run** (no Unity / C# compiler where it was made). Build it
and test in flight before merging. Defaults reproduce the current behaviour exactly.

## What it adds

Two optional fields on a ship's mount data (`WeaponMount`), read from a mod ship's `"mounts"` JSON like `turretUp`:

| Field | Type | Meaning | Default |
|---|---|---|---|
| `launchDir` | `[x, y, z]` | The direction missiles leave this mount along. Game space like `turretUp` (x toward the pilot's left, y up, z forward; any length). | none = straight ahead |
| `homingDelayMs` | number | Milliseconds a homing missile flies its launch heading before it starts to steer onto the lock. | 0 = steer at once |

They apply to **secondary** mounts (`slotType` 1) only. Primaries are untouched. Guided munitions (item attribute 15, the
Liberator, flown by the pilot) keep their own launch.

## Where each change is

| File | Change |
|---|---|
| `Runtime/Game/Database.cs` | `WeaponMount.launchDir`, `WeaponMount.homingDelayMs`, and the `LaunchDirLocal` property (game space to Unity ship frame, `(-x, y, z)` like `TurretUpLocal`; `Vector3.forward` when unset). |
| `Runtime/Space/Gun.cs` | New `launchLocal` field (default `Vector3.forward`). `TryFire` uses it for the spawn offset, the shot direction (`fwd` and `dir`, so cluster, salvo and mine shots follow too) and the bullet's up. Straight up or down gives the bullet the ship's nose as its up, because `GunRig` uses `LookRotation(velocity, up)`. |
| `Runtime/Space/WeaponSystem.cs` | When the player's guns are built, a secondary (not guided) takes `launchLocal` and `homingDelayMs` from the mount it sits on. |
| `Runtime/Modding/ModContentMerging.cs` | `Copy(WeaponMount)` keeps the two new fields. |
| `notes/mods-weapons-audio.md` | A documentation entry for the two fields. |

## How it works (no new mechanism)

- `Gun.homingDelayMs` already existed (NPC rockets use 1000 ms): `Update` only steers a bullet once `b.age >= homingDelayMs`.
  Homing steers onto the gun's *current* lock each frame, so a lock gained after launch is picked up.
- Multiplayer: the shot message (`NetShotData`) already carries the velocity and `homingDelayMs`, so other players' copies of
  the missile fly the same heading and steer the same way. No netcode changed.

## Usage (mod JSON)

A secondary mount that fires straight up and starts steering half a second later:

    { "slotType": 1, "position_engine": [0, 400, 1200], "launchDir": [0, 1, 0], "homingDelayMs": 500 }

## Test checklist

1. A ship with no `launchDir` / `homingDelayMs`: missiles behave exactly as before.
2. `launchDir` up, down, left, right, back: the missile leaves that way; no model twisting at straight up / down.
3. `homingDelayMs` 0 vs 500 vs 1500 with a lock held: the missile flies its heading, then curves onto the target.
4. Fire with no lock, then lock after launch: the missile starts steering once it has a lock and the delay has passed.
5. Cluster missiles, salvos and mines from an angled mount.
6. Two players: the other player sees the same flight path.
7. The Liberator on an angled mount still launches as before.

## Known limits

- The muzzle flash still faces the nose (`GunRig` uses `ship.forward`).
- Salvo shots are laid side by side along the ship's right axis, which looks off on a side-facing tube.
- Thermo's random spread is applied in ship axes, not around the launch direction.
- Turn rate is unchanged (a fixed fraction of the error per frame, so a missile launched sideways or up arcs widely).
- The Mod Builder has no UI for the two fields; hand-written JSON only. Ships built from kits (`KitRules`) are not covered.
