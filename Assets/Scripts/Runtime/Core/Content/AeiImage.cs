// AeiImage.cs
// Feature: docs/notes/player-assets.md "Formats in the player's copy": Galaxy on Fire 2's own image files (.aei, the ABYSS
// engine's "AEimage"), read from the player's copy of the game (Android OBB, iOS .ipa) into RGBA8 pixels. Plain C#, no
// Unity types: the conversion runs on every platform and is tested outside the Editor.
//
// Layout (Reference/tools/asset_conversion/aei.py, and the iOS 1.1.19 / Android 2.0.16 files, 2026-11-15):
//   "AEimage" [7], a byte, type [1], width, height, region count [u16 each], regions [x, y, w, h: u16 each], then
//   - types 0x01 / 0x03 / 0x81: width x height x 4 bytes RGBA8, rows top first (0x81: a cubemap strip, 6 faces stacked)
//   - every other type: a u32 byte count, then that many bytes of compressed data (level 0 first, then its mipmaps)
// Types (bit 1 = mipmaps follow; the decoders read level 0 only, Unity makes its own mipmaps):
//   0x40 / 0x42 (and 0x14 / 0x16 / 0x17, aei.py's): ETC1 (Android)
//   0x10 / 0x12: PVRTC 4 bpp (iOS), 0x0d: PVRTC 2 bpp (iOS)
//   0x20 / 0x22: DXT1, 0x24 / 0x26: DXT5 (the macOS app's high/dx5 textures; DXT5 checked against the Android copy's same
//   textures: colour within 0.1 / 255, alpha equal; iOS' one 0x24 file is its cloak map)

using System;
using System.Collections.Generic;

namespace GoF2Remake.Data
{
    public sealed class AeiImage
    {
        public struct Region { public int x, y, width, height; }

        public int Type;
        public int Width, Height;
        public readonly List<Region> Regions = new List<Region>();
        /// <summary>Level 0 as RGBA8, rows top first (as the original's files and the converted PNGs have them).</summary>
        public byte[] Rgba;

        /// <summary>Whether the type is a cubemap strip (six faces stacked).</summary>
        public bool IsCubemapStrip => Type == 0x81;
        public bool HasMipmaps => (Type & 0x02) != 0 && Type != 0x03 && Type != 0x81;
        public string Format => FormatOf(Type);

        public static string FormatOf(int type)
        {
            switch (type)
            {
                case 0x01: case 0x03: return "RGBA8";
                case 0x81: return "RGBA8 cubemap strip";
                case 0x14: case 0x16: case 0x17: case 0x40: case 0x42: return "ETC1";
                case 0x10: case 0x12: return "PVRTC 4bpp";
                case 0x0d: return "PVRTC 2bpp";
                case 0x20: case 0x22: return "DXT1";
                case 0x24: case 0x26: return "DXT5";
                default: return $"unknown (0x{type:x2})";
            }
        }

        /// <summary>Whether Read can decode this type.</summary>
        public static bool CanRead(int type) => !FormatOf(type).StartsWith("unknown");

        /// <summary>The header alone (type, size, regions), without decoding the pixels.</summary>
        public static AeiImage ReadHeader(byte[] file) => Parse(file, false);

        /// <summary>The image with its level 0 decoded. Throws FormatException on a file that isn't an AEimage or a type
        /// it can't decode.</summary>
        public static AeiImage Read(byte[] file) => Parse(file, true);

        static AeiImage Parse(byte[] b, bool pixels)
        {
            if (b == null || b.Length < 15 || b[0] != (byte)'A' || b[1] != (byte)'E' || b[2] != (byte)'i' || b[3] != (byte)'m'
                || b[4] != (byte)'a' || b[5] != (byte)'g' || b[6] != (byte)'e')
                throw new FormatException("not an AEimage file");
            var img = new AeiImage { Type = b[8], Width = U16(b, 9), Height = U16(b, 11) };
            int n = U16(b, 13), o = 15;
            if (o + 8 * n > b.Length) throw new FormatException("regions run past the end");
            for (int i = 0; i < n; i++, o += 8)
                img.Regions.Add(new Region { x = U16(b, o), y = U16(b, o + 2), width = U16(b, o + 4), height = U16(b, o + 6) });
            if (!pixels) return img;
            int w = img.Width, h = img.Height;
            switch (img.Type)
            {
                case 0x01: case 0x03: case 0x81:
                {
                    long size = (long)w * h * 4;
                    if (o + size > b.Length) throw new FormatException("pixels run past the end");
                    img.Rgba = new byte[size];
                    Buffer.BlockCopy(b, o, img.Rgba, 0, (int)size);
                    return img;
                }
            }
            if (!CanRead(img.Type)) throw new FormatException($"AEimage type 0x{img.Type:x2} isn't supported");
            if (o + 4 > b.Length) throw new FormatException("no data size");
            int count = (int)U32(b, o); o += 4;
            if (count < 0 || o + count > b.Length) throw new FormatException("data runs past the end");
            var data = new ReadOnlySpan<byte>(b, o, count);
            switch (img.Type)
            {
                case 0x10: case 0x12: img.Rgba = Pvrtc.Decode(data, w, h, false); break;
                case 0x0d: img.Rgba = Pvrtc.Decode(data, w, h, true); break;
                case 0x20: case 0x22: img.Rgba = Dxt.Decode(data, w, h, Dxt.Kind.Dxt1); break;
                case 0x24: case 0x26: img.Rgba = Dxt.Decode(data, w, h, Dxt.Kind.Dxt5); break;
                default: img.Rgba = Etc1.Decode(data, w, h); break;
            }
            return img;
        }

        /// <summary>A region's pixels (rows top first) cut out of Rgba.</summary>
        public byte[] Crop(Region r)
        {
            var o = new byte[r.width * r.height * 4];
            for (int y = 0; y < r.height; y++)
                Buffer.BlockCopy(Rgba, ((r.y + y) * Width + r.x) * 4, o, y * r.width * 4, r.width * 4);
            return o;
        }

        static int U16(byte[] b, int o) => b[o] | (b[o + 1] << 8);
        static uint U32(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
    }

    /// <summary>S3TC / DXT (the macOS app's high/dx5 textures): 4x4 blocks, row-major, little-endian. DXT1 8 bytes (two
    /// RGB565 colours, 2-bit indices; colour 0 <= colour 1: three colours and transparent black); DXT3 16 bytes (4-bit
    /// explicit alpha, then a DXT1 colour block in four-colour mode); DXT5 16 bytes (two alpha endpoints and 3-bit indices,
    /// then the colour block).</summary>
#if UNITY_5_3_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]   // (no state)
#endif
    public static class Dxt
    {
        public enum Kind { Dxt1, Dxt3, Dxt5 }

        public static byte[] Decode(ReadOnlySpan<byte> data, int width, int height, Kind kind)
        {
            var o = new byte[width * height * 4];
            int bw = (width + 3) / 4, bh = (height + 3) / 4, size = kind == Kind.Dxt1 ? 8 : 16;
            if (data.Length < bw * bh * size) throw new FormatException("DXT data too short");
            Span<byte> px = stackalloc byte[64];
            Span<byte> alpha = stackalloc byte[16];
            Span<byte> a = stackalloc byte[8];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    var blk = data.Slice((by * bw + bx) * size, size);
                    if (kind == Kind.Dxt3)
                        for (int i = 0; i < 16; i++) { int v = (blk[i / 2] >> ((i & 1) * 4)) & 15; alpha[i] = (byte)(v * 17); }
                    else if (kind == Kind.Dxt5)
                    {
                        a[0] = blk[0]; a[1] = blk[1];
                        if (a[0] > a[1]) for (int i = 1; i < 7; i++) a[i + 1] = (byte)(((7 - i) * a[0] + i * a[1]) / 7);
                        else { for (int i = 1; i < 5; i++) a[i + 1] = (byte)(((5 - i) * a[0] + i * a[1]) / 5); a[6] = 0; a[7] = 255; }
                        ulong bits = 0;
                        for (int i = 0; i < 6; i++) bits |= (ulong)blk[2 + i] << (8 * i);
                        for (int i = 0; i < 16; i++) alpha[i] = a[(int)((bits >> (3 * i)) & 7)];
                    }
                    Color(kind == Kind.Dxt1 ? blk : blk.Slice(8), kind == Kind.Dxt1, px);
                    for (int i = 0; i < 16; i++)
                    {
                        int x = bx * 4 + (i & 3), y = by * 4 + (i >> 2);
                        if (x >= width || y >= height) continue;
                        int d = (y * width + x) * 4;
                        o[d] = px[i * 4]; o[d + 1] = px[i * 4 + 1]; o[d + 2] = px[i * 4 + 2];
                        o[d + 3] = kind == Kind.Dxt1 ? px[i * 4 + 3] : alpha[i];
                    }
                }
            return o;
        }

        static void Color(ReadOnlySpan<byte> b, bool dxt1, Span<byte> px)
        {
            int c0 = b[0] | b[1] << 8, c1 = b[2] | b[3] << 8;
            Span<int> r = stackalloc int[4], g = stackalloc int[4], bl = stackalloc int[4], a = stackalloc int[4];
            Rgb565(c0, out r[0], out g[0], out bl[0]); Rgb565(c1, out r[1], out g[1], out bl[1]);
            a[0] = a[1] = a[2] = a[3] = 255;
            if (c0 > c1 || !dxt1)
            {
                r[2] = (2 * r[0] + r[1]) / 3; g[2] = (2 * g[0] + g[1]) / 3; bl[2] = (2 * bl[0] + bl[1]) / 3;
                r[3] = (r[0] + 2 * r[1]) / 3; g[3] = (g[0] + 2 * g[1]) / 3; bl[3] = (bl[0] + 2 * bl[1]) / 3;
            }
            else
            {
                r[2] = (r[0] + r[1]) / 2; g[2] = (g[0] + g[1]) / 2; bl[2] = (bl[0] + bl[1]) / 2;
                r[3] = g[3] = bl[3] = 0; a[3] = 0;
            }
            uint idx = (uint)(b[4] | b[5] << 8 | b[6] << 16 | b[7] << 24);
            for (int i = 0; i < 16; i++)
            {
                int k = (int)((idx >> (2 * i)) & 3);
                px[i * 4] = (byte)r[k]; px[i * 4 + 1] = (byte)g[k]; px[i * 4 + 2] = (byte)bl[k]; px[i * 4 + 3] = (byte)a[k];
            }
        }

        static void Rgb565(int c, out int r, out int g, out int b)
        {
            r = (c >> 11) & 31; g = (c >> 5) & 63; b = c & 31;
            r = (r << 3) | (r >> 2); g = (g << 2) | (g >> 4); b = (b << 3) | (b >> 2);
        }
    }

    /// <summary>ETC1 (Ericsson Texture Compression 1, OES_compressed_ETC1_RGB8_texture): 4x4 blocks of 8 bytes, big-endian,
    /// row-major; individual or differential base colours per 2x4 / 4x2 half, an 8-row intensity table.</summary>
#if UNITY_5_3_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]   // (only constant tables: nothing to reset)
#endif
    public static class Etc1
    {
        static readonly int[,] Table =
        {
            { 2, 8, -2, -8 }, { 5, 17, -5, -17 }, { 9, 29, -9, -29 }, { 13, 42, -13, -42 },
            { 18, 60, -18, -60 }, { 24, 80, -24, -80 }, { 33, 106, -33, -106 }, { 47, 183, -47, -183 },
        };

        public static byte[] Decode(ReadOnlySpan<byte> data, int width, int height)
        {
            var o = new byte[width * height * 4];
            int bw = (width + 3) / 4, bh = (height + 3) / 4;
            if (data.Length < bw * bh * 8) throw new FormatException("ETC1 data too short");
            for (int by = 0; by < bh; by++)
            for (int bx = 0; bx < bw; bx++)
            {
                int p = (by * bw + bx) * 8;
                uint hi = (uint)(data[p] << 24 | data[p + 1] << 16 | data[p + 2] << 8 | data[p + 3]);
                uint lo = (uint)(data[p + 4] << 24 | data[p + 5] << 16 | data[p + 6] << 8 | data[p + 7]);
                bool diff = (hi & 2) != 0, flip = (hi & 1) != 0;
                int r1, g1, b1, r2, g2, b2;
                if (diff)
                {
                    int r = (int)(hi >> 27) & 31, g = (int)(hi >> 19) & 31, b = (int)(hi >> 11) & 31;
                    int dr = Signed3((int)(hi >> 24) & 7), dg = Signed3((int)(hi >> 16) & 7), db = Signed3((int)(hi >> 8) & 7);
                    r1 = Ext5(r); g1 = Ext5(g); b1 = Ext5(b);
                    r2 = Ext5((r + dr) & 31); g2 = Ext5((g + dg) & 31); b2 = Ext5((b + db) & 31);
                }
                else
                {
                    r1 = Ext4((int)(hi >> 28) & 15); r2 = Ext4((int)(hi >> 24) & 15);
                    g1 = Ext4((int)(hi >> 20) & 15); g2 = Ext4((int)(hi >> 16) & 15);
                    b1 = Ext4((int)(hi >> 12) & 15); b2 = Ext4((int)(hi >> 8) & 15);
                }
                int t1 = (int)(hi >> 5) & 7, t2 = (int)(hi >> 2) & 7;
                for (int x = 0; x < 4; x++)
                for (int y = 0; y < 4; y++)
                {
                    int px = bx * 4 + x, py = by * 4 + y;
                    if (px >= width || py >= height) continue;
                    int i = x * 4 + y;
                    int idx = (int)((lo >> (i + 15)) & 2) | (int)((lo >> i) & 1);
                    bool second = flip ? y >= 2 : x >= 2;
                    int m = Table[second ? t2 : t1, idx];
                    int q = (py * width + px) * 4;
                    o[q] = Clamp((second ? r2 : r1) + m); o[q + 1] = Clamp((second ? g2 : g1) + m);
                    o[q + 2] = Clamp((second ? b2 : b1) + m); o[q + 3] = 255;
                }
            }
            return o;
        }

        static int Signed3(int v) => v >= 4 ? v - 8 : v;
        static int Ext4(int v) => v << 4 | v;
        static int Ext5(int v) => v << 3 | v >> 2;
        static byte Clamp(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
    }

    /// <summary>PVRTC 4 bpp / 2 bpp (PowerVR, iOS): 64-bit words (modulation, then colours A and B) of 4x4 / 8x4 pixels in
    /// Morton order, each pixel blended between two bilinearly upscaled low-resolution images; after the PowerVR SDK's
    /// reference decompressor (PVRTDecompress).</summary>
#if UNITY_5_3_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]   // (only constant tables: nothing to reset)
#endif
    public static class Pvrtc
    {
        struct Word { public uint mod, col; }

        public static byte[] Decode(ReadOnlySpan<byte> data, int width, int height, bool twoBpp)
        {
            int ww = twoBpp ? 8 : 4, wh = 4;
            int xw = Math.Max(width / ww, 2), yw = Math.Max(height / wh, 2);
            int pw = xw * ww, ph = yw * wh;   // the padded size the words cover
            if (data.Length < xw * yw * 8) throw new FormatException("PVRTC data too short");
            var words = new uint[xw * yw * 2];
            for (int i = 0; i < words.Length; i++)
                words[i] = (uint)(data[i * 4] | data[i * 4 + 1] << 8 | data[i * 4 + 2] << 16 | data[i * 4 + 3] << 24);
            var full = new byte[pw * ph * 4];
            var modV = new int[16, 8]; var modM = new int[16, 8];
            var ua = new int[32 * 4]; var ub = new int[32 * 4];
            for (int wy = -1; wy < yw - 1; wy++)
            for (int wx = -1; wx < xw - 1; wx++)
            {
                int px = Wrap(xw, wx), py = Wrap(yw, wy), qx = Wrap(xw, wx + 1), ry = Wrap(yw, wy + 1);
                Word P = Get(words, xw, yw, px, py), Q = Get(words, xw, yw, qx, py),
                     R = Get(words, xw, yw, px, ry), S = Get(words, xw, yw, qx, ry);
                Unpack(P, 0, 0, modV, modM, twoBpp); Unpack(Q, ww, 0, modV, modM, twoBpp);
                Unpack(R, 0, wh, modV, modM, twoBpp); Unpack(S, ww, wh, modV, modM, twoBpp);
                Interpolate(ColorA(P.col), ColorA(Q.col), ColorA(R.col), ColorA(S.col), ua, twoBpp);
                Interpolate(ColorB(P.col), ColorB(Q.col), ColorB(R.col), ColorB(S.col), ub, twoBpp);
                for (int y = 0; y < wh; y++)
                for (int x = 0; x < ww; x++)
                {
                    int mod = Modulation(modV, modM, x + ww / 2, y + wh / 2, twoBpp);
                    bool punch = false;
                    if (mod > 10) { punch = true; mod -= 10; }
                    int k = (y * ww + x) * 4;
                    // the pixel's place: P's lower right quarter, Q's lower left, R's upper right, S's upper left
                    int ox, oy;
                    if (y < wh / 2) { oy = py * wh + y + wh / 2; ox = x < ww / 2 ? px * ww + x + ww / 2 : qx * ww + x - ww / 2; }
                    else { oy = ry * wh + y - wh / 2; ox = x < ww / 2 ? px * ww + x + ww / 2 : qx * ww + x - ww / 2; }
                    int q = (oy * pw + ox) * 4;
                    for (int c = 0; c < 4; c++)
                        full[q + c] = (byte)Math.Clamp((ua[k + c] * (8 - mod) + ub[k + c] * mod) / 8, 0, 255);
                    if (punch) full[q + 3] = 0;
                }
            }
            if (pw == width && ph == height) return full;
            var o = new byte[width * height * 4];
            for (int y = 0; y < height; y++) Buffer.BlockCopy(full, y * pw * 4, o, y * width * 4, width * 4);
            return o;
        }

        static int Wrap(int n, int i) => ((i % n) + n) % n;

        static Word Get(uint[] words, int xw, int yw, int x, int y)
        {
            int i = (int)Twiddle(xw, yw, x, y) * 2;
            return new Word { mod = words[i], col = words[i + 1] };
        }

        /// <summary>Morton order of word (x, y): y in the even bits, x in the odd ones over the smaller dimension, the
        /// larger dimension's remaining bits above (x in the even bits put each word at its transposed place: the iOS
        /// files decoded as their Android counterparts mirrored about the diagonal, 2026-11-15).</summary>
        static uint Twiddle(int xw, int yw, int x, int y)
        {
            int min = Math.Min(xw, yw);
            uint t = 0; int src = 1, dst = 1, shift = 0;
            while (src < min)
            {
                if ((y & src) != 0) t |= (uint)dst;
                if ((x & src) != 0) t |= (uint)(dst << 1);
                src <<= 1; dst <<= 2; shift++;
            }
            int max = (xw >= yw ? x : y) >> shift;
            return t | (uint)(max << (2 * shift));
        }

        // colours: 5-bit RGB (4-bit alpha, 15 = opaque); A from bits 0..15, B from 16..31
        static (int r, int g, int b, int a) ColorA(uint c)
        {
            if ((c & 0x8000) != 0)
                return ((int)((c & 0x7c00) >> 10), (int)((c & 0x3e0) >> 5), (int)((c & 0x1e) | ((c & 0x1e) >> 4)), 0xf);
            return ((int)(((c & 0xf00) >> 7) | ((c & 0xf00) >> 11)), (int)(((c & 0xf0) >> 3) | ((c & 0xf0) >> 7)),
                    (int)(((c & 0xe) << 1) | ((c & 0xe) >> 2)), (int)((c & 0x7000) >> 11));
        }

        static (int r, int g, int b, int a) ColorB(uint c)
        {
            if ((c & 0x80000000) != 0)
                return ((int)((c & 0x7c000000) >> 26), (int)((c & 0x3e00000) >> 21), (int)((c & 0x1f0000) >> 16), 0xf);
            return ((int)(((c & 0xf000000) >> 23) | ((c & 0xf000000) >> 27)), (int)(((c & 0xf00000) >> 19) | ((c & 0xf00000) >> 23)),
                    (int)(((c & 0xf0000) >> 15) | ((c & 0xf0000) >> 19)), (int)((c & 0x70000000) >> 27));
        }

        /// <summary>The four words' colours bilinearly upscaled over the word grid between their centres, as 8-bit channels.</summary>
        static void Interpolate((int r, int g, int b, int a) P, (int r, int g, int b, int a) Q, (int r, int g, int b, int a) R,
                                (int r, int g, int b, int a) S, int[] o, bool twoBpp)
        {
            int ww = twoBpp ? 8 : 4, wh = 4;
            int[] hp = { P.r * ww, P.g * ww, P.b * ww, P.a * ww }, hr = { R.r * ww, R.g * ww, R.b * ww, R.a * ww };
            int[] qp = { Q.r - P.r, Q.g - P.g, Q.b - P.b, Q.a - P.a }, sr = { S.r - R.r, S.g - R.g, S.b - R.b, S.a - R.a };
            var res = new int[4]; var dy = new int[4];
            for (int x = 0; x < ww; x++)
            {
                for (int c = 0; c < 4; c++) { res[c] = 4 * hp[c]; dy[c] = hr[c] - hp[c]; }
                for (int y = 0; y < wh; y++)
                {
                    int k = (y * ww + x) * 4;
                    if (twoBpp)
                    {
                        for (int c = 0; c < 3; c++) o[k + c] = (res[c] >> 7) + (res[c] >> 2);
                        o[k + 3] = (res[3] >> 5) + (res[3] >> 1);
                    }
                    else
                    {
                        for (int c = 0; c < 3; c++) o[k + c] = (res[c] >> 6) + (res[c] >> 1);
                        o[k + 3] = (res[3] >> 4) + res[3];
                    }
                    for (int c = 0; c < 4; c++) res[c] += dy[c];
                }
                for (int c = 0; c < 4; c++) { hp[c] += qp[c]; hr[c] += sr[c]; }
            }
        }

        /// <summary>A word's modulation values into the 2x2-word grid at (ox, oy): 4 bpp, 0..8 weights (+10: punch-through
        /// alpha); 2 bpp, the stored 2-bit values and the word's mode (0 one bit a pixel, 1 H+V, 2 H, 3 V interpolated).</summary>
        static void Unpack(Word w, int ox, int oy, int[,] v, int[,] m, bool twoBpp)
        {
            uint mode = w.col & 1, bits = w.mod;
            if (twoBpp)
            {
                if (mode != 0)
                {
                    if ((bits & 1) != 0)
                    {
                        mode = (bits & (1u << 20)) != 0 ? 3u : 2u;
                        if ((bits & (1u << 21)) != 0) bits |= 1u << 20; else bits &= ~(1u << 20);
                    }
                    if ((bits & 2) != 0) bits |= 1; else bits &= ~1u;
                    for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 8; x++)
                    {
                        m[x + ox, y + oy] = (int)mode;
                        if (((x ^ y) & 1) == 0) { v[x + ox, y + oy] = (int)(bits & 3); bits >>= 2; }
                    }
                }
                else
                {
                    for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 8; x++)
                    {
                        m[x + ox, y + oy] = 0;
                        v[x + ox, y + oy] = (bits & 1) != 0 ? 3 : 0;
                        bits >>= 1;
                    }
                }
                return;
            }
            for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
            {
                int b = (int)(bits & 3); bits >>= 2;
                if (mode != 0) b = b == 0 ? 0 : b == 1 ? 4 : b == 2 ? 14 : 8;   // punch-through: 2 = half weight, alpha 0
                else { b *= 3; if (b > 3) b -= 1; }                               // 0, 3, 5, 8
                v[x + ox, y + oy] = b;
            }
        }

        static readonly int[] Rep = { 0, 3, 5, 8 };

        static int Modulation(int[,] v, int[,] m, int x, int y, bool twoBpp)
        {
            if (!twoBpp) return v[x, y];
            if (m[x, y] == 0 || ((x ^ y) & 1) == 0) return Rep[v[x, y]];
            if (m[x, y] == 1) return (Rep[v[x, y - 1]] + Rep[v[x, y + 1]] + Rep[v[x - 1, y]] + Rep[v[x + 1, y]] + 2) / 4;
            if (m[x, y] == 2) return (Rep[v[x - 1, y]] + Rep[v[x + 1, y]] + 1) / 2;
            return (Rep[v[x, y - 1]] + Rep[v[x, y + 1]] + 1) / 2;
        }
    }
}
