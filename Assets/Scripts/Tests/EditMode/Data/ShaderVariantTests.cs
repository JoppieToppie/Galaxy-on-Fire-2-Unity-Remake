// ShaderVariantTests.cs
// What builds leave out (Data.ShaderVariantRules, docs/notes/platforms.md "Build times"): the features the game can't turn on
// go, VR's stereo only where VR can't run, and what it uses stays. The prefabs and scenes are read for the two features a
// new asset could bring in by itself: a stripped variant shows no error, only the wrong picture.

using System.IO;
using System.Text.RegularExpressions;
using GoF2Remake.Data;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ShaderVariantTests
    {
        [TestCase("FOG_EXP")]
        [TestCase("FOG_EXP2")]
        [TestCase("LOD_FADE_CROSSFADE")]
        [TestCase("_LIGHT_COOKIES")]
        public void WhatTheGameCantTurnOnIsLeftOut(string keyword)
        {
            Assert.IsTrue(ShaderVariantRules.UnusedKeyword(keyword, true));
            Assert.IsTrue(ShaderVariantRules.UnusedKeyword(keyword, false));
        }

        [Test]
        public void StereoGoesOnlyWhereVrCantRun()
        {
            Assert.IsTrue(ShaderVariantRules.UnusedKeyword("STEREO_INSTANCING_ON", false));
            Assert.IsFalse(ShaderVariantRules.UnusedKeyword("STEREO_INSTANCING_ON", true));
        }

        [TestCase("FOG_LINEAR")]
        [TestCase("_MAIN_LIGHT_SHADOWS")]
        [TestCase("_MAIN_LIGHT_SHADOWS_CASCADE")]
        [TestCase("_SHADOWS_SOFT")]
        [TestCase("_SCREEN_SPACE_OCCLUSION")]
        [TestCase("_ADDITIONAL_LIGHT_SHADOWS")]
        [TestCase("_GOF2_PBR")]
        [TestCase("_EMISSION")]
        public void WhatTheGameUsesStays(string keyword) => Assert.IsFalse(ShaderVariantRules.UnusedKeyword(keyword, false));

        [Test]
        public void OnlyTheTerrainShadersGoWhole()
        {
            Assert.IsTrue(ShaderVariantRules.UnusedShader("Hidden/TerrainEngine/Details/UniversalPipeline/Vertexlit"));
            Assert.IsFalse(ShaderVariantRules.UnusedShader("GoF2/Hull"));
            Assert.IsFalse(ShaderVariantRules.UnusedShader("Universal Render Pipeline/Lit"));
        }

        [TestCase("Linux Vulkan", "LinuxStandaloneSupport", "15000000i")]
        [TestCase("Linux OpenGL", "LinuxStandaloneSupport", "11000000i")]
        [TestCase("Windows D3D12", "WindowsStandaloneSupport", "12000000i")]
        [TestCase("Windows D3D11", "WindowsStandaloneSupport", "02000000i")]
        [TestCase("Android Vulkan", "AndroidPlayer", "15000000i")]
        [TestCase("Android GLES3", "AndroidPlayer", "0b000000i")]
        public void ABackendProfileHoldsItsOneApi(string profile, string platform, string api)
        {
            string text = File.ReadAllText($"Assets/Settings/Build Profiles/{profile}.asset").Replace("\r", "");
            var entry = Regex.Match(text, $@"- m_BuildTarget: {platform}\n    m_APIs: (\w+)\n.*\n    m_Automatic: (\d)");
            Assert.IsTrue(entry.Success, "an override of the Player Settings");
            Assert.AreEqual(api, entry.Groups[1].Value);
            Assert.AreEqual("0", entry.Groups[2].Value, "not Unity's automatic list");
        }

        [TestCase("macOS", 2)]
        [TestCase("iOS", 9)]
        public void AMetalPlatformHasOneProfile(string profile, int target) =>
            StringAssert.Contains($"m_BuildTarget: {target}\n", File.ReadAllText($"Assets/Settings/Build Profiles/{profile}.asset").Replace("\r", ""));

        [Test]
        public void NoPrefabOrSceneUsesAStrippedFeature()
        {
            foreach (string folder in new[] { "Assets/Resources", "Assets/Prefabs", "Assets/Scenes" })
                foreach (string file in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
                {
                    if (!file.EndsWith(".prefab") && !file.EndsWith(".unity")) continue;
                    foreach (string line in File.ReadLines(file))
                    {
                        string text = line.Trim();
                        Assert.IsFalse(text.StartsWith("m_FadeMode: ") && !text.EndsWith(": 0"), file + ": an LODGroup crossfades (LOD_FADE_CROSSFADE)");
                        Assert.IsFalse(text.StartsWith("m_Cookie: {fileID: ") && !text.StartsWith("m_Cookie: {fileID: 0"), file + ": a light has a cookie (_LIGHT_COOKIES)");
                    }
                }
        }
    }
}
