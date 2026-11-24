# Optional jump drive rules (remake mods)

The base Khador Drive is unchanged unless an enabled mod overrides it. Mods that create
a drive based on item 85 and already provide `jumpFx` can additionally provide
`jumpRules` on the same `items.json` entry. The existing `jumpFx` data/texture and
legacy `failChance` / `failMax` / `failNoRoute` settings remain supported.

## Hyper Drive example

```json
[
  {
    "id": "hyperdrive",
    "base": 85,
    "name": "Hyper Drive",
    "jumpFx": {
      "data": "fx/hyperdrive_3d.json",
      "texture": "fx/hyper_drive.png",
      "failTexture": "fx/hyper_drive_purple.png",
      "noVoid": true
    },
    "jumpRules": {
      "energyCellsMultiplier": 4,
      "connectedFailurePerSegment": 0.03,
      "connectedFailureMaximum": 0.20,
      "unconnectedFailureChance": 0.20,
      "requireConnectedFailureDestination": true,
      "showFailureChance": true,
      "catastrophicHullDamage": 0.40,
      "minimumHullRemaining": 0.10,
      "equipmentLossChance": 0.50,
      "equipmentLossIncludesDrive": true
    }
  },
  {
    "override": 85,
    "availability": {
      "shops": false,
      "equipment": false,
      "loot": false,
      "npcCargo": false,
      "blueprints": false
    }
  }
]
```

The `jumpFx` data and textures in this sample must exist within the mod. This is a
minimal example, not a replacement for the complete Hyper Drive mod's visual options.

## Mechanics

- Connected-route failure chance is `min(connectedFailureMaximum,
  connectedFailurePerSegment * numberOfGateSegments * extremeMultiplier)`;
  Extreme uses multiplier 2, otherwise 1.
- Unconnected jumps use `unconnectedFailureChance` as an **absolute probability**,
  unchanged by Extreme. In this example that is exactly 20%.
- Energy consumption is based on the original drive's energy calculation, multiplied
  by `energyCellsMultiplier`, including the original Extreme consumption modifier.
- With `showFailureChance`, the selected destination's predicted risk appears beside
  energy cells in the galaxy map, in the jump confirmation, and on charging.
- With `requireConnectedFailureDestination`, a failure selects a visible, populated
  station orbit in a different system from the departure **and** intended destination,
  whose system has an outbound and reciprocal jumpgate link. A live gate-state provider,
  when installed, must report both directions as operational. If no safe emergency
  destination exists, a failed jump instead continues to the requested system without
  catastrophic penalties.
- Hull damage uses a percentage of maximum hull, applied at the departure scene before
  transition, capped so current hull never falls below `minimumHullRemaining` of maximum
  hull. Hull already at/below that threshold does not take any damage.
- On the independent `equipmentLossChance` roll, one random installed **equipment**
  item is lost, not cargo, weapons, ammunition or ship ownership. If enabled, the current
  drive may be among those items. The drive's already-started failure visual is preserved
  even if that drive is lost.
- No penalties are applied to the base Khador Drive or legacy jumpFx mods that do not
  supply jumpRules.

## Item availability

The `availability` object is also valid on original-item overrides that change nothing
else. Missing keys are allowed. Supported keys:

- `shops`: filters generated and existing station shop stock, prevents purchase, and
  filters ordinary lounge item sellers.
- `equipment`: prevents mounting and disables jump-drive functionality for equipment
  already installed (but does not delete or convert it).
- `loot`: filters NPC loot and capital-ship equipment crates.
- `npcCargo`: filters ordinary NPC loot/holds resolved through the shared loot tables.
- `blueprints`: filters products and prevents blueprint production and standard
  blueprint sellers.

Existing owned or cargo copies are retained, rather than silently deleted.
Scripted story grants are not rewritten; mods may need additional story handling
if they disable an item required by their campaign.

## Gate and multiplayer integration

`GameHooks.JumpgateLinkOperational` is an optional `Func<int, int, bool>` for live
gate availability (including EMP disabling, scrapping and destruction). It is null in
the existing base game, which means static reciprocal links count as operational.
In multiplayer, NetHooksInstaller registers the player-jumpgate registry as the live
link provider. Original static reciprocal links remain valid. A connection created
by player-built gates qualifies only if BOTH systems have operational gates
pointing at one another. Construction, EMP shutdown, structural destruction and
scrapping remove those dynamic links from emergency-destination eligibility.
The provider checks gate operational state, not the player's access permissions.

In solo play the local jump controller resolves the outcome. In multiplayer, opt-in
jump-rule drives require an active controlling server profile (not guest, arena or
host-save mode). The client sends a profile snapshot and jump request; the server
checks the origin, destination visibility, mounted drive and fuel, rolls failure,
selects a visible emergency destination with operational reciprocal links, and
commits fuel and equipment loss and the arrival station before acknowledging the
result. The client applies the returned hull limit and plays the corresponding
transition. If no eligible emergency destination exists, the requested jump proceeds
without catastrophic penalties. Replayed profile upload sequences are rejected.

The server uses replicated pilot state and accepted profile data; this is not a
fully server-simulated flight model. Legacy drives without jumpRules retain their
existing behaviour. Multiplayer profiles use Normal difficulty for this calculation.

## Compatibility and defaults

Every new setting is opt-in. The old Khador Drive, existing saves, and legacy
mod drive effects retain their previous behaviour when `jumpRules` and
`availability` are absent.

For reference, the relevant engine files are `ModJumpFx.cs`, `SystemJump.cs`,
`StarMapRules.cs`, `StarMapOverlay.cs`, `ModContentNumbering.cs`, `Shop.cs`,
`Hangar.cs` and `GameHooks.cs`.
