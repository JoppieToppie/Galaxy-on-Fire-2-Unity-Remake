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

The current implementation persists, replicates and loads station records; starts projects remotely; and accumulates required materials through server-validated deposits. Construction completion opens the reserved orbit. A project status command is now available through `/station status <project id>`.

## Audit findings (19 November 2026)

- **Replication capacity is undersized:** the registry is carried in one `FixedString4096Bytes` NetworkVariable and explicitly rejected above 4,000 UTF-8 bytes, while the registry permits 256 stations and up to 512 listings per station. Persistence can succeed while clients silently retain an older snapshot. Replace this with versioned chunked replication before expanding station capacity.
- **Load-time truncation — fixed in this continuation:** `Start` and `ReceiveSnapshot` now preserve over-limit station, add-on and listing records and warn instead of silently trimming them. The per-world station cap still prevents new construction once the registry is full.
- **Construction deposits span two durable stores:** cargo is written to the player profile before the station registry is saved; a compensating cargo write is attempted if the registry save fails. This is not a fully atomic transaction and needs a recoverable transaction journal/idempotency key before marketplace transactions are added.
- **No dedicated station tests exist yet.** Add coverage for registry loading, stable station indices, snapshot replication, construction progress, failed writes and reconnect/restart recovery before treating the current implementation as production-safe.
- **The material recipe is currently hard-coded** to item IDs 156 (Iron), 155 (Titanium), 118 (Electronics) and 127 (Microchips). Those IDs were checked against the branch's item database. Recipes and costs should become data-driven as station modules are introduced.

Station UI, storage, production modules, dry docks, the marketplace and configurable jumpgates remain to be implemented.
