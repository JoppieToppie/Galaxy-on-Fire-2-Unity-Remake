// AssetImport.cs  (Editor only)
// 1) AssetPostprocessor: sets correct import settings for the converted GoF2 textures, models and audio.
// 2) Menu "GoF2/Build Materials And Prefabs": uses Resources/GoF2Data/resources.json (recovered from the
//    game's own resource table) to create one material per game material and one prefab per game mesh,
//    with the exact texture/shader binding the original used.
// 3) Menu "GoF2/Create Flight Test Scene": ship + chase camera, ready to press Play.
//
// Materials use the GoF2 Shader Graphs (Shaders/*.shadergraph, URP) for unlit/additive/alpha layers and URP Lit
// for bump-mapped hulls.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GoF2Remake.EditorTools
{
    public static class ImportSettings
    {
        public const string Root = "Assets";
        /// <summary>Meters per GoF unit. Must match ShipController.metersPerUnit (default 0.05).</summary>
        public const float ModelScale = 0.05f;
        /// <summary>The remake's own models (custom_ships.json): imported in their file units, unflipped, own materials.</summary>
        public const string CustomModels = Root + "/Models/custom/";
        public static bool IsCustom(string assetPath) => assetPath.StartsWith(CustomModels);
    }

    public class ImportPostprocessor : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (ImportSettings.IsCustom(assetPath)) { PreprocessCustomTexture(); return; }
            if (!assetPath.StartsWith(ImportSettings.Root + "/Textures/")) return;
            var ti = (TextureImporter)assetImporter;
            string name = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            ti.maxTextureSize = 2048;
            ti.mipmapEnabled = true;

            if (name.Contains("_normal_specular") || name.EndsWith("_normal"))
            {
                ti.textureType = TextureImporterType.NormalMap;   // alpha (specular) is ignored here
            }
            else if (name.Contains("_metallic_smoothness"))
            {
                ti.sRGBTexture = false;
            }
            else if (name.Contains("cubemap"))
            {
                // Original stores cubemaps as a vertical 1x6 strip; Unity's auto layout detects this.
                ti.textureShape = TextureImporterShape.TextureCube;
                ti.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
            }
            else if (IsFxAtlas(assetPath, name))
            {
                // Effect atlases (explosions, projectiles, sparks, engine glow): each mesh maps one cell, so repeat wrapping
                // pulled the opposite edge's cells into the outer cells (lines on explosions): clamped. Mipmaps as the
                // original (TextureCreateFromFileIntern: every fx .aei is type 3 / 0x42, image flag 2 = glGenerateMipmap,
                // trilinear, 8x anisotropic); without them the small sprites (exhaust, engine glow) shimmered and looked
                // pixelated.
                ti.mipmapEnabled = true;
                ti.filterMode = FilterMode.Trilinear;
                ti.anisoLevel = 8;
                ti.wrapMode = FxRepeats(name) ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            }
            else if (assetPath.Contains("/Textures/textures/"))
            {
                // 2D UI/HUD art (fonts, icons, portraits). Imported as sprites.
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = true;
            }
        }

        /// <summary>The fx textures that are atlases of separate cells (not the tiling fog, noise, stream and sphere maps).</summary>
        /// <summary>Effect textures whose meshes map past 0..1 (the original never clamps: TextureCreateFromFileIntern uses
        /// GL_REPEAT, Engine::clampTextures is only ever 0): khador_jump's expanding streak (u -0.28..1.28) and the time
        /// jump's hyper_drive ring (v 0..2) smeared their edge texels clamped. Repeat, the atlas's mipmaps kept.</summary>
        static bool FxRepeats(string name) => name == "khador_jump" || name == "hyper_drive";

        public static bool IsFxAtlas(string path, string name)
        {
            if (!path.Contains("/fx/") || name.EndsWith("_normal")) return false;
            foreach (var tiled in new[] { "fog", "v_fog_ice", "cloak_map", "v_shield_noise", "sn_plasma_stream", "sn_shock_blast_sphere",
                                          "sn_supernova", "sn_ship_blaze_flames" })
                if (name == tiled) return false;
            return true;
        }

        /// <summary>Custom ship textures (PBR sets): *_nrm / *_normal = normal maps, the metallic / smoothness masks linear.</summary>
        void PreprocessCustomTexture()
        {
            var ti = (TextureImporter)assetImporter;
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            ti.mipmapEnabled = true;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            if (name.EndsWith("_nrm") || name.EndsWith("_normal") || name.Contains("_normal_")) ti.textureType = TextureImporterType.NormalMap;
            else
            {
                ti.textureType = TextureImporterType.Default;
                bool mask = name.Contains("metallic_smoothness") || name.EndsWith("_metal") || name.EndsWith("_rough") || name.EndsWith("_alpha");
                ti.sRGBTexture = !mask;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
            }
        }

        /// <summary>Custom ship models keep their file units (the builder scales the hull to its modelLength) and their
        /// facing; materials come from custom_ships.json.</summary>
        void PreprocessCustomModel()
        {
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importAnimation = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.indexFormat = ModelImporterIndexFormat.Auto;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.isReadable = false;
        }

        void OnPreprocessModel()
        {
            if (ImportSettings.IsCustom(assetPath)) { PreprocessCustomModel(); return; }
            if (!assetPath.StartsWith(ImportSettings.Root + "/Models/")) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = ImportSettings.ModelScale;
            mi.useFileScale = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.None; // materials come from resources.json
            mi.importAnimation = false;       // keyframes are in the .gof2mesh.json sidecar (see PartAnimation)
            mi.importCameras = false;
            mi.importLights = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.isReadable = false;
        }

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(ImportSettings.Root + "/Audio/")) return;
            var ai = (AudioImporter)assetImporter;
            var s = ai.defaultSampleSettings;
            string p = assetPath.ToUpperInvariant();
            if (p.Contains("MUSIC") || p.Contains("CUTSCENES")) s.loadType = AudioClipLoadType.Streaming;
            else if (p.Contains("VOICE") || p.Contains("LOUNGE") || p.Contains("GENERIC")) s.loadType = AudioClipLoadType.CompressedInMemory;
            else s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            ai.defaultSampleSettings = s;
            ai.loadInBackground = s.loadType != AudioClipLoadType.DecompressOnLoad;
        }
    }

    /// <summary>
    /// Turns every converted model 180 degrees around Y at import time (vertices, normals, tangents and
    /// part transforms), so ships face +Z (Unity forward) and game coordinates map to Unity as (x, y, -z).
    /// Kept in its own class so bumping GetVersion() only reimports models, not the 1.5 GB of textures.
    /// </summary>
    public class ModelOrientationPostprocessor : AssetPostprocessor
    {
        public override uint GetVersion() => 1;

        void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.StartsWith(ImportSettings.Root + "/Models/") || ImportSettings.IsCustom(assetPath)) return;
            var r = Quaternion.Euler(0f, 180f, 0f);
            var ri = Quaternion.Inverse(r);

            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh;
                if (m == null) continue;
                var v = m.vertices; for (int i = 0; i < v.Length; i++) v[i] = new Vector3(-v[i].x, v[i].y, -v[i].z);
                m.vertices = v;
                var n = m.normals; for (int i = 0; i < n.Length; i++) n[i] = new Vector3(-n[i].x, n[i].y, -n[i].z);
                if (n.Length > 0) m.normals = n;
                var t = m.tangents; for (int i = 0; i < t.Length; i++) t[i] = new Vector4(-t[i].x, t[i].y, -t[i].z, t[i].w);
                if (t.Length > 0) m.tangents = t;
                m.RecalculateBounds();
            }
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            {
                if (tr == root.transform) continue;
                tr.localPosition = r * tr.localPosition;
                tr.localRotation = r * tr.localRotation * ri;
            }
        }
    }

    // ---- resources.json model (JsonUtility-compatible) ----------------------------------------
    [System.Serializable] public class ResMesh { public int id; public string model; public string source; public int materialId; }
    [System.Serializable] public class ResMaterial { public int id; public int shaderId; public string shading; public string[] textures; }
    [System.Serializable] public class ResTable { public ResMesh[] meshes; public ResMaterial[] materials; }

    public static class PrefabBuilder
    {
        static string R(string rel) => ImportSettings.Root + "/" + rel;

        static bool IsURP()
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            return rp != null && rp.GetType().Name.Contains("Universal");
        }

        [MenuItem("GoF2/Build Materials And Prefabs", priority = 1)]
        public static void BuildAll()
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(R("Resources/GoF2Data/resources.json"));
            if (json == null) { Debug.LogError("GoF2: resources.json not found"); return; }
            var table = JsonUtility.FromJson<ResTable>(json.text);
            bool urp = IsURP();
            Directory.CreateDirectory(R("Materials"));
            Directory.CreateDirectory(R("Prefabs"));

            var mats = new Dictionary<int, Material>();
            try
            {
                AssetDatabase.StartAssetEditing();
                int i = 0;
                foreach (var m in table.materials)
                {
                    if (i++ % 20 == 0) EditorUtility.DisplayProgressBar("GoF2", "Materials", (float)i / table.materials.Length);
                    var mat = CreateMaterial(m, urp);
                    if (mat != null) mats[m.id] = mat;
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.SaveAssets();

            int made = 0, missing = 0, n = 0;
            foreach (var mesh in table.meshes)
            {
                if (n++ % 10 == 0 && EditorUtility.DisplayCancelableProgressBar("GoF2", "Prefabs: " + mesh.model, (float)n / table.meshes.Length)) break;
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(R(mesh.model));
                if (src == null) { missing++; continue; }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
                mats.TryGetValue(mesh.materialId, out var mat);
                if (mat != null)
                    foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                        r.sharedMaterials = Enumerable.Repeat(mat, Mathf.Max(1, r.sharedMaterials.Length)).ToArray();

                var metaPath = Path.ChangeExtension(R(mesh.model), null) + ".gof2mesh.json";
                var meta = AssetDatabase.LoadAssetAtPath<TextAsset>(metaPath);
                if (meta != null && meta.text.Contains("\"target\""))
                {
                    var anim = inst.AddComponent<GoF2Remake.Visuals.PartAnimation>();
                    anim.meta = meta;
                    anim.metersPerUnit = ImportSettings.ModelScale;
                    // Only "_anim" models (FX, turrets, rotating parts) animate by default; others keep their rest pose.
                    anim.play = mesh.model.Contains("_anim");
                }

                var info = inst.AddComponent<GoF2Remake.Visuals.ResourceInfo>();
                info.meshId = mesh.id; info.materialId = mesh.materialId; info.originalPath = mesh.source;

                var outPath = R("Prefabs/" + Path.ChangeExtension(mesh.model.Substring("Models/".Length), ".prefab"));
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                PrefabUtility.SaveAsPrefabAsset(inst, outPath);
                Object.DestroyImmediate(inst);
                made++;
            }
            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            PostProcessing.ApplyGlow();
            Debug.Log($"GoF2: {mats.Count} materials, {made} prefabs created ({missing} meshes referenced by the game were not shipped in the OBB). Pipeline: {(urp ? "URP" : "Built-in")}");
        }

        static Texture2D Tex(string rel) => string.IsNullOrEmpty(rel) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(R(rel));

        static Material CreateMaterial(ResMaterial m, bool urp)
        {
            var tex = m.textures ?? new string[0];
            var main = Tex(tex.Length > 0 ? tex[0] : null);
            string label = main != null ? main.name : "none";
            string path = R($"Materials/mat_{m.id}_{label}.mat");
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader sh;
            switch (m.shading)
            {
                case "unlit": sh = Shader.Find("GoF2/Unlit"); break;
                case "additive": case "additive_anim": sh = Shader.Find("GoF2/Additive"); break;
                case "alpha": case "alpha_anim": sh = Shader.Find("GoF2/AlphaBlend"); break;
                case "lit_alpha_test": sh = Shader.Find("GoF2/AlphaTest"); break;
                default: sh = Shader.Find(urp ? "Universal Render Pipeline/Lit" : "Standard"); break;
            }
            if (sh == null) sh = Shader.Find("Standard");
            var mat = new Material(sh) { name = Path.GetFileNameWithoutExtension(path) };

            bool lit = sh.name == "Standard" || sh.name.StartsWith("Universal Render Pipeline/Lit");
            if (lit)
            {
                if (urp) mat.SetTexture("_BaseMap", main); else mat.SetTexture("_MainTex", main);
                string nrmRel = tex.FirstOrDefault(t => t != null && t.Contains("_normal"));
                var nrm = Tex(nrmRel);
                if (nrm != null) { mat.SetTexture("_BumpMap", nrm); mat.EnableKeyword("_NORMALMAP"); }
                if (!string.IsNullOrEmpty(nrmRel) && nrmRel.Contains("_normal_specular"))
                {
                    var ms = Tex(nrmRel.Replace("_normal_specular", "_metallic_smoothness"));
                    if (ms != null)
                    {
                        mat.SetTexture("_MetallicGlossMap", ms);
                        if (urp) { mat.EnableKeyword("_METALLICSPECGLOSSMAP"); mat.SetFloat("_Smoothness", 0.8f); }
                        else { mat.EnableKeyword("_METALLICGLOSSMAP"); mat.SetFloat("_GlossMapScale", 0.8f); }
                    }
                }
                mat.SetFloat("_Metallic", 0f);
            }
            else mat.SetTexture("_MainTex", main);

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        [MenuItem("GoF2/Reapply Import Settings (reimport Models, Textures, Audio)", priority = 20)]
        public static void ReapplyImportSettings()
        {
            foreach (var f in new[] { "Models", "Textures", "Audio" })
                AssetDatabase.ImportAsset(R(f), ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            Debug.Log("GoF2: import settings reapplied.");
        }

        [MenuItem("GoF2/Reimport Models Only", priority = 21)]
        public static void ReimportModels()
        {
            AssetDatabase.ImportAsset(R("Models"), ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            Debug.Log("GoF2: models reimported.");
        }

        [MenuItem("GoF2/Create Flight Test Scene", priority = 2)]
        public static void CreateTestScene()
        {
            var shipPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(R("Resources/Assembled/main/ships/ship_000_midorian.prefab"))
                             ?? AssetDatabase.LoadAssetAtPath<GameObject>(R("Prefabs/main/ships/ship_000_midorian.prefab"))
                             ?? AssetDatabase.LoadAssetAtPath<GameObject>(R("Models/main/ships/ship_000_midorian.fbx"));
            var root = new GameObject("Ship (Betty)");
            var ctrl = root.AddComponent<GoF2Remake.Flight.ShipController>();
            if (shipPrefab != null)
            {
                var vis = (GameObject)PrefabUtility.InstantiatePrefab(shipPrefab);
                vis.transform.SetParent(root.transform, false);
                ctrl.visualModel = vis.transform;
            }
            // size the chase camera to the ship
            var rends = root.GetComponentsInChildren<Renderer>();
            float size = 10f;
            if (rends.Length > 0)
            {
                var b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);
                size = Mathf.Max(1f, b.size.magnitude);
            }
            var camGo = Camera.main != null ? Camera.main.gameObject : new GameObject("Main Camera", typeof(Camera));
            camGo.tag = "MainCamera";
            // No '??' here: a missing component is a Unity "fake null" that '??' treats as non-null.
            var chase = camGo.GetComponent<GoF2Remake.Flight.ChaseCamera>();
            if (chase == null) chase = camGo.AddComponent<GoF2Remake.Flight.ChaseCamera>();
            chase.target = ctrl;
            chase.offset = new Vector3(0f, size * 0.3f, -size * 1.4f);
            chase.lookOffset = new Vector3(0f, size * 0.1f, size * 0.8f);
            camGo.GetComponent<Camera>().farClipPlane = 50000f;
            PostProcessing.AddToScene();
            // Space sky as the Unity skybox: also the ambient light and reflection source for the ships.
            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyboxBaker.MaterialPath(3));
            if (sky != null)
            {
                RenderSettings.skybox = sky;
                RenderSettings.ambientMode = AmbientMode.Skybox;
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
                camGo.GetComponent<Camera>().clearFlags = CameraClearFlags.Skybox;
            }
            Selection.activeGameObject = root;
            Debug.Log("GoF2: test scene created. Press Play: WASD/arrows steer, Q/E throttle, Space boost, R level.");
        }
    }
}
