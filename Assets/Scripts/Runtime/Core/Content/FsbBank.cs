// FsbBank.cs
// Feature: docs/notes/player-assets.md "Importers so far": Galaxy on Fire 2's own sound banks (FMOD_GOF2_<bank>.fsb, FMOD's
// FSB5) read from the player's copy of the game, each sample as a file the game loads like a mod's sound (ModAudio: .mp3,
// .wav). Plain C#, no Unity types. Both copies use three codecs only (2026-11-15): MPEG (voices, music: the MP3 frames are
// kept as they are, the bank's padding between them dropped), IMA ADPCM (effects: decoded to 16-bit PCM) and PCM16.
//
// FSB5 (after vgmstream's fsb5.c and FMOD's own layout): "FSB5", version, sample count, sample header size, name table size,
// data size, codec (u32 each), then 0x3C (version 1) or 0x40 bytes of header in all; per sample a u64 (bit 0 more chunks
// follow, bits 1-4 the rate index, bit 5 stereo, bits 6-33 the data offset / 16, bits 34-63 the sample count) and its
// chunks (u32: bit 0 another follows, bits 1-24 size, 25-31 type: 1 channels, 2 rate, 3 loop); the name table (an offset
// per sample, then the names); the data.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GoF2Remake.Data
{
#if UNITY_5_3_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]   // (only constant tables: nothing to reset)
#endif
    public sealed class FsbBank
    {
        public const int CodecPcm16 = 2, CodecImaAdpcm = 7, CodecMpeg = 11;

        public sealed class Sample
        {
            public string Name;
            public int Frequency, Channels;
            public long SampleCount;
            public int LoopStart = -1, LoopEnd = -1;
            public int DataOffset, DataLength;   // within the bank's data
        }

        public int Version, Codec;
        public readonly List<Sample> Samples = new List<Sample>();
        byte[] file; int dataStart;

        static readonly int[] Rates = { 4000, 8000, 11000, 11025, 16000, 22050, 24000, 32000, 44100, 48000, 96000 };

        public static FsbBank Read(byte[] b)
        {
            if (b == null || b.Length < 0x3C || b[0] != 'F' || b[1] != 'S' || b[2] != 'B' || b[3] != '5')
                throw new FormatException("not an FSB5 bank");
            var bank = new FsbBank { file = b, Version = I32(b, 4), Codec = I32(b, 0x18) };
            int count = I32(b, 8), headers = I32(b, 12), names = I32(b, 16), dataSize = I32(b, 20);
            int head = bank.Version == 0 ? 0x40 : 0x3C;
            bank.dataStart = head + headers + names;
            if (bank.dataStart > b.Length) throw new FormatException("the bank's tables run past its end");
            int o = head;
            for (int i = 0; i < count; i++)
            {
                ulong raw = U64(b, o); o += 8;
                var s = new Sample
                {
                    Frequency = Rates[Math.Min((int)((raw >> 1) & 0xF), Rates.Length - 1)],
                    Channels = (int)((raw >> 5) & 1) + 1,
                    DataOffset = (int)(((raw >> 6) & 0x0FFFFFFF) * 16),
                    SampleCount = (long)((raw >> 34) & 0x3FFFFFFF),
                };
                bool more = (raw & 1) != 0;
                while (more)
                {
                    uint c = (uint)I32(b, o); o += 4;
                    more = (c & 1) != 0;
                    int size = (int)((c >> 1) & 0xFFFFFF), type = (int)((c >> 25) & 0x7F);
                    if (type == 1) s.Channels = b[o];
                    else if (type == 2) s.Frequency = I32(b, o);
                    else if (type == 3) { s.LoopStart = I32(b, o); s.LoopEnd = I32(b, o + 4); }
                    o += size;
                }
                bank.Samples.Add(s);
            }
            for (int i = 0; i < count; i++)
            {
                var s = bank.Samples[i];
                int end = i + 1 < count ? bank.Samples[i + 1].DataOffset : dataSize;
                s.DataLength = end - s.DataOffset;
                if (names > 0)
                {
                    int nb = head + headers, at = nb + I32(b, nb + 4 * i), z = at;
                    while (z < b.Length && b[z] != 0) z++;
                    s.Name = Encoding.UTF8.GetString(b, at, z - at);
                }
                else s.Name = "sample_" + i;
            }
            return bank;
        }

        /// <summary>The extension Extract writes for this bank's codec (".mp3", ".wav"), null for a codec it can't read.</summary>
        public string Extension => Codec == CodecMpeg ? ".mp3" : Codec == CodecImaAdpcm || Codec == CodecPcm16 ? ".wav" : null;

        public ReadOnlySpan<byte> Data(Sample s) => new ReadOnlySpan<byte>(file, dataStart + s.DataOffset, s.DataLength);

        /// <summary>The sample as a playable file: MPEG its frames, IMA ADPCM / PCM16 a 16-bit WAV.</summary>
        public byte[] Extract(Sample s)
        {
            switch (Codec)
            {
                case CodecMpeg: return MpegFrames(Data(s));
                case CodecPcm16:   // (its data is padded to 16 / 32 bytes: the sample count ends it)
                {
                    var d = Data(s);
                    long n = s.SampleCount > 0 ? Math.Min(d.Length, s.SampleCount * s.Channels * 2) : d.Length;
                    return Wav(d.Slice(0, (int)n).ToArray(), s.Channels, s.Frequency);
                }
                case CodecImaAdpcm: return Wav(XboxIma(Data(s), s.Channels, s.SampleCount), s.Channels, s.Frequency);
                default: throw new NotSupportedException($"FSB5 codec {Codec}");
            }
        }

        // ---- MPEG: the frames back to back, the bank's zero padding between them left out --------------------------

        static readonly int[,] Bitrates =
        {
            { 0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448 },   // MPEG-1 layer I
            { 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384 },      // MPEG-1 layer II
            { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 },       // MPEG-1 layer III
            { 0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256 },      // MPEG-2 layer I
            { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 },           // MPEG-2 layers II / III
        };
        static readonly int[,] SampleRates = { { 44100, 48000, 32000 }, { 22050, 24000, 16000 }, { 11025, 12000, 8000 } };

        /// <summary>The length of the MPEG audio frame starting at 'o', 0 when there is none there.</summary>
        public static int FrameLength(ReadOnlySpan<byte> d, int o)
        {
            if (o + 4 > d.Length || d[o] != 0xFF || (d[o + 1] & 0xE0) != 0xE0) return 0;
            int ver = (d[o + 1] >> 3) & 3, layer = (d[o + 1] >> 1) & 3, br = d[o + 2] >> 4, sr = (d[o + 2] >> 2) & 3, pad = (d[o + 2] >> 1) & 1;
            if (ver == 1 || layer == 0 || br == 0 || br == 15 || sr == 3) return 0;
            bool v1 = ver == 3;
            int row = v1 ? 3 - layer : layer == 3 ? 3 : 4;
            int bitrate = Bitrates[row, br] * 1000;
            int rate = SampleRates[ver == 3 ? 0 : ver == 2 ? 1 : 2, sr];
            if (layer == 3) return (12 * bitrate / rate + pad) * 4;                     // layer I
            int per = layer == 1 && !v1 ? 72 : 144;                                      // layer III on MPEG-2 / 2.5: 576 samples
            return per * bitrate / rate + pad;
        }

        public static byte[] MpegFrames(ReadOnlySpan<byte> d)
        {
            var o = new MemoryStream(d.Length);
            int i = 0;
            while (i < d.Length)
            {
                int n = FrameLength(d, i);
                if (n > 0 && i + n <= d.Length) { o.Write(d.Slice(i, n)); i += n; }
                else i++;   // padding (or a cut last frame)
            }
            return o.ToArray();
        }

        // ---- IMA ADPCM (FSB5's: Xbox IMA, 0x24 bytes a channel a block, 64 samples) --------------------------------

        static readonly int[] Steps =
        {
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107,
            118, 130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963,
            1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894,
            6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794,
            32767,
        };
        static readonly int[] IndexTable = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };

        /// <summary>16-bit PCM (interleaved, little-endian) of an Xbox IMA stream: per block and channel a 4-byte header
        /// (the predictor, which is the block's first sample, and the step index) then 32 bytes of nibbles, low first, of
        /// which the first 63 make the block's other samples; with two channels the data alternates between them every 4
        /// bytes. 'samples' trims the last block's tail.</summary>
        public static byte[] XboxIma(ReadOnlySpan<byte> d, int channels, long samples)
        {
            int block = 0x24 * channels, perBlock = 64;
            int blocks = d.Length / block;
            long total = Math.Min(samples > 0 ? samples : long.MaxValue, (long)blocks * perBlock);
            var pcm = new short[total * channels];
            var hist = new int[channels]; var step = new int[channels];
            long done = 0;
            for (int bk = 0; bk < blocks && done < total; bk++)
            {
                int b0 = bk * block;
                for (int c = 0; c < channels; c++)
                {
                    hist[c] = (short)(d[b0 + 4 * c] | d[b0 + 4 * c + 1] << 8);
                    step[c] = Math.Clamp((int)d[b0 + 4 * c + 2], 0, 88);
                }
                int data = b0 + 4 * channels;
                for (int c = 0; c < channels; c++)
                {
                    // the header's sample first, then the first 63 nibbles (the 64th is never played: vgmstream's xbox_ima)
                    pcm[done * channels + c] = (short)hist[c];
                    for (int k = 0; k < perBlock - 1 && done + k + 1 < total; k++)
                    {
                        // sample k of channel c: byte (k / 8) * 4 * channels + c * 4 + (k % 8) / 2, low nibble first
                        int byteAt = data + (k / 8) * 4 * channels + c * 4 + (k % 8) / 2;
                        int nib = (k & 1) == 0 ? d[byteAt] & 0xF : d[byteAt] >> 4;
                        int st = Steps[step[c]];
                        int delta = st >> 3;
                        if ((nib & 1) != 0) delta += st >> 2;
                        if ((nib & 2) != 0) delta += st >> 1;
                        if ((nib & 4) != 0) delta += st;
                        hist[c] = Math.Clamp((nib & 8) != 0 ? hist[c] - delta : hist[c] + delta, -32768, 32767);
                        step[c] = Math.Clamp(step[c] + IndexTable[nib], 0, 88);
                        pcm[(done + k + 1) * channels + c] = (short)hist[c];
                    }
                }
                done += Math.Min(perBlock, total - done);
            }
            var bytes = new byte[pcm.Length * 2];
            Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        /// <summary>A RIFF WAVE file around 16-bit PCM.</summary>
        public static byte[] Wav(byte[] pcm, int channels, int rate)
        {
            var o = new MemoryStream(44 + pcm.Length);
            var w = new BinaryWriter(o);
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + pcm.Length); w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(rate);
            w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(pcm.Length); w.Write(pcm);
            w.Flush();
            return o.ToArray();
        }

        static int I32(byte[] b, int o) => BitConverter.ToInt32(b, o);
        static ulong U64(byte[] b, int o) => BitConverter.ToUInt64(b, o);
    }
}
