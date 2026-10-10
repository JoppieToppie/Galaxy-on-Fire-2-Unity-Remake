# Making mods for the Galaxy on Fire 2 Unity Remake

A mod is a folder (or a `.zip`) of plain files: JSON for data, PNG for images. Mods contain no code, so a mod you download can't
harm your computer. Players turn mods on and off in the main menu's **Mods** screen.

**Making a mod with an AI assistant:** `Modding/ai/gof2-modding` is this guide condensed for Claude, ChatGPT and the like,
with every original item, ship, system and station number in `reference.md`. See `Modding/ai/README.md` for how to use it.

What mods can do so far:

- **Add new items**: weapons, missiles, equipment and goods. Each new item is based on an existing one and takes its model,
  icon, sounds and behaviour, with its own name, description, stats, prices and availability.
- **Change existing items**: rebalance prices, stats, tech levels and availability.
- **Give weapons their own look and sound**: projectiles, muzzle flashes and impacts from your own sprites or 3D models,
  your own shot and explosion sounds.
- **Add new ships**: your own 3D model (glTF / GLB) with its stats, weapon mounts, turrets, engine glows and textures, sold
  by visitors in the Space Lounges and by the ordinary ship dealers.
- **Add blueprints**: for ships (skins too) and items, with their ingredients and autocomplete price, found in lounges, in
  hackable derelict freighters and in the wrecks of the ships the player destroys.
- **Add new-game options**: a card the player switches on when starting a new game (like "All Canon Ships"), and content
  that only appears with it, or only later in the game (after leaving Mido, after the Supernova...).
- **Change existing ships**: armour, cargo, price, slots, handling, and their model.
- **Add star systems and stations**: on the star map, linked by jumpgates, with their own orbits, shops and bars, their own
  station models (glTF / GLB), planets and suns, and their own hangars and bars to dock in.
- **Add quests and bar missions**: story chains and side jobs made as event graphs in the Unity Editor, with dialogue,
  voice-over, objectives, map markers, checkpoints that saves keep, rewards and failure.
- **Add and replace music**: replace any of the game's tracks, or add new ones for your systems, stations and quests.
- **Replace sound effects**: any of the game's sounds, from the guns to the menu clicks and the voices.
- **Reskin ships and objects**: replace any of the game's textures, for example a ship's hull paint.
- **Build on other mods**: a story mod can use the ships, items and systems of a mod it depends on.
- **Make a whole new game**: a campaign of its own under New Game, with its own start, galaxy, shops and NPC ships.
- **Translate** their own texts into any of the game's languages.

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
| `preview` | The image shown in the Mods screen, PNG or JPG. Default `preview.png`. 16:9 looks best, for example 640 × 360. |
| `dependencies` | Ids of mods that must be on too, optionally with the lowest version: `["ship_pack", "frontier_systems>=1.2"]`. They are loaded first, turning this mod on turns them on as well, and turning one of them off turns this mod off. See [Building on other mods](#building-on-other-mods). |

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
looks, sounds and works like it: its icon, projectile, impact, shot sound, turret model and any special behaviour (beam lasers,
cluster missiles, cloaks...). Everything you give in the entry replaces the base's value. Blueprint recipes aren't copied.

### Changing items

`override`: the item to change, an original item's number or `"mod_id:item_id"` of another mod's item (that mod must load
earlier). Only the fields you give change. Giving `name` or `description` renames the item.

### Fields

| Field | |
|---|---|
| `name`, `description` | A string, or one text per language (see Texts below). |
| `techLevel` | 1 to 10. Shops sell only items up to their station's tech level. |
| `occurrence` | How often shops stock it (the originals use 0 to 90; 0 = never in a random shop). |
| `minPrice`, `maxPrice` | The price range. A station's price lies between them, by distance to the cheapest and dearest systems. |
| `price` | Shorthand: `1500` (both) or `[1200, 1500]`. |
| `lowestPriceSystem`, `highestPriceSystem` | System numbers where it's cheapest and dearest. |
| `vosskOnly` | `true`: only Vossk systems sell it. |
| `alwaysSoldAt` | A station number that always stocks it, whatever its tech level (78 = Var Hastra, the starting station; -1 = none). |
| `stats` | The item's numbers, see below. |
| `attributes` | The original game's attribute numbers directly: `{ "9": 14 }`. For anything `stats` has no name for. |
| `defaultEconomy` | The same fields again, used when the player picked the Default Economy (the PC / Mac prices) for their game. |
| `fx` | A weapon's own look and sound, see [Weapon effects](#weapon-effects). |
| `icon` | Its own shop icon: a PNG in the mod, 180 x 88 like the originals (frame and background included; copy one from `Assets/Resources/GoF2Icons` to start from). Without it the item shows its base item's icon. An `override` with `icon` gives an original item a new one. |
| `available` | A [condition](#conditions) that must hold before shops stock it or it turns up in loot (an `override` can hold back an original item too). |

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

Which stats an item uses depends on its category, so copy the stats its base item has (see `items.json` and
`item_attributes.json` in `Assets/Resources/GoF2Data`). items.json's own stat names (`loadingTimeMs`, `range`,
`projectileSpeed`...) work too.

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

### Weapon effects

A weapon (a new one or an `override` of an original) can look and sound like its own thing with `fx`. Anything it leaves out
stays its base item's. What the weapon *does* never changes: a beam stays a beam, a mine a mine, a homing missile homes.

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

| Field | |
|---|---|
| `projectile` | What flies. For a beam weapon the beam itself (stretched from the gun to what it hits along its length), for a missile the missile. |
| `muzzle` | The flash at the gun on every shot. |
| `impact` | What shows where a shot hits. |
| `shot` | The sound of a shot: a file, or a list of files (one is picked at random each shot). `.ogg`, `.wav` or `.mp3`. |
| `shotLoops` | `true`: the shot sound loops while the trigger is held (like the auto-cannons); `false`: one per shot. Default: the base item's. |
| `explosionSound` | Bombs, mines and blast weapons: the sound of the blast (a file or a list). |

Each of `projectile`, `muzzle` and `impact` is either `false` (none at all) or an object:

| Field | |
|---|---|
| `sprite` | A PNG drawn on a flat square that always faces the camera. Draw it white on black (or transparent): bright = visible, and `color` tints it. |
| `model` | Or a glTF / GLB model. With `texture` it is drawn glowing like a sprite with that PNG; without, with the model's own materials (lit by the sun). |
| `size` | Its size in game units (20 units = 1 m): the sprite's width, or the model's largest extent. Default 200. The game's own effects are big: a blaster bolt is about 1600 units across, an impact glow several thousand. |
| `color` | A tint, `[r, g, b]` or `[r, g, b, a]` from 0 to 1, or `"#rrggbb"`. Default white. |
| `glow` | How much it blooms; above 1 glows under the bloom option (the game's effects use 2.5). Default: as the game's effects. |
| `additive` | `true` (default): light added to what is behind it, like fire, plasma and lasers. `false`: drawn over it, using the PNG's transparency (smoke, solid shells). |
| `lifetime` | Milliseconds it lasts, for `muzzle` (default 100) and `impact` (default 300). A projectile lasts as long as it flies. |
| `grow` | Its size at the end of its lifetime, times the start (`2` = twice as big). Default 1. |
| `fade` | `false`: it doesn't fade out over its lifetime. Default `true` (projectiles don't fade). |
| `spin` | Turns about itself, degrees per second. |
| `alongFlight` | Sprites: stretched along the direction it flies and turned about it to face the camera, like a laser bolt, instead of a round sprite. Use with `stretch`. |
| `stretch` | Sprites: its length as a multiple of `size` (a 64 x 256 PNG wants `4`). Default 1. |
| `rotate` | Models: `[x, y, z]` degrees, when the model doesn't point forward (+Z). |

The game loads them with the mod at the start (the loading screen waits). Every gun of that item uses them: the player's,
NPC ships', turrets, sentry guns, and other players' in multiplayer.

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
| `race` | Who builds it: 0 Terran, 1 Vossk, 2 Nivelian, 3 Midorian, 8 pirate, 9 Void. Prices are 1 % lower in its race's systems. |
| `armor`, `cargo`, `handling` | Hull points, hold size (t), handling (the originals use 60 to 160). |
| `price`, `priceDefault` | The price, and the price in the Default Economy (0 = the same). |
| `slots` | `primary`, `secondary`, `turret`, `equipment`. A ship may have several turret slots, one per turret mount. |
| `model` | **Required.** The ship's model, a `.glb` (or `.gltf`) file in the mod. Shots hit its real shape, and the player's ship slides along it: every mesh counts except those with an additive / glow material, so keep invisible helper meshes out of the file. |
| `modelLength`, `modelYaw` | Its length nose to tail in game units (the Phantom is about 1000; 20 units = 1 m), and a turn in degrees when the model's nose doesn't point forward (+Z). The model is scaled to that length. |
| `icon` | The shop icon, a PNG of 180 x 88 like the originals (the ship on the plate). None = the Phantom's. |
| `hangarHeight` | How high its pivot sits above the hangar pad, in game units (the originals 140 to 400). |
| `mounts` | Where guns and exhausts sit, in game units from the ship's centre (x right, y up, z forward): `slotType` 0 primary gun, 1 secondary, 2 turret (`"upsideDown": true` hangs it under the hull), 3 engine exhaust (the flame and its particles; `turretAngles[0]` sizes the particles; `"glowColor": [r, g, b]` (0 to 1) colours that exhaust's flame and its exhaust particles; `"glowSize": [halfWidth, halfHeight, length]` (game units) makes the flame an ellipse of that size, its flared ring `length` behind the nozzle). |
| `engineGlowRadius` | Size of the engine flame at each exhaust mount (game units, default 24); a mount's `glowSize` replaces it. |
| `engineGlowColor` | `[r, g, b]` (0 to 1): the flame colour of every exhaust without its own `glowColor`. None = the game's own blue-white flame. |
| `materials` | Optional. Replace the model's own materials, see below. |
| `throttleGlow`, `extraGlows` | Optional. Parts of the hull that glow with the throttle, see below. |
| `lounge` | Optional. A visitor in the Space Lounges sells it: in systems of `systemRace` (-1 = any), from story step `minCampaign` (in free play from rank `minRank`), `chance` % each time a bar fills with new visitors. |
| `dealer` | Optional. The ordinary ship dealers may stock it: `{ "chance": 2, "systemRace": -1, "minTechLevel": 0 }`, `chance` % (decimals allowed) each time a station's dealer list is made, at stations of `systemRace` systems (-1 = any) with at least that tech level. Not the special yards (Kothar, Quineros, Thynome...). |
| `available` | Optional. A [condition](#conditions) that must hold before it is sold anywhere (dealers and lounges). |

The model's own materials (base colour, metallic-roughness with its metallic / roughness factors, normal map, emission,
transparency) are used as they are. glTF
from Blender works: File > Export > glTF 2.0, format glTF Binary (.glb), +Y up. Projects made in Unity can use the
Project window's right-click **GoF2 > Export Model As GLB (Mods)**.

### materials

To set materials up in the mod instead (paths inside the mod), each entry applies to the renderers whose name contains
`mesh` (empty = all) and, when `submesh` is 0 or more, only that submesh:

| Field | |
|---|---|
| `diffuse`, `color` | Base colour map (PNG) and / or an RGB tint `[r, g, b]` (0 to 1). |
| `normal`, `normalScale` | Normal map. |
| `metallicSmoothness`, `metallic`, `smoothness` | Metallic (red) / smoothness (alpha) map; without a map `metallic` sets the metalness. |
| `emission`, `emissionColor`, `emissionIntensity` | Emission map and / or colour; above 1 it blooms. |
| `alphaClip` | Cut the diffuse's alpha below this (decals). |
| `opacity` | Below 1: glass (see-through, reflections stay bright). |
| `detailAlbedo`, `detailNormal`, `detailTiling`, `detailNormalScale` | A tiled detail pair (brushed metal ...). |
| `doubleSided` | Draw both faces (models with one-sided panels). |

### Glows

`throttleGlow` (and each entry of `extraGlows`) lights the hull where a mask is bright, following the throttle:

| Field | |
|---|---|
| `mask` | A PNG over the model's UVs; its bright parts glow (a plain white mask = the whole renderer). |
| `mesh`, `submesh` | Which renderers / submesh. |
| `color` | RGB tint. |
| `idle`, `full`, `boost` | Brightness at throttle 0, full throttle and while boosting (above 1 blooms). |
| `offset` | How far the glow sits out from the hull (game units, default 0.5). |
| `trailWidth`, `trailTime`, `trailBrightness`, `trailCount` | A trail in the glow's colour while boosting or jumping: width (game units), length (s), brightness, and `trailCount` above 0 for that many trails along the glow's rear edge. |

### Changing ships

`override`: an original ship's number or another mod's `"mod_id:ship_id"`, with any of `armor`, `cargo`, `price`,
`priceDefault`, `slots`, `handling`, `name`, `description`, `mounts` (and for mod ships `race`, `hangarHeight`).

**More (or fewer) weapons:** `slots` sets how many primaries, secondaries, turrets and equipment the ship takes (give only the
ones you change), and `mounts`
where its guns sit (the same list as a new ship's; it replaces the ship's whole list, exhausts included, so copy the ones you
keep). A ship with more weapon slots than gun mounts still fires every weapon: the extra ones share the mounts in turn. With
fewer slots, what no longer fits moves to the hold the next time the player docks.

```json
[ { "override": 10, "slots": { "primary": 3, "secondary": 2 } } ]
```

An override of an original ship can also give it a new **model**: `model` with `modelLength`, `modelYaw`, `materials`,
`engineGlowRadius` / `engineGlowColor` and the glows, as for a new ship. Every ship of that number then uses it (yours, the
NPCs', the hangar's). Its guns and exhausts stay at the original's mounts unless the entry has its own `mounts`.

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
        "planetTexture": "textures/kepler_prime_planet.png"
    },
    { "id": "rho_freeport", "system": "frontier_systems:kepler", "name": "Rho Freeport", "techLevel": 4, "planet": 6, "looksLike": 41 }
]
```

### Systems

| Field | |
|---|---|
| `id`, `name` | As for items. |
| `race` | Who owns it: 0 Terran, 1 Vossk, 2 Nivelian, 3 Midorian. It sets the hangars, bars, music, traffic, shops and the jumpgate. |
| `security` | 0 (lawless) to 3 (secure): how many raiders show up. |
| `visible` | On the star map from the start (default true). A hidden system can't be reached yet. |
| `position` | `[x, y, z]` on the star map. The original systems lie between 15 and 94 (x), 2 and 96 (y) and 10 and 90 (z). |
| `sky` | Which of the original skies (nebula and sun) it uses, 0 to 18. |
| `gates` | The systems its jumpgate reaches (numbers, or `"mod_id:system_id"`). Routes work both ways: the other system gets the route back. |
| `gateStation` | The station orbit where the jumpgate is. Default: the system's first station. |
| `sunTexture` | Its own sun: a PNG like the game's suns (512 x 512, a small bright core and a dim glow fading to black well inside the edge, on black; it is drawn additively). Default: the sun of its `sky`. |
| `sunColor` | `[r, g, b]` from 0 to 1: the colour and strength of the sunlight on ships and stations. Default: the light of its `sky`. |
| `skybox` | Its own sky, see [Skies](#skies). Default: the nebula of its `sky` and the game's stars. |
| `spaceMusic`, `stationMusic` | A mod's track (see [Music](#music)). |

Changing a system (`"override"`): `name`, `security`, `visible`, `sky`, `sunTexture`, `sunColor`, `skybox`, the music, and `gates` (added to its routes; links original systems too).

### Skies

The game's sky is two layers added together: the stars and a nebula. A system's `skybox` replaces either with your own image:

| Field | |
|---|---|
| `nebula` | The nebula image (PNG / JPG). |
| `stars` | A star image of your own, one of the game's three star layers (`0`, `1`, `2`), or `"none"` (when your nebula image has its stars painted in). Default: the system's usual stars. |
| `nebulaBrightness`, `starsBrightness` | Multipliers, default 1. |
| `rotation` | `[x, y, z]` degrees: a fixed turn of the sky. Without it the sky is turned per station like the game's own (each orbit sees it from another angle). |

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
| `system` | **Required.** Its system (a number or `"mod_id:system_id"`). A system holds at most 7 stations. |
| `techLevel` | 0 to 10: which items its shop sells. |
| `planet` | The planet it orbits: one of the game's planet textures, 0 to 22 or 24 to 26. |
| `looksLike` | An original station whose model and docking shape it uses. Default: the first original station of its system's race. With a `model` it stands in while the model loads, or when it can't be read. |
| `model` | Its own 3D model: a glTF / GLB file in the mod (made like a ship's, see [Ship models](#new-ships)). |
| `modelSize` | The model's largest extent in game units (20 units = 1 m). Default 40000 (2 km); the original stations are 20000-100000. |
| `modelYaw` | Degrees to turn the model about its up axis. A launch leaves along the station's front (the model's +Z after the turn, toward the orbit's planet); arrivals come from there too. |
| `modelCentre` | `true` (default): the middle of the model is the station's centre; `false`: the model's own origin is. |
| `materials` | Replace the model's materials on parts it names, like [ships.json's materials](#materials). |
| `collision` | What ships bump into: `"box"` (default, the model's bounds), `"sphere"`, or `"none"`. The player's ship collides with your model's real shape (its meshes, except additive / glow materials); the box or sphere is what NPC fighters steer around. `"none"` turns both off. |
| `volumes` | Instead of `collision`, your own shapes in game units around the station's centre: `[{ "box": [x, y, z, half x, half y, half z] }, { "sphere": [x, y, z, radius] }]`. Boxes stay axis-aligned, like the originals'. |
| `interior` | The hangar and bar you see when docked: `"terran"`, `"vossk"`, `"nivelian"`, `"midorian"` (or 0-3). Default: those of its system's race. |
| `hangar`, `bar` | Your own hangar / bar room from `interiors.json` (see [Hangars and bars](#hangars-and-bars)): its id, or `"mod_id:interior_id"` for another mod's. `interior` still picks the race whose ships park there. |
| `planetTexture` | Its own planet: a PNG with transparency (the round planet on a transparent background, its lit side as you like it; 512 x 512 is plenty). Seen big from its own orbit and small from the system's other orbits. Default: the game's texture `planet`. |
| `music` | A mod's track for its docked music (see [Music](#music)). |

Changing a station (`"override"`): `name`, `techLevel`, and the looks: `model` and its fields, `interior`, `planetTexture`,
`music` (an original station can get a new model too).

The docking range and the launch point grow with a big model: the "Dock" prompt shows within its radius plus 300 m, and a
launch starts 150 m clear of it. Station models load in the background with the ships while the game starts (the loading
screen waits for them).

Prices follow the distance on the star map, like the originals. The story stays in the original galaxy: the Void's invasions
never pick a mod's system.

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
| `quest` | An event graph (a quest, see [Quests and bar missions](#quests-and-bar-missions)) that starts with the game: the campaign's story. Its other quests start by their own conditions as usual. |
| `galaxy` | `"mod"`: only this mod's star systems and those of the mods it depends on exist in this game (the map, missions, the news); `"all"` (default): the whole galaxy. |
| `items` | `"mod"`: shops and bar sellers offer only mods' items (sell the player anything they find; mining still brings the game's ores). Default `"all"`. Give the campaign its own goods and energy cells if it needs them. |
| `ships` | `"mod"`: dealers sell only mods' ships. Default `"all"`. |
| `trafficShips` | The ships NPC fighters fly, per race (`terran`, `vossk`, `nivelian`, `midorian`, `pirate`). Default: the game's. |

A campaign's mod usually depends on the mods with its galaxy, ships and items, so turning it on turns them on too. Saves
remember the campaign; loading one asks for its mods like any modded save.

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

Two cards show at a time; more scroll sideways (drag, wheel, or the arrow keys / D-pad). A game started before your mod was
installed has every option off.

## Conditions

Several fields take a condition: `available` on items, ships and blueprints, and the quests' "Starts when". It is written in
the event graphs' expression language and is checked when it matters (a shop's stock, a lounge's visitors, a kill...):

| Word | |
|---|---|
| `option(mod_id:option_id)` | 1 when that new-game option is on in this game. |
| `won(main)`, `won(valkyrie)`, `won(supernova)` | 1 when that campaign's story is finished in this game. |
| `systemsvisited` | How many star systems the player has docked in (2 or more: they have left Mido). |
| `campaign` | The story step (-1 in free play and mod campaigns). |
| `rank`, `credits`, `kills`, `station`, `system`, `ship` | The player's rank, credits, kills, where they are docked / flying, the ship they fly. |
| `visited(station)`, `has(item)`, `cargo(item)`, `quest(name)` | A station docked at; how many of an item the player has (mounted too) / has in the hold; a quest under way (1) or done (2). |

Combine them with `and`, `or`, `not`, comparisons and arithmetic:

```json
"available": "option(canon_ships:canon_ships) and systemsvisited >= 2"
"available": "option(canon_ships:canon_ships) and won(supernova)"
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
| `available` | A [condition](#conditions) before any source offers it. |
| `unlocked` | `true`: the player knows it as soon as it is available (the next docking), without finding it. |
| `lounge` | A Space Lounge visitor sells it: `chance` % each time a bar fills with new visitors, for `price` (default half the product's price), in `systemRace` systems (-1 = any). |
| `derelict` | `chance` % of the orbits visited hold a derelict freighter (`race`: terran, vossk, nivelian, midorian or any) to dock at and hack; it leaves the blueprint in a data container. |
| `drops` | A ship the player destroys may leave it in a data container: `race` (terran, vossk, nivelian, midorian, pirate, void, specter or any), `chance` % per kill (decimals allowed). |

A blueprint found in a data container is learnt when the tractor beam pulls the container in ("Blueprint found: ..."). A
ship's blueprint shows the ship in the Blueprints tab, and the finished ship waits at the station where it was built: there
it replaces the player's ship like a purchase (the old one is traded in at its price, or parked in the Kaamo Club when the
player owns it and has no ship of that type stored), the equipment moving over as far as it fits.

Every source stops offering a blueprint once the player knows it.

## Hangars and bars

A station can have its own hangar (where your ship stands when docked) and its own Space Lounge bar. Each room is a glTF /
GLB model listed in `interiors.json`:

```json
[
    { "id": "kepler_hangar", "type": "hangar", "model": "models/kepler_hangar.glb", "fov": 46, "parkedMax": 3 },
    { "id": "kepler_bar", "type": "bar", "model": "models/kepler_bar.glb", "fov": 60 }
]
```

and picked by the station: `"hangar": "kepler_hangar", "bar": "kepler_bar"` in stations.json.

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

## Quests and bar missions

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

- **Quest** works like the game's own story chain. It starts by itself once **Starts when** holds, checked every second in a
  game (an expression: `campaign >= 20`, `visited(Kernstal)`, `quest(first_signal) == 2`, `credits > 50000`; empty = at once,
  `never` = only when another graph's **Start Quest** node starts it). Its **Mission title** and current objective show in the
  Missions window's Story panel. **Mission Complete** pays the reward and finishes it for good. **Mission Failed** works like
  failing a story mission: "Mission failed!", then the last save is loaded.
- **Bar Mission** is offered by a visitor in the Space Lounges (**Offered at**: station names or numbers, empty = every
  station; **Client**: a story character like `Keith`, one of your [characters](#characters) or a race and a name like `vossk K'ekki`, empty = someone of the
  station's race). In single player it is the player's one mission, like the game's freelance missions: it shows in the
  Missions window's Freelance panel with **Discard**, and a success counts like a freelance mission. In multiplayer the squad
  takes it together.
- **Event** is a multiplayer game mode started by an admin's `/event`.

Nodes made for quests:

| Node | |
|---|---|
| **Checkpoint** | What a save keeps: the quest's variables, objective, target and quest orbits as they are here. Loading a save goes on from the last checkpoint. Put one after each step the player shouldn't have to repeat. Only on the main flow from Start, not inside an If / While / Repeat / parallel block. |
| **Set Objective** | The objective in the Missions window (`\n` = a new line) and, with a **Get Orbit** wired in, the target station: the gold story icon on the star map and the HUD, and the window's **Show on map**. |
| **Quest Orbit** | That orbit becomes the quest's: no normal traffic is built there (like the story's own mission orbits), so the graph can spawn exactly the ships it wants. |
| **Start Quest** | Starts another quest beside this one (a chain of quests, or a bar mission leading into one). |

Everything else is the event graphs' toolbox: **Dialog** (conversations with portraits, see [Voice-over](#voice-over)), **Radio**,
**Spawn** (ships, friendly or hostile, named; objects as scenery), **Wait Until**, **On** triggers (docked, entered an orbit,
destroyed, a spawned ship destroyed, all enemies cleared, arriving near a point), **Set Waypoint**, **Restrict Travel**,
**Give Item**, **Reward**, **Title**, **Timer**, **Play Music** and more. Game values for conditions: `campaign` (the story
step, -1 without one), `credits`, `rank`, `station`, `system`, `ship`, `kills`, and the functions `cargo(item)`, `has(item)`
(in the hold or mounted), `visited(station)` and `quest(name)` (0 not started, 1 under way, 2 done); items and stations by
name or number. In single player `%player%` is Keith T. Maxwell.

Mod quests run beside the game's own story. A quest whose mod is turned off waits in the save until the mod is back.

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
them still fly) and `distance(Blue Wing, 0, 0, 30000)` (game units from the nearest of them to that point, -1 when none).
A ship that docked, jumped out, vanished or fled is gone but nobody's kill, and an event's ships never come back by
themselves (the orbit's own fighters relaunch from the station, a graph's don't).

In typed commands (multiplayer admins): `/npc Blue Wing goto 0 0 30000 then hold`, `/npc Blue Wing route 0 0 20000 / 9000 0
30000 loop`, `/npc Blue Wing escort player offset 0 300 -2500`, `/npc Blue Wing attack ship "Pirate Boss"`, `/npc Pirate Boss
flee seconds 5`, `/npc Blue Wing dock`, `/npc Blue Wing hold | resume | jump | speed 3`.

### Cutscenes

The **Cutscene** nodes stage a scene in flight. They work in quests, bar missions and multiplayer events alike.

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

The cutscene ends with the graph too (a quest finished or failed, an event stopped), so the player is never left stuck in it.
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
| `background`, `frame` | `false`: without the game's portrait background / frame. |
| `replaces` | A story character (`"Gunant Breh"`, or a speaker number) whose portrait this one takes everywhere, the game's own story included. Their name and voice stay. |

Name the character in a speaker slot by `vega`, by `frontier_systems:vega` (always unique, also across mods) or by its name
(`Captain Vega`). `vega as Harbour Master` shows another name over the same face, like the story characters. In typed
commands: `/dialog frontier_systems:vega : Welcome to Kepler.`. The dialogue tints character names like the story's people.

## Voice-over

Any **Dialog** page or **Radio** line can have voice-over: add `[voice <clip>]` to its text, for example
`Keith : I picked up a strange signal. [voice keith_signal_01]`. The tag doesn't show.

- `<clip>` can be one of the game's own voice lines by its name (`MSG_MISSION_24_NO_EQUIPMENT_INSTALLED`, the names in
  `Assets/Audio/*_VOICE_*`).
- Or it is a file in the mod's `voices` folder: `voices/<clip>.ogg` (`.wav` and `.mp3` work too). For German voices add
  `voices/de/<clip>.ogg`; the game picks it when the player's voice language is German, else `voices/en/<clip>.ogg`, else
  `voices/<clip>.ogg`.

The window waits a moment for the clip to load, types the text along with it, and turns the page when the line ends (with
the player's **auto-advance** option on), like the story's own voices.

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

## Sound effects

Put sounds in the mod's `sounds` folder (`.ogg`, `.wav` or `.mp3`), each named like one of the game's sounds: it plays
instead, everywhere the game plays that sound. The names are the file names in the game's `Assets/Audio` folders (one folder
per sound bank), for example `Target_Lock_v08` (a ship lock), `Blaster_Nsaan_01` (the N'saan's shot), `Jumpgate_3b`,
`Engine_09` (an NPC engine). `Reference/research/fmod_event_ids.txt` in the repository lists every sound event with its
files. Voices are sounds too: a voice line's file name replaces that line (the German voices are the same names with `de_`
in front, so each language is replaced on its own). Music has its own `music` folder (above).

A sound the game picks at random from several files (most shots and explosions) needs each file replaced to always sound
yours. Only a weapon's own shot can be set on the item instead (`fx` → `shot`, above).

## Textures (skins)

Put images in the mod's `textures` folder (`.png` or `.jpg`), each named like one of the game's textures: it is used instead
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
otherwise they are told which ones they lack. The server browser tags such games **Modded** and lists their mods. A dedicated
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
