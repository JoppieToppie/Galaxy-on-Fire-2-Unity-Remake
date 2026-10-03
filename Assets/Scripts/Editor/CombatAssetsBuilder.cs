// CombatAssetsBuilder.cs  (Editor only)
// Menu "GoF2/Build Combat Assets": Resources/GoF2Combat/CombatAssets (CombatAssets), the prefabs and sounds of ship
// combat (crates, wrecks, explosion, tractor beams, hit / death / music clips). Run by Create Space Scene.

using System.IO;
using System.Linq;
using GoF2Remake.Flight;
using UnityEditor;
using UnityEngine;

namespace GoF2Remake.EditorTools
{
    public static class CombatAssetsBuilder
    {
        const string Root = ImportSettings.Root;
        public const string AssetPath = Root + "/Resources/" + CombatAssets.ResourcePath + ".asset";

        [MenuItem("GoF2/Build Combat Assets", priority = 17)]
        public static void Build()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            var a = AssetDatabase.LoadAssetAtPath<CombatAssets>(AssetPath);
            if (a == null) { a = ScriptableObject.CreateInstance<CombatAssets>(); AssetDatabase.CreateAsset(a, AssetPath); }

            a.crates = new[] { "container_003_terran", "container_004_vossk", "container_002_nivelian", "container_001_midorian", "container_005_void" }
                .Select(n => Prefab($"Prefabs/main/misc/{n}.prefab")).ToArray();
            a.wrecks = new[] { "cargo_003_terran", "cargo_004_vossk", "cargo_002_nivelian", "cargo_001_midorian", "battleship_terran" }
                .Select(n => Prefab($"Prefabs/main/ships/{n}_explosion_anim.prefab")).ToArray();
            a.explosion = Prefab("Resources/Assembled/main/fx/explosion_anim_lookat_alpha.prefab");
            a.debris = Prefab("Prefabs/main/fx/explosion_debris_anim_add.prefab");
            a.junk = new[] { "space_junk_001", "space_junk_002", "space_junk_003" }.Select(n => Prefab($"Prefabs/main/misc/{n}.prefab")).ToArray();
            a.junkCrate = Prefab("Prefabs/main/misc/space_junk_004.prefab");
            a.tractorBeams = new[] { Find("projectile_068_anim_add"), Find("projectile_069_anim_add"), Find("projectile_070_anim_add"), Find("v_projectile_194_anim_add") };

            a.smokeMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/mat_20101_sprite_smoke.mat");
            a.fogMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/mat_20095_fog.mat");
            a.fireMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/mat_27250_sprite_fire.mat");
            a.explosionSpriteMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/mat_20099_sprite_explosion.mat");
            a.particlesMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/mat_20090_particles.mat");
            if (a.smokeMaterial == null || a.fireMaterial == null) Debug.LogWarning("GoF2: missing sprite_smoke / sprite_fire materials");
            a.hitShield = Clips("SFX_SPACE", "Incoming_Fire_Shield");
            a.hitArmor = Clips("SFX_SPACE", "Incoming_Fire_Armor");
            a.hitHull = Clips("SFX_SPACE", "Incoming_Fire_Hull");
            a.shipDestroyed = Clips("SFX_SPACE", "Destruction_Ship_Small");
            a.explosionBig = Clips("SFX_SPACE", "Destruction_Ship_Big");
            a.explosionMid = Clips("SFX_SPACE", "Destruction_Ship_Med");
            a.shots = new[] { Clip("SFX_SPACE/Laser_Nirai_Impulse_EX1_V01.ogg"), Clip("SFX_SPACE/Laser_Shkoom_v02.ogg"),
                              Clip("SFX_SPACE/Laser_Nirai_Charged_Impulse_v01.ogg"), Clip("SFX_SPACE/Laser_Nirai_Impulse_EX2_V01.ogg"),
                              Clip("SFX_SPACE/Laser_Enemy_V04b.ogg"), Clip("SFX_SPACE/Laser_Vossk_V01.ogg"), Clip("DLC2_SFX/DarkMatterLaser_04.ogg") };
            a.targetLock = Clip("SFX_SPACE/Target_Lock_v08.ogg");
            a.tractorLoop = Clip("SFX_SPACE/Tractor_Beam_v1.ogg");
            a.tractorClose = Clip("SFX_SPACE/Tractor_Beam_Close_Door_01c.ogg");
            a.gameOver = Clip("SFX_SPACE/game_over_v02.ogg");
            a.missionAccomplished = Clip("SFX_SPACE/Mission_Accomplished_v05.ogg");
            a.enemyEngines = new[] { "Engine_09", "Engine_newnew_05", "Engine_newnew_02", "Engine_newnew_06_mixdown", "Engine_newnew_03" }
                .Select(n => Clip($"SFX_SPACE/{n}.ogg")).ToArray();
            a.freighterEngines = new[] { Clip("SFX_SPACE/Engine_Freighter_03.ogg"), Clip("SFX_SPACE/Engine_Freighter_02.ogg") };
            a.spaceMusic = new[] { Clip("MUSIC/Space_Terraner.ogg"), Clip("MUSIC/Space_Vossk.ogg"), Clip("MUSIC/Space_Nivelianer.ogg"), Clip("MUSIC/Space_Midorianer.ogg") };
            a.battleMusic = new[] { Clip("MUSIC/Space_Battle_Low.ogg"), Clip("MUSIC/Space_Battle_Medium.ogg"), Clip("MUSIC/Space_Battle_Full.ogg") };
            a.homeBaseMusic = Clip("MUSIC/HomeBase_NoCombat.ogg");
            a.valkyrieMusic = Clip("MUSIC/Space_NoCombat_Valkyrie.ogg");
            a.deepScienceMusic = Clip("DLC2_MUSIC/GOF2_DeepScience_Space.ogg");
            a.outpostWreck = Prefab("Prefabs/main/stations/station_pirates_explosion_anim.prefab");
            a.explosionEmp = Prefab("Prefabs/main/fx/explosion_emp_anim_lookat_add.prefab");
            a.explosionScatter = Prefab("Prefabs/valkyrie/fx/v_scattergun_000_explosion_lookat_anim_add.prefab");
            a.shockGlow = Prefab("Prefabs/supernova/fx/sn_shock_blast_glow_anim_lookat_add.prefab");
            a.shockSphere = Prefab("Prefabs/supernova/fx/sn_shock_blast_sphere_anim_add.prefab");
            a.fireworksBurst = Prefab("Prefabs/supernova/fx/sn_fireworks_lookat_anim_add.prefab");
            a.garbageExplosion = Clips("SFX_SPACE", "Garbage_Explosion");
            a.empSparkMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/mat_27260_khador_jump.mat");
            a.shieldBubble = Prefab("Prefabs/valkyrie/fx/v_shield.prefab");
            a.shieldBubbleShader = Shader.Find("GoF2/ShieldBubble");
            a.gammaBlazeFlames = Prefab("Prefabs/supernova/fx/sn_ship_blaze_flames_anim_add.prefab");
            a.gammaBlazeGlow = Prefab("Prefabs/supernova/fx/sn_ship_blaze_glow_anim_add.prefab");
            a.invincibility = Clip("DLC_SFX/Invincibility_01.ogg");
            a.injectorInit = Clip("DLC2_SFX/PlasmaInjector_Init_02.ogg");
            a.injectorLoop = Clip("DLC2_SFX/PlasmaInjector_Loop_03.ogg");
            a.injectorEnd = Clip("DLC2_SFX/PlasmaInjector_End_01.ogg");
            a.gammaShield1 = Clip("DLC2_SFX/Gamma_Shield_01.ogg");
            a.gammaShield2 = Clip("DLC2_SFX/Gamma_Shield_03.ogg");
            a.cloak = Clip("SFX_SPACE/Cloak_02.ogg");
            a.timeShift = Clip("MUSIC/TimeShift_Start.ogg");
            a.timeShiftEnd = Clip("DLC_SFX/TimeShift_01b.ogg");
            a.cloakShader = Shader.Find("GoF2/Cloak");
            a.cloakMap = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/main/fx/cloak_map.png");
            a.repairBeam = Prefab("Prefabs/supernova/fx/sn_projectile_207_anim_add.prefab");
            a.transfusionBeam = Prefab("Prefabs/supernova/fx/sn_projectile_222_anim_add.prefab");
            a.repairLoop1 = Clip("DLC2_SFX/Nirai_SPP_C1_2.ogg");
            a.repairLoop2 = Clip("DLC2_SFX/Nirai_SPP_M50_2.ogg");
            a.drainLoop1 = Clip("DLC2_SFX/Crimso_Drain.ogg");
            a.drainLoop2 = Clip("DLC2_SFX/Pandorra_Leech_01.ogg");

            EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssets();
            Debug.Log($"GoF2: combat assets at {AssetPath} ({a.crates.Count(c => c != null)} crates, {a.wrecks.Count(w => w != null)} wrecks, " +
                      $"{a.tractorBeams.Count(t => t != null)} tractor beams, explosion {(a.explosion != null)}).");
        }

        static GameObject Prefab(string rel)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/{rel}");
            if (p == null) Debug.LogWarning($"GoF2: missing prefab {rel}");
            return p;
        }

        static GameObject Find(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets($"{name} t:Prefab", new[] { $"{Root}/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == name) return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            Debug.LogWarning($"GoF2: missing prefab {name}");
            return null;
        }

        static AudioClip Clip(string rel)
        {
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>($"{Root}/Audio/{rel}");
            if (c == null) Debug.LogWarning($"GoF2: missing audio {rel}");
            return c;
        }

        static AudioClip[] Clips(string folder, string prefix) =>
            Directory.GetFiles($"{Root}/Audio/{folder}", prefix + "*.ogg").OrderBy(p => p)
                .Select(p => AssetDatabase.LoadAssetAtPath<AudioClip>(p.Replace('\\', '/'))).Where(c => c != null).ToArray();
    }
}
