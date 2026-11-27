# Advanced economy (initial native implementation)

This optional feature affects shared multiplayer NPC shop commodity rows only. Single-player,
co-op campaign, player marketplaces, owned Kaamo storage, equipment and ship stock retain existing rules.

Create `advanced-economy.json` at the root of a mod. Nothing is enabled by default.
Example (replace station and item references with valid station/commodity IDs or mod keys):

```json
{
  "enabled": true,
  "persistentInventory": true,
  "stationProduction": true,
  "stationConsumption": true,
  "tickIntervalSeconds": 900,
  "stockRules": [
    {
      "station": 106,
      "item": 101,
      "initialStock": 100,
      "targetStock": 100,
      "maximumStock": 200,
      "productionPerTick": 15,
      "consumptionPerTick": 5
    }
  ]
}
```

Each row is opt-in; unmanaged rows follow vanilla restocking including rare goods.
The target is a reference for future pricing/trade, not an automatic refill.
The host retains managed stock through vanilla shipments for the lifetime of its session.
Production and consumption use explicit units per tick. Stock is updated from existing
server stock transactions, and the ordinary stock broadcast carries changes to clients.
The initial phase only simulates stations that have generated stock. Sales over a row's
maximum follow the existing shop-cap behaviour: excess is accepted but not retained.
The underlying vanilla credit transaction protocol has not yet been changed.

Mod Builder > Advanced economy edits toggles, interval and existing stock rows.
New rows can be added in the JSON file before loading the project. The root file is
included in multiplayer mod checks and export. Later active enabled configurations
override a duplicate station/item row; their inventory tick interval also wins.
Disabled files contribute nothing. Emergency fuel at the three special stations is
excluded. No configuration is automatically installed or activated.

Next phases: server restart snapshots, global unvisited-station simulation, category
profiles and specialization, server-authoritative price quotes/transactions, demand and
scarcity pricing, abstract shipments, and gate-based frontline war pressure. Do not
activate the advanced economy for production servers until these are validated.
