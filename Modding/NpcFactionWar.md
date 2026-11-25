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

The interval is a campaign-wide minimum delay between battle starts. Battles start in occupied frontier orbits, with at most one active engagement per system. Terran fights Vossk and Nivelian fights Midorian. Frontiers follow gate links, captured footholds, and nearest-neighbour links for systems without a gate route. Ordinary pirate traffic and scripted encounters retain their own rules.

Secure systems and the protected hubs V'ikka, Wolf-Reiser, Weymire, Mido, Loma, and Shima cannot be captured. Mods can add protected systems using their keys or numeric IDs. Losing an entire battle fleet lets the winner hold that orbit. Frontier systems are marked on the star map. The side with more than half the system's eligible orbits controls the system. A tie leaves the system contested. Station models and player-built infrastructure stay in place. New ambient traffic follows the faction holding the orbit.

Players can join a battle by fighting either fleet. Kills affect ordinary reputation and are exempt from the separate PvE bounty count. Timed-out battles leave ownership intact. Single-player saves and profile-enabled multiplayer servers retain territorial control, while servers without profiles retain it for the session.

Host or single-player Lua scripts can listen for `npcFactionBattleResolved(orbit, system, winnerRace, captured)`.
