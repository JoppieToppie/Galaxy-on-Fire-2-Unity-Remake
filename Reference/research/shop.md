# Station hangar: shop, ship dealer, equipment, blueprints (HangarWindow)

Scope: everything needed to rebuild the docked-station **Hangar** screen (button 0 of the station menu, see
`station_interior.md` §3.2) in Unity: icons, list layout, buying/selling, mounting, stock generation, prices,
ship trading, new-game state. Machine-readable icon table: **`research/item_icons.json`**.
Helper scripts (all read-only on the binary / data): `tools/shop/`

| script | what |
|---|---|
| `rb.py <addr> <n> [b\|h\|i\|f]` | dump a table from libgof2hdaa.so at a Ghidra address |
| `thumb.py <start> <end>` | capstone Thumb disassembly with literal-pool values |
| `tx.py 173 265-270` | English text ids |
| `img_records.py [id...]` | Image2D records (id → texture, region) parsed from `BuildResourceList_DISASSEMBLY.txt`, **including the iPad/large branches the emulation never ran** |
| `atlas.py` | `rect(imageId, large=True)` → (png, [x,y,w,h]) for the Android-HD variant |
| `build_item_icons.py` | writes `research/item_icons.json` |
| `prices.py <station> [items]` | reference implementation of the price rule (for unit tests) |

Conventions: addresses are Ghidra (image base 0x10000). Text ids → `Assets/Localization/text_en.json`.
FMOD ids → `research/fmod_event_ids.txt`. Item "type" = attr 1 (0 primary, 1 secondary, 2 turret, 3 equipment,
4 commodity); item "sort" = category = attr 2 (text 221 + sort).

---

## 0. TL;DR for Unity

- **Icons**: item `i` → image id `(i < 176 ? 2200 : 3824) + i`; ship `s` → `2417 + s`. Android HD on a ≥1700×1080
  screen loads `gof2_items_ipad_large.png` (ids 2200–2471 = regions 0–271) and `gof2_items_ipad_2_large.png`
  (ids 4000–4056 = regions 0–56, 2472–2480 = regions 57–65). All icons are **178×86 px**. Each is drawn on top of a
  **striped frame of its type** (red primary, orange secondary, yellow turret, green equipment, grey commodity, blue ship),
  also 178×86, from `gof2_interface2_ipad_large.png`. Rects for everything: `item_icons.json` (verified visually).
- **Prices are per station and deterministic**: `java.util.Random(stationIndex)`, price = min + clamp01(distance factor)·(max−min)
  ± 2 % jitter (§4.1). **Sell price = buy price** at that station. No supply/demand.
- **Stock is random** (time-seeded) and generated once per station while the station stays in the 3-station "recent" stack
  (§4.2); re-entering >30 s later nibbles 0–2 units off each stock item.
- **Buying never checks cargo space**; the footer shows `load / max t` (red when over), and launch/map are blocked while
  overloaded (text 204). Every unit in cargo = 1 t (weapons, equipment, ammo and goods alike); mounted items weigh nothing.
- Bought items go to **cargo**; the Ship tab mounts them (first free slot of their type). One-of-a-kind categories swap instead.
  Buying more of a mounted secondary merges the ammo into the mounted stack automatically.
- Ships: price = `ships.json price` −1 % in systems of the ship's race; trading in credits your current ship's full price
  (same formula) and **moves all equipment and cargo** to the new ship (overflow → cargo).
- New game (`Status::resetGame`): **0 credits**, Phantom with lasers/missiles/shield/armor/scanner/booster, at Var Hastra;
  on the first docking (campaign mission 1) the ship is replaced by a Betty with two unsaleable items (§8).
  The Hangar button says 528 "Not available." before campaign mission 5.

---

## 1. Item and ship icons

### 1.1 Image id rule
| function | address | rule |
|---|---|---|
| `ImageFactory::getItemImageId(idx)` | 0x141ab0 | `idx < 0xb0 ? 0x898 + idx : 0xef0 + idx` → 2200+idx (items 0–175), 3824+idx (items 176–232 → 4000–4056) |
| `ImageFactory::drawItem(idx, frame, x, y)` | 0x141a10 | `Sprite::setFrame(frame)` (frame = item type), draw frame at (x,y), then the item image at the same (x,y) |
| `ImageFactory::drawShip(ship, x, y)` | 0x141b38 | frame 5, then image `0x971 + ship` = 2417+ship (ships 0–54 → 2417–2471, 55–63 → 2472–2480) |
| `ImageFactory::reload` | 0x1415fc | Sprite frames = images `[0x4fa, 0x4fb, 0x4f7, 0x4f8, 0x4f9, 0x4fc]` → frame 0 red, 1 orange, 2 yellow, 3 green, 4 grey, 5 blue |

Regions 176–216 of `gof2_items*` (ids 2376–2416) are medal/achievement symbols (176–211) and medal plates (212–216),
not shop icons. Ships 13, 14, 15 (battleship/freighter slots), 50 and 53 have a "?" icon (not sold normally).

### 1.2 Image id → atlas rect, and which variant Android HD loads
- The resource table (`BuildResourceList` 0xfd934) registers every Image2D as a 16-byte resource whose data is
  **`u16 textureId, u16 regionIndex`** (the emulated output keeps these bytes in `raw`). The region list itself is in each
  `.aei` (`Textures/_texture_manifest.json` → `regions`, `[x, y, w, h]`, top-left origin). **All resolution variants of an
  atlas share the region order**; only coordinates differ. (This supersedes the "rank" rule in `mainmenu_notes.md`: it gives
  the same answer for gof2_interface/gof2_items, but the stored region index is the ground truth.)
- The texture file for a texture id is chosen by display flags set in `GameData::GameData` (0xe1f00 area, from
  `PaintCanvas::GetWidth/GetHeight`): **w ≥ 1700 and h ≥ 1080 (or ≥2048×1440) → `retinaDisplay = iPad = iPadLarge = 1`,
  `iPadHD = 0`** (iPadHD is always 0 in this binary). 1440–1699 × ≥1080 → `iPad` only (`_ipad` files); 1280×≥768 → retina
  phone (`_iphone4`); smaller → base files.
- BuildResourceList for iPadLarge (disassembly 0x12d81c → 0x12dada → 0x12c4f2 → 0x12df84): fonts/`gof2_interface_iphone4`/
  `gof2_logos_iphone4`/`gof2_credits_iphone4`, then `gof2_interface3_ipad_large`, **`gof2_interface2_ipad_large`**,
  **`gof2_items_ipad_large`**, **`gof2_items_ipad_2_large`**, `gof2_dlc_interface_ipad_large`, `gof2_challenge_interface_ipad_large`,
  `gof2_campaign_select_ipad_large`, `gof2_shop_button_ipad_large`.
- **iPad re-binding**: the iPad branch (0x12e304–0x1314e2) re-registers ~111 interface image ids to texture 0x274f
  (`gof2_interface2_ipad_large`) with `AddResource` calls that run **before** the common bulk `SetResourceList` (0x12c0f8), and
  `PaintCanvas::Image2DCreate` (0x83bc0) takes the **first** match. So on iPadLarge the item frames 0x4f7–0x4fc, the
  mounted/new markers, mount/demount icons, the style 7/8/9 button pieces (0x6ad–0x6bd) and most HUD images (0x4a7–0x4dc,
  e.g. lock brackets; relevant for `weapons.md` §9) come from `gof2_interface2_ipad_large.png`, everything else from
  `gof2_interface_iphone4.png`. `atlas.IPAD_OVERRIDE` has the table.
- Scale: base 58×28, iphone4 116×56 (2×), ipad 89×43, ipad_1440 125×60, **ipad_large 178×86 (≈3.07×)** item cells.
  The engine draws images 1:1 in screen pixels (no scaling), so on 1920×1080 an icon is 178×86 px.
- `gof2_items_ipad_1440` has 278 regions (6 extra duplicates of ships at the end); unused here.

### 1.3 `item_icons.json`
`entries[]`: `{"kind":"item"|"ship"|"ui", "index"/"name", "type", "imageId", "atlas", "rect":[x,y,w,h], "frameImageId",
"base":{atlas,rect}, "iphone4":{atlas,rect}}`. 233 items + 64 ships + 36 UI images (frames, markers, info/mount/demount,
blueprint progress bar, tab/arrow/credits button pieces). `atlas`/`rect` = the ipad_large variant (what HD uses); `base`
and `iphone4` are alternatives with matching tables (the iphone4 set is self-consistent: 116×56 icons + 116×56 frames in
`gof2_interface_iphone4`). Verified by cropping 51 items/ships composited on their frames plus all 36 UI rects.

---

## 2. Hangar window structure (`HangarWindow`)

`ModStation` button 0 (text 167 "Hangar") → `HangarWindow::HangarWindow` 0x171630 + `initialize` 0x171740.
Locked (ChoiceWindow 528 "Not available.") when campaign mission < 5 or = 48, 49, 56 (`ModStation::OnKeyPress` 0xec6f8).
Station 4 with an active pirate base shows dialogue 432 ("shop remains closed") instead.

### 2.1 initialize
1. Storage mode `+0x11d` = station 108 (Kaamo Club) **and** `Status+0x114 == 3` (club owned). In storage mode nothing costs money.
2. `Status::calcCargoPrices` (§4.1) for cargo, mounted equipment and station stock.
3. `+0x10` = `Item::mixItems(ship cargo, station items)` = merged shop list (§3.2).
4. `HangarList::init(ship, mixed, station ships, blueprints)` → 5 lists: 0 Ship tab, 1 Shop tab, 2 Blueprints tab,
   3 "Selected/Actions" list (`fillBuyList`, no callers in HD: unused), 4 blueprint ingredients.
5. Top-right tab buttons (TouchButton style 3, right-aligned, left of the help button): **183 "Ship"**, **185 "Shop"** (186 "Store"
   in storage mode), **272 "Blueprints"**. Static `lastTab` starts at 1 (Shop) and is remembered.
6. Row buttons (`+0x24`, 24 TouchButtons, positioned per selected row in `render`):

| idx | look | text | action (`OnTouchEnd` 0x176d94) |
|---|---|---|---|
| 0 | image 0x470 ("i") | – | details window `ListItemWindow::set(item, …, showPrice=true)`, sound 0x61 Button_Info |
| 1 | style 7 | 301 "Buy ship" (332 "Use" in storage / mission 77 at station 100) | `selectItem` → ship purchase flow §5 |
| 2 | style 7 | 302 "Sell ship" | storage mode only |
| 3 | image 0x533 | – | Ship tab, mounted item: demount |
| 4 | image 0x532 | – | Ship tab, cargo item: mount |
| 5, 6, 7 | style 7 | 279 "Sell this item", 282 "Demount", 283 "Edit" | 7 = open blueprint (tab 2) |
| 8 | style 8 (left arrow) | – | **sell one** (`transaction(false)`), sound 0x64 Button_to_station |
| 9 | style 9 (right arrow) | – | **buy one** (`transaction(true)`), sound 0x65 Button_to_ship |
| 10 | style 7 | 330 "Sell" | sell a stored ship (storage mode), confirm 334 |
| 11 | style 0xb | formatted credits | footer credits button → IAP credit shop (not needed) |
| 12–17, 18–22 | | | IAP / free-credit offers (skip) |
| 23 | style 7 | "Autocomplete" | blueprint autocomplete, confirm 195 (price §7) |

Holding 8/9 repeats (`update` 0x172626): first repeat after 200 ms, then every 30 ms once held > 1500 ms, and 5 units per
repeat once held > 4000 ms (no sound on repeats).

### 2.2 Tabs and list content (`HangarList` 0x142b88…)
**Shop tab** (`initShopTab` 0x142f9c): if the station has ships: header **173 "Ships"** + one row per station ship; then for each
type 0–4 that occurs in the merged list: header **265 "Primary weapons" / 266 "Secondary weapons" / 267 "Turrets" /
269 "Equipment" / 270 "Commodities"** followed by the items of that type (merged list order = item index ascending).
**Ship tab** (`initShipTab` 0x142c98): header 183 "Ship", a row for the ship itself, then per slot type with slots > 0: the same
header texts, one row per slot (mounted item, or an empty slot row, label text 174 = "-BLANK-" placeholder, i.e. empty), then
the cargo items of that type (candidates to mount). Commodities are not listed.
**Blueprints tab** (`initBlueprintTab` 0x143208): 273 "Available blueprints" + unlocked blueprints (progress bar 0x475/0x476/0x477
+ "N%  (station)"), then 274 "Products finished" + pending products ("#×name" + 275 "at" + station).

### 2.3 Row rendering (`HangarWindow::render` 0x172d00)
- Row box: `Layout::drawBox` style 3 (normal) / 4 (selected); ship-tab slot rows 9 / 10.
- Name: item text **1274+idx** (`0x4fa+idx`), ship **913+idx**; ship/cargo tab appends " (N)" when amount > 1.
- Icon: `drawItem(idx, type)` / `drawShip` at the row's left; marker next to the icon: **0x544 "+" = never inspected**
  (shop tab, `Status+0x54[idx] == 0`; set when the item is bought/sold/viewed) else **0x52e dot = mounted / ship has it**.
- Price: `Layout::formatCredits` = thousands separated ("," for languages 0, 10, 11, "." otherwise) + "$". Shop tab colours the
  price by `price ≤ credits` (colour constants not recovered). Ship rows show `newShip.price − currentShip.price` (trade-in delta);
  storage mode shows no prices.
- Selected shop item ("trade mode", `+0x88`): column labels **136 "Station"** (left) and **183 "Ship"** (right), stock
  "Nt" left and cargo "Nt" right, sell arrow (8) shown if cargo amount > 0, buy arrow (9) if station amount > 0.
- Header 167 "Hangar"; footer (`Layout::drawFooter` 0xe6410): centred **"load / max t"** (other colour when load > max), credits
  right-aligned.
- First-time hints: 587 (slot selected), 588 (first shop selection), 589 (after buying equipment), 109 credits hint (IAP).
  Help button texts per tab: 623 Ship, 622 Shop, 625 Blueprints, 624 (list 3), 626 (ingredients), 643 details window.

### 2.4 Details window (`ListItemWindow::set` 0x159468)
iPadLarge: 1300×800 px centred (`Layout::setWindowDimensions`); left half 3D ship (ships only), right half a scroll text.
- **Ship**: rows (label, value, current-ship value for comparison arrows 0x512/0x513/0x514):
  165 Armor (maxHP incl. mods, "(+)" if modded), 166 Cargo hold (modded load), 265/266/267/269 slot counts, 164 Handling
  (`handling·100`), 132 Price. Text = **977+ship**.
- **Item**: for attributes k = 0…61 present on the item, except {0, 1, 4, 5, 6, 7, 8, 60, 61} (and 13 for beams 9–11, 228):
  label text `DAT_0025875c[k]`, unit `LISTITEMWINDOW_UNITS[k]` (0x26ae0c):

| attr | label (text id) | value / unit |
|---|---|---|
| 2 | 320 Type | text 221+value (category) |
| 3 | 133 Tech level | |
| 9 / 10 | 146 Damage / 138 EMP damage | |
| 11 | 149 Loading speed | ms; for primaries, turrets and sort 39 an extra row **188 "Damage per second"** = `dmg (or EMP) / attr11 · 1000`, one decimal |
| 12 | 151 Range | m, = `(int)(attr12/3600 · attr13·250)` floored to 100 (quirk: +50 when the remainder is exactly 50); mines (sort 11): label 157 Effect, `attr12/1000` "s" |
| 13 | 156 Speed | `attr13 · 250` km/h |
| 14 | 139 Magnitude | float, m |
| 15, 16, 30, 31, 57, 58 | 158 Steerable, 153 Automatic, 142, 143, 162, 159 | 134 Yes / 135 No |
| 23 | 153 Automatic | 0 → 135 No, 1 → 154 Front, else 155 Omni |
| 17, 18, 20 | 164 Handling, 148 Capacity, 165 Armor | |
| 19, 26, 36, 37, 43 | 149 Loading speed | ms |
| 21, 22, 25, 28, 46, 54, 56 | 140 EMP defense / 157 Effect | % |
| 24, 29 | 141 Time to lock | ms |
| 27 | 152 Boost duration | ms |
| 32, 33 | 164 Handling / 144 Yield | % |
| 34, 48 | 145 Cabin size | –, t |
| 35, 41, 42 | 157 Effect | ms |
| 38 | 150 Energy consumption | blank for item 85 in hardcore |
| 39, 40 | 147 Fire rate / 146 Damage | "+N%" |
| 44, 47 | 151 Range | m |
| 51, 53 | 151 Range | m, `(int)(value * 0.1f)` (DAT_00159818 = 0.1 at 0x159fa0; the decompile drops the multiply) |
| 49 | 156 Speed | `v·50` km/h |
| 50 | 139 Magnitude | % float |
| 52 | 160 Gamma shielding | % |
| 55 | 161 Count | |
| 59 | 163 Plasma consumption | t |

  Then "\n132 Price\n<single price>" (when opened from the info button), and the text = **1041+idx** + "\n\n214 Known price range:\n"
  + lowest ("-> N$ (system or 215 This system)") + "\n" + highest. The known lowest/highest are recorded per item whenever a hangar
  opens (`Status+0x3c/+0x44` lowest price/system, `+0x40/+0x48` highest, 233 ints each), except in black-market systems/storage.

---

## 3. Buying and selling

### 3.1 Item transaction (`Item::transaction(buy, load, free)` 0xf4074, `HangarWindow::transaction` 0x172820)
- **Buy one**: needs `stationAmount > 0` and `credits ≥ price` (storage mode: free). `stationAmount−1, amount+1`, credits −price,
  load +1 (`Ship::changeLoad`). **No cargo-space check.** Not enough money → 203 "You need an additional #C." + 124.
- **Sell one**: needs `amount > 0`; `amount−1, stationAmount+1`, credits **+price (same price as buying)**, load −1.
- Unsaleable items (`Item+0x44`, e.g. the campaign Betty gear, quest goods 131/209) → 323 "This item cannot be sold or demounted at the moment."
- Buying a system specialty (items 132–153) sets `Status+0xac[idx−132]` (achievement tracking).
- Leaving trade mode commits: cargo = items with amount > 0, station stock = items with stationAmount > 0 (so **anything you sell
  becomes part of that station's stock**). Items can be sold at **every** station (the shop list always includes all cargo),
  also at stations with no stock.
- `Status::changeCredits` ignores changes with |Δ| > 1 000 000 000 and clamps credits at 0.

### 3.2 Merged list (`Item::mixItems` 0xf4560)
Cargo items are cloned (amount = cargo amount); each station item is merged into the same row if the index matches (price =
**station price**, amount = cargo amount, stationAmount = stock) or appended (amount 0). Sorted by item index, duplicates combined.

### 3.3 Cargo rules
- `Ship::getMaxLoad` = base cargo (`ships.json cargo`) + cargoPlus, cargoPlus = 30 per cargo mod (mod 1) + `(int)((base + mods)·Σcompression% / 100)`
  (compression = attr 22 of every mounted sort-12 item, stackable).
- `getCurrentLoad` = Σ amount of all cargo items (every unit 1 t). Mounted items and mounted ammo don't count.
- Overload is allowed in the hangar. `ModStation::leaveStation` 0xec1ec and the Map button refuse with 204 while load > max.

### 3.4 Sounds
0x7c Button_Push (row highlight), 0x61 Button_Info, 0x64 Button_to_station (sell), 0x65 Button_to_ship (buy),
0x62 Button_Mount, 0x60 Button_Dismount. 0x63 Button_Sell is not used by the hangar. Ambience 0x5f `Station_Atmo_Hangar`.

---

## 4. Station stock and prices

### 4.1 Price rule (`Status::calcCargoPrices` 0xb9d70) — reference code `tools/shop/prices.py`
For each of the three lists (ship cargo, mounted equipment, station stock), **reseed** `Globals::rnd` = java.util.Random with
**seed = station index** (setSeed 0x7ad04, verified in the disassembly: r2 = index, r3 = sign), then for every item in list order
whose current price > 0:
```
span = dist(minPriceSystem, maxPriceSystem); cur = dist(minPriceSystem, stationSystem)   // dist = (int)sqrt(dx²+dy²), map x/y
f    = min(1, (100/span)·cur/100)            // float32; span = 0 → inf/NaN → 1
base = minPrice + (int)(f·(maxPrice − minPrice))
d    = max(1, (int)(base·0.02))
price = base − d + rnd.nextInt(2d + 1)        // so prices can exceed maxPrice by up to 2 %
if Loma rule: price = maxPrice (no RNG)       // system 25 and current station != Status+0x78 (see Uncertainties)
if Globals::globalPriceRaise: price = (int)(price + price·raise·0.01)   // never written in native code → 0
```
`lowestPriceSystem`/`highestPriceSystem` in items.json are attr 4/5. Prices are recomputed on every hangar open, so a station
always shows the same prices for the same list order. `Item::adjustPrice` (tech-level based) and `Item::getPriceRate` are dead code.
Station stock items get their price from the stock list (index order), cargo-only items from the cargo order.

### 4.2 When stock is generated (`Status::departStation` 0xb63e0, `addStationToStack` 0xb6140)
`Status+0x19c` = stack of the last **3** visited stations (Station objects keep items/ships/agents). Arriving at a station that
is not on the stack → `Generator::getItemBuyList`, `getShipBuyList`, `createAgents`; the oldest stack entry is discarded.
`ModStation::OnInitialize` then runs `Generator::computerTradeGoods` (0xa0538) if > 30 000 ms of play time passed since the
last departure (`Status+0x70`): for each stock item `r = rnd.nextInt(3)`; if `r < amount` → amount −= r (not station 108).
Kaamo Club (108, owned) uses the storage station `Status+0x14c` instead.

### 4.3 `Generator::getItemBuyList(station)` 0xa05c4
Special stations first:
- Station 78 (Var Hastra) and campaign mission < 7 → exactly items **0, 22, 55, ×1 each, price 0** (free tutorial gear).
- Station 108 (Kaamo) or a Supernova system → no stock.
- Mission 139 in system 25 → item 190 prepended; station 126 → item 209 ×(1–10; 1 in mission 117) prepended.

Then `AERandom::reset` (time seed) and for every item (index order), with `techS` = station tech level
(`stations.json techLevel`), `techI` = item tech (attr 3), `occ` = occurrence (attr 6):
1. `exclusive` = attr 61 == station index (the item's "home station", e.g. Nirai lasers at 52, Var Destro/Quineros DLC gear),
   except item 196 and 198–200 before campaign mission 142.
2. Station 106 (Sao Perula) only considers items {101, 102, 103, 107, 108, 109, 114, 124} and specialties 132–153, and only commodities.
3. `occ` modifiers: item 122 × (1 + energyCellsProbChange%), secondaries × (1 + secondaryWeaponsProbChange%) (both globals 0).
   DLC fallback: `occ == 0` and the DLC is owned (`options[0x35]` Valkyrie for idx < 196, `options[0x37]` Supernova for ≥ 196) and
   not a commodity / item 85 / blueprint item / item 181 before mission 59 / sorts 33–35 or 43 before mission 142 / sort 36 /
   items 209, 210, 217, 218 / item 205 before mission 94 / Kaamo specials {200, 220, 208, 213, 216, 228, 229, 230, 231} /
   signatures outside black-market systems → `occ = rnd(30) + (int)((1 − techI/10)·30)`.
4. Not exclusive → skip if: has ingredients (blueprint product), items 217, 218, 164, 175, `techS < techI`, `occ == 0`,
   price 0, attr 60 == 1 (Vossk-only) outside Vossk systems, specialty 132–153 other than `132 + stationSystem` (except station 106).
5. Hardcore mode: no ore / ore cores (sorts 23, 24). Station 107 (Quineros): equipment only; 105 (Var Destro): weapons or sort 28;
   101: weapons only; 106: commodities only.
6. Chance: exclusive items always; otherwise `(techI ≤ techS or specialty) and rnd(100) < (int)(k·occ)` with
   **k = min(1.5, (campaignMission + 25)/45)**, and additionally, if `techI < lowTech` (lowTech = techS/2, 1 when techS < 4,
   0 at stations 105/107) and not item 122: `rnd(100) < 61`.
7. Amount: `r = rnd(15) + 5` (5–19). Secondaries: r. Item 109: max(1, r/2). Commodities: r, or when
   `inv = 100 − dist(stationSystem, minPriceSystem) > 50`: `r · max(1, (int)((inv−50)/50 · (hardcore ? 2 : 20)))`
   (up to ×20 next to the cheapest system); item 110 capped to rnd(10)+10. Weapons/turrets/equipment: r/5 (1–3).
   The stock row stores this in `amount` (becomes `stationAmount` in the merged list).
Extra fix-ups in `ModStation::OnInitialize`: stations 10, 100, 101 add 10 Energy Cells (122) if not stocked and the ship carries < 6;
station 10 after the game is won adds 50 × item 164 if the player has no jump drive (85) anywhere.

### 4.4 `Generator::getShipBuyList(station)` 0xa0eb8
- None: system 15 before mission 16, stations 101 and 108, Supernova systems.
- Station 100 after DLC1 won: all ships with an integrated jump drive (37, 38, 40).
- Station 107: all ships 0–49 of race 8 (pirate) + ships 45, 46, 47, 48 once their Supernova wanted targets are terminated (`options[0x37]`).
- Otherwise `n = rnd(6)` (+1 at station 41; 0 → no ships; station 10 with all gold medals → n = 1, ship 8). Per slot: ship =
  `Globals::getRandomEnemyFighter(systemRace)` (0xf9034: race 1 → ship 9 (after DLC1: 60 % 9, 25 % 41, 15 % 39); 9 → 8; 10 → 44;
  else random 0–36 excluding 0, 8, 9, 10, 13, 14, 15 with `DAT_00254990[ship] == race`); first slot at station 41 = Phantom (10),
  first slot at station 78 = Betty (0); when n > 1, 22 % chance to use a random other race (own race or 4 → pirate 8). No duplicates.
- Extras: Terran system 1/7 ship 62; Vossk 1/5 ship 63, Nivelian 1/8 ship 61; Vossk + Valkyrie + DLC1 won: 1/2 ship 39, 1/2 ship 41;
  Supernova owned: Vossk 1/4 ship 54, station 120 after mission 158: ship 49 (+ ship 44 in hardcore or with all Supernova medals),
  Terran 1/8 ship 51; system 17: 1/3 each ship 42, 43, 52.
- Race per ship (`DAT_00251d5c` = `DAT_00254990`, 64 ints): `3 0 8 3 2 0 3 0 9 1 0 8 2 0 0 0 2 0 2 3 3 2 0 8 8 8 0 0 0 8 3 2 8 0 0 2 0 0 0 1 0 1 1 2 1 3 3 3 3 1 1 0 8 1 1 0 3 2 0 0 8 1 3 1`
  (0 Terran, 1 Vossk, 2 Nivelian, 3 Midorian, 8 Pirate, 9 Void; text 406+race).
- `ModStation::OnInitialize` also adds ships 37, 38, 40 at station 100 (DLC1 won or missions 80–84) and re-rolls station 10's list to include ship 8 after all gold medals.

---

## 5. Ships (dealer)

- `Ship::adjustPrice` 0x1a433c: `price = basePrice(ships.json) · (1 − 0.01 if DAT_0025cee4[ship] == systemRace) (+ globalPriceRaise %)`,
  applied to every listed ship **and to the player's current ship** (`HangarWindow::initialize`), so trade-in value = full price.
  (`Ship::priceDecline` = base/1.25 is applied to the start ship in `resetGame` but overwritten by the first adjustPrice.)
- Select a ship row (`selectItem` 0x178e20): if `current.price + credits < new.price` → 203 (+124). Passengers on board → 336.
  Mission 77 while flying ship 37 → 325. Same ship type → 329. Else confirm **304 "Do you really want to buy this ship?"**
  (333 "Do you want to switch ships?" in storage).
- Confirm (`OnTouchEnd`): if the Kaamo Club is owned: 327 with 330 "Sell" / 331 "Keep"; Keep = pay the full price and store the old
  ship at the club (328 if one of that type is already stored). Not owned: an advert (189) for the club, then trade-in:
  **credits += current.price − new.price**.
- New ship = `makeShip(index)`, race of the dealer copy, adjustPrice; **cargo cloned; each mounted item re-added with
  `addEquipment` (first free slot of its type, else into cargo)**; ship mods copied. The dealer's row is replaced by your old
  ship (so it can be bought back). Message **303 "Congratulations! You now own a brand new #N…"** (#N = 913+ship).
- Ship stats come from `ships.json` (Ship ctor 0x1a335c: index, armor, cargo, price, slots primary/secondary/turret/equipment,
  handling/100). Mods (`Ship::addMod`): 0 = +40 HP, 1 = +30 t cargo, 2 = +1 equipment slot, 3 = +0.2 handling.

---

## 6. Equipment / mounting (Ship tab)

- Slots: `Ship+0x68 = [primary, secondary, turret, equipment]` from ships.json; the equipment array `+0x6c` is slot-ordered
  (all primaries, then secondaries, turrets, equipment). `Ship::addEquipment` 0x1a3fe4 puts an item into the first empty slot
  of **its type**; `setEquipment(item, n)` = n-th slot of the item's type.
- Tap a cargo item (`selectItem` tab 0): if an item of the same **sort** is mounted and the sort is not multi-installable →
  ChoiceWindow **287 "Do you want to demount the #ITEM1 and mount the #ITEM2?"** → demount old + mount new. Else `mountItem`
  (0x178824): secondaries move the **whole stack** (amount = ammo), everything else 1 unit; removed from cargo; sound 0x62.
  The mount button is only shown when a free slot of the type exists (or the swap case applies).
- Tap a mounted item → `demountItem` 0x178674: to cargo (secondaries with their ammo), sound 0x60. Cabins (sort 20) can't be
  demounted while `passengers > maxPassengers − cabin attr 34` (323). Unsaleable items → 323.
- Buying a secondary that is already mounted: when the selection moves on, `autoEquipSecondaryWeapons` 0x1761c4 merges the cargo
  stack into the first mounted slot with the same item (amount = mounted + bought) and shows 208 "#N mounted.".
- **Multi-installable by sort** (`Item::canBeInstalledMultipleTimes` = `DAT_00254930[sort]`): yes for 0–7 (lasers, blasters,
  auto-cannons, thermo, rockets, missiles, EMP bombs, nukes), 11 mines, 12 compression, 20 cabins, 22–25, 32, 34 ionizing missile,
  36, 39 sentry gun, 40 cluster missile, 42 shock blast; **one per ship** for 8 turret, 9 shield, 10 armor, 13 tractor beam,
  14 booster, 15 repair bot, 16 steering nozzle, 17 scanner, 18 jump drive, 19 mining laser, 21 cloak, 26–29, 33, 35, 37, 38, 41.
  (Sort 43 reads past the 43-entry table → non-zero → multi.) Ships 44 and 49 have an integrated cloak (item 95).
- Stats from mounted items (`Ship::refreshValue` 0x1a33f4): sort 9 shield attr 18/19 → shield/regen; 10 armor attr 20; 12 attr 22
  compression (summed); 14 booster attr 25/27/26 → boost speed/time/delay; 15 repair (item 75 = type 0, else 1); 16 attr 28 agility;
  17 attr 31 radar; 18 jump drive; 20 attr 34 passengers (summed); 21 cloak; 27 emergency system; 28 attr 39/40 fire-rate/damage
  factors; 29 signature race = idx − 189. Firepower = Σ (dmg/reload·1000) over sorts 0–3, 8, 25, 39 (with the factors).
  Max HP = armor + 40 per HP mod.

---

## 7. Blueprints (brief)

Blueprint items = items with ingredients (`Status+0x18`, one BluePrint per such item; base quantity 10 for secondaries else 1).
Edit (button 7) → ingredients list (tab 4, header = product name) where cargo goods are moved into the blueprint with the same
arrows. The station where the first ingredient was added is the production station; adding at another station costs
**200 $ per unit** (288 "Shipping these items to #S will cost you #C."; volatile goods 209/204 can't be shipped, 289).
Completion → 211 (same station, product to cargo) or 210 + pending product at the production station. Autocomplete price =
`maxPrice · baseQuantity · 1.25` (blueprint 210: ingredient value + 2 000 000). Start confirm 212; blueprints 210/223 need a
system with jump routes (else 528).

---

## 8. Status fields and new-game state

`Status` (offsets): `+0x1ac` credits, `+0x18c` Ship*, `+0x198` current Station*, `+0x1a0` SolarSystem*, `+0x1e8` campaign mission,
`+0x34` passengers, `+0x3c/+0x44` lowest known price/system [233], `+0x40/+0x48` highest [233], `+0x54` item "seen" flags (bytes),
`+0xac` specialty bought flags, `+0x114` Kaamo Club state (0 none, 1 offer dialogue pending, 2 purchasable for 30 000 000 $ + 50 Buskat,
3 owned; `options[0x36]` → 3 at reset), `+0x14c` club storage Station, `+0x19c` recent-station stack [3], `+0x70` playing time
at last departure, `+0x18` blueprints.
`Ship`: `+0x10` current load, `+0x28` cargoPlus, `+0x6c` equipment Array<Item*>, `+0x70` cargo Array<Item*> (acquisition order),
`+0x78` mods, `+0x7c` added device slots (rest as in CLAUDE.md).
`Item` (0x48 bytes, `Item::init` 0xf3f32 copies attrs 0–8): `+0` index, `+4` type, `+8` sort, `+0xc` tech, `+0x10` minPriceSystem,
`+0x14` maxPriceSystem, `+0x18` price (init = (min+max)/2), `+0x1c` occurrence, `+0x20` min, `+0x24` max, `+0x28/+0x2c`
ingredients/quantities, `+0x30` attribute pairs, `+0x34` amount, `+0x38` stationAmount, `+0x3c` blueprintAmount, `+0x44` unsaleable.
Missing attribute → sentinel 0xc5997825.

**`Status::resetGame` 0xba78c**: credits **0**; ship **10 Phantom** (`priceDecline`); cargo empty; equipment: primary slots 0/1 =
item **2 Nirai Charged Pulse** ×2, secondary 0 = item **36 Edo ×6**, equipment 0–3 = **54 Fluxed Matter Shield, 59 T'yol,
82 Telta Ecoscan, 73 Synchrotron Boost**; station **78 Var Hastra**; campaign mission 0 = `Mission(4, 0, 78)`; HP/shield/armor
full; all blueprints locked; price memory cleared. (That is the intro flight.)
**First docking** (`ModStation::OnInitialize` state 0x14, campaign mission 1): ship replaced by **0 Betty** (race 8) with
**item 90 Gunant's Drill** (equipment slot 0) and **item 81 Telta Quickscan** (equipment slot 1), both **unsaleable**; the hangar
then offers items 0, 22, 55 for free until mission 7. `nextCampaignMission` case 3 clears the cargo, case 5 removes all cargo;
case 7 makes all equipment/cargo saleable again (and resets their prices, see Uncertainties). Mission 44 (case 0x2c) pays 40 000 $.
Missions 6/7 won't let you launch without a mounted weapon + armor (529/530).

---

## 9. Text ids used

| id | English |
|---|---|
| 132, 133, 136 | Price, Tech level, Station |
| 164, 165, 166 | Handling, Armor, Cargo hold |
| 167, 173, 183, 184, 185, 186 | Hangar, Ships, Ship, Cargo, Shop, Store |
| 188 | Damage per second |
| 203, 204 | You need an additional #C. / Your cargo hold is full. Sell items or mount them … before leaving the station. |
| 207–213 | #N sold. / #N mounted. / #N demounted and moved to cargo hold. / blueprint messages |
| 214, 215 | Known price range: / This system |
| 221+sort | category names (Laser … Shield Injector) |
| 265, 266, 267, 269, 270 | Primary weapons, Secondary weapons, Turrets, Equipment, Commodities |
| 272, 273, 274, 275 | Blueprints, Available blueprints, Products finished, at |
| 277–285 | Selected, Actions, Sell this item, Description, Mount, Demount, Edit, Available in cargo, Available in hangar |
| 287 | Do you want to demount the #ITEM1 and mount the #ITEM2? |
| 288, 289 | shipping cost / volatile goods |
| 301–304 | Buy ship, Sell ship, Congratulations! You now own a brand new #N. …, Do you really want to buy this ship? |
| 320 | Type |
| 323 | This item cannot be sold or demounted at the moment. |
| 325, 327–329 | cannot sell ship now / sell or keep old ship / already a ship of this type / already flying this ship |
| 330–334 | Sell, Keep, Use, Do you want to switch ships?, Do you want to sell this ship? |
| 336 | You cannot sell your ship while you have passengers on-board. |
| 528 | Not available. |
| 587–589, 622–626, 643 | hints / help pages |
| 913+s, 977+s | ship name / description |
| 1274+i, 1041+i | item name / description |

---

## 10. Unity build recipe

1. **Icons**: load `item_icons.json`; build Sprites from `atlas`+`rect` (convert to bottom-left origin: `y' = texH − y − h`).
   Draw frame (`frameImageId` entry) then icon in the same 178×86 rect; ship icons use the blue frame. For a smaller UI, the
   `iphone4` rects (116×56) are a consistent alternative.
2. **Data**: `Item` runtime = items.json row + `price`, `amount`, `stationAmount`, `unsaleable`. `ShopStation` = stock list +
   ship list, cached for the last 3 stations.
3. **Prices**: port `tools/shop/prices.py` (JavaRandom already exists as `JavaRandom`); price the stock list, cargo list and
   equipment list each with a fresh `Random(stationIndex)` whenever the hangar opens.
4. **Stock**: port §4.3/§4.4 with `UnityEngine.Random` (the original is time-seeded); run on arrival at a station not in the stack;
   `computerTradeGoods` on re-entry after 30 s.
5. **UI** (UI Toolkit): tabs Ship / Shop / Blueprints; Shop list = merged list grouped by Ships + 5 type headers; selected row
   shows station stock (left, sell arrow) and cargo (right, buy arrow); info button → details panel (§2.4); footer `load / max t`
   + credits "12,345$".
6. **Rules**: buy/sell 1 unit per tap (hold to repeat 200 ms → 30 ms → 5 units), same price both ways, no cargo check, block launch
   when overloaded; mount/demount/swap per §6; ship trade per §5.
7. **New game**: §8 (credits 0; Betty + drill + scanner after the intro; free items 0/22/55 at Var Hastra until mission 7).

---

## 11. Functions

| Function | Address |
|---|---|
| ImageFactory::ImageFactory / reload / drawItem(4) / getItemImageId / drawItem(3) / drawShip | 0x141584 / 0x1415fc / 0x141a10 / 0x141ab0 / 0x141ac0 / 0x141b38 |
| HangarWindow::HangarWindow / initialize / update / transaction / render / OnTouchBegin / highlightItem / autoEquipSecondaryWeapons / setSellMode / OnTouchEnd / demountItem / mountItem / readyToClose / selectItem | 0x171630 / 0x171740 / 0x172626 / 0x172820 / 0x172d00 / 0x175c74 / 0x176164 / 0x1761c4 / 0x176594 / 0x176d94 / 0x178674 / 0x178824 / 0x178da0 / 0x178e20 |
| HangarList::initShipTab / initShopTab / initBlueprintTab / init / fillBuyList / fillIngredientsList | 0x142c98 / 0x142f9c / 0x143208 / 0x1433f4 / 0x14348c / 0x14378c |
| ListItemWindow::set / draw | 0x159468 / 0x15b620 |
| ListItem::checkCredits / checkSlot / checkSort / getPrice | 0xb3690 / 0xb36dc / 0xb3718 / 0xb3748 |
| Item::init / canBeInstalledMultipleTimes / transaction / mixItems / extractItems / combineItems / adjustPrice (dead) | 0xf3f32 / 0xf3f88 / 0xf4074 / 0xf4560 / 0xf43d8 / 0xf4246 / 0xf4858 |
| Generator::computerTradeGoods / getItemBuyList / getShipBuyList | 0xa0538 / 0xa05c4 / 0xa0eb8 |
| Globals::getRandomEnemyFighter | 0xf9034 |
| Status::addStationToStack / isOnStack / departStation / changeCredits / calcCargoPrices / resetGame / nextCampaignMission | 0xb6140 / 0xb61b8 / 0xb63e0 / 0xb8e84 / 0xb9d70 / 0xba78c / 0xb6c98 |
| Ship::Ship / refreshValue / getMaxLoad / addEquipment / setEquipment(item,n) / slotAvailable / priceDecline / adjustPrice / addMod | 0x1a335c / 0x1a33f4 / 0x1a3a3e / 0x1a3fe4 / 0x1a4032 / 0x1a4220 / 0x1a430c / 0x1a433c / 0x1a4404 |
| BluePrint::addItem / getCompletionRate / getAutoCompletionPrice | 0x1a6154 / 0x1a63dc / 0x1a64c8 |
| Layout::formatNumber / formatCredits / drawFooter | 0xe59a8 / 0xe5bd4 / 0xe6410 |
| PaintCanvas::AddResource / SetResourceList / Image2DCreate | 0x81cc8 / 0x81c88 / 0x83bc0 |
| TouchButton::init (style images) | 0x1961f8 |
| Galaxy::distancePercent / invDistancePercent | 0x1a4e40 / 0x1a4e00 |
| AERandom::setSeed / reset / nextInt(n) | 0x7ad04 / 0x7ad32 / 0x7add2 |

Tables: `DAT_00254930` multi-install per sort (43 bytes), `DAT_00258730` hidden attrs, `DAT_0025875c` attr label text ids (62),
`LISTITEMWINDOW_UNITS` 0x26ae0c (62 char*), `DAT_00251d10` Kaamo specials (9), `DAT_00251d34` station-106 goods (10),
`DAT_00251d5c`/`DAT_00254990`/`DAT_0025cee4` ship races (64), TouchButton style image tables `DAT_0025c890` (iPad) /
`DAT_0025cb84` (phone), 9 × u32 per style.

---

## Uncertainties

1. **Variant choice** assumes a ≥1700×1080 Android screen (→ iPadLarge). A 1440–1699 px wide screen would use the `_ipad`
   files (89×43), 1280×768 the `_iphone4` ones. The ids and region indices are the same in every variant.
2. **iPad re-binding table** comes from a static parse of the disassembly (`img_records.py`, 306 of the records; the parser
   misses a few odd code shapes, e.g. 0x6a8/0x6a9). All ids used here were found and visually verified; ids not in
   `IPAD_OVERRIDE` are assumed to stay on `gof2_interface_iphone4`.
3. Colours passed to `PaintCanvas::SetColor` (affordable vs unaffordable price, overloaded footer) were lost by the decompiler.
4. **Loma max-price rule**: `Status+0x78` is a Station object created in the Status ctor whose meaning (it is compared by index
   with the current station) was not traced; in practice every station in system 25 probably sells at maxPrice.
   Resolved: it is a bare `Station()` placeholder (the Status ctor's `new Station`), the current station only outside a real
   orbit, so every real Loma station sells at maxPrice. `HangarWindow::initialize` skips the lowest / highest seen-price
   records there, and `Generator::getItemBuyList` rolls the signatures (sort 0x1d) only in the black-market system.
5. `globalPriceRaise`, `energyCellsProbChange`, `secondaryWeaponsProbChange` live in .bss and are never written by native code
   (possibly meant for remote config) → treated as 0.
6. Trade-in at full price follows from `Ship::adjustPrice` overwriting the price of the player's ship each time the hangar opens
   (it recomputes from the template, undoing `priceDecline`); verify against the real game if possible.
7. `nextCampaignMission` case 7 resets mounted/cargo item prices with `Globals::items[loopIndex].price` (loop index, not item
   index) — an original bug; harmless since prices are recomputed on the next hangar open.
8. Empty-slot label is text 174, which reads "-BLANK-" in the English table (a translation placeholder); render it as empty.
9. The "Selected/Actions" list (tab 3, `fillBuyList`) and texts 279/282 are built but `fillBuyList` has no callers in this build.
10. Stock generation shares the time-seeded `Globals::rnd` between items, ships and agents, so it cannot (and need not) be
    reproduced exactly.
