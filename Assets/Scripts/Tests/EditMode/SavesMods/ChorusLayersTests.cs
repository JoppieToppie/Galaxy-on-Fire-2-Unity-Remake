// ChorusLayersTests.cs
// Feature: docs/notes/mods-models.md "Chorus's layers": a ship material's "chorus" block (ChorusLayersSpec) turns on GoF2/Hull's
// ChorusLayers (Chorus's M_TrimLayersMaster / M_NPCShipTrimMaster / M_PlatingMaster, from their compiled shaders): the
// palette in its own slot, the other maps in URP's, the masters' values in the _Chorus* vectors; off without a palette.

using System.IO;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ChorusLayersTests : SandboxTest
    {
        ModSource source;

        ModInfo ModWithMaps()
        {
            string dir = Path.Combine(Sandbox, "chorus_test");
            Directory.CreateDirectory(Path.Combine(dir, "maps"));
            File.WriteAllText(Path.Combine(dir, ModManifest.FileName), "{ \"id\": \"chorus_test\", \"name\": \"Chorus test\" }");
            foreach (var name in new[] { "palette", "aao", "emissive" })
            {
                var t = new Texture2D(8, 4, TextureFormat.RGBA32, false);
                var px = new Color32[32];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(200, 100, 50, 255);
                t.SetPixels32(px); t.Apply();
                File.WriteAllBytes(Path.Combine(dir, "maps", name + ".png"), t.EncodeToPNG());
                Object.DestroyImmediate(t);
            }
            source = ModSource.Open(dir);
            return new ModInfo(ModManifest.Read(source), source);
        }

        [TearDown]
        public void CloseSource() { source?.Dispose(); source = null; }

        [Test]
        public void ATrimEntryTurnsOnTheLayerWithItsMastersValues()
        {
            var mod = ModWithMaps();
            var c = new ChorusLayersSpec
            {
                kind = "trim", palette = "maps/palette.png", trimAo = "maps/aao.png", trimEmissive = "maps/emissive.png", tiling = 2f,
                accentColor = new[] { 0.1f, 0.2f, 0.3f }, accentMetal = 1f, accentRoughness = 0.25f, emissiveR = new[] { 1f, 0.3f, 0f },
                emissiveAmount = 25f, emissiveUvShift = true, distanceBoost = 15f, nearEmission = 2f, kickerColor = new[] { 1f, 0f, 0f },
                kickerExp = 5f, kickerFadeOffset = 150f, kickerFadeLength = 900f, emissionScale = 0.12f,
            };
            var m = ModMaterials.FromSpec(mod, new CustomShipMaterial { chorus = c }, "chorus trim");
            try
            {
                Assert.AreEqual(new Vector4(1f, 2f, 1f, 0.12f), m.GetVector("_ChorusParams"));
                Assert.IsNotNull(m.GetTexture("_ChorusPalette"), "the palette in its own slot");
                Assert.IsNotNull(m.GetTexture("_OcclusionMap"), "Trim AAO in the occlusion slot");
                Assert.IsNotNull(m.GetTexture("_EmissionMap"), "Trim Emissive in the emission slot");
                Assert.Less(Vector4.Distance(new Vector4(0.1f, 0.2f, 0.3f, 1f), m.GetVector("_ChorusAccent")), 1e-5f, "linear, as given");
                Assert.AreEqual(new Vector4(1f, 0.3f, 0f, 25f), m.GetVector("_ChorusEmitR"));
                Assert.AreEqual(1f, m.GetVector("_ChorusEmitG").w, "lit only where the faces are shifted out of the palette");
                Assert.AreEqual(15f, m.GetVector("_ChorusEmitB").w);
                Assert.AreEqual(new Vector4(1f, 0f, 0f, 5f), m.GetVector("_ChorusKicker"));
                Assert.AreEqual(new Vector4(150f, 900f, 2f, 0f), m.GetVector("_ChorusKicker2"));
                Assert.AreEqual(0f, m.GetVector("_ChorusDirt").w, "no dirt map: no dirt");
                Assert.AreEqual(Color.black, m.GetColor("_EmissionColor"), "the layer makes its own emission");
            }
            finally { Object.DestroyImmediate(m); }
        }

        [Test]
        public void APlatingEntryHasNoAccentOrTrimEmissive()
        {
            var mod = ModWithMaps();
            var m = ModMaterials.FromSpec(mod, new CustomShipMaterial { chorus = new ChorusLayersSpec { kind = "plating", palette = "maps/palette.png",
                trimEmissive = "maps/emissive.png", accentColor = new[] { 1f, 1f, 1f } } }, "chorus plating");
            try
            {
                Assert.AreEqual(2f, m.GetVector("_ChorusParams").x);
                Assert.AreEqual(0f, m.GetVector("_ChorusAccent").w);
                Assert.AreEqual(0f, m.GetVector("_ChorusEmitR").w);
            }
            finally { Object.DestroyImmediate(m); }
        }

        [Test]
        public void WithoutAPaletteTheLayerStaysOff()
        {
            var plain = ModMaterials.FromSpec(null, new CustomShipMaterial { metallic = 0.5f }, "plain");
            var missing = ModMaterials.FromSpec(ModWithMaps(), new CustomShipMaterial { chorus = new ChorusLayersSpec { kind = "trim", palette = "maps/none.png" } }, "missing");
            try
            {
                Assert.AreEqual(0f, plain.GetVector("_ChorusParams").x);
                Assert.AreEqual(0f, missing.GetVector("_ChorusParams").x);
            }
            finally { Object.DestroyImmediate(plain); Object.DestroyImmediate(missing); }
        }

        [Test]
        public void TheHullsForwardPassCompilesWithTheLayer()
        {
            // the template's own variant (its keywords), compiled now: ChorusLayers is HLSL only a render would compile
            var a = Resources.Load<ModAssets>(ModAssets.ResourcePath);
            var m = new Material(a.litOpaque);
            try
            {
                m.SetVector("_ChorusParams", new Vector4(1f, 1f, 1f, 1f));
                int pass = m.FindPass("ForwardLit");
                Assert.GreaterOrEqual(pass, 0);
                UnityEditor.ShaderUtil.CompilePass(m, pass, true);
                var errors = System.Linq.Enumerable.Where(UnityEditor.ShaderUtil.GetShaderMessages(m.shader),
                    x => x.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error);
                CollectionAssert.IsEmpty(System.Linq.Enumerable.Select(errors, x => $"{x.file}:{x.line} {x.message}"));
            }
            finally { Object.DestroyImmediate(m); }
        }

        [Test]
        public void AWaveEntryTurnsOnTheGlowAlongItsPattern()
        {
            // Chorus's M_Phoenix glow lines: the pattern and the masks as data, the wave's numbers in _WaveParams
            var mod = ModWithMaps();
            var e = JsonUtility.FromJson<CustomShipMaterial>("{ \"material\": \"wing\", \"wave\": { \"pattern\": \"maps/palette.png\", " +
                "\"mask\": \"maps/emissive.png\", \"tiling\": 10, \"min\": [0.015, 0.05, 0.0383], \"max\": [0.3, 1, 0.7667] } }");
            Assert.IsTrue(e.wave.On);
            Assert.AreEqual(1, e.wave.channel, "the masks' green by default");
            var m = ModMaterials.FromSpec(mod, e, "wing");
            try
            {
                Assert.AreEqual(new Vector4(1f, 10f, 1f, 1f), m.GetVector("_WaveParams"));
                Assert.IsNotNull(m.GetTexture("_WavePattern"));
                Assert.AreEqual(TextureWrapMode.Repeat, m.GetTexture("_WavePattern").wrapMode);
                Assert.Less(Vector4.Distance(new Vector4(0.3f, 1f, 0.7667f, 1f), m.GetVector("_WaveMax")), 1e-5f);
                var plain = ModMaterials.FromSpec(mod, new CustomShipMaterial { material = "hull" }, "hull");
                Assert.AreEqual(0f, plain.GetVector("_WaveParams").x, "off without a wave");
                Object.DestroyImmediate(plain);
            }
            finally { Object.DestroyImmediate(m); }
        }

        [Test]
        public void ACrystalEntryTakesItsSurfacesAndGlow()
        {
            // Chorus's M_JTU_Blend (the Prophet's crown): A's maps in URP's slots, B's in the detail / occlusion slots, the numbers
            // in the _Chorus* vectors, kind 4
            var mod = ModWithMaps();
            var c = new ChorusLayersSpec { kind = "crystal", diffuseA = "maps/palette.png", maskA = "maps/aao.png", diffuseB = "maps/emissive.png",
                maskB = "maps/aao.png", heightmap = "maps/aao.png", variation = "maps/palette.png", breath = "maps/emissive.png",
                scaleA = 25000f, scaleB = 30000f, innerGlow = new[] { 0.1651f, 0.0116f, 0.0168f }, glowA = 2f, glowB = 5f,
                breathInterval = 8f, pulsePeriod = 4000000f, transitionSoftness = 0.44f, emissionScale = 0.5f };
            Assert.IsTrue(c.On && c.Crystal);
            var m = ModMaterials.FromSpec(mod, new CustomShipMaterial { chorus = c }, "crown");
            try
            {
                Assert.AreEqual(new Vector4(4f, 25000f, 30000f, 0.5f), m.GetVector("_ChorusParams"));
                Assert.IsNotNull(m.GetTexture("_DetailAlbedoMap"), "B's colour");
                Assert.IsNotNull(m.GetTexture("_Es2WearMap"), "the veins");
                Assert.AreEqual(TextureWrapMode.Clamp, m.GetTexture("_ChorusDirtRough").wrapMode, "the breath row");
                Assert.AreEqual(8f, m.GetVector("_ChorusKicker").x);
                Assert.Less(Vector4.Distance(new Vector4(0.1651f, 0.0116f, 0.0168f, 2f), m.GetVector("_ChorusEmitR")), 1e-5f);
                Assert.AreEqual(0f, m.GetVector("_Es2Wear2").z, "the wear layer stays off");
            }
            finally { Object.DestroyImmediate(m); }
        }

        [Test]
        public void ATissueEntryTakesItsMapsAndGlow()
        {
            var mod = ModWithMaps();
            var c = new ChorusLayersSpec { kind = "tissue", thickness = "maps/aao.png", outerVariation = "maps/palette.png", innerVariation = "maps/emissive.png",
                ice = "maps/aao.png", dirt = "maps/palette.png", breath = "maps/emissive.png", outerGlow = new[] { 0.17f, 0f, 0f },
                innerGlow = new[] { 3f, 0f, 0.08f }, noiseTiling = 2f, depth = 0.1f, strength = 1.5f, roughnessIntensity = 0.6f, breathInterval = 4f };
            Assert.IsTrue(c.On && c.Tissue);
            var m = ModMaterials.FromSpec(mod, new CustomShipMaterial { chorus = c }, "tissue");
            try
            {
                Assert.AreEqual(5f, m.GetVector("_ChorusParams").x);
                Assert.AreEqual(new Vector4(2f, 0.1f, 1.5f, 0.6f), m.GetVector("_ChorusEmitB"));
                Assert.AreEqual(new Vector4(3f, 0f, 0.08f, 0f), m.GetVector("_ChorusEmitG"));
                Assert.IsNotNull(m.GetTexture("_Es2WearNoiseMap"), "the ice");
                Assert.AreEqual(4f, m.GetVector("_ChorusKicker").x);
                Assert.AreEqual(Vector4.zero, m.GetVector("_ChorusAccent"), "no core");
            }
            finally { Object.DestroyImmediate(m); }

            // Enable Core (MI_JTU_OmenDroneCore): its colour and contrast, its sphere
            c.coreColor = new[] { 3f, 0.75f, 0f }; c.coreContrast = 6.59f; c.sphereRadius = 3798869.8f;
            var cored = ModMaterials.FromSpec(mod, new CustomShipMaterial { chorus = c }, "core");
            try
            {
                Assert.AreEqual(new Vector4(3f, 0.75f, 0f, 6.59f), cored.GetVector("_ChorusAccent"));
                Assert.AreEqual(new Vector4(0f, 0f, 0f, 3798869.8f), cored.GetVector("_ChorusDirt"));
            }
            finally { Object.DestroyImmediate(cored); }
        }

        [Test]
        public void TheHullsForwardPassCompilesWithTheCrystal()
        {
            var a = Resources.Load<ModAssets>(ModAssets.ResourcePath);
            var m = new Material(a.litOpaque);
            try
            {
                m.SetVector("_ChorusParams", new Vector4(4f, 25000f, 30000f, 1f));
                m.EnableKeyword("_NORMALMAP");
                UnityEditor.ShaderUtil.CompilePass(m, m.FindPass("ForwardLit"), true);
                m.SetVector("_ChorusParams", new Vector4(5f, 1f, 1f, 1f));   // (and the tissue)
                UnityEditor.ShaderUtil.CompilePass(m, m.FindPass("ForwardLit"), true);
                var errors = System.Linq.Enumerable.Where(UnityEditor.ShaderUtil.GetShaderMessages(m.shader),
                    x => x.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error);
                CollectionAssert.IsEmpty(System.Linq.Enumerable.Select(errors, x => $"{x.file}:{x.line} {x.message}"));
            }
            finally { Object.DestroyImmediate(m); }
        }

        [Test]
        public void TheHullsForwardPassCompilesWithTheWave()
        {
            var a = Resources.Load<ModAssets>(ModAssets.ResourcePath);
            var m = new Material(a.litOpaque);
            try
            {
                m.SetVector("_WaveParams", new Vector4(1f, 10f, 1f, 1f));
                int pass = m.FindPass("ForwardLit");
                UnityEditor.ShaderUtil.CompilePass(m, pass, true);
                var errors = System.Linq.Enumerable.Where(UnityEditor.ShaderUtil.GetShaderMessages(m.shader),
                    x => x.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error);
                CollectionAssert.IsEmpty(System.Linq.Enumerable.Select(errors, x => $"{x.file}:{x.line} {x.message}"));
            }
            finally { Object.DestroyImmediate(m); }
        }

        [Test]
        public void AShellEntryTakesItsMasksAndFlowValues()
        {
            // Chorus's M_Guilt_Shell: the mask in _BaseMap (the shadow pass reads its alpha), the flowing noise in _OcclusionMap,
            // the flow's numbers and the push in the vectors
            var mod = ModWithMaps();
            var c = new ChorusLayersSpec { kind = "shell", mask = "maps/aao.png", noise = "maps/palette.png", tiling = 8f, randomness = 3f,
                flowSpeed = 2.2f, flowStrength = 4f, armsFade = 2f, coverage = 0.17f, erode = 0.056f, push = 17.6f, pushNoise = 35.3f,
                metal = 0.5f, roughness = 0.5f, emissiveExponent = 20f, emissiveIntensity = 0.05f, emissiveColor = new[] { 1f, 0.016f, 0.016f }, centerRadius = 1613f };
            var m = ModMaterials.FromSpec(mod, new CustomShipMaterial { chorus = c, doubleSided = true }, "guilt shell");
            try
            {
                Assert.IsTrue(c.On && c.Shell);
                Assert.AreEqual(new Vector4(3f, 8f, 3f, 1f), m.GetVector("_ChorusParams"));
                Assert.IsNotNull(m.GetTexture("_BaseMap"));
                Assert.IsNotNull(m.GetTexture("_OcclusionMap"));
                Assert.Less(Vector4.Distance(new Vector4(2f, 0.17f, 0.056f, 1f), m.GetVector("_ChorusEmitR")), 1e-5f);
                Assert.Less(Vector4.Distance(new Vector4(17.6f, 35.3f, 0f, 0.5f), m.GetVector("_ChorusEmitG")), 1e-4f);
                Assert.Less(Vector4.Distance(new Vector4(20f, 1613f, 2.2f, 4f), m.GetVector("_ChorusAccent2")), 1e-3f);
                Assert.AreEqual(0f, m.GetVector("_ChorusKicker").w);
                Assert.AreSame(m.GetTexture("_BaseMap"), m.GetTexture("_EmissionMap"), "no glow map: the mask glows");
                Assert.IsTrue(m.IsKeywordEnabled("_NORMALMAP"), "its normal from the noise");
                Assert.IsTrue(m.IsKeywordEnabled("_ALPHATEST_ON"), "the UVs for the shadow's dissolve");
                Assert.AreEqual(0f, m.GetFloat("_Cutoff"), "URP's own test never clips");
            }
            finally { Object.DestroyImmediate(m); }

            c.glow = "maps/emissive.png";
            var g = ModMaterials.FromSpec(mod, new CustomShipMaterial { chorus = c, doubleSided = true }, "guilt shell");
            try
            {
                Assert.AreNotSame(g.GetTexture("_BaseMap"), g.GetTexture("_EmissionMap"), "its own glow map");
                CollectionAssert.Contains(c.Textures(), ("maps/emissive.png", true, false));
            }
            finally { Object.DestroyImmediate(g); }
        }

        [Test]
        public void ALavaBlockFlowsOnTheHull()
        {
            // Chorus's M_BrokenPlanet_Core: the hull material keeps its surface, the lava's maps and numbers set, its glow on
            var mod = ModWithMaps();
            var e = JsonUtility.FromJson<CustomShipMaterial>("{ \"material\": \"core\", \"diffuse\": \"maps/palette.png\", \"lava\": { " +
                "\"emit\": \"maps/emissive.png\", \"flow\": \"maps/aao.png\", \"masks\": \"maps/palette.png\", \"a\": [6, 0.05, 1, 0.1], " +
                "\"b\": [1, 1, 0, 0], \"c\": [30, 0.1, 2, 0.05], \"d\": [1, 1.5, 2, 1], \"tempMin\": 400, \"tempMax\": 3000, \"mip\": 2, \"glow\": 0.5 } }");
            Assert.IsTrue(e.lava.On);
            Assert.IsFalse(JsonUtility.FromJson<CustomShipMaterial>("{ \"material\": \"hull\" }").lava?.On ?? false, "off without its maps");
            var m = ModMaterials.FromSpec(mod, e, "lava");
            try
            {
                Assert.AreEqual(new Vector4(6f, 0.05f, 1f, 0.1f), m.GetVector("_LavaA"));
                Assert.AreEqual(new Vector4(1f, 400f, 3000f, 0.5f), m.GetVector("_LavaE"));
                Assert.AreEqual(2f, m.GetVector("_LavaF").x);
                Assert.IsNotNull(m.GetTexture("_LavaFlowMaps"));
                UnityEditor.ShaderUtil.CompilePass(m, m.FindPass("ForwardLit"), true);
                var errors = System.Linq.Enumerable.Where(UnityEditor.ShaderUtil.GetShaderMessages(m.shader),
                    x => x.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error);
                CollectionAssert.IsEmpty(System.Linq.Enumerable.Select(errors, x => $"{x.file}:{x.line} {x.message}"));
            }
            finally { Object.DestroyImmediate(m); }
        }

        [Test]
        public void TheHullsShadowAndDepthPassesCompileWithTheShell()
        {
            // the shell pushed and dissolved in the shadow, depth and depth-normals passes too
            var a = Resources.Load<ModAssets>(ModAssets.ResourcePath);
            var m = new Material(a.litOpaque);
            try
            {
                m.SetVector("_ChorusParams", new Vector4(3f, 8f, 3f, 1f));
                m.EnableKeyword("_NORMALMAP");
                m.EnableKeyword("_ALPHATEST_ON");
                foreach (var pass in new[] { "ForwardLit", "ShadowCaster", "DepthOnly", "DepthNormals" })
                {
                    int i = m.FindPass(pass);
                    Assert.GreaterOrEqual(i, 0, pass);
                    UnityEditor.ShaderUtil.CompilePass(m, i, true);
                }
                var errors = System.Linq.Enumerable.Where(UnityEditor.ShaderUtil.GetShaderMessages(m.shader),
                    x => x.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error);
                CollectionAssert.IsEmpty(System.Linq.Enumerable.Select(errors, x => $"{x.file}:{x.line} {x.message}"));
            }
            finally { Object.DestroyImmediate(m); }
        }

        [Test]
        public void AShipCanKeepItsSkinnedMeshes()
        {
            Assert.IsTrue(JsonUtility.FromJson<CustomShipData>("{ \"id\": \"guilt\", \"skinned\": true }").skinned);
            Assert.IsFalse(JsonUtility.FromJson<CustomShipData>("{ \"id\": \"ship\" }").skinned, "baked to the rest pose by default");
        }

        [Test]
        public void TheBlockReadsFromJsonAndListsItsMaps()
        {
            var e = JsonUtility.FromJson<CustomShipMaterial>("{ \"material\": \"hull\", \"chorus\": { \"kind\": \"trim\", \"palette\": \"p.png\", " +
                "\"trimNormal\": \"n.png\", \"tilingDiffuse\": \"d.png\", \"emissiveUvShift\": true } }");
            Assert.IsTrue(e.chorus.On);
            Assert.IsTrue(e.chorus.emissiveUvShift);
            CollectionAssert.AreEquivalent(new[] { ("p.png", true, false), ("d.png", false, false), ("n.png", true, true) }, e.chorus.Textures());
            Assert.IsFalse(JsonUtility.FromJson<CustomShipMaterial>("{ \"material\": \"hull\" }").chorus?.On ?? false);
        }
    }
}
