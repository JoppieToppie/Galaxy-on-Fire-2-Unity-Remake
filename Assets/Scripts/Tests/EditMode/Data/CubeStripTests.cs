// CubeStripTests.cs
// Feature: docs/notes/player-assets.md "Skies": a 6-face strip made into a Cubemap at run time (OriginalSkies.FromStrip:
// the reflections' vertical strips, the rendered skies' horizontal ones) is the cubemap Unity's importer makes of the same
// strip (the Editor's skies and reflections were imported so): each face, pixel for pixel.

using System.IO;
using GoF2Remake.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class CubeStripTests
    {
        const int S = 16;
        const string Dir = "Assets/TmpCubeStripTest";

        /// <summary>A strip whose every pixel tells its face and place: R = x, G = y (in the face, from the strip image's top
        /// left), B = the face.</summary>
        static Texture2D Strip(bool horizontal)
        {
            var t = new Texture2D(horizontal ? S * 6 : S, horizontal ? S : S * 6, TextureFormat.RGBA32, false);
            for (int f = 0; f < 6; f++)
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        int ix = horizontal ? f * S + x : x, iyTop = horizontal ? y : f * S + y;
                        t.SetPixel(ix, t.height - 1 - iyTop, new Color32((byte)(x * 16), (byte)(y * 16), (byte)(f * 40), 255));
                    }
            t.Apply();
            return t;
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AStripIsTheCubemapTheImporterMakes(bool horizontal)
        {
            Directory.CreateDirectory(Dir);
            string path = $"{Dir}/strip_{(horizontal ? "h" : "v")}.png";
            try
            {
                var strip = Strip(horizontal);
                var png = strip.EncodeToPNG();
                File.WriteAllBytes(path, png);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.textureShape = TextureImporterShape.TextureCube;
                ti.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
                ti.mipmapEnabled = false;
                ti.isReadable = true;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.sRGBTexture = false;
                ti.filterMode = FilterMode.Point;
                ti.SaveAndReimport();
                var imported = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                Assert.IsNotNull(imported, "imported as a cubemap");
                var made = OriginalSkies.FromStrip(png, "test", linear: true, mipmaps: false);
                Assert.IsNotNull(made);
                for (int f = 0; f < 6; f++)
                {
                    var a = imported.GetPixels((CubemapFace)f);
                    var b = made.GetPixels((CubemapFace)f);
                    for (int i = 0; i < a.Length; i++)
                        Assert.AreEqual((Color32)a[i], (Color32)b[i], $"face {(CubemapFace)f}, pixel {i % S}, {i / S}");
                }
                Object.DestroyImmediate(made);
            }
            finally
            {
                AssetDatabase.DeleteAsset(Dir);
            }
        }
    }
}
