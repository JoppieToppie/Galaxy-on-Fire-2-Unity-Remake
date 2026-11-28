// MovieModeFlightTests.cs
// Feature: docs/notes/event-graphs.md "Movie mode": in flight, Movie mode holds the world while a scene is set up; Play
// runs the timeline as one event run with the world going (a spawn cue's ship appears, the camera flies the keys), Stop
// holds it again, and the next take clears the last take's ships first so it starts on the same scene.
// Expected values: the rules in UI.MovieMode / Events.MovieTimeline.

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GoF2Remake.Data;
using GoF2Remake.Events;
using GoF2Remake.UI;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GoF2Remake.Tests
{
    public class MovieModeFlightTests : PlayModeTest
    {
        /// <summary>A station of a secure system (Augmenta, security 3): no raiders in the take.</summary>
        static int QuietStation => Db.Stations.First(s => s.system == 19).index;

        static List<NpcShip> TakeShips() =>
            Find<Traffic>().Ships.FindAll(s => s != null && !s.Gone && s.Spec != null && s.Spec.eventTag != 0);

        static string Coords(Vector3 game) => $"{Mathf.RoundToInt(game.x)} {Mathf.RoundToInt(game.y)} {Mathf.RoundToInt(game.z)}";

        [UnityTest]
        public IEnumerator ATakeSpawnsItsShipFliesTheKeysAndTheNextTakeStartsClean()
        {
            FreePlayAt(QuietStation);
            Session.LaunchedFromStation = true;
            yield return Load("Space");
            yield return WaitFor(() => Find<SpaceLevel>() != null && Find<SpaceLevel>().LaunchCameraOver && Find<SpaceLevel>().Navigation != null, 60f, "the orbit");
            var level = Find<SpaceLevel>();
            var movie = Get<MovieMode>(Find<FlightHud>(), "movie");
            var cam = Camera.main;

            Assert.IsTrue(MovieMode.Available(level));
            movie.Enter(level);
            Assert.IsTrue(movie.Active);
            Assert.AreEqual(0f, Time.timeScale, "the world holds while the scene is set up");

            var ship = level.Player.transform;
            var timeline = new MovieTimeline { name = "test", length = 4f };
            timeline.keys.Add(new MovieCameraKey { time = 0f, position = OrbitScale.ToGame(ship.position + ship.right * 80f), rotation = ship.rotation, fov = 50f });
            timeline.keys.Add(new MovieCameraKey { time = 4f, position = OrbitScale.ToGame(ship.position - ship.right * 80f), rotation = ship.rotation, fov = 30f });
            timeline.cues.Add(new MovieCue { time = 0.2f, command = $"spawn @a {Session.ShipIndex} neutral named Extra1 at {Coords(OrbitScale.ToGame(ship.position + ship.forward * 400f))}" });
            typeof(MovieMode).GetField("movie", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, timeline);

            Invoke(movie, "Play");
            Assert.IsTrue(movie.Playing);
            Assert.AreEqual(1f, Time.timeScale, "the world runs in a take");
            yield return WaitFor(() => TakeShips().Count == 1, 10f, "the cue's ship");
            var first = TakeShips()[0];
            Assert.AreEqual("Extra1", first.Target.displayName);

            yield return new WaitForEndOfFrame();   // after FlightHud's LateUpdate placed the camera for this frame's clock
            timeline.CameraAt(Get<float>(movie, "clock"), out var p, out _, out float fov);
            Assert.Less(Vector3.Distance(cam.transform.position, OrbitScale.ToUnity(p)), 0.5f, "the camera on its keys");
            Assert.AreEqual(fov, cam.fieldOfView, 0.01f);

            Invoke(movie, "StopTake");
            Assert.IsFalse(movie.Playing);
            Assert.AreEqual(0f, Time.timeScale, "held again after the take");

            Invoke(movie, "Play");
            Assert.IsTrue(first.Gone, "the last take's ship cleared");
            yield return WaitFor(() => TakeShips().Count == 1 && TakeShips()[0] != first, 10f, "the new take's ship");

            movie.Exit();
            Assert.IsFalse(movie.Active);
            Assert.IsFalse(EventRunner.IsRunning("movie"), "leaving ends the take");
            Assert.AreEqual(1f, Time.timeScale);
        }
    }
}
