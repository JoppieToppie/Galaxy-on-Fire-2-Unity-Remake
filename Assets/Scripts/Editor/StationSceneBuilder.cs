// StationSceneBuilder.cs  (Editor only)
// Menu "GoF2/Scenes/Station Scene": Assets/Scenes/Station.unity, the docked station (hangar + Space Lounge).
// Like the flight level the scene is almost empty (camera, one light, post-processing, StationLevel, the station
// menu UI); StationLevel builds the current station's hangar and bar at runtime. This wires up what the level
// can't load by name: the bar visitor / glow / shadow single-mesh prefabs, the glow material per bar race, music,
// ambience, button and hangar sounds, language tables. Builds the shop icons (GoF2 > Build > Item Icons) and the star map
// assets (GoF2 > Build > Star Map Assets) if missing.
// Build settings: after Space. Docking in Space loads it, its launch button loads Space.

using System.IO;
using System.Linq;
using GoF2Remake.UI;
using GoF2Remake.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace GoF2Remake.EditorTools
{
    public static class StationSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Station.unity";
        const string Root = ImportSettings.Root;

        [MenuItem("GoF2/Scenes/Station Scene", priority = 102)]
        public static void Build()
        {
            try { BuildScene(); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        static void BuildScene()
        {
            if (!File.Exists($"{SkyboxBaker.SpaceSkyDir}/nebula_018.png")) SkyboxBaker.BakeSpaceSkies();
            if (!File.Exists($"{ItemIconBuilder.OutDir}/item_232.png")) ItemIconBuilder.Build();
            if (!StarMapBuilder.Exists) StarMapBuilder.Build();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cam.clearFlags = CameraClearFlags.Skybox;

            var light = new GameObject("Key light (LIGHT0)").AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;

            PostProcessing.AddToScene();

            var levelGo = new GameObject("Station Level");
            var level = levelGo.AddComponent<StationLevel>();
            level.mainCamera = cam;
            level.keyLight = light;
            level.visitorTerranMale = Prefab("bar_visitor_terran_m");
            level.visitorTerranFemale = Prefab("bar_visitor_terran_f");
            level.visitorVossk = Prefab("bar_visitor_vossk");
            level.visitorNivelian = Prefab("bar_visitor_nivelian");
            level.visitorMultipod = Prefab("bar_visitor_multipod");
            level.visitorBobolan = Prefab("bar_visitor_bobolan");
            level.visitorGrey = Prefab("bar_visitor_grey");
            level.visitorGlow = Prefab("bar_visitor_glow");
            level.visitorShadow = Prefab("bar_visitor_shadow");
            level.glowMaterials = new[] { "terran", "vossk", "nivelian", "midorian" }
                .Select((r, i) => AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/mat_{34800 + i}_bar_visitor_glow_{r}.mat")).ToArray();

            level.musicSource = Source(levelGo, true);
            // Indexed by StationTables.Music: Terran, Vossk, Nivelian, Midorian, home base, Valkyrie, Deep Science.
            level.music = new[]
            {
                Clip("MUSIC/Station_Terraner.ogg"), Clip("MUSIC/Station_Vossk.ogg"), Clip("MUSIC/Station_Nivelianer.ogg"),
                Clip("MUSIC/Station_Midorianer.ogg"), Clip("MUSIC/HomeBase_Station.ogg"), Clip("MUSIC/Station_Valkyrie.ogg"),
                Clip("DLC2_MUSIC/DeepScience_Station_02.ogg"),
            };
            level.mainViewAmbience = Clip("SFX_STATION_MAINVIEW/Station_Atmo_Mainview_1.ogg");
            level.mainViewAdds = Clips("SFX_STATION_MAINVIEW", "Station_Atmo_Mainview_Add_");
            level.loungeAmbience = Clip("SFX_STATION_LOUNGE/Station_Atmo_Lounge_1.ogg");
            level.loungeAdds = Clips("SFX_STATION_LOUNGE", "Station_Atmo_Lounge_Add_");
            level.hangarAmbience = Clip("SFX_STATION_HANGAR/Station_Atmo_Hangar3.ogg");
            level.hangarAdds = Clips("SFX_STATION_HANGAR", "Station_Atmo_Hangar_Add_");
            EditorUtility.SetDirty(level);

            var uiGo = new GameObject("Station Menu");
            var panel = uiGo.AddComponent<PanelRenderer>();
            panel.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>($"{Root}/UI/GoF2PanelSettings.asset");
            panel.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"{Root}/UI/Station/StationMenu.uxml");
            EditorUtility.SetDirty(panel);
            var menu = uiGo.AddComponent<StationMenu>();
            menu.level = level;
            menu.sfxSource = Source(uiGo, false);
            menu.buttonPush = Clip("SFX_GENERAL/Button_Push_v06.ogg");
            menu.buttonRelease = Clip("SFX_GENERAL/Button_Release_V06.ogg");
            menu.infoSound = Clip("SFX_GENERAL/Message_Info_Screen_v04.ogg");
            menu.shopBuy = Clip("SFX_STATION_HANGAR/Button_to_ship_v01.ogg");
            menu.shopSell = Clip("SFX_STATION_HANGAR/Button_to_station_v01.ogg");
            menu.shopMount = Clip("SFX_STATION_HANGAR/Button_Mount_v01.ogg");
            menu.shopDemount = Clip("SFX_STATION_HANGAR/Button_Dismount_v02.ogg");
            var tables = Directory.GetFiles($"{Root}/Localization", "text_*.json").Select(p => p.Replace('\\', '/'))
                .OrderBy(p => Path.GetFileNameWithoutExtension(p) == "text_en" ? 0 : 1).ToArray();   // English first (fallback)
            menu.languageCodes = tables.Select(p => Path.GetFileNameWithoutExtension(p).Substring(5)).ToArray();
            menu.languageTables = tables.Select(p => AssetDatabase.LoadAssetAtPath<TextAsset>(p)).ToArray();
            WriteLanguageTables(menu.languageCodes, menu.languageTables);
            EditorUtility.SetDirty(menu);

            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();

            RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>($"{SkyboxBaker.SpaceSkyDir}/SpaceSky.mat");
            EditorSceneManager.SaveScene(scene, ScenePath);

            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            int space = list.FindIndex(s => s.path == SpaceSceneBuilder.ScenePath);
            list.Insert(space >= 0 ? space + 1 : list.Count, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log($"GoF2: station scene created at {ScenePath} (station = Session.StationIndex, or the level's override).");
        }

        /// <summary>Resources/GoF2LanguageTables: lets any scene load the text table on first use (Localization).</summary>
        public static void WriteLanguageTables(string[] codes, TextAsset[] tables)
        {
            string path = $"{Root}/Resources/{GoF2Remake.Data.LanguageTables.ResourceName}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<GoF2Remake.Data.LanguageTables>(path);
            if (asset == null) { asset = ScriptableObject.CreateInstance<GoF2Remake.Data.LanguageTables>(); AssetDatabase.CreateAsset(asset, path); }
            asset.codes = codes;
            asset.tables = tables;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        static AudioSource Source(GameObject go, bool loop)
        {
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            return s;
        }

        static GameObject Prefab(string name)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Prefabs/main/bars/{name}.prefab");
            if (p == null) Debug.LogWarning($"GoF2: missing prefab {name}");
            return p;
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
