// AssetImport.cs  (Editor only)
// 1) AssetPostprocessor: sets correct import settings for the converted GoF2 textures, models and audio.
// 2) Menu "GoF2/Build/Materials And Prefabs": uses Resources/GoF2Data/resources.json (recovered from the
//    game's own resource table) to create one material per game material and one prefab per game mesh,
//    with the exact texture/shader binding the original used.
// 3) Menu "GoF2/Legacy/Flight Test Scene": ship + chase camera, ready to press Play.
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
    }

    public class ImportPostprocessor : AssetPostprocessor
    {
        /// <summary>Textures the Texture quality option (Settings.TextureQuality, QualitySettings.globalTextureMipmapLimit)
        /// leaves at full size: the space skies (their stars blur when smaller), the reflection cubemaps (picked by mip
        /// level for their blur), the HUD / UI images.</summary>
        static bool KeepsFullMips(string path, string name) =>
            path.StartsWith("Assets/Resources/GoF2Sky/") || path.StartsWith("Assets/Resources/GoF2Reflections/")
            || path.StartsWith("Assets/Resources/GoF2Hud/") || path.StartsWith("Assets/UI/")
            || (path.StartsWith(ImportSettings.Root + "/Textures/") && name.Contains("cubemap"));

        void OnPreprocessTexture()
        {
            string name = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            if (KeepsFullMips(assetPath, name)) ((TextureImporter)assetImporter).ignoreMipmapLimit = true;
            if (!assetPath.StartsWith(ImportSettings.Root + "/Textures/")) return;
            var ti = (TextureImporter)assetImporter;
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
            else if (name == "skybox_asteroid_belt_diffuse")
            {
                // The asteroid belt sky layer (systems 24-26, SkyLayers): uncompressed on UWP like the space skies (#20,
                // SkyboxBaker.ApplyUwpOverride): the Xbox drew its compressed blocks as a checkerboard of squares.
                var uwp = ti.GetPlatformTextureSettings("WindowsStoreApps");
                uwp.overridden = true;
                uwp.maxTextureSize = 2048;
                uwp.format = TextureImporterFormat.RGBA32;
                ti.SetPlatformTextureSettings(uwp);
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

        void OnPreprocessModel()
        {
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
            // Read/Write only where runtime code reads the mesh: Valkyrie 80 splits the deep science station's damaged
            // thruster flames per engine (ValkyrieLevels.MakeFlamePieces80, #70), and the hulls get MeshColliders
            // (HullCollision: a player build can't cook a collider from a mesh it can't read).
            // Modified: and the supernova flares' sky sphere, which SkySphereSmoothing subdivides onto the sphere.
            mi.isReadable = assetPath.EndsWith("/v_station_deep_science_damaged_emitters_anim_add.fbx") || IsHullModel(assetPath)
                            // Modified: the hangar rooms (not their glow layers): Inspect ship's camera stops at their walls
                            // (StationLevel.AddRoomColliders).
                            || (assetPath.Replace('\\', '/').Contains("/hangars/") && !assetPath.Contains("_add") && !assetPath.Contains("_anim"))
                            || (assetPath.Contains("/skyboxes/sn_skybox_") && assetPath.Contains("_flares_"));
        }

        /// <summary>A model whose meshes become collision hulls (HullCollision): ships, stations, jumpgates, turrets and the
        /// level objects (misc), not their additive / light layers or lower LODs.</summary>
        public static bool IsHullModel(string path)
        {
            string p = path.Replace('\\', '/');
            if (!(p.Contains("/ships/") || p.Contains("/stations/") || p.Contains("/jumpgates/") || p.Contains("/turrets/") || p.Contains("/misc/")))
                return false;
            string name = System.IO.Path.GetFileNameWithoutExtension(p);
            return !name.Contains("_add") && !name.Contains("_lights") && !name.Contains("_lod_");
        }

        /// <summary>A flying ship's model whose own keys play from the start (Materials And Prefabs): under ships/, not a
        /// wreck or an explosion (the freighters' and Supernova wrecks' keys break them apart: their levels play them).</summary>
        public static bool IsAnimatedShipHull(string path)
        {
            string p = path.Replace('\\', '/');
            if (!p.Contains("/ships/")) return false;
            string name = System.IO.Path.GetFileNameWithoutExtension(p);
            return !name.Contains("wreck") && !name.Contains("explosion");
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
        // Modified: 2 = the hangar rooms imported readable (ImportPostprocessor.OnPreprocessModel, Inspect ship's room colliders):
        // a Library imported before kept them unreadable, and StationLevel.AddRoomColliders skipped every one. Bumping this
        // reimports the models only (the texture importer's version is ImportPostprocessor's).
        public override uint GetVersion() => 2;

        void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.StartsWith(ImportSettings.Root + "/Models/")) return;
            var r = Quaternion.Euler(0f, 180f, 0f);
            var ri = Quaternion.Inverse(r);

            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh;
                if (m == null || m.vertexCount == 0) continue;   // empty parts (sn_ship_047_most_wanted_engine_add_part0)
                // Read through MeshData: Unity 7 hands the meshes of an in-process import over unreadable (isReadable is
                // off in OnPreprocessModel), and m.vertices then throws; MeshData reads them in the Editor all the same.
                Vector3[] v, n;
                Vector4[] t;
                using (var data = Mesh.AcquireReadOnlyMeshData(m))
                {
                    var d = data[0];
                    v = Read3(d, VertexAttribute.Position, d.GetVertices);
                    n = Read3(d, VertexAttribute.Normal, d.GetNormals);
                    t = new Vector4[d.HasVertexAttribute(VertexAttribute.Tangent) ? d.vertexCount : 0];
                    if (t.Length > 0)
                    {
                        using var na = new Unity.Collections.NativeArray<Vector4>(t.Length, Unity.Collections.Allocator.Temp);
                        d.GetTangents(na);
                        na.CopyTo(t);
                    }
                }
                for (int i = 0; i < v.Length; i++) v[i] = new Vector3(-v[i].x, v[i].y, -v[i].z);
                m.SetVertices(v);
                for (int i = 0; i < n.Length; i++) n[i] = new Vector3(-n[i].x, n[i].y, -n[i].z);
                if (n.Length > 0) m.SetNormals(n);
                for (int i = 0; i < t.Length; i++) t[i] = new Vector4(-t[i].x, t[i].y, -t[i].z, t[i].w);
                if (t.Length > 0) m.SetTangents(t);
                m.RecalculateBounds();
            }
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            {
                if (tr == root.transform) continue;
                tr.localPosition = r * tr.localPosition;
                tr.localRotation = r * tr.localRotation * ri;
            }
        }

        static Vector3[] Read3(Mesh.MeshData d, VertexAttribute attr, System.Action<Unity.Collections.NativeArray<Vector3>> get)
        {
            if (!d.HasVertexAttribute(attr)) return new Vector3[0];
            using var na = new Unity.Collections.NativeArray<Vector3>(d.vertexCount, Unity.Collections.Allocator.Temp);
            get(na);
            return na.ToArray();
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

        [MenuItem("GoF2/Build/Materials And Prefabs", priority = 240)]
        public static void BuildAll() => Build(false);

        /// <summary>Modified: the per-mesh prefabs of resources.json's meshes that have none yet (a model added to the
        /// project, e.g. the Terran hangar's bot e), and their materials if missing, leaving every existing prefab alone.
        /// Then AssemblyBuilder.BuildMissing for the assemblies without a prefab.</summary>
        [MenuItem("GoF2/Build/Missing Prefabs Only", priority = 242)]
        public static void BuildMissing()
        {
            Build(true);
            AssemblyBuilder.BuildMissing();
        }

        static void Build(bool onlyMissing)
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
                var outPath = R("Prefabs/" + Path.ChangeExtension(mesh.model.Substring("Models/".Length), ".prefab"));
                if (onlyMissing && AssetDatabase.LoadAssetAtPath<GameObject>(outPath) != null) continue;
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
                    // Only "_anim" models (FX, turrets, rotating parts) animate by default; others keep their rest pose. Remake:
                    // a living ship's hull too: the Cronus' (v_ship_037_deep_science) engine pods bob on their own keys and stood
                    // still (players' report); the wrecks and explosions stay with the scripts that play them.
                    anim.play = mesh.model.Contains("_anim") || ImportPostprocessor.IsAnimatedShipHull(mesh.model);
                }

                var info = inst.AddComponent<GoF2Remake.Visuals.ResourceInfo>();
                info.meshId = mesh.id; info.materialId = mesh.materialId; info.originalPath = mesh.source;

                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                PrefabUtility.SaveAsPrefabAsset(inst, outPath);
                Object.DestroyImmediate(inst);
                made++;
            }
            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            if (!onlyMissing) PostProcessing.ApplyGlow();
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
                        // GoF2/Hull: the original's reflection and rim (BumpRimCubeShader), on the ships, stations and objects,
                        // not the station interiors (hangar and bar rooms) or the sky props.
                        bool interior = nrmRel.Contains("/hangars/") || nrmRel.Contains("/bars/") || nrmRel.Contains("/skyboxes/");
                        // The MotionVectors pass stays on: GoF2/Hull borrows URP's LitShader editor, whose ValidateMaterial
                        // doesn't know the shader and turns the pass on whenever the Editor loads the material (Play mode,
                        // batch runs, builds). Turned off here, every load rewrote the .mat files.
                        if (urp && !interior) mat.shader = Shader.Find("GoF2/Hull") ?? mat.shader;
                        if (urp) { mat.EnableKeyword("_METALLICSPECGLOSSMAP"); mat.SetFloat("_Smoothness", 0.8f); }
                        else { mat.EnableKeyword("_METALLICGLOSSMAP"); mat.SetFloat("_GlossMapScale", 0.8f); }
                    }
                }
                mat.SetFloat("_Metallic", 0f);
                // Remake: "bump_alpha_glow" (material 99006, the remodelled Nivelian hangar's launch pads,
                // hangar_nivelian_alt_alpha_add): the room's normal map, blended by the diffuse's alpha like the _alpha
                // layers and glowing like them (they're unlit at _Glow 1: the diffuse as emission).
                if (urp && m.shading == "bump_alpha_glow") MakeLitAlphaGlow(mat, main);
            }
            else mat.SetTexture("_MainTex", main);

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>URP Lit alpha-blended (straight alpha, no depth write, no shadows) with 'emission' as its emission map.</summary>
        static void MakeLitAlphaGlow(Material mat, Texture2D emission)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_BlendModePreserveSpecular", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetShaderPassEnabled("DepthOnly", false);
            mat.SetShaderPassEnabled("ShadowCaster", false);
            mat.SetTexture("_EmissionMap", emission);
            mat.SetColor("_EmissionColor", Color.white);
            mat.EnableKeyword("_EMISSION");
            // URP 17.7's BaseShaderGUI keeps _EMISSION only with this flag: with None its ValidateMaterial (run whenever the
            // Editor loads the material) turned the glow off again.
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeDirectEmission;
        }

        [MenuItem("GoF2/Import/Reapply Import Settings", priority = 300)]
        public static void ReapplyImportSettings()
        {
            foreach (var f in new[] { "Models", "Textures", "Audio" })
                AssetDatabase.ImportAsset(R(f), ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            Debug.Log("GoF2: import settings reapplied.");
        }

        [MenuItem("GoF2/Import/Reimport Models Only", priority = 301)]
        public static void ReimportModels()
        {
            AssetDatabase.ImportAsset(R("Models"), ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            Debug.Log("GoF2: models reimported.");
        }

        [MenuItem("GoF2/Legacy/Flight Test Scene", priority = 500)]
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
