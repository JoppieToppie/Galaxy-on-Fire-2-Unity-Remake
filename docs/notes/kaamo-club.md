# Kaamo Club

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Kaamo Club (station 108, Shima)

Research: `Reference/research/kaamo_club.md` (states, the siege, docking conversations, storage rules, save; corrects
`blueprints_mods.md` 3 on 457 / 3162 / 3163), `blueprints_mods.md` 2-3, `shop.md` 5. Rules in `KaamoClub` (plain C#:
`Session.KaamoState` 0-3, `KaamoItems`, `KaamoShips` = `StoredShip` index / race / mods; save v4), the orbit in
`KaamoSiege`, the storage in `Hangar` / `HangarWindow`.

- **Siege** (state 0, `Level::createMission`): 4 Pirate Outposts (`station_pirates`, name 441) at fixed points as `NpcShip`
  static objects (`SpawnSpec.fixedObject`: never move, no gun / engine / loot, a +-7500 hit cube, the `collision.json`
  1002 volumes with the static-object rule `CollisionVolume.ForStaticObject`; death: wreck animation 14246 then an x8
  explosion, the wreck stays; hull `5 (4 campaign + 15 rank + 20)`), 6 (8) pirates within +-20000, respawned every
  22.5 s while an outpost stands. At 5 s Mkkt Bkkt calls (457, voiced) and the state becomes 1; all dead = 458,
  missions completed +1. Docking, the gate and the Khador Drive give 525 while it runs. Remake: the siege keeps the
  player's freelance mission (the original overwrites it). Music 146 `HomeBase_NoCombat` in the orbit (all states).
- **Docking** (`StationMenu.CheckKaamo`): state 1 = the 18-page first visit (459-475 + 476, voiced) -> 2; state 2 = 476
  (not enough: >= 30 000 001 $ and 50 t Buskat in the hold) or 477 -> Yes: pay, (remake) the unused 6-page purchase
  talk 479-484, 485 -> owned (3), the storage cleared.
- **Storage** (owned, at 108): the Shop tab becomes 186 "Store": the storage is 108's stock (one list), free transfers,
  no prices, unsaleable goods refused (323), mounted items not listed; stored hulls with their sell value, row buttons
  332 "Use" (336 / 329 / 333: cargo and equipment move over, the old hull takes the row) and 330 "Sell" (334; X / pad X).
  One hull per type. The Midorian hangar parks the first 5 stored hulls (the original 3; its remodelled room has two more pads).
- **Stored hulls keep their equipment** (remake, players' suggestion; Options > Gameplay "Stored ships keep their equipment",
  `Settings.KaamoKeepsEquipment`, on by default; `StoredShip.equipment`, null / empty = a bare hull, older saves): a hull going
  into the club (327 Keep, a lounge seller's Keep, a mod blueprint's delivery) keeps what is mounted on it
  (`Hangar.EquipmentToStore`; the new hull starts bare) except the story's unsaleable items, which stay with the player; "Use"
  mounts the stored hull's own items (`SwitchTo`'s 'mount': the story items first, the rest by slot, overflow to the hold) and
  the old hull keeps its; off: the original (the items move over) and a stored hull's items go to the storage
  (`KaamoClub.AddToStorage`), as do a sold stored hull's. Trade-ins elsewhere hand the items over as before. The Store tab shows
  "N mounted" on the row and the items under the details (`ItemInfo.AddEquipmentLines`); checked by `SaveGame.TryParse`, fixed by
  `ModSaves`, counted in a profile's worth.
- **Buying elsewhere** while owning it: 304, then 327 with 330 Sell (trade-in) / 331 Keep (328 when the old type is
  stored, else the full price and the old hull goes to the club, the dealer row is gone). Remake (#83): with "Stored ships
  keep their equipment" on and movable equipment mounted, Keep then asks whether it moves to the new ship (Yes: mounted
  there, the rest to the hold, the original) or stays on the old hull in the club (No) (`StationMenu.AskMoveEquipment`,
  `Hangar.KeepAndBuyShip(ship, moveEquipment)`; the lounge sellers' Keep too).
- **Lounge**: agents 21-26 once campaign > 16 (mechanics = ship mods, 25 special items, 26 a ship of [55..60] the player
  neither flies nor stores, bare hull into the storage, greeting only until owned). Medal 37 counts the stored hulls;
  the map shows Shima as "Secure" after the siege and the orange house when owned.
- **Expansion at the start** (remake, GitHub #8): the original's Kaamo Club in-app purchase (texts 78 / 88 / 93;
  `Status::resetGame` sets state 3 while it is bought) is the new game's toggle in the Game options panel after the
  economy (with Hardcore and Start game) (`MainMenu.KaamoFromStart`, PlayerPrefs `newgame_kaamo`): the club owned from
  the start, no siege, no purchase.
- **VIP Card** (remake, players' suggestion: Discord #suggestions "Kaamo Club features", 2026-10-25): the original's second
  in-app purchase "Kaamo Club VIP Card" (texts 80 / 90 / 95, `options[0x38]`: the Kaamo dealers at half price, i.e. x2
  without it, `blueprints_mods.md` 2.2; the remake always charged the card's price) is a card bought with credits:
  `KaamoClub.VipPrice` 1 000 000 $, the Store tab's first row while `KaamoClub.VipForSale` (club owned, not bought, option
  on "Buy at the club"), its perks in the details, BUY -> Yes / No -> `KaamoClub.BuyVip` -> a welcome. `Session.KaamoVip`
  (save field `kaamoVip`, no version: older saves have none; New Game+ keeps it). Options > Gameplay "Kaamo Club VIP Card"
  (`Settings.KaamoVipMode`, local): Buy at the club (default) / Always on (no card) / Off (the original club, nothing below).
  With its perks (`KaamoClub.VipFeatures`), only in the club's storage (the stations' shops stay the original's,
  `HangarWindowKaamoVip`): a search box and a sort button on the header bar (the Remake's after the tabs, the Original's
  between its title and the tabs; F / R, the controller's right / left stick press: the search opens `PadKeyboard`; the game's
  keys rest while typing, `NetChat.SetTyping`, Enter / Esc / a press elsewhere end it); the search matches name, category /
  race and a mod's tag, the sort is the club's order / A-Z / value (items: the middle of the price range, ships: the sell
  value; highest first) / race (ships) or type (items), kept while the game runs; the stored hulls move to a Ships tab of
  their own (key 4, Q / E walk Ship, Store, Ships, Blueprints), the Store keeps the goods. Everywhere else a dealer's ship
  of a type parked at the club carries an AT KAAMO badge with the map's house (`map_home`), and a lounge seller's ship
  says so in the offer (`loungeShipAtKaamo`). No multiplayer handling: the flag rides in the save / profile.
- Not wired in the original and left out: advert 189, the 478 hint (no shop to jump to), radio 3162 / 3163 (voiced, never sent).
