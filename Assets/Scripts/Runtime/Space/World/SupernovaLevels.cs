// SupernovaLevels.cs
// The Supernova add-on's in-space levels (campaign indices 87-158; Level::createCampaignMission 0xc3370 + LevelScript::
// LevelScript / process; Reference/research/campaign_levels_b.md parts 2-3, campaign_levels_c.md 3). Plain C#, run by
// CampaignLevel like ValkyrieLevels. Ships are Level+0xf8 slots in the original's order; the script step is the
// level-script event (radio trigger 27).
//   87 Thynome      the flight with Carla: radio only, from (0, 0, 210000)
//   89 Naneroh      "Meanwhile in Midorian space": the supernova destroys Luur (cutscene), then docked at Thynome
//   91 Valpatro     10 miners from the damaged freighter (dock, the cabins load them), it explodes, on to Tadram
//   92 Tadram       the miners onto the freighter (unloading), the first Specter attack, the freighter jumps away
//   94 Luur         83 evacuees from the burning platform to the freighter, with two shuttles; Specter waves
//   95 / 99 / 109 / 119 / 126 / 133 / 160 / 161   "Meanwhile..." cutscenes: the camera drifting past the station
//   97 Genoh        12 pirates against the Nivelians
//   100 Alioth      two Specters ambush (the radio is the objective)
//   102 Tadram      the carrier evacuation: four Rhino dropships shuttle 1700 evacuees, Specter waves, the carrier jumps
//   105 Naneroh     Khador's reverse-matter bomb fired into the supernova (a 650 000-unit flight), then Luur
//   106 Luur        the supernova grew; a damaged Specter explodes; on to Thynome (106 -> 108)
//   114 Marktesh    six loan-shark pirates hiding in the asteroid field
//   120 Valadon     two Specters (the radio is the objective)
//   123 Navan       the security check (radio)
//   125 Kappa       the freighter's black box: three secure containers to dock at and hack, pirates
//   131 Var Lupra   the plasma array fly-by (cutscene start)
//   135 Coromesk    140 t of titanium into the mining plant under pirate waves
//   137 B'akrram    the Vossk hail (radio)
//   139 Bra'Murr    two Vossk battleships: hack them, the second's cargo bots bring the prism aboard
//   142 Kernstal    the plasma tutorial with Gunant (the gas clouds, the ionizing missiles, the collector)
//   144 Var Lupra   Harval's ultimatum (cutscene), 145 the attack: the plasma array destroyed
//   147 the Void    hail Alice (radio)
//   154 the Void    Alice's betrayal: Hans' freighter, 20 Void fighters, hack Valkyrie within 91 s
//   157 Var Lupra   the final battle: Harval's fleet, Alice takes Valkyrie and fires the plasma array into the sun
//   158 Luur        the duel with Trunt Harval (his sentry guns)
// Remake picks where the decompile lost floats or the cutscene is only described (listed in campaign_levels_b/c.md's
// "uncertain"): the cutscene cameras' middle drift axis (0), the camera paths of 157 (qualitative), the helper shots of
// 145 and 157 (a rocket mesh flying at 8 / 35 u/ms), the Rhinos' and shuttles' docking (a stop of the route's docking time
// at each end, unloading while at the drop-off; 154's freighter docks the same way on Valkyrie's pad and, its route ending
// there, stays at its last waypoint).
// Harval in 157 / 158 (Level::assignGuns, campaign 0x9d / 0x9e): item 7 Berger Retribution x3 and the Shesha cluster
// missiles (item 0xd6) x4 in the second slot, toggled every 20 s like a Wanted pilot's.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class SupernovaLevels
    {
        const float M = 0.05f;

        readonly CampaignLevel c;
        readonly SpaceLevel level;
        readonly CutsceneCamera cam;
        readonly StoryAssets assets;
        readonly SupernovaAssets sn;
        readonly CombatAssets combat;
        int built;
        float stepMs, timerMs;
        bool loading;
        float playerSpeed;
        GameObject helper, bomb, explosionAnim, beam;
        readonly List<GameObject> scenery = new List<GameObject>();

        public SupernovaLevels(CampaignLevel campaignLevel, SpaceLevel spaceLevel)
        {
            c = campaignLevel;
            level = spaceLevel;
            assets = StoryAssets.Load();
            sn = SupernovaAssets.Load();
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
        static Vector3 GameDir(Transform t) => new Vector3(t.forward.x, t.forward.y, -t.forward.z);
        // Modified: AEGeometry::getRightVector is Unity's left: the (x, y, -z) mirror turns the original's right-handed frame
        // into Unity's left-handed one, so its right (up x dir there) comes out as -right here (S(y x z) = -(Sy x Sz)). Every
        // camera and spawn placed to a ship's side stood on the wrong one (a player's comparison with the original, 92).
        static Vector3 GameRight(Transform t) => new Vector3(-t.right.x, -t.right.y, t.right.z);
        static Vector3 GameUp(Transform t) => new Vector3(t.up.x, t.up.y, -t.up.z);
        Vector3 PlayerGame => ToGame(Player.position);
        Vector3 PlayerDirGame => GameDir(Player);
        Vector3 LightGame => level.Layout.lightDirection.normalized;
        static int R(int n) => Random.Range(0, n);
        static float Sign() => R(2) == 0 ? 1f : -1f;

        /// <summary>Game-space position of a ship / object.</summary>
        static Vector3 G(NpcShip s) => s != null ? ToGame(s.transform.position) : Vector3.zero;

        /// <summary>The asteroid field's centre when a level moves it (Level::createAsteroids), else null.</summary>
        public static Vector3? AsteroidCentre(int index, int station, bool alien)
        {
            if (Session.FreePlay) return null;
            if (index == 89) return new Vector3(-100000, 0, -50000);
            // Modified: Level::createAsteroids' mission 0x5b at station 0x6e (Valpatro) case, missing here: the field stood at
            // the station's seeded place, on the other side of the supernova (a player's comparison with the original).
            if (index == 91 && station == 110) return new Vector3(60000, 0, 50000);
            if (index == 114 && station == 83) return new Vector3(30000, 0, 80000);
            if (index == 145 && station == 112) return new Vector3(50000, 0, 70000);
            if (index == 154 && alien) return new Vector3(-70000, 0, 30000);
            return null;
        }

        // ---- spawn helpers ---------------------------------------------------------------------------------------

        NpcShip Fighter(int race, int ship, Vector3 at, bool jitter, System.Action<SpawnSpec> setup = null) => c.SpawnShip(race, ship, at, jitter, setup);

        NpcShip Specter(Vector3 at, bool jitter, System.Action<SpawnSpec> setup = null) =>
            c.SpawnShip(Standing.Specter, 44, at, jitter, s => { s.alwaysEnemy = true; setup?.Invoke(s); });

        /// <summary>Level::createStaticObject: a dockable (or plain) story object, friendly, unkillable, never moving; its
        /// getBoundingVolume id (the player is pushed out of it, fighters steer around it).</summary>
        NpcShip Static(string assembly, Vector3 at, Vector3 rotation, int nameText, int dockingType, int points, System.Action<SpawnSpec> setup = null) =>
            c.SpawnShip(3, -1, at, false, s =>
            {
                s.group = NpcGroup.Special; s.fixedObject = assembly; s.rotation = rotation; s.nameText = nameText; s.dockingType = dockingType;
                s.spacePoints = points; s.alwaysFriend = true; s.hitpoints = 9999999; s.noLoot = true; s.stationary = true; s.hitRadius = 6000f;
                s.collisionId = StaticVolume(assembly);
                setup?.Invoke(s);
            });

        /// <summary>Level::createStaticObject's getBoundingVolume ids by object: 0x495d the burning Luur platform -> collision.json
        /// 111, 0x4220 Valkyrie 1003, 0x4961 the secure containers 2003, 0x4974 the carrier 2005, 0x4a6b the Vossk battleship
        /// 2006; the wrecks (0x4299), the junk field (0x4962) and the freighters' own cargo_001 have none.</summary>
        static int StaticVolume(string assembly) => assembly switch
        {
            "sn_burning_station_mission_object" => 111,
            "v_station_battlestation_anim_mission_object" => 1003,
            "sn_secure_container_nivelian" => 2003,
            "sn_carrier_terran_1" => 2005,
            "sn_battleship_vossk" => 2006,
            _ => -1,
        };

        GameObject Scenery(string assembly, Vector3 gamePos, Quaternion rot, string label)
        {
            var go = OrbitBuilder.Spawn(level.Database, assembly, gamePos, rot, label, null);
            if (go != null) { GunRig.StripForFx(go); scenery.Add(go); }
            return go;
        }

        static void Remove(NpcShip s)
        {
            if (s == null) return;
            s.SetVisible(false);
            s.Deactivate();
            s.Place(new Vector3(-1e5f, -1e5f, -1e5f), Vector3.forward);
        }

        /// <summary>A hidden, inactive ship put somewhere (KIPlayer setPosition + setActive + setVisible).</summary>
        static void Show(NpcShip s, Vector3 gamePos, Vector3 gameFacing)
        {
            if (s == null) return;
            s.Place(ToUnity(gamePos), Dir(gameFacing));
            s.Wake();
            s.SetVisible(true);
        }

        void MovePlayer(Vector3 gamePos, Vector3 gameFacing) => level.MovePlayer(ToUnity(gamePos), Quaternion.LookRotation(Dir(gameFacing), Vector3.up));

        // ---- building (Level::createCampaignMission + the LevelScript constructor) ------------------------------------

        public bool Build(int index)
        {
            built = index;
            switch (index)
            {
                case 87: Build87(); return true;
                case 89: Build89(); return true;
                case 91: Build91(); return true;
                case 92: Build92(); return true;
                case 94: Build94(); return true;
                case 95: case 99: case 109: case 119: case 126: case 133: case 160: case 161: BuildMeanwhile(); return true;
                case 97: Build97(); return true;
                case 100: Build100(); return true;
                case 102: Build102(); return true;
                case 105: Build105(); return true;
                case 106: Build106(); return true;
                case 114: Build114(); return true;
                case 120: Build120(); return true;
                case 123: MovePlayer(new Vector3(0, 0, 180000), -new Vector3(0, 0, 180000)); c.WinObjective = () => Over(3); return true;
                case 125: Build125(); return true;
                case 131: Build131(); return true;
                case 135: Build135(); return true;
                case 137: MovePlayer(new Vector3(20000, 0, 150000), -new Vector3(20000, 0, 150000)); c.WinObjective = () => Over(4); return true;
                case 139: Build139(); return true;
                case 142: Build142(); return true;
                case 144: Build144(); return true;
                case 145: Build145(); return true;
                case 147: c.WinObjective = () => Over(1); return true;
                case 154: Build154(); return true;
                case 157: Build157(); return true;
                case 158: Build158(); return true;
                default: return false;
            }
        }

        /// <summary>105's formation speed (u/ms), a little faster than the camera's dolly (remake pick, see Build105).</summary>
        const float FormationSpeed = 2.5f;

        /// <summary>Turret offsets around a Vossk battleship (table 0x2536ac).</summary>
        static readonly Vector3[] BattleshipTurrets =
            { new Vector3(10283, -1123, 26039), new Vector3(-10171, -1083, 25907), new Vector3(-15624, -787, -7569), new Vector3(15624, -787, -7569), new Vector3(0, 4901, 3084) };

        /// <summary>Harval's twelve Specters around him (offsets in his frame, +-30).</summary>
        static readonly Vector3[] Formation =
        {
            new Vector3(-5000, 300, 200), new Vector3(-3500, 300, 200), new Vector3(5000, 300, 200), new Vector3(3500, 300, 200),
            new Vector3(-6500, 500, -5000), new Vector3(-3250, 500, -5000), new Vector3(0, -300, -5000), new Vector3(3250, 500, -5000),
            new Vector3(6500, 500, -5000), new Vector3(-6500, -400, -5000), new Vector3(0, -400, -5000), new Vector3(6500, -400, -5000),
        };

        // ---- the pause menu's Skip (395; LevelScript::canSkipCutsceneNow 0x16f5b8 / skipCutscene 0x16f61c) ---------------

        /// <summary>154 in states 1-9, 157 in states 2-4, 158 in states 0-1 (this+0x1c).</summary>
        public bool CanSkipCutscene
        {
            get
            {
                if (Story.Index != built || !c.Cutscene) return false;
                return built == 154 ? Step >= 1 && Step <= 9 : built == 157 ? Step >= 2 && Step <= 4 : built == 158 && Step <= 1;
            }
        }

        /// <summary>154: radio 0-7 done, state 9 with its timer at 2001 ms, Hans back under his AI at the ship; 157: radio 0-3
        /// done, state 4 at 18 001 ms (the Specters awake); 158: radio 0-2 done, state 2 at 12 001 ms, Harval shown at
        /// (-2077, 0, 20233).</summary>
        public void SkipCutscene()
        {
            if (!CanSkipCutscene) return;
            switch (built)
            {
                case 154:
                {
                    c.Radio?.MarkShown(8);
                    var hans = S(0);
                    var valkyrie = S(1);
                    if (hans != null && valkyrie != null)
                    {
                        // skipCutscene 0x16f61c: AI on, set to route waypoint 0; the route then docks him on Valkyrie's pad.
                        var at = new Vector3(-10000, 1000, 15000);
                        hans.Place(ToUnity(at), Dir(G(valkyrie) - at));
                        hans.frozen = false;
                        StartHansRoute(hans, valkyrie);
                    }
                    Step = 9;
                    stepMs = 2001f;
                    break;
                }
                case 157:
                    c.Radio?.MarkShown(4);
                    for (int i = 11; i <= 21; i++) { var s = S(i); if (s == null) continue; s.Wake(); s.SetVisible(true); }
                    Step = 4;
                    stepMs = 7000f;
                    flyInMs = 18001f;
                    break;
                case 158:
                {
                    c.Radio?.MarkShown(3);
                    var harval = S(0);
                    if (harval != null) { harval.Place(ToUnity(new Vector3(-2077, 0, 20233)), harval.transform.forward); harval.SetVisible(true); harval.SetExhaust(true); harval.scriptedSpeed = 1f; }
                    playerSpeed = 0f;
                    Step = 2;
                    stepMs = 12001f;
                    break;
                }
            }
        }

        // ---- cutscene helpers (LevelScript "cutscene on" / "off") ------------------------------------------------------

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
            // A ship docked at an object (92: the freighter) stays the docking's: its controls come back on undocking.
            var d = level.Docking;
            if (d == null || !d.Busy || d.State == ObjectDocking.Phase.Approach)
            {
                Ship.externalControl = false;
                if (level.Weapons != null) level.Weapons.Blocked = false;
            }
            SetPlayerVisible(true);
            cam.Release();
        }

        void SetPlayerVisible(bool on)
        {
            if (Ship.visualModel != null) Ship.visualModel.gameObject.SetActive(on);
        }

        // ---- per frame (LevelScript::process) ------------------------------------------------------------------------

        public void Tick(int index, float dtMs)
        {
            stepMs += dtMs;
            timerMs += dtMs;
            // Only while the level's own cutscene holds the ship: the mining approach and object docking set externalControl
            // too, and this push (at the speed from the last cutscene, a boost's if the player was boosting) flew the ship
            // through the asteroid it was landing on (step 29's Void crystals, a player's report).
            if (c.Cutscene && Ship.externalControl && !level.Health.Dead && playerSpeed > 0f)
            {
                Player.position += Player.forward * playerSpeed * dtMs * M;
                Ship.ExternalSpeedMetersPerSecond = playerSpeed * 1000f * M;
            }
            if (loading) return;
            shuttles.Update(dtMs, Unload);
            if (index != built && c.Cutscene && !ScriptOwnsAdvance) LeaveCutscene();
            switch (built)
            {
                case 89: Tick89(dtMs); break;
                case 91: if (index == 91) Tick91(dtMs); break;
                case 92: if (index == 92) Tick92(dtMs); break;
                case 94: if (index == 94) Tick94(); break;
                case 95: case 99: case 109: case 119: case 126: case 133: case 160: case 161: if (index == built) TickMeanwhile(); break;
                case 102: if (index == 102) Tick102(dtMs); break;
                case 105: Tick105(dtMs); break;
                case 106: Tick106(); break;
                case 114: if (index == 114) Tick114(); break;
                case 125: if (index == 125) Tick125(); break;
                case 131: if (index == 131) Tick131(); break;
                case 135: if (index == 135) Tick135(); break;
                case 139: if (index == 139) Tick139(); break;
                case 142: if (index == 142) Tick142(); break;
                case 144: Tick144(dtMs); break;
                case 145: if (index == 145) Tick145(dtMs); break;
                case 154: if (index == 154) Tick154(); break;
                case 157: Tick157(dtMs); break;
                case 158: if (index == 158) Tick158(dtMs); break;
            }
        }

        /// <summary>Levels whose own script advances the index while their cutscene still runs.</summary>
        bool ScriptOwnsAdvance => built == 89 || built == 105 || built == 106 || built == 144 || built == 157;

        public void LateTick(float dtMs)
        {
            cam.LateTick(dtMs);
        }

        /// <summary>A save of hull / shield / armour / gamma, nextCampaignMission, and the next orbit (the scripts' jumps);
        /// 'streamOut' false: Level::initStreamOutPosition 0, the undock point.</summary>
        void AdvanceAndTravel(int times, int station, bool streamOut = true)
        {
            loading = true;
            for (int i = 0; i < times; i++) Story.Advance(level.Database);
            level.TravelTo(station, streamOut);
        }

        void AdvanceAndDock(int station)
        {
            loading = true;
            Story.Advance(level.Database);
            Session.StationIndex = station;
            level.Dock();
        }

        /// <summary>89's look-at helper's right in the original engine's (mirrored) sense: Unity's -transform.right.</summary>
        Vector3 HelperRight89 => -helper.transform.right;

        // 91 (process 9556-9762).
        readonly List<PartAnimation> wreckAnims = new List<PartAnimation>();
        // Modified: 91's hull debris, held until the player is within WreckDebrisMeters (1.5 km = 30000 units) of the wreck.
        readonly List<PartAnimation> wreckDebris = new List<PartAnimation>();
        bool wreckDebrisStarted;
        const float WreckDebrisMeters = 1500f;
        AudioSource wreckLoop;
        bool wreckBroken;

        float freighterSpeed;

        float specterWake;

        float bombMs;

        bool turned;

        WreckBurn arrayBurn;

        // 154 (campaign_levels_c.md 3.15).
        bool autoTurretWasOn;
        bool hacked;

        // ---- 157's Valkyrie (the PlayerStation of 101 with the plasma gun, and the burning stages of the ctor) ---------

        GameObject valkyrieGo, valkyrieGun, stage1, stage2;
        Vector3 valkyrieAt, chaseDir;
        Quaternion valkyrieRot;
        bool valkyrieSound;
        float burnMs, blastMs;

        float flyInMs, harvalCloakMs;

        float cloakMs;

        // ---- the shuttles of 94 / 102 (ShuttleRoutes) -----------------------------------------------------------------

        readonly ShuttleRoutes shuttles = new ShuttleRoutes();
        ShuttleRoutes.Shuttle hansRoute;
    }
}
