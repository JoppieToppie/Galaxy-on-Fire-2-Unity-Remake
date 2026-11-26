# NPC faction warfare

Add `npc-faction-war.json` to an active mod to enable the campaign. It is disabled by default.

```json
{
  "enabled": true,
  "shipsPerSide": 12,
  "intervalSeconds": 1200,
  "battleSeconds": 480,
  "coreSystems": ["your_mod:home_system"]
}
```

The interval is a campaign-wide minimum delay between battle starts. In multiplayer the server starts battles in eligible frontier orbits whether or not any player is present, with at most one active engagement per system. In single-player battles start in the player's occupied frontier orbit. Multiplayer battles without an active local fleet use lightweight, symmetric random attrition on the server; a runner entering the orbit spawns only surviving ships and takes over combat. If any player is in space in the battle orbit, both abstract attrition and the automatic resolution countdown pause. Physical combat can still win the battle, regardless of elapsed time. When the last player leaves, the remaining time and attrition schedule resume, without resetting casualties. Unresolved battles still time out after consuming their unoccupied time budget, and server news announces starts and resolved outcomes. The galaxy and orbit maps pulse a crossed-swords marker over active engagements; selecting it shows surviving ships per faction, the remaining background time and whether it is paused. Active battle state is synchronized on changes and when players join; existing persistence rules for captured territory are unchanged. Terran fights Vossk and Nivelian fights Midorian. Frontiers follow gate links, captured footholds, and nearest-neighbour links for systems without a gate route. Ordinary pirate traffic and scripted encounters retain their own rules.

Secure systems and the protected hubs V'ikka, Wolf-Reiser, Weymire, Mido, Loma, and Shima cannot be captured. Mods can add protected systems using their keys or numeric IDs. Losing an entire battle fleet lets the winner hold that orbit. Frontier systems are marked on the star map. The side with more than half the system's eligible orbits controls the system. A tie leaves the system contested. Station models and player-built infrastructure stay in place. New ambient traffic follows the faction holding the orbit.

Players can join a battle by fighting either fleet. Kills affect ordinary reputation and are exempt from the separate PvE bounty count. Timed-out battles leave ownership intact. Single-player saves and profile-enabled multiplayer servers retain territorial control, while servers without profiles retain it for the session.

Host or single-player Lua scripts can listen for `npcFactionBattleResolved(orbit, system, winnerRace, captured)`.

Use `game.on("npcFactionBattleStarted", function(orbit, system, defenderRace, attackerRace, shipsPerSide) ... end)` for a campaign announcement or participation objective. The host or single-player game emits it once when it starts the encounter. Fleet sizes range from 4 to 24 ships per side. The minimum interval ranges from 60 to 86,400 seconds and the battle time limit ranges from 60 to 3,600 seconds.
