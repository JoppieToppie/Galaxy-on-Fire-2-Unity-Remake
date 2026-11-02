# Galaxy on Fire 2 Remake: game data reference for modders

Generated from the game's own tables (GoF2 > Build > Modding AI Reference; the Android economy, the Default economy's
prices differ). Use these numbers and names in mod files and event graphs: `base`, `override`, ingredients, `alwaysSoldAt`,
`system`, `looksLike`, `planet`, `sky`, `startStation`, `startShip`, `replaces`, speakers, `textures/<name>.png`,
`music/<name>.ogg`, `sounds/<name>.ogg`, `/spawn` objects... Mod content is referred to by key instead (`mod_id:id`).
Beside this file: `voices.md` (every voice clip with its line), `events.md` (every event graph node with its options and
ports, every command), `missions.md` + `mission.schema.json` (the .gof2mission step format), `guide.md` (the full modders' guide:
every field of every mod file).

Contents: Races, Items, Item categories, Item stats (attribute numbers), Blueprints, Ships, Ship hardpoints, Default
economy, Systems, Skies, Stations, Planets, Story steps, Story speakers, Models and their textures, Backdrop textures,
Music, Sound effects, Event sounds and music, Built-in event graphs.

## Races

0 Terran, 1 Vossk, 2 Nivelian, 3 Midorian, 8 Pirate, 9 Void, 10 Specter. Systems and stations belong to 0-3.
By name (characters, campaigns, event graphs): terran, vossk, nivelian, midorian, pirate, void, specter.

## Items

Type: primary / secondary / turret / equipment / commodity. Tech = tech level (1-10). Occ = occurrence (how often shops
stock it, 0-90). Price = min-max (credits). Stats = every attribute the item has, by the stat name items.json's `stats`
takes; `attr N` = an attribute without a name (set it with `"attributes": { "N": value }`); `vosskOnly` and
`alwaysSoldAt <station>` are items.json fields of their own. Copy them when basing an item on it. BP = has a blueprint
recipe in the original (see Blueprints).

| # | Name | Type | Category | Tech | Occ | Price | Stats | BP |
|---|---|---|---|---|---|---|---|---|
| 0 | Nirai Impulse EX 1 | primary | Laser | 1 | 90 | 400-440 | damage 3, reloadMs 400, lifetimeMs 2000, speed 20, alwaysSoldAt 52 |  |
| 1 | Nirai Impulse EX 2 | primary | Laser | 2 | 70 | 6230-6481 | damage 5, reloadMs 400, lifetimeMs 2000, speed 22, alwaysSoldAt 52 |  |
| 2 | Nirai Charged Pulse | primary | Laser | 3 | 60 | 9510-10461 | damage 6, reloadMs 380, lifetimeMs 2000, speed 20, alwaysSoldAt 52 |  |
| 3 | V'skorr | primary | Laser | 6 | 50 | 8000-8800 | damage 7, reloadMs 500, lifetimeMs 2000, speed 20, vosskOnly, alwaysSoldAt 52 |  |
| 4 | Sh'koom | primary | Laser | 6 | 40 | 13000-13747 | damage 10, reloadMs 600, lifetimeMs 2000, speed 20, vosskOnly, alwaysSoldAt 52 |  |
| 5 | Berger Focus I | primary | Laser | 5 | 40 | 19100-21010 | damage 16, reloadMs 900, lifetimeMs 2200, speed 22, alwaysSoldAt 52 |  |
| 6 | Berger Focus II A1 | primary | Laser | 6 | 30 | 28100-30500 | damage 17, reloadMs 850, lifetimeMs 2200, speed 24, alwaysSoldAt 52 |  |
| 7 | Berger Retribution | primary | Laser | 7 | 20 | 35100-36200 | damage 18, reloadMs 750, lifetimeMs 2200, speed 23, alwaysSoldAt 52 |  |
| 8 | Berger Converge IV | primary | Laser | 8 | 10 | 81100-85300 | damage 28, reloadMs 850, lifetimeMs 2000, speed 22, alwaysSoldAt 52 |  |
| 9 | M6 A1 "Wolf" | primary | Laser | 7 | 30 | 35420-37000 | damage 27, reloadMs 1200, lifetimeMs 2400, speed 20, alwaysSoldAt 52 |  |
| 10 | M6 A2 "Cougar" | primary | Laser | 8 | 20 | 41230-44000 | damage 34, reloadMs 1400, lifetimeMs 2600, speed 20, alwaysSoldAt 52 |  |
| 11 | M6 A3 "Wolverine" | primary | Laser | 9 | 10 | 101629-107979 | damage 51, reloadMs 1500, lifetimeMs 2600, speed 20 | yes |
| 12 | N'saan | primary | Blaster | 1 | 60 | 10580-11638 | damage 8, reloadMs 600, lifetimeMs 1500, speed 14, vosskOnly, alwaysSoldAt 53 |  |
| 13 | K'booskk | primary | Blaster | 2 | 50 | 14500-15950 | damage 8, reloadMs 530, lifetimeMs 1500, speed 14, vosskOnly, alwaysSoldAt 53 |  |
| 14 | Sh'gaal | primary | Blaster | 4 | 40 | 22900-23604 | damage 9, reloadMs 430, lifetimeMs 1000, speed 17, vosskOnly, alwaysSoldAt 53 |  |
| 15 | H'nookk | primary | Blaster | 6 | 15 | 48513-52401 | damage 9, reloadMs 330, lifetimeMs 1500, speed 16, vosskOnly | yes |
| 16 | Luna EMP Mk I | primary | Blaster | 4 | 50 | 4800-5280 | damage 0, empDamage 3, reloadMs 350, lifetimeMs 1200, speed 17, alwaysSoldAt 53 |  |
| 17 | Sol EMP Mk II | primary | Blaster | 5 | 40 | 10500-11550 | damage 0, empDamage 5, reloadMs 450, lifetimeMs 2000, speed 15, alwaysSoldAt 53 |  |
| 18 | Dia EMP Mk III | primary | Blaster | 6 | 30 | 34120-37532 | damage 0, empDamage 8, reloadMs 450, lifetimeMs 2000, speed 17, alwaysSoldAt 53 |  |
| 19 | Gram Blaster | primary | Blaster | 7 | 20 | 38000-40000 | damage 12, reloadMs 300, lifetimeMs 1400, speed 17, alwaysSoldAt 53 |  |
| 20 | Ridil Blaster | primary | Blaster | 8 | 20 | 62000-65000 | damage 12, reloadMs 250, lifetimeMs 1000, speed 18, alwaysSoldAt 53 |  |
| 21 | Tyrfing Blaster | primary | Blaster | 9 | 10 | 89000-94000 | damage 13, reloadMs 220, lifetimeMs 1000, speed 19, alwaysSoldAt 53 |  |
| 22 | Micro Gun MK I | primary | Auto-cannon | 1 | 70 | 440-484 | damage 2, reloadMs 220, lifetimeMs 1000, speed 18, alwaysSoldAt 54 |  |
| 23 | Micro Gun MKII | primary | Auto-cannon | 2 | 60 | 5000-5354 | damage 2, reloadMs 170, lifetimeMs 1000, speed 20, alwaysSoldAt 54 |  |
| 24 | 64MJ Railgun | primary | Auto-cannon | 3 | 50 | 12300-13530 | damage 2, reloadMs 140, lifetimeMs 1200, speed 22, alwaysSoldAt 54 |  |
| 25 | 128MJ Railgun | primary | Auto-cannon | 6 | 30 | 24615-26687 | damage 3, reloadMs 120, lifetimeMs 1400, speed 26 | yes |
| 26 | Scram Cannon | primary | Auto-cannon | 7 | 30 | 42400-45000 | damage 2, reloadMs 100, lifetimeMs 1400, speed 28, alwaysSoldAt 54 |  |
| 27 | Mass Driver MD 10 | primary | Auto-cannon | 8 | 10 | 113519-120463 | damage 4, reloadMs 90, lifetimeMs 1400, speed 30 | yes |
| 28 | Thermic o5 | primary | Thermo | 7 | 30 | 8000-8800 | damage 3, reloadMs 300, lifetimeMs 2000, speed 12, alwaysSoldAt 54 |  |
| 29 | ReHeat o10 | primary | Thermo | 8 | 25 | 34000-34269 | damage 4, reloadMs 170, lifetimeMs 2000, speed 14, alwaysSoldAt 54 |  |
| 30 | MaxHeat o20 | primary | Thermo | 9 | 25 | 81000-82689 | damage 5, reloadMs 150, lifetimeMs 2000, speed 16, alwaysSoldAt 54 |  |
| 31 | G'liissk | secondary | Rocket | 1 | 80 | 20-22 | damage 60, reloadMs 1200, lifetimeMs 1200, speed 12, vosskOnly, alwaysSoldAt 34 |  |
| 32 | Jet Rocket | secondary | Rocket | 3 | 60 | 30-32 | damage 70, reloadMs 900, lifetimeMs 1200, speed 14, alwaysSoldAt 34 |  |
| 33 | Amour Rocket | secondary | Rocket | 5 | 40 | 97-101 | damage 72, empDamage 34, reloadMs 2000, lifetimeMs 1000, speed 16 | yes |
| 34 | EMP Rocket Mk I | secondary | Rocket | 6 | 20 | 50-55 | damage 10, empDamage 45, reloadMs 1000, lifetimeMs 1000, speed 18, alwaysSoldAt 34 |  |
| 35 | EMP Rocket Mk II | secondary | Rocket | 7 | 20 | 65-65 | damage 30, empDamage 60, reloadMs 1000, lifetimeMs 1000, speed 20, alwaysSoldAt 34 |  |
| 36 | Edo | secondary | Missile | 1 | 80 | 90-95 | damage 70, reloadMs 2000, lifetimeMs 6000, speed 8, alwaysSoldAt 34 |  |
| 37 | Intelli Jet | secondary | Missile | 5 | 50 | 259-273 | damage 100, empDamage 50, reloadMs 3000, lifetimeMs 8000, speed 18 | yes |
| 38 | S'koonn | secondary | Missile | 3 | 50 | 170-187 | damage 140, reloadMs 3000, lifetimeMs 8000, speed 10, vosskOnly, alwaysSoldAt 34 |  |
| 39 | Mamba EMP | secondary | Missile | 7 | 20 | 90-98 | damage 0, empDamage 100, reloadMs 3000, lifetimeMs 10000, speed 15, alwaysSoldAt 34 |  |
| 40 | Dephase EMP | secondary | Missile | 8 | 20 | 250-268 | damage 120, empDamage 100, reloadMs 3000, lifetimeMs 10000, speed 17, alwaysSoldAt 34 |  |
| 41 | EMP GL I | secondary | EMP bomb | 4 | 60 | 65-72 | damage 2, empDamage 80, reloadMs 6000, lifetimeMs 6000, speed 7, blastRadius 22000, guided 0, alwaysSoldAt 11 |  |
| 42 | EMP GL II | secondary | EMP bomb | 5 | 40 | 180-198 | damage 2, empDamage 150, reloadMs 6500, lifetimeMs 6500, speed 8, blastRadius 30000, guided 0, alwaysSoldAt 11 |  |
| 43 | EMP GL DX | secondary | EMP bomb | 6 | 20 | 330-360 | damage 4, empDamage 300, reloadMs 5200, lifetimeMs 5200, speed 10, blastRadius 37000, guided 0, alwaysSoldAt 11 |  |
| 44 | AMR Tormentor | secondary | Nuke | 4 | 60 | 800-855 | damage 150, reloadMs 6000, lifetimeMs 6000, speed 6, blastRadius 20000, guided 0, alwaysSoldAt 11 |  |
| 45 | AMR Oppressor | secondary | Nuke | 5 | 40 | 1200-1305 | damage 400, reloadMs 6500, lifetimeMs 6500, speed 7, blastRadius 30000, guided 0, alwaysSoldAt 11 |  |
| 46 | AMR Extinctor | secondary | Nuke | 9 | 10 | 4719-4968 | damage 700, reloadMs 7000, lifetimeMs 7000, speed 9, blastRadius 40000, guided 0 | yes |
| 47 | Hammerhead D1 | turret | Turret | 5 | 60 | 20000-22000 | damage 6, reloadMs 300, lifetimeMs 2000, speed 28, automatic 0, turretTurnSpeed 40, alwaysSoldAt 0 |  |
| 48 | Hammerhead D2A2 | turret | Turret | 6 | 40 | 68000-73769 | damage 10, reloadMs 280, lifetimeMs 2500, speed 32, automatic 0, turretTurnSpeed 60, alwaysSoldAt 0 |  |
| 49 | L'ksaar | turret | Turret | 6 | 20 | 140000-142942 | damage 12, reloadMs 250, lifetimeMs 3000, speed 32, automatic 0, turretTurnSpeed 100, vosskOnly, alwaysSoldAt 0 |  |
| 50 | Targe Shield | equipment | Shield | 2 | 80 | 3750-4125 | shieldCapacity 50, shieldRegenMs 20000, gammaShielding 0, alwaysSoldAt 2 |  |
| 51 | Riot Shield | equipment | Shield | 4 | 60 | 11500-12650 | shieldCapacity 80, shieldRegenMs 46000, gammaShielding 0, alwaysSoldAt 2 |  |
| 52 | H'Belam | equipment | Shield | 6 | 30 | 30750-33825 | shieldCapacity 120, shieldRegenMs 58000, gammaShielding 0, vosskOnly, alwaysSoldAt 2 |  |
| 53 | Beamshield II | equipment | Shield | 8 | 20 | 89500-98450 | shieldCapacity 150, shieldRegenMs 45000, gammaShielding 0, alwaysSoldAt 2 |  |
| 54 | Fluxed Matter Shield | equipment | Shield | 10 | 5 | 263825-279957 | shieldCapacity 220, shieldRegenMs 60000, gammaShielding 0 | yes |
| 55 | E2 Exoclad | equipment | Armor | 2 | 80 | 2250-2475 | armor 40, alwaysSoldAt 4 |  |
| 56 | E4 Ultra Lamina | equipment | Armor | 4 | 60 | 9500-10450 | armor 80, alwaysSoldAt 4 |  |
| 57 | E6 D-X Plating | equipment | Armor | 6 | 40 | 41000-45100 | armor 110, alwaysSoldAt 4 |  |
| 58 | D'iol | equipment | Armor | 8 | 20 | 107500-118250 | armor 160, vosskOnly, alwaysSoldAt 4 |  |
| 59 | T'yol | equipment | Armor | 10 | 5 | 169484-183378 | armor 250, vosskOnly | yes |
| 60 | AMR Saber | secondary | Mine | 3 | 0 | 190-209 | damage 350, reloadMs 1000, lifetimeMs 40000, blastRadius 1000 |  |
| 61 | Neétha EMP | secondary | Mine | 3 | 0 | 340-374 | empDamage 500, reloadMs 3000, lifetimeMs 50000, blastRadius 2200 |  |
| 62 | Ksann'k | secondary | Mine | 3 | 0 | 1400-1500 | damage 700, reloadMs 4000, lifetimeMs 60000, blastRadius 5000 |  |
| 63 | ZMI Optistore | equipment | Compression | 1 | 80 | 2100-2310 | cargoBonus 15, alwaysSoldAt 6 |  |
| 64 | Autopacker 2 | equipment | Compression | 3 | 60 | 4700-5170 | cargoBonus 25, alwaysSoldAt 6 |  |
| 65 | Ultracompact | equipment | Compression | 5 | 40 | 13000-14300 | cargoBonus 40, alwaysSoldAt 6 |  |
| 66 | Shrinker BT | equipment | Compression | 7 | 20 | 24800-27280 | cargoBonus 75, alwaysSoldAt 6 |  |
| 67 | Rhoda Blackhole | equipment | Compression | 10 | 5 | 104550-110700 | cargoBonus 100 | yes |
| 68 | AB-1 "Retractor" | equipment | Tractor beam | 6 | 10 | 150000-154500 | tractorAuto 0, tractorLockMs 4000, alwaysSoldAt 8 |  |
| 69 | AB-2 "Glue Gun" | equipment | Tractor beam | 7 | 8 | 450000-459000 | tractorAuto 0, tractorLockMs 1800, alwaysSoldAt 8 |  |
| 70 | AB-3 "Kingfisher" | equipment | Tractor beam | 8 | 6 | 1125000-1136250 | tractorAuto 1, tractorLockMs 0, alwaysSoldAt 8 |  |
| 71 | Linear Boost | equipment | Booster | 4 | 60 | 4800-5280 | boostSpeed 60, boostRechargeMs 8000, boostDurationMs 3000, alwaysSoldAt 13 |  |
| 72 | Cyclotron Boost | equipment | Booster | 5 | 40 | 10400-11440 | boostSpeed 80, boostRechargeMs 10000, boostDurationMs 4400, alwaysSoldAt 13 |  |
| 73 | Synchrotron Boost | equipment | Booster | 7 | 30 | 20300-21444 | boostSpeed 160, boostRechargeMs 12000, boostDurationMs 5600, alwaysSoldAt 13 |  |
| 74 | Me'al | equipment | Booster | 7 | 20 | 38700-42570 | boostSpeed 200, boostRechargeMs 16000, boostDurationMs 6000, vosskOnly, alwaysSoldAt 13 |  |
| 75 | Ketar Repair Bot | equipment | Repair bot | 4 | 80 | 36250-39875 |  |  |
| 76 | Static Thrust | equipment | Steering nozzle | 1 | 80 | 1200-1320 | agility 20, alwaysSoldAt 13 |  |
| 77 | Pendular Thrust | equipment | Steering nozzle | 3 | 60 | 2800-2824 | agility 40, alwaysSoldAt 13 |  |
| 78 | D'ozzt Thrust | equipment | Steering nozzle | 5 | 40 | 4700-5170 | agility 70, vosskOnly, alwaysSoldAt 13 |  |
| 79 | Mp'zzzm Thrust | equipment | Steering nozzle | 7 | 20 | 16900-17907 | agility 100, vosskOnly, alwaysSoldAt 13 |  |
| 80 | Pulsed Plasma Thrust | equipment | Steering nozzle | 8 | 10 | 28800-29707 | agility 130, alwaysSoldAt 13 |  |
| 81 | Telta Quickscan | equipment | Scanner | 2 | 60 | 460-506 | lockTimeMs 4000, showClassAAsteroids 0, radarShowsCargo 0, alwaysSoldAt 11 |  |
| 82 | Telta Ecoscan | equipment | Scanner | 3 | 40 | 7900-8256 | lockTimeMs 3000, showClassAAsteroids 0, radarShowsCargo 1, alwaysSoldAt 11 |  |
| 83 | Hiroto Proscan | equipment | Scanner | 6 | 20 | 33500-36850 | lockTimeMs 1800, showClassAAsteroids 0, radarShowsCargo 1, alwaysSoldAt 11 |  |
| 84 | Hiroto Ultrascan | equipment | Scanner | 7 | 20 | 175200-179204 | lockTimeMs 1800, showClassAAsteroids 1, radarShowsCargo 1, alwaysSoldAt 11 |  |
| 85 | Khador Drive | equipment | Jump drive | 10 | 0 | 624803-662923 | cruiseChargeMs 5000, energyCells 1 | yes |
| 86 | IMT Extract 1.3 | equipment | Mining laser | 2 | 70 | 480-528 | drillSpeed 30, miningYield 60, alwaysSoldAt 8 |  |
| 87 | IMT Extract 2.7 | equipment | Mining laser | 6 | 50 | 20280-22308 | drillSpeed 40, miningYield 60, alwaysSoldAt 8 |  |
| 88 | K'yuul | equipment | Mining laser | 7 | 30 | 40820-44902 | drillSpeed 50, miningYield 75, vosskOnly, alwaysSoldAt 8 |  |
| 89 | IMT Extract 4.0X | equipment | Mining laser | 8 | 20 | 85020-93522 | drillSpeed 60, miningYield 80, alwaysSoldAt 8 |  |
| 90 | Gunant's Drill | equipment | Mining laser | 10 | 5 | 220426-236114 | drillSpeed 100, miningYield 100 | yes |
| 91 | Small Cabin | equipment | Cabin | 3 | 70 | 2600-2860 | cabins 3, alwaysSoldAt 14 |  |
| 92 | Medium Cabin | equipment | Cabin | 6 | 50 | 5900-6195 | cabins 5, alwaysSoldAt 14 |  |
| 93 | Large Cabin | equipment | Cabin | 7 | 30 | 12800-13440 | cabins 10, alwaysSoldAt 14 |  |
| 94 | Sight Suppressor II | equipment | Cloak | 6 | 30 | 61750-67925 | cloakDurationMs 20000, cloakChargeMs 6500, energyCells 2, alwaysSoldAt 14 |  |
| 95 | U'tool | equipment | Cloak | 7 | 20 | 95000-104500 | cloakDurationMs 10000, cloakChargeMs 2000, energyCells 1, vosskOnly, alwaysSoldAt 14 |  |
| 96 | Yin Co. Shadow Ninja | equipment | Cloak | 10 | 10 | 273204-288941 | cloakDurationMs 40000, cloakChargeMs 3500, energyCells 5 | yes |
| 97 | Drinking Water | commodity | Commodity | 1 | 60 | 10-11 |  |  |
| 98 | Medical Supplies | commodity | Commodity | 2 | 50 | 50-55 | alwaysSoldAt 100 |  |
| 99 | Space Waste | commodity | Commodity | 1 | 70 | 5-6 |  |  |
| 100 | Artefacts | commodity | Commodity | 5 | 10 | 1200-1320 |  |  |
| 101 | Rare Animals | commodity | Commodity | 5 | 10 | 2700-2970 | alwaysSoldAt 106 |  |
| 102 | Rare Plants | commodity | Commodity | 5 | 15 | 3800-4180 | alwaysSoldAt 106 |  |
| 103 | Drugs | commodity | Commodity | 1 | 20 | 20-40 | alwaysSoldAt 106 |  |
| 104 | Luxury | commodity | Commodity | 7 | 20 | 1070-1177 |  |  |
| 105 | Premium Food | commodity | Commodity | 8 | 20 | 30-40 |  |  |
| 106 | Basic Food | commodity | Commodity | 1 | 60 | 3-4 |  |  |
| 107 | Organs | commodity | Commodity | 5 | 30 | 4200-4620 | alwaysSoldAt 106 |  |
| 108 | Vossk Organs | commodity | Commodity | 7 | 7 | 22000-24200 | alwaysSoldAt 106 |  |
| 109 | Buskat | commodity | Commodity | 4 | 6 | 9000-9900 | alwaysSoldAt 106 |  |
| 110 | Collectible Figure | commodity | Commodity | 5 | 2 | 100-1000 |  |  |
| 111 | Digital Watch | commodity | Commodity | 2 | 30 | 5-7 |  |  |
| 112 | Towels | commodity | Commodity | 2 | 25 | 5-7 |  |  |
| 113 | Implants | commodity | Commodity | 8 | 25 | 6450-7095 |  |  |
| 114 | Explosives | commodity | Commodity | 3 | 50 | 370-400 | alwaysSoldAt 106 |  |
| 115 | Documents | commodity | Commodity | 2 | 0 | 1200-1320 |  |  |
| 116 | Secure Container | commodity | Commodity | 5 | 0 | 0-0 |  |  |
| 117 | Secure Cabin | commodity | Commodity | 5 | 0 | 0-0 |  |  |
| 118 | Electronics | commodity | Commodity | 2 | 60 | 400-420 |  |  |
| 119 | Chemicals | commodity | Commodity | 7 | 65 | 130-143 | alwaysSoldAt 100 |  |
| 120 | Plastics | commodity | Commodity | 5 | 50 | 150-165 | alwaysSoldAt 100 |  |
| 121 | Nanotech | commodity | Commodity | 7 | 50 | 875-963 | alwaysSoldAt 100 |  |
| 122 | Energy Cells | commodity | Commodity | 2 | 50 | 1400-1540 |  |  |
| 123 | Biowaste | commodity | Commodity | 1 | 50 | 4-6 |  |  |
| 124 | Radioactive Goods | commodity | Commodity | 3 | 30 | 260-286 | alwaysSoldAt 106 |  |
| 125 | Mechanical Supplies | commodity | Commodity | 4 | 70 | 450-495 |  |  |
| 126 | Noble Gas | commodity | Commodity | 2 | 50 | 360-396 |  |  |
| 127 | Microchips | commodity | Commodity | 7 | 50 | 163-179 | alwaysSoldAt 100 |  |
| 128 | Com. Devices | commodity | Commodity | 5 | 50 | 75-83 |  |  |
| 129 | Optics | commodity | Commodity | 7 | 50 | 400-440 | alwaysSoldAt 100 |  |
| 130 | Hydraulics | commodity | Commodity | 2 | 70 | 270-297 |  |  |
| 131 | Alien Remains | commodity | Commodity | 10 | 0 | 40-44 |  |  |
| 132 | Suteo Liqueur | commodity | Commodity | 1 | 30 | 139-153 |  |  |
| 133 | Pan Whiskey | commodity | Commodity | 1 | 30 | 489-538 |  |  |
| 134 | Behén Wine | commodity | Commodity | 1 | 30 | 789-868 |  |  |
| 135 | V'ikka Moonshine | commodity | Commodity | 1 | 30 | 237-261 |  |  |
| 136 | Eanya Tonic | commodity | Commodity | 1 | 30 | 211-232 |  |  |
| 137 | S'kolptorr Rum | commodity | Commodity | 1 | 30 | 245-270 |  |  |
| 138 | Wolf-Reiser Brandy | commodity | Commodity | 1 | 50 | 99-109 |  |  |
| 139 | Aquila Cocktail | commodity | Commodity | 1 | 30 | 320-352 |  |  |
| 140 | Buntta Apéritif | commodity | Commodity | 1 | 30 | 259-285 |  |  |
| 141 | Weymire Punch | commodity | Commodity | 1 | 30 | 1073-1180 |  |  |
| 142 | Y'mirr Schnaps | commodity | Commodity | 1 | 30 | 89-98 |  |  |
| 143 | Union Draught | commodity | Commodity | 1 | 50 | 105-116 |  |  |
| 144 | Oom'bak Gin | commodity | Commodity | 1 | 50 | 113-124 |  |  |
| 145 | Vulpes Soup | commodity | Commodity | 1 | 30 | 398-438 |  |  |
| 146 | Magnetar Juice | commodity | Commodity | 1 | 30 | 1100-1210 |  |  |
| 147 | Mido Distillate | commodity | Commodity | 1 | 30 | 453-498 |  |  |
| 148 | Prospero Flip | commodity | Commodity | 1 | 30 | 262-288 |  |  |
| 149 | Nesla Brandy | commodity | Commodity | 1 | 30 | 258-284 |  |  |
| 150 | Pescal Inartu Brew | commodity | Commodity | 1 | 30 | 829-912 |  |  |
| 151 | Augmenta Fizz | commodity | Commodity | 1 | 30 | 1125-1238 |  |  |
| 152 | K'ontrr Dishwater | commodity | Commodity | 1 | 30 | 578-636 |  |  |
| 153 | Ni'mrrod Muck | commodity | Commodity | 1 | 30 | 510-561 |  |  |
| 154 | Gold | commodity | Ore | 1 | 20 | 80-88 | alwaysSoldAt 51 |  |
| 155 | Titanium | commodity | Ore | 1 | 30 | 150-165 | alwaysSoldAt 51 |  |
| 156 | Iron | commodity | Ore | 1 | 40 | 10-40 | alwaysSoldAt 51 |  |
| 157 | Orichalzine | commodity | Ore | 1 | 40 | 120-132 | alwaysSoldAt 51 |  |
| 158 | Pyresium | commodity | Ore | 1 | 40 | 80-88 | alwaysSoldAt 51 |  |
| 159 | Sodil | commodity | Ore | 1 | 40 | 70-77 | alwaysSoldAt 51 |  |
| 160 | Doxtrite | commodity | Ore | 1 | 40 | 35-40 | alwaysSoldAt 51 |  |
| 161 | Cesogen | commodity | Ore | 1 | 40 | 44-48 | alwaysSoldAt 51 |  |
| 162 | Perrius | commodity | Ore | 1 | 40 | 38-42 | alwaysSoldAt 51 |  |
| 163 | Hypanium | commodity | Ore | 1 | 40 | 70-77 | alwaysSoldAt 51 |  |
| 164 | Void Crystals | commodity | Ore | 1 | 40 | 40-44 |  |  |
| 165 | Golden Core | commodity | Ore core | 3 | 15 | 414-455 | alwaysSoldAt 50 |  |
| 166 | Titanium Core | commodity | Ore core | 3 | 15 | 800-880 | alwaysSoldAt 50 |  |
| 167 | Iron Core | commodity | Ore core | 3 | 15 | 115-127 | alwaysSoldAt 50 |  |
| 168 | Orichalzin Core | commodity | Ore core | 3 | 15 | 600-660 | alwaysSoldAt 50 |  |
| 169 | Pyresium Core | commodity | Ore core | 3 | 15 | 460-506 | alwaysSoldAt 50 |  |
| 170 | Sodil Core | commodity | Ore core | 3 | 15 | 414-455 | alwaysSoldAt 50 |  |
| 171 | Doxtrit Core | commodity | Ore core | 3 | 15 | 218-240 | alwaysSoldAt 50 |  |
| 172 | Cesogen Core | commodity | Ore core | 3 | 15 | 240-264 | alwaysSoldAt 50 |  |
| 173 | Perrius Core | commodity | Ore core | 3 | 15 | 257-283 | alwaysSoldAt 50 |  |
| 174 | Hypanium Core | commodity | Ore core | 3 | 15 | 380-418 | alwaysSoldAt 50 |  |
| 175 | Void Essence | commodity | Ore core | 3 | 15 | 6500-6500 |  |  |
| 176 | Nirai .50AS | primary | Scatter gun | 6 | 0 | 37000-40700 | damage 13, reloadMs 700, lifetimeMs 2000, speed 22, blastRadius 2800, alwaysSoldAt 105 |  |
| 177 | Berger FlaK 9-9 | primary | Scatter gun | 7 | 0 | 113100-124410 | damage 19, reloadMs 750, lifetimeMs 2100, speed 25, blastRadius 4400, alwaysSoldAt 105 |  |
| 178 | Icarus Heavy AS | primary | Scatter gun | 9 | 0 | 407928-438997 | damage 30, reloadMs 900, lifetimeMs 2600, speed 28, blastRadius 6300 | yes |
| 179 | Liberator | secondary | Nuke | 8 | 0 | 80232-85313 | damage 850, reloadMs 10000, lifetimeMs 20000, speed 10, blastRadius 25000, guided 1 | yes |
| 180 | Berger AGT 20mm | turret | Turret | 6 | 0 | 193500-212850 | damage 4, reloadMs 100, lifetimeMs 2000, speed 32, automatic 1, turretTurnSpeed 100, alwaysSoldAt 105 |  |
| 181 | Skuld AT XR | turret | Turret | 6 | 0 | 338100-371910 | damage 9, reloadMs 190, lifetimeMs 2500, speed 32, automatic 1, turretTurnSpeed 125, alwaysSoldAt 105 |  |
| 182 | HH-AT "Archimedes" | turret | Turret | 6 | 0 | 500144-532463 | damage 8, reloadMs 150, lifetimeMs 3000, speed 32, automatic 1, turretTurnSpeed 150 | yes |
| 183 | Disruptor Laser | primary | Laser | 9 | 0 | 173833-186554 | damage 18, reloadMs 300, lifetimeMs 2000, speed 28 | yes |
| 184 | Rhoda Vortex | equipment | Time Extender | 9 | 0 | 312000-343200 | timeExtenderMs 15000, timeExtenderCooldownMs 30000, alwaysSoldAt 107 |  |
| 185 | Emergency System | equipment | Emergency System | 6 | 0 | 7000-7700 | emergencyMs 10000, alwaysSoldAt 107 |  |
| 186 | Nirai Overdrive | equipment | Weapon Mod | 5 | 0 | 25000-27500 | fireRateFactor 20, damageFactor -10, alwaysSoldAt 107 |  |
| 187 | Nirai Overcharge | equipment | Weapon Mod | 5 | 0 | 25000-27500 | fireRateFactor -10, damageFactor 20, alwaysSoldAt 107 |  |
| 188 | Ketar Repair Bot II | equipment | Repair bot | 7 | 0 | 118000-129800 | alwaysSoldAt 107 |  |
| 189 | Signature: Terran | equipment | Signature | 1 | 0 | 500000-500000 | alwaysSoldAt 107 |  |
| 190 | Signature: Vossk | equipment | Signature | 1 | 0 | 500000-500000 | alwaysSoldAt 107 |  |
| 191 | Signature: Nivelian | equipment | Signature | 1 | 0 | 500000-500000 | alwaysSoldAt 107 |  |
| 192 | Signature: Midorian | equipment | Signature | 1 | 0 | 500000-500000 | alwaysSoldAt 107 |  |
| 193 | SunFire o50 | primary | Thermo | 9 | 0 | 152500-167750 | damage 5, reloadMs 120, lifetimeMs 2200, speed 17 |  |
| 194 | AB-4 "Octopus" | equipment | Tractor beam | 10 | 0 | 3093750-3403125 | tractorAuto 2, tractorLockMs 0, alwaysSoldAt 107 |  |
| 195 | Polytron Boost | equipment | Booster | 8 | 0 | 72500-79750 | boostSpeed 300, boostRechargeMs 16000, boostDurationMs 6000, alwaysSoldAt 107 |  |
| 196 | Spectral Filter SA-1 | equipment | Spectral filter | 9 | 0 | 2300-2530 | showInfo 0, showOnRadar_58 0 |  |
| 197 | Ion Lambda Mk1 | secondary | Ionizing missile | 4 | 0 | 4250-4675 | damage 5, empDamage 0, reloadMs 6000, lifetimeMs 6000, speed 6, blastRadius 10000, guided 0, attr 56 50 |  |
| 198 | PE Proton | turret | Plasma collector | 9 | 0 | 4100-4300 | damage 0, turretTurnSpeed 100, collectorSpeed 16, collectorMagnitude 40, collectorRange 27500 |  |
| 199 | PE Ambipolar-5 | turret | Plasma collector | 9 | 0 | 593750-599688 | damage 0, turretTurnSpeed 150, collectorSpeed 28, collectorMagnitude 80, collectorRange 50000 |  |
| 200 | PE Fusion H2 | turret | Plasma collector | 9 | 0 | 1131000-1142310 | damage 0, turretTurnSpeed 200, collectorSpeed 32, collectorMagnitude 100, collectorRange 60000 |  |
| 201 | Green Plasma | commodity | Plasma | 9 | 0 | 35-39 |  |  |
| 202 | Blue Plasma | commodity | Plasma | 9 | 0 | 40-44 |  |  |
| 203 | Purple Plasma | commodity | Plasma | 9 | 0 | 50-55 |  |  |
| 204 | Red Plasma | commodity | Plasma | 9 | 0 | 80-88 |  |  |
| 205 | Gamma Shield I | equipment | Gamma shield | 8 | 0 | 25000-27000 | gammaShielding 40 |  |
| 206 | Gamma Shield II | equipment | Gamma shield | 8 | 0 | 533256-586582 | gammaShielding 60 | yes |
| 207 | Nirai SPP-C1 | equipment | Repair beam | 8 | 0 | 6200-6700 | beamRange 15000, beamStrength 60, beamTargets 1 |  |
| 208 | Nirai SPP-M50 | equipment | Repair beam | 8 | 0 | 340000-360000 | beamRange 60000, beamStrength 100, beamTargets 3 |  |
| 209 | K'mirkk Toad Mutagen | commodity | Commodity | 10 | 0 | 500000-525000 |  |  |
| 210 | Chromo Plasma | commodity | Commodity | 10 | 0 | 775000-786625 |  | yes |
| 211 | Berger SG-100 | secondary | Sentry Gun | 9 | 0 | 640-704 | damage 9, reloadMs 430, lifetimeMs 1000, speed 22 |  |
| 212 | Berger SG-400 | secondary | Sentry Gun | 9 | 0 | 1200-1320 | damage 11, reloadMs 300, lifetimeMs 1000, speed 25 |  |
| 213 | T'Suum | secondary | Sentry Gun | 9 | 0 | 5100-5300 | damage 14, reloadMs 250, lifetimeMs 1000, speed 28 |  |
| 214 | Shesha | secondary | Cluster missile | 9 | 0 | 310-341 | damage 60, reloadMs 3000, lifetimeMs 8000, speed 8 |  |
| 215 | Garuda-IV | secondary | Cluster missile | 9 | 0 | 670-737 | damage 75, reloadMs 3000, lifetimeMs 10000, speed 9 |  |
| 216 | Patala | secondary | Cluster missile | 9 | 0 | 1300-1430 | damage 90, reloadMs 3000, lifetimeMs 10000, speed 10 |  |
| 217 | Novanium | commodity | Ore | 1 | 0 | 400-430 |  |  |
| 218 | Novanium Core | commodity | Ore core | 3 | 0 | 5000-5150 |  |  |
| 219 | Spectral Filter ST-X | equipment | Spectral filter | 9 | 0 | 462500-467125 | attr 57 1 |  |
| 220 | Spectral Filter Omega | equipment | Spectral filter | 9 | 0 | 916500-925665 | attr 57 1, attr 58 1 |  |
| 221 | Ion Lambda Mk2 | secondary | Ionizing missile | 6 | 0 | 8128-8761 | damage 5, empDamage 0, reloadMs 6000, lifetimeMs 6000, speed 6, blastRadius 15000, guided 0, attr 56 100 | yes |
| 222 | Crimson Drain | equipment | Transfusion beam | 8 | 0 | 8400-8567 | beamRange 20000, beamStrength 50, beamTargets 1 |  |
| 223 | Pandora Leech | equipment | Transfusion beam | 8 | 0 | 508045-545097 | beamRange 10000, beamStrength 100, beamTargets 3 | yes |
| 224 | Matador TS | turret | Turret | 9 | 0 | 470500-476000 | damage 18, reloadMs 200, lifetimeMs 3000, speed 32, automatic 0, turretTurnSpeed 130, alwaysSoldAt 114 |  |
| 225 | Particle Shield | equipment | Shield | 10 | 5 | 738210-780835 | shieldCapacity 380, shieldRegenMs 55000, gammaShielding 0 | yes |
| 226 | Shock Blast | secondary | Shock Blast | 9 | 20 | 7404-7905 | damage 140, empDamage 80, reloadMs 7000, lifetimeMs 7000, speed 0, blastRadius 80000, guided 0 | yes |
| 227 | Phoenix SIS | equipment | Shield Injector | 9 | 0 | 881346-900606 | attr 59 30 | yes |
| 228 | M6 A4 "Raccoon" | primary | Laser | 9 | 0 | 525000-542000 | damage 120, reloadMs 1300, lifetimeMs 2600, speed 22 |  |
| 229 | Dark Matter Laser | primary | Laser | 9 | 0 | 500000-515000 | damage 60, reloadMs 680, lifetimeMs 2000, speed 24 |  |
| 230 | Mass Driver MD 12 | primary | Auto-cannon | 9 | 0 | 405000-415000 | damage 7, reloadMs 100, lifetimeMs 1500, speed 30 |  |
| 231 | Mimung Blaster | primary | Blaster | 9 | 0 | 350000-370000 | damage 16, reloadMs 230, lifetimeMs 1000, speed 20 |  |
| 232 | Fireworks | secondary | Nuke | 1 | 0 | 1550000-1573250 | damage 1, reloadMs 8000, lifetimeMs 8000, speed 12, blastRadius 20000, guided 0 | yes |

## Item categories

A new item keeps its `base` item's category (and works like it): pick a base of the category you want.

| Category # | Name | Type | Items |
|---|---|---|---|
| 0 | Laser | primary | 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 183, 228, 229 |
| 1 | Blaster | primary | 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 231 |
| 2 | Auto-cannon | primary | 22, 23, 24, 25, 26, 27, 230 |
| 3 | Thermo | primary | 28, 29, 30, 193 |
| 4 | Rocket | secondary | 31, 32, 33, 34, 35 |
| 5 | Missile | secondary | 36, 37, 38, 39, 40 |
| 6 | EMP bomb | secondary | 41, 42, 43 |
| 7 | Nuke | secondary | 44, 45, 46, 179, 232 |
| 8 | Turret | turret | 47, 48, 49, 180, 181, 182, 224 |
| 9 | Shield | equipment | 50, 51, 52, 53, 54, 225 |
| 10 | Armor | equipment | 55, 56, 57, 58, 59 |
| 11 | Mine | secondary | 60, 61, 62 |
| 12 | Compression | equipment | 63, 64, 65, 66, 67 |
| 13 | Tractor beam | equipment | 68, 69, 70, 194 |
| 14 | Booster | equipment | 71, 72, 73, 74, 195 |
| 15 | Repair bot | equipment | 75, 188 |
| 16 | Steering nozzle | equipment | 76, 77, 78, 79, 80 |
| 17 | Scanner | equipment | 81, 82, 83, 84 |
| 18 | Jump drive | equipment | 85 |
| 19 | Mining laser | equipment | 86, 87, 88, 89, 90 |
| 20 | Cabin | equipment | 91, 92, 93 |
| 21 | Cloak | equipment | 94, 95, 96 |
| 22 | Commodity | commodity | 97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 125, 126, 127, 128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 148, 149, 150, 151, 152, 153, 209, 210 |
| 23 | Ore | commodity | 154, 155, 156, 157, 158, 159, 160, 161, 162, 163, 164, 217 |
| 24 | Ore core | commodity | 165, 166, 167, 168, 169, 170, 171, 172, 173, 174, 175, 218 |
| 25 | Scatter gun | primary | 176, 177, 178 |
| 26 | Time Extender | equipment | 184 |
| 27 | Emergency System | equipment | 185 |
| 28 | Weapon Mod | equipment | 186, 187 |
| 29 | Signature | equipment | 189, 190, 191, 192 |
| 33 | Spectral filter | equipment | 196, 219, 220 |
| 34 | Ionizing missile | secondary | 197, 221 |
| 35 | Plasma collector | turret | 198, 199, 200 |
| 36 | Plasma | commodity | 201, 202, 203, 204 |
| 37 | Repair beam | equipment | 207, 208 |
| 38 | Gamma shield | equipment | 205, 206 |
| 39 | Sentry Gun | secondary | 211, 212, 213 |
| 40 | Cluster missile | secondary | 214, 215, 216 |
| 41 | Transfusion beam | equipment | 222, 223 |
| 42 | Shock Blast | secondary | 226 |
| 43 | Shield Injector | equipment | 227 |

## Item stats (attribute numbers)

The stat names items.json's `stats` takes and their attribute numbers (`attributes` takes the numbers). 100+ are the
remake's own. The original's attributes without a name: 56, 57, 58, 59 (set only through raw
`attributes`); 60 = Vossk-made (`vosskOnly`), 61 = always sold at that station (`alwaysSoldAt`).

| Attr | Stat name | Used by items |
|---|---|---|
| 9 | damage | 79 items |
| 10 | empDamage | 16, 17, 18, 33, 34, 35, 37, 39, 40, 41, 42, 43, 61, 197, 221, 226 |
| 11 | reloadMs | 77 items |
| 12 | lifetimeMs | 77 items |
| 13 | speed | 74 items |
| 14 | blastRadius | 41, 42, 43, 44, 45, 46, 60, 61, 62, 176, 177, 178, 179, 197, 221, 226, 232 |
| 15 | guided | 41, 42, 43, 44, 45, 46, 179, 197, 221, 226, 232 |
| 16 | automatic | 47, 48, 49, 180, 181, 182, 224 |
| 17 | turretTurnSpeed | 47, 48, 49, 180, 181, 182, 198, 199, 200, 224 |
| 18 | shieldCapacity | 50, 51, 52, 53, 54, 225 |
| 19 | shieldRegenMs | 50, 51, 52, 53, 54, 225 |
| 20 | armor | 55, 56, 57, 58, 59 |
| 22 | cargoBonus | 63, 64, 65, 66, 67 |
| 23 | tractorAuto | 68, 69, 70, 194 |
| 24 | tractorLockMs | 68, 69, 70, 194 |
| 25 | boostSpeed | 71, 72, 73, 74, 195 |
| 26 | boostRechargeMs | 71, 72, 73, 74, 195 |
| 27 | boostDurationMs | 71, 72, 73, 74, 195 |
| 28 | agility | 76, 77, 78, 79, 80 |
| 29 | lockTimeMs | 81, 82, 83, 84 |
| 30 | showClassAAsteroids | 81, 82, 83, 84 |
| 31 | radarShowsCargo | 81, 82, 83, 84 |
| 32 | drillSpeed | 86, 87, 88, 89, 90 |
| 33 | miningYield | 86, 87, 88, 89, 90 |
| 34 | cabins | 91, 92, 93 |
| 35 | cloakDurationMs | 94, 95, 96 |
| 36 | cloakChargeMs | 94, 95, 96 |
| 37 | cruiseChargeMs | 85 |
| 38 | energyCells | 85, 94, 95, 96 |
| 39 | fireRateFactor | 186, 187 |
| 40 | damageFactor | 186, 187 |
| 41 | emergencyMs | 185 |
| 42 | timeExtenderMs | 184 |
| 43 | timeExtenderCooldownMs | 184 |
| 49 | collectorSpeed | 198, 199, 200 |
| 50 | collectorMagnitude | 198, 199, 200 |
| 51 | collectorRange | 198, 199, 200 |
| 52 | gammaShielding | 50, 51, 52, 53, 54, 205, 206, 225 |
| 53 | beamRange | 207, 208, 222, 223 |
| 54 | beamStrength | 207, 208, 222, 223 |
| 55 | beamTargets | 207, 208, 222, 223 |
| 100 | miningBeam |  |
| 101 | miningBeamRange |  |
| 102 | miningBeamLayerMs |  |
| 103 | miningBeamLook |  |
| 104 | phaseCloak |  |
| 105 | missiles |  |
| 106 | salvoShots |  |
| 107 | salvoCount |  |
| 108 | salvoGapMs |  |
| 109 | turretRange |  |
| 110 | flightScope |  |
| 111 | topSpeed |  |
| 112 | turnRate |  |
| 113 | strafeSpeed |  |
| 114 | originalTopSpeed |  |
| 115 | originalTurnRate |  |
| 116 | originalStrafeSpeed |  |
| 117 | freeTopSpeed |  |
| 118 | freeTurnRate |  |
| 119 | freeStrafeSpeed |  |
| 120 | freeAcceleration |  |
| 121 | freeBoostFactor |  |
| 122 | tractorTargets |  |
| 123 | tractorRange |  |
| 124 | shieldDamage |  |
| 127 | echoDelayMs |  |
| 128 | echoPercent |  |
| 129 | echoRadius |  |
| 130 | echoExplosion |  |
| 131 | missileDefense |  |
| 132 | miningTime |  |
| 133 | cruiseSpeed |  |
| 134 | cruiseRange |  |
| 135 | interstellar |  |
| 137 | tractorNoLock |  |

## Blueprints

The original blueprint recipes (product, then amount x ingredient). blueprints.json can give an item another recipe.

| Product # | Product | Ingredients |
|---|---|---|
| 11 | M6 A3 "Wolverine" | 1 x 9 M6 A1 "Wolf", 50 x 155 Titanium, 75 x 120 Plastics, 20 x 126 Noble Gas, 20 x 122 Energy Cells, 20 x 129 Optics |
| 15 | H'nookk | 1 x 12 N'saan, 15 x 130 Hydraulics, 10 x 129 Optics, 10 x 122 Energy Cells, 1 x 166 Titanium Core, 10 x 125 Mechanical Supplies |
| 25 | 128MJ Railgun | 1 x 24 64MJ Railgun, 4 x 125 Mechanical Supplies, 2 x 121 Nanotech, 2 x 122 Energy Cells, 5 x 120 Plastics, 5 x 127 Microchips |
| 27 | Mass Driver MD 10 | 1 x 26 Scram Cannon, 55 x 154 Gold, 14 x 122 Energy Cells, 16 x 125 Mechanical Supplies, 20 x 127 Microchips, 27 x 130 Hydraulics |
| 33 | Amour Rocket | 1 x 118 Electronics, 1 x 120 Plastics |
| 37 | Intelli Jet | 2 x 128 Com. Devices, 1 x 129 Optics, 2 x 127 Microchips, 1 x 125 Mechanical Supplies |
| 46 | AMR Extinctor | 40 x 124 Radioactive Goods, 20 x 114 Explosives, 7 x 128 Com. Devices, 5 x 127 Microchips, 10 x 125 Mechanical Supplies, 10 x 118 Electronics |
| 54 | Fluxed Matter Shield | 80 x 163 Hypanium, 100 x 118 Electronics, 75 x 125 Mechanical Supplies, 90 x 121 Nanotech, 80 x 127 Microchips |
| 59 | T'yol | 40 x 157 Orichalzine, 20 x 168 Orichalzin Core, 80 x 156 Iron, 45 x 125 Mechanical Supplies, 30 x 120 Plastics, 100 x 122 Energy Cells |
| 67 | Rhoda Blackhole | 50 x 118 Electronics, 10 x 122 Energy Cells, 50 x 125 Mechanical Supplies, 50 x 130 Hydraulics |
| 85 | Khador Drive | 50 x 164 Void Crystals, 200 x 158 Pyresium, 200 x 160 Doxtrite, 30 x 163 Hypanium, 100 x 118 Electronics, 150 x 125 Mechanical Supplies, 150 x 127 Microchips, 120 x 129 Optics, 125 x 122 Energy Cells |
| 90 | Gunant's Drill | 90 x 121 Nanotech, 80 x 125 Mechanical Supplies, 90 x 130 Hydraulics, 10 x 166 Titanium Core, 40 x 155 Titanium |
| 96 | Yin Co. Shadow Ninja | 100 x 126 Noble Gas, 80 x 119 Chemicals, 80 x 130 Hydraulics, 100 x 129 Optics, 80 x 127 Microchips, 125 x 124 Radioactive Goods |
| 178 | Icarus Heavy AS | 1 x 27 Mass Driver MD 10, 90 x 125 Mechanical Supplies, 70 x 121 Nanotech, 50 x 122 Energy Cells, 5 x 171 Doxtrit Core, 1 x 186 Nirai Overdrive, 5 x 166 Titanium Core |
| 179 | Liberator | 10 x 44 AMR Tormentor, 100 x 124 Radioactive Goods, 250 x 114 Explosives, 150 x 127 Microchips, 125 x 118 Electronics, 175 x 129 Optics, 215 x 157 Orichalzine, 25 x 166 Titanium Core, 160 x 122 Energy Cells |
| 182 | HH-AT "Archimedes" | 1 x 47 Hammerhead D1, 2 x 15 H'nookk, 100 x 118 Electronics, 120 x 125 Mechanical Supplies, 100 x 127 Microchips, 100 x 128 Com. Devices, 100 x 129 Optics, 40 x 168 Orichalzin Core, 40 x 166 Titanium Core |
| 183 | Disruptor Laser | 30 x 118 Electronics, 25 x 121 Nanotech, 10 x 155 Titanium, 8 x 168 Orichalzin Core, 10 x 172 Cesogen Core, 30 x 120 Plastics, 28 x 126 Noble Gas, 50 x 122 Energy Cells, 1 x 175 Void Essence |
| 206 | Gamma Shield II | 1 x 205 Gamma Shield I, 200 x 119 Chemicals, 150 x 121 Nanotech, 100 x 163 Hypanium, 25 x 172 Cesogen Core, 250 x 122 Energy Cells |
| 210 | Chromo Plasma | 930 x 201 Green Plasma, 930 x 202 Blue Plasma, 930 x 203 Purple Plasma, 930 x 204 Red Plasma |
| 221 | Ion Lambda Mk2 | 10 x 197 Ion Lambda Mk1, 2 x 173 Perrius Core, 40 x 114 Explosives, 20 x 124 Radioactive Goods |
| 223 | Pandora Leech | 200 x 122 Energy Cells, 90 x 129 Optics, 1 x 222 Crimson Drain, 300 x 204 Red Plasma, 10 x 218 Novanium Core |
| 225 | Particle Shield | 80 x 163 Hypanium, 55 x 118 Electronics, 12 x 120 Plastics, 45 x 121 Nanotech, 100 x 122 Energy Cells, 50 x 127 Microchips, 250 x 201 Green Plasma, 250 x 202 Blue Plasma, 5 x 218 Novanium Core, 1 x 54 Fluxed Matter Shield |
| 226 | Shock Blast | 25 x 114 Explosives, 25 x 122 Energy Cells, 10 x 118 Electronics, 8 x 126 Noble Gas, 30 x 203 Purple Plasma |
| 227 | Phoenix SIS | 5 x 118 Electronics, 5 x 119 Chemicals, 50 x 122 Energy Cells, 1 x 210 Chromo Plasma |
| 232 | Fireworks | 10 x 114 Explosives, 1 x 210 Chromo Plasma |

## Ships

Armor = hull points. Handling: 100 = average. Slots = primary / secondary / turret / equipment. Hangar = `hangarHeight`
(pivot above the pad, game units). Size = length x width x height of the hull in game units (20 = 1 m; a new ship's
`modelLength` compares with Length). Model = its assembled model's name: the prefix of its texture names and the folder
of one-ship skins (`textures/<model>/<name>.png`); its textures are listed under Models. Ships 13, 14 and 15 are the
freighters and the Terran battleship (not sold, only flown by NPCs).

| # | Name | Race | Armor | Cargo t | Handling | Price | Slots | Hangar | Size (L x W x H) | Model |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | Betty | Midorian | 95 | 25 | 120 | 16200 | 1/1/0/3 | 160 | 562 x 782 x 322 | ship_000_midorian |
| 1 | Teneta | Terran | 192 | 65 | 106 | 100320 | 2/4/1/7 | 150 | 1202 x 1331 x 303 | ship_001_terran |
| 2 | Hiro | Pirate | 160 | 52 | 150 | 32600 | 1/2/0/4 | 220 | 1150 x 657 x 432 | ship_002_pirates |
| 3 | Badger | Midorian | 135 | 55 | 112 | 39100 | 2/2/0/5 | 310 | 801 x 1244 x 470 | ship_003_midorian |
| 4 | Dace | Nivelian | 170 | 38 | 162 | 188480 | 4/1/0/5 | 160 | 1618 x 780 x 329 | ship_004_nivelian |
| 5 | Inflict | Terran | 150 | 45 | 125 | 30900 | 2/1/0/4 | 180 | 941 x 854 x 302 | ship_005_terran |
| 6 | Hector | Midorian | 105 | 42 | 148 | 38400 | 2/1/0/5 | 140 | 876 x 1043 x 351 | ship_006_midorian |
| 7 | Anaan | Terran | 220 | 240 | 65 | 141520 | 2/1/1/7 | 170 | 1391 x 1329 x 406 | ship_007_terran |
| 8 | VoidX | Void | 450 | 30 | 155 | 4057950 | 4/4/0/15 | 170 | 1725 x 730 x 270 | ship_008_void |
| 9 | H'Soc | Vossk | 210 | 45 | 140 | 120000 | 2/2/0/7 | 110 | 1591 x 723 x 276 | ship_009_vossk |
| 10 | Phantom | Terran | 200 | 52 | 150 | 966667 | 2/1/0/9 | 250 | 938 x 757 x 446 | ship_010_terran |
| 11 | Hernstein | Pirate | 210 | 180 | 75 | 262240 | 2/2/1/8 | 205 | 1554 x 1125 x 405 | ship_011_pirates |
| 12 | Type 43 | Nivelian | 175 | 30 | 132 | 58000 | 2/3/0/6 | 240 | 1448 x 815 x 399 | ship_012_nivelian |
| 13 | Vossk freighter (not sold) | Vossk | 100 | 50 | 50 | 19700 | 1/2/0/5 | 250 | 15651 x 4477 x 3373 | cargo_004_vossk |
| 14 | Terran battleship (not sold) | Terran | 100 | 50 | 50 | 19700 | 1/2/0/5 | 250 | 42128 x 10722 x 11737 | battleship_terran |
| 15 | freighter of each race (not sold) | each | 100 | 50 | 50 | 19700 | 1/2/0/5 | 250 | 10588 x 4801 x 2979 | cargo_003_terran, cargo_004_vossk, cargo_002_nivelian, cargo_001_midorian |
| 16 | Kinzer | Nivelian | 180 | 45 | 120 | 1418733 | 2/4/0/10 | 170 | 1640 x 1066 x 377 | ship_016_nivelian |
| 17 | Ward | Terran | 145 | 65 | 95 | 1103200 | 4/2/0/10 | 170 | 906 x 1317 x 366 | ship_017_terran |
| 18 | Hatsuyuki | Nivelian | 115 | 28 | 145 | 137520 | 2/2/0/8 | 190 | 1138 x 468 x 373 | ship_018_nivelian |
| 19 | Nuyang II | Midorian | 225 | 105 | 90 | 626467 | 4/2/0/9 | 460 | 830 x 1592 x 648 | ship_019_midorian |
| 20 | Cicero | Midorian | 125 | 25 | 155 | 42000 | 2/1/0/6 | 170 | 668 x 686 x 451 | ship_020_midorian |
| 21 | Aegir | Nivelian | 190 | 70 | 100 | 1808600 | 4/4/0/11 | 210 | 1848 x 1355 x 419 | ship_021_nivelian |
| 22 | Groza | Terran | 160 | 130 | 118 | 201280 | 3/3/0/8 | 140 | 1231 x 1091 x 305 | ship_022_terran |
| 23 | Azov | Pirate | 150 | 55 | 128 | 49520 | 2/2/1/5 | 170 | 1176 x 1028 x 367 | ship_023_pirates |
| 24 | Velasco | Pirate | 170 | 95 | 125 | 456200 | 3/2/1/8 | 160 | 1470 x 1191 x 365 | ship_024_pirates |
| 25 | Tyrion | Pirate | 155 | 52 | 145 | 253120 | 1/4/0/9 | 190 | 1228 x 1403 x 405 | ship_025_pirates |
| 26 | Hera | Terran | 152 | 64 | 108 | 85600 | 2/2/0/7 | 170 | 1300 x 795 x 363 | ship_026_terran |
| 27 | Taipan | Terran | 176 | 50 | 113 | 80080 | 3/2/0/5 | 240 | 1384 x 974 x 479 | ship_027_terran |
| 28 | Veteran | Terran | 200 | 110 | 92 | 1658933 | 3/4/1/12 | 220 | 1976 x 1361 x 381 | ship_028_terran |
| 29 | Mantis | Pirate | 240 | 75 | 118 | 2363886 | 4/4/0/12 | 280 | 1632 x 1630 x 502 | ship_029_pirates |
| 30 | Berger CrossXT | Midorian | 165 | 45 | 128 | 70560 | 2/2/0/6 | 200 | 1043 x 953 x 441 | ship_030_midorian |
| 31 | Salvéhn | Nivelian | 156 | 110 | 102 | 75600 | 2/1/1/6 | 200 | 1807 x 1055 x 450 | ship_031_nivelian |
| 32 | Wasp | Pirate | 100 | 30 | 160 | 19500 | 1/1/0/3 | 140 | 1051 x 1109 x 303 | ship_032_pirates |
| 33 | Furious | Terran | 176 | 108 | 112 | 60640 | 1/2/1/6 | 160 | 1241 x 1090 x 295 | ship_033_terran |
| 34 | Razor 6 | Terran | 135 | 60 | 130 | 235920 | 4/1/0/6 | 180 | 1055 x 956 x 382 | ship_034_terran |
| 35 | Night Owl | Nivelian | 125 | 40 | 150 | 26500 | 1/3/0/4 | 150 | 633 x 676 x 294 | ship_035_nivelian |
| 36 | Cormorant | Terran | 200 | 350 | 45 | 135120 | 0/4/1/8 | 310 | 2008 x 1411 x 614 | ship_036_terran |
| 37 | Cronus | Terran | 190 | 95 | 120 | 0 | 2/2/0/7 | 390 | 1192 x 1146 x 605 | v_ship_037_deep_science |
| 38 | Typhon | Terran | 175 | 40 | 145 | 1666667 | 4/0/0/12 | 480 | 828 x 1132 x 683 | v_ship_038_deep_science |
| 39 | S'Kanarr | Vossk | 315 | 150 | 70 | 7250000 | 4/2/1/11 | 200 | 2031 x 1317 x 396 | v_ship_039_vossk |
| 40 | Nemesis | Terran | 235 | 105 | 95 | 3400000 | 4/1/0/14 | 300 | 2035 x 1026 x 650 | v_ship_040_deep_science |
| 41 | K'Suukk | Vossk | 255 | 55 | 125 | 1300000 | 3/2/0/12 | 300 | 1614 x 843 x 323 | v_ship_041_vossk |
| 42 | Vol Noor | Vossk | 165 | 75 | 110 | 84000 | 2/2/0/7 | 170 | 1680 x 1475 x 252 | ship_042_retro |
| 43 | Wraith | Nivelian | 180 | 65 | 140 | 1166667 | 4/2/0/9 | 200 | 1346 x 1033 x 283 | ship_043_retro |
| 44 | Specter | Vossk | 800 | 65 | 138 | 12000000 | 4/2/0/16 | 170 | 1849 x 1542 x 393 | sn_ship_044_elite_nivelian |
| 45 | Bloodstar | Midorian | 460 | 180 | 88 | 5400000 | 4/4/1/14 | 370 | 1878 x 1864 x 549 | sn_ship_045_most_wanted |
| 46 | Blue Fyre | Midorian | 270 | 125 | 116 | 2571429 | 3/3/0/13 | 370 | 1981 x 1785 x 801 | sn_ship_046_most_wanted |
| 47 | Gator Custom | Midorian | 335 | 320 | 95 | 2971429 | 4/2/0/12 | 300 | 1728 x 1764 x 611 | sn_ship_047_most_wanted |
| 48 | Amboss | Midorian | 305 | 80 | 110 | 3400000 | 4/4/0/14 | 440 | 1024 x 715 x 821 | sn_ship_048_most_wanted |
| 49 | Scimitar | Vossk | 400 | 40 | 105 | 3314286 | 3/2/0/15 | 340 | 2393 x 1683 x 636 | sn_ship_049_boss_nivelian |
| 50 | - | Vossk | 1000 | 50 | 50 | 8165900 | 1/2/0/5 | 170 |  |  |
| 51 | Rhino | Terran | 1200 | 480 | 30 | 466667 | 0/2/1/9 | 470 | 2541 x 1635 x 1061 | sn_ship_051_dropship_terran |
| 52 | Gryphon | Pirate | 220 | 90 | 130 | 1400000 | 4/2/0/10 | 170 | 1790 x 1968 x 410 | sn_ship_052_retro |
| 53 | - | Vossk | 255 | 65 | 120 | 2250000 | 4/4/0/10 | 170 |  |  |
| 54 | Na'srrk | Vossk | 280 | 70 | 145 | 3085714 | 4/4/0/12 | 170 | 1576 x 1209 x 296 | sn_ship_054_vossk |
| 55 | Groza Mk II | Terran | 450 | 90 | 122 | 3565000 | 5/1/0/11 | 170 | 1228 x 1160 x 486 | sn_ship_055_modified |
| 56 | Berger Cross Special | Midorian | 410 | 55 | 130 | 3705000 | 4/4/0/14 | 200 | 1043 x 953 x 448 | sn_ship_056_modified |
| 57 | Kinzer RS | Nivelian | 420 | 65 | 125 | 4465000 | 4/4/0/15 | 170 | 1640 x 1066 x 344 | sn_ship_057_modified |
| 58 | Phantom XT | Terran | 425 | 60 | 150 | 3715000 | 4/1/0/14 | 250 | 966 x 810 x 410 | sn_ship_058_modified |
| 59 | Teneta R.E.D. | Terran | 545 | 70 | 118 | 3805000 | 4/2/1/13 | 150 | 1202 x 1329 x 418 | sn_ship_059_modified |
| 60 | Darkzov | Pirate | 420 | 70 | 130 | 3618000 | 4/1/1/14 | 170 | 1176 x 1028 x 337 | sn_ship_060_modified |
| 61 | Ghost | Vossk | 530 | 50 | 135 | 3333333 | 4/2/0/14 | 250 | 1738 x 1228 x 460 | ship_061_elite_nivelian_prototype |
| 62 | Dark Angel | Midorian | 350 | 85 | 125 | 3650000 | 4/2/1/14 | 270 | 1485 x 1653 x 482 | ship_062_prototype |
| 63 | N'Tirrk | Vossk | 280 | 80 | 128 | 200000 | 2/4/0/8 | 170 | 1653 x 1422 x 432 | ship_063_vossk_prototype |

## Ship hardpoints

Every original ship's mounts as ships.json `mounts` takes them (game units from the ship's centre: x right, y up, z
forward; slotType 0 primary, 1 secondary, 2 turret, 3 engine exhaust with `turretAngles[0]` its particle size). An
`override` with `mounts` replaces the whole list: copy the line and change what you need. Also a guide for placing a
new ship's mounts (compare the hull sizes above).

- **0 Betty**: `[{ "slotType": 0, "position_engine": [219, -22, 26] }, { "slotType": 1, "position_engine": [-217, -40, -60] }, { "slotType": 3, "position_engine": [-160, 99, -256], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [159, 100, -256], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [53, 82, -282], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [-53, 82, -282], "turretAngles": [1, 1, 1] }]`
- **1 Teneta**: `[{ "slotType": 0, "position_engine": [573, -88, 102] }, { "slotType": 0, "position_engine": [-573, -88, 102] }, { "slotType": 1, "position_engine": [-382, -121, 106] }, { "slotType": 1, "position_engine": [382, -121, 106] }, { "slotType": 1, "position_engine": [123, -83, 584] }, { "slotType": 1, "position_engine": [-124, -83, 584] }, { "slotType": 2, "position_engine": [0, 155, -239] }, { "slotType": 3, "position_engine": [231, -42, -533], "turretAngles": [0.8, 0.8, 0.8] }, { "slotType": 3, "position_engine": [-231, -42, -533], "turretAngles": [0.8, 0.8, 0.8] }, { "slotType": 3, "position_engine": [0, -15, -600], "turretAngles": [1, 1, 1] }]`
- **2 Hiro**: `[{ "slotType": 0, "position_engine": [0, -74, 466] }, { "slotType": 1, "position_engine": [-189, -183, -203] }, { "slotType": 1, "position_engine": [189, -183, -203] }, { "slotType": 3, "position_engine": [53, -61, -512], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [0, -1, -512], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [-53, -61, -512], "turretAngles": [0.5, 0.5, 0.5] }]`
- **3 Badger**: `[{ "slotType": 0, "position_engine": [138, -264, 371] }, { "slotType": 0, "position_engine": [-138, -264, 371] }, { "slotType": 1, "position_engine": [563, -166, 67] }, { "slotType": 1, "position_engine": [-563, -166, 67] }, { "slotType": 3, "position_engine": [-88, 41, -360], "turretAngles": [0.8, 0.8, 0.8] }, { "slotType": 3, "position_engine": [88, 41, -360], "turretAngles": [0.8, 0.8, 0.8] }, { "slotType": 3, "position_engine": [400, -38, -430], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [-400, -38, -430], "turretAngles": [1, 1, 1] }]`
- **4 Dace**: `[{ "slotType": 0, "position_engine": [167, -74, 831] }, { "slotType": 0, "position_engine": [-167, -74, 831] }, { "slotType": 0, "position_engine": [332, -120, 430] }, { "slotType": 0, "position_engine": [-332, -120, 430] }, { "slotType": 1, "position_engine": [219, -134, 458] }, { "slotType": 3, "position_engine": [151, 97, -787], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [-152, 97, -787], "turretAngles": [0.7, 0.7, 0.7] }]`
- **5 Inflict**: `[{ "slotType": 0, "position_engine": [324, -74, -48] }, { "slotType": 0, "position_engine": [-324, -74, -48] }, { "slotType": 1, "position_engine": [0, -70, 483] }, { "slotType": 3, "position_engine": [44, -43, -460], "turretAngles": [0.6, 0.6, 0.6] }, { "slotType": 3, "position_engine": [-44, -43, -460], "turretAngles": [0.6, 0.6, 0.6] }, { "slotType": 3, "position_engine": [0, 24, -460], "turretAngles": [0.6, 0.6, 0.6] }]`
- **6 Hector**: `[{ "slotType": 0, "position_engine": [486, -96, 82] }, { "slotType": 0, "position_engine": [-486, -96, 82] }, { "slotType": 1, "position_engine": [-128, -44, 93] }, { "slotType": 3, "position_engine": [0, 11, -157], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [252, 2, -463], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [-252, 2, -463], "turretAngles": [1, 1, 1] }]`
- **7 Anaan**: `[{ "slotType": 0, "position_engine": [481, -67, 719] }, { "slotType": 0, "position_engine": [-481, -67, 719] }, { "slotType": 1, "position_engine": [0, -153, 726] }, { "slotType": 2, "position_engine": [0, 172, 8] }, { "slotType": 3, "position_engine": [78, -47, -656], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [-79, -47, -656], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [-80, 44, -656], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [79, 43, -656], "turretAngles": [1, 1, 1] }]`
- **8 VoidX**: `[{ "slotType": 0, "position_engine": [33, -29, 914] }, { "slotType": 0, "position_engine": [-33, -29, 914] }, { "slotType": 0, "position_engine": [72, -29, 1221] }, { "slotType": 0, "position_engine": [-72, -29, 1221] }, { "slotType": 1, "position_engine": [247, -80, 188] }, { "slotType": 1, "position_engine": [188, -41, 706] }, { "slotType": 1, "position_engine": [-188, -41, 706] }, { "slotType": 1, "position_engine": [-247, -80, 188] }, { "slotType": 3, "position_engine": [0, 26, -457], "turretAngles": [0.6, 0.6, 0.6] }, { "slotType": 3, "position_engine": [-154, -6, -389], "turretAngles": [0.465, 0.465, 0.465] }, { "slotType": 3, "position_engine": [154, -6, -389], "turretAngles": [0.465, 0.465, 0.465] }]`
- **9 H'Soc**: `[{ "slotType": 0, "position_engine": [58, -92, 765] }, { "slotType": 0, "position_engine": [-59, -92, 765] }, { "slotType": 1, "position_engine": [29, -35, 571] }, { "slotType": 1, "position_engine": [29, -35, 571] }, { "slotType": 1, "position_engine": [-30, -35, 571] }, { "slotType": 3, "position_engine": [0, 0, -477], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [-65, -57, -447], "turretAngles": [0.344, 0.344, 0.344] }, { "slotType": 3, "position_engine": [65, -57, -447], "turretAngles": [0.344, 0.344, 0.344] }]`
- **10 Phantom**: `[{ "slotType": 0, "position_engine": [-142, -98, -84] }, { "slotType": 0, "position_engine": [142, -98, -84] }, { "slotType": 1, "position_engine": [0, -138, 455] }, { "slotType": 3, "position_engine": [45, -54, -455], "turretAngles": [0.6, 0.6, 0.6] }, { "slotType": 3, "position_engine": [-45, -54, -455], "turretAngles": [0.6, 0.6, 0.6] }, { "slotType": 3, "position_engine": [0, 18, -455], "turretAngles": [0.6, 0.6, 0.6] }]`
- **11 Hernstein**: `[{ "slotType": 0, "position_engine": [243, -44, 504] }, { "slotType": 0, "position_engine": [-243, -44, 504] }, { "slotType": 1, "position_engine": [-504, -149, 232] }, { "slotType": 1, "position_engine": [504, -149, 232] }, { "slotType": 2, "position_engine": [0, 207, -622] }, { "slotType": 3, "position_engine": [-383, -93, -773], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [-383, 11, -773], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [383, 11, -773], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [383, -93, -773], "turretAngles": [0.7, 0.7, 0.7] }]`
- **12 Type 43**: `[{ "slotType": 0, "position_engine": [347, -8, 729] }, { "slotType": 0, "position_engine": [-347, -8, 729] }, { "slotType": 1, "position_engine": [-179, -38, -188] }, { "slotType": 1, "position_engine": [179, -38, -188] }, { "slotType": 1, "position_engine": [0, 180, -370] }, { "slotType": 3, "position_engine": [0, 89, -707], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [312, -64, -588], "turretAngles": [0.6, 0.6, 0.6] }, { "slotType": 3, "position_engine": [-317, -67, -588], "turretAngles": [0.6, 0.6, 0.6] }]`
- **16 Kinzer**: `[{ "slotType": 0, "position_engine": [251, -69, 830] }, { "slotType": 0, "position_engine": [-251, -69, 830] }, { "slotType": 1, "position_engine": [156, -55, -168] }, { "slotType": 1, "position_engine": [502, -133, 13] }, { "slotType": 1, "position_engine": [-502, -133, 13] }, { "slotType": 1, "position_engine": [-156, -55, -168] }, { "slotType": 3, "position_engine": [-1, 12, -646], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [-1, 125, -806], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [-122, 10, -806], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [119, 10, -806], "turretAngles": [0.7, 0.7, 0.7] }]`
- **17 Ward**: `[{ "slotType": 0, "position_engine": [530, -108, 269] }, { "slotType": 0, "position_engine": [-530, -108, 269] }, { "slotType": 0, "position_engine": [275, -140, 512] }, { "slotType": 0, "position_engine": [-275, -140, 512] }, { "slotType": 1, "position_engine": [123, -132, 348] }, { "slotType": 1, "position_engine": [-123, -132, 348] }, { "slotType": 3, "position_engine": [-100, 38, -367], "turretAngles": [0.4, 0.4, 0.4] }, { "slotType": 3, "position_engine": [0, 150, -349], "turretAngles": [0.4, 0.4, 0.4] }, { "slotType": 3, "position_engine": [0, 39, -332], "turretAngles": [0.6, 0.6, 0.6] }, { "slotType": 3, "position_engine": [100, 38, -367], "turretAngles": [0.4, 0.4, 0.4] }]`
- **18 Hatsuyuki**: `[{ "slotType": 0, "position_engine": [147, 103, 352] }, { "slotType": 0, "position_engine": [-147, 103, 352] }, { "slotType": 1, "position_engine": [36, -130, 154] }, { "slotType": 1, "position_engine": [-36, -130, 154] }, { "slotType": 3, "position_engine": [-39, 35, -83], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [0, 0, -73], "turretAngles": [0.4, 0.4, 0.4] }, { "slotType": 3, "position_engine": [43, 35, -83], "turretAngles": [0.5, 0.5, 0.5] }]`
- **19 Nuyang II**: `[{ "slotType": 0, "position_engine": [103, -183, 333] }, { "slotType": 0, "position_engine": [-103, -183, 333] }, { "slotType": 0, "position_engine": [65, -400, 378] }, { "slotType": 0, "position_engine": [-65, -400, 378] }, { "slotType": 1, "position_engine": [748, -89, 96] }, { "slotType": 1, "position_engine": [-748, -89, 96] }, { "slotType": 3, "position_engine": [446, -3, -240], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [0, 69, -316], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [-446, -3, -240], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [0, -70, -316], "turretAngles": [0.5, 0.5, 0.5] }]`
- **20 Cicero**: `[{ "slotType": 0, "position_engine": [324, -124, 315] }, { "slotType": 0, "position_engine": [-324, -124, 315] }, { "slotType": 1, "position_engine": [-201, -13, 43] }, { "slotType": 3, "position_engine": [148, 180, -295], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [0, 202, -169], "turretAngles": [0.9, 0.9, 0.9] }, { "slotType": 3, "position_engine": [-148, 180, -295], "turretAngles": [0.5, 0.5, 0.5] }]`
- **21 Aegir**: `[{ "slotType": 0, "position_engine": [604, -69, -372] }, { "slotType": 0, "position_engine": [-603, -69, -372] }, { "slotType": 0, "position_engine": [218, -4, 884] }, { "slotType": 0, "position_engine": [-218, -5, 884] }, { "slotType": 1, "position_engine": [515, -52, -542] }, { "slotType": 1, "position_engine": [304, 113, -570] }, { "slotType": 1, "position_engine": [-300, 111, -570] }, { "slotType": 1, "position_engine": [-514, -50, -542] }, { "slotType": 3, "position_engine": [-2, 88, -900], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [-2, 22, -878], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [280, -14, -884], "turretAngles": [1.1, 1.1, 1.1] }, { "slotType": 3, "position_engine": [-289, -14, -884], "turretAngles": [1.1, 1.1, 1.1] }]`
- **22 Groza**: `[{ "slotType": 0, "position_engine": [212, -42, 124] }, { "slotType": 0, "position_engine": [-212, -43, 124] }, { "slotType": 0, "position_engine": [0, -114, 614] }, { "slotType": 1, "position_engine": [300, -73, 244] }, { "slotType": 1, "position_engine": [-299, -72, 244] }, { "slotType": 1, "position_engine": [0, -46, 589] }, { "slotType": 3, "position_engine": [111, 3, -483], "turretAngles": [0.4, 0.4, 0.4] }, { "slotType": 3, "position_engine": [0, 4, -504], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [-111, 3, -483], "turretAngles": [0.4, 0.4, 0.4] }, { "slotType": 3, "position_engine": [-89, -62, -483], "turretAngles": [0.4, 0.4, 0.4] }, { "slotType": 3, "position_engine": [89, -62, -483], "turretAngles": [0.4, 0.4, 0.4] }]`
- **23 Azov**: `[{ "slotType": 0, "position_engine": [214, -16, 59] }, { "slotType": 0, "position_engine": [-214, -16, 59] }, { "slotType": 1, "position_engine": [-369, -164, -99] }, { "slotType": 1, "position_engine": [369, -164, -99] }, { "slotType": 2, "position_engine": [0, 99, -288] }, { "slotType": 3, "position_engine": [42, -64, -491], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [-42, -64, -491], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [-106, 42, -498], "turretAngles": [0.8, 0.8, 0.8] }, { "slotType": 3, "position_engine": [102, 42, -498], "turretAngles": [0.8, 0.8, 0.8] }]`
- **24 Velasco**: `[{ "slotType": 0, "position_engine": [226, -34, 161] }, { "slotType": 0, "position_engine": [-226, -34, 161] }, { "slotType": 0, "position_engine": [0, -125, 700] }, { "slotType": 1, "position_engine": [417, -124, -62] }, { "slotType": 1, "position_engine": [-417, -124, -62] }, { "slotType": 2, "position_engine": [0, 103, -298] }, { "slotType": 3, "position_engine": [320, 40, -476], "turretAngles": [0.8, 0.8, 0.8] }, { "slotType": 3, "position_engine": [-319, 40, -476], "turretAngles": [0.8, 0.8, 0.8] }, { "slotType": 3, "position_engine": [0, 36, -669], "turretAngles": [1, 1, 1] }]`
- **25 Tyrion**: `[{ "slotType": 0, "position_engine": [-185, -16, 102] }, { "slotType": 1, "position_engine": [503, -115, -145] }, { "slotType": 1, "position_engine": [358, -81, -145] }, { "slotType": 1, "position_engine": [-503, -115, -144] }, { "slotType": 1, "position_engine": [-358, -81, -145] }, { "slotType": 3, "position_engine": [1, 30, -566], "turretAngles": [0.8, 0.8, 0.8] }, { "slotType": 3, "position_engine": [1, 130, -600], "turretAngles": [0.8, 0.8, 0.8] }, { "slotType": 3, "position_engine": [-168, 79, -540], "turretAngles": [0.8, 0.8, 0.8] }, { "slotType": 3, "position_engine": [168, 79, -541], "turretAngles": [0.8, 0.8, 0.8] }]`
- **26 Hera**: `[{ "slotType": 0, "position_engine": [183, -13, 663] }, { "slotType": 0, "position_engine": [-183, -13, 663] }, { "slotType": 1, "position_engine": [348, -10, -209] }, { "slotType": 1, "position_engine": [-348, -10, -209] }, { "slotType": 3, "position_engine": [-99, -57, -506], "turretAngles": [0.4, 0.4, 0.4] }, { "slotType": 3, "position_engine": [-104, 27, -506], "turretAngles": [0.4, 0.4, 0.4] }, { "slotType": 3, "position_engine": [0, -59, -618], "turretAngles": [0.8, 0.8, 0.8] }, { "slotType": 3, "position_engine": [0, 46, -618], "turretAngles": [0.8, 0.8, 0.8] }, { "slotType": 3, "position_engine": [102, 27, -506], "turretAngles": [0.4, 0.4, 0.4] }, { "slotType": 3, "position_engine": [97, -57, -506], "turretAngles": [0.4, 0.4, 0.4] }]`
- **27 Taipan**: `[{ "slotType": 0, "position_engine": [137, -46, 693] }, { "slotType": 0, "position_engine": [-137, -46, 693] }, { "slotType": 0, "position_engine": [0, -192, -195] }, { "slotType": 1, "position_engine": [256, 47, -91] }, { "slotType": 1, "position_engine": [-256, 47, -91] }, { "slotType": 3, "position_engine": [-407, 114, -693], "turretAngles": [0.3, 0.3, 0.3] }, { "slotType": 3, "position_engine": [-407, 12, -670], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [-125, 40, -514], "turretAngles": [0.9, 0.9, 0.9] }, { "slotType": 3, "position_engine": [406, 114, -686], "turretAngles": [0.3, 0.3, 0.3] }, { "slotType": 3, "position_engine": [406, 12, -663], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [123, 40, -516], "turretAngles": [0.9, 0.9, 0.9] }]`
- **28 Veteran**: `[{ "slotType": 0, "position_engine": [140, -26, 940] }, { "slotType": 0, "position_engine": [-140, -26, 940] }, { "slotType": 0, "position_engine": [0, -31, 1109] }, { "slotType": 1, "position_engine": [506, -158, -244] }, { "slotType": 1, "position_engine": [423, -121, -56] }, { "slotType": 1, "position_engine": [-423, -121, -56] }, { "slotType": 1, "position_engine": [-506, -158, -244] }, { "slotType": 2, "position_engine": [0, 168, -457] }, { "slotType": 3, "position_engine": [-183, -7, -849], "turretAngles": [0.4, 0.4, 0.4] }, { "slotType": 3, "position_engine": [0, 68, -864], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [0, -45, -873], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [183, -7, -849], "turretAngles": [0.4, 0.4, 0.4] }]`
- **29 Mantis**: `[{ "slotType": 0, "position_engine": [780, -213, -162] }, { "slotType": 0, "position_engine": [-780, -213, -162] }, { "slotType": 0, "position_engine": [252, -83, 281] }, { "slotType": 0, "position_engine": [-252, -83, 281] }, { "slotType": 1, "position_engine": [-517, -216, -177] }, { "slotType": 1, "position_engine": [-355, -181, -176] }, { "slotType": 1, "position_engine": [517, -216, -177] }, { "slotType": 1, "position_engine": [355, -181, -176] }, { "slotType": 3, "position_engine": [-198, 76, -790], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [-107, 105, -785], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [-107, 38, -778], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [112, 105, -785], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [112, 38, -778], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [201, 75, -790], "turretAngles": [1, 1, 1] }]`
- **30 Berger CrossXT**: `[{ "slotType": 0, "position_engine": [235, -66, 684] }, { "slotType": 0, "position_engine": [-232, -65, 680] }, { "slotType": 1, "position_engine": [434, -165, 225] }, { "slotType": 1, "position_engine": [-425, -165, 225] }, { "slotType": 3, "position_engine": [87, 187, -373], "turretAngles": [0.6, 0.6, 0.6] }, { "slotType": 3, "position_engine": [233, -19, -65], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [-85, 187, -373], "turretAngles": [0.6, 0.6, 0.6] }, { "slotType": 3, "position_engine": [-231, -19, -65], "turretAngles": [1, 1, 1] }]`
- **31 Salvéhn**: `[{ "slotType": 0, "position_engine": [158, -150, 943] }, { "slotType": 0, "position_engine": [-158, -150, 943] }, { "slotType": 1, "position_engine": [0, 173, -157] }, { "slotType": 2, "position_engine": [0, 230, -298] }, { "slotType": 3, "position_engine": [-3, -124, -565], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [-3, -50, -565], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [70, -90, -565], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [-65, -90, -565], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [237, -97, -519], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [-238, -97, -519], "turretAngles": [0.7, 0.7, 0.7] }]`
- **32 Wasp**: `[{ "slotType": 0, "position_engine": [296, -22, 109] }, { "slotType": 1, "position_engine": [-297, -19, -17] }, { "slotType": 3, "position_engine": [0, 90, -527], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [-136, 49, -527], "turretAngles": [0.35, 0.35, 0.35] }, { "slotType": 3, "position_engine": [136, 49, -527], "turretAngles": [0.35, 0.35, 0.35] }]`
- **33 Furious**: `[{ "slotType": 0, "position_engine": [0, -40, 694] }, { "slotType": 1, "position_engine": [487, -111, 192] }, { "slotType": 1, "position_engine": [-487, -111, 192] }, { "slotType": 2, "position_engine": [0, 145, -387] }, { "slotType": 3, "position_engine": [-56, -10, -334], "turretAngles": [0.8, 0.8, 0.8] }, { "slotType": 3, "position_engine": [56, -10, -334], "turretAngles": [0.8, 0.8, 0.8] }, { "slotType": 3, "position_engine": [209, 33, -519], "turretAngles": [0.8, 0.8, 0.8] }, { "slotType": 3, "position_engine": [-209, 33, -519], "turretAngles": [0.8, 0.8, 0.8] }]`
- **34 Razor 6**: `[{ "slotType": 0, "position_engine": [106, -94, 509] }, { "slotType": 0, "position_engine": [-106, -94, 509] }, { "slotType": 0, "position_engine": [347, -2, 116] }, { "slotType": 0, "position_engine": [-347, -2, 116] }, { "slotType": 1, "position_engine": [0, -40, 697] }, { "slotType": 3, "position_engine": [0, -55, -490], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [0, 38, -513], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [59, -25, -427], "turretAngles": [0.3, 0.3, 0.3] }, { "slotType": 3, "position_engine": [228, 0, -397], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [-59, -25, -427], "turretAngles": [0.3, 0.3, 0.3] }, { "slotType": 3, "position_engine": [-228, 0, -397], "turretAngles": [0.7, 0.7, 0.7] }]`
- **35 Night Owl**: `[{ "slotType": 0, "position_engine": [0, -110, 319] }, { "slotType": 1, "position_engine": [0, 105, 1] }, { "slotType": 1, "position_engine": [310, 2, -1] }, { "slotType": 1, "position_engine": [-310, 2, -1] }, { "slotType": 3, "position_engine": [0, -26, -293], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [0, 36, -303], "turretAngles": [0.5, 0.5, 0.5] }]`
- **36 Cormorant**: `[{ "slotType": 1, "position_engine": [435, -176, 366] }, { "slotType": 1, "position_engine": [641, -244, 445] }, { "slotType": 1, "position_engine": [-641, -244, 445] }, { "slotType": 1, "position_engine": [-435, -176, 366] }, { "slotType": 2, "position_engine": [0, 238, -352] }, { "slotType": 3, "position_engine": [309, -53, -73], "turretAngles": [0.9, 0.9, 0.9] }, { "slotType": 3, "position_engine": [205, 82, -1000], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [-205, 82, -1000], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [-309, -53, -73], "turretAngles": [0.9, 0.9, 0.9] }]`
- **37 Cronus**: `[{ "slotType": 0, "position_engine": [523, -354, 105] }, { "slotType": 0, "position_engine": [-523, -354, 105] }, { "slotType": 1, "position_engine": [358, -74, -81] }, { "slotType": 1, "position_engine": [-358, -74, -81] }, { "slotType": 3, "position_engine": [525, -318, -423], "turretAngles": [0.75, 0.75, 0.75] }, { "slotType": 3, "position_engine": [0, 18, -566], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [525, -318, -423], "turretAngles": [0.75, 0.75, 0.75] }, { "slotType": 3, "position_engine": [0, 18, -566], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [-525, -318, -423], "turretAngles": [0.75, 0.75, 0.75] }, { "slotType": 3, "position_engine": [-525, -318, -423], "turretAngles": [0.75, 0.75, 0.75] }]`
- **38 Typhon**: `[{ "slotType": 0, "position_engine": [285, -156, 272] }, { "slotType": 0, "position_engine": [-285, -156, 272] }, { "slotType": 0, "position_engine": [285, -240, 272] }, { "slotType": 0, "position_engine": [-285, -240, 272] }, { "slotType": 3, "position_engine": [0, 124, -428], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [28, 37, -365], "turretAngles": [0.75, 0.75, 0.75] }, { "slotType": 3, "position_engine": [-29, 37, -365], "turretAngles": [0.75, 0.75, 0.75] }]`
- **39 S'Kanarr**: `[{ "slotType": 0, "position_engine": [487, -5, 837] }, { "slotType": 0, "position_engine": [114, -17, 949] }, { "slotType": 0, "position_engine": [-114, -17, 949] }, { "slotType": 0, "position_engine": [-484, -5, 837] }, { "slotType": 1, "position_engine": [138, 121, 329] }, { "slotType": 1, "position_engine": [-136, 121, 329] }, { "slotType": 2, "position_engine": [0, 243, -27] }, { "slotType": 3, "position_engine": [-236, 42, -422], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [-231, -20, -378], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [231, -20, -378], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [237, 42, -422], "turretAngles": [0.5, 0.5, 0.5] }]`
- **40 Nemesis**: `[{ "slotType": 0, "position_engine": [338, 5, -271] }, { "slotType": 0, "position_engine": [71, -115, 1142] }, { "slotType": 0, "position_engine": [-338, 5, -271] }, { "slotType": 0, "position_engine": [-71, -115, 1142] }, { "slotType": 1, "position_engine": [2, 167, -136] }, { "slotType": 3, "position_engine": [85, -129, -227], "turretAngles": [0.35, 0.35, 0.35] }, { "slotType": 3, "position_engine": [116, 50, -708], "turretAngles": [0.75, 0.75, 0.75] }, { "slotType": 3, "position_engine": [-116, 50, -708], "turretAngles": [0.75, 0.75, 0.75] }, { "slotType": 3, "position_engine": [-85, -129, -227], "turretAngles": [0.35, 0.35, 0.35] }]`
- **41 K'Suukk**: `[{ "slotType": 0, "position_engine": [0, -169, 592] }, { "slotType": 0, "position_engine": [-244, -120, 751] }, { "slotType": 0, "position_engine": [244, -120, 751] }, { "slotType": 1, "position_engine": [-151, -106, 408] }, { "slotType": 1, "position_engine": [151, -106, 408] }, { "slotType": 3, "position_engine": [-123, 32, -634], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [123, 32, -634], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [0, -59, -296], "turretAngles": [0.7, 0.7, 0.7] }]`
- **42 Vol Noor**: `[{ "slotType": 0, "position_engine": [120, -43, 910] }, { "slotType": 0, "position_engine": [-120, -43, 910] }, { "slotType": 1, "position_engine": [674, -58, 175] }, { "slotType": 1, "position_engine": [-674, -58, 175] }, { "slotType": 3, "position_engine": [270, -21, -733], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [-270, -21, -733], "turretAngles": [1, 1, 1] }]`
- **43 Wraith**: `[{ "slotType": 0, "position_engine": [478, -48, 131] }, { "slotType": 0, "position_engine": [216, -37, 810] }, { "slotType": 0, "position_engine": [-216, -37, 810] }, { "slotType": 0, "position_engine": [-478, -48, 131] }, { "slotType": 1, "position_engine": [105, 101, -115] }, { "slotType": 1, "position_engine": [-105, 101, -115] }, { "slotType": 3, "position_engine": [116, 113, -381], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [0, 23, -478], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [-116, 113, -381], "turretAngles": [0.5, 0.5, 0.5] }]`
- **44 Specter**: `[{ "slotType": 0, "position_engine": [-472, -80, -176] }, { "slotType": 0, "position_engine": [473, -80, -177] }, { "slotType": 0, "position_engine": [-476, -54, -33] }, { "slotType": 0, "position_engine": [476, -54, -31] }, { "slotType": 1, "position_engine": [-568, -64, -518] }, { "slotType": 1, "position_engine": [569, -68, -518] }, { "slotType": 3, "position_engine": [-309, -22, -784], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [326, -22, -784], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [195, -8, -806], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [-195, -8, -806], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [-19, 24, -806], "turretAngles": [0.697, 0.697, 0.697] }, { "slotType": 3, "position_engine": [22, 24, -806], "turretAngles": [0.697, 0.697, 0.697] }, { "slotType": 3, "position_engine": [-257, -15, -795], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [264, -15, -795], "turretAngles": [0.5, 0.5, 0.5] }]`
- **45 Bloodstar**: `[{ "slotType": 0, "position_engine": [-470, -157, 367] }, { "slotType": 0, "position_engine": [-720, -157, 154] }, { "slotType": 0, "position_engine": [471, -156, 365] }, { "slotType": 0, "position_engine": [720, -156, 151] }, { "slotType": 1, "position_engine": [10, -189, 962] }, { "slotType": 1, "position_engine": [-9, -189, 962] }, { "slotType": 1, "position_engine": [10, -169, 962] }, { "slotType": 1, "position_engine": [-9, -169, 962] }, { "slotType": 2, "position_engine": [0, 172, -460] }, { "slotType": 3, "position_engine": [0, -105, -668], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [0, -16, -668], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [101, -61, -668], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [290, -73, -605], "turretAngles": [0.405, 0.405, 0.405] }, { "slotType": 3, "position_engine": [-100, -61, -668], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [-287, -73, -603], "turretAngles": [0.405, 0.405, 0.405] }]`
- **46 Blue Fyre**: `[{ "slotType": 0, "position_engine": [-769, -332, 43] }, { "slotType": 0, "position_engine": [0, 213, 42] }, { "slotType": 0, "position_engine": [769, -330, 45] }, { "slotType": 1, "position_engine": [-770, -389, -105] }, { "slotType": 1, "position_engine": [0, 271, -104] }, { "slotType": 1, "position_engine": [768, -387, -104] }, { "slotType": 3, "position_engine": [475, -201, -732], "turretAngles": [0.8, 0.8, 0.8] }, { "slotType": 3, "position_engine": [475, -79, -759], "turretAngles": [1.1, 1.1, 1.1] }, { "slotType": 3, "position_engine": [-476, -201, -734], "turretAngles": [0.8, 0.8, 0.8] }, { "slotType": 3, "position_engine": [-476, -79, -749], "turretAngles": [1.1, 1.1, 1.1] }]`
- **47 Gator Custom**: `[{ "slotType": 0, "position_engine": [-794, -96, -200] }, { "slotType": 0, "position_engine": [-76, -192, 1068] }, { "slotType": 0, "position_engine": [77, -192, 1068] }, { "slotType": 0, "position_engine": [795, -96, -202] }, { "slotType": 1, "position_engine": [-278, -201, 158] }, { "slotType": 1, "position_engine": [284, -201, 158] }, { "slotType": 3, "position_engine": [0, 149, -629], "turretAngles": [1.3, 1.3, 1.3] }, { "slotType": 3, "position_engine": [614, 4, -665], "turretAngles": [0.8, 1, 0.8] }, { "slotType": 3, "position_engine": [527, 25, -665], "turretAngles": [0.896, 0.896, 0.896] }, { "slotType": 3, "position_engine": [200, 113, -608], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [-614, 4, -665], "turretAngles": [0.8, 1, 0.8] }, { "slotType": 3, "position_engine": [-535, 27, -665], "turretAngles": [0.896, 0.896, 0.896] }, { "slotType": 3, "position_engine": [-202, 111, -608], "turretAngles": [1, 1, 1] }]`
- **48 Amboss**: `[{ "slotType": 0, "position_engine": [-130, -354, 452] }, { "slotType": 0, "position_engine": [129, -353, 452] }, { "slotType": 0, "position_engine": [265, -73, 104] }, { "slotType": 0, "position_engine": [-267, -73, 104] }, { "slotType": 1, "position_engine": [184, 42, 1] }, { "slotType": 1, "position_engine": [87, 42, 1] }, { "slotType": 1, "position_engine": [-88, 42, 1] }, { "slotType": 1, "position_engine": [-185, 42, 1] }, { "slotType": 3, "position_engine": [0, -332, 81], "turretAngles": [0.8, 0.8, 0.8] }, { "slotType": 3, "position_engine": [0, -268, 82], "turretAngles": [0.8, 0.8, 0.8] }, { "slotType": 3, "position_engine": [0, 277, -429], "turretAngles": [0.8, 0.8, 0.8] }, { "slotType": 3, "position_engine": [0, 195, -429], "turretAngles": [0.8, 0.8, 0.8] }]`
- **49 Scimitar**: `[{ "slotType": 0, "position_engine": [-506, -125, 1061] }, { "slotType": 0, "position_engine": [0, 106, -316] }, { "slotType": 0, "position_engine": [513, -125, 1061] }, { "slotType": 1, "position_engine": [229, 73, -183] }, { "slotType": 1, "position_engine": [-228, 73, -184] }, { "slotType": 3, "position_engine": [317, 6, -1107], "turretAngles": [0.6, 0.6, 0.6] }, { "slotType": 3, "position_engine": [194, 79, -1091], "turretAngles": [0.6, 0.6, 0.6] }, { "slotType": 3, "position_engine": [99, 109, -1121], "turretAngles": [0.663, 0.663, 0.663] }, { "slotType": 3, "position_engine": [-316, 4, -1108], "turretAngles": [0.6, 0.6, 0.6] }, { "slotType": 3, "position_engine": [-194, 80, -1092], "turretAngles": [0.6, 0.6, 0.6] }, { "slotType": 3, "position_engine": [-100, 109, -1121], "turretAngles": [0.663, 0.663, 0.663] }, { "slotType": 3, "position_engine": [-152, 109, -1118], "turretAngles": [0.6, 0.6, 0.6] }, { "slotType": 3, "position_engine": [152, 109, -1118], "turretAngles": [0.6, 0.6, 0.6] }]`
- **51 Rhino**: `[{ "slotType": 1, "position_engine": [665, -43, -109] }, { "slotType": 1, "position_engine": [-665, -43, -109] }, { "slotType": 2, "position_engine": [0, 522, -57] }, { "slotType": 3, "position_engine": [-157, 130, -992], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [0, 130, -992], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [157, 130, -992], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [579, -50, -793], "turretAngles": [0.503, 0.503, 0.503] }, { "slotType": 3, "position_engine": [579, 55, -793], "turretAngles": [0.503, 0.503, 0.503] }, { "slotType": 3, "position_engine": [454, 16, -793], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [-455, 16, -793], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [-584, -50, -793], "turretAngles": [0.503, 0.503, 0.503] }, { "slotType": 3, "position_engine": [-584, 55, -793], "turretAngles": [0.503, 0.503, 0.503] }]`
- **52 Gryphon**: `[{ "slotType": 0, "position_engine": [594, -27, -81] }, { "slotType": 0, "position_engine": [-594, -27, -81] }, { "slotType": 0, "position_engine": [894, -27, -170] }, { "slotType": 0, "position_engine": [-894, -27, -170] }, { "slotType": 1, "position_engine": [202, 1, 58] }, { "slotType": 1, "position_engine": [-202, 1, 58] }, { "slotType": 3, "position_engine": [295, -25, -789], "turretAngles": [0.6, 0.6, 0.6] }, { "slotType": 3, "position_engine": [-295, -25, -790], "turretAngles": [0.6, 0.6, 0.6] }, { "slotType": 3, "position_engine": [-83, 7, -846], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [83, 7, -847], "turretAngles": [1, 1, 1] }]`
- **54 Na'srrk**: `[{ "slotType": 0, "position_engine": [344, -42, 490] }, { "slotType": 0, "position_engine": [-344, -42, 490] }, { "slotType": 0, "position_engine": [378, -47, 450] }, { "slotType": 0, "position_engine": [-378, -47, 450] }, { "slotType": 1, "position_engine": [185, -29, 501] }, { "slotType": 1, "position_engine": [-185, -29, 501] }, { "slotType": 1, "position_engine": [240, -34, 426] }, { "slotType": 1, "position_engine": [-240, -34, 426] }, { "slotType": 3, "position_engine": [0, 67, -549], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [-21, 15, -475], "turretAngles": [0.3, 0.3, 0.3] }, { "slotType": 3, "position_engine": [20, 15, -475], "turretAngles": [0.3, 0.3, 0.3] }]`
- **55 Groza Mk II**: `[{ "slotType": 0, "position_engine": [0, -114, 661] }, { "slotType": 0, "position_engine": [300, -73, 317] }, { "slotType": 0, "position_engine": [-300, -73, 317] }, { "slotType": 0, "position_engine": [212, -42, 124] }, { "slotType": 0, "position_engine": [-212, -42, 124] }, { "slotType": 1, "position_engine": [0, -46, 589] }, { "slotType": 3, "position_engine": [0, 4, -536], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [-111, 3, -483], "turretAngles": [0.4, 0.4, 0.4] }, { "slotType": 3, "position_engine": [-89, -62, -483], "turretAngles": [0.4, 0.4, 0.4] }, { "slotType": 3, "position_engine": [89, -62, -483], "turretAngles": [0.4, 0.4, 0.4] }, { "slotType": 3, "position_engine": [111, 3, -483], "turretAngles": [0.4, 0.4, 0.4] }]`
- **56 Berger Cross Special**: `[{ "slotType": 0, "position_engine": [233, 46, 605] }, { "slotType": 0, "position_engine": [-233, 46, 605] }, { "slotType": 0, "position_engine": [235, -66, 679] }, { "slotType": 0, "position_engine": [-235, -66, 679] }, { "slotType": 1, "position_engine": [114, -71, 181] }, { "slotType": 1, "position_engine": [-114, -71, 181] }, { "slotType": 1, "position_engine": [434, -165, 219] }, { "slotType": 1, "position_engine": [-434, -165, 219] }, { "slotType": 3, "position_engine": [0, 66, -249], "turretAngles": [0.6, 0.6, 0.6] }, { "slotType": 3, "position_engine": [-231, -19, -65], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [-80, 197, -373], "turretAngles": [0.6, 0.6, 0.6] }, { "slotType": 3, "position_engine": [80, 197, -373], "turretAngles": [0.6, 0.6, 0.6] }, { "slotType": 3, "position_engine": [233, -19, -65], "turretAngles": [1, 1, 1] }]`
- **57 Kinzer RS**: `[{ "slotType": 0, "position_engine": [57, -50, 299] }, { "slotType": 0, "position_engine": [-57, -50, 299] }, { "slotType": 0, "position_engine": [251, -69, 823] }, { "slotType": 0, "position_engine": [-251, -69, 823] }, { "slotType": 1, "position_engine": [156, -55, -181] }, { "slotType": 1, "position_engine": [-156, -55, -181] }, { "slotType": 1, "position_engine": [502, -133, 13] }, { "slotType": 1, "position_engine": [-502, -133, 13] }, { "slotType": 3, "position_engine": [-1, 12, -646], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [-122, 10, -806], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [119, 10, -806], "turretAngles": [0.7, 0.7, 0.7] }]`
- **58 Phantom XT**: `[{ "slotType": 0, "position_engine": [20, -95, 497] }, { "slotType": 0, "position_engine": [-20, -95, 497] }, { "slotType": 0, "position_engine": [142, -98, -84] }, { "slotType": 0, "position_engine": [-142, -98, -84] }, { "slotType": 1, "position_engine": [0, -138, 456] }, { "slotType": 3, "position_engine": [44, -55, -443], "turretAngles": [0.479, 0.479, 0.479] }, { "slotType": 3, "position_engine": [-44, -55, -443], "turretAngles": [0.479, 0.479, 0.479] }, { "slotType": 3, "position_engine": [0, 16, -474], "turretAngles": [0.7, 0.7, 0.7] }]`
- **59 Teneta R.E.D.**: `[{ "slotType": 0, "position_engine": [123, -83, 584] }, { "slotType": 0, "position_engine": [-123, -83, 584] }, { "slotType": 0, "position_engine": [566, -27, 102] }, { "slotType": 0, "position_engine": [-566, -27, 102] }, { "slotType": 1, "position_engine": [491, -99, -117] }, { "slotType": 1, "position_engine": [-491, -99, -117] }, { "slotType": 2, "position_engine": [0, 147, -239] }, { "slotType": 3, "position_engine": [231, -42, -533], "turretAngles": [0.8, 0.8, 0.8] }, { "slotType": 3, "position_engine": [-231, -42, -533], "turretAngles": [0.8, 0.8, 0.8] }, { "slotType": 3, "position_engine": [0, -15, -611], "turretAngles": [1, 1, 1] }]`
- **60 Darkzov**: `[{ "slotType": 0, "position_engine": [214, -16, 59] }, { "slotType": 0, "position_engine": [-214, -16, 59] }, { "slotType": 0, "position_engine": [324, -73, 187] }, { "slotType": 0, "position_engine": [-324, -73, 187] }, { "slotType": 1, "position_engine": [2, -147, 21] }, { "slotType": 2, "position_engine": [0, 99, -288] }, { "slotType": 3, "position_engine": [42, -64, -491], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [-42, -64, -491], "turretAngles": [0.5, 0.5, 0.5] }, { "slotType": 3, "position_engine": [-106, 42, -511], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [102, 42, -511], "turretAngles": [0.7, 0.7, 0.7] }]`
- **61 Ghost**: `[{ "slotType": 0, "position_engine": [543, -177, 538] }, { "slotType": 0, "position_engine": [-545, -178, 538] }, { "slotType": 0, "position_engine": [275, 11, 872] }, { "slotType": 0, "position_engine": [-274, 11, 872] }, { "slotType": 1, "position_engine": [-364, 105, -638] }, { "slotType": 1, "position_engine": [365, 106, -638] }, { "slotType": 3, "position_engine": [290, 0, -786], "turretAngles": [1.1, 1.1, 1.1] }, { "slotType": 3, "position_engine": [-299, 1, -786], "turretAngles": [1.1, 1.1, 1.1] }, { "slotType": 3, "position_engine": [27, 75, -717], "turretAngles": [0.7, 0.7, 0.7] }, { "slotType": 3, "position_engine": [-28, 75, -718], "turretAngles": [0.7, 0.7, 0.7] }]`
- **62 Dark Angel**: `[{ "slotType": 0, "position_engine": [-499, -126, 151] }, { "slotType": 0, "position_engine": [-645, -194, 525] }, { "slotType": 0, "position_engine": [499, -126, 151] }, { "slotType": 0, "position_engine": [645, -194, 525] }, { "slotType": 1, "position_engine": [-300, -118, 86] }, { "slotType": 1, "position_engine": [300, -118, 86] }, { "slotType": 2, "position_engine": [0, 133, -184] }, { "slotType": 3, "position_engine": [0, -13, -449], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [128, 128, -734], "turretAngles": [1, 1, 1] }, { "slotType": 3, "position_engine": [-130, 128, -734], "turretAngles": [1, 1, 1] }]`
- **63 N'Tirrk**: `[{ "slotType": 0, "position_engine": [488, -56, 519] }, { "slotType": 0, "position_engine": [-488, -56, 519] }, { "slotType": 1, "position_engine": [-279, -17, 537] }, { "slotType": 1, "position_engine": [279, -17, 537] }, { "slotType": 1, "position_engine": [275, -17, 537] }, { "slotType": 1, "position_engine": [-275, -17, 537] }, { "slotType": 3, "position_engine": [488, -9, -183], "turretAngles": [0.3, 0.3, 0.3] }, { "slotType": 3, "position_engine": [-488, -9, -183], "turretAngles": [0.3, 0.3, 0.3] }, { "slotType": 3, "position_engine": [0, 122, -545], "turretAngles": [0.5, 0.5, 0.5] }]`

## Default economy

The PC / Mac / iPhone numbers where they differ from the Android ones above (Android -> Default). Put them in an item's
`defaultEconomy` block or a ship's `priceDefault` when your mod should match both economies.

| Item # | Name | Changes |
|---|---|---|
| 0 | Nirai Impulse EX 1 | price 400-440 -> 2000-2381 |
| 2 | Nirai Charged Pulse | price 9510-10461 -> 9510-10959 |
| 3 | V'skorr | price 8000-8800 -> 8000-9291 |
| 5 | Berger Focus I | price 19100-21010 -> 19100-21900 |
| 11 | M6 A3 "Wolverine" | price 101629-107979 -> 63000-65500; blueprint: 1 x 9, 10 x 155, 14 x 120, 10 x 126, 5 x 122, 5 x 129 |
| 12 | N'saan | price 10580-11638 -> 10580-12580 |
| 13 | K'booskk | price 14500-15950 -> 14500-16305 |
| 15 | H'nookk | price 48513-52401 -> 42675-47037; techLevel 6 -> 5; occurrence 15 -> 30; blueprint: 1 x 12, 14 x 130, 9 x 129, 8 x 122, 1 x 166, 9 x 125 |
| 16 | Luna EMP Mk I | price 4800-5280 -> 4800-5700 |
| 17 | Sol EMP Mk II | price 10500-11550 -> 10500-12000 |
| 18 | Dia EMP Mk III | price 34120-37532 -> 34120-39549 |
| 22 | Micro Gun MK I | price 440-484 -> 2200-2458 |
| 24 | 64MJ Railgun | price 12300-13530 -> 12300-15000 |
| 25 | 128MJ Railgun | price 24615-26687 -> 23500-24500; techLevel 6 -> 5; occurrence 30 -> 40; blueprint: 5 x 125, 8 x 121, 11 x 122, 8 x 120, 15 x 127 |
| 26 | Scram Cannon | techLevel 7 -> 6 |
| 27 | Mass Driver MD 10 | price 113519-120463 -> 92000-110000; occurrence 10 -> 30; blueprint: 55 x 154, 31 x 122, 16 x 125, 20 x 127, 27 x 130 |
| 28 | Thermic o5 | price 8000-8800 -> 8000-9517 |
| 33 | Amour Rocket | price 97-101 -> 50-59; blueprint: 3 x 118, 1 x 122, 2 x 120 |
| 34 | EMP Rocket Mk I | price 50-55 -> 50-59 |
| 37 | Intelli Jet | price 259-273 -> 130-151; blueprint: 2 x 128, 2 x 129, 2 x 127, 1 x 125 |
| 38 | S'koonn | price 170-187 -> 170-191 |
| 42 | EMP GL II | price 180-198 -> 180-211 |
| 44 | AMR Tormentor | price 800-855 -> 500-555 |
| 46 | AMR Extinctor | price 4719-4968 -> 3000-3124; occurrence 10 -> 20; blueprint: 40 x 124, 20 x 114, 7 x 128, 5 x 127, 5 x 125, 10 x 118 |
| 47 | Hammerhead D1 | price 20000-22000 -> 20000-23075 |
| 50 | Targe Shield | price 3750-4125 -> 1500-1561; techLevel 2 -> 1 |
| 51 | Riot Shield | price 11500-12650 -> 4600-5081; techLevel 4 -> 3 |
| 52 | H'Belam | price 30750-33825 -> 12300-12491; techLevel 6 -> 5 |
| 53 | Beamshield II | price 89500-98450 -> 35800-37532; techLevel 8 -> 7 |
| 54 | Fluxed Matter Shield | price 263825-279957 -> 88000-97069; blueprint: 80 x 163, 75 x 118, 20 x 125, 65 x 121, 80 x 127 |
| 55 | E2 Exoclad | price 2250-2475 -> 900-1025; techLevel 2 -> 1 |
| 56 | E4 Ultra Lamina | price 9500-10450 -> 3800-4559; techLevel 4 -> 3 |
| 57 | E6 D-X Plating | price 41000-45100 -> 16400-19265; techLevel 6 -> 5 |
| 58 | D'iol | price 107500-118250 -> 43000-49613; techLevel 8 -> 7 |
| 59 | T'yol | price 169484-183378 -> 107000-112435; occurrence 5 -> 10; blueprint: 40 x 157, 15 x 168, 35 x 156, 40 x 125, 30 x 120 |
| 60 | AMR Saber | price 190-209 -> 190-300 |
| 61 | Neétha EMP | price 340-374 -> 340-500 |
| 63 | ZMI Optistore | price 2100-2310 -> 2100-2463 |
| 64 | Autopacker 2 | price 4700-5170 -> 4700-5488 |
| 65 | Ultracompact | price 13000-14300 -> 13000-14350 |
| 66 | Shrinker BT | price 24800-27280 -> 24800-27281 |
| 67 | Rhoda Blackhole | price 104550-110700 -> 55900-63203; techLevel 10 -> 9; occurrence 5 -> 20; blueprint: 45 x 118, 17 x 122, 50 x 125, 60 x 130 |
| 68 | AB-1 "Retractor" | price 150000-154500 -> 8400-8567; techLevel 6 -> 4; occurrence 10 -> 80 |
| 69 | AB-2 "Glue Gun" | price 450000-459000 -> 24500-26179; techLevel 7 -> 5; occurrence 8 -> 50 |
| 70 | AB-3 "Kingfisher" | price 1125000-1136250 -> 52700-58544; techLevel 8 -> 7; occurrence 6 -> 20 |
| 71 | Linear Boost | price 4800-5280 -> 4800-5455 |
| 72 | Cyclotron Boost | price 10400-11440 -> 10400-11158 |
| 74 | Me'al | price 38700-42570 -> 38700-44869 |
| 75 | Ketar Repair Bot | price 36250-39875 -> 14500-14575 |
| 76 | Static Thrust | price 1200-1320 -> 1200-1336 |
| 78 | D'ozzt Thrust | price 4700-5170 -> 4700-5552 |
| 81 | Telta Quickscan | price 460-506 -> 2300-2326 |
| 83 | Hiroto Proscan | price 33500-36850 -> 33500-37165 |
| 84 | Hiroto Ultrascan | price 175200-179204 -> 87600-91604 |
| 85 | Khador Drive | price 624803-662923 -> 270300-296082; blueprint: 50 x 164, 10 x 158, 10 x 160, 10 x 163, 40 x 118, 70 x 125, 75 x 127, 40 x 129 |
| 86 | IMT Extract 1.3 | price 480-528 -> 3700-4234 |
| 87 | IMT Extract 2.7 | price 20280-22308 -> 15600-16654; techLevel 6 -> 4; drillSpeed 40 -> 50; miningYield 60 -> 50 |
| 88 | K'yuul | price 40820-44902 -> 31400-36157; techLevel 7 -> 5; drillSpeed 50 -> 80; miningYield 75 -> 40 |
| 89 | IMT Extract 4.0X | price 85020-93522 -> 65400-70570; techLevel 8 -> 7; drillSpeed 60 -> 70 |
| 90 | Gunant's Drill | price 220426-236114 -> 168000-201116 |
| 91 | Small Cabin | price 2600-2860 -> 2600-3043 |
| 92 | Medium Cabin | price 5900-6195 -> 5900-6053 |
| 93 | Large Cabin | price 12800-13440 -> 12800-13593 |
| 94 | Sight Suppressor II | price 61750-67925 -> 24700-28402; techLevel 6 -> 4 |
| 95 | U'tool | price 95000-104500 -> 38000-45424; techLevel 7 -> 6 |
| 96 | Yin Co. Shadow Ninja | price 273204-288941 -> 59800-66060; blueprint: 40 x 126, 15 x 119, 20 x 130, 40 x 129, 10 x 127, 40 x 124 |
| 97 | Drinking Water | price 10-11 -> 10-60 |
| 98 | Medical Supplies | price 50-55 -> 50-240 |
| 99 | Space Waste | price 5-6 -> 5-7 |
| 100 | Artefacts | price 1200-1320 -> 1200-2000 |
| 101 | Rare Animals | price 2700-2970 -> 2700-3400 |
| 102 | Rare Plants | price 3800-4180 -> 3800-5000 |
| 103 | Drugs | price 20-40 -> 20-400 |
| 104 | Luxury | price 1070-1177 -> 1070-1500 |
| 105 | Premium Food | price 30-40 -> 30-260 |
| 106 | Basic Food | price 3-4 -> 3-20 |
| 107 | Organs | price 4200-4620 -> 4200-6000 |
| 108 | Vossk Organs | price 22000-24200 -> 22000-26000 |
| 109 | Buskat | price 9000-9900 -> 9000-12000 |
| 110 | Collectible Figure | price 100-1000 -> 10-10000 |
| 111 | Digital Watch | price 5-7 -> 5-100 |
| 112 | Towels | price 5-7 -> 5-200 |
| 113 | Implants | price 6450-7095 -> 6450-9000 |
| 114 | Explosives | price 370-400 -> 150-400 |
| 115 | Documents | price 1200-1320 -> 1200-1600 |
| 118 | Electronics | price 400-420 -> 50-180 |
| 119 | Chemicals | price 130-143 -> 30-200 |
| 120 | Plastics | price 150-165 -> 15-180 |
| 121 | Nanotech | price 875-963 -> 150-400 |
| 122 | Energy Cells | price 1400-1540 -> 560-700 |
| 123 | Biowaste | price 4-6 -> 4-16 |
| 124 | Radioactive Goods | price 260-286 -> 90-300 |
| 125 | Mechanical Supplies | price 450-495 -> 60-200 |
| 126 | Noble Gas | price 360-396 -> 180-200 |
| 127 | Microchips | price 163-179 -> 15-80 |
| 128 | Com. Devices | price 75-83 -> 75-230 |
| 129 | Optics | price 400-440 -> 50-180 |
| 130 | Hydraulics | price 270-297 -> 56-120 |
| 131 | Alien Remains | price 40-44 -> 199-199 |
| 132 | Suteo Liqueur | price 139-153 -> 139-195 |
| 133 | Pan Whiskey | price 489-538 -> 489-636 |
| 134 | Behén Wine | price 789-868 -> 789-1624 |
| 135 | V'ikka Moonshine | price 237-261 -> 237-322 |
| 136 | Eanya Tonic | price 211-232 -> 211-388 |
| 137 | S'kolptorr Rum | price 245-270 -> 245-480 |
| 138 | Wolf-Reiser Brandy | price 99-109 -> 99-202 |
| 139 | Aquila Cocktail | price 320-352 -> 320-648 |
| 140 | Buntta Apéritif | price 259-285 -> 259-314 |
| 141 | Weymire Punch | price 1073-1180 -> 1073-1679 |
| 142 | Y'mirr Schnaps | price 89-98 -> 89-205 |
| 143 | Union Draught | price 105-116 -> 105-178 |
| 144 | Oom'bak Gin | price 113-124 -> 113-208 |
| 145 | Vulpes Soup | price 398-438 -> 398-537 |
| 146 | Magnetar Juice | price 1100-1210 -> 264-487 |
| 147 | Mido Distillate | price 453-498 -> 453-668 |
| 148 | Prospero Flip | price 262-288 -> 262-374 |
| 149 | Nesla Brandy | price 258-284 -> 258-337 |
| 150 | Pescal Inartu Brew | price 829-912 -> 829-1319 |
| 151 | Augmenta Fizz | price 1125-1238 -> 1125-1759 |
| 152 | K'ontrr Dishwater | price 578-636 -> 578-931 |
| 153 | Ni'mrrod Muck | price 510-561 -> 510-835 |
| 154 | Gold | price 80-88 -> 80-100 |
| 155 | Titanium | price 150-165 -> 150-200 |
| 157 | Orichalzine | price 120-132 -> 120-170 |
| 158 | Pyresium | price 80-88 -> 80-120 |
| 159 | Sodil | price 70-77 -> 70-110 |
| 160 | Doxtrite | price 35-40 -> 35-60 |
| 161 | Cesogen | price 44-48 -> 44-80 |
| 162 | Perrius | price 38-42 -> 38-74 |
| 163 | Hypanium | price 70-77 -> 80-90 |
| 164 | Void Crystals | price 40-44 -> 400-400 |
| 165 | Golden Core | price 414-455 -> 414-621 |
| 166 | Titanium Core | price 800-880 -> 805-1207 |
| 167 | Iron Core | price 115-127 -> 115-172 |
| 168 | Orichalzin Core | price 600-660 -> 667-1000 |
| 169 | Pyresium Core | price 460-506 -> 460-690 |
| 170 | Sodil Core | price 414-455 -> 414-621 |
| 171 | Doxtrit Core | price 218-240 -> 218-327 |
| 172 | Cesogen Core | price 240-264 -> 285-427 |
| 173 | Perrius Core | price 257-283 -> 257-385 |
| 174 | Hypanium Core | price 380-418 -> 380-480 |
| 175 | Void Essence | price 6500-6500 -> 5000-5000 |
| 176 | Nirai .50AS | price 37000-40700 -> 37000-42600; techLevel 6 -> 5 |
| 177 | Berger FlaK 9-9 | price 113100-124410 -> 113100-130100 |
| 178 | Icarus Heavy AS | price 407928-438997 -> 303700-349300; blueprint: 1 x 27, 15 x 125, 7 x 121, 20 x 122, 1 x 171, 1 x 186 |
| 179 | Liberator | price 80232-85313 -> 6100-7100; techLevel 8 -> 4; blueprint: 10 x 44, 15 x 124, 25 x 114, 5 x 127, 10 x 118, 8 x 129, 12 x 157, 2 x 166 |
| 180 | Berger AGT 20mm | price 193500-212850 -> 193500-222600; techLevel 6 -> 5 |
| 181 | Skuld AT XR | price 338100-371910 -> 338100-388900 |
| 182 | HH-AT "Archimedes" | price 500144-532463 -> 500100-575200; blueprint: 1 x 47, 1 x 15, 35 x 118, 22 x 125, 25 x 127, 5 x 128, 18 x 129, 40 x 168 |
| 183 | Disruptor Laser | price 173833-186554 -> 325200-374000; blueprint: 30 x 118, 25 x 121, 8 x 155, 5 x 168, 5 x 172, 30 x 120, 28 x 126, 45 x 122, 1 x 175 |
| 184 | Rhoda Vortex | price 312000-343200 -> 312000-358800 |
| 185 | Emergency System | price 7000-7700 -> 7000-8000 |
| 186 | Nirai Overdrive | price 25000-27500 -> 25000-28750 |
| 187 | Nirai Overcharge | price 25000-27500 -> 25000-28750 |
| 188 | Ketar Repair Bot II | price 118000-129800 -> 118000-135700 |
| 189 | Signature: Terran | price 500000-500000 -> 7500-7500; alwaysSoldAt 107 -> 0 |
| 190 | Signature: Vossk | price 500000-500000 -> 7500-7500; alwaysSoldAt 107 -> 0 |
| 191 | Signature: Nivelian | price 500000-500000 -> 7500-7500; alwaysSoldAt 107 -> 0 |
| 192 | Signature: Midorian | price 500000-500000 -> 7500-7500; alwaysSoldAt 107 -> 0 |
| 193 | SunFire o50 | price 152500-167750 -> 152500-175375 |
| 194 | AB-4 "Octopus" | price 3093750-3403125 -> 165000-189750; techLevel 10 -> 8 |
| 195 | Polytron Boost | price 72500-79750 -> 72500-83375 |
| 196 | Spectral Filter SA-1 | price 2300-2530 -> 23000-25000 |
| 197 | Ion Lambda Mk1 | price 4250-4675 -> 850-1050 |
| 198 | PE Proton | price 4100-4300 -> 41000-43000; turretTurnSpeed 100 -> 125; collectorSpeed 16 -> 26; collectorMagnitude 40 -> 80; collectorRange 27500 -> 40000 |
| 199 | PE Ambipolar-5 | price 593750-599688 -> 110000-113000 |
| 200 | PE Fusion H2 | price 1131000-1142310 -> 580000-620000 |
| 201 | Green Plasma | price 35-39 -> 35-60 |
| 202 | Blue Plasma | price 40-44 -> 40-45 |
| 203 | Purple Plasma | price 50-55 -> 50-80 |
| 204 | Red Plasma | price 80-88 -> 80-400 |
| 206 | Gamma Shield II | price 533256-586582 -> 41500-43000; blueprint: 1 x 205, 5 x 119, 7 x 121, 20 x 163, 12 x 172 |
| 207 | Nirai SPP-C1 | price 6200-6700 -> 62000-67000 |
| 209 | K'mirkk Toad Mutagen | price 500000-525000 -> 40000-60000 |
| 210 | Chromo Plasma | price 775000-786625 -> 470500-472000; blueprint: 917 x 201, 917 x 202, 917 x 203, 917 x 204 |
| 211 | Berger SG-100 | price 640-704 -> 640-750 |
| 212 | Berger SG-400 | price 1200-1320 -> 1200-1600 |
| 214 | Shesha | price 310-341 -> 310-350 |
| 215 | Garuda-IV | price 670-737 -> 670-780 |
| 216 | Patala | price 1300-1430 -> 1300-1600 |
| 219 | Spectral Filter ST-X | price 462500-467125 -> 148000-152000 |
| 220 | Spectral Filter Omega | price 916500-925665 -> 470500-476000 |
| 221 | Ion Lambda Mk2 | price 8128-8761 -> 1800-1950; blueprint: 10 x 197, 2 x 173, 20 x 114, 10 x 124 |
| 223 | Pandora Leech | price 508045-545097 -> 77000-79000; blueprint: 40 x 122, 68 x 129, 1 x 222, 25 x 204, 5 x 218 |
| 225 | Particle Shield | price 738210-780835 -> 147000-155000; blueprint: 80 x 163, 75 x 118, 12 x 120, 65 x 121, 80 x 122, 80 x 127, 35 x 201, 35 x 202, 5 x 218 |
| 226 | Shock Blast | price 7404-7905 -> 9000-9500; blueprint: 25 x 114, 60 x 122, 10 x 118, 8 x 126, 30 x 203 |
| 227 | Phoenix SIS | price 881346-900606 -> 520000-540000; blueprint: 5 x 118, 5 x 119, 25 x 122, 1 x 210 |
| 232 | Fireworks | price 1550000-1573250 -> 20000-22000 |

| Ship # | Name | Changes |
|---|---|---|
| 1 | Teneta | price 100320 -> 125400 |
| 4 | Dace | price 188480 -> 235600 |
| 7 | Anaan | price 141520 -> 176900 |
| 8 | VoidX | price 4057950 -> 8115900 |
| 9 | H'Soc | price 120000 -> 150000 |
| 10 | Phantom | price 966667 -> 1450000 |
| 11 | Hernstein | price 262240 -> 327800 |
| 12 | Type 43 | price 58000 -> 72500 |
| 16 | Kinzer | price 1418733 -> 2128100 |
| 17 | Ward | price 1103200 -> 1654800 |
| 18 | Hatsuyuki | price 137520 -> 171900 |
| 19 | Nuyang II | price 626467 -> 939700 |
| 20 | Cicero | price 42000 -> 52500 |
| 21 | Aegir | price 1808600 -> 2712900 |
| 22 | Groza | price 201280 -> 251600 |
| 23 | Azov | price 49520 -> 61900 |
| 24 | Velasco | price 456200 -> 684300 |
| 25 | Tyrion | price 253120 -> 316400 |
| 26 | Hera | price 85600 -> 107000 |
| 27 | Taipan | price 80080 -> 100100 |
| 28 | Veteran | price 1658933 -> 2488400 |
| 29 | Mantis | price 2363886 -> 4136800 |
| 30 | Berger CrossXT | price 70560 -> 88200 |
| 31 | Salvéhn | price 75600 -> 94500 |
| 33 | Furious | price 60640 -> 75800 |
| 34 | Razor 6 | price 235920 -> 294900 |
| 35 | Night Owl | slots 1/3/0/4 -> 1/2/0/4 |
| 36 | Cormorant | price 135120 -> 168900 |
| 37 | Cronus | price 0 -> 1200000 |
| 38 | Typhon | price 1666667 -> 2500000 |
| 40 | Nemesis | price 3400000 -> 6800000 |
| 41 | K'Suukk | price 1300000 -> 1950000 |
| 42 | Vol Noor | price 84000 -> 105000 |
| 43 | Wraith | price 1166667 -> 1750000 |
| 44 | Specter | price 12000000 -> 30000000 |
| 45 | Bloodstar | price 5400000 -> 13500000 |
| 46 | Blue Fyre | price 2571429 -> 4500000 |
| 47 | Gator Custom | price 2971429 -> 5200000 |
| 48 | Amboss | price 3400000 -> 6800000 |
| 49 | Scimitar | price 3314286 -> 5800000 |
| 51 | Rhino | price 466667 -> 700000 |
| 52 | Gryphon | price 1400000 -> 2100000 |
| 54 | Na'srrk | price 3085714 -> 5400000 |
| 55 | Groza Mk II | price 3565000 -> 7130000 |
| 56 | Berger Cross Special | price 3705000 -> 7410000 |
| 57 | Kinzer RS | price 4465000 -> 8930000 |
| 58 | Phantom XT | price 3715000 -> 7430000 |
| 59 | Teneta R.E.D. | price 3805000 -> 7610000 |
| 60 | Darkzov | price 3618000 -> 7236000 |
| 61 | Ghost | price 3333333 -> 6000000 |
| 62 | Dark Angel | price 3650000 -> 7300000 |
| 63 | N'Tirrk | price 200000 -> 250000 |

## Systems

Security 0 lawless .. 3 secure. Visible = on the star map from a new game's start. Gates = the systems its jumpgate
reaches; Gate station = the station whose orbit has the jumpgate. Sky = the nebula and sun it uses (a mod system's `sky`,
0-18; see Skies).

| # | Name | Race | Security | Visible | Map position | Sky | Gates | Gate station | Stations |
|---|---|---|---|---|---|---|---|---|---|
| 0 | Suteo | Nivelian | 1 |  | 22, 55, 55 | 4 | 2, 8 | 0 | 0, 1, 2, 3, 4 |
| 1 | Pan | Terran | 1 |  | 22, 81, 62 | 2 | 8, 18 | 5 | 5, 6, 7, 8, 9 |
| 2 | Behén | Nivelian | 1 | yes | 24, 40, 73 | 9 | 0, 8, 9, 17 | 30 | 30, 31, 32, 33 |
| 3 | V'ikka | Vossk | 2 | yes | 52, 61, 45 | 5 | 5, 8, 12, 14, 19 | 15 | 15, 16, 17, 18, 19 |
| 4 | Eanya | Midorian | 0 | yes | 15, 15, 90 | 8 | 17, 27 | 20 | 20, 21, 22, 23, 24 |
| 5 | S'kolptorr | Vossk | 1 | yes | 64, 67, 30 | 9 | 3, 12, 20 | 25 | 25, 26, 27, 28, 29 |
| 6 | Wolf-Reiser | Terran | 3 |  | 75, 20, 35 | 0 | 7 | 10 | 10 |
| 7 | Aquila | Terran | 1 | yes | 66, 15, 47 | 7 | 6, 11, 25 | 35 | 35, 36, 37, 38, 39 |
| 8 | Buntta | Terran | 2 | yes | 37, 63, 62 | 1 | 0, 1, 2, 3, 18, 19 | 40 | 40, 41, 42, 43, 44 |
| 9 | Weymire | Nivelian | 2 | yes | 39, 29, 63 | 7 | 2, 11, 17, 19, 26 | 45 | 45, 46, 47, 48, 49 |
| 10 | Y'mirr | Vossk | 0 |  | 81, 63, 60 | 6 |  |  | 50, 51, 52, 53, 54 |
| 11 | Union | Terran | 2 | yes | 55, 27, 59 | 3 | 7, 9, 14, 16, 25 | 55 | 55, 56, 57 |
| 12 | Oom'bak | Vossk | 2 | yes | 67, 55, 41 | 0 | 3, 5, 13, 14 | 60 | 60, 61 |
| 13 | Vulpes | Terran | 1 | yes | 78, 47, 55 | 4 | 12, 16 | 65 | 65, 66, 67, 68, 69 |
| 14 | Magnetar | Terran | 3 | yes | 57, 42, 39 | 5 | 3, 11, 12, 19 | 70 | 70, 71, 72, 73, 74 |
| 15 | Mido | Midorian | 0 | yes | 29, 10, 90 | 9 |  |  | 75, 76, 77, 78, 79 |
| 16 | Prospero | Terran | 1 | yes | 77, 32, 74 | 3 | 11, 13 | 80 | 80, 81, 82, 83, 84 |
| 17 | Nesla | Nivelian | 1 | yes | 26, 21, 79 | 8 | 2, 4, 9, 26, 29 | 85 | 85, 86, 87, 88, 89 |
| 18 | Pescal Inartu | Terran | 1 | yes | 43, 85, 81 | 1 | 1, 8 | 90 | 90, 91, 92, 93, 94 |
| 19 | Augmenta | Terran | 3 | yes | 44, 46, 50 | 6 | 3, 8, 9, 14 | 95 | 95, 96, 97, 98, 99 |
| 20 | K'ontrr | Vossk | 0 | yes | 77, 79, 20 | 1 | 5, 21, 30, 31 | 62 | 58, 59, 62, 63, 64 |
| 21 | Ni'mrrod | Vossk | 0 |  | 89, 84, 10 | 9 | 20, 30, 31 | 12 | 11, 12, 13, 14, 34 |
| 22 | Beidan | Terran | 3 |  | 92, 40, 32 | 12 |  |  | 100 |
| 23 | Herjaza | Terran | 1 |  | 59, 82, 56 | 11 |  |  | 101, 102 |
| 24 | Skavac | Terran | 0 |  | 68, 93, 69 | 13 |  |  | 103, 104 |
| 25 | Loma | Midorian | 0 |  | 43, 7, 70 | 14 | 11, 7, 26 | 105 | 105, 106, 107 |
| 26 | Shima | Midorian | 0 | yes | 36, 16, 50 | 1 | 17, 25, 9 | 108 | 108 |
| 27 | Ginoya | Midorian | 0 |  | 18, 8, 90 | 15 | 28, 4 | 112 | 109, 110, 111, 112, 113 |
| 28 | Talidor | Midorian | 0 |  | 24, 7, 77 | 17 | 27 | 116 | 114, 115, 116, 117, 118 |
| 29 | Paréah | Nivelian | 2 |  | 18, 31, 58 | 16 | 17 | 120 | 119, 120, 121, 122, 123 |
| 30 | Me'enkk | Vossk | 0 |  | 82, 96, 69 | 9 | 20, 21 | 125 | 124, 125, 126, 127 |
| 31 | Wah'norr | Vossk | 0 |  | 94, 59, 50 | 18 | 20, 21 | 130 | 128, 129, 130, 131 |
| 32 | Skor Terpa | Terran | 0 |  | 50, 2, 65 | 13 |  |  | 132 |
| 33 | Alda | Terran | 0 |  | 53, 89, 70 | 7 |  |  | 133, 134 |

Special places: -1 = the Void's home orbit (the alien orbit, "void" in event graphs).

## Skies

What a system's `sky` number looks like: the nebula layer (`nebula_0NN`, drawn over one of three star layers) and the
sun. A mod system can keep the sky and replace only the sun (`sunTexture`) or the nebula (`skybox`).

| Sky | Nebula | Sun | Used by systems |
|---|---|---|---|
| 0 | green-teal nebula clouds | sun_000 (green) | 6 Wolf-Reiser, 12 Oom'bak |
| 1 | dark red with a bright fiery red-orange streak | sun_001 (orange) | 8 Buntta, 18 Pescal Inartu, 20 K'ontrr, 26 Shima |
| 2 | faint dim blue-green and brown wisps (dark sky) | sun_002 (pale green) | 1 Pan |
| 3 | bright blue nebula clouds | sun_003 (magenta) | 11 Union, 16 Prospero |
| 4 | faint grey-blue wisps with a bright white band | sun_004 (small white) | 0 Suteo, 13 Vulpes |
| 5 | red-orange fiery clouds | sun_005 (red) | 3 V'ikka, 14 Magnetar |
| 6 | dim green and pink clouds with bright white bands | sun_006 (pale turquoise) | 10 Y'mirr, 19 Augmenta |
| 7 | bright white-gold and grey-green clouds | sun_007 (yellow) | 7 Aquila, 9 Weymire, 33 Alda |
| 8 | green and golden-yellow clouds | sun_008 (cyan-blue) | 4 Eanya, 17 Nesla |
| 9 | dim grey-violet wisps (dark sky) | sun_009 (white) | 2 Behén, 5 S'kolptorr, 15 Mido, 21 Ni'mrrod, 30 Me'enkk |
| 10 | violet and purple clouds (the Void) | sun_010 (blue) |  |
| 11 | the whole sky orange-gold haze | sun_001 (orange) | 23 Herjaza |
| 12 | the whole sky blue haze | sun_008 (cyan-blue) | 22 Beidan |
| 13 | mostly dark, faint grey wisps and one bright white streak | sun_004 (small white) | 24 Skavac, 32 Skor Terpa |
| 14 | yellow-green clouds | sun_000 (green) | 25 Loma |
| 15 | the whole sky orange with bright gold clusters (the supernova) | sn_supernova (the supernova) | 27 Ginoya |
| 16 | pale turquoise and white storm haze | sun_007 (yellow) | 29 Paréah |
| 17 | white glare over a turquoise cloud horizon | sun_002 (pale green) | 28 Talidor |
| 18 | white glare over a golden-yellow cloud horizon | sun_006 (pale turquoise) | 31 Wah'norr |

## Stations

78 Var Hastra (Mido) is where a new game starts. 108 is the Kaamo Club. Tech = which items its shop can sell. Planet = its
planet texture in the other orbits (a mod station's `planet`). Race = its system's (the hangar and bar it docks into). Model =
its assembled model: what `looksLike` borrows (only the model and its collision: a station's special rules, like the
Kaamo Club's storage and siege, belong to the original station's number and don't come with the look).

| # | Name | System | Race | Tech | Planet | Model |
|---|---|---|---|---|---|---|
| 0 | Nehébru | 0 Suteo | Nivelian | 8 | 0 | station_000_nivelian |
| 1 | Mahasa | 0 Suteo | Nivelian | 7 | 15 | station_001_nivelian |
| 2 | Anesa | 0 Suteo | Nivelian | 10 | 11 | station_002_nivelian |
| 3 | Géhlon | 0 Suteo | Nivelian | 9 | 19 | station_003_nivelian |
| 4 | Posého | 0 Suteo | Nivelian | 10 | 8 | station_004_nivelian |
| 5 | Decimus | 1 Pan | Terran | 9 | 3 | station_005_terran |
| 6 | Emisto | 1 Pan | Terran | 9 | 10 | station_006_terran |
| 7 | Binon | 1 Pan | Terran | 10 | 14 | station_007_terran |
| 8 | Taygete | 1 Pan | Terran | 9 | 13 | station_008_terran |
| 9 | Coppolite | 1 Pan | Terran | 8 | 17 | station_009_terran |
| 10 | Thynome | 6 Wolf-Reiser | Terran | 10 | 9 | station_010_terran |
| 11 | S'porrk | 21 Ni'mrrod | Vossk | 3 | 16 | station_vossk |
| 12 | Ukka N'ima | 21 Ni'mrrod | Vossk | 2 | 7 | station_vossk |
| 13 | Ekrr U'rra | 21 Ni'mrrod | Vossk | 1 | 12 | station_vossk |
| 14 | K'ane | 21 Ni'mrrod | Vossk | 3 | 2 | station_vossk |
| 15 | T'rry T'rry | 3 V'ikka | Vossk | 3 | 17 | station_vossk |
| 16 | N'arrki | 3 V'ikka | Vossk | 9 | 4 | station_vossk |
| 17 | Me'tokka | 3 V'ikka | Vossk | 7 | 18 | station_vossk |
| 18 | P'errka | 3 V'ikka | Vossk | 4 | 5 | station_vossk |
| 19 | Ma'rrkka | 3 V'ikka | Vossk | 4 | 6 | station_vossk |
| 20 | Kallsta Omba | 4 Eanya | Midorian | 5 | 11 | station_020_midorian |
| 21 | Euclades | 4 Eanya | Midorian | 1 | 14 | station_021_midorian |
| 22 | Dekato | 4 Eanya | Midorian | 9 | 3 | station_022_midorian |
| 23 | Psa Tori | 4 Eanya | Midorian | 8 | 5 | station_023_midorian |
| 24 | Sao Noma | 4 Eanya | Midorian | 8 | 7 | station_024_midorian |
| 25 | S'inokk | 5 S'kolptorr | Vossk | 5 | 0 | station_vossk |
| 26 | F'err | 5 S'kolptorr | Vossk | 10 | 17 | station_vossk |
| 27 | B'akka | 5 S'kolptorr | Vossk | 4 | 4 | station_vossk |
| 28 | B'errokk | 5 S'kolptorr | Vossk | 9 | 10 | station_vossk |
| 29 | Ga'kkrr | 5 S'kolptorr | Vossk | 6 | 18 | station_vossk |
| 30 | Néhma | 2 Behén | Nivelian | 6 | 1 | station_030_nivelian |
| 31 | Neso Aehma | 2 Behén | Nivelian | 4 | 8 | station_031_nivelian |
| 32 | Duhnu | 2 Behén | Nivelian | 6 | 13 | station_032_nivelian |
| 33 | Néh Suhnu | 2 Behén | Nivelian | 4 | 9 | station_033_nivelian |
| 34 | Narsaxa | 21 Ni'mrrod | Vossk | 3 | 1 | station_vossk |
| 35 | Io Ombak | 7 Aquila | Terran | 8 | 6 | station_035_terran |
| 36 | Dendra Ekta | 7 Aquila | Terran | 3 | 16 | station_036_terran |
| 37 | Carme | 7 Aquila | Terran | 10 | 11 | station_037_terran |
| 38 | Nepis | 7 Aquila | Terran | 7 | 9 | station_038_terran |
| 39 | Tsoj Delev | 7 Aquila | Terran | 7 | 18 | station_039_terran |
| 40 | Valadon | 8 Buntta | Terran | 3 | 16 | station_040_terran |
| 41 | Ko-on | 8 Buntta | Terran | 5 | 2 | station_041_terran |
| 42 | Epigome | 8 Buntta | Terran | 8 | 13 | station_042_terran |
| 43 | Arpalys | 8 Buntta | Terran | 4 | 19 | station_043_terran |
| 44 | Isonoma | 8 Buntta | Terran | 9 | 10 | station_044_terran |
| 45 | Éhna | 9 Weymire | Nivelian | 3 | 3 | station_045_nivelian |
| 46 | Himo | 9 Weymire | Nivelian | 6 | 12 | station_046_nivelian |
| 47 | Bosméh | 9 Weymire | Nivelian | 7 | 17 | station_047_nivelian |
| 48 | Sahi | 9 Weymire | Nivelian | 8 | 0 | station_048_nivelian |
| 49 | Siaméh | 9 Weymire | Nivelian | 1 | 14 | station_049_nivelian |
| 50 | P'arrenkk | 10 Y'mirr | Vossk | 10 | 1 | station_vossk |
| 51 | Rr'ostam | 10 Y'mirr | Vossk | 10 | 15 | station_vossk |
| 52 | G'ukkion | 10 Y'mirr | Vossk | 10 | 5 | station_vossk |
| 53 | Ba'rrtu | 10 Y'mirr | Vossk | 9 | 7 | station_vossk |
| 54 | K'mirkk | 10 Y'mirr | Vossk | 10 | 8 | station_vossk |
| 55 | Kappa | 11 Union | Terran | 5 | 4 | station_055_terran |
| 56 | Suttnar | 11 Union | Terran | 2 | 15 | station_056_terran |
| 57 | Tornard | 11 Union | Terran | 2 | 6 | station_057_terran |
| 58 | B'akrram | 20 K'ontrr | Vossk | 10 | 1 | station_vossk |
| 59 | E'kkide | 20 K'ontrr | Vossk | 3 | 11 | station_vossk |
| 60 | Kkit S'ukk | 12 Oom'bak | Vossk | 6 | 6 | station_vossk |
| 61 | L'ikirr | 12 Oom'bak | Vossk | 6 | 18 | station_vossk |
| 62 | Makke S'ik | 20 K'ontrr | Vossk | 9 | 14 | station_vossk |
| 63 | A'rk Oomk | 20 K'ontrr | Vossk | 6 | 3 | station_vossk |
| 64 | Bak W'ok | 20 K'ontrr | Vossk | 8 | 12 | station_vossk |
| 65 | Lopat | 13 Vulpes | Terran | 7 | 2 | station_065_terran |
| 66 | Inari Onu | 13 Vulpes | Terran | 9 | 8 | station_066_terran |
| 67 | Orcon | 13 Vulpes | Terran | 4 | 10 | station_067_terran |
| 68 | Sorox | 13 Vulpes | Terran | 5 | 17 | station_068_terran |
| 69 | Oni | 13 Vulpes | Terran | 6 | 19 | station_069_terran |
| 70 | Dis | 14 Magnetar | Terran | 7 | 13 | station_070_terran |
| 71 | Unotok | 14 Magnetar | Terran | 2 | 7 | station_071_terran |
| 72 | Zepar | 14 Magnetar | Terran | 4 | 5 | station_072_terran |
| 73 | Lon Nion | 14 Magnetar | Terran | 4 | 9 | station_073_terran |
| 74 | Kanado | 14 Magnetar | Terran | 1 | 16 | station_074_terran |
| 75 | Deuter IV | 15 Mido | Midorian | 3 | 4 | station_075_midorian |
| 76 | Yrdal Gedal | 15 Mido | Midorian | 3 | 10 | station_076_midorian |
| 77 | Heinsten | 15 Mido | Midorian | 2 | 18 | station_077_midorian |
| 78 | Var Hastra | 15 Mido | Midorian | 2 | 0 | station_078_midorian |
| 79 | Kernstal | 15 Mido | Midorian | 3 | 11 | station_079_midorian |
| 80 | Kumppa | 16 Prospero | Terran | 1 | 6 | station_080_terran |
| 81 | Teres | 16 Prospero | Terran | 4 | 19 | station_081_terran |
| 82 | Plural Z | 16 Prospero | Terran | 8 | 12 | station_082_terran |
| 83 | Marktesh | 16 Prospero | Terran | 7 | 15 | station_083_terran |
| 84 | Urda Arcturius | 16 Prospero | Terran | 7 | 2 | station_084_terran |
| 85 | Genoh | 17 Nesla | Nivelian | 10 | 13 | station_085_nivelian |
| 86 | Hamina | 17 Nesla | Nivelian | 8 | 9 | station_086_nivelian |
| 87 | Geséhn | 17 Nesla | Nivelian | 3 | 8 | station_087_nivelian |
| 88 | Aoéh | 17 Nesla | Nivelian | 2 | 7 | station_088_nivelian |
| 89 | Ahma Ésoh | 17 Nesla | Nivelian | 5 | 16 | station_089_nivelian |
| 90 | Festus | 18 Pescal Inartu | Terran | 7 | 1 | station_090_terran |
| 91 | Dima | 18 Pescal Inartu | Terran | 4 | 4 | station_091_terran |
| 92 | Fejar | 18 Pescal Inartu | Terran | 2 | 14 | station_092_terran |
| 93 | Maissa | 18 Pescal Inartu | Terran | 2 | 17 | station_093_terran |
| 94 | Sobotnik | 18 Pescal Inartu | Terran | 5 | 5 | station_094_terran |
| 95 | Gome C | 19 Augmenta | Terran | 1 | 3 | station_095_terran |
| 96 | Kalun Amir | 19 Augmenta | Terran | 8 | 8 | station_096_terran |
| 97 | Nyrand | 19 Augmenta | Terran | 6 | 14 | station_097_terran |
| 98 | Alioth | 19 Augmenta | Terran | 6 | 12 | station_098_terran |
| 99 | Damarque I | 19 Augmenta | Terran | 5 | 6 | station_099_terran |
| 100 | Kothar | 22 Beidan | Terran | 10 | 0 | v_station_deep_science_damaged (v_station_deep_science before the Valkyrie add-on is won) |
| 101 | Valkyrie | 23 Herjaza | Terran | 8 | 1 | v_station_battlestation_anim |
| 102 | Scion | 23 Herjaza | Terran | 0 | 2 |  |
| 103 | Coromesk | 24 Skavac | Terran | 0 | 3 |  |
| 104 | Nosdron | 24 Skavac | Terran | 0 | 4 |  |
| 105 | Var Destro | 25 Loma | Midorian | 10 | 20 | v_station_105_loma |
| 106 | Sao Perula | 25 Loma | Midorian | 10 | 21 | v_station_106_loma |
| 107 | Quineros | 25 Loma | Midorian | 10 | 22 | v_station_107_loma |
| 108 | Kaamo | 26 Shima | Midorian | 1 | 11 | station_kaamo_club |
| 109 | Naneroh | 27 Ginoya | Midorian | 4 | 14 | sn_station_midorian_wrecked |
| 110 | Valpatro | 27 Ginoya | Midorian | 5 | 8 | sn_station_midorian_wrecked |
| 111 | Luur | 27 Ginoya | Midorian | 5 | 12 | sn_station_midorian_wrecked (sn_burning_station_luur during the Supernova story) |
| 112 | Var Lupra | 27 Ginoya | Midorian | 3 | 24 | sn_station_112_midorian |
| 113 | Tadram | 27 Ginoya | Midorian | 6 | 9 | sn_station_113_midorian |
| 114 | Midantha | 28 Talidor | Midorian | 4 | 25 | sn_station_114_midorian |
| 115 | Tergalon | 28 Talidor | Midorian | 4 | 10 | sn_station_115_midorian |
| 116 | Psa Leri | 28 Talidor | Midorian | 5 | 18 | sn_station_116_midorian |
| 117 | Merlur | 28 Talidor | Midorian | 2 | 12 | sn_station_117_midorian |
| 118 | Sao Laros | 28 Talidor | Midorian | 6 | 11 | sn_station_118_midorian |
| 119 | Lan Manéh | 29 Paréah | Nivelian | 7 | 5 | sn_station_119_nivelian |
| 120 | Katashán | 29 Paréah | Nivelian | 6 | 26 | sn_station_120_nivelian |
| 121 | Névan | 29 Paréah | Nivelian | 7 | 9 | sn_station_121_nivelian |
| 122 | Okana | 29 Paréah | Nivelian | 9 | 7 | sn_station_122_nivelian |
| 123 | Shelén | 29 Paréah | Nivelian | 9 | 20 | sn_station_123_nivelian |
| 124 | Vu's | 30 Me'enkk | Vossk | 6 | 3 | station_vossk |
| 125 | Bor M'okk | 30 Me'enkk | Vossk | 9 | 11 | station_vossk |
| 126 | Bak S'ondorr | 30 Me'enkk | Vossk | 6 | 22 | station_vossk |
| 127 | Kan'Tarr | 30 Me'enkk | Vossk | 8 | 24 | station_vossk |
| 128 | M'Ankk | 31 Wah'norr | Vossk | 6 | 18 | station_vossk |
| 129 | Va'lerrm | 31 Wah'norr | Vossk | 9 | 14 | station_vossk |
| 130 | O'muarkk | 31 Wah'norr | Vossk | 6 | 3 | station_vossk |
| 131 | Bra'Murr | 31 Wah'norr | Vossk | 8 | 12 | station_vossk |
| 132 | Bervegor | 32 Skor Terpa | Terran | 0 | 24 |  |
| 133 | Merxde | 33 Alda | Terran | 0 | 6 |  |
| 134 | Selbah | 33 Alda | Terran | 0 | 5 |  |

## Planets

A station's `planet` number: its texture (`_big` close up, `_small` far away; a skin replaces both) and what it looks like.

| Planet | Texture | Looks like | Used by stations |
|---|---|---|---|
| 0 | planet_000_big / _small | Earth-like: green-brown land, white clouds | 0, 25, 48, 78, 100 |
| 1 | planet_001_big / _small | banded tan and brown gas giant | 30, 34, 50, 58, 90, 101 |
| 2 | planet_002_big / _small | murky olive-brown world | 14, 41, 65, 84, 102 |
| 3 | planet_003_big / _small | pale lilac cloudy world | 5, 22, 45, 63, 95, 103, 124, 130 |
| 4 | planet_004_big / _small | blue-green ocean world with clouds | 16, 27, 55, 75, 91, 104 |
| 5 | planet_005_big / _small | molten lava world, glowing orange cracks | 18, 23, 52, 72, 94, 119, 134 |
| 6 | planet_006_big / _small | green jungle / swamp world | 19, 35, 57, 60, 80, 99, 133 |
| 7 | planet_007_big / _small | rust-red desert world (Mars-like) | 12, 24, 53, 71, 88, 122 |
| 8 | planet_008_big / _small | blue ocean world with green continents | 4, 31, 54, 66, 87, 96, 110 |
| 9 | planet_009_big / _small | small gas giant with tilted rings | 10, 33, 38, 73, 86, 113, 121 |
| 10 | planet_010_big / _small | green and turquoise banded gas giant | 6, 28, 44, 67, 76, 115 |
| 11 | planet_011_big / _small | dark red rocky world | 2, 20, 37, 59, 79, 108, 118, 125 |
| 12 | planet_012_big / _small | orange and cream banded gas giant (Jupiter-like) | 13, 46, 64, 82, 98, 111, 117, 131 |
| 13 | planet_013_big / _small | dark world with glowing orange city lights / cracks | 8, 32, 42, 70, 85 |
| 14 | planet_014_big / _small | dark moon, a thin lit crescent | 7, 21, 49, 62, 92, 97, 109, 129 |
| 15 | planet_015_big / _small | grey-brown cloudy rocky world | 1, 51, 56, 83 |
| 16 | planet_016_big / _small | small tan gas giant with tilted rings | 11, 36, 40, 74, 89 |
| 17 | planet_017_big / _small | deep blue ocean world with clouds | 9, 15, 26, 47, 68, 93 |
| 18 | planet_018_big / _small | dark grey dim world | 17, 29, 39, 61, 77, 116, 128 |
| 19 | planet_019_big / _small | pale blue-grey ice / ocean world | 3, 43, 69, 81 |
| 20 | v_planet_020_big / _small | dark olive-gold rocky world | 105, 123 |
| 21 | v_planet_021_big / _small | green planet with wide golden rings | 106 |
| 22 | v_planet_022_big / _small | cratered tan-grey moon | 107, 126 |
| 23 | planet_void_big / _small | the Void's world: violet cell pattern |  |
| 24 | sn_planet_024_big / _small | scorched burning lava world (the supernova) | 112, 127, 132 |
| 25 | sn_planet_025_big / _small | blue Earth-like world with a moon beside it | 114 |
| 26 | sn_planet_026_big / _small | dark blue-green ocean world with a bright atmosphere rim | 120 |

## Story steps

The campaign index (`campaign` in conditions, `minCampaign` of lounge sellers). 0-44 the main story, 45-83 Valkyrie, 84-162
Supernova. Station = the step's target.

| Step | Title | Station | Summary |
|---|---|---|---|
| 0 | Prologue: the pirate ambush | 78 Var Hastra | 3598 A.D., the Phantom against three pirates; flight tutorial |
| 1 | Rescued by Gunant | 78 Var Hastra | Cutscene: towed into Var Hastra, the Phantom becomes Betty |
| 2 | Mine 10 t of ore | 78 Var Hastra | Mining tutorial in Var Hastra's orbit |
| 3 | Bring the ore to Var Hastra | 78 Var Hastra |  |
| 4 | Mine a full hold, then flee | 78 Var Hastra | 25 t of ore; a pirate shows up |
| 5 | Report the pirate to Gunant | 78 Var Hastra | The hangar unlocks |
| 6 | Buy a weapon and a shield | 78 Var Hastra | Shop tutorial: buy and mount both |
| 7 | Raid the pirate hideout with Gunant | 78 Var Hastra | Follow the waypoints; three pirates lie in wait |
| 8 | Back to Var Hastra | 78 Var Hastra | The time jump: it is 3624 |
| 9 | Gunant sends you to Norris | 78 Var Hastra | The map and Missions unlock |
| 10 | Meet Norris Bernard at Kernstal | 79 Kernstal |  |
| 11 | Check the silent station | 76 Yrdal Gedal | Meet Mkkt Bkkt |
| 12 | Report back to Norris | 79 Kernstal | The Space Lounge unlocks |
| 13 | Complete a freelance mission | 0 Nehébru | Then Norris calls: the convoy has arrived |
| 14 | Save the Terran convoy | 79 Kernstal | Pirates at Kernstal; ends with your arrest by the navy |
| 15 | Interrogated at Alioth | 98 Alioth | The Void alarm sounds |
| 16 | First contact: the Void raid | 98 Alioth | Defend the Terran freighters at Alioth |
| 17 | Brent's briefing | 98 Alioth | Hint: the jumpgates |
| 18 | Fly to Suttnar | 56 Suttnar | Professor Boyle has been kidnapped |
| 19 | Fly to Kappa | 55 Kappa | The EMP bomb plan |
| 20 | Get EMP bombs at Kappa | 55 Kappa | Dock and mount them (10 free in stock) |
| 21 | Stop the hijacker | 55 Kappa | Disable Boyle's kidnapper with EMP; killing him fails |
| 22 | Boyle rescued | 55 Kappa | He has the Wolf-Reiser coordinates |
| 23 | Take Boyle to Deep Science | 10 Thynome | Meet Carla at Thynome |
| 24 | Collect Void samples at Sahi | 48 Sahi | Needs a scanner and a tractor beam; ends in the wormhole |
| 25 | Stranded in the Void |  | The first wormhole ride |
| 26 | Escape the Void pursuers | 48 Sahi | Back at Sahi: survive the Void ships |
| 27 | Deliver the Void remains to Thynome | 10 Thynome | Carla asks you out |
| 28 | Enter the wormhole at Dima | 91 Dima | With Carla's probe aboard |
| 29 | Launch the probe in the Void |  | Lock the Void station, then survive 3 minutes |
| 30 | Call Carla | 91 Dima | Back in Dima's orbit |
| 31 | Brent's news at Alioth | 98 Alioth |  |
| 32 | Meet Khador at Thynome | 10 Thynome | The idea of the Khador Drive |
| 33 | Bring 50 Void Crystals | 10 Thynome | Mine them in the Void, deliver them to Thynome |
| 34 | Brent's plan at Nähma | 30 Néhma | Unlocks the Khador Drive blueprint |
| 35 | Meet Errkt Uggut in S'kolptorr | 29 Ga'kkrr |  |
| 36 | Kill contest with Errkt | 27 B'akka | Outscore him at the B'akka pirate hideout |
| 37 | Brent is furious | 27 B'akka | A convoy has been hijacked |
| 38 | Defend the Nivelian freighters | 22 Dekato | Destroy the Midorian escort without hitting the convoy |
| 39 | Final briefing at Nähma | 30 Néhma | The bomb; a scanner and tractor beam for Errkt's pod |
| 40 | Escort Errkt into the wormhole |  | Guard his freighter at the attacked station |
| 41 | Battle at the Void mother ship |  | The escort in the Void; entering the wormhole early kills you |
| 42 | Destroy the freighter and escape |  | The mother ship explodes; the ride back out |
| 43 | The date with Carla | 10 Thynome | Thynome; then the ending and the credits |
| 44 | The morning after | 10 Thynome | Carla's note |
| 45 | Free play: the main game won |  | +40 000 credits |
| 46 | Valkyrie: the call in space |  | The add-on's opening radio call |
| 47 | Meet Cornelius at Kanado | 74 Kanado |  |
| 48 | Chauffeured to B'akrram | 58 B'akrram | In the loaner H'Soc, on autopilot |
| 49 | Steal the K'Suukk | 58 B'akrram | Leave calmly in the stolen Vossk ship |
| 50 | Detected at Makke S'ik | 62 Makke S'ik | Fly the K'Suukk through the orbit |
| 51 | The Vossk freighter at S'inokk | 25 S'inokk | Keep going toward the jumpgate |
| 52 | Chased by Vossk freighters | 25 S'inokk | Flee to the jumpgate |
| 53 |  |  |  |
| 54 | Deliver the K'Suukk to Kanado | 74 Kanado | 200 000 credits; the Herjaza coordinates |
| 55 | Meet Alice on the Valkyrie | 101 Valkyrie | Your own ship is back |
| 56 | The turret test | 102 Scion | In the loaner S'Kanarr, with a pirate escort |
| 57 | Report to Alice | 101 Valkyrie | 150 000 credits; talk of the Liberator |
| 58 | Build 10 Liberators | 101 Valkyrie | From the blueprint, then bring them to Alice |
| 59 | Destroy the rival arms convoys | 101 Valkyrie | At Suttnar, Öhna and Dekato; a bonus per Liberator kill |
| 60 | Report to Alice | 101 Valkyrie | 50 000 credits plus the Liberator bonus |
| 61 | Carla calls | 101 Valkyrie | 10 s after the launch |
| 62 | Visit Carla at Kothar | 100 Kothar |  |
| 63 | Khador has been kidnapped | 103 Coromesk | Beat the pirates at Coromesk |
| 64 | Free Khador at Nosdron | 104 Nosdron |  |
| 65 | Escort Khador to Kothar | 100 Kothar |  |
| 66 | Back to Alice | 101 Valkyrie | Cornelius and the mines |
| 67 | Cover Corny's break-in | 104 Nosdron | The Skavac pirates at the Nosdron outpost |
| 68 | Bring a Void Essence to Netor | 66 Inari Onu | Drilled from Void Crystals in the Void |
| 69 | Hunt Trot Lykkt at Inari Onu | 66 Inari Onu | The Disruptor thief |
| 70 | Stop Trot Lykkt before the gate | 65 Lopat | At Lopat |
| 71 | Report to Netor | 66 Inari Onu |  |
| 72 | Alice calls | 101 Valkyrie | 150 000 credits; the Disruptor blueprint |
| 73 | EMP the weapons convoy | 81 Teres | At Teres |
| 74 | Escort the convoy to Kothar | 100 Kothar |  |
| 75 | Alice is Carla's sister | 100 Kothar |  |
| 76 | Khador's warning | 100 Kothar |  |
| 77 | Confront Alice | 101 Valkyrie | Imprisoned on the Valkyrie |
| 78 | Escape from the Valkyrie | 101 Valkyrie | Launches straight away |
| 79 | The misjump into the Void |  |  |
| 80 | Defend Kothar | 100 Kothar | Against Alice's battlestation |
| 81 | Alice stranded in the Void |  | Cutscene |
| 82 | Aftermath with Carla | 100 Kothar |  |
| 83 | Khador's gift: the Cronus | 100 Kothar |  |
| 84 | Free play: Valkyrie won |  | +1 Khador Drive |
| 85 | Supernova: Carla calls | 0 Nehébru | The add-on's opening radio call |
| 86 | Pick up Carla at Kothar | 100 Kothar |  |
| 87 | Fly Carla to Thynome | 10 Thynome | Radio chat on the way |
| 88 | A few months later... | 10 Thynome |  |
| 89 | The Ginoya supernova | 109 Naneroh | Cutscene: meanwhile in Midorian space |
| 90 | Gunant's distress call | 10 Thynome | Bring passenger cabins |
| 91 | Rescue the Valpatro miners | 110 Valpatro | Evacuate 10 survivors from the damaged freighter |
| 92 | Hand over the evacuees at Tadram | 113 Tadram | The first stealth fighter attack |
| 93 | Meet Bargand Surr | 114 Midantha |  |
| 94 | Evacuate the burning Luur station | 111 Luur | Gamma Shield I given |
| 95 | Meanwhile on Thynome... | 10 Thynome | Cutscene: Carla, Moonsprocket, Lampeter and Khador |
| 96 | See Brent at Alioth | 98 Alioth |  |
| 97 | Pirates attack the Nivelians | 85 Genoh | At Genoh |
| 98 | Meet Trunt Harval | 120 Katashán |  |
| 99 | Meanwhile on Thynome... | 10 Thynome | Cutscene: Moonsprocket and Carla |
| 100 | Stealth fighter ambush at Alioth | 98 Alioth |  |
| 101 | Report to Brent | 98 Alioth |  |
| 102 | Protect the dropships | 113 Tadram | The carrier evacuation at Tadram; a repair beam given |
| 103 | The reversal plan | 10 Thynome | At Thynome |
| 104 | Build a Gamma Shield II | 10 Thynome | From the blueprint |
| 105 | Bomb the supernova | 109 Naneroh | Fire the reverse-matter missile at Naneroh |
| 106 | Check on Luur | 111 Luur | The supernova grew; a damaged stealth fighter |
| 107 | Follow the stealth fighter |  |  |
| 108 | Back to Thynome | 10 Thynome |  |
| 109 | Meanwhile in the Midorian sector... | 114 Midantha | Cutscene: Bargand and Brent |
| 110 | Ask the barkeeper at Thynome | 10 Thynome |  |
| 111 | The drunk lady at Nepis | 38 Nepis |  |
| 112 | Bring her a Magnetar Juice | 38 Nepis |  |
| 113 | Search the lounge at Plural Z | 82 Plural Z |  |
| 114 | The loan sharks at Marktesh | 83 Marktesh |  |
| 115 | Back to Plural Z | 82 Plural Z |  |
| 116 | Search the Pescal Inartu lounges | 93 Maissa |  |
| 117 | Go to Bak S'ondorr | 126 Bak S'ondorr |  |
| 118 | Buy K'mirkk Toad Mutagen | 126 Bak S'ondorr |  |
| 119 | Meanwhile... | 10 Thynome | Cutscene: Carla and Khador |
| 120 | Specter ambush at Valadon | 40 Valadon | Carrying the volatile mutagen |
| 121 | Deliver the mutagen to Moonsprocket | 93 Maissa |  |
| 122 | Bring Moonsprocket to Thynome | 10 Thynome |  |
| 123 | Security check at Növan | 121 Névan | The Nivelian vaults |
| 124 | Dock at Növan | 121 Névan |  |
| 125 | Find the freighter's black box | 55 Kappa | Hack the secure containers at Kappa |
| 126 | Meanwhile... | 120 Katashán | Cutscene: Trunt Harval |
| 127 | The first prism to Brent | 98 Alioth |  |
| 128 | Hunt Pal Tyyrt |  | On the Most Wanted board |
| 129 |  |  |  |
| 130 | Hunt Kehnor |  | On the Most Wanted board |
| 131 | Deliver the second prism | 112 Var Lupra | Past the plasma array at Var Lupra |
| 132 | Dock at Var Lupra | 112 Var Lupra |  |
| 133 | Meanwhile... | 120 Katashán | Cutscene: Trunt Harval |
| 134 | The mining operation at Dekato | 22 Dekato |  |
| 135 | Mine titanium for the plant | 103 Coromesk | 140 t at Coromesk |
| 136 | The third prism to Var Lupra | 112 Var Lupra |  |
| 137 | The Vossk hail at B'akrram | 58 B'akrram |  |
| 138 | Meet Mekant at B'akrram | 58 B'akrram |  |
| 139 | Raid the Vossk battleships | 131 Bra'Murr | Hack the cargo bay at Bra'Murr; needs a Vossk ship and signature |
| 140 | The last prism to Var Lupra | 112 Var Lupra |  |
| 141 | Gunant at Var Hastra | 78 Var Hastra | The Chromo Plasma blueprint |
| 142 | Gather plasma with Gunant | 79 Kernstal | The plasma tutorial at Kernstal |
| 143 | Bring Chromo Plasma | 112 Var Lupra |  |
| 144 | Harval's ultimatum | 112 Var Lupra | Cutscene: the plasma array fires |
| 145 | Harval destroys the plasma array | 112 Var Lupra |  |
| 146 | Regroup | 112 Var Lupra |  |
| 147 | Hail Alice in the Void |  |  |
| 148 | Find a real-estate broker | 96 Kalun Amir |  |
| 149 | The broker dialogue at Inari Onu | 96 Kalun Amir | Dialogue only, not normally reached |
| 150 | The broker dialogue at Coppolite | 96 Kalun Amir | Dialogue only, not normally reached |
| 151 | The broker at Kalun Amir | 96 Kalun Amir |  |
| 152 | Back to Alice |  |  |
| 153 | Energy cells from Brent | 98 Alioth |  |
| 154 | Alice's betrayal |  | The deal in the Void; a hack against the clock |
| 155 | A call after the launch | 0 Nehébru |  |
| 156 | Hand Alice over | 99 Damarque I |  |
| 157 | The final battle at Var Lupra | 112 Var Lupra |  |
| 158 | Duel with Trunt Harval at Luur | 111 Luur |  |
| 159 | Celebration at Thynome | 10 Thynome |  |
| 160 | Carla finds Keith's letter | 10 Thynome | Cutscene |
| 161 | The bar at Maissa | 93 Maissa | Cutscene |
| 162 | The end |  | Free play; the Most Wanted boards unlock |

## Story speakers

Speakers with the game's portraits: a speaker in dialog / radio lines and event graphs (by name or number), what a
character's `replaces` takes. 16 Info and 17 Story are the tutorial pages and the narrator; 19 and 56 talk in the alien font.

| # | Name |
|---|---|
| 0 | Keith T. Maxwell |
| 1 | Brent Snocom |
| 2 | Gunant Breh |
| 3 | Norris Bernard |
| 4 | Mkkt Bkkt |
| 5 | Tommy |
| 6 | Carla |
| 7 | Errkt Uggut |
| 8 | Jean Baffour |
| 9 | Pirate Boss |
| 10 | Pirate |
| 11 | Pirate |
| 12 | Security |
| 13 | Security |
| 14 | Hijacker |
| 15 | Computer |
| 16 | Info |
| 17 | Story |
| 18 | Terran Officer |
| 19 | Void |
| 20 | Khador |
| 21 | Midorian |
| 22 | Nivelian |
| 23 | Agent |
| 24 | Taret Orskk |
| 25 | Mekant Orskk |
| 26 | Alice |
| 27 | Escort |
| 28 | Pirate |
| 29 | Pirate |
| 30 | Pirate |
| 31 | Cornelius Tenner |
| 32 | Netor |
| 33 | Convoy Captain |
| 34 | Trot Lykkt |
| 35 | Smuggler |
| 36 | Corny |
| 37 | Bargand Surr |
| 38 | Moonsprocket |
| 39 | Trunt Harval |
| 40 | Brillo Lampeter |
| 41 | Drunk Lady |
| 42 | Barkeeper |
| 43 | Dealer |
| 44 | Dr. Gum'Skratch |
| 45 | Pal Tyyrt |
| 46 | Kehnor |
| 47 | Hongar Meton |
| 48 | Industrialist |
| 49 | Miner |
| 50 | Vossk |
| 51 | Real Estate Broker |
| 52 | Real Estate Broker |
| 53 | Real Estate Broker |
| 54 | Real Estate Broker |
| 55 | Hans |
| 56 | Corny |
| 57 | Barkeeper |
| 58 | Barkeeper |
| 59 | Freighter Captain |
| 60 | Gunant IV |
| 61 | Deep Science Escort |
| 62 | Barkeeper |

## Models and their textures

Every assembled object of the game (the names `/spawn` and the Spawn node take for scenery, and the folder name of
one-model skins), with the textures it draws: a mod's `textures/<texture>.png` replaces that texture on every model using
it, `textures/<model>/<texture>.png` on that model only, `textures/ships/<ship id>/<texture>.png` on one new ship on that
model only (`"model": "gof2:<name>"`). Keep the original's UV layout. `_normal_specular` = normal map,
`_metallic_smoothness` = the remake's metallic map; `glow_<race>` / `ship_engine_glow` and the like are shared lights.
Any of them is also a model a mod can use without a file: `"model": "gof2:<name>"` in ships.json, stations.json and
items.json fx (see the guide's "The game's own models").

### ships

| Model | Pack | Textures |
|---|---|---|
| ship_000_midorian | main | glow_midorian, ship_000_midorian_diffuse, ship_000_midorian_metallic_smoothness, ship_000_midorian_normal_specular, ship_engine_glow |
| ship_001_terran | main | glow_terran, ship_001_terran_diffuse, ship_001_terran_metallic_smoothness, ship_001_terran_normal_specular, ship_engine_glow |
| ship_002_pirates | main | glow_pirates, ship_002_pirates_diffuse, ship_002_pirates_metallic_smoothness, ship_002_pirates_normal_specular, ship_engine_glow |
| ship_003_midorian | main | glow_midorian, ship_003_midorian_diffuse, ship_003_midorian_metallic_smoothness, ship_003_midorian_normal_specular, ship_engine_glow |
| ship_004_nivelian | main | glow_nivelian, ship_004_nivelian_diffuse, ship_004_nivelian_metallic_smoothness, ship_004_nivelian_normal_specular, ship_engine_glow |
| ship_005_terran | main | glow_terran, ship_005_terran_diffuse, ship_005_terran_metallic_smoothness, ship_005_terran_normal_specular, ship_engine_glow |
| ship_006_midorian | main | glow_midorian, ship_006_midorian_diffuse, ship_006_midorian_metallic_smoothness, ship_006_midorian_normal_specular, ship_engine_glow |
| ship_007_terran | main | glow_terran, ship_007_terran_diffuse, ship_007_terran_metallic_smoothness, ship_007_terran_normal_specular, ship_engine_glow |
| ship_008_void | main | glow_void, ship_008_void_diffuse, ship_008_void_metallic_smoothness, ship_008_void_normal_specular, ship_engine_glow |
| ship_009_vossk | main | glow_vossk, ship_009_vossk_diffuse, ship_009_vossk_metallic_smoothness, ship_009_vossk_normal_specular, ship_engine_glow |
| ship_010_terran | main | glow_terran, ship_010_terran_diffuse, ship_010_terran_metallic_smoothness, ship_010_terran_normal_specular, ship_engine_glow |
| ship_011_pirates | main | glow_pirates, ship_011_pirates_diffuse, ship_011_pirates_metallic_smoothness, ship_011_pirates_normal_specular, ship_engine_glow |
| ship_012_nivelian | main | glow_nivelian, ship_012_nivelian_diffuse, ship_012_nivelian_metallic_smoothness, ship_012_nivelian_normal_specular, ship_engine_glow |
| cargo_004_vossk | main | cargo_vossk_diffuse, cargo_vossk_metallic_smoothness, cargo_vossk_normal_specular, glow_vossk |
| battleship_terran | main | battleship_terran_diffuse, battleship_terran_metallic_smoothness, battleship_terran_normal_specular, glow_terran |
| cargo_003_terran | main | cargo_terran_diffuse, cargo_terran_metallic_smoothness, cargo_terran_normal_specular, glow_terran |
| cargo_002_nivelian | main | cargo_nivelian_diffuse, cargo_nivelian_metallic_smoothness, cargo_nivelian_normal_specular, glow_nivelian |
| cargo_001_midorian | main | cargo_midorian_diffuse, cargo_midorian_metallic_smoothness, cargo_midorian_normal_specular, glow_midorian |
| ship_016_nivelian | main | glow_nivelian, ship_016_nivelian_diffuse, ship_016_nivelian_metallic_smoothness, ship_016_nivelian_normal_specular, ship_engine_glow |
| ship_017_terran | main | glow_terran, ship_017_terran_diffuse, ship_017_terran_metallic_smoothness, ship_017_terran_normal_specular, ship_engine_glow |
| ship_018_nivelian | main | glow_nivelian, ship_018_nivelian_diffuse, ship_018_nivelian_metallic_smoothness, ship_018_nivelian_normal_specular, ship_engine_glow |
| ship_019_midorian | main | glow_midorian, ship_019_midorian_diffuse, ship_019_midorian_metallic_smoothness, ship_019_midorian_normal_specular, ship_engine_glow |
| ship_020_midorian | main | glow_midorian, ship_020_midorian_diffuse, ship_020_midorian_metallic_smoothness, ship_020_midorian_normal_specular, ship_engine_glow |
| ship_021_nivelian | main | glow_nivelian, ship_021_nivelian_diffuse, ship_021_nivelian_metallic_smoothness, ship_021_nivelian_normal_specular, ship_engine_glow |
| ship_022_terran | main | glow_terran, ship_022_terran_diffuse, ship_022_terran_metallic_smoothness, ship_022_terran_normal_specular, ship_engine_glow |
| ship_023_pirates | main | glow_pirates, ship_023_pirates_diffuse, ship_023_pirates_metallic_smoothness, ship_023_pirates_normal_specular, ship_engine_glow |
| ship_024_pirates | main | glow_pirates, ship_024_pirates_diffuse, ship_024_pirates_metallic_smoothness, ship_024_pirates_normal_specular, ship_engine_glow |
| ship_025_pirates | main | glow_pirates, ship_025_pirates_diffuse, ship_025_pirates_metallic_smoothness, ship_025_pirates_normal_specular, ship_engine_glow |
| ship_026_terran | main | glow_terran, ship_026_terran_diffuse, ship_026_terran_metallic_smoothness, ship_026_terran_normal_specular, ship_engine_glow |
| ship_027_terran | main | glow_terran, ship_027_terran_diffuse, ship_027_terran_metallic_smoothness, ship_027_terran_normal_specular, ship_engine_glow |
| ship_028_terran | main | glow_terran, ship_028_terran_diffuse, ship_028_terran_metallic_smoothness, ship_028_terran_normal_specular, ship_engine_glow |
| ship_029_pirates | main | glow_pirates, ship_029_pirates_diffuse, ship_029_pirates_metallic_smoothness, ship_029_pirates_normal_specular, ship_engine_glow |
| ship_030_midorian | main | glow_midorian, ship_030_midorian_diffuse, ship_030_midorian_metallic_smoothness, ship_030_midorian_normal_specular, ship_engine_glow |
| ship_031_nivelian | main | glow_nivelian, ship_031_nivelian_diffuse, ship_031_nivelian_metallic_smoothness, ship_031_nivelian_normal_specular, ship_engine_glow |
| ship_032_pirates | main | glow_pirates, ship_032_pirates_diffuse, ship_032_pirates_metallic_smoothness, ship_032_pirates_normal_specular, ship_engine_glow |
| ship_033_terran | main | glow_terran, ship_033_terran_diffuse, ship_033_terran_metallic_smoothness, ship_033_terran_normal_specular, ship_engine_glow |
| ship_034_terran | main | glow_terran, ship_034_terran_diffuse, ship_034_terran_metallic_smoothness, ship_034_terran_normal_specular, ship_engine_glow |
| ship_035_nivelian | main | glow_nivelian, ship_035_nivelian_diffuse, ship_035_nivelian_metallic_smoothness, ship_035_nivelian_normal_specular, ship_engine_glow |
| ship_036_terran | main | glow_terran, ship_036_terran_diffuse, ship_036_terran_metallic_smoothness, ship_036_terran_normal_specular, ship_engine_glow |
| v_ship_037_deep_science | valkyrie | v_ship_037_deep_science_diffuse, v_ship_037_deep_science_metallic_smoothness, v_ship_037_deep_science_normal_specular, v_ship_engine_glow |
| v_ship_038_deep_science | valkyrie | v_ship_038_deep_science_diffuse, v_ship_038_deep_science_metallic_smoothness, v_ship_038_deep_science_normal_specular, v_ship_engine_glow |
| v_ship_039_vossk | valkyrie | v_ship_039_vossk_diffuse, v_ship_039_vossk_metallic_smoothness, v_ship_039_vossk_normal_specular, v_ship_engine_glow |
| v_ship_040_deep_science | valkyrie | v_ship_040_deep_science_diffuse, v_ship_040_deep_science_metallic_smoothness, v_ship_040_deep_science_normal_specular, v_ship_engine_glow |
| v_ship_041_vossk | valkyrie | v_ship_041_vossk_diffuse, v_ship_041_vossk_metallic_smoothness, v_ship_041_vossk_normal_specular, v_ship_engine_glow |
| ship_042_retro | main | ship_042_retro_diffuse, ship_042_retro_metallic_smoothness, ship_042_retro_normal_specular, ship_engine_glow |
| ship_043_retro | main | ship_043_retro_diffuse, ship_043_retro_metallic_smoothness, ship_043_retro_normal_specular, ship_engine_glow |
| sn_ship_044_elite_nivelian | supernova | sn_ship_044_elite_nivelian_diffuse, sn_ship_044_elite_nivelian_metallic_smoothness, sn_ship_044_elite_nivelian_normal_specular |
| sn_ship_045_most_wanted | supernova | sn_ship_045_most_wanted_diffuse, sn_ship_045_most_wanted_metallic_smoothness, sn_ship_045_most_wanted_normal_specular |
| sn_ship_046_most_wanted | supernova | sn_ship_046_most_wanted_diffuse, sn_ship_046_most_wanted_metallic_smoothness, sn_ship_046_most_wanted_normal_specular |
| sn_ship_047_most_wanted | supernova | sn_ship_047_most_wanted_diffuse, sn_ship_047_most_wanted_metallic_smoothness, sn_ship_047_most_wanted_normal_specular |
| sn_ship_048_most_wanted | supernova | sn_ship_048_most_wanted_diffuse, sn_ship_048_most_wanted_metallic_smoothness, sn_ship_048_most_wanted_normal_specular |
| sn_ship_049_boss_nivelian | supernova | sn_ship_049_boss_nivelian_diffuse, sn_ship_049_boss_nivelian_metallic_smoothness, sn_ship_049_boss_nivelian_normal_specular |
| sn_ship_051_dropship_terran | supernova | sn_ship_051_dropship_terran_diffuse, sn_ship_051_dropship_terran_metallic_smoothness, sn_ship_051_dropship_terran_normal_specular |
| sn_ship_052_retro | supernova | sn_ship_052_retro_diffuse, sn_ship_052_retro_metallic_smoothness, sn_ship_052_retro_normal_specular |
| sn_ship_054_vossk | supernova | sn_ship_054_vossk_diffuse, sn_ship_054_vossk_metallic_smoothness, sn_ship_054_vossk_normal_specular |
| sn_ship_055_modified | supernova | ship_engine_glow, sn_ship_055_modified_diffuse, sn_ship_055_modified_metallic_smoothness, sn_ship_055_modified_normal_specular |
| sn_ship_056_modified | supernova | ship_engine_glow, sn_ship_056_modified_diffuse, sn_ship_056_modified_metallic_smoothness, sn_ship_056_modified_normal_specular |
| sn_ship_057_modified | supernova | ship_engine_glow, sn_ship_057_modified_diffuse, sn_ship_057_modified_metallic_smoothness, sn_ship_057_modified_normal_specular |
| sn_ship_058_modified | supernova | ship_engine_glow, sn_ship_058_modified_diffuse, sn_ship_058_modified_metallic_smoothness, sn_ship_058_modified_normal_specular |
| sn_ship_059_modified | supernova | ship_engine_glow, sn_ship_059_modified_diffuse, sn_ship_059_modified_metallic_smoothness, sn_ship_059_modified_normal_specular |
| sn_ship_060_modified | supernova | glow_pirates, ship_engine_glow, sn_ship_060_modified_diffuse, sn_ship_060_modified_metallic_smoothness, sn_ship_060_modified_normal_specular |
| ship_061_elite_nivelian_prototype | main | ship_061_elite_nivelian_prototype_diffuse, ship_061_elite_nivelian_prototype_metallic_smoothness, ship_061_elite_nivelian_prototype_normal_specular |
| ship_062_prototype | main | ship_062_prototype_diffuse, ship_062_prototype_metallic_smoothness, ship_062_prototype_normal_specular |
| ship_063_vossk_prototype | main | ship_063_vossk_diffuse, ship_063_vossk_metallic_smoothness, ship_063_vossk_normal_specular |

### stations

| Model | Pack | Textures |
|---|---|---|
| station_000_nivelian | main | glow_nivelian, stations_nivelian_diffuse, stations_nivelian_metallic_smoothness, stations_nivelian_normal_specular |
| station_001_nivelian | main | glow_nivelian, stations_nivelian_diffuse, stations_nivelian_metallic_smoothness, stations_nivelian_normal_specular |
| station_002_nivelian | main | glow_nivelian, stations_nivelian_diffuse, stations_nivelian_metallic_smoothness, stations_nivelian_normal_specular |
| station_003_nivelian | main | glow_nivelian, stations_nivelian_diffuse, stations_nivelian_metallic_smoothness, stations_nivelian_normal_specular |
| station_004_nivelian | main | glow_nivelian, stations_nivelian_diffuse, stations_nivelian_metallic_smoothness, stations_nivelian_normal_specular |
| station_005_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_006_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_007_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_008_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_009_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_010_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_020_midorian | main | glow_midorian, stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular |
| station_021_midorian | main | glow_midorian, stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular |
| station_022_midorian | main | glow_midorian, stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular |
| station_023_midorian | main | glow_midorian, stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular |
| station_024_midorian | main | glow_midorian, stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular |
| station_030_nivelian | main | glow_nivelian, stations_nivelian_diffuse, stations_nivelian_metallic_smoothness, stations_nivelian_normal_specular |
| station_031_nivelian | main | glow_nivelian, stations_nivelian_diffuse, stations_nivelian_metallic_smoothness, stations_nivelian_normal_specular |
| station_032_nivelian | main | glow_nivelian, stations_nivelian_diffuse, stations_nivelian_metallic_smoothness, stations_nivelian_normal_specular |
| station_033_nivelian | main | glow_nivelian, stations_nivelian_diffuse, stations_nivelian_metallic_smoothness, stations_nivelian_normal_specular |
| station_035_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_036_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_037_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_038_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_039_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_040_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_041_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_042_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_043_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_044_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_045_nivelian | main | glow_nivelian, stations_nivelian_diffuse, stations_nivelian_metallic_smoothness, stations_nivelian_normal_specular |
| station_046_nivelian | main | glow_nivelian, stations_nivelian_diffuse, stations_nivelian_metallic_smoothness, stations_nivelian_normal_specular |
| station_047_nivelian | main | glow_nivelian, stations_nivelian_diffuse, stations_nivelian_metallic_smoothness, stations_nivelian_normal_specular |
| station_048_nivelian | main | glow_nivelian, stations_nivelian_diffuse, stations_nivelian_metallic_smoothness, stations_nivelian_normal_specular |
| station_049_nivelian | main | glow_nivelian, stations_nivelian_diffuse, stations_nivelian_metallic_smoothness, stations_nivelian_normal_specular |
| station_055_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_056_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_057_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_065_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_066_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_067_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_068_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_069_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_070_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_071_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_072_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_073_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_074_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_075_midorian | main | glow_midorian, stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular |
| station_076_midorian | main | glow_midorian, stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular |
| station_077_midorian | main | glow_midorian, stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular |
| station_078_midorian | main | glow_midorian, stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular |
| station_079_midorian | main | glow_midorian, stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular |
| station_080_terran | main | stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_081_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_082_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_083_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_084_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_085_nivelian | main | glow_nivelian, stations_nivelian_diffuse, stations_nivelian_metallic_smoothness, stations_nivelian_normal_specular |
| station_086_nivelian | main | glow_nivelian, stations_nivelian_diffuse, stations_nivelian_metallic_smoothness, stations_nivelian_normal_specular |
| station_087_nivelian | main | glow_nivelian, stations_nivelian_diffuse, stations_nivelian_metallic_smoothness, stations_nivelian_normal_specular |
| station_088_nivelian | main | glow_nivelian, stations_nivelian_diffuse, stations_nivelian_metallic_smoothness, stations_nivelian_normal_specular |
| station_089_nivelian | main | glow_nivelian, stations_nivelian_diffuse, stations_nivelian_metallic_smoothness, stations_nivelian_normal_specular |
| station_090_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_091_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_092_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_093_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_094_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_095_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_096_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_097_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_098_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| station_099_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| v_station_105_loma | valkyrie | glow_midorian, stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular |
| v_station_106_loma | valkyrie | glow_midorian, stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular |
| v_station_107_loma | valkyrie | glow_midorian, stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular |
| sn_station_112_midorian | supernova | stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular |
| sn_station_113_midorian | supernova | glow_midorian, stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular |
| sn_station_114_midorian | supernova | glow_midorian, stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular |
| sn_station_115_midorian | supernova | glow_midorian, stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular |
| sn_station_116_midorian | supernova | glow_midorian, stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular |
| sn_station_117_midorian | supernova | glow_midorian, stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular |
| sn_station_118_midorian | supernova | glow_midorian, stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular |
| sn_station_119_nivelian | supernova | glow_nivelian, stations_nivelian_diffuse, stations_nivelian_metallic_smoothness, stations_nivelian_normal_specular |
| sn_station_120_nivelian | supernova | glow_nivelian, stations_nivelian_diffuse, stations_nivelian_metallic_smoothness, stations_nivelian_normal_specular |
| sn_station_121_nivelian | supernova | glow_nivelian, stations_nivelian_diffuse, stations_nivelian_metallic_smoothness, stations_nivelian_normal_specular |
| sn_station_122_nivelian | supernova | glow_nivelian, stations_nivelian_diffuse, stations_nivelian_metallic_smoothness, stations_nivelian_normal_specular |
| sn_station_123_nivelian | supernova | glow_nivelian, stations_nivelian_diffuse, stations_nivelian_metallic_smoothness, stations_nivelian_normal_specular |
| station_kaamo_club | main | stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular, tex_player_station_fx |
| station_vossk | main | stations_vossk_diffuse, stations_vossk_metallic_smoothness, stations_vossk_normal_specular |
| station_void | main | stations_void_diffuse, stations_void_glow, stations_void_metallic_smoothness, stations_void_normal_specular |
| v_station_battlestation_anim | valkyrie | sn_plasma_gun_valkyrie_diffuse, sn_plasma_gun_valkyrie_metallic_smoothness, sn_plasma_gun_valkyrie_normal_specular, v_station_battlestation_diffuse, v_station_battlestation_metallic_smoothness, v_station_battlestation_normal_specular |
| v_station_battlestation_anim_mission_object | valkyrie | v_station_battlestation_diffuse, v_station_battlestation_metallic_smoothness, v_station_battlestation_normal_specular |
| v_station_deep_science | valkyrie | v_station_deep_science_diffuse, v_station_deep_science_emitters, v_station_deep_science_glow, v_station_deep_science_metallic_smoothness, v_station_deep_science_normal_specular |
| v_station_deep_science_damaged | valkyrie | v_station_deep_science_diffuse, v_station_deep_science_emitters, v_station_deep_science_glow, v_station_deep_science_metallic_smoothness, v_station_deep_science_normal_specular |
| v_station_deep_science_explosion_anim | valkyrie | v_station_deep_science_diffuse, v_station_deep_science_emitters, v_station_deep_science_glow, v_station_deep_science_metallic_smoothness, v_station_deep_science_normal_specular |
| sn_burning_station_luur | main | glow_midorian, stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular |
| station_111_luur_mission_94 | main | stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular |
| sn_station_midorian_wrecked | supernova | sn_stations_midorian_wrecked_diffuse, sn_stations_midorian_wrecked_metallic_smoothness, sn_stations_midorian_wrecked_normal_specular |
| sn_burning_station_mission_object | main | glow_midorian, sn_burning_station_fire, stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular |
| station_111_luur_intact_mission_89 | main | glow_midorian, stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular |
| sn_burning_station_fire_intro | main | sn_burning_station_fire, stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular |
| station_pirates | main | glow_pirates, stations_pirates_diffuse, stations_pirates_metallic_smoothness, stations_pirates_normal_specular |
| sn_station_mining_plant | supernova | sn_station_mining_plant_part1_diffuse, sn_station_mining_plant_part1_metallic_smoothness, sn_station_mining_plant_part1_normal_specular, sn_station_mining_plant_part2_diffuse, sn_station_mining_plant_part2_metallic_smoothness, sn_station_mining_plant_part2_normal_specular |
| sn_station_113_midorian_wreck_proxy | main |  |
| v_station_battlestation_shield | valkyrie | v_station_battlestation_diffuse, v_station_battlestation_metallic_smoothness, v_station_battlestation_normal_specular |
| v_station_battlestation_laser_anim_add | valkyrie | v_station_battlestation_diffuse |
| v_station_deep_science_explosion_laser_anim_add | valkyrie | v_station_deep_science_glow |
| sn_burning_valkyrie_stage_1 | supernova | sn_burning_station_fire |
| sn_burning_valkyrie_stage_2 | supernova | sn_burning_station_fire |
| v_station_deep_science_explosion_emitters_anim_add | valkyrie | v_station_deep_science_emitters |

### jumpgates

| Model | Pack | Textures |
|---|---|---|
| jumpgate_terran | main | glow_terran, stations_terran_diffuse, stations_terran_metallic_smoothness, stations_terran_normal_specular |
| jumpgate_vossk_anim | main | stations_vossk_diffuse, stations_vossk_metallic_smoothness, stations_vossk_normal_specular |
| jumpgate_nivelian | main | glow_nivelian, stations_nivelian_diffuse, stations_nivelian_metallic_smoothness, stations_nivelian_normal_specular |
| jumpgate_midorian | main | glow_midorian, stations_midorian_diffuse, stations_midorian_metallic_smoothness, stations_midorian_normal_specular |

### asteroids

| Model | Pack | Textures |
|---|---|---|
| asteroid_01 | main | asteroid_01_diffuse, asteroid_01_metallic_smoothness, asteroid_01_normal_specular |
| asteroid_void | main | asteroid_void_diffuse, asteroid_void_metallic_smoothness, asteroid_void_normal_specular |
| v_asteroid_ice | valkyrie | v_asteroid_ice_diffuse, v_asteroid_ice_metallic_smoothness, v_asteroid_ice_normal_specular |
| sn_asteroid_magma | supernova | sn_asteroid_magma_diffuse, sn_asteroid_magma_metallic_smoothness, sn_asteroid_magma_normal_specular |

### misc

| Model | Pack | Textures |
|---|---|---|
| sn_plasma_array_midorian_stage_001 | supernova | sn_plasma_array_midorian_diffuse, sn_plasma_array_midorian_metallic_smoothness, sn_plasma_array_midorian_normal_specular |
| sn_plasma_array_midorian_stage_002 | supernova | sn_plasma_array_midorian_diffuse, sn_plasma_array_midorian_metallic_smoothness, sn_plasma_array_midorian_normal_specular |
| sn_plasma_array_midorian_stage_003 | supernova | sn_plasma_array_midorian_diffuse, sn_plasma_array_midorian_metallic_smoothness, sn_plasma_array_midorian_normal_specular |
| sn_plasma_array_midorian_stage_004 | supernova | sn_plasma_array_midorian_diffuse, sn_plasma_array_midorian_metallic_smoothness, sn_plasma_array_midorian_normal_specular |
| sn_plasma_array_midorian_stage_005 | supernova | sn_plasma_array_midorian_diffuse, sn_plasma_array_midorian_metallic_smoothness, sn_plasma_array_midorian_normal_specular |
| sn_cargo_001_midorian_wrecked | supernova | glow_midorian, sn_cargo_midorian_wrecked_diffuse, sn_cargo_midorian_wrecked_metallic_smoothness, sn_cargo_midorian_wrecked_normal_specular |
| missing_0x4961 | supernova | cargo_nivelian_diffuse, cargo_nivelian_metallic_smoothness, cargo_nivelian_normal_specular, glow_nivelian |
| sn_carrier_terran_1 | supernova | sn_carrier_terran_1_diffuse, sn_carrier_terran_1_metallic_smoothness, sn_carrier_terran_1_normal_specular, sn_carrier_terran_2_diffuse, sn_carrier_terran_2_metallic_smoothness, sn_carrier_terran_2_normal_specular, sn_carrier_terran_3_diffuse, sn_carrier_terran_3_metallic_smoothness, sn_carrier_terran_3_normal_specular |
| sn_battleship_vossk | supernova | sn_battleship_vossk_diffuse, sn_battleship_vossk_metallic_smoothness, sn_battleship_vossk_normal_specular |
| sn_station_113_midorian_col_box_proxy | main |  |
| rocket_explosive | main | bomb_rocket_fx, rocket_explosive_diffuse, rocket_explosive_metallic_smoothness, rocket_explosive_normal_specular |
| rocket_emp | main | bomb_rocket_fx, rocket_emp_diffuse, rocket_emp_metallic_smoothness, rocket_emp_normal_specular |
| bomb_explosive_a | main | bomb_explosive_a_diffuse, bomb_explosive_a_metallic_smoothness, bomb_explosive_a_normal_specular, bomb_rocket_fx |
| bomb_explosive_b | main | bomb_explosive_b_diffuse, bomb_explosive_b_metallic_smoothness, bomb_explosive_b_normal_specular, bomb_rocket_fx |
| bomb_emp_a | main | bomb_emp_a_diffuse, bomb_emp_a_metallic_smoothness, bomb_emp_a_normal_specular, bomb_rocket_fx |
| v_guided_missile_anim | valkyrie | v_guided_missile_diffuse, v_guided_missile_metallic_smoothness, v_guided_missile_normal_specular |
| v_mine_001 | valkyrie | v_mine_001_diffuse, v_mine_001_metallic_smoothness, v_mine_001_normal_specular |
| v_mine_003 | valkyrie | v_mine_003_diffuse, v_mine_003_metallic_smoothness, v_mine_003_normal_specular |
| v_mine_002 | valkyrie | v_mine_002_diffuse, v_mine_002_metallic_smoothness, v_mine_002_normal_specular |
| sn_gas_cloud_green_anim_lookat_add | supernova | sn_gas_plasma |
| sn_gas_cloud_blue_anim_lookat_add | supernova | sn_gas_plasma |
| sn_gas_cloud_violet_anim_lookat_add | supernova | sn_gas_plasma |
| sn_gas_cloud_red_anim_lookat_add | supernova | sn_gas_plasma |
| container_003_terran | main | container_003_terran_diffuse, container_003_terran_metallic_smoothness, container_003_terran_normal_specular |
| sn_secure_container_nivelian | supernova | cargo_nivelian_diffuse, cargo_nivelian_metallic_smoothness, cargo_nivelian_normal_specular, glow_nivelian |
| sn_junk_field | supernova | sn_junk_field_diffuse, sn_junk_field_metallic_smoothness, sn_junk_field_normal_specular |
| sn_plasma_array_midorian_explosion_anim | supernova | sn_plasma_array_midorian_diffuse, sn_plasma_array_midorian_metallic_smoothness, sn_plasma_array_midorian_normal_specular |
| sn_plasma_gun_valkyrie | supernova | sn_plasma_gun_valkyrie_diffuse, sn_plasma_gun_valkyrie_metallic_smoothness, sn_plasma_gun_valkyrie_normal_specular |

### turrets

| Model | Pack | Textures |
|---|---|---|
| turret_002_static | main | turret_002_diffuse, turret_002_metallic_smoothness, turret_002_normal_specular |
| turret_003_static | main | turret_003_diffuse, turret_003_metallic_smoothness, turret_003_normal_specular |
| v_station_battlestation_turret | valkyrie | v_station_battlestation_diffuse, v_station_battlestation_metallic_smoothness, v_station_battlestation_normal_specular |
| sn_sentry_gun_001 | supernova | sn_sentry_gun_001_diffuse, sn_sentry_gun_001_metallic_smoothness, sn_sentry_gun_001_normal_specular |
| sn_sentry_gun_002 | supernova | sn_sentry_gun_002_diffuse, sn_sentry_gun_002_metallic_smoothness, sn_sentry_gun_002_normal_specular |
| sn_sentry_gun_003 | supernova | sn_sentry_gun_003_diffuse, sn_sentry_gun_003_metallic_smoothness, sn_sentry_gun_003_normal_specular |
| turret_001_ship_mounted | main | turret_001_diffuse, turret_001_metallic_smoothness, turret_001_normal_specular |
| turret_002_ship_mounted | main | turret_002_diffuse, turret_002_metallic_smoothness, turret_002_normal_specular |
| turret_003_ship_mounted | main | turret_003_diffuse, turret_003_metallic_smoothness, turret_003_normal_specular |
| v_autoturret_001_anim_ship_mounted | valkyrie | v_autoturret_001_diffuse, v_autoturret_001_metallic_smoothness, v_autoturret_001_normal_specular |
| v_autoturret_002_ship_mounted | valkyrie | v_autoturret_002_diffuse, v_autoturret_002_metallic_smoothness, v_autoturret_002_normal_specular |
| v_autoturret_003_ship_mounted | valkyrie | v_autoturret_003_diffuse, v_autoturret_003_metallic_smoothness, v_autoturret_003_normal_specular |
| sn_plasma_collector_001_ship_mounted | supernova | sn_plasma_collector_001_diffuse, sn_plasma_collector_001_metallic_smoothness, sn_plasma_collector_001_normal_specular, sn_plasma_stream |
| sn_plasma_collector_002_ship_mounted | supernova | sn_plasma_collector_002_diffuse, sn_plasma_collector_002_metallic_smoothness, sn_plasma_collector_002_normal_specular, sn_plasma_stream |
| sn_plasma_collector_003_ship_mounted | supernova | sn_plasma_collector_003_diffuse, sn_plasma_collector_003_metallic_smoothness, sn_plasma_collector_003_normal_specular, sn_plasma_stream |
| sn_turret_004_ship_mounted | supernova | sn_turret_004_diffuse, sn_turret_004_metallic_smoothness, sn_turret_004_normal_specular |

### fx

| Model | Pack | Textures |
|---|---|---|
| explosion_anim_lookat_alpha | main | explosion |
| sn_shock_blast_glow_anim_lookat_add | supernova | sn_shock_blast_glow, sn_shock_blast_sphere |
| asteroid_01_explosion_anim | main | asteroid_01_diffuse, asteroid_01_metallic_smoothness, asteroid_01_normal_specular, asteroid_explosion |
| asteroid_void_explosion_anim | main | asteroid_explosion, asteroid_void_diffuse, asteroid_void_metallic_smoothness, asteroid_void_normal_specular |
| v_asteroid_ice_explosion_anim | valkyrie | v_asteroid_ice_diffuse, v_asteroid_ice_metallic_smoothness, v_asteroid_ice_normal_specular |
| sn_asteroid_magma_explosion_anim | supernova | sn_asteroid_magma_diffuse, sn_asteroid_magma_explosion, sn_asteroid_magma_metallic_smoothness, sn_asteroid_magma_normal_specular |
| explosion_void_station | main | explosion_void_station |
| sn_plasma_gun_fx_valkyrie_beam_anim_add | supernova | sn_plasma_gun_fx_valkyrie |
| projectile_009_anim_add | main | projectiles |
| explosion_debris | main | explosion |

### hangars

| Model | Pack | Textures |
|---|---|---|
| hangar_terran | main | hangar_terran_alpha_diffuse, hangar_terran_diffuse, hangar_terran_metallic_smoothness, hangar_terran_normal_specular |
| hangar_vossk | main | hangar_vossk_anim_add_diffuse, hangar_vossk_diffuse, hangar_vossk_metallic_smoothness, hangar_vossk_normal_specular |
| hangar_nivelian | main | hangar_nivelian_diffuse, hangar_nivelian_metallic_smoothness, hangar_nivelian_normal_specular |
| hangar_midorian | main | hangar_midorian_diffuse, hangar_midorian_metallic_smoothness, hangar_midorian_normal_specular |
| hangar_deep_science | valkyrie | v_hangar_deep_science_diffuse, v_hangar_deep_science_metallic_smoothness, v_hangar_deep_science_normal_specular |
| hangar_battlestation | valkyrie | v_hangar_battlestation_diffuse, v_hangar_battlestation_metallic_smoothness, v_hangar_battlestation_normal_specular |
| hangar_nivelian_bot_a | main | hangar_nivelian_diffuse, hangar_nivelian_metallic_smoothness, hangar_nivelian_normal_specular |
| hangar_nivelian_bot_b | main | hangar_nivelian_diffuse, hangar_nivelian_metallic_smoothness, hangar_nivelian_normal_specular |
| hangar_nivelian_bot_c | main | hangar_nivelian_diffuse, hangar_nivelian_metallic_smoothness, hangar_nivelian_normal_specular |
| hangar_terran_bot_a | main | hangar_terran_diffuse, hangar_terran_metallic_smoothness, hangar_terran_normal_specular |
| hangar_terran_bot_b | main | hangar_terran_diffuse, hangar_terran_metallic_smoothness, hangar_terran_normal_specular |
| hangar_terran_bot_c | main | hangar_terran_diffuse, hangar_terran_metallic_smoothness, hangar_terran_normal_specular |
| hangar_terran_bot_d | main | hangar_terran_diffuse, hangar_terran_metallic_smoothness, hangar_terran_normal_specular |
| hangar_terran_bot_e | main | hangar_terran_diffuse, hangar_terran_metallic_smoothness, hangar_terran_normal_specular |

### hangar_turret

| Model | Pack | Textures |
|---|---|---|
| hangar_turret_item_47 | main | turret_001_diffuse, turret_001_metallic_smoothness, turret_001_normal_specular |
| hangar_turret_item_48 | main | turret_002_diffuse, turret_002_metallic_smoothness, turret_002_normal_specular |
| hangar_turret_item_49 | main | turret_003_diffuse, turret_003_metallic_smoothness, turret_003_normal_specular |
| hangar_turret_item_180 | valkyrie | v_autoturret_001_diffuse, v_autoturret_001_metallic_smoothness, v_autoturret_001_normal_specular |
| hangar_turret_item_181 | valkyrie | v_autoturret_002_diffuse, v_autoturret_002_metallic_smoothness, v_autoturret_002_normal_specular |
| hangar_turret_item_182 | valkyrie | v_autoturret_003_diffuse, v_autoturret_003_metallic_smoothness, v_autoturret_003_normal_specular |
| hangar_turret_item_198 | supernova | sn_plasma_collector_001_diffuse, sn_plasma_collector_001_metallic_smoothness, sn_plasma_collector_001_normal_specular |
| hangar_turret_item_199 | supernova | sn_plasma_collector_002_diffuse, sn_plasma_collector_002_metallic_smoothness, sn_plasma_collector_002_normal_specular |
| hangar_turret_item_200 | supernova | sn_plasma_collector_003_diffuse, sn_plasma_collector_003_metallic_smoothness, sn_plasma_collector_003_normal_specular |
| hangar_turret_item_224 | supernova | sn_turret_004_diffuse, sn_turret_004_metallic_smoothness, sn_turret_004_normal_specular |

### bars

| Model | Pack | Textures |
|---|---|---|
| bar_terran | main | bar_terran_diffuse, bar_terran_metallic_smoothness, bar_terran_normal_specular |
| bar_vossk | main | bar_vossk_diffuse, bar_vossk_metallic_smoothness, bar_vossk_normal_specular |
| bar_nivelian | main | bar_nivelian_diffuse, bar_nivelian_metallic_smoothness, bar_nivelian_normal_specular |
| bar_midorian | main | bar_midorian_diffuse, bar_midorian_metallic_smoothness, bar_midorian_normal_specular |
| bar_visitor | main | bar_visitor_glow_midorian, bar_visitor_shadow, visitor_terran_m |

### galaxymap

| Model | Pack | Textures |
|---|---|---|
| galaxymap_background | main | galaxymap_bg, galaxymap_fog_layer_0, galaxymap_fog_layer_1 |
| galaxymap_sun | main | sun_006 |
| galaxymap_wormhole | main | wormhole |
| galaxymap_planet_000 | main | galaxymap_planets_diffuse |
| galaxymap_planet_001 | main | galaxymap_planets_diffuse |
| galaxymap_planet_002 | main | galaxymap_planets_diffuse |
| galaxymap_planet_003 | main | galaxymap_planets_diffuse |
| galaxymap_planet_004 | main | galaxymap_planets_diffuse |
| galaxymap_planet_005 | main | galaxymap_planets_diffuse |
| galaxymap_planet_006 | main | galaxymap_planets_diffuse |
| galaxymap_planet_007 | main | galaxymap_planets_diffuse |
| galaxymap_planet_008 | main | galaxymap_planets_diffuse |
| galaxymap_planet_009 | main | galaxymap_planets_diffuse |
| galaxymap_planet_010 | main | galaxymap_planets_diffuse |
| galaxymap_planet_011 | main | galaxymap_planets_diffuse |
| galaxymap_planet_012 | main | galaxymap_planets_diffuse |
| galaxymap_planet_013 | main | galaxymap_planets_diffuse |
| galaxymap_planet_014 | main | galaxymap_planets_diffuse |
| galaxymap_planet_015 | main | galaxymap_planets_diffuse |
| galaxymap_planet_016 | main | galaxymap_planets_diffuse |
| galaxymap_planet_017 | main | galaxymap_planets_diffuse |
| galaxymap_planet_018 | main | galaxymap_planets_diffuse |
| galaxymap_planet_019 | main | galaxymap_planets_diffuse |
| galaxymap_planet_020 | valkyrie | v_galaxymap_planets |
| galaxymap_planet_021 | valkyrie | galaxymap_planets_diffuse, v_galaxymap_planets |
| galaxymap_planet_022 | valkyrie | v_galaxymap_planets |
| galaxymap_planet_024 | supernova | sn_galaxymap_planets |
| galaxymap_planet_025 | supernova | sn_galaxymap_planets |
| galaxymap_planet_026 | supernova | sn_galaxymap_planets |
| galaxymap_orbit | main | galaxymap_orbit |
| galaxymap_system_sun | main | sun_006 |

### shop_preview

| Model | Pack | Textures |
|---|---|---|
| shop_preview_ship_04 | main | glow_nivelian, ship_004_nivelian_diffuse, ship_004_nivelian_metallic_smoothness, ship_004_nivelian_normal_specular |
| shop_preview_ship_08 | main | glow_void, ship_008_void_diffuse, ship_008_void_metallic_smoothness, ship_008_void_normal_specular |
| shop_preview_ship_09 | main | ship_009_vossk_diffuse, ship_009_vossk_metallic_smoothness, ship_009_vossk_normal_specular |
| shop_preview_ship_12 | main | glow_nivelian, ship_012_nivelian_diffuse, ship_012_nivelian_metallic_smoothness, ship_012_nivelian_normal_specular |
| shop_preview_ship_16 | main | glow_nivelian, ship_016_nivelian_diffuse, ship_016_nivelian_metallic_smoothness, ship_016_nivelian_normal_specular |
| shop_preview_ship_18 | main | glow_nivelian, ship_018_nivelian_diffuse, ship_018_nivelian_metallic_smoothness, ship_018_nivelian_normal_specular |
| shop_preview_ship_21 | main | glow_nivelian, ship_021_nivelian_diffuse, ship_021_nivelian_metallic_smoothness, ship_021_nivelian_normal_specular |
| shop_preview_ship_31 | main | glow_nivelian, ship_031_nivelian_diffuse, ship_031_nivelian_metallic_smoothness, ship_031_nivelian_normal_specular |
| shop_preview_ship_35 | main | glow_nivelian, ship_035_nivelian_diffuse, ship_035_nivelian_metallic_smoothness, ship_035_nivelian_normal_specular |
| shop_preview_ship_39 | valkyrie | v_ship_039_vossk_diffuse, v_ship_039_vossk_metallic_smoothness, v_ship_039_vossk_normal_specular |
| shop_preview_ship_41 | valkyrie | v_ship_041_vossk_diffuse, v_ship_041_vossk_metallic_smoothness, v_ship_041_vossk_normal_specular |
| shop_preview_ship_42 | main | ship_042_retro_diffuse, ship_042_retro_metallic_smoothness, ship_042_retro_normal_specular |
| shop_preview_ship_43 | main | ship_043_retro_diffuse, ship_043_retro_metallic_smoothness, ship_043_retro_normal_specular |
| shop_preview_ship_49 | supernova | sn_ship_049_boss_nivelian_diffuse, sn_ship_049_boss_nivelian_metallic_smoothness, sn_ship_049_boss_nivelian_normal_specular |
| shop_preview_ship_54 | supernova | sn_ship_054_vossk_diffuse, sn_ship_054_vossk_metallic_smoothness, sn_ship_054_vossk_normal_specular |
| shop_preview_ship_55 | supernova | sn_ship_055_modified_diffuse, sn_ship_055_modified_metallic_smoothness, sn_ship_055_modified_normal_specular |
| shop_preview_ship_56 | supernova | sn_ship_056_modified_diffuse, sn_ship_056_modified_metallic_smoothness, sn_ship_056_modified_normal_specular |
| shop_preview_ship_57 | supernova | sn_ship_057_modified_diffuse, sn_ship_057_modified_metallic_smoothness, sn_ship_057_modified_normal_specular |
| shop_preview_ship_58 | supernova | sn_ship_058_modified_diffuse, sn_ship_058_modified_metallic_smoothness, sn_ship_058_modified_normal_specular |
| shop_preview_ship_59 | supernova | sn_ship_059_modified_diffuse, sn_ship_059_modified_metallic_smoothness, sn_ship_059_modified_normal_specular |
| shop_preview_ship_60 | supernova | glow_pirates, sn_ship_060_modified_diffuse, sn_ship_060_modified_metallic_smoothness, sn_ship_060_modified_normal_specular |
| shop_preview_ship_63 | main | ship_063_vossk_diffuse, ship_063_vossk_metallic_smoothness, ship_063_vossk_normal_specular |

## Backdrop textures

Planets, suns and rings in the sky (replace with `textures/<name>.png`, one whole disc, transparent edge). A station's
`planet` N uses `planet_0NN_big` / `_small` (24-26 the Supernova's `sn_planet_0NN_*`).

planet_000_big, planet_000_small, planet_001_big, planet_001_small, planet_002_big, planet_002_small, planet_003_big, planet_003_small, planet_004_big, planet_004_small, planet_005_big, planet_005_small, planet_006_big, planet_006_small, planet_007_big, planet_007_small, planet_008_big, planet_008_small, planet_009_big, planet_009_small, planet_010_big, planet_010_small, planet_011_big, planet_011_small, planet_012_big, planet_012_small, planet_013_big, planet_013_small, planet_014_big, planet_014_small, planet_015_big, planet_015_small, planet_016_big, planet_016_small, planet_017_big, planet_017_small, planet_018_big, planet_018_small, planet_019_big, planet_019_small, planet_void_big, planet_void_small, sn_planet_024_big, sn_planet_024_small, sn_planet_025_big, sn_planet_025_small, sn_planet_026_big, sn_planet_026_small, sn_planet_ring, sn_sun_011, sn_sun_012, sn_sun_explosion_core, sn_sun_explosion_ring, sun_000, sun_001, sun_002, sun_003, sun_004, sun_005, sun_006, sun_007, sun_008, sun_009, sun_010, v_planet_020_big, v_planet_020_small, v_planet_021_big, v_planet_021_small, v_planet_022_big, v_planet_022_small

## Music

The game's tracks: `music/<name>.ogg` with one of these names replaces it everywhere (any other name is a new track for
`spaceMusic`, `stationMusic`, `music` and `/music`). Space_<race> plays in flight, Station_<race> docked, Space_Battle_* in
combat, Space_NoCombat_Void in the main menu.

- MUSIC: Errkt_CutSeq_01, HomeBase_NoCombat, HomeBase_Station, IntroAtmo_02, Music_Space_Battle_Full_Loop, OutroSong_02b, Space_Battle_Full, Space_Battle_Low, Space_Battle_Medium, Space_Battle_Void, Space_Midorianer, Space_Nivelianer, Space_NoCombat_Valkyrie, Space_NoCombat_Void, Space_Terraner, Space_Vossk, Station_Midorianer, Station_Nivelianer, Station_Terraner, Station_Valkyrie, Station_Vossk, TimeShift_Start
- DLC_MUSIC: DeepScienceAttacked_02, ValkyrieBattlemode_CutSeq_01
- DLC2_MUSIC: 20120527_GOF2_Addon_GammarRaySystems, 20120527_GOF2_Addon_StealthFighter1, 20120527_GOF2_Addon_StealthFighter2, 20120527_GOF2_Addon_WantedBoardCriminal, DeepScience_Station_02, GOF2_DeepScience_Space, GOF2_Mission_102_CutScene, GOF2_Mission_102_CutScene_Loop, GOF2_Mission_102_CutScene_Loop_2, GOF2_SN_CS_1_02, GOF2SN_CS_126_Trunt1_02

## Sound effects

`sounds/<name>.ogg` (or .wav / .mp3) with one of these names replaces that sound everywhere. Voices are in `voices.md`.

- SFX_GENERAL: Button_Push_v06, Button_Release_V06, Message_Inc_V01, Message_Info_Screen_v04
- SFX_SPACE: Autopilot_Activate_01, Autopilot_Deactivate_01, Beam_M6_Coug_v1, Beam_M6_Coug_v2, Beam_M6_Coug_v3, Beam_M6_Wolv_v1, Beam_M6_Wolv_v2, Beam_M6_Wolv_v3, Beam_M6_Wolv_v4, Beam_M6_Wulf, Beam_M6_Wulf_v2, Blaster_EMP_Dia_V01, Blaster_EMP_Luna_V01, Blaster_EMP_Sol_V01, Blaster_Gram_01, Blaster_Hnookk_01, Blaster_Kbooskk_01, Blaster_Nsaan_01, Blaster_Ridil_01, Blaster_Shgaal_01, Blaster_Tyrfing, Booster_Cyclotron_v01, Booster_Linear_v01, Booster_Meal_01, Booster_Synchrotron_1, Button_Sell_v02, Canon_Massdriver_02, Canon_Microgun_128MJ_02b, Canon_Microgun_64MJ_02, Canon_Microgun_MK1_02, Canon_Microgun_MK2_02b, Canon_Scram_02, Cloak_02, Cutscenes_Explosion_01, Destruction_Asteroid, Destruction_Ship_Big_4, Destruction_Ship_Big_4b, Destruction_Ship_Big_5, Destruction_Ship_Big_5b, Destruction_Ship_Med_1, Destruction_Ship_Med_2, Destruction_Ship_Med_2b, Destruction_Ship_Med_2c, Destruction_Ship_Med_3, Destruction_Ship_Small_1, Destruction_Ship_Small_1b, Engine_09, Engine_09_Broken, Engine_Freighter_02, Engine_Freighter_03, Engine_newnew_02, Engine_newnew_03, Engine_newnew_05, Engine_newnew_06_mixdown, Explosion_Bomb_AMR_Exctinctor_02, Explosion_Bomb_AMR_Oppressor_02, Explosion_Bomb_AMR_Tormentor_01, Explosion_EMP_GL1_01, Explosion_EMP_GL2, Explosion_EMP_GLDX_01, game_over_v02, Garbage_Explosion_01, Garbage_Explosion_02, Incoming_Fire_Armor_V02 b, Incoming_Fire_Armor_v02a, Incoming_Fire_Armor_V02c, Incoming_Fire_Hull_v01, Incoming_Fire_Hull_v02, Incoming_Fire_Shield_v01, Incoming_Fire_Shield_v02, Jump_to_planets_01, Jumpgate_1b, Jumpgate_2b, Jumpgate_3b, Jumpgate_4c, Jumpgate_5c, Jumpgate_Charge_02, Laser_Berger_Focus_2, Laser_Berger_Focus_Converge_v01, Laser_Berger_Focus_I_v01, Laser_Berger_Focus_Retribution_v01, Laser_Enemy_V04b, Laser_Nirai_Charged_Impulse_v01, Laser_Nirai_Impulse_EX1_V01, Laser_Nirai_Impulse_EX2_V01, Laser_Shkoom_v02, Laser_Vossk_V01, Laser_Vskorr_v01, Launch_Missile_Amour_01, Launch_Missile_Dephase_01, Launch_Missile_Edo_01, Launch_Missile_Edo_02, Launch_Missile_EMP_GL1, Launch_Missile_EMP_GL2, Launch_Missile_EMP_GLDX_02, Launch_Missile_EMP_MK1_02, Launch_Missile_EMP_MK2_01, Launch_Missile_Gliissk_01, Launch_Missile_IntelliJet_01, Launch_Missile_Mamba, Launch_Missile_Skoonn_01, Launch_Missle_JetRocket_01, Lounge_Confirm_Alien_1, Lounge_Decline_1_Alien, Lounge_Difficulty_1_Alien, Lounge_Talk_To_Alien_1, Low_Life_v03, Mining_Drill_Add_1, Mining_Drill_Add_2, Mining_Drill_Broken, Mining_Drill_Slow_1, Mining_Drill_Switch, Mining_Landing_01, Mission_Accomplished_v05, Rocket_Launch_AMR_Extinction_01, Rocket_Launch_AMR_Oppressor_01, Rocket_Launch_AMR_Oppressor_02, Rocket_Launch_AMR_Tormentor_01, Rumble_CutScene_01, Spaceship_Engine_05_Broken_02, Spaceship_Engine_05_Broken_02_Add_01, Spaceship_Engine_05_Broken_02_Add_02, Spaceship_Engine_05_Broken_02_Add_03, Spaceship_Engine_05_Broken_02_Add_04, Spaceship_Engine_05_Broken_02_Add_05, Spaceship_Engine_05_Broken_02_Add_06, Spaceship_Engine_05_Broken_02_Add_07, Spaceship_Engine_05_Broken_02_Add_08, Spaceship_Engine_05_Broken_02_Add_09, Spaceship_Engine_05_Broken_02_Add_10, Spaceship_Engine_05_Broken_02_Add_11, Spaceship_Engine_07, Spaceship_Engine_08, Spaceship_Engine_v1, Target_Lock_v08, Thermalfusion_Thermic_MaxHeat_v3, Thermalfusion_Thermic_O5_v2c, Thermalfusion_Thermic_ReHeat_v2b, Tractor_Beam_Close_Door_01c, Tractor_Beam_v1, Turret_Hammerhead_Lksaar_01, Turret_Hammerhead_MK1, Turret_Hammerhead_MK2_b, Wormhole_1b
- SFX_STATION_MAINVIEW: Map_Click_01, Map_Select_Planet_Push_01, Map_Select_Planet_Release_01, Map_Zoom_In, Map_Zoom_Out, Select_System_04, Station_Atmo_Mainview_1, Station_Atmo_Mainview_Add_1
- SFX_STATION_HANGAR: Button_Dismount_v02, Button_Info_v01, Button_Mount_v01, Button_to_ship_v01, Button_to_station_v01, Station_Atmo_Hangar_Add_1, Station_Atmo_Hangar_Add_2, Station_Atmo_Hangar_Add_3, Station_Atmo_Hangar_Add_4, Station_Atmo_Hangar_Add_5, Station_Atmo_Hangar_Add_6, Station_Atmo_Hangar_Add_7, Station_Atmo_Hangar3
- SFX_STATION_LOUNGE: Loung_Difficulty_2, Loung_Difficulty_fem_01, Lounge_Confirm, Lounge_Confirm_fem_02, Lounge_Decline_2, Lounge_Decline_fem_01, Lounge_Talk_To_2, Lounge_Talk_To_fem_03, Lounge_Where_v01, Station_Atmo_Lounge_1, Station_Atmo_Lounge_Add_1, Station_Atmo_Lounge_Add_10, Station_Atmo_Lounge_Add_11, Station_Atmo_Lounge_Add_11b, Station_Atmo_Lounge_Add_12, Station_Atmo_Lounge_Add_13, Station_Atmo_Lounge_Add_14b, Station_Atmo_Lounge_Add_15b, Station_Atmo_Lounge_Add_16, Station_Atmo_Lounge_Add_3, Station_Atmo_Lounge_Add_4, Station_Atmo_Lounge_Add_5, Station_Atmo_Lounge_Add_6, Station_Atmo_Lounge_Add_7, Station_Atmo_Lounge_Add_8, Station_Atmo_Lounge_Add_9
- DLC_SFX: AMR_Claymor_02, AMR_Liberator_Engine_02, Berger_AGT_01_Loop, Berger_Flak_01, EngineDLC_01, EngineDLC_04, EngineDLC_05, EngineDLC_06, EngineDLC_07, EngineDLC_08, EngineDLC_09, EngineDLC_11, Flak_Explosion_02, Icarus_Heavy_01, Invincibility_01, Ksannk_04, Laser_Disruptor_03, Liberator_Launch, Neetha_01, Nirai50_02, Polytron_Boost_01, Skuld_AT_XR_01_Loop, Sunfire_01, TimeShift_01b, Turret_HH_AT_Archimedes_03_Loop
- DLC2_SFX: Berger_SG100, Blaster_Mimung_2, Canon_Massdriver_MD12, Cluster_Explosion_01, Container_01, Crimso_Drain, CS102_CarrierJump_2, DarkMatterLaser_04, Docking_Landing_01, Explosion, Extractor_Loop_01, Fireworks, Gamma_Shield_01, Gamma_Shield_03, GOF2_Valkyrie_RayBeam_1, Hacking_Dialling_1, Hacking_Solved_1, Launch, Launch_02, M6_A4_RACOON_02, Mud1, Mud2, Mud3, Mud4, Nirai_SPP_C1_2, Nirai_SPP_M50_2, Pandorra_Leech_01, Plasma_Rocket_Explosion, PlasmaInjector_End_01, PlasmaInjector_Init_02, PlasmaInjector_Loop_03, Selfdestruct_Warning, SentryGun_SG400_02, Shockblast_01, Transport_Loop, TSuum, Turret_Matador_TS_01
- CUTSCENES: Alert_03, Explosion_Mothership_Loop_01, Explosion_Mothership_Loop_05, Mothership_Xplosion_CutSeq, Mothership_Xplosion_Loop_01, Mothership_Xplosion_Loop_01_Add_01, Mothership_Xplosion_Loop_01_Add_02, Mothership_Xplosion_Loop_01_Add_03, Mothership_Xplosion_Loop_01_Add_04, Mothership_Xplosion_Loop_01_Add_05, Mothership_Xplosion_Loop_01_Add_06, Mothership_Xplosion_Loop_01_Add_07, Mothership_Xplosion_Loop_01_Add_08, Mothership_Xplosion_Loop_01_Add_09, Mothership_Xplosion_Loop_01_Add_10, Mothership_Xplosion_Loop_01_Add_11, Mothership_Xplosion_Loop_01_Add_12, Mothership_Xplosion_Loop_01_Add_13, Mothership_Xplosion_Loop_01_Add_14, Mothership_Xplosion_Loop_01_Add_15, Mothership_Xplosion_Loop_01_Add_16, Mothership_Xplosion_Loop_01_Add_17, Mothership_Xplosion_Loop_01_Add_18, Mothership_Xplosion_Loop_01_Add_19, Spaceship_Engine_05_Broken_02_Add_12, Spaceship_Engine_05_Broken_02_Add_13, Spaceship_Engine_05_Broken_02_Add_14, SpaceTimeJump_01
- SVMODE: SV_Mode_01, SV_Mode_02, SV_Mode_03, SV_Mode_04, SV_Mode_05, SV_Mode_06, SV_Mode_07, SV_Mode_08, SV_Mode_09, SV_Mode_10

## Event sounds and music

The words of the event commands `/sound <word>` and `/music <word>` (the Play Sound / Play Music nodes' lists); `/music` also
takes a mod's own track name.

- Sounds: alarm, warning, success, message, info, click, explosion, smallexplosion, jumpgate, khador, charge, lock, boost, shield, timeshift, medal
- Music: battle, battlelow, battlefull, voidbattle, voidcalm, boss, specters, spectershigh, intro, outro, homebase, valkyrie, gamma, deepscience, terran, vossk, nivelian, midorian, mothership, stop

## Built-in event graphs

Usable with `/event <name>` and Start Event; a mod's graph of the same name replaces it.

pirate_hideout, survival, waves
