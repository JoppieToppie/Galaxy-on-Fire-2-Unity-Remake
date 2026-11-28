// RecorderYuvTests.cs
// Feature: docs/notes/platforms.md "Screen recorder": the recorder's shader (Resources/GoF2Recorder/RecorderYuv) packs a
// picture into I420 as the encoders take it: the Y plane with the image's top row first, then U, then V, BT.709 limited
// range, read back without a CPU flip. A picture red at the top left and black elsewhere, through the GPU.
// Expected values: BT.709 limited: red Y = 16 + 219 x 0.2126 = 62.6, U = 128 - 224 x 0.1146 = 102.3, V = 128 + 112 = 240;
// black Y 16, U and V 128.

using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace GoF2Remake.Tests
{
    public class RecorderYuvTests
    {
        /// <summary>A W x H picture of PQ codes (an HDR screen's copy): 'topLeft' in the top left quarter, 'rest' elsewhere,
        /// packed by the shader in mode 'hdr' into a 16-bit (HDR10) or 8-bit target; the samples read back.</summary>
        static ushort[] PackHdr(int hdr, Color topLeft, Color rest, out int w, out int h)
        {
            w = 64; h = 32;
            var shader = Resources.Load<Shader>("GoF2Recorder/RecorderYuv");
            var source = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) pixels[y * w + x] = x < w / 2 && y >= h / 2 ? topLeft : rest;   // (row 0 the bottom)
            source.SetPixels(pixels);
            source.Apply();
            var material = new Material(shader);
            material.SetVector("_Size", new Vector4(w, h, 0f, 0f));
            material.SetFloat("_Flip", 0f);
            material.SetFloat("_Linear", 0f);
            material.SetFloat("_Hdr", hdr);
            material.SetFloat("_PaperWhite", 200f);
            material.SetFloat("_Peak", 1000f);
            material.SetTexture("_MainTex", source);
            bool wide = hdr == 1;
            var target = new RenderTexture(w, h * 3 / 2, 0, wide ? RenderTextureFormat.R16 : RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
            try
            {
                Graphics.Blit(source, target, material, 0);
                var request = AsyncGPUReadback.Request(target, 0, wide ? TextureFormat.R16 : TextureFormat.R8);
                request.WaitForCompletion();
                Assert.IsFalse(request.hasError);
                if (wide) return request.GetData<ushort>().ToArray();
                var bytes = request.GetData<byte>();
                var result = new ushort[bytes.Length];
                for (int i = 0; i < bytes.Length; i++) result[i] = bytes[i];
                return result;
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(material);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        static float NitsToPq(float nits)
        {
            float y = Mathf.Pow(nits / 10000f, 0.1593017578125f);
            return Mathf.Pow((0.8359375f + 18.8515625f * y) / (1f + 18.6875f * y), 78.84375f);
        }

        /// <summary>HDR10: BT.2020 10-bit limited, the code in the 16-bit sample's high bits (P010's). A PQ red 0.75: Y' 0.197,
        /// Y 64 + 876 Y' = 237, Cb 512 - 896 x 0.197 / 1.8814 = 418, Cr 512 + 896 x 0.553 / 1.4746 = 848; PQ black Y 64.</summary>
        [Test]
        public void AnHdrPictureIsPackedAsTenBitBt2020()
        {
            var d = PackHdr(1, new Color(0.75f, 0f, 0f, 1f), new Color(0f, 0f, 0f, 1f), out int w, out int h);
            Assert.AreEqual(w * h * 3 / 2, d.Length);
            Assert.AreEqual(237 << 6, d[0], "Y: red at the top left");
            Assert.AreEqual(64 << 6, d[w - 1], "Y: black at the top right");
            Assert.AreEqual(64 << 6, d[(h - 1) * w], "Y: black at the bottom left");
            int u = w * h, v = u + w * h / 4;
            Assert.AreEqual(418 << 6, d[u], "U");
            Assert.AreEqual(848 << 6, d[v], "V");
            Assert.AreEqual(512 << 6, d[d.Length - 1], "V: neutral at the bottom right");
        }

        /// <summary>SDR from an HDR screen: paper white (200 nits here) comes out near SDR's white, the shoulder's 0.926,
        /// sRGB 0.967, Y 16 + 219 x 0.967 = 228; black stays black.</summary>
        [Test]
        public void AnHdrPictureMadeSdrKeepsPaperWhiteNearWhite()
        {
            float pq = NitsToPq(200f);
            var d = PackHdr(2, new Color(pq, pq, pq, 1f), new Color(0f, 0f, 0f, 1f), out int w, out int h);
            Assert.AreEqual(228, d[0], 1, "paper white");
            Assert.AreEqual(16, d[w - 1], 1, "black");
        }

        [Test]
        public void ThePictureIsPackedAsI420TopRowFirst()
        {
            const int W = 64, H = 32;
            var shader = Resources.Load<Shader>("GoF2Recorder/RecorderYuv");
            Assert.IsNotNull(shader);
#if UNITY_EDITOR
            Assert.AreEqual(0, UnityEditor.ShaderUtil.GetShaderMessages(shader).Length, "the shader compiles");
#endif
            var source = new Texture2D(W, H, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            var pixels = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    pixels[y * W + x] = x < W / 2 && y >= H / 2 ? new Color32(255, 0, 0, 255) : new Color32(0, 0, 0, 255);   // (row 0 the bottom)
            source.SetPixels32(pixels);
            source.Apply();
            var material = new Material(shader);
            material.SetVector("_Size", new Vector4(W, H, 0f, 0f));
            material.SetFloat("_Flip", 0f);
            material.SetTexture("_MainTex", source);   // as ScreenRecorder does
            var target = new RenderTexture(W, H * 3 / 2, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
            try
            {
                Graphics.Blit(source, target, material, 0);   // (the packing pass; pass -1 would run the thumbnail pass over it)
                var request = AsyncGPUReadback.Request(target, 0, TextureFormat.R8);
                request.WaitForCompletion();
                Assert.IsFalse(request.hasError);
                var data = request.GetData<byte>();
                Assert.AreEqual(W * H * 3 / 2, data.Length);
                Assert.AreEqual(63, data[0], 1, "Y: the top row starts red");
                Assert.AreEqual(16, data[W - 1], 1, "Y: black at the top right");
                Assert.AreEqual(16, data[(H - 1) * W], 1, "Y: black at the bottom left");
                int u = W * H, v = u + W * H / 4;
                Assert.AreEqual(102, data[u], 1, "U: red's at the top left");
                Assert.AreEqual(240, data[v], 1, "V: red's at the top left");
                Assert.AreEqual(128, data[v - 1], 1, "U: neutral at the bottom right");
                Assert.AreEqual(128, data[data.Length - 1], 1, "V: neutral at the bottom right");
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(material);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }
    }
}
