// StoryAssets.cs
// What the story presentation can't load by name (Resources/GoF2Story/StoryAssets, built by "GoF2 > Build > Story Assets"):
//   voice lines   Audio/VOICE_*, DLC_VOICE_*, DLC2_VOICE_* (FMOD voice banks, dialogue_cutscenes.md 5.1), English and
//                 German (the German files carry a "de_" prefix; only part of the lines were recorded in German)
//   portraits     the face parts Textures/textures/<body>_<part>_<variant>_ipad_large.png (ImageFactory::loadImage 0x141870)
//                 plus the portrait background 0x485 and frame 0x511 (Resources/GoF2Hud)
//   cutscenes     the prologue's sounds (FMOD ids 141-143, 156-161, fmod_event_ids.txt with dialogue_cutscenes.md 5.2's
//                 correction) and its skies (skybox_003 = Level::createSpace's intro sky 0x458b / 0x2754, skybox_009 =
//                 switchSkyboxForIntro 0x4591 / 0x275a)
// The voice clips don't preload their audio data, so referencing all of them here is cheap.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class StoryAssets : ScriptableObject
    {
        public List<string> voiceNamesEng = new List<string>();
        public List<AudioClip> voiceClipsEng = new List<AudioClip>();
        public List<string> voiceNamesDeu = new List<string>();
        public List<AudioClip> voiceClipsDeu = new List<AudioClip>();
        [Tooltip("\"body_part_variant\" names of the portrait parts.")]
        public List<string> partNames = new List<string>();
        public List<Texture2D> partTextures = new List<Texture2D>();
        [Tooltip("Height of each part's image inside its power-of-two canvas (the .aei region, Textures/_texture_manifest.json).")]
        public List<int> partHeights = new List<int>();
        public Texture2D portraitBackground, portraitFrame;
        [Header("Cutscenes")]
        public AudioClip introAtmo;           // 143 IntroAtmo
        public AudioClip battleFull;          // 142 Space_Combat_Full
        public AudioClip timeShift;           // 141 Space_Combat_Mid (Space_Battle_Medium, looped): after the time jump
        public AudioClip cutsceneExplosion;   // 157 Cutscenes_Explosion
        public AudioClip rumble;              // 158 Rumble_CutScene_01
        public AudioClip timeJumpEnd;         // 159 TimeSpaceJumpEnd (no file of its own: SpaceTimeJump stands in)
        public AudioClip timeJump;            // 160 SpaceTimeJump
        public AudioClip engineBroken;        // 161 Engine_09_Broken
        public AudioClip engineBrokenLoop;    // 156 Spaceship_Engine_05_Broken
        public AudioClip[] engineBrokenAdds;  // 156's oneshot layer (Spaceship_Engine_05_Broken_02_Add_01 sound definition, 14 waves)
        public Material introSky, introSkyAfterJump;
        public GameObject hyperDrive;         // mesh 15027 hyper_drive
        public GameObject[] menuStatics;      // meshes 0x37d0 beer, 0x37d1 bra: the index-43 menu backdrop (Level::createScene)
        [Header("Void campaign (campaign_levels_a.md 3.5-3.16)")]
        public GameObject wormhole;           // mesh 16994 wormhole_anim_add (PlayerWormHole, landmark 3)
        public GameObject scannerProbe;       // mesh 14290 scanner_probe (index 29)
        public GameObject[] voidStationExplosion;   // meshes 14285-14287 explosion_void_station_* (index 42)
        public AudioClip wormholeSound;       // 34 Wormhole
        public AudioClip empHit;              // 15 Explosion_EMP_GL1 (index 14)
        public AudioClip probeLaunch;         // 14 (index 29)
        public AudioClip[] mothershipLoops;   // 153's three loop layers (MothershipSound)
        public AudioClip[] mothershipAdds1, mothershipAdds2;   // 153's one-shot layers (Loop_01_Add_01 / _Add_02 sound definitions)
        public AudioClip mothershipCutscene;  // 154 Mothership_Xplosion_CutSeq
        public AudioClip errktCutscene;       // 155 Errkt_CutSeq_01
        public AudioClip outroSong;           // 144 OutroSong (the ending)
        public AudioClip alert;               // 162 Alert (Alert_03, looped): step 15's Void alarm (DialogueWindow::loadContent)
        public AudioClip voidMusic;           // 145 Space_NoCombat_Void (the alien orbit)
        public AudioClip voidBattle;          // 136 Space_Combat_Void (the alien orbit / an attacked station)
        [Header("Valkyrie campaign (campaign_levels_b.md 78 / 80)")]
        public AudioClip deepScienceAttacked; // 1121 (name-matched DLC_MUSIC/DeepScienceAttacked, index 80)
        public AudioClip valkyrieBattlemode;  // 1122 (name-matched DLC_MUSIC/ValkyrieBattlemode_CutSeq, index 78)
        [Tooltip("UI/Ending/Ending.uxml: the ending's overlay (EndingCredits, shown by the main menu).")]
        public VisualTreeAsset endingUxml;

        Dictionary<string, AudioClip> eng, deu;
        Dictionary<string, (Texture2D tex, int height)> parts;

        static StoryAssets instance;
        public static StoryAssets Load() => instance != null ? instance : instance = Resources.Load<StoryAssets>("GoF2Story/StoryAssets");

        /// <summary>A voice line by its event name; German when the voices are German (Settings.GermanVoices: the voice
        /// language option, by default the text language) and the line was recorded, else English (FMOD's language banks).</summary>
        /// <summary>Re-recorded lines keep their event name but the wave is named "&lt;event&gt;_Alt2" (Brillo Lampeter's).</summary>
        const string AltSuffix = "_Alt2";

        public AudioClip Voice(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            eng ??= Map(voiceNamesEng, voiceClipsEng);
            deu ??= Map(voiceNamesDeu, voiceClipsDeu);
            if (Settings.GermanVoices && (deu.TryGetValue(name, out var d) || deu.TryGetValue(name + AltSuffix, out d))) return d;
            return eng.TryGetValue(name, out var e) || eng.TryGetValue(name + AltSuffix, out e) ? e : null;
        }

        /// <summary>A portrait part: its texture (the image sits in the top-left corner of the canvas) and image height.</summary>
        public (Texture2D tex, int height) Part(int body, int part, int variant) => Part($"{body}_{part}_{variant}");

        public (Texture2D tex, int height) Part(string key)
        {
            if (parts == null)
            {
                parts = new Dictionary<string, (Texture2D, int)>();
                for (int i = 0; i < partNames.Count && i < partTextures.Count; i++)
                    parts[partNames[i]] = (partTextures[i], i < partHeights.Count ? partHeights[i] : partTextures[i] != null ? partTextures[i].height : 0);
            }
            return parts.TryGetValue(key, out var t) ? t : (null, 0);
        }

        static Dictionary<string, T> Map<T>(List<string> names, List<T> values)
        {
            var d = new Dictionary<string, T>();
            for (int i = 0; i < names.Count && i < values.Count; i++) d[names[i]] = values[i];
            return d;
        }
    }
}
