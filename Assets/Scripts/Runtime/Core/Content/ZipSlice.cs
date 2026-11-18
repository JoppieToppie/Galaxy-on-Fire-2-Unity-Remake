// ZipSlice.cs
// Feature: docs/notes/player-assets.md "First start": a zip inside a zip read in place. An Android XAPK holds the game's
// 1.4 GB OBB (itself a zip) stored, not compressed; the converter used to copy it into memory to open it, which a phone
// can't afford. Stored finds a stored entry's bytes in the outer file (the central directory, Zip64 too, then the entry's
// local header), and SubStream shows that range as a seekable stream a ZipArchive opens directly.

using System;
using System.IO;
using System.Text;

namespace GoF2Remake.Data
{
    public static class ZipSlice
    {
        /// <summary>The data range (offset, length) of the stored (uncompressed) entry 'name' in the zip 's'; null when it has
        /// none by that name, the entry is compressed or the directory can't be read.</summary>
        public static (long offset, long length)? Stored(Stream s, string name)
        {
            try
            {
                long len = s.Length;
                int tail = (int)Math.Min(len, 22 + 65535);
                var t = ReadAt(s, len - tail, tail);
                int eocd = -1;
                for (int i = tail - 22; i >= 0; i--)
                    if (U32(t, i) == 0x06054b50) { eocd = i; break; }
                if (eocd < 0) return null;
                long cdSize = U32(t, eocd + 12), cdOffset = U32(t, eocd + 16);
                if (cdOffset == 0xFFFFFFFF || cdSize == 0xFFFFFFFF || U16(t, eocd + 10) == 0xFFFF)
                {
                    // Zip64: the locator just before the record names the Zip64 end record
                    if (eocd < 20 || U32(t, eocd - 20) != 0x07064b50) return null;
                    long z = (long)U64(t, eocd - 20 + 8);
                    var z64 = ReadAt(s, z, 56);
                    if (U32(z64, 0) != 0x06064b50) return null;
                    cdSize = (long)U64(z64, 40);
                    cdOffset = (long)U64(z64, 48);
                }
                if (cdSize <= 0 || cdSize > int.MaxValue || cdOffset + cdSize > len) return null;
                var cd = ReadAt(s, cdOffset, (int)cdSize);
                for (int p = 0; p + 46 <= cd.Length && U32(cd, p) == 0x02014b50;)
                {
                    int method = U16(cd, p + 10), nameLen = U16(cd, p + 28), extraLen = U16(cd, p + 30), commentLen = U16(cd, p + 32);
                    long comp = U32(cd, p + 20), plain = U32(cd, p + 24), local = U32(cd, p + 42);
                    string entry = Encoding.UTF8.GetString(cd, p + 46, nameLen);
                    if (entry == name)
                    {
                        // Zip64 sizes / offset in the extra field, in this order, for those set to 0xFFFFFFFF
                        for (int e = p + 46 + nameLen, end = e + extraLen; e + 4 <= end;)
                        {
                            int id = U16(cd, e), size = U16(cd, e + 2), q = e + 4;
                            if (id == 1)
                            {
                                if (plain == 0xFFFFFFFF) { plain = (long)U64(cd, q); q += 8; }
                                if (comp == 0xFFFFFFFF) { comp = (long)U64(cd, q); q += 8; }
                                if (local == 0xFFFFFFFF) { local = (long)U64(cd, q); }
                            }
                            e += 4 + size;
                        }
                        if (method != 0 || comp != plain) return null;
                        var lh = ReadAt(s, local, 30);
                        if (U32(lh, 0) != 0x04034b50) return null;
                        long data = local + 30 + U16(lh, 26) + U16(lh, 28);
                        return data + comp <= len ? (data, comp) : ((long, long)?)null;
                    }
                    p += 46 + nameLen + extraLen + commentLen;
                }
                return null;
            }
            catch (IOException) { return null; }
        }

        static byte[] ReadAt(Stream s, long at, int n)
        {
            var b = new byte[n];
            s.Position = at;
            for (int got = 0, r; got < n; got += r)
                if ((r = s.Read(b, got, n - got)) <= 0) throw new EndOfStreamException();
            return b;
        }

        static int U16(byte[] b, int i) => b[i] | b[i + 1] << 8;
        static long U32(byte[] b, int i) => (uint)(b[i] | b[i + 1] << 8 | b[i + 2] << 16 | b[i + 3] << 24);
        static ulong U64(byte[] b, int i) => (ulong)U32(b, i) | (ulong)U32(b, i + 4) << 32;

        /// <summary>A read-only, seekable window [offset, offset + length) of a seekable stream; it seeks the stream itself
        /// before each read, so it shares it with whatever else reads it (one at a time).</summary>
        public sealed class SubStream : Stream
        {
            readonly Stream inner;
            readonly long start, length;
            long pos;

            public SubStream(Stream inner, long offset, long length) { this.inner = inner; start = offset; this.length = length; }

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => length;
            public override long Position { get => pos; set => pos = Math.Max(0, value); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                long left = length - pos;
                if (left <= 0) return 0;
                if (count > left) count = (int)left;
                inner.Position = start + pos;
                int n = inner.Read(buffer, offset, count);
                pos += n;
                return n;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                Position = origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? pos + offset : length + offset;
                return pos;
            }

            public override void Flush() { }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
