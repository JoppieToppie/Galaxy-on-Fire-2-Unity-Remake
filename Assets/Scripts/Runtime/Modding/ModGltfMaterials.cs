// ModGltfMaterials.cs
// glTFast's material generator for mods' models: every glTF material becomes a copy of the game's URP Lit templates
// (ModMaterials.FromGltf) instead of glTFast's own shader graphs, which builds wouldn't carry. 'images' gives the textures
// the mods' loader made for the GLB's images (ModGltf: the texture index, its role, and for a metallic-roughness map the
// material's metallic / roughness factors; null = glTFast's own).

using GLTFast;
using GLTFast.Logging;
using GLTFast.Materials;
using GLTFast.Schema;

namespace GoF2Remake.Modding
{
    public class ModGltfMaterials : IMaterialGenerator
    {
        readonly System.Func<int, ModGlbImages.Role, float, float, UnityEngine.Texture2D> images;
        readonly System.Collections.Generic.Dictionary<string, UnityEngine.Vector4> gof3;   // by material name (ModGlbImages)
        readonly System.Func<string, UnityEngine.Texture2D> gof3Cube;   // a material's GoF3 environment strip, by its name
        readonly System.Collections.Generic.Dictionary<string, ModGlbImages.Glow> glow;   // glow layers, by material name

        public ModGltfMaterials(System.Func<int, ModGlbImages.Role, float, float, UnityEngine.Texture2D> images = null,
            System.Collections.Generic.Dictionary<string, UnityEngine.Vector4> gof3 = null,
            System.Func<string, UnityEngine.Texture2D> gof3Cube = null,
            System.Collections.Generic.Dictionary<string, ModGlbImages.Glow> glow = null)
        { this.images = images; this.gof3 = gof3; this.gof3Cube = gof3Cube; this.glow = glow; }

        public UnityEngine.Material GetDefaultMaterial(bool pointsSupport = false) => ModMaterials.Default();

        public UnityEngine.Material GenerateMaterial(MaterialBase gltfMaterial, IGltfReadable gltf, bool pointsSupport = false) =>
            glow != null && gltfMaterial.name != null && glow.TryGetValue(gltfMaterial.name, out var g)
                ? ModMaterials.GlowFromGltf(gltfMaterial, gltf, images, g) ?? ModMaterials.Default()
                : ModMaterials.FromGltf(gltfMaterial, gltf, images,
                gof3 != null && gltfMaterial.name != null && gof3.TryGetValue(gltfMaterial.name, out var p) ? p : (UnityEngine.Vector4?)null,
                gof3Cube != null && gltfMaterial.name != null ? gof3Cube(gltfMaterial.name) : null);

        public void SetLogger(ICodeLogger logger) { }
    }
}
