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
Unmanaged rows retain the vanilla credit transaction protocol; managed rows can opt into the server-priced path described below.

Mod Builder > Advanced economy edits toggles, interval, commodity rows, system profiles and routes. Station, system and commodity pickers use normal mod dependencies. Stock rules can use a system reference instead of a station reference to apply to each NPC station in the system. An explicit station rule overrides that mod's system default for the same commodity, irrespective of JSON order. The root file is
included in multiplayer mod checks and export. Later active enabled configurations
override a duplicate station/item row; their inventory tick interval also wins.
Disabled files contribute nothing. Emergency fuel at the three special stations is
excluded. No configuration is automatically installed or activated.

## Optional trade routes and wartime demand

Enable `regionalTrade` to process directed `tradeRoutes` (default false). Both
endpoints must have a managed stock rule for the commodity. Each route links
distinct NPC stations with a reachable path through the jumpgate graph. The source exports surplus above target, capped by capacity
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
classifies frontlines; normal systems have pressure 0, frontline stations default to 50
and stations in active battle systems default to 100. Configure both with frontlinePressurePercent and battlePressurePercent (0–100). Extra consumption scales with pressure.
Enable `tradeDisruption` as well for deterministic losses on shipments
according to `warLossPercent` and the higher endpoint's pressure.
Physical freight is optional (see below); no coordinate-proximity trade routes or offline catch-up.

Enable automaticRoutes to discover suppliers for shortages, using shortest reachable paths with deterministic ties, automaticRouteCapacity, maxAutomaticHops, ticksPerHop and automaticRouteWarLossPercent. Explicit routes also discover multi-hop paths. A route without a path pauses in-flight cargo; path reopening resumes it. Enable playerGateRouting separately for directional operational public gates without access restrictions. Gate topology and war classification are refreshed each tick; the highest route pressure determines departure losses. By default this is abstract transport; physical freight can materialize these shipments in occupied orbits.


## Dynamic pricing and freight

Enable `dynamicPricing` together with `saveInventory` on a persistent profile server.
Only explicitly managed commodity rows use the server-priced transaction path.
The host uses the existing station-specific base price, then adds scarcity, war pressure
and optional weighted import-hop costs, or applies a surplus discount. A configurable
buy/sell spread and marginal per-unit pricing prevent profiting from an immediate bulk
buy-and-resell solely by moving along the stock curve. Each batch quotes the sum of all
unit prices rather than selling or buying the whole batch at the first unit's price. Both the quote and the actual credit/cargo
transfer are checked on the server; changed quotes are rejected and refreshed. The existing
station transaction journal contains the economy snapshot, player balances and faction VAT
targets so interrupted transactions can be replayed without a second debit. Existing player
profile upload validation still applies; this is not a complete rewrite of profile security.

Settings (all feature switches default false): `dynamicPricing`, `transportCosts`,
`scarcityMarkupPercent` (100), `surplusDiscountPercent` (30), `warMarkupPercent` (50),
`transportCostPerHopPercent` (5), `sellSpreadPercent` (10). Prices are bounded between
10% and 2000% of the underlying list price and int range before faction adjustments.
The hangar shows buy/sell quotes, stock target, war pressure and weighted import hops.

## Player marketplace procurement

Enable `playerMarketTrade` alongside dynamic pricing and saveInventory. Set
`playerMarketBudgetPerTick` (default 0 credits) and `playerMarketUnitsPerTick` (100).
A finance-authorized player-station manager must also opt their station in under
Marketplace settings or /station npc-market <id> on|off.

After NPC shipments and production/consumption, NPC stations with shortages can buy
commodity escrow listings from opted-in player stations in the same system. With regionalTrade and automaticRoutes enabled, procurement can also reach markets over the directed public gate graph, within maxAutomaticHops. Imported stock records the route distance. Marketplace procurement is an immediate abstract delivery, not a delayed NPC shipment. Purchases are
bounded by deficit after reserving incoming NPC shipments, per-tick global credit and unit budgets, and the destination's quoted
price including marketplace taxes. Escrow, seller/faction proceeds, station taxes and NPC
stock are journalled together. This is an abstract NPC credit source, not a transfer from
a player treasury. No private storage, factory stock, ship listings or construction escrow
is modified. NPC buyers are anonymous; enabling this specific market channel authorizes
them independently of player-specific access rules. Player-to-player listings remain priced
by their owners. NPC construction supply is separately opt-in; player-built gates are already usable for NPC-to-NPC shipments.

## Verification

Edit-mode tests cover stock bounds, zero balances, routing, demand, shipment losses and
price calculations. Unity compilation, test execution, dedicated-server restart recovery,
client profile handover and multiplayer price race tests are required before enabling the
system on production servers. Offline simulation intentionally pauses.

## NPC construction supply

Enable `npcConstructionSupply` with dynamic pricing and saved inventory, and set `npcConstructionUnitsPerTick` (default 100). NPC stations supply only managed commodity surplus above target, after ordinary trade routes. Funded construction and module buy orders are consent to supply; the bid must cover the marginal source price including route distance. Supply is local unless regional automatic routing is active. The posted bid is consumed from existing escrow as an NPC credit sink. No player seller is credited. The same journal records stock, delivered materials, build funding and any refunds. Deliveries are abstract and immediate. No independent jumpgate construction contribution is supplied.

All regional economy phases share one topology and shortest-path cache per tick. Hangar commodity details show stock relative to target, including the numerical shortage or surplus.

## Restricted player-built gates

Enable `restrictedGateRouting` alongside `regionalTrade` and `playerGateRouting` to let authorized gate managers explicitly allow anonymous abstract NPC commerce through restricted gates. The security tab and `/station jumpgate-npc <orbit> on|off` manage this consent. No player access rule is altered. Consent is tied to the current owner; reclaimed gates clear it. Scrapped, unbuilt, destroyed, EMP-disabled or unlinked gates remain unavailable. Unrestricted public routing continues to work independently.

Profile settlement rejects malformed duplicate/integrated cargo rows on advanced-pricing client uploads, uses saturating worth calculations, and replays absolute cargo targets without duplicating rows. Economy journal snapshots are validated before any account is changed. EditMode regression coverage is provided; actual process-kill and restart tests remain required. The general client-authoritative gameplay income model remains unchanged.

## Release verification checklist

Use disposable persistent server profiles and keep copies of the economy and station registry files. Run EditMode AdvancedEconomyTests before multiplayer testing. In a two-client session, race purchases of the last units, attempt stale quotes, test a funded construction order with a partial fill, cancel the remainder, and compare source stock, escrow and delivered quantities. Disconnect during a pending trade and reconnect; the server's persisted balances must prevail. Interrupt a disposable server after durable journal creation, after profile settlement, after writing the economy snapshot and before journal cleanup, then restart each time; compare final cargo, credits, faction VAT, construction escrow and stock against the same uninterrupted control trade. Test public, restricted/consented, owner-transferred, and EMP-disabled gate paths. Run a long session with the intended number of systems and managed rows and profile the economy tick, including journal disk writes. None of these process-kill or load tests is implied by the EditMode regression coverage.

## Physical freight (persistent multiplayer)

Enable physical freight in the Advanced Economy settings of the Mod Builder, or add these fields to the existing advanced economy configuration:

```json
{
  "persistentInventory": true,
  "saveInventory": true,
  "regionalTrade": true,
  "automaticRoutes": true,
  "physicalFreight": true,
  "automaticRouteCapacity": 2500,
  "rhinoCapacity": 2500,
  "cormorantCapacity": 400,
  "freightPerOrbit": 4,
  "freightSalvagePercent": 25,
  "tickIntervalSeconds": 900,
  "freightDispatchSeconds": 60,
  "freightTravelMinSeconds": 30,
  "freightTravelMaxSeconds": 90,
  "freightUnloadMinSeconds": 15,
  "freightUnloadMaxSeconds": 30,
  "freightWreckLifetimeSeconds": 300,
  "rhinoFreightSpeedPercent": 75,
  "cormorantFreightSpeedPercent": 125
}
```

Physical freight requires a persistent server profile and saved regional inventory. Existing installations retain abstract transport unless opted in. Default Terran traffic mixes native cargo freighters, Rhino (51) and Cormorant (36). Other races default to their native freight model, using the mod race's configured look where relevant. Override the optional `rhinoShip` and `cormorantShip` fields for the default Terran pool, or configure explicit per-race fleets below. NPC freight capacities are independent of purchasable ship stats. A shipment carries a single commodity, with its quantity constrained by supplier surplus, destination deficit, route capacity and the selected hull capacity. Set route capacity and stock targets sufficiently high for bulk loads to occur.

The persistent ledger reserves cargo from suppliers, records freight identity, hull fraction, current orbit, cargo remaining and progress, and credits only surviving deliveries. In occupied orbits a bounded number of freighters materialize via the existing NPC network runner, moving between station and gate/arrival lanes. Materialized legs advance on actual waypoint arrival and EMP recovery; unmaterialized legs advance on their own randomized travel countdown, including systems with no players. Some generic ambient freighters are replaced rather than adding traffic indefinitely. The flight is hybrid: exact gate traversal, docking and spatial position continuity are not simulated across every orbit.

EMP-disabled freight can be tractor-stolen. Destroying a freighter cancels its delivery and creates a recoverable wreck with the configured fraction of remaining cargo. Wrecks expire after the configured lifetime (five minutes by default). Claims are limited by the ledger's cargo, the claimant's available hold capacity and tractor reach, and journalled with persistent profile changes. NPC combat remains orbit-runner simulated under the existing multiplayer model. Player-market procurement and construction orders remain abstract; escorts and physical player-market deliveries are not implemented by this option.

### Race-specific economy fleets

Use Mod Builder → Advanced Economy → Economy traffic fleets by race, or add `freightFleets` rows to `advanced-economy.json`:

```json
{
  "nativeFreighterCapacity": 2500,
  "nativeFreighterSpeedPercent": 100,
  "freightFleets": [
    {"race":"terran","ship":"native","capacity":2500,"weight":3,"speedPercent":100},
    {"race":"terran","ship":"51","capacity":2500,"weight":1,"speedPercent":75},
    {"race":"terran","ship":"36","capacity":400,"weight":2,"speedPercent":125},
    {"race":"vossk","ship":"native","capacity":3500,"weight":1,"speedPercent":90},
    {"race":"nivelian","ship":"native","capacity":2500,"weight":1,"speedPercent":100},
    {"race":"midorian","ship":"native","capacity":3000,"weight":1,"speedPercent":95}
  ]
}
```

Each configured race's rows replace its default pool. Race references accept base race names/numeric IDs or stable mod race keys; ship references accept a content reference or `native` for the original racial cargo ship (Vossk index 13; index 15 uses the corresponding racial cargo assembly for other races). Races without distinct native cargo models borrow the existing game look. Capacity is 1–100000 tonnes, speed is 10–500 percent of normal freighter speed, and relative weight is 1–1000. The server chooses among hulls that can fit the requested cargo using relative weights; if none can, it selects among the largest available hulls and caps the dispatch to that capacity. Smaller loads may use larger hulls. Increase the route capacity too if larger shipments are desired.

The originating station's NPC faction controls the fleet; its race, chosen hull reference and speed remain with the shipment across system changes and restarts. Existing shipments without fleet fields select a hull without re-debiting or discarding their cargo. Removing a selected race or hull mod freezes that physical shipment rather than silently reassigning it. This config covers NPC racial factions, not player-owned factions.

### Independent freight timing

All intervals are configurable in Mod Builder. Station production, consumption, wartime demand and NPC market/construction procurement remain on `tickIntervalSeconds` (900 seconds by default). Freight dispatch checks run every `freightDispatchSeconds` (60 seconds). Each unmaterialized leg chooses an inclusive random delay between `freightTravelMinSeconds` and `freightTravelMaxSeconds` (30–90 seconds). A materialized leg instead advances when its ship reaches the waypoint, provided EMP is not active; speed percentages control the actual flight. At the destination, unloading counts down between `freightUnloadMinSeconds` and `freightUnloadMaxSeconds` (15–30 seconds) once an approach is complete; if no ship materializes it uses the abstract countdown directly. A full destination keeps the remaining cargo in transit until storage becomes available. Cargo salvage expires after `freightWreckLifetimeSeconds` (300 seconds).

Freight timing options accept 1–86400 seconds, with each maximum clamped to at least its corresponding minimum. Station flow keeps its original 60–86400 second range. Travel/unloading progress is persisted as remaining active server seconds, not a wall-clock deadline; offline time does not advance shipments. Legacy snapshots migrate on the next freight pulse without re-debiting stock. The older `travelTicks` and `ticksPerHop` still govern non-physical regional trade, not the independent physical-freight clock.

Suggested multiplayer validation: compare source/destination inventory before and after delivery, theft, destruction and simultaneous salvage attempts; restart during a shipment; transfer orbit ownership; verify both hulls, EMP delays, scaled station/gate approaches and ambient replacement limits.
