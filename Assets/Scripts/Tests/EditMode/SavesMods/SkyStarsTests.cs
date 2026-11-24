// SkyStarsTests.cs
// Feature: docs/notes/space-scene.md "Enhanced skies' layers and stars": Options > Graphics "Skies" Enhanced / Combined (Settings.EnhancedSkies) on
// (Original by default) puts Resources/GoF2SkyHd on the game's own sky layers (OrbitBuilder.Enhance): the point stars and specks as star
// lists drawn sharp by GoF2/SpaceSky (World.SkyStars, SpaceSkyCore.hlsl StarPoints) over what stays of the layers (the
// nebulae upscaled by Real-ESRGAN, Reference/tools/sky/enhance_skies.py).

using System.IO;
using GoF2Remake.Data;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class SkyStarsTests : SavesModsTest
    {
        static SkyStars.Star At(Vector3 d, float amp) =>
            new SkyStars.Star { dir = d.normalized, sigma = 5e-4f, amp = new Vector3(amp, amp, amp), layer = SkyStars.LayerStars };

        [Test]
        public void FacesAreTheSkyStripsLayout()
        {
            // Unity's strip order +X -X +Y -Y +Z -Z, each face as seen from inside, v up (as SpaceSkyCore's StripUV)
            Assert.AreEqual(0, SkyStars.Face(new Vector3(1, 0.5f, -0.5f), out var uv));
            Assert.AreEqual(new Vector2(0.75f, 0.75f), uv, "the +X face's right is -Z");
            Assert.AreEqual(1, SkyStars.Face(new Vector3(-1, 0, 0.5f), out uv));
            Assert.AreEqual(new Vector2(0.75f, 0.5f), uv, "the -X face's right is +Z");
            Assert.AreEqual(2, SkyStars.Face(new Vector3(0, 1, 0.5f), out uv));
            Assert.AreEqual(new Vector2(0.5f, 0.25f), uv, "up: the front at the bottom");
            Assert.AreEqual(3, SkyStars.Face(new Vector3(0, -1, 0.5f), out uv));
            Assert.AreEqual(new Vector2(0.5f, 0.75f), uv, "down: the front at the top");
            Assert.AreEqual(4, SkyStars.Face(new Vector3(0.5f, 0, 1), out uv));
            Assert.AreEqual(new Vector2(0.75f, 0.5f), uv);
            Assert.AreEqual(5, SkyStars.Face(new Vector3(-0.5f, 0.5f, -1), out uv));
            Assert.AreEqual(new Vector2(0.75f, 0.75f), uv, "the back face's right is -X");
        }

        [Test]
        public void ReadsTheToolsStarLists()
        {
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                w.Write(new[] { (byte)'G', (byte)'S', (byte)'L', (byte)'1' });
                w.Write(1); w.Write(SkyStars.LayerNebula);
                foreach (var f in new[] { 0f, 0f, 1f, 4e-4f, 0.5f, 0.25f, 0.125f }) w.Write(f);
            }
            var stars = SkyStars.Parse(ms.ToArray());
            Assert.AreEqual(1, stars.Length);
            Assert.AreEqual(Vector3.forward, stars[0].dir);
            Assert.AreEqual(4e-4f, stars[0].sigma, 1e-9f);
            Assert.AreEqual(new Vector3(0.5f, 0.25f, 0.125f), stars[0].amp);
            Assert.AreEqual(SkyStars.LayerNebula, stars[0].layer);
            Assert.AreEqual(0, SkyStars.Parse(new byte[] { 1, 2, 3 }).Length, "not a list: no stars");
        }

        [Test]
        public void ACellHoldsTheTwoBrightestOfItsStars()
        {
            var stars = new[] { At(Vector3.forward, 0.1f), At(Vector3.forward, 0.9f), At(Vector3.forward, 0.5f) };
            var order = SkyStars.Order(stars);
            CollectionAssert.AreEqual(new[] { 1, 2, 0 }, order, "brightest first");
            const int g = 8;
            var cells = SkyStars.Cells(stars, order, g, out int dropped);
            int at = ((g / 2) * 6 * g + 4 * g + g / 2) * SkyStars.Slots;   // the +Z face's middle cell
            Assert.AreEqual(1, cells[at], "the brightest: number 0 in the order, stored + 1");
            Assert.AreEqual(2, cells[at + 1]);
            Assert.AreEqual(1, dropped, "the third is left out");
        }

        [Test]
        public void AStarAtAFacesEdgeIsInTheFaceBeyondToo()
        {
            const int g = 8;
            var stars = new[] { At(new Vector3(1, 0, 0.999f), 1f) };   // on +X, just short of its edge with +Z
            var cells = SkyStars.Cells(stars, SkyStars.Order(stars), g, out _);
            int Cell(int face, int cx, int cy) => cells[(cy * 6 * g + face * g + cx) * SkyStars.Slots];
            Assert.AreEqual(1, Cell(0, 0, g / 2), "its own face: the +X face's left edge (its right is -Z)");
            Assert.AreEqual(1, Cell(4, g - 1, g / 2), "the +Z face's right edge cell, which that side's pixels clamp to");
            int placed = 0;
            foreach (var c in cells) if (c != 0) placed++;
            Assert.AreEqual(2, placed);
        }

        [Test]
        public void TheSkiesAreOriginalByDefault()
        {
            Settings.Reset(new[] { "skyStyle" });
            Assert.AreEqual(Settings.SkyOriginal, Settings.SkyStyle, "the enhanced looks invent detail: the original skies unless the player asks");
            Assert.IsFalse(Settings.EnhancedSkies);
            Settings.SkyStyle = Settings.SkyCombined;
            Assert.IsTrue(Settings.EnhancedSkies, "Combined has Enhanced's layers and stars");
            Settings.SkyStyle = Settings.SkyEs2;
            Assert.IsFalse(Settings.EnhancedSkies, "EVERSPACE 2 style draws no painted nebula");
            CollectionAssert.Contains(Settings.GraphicsKeys, "skyStyle", "reset with the Graphics tab");
            Settings.SkyStyle = Settings.SkyOriginal;
        }

        [Test]
        public void TheGamesSkiesHaveTheirStarLists()
        {
            if (GameContent.Bytes("GoF2SkyHd/stars_000") == null) Assert.Ignore("the Enhanced skies aren't in this checkout or the copy (the HD pack)");
            for (int i = 0; i < 3; i++)
            {
                var stars = SkyStars.List($"stars_{i:000}");
                Assert.Greater(stars.Length, 10000, $"stars_{i:000}");
                Assert.Less(stars.Length, SkyStars.MaxStars - 10000, "with a nebula's specks, under the grid's 16-bit numbers");
                foreach (var s in stars)
                {
                    Assert.AreEqual(1f, s.dir.magnitude, 1e-4f);
                    Assert.Greater(s.sigma, 0f);
                    Assert.AreEqual(SkyStars.LayerStars, s.layer);
                }
            }
            Assert.Greater(SkyStars.List("nebula_016").Length, 1000, "nebula 016's specks");
            Assert.AreEqual(SkyStars.LayerNebula, SkyStars.List("nebula_016")[0].layer);
            for (int i = 0; i <= 18; i++)
            {
                Assert.IsNotNull(GoF2Remake.Visuals.OriginalSkies.Hd($"nebula_{i:000}"), $"nebula_{i:000}");
                Assert.IsNotNull(GameContent.Bytes($"GoF2SkyHd/nebula_{i:000}"), $"nebula_{i:000}.bytes");
            }
        }

        [Test]
        public void EnhancePutsTheUpscaledLayersAndTheStarsOnTheSky()
        {
            if (GameContent.Bytes("GoF2SkyHd/stars_000") == null) Assert.Ignore("the Enhanced skies aren't in this checkout or the copy (the HD pack)");
            Assume.That(SkyStars.Supported, "a device with 32-bit float textures");
            var sky = new Material(Shader.Find("GoF2/SpaceSky"));
            try
            {
                OrbitBuilder.Enhance(sky, 1, 16);
                Assert.AreEqual(GoF2Remake.Visuals.OriginalSkies.Hd("stars_001"), sky.GetTexture("_Stars"));
                Assert.AreEqual(GoF2Remake.Visuals.OriginalSkies.Hd("nebula_016"), sky.GetTexture("_Nebula"));
                Assert.AreEqual(new Vector4(SkyStars.GridSize, SkyStars.DataWidth, 0, 0), sky.GetVector("_StarGrid"));
                var index = sky.GetTexture("_StarIndex");
                Assert.AreEqual(6 * SkyStars.GridSize, index.width);
                Assert.AreEqual(SkyStars.GridSize, index.height);
                Assert.AreSame(index, SkyStars.Grid(1, 16)?.index, "made once a pair of layers");

                var plain = new Material(Shader.Find("GoF2/SpaceSky"));
                OrbitBuilder.Enhance(plain, -1, -1);
                Assert.AreEqual(Vector4.zero, plain.GetVector("_StarGrid"), "a sky of mod images only: nothing to enhance");
                Object.DestroyImmediate(plain);
            }
            finally { Object.DestroyImmediate(sky); }
        }
    }
}
