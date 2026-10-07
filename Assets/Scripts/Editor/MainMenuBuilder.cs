// MainMenuBuilder.cs  (Editor only)
// Menu "GoF2/Scenes/Main Menu Scene": builds Assets/Scenes/MainMenu.unity and the assets it needs.
//   - UI images cut from the original atlases: GoF2 / FISHLABS / ABYSS ENGINE logos (gof2_logos_1440.png,
//     images 7002 / 7001 / 7000) and the Select Campaign cards (gof2_campaign_select_ipad_large.png,
//     images 9500-9505), plus two generated shading gradients.
//   - Inter font assets (SIL Open Font License), PanelSettings with the GoF2 theme, a menu post-processing
//     profile, and the scene: a random station orbit as backdrop (ModMainMenu::OnInitialize), built at runtime
//     by MenuBackground with the flight level's OrbitBuilder (sky, sun/planets, lights, asteroids),
//     slow cinematic camera, background traffic, UI Toolkit menu, music.

using System.IO;
using System.Linq;
using GoF2Remake.UI;
using GoF2Remake.Visuals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace GoF2Remake.EditorTools
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class MainMenuBuilder
    {
        const string UiDir = ImportSettings.Root + "/UI";
        const string MenuDir = UiDir + "/MainMenu";
        const string ImageDir = MenuDir + "/Images";
        const string ScenePath = "Assets/Scenes/MainMenu.unity";

        // Short, neutral Terran radio lines (1.6-2.8 s) for the voice volume preview.
        public static readonly string[] VoicePreviewLines =
        {
            "TERRANFEMALE_MISSION_RADIO_BACK_FOR_REWARD", "TERRANMALE_MISSION_RADIO_WON_1",
            "TERRANFEMALE_MISSION_RADIO_START_5", "TERRANMALE_MISSION_RADIO_BACK_FOR_REWARD",
            "TERRANFEMALE_MISSION_RADIO_WON_1", "TERRANMALE_MISSION_RADIO_START_5",
        };

        static readonly (string code, string name)[] Languages =
        {
            ("en", "English"), ("de", "Deutsch"), ("fr", "Français"), ("es", "Español"),
            ("it", "Italiano"), ("nl", "Nederlands"), ("pl", "Polski"), ("ru", "Русский"), ("pt", "Português (Brasil)"),("zh", "简体中文"),

        };

        [MenuItem("GoF2/Scenes/Main Menu Scene", priority = 100)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildImages();
            ConfigureSplashScreen();
            BuildAppIcons();
            BuildFonts();
            var panelSettings = BuildPanelSettings();
            var profile = BuildVolumeProfile();
            SpaceSceneBuilder.BuildBackdropMaterials();   // the backdrop orbit uses the flight level's sky + materials
            if (!System.IO.File.Exists($"{SkyboxBaker.SpaceSkyDir}/nebula_018.png")) SkyboxBaker.BakeSpaceSkies();
            AssetDatabase.ImportAsset(MenuDir, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            BuildScene(panelSettings, profile);
        }

        // ---- images ------------------------------------------------------------------------------

        static void BuildImages()
        {
            Directory.CreateDirectory(ImageDir);
            var logos = Load($"{ImportSettings.Root}/Textures/textures/gof2_logos_1440.png");
            // Rects from _texture_manifest.json (x, y, w, h, top-left origin).
            Save(Crop(logos, 269, 71, 670, 207), "logo_gof2");
            Save(Crop(logos, 1, 71, 266, 303), "logo_fishlabs");   // ABYSS ENGINE (7000) is replaced by the Unity logo

            // 2048 cards sheet: 3 x 2 grid of cards (blue = normal, orange = selected).
            var cards = Load($"{ImportSettings.Root}/Textures/textures/gof2_campaign_select_ipad_large.png");
            Save(CardAt(cards, 1, 1), "card_gof2");
            Save(CardAt(cards, 1, 0), "card_gof2_hover");
            Save(CardAt(cards, 1, 2), "card_valkyrie");
            Save(CardAt(cards, 0, 0), "card_valkyrie_hover");
            Save(CardAt(cards, 0, 1), "card_supernova");
            Save(CardAt(cards, 0, 2), "card_supernova_hover");

            Save(Gradient(256, 4, true), "shade_left");
            Save(Gradient(4, 256, false), "shade_bottom");
            AssetDatabase.Refresh();
            foreach (var f in Directory.GetFiles(ImageDir, "*.png"))
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(f.Replace('\\', '/'));
                ti.textureType = TextureImporterType.Default;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                if (Path.GetFileName(f).StartsWith("logo_"))
                {
                    ti.textureType = TextureImporterType.Sprite;   // also used by the Unity splash screen
                    ti.spriteImportMode = SpriteImportMode.Single;
                }
                ti.SaveAndReimport();
            }
        }

        static Texture2D Load(string path)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.LoadImage(File.ReadAllBytes(path));
            return t;
        }

        static Texture2D Crop(Texture2D src, int x, int y, int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.SetPixels(src.GetPixels(x, src.height - y - h, w, h));
            t.Apply();
            return t;
        }

        /// <summary>Card in grid cell (row from the top, col): 605 x 943 px cards on a 626 x 966 px pitch
        /// (measured; the sheet is opaque, so the edges can't come from alpha).</summary>
        static Texture2D CardAt(Texture2D sheet, int row, int col) => Crop(sheet, 2 + col * 626, 2 + row * 966, 605, 943);

        static Texture2D Gradient(int w, int h, bool horizontal)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var shade = new Color(0.0f, 0.015f, 0.04f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float k = horizontal ? 1f - x / (float)(w - 1) : 1f - y / (float)(h - 1);
                k = Mathf.SmoothStep(0f, 1f, k);
                t.SetPixel(x, y, new Color(shade.r, shade.g, shade.b, k * (horizontal ? 0.88f : 0.75f)));
            }
            t.Apply();
            return t;
        }

        static void Save(Texture2D t, string name) => File.WriteAllBytes($"{ImageDir}/{name}.png", t.EncodeToPNG());

        /// <summary>
        /// Startup logos (MTitle): FISHLABS, then "Made with Unity" where the original showed ABYSS ENGINE.
        /// Unity's splash screen plays them in builds; the menu scene only shows them in the editor.
        /// </summary>
        public static void ConfigureSplashScreen()
        {
            var fishlabs = AssetDatabase.LoadAssetAtPath<Sprite>($"{ImageDir}/logo_fishlabs.png");
            PlayerSettings.SplashScreen.show = true;
            PlayerSettings.SplashScreen.showUnityLogo = true;
            PlayerSettings.SplashScreen.drawMode = PlayerSettings.SplashScreen.DrawMode.AllSequential;
            PlayerSettings.SplashScreen.unityLogoStyle = PlayerSettings.SplashScreen.UnityLogoStyle.LightOnDark;
            PlayerSettings.SplashScreen.animationMode = PlayerSettings.SplashScreen.AnimationMode.Dolly;
            PlayerSettings.SplashScreen.backgroundColor = Color.black;
            PlayerSettings.SplashScreen.logos = fishlabs != null
                ? new[] { PlayerSettings.SplashScreenLogo.Create(2f, fishlabs), PlayerSettings.SplashScreenLogo.CreateWithUnityLogo(2f) }
                : new[] { PlayerSettings.SplashScreenLogo.CreateWithUnityLogo(2f) };
            EditorUtility.SetDirty(Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// App icon: the remake's icon art (AppIcon/icon_source.png, a square with rounded corners) as it is for the flat
        /// icon (Windows / Linux / UWP / iOS, Android's legacy and round icons); Android's adaptive icon gets it inside the
        /// 66 % safe zone over a blurred, enlarged copy of itself (the launcher's mask, circle or squircle, then shows the
        /// art's matching colours past its rounded corners) and a white mask of its bright parts for the themed icon.
        /// Without the art: the old icon, the GoF2 logo on a dark nebula (skybox_003).
        /// </summary>
        public static void BuildAppIcons()
        {
            string dir = UiDir + "/AppIcon";
            Directory.CreateDirectory(dir);
            const int S = 1024;
            if (File.Exists($"{dir}/icon_source.png")) { BuildAppIconsFromArt(dir, S); return; }
            var logo = Load($"{ImageDir}/logo_gof2.png");
            var sky = Load($"{ImportSettings.Root}/Textures/main/skyboxes/skybox_003.png");

            var bg = new Texture2D(S, S, TextureFormat.RGBA32, false);
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                // 900 px square around the bright part of the nebula (texture rows are bottom-up).
                float u = (560f + x / (float)S * 900f) / sky.width, v = (160f + y / (float)S * 900f) / sky.height;
                var c = sky.GetPixelBilinear(u, v) * 0.6f;
                float vignette = 1f - 0.45f * ((x - S * 0.5f) * (x - S * 0.5f) + (y - S * 0.5f) * (y - S * 0.5f)) / (S * S * 0.5f);
                bg.SetPixel(x, y, new Color(c.r * vignette, c.g * vignette, c.b * vignette + 0.02f, 1f));
            }
            bg.Apply();
            var fg = new Texture2D(S, S, TextureFormat.RGBA32, false);
            fg.SetPixels(new Color[S * S]);
            Stamp(fg, logo, 0.62f);                      // adaptive foreground: inside the 66% safe zone
            var mono = new Texture2D(S, S, TextureFormat.RGBA32, false);   // Android 13 themed icon: white silhouette
            var fgPx = fg.GetPixels();
            for (int i = 0; i < fgPx.Length; i++) fgPx[i] = new Color(1f, 1f, 1f, Mathf.Clamp01(fgPx[i].a * 1.4f - 0.25f));
            mono.SetPixels(fgPx);
            mono.Apply();
            var flat = new Texture2D(S, S, TextureFormat.RGBA32, false);
            flat.SetPixels(bg.GetPixels());
            Stamp(flat, logo, 0.84f);                    // legacy / round / other platforms
            WriteAndApplyIcons(dir, bg, fg, flat, mono);
        }

        /// <summary>The icons from the art (BuildAppIcons).</summary>
        static void BuildAppIconsFromArt(string dir, int S)
        {
            var art = Load($"{dir}/icon_source.png");
            Texture2D Resample(float scale, bool opaqueOnly)
            {
                // The art centred at 'scale' of the icon's size; outside it transparent.
                var t = new Texture2D(S, S, TextureFormat.RGBA32, false);
                var px = new Color[S * S];
                float size = S * scale, x0 = (S - size) * 0.5f;
                for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f - x0) / size, v = (y + 0.5f - x0) / size;
                    var c = u < 0f || v < 0f || u > 1f || v > 1f ? Color.clear : art.GetPixelBilinear(u, v);
                    if (opaqueOnly) c.a = 1f;
                    px[y * S + x] = c;
                }
                t.SetPixels(px);
                t.Apply();
                return t;
            }
            var flat = Resample(1f, false);
            // The background: the art scaled up past the edges and blurred (down to 32 px, then bilinear), opaque.
            var small = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            var smallPx = new Color[32 * 32];
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                // The middle 70 % of the art (its rounded corners left out), averaged over a 4 x 4 grid per pixel.
                Color sum = Color.clear;
                for (int j = 0; j < 4; j++)
                for (int i = 0; i < 4; i++)
                    sum += art.GetPixelBilinear(0.15f + (x + (i + 0.5f) / 4f) / 32f * 0.7f, 0.15f + (y + (j + 0.5f) / 4f) / 32f * 0.7f);
                var c = sum / 16f;
                smallPx[y * 32 + x] = new Color(c.r * 0.8f, c.g * 0.8f, c.b * 0.8f, 1f);
            }
            small.SetPixels(smallPx);
            small.Apply();
            var bg = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var bgPx = new Color[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
                bgPx[y * S + x] = small.GetPixelBilinear((x + 0.5f) / S, (y + 0.5f) / S);
            bg.SetPixels(bgPx);
            bg.Apply();
            var fg = Resample(0.62f, false);   // the adaptive icon shows the middle 66.7 %; a circle mask keeps the title
            // The themed icon: white where the art is bright (the title, the engines, the sun).
            var mono = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var fgPx = fg.GetPixels();
            for (int i = 0; i < fgPx.Length; i++)
            {
                var c = fgPx[i];
                float lum = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                fgPx[i] = new Color(1f, 1f, 1f, Mathf.Clamp01((lum - 0.55f) * 3f) * c.a);
            }
            mono.SetPixels(fgPx);
            mono.Apply();
            WriteAndApplyIcons(dir, bg, fg, flat, mono);
            BuildUwpImages(dir, art, small);
        }

        /// <summary>UWP's Start menu tiles, store logo and splash (PlayerSettings.WSA visual assets, scales 100 and 200 %): the
        /// art on the square ones, the art centred on its blurred colours (the adaptive background) on the wide ones.</summary>
        static void BuildUwpImages(string dir, Texture2D art, Texture2D blurred)
        {
            string uwp = dir + "/UWP";
            Directory.CreateDirectory(uwp);
            Texture2D Compose(int w, int h, float artHeight)
            {
                var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
                var px = new Color[w * h];
                float size = h * artHeight, x0 = (w - size) * 0.5f, y0 = (h - size) * 0.5f;
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var b = w == h && artHeight >= 1f ? Color.clear : blurred.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h);
                    float u = (x + 0.5f - x0) / size, v = (y + 0.5f - y0) / size;
                    var c = u < 0f || v < 0f || u > 1f || v > 1f ? Color.clear : art.GetPixelBilinear(u, v);
                    float a = c.a + b.a * (1f - c.a);
                    var rgb = a > 0f ? (new Color(c.r, c.g, c.b) * c.a + new Color(b.r, b.g, b.b) * b.a * (1f - c.a)) / a : Color.clear;
                    px[y * w + x] = new Color(rgb.r, rgb.g, rgb.b, a);
                }
                t.SetPixels(px);
                t.Apply();
                return t;
            }
            var images = new (PlayerSettings.WSAImageType type, int w, int h, float art)[]
            {
                (PlayerSettings.WSAImageType.PackageLogo, 50, 50, 1f),
                (PlayerSettings.WSAImageType.UWPSquare44x44Logo, 44, 44, 1f),
                (PlayerSettings.WSAImageType.UWPSquare71x71Logo, 71, 71, 1f),
                (PlayerSettings.WSAImageType.UWPSquare150x150Logo, 150, 150, 1f),
                (PlayerSettings.WSAImageType.UWPSquare310x310Logo, 310, 310, 1f),
                (PlayerSettings.WSAImageType.UWPWide310x150Logo, 310, 150, 0.9f),
                (PlayerSettings.WSAImageType.SplashScreenImage, 620, 300, 0.9f),
            };
            var made = new System.Collections.Generic.List<(PlayerSettings.WSAImageType type, PlayerSettings.WSAImageScale scale, string path)>();
            foreach (var (type, w, h, artSize) in images)
                foreach (var (scale, k) in new[] { (PlayerSettings.WSAImageScale._100, 1), (PlayerSettings.WSAImageScale._200, 2) })
                {
                    string path = $"{uwp}/{type}_{k * 100}.png";
                    var png = Compose(w * k, h * k, artSize).EncodeToPNG();
                    // The UWP packager refuses a logo over 200 KB (APPX3207): the detailed art at 200 % (the 310 x 310 and
                    // wide tiles) is left out, so Windows scales the 100 % image.
                    if (png.Length > 204800 && k > 1)
                    {
                        if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
                        PlayerSettings.WSA.SetVisualAssetsImage("", type, scale);
                        Debug.Log($"GoF2: {type} at {k * 100} % is {png.Length / 1024} KB, over UWP's 200 KB: left out");
                        continue;
                    }
                    File.WriteAllBytes(path, png);
                    made.Add((type, scale, path));
                }
            AssetDatabase.Refresh();
            foreach (var (type, scale, path) in made)
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                if (ti != null) { ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.SaveAndReimport(); }
                PlayerSettings.WSA.SetVisualAssetsImage(path, type, scale);
            }
        }

        static void WriteAndApplyIcons(string dir, Texture2D bg, Texture2D fg, Texture2D flat, Texture2D mono)
        {
            File.WriteAllBytes($"{dir}/icon_background.png", bg.EncodeToPNG());
            File.WriteAllBytes($"{dir}/icon_foreground.png", fg.EncodeToPNG());
            File.WriteAllBytes($"{dir}/icon.png", flat.EncodeToPNG());
            File.WriteAllBytes($"{dir}/icon_monochrome.png", mono.EncodeToPNG());
            AssetDatabase.Refresh();
            foreach (var n in new[] { "icon_background", "icon_foreground", "icon_monochrome", "icon" })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath($"{dir}/{n}.png");
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }
            var tFlat = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/icon.png");
            var tBg = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/icon_background.png");
            var tFg = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/icon_foreground.png");
            var tMono = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/icon_monochrome.png");

            PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { tFlat }, IconKind.Any);
            var android = UnityEditor.Build.NamedBuildTarget.Android;
            foreach (var kind in PlayerSettings.GetSupportedIconKinds(android))
            {
                var icons = PlayerSettings.GetPlatformIcons(android, kind);
                foreach (var icon in icons)
                {
                    // Adaptive icons: background, foreground (+ monochrome on newer Unity/Android); else flat.
                    if (icon.minLayerCount >= 3) icon.SetTextures(tBg, tFg, tMono);
                    else if (icon.minLayerCount == 2) icon.SetTextures(tBg, tFg);
                    else icon.SetTexture(tFlat);
                }
                PlayerSettings.SetPlatformIcons(android, kind, icons);
            }
            EditorUtility.SetDirty(Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
            AssetDatabase.SaveAssets();
        }

        /// <summary>Alpha-blends 'logo' centred onto 'dst', scaled to widthFraction of its width.</summary>
        static void Stamp(Texture2D dst, Texture2D logo, float widthFraction)
        {
            int w = Mathf.RoundToInt(dst.width * widthFraction), h = Mathf.RoundToInt(w * logo.height / (float)logo.width);
            int x0 = (dst.width - w) / 2, y0 = (dst.height - h) / 2;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = logo.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h);
                var d = dst.GetPixel(x0 + x, y0 + y);
                float a = c.a + d.a * (1f - c.a);
                var rgb = a > 0f ? (new Color(c.r, c.g, c.b) * c.a + new Color(d.r, d.g, d.b) * d.a * (1f - c.a)) / a : Color.clear;
                dst.SetPixel(x0 + x, y0 + y, new Color(rgb.r, rgb.g, rgb.b, a));
            }
            dst.Apply();
        }

        // ---- fonts, panel, profile -----------------------------------------------------------------

        static void BuildFonts()
        {
            foreach (var n in new[] { "Inter-Regular", "Inter-SemiBold" })
            {
                string path = $"{UiDir}/Fonts/{n}-SDF.asset";
                if (AssetDatabase.LoadAssetAtPath<FontAsset>(path) != null) continue;
                var font = AssetDatabase.LoadAssetAtPath<Font>($"{UiDir}/Fonts/{n}.ttf");
                var fa = FontAsset.CreateFontAsset(font);
                fa.name = $"{n}-SDF";
                AssetDatabase.CreateAsset(fa, path);
                fa.material.name = fa.name + " Material";
                AssetDatabase.AddObjectToAsset(fa.material, fa);
                foreach (var tex in fa.atlasTextures)
                {
                    tex.name = fa.name + " Atlas";
                    AssetDatabase.AddObjectToAsset(tex, fa);
                }
                EditorUtility.SetDirty(fa);
            }
            AssetDatabase.SaveAssets();
        }

        static PanelSettings BuildPanelSettings()
        {
            string path = $"{UiDir}/GoF2PanelSettings.asset";
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(ps, path);
            }
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>($"{UiDir}/GoF2Theme.tss");
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            EditorUtility.SetDirty(ps);
            AssetDatabase.SaveAssets();
            return ps;
        }

        static VolumeProfile BuildVolumeProfile()
        {
            string path = $"{MenuDir}/MainMenuVolume.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile != null) return profile;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            var bloom = profile.Add<Bloom>();
            bloom.threshold.Override(1f);
            bloom.intensity.Override(1.6f);
            bloom.scatter.Override(0.72f);
            bloom.highQualityFiltering.Override(true);
            var tone = profile.Add<Tonemapping>();
            tone.mode.Override(TonemappingMode.Neutral);
            var vignette = profile.Add<Vignette>();
            vignette.intensity.Override(0.32f);
            vignette.smoothness.Override(0.45f);
            var color = profile.Add<ColorAdjustments>();
            color.postExposure.Override(0f);
            color.contrast.Override(10f);
            color.saturation.Override(6f);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        // ---- scene ---------------------------------------------------------------------------------

        static void BuildScene(PanelSettings panelSettings, VolumeProfile profile)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Sky, ambient and fog are set at runtime from the chosen orbit (OrbitBuilder.SetupSky); this is
            // only what the scene shows in edit mode.
            RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>($"{SkyboxBaker.SpaceSkyDir}/SpaceSky.mat");
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

            // LIGHT0 (toward the sun) and LIGHT1 (planet light), aimed and coloured per orbit at runtime.
            var sun = new GameObject("Sun (LIGHT0)").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.None;
            var planetLight = new GameObject("Planet light (LIGHT1)").AddComponent<Light>();
            planetLight.type = LightType.Directional;
            planetLight.shadows = LightShadows.None;

            // Camera (CutScene: FOV ~53 deg, far 200000 game units = 10 km; stations need more here).
            var camGo = new GameObject("Menu Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 60000f;
            cam.allowHDR = true;
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.AddComponent<AudioListener>();
            var menuCam = camGo.AddComponent<MenuCamera>();   // CutScene(2)'s fixed camera and yaw pan (MenuBackground places it)

            // Station backdrop.
            var bgGo = new GameObject("Background");
            var bg = bgGo.AddComponent<MenuBackground>();
            bg.menuCamera = menuCam;
            bg.sunLight = sun;
            bg.planetLight = planetLight;   // the station: random 0..99 at run time, like the original


            var volGo = new GameObject("Post Processing");
            var volume = volGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;

            // UI.
            var uiGo = new GameObject("Main Menu UI");
            var panel = uiGo.AddComponent<PanelRenderer>();   // Unity 6.7's successor to UIDocument
            panel.panelSettings = panelSettings;
            panel.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"{MenuDir}/MainMenu.uxml");
            EditorUtility.SetDirty(panel);
            var music = uiGo.AddComponent<AudioSource>();
            music.playOnAwake = false;
            music.loop = true;
            var sfx = uiGo.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            var voice = uiGo.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            var menu = uiGo.AddComponent<MainMenu>();
            menu.musicSource = music;
            menu.sfxSource = sfx;
            menu.voiceSource = voice;
            menu.voicePreviewEnglish = VoicePreviewLines.Select(l => Clip($"GENERIC_eng/{l}.ogg")).ToArray();
            menu.voicePreviewGerman = VoicePreviewLines.Select(l => Clip($"GENERIC_deu/de_{l}.ogg")).ToArray();
            menu.menuMusic = Clip("MUSIC/Space_NoCombat_Void.ogg");
            menu.buttonPush = Clip("SFX_GENERAL/Button_Push_v06.ogg");
            menu.buttonRelease = Clip("SFX_GENERAL/Button_Release_V06.ogg");
            menu.infoSound = Clip("SFX_GENERAL/Message_Info_Screen_v04.ogg");
            menu.editorSplashLogos = new[] { AssetDatabase.LoadAssetAtPath<Texture2D>($"{ImageDir}/logo_fishlabs.png") };
            menu.languageCodes = Languages.Select(l => l.code).ToArray();
            menu.languageNames = Languages.Select(l => l.name).ToArray();
            menu.languageTables = Languages.Select(l => AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>($"{ImportSettings.Root}/Localization/text_{l.code}.json")).ToArray();
            menu.postVolume = volume;

            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath, 0);
            AddToBuildSettings(SpaceSceneBuilder.ScenePath, 1);
            AddToBuildSettings(StationSceneBuilder.ScenePath, 2);
            Debug.Log($"GoF2: main menu scene created at {ScenePath}. Press Play.");
        }

        static AudioClip Clip(string rel) => AssetDatabase.LoadAssetAtPath<AudioClip>($"{ImportSettings.Root}/Audio/{rel}");

        static void AddToBuildSettings(string path, int index)
        {
            if (!File.Exists(path)) return;
            var list = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
            list.Insert(Mathf.Min(index, list.Count), new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
