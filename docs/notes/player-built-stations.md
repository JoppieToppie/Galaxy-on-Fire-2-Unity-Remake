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

The current implementation persists, replicates and loads station records; starts projects remotely; and accumulates required materials through server-validated deposits. Registry replication now uses revisioned chunks rather than a single 4 KB variable. Construction completion opens the reserved orbit. Project status is available only to the station owner and authorised faction members through `/station status <project id>`; there is no public station directory. Three functional station add-ons are now implemented. Storage provides persistent inventory with deposit/withdraw commands while docked at the station; inventory readouts are restricted to the owner and authorized faction members; the owner can optionally grant access to members of their current faction with `/station access <id> faction`, or restore owner-only access with `/station access <id> owner`. A refinery can be installed alongside storage and converts ten stored units of any supported raw ore into one matching core, using the game's existing ore/core commodity pairs. A factory can also be installed alongside storage and manufactures selected trade commodities from stored inputs in server-validated batches. Its initial recipes are Plastics (2 Chemicals), Mechanical Supplies (2 Iron + 1 Titanium), Optics (1 Electronics + 1 Noble Gas), and Com. Devices (1 Electronics + 1 Microchips); each batch yields one output unit. `/station modules` lists module costs, `/station recipes` lists factory recipes, and `/station manufacture <station id> <recipe id> <batches>` executes 1–100 batches. Factory recipes are currently explicit code-defined recipes rather than data-driven mod recipes; manufacturing is player-triggered, not an offline production timer.

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
- Station storage is attributed to original contributors through the per-profile inventory ledger. Existing legacy stock is assigned to the station's legacy individual owner, and original contributors retain withdrawal rights after a transfer.
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

- Station project status and transfer details are server-authorised and no longer act as a public station directory.


## Player-built jumpgates

Player-built jumpgates occupy an existing orbital anchor in a system, independently of whether a player-built station exists there. The orbit index is the database station index listed by `/station jumpgate-orbits <system id>`. The owner authorises construction by docking at and managing a player-built sponsor station in the same system; the gate itself can be placed at any other valid orbit in that system, including an otherwise empty orbital location. A system can contain multiple player-built gates, but each orbital anchor can contain only one gate. The system's original jumpgate orbit is reserved and cannot receive a player-built gate.

A gate has one fixed destination system. It bypasses the ordinary system jump-route graph and arrives at the destination system's existing jumpgate orbit when one exists, otherwise at that system's first station orbit. Connections are directional: a return route requires a separately built gate in the destination system pointing back.

Construction is server-authoritative and persisted in the station registry's separate jumpgate collection:
- List available and occupied orbital locations with `/station jumpgate-orbits <system id>`.
- Start from the station management UI or `/station jumpgate <sponsor station id> <orbit station index> <destination system id> [build seconds]`.
- The material recipe is 500 Iron, 250 Titanium, 200 Electronics and 150 Microchips.
- Contributions can be deposited while docked at any station using `/station jumpgate-contribute <orbit station index> <item id> <amount>`.
- Individually owned gates use personal material contributions. Faction-owned gates pay each contributor immediately from the owning faction bank at the fixed catalogue minimum value, using the same journalled cargo/credit transaction as station construction.
- A build time of 0 makes the gate operational as soon as it is fully funded. A value from 1 to 2,592,000 seconds starts a persistent UTC timer after the final required material arrives.
- `/station jumpgate-status <orbit station index>` shows destination, material progress, timer state and the next permitted destination-change time.
- The gate owner, or an authorised faction manager for a faction-owned gate, can change its destination once every 24 hours while docked: `/station jumpgate-destination <orbit station index> <destination system id>`. The cooldown is stored per gate and begins when the gate first becomes operational; changing ownership or reconnecting does not reset it.
- The station management window exposes orbit selection, the system's orbital-location list, construction, contributions, status and destination changes.
- The server update loop completes due UTC timers and replicates the updated gate record. If the orbit is already loaded, the gate model, collision and navigation target are materialized in place; players do not need to leave and re-enter.

Existing saves with the earlier station-bound jumpgate fields are migrated into the separate gate collection on server startup. The legacy fields are retained for backward-compatible deserialization but are no longer used for new construction.

The gate is rendered with the system's existing race/mod jumpgate assembly and collision/approach behavior. A player-built gate's destination is fixed at any given time; it does not open the unrestricted gate destination map. The reverse connection is intentionally a separate construction project.

This is source-level implementation. Unity compilation and multiplayer tests remain outstanding, including: multiple gates in different orbits in one system; rejection of a second gate in the same orbit; rejection of the system's original jumpgate orbit; placement at a non-player-station orbit; directed travel and return-gate behavior; 24-hour destination cooldown and persistence across restart; timer completion across restart; initial registry sync and live updates; faction-bank contribution settlement; and compatibility with systems that already have a static jumpgate.

## Follow-up audit (23 November 2026)

- **Unavailable station index preservation:** if a saved station can no longer be placed because its system, orbit list or model preset is missing, the loader appends an inert `StationData` slot at its reserved index. Later player stations therefore retain their original indices instead of colliding with shifted database entries.
- **Construction orbit lifecycle:** an under-construction project reserves its database index but is not added to `SystemData.stations`, so it cannot be visited or docked at before funding completes. Direct material contributions and construction buy-order fulfilment both activate the orbit after the durable transaction commits. Clients add the orbit when the completed registry snapshot arrives.
- **Construction procurement:** buy orders can be created, fulfilled and cancelled while docked at any existing station. Requiring docking at the project itself was incompatible with keeping its orbit closed during construction.
- **Dry-dock recipe validation:** ingredient totals are checked for integer overflow before station storage is debited; the management UI uses 64-bit totals when displaying recipes.
- **Storage ownership normalization:** null and non-positive lots are discarded, and aggregate ownership totals use 64-bit arithmetic to avoid overflow during migration and recovery.
- **Construction escrow and live balances:** direct material contributions and buy-order fulfilment return unused order escrow to its original profile or faction in the same journal as project completion. Ambiguous escrow ownership blocks completion rather than guessing. Credit deltas are sent to the correct online profile controller for buy-order debits, seller payments, contributor payouts and refunds; faction-funded jumpgate contributions use the same live-credit synchronization.
- **Runtime journal recovery:** if a durable station transaction cannot finish because a profile or registry write temporarily fails, the server retries the journal every five seconds during the live session, including after station persistence has entered its faulted state. A successful recovery republishes the authoritative registry snapshot.
- These are source-level changes. The only GitHub Actions workflow builds release server images (release publication or manual dispatch); it does not compile or test this branch, and no runs were recorded for it. Unity compilation, edit-mode tests, multiplayer synchronization, and persistence crash-injection tests remain outstanding.


## Factory production and automation (23 November 2026)

- Factory production is now a persistent timed job rather than an immediate inventory conversion. The current initial duration is 60 seconds per batch; batches scale linearly. Inputs are reserved from the initiating profile's station-storage lots when the job starts, and outputs are credited to that profile only when the job completes.
- A factory runs one production job at a time. A job records its recipe, batch count, operator, output, reserved inputs and UTC start/completion timestamps. Jobs survive server restarts through the station registry.
- One-off manufacture is available to any controlled profile docked at the station, provided that profile owns the required inputs in station storage.
- Repeat orders support continuous production and a fixed total batch count. Each order specifies its cycle size (1–100 batches); fixed orders are removed after the requested total is completed. An order pauses when its owner-profile storage lacks the next cycle's inputs and automatically resumes when enough owned stock is present.
- Repeat-order creation and cancellation are restricted to the individual station owner, or to members of the owning faction who have station-operations permission. The order remains attributed to its creator profile so another player's storage is never silently consumed.
- Commands: `/station repeat <station id> <recipe id> continuous|<total batches> [batches per cycle]` and `/station repeat-cancel <station id> <order id>`.
- The four existing recipes remain unchanged. The recipe catalogue still needs expansion using verified in-game item data; no unverified recipes have been invented.
- The general-purpose factory remains one module type. Recipe definitions are still source-defined rather than fully data-driven, and should be migrated to validated data definitions as the catalogue expands.
- Source-level changes only. Unity compilation, restart recovery, simultaneous storage deposits/withdrawals, owner/faction permission tests, and factory job completion tests remain outstanding.

## Jumpgate lifecycle schema groundwork (23 November 2026)

- Jumpgate records now have independent access-policy, blacklist, construction provenance, scrapped/permanently-destroyed state and destruction-count fields. Access modes are Everyone, OwningFaction and Blacklist; blacklist entries can identify an individual profile or a faction.
- Persistent factory job and repeat-order record types were added to the station registry.
- These jumpgate fields are schema groundwork only. Access enforcement, discovery validation/granting, independent reclamation, salvage cargo drops and destruction hooks still need runtime implementation and tests. Gate upkeep remains disabled/unconfigured.


## Factory and jumpgate implementation pass (23 November 2026)

Factory follow-up:
- Recipe definitions are now held in a mutable server-side catalogue. Mod/server extensions can register additional recipes with `NetPlayerStations.RegisterFactoryRecipe(id, outputItem, outputAmount, (inputItem, inputAmount), ...)`.
- Factory outputs are no longer restricted to the `commodity` item type. A validated recipe may produce any item present in the active item database, including manufactured equipment and other goods.
- The four built-in recipes are unchanged. The wider base-game recipe catalogue still needs to be authored from verified item/blueprint data.

Jumpgate follow-up:
- New construction orders are authorised only for the individual station owner, the owning faction's leader, an assigned station executive, or the station's original builder. The actual issuer determines whether the new gate is individually owned or faction-owned.
- Gate ownership is independent after construction. A later transfer of the sponsoring station no longer transfers the gate.
- Destination selection is checked against the individual owner's saved discoveries or the union of the owning faction's members' saved discoveries. Reclaimed gates require the new claimant/faction to have discovered the selected destination too.
- On interaction, the gate destination is granted to the travelling profile through a server-validated RPC, persisted in the profile account index and merged into the normal `systemVisible` save data. The local galaxy map is updated immediately.
- Access policy supports Everyone, OwningFaction, and Blacklist mode. Profile and faction blacklist entries are checked before faction access, so a faction member can be individually blocked even from their own faction's gate. The gate's actual individual owner bypasses their own blacklist.
- Gate access changes and blacklist edits are exposed through `/station jumpgate-access` and `/station jumpgate-blacklist`. Gate management is independent of station ownership, but is performed while docked at a station in the gate's origin system.
- Jumpgate materials must now be delivered while physically in the construction orbit. Faction-owned gate contributors are paid from the faction bank for personal-cargo contributions.
- First-destruction and second-destruction state transitions are implemented in the server registry. Second destruction creates a 25% salvage manifest; the server waits for the orbit authority and spawns it as persistent multiplayer tractor-beam cargo crates. Scrapped gates can be reclaimed by a faction member on behalf of their faction, or individually by a player who owns an operational individual station. Reclaimers must choose a destination they have discovered. Permanently destroyed gates can only be reconstructed through a new station-issued build order.
- Important integration gap: the registry destruction transition is exposed as `MarkJumpgateDestroyed`, but the existing in-world gate object still has no authoritative damage/destruction hook calling it. The lifecycle and salvage drop are ready behind that hook; this pass does not yet make the gate itself damageable.
- Faction storage is not yet a distinct inventory source in the existing station/faction data model. Current gate contributions debit the delivering player's cargo and pay contributors for faction-owned gates. The no-payment path for materials withdrawn from faction storage must be added alongside a real faction-storage source, rather than guessing provenance from cargo contents.
- Gate upkeep remains disabled and has no recurring debit.

Validation status:
- Source changes are committed to `feature/player-built-stations`.
- No Unity compilation, multiplayer host/client run, profile reconnect test, gate travel test, cargo pickup test or destruction-hook test has been performed. Treat this as an implementation pass requiring Unity validation, not a tested release.

## Player-station destruction and claims

Player stations follow the server-owned jumpgate lifecycle pattern for damage, persistent wreck state and salvage dispatch. The station lifecycle has two destruction states.

- **Partial destruction** leaves a claimable wreck with no cargo drop. A solo player or faction may claim it and rebuild at a reduced construction cost.
- **Full destruction** releases a random selection representing 25% of the construction resources actually used for that station as public pickup cargo. The resource manifest is rolled once and persisted before dispatch so a server restart cannot reroll it.
- **Compensation** is a one-time payout to the owner at destruction for 50% of the combined credit value of the station and all personal belongings still stored there. Marketplace listings, buy orders and payouts are excluded, consistent with the station asset policy. The payout and settled destruction state must use the same durable transaction.
- **Claim transfer.** A solo player or faction takes over the existing wreck claim as soon as it accumulates five minutes of uncontested control. Contested control pauses its progress. If the same challenger regains uncontested control, its timer resumes from the time already earned.
- **Claim exclusivity.** The 30-minute timer starts when the claim is established or transferred and continues through contests. A completed transfer to a different player or faction restarts the full 30-minute timer for the new claimant. When the timer expires, the current claim becomes exclusive even if a contest is active. The current claim timestamp and exclusive lock must survive a server restart.

Server claim timing runs for wrecks opened by the partial-destruction transaction. By default, the first player or faction with uncontested control claims an unclaimed wreck immediately. Control comes from living pilots within 5 km of the wreck in its orbit. Faction members count as one side. Claimants must control a signed-in profile, and other eligible pilots in the radius can still contest their presence. Earned takeover progress is checkpointed once per second during continuous control and when control pauses. It survives absences and restarts without credit for downtime. A contest while the saved challenger is absent, the incumbent regaining uncontested control, or a different challenger taking uncontested control clears that attempt. The current claimant and exclusivity deadline are saved with the station registry.

Operational stations have 300,000 structural integrity. Wrecks and incomplete reconstructions have 120,000. Existing damaged stations retain their remaining integrity percentage when the capacity changes. Station combat uses the exact station mesh and the same weapon-level structural eligibility as jumpgates. Server-validated structural damage can turn an operational station into a wreck. The current owner receives one payout when the station becomes a wreck, in the same journal as the destruction. The catalogue material and item floors and catalogue hull prices determine the payout. A faction-owned station pays its treasury. Marketplace orders, escrow, listings, and payouts are excluded.

Destroying a wreck beyond recovery rolls a public tractor-beam cargo drop from 25% of its delivered construction units and 30% of the aggregate blueprint ingredients of stored hulls with catalogued recipes. The salvage manifest is persisted before dispatch. A destroyed site keeps its orbital index so players can reach outstanding salvage. Docking and station services close when the station becomes a wreck.

The exclusive claimant can use `/station rebuild <wreck id>` near the wreck. Reconstruction costs 50% of a new station's base materials, rounded up per material, and stays in the same orbit at a different position. The claimant can choose a built-in model from the wreck model's race. Use `/station contribute <id> <item id> <amount>` within 5 km of the site to fund it. The incomplete project is physically attackable. If destroyed before completion, it closes permanently and yields no payout, refund, or salvage. Completing the project restores docking and assigns fresh builder rights to the pilot who started the rebuild.


### Station integration update

- Direct contributions and construction buy-order deliveries use the same location and completion checks. Reconstruction deliveries require a living pilot within 5 km of the new construction site. Either delivery path can reopen a completed station.
- Sponsored jumpgates follow station ownership on ordinary transfers and when reconstruction completes. While a station is a wreck or under reconstruction, the gates keep their last owner. Independently reclaimed jumpgates retain their own ownership.
- The station owner or owning faction leader can set a 0–10% marketplace sales tax with `/station tax <id> <percent>`. The buyer pays the tax on top of the item price, rounded up to a whole credit. The station owner or faction treasury receives it in the same journal transaction as the purchase.
- The exclusive wreck claimant can start with `/station rebuild <wreck id> [model index]`. Reconstruction stays in the same orbit and shifts the station between two locations 1.5 km to either side of the original orbit centre. The optional model must be from the wreck model's race.

- The public salvage crate is drawn from the saved wreck manifest. Tractor pickup transfers up to the pilot's cargo room, capped at 500 units per pickup, and reduces the manifest in the same profile transaction. The remaining cargo stays at the wreck through disconnects and restarts.

- Bare hulls can be listed directly from the personal stored fleet. Use `/station list-ship <id> <fleet index> <price>`, `/station market <id>`, `/station buy-ship <id> <listing>` and `/station delist-ship <id> <listing>`. Purchase journals the hull, price and sales tax together.

- The factory catalogue adds Medical Supplies, Explosives, Electronics, Nanotech, Energy Cells and Hydraulics. Existing repeat orders and the mod/server recipe registration API use the same recipe path.

- Faction members can donate jumpgate materials from personal cargo while docked at one of their faction's operational stations. `/station faction-materials` shows the receiving station’s donated stock. The current station owner or members of its owning faction can allocate it via `/station jumpgate-fund-stock <orbit> <item> <amount>` while in that gate's construction orbit. Donating or allocating shared stock pays no personal credits.

- Jumpgate salvage uses the same persistent tractor-pickup transaction as station salvage. Final destruction adds 25% of delivered construction materials, rounded up per item, to the orbit's uncollected salvage. A replacement gate preserves the manifest. Earlier saves with an undispatched drop remain eligible. Saves that had already recorded a dispatch are not reissued automatically.

- Wreck salvage uses server-calculated free cargo space for both stations and gates. The persistent host uses the live hold. A remote pilot's request follows a freshly accepted upload on the same reliable network object. The server uses the active hull, saved kit build, cargo upgrades, mounted compressors and cargo load. It journals only what fits at pickup, with a 500-unit maximum per transfer. An empty or failed transfer leaves salvage available.

- Wreck pickup now uses the current tractor's configured reach from the server's host loadout or the accepted remote profile. The 5 km wreck claim radius still governs contests.
- Disbanding reopens wreck claims owned by that faction and clears its in-progress challenges. Gates not transferred with a sponsor fall back to the former faction leader. Donated stock stays with its receiving station through succession. Materials already committed to a structure remain with that project.
- Server administrators can use `/station retire <destroyed station id>` after the orbit's explosions and salvage have cleared. Retiring releases one station slot and removes the destination from system navigation while retaining its database index. A gate, gate wreck or remaining gate salvage at the same orbit blocks retirement.
- Public jumpgate records omit owner account and faction IDs, construction issuer details, blacklists and individual contribution histories. The server sends management visibility only to the relevant pilot and evaluates actual access itself.

- Legacy faction-wide stock with no recorded destination is assigned to the oldest surviving station owned by that faction. A pool with no surviving recipient remains saved. Station stock can fund projects sponsored by that station.


### Voluntary station retirement

An individual station owner or the owning faction's leader can dock at an operational station and run `/station retire <station id>` to offer it for public claim. The first eligible docked pilot to pay 25,000,000 credits through `/station claim <station id>` becomes its individual owner. The fee is removed from circulation.

While listed, the station remains open for ordinary docking and NPC services. The hangar offers no items or ships, and player modules, private storage and marketplace listings are unavailable. Dry-dock and factory jobs pause. Installed facilities, personal holdings, donated stock, prices and escrow stay with the station. Claiming restores service and resumes the jobs. Sponsored gates transfer with the claim. The usual ownership handover rules apply to builder rights, bans and roles.
