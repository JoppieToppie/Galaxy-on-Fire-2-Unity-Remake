# Sound

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Sound

The FMOD data comes from the FEV's LGCY chunk (`Reference/tools/audio/fev_lgcy.py`,
`Reference/research/fmod_event_ids.txt`: every system id -> event, group, .ogg files). Builders take clips by event id
where it matters (`WeaponBuilder.EventClips`: every weapon's shot event from `weapon_fx.json`'s `soundId`, all its
waves, `WeaponFx.Shot` / `ExplosionSound` pick one at random; the sentry deploy is 2263 for all three).

- **Volumes**: an event's own volume × `Sfx.EventGain` (4, the level the remake's older sounds already play at) for the
  sounds rebuilt from the FEV: the engines, boosts, NPC engines (46 0.076, freighters 47 0.195), the gamma loop, the
  collector loop, the ambiences, the alarm. The older SFX still play at hand-set levels.
- **Engine loops in space** (`EngineVoices`; every engine event 42-48 / 1104-1107 is 3D, linear rolloff from 1 to 10000 game
  units, the listener at the camera: `MGame::OnUpdate`, `Player::PlayEngineSound` / `Player::update`): NPC and other
  players' engines fade linearly from 0.05 to 500 m; the player's own engine (a 2D source) takes the same factor from the
  camera's distance (about 85 % behind the ship); the NPC events 46 / 47 / 48 play at most 3 each at once, the loudest
  (max_playbacks 3, behaviour 5), fading in 800 ms / out 200 ms. Before: every ship's loop audible to 1.5 km.
- **Parameters** (envelope values: pitch ×2^(8v − 4), volume linear): the drill (`DrillSound`, see "Mining"), the map
  whoosh (see "Star map"), the player engine (`PlayerEngine`: `PlayerEgo::PlayerEgo` picks 42-45 by handling, ships 42 /
  43 / 40 1104 / 1106 / 1107; pitch by the stick's larger axis ×0.891 → ×1.122, "load" never set = gain 0.7; the gamma
  shield's loop takes its place; paused while mining) and the boost sound (38-41 by booster, 1102 Polytron, on a boost's
  start).
- **Cycle events** (`CycleSound`: a self-running looping parameter with looped layers and oneshot adds at fixed points,
  random waves): 153 the mothership (level 42, "time" 0.5 from `LevelScript::process` state 6), 122 / 95 / 108 the
  station ambiences.
- **Volatile goods** (`VolatileCargo`, `PlayerEgo::update`): with 204 / 209 aboard the volatile force grows by stick
  changes (dt·0.0005·max|Δ|, counted per 30 fps frame), boosting (dt·0.001·0.13, Polytron 0.17) and asteroid hits
  (+0.2), the dodge (+0.17), a player bomb's ignition, every shot (+0.008) and every hit taken (+0.065); it decays
  0.025/s; at 1 the ship explodes; 35 Selfdestruct_Warning beeps by it (the spawn intensity envelope 0 below 0.2, then
  0.18 → 0.66, which libfmodevent turns into (e^(5.7865 v) − 1) · 0.3078: a beep every 1.7 s at 0.2, almost continuous
  at 1; +3 st); 3202 warns 5 s in (`FlightHints`).
- **Music rules** (`Traffic.UpdateMusic`, `Radar::draw` 0x157c6c): nothing switches while 143 IntroAtmo plays; at 0x91 a calm orbit keeps the battle track;
  single battle tracks can be turned off (Options > Sound, `MusicTracks`, see "Music tracks off").
- **Flight UI**: buttons in the pause / autopilot menus, the Menu button and the dialogue click 124 / 123; the pause
  menu's confirmation 126. Step 15's success page 1833 (or skipping it) stops the music for 136 and loops 162 Alert
  (`StationMenu.StartVoidAlarm`).
- Fixed from the audit: 141 after the time jump = Space_Battle_Medium looped (was TimeShift_Start), the gamma shields'
  loops (205 = 2261, 206 = 2260), the gas cloud collect = 2256 Mud1-4, 92's freighter jump 0x8c9, 105's bomb 14, the
  Void / Specter shots 62 / 2276, 144 OutroSong loops, 144/145's periodic explosion Med, space junk 22, the collector
  turret view 2255.
- 156 (the prologue wreck's broken engine) is a `CycleSound` (3.3 s cycle, random adds: no repeat, volume ×0.708..1,
  pitch ±0.025 octave, sound definition 1253); 158 Rumble_CutScene_01 is a 17.9 s oneshot (0.624), 161 Engine_09_Broken
  a loop at 0.115 (`CampaignLevel.PlayLoop` takes the event volume); 1116 (the Liberator) pitches its loop by the
  rocket's banking (`BombGun::update`: banking += dt·stick x·0.01, Vertical = ×0.2) and adds EngineDLC_06 from 0.336.
- **Weapon mod pitch** (`WeaponSystem.primaryPitch`, #76; `MGame::OnInitialize` -> `PlayerEgo::pitchAllPrimaryGuns(1 - Ship::getFireRateFactor())`,
  Gun+0xb0, `Player::playShootSound`'s raw FMOD pitch, raw x 4 = octaves): with a sort-28 weapon mod that speeds the
  fire rate the player's primary shots (and their loops) play 2^(4 x (attr 39 / 100)) higher: the Nirai Overdrive (+20
  %) 0.8 octaves; a slower one (the Overcharge, -10 %) is skipped like the original's negative value.
- **Shot voices** (`ShotVoices`): every gun shot (the player's, NPCs', other players' mirrored ones) plays through
  shared 2D voices, at most 2 per shot sound, a third restarting the newest (the FEV weapon events' max_playbacks 2,
  behaviour 1); shots away from the camera fall off like `Sfx.PlayAt`. Before, each shot was its own PlayOneShot and a
  few ships firing clipped ("crunchy"). The mirror sounds only one primary per shooter (`Player::calcWeaponSounds`: only
  the first primary gun makes the sound, also the player's own: a mixed loadout sounds slot 1, as the original; the
  Sound tab's "Every weapon's own shot sound" sounds the first gun of each primary item, mirrors too). Real voices 64
  (AudioManager).
- **Stereo pan** (#180, `Sfx.Pan`; the rule in `Sfx.PanLocal`, tested by `SfxPanTests`): `Sfx.PlayAt` and the shot
  voices pan by the sound's side of the camera (camera-space x / distance × `PanAmount` 0.85, never fully one-sided;
  centred ahead, behind and within 1 m), as the original's 3D events are heard from where they are.
- **Menu theme from the first logo** (#180, `SplashMusic`; MTitle starts 145 Space_NoCombat_Void with the first logo):
  the Splash scene starts it as it wakes (`StartupSplash`, see "UI and platforms"; it played from Unity's splash screen
  before, `BeforeSplashScreen`), also in the Editor when Play mode starts there (from MainMenu the menu starts it itself);
  not in batch mode or on a dedicated server (`DedicatedServer.Enabled`). A listener of its own until the menu's camera
  brings one (the splash has none: the drop waits for a scene with a listener). `Resources/GoF2Music/MenuTheme` is a
  `SplashTheme` asset that references `Audio/MUSIC/Space_NoCombat_Void.ogg` (it was a 1 MB copy of it: the clip shipped
  twice). `MainMenu` adopts the playing source as its music source (`SplashMusic.Take` moves it into the menu scene; it
  used to destroy it and seek a second stream to the same point: a gap or a doubling). A mod's menu theme fades in over
  it (`MainMenu.SwapMusic`: 1 s out, 1.5 s in; it cut in hard), also when it finishes loading later; the newest
  `FadeMusic` wins (leaving the menu stops a swap). Nothing else is heard while the logos show (2026-11-05,
  `StartupSplash.MuteAllButMusic`): every other `AudioSource` (the menu scene loading behind them, its backdrop orbit)
  is muted in `LateUpdate` as it appears and unmuted when the splash goes; muted rather than `AudioListener.pause`, which
  would hold the one-shots and play them all at once afterwards. Nothing else in the game sets `AudioSource.mute`.
- The rescue (index 1) plays the system race's space track (index 0's state 16 sets `Globals::switch_to_target_setting`:
  `playMusicAndFadeOutCurrent(1)`, Mido 137); 141 Space_Battle_Medium is the time jump's arrival only.
- Not yet: the generic event volumes of the older SFX; the loop waves' seams (many SFX loops end some samples past their
  clean loop point, a click per loop: Engine_Freighter_02, Engine_09).
