// MainCampaignLevels.cs
// The main campaign's in-space levels after the tutorial (Level::createCampaignMission 0xc3370 + LevelScript::process
// 0x160d50; Reference/research/campaign_levels_a.md 3.5-3.16, levelscript_cutscenes.md 2). Plain C#, run by CampaignLevel.
// Ships are Level+0xf8 slots in the original's order (the radio triggers and objectives index them); the step number is
// the level-script event (radio trigger 27).
//   14 Kernstal     pirates in a Betty, the Terran navy (2 Inflicts, 2 battleships): the first kill -> EMP'd and arrested
//                   (flash, the ship stops and turns, the cruiser leaves), nextCampaignMission -> docked at Alioth (98)
//   16 Alioth       Void fighters (x10 hull, they ignore the player) raid three parked Terran freighters, Brent's three
//                   Inflicts help; a cutscene on the freighters, then (the freighters dead) the Void flee into the wormhole;
//                   won when the last radio line is over
//   21 Kappa        three Terran scouts on patrol (hostile once hit or once the route is passed), the Hijacker (Ward) at the
//                   second waypoint: disable him with EMP (the last line) - destroying him fails
//   24 Sahi         Void fighters hunt two Nivelian freighters; 3 t of their remains from crates -> the wormhole opens in
//                   front of the player and swallows him (the ride advances to 25)
//   25 / 26 Void    the Void home orbit: three Void fighters; at 26 the wormhole reopens (line 1), the ride back to Sahi,
//                   where 26 is two Void pursuers behind the player (both dead = won)
//   28 Dima         the wormhole at (100000, -50000, -40000), five Void fighters, three Terran freighters: fly in (-> 29)
//   29 Void         lock the Void station: the probe launch (cutscene), then survive 180 s (the HUD counts down)
//   36 B'akka       the kill contest with Errkt (H'Soc, speed 3, on the player's 4-waypoint route): 7 sleeping pirates;
//                   won with more kills than Errkt, failed with as many or fewer
//   38 Dekato       two parked Nivelian freighters, five Midorian fighters: kill them before both freighters die
//   40 invasion     Errkt's freighter (Vossk, 5 * level + 1800) comes out at 40 s and flies (+Z) into the wormhole (which
//                   swallows it: its distance past the hole doubles every frame; then Keith's 2048), the
//                   player's orbit    Terran fighters (Jean Baffour) help, 4 + 4 reserve Void fighters; the wormhole never closes; entering
//                   it after the freighter went through carries its hull into 41
//   41 Void         the escort to the mother ship: seven Void fighters, three more at the freighter near z -100000, its
//                   engine hit (Errkt_CutSeq), it drifts for 15 s, then parked; 15 s later its hull is 100 and the level won
//   42 (41's level) the player finishes the freighter; the mother ship's explosion loop, the wormhole opens at (25000,
//                   20000, -55000); inside it: the explosion cutscene (station gone after 4 s, fade out at 15 s), then the
//                   ride out to the station the player came from
// Floats the decompiler lost (campaign_levels_a.md 4) and the remake picked: the index-24 wormhole 30000 ahead (moved off
// the station when that point is inside it), the
// index-26 pursuers 10000 behind (where the arrival wormhole sits), the cutscene camera offsets of 24 / 29 / 40 / 41.

using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public class MainCampaignLevels
    {
        const float M = 0.05f;

        readonly CampaignLevel c;
        readonly SpaceLevel level;
        readonly CutsceneCamera cam;
        readonly StoryAssets assets;
        int built;
        float stepMs, playerSpeed;   // u/ms while the script flies the ship
        bool loading, won41, objectivesRemoved;
        EmpSparks playerSparks;
        ShipSmoke freighterSmoke;
        GameObject probe;
        GameObject[] explosion;
        Route friendRoute;

        /// <summary>Level::connectPlayers: ships of this race leave the player out of their enemy list (16 / 24 / 28: Void).</summary>
        public int PlayerExemptRace { get; private set; } = -99;

        public MainCampaignLevels(CampaignLevel campaignLevel, SpaceLevel spaceLevel)
        {
            c = campaignLevel;
            level = spaceLevel;
            assets = StoryAssets.Load();
            cam = new CutsceneCamera(level.mainCamera);
        }

        Transform Player => level.Player.transform;
        ShipController Ship => level.Player;
        Wormhole Hole => level.Wormhole;
        int Step { get => c.Event; set { c.Event = value; stepMs = 0f; } }
        NpcShip S(int i) => i >= 0 && i < c.Ships.Count ? c.Ships[i] : null;
        bool Over(int line) => c.Radio != null && c.Radio.Over(line);
        bool Triggered(int line) => c.Radio != null && c.Radio.Triggered(line);

        static Vector3 ToUnity(Vector3 game) => new Vector3(game.x, game.y, -game.z) * M;
        static Vector3 ToGame(Vector3 unity) => new Vector3(unity.x, unity.y, -unity.z) / M;
        static Vector3 Dir(Vector3 game) => new Vector3(game.x, game.y, -game.z).normalized;
        Vector3 PlayerGame => ToGame(Player.position);
        Vector3 PlayerDirGame => new Vector3(Player.forward.x, Player.forward.y, -Player.forward.z);
        static int R(int n) => Random.Range(0, n);

        /// <summary>A freighter / battleship (Level::createShip kind 1, PlayerFixedObject), not moving by default.</summary>
        NpcShip Freighter(int race, int ship, Vector3 at, bool jitter, System.Action<SpawnSpec> setup = null) =>
            c.SpawnShip(race, ship, at, jitter, s => { s.freighter = true; s.stationary = true; s.group = NpcGroup.Special; setup?.Invoke(s); });

        /// <summary>Level::createShip's hull for kind 1 (the freighters' max HP / n).</summary>
        static int FreighterHull(int ship, int div) => Mathf.Max(1, NpcTables.Hull(1, ship) / div);

        // ---- building (Level::createCampaignMission) ------------------------------------------------------------

        public bool Build(int index)
        {
            built = index;
            switch (index)
            {
                case 14: Build14(); return true;
                case 16: Build16(); return true;
                case 21: Build21(); return true;
                case 24: Build24(); return true;
                case 25: case 29: BuildVoidOrbit(); return true;
                case 26: Build26(); return true;
                case 28: Build28(); return true;
                case 36: Build36(); return true;
                case 38: Build38(); return true;
                case 40: Build40(); return true;
                case 41: Build41(); return true;
                default: return false;
            }
        }

        void Build14()
        {
            // Enemy route (40000, 5000, 30000); the player at (40000, 0, 120000) (facing the waypoint, assumed).
            var w = new Vector3(40000, 5000, 30000);
            var route = new Route(false);
            route.points.Add(w);
            var start = new Vector3(40000, 0, 120000);
            level.MovePlayer(ToUnity(start), Quaternion.LookRotation(Dir(w - start), Vector3.up));
            for (int i = 0; i < 3; i++) c.SpawnShip(Standing.Pirate, 0, w, true, s => s.route = route);   // pirates in a Betty
            for (int i = 0; i < 2; i++) c.SpawnShip(0, 5, w, true, s => s.route = route);                // Terran Inflicts
            Freighter(0, 14, w + new Vector3(7000, 0, 0), false);                                        // battleships
            Freighter(0, 14, w + new Vector3(-9000, 2000, 7000), false);
            c.WinObjective = null;   // Objective 0x1f: never, the script ends it
        }

        void Build16()
        {
            var w = new Vector3(0, 0, 170000);
            // [0]-[2] Terran freighters, always-friend, parked, max HP / 3 ([0] once more / 6: / 18), no loot.
            Freighter(0, 15, w, false, s => { s.alwaysFriend = true; s.noLoot = true; s.hitpoints = FreighterHull(15, 18); });
            Freighter(0, 15, w + new Vector3(3000, 2000, -3000), false, s => { s.alwaysFriend = true; s.noLoot = true; s.hitpoints = FreighterHull(15, 3); });
            Freighter(0, 15, w + new Vector3(-9000, -8000, -7000), false, s => { s.alwaysFriend = true; s.noLoot = true; s.hitpoints = FreighterHull(15, 3); });
            // [3]-[6] Void fighters, max HP x 10.
            for (int i = 0; i < 4; i++)
                c.SpawnShip(Standing.Void, 8, w + new Vector3(R(20000) - 10000, R(20000) - 10000, 50000), false, s => s.hitpoints = NpcTables.Hull(0, 8) * 10);
            // [7]-[9] Brent's Inflicts beside the player, always-friend, 600 hull.
            var p = PlayerGame;
            for (int i = 0; i < 3; i++)
                c.SpawnShip(0, 5, p + new Vector3(R(4000) - 2000, R(3400) - 1700, 2000 + R(4000) - 2000), false, s => { s.alwaysFriend = true; s.hitpoints = 600; });
            if (Hole != null) { Hole.SetPosition(w + new Vector3(0, 0, 40000)); Hole.ResetTimer(false); Hole.SetVisible(true); }
            PlayerExemptRace = Standing.Void;
            // Radar::draw: a fight at campaign 0x10 plays 136 Space_Combat_Void (Traffic.UpdateMusic), not the race's tracks.
            c.WinObjective = () => c.Radio != null && c.Radio.LastOver;   // Objective 0x16
        }

        void Build21()
        {
            var route = new Route(false);
            route.points.Add(new Vector3(40000, -40000, 120000));
            route.points.Add(new Vector3(-10000, 20000, 190000));
            c.SetPlayerRoute(route);
            var a = new Route(true);
            a.points.Add(new Vector3(40000, -40000, 120000));
            var b = new Route(true);
            b.points.Add(new Vector3(-10000, 20000, 190000));
            // [0] the Hijacker (Terran Ward, always-enemy) at the second waypoint, [1]-[3] Terran scouts on patrol; all asleep.
            c.SpawnShip(0, 17, b.points[0] + new Vector3(1000, 0, 2000), false, s => { s.alwaysEnemy = true; s.route = b; s.nameText = 1611; s.asleep = true; });
            for (int i = 0; i < 3; i++) c.SpawnShip(0, 5, a.points[0], true, s => { s.route = a; s.asleep = true; });
            c.WinObjective = () => c.Radio != null && c.Radio.LastOver;   // 0x16: the last line (the Hijacker disabled)
            c.FailObjective = () => c.ShipDestroyed(0);                          // 7 (1): the Hijacker destroyed
        }

        void Build24()
        {
            var route = new Route(false);
            route.points.Add(new Vector3(100000, 0, 0));
            route.points.Add(new Vector3(100000, 0, -30000));
            for (int i = 0; i < 3; i++) c.SpawnShip(Standing.Void, 8, route.points[0], true, s => s.route = route);
            for (int i = 0; i < 2; i++) Freighter(2, 15, route.points[0], true, s => { s.noLoot = true; s.hitpoints = FreighterHull(15, 3); });
            Hole?.SetVisible(false);
            PlayerExemptRace = Standing.Void;
            c.WinObjective = null;   // 0x1f: the wormhole ends it
        }

        /// <summary>Indices 25 / 29 in the alien orbit: three Void fighters at (+-(20000 + rand(80000))) per axis.</summary>
        void BuildVoidOrbit()
        {
            float Sign() => R(2) == 0 ? 1f : -1f;
            for (int i = 0; i < 3; i++)
                c.SpawnShip(Standing.Void, 8, new Vector3(Sign() * (20000 + R(80000)), Sign() * (20000 + R(80000)), Sign() * (20000 + R(80000))), false);
        }

        void Build26()
        {
            // The pursuers out of the wormhole behind the player (player - direction * k +- 700).
            var p = PlayerGame - PlayerDirGame * 10000f;
            for (int i = 0; i < 2; i++) c.SpawnShip(Standing.Void, 8, p + new Vector3(R(1400) - 700, R(1400) - 700, R(1400) - 700), false);
            c.WinObjective = () => c.DeadRange(0, 2);   // Objective 7 (2)
        }

        void Build28()
        {
            var w = new Vector3(100000, -50000, -40000);
            if (Hole != null) { Hole.SetPosition(w); Hole.ResetTimer(false); Hole.SetVisible(true); }
            for (int i = 0; i < 5; i++) c.SpawnShip(Standing.Void, 8, w, true);
            for (int i = 0; i < 3; i++) Freighter(0, 15, w, true, s => { s.noLoot = true; s.hitpoints = FreighterHull(15, 4); });
            PlayerExemptRace = Standing.Void;
        }

        void Build36()
        {
            var route = new Route(false);
            route.points.Add(new Vector3(110000, -10000, -80000));
            route.points.Add(new Vector3(70000, 0, -100000));
            route.points.Add(new Vector3(-100000, 10000, -80000));
            route.points.Add(new Vector3(-130000, -50000, -150000));
            c.SetPlayerRoute(route);
            // [0] Errkt Uggut: Vossk H'Soc beside the player, speed 3, unkillable, on the player's route.
            c.SpawnShip(1, 9, PlayerGame + new Vector3(R(1400) - 700, R(1400) - 700, 1000), false,
                        s => { s.speed = 3f; s.hitpoints = 9999999; s.route = route.Clone(); s.alwaysFriend = true; s.nameText = 1604; s.noLoot = true; });
            // [1]-[7] pirates asleep at random waypoints.
            for (int i = 0; i < 7; i++)
                c.SpawnShip(Standing.Pirate, NpcTables.RandomFighter(Standing.Pirate), route.points[R(route.points.Count)], true, s => s.asleep = true);
            c.WinObjective = () => c.DeadRange(1, 8) && c.NpcKills < c.PlayerKills;    // 0x14 (1, 8)
            c.FailObjective = () => c.DeadRange(1, 8) && c.NpcKills >= c.PlayerKills;  // 0x15 (1, 8)
        }

        void Build38()
        {
            var w = new Vector3(90000, 10000, 80000);
            for (int i = 0; i < 2; i++)
                Freighter(2, 15, w + new Vector3(R(20000) - 10000, R(20000) - 10000, R(20000) - 10000), false, s => s.alwaysFriend = true);
            for (int i = 0; i < 5; i++) c.SpawnShip(3, NpcTables.RandomFighter(3), w, true, s => s.alwaysEnemy = true);
            c.WinObjective = () => c.DeadRange(2, 7);    // 0x12 (2, 7)
            c.FailObjective = () => c.DeadRange(0, 2);   // 7 (2): both freighters
        }

        void Build40()
        {
            var e = new Vector3(-20000, -3000, 200000);
            friendRoute = new Route(false);
            friendRoute.points.Add(new Vector3(-20000, -3000, 35000));
            friendRoute.points.Add(e);
            if (Hole != null) { Hole.SetPosition(e); Hole.ResetTimer(false); Hole.SetVisible(true); }
            if (level.StreamOutArrival)
                level.MovePlayer(ToUnity(new Vector3(-105000, 0, 80000)), OrbitLayout.RotationToUnity(new Vector3(0f, Mathf.PI / 2f, 0f)));
            int hull = 5 * Session.Rank + 1800;
            // [0] Errkt's freighter (the Vossk freighter), parked far away, hidden and asleep until his call.
            var f = Freighter(1, 13, new Vector3(-9999999, -9999999, -9999999), false,
                              s => { s.alwaysFriend = true; s.nameText = 1604; s.hitpoints = hull; s.inactive = true; s.noLoot = true; });
            f.SetVisible(false);
            // [1]-[4] Terran fighters on the friend route ([2] Jean Baffour), [5]-[8] Void at the wormhole, [9]-[12] in reserve.
            for (int i = 1; i <= 4; i++)
            {
                int k = i;
                c.SpawnShip(0, NpcTables.RandomFighter(0), friendRoute.points[0], true, s => { s.route = friendRoute; s.alwaysFriend = true; s.nameText = k == 2 ? 1605 : -1; });
            }
            for (int i = 0; i < 4; i++) c.SpawnShip(Standing.Void, 8, e, true, s => s.alwaysEnemy = true);
            for (int i = 0; i < 4; i++) c.SpawnShip(Standing.Void, 8, new Vector3(-500000, -500000, -500000), false, s => { s.alwaysEnemy = true; s.inactive = true; });
            c.FailObjective = () => c.ShipDestroyed(0);   // 7 (1): the freighter destroyed
        }

        void Build41()
        {
            level.MovePlayer(ToUnity(new Vector3(3000, 2000, -320000)), OrbitLayout.RotationToUnity(Vector3.zero));
            int max = 5 * Session.Rank + 1800;
            int hull = Session.LastFreighterHull > 0 ? Session.LastFreighterHull : (int)(0.7f * max);
            var f = c.SpawnShip(1, 13, new Vector3(0, 0, -300000), false,
                                s => { s.freighter = true; s.group = NpcGroup.Special; s.alwaysFriend = true; s.nameText = 1604; s.hitpoints = max; s.noLoot = true; });
            f.SetHull(hull, false);
            Vector3[] at =
            {
                new Vector3(0, 0, -260000), new Vector3(-10000, 10000, -240000), new Vector3(13000, 2000, -220000), new Vector3(-72000, -4000, -210000),
                new Vector3(60000, -40000, -190000), new Vector3(-18000, 30000, -160000), new Vector3(17000, 40000, -140000),
            };
            foreach (var p in at) c.SpawnShip(Standing.Void, 8, p, false, s => s.alwaysEnemy = true);
            c.WinObjective = () => won41;            // 0x19 (0): the script's flag on the freighter
            c.FailObjective = () => c.ShipDestroyed(0);   // 7 (1)
        }

        // ---- cutscene helpers (the "enter" / "leave" sequences, campaign_levels_a.md 1.5) ------------------------------

        /// <summary>'invulnerable' false at 14 / 16: those scripts never call Player::setVulnerable(false).</summary>
        void EnterCutscene(bool keepSpeed = true, bool invulnerable = true)
        {
            c.Cutscene = true;
            c.PlayerInvulnerable = invulnerable;
            playerSpeed = keepSpeed ? Ship.SpeedMetersPerSecond / (1000f * M) : 0f;
            Ship.externalControl = true;
            if (level.Weapons != null) level.Weapons.Blocked = true;
            level.Navigation?.SetAutopilot(null);
        }

        void LeaveCutscene()
        {
            c.Cutscene = false;
            c.PlayerInvulnerable = false;
            Ship.externalControl = false;
            if (level.Weapons != null) level.Weapons.Blocked = false;
            cam.Release();
            if (Camera.main != null && Camera.main.GetComponent<ChaseCamera>() is ChaseCamera ch) ch.constantRumble = 0f;
        }

        void SetPlayerVisible(bool on)
        {
            if (Ship.visualModel != null) Ship.visualModel.gameObject.SetActive(on);
        }

        /// <summary>"Hidden and removed": out of the level (kept in the slot list, like the original's dead entries).</summary>
        static void Remove(NpcShip s)
        {
            if (s == null) return;
            s.SetVisible(false);
            s.Deactivate();
            s.Place(new Vector3(-1e5f, -1e5f, -1e5f), Vector3.forward);
        }

        // ---- per frame (LevelScript::process) --------------------------------------------------------------------

        public void Tick(int index, float dtMs)
        {
            stepMs += dtMs;
            if (Ship.externalControl && !level.Health.Dead)
            {
                Player.position += Player.forward * playerSpeed * dtMs * M;
                Ship.ExternalSpeedMetersPerSecond = playerSpeed * 1000f * M;
            }
            if (loading) return;
            // The level's success advanced the index while its last cutscene step was still waiting for a line (16: msg 4
            // over and the win on the same frame): give the controls back; 25 -> 26 and 41 -> 42 keep their scripts.
            bool continues = (built == 25 && index == 26) || (built == 41 && index == 42);
            if (index != built && !continues && c.Cutscene) LeaveCutscene();
            switch (built)
            {
                case 14: if (index == 14) Tick14(dtMs); break;
                case 16: if (index == 16) Tick16(); break;
                case 21: if (index == 21) Tick21(); break;
                case 24: if (index == 24) Tick24(dtMs); break;
                case 25: if (index == 26 && Step == 0 && Triggered(1)) { Hole?.ResetTimer(false); Hole?.SetVisible(true); Step = 1; } break;
                case 29: if (index == 29) Tick29(dtMs); break;
                case 40: if (index == 40) Tick40(dtMs); break;
                case 41: if (index == 41) Tick41(dtMs); else if (index == 42) Tick42(dtMs); break;
            }
        }

        public void LateTick(float dtMs)
        {
            cam.LateTick(dtMs);
            if (explosion != null && cam.Camera != null)
                for (int i = 1; i < explosion.Length; i++)   // the _lookat meshes face the camera
                    if (explosion[i] != null) explosion[i].transform.rotation = Quaternion.LookRotation(cam.Camera.position - explosion[i].transform.position, Vector3.up);
        }

        // 14: the EMP arrest (levelscript_cutscenes.md M14).
        void Tick14(float dtMs)
        {
            switch (Step)
            {
                case 0:
                    if (Triggered(1))   // the first kill
                    {
                        S(0)?.Vanish();   // setHitpoints(0) + setDead: no explosion, no crate
                        playerSparks = new EmpSparks(Ship.visualModel != null ? Ship.visualModel : Player);
                        playerSparks.SetEmitting(true);
                        Sfx.PlayAt(assets?.empHit, Player.position);
                        Step = 1;
                    }
                    break;
                case 1:
                    if (Over(1))
                    {
                        c.Fade(true, Color.white, 600f);   // Level::flashScreen(3)
                        EnterCutscene(false, false);
                        cam.LookAtUnity(Player.position + ToUnity(new Vector3(1000, 700, 1000)), Player);
                        foreach (var s in c.Ships) if (s != null && s.Race == Standing.Pirate) s.Vanish();
                        Step = 2;
                    }
                    break;
                case 2:
                    Player.Rotate(0f, dtMs / 10000f * Mathf.Rad2Deg, 0f, Space.Self);
                    cam.SetDolly(new Vector3(0f, 0f, 1f));
                    if (Over(3))
                    {
                        // Taken aboard: the ship gone, the cruiser leaves, the camera follows it.
                        SetPlayerVisible(false);
                        playerSparks?.Clear();
                        var cruiser = S(c.Ships.Count - 1);
                        if (cruiser != null)
                        {
                            cruiser.SetMoving(true);
                            cam.LookAtUnity(cruiser.transform.position + ToUnity(new Vector3(-10000, -200, 5000)), cruiser.transform);
                        }
                        Step = 3;
                    }
                    break;
                case 3:
                    if (Over(4)) Step = 4;
                    break;
                case 4:
                    if (stepMs >= 4000f)
                    {
                        // nextCampaignMission (-> 15), departStation(98 Alioth), the station module.
                        loading = true;
                        Story.Advance(level.Database);
                        Session.StationIndex = 98;
                        level.Dock();
                    }
                    break;
            }
        }

        // 16: the first Void contact (M16).
        void Tick16()
        {
            switch (Step)
            {
                case 0:
                    if (Triggered(0) && S(0) != null)
                    {
                        EnterCutscene(true, false);
                        var f = S(0).transform;
                        cam.LookAtUnity(f.position + ToUnity(new Vector3(6000, 4000, 47500)), f);
                        cam.SetDolly(new Vector3(-1f, 0f, -3f));
                        Step = 1;
                    }
                    break;
                case 1:
                    if (Over(1)) { LeaveCutscene(); Step = 2; }
                    break;
                case 2:
                    if (c.MissionMs >= 20000f) { Hole?.ResetTimer(false); Step = 3; }
                    break;
                case 3:
                    if (Triggered(2) && Hole != null)
                    {
                        // They flee: the wormhole opens, every Void fighter gets a route through it and drops its targets.
                        Hole.SetVisible(true);
                        Hole.Open();
                        EnterCutscene(true, false);
                        cam.LookAtUnity(Hole.transform.position + ToUnity(new Vector3(-9000, -4000, -30000)), Hole.transform);
                        cam.SetDolly(new Vector3(0.5f, 0f, 0.2f));
                        var w = Hole.GamePosition;
                        var flee = new Route(false);
                        flee.points.Add(w + new Vector3(0, 0, 30000));
                        foreach (var s in c.Ships)
                        {
                            if (s == null || s.Race != Standing.Void || !s.Target.Alive) continue;
                            var at = w + new Vector3(R(4000) - 2000, R(4000) - 2000, -(13000 + R(4000)));
                            s.Place(ToUnity(at), Dir(w - at));
                            s.SetOnlyEnemy(null);
                            s.SetRoute(flee);
                        }
                        Step = 4;
                    }
                    break;
                case 4:
                    if (Over(3))
                    {
                        foreach (var s in c.Ships) if (s != null && s.Race == Standing.Void) Remove(s);
                        Hole?.ResetTimer(true);   // reset(closing): shrinks away after a second
                        Step = 5;
                    }
                    break;
                case 5:
                    if (Over(4)) { LeaveCutscene(); Step = 6; }
                    break;
            }
        }

        // 21: the scouts turn hostile once one of them was hit or the player passed the first waypoint (line 2).
        void Tick21()
        {
            if (Step != 0) return;
            bool turned = Triggered(2);
            for (int i = 1; i <= 3; i++) if (S(i) != null && S(i).turnedEnemy) turned = true;
            if (!turned) return;
            for (int i = 1; i <= 3; i++) if (S(i) != null) S(i).alwaysEnemy = true;
            Step = 1;
        }

        // 24: the wormhole takes Keith (M24).
        void Tick24(float dtMs)
        {
            switch (Step)
            {
                case 0:
                    if (Triggered(3))
                    {
                        EnterCutscene();
                        var side = Player.right * 3000f + Player.up * 1500f - Player.forward * 6000f;
                        cam.LookAtUnity(Player.position + side * M, Player);
                        if (Hole != null)
                        {
                            // Ahead of the player, inside PlayerEgo::calcCollision's 40000 pull radius whatever the ship does
                            // meanwhile (the original's distance k is lost); the remake keeps it clear of the station by moving
                            // it above the ship at the same distance instead (adding 30000 upward left it 42000 away from a ship
                            // parked near the station: never pulled, and gone after a minute).
                            var ahead = PlayerGame + PlayerDirGame * 20000f;
                            if (level.Station != null && ahead.magnitude < 30000f)
                                ahead = PlayerGame + new Vector3(Player.up.x, Player.up.y, -Player.up.z) * 20000f;
                            Hole.SetPosition(ahead);
                        }
                        foreach (var s in c.Ships) s?.SetOnlyEnemy(null);
                        Step = 1;
                    }
                    break;
                case 1:
                    if (Triggered(4) && Hole != null)
                    {
                        Hole.SetVisible(true);
                        Hole.ResetTimer(false);
                        Hole.Open();
                        Sfx.PlayAt(assets?.wormholeSound, cam.Camera != null ? cam.Camera.position : Player.position);
                        Step = 2;
                    }
                    break;
                case 2:
                    cam.Rumble = 0.5f;
                    Player.Rotate(0f, 0f, dtMs / 5000f * Mathf.Rad2Deg, Space.Self);   // the ride itself is SpaceLevel's
                    break;
            }
        }

        // 29: the probe (M29), then 180 s of survival.
        void Tick29(float dtMs)
        {
            switch (Step)
            {
                case 0:
                    if (Triggered(0))
                    {
                        EnterCutscene();
                        if (assets != null && assets.scannerProbe != null)
                        {
                            probe = Object.Instantiate(assets.scannerProbe, Player.position + Player.forward * 300f * M, Player.rotation);
                            GunRig.StripForFx(probe);
                        }
                        var at = Player.position + (Player.forward * 2000f + Player.right * 1500f + Player.up * 800f) * M;
                        cam.LookAtUnity(at, probe != null ? probe.transform : Player);
                        Sfx.PlayAt(assets?.probeLaunch, Player.position);
                        Step = 1;
                    }
                    break;
                case 1:
                    if (probe != null) probe.transform.position += probe.transform.forward * 3f * dtMs * M;
                    if (Over(2))
                    {
                        LeaveCutscene();
                        if (probe != null) Object.Destroy(probe);
                        probe = null;
                        c.ResetClock();
                        c.TimeLimitMs = 180000f;
                        c.WinObjective = () => c.MissionMs > 180000f;   // Objective 3 (180000): survival
                        Step = 2;
                    }
                    break;
            }
        }

        // 40: Errkt's freighter through the wormhole (M40).
        void Tick40(float dtMs)
        {
            var f = S(0);
            if (f == null) return;
            float z = -f.transform.position.z / M;
            switch (Step)
            {
                case 0:
                    if (Triggered(3))
                    {
                        f.Place(ToUnity(friendRoute.points[0]), Dir(new Vector3(0, 0, 1)));
                        f.SetRace(1);
                        f.SetVisible(true);
                        f.Wake();
                        f.SetMoving(true);
                        EnterCutscene();
                        cam.LookAtUnity(f.transform.position + ToUnity(new Vector3(-9000, 3000, -14000)), f.transform);
                        cam.SetDolly(new Vector3(0f, 0f, -2f));
                        Step = 1;
                    }
                    break;
                case 1:
                    if (Over(4)) { LeaveCutscene(); Step = 2; }
                    break;
                case 2:
                    if (z >= 90000f && Hole != null)
                    {
                        // The reserve Void fighters come out of the wormhole.
                        var w = Hole.GamePosition;
                        for (int i = 9; i <= 12; i++)
                        {
                            var s = S(i);
                            if (s == null) continue;
                            var at = w + new Vector3(R(20000) - 10000, R(20000) - 10000, R(20000) - 10000);
                            s.Place(ToUnity(at), Dir(PlayerGame - at));
                            s.Wake();
                        }
                        Step = 3;
                    }
                    break;
                case 3:
                    if (Hole != null && z >= Hole.GamePosition.z) { holeZ = Hole.GamePosition.z; Step = 4; }
                    break;
                case 4:
                {
                    // LevelScript 0x28 event 4 (0x1676e0): moveForward((z - 200000) - dt) on top of PlayerFixedObject's
                    // own moveForward(dt): each 30 fps frame it advances by its distance past the wormhole, which doubles
                    // it, so the hole swallows the freighter in about half a second (here per real time, not per frame).
                    float past = Mathf.Max(1f, z - holeZ);
                    float extra = past * (Mathf.Pow(2f, dtMs / (1000f / 30f)) - 1f);
                    f.transform.position += f.transform.forward * (extra * M);
                    z = -f.transform.position.z / M;
                    if (z > 500000f)
                    {
                        // Inactive, not dead: radio trigger 0x18 then gives Keith's 2048 (Errkt went through).
                        f.Place(ToUnity(new Vector3(0, 0, -200000)), Dir(new Vector3(0, 0, 1)));
                        f.Deactivate();
                        f.SetVisible(false);
                        Step = 5;
                    }
                    break;
                }
            }
        }

        float holeZ;

        // 41: Errkt's last flight (M41).
        void Tick41(float dtMs)
        {
            var f = S(0);
            if (f == null) return;
            switch (Step)
            {
                case 0:
                    if (Triggered(4))
                    {
                        EnterCutscene();
                        Vector3[] at = { new Vector3(-40000, 500, -30000), new Vector3(-41000, -200, -31000), new Vector3(-42000, 100, -32000) };
                        for (int i = 1; i <= 3; i++)
                        {
                            var s = S(i);
                            if (s == null) continue;
                            if (!s.Target.Alive || s.Gone) s.Revive(ToUnity(at[i - 1]));
                            s.Place(ToUnity(at[i - 1]), (f.transform.position - ToUnity(at[i - 1])).normalized);
                            s.Wake();
                            s.SetOnlyEnemy(f.Target);
                        }
                        cam.LookAtUnity(f.transform.position + ToUnity(new Vector3(-7000, 2500, -9000)), f.transform);
                        Step = 1;
                    }
                    break;
                case 1:
                    if (Triggered(5))
                    {
                        // The engine hit: fire and smoke, unkillable, stopped, Errkt's music.
                        freighterSmoke = new ShipSmoke(f.transform);
                        freighterSmoke.SetEmitting(true);
                        f.SetHull(9999999);
                        f.SetMoving(false);
                        f.SetEngineSound(false);
                        // 0x9b Errkt_CutSeq_01 is an FMOD cutscene event: it plays over the orbit's music (not stopped).
                        c.PlayMusic(assets?.errktCutscene, false);
                        Step = 2;
                    }
                    break;
                case 2:
                    // It drifts (0, -dt, 2dt) and rolls dt * 3e-5 for 15 s.
                    f.transform.position += ToUnity(new Vector3(0f, -1f, 2f)) * dtMs;
                    // AEGeometry::rotate(0, 0, dt * 3e-5) (disassembly 0x166df4): a roll only (game z = the hull's own axis;
                    // the mirror flips its sign).
                    f.transform.Rotate(0f, 0f, -dtMs * 3e-5f * Mathf.Rad2Deg, Space.Self);
                    if (stepMs >= 15000f) Step = 3;
                    break;
                case 3:
                    // LevelScript 0x29 step 3 (0x16c676): setPosition(2006, -31500, -86720), then the absolute
                    // setRotation(-0.4, 0, 1.8): on its side on the mother ship's arm (keeping the drift's heading with a
                    // level roll put it in nose first, like a dart); the wreck takes this pose when it dies.
                    f.transform.SetPositionAndRotation(ToUnity(new Vector3(2006, -31500, -86720)), OrbitLayout.RotationToUnity(new Vector3(-0.4f, 0f, 1.8f)));
                    f.SetExhaust(false);
                    // The camera at the freighter + (3000, 1000, 2000), world up (useTargetsUpVector(false)).
                    cam.LookAtUnity(f.transform.position + ToUnity(new Vector3(3000, 1000, 2000)), f.transform);
                    for (int i = 1; i < c.Ships.Count; i++) S(i)?.SetOnlyEnemy(level.Health.Target);
                    Step = 4;
                    break;
                case 4:
                    cam.SetDolly(new Vector3(1f, 0f, -2f));
                    if (stepMs >= 15000f)
                    {
                        f.SetHull(100);   // the player has to finish him
                        won41 = true;
                        LeaveCutscene();
                        Step = 5;
                    }
                    break;
            }
        }

        // 42 on 41's level: the mother ship's end and the escape (M42).
        CycleSound mothership;

        void Tick42(float dtMs)
        {
            mothership?.Update(dtMs);
            if (!objectivesRemoved) { objectivesRemoved = true; c.RemoveObjectives(); }   // MGame::OnTouchEnd, new index 0x2a
            var chase = Camera.main != null ? Camera.main.GetComponent<ChaseCamera>() : null;
            switch (Step)
            {
                case 5:
                    if (Over(7) && Hole != null)
                    {
                        if (mothership == null && assets != null)
                            mothership = CycleSound.Mothership(level.gameObject, assets.mothershipLoops, assets.mothershipAdds1, assets.mothershipAdds2);
                        mothership?.Play();   // 153 (its "time" parameter 0.5: state 6)
                        c.ResetClock();
                        Hole.SetPosition(new Vector3(25000, 20000, -55000));
                        Hole.SetVisible(true);
                        Hole.ResetTimer(false);
                        Hole.Open();
                        Step = 6;
                    }
                    break;
                case 6:
                    if (chase != null) chase.constantRumble = 0.5f;
                    if (level.Collision != null && level.Collision.InWormhole)
                    {
                        // Through the wormhole: the camera looks back at the mother ship as it explodes.
                        EnterCutscene(false);
                        SetPlayerVisible(false);
                        var station = level.Station != null ? level.Station.transform.position : Vector3.zero;
                        var look = station + Player.position.normalized * 5000f * M;
                        cam.LookAtUnity(Player.position, null, look);
                        Hole?.FreeMissionLock();
                        Hole?.ResetTimer(false);
                        Sfx.PlayAt(assets?.mothershipCutscene, Player.position);   // 154
                        SpawnExplosion(station);
                        Step = 7;
                    }
                    break;
                case 7:
                    cam.Rumble = 1f;
                    cam.SetDolly(new Vector3(-18f, 0f, 0f));
                    // The second and third explosion meshes (+0xb0 / +0xb4) face the camera's direction every frame.
                    if (explosion != null && cam.Camera != null)
                        for (int i = 1; i < explosion.Length && i <= 2; i++)
                            if (explosion[i] != null) explosion[i].transform.rotation = Quaternion.LookRotation(cam.Camera.forward, Vector3.up);
                    if (stepMs >= 4000f && level.Station != null && level.Station.activeSelf) level.Station.SetActive(false);
                    if (stepMs >= 15000f)
                    {
                        // this+0xa8 cleared: the explosion meshes stop animating (frozen) and the fade starts.
                        if (explosion != null)
                            foreach (var e in explosion)
                                if (e != null) foreach (var a in e.GetComponentsInChildren<PartAnimation>(true)) a.Pause();
                        c.Fade(false, Color.black, 4000f);
                        Step = 8;
                    }
                    break;
                case 8:
                    if (c.FadeDone || stepMs >= 10000f)
                    {
                        loading = true;
                        mothership?.Stop();
                        level.RideWormhole();   // hull / shield / armor kept, station Status+0x84, comingFromAlienWorld
                    }
                    break;
            }
        }

        void SpawnExplosion(Vector3 at)
        {
            if (assets == null || assets.voidStationExplosion == null) return;
            explosion = new GameObject[assets.voidStationExplosion.Length];
            for (int i = 0; i < explosion.Length; i++)
            {
                var prefab = assets.voidStationExplosion[i];
                if (prefab == null) continue;
                explosion[i] = Object.Instantiate(prefab, at, Quaternion.identity);
                GunRig.StripForFx(explosion[i]);
                GunRig.EnableFades(explosion[i]);   // the parts' `extra` fade-out
                PartAnimation.PlayOnce(explosion[i]);
            }
        }
    }
}
