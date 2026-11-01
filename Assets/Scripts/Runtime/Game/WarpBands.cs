// WarpBands.cs
// Remake: the two images GoF2/SpaceSky shades the warp with (Visuals.WarpSky, a mod's warp.json replaces them), made from
// nothing but a seed: random phases under a chosen spectrum, through an inverse FFT, which tiles both ways by construction.
//   Tunnel  soft blobs, a few to the tile, centred on grey
//   Detail  veins: bright where two fields cross zero, bent by a slow warp, over a wider cloud; mostly dark, finer across v
// Plain data, so the tests can read their levels; WarpSky makes the textures once a run.

using System;
using UnityEngine;

namespace GoF2Remake.Data
{
    public static class WarpBands
    {
        public const int TunnelSize = 128, DetailSize = 256;

        /// <summary>Tunnel's levels about grey, and Detail's (the mean and spread of its logarithm).</summary>
        const float TunnelGrey = 0.5f, TunnelSpread = 0.131f, DetailLog = -1.21f, DetailSpread = 0.33f;
        /// <summary>Detail's features are this much wider along u than along v.</summary>
        const float Squash = 0.65f;
        /// <summary>How far the warp bends the veins, in pixels of DetailSize.</summary>
        const float Bend = 5f;

        public static byte[] Tunnel(int seed = 1)
        {
            var field = Field(TunnelSize, seed, 1.6f, 5f, 1f, 0f);
            var bytes = new byte[field.Length];
            for (int i = 0; i < field.Length; i++) bytes[i] = Level(TunnelGrey + TunnelSpread * field[i]);
            return bytes;
        }

        public static byte[] Detail(int seed = 2)
        {
            int n = DetailSize;
            float[] warpX = Field(n, seed + 10, 1.5f, 3f, 1f, 0f), warpY = Field(n, seed + 11, 1.5f, 3f, 1f, 0f);
            float[] wide = Warped(Field(n, seed, 3f, 11f, Squash, 0.2f), warpX, warpY, n);
            float[] fine = Warped(Field(n, seed + 1, 6f, 15f, Squash, 0.2f), warpX, warpY, n);
            float[] cloud = Warped(Field(n, seed + 2, 1.5f, 6f, Squash, 0f), warpX, warpY, n);
            var veins = new float[n * n];
            for (int i = 0; i < veins.Length; i++)
                veins[i] = (Mathf.Exp(-Mathf.Abs(wide[i]) / 0.7f) + 0.75f * Mathf.Exp(-Mathf.Abs(fine[i]) / 0.4f)) * (1f + 0.25f * cloud[i]) + 0.35f * cloud[i];
            Normalise(veins);
            var bytes = new byte[veins.Length];
            for (int i = 0; i < veins.Length; i++) bytes[i] = Level(Mathf.Exp(DetailLog + DetailSpread * veins[i]));
            return bytes;
        }

        static byte Level(float value) => (byte)(Mathf.Clamp01(value) * 255f + 0.5f);

        /// <summary>Noise of mean 0 and spread 1 between the wave numbers 'low' and 'high' (cycles to the tile), falling by
        /// 'slope' above them; 'squash' under 1 takes v's waves as lower than they are, so v gets finer.</summary>
        static float[] Field(int size, int seed, float low, float high, float squash, float slope)
        {
            var random = new System.Random(seed);
            float[] re = new float[size * size], im = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float kx = x < size / 2 ? x : x - size, ky = (y < size / 2 ? y : y - size) * squash;
                    float k = Mathf.Sqrt(kx * kx + ky * ky), a = k / low, b = k / high;
                    float gain = Mathf.Sqrt((1f - Mathf.Exp(-a * a * a * a)) * Mathf.Exp(-b * b * b * b)) / Mathf.Pow(1f + k, slope);
                    float phase = (float)(random.NextDouble() * 2.0 * Math.PI);
                    re[y * size + x] = gain * Mathf.Cos(phase);
                    im[y * size + x] = gain * Mathf.Sin(phase);
                }
            float[] cos = new float[size / 2], sin = new float[size / 2];
            for (int i = 0; i < size / 2; i++)
            {
                cos[i] = (float)Math.Cos(2.0 * Math.PI * i / size);
                sin[i] = (float)Math.Sin(2.0 * Math.PI * i / size);
            }
            for (int y = 0; y < size; y++) Fft(re, im, y * size, 1, size, cos, sin);
            for (int x = 0; x < size; x++) Fft(re, im, x, size, size, cos, sin);
            Normalise(re);
            return re;
        }

        /// <summary>One line of the image, in place: radix 2, 'size' a power of two. The phases are random, so the sign of the
        /// transform and its scale don't matter.</summary>
        static void Fft(float[] re, float[] im, int start, int stride, int size, float[] cos, float[] sin)
        {
            for (int i = 1, j = 0; i < size; i++)
            {
                int bit = size >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i >= j) continue;
                int a = start + i * stride, b = start + j * stride;
                (re[a], re[b]) = (re[b], re[a]);
                (im[a], im[b]) = (im[b], im[a]);
            }
            for (int length = 2; length <= size; length <<= 1)
                for (int i = 0; i < size; i += length)
                    for (int j = 0; j < length / 2; j++)
                    {
                        int a = start + (i + j) * stride, b = a + length / 2 * stride, turn = j * (size / length);
                        float tr = re[b] * cos[turn] - im[b] * sin[turn], ti = re[b] * sin[turn] + im[b] * cos[turn];
                        re[b] = re[a] - tr;
                        im[b] = im[a] - ti;
                        re[a] += tr;
                        im[a] += ti;
                    }
        }

        /// <summary>'field' read through the warp, bilinear and wrapped, so it still tiles.</summary>
        static float[] Warped(float[] field, float[] warpX, float[] warpY, int size)
        {
            var bent = new float[field.Length];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int i = y * size + x;
                    float u = x + warpX[i] * Bend, v = y + warpY[i] * Bend;
                    int cx = Mathf.FloorToInt(u), cy = Mathf.FloorToInt(v);
                    float fx = u - cx, fy = v - cy;
                    int x0 = (cx % size + size) % size, y0 = (cy % size + size) % size, x1 = (x0 + 1) % size, y1 = (y0 + 1) % size;
                    float a = Mathf.Lerp(field[y0 * size + x0], field[y0 * size + x1], fx);
                    float b = Mathf.Lerp(field[y1 * size + x0], field[y1 * size + x1], fx);
                    bent[i] = Mathf.Lerp(a, b, fy);
                }
            return bent;
        }

        static void Normalise(float[] values)
        {
            double sum = 0.0, squares = 0.0;
            foreach (float v in values) sum += v;
            double mean = sum / values.Length;
            foreach (float v in values) squares += (v - mean) * (v - mean);
            float scale = 1f / (float)Math.Sqrt(squares / values.Length);
            for (int i = 0; i < values.Length; i++) values[i] = (values[i] - (float)mean) * scale;
        }
    }
}
