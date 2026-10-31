# Mods

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Mods (remake-only)

Player-made content as data (no code: the game is IL2CPP), in `Scripts/Runtime/Modding` (`GoF2Remake.Modding`); the modders'
guide is `Modding/README.md` (keep it in step, and the AI version `Modding/ai/gof2-modding/SKILL.md` with it: an Agent
Skill / paste-in guide for Claude and ChatGPT, with `reference.md` from **GoF2 > Build > Modding AI Reference** and the
how-to `Modding/ai/README.md`), the examples `Modding/Examples/plasma_arsenal` (items), `frontier_systems`
(systems / stations); cruise drives (navigation.md "Cruise drive"; a mod's `encounters.json`,
`ModEncounters`, adds to the cruise encounter table). Items, ships, systems and stations, quests and bar missions (event graphs), voice-over, music.

- **Unreadable mod files** (Android crash reports, 2026-10: "A local file header is corrupt" / "Corrupted data ReadInternal"
  on single GLBs of the GoF3 Ships zip): `ZipSource` reads one entry at a time under its gate, `Dispose` takes the gate too
  (a Mod Browser rescan, `ModManager.Scan`, released the archives while the background loaders still read them), checks
  the length and the CRC-32 (`Crc32`, slicing-by-8, ~2.4 GB/s on the desktop: the inflater doesn't, and a damaged entry
  inflated without an error into wrong bytes) and tries once more on the archive opened afresh; then a `ModFileException`
  ("X is damaged in Y.zip: download or import the mod again"). The loaders (`ModShips`, `ModStations`, `ModWeapons`,
  `ModCharacters`, `ModMaterials.Texture`, `ModJson.Read` as a ModJsonException, `ModInfo`, `EventRunner`) make it the mod's
  warning and never `Debug.LogException` a file error (`ModFileException.IsFileError`): the cloud diagnostics count every
  logged exception as an unhandled one; a load the mods changed under says nothing (a newer one runs). `ModInfo.NetHash`
  (every installed mod's, on or off, in each multiplayer connection's data) hashes an unreadable file as "damaged" with a
  warning, like `Hash` (`ModNetHashTests`): thrown, it would stop every host and join; the main menu's multiplayer start
  catches anything thrown (`MainMenu.TryStart`, `NetGame.AbortStart`: the session closed, the reason in the panel's status
  line, Extra `mpStartError`) instead of staying on its fade or "Connecting..." (a Discord report, cause not confirmed).
- **A mod** = a folder or a `.zip` (files at its root or in one top folder, `ZipSource`) with `mod.json`
  (`ModManifest`: id (a-z 0-9 _ -, the lasting name), name / description (a string or per language), version, author,
  website, preview image (default preview.png), dependencies). Mod JSON is read with Newtonsoft's JToken (`ModJson`: comments
  allowed, errors with file and line), not JsonUtility. Folders (`ModManager.Folders`): `persistentDataPath/Mods` everywhere,
  `<exe dir>/Mods` on desktop players, `<project>/Mods` in the Editor (git-ignored).
- **On / off** (`ModManager`): PlayerPrefs `mods_enabled` (the ids in load order); new mods start off; `Active` = enabled,
  unbroken, dependencies on, dependencies first; `Revision` bumps on every change. A broken mod.json or content file makes the
  mod unusable (`ModInfo.Errors`), unknown fields are warnings. `ModInfo.Hash` = SHA-256 of every path + file (first 12 hex).
- **Items** (`ModContent`, items.json): `{ "id", "base": n | "mod:id", fields }` adds an item (a copy of the base: type,
  category and *look* kept, `ItemData.modded` / `modKey` / `Look`), `{ "override": n | "mod:id", fields }` changes one;
  fields: name, description, techLevel, occurrence, `stock`, prices (`price` n or [min, max]), price systems, `vosskOnly` (attr 60),
  `alwaysSoldAt` (attr 61; a mod item's always stocks it there, also at the stations whose original filter allows one
  kind only, Var Destro 105 weapons, Quineros 107 equipment, Valkyrie 101, Sao Perula 106: `Shop.GenerateItemsOriginal`
  modAlways), `stats` by name (`ItemStats`: readable names + items.json's statList keys, kept in step with the
  attribute), raw `attributes`, `defaultEconomy` (the same, for the Default Economy). **`stock`** (remake, attributes 125 /
  126, a number or [least, most], 1..`Shop.MaxStock` 9999, clamped with a warning): how many units a shop holds of it
  (`Shop.ModStock` in `Shop.GenerateItemsOriginal`, in place of getItemBuyList's amount by item type: goods and secondaries
  rnd(15) + 5, a good x the distance to its cheapest system up to x20 (x2 Extreme), weapons / turrets / equipment a fifth
  of it, so 1-3); `occurrence` still decides whether it is stocked at all, an `override` sets a game item's too, and such a row
  is left out of `EnterStation`'s computerTradeGoods nibble, so an exact amount stays exact. Texts inline or `text/<lang>.json`
  (`items.<id>.name`). `Database.Load` merges after the economy overlay (`ModContent.Apply`, only for the GoF2Data folder).
  A mod item's 0 occurrence means never (the add-on items' random roll in `Shop.GenerateItems` skips them).
- **Tech levels above 10** (a modder's report 2026-10-17, the community's "Tech Level 11 Add-On"): the original's items and
  stations both end at 10 (`Shop.TopTech`), and getItemBuyList stocks an item only where techI <= techS, so a tech level 11
  item was never sold. A mod item may now have 0..`Shop.MaxItemTech` (20; past it clamped with a warning, `ModContent`);
  `Shop.StationStocks` = techI <= techS or a station of tech level 10, which stocks every item above it by its occurrence
  alone (the 16 TL10 stations; Var Destro / Sao Perula / Quineros keep their kind filters); the original items never reach
  the new branch. A ships.json `dealer.minTechLevel` above 10 likewise = the TL10 stations (`ModUnlocks.AddDealerShips`).
  Loot: a tier-5 pirate outpost's two pieces of equipment are each 1 in `TrafficPlan.TopTechOdds` (3) one of
  `Shop.AboveTopTech` (mod weapons / equipment above 10, priced, occurrence > 0, no blueprint product, `available`, a mod
  campaign's filter; picked by occurrence, `Shop.PickByOccurrence`) while it isn't empty (`TrafficPlan.OutpostGear`; no extra
  random draws without such items); the enhanced capital ships' TL5+ gear pool takes them as before; `NpcTables.RollLoot`
  keeps the original's no gear from tech level 8. On the way, `available` is now honoured by the capital ships' gear and
  shop (`CapitalShips.RollLoot` / `ArmoryItem`), the lounge item sellers and the Purchase missions (`AgentGenerator`); a
  mod repair bot / gamma shield takes its base's repair rates / loop sound by its
  look (`PlayerHealth`, was by index: a repair bot based on 75 repaired at Mk II's rate). The
  Mod Builder's Items page takes 1-20 (`mbItemTechLevel`). Freelance difficulty stays capped at 10. Tests
  `ModTechLevelTests`. Verified in Play mode with a local test mod (`Mods/tech11_test`: a TL11 armour and a TL11 tractor).
- **Ships** (ships.json; the format of PR #34's custom_ships.json, `CustomShipData` in Database.cs): `{ "id", "model": glb,
  stats, slots, race, hangarHeight, mounts (slotType 0-4 (4: manoeuvring jets with a `direction`, `LateralThrusters`),
  `upsideDown` turrets, `builtIn` turrets: `PlayerTurret` puts an empty pivot / gun stand-in at the point instead of the
  item's `turretMounted` model (the item's gun, stats, aim, auto fire, turret view; shots from the point + 120 units,
  the view 60 units above it), `BuildStatic` (hangar, other players) shows nothing there; a plasma collector keeps only
  its stream, `sn_plasma_stream_anim_add` moved from a copy of the item's model onto the stand-in gun, so it pours from
  the point and turns with the aim; the ES2 kit's turret sockets), icon, modelLength / modelYaw,
  engineGlowRadius, materials, throttleGlow / extraGlows, lounge }` adds a ship (numbered after the original 64,
  `ModRegistry.Kind.Ship`, assembly "ship_NNN_mod", pack "mod"); `{ "override", armor, cargo, price, priceDefault, slots,
  handling, mounts }` changes one (`mounts` replaces its hardpoints, with or without a new model). Both take `npcTurrets`
  (remake, generic turrets: turret items, numbers or "mod:id", the NPC ships of that type carry on its turret mounts in space
  and parked in the hangar; `ModContent.ApplyNpcTurrets` -> `Database.NpcTurrets`; not a turret / more than the mounts: a
  warning). `ModContent.ApplyShips` merges them into Ships / Assemblies / WeaponMounts; `CustomShips` (the PR's
  API, now over the mods) serves race, hangar height and the lounge sellers; `GameNames.Ship` the texts (`ships.<id>.name`).
  A ship whose mod is off is a placeholder (never sold); `ModSaves.FixShips` puts a save flying one into a Phantom (refunded,
  its equipment in the hold) and drops stored / dealer / seller rows. An `override` of an original ship with `"model"`
  replaces its model (`ModContent.ModelOverrides`: built by `ModShips` like a new ship, assembly "ship_NNN_mod" added by
  `ApplyShips`, which `Database.ShipAssembly` returns before the original's; the original's mounts unless the entry has
  its own; not a CustomShips ship: no sellers, the stats stay). Verified in Play mode: the Groza (22) with its Galaxy on
  Fire 3: Manticore model (`Mods/groza`, converted from the .aem / .aei pack; git-ignored) in the hangar and in flight.
- **A modder's requests, 2026-10-16** (tests `ModEquipmentLookTests`, `ModBlueprintsTests`, `EventGraphCompilerTests`):
  a mod's booster sounds like its base (`PlayerEngine.BoostSound` by `ItemData.Look`: it was silent, the boost sound picked by
  item number) and a mod's tractor beam draws its base's beam (`CombatAssets.Tractor(look)`). **Multi-grab tractor beams**
  (`CombatRadar`, items.json stats `tractorTargets` 122 (1..8) / `tractorRange` 123 (units, default 30000)): N - 1 extra beams
  (or all N with no lock running) take the nearest crates within reach by themselves (`UpdateExtraBeams`, a `Pull` each with
  its own beam object; not steals; a mission crate only while the hold has room), no salvage lock, so the ship lock stays free
  (`Publish` counts only the lock's beam); they let go in the turret view, on death and in jumps; `Crate.pulled` keeps the
  manual lock off them and drives multiplayer's claims (`NetCrate.TrackPull`). Hackable wrecks for events and quests:
  `/spawn [players] wreck [race] [named <name>] [at x y z]` (`NetAdmin.Order.Wreck` 31, `DebugSpawner.SpawnWreck`,
  `TrafficPlan.HackableWreck`, `SpawnSpec.eventWreck`: hacked once, `Traffic.OnHackWon` sets its docking type 0), the quest
  step `wreck`, the graph's Spawn node with Ship "wreck" (the picker's "Hackable wreck"); hacking any wreck fires `on hacked`
  (above, Events). In a session the wreck exists only in the games it was sent to. Verified in Play mode (a test mod with a
  4-beam tractor based on 69 and a booster based on 73, at Dis): four blue beams at once on four crates, six crates in, the
  one past the range left; the booster's sound index 2 (73's); a quest's `wreck` spawned "Hulk", its `on hacked` paid and
  showed "Hacked Hulk" after the hack (`HackWon` invoked; the hacking game itself not played). Not tested in multiplayer.
- **Built-in equipment and a steady engine glow** (remake, a modder's requests, 2026-10-16): ships.json `integrated` (new
  ships and overrides; `ModContent.ApplyIntegrated`): `khadorDrive` true / false (`Database.IntegratedDrives`, defaults
  37 / 38 / 40, `Ship::hasJumpDriveIntegrated`; read by `GalaxyMap.HasIntegratedDrive` through `Database.Shared`) and
  `cloak` true (95's stats) / a cloak item (category 21) / false (`Database.IntegratedCloaks`, defaults 44 / 49 -> 95,
  `Ship::hasCloakIntegrated`; `PlayerCloak.HasCloak` / `Attach`, `Hangar.HasCloakBay`); an override can take an original's
  away; NPCs' cloaking is unchanged (Specters and ship 49). Any item (2026-10-18, the user's request): `integrated.items`
  [n | "mod:id" | { item, amount }] (`Database.IntegratedItems` / `BuiltInItems`; a secondary's amount its starting ammo,
  default 10; a commodity refused with a warning): `Hangar.SyncBuiltIn` (at docking before `FitToSlots`, `SpaceLevel`'s
  spawn and `SwapPlayerShip`, `SwitchTo`, `Story.TakeShip`, `StationLevel.ReplacePlayerShip`, the Mod Builder's loadout)
  puts the flown ship's first in `Session.Equipment` as `ItemStack.builtIn` stacks (saved with the flag), so every reader of
  the mounted items has them; they take no slot (`Hangar.TakesSlot`, `MountedOfType`, `FitToSlots`, the Customize stats),
  can't be demounted or swapped, and never go to the hold, the Kaamo Club, another ship, a preset, a profile's worth or an
  original-game export; another ship's go at the next sync, a missing one comes back (an emergency system used up: back at
  the next docking; a kept secondary keeps its ammo, the shops' missiles join it). Like the Specter's cloak bay, a fitted
  one-per-ship equipment item (`Hangar.Replaceable`: type 3, `Shop.CanInstallMultiple` false) of a built-in one's kind
  takes its place without a slot (`InBuiltInPlace`) and the built-in one steps aside until it is demounted (SyncBuiltIn
  leaves it out); built-in weapons and turrets add to the slots. The hangar's Ship tab lists them under "Built into the
  ship" (BUILT IN badge; a fitted one there "Replaces the built-in X"). The glow: a material's `engineIdle` (with `engine`, 0..1, its
  share at throttle 0, `EngineMaterialGlow.Target`; 1 = lit whenever the engines run, dark parked) and a throttle glow's
  `off` (`ThrottleGlow.off`: its level parked in a hangar with the engine part hidden, default -1 = idle; idle = full with
  off 0 = steady): before, a mod ship's glow either followed the throttle or stayed lit on the pad. Tests in
  `ModContentTests`. Not yet looked at in Play mode.
- **From PR #34, now general** (for any ship with the data): several turret mounts (`PlayerTurret.AttachAll`, one item per
  mount in equipment order, `upsideDown` / `turretUp` mounts; `FreeLookCamera` visits Turret 1, 2...; the HUD's auto-fire and turret-view
  hints for each kind; `Hangar` takes a second turret into a second slot), `ThrottleGlow`, lounge ship sellers
  (`AgentGenerator.AddCustomShipSellers`, `AgentOffer.SellShip`, `LoungeChat` / `LoungePanel` ConfirmShip: "Okay." buys, with
  the Kaamo Club owned 327 Sell / Keep; `Hangar.BuyShipFor` / `KeepAndBuyShipFor`), `Shop.RaceOfShip`, `StationTables.ShipY`.
  The turret view's crosshair follows the gun (`FlightHud.UpdateCrosshair`; collectors 0x1f5d `GoF2Hud/plasma_crosshair`,
  `.crosshair--plasma`); before, it stayed on the nose for every manual turret.
- **PR #34's ships as mods** (not in the repository: third-party models): `Mods/starwars_ships` (Jedi Starfighter,
  Millennium Falcon), `startrek_ships` (Enterprise-E, Enterprise-D), `halo_ships` (Space Banshee); GLBs exported with
  `ModModelExporter` (Project window, **GoF2 > Export Model As GLB (Mods)**; its own Editor assembly `GoF2.ModExport.Editor`,
  glTFast.Export isn't auto-referenced; the plain-materials mode for ships whose ships.json materials replace them all),
  the PR's textures (above 2048 px shrunk to 2048, the PR's import size), icons, previews, CREDITS.txt / mod.json credits
  (the Falcon's and the Banshee's sources aren't stated in the PR).
- **The look** (`ModContent.ItemLook`, `ItemData.Look`, `Gun.lookIndex`): the original item a mod item is based on supplies
  the icon (`ItemInfo.ItemIcon`), the weapon fx (`WeaponFx.Load`), the text sprite, the turret models, trails, loop-release
  rules and the index-keyed behaviour (beams 9-11 / 228, cluster pools, sentry looks). Code keyed on an item's number for a
  *behaviour* should use `Look`; story / rule checks keep the real index.
- **Names** (`GameNames`): every item / ship name and description goes through it (the original's text blocks 1274 / 1041 /
  913 / 977 + index sit back to back: item 233's name would be the first medal's); never `Localization.Get(1274 + i)`.
- **Numbers** (`ModRegistry`, `persistentDataPath/mod_registry.json`, outside the Mods folder): single player gives each "mod:id" key the next free
  number after the original table (233+) for good; a key whose mod is off gets a placeholder (`ModContent.IsMissingItem`:
  commodity 22, no price, never stocked) so the table stays 0..N-1 and saves keep their numbers. Sessions number afresh in the
  session's order (the same on every game) and never touch the registry.
- **Saves** (v12, `ModSaves`): `mods` (id, name, version) and `modKeys` (kind, key, number, price). `MissingMods` → the main
  menu's and the station's load dialogs warn; `Fix` (before `SaveGame.Apply`) moves keys to their current numbers (a save
  from another device; `ModRegistry.Adopt` takes the save's number when free) and removes a missing mod's items (equipment,
  cargo, Kaamo storage, the parked ship refunded at the saved price; shop rows, bar sellers, records, blueprints, a Purchase
  mission dropped). Imports accept a modded number the save names (`ModSaves.Names`).
- **Mod campaigns** (`ModCampaigns`, a mod's `campaign.json`: name, description, image, imageHover, showTitle, startStation,
  startShip, credits, equipment, cargo, standing, raceStanding (mods' races, mods-world.md "Mod races"), quest, galaxy
  mod | all, items mod | all, ships mod | all, trafficShips per race): a card after the three campaign cards, picked the same way (`MainMenu.RefreshModCampaigns` / `ModCard` /
  `PickModCampaign`: the art cropped to the card, the hover art fading in while selected, a name plate unless showTitle is
  false; four cards shrink to fit the row (`.card-row--4`), five or more go small and the row scrolls sideways
  (`.card-row--many`, `card-scroll`, `DragScroll` horizontal too)) → difficulty →
  economy → `BeginGame` → `ModCampaigns.Start`: Session.ResetNewGame's state, then `Session.ModCampaign` (save v14
  `modCampaign`), `FreePlay` (no GoF2 story, index 20), the start station / ship / credits / equipment / cargo / standing, the
  quest as a pending `GraphQuestState` (`EventRunner.RestorePending`: started once the station is up; the record takes the
  graph's title). `ModCampaigns.Current` while its mod is on. "galaxy": "mod" = the campaign mod and its dependencies'
  systems only (`ModWorld.SystemOwner`): `GalaxyMap.Visibility` forces the rest hidden on every call (the map draws no sun
  for them, so no route or jump reaches them), `AgentGenerator.GenerateStationIndex` picks `ModCampaigns.RandomStation`
  instead of R(135), the news ticker skips them; "items" / "ships": "mod" filter `Shop.GenerateItems` / `GenerateShips` (the
  originals renamed *Original) and the bar's item sellers; `NpcTables.RandomFighter` → `ModCampaigns.TrafficShip`. Verified
  in Play mode with a local test campaign (Mods/test_campaign: Kepler, the Jedi Starfighter, the plasma weapons): the entry,
  the start (station, ship, credits, equipment), only Kepler visible, mod-only dealers, Jedi / Banshee traffic, missions
  inside Kepler, the quest running, save / load keeping it.
- **New-game options** (`ModGameOptions`, a mod's `gameoptions.json`: id, name, description, image, imageHover, showTitle,
  default): campaign-style cards in the Game options panel (`MainMenu.RefreshModOptions`: the panel goes wide, the toggles in
  a left column, the cards in a right one that scrolls sideways with "N options · scroll for more" past two, Start game under
  both; `NavigateGameOptions`: left / right through the cards, right from the column to the last card visited, left out of
  the first card back to the toggle it came from, up / down in the column; the containers are made again after a UI reload),
  ON / OFF badge, the art dimmed while off; the choice is remembered (PlayerPrefs `newgame_modopt_<mod:id>`) and becomes the
  game's at `BeginGame` (`ApplyChoices`, `Session.ModGameOptions`, save v16 `modGameOptions`); a multiplayer session plays
  with each option's default. Verified in Play mode with 8 options from two mods (with and without images, a missing image
  warned), the arrow-key navigation and the scrolling, and 3 mod campaigns (6 cards).
- **Flight styles** (`ModFlight`; the guide's "Flight styles"): mods choose per content whether they touch the original
  flight, free flight or both. Items: attributes 110-121 (`ItemStats`): `flightScope` (110: 0 both, 1 original, 2 free) keeps
  the item's booster / Steering Nozzle attributes 25-28 to one style; % bonuses `topSpeed` / `turnRate` / `strafeSpeed` (both,
  111-113), `original*` (114-116), `free*` + `freeAcceleration` (117-120) and `freeBoostFactor` (121, x100, instead of the
  booster's).
  Ships (new or override): `flightStyle` and `flight` { topSpeed, turnRate, strafeSpeed, acceleration, boostFactor, original {},
  free {} } (`ShipData.flightStyle` / `flightBoth` / `flightOriginal` / `flightFree`, non-serialized, set by
  `ModFlight.ApplyShip`). `Database.BuildFlightStats` fills `FlightStats.original` / `free` (`FlightTuning`: the style's
  booster, agility and summed `FlightBonus`; the ship's first, so an item's freeBoostFactor wins); `FlightModel.Configure`
  makes a `StyleState` per style and `FreeFlight` picks one (`Handling`, the booster, `TopSpeed`, the turn / strafe / ease
  factors; totals never below -90 %); the original scales its speed and boost by the top speed bonus, its target rate, ramp and
  decay by the turn bonus, its strafe by the strafe bonus (it has no acceleration). The style: `FlightStyles.Current` = the
  first of `EventFlight` (`/flightstyle [players] <free | original | player>`, NetAdmin.Order.Flight 30, the graphs' Set Flight
  Model node, undone at the event's end, re-applied when a quest resumes), the ship's, the mod campaign's, an on new-game
  option's (`"flightStyle"` in campaign.json / gameoptions.json), else `Settings.FlightStyle`; `ShipController.FreeFlightActive`,
  the HUD hints and the key layout (`GameControls.ApplyStyle`, polled by the flight and the bindings page) follow it, and the
  Options page names who sets it (`OptionsCatalog.FlightStyleForcedNote`). Expression `freeflight`. Verified on `FlightModel`
  in the Editor (the bonuses per style, a free-only booster, freeBoostFactor, a ship's JSON, the validation, a ship-forced
  style); not yet played with a real mod.
- **Conditions** (`Events.EventRunner.Condition(expression, out error)`: the event graphs' expression language outside an
  event, a throwaway run like the quests' "Starts when"; new words `option(mod:id)`, `won(main | valkyrie | supernova)`
  (or the multiplayer finished world), `systemsvisited` (distinct systems of `Session.VisitedStations`), `standing(race)`
  (2026-10-28: a game race's or a mod race's standing, `Standing.RawToward`; the always-hostile sides -100)): items.json /
  ships.json "available" (new entries and overrides; `ModUnlocks.ItemAvailable` in `Shop.GenerateItems` and
  `NpcTables.RollLoot`, `ShipAvailable` in `Shop.GenerateShips` and the lounge ship sellers), blueprints' "available"; an
  unreadable one is false with a warning on its mod. ships.json "dealer" { chance %, systemRace, minTechLevel }
  (`CustomShipData.dealer`, `ModUnlocks.AddDealerShips` at the end of `Shop.GenerateShipsOriginal`'s ordinary path: not the
  special yards).
- **Mod ships in traffic** (`ModTraffic`, Options > Gameplay "Mod ships in traffic", `Settings.ModShipTraffic`: Off / 10 /
  25 % (default) / 50 %, each player's own, not a session rule): `NpcTables.RandomFighter` / `StrongFighter` (every NPC
  fighter roll: locals, raiders, pirates, jumpers, escorts, guards, battle wings) take one of the mods' ships of the race
  (`ModTraffic.Pick`) at that chance; candidates: the mods that are on, `race` (or ships.json `traffic.races`; minor races
  count as pirates, no Specters), `ModUnlocks.ShipAvailable`, the model built (`ModShips.Built`) and at most 150 m long (its
  meshes along the nose, cached per `ModManager.Revision`), weighted by `traffic.weight` (`CustomTraffic`, 0 = never); a
  strong fighter one of the dearer half; a mod campaign's `trafficShips` for the race skip it (`ModCampaigns.HasTrafficShips`).
  The hull and gun come from `NpcTables` as before (only the look changes); freighters / capital ships / story ships keep
  theirs. Verified in Play mode (gof3_ships, 50 %): 37 of its 38 hulls qualify (the 159 m Bl'Arek doesn't), Dis's Terran
  fighters 4 of 5 GoF3 hulls with their GoF3 exhausts, the freighters untouched. Candidates are cached per race for 5 s
  (a scan cost ~2 ms a roll). A kit ship's NPCs fly random builds (`Traffic.Create` -> `ModTraffic.Assembly` ->
  `RandomBuild` -> `ModShipKits.VariantAssembly`, the variant in `NpcShip.ModelPath` for the proxies; `traffic.randomBuild`,
  default true): per slot a part the type may fit, a tier, a kit preset's colours (75 %) else a palette colour per slot, a
  skin 30 %; only from what is loaded (`ModShips.PartLoaded` / `BuildLoaded`: the tier steps down, then the skin goes, else
  the default build), so it assembles at once; `ModTraffic.Warm` (each `StationLevel.Awake`) starts 3 (phones 1) random
  full builds' parts loading through `ModShips.Warm` (`LoadOnDemand` quiet: no KitPartsLoaded / ModelsChanged of its own,
  unless a player build waited on the same files, `notifyPending`) until a kit has 150 (phones 40) part files loaded. A
  proxy whose build isn't loaded in that game shows the default build. The ES2 importer gives every ES2 ship
  `"traffic": { "races": [0, 1, 2, 3, 8, 9] }` (every race but the Specters). Verified in Play mode (es2_ships + gof3_ships,
  50 %): all six races roll ES2 ships (Void / pirates only those), 12 random Sentinel builds all different, four warm-ups
  taking its slot parts from ~7 to 15, a random-build Sentinel (Tormentor wings, body C) patrolling Dis, 2400 rolls in 57 ms.
  Boss hulls (2026-10-24, `CustomTraffic.boss`, ships.json `"traffic": { "boss": true }`): never an ordinary fighter
  (`Scan` keeps them to their own cache key); the pirate boss (`TrafficPlan.AddPirateEventShips`) flies one at the same
  chance (`ModTraffic.PickBoss` over `BossShips`), up to 400 m (`MaxBossLength`); its hull, gun and escorts stay the
  boss's, `npcTurrets` arm its turret mounts. The GoF3 Ships mod's five GoF3 bosses use it.
- **Mod capital ships and freighters** (remake, 2026-10-24, the user's request for GoF3's capital ships;
  `ModCapitalShips`, ships.json `"traffic": { "capital": "battleship" | "freighter" }`, `CustomTraffic.capital`): never
  fighters (`ModTraffic` skips them). A battleship parks in the orbit's capital ship special (`TrafficPlan`: Terran /
  Vossk systems in place of the game's battleship / Vossk battleship at the "Mod ships in traffic" chance,
  `PickCapital(race, true)`; Nivelian / Midorian systems, which have none, in 30 % of the orbits with freighters;
  `AddModCapital`: the Terran battleship's box, 4-8 km out, `ClearOfStation`); a freighter replaces one of the orbit's
  freighters at that chance (`PickFreighter`); nothing is drawn while the mods have none for the race (`Has`), so the
  game's own traffic rolls stay as they were. Both fly NpcShip's freighter path (`SpawnSpec.freighter`, `modCapital`; a
  battleship `stationary`, the Terran battleship's hull `Hull(1, 14)`, its name on the lock plate) with the mod ship's
  own model (`Traffic.Create`), its `npcTurrets` armed even parked (`NpcShip`: `!parked || modCapital`); collision boxes
  measured on the built model (`CollisionVolume.ShipVolumes` hook in `ForFreighter`, filled by `SpaceHooksInstaller`:
  `ModCapitalShips.HullVolumes`, the hull meshes cut into 8 slices along the longest axis, each slice's cross-section,
  `Measure`), so `NetProxy`'s freighter boxes match; no wreck (no `WreckPrefab`, `leavesWreck` false, empty volumes once
  dead), the explosion its `explosionScale` (`FreighterExplosionScale`, else 8 / 6). `ModShipBuilder` culls a mod ship
  at max(4 km, 25 x its size). Not for the capital ship enhancements (escorts, resupply, fleet battles). Tests
  `ModCapitalShipsTests`. Verified in Play mode: the GoF3 Nemesis (4.4 km) spawned at Dis with 8 boxes and 8 turrets. A
  ship's own `engineFx` takes each mount's `outline` like an exhaust style (`EngineFx.CreateShaped`,
  `ModExhausts.OutlineOf`: the cards reach the nozzle's shape, the trails along its long side, centred on it); the GoF3
  capitals get GoF3's large engine on their nozzles, shaped to them (see Mobile stations).
- **Capital ship subsystems** (remake, 2026-10-24, the user's request: EVERSPACE 2's warships and its Dreadnought
  encounter; `World.CapitalSubsystems`, ships.json `"subsystems"` parsed by `ModContent.ApplySubsystems` into
  `Database.ShipSubsystems`, `Data.CapitalSubsystem`): Traffic.Create attaches it to a `modCapital` spec with
  subsystems; its first Update spawns each part through `Traffic.SpawnShip` (capitalPart / capitalHost = the ship, its
  event tag, so a mission's `alive(name)` counts them) placed like a turret on a mount
  (`transform.TransformPoint(WeaponSystem.MountToLocal)`, `TurretTurn`) and parented to the hull: "turret"
  (turret_002_static with `turretItem`), "object" (a `fixedObject`: "gof2:<assembly>", a ship's assembly by ship
  reference, else `v_station_battlestation_shield`) or "hangar" (an object, or `"model": "none"`: a launch point on the
  hull, `Part.localPos`). Rules each frame: `shields` parts standing -> the hull's `Target.damageFactor` =
  `shieldedDamage` (Target.Damage multiplies every hit; a player's hit then "Its shields hold", rate-limited) and the
  renderers whose material name contains `shieldMaterial` shown; `stage` parts invulnerable while a lower stage stands;
  a `weakspot` part's `Target.forwardTo` / `forwardFactor` pass its hits on to the hull, its death takes `bonus` of the
  hull's maximum (through the shields); hangars launch `ships` (escorts looping round the hull,
  `CapitalShips.LoopAround`) while it is hostile and the player within 7.5 km. Hostile pirate (8) / Void (9) capitals
  (`ModCapitalShips.PickHostile` / `SpawnHostile`): with a pirate or Void raid at a third of the traffic chance, 3 km
  beyond the raiders' point, and in the Void's orbit. `/spawn` of a mod capital (`DebugSpawner.SpawnShip`) uses
  `ModCapitalShips.Spawn` (parked, subsystems) 1-2 hull lengths ahead. `hullFactor` scales a mod capital's hull;
  `engineStyle: "es2"` runs `Es2EngineVfx` without flame materials (no glow discs), `es2Engine` { glow, trails }
  (`CustomEs2Engine`, default on) switches its NPC glow / trails (and the player's veils) off as a blueprint's
  SpawnEngineGlow / SpawnEngineTrails do (EngineVFXEnabled false = both; the ES2 importer writes it: every ES2 warship
  and freighter but the Ancient Swarmer has them off, so their engines are their nozzles' glow); a parked mod
  battleship's engines idle at `ModCapitalShips.ParkedDrive` 0.6 (`EngineDrive.Idle` from its "ship_NNN_mod" model, also
  on the multiplayer copies; `EngineFlame`'s velocity thrust takes it as its floor): driven by their speed, a parked
  one's showed nothing; CustomShipMaterial `tiling` scales URP Lit's `_BaseMap` (EVERSPACE 2's plating Tiling). Tests in
  `ModCapitalShipsTests`. The ES2 Ships mod's importer (`tools/warships.py`, `weapons.py`, `encounter.py`, `es2mat.py`)
  builds ES2's ten warships and seven freighters from their blueprints (turrets, missile launchers, drone / hangar
  spawners, the frigate's weak spots), ES2's weapons as items on GoF2's DPS ladder with their own colours, sounds and
  models, the capital turrets on ES2's turret meshes (split by bone, textures embedded), and the Dreadnought (its stage
  pieces as one hull) with its encounter (the raids, below): fields / shield generator (stage 1, shields) with the
  torpedo launchers, sniper and lightning turrets (stage 2), explosive weak points (stage 3), flak / missile batteries
  and five hangars throughout.
  **Retreat** (the user's request, as in ES2: BP_Encounter_Dreadnought_Base's OnHealthDepleted -> StartEnemyJumpOut,
  BP_Dreadnought's SpawnJumpEffect; ships.json `"retreat": true`, `CustomShipData.retreat`, attached by Traffic.Create
  to a `modCapital`): `World.CapitalRetreat` takes `Target.SaveFromDeath`: at an empty hull it keeps 1, invulnerable,
  holds its fire, `CapitalSubsystems.Halt` (parts silent and unharmable, no launches, the hangars' fighters `JumpOut`),
  "{0} is jumping away!" (Extra `capitalRetreats`) and sound 33; after 4 s `SystemJump.SpawnKhadorFx` a ship length
  ahead of its centre, turned with its model's bow (as the player's jump), scaled by its length over 70 m
  (`ModTraffic.HullLength`; by its widest extent the Dreadnought's flash filled the screen white from 2.5 km), sound 32,
  the ship pushed forward into it (x1.1 a 30 fps frame), gone at 1700 ms (`LeaveWithHost` + `NpcShip.Vanish`,
  `killedByNpc`: no wreck, crate or kill). The other players only see its copy go (no fx there yet). **Mod gameplay
  settings** (gameoptions.json `"setting": true`, `ModGameOptions.Def.setting`): no new-game card (`Cards()`), an
  Options > Gameplay toggle (`OptionsCatalog`, id "mod:<key>", only while the mod is on, `GameplaySettings()`) in
  PlayerPrefs `modsetting_<key>` (`Setting` / `SetSetting`; reset by Default settings, `ResetSettings`), which
  `option(...)` reads (`IsOn`). The importer's encounter is now the **Dreadnought raids** (the user's request; its
  setting `es2_ships:dreadnought_raids`, on by default): at rank 5+, a 1 in 300 chance each second, a random station:
  Brent Snocom calls (a dialog), the station is the objective's target (map and HUD), a checkpoint keeps it, the quest
  waits until the player is in that orbit, then two ES2 freighters (the convoy, friendly, held 4-5 km short of it by a
  `shipaction hold`: in its turrets' reach one died within a minute) and the Dreadnought spawn (a guard: no Dreadnought
  30 s on = the run stops, nothing won); driven off it pays 150 000 and 10 legendary swarm missiles; it comes again 90
  minutes of play later. Brent's lines follow his own in the story ("Keith, this is Brent!", "Dammit", "I transferred
  ... to your account"). Played in the Editor: the call, the map marker, the spawns, the convoy holding, the retreat,
  the reward and the cooldown. Fixed on the way: `NetGame.Db` (the event commands' look-ups) was loaded once, in single
  player before the mods were on, so every mission / event command naming mod content (spawns, rewards) failed; it
  reloads when `ModManager.Revision` changes. Conversation pages and radio lines are cut at `NetChat.MaxPageLength` 600
  (were the chat's 160: a call broke off mid-word), a conversation at `MaxConversation` 4000. A queued conversation
  waits for the star map to close (`EventScreen`, `SpaceHooks.StarMapOpen`); showing it over the map
  (`DialogueView.HostIn`, 2026-10-24) was taken out again on 2026-10-25 at the user's request. ES2's Mobile Homebase is
  no warship but a mobile station like the GoF3 mod's Manticore (`warships.build_homebase`: stations.json, route "any",
  30 minutes an orbit, two hours a system), docked into its own hangar and casino (`homebase_hangar.py`: ES2's carrier
  level POI_HomebaseCarrier_001 assembled into one interiors.json room x2.8, its six landing pads the pad and parked
  slots, a side force field's opening the flights' gate, the casino behind the hall as the bar; the structure colours as
  ES2's shader computes them, saturate(2 x C x vertex-colour tint), read from its compiled shader). Fixed on the way: a
  station's stations.json `materials`, and an interiors.json room's, ignored their `material` filter
  (`ModStations.Build` / `ModInteriors.Build` passed no glTF material name to `ModShipBuilder.MaterialFor`), so such a
  station showed its model's own white materials.
- **Turrets don't fire through their own ship** (remake, 2026-10-24, the user's request, for every turret):
  `TurretCore.Host` (the ship it sits on: NpcShip's turret objects their capital host (`Spec.capitalHost`) or the ship
  they ride on, `NpcMountedTurrets` / `PlayerTurret` their ship) and `TurretCore.Fire` checks, once the gun is ready, a
  ray from the gun's pivot along the barrel against that ship's hull (`HullCollision.RayHits`: its own mesh colliders,
  past the turret's own meshes; built in every collision mode but Original), else its collision volumes
  (`Obstacle.RayInto`: entering another volume, or heading inward through the face of the one it starts in); blocked =
  no shot, `TurretCore.Blocked`, and the owner drops that target like an unreachable one (`TurretTargeting.LimitHit`).
  Review of #168: `Blocked` is worked out when asked (once a frame, again after `StepToward`), the owners asking it only
  once aligned; it used to be set only by a ready `Fire`, which `PlayerTurret` / `NpcMountedTurrets` stopped calling once
  it was set, so an auto turret blocked once never fired again. `RayHits` asks the hull's own colliders
  (`Collider.Raycast`), not the shared 64-hit buffer that other hulls could fill. Test `TurretOwnHullTests`.
  Shots pass their own side's hulls, so a turret on a capital ship's far side hit the player straight through it.
  Checked in Play mode: 141 shots of a hostile Dreadnought's turrets with the player under it, none through the hull.
  Test `ModCapitalShipsTests.ATurretDoesntFireIntoItsOwnHullsVolumes`.
- **Mobile stations** (remake, 2026-10-24, the user's request: GoF3's Manticore as a moving station with its own hangar;
  stations.json `"mobile": true | { "route", "stayMinutes", "systemMinutes", "exclude", "speed", "race", "engines",
  "engineColor", "engineFx" }`, `ModWorld.MobileSpec` / `IsMobile` / `Mobiles`; `MobileStations` (Game layer, plain C#);
  `World.MobileStation`): a new station with no planet (not added to its system's `stations`, so not on the map, not in
  the 7, never a random target). Its route (any systems' stations, default every station of its own system; `"any"`:
  every system, the user's request for the Manticore: `systemMinutes` in one, its orbits there in turn, then another,
  every system once per round in an order drawn from the station and the round (`RoamSystemAt`, never the same twice in
  a row), never `MobileStations.RoamSkip`'s story / Kaamo orbits; `exclude` systems) is walked by the UTC clock
  (`MobileStations.Clock`, `ClockOverride` for tests; the same for every player, it moves on while the game is closed):
  stay n = seconds / stay, the (n + station) % count-th stop; in that orbit a straight course crossed sideways
  (`CourseAt`: a JavaRandom of station and n, the middle 6-8 km out within 50 deg of the station's front, heading square
  to it, so the hull never sweeps the station). `SpaceLevel` spawns those in its orbit (`MobileStation.SpawnAll`, not in
  an arena or the prologue): the model (its ModStations template, else looksLike), an `Obstacle` turning with it
  (`rotates`; `ModCapitalShips.Measure` in its own frame), `HullCollision`, moving at its speed in game time; Navigation
  gets a Station target with its transform (`AddMobileStation`, `Target.techLevel` for its marker, `GoingToMobile`;
  `GoingToStation` now only the orbit's own); `CanDock` / `Dock` / the autopilot dock at the one in range (bounding
  radius + 6000 units, after the player left it once: `MobileStation.LeftRange`; `DockAt`: `Session.StationIndex` = the
  mobile station, the Station scene with its own hangar / bar). A level loaded with a mobile station's index (a launch
  from it, a teleport) is its current host orbit (`SpaceLevel.Awake`, `launchFromMobile`) and the player comes out
  beside it (`LaunchPose`: its right, along its course). Each `Database.Load` gives it the system of the orbit it is in
  (`ModWorld.Apply`: `StationData.system` = the host's, so the docked header, prices and star map follow it; a scene
  keeps the one it loaded), while its race stays `MobileSpec.race` ("race", else its interior's, else its home system's;
  `OrbitLayout.Build`); its title is its name alone (`StationMenu`, `MobileStation.Label`). The star map marks it
  (`StarMap.MobileMark`: amber, after the system's name and under its orbit's planet with the minutes until it leaves
  the system, `MobileStations.LeavesSystemIn`); its `engines` (exhaust mounts from its centre) get a mod ship's glow
  (`ModShipBuilder.BuildEngineGlow`), or with `"engineFx"` (ships.json's, `CustomEngineFx`) Galaxy on Fire 3's engine
  effect instead (`EngineFx.CreateShaped`, each engine by its `fxScale` and its `outline`, run at
  `MobileStation.CruiseDrive` 0.8 through an `EnginePreview`: its 0.15 u/ms measured as idle; the textures preloaded
  with the station's, `ModStations.TexturesOf`). The GoF3 Ships mod gives it and the nine capital ships GoF3's capital
  engine (`vfx/engines/large`, the importer's `capitals.large_engine_fx`: the glowing nozzle body as the cone, its taper
  drawn into the cards' picture, the hot core as one straight trail, the flat flare as the glow's streak (1.6 x GoF3's
  quad and brighter: our texture fades toward its ends), in the race's colours) on their engine helpers at the helpers'
  scale, each shaped to its nozzle (the importer's `nozzle_outline`: slices across the nozzle, rays from the axis to its
  walls, the aftmost closed slice's convex hull, of three readings the one nearest GoF3's own size; the mount's
  `outline`); before, plain discs twice the nozzle's size floated off the nozzles (the user's report), and the Hades'
  hull sat 86 GoF3 units above its own engine helpers (the importer's scene reader took each node's parent from the
  record before: fixed in `ae4_scene`). Tests `ModCapitalShipsTests`. Verified in Play mode (the GoF3 Ships mod's
  Manticore, 6.3 km, Magnetar): found in Dis's orbit cruising nose first, docked into its hangar, launched back beside
  it.
- **EVERSPACE 2 docking** (remake, 2026-10-25, the user's request: ES2's stations and the Mobile Homebase dock as ES2
  does; `World.Es2Dock`, interiors.json hangar `"dock": "es2"`, `ModInteriors.Es2DockOf`): the hangar room is the
  station's own model (same GLB, the room's `scale` = the station's), so its markers map into the orbit through the
  station's `hull` (`W(m) = hull.TransformPoint(m / scale)`). From BP_DockableStation's decompiled blueprint and its
  timeline curves (`Es2DockCurves`, UE cubic keys): the dock's marker is the station landmark (`DockPoint`, the Interact
  box's centre); Dock works inside that box (`InZone`, TriggerBoxExtent 7000 x 7000 x 3000 cm, 35 m up) instead of
  GoF2's range; docking cuts to the dock's CineCamera (spring arm 35 m, pitch -15 yaw 160, 23 deg up a 16:9 screen)
  looking straight at the ship + its look offset while ShipDocking (5 s) moves it TLerp(start, DockingTransform,
  TransformAlpha) (landed at 4 s; the ship's bounds centre on it), banking wobble BankingAlpha x 30 x
  InitialDistanceFactor, the SteadyCam sway (1 deg at 5.19 / 5.88 / 6.95 rad/s x factor x GlobalAlpha); take-off (3 s):
  climb TakeOffHeight turning to the take-off point (Align), then out to TakeOffTransform (Launch, accelerating),
  banking x 10, the camera eased toward the ship, at 1 s the boost and a 3 s ease-in-out (exp 4) view blend to the chase
  camera (Es2Dock restores the chase camera's own pose each Update so its state stays its own); `dockSound` /
  `undockSound` (the mod's ES2 S_SC_Docking / Undocking). **Docked inside the orbit** (the user's choice: no separate
  space): when the ship lands, `SpaceLevel.DockInOrbit` loads the Station scene additively; the orbit lives on (traffic,
  sky), the ship held on the pad (`Es2Dock.Docked`, a mobile station carries it), its model hidden, the HUD hidden
  (`FlightHud`), flight input halted (`SpaceHooks.DockedInOrbit` in `Navigation.InputHalted`), looping sounds paused,
  invulnerable, the ship state stored (`PlayerHealth.StoreToSession`). StationLevel's orbit mode (`DockedInOrbit`): no
  room, its camera / volumes / key light off, the space camera driven instead, `hangarRoot` parented under the station's
  hull (scale 1 / room scale) so every hangar place is room metres (`SpawnShip` local); Launch =
  `SpaceLevel.UndockFromOrbit` (unload, `SwapPlayerShip` + `ReloadFromSession` for what the station changed, the
  take-off; a story launch still reloads). The Station scene loaded on its own for such a station (a save, a story move)
  redirects to Space with `Session.Es2StartDocked` (docked on the pad, then additive); `Session.Es2NoRedirect` when no
  dock could be found. Mobile stations: a dock each (`mobileDocks`, their landmark the dock point), `ActiveEs2` the one
  in use. stations.json `"bar": "none"` (`ModWorld.NoBar`): no Space Lounge (`Story.LoungeUnlocked`, the button hidden):
  ES2's stations, the Homebase, GoF3's Manticore. A roaming mobile station's `"systems"`: only those (the Homebase:
  ES2's sectors). Tests `Es2DockTests`. Verified in Play mode: docking and take-off at Union Bridge and at the Mobile
  Homebase (into its hall, out through its open end), the station menu over the live orbit, Launch without a reload, the
  sounds. While docked so the Station scene is loaded beside the Space scene, whose root "EventSystem" is switched off
  (`SpaceLevel.DockInOrbit`, back on in `UndockFromOrbit`; GoF2.Space can't name UGUI's type): two flooded the console
  with "There are 2 event systems in the scene". **Lights and landing dust** (BP_DockableStation's SpotLight_Top / _Fill
  and P_Vehicle_LandingDust, from its component defaults and the dust's Cascade emitter): from the docking's start until
  the take-off ends, a white spot 30 ES2 m above the pad pointing down and a cyan (77D2FF) one on a pivot turned with
  the camera (5 m ahead, 20 m right, 5 m below the pad, aimed back at it), cones 45 deg, reach 50 m; UE's legacy falloff
  (1 - (d / R)^2)^2 isn't URP's inverse square, so each frame their URP intensity is set to give the ship UE's falloff
  at its distance (`Es2Dock.Intensity`, x `LightScale` 0.005 per unitless UE unit: 100 top, 30 fill; matched by eye at
  Union Bridge). The dust from the docking's start to 4 s: a trace 50 m down the ship's banked up (the hull colliders,
  else the dock's plane), Amount 1 - distance / 50 m; 200 x Amount a second, 0.5-1 s, on a 5 m circle in the dock's
  plane flying out at 3-4 x it a second + the ship's lean across the plane x -50 m/s, rising 10 m/s^2, 5-10 m sprites of
  a random frame of T_Smoke_001_M's 6 x 6 flipbook blended to the next (its green = opacity), grey 0.2 lit without
  direction, alpha 0 -> 0.1 -> 0 x Amount; drawn by `GoF2/ModEs2Trail` kind 11 (Landing dust); interiors.json `"dust"`
  names the flipbook (the ES2 importer writes `textures/es2_landing_dust.png`). Seen at Okrot: a faint haze over the
  pad, as ES2's own values give. Not done: multiplayer (other players' view of a docked ship, NetHangar).
  **Engine effects after ES2 launch** (2026-10-28): the hangar and lounge visual roots carry
  `Visuals.StationModelContext`. `EngineDrive.IsStationModel` checks a model's ancestors; EngineFlame, Es2EngineVfx,
  LateralThrusters, EngineFx and ThrottleGlow use it in OnEnable. Looking for any loaded StationLevel classified the
  replacement flight model as a hangar ship while the additive Station scene was unloading, capping its drive and hiding
  boost effects. Hangar models keep their context when carried under the orbit's station hull; other orbit ships retain
  flight effects. Tests `EngineStationContextTests` cover flight inputs, hangar previews and all five effect components.
- **Mod blueprints** (`ModBlueprints`, a mod's `blueprints.json`, parsed with the rest in `ModContent.Parse`): a "ship" or
  "item" product, ingredients, requiresShip (a skin: built only while flying that ship, which it rebuilds, the Kaamo upgrades
  kept), autocomplete (true / false / a price: `Blueprints.AutoCompletePrice` / `CanAutocomplete`, the hangar hides the row),
  available, unlocked (known once available, `UnlockAvailable` at `Shop.EnterStation`), and three sources while available and
  not known (`Offerable`): "lounge" (`AgentGenerator.AddModBlueprintSellers`, a local visitor with offer SellBlueprint, the
  openers 874 / 875 in `LoungeChat.StoryLine`; "stations" pins it to those bars like the game's own sellers, chance 100,
  `LoungeAllowedAt`), "derelict" (`TrafficPlan.Build`: one hackable freighter wreck per visit at
  most, `TrafficPlan.HackableWreck` + `SpawnSpec.modBlueprint`; `Traffic.OnHackWon` leaves a data crate; "stations" limits it
  to those orbits, `RollDerelict(station)` / `DerelictAllowedAt`, the chance then defaulting to 100, a list of unknown ones =
  nowhere; "position" [x, y, z] game units in the orbit, else out in front) and "drops" per race or per ship type (`{
  "ship", "chance" }`, `Def.shipDrops`, rolled first: a boss hull's own blueprint) (`NpcShip.OnDied`, a kill by the
  player: `RollDrop`, a data crate). A blueprint is obtained once (2026-10-24, the user's rule): a seller standing in a
  bar since before it was learnt has 858 (`LoungeChat.Start`), a derelict hacked after leaves no crate and no data crate
  is made while one with it floats (`Crate.HoldsAny`, `NpcShip.OnDied`, `Traffic.OnHackWon`); production stays
  repeatable. A data crate is a `Crate` (look 3, the junk container) holding the product item;
  `CombatRadar.Capture` learns a mod blueprint's product instead of loading it ("Blueprint found: X"). An item product gets the
  recipe on its `ItemData.blueprint`; a ship product is a hidden "blueprint item" ("<mod>:blueprint:<id>", numbered like a mod
  item in `ModContent.EnsureMapping`, made in `Apply` (`Deed`), its price the ship's after `ApplyShips`; `ModContent.ItemText`
  / `ItemInfo.ItemIcon` / `ItemInfo.Category` show it as the ship, "Ship blueprint"), so the Blueprints tab, the saves and the
  crates need nothing new. A finished ship never enters the hold: `Blueprints.Produce` leaves it pending at the production
  station, `Hangar.DeliverBuiltShips` (the hangar's Finish there, `StationMenu.CheckPendingProducts` on docking) swaps it in
  like a purchase: the old hull traded in at its price into the dealer list, or parked in the Kaamo Club when owned and free of
  that type; `level.ReplacePlayerShip`. The details show the ship's stats (`HangarWindow.ShowBlueprintDetails`), "Let me see
  it" the ship (`LoungePanel.ShowGoods`). Verified in Play mode with a test mod: the tab and details, autocomplete and the
  delivery (trade-in credited, gear moved), the skin's refusal and rebuild, the lounge seller's offer, a derelict hacked into a
  data crate, a pirate kill's drop, the capture unlocking it.
- **Dependencies** (`ModManifest.dependencies`, "id" or "id>=version" → `dependencyVersions`, `VersionAtLeast`): `Active`
  drops a mod whose dependency is missing, broken, off or too old (`InactiveReason` says which); `SetEnabled` on turns the
  dependencies on (recursively), off turns the mods that need it off (`NeededBy`); the browser lists each dependency's state
  and "Needed by". Another mod's content by key "mod:id": items.json / ships.json `base` / `override`, systems / stations
  refs, and the event graphs' names (`NetAdmin.FindShip` / `FindItem` / `FindHull`, `NetTeleport.ParseStation`).
- **Quests and bar missions** (event graphs in a mod's `events/*.gof2event`, found by `EventRunner.Load` / `List` after the
  Events folders, the later mod in the load order winning; then the built-ins): see "Event graph quests" in Multiplayer.
- **Mod browser** (`ModBrowser`, the main menu's Mods button and `modsPanel`; styles `.mods-*` in MainMenu.uss): the list in
  load order (thumbnail, name, version · author, On / Off / Error / Can't load), a click or keyboard / controller focus
  selecting a row (the mouse's hover focus doesn't: passing over the rows on the way to Turn on changed the mod it acted
  on), the selected mod's preview, name, meta,
  description and credits (mod.json `credits`), where it is installed, errors and warnings, Turn on / off (also the
  controller's X from anywhere in the panel, `ToggleSelected`, its glyph on the button with a controller,
  `.mods-pad-x`), Earlier / Later, Open mods folder (the selected
  mod's folder, else `ModManager.MainFolder`: the user folder, the project's Mods in the Editor; desktop only, phones show
  the path), Refresh, Rebuild cache (`MainMenu.RebuildModCache`: `ModTextureCache.ClearAll` deletes `ModCache` (textures,
  hangar shadows) and the zip mods' unpacked files, then a rescan loads every mod again behind the fade with the loading screen).
  Mods change only in the menu: the tables are rebuilt when a game starts or loads. **Import mod** / **Delete** (`ModImport`;
  players couldn't reach the Android Mods folder): the system's file picker copies the picked file into
  temporaryCachePath/ModImport (Android: `ModImportActivity`, `Assets/Plugins/Android/ModImportActivity.java`, a see-through
  activity around ACTION_OPEN_DOCUMENT that copies on a worker thread and is polled through its static fields, registered
  in the unityLibrary manifest by the Editor's `AndroidModImportActivity`; Windows: `FileDialog.Open(title, filter, exts)`;
  Linux: zenity, else kdialog, on a worker thread; macOS: osascript's choose file; UWP: `FileOpenPicker` on the UI thread;
  the Editor: its open panel; iOS: the document picker, `Plugins/iOS/FileImportPicker.mm`, which moves the system's
  "asCopy" copy out of tmp/<bundle id>-Inbox (copied, it stayed there as a second copy); `ModImport.Available`),
  `Inspect` reads it as a zip mod (mod.json, a valid id),
  `Install` moves it into `ModManager.MainFolder` as `<id>.zip` (a mod with that id: replaced in its own folder
  after the menu's Yes / No, keeping on / off and its load order); Delete asks (naming the mods that need it), turns it off,
  releases its zip and deletes the file or folder. The browser takes the menu's `ShowDialog` / `ShowNotice`. Verified in the
  Editor (a non-zip refused, a zip with a top folder installed, replaced by a newer zip and kept on, deleted through the
  dialog) and compiled for Android / Windows / Linux / UWP; on iOS (the iPhone 18 Pro simulator, 2026-10-17) the private
  es2_ships zip (120 MB, 60 .ogg sounds) picked from On My iPhone, installed, turned on and loaded (the sounds without a
  warning: runtime Ogg Vorbis works on iOS; the textures cached as ETC2, the simulator offers no DXT) and its Sentinel
  flown; the browser names the folder as the Files app shows it on iOS (`ModBrowser.Shown`: "Files › On My iPhone › GoF2
  Remake › Mods", not the sandbox path); not yet run on an Android phone, Linux, macOS or UWP.
- **Multiplayer** (`NetMods`): the Host card's "Mods (N on)" Off / Allowed (`mp_allow_mods`, off by default; a dedicated
  server's `-allowmods` / `ALLOWMODS=1` = every usable mod in its Mods folders). Every session starts without mods
  (`PrepareSession` → `BeginClient`); the host's `BeginHost` sets `ModManager.BeginSession` and `NetMods.SessionList`
  ("id@version#hash;..."), NetState carries it (`sessionMods`, FixedString4096) and a joining game turns exactly those on
  (`ApplyFromServer`) before `EnterWorld`. The connection data's 4th line lists the joiner's installed mods ("id#hash;...");
  `NetMods.Refusal` turns away a game lacking one (same files, on or off doesn't matter) with their names. "Same files" is
  `ModInfo.NetHash` (`NetSignature` in the session list): every file but those mod.json's `multiplayerIgnore` lists
  (`ModManifest.MultiplayerIgnores`: "folder/" or `*` patterns; never root `.json` files or `events/`), and mod.json only as its
  id / version / dependencies / that list, so a phone build with decimated models and halved textures plays with the PC build
  (the es2_ships importer writes `parts/`, `maps/`, `icons/`, `preview.png`); `ModInfo.Hash` (every file) stays the saves' signature. Lobby keys `mods` /
  `modnames`: the browser tags such games "Modded", shows the mods and "needs mods: ..." when this game lacks some (not
  joinable). `NetGame.OnMainMenu` → `NetMods.End` (single player's mods back); `NetGame.ResetDb` on every change.
- Verified in Play mode (ships): the three PR mods load without warnings; every ship on the turntable and the Falcon in flight
  (engine band, two turrets with the auto one under the hull, the turret crosshair); four lounge sellers at Var Hastra, the
  Falcon bought ("Okay.", trade-in); a Windows development build joining an Editor host with mods allowed: the session's
  four mods turned on, the five models built in the IL2CPP build, the host's Falcon parked in the client's hangar and seen in
  flight; the item window's 3D view. Not yet: a client refused for missing mods in a build (the check itself is verified).
- Verified in Play mode: the browser (folder, zip, broken mod with its line, toggling), the example's items in Var Hastra's shop
  with their texts / stats / icon, bought, mounted and fired with the N'saan's fx, a save with the mod turned off (the warning,
  removal, refund, placeholder), hosting with mods allowed (the session list, NetState, the join check both ways) and off (the
  original tables), the Host card row. Not yet: the server browser's Modded tag against a real listed game, a client build.
