// BenchmarkTests.cs
// The benchmark's numbers (Data.Benchmark): averages, lows, the graph's steps and what held a frame back.

using System.Collections.Generic;
using GoF2Remake.Data;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class BenchmarkTests
    {
        [Test]
        public void AverageAndLowsComeFromTheFrameTimes()
        {
            // 990 frames of 10 ms and 10 of 100 ms: 10.9 s, 1000 frames
            var ms = new List<float>();
            for (int i = 0; i < 990; i++) ms.Add(10f);
            for (int i = 0; i < 10; i++) ms.Add(100f);
            var s = Benchmark.Measure("test", ms);
            Assert.AreEqual(1000, s.frames);
            Assert.AreEqual(10.9f, s.seconds, 0.001f);
            Assert.AreEqual(1000f / 10.9f, s.average, 0.01f);
            Assert.AreEqual(10f, s.low1, 0.01f);      // the slowest 1 %: the ten 100 ms frames
            Assert.AreEqual(10f, s.low01, 0.01f);     // the slowest 0.1 %: one of them
            Assert.AreEqual(10f, s.minimum, 0.01f);
            Assert.AreEqual(100f, s.maximum, 0.01f);
            // the graph: a step for every half second of frames
            Assert.AreEqual(21, s.fps.Length);
            Assert.AreEqual(100f, s.fps[0], 0.01f);
        }

        [Test]
        public void EmptyAndShortRunsGiveNumbers()
        {
            Assert.AreEqual(0f, Benchmark.Measure("none", new List<float>()).average);
            var one = Benchmark.Measure("one", new List<float> { 20f });
            Assert.AreEqual(50f, one.average, 0.01f);
            Assert.AreEqual(50f, one.low1, 0.01f);
            Assert.AreEqual(1, one.fps.Length);
        }

        [Test]
        public void TheLimitIsThePartThatFillsTheFrame()
        {
            // frame, main thread, render thread, GPU, present wait (ms)
            Assert.AreEqual(Benchmark.Limit.Gpu, Benchmark.Classify(10f, 3f, 2f, 9.5f, 5f));
            Assert.AreEqual(Benchmark.Limit.Cpu, Benchmark.Classify(10f, 9f, 2f, 4f, 0f));
            Assert.AreEqual(Benchmark.Limit.Cpu, Benchmark.Classify(10f, 3f, 9f, 4f, 0f));
            Assert.AreEqual(Benchmark.Limit.Present, Benchmark.Classify(16.6f, 3f, 2f, 4f, 10f));   // a frame rate limit
            Assert.AreEqual(Benchmark.Limit.Balanced, Benchmark.Classify(10f, 9f, 2f, 9f, 0f));
            Assert.AreEqual(Benchmark.Limit.Unknown, Benchmark.Classify(10f, 9f, 2f, 0f, 0f));      // no GPU time
        }

        [Test]
        public void TheSceneTakesTheLimitOfMostFrames()
        {
            var limits = new int[5];
            limits[(int)Benchmark.Limit.Gpu] = 70;
            limits[(int)Benchmark.Limit.Cpu] = 20;
            limits[(int)Benchmark.Limit.Balanced] = 10;
            limits[(int)Benchmark.Limit.Unknown] = 50;   // (not counted)
            var s = Benchmark.Measure("test", new List<float> { 10f, 10f }, 6.0, 18.0, 2, limits, 2f);
            Assert.AreEqual("Gpu", s.limit);
            Assert.AreEqual(0.7f, s.gpuShare, 0.001f);
            Assert.AreEqual(0.2f, s.cpuShare, 0.001f);
            Assert.AreEqual(3f, s.cpuMs, 0.001f);
            Assert.AreEqual(9f, s.gpuMs, 0.001f);
            Assert.AreEqual(2f, s.shownPerFrame);
        }
    }
}
