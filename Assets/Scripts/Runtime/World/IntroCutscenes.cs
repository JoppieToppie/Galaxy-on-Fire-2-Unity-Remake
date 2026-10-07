// IntroCutscenes.cs
// The new game's opening, campaign indices 0 and 1 (LevelScript::LevelScript 0x15e650 / process 0x160d50; positions and
// timings from the Thumb disassembly, Reference/research/levelscript_cutscenes.md 2, campaign_levels_a.md 3.1 / 3.2):
//   index 0, the prologue "3598 A.D. - Dareius Asteroid Belt" (orbit of Var Hastra, no station, intro sky): Keith's
//     Phantom (unkillable) finds three pirates hiding in the belt, the player fights them, then the hyperdrive fails:
//     a time jump (hyper_drive fx) out of the belt into Var Hastra's orbit, the broken ship tumbling while its oxygen runs
//     out; fade to black, nextCampaignMission, the flight level again
//   index 1, the rescue: the drifting Phantom, Gunant Breh's salvager (Midorian ship 30) closes in, his three radio lines,
//     fade to black, docked at Var Hastra (the first station conversation follows)
// The state number is the level-script event (radio trigger 27). Plain C#, run by CampaignLevel.
// The broken ship's smoke and fire (PlayerEgo::setLevel 0xa6f90, records 15 / 42) start at step 11 and never stop
// (Reference/research/prologue_particles.md A); created but never enabled in the rescue, so not built there.
// Skip (remake-only button, the flight HUD's Skip): MGame::OnTouchEnd 0x1a98d8 has skip branches for both indices that no
// button reaches in this build (levelscript_cutscenes.md 3): index 0 = nextCampaignMission + setKills(3) + the flight
// level again (the rescue), index 1 = straight to the station (module 5).
// Not reproduced: the player engine sound.

using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoF2Remake.World
{
    public class IntroCutscenes
    {
        const float M = 0.05f;

        readonly CampaignLevel campaign;
        readonly SpaceLevel level;
        readonly CutsceneCamera cam;
        readonly StoryAssets assets;
        readonly int index;
        float stepMs, playerSpeed = 2f;   // u/ms
        float fxMs, fxLength;
        bool soundsPlayed, loading, battleOver;
        float rescueTurn;
        GameObject fx;
        ShipSmoke smoke;

        ShipController Ship => level.Player;
        Transform Player => level.Player.transform;
        int Step { get => campaign.Event; set { campaign.Event = value; stepMs = 0f; } }

        static Vector3 ToUnity(Vector3 game) => new Vector3(game.x, game.y, -game.z) * M;
        static Vector3 Dir(Vector3 game) => new Vector3(game.x, game.y, -game.z).normalized;

        public IntroCutscenes(CampaignLevel campaignLevel, SpaceLevel spaceLevel, int storyIndex)
        {
            campaign = campaignLevel;
            level = spaceLevel;
            index = storyIndex;
            assets = StoryAssets.Load();
            cam = new CutsceneCamera(level.mainCamera);
        }

        public CutsceneCamera Camera => cam;

        /// <summary>The skip still does something (not already loading the next scene).</summary>
        public bool CanSkip => !loading;

        /// <summary>MGame::OnTouchEnd's skip branches (see the header).</summary>
        public void Skip()
        {
            if (loading) return;
            loading = true;
            if (index == 0)
            {
                Session.Kills = 3;   // setKills(3): the three pirates of the prologue
                Story.Advance(level.Database);
                Session.LaunchedFromStation = Session.ArrivedByTravel = false;
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            }
            else level.Dock();
        }

        // ---- set-up (Level::createCampaignMission cases 0 / 1 + the LevelScript constructor) ------------------------

        public void Build()
        {
            // Case 0: Player::setHitpoints(9 999 999) (the maximum too), not setVulnerable(false): the shield and armor still take
            // the pirates' hits, only the hull never runs out. The rescue has nothing to hit it.
            if (index == 0 && level.Health != null && level.Health.Hp != null)
            {
                var hp = level.Health.Hp;
                hp.hull = hp.maxHull = 9999999;
                level.Health.Target.hp = level.Health.Target.maxHp = hp.hull;
            }
            else campaign.PlayerInvulnerable = true;
            campaign.Cutscene = true;
            campaign.CollisionOff = true;
            campaign.StartSequenceOver = false;
            campaign.MusicOwned = true;
            Ship.externalControl = true;
            if (level.Weapons != null) level.Weapons.Blocked = true;
            if (index == 0) BuildPrologue(); else BuildRescue();
        }

        void BuildPrologue()
        {
            // Pirates: ships 2 Hiro, 23 Azov, 2 Hiro at (50 000, 50 000, 50 000), asleep and invisible, always-enemy, no loot,
            // HP 150, exhausts hidden.
            int[] ships = { 2, 23, 2 };
            foreach (int s in ships)
            {
                var p = campaign.SpawnShip(Standing.Pirate, s, new Vector3(50000, 50000, 50000), false,
                                           spec => { spec.inactive = true; spec.alwaysEnemy = true; spec.noLoot = true; spec.hitpoints = 150; });
                p.SetVisible(false);
                p.SetExhaust(false);
                p.SetEngineSound(false);   // silent while they hide in the belt
            }
            // Player at (0, 0, -60 000) facing +Z, computer-controlled; camera (-1000, -500, -40 000) looking at it.
            Player.SetPositionAndRotation(ToUnity(new Vector3(0, 0, -60000)), Quaternion.LookRotation(Dir(new Vector3(0, 0, 1)), Vector3.up));
            cam.LookAt(new Vector3(-1000, -500, -40000), Player);
            smoke = new ShipSmoke(Ship.visualModel != null ? Ship.visualModel : Player);
            campaign.PlayMusic(assets?.introAtmo, true);   // MenuTouchWindow::startGOF2: 143 IntroAtmo
            Step = 0;
        }

        void BuildRescue()
        {
            // The salvager: Midorian ship 30 at (300, 50, -6000), heading for the Phantom, asleep, engines off.
            var g = campaign.SpawnShip(3, 30, new Vector3(300, 50, -6000), false, spec => { spec.inactive = true; spec.alwaysFriend = true; spec.nameText = 1599; });
            g.Place(ToUnity(new Vector3(300, 50, -6000)), Dir(new Vector3(0, 0, 1)));
            g.SetExhaust(false);
            g.SetEngineSound(false);
            // Player frozen at the origin, rotation (0.462, 0.462, 1.5339) rad, exhaust off; camera (1500, 1600, -3000).
            Player.SetPositionAndRotation(Vector3.zero, OrbitLayout.RotationToUnity(new Vector3(0.462f, 0.462f, 1.5339f)));
            SetPlayerExhaust(false);
            playerSpeed = 0f;
            cam.LookAt(new Vector3(1500, 1600, -3000), Player);
            campaign.Fade(true, Color.black, 5000f, fromOpaque: true);
            // Index 0's state 16 (and its skip) sets Globals::switch_to_target_setting before the new level, so
            // MGame::OnInitialize calls playMusicAndFadeOutCurrent(1): 141 stops and the system race's space track plays
            // (DAT_00252010; Mido: 137 Space_Nocombat_Midorianer). No campaign-1 rule there: the radar's IntroAtmo for
            // campaign 1 never runs, the cutscene flag stays set.
            campaign.PlayMusic(level.Traffic != null ? level.Traffic.RaceSpaceMusic() : assets?.timeShift, true);
            Step = 0;
        }

        void SetPlayerExhaust(bool on)
        {
            var asm = Ship.visualModel != null ? Ship.visualModel.GetComponent<AssembledObject>() : null;
            asm?.SetExhaust(on, true);
        }

        void SetPlayerVisible(bool on)
        {
            if (Ship.visualModel != null) Ship.visualModel.gameObject.SetActive(on);
        }

        bool Over(int line) => campaign.Radio != null && campaign.Radio.Over(line);
        bool Triggered(int line) => campaign.Radio != null && campaign.Radio.Triggered(line);

        // ---- per frame ----------------------------------------------------------------------------------------

        CycleSound brokenEngine;

        public void Tick(float dtMs)
        {
            brokenEngine?.Update(dtMs);
            stepMs += dtMs;
            if (fx != null) fxMs += dtMs;
            if (index == 0) TickPrologue(dtMs); else TickRescue(dtMs);
            // Computer-controlled flight: straight on at the script's speed.
            if (Ship.externalControl)
            {
                Player.position += Player.forward * playerSpeed * dtMs * M;
                Ship.ExternalSpeedMetersPerSecond = playerSpeed * 1000f * M;
            }
        }

        public void LateTick(float dtMs)
        {
            cam.LateTick(dtMs);
<<<<<<< Updated upstream
            // LevelScript state 7: setDirection(MatrixGetDir(the camera), (0, 1, 0)). The engine's camera looks down its -z
            // (MatrixGetLookAt: dir = eye - target), so the fx's +z points at the viewer; the camera's own rotation (+z away)
            // showed it mirrored, spinning the wrong way.
            if (fx != null && cam.Camera != null) fx.transform.rotation = cam.Camera.rotation * Quaternion.Euler(0f, 180f, 0f);
=======
            if (fx != null && cam.Camera != null) fx.transform.rotation = cam.Camera.rotation * Quaternion.Euler(0f, 180f, 0f);   // billboarded to the camera (+180 Y)
>>>>>>> Stashed changes
        }

        /// <summary>Remake: the room the pirates' hiding place keeps clear of asteroids, past the farthest pirate (150 m; the
        /// cutscene camera beside them is inside it too).</summary>
        const float PirateBubbleClearUnits = 3000f;

        /// <summary>Remake: the belt is random (OrbitBuilder.SpawnAsteroids), so an asteroid could sit right where the pirates
        /// appear, the ships inside it: every asteroid reaching into a bubble around the three spots goes before they show.</summary>
        void ClearPirateBubble(Vector3[] gameSpots)
        {
            if (level.Asteroids == null || gameSpots.Length == 0) return;
            var centre = Vector3.zero;
            foreach (var p in gameSpots) centre += ToUnity(p);
            centre /= gameSpots.Length;
            float reach = 0f;
            foreach (var p in gameSpots) reach = Mathf.Max(reach, Vector3.Distance(centre, ToUnity(p)));
            float bubble = reach + PirateBubbleClearUnits * M;
            var doomed = new System.Collections.Generic.List<GameObject>();
            foreach (Transform a in level.Asteroids)
            {
                var t = a.GetComponent<Target>();
                float size = t != null ? t.radius / 0.7f : 0f;   // the hit radius is 0.7 x the mesh radius
                if (Vector3.Distance(a.position, centre) - size < bubble) doomed.Add(a.gameObject);
            }
            foreach (var go in doomed) Object.Destroy(go);
        }

        void TickPrologue(float dtMs)
        {
            var pirates = campaign.Ships;
            switch (Step)
            {
                case 0:
                    if (Over(2))
                    {
                        // 0x160f0e: the player to (18000, -12000, -40000), the camera to (-12000, 2000, -500), still looking at
                        // the player (no setTarget): just behind the pirates, who talk in the foreground while Keith's ship
                        // passes in the distance (levelscript_cutscenes.md had -3000 for both -12000s, which put them behind
                        // the camera).
                        Player.position = ToUnity(new Vector3(18000, -12000, -40000));
                        cam.LookAt(new Vector3(-12000, 2000, -500), Player);
                        Vector3[] at = { new Vector3(-10000, 500, 0), new Vector3(-10000, -300, -1700), new Vector3(-10000, -200, 2000) };
                        ClearPirateBubble(at);
                        for (int i = 0; i < 3 && i < pirates.Count; i++)
                        {
                            pirates[i].Place(ToUnity(at[i]), Vector3.right);   // facing +X
                            pirates[i].SetVisible(true);
                        }
                        Step = 1;
                    }
                    break;
                case 1:
                case 2:
                    // While step < 3 the pirates drift: every (30 fps) frame translate(0, getPulseValue(0.0005) - 0.5, 0), the
                    // pulse |sin(playing time ms * 0.0005)| (Layout::getPulseValue 0xe74fc; 0x1614ac).
                    {
                        float pulse = Mathf.Abs(Mathf.Sin(Session.PlaySeconds * 1000f * 0.0005f));
                        float dy = (pulse - 0.5f) * dtMs / (1000f / 30f) * M;
                        for (int i = 0; i < 3 && i < pirates.Count; i++) pirates[i].transform.position += Vector3.up * dy;
                    }
                    if (Step == 1 && Over(6))
                    {
                        if (pirates.Count > 0) cam.LookAt(new Vector3(-5000, 300, -5000), pirates[0].transform);
                        campaign.PlayMusic(assets?.battleFull, true);   // music stop, 142
                        cam.SetDolly(new Vector3(0.2f, 0f, 2.2f));
                        Step = 2;
                    }
                    else if (Step == 2 && Over(7))
                    {
                        foreach (var p in pirates) { p.SetExhaust(true); p.SetEngineSound(true); p.SetVisible(true); p.Wake(); }
                        cam.SetDolly(Vector3.zero);
                        Step = 3;
                    }
                    break;
                case 3:
                    if (Over(8))
                    {
                        // Player control: radar, collision, HUD; the start sequence is over (the steering briefing opens).
                        Ship.externalControl = false;
                        if (level.Weapons != null) level.Weapons.Blocked = false;
                        campaign.Cutscene = false;
                        campaign.CollisionOff = false;
                        campaign.StartSequenceOver = true;
                        cam.Release();
                        Step = 4;
                    }
                    break;
                case 4:
                    // Radar::draw (the fight isn't a cutscene): a dying pirate no longer counts (KIPlayer::isDying), and with
                    // no hostile ship left 142 isn't in the battle set, so the system's calm track takes over until 143.
                    if (!battleOver && pirates.Count > 0 && pirates.TrueForAll(p => p == null || p.Gone || p.Current == NpcShip.State.Dying || p.Current == NpcShip.State.Dead))
                    {
                        battleOver = true;
                        if (level.Traffic != null) campaign.PlayMusic(level.Traffic.CalmClip(), true);
                    }
                    if (Over(10))
                    {
                        // The pirates are dead: back to the cutscene, guns off, facing +X, camera out to the side.
                        campaign.PlayMusic(assets?.introAtmo, true);
                        campaign.Cutscene = true;
                        campaign.CollisionOff = true;
                        Ship.externalControl = true;
                        playerSpeed = 2f;
                        if (level.Weapons != null) level.Weapons.Blocked = true;
                        Player.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
                        cam.LookAtUnity(Player.position + ToUnity(new Vector3(25000, -200, -1000)), Player);
                        soundsPlayed = false;
                        Step = 5;
                    }
                    break;
                case 5:
                    if (!soundsPlayed && Over(12))
                    {
                        // "Activating hyperdrive": the explosion and rumble at the ship, the broken engine, slowing down.
                        soundsPlayed = true;
                        Sfx.PlayAt(assets?.cutsceneExplosion, Player.position);
                        campaign.PlayLoop(0, assets?.rumble, 0.624f, false);   // 158 Rumble_CutScene_01: a 17.9 s oneshot
                        campaign.PlayLoop(1, assets?.engineBroken, 0.115f);    // 161 Engine_09_Broken, looped
                    }
                    if (soundsPlayed) playerSpeed *= Mathf.Pow(0.98f, dtMs / 33.3f);
                    if (Over(13)) cam.SetDolly(new Vector3(-0.2f, 0f, 0.3f));
                    if (Triggered(14)) Step = 6;
                    break;
                case 6:
                    // (the x0.98 per frame slowdown is state 5's only: the speed holds from here)
                    cam.Rumble = Mathf.Min(1f, stepMs / 4000f);
                    if (Over(15))
                    {
                        SpawnFx(Player.position);
                        Sfx.PlayAt(assets?.timeJump, Player.position);   // 160
                        campaign.StopLoop(1);
                        Step = 7;
                    }
                    break;
                case 7:
                    if (stepMs >= 2000f && Ship.visualModel != null && Ship.visualModel.gameObject.activeSelf)
                    {
                        campaign.StopLoop(0);
                        SetPlayerVisible(false);
                        cam.Rumble = 0f;
                        playerSpeed = 0f;
                    }
                    if (fx == null || fxMs >= fxLength)
                    {
                        campaign.PlayMusic(assets?.timeShift, true);   // LevelScript::process state 7: music 141 Space_Combat_Mid
                        Step = 8;
                    }
                    break;
                case 8:
                    cam.Rumble = Mathf.Max(1f - stepMs / 3000f, 0f);
                    if (stepMs >= 4000f)
                    {
                        HideFx();
                        Step = 9;
                    }
                    break;
                case 9:
                {
                    // Out of the belt: the intro sky and planet switch, the asteroids gone; the ship at the origin.
                    level.RestoreOrbitSky();   // Level::switchSkyboxForIntro: skybox_009, the system's own sky
                    level.Backdrop?.SwitchOrbitPlanetForIntro();
                    if (level.Asteroids != null) Object.Destroy(level.Asteroids.gameObject);
                    // LevelScript state 9 (0x167bd0): setDirection(parent, (0, 0, -1)) moves only the drifting parent node,
                    // then PlayerEgo::rotate(pi/4, pi/4, pi/4) turns the ship node in it (Tumble).
                    Player.SetPositionAndRotation(Vector3.zero, Quaternion.LookRotation(Dir(new Vector3(0, 0, -1)), Vector3.up));
                    wreckEuler = Vector3.zero;
                    Ship.modelHeld = true;   // ShipController's computer-controlled levelling leaves the model alone
                    Tumble(Mathf.PI / 4f);
                    cam.LookAt(new Vector3(5000, 500, -10000), Player);
                    Step = 10;
                    break;
                }
                case 10:
                    cam.Rumble = Mathf.Min(1f, stepMs / 2000f);
                    if (stepMs >= 2000f)
                    {
                        Sfx.PlayAt(assets?.timeJumpEnd, Player.position);   // 159
                        SpawnFx(Player.position);
                        Step = 11;
                    }
                    break;
                case 11:
                    cam.Rumble = Mathf.Max(1f - stepMs / 2500f, 0f);
                    if (stepMs >= 2500f)
                    {
                        // The broken ship drifts out: visible, broken engine, no exhaust, speed 2.
                        SetPlayerVisible(true);
                        SetPlayerExhaust(false);
                        if (brokenEngine == null && assets != null)
                            brokenEngine = CycleSound.BrokenEngine(campaign.gameObject, assets.engineBrokenLoop, assets.engineBrokenAdds);
                        brokenEngine?.Play();   // 156 with its oneshot layer
                        smoke?.SetEmitting(true);   // PlayerEgo::startSmokeEmission 0xadc20
                        playerSpeed = 2f;
                        cam.Rumble = 0f;
                        Step = 12;
                    }
                    break;
                case 12:
                    Tumble(dtMs / 3000f);
                    if (Over(17)) { cam.LookAtUnity(Player.position + ToUnity(new Vector3(-2000, -2000, -5000)), Player); Step = 13; }
                    break;
                case 13:
                    Tumble(dtMs / 4000f);
                    if (stepMs >= 6000f) { cam.LookAtUnity(Player.position + ToUnity(new Vector3(700, 0, -1700)), Player); Step = 14; }
                    break;
                case 14:
                    Tumble(dtMs / 5000f);
                    cam.SetDolly(new Vector3(-2f, 0f, 0f));
                    if (stepMs >= 12000f) { cam.LookAtUnity(Player.position + ToUnity(new Vector3(-3000, 3500, -6700)), Player); Step = 15; }
                    break;
                case 15:
                    if (Over(22)) { campaign.Fade(false, Color.black, 5000f); Step = 16; }
                    break;
                case 16:
                    if (!loading && campaign.FadeDone)
                    {
                        // nextCampaignMission (-> 1) and a new flight level: the rescue.
                        loading = true;
                        Story.Advance(level.Database);
                        Session.LaunchedFromStation = Session.ArrivedByTravel = false;
                        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                    }
                    break;
            }
        }

        void TickRescue(float dtMs)
        {
            // The Phantom turns slowly: rotate(0, a, a), a = (2dt & ~15) / 65536 * 2pi. PlayerEgo::rotate 0xad980 adds to
            // the stored Euler angles (+0x2e8..+0x2f0) and rebuilds the matrix (setRotation, Rx*Ry*Rz), so the pose is
            // (0.462, 0.462 + t, 1.5339 + t); t grows at the 30 fps rate (64 per 33.3 ms frame), so it also turns at
            // high frame rates (below 8 ms a frame the quantised step is 0). The camera drifts (0.1dt, 0, 0), the
            // salvager slows as it arrives (0.2dt * min(z / -5000, 1)).
            rescueTurn += dtMs * (64f / (1000f / 30f)) / 65536f * 2f * Mathf.PI;
            Player.rotation = OrbitLayout.RotationToUnity(new Vector3(0.462f, 0.462f + rescueTurn, 1.5339f + rescueTurn));
            cam.SetDolly(new Vector3(0.1f, 0f, 0f));
            if (campaign.Ships.Count > 0)
            {
                var g = campaign.Ships[0].transform;
                float z = -g.position.z / M;   // game z
                g.position += g.forward * (0.2f * dtMs * Mathf.Min(z / -5000f, 1f)) * M;
            }
            switch (Step)
            {
                case 0:
                    if (Over(2)) { campaign.Fade(false, Color.black, 5000f); Step = 1; }
                    break;
                case 1:
                    if (!loading && campaign.FadeDone)
                    {
                        // Module 5: docked at Var Hastra; the index-1 mission completes there (the first conversation).
                        loading = true;
                        level.Dock();
                    }
                    break;
            }
        }

        /// <summary>The drifting wreck turns: PlayerEgo::rotate 0xad980 (rotate(dt / 3000) ... on each axis) adds to the ship
        /// node's stored Euler angles (+0x2e8..+0x2f0) and rebuilds its local matrix Rx*Ry*Rz, inside the parent that
        /// drifts along game -z. Only the model turns, so the drift stays straight.</summary>
        void Tumble(float rad)
        {
            wreckEuler += Vector3.one * rad;
            // The model's local rotation: the parent (game -z = Unity identity here) times the node's game rotation, i.e.
            // Y180 * RotationToUnity(e) (RotationToUnity undoes the import's 180 deg yaw, the parent's own turn adds it back).
            var local = Quaternion.Euler(0f, 180f, 0f) * OrbitLayout.RotationToUnity(wreckEuler);
            if (Ship.visualModel != null) Ship.visualModel.localRotation = local;
            else Player.rotation = Quaternion.LookRotation(Dir(new Vector3(0, 0, -1)), Vector3.up) * local;
        }

        Vector3 wreckEuler;   // PlayerEgo+0x2e8..+0x2f0 (game radians)

        void SpawnFx(Vector3 at)
        {
            HideFx();
            if (assets == null || assets.hyperDrive == null) { fxLength = 2000f; fxMs = 0f; return; }
<<<<<<< Updated upstream
            fx = Object.Instantiate(assets.hyperDrive, at, (cam.Camera != null ? cam.Camera.rotation : Quaternion.identity) * Quaternion.Euler(0f, 180f, 0f));   // +z at the viewer (LateTick)
=======
            fx = Object.Instantiate(assets.hyperDrive, at, (cam.Camera != null ? cam.Camera.rotation : Quaternion.identity) * Quaternion.Euler(0f, 180f, 0f));
>>>>>>> Stashed changes
            GunRig.EnableFades(fx);   // its parts fade out by their `extra` channel (0 at 3000 ms); without it the fx froze, then vanished
            float len = PartAnimation.PlayOnce(fx);
            fxLength = len > 0f ? len : 3000f;
            fxMs = 0f;
        }

        void HideFx()
        {
            if (fx != null) Object.Destroy(fx);
            fx = null;
        }
    }
}
