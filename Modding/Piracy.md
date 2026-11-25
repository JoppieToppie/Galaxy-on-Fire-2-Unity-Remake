# Multiplayer piracy and faction bounties

Add `piracy.json` to the root of an active mod to opt in on a multiplayer server with profiles.

```json
{ "enabled": true }
```

Piracy and faction bounties are disabled by default.

- A pilot becomes a pirate after 30 registered PvP kills in a rolling four-day window. Outside secure systems, retaliation, active siege opponents, pirates, and the faction diplomacy exemptions below are excluded. Arena matches are excluded everywhere.
- Faction bounties last seven days and require five qualifying player kills or 100 qualifying NPC kills within seven days. Mixed kills combine at 20 NPC kills per player kill. Secure systems register hostile and retaliatory PvP alike and count each PvP kill twice toward a faction bounty.
- Officers can use `/faction rival TAG` and `/faction unrival TAG`. Rivalry exempts fights outside secure systems once both factions declare it. `/faction hostile NAME` and `/faction unhostile NAME` mark a pilot as hostile to your faction. Your members and protected pilots can fight that pilot without registering a kill outside secure systems.
- A lounge diplomat can clear your pending NPC kills with their faction and revoke pirate status. PvP kill history for faction bounties and existing bounties survive the deal.
- `/faction bounties` lists active bounties. A verified player kill claims the target's active bounties and removes them from the board. A wanted pirate's death drops an equipped item into a salvage container. The container can be tractored using the normal loot controls.
