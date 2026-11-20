// Benchmark.cs
// Remake (2026-11-20, the user's request; docs/notes/options.md "Benchmark"): the numbers of the Graphics tab's benchmark.
// UI.BenchmarkRunner flies the scenes and collects the frame times; here is what they add up to (plain C#, tested):
// a scene's average, its 1 % and 0.1 % lows (the frame rate of its slowest hundredth / thousandth of frames), the frame
// rate over time for the graph, and what held each frame back, by the rule of Unity's own Rendering Debugger
// (DebugFrameTiming.DetermineBottleneck: a part whose time is within 20 % of the whole frame's is the limit).
// While a run is on (Active) the game is a throwaway one (SaveGame saves nothing) and the frame rate option's limit is
// lifted (Bootstrap.ApplyFrameRate, FrameGeneration.FrameLimitUs).

using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class Benchmark
    {
        /// <summary>A benchmark run is on, from its start until its results are up.</summary>
        public static bool Active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Active = false;

        /// <summary>What held a frame back. Unknown: the graphics API gave no GPU time.</summary>
        public enum Limit { Unknown, Gpu, Cpu, Present, Balanced }

        /// <summary>The graph's step: the frame rate of every half second.</summary>
        public const float GraphStepMs = 500f;

        [Serializable]
        public sealed class Scene
        {
            public string name;
            public float seconds;
            public int frames;
            /// <summary>Frames a second, of the frames the game rendered (frame generation's extra ones not counted).</summary>
            public float average, low1, low01, minimum, maximum;
            /// <summary>Frames shown for each rendered one (frame generation), 1 without.</summary>
            public float shownPerFrame = 1f;
            /// <summary>The main thread's and the GPU's time for a frame, ms; 0 = not measured.</summary>
            public float cpuMs, gpuMs;
            /// <summary>The limit most frames had (a Limit's name) and the share of the timed frames with each.</summary>
            public string limit = nameof(Limit.Unknown);
            public float gpuShare, cpuShare, presentShare;
            /// <summary>The frame rate of each GraphStepMs.</summary>
            public float[] fps = Array.Empty<float>();
        }

        [Serializable]
        public sealed class Report
        {
            public string date, version, cpu, gpu, memory, system, display, api;
            public List<string> settings = new List<string>();
            public List<Scene> scenes = new List<Scene>();
            public Scene overall;
        }

        /// <summary>DebugFrameTiming.DetermineBottleneck: the GPU's time close to the whole frame's and the CPU threads' not:
        /// the GPU; a CPU thread's close and the GPU's not: the CPU; none close while the main thread waited for the
        /// present: a frame rate limit or the display; else balanced. Times in ms.</summary>
        public static Limit Classify(float frameMs, float mainMs, float renderMs, float gpuMs, float presentWaitMs)
        {
            if (gpuMs <= 0f || mainMs <= 0f || frameMs <= 0f) return Limit.Unknown;
            float near = frameMs * 0.8f;
            bool gpu = gpuMs > near, cpu = mainMs > near || renderMs > near;
            if (gpu && !cpu) return Limit.Gpu;
            if (cpu && !gpu) return Limit.Cpu;
            if (!gpu && !cpu && presentWaitMs > 0.5f) return Limit.Present;
            return Limit.Balanced;
        }

        /// <summary>A scene's numbers from its frames' times (ms, in order); 'limits' counts the frames by Limit.</summary>
        public static Scene Measure(string name, IReadOnlyList<float> frameMs, double cpuMsSum = 0, double gpuMsSum = 0, int timed = 0,
                                    int[] limits = null, float shownPerFrame = 1f)
        {
            var s = new Scene { name = name, frames = frameMs.Count, shownPerFrame = Mathf.Max(1f, shownPerFrame) };
            if (frameMs.Count == 0) return s;
            var sorted = new List<float>(frameMs);
            sorted.Sort((a, b) => b.CompareTo(a));   // the slowest first
            double total = 0;
            foreach (float ms in sorted) total += ms;
            s.seconds = (float)(total / 1000.0);
            s.average = total > 0 ? (float)(frameMs.Count * 1000.0 / total) : 0f;
            s.low1 = Low(sorted, 0.01f);
            s.low01 = Low(sorted, 0.001f);
            s.minimum = Fps(sorted[0]);
            s.maximum = Fps(sorted[sorted.Count - 1]);
            if (timed > 0) { s.cpuMs = (float)(cpuMsSum / timed); s.gpuMs = (float)(gpuMsSum / timed); }
            if (limits != null)
            {
                int known = limits[(int)Limit.Gpu] + limits[(int)Limit.Cpu] + limits[(int)Limit.Present] + limits[(int)Limit.Balanced];
                if (known > 0)
                {
                    s.gpuShare = limits[(int)Limit.Gpu] / (float)known;
                    s.cpuShare = limits[(int)Limit.Cpu] / (float)known;
                    s.presentShare = limits[(int)Limit.Present] / (float)known;
                    int most = (int)Limit.Gpu;
                    for (int i = (int)Limit.Cpu; i <= (int)Limit.Balanced; i++) if (limits[i] > limits[most]) most = i;
                    s.limit = ((Limit)most).ToString();
                }
            }
            // The graph: the frames of each half second.
            var graph = new List<float>();
            double span = 0;
            int count = 0;
            foreach (float ms in frameMs)
            {
                span += ms;
                count++;
                if (span < GraphStepMs) continue;
                graph.Add((float)(count * 1000.0 / span));
                span = 0;
                count = 0;
            }
            if (graph.Count == 0) graph.Add(s.average);
            s.fps = graph.ToArray();
            return s;
        }

        /// <summary>The whole run: the scenes' frames as one (the average by time, the lows over every frame).</summary>
        public static Scene Overall(string name, IReadOnlyList<Scene> scenes, IReadOnlyList<float> allFrameMs, double cpuMsSum, double gpuMsSum,
                                    int timed, int[] limits)
        {
            var s = Measure(name, allFrameMs, cpuMsSum, gpuMsSum, timed, limits);
            double shown = 0, seconds = 0;
            var graph = new List<float>();
            foreach (var scene in scenes)
            {
                shown += scene.shownPerFrame * scene.seconds;
                seconds += scene.seconds;
                graph.AddRange(scene.fps);
            }
            s.shownPerFrame = seconds > 0 ? (float)(shown / seconds) : 1f;
            s.fps = graph.ToArray();   // (scene by scene: a half second never spans two)
            return s;
        }

        static float Fps(float ms) => ms > 0f ? 1000f / ms : 0f;

        /// <summary>The frame rate of the slowest 'share' of the frames (at least one), 'sorted' slowest first.</summary>
        static float Low(List<float> sorted, float share)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(sorted.Count * share));
            double total = 0;
            for (int i = 0; i < n; i++) total += sorted[i];
            return total > 0 ? (float)(n * 1000.0 / total) : 0f;
        }
    }
}
