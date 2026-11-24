// ChorusPlanetTests.cs
// Feature: docs/notes/mods-world.md "Chorus planets": stations.json planetSphere "look": "chorus" with its "chorus" block
// (ModWorld.ChorusPlanetSpec: M_Planet03 / M_Planet_SurfaceCheap's switches and values by Chorus's names, its sRGB maps) and
// "chorusAtmosphere" (the shell), drawn by GoF2/ChorusPlanet and GoF2/ChorusAtmosphere.

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ChorusPlanetTests : SavesModsTest
    {
        [Test]
        public void TheBlocksAreRead()
        {
            WriteMod("w", "t_planet", "1.0", "", ("planets/n.png", "x"), ("planets/c.png", "x"), ("planets/d.png", "x"),
                ("stations.json", "[ { 'override': 78, 'planetSphere': { 'look': 'chorus', 'maps': { 'terrainNormal': 'planets/n.png', " +
                    "'curves': 'planets/c.png', 'diffuse': 'planets/d.png' }, " +
                    "'chorus': { 'kind': 'surface', 'water': true, 'macroThreshold': true, 'srgb': ['diffuse'], " +
                    "'values': { 'Water Level': 0.6, 'Water Color Deep': [0.6, 0.2, 0.1], 'bad': 'x' } }, " +
                    "'chorusAtmosphere': { 'values': { 'PlanetRadiusUU': 200000000, 'AtmosphereHeightUU': 6000000 } } } } ]"));
            ScanAndEnable("t_planet");
            Database.Load();
            var s = ModWorld.PlanetSphereOf(78);
            Assert.AreEqual("chorus", s.look);
            var c = s.chorus;
            Assert.AreEqual("surface", c.kind);
            Assert.IsTrue(c.water && c.macroThreshold);
            Assert.IsTrue(c.clouds && c.roughnessConstant, "the master's defaults");
            Assert.IsFalse(c.macroNormal || c.shadedSpecular);
            Assert.AreEqual(0.6f, c.Value("Water Level", 0f));
            Assert.AreEqual(new Vector4(0.6f, 0.2f, 0.1f, 0f), c.Colour("Water Color Deep", Vector4.zero));
            Assert.IsFalse(c.values.ContainsKey("bad"), "a value is a number or a colour");
            CollectionAssert.Contains(c.srgb.ToList(), "diffuse");
            Assert.AreEqual(0.03f, s.chorusAtmosphere.Value("AtmosphereHeightUU", 0f) / s.chorusAtmosphere.Value("PlanetRadiusUU", 1f), 1e-6f);
        }

        [Test]
        public void TheChorusLookNeedsItsBlock()
        {
            string dir = WriteMod("e", "t_err", "1.0", "", ("stations.json", "[ { 'override': 78, 'planetSphere': { 'look': 'chorus' } } ]"));
            var m = OpenParsed(dir);
            Assert.IsTrue(m.Broken);
            StringAssert.Contains("needs its \"chorus\" block", m.Errors[0]);
        }

        [Test]
        public void TheShadersCompile()
        {
            foreach (var name in new[] { "GoF2Sky/ChorusPlanet", "GoF2Sky/ChorusAtmosphere" })
            {
                var shader = Resources.Load<Shader>(name);
                Assert.IsNotNull(shader, name);
                var m = new Material(shader);
                try
                {
                    UnityEditor.ShaderUtil.CompilePass(m, 0, true);
                    var errors = UnityEditor.ShaderUtil.GetShaderMessages(shader).Where(x => x.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error);
                    CollectionAssert.IsEmpty(errors.Select(x => $"{x.file}:{x.line} {x.message}"), name);
                }
                finally { Object.DestroyImmediate(m); }
            }
        }
    }
}
