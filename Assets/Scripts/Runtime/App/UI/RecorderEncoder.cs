// RecorderEncoder.cs
// Remake-only: the screen recorder's platform encoder (ScreenRecorder feeds it; docs/notes/platforms.md "Screen recorder"):
// Windows (player and Editor) the native gof2_recorder.dll (Native/Recorder, Media Foundation), Android the Java
// ClipEncoder (MediaCodec + MediaMuxer) through JNI: the frame as a direct ByteBuffer over the readback, the sound as a
// float array from the audio thread (attached to the VM once; the class and method ids looked up on the main thread,
// where the app's class loader finds them), Linux the system's ffmpeg (FfmpegEncoder; Windows too with
// $GOF2_RECORDER=ffmpeg). Anywhere else there is none (Supported false). Times in 100 ns.

using System;
using Unity.Collections;
using UnityEngine;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class RecorderEncoder
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        const string Dll = "gof2_recorder";
        [System.Runtime.InteropServices.DllImport(Dll, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int gof2rec_start(string path, int w, int h, int fps, int kbps, int rate, int channels, int hdr, int peakNits, int averageNits);
        [System.Runtime.InteropServices.DllImport(Dll)] static extern void gof2rec_video(byte[] yuv, int w, int h, long time);
        [System.Runtime.InteropServices.DllImport(Dll)] static extern void gof2rec_audio(float[] samples, int frames, int channels, long time);
        [System.Runtime.InteropServices.DllImport(Dll)] static extern int gof2rec_stop();
        [System.Runtime.InteropServices.DllImport(Dll)] static extern int gof2rec_dropped();

        static byte[] frame = new byte[0];
        static bool ffmpeg;   // this recording goes through ffmpeg ($GOF2_RECORDER=ffmpeg when it started)
        static bool FfmpegAsked => Environment.GetEnvironmentVariable("GOF2_RECORDER") == "ffmpeg";

        public static bool Supported => !FfmpegAsked || FfmpegEncoder.Found;

        /// <summary>Null, or why the file couldn't be started; 'hdr': HDR10 (HEVC Main 10, 16-bit frames), refused when the
        /// PC has no such encoder (then SDR).</summary>
        public static string Start(string path, int w, int h, int fps, int kbps, int rate, int channels, bool hdr, int peakNits, int averageNits)
        {
            ffmpeg = FfmpegAsked;
            if (ffmpeg) return FfmpegEncoder.Start(path, w, h, fps, kbps, rate, channels, hdr, peakNits, averageNits);
            try
            {
                int hr = gof2rec_start(path, w, h, fps, kbps, rate, channels, hdr ? 1 : 0, peakNits, averageNits);
                return hr == 0 ? null : $"Media Foundation 0x{hr:X8}";
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException) { return "gof2_recorder.dll: " + e.Message; }
        }

        /// <summary>A frame: planar 4:2:0, 8-bit (I420) or 16-bit samples in HDR.</summary>
        public static void Video(NativeArray<byte> yuv, int w, int h, long time)
        {
            if (ffmpeg) { FfmpegEncoder.Video(yuv, w, h, time); return; }
            if (frame.Length != yuv.Length) frame = new byte[yuv.Length];
            NativeArray<byte>.Copy(yuv, frame, yuv.Length);
            gof2rec_video(frame, w, h, time);
        }

        public static void Audio(float[] samples, int frames, int channels, long time)
        {
            if (ffmpeg) FfmpegEncoder.Audio(samples, frames, channels, time);
            else gof2rec_audio(samples, frames, channels, time);
        }

        /// <summary>Null, or what went wrong while writing (the file is then unusable).</summary>
        public static string Stop()
        {
            if (ffmpeg) return FfmpegEncoder.Stop();
            int hr = gof2rec_stop();
            return hr == 0 ? null : $"Media Foundation 0x{hr:X8}";
        }

        public static int Dropped => ffmpeg ? FfmpegEncoder.Dropped : gof2rec_dropped();
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
        public static bool Supported => FfmpegEncoder.Found;
        public static string Start(string path, int w, int h, int fps, int kbps, int rate, int channels, bool hdr, int peakNits, int averageNits) =>
            FfmpegEncoder.Start(path, w, h, fps, kbps, rate, channels, hdr, peakNits, averageNits);
        public static void Video(NativeArray<byte> yuv, int w, int h, long time) => FfmpegEncoder.Video(yuv, w, h, time);
        public static void Audio(float[] samples, int frames, int channels, long time) => FfmpegEncoder.Audio(samples, frames, channels, time);
        public static string Stop() => FfmpegEncoder.Stop();
        public static int Dropped => FfmpegEncoder.Dropped;
#elif UNITY_ANDROID && !UNITY_EDITOR
        static IntPtr cls, startId, videoId, audioId, stopId, droppedId;
        [ThreadStatic] static bool attached;

        public static bool Supported => true;

        static void Ids()
        {
            if (cls != IntPtr.Zero) return;
            var local = AndroidJNI.FindClass("com/joppietoppie/gof2remake/ClipEncoder");
            cls = AndroidJNI.NewGlobalRef(local);
            AndroidJNI.DeleteLocalRef(local);
            startId = AndroidJNI.GetStaticMethodID(cls, "start", "(Ljava/lang/String;IIIIIZII)Ljava/lang/String;");
            videoId = AndroidJNI.GetStaticMethodID(cls, "video", "(Ljava/nio/ByteBuffer;J)V");
            audioId = AndroidJNI.GetStaticMethodID(cls, "audio", "([FIIJ)V");
            stopId = AndroidJNI.GetStaticMethodID(cls, "stop", "()Ljava/lang/String;");
            droppedId = AndroidJNI.GetStaticMethodID(cls, "dropped", "()I");
        }

        public static string Start(string path, int w, int h, int fps, int kbps, int rate, int channels, bool hdr, int peakNits, int averageNits)
        {
            try
            {
                Ids();
                var jpath = AndroidJNI.NewStringUTF(path);
                string result = AndroidJNI.CallStaticStringMethod(cls, startId, new[]
                {
                    new jvalue { l = jpath }, new jvalue { i = w }, new jvalue { i = h }, new jvalue { i = fps },
                    new jvalue { i = kbps * 1000 }, new jvalue { i = rate }, new jvalue { z = hdr }, new jvalue { i = peakNits }, new jvalue { i = averageNits },
                });
                AndroidJNI.DeleteLocalRef(jpath);
                return string.IsNullOrEmpty(result) ? null : result;
            }
            catch (Exception e) { return e.Message; }
        }

        public static void Video(NativeArray<byte> yuv, int w, int h, long time)
        {
            var buffer = AndroidJNI.NewDirectByteBuffer(yuv);
            AndroidJNI.CallStaticVoidMethod(cls, videoId, new[] { new jvalue { l = buffer }, new jvalue { j = time / 10 } });
            AndroidJNI.DeleteLocalRef(buffer);
        }

        /// <summary>From the audio thread.</summary>
        public static void Audio(float[] samples, int frames, int channels, long time)
        {
            if (cls == IntPtr.Zero) return;
            if (!attached) { AndroidJNI.AttachCurrentThread(); attached = true; }
            var array = AndroidJNI.ToFloatArray(samples);
            AndroidJNI.CallStaticVoidMethod(cls, audioId, new[] { new jvalue { l = array }, new jvalue { i = frames }, new jvalue { i = channels }, new jvalue { j = time / 10 } });
            AndroidJNI.DeleteLocalRef(array);
        }

        public static string Stop()
        {
            string result = AndroidJNI.CallStaticStringMethod(cls, stopId, new jvalue[0]);
            return string.IsNullOrEmpty(result) ? null : result;
        }

        public static int Dropped => cls == IntPtr.Zero ? 0 : AndroidJNI.CallStaticIntMethod(cls, droppedId, new jvalue[0]);
#else
        public static bool Supported => false;
        public static string Start(string path, int w, int h, int fps, int kbps, int rate, int channels, bool hdr, int peakNits, int averageNits) => "not on this platform";
        public static void Video(NativeArray<byte> yuv, int w, int h, long time) { }
        public static void Audio(float[] samples, int frames, int channels, long time) { }
        public static string Stop() => null;
        public static int Dropped => 0;
#endif
    }
}
