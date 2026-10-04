// StoryAssetsBuilder.cs  (Editor only)
// Menu "GoF2/Build/Story Assets": Resources/GoF2Story/StoryAssets (StoryAssets): the story voice lines (English and
// German, base game and both add-ons) and the portrait parts. Needs the HUD images (portrait background / frame).

using System.IO;
using System.Text.RegularExpressions;
using GoF2Remake.Data;
using UnityEditor;
using UnityEngine;

namespace GoF2Remake.EditorTools
{
    public static class StoryAssetsBuilder
    {
        const string Root = ImportSettings.Root;
        public const string AssetPath = Root + "/Resources/GoF2Story/StoryAssets.asset";
        static readonly string[] VoiceBanks = { "VOICE", "DLC_VOICE", "DLC2_VOICE", "LOUNGE", "GENERIC" };   // LOUNGE: the bar greetings, GENERIC: agents' radio (GenericVoice)
        static readonly Regex PartName = new Regex(@"^(\d+)_(\d)_(\d+)_ipad_large$");

        [MenuItem("GoF2/Build/Story Assets", priority = 202)]
        public static void Build()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            var a = AssetDatabase.LoadAssetAtPath<StoryAssets>(AssetPath);
            if (a == null) { a = ScriptableObject.CreateInstance<StoryAssets>(); AssetDatabase.CreateAsset(a, AssetPath); }

            a.voiceNamesEng.Clear(); a.voiceClipsEng.Clear(); a.voiceNamesDeu.Clear(); a.voiceClipsDeu.Clear();
            foreach (var bank in VoiceBanks)
            {
                AddVoices($"{Root}/Audio/{bank}_eng", "", a.voiceNamesEng, a.voiceClipsEng);
                AddVoices($"{Root}/Audio/{bank}_deu", "de_", a.voiceNamesDeu, a.voiceClipsDeu);
            }

            a.partNames.Clear(); a.partTextures.Clear(); a.partHeights.Clear();
            var regions = ManifestRegionHeights();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { $"{Root}/Textures/textures" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var m = PartName.Match(Path.GetFileNameWithoutExtension(path));
                if (!m.Success) continue;
                a.partNames.Add($"{m.Groups[1].Value}_{m.Groups[2].Value}_{m.Groups[3].Value}");
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                a.partTextures.Add(tex);
                a.partHeights.Add(regions.TryGetValue(Path.GetFileName(path), out int h) ? h : tex != null ? tex.height : 0);
            }
            a.portraitBackground = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Resources/GoF2Hud/portrait_bg.png");
            a.portraitFrame = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Resources/GoF2Hud/portrait_frame.png");
            if (a.portraitBackground == null || a.portraitFrame == null) Debug.LogWarning("GoF2: portrait background / frame missing, run Build HUD Images");

            a.introAtmo = Clip("MUSIC/IntroAtmo_02.ogg");
            a.battleFull = Clip("MUSIC/Space_Battle_Full.ogg");
            a.timeShift = Clip("MUSIC/Space_Battle_Medium.ogg");
            a.cutsceneExplosion = Clip("SFX_SPACE/Cutscenes_Explosion_01.ogg");
            a.rumble = Clip("SFX_SPACE/Rumble_CutScene_01.ogg");
            a.timeJump = Clip("CUTSCENES/SpaceTimeJump_01.ogg");
            a.timeJumpEnd = a.timeJump;
            a.engineBroken = Clip("SFX_SPACE/Engine_09_Broken.ogg");
            a.engineBrokenLoop = Clip("SFX_SPACE/Spaceship_Engine_05_Broken_02.ogg");
            a.engineBrokenAdds = System.Array.ConvertAll(new[] { 1, 5, 2, 3, 4, 6, 7, 8, 9, 10, 11, 13, 14, 12 },
                n => Clip($"{(n >= 12 ? "CUTSCENES" : "SFX_SPACE")}/Spaceship_Engine_05_Broken_02_Add_{n:00}.ogg"));
            a.introSky = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Skyboxes/skybox_003.mat");
            a.introSkyAfterJump = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Skyboxes/skybox_009.mat");
            a.hyperDrive = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Prefabs/main/fx/hyper_drive.prefab");
            a.menuStatics = new[]
            {
                AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Prefabs/main/misc/beer.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Prefabs/main/misc/bra.prefab"),
            };
            a.wormhole = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Prefabs/main/misc/wormhole_anim_add.prefab");
            a.scannerProbe = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Prefabs/main/misc/scanner_probe.prefab");
            a.voidStationExplosion = new[]
            {
                AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Prefabs/main/fx/explosion_void_station_add.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Prefabs/main/fx/explosion_void_station_add_lookat.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Prefabs/main/fx/explosion_void_station_alpha_lookat.prefab"),
            };
            a.wormholeSound = Clip("SFX_SPACE/Wormhole_1b.ogg");
            a.empHit = Clip("SFX_SPACE/Explosion_EMP_GL1_01.ogg");
            a.probeLaunch = Clip("SFX_SPACE/Explosion_Bomb_AMR_Tormentor_01.ogg");
            a.alert = Clip("CUTSCENES/Alert_03.ogg");
            a.mothershipLoops = new[] { Clip("CUTSCENES/Mothership_Xplosion_Loop_01.ogg"), Clip("CUTSCENES/Explosion_Mothership_Loop_01.ogg"),
                                        Clip("CUTSCENES/Explosion_Mothership_Loop_05.ogg") };
            a.mothershipAdds1 = System.Array.ConvertAll(new[] { 1, 9, 10, 11, 12, 13, 14, 2, 3, 4, 5, 6, 7, 8 }, n => Clip($"CUTSCENES/Mothership_Xplosion_Loop_01_Add_{n:00}.ogg"));
            a.mothershipAdds2 = System.Array.ConvertAll(new[] { 2, 13, 14, 15, 16, 17, 18, 19, 10, 11, 12 }, n => Clip($"CUTSCENES/Mothership_Xplosion_Loop_01_Add_{n:00}.ogg"));
            a.mothershipCutscene = Clip("CUTSCENES/Mothership_Xplosion_CutSeq.ogg");
            a.errktCutscene = Clip("MUSIC/Errkt_CutSeq_01.ogg");
            a.outroSong = Clip("MUSIC/OutroSong_02b.ogg");
            a.voidMusic = Clip("MUSIC/Space_NoCombat_Void.ogg");
            a.voidBattle = Clip("MUSIC/Space_Battle_Void.ogg");
            a.deepScienceAttacked = Clip("DLC_MUSIC/DeepScienceAttacked_02.ogg");
            a.valkyrieBattlemode = Clip("DLC_MUSIC/ValkyrieBattlemode_CutSeq_01.ogg");
            a.endingUxml = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.VisualTreeAsset>($"{Root}/UI/Ending/Ending.uxml");

            EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssets();
            Debug.Log($"GoF2: story assets at {AssetPath} ({a.voiceClipsEng.Count} English / {a.voiceClipsDeu.Count} German voice lines, " +
                      $"{a.partTextures.Count} portrait parts).");
        }

        /// <summary>Textures/_texture_manifest.json: png name -> height of its first region (the .aei image inside the canvas).</summary>
        static System.Collections.Generic.Dictionary<string, int> ManifestRegionHeights()
        {
            var map = new System.Collections.Generic.Dictionary<string, int>();
            string path = $"{Root}/Textures/_texture_manifest.json";
            if (!File.Exists(path)) { Debug.LogWarning("GoF2: texture manifest missing"); return map; }
            // "regions": [[x, y, w, h]] is a nested array (JsonUtility can't read it): a small regex per entry instead.
            var entry = new Regex(@"""regions"":\s*\[\s*\[\s*-?\d+,\s*-?\d+,\s*(\d+),\s*(\d+)\s*\][^{}]*?""png"":\s*""([^""]+)""");
            foreach (Match m in entry.Matches(File.ReadAllText(path)))
                map[Path.GetFileName(m.Groups[3].Value)] = int.Parse(m.Groups[2].Value);
            return map;
        }

        static AudioClip Clip(string rel)
        {
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>($"{Root}/Audio/{rel}");
            if (c == null) Debug.LogWarning($"GoF2: missing clip {rel}");
            return c;
        }

        static void AddVoices(string folder, string prefix, System.Collections.Generic.List<string> names, System.Collections.Generic.List<AudioClip> clips)
        {
            if (!AssetDatabase.IsValidFolder(folder)) { Debug.LogWarning($"GoF2: missing voice folder {folder}"); return; }
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                // "de_" (VOICE, DLC_VOICE, GENERIC, LOUNGE) or "DE_" (all of DLC2_VOICE_deu, the Supernova lines).
                if (prefix.Length > 0 && name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)) name = name.Substring(prefix.Length);
                names.Add(name);
                clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(path));
            }
        }
    }
}
