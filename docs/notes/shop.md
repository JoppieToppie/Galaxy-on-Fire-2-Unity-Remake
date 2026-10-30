# Shop

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Shop

Research: `Reference/research/shop.md` (+ `item_icons.json`, reference price code `Reference/tools/shop/prices.py`).
Rules are plain C# in `Shop` (prices, stock, dealer ships) and `Hangar` (one opening of the window: trading, mounting,
ship trade); the UI is `HangarWindow` (tabs Ship / Shop in `UI/Station`, styles `HangarWindow.uss`) driven by
`StationMenu`. Player state is in `Session` (credits, `Equipment` = mounted stacks with secondary ammo as the amount,
`Cargo`, the last 3 stations' stock).

- **Economy** (`Session.Economy`, the new game's second step after the difficulty, saved from v10; older saves = Android):
  **Android** = the remake's data files (the Android OBB's items.bin / ships.bin) or **Default** = the macOS / Windows /
  iPhone tables (KiritoJPK's `GOF2_2.0.16_Default_Economy.apk` differs from the Android one only by
  `assets/data/bin/items.bin` / `ships.bin`, kept in `Reference/binaries/default_economy/`;
  `Reference/tools/shop/build_default_economy.py` writes `Resources/GoF2Data/economy_default.json`: the complete values of
  the 188 items and 55 ships that differ). `Database.Load` applies the overlay when the session's economy is Default
  (`Database.Economy` says which): item prices, tech levels, occurrences, every attribute (and the stats), blueprint recipes;
  ship prices, stats and slots. Android vs Default: commodities ×2-25 (Energy Cells 1400-1540 / 560-700), tractor beams ~×18,
  signatures 500 000 / 7500 (Android: only at 107, attr 61), shields / armor / repair bots ~×2.5, blueprint recipes several
  times the ingredients (Khador Drive 200 / 10 Pyresium), starter weapons / scanner / drill cheaper, ships ×0.5-0.8; a few
  stats (mining lasers' attrs 32 / 33, PE Proton 17 / 49-51), tech levels and Night Owl's 3 / 2 secondary slots.
  Multiplayer sessions always use Android (the shared stock). The save slots show difficulty and economy.
- **Prices** are deterministic per station: min + distance factor * (max - min) +- 2 %. The original draws the +- 2 %
  from `java.util.Random(station)` reseeded per list (cargo, mounted, stock), so an item's price depended on its list
  place and a unit bought from the stock could sell for more from the hold (a multiplayer report: Garuda at Thynome
  bought 724, sold 746, repeatedly); remake, in a multiplayer session only: `java.util.Random(station * 10007 + item)`
  per item (`Shop.PriceList`, `NetSession.Active`); single player keeps the original's, verified identical to
  `prices.py` (its docking's prices pinned, `Hangar.PinPrices`). Selling pays the buying price (a faction member at its
  own station sells at the 10 % cut too, `Hangar.Sell`); anything sold joins the station's stock. Loma (system 25)
  always charges the maximum in the Android 2.0.16 build; the iOS 1.1.19 build charges `(int)(maxPrice x 1.05)`
  (`Status::calcCargoPrices`, the Android disassembly has no multiply), which the remake applies with the Default
  economy (`Session.Economy`), the Android economy keeping the maximum. Remake: an item's price is fixed for the docking (`Hangar.PinPrices`, cleared by
  `Hangar.NewDocking` in `StationLevel`): an item's price depends on its place in the lists, so a trade moving it
  between the stock and the hold could shift it by up to ±2 %, and in multiplayer, where the window is priced again
  after every trade, buying and selling at once made money with every click.
- **Stock** (`Generator::getItemBuyList`) and **dealer ships** (`getShipBuyList`) are rolled with `UnityEngine.Random`
  when the station isn't among the last 3 (`Status::departStation`: arriving in its orbit, `Shop.ArriveInOrbit` from
  `SpaceLevel`, or docking; a revisit doesn't reorder the stack; the remake pushed only on docking until 2026-10-16, so
  Katashán's empty dealer, `getShipBuyList`'s 1 in 6, stayed until 3 other dockings; not in multiplayer sessions;
  the iOS 1.1.19 build rolls 2..5 ships, never none, and rolls a duplicate again: the Default economy does the same, as
  it takes that build's stock factor (step + 10) / 45, its 40 % AB-1 Retractor in every shop during steps 17..24 and its
  exhaust anchors for the VoidX / H'Soc / Specter / Scimitar, `weapons_hd_default.json` over `weapons_hd.json` in
  `Database.ApplyDefaultEconomy`, built by `Reference/tools/shop/build_default_mounts.py` from the IPA's `weapons_hd.bin`);
  re-docking after > 30 s nibbles 0-2 units off each row.
- **Cargo:** every unit is 1 t, mounted items weigh nothing; buying never checks space, launching is refused while overloaded.
- **Unknown item numbers** (a Windows crash report, 2026-10-12: `ItemInfo.Category` on a null item from `HangarWindow.AddRow`
  after every trade; where the number came from is not known yet): `Shop.AddToCargo` refuses an item `Database.Shared` lacks
  (a warning with the caller's stack), `Hangar.ShopItems` leaves one already in the hold or stock out of the list (a warning
  once per number), `ItemInfo.Category(null)` is "", and `SaveGame.Load` drops such items from the save (after
  `ModSaves.Fix`, `DropUnknownItems`: hold, equipment, shop memory and its bar offers, the Kaamo Club, the parked ship,
  blueprints, the production queue; a warning each). A second report (Android, 2026-10-10) had it at docking.
- **Blueprint states** (`Blueprints.Fit`, from `State` and the readers): the per-ingredient list is made as long as the
  product's current recipe; one saved under another recipe (Default economy, a mod, an empty one for a then-unknown
  product) was shorter and the Blueprints tab read past its end (an Android crash report, 2026-10-11).
- **Cloud Diagnostics**: `Bootstrap.Init` switches exception capture off in the Editor
  (`CrashReportHandler.enableCaptureExceptions`): contributors' clones reported their Editors' errors.
- **ScrollTo** from a scheduled callback checks `contentContainer.Contains` first (`CheatsCatalog`'s icon grid,
  `HangarWindow.ScrollToSelected`): the list can be rebuilt before it runs, and ScrollView.ScrollTo throws then.
- **Sell all / Buy all by holding the arrow** (remake, players' suggestion; Store all / Take all in the Kaamo Club's
  storage; `HeldTrade`, `HangarWindow.TradeAll`): a held ‹ SELL / BUY › (pointer, A / D, the arrows, the D-pad) trades
  unit by unit, faster and faster (an ease-in, t^2, never slower than one per 120 ms after 200 ms; the original's repeat
  was 200 ms, then 30 ms, 5 units after 4 s), the whole stack counted at the press gone after two seconds: what the hold
  has of it, or the station's stock as far as the hold has room, as far as the credits reach (no "not enough credits"
  after some units); from the second unit (a click, one unit, shows none) a bar fills the arrow with the share of that
  stack traded (`.trade-arrow-fill`; not the time held: a small stack or one the hold's room cuts short is done sooner);
  every unit plays the buy / sell button sound (0x65 / 0x64; the original's held repeats were silent,
  `HangarWindow::update`, only a tap sounded, `OnTouchEnd`), its pitch rising from 1 to at most 1.5 as the stack goes,
  on voices of its own (`HeldTrade.Tick`, at most one per 30 ms). Until 2026-10-21 these were separate Sell all / Buy
  all buttons under the arrows (Shift + left / right, LT / RT, held a second); the user asked for one button (the
  buttons were squashed: a Button with child elements stops measuring its own text). In a session the whole hold,
  singles and all, is one stock message sent on release (`Hangar.BeginBatch` from the press, `EndBatch` in
  `ReleaseArrow`; the user's request: not one per unit; `NetState.StockItemRpc` takes up to 10 000 units, grants what
  the host's row has and refuses the rest in one `ItemRefusedRpc` with the count, the faction tax on the granted units).
  The carrier shop's BUY the same (its Buy all button and Shift + right / X went).
- **List after a trade** (remake, `HangarWindow.Rebuild`; after a mount, demount or swap too, #45: they reset the
  selection to the top): the same row stays selected; when it is gone (missiles of a mounted
  type bought out: they go onto the mounted stack, not the hold) the row that took its place (or the one before it at the
  end, `NearestSelectable`), so the list keeps its place; it used to jump back to the top.
- **Lists wrap round** (remake, players' request; `HangarWindow.MoveSelection`, `CarrierShopWindow.Move`): up on the
  first row selects the last and down on the last the first (every tab, the blueprint ingredients, the carrier's shop),
  scrolled to it, so the commodities are one press up from the shop's top; only a fresh press wraps, a held key's repeat
  stops at the end. The mouse wheel / touch scrolling don't wrap; the station's focus-navigated lists (lounge visitors,
  Missions, save slots) don't either.
- **Mounting:** first free slot of the item's type; on docking the equipment is fitted to the ship's slots (remake,
  `Hangar.FitToSlots`, from `StationLevel`: a mod lowering a ship's slots or a debug hull swap could leave more mounted
  than it has; the rest goes to the hold in slot order like a bought ship's, story items keep their place); secondaries
  move with their ammo; one-per-ship categories (shield, armor, booster, scanner...) swap (287); bought missiles of a
  mounted type join the mounted stack. Remaining missiles are written back to `Equipment` when docking. Remake: the Ship
  tab tints mounted rows with a cyan edge and badges the hold's candidates IN CARGO under an amber "Available in cargo"
  header; a a passenger cabin can't be demounted while the other cabins couldn't seat the passengers aboard (323,
  `HangarWindow::selectItem` 0x178e20); a double click / double tap mounts or demounts (the Mount / Demount button's
  action; a controller's confirm button does the same).
- **Hangar opened with A / Enter** (#104, `StationMenu.hangarConfirmHeld`): the window ignores Enter / A until the press that
  opened it is released; the Hangar button took the press, and in the same frame the window bought its first row.
- **Ships:** price -1 % in systems of the ship's race; trade-in at full price; remake (players' report), in a
  multiplayer session only: a bought hull (dealer, lounge seller, Kaamo Keep, a blueprint's ship) starts bare, the
  saleable mounted items go to the hold (`Hangar.SwitchTo` dismount; the story's unsaleable items stay mounted; single
  player, like the original, moves them onto the new hull); equipment that doesn't fit goes to cargo; the hangar's
  turntable swaps the model. Remake (#60 / #62): without the Kaamo Club the 304 question adds what happens to the old
  ship ("Your X is traded in for N, so you pay / get back M", `shopTradeInNote` / `shopTradeInRefund`); the details
  panel's ship stats of a dealer or stored hull carry the original's comparison arrows against the ship flown
  (`ItemInfo.ShipStatsCompared`, the Kaamo upgrades counted on both sides; the original shows them only in its item
  window); a Shop tab row of an item the hold has gets the Ship tab's cargo look (amber edge, IN CARGO).
- **New game:** 0 credits, like the original (sell starting gear to get money). `Session.CampaignMission` is a free-play
  20 (past the tutorial locks) until there is a campaign. Var Hastra always stocks a drill and energy cells then
  (remake-only, see "Mining" and "Star map and system travel").
- Blueprints tab: see "Bar and freelance". Ship mods (Kaamo mechanics): +40 hull, +30 t cargo, +1 equipment slot,
  handling +0.2, kept with the hull (`Session.ShipMods`). Remake (players' suggestion): a hull's mods show as lines
  under its description, "Kaamo Club armor upgrade applied (+40)" etc. (`ItemInfo.FillModLines`: the hangar's own /
  dealer / stored ship, the carrier's repair page, the item window), and the hangar's stat rows include them with "(+)"
  (`ItemInfo.ShipStats(..., mods)`). **Stacking** (remake, Options > Gameplay "Stackable Kaamo Club upgrades",
  `Settings.KaamoStacking`, on by default): the mechanics fit their mod again up to the upgrade limit
  (`Session.MaxModLevel` = Options > Gameplay "Kaamo Club upgrade limit", `Settings.KaamoMaxLevel`, 2..20, default 5; 10
  until 2026-10-16, after players' feedback that uncapped they reached 45 equipment slots on a Cronus; its row only
  while stacking is on; a session rule: the host's option, a dedicated server's `-kaamomaxlevel N` / `/set kaamomaxlevel`,
  carried beside the rule bits as `NetRules.HostKaamoLevel` / `NetState.KaamoLevel`; a server with profiles cuts
  profiles back to it, `NetProfiles.ClampMods`; levels fitted before a lower limit stay in single player; at the top the
  mechanic says so, `kaamoModMaxed`), a level = how many times it is in `Session.ShipMods` (`Session.ModLevel`; every
  effect is per level: hull, cargo, slots, `BuildFlightStats`' handling upgrades), the price x2 per level fitted
  (`LoungeChat.ModPrice`, capped at int.MaxValue; a ship without a price, the Cronus (37, Khador's gift at step 77), is
  valued at 2 500 000 between its sister ships the Typhon and the Nemesis, `UnpricedShipValue`; in multiplayer the
  Cronus itself costs 1 150 000, `Database.CronusSessionPrice`: its upgrades cost 0 at every level; the offer adds
  "(Upgrade level N.)"), the lines show the total and "level N"; off = the original's one of each
  (`LoungeChat.ModForSale`), levels already fitted stay. The list format is unchanged, so saves, traded-in / stored
  hulls and presets carry the levels. A traded-in hull keeps them in the dealer row that takes it
  (`StationStock.shipMods`, by ship index; `HangarWindow::OnTouchEnd` 0x176d94 adds the old ship's mods to that row and
  gives the bought row's mods to the new ship), so selling a modded ship and buying it back returns its mods; the row
  shows (+) and lists them. Rows replaced outside a trade (story steps, the multiplayer host's shared list) drop their
  mods (`PruneShipMods`).
- Kaamo Club storage (Store tab, stored hulls, 327 Sell / Keep): see "Kaamo Club".
- The Supernova wrecks' hidden blueprints (hacking): see "Supernova add-on".
- **Dealer extras** (`Shop.GenerateShips`, `getShipBuyList` 0xa0eb8): none in the supernova system (no stock either);
  Kothar after Valkyrie exactly 37 / 38 / 40; Quineros (107) every pirate ship plus 45-48 for the terminated Most Wanted
  6 / 12 / 18 / 24; Thynome (10) the VoidX alone with every gold medal (re-rolled on docking); a roll of 0 ships gives
  none; after Valkyrie the Vossk dealers 1/2 each 39 / 41 and every Vossk fighter roll 60 / 25 / 15 % 9 / 41 / 39
  (`NpcTables.RandomFighter`, traffic too); Katashán (120) after 158: 44 (Extreme or all Supernova medals) and 49.
  Docking fix-ups (`Story.OnDocked`): 50 Void Crystals at Thynome after the game is won for a player without a Khador
  Drive, 10 energy cells at 10 / 100 / 101 for a nearly empty hold; stock extras 190 (139, system 25), 209 at 126.
- **Item details** (`ItemInfoWindow`, `ListItemWindow`): the selected row's "i" (or I / controller Y; remake: with a
  controller the "i" shows the yellow Y instead, `.row-info-pad`, and an INFO hint in the hangar's hint row) and the
  lounge's "Let me see it" (776, no price) open a full-screen window: 390 Info, 643, the name box, the stat rows (ships
  with 0x512 / 0x513 / 0x514 comparison arrows against the current ship, mods "(+)"), the 3D player-variant ship on
  layer 30 into a RenderTexture over the floor glow 0x50b (remake: framed by its bounds along the original's camera
  direction), drag / A D / stick turning (120 px = 1 rad, from 260 px, ×0.9 fling), 280 Description with the text; sound
  97 on opening; Back 170 / Esc / B. Remake (#82): the hangar's details panel shows the same turning ship under the name
  for ship rows and mod ship blueprints (`ShipPreview`, shared with this window, each on a stage of its own; the shop
  icon hidden meanwhile, `.detail-preview`), spinning slowly (0.3 rad/s) unless dragged; a click / tap on it opens this
  window. Phones (PR #107): the preview's
  texture is the box's own size on screen times the render scale, MSAA 2x, no mipmaps (the desktop's screen-tall 2x
  supersampled one cost frame rate), the ship-only view keeps the desktop's 1300 x 800 box, and the hangar list's
  scrollbar has a 56 px touch area around the slim track (`.list-bar-track`), its handle at least 60 px.
- **Weapon mod factors** (attrs 39 / 40) read "-10 %" for a negative one (#105: "+-10 %", `ItemInfo` Signed).
