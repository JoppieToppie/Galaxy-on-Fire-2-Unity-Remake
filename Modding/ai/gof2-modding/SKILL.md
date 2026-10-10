---
name: gof2-remake-modding
description: Make mods for the Galaxy on Fire 2 Unity Remake - JSON data mods with new or changed items, weapons, ships, blueprints, new-game option cards, star systems and stations, campaigns, characters, music, sounds and skins. Use when someone wants to create, fix, extend or balance a GoF2 Remake mod, or asks how its mod files work.
---

# Galaxy on Fire 2 Remake modding

You help a player make a mod for the Galaxy on Fire 2 Unity Remake. A mod is a folder (or a .zip of it) of JSON files plus
the PNG / GLB / OGG assets they name. Mods contain no code. This file is the complete format reference; `reference.md` next to
it lists every original item, ship, system and station with its number and stats. If you can't open `reference.md`, ask the
user to paste the part you need (or to attach it).

## How to work

1. Find out what the mod should do. Ask for anything that changes the files: names, which ships/items it builds on, prices,
   when content should appear, which assets (models, images, sounds) the user has or will make.
2. Write every file in full, each under its path inside the mod folder (`my_mod/items.json` ...). JSON only: no trailing
   commas. `//` comments are allowed but keep files plain unless the user wants notes.
3. Use real numbers: an original item, ship, system or station is referred to by its number from `reference.md`; never
   guess one. Content from a mod (this one or another) is referred to by its key `mod_id:id`.
4. You can't make binary assets. For every PNG / GLB / OGG a file names, tell the user exactly what to provide (size, format,
   layout, where to put it). Paths in JSON are relative to the mod folder, with `/`.
5. Event graphs (quests, bar missions, cutscenes) are made in the Unity Editor, not written by hand. For those, give the user
   a node-by-node recipe (see "Quests").
6. Check your output against the checklist at the end before giving it.

The player installs the mod by copying the folder (or zip) into the game's `Mods` folder, pressing **Refresh** in the main
menu's **Mods** screen and turning it on. Problems show there with file and line: errors stop the mod, warnings skip a field.

## Basics

- **Units:** 20 game units = 1 metre. Times in milliseconds. Speeds in game units per millisecond.
- **Races:** 0 Terran, 1 Vossk, 2 Nivelian, 3 Midorian, 8 pirate, 9 Void, 10 Specter. Systems/stations use 0-3. Some fields
  take race names instead (`"terran"`, `"vossk"`, `"nivelian"`, `"midorian"`, `"pirate"`, `"void"`, `"specter"`, `"any"`).
- **Ids:** lowercase `a-z`, `0-9`, `_`, `-` (max 40 for the mod id). Ids are permanent: saves refer to them, never rename after
  release. Inside one file an id must be unique.
- **Keys:** the game knows mod content as `<mod id>:<id>` (`plasma_arsenal:plasma_lance`). Use the key to refer to another
  mod's content (put that mod in `dependencies`) or to your own content in other files. Your own items listed earlier in
  items.json can be named by key too.
- **Texts:** `name` / `description` are a string or one per language: `{ "en": "Plasma Lance", "de": "Plasmalanze" }`.
  Languages: en, de, fr, es, it, nl, pl, ru, pt, zh, hi, ar, ja, ko. Or `text/<lang>.json` files with keys like
  `items.<id>.name`, `items.<id>.description`, `ships.<id>.name`, `systems.<id>.name`, `stations.<id>.name` (an override uses
  the number: `items.12.name`).
- **Economies:** players pick Android (default numbers) or Default (PC prices). Ships have `priceDefault`; items have a
  `defaultEconomy` block with the same fields.

## Mod folder

```
my_mod/
  mod.json            required
  preview.png         Mods screen image, 16:9 (640 x 360)
  items.json          new / changed items and weapons
  ships.json          new / changed ships (models in models/, icons in icons/)
  blueprints.json     blueprints for ships and items
  gameoptions.json    cards in the new game's Game options panel
  campaign.json       a whole new game under Start new game
  systems.json, stations.json, interiors.json   new star systems, stations, hangars and bars
  characters.json     speakers with portraits
  events/*.gof2event  quests and bar missions (made in the Unity Editor)
  text/<lang>.json    translations
  music/, sounds/, voices/, textures/          replacement / new audio and skins
```

### mod.json

```json
{
    "id": "canon_ships",
    "name": "All Canon Ships",
    "version": "1.0",
    "author": "Torch",
    "description": "The ships of Galaxy on Fire 3D and 3.",
    "preview": "preview.png",
    "credits": "Models: ..., licence ...",
    "dependencies": ["other_mod", "ship_pack>=1.2"]
}
```

`id` is required; `name`/`description` can be per language; `preview` defaults to `preview.png`.

## items.json

A list. Each entry adds an item (`"id"` + `"base"`) or changes one (`"override"`).

```json
[
    { "id": "plasma_lance", "base": 12, "name": "Plasma Lance", "description": "...", "techLevel": 3, "occurrence": 40,
      "price": [24000, 26500], "stats": { "damage": 14, "reloadMs": 700, "lifetimeMs": 1800, "speed": 16 } },
    { "override": 50, "stats": { "shieldCapacity": 65 }, "maxPrice": 4600 }
]
```

A new item is a copy of `base` (required: an original number or a key): it keeps the base's type and category and looks,
sounds and works like it (icon, projectile, special behaviour); every field you give replaces the base's value. Pick the
base for the behaviour you want (a blaster for a blaster, a homing missile for a homing missile). Blueprint recipes aren't
copied.

| Field | |
|---|---|
| `name`, `description` | String or per language. |
| `techLevel` | 1-10: shops sell items up to their station's tech level. |
| `occurrence` | How often shops stock it, 0-90 (0 = never randomly). |
| `price` / `minPrice`, `maxPrice` | `1500` or `[1200, 1500]`; a station's price lies in the range. |
| `lowestPriceSystem`, `highestPriceSystem` | System numbers where it's cheapest / dearest. |
| `vosskOnly` | `true`: only Vossk systems sell it. |
| `alwaysSoldAt` | A station number that always stocks it (78 = Var Hastra, the start; -1 none). |
| `stats` | Named stats (below); copy the base item's stats from `reference.md` and change them. |
| `attributes` | Raw attribute numbers `{ "9": 14 }` for anything without a name (not 0-8). |
| `defaultEconomy` | The same fields for the Default economy. |
| `icon` | Own shop icon PNG, 180 x 88 (frame included). Default: the base's. |
| `fx` | The weapon's own look and sound (below). |
| `available` | A condition (see Conditions) before shops stock it or loot drops it. |

Stats (name = attribute): damage 9, empDamage 10, reloadMs 11, lifetimeMs 12 (shot flight time; with speed = range), speed 13
(units/ms), blastRadius 14, guided 15, automatic 16 (auto turret), turretTurnSpeed 17, shieldCapacity 18, shieldRegenMs 19,
armor 20, cargoBonus 22, tractorAuto 23 (1 nearest, 2 any crate), tractorLockMs 24, boostSpeed 25, boostRechargeMs 26,
boostDurationMs 27, agility 28 (steering nozzle), lockTimeMs 29 (scanner), showClassAAsteroids 30, radarShowsCargo 31,
drillSpeed 32, miningYield 33, cabins 34, cloakDurationMs 35, cloakChargeMs 36, energyCells 38 (per cloak), fireRateFactor 39,
damageFactor 40 (weapon mods %), emergencyMs 41, timeExtenderMs 42, timeExtenderCooldownMs 43, collectorSpeed 49,
collectorMagnitude 50, collectorRange 51, gammaShielding 52, beamRange 53, beamStrength 54, beamTargets 55, miningBeam 100 (a
drill based on 86-90 that cuts while flying), miningBeamRange 101, miningBeamLayerMs 102, miningBeamLook 103 (9/10/11/228), phaseCloak 104 (a
cloak based on 94-96 whose ship passes through asteroids, stations, gates, ships, shots and explosions while cloaked).
items.json's own names (`loadingTimeMs`, `range`, `projectileSpeed`...) work too. An unknown stat name is an error.

### Weapon fx

```json
"fx": {
    "projectile": { "sprite": "fx/bolt.png", "size": 450, "stretch": 4, "alongFlight": true, "color": [0.75, 0.45, 1.0], "glow": 3 },
    "muzzle": { "sprite": "fx/flash.png", "size": 500, "lifetime": 90, "grow": 1.4 },
    "impact": { "sprite": "fx/impact.png", "size": 2400, "lifetime": 280, "grow": 2.2 },
    "shot": ["fx/shot_1.ogg", "fx/shot_2.ogg"], "shotLoops": false, "explosionSound": "fx/boom.ogg"
}
```

Each of projectile / muzzle / impact is `false` (none) or an object: `sprite` (PNG, white on black or transparent; `color`
tints it) or `model` (GLB; with `texture` drawn glowing), `size` (game units; a blaster bolt is ~1600, an impact glow
several thousand), `color` (`[r,g,b]` 0-1 or `"#rrggbb"`), `glow` (>1 blooms; game effects ~2.5), `additive` (default true),
`lifetime` ms (muzzle 100, impact 300), `grow`, `fade`, `spin` (deg/s), `alongFlight` + `stretch` (laser bolts), `rotate`
(models). Sounds: `.ogg` / `.wav` / `.mp3`. Behaviour never changes (a beam stays a beam).

## ships.json

```json
[
    {
        "id": "valkyrie", "name": "Valkyrie", "description": "...", "race": 0,
        "armor": 480, "cargo": 60, "handling": 150, "price": 3800000, "priceDefault": 7600000,
        "slots": { "primary": 4, "secondary": 2, "turret": 0, "equipment": 12 },
        "model": "models/valkyrie.glb", "icon": "icons/valkyrie.png", "modelLength": 1000, "modelYaw": 0, "hangarHeight": 290,
        "mounts": [
            { "slotType": 0, "position_engine": [-73, -23, 442] },
            { "slotType": 0, "position_engine": [73, -23, 442] },
            { "slotType": 1, "position_engine": [0, -48, 115] },
            { "slotType": 3, "position_engine": [0, 0, -478], "turretAngles": [0.55, 0.55, 0.55] }
        ],
        "lounge": { "systemRace": 0, "minCampaign": 32, "minRank": 12, "chance": 12 },
        "dealer": { "chance": 2, "systemRace": -1, "minTechLevel": 5 },
        "available": "option(canon_ships:gof3) and won(supernova)"
    },
    { "override": 10, "armor": 260, "cargo": 60 }
]
```

| Field | |
|---|---|
| `race` | Builder: 0-3, 8 pirate, 9 Void (1 % cheaper in its race's systems). |
| `armor`, `cargo`, `handling` | Hull points, hold (t), handling (originals 30-162; 100 average). |
| `price`, `priceDefault` | Price; Default economy price (0 = same). |
| `slots` | primary, secondary, turret, equipment. One turret slot per turret mount. |
| `model` | **Required** for a new ship: `.glb`/`.gltf` in the mod (Blender: glTF Binary, +Y up; nose toward +Z). |
| `modelLength`, `modelYaw` | Nose-to-tail length in game units (Phantom ~1000); yaw degrees if the nose isn't +Z. |
| `icon` | Shop icon PNG 180 x 88. Default: the Phantom's. |
| `hangarHeight` | Pivot height above the hangar pad (originals 140-400). |
| `mounts` | Game units from the ship centre (x right, y up, z forward). `slotType` 0 primary, 1 secondary, 2 turret (`upsideDown`), 3 engine exhaust (`turretAngles[0]` particle size, `glowColor` [r,g,b], `glowSize` [halfW, halfH, length]). Give each gun slot its own mount where possible (the originals do). |
| `engineGlowRadius`, `engineGlowColor` | Flame size (default 24) / colour for all exhausts. |
| `materials` | Replace materials: entries `{ "mesh", "submesh", "diffuse", "color", "normal", "normalScale", "metallicSmoothness", "metallic", "smoothness", "emission", "emissionColor", "emissionIntensity", "alphaClip", "opacity", "detailAlbedo", "detailNormal", "detailTiling", "detailNormalScale", "doubleSided" }`. Without it the GLB's own materials are used. |
| `throttleGlow`, `extraGlows` | Hull parts glowing with the throttle: `{ "mask", "mesh", "submesh", "color", "idle", "full", "boost", "offset", "trailWidth", "trailTime", "trailBrightness", "trailCount" }`. |
| `lounge` | A Space Lounge visitor sells it: systemRace (-1 any), minCampaign (story step), minRank (free play), chance %. |
| `dealer` | Ordinary ship dealers stock it: chance % (decimals ok) per dealer list, systemRace, minTechLevel. Not the special yards. |
| `available` | Condition before dealers / lounges sell it. |

`override` changes an original (number) or another mod's ship (key): armor, cargo, price, priceDefault, slots, handling, name,
description, `mounts` (mod ships also race, hangarHeight), and an original can get a new `model` (+ its fields). `slots` merges
(give only the changed counts); `mounts` replaces the ship's whole list (exhausts too). More weapon slots than gun mounts: the
extra weapons share the mounts; fewer slots: what doesn't fit goes to the hold on the next docking.

## blueprints.json

Built in the hangar's Blueprints tab: invest ingredients (or autocomplete); the product is made at the station of the first
investment. A ship product waits there and replaces the player's ship like a purchase (old ship traded in at its price, or
parked in the Kaamo Club if owned).

```json
[
    {
        "id": "valkyrie",
        "ship": "canon_ships:valkyrie",
        "ingredients": [ { "item": 155, "amount": 400 }, { "item": 127, "amount": 60 }, { "item": 122, "amount": 100 } ],
        "autocomplete": 1500000,
        "available": "option(canon_ships:gof3) and won(supernova)",
        "lounge": { "chance": 3, "price": 400000, "systemRace": -1 },
        "derelict": { "chance": 2, "race": "terran" },
        "drops": [ { "race": "pirate", "chance": 1 }, { "race": "vossk", "chance": 0.5 } ]
    },
    {
        "id": "valkyrie_gold",
        "ship": "canon_ships:valkyrie_gold",
        "requiresShip": "canon_ships:valkyrie",
        "ingredients": [ { "item": 154, "amount": 1000 }, { "item": 165, "amount": 100 }, { "item": 122, "amount": 300 } ],
        "autocomplete": false,
        "unlocked": true,
        "available": "option(canon_ships:gof3) and won(supernova)"
    }
]
```

| Field | |
|---|---|
| `id` | **Required.** |
| `ship` **or** `item` | **Required, exactly one.** The product (key or number). An item that already has a blueprint gets yours. Secondary weapons come 10 per run. |
| `ingredients` | **Required, at least one.** `{ "item", "amount" }` (amount >= 1). Commodities, ores, cores and items (demounted). |
| `requiresShip` | Only built while flying that ship; the result replaces it (a skin). Kaamo upgrades stay. |
| `autocomplete` | `true` (default, 1.25 x product price), a number (that price) or `false` (never). |
| `available` | Condition before any source offers it. |
| `unlocked` | `true`: known as soon as available (next docking). |
| `lounge` | Lounge seller: chance % per bar refresh, price (default half the product price), systemRace. |
| `derelict` | chance % of orbit visits holding a hackable derelict freighter (race terran/vossk/nivelian/midorian/any) with it. |
| `drops` | `{ "race", "chance" }`: chance % (decimals ok) that a ship of that race the player destroys drops it. |

Found blueprints come in a data container the tractor beam pulls in. Sources stop once the player knows the blueprint.
Useful ingredients (see reference.md): 122 Energy Cells, 127 Microchips, 154 Gold, 155 Titanium, 157 Orichalzine, 165 Golden
Core, 114 Explosives, 118 Electronics.

## gameoptions.json

Cards in Start new game's **Game options** panel, each a switch saved with the game; content asks for it with `option(...)`.

```json
[
    { "id": "gof3", "name": "GoF3 Ships", "description": "Galaxy on Fire 3's ships as blueprints after the Supernova.",
      "image": "cards/gof3.png", "imageHover": "cards/gof3_hover.png", "showTitle": false, "default": true }
]
```

`id` required. `image` is card art like the game's campaign cards: **290 x 448** or a multiple (580 x 896), portrait; cropped
to the card. `showTitle` false when the art carries its own title (else a name plate is drawn at the foot). `default` is the
first-time position and what multiplayer uses. A game started before the mod was installed has the option off.

## Conditions

Used by `available` (items, ships, blueprints) and quests' "Starts when". Expression language:

| Word | Value |
|---|---|
| `option(mod_id:option_id)` | 1 if that game option is on in this game (`option(option_id)` works if unique). |
| `won(main)`, `won(valkyrie)`, `won(supernova)` | 1 if that campaign's story is finished. |
| `systemsvisited` | Star systems docked in; `>= 2` = has left Mido (the start system). |
| `campaign` | Story step (0-162; -1 in free play / mod campaigns). |
| `rank`, `credits`, `kills`, `station`, `system`, `ship` | Player state. |
| `visited(station)`, `has(item)`, `cargo(item)`, `quest(name)` | Docked there (1/0); count owned incl. mounted / in hold; quest 0 none, 1 running, 2 done. |

Operators: `and`, `or`, `not`, `==`, `!=`, `<`, `<=`, `>`, `>=`, `+ - * /`, parentheses, `min(a,b)`, `max(a,b)`,
`floor(x)`, `random(a,b)`. Example: `"option(canon_ships:gof3d) and systemsvisited >= 2"`. An unreadable condition counts as
false and warns.

## campaign.json

A whole new game under Start new game, shown as a card next to the three campaigns.

```json
{
    "name": "Kepler Frontier", "description": "...", "image": "card.png", "imageHover": "card_hover.png", "showTitle": true,
    "startStation": "frontier_systems:kepler_prime", "startShip": 10, "credits": 25000,
    "equipment": [0, { "item": 37, "amount": 12 }], "cargo": [{ "item": 122, "amount": 5 }], "standing": [40, 10],
    "quest": "kepler_story", "galaxy": "mod", "items": "all", "ships": "all",
    "trafficShips": { "terran": ["starwars_ships:jedi_starfighter"], "pirate": [2, 23] }
}
```

`startStation` and `startShip` required. `image` 290 x 448 card art. `standing` = [Terran/Vossk, Nivelian/Midorian] -100..100
(default [30, 0]). `galaxy`/`items`/`ships` `"mod"` limit the game to mods' content. `quest` = an event graph started with
the game.

## systems.json, stations.json

```json
[ { "id": "kepler", "name": "Kepler", "race": 0, "security": 2, "visible": true, "position": [8, 27, 83], "sky": 4,
    "gates": [4, 17], "gateStation": "frontier_systems:rho_freeport", "sunTexture": "textures/sun.png", "sunColor": [1, 0.8, 0.55],
    "skybox": { "nebula": "sky/nebula.png", "stars": 1, "nebulaBrightness": 0.8 }, "spaceMusic": "kepler_space" } ]
```
```json
[ { "id": "kepler_prime", "system": "frontier_systems:kepler", "name": "Kepler Prime", "techLevel": 7, "planet": 12,
    "looksLike": 40, "model": "models/station.glb", "modelSize": 50000, "collision": "box", "interior": "nivelian",
    "planetTexture": "textures/planet.png", "music": "kepler_bar" } ]
```

Systems: race 0-3, security 0 (lawless) - 3 (secure), map position (originals x 15-94, y 2-96, z 10-90), sky 0-18, gates
both ways, up to 7 stations each. Skybox images: 2:1 panorama or 6:1 cube strip (right, left, up, down, front, back), light
on black, 8192 x 4096 sharp. Stations: techLevel 0-10, planet 0-22 or 24-26, looksLike = an original station whose model
it borrows, modelSize = largest extent in game units (default 40000), collision box/sphere/none or `volumes` (what NPC fighters steer around; the player and shots use the model's real meshes, `none` turns all off), interior =
race of hangar and bar, or own rooms `hangar` / `bar` from interiors.json (GLB rooms with marker empties: hangar `pad`,
`camera` required, `camera_target`, `parked_N`, `gate`, `gate_out`, `light`; bar `camera`, `camera_target`, `visitor_N`
required). `override` changes originals (name, techLevel, looks, music...).

## characters.json, voices, music, sounds, textures

- `characters.json`: `[{ "id", "name", "portrait": "portraits/x.png" (4:5, 320 x 400), "race", "gender", "mirrored",
  "background", "frame", "replaces" }]`. Named in dialogs as `id`, `mod_id:id` or the name; `id as Other Name`.
- Voice-over: `[voice <clip>]` in a Dialog page / Radio line; files `voices/<clip>.ogg` (or `voices/en/`, `voices/de/`).
- `music/<name>.ogg`: a game track's name (`Space_Terraner`, `Station_Vossk`, `Space_Battle_Full`, `Space_NoCombat_Void`
  (main menu)...) replaces it; any other name is a new track for systems/stations (`spaceMusic`, `stationMusic`, `music`) and
  quests.
- `sounds/<name>.ogg`: replaces the game sound with that file name (from the game's Assets/Audio folders).
- `textures/<name>.png`: replaces that game texture everywhere (`ship_028_terran_diffuse`; keep the original UV layout,
  2048 x 2048). Also the sky's planets, suns and rings (`planet_000_small` ... `planet_019_big`, `planet_void_*`,
  `sn_planet_024_*` ... `sn_planet_026_*`, `sun_000` ..., `sn_sun_011`, `sn_planet_ring`): one whole disc image each, keep the
  transparent edge. `backdrop.json` `{ "planets": "star", "planetGlow": 2, "planetFlare": 0.35 }` draws the retextured planets
  as stars (facing the camera, no ring, a core glow, a weaker copy of the sun's flare).

## Quests and bar missions (event graphs)

Made as `.gof2event` node graphs in the Unity Editor (the remake's project: Assets > Create > GoF2 > Event Graph), saved
to the mod's `events/` folder; the file name is the graph's name. The Start node's Kind: **Quest** (starts by itself when
"Starts when" holds), **Bar Mission** (offered by a lounge visitor; Offered at, Client, Reward) or **Event** (multiplayer).
Nodes include Dialog, Radio, Spawn (named ships), Wait / Wait Until, On (docked, entered orbit, destroyed, cleared, near a
point), Set Waypoint, Set Objective, Checkpoint, Quest Orbit, Give Item, Reward, Mission Complete / Failed, Start Quest, ship
orders (Fly To, Fly Route, Follow, Attack, Ship Action), cutscene nodes (Start Cutscene, Camera Shot, Fade, End Cutscene),
Play Music. You can't write the file; describe the graph as an ordered list of nodes with their settings for the user to
build.

## Balancing (the originals, Android economy)

| Price band (credits) | Armor | Cargo t | Handling | Primary slots | Equipment slots | Examples |
|---|---|---|---|---|---|---|
| up to ~80 000 (starter) | 95-190 | 25-110 | 102-160 | 1-3 | 3-7 | Betty, Wasp, Cronus, Taipan |
| ~85 000-470 000 (low) | 115-1200 (freighter types) | 28-480 | 30-162 | 0-4 | 5-9 | Hera, Teneta, Rhino |
| ~630 000-3 100 000 (mid/high) | 145-335 | 40-320 | 90-150 | 2-4 | 9-13 | Phantom (200/52/150, 4 slots 2/1/0/9), Ward |
| ~3 300 000-12 000 000 (top) | 235-1000 | 30-180 | 50-155 | 1-5 | 5-16 | Scimitar, Ghost, Nemesis, Specter (800 armor, 12 M) |

Guidance: stay inside a band's ranges for "as good as" that band; beat its best values only for ships meant to be special
(and price them above 12 M or gate them behind blueprints / late conditions). Weapons: compare damage x (1000 / reloadMs) to
the originals of the same category and tech level in reference.md. Prices roughly follow power; tech level gates where
it's sold.

## Example: a small canon-ships mod

```
canon_ships/
  mod.json
  gameoptions.json   two cards: gof3d (default on), gof3 (default on)
  ships.json         viper_3d (weak, dealer chance 1 after leaving Mido), valkyrie + valkyrie_gold (blueprint only)
  blueprints.json    valkyrie (lounge / derelict / drops, autocomplete 1.5 M), valkyrie_gold (skin, no autocomplete)
  models/*.glb, icons/*.png (180 x 88), cards/gof3d.png + cards/gof3.png (580 x 896)
```

ships.json entry for the weak GoF3D ship (no better than the worst originals):

```json
{ "id": "viper_3d", "name": "Viper", "race": 0, "armor": 110, "cargo": 30, "handling": 110, "price": 45000,
  "slots": { "primary": 1, "secondary": 1, "turret": 0, "equipment": 4 },
  "model": "models/viper_3d.glb", "icon": "icons/viper_3d.png", "modelLength": 900, "hangarHeight": 250,
  "mounts": [ { "slotType": 0, "position_engine": [0, -20, 400] }, { "slotType": 1, "position_engine": [0, -40, 100] },
              { "slotType": 3, "position_engine": [0, 0, -420], "turretAngles": [0.5, 0.5, 0.5] } ],
  "dealer": { "chance": 1, "systemRace": -1, "minTechLevel": 0 },
  "available": "option(canon_ships:gof3d) and systemsvisited >= 2" }
```

## Checklist

- mod.json has `id`; every other file is the right shape (items/ships/blueprints/gameoptions/characters/systems/stations/
  interiors are lists `[ ... ]`; campaign.json and mod.json are objects `{ ... }`).
- Every entry has `id` (new) or `override` (change), never both; new items have `base`; new ships have `model`; blueprints
  have exactly one of `ship` / `item` and at least one ingredient.
- Ids unique per file, lowercase `a-z0-9_-`. Cross-file references use `mod_id:id`; other mods are in `dependencies`.
- Original numbers come from reference.md (items 0-232, ships 0-63, systems 0-33, stations 0-134).
- Stat names from the list above; numbers are integers (except chances, colours, sizes).
- Conditions use only the words above; option keys match gameoptions.json ids (`mod_id:option_id`).
- Every file path named exists in the mod; images: card art 290 x 448 (or x2), icons 180 x 88, preview 16:9, portraits 4:5.
- Prices and stats sit in the intended balance band.
