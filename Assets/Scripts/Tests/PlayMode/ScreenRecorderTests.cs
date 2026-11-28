// ScreenRecorderTests.cs
// Feature: docs/notes/platforms.md "Screen recorder": in flight the recorder writes an MP4 of the screen (the encoder's
// file starts with an "ftyp" box), its thumbnail and its length go beside it, and the clips list shows it; a Movie mode
// take with "record takes" on is a clip of its own. Windows only (the Editor's encoder: gof2_recorder.dll).
// Expected values: the rules in UI.ScreenRecorder / ClipLibrary.

using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using GoF2Remake.Data;
using GoF2Remake.Events;
using GoF2Remake.UI;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GoF2Remake.Tests
{
    public class ScreenRecorderTests : PlayModeTest
    {
        static int QuietStation => Db.Stations.First(s => s.system == 19).index;

        IEnumerator Orbit()
        {
            FreePlayAt(QuietStation);
            Session.LaunchedFromStation = true;
            yield return Load("Space");
            yield return WaitFor(() => Find<SpaceLevel>() != null && Find<SpaceLevel>().LaunchCameraOver, 60f, "the orbit");
            ScreenRecorder.Install();
        }

        static void AssertMp4(string path)
        {
            Assert.IsTrue(File.Exists(path), path);
            var head = new byte[8];
            using (var f = File.OpenRead(path)) f.Read(head, 0, 8);
            Assert.AreEqual("ftyp", Encoding.ASCII.GetString(head, 4, 4), "an MP4");
            Assert.Greater(new FileInfo(path).Length, 20_000);
        }

        [UnityTest]
        public IEnumerator ARecordingIsAnMp4WithItsThumbnailAndLength()
        {
            if (!ScreenRecorder.Supported) Assert.Ignore("no encoder on this platform");
            yield return Orbit();
            Assert.IsTrue(ScreenRecorder.Begin("test"));
            Assert.IsTrue(ScreenRecorder.Recording);
            string path = ScreenRecorder.CurrentPath;
            yield return Run(2.5f);
            ScreenRecorder.Stop();
            Assert.IsFalse(ScreenRecorder.Recording);
            AssertMp4(path);
            var clip = ClipLibrary.List().Single();
            Assert.AreEqual(Path.GetFileNameWithoutExtension(path), clip.name);
            Assert.That(clip.seconds, Is.InRange(2f, 3f));
            yield return WaitFor(() => ClipLibrary.List().Single().thumbnail != null, 5f, "the thumbnail");
        }

        /// <summary>Linux's encoder (FfmpegEncoder), which Windows takes with $GOF2_RECORDER=ffmpeg: the clip has its picture
        /// and its sound (two streams by the MP4's track boxes).</summary>
        [UnityTest]
        public IEnumerator AnFfmpegRecordingIsAnMp4WithSound()
        {
            if (!FfmpegEncoder.Found) Assert.Ignore("no ffmpeg on this machine");
            yield return Orbit();
            System.Environment.SetEnvironmentVariable("GOF2_RECORDER", "ffmpeg");
            try
            {
                Assert.IsTrue(ScreenRecorder.Begin("ffmpeg"));
                string path = ScreenRecorder.CurrentPath;
                yield return Run(2.5f);
                ScreenRecorder.Stop();
                AssertMp4(path);
                string text = Encoding.ASCII.GetString(File.ReadAllBytes(path));
                Assert.AreEqual(2, System.Text.RegularExpressions.Regex.Matches(text, "trak").Count, "a picture and a sound track");
                Assert.That(ClipLibrary.List().Single().seconds, Is.InRange(2f, 3f));
            }
            finally { System.Environment.SetEnvironmentVariable("GOF2_RECORDER", null); }
        }

        [UnityTest]
        public IEnumerator AMovieTakeIsAClipOfItsOwn()
        {
            if (!ScreenRecorder.Supported) Assert.Ignore("no encoder on this platform");
            yield return Orbit();
            var movie = Get<MovieMode>(Find<FlightHud>(), "movie");
            movie.Enter(Find<SpaceLevel>());
            var timeline = new MovieTimeline { name = "ambush", length = 1.5f };
            timeline.cues.Add(new MovieCue { time = 0f, command = "letterbox @a on" });
            typeof(MovieMode).GetField("movie", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, timeline);
            typeof(MovieMode).GetField("recordTakes", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, true);
            try
            {
                Invoke(movie, "Play");
                Assert.IsTrue(ScreenRecorder.Recording, "the take records");
                yield return WaitFor(() => !movie.Playing, 10f, "the take's end");
                Assert.IsFalse(ScreenRecorder.Recording, "the take's end stops it");
                var clip = ClipLibrary.List().Single();
                StringAssert.StartsWith("ambush_", clip.name);
                AssertMp4(clip.path);
            }
            finally
            {
                typeof(MovieMode).GetField("recordTakes", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, false);
                movie.Exit();
            }
        }
    }
}
