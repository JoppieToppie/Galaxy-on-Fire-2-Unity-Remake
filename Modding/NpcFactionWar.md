# NPC faction warfare

Add `npc-faction-war.json` to an active mod to enable the campaign. It is disabled by default.

```json
{
  "enabled": true,
  "shipsPerSide": 12,
  "intervalSeconds": 1200,
  "battleSeconds": 480,
  "doubleSidedDamageThresholdPercent": 150,
  "coreSystems": ["your_mod:home_system"],
  "frontlineSystems": [3],
  "excludedSystems": [30, 31],
  "dynamicFrontlines": true,
  "gateOnlyFrontlines": false,
  "unprotectedDefaultCores": []
}
```

The interval is a campaign-wide minimum delay between battle starts. In multiplayer the server starts battles in eligible frontier orbits whether or not any player is present, with at most one active engagement per system. In single-player battles start in the player's occupied frontier orbit. Multiplayer battles without an active local fleet use lightweight, symmetric random attrition on the server; a runner entering the orbit spawns only surviving ships and takes over combat. If any player is in space in the battle orbit, both abstract attrition and the automatic resolution countdown pause. Physical combat can still win the battle, regardless of elapsed time. When the last player leaves, the remaining time and attrition schedule resume, without resetting casualties. Unresolved battles still time out after consuming their unoccupied time budget, and server news announces starts and resolved outcomes. The galaxy and orbit maps pulse a crossed-swords marker over active engagements; selecting it shows surviving ships per faction, the remaining background time and whether it is paused. Active battle state is synchronized on changes and when players join; existing persistence rules for captured territory are unchanged. By default Terran fights Vossk and Nivelian fights Midorian. Mods can add other war pairs, including custom factions. Frontiers follow gate links, captured footholds, and nearest-neighbour links for systems without a gate route. Ordinary pirate traffic and scripted encounters retain their own rules.

Maximum-security systems cannot be captured. V'ikka, Wolf-Reiser, Weymire, Mido, Loma, and Shima are protected by default. An explicit frontline can override default hub protection (but never maximum security). Mods can add protected systems using `coreSystems`. Losing an entire battle fleet lets the winner hold that orbit. Frontier systems are marked on the star map. The side with more than half the system's eligible orbits controls the system. A tie leaves the system contested. Station models and player-built infrastructure stay in place. New ambient traffic follows the faction holding the orbit.

Players can join a battle by fighting either fleet. Kills affect ordinary reputation and are exempt from the separate PvE bounty count. Timed-out battles leave ownership intact. Single-player saves and profile-enabled multiplayer servers retain territorial control, while servers without profiles retain it for the session.

Host or single-player Lua scripts can listen for `npcFactionBattleResolved(orbit, system, winnerRace, captured)`.

Use `game.on("npcFactionBattleStarted", function(orbit, system, defenderRace, attackerRace, shipsPerSide) ... end)` for a campaign announcement or participation objective. The host or single-player game emits it once when it starts the encounter. Fleet sizes range from 4 to 100 ships per side (200 battle ships total, before ambient traffic). Large fleets increase orbit-runner CPU and network load; profile before deploying them. Server and clients must use the same build because the battle casualty RPC and event-tag spacing changed. The minimum interval ranges from 60 to 86,400 seconds and the battle time limit ranges from 60 to 3,600 seconds.

## Custom factions and battleship fleets

Configure `warPairs` as an array of two-element arrays of race names, IDs or stable mod race keys. Pairs are symmetric. Set `includeDefaultWars: false` with an explicit `warPairs` array to replace the inherited pair set; otherwise pairs accumulate. Declared pairs also make the factions hostile in NPC combat while warfare is enabled. A faction may have multiple opponents; only opponents with an eligible frontline can attack. All existing core/security restrictions still apply.

```json
{
  "enabled": true,
  "warPairs": [["your_mod:grey", "your_mod:octopod"], ["terran", "your_mod:grey"]],
  "battleships": {
    "0": {"chancePercent": 80, "hulls": ["native_terran"]},
    "1": {"chancePercent": 80, "hulls": ["native_vossk"]},
    "your_mod:grey": {"chancePercent": 100, "hulls": [{"ship":"your_mod:cruiser", "weight":3}, {"ship":"your_mod:dreadnought", "weight":1}]}
  }
}
```

Each faction's battleship pool replaces earlier entries for that faction; absence means no battleship. A chance of 100 guarantees one hull from the selection; 0 disables it. Hull references are ship IDs or mod keys, or `native_terran`/`native_vossk` for the original NPC capital types (including their NPC type overrides). Weights default to 1. The server chooses once when the engagement starts, independently for each side; a battleship replaces slot zero, counts toward the fleet total and cannot reroll when the orbit is reopened. Turrets are not roster members. The hull uses the native capital spawn path, no docking or additional launch waves, and no separate capital loot. For custom hulls configure their turret mounts/subsystems. Victory reward entries also accept race names and mod race keys; persistent faction ownership and reward records store stable custom race keys. Mod Builder exposes pair, chance and hull controls. Restart and use matching server/client builds.

## Mod-defined frontlines

In the Mod Builder choose **Menu > NPC faction warfare**. Select systems by name in the three lists; the file is saved with the project and participates in undo/redo.

- `frontlineSystems`: additional eligible frontlines, even without hostile gate neighbours. They use the usual opposing faction pair and require eligible orbits. Explicit entries override built-in hub protection only.
- `excludedSystems`: never eligible for new territorial battles or the frontline marker, even if explicitly included.
- `dynamicFrontlines`: defaults to true. When false, only explicitly listed frontlines may host new battles; when true the normal frontier algorithm is also used.
- `gateOnlyFrontlines`: defaults to false for compatibility. When true, dynamic frontlines use gate links and existing captured footholds, never coordinate-based nearest neighbours. Explicit frontlines still work.
- `unprotectedDefaultCores`: removes built-in hub protection without forcing a system to become a frontline. Explicit cores, exclusions and maximum security still win.
- `coreSystems`: explicit protection always wins, as does security level 3. All four lists accept numeric IDs or mod system keys. Lists accumulate across active mods; the last explicit dynamic and gate-only settings win.

These rules govern new engagements. Restart the session after changing rules so previously spawned fleets cannot finish under the old settings. Existing territory is not erased by changing the dynamic setting. The example above is illustrative, not a change to the default campaign.


## Multiplayer capture rewards

The server freezes a credit pool and a loot list when an attacking NPC faction captures an orbit. By default (legacy), credit pools are shared equally across eligible contributors, with remainder credits assigned in stable account order, and each pilot receives the full configured equipment list. Set `distribution` to `split` to share both credits and each loot quantity, or `flat` to award the full credits and loot to every eligible pilot. A pilot must have killed or recently hit a destroyed defender, and must have more such contributions against defenders than attackers. A pilot who removes at least the configured `doubleSidedDamageThresholdPercent` of hull from **each** fleet in the same battle is disqualified, regardless of kill count or reward distribution: each hit adds the actual hull lost divided by the victim's maximum hull. Shield/armor damage, overkill, NPC-inflicted damage, and damage to or kills of other players do not count. Nonlethal hits count toward disqualification. Set the threshold under **Menu > NPC faction warfare > Victory rewards** (0–10,000%, default 150% = 1.5 ships per side). Zero disables only double-sided damage disqualification; the ordinary contribution requirement and PvP exclusions remain. The server keeps both totals by reward account for the battle; disqualified pilots receive no credits, loot, or qualifying-capture increment. Unoccupied simulated casualties, spectators and repelled attacks do not create rewards. A pilot who leaves before the victory can still qualify if they contributed.

Claim by flying into the captured station's hangar after victory. A different station, simply loading a docked profile or a death respawn does not redeem the reward. The entitlement is retained until claimed or until the orbit changes hands again. Shares are never redistributed. Persistent profile servers save the private entitlements and atomically write the credits, cargo and reward receipt; reconnecting cannot pay twice. If cargo space is insufficient, make room and remain docked or dock again. Profile-less sessions use an in-memory entitlement and apply rewards to the docked player's cargo. Guests on a profile server are not eligible. Single-player territorial battles do not yet use this multiplayer reward path.

Configure rewards in **Menu > NPC faction warfare > Victory rewards** or in `npc-faction-war.json`:

```json
{
  "enabled": true,
  "rewards": {
    "default": {
      "credits": 100000,
      "loot": []
    },
    "0": {
      "credits": 250000,
      "loot": [
        { "item": "your_mod:terran_reward_equipment", "amount": 1 }
      ]
    },
    "1": {
      "loot": [
        { "item": "your_mod:vossk_reward_equipment", "amount": 2 }
      ]
    },
    "2": { "credits": 150000 },
    "3": { "loot": [] }
  }
}
```

IDs 0, 1, 2 and 3 are Terran, Vossk, Nivelian and Midorian respectively. Omitted faction values inherit defaults; an explicit empty loot array disables loot for that faction. Item references can be base-game numeric IDs or mod item keys. The built-in credit pool is 100,000 and the built-in loot list is empty; mods choose real items. The sample keys are illustrative.

### Personal reward scaling

Each default or faction reward can set `reputationBonusPercent` (bonus at +100 earned reputation, linearly interpolated from zero), `participationBonusPercent` (bonus per prior qualifying capture for that faction), `participationBonusCapPercent` (default 100), and `scaleLoot` (default false). Bonus percentages add, apply after distribution, and round down to whole credits/items. The two bonus rates default to zero; all percentage settings accept 0–1000. Positive bonuses increase the total paid even in split mode. Reputation is sampled at the player's last qualifying contribution using the existing replicated standing; prior qualifying captures count once per account and faction, regardless of claim, and persist on profile servers. Session-only counts are lost on restart. Existing profiles start at zero; historical captures are not reconstructed. Per-faction settings inherit omitted fields.

### Equipment chances and unlock tiers

Each loot entry can independently configure `chancePercent` (default 100), `minReputation` (default -100), `minPriorVictories` (default 0), `reputationChanceBonusPercent` (default 0), `victoryChanceBonusPercent` (default 0), `victoryChanceCapPercent` (default 100), and optional `scaleQuantity` (inherits reward-level `scaleLoot` when omitted). Both minimum requirements must be met. Chance bonuses are additive percentage points, independent of credit/quantity bonus rates, and final chance is capped at 100%. Reputation uses positive earned standing only for chance bonuses; thresholds can include negative standing. Prior victories exclude the current capture. Tier rows are additive, not replacements for lower tiers; a zero base chance can increase with standing or prior wins. Each entry rolls once per qualified player on the server at capture, never at claim. Flat and legacy grant the configured amount to successful rolls; split divides the item pool among successful rolls, randomly allocating scarce gated/probabilistic remainder items. Quantity bonuses apply after distribution when enabled. Existing entries without new fields remain guaranteed and unchanged.

For example, a rare item can require 60 reputation and 10 prior victories, with a 10% base chance, +20 points at maximum reputation and +2 points per prior victory capped at +30. Set `scaleQuantity: false` to prevent duplicate rare items from quantity bonuses. All fields are exposed under the Mod Builder victory reward equipment entries.

### First-time reward tutorial

Client-side events: `npcFactionWarRewardAvailable(orbit, winnerRace, credits)` is raised for eligible connected pilots on capture; `npcFactionWarRewardClaimed(orbit, winnerRace, credits)` is raised after delivery. These events also drive data-only tutorials, without Lua approval. For example, add to `tutorials/gameplay.json`:

```json
{
  "tutorials": [
    {
      "id": "orbit_victory_reward",
      "title": "Faction victory rewards",
      "text": "Help a faction capture an orbit by destroying enemy ships or assisting in their destruction. Your share of the credits and any faction equipment is waiting at the captured station. Dock there to collect it before the station changes hands again.",
      "trigger": "npcFactionWarRewardAvailable",
      "presentation": "popup",
      "repeatable": false,
      "persistPending": true,
      "multiplayer": true,
      "category": "Faction warfare"
    }
  ]
}
```
