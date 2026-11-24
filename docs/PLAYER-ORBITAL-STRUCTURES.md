# Player-built orbital structures

Dock at an operational player station and open **Station Management → Orbital Structures**. Choose a preset and an orbit in the sponsor system. Each orbit has three shared defense slots by default and eight other structure slots. Orders reserve a slot before the first delivery. Lowering the defense limit preserves existing defenses and projects. The server accepts new orders once usage falls below the limit. Open **Multiplayer → Orbit Projects** in the build orbit to deliver the required materials and see progress. The station owner or an eligible faction member can use the sponsor's donated stock. Qualifying deliveries from cargo to faction projects are paid from the faction bank.

An operational defense engages hostile ships within its configured range. Players are hostile if banned by the sponsor, hostile to the sponsor's system race by standing, or recently recorded attacking player infrastructure in that orbit. The sponsor and its faction are protected. Hostile NPCs are selected using the sponsor system race and the NPC's alignment. Defenses choose a hostile player first, then a hostile NPC if no eligible player is in range. The server applies player defense damage and schedules NPC defense fire through the orbit runner. Defenses are inactive while the sponsor is unavailable and resume when it is operational again.

Additional structures use the same construction, sponsorship and damage rules. They are physical installations with configurable models and integrity. A structure with `defense: false` has no weapon behavior. Structural weapons can destroy an operational structure and free its slot. Sponsor managers can dismantle a structure from Station Management. Dismantling does not refund materials.

## Mod Builder

Open **Menu → Player-built stations & gates**. **Defense limit per orbit** accepts 0–12, with 0 blocking new defense orders. The setting exports as `orbitalDefenseLimit` in `playerInfrastructure.json` and applies with the active mod's infrastructure rules. The **Orbital structure presets** field accepts the following `orbitalStructures` array. The settings export with the mod in `playerInfrastructure.json`. Disable **Offer the stock orbital defense** to use only your own presets. A custom definition with ID `orbital-defense` replaces the stock defense with the same ID.

```json
[
  {
    "id": "frontier-defense",
    "name": "Frontier Defense",
    "defense": true,
    "stationModel": "my_mod:frontier_platform",
    "modelScale": 1,
    "hitRadius": 300,
    "integrity": 18000,
    "range": 6000,
    "damage": 150,
    "fireIntervalSeconds": 4,
    "buildSeconds": 60,
    "materials": [
      { "item": 156, "amount": 300 },
      { "item": 118, "amount": 120 }
    ]
  }
]
```

Use either `stationModel` with a station model reference or `assembly` with an existing game assembly such as `turret_002_static`. Distances are in metres. Integrity, material costs, scale, timing and combat settings are saved into each project when ordered. Additional station or gate assets can be registered in the same Mod Builder section.
