# Advanced economy (initial native implementation)

This optional feature affects shared multiplayer NPC shop commodity rows only. Single-player,
co-op campaign, player marketplaces, owned Kaamo storage, equipment and ship stock retain existing rules.

Create `advanced-economy.json` at the root of a mod. Nothing is enabled by default.
Example (replace station and item references with valid station/commodity IDs or mod keys):

```json
{
  "enabled": true,
  "persistentInventory": true,
  "saveInventory": true,
  "simulateUnvisitedStations": true,
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
The host retains managed stock through vanilla shipments. With saveInventory enabled on a persistent host or dedicated profile server, balances (including zero and resale provenance) are saved in advanced-economy-stock.json in the server profile folder, with atomic writes and backup recovery. Saving is batched at most every ten seconds and attempted at orderly shutdown. A corrupt primary and backup suspend saving rather than overwrite recovery data. No offline catch-up is simulated.
Production and consumption use explicit units per tick. Stock is updated from existing
server stock transactions, and the ordinary stock broadcast carries changes to clients.
With simulateUnvisitedStations enabled, all configured NPC stations run on one economy tick, even if nobody has visited them. Otherwise only generated shops participate. No vanilla dealer lists are generated for unvisited stations. Sales over a row's
maximum follow the existing shop-cap behaviour: excess is accepted but not retained.
The underlying vanilla credit transaction protocol has not yet been changed.

Mod Builder > Advanced economy edits toggles, interval, commodity rows, system profiles and routes. Station, system and commodity pickers use normal mod dependencies. Stock rules can use a system reference instead of a station reference to apply to each NPC station in the system. An explicit station rule overrides that mod's system default for the same commodity, irrespective of JSON order. The root file is
included in multiplayer mod checks and export. Later active enabled configurations
override a duplicate station/item row; their inventory tick interval also wins.
Disabled files contribute nothing. Emergency fuel at the three special stations is
excluded. No configuration is automatically installed or activated.

## Optional trade routes and wartime demand

Enable `regionalTrade` to process directed `tradeRoutes` (default false). Both
endpoints must have a managed stock rule for the commodity. Each route links
distinct NPC stations in the same system or directly connected by existing
jumpgate links. The source exports surplus above target, capped by capacity
and the destination's shortfall, including reserved incoming cargo.

Example route (replace references with actual station and commodity IDs):
```json
{ "source": 1, "destination": 2, "item": 101, "capacity": 25, "travelTicks": 2, "warLossPercent": 20 }
```

A shipment has a finite quantity, is debited at departure, and arrives after
the specified number of economy ticks. One shipment per route is allowed in
transit. An overfilled destination holds cargo pending room. In-flight cargo
is saved alongside balances. Disabling/removing a route freezes its in-flight
cargo until the same route returns. With unvisited simulation off, both
stations must have generated shops before the route operates.

Enable `warPressure` for category-specific extra demand via each commodity
row's `warDemandPerTick` (default 0). The existing NPC faction campaign
classifies frontlines; normal systems have pressure 0, frontline stations 50
and stations in active battles 100. Extra consumption scales with pressure.
Enable `tradeDisruption` as well for deterministic losses on shipments
according to `warLossPercent` and the higher endpoint's pressure.
No physical convoy, no coordinate-proximity trade routes, no offline catch-up.

Next phases: reusable named production/consumption profiles, server-authoritative price
quotes/transactions, scarcity and logistics pricing, player-station integration
and dynamic player-built gate routing. Do not
activate the advanced economy for production servers until these are validated.
