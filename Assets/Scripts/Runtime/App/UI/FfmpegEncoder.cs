// FfmpegEncoder.cs
// Remake-only: the screen recorder's encoder on Linux (RecorderEncoder; docs/notes/platforms.md "Screen recorder"; the
// user's request "linux should also work"): the system's ffmpeg (on the PATH, or $GOF2_FFMPEG), which Linux distributions
// and SteamOS carry; the game ships none. The frames ScreenRecorder packs (I420, or 16-bit planar 4:2:0 in HDR) go to
// ffmpeg's standard input as raw video at a constant frame rate: a writer thread fills a skipped slot (a late frame, a
// full queue) with the frame before it, so the picture keeps the clock the sound keeps. The sound goes to a raw float
// file beside it; at the stop ffmpeg finishes the picture, then a second ffmpeg muxes the two (AAC) into the clip. H.264
// by libx264 (else libopenh264, else mpeg4); HDR10 by libx265 Main 10 with BT.2020 / PQ / the light levels (refused
// without it: the game then records SDR). On Windows too with $GOF2_RECORDER=ffmpeg (tests, a PC without the DLL).

#if UNITY_STANDALONE || UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Unity.Collections;
using Debug = UnityEngine.Debug;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class FfmpegEncoder
    {
        const int MaxQueuedFrames = 45;

        static string exe, encoders;
        static bool searched;

        static Process process;
        static Thread writer;
        static readonly ConcurrentQueue<(long slot, byte[] data)> frames = new ConcurrentQueue<(long, byte[])>();
        static readonly ConcurrentQueue<float[]> sound = new ConcurrentQueue<float[]>();
        static readonly AutoResetEvent wake = new AutoResetEvent(false);
        static volatile bool stopping;
        static int dropped, fps, sampleRate;
        static long ticksPerFrame;
        static string target, videoTemp, soundTemp, failure;
        static StringBuilder log;

        /// <summary>ffmpeg's path, or null when there is none.</summary>
        public static string Exe
        {
            get
            {
                if (searched) return exe;
                searched = true;
                exe = Find();
                return exe;
            }
        }

        public static bool Found => Exe != null;
        public static int Dropped => dropped;

        static string Find()
        {
            string own = Environment.GetEnvironmentVariable("GOF2_FFMPEG");
            if (!string.IsNullOrEmpty(own) && File.Exists(own)) return own;
            string name = Path.DirectorySeparatorChar == '\\' ? "ffmpeg.exe" : "ffmpeg";
            var dirs = new List<string>((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
            dirs.AddRange(new[] { "/usr/bin", "/usr/local/bin", "/app/bin", "/run/host/usr/bin" });
            foreach (var d in dirs)
            {
                if (string.IsNullOrWhiteSpace(d)) continue;
                try { string f = Path.Combine(d.Trim(), name); if (File.Exists(f)) return f; } catch (Exception) { }
            }
            return null;
        }

        /// <summary>The encoder is in this ffmpeg ("libx264", "libx265"...): its -encoders list, read once.</summary>
        static bool Has(string encoder)
        {
            if (encoders == null)
            {
                encoders = "";
                try
                {
                    using var p = Process.Start(new ProcessStartInfo(Exe, "-hide_banner -encoders")
                        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true });
                    encoders = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(10000);
                }
                catch (Exception e) { Debug.LogWarning("FfmpegEncoder: " + e.Message); }
            }
            return encoders.Contains(" " + encoder + " ");
        }

        /// <summary>Null, or why it couldn't start (no ffmpeg; HDR without libx265).</summary>
        public static string Start(string path, int w, int h, int frameRate, int kbps, int rate, int channels, bool hdr, int peakNits, int averageNits)
        {
            if (!Found) return "ffmpeg not found (install it, or set GOF2_FFMPEG)";
            if (process != null) return "already recording";
            string codec;
            var a = new StringBuilder();
            if (hdr)
            {
                if (!Has("libx265")) return "this ffmpeg has no libx265 for HDR";
                codec = "-c:v libx265 -preset fast -pix_fmt yuv420p10le -tag:v hvc1"
                      + " -color_primaries bt2020 -color_trc smpte2084 -colorspace bt2020nc -color_range tv"
                      + $" -x265-params \"hdr10=1:repeat-headers=1:colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc:range=limited"
                      + $":max-cll={Math.Max(1, peakNits)},{Math.Max(1, averageNits)}"
                      + $":master-display=G(8500,39850)B(6550,2300)R(35400,14600)WP(15635,16450)L({Math.Max(1, peakNits) * 10000},50)\"";
            }
            else
            {
                string enc = Has("libx264") ? "libx264 -preset veryfast" : Has("libopenh264") ? "libopenh264" : "mpeg4";
                codec = $"-c:v {enc} -pix_fmt yuv420p -color_primaries bt709 -color_trc bt709 -colorspace bt709 -color_range tv";
            }
            string temp = Path.Combine(Path.GetTempPath(), "GoF2Clip_" + Guid.NewGuid().ToString("N"));
            videoTemp = temp + ".mp4";
            soundTemp = rate > 0 ? temp + ".f32" : null;
            target = path;
            fps = frameRate;
            sampleRate = rate;
            ticksPerFrame = 10_000_000L / frameRate;
            dropped = 0;
            failure = null;
            stopping = false;
            while (frames.TryDequeue(out _)) { }
            while (sound.TryDequeue(out _)) { }
            a.Append($"-hide_banner -loglevel error -y -f rawvideo -pix_fmt {(hdr ? "yuv420p16le" : "yuv420p")} -video_size {w}x{h}");
            a.Append($" -framerate {frameRate} -i pipe:0 {codec} -b:v {kbps}k -maxrate {kbps * 3 / 2}k -bufsize {kbps * 2}k \"{videoTemp}\"");
            try
            {
                log = new StringBuilder();
                process = new Process
                {
                    StartInfo = new ProcessStartInfo(Exe, a.ToString())
                        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true, CreateNoWindow = true },
                };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (log) log.AppendLine(e.Data); };
                process.Start();
                process.BeginErrorReadLine();
            }
            catch (Exception e) { process = null; return "ffmpeg: " + e.Message; }
            writer = new Thread(Write) { IsBackground = true, Name = "GoF2 ffmpeg writer" };
            writer.Start();
            return null;
        }

        public static void Video(NativeArray<byte> yuv, int w, int h, long time)
        {
            if (process == null || stopping) return;
            if (frames.Count >= MaxQueuedFrames) { dropped++; return; }   // ffmpeg is behind: the slot is filled later
            frames.Enqueue(((time + ticksPerFrame / 2) / ticksPerFrame, yuv.ToArray()));
            wake.Set();
        }

        /// <summary>From the audio thread: the first two channels (mono doubled) kept.</summary>
        public static void Audio(float[] samples, int count, int channels, long time)
        {
            if (process == null || stopping || soundTemp == null || channels <= 0) return;
            var stereo = new float[count * 2];
            for (int i = 0; i < count; i++)
            {
                stereo[i * 2] = samples[i * channels];
                stereo[i * 2 + 1] = channels > 1 ? samples[i * channels + 1] : samples[i * channels];
            }
            sound.Enqueue(stereo);
        }

        /// <summary>The writer thread: frames into ffmpeg in slot order (a gap filled with the frame before), the sound into
        /// its file, until the stop.</summary>
        static void Write()
        {
            long next = 0;
            byte[] last = null;
            try
            {
                using var soundFile = soundTemp != null ? new FileStream(soundTemp, FileMode.Create, FileAccess.Write) : null;
                var stdin = process.StandardInput.BaseStream;
                var bytes = new byte[0];
                while (true)
                {
                    bool any = false;
                    while (frames.TryDequeue(out var f))
                    {
                        any = true;
                        if (f.slot < next) continue;   // (a slot already filled)
                        for (; last != null && next < f.slot; next++) stdin.Write(last, 0, last.Length);
                        stdin.Write(f.data, 0, f.data.Length);
                        last = f.data;
                        next = f.slot + 1;
                    }
                    while (soundFile != null && sound.TryDequeue(out var s))
                    {
                        any = true;
                        if (bytes.Length < s.Length * 4) bytes = new byte[s.Length * 4];
                        Buffer.BlockCopy(s, 0, bytes, 0, s.Length * 4);
                        soundFile.Write(bytes, 0, s.Length * 4);
                    }
                    if (!any)
                    {
                        if (stopping && frames.IsEmpty && sound.IsEmpty) break;
                        wake.WaitOne(50);
                    }
                }
                stdin.Flush();
            }
            catch (Exception e) { failure = "ffmpeg: " + e.Message; }
        }

        /// <summary>Finishes the clip (the picture, then the sound muxed in). Null, or what went wrong.</summary>
        public static string Stop()
        {
            if (process == null) return null;
            stopping = true;
            wake.Set();
            writer.Join(30000);
            try { process.StandardInput.Close(); } catch (Exception) { }
            bool done = process.WaitForExit(120000);
            int code = done ? process.ExitCode : -1;
            process.Dispose();
            process = null;
            string error = failure ?? (code != 0 ? $"ffmpeg exited with {code}: {Tail()}" : null);
            if (error == null)
            {
                bool withSound = soundTemp != null && File.Exists(soundTemp) && new FileInfo(soundTemp).Length > 0;
                string args = withSound
                    ? $"-hide_banner -loglevel error -y -i \"{videoTemp}\" -f f32le -ar {sampleRate} -ac 2 -i \"{soundTemp}\" -map 0:v -map 1:a -c:v copy -c:a aac -b:a 192k -shortest -movflags +faststart \"{target}\""
                    : $"-hide_banner -loglevel error -y -i \"{videoTemp}\" -c copy -movflags +faststart \"{target}\"";
                error = Run(args);
            }
            TryDelete(videoTemp);
            TryDelete(soundTemp);
            return error;
        }

        static string Run(string args)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(Exe, args)
                    { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true });
                string err = p.StandardError.ReadToEnd();
                p.WaitForExit(120000);
                return p.ExitCode == 0 ? null : $"ffmpeg mux exited with {p.ExitCode}: {err.Trim()}";
            }
            catch (Exception e) { return "ffmpeg: " + e.Message; }
        }

        static string Tail()
        {
            if (log == null) return "";
            lock (log) { string s = log.ToString().Trim(); return s.Length > 300 ? s.Substring(s.Length - 300) : s; }
        }

        static void TryDelete(string file)
        {
            try { if (file != null && File.Exists(file)) File.Delete(file); } catch (Exception) { }
        }
    }
}
#endif
