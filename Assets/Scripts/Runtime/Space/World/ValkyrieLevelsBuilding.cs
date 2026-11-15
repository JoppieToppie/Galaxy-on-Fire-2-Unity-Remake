// ValkyrieLevelsBuilding.cs
// ValkyrieLevels: building (Level::createCampaignMission + the LevelScript constructor) (part of the partial ValkyrieLevels; its fields are in ValkyrieLevels.cs).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class ValkyrieLevels
    {
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
                level.MovePlayer(At(p), Quaternion.LookRotation(Dir(-p), Vector3.up));
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
            // 0x1b (4): Level::init counts the enemies without the outpost (PlayerFixedObject sets KIPlayer[0x3d], which
            // Level::init skips), so 6 pirates; its death still takes one off (Level::enemyDied). Any 3 kills win, the
            // station usually still standing. The remake counts the outpost too (7): < 3 is the same win (#131: the
            // pirates only gave Khador away once the station blew up).
            c.WinObjective = () => c.EnemiesLeft < 3;
        }
        void Build64()
        {
            // After the normal arrival the player is pushed 40 000 back along his heading.
            var back = PlayerGame - PlayerDirGame * 40000f;
            level.MovePlayer(At(back), Player.rotation);
            var route = new Route(false);
            route.points.Add(new Vector3(100000, 0, 0));
            var khador = c.SpawnShip(0, 38, Vector3.zero, false,
                                     s => { s.alwaysFriend = true; s.nameText = 1617; s.route = route.Clone(); s.noLoot = true; });
            khador.Place(At(Vector3.zero), Dir(new Vector3(1, 0, 0)));
            for (int i = 0; i < 8; i++)
            {
                var at = new Vector3(Sign() * (1000 + R(1500)), Sign() * (1000 + R(1500)), Sign() * (1000 + R(1500)));
                var s = c.SpawnShip(Standing.Pirate, NpcTables.RandomFighter(Standing.Pirate), at, false, sp => sp.route = route.Clone());
                s.Place(At(at), Dir(new Vector3(1, 0, 0)));
            }
            c.WinObjective = () => c.Radio != null && c.Radio.LastOver;   // 0x16
            c.FailObjective = () => c.ShipDestroyed(0);                          // 1 (0): Khador dead
        }
        void Build65()
        {
            var p = new Vector3(0, 0, 170000);
            level.MovePlayer(At(p), Quaternion.LookRotation(Dir(-p), Vector3.up));
            var route = new Route(false);
            route.points.Add(Vector3.zero);
            var khador = c.SpawnShip(0, 38, p + new Vector3(-3000, 0, 0), false,
                                     s => { s.alwaysFriend = true; s.nameText = 1617; s.route = route; s.hitpoints = 9999999; s.noLoot = true; });
            khador.Place(At(p) + ToUnity(new Vector3(-3000, 0, 0)), Dir(new Vector3(0, 0, -1)));   // OrbitScale: beside the player
        }
        void Build67()
        {
            var w = new Vector3(220000, -20000, -10000);
            var route = new Route(false);
            route.points.Add(w);
            c.SetPlayerRoute(route);
            // [0] the outpost, friendly (Corny breaks in, it must not be destroyed).
            var outpost = Outpost(w, s => { s.alwaysFriend = true; s.asleep = true; });
            float grown = outpost != null ? outpost.StructureScale : 1f;   // OrbitScale: NpcShip grows the outpost (a structure)
            // [1] beside the outpost, [2-4] around it, [5-9] parked far away (5-8 are brought in later): asleep, detection 150 000.
            void Pirate(Vector3 at, bool jitter) =>
                c.SpawnShip(Standing.Pirate, NpcTables.RandomFighter(Standing.Pirate), at, jitter, s => s.asleep = true).detectRange = 150000f;
            // OrbitScale: their spots off the outpost (a structure) grow with it, so none is inside it (scaled: CampaignLevel's
            // +-20 000 jitter drawn here, times the scale).
            Pirate(w + new Vector3(30000 * grown, 0, 0), false);
            for (int i = 2; i <= 4; i++)
                if (grown != 1f) Pirate(w + new Vector3(R(40000) - 20000, R(40000) - 20000, R(40000) - 20000) * grown, false);
                else Pirate(w, true);
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
            trot.Place(At(q), Dir(q.normalized));
            for (int i = 0; i < 4; i++) c.SpawnShip(0, NpcTables.RandomFighter(0), new Vector3(0, 0, 20000), true);
            c.WinObjective = () => c.Radio != null && c.Radio.LastOver;   // 0x16
        }
        void Build70()
        {
            // Level case 0x46: the player 120 000 further back along his heading.
            var p = PlayerGame - PlayerDirGame * 120000f;
            level.MovePlayer(At(p), Player.rotation);
            // landmarks[1]: the jumpgate (the visible one, else the hidden arrival gate); Trot flies to it.
            // (The raw layout fields: the original's coordinates, like p; At / the spawn map them.)
            var target = level.Layout.hasJumpgate ? level.Layout.jumpgate : level.Layout.hiddenJumpgate;
            var at = p + (target - p) / 4f;
            var route = new Route(false);
            route.points.Add(target);
            int hull = Mathf.RoundToInt(NpcTables.Hull(0, 12) * 2.5f);
            var trot = c.SpawnShip(0, 12, at, false, s => { s.alwaysFriend = true; s.nameText = 1631; s.route = route; s.hitpoints = hull; s.noLoot = true; });
            trot.Place(At(at), Dir((target - at).normalized));
            // Level::assignGuns, mission 0x46: every ship but the wingmen fires the Disruptor Laser at x2.5 (NpcTables.GunDamage).
            c.WinObjective = () => c.ShipDestroyed(0);   // 1 (0)
        }
        void Build73()
        {
            var start = new Vector3(-30000, 0, 150000);
            level.MovePlayer(At(start), Quaternion.LookRotation(Dir(new Vector3(170000, 0, -50000) - start), Vector3.up));
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
            var off0 = new Vector3(TurretSide0_78 * TurretStartX0_78 / M, t0.pos.y, t0.pos.z * mz);
            var off1 = new Vector3(TurretSide1_78 * TurretStartX1_78 / M, t1.pos.y, t1.pos.z * mz);
            var tr0 = BattleTurret(false, 0, host78 + off0, new Vector3(0, 0, Mathf.Sign(t0.rz) * TurretRollDeg78 * Mathf.Deg2Rad * mr));
            var tr1 = BattleTurret(false, 0, host78 + off1, new Vector3(0, 0, Mathf.Sign(t1.rz) * TurretRollDeg78 * Mathf.Deg2Rad * mr));
            PlaceOnStation78(tr0, off0);   // OrbitScale: on the grown battlestation
            PlaceOnStation78(tr1, off1);
            // Modified: the start pose (rotation X = TurretStartRotX78, Y = 0, Z = +-TurretStartRollDeg78; the Z sign is the
            // spawn's: 87 -> +, 273 (= -87) -> -).
            if (tr0 != null) { rollSign0_78 = tr0.transform.eulerAngles.z > 180f ? -1f : 1f; tr0.transform.rotation = Quaternion.Euler(TurretStartRotX78, 0f, rollSign0_78 * TurretStartRollDeg78); }
            if (tr1 != null) { rollSign1_78 = tr1.transform.eulerAngles.z > 180f ? -1f : 1f; tr1.transform.rotation = Quaternion.Euler(TurretStartRotX78, 0f, rollSign1_78 * TurretStartRollDeg78); }
            SetTurretGun(tr0, TurretGunIn78);   // Modified: the guns drawn in until the slide is half way (SlideTurret)
            SetTurretGun(tr1, TurretGunIn78);
            for (int i = 2; i < 22; i++)
            {
                var at = new Vector3(-17000 + 2000 * (i - 2) + R(2000), R(10000) - 5000, 155000 + R(10000));
                var s = c.SpawnShip(Standing.Pirate, NpcTables.RandomFighter(Standing.Pirate), at, false, sp => { sp.alwaysEnemy = true; sp.asleep = true; });
                s.Place(At(at), Dir(new Vector3(0, 0, -1)));
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
            cam.LookAtUnity(At(new Vector3(2000, -1500, 16000)), level.Station != null ? level.Station.transform : Player);   // OrbitScale: framing the grown station
            PauseAnimation(level.Station);
            Step = 1;
        }
        /// <summary>OrbitScale: a battlestation turret of 78 at its offset (game units, from the station) grown with the station.</summary>
        void PlaceOnStation78(NpcShip turret, Vector3 offset)
        {
            if (turret == null || !OrbitScale.Active) return;
            var st = level.Station != null ? level.Station.transform.position : Vector3.zero;
            turret.transform.position = st + ToUnity(offset) * StructureScale;
        }
        /// <summary>Modified: a battlestation turret's muzzle flash and shots at the tip of each barrel (both started inside
        /// the turret): a flash at each tip with every shot, the shots from one barrel and the other in turn.</summary>
        static void SetTurretMuzzles(NpcShip turret)
        {
            if (turret == null) return;
            var part = TurretGun(turret);
            if (part == null) return;
            turret.Rig?.SetMuzzles(part, TurretMuzzleA, TurretMuzzleB);
            var a = new GameObject("muzzle A").transform;
            var b = new GameObject("muzzle B").transform;
            a.SetParent(part, false); a.localPosition = TurretMuzzleA;
            b.SetParent(part, false); b.localPosition = TurretMuzzleB;
            turret.Turret?.SetShotMuzzles(a, b);
        }
        static Transform TurretGun(NpcShip turret)
        {
            if (turret == null) return null;
            foreach (var t in turret.GetComponentsInChildren<Transform>(true))
                if (t.name == TurretGunPart) return t;
            return null;
        }
        /// <summary>Modified: the guns of 80 by their turrets' pitch: out when level, drawn back into the base as they rise.</summary>
        void TurretGunsByPitch()
        {
            foreach (var (ship, gun) in guns80)
            {
                if (ship == null || gun == null || ship.Turret == null || ship.Turret.Aim == null) continue;
                var aim = ship.Turret.Aim;
                float up = float.IsNaN(aim.pitchMax) || aim.pitchMax <= 0f ? 0f : Mathf.Clamp01(aim.Pitch / aim.pitchMax);
                gun.localPosition = Vector3.Lerp(TurretGunOut, TurretGunRaised, up);
            }
        }
        /// <summary>Modified: places a battlestation turret's gun part (local position).</summary>
        static void SetTurretGun(NpcShip turret, Vector3 localPosition)
        {
            if (turret == null) return;
            foreach (var t in turret.GetComponentsInChildren<Transform>(true))
                if (t.name == TurretGunPart) { t.localPosition = localPosition; return; }
        }
        void Build79()
        {
            for (int i = 0; i < 7; i++)
                c.SpawnShip(Standing.Void, NpcTables.RandomFighter(Standing.Void),
                            new Vector3(Sign() * (20000 + R(80000)), Sign() * (20000 + R(80000)), Sign() * (20000 + R(80000))), false, s => s.alwaysEnemy = true);
        }
        void Build80()
        {
            guns80.Clear();
            var p = new Vector3(-70000, 0, -30000);
            level.MovePlayer(At(p), Quaternion.LookRotation(Dir(new Vector3(1, 0, 1).normalized), Vector3.up));
            var host = Host80;
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
                    s.transform.SetPositionAndRotation(battlestation.transform.position + pose.offset * StructureScale, pose.rotation);   // OrbitScale: on the grown hull
                }
                if (!t.shield)
                {
                    // Modified: the gun out, as at the end of 78's slide; a flash and the shots at each barrel's tip.
                    SetTurretGun(s, TurretGunOut);
                    SetTurretMuzzles(s);
                    var gunPart = TurretGun(s);
                    if (s != null && gunPart != null) guns80.Add((s, gunPart));
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
            cam.LookAtUnity(At(new Vector3(-20000, 800, 120000)), null, Vector3.zero);   // OrbitScale: framing the grown battlestation
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
            o.volumes = OrbitScale.StructureVolumes(o.volumes);   // OrbitScale: grown with the battlestation (Scenery)
            HullCollision.Attach(go, null, o);   // remake: the player slides along its real shape; NPCs keep the volumes
        }
        static void PauseAnimation(GameObject go)
        {
            if (go == null) return;
            foreach (var a in go.GetComponentsInChildren<PartAnimation>(true)) a.play = false;
        }
    }
}
