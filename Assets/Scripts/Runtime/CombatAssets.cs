// CombatAssets.cs
// What ship combat needs that can't be loaded by name (Resources/GoF2Combat/CombatAssets, made by GoF2 > Build Combat
// Assets; Reference/research/ship_combat.md 5, 8 and npc_traffic_ai.md 8):
//   crates      container_003_terran / 004_vossk / 002_nivelian / 001_midorian / 005_void (KIPlayer::createCrate)
//   wrecks      cargo_003_terran / 004_vossk / 002_nivelian / 001_midorian _explosion_anim, battleship_terran_explosion_anim
//   explosion   Explosion type 0: explosion_anim_lookat_alpha (+ _add child), 3..9 explosion_debris streaks
//   tractor     beam meshes of items 68, 69, 70, 194 (projectile_068..070, v_projectile_194)
//   smoke       materials 20101 sprite_smoke (alpha) / 27250 sprite_fire (additive) of the burning-ship sprites (ShipSmoke)
//   sounds      25 / 23 / 24 incoming fire shield / armor / hull, 20 ship destroyed, 18 / 19 explosion big / mid,
//               0 tractor beam loop, 4 tractor door, 37 game over, NPC shots per race (52, 55, 54, 53, 61), engine loops
//               46 Spaceship_Engine_Enemy / 47 Spaceship_Engine_Freighter: their FEV sound definitions Engine_Enemy_01 (one
//               of Engine_09, Engine_newnew_05 / 02 / 06_mixdown / 03, equal weights) and Engine_Freighter (Engine_Freighter_03 / 02)
//   music       134 / 139 / 138 / 137 space no-combat per race, 140 / 141 / 142 Space_Battle_Low / Medium / Full

using UnityEngine;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class CombatAssets : ScriptableObject
    {
        public const string ResourcePath = "GoF2Combat/CombatAssets";

        [Tooltip("Terran, Vossk, Nivelian, Midorian, Void.")]
        public GameObject[] crates;
        [Tooltip("Freighter wrecks by race (Terran, Vossk, Nivelian, Midorian), then the battleship.")]
        public GameObject[] wrecks;
        public GameObject explosion, debris;
        [Tooltip("Items 68, 69, 70, 194.")]
        public GameObject[] tractorBeams;
        public Material smokeMaterial, fireMaterial;
        [Tooltip("Material 20095 fog.png (additive): the fog manager Level+0x7c, e.g. the pirate base's SET_FOG_STATIC.")]
        public Material fogMaterial;
        [Tooltip("Material 20099 sprite_explosion (additive): the dying ships' burn, records 9 / 11 (ShipBurn).")]
        public Material explosionSpriteMaterial;
        [Tooltip("Material 20090 particles (additive): the player's exhaust particles (ShipExhaust).")]
        public Material particlesMaterial;

        public AudioClip[] hitShield, hitArmor, hitHull, shipDestroyed, explosionBig, explosionMid, shots;
        public AudioClip targetLock, tractorLoop, tractorClose, gameOver;
        [Tooltip("Sound 36 Mission_accomplished.")]
        public AudioClip missionAccomplished;
        [Tooltip("Space junk 0x4215-0x4217 (freelance Junk removal).")]
        public GameObject[] junk;
        [Tooltip("KIPlayer::createCrate(3): the destroyed junk's container, mesh 0x4218 space_junk_004.")]
        public GameObject junkCrate;
        [Tooltip("Sound 46: one picked per ship.")]
        public AudioClip[] enemyEngines;
        [Tooltip("Sound 47: one picked per ship.")]
        public AudioClip[] freighterEngines;
        [Tooltip("No-combat music by race: Terran, Vossk, Nivelian, Midorian.")]
        public AudioClip[] spaceMusic;
        [Tooltip("Space_Battle_Low, Medium, Full.")]
        public AudioClip[] battleMusic;
        [Tooltip("146 HomeBase_NoCombat: the Kaamo Club's orbit.")]
        public AudioClip homeBaseMusic;
        [Tooltip("147 Valkyrie_NoCombat (station 101) and 152 Space_NoCombat_DeepScience (stations 10 / 100).")]
        public AudioClip valkyrieMusic, deepScienceMusic;
        [Tooltip("Mesh 14246 station_pirates_explosion_anim: a Pirate Outpost's wreck animation (20 s).")]
        public GameObject outpostWreck;
        [Tooltip("Explosion types 7 (EMP), 8-10 (scatter), 11 (shock blast glow + sphere), 13 (fireworks).")]
        public GameObject explosionEmp, explosionScatter, shockGlow, shockSphere, fireworksBurst;
        [Tooltip("Sound 22 Garbage_Explosion: turrets, sentries, scatter bursts, mines.")]
        public AudioClip[] garbageExplosion;
        [Tooltip("The EMP lightning (records 17 / 18: material 27260 khador_jump, additive).")]
        public Material empSparkMaterial;
        [Tooltip("The emergency system's bubble (mesh 14374 v_shield), its refraction shader (GoF2/ShieldBubble, " +
                 "SimpleRefractionShader) and its sound (1115 Invincibility_01).")]
        public GameObject shieldBubble;
        public Shader shieldBubbleShader;
        public AudioClip invincibility;
        [Tooltip("The shield injector (2258 init, 2257 loop, 2259 end) and the gamma shield loops (2260 / 2261).")]
        public AudioClip injectorInit, injectorLoop, injectorEnd, gammaShield1, gammaShield2;
        [Tooltip("The gamma shield's blaze around the ship in the supernova system (meshes 18803 sn_ship_blaze_flames_anim_add " +
                 "and 18802 sn_ship_blaze_glow_anim_add, PlayerEgo::PlayerEgo).")]
        public GameObject gammaBlazeFlames, gammaBlazeGlow;
        [Tooltip("The cloak (sound 30 Cloak_02) and the time extender (1120 TimeShift_Start, 1119 TimeShift_01b at the end).")]
        public AudioClip cloak, timeShift, timeShiftEnd;
        [Tooltip("The cloak's shader (GoF2/Cloak) and dissolve map (Textures/main/fx/cloak_map.png).")]
        public Shader cloakShader;
        public Texture2D cloakMap;
        [Tooltip("Repair / transfusion beams (19092 / 19093) and their loops (2271 / 2272 / 2267 / 2268).")]
        public GameObject repairBeam, transfusionBeam;
        public AudioClip repairLoop1, repairLoop2, drainLoop1, drainLoop2;

        static CombatAssets cached;
        public static CombatAssets Load() => cached != null ? cached : cached = Resources.Load<CombatAssets>(ResourcePath);

        public GameObject Crate(int race) => crates == null || crates.Length < 5 ? null
            : race == 0 ? crates[0] : race == 1 ? crates[1] : race == 3 ? crates[3] : race == 9 ? crates[4] : crates[2];

        public GameObject Tractor(int item) => tractorBeams == null || tractorBeams.Length < 4 ? null
            : item == 68 ? tractorBeams[0] : item == 69 ? tractorBeams[1] : item == 70 ? tractorBeams[2] : tractorBeams[3];

        public static AudioClip Pick(AudioClip[] clips) => clips == null || clips.Length == 0 ? null : clips[Random.Range(0, clips.Length)];
    }
}
