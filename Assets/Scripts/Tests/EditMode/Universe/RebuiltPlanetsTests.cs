// RebuiltPlanetsTests.cs
// Feature: Options > Graphics "3D planets" (Settings.RebuiltPlanets, World.RebuiltPlanets, the user's request 2026-11-06): every
// game planet's png but planet_void has its rebuilt sphere in Resources/GoF2Planets3D (000-022, 024-026; 025 with its moon 025m;
// 009 / 016 / 021 with their rings), found from the backdrop's image names and the stations' planet numbers; its disc where the
// png has it; both of its shaders load; the setting is on by default.

using GoF2Remake.Data;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class RebuiltPlanetsTests : SandboxTest
    {
        /// <summary>The maps are derived from the original's planets and distributed separately (docs/notes/player-assets.md
        /// "Weapon fx, hangar shadows, sky masks, 3D planets"): a checkout without them (the test bed) has nothing to test.</summary>
        [SetUp]
        public void NeedTheMaps()
        {
            if (UnityEngine.Resources.Load<UnityEngine.Texture2D>(RebuiltPlanets.Folder + "/000/albedo") == null)
                Assert.Ignore("the 3D planets' maps aren't in this checkout (distributed separately)");
        }

        static readonly string[] Keys =
            { "000", "001", "002", "003", "004", "005", "006", "007", "008", "009", "010", "011", "012", "013", "014", "015", "016", "017", "018", "019", "020", "021", "022", "024", "025", "026" };

        [Test]
        public void TheBackdropsImagesFindTheirPlanets()
        {
            Assert.AreEqual("007", RebuiltPlanets.KeyOfTexture("planet_007_big"));
            Assert.AreEqual("019", RebuiltPlanets.KeyOfTexture("planet_019_small"));
            Assert.AreEqual("021", RebuiltPlanets.KeyOfTexture("v_planet_021_small"));
            Assert.AreEqual("022", RebuiltPlanets.KeyOfTexture("v_planet_022_small"), "the Void's small image is 022's png");
            Assert.AreEqual("025", RebuiltPlanets.KeyOfTexture("sn_planet_025_big"));
            Assert.IsNull(RebuiltPlanets.KeyOfTexture("planet_void_big"), "the Void keeps its png");
            Assert.IsNull(RebuiltPlanets.KeyOfTexture("sun_000"));
            Assert.IsNull(RebuiltPlanets.KeyOfTexture("sn_planet_ring"));
            Assert.IsNull(RebuiltPlanets.KeyOfTexture("mod:some_mod|textures/planet_000_big.png"), "a mod's own image");
            Assert.IsNull(RebuiltPlanets.KeyOfTexture(null));
        }

        [Test]
        public void EveryPlanetNumberButTheVoidsHasOne()
        {
            for (int i = 0; i <= 26; i++)
                if (i == 23) Assert.IsNull(RebuiltPlanets.KeyOfIndex(i), "the Void (23)");
                else Assert.AreEqual(i.ToString("000"), RebuiltPlanets.KeyOfIndex(i), $"planet {i}");
            Assert.IsNull(RebuiltPlanets.KeyOfIndex(27));
            Assert.IsNull(RebuiltPlanets.KeyOfIndex(-1));
        }

        [Test]
        public void EachPlanetsMaterialsLoadWithItsMaps()
        {
            foreach (var key in Keys)
                foreach (bool backdrop in new[] { true, false })
                {
                    var m = RebuiltPlanets.MakeMaterial(key, backdrop);
                    Assert.IsNotNull(m, $"{key} ({(backdrop ? "backdrop" : "scene")})");
                    try
                    {
                        Assert.AreEqual(backdrop ? "GoF2/RebuiltPlanetBackdrop" : "GoF2/RebuiltPlanet", m.shader.name);
                        Assert.IsTrue(m.shader.isSupported, $"{key}: {m.shader.name} compiles");
                        foreach (var t in new[] { "_Albedo", "_Face", "_Corr", "_Curves", "_Halo", "_Cover" })
                            Assert.IsNotNull(m.GetTexture(t), $"{key} {t}");
                        var face = m.GetTexture("_Face");
                        Assert.GreaterOrEqual(face.width, 512, $"{key}: its face at the png's detail");
                        Assert.AreEqual(2 * m.GetTexture("_Albedo").height, m.GetTexture("_Albedo").width, $"{key}: the surface 2:1 round the sphere");
                        Assert.AreEqual(key == "009" || key == "016" || key == "021", RebuiltPlanets.HasRing(key), $"{key}'s ring");
                        if (RebuiltPlanets.HasRing(key)) Assert.IsNotNull(m.GetTexture("_Ring"), $"{key} _Ring");
                    }
                    finally { Object.DestroyImmediate(m); }
                }
        }

        [Test]
        public void TheMoonOf025IsBehindItBesideIt()
        {
            var c = RebuiltPlanets.Companions("025", false);
            Assert.AreEqual(1, c.Count);
            Assert.AreEqual("025m", c[0].key);
            Assert.That(c[0].radius, Is.EqualTo(254.8f / 776f).Within(0.01f));
            Assert.Greater(c[0].at.x, 0.5f, "to the right");
            Assert.Greater(c[0].at.y, 0.5f, "above");
            Assert.Greater(c[0].at.z, 0f, "behind it (object +z: away from the png's viewer)");
            Assert.GreaterOrEqual(c[0].at.magnitude, 1f + c[0].radius, "not inside it");
            Assert.Less(RebuiltPlanets.Companions("025", true)[0].at.x, 0f, "mirrored with it");
            Assert.AreEqual(0, RebuiltPlanets.Companions("000", false).Count);
        }

        [Test]
        public void TheDiscIsWhereThePngHasIt()
        {
            // the pngs' discs by their alpha (planet_rebuild's survey): 000 0.431 of the width round (0.489, 0.500); 009's planet
            // inside its ring 0.161; 025's planet beside its moon 0.379
            Assert.IsTrue(RebuiltPlanets.Disc("000", out var c0, out float r0));
            Assert.That(r0, Is.EqualTo(0.431f).Within(0.005f));
            Assert.That(c0.x, Is.EqualTo(0.489f).Within(0.005f));
            Assert.That(c0.y, Is.EqualTo(0.500f).Within(0.005f));
            Assert.IsTrue(RebuiltPlanets.Disc("009", out _, out float r9));
            Assert.That(r9, Is.EqualTo(329.07f / 2048f).Within(0.003f));
            Assert.IsTrue(RebuiltPlanets.Disc("025", out var c25, out float r25));
            Assert.That(r25, Is.EqualTo(776f / 2048f).Within(0.003f));
            Assert.That(c25.x, Is.EqualTo(1030f / 2048f).Within(0.003f));
            Assert.IsFalse(RebuiltPlanets.Disc("023", out _, out _));
        }

        [Test]
        public void TheSettingIsOnByDefault()
        {
            Assert.IsTrue(Settings.TryDefault(() => Settings.RebuiltPlanets, out bool on));
            Assert.IsTrue(on);
            using (new SettingsScope<bool>(() => Settings.RebuiltPlanets, v => Settings.RebuiltPlanets = v))
            {
                Settings.RebuiltPlanets = false;
                Assert.IsFalse(RebuiltPlanets.Enabled);
                Settings.RebuiltPlanets = true;
                Assert.IsTrue(RebuiltPlanets.Enabled);
            }
            CollectionAssert.Contains(Settings.GraphicsKeys, "rebuiltPlanets", "Default settings on the Graphics tab resets it");
        }
    }
}
