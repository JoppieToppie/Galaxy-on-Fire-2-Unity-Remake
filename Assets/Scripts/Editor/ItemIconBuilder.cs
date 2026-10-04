// ItemIconBuilder.cs  (Editor only)
// Menu "GoF2/Build/Item Icons": one icon per item and per ship for the shop, from the original's atlases, following
// Reference/research/item_icons.json (image ids decoded in Reference/research/shop.md section 1):
//   item i -> image 2200 + i (items >= 176: 3824 + i), ship s -> image 2417 + s, in gof2_items_ipad_large.png /
//   gof2_items_ipad_2_large.png (what Android HD loads on >= 1700x1080 screens), 178x86 each;
//   ImageFactory::drawItem 0x141a10 draws them over a striped frame of the item's type (red primary, orange secondary,
//   yellow turret, green equipment, grey commodity, blue ship) from gof2_interface2_ipad_large.png.
// Output: Resources/GoF2Icons/item_XXX.png and ship_XXX.png (frame + icon, padded to 180x88 so they compress),
// loaded by name at runtime (GoF2ItemIcons).

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GoF2Remake.EditorTools
{
    public static class ItemIconBuilder
    {
        public const string OutDir = ImportSettings.Root + "/Resources/GoF2Icons";
        const string AtlasDir = ImportSettings.Root + "/Textures/textures";
        const int PadW = 180, PadH = 88;

        [System.Serializable] class Entry { public string kind, name, atlas; public int index = -1, imageId, frameImageId; public int[] rect; }
        [System.Serializable] class IconFile { public List<Entry> entries; }

        public static string JsonPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Reference", "research", "item_icons.json"));

        [MenuItem("GoF2/Build/Item Icons", priority = 221)]
        public static void Build()
        {
            if (!File.Exists(JsonPath)) { Debug.LogError($"GoF2: {JsonPath} missing (Reference/tools/shop/build_item_icons.py)"); return; }
            var file = JsonUtility.FromJson<IconFile>(File.ReadAllText(JsonPath));
            var frames = new Dictionary<int, Entry>();
            foreach (var e in file.entries) if (e.kind == "ui") frames[e.imageId] = e;

            var atlases = new Dictionary<string, Pixels>();
            Pixels Atlas(string name)
            {
                if (atlases.TryGetValue(name, out var px)) return px;
                string p = Path.Combine(AtlasDir, name);
                if (!File.Exists(p)) { Debug.LogWarning($"GoF2: atlas {name} missing"); return atlases[name] = null; }
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                t.LoadImage(File.ReadAllBytes(p));
                px = new Pixels { data = t.GetPixels32(), width = t.width, height = t.height };
                Object.DestroyImmediate(t);
                return atlases[name] = px;
            }

            Directory.CreateDirectory(OutDir);
            var written = new List<string>();
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var e in file.entries)
                {
                    if ((e.kind != "item" && e.kind != "ship") || e.index < 0 || e.rect == null) continue;
                    var icon = Atlas(e.atlas);
                    if (icon == null) continue;
                    var canvas = new Color32[PadW * PadH];
                    if (frames.TryGetValue(e.frameImageId, out var f) && Atlas(f.atlas) != null) Blend(canvas, Atlas(f.atlas), f.rect);
                    Blend(canvas, icon, e.rect);
                    var o = new Texture2D(PadW, PadH, TextureFormat.RGBA32, false);
                    o.SetPixels32(canvas);
                    o.Apply();
                    string path = $"{OutDir}/{e.kind}_{e.index:000}.png";
                    File.WriteAllBytes(path, o.EncodeToPNG());
                    Object.DestroyImmediate(o);
                    written.Add(path);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.Refresh();
            foreach (var path in written) ConfigureIcon(path);
            Debug.Log($"GoF2: {written.Count} item / ship icons in {OutDir}." +
                      (GoF2Remake.Data.CustomShips.All.Count > 0 ? " The custom ships' icons come from GoF2 > Build Custom Ships." : ""));
        }

        static void ConfigureIcon(string path)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            if (ti == null) return;
            ti.textureType = TextureImporterType.Default;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.SaveAndReimport();
        }

        /// <summary>Remake: a custom ship's icon (CustomShipBuilder): the ships' blue plate frame (the first ship entry's
        /// frameImageId) with 'icon' (w x h, bottom-up rows) centred on it, written as ship_NNN.png.</summary>
        public static void WriteShipIcon(int ship, Color32[] icon, int w, int h)
        {
            var canvas = new Color32[PadW * PadH];
            if (File.Exists(JsonPath))
            {
                var file = JsonUtility.FromJson<IconFile>(File.ReadAllText(JsonPath));
                var shipEntry = file.entries.Find(e => e.kind == "ship" && e.index >= 0);
                var frame = shipEntry == null ? null : file.entries.Find(e => e.kind == "ui" && e.imageId == shipEntry.frameImageId);
                string atlas = frame == null ? null : Path.Combine(AtlasDir, frame.atlas);
                if (atlas != null && File.Exists(atlas))
                {
                    var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    t.LoadImage(File.ReadAllBytes(atlas));
                    Blend(canvas, new Pixels { data = t.GetPixels32(), width = t.width, height = t.height }, frame.rect);
                    Object.DestroyImmediate(t);
                }
            }
            Blend(canvas, new Pixels { data = icon, width = w, height = h }, new[] { 0, 0, w, h });
            var o = new Texture2D(PadW, PadH, TextureFormat.RGBA32, false);
            o.SetPixels32(canvas);
            o.Apply();
            Directory.CreateDirectory(OutDir);
            string path = $"{OutDir}/ship_{ship:000}.png";
            File.WriteAllBytes(path, o.EncodeToPNG());
            Object.DestroyImmediate(o);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            ConfigureIcon(path);
        }

        class Pixels { public Color32[] data; public int width, height; }

        /// <summary>Alpha-blends a top-left based atlas rect onto the canvas, centred (1 px transparent border).</summary>
        static void Blend(Color32[] canvas, Pixels atlas, int[] r)
        {
            int x = r[0], y = r[1], w = Mathf.Min(r[2], PadW), h = Mathf.Min(r[3], PadH);
            var src = atlas.data;
            int ox = (PadW - w) / 2, oy = (PadH - h) / 2;
            for (int row = 0; row < h; row++)
            {
                int sy = atlas.height - 1 - (y + row);    // top-left rect -> bottom-left texture rows
                int dy = PadH - 1 - (oy + row);
                if (sy < 0 || sy >= atlas.height) continue;
                for (int col = 0; col < w; col++)
                {
                    int sx = x + col;
                    if (sx < 0 || sx >= atlas.width) continue;
                    var s = src[sy * atlas.width + sx];
                    ref var d = ref canvas[dy * PadW + ox + col];
                    float a = s.a / 255f, da = d.a / 255f, outA = a + da * (1f - a);
                    if (outA <= 0f) continue;
                    d = new Color32(
                        (byte)((s.r * a + d.r * da * (1f - a)) / outA),
                        (byte)((s.g * a + d.g * da * (1f - a)) / outA),
                        (byte)((s.b * a + d.b * da * (1f - a)) / outA),
                        (byte)(outA * 255f));
                }
            }
        }
    }
}
