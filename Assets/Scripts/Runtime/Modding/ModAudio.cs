// ModAudio.cs
// Remake mods: loading a mod's audio file (.ogg, .wav, .mp3) into an AudioClip, for ModVoices and ModMusic. Unity can't
// decode compressed audio from bytes, so the file is read with UnityWebRequestMultimedia from disk: a folder mod's own
// file, a zip mod's copied into the cache first. In the background; the
// callback gets the clip, or null when it couldn't be read (logged). (The zip copy is ModInfo.LocalFile, shared with the
// textures' background decoding.)
// One clip per file (Load's cache): every loader asking for the same file gets the same clip, and a new mod revision reads
// only the files that changed. The Mod Builder reads its project again after every edit, and each edit used to load every
// track and sound afresh and keep the old copies too, until the game ran out of memory (a Discord report, 2026-10-30:
// crashes after replacing music). The clips are the cache's: Sweep destroys those whose file changed or went, or whose
// mod is off; nobody else destroys them.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModAudio
    {
        public static readonly string[] Extensions = { ".ogg", ".wav", ".mp3" };

        sealed class Entry
        {
            public string modId, location, file;
            public long stamp;
            public AudioClip clip;
            public List<Action<AudioClip>> waiting;   // still loading: the callbacks to call then
        }

        static readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>(StringComparer.Ordinal);
        static int sweptRevision = int.MinValue;

        /// <summary>Reads a file into a new clip (tests replace it).</summary>
        internal static Action<ModInfo, string, bool, Action<AudioClip>> Reader = Read;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Clear(); Reader = Read; }

        /// <summary>Every cached clip goes.</summary>
        internal static void Clear()
        {
            foreach (var e in cache.Values) if (e.clip != null) Gone(e.clip);
            cache.Clear();
            sweptRevision = int.MinValue;
        }

        static void Gone(AudioClip c) { if (Application.isPlaying) UnityEngine.Object.Destroy(c); else UnityEngine.Object.DestroyImmediate(c); }

        /// <summary>The clips loaded and loading (tests).</summary>
        internal static int CachedCount => cache.Count;

        /// <summary>The AudioType of a file name (Unknown: not one of the Extensions).</summary>
        public static AudioType TypeOf(string file)
        {
            string ext = Path.GetExtension(file ?? "").ToLowerInvariant();
            return ext == ".ogg" ? AudioType.OGGVORBIS : ext == ".wav" ? AudioType.WAV : ext == ".mp3" ? AudioType.MPEG : AudioType.UNKNOWN;
        }

        /// <summary>The AudioType a file's first bytes show (RIFF..WAVE, OggS, an ID3 tag or an MPEG frame sync; Unknown: none of
        /// them). The Mod Builder saved every picked file as .ogg until 2026-10-24 (the pickers' copy is "picked.tmp"), so a
        /// WAV or MP3 under an .ogg name failed to decode.</summary>
        public static AudioType Sniff(byte[] head, int count)
        {
            if (head == null || count < 3) return AudioType.UNKNOWN;
            if (count >= 12 && head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F'
                && head[8] == 'W' && head[9] == 'A' && head[10] == 'V' && head[11] == 'E') return AudioType.WAV;
            if (count >= 4 && head[0] == 'O' && head[1] == 'g' && head[2] == 'g' && head[3] == 'S') return AudioType.OGGVORBIS;
            if (head[0] == 'I' && head[1] == 'D' && head[2] == '3') return AudioType.MPEG;
            if (head[0] == 0xFF && (head[1] & 0xE0) == 0xE0) return AudioType.MPEG;
            return AudioType.UNKNOWN;
        }

        /// <summary>Sniff on a file on disk (Unknown when it can't be read).</summary>
        public static AudioType SniffFile(string path)
        {
            try
            {
                var head = new byte[12];
                using var f = File.OpenRead(path);
                return Sniff(head, f.Read(head, 0, head.Length));
            }
            catch { return AudioType.UNKNOWN; }
        }

        /// <summary>The type a file is read as: its content's ('sniffed', Sniff), else its name's (Unknown: neither shows one).</summary>
        public static AudioType TypeOf(string file, AudioType sniffed) => sniffed != AudioType.UNKNOWN ? sniffed : TypeOf(file);

        /// <summary>The extension (with the dot) for an AudioType of Sniff, else null.</summary>
        public static string ExtensionOf(AudioType type) =>
            type == AudioType.WAV ? ".wav" : type == AudioType.OGGVORBIS ? ".ogg" : type == AudioType.MPEG ? ".mp3" : null;

        /// <summary>The file's own spelling in the mod (any case; null: no such file).</summary>
        public static string Find(ModSource source, string path)
        {
            foreach (var f in source.Files) if (string.Equals(f, path, StringComparison.OrdinalIgnoreCase)) return f;
            return null;
        }

        /// <summary>Loads 'file' of 'mod' and calls 'done' with the clip or null. 'compressed': kept compressed in memory
        /// (music and voices are long); sound effects are decoded once (no decoding per play). The clip is the cache's (the
        /// same one for every call while the file is unchanged): never destroy it.</summary>
        public static void Load(ModInfo mod, string file, Action<AudioClip> done, bool compressed = true)
        {
            Sweep();
            long stamp = Stamp(mod, file);
            string key = mod.Id + "|" + mod.Source?.Location + "|" + file.ToLowerInvariant() + "|" + stamp + (compressed ? "|c" : "|d");
            if (cache.TryGetValue(key, out var e))
            {
                if (e.waiting != null) { e.waiting.Add(done); return; }
                if (e.clip != null) { done?.Invoke(e.clip); return; }
                cache.Remove(key);   // destroyed after all
            }
            e = new Entry { modId = mod.Id, location = mod.Source?.Location, file = file, stamp = stamp, waiting = new List<Action<AudioClip>> { done } };
            cache[key] = e;
            Reader(mod, file, compressed, clip =>
            {
                var waiting = e.waiting;
                e.waiting = null;
                bool current = cache.TryGetValue(key, out var now) && now == e;
                if (clip != null && current && Valid(e)) e.clip = clip;
                else
                {
                    // Failed (tried again by the next load), or the file changed or its mod went meanwhile.
                    if (clip != null) Gone(clip);
                    clip = null;
                    if (current) cache.Remove(key);
                }
                foreach (var w in waiting) w?.Invoke(clip);
            });
        }

        /// <summary>The mods changed: the clips of files that changed or went, or of mods now off, are destroyed (once per
        /// revision; Load calls it, and the loaders when their mods change).</summary>
        public static void Sweep()
        {
            if (sweptRevision == ModManager.Revision) return;
            sweptRevision = ModManager.Revision;
            List<string> gone = null;
            foreach (var kv in cache)
                if (!Valid(kv.Value)) (gone ??= new List<string>()).Add(kv.Key);
            if (gone == null) return;
            foreach (var key in gone)
            {
                var c = cache[key].clip;   // one still loading is destroyed when it arrives (Load)
                if (c != null) Gone(c);
                cache.Remove(key);
            }
        }

        /// <summary>The entry's file is still the one in a mod that is on (or the Mod Builder's project).</summary>
        static bool Valid(Entry e)
        {
            var b = ModManager.BuilderProject;
            if (b != null && b.Id == e.modId && b.Source?.Location == e.location) return Stamp(b, e.file) == e.stamp;
            foreach (var m in ModManager.Active)
                if (m.Id == e.modId && m.Source?.Location == e.location) return Stamp(m, e.file) == e.stamp;
            return false;
        }

        /// <summary>The file's date and size (a folder mod's own file; a zip mod's zip): changes when the file does. 0: none.</summary>
        static long Stamp(ModInfo mod, string file)
        {
            try
            {
                if (mod.Source == null) return 0;
                var info = new FileInfo(mod.Source is FolderSource ? Path.Combine(mod.Source.Location, file) : mod.Source.Location);
                return info.Exists ? info.LastWriteTimeUtc.Ticks ^ info.Length : 0;
            }
            catch (Exception) { return 0; }
        }

        static async void Read(ModInfo mod, string file, bool compressed, Action<AudioClip> done)
        {
            AudioClip clip = null;
            try
            {
                AudioType type;
                // A zip mod's file is copied out on a worker thread (ModInfo.LocalFile).
                // The content decides over the name (a WAV saved as .ogg, see Sniff; #208: the weapon fx page saved a picked
                // sound as "fx/<item>.tmp" before the picker named its copy by type, so the name alone refused it).
                AudioType sniffed = AudioType.UNKNOWN;
                string path = await System.Threading.Tasks.Task.Run(() =>
                {
                    string p = mod.LocalFile(file);
                    if (p != null) sniffed = SniffFile(p);
                    return p;
                });
                if (path == null) throw new Exception("can't be read");
                type = TypeOf(file, sniffed);
                if (type == AudioType.UNKNOWN) throw new Exception("not an .ogg, .wav or .mp3 file");
                // #208: on Android every picked sound came back "couldn't be played" (the weapon kept its game sound). The
                // escaped file URI is tried first, then the plain "file://" + path some Android players want; a WAV that
                // Unity's decoder still refuses is decoded here (PCM / float, DecodeWav), so one format always works.
                string error = null;
                foreach (string url in Urls(path))
                {
                    using var req = UnityWebRequestMultimedia.GetAudioClip(url, type);
                    if (req.downloadHandler is DownloadHandlerAudioClip handler) handler.compressed = compressed;
                    var op = req.SendWebRequest();
                    while (!op.isDone) await Awaitable.NextFrameAsync();
                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        try { clip = DownloadHandlerAudioClip.GetContent(req); } catch (Exception e) { error = e.Message; }
                        if (clip != null && clip.loadState != AudioDataLoadState.Failed && clip.length > 0f) break;
                        if (clip != null) { Gone(clip); clip = null; }
                        error ??= "the clip couldn't be decoded";
                    }
                    else error = req.error;
                }
                if (clip == null && type == AudioType.WAV)
                {
                    var wav = await System.Threading.Tasks.Task.Run(() => DecodeWav(File.ReadAllBytes(path)));
                    if (wav != null)
                    {
                        clip = AudioClip.Create(Path.GetFileNameWithoutExtension(file), wav.Value.samples.Length / wav.Value.channels, wav.Value.channels, wav.Value.rate, false);
                        clip.SetData(wav.Value.samples, 0);
                    }
                }
                if (clip == null) throw new Exception(error ?? "the clip couldn't be decoded");
                clip.name = Path.GetFileNameWithoutExtension(file);
            }
            catch (Exception e) { Debug.LogWarning($"Mods: {mod.Id}: {file}: {e.Message}"); LastError = e.Message; clip = null; }
            done?.Invoke(clip);
        }

        /// <summary>Why the last file that failed to load did (shown by the Mod Builder's preview; null: none yet).</summary>
        public static string LastError { get; private set; }

        /// <summary>The URLs a file on disk is read from: the escaped file URI, then (an absolute Unix path, Android's)
        /// "file://" + the path as it is.</summary>
        internal static List<string> Urls(string path)
        {
            var urls = new List<string> { new Uri(path).AbsoluteUri };
            if (path.StartsWith("/", StringComparison.Ordinal) && !urls.Contains("file://" + path)) urls.Add("file://" + path);
            return urls;
        }

        /// <summary>A RIFF/WAVE file's samples (PCM 8 / 16 / 24 / 32 bit, IEEE float 32 bit, also as WAVE_FORMAT_EXTENSIBLE),
        /// interleaved, -1..1; null when it is none of them (any thread).</summary>
        internal static (float[] samples, int channels, int rate)? DecodeWav(byte[] b)
        {
            if (b == null || b.Length < 12 || Sniff(b, 12) != AudioType.WAV) return null;
            int format = 0, channels = 0, rate = 0, bits = 0, pos = 12;
            while (pos + 8 <= b.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
                int size = BitConverter.ToInt32(b, pos + 4);
                int body = pos + 8;
                if (size < 0) return null;
                if (id == "fmt " && size >= 16 && body + 16 <= b.Length)
                {
                    format = BitConverter.ToUInt16(b, body);
                    channels = BitConverter.ToUInt16(b, body + 2);
                    rate = BitConverter.ToInt32(b, body + 4);
                    bits = BitConverter.ToUInt16(b, body + 14);
                    if (format == 0xFFFE && size >= 26 && body + 26 <= b.Length) format = BitConverter.ToUInt16(b, body + 24);
                }
                else if (id == "data")
                {
                    if (channels <= 0 || rate <= 0 || (format != 1 && format != 3)) return null;
                    int bytes = bits / 8;
                    if (bytes < 1 || bytes > 4 || (format == 3 && bits != 32)) return null;
                    int len = (int)Math.Min((long)size, b.Length - body);
                    int count = len / bytes / channels * channels;
                    if (count <= 0) return null;
                    var s = new float[count];
                    for (int i = 0, o = body; i < count; i++, o += bytes)
                        s[i] = format == 3 ? BitConverter.ToSingle(b, o)
                             : bytes == 1 ? (b[o] - 128) / 128f
                             : bytes == 2 ? BitConverter.ToInt16(b, o) / 32768f
                             : bytes == 3 ? ((b[o] << 8 | b[o + 1] << 16 | b[o + 2] << 24) >> 8) / 8388608f
                             : BitConverter.ToInt32(b, o) / 2147483648f;
                    return (s, channels, rate);
                }
                pos = body + size + (size & 1);
            }
            return null;
        }
    }
}
