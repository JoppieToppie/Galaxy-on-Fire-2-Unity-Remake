// UiImageSlots.cs
// Feature: docs/notes/player-assets.md "Stylesheet images": the interface images the stylesheets name by path (the Original
// interface style's pieces UI/Original/og_*, the flight HUD's crosshairs, the plasma gun's, the campaign cards) are cut from the original's
// atlases or made from its pieces, so a build without the originals hasn't them. stripped_checkout.py puts a 4 x 4
// placeholder of its own at each such path (the original's .meta kept: the stylesheets' references hold), and this fills
// each placeholder texture with the real image once the player's copy is converted (after every scene load: Unity may
// unload and reload the assets), so every state the stylesheets give (hover, focus, pressed) draws it:
//   an exact rectangle of an atlas of the converted copy (found by matching the project's images: Crops), the HUD images
//   the converter cut (GoF2Hud), the menu's cuts (MenuImage), and a pack's copy before all of them (Resources/GoF2UiSlots:
//   the common pack's processed pieces; the HD pack's large FISHLABS logo). Also the inline text icons' atlas (TextIcons:
//   the sprite asset is the remake's own, its atlas a placeholder), drawn from the copy's HUD and shop icons. What is made
//   here (a rectangle of an atlas, the menu's cuts, the text icons' atlas) is kept as PNG in the copy's GPU cache
//   (<GpuCache>/ui), keyed by its sources' files: later starts read it instead of decoding atlases (~0.9 s of the menu's
//   first frame before).

using System.Collections.Generic;
using System.IO;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class UiImageSlots
    {
        /// <summary>Exact rectangles (x, y from the top left) of the converted copy's atlases (Textures/textures/<atlas>.png).</summary>
        static readonly Dictionary<string, (string atlas, int x, int y, int w, int h)> Crops = new Dictionary<string, (string, int, int, int, int)>
        {
            ["crosshair"] = ("gof2_interface_iphone4", 392, 78, 81, 81),
            ["crosshair_hit"] = ("gof2_interface_iphone4", 1835, 1231, 81, 81),
            ["og_check"] = ("gof2_interface_iphone4", 1764, 337, 66, 63),
            ["og_check_on"] = ("gof2_interface_iphone4", 1696, 337, 66, 63),
            ["og_footer"] = ("gof2_interface_iphone4", 317, 261, 332, 74),
            ["og_header"] = ("gof2_interface_iphone4", 1601, 684, 332, 60),
            ["og_panel_bg"] = ("gof2_interface_iphone4", 317, 417, 320, 320),
            ["og_title_plate"] = ("gof2_interface_iphone4", 317, 1340, 274, 60),
        };

        /// <summary>The images the converter cuts into Resources/GoF2Hud.</summary>
        static readonly HashSet<string> Hud = new HashSet<string> { "plasma_crosshair", "plasma_crosshair_idle" };

        /// <summary>The remake's processed pieces, from the common pack (Resources/GoF2UiSlots/<name>.png in the copy).</summary>
        public static readonly string[] FromPack =
        {
            "og_action",
            "og_action_on",
            "og_button",
            "og_button_on",
            "og_buy",
            "og_buy_on",
            "og_fl_back",
            "og_fl_back_on",
            "og_fl_bar",
            "og_fl_bar_on",
            "og_fl_next",
            "og_fl_next_on",
            "og_frame",
            "og_hg_back",
            "og_hg_back_on",
            "og_hg_bar",
            "og_hg_footer",
            "og_hg_help",
            "og_hg_infotab",
            "og_hg_plate",
            "og_hg_row",
            "og_hg_row_on",
            "og_hg_steel",
            "og_hg_steel_on",
            "og_launch",
            "og_launch_on",
            "og_mm_back",
            "og_mm_back_on",
            "og_mm_section",
            "og_plate",
            "og_row",
            "og_row_on",
            "og_sell",
            "og_sell_on",
            "og_st_back",
            "og_st_back_on",
            "og_st_button",
            "og_st_button_on",
            "og_st_launch",
            "og_st_launch_on",
            "og_tab",
            "og_tab_on",
            "og_tab_press",
            "og_title",
        };

        static bool hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { hooked = false; filtered.Clear(); textIcons = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            if (hooked) return;
            hooked = true;
            SceneManager.sceneLoaded += (_, __) => { KeepOutOfAtlas(); Fill(); };
        }

        static readonly HashSet<UnityEngine.UIElements.PanelSettings> filtered = new HashSet<UnityEngine.UIElements.PanelSettings>();

        /// <summary>UI Toolkit copies small textures into its dynamic atlas the first time it draws them: a placeholder drawn
        /// before it was filled stayed 4 x 4 and clear there, whatever the texture became (the title logo and the Original
        /// style's pieces didn't show). The slots' textures stay out of every panel's atlas.</summary>
        static void KeepOutOfAtlas()
        {
            if (OriginalSetup.BuiltIn) return;
            foreach (var ps in Resources.FindObjectsOfTypeAll<UnityEngine.UIElements.PanelSettings>())
            {
                if (ps == null || !filtered.Add(ps)) continue;
                var settings = ps.dynamicAtlasSettings;
                var before = settings.customFilter;
                settings.customFilter = (UnityEngine.UIElements.DynamicAtlasCustomFilter)((Texture2D t, ref UnityEngine.UIElements.DynamicAtlasFilters f) =>
                    !IsSlot(t) && (before == null || before(t, ref f)));
                ps.dynamicAtlasSettings = settings;
            }
        }

        static bool IsSlot(Texture2D t) => t != null && (t.name == TextIcons.AssetName || Crops.ContainsKey(t.name) || Hud.Contains(t.name) || t.name.StartsWith("og_")
                                                         || t.name.StartsWith("card_") || t.name.StartsWith("logo_") || t.name.Contains("crosshair"));

        /// <summary>Every placeholder texture loaded now (4 x 4, one of the slots' names) given its image; how many.</summary>
        public static int Fill()
        {
            if (OriginalSetup.BuiltIn || !GameContent.Installed) return 0;
            using var timing = ContentTimings.Measure("interface image slots");
            // (loaded now, so its atlas is among the textures below before a text first asks for an icon)
            textIcons = Resources.Load<UnityEngine.TextCore.Text.SpriteAsset>("Sprite Assets/" + TextIcons.AssetName);
            int n = 0;
            foreach (var t in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                if (t == null || t.width > 4 || t.height > 4) continue;
                var png = ImageOf(t.name);
                if (png == null) continue;
                if (t.LoadImage(png, false)) n++;
            }
            OriginalTextures.ReleaseAtlases();   // (decoded once for all of a fill's rectangles)
            return n;
        }

        static UnityEngine.TextCore.Text.SpriteAsset textIcons;

        /// <summary>The PNG bytes of a slot's image (null: not a slot, or the copy hasn't it).</summary>
        public static byte[] ImageOf(string name)
        {
            if (name == TextIcons.AssetName)
                return Made(name, TextIconsStamp(), TextIconsAtlas);
            // a pack's own copy first (the common pack's processed pieces; the HD pack's large FISHLABS logo over the copy's cut)
            var packed = GameContent.CachePath($"Resources/GoF2UiSlots/{name}.png");
            if (packed != null) return File.ReadAllBytes(packed);
            if (name.StartsWith("card_") || name == "logo_fishlabs" || name == "logo_gof2")   // (the cards, the FISHLABS logo)
                return Made(name, Stamp("Textures/textures/gof2_logos_1440.png", "Textures/textures/gof2_campaign_select_ipad_large.png"),
                            () => OriginalTextures.MenuImage(name)?.EncodeToPNG());
            if (Crops.TryGetValue(name, out var c))
                return Made(name, Stamp($"Textures/textures/{c.atlas}.png") + $"{c.x},{c.y},{c.w},{c.h}",
                            () => OriginalTextures.Crop(c.atlas, c.x, c.y, c.w, c.h, name)?.EncodeToPNG());
            var file = Hud.Contains(name) ? GameContent.CachePath($"Resources/GoF2Hud/{name}.png") : null;
            return file != null ? File.ReadAllBytes(file) : null;
        }

        /// <summary>The text icons' atlas drawn from the converted copy's HUD and shop icons (the cells of the build's sprite
        /// asset), as PNG; null without the asset.</summary>
        static byte[] TextIconsAtlas()
        {
            var asset = textIcons != null ? textIcons : Resources.Load<UnityEngine.TextCore.Text.SpriteAsset>("Sprite Assets/" + TextIcons.AssetName);
            if (asset == null) return null;
            var atlas = TextIcons.Compose(asset, rel =>
            {
                var file = GameContent.CachePath($"Resources/{rel}.png");
                if (file == null) return null;
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                return t.LoadImage(File.ReadAllBytes(file), false) ? t : null;   // (readable: Compose samples it)
            });
            if (atlas == null) return null;
            var png = atlas.EncodeToPNG();
            if (Application.isPlaying) Object.Destroy(atlas); else Object.DestroyImmediate(atlas);
            return png;
        }

        /// <summary>The source files' identity (path, size, change time) a made image is kept under.</summary>
        static string Stamp(params string[] rels)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var rel in rels)
            {
                var p = GameContent.CachePath(rel);
                if (p == null) { sb.Append("-|"); continue; }
                var fi = new FileInfo(p);
                sb.Append(p).Append(':').Append(fi.Length).Append(':').Append(fi.LastWriteTimeUtc.Ticks).Append('|');
            }
            return sb.ToString();
        }

        /// <summary>The text icons' sources: the copy (its manifest, rewritten by a conversion), the HD layer, the sprite table.</summary>
        static string TextIconsStamp()
        {
            var m = new FileInfo(Path.Combine(GameContent.Root, "_manifest.tsv"));
            var asset = textIcons != null ? textIcons : Resources.Load<UnityEngine.TextCore.Text.SpriteAsset>("Sprite Assets/" + TextIcons.AssetName);
            return $"{(m.Exists ? m.LastWriteTimeUtc.Ticks : 0)}|{GameContent.HdActive}|{(asset != null ? asset.spriteCharacterTable.Count : 0)}";
        }

        /// <summary>A made image's PNG: from the copy's GPU cache when made before from the same sources, else made and kept
        /// there. Null when it can't be made.</summary>
        static byte[] Made(string name, string stamp, System.Func<byte[]> make)
        {
            string file;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var h = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(stamp));
                file = Path.Combine(GameContent.GpuCache, "ui", $"{name}_{System.BitConverter.ToString(h, 0, 8).Replace("-", "").ToLowerInvariant()}.png");
            }
            try { if (File.Exists(file)) return File.ReadAllBytes(file); }
            catch (IOException) { }
            var png = make();
            if (png == null) return null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllBytes(file + ".tmp", png);
                if (File.Exists(file)) File.Delete(file);
                File.Move(file + ".tmp", file);
            }
            catch (System.Exception e) when (e is IOException || e is System.UnauthorizedAccessException) { }
            return png;
        }
    }
}
