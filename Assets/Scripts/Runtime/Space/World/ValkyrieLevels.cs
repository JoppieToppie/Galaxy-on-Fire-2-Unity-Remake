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
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public partial class ValkyrieLevels
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
        /// <summary>OrbitScale: an absolute station-anchored game position (the original's) to where the scaled orbit has it
        /// (ToUnity stays for offsets, and for positions read back from the scene with ToGame).</summary>
        static Vector3 At(Vector3 game) => OrbitScale.ToUnity(game);
        /// <summary>OrbitScale: a structure's size (the battlestation, Kothar's station): offsets on its hull grow with it.</summary>
        static float StructureScale => OrbitScale.Factor;
        static Vector3 Dir(Vector3 game) => new Vector3(game.x, game.y, -game.z).normalized;
        Vector3 PlayerGame => OrbitScale.ToGame(Player.position);   // OrbitScale: the original's coordinates (spawns, At)
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
            // OrbitScale: the story's station-sized objects (the battlestation, its laser, Kothar's laser) where the scaled orbit
            // has them, grown like the station.
            var go = OrbitBuilder.Spawn(level.Database, assembly, OrbitScale.Place(gamePos), rot, label, null);
            if (go != null) { GunRig.StripForFx(go); OrbitScale.Structure(go.transform); }
            return go;
        }

        // ---- building (Level::createCampaignMission + the LevelScript constructor) --------------------------------

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
        // Modified: the turret's gun (v_station_battlestation_turret_gun_part0, local to its gun node, measured in the
        // Inspector): drawn in at the start of 78's slide, out from half way (rotation X 90) to the end, and out in 80.
        static readonly Vector3 TurretGunIn78 = new Vector3(0f, 0f, -20f);
        static readonly Vector3 TurretGunOut = new Vector3(0f, 6f, 54f);
        const string TurretGunPart = "v_station_battlestation_turret_gun_part0";

        // Modified: the tips of the gun's two barrels (local to TurretGunPart, from the model: z = the barrels' end).
        static readonly Vector3 TurretMuzzleA = new Vector3(5.7f, 2.25f, 99.8f);
        static readonly Vector3 TurretMuzzleB = new Vector3(-6.3f, 2.25f, 99.8f);
        // Modified: the gun drawn back into its base as the turret raises it: TurretGunOut level, TurretGunRaised at the
        // highest pitch (52.7 deg), in between by the pitch (measured in the Inspector).
        static readonly Vector3 TurretGunRaised = new Vector3(0f, -30f, 40f);

        // 80's turrets and their gun parts (TurretGunsByPitch every frame).
        readonly List<(NpcShip ship, Transform gun)> guns80 = new List<(NpcShip, Transform)>();

        // ---- cutscene helpers (LevelScript "cutscene on" / "off", campaign_levels_b.md part 2) ------------------------

        float playerSpeed;

        // ---- per frame (LevelScript::process) --------------------------------------------------------------------

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

        // Modified: the stop in front of the station (78), at the end of the first of its unfolding's two parts.
        const float StopAtUnfold78 = 0.5f;   // fraction of the unfolding animation (0.5 = halfway, 1 = its end)
        bool stopped78;
        float stopAtT78;

        // 80: Alice attacks Kothar (LevelScript.c 9761-10155).
        Transform burnA, burnB;

        // Modified: 80's last shot (the battlestation's jump): the camera goes along Unity Z from where it starts to
        // JumpShotTravel80 further over the whole shot (steps 11 + 12 + 13 = 4000 + 8000 + 10001 ms), X and Y unchanged.
        const float JumpShotTravel80 = 8000f;   // Unity metres along +Z: from the shot's start (Z -7000) to Z 1000
        const float JumpShotMs80 = 4000f + 8000f + 10001f;
        float jumpShotMs80, jumpShotStartZ80, jumpShotEndZ80;
        /// <summary>80's battlestation (game units, the original's; Level case 0x50).</summary>
        static readonly Vector3 Host80 = new Vector3(0, 0, 160000);

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
        static readonly Vector3 FlameCopyPos80 = new Vector3(2024.8f, 1.6f, 23.1f), FlameCopyRot80 = new Vector3(0f, 119f, 0f), FlameCopyScale80 = Vector3.one;   // Inspector values
        // Modified: the battlestation laser's parts moved where the user placed them in the Inspector (local positions,
        // Unity metres; 2026-11-16); the others stay at the model's.
        static readonly Dictionary<string, Vector3> LaserPartPos80 = new Dictionary<string, Vector3>
        {
            { "v_station_battlestation_laser_anim_add_part0", new Vector3(-1.3f, 0f, -43.7f) },
            { "v_station_battlestation_laser_anim_add_part1", new Vector3(-12.8f, 0f, -7.4f) },
            { "v_station_battlestation_laser_anim_add_part6", new Vector3(-2f, 0f, -45f) },
            { "v_station_battlestation_laser_anim_add_part8", new Vector3(-11f, 0f, -12f) },
            // part 4 (the main beam's flash) moves with its keys: (-2.4, -19.5, -2222.3) in the Inspector at its load pose,
            // less its keyed offset there (0, -1.445, -2210.87)
            { "v_station_battlestation_laser_anim_add_part4", new Vector3(-2.4f, -18.055f, -11.43f) },
            { "v_station_battlestation_laser_anim_add_part5", new Vector3(-2f, 8.2f, 2.2f) },
        };
        // Modified: the four beams (parts 6-9) went dark from LaserDipFrom80 to LaserDipTo80 of the laser's animation (100 ->
        // 0 at 1550, 0 at 1600, back to 100 at 1650) as the main beam fires: all of them gone for about a frame (the user's
        // report, 2026-11-16). Their keys in between are dropped: they stay on.
        const float LaserDipFrom80 = 1500f, LaserDipTo80 = 1650f;
        static readonly string[] LaserDipParts80 =
        {
            "v_station_battlestation_laser_anim_add_part6", "v_station_battlestation_laser_anim_add_part7",
            "v_station_battlestation_laser_anim_add_part8", "v_station_battlestation_laser_anim_add_part9",
        };
        readonly List<(Renderer piece, Renderer glow)> flamePieces80 = new List<(Renderer, Renderer)>();
        Renderer flameSource80;
        MaterialPropertyBlock flameBlock80, glowBlock80;

        // Modified: 80's hyperdrive fx (the battlestation jumping away), Unity units relative to the battlestation /
        // degrees (Inspector: world (0, -150, -7600) with the battlestation at (0, 0, -8000)).
        static readonly Vector3 HyperDrivePos80 = new Vector3(0f, -150f, 400f);
        static readonly Vector3 HyperDriveRot80 = new Vector3(0f, 0f, 0f);
        const float HyperDriveScale80 = 8f;

        // The first shot as the original's (the user's request, 2026-11-16): step 1 waits 3000 ms, step 2 plays the laser's
        // animation for 2000 ms, then the cut to the hit; steps 7 and 8 wait 800 ms each (the explosion at 6600 ms).
        const float LaserDelay80 = 3000f;
        const float LaserWindow80 = 2000f;
        const float HitWait80 = 800f;

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

        // Modified: the hit's blast from the hit point, game units (Unity (740, -550, -370), set in the Inspector).
        static readonly Vector3 HitBlastOffset80 = new Vector3(2313f, 451f, 1442f);
        // Modified: the hit's blast scale (was 3, LevelScript ctor's Explosion::setScaling): it was a small glow next to the
        // original's flash that covers Kothar (the user's frames, 2026-11-16).
        const float HitBlastScale80 = 12f;
        // Modified: both lasers' (the battlestation's and Kothar's hit) glow, their materials' _Glow 2.5 through a property
        // block: the bloom made them thick beams, the original's are thin lines (the user's frames, 2026-11-16).
        const float LaserGlow80 = 1f;

        const float CamPullBack80 = 3250f;   // Modified: Unity metres along +Z over the shot (from Z -6250 to -3000; X and Y stay put)
        float camStartZ80, camEndZ80;

        // Modified: 81's hyperdrive fx (the battlestation dropping out of hyperspace), Unity units relative to the
        // battlestation (at the origin) / degrees, from the Inspector.
        static readonly Vector3 HyperDrivePos81 = new Vector3(0f, -160f, -500f);
        static readonly Vector3 HyperDriveRot81 = new Vector3(0f, 0f, 0f);
        const float HyperDriveScale81 = 8f;
        const float BattlestationAppears81 = 12201f;   // Modified: ms into 81 when the battlestation shows (was 12 001)

    }
}
