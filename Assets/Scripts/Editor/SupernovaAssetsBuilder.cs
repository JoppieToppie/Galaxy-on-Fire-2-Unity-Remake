// SupernovaAssetsBuilder.cs  (Editor only)
// Menu "GoF2/Build/Supernova Assets": Resources/GoF2Story/SupernovaAssets (SupernovaAssets), the DLC2 sounds and music.

using System.IO;
using GoF2Remake.Data;
using UnityEditor;
using UnityEngine;

namespace GoF2Remake.EditorTools
{
    public static class SupernovaAssetsBuilder
    {
        const string Root = ImportSettings.Root;
        public const string AssetPath = Root + "/Resources/GoF2Story/SupernovaAssets.asset";

        [MenuItem("GoF2/Build/Supernova Assets", priority = 203)]
        public static void Build()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            var a = AssetDatabase.LoadAssetAtPath<SupernovaAssets>(AssetPath);
            if (a == null) { a = ScriptableObject.CreateInstance<SupernovaAssets>(); AssetDatabase.CreateAsset(a, AssetPath); }

            a.docking = Clip("DLC2_SFX/Docking_Landing_01.ogg");
            a.hackingTurn = Clip("DLC2_SFX/Hacking_Dialling_1.ogg");
            a.hackingSolved = Clip("DLC2_SFX/Hacking_Solved_1.ogg");
            a.transferLoop = Clip("DLC2_SFX/Transport_Loop.ogg");
            a.container = Clip("DLC2_SFX/Container_01.ogg");
            a.extractorLoop = Clip("DLC2_SFX/Extractor_Loop_01.ogg");
            a.plasmaCollected = new[] { Clip("DLC2_SFX/Mud1.ogg"), Clip("DLC2_SFX/Mud2.ogg"), Clip("DLC2_SFX/Mud3.ogg"), Clip("DLC2_SFX/Mud4.ogg") };
            a.ionizingBlast = Clip("DLC2_SFX/Plasma_Rocket_Explosion.ogg");
            a.carrierJump = Clip("DLC2_SFX/CS102_CarrierJump_2.ogg");
            a.valkyrieBeam = Clip("DLC2_SFX/GOF2_Valkyrie_RayBeam_1.ogg");
            a.selfDestruct = Clip("DLC2_SFX/Selfdestruct_Warning.ogg");
            a.explosion = Clip("DLC2_SFX/Explosion.ogg");
            a.wantedMusic = Clip("DLC2_MUSIC/20120527_GOF2_Addon_WantedBoardCriminal.ogg");
            a.stealthMusic1 = Clip("DLC2_MUSIC/20120527_GOF2_Addon_StealthFighter1.ogg");
            a.stealthMusic2 = Clip("DLC2_MUSIC/20120527_GOF2_Addon_StealthFighter2.ogg");
            a.gammaRayMusic = Clip("DLC2_MUSIC/20120527_GOF2_Addon_GammarRaySystems.ogg");
            a.supernovaIntro = Clip("DLC2_MUSIC/GOF2_SN_CS_1_02.ogg");
            a.katashunMusic = Clip("DLC2_MUSIC/GOF2SN_CS_126_Trunt1_02.ogg");
            a.mission102 = Clip("DLC2_MUSIC/GOF2_Mission_102_CutScene.ogg");
            a.mission102Loop = Clip("DLC2_MUSIC/GOF2_Mission_102_CutScene_Loop.ogg");
            a.mission102Loop2 = Clip("DLC2_MUSIC/GOF2_Mission_102_CutScene_Loop_2.ogg");
            a.supernovaIntroSky = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Skyboxes/skybox_005.mat");
            a.sunExplosionRing = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Prefabs/supernova/fx/sn_sun_explosion_ring_anim_add.prefab");
            a.sunExplosionCore = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Prefabs/supernova/fx/sn_sun_explosion_core_anim_add.prefab");
            a.sunExplosionCoreTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/supernova/fx/sn_sun_explosion_core.png");
            a.sunExplosionRingTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/supernova/fx/sn_sun_explosion_ring.png");

            EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssets();
            Debug.Log($"GoF2: Supernova assets at {AssetPath}");
        }

        static AudioClip Clip(string rel)
        {
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>($"{Root}/Audio/{rel}");
            if (c == null) Debug.LogWarning($"GoF2: missing clip {rel}");
            return c;
        }
    }
}
