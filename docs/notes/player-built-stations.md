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

A ship hangar add-on is now available. It stores up to 100 hulls from a player's existing personal stored fleet (the profile's Kaamo Club ship list) in the station registry. /station fleet lists the player's own stored hulls; /station store-ship <station id> <fleet index> moves one into a station hangar; /station ships <station id> lists the station hangar; /station retrieve-ship <station id> <hangar index> returns one to that player's personal fleet. Stored hull records retain their existing hull upgrade and equipment data, while custom ship build definitions remain in the owning profile. The existing owner/faction storage-access policy also governs hangar access, but faction members can only move hulls from or to their own profile. The player's currently flown ship is deliberately not eligible: this first implementation moves already-stored ships only, avoiding a live ship swap while docked. Successful transfers refresh the controller's profile from the server so the next periodic upload cannot restore a stale fleet list. This is source-level functionality and still needs Unity compilation plus host/client, faction, save/reload and disconnect testing.

The factory add-on was added in this continuation. It requires storage, has its own profile-backed installation cost, and only produces commodity entries present in the active item database. Manufacturing consumes station storage inputs, checks output stack and total capacity limits before mutation, and rolls back all in-memory changes if the registry save fails. This is a manually triggered first implementation; timed/offline production, recipe configuration through mods, and a factory queue remain future work.\n\nThe station marketplace supports public listing readouts, owner-created listings backed by escrowed station inventory, owner cancellation, and buyer checkout. Checkout requires the buyer to be docked at the station, debits the buyer's saved profile, transfers the goods, credits the listing's seller profile (including offline sellers), and reduces or removes the escrowed listing through the cross-profile transaction journal. The checkout command is `/station buy <station id> <listing index> <amount>`. A graphical station management UI, faction-member ship storage, additional production facilities and factory recipes, and configurable jumpgates remain to be implemented. Item storage supports optional access for the owner's current faction and has a 10,000-unit base capacity.


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


## Dry dock implementation

The station dry dock is a separate add-on from the regular ship hangar. It holds up to 20 large hulls whose mod-defined ship class is Frigate, Freighter, Cruiser or Carrier. Dry-docked hulls are bare: removable equipment and cargo move into the station's shared storage, while hull upgrades remain attached. Retrieval returns the bare hull to the requesting player's personal stored fleet. Faction access follows the existing station hangar permission policy, but each pilot can only move hulls belonging to their own profile.

The dry dock is also a shipyard. It lists unlocked mod ship blueprints that require a dry dock and starts a timed construction job, reserving the complete recipe from station storage when the job begins. The bare hull is added to the dry dock when the server UTC timer expires. A station has one active dry-dock construction slot; the stored-hull capacity is separate. Frigate, Freighter, Cruiser and Carrier blueprints automatically require a dry dock. Other ship blueprints can opt in with `"dryDockOnly": true` in their `blueprints.json` entry. The fields `dryDockOnly`, `consumeBlueprint` and `constructionTimeSeconds` are valid only on ship blueprints. `consumeBlueprint` defaults to `false`; when enabled, the blueprint unlock is consumed when construction starts, and restored if the job cannot be saved. `constructionTimeSeconds` overrides the class default and accepts 1–2,592,000 seconds; 0 or omission uses the class default. Defaults are Frigate 6 hours, Freighter 8 hours, Cruiser 12 hours, Carrier 18 hours, and 2 hours for other dry-dock-only ships. The ordinary blueprint investment and completion paths also reject these blueprints at stations without an installed dry dock.

- Install: `/station install <id> drydock`
- List stored hulls and active construction status: `/station drydock <id>`
- List unlocked buildable ship blueprints: `/station drydock-blueprints <id>`
- Start a timed ship build (blueprint product item ID): `/station drydock-build <id> <blueprint item id>`
- Store an owned hull: `/station drydock-store <id> <fleet index>`
- Retrieve a hull: `/station drydock-retrieve <id> <dock index>`

The dry dock costs 200 Iron, 150 Titanium, 100 Electronics and 80 Microchips. It requires station storage for construction materials and transferred ship inventory. Construction now runs as one persistent timed job per dry dock. The full recipe is reserved from station storage at job start; the completed bare hull is placed in the dock when the server UTC deadline is reached. The job and its timestamps are saved in the station registry and replicated to clients. The server update loop completes due jobs, and overdue jobs are retried if the completion save fails. `NetPlayerStations.GetDryDockVisualStatus(stationId)` exposes a presentation-neutral snapshot for future visuals: installed/operational/building flags, station name, active blueprint item and ship index/name, start/completion UTC timestamps, total duration and remaining seconds, normalized progress, blueprint-consumption state, stored-hull count and capacity. It does not expose private station storage. The snapshot is refreshed from the locally replicated UTC deadline, so a visual countdown can update every frame without polling the server; a null result means the station ID is not in the current registry. The station menu now exposes a DRY DOCK button at player-built stations with an installed dry dock. Its shipyard window lists eligible ship blueprints (including locked entries), shows ship class, unlock state, aggregated recipe requirements and whether the blueprint is consumed, and sends build requests through the existing server command path. While a build is active it displays the current hull, percentage, remaining time and expected local completion time. It supports the station menu's keyboard/controller navigation and a compact phone layout. The server remains authoritative for access, unlock, material and construction validation. This is source-level functionality; Unity compilation and host/client, faction, save/reload, timer-expiry, restart and failure-path tests remain outstanding. Ships must have one of the four explicit mod-defined large classes to be stored as ordinary dry-dock hulls; unclassified original hulls are not automatically treated as capital ships.


## Ownership and stored-hull safeguards (20 November 2026)

- Station records now carry an explicit `ownershipType` (`Individual` or `Faction`) and an `ownerFactionId` field. Missing ownership data in existing JSON records defaults to `Individual`, preserving the existing `ownerProfile` as the owner.
- Station management now recognises the individual owner, or the leader/officers of a faction-owned station. Faction members can use shared storage and hangar facilities on faction-owned stations; individual owners can continue to opt into current-faction sharing. Server-side checks remain authoritative.
- The management window only shows the station at the current location when the local profile is its individual owner or an authorised member of its owning/shared faction. The public station-directory section has been removed.
- Hangar and dry-dock hull records now have parallel owner-profile entries. Existing hulls migrate to the station's individual owner, preventing them from becoming claimable by every faction member. New hulls retain the profile that stored them; only that profile can retrieve them. A ship built in the dry dock is assigned to the profile that initiated the build.
- Ownership transfer, faction-bank proceeds, faction disband resolution and station-asset policy are intentionally pending explicit design decisions. These require deciding how station inventory, installed facilities, active dry-dock jobs, escrowed listings and outstanding seller proceeds behave when the owner changes.
- Source changes have not yet been compiled in Unity or tested in a multiplayer session.


## Transfer policy and contribution ledger (20 November 2026)

- Station structure, installed modules, upgrades and expansion remain attached to the station during ownership changes.
- Construction contributions are recorded by contributor profile and material. They do not grant ownership automatically; the ledger is retained so contributors can agree compensation during a transfer. Automated compensation settlement is not yet implemented.
- Station transfers use a persisted proposal and acceptance flow. The individual owner or current faction leader proposes; the receiving player or receiving faction leader accepts while docked at the station. Proposals expire after seven days and are revalidated against current authority at acceptance. Commands: `/station transfer <id> player|faction <recipient account id>`, `/station transfer-accept <id>`, `/station transfer-cancel <id>`, `/station transfer-status <id>`.
- The command `/station contributions <id>` displays the construction contribution ledger.
- Transfer acceptance changes station ownership only. Stored hulls remain personal property, the active dry-dock builder retains the resulting ship, and marketplace seller-profile claims are preserved.
- Station storage is intended to remain attributable to its original contributors. The per-profile inventory ledger and safe migration/withdrawal path are the next implementation step; until then, transfers should not be treated as production-ready.
- Faction-owned station disband resolution still needs integration with the faction disband/leadership lifecycle. The agreed policy is to require the faction to nominate a successor or recipient before disbanding; a station must never be silently orphaned.
- Marketplace proceeds default to the listing creator. An optional per-listing faction-treasury destination is planned, but must be integrated into the marketplace transaction journal before it can be safely enabled.


## Asset ownership and transfer implementation update (20 November 2026)

The transfer foundation and asset ledgers are now present in source:

- **Station structure:** ownership changes leave the station, installed modules and upgrades in place.
- **Personal ships:** stored hulls remain attached to their original player profile. Ships built in a dry dock belong to the profile that started the build, and the builder must personally own the reserved recipe materials.
- **Station inventory:** storage is tracked by item and contributing profile. Legacy stock without a contributor record is assigned to the station's legacy individual owner. A contributor can view and withdraw only their own goods, including after station ownership changes. Storage deposits require current station access; withdrawal of an already-owned lot only requires docking at the station with the contributing profile.
- **Production:** refinery and factory inputs must belong to the profile operating the facility. Outputs are attributed to that operator. This prevents one member from consuming another contributor's stock.
- **Marketplace escrow:** goods can only be listed by their owner. Listing proceeds default to the seller profile. Sellers who belong to a faction can optionally select `faction` when creating a listing; the sale is then credited to that faction's treasury through the station write-ahead transaction journal. The payout faction is captured on the listing, so later ownership or membership changes do not redirect proceeds. Only the original seller can cancel and reclaim a listing.
- **Construction contributions:** each profile's delivered material quantities are recorded. The ledger is visible to station ownership managers. It is retained across transfers, but negotiated compensation is not yet automatically settled.
- **Transfers:** the proposal/acceptance flow is available through station commands and the management window. The individual owner or faction leader proposes; the receiving player or faction leader accepts while docked. The proposal expires after seven days and authority is revalidated at acceptance.
- **Faction dissolution:** a faction cannot disband or have its last leader leave while it owns player stations or is named as the payout destination for active marketplace listings. Transfer stations and settle/cancel those listings first. A faction leadership handover preserves station ownership because ownership is tied to the faction ID, not the leader's profile.

Useful commands:
- `/station transfer <id> player <profile ID or account name>`
- `/station transfer <id> faction <member profile ID or account name>`
- `/station transfer-accept <id>`
- `/station transfer-cancel <id>`
- `/station transfer-status <id>`
- `/station contributions <id>`
- `/station list-item <id> <item id> <amount> <unit price> [personal|faction]`

These changes are source-level only. Unity compilation, server/client testing, crash recovery tests, ownership-transfer tests, and faction treasury settlement tests remain outstanding.
