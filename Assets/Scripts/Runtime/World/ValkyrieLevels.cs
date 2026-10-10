// ValkyrieLevels.cs
// The Valkyrie add-on's in-space levels (campaign indices 48-81; Level::createCampaignMission 0xc3370 + LevelScript::
// LevelScript / process; Reference/research/campaign_levels_a.md 3.17-3.19, campaign_levels_b.md parts 1-2). Plain C#, run
// by CampaignLevel. Ships are Level+0xf8 slots in the original's order (the radio triggers and objectives index them); the
// script step is the level-script event (radio trigger 27).
//   48 B'akrram     chauffeured in Taret Orskk's H'Soc: 5 friendly H'Soc, the player at (0, 0, 55000) on the autopilot to
//                   the station, the stick ignored (MGame::OnUpdate)
//   49 B'akrram     the stolen K'Suukk: the same 5 H'Soc; done 10 s after launch (type 0x9c)
//   50 Makke S'ik   4 H'Soc; once line 2 is over they turn (always-enemy, turnEnemy) and the Terran/Vossk axis goes to
//                   +100 (Standing::setStanding(0, 100))
//   51 S'inokk      a Vossk freighter a third of the way from the gate to the player, 5 H'Soc looping on the gate
//   52 elsewhere    two Vossk freighters halfway from the gate, 6 H'Soc (hull 270 for 49-52 and 56, freighters x5)
//   56 Scion        the turret test in the S'Kanarr: 3 pirate escorts (Velasco, unkillable) that hold fire until the
//                   player's armor is gone, 6 sleeping pirates at the first two waypoints
//   63 Coromesk     the pirate outpost and 6 sleeping pirates; won with fewer than 4 left; the pirates then stop shooting
//   64 Nosdron      Khador (Typhon) flying (100000, 0, 0) with 8 pirates; a cutscene on him, he sits still until 5 are dead,
//                   then the rest flee; won when the last line is over (Khador dead fails)
//   65 Kothar       straight in from 64: the player at (0, 0, 170000), Khador beside him flying to the station
//   67 Nosdron      Corny's break-in: the friendly outpost far out, pirates asleep around it, Corny waits, goes in, comes
//                   out and leaves (fail: the outpost destroyed, until he is out)
//   69 Inari Onu    Trot Lykkt (Type 43) leaves toward 4 x the second planet; a cutscene, then he is gone
//   70 Lopat        Trot Lykkt between the player (120000 further back) and the station: the Disruptor Laser (x2.5), his
//                   hull x2.5 then x3, speed 10; won when he is dead
//   73 Teres        the "weapons" convoy: 4 Terran freighters, 8 pirates (4 of them later); EMP a freighter to stop the
//                   convoy; won with all pirates dead, failed with all freighters dead
//   78 Valkyrie     the escape: the battlestation's turrets slide out, the hyper drive, the station jumps away, 20 pirates
//                   come in (the Khador Drive then misjumps into the Void, SystemJump)
//   79 the Void     7 Void fighters (done at once: type 0xa5)
//   80 Kothar       Alice's battlestation (0, 0, 160000) with 8 turrets and 4 shield generators, pirates, 3 friendly Wards;
//                   its laser blows up part of the station; all of them dead -> the battlestation jumps away
//   81 the Void     Alice stranded: the battlestation drops out of hyperspace, her lines, fade out, docked at Kothar
// Remake picks for floats the decompile lost (campaign_levels_b.md "uncertain"): the cutscene camera y drift of 80 (0),
// the step-11 explosion and the step-12 hyper drive of 80 at the battlestation, the look-at point of 81 (the origin), the
// Void fighters of 79 / 81 around the orbit like the other Void orbits.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public class ValkyrieLevels
    {
        const float M = 0.05f;

        readonly CampaignLevel c;
        readonly SpaceLevel level;
        readonly CutsceneCamera cam;
        readonly StoryAssets assets;
        readonly CombatAssets combat;
        int built;
        float stepMs;
        bool loading, autopiloted;
        GameObject fx, battlestation, stationLaser, battleLaser;
        float turretMoved, fxMs, fxLength;

        public ValkyrieLevels(CampaignLevel campaignLevel, SpaceLevel spaceLevel)
        {
            c = campaignLevel;
            level = spaceLevel;
            assets = StoryAssets.Load();
            combat = CombatAssets.Load();
            cam = new CutsceneCamera(level.mainCamera);
        }

        Transform Player => level.Player.transform;
        ShipController Ship => level.Player;
        int Step { get => c.Event; set { c.Event = value; stepMs = 0f; } }
        NpcShip S(int i) => i >= 0 && i < c.Ships.Count ? c.Ships[i] : null;
        bool Over(int line) => c.Radio != null && c.Radio.Over(line);
        bool Triggered(int line) => c.Radio != null && c.Radio.Triggered(line);
        float T => c.MissionMs;

        static Vector3 ToUnity(Vector3 game) => new Vector3(game.x, game.y, -game.z) * M;
        static Vector3 ToGame(Vector3 unity) => new Vector3(unity.x, unity.y, -unity.z) / M;
        static Vector3 Dir(Vector3 game) => new Vector3(game.x, game.y, -game.z).normalized;
        Vector3 PlayerGame => ToGame(Player.position);
        Vector3 PlayerDirGame => new Vector3(Player.forward.x, Player.forward.y, -Player.forward.z);
        static int R(int n) => Random.Range(0, n);
        static float Sign() => R(2) == 0 ? 1f : -1f;

        /// <summary>Level::createShip's hull, forced to 270 for campaign 0x31-0x34 and 0x38 (then x5 for freighters, x2 on
        /// Extreme).</summary>
        static int Hull270(bool freighter) => (int)(270 * (freighter ? 5 : 1) * Session.DifficultyFactor);

        /// <summary>Landmark 1, the visible jumpgate (game units), or (0, 0, 40000) without one (Level+0x18c).</summary>
        Vector3 Gate => level.Layout.hasJumpgate ? level.Layout.jumpgate : new Vector3(0, 0, 40000);

        NpcShip Freighter(int race, int ship, Vector3 at, bool jitter, bool moving, System.Action<SpawnSpec> setup = null) =>
            c.SpawnShip(race, ship, at, jitter, s => { s.freighter = true; s.stationary = !moving; s.group = NpcGroup.Special; setup?.Invoke(s); });

        /// <summary>Level::createStaticObject(0x37a3): a Pirate Outpost (name 441), the pirate bases' volumes and wreck.</summary>
        NpcShip Outpost(Vector3 at, System.Action<SpawnSpec> setup) =>
            c.SpawnShip(Standing.Pirate, -1, at, false, s =>
            {
                s.group = NpcGroup.Outpost; s.fixedObject = "station_pirates"; s.collisionId = 1002; s.hitRadius = 7500f;
                s.hitpoints = KaamoClub.OutpostHull(); s.wreckPrefab = combat != null ? combat.outpostWreck : null; s.explosionScale = 8f;
                s.stationary = true; s.noLoot = true; s.nameText = 441;
                setup?.Invoke(s);
            });

        /// <summary>Level::createStaticObject(0x381b / 0x381d): a battlestation turret (1666) or shield generator (1665, no gun).</summary>
        NpcShip BattleTurret(bool shield, int race, Vector3 at, Vector3 rot, System.Action<SpawnSpec> setup = null) =>
            c.SpawnShip(race, -1, at, false, s =>
            {
                s.group = NpcGroup.Turret; s.rotation = rot; s.noLoot = true; s.stationary = true; s.hitpoints = 1000;
                if (shield) { s.fixedObject = "v_station_battlestation_shield"; s.nameText = 1665; }
                else { s.turretAssembly = "v_station_battlestation_turret"; s.nameText = 1666; }
                setup?.Invoke(s);
            });

        GameObject Scenery(string assembly, Vector3 gamePos, Quaternion rot, string label)
        {
            var go = OrbitBuilder.Spawn(level.Database, assembly, gamePos, rot, label, null);
            if (go != null) GunRig.StripForFx(go);
            return go;
        }

        // ---- building (Level::createCampaignMission + the LevelScript constructor) --------------------------------

        public bool Build(int index)
        {
            built = index;
            switch (index)
            {
                case 48: case 49: Build48(); return true;
                case 50: Build50(); return true;
                case 51: Build51(); return true;
                case 52: Build52(); return true;
                case 56: Build56(); return true;
                case 63: Build63(); return true;
                case 64: Build64(); return true;
                case 65: Build65(); return true;
                case 67: Build67(); return true;
                case 69: Build69(); return true;
                case 70: Build70(); return true;
                case 73: Build73(); return true;
                case 78: Build78(); return true;
                case 79: Build79(); return true;
                case 80: Build80(); return true;
                case 81: Build81(); return true;
                default: return false;
            }
        }

        /// <summary>After Level::connectPlayers (the enemy lists are built): index 64's special case.</summary>
        public void AfterConnect()
        {
            if (built != 64) return;
            // connectPlayers 0x40: Khador and his escort never target each other (only the player).
            var khador = S(0);
            khador?.SetOnlyEnemy(null);
            for (int i = 1; i < c.Ships.Count; i++) if (S(i) != null && khador != null) S(i).enemies.Remove(khador.Target);
        }

        void Build48()
        {
            // 5 Vossk H'Soc (race 1), always-friend, around the origin +-20 000.
            for (int i = 0; i < 5; i++) c.SpawnShip(1, 9, Vector3.zero, true, s => { s.alwaysFriend = true; if (built == 49) s.hitpoints = Hull270(false); });
            if (built == 48)
            {
                // LevelScript ctor: the player at (0, 0, 55000) facing the station, on the autopilot to it.
                var p = new Vector3(0, 0, 55000);
                level.MovePlayer(ToUnity(p), Quaternion.LookRotation(Dir(-p), Vector3.up));
            }
        }

        void Build50()
        {
            for (int i = 0; i < 4; i++) c.SpawnShip(1, 9, Vector3.zero, true, s => { s.alwaysFriend = true; s.hitpoints = Hull270(false); });
        }

        void Build51()
        {
            var g = Gate;
            var loop = new Route(true);
            loop.points.Add(g);
            Freighter(1, 13, g + (PlayerGame - g) / 3f, false, true, s => { s.alwaysEnemy = true; s.hitpoints = Hull270(true); });
            for (int i = 0; i < 5; i++) c.SpawnShip(1, 9, g, true, s => { s.alwaysEnemy = true; s.route = loop; s.hitpoints = Hull270(false); });
        }

        void Build52()
        {
            var g = Gate;
            var f = g + (PlayerGame - g) / 2f;
            var loop = new Route(true);
            loop.points.Add(g);
            Freighter(1, 13, f, false, true, s => { s.alwaysEnemy = true; s.hitpoints = Hull270(true); });
            Freighter(1, 13, f + new Vector3(16000, 13000, 16000), false, true, s => { s.alwaysEnemy = true; s.hitpoints = Hull270(true); });
            for (int i = 0; i < 6; i++) c.SpawnShip(1, 9, g, true, s => { s.alwaysEnemy = true; s.route = loop; s.hitpoints = Hull270(false); });
        }

        void Build56()
        {
            var route = new Route(false);
            route.points.Add(new Vector3(0, 0, -60000));
            route.points.Add(new Vector3(-12000, -7000, -110000));
            route.points.Add(new Vector3(15000, 3000, -160000));
            c.SetPlayerRoute(route);
            // [0-2] the pirate escort (Velasco), friendly, unkillable, asleep, on the friend route beside the player.
            var p = PlayerGame * 0.8f;
            for (int i = 0; i < 3; i++)
                c.SpawnShip(Standing.Pirate, 24, p + new Vector3(R(7000) - 3500, R(7000) - 3500, R(7000) - 3500), false,
                            s => { s.alwaysFriend = true; s.asleep = true; s.route = route.Clone(); s.hitpoints = 9999999; s.noLoot = true; });
            // [3-4] at waypoint 0, [5-8] at waypoint 1: sleeping pirates, hull 270.
            for (int i = 3; i < 9; i++)
                c.SpawnShip(Standing.Pirate, NpcTables.RandomFighter(Standing.Pirate), route.points[i < 5 ? 0 : 1], true,
                            s => { s.asleep = true; s.hitpoints = Hull270(false); });
            c.WinObjective = () => c.DeadRange(3, 9);   // 0x12 (3, 9)
        }

        void Build63()
        {
            Outpost(Vector3.zero, s => s.asleep = true);
            for (int i = 0; i < 6; i++)
            {
                var at = new Vector3(Sign() * R(20000) + 10000, Sign() * R(20000) + 10000, Sign() * R(20000) + 10000);
                c.SpawnShip(Standing.Pirate, NpcTables.RandomFighter(Standing.Pirate), at, false, s => s.asleep = true);
            }
            c.WinObjective = () => c.EnemiesLeft < 4;   // 0x1b (4)
        }

        void Build64()
        {
            // After the normal arrival the player is pushed 40 000 back along his heading.
            var back = PlayerGame - PlayerDirGame * 40000f;
            level.MovePlayer(ToUnity(back), Player.rotation);
            var route = new Route(false);
            route.points.Add(new Vector3(100000, 0, 0));
            var khador = c.SpawnShip(0, 38, Vector3.zero, false,
                                     s => { s.alwaysFriend = true; s.nameText = 1617; s.route = route.Clone(); s.noLoot = true; });
            khador.Place(ToUnity(Vector3.zero), Dir(new Vector3(1, 0, 0)));
            for (int i = 0; i < 8; i++)
            {
                var at = new Vector3(Sign() * (1000 + R(1500)), Sign() * (1000 + R(1500)), Sign() * (1000 + R(1500)));
                var s = c.SpawnShip(Standing.Pirate, NpcTables.RandomFighter(Standing.Pirate), at, false, sp => sp.route = route.Clone());
                s.Place(ToUnity(at), Dir(new Vector3(1, 0, 0)));
            }
            c.WinObjective = () => c.Radio != null && c.Radio.LastOver;   // 0x16
            c.FailObjective = () => c.ShipDestroyed(0);                          // 1 (0): Khador dead
        }

        void Build65()
        {
            var p = new Vector3(0, 0, 170000);
            level.MovePlayer(ToUnity(p), Quaternion.LookRotation(Dir(-p), Vector3.up));
            var route = new Route(false);
            route.points.Add(Vector3.zero);
            var khador = c.SpawnShip(0, 38, p + new Vector3(-3000, 0, 0), false,
                                     s => { s.alwaysFriend = true; s.nameText = 1617; s.route = route; s.hitpoints = 9999999; s.noLoot = true; });
            khador.Place(ToUnity(p + new Vector3(-3000, 0, 0)), Dir(new Vector3(0, 0, -1)));
        }

        void Build67()
        {
            var w = new Vector3(220000, -20000, -10000);
            var route = new Route(false);
            route.points.Add(w);
            c.SetPlayerRoute(route);
            // [0] the outpost, friendly (Corny breaks in, it must not be destroyed).
            Outpost(w, s => { s.alwaysFriend = true; s.asleep = true; });
            // [1] beside the outpost, [2-4] around it, [5-9] parked far away (5-8 are brought in later): asleep, detection 150 000.
            void Pirate(Vector3 at, bool jitter) =>
                c.SpawnShip(Standing.Pirate, NpcTables.RandomFighter(Standing.Pirate), at, jitter, s => s.asleep = true).detectRange = 150000f;
            Pirate(w + new Vector3(30000, 0, 0), false);
            for (int i = 2; i <= 4; i++) Pirate(w, true);
            for (int i = 5; i <= 9; i++) Pirate(new Vector3(800000, 800000, 800000), false);
            // [10] Corny (Terran Taipan), friendly, unkillable, on the player's route.
            c.SpawnShip(0, 27, PlayerGame + new Vector3(2000, 500, -7000), false,
                        s => { s.alwaysFriend = true; s.nameText = 1633; s.hitpoints = 9999999; s.route = route.Clone(); s.noLoot = true; });
            c.WinObjective = () => c.Radio != null && c.Radio.LastOver;   // 0x16
            c.FailObjective = () => c.ShipDestroyed(0);                          // 1 (0): the outpost destroyed
        }

        /// <summary>StarSystem::getPlanets()[1] (game units, -20000 * its direction): the original's list starts with the sun
        /// (StarSystem::StarSystem: element 0 the sun, element i the planet of SolarSystem::getStations()[i - 1]), so element 1
        /// is the system's first station's planet, Lopat in Vulpes, where Trot flies off to ("He's flying to Lopat"). The
        /// remake's planet list has no sun: its [0]. ([1] showed Inari Onu, the orbit's own planet.)</summary>
        Vector3 FirstStationPlanet()
        {
            var planets = level.Layout.planets;
            if (planets.Count == 0) return new Vector3(0, 0, -20000);
            var p = planets[0];
            return -OrbitLayout.BackdropDistance * OrbitLayout.Direction(p.pitch, p.yaw);
        }

        void Build69()
        {
            var q = 4f * FirstStationPlanet();
            var route = new Route(false);
            route.points.Add(q);
            route.points.Add(10f * q);
            var trot = c.SpawnShip(0, 12, q, false, s => { s.alwaysFriend = true; s.nameText = 1631; s.route = route; s.speed = 5.5f; s.noLoot = true; });
            trot.Place(ToUnity(q), Dir(q.normalized));
            for (int i = 0; i < 4; i++) c.SpawnShip(0, NpcTables.RandomFighter(0), new Vector3(0, 0, 20000), true);
            c.WinObjective = () => c.Radio != null && c.Radio.LastOver;   // 0x16
        }

        void Build70()
        {
            // Level case 0x46: the player 120 000 further back along his heading.
            var p = PlayerGame - PlayerDirGame * 120000f;
            level.MovePlayer(ToUnity(p), Player.rotation);
            // landmarks[1]: the jumpgate (the visible one, else the hidden arrival gate); Trot flies to it.
            var target = level.Layout.hasJumpgate ? level.Layout.jumpgate : level.Layout.hiddenJumpgate;
            var at = p + (target - p) / 4f;
            var route = new Route(false);
            route.points.Add(target);
            int hull = Mathf.RoundToInt(NpcTables.Hull(0, 12) * 2.5f);
            var trot = c.SpawnShip(0, 12, at, false, s => { s.alwaysFriend = true; s.nameText = 1631; s.route = route; s.hitpoints = hull; s.noLoot = true; });
            trot.Place(ToUnity(at), Dir((target - at).normalized));
            // Level::assignGuns, mission 0x46: every ship but the wingmen fires the Disruptor Laser at x2.5 (NpcTables.GunDamage).
            c.WinObjective = () => c.ShipDestroyed(0);   // 1 (0)
        }

        void Build73()
        {
            var start = new Vector3(-30000, 0, 150000);
            level.MovePlayer(ToUnity(start), Quaternion.LookRotation(Dir(new Vector3(170000, 0, -50000) - start), Vector3.up));
            var wp0 = new Vector3(80000, 0, 60000);
            var wp1 = new Vector3(150000, 0, -50000);
            for (int i = 0; i < 4; i++) c.SpawnShip(Standing.Pirate, NpcTables.RandomFighter(Standing.Pirate), wp0, true, s => { s.alwaysEnemy = true; s.asleep = true; });
            for (int i = 0; i < 4; i++) c.SpawnShip(Standing.Pirate, NpcTables.RandomFighter(Standing.Pirate), new Vector3(-800000, -800000, -800000), false, s => { s.alwaysEnemy = true; s.asleep = true; });
            for (int i = 0; i < 4; i++)
                Freighter(0, 15, wp1 + new Vector3(R(20000) - 10000, R(20000) - 10000, R(20000) - 10000), false, true,
                          s => { s.alwaysFriend = true; s.noLoot = true; });
            c.WinObjective = () => c.DeadRange(0, 8);    // 0x12 (0, 8)
            c.FailObjective = () => c.DeadRange(8, 12);  // 0x12 (8, 12)
        }

        /// <summary>StationTurrets[i]'s pose relative to an unrotated battlestation (OrbitLayout.RotationToUnity(0), as in 80):
        /// the Unity offset (metres) and rotation; the measured one where there is (TurretPoses80), else the table's (offset
        /// (-x, y, -z), rotation (0, 0, -rz): the table turned by (0, pi, 0)).</summary>
        public static (Vector3 offset, Quaternion rotation) StationTurretPose(int i)
        {
            if (TurretPoses80.TryGetValue(i, out var p)) return (p.pos, Quaternion.Euler(p.rot));
            var t = StationTurrets[i];
            return (ToUnity(new Vector3(-t.pos.x, t.pos.y, -t.pos.z)), OrbitLayout.RotationToUnity(new Vector3(0f, 0f, -t.rz)));
        }

        /// <summary>DAT_002539d4: the battlestation's 8 turrets and 4 shield generators (position, rotation z, shield).</summary>
        public static readonly (Vector3 pos, float rz, bool shield)[] StationTurrets =
        {
            (new Vector3(-3994.97f, 23359f, -7378.1f), 1.5708f, false), (new Vector3(3994.96f, 23359f, -7378.1f), -1.5708f, false),
            (new Vector3(1988.03f, -37327.1f, -4511.46f), -1.5708f, true), (new Vector3(-1995.02f, -37327.1f, -4511.46f), 1.5708f, true),
            (new Vector3(-3264.75f, -22848.6f, 791.524f), 1.5708f, false), (new Vector3(3273.29f, -22848.6f, 791.524f), -1.5708f, false),
            (new Vector3(-29726.7f, -10994.8f, -3765.53f), 3.14159f, false), (new Vector3(29716.2f, -10994.8f, -3765.53f), 3.14159f, false),
            (new Vector3(29716.2f, 4854.22f, -3758.77f), 0f, false), (new Vector3(17013f, -764.391f, -1690.65f), 0f, true),
            (new Vector3(-17013.3f, -764.512f, -1690.65f), 0f, true), (new Vector3(-29726.7f, 4854.22f, -3758.77f), 0f, false),
        };

        void Build78()
        {
            // [0-1] the two top turrets, slid 1200 in (they slide out in the cutscene), [2-21] 20 pirates asleep far ahead.
            var t0 = StationTurrets[0];
            var t1 = StationTurrets[1];
            // Modified: the turrets' slide was measured against the original (Unity units / degrees, see the constants after
            // Build78): X from each turret's start to its end, rotation X from TurretStartRotX78 to 0, Y always 0, Z
            // from +-TurretStartRollDeg78 to +-TurretRollDeg78. Y and Z positions from the table.
            float mz = TurretMirrorZ78, mr = TurretRotZ78;
            // Modified: relative to where the station actually is (like 80's host), not the origin.
            var host78 = level.Station != null ? ToGame(level.Station.transform.position) : Vector3.zero;
            var tr0 = BattleTurret(false, 0, host78 + new Vector3(TurretSide0_78 * TurretStartX0_78 / M, t0.pos.y, t0.pos.z * mz), new Vector3(0, 0, Mathf.Sign(t0.rz) * TurretRollDeg78 * Mathf.Deg2Rad * mr));
            var tr1 = BattleTurret(false, 0, host78 + new Vector3(TurretSide1_78 * TurretStartX1_78 / M, t1.pos.y, t1.pos.z * mz), new Vector3(0, 0, Mathf.Sign(t1.rz) * TurretRollDeg78 * Mathf.Deg2Rad * mr));
            // Modified: the start pose (rotation X = TurretStartRotX78, Y = 0, Z = +-TurretStartRollDeg78; the Z sign is the
            // spawn's: 87 -> +, 273 (= -87) -> -).
            if (tr0 != null) { rollSign0_78 = tr0.transform.eulerAngles.z > 180f ? -1f : 1f; tr0.transform.rotation = Quaternion.Euler(TurretStartRotX78, 0f, rollSign0_78 * TurretStartRollDeg78); }
            if (tr1 != null) { rollSign1_78 = tr1.transform.eulerAngles.z > 180f ? -1f : 1f; tr1.transform.rotation = Quaternion.Euler(TurretStartRotX78, 0f, rollSign1_78 * TurretStartRollDeg78); }
            for (int i = 2; i < 22; i++)
            {
                var at = new Vector3(-17000 + 2000 * (i - 2) + R(2000), R(10000) - 5000, 155000 + R(10000));
                var s = c.SpawnShip(Standing.Pirate, NpcTables.RandomFighter(Standing.Pirate), at, false, sp => { sp.alwaysEnemy = true; sp.asleep = true; });
                s.Place(ToUnity(at), Dir(new Vector3(0, 0, -1)));
            }
            // LevelScript ctor: no launch camera; the cutscene camera at (2000, -1500, 16000) on the station; the player faces +Z.
            level.EndStartSequence();
            Player.rotation = Quaternion.LookRotation(Dir(new Vector3(0, 0, 1)), Vector3.up);
            EnterCutscene(false);
            // Modified: in the original the ship leaves the station at its full default speed (100 % throttle) and keeps
            // flying; EnterCutscene(false) parked it at the door (speed 0). The flight model flies it instead, input locked.
            Ship.externalControl = false;
            Ship.inputLocked = true;
            Ship.SetThrottle(1f);
            cam.LookAt(new Vector3(2000, -1500, 16000), level.Station != null ? level.Station.transform : Player);
            PauseAnimation(level.Station);
            Step = 1;
        }

        /// <summary>Modified: the two top turrets of 78, tuned against the original game (the table's X put them on swapped
        /// sides, and the remake swung them about Y instead of X).</summary>
        const float TurretSide0_78 = 1f;           // turret [0]'s side: +X
        const float TurretSide1_78 = -1f;          // turret [1]'s side: -X
        const float TurretStartX0_78 = 177.1f;     // turret [0]: |X| where its slide starts (Unity units, measured)
        const float TurretStartX1_78 = 176f;       // turret [1]: |X| where its slide starts (Unity units, measured)
        const float TurretEndX0_78 = 216.1f;       // turret [0]: |X| where its slide ends (Unity units, measured)
        const float TurretEndX1_78 = 215f;         // turret [1]: |X| where its slide ends (Unity units, measured)
        const float TurretStartRotX78 = 180f;      // rotation X at the start of the slide (degrees, measured); it ends at 0
        float rollSign0_78 = 1f, rollSign1_78 = -1f;   // each turret's Z rotation sign
        const float TurretStartRollDeg78 = 93f;    // |Z rotation| at the start of the slide (degrees, measured)
        const float TurretRollDeg78 = 87f;         // |Z rotation| at the end of the slide (degrees; the table has 90)
        const float TurretRotZ78 = 1f;             // the turrets' z rotation sign (-1 = flipped)
        const float TurretMirrorZ78 = 1f;          // front / back (-1 = mirrored)

        void Build79()
        {
            for (int i = 0; i < 7; i++)
                c.SpawnShip(Standing.Void, NpcTables.RandomFighter(Standing.Void),
                            new Vector3(Sign() * (20000 + R(80000)), Sign() * (20000 + R(80000)), Sign() * (20000 + R(80000))), false, s => s.alwaysEnemy = true);
        }

        void Build80()
        {
            var p = new Vector3(-70000, 0, -30000);
            level.MovePlayer(ToUnity(p), Quaternion.LookRotation(Dir(new Vector3(1, 0, 1).normalized), Vector3.up));
            var host = new Vector3(0, 0, 160000);
            // [0] Alice's battlestation, unrotated (scenery with the station's volumes mirrored; the radio doesn't count it).
            c.AddPlaceholder();
            battlestation = Scenery("v_station_battlestation_anim_mission_object", host, OrbitLayout.RotationToUnity(Vector3.zero), "Valkyrie battlestation");
            // PlayerStation ctor 0x1473c2 (station 0x65 from campaign 0x50): Transform::Update(dt = the animation's length)
            // once, then PlayerStation::update 0x147dbc never runs it again: the arms unfolded (the turrets and shield
            // generators below sit on them), not the folded load pose that looped as "opening itself".
            PartAnimation.HoldAllAtEnd(battlestation);
            AddStationVolumes(battlestation, true, unfolded: true);   // Modified: the unfolded arms' volumes
            // [1-12] turrets and shield generators: at host + (-x, y, -z), rotation (0, 0, -rz) (the table rotated by (0, pi, 0)).
            int rank = Session.Rank;
            int hp = rank <= 20 ? rank * 15 + 220 : 520;
            for (int i = 0; i < StationTurrets.Length; i++)
            {
                var t = StationTurrets[i];
                var s = BattleTurret(t.shield, Standing.Pirate, host + new Vector3(-t.pos.x, t.pos.y, -t.pos.z), new Vector3(0, 0, -t.rz),
                                     sp => { sp.alwaysEnemy = true; sp.hitpoints = hp; });
                // Modified: named after its StationTurrets entry, so each one can be told apart in the Hierarchy.
                if (s != null) s.gameObject.name = $"{(t.shield ? "Shield" : "Turret")} {i} (StationTurrets[{i}])";
                // Modified: the turrets' pose measured against the original where there is one (StationTurretPose).
                if (s != null && battlestation != null)
                {
                    var pose = StationTurretPose(i);
                    s.transform.SetPositionAndRotation(battlestation.transform.position + pose.offset, pose.rotation);
                }
                // Level::assignGuns, mission 0x50: turrets x1.7 (NpcTables.GunDamage).
                if (t.shield) s.shootingEnabled = false;
            }
            // [13-18] pirates, [19-21] Terran Wards (friendly) on the waypoint (0, 0, 80000).
            var route = new Route(false);
            route.points.Add(new Vector3(0, 0, 80000));
            for (int i = 0; i < 6; i++) c.SpawnShip(Standing.Pirate, NpcTables.RandomFighter(Standing.Pirate), route.points[0], true, s => { s.alwaysEnemy = true; s.route = route; });
            for (int i = 0; i < 3; i++) c.SpawnShip(0, 17, route.points[0], true, s => { s.alwaysFriend = true; s.route = route; });
            // LevelScript ctor: the battlestation's laser at it (playing), the deep science station's laser (hidden), the
            // station's explosion animation held until the hit.
            battleLaser = Scenery("v_station_battlestation_laser_anim_add", host, OrbitLayout.RotationToUnity(Vector3.zero), "Battlestation laser");
            // Modified: the laser waits for the first cutscene (it played from the level's start); shown in Tick80 step 0.
            if (battleLaser != null) battleLaser.SetActive(false);
            stationLaser = Scenery("v_station_deep_science_explosion_laser_anim_add", Vector3.zero, OrbitLayout.RotationToUnity(new Vector3(0, Mathf.PI, 0)), "Station laser");
            if (stationLaser != null) stationLaser.SetActive(false);
            // Modified: the station is held intact (its load pose) until the hit; only pausing it left it showing the
            // explosion's last frame (already blown apart) from the start. Its explosion's material channels (the
            // `extra` fades: the blown-off part's engine glow, the debris) are applied, or they never fade out.
            if (level.Station != null)
            {
                foreach (var a in level.Station.GetComponentsInChildren<PartAnimation>(true)) a.applyMaterialChannels = true;
                PartAnimation.HoldAll(level.Station);
                LoopEmitters80(level.Station);   // Modified: its thrusters' flames keep scrolling
                MakeFlamePieces80();             // Modified: the flames split onto the engine glows they belong to
            }
            c.WinObjective = () => c.Radio != null && c.Radio.LastOver;   // 0x16
        }

        void Build81()
        {
            // [0] the battlestation (hidden until it drops out of hyperspace), [1-8] Void fighters.
            c.AddPlaceholder();
            battlestation = Scenery("v_station_battlestation_anim_mission_object", Vector3.zero, Quaternion.identity, "Valkyrie battlestation");
            PartAnimation.HoldAllAtEnd(battlestation);   // station 101 from 0x50: unfolded and held (as in 80)
            if (battlestation != null) battlestation.SetActive(false);
            AddStationVolumes(battlestation, false);
            for (int i = 0; i < 8; i++)
                c.SpawnShip(Standing.Void, 8, new Vector3(Sign() * (20000 + R(80000)), Sign() * (20000 + R(80000)), Sign() * (20000 + R(80000))), false);
            // LevelScript ctor: the player hidden and parked at the origin, the camera at (-20000, 800, 120000).
            level.EndStartSequence();
            level.MovePlayer(Vector3.zero, Quaternion.LookRotation(Dir(new Vector3(0, 0, 1)), Vector3.up));
            EnterCutscene(false);
            SetPlayerVisible(false);
            cam.LookAt(new Vector3(-20000, 800, 120000), null, Vector3.zero);
            cam.SetDolly(new Vector3(2f, 0f, -4f));
            // Modified: the Void's battle music, 136 Space_Combat_Void (Space_Battle_Void.ogg, StoryAssets.voidBattle; it stayed
            // silent after the battlestation's jump: the cutscene starts the level with nothing playing). Fallback: the
            // system's space track.
            c.MusicOwned = true;
            var voidMusic = assets != null ? assets.voidBattle : null;
            if (voidMusic == null && level.Traffic != null) voidMusic = level.Traffic.RaceSpaceMusic() ?? level.Traffic.CalmClip();
            if (voidMusic != null) c.PlayMusic(voidMusic, true);
        }

        /// <summary>PlayerStation 101's volumes on a scenery battlestation (unrotated: the (0, pi, 0) of the table mirrored).</summary>
        /// Modified: 'unfolded' (80: the arms held unfolded) takes the alien orbit's battlestation volumes (collision 1003:
        /// the arms out at +-29630, where 101 has them folded at +-15990, so the unfolded arms had no collision).
        static void AddStationVolumes(GameObject go, bool unrotated, bool unfolded = false)
        {
            if (go == null) return;
            var o = go.AddComponent<Obstacle>();
            o.landmark = true;
            o.volumes = unfolded ? CollisionVolume.ForStation(101, true, true) : CollisionVolume.ForStation(101, false);
            if (unrotated)
                for (int i = 0; i < o.volumes.Count; i++)
                {
                    var v = o.volumes[i];
                    v.centre = new Vector3(-v.centre.x, v.centre.y, -v.centre.z);
                    o.volumes[i] = v;
                }
            HullCollision.Attach(go, null, o);   // remake: the player slides along its real shape; NPCs keep the volumes
        }

        static void PauseAnimation(GameObject go)
        {
            if (go == null) return;
            foreach (var a in go.GetComponentsInChildren<PartAnimation>(true)) a.play = false;
        }

        // ---- cutscene helpers (LevelScript "cutscene on" / "off", campaign_levels_b.md part 2) ------------------------

        float playerSpeed;

        void EnterCutscene(bool keepSpeed = true)
        {
            c.Cutscene = true;
            c.PlayerInvulnerable = true;
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
        }

        void SetPlayerVisible(bool on)
        {
            if (Ship.visualModel != null) Ship.visualModel.gameObject.SetActive(on);
        }

        static void Remove(NpcShip s)
        {
            if (s == null) return;
            s.SetVisible(false);
            s.Deactivate();
            s.Place(new Vector3(-1e5f, -1e5f, -1e5f), Vector3.forward);
        }

        /// <summary>The hyper_drive fx (scale 20 / 30): anim state 3 then 1 = played once, facing the camera.</summary>
        void SpawnHyperDrive(Vector3 gamePos, float scale, bool texture78 = false)
        {
            fxFixed = false;   // Modified: billboarded unless a level pins it (78)
            if (fx != null) Object.Destroy(fx);
            fx = null;
            if (assets == null || assets.hyperDrive == null) return;
            fx = Object.Instantiate(assets.hyperDrive, ToUnity(gamePos), (cam.Camera != null ? cam.Camera.rotation : Quaternion.identity) * Quaternion.Euler(0f, 180f, 0f));
            if (texture78) ReplaceFxTexture78(fx);   // Modified: before the fades / animation pick up the materials
            fx.transform.localScale *= scale;
            GunRig.StripForFx(fx);
            GunRig.EnableFades(fx);   // the parts' `extra` fade-out
            float len = PartAnimation.PlayOnce(fx);
            fxLength = len > 0f ? len : 3000f;
            fxMs = 0f;
            Sfx.PlayAt(assets.timeJump, cam.Camera != null ? cam.Camera.position : Player.position);   // 160
        }

        void FaceCamera(GameObject go)
        {
            if (go != null && cam.Camera != null)
                go.transform.rotation = Quaternion.LookRotation(cam.Camera.position - go.transform.position, Vector3.up) * Quaternion.Euler(0f, 180f, 0f);
        }

        static void PlayAtCamera(AudioClip clip)
        {
            var cam = Camera.main;
            if (clip != null && cam != null) Sfx.PlayAt(clip, cam.transform.position);
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
            // The level's win advanced the index while its cutscene still ran (78 -> 79 by the Khador Drive keeps it).
            if (index != built && c.Cutscene && built != 78) LeaveCutscene();
            switch (built)
            {
                case 48: if (index == 48) Tick48(); break;
                case 50: if (index == 50 || index == 51) Tick50(); break;
                case 56: if (index == 56) Tick56(); break;
                case 64: if (index == 64) Tick64(); break;
                // 67 / 69: the last line wins the level, so the script's final step runs after the advance.
                case 67: if (index == 67 || index == 68) Tick67(); break;
                case 69: if (index == 69 || index == 70) Tick69(); break;
                case 70: if (index == 70) Tick70(); break;
                case 73: if (index == 73) Tick73(); break;
                case 78: Tick78(dtMs); break;
                case 80: if (index == 80) Tick80(dtMs); break;
                case 81: if (index == 81) Tick81(dtMs); break;
            }
        }

        public void LateTick(float dtMs)
        {
            cam.LateTick(dtMs);
            if (built == 80) UpdateFlamePieces80();   // Modified
            if (!fxFixed) FaceCamera(fx);   // Modified: 78's jump is aligned with the station, not the camera
            // The fx plays once (anim state 3, then 1) and is gone at its end.
            if (fx != null && (fxMs += dtMs) >= fxLength) { Object.Destroy(fx); fx = null; }
        }

        // 48: Taret flies the player in: the autopilot to the station once the arrival camera is over, the stick ignored.
        void Tick48()
        {
            Ship.steeringLocked = true;
            if (autopiloted || !level.StartSequenceOver || level.Navigation == null) return;
            var station = level.Navigation.Targets.Find(t => t.kind == Navigation.Kind.Station);
            if (station == null) return;
            level.Navigation.SetAutopilot(station);
            autopiloted = true;
        }

        // 50 (and 51 on its level): the shared 0x32 / 0x33 block: once line 2 is over the H'Soc turn hostile.
        void Tick50()
        {
            if (Step != 0 || c.Radio == null || c.Radio.Count < 3 || !Over(2)) return;
            foreach (var s in c.Ships)
            {
                if (s == null || s.IsWingman) continue;
                s.alwaysFriend = false;
                s.alwaysEnemy = true;
                s.turnedEnemy = true;
            }
            Session.Standing[0] = 100;   // Standing::setStanding(0, 100): the Vossk hostile
            Step = 1;
        }

        // 56: the escort holds fire until the player's armor is gone.
        void Tick56()
        {
            if (Step == 0)
            {
                foreach (var s in c.Ships) if (s != null && s.alwaysFriend) s.shootingEnabled = false;
                Step = 1;
            }
            else if (Step == 1 && c.PlayerArmorGone)
            {
                foreach (var s in c.Ships) if (s != null && s.alwaysFriend) s.shootingEnabled = true;
                Step = 2;
            }
        }

        // 64: Khador's rescue.
        void Tick64()
        {
            var khador = S(0);
            switch (Step)
            {
                case 0:
                    if (Triggered(0) && khador != null)
                    {
                        EnterCutscene();
                        var kdir = new Vector3(khador.transform.forward.x, khador.transform.forward.y, -khador.transform.forward.z);
                        cam.LookAt(ToGame(khador.transform.position) + kdir * 10000f + new Vector3(0, 300, 3000), khador.transform);
                        cam.SetDolly(new Vector3(0f, 0.2f, 0f));
                        Step = 1;
                    }
                    break;
                case 1:
                    if (Triggered(2))
                    {
                        LeaveCutscene();
                        foreach (var s in c.Ships) s?.SetRoute(null);
                        Step = 2;
                    }
                    break;
                case 2:
                    if (khador != null) khador.frozen = true;
                    if (Triggered(5))
                    {
                        if (khador != null) khador.frozen = false;
                        var flee = new Route(false);
                        flee.points.Add(new Vector3(500000, 500000, 500000));
                        for (int i = 1; i <= 8; i++)
                        {
                            var s = S(i);
                            if (s == null || s.IsWingman || !s.Target.Alive) continue;
                            s.SetSpeed(16.5f);
                            s.SetOnlyEnemy(null);
                            s.SetRoute(flee);
                        }
                        Step = 3;
                    }
                    break;
                case 3:
                    if (Over(6))
                    {
                        for (int i = 1; i <= 8; i++) if (S(i) != null && S(i).Target.Alive) S(i).Vanish();   // setDead
                        Step = 4;
                    }
                    break;
            }
        }

        // 67: Corny's break-in.
        void Tick67()
        {
            var corny = S(10);
            var outpost = S(0);
            if (corny == null) return;
            var w = outpost != null ? ToGame(outpost.transform.position) : new Vector3(220000, -20000, -10000);
            switch (Step)
            {
                case 0:
                    if (Triggered(1)) { corny.SetSpeed(0f); corny.SetOnlyEnemy(null); corny.SetExhaust(false); Step = 1; }
                    break;
                case 1:
                    if (Over(3))
                    {
                        corny.SetSpeed(3f);
                        corny.SetExhaust(true);
                        EnterCutscene();
                        cam.LookAt(ToGame(corny.transform.position) + new Vector3(6000, -200, 1000), corny.transform);
                        Step = 2;
                    }
                    break;
                case 2:
                    if (Over(4))
                    {
                        LeaveCutscene();
                        corny.Place(ToUnity(new Vector3(500000, 500000, 500000)), corny.transform.forward);
                        corny.Deactivate();
                        corny.SetVisible(false);
                        Step = 3;
                    }
                    break;
                case 3:
                    if (Triggered(5))
                    {
                        for (int i = 5; i <= 8; i++)
                        {
                            var at = w + new Vector3(Sign() * (15000 + R(10000)), R(10000) - 5000, Sign() * (15000 + R(10000)));
                            S(i)?.Place(ToUnity(at), S(i).transform.forward);
                        }
                        Step = 4;
                    }
                    break;
                case 4:
                    if (Triggered(7))
                    {
                        var at = w + new Vector3(0, 0, 12000);
                        corny.Place(ToUnity(at), Dir(new Vector3(0, 0, 1)));
                        corny.Wake();
                        corny.SetVisible(true);
                        corny.SetSpeed(2f);
                        var away = new Route(false);
                        away.points.Add(new Vector3(0, 0, 800000));
                        corny.SetRoute(away);
                        c.FailObjective = null;   // Level+0x2c = 0: he's out
                        EnterCutscene();
                        cam.LookAt(at + new Vector3(6000, -200, 10000), corny.transform);
                        Step = 5;
                    }
                    break;
                case 5:
                    if (Triggered(9)) { corny.SetSpeed(8f); LeaveCutscene(); Step = 6; }
                    break;
                case 6:
                    if (Over(11)) { corny.Deactivate(); corny.SetVisible(false); Step = 7; }
                    break;
            }
        }

        // 69: Trot Lykkt leaves.
        void Tick69()
        {
            var trot = S(0);
            if (trot == null) return;
            if (Step == 0 && Triggered(0))
            {
                EnterCutscene();
                var dir = new Vector3(trot.transform.forward.x, trot.transform.forward.y, -trot.transform.forward.z);
                cam.LookAt(ToGame(trot.transform.position) + dir * 10000f + new Vector3(600, 300, 1000), trot.transform);
                Step = 1;
            }
            else if (Step == 1 && Over(1))
            {
                LeaveCutscene();
                Remove(trot);
                trot.Place(ToUnity(new Vector3(0, 0, -5000000)), trot.transform.forward);
                Step = 2;
            }
        }

        // 70: stop Trot Lykkt.
        void Tick70()
        {
            var trot = S(0);
            if (trot == null) return;
            var dir = new Vector3(trot.transform.forward.x, trot.transform.forward.y, -trot.transform.forward.z);
            switch (Step)
            {
                case 0:
                    if (Triggered(0))
                    {
                        EnterCutscene();
                        cam.LookAt(ToGame(trot.transform.position) + dir * 3000f + new Vector3(-600, -300, -1000), trot.transform);
                        Step = 1;
                    }
                    break;
                case 1:
                    if (Over(1))
                    {
                        trot.alwaysFriend = false;
                        trot.alwaysEnemy = true;
                        trot.detectRange = 100000f;
                        trot.SetRoute(null);
                        cam.LookAt(ToGame(trot.transform.position) + dir * 2000f + new Vector3(-600, 800, -1000), trot.transform);
                        Step = 2;
                    }
                    break;
                case 2:
                    if (Over(2))
                    {
                        LeaveCutscene();
                        trot.SetHull(trot.Hp.maxHull * 3);
                        trot.SetSpeed(10f);
                        Step = 3;
                    }
                    break;
            }
        }

        // 73: the convoy.
        void Tick73()
        {
            switch (Step)
            {
                case 0:
                    if (S(8) != null && (S(8).transform.position - Player.position).magnitude < 50000f * M) Step = 1;
                    break;
                case 1:
                    for (int i = 8; i < 12; i++)
                        if (S(i) != null && S(i).Target.Alive && S(i).Hp.emp < S(i).Hp.maxEmp) { Step = 2; break; }
                    break;
                case 2:
                    for (int i = 8; i < 12; i++)
                    {
                        if (S(i) == null || !S(i).Hp.empDisabled) continue;
                        // The first freighter (from 8) stops for good.
                        for (int k = 8; k < 12; k++) if (S(k) != null && S(k).Target.Alive) { S(k).frozen = true; S(k).SetMoving(false); break; }
                        Step = 3;
                        break;
                    }
                    break;
                case 3:
                    if (Triggered(7))
                    {
                        var p = PlayerGame;
                        for (int i = 4; i <= 7; i++)
                        {
                            var s = S(i);
                            if (s == null) continue;
                            var at = p + new Vector3(Sign() * (35000 + R(10000)), R(10000) - 5000, Sign() * (35000 + R(10000)));
                            s.Place(ToUnity(at), Dir(p - at));
                            s.Wake();
                        }
                        Step = 4;
                    }
                    break;
            }
        }

        // 78: the escape from Valkyrie (LevelScript.c 2001-2302, t = the level clock).
        void Tick78(float dtMs)
        {
            var station = level.Station;
            if (T >= 7901f && station != null && !animStarted)
            {
                animStarted = true;
                float unfoldMs = PartAnimation.PlayOnce(station);
                // Modified: the ship stops and faces the station at the end of the unfolding's first part (StopAtUnfold78 of
                // the whole animation; see below).
                stopAtT78 = T + (unfoldMs > 0f ? unfoldMs * StopAtUnfold78 : 0f);
            }
            if (animStarted && !stopped78 && T >= stopAtT78)
            {
                // Modified: at the end of the unfolding's first part the ship stops at once and faces the station (computer
                // controlled again, speed 0).
                stopped78 = true;
                Ship.externalControl = true;
                playerSpeed = 0f;
                if (station != null)
                {
                    var to = station.transform.position - Player.position;
                    if (to.sqrMagnitude > 1e-6f) Player.rotation = Quaternion.LookRotation(to, Vector3.up);
                }
            }
            switch (Step)
            {
                case 1:
                    if (station != null) cam.SetTarget(station.transform);
                    Step = 2;
                    break;
                case 2:
                    cam.SetDolly(new Vector3(-0.5f, 0.3f, 1f));
                    if (T >= 7001f) { cam.LookAt(new Vector3(20000, 5000, 30000), station != null ? station.transform : Player); Step = 3; }
                    break;
                case 3:
                    if (T >= 7901f && !musicPlayed) { musicPlayed = true; PlayAtCamera(assets?.valkyrieBattlemode); }   // 0x462
                    if (T >= 9001f) Step = 4;
                    break;
                case 4:
                    if (T >= 18001f)
                    {
                        cam.LookAt(new Vector3(8000, 31000, -9000), station != null ? station.transform : Player);
                        // Modified: the player's ship stays visible through the rest of the cutscene (was SetPlayerVisible(false)).
                        Step = 5;
                    }
                    break;
                case 5:
                    cam.SetDolly(new Vector3(-0.8f, 0f, 0f));
                    turretMoved = 0f;
                    Step = 6;
                    break;
                case 6:
                    if (SlideTurret(S(0), TurretSide0_78, TurretStartX0_78, TurretEndX0_78, rollSign0_78, dtMs)) { turretMoved = 0f; Step = 7; }   // Modified: [0] first (was [1])
                    break;
                case 7:
                    if (T >= 27001f) Step = 8;
                    break;
                case 8:
                    if (SlideTurret(S(1), TurretSide1_78, TurretStartX1_78, TurretEndX1_78, rollSign1_78, dtMs) && stepMs >= 2001f)   // Modified: [1] second (was [0])
                    {
                        cam.LookAt(new Vector3(-12000, 5000, 15000), station != null ? station.transform : Player);
                        Step = 9;
                    }
                    break;
                case 9:
                    cam.SetDolly(new Vector3(-0.7f, 1f, 3f));
                    cam.Rumble = Mathf.Clamp01(stepMs / 10000f);
                    if (stepMs >= 6000f)
                    {
                        SpawnHyperDrive(new Vector3(0, 0, 14000), 20f, texture78: true);
                        // Modified: the jump aligned with the station instead of facing the camera (values from the
                        // Inspector, Unity units relative to the station: position, rotation, scale).
                        if (fx != null)
                        {
                            var st = station != null ? station.transform.position : Vector3.zero;
                            fx.transform.SetPositionAndRotation(st + HyperDrivePos78, Quaternion.Euler(HyperDriveRot78));
                            fx.transform.localScale = Vector3.one * HyperDriveScale78;
                            fxFixed = true;
                        }
                        Step = 10;
                    }
                    break;
                case 10:
                    cam.SetDolly(new Vector3(-0.7f, 1f, 4f));
                    cam.Rumble = Mathf.Clamp01(1f - stepMs / 4000f);
                    if (stepMs >= 2151f && station != null && station.activeSelf)
                    {
                        // The battlestation jumps away with its turrets (the camera keeps looking where it was).
                        Remove(S(0));
                        Remove(S(1));
                        station.SetActive(false);
                    }
                    if (stepMs >= 4001f) Step = 11;
                    break;
                case 11:
                    cam.SetDolly(new Vector3(-0.7f, 1f, 3f));
                    cam.Rumble = 0f;
                    if (stepMs >= 3000f)
                    {
                        for (int i = 2; i < c.Ships.Count; i++)
                        {
                            var s = S(i);
                            if (s == null) continue;
                            s.transform.position += ToUnity(new Vector3(0, 0, -50000));
                            s.Wake();
                        }
                        var lead = S(c.Ships.Count - 10);
                        if (lead != null) cam.LookAt(ToGame(lead.transform.position) + new Vector3(2000, 500, -20000), lead.transform);
                        Step = 12;
                    }
                    break;
                case 12:
                    cam.SetDolly(new Vector3(-0.3f, 0.3f, 0.5f));
                    if (stepMs >= 5000f)
                    {
                        SetPlayerVisible(true);
                        Ship.inputLocked = false;   // Modified: the controls back (locked in Build78)
                        LeaveCutscene();
                        Step = 13;
                    }
                    break;
            }
        }
        bool animStarted, musicPlayed;
        // Modified: 78's hyperdrive fx, aligned with the station (Unity units / degrees, measured in the Inspector).
        static readonly Vector3 HyperDrivePos78 = new Vector3(0f, -150f, -450f);
        static readonly Vector3 HyperDriveRot78 = new Vector3(0f, 0f, 0f);
        const float HyperDriveScale78 = 8f;
        bool fxFixed;   // the fx keeps its own rotation (no FaceCamera)

        // Modified: 78's hyperdrive can use its own texture (only in this cutscene; the other jumps keep the normal one).
        // The image goes in Assets/Resources/ under this path, without the extension (e.g. Assets/Resources/GoF2Custom/
        // hyperdrive_78.png -> "GoF2Custom/hyperdrive_78"). Empty = no change.
        const string HyperDriveTexture78 = "GoF2Custom/hyperdrive_78";
        // Only the parts whose current texture has this name are changed (empty = every part of the fx). The names are
        // shown in the Console when a custom texture is applied ("[78] hyperdrive textures: ...").
        const string HyperDriveTextureOriginal78 = "";

        void ReplaceFxTexture78(GameObject go)
        {
            if (go == null || string.IsNullOrEmpty(HyperDriveTexture78)) return;
            var names = new System.Collections.Generic.HashSet<string>();
            var tex = Resources.Load<Texture2D>(HyperDriveTexture78);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.materials)   // per-instance copies: the prefab's materials stay untouched
                {
                    foreach (var prop in new[] { "_BaseMap", "_MainTex" })
                    {
                        if (!m.HasProperty(prop)) continue;
                        var cur = m.GetTexture(prop);
                        if (cur != null) names.Add(cur.name);
                        if (tex == null) continue;
                        if (!string.IsNullOrEmpty(HyperDriveTextureOriginal78) && (cur == null || cur.name != HyperDriveTextureOriginal78)) continue;
                        m.SetTexture(prop, tex);
                    }
                }
            if (tex != null) Debug.Log($"[78] hyperdrive textures: {string.Join(", ", names)} -> {tex.name}");
        }
        // Modified: the stop in front of the station (78), at the end of the first of its unfolding's two parts.
        const float StopAtUnfold78 = 0.5f;   // fraction of the unfolding animation (0.5 = halfway, 1 = its end)
        bool stopped78;
        float stopAtT78;

        /// <summary>78 steps 6 / 8: a top turret slides out at 0.3 u/ms until it has moved 1100 units, swinging round as it
        /// goes. True when done.</summary>
        // Modified: driven from the measured start / end poses (side = +1 / -1): X from startX to endX, rotation X
        // from TurretStartRotX78 to 0, Y kept at 0, Z from TurretStartRollDeg78 to TurretRollDeg78 with the turret's sign
        // (the remake swung it about Y).
        bool SlideTurret(NpcShip turret, float side, float startX, float endX, float rollSign, float dtMs)
        {
            if (turret == null || turretMoved >= 1100f) return true;
            turretMoved = Mathf.Min(turretMoved + 0.3f * dtMs, 1100f);
            float k = turretMoved / 1100f;
            float hostX = level.Station != null ? level.Station.transform.position.x : 0f;
            var p = turret.transform.position;
            turret.transform.position = new Vector3(hostX + side * Mathf.Lerp(startX, endX, k), p.y, p.z);
            turret.transform.rotation = Quaternion.Euler(Mathf.Lerp(TurretStartRotX78, 0f, k), 0f, rollSign * Mathf.Lerp(TurretStartRollDeg78, TurretRollDeg78, k));
            return turretMoved >= 1100f;
        }

        // 80: Alice attacks Kothar (LevelScript.c 9761-10155).
        Transform burnA, burnB;

        void Tick80(float dtMs)
        {
            var host = battlestation != null ? battlestation.transform.position : ToUnity(new Vector3(0, 0, 160000));
            switch (Step)
            {
                case 0:
                    if (Over(0))
                    {
                        EnterCutscene();
                        // Modified: the shot is on the battlestation itself (the laser only appears LaserDelay80 ms in, step 1).
                        cam.LookAtUnity(host + ToUnity(new Vector3(-20000, 5000, -35000)), battlestation != null ? battlestation.transform : battleLaser?.transform);
                        camStartZ80 = cam.Camera != null ? cam.Camera.position.z : 0f;
                        PlayAtCamera(assets?.deepScienceAttacked);   // 0x461
                        Step = 1;
                    }
                    break;
                case 1:
                    // Modified: the camera pulls back from the battlestation, still looking at it (was (1, 0, -1) sideways).
                    PullBack80(stepMs);
                    if (stepMs >= LaserDelay80)
                    {
                        // Modified: the laser starts LaserDelay80 ms into the shot (it played from the level's start), its
                        // firing animation from the beginning.
                        if (battleLaser != null)
                        {
                            battleLaser.SetActive(true);
                            // the charge-up lives in the parts' material channels (like 89's supernova front)
                            laserAnims80 = battleLaser.GetComponentsInChildren<PartAnimation>(true);
                            laserFullOn80 = new float[laserAnims80.Length];
                            foreach (var a in laserAnims80) { a.applyMaterialChannels = true; a.speed = 1f; }
                            PartAnimation.PlayOnce(battleLaser);
                            // Modified: each part plays from its start to LaserHoldAt80 of its length in LaserChargeMs80, then is
                            // held there (step 2) until the cut: its tail (fading out) no longer shows, nor a second hit.
                            for (int i = 0; i < laserAnims80.Length; i++)
                            {
                                var a = laserAnims80[i];
                                float start = Mathf.Clamp(a.loopStartMs, 0f, a.LengthMs);
                                laserFullOn80[i] = Mathf.Lerp(start, a.LengthMs, Mathf.Clamp01(LaserHoldAt80));
                                a.speed = Mathf.Max(0.01f, (laserFullOn80[i] - start) / LaserChargeMs80);
                            }
                        }
                        laserLooping80 = false;
                        Step = 2;
                    }
                    break;
                case 2:
                    PullBack80(LaserDelay80 + stepMs);   // Modified: still pulling back
                    // Modified: once charged the laser stays fully on (held at its LaserHoldAt80 pose) until the
                    // camera cuts to the Void.
                    if (!laserLooping80 && stepMs >= LaserChargeMs80 && laserAnims80 != null)
                    {
                        for (int i = 0; i < laserAnims80.Length; i++) laserAnims80[i].Hold(laserFullOn80[i]);
                        laserLooping80 = true;
                    }
                    if (stepMs >= LaserWindow80)
                    {
                        if (battleLaser != null) battleLaser.SetActive(false);
                        Step = 6;
                    }
                    break;
                case 6:
                {
                    // The hit: the camera on the impact point, the station's laser visible.
                    var hit = new Vector3(12487, -11451, 5958);
                    cam.LookAt(new Vector3(-16974, -17691, 37541), null, ToUnity(hit));
                    if (stationLaser != null)
                    {
                        stationLaser.SetActive(true);
                        // Modified: held at its first frame until step 7 fires it (it looped on its own for these 800 ms, then
                        // PlayOnce started it over: it vanished and fired again).
                        foreach (var a in stationLaser.GetComponentsInChildren<PartAnimation>(true))
                        {
                            a.applyMaterialChannels = true;
                            a.Hold(Mathf.Clamp(a.loopStartMs, 0f, a.LengthMs));
                        }
                    }
                    Step = 7;
                    break;
                }
                case 7:
                    cam.SetDolly(new Vector3(0.5f, 0f, 0.2f));
                    if (stepMs >= HitWait80)
                    {
                        if (stationLaser != null)
                        {
                            foreach (var a in stationLaser.GetComponentsInChildren<PartAnimation>(true)) a.applyMaterialChannels = true;   // Modified: its fades
                            PartAnimation.PlayOnce(stationLaser);
                        }
                        if (level.Station != null)
                        {
                            PartAnimation.PlayOnce(level.Station);   // the station's explosion animation
                            LoopEmitters80(level.Station);   // Modified: not the flames, they go on looping
                        }
                        Step = 8;
                    }
                    break;
                case 8:
                    if (stepMs >= HitWait80)
                    {
                        var hitPoint = new Vector3(12487, -11451, 5958);
                        // Modified: the blast HitBlastOffset80 from the hit point (was the hit point itself).
                        Explosion.Spawn(0, ToUnity(hitPoint + HitBlastOffset80), Vector3.forward, 3f, CombatAssets.Pick(combat?.explosionBig), true);   // sound 18
                        // Level+0x58 at the hit point and Level+0x5c at hit + (4500, 0, 1000) (record 24, both emitting for
                        // the rest of the level; the second point drifts +2 u/ms along x in state 9).
                        burnA = new GameObject("Deep science burn A").transform;
                        burnA.position = ToUnity(hitPoint);
                        new WreckBurn(burnA, WreckBurn.DeepScience).SetEmitting(true);
                        burnB = new GameObject("Deep science burn B").transform;
                        burnB.position = ToUnity(hitPoint + new Vector3(4500, 0, 1000));
                        new WreckBurn(burnB, WreckBurn.DeepScience).SetEmitting(true);
                        // Modified: the fires' own pose under each burn point, matched to the original (Inspector values).
                        PlaceBurn80(burnA, BurnPosA80, BurnRotA80);
                        PlaceBurn80(burnB, BurnPosB80, BurnRotB80);
                        // Modified: fire B rides the blown-off part 4 of the station (it drifted +2 u/ms along x on its own).
                        var part4 = FindStationPart80(BurnBFollows80);
                        if (part4 != null) burnB.SetParent(part4, true);
                        else Debug.LogWarning($"[80] {BurnBFollows80} not found: fire B drifts on its own");
                        Step = 9;
                    }
                    break;
                case 9:
                    if (burnB != null && burnB.parent == null) burnB.position += ToUnity(new Vector3(2f * dtMs, 0, 0)) - ToUnity(Vector3.zero);   // Modified: only without part 4
                    if (stepMs >= 8000f)
                    {
                        LeaveCutscene();
                        if (stationLaser != null) stationLaser.SetActive(false);
                        Step = 10;
                    }
                    break;
                case 10:
                    if (Over(9))
                    {
                        EnterCutscene();
                        cam.LookAtUnity(host + ToUnity(new Vector3(20000, 5000, -20000)), battlestation != null ? battlestation.transform : null);
                        jumpShotMs80 = 0f;
                        jumpShotStartZ80 = cam.Camera != null ? cam.Camera.position.z : 0f;
                        Step = 11;
                    }
                    break;
                case 11:
                    // Modified: the camera moves along Unity Z only, JumpShotTravel80 further by the end of the cutscene (steps 11-13;
                    // was the dolly (0.5, 0, 0.2) drifting on).
                    JumpShot80(dtMs);
                    if (stepMs >= 4000f)
                    {
                        Explosion.Spawn(0, host + ToUnity(new Vector3(-4000, 5000, -6000)), Vector3.forward, 3f, CombatAssets.Pick(combat?.explosionBig), true);
                        Step = 12;
                    }
                    break;
                case 12:
                    JumpShot80(dtMs);   // Modified
                    cam.Rumble = Mathf.Clamp01(stepMs / 8000f);
                    if (stepMs >= 8000f)
                    {
                        // Modified: like 78's jump: aligned with the battlestation instead of facing the camera, and 78's custom
                        // texture (Resources/GoF2Custom/hyperdrive_78) when there is one.
                        SpawnHyperDrive(ToGame(host) + new Vector3(0, 0, -10000), 20f, texture78: true);
                        if (fx != null)
                        {
                            fx.transform.SetPositionAndRotation(host + HyperDrivePos80, Quaternion.Euler(HyperDriveRot80));
                            fx.transform.localScale = Vector3.one * HyperDriveScale80;
                            fxFixed = true;
                        }
                        Step = 13;
                    }
                    break;
                case 13:
                    JumpShot80(dtMs);   // Modified
                    cam.Rumble = Mathf.Clamp01(1f - stepMs / 10000f);
                    if (stepMs >= 2151f && battlestation != null && battlestation.activeSelf) battlestation.SetActive(false);
                    if (stepMs >= 10001f) { LeaveCutscene(); Step = 14; }
                    break;
            }
        }

        // Modified: 80's last shot (the battlestation's jump): the camera goes along Unity Z from where it starts to
        // JumpShotTravel80 further over the whole shot (steps 11 + 12 + 13 = 4000 + 8000 + 10001 ms), X and Y unchanged.
        const float JumpShotTravel80 = 8000f;   // Unity metres along +Z: from the shot's start (Z -7000) to Z 1000
        const float JumpShotMs80 = 4000f + 8000f + 10001f;
        float jumpShotMs80, jumpShotStartZ80;

        void JumpShot80(float dtMs)
        {
            cam.SetDolly(Vector3.zero);
            jumpShotMs80 += dtMs;
            if (cam.Camera == null) return;
            var p = cam.Camera.position;
            cam.Camera.position = new Vector3(p.x, p.y, Mathf.Lerp(jumpShotStartZ80, jumpShotStartZ80 + JumpShotTravel80, Mathf.Clamp01(jumpShotMs80 / JumpShotMs80)));
        }

        // Modified: the deep science station's two fires (the "Deep science burn" particle systems under burn A / B), local
        // position and rotation as set in the Inspector.
        static readonly Vector3 BurnPosA80 = new Vector3(100f, 117f, -117f), BurnRotA80 = new Vector3(0f, 30f, 0f);
        static readonly Vector3 BurnPosB80 = new Vector3(-200f, 80f, 0f), BurnRotB80 = new Vector3(0f, 30f, 0f);

        const string BurnBFollows80 = "v_station_deep_science_explosion_anim_part4";   // Modified: the part fire B rides

        // Modified: the damaged thrusters' flames (FlameSource80, one mesh for all of them, one triangle list per material)
        // split into one piece per engine glow of the explosion (explosion_anim_add parts FlameTargets80; part 1 is the
        // lights around the station): each flame (its layers together) goes to the nearest engine glow (part 0, the whole station, only gets
        // what no engine glow is within FlameReach80 of). Each piece is a child of its glow at 0, 0, 0 (the meshes are all in
        // the station's frame), shows only while its glow does (part 2's goes out 1 s into the explosion) and scrolls with
        // the original. Part 2's engine has no flame in the mesh: it gets a copy of part 4's, at part 7's place mirrored
        // (its twin across the station), then FlameCopyPos80 / Rot80 / Scale80 in part 2's frame. The mesh must be Read/Write enabled
        // (v_station_deep_science_damaged_emitters_anim_add.fbx, Model tab); without it the flames stay as they were.
        const string FlameSource80 = "v_station_deep_science_damaged_emitters_anim_add_part0";
        const string FlameTargetPrefix80 = "v_station_deep_science_explosion_anim_add_part";
        static readonly int[] FlameTargets80 = { 0, 2, 3, 4, 5, 6, 7 };
        const int FlameCopyFrom80 = 4, FlameCopyTo80 = 2, FlameCopyTwin80 = 7;   // part 2 gets part 4's flame (part 3's is smaller), placed by part 7's
        const float FlameReach80 = 300f;   // metres: how far from an engine glow its flame may reach
        // part 2's flame: local position / rotation (degrees) / scale under v_station_deep_science_explosion_anim_add_part2
        static readonly Vector3 FlameCopyPos80 = new Vector3(2099.4f, 1.6f, 28f), FlameCopyRot80 = new Vector3(0f, 119f, 0f), FlameCopyScale80 = Vector3.one;   // Inspector values
        readonly List<(Renderer piece, Renderer glow)> flamePieces80 = new List<(Renderer, Renderer)>();
        Renderer flameSource80;
        MaterialPropertyBlock flameBlock80, glowBlock80;

        void MakeFlamePieces80()
        {
            flamePieces80.Clear();
            flameSource80 = null;
            var source = FindStationPart80(FlameSource80);
            var mf = source != null ? source.GetComponent<MeshFilter>() : null;
            var sr = source != null ? source.GetComponent<Renderer>() : null;
            if (mf == null || sr == null || mf.sharedMesh == null) { Debug.LogWarning($"[80] flames: {FlameSource80} not found"); return; }
            var mesh = mf.sharedMesh;
            if (!mesh.isReadable) { Debug.LogWarning($"[80] flames: turn on Read/Write for {mesh.name}'s model to split them"); return; }

            var glows = new List<(int part, Renderer r, Bounds bounds)>();
            foreach (int k in FlameTargets80)
            {
                var t = FindStationPart80(FlameTargetPrefix80 + k);
                var r = t != null ? t.GetComponent<Renderer>() : null;
                if (r != null) glows.Add((k, r, r.bounds));
                else Debug.LogWarning($"[80] flames: {FlameTargetPrefix80}{k} not found");
            }
            if (glows.Count == 0) return;
            int largest = 0;   // part 0's glow (the whole station)
            for (int g = 1; g < glows.Count; g++)
                if (glows[g].bounds.size.sqrMagnitude > glows[largest].bounds.size.sqrMagnitude) largest = g;

            var verts = mesh.vertices;
            int subs = mesh.subMeshCount;
            var subTris = new int[subs][];
            for (int m = 0; m < subs; m++) subTris[m] = mesh.GetTriangles(m);

            // The flames, each one whole: triangles joined by shared vertex positions, then the pieces that overlap (a flame's
            // layers, one per material) merged. By single triangles, a flame's outer layer had gone to another glow.
            var key = new Dictionary<Vector3Int, int>();
            var parent = new List<int>();
            int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
            void Union(int x, int y) { x = Find(x); y = Find(y); if (x != y) parent[x] = y; }
            var vertexNode = new int[verts.Length];
            for (int v = 0; v < verts.Length; v++)
            {
                var k = Vector3Int.RoundToInt(verts[v] * 100f);
                if (!key.TryGetValue(k, out int node)) { node = parent.Count; parent.Add(node); key[k] = node; }
                vertexNode[v] = node;
            }
            for (int m = 0; m < subs; m++)
                for (int i = 0; i + 2 < subTris[m].Length; i += 3)
                {
                    Union(vertexNode[subTris[m][i]], vertexNode[subTris[m][i + 1]]);
                    Union(vertexNode[subTris[m][i]], vertexNode[subTris[m][i + 2]]);
                }
            var comp = new Dictionary<int, int>();   // root -> cluster
            var clusterBounds = new List<Bounds>();
            var clusterTris = new List<List<(int m, int i)>>();
            for (int m = 0; m < subs; m++)
                for (int i = 0; i + 2 < subTris[m].Length; i += 3)
                {
                    int root = Find(vertexNode[subTris[m][i]]);
                    if (!comp.TryGetValue(root, out int c))
                    {
                        c = clusterBounds.Count; comp[root] = c;
                        clusterBounds.Add(new Bounds(verts[subTris[m][i]], Vector3.zero));
                        clusterTris.Add(new List<(int, int)>());
                    }
                    var cb = clusterBounds[c];
                    cb.Encapsulate(verts[subTris[m][i]]); cb.Encapsulate(verts[subTris[m][i + 1]]); cb.Encapsulate(verts[subTris[m][i + 2]]);
                    clusterBounds[c] = cb;
                    clusterTris[c].Add((m, i));
                }
            for (bool merged = true; merged;)   // overlapping pieces are one flame
            {
                merged = false;
                for (int x = 0; x < clusterBounds.Count && !merged; x++)
                    for (int y = x + 1; y < clusterBounds.Count && !merged; y++)
                        if (clusterBounds[x].Intersects(clusterBounds[y]))
                        {
                            var bx = clusterBounds[x]; bx.Encapsulate(clusterBounds[y]); clusterBounds[x] = bx;
                            clusterTris[x].AddRange(clusterTris[y]);
                            clusterBounds.RemoveAt(y); clusterTris.RemoveAt(y);
                            merged = true;
                        }
            }

            // each flame to the nearest engine glow (part 2 gets part 4's, below; part 0 only what no engine glow is near)
            var perGlow = new List<int>[glows.Count][];
            var glowFlameBounds = new Bounds?[glows.Count];
            for (int g = 0; g < glows.Count; g++) { perGlow[g] = new List<int>[subs]; for (int m = 0; m < subs; m++) perGlow[g][m] = new List<int>(); }
            for (int c = 0; c < clusterBounds.Count; c++)
            {
                var centre = source.TransformPoint(clusterBounds[c].center);
                int best = -1; float bestD = float.MaxValue, bestSize = float.MaxValue;
                for (int g = 0; g < glows.Count; g++)
                {
                    if (g == largest || glows[g].part == FlameCopyTo80) continue;
                    float d = glows[g].bounds.SqrDistance(centre);
                    float size = glows[g].bounds.size.sqrMagnitude;
                    if (d < bestD - 1e-4f || (Mathf.Abs(d - bestD) <= 1e-4f && size < bestSize)) { bestD = d; bestSize = size; best = g; }
                }
                if (best < 0 || bestD > FlameReach80 * FlameReach80) best = largest;
                foreach (var (m, i) in clusterTris[c]) { perGlow[best][m].Add(subTris[m][i]); perGlow[best][m].Add(subTris[m][i + 1]); perGlow[best][m].Add(subTris[m][i + 2]); }
                if (glowFlameBounds[best] is Bounds fb) { fb.Encapsulate(clusterBounds[c]); glowFlameBounds[best] = fb; } else glowFlameBounds[best] = clusterBounds[c];
            }

            // Part 2's engine has no flame in the mesh: part 4's flame, moved to where part 7's (part 2's twin across the
            // station) would be mirrored, then FlameCopyPos80 / Rot80 / Scale80 in part 2's frame.
            int from = glows.FindIndex(x => x.part == FlameCopyFrom80), twin = glows.FindIndex(x => x.part == FlameCopyTwin80);
            Vector3 copyShift = Vector3.zero;
            if (from >= 0 && glowFlameBounds[from] is Bounds fromB)
            {
                if (twin >= 0 && glowFlameBounds[twin] is Bounds twinB) { var t = twinB.center; t.x = -t.x; copyShift = t - fromB.center; }
                else Debug.LogWarning("[80] flames: part " + FlameCopyTwin80 + " has no flame to place part 2's by");
            }

            for (int g = 0; g < glows.Count; g++)
            {
                bool copied = glows[g].part == FlameCopyTo80;
                var lists = copied ? (from >= 0 ? perGlow[from] : null) : perGlow[g];
                if (lists == null) continue;
                int count = 0; foreach (var l in lists) count += l.Count / 3;
                if (count == 0) continue;
                var pieceMesh = BuildFlamePiece80(mesh, lists, copied ? copyShift : Vector3.zero);
                var go = new GameObject("Flame (" + glows[g].r.name + ")");
                // where the flames are (the source's pose), then onto its glow: 0, 0, 0 there when both sit in the station's frame
                go.transform.SetParent(source.parent, false);
                go.transform.localPosition = source.localPosition;
                go.transform.localRotation = source.localRotation;
                go.transform.localScale = source.localScale;
                go.transform.SetParent(glows[g].r.transform, true);
                if (copied && (FlameCopyPos80 != Vector3.zero || FlameCopyRot80 != Vector3.zero || FlameCopyScale80 != Vector3.one))
                {
                    go.transform.localPosition = FlameCopyPos80;
                    go.transform.localEulerAngles = FlameCopyRot80;
                    go.transform.localScale = FlameCopyScale80;
                }
                go.AddComponent<MeshFilter>().sharedMesh = pieceMesh;
                var pr = go.AddComponent<MeshRenderer>();
                pr.sharedMaterials = sr.sharedMaterials;   // every material of the flames (one per triangle list)
                pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                pr.receiveShadows = false;
                flamePieces80.Add((pr, glows[g].r));
            }
            sr.enabled = false;   // the pieces stand in for it (its PartAnimation still scrolls the texture, copied below)
            flameSource80 = sr;
            flameBlock80 = new MaterialPropertyBlock();
            glowBlock80 = new MaterialPropertyBlock();
        }

        /// <summary>A mesh of the given triangles (per material) of 'mesh', every vertex channel kept, moved by 'shift'.</summary>
        static Mesh BuildFlamePiece80(Mesh mesh, List<int>[] lists, Vector3 shift)
        {
            var verts = mesh.vertices; var normals = mesh.normals; var tangents = mesh.tangents; var colors = mesh.colors;
            var uv = new List<Vector2>[4];
            for (int c = 0; c < 4; c++) { uv[c] = new List<Vector2>(); mesh.GetUVs(c, uv[c]); }
            var map = new Dictionary<int, int>();
            var pv = new List<Vector3>(); var pn = new List<Vector3>(); var pt = new List<Vector4>(); var pc = new List<Color>();
            var pu = new List<Vector2>[4]; for (int c = 0; c < 4; c++) pu[c] = new List<Vector2>();
            var subTris = new List<int[]>();
            foreach (var list in lists)
            {
                var t = new int[list.Count];
                for (int i = 0; i < list.Count; i++)
                {
                    int v = list[i];
                    if (!map.TryGetValue(v, out int nv))
                    {
                        nv = pv.Count; map[v] = nv;
                        pv.Add(verts[v] + shift);
                        if (normals.Length == verts.Length) pn.Add(normals[v]);
                        if (tangents.Length == verts.Length) pt.Add(tangents[v]);
                        if (colors.Length == verts.Length) pc.Add(colors[v]);
                        for (int c = 0; c < 4; c++) if (uv[c].Count == verts.Length) pu[c].Add(uv[c][v]);
                    }
                    t[i] = nv;
                }
                subTris.Add(t);
            }
            var piece = new Mesh { name = mesh.name + " (flame)" };
            if (pv.Count > 65535) piece.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            piece.SetVertices(pv);
            if (pn.Count == pv.Count) piece.SetNormals(pn);
            if (pt.Count == pv.Count) piece.SetTangents(pt);
            if (pc.Count == pv.Count) piece.SetColors(pc);
            for (int c = 0; c < 4; c++) if (pu[c].Count == pv.Count && pv.Count > 0) piece.SetUVs(c, pu[c]);
            piece.subMeshCount = subTris.Count;
            for (int m = 0; m < subTris.Count; m++) piece.SetTriangles(subTris[m], m);
            piece.RecalculateBounds();
            return piece;
        }

        void UpdateFlamePieces80()
        {
            if (flameSource80 == null || flamePieces80.Count == 0) return;
            flameSource80.GetPropertyBlock(flameBlock80);
            foreach (var (piece, glow) in flamePieces80)
            {
                if (piece == null) continue;
                piece.SetPropertyBlock(flameBlock80);
                piece.enabled = GlowShown80(glow);
            }
        }

        /// <summary>The glow is drawn: active, enabled, and its `extra` fade (PartAnimation's _Fade / _Color) not at 0.</summary>
        bool GlowShown80(Renderer glow)
        {
            if (glow == null || !glow.enabled || !glow.gameObject.activeInHierarchy) return false;
            glow.GetPropertyBlock(glowBlock80);
            if (glowBlock80.HasFloat("_Fade") && glowBlock80.GetFloat("_Fade") < 0.01f) return false;
            if (glowBlock80.HasColor("_Color"))
            {
                var c = glowBlock80.GetColor("_Color");
                if (c.a < 0.01f || c.maxColorComponent < 0.01f) return false;
            }
            return true;
        }

        Transform FindStationPart80(string name)
        {
            if (level.Station == null) return null;
            foreach (var t in level.Station.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith(name)) return t;
            return null;
        }

        static void PlaceBurn80(Transform burn, Vector3 localPos, Vector3 localEuler)
        {
            if (burn == null) return;
            bool found = false;
            foreach (var ps in burn.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps.transform == burn || ps.transform.parent != burn) continue;   // the fire itself, right under the point
                ps.transform.localPosition = localPos;
                ps.transform.localEulerAngles = localEuler;
                found = true;
            }
            if (!found) Debug.LogWarning($"[80] {burn.name}: no fire found under it, its position was not changed");
        }

        // Modified: 80's hyperdrive fx (the battlestation jumping away), Unity units relative to the battlestation /
        // degrees (Inspector: world (0, -150, -7600) with the battlestation at (0, 0, -8000)).
        static readonly Vector3 HyperDrivePos80 = new Vector3(0f, -150f, 400f);
        static readonly Vector3 HyperDriveRot80 = new Vector3(0f, 0f, 0f);
        const float HyperDriveScale80 = 8f;

        // Modified: the hit's explosion (step 8) lands ExplosionAt80 ms after the cutscene's start, when the explosion is
        // heard in DeepScienceAttacked (played at the start); the first shot is shortened to fit: step 1 (LaserDelay80) +
        // step 2 (LaserWindow80) + step 7 (HitWait80) + step 8 (HitWait80) = ExplosionAt80.
        const float ExplosionAt80 = 6900f;
        const float HitWait80 = 800f;        // steps 7 and 8 (unchanged from the original)
        const float LaserDelay80 = ExplosionAt80 - LaserWindow80 - 2f * HitWait80;   // Modified: ms until the laser fires (3600 now; was step 1's 3000)
        // Modified: the battlestation's turrets (StationTurrets index -> position relative to the battlestation, Unity units,
        // and rotation, degrees), measured in 80 from the Inspector with the battlestation at (0, 0, -8000), unrotated
        // (OrbitLayout.RotationToUnity(0)). The shield generators keep the table's pose. Read through StationTurretPose
        // (80 and PlayerHull's Valkyrie hull).
        /// <summary>Modified: the deep science station's thruster flames (its *_emitters_anim_add meshes: a texture scroll over
        /// 11 s) keep looping. Holding the whole station until the hit froze them, and its PlayOnce would stop them after
        /// one scroll.</summary>
        static void LoopEmitters80(GameObject station)
        {
            foreach (var a in station.GetComponentsInChildren<PartAnimation>(true))
            {
                if (a.gameObject.name.IndexOf("_emitters", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                a.loop = true;
                a.play = true;
            }
        }

        static readonly Dictionary<int, (Vector3 pos, Vector3 rot)> TurretPoses80 = new Dictionary<int, (Vector3 pos, Vector3 rot)>
        {
            { 0, (new Vector3(215f, 1165f, -370f), new Vector3(0f, 180f, 87f)) },
            { 1, (new Vector3(-215f, 1165f, -370f), new Vector3(0f, 180f, -87f)) },
            { 4, (new Vector3(170f, -1138f, 30f), new Vector3(0f, 180f, 90f)) },
            { 5, (new Vector3(-170f, -1138f, 30f), new Vector3(0f, 180f, -90f)) },
            { 6, (new Vector3(1486f, -532f, -188f), new Vector3(-180f, 0f, 0f)) },
            { 7, (new Vector3(-1485f, -536f, -191f), new Vector3(-180f, 0f, 0f)) },
            { 8, (new Vector3(-1485f, 225f, -191f), new Vector3(0f, -180f, 0f)) },
            { 11, (new Vector3(1485f, 225f, -191f), new Vector3(0f, -180f, 0f)) },
        };

        const float LaserChargeMs80 = 1500f; // Modified: ms the laser's charge-up animation takes; then it stays on until the cut
        bool laserLooping80;   // the laser is held at its LaserHoldAt80 pose
        // Modified: where in its animation the laser is held after the charge-up (0 = its start, 1 = its last frame, where
        // it has already faded out): the charge plays up to there in LaserChargeMs80.
        const float LaserHoldAt80 = 0.85f;
        // Modified: the hit's blast from the hit point, game units (Unity (740, -550, -370), set in the Inspector).
        static readonly Vector3 HitBlastOffset80 = new Vector3(2313f, 451f, 1442f);
        PartAnimation[] laserAnims80;
        float[] laserFullOn80;

        const float LaserWindow80 = 1700f;   // Modified: ms from the laser appearing to the camera cut (step 2; was 2000): it fires 0.3 s later, the cut stays put
        const float CamPullBack80 = 3250f;   // Modified: Unity metres along +Z over the shot (from Z -6250 to -3000; X and Y stay put)
        float camStartZ80;

        /// <summary>Modified: 80's first shot pulls straight back along Unity Z, from where it starts by CamPullBack80 over the
        /// whole shot (LaserDelay80 + LaserWindow80), X and Y unchanged, still looking at the battlestation.</summary>
        void PullBack80(float shotMs)
        {
            cam.SetDolly(Vector3.zero);
            if (cam.Camera == null) return;
            float k = Mathf.Clamp01(shotMs / (LaserDelay80 + LaserWindow80));
            var p = cam.Camera.position;
            cam.Camera.position = new Vector3(p.x, p.y, Mathf.Lerp(camStartZ80, camStartZ80 + CamPullBack80, k));
        }

        // Modified: 81's hyperdrive fx (the battlestation dropping out of hyperspace), Unity units relative to the
        // battlestation (at the origin) / degrees, from the Inspector.
        static readonly Vector3 HyperDrivePos81 = new Vector3(0f, -160f, -500f);
        static readonly Vector3 HyperDriveRot81 = new Vector3(0f, 0f, 0f);
        const float HyperDriveScale81 = 8f;
        const float BattlestationAppears81 = 12201f;   // Modified: ms into 81 when the battlestation shows (was 12 001)

        // 81: Alice stranded in the Void (LevelScript.c 1899-1998).
        void Tick81(float dtMs)
        {
            switch (Step)
            {
                case 0:
                    cam.Rumble = Mathf.Clamp01(T / 10000f);
                    if (T >= 10001f)
                    {
                        // Modified: like 78 / 80: aligned with the battlestation instead of facing the camera, with 78's custom
                        // texture when there is one.
                        SpawnHyperDrive(new Vector3(0, 0, 10000), 30f, texture78: true);
                        if (fx != null)
                        {
                            var bs = battlestation != null ? battlestation.transform.position : Vector3.zero;
                            fx.transform.SetPositionAndRotation(bs + HyperDrivePos81, Quaternion.Euler(HyperDriveRot81));
                            fx.transform.localScale = Vector3.one * HyperDriveScale81;
                            fxFixed = true;
                        }
                        Step = 1;
                    }
                    break;
                case 1:
                    cam.Rumble = Mathf.Clamp01(1f - stepMs / 4000f);
                    // Modified: the battlestation appears 0.2 s later (12 201 ms, was 12 001), after the hyperdrive's flash.
                    if (T >= BattlestationAppears81 && battlestation != null && !battlestation.activeSelf) battlestation.SetActive(true);
                    if (T >= 14001f) { cam.Rumble = 0f; Step = 2; }
                    break;
                case 2:
                    if (Over(3)) { c.Fade(false, Color.black, 5000f); Step = 3; }
                    break;
                case 3:
                    if (c.FadeDone)
                    {
                        // nextCampaignMission (-> 82), departStation(100), the station module.
                        loading = true;
                        Story.Advance(level.Database);
                        Session.StationIndex = 100;
                        Session.AttackedStations.Remove(100);   // Station::setAttackedFriends(false) on the new station
                        Session.ComingFromVoid = false;
                        level.Dock();
                    }
                    break;
            }
        }
    }
}
