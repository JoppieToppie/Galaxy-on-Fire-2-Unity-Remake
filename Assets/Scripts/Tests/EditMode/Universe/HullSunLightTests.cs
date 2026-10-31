// HullSunLightTests.cs
// Feature: CLAUDE.md "Space scene" > the hull shader's gamma-space sunlight (OrbitBuilder.SetupLights, GoF2/Hull's
// _GoF2SunGamma / _GoF2SunGlow / _GoF2SunGlowFrom; PRs #203 and #212). LIGHT0 is 15 x the sun table's colour clamped at 2,
// so a red sun comes out nearly white; 30 % of the table's own hue is blended back in (SunTint). Expected numbers by hand
// from PR #212: a sun of 0.38 / 0.13 / 0.11 (1 : 0.34 : 0.29) gives LIGHT0 2 / 1.95 / 1.65 and the hulls 2 / 1.56 / 1.30.

using GoF2Remake.Data;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class HullSunLightTests : SandboxTest
    {
        GameObject sunGo, planetGo;
        Light sun, planet;
        Vector4 savedGamma, savedAmbient, savedPlanetLight, savedPlanetColor, savedPlanetLinear;
        float savedGlow, savedGlowFrom;

        [SetUp]
        public void MakeLights()
        {
            savedGamma = Shader.GetGlobalVector("_GoF2SunGamma");
            savedAmbient = Shader.GetGlobalVector("_GoF2SunAmbient");
            savedPlanetLight = Shader.GetGlobalVector("_GoF2PlanetLight");
            savedPlanetColor = Shader.GetGlobalVector("_GoF2PlanetColor");
            savedPlanetLinear = Shader.GetGlobalVector("_GoF2PlanetLinear");
            savedGlow = Shader.GetGlobalFloat("_GoF2SunGlow");
            savedGlowFrom = Shader.GetGlobalFloat("_GoF2SunGlowFrom");
            sunGo = new GameObject("sun");
            sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            planetGo = new GameObject("planet");
            planet = planetGo.AddComponent<Light>();
            planet.type = LightType.Directional;
        }

        [TearDown]
        public void DropLights()
        {
            Object.DestroyImmediate(sunGo);
            Object.DestroyImmediate(planetGo);
            Shader.SetGlobalVector("_GoF2SunGamma", savedGamma);
            Shader.SetGlobalVector("_GoF2SunAmbient", savedAmbient);
            Shader.SetGlobalVector("_GoF2PlanetLight", savedPlanetLight);
            Shader.SetGlobalVector("_GoF2PlanetColor", savedPlanetColor);
            Shader.SetGlobalVector("_GoF2PlanetLinear", savedPlanetLinear);
            Shader.SetGlobalFloat("_GoF2SunGlow", savedGlow);
            Shader.SetGlobalFloat("_GoF2SunGlowFrom", savedGlowFrom);
        }

        OrbitLayout Layout(Color sunColor)
        {
            var layout = OrbitLayout.Build(Database.Shared, 0);
            layout.sunColor = sunColor;
            return layout;
        }

        /// <summary>The gamma-space LIGHT0 the hulls get: _GoF2SunGamma x the URP light's linear colour.</summary>
        Vector3 HullLight()
        {
            Vector4 ratio = Shader.GetGlobalVector("_GoF2SunGamma");
            Color lin = sun.color.linear * sun.intensity;
            return new Vector3(ratio.x * lin.r, ratio.y * lin.g, ratio.z * lin.b);
        }

        static void Near(Vector3 expected, Vector3 actual, string what)
        {
            Assert.AreEqual(expected.x, actual.x, 0.01f, what + " r");
            Assert.AreEqual(expected.y, actual.y, 0.01f, what + " g");
            Assert.AreEqual(expected.z, actual.z, 0.01f, what + " b");
        }

        [Test]
        public void ARedSunKeepsSomeOfItsHue()
        {
            var layout = Layout(new Color(0.38f, 0.13f, 0.11f));
            Assert.AreEqual(1.95f, layout.SunLightColor.g, 1e-3f, "the test's premise: LIGHT0 nearly white");
            OrbitBuilder.SetupLights(layout, sun, planet);
            var hull = HullLight();
            float g = 1.95f * Mathf.Lerp(1f, 0.13f / 0.38f, 0.3f), b = 1.65f * Mathf.Lerp(1f, 0.11f / 0.38f, 0.3f);
            Near(new Vector3(2f, g, b), hull, "2 / 1.56 / 1.30");
            Assert.Greater(hull.x / hull.y, 1.2f, "redder than the clamped LIGHT0 (2 / 1.95)");
        }

        [Test]
        public void AWhiteSunIsUnchanged()
        {
            OrbitBuilder.SetupLights(Layout(new Color(0.2f, 0.2f, 0.2f)), sun, planet);
            Near(new Vector3(2f, 2f, 2f), HullLight(), "a white sun");
        }

        [Test]
        public void TheHullsGlowWithTheSunlightOnly()
        {
            OrbitBuilder.SetupLights(Layout(new Color(0.2f, 0.2f, 0.2f)), sun, planet);
            Assert.AreEqual(1.3f, Shader.GetGlobalFloat("_GoF2SunGlow"), 1e-6f);
            Assert.AreEqual(0.4f, Shader.GetGlobalFloat("_GoF2SunGlowFrom"), 1e-6f);
            // the hangars and the lounge: URP's own linear diffuse, no glow
            OrbitBuilder.SetupLights(Layout(new Color(0.2f, 0.2f, 0.2f)), sun, planet, gammaSun: false);
            Assert.AreEqual(Vector4.zero, Shader.GetGlobalVector("_GoF2SunGamma"));
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_GoF2SunGlow"));
        }
    }
}
