// ChorusShieldTests.cs
// Feature: docs/notes/mods-models.md "Chorus shield shell": a mod material's "shield" block (ChorusShieldSpec) draws Chorus's
// M_Shield with GoF2/ChorusShield (ModMaterials.Shield), and ships.json "overcharge" (CustomOvercharge) shows the ship's
// shell for the Shield Overcharge (Flight.ShieldShell): hidden through its delay, its life and tint set while it shows, gone
// at its end.

using System.IO;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ChorusShieldTests : SandboxTest
    {
        ModSource source;

        ModInfo ModWithMaps()
        {
            string dir = Path.Combine(Sandbox, "shield_test");
            Directory.CreateDirectory(Path.Combine(dir, "maps"));
            File.WriteAllText(Path.Combine(dir, ModManifest.FileName), "{ \"id\": \"shield_test\", \"name\": \"Shield test\" }");
            foreach (var name in new[] { "mask", "noise" })
            {
                var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                t.SetPixels32(Enumerable.Repeat(new Color32(200, 100, 50, 255), 16).ToArray()); t.Apply();
                File.WriteAllBytes(Path.Combine(dir, "maps", name + ".png"), t.EncodeToPNG());
                Object.DestroyImmediate(t);
            }
            source = ModSource.Open(dir);
            return new ModInfo(ModManifest.Read(source), source);
        }

        [TearDown]
        public void CloseSource() { source?.Dispose(); source = null; }

        [Test]
        public void TheBlockMakesAShieldMaterial()
        {
            var mod = ModWithMaps();
            var e = JsonUtility.FromJson<CustomShipMaterial>("{ \"material\": \"s\", \"shield\": { \"mask\": \"maps/mask.png\", \"noise\": \"maps/noise.png\", " +
                "\"noiseScale\": 12, \"radius\": [0, 600], \"ring\": [0, 50, 0, 0.2], \"cameraFade\": [72, 144], \"depthFade\": 144, \"exposure\": 0.01 } }");
            Assert.IsTrue(e.shield.On);
            Assert.AreEqual(2, e.shield.Textures().Count());
            Assert.IsFalse(JsonUtility.FromJson<CustomShipMaterial>("{ \"material\": \"hull\" }").shield?.On ?? false, "off without a mask");
            var m = ModMaterials.FromSpec(mod, e, "shield");
            try
            {
                Assert.AreEqual("GoF2/ChorusShield", m.shader.name);
                Assert.AreEqual(12f, m.GetVector("_ShieldNoise").x);
                Assert.AreEqual(new Vector4(0f, 600f, 3f, 1f), m.GetVector("_ShieldRadius"));
                var fade = m.GetVector("_ShieldFade");
                Assert.AreEqual(3.6f, fade.z, 1e-4f, "72 game units: 3.6 m");
                Assert.AreEqual(0.01f, fade.w, 1e-6f, "the exposure");
                Assert.Less(Vector4.Distance(new Vector4(7.2f, 7.2f, 0f, 0f), m.GetVector("_ShieldDepth")), 1e-4f, "the fades in metres");
                Assert.IsNotNull(m.GetTexture("_Mask"));
            }
            finally { Object.DestroyImmediate(m); }
        }

        [Test]
        public void TheShellShowsAfterItsDelayAndGoesAtItsEnd()
        {
            var c = JsonUtility.FromJson<CustomShipData>("{ \"id\": \"x\", \"overcharge\": { \"model\": \"s.glb\", \"color\": [0.7, 10, 3.025], " +
                "\"alpha\": 25, \"colorOverLife\": [0, 1, 0], \"alphaOverLife\": [0, 1, 1, 0] } }");
            Assert.AreEqual(4.8f, c.overcharge.seconds, "Chorus's Lifetime by default");
            Assert.AreEqual(0.5f, c.overcharge.delay, "its EmitterDelay");
            Assert.IsTrue(string.IsNullOrEmpty(JsonUtility.FromJson<CustomShipData>("{ \"id\": \"y\" }").overcharge?.model), "none: the bubble");

            Assert.AreEqual(-0.5f / 4.8f, ShieldShell.LifeAt(c.overcharge, 0f), 1e-5f);
            Assert.AreEqual(0.5f, ShieldShell.LifeAt(c.overcharge, 2.9f), 1e-5f);
            var mid = ShieldShell.TintAt(c.overcharge, 0.5f);
            Assert.AreEqual(new Vector4(0.7f, 10f, 3.025f, 25f), mid, "the colour and alpha over life's middle: 1");
            Assert.AreEqual(0f, ShieldShell.TintAt(c.overcharge, 1f).w, "faded out at its end");
            Assert.AreEqual(0f, ShieldShell.TintAt(c.overcharge, 0f).y, "dark at its start");

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("GoF2/ChorusShield"));
            go.SetActive(false);
            var mat = go.GetComponent<Renderer>().sharedMaterial;
            try
            {
                var s = ShieldShell.Wrap(go, c.overcharge);
                Assert.IsFalse(go.activeSelf, "through its delay");
                s.Step(2.9f);
                Assert.IsTrue(go.activeSelf);
                var block = new MaterialPropertyBlock();
                go.GetComponent<Renderer>().GetPropertyBlock(block);
                Assert.AreEqual(0.5f, block.GetFloat("_ShieldLife"), 1e-5f);
                Assert.AreEqual(10f, block.GetVector("_ShieldTint").y, 1e-4f);
                Assert.IsTrue(s.Alive);
                s.Step(3f);
                Assert.IsFalse(s.Alive, "gone at its end");
                Assert.IsTrue(go == null, "destroyed");
            }
            finally { if (go != null) Object.DestroyImmediate(go); Object.DestroyImmediate(mat); }
        }

        [Test]
        public void TheShaderCompiles()
        {
            var shader = Resources.Load<Shader>("GoF2Fx/ChorusShield");
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
