// StarMapBuilder.cs  (Editor only)
// Menu "GoF2/Build/Star Map Assets": Resources/GoF2StarMap/StarMapAssets (StarMapAssets), the references the runtime
// star map and the system jumps can't load by name. Sun materials: mesh 18070 + texture index -> its materialId in
// resources.json -> Materials/mat_<id>_*.mat. Run by Create Space Scene and Create Station Scene when missing.

using System.IO;
using System.Linq;
using GoF2Remake.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.EditorTools
{
    public static class StarMapBuilder
    {
        const string Root = ImportSettings.Root;
        public const string AssetPath = Root + "/Resources/" + StarMapAssets.ResourcePath + ".asset";

        [System.Serializable] class MeshRow { public int id; public int materialId; }
        [System.Serializable] class ResourceTable { public MeshRow[] meshes; }

        public static bool Exists => File.Exists(AssetPath);

        [MenuItem("GoF2/Build/Star Map Assets", priority = 204)]
        public static void Build()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            var a = AssetDatabase.LoadAssetAtPath<StarMapAssets>(AssetPath);
            if (a == null) { a = ScriptableObject.CreateInstance<StarMapAssets>(); AssetDatabase.CreateAsset(a, AssetPath); }

            a.layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"{Root}/UI/StarMap/StarMap.uxml");
            a.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>($"{Root}/UI/GoF2PanelSettings.asset");

            var table = JsonUtility.FromJson<ResourceTable>(File.ReadAllText($"{Root}/Resources/GoF2Data/resources.json"));
            int maxTex = 0;
            var db = GoF2Remake.Data.Database.Load();
            foreach (var s in db.Systems) maxTex = Mathf.Max(maxTex, s.textureIndex);
            a.sunMaterials = new Material[maxTex + 1];
            for (int tex = 0; tex <= maxTex; tex++)
            {
                var row = table.meshes.FirstOrDefault(m => m.id == 18070 + tex);
                if (row == null) continue;
                string guid = AssetDatabase.FindAssets($"mat_{row.materialId}_ t:Material", new[] { $"{Root}/Materials" }).FirstOrDefault();
                if (guid != null) a.sunMaterials[tex] = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (a.sunMaterials[tex] == null) Debug.LogWarning($"GoF2: no sun material for texture index {tex} (material {row.materialId})");
            }
            a.khadorJump = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Prefabs/main/fx/khador_jump.prefab");

            a.selectSystem = Clip("SFX_STATION_MAINVIEW/Select_System_04.ogg");
            a.planetPush = Clip("SFX_STATION_MAINVIEW/Map_Select_Planet_Push_01.ogg");
            a.planetRelease = Clip("SFX_STATION_MAINVIEW/Map_Select_Planet_Release_01.ogg");
            a.zoomIn = Clip("SFX_STATION_MAINVIEW/Map_Zoom_In.ogg");
            a.zoomOut = Clip("SFX_STATION_MAINVIEW/Map_Zoom_Out.ogg");
            a.buttonPush = Clip("SFX_GENERAL/Button_Push_v06.ogg");
            a.buttonRelease = Clip("SFX_GENERAL/Button_Release_V06.ogg");
            a.infoSound = Clip("SFX_GENERAL/Message_Info_Screen_v04.ogg");
            a.jumpgate = new[] { Clip("SFX_SPACE/Jumpgate_3b.ogg"), Clip("SFX_SPACE/Jumpgate_4c.ogg"), Clip("SFX_SPACE/Jumpgate_1b.ogg"), Clip("SFX_SPACE/Jumpgate_2b.ogg") };
            a.jumpgateCharge = Clip("SFX_SPACE/Jumpgate_Charge_02.ogg");
            a.khadorDrive = Clip("SFX_SPACE/Jumpgate_5c.ogg");
            a.mapClick = Clip("SFX_STATION_MAINVIEW/Map_Click_01.ogg");

            EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssets();
            Debug.Log($"GoF2: star map assets at {AssetPath} ({a.sunMaterials.Count(m => m != null)} sun materials).");
        }

        static AudioClip Clip(string rel)
        {
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>($"{Root}/Audio/{rel}");
            if (c == null) Debug.LogWarning($"GoF2: missing audio {rel}");
            return c;
        }
    }
}
