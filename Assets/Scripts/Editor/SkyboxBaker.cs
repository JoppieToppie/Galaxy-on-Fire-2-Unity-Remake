// SkyboxBaker.cs  (Editor only)
// The original draws the sky as meshes around the camera (Level::createSpace / Level::renderBG): first the
// stars layer (skybox_stars, texture skybox_stars_00(systemIndex % 3)), then the nebula mesh skybox_0XX
// (texture = SolarSystem textureIndex) added on top. This renders both into the six faces of a cubemap so
// the sky is a regular Unity skybox, which also drives URP ambient light and reflections.
//
// Menu "GoF2/Bake Skyboxes": Assets/Skyboxes/skybox_0XX.png (6-face horizontal strip, imported as a
// Cubemap) + skybox_0XX.mat (Skybox/Cubemap). Used by the main menu.
//
// Menu "GoF2/Bake Space Skies": the flight levels combine the layers at runtime instead, because a sky is really
// the pair (nebula = system textureIndex 0..18, stars = system index % 3) and the original rotates it per station
// (space_backdrop.md). Bakes Resources/GoF2Sky/stars_00X and nebula_0XX cubemaps (nebula on black, it is added)
// plus the SpaceSky.mat template (shader GoF2/SpaceSky) that SpaceLevel instantiates.

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GoF2Remake.EditorTools
{
    public static class SkyboxBaker
    {
        public const string OutDir = ImportSettings.Root + "/Skyboxes";
        public const int SkyboxCount = 11;
        // The sky textures are 2048 x 2048 on meshes around the camera: 1024 px faces (about 11 texels per degree) were
        // magnified on a 1080p screen (15 px per degree at the 1.22 rad FOV) after the bake's own filtering, so the sky
        // looked blurry next to the original's directly drawn meshes. 2048 px faces keep the source detail.
        const int FaceSize = 2048;

        // Unity's horizontal-strip cubemap layout: +X, -X, +Y, -Y, +Z, -Z, each face as seen from the centre.
        static readonly Vector3[] FaceForward = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        static readonly Vector3[] FaceUp = { Vector3.up, Vector3.up, Vector3.back, Vector3.forward, Vector3.up, Vector3.up };

        public const string SpaceSkyDir = ImportSettings.Root + "/Resources/GoF2Sky";

        public static string MaterialPath(int index) => $"{OutDir}/skybox_{index:000}.mat";

        [MenuItem("GoF2/Bake Space Skies", priority = 24)]
        public static void BakeSpaceSkies()
        {
            Directory.CreateDirectory(SpaceSkyDir);
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    EditorUtility.DisplayProgressBar("GoF2", $"Baking stars_{i:000}", i / 22f);
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ImportSettings.Root}/Textures/main/skyboxes/skybox_stars_{i:000}.png");
                    BakeCube($"{SpaceSkyDir}/stars_{i:000}.png", (LoadMesh("Models/main/skyboxes/skybox_stars.fbx"), "GoF2/Unlit", tex));
                }
                for (int t = 0; t <= 18; t++)
                {
                    EditorUtility.DisplayProgressBar("GoF2", $"Baking nebula_{t:000}", (3 + t) / 22f);
                    var (mesh, tex) = NebulaLayer(t);
                    if (mesh == null || tex == null) { Debug.LogError($"GoF2: nebula {t} mesh or texture missing"); continue; }
                    BakeCube($"{SpaceSkyDir}/nebula_{t:000}.png", (mesh, "GoF2/Additive", tex));
                }
                string matPath = $"{SpaceSkyDir}/SpaceSky.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(matPath) == null)
                    AssetDatabase.CreateAsset(new Material(Shader.Find("GoF2/SpaceSky")), matPath);
                AssetDatabase.SaveAssets();
            }
            finally { EditorUtility.ClearProgressBar(); }
            Debug.Log($"GoF2: space skies baked to {SpaceSkyDir}.");
        }

        /// <summary>Nebula mesh + texture for a system textureIndex (Level::createSpace: mesh 17800 + t, texture 10065 + t).</summary>
        static (Mesh, Texture2D) NebulaLayer(int t)
        {
            string R = ImportSettings.Root;
            if (t <= 10) return (LoadMesh($"Models/main/skyboxes/skybox_{t:000}.fbx"),
                                 AssetDatabase.LoadAssetAtPath<Texture2D>($"{R}/Textures/main/skyboxes/skybox_{t:000}.png"));
            if (t <= 14) return (LoadMesh($"Models/valkyrie/skyboxes/v_skybox_{(t == 14 ? 13 : t):000}.fbx"),   // 17814 reuses the 013 mesh
                                 AssetDatabase.LoadAssetAtPath<Texture2D>($"{R}/Textures/valkyrie/skyboxes/v_skybox_{t:000}.png"));
            return (LoadMesh($"Models/supernova/skyboxes/sn_skybox_{t:000}.fbx"),
                    AssetDatabase.LoadAssetAtPath<Texture2D>($"{R}/Textures/supernova/skyboxes/sn_skybox_{t:000}.png"));
        }

        [MenuItem("GoF2/Bake Skyboxes", priority = 23)]
        public static void BakeAll()
        {
            try
            {
                for (int i = 0; i < SkyboxCount; i++)
                {
                    EditorUtility.DisplayProgressBar("GoF2", $"Baking skybox_{i:000}", (float)i / SkyboxCount);
                    Bake(i);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            Debug.Log($"GoF2: {SkyboxCount} skyboxes baked to {OutDir}.");
        }

        /// <summary>Bakes skybox_{index} (+ its stars layer) and returns its skybox material.</summary>
        public static Material Bake(int index)
        {
            Directory.CreateDirectory(OutDir);
            string name = $"skybox_{index:000}";
            var nebulaMesh = LoadMesh($"Models/main/skyboxes/{name}.fbx");
            var starsMesh = LoadMesh("Models/main/skyboxes/skybox_stars.fbx");
            var nebulaTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ImportSettings.Root}/Textures/main/skyboxes/{name}.png");
            var starsTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ImportSettings.Root}/Textures/main/skyboxes/skybox_stars_{index % 3:000}.png");
            if (nebulaMesh == null || nebulaTex == null) { Debug.LogError($"GoF2: {name} mesh or texture missing"); return null; }

            var cube = BakeCube($"{OutDir}/{name}.png", (starsMesh, "GoF2/Unlit", starsTex), (nebulaMesh, "GoF2/Additive", nebulaTex));

            string matPath = MaterialPath(index);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Skybox/Cubemap"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_Tex", cube);
            mat.SetFloat("_Exposure", 1f);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        /// <summary>Renders the layers (in order, from the centre) into a 6-face strip PNG imported as a Cubemap.</summary>
        static Cubemap BakeCube(string pngPath, params (Mesh mesh, string shader, Texture tex)[] layers)
        {
            var strip = new Texture2D(FaceSize * 6, FaceSize, TextureFormat.RGB24, false);
            var scene = EditorSceneManager.NewPreviewScene();
            var rt = new RenderTexture(FaceSize, FaceSize, 24, RenderTextureFormat.ARGB32);
            var mats = new Material[layers.Length];
            try
            {
                for (int i = 0; i < layers.Length; i++) mats[i] = Layer(scene, layers[i].mesh, Shader.Find(layers[i].shader), layers[i].tex);

                var camGo = new GameObject("SkyboxCamera");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.fieldOfView = 90f;
                cam.aspect = 1f;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 1000f;
                cam.targetTexture = rt;
                cam.GetUniversalAdditionalCameraData().renderPostProcessing = false;

                var prev = RenderTexture.active;
                for (int f = 0; f < 6; f++)
                {
                    camGo.transform.rotation = Quaternion.LookRotation(FaceForward[f], FaceUp[f]);
                    cam.Render();
                    RenderTexture.active = rt;
                    strip.ReadPixels(new Rect(0, 0, FaceSize, FaceSize), f * FaceSize, 0);
                }
                RenderTexture.active = prev;
                strip.Apply();
                cam.targetTexture = null;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                rt.Release();
                Object.DestroyImmediate(rt);
                foreach (var m in mats) if (m != null) Object.DestroyImmediate(m);
            }

            File.WriteAllBytes(pngPath, strip.EncodeToPNG());
            Object.DestroyImmediate(strip);
            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceSynchronousImport);
            var ti = (TextureImporter)AssetImporter.GetAtPath(pngPath);
            ti.textureShape = TextureImporterShape.TextureCube;
            ti.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
            ti.mipmapEnabled = true;
            ti.maxTextureSize = 16384;   // the 6-face strip's width
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            // Phones: the strip at 8192 (faces of 1365 px), two of them in memory per orbit.
            var android = ti.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 8192;
            ti.SetPlatformTextureSettings(android);
            ApplyUwpOverride(ti);
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Cubemap>(pngPath);
        }

        /// <summary>UWP (#20): BC1 instead of the default's BC7 (CompressedHQ). On the Xbox the nebula cubemaps showed bands of
        /// shifted tiles while every BC1 / BC3 texture drew right; Windows keeps BC7.</summary>
        public static void ApplyUwpOverride(TextureImporter ti)
        {
            var uwp = ti.GetPlatformTextureSettings("WindowsStoreApps");
            uwp.overridden = true;
            uwp.maxTextureSize = 16384;
            uwp.format = TextureImporterFormat.DXT1;
            ti.SetPlatformTextureSettings(uwp);
        }

        static Material Layer(UnityEngine.SceneManagement.Scene scene, Mesh mesh, Shader shader, Texture tex)
        {
            if (mesh == null) return null;
            var go = new GameObject(mesh.name);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mat = new Material(shader);
            mat.SetTexture("_MainTex", tex);
            mat.SetFloat("_Cull", 0f); // seen from inside
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return mat;
        }

        static Mesh LoadMesh(string rel)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath($"{ImportSettings.Root}/{rel}"))
                if (o is Mesh m) return m;
            return null;
        }
    }
}
