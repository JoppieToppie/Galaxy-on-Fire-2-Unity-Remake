# Bar and freelance

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Bar and freelance (agents, chat, missions, blueprints, wingmen, Status)

Research: `Reference/research/freelance_missions.md` (agents, offers, mission generation, chat texts, missions in space,
delivery), `lounge_ui.md` (the HD lounge UI, portraits, lounge voices, ticker, Missions and Status windows, medals),
`blueprints_mods.md` (+ `Reference/tools/blueprints/blueprint_table.py`), `wingmen_wanted.md` (+
`Reference/tools/wingmen/wingmen_wanted.py`); `Reference/tools/missions/mission_tables.py` (Python port of the
generator).

- **Agents** (`Agent`, `AgentGenerator`): `Generator::createAgents` per station when it isn't among the last 3 visited,
  kept on `StationStock.agents` and saved: 3-5 visitors (story agents from `agents.json` once campaign > 16, generic
  ones: 20 % any of 8 races, offers mission 46 % / small talk / item / purchase / wingmen, one wingman offer per bar, 35
  % diplomats for hostile races, 1 % a 10x mission; the 10x and the step-23 AB-1 seller are the Android build's, the
  iOS 1.1.19 build has neither, so the Default economy skips them), names from `names.json`, portraits `ImageFactory::createChar` (the
  generic sets draw through `Portrait.Show`). Nobody in the supernova system's lounges (`Status::inSupernovaSystem`,
  system 27 before campaign 0x9e: the evacuated Ginoya stations; a save's kept visitors there are cleared on docking,
  `Shop.EnterStation`); remake, the same stations (`StationLevel.Evacuated`) have no parked ships, hangar traffic or bar
  flybys either. At index 24, launching from Thynome (10) leaves index 23's AB-1 Retractor seller with nothing to sell
  (`ModStation::OnTouchEnd`: setEvent(1) + setOfferAccepted, 858; `StationLevel.Launch`). Missions: `createMission`
  (target rules, every type once before repeats, difficulty, reward `(int(d/10*5500)+1500)*(dist/1200+1)` per type +
  `10*level^3`, standing bonus recomputed at every chat; the taken mission pays the bonus its chat promised, whatever
  the standing is by then, like the original's stored Mission bonus: `FreelanceMission.promisedBonus`, set by
  `Freelance.Accept`, a player's report; older saves recompute).
- **Lounge** (`LoungePanel`, `UI/Station/Lounge.uss`): one visitor billboard per agent; plates over them (race until
  talked to, then name + role; remake: always shown) and a visitor list (remake, for keys / controller); tapping opens
  the chat (`LoungeChat`): the original's texts (greeting, intro, offer, reward line, question), HD answers (green Okay
  / red No thanks + Let me see it or Show it on the map + What's the risk?; No thanks closes; a single Okay for closing
  lines), deals: the original asks again in a ChoiceWindow (865 / 866-873 / 885); remake: Okay is the deal, the station
  dialog only asks when it warns (864 the current mission discarded, the Extreme up-front costs, the multiplayer squad;
  `LoungeChat.ConfirmWarning`; the dialog is opaque: the 0.93 panel let the chat text read through it), checks 337 / 338
  / 203 / 785 as messages, one lounge voice greeting per chat (`LOUNGE_eng/deu` in the story assets;
  `SpaceLounge::getSoundId`: the plain GENERIC greeting for an accepted offer, and remake for every opening with nothing
  left, `LoungeChat.offerSpent`: a Kaamo mechanic's upgrade is per hull, so the agent is never accepted and voiced "I
  have something special" over 858, a player's report 2026-10-24). Diplomats rehabilitate (±35), coordinate sellers
  reveal the system (the map opens on it), blueprint sellers unlock (remake, free play only: the blueprints only story
  steps unlock, Khador Drive / Disruptor at Var Destro 105 and Gamma Shield II / Chromo Plasma at Quineros 107, are sold
  by a generic visitor at 1.5x the item's max price until known, `AgentGenerator.AddStoryBlueprintSellers`; remake: a
  seller whose blueprint is already owned, e.g. Sao Perula's Liberator after step 58, has 858 "nothing left" instead of
  selling it again for nothing like the original, `AgentGenerator.StoryAgent`), mod sellers mod the hull.
- **Freelance** (`Freelance`, one mission at a time, `Session.FreelanceMission`): Courier loads Secure Containers (116,
  unsaleable), Passenger needs cabins; docking delivers Courier / Passenger / Purchase / Stolen goods / Informer (the
  client's message, payout with the 1 000 001 guard, standing +5, missions completed +1, sound 36). The target orbit of
  a space mission is built by `FreelanceOrbit` instead of the traffic: Defense, Protection, Recovery / Salvage (EMP the
  Hijacker: its container drops for the tractor beam, then deliver it; the Hijacker's and the Informer's lock plate
  reads their name alone in red, like a Most Wanted criminal's, `Target.plateRed`, `Radar::drawCurrentLock`'s 0x64b /
  0x67f), Pirate hunting, Wanted, Junk removal (121 s, HUD countdown; gone once the mission is over, #46), Escort,
  Intercept, Challenge (score vs the rival; an odd number of pirates, i + 3 or i + 4, so the kills never tie: a tie is
  the rival's; remake, a player's suggestion 2026-10-30: the rival remembers the result, `Agent.challengeResult` set by
  `Freelance.RecordChallenge`, and at the bar says one of four lines about it, `challengeRivalLost0-3` /
  `challengeRivalWon0-3`, where the original repeats 859 "See you outside" after the contest too); briefing, success / failure messages; remake waypoint to the enemies (lockable, its own marker on the
  lock plate; it blocks no other lock). A mission orbit (the level mission Status+400) refuses docking, the gate, planet
  jumps (the autopilot's too) and the Khador Drive with 525 until the mission is won or failed, except Courier
  (`Freelance.BlocksTravel`, `MGame::dockEvent` / `Radar::draw` / `MGame::UseKhadorDrive`); the story's level missions
  refuse the gate too. Informer orbits skip the "He's back!" alarm and the remake's pirate events (a player's report: a
  pirate's death there spoiled the mission; their ships, `SpawnSpec.pirateEvent` incl. `EventGuard`, never count in
  `Traffic.OnShipDied`) (`TrafficPlan.InformerOrbit`; a spoiled Informer gets the normal traffic). Killing the spy
  alarms its race like any friendly fire (the original). Options > Gameplay "Informer missions"
  (`Settings.InformerOriginalRule`): Remake (default) = other deaths after the spy's no longer count; Original = any
  other death before docking fails it (`PlayerFighter::update` 0xf1c8c checks only +0xf1). Stolen goods' markers
  (`Freelance.MarkedStation` / `MapStation`: the star map, Show on map, the HUD): only the system until the documents
  are aboard, then the client's station (the original marks the documents' station from the start). Escort attackers
  (case 9) are always-enemy at a random waypoint of the route, the freighters of the client's race (Terran past race 3);
  without it they showed as friends while killing the convoy. **Sleepers** (`NpcShip.UpdateSleep`,
  `PlayerFighter::update` 0xf19b8 / 0xf2750): a hostile one wakes only on the player (or the steered Liberator) within
  +-25 000, or within +-detectRange (default 50 000) unless the player is cloaked; other ships never wake it (the rival,
  the convoy and wingmen woke them early); a non-hostile one on its target within +-detectRange; fixed objects and
  freighters on any enemy within +-50 000 (`PlayerFixedObject::update`). Courier / Passenger cargo draws pirate escorts;
  the Informer orbit has its spy (1663). Story step 13 is completed by three freelance missions (remake, #196,
  `Story.StepThirteenMissions`, as players remember the original; the binary's `nextCampaignMission`: one). EMP
  weapons now drain NPC EMP.
- **Stolen goods** (#113): the original marks the agent's station from the start (`Mission::getTargetStation`), and players
  went back there looking for the documents; remake: no marker until the documents are aboard, then the client's station
  (`NavigationView.IsFreelanceTarget`, `Freelance.HasDocuments`), and the target's shop gets no new set while they are
  aboard. A mission orbit's waypoint goes as soon as its mission ends however it ends (discarded, another taken, the
  squad), not only through its own dialogs (#48, `FreelanceOrbit.routeCleared`).
- **Missions window** (`MissionsWindow`): Story and Freelance side by side, Show on map (`StarMapMode.Mission`: view
  only, centred on the target, story / freelance icons; like the original it doesn't program the autopilot: the
  station's Map does), Discard (418). The story text is every step's objective (`MissionsWindow::init` 0x17a604:
  DAT_00258f68[index] below 0xa4), hidden and empty missions too (step 13's "find work in the Space Lounge" before the
  convoy); the map button only for a visible mission with a target. The pause menu's Missions page the same. Remake: a
  "Pirate activity" tab beside Most Wanted (see "NPCs and combat", Pirate activity board).
- **Blueprints** (`Blueprints`, the hangar's Blueprints tab 272): 25 products keyed by item (items.json ingredients),
  per-ingredient progress, production station (first investment; 212), 200 $ per unit shipping from another station
  (288; volatile 204 / 209 refused, 289), 210 / 223 need gate routes (528), Autocomplete `int(qty*maxPrice*1.25)` (iOS 1.1.19, the Default economy: `int(max(remaining value, qty*maxPrice)*1.4)`; 210:
  2 000 000 + the rest's value), a finished run to the hold (211) or waiting at the production station (210, collected
  on docking, 213; remake, a player's report: a finished run left the old Autocomplete row selected on the Shop tab,
  and A autocompleted 'blueprint -1' for 0 $ into an '#item-1' in the hold; selecting no longer commits, below).
  Unlocks: lounge sellers, campaign steps 34 / 58 / 72 / 104 / 141 with pre-invested ingredients. Remake (a player's
  suggestion): a run finished or autocompleted at its production station whose product has a free slot
  (`Hangar.OffersMountAfterBuild`: no swap, no commodity, not a secondary that joined its mounted stack) asks 211 +
  "Mount it now?" (`HangarWindow.Finish`); Yes mounts it (208) and shows the Ship tab, No leaves it in the hold.
  - **Making blueprints** (PR #166, a contributor's rework, fixed 2026-10-28): at the production station (or the first
    unit, after 212) ADD puts the units in at once; at another station they wait with a FREIGHT badge (`bpFreight`)
    and are shipped (288) on FREIGHT, Complete, a tab switch, Back or closing the hangar (`ReadyToClose` asks as the
    original's readyToClose did). A refusal (289, too little money) or No puts them back in the hold, so the next tab
    switch doesn't ask again. A full blueprint waits for Complete (`bpComplete`); the view stays on the blueprint for
    another run (it went to the Shop tab / the blueprint list). 211 / 210 stay the original's dialogs.
  - The actions under the ingredients: 185 Shop (186 at the owned club: that tab, the recipe's goods with a cyan edge
    and their recipe amount `bpRecipeAmount`), Autocomplete (195) and Complete. Each is a row of its own
    (`RowKind.ShopLink` / `Autocomplete` / `Complete`): up / down reach it, Enter / A does it, a click selects and does
    it (the PR's click-only labels left controller players unable to finish a run). X / the left arrow is TAKE; the
    hint row says TAKE / ADD in the ingredients view (`HangarWindow.EditingBlueprint`).
  - **TAKE** (`Blueprints.Withdraw`, remake only: BluePrint has addItem and complete, nothing that removes): staged
    units first, then at the production station only the player's own (`BlueprintState.playerUnits`, counted by
    `Invest(..., byPlayer: true)`), never the story's pre-invested goods (the owner's decision: thousands of plasma at
    112, 50 Void Crystals, 10 Hypanium, 5 Microchips could otherwise be taken out and sold). moneySpent goes down by
    what those units added (`playerSpent`), not today's price, so it can't reach 0 while goods are still in; all taken
    out, the blueprint is unstarted again (station -1). Saved with the game; a save from before has 0 per slot. The
    200 $ shipping is not refunded. Produce / ResetAtStation clear the share (`BlueprintWithdrawTests`,
    `BlueprintSaveTests`).
- **Wingmen** (`Wingmen`, `Traffic.SpawnWingmen`, `NpcShip` wingman mode): spawned next to the player in every orbit
  (model seeded by the name length, 600 hull, unarmed in a Challenge), formation slots, attack the first hostile ship;
  the flight menu's Wingmen entry (306) gives the commands 307-311 (fire at will, attack my target, secure next
  waypoint, laser / EMP blaster); a dead wingman leaves the contract; the 10-minute contract runs while flying, goodbye
  313 at the next docking.
- **Station extras**: the news ticker on the main view (`NewsTicker`: the campaign window's story news + 2 random items,
  tokens, 50 px/s; a press holds it and it follows the pointer 1:1, no inertia, `NewsTicker::OnTouchBegin` /
  `OnTouchMove`; remake: the full width of the UI along its bottom edge, repeated so it is never empty, and it keeps
  scrolling under dialogs; not at 101 / 108 / Loma); the Status window (`StatusWindow`: pilot, ship, reputation bars,
  statistics, 45 medals by grade with hints; `Achievements` checks on docking, "New medal!" 353; with all base medals
  the wingman fans pay you).
- The Most Wanted board (Missions window tab): see "Supernova add-on".
- **Medals** (`StatusWindow`, `TouchButton::draw` style 4, images from **Build HUD Images**: `GoF2Hud/medal_plate_*`,
  `medal_00..44`, `medal_pressed`): the plate by grade (elite 36-44 their own), the 54 px symbol at (114, 41) tinted by
  grade (DAT_00252060 / 50), the pressed overlay on the selected medal, the name in white under it; the elite medals
  react at grade 0 too. Counters: 8 Personal Need = the booze tonnes gained per hangar visit (`Session.BoozeBought`,
  `ModStation::OnKeyPress` / `OnTouchEnd`), 9 Barkeeper = the booze types (132-153) bought / sold / from the lounge /
  from non-Void crates (`BoozeTypes`; its silver hint lists the missing drinks), 21 Alien Hunter = tonnes from Void
  crates (`AlienRemainsCollected`); save v8. `checkForNewMedal`'s strict medals (5-8, 10, 16, 18, 20, 21, 26, 27, 29,
  31-34, 36) need more than the threshold; 36 counts `Ship::getMaxLoad`. 22 = no weapon or equipment from campaign 8,
  the loadout docked with (`Session.ArrivedWithoutGear`, taken as the station loads: stripping the ship before the check
  earned it, #46); the elite medals 38 / 40 / 41 / 42 / 44 (`Session.EliteFlags`, `Achievements.Elite`): the Ore Athlete
  streak (`Session.OreStreak`, reset on docking and a lost mining game), 40 kills without a scanner (`BlindKills`), 41
  asteroids by rockets in one flight, 44 asteroids by the Liberator; save v9 (also the Loma flags).
- **Supernova medal progress in flight** (`Achievements.Progress`, `Hud::hudEventMedal` 0x18f36c; a player's report
  2026-10-26): "name: n%" on the HUD message plate (`GameHooks.FlightMessage`) while the medal isn't earned, n = the
  counter against the gold threshold (remake: at most 100), when the counter's caller shows it: 38 from 30 % at each
  10 %, 39 at every kill below twice the threshold, 40 at each 10 %, 41-44 once reached; none in multiplayer. Test
  `AchievementsTests.SupernovaMedalProgressShowsInFlightAsTheOriginalDoes`.
- **The Supernova medals pay too**: `ModStation::checkMedals` pays `DAT_00251ff0[grade]` = {0, 5000, 2500, 1000} for
  every new medal of the 45 (none on Hardcore), and the elite medals only have a gold grade (thresholds [t, -1, -1]), so
  each pays 5000 in the original as well; its medal window just never shows the amount (`ChoiceWindow::setMedal`: the
  name and the hint), the remake's toast does. Kept (a player's report 2026-10-25 thought they paid nothing).
- Type 15 Ore Mining (unreachable in the original's generator) is only rolled in multiplayer sessions (see "Multiplayer").
