# Player-built stations

Player-built stations are a persistent multiplayer feature. The server owns station identity, location, ownership, installed add-ons, and marketplace listings. Clients request actions; the server validates and persists them before publishing changes.

## Existing systems

- `NetTrade` already supports direct trades between two pilots in the same hangar. The station feature adds a player-run marketplace; it should not replace or fork direct trade.
- `NetFactions` and `NetProfiles` provide server-side persistence and atomic backup writes that can be reused.
- `StationData` and `MobileStations` describe static and mod-defined stations. Player stations need stable server IDs and runtime replication rather than new mod data or a mod reload.

## Delivery stages

1. **Persistent registry:** stable IDs and server-saved station records for system/orbit location, owner, add-ons, and listings. Load before players can act; write every accepted state change atomically. Player stations are appended to the runtime database after static and mod stations, then added to their system's station list so existing map and orbit generation can discover them.
2. **Construction and placement:** planning is available while connected without docking. Choose a system with existing orbital locations; each project reserves a new, empty generated orbit. Construction is a persistent project that accepts partial deposits of iron, titanium, electronics and microchips while docked at any station. The orbit becomes dockable when all required materials arrive.
3. **Buildable add-ons:** provide owner and faction-member ship/item storage, a dry dock for capital ships, frigates, freighters and carriers, a refinery and factory for producing common sale goods, and a marketplace. Add-on costs and output are resource-based, server-checked and persisted.
4. **Marketplace:** owners choose which items, ships and other eligible goods to list. Purchases move credits and goods through profile-backed accounts, and handle disconnects/retries without duplication. Existing direct trades remain available as a separate feature.
5. **Jumpgates:** allow multiple player-built gates per system, with one gate in each occupied orbit and a configurable destination system for each gate.
6. **Recovery and limits:** protect projects and modules across handovers/restarts, cap per-world stations/listings, and provide server administration for recovery/removal.

The current implementation persists, replicates and loads station records; starts projects remotely; and accumulates required materials through server-validated deposits. Registry replication now uses revisioned chunks rather than a single 4 KB variable. Construction completion opens the reserved orbit. A station directory and project status are available through `/station list` and `/station status <project id>`. Three functional station add-ons are now implemented. Storage provides persistent inventory with deposit/withdraw commands while docked at the station; inventory readouts are restricted to the owner and authorized faction members; the owner can optionally grant access to members of their current faction with `/station access <id> faction`, or restore owner-only access with `/station access <id> owner`. A refinery can be installed alongside storage and converts ten stored units of any supported raw ore into one matching core, using the game's existing ore/core commodity pairs. A factory can also be installed alongside storage and manufactures selected trade commodities from stored inputs in server-validated batches. Its initial recipes are Plastics (2 Chemicals), Mechanical Supplies (2 Iron + 1 Titanium), Optics (1 Electronics + 1 Noble Gas), and Com. Devices (1 Electronics + 1 Microchips); each batch yields one output unit. `/station modules` lists module costs, `/station recipes` lists factory recipes, and `/station manufacture <station id> <recipe id> <batches>` executes 1–100 batches. Factory recipes are currently explicit code-defined recipes rather than data-driven mod recipes; manufacturing is player-triggered, not an offline production timer.

## Audit findings (19 November 2026)

- **Replication capacity — first implementation added:** `NetState` now publishes the UTF-8 registry as versioned, base64-encoded `NetworkList<FixedString4096Bytes>` chunks. Each entry carries a revision, index and total count; clients assemble and validate a complete revision before passing it to `NetPlayerStations.ReceiveSnapshot`, so intermediate list states are not applied. Chunk payloads are 2,800 bytes and the transport currently caps a snapshot at 1,024 chunks (about 2.8 MB of unencoded registry data). This removes the former 4 KB ceiling, but the new path still needs Unity compilation and host/client tests for initial synchronization, live updates, reconnects, large snapshots and packet fragmentation. The cap should be revisited against real-world registry sizes and the project's transport configuration.
- **Load-time truncation — fixed in this continuation:** `Start` and `ReceiveSnapshot` now preserve over-limit station, add-on and listing records and warn instead of silently trimming them. The per-world station cap still prevents new construction once the registry is full.
- **Construction deposits span two durable stores:** cargo is written to the player profile before the station registry is saved; a compensating cargo write is attempted if the registry save fails. This is not a fully atomic transaction and needs a recoverable transaction journal/idempotency key before marketplace transactions are added.
- **No dedicated station tests exist yet.** Add coverage for registry loading, stable station indices, snapshot replication, construction progress, storage capacity, faction permissions and transfers, refinery recipes and output limits, listing escrow and cancellation, failed writes and reconnect/restart recovery before treating the current implementation as production-safe.
- **The material recipe is currently hard-coded** to item IDs 156 (Iron), 155 (Titanium), 118 (Electronics) and 127 (Microchips). Those IDs were checked against the branch's item database. Recipes and costs should become data-driven as station modules are introduced.

The factory add-on was added in this continuation. It requires storage, has its own profile-backed installation cost, and only produces commodity entries present in the active item database. Manufacturing consumes station storage inputs, checks output stack and total capacity limits before mutation, and rolls back all in-memory changes if the registry save fails. This is a manually triggered first implementation; timed/offline production, recipe configuration through mods, and a factory queue remain future work.\n\nThe station marketplace supports public listing readouts, owner-created listings backed by escrowed station inventory, owner cancellation, and buyer checkout. Checkout requires the buyer to be docked at the station, debits the buyer's saved profile, transfers the goods, credits the listing's seller profile (including offline sellers), and reduces or removes the escrowed listing through the cross-profile transaction journal. The checkout command is `/station buy <station id> <listing index> <amount>`. A graphical station management UI, faction-member ship storage, additional production facilities and factory recipes, dry docks, and configurable jumpgates remain to be implemented. Item storage supports optional access for the owner's current faction and has a 10,000-unit base capacity.


## Transaction journal implementation

- Cross-file station/profile cargo operations now write a durable `player-stations.json.transaction` write-ahead journal containing the intended station-registry snapshot and absolute post-transaction cargo counts.
- The profile side uses absolute cargo targets rather than replaying deltas. Re-running recovery after a crash between the profile write and registry write therefore cannot debit or credit the same cargo twice.
- Startup recovery replays the profile targets, writes the station snapshot, then removes the journal. Ordinary station saves are held while an unresolved journal cannot be recovered, preventing later writes from overwriting the pending intended state.
- Construction contributions, storage deposits/withdrawals, storage-module installation and refinery installation use this coordinator. Their previous compensating-write paths are removed.
- The journal is deliberately single-flight: a second cross-file transaction cannot start until the first is completed or recovered.
- This is source-level implementation only at this point. Unity compilation and dedicated-server/persistent-host crash-injection tests are still required. In particular, test a crash after journal creation, after each profile cargo write, after the registry write, and during journal cleanup. Verify host live cargo and its saved profile remain aligned.


## Marketplace checkout implementation

- Buyer checkout is server-authoritative and requires a controlled profile docked at the player station.
- One journal now coordinates multiple profiles: buyer cargo, buyer credits, seller credits, and the station's post-purchase listing state.
- A fully purchased listing is removed; a partially purchased listing retains its remaining escrow amount.
- The buyer's active game receives credit and item updates; an online seller's active controller receives the credit update. Offline sellers are credited in their saved profile.
- The current client-facing credit update is bounded to the `int` range supported by the existing multiplayer admin RPC, even though stored profile balances are 64-bit.
- Still required: Unity compilation, host/client tests, offline-seller tests, insufficient-credit tests, full/partial listing tests, and crash injection at each journal stage. Buyer cargo-capacity handling also needs gameplay verification.
