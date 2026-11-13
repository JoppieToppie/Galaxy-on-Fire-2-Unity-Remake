// CombatAssets.cs
// What ship combat needs that can't be loaded by name (Resources/GoF2Combat/CombatAssets, made by GoF2 > Build > Combat Assets; Reference/research/ship_combat.md 5, 8 and npc_traffic_ai.md 8):
//   crates      container_003_terran / 004_vossk / 002_nivelian / 001_midorian / 005_void (KIPlayer::createCrate), the shot
//               asteroids' asteroid_01_junk / asteroid_void_junk (types 1 / 2), the junk's space_junk_004 (type 3)
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
        [Tooltip("Material 24096 v_projectiles (additive): the SunFire o50's trail, record 28 on Level+0x98 (RocketTrail).")]
        public Material sunfireTrailMaterial;
        [Tooltip("Material 27321 sn_sprite_fireworks_rocket_sparks (additive): the Fireworks' trail, record 47 on Level+0x9c.")]
        public Material fireworksSparkMaterial;

        public AudioClip[] hitShield, hitArmor, hitHull, shipDestroyed, explosionBig, explosionMid, shots;
        public AudioClip targetLock, tractorLoop, tractorClose, gameOver;
        [Tooltip("Sound 36 Mission_accomplished.")]
        public AudioClip missionAccomplished;
        [Tooltip("Space junk 0x4215-0x4217 (freelance Junk removal).")]
        public GameObject[] junk;
        [Tooltip("KIPlayer::createCrate(3): the destroyed junk's container, mesh 0x4218 space_junk_004.")]
        public GameObject junkCrate;
        [Tooltip("KIPlayer::createCrate(1) / (2): a shot asteroid's crate, meshes 0x421e asteroid_01_junk / 0x421f asteroid_void_junk.")]
        public GameObject asteroidCrate, voidAsteroidCrate;
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
        [Tooltip("The hull materials a wreck is drawn with (Terran, Vossk, Nivelian, Midorian freighter, the battleship, the pirate " +
                 "outpost: 34708 / 34712 / 34704 / 34700 / 34716 / 20104) and, at the same index, their damaged ones " +
                 "(PlayerFixedObject::update: MaterialCreate(0x8248 + kind) 140 ms into the death; 33354 / 33355 / 33353 / 33352 / " +
                 "33356 / 33357, the *_dmg textures).")]
        public Material[] wreckIntact, wreckDamaged;

        /// <summary>PlayerFixedObject::update 0x17f6b4: 140 ms into a fixed object's or freighter's death the wreck mesh takes
        /// its damaged material (MeshChangeMaterial: 0x8248..0x824d by wreck kind, the charred *_dmg textures on the same UVs).
        /// Swaps every intact wreck material on 'go' for its damaged one; other materials (lights, glows) stay.</summary>
        public static void ApplyWreckDamage(GameObject go)
        {
            var a = Load();
            if (go == null || a == null || a.wreckIntact == null || a.wreckDamaged == null) return;
            string scope = go.GetComponentInChildren<Visuals.AssembledObject>(true)?.ModScope;   // its own skin first
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    // A skinned hull (Modding.ModTextures) draws a copy: its game material finds the damaged one, which
                    // takes the mod's *_dmg textures too.
                    var intact = Modding.ModTextures.OriginalOf(mats[i]) ?? mats[i];
                    int k = intact != null ? System.Array.IndexOf(a.wreckIntact, intact) : -1;
                    if (k < 0 || k >= a.wreckDamaged.Length || a.wreckDamaged[k] == null) continue;
                    mats[i] = Modding.ModTextures.Replace(a.wreckDamaged[k], scope);
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }
        [Tooltip("Explosion types 7 (EMP), 8-10 (scatter), 11 (shock blast glow + sphere), 13 (fireworks).")]
        public GameObject explosionEmp, explosionScatter, shockGlow, shockSphere, fireworksBurst;
        [Tooltip("Materials 20151 / 20152 / 20153 (v_scattergun_000 / 001 / 002_explosion.png): the scatter bursts of types 8 / 9 / 10 " +
                 "(items 176 Nirai / 177 Berger / 178 Icarus). One mesh for all three (resources 16806-16808), so one per-mesh prefab, " +
                 "whose material is the last id's (Icarus).")]
        public Material[] scatterMaterials;
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
        [Tooltip("Modified: sn_ship_blaze_flames_normal (texture 11759): loaded by the Android build but used by no material; " +
                 "the remake's blaze haze bends the scene by it (GoF2/BlazeHaze), as the iOS build's blaze seems to.")]
        public Texture2D gammaBlazeNormal;
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

        /// <summary>KIPlayer::createCrate(type)'s mesh: 1 the rock 0x421e, 2 the Void rock 0x421f, 3 the junk container 0x4218,
        /// else the race's container.</summary>
        public GameObject CrateModel(int look, int race) =>
            look == Flight.Crate.LookRock ? asteroidCrate : look == Flight.Crate.LookVoidRock ? voidAsteroidCrate
            : look == Flight.Crate.LookJunk ? junkCrate : Crate(race);

        public GameObject Crate(int race) => crates == null || crates.Length < 5 ? null
            : race == 0 ? crates[0] : race == 1 ? crates[1] : race == 3 ? crates[3] : race == 9 ? crates[4] : crates[2];

        /// <summary>The tractor beam's look by the item's look (ItemData.Look: a mod's beam is its base's): 68, 69, 70, else
        /// 194's.</summary>
        public GameObject Tractor(int look) => tractorBeams == null || tractorBeams.Length < 4 ? null
            : look == 68 ? tractorBeams[0] : look == 69 ? tractorBeams[1] : look == 70 ? tractorBeams[2] : tractorBeams[3];

        public static AudioClip Pick(AudioClip[] clips) => clips == null || clips.Length == 0 ? null : clips[Random.Range(0, clips.Length)];
    }
}
