// ChorusPortalTests.cs
// Feature: docs/notes/mods-models.md "Chorus portals": a mod material's "portal" block (ChorusPortalSpec) draws Chorus's FTL
// portal or its glow with GoF2/ChorusPortal (ModMaterials.Portal); an object with one gets World.PortalRefraction (the
// scene behind it for the refraction pass).

using System.IO;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ChorusPortalTests : SandboxTest
    {
        ModSource source;

        ModInfo ModWithMaps()
        {
            string dir = Path.Combine(Sandbox, "portal_test");
            Directory.CreateDirectory(Path.Combine(dir, "maps"));
            File.WriteAllText(Path.Combine(dir, ModManifest.FileName), "{ \"id\": \"portal_test\", \"name\": \"Portal test\" }");
            foreach (var name in new[] { "noise", "height", "curve", "gradient" })
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
            var e = JsonUtility.FromJson<CustomShipMaterial>("{ \"material\": \"portal\", \"portal\": { \"kind\": \"portal\", \"noise\": \"maps/noise.png\", " +
                "\"height\": \"maps/height.png\", \"curve\": \"maps/curve.png\", \"tilingV\": 0.2, \"ior\": 1.4 } }");
            Assert.IsTrue(e.portal.On);
            Assert.IsFalse(e.portal.Glow);
            Assert.AreEqual(0.2f, e.portal.tilingV);
            Assert.AreEqual(3, e.portal.Textures().Count());
            Assert.IsFalse(JsonUtility.FromJson<CustomShipMaterial>("{ \"material\": \"hull\" }").portal?.On ?? false, "off without the block");
        }

        [Test]
        public void ThePortalAndTheGlowGetTheirMaterials()
        {
            var mod = ModWithMaps();
            var portal = ModMaterials.FromSpec(mod, JsonUtility.FromJson<CustomShipMaterial>("{ \"material\": \"p\", \"portal\": { \"kind\": \"portal\", " +
                "\"noise\": \"maps/noise.png\", \"height\": \"maps/height.png\", \"curve\": \"maps/curve.png\", \"tilingV\": 0.15, \"panningV\": 0.33, " +
                "\"structurePower\": 1.5, \"structureMult\": 2, \"ior\": 1.5, \"refraction\": 0.1 } }"), "portal");
            var glow = ModMaterials.FromSpec(mod, JsonUtility.FromJson<CustomShipMaterial>("{ \"material\": \"g\", \"portal\": { \"kind\": \"glow\", " +
                "\"gradient\": \"maps/gradient.png\", \"curve\": \"maps/curve.png\", \"color\": [1, 0, 0], \"lerpColor\": 0.5 } }"), "glow");
            try
            {
                Assert.AreEqual("GoF2/ChorusPortal", portal.shader.name);
                Assert.AreEqual(0f, portal.GetFloat("_Kind"));
                Assert.AreEqual(new Vector4(0.15f, 0.33f, 1.5f, 2f), portal.GetVector("_Polar"));
                Assert.AreEqual(1.5f, portal.GetVector("_Bump").z);
                Assert.IsNotNull(portal.GetTexture("_Noise"));
                Assert.AreEqual(TextureWrapMode.Clamp, portal.GetTexture("_Curve").wrapMode, "the curve row clamped");
                Assert.AreEqual(1f, glow.GetFloat("_Kind"));
                Assert.AreEqual(new Vector4(1f, 0f, 0f, 0.5f), glow.GetVector("_GlowColor"));

                var go = new GameObject("gate");
                try
                {
                    var plain = new GameObject("hull"); plain.transform.SetParent(go.transform);
                    plain.AddComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                    Assert.IsFalse(GoF2Remake.World.PortalRefraction.AttachIfPortal(go), "no portal: no opaque texture");
                    var card = new GameObject("card"); card.transform.SetParent(go.transform);
                    card.AddComponent<MeshRenderer>().sharedMaterial = portal;
                    Assert.IsTrue(GoF2Remake.World.PortalRefraction.AttachIfPortal(go));
                    Assert.IsNotNull(go.GetComponent<GoF2Remake.World.PortalRefraction>());
                }
                finally { Object.DestroyImmediate(go); }
            }
            finally { Object.DestroyImmediate(portal); Object.DestroyImmediate(glow); }
        }

        [Test]
        public void TheShaderCompiles()
        {
            var shader = Resources.Load<Shader>("GoF2Fx/ChorusPortal");
            Assert.IsNotNull(shader);
            var m = new Material(shader);
            try
            {
                for (int p = 0; p < m.passCount; p++) UnityEditor.ShaderUtil.CompilePass(m, p, true);
                var errors = UnityEditor.ShaderUtil.GetShaderMessages(shader).Where(x => x.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error);
                CollectionAssert.IsEmpty(errors.Select(x => $"{x.file}:{x.line} {x.message}"));
                Assert.AreEqual(2, m.passCount, "the refraction, then the veins / glow");
            }
            finally { Object.DestroyImmediate(m); }
        }
    }
}
