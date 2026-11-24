// GoF2SkyLayersTests.cs
// Feature: docs/notes/space-scene.md "Skies": Options > Graphics "Skies" (Settings.SkyStyle: Original, Enhanced, EVERSPACE 2
// style, Combined) draws the game's own 19 nebula skies as layered skies (World.GoF2SkyLayers) from the remake's own assets
// (Resources/GoF2SkyLayers, Reference/tools/sky/sky_layers.py): Chorus's sharper image patch, EVERSPACE 2's clustered stars,
// tinted masks and band, or both.

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class GoF2SkyLayersTests : SavesModsTest
    {
        [Test]
        public void EverySkyHasItsLayoutAndTheImagesAreTheGames()
        {
            // (the patches come from the optional upscaled skies: a checkout without them, the test bed, has none)
            if (Resources.Load<Texture2D>("GoF2SkyLayers/patch_000") == null) Assert.Ignore("the sky patches aren't in this checkout (the optional upscaled skies)");
            for (int i = 0; i <= 18; i++)
            {
                var sky = GoF2SkyLayers.Of(i);
                Assert.IsNotNull(sky, $"nebula {i}");
                Assert.IsNotEmpty(sky.regions, $"nebula {i}'s bright regions");
                Assert.IsNotNull(sky.patch?.image, $"nebula {i}'s patch");
                Assert.IsNotNull(Resources.Load<Texture2D>("GoF2SkyLayers/" + sky.patch.image), sky.patch.image);
            }
            Assert.IsNotNull(Resources.Load<Texture2D>("GoF2SkyLayers/star_mask"), "the remake's own star mask, no other game's");
            Assert.IsNotNull(Resources.Load<Texture2D>("GoF2SkyLayers/nebula_masks"));
        }

        [Test]
        public void ALayerTowardADirectionIsCentredOnIt()
        {
            // SpaceSkyCore ChorusLayer's mapping, here in C#: the patch's middle (k = 0) must be the direction asked for
            foreach (var dir in new[] { new Vector3(0.3f, 0.5f, -0.8f), Vector3.up, new Vector3(-1f, -0.2f, 0.1f), Vector3.forward })
            {
                GoF2SkyLayers.Toward(dir, out float heading, out float elevation);
                Vector2 k = PatchCoords(dir.normalized, heading, elevation, 0.3f);
                Assert.Less(k.magnitude, 1e-3f, $"{dir}: {k}");
                Assert.Greater(PatchCoords(Quaternion.Euler(10f, 10f, 0f) * dir.normalized, heading, elevation, 0.3f).magnitude, 0.05f, "and off it elsewhere");
            }
        }

        static Vector2 PatchCoords(Vector3 w, float heading, float elevation, float scale)
        {
            float h = heading * 2f * Mathf.PI, e = -elevation * 0.5f * Mathf.PI;
            var a = new Vector4(Mathf.Sin(h), Mathf.Cos(h), Mathf.Sin(e), Mathf.Cos(e));
            var v = new Vector3(w.z, w.x, w.y).normalized; var d = -v;
            var p = new Vector3(d.y * a.x + d.z * a.y, d.x, d.y * a.y - d.z * a.x);
            var q = new Vector3(p.z, p.x * a.w - p.y * a.z, p.x * a.z + p.y * a.w).normalized;
            float u = (Mathf.Atan2(q.x, -q.y) / Mathf.PI + 1f) * 0.5f, vv = Mathf.Acos(Mathf.Clamp(q.z, -1f, 1f)) / Mathf.PI;
            return new Vector2((2f * u - 1f) / scale, (vv - 0.5f) / scale);
        }

        [Test]
        public void EachStyleBuildsItsLayers()
        {
            Assert.IsNull(GoF2SkyLayers.Build(16, Settings.SkyOriginal));

            var enhanced = GoF2SkyLayers.Build(16, Settings.SkyEnhanced);
            Assert.IsTrue(enhanced.keepBase, "over the painted sky");
            Assert.IsNull(enhanced.stars, "GoF2's own stars only");
            Assert.AreEqual(1, enhanced.layers.Count, "Chorus's way: the sharper patch alone");
            Assert.AreEqual(ModWorld.ChorusLayer.Image, enhanced.layers[0].kind);
            StringAssert.StartsWith("gof2:patch_", enhanced.layers[0].image);

            var es2 = GoF2SkyLayers.Build(16, Settings.SkyEs2);
            Assert.IsFalse(es2.keepBase, "no painted nebula");
            Assert.AreEqual("gof2:star_mask", es2.stars);
            Assert.Greater(es2.starsNoiseScale, 0f, "ES2's clustered stars");
            Assert.IsFalse(es2.layers.Any(l => l.kind == ModWorld.ChorusLayer.Image), "no patch");
            Assert.LessOrEqual(es2.layers.Count, 1, "ES2's sky sphere: the stars and at most a faint sky-wide nebula (its nebulae are 3D clouds)");
            Assert.IsTrue(es2.layers.All(l => l.kind == ModWorld.ChorusLayer.Mask && l.falloff == 0f));
            Assert.AreEqual(0, GoF2SkyLayers.Build(13, Settings.SkyEs2).layers.Count, "a bare sky: no sky-wide nebula");

            var both = GoF2SkyLayers.Build(16, Settings.SkyCombined);
            Assert.IsTrue(both.keepBase);
            Assert.AreEqual(ModWorld.ChorusLayer.Image, both.layers[0].kind, "the painted sky's patch");
            Assert.AreEqual("gof2:star_mask", both.stars, "ES2's star field behind GoF2's stars");
            Assert.Less(both.starsBrightness, es2.starsBrightness, "only a faint star field behind GoF2's own stars");
            Assert.LessOrEqual(both.layers.Count, ModWorld.ChorusSky.MaxLayers);
        }

        [Test]
        public void TheSkyTakesTheStyle()
        {
            var db = Database.Load();
            var st = db.Stations.First(s => db.Systems.First(y => y.index == s.system).textureIndex == 16);
            var layout = OrbitLayout.Build(db, st.index);
            int style = Settings.SkyStyle; bool live = Settings.LiveSkies;
            try
            {
                Settings.LiveSkies = true;
                Settings.SkyStyle = Settings.SkyOriginal;
                var plain = OrbitBuilder.MakeSky(layout);
                Assert.IsFalse(plain.IsKeywordEnabled("_CHORUS_SKY"));
                Settings.SkyStyle = Settings.SkyCombined;
                var both = OrbitBuilder.MakeSky(layout);
                Assert.IsTrue(both.IsKeywordEnabled("_CHORUS_SKY"));
                Assert.AreEqual(1f, both.GetVector("_ChorusStarsB").w, "over the game's layers");
                // (Enhanced's: the build's or the HD pack's; without either the game's own nebula)
                var hd = GoF2Remake.Visuals.OriginalSkies.Hd("nebula_016");
                if (hd != null) Assert.AreEqual(hd, both.GetTexture("_Nebula"), "Enhanced's upscaled nebula under it");
                if (Resources.Load<Texture2D>("GoF2SkyLayers/patch_000") != null)   // (the optional upscaled skies' patches)
                    Assert.IsNotNull(both.GetTexture("_ChorusLayer1"), "the patch");
                Settings.SkyStyle = Settings.SkyEs2;
                var es2 = OrbitBuilder.MakeSky(layout);
                Assert.AreEqual(0f, es2.GetVector("_ChorusStarsB").w, "no painted nebula under it");
                Settings.LiveSkies = false;
                Assert.IsFalse(OrbitBuilder.MakeSky(layout).IsKeywordEnabled("_CHORUS_SKY"), "the layers need Live mod skies");
                Object.DestroyImmediate(plain); Object.DestroyImmediate(both); Object.DestroyImmediate(es2);
            }
            finally { Settings.SkyStyle = style; Settings.LiveSkies = live; }
        }
    }
}
