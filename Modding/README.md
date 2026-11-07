# Making mods for the Galaxy on Fire 2 Unity Remake

A mod is a folder (or a `.zip`) of plain files: JSON for data, PNG for images. Mods contain no code, so a mod you download can't
harm your computer. Players turn mods on and off in the main menu's **Mods** screen.

**Making a mod in the game:** main menu > **Mods** > **Mod Builder** (experimental) makes projects in this same format
without writing any JSON: new ships from a `.glb` model or on one of the game's ship models (no 3D file needed), or
changes to the game's ships, their stats and textures, their
hardpoints placed on the hull in 3D (turrets facing out of the hull where they sit: top, sides, under it or any slant),
and test flights against pirates; new items made from a game item (or changes to one) with their own stats and icon (the
remake-only stats too: missiles and salvos, a turret's range, mining beams, phase cloaks), a turret's model (a game
turret's or your own `.glb`), resized and moved on its mount, a weapon's projectile, muzzle flash, impact and sounds
borrowed from the game's other weapons, fitted to the ship to try them out; characters with their portraits, for your missions
or standing in for a story character; and skins: a game ship's textures replaced, with its originals saved for you to paint
over. A new ship's shop icon is rendered from its model
(`icons/<id>.png`; put your own PNG in `icon` and the builder leaves it alone). Each kind of content is edited where it lives
in the game:

| Kind | Where | |
|---|---|---|
| Ships, Items, Skins | the hangar | as above; **Test fly** flies them against pirates |
| Ship kits | the hangar | customizable ships: the kit's slots, its parts (a `.glb` each) with their stats, weapon slots, tier models and hardpoints placed on the hull, paint slots; the turntable shows the kit's ship with the part you picked |
| Blueprints | the hangar | the product (a ship stands on the turntable), ingredients, autocomplete, where it is found |
| Campaign | the hangar | the card, the start (station, ship and its equipment, money, standings), its story mission, the galaxy / shops kept to mods; new-game options |
| Characters, Missions, Audio | the Space Lounge | portraits; story missions, bar missions and events in a node graph (or as a list of steps, below), Unity-made event graphs imported; music, sound effects and voice lines, each with ▶ |
| Systems | the star map | every system shown: drag yours to their place, their gates, sky, sun, music, stations |
| Stations, Weapon fx | in space | a station in its own orbit (model, size, turn, rooms, planet); weapons firing from one ship onto another |
| NPC ships | in space | capital ships, freighters and bosses nobody flies, new or the game's changed: the hull beside the station, hardpoints and docking pads clicked onto it, escorts, loot, where and how often they come, the dock's shop, launches |
| Hangars & bars | the room itself | a `.glb` room or one of the game's (its markers filled in), the markers clicked into the room, light, fog, lens |

Wherever content names other content (a mission's speaker, a station's system, a blueprint's product, a campaign's ship) the
picker offers the game's, your project's own (your character can speak in your mission) and **every other installed mod's**:
picking another mod's makes it a dependency of yours (Menu > Dependencies lists them). **Play** in the top bar starts a play
test: the game as a player has it with your mod on, from the builder's station, from your campaign's start or with one of
your missions under way (a bar mission is offered in the station's Space Lounge instead, by its client, whatever its offer
chance and stations: take it there as a player would); the pause menu's (or the station menu's) Back to Mod Builder ends it, and so does a death.
**Undo** / **Redo** in the top bar (Ctrl+Z, Ctrl+Y or Ctrl+Shift+Z) step back and forth through your edits, files you replaced
or removed included, until you close the project. Menu > **Optimize models and textures** (or **Optimize…** beside a ship's
model) makes a big download small: every texture inside your `.glb` models scaled down to 1K, 2K or 4K and stored as a JPEG
(a PNG where it uses transparency), your own image files scaled down in their own format, and, if you like, a model's
triangles cut to 75, 50 or 25 % (its seams and open edges are kept, so textures don't tear); a 61.7 MB model with nine 4K
PNGs came to 2.7 MB at 1K. Undo puts the original files back. With Options > Graphics > **Levels of detail** on (the
default) the game also makes simpler copies of your ships' and stations' models for when they are far away, from the
model as you ship it, optimized or not; they are made again by themselves after you optimize. **Export as mod** puts the finished project into your Mods folder.

**Mods for the original game** (the game's own `.aem` models and `.aei` textures, usually in `MESHES/` and `TEXTURES/`
folders, named like the files they replace): the projects page's **Import mod > From a file...** makes a project of such a
zip. A game ship's hull (`ship_016_nivelian.aem`) becomes a change to that ship with the new model at the length it had in
the game, so the ship's hardpoints still fit; its `_diffuse` and `_normal_specular` textures become the hull's textures
(the specular map as its smoothness). Any other `.aei` replaces the game's texture of that name, so a mod that only
changes textures becomes a skin. The ship's glowing layers become its `addParts` (see the table below), drawn like the
game's own ships draw them: `_lights_add` (the lights), `_engine_glow_add` (your engine glow) and `_engine_add` (the NPCs'
engine glow). Copies of the hull in those files (some mods fill them with it) and the `_lod_1` / `_lod_2` models are left
out. The first release's files work too: `ship_00.aem` with `ship_00_lights.aem` and `ship_00_add.aem` (its engine glow),
and its race's texture atlas (`tex_mido_ships.aei` ...) as the hull's and the parts' texture. The importer reads `.zip`
files: a `.7z` has to be repacked as a zip first. Every model picker in the builder also takes a single `.aem`, and every
texture picker an `.aei`.

Every entry's page has **Edit as JSON…**: the entry's fields as this guide lists them, also the ones the builder has no field
for (a ship's per-mesh `materials`, throttle and extra glows, a station's collision `volumes`, a room's `gateSpan`). The
Menu has **Mod details** (website, credits, the Mods screen's picture) and **Translations** (every name and description of
your items, ships, systems and stations in each language, `text/<lang>.json`); the Skins kind's **Other textures…** replaces
any game texture (a station's, a planet's, a sun's, a ring's) and turns your retextured planets into stars (`backdrop.json`).
**AI tools** in the top bar (Windows) is a switch for an MCP server: your own AI assistant (Claude Code, Claude Desktop,
other MCP apps) can then read the modding guide (`read_guide`), look up content, write entries and files, check missions, play-test the open project, look at models
and textures, aim the builder's camera, place hardpoints and room markers, set the test loadout, test-fly, play a mission
through by itself and export
(`Modding/ai/README.md` says how to connect; off by default, this computer only, with a token). Its **Permissions** say
what waits for your Yes: Read only, Ask always, Ask to remove (the default) or Allow all, so an assistant can also build
while you do something else.

**Making a mod with an AI assistant:** `Modding/ai/gof2-modding` is this guide condensed for Claude, ChatGPT and the like,
with every number and name of the original game a mod refers to in `reference.md` (items, stats, blueprints, ships, systems,
stations, hardpoints, skies, planets, story steps and speakers, models and their textures, music, sounds), every voice clip in
`voices.md`, every event graph node and command in `events.md`, and a copy of this guide as `guide.md`. See `Modding/ai/README.md` for how to use it.

What mods can do so far:

- **Add new items**: weapons, missiles, equipment and goods. Each new item is based on an existing one and takes its model,
  icon, sounds and behaviour, with its own name, description, stats, prices and availability.
- **Change existing items**: rebalance prices, stats, tech levels and availability.
- **Give weapons their own look and sound**: projectiles, muzzle flashes and impacts from your own sprites or 3D models,
  your own shot and explosion sounds.
- **Add new ships**: your own 3D model (glTF / GLB) with its stats, weapon mounts, turrets, engine glows and textures, sold
  by visitors in the Space Lounges and by the ordinary ship dealers.
- **Add customizable ships**: ships assembled from parts (body, wings, cockpit...) the player swaps in the hangar's
  **Customize ship** screen, like EVERSPACE 2's ship modules (see [Customizable ships](#customizable-ships)).
- **Add blueprints**: for ships (skins too) and items, with their ingredients and autocomplete price, found in lounges, in
  hackable derelict freighters and in the wrecks of the ships the player destroys.
- **Add new-game options**: a card the player switches on when starting a new game (like "All Canon Ships"), and content
  that only appears with it, or only later in the game (after leaving Mido, after the Supernova...).
- **Change existing ships**: armour, cargo, price, slots, handling, and their model.
- **Tune the flight, for either flight style**: items and ships that fly faster, turn quicker or boost harder in the
  original flight, in free flight (the EVERSPACE 2-style option) or both, and campaigns, ships, new-game options and missions
  that decide which of the two the game flies with (see [Flight styles](#flight-styles)).
- **Add star systems and stations**: on the star map, linked by jumpgates, with their own orbits, shops and bars, their own
  station models (glTF / GLB), planets and suns, and their own hangars and bars to dock in.
- **Add races**: new races beside the game's four, that own systems, fly ships and raid, each with its own reputation,
  relations and lounge diplomats.
- **Add story missions and bar missions**: story chains and side jobs made as event graphs in the Unity Editor, with dialogue,
  voice-over, objectives, map markers, checkpoints that saves keep, rewards and failure.
- **Add and replace music**: replace any of the game's tracks, or add new ones for your systems, stations and missions.
- **Replace sound effects**: any of the game's sounds, from the guns to the menu clicks and the voices.
- **Reskin ships and objects**: replace any of the game's textures, for example a ship's hull paint.
- **Build on other mods**: a story mod can use the ships, items and systems of a mod it depends on.
- **Make a whole new game**: a campaign of its own under New Game, with its own start, galaxy, shops and NPC ships.
- **Translate** their own texts into any of the game's languages.

**Image formats:** wherever this guide asks for a PNG (icons, portraits, textures and skins, planets, suns, skies, card art,
the preview), JPG, TGA (uncompressed or RLE), BMP, DDS (DXT1, DXT3, DXT5, ATC or uncompressed; not BC7) and the game's own `.aei`
files (from the PC, Android or iOS version) work too. PNG stays the safe choice. The Mod Builder's pickers take all of them and store the picture as a PNG.

## Where mods go

| Platform | Folder |
|---|---|
| Windows | `%USERPROFILE%\AppData\LocalLow\JoppieToppie\Galaxy on Fire 2\Mods`, or a `Mods` folder next to the game's `.exe` |
| Linux | `~/.config/unity3d/JoppieToppie/Galaxy on Fire 2/Mods`, or a `Mods` folder next to the game |
| Android | `Android/data/com.joppietoppie.gof2remake/files/Mods` (reachable over USB) |
| Unity Editor | `<project>/Mods` (not part of the repository) |

The **Open mods folder** button in the Mods screen opens the folder the selected mod is in (with no mod installed, the first
one above; on a phone the screen shows its path). The selected mod's details show where it is installed. After copying a
mod in, press **Refresh**. New mods start turned off.

Or press **Import mod** and pick the mod's `.zip` file with the system's file picker (Android, Windows, Linux with
zenity or kdialog installed, macOS, the Windows Store version; not iOS): the game checks that it is a mod and puts it into the
Mods folder as `<id>.zip`. A mod with the same id is replaced (the game asks first) and stays on if it was. **Delete** removes
the selected mod from the device after asking; the mods that need it are turned off.

The game keeps what it makes from a mod's textures (and its ships' hangar shadows) in a cache, so later starts load faster;
an edited texture is made again by itself. If a mod still looks wrong after you changed it, press **Rebuild cache**: every
mod cache is deleted and the mods load again.

## Sharing a mod as a zip

Zip the mod's folder and share the `.zip`. Players drop it into their Mods folder as it is; there's no need to unpack it. Both
layouts work:

```
plasma_arsenal.zip            plasma_arsenal.zip
├─ mod.json                   └─ plasma_arsenal/
├─ preview.png                   ├─ mod.json
└─ items.json                    ├─ preview.png
                                 └─ items.json
```

## mod.json

Every mod needs a `mod.json` at its top level:

```json
{
    "id": "plasma_arsenal",
    "name": "Plasma Arsenal",
    "version": "1.0",
    "author": "Your name",
    "description": "What the mod adds or changes.",
    "preview": "preview.png",
    "website": "https://example.com",
    "credits": "Ship model: Someone, CC BY 4.0",
    "dependencies": ["another_mod"]
}
```

| Field | |
|---|---|
| `id` | **Required.** Lowercase letters, digits, `_` and `-`, at most 40 characters. This is the mod's permanent name: saves and other mods refer to it, so never change it after release. |
| `name` | Shown in the Mods screen. Either a string, or one text per language: `{ "en": "Plasma Arsenal", "de": "Plasma-Arsenal" }`. |
| `version` | Shown in the Mods screen and the multiplayer server browser. |
| `author`, `website` | Shown in the Mods screen. |
| `description` | Shown in the Mods screen (a string, or one text per language). |
| `credits` | Shown in the Mods screen under the description: who made the models, textures or sounds the mod uses, and their licenses. |
| `tag` | A short tag shown smaller and half transparent after the names of everything the mod adds, so players tell content from different games or packs apart: `"GoF3"` shows "Veteran [GoF3]". Its new ships and items in the hangar, shop, cargo and Kaamo lists, their details, the item window and the status screen; its new systems and stations on the star map and in the docked station's title; its Most Wanted criminals on the board and in the pause menu. A string, or one text per language. One tag per mod: what it only changes (an `override`) keeps its name as it is. |
| `preview` | The image shown in the Mods screen, PNG or JPG. Default `preview.png`. 16:9 looks best, for example 640 × 360. |
| `dependencies` | Ids of mods that must be on too, optionally with the lowest version: `["ship_pack", "frontier_systems>=1.2"]`. They are loaded first, turning this mod on turns them on as well, and turning one of them off turns this mod off. See [Building on other mods](#building-on-other-mods). |
| `multiplayerIgnore` | Files multiplayer doesn't compare: what only changes how the mod looks, or what the game never reads. `"folder/"` means everything in that folder, `*` any characters: `["parts/", "maps/*.png", "icons/", "preview.png", "tools/"]`. With it a lighter phone build of the mod (smaller models and textures under the same names) plays with the PC build. The data always counts, whatever the list says: `mod.json`'s `id`, `version`, `dependencies` and this list, the `.json` files at the mod's root, and everything in `events/`. See [Multiplayer](#multiplayer). |

## items.json

A list of entries. Each entry either adds an item (`"id"`) or changes an existing one (`"override"`). Comments (`//`) are
allowed.

```json
[
    {
        "id": "plasma_lance",
        "base": 12,
        "name": { "en": "Plasma Lance", "de": "Plasmalanze" },
        "description": "A Terran rebuild of the Vossk blaster.",
        "techLevel": 3,
        "occurrence": 40,
        "price": [24000, 26500],
        "vosskOnly": false,
        "alwaysSoldAt": 78,
        "stats": { "damage": 14, "reloadMs": 700, "lifetimeMs": 1800, "speed": 16 }
    },
    {
        "override": 50,
        "stats": { "shieldCapacity": 65 },
        "maxPrice": 4600
    }
]
```

### New items

- `id`: the item's id within your mod (same rules as the mod id). The game knows it as `<mod id>:<id>`, here
  `plasma_arsenal:plasma_lance`.
- `base`: **required.** The item it's made from, either an original item's number (see `Assets/Resources/GoF2Data/items.json`,
  the `index` field) or `"mod_id:item_id"` for an item from another mod (put that mod in `dependencies`) or one listed earlier
  in your own file.

A new item is a copy of its base. It keeps the base's type and category (a blaster stays a blaster, a missile a missile) and
looks, sounds and works like it: its icon, projectile, impact, shot sound, turret model, a booster's boost sound, a tractor
beam's beam and any special behaviour (beam lasers, cluster missiles, cloaks...). Everything you give in the entry replaces the base's value. Blueprint recipes aren't copied.

### Changing items

`override`: the item to change, an original item's number or `"mod_id:item_id"` of another mod's item (that mod must load
earlier). Only the fields you give change. Giving `name` or `description` renames the item.

### Fields

| Field | |
|---|---|
| `name`, `description` | A string, or one text per language (see Texts below). |
| `techLevel` | 1 to 10 like the originals, or up to 20 for gear above them (see [Tech levels above 10](#tech-levels-above-10)). Shops sell only items up to their station's tech level. |
| `occurrence` | How often shops stock it (the originals use 0 to 90; 0 = never in a random shop). |
| `stock` | How many units a shop stocks of it: `50`, or `[10, 25]` for a random amount between the two (1 to 9999). Without it the amount follows the item's kind, see [How many units a shop stocks](#how-many-units-a-shop-stocks). |
| `minPrice`, `maxPrice` | The price range. A station's price lies between them, by distance to the cheapest and dearest systems. |
| `price` | Shorthand: `1500` (both) or `[1200, 1500]`. |
| `lowestPriceSystem`, `highestPriceSystem` | System numbers where it's cheapest and dearest. |
| `vosskOnly` | `true`: only Vossk systems sell it. |
| `alwaysSoldAt` | A station number that always stocks it, whatever its tech level (78 = Var Hastra, the starting station; -1 = none). |
| `tractorColor` | A tractor beam's colour, `"#rrggbb"` or `[r, g, b]` (0-255): its beams are drawn in it (see [Multi-grab tractor beams](#multi-grab-tractor-beams)). |
| `stats` | The item's numbers, see below. |
| `attributes` | The original game's attribute numbers directly: `{ "9": 14 }`. For anything `stats` has no name for. |
| `defaultEconomy` | The same fields again, used when the player picked the Default Economy (the PC / Mac prices) for their game. |
| `fx` | A weapon's own look and sound; or a weapon effect's id from `weaponfx.json`, or a game weapon's number for its whole look. See [Weapon effects](#weapon-effects). |
| `icon` | Its own shop icon: a PNG in the mod, 180 x 88 like the originals (frame and background included; copy one from `Assets/Resources/GoF2Icons` to start from), or one of the game's: `"gof2:item_046"`. Without it the item shows its base item's icon. An `override` with `icon` gives an original item a new one. |
| `available` | A [condition](#conditions) that must hold before shops stock it or it turns up in loot (an `override` can hold back an original item too). |

### How many units a shop stocks

Without `stock`, how many units a station holds follows the item's kind, exactly as in the original game:

| Kind | Units |
|---|---|
| Goods (commodities) | 5-19, multiplied by the distance to `lowestPriceSystem` (up to x20, x2 on Extreme): scarce near its home system, piled high far away |
| Secondary weapons (missiles, mines, bombs) | 5-19 |
| Primary weapons, turrets, equipment | 1-3 |

`stock` replaces that with your own number: `"stock": 50` for exactly 50 units, `"stock": [10, 25]` for a random amount
between the two (both included). A row with `stock` is also left out of the 0-2 units the game shaves off the other rows
when the player re-docks, so an exact amount stays exact.

`occurrence` still decides **whether** the shop stocks it at all, and `techLevel` and the station's own tech level whether
it may turn up there. It works on an `override` too, so `{ "override": 122, "stock": [60, 90] }` fills every shop's
energy cells.

### Stats

| Name | Attribute | Meaning |
|---|---|---|
| `damage` | 9 | Hull damage per hit |
| `empDamage` | 10 | EMP damage per hit |
| `reloadMs` | 11 | Time between shots (ms) |
| `lifetimeMs` | 12 | How long a shot flies (ms); with `speed` this gives the range |
| `speed` | 13 | Projectile speed (game units per ms) |
| `blastRadius` | 14 | Explosion radius of bombs, mines and scatter guns |
| `guided` | 15 | 1 = steerable rocket (like the Liberator) |
| `automatic` | 16 | 1 = automatic turret |
| `turretTurnSpeed` | 17 | Turret turning speed |
| `shieldCapacity` | 18 | Shield points |
| `shieldRegenMs` | 19 | Time to recharge a full shield (ms) |
| `armor` | 20 | Armour points |
| `cargoBonus` | 22 | Extra cargo space (t) |
| `tractorAuto` | 23 | Tractor beam auto mode (1 nearest crate, 2 any crate) |
| `tractorLockMs` | 24 | Tractor beam lock time (ms) |
| `boostSpeed` | 25 | Booster speed |
| `boostRechargeMs` | 26 | Booster recharge (ms) |
| `boostDurationMs` | 27 | Booster duration (ms) |
| `agility` | 28 | Steering nozzle handling bonus |
| `lockTimeMs` | 29 | Scanner lock time (ms) |
| `showClassAAsteroids` | 30 | 1 = marks class-A asteroids (Ultrascan) |
| `radarShowsCargo` | 31 | 1 = shows a locked ship's cargo |
| `drillSpeed` | 32 | Mining laser speed |
| `miningYield` | 33 | Mining laser yield |
| `cabins` | 34 | Passenger cabins |
| `cloakDurationMs` | 35 | How long the cloak lasts (ms) |
| `cloakChargeMs` | 36 | Cloak charge time (ms) |
| `energyCells` | 38 | Energy cells used per cloak |
| `fireRateFactor` | 39 | Weapon mod: fire rate bonus (%) |
| `damageFactor` | 40 | Weapon mod: damage bonus (%) |
| `emergencyMs` | 41 | Emergency system invulnerability (ms) |
| `timeExtenderMs` | 42 | Time extender duration (ms) |
| `timeExtenderCooldownMs` | 43 | Time extender cooldown (ms) |
| `collectorSpeed`, `collectorMagnitude`, `collectorRange` | 49 to 51 | Plasma collector |
| `gammaShielding` | 52 | Gamma shield strength (%) |
| `beamRange`, `beamStrength`, `beamTargets` | 53 to 55 | Repair and transfusion beams |
| `miningBeam` | 100 | 1 = a drill that works as a mining beam (see [Mining beams](#mining-beams)) |
| `miningBeamRange` | 101 | Mining beam reach from the asteroid's surface (game units, 20 per metre; default 24000) |
| `miningBeamLayerMs` | 102 | Mining beam: time to cut one rock layer (ms; default 6000, the drill minigame's) |
| `miningBeamLook` | 103 | Mining beam: the beam laser whose beam and impact it shows (9, 10, 11 or 228; default 228) |
| `phaseCloak` | 104 | 1 = a cloak that also phases the ship through objects, shots and explosions while cloaked (see [Phase cloaks](#phase-cloaks)) |
| `missiles` | 105 | 1 = a gun or turret whose shots are homing missiles (see [Missile turrets and salvos](#missile-turrets-and-salvos)) |
| `salvoShots` | 106 | Shots fired side by side per trigger (default 1) |
| `salvoCount` | 107 | Salvos per magazine: after the last one comes `reloadMs` (default 1) |
| `salvoGapMs` | 108 | Time between the salvos of one magazine (ms) |
| `turretRange` | 109 | How far an automatic turret looks for a target (game units, 20 per metre; default 60000) |
| `flightScope` | 110 | Which flight style the item's booster / Steering Nozzle stats count in: 0 both (default), 1 the original only, 2 free flight only (see [Flight styles](#flight-styles)) |
| `topSpeed`, `turnRate`, `strafeSpeed` | 111 to 113 | Flight bonuses in both styles, in % (20 = +20 %, -10 = 10 % less) |
| `originalTopSpeed`, `originalTurnRate`, `originalStrafeSpeed` | 114 to 116 | The same, in the original flight only |
| `freeTopSpeed`, `freeTurnRate`, `freeStrafeSpeed`, `freeAcceleration` | 117 to 120 | The same, in free flight only (acceleration: how fast the ship reaches its speed) |
| `freeBoostFactor` | 121 | Free flight's boost top speed in hundredths of the top speed (350 = x3.5) instead of the booster's own |
| `tractorTargets` | 122 | Tractor beams: how many containers it pulls at once, 1 to 8 (default 1; see [Multi-grab tractor beams](#multi-grab-tractor-beams)) |
| `tractorRange` | 123 | Multi-grab tractor beams: how far the extra beams reach for containers (game units, 20 per metre; default 30000 = 1.5 km) |
| `tractorNoLock` | 137 | Tractor beams: 1 = the scanner never locks a container; every beam takes containers by itself (see [Multi-grab tractor beams](#multi-grab-tractor-beams)) |
| `shieldDamage` | 124 | A weapon's damage to a shield in percent (default 100; the Disruptor Laser and items based on it 200). What the shield doesn't absorb hits the armor and hull at the plain damage. Only ships with a shield feel it: the players (each other in multiplayer), not the game's NPCs |
| `echoDelayMs` | 127 | A second, weaker blast at each hit, this many ms after it (0 or none = no echo; see [Echo blasts](#echo-blasts)) |
| `echoPercent` | 128 | The echo's damage, EMP and size in percent of the shot's (default 50) |
| `echoRadius` | 129 | The echo's reach in game units (0 = the gun's own `blastRadius`) |
| `echoExplosion` | 130 | The explosion the echo shows: 0 ship, 8 to 10 scatter (8 the default), 11 shock blast, 13 fireworks; -1 none |
| `missileDefense` | 131 | Missile defense: with this item mounted, the ship's automatic turrets shoot down the homing missiles flying at it within this reach (game units, 20 per metre), before turning to enemy ships. Any item can carry it; on a seatless cabin (`"base": 91`, `"cabinSize": 0`) it changes nothing else and the shop calls it "Missile defense". Another player's missiles in multiplayer are left alone |
| `cruiseSpeed` | 133 | A cruise drive's speed factor in hundredths (400 = x4; > 0 makes an item based on the Khador Drive a cruise drive, see [Cruise drives](#cruise-drives)) |
| `interstellar` | 135 | 1: the mounted cruise drive also flies the space between the systems ([Cruise drives](#cruise-drives)) |
| `cruiseRange` | 134 | Cruise drive: the distance one leg of energy cells buys (game units, 20 per metre; default 60000 = 3 km) |
| `cruiseChargeMs` | 37 | Cruise drive: the charge before it engages (ms; the Khador Drive's own charge attribute) |
| `miningTime` | 132 | An ore's mining time in percent of the game's 6 seconds per layer (default 100; 200 = twice as long). The ore a whole asteroid gives stays the same: it comes slower. Counts for the drill's minigame and the mining beam |
| `fullStopDamage` | 138 | EVERSPACE 2's full-stop bonus: the primary guns' damage in percent more while the ship stands still (any mounted item, both flight styles; adds up with a ship's `flight` field of the same name) |
| `lowSpeedResistance` | 139 | EVERSPACE 2's low-speed resistance (the Gunship's): damage taken in percent less once the ship has flown slower than 30 m/s for a second, gone at once above it (any mounted item, both flight styles; adds up with a ship's `flight` field) |
| `cloakCooldownMs` | 160 | A cloak's own cooldown in ms, in place of the difficulty's 5 to 12 seconds |
| `shieldBypass` | 161 | 1: the weapon's hits go past the shield, straight to the armor and hull (the shield untouched) |
| `slowMs`, `slowPercent` | 162, 163 | A hit slows the ship to `slowPercent` of its speed (default 50) for `slowMs`; it can still turn. The game's NPCs and other games' NPCs in multiplayer; not players |
| `slowFreeze` | 167 | 1: the slow also stops the ship turning (EVERSPACE 2's frozen: controls disabled); with `slowPercent` 0 the ship hangs still |
| `dotDamage`, `dotMs` | 168, 169 | A hit adds `dotDamage` every 0.95 s for `dotMs`, past the shield, counted as yours; hits stack (EVERSPACE 2's corrosion and infestation). NPC ships only |
| `vulnerablePercent`, `vulnerableMs` | 170, 171 | A hit makes the ship take `vulnerablePercent` % more from every hit for `vulnerableMs`; hits stack, up to +100 % (EVERSPACE 2's destabilized). NPC ships only |
| `device` | 150 | An active device's type (see [Devices](#devices)): 1 EMP wave, 2 EMP shot, 3 shield disruptor, 4 overcharger area, 5 overcharger beam, 6 strike charge, 7 deflector |
| `deviceCooldownMs` | 151 | Device: its cooldown after a run (default 60000) |
| `deviceDurationMs` | 152 | Device: how long it runs: the slow on its targets, the strike charge's boost, the deflector (0 = at once) |
| `deviceRadius` | 153 | Device: its reach in game units (20 per metre): the EMP wave's, the EMP shot's burst, the overchargers' |
| `deviceGrowMs` | 154 | Device: the EMP wave's time to reach its full radius |
| `deviceSlowPercent` | 155 | Device: the speed an EMP device leaves its targets, in percent (default 25) |
| `deviceShieldCost` | 156 | Device: the overchargers' cost in percent of the ship's maximum shield |
| `deviceDamagePercent` | 157 | Device: the overchargers' damage per target in percent of the shield spent; the strike charge's damage factor (175 = x1.75); the deflector's turned-back shots' damage; the shield disruptor's damage on a ship without a shield, in percent of its `damage` (default 100) |
| `deviceCharge` | 158 | Device: the strike charge's charge, the damage the primary guns must deal before it can fire |
| `deviceConeDeg` | 159 | Device: the overcharger beam's half-angle ahead (default 30) |

Which stats an item uses depends on its category, so copy the stats its base item has (see `items.json` and
`item_attributes.json` in `Assets/Resources/GoF2Data`). items.json's own stat names (`loadingTimeMs`, `range`,
`projectileSpeed`...) work too.

### Tech levels above 10

The original game's items and stations both stop at tech level 10, so an item can go higher (`"techLevel": 11`, up to 20;
past that it is cut back to 20 with a warning) for gear better than anything in the game, like a community "Tech Level
11" add-on. Where it turns up:

- **Shops**: every station of tech level 10 (16 of them: Anesa, Posého, Binon, Thynome, F'err, Carme, P'arrenkk, Rr'ostam,
  G'ukkion, K'mirkk, B'akrram, Genoh, Kothar, and the special stations Var Destro, Sao Perula and Quineros, which keep their
  own rules: Var Destro weapons, Quineros equipment) may stock it, never a station below. Tech levels 11 and 20 are the same
  there: `occurrence` alone says how often (40 = about every other visit at a mid-story stop, 100 = almost every time), so
  give the top items a low one. `alwaysSoldAt` still puts it in one station's shop whatever the tech level.
- **Loot**: a tier 5 ("Extremely dangerous") pirate outpost's two pieces of equipment are each one time in three an item
  above tech level 10 (picked by `occurrence`) while one is available; an enhanced capital ship's gear (two pieces of tech
  level 5+) may be one; ordinary pirates and freighters never drop weapons or equipment from tech level 8, as in the original.
- **Everywhere else** it is an item like any other: lounge sellers may offer it, the item window shows "Tech level 11".

Pair it with the other tools: `available` (e.g. `"won(main)"`, only after the story) holds it back from shops, loot and
sellers until then, a high `price`, and a [blueprint](#blueprints) with `"autocomplete": false` when it should be built
rather than bought. A ships.json `dealer` with `minTechLevel` above 10 likewise means the tech level 10 stations' dealers
(ships have no tech level of their own).

```json
{ "id": "vanadis", "base": 59, "name": "Vanadis Plating", "techLevel": 11, "occurrence": 8,
  "price": [420000, 460000], "available": "won(main)", "stats": { "armor": 260 } }
```

### Mining beams

A drill (an item based on one of the drills, 86 to 90) with `"miningBeam": 1` is a mining beam: the player locks on to an
asteroid with the scanner as usual, but there is no autopilot approach, landing or minigame. Holding fire on the locked
asteroid (the guns stay silent while the beam cuts, or could: the nose on the rock, in reach, room in the hold) cuts its ore while the ship keeps flying: beams from the ship's outer
gun mounts, sparks on the rock, and every ton flying into the ship as a chunk of the asteroid before it lands in the hold.
The lock plate shows how much of the rock is cut. Letting go keeps the progress; a depleted asteroid gives its core (class A)
and explodes. Without the item mounted, mining works as in the original.

The ore follows the drill minigame's rules: the rock's layers (class A 7 ... D 4) are cut one by one, and a whole asteroid
gives what a perfect minigame run would with that yield: class A 62.7 t x `miningYield` / 100 (plus its core), class D
23.7 t x `miningYield` / 100. `miningBeamLayerMs` only sets how fast. Gunant's Drill, the best original drill, has a yield
of 100 and needs the landing and 6 s per layer.

```json
{
    "id": "extract_beam",
    "base": 90,
    "name": "IMT Extract Beam 5.0",
    "techLevel": 10,
    "price": [480000, 520000],
    "stats": { "miningBeam": 1, "miningYield": 160, "miningBeamLayerMs": 4000, "miningBeamRange": 24000, "miningBeamLook": 228 }
}
```

### Phase cloaks

A cloak (an item based on one of the cloaks, 94 to 96) with `"phaseCloak": 1` works like any cloak (the energy cells, the
charge, the duration, the cooldown, the look) and while it is cloaked, its fades included, the ship is phased out: it flies
straight through asteroids, stations, jumpgates, freighters and other players' ships without touching them, and shots,
beams and explosions pass through it without doing any damage (in multiplayer too). The wormhole still pulls. When the cloak ends inside something, the ship collides as usual: pushed out of a station's or a freighter's
volume, or the asteroid it sits in destroyed for 20 damage.

```json
{
    "id": "pegaphase",
    "base": 96,
    "name": "PegaPhase",
    "techLevel": 10,
    "price": [540000, 580000],
    "stats": { "cloakDurationMs": 80000, "cloakChargeMs": 7000, "energyCells": 5, "phaseCloak": 1 }
}
```

### Devices

An active device is equipment fired from the quick menu (its name, half-transparent while it isn't ready) or the keys 4, 5
and 6 (the first three mounted, rebindable as Device 1-3), then cooling down for `deviceCooldownMs` (the HUD's row of
recharge icons shows it with keys and a controller; "<item> ready" when it is). Make one from the seatless cabin
(`"base": 91`, `"cabinSize": 0`) with `"device"` set to its type; the shop calls it "Device". A ship carries one of each
type. The seven types are Galaxy on Fire 3's (the GoF3 Ships mod has them all):

| `device` | What it does | Its stats |
| --- | --- | --- |
| 1 EMP wave | A wave from the ship out to `deviceRadius` over `deviceGrowMs`; each hostile ship it reaches flies at `deviceSlowPercent` of its speed and can't turn for `deviceDurationMs` (sparks show it); `empDamage` adds an EMP hit | radius, grow, slow, duration |
| 2 EMP shot | A homing shot at the locked ship (`projectileSpeed`, `lifetimeMs`); where it hits, every hostile ship within `deviceRadius` is slowed like the wave | radius, slow, duration |
| 3 Shield disruptor | A homing shot whose hit takes `damage` off the target's shield; a ship without a shield (the game's NPCs) takes `deviceDamagePercent` of it as damage | damage, damage percent |
| 4 Overcharger area | Spends `deviceShieldCost` % of the ship's maximum shield (refused with less shield than that) and deals `deviceDamagePercent` % of what it spent to every hostile ship within `deviceRadius` | radius, shield cost, damage percent |
| 5 Overcharger beam | The same cost, as beams at every hostile ship within `deviceRadius` and `deviceConeDeg` of the nose | radius, cone, shield cost, damage percent |
| 6 Strike charge | Charged by `deviceCharge` points of damage the primary guns deal (its icon fills as it charges), then `deviceDurationMs` of their damage x `deviceDamagePercent` / 100 | charge, duration, damage percent |
| 7 Deflector | For `deviceDurationMs` the enemy shots that would hit the ship fly back at their shooter, homing, at `deviceDamagePercent` % of their damage; beams are stopped | duration, damage percent |
| 8 Sensor cloud | A cloud of `deviceRadius` around the ship for `deviceDurationMs`, grown over `deviceGrowMs`: while the ship flies inside it the enemies lose it, as under a cloak (they hold their fire, missiles lose their lock), like EVERSPACE 2's Smoke Bomb | radius, grow, duration |
| 9 Hull repair | Repairs `deviceDamagePercent` % of the hull's maximum at once (EVERSPACE 2's Nano Injector), or with a `deviceDurationMs` over that time (its Nanobots); refused with nothing to repair | damage percent, duration |
| 10 Armour repair | Repairs `deviceDamagePercent` % of the armour's maximum at once (EVERSPACE 2's Armor Repair, 100 = to full); refused with nothing to repair | damage percent |
| 11 Restock | `deviceAmmo` rounds more for the selected secondary weapon (EVERSPACE 2's Secondaries Restock); refused with none selected | ammo |
| 12 Device charger | Every other device's cooldown ends at once (EVERSPACE 2's Device Charger); refused while none is cooling down | |
| 13 Corrosion | The locked ship (within `deviceRadius`) takes `damage` every 0.95 s for `deviceDurationMs`, past its shield (EVERSPACE 2's Corrosion Injector) | radius, duration, damage |
| 14 Teleport | A blink `deviceRadius` ahead, stopped 10 m short of a hull, a station or an asteroid in the way (EVERSPACE 2's Teleporter); refused with no room | radius |
| 15 Spectral shift | The same blink, and every hostile ship within `deviceRange2` of the path takes `damage` (EVERSPACE 2's Spectral Shift) | radius, range 2, damage |
| 16 Fusion hook | The hostile ship in the sights (within `deviceRadius`, 18 degrees of the nose) is grappled for up to `deviceDurationMs`: after 0.3 s you are pulled toward it (up to 800 m/s) and it toward you, until you are within 200 m, it leaves the hook's cone, or it dies (EVERSPACE 2's Fusion Hook) | radius, duration |
| 17 Quantum tether | For `deviceDurationMs` the damage the locked ship takes is dealt to every hostile ship within `deviceRadius` of it too; when it dies the tether jumps to one of them (EVERSPACE 2's Quantum Tether) | radius, duration |
| 18 Entangler | For `deviceDurationMs` the damage your ship takes is summed and, at the end, dealt to the locked ship (within `deviceRadius`) x `deviceDamagePercent` / 100, nothing if it died first (EVERSPACE 2's Quantum Entangler) | radius, duration, damage percent |
| 19 Repair kit | One of your overheated items (a rift's Overheated Systems) works again at once (EVERSPACE 2's Nano Repair Kit); refused with none overheated | |
| 20 Recovery | Lifts the debuffs on your ship: a web's slow, a rift's corrosion (EVERSPACE 2's System Recovery Routine); refused with none on | |
| 21 Virus | Infects the locked ship (within `deviceRadius`); every `deviceGrowMs` (default 1 s) it spreads to the nearest hostile ship within `deviceRadius` of an infected one, up to 6; after `deviceDurationMs` every infected ship takes `deviceDamagePercent` % of a pirate raider's hull (EVERSPACE 2's Annihilator Virus) | radius, grow, duration, damage percent |

A **consumable** device (`"deviceConsumable": 1`, EVERSPACE 2's consumables) is used up one unit per use and has no
cooldown unless given one: the hangar stacks bought units like a secondary's ammo (mounting moves the whole stack,
more bought join it), the HUD says "<item>: N left" after each use, and the last use takes the item off the ship.

The item's `"fx"` gives its sound (its shot sound plays when it fires) and the EMP and disruptor shots' look; without
one they fly as the Dia EMP's shots. Kills and standing count as the player's. In multiplayer the slow reaches another
game's NPCs; the shots and the beams are seen only by their pilot.

```json
{
    "id": "area_emp",
    "base": 91,
    "name": "Area EMP",
    "techLevel": 6,
    "occurrence": 25,
    "price": [64000, 67200],
    "stats": { "cabinSize": 0, "device": 1, "deviceCooldownMs": 60000, "deviceDurationMs": 8000, "deviceRadius": 9120,
               "deviceGrowMs": 2500, "deviceSlowPercent": 25 }
}
```

### Cruise drives

A cruise drive is an item based on the Khador Drive (85) with `"cruiseSpeed"` above 0: in-system fast travel for a pilot
flying by hand. A ship carries one drive: mounting a cruise drive over a Khador Drive or another cruise drive asks to
replace it, as a new shield replaces the old one (the replaced drive goes to the hold). Hold the fast-forward button
(the touch button, Tab, controller Y) while nothing else flies the ship: the drive charges for `cruiseChargeMs`, pays
`energyCells` energy cells (twice as many on Extreme, like a Khador jump) and the ship flies `cruiseSpeed` / 100 times
its forward speed, looking and sounding like a boost; every `cruiseRange` units it pays another leg of cells, and without
the cells for the next leg it stops. Releasing the button, hostile ships, the autopilot, the time extender, a menu, a hit
on the ship, a station, freighter, object or asteroid within 400 m ahead (the speed already easing down from 2 km) or the locked
landmark within 1 km (the fast-forward's own limit) drop it; a new press charges again. The HUD shows "<item> charging"
on the Khador Drive's charge bar, "-N t Energy Cells" when a leg is paid and why it stopped.

```json
{
    "id": "slipstream",
    "base": 85,
    "name": "Slipstream",
    "techLevel": 7,
    "occurrence": 25,
    "price": [148000, 162800],
    "stats": { "cruiseSpeed": 400, "cruiseRange": 60000, "cruiseChargeMs": 2000, "energyCells": 1 }
}
```

**Between the systems.** With `"interstellar": 1` as well, the mounted drive also flies the space between the systems.
There is no option for it: mounting the item is the switch, and in the hold it does nothing. It adds an entry with the
item's name to the quick menu (the Khador Drive's key reaches it on a ship without a Khador Drive), refused where a Khador
jump is (a mission, volatile goods, the Void). The Khador Drive's charge and scene play once, and instead of a destination the
ship comes out in an empty space with every system of the star map as a sun at its place on the map. Only what the map
shows is there: a system appears once it is revealed (the story, bought coordinates, an event, its `available`) and is
gone while it is hidden. Fly by hand on the fast-forward button, or lock a system or pick it from the autopilot menu (the
programmed destination's system, then the seven nearest): the autopilot holds the drive itself. Within 8 km of a system the
ship flies straight on into its gate orbit, the warp and its speed easing out over 7 seconds as the orbit grows ahead. A leg here is the average gate hop, 30 km, whatever `cruiseRange`
says, so the crossing costs what the gate route's Khador jump would: `energyCells` a hop (twice on Extreme). What a leg paid
for survives a stop. The HUD says when the hold pays its last leg; out of cells, the ship falls out at the nearest system. No traffic, encounters or story happen here,
and a save or a load is the orbit the ship left. `cruiseSpeed` is the time a crossing takes: x8 flies an average hop in 38
seconds. The item is no jump drive: it doesn't replace the Khador Drive for the story's checks.

The game's own cruise drive is the Khador Slipstream (`gof2:khador_slipstream`); a mod can add more with these stats.

`warp.json` (optional, the last mod in the load order that has one wins) gives that space its look and sound; each entry is
optional. Without one the game uses its own band images and the warp is silent:

```json
{ "tunnel": "warp/tunnel.png", "detail": "warp/detail.png", "loop": "warp/loop.ogg", "enter": "warp/enter.ogg" }
```

The warp stretches the sky itself, so its colours are always the sky's. It runs all the time between the
systems and with any cruise drive in an orbit, fading in and out with the drive; between the systems it also carries the
Khador Drive's blue vein web and ice blue. The two images tile and only shape it, of which the red channel is read: `tunnel` and `detail` shade it into bands. `loop` plays with its strength and `enter` as the drive engages. A
system's own sky blends in over the last 20 km, so the arrival matches.

**Encounters.** A cruise runs into things: after the first 6 seconds of a cruise, every 5 seconds there is a 10 % chance
of an encounter (90 seconds apart at least, two per orbit visit at most), picked by weight from a table. The game's own
table: `pirates` (weight 30, an ambush: raiders appear ahead and the drive drops), `trader` (20, a signal: a freighter
with escorts), `derelict` (20, a signal: drifting crates), `distress` (20, from rank 2, a signal: a local ship under
attack), `flyby` (10, a wing passing through; the drive keeps running). A signal is a waypoint ahead and to one side:
fly or autopilot to it and it appears, cruise on and it fades after 3 minutes or 10 km. None come in the special orbits,
the Void and supernova systems, Loma, a scripted orbit (a story or freelance mission, the Kaamo siege, the Supernova
Challenge) or, in multiplayer, an orbit another player runs. Between the systems (a Khador Slipstream's crossing, single
player) the same table rolls, and every encounter pulls the ship out of the slipstream: right there in deep space (the warp
eases out, then it appears ahead) or at the nearest system (the ship falls out there and meets it on arrival); none comes
while a system's orbit is already in sight. `encounters.json` adds to the table and changes it:

```json
[
    { "id": "ambush", "kind": "pirates", "weight": 20, "minRank": 4, "security": [0, 1], "ships": 4, "name": "Raider pack" },
    { "id": "beacon", "kind": "quest", "quest": "old_beacon", "weight": 5 },
    { "override": "flyby", "weight": 0 },
    { "override": "trader", "visible": false }
]
```

| Field | Meaning |
|---|---|
| `id` | A new entry (its key is `mod:id`); or `override`: one of the game's (`pirates`, `trader`, `derelict`, `distress`, `flyby`) or another mod's `mod:id` to change. |
| `kind` | `pirates`, `trader`, `derelict`, `distress`, `flyby`, or `quest` (starts the quest graph `quest` from the mod's `events/`, single player; the graph spawns, talks and sets waypoints itself). Seven more, after EVERSPACE 2's encounters, only in a mod's entries (the game's own table doesn't have them): `battle` (a wing of the system's race fighting raiders; you are paid as EVERSPACE 2 pays an encounter when the raiders are gone: 800 credits plus up to 4200 more by the station's tech level), `trap` (passes for an unknown signal: raiders appear around you on arrival, sometimes a second group ahead), `stash` (one crate with three loot rolls), `depot` (crates of ore with a few raiders guarding them), `highrisk` (three waves of raiders, each when the last is gone, then a crate with 8 to 14 loot rolls (EVERSPACE 2's for a high-risk area's boss) and a battle's pay; flying off ends it), `lootdrone` (a neutral loot drone: one of the mods' ships with the traffic `"role": "lootDrone"`, else a pirate fighter flying as one; shooting it is no crime, and its kill counts as a pirate's), `rift` (an EVERSPACE 2 rift run where the signal was: four stages, the last a boss. A fight stage ends at its kill points (15, 21, 26, 30, 35 ... by stage, ES2's), with raiders kept coming; each kill refills the Resolve shown on the HUD, which drains after 10 s, and a stage won with Resolve left is mastered, which adds to the boss's crate. From the second stage on each brings one of EVERSPACE 2's rift mutators (Enemy Regen, Evasive, No EMP, Jarred Sensors, Hidden Health, Explosive Death, Repair On Kill, Strength in Death, Nearsighted, Farsighted, No Lock, Vengeance, Mines on Death, Overheated Systems (every 10 s your guns, missiles, shield or booster run at half for 10 s), Corrosive Death, Burst Bulbs (shoot the growing bulb or it bursts over the whole stage)), the boss's stage one of the boss's (Quantum Mend, Evasive Drift, Guardians, Phase Barrier (shots from outside its shell don't hurt it: fly in), Stasis Snares (web bubbles around you), Apocalypse (sit out its blast in a safe field)). Each stage is fought by one side at random: the pirates, the Void, or a hostile [race](#races) of an active mod that has fighters (they fly its mods' ships; the pirates and the Void the game's, now and then a mod's of theirs at the "Mod ships in traffic" chance). `"bosses": [ { "ship": "mod:id", "raiders": 8, "race": "mod:id", "weapon": "mod:gun", "secondary": "mod:missiles" } ]` names the ships its boss may fly, one at random (a capital ship parks as one and fires its own turrets), its hull that many pirate raiders' hulls, the race it flies for (a race word or a mod race; its stage is then that race's, its guards too; default the stage's side), and a fighter boss's gun and missiles (items; default the pirate boss's gun, no missiles); without them the stage's side's boss. Flying 15 km off ends it. The rift is the remake's own entry, with its own chance (weight 4, a signal, from rank 5, four stages): a mod's `rift` entry only adds its `bosses` to one shared pool (its other fields are ignored, with a warning, and the rift can't be overridden), and the rift comes once any mod adds one. More mods with rifts mean more bosses to meet, not more rifts). |
| `weight` | Its share of the roll (0 = never; the game's add up to 100). |
| `visible` | `true`: a signal the pilot may fly to; `false`: it appears at once (pirates, distress and flyby ambush, trader and derelict signal by default). |
| `minRank`, `security`, `freePlayOnly` | Only from that rank, only in systems of those security levels (0 Dangerous, 1 Risky, 2 Average, 3 Secure), only in free play. |
| `ships` | How many: pirates, the trader's escorts, the derelict's crates, the distress call's raiders, the flyby's wing, the battle's raiders, the trap's raiders, the depot's crates, each high-risk wave. |
| `name` | The signal's name on the HUD (a string or per language, like an item's). |
| `interstellar` | Where it pulls the ship out of the Khador Slipstream between the systems: `space` (met right there in deep space), `system` (at the nearest system, met on arrival), `either` (the default: one or the other) or `never`. The game's: pirates, derelict and flyby `space`, trader and distress `either`; a `quest` always goes to the system. |

### Jump drives

An item based on the Khador Drive (85) without `cruiseSpeed` is a jump drive: it opens the star map from the quick menu
(its own name on the entry), charges and jumps like the Khador Drive. A ship carrying the Khador Drive itself (or one with
the drive built in) jumps with that instead. `"jumpFx"` gives the drive its own effect and rules (every field optional but
`data`):

```json
{
    "id": "hyperdrive",
    "base": 85,
    "name": "Hyper Drive",
    "jumpFx": {
        "data": "fx/hyperdrive_3d.json", "texture": "fx/hyper_drive.png", "scale": 0.5, "glow": 2.5,
        "meetMs": 2300, "meetDepth": 8200, "sound": "gof2:timeJump", "endHoldMs": 2000, "arrivalFx": true,
        "startDelayMs": 2000, "chargeVoice": "RADIO_0_12", "cellsFactor": 2, "noVoid": true,
        "failChance": 0.03, "failMax": 0.5, "failNoRoute": 0.25, "failTexture": "fx/hyper_drive_purple.png",
        "failHandling": 0.1, "failMs": 16500, "failRumbleFromMs": 5500, "failRumble": 0.25
    }
}
```

| Field | Meaning |
|---|---|
| `data` | The effect's parts: a JSON file of meshes with their keyframes (`objects`: `name`, `verts`, `faces`, `uvs` per face corner, `keys` `posX`..`sclZ` and `extra` (opacity 0..1) as `[[ms, value], ...]`, `base` `{ parent_loc, parent_scale, parent_rotY }`, `billboard`), in Blender's frame (Z up, the viewer on +Y), game units. The ship flies into it along -Y. |
| `texture` | The effect's image (additive). |
| `scale`, `glow` | The effect's size (x the data) and brightness (HDR, for bloom). |
| `meetMs`, `meetDepth` | The ship reaches the point (0, -`meetDepth`, 0) of the effect at `meetMs` after it starts, and vanishes there. |
| `sound` | `gof2:<name>`, a sound of the game's story assets (`timeJump`: the prologue's time jump) in place of the Khador jump's. |
| `endHoldMs` | The scene holds this long after the effect (the camera drifting on) before the next area loads. |
| `arrivalFx` | `true`: the ship comes into the next orbit with the prologue's 2D hyper drive effect in this drive's texture (or `failTexture`), hidden and held still for 2.5 s; the arrival camera starts once it has played. `texture` must then have the layout of the game's `hyper_drive.png`. |
| `startDelayMs` | A jump asked for waits this long before it charges (the Khador Drive's own is 2000; not on top of an orbit's first 5 seconds). |
| `chargeVoice` | A story voice line (by its clip name) as the charge starts; `RADIO_0_12` is the prologue's "Activating hyperdrive." |
| `cellsFactor` | x the Khador Drive's energy cells (Extreme's x2 included). `freeJumps: true`: none at all. |
| `noVoid` | `true`: no jump into the Void, and none out of it (the story's own jumps still go). |
| `failChance` | The chance a jump fails, per gate jump of the route (0.03 = 3 % to a neighbour, 6 % two systems away...; x2 on Extreme), at most `failMax`. |
| `failNoRoute` | The chance from or to a system without jumpgate routes. |
| `failMs`, `failHandling`, `failRumbleFromMs`, `failRumble` | A failing jump: when the charge ends, the prologue's hyperdrive failure (its explosion, rumble and broken engine) for `failMs`, the ship flying at `failHandling` of its speed and turning, the camera shaking from `failRumbleFromMs`, growing over 4 s to `failRumble` (1 = the prologue's, a full boost ~0.16). |
| `failTexture` | The effect's image for a failed jump. |
| `failVoice`, `failVoiceChance`, `failVoiceMs` | A story voice line (like `chargeVoice`) `failVoiceMs` into a failure, at `failVoiceChance` (0 to 1, default 1) per failure: `"failVoice": "RADIO_0_15", "failVoiceChance": 0.1` is the prologue pilot's "Hey, what's happening?" one failure in ten. |
| `failLine`, `failSpeaker` | With `failVoice`: the line also comes in the radio box, its text (a text number) with a story speaker's face and name (default 0, Keith): `"failLine": 1689`. |
| `failRadiusKm` | A failed jump lands at a random station of a system on the map, the ship coming out anywhere within this distance of it (default 20 km, as the HUD counts), clear of the station, the gate and the asteroids. |

While the drive's effect hides the ship (leaving, from the moment it vanishes; arriving, until it shows) enemy shots fly
through it and nothing targets it. The `stats` show the drive's values in the shop: `"energyCells": 2` for a drive that
costs twice the Khador Drive's cells (the Khador Drive shows 1).

### Multi-grab tractor beams

A tractor beam (an item based on one of the tractor beams, 68 to 70 or 194) with `"tractorTargets": 2` or more pulls that
many containers at once. The first beam works as always: the scanner's salvage lock on a container in the crosshair (or a
disabled ship's cargo, or the Kingfisher's / Octopus' automatic pick, `tractorAuto`). The others reach out by themselves,
like the transfusion beams: each takes the nearest container within `tractorRange`, on screen or not, pulls it in on a beam
of its own and goes for the next. They need no lock, so the scanner stays free to lock ships while they work, and they keep
pulling while you mine or fly the autopilot. With no lock running, all of the beams pick containers. They let go in the turret
view, and they leave a mission container alone while the hold is full. In multiplayer each container is claimed as usual,
so two players never pull the same one.

With `"tractorNoLock": 1` the scanner leaves containers alone altogether: it never locks one, so it is always free for
ships, and every beam (a single one too) takes containers by itself as the extra beams do. An EMP-disabled ship's cargo
is still taken the usual way, by locking the ship for the beam's `tractorLockMs`, so keep that short. `"tractorColor"`
(an item field, not a stat) draws the beams in a colour of your own: `"#ff8000"` or `[255, 128, 0]`.

```json
{
    "id": "octopus_mk2",
    "base": 69,
    "name": "AB-6 Hydra",
    "techLevel": 8,
    "price": [210000, 240000],
    "tractorColor": "#40c0ff",
    "stats": { "tractorTargets": 4, "tractorRange": 40000, "tractorLockMs": 400, "tractorNoLock": 1 }
}
```

### Missile turrets and salvos

Any gun or turret that fires ordinary shots (not beam lasers, bombs, mines or cluster missiles) can fire homing missiles
and salvos:

- `"missiles": 1`: its shots are homing missiles. A turret's missiles home on the ship it is aiming at (an automatic
  turret's target, else the ship the scanner has locked), a gun's on the scanner's lock like the game's missiles. They keep
  their size, trail smoke and burst like a rocket on a hit; a boost or a cloak shakes them off.
- `salvoShots` fires that many shots side by side (6 m apart) each time; `salvoCount` salvos make a magazine,
  `salvoGapMs` apart, then the weapon waits `reloadMs` (`2`, `2`, `500`, `2000`: two missiles, half a second, two more, two
  seconds' reload). A secondary uses one round per salvo.

With the look of a missile, its own turret model and the missile's launch sound (see [Weapon effects](#weapon-effects)):

```json
{
    "id": "hornet_mat4",
    "base": 182,
    "name": "Hornet MAT-4 Missile Turret",
    "techLevel": 9,
    "price": [900000, 950000],
    "stats": { "damage": 30, "reloadMs": 2000, "speed": 56, "lifetimeMs": 1786, "automatic": 1, "turretTurnSpeed": 150,
               "missiles": 1, "salvoShots": 2, "salvoCount": 2, "salvoGapMs": 500, "turretRange": 100000 },
    "fx": {
        "turret": { "model": "models/mat4_turret.glb", "muzzle": 28, "shotStart": 32 },
        "projectile": { "item": 46, "scale": 0.35 },
        "shot": 46,
        "shotLoops": false
    }
}
```

### Echo blasts

A shot can set off a second, weaker explosion where it hit, a little later: the echo. Any gun does it (a plain gun, a scatter
gun, a homing missile, a turret, an NPC's gun), when its shot hits something; a bomb or a mine doesn't. The echo's blast
hurts everything in its reach with `percent` of the shot's damage, EMP and size, and shows the explosion `echoExplosion`
at that size. The shot's own hit and its burst are unchanged.

```json
{
    "id": "icarus_heavy_red",
    "base": 178,
    "name": { "en": "Icarus Heavy Enhanced" },
    "stats": { "damage": 50, "echoDelayMs": 500, "echoPercent": 60, "echoExplosion": 10 }
}
```

Here every burst of the Icarus Heavy is followed half a second later by a second burst at 60 % of the damage and size. Its
reach is the gun's own `blastRadius` (`echoRadius` left out). A gun without a blast radius has an echo that only shows its
explosion unless `echoRadius` is given. The echo is the shooter's blast, so it hurts what the shooter's own blasts hurt: not
the player's own ship, and no EMP for a turret or sentry.

### Weapon effects

A weapon (a new one or an `override` of an original) can look and sound like its own thing with `fx`. Anything it leaves out
stays its base item's. Several weapons can share one look: see [Effects many weapons share](#effects-many-weapons-share-weaponfxjson). What the weapon *does* never changes: a beam stays a beam, a mine a mine, a homing missile homes.

```json
{
    "id": "plasma_lance",
    "base": 12,
    ...,
    "fx": {
        "projectile": { "sprite": "fx/lance_bolt.png", "size": 450, "stretch": 4, "alongFlight": true, "color": [0.75, 0.45, 1.0], "glow": 3 },
        "muzzle": { "sprite": "fx/lance_flash.png", "size": 500, "lifetime": 90, "grow": 1.4 },
        "impact": { "sprite": "fx/lance_impact.png", "size": 2400, "lifetime": 280, "grow": 2.2 },
        "shot": ["fx/lance_shot_1.ogg", "fx/lance_shot_2.ogg"],
        "shotLoops": false,
        "explosionSound": "fx/lance_boom.ogg"
    }
}
```

The PNGs and sounds in this example are the mod's own files, in its `fx` folder (see `Modding/Examples/plasma_arsenal`):
the game's effects aren't PNGs you can copy. To keep the original effect and only change its colour, leave out `sprite` /
`model` and give just `color` (and `glow` if you like):

```json
{ "override": 0, "fx": { "projectile": { "color": [134, 59, 135] }, "impact": { "color": "#ff4020", "glow": 4 } } }
```

That recolours the base item's own projectile, muzzle flash or impact: its shape, animation and brightness stay, the
original's hue is replaced by yours (the Nirai Impulse's orange bolt turns purple). Any colour picker's values work.

| Field | |
|---|---|
| `projectile` | What flies. For a beam weapon the beam itself (stretched from the gun to what it hits along its length), for a missile the missile. |
| `muzzle` | The flash at the gun on every shot. |
| `impact` | What shows where a shot hits. |
| `shot` | The sound of a shot: a file, or a list of files (one is picked at random each shot). `.ogg`, `.wav` or `.mp3`. Or an item's number: that item's shot sound (`46`: the AMR Extinctor's launch). |
| `shotLoops` | `true`: the shot sound loops while the trigger is held (like the auto-cannons); `false`: one per shot. Default: the base item's (or, with `shot` an item's number, that item's). |
| `explosionSound` | Bombs, mines and blast weapons: the sound of the blast (a file, a list, or an item's number). |
| `trail` | The smoke trail behind each shot: `false` for none, or `{ "color": [r, g, b], "width": 100, "length": 3000 }` (its colour at the start, fading to nothing; half its width in game units; how long each part lasts in ms). Rockets and missiles trail white smoke (100 / 3000), thermo guns a coloured flame; a weapon without a trail gets one. Your own ship's shots and other players' show it. |
| `turret` | Turrets: the turret's own model, shown on the ship in flight, in the hangar and to other players. A glTF / GLB file, or `{ "model": "models/t.glb", "muzzle": 28, "shotStart": 32, "scale": 1.5, "offset": [0, 20, 0] }`. Its origin sits on the mount, 1 unit = 1 m as made. A node named `pivot` holding the base turns it left and right and, in it, a node whose name contains `_gun` tilts it up and down (the barrel along +Z); a model without them turns as one piece. `scale` resizes it; `offset` moves it off its mount (game units: x toward the pilot's left, y along the turret's up, z forward). Leave out `model` and give `scale` / `offset` to resize or move the base item's own turret; `"item": 48` instead of `model` takes that game turret's model (with its muzzle and shot start), and `model` may be one of the game's (`"gof2:turret_003_ship_mounted"`, see [The game's own models](#the-games-own-models)). `muzzle` is where the muzzle flash shows and `shotStart` where the shots start, along the gun node in game units (20 per metre) on the model as made; default: the base turret's. |

Each of `projectile`, `muzzle` and `impact` is either `false` (none at all), an object without a `sprite` or `model` (the
base item's own, or another item's, changed: below), or an object with a `sprite` or `model`.

Without a sprite or model:

| Field | |
|---|---|
| `item` | Another item's number: its projectile, muzzle flash or impact instead of the base item's (`46`: the AMR Extinctor's missile). |
| `scale` | Its size, times the original's (`0.35`). Default 1. |
| `color`, `glow` | Recolours it (above). |

With a sprite or model:

| Field | |
|---|---|
| `sprite` | A PNG drawn on a flat square that always faces the camera. Draw it white on black (or transparent): bright = visible, and `color` tints it. |
| `model` | Or a glTF / GLB model, or one of the game's (`"gof2:<name>"`, see [The game's own models](#the-games-own-models)). With `texture` it is drawn glowing like a sprite with that PNG; without, with the model's own materials (lit by the sun). |
| `size` | Its size in game units (20 units = 1 m): the sprite's width, or the model's largest extent. Default 200. The game's own effects are big: a blaster bolt is about 1600 units across, an impact glow several thousand. |
| `color` | A tint: `[r, g, b]` or `[r, g, b, a]` from 0 to 1, or from 0 to 255 (`[134, 59, 135]`: any value above 1 means the 0-255 scale), or `"#rrggbb"`. Default white. Without a sprite or model: the colour the original effect is recoloured to. |
| `glow` | How much it blooms; above 1 glows under the bloom option (the game's effects use 2.5). Default: as the game's effects. |
| `additive` | `true` (default): light added to what is behind it, like fire, plasma and lasers. `false`: drawn over it, using the PNG's transparency (smoke, solid shells). |
| `lifetime` | Milliseconds it lasts, for `muzzle` (default 100) and `impact` (default 300). A projectile lasts as long as it flies. |
| `grow` | Its size at the end of its lifetime, times the start (`2` = twice as big). Default 1. |
| `fade` | `false`: it doesn't fade out over its lifetime. Default `true` (projectiles don't fade). |
| `spin` | Turns about itself, degrees per second. |
| `alongFlight` | Sprites: stretched along the direction it flies and turned about it to face the camera, like a laser bolt, instead of a round sprite. Use with `stretch`. |
| `stretch` | Sprites: its length as a multiple of `size` (a 64 x 256 PNG wants `4`). Default 1. |
| `rotate` | Models: `[x, y, z]` degrees, when the model doesn't point forward (+Z). |

**Beam lasers** (an item based on the M6 A3 "Wolverine" family's beams: 9, 10, 11 or 228): the projectile is the beam
itself, drawn from the gun to the beam's reach. A `sprite` there runs the whole length of the beam, `size` units wide,
turned about it to face the camera: the PNG's width is across the beam and its height along it (bottom at the gun, top at
the far end), so a beam texture is a vertical strip, e.g. a bright core fading to the sides, 64 x 256. `alongFlight` and
`stretch` don't apply, and `grow` widens it only. A `model` is fitted the same way: `size` across, its length (+Z) the
beam's.

The game loads them with the mod at the start (the loading screen waits). Every gun of that item uses them: the player's,
NPC ships', turrets, sentry guns, and other players' in multiplayer.

#### Effects many weapons share (weaponfx.json)

A look made once can be given to several weapons. `weaponfx.json` lists the mod's own **weapon effects**: each has an
`id`, a `name` (for you and the Mod Builder) and a `base`, the game weapon whose look and sounds it starts from; then any
of the fields above. What an effect leaves out is its base weapon's, whatever weapon takes it.

```json
[
    { "id": "violet_bolt", "name": "Violet bolt", "base": 0,
      "projectile": { "color": [0.75, 0.45, 1.0] }, "impact": { "color": "#b070ff", "glow": 4 }, "shot": 46 }
]
```

A weapon takes one with `"fx": "<id>"` in items.json instead of an object, or `"fx": "<mod id>:<id>"` for another mod's
(list that mod in your `dependencies`). `"fx": <number>` takes a game weapon's whole look and sound, as an effect with
only a `base` would:

```json
[
    { "id": "violet_cannon", "base": 2, "name": "Violet Cannon", "fx": "violet_bolt" },
    { "override": 5, "fx": 46 }
]
```

The weapon still works like its own base item, and keeps its own turret model and blast; an effect's `turret`, `trail` and
sounds apply to every weapon taking it. In the Mod Builder: **+ New weapon effect** in the Weapon fx kind, and a weapon's
**Weapon effect** on its Items page (your effects first, then every game weapon's look).

## ships.json

A list of entries, like items.json: `"id"` adds a ship, `"override"` changes one.

```json
[
    {
        "id": "jedi_starfighter",
        "name": "Jedi Starfighter",
        "description": "A sleek interceptor ...",
        "race": 0,
        "armor": 480, "cargo": 40, "handling": 160,
        "price": 4150000, "priceDefault": 8300000,
        "slots": { "primary": 4, "secondary": 2, "turret": 0, "equipment": 13 },
        "model": "models/jedi_starfighter.glb",
        "icon": "icons/jedi_starfighter.png",
        "modelLength": 1000, "modelYaw": 0,
        "hangarHeight": 290,
        "mounts": [
            { "slotType": 0, "position_engine": [-73, -23, 442] },
            { "slotType": 1, "position_engine": [-38, -48, 115] },
            { "slotType": 3, "position_engine": [-48, -73, -478], "turretAngles": [0.55, 0.55, 0.55] }
        ],
        "lounge": { "systemRace": 0, "minCampaign": 32, "minRank": 12, "chance": 12 }
    },
    { "override": 10, "armor": 260, "cargo": 60 }
]
```

### New ships

| Field | |
|---|---|
| `id`, `name`, `description` | As for items. |
| `race` | Who builds it: 0 Terran, 1 Vossk, 2 Nivelian, 3 Midorian, 8 pirate, 9 Void, or a [mod's race](#races) (`"guild"`, `"mod_id:guild"`). Prices are 1 % lower in its race's systems. |
| `armor`, `cargo`, `handling` | Hull points, hold size (t), handling (the originals use 60 to 160). |
| `price`, `priceDefault` | The price, and the price in the Default Economy (0 = the same). |
| `slots` | `primary`, `secondary`, `turret`, `equipment`. A ship may have several turret slots, one per turret mount. |
| `model` | **Required.** The ship's model, a `.glb` (or `.gltf`) file in the mod, or one of the game's (`"gof2:ship_028_terran"`, see [The game's own models](#the-games-own-models)). Shots hit its real shape, and the player's ship slides along it: every mesh counts except those with an additive / glow material, so keep invisible helper meshes out of the file. |
| `modelLength`, `modelYaw` | Its length nose to tail in game units (the Phantom is about 1000; 20 units = 1 m), and a turn in degrees when the model's nose doesn't point forward (+Z). The model is scaled to that length. |
| `modelPitch`, `modelRoll` | More turns in degrees (about the side axis and the length axis), for a model lying nose up or on its side in its file. |
| `modelOffset` | `[x, y, z]` in game units (like the mounts): the sized model moved off the ship's centre, the point it turns, docks and flies around. |
| `icon` | The shop icon, a PNG of 180 x 88 like the originals (the ship on the plate), or one of the game's (`"gof2:ship_028"`). None = the Phantom's (on a game model: that ship's). |
| `mounts` | Where guns and exhausts sit, in game units from the ship's centre (x toward the pilot's left, y up, z forward): `slotType` 0 primary gun, 1 secondary, 2 turret (`"turretUp": [x, y, z]` is the way its base faces, in the same axes: `[0, 1, 0]` on top (the default), `[0, -1, 0]` under the hull (or `"upsideDown": true`), `[1, 0, 0]` / `[-1, 0, 0]` the left / right side, `[0, 0, 1]` the nose, or any slant; the Mod Builder sets it from the hull's surface where you place the turret), 3 engine exhaust (the flame and its particles; `turretAngles[0]` sizes the particles; `"glowColor": [r, g, b]` (0 to 1) colours that exhaust's flame and its exhaust particles; `"glowSize": [halfWidth, halfHeight, length]` (game units) makes the flame an ellipse of that size, its flared ring `length` behind the nozzle; `"glowShape"` gives it another outline in that box: `"round"` (the default), `"rounded"` (a rectangle with rounded corners), `"square"` (a square or, with a different half width and half height, a rectangle), `"hexagon"`, `"diamond"` or `"triangle"`). |
| `hangarHeight` | How high its pivot sits above the hangar pad, in game units (the originals 140 to 400). A customizable ship's is measured from its build instead. |
| `mounts` | Where guns and exhausts sit, in game units from the ship's centre (x right, y up, z forward): `slotType` 0 primary gun, 1 secondary, 2 turret (`"upsideDown": true` hangs it under the hull; `"builtIn": true`: the ship's own turret, no turret model of the game on it, the mounted turret item firing from this point; a customizable ship's primary / secondary mounts may also carry `"rotation"`, see [Customizable ships](#customizable-ships)), 3 engine exhaust (the flame and its particles; `turretAngles[0]` sizes the particles; `"glowColor": [r, g, b]` (0 to 1) colours that exhaust's flame and its exhaust particles; `"glowSize": [halfWidth, halfHeight, length]` (game units) makes the flame an ellipse of that size, its flared ring `length` behind the nozzle; `"glowShape"` gives it another outline in that box: `"round"` (the default), `"rounded"` (a rectangle with rounded corners), `"square"` (a square or, with a different half width and half height, a rectangle), `"hexagon"`, `"diamond"` or `"triangle"`; with `engineFx`, `"fxScale"` sizes that exhaust's effect and `"rotation"` turns the way it blows, as a weapon mount's below;
`"outline": [x0, y0, x1, y1, ...]` the nozzle's shape seen from behind, game units around the mount, for the [exhaust
styles](#exhaust-styles)), 4 manoeuvring jet (`"direction": [x, y, z]`, the way it blows in the same axes: a short jet fires whenever the ship strafes, rises, sinks or reverses against it, and with no input the jets blowing along the ship's drift fire to brake it, like EVERSPACE 2's lateral thrusters; only on the player's ship. Its colour is the hue of the `lateral thruster` colour slot, else of the flames' boost colour, at EVERSPACE 2's own brightness: a slot's `intensity` doesn't change it), 5 booster (a ship with engine flames: EVERSPACE 2's booster socket, its trail and light in `boostColor` while boosting; `"socketScale"` sizes them, as on an exhaust), 6 contrail point (EVERSPACE 2's wing-tip contrails: a white ribbon trailing from the point while boosting and flying nose first, `"socketScale"` its width and, below 1, its faintness; on every ship, `"es2Engine": { "contrails": false }` turns a ship's off), 7 headlight fixture (`"direction": [x, y, z]` the way it shines: a lens flare there while the player's headlights are on (the Headlights key, L), and materials named `Headlight` glow only then; the light itself is the player's one headlight at the nose, on any ship). |
| `engineGlowRadius` | Size of the engine flame at each exhaust mount (game units, default 24); a mount's `glowSize` replaces it. |
| `engineGlowColor` | `[r, g, b]` (0 to 1): the flame colour of every exhaust without its own `glowColor`. None = the game's own blue-white flame. |
| `materials` | Optional. Replace the model's own materials, see below. |
| `throttleGlow`, `extraGlows` | Optional. Parts of the hull that glow with the throttle, see below. |
| `addParts` | Optional. Glowing layers modelled beside the hull, in the same place and size (the original game's `_lights_add` / `_engine_glow_add` / `_engine_add`): a list of `{ "model": "models/x_lights.glb", "role": "lights" }`. `role`: `lights` (always lit), `engineGlow` (your engine glow, lit with the engines, in place of the round glows at the exhausts) or `engine` (the NPCs' engine glow). Drawn additively with the entry's `texture` (a PNG in the mod), else the model's own base colour texture, else (a model without textures) the game's glow texture for the role (the ship's race). The builder's Ship tab lists them under Glowing parts, each with a Texture… button. |
| `engineFx` | Optional. A Galaxy on Fire 3 style engine effect at every exhaust instead of the flame and its particles, see [Engine effect](#engine-effect). For effects any ship can wear, see [Exhaust styles](#exhaust-styles). |
| `lounge` | Optional. A visitor in the Space Lounges sells it: in systems of `systemRace` (-1 = any), from story step `minCampaign` (in free play from rank `minRank`), `chance` % each time a bar fills with new visitors. |
| `npcTurrets` | Optional, also in an `override`. Turret items (numbers or `"mod:id"`, e.g. `[ 48, "my_mod:flak" ]`) the **NPC** ships of this type carry on its turret mounts (`slotType` 2), the n-th item on the n-th mount: in space they aim and fire by themselves at whatever that ship is hostile to (your own turret model, missiles and salvos included), and parked in a hangar they show them. Items that are no turret, or more items than mounts, give a warning. The game's own NPC ships carry none; the Bloodstar (45) and the Rhino (51) swap their built-in turret for yours. |
| `dealer` | Optional. The ordinary ship dealers may stock it: `{ "chance": 2, "systemRace": -1, "minTechLevel": 0 }`, `chance` % (decimals allowed) each time a station's dealer list is made, at stations of `systemRace` systems (-1 = any) with at least that tech level (above 10: the tech level 10 stations). Not the special yards (Kothar, Quineros...); Thynome's dealer is an ordinary one, the VoidX added to it once every gold medal is won. |
| `traffic` | Optional. How NPC fighters fly it: `{ "weight": 1, "races": [8] }`. With the Gameplay option "Mod ships in traffic" (Off / 10 / 25 % (default) / 50 %) that share of the game's NPC fighters (patrols, raiders, pirates, escorts, guards) fly one of the mods' ships of their race instead, picked by `weight` (0 = never); `races` are the races whose fighters fly it (0 Terran, 1 Vossk, 2 Nivelian, 3 Midorian, 8 pirates, 9 Void, or [races](#races); default its own `race`). A mod race's fighters always fly one of the mods' ships, whatever the option. Only ships up to 150 m long, available now (`available`), with their model loaded. Their hull and guns stay those of the fighter they replace. Freighters and capital ships aren't replaced, and a [campaign's](#campaigns) `trafficShips` win. A [customizable ship](#customizable-ships)'s NPCs each fly a random build of it (parts the type may fit, a tier, one of the kit's colour presets or random palette colours, now and then a skin); `"randomBuild": false` keeps them to its default build. The builds use the parts already loaded, and every docking loads a few more in the background, so the variety grows as you play. `"boss": true` makes it a boss hull instead: it never flies as an ordinary fighter, and the pirate bosses (Pirate outposts and bosses, the Pirate activity board) fly it at the same option's chance, up to 400 m long; give it `npcTurrets` to arm its turret mounts. Their hull, guns and escorts stay the pirate boss's. `"capital": "battleship"` or `"freighter"` makes it a capital ship instead, see [Capital ships and freighters](#capital-ships-and-freighters). `"role"` gives it a part in a fight, after EVERSPACE 2's drones: `"sniper"` fires from twice as far; `"detonator"` holds its fire, flies into its target and blows up within 72 m, hitting every ship within 360 m for 1.8 seconds of its gun's fire (EVERSPACE 2's); `"webber"`'s hits slow your ship to 40 % speed for 4 to 5 s; `"shieldCharger"` holds its fire and repairs about 5 % a second of the hull of its race's ships within 1.5 km; `"armorLink"` holds its fire and links its nearest such ship, which then takes three quarters of the damage and repairs about 1.6 % of its hull a second (EVERSPACE 2's armour buff over GoF2's hull-only NPCs); `"antiMissile"` holds its fire and shoots down one of your missiles every quarter second within 2.9 km; `"jumpSuppressor"` holds its fire and, while it flies hostile in the orbit, your Khador Drive won't charge (it does once the drone is gone); `"lootDrone"` holds its fire and carries 4 to 7 pieces of loot: once hit or once you come near, it flees from you, charges its jump drive 5 to 10 s later and is gone 6 s after that with what it still carries (an EMP makes the charge start over); each time its hull falls far enough (about a share for each piece, the first at its first hit) a piece pops out in a crate of its own, and the rest drop when it dies. A loot drone never flies as an ordinary fighter: the cruise drive's `lootdrone` encounter flies it (see [Cruise drives](#cruise-drives)). `"loot": [ { "item": "155", "chance": 0.2, "min": 1, "max": 3 } ]` is what its NPCs drop besides their cargo, each entry by its chance (0-1), `min` to `max` of the item (a reference as text: `"155"` or `"mod:id"`), at most `"lootDrops"` of them (0 = any number), in a crate a kind; a Void ship's too, which carries no cargo. `"ai": "gof3"` makes its NPC fighters fight Galaxy on Fire 3's way while the Gameplay option "NPC dogfighting" is "GoF3 on mod hulls" (the default; "GoF3 for all" does it for every fighter, "Original" for none): attack runs of 8 to 13 seconds that end by veering off for 5 to 6 seconds (or holding back for 2) instead of circling you, now and then 8 seconds on your tail at 1.6 times the speed or 4 to 6 seconds just ahead of your nose holding fire (bait), shots led to where you will be with an aim that settles in half a second, and a barrel roll away from a hit (1.8 seconds, the first 1.5 invulnerable; on Normal 15 % of the hits, Hard 25 %, Extreme 33 %, Easy never, a boss 66 %); a system's local patrol flies in a V behind its first fighter until a fight breaks it up. `"devices": [ ... ]` gives its NPCs the devices a Most Wanted criminal can have, in the same format (see the boss devices under Most Wanted, below: mine layers, clouds, shields and the rest), used whenever they fight you, without the criminals' warnings on the HUD; a `mineLayer` with a `layBehind` below 0 throws its mine ahead of the nose (GoF3's kamikazes throw one at you and lay others behind them). Not on wingmen. `"secondaries": [ { "item": "mod:missiles", "minRank": 6, "maxRank": 17, "share": 25, "factor": 1 } ]` gives its NPCs a second weapon (missiles or another gun) as the Most Wanted criminals carry one: the first entry an NPC carries as it comes, while your rank is within `minRank` / `maxRank` and for `share` % of them (default 100), its damage x `factor`. |
| `available` | Optional. A [condition](#conditions) that must hold before it is sold anywhere (dealers and lounges), and before NPCs fly it. |
| `sounds` | Optional. The ship's own engine and boost sounds, see [Ship sounds](#ship-sounds). |
| `crate`, `crateSize` | Optional. The loot container a ship of this type leaves when it is destroyed or robbed with a tractor beam: a `.glb` / `.gltf` file in the mod, drawn in place of its race's container and as big as that container, unless `crateSize` gives its largest extent in game units (the game's containers are about 400). Ships may share a file; the first one's `crateSize` counts. None = its race's container. |
| `wingmanDevice` | Optional. What a hired wingman flying this ship uses (Galaxy on Fire 3's wingmen): `"repair"` (Hull Repair: with your hull under 40 %, a quarter of it mended over 3 seconds), `"ammo"` (Ammo Supply: 5 more rounds of your selected secondary weapon when you are down to one or none) or `"charged"` (Charged Shots: its own shots do x1.75 for 5 seconds while it attacks); 55 seconds between uses. The HUD says when it does. None = nothing, like the game's wingmen. See [Wingmen](#wingmen). |
| `integrated` | Optional, also in an `override`. Equipment built into the hull, like the Kothar ships' Khador Drive (Cronus, Typhon, Nemesis) and the Specter's / Scimitar's cloak: `{ "khadorDrive": true, "cloak": true }`. `khadorDrive`: the ship can make Khador jumps without the drive in a slot (it still needs energy cells). `cloak`: `true` (the U'tool's stats), a cloak item (a number or `"mod:id"`, e.g. `96`) whose stats it has, or `false`; a cloak the player mounts replaces it, and with the Gameplay option "Cloak bay on cloaking ships" it goes into the built-in cloak's bay instead of a slot. `false` takes an original ship's away. `items`: any equipment built in, a list of item numbers, `"mod:id"` keys or `{ "item": ..., "amount": n }` (a secondary weapon's starting ammo, default 10), e.g. `"items": [46, { "item": 36, "amount": 20 }, 54]`. Built-in items work like mounted ones but take no slot, show on the hangar's Ship tab under "Built into the ship", and can't be demounted, sold or stored; they stay with the ship type when the player changes ships. A one-per-ship item the player fits (a shield, armor, a booster, a scanner, a drill, a cloak...) takes the built-in one's place without a slot, and the built-in one is back when it is demounted; built-in weapons and turrets add to the ship's own slots. A built-in launcher's missiles are refilled by buying more; a one-off item that is used up (an emergency system) is back at the next docking. An empty list takes them away. |

Ship and station materials are lit physically (URP Lit): they reflect the system's sky by their smoothness (the
alpha of the metallic map) and metalness, the way PBR engines draw them. The game's own hulls add the original's shine
on top, from specular maps made for it; a mod's smoothness map is a different thing, so mod materials don't get it. In
the game's hangars a mod hull is lit from above with less of the room's flat ambient light, so it keeps its shading
(models have no shading painted into their textures, the game's do). A custom hangar or bar (interiors.json) lights
mod and game ships alike. A mod system reflects its template system's sky.

The model's own materials (base colour, metallic-roughness with its metallic / roughness factors, normal map, emission,
transparency) are used as they are. glTF
from Blender works: File > Export > glTF 2.0, format glTF Binary (.glb), +Y up. Projects made in Unity can use the
Project window's right-click **GoF2 > Export Model As GLB (Mods)**.

Moving parts animate: the model's first animation (a spinning radar dish, bobbing engine pods: objects moved, turned or
scaled over time) plays on a loop wherever the ship is shown, in flight, in the hangar and on NPC ships. Animations
that bend the mesh with bones (an armature) don't play: such a hull is shown in its rest pose. Keep moving parts as
separate objects animated by themselves, and put the animation you want looped first in the file.

### Capital ships and freighters

A big hull can fly in the orbits as a capital ship or a freighter rather than a fighter: give it
`"traffic": { "capital": "battleship" }` or `"traffic": { "capital": "freighter" }` (it never flies as a fighter then).

- **`"battleship"`** parks in the orbit's capital ship special: in a Terran or Vossk system, at the "Mod ships in traffic"
  chance, in place of the game's Terran battleship or Vossk battleship; in a Nivelian or Midorian system (the game has no
  capital ships there) in 30 % of the orbits with freighters. It has the Terran battleship's hull (25 x a freighter's)
  and its `npcTurrets` on its turret mounts fire although it is parked.
- **`"freighter"`** is one of the orbit's freighters now and then (the same option's chance), cruising like them.

A **pirate** (`race` 8) or **Void** (9) capital ship is hostile instead: now and then one comes with a pirate raid (a third of
the option's chance, parked 3 km beyond the raiders) and with the Void's ships in their own orbit. `"traffic": { "capital":
"battleship", "weight": 0 }` keeps one out of the traffic altogether: a mission brings it (the `spawn` step or `/spawn
mod_id:ship`, which parks any capital ship 1-2 hull lengths ahead of the player, subsystems and all). `"hullFactor": 6` makes
its hull that many times the Terran battleship's.

Its race is its `race` (or the races in `"races"`). Its collision boxes are measured on the model: the hull cut along its
length into 8 slices. The player and the shots still meet its real shape (unless Options > Gameplay "Ship collision" is
Original). It leaves no wreck: it burns, explodes at its `explosionScale` and is gone. The capital ship enhancements'
fleet battles stay with the game's own capital ships; escorts, a dock with a shop, launches and loot of its own are
`npcships.json`'s ("NPC ships" below, `"ship"`: this hull). A capital ship's hardpoints carry at most 8 turrets
in multiplayer copies, so give it 8 at most. Keep it under about 5 km: the flight level's far plane is 15 km.

### Capital ship subsystems

A capital ship's turrets, shield generators, weak spots and hangars can be parts of their own that the player shoots off its
hull (EVERSPACE 2's warships): `"subsystems"`, each placed like a mount (`position_engine`, game units in the ship's frame;
`turretUp` / `upsideDown` the way it faces):

```json
"subsystems": [
    { "kind": "turret", "name": "Torpedo launcher", "position_engine": [0, 3200, 9000], "item": "my_mod:torpedo_turret", "scale": 2, "stage": 1 },
    { "kind": "object", "name": "Field emitter", "position_engine": [4000, 0, 21000], "model": "my_mod:emitter", "shields": true, "stage": 1, "hull": 2600 },
    { "kind": "object", "name": "Weak point", "position_engine": [-2500, -3300, 8000], "model": "gof2:v_station_battlestation_shield", "weakspot": 2.5, "bonus": 0.08, "stage": 3 },
    { "kind": "hangar", "model": "none", "position_engine": [-4400, 0, 4400], "turretUp": [1, 0, 0], "ships": [ "my_mod:raider", 2 ], "every": 30, "perWave": 2, "maxAlive": 4, "total": 8 }
],
"shieldMaterial": "energyfield|", "shieldedDamage": 0
```

- `"turret"`: a turret object of turret item `item` (its model, gun and aim; `scale` sizes its model).
- `"object"`: a part without a gun: its `model` is one of the game's (`"gof2:<model>"`) or a ship of a mod (its model; give such
  a prop ship `"race": -1` and `"traffic": { "weight": 0 }` so it never flies), the game's battlestation shield generator
  without one.
- `"hangar"`: launches `ships` (numbers or `"mod:id"`, random builds for customizable ships) along its facing, `perWave` every
  `every` seconds while the capital ship is the player's enemy and the player within 7.5 km, at most `maxAlive` flying and
  `total` in all (-1 = no end); `"model": "none"` makes it a launch point on the hull that can't be shot off.
- `hull`: its hit points (default by kind, growing with the rank), x the difficulty.
- `shields`: while any part with it stands, the hull takes `shieldedDamage` of every hit (0 = none; the player is told to
  find the generators), and its renderers whose material name contains `shieldMaterial` (its energy fields) show, hidden once
  the last of them falls.
- `weakspot`: hits on it also go to the hull x this; its destruction takes `bonus` (a share) of the hull's maximum.
- `stage`: a part can't be harmed while a part of a lower stage still stands (an encounter's order: fields, then guns, then
  the weak points).

Parts take the capital ship's side, go when it dies, and a mission's `alive("<name>")` counts them. Every turret (these, the
`npcTurrets` on a ship's mounts, the game's own capital ship turrets, your own) holds its fire while its barrel points into
its own ship's hull and turns to another target: a turret on the far side can't shoot you through the ship. Without `subsystems` its
`npcTurrets` still arm its turret mounts. `"engineStyle": "es2"` gives a capital ship EVERSPACE 2's engine glow and trails at
its exhaust mounts (`slotType` 3, sized by `socketScale`) without flame meshes of its own; `"es2Engine": { "glow": false,
"trails": false, "contrails": false }` turns any of them off, as EVERSPACE 2's own warships and freighters have them (their blueprints'
`SpawnEngineGlow` / `SpawnEngineTrails`). A parked `battleship`'s engines idle (its flames, nozzle glow and glow at 60 % of
full thrust): another ship's engine effects follow its speed, and a parked one has none. `"retreat": true` makes a capital
ship jump away instead of being destroyed (EVERSPACE 2's Dreadnought): when its hull runs out it keeps 1 point, can't be harmed,
holds its fire and its subsystems stop; its hangars' fighters jump out from 3 seconds on, one by one, and after a 3 second
charge it jumps away in a Khador jump the size of the ship (EVERSPACE 2's timing), its parts with it, leaving no wreck and nobody's kill; a mission's `alive("<name>")` drops to 0
once it is gone. With a mission's `"repeat"` the encounter can come again.

### Camera, cockpit, damage smoke and explosion

How a ship is seen, for a new ship or an original through `override` (each one optional; left out = the game's):

```json
"camera": { "distance": 2400, "height": 900, "lookHeight": 900, "lookAhead": -1100 },
"cockpit": [0, 90, 760],
"smoke": [[39, 68, -64], [-62, 65, 10]],
"explosionScale": 1.2
```

- `camera`: the chase camera, in game units: `distance` behind the ship's pivot and `height` above it, aimed at a point
  `lookHeight` above the pivot and `lookAhead` ahead of it (negative: behind it). The game's are 1338 / 600 / 600 / -650 for
  every ship, made for hulls of about 1200-1400 units; a bigger hull needs the camera further out or it fills the screen.
  A field left out keeps the game's.
- `cockpit`: where the cockpit view (F2, or holding the camera button) sits, a point like a hardpoint's `position_engine`
  (x the pilot's left, y up, z the nose). Without it the game puts the seat on the bow from the hull's shape. Either way
  the seat stays behind the frontmost gun, so its shots start in view.
- `smoke`: up to 4 points where the ship trails smoke and fire once it is below a third of its hull (NPC ships); without
  them it smokes from its middle. The smoke is shared between the points, so two points don't make twice as much.
- `explosionScale`: how big the explosion is when the ship dies, as a multiple of a fighter's (1); 0.25 to 20. The NPC
  ships, the player's ship and other players' ships use it. Freighters and capital ships keep their own.

The Mod Builder has them on a ship's page under **Camera and damage**.

### Ship sounds

`"sounds"` gives a ship (a new one, or an original through `override`) its own engine and boost sounds instead of the game's:

```json
"sounds": {
  "engine":     { "file": "audio/engine.ogg", "volume": [0.15, 0.27], "pitch": [0.9, 1.1] },
  "boostStart": { "file": "audio/boost_start.ogg", "volume": 0.4, "pitch": [0.8, 1.2] },
  "boostLoop":  { "file": "audio/boost_loop.ogg", "volume": [0.5, 1.0], "pitch": [1.0, 1.5] },
  "boostStop":  { "file": "audio/boost_stop.ogg", "volume": 0.25 },
  "boostDepleted": { "file": "audio/boost_dry.ogg", "volume": 0.5 },
  "charger":    { "file": "audio/boost_charge.ogg", "volume": [0.1, 0.6], "pitch": [0.5, 2.0] }
}
```

- `engine`: the engine loop, from its first `volume` / `pitch` at a standstill to the second at top speed. NPCs flying the
  ship (see `traffic`), its hangar flights and other players' copies of it play it too.
- `boostStart` / `boostStop`: played once when a boost starts / ends; a `pitch` range is a random pitch in between.
- `boostDepleted`: played once, beside `boostStop`, when a free-flight boost dies because its booster ran empty (EVERSPACE 2's empty-tank sound); a held key then has to be pressed again.
- `boostLoop`: a loop while boosting, from its first values as the boost starts to its second at full strength.
- `charger`: a loop while the booster recharges, from empty to full.

Each sound is a file name, a list of files (one at random each time), or `{ "file" (or "files"), "volume", "pitch" }`
(`volume` / `pitch` a number or `[from, to]`). The files (`.ogg`, `.wav` or `.mp3`) may sit anywhere in the mod, but not in
`sounds/`, which replaces the game's sounds by name (see [Sound effects](#sound-effects)). A file missing under its name is
taken in another of those formats of the same name (`audio/engine.ogg` from `audio/engine.wav`), so a tool that can't always
encode Ogg Vorbis can keep its JSON the same. The volumes are the game's: its
engine loops play at about 0.27, a booster's start at 0.8, its shots at 0.7. Any sound left out stays the game's.

### materials

To set materials up in the mod instead (paths inside the mod), each entry applies to the renderers whose name contains
`mesh` (empty = all) and, when `submesh` is 0 or more, only that submesh:

| Field | |
|---|---|
| `diffuse`, `color` | Base colour map (PNG) and / or an RGB tint `[r, g, b]` (0 to 1). |
| `normal`, `normalScale` | Normal map. |
| `tiling` | How many times the main textures (colour, normal, metallic, emission) repeat over the UVs (default 1): a plating texture laid over a big hull. |
| `metallicSmoothness`, `metallic`, `smoothness` | Metallic (red) / smoothness (alpha) map; without a map `metallic` sets the metalness. |
| `specularGlossiness`, `specularColor` | The specular workflow in place of metallic: a map of the specular colour (RGB, sRGB) and smoothness (alpha, times `smoothness`), or a specular colour `[r, g, b]` without a map. A coloured highlight and reflection over the diffuse, not a metal, lit the way Galaxy on Fire 3 lights its surfaces (its Phong highlight, gloss as smoothness, a fresnel). A glTF model's `KHR_materials_pbrSpecularGlossiness` materials load the same way. |
| `environment`, `rim` | With a specular map or colour: how much the surroundings reflect in it, 0 to 1 (default 1; Galaxy on Fire 3's `Environment_Amount`: 1 on ship hulls and glass, 0 on stations, capital ships and guns), and its rim light along the outline, 0 or 1 (default 1). |
| `highlightMultiplier`, `multiplierInMap` | With a specular map or colour: Galaxy on Fire 3's `Specular_Multiplier`, how much brighter the highlight is than the map (default 1), and `true` when the map already carries it (the GoF3 importer's hull maps: then it brightens the whole specular, as GoF3's ship shader does). A glTF material says these four in its `extras`: `"extras": { "environment": 0, "rim": 1 }`. |
| `emission`, `emissionColor`, `emissionIntensity` | Emission map and / or colour; above 1 it blooms. |
| `alphaClip` | Cut the diffuse's alpha below this (decals). |
| `opacity` | Below 1: glass (see-through, reflections stay bright). |
| `detailAlbedo`, `detailNormal`, `detailTiling`, `detailNormalScale` | A tiled detail pair (brushed metal ...). |
| `doubleSided` | Draw both faces (models with one-sided panels). |
| `engine` | `true`: its emission (glow) follows the engines: dim at idle, full at full throttle, dark on a ship parked in a hangar (an engine nozzle's glowing surface). |
| `engineIdle` | With `engine`: its share of the glow at throttle 0, 0 to 1 (default 0). `1` = a steady glow like the game's own engines: lit whenever the engines run (the take-off from the hangar and all flight, whatever the throttle), dark while the ship is parked. |
| `boost` | `true`: its emission shows only while boosting, fading in and out with the boost (a boost vent); dark otherwise. |
| `damageEmission` | A mask (PNG) used as the material's emission when it has none: battle damage embers, glowing ember-orange with the ship's hull damage (from a third lost to full at 90 %, flickering slowly). |
| `damageDetail`, `damageNormal` | Battle damage charring (a material without its own detail maps): a detail colour PNG (sRGB, mid grey = unchanged, darker = burnt; its alpha the burn mask) and a normal map, blended in with the hull damage (from a fifth lost to full at 75 %), under the embers. |
| `shimmer` | `true`: a nozzle shimmer: an additive layer over the same faces as the nozzle's glow, its `emission` pattern times the `flameNoise` texture scrolling along it, in `emissionColor` x `emissionIntensity` (with `engine` it follows the engines like the glow). |
| `additive`, `scroll`, `ignoreVertexColors` | `true`: an additive glow layer, unlit, its light added to what is behind it (a gate's glowing ring, an energy field, sparks along a hull): the `emission` texture (else `diffuse`) x `emissionColor` (else `color`, else white) x `emissionIntensity` (above 1 it blooms) x the mesh's vertex colours (not with `ignoreVertexColors`), the texture's alpha as a mask; `scroll` `[u, v]` moves the texture by that many UV units a second (electricity). It casts no shadow and nothing collides with it. |
| `hologram` | `true`: a hologram projector: additive rings in `emissionColor` x `emissionIntensity` through the `emission` pattern, which turns about each ring's centre (UV 0.5, 0.5) while the rings bob along their normal. A second UV channel per ring: x its spin (-1..1: direction and share of the speed), y its share of the bob (written as 1 - share: the loader flips V). |
| `flame` | `true`: these surfaces are engine flames drawn by the game (see below); `emissionColor` x `emissionIntensity` is the flame's colour. |
| `flameLength`, `flameIdle` | How far the flame reaches behind its stub, in the model's own units (default 5: EVERSPACE 2's 500 cm). `flameIdle` is no longer used. |
| `boostColor` | The flame's colour while boosting, RGB 0 to 1 (default a hot blue), x `emissionIntensity`. |
| `flameNoise` | A tiling grey noise PNG (its red channel): each row across the flame flickers with it (EVERSPACE 2's `T_Noise_018_M`). |

**Engine flames.** Model each flame as a short stub behind its nozzle: one or more flat cards (crossed cards read as a round
flame) whose first UV's u runs from 0 at the nozzle to 1 at the tip, with a material the `flame` entry names. The game
draws it as EVERSPACE 2 draws its own (its exhaust material, decompiled): each vertex is moved u x `flameLength` backward;
the flame's strength is the thrust to the 6th power (bright only near full thrust, gone at idle and while the engines are
off), it falls off steeply along its length ((1 - u) / (1 + 18 u), to the 4th power), and each row across it flickers with
the noise, so it reads as a short white-hot core ringed in its colour. A second UV channel's y says what the card is: 0.5
(or none) a main flame card (the engine colour), 0.25 (or 0.75) a boost card (only while boosting, in `boostColor` x 2;
EVERSPACE 2's boost jets). There is no fading by the view angle: seen from behind, flat cards are thin by their shape. On
the player's own ship the flame eases with the controls (faster rising than falling) and its tip bends against the
ship's movement, by at most a sixth of its length at full speed (EVERSPACE 2's steering offset); other ships' flames
follow their speed at once. A ship with flames gets neither the engine glow discs nor the exhaust particles at its exhaust
mounts; it gets EVERSPACE 2's other engine effects instead, at every exhaust mount (`slotType` 3) and booster mount
(`slotType` 5), each sized by the mount's `"socketScale"` (EVERSPACE 2's socket scale, default 1) in the kit's metres: on
the player's ship a faint veil trail behind each engine and a point light on the hull that grows with the thrust; on any
other ship a camera-facing glow and a long trail, seen only while it flies nose first. A booster mount's effects are in
`boostColor` and show only while boosting. A colour slot's `emission` target recolours the flame like any glow.

### Glows

`throttleGlow` (and each entry of `extraGlows`) lights the hull where a mask is bright, following the throttle:

| Field | |
|---|---|
| `mask` | A PNG over the model's UVs; its bright parts glow (a plain white mask = the whole renderer). |
| `mesh`, `submesh` | Which renderers / submesh. |
| `color` | RGB tint. |
| `idle`, `full`, `boost` | Brightness at throttle 0, full throttle and while boosting (above 1 blooms). |
| `off` | Brightness while the ship is parked in a hangar with its engines off (default: `idle`). With `idle` = `full` and `off` 0 the glow is steady like the game's own engine glow: dark on the pad, lit through the take-off and all flight. |
| `offset` | How far the glow sits out from the hull (game units, default 0.5). |
| `trailWidth`, `trailTime`, `trailBrightness`, `trailCount` | A trail in the glow's colour while boosting or jumping: width (game units), length (s), brightness, and `trailCount` above 0 for that many trails along the glow's rear edge. |

### Engine effect

`engineFx` draws a Galaxy on Fire 3 engine at every exhaust mount as that game draws it, in place of the flame disc and the
exhaust particles: the engine's own parts (`meshes`, below) and its particle trail, contrails and booster flash. It follows
the engines like the other engine effects: off while the ship is parked or the engines are off, a little shorter at idle,
stronger while boosting.

| Field | |
|---|---|
| `meshes` | The engine's parts (below): `"fx/engines/<name>.json"`. |
| `meshScale` | Game units per unit of the file (Galaxy on Fire 3's: 53.5). Each exhaust's `fxScale` sizes it further. |
| `meshGlow` | Brightness multiplier (default 1; above 1 blooms). |
| `nozzle` | The nozzle radius the effect is drawn for (game units, default 87): exhaust styles on this ship are sized so its nozzles are this large (only when `nozzle` is given), and a style made from this effect fits other ships' nozzles against it. The GoF3 Ships mod's: Nivelian 60, Terran 100, Vossk 45 (the Vossk plume is three times its nozzles), Inouac 138, the capital engines 108-117. |
| `trailParticles`, `contrailParticles`, `boostParticles`, `contrails` | Galaxy on Fire 3's particle trail, contrails and booster flash ([below](#galaxy-on-fire-3s-trails-contrails-and-booster-flash-trailparticles-contrailparticles-boostparticles)). |

An exhaust mount may carry `"fxScale"` (that exhaust's effect larger or smaller) and `"rotation"` (the way it blows, turned
from straight back; Euler degrees or a quaternion in the ship model's frame, as a weapon mount's), and `"outline"` (its
nozzle's shape: [x0, y0, x1, y1, ...] game units around the mount in `position_engine`'s axes, as for
[Exhaust styles](#exhaust-styles)): the engine's parts (all but its sprites) are then spread to that shape and centred on
it; without one it is round.

```json
"engineFx": { "meshes": "fx/engines/engine_nivelian.json", "meshScale": 53.5, "nozzle": 87,
              "trailParticles": { "texture": "fx/particles/beam_001.png", "life": [1.8, 1.8], "rate": 30, "max": 60 } }
```

(Before 2026-10-24 `engineFx` drew an imitation from PNGs: `glow`, `cone`, `trail`, `spiral` and their sizes and colours.
Those fields are gone: a ship or exhaust style still giving one draws without it, and the mod's warnings name what
replaced it: `meshes` for `glow`, `cone` and `spiral`, `trailParticles` for `trail`.)

#### Galaxy on Fire 3's engines as the game draws them (`meshes`)

`"meshes": "fx/engines/<name>.json"` draws an engine part by part as Galaxy on Fire 3 itself does. The
GoF3 Ships mod's importer writes these files from the game's engine scenes (`tools/engine_meshes.py`); you can write your
own. Each part has its own mesh and the options of the shader Galaxy on Fire 3 compiled for its material:

| Part field | |
|---|---|
| `name`, `pos`, `rot`, `scale` | Its place in the engine's frame: `+z` runs backward along the exhaust from the nozzle, in the file's units (`rot` a quaternion x y z w). |
| `v`, `n`, `uv`, `c`, `i` | Its mesh: positions, normals, UVs, vertex colours (r g b a, 0 to 1) and triangles. |
| `o`, `sprite` | A sprite: every vertex sits at the part's centre and is pushed out by its offset (`o`, 2 per vertex) facing the camera, sized by the engine's scale. |
| `texture`, `useTex`, `flowMap`, `flow` | Its picture (a PNG in the mod, read as stored, as GoF3 does) or, without one, its `diffuse` colour; a flow map's red / green push the picture around (`flowStrength`, `flowTiling`). |
| `diffuse`, `lumAlpha`, `rgbFromDc`, `mulDc` | GoF3's DiffuseColor: the picture's alpha times its brightness, the picture's colour replaced by `diffuse`, the picture times `diffuse`. |
| `opacity`, `alpha` | Its brightness (above 1 is allowed: GoF3 caps each pixel at white) and Alpha_Value. |
| `colorize`, `c1`, `c2`, `colorizeMul`, `colorizeVtx` | Colourising by the picture's red: 1 multiplies by `mix(c1, c2, red) x colorizeMul`, 2 tints (`rgb x (rgb + colour x (1 - rgb) x colorizeMul)`); `colorizeVtx` uses the vertex colour for `c1`. |
| `angle`, `angleMin`, `angleMax` | Fading by the angle to the camera: 1 how squarely the face is seen (either side), 2 seen from its back, 3 from its front, mapped `angleMin` (gone) to `angleMax` (full). |
| `pulsate`, `pulseMin`, `pulseMax`, `pulseFreq` | Pulsing: `|sin(time x pulseFreq)|` kept between the two. |
| `uvSpeed`, `scrollDirect`, `scrollFlow` | The picture or the flow map scrolling `uvSpeed` [u, v] a second. |
| `vertexAnim`, `vaFreq`, `vaStrength` | Vertices wobbling along their normals (by their vertex alpha). |
| `blend` | 2 additive (default), 1 alpha blended. |
| `tint` | Which of an exhaust style's two colours recolours it: `"main"` or `"second"` (default `"second"` for Galaxy on Fire 3's helix, a part named "...spiral", else `"main"`). |
| `intensity` | How much brighter than GoF3's formula an additive part is drawn, before each pixel is capped at white (default 4; the GoF3 Ships mod's Nivelian flare 1.5, plume 2, helix 5, Vossk flare 1.5, plume 5, the rest 4). |
| `anims` | `[ { "kind": "loop" \| "ignite" \| "off", "length": ms, "channels": [ { "name": "rotation.x".."w" \| "translation.x".."z" \| "scale.x".."z" \| "alpha", "keys": [ms, value, ms, value, ...] } ] } ]`: `loop` plays all the time (a spiral turning), `ignite` as the engines light and as a boost starts, `off` as they go out. |


#### Galaxy on Fire 3's trails, contrails and booster flash (`trailParticles`, `contrailParticles`, `boostParticles`)

Particle emitters as Galaxy on Fire 3's Fork Particle runtime runs them (the GoF3 Ships mod's importer reads them from the
game's `.psb` files, `tools/fork_psb.py`). Particles are born at `rate` a second, spread along the path the emitter moved
since the last frame (none while it stands still), and stay where they were born, so they lie along the wake; each is drawn as a quad facing the camera,
stretched along the wake, additively. `trailParticles` runs at every exhaust (in place of `trail`), `contrailParticles` at
the points in `contrails`, `boostParticles` at every exhaust only while boosting.

| Field | |
|---|---|
| `texture` | A PNG in the mod (read as stored). |
| `life` | `[min, max]` seconds each particle lives. |
| `sizeStart`, `sizeMid`, `sizeEnd`, `sizeMidTime` | `[min, max]` each, in the units of `meshScale`, the middle one at `sizeMidTime` of its life. |
| `colors`, `colorTimes` | Four colour keys (16 numbers, r g b a) at four shares of the life (`[0, a, b, 1]`). |
| `rate`, `max` | Particles a second; the most alive at once. |
| `local` | `true`: the particles ride with the ship instead of staying behind. |

`"contrails": [x, y, z, ...]`: the contrail points, game units from the ship's centre (as `position_engine`).

### Exhaust styles

`exhausts.json` offers engine effects to **every** ship: in the hangar's Customize screen (which every ship has while a mod
with exhaust styles is on) the player picks an exhaust for the ship they fly, the game's own ships included, and its two
colours. "Ship's own" keeps the model's exhaust.

```json
[
  { "id": "nivelian", "name": "Nivelian drive",
    "engineFx": { "meshes": "fx/engines/engine_nivelian.json", "meshScale": 53.5, "nozzle": 87 },
    "mainName": "Engine colour", "secondName": "Helix and trail colour" },
  { "id": "thruster", "name": "Thruster", "model": "exhausts/thruster.glb", "nozzle": 0.38,
    "materials": [ { "material": "EngineExhaust", "flame": true, "flameLength": 4, "emissionColor": [1, 0.29, 0.05],
                     "boostColor": [0.05, 0.53, 1], "emissionIntensity": 4 } ],
    "palette": [ { "id": "orange", "name": "Orange", "color": "#FF923F" }, { "id": "blue", "name": "Blue", "color": "#3FC0FF" } ],
    "mainName": "Main thrusters", "secondName": "Booster" }
]
```

| Field | |
|---|---|
| `id`, `name` | The style (builds name it `"mod_id:id"`). |
| `engineFx` | A Galaxy on Fire 3 engine, the fields of a ship's [`engineFx`](#engine-effect). Its two colours: every part but the helix, then the helix and the particles (each pixel takes the colour's hue and keeps its white, so the hot core stays white); "Original" keeps Galaxy on Fire 3's own. |
| `model`, `materials`, `yaw` | Or a flame model put at each exhaust: its origin at the nozzle's exit, the flame blowing along -Z (`yaw`: degrees about the up axis when it faces another way); `materials` as a ship's, a `"flame": true` entry for the flame cards (see [materials](#materials)). Its two colours: the flame and the boost cards. |
| `nozzle` | The nozzle radius the effect is made for: game units for `engineFx` (default its own `nozzle`, else 87), the model's units for `model` (default 1). Each exhaust of the ship is scaled by its own nozzle's size against it (a mod mount's `glowSize`, a mod ship's `engineGlowRadius`, the game's ships' exhaust sizes; on a ship with an `engineFx` of its own that gives a `nozzle`, its nozzles are taken as large as that `nozzle` x `fxScale`, so a ship wearing a style made like its own engine shows it at its own engine's size). |
| `palette` | The colours offered, `[ { "id", "name", "color": "#RRGGBB" } ]` (default the game's bright colour list). Each row also offers "Original", the style's own colour. |
| `mainName`, `secondName` | The two colour rows' names (default "Exhaust colour" / "Second colour"). |
| `group`, `groupName`, `scale` | Flame models of one mod with the same `group` are offered as one choice (named `groupName`, else the first one's name): each nozzle of the ship wears the one made for the nearest nozzle size, its `nozzle` x `scale` (the model's size as the mod's own ships show it: Unity metres per model unit, default 1). The EVERSPACE 2 importer offers its three thrusters this way. |

The style sits where the ship's exhausts are (`slotType` 3 mounts: a kit ship's build's, a mod ship's, the game's own) and takes
each nozzle's shape: its outline (the hull of the nozzle seen from behind) and centre. A flame model's cross-section is bent to
that outline (a square nozzle gets a square flame, a hexagon a hexagonal one), a GoF3 engine's parts are spread to it. Where the outline comes from: a mount's `"outline"` (below; only when it is at least half the
mount's `glowSize` and centred within 1.5 x it, else it is taken for stray lit pixels), else the ship's meshes whose material
name contains "nozzle", else a mount's `glowSize` (an oval), else the game's ships' own nozzles (measured from their engine
glow). It goes
with the engines: off while parked, mining or docking at an object, stronger with the throttle and the boost. The model's own
exhaust (glow, particles, flames, engine effect) is hidden meanwhile; its manoeuvring jets stay. An **Exhaust size** row
resizes it (50 % to 200 % of the fitted size, in 10 % steps) so it fits the ship's nozzles. A Galaxy on Fire 3 engine is
also drawn longer on a ship whose nozzles are small for its length (the game's own ships: the Phantom's are a third of a GoF3
hull's of its size), so its plume reads at the ship's scale. A flame model's boost cards are its
own mod's ships' boost jets: on the game's ships and other mods' ships they stay hidden, and a boost turns each nozzle's main
flame to the boost colour instead. While the Customize screen is open the engines run on the turntable, boosting while a
boost colour row is picked. A build may carry it (`"exhaust": "mod_id:id"`, `"exhaustColor"`, `"exhaustColor2"`: a palette id or
`"#RRGGBB"`, `"exhaustSize"`: 0.5 to 2); it is saved with the game and other players see it. A flame model style isn't
offered on its own mod's ships: it is cut from those ships' own exhaust parts, which they choose in their own rows. The Galaxy
on Fire 3 Ships mod offers GoF3's Nivelian (with its helix), Terran and Vossk drives with their trails and booster flashes,
and its Terran, Nivelian and Vossk capital ship drives; the EVERSPACE 2 importer three ES2 thrusters cut from rear parts.

### Changing ships

`override`: an original ship's number or another mod's `"mod_id:ship_id"`, with any of `armor`, `cargo`, `price`,
`priceDefault`, `slots`, `handling`, `name`, `description`, `mounts`, `flightStyle`, `flight` (see
[Flight styles](#flight-styles)), `sounds` (see [Ship sounds](#ship-sounds)), `camera`, `cockpit`, `smoke`,
`explosionScale` (see [Camera, cockpit, damage smoke and explosion](#camera-cockpit-damage-smoke-and-explosion)) (and for
mod ships `race`, `hangarHeight`).

**More (or fewer) weapons:** `slots` sets how many primaries, secondaries, turrets and equipment the ship takes (give only the
ones you change), and `mounts`
where its guns sit (the same list as a new ship's; it replaces the ship's whole list, exhausts included, so copy the ones you
keep). A ship with more weapon slots than gun mounts still fires every weapon: the extra ones share the mounts in turn. With
fewer slots, what no longer fits moves to the hold the next time the player docks.

```json
[ { "override": 10, "slots": { "primary": 3, "secondary": 2 } } ]
```

**Skins and weapon colours for any ship** (the game's own ships, and new ships built from one model): `"skins"` (the same list as
a kit's [skins](#customizable-ships), for this ship; no `types`) and / or `"weaponColors"` (a palette list of `{ "id", "name",
"color" }`, or `true` for the game's own colours) on the ship's entry give it the hangar's **Customize ship** screen: a Skin row
("Standard" is its own look) and the Weapon colour row. A skin names the ship's own materials: the game's ship materials are
called after their textures (`ship_010_terran_diffuse` for the Phantom's hull; find a ship's in the reference or by its
texture's name). Several mods may add skins to one ship (each its own ids). The player's choice is saved with the game; with
the mod turned off the ship is back to its own look. `engineColor` only works on customizable (kit) ships.

```json
[
    { "override": 10,
      "skins": [ { "id": "crimson", "name": "Crimson",
                   "materials": [ { "material": "ship_010_terran_diffuse", "diffuse": "skins/phantom_crimson.png" } ] } ],
      "weaponColors": [ { "id": "ice", "name": "Ice", "color": "#9FE8FF" } ] }
]
```

An override of an original ship can also give it a new **model**: `model` with `modelLength`, `modelYaw` (and `modelPitch`,
`modelRoll`, `modelOffset`), `materials`,
`engineGlowRadius` / `engineGlowColor` and the glows, as for a new ship. Every ship of that number then uses it (yours, the
NPCs', the hangar's). Its guns and exhausts stay at the original's mounts unless the entry has its own `mounts`.

Any turret item can arm the NPC ships of a type, the game's own included (the Veteran, 28, has one turret mount):

```json
[ { "override": 28, "npcTurrets": [ "my_mod:flak" ] } ]
```

```json
[
    {
        "override": 22,
        "model": "models/groza.glb",
        "modelLength": 1000, "modelYaw": 0,
        "materials": [ { "diffuse": "maps/groza_diffuse.png", "normal": "maps/groza_normal.png" } ]
    }
]
```

## NPC ships (npcships.json)

The ships nobody flies have a file of their own, as `ships.json` is for the ones a player flies: the capital ships,
freighters and bosses of the orbits' traffic. An entry adds one (`"id"`) or changes one of the game's own (`"override"`:
`"battleship"` the Terran battleship, `"carrier"` the Terran carrier, `"vossk_battleship"`, `"freighter"` every race's
freighters, `"pirate_boss"` the pirate events' boss) or another mod's (`"mod_id:id"`). The Mod Builder's **NPC ships** kind
edits them in space, the hull beside the orbit's station with its hardpoints and docking pads on it.

```json
[
    { "id": "dreadnought", "kind": "capital", "ship": "my_mod:dread_hull", "name": "Dreadnought", "race": "terran",
      "hullFactor": 2, "gunFactor": 1.5, "explosionScale": 14,
      "hardpoints": [ { "position": [0, 3200, 9000], "item": 48 }, { "position": [0, -3000, 4000], "upsideDown": true },
                      { "kind": "hangar", "model": "none", "position": [0, 0, -12000], "ships": [5], "every": 30 } ],
      "escorts": { "count": [3, 5], "ships": [5, "my_mod:interceptor"], "strength": 1.5 },
      "loot": { "items": [ { "item": 113, "amount": [6, 10] }, { "item": "my_mod:core", "chance": 25 } ], "cargoRolls": [1, 2] },
      "spawn": { "chance": 15, "races": ["terran", "nivelian"], "security": [2, 3], "minRank": 6, "distance": [5, 9] },
      "docking": { "pads": [ { "position": [0, 2600, -4000] }, { "position": [0, 2600, -7000] } ], "standing": 40,
                   "raceShip": false, "markup": 1.5, "energyCells": [20, 30], "gear": 3,
                   "sells": [ { "item": 37, "amount": [10, 20] }, { "item": "my_mod:core", "amount": 1, "chance": 50 } ] },
      "launches": { "ships": [5], "perWave": 3, "total": 12, "every": 25 } },
    { "id": "ore_hauler", "kind": "freighter", "name": "Ore hauler", "speed": 1.5,
      "escorts": { "count": 2 }, "loot": { "items": [ { "item": 116, "amount": [8, 15] } ] }, "spawn": { "chance": 40 } },
    { "override": "battleship", "spawn": { "chance": 50 }, "escorts": { "count": 4, "strength": 2 } },
    { "override": "freighter", "spawn": { "count": [2, 6] }, "loot": { "keep": true, "cargoRolls": 1 } },
    { "override": "pirate_boss", "name": "The Butcher", "ship": 45, "hullFactor": 1.5, "escorts": { "count": [4, 6] } }
]
```

- **`kind`** (new ones): `"capital"` (the default) parks in its orbit with its turrets armed, can be destroyed (its hull
  burns along its length, then a big blast; its turrets and parts go with it), carries a crate and fires missile salvos
  (`"missiles": false` not); `"freighter"` cruises through the orbit like the game's freighters (`speed` in units a
  millisecond, theirs 1); `"boss"` is a named fighter that comes for the player with its escorts, out where the pirate boss
  comes (hostile unless `"hostile": false`; its hull and gun grow with the rank like the pirate boss's).
- **The hull**: `"ship"`, a `ships.json` ship (a number or `"mod:id"`: a hull you made for it, which needs no dealer and is
  then never sold; for a capital ship or freighter also the game's 13, 14, 15), or `"looksLike"` a game NPC ship's hull
  (`"battleship"`, `"carrier"`, `"vossk_battleship"`, `"freighter"`: the race's). Without either: the kind's game hull.
- **`scale`**: its size, x the hull's (1; 0.05 to 10). The model, its collision boxes, its hardpoints, the game hull's turrets
  and its docking pads all grow with it, so positions stay in the hull's own size: a carrier at `"scale": 2` keeps its
  pads where they were on the deck. In the Mod Builder: Size, and "Compare with…" to park another hull beside it.
  An override's `"ship"` gives the game's ship your hull (the game's turrets and pads then leave it: give it hardpoints and
  pads of its own).
- **`name`** (or per language, `npcships.<id>.name` in `text/<lang>.json`) is on its lock plate. **`race`**: a game race
  (`"terran"`, `"vossk"`, `"nivelian"`, `"midorian"`, `"pirates"`, `"void"` or 0-9) or a mod race; without one, the system's.
  **`"hostile": true`** makes it always the player's enemy.
- **`hull`** sets its hit points (x the difficulty), or **`hullFactor`** multiplies its default (a new capital ship: 5 x the
  Terran battleship's, as the enhanced battleship has; a freighter: the freighter's; a boss: the pirate boss's).
  **`gunFactor`** multiplies its guns' and turrets' damage. A game capital ship given a hull (or a ship) can be destroyed.
- **`hardpoints`**: its turrets and other parts, in the format of `ships.json`'s `"subsystems"` (above) with `"position"`
  (game units in the ship's frame), `"kind"` `"turret"` by default and a turret without an `"item"` the game's Terran
  turret. They replace the hull's own turrets (the game hull's, or a ship's `npcTurrets`); a freighter carries them along.
- **`escorts`**: `count` (a number or `[least, most]`; without it, a capital ship's 3 + rank / 7, a freighter's 2), `ships`
  (without them, the dearer half of its race's fighters), their `race`, and `strength` (their hull and guns x it; or `hull`
  and `gun` apart). A capital ship's loop round it, a freighter's follow it in formation and fight what attacks it, a boss's
  fly with it. On a game ship they replace the game's escorts.
- **`loot`**: `false` for nothing, or `items` (`amount` a number or `[least, most]`, `chance` in %, 100 by default) and
  `cargoRolls` (a freighter's cargo rolls); `"keep": true` adds them to the game's own loot. Every kind of loot drops a crate
  of its own.
- **`spawn`** (new ones): `chance` (% of the orbits where the rest holds; 0 or none = never: the Debug page's **Spawn mod NPC
  ship** or a mission's `spawntype` step (`/spawn type mod_id:id`) brings it), `count` (how many when it comes, 1 by default), `races` (the system's race), `systems`, `stations`,
  `security` (0 Dangerous .. 3 Secure), `minRank` / `maxRank`, `minStep` / `maxStep` (story steps), `freePlayOnly` /
  `storyOnly`, `condition` (an expression like a mission's, see "Conditions"), and where: `"position": [x, y, z]` (game units
  from the station, z ahead) or `"distance": [least, most]` (km in front of the station); else its kind's place (a capital
  ship 4-8 km ahead, clear of the station; a freighter in the freighters' box; a boss where the pirate boss comes). A capital
  ship's `"replaceGame": true` comes only where the game's capital ship would, in its place. Never in the story's special
  orbits, the pirate havens or Loma. On the game's ships: `chance` replaces the 30 % of the orbits with freighters that get
  the battleship, the carrier (once the battleship came) or the Vossk battleship; the freighters' `count` replaces their 0-4
  an orbit; their races and places stay the game's.
- **`docking`** (capital ships): it takes ships aboard to resupply them and sells from its shop, like the enhanced carrier.
  `pads` (each `position`, its `approach` point, by default 2000 units above it, and `facing`, the docked ship's nose,
  forward by default; without pads the carrier and the Vossk battleship keep their game pads, another hull has none and no
  dock); who it takes: `standing` (the least standing with its race, -100..100, a friend from 71: the default), `raceShip`
  (only in a ship of its race; true by default), `signature` (its race's signature lets anyone in; true); its shop: `repair`
  (hull and armour, true) at `repairPrice` credits a point (10), `markup` (x the orbit station's prices, 1.25), `energyCells`
  and `ammo` (rounds of each mounted secondary of its race) in stock, `gear` (how many of its race's weapons and equipment;
  the game's 5 / 6 by default) and `sells` (any items: `item`, `amount`, `chance`). Not while it is hostile or in a battle.
- **`launches`** (capital ships): `ships` it launches from its pads (else from the top of its hull) when attacked, `perWave`
  every `every` seconds, `total` in all (5 / 20 s / 15 by default). A `"hangar"` hardpoint launches on its own while it is
  hostile.
- `"retreat": true` (capital ships): it jumps away instead of being destroyed, as `ships.json`'s.

Game NPC ships keep everything an override leaves out, so a mod that changes only the battleship's chance changes nothing
else. In multiplayer the orbit's authority builds them and the other players see copies (their docking is only tested in
single player).

## Customizable ships

A ship can be built from parts the player picks in the hangar (**Customize ship**, under Inspect ship), instead of one model.
The parts are a **kit** in `shipkits.json`; the ship names its kit, its type and its default build in `ships.json`.
The Mod Builder's **Ship kits** kind makes both without JSON (and a kit ship's type and default build are on its Ships page).
A kit without parts yet is left out with a warning, so a mod still loads while you are making it.

```json
[
    {
        "id": "medium",
        "name": "Medium hulls",
        "slots": ["body", "rear", "cockpit", "wings"],
        "scale": 1.0,
        "parts": [
            { "id": "body_a", "slot": "body", "name": "A", "model": "parts/body_a.glb" },
            { "id": "body_b", "slot": "body", "name": "B", "model": "parts/body_b.glb" },
            { "id": "rear_1", "slot": "rear", "name": "1", "model": "parts/rear_1.glb" },
            { "id": "bubble", "slot": "cockpit", "name": "Bubble", "model": "parts/cockpit_1.glb" },
            { "id": "hawk", "slot": "wings", "name": "Hawk", "model": "parts/wings_hawk.glb", "types": ["striker"],
              "tiers": ["parts/wings_hawk_t1.glb", "parts/wings_hawk_t2.glb"] },
            { "id": "reaver", "slot": "wings", "name": "Reaver", "model": "parts/wings_reaver.glb", "types": ["sentinel"] }
        ]
    }
]
```

| Kit field | |
|---|---|
| `id` | **Required.** The kit's id; ships name it `"kit": "<id>"` (your mod's) or `"mod_id:kit_id"` (another mod's). |
| `slots` | The build's slots, in the order the parts are put together. Default: every slot the parts use. |
| `scale` | Unity metres per model unit (glTF's metres: 1). One scale for the whole kit: the parts are made to fit together at one origin, so every build has the same size (not stretched to a `modelLength`). |
| `yaw` | Degrees about the up axis when the parts' nose doesn't face +Z. |
| `materials` | Like a ship's `materials`, applied to every part's meshes they name. |
| `parts` | Each `{ "id", "slot", "name", "model" }`, optionally `"mounts"` (the part's own weapon, turret and exhaust mounts, like a ship's `mounts`), `"tierMounts"` (a list of mount lists, one per tier extension), `"stats"` and `"slots"` (see below): a part's id (unique in the kit), its slot, its name in the Customize screen (a string or per language), its glTF / GLB. `types`: the ship types that may fit it (a ship's `kitType`; none = every type: a body any ship of the kit can take). `tiers`: extension meshes shown at build tier 1, 2, ... (EVERSPACE 2's wing extensions); the tier row cycles from the base (0) to the kit's highest. `"anchors": { "cockpit": [x, y, z] }`: named points this part offers (model units, the GLB's own coordinates); `"anchor": "cockpit"`: this part and its tier models sit on that point of the build's part that offers it (a canopy on each body's own cockpit spot). |

All the parts of a kit share one origin: export them in place, as they sit on the assembled ship. Only the parts a build uses
are loaded (the ships' default builds and the player's builds at the start, others when the player first picks them), so a
big kit costs little memory; still, keep each part's triangle count modest for phones.

**Stats and slots.** Parts can change the ship: `"stats": { "armor": 8, "cargo": -10, "handling": 3, "equipment": 1, "topSpeed": 4,
"turnRate": -5, "acceleration": 6, "strafeSpeed": 0 }` (hull, cargo and handling in % of the ship's own; equipment slots; flight
bonuses in % in both flight styles) and `"slots": { "primary": 3, "secondary": 2, "turret": 1 }`. A build adds up its parts'
stats; for each weapon slot type it has the sum of its parts' slots, or the ship's own count when none of its parts gives that
type. The hull, cargo, slots and flight of the player's ship follow its build, the hangar's stat rows show it, and the
Customize screen shows the result live; applying a build with fewer slots moves what no longer fits to the hold. The tier is
cosmetic. Keep the changes small (a few %) so every build stays close to the ship's own numbers.

**Colours.** A kit can also let the player paint the ship: palettes of named colours, colour slots that recolour materials,
and preset schemes that set every slot at once. The Customize screen then shows a Colour scheme row and a row per slot.

```json
"palettes": {
    "main": [ { "id": "white", "name": "Snow White", "color": "#F9F9F9" }, { "id": "jet", "name": "Jet", "color": "#383838", "metallic": 0.8, "smoothness": 0.6 } ],
    "lights": [ { "id": "amber", "name": "Amber", "color": "#FFC059" } ]
},
"colorSlots": [
    { "id": "hull1", "name": "Main colour", "palette": "main", "default": "white", "targets": [ { "material": "Paint1" } ] },
    { "id": "lights", "name": "Lights", "palette": "lights", "default": "amber",
      "targets": [ { "material": "Glow", "property": "emission", "intensity": 4 } ] },
    { "id": "engines", "name": "Engines", "palette": "lights", "targets": [ { "property": "engine" } ] }
],
"presets": [ { "id": "racer", "name": "Racer", "colors": { "hull1": "#CB7800/0/0.5", "lights": "amber" } } ]
```

| Field | |
|---|---|
| `palettes` | Named lists of colours: `id`, `name` (string or per language), `color` (`#RRGGBB`), optionally `metallic` and `smoothness` (0..1). |
| `colorSlots` | What the player paints: `id`, `name`, the `palette` it picks from, its `default` colour, and `targets`. A target recolours the materials whose name contains `material` (the model's own material name, or a kit `materials` entry's `material` filter): `property` `"base"` (the default: the base colour, x `intensity`, and the colour's metallic / smoothness), `"emission"` (the glow colour x `intensity`), `"boost"` (an engine flame's boost colour x `intensity`: its boost cards; the manoeuvring jets' material is named `lateral thruster`, and their glow discs follow it) or `"engine"` (the engine glow, no `material`). A **strength slot** has no palette: `{ "id", "name", "steps": [0, 0.25, 0.5, 0.75, 1], "default": "0.5", "scales": "<colour slot id>" }`; the player steps through `steps` and the value scales that colour slot's emission targets (EVERSPACE 2's Emissive 3 intensity slider). Presets give it a number as text. |
| `presets` | Colour schemes: `id`, `name` and `colors` (slot -> a palette id, or `"#RRGGBB"` with an optional `/metallic/smoothness`). |

**Skins.** Paint jobs made of textures instead of colours (Galaxy on Fire 3's paint schemes): the Customize screen then has a
Skin row ("Standard" is the model's own look, then the skins the ship's type may wear).

```json
"skins": [
    { "id": "gold", "name": "Gold", "types": ["groza"], "engineColor": [1, 0.85, 0.6],
      "materials": [
        { "material": "groza_body", "diffuse": "skins/groza/gold_basecolor.jpg", "emission": "skins/groza/gold_emissive.jpg",
          "metallicSmoothness": "skins/groza/gold_metallic.png" },
        { "material": "groza_glass", "diffuse": "skins/groza/gold_glass.png" } ] }
]
```

| Field | |
|---|---|
| `id`, `name` | The skin's id (not `none`) and its name in the Customize screen (a string or per language). |
| `types` | The ship types that may wear it (a ship's `kitType`; none = every type of the kit). |
| `materials` | Per material whose name contains `material` (the model's own glTF material name, or part of it), the textures that replace its own: `diffuse` (base colour), `emission`, `metallicSmoothness` (URP Lit's layout: R metallic, A smoothness; not glTF's metallic-roughness), `specularGlossiness` (RGB specular, A smoothness: on a material in the specular workflow), `normal`; `color` / `emissionColor` (`[r, g, b]`, 0 to 1) and `emissionIntensity` too. A field left out keeps the model's own; a material no entry names is untouched. PNG or JPG. |
| `engineColor` | `[r, g, b]`: every exhaust's glow in this skin (otherwise the mounts' own `glowColor` / the ship's `engineGlowColor`). |
| `colors` | A colour scheme the skin brings along: a preset's id, or `{ "slot": value }` like a preset's `colors`. Picking the skin sets them; the colour rows can change them after. |
| `livery`, `decal` | A livery's / decal's id (below) the skin brings along. |

**Liveries.** A pattern projected onto the hull from above, like EVERSPACE 2's projection decals: a greyscale mask over the
ship's length and width, mirrored at the middle, that tints the base colour (keeping the texture's shading) and changes the
metallic and smoothness where it is white. It shows on the materials marked `"livery": true` (a kit `materials` entry), and
the Customize screen gets a Livery row ("None", then the liveries the ship's type may wear).

```json
"liveries": [ { "id": "blossom", "name": "Nebula Blossom", "texture": "liveries/blossom.png", "color": "#FFF5F4",
                "metallic": 0, "smoothness": 0.4, "opacity": 1, "mirror": [false, false], "scale": [1, 1], "offset": [0, 0],
                "tiling": false, "clipAngle": 90 } ],
"liveryAxes": { "forward": "x", "right": "z", "up": "y" }
```

| Field | |
|---|---|
| `texture` | **Required.** The mask (PNG; white = the livery). Its middle is the ship's middle, the nose to the image's right. |
| `color`, `metallic`, `smoothness` | What the masked area becomes. A colour slot with `"property": "livery"` lets the player pick the colour. |
| `opacity`, `mirror`, `scale`, `offset`, `tiling` | How strongly it shows; mirrored at the middle per axis; scaled and moved over the ship; repeated outside the image. |
| `clipAngle` | Faces steeper than this from horizontal get none (90: every face). |
| `liveryAxes` | The parts' axes (glTF's, signed: `"-x"`): forward, right and up. Default EVERSPACE 2's export. |

**Decals.** Two masks for the parts' decal faces, the insignia and the type lettering (EVERSPACE 2's ship decals): R colour 1,
G colour 2, B the border colour, A where it shows. They draw on the materials marked `"decal": "insignia"` or `"decal": "type"`;
their colours are colour slots with `"property": "decal1"`, `"decal2"` and `"decal3"`. The Customize screen gets a Decal row.

```json
"decals": [ { "id": "ramen_cat", "name": "Ramen Cat", "insignia": "decals/ramen_cat_insignia.png", "type": "decals/ramen_cat_type.png" } ]
```

**Engine trails.** EVERSPACE 2's customization trails behind the player's own engines and boosters: the Customize screen gets
an Engine trail and a Booster trail row. The trails are EVERSPACE 2's own particle systems rebuilt in the game, so a kit can
only pick from them (`kind`) and give their data; the ES2 Ships importer writes all thirteen from your copy of the game.

```json
"trails": [ { "id": "glitter", "name": "Stardust", "kind": "glitter", "icon": "trails/glitter_icon.png",
              "curves": [[1, 1.006, 0.95, 0.395, 0]], "texture": "trails/T_Pattern_Barcode_001.png" } ]
```

| Field | |
|---|---|
| `kind` | `default` (the plain veil), `glitter`, `smoke`, `bubbles`, `sparks`, `darktrail`, `firetrail`, `lines`, `lightning`, `rings`, `squares`, `triangles` or `veils`. |
| `curves` | The system's curve tables over a particle's life (0 to 1), in this order: glitter [alpha]; smoke [size, alpha]; bubbles [colour, size, alpha]; sparks [size, unused]; darktrail, firetrail, lines [alpha]; lightning [ribbon alpha, arc size, arc alpha]; rings [alpha, scale]; squares, triangles [alpha, unused]; veils [first veil alpha, second veil alpha]. A missing curve counts as 1. |
| `texture`, `mask`, `normal` | The textures the trail's material samples (the masks as sRGB images, `normal` a normal map): glitter and lightning the barcode, smoke its colour (`texture`), puff (`mask`) and sphere normal, bubbles the sphere normal, darktrail and firetrail T_Noise_020, lines T_Noise_012. |
| `mesh` | The rings / squares / triangles cross section: `positions`, `normals` (3 numbers each), `uv` (2) and `indices`, in EVERSPACE 2's centimetres and axes (x forward, y right, z up). |

A build picks them with `"trail"` and `"boostTrail"` (ids; none = the plain veil). Only the player's own ship shows them; the
Customize screen shows them on the turntable while its engines run (the booster trail while its row is selected).

**Weapon colour.** Every customizable ship also has a Weapon colour row in the Customize screen: "Original" (the weapons'
own look) or one colour for all its energy weapons: the lasers and beams, blasters (EMP ones too), thermo guns, and the turrets
that fire energy bolts (Hammerhead D1 / D2A2, L'ksaar, Matador TS), with their muzzle flashes, impacts and thermo trails. The
auto-cannons, railguns, mass drivers, scatter guns and cannon turrets keep their own look; mod items count as the item they
are based on. The colours are the game's own list unless the kit has a palette named `"weapons"` (`"palettes": { "weapons":
[ ... ] }`, the same entries as any palette). A default `build` may set one (`"weaponColor": "cyan"` or `"#RRGGBB"`). Other
players see your colour in multiplayer.

A skin changes only the look, never the stats. Its textures load when a build first shows it (the default builds' and the
player's at the start). A default `build` may name one (`"skin": "gold"`). Colour slots still apply on top of a skin.

A ship's default `build` can give its colours too: `"build": { "wings": "reaver", "colors": { "hull1": "jet" } }`. To paint parts of
a mesh separately, give them their own materials in the model (e.g. "Paint1", "Paint2").

In `ships.json`, a new ship with `"kit"` instead of `"model"`:

```json
{ "id": "nemesis", "name": "Nemesis", "race": 0, "armor": 420, "cargo": 50, "handling": 110, "price": 420000,
  "slots": { "primary": 2, "secondary": 2, "turret": 0, "equipment": 10 },
  "kit": "medium", "kitType": "sentinel", "build": { "wings": "reaver", "body": "body_a", "tier": 1 },
  "mounts": [ ... ] }
```

| Field | |
|---|---|
| `kit` | The kit it is built from. |
| `kitType` | **Required** with a kit: which parts it may fit (a part's `types`). |
| `build` | Its default build: a part id per slot and `tier`. Dealers sell it so and NPC pilots fly it so. A slot left out takes the first part the type may fit. |

Everything else is a normal ship's (stats, `mounts`, `icon`, glows, `lounge`, `dealer`). Mounts can come with the parts: a build
fires from, and glows at, its parts' mounts of each slot type (with the tier extensions' at that tier), and uses the ship's own
`mounts` only for a slot type none of its parts has. A primary or secondary mount may carry `"rotation"`: how its weapon model is turned on the hull, in the ship model's frame (x
right, y up, z forward), as Euler degrees `[x, y, z]` or a quaternion `[x, y, z, w]` (a gun on a slanted wing); the shots
still fly along the nose, from the turned model's muzzle. `"nodes": ["<node name>", ...]` names parts of the ship's own
model (a hardpoint's pylon) that show only while that mount carries a weapon: with two guns mounted, only the first two
mounts' pylons show. So wings can carry their guns, rears their exhausts, and a turret socket
comes with the wing set that has one (give the ship as many turret slots as its default build has turret mounts).
Mark a part's turret mounts `"builtIn": true` when the part's model already shows the turret: a turret item mounted in that
slot then puts no model of the game's turrets on the ship (in space, in the hangar or on other players' screens) and keeps
only what it does: its gun, damage, reload, shots and sound, auto aiming or the turret view, the plasma collector's pull and its stream (the game's stream effect, pouring from the
mount point and turning with the aim).
Its shots leave from the mount point (a little ahead of it) and the turret view looks from just above it.

**Weapon and turret models** (`"hardpoints"` in a kit): the kit can show the mounted weapons and turrets with models of its
own, at the kit's scale and yaw, with the kit's materials:

```json
"hardpoints": {
  "turret": "equipment/turret.glb",
  "toggle": true,
  "weapons": [
    { "slot": "primary", "model": "equipment/beam_laser.glb", "items": [9, 10, 11] },
    { "slot": "primary", "model": "equipment/blaster.glb", "categories": [1] },
    { "slot": "primary", "model": "equipment/pulse_laser.glb" },
    { "slot": "secondary", "model": "equipment/pod.glb",
      "sound": { "shot": { "files": ["audio/launch_1.ogg", "audio/launch_2.ogg"], "volume": 0.5, "pitch": [0.95, 1.05] } } },
    { "slot": "turret", "model": "", "categories": [35] },
    { "slot": "turret", "model": "equipment/coil_gun.glb" }
  ]
}
```

- `turret`: the model a turret item shows on the build's `builtIn` turret mounts, instead of nothing. It needs a node named
  `pivot` (it turns sideways) with a node under it whose name contains `gun` (it tilts up and down; the barrel along the
  parts' nose), and may have a `muzzle` node (where the shots, and a plasma collector's stream, leave). Without a `muzzle`
  the turret's weapon model (below) sits on the `gun` node and its own muzzle is the turret's: a plain swivel can carry
  whichever gun the turret item maps to. Leave the pivot and
  gun nodes unrotated. It shows in flight, in the hangar and on other players' ships; the turret item still decides how it
  fires.
- `weapons`: for each mounted weapon the first rule of its slot that matches gives its model. `items` matches item numbers
  (also mod items based on them) or `"mod:id"` keys, `categories` item category numbers (see the reference); a rule with
  neither is the slot's default, and `"model": ""` shows nothing. A primary / secondary model's origin goes on its mount (the
  n-th primary weapon on the n-th primary mount), and a node named `muzzle` in it moves that gun's shots to its muzzle. A
  turret weapon goes on the turret's gun with its muzzle on the turret's `muzzle` node. The weapon models show on the player's
  ship in flight and in the hangar, and on other players' ships.
- `sound` (optional, on a rule): the weapon's sound on this kit's ships instead of the item's own. `shot` plays with every
  shot (a list of files: one at random; a `pitch` range: a random pitch), `loop` loops while the trigger is held (a beam),
  with `shot` then played once as it starts, and fades out over `release` ms (default 100) when let go. Each is a sound as in
  [Ship sounds](#ship-sounds) (`volume` a number; the game's shots play at 0.7). Other players hear it on your shots too.
- `toggle` (optional, default false): the hangar's Customize screen gets a **Weapon models** row, Shown / Hidden. Hidden,
  the ship shows none of these models, no turret model and none of its mounts' `nodes` (the pylons): a bare hull, for kits
  whose weapons are an addition to models that had none. The weapons still fire, from the mounts themselves, and the turret
  works as on a `builtIn` mount without a model. The choice is part of the build (text `weapons=off`); a ship's default
  `"build"` may start hidden with `"weapons": false`. Other players see the choice.

In the hangars a customizable ship floats at the height its build needs: its lowest point, weapon models hanging under it
included, 1.5 m above the pad (its `hangarHeight` isn't used), so no build cuts into the floor.

The player's build of each customizable ship (parts, tier, skin and colours) is saved with the game (one per ship type, also for a
hull kept in the Kaamo Club) and shown in the hangar and in flight. Changing it is free. In multiplayer the other players see
it too, in space and parked in their hangars (parts their game hasn't loaded yet load first; the default build shows
meanwhile).

## Flight styles

The remake has two ways to fly the player's ship: the **original** (a throttle, the ship always flying forward) and **free
flight** (the EVERSPACE 2-style option under Options > Controls: held thrust, strafe and up / down, a held boost, inertial
dampeners). A mod decides for itself whether its content touches one, the other or both.

**Items** (their `stats`, see [Stats](#stats)): a booster's and a Steering Nozzle's own stats (`boostSpeed`,
`boostRechargeMs`, `boostDurationMs`, `agility`) count in both styles unless `flightScope` keeps them to one. On top of that,
any item (an engine part, a module, a booster) can carry percentage bonuses for both styles (`topSpeed`, `turnRate`,
`strafeSpeed`), for the original only (`originalTopSpeed`...) or free flight only (`freeTopSpeed`, ..., `freeAcceleration`), and
`freeBoostFactor` sets free flight's boost outright (by default free flight takes the booster's original boost speed x 3.2 / 3:
Linear and Cyclotron x1.6, Synchrotron x2.67, Me'al x3.2, Polytron x4.27). The bonuses of every mounted item and of the ship
add up; no total goes below -90 %.

```json
[
    { "id": "afterburner", "base": 74, "name": "Vortex Afterburner",
      "stats": { "boostSpeed": 200, "boostRechargeMs": 14000, "boostDurationMs": 7000, "freeBoostFactor": 450 } },
    { "id": "gyro_vanes", "base": 77, "name": "Gyro Vanes",
      "stats": { "agility": 40, "flightScope": 1, "freeTurnRate": 15, "freeAcceleration": 30 } }
]
```

The Gyro Vanes turn the original flight through the agility (like any Steering Nozzle) and free flight through their own
bonuses instead.

**Ships** (`ships.json`, a new ship or an `override`):

```json
{ "override": 10, "flightStyle": "free",
  "flight": { "turnRate": 10, "original": { "topSpeed": 15 }, "free": { "acceleration": 25, "boostFactor": 380 } } }
```

| Field | |
|---|---|
| `flightStyle` | `"free"` or `"original"`: this ship always flies so, whatever the player's option (`"player"` or no field: the player's). |
| `flight` | Bonuses in % for both styles: `topSpeed`, `turnRate`, `strafeSpeed`, `acceleration` (free flight only: the original has none), `boostFactor` (free flight's boost in hundredths of the top speed), `fullStopDamage` and `lowSpeedResistance` (EVERSPACE 2's ship passives, see the [stats](#stats) 138 / 139: the guns hit harder while the ship stands still, it takes less once it has flown slower than 30 m/s for a second). `original` / `free` inside it: the same, for one style only. |

**Who decides the style**, first match: an event or mission (the **Set Flight Model** node, `/flightstyle`), the ship flown,
the mod campaign (`campaign.json`'s `"flightStyle"`), a new-game option that is on (`gameoptions.json`'s `"flightStyle"`), then
the player's own option. The Options page says so while a mod decides, and the key layout follows the style in use. Touch,
tilt and VR always fly the original. Missions can ask with `freeflight` (1 in free flight, 0 in the original).

In the original a top speed bonus speeds the boost up too, and a turn rate bonus keeps the time the ship takes to reach its
turn rate. Free flight's top speed, turn rate and acceleration otherwise come from the ship's handling (with its Steering
Nozzle and Kaamo Club upgrades), its boost from the booster.

## systems.json and stations.json

New star systems and their stations. Both are lists: `"id"` adds one, `"override"` changes one.

```json
[
    {
        "id": "kepler",
        "name": "Kepler",
        "race": 0,
        "security": 2,
        "visible": true,
        "position": [8, 27, 83],
        "sky": 4,
        "gates": [4, 17],
        "gateStation": "frontier_systems:rho_freeport",
        "sunTexture": "textures/kepler_sun.png",
        "sunColor": [1.0, 0.8, 0.55],
        "skybox": { "nebula": "sky/kepler_nebula.png", "stars": 1, "nebulaBrightness": 0.8 }
    }
]
```

```json
[
    {
        "id": "kepler_prime", "system": "frontier_systems:kepler", "name": "Kepler Prime", "techLevel": 7, "planet": 12,
        "looksLike": 40,
        "model": "models/kepler_station.glb", "modelSize": 50000, "collision": "box",
        "interior": "nivelian",
        "planetTexture": "textures/kepler_prime_planet.png",
        "mapTexture": "textures/kepler_prime_map.png", "mapRing": true
    },
    { "id": "rho_freeport", "system": "frontier_systems:kepler", "name": "Rho Freeport", "techLevel": 4, "planet": 6, "looksLike": 41 }
]
```

### Systems

| Field | |
|---|---|
| `id`, `name` | As for items. |
| `race` | Who owns it: 0 Terran, 1 Vossk, 2 Nivelian, 3 Midorian, or a [mod's race](#races) (`"guild"` or `"mod_id:guild"`). It sets the hangars, bars, music, traffic, shops and the jumpgate (a mod's race: those of its `look`). |
| `traffic` | `"mod"`: only the mods' ships fly here. No game freighters (the mods' freighters of the system's race take their place, or none), no pirate outposts or bosses, and the raiders are the system's `raiders`. Default: the game's traffic. |
| `locals` | The races (the game's or mods') whose patrols fly here, e.g. `["guild", "allies"]`. Default: the system's `race`. |
| `raiders` | The races (the game's or mods') that raid it, e.g. `["gang"]`. Default: the game's pick (pirates, or the system race's enemy). |
| `pirateHeld` | `true`: pirates hold every station of the system (a station's `orbit` can say otherwise), see the orbit's `pirateHeld`. |
| `security` | 0 (lawless) to 3 (secure): how many raiders show up. |
| `visible` | On the star map from the start (default true). A hidden system can't be reached yet. |
| `available` | A condition (the expressions of [Conditions](#conditions)) for a system that comes and goes: while it holds the system is on the star map, while it doesn't it is hidden, whatever was revealed, with its routes and jumps. A player inside when it disappears is told so and, 4 seconds later, taken to the nearest system on the map. For example `"available": "quest(smuggler_run) == 1"`: only while that story mission or bar mission is under way. |
| `position` | `[x, y, z]` on the star map. The original systems lie between 15 and 94 (x), 2 and 96 (y) and 10 and 90 (z). |
| `sky` | Which of the original skies (nebula and sun) it uses, 0 to 18. |
| `gates` | The systems its jumpgate reaches (numbers, or `"mod_id:system_id"`). Routes work both ways: the other system gets the route back. |
| `gateStation` | The station orbit where the jumpgate is. Default: the system's first station. |
| `sunTexture` | Its own sun: a PNG like the game's suns (512 x 512, a small bright core and a dim glow fading to black well inside the edge, on black; it is drawn additively). Default: the sun of its `sky`. |
| `sunColor` | `[r, g, b]` from 0 to 1: the colour and strength of the sunlight on ships and stations. Default: the light of its `sky`. |
| `skybox` | Its own sky, see [Skies](#skies). Default: the nebula of its `sky` and the game's stars. |
| `jumpgate` | Its own jumpgate model in its gate orbit: `{ "model": "models/gate.glb", "size": 15000, "yaw": 0, "radius": 7500, "materials": [...] }`. `model` a glTF / GLB in the mod (like a station's) or a game model (`"gof2:jumpgate_vossk_anim"`); `size` its largest extent in game units (default 15000; the game's gates are about 15000, the Vossk one 22500); `yaw` degrees about its up axis (ships fly through it along its front, the model's +Z); `radius` how near a ship must come to jump, and its collision sphere (default half its size); `materials` like [ships.json's](#materials). Default: its race's gate. A gate of your own has no jump flash (the game's gates play one as a ship goes through). |
| `spaceMusic`, `stationMusic`, `battleMusic` | A mod's track (see [Music](#music)); `battleMusic` one or three. |

Changing a system (`"override"`): `name`, `security`, `visible`, `available`, `sky`, `sunTexture`, `sunColor`, `skybox`, `jumpgate`, the music, and `gates` (added to its routes; links original systems too).

Events and missions can also show or hide a system themselves: the `system` step (Show / hide a system), the graph's **Show /
Hide System** node, or the command `system [players] <system> <show | hide | reset>` (the system by number, name or
`"mod_id:system_id"`). It holds, and is saved, until the same system is reset, and it wins over `available`. A mission that
shows a system for its mission hides (or resets) it again in its last steps, including the failure branch.

### Skies

The game's sky is two layers added together: the stars and a nebula. A system's `skybox` replaces either with your own image:

| Field | |
|---|---|
| `nebula` | The nebula image (PNG / JPG). |
| `stars` | A star image of your own, one of the game's three star layers (`0`, `1`, `2`), or `"none"` (when your nebula image has its stars painted in). Default: the system's usual stars. |
| `nebulaBrightness`, `starsBrightness` | Multipliers, default 1. |
| `rotation` | `[x, y, z]` degrees: a fixed turn of the sky. Without it the sky is turned per station like the game's own (each orbit sees it from another angle). |
| `sunAligned` | `true`: in every orbit the sky is turned so its top (+Y, the panorama's top edge, the strip's up face) points at that orbit's sun, the way the game turns the supernova's sky. Paint the glow or the bright side of your nebula at the top and it always lies behind the sun, whatever planets the stations have. With `rotation` too, that turn is applied to the image first (it turns the picture about its top). |

An image is either:

- a **panorama** (2:1, equirectangular, as space art tools and the NASA star maps export it): its middle is straight ahead,
  its top straight up. It must wrap: the left and right edges meet behind you.
- a **strip of cube faces** (6:1): right, left, up, down, front, back from left to right, each seen from inside facing it;
  the up face has the front at its bottom edge, the down face has it at its top edge (the usual skybox tool layout).

The sky fills the whole screen, so it needs pixels: 8192 x 4096 for a panorama (or six 2048 px faces) looks sharp on a
1080p screen, 4096 x 2048 is soft but much smaller. Both layers are added (light on black), like the game's own: paint a
nebula on black, not on a dark blue. The sky also lights the scene a little (its average colour is the ambient light).

### Stations

| Field | |
|---|---|
| `id`, `name` | As for items. Translations: `stations.<id>.name` (systems: `systems.<id>.name`). |
| `subtitle` | A short description shown after its name: under its planet on the star map, in the map's lore panel and in the station ticker (`"Jump Gate & Trading Station"`). Translations: `stations.<id>.subtitle`. |
| `description` | Its lore: shown in a panel when its planet is picked in the star map's system view, and up to three stations' lore of the docked system runs in the station ticker. Translations: `stations.<id>.description`. |
| `system` | **Required.** Its system (a number or `"mod_id:system_id"`). A system holds at most 7 stations. |
| `techLevel` | 0 to 10: which items its shop sells. |
| `planet` | The planet it orbits: one of the game's planet textures, 0 to 22 or 24 to 26. |
| `looksLike` | An original station whose model and docking shape it uses. Default: the first original station of its system's race. With a `model` it stands in while the model loads, or when it can't be read. |
| `model` | Its own 3D model: a glTF / GLB file in the mod (made like a ship's, see [Ship models](#new-ships)), or one of the game's (`"gof2:station_078_midorian"`, as made unless `modelSize` is given). Its first animation (turning rings, sliding arms) plays on a loop, like a ship's. |
| `modelSize` | The model's largest extent in game units (20 units = 1 m). Default 40000 (2 km); the original stations are 20000-100000. |
| `modelYaw` | Degrees to turn the model about its up axis. A launch leaves along the station's front (the model's +Z after the turn, toward the orbit's planet); arrivals come from there too. |
| `modelCentre` | `true` (default): the middle of the model is the station's centre; `false`: the model's own origin is. |
| `materials` | Replace the model's materials on parts it names, like [ships.json's materials](#materials). |
| `collision` | What ships bump into: `"box"` (default, the model's bounds), `"sphere"`, or `"none"`. The player's ship collides with your model's real shape (its meshes, except additive / glow materials); the box or sphere is what NPC fighters steer around. `"none"` turns both off. |
| `volumes` | Instead of `collision`, your own shapes in game units around the station's centre: `[{ "box": [x, y, z, half x, half y, half z] }, { "sphere": [x, y, z, radius] }]`. Boxes stay axis-aligned, like the originals'. |
| `interior` | The hangar and bar you see when docked: `"terran"`, `"vossk"`, `"nivelian"`, `"midorian"` (or 0-3). Default: those of its system's race. |
| `hangar`, `bar` | Your own hangar / bar room from `interiors.json` (see [Hangars and bars](#hangars-and-bars)): its id, or `"mod_id:interior_id"` for another mod's. `interior` still picks the race whose ships park there. |
| `planetTexture` | Its own planet: a PNG with transparency (the round planet on a transparent background, its lit side as you like it; 512 x 512 is plenty). Seen big from its own orbit and small from the system's other orbits. Default: the game's texture `planet`. |
| `planetSphere` | Its planet drawn as a lit 3D sphere in place of the flat picture, with one of EVERSPACE 2's planet looks: `{ "look": "es2Desert" \| "es2GasSmooth" \| "es2Gas" \| "es2Cratered" \| "es2Volcanic" \| "es2Ice" \| "es2Water" \| "es2Earthlike", "maps": { "surface", "normal", "surface2", "normal2", "capNormal", "capColour", "noise", "noise2", "clouds", "cloudsNormal": PNGs in your mod }, "atmosphere": [r, g, b], "colors": [[r, g, b], [r, g, b]], "variation": 0-1 }`. The maps are the ones the look's EVERSPACE 2 material uses, as they are. For the gas giants, `colors` are the two colours its bands go between (for `es2Volcanic`: its rock's and its lava's; `es2Ice`: its ice's; `es2Water`: its water's and its floes'; their clouds drift slowly) (linear, like EVERSPACE 2's: the smooth giant's Color and Color2, the banded one's SecondaryColor and BaseColor) and `variation` turns their hue, so planets sharing maps look different. The sun of its orbit lights it; with `atmosphere` it gets a glowing shell like EVERSPACE 2's, thickest round its edge, its colour turning toward the opposite hue where day meets night. |
| `mapTexture` | The planet on the star map's system view: an image wrapped round the map's sphere like a world map (2:1 equirectangular; it must wrap at the left and right edges; 1024 x 512 is plenty), lit by the system's sun like the game's map planets. Without it a `planetTexture` is shown there as a flat disc facing you; without either, the game's map planet for `planet`. |
| `mapRing` | `true`: a ring round the planet on the star map (the game's own planet ring, as the planet ring orbits have it). |
| `mapPlace` | `{ "angle": degrees, "distance": n }`: where its planet sits in the star map's system view, round the sun (0 to 360) and how far out (the game's planets lie about 8000 to 45000 out). Either may be left out. Default: the game's place for it. |
| `orbit` | The layout of its orbit level: station or not, jumpgate, arrivals, asteroids, fog, sun and planet. See [Orbit layouts](#orbit-layouts). |
| `music` | A mod's track for its docked music (see [Music](#music)). |
| `mobile` | A new station that moves: `true` or `{ "route": [stations] \| "any", "stayMinutes": 15, "systemMinutes": 90, "speed": 0.2, "race": "terran", "engines": [mounts] }`. See [Mobile stations](#mobile-stations). |
| `life` | Drones at work round its own `model`, like EVERSPACE 2's worker drones. See [Station life](#station-life). |

Changing a station (`"override"`): `name`, `subtitle`, `description`, `techLevel`, and the looks: `model` and its fields, `interior`, `planetTexture`,
`mapTexture`, `mapRing`, `mapPlace`, `orbit`, `music` (an original station can get a new model too).

### Station life

A station with its own `model` can have drones at work round it, like EVERSPACE 2's worker drones: they fly loops round its
structures, and now and then one leaves its loop for a spot on the hull, welds there a while with sparks and flies back to
its place on the loop.

```json
"life": { "scene": "life", "loops": [ { "points": [[x, y, z], ...], "count": 4, "speed": 20, "loop": true, "twoLanes": true,
                                       "laneOffset": 5, "spots": [[x, y, z, nx, ny, nz], ...] } ] }
```

Everything is in the model's own frame: its metres as the glTF file has them, before `modelYaw`, `modelSize` and the
centring, so the drones turn, scale and move with the model.

| Field | |
|---|---|
| `scene` | The drone: the scene of this name in the station's model file (glTF files can hold several scenes; the station is its main one). Its nose faces +Z. Default `"life"`. |
| `loops` | At most 24. Each: |
| `points` | The loop's path, at least two points; a smooth curve runs through them. |
| `count` | Drones on it, spread evenly: 1 to 40 (default 4). |
| `speed` | Metres a second, in the model's metres (default 20, EVERSPACE 2's). |
| `loop` | `true` (default): the path closes; `false`: the drones turn back at its ends. |
| `twoLanes` | `true` (default): every other drone runs the other way, each `laneOffset` metres (default 5) to its right. |
| `spots` | Where its drones weld: a point on the hull and the hull's outward direction there, at most 64. A drone with a spot within 100 m and 90 degrees of its heading may go: it waits 5 m off the spot facing the hull, welds 10 to 20 s, and its next weld comes at most 20 s after it is back (one drone of a loop is picked every 2 s, shared among those not welding). Without spots the drones only fly. |

The drones are only scenery: no marker, no collision. Not in the alien orbit or interstellar space.

### Mobile stations

`"mobile"` makes a new station a moving one, like a carrier you can dock in. It has no planet: it isn't one of its system's
stations (so it doesn't count toward the 7, isn't on the star map and can't be travelled to by planet jump). Instead it
cruises from orbit to orbit:

- `route`: the stations whose orbits it visits, in order (numbers or `"mod_id:station_id"`), in any systems. Default:
  every station of its own `system`. `"any"`: it roams every system, staying `systemMinutes` in one (its orbits there
  `stayMinutes` each, so a pilot who leaves can come back to it soon), then another; every system once per round, in an
  order of its own, never the same one twice in a row. It never stops at the story's places (Kothar's deep science
  station, the Valkyrie, Skavac, the supernova system, Alda) or the Kaamo Club.
- `systemMinutes`: with `"any"`, how long it stays in a system (default 90; at least `stayMinutes`, at most a day).
- `exclude`: with `"any"`, systems it never goes to (numbers, `"mod_id:system_id"`).
- `systems`: with `"any"`, the only systems it goes to (numbers, `"mod_id:system_id"`); none: every system.
- `engines`: its exhausts, lit while it cruises: mounts like a ship's (`position_engine` in game units from the model's
  centre, its front +Z; `glowSize`, `glowColor`, `fxScale`); `engineColor` [r, g, b] for every one.
- `engineFx`: an engine effect at those exhausts instead of the glow discs, the same object as a ship's `engineFx` (see
  [Engine effect](#engine-effect)), each exhaust sized by its `fxScale`; it runs at a cruise's thrust.
- `race`: its race, `terran`, `vossk`, `nivelian`, `midorian` or 0-3, the same wherever it is (its hangar, the docked
  screen's race). Default: its `interior`'s, else its own system's.
- `stayMinutes`: how long it stays in each orbit (1-240, default 15).
- `speed`: game units per ms (0-2, default 0.2, 10 m/s; the freighters fly 1).

Where it is follows the real clock, so it is in the same place for every player of a multiplayer game, and it has
moved on when you come back later. In each orbit it crosses a straight course sideways past the station, its middle 6 to 8
km out in front of it, so its hull never sweeps through the station. Fly close and lock it like a station: the autopilot
flies to it, and within its range (its size + 300 m) **Dock** takes you into its hangar and bar (its `interior`, or your own
`hangar` / `bar`). Its shop, bar and missions are a station's like any other. A launch from it comes out beside it, in the
orbit it is in by then. Docked, the system it shows (the header, the prices, the star map's "you are here") is the one of
the orbit it was in when you docked. Its title is its name alone, without "Station". The star map marks where it is now:
its name in amber after the system's and under the planet of its orbit, with how long until it leaves the system.

Give it a `model` and a `modelSize`; the course runs along the model's front (+Z after `modelYaw`). Keep it under about
6 km: the flight level's far plane is 15 km.

### Orbit layouts

Every orbit is laid out by the game from fixed numbers per station: where the jumpgate stands (only in the system's gate
orbit), where ships arrive, how many asteroids there are and where, the sun's direction, the planet's size. A station's
`orbit` changes any of it; what it leaves out stays the game's. The station is always the orbit's centre: places are game
units from it (20 units = 1 m), `z` ahead of a ship launching from the station, `y` up, `x` across (+x to the launching ship's
left). Directions are degrees: yaw 0 behind a launching ship (where the game puts the orbit's planet), 90 to its right, 180
ahead of it; pitch up from the horizon.

| Field | |
|---|---|
| `station` | `true` a station to dock at (also in an orbit the game keeps empty), `false` no station at all (an empty orbit: nothing to dock at, no station in the autopilot menu), `"nodock"` its model without docking (like the game's wrecked Luur). |
| `jumpgate` | `true` the jumpgate stands in this orbit, `false` none. Default: in the system's gate orbit only. A gate here takes ships out through the routes; ships coming in through them still arrive in the system's gate orbit (`gateStation`), so keep the gate there. |
| `jumpgatePosition` | `[x, y, z]`: where the jumpgate stands (the game's: 70 000-120 000 units out). |
| `arrival` | `[x, y, z]`: where every arrival by travel comes in (a planet jump, the jumpgate, the Khador Drive, a wormhole), facing the station. Default: from the direction of the planet left behind, or the gate. |
| `asteroids` | `false` no field (the autopilot's "Asteroid field" entry goes too), or `{ "count": 1-300, "centre": [x, y, z], "spread": units across (default 100000; the big ones in 0.6 of it), "type": "rock" \| "void" \| "ice" \| "magma", "ores": [items] }`. `ores` are item numbers or `"mod_id:id"`, or `{ "item": 157, "chance": 3 }` for a weight (default 1); without them the system's ores by distance, like the game's. The game's ores (154-164, 217) give their cores from class A asteroids. `"models": ["rocks/a.glb", ...]` (glTF / GLB files in the mod) draws the asteroids, one at random each, as big as the game's asteroid of that size, with `"materials"` like ships.json's; mining, ores and hits stay the game's. |
| `fog` | `false` no fog, or `{ "color": [r, g, b] (0..1), "end": units }`: the haze some systems have (the game's end 50 000-150 000). |
| `sun` | `[yaw, pitch]`: the sun's direction in this orbit (and the light, the flare, the skybox's `sunAligned`). |
| `planet` | `{ "size": x the game's, "yaw", "pitch" }`: the orbit's own planet. |
| `light2` | The second light (the game's planet light): `{ "color": [r, g, b] (0 to 2, as bright as the sun's 1.5 at 1.5), "yaw", "pitch" }` (directions as `sun`), or `false` for none. Default: the system's colour, from the orbit planet's side. |
| `ambient` | `[r, g, b]` (0 to 1): the light everything gets on its dark side. Default: 0.15 x the sun's colour. |
| `skybox` | The orbit's own sky, with the fields of a system's [`skybox`](#skies), in place of the system's sky in this orbit alone (each of a system's stations can show its own). |
| `skyRotation` | `[x, y, z]` degrees: the sky's turn in this orbit, like the system skybox's `rotation` but for this station alone (over the system's `rotation` and `sunAligned`): each orbit can show its sky from its own angle, lined up with its own `sun`. |
| `pads` | Docking pads outside the station, docked at from space like the arms in the Luur mission: `[ { "position": [x, y, z], "approach": [x, y, z], "facing": [x, y, z] } ]`. `position` is where the ship rests, `approach` where it comes in from (default 2000 units above the pad), `facing` where its nose points (default `z`). The player docks at the nearest free one ("DOCK" on the station's pads target); missions hear it as `on pad` (see the steps). With `"station": "nodock"` they are the only way to dock. |
| `pirateHeld` | `true`: pirates hold this station on every visit, like the Kaamo Club at the start (four pirate outposts and their fighters; no docking, jumpgate or Khador Drive until the outposts fall). Driven out, they come back after 30 minutes of play. `false` keeps the station free in a system that is `pirateHeld`. |
| `wrecks` | A field of wrecks to salvage, like EVERSPACE 2's wreck fields: `{ "count": 1-60 (default 12), "centre": [x, y, z], "spread": units across (default 60000) }`, or `true` for 12 at the default place. Each visit lays out that many wreck containers at random in the field, each with a freighter's loot (or a little Space Waste); they stay until emptied. `"models"`: glTF / GLB wreck hulls in your mod laid among them (`"hulks"` of them, default twice the models up to 8; one at random each; the largest model `"size"` units at its largest side, default 8000, the others in proportion as you made them, each a little bigger or smaller; turned at random, the same for every player), with `"materials"` like a station model's; the player slides along each one's box and pilots steer around it. |
| `containers` | Closed containers, like EVERSPACE 2's: `{ "count": 1-40 (default 6), "centre": [x, y, z], "spread": units across (default 40000), "bulletproof": share 0-1 (default 0.3) }`, or `true` for 6 at the default place. Each visit lays them out at random in the field, each holding 1 to 3 freighter loot rolls (a bulletproof one 2 to 4: EVERSPACE 2's counts for its containers and rare ones). A hit opens an ordinary one; shots bounce off a bulletproof one. Either opens with the action key (OPEN, F) within 360 m in front of your ship. Opened, it throws its loot out as crates for the tractor beam, which stay until emptied. Not in the alien orbit or interstellar space. `"loot": [ { "item": "mod:core", "amount": 2 }, ... ]` puts fixed items in them on top, an entry a container (the bulletproof ones first), each found once a game: once its container is opened it doesn't come back. `"model"` and `"bulletproofModel"` (glTF / GLB files in the mod) draw them, as big as the race's container, with `"materials"` like ships.json's (by the model's mesh or material names); without them the race's container. |
| `lanes` | Traffic lanes, like EVERSPACE 2's spline traffic: `[ { "points": [[x, y, z], ...], "ships": ["12", "mod:id"], "count": 1-40 (default 10), "speed": m/s (default 80), "loop": true, "twoLanes": true } ]`, at most 12. Ships stream along a smooth path through the points (at least two; three or more can close into a loop), spread evenly, engines lit; with two lanes every other one runs the other way, each keeping to its right. On an open path they turn over at the ends. They look like the `ships` given, or else the system race's fighters. They are scenery: no AI, marker or lock, but a hit can destroy one, which is a small crime against the system's race, and it is back 30 s later. Not in the alien orbit or interstellar space. |
| `level` | The orbit's EVERSPACE 2 location level, 1-30: what its encounters pay (EVERSPACE 2's 800 credits plus up to 4200 more by level). Without it the station's tech level x 3. |
| `hazards` | Hazard volumes, like EVERSPACE 2's: `[ { "kind": "heat" \| "radiation" \| "frost" \| "corrosion" \| "damage", "centre": [x, y, z], "radius": units, "dps": damage } ]`. Inside a `heat` sphere the ship takes up to `dps` a second, most at the centre (falling off with the square of the distance), in quarter-second ticks; `"raiderDps"` gives it instead as that many pirate raiders' gunfire, so it grows with your rank as their guns do (EVERSPACE 2's heat volumes: about 3.6). Every 5 seconds inside a `radiation` sphere the ship is radiated once more: its hull can't be repaired past 85 % of its maximum per dose (up to 10), in every orbit, until it docks. Five seconds in a `frost` sphere freeze the ship for 6 seconds (it barely answers the controls); the frost thaws over a minute outside. A `corrosion` sphere eats at the hull past the shield every second while inside and for 8 seconds after (`raiderDps`: each tick, default a twelfth of a raider's second of fire); a System Recovery Routine stops it. A `damage` sphere takes `dps` (or `raiderDps`) a second everywhere inside. The HUD says when the ship flies in. They look like EVERSPACE 2's fields, drawn with its own shader math: drifting clouds in the volume (yellow-green churning corrosion with bubbles, yellow speckled radiation with sparks, blue frost wisps, a faint orange heat haze, fire for `damage`); inside, frost creeps in from the screen's edges as it builds, radiation makes the edges glow and grain, and heat makes everything glow brighter. |
| `storm` | A lightning storm round the ship, like EVERSPACE 2's lightning fields: `true`, or `{ "frequency": flashes a second (default 1), "radius": units out (default 80000), "heightPercent", "scale", "intensity", "thunder": [your sounds], "thunderChance": 0-1 (default 0.2) }`. Flashes of light and lightning bolts on a ring round the camera, blue turning orange as they fade; now and then one of your thunder sounds follows. Only an effect: no damage. |
| `ambience` | Sound beds, like EVERSPACE 2's ambience zones: `[ { "loops": ["name", ...], "adds": ["name", ...], "addEveryMs": [8000, 15000], "addVolume": 0.3, "volume": 1, "centre": [x, y, z], "radius": units } ]`, the names your mod's [sounds](#sound-effects) (a loop may also be one of its [music](#music) tracks: kept compressed, better for long beds). One of `loops` (or `"loop"`) plays on repeat, one of `adds` now and then at `addVolume`, under the music at the sound effects' volume; full within `radius` (default 30000) of `centre`, fading out over half the radius more, or everywhere in the orbit without a `centre`. |
| `gasClouds` | Its own plasma clouds, like EVERSPACE 2's plasma fields: `{ "plasma": 201, "clouds": [ [x, y, z], ... ] }` (game units from the station). They replace the game's random clouds in that orbit, in the plasma given (201 Green, 202 Blue, 203 Purple, 204 Red, or a mod item; without it the one the game picks for the system); as the game's, they only show with a spectral filter mounted, burst with an ionizing missile and are gathered with a plasma collector. |
| `locals`, `raiders` | Who flies in this orbit, like the system's own `locals` / `raiders` (races, numbers or mod races), beating the system's here: the local fighters' sides and who raids it. |

```json
{
    "id": "rho_freeport", "system": "frontier_systems:kepler", "name": "Rho Freeport", "techLevel": 4, "planet": 6,
    "mapPlace": { "angle": 200, "distance": 26000 },
    "orbit": {
        "jumpgatePosition": [-60000, 8000, 70000],
        "asteroids": { "count": 90, "centre": [40000, -6000, 50000], "spread": 140000, "type": "ice", "ores": [154, 159, { "item": 161, "chance": 2 }] },
        "sun": [120, 20],
        "planet": { "size": 1.4, "yaw": -25, "pitch": 8 }
    }
}
```

Keep the jumpgate, the field and the arrival within 10 km (200 000 units) of the station: the game's own lie within 6 km,
and the view reaches 15 km. A launching ship flies along +z: leave room there for the departure. The Mod Builder's Stations
page has every field (Its orbit) and **Show its orbit** shows the result.

The docking range and the launch point grow with a big model: the "Dock" prompt shows within its radius plus 300 m, and a
launch starts 150 m clear of it. Station models load in the background with the ships while the game starts (the loading
screen waits for them).

Prices follow the distance on the star map, like the originals. The story stays in the original galaxy: the Void's invasions
never pick a mod's system.

## Races

`races.json` adds races of the mod's own beside the game's (Terran, Vossk, Nivelian, Midorian, the pirates, the Void and
the Specters), for a galaxy whose ships don't fly for the game's four. A mod's race can own systems (systems.json `race`),
fly ships (ships.json `race` and `traffic.races`) and raid (systems.json `raiders`). Each has its own standing with the
player, shown on the Status window under the game's reputation bars, and its own relations to the other races. Wherever
a race goes (a system's or ship's `race`, `/spawn`, `/standing`, `standing(...)`), name it by its `id` or `"mod_id:id"`.
(Mods made before this was renamed may still have a `factions.json`: it is read the same way.)

```json
[
  { "id": "guild", "name": "Mining Guild", "look": "midorian", "icon": "races/guild.png", "color": [1, 0.7, 0.2],
    "standing": 10, "relations": { "gang": "enemy", "allies": "friend" } },
  { "id": "gang", "name": "The Gang", "look": "pirates", "hostile": true },
  { "id": "allies", "name": "Allies", "look": "terran", "standing": 80 }
]
```

| Field | |
|---|---|
| `id`, `name`, `description` | As for items. Other mods name it `"mod_id:id"`. |
| `look` | The game race whose hangar, bar, lounge visitors, music, NPC guns and jumpgate the race borrows: `terran`, `vossk`, `nivelian`, `midorian`, `pirates` or `void` (default `terran`). |
| `icon` | Its emblem, a PNG (square, transparent around it, like the game's race emblems). Default: its look race's. Shown on the station's header, the star map, the lock plate and the Status window. |
| `color` | `[r, g, b]` (0 to 1). |
| `standing` | The player's standing at the start of a game, -100 to 100 (default 0). At -71 and below the race is hostile, at 71 and above a friend, as with the game's races. Killing its ships costs 5 (10 on Extreme). |
| `hostile` | `true`: always the player's enemy, whatever the standing, and the enemy of every race it doesn't call a friend, like the game's pirates. It isn't listed on the Status window. Killing one of its ships in the system of a race it is at war with raises that race's standing by 1. |
| `relations` | `{ "<race>": "friend" \| "neutral" \| "enemy" }`: who its ships fight. Mods' races by id or `"mod_id:id"`, the game's races by word (`terran`, `vossk`, `nivelian`, `midorian`, `pirates`, `void`, `specters`). A relation one side leaves out is the other side's; when neither names it the two are enemies if either is `hostile` or one is the game's pirates, Void or Specters, else neutral. |
| `diplomats` | `false`: no lounge diplomat reconciles it (default `true`, `false` for a hostile race). |
| `gun` | An item (a number or `"mod:id"`): the gun look and sound of its NPC fighters. Default: its look race's. |

In the Space Lounges of a mod race's system, now and then (the game's 35 %) a diplomat of a race of the same mod that
the player is at war with offers to restore it to neutral, for |standing| / 100 x 16 000 credits, like the game's
diplomats. The standings are saved with the game, and in multiplayer each player's own standings decide how the races'
ships treat them. A campaign can start the player elsewhere (`campaign.json` `raceStanding`), and a story can change
them (`/standing`, the quests' Standing step) or ask for them (`standing(guild) > 50`).

## Campaigns

A mod can be a game of its own: `campaign.json` adds an entry under the three campaigns in **Start new game**. Picking it
asks for the difficulty and the economy like the others, then starts the player where the campaign says, with the GoF2
story off.

```json
{
    "name": "Kepler Frontier",
    "description": "Start over on the frontier.",
    "image": "campaign.png",
    "startStation": "frontier_systems:kepler_prime",
    "startShip": "starwars_ships:jedi_starfighter",
    "credits": 25000,
    "equipment": ["plasma_arsenal:plasma_lance", { "item": "plasma_arsenal:hornet_swarm", "amount": 12 }],
    "cargo": [{ "item": 22, "amount": 5 }],
    "standing": [40, 10],
    "quest": "kepler_story",
    "galaxy": "mod",
    "items": "mod",
    "ships": "mod",
    "trafficShips": { "terran": ["starwars_ships:jedi_starfighter"], "pirate": ["halo_ships:space_banshee"] }
}
```

| Field | |
|---|---|
| `name`, `description` | The entry's title and text (strings, or per language). |
| `image` | The campaign's card, shown beside the game's three campaign cards and picked the same way: a PNG like theirs, 290 x 448 or a multiple (580 x 896 is sharp). It is cropped to the card. |
| `imageHover` | Optional. The card while it is selected (fades in, like the game's cards). |
| `showTitle` | `true` (default): the name and the mod on a plate at the card's foot. `false` when the art has its own title. |
| `startStation` | **Required.** Where the game begins, docked (a number or `"mod_id:station_id"`). |
| `startShip` | **Required.** The player's first ship. |
| `credits` | The money the player starts with. Default 0. |
| `equipment` | What is mounted on the ship: items, or `{ "item", "amount" }` (a secondary weapon's amount is its ammo). Default: nothing. |
| `cargo` | What is in the hold: `{ "item", "amount" }`. |
| `standing` | `[Terran / Vossk, Nivelian / Midorian]` from -100 to 100 (positive leans toward the first race). Default `[30, 0]`. |
| `raceStanding` | The start toward mods' [races](#races): `{ "guild": -60, "mod_id:allies": 80 }`, -100 to 100. Default: each race's own `standing`. |
| `quest` | An event graph (a mission, see [Story missions and bar missions](#story-missions-and-bar-missions)) that starts with the game: the campaign's story. Its other missions start by their own conditions as usual. |
| `galaxy` | `"mod"`: only this mod's star systems and those of the mods it depends on exist in this game (the map, missions, the news); `"all"` (default): the whole galaxy. |
| `items` | `"mod"`: shops and bar sellers offer only mods' items (sell the player anything they find; mining still brings the game's ores). Default `"all"`. Give the campaign its own goods and energy cells if it needs them. |
| `ships` | `"mod"`: dealers sell only mods' ships. Default `"all"`. |
| `trafficShips` | The ships NPC fighters fly, per race (`terran`, `vossk`, `nivelian`, `midorian`, `pirate`). Default: the game's. |
| `flightStyle` | `"free"` or `"original"`: the campaign's games always fly so (see [Flight styles](#flight-styles)). Default: the player's option. |

A campaign's mod usually depends on the mods with its galaxy, ships and items, so turning it on turns them on too. Saves
remember the campaign; loading one asks for its mods like any modded save.

## DLCs

A mod can also go on with the game's own story, the way Valkyrie follows the main game and Supernova follows Valkyrie:
`dlc.json` makes it a DLC. Its card sits with the game's three campaigns, right after Supernova. In a game that has
reached the end of Supernova, the DLC begins by itself the next time the player flies (its intro, then its story).
With several DLCs, they play one after the other.

```json
{
    "name": "The Kepler Incident",
    "description": "A signal from beyond the Void.",
    "image": "dlc.png",
    "quest": "kepler_story",
    "index": 1,
    "intro": "Keith, we're picking up something strange near Maissa.",
    "introSpeaker": 6
}
```

| Field | |
|---|---|
| `name`, `description` | The card's title and text (strings, or per language). |
| `image`, `imageHover`, `showTitle` | The card, as for [campaigns](#campaigns). The card's plate says which DLC it is ("DLC 2"). |
| `quest` | **Required.** The DLC's story: an event graph (see [Story missions and bar missions](#story-missions-and-bar-missions)), started as a mission. The DLC counts as played once it is done. |
| `index` | Its place among the DLCs: 1 plays right after Supernova, 2 after that, and so on. Default: after the DLCs that give one. Players can change the order in the Mods browser (a DLC mod's **Play earlier** / **Play later**), and their order wins. |
| `intro`, `introSpeaker` | Optional. The call that opens the DLC in a finished game: one page of text, said by a story speaker (a number, default 0). Without it the quest starts at once (its first node can be a dialogue of its own). |
| `startStation`, `startShip`, `credits`, `equipment`, `cargo` | Optional, for a new game from the DLC's card. That game starts at the end of the whole story (every step up to Supernova's last played, the DLCs before this one counted as played), docked at Maissa in Supernova's ship and gear; these change the start like [campaigns'](#campaigns) fields. |

A DLC needs a game whose GoF2 story is finished: it never starts in free play, in a mod campaign or in a multiplayer
session. Only one DLC runs at a time; the next one begins once the one before it is done.

## New-game options

`gameoptions.json` adds cards to the **Game options** panel of Start new game (after the difficulty and the economy), beside
the Kaamo Club, Hardcore and Tutorials switches. Each card is a switch the player turns on or off for the game they start; the
game remembers it in its saves, and your content asks for it with `option(...)` in its [conditions](#conditions).

```json
[
    {
        "id": "canon_ships",
        "name": "All Canon Ships",
        "description": "The ships of Galaxy on Fire 3D and 3 join the game as you progress.",
        "image": "cards/canon_ships.png",
        "showTitle": false,
        "default": true
    }
]
```

| Field | |
|---|---|
| `id` | **Required.** The option's id within your mod; conditions name it `option(<mod id>:<id>)` (or `option(<id>)` when no other mod has that id). |
| `name`, `description` | The card's name (on its plate and in the tooltip) and its description (the tooltip). |
| `image` | The card's art, like a campaign card: 290 x 448 or a multiple. Without one the card is plain with its name. |
| `imageHover` | Optional. The art while the card is selected. |
| `showTitle` | `true` (default): the name and the mod on a plate at the card's foot; `false` when the art has its own title. |
| `default` | The switch's position the first time (afterwards the player's last choice). Also what a multiplayer session plays with. |
| `flightStyle` | `"free"` or `"original"`: while the option is on, the game flies so (see [Flight styles](#flight-styles)). |
| `setting` | `true`: no card, a switch in **Options > Gameplay** instead, shown only while your mod is on and the player's own for every game (it starts at `default`; Default settings puts it back). `option(...)` reads it the same way. For what a player may want to turn off at any time (the ES2 Ships mod's Dreadnought raids). |

Two cards show at a time; more scroll sideways (drag, wheel, or the arrow keys / D-pad). A game started before your mod was
installed has every option off.

## Conditions

Several fields take a condition: `available` on items, ships and blueprints, and the missions' "Starts when". It is written in
the event graphs' expression language and is checked when it matters (a shop's stock, a lounge's visitors, a kill...):

| Word | |
|---|---|
| `option(mod_id:option_id)` | 1 when that new-game option (or gameplay setting) is on in this game. |
| `won(main)`, `won(valkyrie)`, `won(supernova)` | 1 when that campaign's story is finished in this game. |
| `systemsvisited` | How many star systems the player has docked in (2 or more: they have left Mido). |
| `standing(race)` | The player's standing toward a race, -100 to 100: `terran`, `vossk`, `nivelian`, `midorian` or a mod's [race](#races) (`guild`, `mod_id:guild`); pirates, the Void, the Specters and a `hostile` race are always -100. |
| `campaign` | The story step (-1 in free play and mod campaigns). |
| `rank`, `credits`, `kills`, `station`, `system`, `ship` | The player's rank, credits, kills, where they are docked / flying, the ship they fly. |
| `visited(station)`, `has(item)`, `cargo(item)`, `quest(name)` | A station docked at; how many of an item the player has (mounted too) / has in the hold; a mission under way (1) or done (2). |
| `docked(station)`, `inorbit(station)` | How many of the players are docked at that station now / alive in its orbit (single player: 1 or 0). |
| `medals(gold)`, `medals(silver)`, `medals(bronze)`, `medals(any)` | How many medals the player holds of that grade (`any`: earned at all). |
| `medals(allgold)`, `medals(allsupernova)` | 1 with every medal of the main game gold (what puts the VoidX at Thynome), or those and the nine Supernova medals too. In a game started without medals: 1 once the main story is won, or the Supernova story finished. |
| `bought(ship)` | 1 once the player has bought (or built from a blueprint) that ship in this game: a number, a name or `"mod_id:id"`. Ships bought before this word existed don't count. |
| `produced(item)` | How many times the player has built that item's blueprint in this game (a number, a name or `"mod_id:id"`): `produced(85) > 0` once the Khador Drive is built. |
| `unlocked(item)` | 1 once the player knows that item's blueprint (bought, found or given by the story): `unlocked(85)` from the story step that hands over the Khador Drive's. |

Combine them with `and`, `or`, `not`, comparisons and arithmetic:

```json
"available": "option(canon_ships:canon_ships) and systemsvisited >= 2"
"available": "option(canon_ships:canon_ships) and won(supernova)"
"available": "bought(my_mod:raptor_mk2)"
"available": "medals(allgold)"
```

A condition the game can't read counts as false, and the mod shows a warning in the Mods screen.

## Blueprints

`blueprints.json` adds blueprints, built in the hangar's **Blueprints** tab like the game's own: invest the ingredients (or
autocomplete), and the product is made at the station where the first ingredient went in.

```json
[
    {
        "id": "valkyrie",
        "ship": "canon_ships:valkyrie",
        "ingredients": [ { "item": 155, "amount": 400 }, { "item": 127, "amount": 60 }, { "item": 122, "amount": 100 } ],
        "autocomplete": 1500000,
        "available": "option(canon_ships:canon_ships) and won(supernova)",
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
        "available": "option(canon_ships:canon_ships) and won(supernova)"
    }
]
```

| Field | |
|---|---|
| `id` | **Required.** The blueprint's id within your mod. |
| `ship` or `item` | **Required, one of them.** What it makes: a ship (yours, another mod's or an original's number) or an item (a secondary weapon comes 10 at a time, like the game's). An item that already has a blueprint gets yours instead. |
| `ingredients` | **Required.** `{ "item", "amount" }` each: goods, ores and cores, and items too (the player demounts them first). |
| `requiresShip` | Optional. Only built while flying that ship, which it then replaces: a skin ("Valkyrie, 1000 t Gold, 100 Golden Cores, 300 Energy Cells"). The ship's Kaamo Club upgrades stay. |
| `autocomplete` | `true` (default): the game's price, 1.25 x the product's price. A number: that price. `false`: it can't be autocompleted. |
| `available` | A [condition](#conditions) before any source offers it. On a game blueprint you took over (the same `item`), the story character who sells it waits for it too: Gunant at Var Hastra and his drill (90) with `"available": "unlocked(85)"` only once the Khador Drive's blueprint is known. |
| `unlocked` | `true`: the player knows it as soon as it is available (the next docking), without finding it. |
| `lounge` | A Space Lounge visitor sells it: `chance` % each time a bar fills with new visitors, for `price` (default half the product's price), in `systemRace` systems (-1 = any). `stations`: only in those bars (station numbers or `"mod_id:station_id"`; then `chance` defaults to 100), like the game's own blueprint sellers, each at one station: `{ "stations": [ 54 ], "price": 59999 }`. With `stations`, `seller` puts a named character there instead of a local visitor (Kothar and Valkyrie have no lounge): `{ "name": "Khador", "speaker": 20, "race": 7, "offer": "...", "thanks": "...", "known": "..." }`, `speaker` a story character's number for the face, and the optional lines for the offer (`#N` the product, `#C` the price), the sale and every chat after it. |
| `derelict` | `chance` % of the orbits visited hold a derelict freighter (`race`: terran, vossk, nivelian, midorian or any) to dock at and hack; it leaves the blueprint in a data container. `stations`: only in those orbits (station numbers or `"mod_id:station_id"`; then `chance` defaults to 100, always there). `position`: `[ x, y, z ]` in game units (what `/pos` shows in that orbit), else somewhere out in front of the station. Hacking it also fires the missions' and events' `hacked` trigger. |
| `drops` | A ship the player destroys may leave it in a data container: `race` (terran, vossk, nivelian, midorian, pirate, void, specter or any), `chance` % per kill (decimals allowed). Or `ship` (a number or `"mod_id:ship_id"`) instead of `race`: only ships of that type drop it, whatever their race, and before any race-wide drop; `{ "ship": "my_mod:boss_hull", "chance": 100 }` makes a boss leave its own hull's blueprint. A blueprint already known is never dropped again. |

A blueprint found in a data container is learnt when the tractor beam pulls the container in ("Blueprint found: ..."). A
ship's blueprint shows the ship in the Blueprints tab, and the finished ship waits at the station where it was built: there
it replaces the player's ship like a purchase (the old one is traded in at its price, or parked in the Kaamo Club when the
player owns it and has no ship of that type stored), the equipment moving over as far as it fits.

A blueprint is obtained once: once the player knows it, every source stops offering it. A seller already standing in a bar
has nothing left ("I don't have anything left, sorry."), a derelict hacked afterwards is empty, and no second data container
is dropped while one with it still floats in the orbit. It can be built from as often as the player likes.

## Wingmen

The Space Lounges' wingmen offers can fly your ships: a `wingmen.json` in the mod (one object) makes a crew of them.

```json
{
    "chance": 40,
    "ships": [ "falcon", "my_mod:hawk" ],
    "radio": {
        "kill": [ "Scratched one!", "Got one!" ],
        "lost": [ "We've lost a wingman." ],
        "help": [ "Help me, I can't shake him off!" ],
        "thanks": [ "Nice shot!" ],
        "warn": [ "Enemy behind you!" ],
        "playerDown": [ "Pull out, you're on fire!" ]
    }
}
```

| Field | |
|---|---|
| `chance` | % of the bars' wingmen offers whose crew flies your ships (default 40). With several mods' crews each is rolled in turn, in load order; the first that comes up flies, else the game's way (a fighter of the agent's race, the same one for a name every time). |
| `ships` | The ships they fly (`"mod:id"`, your own by their `id`, or numbers). Default: your ships with a `wingmanDevice`. Each pilot gets one of those of the agent's race, else any of them; the agent and its friends may fly different ones. |
| `radio` | Lines a wingman in one of these ships says in the flight HUD's radio box, with its name and face: `kill` (it destroyed a ship; now and then), `lost` (another of them was shot down), `help` (its hull under a third), `thanks` (you destroyed a ship that was after it), `warn` (an enemy after you within 1.5 km behind you; then quiet for 30 seconds), `playerDown` (your hull under a fifth; once until it is back over half). A list of lines (one at random) or one line; at most one line every 12 seconds, and none over other radio calls. |

A ship's `wingmanDevice` (see [ships.json](#shipsjson)) works whenever a hired wingman flies it, with or without a
`wingmen.json`. The crew's ships are saved with the contract; a ship no longer there is replaced the game's way.

## Most Wanted

`wanted.json` adds criminals to the Most Wanted boards (the Missions window's **Most Wanted** tab in a station of the
board's race). Each one travels between stations like the game's own; in the orbit where it is, it waits with its escorts
until locked, and its death pays the bounty.

```json
[
    {
        "id": "snotch",
        "name": "Snotch",
        "description": "A smuggler king who never sells what he can steal.",
        "board": "terran",
        "race": "pirate",
        "ship": "my_mod:snotch_hull",
        "weapon": "my_mod:boss_laser",
        "secondary": 36,
        "hitpoints": 6000,
        "reward": 180000,
        "loot": 113, "lootAmount": 4,
        "wingmen": 3,
        "requiredBounties": 1,
        "systems": [ "my_mod:sector_9", 12 ],
        "route": [ 1, 3 ],
        "character": "my_mod:snotch",
        "available": "systemsvisited >= 2"
    }
]
```

| Field | |
|---|---|
| `id`, `name` | **Required.** Its id within your mod and its name (translate both `name` and `description` with `wanted.<id>.name` / `wanted.<id>.description` in `text/<lang>.json`). |
| `description` | What the board says about it. |
| `ship` | **Required.** Its ship: a number, a name or `"mod_id:id"` (a capital ship works too). |
| `race` | The pilot's and its escorts' race: `terran`, `vossk`, `nivelian`, `midorian`, `pirate` (default), `void` or a mod's [race](#races). |
| `board` | The board that lists it: `terran`, `vossk`, `nivelian` or `midorian` (default: its race's, else terran). It shows in that race's stations, or with `systems` in those systems' stations instead. |
| `weapon`, `secondary` | Its gun (default the game's first) and missiles (default none): items by number or `"mod_id:id"`. |
| `hitpoints`, `reward` | Its hull on top of the game's criminal bonus (default 1000) and the bounty (default 50,000). |
| `loot`, `lootAmount` | What it drops and how many (default nothing). |
| `wingmen` | Escorts flying with it (0 to 12, default 0). |
| `escorts` | Their ships: one ship or a list the escorts take in turn (default: fighters of its race). |
| `taunt` | What it says over the radio, with its face, when it is uncovered (translate with `wanted.<id>.taunt`). |
| `music` | A track of a mod's [music](#music) folder that plays while it fights (default: the game's Most Wanted music). |
| `damageTaken` | How much of your weapons' damage it takes, in percent: `{ "primary": 70, "secondary": 96, "Laser": 140, "my_mod:boss_bane": 200 }`. A key is a weapon kind (`primary`, `secondary`, `turret`), a weapon category as the shop names it (`Laser`, `Blaster`, `Auto-cannon`, `Thermo`, `Scatter gun`, `Rocket`, ...) or an item; the most specific one counts (the item, then its category, then its kind), anything unlisted takes 100. Other players' hits in multiplayer take all of it. |
| `devices` | Boss devices it uses in the fight (see below). |
| `turrets` | Turret items on its ship's own turret mounts, the first on the first: `[ 48, "my_mod:flak" ]` (default: what its ship type carries, its `npcTurrets`; `[]` none). More than the ship has mounts for are left off, with a warning. |
| `requiredBounties` | How many of this board's mod criminals must be dead first (default 0). The game's criminals count apart. |
| `systems` | The systems it travels in (numbers or `"mod_id:id"`), else any the player can see. |
| `route` | `[ least, most ]` systems a trip crosses (default `[ 2, 4 ]`; `[ 1, 1 ]` keeps it in one system). |
| `character` | A [character](#characters) whose face the board shows; else a face of its race (`portraitParts` `[ race, head, gender, hair, extra ]` picks one, `gender` `"female"`). Its radio lines take that character's face too. |
| `available` | A [condition](#conditions) before it appears. |

A mod's criminals appear in free play as well as in the campaign (the game's own, from the Supernova add-on's story, stay in
the campaign). A save keeps each one's state by its key, so adding or removing other mods doesn't move it.

**Boss devices.** `"devices"` is a list of the tricks a criminal pulls in its fight (after Galaxy on Fire 3's bosses):

```json
"devices": [
    { "type": "teleport", "everyMs": 15000, "distance": [ 1100, 2300 ], "cooldownMs": 15000 },
    { "type": "backShield", "onHit": true, "everyMs": 10000, "durationMs": 10000, "cooldownMs": 10000 },
    { "type": "flashGrenade", "range": [ 0, 17000 ], "facing": 80, "delayMs": 3000, "durationMs": 7000, "cooldownMs": 15000 }
]
```

| `type` | What it does while it runs |
|---|---|
| `teleport` | Vanishes and comes out `distance` (`[ least, most ]` game units, default 1200-2400) from you, never in front of you, nose on you. |
| `frontShield`, `backShield` | Stops `blockPercent` (default 100) of every hit coming within `shieldAngle` degrees (default 90) of its nose / tail: fly round it. |
| `energyAbsorber` | Takes no damage; mends its hull by `healPercent` (default 50) of what it soaks. |
| `radarJammer` | Your radar locks nothing and shows no ships. |
| `flashGrenade` | Flashes `delayMs` (default 3000) after the throw: white over your view for its `durationMs`, `strength` percent (default 100) looking straight at it, nothing from 90 degrees away. |
| `mindControl` | Your wingmen turn on you; each comes back to its senses when you hit it. |
| `bombSpreader` | Drops a proximity mine behind it at the start and every `bombEveryMs` (default 1200), flung within 45 degrees; each blasts your ship for `damage` (default 350, GoF2's AMR Saber) when you come near. |
| `mineLayer` | Lays a mine `layBehind` (default 855) game units behind its tail at the start and every `bombEveryMs` while it runs (a burst: GoF3's boss mine layers). Each mine chases you within `seekRange` (default 11400) at `seekSpeed` game units a second (default 456) and goes off within `triggerRadius` (default 969) or after `lifeMs` (default 5000): it blasts your ship within `blastRadius` (default 1938) for `damage` and slows it by `slowPercent` for `slowMs` (default none). Shoot it down first: it takes `hull` (default 60) points. |
| `cloud` | Leaves a cloud `cloudRadius` (default 5700) game units wide `layBehind` its tail (GoF3's smart bombs), at the start and every `bombEveryMs` while it runs, lasting `lifeMs` (default 12000). Inside it your ship keeps only `100 - slowPercent` % of its speed (default 50) and loses `hullPercent` of its hull a second (default none), past the shield, and `shieldPercent` of its shield a second (default none). |
| `shadowClone` | Splits off `count` copies of itself (default 2) with `hullPercentOfShip` of its hull (default 20), flying and firing as it does; they go when the run ends (`durationMs`) or it dies. |
| `wormhole` | Opens a wormhole `radius` game units wide (default 684) `layBehind` its tail (default 570) for `lifeMs` (default 5000): fly into it and you come out `exitDistance` (default 1710) in front of its nose, still heading as you were, right in its sights. |
| `minions` | Launches `count` shield drones (default 5) circling it `radius` game units out (default 1140), each with `hullPercentOfShip` of its hull (default 7). While any of them lives, its hits lose `blockPercent` (default 100): shoot the drones down first. They go when the run ends. |

Each runs `durationMs` (default 5000), then cools down for `cooldownMs` (default 15000). When it starts: `onHit` (true: when
hit) and `everyMs` (every so often, `chance` percent of the time, default 100) fire it; `range` (`[ least, most ]` game
units to you), `ahead` (you within so many degrees of its nose), `notAhead` (you outside them), `facing` (your nose within
so many degrees of it) and `hullBelow` (its own hull at or under so many percent: a second phase) must all hold when given. With neither `onHit` nor `everyMs` it starts as soon as the others hold.
`minRank` / `maxRank` give it only while your rank is within them, and `share` (percent, default 100) to only that part of
the ship's NPCs (rolled as each one comes): a traffic hull's NPCs can carry different devices.
In multiplayer the game running the orbit's ships plays them for its own player.

## Hangars and bars

A station can have its own hangar (where your ship stands when docked) and its own Space Lounge bar. Each room is a glTF /
GLB model listed in `interiors.json`:

```json
[
    { "id": "kepler_hangar", "type": "hangar", "model": "models/kepler_hangar.glb", "fov": 46, "parkedMax": 3 },
    { "id": "kepler_bar", "type": "bar", "model": "models/kepler_bar.glb", "fov": 60 }
]
```

and picked by the station: `"hangar": "kepler_hangar", "bar": "kepler_bar"` in stations.json. `"bar": "none"`: the station has
no Space Lounge (its button is gone).

**Docking EVERSPACE 2's way** (`"dock": "es2"` on a hangar): the hangar is the station's own model, the same file as its
stations.json `model` at the room's `scale` (so the room's metres are the station's), and the player docks in the station's
orbit: the dock's marker shows in space, inside the dock zone the Dock button lands the ship on the pad, under the dock's
camera, over 4 s (EVERSPACE 2's own docking timeline, its wobble and camera sway), and the station's menu opens over the
live orbit (the traffic flies on; no separate hangar scene). Launch takes the ship off from the pad there: up `rise` metres,
turning toward `takeoff`, then out to it on a boost while the view blends back behind the ship over 3 s. It works on a
mobile station too (the dock rides along). Its markers, beside `pad` and `camera`:

| Marker | |
|---|---|
| `ship`, `ship_ahead` | Where the docked ship's middle sits, and a point ahead of its nose. |
| `takeoff`, `takeoff_ahead` | Where the take-off ends, and a point ahead of it (the way the ship faces there). |
| `camera_target` | The point the dock's camera looks at while the ship is down (it follows the ship by the same offset). |
| `dock_zone` | The middle of the box the ship docks from (`dockZone` its half sizes). Default: the pad. |

Fields: `rise` (metres the take-off climbs first; default 14), `dockZone` `[right, up, forward]` half sizes in metres in the
dock's own axes (default 196, 84, 196), `dockSound` / `undockSound` (the mod's own sounds by name, from its `sounds/`
folder, played as the docking / take-off starts), `dust` (an image in the mod: the landing dust's 6 x 6 flipbook, frames
row by row from the top, its grey the opacity; none without it). The dock lights the docking ship with EVERSPACE 2's
two spotlights by itself. Hangar flights don't apply (`flights` is ignored).

**Markers** can also be listed in `interiors.json` (`"markers": { "pad": [x, y, z], "parked_1": [x, y, z] }`, room metres;
they win over the model's nodes of that name): the Mod Builder's Hangars & bars kind places them by clicking into the room. A
room's `model` may be one of the game's rooms (`"gof2:hangar_terran"`, `"gof2:bar_vossk"`), which has no marker nodes: the
builder fills its markers in from the game's own tables.

**Markers.** Put empty objects (Blender: Add > Empty) into the room where things go, named like this; only their position
counts, so their rotation and size don't matter. Names are matched without case; numbered ones by their start.

| Hangar marker | |
|---|---|
| `pad` | **Required.** Where your ship stands (the floor of its pad: each ship hovers at its own height above it). |
| `camera` | **Required.** Where the camera is. It drifts a few metres around it, like the originals'. |
| `camera_target` | Where the camera looks. Default: the pad. |
| `parked_1`, `parked_2`, ... | Pads for other ships, which land and take off now and then (with the hangar flights option on). |
| `gate`, `gate_out` | The middle of the opening to space (the forcefield) and a point outside it. Ships fly in and out through there; without these two, no flights. |
| `light` | Where the main light comes from (it shines toward the pad). |

| Bar marker | |
|---|---|
| `camera`, `camera_target` | **Required.** Where the camera rests and where it looks. It sways a little, like the originals'. |
| `camera_start`, `camera_start_target` | The first visit glides from here to the resting view. Default: no glide. |
| `visitor_1`, `visitor_2`, ... | **At least one.** Where the bar's visitors stand (on the floor; up to as many visitors as markers). |
| `light` | Where the main light comes from (it shines toward `camera_target`). Default: the system's sun, like the originals. |

Windows that show space work by themselves (the sky and the system's planets are drawn behind the room), and ships fly past
outside them now and then.

**Scale.** The model's metres are the game's metres (`"scale"` changes that). The original rooms are big: a hangar about
700 x 300 x 800 m with pads some 200 m apart (the ships are 50-100 m long and float 10-20 m above their pad), a bar about
350 x 100 x 350 m with the visitors 50-130 m from the middle and the camera some 30 m up.

| Field | |
|---|---|
| `id`, `type` | **Required.** The room's id, and `"hangar"` or `"bar"`. |
| `model` | **Required.** The glTF / GLB file. Its own cameras and lights are left out. |
| `scale` | Multiplies the model's size. Default 1. |
| `materials` | Replace the model's materials on parts it names, like [ships.json's materials](#materials). |
| `fov` | The camera's vertical field of view in degrees. Default: the originals' (hangar 46, bar 69). |
| `near`, `far` | The camera's near and far clip distances in metres. Default: 10 and 5000 (hangar) / 2400 (bar). |
| `ambient` | `[r, g, b]`, the room's base light. Default: the hangar of its race's, the bar the sky's. |
| `lightIntensity` | The main light's strength (about 1). |
| `fog` | `[r, g, b, end metres]`: fog that thickens toward the end distance (the Vossk rooms have green fog). Default: none. |
| `startYaw` | Hangar: which way your ship faces, in degrees about the up axis. Default: turned a little away from the camera. |
| `parkedMax` | Hangar: at most this many parked ships. Default: one per `parked_` marker. |
| `flights` | Hangar: `false` turns the fly-in and take-off off for this room. |
| `cruise` | Hangar: how high above the pad (metres) ships cross the room on their flights. Default 40. |
| `gateSpan` | Hangar: `[low, high]`, the heights (metres) ships may fly through the gate at. Default: the gate's height ± 30. |

A room that can't be read, or misses a required marker, says so in the Mods screen; the station then uses its race's room.

## Texts

`name` and `description` can carry every language inline (`{ "en": "...", "de": "..." }`), or the mod can have one file per
language in a `text` folder:

```
text/de.json
{
    "items.plasma_lance.name": "Plasmalanze",
    "items.plasma_lance.description": "Ein terranischer Nachbau des Vossk-Blasters."
}
```

The keys are `items.<id>.name` / `items.<id>.description`, `ships.<id>.name` / `ships.<id>.description`,
`systems.<id>.name` and `stations.<id>.name` (for an override the number: `items.12.name`). The game uses the player's
language, then English, then the plain text. Language codes: `en`, `de`, `fr`, `es`, `it`, `pt`, `ru`, `pl`, `nl`,
`ar`, `ja`, `ko`, `zh`, `hi`.

## Story missions and bar missions

Story content is made with **event graphs**, the same node graphs the multiplayer events use (`.gof2event` files, made in
the Unity Editor of the remake's project: **Assets > Create > GoF2 > Event Graph**; older `.gof2netevent` files still work, and
the Editor renames them when you copy them into the project). Put them in the mod's `events` folder:

```
my_story/
├─ mod.json
├─ events/
│  ├─ first_signal.gof2event
│  └─ the_smugglers.gof2event
└─ voices/
   └─ keith_signal_01.ogg
```

The file name (without `.gof2event`) is the graph's name; keep it one word. The graph's **Start** node decides what it is
(its **Kind**):

- **Story Mission** works like the game's own story chain. It starts by itself once **Starts when** holds, checked every second in a
  game (an expression: `campaign >= 20`, `visited(Kernstal)`, `quest(first_signal) == 2`, `credits > 50000`; empty = at once,
  `never` = only when another graph's **Start Mission** node starts it). Its **Mission title** and current objective show in the
  Missions window's Story panel. **Mission Complete** pays the reward and finishes it for good. **Mission Failed** works like
  failing a story mission: "Mission failed!", then the last save is loaded.
- **Bar Mission** is offered by a visitor in the Space Lounges (**Offered at**: station names or numbers, empty = every
  station; **Client**: a story character like `Keith`, one of your [characters](#characters) or a race and a name like `vossk K'ekki`, empty = someone of the
  station's race). **Offer chance %** (default 35) is how many of those stations' bars offer it: each station decides for 30
  minutes, so docking again doesn't roll it anew, and once someone takes it there that bar doesn't offer it again for 30
  minutes; 100 = every bar, always. **Who can take it** (multiplayer): **One In Universe** (the default: while a squad is
  on it no bar anywhere offers it), **One Per Station** (taking it takes it out of that bar for 30 minutes, other stations
  still offer it) or **Anyone** (any number of squads, at the same station too). The visitor's plate names the mission only once you have talked to them. When it ends
  with **Mission Complete** or **Mission Failed** the client sends their message, like the game's own freelance missions.
  In single player, **Starts when** works for a bar mission too: no bar offers it until it holds (`never`: only another
  graph's **Start Mission** starts it), and **Only once** keeps it out of the bars for good once it was completed (a failed
  or discarded one is offered again; `quest(<name>)` then reads 2). A multiplayer session's bars ignore both.
  In single player it is the player's one mission, like the game's freelance missions: it shows in the
  Missions window's Freelance panel with **Discard**, and a success counts like a freelance mission (only missions for one
  pilot are offered there: **Min pilots** 1). In multiplayer the squad takes it together.
- **Event** is a multiplayer game mode started by an admin's `/event`.

Nodes made for missions:

| Node | |
|---|---|
| **Checkpoint** | What a save keeps: the mission's variables, objective, target and mission orbits as they are here. Loading a save goes on from the last checkpoint. Put one after each step the player shouldn't have to repeat. Only on the main flow from Start, not inside an If / While / Repeat / parallel block. |
| **Set Objective** | The objective in the Missions window (`\n` = a new line) and, with a **Get Orbit** wired in, the target station: the gold story icon on the star map and the HUD, and the window's **Show on map**. |
| **Mission Orbit** | That orbit becomes the mission's: no normal traffic is built there (like the story's own mission orbits), so the graph can spawn exactly the ships it wants. |
| **Start Mission** | Starts another mission beside this one (a chain of missions, or a bar mission leading into one). |

Everything else is the event graphs' toolbox: **Dialog** (conversations with portraits, see [Voice-over](#voice-over)), **Radio**,
**Spawn** (ships, friendly or hostile, named; objects as scenery; `wreck`: a hackable freighter wreck), **Wait Until**, **On**
triggers (docked, entered an orbit, destroyed, a spawned ship destroyed, all enemies cleared, arriving near a point, a wreck
hacked, docked at a station's pad outside), **Set Waypoint**, **Open Wormhole** / **Close Wormhole** (see below), **Restrict Travel**,
**Give Item**, **Take Item** (a hand-in; Mounted takes mounted ones too), **Spawn NPC Type**, **Reward**, **Title**, **Timer**, **Play Music**, **Set Flight Model** (free flight or the original for a race
or a chase, undone when the graph ends; see [Flight styles](#flight-styles)) and more. Game values for conditions: `campaign` (the story
step, -1 without one), `credits`, `rank`, `station`, `system`, `ship`, `kills`, `freeflight` (1 in free flight), and the functions `cargo(item)`, `has(item)`
(in the hold or mounted), `visited(station)` and `quest(name)` (0 not started, 1 under way, 2 done); items and stations by
name or number. In single player `%player%` is Keith T. Maxwell.

**Hacked wrecks.** The `wreck` step (the Spawn node with `wreck` as its ship; the command `spawn @a wreck vossk named Hulk
at 0 0 30000`) puts a damaged freighter (Terran, Vossk, Nivelian or Midorian; left out, one at random) in the player's orbit,
1.5 km ahead or at the coordinates, with its name on the HUD. The player docks at it and plays the hacking game like the
Supernova's wrecks; it can be hacked once. Winning fires the `hacked` trigger (`on hacked`, the **On** node's Wreck Hacked;
limited to a station like the others), with the player as `@trigger` and the wreck's name as `%victim%`, for any hacked wreck
(yours, a blueprint's derelict, the Supernova's): spawn the ambush, play the radio call or pay the reward there.

```json
{ "type": "wreck", "race": "vossk", "name": "Hulk", "at": "0 0 30000" },
{ "type": "on", "trigger": "hacked", "steps": [
    { "type": "radio", "speaker": "pirate Snake", "text": "That cargo is ours!" },
    { "type": "spawn", "ship": "Hiro", "count": 3, "race": "pirate" } ] }
```

In multiplayer the wreck is in the games it was spawned for (each player of `@a` gets their own); the other players see it
but can't dock at it. Spawn it for one player (`@trigger`, `@p`) when a squad shares the orbit.

**Pickups.** The `pickup` step (the command `pickup @a <station> <x y z> [unmarked] [named <name>]`) puts a glowing orb at
those coordinates of an orbit, its name on the HUD (`"marked": false` leaves the name off, for a field of many). It is only
shown: to collect it, put an **On** "near" at the same point (a radius of 100 m or more: the game checks four times a
second, and racing ships are fast) whose steps remove it (`"action": "remove"` with its name; `"clear"` takes them all) and
count it. The mission's end clears them. The GoF3 Ships mod's races are made this way:

```json
{ "type": "pickup", "station": "{st}", "at": "0 4000 30000", "name": "Checkpoint" },
{ "type": "on", "trigger": "near", "station": "{st}", "at": "0 4000 30000", "radius": 150, "steps": [
    { "type": "if", "condition": "cp == 0", "then": [
        { "type": "set", "variable": "cp", "value": "1" },
        { "type": "pickup", "action": "remove", "name": "Checkpoint" },
        { "type": "sound", "sound": "boost" } ] } ] }
```

**Wormholes.** The `wormhole` step (the **Open Wormhole** node; the command `wormhole @a <station> [x y z] to <station | void>
[for <seconds>]`) opens a wormhole like the Void's in a station's orbit, at the coordinates or about 4 km out beside the station's launch path (keep your own spot 2 km, 40000 units, off the line
straight ahead of the station, x 0 / y 0: ships launch along it and would be pulled in):
it grows in 3 seconds, pulls a ship within 2 km toward it, shows as "Wormhole" on the HUD, and flying in takes the player to
its destination, any station or `void` (the Void's home orbit), arriving from deep space with a wormhole closing behind. The
destination may be a variable: wire a value or a variable into the step's Destination (the node: a **Get Orbit** with a
number wired in), e.g. one picked with `random(0, 99)` or set by an Ask. It stays open until the step with no orbit (Close
Wormhole, `wormhole @a close`), its seconds run out (game time) or the mission ends; each player has one, a new one replaces
it, and it opens again whenever they come back to that orbit. Arriving fires `entered` there like any arrival.

```json
{ "type": "set", "variable": "dest", "value": "random(0, 99)" },
{ "type": "wormhole", "station": 78, "at": "-60000 10000 40000", "to": { "$value": "v1" }, "seconds": 120 }
```

with the value node `{ "id": "v1", "type": "variable", "name": "dest" }` (or simply `"to": "{dest}"`).

Mod missions run beside the game's own story. A mission whose mod is turned off waits in the save until the mod is back.

### Missions made in the Mod Builder

The Mod Builder's Missions kind makes story missions, bar missions and events without the Unity Editor, in a **node graph** like
the Editor's: a Start node, every step a node, the white wires the order they run in, a block's outputs (If's Then / Else,
an Ask's answers...) the steps it runs. Drag the canvas to move around, the wheel to zoom; drag from an output onto a node
to wire it (onto the empty canvas: a new step there, or cut the wire); drag a wired input away to cut it; double-click
the canvas for a new step; Delete removes the selected node; **Tidy up** lays the graph out. The panel on the right edits
the selected node (or the mission's settings), **Edit as JSON…** included. A node reached by no wire is drawn faded: it
doesn't run. **Value nodes** (+ Value: a number, the game's state, a variable, maths, a comparison, and / or / not,
random, min / max, the players, an orbit, ships alive, a ship's distance, cargo, docked at, visited, a mission's state)
wire into the small square inputs of a step's fields (conditions, numbers, stations, whom it is for) or into each other.
**List view** shows the same as an outline of steps. **Import an event graph…** reads a `.gof2event` from the Unity Editor:
converted into the builder's nodes (where they stood on the Unity canvas; the lines the builder has no node for stay
Command nodes, so it runs the same) or added as it is.

It writes `events/<name>.gof2mission`, which the game turns into an event graph's script when it loads it (**Show the script**
shows it). It can be written by hand too:

```json
{
    "format": "gof2mission", "title": "The first signal", "kind": "quest", "startsWhen": "visited(78)", "reward": 5000,
    "variables": [ { "name": "waves", "value": 2 } ],
    "steps": [
        { "type": "dialog", "pages": [ { "speaker": "Keith", "text": "A signal! {waves} waves of them.", "voice": "signal_1" }, { "speaker": "my_mod:vega", "text": "Come." } ] },
        { "type": "goto", "station": 78, "dock": false, "objective": "Fly to Var Hastra" },
        { "type": "repeat", "count": "waves", "as": "wave", "steps": [
            { "type": "fight", "station": 78, "name": "Raider", "ships": [ { "ship": 2, "count": 3, "race": "pirate" } ] }
        ] },
        { "type": "ask", "question": "Go on?", "speaker": "Keith", "answers": [
            { "text": "Yes", "steps": [ { "type": "reward", "credits": 5000 } ] },
            { "text": "No", "steps": [ { "type": "fail", "title": "You gave up." } ] }
        ] },
        { "type": "checkpoint" },
        { "type": "complete" }
    ]
}
```

`kind` is `quest`, `mission` (a bar mission: `offer`, `client`, `stations`, `players` [min, max] as the Start node's, and
in single player `startsWhen` and `"once": true`, see above) or
`event` (started by `/event <name>` or another one's Next event step). A `quest` with `"repeat": <minutes>` (the Start node's
"Comes again after", the builder's "Comes again after (minutes of play)") is an encounter that comes back: done, it isn't
done for good but starts again that many minutes of playing time later, as soon as its `startsWhen` holds (add a chance to
it, `random(1, 300) == 1`, for a random moment); `quest(<name>)` reads 2 meanwhile. `randomstation()` picks a station of a
system on the map for a roaming encounter (not the player's own orbit, an empty or special orbit, a story's orbit, Loma, a
pirate base's system; -1 when none), kept in a variable; a text shows its name with `{stationname(st)}` and its system's
with `{systemname(st)}`, and a station field takes the variable (`"station": "{st}"`). `variables`: numbers the mission keeps (`setting`: true
lets `/event name x=3` change it); a text shows one in braces (`{waves}`). Without `complete` / `fail` a mission completes after
its last step (`"autoComplete": false` turns that off). Every step takes `who` (whom it is for: `@a` everyone the mission is for,
the default; `@trigger` the player a When block is about; any selector). The steps:

| Group | Steps (fields) |
|---|---|
| Story | `dialog` (pages: speaker, text, voice), `radio` (speaker, text, voice), `objective` (text, station), `title` (title, subtitle, seconds), `note` (text: a comment) |
| Flow | `wait` (seconds), `waituntil` (condition, timeout), `if` (condition, `then`, `else`), `while` (condition, `steps`), `repeat` (count, as, `steps`), `parallel` (`steps`: beside the rest), `every` (seconds, `steps`), `on` (trigger: died, docked, launched, kill, cleared, entered, near, respawned, hacked, pad (docked at a station's pad outside), joined, left, pvpkill; station; near: at "x y z", radius; `steps`), `ask` / `vote` (question, speaker, seconds, `answers`: [{ text, steps }]; a vote's `none` runs when nobody voted), `random` (`branches`: [{ weight, steps }]), `set` (variable, value), `checkpoint` (name), `startquest` (mission), `startevent` (event, settings), `complete` (reward, title), `fail` (title), `stop` |
| Places | `goto` (station, `dock` true: docked there, false: in its orbit, objective), `waypoint` (station, at), `pickup` (action: show / remove / clear, station, at, name, marked: see below), `wormhole` (station, at, to, void, seconds; no station: closed), `target` (station), `questorbit` (station, on), `system` (system, action: show / hide / reset), `restrict` (what: jumps / docking / all / khador (only the Khador Drive) / off), `respawn` (station, at, spread, delay), `teleport` (station, dock, at) |
| Ships | `fight` (station, ships: ship / count / race / behaviour, name, quiet; ends when none is left), `spawn` (ship, count, race, behaviour, name, at, turrets, hull, guns: their hull and gun damage x that), `spawntype` (npc: an [NPC ship type](#npc-ships) like `pirate_boss` or `mod_id:id`, name, at), `wreck` (race, name, at: a hackable freighter wreck), `shipgoto` (ship, to, speed, radius, fight, then), `shiproute` (ship, route "place / place", loop, ...), `shipfollow` (ship, leader, offset, escort, speed), `shipattack` (ship, target), `shipaction` (ship, action: hold / resume / jump / dock / flee, seconds), `provoke` (race, within) |
| The player | `reward` (credits, items, title), `give` (item, amount, mount), `cargo` (item, amount, station: waits until the hold has it; `take`: then handed in, out of the hold), `take` (item, amount: empty for all, mounted: the mounted ones too, e.g. the Khador Drive), `credits` (amount), `ship` (hull, own), `lend` (hull, keep, back: see [Loaner ships](#loaner-ships)), `heal`, `ammo`, `kill`, `reveal`, `peace` |
| Cutscenes | `cutscene` (letterbox, freeze, invulnerable), `camera` (from, to, seconds, look, follow, fov, shake), `chasecamera`, `cutsceneend`, `fade` (direction: out / in / clear, seconds, colour), `letterbox` (on) |
| Sound and screen | `music` (track), `sound` (sound), `timer` (seconds, label, stop) |
| Score | `points` (amount), `scoreboard` (show, title), `score` (mode), `winner` (mode), `pvp` (on) |
| Other | `command` (line: any event command) |

Places (camera, ship orders) are written as for the [cutscenes](#cutscenes). A field wired to a value node holds
`{ "$value": "v1" }`, the node in `"values"`: `[ { "id": "v1", "type": "compare", "op": ">=", "a": { "$value": "v2" }, "b": 5 } ]`.
The graph view keeps each node's place (`"pos"`), the chains wired to nothing (`"loose"`) and the Start node's place.
Conditions also know `docked(<station>)` and `inorbit(<station>)`.

### Loaner ships

A trial mission can lend the player a ship, as the Valkyrie story lends the S'Kanarr: the **Lend a ship** step (`lend`,
the command `/lend [players] <ship> [keep] | back`). The player's own ship is parked with its equipment, cargo and damage,
and they fly the loaner: a fresh hull with an empty hold and nothing mounted (mount its guns with **Give Item**'s
Mount), or with **keep** the player's own equipment and cargo moved over. Meanwhile the hangar is closed, so the loaner
can't be sold or traded in. When the mission ends (complete, failed, discarded or stopped) the own ship comes back by
itself, with everything it had; a fresh loaner's guns and cargo go with it (a **keep** loaner's come back with the
player). **Their own ship back now** ends the loan before that. Only one loaner at a time, and only a ship a player can
own (no freighters or capital ships). Lent while docked, the hangar shows the loaner; lent in flight, the ship changes
where it is. Loading a save made with a loaner whose mission isn't under way in it gives the own ship back.

```json
{ "type": "lend", "hull": "my_mod:prototype" },
{ "type": "give", "item": 48, "amount": 1, "mount": true },
{ "type": "goto", "station": 78 },
{ "type": "complete" }
```

### Moving ships

The **Ships** nodes give the ships a graph spawned orders. A ship is named by its **Spawn** node's Name; a numbered group
("Blue Wing 1", "Blue Wing 2"...) all take the order and spread out beside each other.

| Node | |
|---|---|
| **Fly To** | Flies to a place, then **Then**: Resume (its own flying and fighting), Hold (stays there; neither flies nor shoots), Vanish, or Jump Out. **Speed** in units per ms (0 = its own: a fighter 2, a freighter 1), **Radius** how close counts as there (default 2500 game units: ships can't turn on the spot), **Fight** to take on the enemies it meets on the way. |
| **Fly Route** | The same through several places, `/` between them; **Loop** for a patrol. |
| **Follow** | Keeps its place beside the player or a ship (**Offset** `right up forward`, default 1500 behind); **Escort** also fights what it meets and comes back. Freighters follow too: an escort mission is a freighter on a Fly Route with fighters on Follow + Escort. |
| **Attack** | Goes after one target (the player or a ship), whatever its standing; when that is gone it resumes. |
| **Ship Action** | Hold, Resume, Jump Out (accelerates away, gone), Dock (flies into the station, gone), Flee (away from the player at full speed for Seconds, then jumps out). |

Places as the cutscene camera's: `x y z` or `station x y z` (orbit coordinates), `player right up forward` (from the player's
ship: `player 0 0 20000` is far ahead of it), `ship <name> [x y z]`. A place relative to the player or a ship is taken when
the order arrives (a route doesn't move along with them); Follow and Attack keep after their ship.

To wait for a ship, two values work in **Wait Until** / **If** (an **Expression** node): `alive(Blue Wing)` (how many of
them still fly) and `distance(Blue Wing, 0, 0, 30000)` (game units from the nearest of them to that point, -1 when none);
`distance(player, 0, 0, 30000)` is the same for the event's players alive in space, so a graph can ask a player to stay near
a point (an **Every** that counts the seconds while it is 2000 or less: a scan).
A ship that docked, jumped out, vanished or fled is gone but nobody's kill, and an event's ships never come back by
themselves (the orbit's own fighters relaunch from the station, a graph's don't).

In typed commands (multiplayer admins): `/npc Blue Wing goto 0 0 30000 then hold`, `/npc Blue Wing route 0 0 20000 / 9000 0
30000 loop`, `/npc Blue Wing escort player offset 0 300 -2500`, `/npc Blue Wing attack ship "Pirate Boss"`, `/npc Pirate Boss
flee seconds 5`, `/npc Blue Wing dock`, `/npc Blue Wing hold | resume | jump | speed 3`.

### Cutscenes

The **Cutscene** nodes stage a scene in flight. They work in story missions, bar missions and multiplayer events alike.

| Node | |
|---|---|
| **Start Cutscene** | Cinematic mode: the HUD goes (the radio and conversations stay), the player can't steer, fire or use the autopilot, the cinema bars slide in (**Letterbox**). **Freeze ship** holds the ship still (else it flies on, straight); **Invulnerable** keeps it from harm. It lasts until **End Cutscene**, also across a teleport to another orbit. |
| **Camera Shot** | Puts the camera at **From**, moving to **To** over **Seconds** (eased; leave To empty for a still shot), looking at **Look At** every frame. **Follow**: the places that move (the player, a ship) are followed while the shot runs. **Fov** in degrees (0 = the game's 70), **Shake** 0 to 1. The shot holds until the next one: put a **Wait** after it. |
| **Chase Camera** | Back to the normal camera behind the ship (the cutscene itself goes on). |
| **End Cutscene** | Everything back: the camera, the HUD, the controls, the bars. |
| **Fade** | **Out**: the screen to a colour (held), **In**: from it back to the game, **Clear**: gone at once. The colour as `rrggbb` (`000000` black, `ffffff` white). A conversation or the radio stays readable over it, so "fade out, talk, fade in" works. |
| **Letterbox** | Only the cinema bars, on or off. |

A place for From, To and Look At is one of:

- `player right up forward`: game units in the player's ship's own frame, so `0 600 -1338` is where the chase camera sits,
  `0 300 4000` in front looking back, `-3000 500 0` off the left wing. `player` alone is the ship itself.
- `station x y z` or just `x y z`: game coordinates of the orbit (the station sits at 0 0 0; `/pos` in multiplayer shows yours).
- `ship <name> [x y z]`: a ship spawned with that name (**Spawn**'s Name), plus world-axis offsets.

An example: a pirate boss arrives.

```
Spawn (Pirate Boss)  ->  Start Cutscene (Freeze ship)  ->  Camera Shot (From "ship Pirate Boss 0 800 -3000",
Look At "ship Pirate Boss", Follow, Seconds 0)  ->  Radio (the boss's threat)  ->  Wait 4  ->  Camera Shot (From "player 0 300 4000",
To "player -2500 400 1500", Seconds 3)  ->  Wait 3  ->  End Cutscene
```

The cutscene ends with the graph too (a mission finished or failed, an event stopped), so the player is never left stuck in it.
The same in typed commands (multiplayer admins): `/cutscene start freeze`, `/camera player 0 300 4000 to station 0 2000 20000 over 5
look station`, `/camera chase`, `/fade out 1 ffffff`, `/letterbox on`, `/cutscene end`; a ship name with spaces goes in
"quotes" there (`ship "Pirate Boss"`).

## Characters

A mod can bring its own cast: `characters.json` lists characters with a name and a portrait, and every place a speaker is
named can use them: a **Dialog** page, a **Radio** call, an **Ask** / **Vote** question, a bar mission's **Client**.

```json
[
    {
        "id": "vega",
        "name": { "en": "Captain Vega", "de": "Kapitänin Vega" },
        "portrait": "portraits/vega.png",
        "race": "terran",
        "gender": "female"
    }
]
```

| Field | |
|---|---|
| `id` | **Required.** a-z, 0-9, _ and -. |
| `name` | The name shown over the portrait (a string, or per language). Default: the id. |
| `portrait` | **Required.** A PNG for the game's portrait box, 4:5 (320 x 400 stays sharp on big screens). It covers the box, its top kept. Leave it transparent around the character and the game's portrait background shows behind, like the game's own faces. |
| `race`, `gender` | What the game draws by race: the figure standing in the Space Lounge when the character is a bar mission's client, the name's colour in the dialogue. Default `terran`, `male`. |
| `mirrored` | `true`: drawn mirrored (the game mirrors Keith's portrait). |
| `frame` | `false`: without the faint scanlines the game draws over every portrait (a light screen look, about 10 % dark lines). |
| `background` | `false`: without the dark, half-transparent backdrop behind the picture. It only shows through transparent parts of your PNG (a cut-out character), so a full picture looks the same either way. |
| `replaces` | A story character (`"Gunant Breh"`, `"Keith"`, or a speaker number) whose portrait this one takes everywhere, the game's own story included, and whose name too when this character has a `name` (the dialogue and radio headers, `%player%` for Keith). Other characters' lines that mention the old name, and the voices, stay. The Mod Builder's **Replace a game character…** starts one from that character's own portrait and name. |

Name the character in a speaker slot by `vega`, by `frontier_systems:vega` (always unique, also across mods) or by its name
(`Captain Vega`). `vega as Harbour Master` shows another name over the same face, like the story characters. In typed
commands: `/dialog frontier_systems:vega : Welcome to Kepler.`. The dialogue tints character names like the story's people.

## Voice-over

Any **Dialog** page or **Radio** line can have voice-over: add `[voice <clip>]` to its text, for example
`Keith : I picked up a strange signal. [voice keith_signal_01]`. The tag doesn't show.

- `<clip>` can be one of the game's own voice lines by its name (`MSG_MISSION_24_NO_EQUIPMENT_INSTALLED`, the names in
  `Assets/Audio/*_VOICE_*`).
- Or it is a file in the mod's `voices` folder: `voices/<clip>.ogg` (`.wav` and `.mp3` work too) for every language. A
  language of its own goes in `voices/<language>/<clip>.ogg`, any of the game's text languages (`en`, `de`, `fr`, `es`,
  `it`, `nl`, `pl`, `ru`, `pt`, `zh`, `hi`): with the voice language option on "as text" a player hears the one in their
  text language, then the game's voice language (German or English), then `voices/<clip>.ogg`; with the option on English
  or German, that language's.

The window waits a moment for the clip to load, types the text along with it, and turns the page when the line ends (with
the player's **auto-advance** option on), like the story's own voices.

**Replace one of the game's lines**: name a file in `voices` like the line (`voices/MSG_MISSION_24_NO_EQUIPMENT_INSTALLED.ogg`)
and it is spoken instead, everywhere the game uses that line, the story included. `voices/` replaces it in every language,
`voices/<language>/` in that language only (a French file there gives French players a French line). The Mod Builder's Audio page lists the lines (**Replace a
game line**).

## Music

Put tracks in the mod's `music` folder (`.ogg`, `.wav` or `.mp3`):

- **Replace a track**: name the file like one of the game's tracks and it plays instead, everywhere the game plays that track.
  The names: `Space_Terraner`, `Space_Vossk`, `Space_Nivelianer`, `Space_Midorianer` (calm space by race), `Station_Terraner`,
  `Station_Vossk`, `Station_Nivelianer`, `Station_Midorianer`, `Station_Valkyrie` (docked), `Space_Battle_Low`,
  `Space_Battle_Medium`, `Space_Battle_Full`, `Space_Battle_Void` (fights), `Space_NoCombat_Void` (the main menu and the Void),
  `Space_NoCombat_Valkyrie`, `HomeBase_NoCombat`, `HomeBase_Station` (the Kaamo Club), `IntroAtmo_02`, `TimeShift_Start`,
  `Errkt_CutSeq_01`, `OutroSong_02b` (the ending), and the add-ons' tracks in `Assets/Audio/DLC_MUSIC` / `DLC2_MUSIC`.
- **Add a track**: any other name. The event graphs' **Play Music** node plays it (its **Mod track** field), so does
  `/music <name>`, and your systems and stations can use it as theirs:

```json
{ "id": "kepler", "name": "Kepler", ..., "spaceMusic": "kepler_space", "stationMusic": "kepler_bar" }
```

`spaceMusic` is the calm music in the system's orbits and `stationMusic` the music docked at its stations. A station's own
`"music"` in stations.json beats its system's. Both work in an `override` of an original system or station too.

`battleMusic` is the music of the system's fights: one track for all of them, or three, `["low", "medium", "full"]`, for
the game's three battle tracks (1-2 enemies around, 3-4, 5 or more). Fights with the Void, Specters or a Most Wanted
criminal keep their own music.

## Sound effects

Put sounds in the mod's `sounds` folder (`.ogg`, `.wav` or `.mp3`), each named like one of the game's sounds: it plays
instead, everywhere the game plays that sound. The names are the file names in the game's `Assets/Audio` folders (one folder
per sound bank), for example `Target_Lock_v08` (a ship lock), `Blaster_Nsaan_01` (the N'saan's shot), `Jumpgate_3b`,
`Engine_09` (an NPC engine). `Reference/research/fmod_event_ids.txt` in the repository lists every sound event with its
files. Voices are sounds too: a voice line's file name replaces that line (the German voices are the same names with `de_`
in front, so each language is replaced on its own). Music has its own `music` folder (above).

**Add a sound**: any other name is a new sound of your own. The missions' **Sound** step and the event graphs' **Play Sound**
node (its **Mod sound** field) play it by that name, so does `/sound <name>`.

A sound the game picks at random from several files (most shots and explosions) needs each file replaced to always sound
yours. Only a weapon's own shot can be set on the item instead (`fx` → `shot`, above). A ship's own engine and boost sounds are
[Ship sounds](#ship-sounds), a customizable ship's weapon sounds its hardpoint rules' `sound`.

## Textures (skins)

Put images in the mod's `textures` folder (`.png`, `.jpg`, `.tga`, `.bmp`, `.dds` or `.aei`), each named like one of the game's textures: it is used instead
on every ship, station and object that uses it. The names are the file names in the game's `Assets/Textures` folders, for
example `ship_028_terran_diffuse` (the Veteran's hull paint), `ship_028_terran_normal_specular` (its bumps and shine) or
`ship_010_terran_diffuse` (the Phantom). A ship's hull texture is `ship_<number>_<race>_diffuse`; the race is the one in its
model's name, which isn't always the race that sells it.

Keep the original's layout: the image is wrapped around the model by the same UV mapping, so paint over a copy of the
original texture (or use a skin maker that does). Its size may differ; 2048 x 2048 like the originals looks best. A
skin changes every ship of that model, yours and the NPCs'. Names with `_normal` or `_metallic` are read as data, the rest as
colours.

```
veteran_skin/
  mod.json
  preview.png
  textures/
    ship_028_terran_diffuse.png
```

### One ship only

Some textures are shared: `glow_terran` (the lights of every Terran ship), `ship_engine_glow` (every ship's engine glow), a
race's freighter and wreck textures. To change one only on a single model, put it in a folder named after that model's
assembly inside `textures` (the ship's model name, as in its hull texture: `ship_028_terran` for the Veteran). It is used on
that model instead of the game's, and before a `textures/<name>` file of the same name, which every other ship keeps using.

```
veteran_skin/
  textures/
    ship_028_terran_diffuse.png
    ship_028_terran/
      glow_terran.png          (the Veteran's lights only)
      ship_engine_glow.png     (its engine glow only)
```

The Mod Builder's Skins tab writes these for you: a shared texture has "This ship..." and "All ships..." buttons.

### A new ship on a game model

A new ship that flies one of the game's models (`"model": "gof2:ship_028_terran"`, see
[The game's own models](#the-games-own-models)) uses that model's textures, so a skin of the model above changes the game's
ship too. To give your ship textures of its own, put them in `textures/ships/<ship id>/`, named like the game's (the `id` of
its ships.json entry). They change that ship only: the game's Veteran and every other ship on its model keep their look.
What the folder doesn't replace still takes the model's other skins (`textures/<model>/`, `textures/<name>`).

```
custom_veteran/
  ships.json                   [ { "id": "red_veteran", "name": "Red Veteran", "model": "gof2:ship_028_terran", ... } ]
  textures/
    ships/
      red_veteran/
        ship_028_terran_diffuse.png    (the Red Veteran's hull only)
        glow_terran.png                (its lights only)
```

In the Mod Builder: **Its own textures…** on the ship's Ship tab, or **Skin a game ship** in the Skins tab, which lists your
ships on a game model too. Their Replace buttons write to that ship's folder.

### Planets, suns and rings

The same goes for the planets, suns and rings in the sky: a texture named like the game's replaces it in every system that
shows it. The planets are `planet_000_small` ... `planet_019_small` (the other stations' planets) and `planet_000_big` ...
`planet_019_big` (the orbit's own big planet), `planet_void_small` / `_big`, and the Supernova add-on's `sn_planet_024_small`
... `sn_planet_026_big`; the suns `sun_000` ... (and `sn_sun_011`, the supernova); the rings `sn_planet_ring`. Each is one
whole image (a disc on a transparent background, not a wrapped map), so keep the original's size and keep the transparent
edge: the sun swells when it is near the middle of the screen, and a glow reaching the image's edge then shows as a square.

To draw your retextured planets as stars, add a `backdrop.json` to the mod (the last mod in the load order that has one wins):

```json
{ "planets": "star", "planetGlow": 2, "planetFlare": 0.35 }
```

| Field | Meaning |
|---|---|
| `planets` | `"star"`: the planets whose texture your mod replaces face the camera like the sun, aren't mirrored and have no ring |
| `planetGlow` | How much their bright core glows with the game's bloom (default 2; 1 = no extra glow) |
| `planetFlare` | The sun's flare at this share of its strength when you look at them: a slight swell and a horizontal streak (0..1, default 0.35; 0 = none) |

Planets you don't retexture stay planets.

### Flight HUD markers

The combat HUD's images are replaced the same way, by their names in the game's `Assets/Resources/GoF2Hud` folder: the ship
markers `ship_dot_<faction>` (a far ship), `ship_ring_<faction>` (a far locked ship), `ship_ring_open_<faction>` (a far ship
with the "Far ship markers" option on Ring), `ship_bracket_<faction>` (a locked ship's bracket), `ship_bar_<faction>` /
`ship_fill_<faction>` (a near ship's hull bar), with `<faction>` `enemy`, `friend` or `neutral`; the containers `crate_dot`,
`bracket`, `crate_off`; the lock ring's frames `lock_00` ... `lock_23`; the status bars and the hit arcs. Each is drawn at its
own size, placed where the original's is, so keep the transparent edge and roughly the original's size. Other HUD images
(the crosshair, the navigation markers, the touch controls) can't be replaced yet.

```
small_markers/
  mod.json
  textures/
    ship_dot_enemy.png
    ship_dot_friend.png
    ship_dot_neutral.png
```

## The game's own models

Anywhere a mod takes a 3D model, it can name one of the game's own models instead of a `.glb` file: `"gof2:<model name>"`,
with the names in `Modding/ai/gof2-modding/reference.md` ("Models and their textures"). No 3D file is needed, and the game's
model comes with everything it has: its textures, lights, engine glows, animations and level of detail.

| Where | |
|---|---|
| ships.json `model` | A new ship on a game ship's model: `"model": "gof2:ship_028_terran"`. It gets that ship's hardpoints (unless the entry has its own `mounts`), sits on the hangar pads like that ship (unless it gives `hangarHeight`) and uses its shop icon (unless it gives `icon`). Textures of its own go in `textures/ships/<ship id>/` (see [A new ship on a game model](#a-new-ship-on-a-game-model)). On an `override` it gives an original ship another ship's model. `modelLength`, `modelYaw` and the other model fields don't apply: the model is used as made. |
| stations.json `model` | A station on any game model, at its own size unless `modelSize` is given: `"model": "gof2:station_078_midorian"`. |
| items.json fx `turret` | A turret on any game model: `"turret": "gof2:turret_003_ship_mounted"` (a model without the turret's `pivot` / `*_gun` nodes turns as one piece), or `"turret": { "item": 48 }` for that game turret's model with its own muzzle and shot start. `scale` and `offset` work on both. |
| items.json fx parts | A projectile, muzzle flash or impact on a game model: `{ "model": "gof2:rocket_002", "size": 300 }`; or another item's own part with `{ "item": 46 }` (see [Weapon effects](#weapon-effects)). Two props that aren't in the model list work here too: `"gof2:bra"` and `"gof2:beer"`, the bra and beer can that drift through the ending (see `Modding/Examples/exploding_bra`). |
| items.json / ships.json `icon` | One of the game's shop icons: `"icon": "gof2:item_046"`, `"icon": "gof2:ship_028"`. |

The Mod Builder offers the same without typing names: **+ New ship on a game model** and **Game model…** on the Ship tab,
**Game item…** for an item's icon, **Game turret…** for a turret's model, and the **Look and sound** section for a weapon's
projectile, muzzle flash, impact, shot sound and blast sound from any of the game's weapons.

## Building on other mods

A mod can use what another mod adds: list it in `dependencies` (with the lowest version it needs, `"ship_pack>=1.2"`), and
refer to its content by **key**, `<mod id>:<id>`. The key stays the same whatever number the content gets in a player's game:

- in items.json / ships.json: `"base": "ship_pack:pulse_cannon"`, `"override": "ship_pack:falcon"`;
- in systems.json / stations.json: `"gates": ["frontier_systems:kepler"]`, `"system": "frontier_systems:kepler"`;
- in event graphs: ship, item and station names in Spawn, Give Item, Reward, Change Ship, Get Orbit and Teleport
  (`starwars_ships:millennium_falcon`), and in `cargo()`, `has()` and `visited()`. Names work too, but a key can't clash with
  another mod's name.

The Mods screen shows each dependency's state (on, off, not installed, too old) and which mods need the selected one. A mod
whose dependency is missing or too old stays off and says why.

## Load order

The Mods screen lists the mods that are on in load order. **Earlier** / **Later** move the selected one. Where two mods change
the same item, the later one wins. A mod's dependencies always load before it.

## Saves

Saves remember which mods were on and keep each mod item and ship at its own number, so turning mods on, off or reordering
never mixes them up. Loading a save made with a mod that isn't on now warns first. Loading anyway removes that mod's items from
your ship, cargo, Kaamo Club storage and shops, and refunds them at the price they had; flying one of its ships, you get a
Phantom instead (the ship refunded, its equipment in your hold); docked at one of its stations, you dock at Var Hastra instead.
To keep them, turn the mod back on before loading.

## Multiplayer

The host decides in the Host card: **Mods: Off** plays the original game for everyone, whatever mods they have on. **Allowed**
plays the host's mods. Players joining then need the same mods installed with exactly the same files (on or off doesn't matter);
otherwise they are told which ones they lack. Files the mod's `mod.json` lists in `multiplayerIgnore` aren't compared, nor are
`mod.json`'s name, description and credits: a phone build with lighter models and textures (the same file names, the same
data) can join a PC build's game and the other way round. A ship's lighter model is also its hull for the shots that player
fires, so the hits can differ a little at the edges of a hull. The server browser tags such games **Modded** and lists their mods. A dedicated
server uses every mod in its Mods folder with `-allowmods` (`ALLOWMODS=1` in its start script).

## When something is wrong

A mod with a broken file shows **Error** in the Mods screen, and the selected mod lists the problem with the file and line
(`items.json line 2: unknown stat "dammage"`). A mod with errors can't be turned on. Smaller problems (an unknown field) show as
warnings and are skipped. The game's log (`Player.log`) lists every problem too.

## Example

`Modding/Examples/plasma_arsenal` in the repository is a complete small mod: two new weapons (the Plasma Lance with its
own bolts, muzzle flash, impact and shot sound), one rebalanced shield, German texts and a preview image. Copy it into your Mods folder to try it. The Star Wars, Star Trek and Halo ship mods (made from
PR #34) show ships with models, materials, glows, two turrets and lounge sellers. `Modding/Examples/frontier_systems` adds the
Kepler system with jumpgate routes and two stations: Kepler Prime with its own station model, its own planet and its own
hangar and bar (GLBs built from simple shapes, with their markers), and the Rho Freeport looking like an original with a
Nivelian interior; the system has its own sun and its own nebula sky. It also brings a character, Captain Vega, with a (placeholder) portrait.
