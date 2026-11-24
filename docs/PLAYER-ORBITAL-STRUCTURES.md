# Player-built orbital structures

Dock at an operational player station and open **Station Management → Orbital Structures**. Choose a preset and an orbit in the sponsor system. Each orbit has three shared defense slots by default and eight other structure slots. Orders reserve a slot before the first delivery. Lowering the defense limit preserves existing defenses and projects. The server accepts new orders once usage falls below the limit. Open **Multiplayer → Orbit Projects** in the build orbit to deliver the required materials and see progress. The station owner or an eligible faction member can use the sponsor's donated stock. Qualifying deliveries from cargo to faction projects are paid from the faction bank.

An operational defense engages hostile ships within its configured range. Players are hostile if banned by the sponsor, hostile to the sponsor's system race by standing, or recently recorded attacking player infrastructure in that orbit. The sponsor and its faction are protected. Hostile NPCs are selected using the sponsor system race and the NPC's alignment. Defenses choose a hostile player first, then a hostile NPC if no eligible player is in range. The server applies player defense damage and schedules NPC defense fire through the orbit runner. Defenses are inactive while the sponsor is unavailable and resume when it is operational again.

Additional structures use the same construction, sponsorship and damage rules. They are physical installations with configurable models and integrity. A structure with `defense: false` has no weapon behavior. Structural weapons can destroy an operational structure and free its slot. Sponsor managers can dismantle a structure from Station Management. Dismantling does not refund materials.

## Defense maintenance

Defense presets can require recurring materials. The first deadline starts when construction completes. While overdue, a defense stops firing and by default loses 1% of its maximum integrity per 24 hours of elapsed time. Decay stops at 15% of maximum integrity. An already damaged defense below 15% stays at its current integrity as far as maintenance decay is concerned. The defense occupies its slot and remains vulnerable to structural damage. Decay includes elapsed time while the server was offline. Open **Multiplayer → Orbit Projects** to deliver upkeep cargo or use eligible sponsor stock. Partial deliveries are saved. Completing all upkeep materials ends decay, restores firing eligibility and starts a fresh interval. Lost integrity stays lost. Maintenance cargo receives no faction payout.

The optional `maintenance` object accepts `intervalSeconds` from 60 to 31,536,000, the same `materials` format as construction, and `decayPercentPerDay` from 0 to 100. Set the decay rate to 0 to disable integrity loss while keeping overdue defenses offline. Presets and existing defense records without upkeep remain maintenance-free. Each defense keeps the upkeep terms saved when it was ordered. Existing defenses ordered before decay rates were configurable use 1% per day and the 15% floor.

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
    "maintenance": {
      "intervalSeconds": 86400,
      "decayPercentPerDay": 0.5,
      "materials": [{ "item": 156, "amount": 20 }]
    },
    "materials": [
      { "item": 156, "amount": 300 },
      { "item": 118, "amount": 120 }
    ]
  }
]
```

Use either `stationModel` with a station model reference or `assembly` with an existing game assembly such as `turret_002_static`. Distances are in metres. Integrity, material costs, scale, timing and combat settings are saved into each project when ordered. Additional station or gate assets can be registered in the same Mod Builder section.
