// HudImageBuilder.cs  (Editor only)
// Menu "GoF2/Build/HUD Images": cuts the flight HUD's mining and navigation images from the original interface atlases
// into Resources/GoF2Hud (loaded by name by MiningView / NavigationView). Rects: Reference/research/mining.md 3.1
// and 4.7, autopilot_travel.md 1 and 4, starmap_travel.md 11.3 (Android HD = gof2_interface_iphone4.png, plus the images the iPad-large build
// re-binds to gof2_interface2_ipad_large.png; top-left origin, verified by cropping). Sprite strips become numbered frames.

using System.IO;
using UnityEditor;
using UnityEngine;

namespace GoF2Remake.EditorTools
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class HudImageBuilder
    {
        public const string OutDir = ImportSettings.Root + "/Resources/GoF2Hud";
        const string AtlasDir = ImportSettings.Root + "/Textures/textures/";

        // The table of images is GoF2Remake.Data.DerivedImages.Hud (Runtime/Core/Content/DerivedImages.cs), shared with the
        // converter that makes these images from a player's own copy of the game (docs/notes/player-assets.md).
        static (string atlas, string name, int x, int y, int w, int h, int frames, bool repeat)[] Images => GoF2Remake.Data.DerivedImages.Hud;
        const string Main = GoF2Remake.Data.DerivedImages.Main, Ipad = GoF2Remake.Data.DerivedImages.Ipad, Low = GoF2Remake.Data.DerivedImages.Low,
                     Logos = GoF2Remake.Data.DerivedImages.Logos, Ipad3 = GoF2Remake.Data.DerivedImages.Ipad3, Items = GoF2Remake.Data.DerivedImages.Items,
                     Challenge = GoF2Remake.Data.DerivedImages.Challenge;

        /// <summary>Modified: the round touch buttons whose thin clear ring showed light specks (the touch controls, the
        /// autopilot / fast-forward / time extender buttons and their pill, the status icons, plate and bars).</summary>
        static bool Bled(string name) => name.StartsWith("touch_") || (name.StartsWith("autopilot") && !name.EndsWith("_title"))
            || name.StartsWith("fastforward") || name.StartsWith("time_extender") || name == "button_pill"
            || name.StartsWith("status_");

        /// <summary>Modified: the atlas keeps bright colours (white, cyan) in nearly transparent pixels, e.g. in the thin clear
        /// ring of the touch stick, boost and camera buttons. The GPU filters colour and alpha apart, so where the buttons
        /// are drawn smaller or off the pixel grid those colours bled into the opaque neighbours as light specks at the
        /// ring's top, bottom and sides. Pixels below 8 / 255 alpha, and bright ones (a channel from 96 / 255) below 32 / 255
        /// (the time extender's lit clock and the secondary button had cyan up to alpha 24 in the ring), become fully clear
        /// and take the colour of their visible neighbours (the dilation done for 8 steps); the visible pixels keep theirs.</summary>
        static void BleedEdges(Color[] px, int w, int h)
        {
            var known = new bool[px.Length];
            for (int i = 0; i < px.Length; i++)
            {
                if (px[i].a < 8f / 255f || (px[i].a < 32f / 255f && Mathf.Max(px[i].r, Mathf.Max(px[i].g, px[i].b)) >= 96f / 255f))
                    px[i].a = 0f;
                known[i] = px[i].a > 0f;
            }
            var next = new bool[px.Length];
            for (int step = 0; step < 8; step++)
            {
                System.Array.Copy(known, next, known.Length);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (known[i]) continue;
                        float r = 0f, g = 0f, b = 0f; int n = 0;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx, yy = y + dy;
                                if (xx < 0 || yy < 0 || xx >= w || yy >= h || !known[yy * w + xx]) continue;
                                var c = px[yy * w + xx]; r += c.r; g += c.g; b += c.b; n++;
                            }
                        if (n == 0) continue;
                        px[i].r = r / n; px[i].g = g / n; px[i].b = b / n;
                        next[i] = true;
                    }
                System.Array.Copy(next, known, known.Length);
            }
        }

        [MenuItem("GoF2/Build/HUD Images", priority = 220)]
        public static void Build()
        {
            var atlases = new System.Collections.Generic.Dictionary<string, Texture2D>();
            Directory.CreateDirectory(OutDir);
            var written = new System.Collections.Generic.List<(string path, bool repeat)>();
            foreach (var im in Images)
            {
                if (!atlases.TryGetValue(im.atlas, out var src))
                {
                    string file = AtlasDir + im.atlas;
                    if (!File.Exists(file)) { Debug.LogError($"GoF2: {file} missing"); continue; }
                    src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    src.LoadImage(File.ReadAllBytes(file));
                    atlases[im.atlas] = src;
                }
                int n = Mathf.Max(1, im.frames), fw = im.w / n;
                for (int f = 0; f < n; f++)
                {
                    string path = im.frames > 0 ? $"{OutDir}/{im.name}_{f:00}.png" : $"{OutDir}/{im.name}.png";
                    var px = src.GetPixels(im.x + f * fw, src.height - im.y - im.h, fw, im.h);   // rects are top-left based
                    if (Bled(im.name)) BleedEdges(px, fw, im.h);
                    var o = new Texture2D(fw, im.h, TextureFormat.RGBA32, false);
                    o.SetPixels(px);
                    o.Apply();
                    File.WriteAllBytes(path, o.EncodeToPNG());
                    Object.DestroyImmediate(o);
                    written.Add((path, im.repeat));
                }
            }
            foreach (var t in atlases.Values) Object.DestroyImmediate(t);
            AssetDatabase.Refresh();
            foreach (var (path, repeat) in written)
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                if (ti == null) continue;
                ti.textureType = TextureImporterType.Default;
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = true;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }
            Debug.Log($"GoF2: {written.Count} HUD images in {OutDir}.");
        }
    }
}
