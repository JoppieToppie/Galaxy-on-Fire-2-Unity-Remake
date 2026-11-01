// NetWire.cs
// Multiplayer's compact encodings (docs/notes/multiplayer-sync.md "Bandwidth"), plain C# so the edit-mode tests can check
// them: a byte writer / reader (variable-length integers, 24-bit integers, halves), positions quantised to one game unit
// (0.05 m) in 3 x 24 bits (+-419 km; beyond that the full floats), rotations as "smallest three" in 32 bits (about 0.1 deg),
// unit directions in 2 x 16 bits (octahedral), and the pose stream's batches (PoseBatch) with their send schedule
// (PoseSchedule). The network layer (GoF2.Net: NetPoses, NetShotData) only moves the bytes.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Data
{
    public sealed class WireWriter
    {
        byte[] buffer;

        public WireWriter(int capacity = 256) => buffer = new byte[Math.Max(16, capacity)];

        public int Length { get; private set; }

        public void Clear() => Length = 0;

        public byte[] ToArray()
        {
            var a = new byte[Length];
            Buffer.BlockCopy(buffer, 0, a, 0, Length);
            return a;
        }

        void Need(int n)
        {
            if (Length + n <= buffer.Length) return;
            Array.Resize(ref buffer, Math.Max(buffer.Length * 2, Length + n));
        }

        public void Byte(byte b)
        {
            Need(1);
            buffer[Length++] = b;
        }

        public void Bool(bool b) => Byte(b ? (byte)1 : (byte)0);

        public void UShort(int v)
        {
            Need(2);
            buffer[Length++] = (byte)v;
            buffer[Length++] = (byte)(v >> 8);
        }

        public void Int24(int v)
        {
            Need(3);
            buffer[Length++] = (byte)v;
            buffer[Length++] = (byte)(v >> 8);
            buffer[Length++] = (byte)(v >> 16);
        }

        public void UInt(uint v)
        {
            Need(4);
            for (int i = 0; i < 4; i++) buffer[Length++] = (byte)(v >> (8 * i));
        }

        public void Float(float f) => UInt((uint)BitConverter.SingleToInt32Bits(f));

        public void Half(float f) => UShort(Mathf.FloatToHalf(f));

        /// <summary>7 bits a byte, the high bit = more follows.</summary>
        public void VarULong(ulong v)
        {
            while (v >= 0x80)
            {
                Byte((byte)(v | 0x80));
                v >>= 7;
            }
            Byte((byte)v);
        }

        /// <summary>A signed int, zigzag (small negatives stay short).</summary>
        public void VarInt(int v) => VarULong((uint)((v << 1) ^ (v >> 31)));

        public void Text(string s)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(s ?? "");
            VarULong((ulong)bytes.Length);
            Need(bytes.Length);
            Buffer.BlockCopy(bytes, 0, buffer, Length, bytes.Length);
            Length += bytes.Length;
        }

        public void Bytes(byte[] bytes, int offset, int count)
        {
            Need(count);
            Buffer.BlockCopy(bytes, offset, buffer, Length, count);
            Length += count;
        }
    }

    /// <summary>Reads what WireWriter wrote. A read past the end returns zeros and clears Ok (a short or damaged message is
    /// dropped by the caller, never an exception).</summary>
    public sealed class WireReader
    {
        readonly byte[] buffer;
        readonly int end;
        int pos;

        public WireReader(byte[] data, int offset = 0, int count = -1)
        {
            buffer = data ?? new byte[0];
            pos = offset;
            end = count < 0 ? buffer.Length : Math.Min(buffer.Length, offset + count);
        }

        public bool Ok { get; private set; } = true;
        public bool AtEnd => pos >= end;
        public int Position => pos;

        public byte Byte()
        {
            if (pos >= end) { Ok = false; return 0; }
            return buffer[pos++];
        }

        public bool Bool() => Byte() != 0;
        public int UShort() => Byte() | Byte() << 8;

        public int Int24()
        {
            int v = Byte() | Byte() << 8 | Byte() << 16;
            return (v << 8) >> 8;   // sign-extended
        }

        public uint UInt() => (uint)(Byte() | Byte() << 8 | Byte() << 16 | Byte() << 24);
        public float Float() => BitConverter.Int32BitsToSingle((int)UInt());
        public float Half() => Mathf.HalfToFloat((ushort)UShort());

        public ulong VarULong()
        {
            ulong v = 0;
            for (int shift = 0; shift < 64; shift += 7)
            {
                byte b = Byte();
                v |= (ulong)(b & 0x7f) << shift;
                if ((b & 0x80) == 0 || !Ok) return v;
            }
            Ok = false;
            return v;
        }

        public int VarInt()
        {
            uint z = (uint)VarULong();
            return (int)(z >> 1) ^ -(int)(z & 1);
        }

        public string Text()
        {
            ulong n = VarULong();
            if (!Ok || n > (ulong)(end - pos)) { Ok = false; return ""; }
            string s = System.Text.Encoding.UTF8.GetString(buffer, pos, (int)n);
            pos += (int)n;
            return s;
        }
    }

    public static class WireQuant
    {
        /// <summary>One game unit in Unity metres: the step a position is sent in.</summary>
        public const float PositionStep = 0.05f;
        const int Int24Max = 8388607;
        const float RotationScale = 511f / 0.70710678f;   // 10 bits a component, +-1/sqrt(2)

        /// <summary>Whether 'p' fits the quantised form (3 x 24 bits); else WritePosition writes the floats.</summary>
        public static bool Fits(Vector3 p) => Fits(p.x) && Fits(p.y) && Fits(p.z);
        static bool Fits(float f) => !float.IsNaN(f) && Mathf.Abs(f / PositionStep) < Int24Max;

        /// <summary>A position: flag byte 0 + 3 x 24 bits, or 1 + 3 floats.</summary>
        public static void WritePosition(WireWriter w, Vector3 p)
        {
            bool fits = Fits(p);
            w.Byte(fits ? (byte)0 : (byte)1);
            WritePosition(w, p, fits);
        }

        public static Vector3 ReadPosition(WireReader r) => ReadPosition(r, r.Byte() != 0);

        /// <summary>Without the flag byte (the caller keeps 'fits' itself: the pose stream's entry flags).</summary>
        public static void WritePosition(WireWriter w, Vector3 p, bool fits)
        {
            if (fits)
            {
                w.Int24(Mathf.RoundToInt(p.x / PositionStep));
                w.Int24(Mathf.RoundToInt(p.y / PositionStep));
                w.Int24(Mathf.RoundToInt(p.z / PositionStep));
                return;
            }
            w.Float(p.x);
            w.Float(p.y);
            w.Float(p.z);
        }

        public static Vector3 ReadPosition(WireReader r, bool full) =>
            full ? new Vector3(r.Float(), r.Float(), r.Float()) : new Vector3(r.Int24(), r.Int24(), r.Int24()) * PositionStep;

        /// <summary>Smallest three: the largest component's index (2 bits) and the other three (10 bits each), the sign
        /// made positive (q and -q are the same rotation).</summary>
        public static uint PackRotation(Quaternion q)
        {
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (m < 1e-6f || float.IsNaN(m)) return 3u << 30 | Packed3(0f, 0f, 0f);   // identity
            float[] c = { q.x / m, q.y / m, q.z / m, q.w / m };
            int largest = 0;
            for (int i = 1; i < 4; i++) if (Mathf.Abs(c[i]) > Mathf.Abs(c[largest])) largest = i;
            float sign = c[largest] < 0f ? -1f : 1f;
            float a = 0f, b = 0f, d = 0f;
            int k = 0;
            for (int i = 0; i < 4; i++)
            {
                if (i == largest) continue;
                float v = c[i] * sign;
                if (k == 0) a = v; else if (k == 1) b = v; else d = v;
                k++;
            }
            return (uint)largest << 30 | Packed3(a, b, d);
        }

        static uint Packed3(float a, float b, float c) => Ten(a) << 20 | Ten(b) << 10 | Ten(c);
        static uint Ten(float v) => (uint)Mathf.Clamp(Mathf.RoundToInt(v * RotationScale) + 511, 0, 1022);
        static float FromTen(uint v) => ((int)(v & 1023) - 511) / RotationScale;

        public static Quaternion UnpackRotation(uint packed)
        {
            int largest = (int)(packed >> 30);
            float a = FromTen(packed >> 20), b = FromTen(packed >> 10), d = FromTen(packed);
            float l = Mathf.Sqrt(Mathf.Max(0f, 1f - a * a - b * b - d * d));
            var c = new float[4];
            int k = 0;
            for (int i = 0; i < 4; i++)
            {
                if (i == largest) { c[i] = l; continue; }
                c[i] = k == 0 ? a : k == 1 ? b : d;
                k++;
            }
            var q = new Quaternion(c[0], c[1], c[2], c[3]);
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return m > 1e-6f ? new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m) : Quaternion.identity;
        }

        public static void WriteRotation(WireWriter w, Quaternion q) => w.UInt(PackRotation(q));
        public static Quaternion ReadRotation(WireReader r) => UnpackRotation(r.UInt());

        /// <summary>A unit direction (octahedral, 2 x 16 bits); a zero vector reads back as zero.</summary>
        public static void WriteDirection(WireWriter w, Vector3 v)
        {
            float n = Mathf.Abs(v.x) + Mathf.Abs(v.y) + Mathf.Abs(v.z);
            if (n < 1e-9f || float.IsNaN(n)) { w.UShort(0); w.UShort(0); return; }
            float x = v.x / n, y = v.y / n;
            if (v.z < 0f)
            {
                float ox = (1f - Mathf.Abs(y)) * (x >= 0f ? 1f : -1f), oy = (1f - Mathf.Abs(x)) * (y >= 0f ? 1f : -1f);
                x = ox;
                y = oy;
            }
            w.UShort(Mathf.Clamp(Mathf.RoundToInt((x * 0.5f + 0.5f) * 65534f), 0, 65534) + 1);
            w.UShort(Mathf.Clamp(Mathf.RoundToInt((y * 0.5f + 0.5f) * 65534f), 0, 65534));
        }

        public static Vector3 ReadDirection(WireReader r)
        {
            int ux = r.UShort(), uy = r.UShort();
            if (ux == 0) return Vector3.zero;
            float x = (ux - 1) / 65534f * 2f - 1f, y = uy / 65534f * 2f - 1f;
            float z = 1f - Mathf.Abs(x) - Mathf.Abs(y);
            if (z < 0f)
            {
                float ox = (1f - Mathf.Abs(y)) * (x >= 0f ? 1f : -1f), oy = (1f - Mathf.Abs(x)) * (y >= 0f ? 1f : -1f);
                x = ox;
                y = oy;
            }
            return new Vector3(x, y, z).normalized;
        }

        /// <summary>A 0..1 fraction in a byte; -1 (none) as 255.</summary>
        public static byte Fraction(float f) => f < 0f ? (byte)255 : f <= 0f ? (byte)0 : (byte)Mathf.Clamp(Mathf.RoundToInt(f * 254f), 1, 254);   // a scratch of hull left is not 0
        public static float Fraction(byte b) => b == 255 ? -1f : b / 254f;
    }

    /// <summary>A remote ship's look in the pose stream (a player's): engine glow, boost, cloak, phase, EMP lightning and the
    /// hull / shield / armor fractions (-1 = no such pool).</summary>
    public struct PoseLook : IEquatable<PoseLook>
    {
        public bool engine, phase, empShock;
        public byte boost;        // 0..255 = 0..1
        public byte cloak;        // 0..100 %
        public byte hull, shield, armor;   // WireQuant.Fraction

        public bool Equals(PoseLook o) => engine == o.engine && phase == o.phase && empShock == o.empShock && boost == o.boost
                                          && cloak == o.cloak && hull == o.hull && shield == o.shield && armor == o.armor;
        public override bool Equals(object obj) => obj is PoseLook o && Equals(o);
        public override int GetHashCode() => boost | cloak << 8 | hull << 16 | (engine ? 1 << 24 : 0) | (phase ? 1 << 25 : 0) | (empShock ? 1 << 26 : 0);
    }

    public struct PoseEntry
    {
        public ulong id;              // the NetworkObject
        public Vector3 position;      // Unity metres
        public Quaternion rotation;
        public bool hasLook;
        public PoseLook look;
    }

    /// <summary>One message of the pose stream: a sequence number (receivers drop older ones: it is sent unreliably), the
    /// sender's clock in ms (16 bits: the gap between two poses is the sender's, not the arrival's), then the entries:
    /// id (varint), flags (bit 0: a look follows, bit 1: the position's full floats), position, rotation [, look]. About 16
    /// bytes an NPC, 22 a player.</summary>
    public static class PoseBatch
    {
        /// <summary>Unreliable messages aren't split by the transport: each batch stays below this.</summary>
        public const int MaxBytes = 1100;

        public static List<byte[]> Encode(int sequence, int clockMs, IReadOnlyList<PoseEntry> entries)
        {
            var batches = new List<byte[]>();
            var w = new WireWriter(MaxBytes + 64);
            void Begin()
            {
                w.Clear();
                w.UShort(sequence);
                w.UShort(clockMs);
            }
            Begin();
            int inBatch = 0;
            foreach (var e in entries)
            {
                int before = w.Length;
                w.VarULong(e.id);
                bool fits = WireQuant.Fits(e.position);
                w.Byte((byte)((e.hasLook ? 1 : 0) | (fits ? 0 : 2)));
                WireQuant.WritePosition(w, e.position, fits);
                WireQuant.WriteRotation(w, e.rotation);
                if (e.hasLook)
                {
                    var l = e.look;
                    w.Byte((byte)((l.engine ? 1 : 0) | (l.phase ? 2 : 0) | (l.empShock ? 4 : 0)));
                    w.Byte(l.boost);
                    w.Byte(l.cloak);
                    w.Byte(l.hull);
                    w.Byte(l.shield);
                    w.Byte(l.armor);
                }
                inBatch++;
                if (w.Length > MaxBytes && inBatch > 1)
                {
                    // Over the limit: this entry starts the next batch.
                    var full = w.ToArray();
                    var cut = new byte[before];
                    Buffer.BlockCopy(full, 0, cut, 0, before);
                    batches.Add(cut);
                    Begin();
                    w.Bytes(full, before, full.Length - before);
                    inBatch = 1;
                }
            }
            if (inBatch > 0) batches.Add(w.ToArray());
            return batches;
        }

        /// <summary>False = damaged (the entries read so far are kept in 'into').</summary>
        public static bool Decode(byte[] data, out int sequence, out int clockMs, List<PoseEntry> into)
        {
            var r = new WireReader(data);
            sequence = r.UShort();
            clockMs = r.UShort();
            while (r.Ok && !r.AtEnd)
            {
                var e = new PoseEntry { id = r.VarULong() };
                byte flags = r.Byte();
                e.position = WireQuant.ReadPosition(r, (flags & 2) != 0);
                e.rotation = WireQuant.ReadRotation(r);
                if ((flags & 1) != 0)
                {
                    e.hasLook = true;
                    byte f = r.Byte();
                    e.look = new PoseLook
                    {
                        engine = (f & 1) != 0, phase = (f & 2) != 0, empShock = (f & 4) != 0,
                        boost = r.Byte(), cloak = r.Byte(), hull = r.Byte(), shield = r.Byte(), armor = r.Byte(),
                    };
                }
                if (!r.Ok) return false;
                into.Add(e);
            }
            return r.Ok;
        }

        /// <summary>Sequence a is newer than b (16 bits, wrapping).</summary>
        public static bool Newer(int a, int b) => (short)(a - b) > 0;

        /// <summary>Seconds between two of the sender's 16-bit clocks (wrapping every 65.5 s).</summary>
        public static float ClockGap(int now, int before) => (ushort)(now - before) / 1000f;
    }

    /// <summary>How often a pose goes out (seconds), from what changed since the last one and how far the nearest other
    /// player in the orbit is (Unity metres; +inf = nobody).</summary>
    public static class PoseSchedule
    {
        public const float NearMeters = 2000f, MidMeters = 6000f;
        /// <summary>An unchanged pose is sent again this often (an unreliable send may have been lost).</summary>
        public const float RefreshSeconds = 2f;
        /// <summary>Moved less than this (metres) and turned less than MinDegrees: unchanged.</summary>
        public const float MinMeters = 0.05f, MinDegrees = 0.5f;

        /// <summary>Nobody else in the orbit: the server still keeps a recent pose (a player arriving, a handover, the
        /// events' and commands' checks), a player's own ship more often (its hull: the events' "dead").</summary>
        public const float AloneSeconds = 2f, AloneIdleSeconds = 10f, AlonePlayerSeconds = 1f;

        public static float Interval(bool player, bool changed, float nearestOther)
        {
            if (float.IsInfinity(nearestOther) || float.IsNaN(nearestOther))
                return player ? (changed ? AlonePlayerSeconds : RefreshSeconds) : changed ? AloneSeconds : AloneIdleSeconds;
            if (!changed) return RefreshSeconds;
            if (nearestOther < NearMeters) return player ? 0.05f : 0.1f;
            if (nearestOther < MidMeters) return player ? 0.1f : 0.25f;
            return 0.5f;
        }

        public static bool Changed(Vector3 sentPos, Quaternion sentRot, Vector3 pos, Quaternion rot) =>
            (pos - sentPos).sqrMagnitude > MinMeters * MinMeters || Quaternion.Angle(sentRot, rot) > MinDegrees;
    }
}
