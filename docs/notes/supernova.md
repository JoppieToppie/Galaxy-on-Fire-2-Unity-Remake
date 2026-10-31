# Supernova add-on

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Supernova add-on (84-162)

Research: `Reference/research/campaign_levels_b.md` (parts 2-3), `campaign_levels_c.md`, `campaign_flow.md` 5, `wingmen_wanted.md` 2.
Sounds / music / the 89 sky in `Resources/GoF2Story/SupernovaAssets` (**GoF2 > Build > Supernova Assets**; the DLC2 ids checked
against the FEV's LGCY data, see "Sound").

- **Story rules** (`Story`): step side effects 89-144 (items given / taken, systems 27-31 revealed, counters reset); empty
  missions at 45 / 84 / 128 / 130 / 162; a pending blueprint product completes 143's purchase; `RequirementRefusal` (the planet
  jump's, the launch's and, since #186, the star map's checks, `StarMap.Confirm` as `StarMap::OnTouchEnd`: cabins 3214, 91
  seats for all ten survivors and 94 any cabin, 105 gamma shield II 3217, 135 3213, 139 the Vol Noor (0x2a passes as it is)
  or another Vossk ship with the signature 190 3215, 142 3216 / 3218). The star map also has `OnTouchEnd`'s step 24 check
  (scanner and tractor beam, else 532; the planet jump's is `StorySpace.RefusePlanetJump`).
  `StorySpace`: the after-success teleports (95, 96 / 127, 100, 110, 120, 126, 134, 144, 155, 161, 162), Carla's chapter calls,
  125's decoy scan. `StationMenu`: the 116 bars (90 / 91 / 92 / 94) and the 148 brokers (55 / 66 / 9).
- **Levels** (`SupernovaLevels`, run by `CampaignLevel` after the main / Valkyrie levels; the header lists every level and the
  remake's picks): 87, 89 (the Luur supernova cutscene), 91 / 92 / 94 / 102 (evacuations by docking, shuttles and Rhinos, Specter
  waves), the "Meanwhile..." cutscenes (95, 99, 109, 119, 126, 133, 160, 161), 97, 100, 105 (the bomb), 106, 114, 120, 123, 125
  (hack the secure containers), 131, 135 (titanium into the mining plant), 137, 139 (the Vossk battleships), 142 (the plasma
  tutorial), 144 / 145 (Harval, the array destroyed), 147, 154 (Alice's betrayal, 91 s hack), 157 (the final battle), 158 (the
  Harval duel). `AsteroidCentre` moves the field for 89 / 114 / 145 / 154. Remake (a player's report): at 102 the player can
  ferry evacuees too: the original's damaged Tadram object (inactive there, docking type 0: only the Rhinos used its pads) is a
  hidden pick-up target on set 10's pads named like the station, whose own landmark it replaces (`Navigation.StationReplaced`;
  `PlayerCollision` lets the approach through the station's volumes); the cutscenes and the carrier's jump end a docking first
  (`ObjectDocking.Abort`); a shuttle finding every pad taken waits 5000 units out from the nearest one (it used to "dock" at the
  object's centre, inside the hull). 144 / 145 (a player's report): the reload into the 145 attack is a stream-out
  arrival (the hidden gate) only after 143 was won in space at Var Lupra (`Session.StreamOutAfterCutscene`,
  `Level::initStreamOutPositionAfterCutscene`), else the undock point; Harval's shot stays the camera's target, hidden,
  through the array's explosion (destroyed, the camera turned to the station). 154: Hans' freighter flies his route onto
  Valkyrie's pad (docking set 7, 10 s, then back out; the shuttles' docking with a route that runs once), not into the
  hull's centre. 154 is in the alien orbit, where `MGame::OnTouchBegin` skips only the landmark block: the action still
  docks at a locked docking target (`Navigation.ActsInAlienOrbit`; until 2026-10-30 it refused every lock but a planet
  there, so the DOCK prompt showed and the action did nothing, a player's report). From state 9 the Valkyrie is race 8,
  always enemy and on the radar, still in `Level+0xf8`, so the auto turret shoots it as in the original
  (`handleAutoTurret` takes any active, enemy, not radar-hidden KIPlayer within 60 000; the state makes Hans vulnerable,
  not the Valkyrie). Remake pick: in 105's opening shot the player and the
  escorts fly 2.5 u/ms, just ahead of the camera's dolly (at the launch speed they slid back at it tail first).
- **89, Luur's supernova** (`StarSystem::switchSunForSupernovaIntro` and LevelScript states 1-4): the look-at helper
  starts 100 000 units sunward of Luur and 200 000 to its own right, drifting back so the view reaches the sun as it
  explodes; the sun is sn_sun_011 before the blast (system 27's sun is 0.99182, 1.37329 from 0x6a, its streak
  sn_sun_011); the container shoots from behind the camera toward the sun with a 500 000-long trail at half speed; no
  fog at 89; rumble p · (rnd(2A) − A) (`CutsceneCamera.RumbleAmplitude`: 100 · falloff / 30, then 1 / 100); the
  explosion = the ring mesh 0x2df1 (texture 0x2df3, from 0.68665, +4e-5 per ms) and the core mesh 0x2df2 (0x2df4) at the
  sun (`Backdrop.StartSupernovaExplosion`; the converted meshes are ~4 m, scaled to the plane quad's 3250 m): the ring
  in the sun's place (no roll), the core as the glow and the streak at 0.3 × the ring. 105's
  `switchSunForSupernovaExpansion` only sets the sun back to 1.37329.
- **Level details from the decompiled scripts**: 157's finale (states 5-12: the Liberator killed, Alice to the Valkyrie, the
  Valkyrie backing away with 0x8cb accelerating to 10 u/ms until z 100 000, Harval chasing at 4 then 2 u/ms firing
  (`NpcShip.scriptedFire`), the explosions 0x8c4 at +(-2000, 1000, -8000) then every 8 s, burning stage 1 after 300 ms, stage 2,
  the big blast 0x8c3, the beam 0x8c7, the supernova 0x8c8); 158's opening (Harval rides a helper from 50 000 toward the sun to
  (9000, 0, -13000) at 2.5 u/ms, shown from 25 s, exhaust off 20-34 s, state 2 at 34 s coasting to 300 units per frame, the
  player rolls dt / 2000 (axis lost, remake pick), then faces Harval); 145 (Harval's Shesha x2, the Specters fire from state 2,
  record 23 at the array); 144 (camera drift (0, 0.05, 0.28) u/ms, `translate` at 0x162efa, which Ghidra garbles;
  instant cloaks); 135 (every 75 s only the dead pirates come back, no
  cargo; 2 -> 3 on that tick); 102 (a Specter whose target died takes the first living dropship, else the player; instant cloaks);
  91 (the wreck's 0x8e9 LOOP, and with 8 aboard after 180 s 0x8ea and its `_wrecked_anim` played once, unverified); 154 (the
  fighters creep 0.5 u/ms in state 1; turret view / free look / Liberator off, the auto turret off until control returns); 89 /
  81 clear the new station's attacked-friends flag. The Coromesk (103) rule: from campaign 0x55 (or 0x87) every start there is at
  (70 000, 0, 100 000) facing the station (`LevelScript::LevelScript` 0x160380, `SpaceLevel.SpawnPlayer`).
- **Object docking** (`ObjectDocking` on the player, `SpacePoints` = `docks_hd.json` sets: type 1 approach, 2 docking point):
  NPC static objects with a `DockingType` (1 drop-off, 2 pickup, 3 hackable) are lockable "DOCK" targets (`Navigation`); the
  autopilot to the approach point, a 2000 ms ease in, a look-at camera, then transfers 1 unit / 1500 ms (ore 1000; "Loading"
  3204 / "Unloading" 3205, 3200) of passengers (cabins) or goods, or the hacking game; UNDOCK eases back out. No collision meanwhile.
  One ship per port: the player takes the nearest free approach point (`SpacePoints.Take` / `Free`, SpacePoint::take /
  giveFree; a taken one only when all are), shared with the story's shuttles (`SupernovaLevels.ShuttleDocking`; a shuttle
  used to dock onto the player's port). Docked (not hacking) the turrets work as in the original (PlayerEgo::setTurretMode
  refuses only mining, the Liberator and auto turrets): the auto turrets fire and the camera key cycles standard / each
  manual turret (`FreeLookCamera.TurretAllowed`, from the ease-in on; the approach is an ordinary autopilot leg with the
  camera key free (`MGame::switchCamera` refuses only an asteroid docking; the remake refused it from the approach on until
  2026-10); docked (not hacking) the camera goes to free look by itself with the original's preset orbit, -98 / 35 px at
  3800 units, and the camera key cycles the manual turrets and free look (`PlayerEgo::approachDockingPoint` +0xb1,
  `MGame::OnUpdate` switchCamera(3); `setDockingState(2)` suppresses it; `ObjectDocking.Docked`,
  `FreeLookCamera.EnterDockedLook` / `DockedView`; #132: the remake had no free look docked); the undock's end goes back to
  the standard view; `PlayerTurret.GunsBlocked` ignores the docked ship's
  blocked guns; the chase camera is on for the view; the stick still reads under `externalControl`; the mouse aims it
  with mouse steering (`FlightHud.UpdateMouseSteering` leaves it on in a docked turret view) and the fire button fires
  it (`WeaponSystem`: the docked ship's blocked guns don't halt the turret view; the press used to be latched, a
  player's report at step 135)). Fast-forward works on the approach while it is 20 000+ units out (`MGame::OnTouchBegin`
  key 0x100 takes `PlayerEgo::isDockingToDockingPoint` like the autopilot; `ObjectDocking.ApproachDistanceUnits`), and
  the quick menu opens approaching and docked (key 4 refuses only mining; not easing in / out or hacking, and without
  the Khador Drive entry while docked).
- **Hacking** (`HackingGame` plain C#, `HackingView`): the 2x3 tile board, the left / right 2x2 blocks turn clockwise, scrambled
  2 x kind rounds and never solvable in `kind` moves (kind 1 at 91, else 4); won 1500 ms after solving. Keys A / D (Q / E, arrows),
  pad LB / RB, taps on the halves. Hidden-blueprint wrecks (`TrafficPlan` table: stations 123 / 129 / 132-134) unlock their
  blueprint (radio 3156+k, `Session.HiddenBlueprintsFound`). Each is its race's freighter (`Level::createMission`:
  createShip(DAT_00253754[k], ship 13 / 15), `PlayerFixedObject::setDeadButSelectable`): the race's freighter wreck held at its
  end pose (`SpawnSpec.deadButSelectable`, `NpcShip.ShowWreckAtEnd`, invulnerable, `ModelPath` "wreck:N" for the proxies),
  docking points sets 14 / 11 / 12 / 13 / 12 (DAT_002537a4: Vossk 132, Midorian 133, Nivelian 134 / 123, Terran 129).
- **Gas clouds** (`GasCloudField`; needs a spectral filter, sort 33, not in the Void; #77, `Radar::draw` 0x1570ec: with
  the filter's attr 57 (ST-X, Omega) every live cloud has the white diamond 0x4f1 and is a lock target
  (`CombatRadar.LockedCloud`: in the crosshair box with no ship or crate there, the ring from 500 ms, locked at the
  scanner's lock time - 200 ms with sound 0x1a, held only while in the box; the plate 3236 "Gas cloud", the distance
  under its marker; a display aid only, `Radar::getLockedGasCloud` has no caller), with attr 58 (Omega) also 0x1f62
  (`GoF2Hud/cloud_off`) on the radar ellipse off screen; the SA-1 has neither): clouds by
  `Galaxy::getPlasmaProbabilities`
  (items 201-204); an ionizing blast (`Gun.Detonated`) bursts clouds in reach into sparks; the plasma collector turret (sort 35,
  `PlayerTurret` collector mode, meshes 198-200) pulls sparks in the turret view (the turret view shows the gun's
  `sn_plasma_stream_anim_add` cone, its texture scrolling inward, `PlayerTurret.stream`: until 2026-10-11 the lookup hid
  the stream and both its mesh parts and switched only the last part back on, under its hidden parent, so no vacuum
  swirl showed; attr 49 speed, 51 range; a spark in its sight takes "toward the turret" as its own direction and keeps
  it after, #79; the crosshair blue 0x1f5e, red 0x1f5d only while a live spark is in range, #78) and collects within 800
  (one sound at a time, messages summed over 600 ms).
- **Most Wanted** (`WantedBoard` plain C#, `Session.Wanted`, the Missions window's Most Wanted tab; save v7): criminals activate
  and move between stations (`WantedBoard.Move` on arrivals); in their orbit the criminal and escorts (`TrafficPlan.AddWanted`:
  hull 15 x min(rank, 20) + hp + 180, speed 4.5, its own gun x4) hide until locked; the storyline criminals surrender below 1/3
  hull at 128 / 130; a kill pays the bounty (3206). Music `wantedMusic`.
- **Specters** (race 10): `NpcCloak` (the player cloak's look, 2000 ms fades, off radar from 25 % and, remake, cloaked
  for everyone's targeting from there (`Target.cloaked`, the player's rule: turrets, auto turrets and sentry guns don't
  aim at it, fighters and wingmen chase it but hold fire, missiles lose their lock; the original only took it off the
  radar), still firing (`PlayerFighter::update` checks only the target's cloak; the remake held their fire until
  2026-10); no loot (ships 44 / 49 lose their cargo list in `Level::createShip`, race 10 gets none, `NpcShip.RollLoot`);
  random cloaking 50 %
  when panicking, else 30 % every 8 s, 9-14 s), raids at 100 < campaign < 0x91, music 149 / 150; the calm supernova system has no
  traffic before 0x9e and plays `gammaRayMusic`. Static story objects: the mining plant (103), the plasma array (112, stage by
  campaign), Luur's burning / wrecked station (111).
- **Animations the original never plays**: `PlayerStation::update` skips the station's animation at 101 and in the alien orbit,
  and `PlayerFixedObject::update` never advances an idle one, so the battlestation holds a pose there: its folded load pose
  until step 78 unfolds it, then its last frame (`PartAnimation.HoldAllAtEnd`: the ctor's Transform::Update(the length),
  station 0x65 from 0x50 0x1473c2, the alien orbit after the Valkyrie add-on 0x146e90, `Level::createStaticObject(0x4220)`
  0xcdcee; levels 80 / 81 / 154 / 157, `OrbitBuilder.SpawnStation`, the debug hull: the turrets, shield generators, 157's fire
  and the 1003 volumes sit on the unfolded arms; looped, it kept "opening itself"). Sky layers follow the camera in `beginCameraRendering` (no frame of lag).
- **Ambient story radio** (`StorySpace`, `MGame::OnUpdate`): the chapter calls (Status+0x178 at 93 / 111 / 143: the level 5 s
  old and 12 s of playing time since the step began, `Session.StoryStepStart`; Carla 0xc60 + 2k, Keith 0xc61 + 2k) and Mrs
  Moonsprocket's complaint at 122-124 (no freelance mission, not mining, an hour since the step began, which it resets; 0xc5c /
  0xc5d at 122, else 0xc5e / 0xc5f, speaker 38, `MOONSPROCKET_PASSENGER_TALK_*`).
- Verified in Play mode: every level builds; 89, 91 -> 92 (load and unload the miners), 94 (the pickup, the shuttles, the
  drop-off), 102 (the Rhinos, the carrier jump), 105, 125 (hack a container), 135 (titanium into the plant), 139 (both hacks,
  the prism), 142, 145, 154 (the hack against the 91 s limit), 157 -> 158, Harval's missiles, a criminal in orbit with its
  rockets, the Most Wanted tab, the 116 and 148 bar scenes, the chapter call and the complaint. Not played through as a player.
- **Supernova Challenge** (mission type 0xb7, `Reference/research/supernova_challenge.md`; rules `Data.SupernovaChallenge`, the
  orbit `World.ChallengeOrbit`, the HUD `UI.ChallengeView`): unreachable in the Android build (nothing started it, its score
  and combo were never written); the remake offers it under the campaign cards (`MainMenu`'s `challengeButton`: 293, the
  highscore 294 and best kills 176, else 298's first paragraph; a click asks 298 "Start Supernova Challenge?").
  `SupernovaChallenge.Start` = `MenuTouchWindow::startSupernovaChallenge`: `Session.ResetNewGame`, campaign index 0x98 (no
  step side effects: an empty story mission), Normal and the Android economy (remake: one highscore table), ship 46 Blue
  Fyre with 231 / 230 / 231, 73, 54, 59, 83, 206, 75, 76 and Patala x5 (x10 on a restart), 220 kills (rank 9), Luur (111),
  the hints marked shown; straight into `Space`. The orbit (`createMission`'s 0xb7 branch): 8 Specters (hull 250, always
  enemy, no loot) at (-55000, 0, -35000) +- 20000; the original's code also makes the burning platform (3208) and a Terran
  carrier as race-10 scenery and 4 unarmed Rhinos shuttling between them, which the remake leaves out since #179 (the
  user's comparison with the original's video: the Specters and the player alone at Luur, ringed with Novanium's magma
  asteroids, `OrbitLayout.ChallengeNovaniumOrbit`, kept apart from `NovaniumOrbit` = `Status::inSupernovaOrbit`, 0x6d
  only). The round opens with the arrival fly-in (`SpaceLevel`'s launch camera, close: 150 m ahead, over once the ship
  passes it; fire or any key skips). The first wave flies to one point (`FirstWavePoint`, reached within 300 m) in about
  5 s (`FirstLegMs`, a Move order at the run's speed), then attacks the player, easing from the run's speed to its own over
  2 s (`UpdateSlowing`); `NpcShip.Revive` clears the order, so a Specter killed on its run comes back without it. Every
  7500 ms each Specter whose explosion is over comes back 50 000-80 000 units from the player (`updateMissionOrbit`); the
  player starts at (-30000, 5000, -100000) facing the Specters' point (remake pick); time up plays the Khador Drive's jump
  scene (`SystemJump.PlayKhadorScene`) before the results; `SpaceLevel` keeps it passive (no traffic or wingmen),
  refuses jumps and the
  gate; music 2242 (`Traffic.CalmClip`, then the Specter battle tracks); gamma 1/s x 0.4 with the Gamma Shield II. The round
  is 181 000 ms (the HUD timer, `HudReadout`). Not in the challenge: the autopilot menu, its touch button and hint
  (`Navigation.CanOpenMenu`, `NavigationView`, `FlightHud`), the pause menu's Missions / Cargo hold, saving (`SaveGame.Save` /
  `AutoSave`), the asteroid lock (`Mining.UpdateLock`: the Blue Fyre has no drill, "No drill installed." came up at every
  rock). Scoring (the iOS 1.1.19 build's `Level::enemyDied` / `MGame::OnUpdate`; the Android code has only
  `Hud::drawChallengeModeScore`'s drawing): a kill scores 1000 + 2000 x the combo timer's remaining share of 7500 ms (3000
  at a full timer, 1000 once it ran out) and sets the timer to 7500 ms, raising the combo while it runs; run out, a combo pays the
  original's `combo x 1000 x (1 + 0.05 combo)` (shown beside the count in its last 3000 ms, blinking 50 ms in 100; the count
  pops by 1 + (timer - 7000) x 0.01); the round's end pays a running combo. HUD: 7 digits (`GoF2Hud/challenge_digit_0..9`,
  images 6000-6009 of `gof2_challenge_interface_ipad_large`, **Build HUD Images**) tinted 0x77CCFF at the top centre, under
  them `challenge_combo` (6010, "COMBOx") with the count and the bonus; `.hud-challenge` moves the lock plate and HUD
  messages under them. Sounds (`SupernovaAssets.challengeSounds`: SV_Mode_01..10, events 2283-2292, never played by the
  original): the combo's steps 01..09, the bonus 10, at the events' volume 0.42658. The end (time out after 1.5 s, death at
  the game over's 7 s): the score is final, a question (`Traffic.Ask`) "Your score / Kills / Highscore or New Highscore! /
  Play again?" (296 / 176 / 294 / 295 / 300); Yes restarts (`Start(true)`), No goes to the main menu. Highscore and best
  kills in PlayerPrefs `challenge_highscore` / `_kills`. Remake pick: the Specters fire their ordinary guns (rank 9, x0.7):
  `assignGuns` forces 0xb7's NPC damage to 1 and the Specters' x0.7 truncates it to 0, a harmless round. Tests
  `SupernovaChallengeTests`. Verified in Play mode (before #179's changes): the menu entry and dialog, the orbit (8
  Specters, then also 4 Rhinos and the scenery), music, the HUD's score / combo / bonus, the time-out's question with a
  new highscore, Yes (round 2, Patala x10), a death's question, No to the menu with the highscore shown. Not played through
  by hand.
- Object docking approach: reached within PlayerEgo+0x1d8 = 0x578 (1400) units; remake: a ship circling the point (its turning
  circle ~2500 units across) starts the ease-in after 4 s within 4000 units.
- Sky layers: looping animations skip their one-off first keys (`PartAnimation.loopStartMs`: flares 1000 ms, storms 33 ms);
  looped from 0 they flashed the whole sky white.
