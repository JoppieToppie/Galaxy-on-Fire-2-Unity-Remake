// ChorusSkyTests.cs
// Feature: docs/notes/mods-world.md "Chorus skies": a systems.json skybox's "chorus" block (ModWorld.ChorusSky), Chorus's
// M_Skybox_MASTER drawn live by GoF2/SpaceSky (_CHORUS_SKY, OrbitBuilder.SetChorusSky) where Options > Graphics "Live mod
// skies" is on (Settings.LiveSkies: from Medium up, unset it follows the detail distance), its "nebula" panorama otherwise.

using System.IO;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ChorusSkyTests : SavesModsTest
    {
        const string Block = "{ 'override': 1, 'skybox': { 'nebula': 'skies/bake.png', 'stars': 'none', 'nebulaBrightness': 2.5, 'chorus': { " +
            "'emissive': 2, 'gradient': { 'up': [0, 0, 2], 'bias': 0.5, 'gamma': 1, 'opacity': 1, 'colorA': [0.05, 0.1, 0.1], 'colorB': [0.02, 0.03, 0.04] }, " +
            "'stars': { 'image': 'skies/stars.png', 'tiling': 1.6, 'offset': [0.1, 0.2], 'brightness': 10, 'exponent': 3, " +
            "'curve': [[0.18, 0, 0, 0], [0.54, 1, 1, 1]] }, " +
            "'layers': [ { 'image': 'skies/nebula.png', 'heading': 0.25, 'elevation': 1, 'scale': 1.6, 'rotation': 0.5, 'emissive': 3, 'exponent': 2.2, 'opacity': 0.4 }, " +
            "{ 'image': 'skies/nebula.png', 'opacity': 0.2 } ] } } }";

        void ChorusMod()
        {
            string dir = WriteMod("w", "t_chorus", "1.0", "", ("systems.json", "[ " + Block + " ]"));
            Directory.CreateDirectory(Path.Combine(dir, "skies"));
            foreach (var (name, w, h) in new[] { ("bake", 16, 8), ("stars", 8, 8), ("nebula", 8, 8) })
            {
                var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
                var px = Enumerable.Repeat(new Color32(120, 80, 40, 255), w * h).ToArray();
                t.SetPixels32(px); t.Apply();
                File.WriteAllBytes(Path.Combine(dir, "skies", name + ".png"), t.EncodeToPNG());
                Object.DestroyImmediate(t);
            }
            ScanAndEnable("t_chorus");
            Database.Load();
        }

        [Test]
        public void TheBlockIsRead()
        {
            ChorusMod();
            var s = ModWorld.SkyOf(1);
            Assert.AreEqual("mod:t_chorus|skies/bake.png", s.nebula, "the bake: Low's sky and the crossfade's");
            var c = s.chorus;
            Assert.IsNotNull(c);
            Assert.AreEqual(2f, c.emissive);
            Assert.AreEqual(new Vector3(0, 0, 1), c.gradientUp.Value, "the up vector normalized");
            Assert.AreEqual(0.5f, c.gradientBias);
            Assert.AreEqual("mod:t_chorus|skies/stars.png", c.stars);
            Assert.AreEqual(new Vector2(0.1f, 0.2f), c.starsOffset);
            Assert.AreEqual(new Vector4(0f, 0f, 0f, 0.18f), c.curve0, "rgb, then the key's time");
            Assert.AreEqual(2, c.layers.Count);
            Assert.AreEqual(0.4f, c.layers[0].opacity);
            Assert.AreEqual(1f, c.layers[1].scale, "defaults");
            Assert.AreEqual(2f, c.layers[1].exponent);
            CollectionAssert.IsSubsetOf(new[] { "mod:t_chorus|skies/stars.png", "mod:t_chorus|skies/nebula.png" },
                ModWorld.BackdropTextures().ToList(), "decoded with the other backdrops");
        }

        [Test]
        public void TheMaterialTakesTheValues()
        {
            ChorusMod();
            var sky = new Material(Resources.Load<Material>("GoF2Sky/SpaceSky"));
            try
            {
                GoF2Remake.World.OrbitBuilder.SetChorusSky(sky, ModWorld.SkyOf(1).chorus);
                Assert.IsTrue(sky.IsKeywordEnabled("_CHORUS_SKY"));
                Assert.AreEqual(new Vector4(3f, 2f, 2f, 0f), sky.GetVector("_ChorusMisc"), "stars exponent, overall emissive, two layers");
                Assert.AreEqual(1f, sky.GetVector("_ChorusGradB").w, "the gradient's opacity (0: none)");
                var a = sky.GetVector("_ChorusL1A");
                // heading 0.25 turns (x 2 pi: 90 deg), elevation 1 (x -pi / 2: -90 deg)
                Assert.Less(Vector4.Distance(new Vector4(1f, 0f, -1f, 0f), a), 1e-4f);
                var b = sky.GetVector("_ChorusL1B");
                Assert.Less(Vector4.Distance(new Vector4(1.6f, -1f, 0f, 3f), b), 1e-4f, "scale, cos / sin of half a turn, emissive");
                Assert.IsNotNull(sky.GetTexture("_ChorusStarsMap"));
                Assert.IsNotNull(sky.GetTexture("_ChorusLayer2"));
            }
            finally { Object.DestroyImmediate(sky); }
        }

        [Test]
        public void NoGradientIsOpacityZero()
        {
            var sky = new Material(Resources.Load<Material>("GoF2Sky/SpaceSky"));
            try
            {
                GoF2Remake.World.OrbitBuilder.SetChorusSky(sky, new ModWorld.ChorusSky());
                Assert.AreEqual(0f, sky.GetVector("_ChorusGradB").w);
                Assert.AreEqual(0f, sky.GetVector("_ChorusMisc").z, "no layers");
            }
            finally { Object.DestroyImmediate(sky); }
        }

        [Test]
        public void Es2LayersAreReadAndPacked()
        {
            // the layered sky's EVERSPACE 2 methods: clustered stars, a tinted mask (no image: a glow), a gradient band
            const string json = "[ { 'override': 1, 'skybox': { 'chorus': { 'base': true, " +
                "'stars': { 'image': 'skies/stars.png', 'tiling': [2, 1], 'channels': [1, 0.5, 0.25], 'noise': { 'scale': 3 } }, 'layers': [ " +
                "{ 'type': 'mask', 'color': [0.2, 0.4, 0.8], 'intensity': 0.5, 'falloff': 30, 'sunScatter': 2, 'heading': 0.5 }, " +
                "{ 'type': 'gradient', 'up': [0, 0, 2], 'absolute': true, 'min': 0.6, 'max': 1, 'bias': 0.25, 'power': 2, 'color': [1, 0, 0], 'opacity': 0.3 } ] } } } ]";
            string dir = WriteMod("w", "t_es2sky", "1.0", "", ("systems.json", json));
            Directory.CreateDirectory(Path.Combine(dir, "skies"));
            var t = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            File.WriteAllBytes(Path.Combine(dir, "skies", "stars.png"), t.EncodeToPNG());
            Object.DestroyImmediate(t);
            ScanAndEnable("t_es2sky");
            Database.Load();
            var c = ModWorld.SkyOf(1).chorus;
            Assert.IsTrue(c.keepBase);
            Assert.AreEqual(2f, c.starsTiling); Assert.AreEqual(1f, c.starsTilingV);
            Assert.AreEqual(new Vector3(1f, 0.5f, 0.25f), c.starsChannels);
            Assert.AreEqual(3f, c.starsNoiseScale); Assert.AreEqual(2.5f, c.starsNoiseExponent, "ES2's ^2.5 by default");
            Assert.AreEqual(ModWorld.ChorusLayer.Mask, c.layers[0].kind);
            Assert.IsNull(c.layers[0].image, "a mask without an image: a glow");
            Assert.AreEqual(2f, c.layers[0].scale, "a mask covers the whole sky by default");
            Assert.AreEqual(ModWorld.ChorusLayer.Gradient, c.layers[1].kind);
            Assert.AreEqual(new Vector3(0, 0, 1), c.layers[1].up, "normalized");

            var sky = new Material(Resources.Load<Material>("GoF2Sky/SpaceSky"));
            try
            {
                GoF2Remake.World.OrbitBuilder.SetChorusSky(sky, c);
                Assert.AreEqual(2f, sky.GetVector("_ChorusMisc").z, "both layers drawn, the mask without an image too");
                Assert.AreEqual(1f, sky.GetVector("_ChorusStarsB").w, "the game's layers under it");
                Assert.AreEqual(3f, sky.GetVector("_ChorusStarsB").y);
                var d = sky.GetVector("_ChorusL1D");
                Assert.AreEqual(1f, d.x, "a mask"); Assert.AreEqual(4f, d.y, "no image: 1 everywhere");
                Assert.AreEqual(30f * Mathf.Deg2Rad, d.z, 1e-5f); Assert.AreEqual(2f, d.w);
                Assert.AreEqual(new Vector4(0.2f, 0.4f, 0.8f, 1f), sky.GetVector("_ChorusL1C"), "colour, exponent");
                Assert.AreEqual(new Vector4(2f, 1f, 0f, 0f), sky.GetVector("_ChorusL2D"), "a gradient, folded");
                Assert.AreEqual(new Vector4(0.6f, 1f, 2f, 2f), sky.GetVector("_ChorusL2B"), "min, max, Schlick's 1 / 0.25 - 2, power");
            }
            finally { Object.DestroyImmediate(sky); }
        }

        [Test]
        public void ALayerIsAnImageOfTheMod() =>
            StringAssert.Contains("isn't in the mod", WorldError("[ { 'override': 1, 'skybox': { 'chorus': { 'layers': [ { 'image': 'skies/none.png' } ] } } } ]"));

        [Test]
        public void TheCurveIsTwoKeys() =>
            StringAssert.Contains("two keys", WorldError("[ { 'override': 1, 'skybox': { 'chorus': { 'stars': { 'curve': [[0, 1, 1, 1]] } } } } ]"));

        string WorldError(string json)
        {
            string dir = WriteMod("e", "t_err", "1.0", "", ("systems.json", json));
            var m = OpenParsed(dir);
            Assert.IsTrue(m.Broken, "accepted: " + json);
            return m.Errors[0];
        }

        [Test]
        public void TheSkyShaderCompilesWithChorusSky()
        {
            foreach (var path in new[] { "GoF2Sky/SpaceSky" })
            {
                var m = new Material(Resources.Load<Material>(path));
                try
                {
                    UnityEditor.ShaderUtil.CompilePass(m, 0, true);   // the game's layers (BaseSky, the Enhanced stars)
                    m.EnableKeyword("_CHORUS_SKY");
                    UnityEditor.ShaderUtil.CompilePass(m, 0, true);
                    var errors = UnityEditor.ShaderUtil.GetShaderMessages(m.shader).Where(x => x.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error);
                    CollectionAssert.IsEmpty(errors.Select(x => $"{x.file}:{x.line} {x.message}"));
                }
                finally { Object.DestroyImmediate(m); }
            }
            var rift = new Material(Resources.Load<Shader>("GoF2Sky/KhadorRift"));
            try
            {
                rift.EnableKeyword("_CHORUS_SKY");
                for (int p = 0; p < rift.passCount; p++) UnityEditor.ShaderUtil.CompilePass(rift, p, true);
                var errors = UnityEditor.ShaderUtil.GetShaderMessages(rift.shader).Where(x => x.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error);
                CollectionAssert.IsEmpty(errors.Select(x => $"{x.file}:{x.line} {x.message}"), "the rift shares the sky's code");
            }
            finally { Object.DestroyImmediate(rift); }
        }

        [Test]
        public void LiveSkiesAreOnFromMedium()
        {
            Assert.AreEqual(0f, Settings.PresetValue(Settings.PresetLow, "liveSkies"));
            Assert.AreEqual(1f, Settings.PresetValue(Settings.PresetMedium, "liveSkies"));
            Assert.AreEqual(1f, Settings.PresetValue(Settings.PresetUltra, "liveSkies"));
            Settings.OverrideAndroidDefaults(false);
            try
            {
                Settings.Reset(new[] { "liveSkies" });
                Settings.DetailDistance = 0;
                Assert.IsFalse(Settings.LiveSkies, "unset, it follows the detail distance: a desktop player on Low stays on Low");
                Settings.DetailDistance = 2;
                Assert.IsTrue(Settings.LiveSkies);
                Settings.LiveSkies = false;
                Assert.IsFalse(Settings.LiveSkies, "the player's own");
            }
            finally { Settings.OverrideAndroidDefaults(null); }
        }
    }
}
