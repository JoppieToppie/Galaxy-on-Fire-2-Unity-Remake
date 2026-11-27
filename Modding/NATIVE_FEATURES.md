# Native mod features: infrastructure, combat and configuration

This reference supplements [the main modding guide](README.md). Use native data fields instead of Lua method patches for functionality already exposed by the game. A feature described here is a capability of this branch, not an instruction to enable it in every mod. Server and clients must use compatible builds and gameplay data. Always validate in the builder and test a fresh multiplayer session when changing gameplay rules.

## Manifest gameplay settings (mod.json)

These keys belong at the top level, alongside `id` and `version`; they are not item stats.

| Key | Default | Meaning and accepted values |
|---|---|---|
| `multipleSecondaryMounts` | false | Allow one secondary item on multiple hardpoints, sharing its ammo pool. |
| `squadFriendlyFire` | false | Removes squad-specific damage immunity where normal PvP rules allow combat. See SQUAD_FRIENDLY_FIRE.md. |
| `fighterNpcShields` | false | Opt ordinary fighter NPCs into rank- and difficulty-dependent shields. Early encounters are not guaranteed shields. |
| `shieldRechargeDelayMs` | omitted | Delay before recharge after damage, milliseconds. Values clamp to -1..60000; -1 selects the default, 5000 with NPC fighter shields enabled, otherwise 0. Explicit 0 requests no delay for this mod. |
| `scannerClassDetectionPercent` | stealth_fighter 50, stealth_bomber 60, all other classes 100 | Map canonical target class IDs to integer percentages 1..1000; only finite sensors use these modifiers. |
| `scannerGeneratorDetectionPercent` | 80 | Passive cloak-generator signature multiplier; explicit integer 1..100, 100 disables the equipment bonus. Omit to retain defaults. Dedicated stealth classes do not stack the generator bonus. |
| `npcScannerDetection` | false | Enables native NPC sensor rules. |
| `npcScannerRangeUnits` | {} | Map canonical observing NPC classes to positive integer game-unit ranges; supplying any entry also enables NPC sensors. Default baseline is 240000 units (12 km), and explicit scripted detection ranges take precedence. |

Boolean opt-ins accumulate across active mods; a false in a later mod does not cancel another mod's true. The largest effective shield delay across mods wins. Later class map entries override earlier entries for the same class. The last explicit generator percentage wins. See SCANNER_DETECTION.md for contacts, turrets, network behavior and tests. Gameplay rules are not merely cosmetic multiplayer exceptions.

Example manifest fragment (add your own id/name/version):
```json
{
  "multipleSecondaryMounts": true,
  "fighterNpcShields": true,
  "shieldRechargeDelayMs": 5000,
  "scannerClassDetectionPercent": {"stealth_fighter": 50, "carrier": 180},
  "scannerGeneratorDetectionPercent": 80,
  "npcScannerDetection": true,
  "npcScannerRangeUnits": {"cruiser": 400000}
}
```

## Native item attributes (items.json)

Place these integer values inside an item entry's `stats` object. Raw attribute numbers in `attributes` are equivalent. Do not set an unrelated attribute just because its name resembles the mechanic. Distances normally use 20 game units per metre; milliseconds are not seconds. Original stat-list aliases are accepted by ItemStats.IdOf, but the readable names are recommended.

| Stat | Attribute | Behavior |
|---|---:|---|
| `beamAimTolerancePercent` | 97 | Beam auto-aim window as a percentage of the original, default 100, clamped 0..1000; 0 disables snapping. This is an integer percentage, not a fractional angle. |
| `projectileSpread` | 98 | Nonnegative integer spread error, 0 perfectly straight. Omitted: autocannon/scatter gun 2, thermo 20, others 0. Not a degree value. |
| `beamReach` | 176 | Explicit positive beam reach in game units; default unconfigured visual reach 30000 (1.5 km). Explicit reach caps ordinary beam target selection; see beamRadius for wide beams. Projectile lifetime/speed are separate. |
| `damageType` | 190 | 0 legacy/unclassified, 1 energy, 2 ballistic. |
| `energyResistance` | 183 | Shield energy resistance, percent 0..100. |
| `ballisticResistance` | 184 | Shield ballistic resistance, percent 0..100; the penetration rule is distinct from energy resistance. |
| `structuralDamage` | 189 | 1 permits damage to player-built structure integrity; EMP effects apply independently of this weapon eligibility. |
| `strictShipClass` | 191 | 1 applies equipment class allow-lists even to an unclassified hull. Requires the applicable class restrictions; not a standalone class list. |
| `fixedBeam` | 192 | Nonzero disables beam target snapping; aim along the actual firing direction. |
| `turretDefenseRole` | 193 | Per-mount defense role: 0 legacy, 1 offensive, 2 balanced, 3 dedicated. |
| `scanClass` | 143 | 1 adds the locked ship's class label independently of hull, health and weapon readouts. |
| `scannerRange` | 194 | Maximum sensor range in game units; absent/0 keeps unlimited legacy detection. |
| `scanLead` | 195 | Positive enables predictive primary weapon PIPs; default off. See PIP_LEADS.md. |

For a precision beam with 14 km reach: `"stats": {"beamReach": 280000, "beamAimTolerancePercent": 5}`. For a non-snapping beam, use `fixedBeam: 1` instead. For a projectile weapon, tune `speed`, `lifetimeMs`, and `projectileSpread` separately; beamReach does not turn it into a beam. Verify close, far, hit and miss reload behavior separately. For the remainder of the stat catalogue and point-defense settings use the main guide's Stats section.

## Player-built stations and gates (playerInfrastructure.json)

One root JSON object, edited by **Player-built stations & gates** in the Mod Builder (including its JSON editor). MCP uses the `playerInfrastructure` singleton kind, or writes the root file with validation. The last active mod supplying the file provides its rules; minimum scale constraints accumulate by taking the maximum across active mods. An omitted file does not disable the feature. Field ranges below are parser validation ranges, not balance recommendations.

| Key | Default | Accepted value / use |
|---|---|---|
| `enabled`, `jumpgatesEnabled` | true | Boolean station construction and gate construction switches. |
| `includeStockStations`, `includeStockJumpgates`, `includeStockOrbitalDefense` | true | Boolean inclusion of built-in model/preset choices. |
| `minimumStationScale`, `minimumJumpgateScale` | 1 | Model scale multiplier, 1..6. |
| `stationModels`, `jumpgateModels` | [] | Up to 256 distinct nonempty string references (up to 128 characters each). |
| `stationModelImages` | {} | Up to 256 mappings from station index or stable station reference to game icon/model reference or a mod texture path. Key max 128, value max 256 characters. |
| `stationBuildSeconds` | 43200 | Construction time in seconds, 0..2592000. |
| `jumpgateBuildSeconds` | 0 | Same range; zero completes without a configured wait. |
| `moduleBuildSeconds` | {} | Map storage, refinery, factory, hangar, drydock, dockarm to 0..2592000 seconds (omitted kinds default to 0). |
| `storageCapacityPerLevel` | 2000 | Capacity per storage level, integer 1..1000000. |
| `storageItemTypes` | 64 | Distinct storage item types, integer 1..4096. |
| `stationPlotClaimPrice` | 20000000 | Credits, integer 0..2147483647. |
| `stationClaimPrice` | 25000000 | Credits, same range. |
| `stationRenamePrice` | 300000 | Credits, same range. |
| `siegeRespawnFactionOfficers`, `siegeRespawnStationAdministrators`, `siegeRespawnOriginalBuilders` | true | Boolean exceptions to siege respawn restrictions. |
| `scrapIntegrity` | 5000 | Scrapped structure integrity, 1..10000000 points. |
| `dryDockIntegrity` | 60000 | Dry dock module integrity, 1..10000000 points. |
| `refineryIntegrity` | 40000 | Refinery module integrity, 1..10000000 points. |
| `orbitalDefenseLimit` | 3 | Per-orbit defense limit, integer 0..12; 0 blocks new orders. |
| `otherOrbitalStructureLimit` | 8 | Per-orbit nondefense limit, integer 0..8. |
| `stockDefenseMaintenanceSeconds` | 604800 | Maintenance interval in seconds, 60..31536000. |
| `stockDefenseDecayPercentPerDay` | 1 | Percent decay per day, 0.001..100. |
| `stockDefenseMinimumIntegrityPercent` | 15 | Decay floor, 15..99 percent. |
| `playerIdentityEnabled` | false | Boolean permanent pilot identity feature. |
| `stationRacePoliciesEnabled`, `stationRaceExemptionsEnabled` | false | Boolean station race policies and exemptions. |
| `jumpgateRacePoliciesEnabled`, `jumpgateRaceExemptionsEnabled` | false | Boolean jumpgate race policies and exemptions. |
| `stationRaceMarketLimitPercent` | 0 | Integer 0..100, market policy limit. |
| `stationRaceRevenueToOwner` | false | Boolean station race-policy revenue to owner. |
| `refineryRecipes` | stock | Up to 256 recipes; provided array replaces defaults. |
| `factoryRecipes` | [] | Up to 256 recipes. |
| `orbitalStructures` | [] | Up to 64 additional/replacement structure definitions. |

### Refinery and factory recipes

Refinery entries contain numeric item IDs `input` and `output` (unique inputs and different input/output); `inputUnits` defaults to 10 and `outputUnits` to 1, both 1..500. Default recipes convert ore IDs 154..164 to cores 165..175, and 217 to 218 at 10:1.

Factory entries contain unique positive integer `id`, numeric item ID `output`, `outputUnits` (1..100000, default 1), and `materials`. Materials are arrays of 1..32 objects with unique nonnegative numeric item IDs `item` and integer `amount` 1..100000. These parsers accept numbers, not mod:id strings for item IDs; ensure the IDs actually resolve on the active build and avoid relying on transient numbering for custom content.

```json
{
  "stationBuildSeconds": 3600,
  "moduleBuildSeconds": {"drydock": 1800, "refinery": 600},
  "refineryRecipes": [{"input": 154, "output": 165, "inputUnits": 10, "outputUnits": 1}],
  "factoryRecipes": [{"id": 1, "output": 118, "outputUnits": 1, "materials": [{"item": 154, "amount": 10}]}]
}
```

### Orbital structure presets

Each preset needs a unique lowercase `id` (letters/digits/hyphens/underscores, max 48), `name` (max 64), and construction `materials` as above. A nondefense structure needs exactly one `assembly` or `stationModel` string, max 128 characters. A defense uses one of sn_sentry_gun_001, sn_sentry_gun_002, sn_sentry_gun_003 (invalid or missing choice falls back to the first); its model scale is fixed at 17.5, and stationModel is ignored. A custom id matching a stock id replaces that preset.

| Field | Default | Limits |
|---|---|---|
| `defense` | false | Boolean. |
| `modelScale` | 1 | Nondefense 0.01..10; defense fixed. |
| `hitRadius` | 250 | 10..2000. |
| `integrity` | 12000 | 1..10000000 points. |
| `buildSeconds` | 0 | 0..2592000 seconds. |
| `range` | 5000 | Defense only, 50..20000. |
| `damage` | 120 | Defense only, 1..10000. |
| `fireIntervalSeconds` | 3 | 1..120 seconds. |
| `targetPlayers`, `targetNpcs` | true for defense | Boolean. |
| `sponsorRaces` | [] | Up to 256 distinct nonempty race-reference strings; restrict availability by sponsor. |
| `maintenance` | omitted | Optional object on defense presets only. |

`maintenance` takes `intervalSeconds` (60..31536000, required in practice), `decayPercentPerDay` (0..100, default 1), `minimumIntegrityPercent` (15..100, default 15), and `materials`. No maintenance object means no configured maintenance interval on that custom preset. Stock defenses have their own default maintenance schedule.

```json
{
  "orbitalDefenseLimit": 3,
  "orbitalStructures": [{
    "id": "frontier-sentry",
    "name": "Frontier Sentry",
    "defense": true,
    "assembly": "sn_sentry_gun_002",
    "integrity": 18000,
    "range": 5000,
    "damage": 150,
    "fireIntervalSeconds": 3,
    "targetPlayers": true,
    "targetNpcs": true,
    "materials": [{"item": 156, "amount": 200}],
    "maintenance": {
      "intervalSeconds": 604800,
      "decayPercentPerDay": 1,
      "minimumIntegrityPercent": 15,
      "materials": [{"item": 156, "amount": 20}]
    }
  }]
}
```

Check exact orbital distance/scale against the live builder preview; do not assume every field named range uses the item-stat game-unit convention.

### Placement, external docking and construction

Station definitions in stations.json can supply `externalDocks` entries of `{"position":[x,y,z],"rotation":[x,y,z]}` in station-local game units and Euler degrees. Use the station editor's placement preview to verify hull-facing and attachment orientation. External eligibility is independent of the placement: a ship must have an eligible docking role/class and the station a usable exterior attachment. Do not confuse these with ordinary `pads` (position/approach/facing).

The player station management construction UI supports rotating station and gate previews and placing/rotating exterior docking hardpoints; drag colored axis rings, use manual angles/snapping, and inspect the ghost ship and undocking displacement. Red clearance warnings are conservative oriented-bounds/swept checks, not exact triangle tests. Construction transforms are authoritative on the server, survive reconstruction and rotate attached structures with their parent. Old structures without stored orientation retain their original placement. Gate travel visuals use the gate orientation. These are placement controls, not new invented JSON setting names. Test with multiple external hull classes, obstruction, undocking, gate travel, reconstruction, reconnect and older saves.

## Per-hull camera and cockpit (ships.json)

`crosshairOffsetX` and `crosshairOffsetY` are finite metre values (positive right/up), clamped to +/-1000. Set per hull, not as player settings. First-person uses zero targeting offset; classified large ships use camera-centered free-look targeting, classified small ships do not, and unclassified hulls retain vanilla option behavior. `camera.orbitDistance` is in game units, default 3800, clamped 1500..70000; classified large hulls have an effective minimum of 50000. `cockpitModel` is an existing mod-relative GLB path or an empty string; `cockpit` is the local seat point in game units (original mount axes: +x pilot's left, +y up, +z nose).

```json
{"override": 10, "crosshairOffsetY": 12, "camera": {"orbitDistance": 50000}, "cockpit": [0, 500, 1000]}
```

Example is illustrative, not a recommended Phantom configuration. The cockpit model is separate from the exterior hull. Check both chase and cockpit aiming and target selection in free look after changing a large hull.

## NPC faction warfare, piracy and jump drives

Use the dedicated guides NpcFactionWar.md, Piracy.md, JUMP_DRIVE_RULES.md, SCANNER_DETECTION.md, PIP_LEADS.md and SQUAD_FRIENDLY_FIRE.md. They are available individually through the same in-game guide and MCP. Native APIs and JSON are preferable to global method interception; Lua hooks remain useful where the documentation explicitly exposes a hook. Do not enable overlapping patches that also replace native behavior.

Additional faction-war configuration in npc-faction-war.json: `pirateMoveMin` (600, clamp 60..3600 seconds), `pirateMoveMax` (1200, clamp min..7200), `pirateEscapeSeconds` (12, clamp 3..120), `pirateGuardCount` (0, clamp 0..24), `pirateCapitalShip` (-1, numeric ship index). The root file takes precedence over the old staged text/npc-faction-war.json path.

Additional piracy settings in piracy.json: `maximumNpcBounty` defaults to 300000 credits, `npcBountyIncrement` to 10000 credits (both positive integers, minimum 1); the actual default NPC threshold in ModPvpPiracy.Apply is 5, not the illustrative 100 in the example. Set 100 explicitly if that balance is desired.

## Maintenance and verification

Source references: ModManifest.Read, ModContent, ItemStats, CombatRadar, Gun, WeaponSystem, ModPlayerInfrastructure.Parse, ModShipView, ModNpcFactionWar.Apply and ModPvpPiracy.Apply. Keep their public data contracts and this reference in step. Do not expose implementation-only variables as mod fields. For any new exposed field, document its JSON location, type, default, bounds, units, effect, multi-mod precedence and multiplayer constraints, and add an example plus a validation test where useful.
