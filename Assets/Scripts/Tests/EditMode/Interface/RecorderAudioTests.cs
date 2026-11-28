// RecorderAudioTests.cs
// Feature: docs/notes/platforms.md "Screen recorder": a mix at another rate than 44.1 / 48 kHz (a phone's own) is
// resampled to the clip's 48 kHz, linearly and seamlessly across the audio blocks, stereo out of any channel count.
// Expected values: a 96 kHz ramp halves; the samples between two blocks continue the ramp.

using System.Reflection;
using GoF2Remake.UI;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class RecorderAudioTests
    {
        const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;

        static int Resample(float[] data, int frames, int channels) =>
            (int)typeof(ScreenRecorder).GetMethod("Resample", Static).Invoke(null, new object[] { data, frames, channels });

        static float[] Out => (float[])typeof(ScreenRecorder).GetField("resampled", Static).GetValue(null);

        static void Rates(int input, int clip)
        {
            typeof(ScreenRecorder).GetField("inputRate", Static).SetValue(null, input);
            typeof(ScreenRecorder).GetField("rate", Static).SetValue(null, clip);
            typeof(ScreenRecorder).GetField("resamplePos", Static).SetValue(null, 0.0);
            var previous = (float[])typeof(ScreenRecorder).GetField("previous", Static).GetValue(null);
            previous[0] = previous[1] = 0f;
        }

        [Test]
        public void A96KhzMixIsHalvedSeamlessly()
        {
            Rates(96000, 48000);
            var block = new float[8];   // mono, a ramp 0..7, then 8..15
            for (int i = 0; i < 8; i++) block[i] = i;
            int n = Resample(block, 8, 1);
            var first = (float[])Out.Clone();
            for (int i = 0; i < 8; i++) block[i] = 8 + i;
            int m = Resample(block, 8, 1);
            var second = Out;
            Assert.AreEqual(4, n);
            Assert.AreEqual(4, m);
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(i * 2f, first[i * 2], 1e-4f, "left");
                Assert.AreEqual(i * 2f, first[i * 2 + 1], 1e-4f, "mono doubled");
                Assert.AreEqual(8f + i * 2f, second[i * 2], 1e-4f, "the next block goes on where the first ended");
            }
        }

        [Test]
        public void A44KhzToThe48KhzClipAddsFramesBetween()
        {
            Rates(32000, 48000);   // 2 input frames make 3
            var block = new float[] { 0f, 0f, 3f, 30f, 6f, 60f, 9f, 90f };   // stereo
            int n = Resample(block, 4, 2);
            Assert.AreEqual(5, n);   // positions 0, 2/3, 4/3, 2, 8/3
            Assert.AreEqual(2f, Out[2], 1e-4f, "left at 2/3");
            Assert.AreEqual(20f, Out[3], 1e-4f, "right at 2/3");
            Assert.AreEqual(6f, Out[6], 1e-4f, "left at 2");
        }
    }
}
