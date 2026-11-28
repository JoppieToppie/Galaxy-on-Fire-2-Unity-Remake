// MovieTimelineTests.cs
// Feature: docs/notes/event-graphs.md "Movie mode": the timeline's cues become one event script (sorted, a wait before
// each, a wait to the end) that the runner accepts; the camera keys make a path (Catmull-Rom between keys, held before /
// after them, a cut holds the key before it); the lanes by command word; saving and opening a movie.
// Expected values: worked by hand from the rules in Events.MovieTimeline.

using GoF2Remake.Events;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class MovieTimelineTests : SandboxTest
    {
        static MovieTimeline Sample()
        {
            var m = new MovieTimeline { name = "test", length = 10f };
            m.cues.Add(new MovieCue { time = 4f, command = "fade @a out 1" });
            m.cues.Add(new MovieCue { time = 1.5f, command = "/spawn @a 3 enemy named Ship1 at 0 0 1000" });
            m.cues.Add(new MovieCue { time = 4f, command = "radio @a player : Go!" });
            m.cues.Add(new MovieCue { time = 6f, command = "   " });
            return m;
        }

        [Test]
        public void TheCuesBecomeOneScriptInTimeOrder()
        {
            string script = Sample().Script();
            Assert.AreEqual("set autostop = 0\nwait 1.5\nspawn @a 3 enemy named Ship1 at 0 0 1000\nwait 2.5\nfade @a out 1\n" +
                            "radio @a player : Go!\nwait 6\n", script);
            Assert.IsNull(EventRunner.Check(script), "the runner reads the take's script");
        }

        [Test]
        public void ACueAtZeroNeedsNoWait()
        {
            var m = new MovieTimeline { length = 2f };
            m.cues.Add(new MovieCue { time = 0f, command = "letterbox @a on" });
            Assert.AreEqual("set autostop = 0\nletterbox @a on\nwait 2\n", m.Script());
        }

        [Test]
        public void TheCameraFollowsItsKeys()
        {
            var m = new MovieTimeline { length = 10f };
            Assert.IsFalse(m.CameraAt(1f, out _, out _, out _), "no keys: no camera path");
            // Evenly spaced keys on a line: between two inner keys the spline is the line; from an end key it eases out
            // (the end key stands in for its missing neighbour: 0.5 (p2 - p0) u + (2 p0 - 5 p1 + 4 p2 - p3) u^2 / 2 + ...).
            m.keys.Add(new MovieCameraKey { time = 2f, position = new Vector3(0, 0, 0), fov = 40f });
            m.keys.Add(new MovieCameraKey { time = 4f, position = new Vector3(100, 0, 0), fov = 60f });
            m.keys.Add(new MovieCameraKey { time = 6f, position = new Vector3(200, 0, 0), fov = 60f });
            m.keys.Add(new MovieCameraKey { time = 8f, position = new Vector3(300, 0, 0), fov = 60f });
            m.CameraAt(0f, out var p, out _, out float fov);
            Assert.AreEqual(Vector3.zero, p, "held at the first key before it");
            Assert.AreEqual(40f, fov);
            m.CameraAt(3f, out p, out _, out fov);
            Assert.AreEqual(43.75f, p.x, 1e-3f, "eased out of the first key");
            Assert.AreEqual(50f, fov, 1e-3f);
            m.CameraAt(5f, out p, out _, out _);
            Assert.AreEqual(150f, p.x, 1e-3f, "straight between inner keys");
            m.CameraAt(9f, out p, out _, out _);
            Assert.AreEqual(300f, p.x, 1e-3f, "held at the last key after it");
        }

        [Test]
        public void ACutHoldsTheKeyBeforeIt()
        {
            var m = new MovieTimeline { length = 10f };
            m.keys.Add(new MovieCameraKey { time = 1f, position = new Vector3(0, 0, 0) });
            m.keys.Add(new MovieCameraKey { time = 3f, position = new Vector3(500, 0, 0), cut = true });
            m.CameraAt(2.9f, out var p, out _, out _);
            Assert.AreEqual(Vector3.zero, p);
            m.CameraAt(3f, out p, out _, out _);
            Assert.AreEqual(500f, p.x, 1e-3f);
        }

        [Test]
        public void TheLanesGoByTheCommandWord()
        {
            Assert.AreEqual(MovieTrack.Scene, MovieTimeline.TrackOf("camera @a player"));
            Assert.AreEqual(MovieTrack.Ships, MovieTimeline.TrackOf("/npc @a Ship1 goto 0 0 0"));
            Assert.AreEqual(MovieTrack.Text, MovieTimeline.TrackOf("TITLE @a Hello"));
            Assert.AreEqual(MovieTrack.Sound, MovieTimeline.TrackOf("music @a battle"));
            Assert.AreEqual(MovieTrack.Other, MovieTimeline.TrackOf("credits @a 5"));
        }

        [Test]
        public void AMovieIsSavedAndOpened()
        {
            var m = Sample();
            m.name = "my film!";
            m.keys.Add(new MovieCameraKey { time = 2f, position = new Vector3(1, 2, 3), rotation = Quaternion.Euler(0, 90, 0), fov = 35f, cut = true });
            m.Save();
            Assert.AreEqual("my_film", m.name, "a file-safe name");
            CollectionAssert.Contains(MovieTimeline.List(), "my_film");
            var back = MovieTimeline.Load("my_film");
            Assert.IsNotNull(back);
            Assert.AreEqual(4, back.cues.Count);
            Assert.AreEqual(1.5f, back.cues[0].time, "sorted on saving");
            Assert.AreEqual(35f, back.keys[0].fov);
            Assert.IsTrue(back.keys[0].cut);
            Assert.AreEqual(m.Script(), back.Script());
            Assert.IsNull(MovieTimeline.Load("nothing_here"));
        }
    }
}
