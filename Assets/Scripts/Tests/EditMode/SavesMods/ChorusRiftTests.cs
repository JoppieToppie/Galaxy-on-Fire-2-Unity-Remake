// ChorusRiftTests.cs
// Feature: docs/notes/mods-models.md "Chorus rifts": a mod material's "rift" block (ChorusRiftSpec) draws Chorus's rift vortex
// with GoF2/ChorusRift (ModMaterials.Rift), added.

using System.IO;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ChorusRiftTests : SandboxTest
    {
        ModSource source;

        ModInfo ModWithMaps()
        {
            string dir = Path.Combine(Sandbox, "rift_test");
            Directory.CreateDirectory(Path.Combine(dir, "maps"));
            File.WriteAllText(Path.Combine(dir, ModManifest.FileName), "{ \"id\": \"rift_test\", \"name\": \"Rift test\" }");
            foreach (var name in new[] { "tiled", "strip", "cloud" })
            {
                var t = new Texture2D(8, 4, TextureFormat.RGBA32, false);
                t.SetPixels32(Enumerable.Repeat(new Color32(200, 100, 50, 255), 32).ToArray()); t.Apply();
                File.WriteAllBytes(Path.Combine(dir, "maps", name + ".png"), t.EncodeToPNG());
                Object.DestroyImmediate(t);
            }
            source = ModSource.Open(dir);
            return new ModInfo(ModManifest.Read(source), source);
        }

        [TearDown]
        public void CloseSource() { source?.Dispose(); source = null; }

        [Test]
        public void TheBlockIsReadAndTurnedOn()
        {
            var e = JsonUtility.FromJson<CustomShipMaterial>("{ \"material\": \"rift\", \"rift\": { \"tiled\": \"maps/tiled.png\", " +
                "\"strip\": \"maps/strip.png\", \"mainUV\": [1.5, 1, 0.01, 0.03], \"redCenter\": 250, \"inner\": [0.1, 0, 0.003] } }");
            Assert.IsTrue(e.rift.On);
            Assert.AreEqual(250f, e.rift.redCenter);
            Assert.AreEqual(7f, e.rift.detailPow, "the instance's own default");
            Assert.AreEqual(2, e.rift.Textures().Count());
            Assert.IsFalse(JsonUtility.FromJson<CustomShipMaterial>("{ \"material\": \"hull\" }").rift?.On ?? false, "off without the block");
        }

        [Test]
        public void TheRiftGetsItsMaterial()
        {
            var mod = ModWithMaps();
            var m = ModMaterials.FromSpec(mod, JsonUtility.FromJson<CustomShipMaterial>("{ \"material\": \"r\", \"rift\": { \"tiled\": \"maps/tiled.png\", " +
                "\"strip\": \"maps/strip.png\", \"cloud\": \"maps/cloud.png\", \"mainUV\": [1.5, 1, 0.01, 0.03], \"detailOffset\": [0.4, 0.3], " +
                "\"frames\": 4, \"lightningMul\": 1000, \"brightness\": [200, 0.8, 5], \"exposure\": 0.5 } }"), "rift");
            try
            {
                Assert.AreEqual("GoF2/ChorusRift", m.shader.name);
                Assert.AreEqual(new Vector4(1.5f, 1f, 0.01f, 0.03f), m.GetVector("_MainUV"));
                Assert.AreEqual(0.3f, m.GetVector("_Detail2").y);
                Assert.AreEqual(new Vector4(4f, 1000f, 200f, 4f), m.GetVector("_Bolts2"));
                Assert.AreEqual(new Vector4(200f, 0.8f, 5f, 0f), m.GetVector("_Brightness"));
                Assert.AreEqual(new Vector4(2f, 1f, 0.018f, 0.02f), m.GetVector("_ParUV"), "unset: the instance's own");
                Assert.AreEqual(0.5f, m.GetFloat("_Exposure"));
                Assert.IsNotNull(m.GetTexture("_Tiled"));
                Assert.AreEqual(TextureWrapMode.Repeat, m.GetTexture("_Strip").wrapMode);
            }
            finally { Object.DestroyImmediate(m); }
        }

        [Test]
        public void TheBottomIsBlendedAndTheVortexWobbles()
        {
            var mod = ModWithMaps();
            var m = ModMaterials.FromSpec(mod, JsonUtility.FromJson<CustomShipMaterial>("{ \"material\": \"r\", \"rift\": { \"tiled\": \"maps/tiled.png\", " +
                "\"mode\": \"bottom\", \"emissive\": [0.05, 0.0075, 0.0088], \"baseColor\": [0.05, 0.0097, 0.0092], \"depthFade\": 6000, " +
                "\"fadeOffset\": 300, \"fadeLength\": 300, \"wpoMap\": \"maps/cloud.png\", \"wpo\": [0.4, 1, 2, 0.01, 0.04] } }"), "bottom");
            try
            {
                Assert.AreEqual(1f, m.GetFloat("_Mode"), "M_PortalRift_BOTTOM");
                Assert.AreEqual((float)UnityEngine.Rendering.BlendMode.SrcAlpha, m.GetFloat("_SrcBlend"), "alpha-blended");
                Assert.AreEqual((float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha, m.GetFloat("_DstBlend"));
                Assert.AreEqual(new Vector4(0.05f, 0.0075f, 0.0088f, 0f), m.GetVector("_Emissive"));
                Assert.AreEqual(new Vector4(300f, 15f, 15f, 0f), m.GetVector("_Fade"), "game units to metres");
                Assert.AreEqual(0.4f, m.GetVector("_Wpo").x, 1e-6f);
                Assert.AreEqual(new Vector4(1f, 2f, 0.01f, 0.04f), m.GetVector("_WpoUV"));
                Assert.IsNotNull(m.GetTexture("_WpoMap"));
            }
            finally { Object.DestroyImmediate(m); }
            var plain = ModMaterials.FromSpec(mod, JsonUtility.FromJson<CustomShipMaterial>("{ \"material\": \"r\", \"rift\": { \"tiled\": \"maps/tiled.png\" } }"), "plain");
            try
            {
                Assert.AreEqual(0f, plain.GetFloat("_Mode"));
                Assert.AreEqual((float)UnityEngine.Rendering.BlendMode.One, plain.GetFloat("_DstBlend"), "added, as before");
                Assert.AreEqual(0f, plain.GetVector("_Wpo").x, "no map: still");
            }
            finally { Object.DestroyImmediate(plain); }
        }

        [Test]
        public void TheShaderCompiles()
        {
            var shader = Resources.Load<Shader>("GoF2Fx/ChorusRift");
            Assert.IsNotNull(shader);
            var m = new Material(shader);
            try
            {
                for (int p = 0; p < m.passCount; p++) UnityEditor.ShaderUtil.CompilePass(m, p, true);
                var errors = UnityEditor.ShaderUtil.GetShaderMessages(shader).Where(x => x.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error);
                CollectionAssert.IsEmpty(errors.Select(x => $"{x.file}:{x.line} {x.message}"));
            }
            finally { Object.DestroyImmediate(m); }
        }
    }
}
