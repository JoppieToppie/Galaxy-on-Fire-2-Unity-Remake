# Player-built stations

Player-built stations are a persistent multiplayer feature. The server owns station identity, location, ownership, installed add-ons, and marketplace listings. Clients request actions; the server validates and persists them before publishing changes.

## Existing systems

- `NetTrade` already supports direct trades between two pilots in the same hangar. The station feature adds a player-run marketplace; it should not replace or fork direct trade.
- `NetFactions` and `NetProfiles` provide server-side persistence and atomic backup writes that can be reused.
- `StationData` and `MobileStations` describe static and mod-defined stations. Player stations need stable server IDs and runtime replication rather than new mod data or a mod reload.

## Delivery stages

1. **Persistent registry:** stable IDs and server-saved station records for system/orbit location, owner, add-ons, and listings. Load before players can act; write every accepted state change atomically.
2. **Construction and placement:** validate a build site, cost and materials on the server; create the station and replicate it to players in the orbit and on the map.
3. **Buildable add-ons:** define a small typed module catalog (initially services such as storage and marketplace), server-check prerequisites/costs, save upgrades, and expose them in station UI.
4. **Marketplace:** owner listings reserve station stock; purchases atomically transfer credits and goods through profile-backed balances, and handle disconnects/retries without duplication. Only marketplace listings are new; existing direct trades remain available.
5. **Recovery and limits:** protect records across handovers/restarts, cap per-world stations/listings, and provide server administration for recovery/removal.

The first implementation commit adds the registry schema and startup loading. It does not yet spawn stations or accept construction or marketplace requests.

