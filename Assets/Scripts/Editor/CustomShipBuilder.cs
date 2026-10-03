// CustomShipBuilder.cs  (Editor only)
// Menu "GoF2/Build Custom Ships": the remake's own ships (Resources/GoF2Data/custom_ships.json, see CustomShips) from
// their source models, the way AssemblyBuilder makes the original ones:
//   - one URP Lit material per entry of 'materials' (Assets/Materials/custom/{assembly}_{mesh}.mat): diffuse, normal map,
//     metallic (R) / smoothness (A) mask, like the game's bump-mapped hulls (PrefabBuilder.CreateMaterial);
//   - Resources/Assembled/custom/ships/{assembly}.prefab: an AssembledObject root, the model as "hull" turned by modelYaw
//     and scaled to modelLength game units nose to tail, and the player's engine glow (playerVariantParts, like
//     *_engine_glow_add) built at every exhaust mount (slotType 3): the Phantom's glow shape (ship_010_terran_engine_glow_add:
//     a 12-segment disc and a flared ring 0.87 r behind it, radius x1.31) on the shared glow sprite (mat_34813,
//     ship_engine_glow.png, centre uv (0.46, 0.947)); no NPC engine parts (like the Kaamo ships 55-63); one LOD level culled
//     at 80000 units like the generic ships;
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
                    var m = MaterialFor(c, mats, r.name);
                    if (m != null) r.sharedMaterials = Enumerable.Repeat(m, Mathf.Max(1, r.sharedMaterials.Length)).ToArray();
                }
                // Scale to modelLength (game units) along the ship's length (Unity z).
                float length = BoundsIn(root.transform, renderers).size.z;
                if (length > 0f) hull.transform.localScale *= c.modelLength * ImportSettings.ModelScale / length;

                var glow = BuildEngineGlow(c);
                if (glow != null)
                {
                    glow.transform.SetParent(root.transform, false);
                    asm.playerVariantParts = new[] { glow };
                }
                else asm.playerVariantParts = new GameObject[0];
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
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                var m = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        static Material MaterialFor(CustomShipData c, List<Material> mats, string rendererName)
        {
            if (c.materials == null || mats.Count == 0) return null;
            string n = rendererName.ToLowerInvariant();
            for (int i = 0; i < c.materials.Count; i++)
                if (!string.IsNullOrEmpty(c.materials[i].mesh) && n.Contains(c.materials[i].mesh.ToLowerInvariant())) return mats[i];
            return mats[0];
        }

        static List<Material> BuildMaterials(CustomShipData c)
        {
            var list = new List<Material>();
            if (c.materials == null) return list;
            Directory.CreateDirectory(MaterialDir);
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            foreach (var e in c.materials)
            {
                string path = $"{MaterialDir}/{c.assembly}_{e.mesh}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool create = mat == null;
                if (create) mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                else mat.shader = shader;
                Texture2D Tex(string rel) => string.IsNullOrEmpty(rel) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(ImportSettings.Root + "/" + rel);
                var diffuse = Tex(e.diffuse);
                if (diffuse == null && !string.IsNullOrEmpty(e.diffuse)) Debug.LogWarning($"GoF2: custom ship {c.index}: Assets/{e.diffuse} not found");
                mat.SetTexture("_BaseMap", diffuse);
                mat.SetTexture("_MainTex", diffuse);
                mat.SetColor("_BaseColor", Color.white);
                var nrm = Tex(e.normal);
                mat.SetTexture("_BumpMap", nrm);
                if (nrm != null) mat.EnableKeyword("_NORMALMAP"); else mat.DisableKeyword("_NORMALMAP");
                var ms = Tex(e.metallicSmoothness);
                mat.SetTexture("_MetallicGlossMap", ms);
                if (ms != null) mat.EnableKeyword("_METALLICSPECGLOSSMAP"); else mat.DisableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetFloat("_Metallic", ms != null ? 1f : 0f);
                mat.SetFloat("_Smoothness", e.smoothness);   // with the mask: its alpha x this
                mat.SetFloat("_SmoothnessTextureChannel", 0f); // metallic alpha
                if (create) AssetDatabase.CreateAsset(mat, path); else EditorUtility.SetDirty(mat);
                list.Add(mat);
            }
            return list;
        }

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
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    var m = v * r.transform.localToWorldMatrix;
                    foreach (var p in mf.sharedMesh.vertices.Where((_, i) => i % 7 == 0))
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
