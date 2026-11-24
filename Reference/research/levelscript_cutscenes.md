# LevelScript cutscenes (story scenes inside flight levels)

Findings of the LevelScript analysis (sub-agent of the dialogue research, reported 2026-09-24; its scratch disassembly
`process.asm` / `ctor.asm` stayed in the session scratchpad). Complements `dialogue_cutscenes.md` (presentation),
`campaign_flow.md` (step table) and `campaign_levels_a/b/c.md` (spawns). "M" = campaign index (decimal, hex in brackets);
"msg[n]" = radio message n of that mission from `Level::createRadioMessages` 0xd0574 (text, speaker, trigger, param).
Radio trigger 5 = time ms, 6 = after msg[p], 27 (0x1b) = LevelScript event == p. Line numbers: `game/LevelScript.c`.

**Decompile warning:** Ghidra drops / scrambles float arguments (e.g. shows `translate(0.2dt, …, 2.2dt)` for
`translate(0.2dt, 0, 2.2dt)`); every number below for M0, M1 and M14 comes from the Thumb disassembly. FMOD ids 159-161:
159 TimeSpaceJumpEnd, 160 SpaceTimeJump fit their use; for 161 "Alert" fits better than Engine_09_Broken (disputed).

## 1. Structure of `LevelScript::process` 0x160d50

- Returns `this[0x11]`, the **cutscene-active flag** (asm 0x16d366); `MGame+0x5f` stores it (MGame.c:5751): hides the HUD,
  is passed to `PlayerEgo::update` / `Level::update`, forces the advanced shader.
- Common cutscene actions: `Hud+1 = 0` (HUD off), `Radar+0x48 = 0` (radar off), player computer-controlled, often
  invulnerable / invisible, `setLookAtCam(true)` (fixed camera looking at a target), `setTarget` / `setPosition`,
  `TargetFollowCamera::translate(dx, dy, dz)` per frame = world-space dolly. Returning control reverses this and
  `resetCamera` (chase offsets (0, 600, -650) / (0, 600, -1338)).
- Fields: `+8` (int64) level clock ms; `+0x90` (int64) per-step timer (reset on most step changes); `+0x28` scratch camera
  vector; geometry slots `+0xd8`, `+0xdc`, `+0xb8..+0xc4`, `+0xac..+0xb4` and Explosion `+0xc8` drawn by `render3D`.
- **Generic fly-in** (`LAB_0016101c`, lines 3137-3176) while `+0x20` (startSequence): LOD update, wingmen frozen (`+0x129`),
  `+0x24 += dt`; ends when `+0x24 > 7000` and M > 1: `+0x20 = 0`, `+0x21 = 1`, look-at off, vulnerable, collision on,
  `this[0x11] = 0`, HUD event for the programmed station, `setAutoPilotToProgrammedStation`. M0 / M1 never end it
  (their scripts own the camera). The constructor sets `+0x10 = 0x100` → `this[0x11] = 1` at every level start (HUD
  hidden during the normal fly-in too). Generic camera: player + R·(±500..2500, ±500..2500, 9000) (7000 with
  `initStreamOutPosition`).
- Dispatch: binary search on the mission index (line 3256 on) plus special blocks before it.
- Small hooks: M5 spawns enemy 0 at player + (5000, 0, 30000) and activates it; M21 scouts turn hostile; M26 (alien orbit)
  resets the wormhole when msg[1] fires; M50/M51 everyone but wingmen hostile, `setStanding(0, 100)`. Constructor spawn
  overrides (player facing the target station / an enemy) for M48 (autopilot to the gate), 65, 87, 91, 92, 102, 114,
  123, 137, 139, 142 and station 0x67 when M > 84 or M = 135.

## 2. Scripted missions

### M0: prologue, Dareius belt (constructor lines 256-296, process 2649-3130)

Constructor: camera (-1000, -500, -40000) look-at the player; player (0, 0, -60000) facing +Z, computer-controlled,
invulnerable, no collision; HUD / radar off; pirates (enemies 0-2) asleep and hidden; `hyper_drive` fx mesh 15027
(0x3ab3); caption msg[0] (text 1668, speaker 17 "Story") at 1500 ms.

| Step | Trigger | Action |
|---|---|---|
| 0→1 | msg[2] ends | player to (18000, -12000, -40000); camera to (-12000, 2000, -500), still looking at the player (no setTarget: the pirates talk in the foreground; rechecked at 0x160f0e, the first pass read 0xc63b8000 as -3000); pirates at (-10000, 500, 0), (-10000, -300, -1700), (-10000, -200, 2000) facing +X, visible; while step < 3 they drift `translate(0, getPulseValue(0.0005) - 0.5, 0)` per frame (the pulse = |sin(playing time ms · 0.0005)|) |
| 1→2 | msg[6] ends | camera targets pirate 0 from (-5000, 300, -5000) |
| 2 | – | music stop, sound 142 (0x8e); camera dolly (0.2dt, 0, 2.2dt) |
| 2→3 | msg[7] "attack" ends | pirates: exhaust, visible, active (`+0x124 = 50000`); step 3 keeps the dolly (0.2dt, 0, 2.2dt) (iOS 0x1000b99ec) |
| 3→4 | msg[8] ends | player control, radar + collision on, `this[0x11] = 0`, `+0x12 = 1`, start sequence off; look-at off and target = player with no setPosition, so the chase camera eases over from the pirates' shot. Fight; msg[9] after 3 kills |
| 4→5 | msg[10] ends | music stop, sound 143 IntroAtmo; cutscene on, guns removed, player rotation (0, π/2, 0), camera = player + (25000, -200, -1000) |
| 5 | msg[12] "Activating hyperdrive" ends | once at speed 2.0: sounds 157 + 158 (3D at player), engine stop, 161 (3D); speed ×0.98 per frame; after msg[13] dolly (-0.2dt, 0, 0.3dt) |
| 5→6 | msg[14] fires | timer reset |
| 6 | – | rumble = t / 4000 |
| 6→7 | msg[15] ends | hyperdrive fx at the player (animation restarted), sound 160, stop 161 |
| 7 | – | fx billboarded to the camera; at 2000 ms stop 158, player hidden, rumble 0; fx animation end: music stop, sound 141 |
| 8 | 4000 ms | rumble = max(1 - t/3000, 0), fx reset |
| 9 | – | `StarSystem::switchPlanetForIntro` (texture 0x273a, planet rescaled), `Level::switchSkyboxForIntro` (sky mesh 0x4591, texture 0x275a, landmark array `Level+0xfc` killed, uncertain); player (0, 0, 0) dir (0, 0, -1), rotate (π/4, π/4, π/4); particles reset; fx at player; camera (5000, 500, -10000) |
| 10 | 2000 ms | rumble = t/2000; then sound 159, fx replayed at the player |
| 11 | 2500 ms | rumble = max(1 - t/2500, 0); then player visible, engine sound 156 (broken), smoke on, exhaust off, speed 2 |
| 12 | msg[17] ends | player rotate(dt/3000); msg[16] "lost control" at step 12 (trigger 27); then camera = player + (-2000, -2000, -5000) |
| 13 | 6000 ms | rotate(dt/4000); camera = player + (700, 0, -1700) |
| 14 | 12000 ms | rotate(dt/5000), dolly (0, 0, -2dt) (iOS 0x1000bf2d0; the Android decompile's (-2dt, …) is scrambled); camera = player + (-3000, 3500, -6700) |
| 15 | msg[22] "Entering orbit of Var Hastra" ends | fade to black 5000 ms |
| 16 | fade done | fill black, `nextCampaignMission`, module 2 (new flight level) |

### M1: rescue at Var Hastra (constructor 297-319, process 1855-1896)

Player (0, 0, 0), rotation (0.462, 0.462, 1.534), frozen; exhaust, engine and enemy engine sounds off. Camera fixed at
(1500, 1600, -3000) look-at the player; fade in from black 5000 ms. Per frame: player rotate(0, a, a), a =
(2dt & ~15)/65536·2π; camera dolly (0.1dt, 0, 0); enemy 0 (the salvager, asleep) moves forward 0.2dt·min(z/-5000, 1).
msg[2] ends: fade to black 5000 ms, fill black, module 5 (station): the player docks.

### M14 (0xe): Terran arrest (lines 7869-8049)

| Step | Trigger | Action |
|---|---|---|
| 0→1 | msg[1] fires (trigger 20) | the pirate dies; damage particles on the player, sound 15 |
| 1→2 | msg[1] ends | `Level::flashScreen(3)`; cutscene on, speed 0, guns removed; camera look-at the player from player + (1000, 700, 1000); all type-8 enemies killed |
| 2→3 | msg[3] "Surrender" ends | ship yaws dt/10000, camera dolly (0, 0, dt); then player hidden, the last enemy (PlayerFixedObject cruiser) moves; camera targets it from its position + (-10000, -200, 5000) |
| 3→4 | msg[4] Story caption ends | timer starts |
| 4 | +4000 ms | `nextCampaignMission`, `departStation(98)`, station module |

### M16 (0x10): first Void contact (lines 8050-8234)

msg[0] (Void gibberish) fires: cutscene on, camera look-at enemy 0 from enemy + (6000, 4000, 47500), dolly (-dt, 0, -3dt).
msg[1] ends: control back. 20000 ms later: the wormhole resets. msg[2] fires: wormhole opens, camera from wormhole +
(-9000, -4000, -30000), dolly (0.5dt, 0, 0.2dt); Void ships (type 9) get a route into the wormhole. msg[3] ends: they are
hidden and removed. msg[4] ends: control back.

### M24 (0x18): the wormhole takes Keith

msg[3] fires: cutscene on, invulnerable, guns removed, wormhole placed ahead along the player's direction, all enemies'
target lists cleared. msg[4] fires: wormhole visible and opened, sound 34 (3D at the camera), rumble; the player keeps
rotating dt/5000. No script exit: the wormhole does the transition.

### M29 (0x1d): probe

msg[0] fires: scanner probe (mesh 14290) launched along the player's heading at 3dt, camera looks at it, sound 14.
msg[2] ends: control back, probe deleted, survive objective `Objective(3, 180000)`.

### M40 (0x28): Errkt's freighter

msg[3] fires: the freighter appears at friend-route waypoint 0 and moves; cutscene look-at the freighter, dolly -2dt;
msg[4] ends: control back. Freighter z < 90000: Void ships spawn at wormhole ±10000 (random 20000). Past the wormhole it
moves off and is parked at (0, 0, -200000).

### M41 (0x29, alien orbit): Errkt's death

Constructor: particle systems 0x28/0x29 on enemy 0, Void station explosion meshes 14285-14287. msg[4] fires: cutscene on,
three Void fighters at (-40000, 500, -30000), (-41000, -200, -31000), (-42000, 100, -32000) aimed at the freighter, camera
on the freighter. msg[5] fires: fire particles, freighter unkillable and stopped, sound 155 (Errkt_CutSeq_01). Step 2
(15000 ms): freighter drifts (0, -dt, 2dt), tumbles dt·3e-5. Step 3: freighter to (2006, -31500, -86720), exhaust off.
Step 4 (15000 ms): camera dolly (dt, 0, -2dt); then stop sound 156, freighter hull 100, control back: the player finishes
Errkt.

### M42 (0x2a): mothership explosion and escape

msg[7] ends at step 5: sound 153 (Mothership_Xplosion_Loop, 3D), objectives cleared, wormhole at (25000, 20000, -55000)
opened. Step 6: rumble 0.5 + a sound parameter; the player enters the wormhole: cutscene on, player hidden, camera at the
player looking at the station part (`landmark[0]+0x13c`) moved by normalize(player position)·5000; `this[0xa8] = 1`
draws the billboarded explosion meshes; sound 154 (Mothership_Xplosion_CutSeq). Step 7: full rumble, dolly (-18dt, …);
~4000 ms into the explosion animation the station is hidden; 15000 ms: fade to black 4000 ms. End: hull / shield / armor
saved, station = `Status+0x84`, `comingFromAlienWorld = 1`, module 2 (arrival wormhole behind the player, constructor
lines 1148-1167).

### Partly decoded

- **M78 (0x4e)**, Alice's drive (constructor line 367): 13 steps on the level clock (7000, 7900, 9000, 18000 ms …);
  station part animation, voice 1122, enemy 1 flees; hyperdrive fx at (0, 0, 14000), sound 160; ships and station hidden,
  control back at step 13.
- **M80 (0x50)**, Alice attacks the station: laser animations 14370 / 14366, Explosion with fire streaks, hyperdrive fx;
  sounds 1121, 18, 160 (lines 9760-10155).
- **M81 (0x51, alien orbit)**: player hidden; camera (-20000, 800, 120000) dolly (-4dt, 0, 2dt); 10000 ms: fx at
  (0, 0, 10000) + sound 160; mothership appears at 12000 ms; ends 14000 ms. msg[3] ends: fade to black 5000 ms,
  `departStation(100)`, station module.
- **M89 (0x59)**, Supernova intro (process 2472-2647): caption at 2000 ms (text 2494 "Meanwhile in Midorian space…").
  Camera tracks a dummy node near enemy 1, dolly 2dt·(1 - t/35000). 30000 ms: the probe ship (enemy 0) aims at the camera,
  projectile animation (mesh 14229) follows; rumble grows within 7000. 38000 ms: voice 2248, the sun grows. 39000 ms:
  fade to white 500 ms; `switchSunForSupernovaIntro` (sun textures 0x2df3/0x2df4, meshes 0x2df1/0x2df2), white fade-in
  10000 ms, enemies 3+ killed; sun grows 4e-5·dt per frame (`scaleSunDuringSupernovaIntro`); 7000 ms: fade to black;
  8000 ms: `nextCampaignMission`, `departStation(10)`, station module.
- **"Meanwhile…" interludes** (M95, 99, 109, 119, 126, 133, 160, 161; lines 2309-2346, constructor 1352-1375): player
  hidden; camera (-15000, 800, 75000), dolly (dt, 0, -2dt); at 2000 ms step 1 and the caption (speaker 17, trigger 27
  param 1); 2000 ms after it ends: `Mission::setStatusValue(1)`.
- **M105 (0x69)**: freighter escort, bomb drop (mesh 14247), `switchSunForSupernovaExpansion`, sounds 14 and 2248, white
  fade 500 ms; `nextCampaignMission`, station 111, module 2.
- **M106 (0x6a)**: fade in from white 8000 ms; camera at player + (2200, 1300, -4000); stealth fighter reveal, chase,
  its death; 2× `nextCampaignMission`, station 10, module 2.
- **M144/145 (0x90/0x91)**: Harval's message (camera at enemy 0 + (-4600, 1000, -4300)); the plasma array explosion.
- **M154 (0x9a)**: Alice's ambush at Valkyrie. **M157 (0x9d)**: Valkyrie fires / supernova reversal
  (`switchSkyboxForSupernovaReversal`, explosion chain, sounds 2244, 2243, 2247, 2248; ends at station 111).
  **M158 (0x9e)**: aftermath (constructor line 675): white fade-in 8000 ms, camera flies toward (9000, 0, -13000),
  Harval appears ~25-30 s, control back 12000 ms after msg[2] ended.
- Cutscene mode also used (not decoded): M64, 67, 69, 70 (Khador rescue / Lykkt chase), M91, 92, 94 (gamma-ray
  evacuation), M102, M125, M131, 135, 137, 139, 142.

## 3. Skip rules

- **Fly-in skip**: `MGame::OnTouchEnd` — a tap the HUD doesn't consume while `startSequence()` (or the player is dead)
  calls `LevelScript::skipSequence` 0x16f57c: if `+0x24 ≥ 1` and M ≥ 1 → `+0x24 = 7001`, player vulnerable; the generic
  end code runs next frame. The M0 prologue can't be skipped this way.
- **Cutscene skip**: the pause menu (`MenuTouchWindow(1)`) shows Skip (395, button 0x12) only for M154, M157, M158, while
  `canSkipCutsceneNow` 0x16f5b8 allows (M154 event 1-9, M157 event 2-4, M158 event ≤ 1). `skipCutscene` 0x16f61c
  triggers + finishes the first N radio messages (8 / 4 / 3) and jumps: M154 event 9, `+0x90 = 2001`, enemy 0 AI on to
  route waypoint 0; M157 event 4, `+0x90 = 18001`; M158 `+0x98 = 12001`, Harval at (-2077, 0, 20233), visible, active,
  event 2. MGame's skip branches for M1 (straight to the station) and M0 (`nextCampaignMission`, `setKills(3)`,
  module 2) are unreachable in this build (no button).
- Not story cutscenes: `Globals::isCinematicModeActive`, `MGame::setCinematicMode`, `MenuTouchWindow::inCinematicMode`
  (state 0xd) = the "Action Freeze" photo mode (text 59, camera 3).

## 4. Presentation

- During `MGame+0x5f`, `OnRender2D` skips `Hud::draw`, `Radar::draw` and the lock plate; still draws `Radio::draw`, the
  dialogue window and (M > 7, event == 0) `Hud::drawOrbitInformation` (MGame.c:7036-7090). Scripts also clear `Hud+1`
  and `Radar+0x48`. No letterbox bars.
- Captions ("3598 A.D.", "Meanwhile…") are ordinary radio boxes (speaker 17 "Story").
- Fades: `Layout::startFade(out, rgb, ms)` / `drawFade` 0xe7978: full-screen rectangle, alpha 255·t/ms (out) or
  255·(1 - t/ms) (in); colour `& 0xffffff00`: 0xff black, -1 white. `enableFillScreen(true)` = opaque black (hides the
  level switch; the constructor clears it). `Level::flashScreen(3)`; camera rumble `setRumblePercentage(0..1, 20)`.

## 5. Constructor summary (0x15e650)

All levels: HUD / radar / camera / level pointers, `drawTitleImage`, event 0, startSequence 1, `this[0x11] = 1`, look-at
camera on, player invulnerable, no collision. M0 / M1 above. M78: camera + orbit setup, event 1, fx 15027. M80: laser
meshes + fx 15027. M81 (alien orbit): HUD off, player hidden, camera (-20000, 800, 120000), fx 15027 scaled. M89: dummy
camera target; enemy 0 at (-25000, 800, 120000) + 1000·light direction, then + (3000, 0, 3000); mesh 14229. M105
(supernova orbit): chase-style offset from enemy 1. M106 / M158: white fade-ins. M131 at station 112: fixed camera from
the chase pose. M144: dummy target facing (1, 0, -1), player hidden and frozen. M157 at its target: Valkyrie beam and
burning-station meshes 19061-19063 and 19095-19098, hidden. Interludes above. M78, M105, M131, M144, M158 set
`+0x20 = 0x100` (start sequence already over): the script owns the camera from the first frame.
