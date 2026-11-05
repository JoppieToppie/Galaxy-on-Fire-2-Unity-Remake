// PatchChunker.cs
// Remake-only (the in-game updater, CLAUDE.md "Update patcher" in docs/notes/platforms.md): cuts a file into chunks at
// places its content picks (content-defined chunking, FastCDC's gear hash with normalized chunking), so bytes inserted or
// removed in the middle of a file (Unity's resources.assets.resS when one texture changes) move the cuts with them and
// every chunk away from the change keeps its id. The release side (PatchPublisher) and the game (PatchInstall) cut with
// this same code, so a chunk the player already has never needs a download.
// Chunk ids: the first 16 bytes of the chunk's SHA-256 as 32 lower-case hex digits.

using System;
using System.IO;
using System.Security.Cryptography;

namespace GoF2Remake.UI
{
    public static class PatchChunker
    {
        // Chunk sizes by 'bits' (a file's PatchFile.bits): most chunks end near the normal size 2^bits, none is shorter than
        // a quarter of it (the end of a file excepted) or longer than four times it.
        /// <summary>The usual size: 512 KB normal (128 KB to 2 MB).</summary>
        public const int DefaultBits = 19;
        /// <summary>Unity's serialized files (resources.assets and the like): 16 KB normal (4 to 64 KB). They hold offsets into
        /// the .resS, which shift with every asset change, so small changes are scattered all through them: 2026.10.24 ->
        /// 11.02's resources.assets downloaded 64.5 MB in 512 KB chunks, 18.9 MB in 16 KB ones (GameAssembly.dll ~32 MB at any
        /// size: IL2CPP moves its code everywhere).</summary>
        public const int SmallBits = 14;
        public const int MinBits = 12;
        /// <summary>No chunk is longer (four times the default normal size).</summary>
        public const int MaxSize = 4 << DefaultBits;

        public static int NormalSize(int bits) => 1 << bits;
        public static int MinSize(int bits) => (1 << bits) / 4;
        public static int MaxSizeFor(int bits) => (1 << bits) * 4;
        public static bool ValidBits(int bits) => bits >= MinBits && bits <= DefaultBits;

        static readonly ulong[] Gear = MakeGear();

        /// <summary>256 fixed random words (splitmix64 from a fixed seed): never change them, or every published chunk
        /// would be cut differently from the game's.</summary>
        static ulong[] MakeGear()
        {
            var gear = new ulong[256];
            ulong x = 0x6F6632526D6B6521UL;
            for (int i = 0; i < 256; i++)
            {
                x += 0x9E3779B97F4A7C15UL;
                ulong z = x;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                gear[i] = z ^ (z >> 31);
            }
            return gear;
        }

        /// <summary>The length of the chunk starting at buffer[start], 'available' bytes in hand ('atEnd': nothing follows
        /// them). Needs MaxSizeFor(bits) bytes in hand unless at the end.</summary>
        public static int FindCut(byte[] buffer, int start, int available, bool atEnd, int bits = DefaultBits)
        {
            int min = MinSize(bits);
            if (available <= min) return atEnd ? available : Math.Min(available, min);
            int n = Math.Min(available, MaxSizeFor(bits));
            int normal = Math.Min(n, NormalSize(bits));
            // The gear hash's high bits depend on the last 64 bytes; a cut needs them zero: two more bits before the normal
            // size (harder), two fewer after it (easier).
            ulong hard = ~0UL << (64 - (bits + 2)), easy = ~0UL << (64 - (bits - 2));
            ulong h = 0;
            int i = min;
            for (; i < normal; i++)
            {
                h = (h << 1) + Gear[buffer[start + i]];
                if ((h & hard) == 0) return i + 1;
            }
            for (; i < n; i++)
            {
                h = (h << 1) + Gear[buffer[start + i]];
                if ((h & easy) == 0) return i + 1;
            }
            return n;
        }

        /// <summary>The chunk size a release file is cut at: small for Unity's serialized files (see SmallBits) and for the
        /// Android APK, a zip of 13,000 separately packed entries where a changed entry renews its whole chunk (2026.10.24 ->
        /// 11.02's APK: 578 MB in 512 KB chunks, 296 MB in 64 KB, 237 MB in 16 KB, 224 MB in 8 KB at twice the manifest).</summary>
        public static int BitsFor(string path)
        {
            string name = path.Substring(path.LastIndexOf('/') + 1);
            return name.EndsWith(".assets") || name.EndsWith(".apk") || name == "globalgamemanagers"
                || name.StartsWith("level") && !name.Contains('.')
                ? SmallBits : DefaultBits;
        }

        /// <summary>One chunk of a stream: where it starts, its length, its data (buffer[start..start+length], valid during
        /// the callback only).</summary>
        public delegate void ChunkFound(long offset, byte[] buffer, int start, int length);

        /// <summary>Cuts the whole stream into chunks, in order. 'progress' gets the bytes read so far.</summary>
        public static void Split(Stream stream, ChunkFound found, Action<long> progress = null,
            System.Threading.CancellationToken cancel = default, int bits = DefaultBits)
        {
            if (!ValidBits(bits)) throw new ArgumentOutOfRangeException(nameof(bits));
            var buffer = new byte[MaxSize * 2];
            int max = MaxSizeFor(bits);
            int begin = 0, end = 0;
            long offset = 0;
            bool eof = false;
            while (true)
            {
                if (!eof && end - begin < max)
                {
                    if (begin > 0)
                    {
                        Buffer.BlockCopy(buffer, begin, buffer, 0, end - begin);
                        end -= begin;
                        begin = 0;
                    }
                    while (end < buffer.Length)
                    {
                        int read = stream.Read(buffer, end, buffer.Length - end);
                        if (read <= 0) { eof = true; break; }
                        end += read;
                    }
                }
                int available = end - begin;
                if (available == 0) return;
                cancel.ThrowIfCancellationRequested();
                int length = FindCut(buffer, begin, available, eof, bits);
                found(offset, buffer, begin, length);
                offset += length;
                begin += length;
                progress?.Invoke(offset);
            }
        }

        /// <summary>The chunk id of data[start..start+length].</summary>
        public static string Id(SHA256 sha, byte[] data, int start, int length)
        {
            byte[] hash = sha.ComputeHash(data, start, length);
            var chars = new char[32];
            const string hex = "0123456789abcdef";
            for (int i = 0; i < 16; i++)
            {
                chars[i * 2] = hex[hash[i] >> 4];
                chars[i * 2 + 1] = hex[hash[i] & 15];
            }
            return new string(chars);
        }

        /// <summary>A chunk id: 32 lower-case hex digits (what a manifest may name: ids become file names).</summary>
        public static bool IsId(string id)
        {
            if (id == null || id.Length != 32) return false;
            foreach (char c in id)
                if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')) return false;
            return true;
        }
    }
}
