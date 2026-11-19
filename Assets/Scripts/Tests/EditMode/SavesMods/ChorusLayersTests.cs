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
