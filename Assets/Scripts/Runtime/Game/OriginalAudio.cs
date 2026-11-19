// OriginalAudio.cs
// Feature: docs/notes/player-assets.md "The content layer": a sound of the player's converted copy (GameContent's
// Audio/<bank>/<name>.wav | .mp3, FsbBank) as an AudioClip, made on the spot so every caller that wants its clip at once
// (the sound tables, StoryAssets.Voice) gets it: the effects' WAV read as they are, the voices' and music's MP3 decoded by
// NLayer (Assets/Plugins/NLayer, MIT). A long track (over StreamSeconds) is decoded as it plays (a streamed clip reading
// the MP3), a short one whole. (Decoding a table's clips side by side on worker threads was tried, 2026-11-15: no faster
// in an IL2CPP player, whose garbage collector serialises the threads' large sample arrays; the station's scene got slower.)

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class OriginalAudio
    {
        public const float StreamSeconds = 30f;

        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);

        struct Pcm { public float[] data; public int channels, rate; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            clips.Clear();
            GameContent.Refreshed -= ResetStatics;
            GameContent.Refreshed += ResetStatics;   // (a new copy converted: what wasn't there may be now)
        }

        /// <summary>The clip of a sound by its project path ("Audio/VOICE_eng/NAME.ogg"; the extension doesn't matter), null
        /// when the converted copy hasn't it.</summary>
        public static AudioClip Get(string projectPath)
        {
            if (string.IsNullOrEmpty(projectPath)) return null;
            string stem = StemOf(projectPath);
            if (clips.TryGetValue(stem, out var c)) return c;
            using var timing = ContentTimings.Measure("audio clip");
            string name = Path.GetFileName(stem);
            try
            {
                var wav = GameContent.CachePath(stem + ".wav");
                if (wav != null) c = Wav(File.ReadAllBytes(wav), name);
                else
                {
                    var mp3 = GameContent.CachePath(stem + ".mp3");
                    if (mp3 != null) c = Mp3(mp3, name);
                }
            }
            catch (Exception e) when (e is IOException || e is FormatException || e is InvalidDataException || e is ArgumentException)
            {
                Debug.LogWarning($"OriginalAudio: {stem}: {e.Message}");
                c = null;
            }
            clips[stem] = c;
            return c;
        }

        static string StemOf(string projectPath)
        {
            int dot = projectPath.LastIndexOf('.');
            return dot > projectPath.LastIndexOf('/') ? projectPath.Substring(0, dot) : projectPath;
        }

        static AudioClip Create(string name, Pcm pcm)
        {
            var clip = AudioClip.Create(name, pcm.data.Length / pcm.channels, pcm.channels, pcm.rate, false);
            clip.SetData(pcm.data, 0);
            return clip;
        }

        /// <summary>A RIFF WAVE of 16-bit PCM (FsbBank.Wav's) as a clip.</summary>
        public static AudioClip Wav(byte[] b, string name) => Create(name, DecodeWav(b));

        /// <summary>A RIFF WAVE of 16-bit PCM as samples (any thread).</summary>
        static Pcm DecodeWav(byte[] b)
        {
            if (b.Length < 12 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F') throw new FormatException("not a WAV file");
            int o = 12, channels = 1, rate = 44100, bits = 16, dataAt = -1, dataLen = 0;
            while (o + 8 <= b.Length)
            {
                int len = BitConverter.ToInt32(b, o + 4);
                string id = System.Text.Encoding.ASCII.GetString(b, o, 4);
                if (id == "fmt ") { channels = BitConverter.ToInt16(b, o + 10); rate = BitConverter.ToInt32(b, o + 12); bits = BitConverter.ToInt16(b, o + 22); }
                else if (id == "data") { dataAt = o + 8; dataLen = Math.Min(len, b.Length - dataAt); break; }
                o += 8 + len + (len & 1);
            }
            if (dataAt < 0 || bits != 16 || channels < 1) throw new FormatException("not 16-bit PCM");
            int samples = dataLen / 2;
            var f = new float[samples];
            // (little-endian 16-bit samples read in place: every platform the game runs on is little-endian)
            var pcm = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(new ReadOnlySpan<byte>(b, dataAt, samples * 2));
            const float Scale = 1f / 32768f;
            for (int i = 0; i < pcm.Length; i++) f[i] = pcm[i] * Scale;
            if (f.Length % channels != 0) Array.Resize(ref f, f.Length - f.Length % channels);
            return new Pcm { data = f, channels = channels, rate = rate };
        }

        /// <summary>An MP3 file as a clip: decoded whole, or streamed when it is longer than StreamSeconds.</summary>
        public static AudioClip Mp3(string path, string name)
        {
            var probe = new NLayer.MpegFile(path);
            int channels = probe.Channels, rate = probe.SampleRate;
            long frames = probe.Length / (sizeof(float) * channels);
            if (frames <= 0) { probe.Dispose(); throw new FormatException("an empty MP3"); }
            if (frames > StreamSeconds * rate)
            {
                // decoded as it plays: the reader keeps its own MpegFile on the audio thread
                var file = probe;
                var clip = AudioClip.Create(name, (int)frames, channels, rate, true,
                    data =>
                    {
                        int got = file.ReadSamples(data, 0, data.Length);
                        for (int i = Math.Max(0, got); i < data.Length; i++) data[i] = 0f;   // (past the end: silence)
                    },
                    position => file.Time = TimeSpan.FromSeconds(position / (double)rate));
                return clip;
            }
            var all = new float[frames * channels];
            int read = 0, n;
            while (read < all.Length && (n = probe.ReadSamples(all, read, all.Length - read)) > 0) read += n;
            probe.Dispose();
            var c = AudioClip.Create(name, read / channels, channels, rate, false);
            c.SetData(all, 0);
            return c;
        }
    }
}
