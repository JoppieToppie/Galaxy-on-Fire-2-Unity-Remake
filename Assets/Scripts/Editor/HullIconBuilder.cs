// HullIconBuilder.cs  (Editor only)
// Menu "GoF2/Build/Hull Icons": shop-style icons for the hulls the original has none for, the Debug page's Ships tab's
// "Not normally flyable" (World.PlayerHull: the Vossk freighter 13, the Terran battleship 14, the race freighters, the Terran
// carrier, the Vossk battleship, the Valkyrie battlestation and the Void mother ship; 13-15's shop images are only a "?").
// Each assembled model is rendered in a preview scene like the original's ship icons look (from its left side, above and
// a little ahead, the nose toward the bottom left, lit warm from the front), 4x supersampled, its alpha from a render on
// black and one on white, over the blue striped ship frame 1276 (ItemIconBuilder's frame, Reference/research/item_icons.json).
// Output: Resources/GoF2Icons/hull_<key>.png (180 x 88 like the others), read by ItemInfo.HullIcon.

using System.Collections.Generic;
using System.IO;
using GoF2Remake.Data;
using GoF2Remake.Visuals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GoF2Remake.EditorTools
{
    public static class HullIconBuilder
    {
        const int IconW = 178, IconH = 86, PadW = 180, PadH = 88, Super = 4;
        const string AtlasDir = ImportSettings.Root + "/Textures/textures";
        /// <summary>The camera's direction from the hull (Unity, the model facing +Z): its left side, above, a little ahead,
        /// so the nose points left.</summary>
        static readonly Vector3 ViewFrom = new Vector3(-1f, 0.55f, 0.3f);
        /// <summary>Turns the picture so the nose dips toward the bottom left, like every original ship icon.</summary>
        const float Roll = -15f;

        [System.Serializable] class Entry { public string kind, name, atlas; public int imageId; public int[] rect; }
        [System.Serializable] class IconFile { public List<Entry> entries; }

        public static string IconPath(string key) => $"{ItemIconBuilder.OutDir}/hull_{key}.png";

        [MenuItem("GoF2/Build/Hull Icons", priority = 222)]
        public static void Build()
        {
            var db = Database.Load();
            var hulls = World.PlayerHull.All(db).FindAll(h => !h.playerShip);
            var frame = LoadFrame();
            var scene = EditorSceneManager.NewPreviewScene();
            var rt = new RenderTexture(IconW * Super, IconH * Super, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var written = new List<string>();
            try
            {
                var camGo = new GameObject("HullIconCamera");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.orthographic = true;
                cam.aspect = (float)IconW / IconH;
                cam.targetTexture = rt;
                cam.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                // A warm key light from the front above, a cool fill from behind below.
                AddLight(scene, new Vector3(-0.4f, -0.6f, -0.7f), new Color(1f, 0.93f, 0.82f), 1.5f);
                AddLight(scene, new Vector3(0.3f, 0.5f, 0.8f), new Color(0.55f, 0.65f, 0.8f), 0.6f);

                foreach (var h in hulls)
                {
                    var prefab = AssembledObject.LoadPrefab(db.AssemblyByName(h.assembly));
                    if (prefab == null) { Debug.LogWarning($"GoF2: no model for hull {h.key} ({h.assembly})"); continue; }
                    var go = (GameObject)Object.Instantiate(prefab);
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
                    try
                    {
                        Pose(go, h);
                        Orient(go);
                        var bounds = Bounds(go);
                        if (bounds.size == Vector3.zero) continue;
                        Frame(cam, bounds);
                        var black = Render(cam, rt, Color.black);
                        var white = Render(cam, rt, Color.white);
                        // Again, fitted to what was drawn (the bounds count the Void ship's dark outer blades and glows).
                        if (Refit(cam, rt.width, rt.height, black, white))
                        {
                            black = Render(cam, rt, Color.black);
                            white = Render(cam, rt, Color.white);
                        }
                        var icon = Compose(black, white, frame);
                        string path = IconPath(h.key);
                        File.WriteAllBytes(path, icon.EncodeToPNG());
                        Object.DestroyImmediate(icon);
                        written.Add(path);
                    }
                    finally { Object.DestroyImmediate(go); }
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                rt.Release();
                Object.DestroyImmediate(rt);
            }

            AssetDatabase.Refresh();
            foreach (var path in written)
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                if (ti == null) continue;
                ti.textureType = TextureImporterType.Default;
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.SaveAndReimport();
            }
            Debug.Log($"GoF2: {written.Count} hull icons in {ItemIconBuilder.OutDir}.");
        }

        static void AddLight(UnityEngine.SceneManagement.Scene scene, Vector3 dir, Color colour, float intensity)
        {
            var go = new GameObject("HullIconLight");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.rotation = Quaternion.LookRotation(dir);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = colour;
            l.intensity = intensity;
        }

        /// <summary>The pose the hull is flown in (PlayerHull.PrepareModel): the animations' keys load in Awake, which edit
        /// mode doesn't run, so it is called first.</summary>
        static void Pose(GameObject go, World.PlayerHull.Hull h)
        {
            var awake = typeof(PartAnimation).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            foreach (var a in go.GetComponentsInChildren<PartAnimation>(true)) awake?.Invoke(a, null);
            if (h.holdAfterOneOff) PartAnimation.HoldAllAfterOneOff(go);
            else if (h.assembly == "v_station_battlestation_anim_mission_object") PartAnimation.HoldAllAtEnd(go);
            else PartAnimation.HoldAll(go);
            foreach (var lg in go.GetComponentsInChildren<LODGroup>(true)) lg.ForceLOD(0);
        }

        /// <summary>The meshes' bounds (no trails or particles), the same as the item window frames a ship by; an additive glow
        /// layer only when it is small beside the hull (engine flames count, the Void ship's 19 km halo disc doesn't).</summary>
        static Bounds Bounds(GameObject go)
        {
            bool any = false;
            var b = new Bounds();
            var glows = new List<Bounds>();
            // Only what LOD 0 draws (ForceLOD(0)): the other levels' meshes would count while not drawn.
            var hidden = new HashSet<Renderer>();
            foreach (var lg in go.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = lg.GetLODs();
                var shown = lods.Length > 0 ? new HashSet<Renderer>(lods[0].renderers) : new HashSet<Renderer>();
                for (int i = 1; i < lods.Length; i++) foreach (var r in lods[i].renderers) if (r != null && !shown.Contains(r)) hidden.Add(r);
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer) || !r.enabled || !r.gameObject.activeInHierarchy || hidden.Contains(r)) continue;
                if (r.name.Contains("_add")) { glows.Add(r.bounds); continue; }
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) return new Bounds();
            float hull = b.size.magnitude;
            foreach (var g in glows) if (g.size.magnitude < hull * 0.5f) b.Encapsulate(g);
            return b;
        }

        /// <summary>The longest side along the view's length, so the hull fills the wide icon: one wider than it is long (the
        /// unfolded Valkyrie battlestation, 3.2 km across, 1.5 km deep) is turned a quarter about its up, and then one taller
        /// than it is long is laid on its side.</summary>
        static void Orient(GameObject go)
        {
            var b = Bounds(go);
            var rot = Quaternion.identity;
            if (b.size.x > b.size.z * 1.3f) { rot = Quaternion.Euler(0f, 90f, 0f); b.size = new Vector3(b.size.z, b.size.y, b.size.x); }
            if (b.size.y > Mathf.Max(b.size.x, b.size.z) * 1.2f) rot = Quaternion.Euler(90f, 0f, 0f) * rot;
            go.transform.rotation = rot;
        }

        /// <summary>Orthographic, from ViewFrom, rolled, sized to the bounds' corners with a small margin.</summary>
        static void Frame(Camera cam, Bounds b)
        {
            float radius = b.extents.magnitude;
            var t = cam.transform;
            t.position = b.center + ViewFrom.normalized * radius * 4f;
            t.rotation = Quaternion.LookRotation(-ViewFrom.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, Roll);
            cam.nearClipPlane = radius * 0.5f;
            cam.farClipPlane = radius * 8f;
            float halfW = 0f, halfH = 0f;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = t.InverseTransformPoint(c);
                halfW = Mathf.Max(halfW, Mathf.Abs(p.x));
                halfH = Mathf.Max(halfH, Mathf.Abs(p.y));
            }
            cam.orthographicSize = Mathf.Max(halfH, halfW / cam.aspect) * 1.05f;   // all of it: Refit tightens
        }

        /// <summary>Re-centres and zooms the orthographic camera on the drawn pixels' box (alpha over 0.1), a 4 % margin;
        /// false when nothing was drawn.</summary>
        static bool Refit(Camera cam, int w, int h, Color[] black, Color[] white)
        {
            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float a = 1f - ((white[i].r - black[i].r) + (white[i].g - black[i].g) + (white[i].b - black[i].b)) / 3f;
                    if (a < 0.1f) continue;
                    x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
                }
            if (x1 < 0) return false;
            float perPixel = cam.orthographicSize * 2f / h;   // world units per pixel
            var t = cam.transform;
            float cx = ((x0 + x1 + 1) * 0.5f - w * 0.5f) * perPixel, cy = ((y0 + y1 + 1) * 0.5f - h * 0.5f) * perPixel;
            t.position += t.right * cx + t.up * cy;
            float halfW = (x1 - x0 + 1) * 0.5f * perPixel, halfH = (y1 - y0 + 1) * 0.5f * perPixel;
            cam.orthographicSize = Mathf.Max(halfH, halfW / cam.aspect) * 1.04f;
            return true;
        }

        static Color[] Render(Camera cam, RenderTexture rt, Color background)
        {
            cam.backgroundColor = background;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var px = tex.GetPixels();
            Object.DestroyImmediate(tex);
            return px;
        }

        /// <summary>Down to 178 x 86 (a box filter), the alpha from the two backgrounds (white - black = what shows through),
        /// over the ship frame on the 180 x 88 canvas.</summary>
        static Texture2D Compose(Color[] black, Color[] white, Color32[] frame)
        {
            int w = IconW * Super;
            var canvas = new Color[PadW * PadH];
            if (frame != null) for (int i = 0; i < canvas.Length; i++) canvas[i] = frame[i];
            int ox = (PadW - IconW) / 2, oy = (PadH - IconH) / 2;
            for (int y = 0; y < IconH; y++)
                for (int x = 0; x < IconW; x++)
                {
                    // On black a pixel is its colour x its alpha (premultiplied); white - black is what shows through.
                    float r = 0f, g = 0f, bl = 0f, alpha = 0f;
                    for (int sy = 0; sy < Super; sy++)
                        for (int sx = 0; sx < Super; sx++)
                        {
                            int i = (y * Super + sy) * w + x * Super + sx;
                            var b = black[i];
                            var wh = white[i];
                            alpha += Mathf.Clamp01(1f - ((wh.r - b.r) + (wh.g - b.g) + (wh.b - b.b)) / 3f);
                            r += b.r; g += b.g; bl += b.b;
                        }
                    if (alpha <= 0.01f) continue;
                    var src = new Color(r / alpha, g / alpha, bl / alpha);   // un-premultiplied
                    alpha /= Super * Super;
                    ref var d = ref canvas[(oy + y) * PadW + ox + x];
                    float outA = alpha + d.a * (1f - alpha);
                    var rgb = (new Color(src.r, src.g, src.b) * alpha + new Color(d.r, d.g, d.b) * d.a * (1f - alpha)) / Mathf.Max(outA, 1e-4f);
                    d = new Color(Mathf.Clamp01(rgb.r), Mathf.Clamp01(rgb.g), Mathf.Clamp01(rgb.b), outA);
                }
            var o = new Texture2D(PadW, PadH, TextureFormat.RGBA32, false);
            o.SetPixels(canvas);
            o.Apply();
            return o;
        }

        /// <summary>The ship frame 1276 on the 180 x 88 canvas, bottom-left rows (null without the atlas).</summary>
        static Color32[] LoadFrame()
        {
            if (!File.Exists(ItemIconBuilder.JsonPath)) return null;
            var file = JsonUtility.FromJson<IconFile>(File.ReadAllText(ItemIconBuilder.JsonPath));
            var e = file.entries.Find(x => x.kind == "ui" && x.imageId == 1276);
            string p = e != null ? Path.Combine(AtlasDir, e.atlas) : null;
            if (p == null || !File.Exists(p)) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.LoadImage(File.ReadAllBytes(p));
            var src = t.GetPixels32();
            var canvas = new Color32[PadW * PadH];
            int x0 = e.rect[0], y0 = e.rect[1], w = Mathf.Min(e.rect[2], PadW), h = Mathf.Min(e.rect[3], PadH);
            int ox = (PadW - w) / 2, oy = (PadH - h) / 2;
            for (int row = 0; row < h; row++)
            {
                int sy = t.height - 1 - (y0 + row);
                int dy = PadH - 1 - (oy + row);
                for (int col = 0; col < w; col++) canvas[dy * PadW + ox + col] = src[sy * t.width + x0 + col];
            }
            Object.DestroyImmediate(t);
            return canvas;
        }
    }
}
