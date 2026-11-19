// PlayerCopyTests.cs
// Feature: docs/notes/player-assets.md "The content layer": the game's tables and text read from the player's own copy of
// Galaxy on Fire 2 once it is converted (GameContent), with nothing of the original's in the build. Needs a converted copy:
// $GOF2_ORIGINAL_CACHE, else ~/GOF2UnityRemakeMods/aeitest/cache_android (Reference/tools/player_assets/convert on the
// Android XAPK); without one the tests are ignored.

using System;
using System.IO;
using System.Linq;
using GoF2Remake.Data;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class PlayerCopyTests : SandboxTest
    {
        static string Cache => Environment.GetEnvironmentVariable("GOF2_ORIGINAL_CACHE")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "GOF2UnityRemakeMods", "aeitest", "cache_android");

        string language;

        [SetUp]
        public void UseTheConvertedCopy()
        {
            if (!File.Exists(Path.Combine(Cache, "_manifest.tsv"))) Assert.Ignore("no converted copy of the game at " + Cache);
            GameContent.Root = Cache;
            language = Localization.Language;
        }

        [TearDown]
        public void Back()
        {
            GameContent.Root = null;
            if (language != null) Localization.Load(language, LanguageTables.TableFor(language));
        }

        [Test]
        public void TheTextIconsAtlasIsDrawnFromTheConvertedCopy()
        {
            // the build's sprite table (the remake's own) and its atlas drawn from the copy's HUD and shop icons (UiImageSlots)
            var asset = Resources.Load<UnityEngine.TextCore.Text.SpriteAsset>("Sprite Assets/" + UI.TextIcons.AssetName);
            Assert.IsNotNull(asset, "Resources/Sprite Assets/gof2_text_icons");
            var png = UI.UiImageSlots.ImageOf(UI.TextIcons.AssetName);
            Assert.IsNotNull(png);
            var atlas = new Texture2D(2, 2);
            Assert.IsTrue(atlas.LoadImage(png));
            Assert.AreEqual(UI.TextIcons.Cell * UI.TextIcons.Columns, atlas.width);
            int filled = 0;
            foreach (var c in asset.spriteCharacterTable)
            {
                var r = c.glyph.glyphRect;
                var px = atlas.GetPixels(r.x, r.y, r.width, r.height);
                bool any = false;
                foreach (var p in px) if (p.a > 0.5f) { any = true; break; }
                if (any) filled++;
                else Assert.Fail(c.name + ": an empty cell");
            }
            Assert.Greater(filled, 300, "the HUD icons and every shop icon");
            UnityEngine.Object.DestroyImmediate(atlas);
        }

        [Test]
        public void TheDatabaseComesFromTheConvertedCopy()
        {
            Assert.IsTrue(GameContent.Installed);
            var db = Database.Load();
            Assert.GreaterOrEqual(db.Items.Count, 233, "the original's 233 items (and the base content's after them)");
            Assert.AreEqual("Nirai Impulse EX 1", db.Items[0].name);
            Assert.GreaterOrEqual(db.Ships.Count, 64);
            Assert.AreEqual(34, db.Systems.Count);
            Assert.GreaterOrEqual(db.Stations.Count, 100);
            Assert.IsNotEmpty(AgentData.StoryAgents, "agents.json");
            StringAssert.StartsWith("[", GameContent.Text("GoF2Data/ticker"), "ticker.json");
        }

        [Test]
        public void TheAssembledObjectsComeFromTheConvertedCopy()
        {
            // Every assembly of assemblies.json built from the converted copy (OriginalAssemblies), as AssembledObject.LoadPrefab
            // gives it in a build without the originals. Those whose models the remake remodelled come from the common pack
            // (not installed here): listed, not failed, as long as most build.
            var db = Database.Load();
            var failed = new System.Collections.Generic.List<string>();
            int built = 0, renderers = 0;
            foreach (var e in db.Assemblies)
            {
                if (e.pack == GoF2Remake.Modding.ModShips.Pack) continue;
                var t = GoF2Remake.Visuals.OriginalAssemblies.Template(e);
                if (t == null) { failed.Add(e.name); continue; }
                built++;
                int r = t.GetComponentsInChildren<UnityEngine.Renderer>(true).Length;
                if (r == 0) failed.Add(e.name + " (no renderers)");
                renderers += r;
            }
            UnityEngine.Debug.Log($"PlayerCopyTests: {built} assemblies built ({renderers} renderers); not built: {string.Join(", ", failed)}");
            Assert.Greater(built, db.Assemblies.Count * 9 / 10, "nearly every assembly builds from the copy; not built: " + string.Join(", ", failed));
        }

        [Test]
        public void TheSoundsComeFromTheConvertedCopy()
        {
            // the story table's voice lines refilled from the copy (OriginalRefs), decoded from its MP3 (NLayer)
            // (a line at a time, when it is asked for: OriginalRefs.Entry)
            var story = StoryAssets.Load();
            Assert.IsNotNull(story);
            var names = story.VoiceNames().Take(25).ToList();
            Assert.AreEqual(25, names.Count);
            foreach (var n in names)
            {
                var line = story.OriginalVoice(n, false);
                Assert.IsNotNull(line, n);
                Assert.Greater(line.samples, 1000, n);
            }
            // an effect from its WAV (IMA ADPCM in the bank)
            var fx = OriginalAudio.Get("Audio/SFX_GENERAL/Button_Push_v06.ogg");
            Assert.IsNotNull(fx); Assert.Greater(fx.samples, 100);
            // a music track: longer than OriginalAudio.StreamSeconds, so streamed
            var music = OriginalAudio.Get("Audio/MUSIC/Space_Battle_Medium.ogg");
            Assert.IsNotNull(music);
            Assert.Greater(music.length, OriginalAudio.StreamSeconds);
        }

        [Test]
        public void TheInterfaceImagesComeFromTheConvertedCopy()
        {
            // cut from the atlases by the converter (DerivedImages), loaded by Resources path (OriginalTextures.Image)
            var ring = OriginalTextures.Image("GoF2Hud/lock_00");
            Assert.IsNotNull(ring, "the lock ring's first frame");
            Assert.AreEqual(80, ring.width); Assert.AreEqual(80, ring.height);
            var icon = OriginalTextures.Image("GoF2Icons/item_000");
            Assert.IsNotNull(icon, "item 0's shop icon");
            Assert.AreEqual(DerivedImages.IconW, icon.width); Assert.AreEqual(DerivedImages.IconH, icon.height);
            Assert.IsNotNull(OriginalTextures.Image("GoF2Icons/ship_000"));
            Assert.AreEqual(TextureWrapMode.Repeat, OriginalTextures.Image("GoF2Hud/strip_red").wrapMode);
        }

        [Test]
        public void TheMaterialsAndPrefabsComeFromTheConvertedCopy()
        {
            // every material / prefab reference of the remake's own assets to the originals (OriginalRefs' manifest)
            var json = Resources.Load<TextAsset>(OriginalRefs.ManifestPath).text;
            var paths = System.Text.RegularExpressions.Regex.Matches(json, "\"([^\"]+\\.(?:mat|prefab))\"").Cast<System.Text.RegularExpressions.Match>()
                            .Select(m => m.Groups[1].Value).Distinct().ToList();
            Assert.Greater(paths.Count, 50, "the manifest's materials and prefabs");
            bool gpu = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            var missing = paths.Where(p => gpu || !p.StartsWith("Skyboxes/"))   // (the intro skies are rendered: a GPU)
                               .Where(p => (p.EndsWith(".mat") ? (UnityEngine.Object)OriginalRefs.Find<Material>(p) : OriginalRefs.Find<GameObject>(p)) == null)
                               .ToList();
            Assert.IsEmpty(missing, "not made: " + string.Join(", ", missing));
            TestContext.WriteLine($"{paths.Count} materials and prefabs made");

            // a weapon's fx table (the remake's own, shipped without its references): prefabs and sounds from the copy
            var fx = GoF2Remake.Flight.WeaponFx.Load(0);
            Assert.IsNotNull(fx, "WeaponFx item_000");
            Assert.IsNotNull(fx.projectile, "its projectile");
            Assert.IsNotNull(fx.impact, "its impact");
            Assert.IsNotNull(fx.Shot, "its shot");

            // the story's portraits: their parts (sprites) and the frame the converter cut
            var story = OriginalRefs.Load<StoryAssets>("GoF2Story/StoryAssets");
            Assert.IsNotNull(story);
            Assert.IsNotNull(story.portraitFrame, "the portrait frame");
            Assert.IsNotNull(story.portraitBackground, "the portrait background");
            for (int i = 0; i < story.partNames.Count; i++)
                Assert.IsNotNull(story.Part(story.partNames[i]).tex, "portrait part " + story.partNames[i]);
            Assert.AreEqual(1, story.Part(story.partNames[0]).tex.mipmapCount, "a part as a sprite: no mipmaps");

            // the backdrop as SpaceSceneBuilder made it
            var sun = GoF2Remake.Visuals.OriginalBackdrop.Get("sun_004");
            Assert.IsNotNull(sun, "sun_004");
            Assert.AreEqual("GoF2/Backdrop", sun.shader.name);
            Assert.AreEqual(2900, sun.renderQueue);
            Assert.AreEqual(TextureWrapMode.Clamp, sun.mainTexture.wrapMode);
            var planet = GoF2Remake.Visuals.OriginalBackdrop.Get("planet_010_big");
            Assert.IsNotNull(planet, "planet_010_big");
            Assert.AreEqual(2901, planet.renderQueue);
            Assert.AreEqual((float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha, planet.GetFloat("_DstBlend"));
            Assert.AreEqual(2902, GoF2Remake.Visuals.OriginalBackdrop.Get("sn_planet_ring").renderQueue);
            foreach (var n in new[] { "v_planet_020_big", "sn_sun_011", "sn_supernova", "space_particle", "fog", "v_fog_ice", "sky_asteroid_belt", "planet_void_big" })
                Assert.IsNotNull(GoF2Remake.Visuals.OriginalBackdrop.Get(n), n);
            Assert.Greater(GoF2Remake.Visuals.OriginalBackdrop.TextureNames().Count, 60, "the Mod Builder's list of planets and suns");
        }

        [Test]
        public void EveryRuntimeMaterialHasItsShaderTemplate()
        {
            // a build without the originals keeps only the variants some material in it uses (RuntimeShaderTemplates)
            var templates = Resources.LoadAll<Material>(GoF2Remake.Visuals.OriginalContent.TemplateFolder)
                                     .Select(GoF2Remake.Visuals.OriginalContent.VariantKey).ToHashSet();
            Assert.IsNotEmpty(templates, "the templates (GoF2 > Build > Runtime Shader Templates)");
            var missing = GoF2Remake.Visuals.OriginalContent.EveryMaterial().Select(GoF2Remake.Visuals.OriginalContent.VariantKey)
                                    .Where(k => k != null && !templates.Contains(k)).Distinct().ToList();
            Assert.IsEmpty(missing, "no template (run GoF2 > Build > Runtime Shader Templates): " + string.Join("; ", missing));
            TestContext.WriteLine($"{templates.Count} templates");
        }

        [Test]
        public void TheMenuImagesAreCutFromTheConvertedCopy()
        {
            // MainMenuBuilder's cuts of the original's atlases, which a build without the originals hasn't
            var fishlabs = OriginalTextures.MenuImage("logo_fishlabs");
            Assert.IsNotNull(fishlabs, "the FISHLABS logo");
            Assert.AreEqual((266, 303), (fishlabs.width, fishlabs.height));
            foreach (var n in new[] { "card_gof2", "card_gof2_hover", "card_valkyrie", "card_valkyrie_hover", "card_supernova", "card_supernova_hover" })
            {
                var t = OriginalTextures.MenuImage(n);
                Assert.IsNotNull(t, n);
                Assert.AreEqual((605, 943), (t.width, t.height), n);
            }
        }

        static void Forget(Type t) =>
            t.GetMethod("ResetStatics", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, null);

        [Test]
        public void TheGpuCacheIsMadeOnceAndReadAfter()
        {
            // the first use decodes, compresses (and renders, for a sky) and stores; a later run reads the stored data back
            if (Directory.Exists(GameContent.GpuCache)) Directory.Delete(GameContent.GpuCache, true);
            Forget(typeof(OriginalTextures));
            Forget(typeof(GoF2Remake.Visuals.OriginalSkies));
            const string Tex = "Textures/main/ships/battleship_terran_diffuse.png";
            var first = OriginalTextures.Get(Tex);
            Assert.IsNotNull(first, Tex);
            Assert.AreEqual(1, Directory.GetFiles(Path.Combine(GameContent.GpuCache, "textures"), "*.tex").Length, "one stored texture");
            Forget(typeof(OriginalTextures));
            var again = OriginalTextures.Get(Tex);
            Assert.AreNotSame(first, again);
            Assert.AreEqual((first.format, first.width, first.height, first.mipmapCount), (again.format, again.width, again.height, again.mipmapCount));

            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;   // (no GPU: no sky)
            var t0 = DateTime.Now;
            var sky = GoF2Remake.Visuals.OriginalSkies.Sky("nebula_003");
            double rendered = (DateTime.Now - t0).TotalMilliseconds;
            Assert.IsNotNull(sky);
            Assert.AreEqual(1, Directory.GetFiles(Path.Combine(GameContent.GpuCache, "skies"), "nebula_003_*.sky").Length, "the sky stored");
            Forget(typeof(GoF2Remake.Visuals.OriginalSkies));
            t0 = DateTime.Now;
            var read = GoF2Remake.Visuals.OriginalSkies.Sky("nebula_003");
            double loaded = (DateTime.Now - t0).TotalMilliseconds;
            Assert.IsNotNull(read);
            Assert.AreNotSame(sky, read);
            Assert.AreEqual((sky.graphicsFormat, sky.width, sky.mipmapCount), (read.graphicsFormat, read.width, read.mipmapCount));
            TestContext.WriteLine($"nebula_003: rendered {rendered:0} ms, read back {loaded:0} ms ({sky.graphicsFormat}, {sky.mipmapCount} mips)");
        }

        [Test]
        public void ACopyConvertedByAnotherVersionIsConvertedAgain()
        {
            string dir = Path.Combine(GamePaths.Data, "old_cache");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "_manifest.tsv"), "path\tpackage\tentry\tformat\n");   // (before the stamp)
            GameContent.Root = dir;
            Assert.IsFalse(GameContent.Installed);
            Assert.IsTrue(GameContent.Stale);
            File.WriteAllText(Path.Combine(dir, "_manifest.tsv"), OriginalConverter.ManifestHeader + "\npath\tpackage\tentry\tformat\n");
            GameContent.Refresh();
            Assert.IsTrue(GameContent.Installed);
            Assert.IsFalse(GameContent.Stale);
        }

        [Test]
        public void TheSkiesComeFromTheConvertedCopy()
        {
            // a nebula and a star field rendered from the copy's sky meshes and textures (OriginalSkies.Sky)
            var nebula = GoF2Remake.Visuals.OriginalSkies.Sky("nebula_003");
            Assert.IsNotNull(nebula, "nebula_003");
            Assert.AreEqual(UnityEngine.Rendering.TextureDimension.Cube, nebula.dimension);
            Assert.IsNotNull(GoF2Remake.Visuals.OriginalSkies.Sky("stars_000"));
            Assert.IsNotNull(GoF2Remake.Visuals.OriginalSkies.Sky("nebula_018"), "a Supernova sky");
            // a reflection: the original's own cubemap strip
            var refl = GoF2Remake.Visuals.OriginalSkies.Reflection("cubemap_skybox_003");
            Assert.IsNotNull(refl);
            Assert.AreEqual(128, refl.width);
        }

        [Test]
        public void TheTextComesFromTheConvertedCopy()
        {
            Assert.IsNotNull(GameContent.LanguageText("en"));
            Localization.Load("en", null);   // no table asset: the converted copy's
            Assert.AreEqual("Nirai Impulse EX 1", Localization.Get(1274), "item 0's name");
            Assert.AreEqual(Database.Load().Ships[0].name, Localization.Get(913), "ship 0's name");
        }
    }
}
