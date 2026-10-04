// CustomShipBuilder.cs  (Editor only)
// Menu "GoF2/Build Custom Ships": the remake's own ships (Resources/GoF2Data/custom_ships.json, see CustomShips) from
// their source models, the way AssemblyBuilder makes the original ones:
//   - one URP Lit material per entry of 'materials' (Assets/Materials/custom/{assembly}_{mesh or submesh}.mat): diffuse,
//     normal map, metallic (R) / smoothness (A) mask, like the game's bump-mapped hulls (PrefabBuilder.CreateMaterial),
//     optionally an emission map and alpha clipping (decals); an entry with 'submesh' >= 0 goes on that submesh only
//     (one FBX mesh with several materials);
//   - Resources/Assembled/custom/ships/{assembly}.prefab: an AssembledObject root, the model as "hull" turned by modelYaw
//     and scaled to modelLength game units nose to tail, and the player's engine glow (playerVariantParts, like
//     *_engine_glow_add) built at every exhaust mount (slotType 3): the Phantom's glow shape (ship_010_terran_engine_glow_add:
//     a 12-segment disc and a flared ring 0.87 r behind it, radius x1.31) on the shared glow sprite (mat_34813,
//     ship_engine_glow.png, centre uv (0.46, 0.947)); a ship with no exhaust mounts has no flame at all (ShipExhaust makes
//     its particles from the same mounts); 'throttleGlow' adds a glow on the hull itself instead (BuildThrottleGlow: the
//     triangles under the lit part of a mask, additive, its strength set by ThrottleGlow from the throttle), also a
//     player engine part; no NPC engine parts (like the Kaamo ships 55-63); one LOD level culled at 80000 units like the
//     generic ships;
//   - the shop icon Resources/GoF2Icons/ship_NNN.png (ItemIconBuilder.WriteShipIcon: the ship plate's frame + the model
//     rendered nose to the lower left like the original icons);
// then runs Build Text Icons (the dialogue's inline ship icon) and Build Hangar Heights (the parking lifts).
// The import rules for Assets/Models/custom/ are in AssetImport.cs (file units, no 180 deg turn, no materials).

using System.Collections.Generic;
using System.IO;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace GoF2Remake.EditorTools
{
    public static class CustomShipBuilder
    {
        const string OutRoot = ImportSettings.Root + "/Resources/" + AssembledObject.ResourcesFolder + "/custom/ships";
        const string MaterialDir = ImportSettings.Root + "/Materials/custom";
        const string MeshDir = ImportSettings.Root + "/Prefabs/custom";
        const string GlowMaterial = ImportSettings.Root + "/Materials/mat_34813_ship_engine_glow.mat";
        const float CullDistance = 80000f;   // game units, the generic ships' last visible distance
        const float ReferenceFov = 60f;      // as AssemblyBuilder

        [MenuItem("GoF2/Build Custom Ships", priority = 3)]
        public static void BuildAll() => Build(true);

        /// <summary>The prefabs and icons; 'tables' also rebuilds the text icons and the hangar heights.</summary>
        public static void Build(bool tables)
        {
            var ships = Database.ReadCustomShips();
            if (ships.Count == 0) { Debug.LogWarning("GoF2: no custom ships (Resources/GoF2Data/custom_ships.json)"); return; }
            int made = 0;
            try
            {
                foreach (var c in ships)
                {
                    EditorUtility.DisplayProgressBar("GoF2", "Custom ship " + c.name, (float)made / ships.Count);
                    var prefab = BuildPrefab(c);
                    if (prefab == null) continue;
                    var icon = RenderIcon(prefab, 178, 86);
                    if (icon != null) ItemIconBuilder.WriteShipIcon(c.index, icon, 178, 86);
                    made++;
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            AssetDatabase.SaveAssets();
            Debug.Log($"GoF2: {made}/{ships.Count} custom ship(s) built in {OutRoot}.");
            if (made > 0 && tables)
            {
                TextIconsBuilder.Build();
                HangarHeightsBuilder.Build();
            }
        }

        // ---- prefab -------------------------------------------------------------------------------------------

        static GameObject BuildPrefab(CustomShipData c)
        {
            if (string.IsNullOrEmpty(c.assembly) || !c.assembly.StartsWith($"ship_{c.index:000}_"))
            {
                Debug.LogError($"GoF2: custom ship {c.index}: 'assembly' must start with ship_{c.index:000}_ (Database.ShipAssembly)");
                return null;
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ImportSettings.Root + "/" + c.model);
            if (model == null) { Debug.LogError($"GoF2: custom ship {c.index}: model Assets/{c.model} not found"); return null; }
            var mats = BuildMaterials(c);

            var root = new GameObject(c.assembly);
            try
            {
                var asm = root.AddComponent<AssembledObject>();
                asm.origin = "custom_ships.json";
                asm.lodDistancesGameUnits = new float[0];
                asm.lastVisibleDistanceGameUnits = CullDistance;

                var hull = (GameObject)PrefabUtility.InstantiatePrefab(model);
                hull.name = "hull";
                hull.transform.SetParent(root.transform, false);
                hull.transform.localPosition = Vector3.zero;
                hull.transform.localRotation = Quaternion.Euler(0f, c.modelYaw, 0f);
                var renderers = hull.GetComponentsInChildren<Renderer>(true);
                foreach (var r in renderers)
                {
                    var mesh = MeshOf(r);
                    int count = Mathf.Max(1, mesh != null ? mesh.subMeshCount : r.sharedMaterials.Length);
                    var assigned = new Material[count];
                    for (int i = 0; i < count; i++) assigned[i] = MaterialFor(c, mats, r.name, i);
                    if (assigned.Any(m => m != null)) r.sharedMaterials = assigned;
                    if (mesh != null && mesh.subMeshCount > 1)
                        Debug.Log($"GoF2: custom ship {c.index}: {r.name} submeshes (triangles): " +
                                  string.Join(", ", Enumerable.Range(0, mesh.subMeshCount).Select(i => $"{i} ({mesh.GetSubMesh(i).indexCount / 3})")));
                }
                // Scale to modelLength (game units) along the ship's length (Unity z).
                float length = BoundsIn(root.transform, renderers).size.z;
                if (length > 0f) hull.transform.localScale *= c.modelLength * ImportSettings.ModelScale / length;

                // The player's engine parts: the exhaust glow (its first part, ShipExhaust watches it). The throttle glow
                // stays lit when the game switches the engines off (mining, object docking, cutscenes): it is not one of
                // them, an empty "engine_state" part is, and while that is off the glow sits at its idle level.
                var parts = new List<GameObject>();
                var glow = BuildEngineGlow(c);
                if (glow != null) parts.Add(glow);
                // 'throttleGlow' and any 'extraGlows' (e.g. the Space Banshee's engines and wing tips), one engine_state for all.
                var glowSpecs = new List<CustomThrottleGlow> { c.throttleGlow };
                if (c.extraGlows != null) glowSpecs.AddRange(c.extraGlows);
                GameObject engineState = null;
                for (int gi = 0; gi < glowSpecs.Count; gi++)
                {
                    var throttleGlow = BuildThrottleGlow(c, glowSpecs[gi], gi == 0 ? "" : "_" + (gi + 1), root.transform, renderers);
                    if (throttleGlow == null) continue;
                    throttleGlow.transform.SetParent(root.transform, false);
                    if (engineState == null) { engineState = new GameObject("engine_state"); parts.Add(engineState); }
                    throttleGlow.GetComponent<ThrottleGlow>().engineState = engineState;
                }
                foreach (var p in parts) p.transform.SetParent(root.transform, false);
                asm.playerVariantParts = parts.ToArray();
                asm.npcVariantParts = new GameObject[0];
                asm.conditionalParts = new GameObject[0];
                asm.conditions = new string[0];

                // One level, culled where the generic ships are (AssemblyBuilder.SetupLods).
                var group = root.AddComponent<LODGroup>();
                group.SetLODs(new[] { new LOD(0.5f, root.GetComponentsInChildren<Renderer>(true)) });
                group.RecalculateBounds();
                float tanHalf = Mathf.Tan(ReferenceFov * 0.5f * Mathf.Deg2Rad);
                float h = Mathf.Clamp01(group.size / (2f * CullDistance * ImportSettings.ModelScale * tanHalf));
                group.SetLODs(new[] { new LOD(h, root.GetComponentsInChildren<Renderer>(true)) });

                Directory.CreateDirectory(OutRoot);
                return PrefabUtility.SaveAsPrefabAsset(root, $"{OutRoot}/{c.assembly}.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static Bounds BoundsIn(Transform root, Renderer[] renderers)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var r in renderers)
            {
                var mesh = MeshOf(r);
                if (mesh == null) continue;
                var mb = mesh.bounds;
                var m = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        /// <summary>The renderer's mesh (a MeshFilter's, or a skinned mesh's).</summary>
        static Mesh MeshOf(Renderer r) => r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;

        /// <summary>The entry for submesh 'submesh' of the renderer 'rendererName': one naming that submesh first, else
        /// the first for every submesh (submesh -1); its 'mesh' must be part of the renderer's name (empty = any).
        /// Otherwise the first material.</summary>
        static Material MaterialFor(CustomShipData c, List<Material> mats, string rendererName, int submesh)
        {
            if (c.materials == null || mats.Count == 0) return null;
            string n = rendererName.ToLowerInvariant();
            int any = -1;
            for (int i = 0; i < c.materials.Count; i++)
            {
                var e = c.materials[i];
                if (!string.IsNullOrEmpty(e.mesh) && !n.Contains(e.mesh.ToLowerInvariant())) continue;
                if (e.submesh == submesh) return mats[i];
                if (e.submesh < 0 && any < 0) any = i;
            }
            return mats[any >= 0 ? any : 0];
        }

        static List<Material> BuildMaterials(CustomShipData c)
        {
            var list = new List<Material>();
            if (c.materials == null) return list;
            Directory.CreateDirectory(MaterialDir);
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            foreach (var e in c.materials)
            {
                string part = !string.IsNullOrEmpty(e.mesh) ? e.mesh : e.submesh >= 0 ? $"submesh{e.submesh}" : "hull";
                string path = $"{MaterialDir}/{c.assembly}_{part}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool create = mat == null;
                if (create) mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                else mat.shader = shader;
                Texture2D Tex(string rel) => string.IsNullOrEmpty(rel) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(ImportSettings.Root + "/" + rel);
                var diffuse = Tex(e.diffuse);
                if (diffuse == null && !string.IsNullOrEmpty(e.diffuse)) Debug.LogWarning($"GoF2: custom ship {c.index}: Assets/{e.diffuse} not found");
                mat.SetTexture("_BaseMap", diffuse);
                mat.SetTexture("_MainTex", diffuse);
                mat.SetColor("_BaseColor", Rgb(e.color, Color.white));
                var nrm = Tex(e.normal);
                mat.SetTexture("_BumpMap", nrm);
                mat.SetFloat("_BumpScale", e.normalScale > 0f ? e.normalScale : 1f);   // 0 / missing = 1
                if (nrm != null) mat.EnableKeyword("_NORMALMAP"); else mat.DisableKeyword("_NORMALMAP");
                var ms = Tex(e.metallicSmoothness);
                mat.SetTexture("_MetallicGlossMap", ms);
                if (ms != null) mat.EnableKeyword("_METALLICSPECGLOSSMAP"); else mat.DisableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetFloat("_Metallic", ms != null ? 1f : e.metallic >= 0f ? e.metallic : 0f);
                mat.SetFloat("_Smoothness", e.smoothness);   // with the mask: its alpha x this
                mat.SetFloat("_SmoothnessTextureChannel", 0f); // metallic alpha
                // Emission: a map (lights, windows) and / or a colour, x emissionIntensity.
                var em = Tex(e.emission);
                if (em == null && !string.IsNullOrEmpty(e.emission)) Debug.LogWarning($"GoF2: custom ship {c.index}: Assets/{e.emission} not found");
                mat.SetTexture("_EmissionMap", em);
                bool emColor = e.emissionColor != null && e.emissionColor.Length >= 3;
                if (em != null || emColor)
                {
                    var ec = Rgb(e.emissionColor, Color.white) * e.emissionIntensity;
                    ec.a = 1f;
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", ec);
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }
                else
                {
                    mat.DisableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", Color.black);
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                }
                // Alpha clipping (decal sheets: the diffuse's alpha cuts them out).
                bool clip = e.alphaClip > 0f;
                mat.SetFloat("_AlphaClip", clip ? 1f : 0f);
                mat.SetFloat("_Cutoff", clip ? e.alphaClip : 0.5f);
                if (clip) mat.EnableKeyword("_ALPHATEST_ON"); else mat.DisableKeyword("_ALPHATEST_ON");
                mat.SetOverrideTag("RenderType", clip ? "TransparentCutout" : "Opaque");
                mat.renderQueue = clip ? (int)RenderQueue.AlphaTest : -1;
                // See-through (glass): URP Lit transparent, premultiplied alpha so the specular highlights stay at full strength.
                bool glass = e.opacity > 0f && e.opacity < 1f;
                mat.SetFloat("_Surface", glass ? 1f : 0f);
                mat.SetFloat("_Blend", glass ? 1f : 0f);   // 1 = premultiply
                mat.SetFloat("_SrcBlend", glass ? (float)BlendMode.One : (float)BlendMode.One);
                mat.SetFloat("_DstBlend", glass ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
                mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                mat.SetFloat("_DstBlendAlpha", glass ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
                mat.SetFloat("_ZWrite", glass ? 0f : 1f);
                if (glass)
                {
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.SetOverrideTag("RenderType", "Transparent");
                    mat.renderQueue = (int)RenderQueue.Transparent;
                    var bc = mat.GetColor("_BaseColor"); bc.a = e.opacity; mat.SetColor("_BaseColor", bc);
                    mat.SetShaderPassEnabled("DepthOnly", false);
                    mat.SetShaderPassEnabled("ShadowCaster", false);
                }
                else
                {
                    mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.SetShaderPassEnabled("DepthOnly", true);
                    mat.SetShaderPassEnabled("ShadowCaster", true);
                }
                // URP detail maps (brushed metal etc.), tiled over the UVs.
                var dAlb = Tex(e.detailAlbedo);
                var dNrm = Tex(e.detailNormal);
                mat.SetTexture("_DetailAlbedoMap", dAlb);
                mat.SetTexture("_DetailNormalMap", dNrm);
                mat.SetFloat("_DetailAlbedoMapScale", 1f);
                mat.SetFloat("_DetailNormalMapScale", e.detailNormalScale > 0f ? e.detailNormalScale : 1f);
                var tiling = new Vector2(e.detailTiling > 0f ? e.detailTiling : 1f, e.detailTiling > 0f ? e.detailTiling : 1f);
                mat.SetTextureScale("_DetailAlbedoMap", tiling);
                mat.SetTextureScale("_DetailNormalMap", tiling);
                if (dAlb != null || dNrm != null) mat.EnableKeyword("_DETAIL_MULX2"); else mat.DisableKeyword("_DETAIL_MULX2");
                mat.DisableKeyword("_DETAIL_SCALED");
                if (create) AssetDatabase.CreateAsset(mat, path); else EditorUtility.SetDirty(mat);
                list.Add(mat);
            }
            return list;
        }

        /// <summary>An RGB triple from custom_ships.json (JsonUtility leaves a missing array empty, not null).</summary>
        static Color Rgb(float[] v, Color fallback) => v != null && v.Length >= 3 ? new Color(v[0], v[1], v[2], 1f) : fallback;

        // ---- engine glow ----------------------------------------------------------------------------------------

        static GameObject BuildEngineGlow(CustomShipData c)
        {
            var exhausts = c.mounts?.Where(m => m.slotType == 3).ToList();
            if (exhausts == null || exhausts.Count == 0) return null;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(GlowMaterial);
            if (mat == null) { Debug.LogWarning($"GoF2: {GlowMaterial} missing (Build Materials And Prefabs): no engine glow"); return null; }

            const int Segments = 12;
            var uvCentre = new Vector2(0.46f, 0.947f);
            const float UvDisc = 0.0254f, UvRing = 0.048f;
            float r = c.engineGlowRadius * ImportSettings.ModelScale, ringR = r * 1.31f, back = r * 0.87f;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            foreach (var m in exhausts)
            {
                var o = WeaponSystem.MountToLocal(m);
                int centre = verts.Count;
                verts.Add(o); uvs.Add(uvCentre);
                int disc = verts.Count;
                for (int i = 0; i < Segments; i++)
                {
                    float a = i * Mathf.PI * 2f / Segments;
                    var d = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                    verts.Add(o + new Vector3(d.x * r, d.y * r, 0f)); uvs.Add(uvCentre + d * UvDisc);
                }
                int ring = verts.Count;
                for (int i = 0; i < Segments; i++)
                {
                    float a = i * Mathf.PI * 2f / Segments;
                    var d = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                    verts.Add(o + new Vector3(d.x * ringR, d.y * ringR, -back)); uvs.Add(uvCentre + d * UvRing);
                }
                for (int i = 0; i < Segments; i++)
                {
                    int j = (i + 1) % Segments;
                    tris.AddRange(new[] { centre, disc + i, disc + j });                                       // the core (two-sided shader)
                    tris.AddRange(new[] { disc + i, ring + i, ring + j, disc + i, ring + j, disc + j });       // the flare
                }
            }
            var mesh = new Mesh { name = c.assembly + "_engine_glow" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            Directory.CreateDirectory(MeshDir);
            string path = $"{MeshDir}/{mesh.name}.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old != null) { EditorUtility.CopySerialized(mesh, old); Object.DestroyImmediate(mesh); mesh = old; EditorUtility.SetDirty(old); }
            else AssetDatabase.CreateAsset(mesh, path);

            var go = new GameObject("engine_glow");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        // ---- throttle glow --------------------------------------------------------------------------------------

        const float GlowMaskThreshold = 0.08f;   // a triangle joins the glow where its mask is brighter than this

        /// <summary>'throttleGlow': the hull triangles (of its submesh) whose corners or centre sample the mask above
        /// GlowMaskThreshold, in the root's space (after the hull's scaling), pushed 'offset' game units out along their
        /// normals so they don't z-fight the hull; GoF2/Additive with the mask as its texture, ThrottleGlow sets the
        /// strength. Null without a mask.</summary>
        static GameObject BuildThrottleGlow(CustomShipData c, CustomThrottleGlow tg, string suffix, Transform root, Renderer[] renderers)
        {
            // JsonUtility always makes a throttleGlow: no mask = none
            if (tg == null || string.IsNullOrEmpty(tg.mask)) return null;
            string maskPath = ImportSettings.Root + "/" + tg.mask;
            var maskTex = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
            if (maskTex == null || !File.Exists(maskPath)) { Debug.LogWarning($"GoF2: custom ship {c.index}: throttle glow mask Assets/{tg.mask} not found"); return null; }
            var shader = Shader.Find("GoF2/Additive");
            if (shader == null) { Debug.LogWarning("GoF2: shader GoF2/Additive missing: no throttle glow"); return null; }

            // The mask's pixels from the file (the imported texture isn't readable).
            var pixels = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            try
            {
                if (!pixels.LoadImage(File.ReadAllBytes(maskPath))) { Debug.LogWarning($"GoF2: custom ship {c.index}: can't read Assets/{tg.mask}"); return null; }
                float Lit(Vector2 uv) { var p = pixels.GetPixelBilinear(uv.x, uv.y); return Mathf.Max(p.r, Mathf.Max(p.g, p.b)); }
                float offset = tg.offset * ImportSettings.ModelScale;
                foreach (var r in renderers)
                {
                    if (!string.IsNullOrEmpty(tg.mesh) && !r.name.ToLowerInvariant().Contains(tg.mesh.ToLowerInvariant())) continue;
                    var mesh = MeshOf(r);
                    if (mesh == null) continue;
                    var mv = mesh.vertices;
                    var mn = mesh.normals;
                    var mu = mesh.uv;
                    if (mu == null || mu.Length != mv.Length) continue;
                    bool hasNormals = mn != null && mn.Length == mv.Length;
                    var m = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        if (tg.submesh >= 0 && s != tg.submesh) continue;
                        var t = mesh.GetTriangles(s);
                        for (int i = 0; i + 2 < t.Length; i += 3)
                        {
                            Vector2 a = mu[t[i]], b = mu[t[i + 1]], d = mu[t[i + 2]];
                            if (Mathf.Max(Mathf.Max(Lit(a), Lit(b)), Mathf.Max(Lit(d), Lit((a + b + d) / 3f))) <= GlowMaskThreshold) continue;
                            for (int k = 0; k < 3; k++)
                            {
                                int v = t[i + k];
                                var n = hasNormals ? m.MultiplyVector(mn[v]).normalized : Vector3.zero;
                                verts.Add(m.MultiplyPoint3x4(mv[v]) + n * offset);
                                normals.Add(n);
                                uvs.Add(mu[v]);
                            }
                        }
                    }
                }
            }
            finally { Object.DestroyImmediate(pixels); }
            if (verts.Count == 0) { Debug.LogWarning($"GoF2: custom ship {c.index}: no hull triangle under the throttle glow mask (submesh {tg.submesh})"); return null; }

            var glowMesh = new Mesh { name = c.assembly + "_throttle_glow" + suffix };
            if (verts.Count > 65535) glowMesh.indexFormat = IndexFormat.UInt32;
            glowMesh.SetVertices(verts);
            glowMesh.SetNormals(normals);
            glowMesh.SetUVs(0, uvs);
            glowMesh.SetTriangles(Enumerable.Range(0, verts.Count).ToArray(), 0);
            glowMesh.RecalculateBounds();
            Directory.CreateDirectory(MeshDir);
            string meshPath = $"{MeshDir}/{glowMesh.name}.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (old != null) { EditorUtility.CopySerialized(glowMesh, old); Object.DestroyImmediate(glowMesh); glowMesh = old; EditorUtility.SetDirty(old); }
            else AssetDatabase.CreateAsset(glowMesh, meshPath);

            var tint = tg.color != null && tg.color.Length >= 3 ? new Color(tg.color[0], tg.color[1], tg.color[2], 1f) : Color.white;
            Directory.CreateDirectory(MaterialDir);
            string matPath = $"{MaterialDir}/{c.assembly}_throttle_glow{suffix}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            bool create = mat == null;
            if (create) mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(matPath) };
            else mat.shader = shader;
            mat.SetTexture("_MainTex", maskTex);
            mat.SetColor("_Color", tint);
            mat.SetFloat("_Glow", tg.idle);
            mat.SetFloat("_UseVertexColor", 0f);
            mat.DisableKeyword("_USEVERTEXCOLOR_ON");
            if (create) AssetDatabase.CreateAsset(mat, matPath); else EditorUtility.SetDirty(mat);

            var go = new GameObject("throttle_glow" + suffix);
            go.AddComponent<MeshFilter>().sharedMesh = glowMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var g = go.AddComponent<ThrottleGlow>();
            g.color = tint;
            g.idle = tg.idle;
            g.full = tg.full;
            g.boost = tg.boost;
            if (tg.trailWidth > 0f)
            {
                // The trails start at the glow's rear end on each side (the rearmost 3 % of its length, averaged per side).
                float minZ = verts.Min(v => v.z), maxZ = verts.Max(v => v.z);
                float cut = minZ + (maxZ - minZ) * 0.03f;
                var rear = verts.Where(v => v.z <= cut).ToList();
                var points = new List<Vector3>();
                foreach (var side in new[] { rear.Where(v => v.x < 0f).ToList(), rear.Where(v => v.x >= 0f).ToList() })
                    if (side.Count > 0) points.Add(new Vector3(side.Average(v => v.x), side.Average(v => v.y), minZ));
                g.trailPoints = points.ToArray();
                g.trailWidth = tg.trailWidth * ImportSettings.ModelScale;
                g.trailTime = tg.trailTime > 0f ? tg.trailTime : 0.6f;
                g.trailBrightness = tg.trailBrightness > 0f ? tg.trailBrightness : 0.3f;
            }
            return go;
        }

        // ---- shop icon ------------------------------------------------------------------------------------------

        /// <summary>The model in a preview scene, orthographic, nose to the lower left and seen from above its left side
        /// like the original shop icons; supersampled 4x, transparent background. Bottom-up rows.</summary>
        static Color32[] RenderIcon(GameObject prefab, int w, int h)
        {
            const int SS = 4;
            var scene = EditorSceneManager.NewPreviewScene();
            var rt = new RenderTexture(w * SS, h * SS, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                var go = (GameObject)Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(go, scene);
                go.GetComponent<AssembledObject>()?.SetExhaust(false, true);
                var lod = go.GetComponent<LODGroup>();
                if (lod != null) lod.enabled = false;
                var renderers = go.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
                if (renderers.Length == 0) return null;
                var b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);

                void AddLight(Vector3 dir, float intensity)
                {
                    var l = new GameObject("Light").AddComponent<Light>();
                    SceneManager.MoveGameObjectToScene(l.gameObject, scene);
                    l.type = LightType.Directional;
                    l.intensity = intensity;
                    l.transform.rotation = Quaternion.LookRotation(dir);
                }
                AddLight(new Vector3(0.5f, -0.8f, -0.4f), 1.7f);
                AddLight(new Vector3(-0.6f, 0.2f, 0.6f), 0.5f);

                var camGo = new GameObject("IconCamera");
                SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene;
                cam.enabled = false;
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.targetTexture = rt;
                float radius = b.extents.magnitude;
                var dir = new Vector3(-1f, 0.85f, 0.35f).normalized;   // camera on the ship's left, above, ahead: nose lower left
                cam.transform.position = b.center + dir * radius * 3f;
                cam.transform.LookAt(b.center);
                // Fit the bounds' corners in view (camera space), centred.
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                var v = cam.transform.worldToLocalMatrix;
                foreach (var r in renderers)
                {
                    var mesh = MeshOf(r);
                    if (mesh == null) continue;
                    var m = v * r.transform.localToWorldMatrix;
                    foreach (var p in mesh.vertices.Where((_, i) => i % 7 == 0))
                    {
                        var q = m.MultiplyPoint3x4(p);
                        minX = Mathf.Min(minX, q.x); maxX = Mathf.Max(maxX, q.x); minY = Mathf.Min(minY, q.y); maxY = Mathf.Max(maxY, q.y);
                    }
                }
                cam.transform.position += cam.transform.right * (minX + maxX) * 0.5f + cam.transform.up * (minY + maxY) * 0.5f;
                float aspect = (float)w / h;
                cam.orthographicSize = Mathf.Max((maxY - minY) * 0.5f, (maxX - minX) * 0.5f / aspect) * 1.06f;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = radius * 8f;

                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
                else cam.Render();

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                var src = tex.GetPixels32();
                Object.DestroyImmediate(tex);

                // Box-filter down (premultiplied, so the edges don't darken).
                var dst = new Color32[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float r = 0, g = 0, bl = 0, a = 0;
                        for (int sy = 0; sy < SS; sy++)
                            for (int sx = 0; sx < SS; sx++)
                            {
                                var s = src[(y * SS + sy) * rt.width + x * SS + sx];
                                float sa = s.a / 255f;
                                r += s.r * sa; g += s.g * sa; bl += s.b * sa; a += sa;
                            }
                        dst[y * w + x] = a <= 0f ? new Color32(0, 0, 0, 0)
                            : new Color32((byte)(r / a), (byte)(g / a), (byte)(bl / a), (byte)Mathf.RoundToInt(a / (SS * SS) * 255f));
                    }
                return dst;
            }
            finally
            {
                rt.Release();
                Object.DestroyImmediate(rt);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
