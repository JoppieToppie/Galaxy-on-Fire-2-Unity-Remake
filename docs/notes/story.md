# Story

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Story (campaign, dialogue, radio)

Research: `Reference/research/campaign_flow.md` (the step table 0-162, completion rules, who advances),
`campaign_levels_a/b/c.md` (what each story orbit spawns and scripts), `levelscript_cutscenes.md` (LevelScript
cutscenes), `dialogue_cutscenes.md` (dialogue window, portraits, radio, voice), `freelance_missions.md` (bar agents,
side missions; not built yet). Data: `Resources/GoF2Data/story.json` from `Reference/tools/campaign/build_story_json.py`
(per index: mission type / target / reward / value / goods / visible, objective text, briefing + success pages with
speaker and voice, radio lines; speakers' portrait descriptors and part offsets); `Resources/GoF2Story/StoryAssets`
("GoF2 > Build > Story Assets": voice clips English + German, portrait parts with their image heights from
`Textures/_texture_manifest.json`).

- **State and rules** (`Story`, plain C#; state in `Session`, saved by `SaveGame` v2): `CampaignMission` = the index,
  `StoryMission` = slot 0. `IsComplete` = `Status::missionCompleted` per type (dock at target, cargo load, reach orbit
  10 s, weapon + armor mounted (sort 10 = armor), lounge, counters...; level types are decided by the campaign level).
  `Advance` = `nextCampaignMission` (52 → 54, 128 → 130; the new step's mission + side effects, main campaign only so
  far). `IsLevelMission` = `departStation` (which orbit is a story orbit), `BlocksDocking` / `BlocksJumps` (525 "Not
  possible on a mission."), planet jumps from index 10, menu unlocks (Hangar 5, Map 9, Lounge 13: OnKeyPress refuses it
  before 13, a player's report), checkpoint repairs on load (25 → 24, 29 → 28, 41 → 39; remake: 79 → 78, a save the old
  Khador advance stranded, #49). Story items are unsaleable and can't be demounted (323).
- **Start**: a new game starts the story (`StartCampaign`): main game = the prologue in flight (index 0, Phantom at Var
  Hastra's orbit), then the rescue (1), then docked at Var Hastra (the Phantom becomes Betty with Gunant's Drill + Telta
  Quickscan when docking at index 1); Valkyrie = 45 advances, Inflict at Dima; Supernova = 84 advances, Berger CrossXT
  at Dis. `Session.FreePlay` (remake-only, old saves) keeps the index-20 free play with the Mido help rules.
- **Docked** (`StationMenu`): while no window is open a completed mission opens its success conversation; closing it
  advances, then reloads the station (9, 44, 75, 76, 83), launches into a story orbit (78, 89, 99, 109, 119, 133, 144,
  160) or credits the reward, and autosaves.
- **In flight** (`StorySpace` on the level, `CampaignLevel` in a story orbit): story orbits get no normal traffic; the
  briefing after the launch camera (restarts the mission clock), success checks from 5 s (in-space rules or the level's
  win objective), failure → "Mission failed!" / "Game Over" → last save, the add-on entry calls at 45 / 84. The game
  pauses during conversations (`Navigation.Paused`). The story icon (`Story.MapMarks`, map and radar) has the original's
  special cases: 116 the bars not searched yet, 120 Maissa only (not the level's orbit, Valadon), 125 the freighter
  stations not scanned yet, none at 52 / 128 / 130 / 148-151. The player route shows its current waypoint as a lockable
  "Waypoint" target (548, story icon `map_story`; 543 / 544 when reached); the campaign target's planet gets the story
  icon.
- **Success pause** (remake, `StorySpace.SuccessPending`): an in-flight success conversation opens 1.5 s after the mission
  completes (the original: the same frame, `MGame::successCheck`, while e.g. the mined asteroid is still bursting); meanwhile
  the mission counts as won and the player is invulnerable.
- **A step won without its conversation** (#88, `Story.ReopenUnshownSuccess` from `SpaceLevel` / `StationLevel.Awake`): a
  mission still marked won when a scene starts never had its success conversation (it runs within one scene and advances
  the step), so it is opened again and completes anew; step 72's call won, the scene changed in the 1.5 s before its
  conversation, and the save kept the step won for good (`IsComplete` skips a won mission).
- **Dialogue** (`DialogueView`, `UI/Dialogue`, a template instance in the station and flight UIs): speaker header,
  portrait (`Portrait`: background, parts 2-1-0-3 at native canvas size, frame; Keith mirrored), scrolling text, Back /
  Skip (confirm 396) / Next-Close; voice per page, auto-advance after the line when voice is on; one-page notes with OK.
  Menus pause it: the flight pause menu
pauses the HUD's shared voice source (it ignores the listener pause for the conversations that pause the game, so the
radio's line kept talking), the station's system menu over a conversation sets `DialogueView.Paused` (voice, typing, input
and auto-advance wait; the menu takes the keys). The Void (19) and Corny (56) talk in the **alien font** there and in
the radio (`AlienText`, `StoryTable.UsesAlienFont`): the 26 magenta glyphs A..Z of `gof2_interface_iphone4.png` (y 263,
cut to `GoF2Hud/alien_A..Z` by Build HUD Images; order, 2 px gaps and 12 px spaces assumed: the .aei glyph table wasn't
converted) as image rows next to the hidden Label, wrapped at spaces; the radio counts 30 alien characters per line.
Keyboard Enter/Space/→ next, Backspace/← back, Esc skip; controller A / B / Y, X the AUTO toggle; with a controller the
buttons carry their glyphs (`InputGlyph.WithGlyphs`: the text moves into a child Label, a Button measures its own text
only while it has no children; remake, nothing showed that Y skips). **AUTO** (remake, Genshin-like, the header's pill,
the same setting as Options > Gameplay "Auto-play dialogue", `Settings.AutoAdvanceDialogue`, on by default): on, a
voiced page turns when its line has ended + 500 ms (the original's `DialogueWindow::update`), a page without voice 1.5 s
+ 45 ms per character (2.5..12 s) after it has typed in; never past the last page. **Animated dialogue** (remake option,
`Settings.AnimatedDialogue`, `TextReveal`; off = the plain page at once): the page types in (each letter fades in and
settles from a bright tint; the rest is laid out invisibly so nothing reflows; a settled plain letter takes no colour
tag, the label's own colour: a note opened as the station loads rendered before the label's style resolved, opaque
black, and every letter came out black, a player's report 2026-10-24), 55 letters/s or paced to the voice line (ends at
85 % of it), pauses after , . ? ! and a hesitation after "...", "!" sentences faster; all-caps shouts (4+ letters or 2+
before "!", not EMP / HUD / AMR) 130 % bold with a short shake (`<voffset>`); `*Sigh*` actions italic and dim without
the asterisks, typed slowly; names tinted: people pale gold with their titles ("Lieutenant Commander Brent Snocom"),
places / factions light aqua with "Station" / "System", ships / items / story goods pink (a one-word item only when the
texts never write it in lower case: not Drugs (Gold is never written in lower case, so it is tinted); names of 2+ words
in any case: "Khador drive"), each race in its emblem's colour (Terran orange, Vossk green, Nivelian blue, Midorian
grey, pirates bone white, Void violet; in either case except "void"), also the factions named after it ("Vossk Empire");
plurals too; inline icons (`<sprite ... tint=1>`, white with the letter's alpha so they fade in; tint=1 is needed, plain
sprites ignore the text alpha): a coin before every amount ("N$", "N credits" and each language's credit word), before
the first mention on the page the race emblem, the jumpgate / wormhole / blueprint / autopilot icon (text ids 547 / 545
/ 271 / 571), an item's / ship's shop icon, an equipment category's icon before its word ("tractor beam", "scanner",
"missiles"; "mine" only as "mines"), the ore core before "core" and the container before "container" (English; "plasma
array", "core generator", "core of" get none); only icons the atlas has (`TextReveal.HasIcon`: the original items' and
ships' shop icons, so a mod ship gets none and a mod item its base's; a sprite name TextCore lacks prints as its tag: a
mod's ship blueprint item, named as its ship, gave "item_279" before Betty), the rules rebuilt when the mods change
(`ModManager.Revision`) and kept in the order added for words of one length; sources: the speakers, stations, systems,
races, ships, items, in multiplayer the session's pilot names (a server `/dialog`'s %player%), and
`TextReveal.LoreNames` (the English lore names only the texts use, from an audit of every dialogue text; most factions
are translated in the other languages, so those only tint in English); a conversation ignores its keys, buttons and taps
for its first 0.8 s (`DialogueView.InGrace`: keys still pressed from drilling or firing turned step 4's pirate warning
away unread); Next or a tap first shows the whole page, pages read before show at once, a new speaker's portrait fades
in, long pages scroll along, Next breathes when done. Used by every place characters talk: the dialogue window (story,
freelance / agent notes), the flight radio and chatter, the lounge chat (the agent's generated name tinted too) and the
ending's radio; not the hints, ticker or item texts, nor the pages that aren't a character talking
(`StoryTable.IsNarration`: 16 Info, the tutorial pages; 17 Story, the narrator, types in). Checked against all 11 text
tables (Hindi and Chinese came later): Russian marks actions as `<шепотом>` (treated like `*…*`), CJK pauses on 。！？、
without spaces, Arabic / Hebrew and Indic (Hindi's Devanagari) pages fade in whole (per-letter tags would split the
letter joining and the conjuncts / reordered vowel signs, `TextReveal.IsShapedScript`), the Chinese credit word without
a space ("20,000个信用分"), all-caps words of item / ship / station / system names ("Micro Gun MKII") and EMP / PEM / IEM /
ЭМИ never shout; alien-font glyphs fade in order.
- **Radio** (`Radio` + the flight HUD's radio box): every line's trigger tested every frame and latched once true
  (`Radio::update`, `RadioMessage::triggered` +0x20; the original shows a new line at once over the one showing, the remake
  queues it; #132: tested only with no line showing, a one-frame trigger such as 105's event 10 was lost; `RadioTests`),
  one at a time, 2 s delay, `lines·2000 + 1500` ms, voice on appearance; blocks fast-forward. Trigger types: time,
  chain, ship deaths / activity / hull, route, event, station lock, armor.
- **Cutscenes** (`IntroCutscenes`, `CutsceneCamera`; `levelscript_cutscenes.md`): LevelScript look-at camera with world
  dolly and rumble, fades, the level's own music / loops, HUD and controls off (`SpaceLevel.Cutscene`); no locks or lock
  sounds then, nor in the launch / arrival fly-in (`Navigation.RadarHidden`: `MGame::OnRender2D` skips `Radar::draw`; a
  player's report). Index 0, the prologue: no station (`inEmptyOrbit`), asteroid belt at the origin, the sky's nebula 3
  (`skybox_003`) under the orbit's stars and sky rotation, the orbit planet `planet_001_big` at half size
  (`StarSystem::StarSystem`: Level type 3 + mission 0); the Phantom (unkillable) meets three pirates (2 Hiro, 23 Azov, 2
  Hiro, HP 150; exhausts and engine sound off until they wake; remake: no asteroid reaches within 150 m of their three
  spots, `IntroCutscenes.ClearPirateBubble`, so none sits inside them), the fight (the steering briefing opens when
  control is given), the hyperdrive failure (sounds 157 / 158 / 161), the time jump (`hyper_drive` fx, 160, music 141 =
  TimeShift_Start), arrival under the system's own sky (nebula 9, `Level::switchSkyboxForIntro`), asteroids gone, the
  orbit planet `planet_000_big` (`switchPlanetForIntro`'s x2 never shows: `StarSystem::render` resets the orbit planet's
  scale every frame), the tumbling wreck (156) trailing smoke and fire (`ShipSmoke`; the drift along game -z, the model
  alone turning by `PlayerEgo::rotate`'s stored Euler angles from (pi/4, pi/4, pi/4), `IntroCutscenes.Tumble`,
  `ShipController.modelHeld`), fade → index 1. Index 1, the rescue: the frozen Phantom, Gunant's salvager (ship 30)
  closing in, his three radio lines, fade → docked. **Skip** (the pause menu's 395 entry,
  `LevelScript::canSkipCutsceneNow`; no on-screen button): the original's unreachable `MGame::OnTouchEnd` branches,
  index 0 = 3 kills + advance to the rescue, index 1 = straight to the station. (Remake #16's "Hold to skip" plate was
  removed again: it brought bugs.) Portraits come from `story.json` speaker layers (resolved through the image ids; the
  computer speakers share `11_3_3`).
- **Campaign levels built** (the whole main story): 0 / 1 (above), 4 / 5 (sleeping pirate brought in by index 5's
  script), 7 (pirate trap: route, 3 sleepers at waypoint 2, Gunant always-friend), and in `MainCampaignLevels`
  (`campaign_levels_a.md` 3.5-3.16): 14 the EMP arrest at Kernstal (first kill → the line; the line over → the
  cutscene with the EMP hit's sound and the EMP lightning on the ship (#85: the original puts the systems on the player's reference matrix,
  which only the cutscene's `updateReferenceMatrix` refreshes, so its chase view shows none; flashScreen(3) is no
  visible flash), turned, cruiser → docked at Alioth, 15), 16 the first Void contact at Alioth (freighters, Brent's
  Inflicts, the Void ignore the player, flee into the wormhole; remake (#196): it closes as the first cutscene ends
  and the player's ship stops for the second; a fight plays 136 Space_Combat_Void, `Radar::draw`
  0x10), 21 the Hijacker at Kappa (scouts turn hostile, EMP him, killing him fails; launching needs an EMP bomb, 531),
  24 Void samples at Sahi (3 t from crates → the wormhole swallows the player; the Sahi jump needs a scanner and a
  tractor beam, Carla's 532), 25 / 26 the Void orbit and the pursuers back at Sahi, 28 the Dima wormhole, 29 the probe
  (lock the Void station) + 180 s survival (HUD countdown), 36 the kill contest with Errkt (NPC vs player kills,
  `CampaignLevel.NpcKills / PlayerKills`), 38 the Nivelian freighters (unkillable on success; 527 on failure), 40
  Errkt's freighter through the wormhole (reserve wave, hull carried; spawned as race 0 like `createShip(0, 1, 0xd)`
  (ship 13 is the Vossk freighter's model whatever the race, `Traffic.Create`; it showed the Terran one) and turned
  Vossk when it appears, the player held still during the cutscene following it, so `connectPlayers`' enemy lists leave
  it out of the Terran escorts', #26), 41 the escort in the Void (Errkt_CutSeq; the three Void ships put on the
  freighter and moved by their offsets, so they come in from its side and hit it (#46: as absolute positions they were
  ~70 000 units off and never fired); three LevelScript camera shots: among the Void ships at freighter + (-38000, 0,
  -30200) at "attacking from the sides", ahead and below at + (-3000, -2000, 12000) at "The engine is damaged!", and the
  crash at + (3000, 1000, 2000) dollying (1, 1, -2) u/ms; from the engine hit records 41 / 40 burn and smoke on it
  (`WreckBurn.VosskCargo` / `VosskFreighterSmoke`) and 0x9b Errkt_CutSeq_01, the broken ship's sound effects, plays over
  the music (remake: from the freighter, 3D to 40000 units; the original has no position); its engines off
  (`AssembledObject.SetExhaust` turns the freighters' single `_engine_add` part off); the freighter drifts, then 100
  hull of its maximum, ~5 %; no friendly-fire rules on it from then on, `NpcShip.noFriendlyFire`), 42 finishing Errkt,
  the mother ship's explosion (remake, #84: the Khador Drive is refused in the alien orbit at 42,
`Story.EscapesByWormhole`, so the way out is the wormhole and its explosion; 42's 0xa0 mission isn't a level mission
there, so the original's drive skipped it; since 2026-10-30 the refusal says "Destroy the freighter, then escape through
the wormhole" (`khadorWormholeEscape`, `Navigation.JumpRefusalText`) instead of 525, which a player took for a bug; explosion meshes 14285-14287, sounds 153 / 154; remake: the wormhole closes behind the player, Errkt's
freighter or wreck goes with the station) and the ride out. The level radio waits during a gate / Khador / planet jump's
scene (#46: Cornelius' call over the jumpgate). Lost floats the remake picked are listed in the file header. Story
orbits without a case stay empty (their radio still plays). A level script flies the player at its cutscene's speed
(`playerSpeed`) only while its cutscene holds the ship (`c.Cutscene`): the mining approach and object docking set
`externalControl` too, and the push (at the last cutscene's speed, a boost's if the player boosted) flew the ship
through the asteroid it was landing on, e.g. after 29's probe (fixed 2026-10-11). NPC options for levels: asleep /
inactive (`Wake`), always-enemy / friend, hit points, no loot, parked / moving (`SetMoving`), routes, only-enemy
targets, race; hostile sleepers are hidden after the tutorial. Failures (and deaths in a story orbit) count toward
`Session.FailCount`: 3 in a row of the same index → NPC guns ×0.7.
- **Ending** (`EndingCredits`, `UI/Ending`; `dialogue_cutscenes.md` 3.4): closing 43's success conversation loads the
  main menu scene with `Session.EndingPending` (the original's CutScene(2) is that backdrop): the backdrop fades in, 144
  OutroSong, Brent's radio 2071-2074 (RADIO_43_0..3) from 4 s, then the logo (0x1b5a, `GoF2Hud/ending_logo`) rises at 30
  px/s, holds 4 s and rises on with the staff credits (text 48); fade out from 130 s; at 136 s, or a tap once the last
  line has shown: `nextCampaignMission` (44) and the station (Keith's epilogue, then 45: +40 000, game won).
- **Map**: from index 32 the galaxy view shows the early-warning wormhole at the attacked system (moving to the attacked
  planet in its system view); `Story.TargetStation` points the Missions window, the map and the HUD's story icon there
  at index 40.
- **Valkyrie add-on (45-84)** (`campaign_levels_a.md` 3.17-3.19, `campaign_levels_b.md` parts 1-2, `campaign_flow.md`
  4): the step side effects (`Story.ApplyStepEffects`, cases 0x2f-0x53): the loaner ships (48 Taret's H'Soc, 49 the
  stolen K'Suukk, 56 the S'Kanarr) with the own ship parked in `Session.ParkedShip` (Status+0x8c; back at 55 / 58),
  systems 23 / 22 / 24 revealed, the Liberator blueprint (58; the original's `BluePrint::lock` at 59 writes the unlocked
  flag too, so it stays unlocked), step 59's target stations (`Session.StoryTargets`, Status+0x90), Cornelius' mines in
  stock, 5 of each free (67; `Item::makeItem(5, 0)`, calcCargoPrices leaves a 0 price alone;
`StationStock.freeUnits` / `Hangar.BuyPriceOf`: only the gifted units, a sold unit is priced, where the original's free row
took sold units too; #131), the Void Essence (68 / 69, the core of Void Crystals; case 0x47 gives one more with the
Disruptor blueprint), the Disruptor blueprint (72), the jump drive unsaleable (77), taken (78) and back with a spare
(84; the lock is the item number, so a save not at 77 drops it on load, `SaveGame.Apply`, #218: a save from before
2026-10-10 kept it on every later Khador Drive); save v6. Station side: launching at 48 goes straight into B'akrram's orbit (`Story.LaunchStation`), menu locks
(Hangar 48 / 49 / 56, remake: and whenever a loaner is flown, `Session.ParkedShip`, #46: the K'Suukk docks at 54 and the
S'Kanarr at 57, and a quick tap on Hangar sold their guns or the loaner itself; Lounge 49, Map 48 / 49, at 77 the Map
only in the Cronus, 326), no autosave imprisoned (77 at 101), Khador's ships at Kothar (77 the Cronus, a free row once
whatever the economy (#122: `ModStation::OnTouchEnd` 0xea4ec's `makeShip(37, 0)`; the Default economy lists it at 1 200
000; `Session.KhadorCronusGift`, saved, -1 in an older save = given at 77 outside the Cronus; `Hangar.BuyShipPrice`,
cleared when bought, its trade-in value stays the table's), 80-84 and after: Cronus / Typhon / Nemesis, 84 + S'kolptorr
Rum), the hangar turntable swaps to the loaner. Levels in `ValkyrieLevels` (48 chauffeured on the autopilot, 49-52 the
K'Suukk escape and the Vossk turning hostile, 56 the turret test, 63 / 64 / 65 / 67 the Skavac pirates, Khador's rescue
and Corny's break-in, 69 / 70 Trot Lykkt with the Disruptor, 73 the EMP convoy, 78 the escape from the battlestation
(control comes back facing the pirates, game +Z as the original never turns the ship; the remake's stop facing the
station had left its back to them), 79 the misjump, 80 Alice's attack on Kothar (turrets and shield generators, the
laser, the explosion variant of the station; the deep science station's thruster flames keep scrolling and its damaged
flames are split per engine glow, #70: `MakeFlamePieces80` reads the mesh, so `AssetImport` keeps
`v_station_deep_science_damaged_emitters_anim_add.fbx` Read/Write, the only game model that is), 81 Alice stranded in
the Void); the in-flight transitions 64 -> Kothar's orbit, 73 -> docked at Kothar, 80 -> the Void
(`SpaceLevel.TravelTo`). Step 59's arms convoy is normal traffic at the target stations (`TrafficPlan.AddConvoy`,
scripted in `Traffic.UpdateConvoy`: within 50 000 all hostile, the freighter's death kills its turrets and ticks the
station off, radio 2185-2189; a Liberator kill (`Target.lastPlayerWeapon` 179) is the 50 000 bonus of step 60). Khador
Drive story cases (`Story.ForcedKhadorTarget`): 78 misjumps into the Void and advances (remake, #49: as the jump scene
starts, not on picking the drive: the charge waits for a 5 s old level and no pause, and at 79 the autopilot docked at
the Valkyrie and stranded the player; a station a level script hides, 78's jumping away, leaves the autopilot menu, the
locks and the docking, `Navigation.StationObject`), 80 in the Void goes to Kothar; the drive asks 422 "Jump to the
Void's system?" whenever its map opens outside the Void (remake, the user's request 2026-10-16: the map opens at once
and its footer's Jump to the Void button, V / RB, asks 422 with No preselected, `StarMap.AskVoid`; asked first with Yes
preselected, Enter / A out of habit jumped into the Void) (`MGame::UseKhadorDrive` 0x1a9480, no campaign gate; free play
and multiplayer too, with a real drive: an item 85 or ships 37 / 38 / 40), checking 2 cells in, 1 out (x2 Extreme) but
taking only 1 (2) going in (`startChargingJumpDrive` 0x1a9710; 580 "two cells, only one left" when that is the case,
else 579), and in the Void always returns to `Session.VoidReturnStation` (remake: the story's own jumps take what cells
there are; in the Void the only refusal is the mission gate, `Story.BlocksJumps`). New NPC controls: `shootingEnabled`,
`frozen`, `detectRange`, `SetGun`; a level's scripted move (`scriptedSpeed`, `AEGeometry::moveForward`) never touches
the ship's own speed, so a released ship flies at its own again (kept, it left Harval at ~9 u/ms in 158 and 154's
fighters at 0).
- **Supernova add-on (84-162)**: see "Supernova add-on".
- **Nags** (`StorySpace.CheckReminder`, `MGame::OnUpdate`): at 0x17 / 0x18 in free flight (no level, not mining) an hour
  of playing time after the step began brings the passenger's nag (Tommy 533 / 534, Carla 535 / 536, voiced; the step
  clock restarts); at 0x18 with a tractor beam, once, Carla's 1912 (hint 0x25).
- **Gunant's drill** (#58): step 10 swaps a mounted drill for the IMT Extract 1.3 (case 10); the remake also swaps Gunant's
  Drill (90) in the hold, demounted before his conversation (the hangar opening before the docking checks is closed since
  2026-10-07, `ArrivalPending`).
- **Station-side story checks** (`StationMenu`): the launch refusals (529 / 530 at 6 / 7, 531 at 20 / 21, 326 at 77,
  `RefuseLaunchForStory`); the rescue (335: docked in a system without gate routes after campaign 16, no jump drive and
  no Khador Drive: the shuttle to Dis (70) for 25 000, `CheckRescue`); the add-on starts give their hints and medals (23
  bronze, 30 gold, `Story.AddonStartHints`).
