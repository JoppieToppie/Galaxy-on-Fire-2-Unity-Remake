// ModGltf.cs
// Loads a mod's glTF / GLB model with glTFast (ships, stations, rooms, weapon fx), its embedded images made by the mods' own
// texture loader instead of glTFast's (ModGlbImages: glTFast keeps each model's images uncompressed, one copy per model):
// the GLB's images are taken out on a worker thread, each different one loaded once per mod, compressed and cached
// (ModMaterials.PreloadImage, its role's conversion done on the CPU: metallic-roughness to URP's metallic / smoothness,
// a normal map's Android layout), then glTFast reads the rest and ModGltfMaterials puts them on the materials.

using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace GoF2Remake.Modding
{
    public static class ModGltf
    {
        /// <summary>The model read by glTFast (null: glTFast couldn't read it). The import owns the meshes and materials:
        /// keep it until they go, then Dispose it.</summary>
        public static async Task<GLTFast.GltfImport> Load(ModInfo mod, byte[] bytes, GLTFast.IDeferAgent agent)
        {
            ModGlbImages.Result stripped = null;
            try { stripped = await Task.Run(() => ModGlbImages.Strip(bytes)); }
            catch (System.Exception e) { Debug.LogWarning($"Mods: {mod.Id}: a model's images are left to glTFast ({e.Message})"); }
            System.Func<int, ModGlbImages.Role, float, float, Texture2D> images = null;
            if (stripped != null)
            {
                bytes = stripped.glb;
                var tasks = new List<Task>();
                foreach (var img in stripped.images.Values)
                {
                    foreach (var role in new[] { ModGlbImages.Role.Color, ModGlbImages.Role.Normal })
                        if ((img.roles & role) != 0) tasks.Add(ModMaterials.PreloadImage(mod, img.hash, img.ext, img.bytes, role));
                    // A metallic-roughness map once per factor pair its materials give it (baked into the conversion).
                    foreach (var f in img.mrFactors)
                        tasks.Add(ModMaterials.PreloadImage(mod, img.hash, img.ext, img.bytes, ModGlbImages.Role.MetallicRoughness, f.metal, f.rough));
                }
                await Task.WhenAll(tasks);
                foreach (var img in stripped.images.Values) img.bytes = null;
                images = (texture, role, metal, rough) =>
                    stripped.textureImage.TryGetValue(texture, out int i) && stripped.images.TryGetValue(i, out var img)
                        ? ModMaterials.Image(mod, img.hash, role, metal, rough) : null;
            }
            // glTFast's own messages collected (without a logger it says nothing): why a model can't be read reaches the
            // mod's warnings (#134: a compression extension without its package looked like an empty model).
            var log = new GLTFast.Logging.CollectingLogger();
            var import = new GLTFast.GltfImport(null, agent, new ModGltfMaterials(images, stripped?.gof3), log);
            var settings = new GLTFast.ImportSettings { GenerateMipMaps = true, AnisotropicFilterLevel = 8 };
            if (await import.Load(bytes, null, settings)) return import;
            import.Dispose();
            if (log.Items != null)
                foreach (var item in log.Items)
                {
                    if (item.Type != LogType.Error && item.Type != LogType.Exception) continue;
                    string message = $"glTFast: {item}";
                    if (!mod.Warnings.Contains(message)) mod.Warnings.Add(message);
                    Debug.LogWarning($"Mods: {mod.Id}: {message}");
                }
            return null;
        }

        /// <summary>The model's scene under 'parent': its default scene, else its first (#134: glTFast's main scene is
        /// nothing when the file sets no "scene", which the glTF viewers ignore by showing the first; the model came out
        /// empty with no warning). False when the file has no scene or it can't be built.</summary>
        public static Task<bool> InstantiateMain(GLTFast.GltfImport import, Transform parent)
        {
            if (import == null || import.SceneCount <= 0) return Task.FromResult(false);
            return import.InstantiateSceneAsync(parent, import.DefaultSceneIndex ?? 0);
        }

        /// <summary>The instantiated model shows nothing: no mesh renderer under 'root' (a file of empty nodes, a scene
        /// without the meshes).</summary>
        public static bool HasNoMeshes(GameObject root) =>
            root.GetComponentsInChildren<MeshRenderer>(true).Length == 0 && root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0;

        /// <summary>A model's glTF animations loop for good, like the game's own (PartAnimation): glTFast puts them on a legacy
        /// Animation with the first clip, at the default wrap mode, which plays it once and stops (a mod ship's turning
        /// radar or bobbing pods stood still). Its first clip plays from the start whenever the model is shown.</summary>
        public static void LoopAnimations(GameObject root)
        {
            foreach (var a in root.GetComponentsInChildren<Animation>(true))
            {
                a.wrapMode = WrapMode.Loop;
                if (a.clip != null) a.clip.wrapMode = WrapMode.Loop;
                a.playAutomatically = true;
                a.cullingType = AnimationCullingType.AlwaysAnimate;
            }
        }
    }
}
