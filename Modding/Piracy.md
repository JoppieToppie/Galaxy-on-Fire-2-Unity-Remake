# Multiplayer piracy and faction bounties

Add `piracy.json` to the root of an active mod to opt in. Bounties and equipment drops require a multiplayer server with profiles; protected Secure-system respawns also apply to single-player.

```json
{ "enabled": true, "playerKillsForBounty": 5, "npcKillsForBounty": 100, "shipRecoverySeconds": 1800, "emergencyShip": 10 }
```

Piracy and faction bounties are disabled by default. A mod can set `playerKillsForBounty` (1–1,000) and `npcKillsForBounty` (1–10,000) independently. Each qualifying kill contributes one fraction of its category's threshold. Later active mods override individual thresholds. The rules below use the defaults.

- A pilot becomes a pirate after 30 registered PvP kill points in a rolling four-day window; qualifying Secure-system kills contribute two points, and other qualifying kills contribute one. Outside secure systems, retaliation, active siege opponents, pirates, and the faction diplomacy exemptions below are excluded. Arena matches are excluded everywhere.
- Faction bounties last seven days and require five qualifying player kills or 100 qualifying NPC kills within seven days. Mixed kills combine at 20 NPC kills per player kill. Secure systems register hostile and retaliatory PvP alike and count each PvP kill twice toward a faction bounty.
- Officers can use `/faction rival TAG` and `/faction unrival TAG`. Rivalry exempts fights outside secure systems once both factions declare it. `/faction hostile NAME` and `/faction unhostile NAME` mark a pilot as hostile to your faction. Your members and protected pilots can fight that pilot without registering a kill outside secure systems.
- A lounge diplomat can clear your pending NPC kills with their faction and revoke pirate status. PvP kill history for faction bounties and existing bounties survive the deal.
- `/faction bounties` lists active bounties. A verified player kill claims the target's active bounties and removes them from the board. Outside Secure systems, a pilot who dies with removable, saleable equipment fitted loses one equipped item. In Secure systems, only pilots with active pirate status lose equipment. Security is effective security (including Shima after the pilot's Kaamo siege), not only the initial galaxy table. A protected death does not charge a respawn docking fine; in single-player it saves current progress at the respawn station rather than loading the old autosave or deleting the hardcore run. The rule applies to PvP and other deaths regardless of bounty status. Organized arena matches are exempt. Other pilots can tractor the dropped item using the normal loot controls.

## Ship recovery

On a penalized multiplayer death the server removes the active owned hull, cargo and remaining removable equipment into a durable recovery record. One eligible fitted item is still dropped as wreck loot. A bare emergency hull (the Phantom by default) replaces the flown hull; it has zero trade-in value and cannot be stored as an owned hull. Dying again in the loaner does not create another recoverable hull.

Mods can choose the emergency replacement using `emergencyShip`: a base-game ship index (for example `10`) or a stable custom ship reference such as `"my_mod:rescue_fighter"`. Later active mods override this setting only when they supply it. The server resolves the reference against its loaded ship database. Missing or non-ownable hulls fall back to the Phantom with a warning. The chosen hull remains an emergency loaner, not player property, regardless of its class or value.

The recovery timer is wall-clock time, including while disconnected. Mods can set `shipRecoverySeconds` (1–604,800, default 1,800). At expiry the server first tries the last docked player station, then other eligible player stations, with actual storage permission, item and ship capacity, and dry dock requirements for large hulls. Stored assets remain individually owned. Owned Kaamo Club storage is an alternative if it can accept the hull. Otherwise the surviving fitted equipment is returned to the pilot's hold and the hull's table price plus the cargo's minimum item prices are credited. No refund is paid for the lost equipment or an emergency hull. The storage transaction journal and durable profile queue prevent repeated settlements after reconnect or restart. A per-profile death intent is journalled before assets are removed; an authoritative transaction ID prevents duplicate penalties, and unfinished intents replay before login or profile handover. A missing or stale wreck pose does not waive equipment loss, although there may be no loot crate to recover.

Secure non-pirates and organized arena matches are exempt. Event-loaned hulls are not treated as owned ships.

## Lua hooks

Install these hooks in a mod script running on the host or dedicated server. The piracy option must also be enabled in `piracy.json`.

```lua
game.hook("GameHooks", "PiracyKillThreshold", function(original)
    return original() + 10
end)

game.hook("GameHooks", "PiracyWindowSeconds", function()
    return 5 * 86400
end)

game.on("piracyKillRegistered", function(killerId, victimId, station, count, threshold, windowSeconds)
    game.log("Piracy progress", killerId, count, threshold)
end)

game.on("piracyStatusChanged", function(pilotId, isPirate)
    game.log("Pirate status", pilotId, isPirate)
end)
```

The rules default to 30 kills and 345,600 seconds. The server evaluates both on each qualifying kill while the pilot is building toward pirate status. Nonpositive values use the defaults. The limits are 1,000 kills and 365 days. The hooks affect pirate status. Faction bounty thresholds use their own rules.

Both events fire on the server after saving. The IDs are session client IDs. The kill event fires while the pirate tally is building, including the kill that sets pirate status. The status event also fires when a diplomat revokes pirate status.
