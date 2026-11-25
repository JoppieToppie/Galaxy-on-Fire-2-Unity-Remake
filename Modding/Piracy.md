# Multiplayer piracy and faction bounties

Add `piracy.json` to the root of an active mod to opt in on a multiplayer server with profiles.

```json
{ "enabled": true, "playerKillsForBounty": 5, "npcKillsForBounty": 100 }
```

Piracy and faction bounties are disabled by default. A mod can set `playerKillsForBounty` (1–1,000) and `npcKillsForBounty` (1–10,000) independently. Each qualifying kill contributes one fraction of its category's threshold. Later active mods override individual thresholds. The rules below use the defaults.

- A pilot becomes a pirate after 30 registered PvP kills in a rolling four-day window. Outside secure systems, retaliation, active siege opponents, pirates, and the faction diplomacy exemptions below are excluded. Arena matches are excluded everywhere.
- Faction bounties last seven days and require five qualifying player kills or 100 qualifying NPC kills within seven days. Mixed kills combine at 20 NPC kills per player kill. Secure systems register hostile and retaliatory PvP alike and count each PvP kill twice toward a faction bounty.
- Officers can use `/faction rival TAG` and `/faction unrival TAG`. Rivalry exempts fights outside secure systems once both factions declare it. `/faction hostile NAME` and `/faction unhostile NAME` mark a pilot as hostile to your faction. Your members and protected pilots can fight that pilot without registering a kill outside secure systems.
- A lounge diplomat can clear your pending NPC kills with their faction and revoke pirate status. PvP kill history for faction bounties and existing bounties survive the deal.
- `/faction bounties` lists active bounties. A verified player kill claims the target's active bounties and removes them from the board. A wanted pirate's death drops an equipped item into a salvage container. The container can be tractored using the normal loot controls.

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
