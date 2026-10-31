// InterstellarSpaceTests.cs
// Feature: the space between the systems (InterstellarSpace, docs/notes/star-map.md "Interstellar space"): the star map's
// rule decides what shows (nothing the player hasn't unlocked, however it is unlocked), a crossing costs what the gate
// route's Khador jump does, the space fits floats, and an arrival is always a real orbit.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class InterstellarSpaceTests : SandboxTest
    {
        bool[] savedVisible;
        Database db;

        [SetUp]
        public void Prepare()
        {
            savedVisible = Session.SystemVisible;
            Session.SystemVisible = null;
            Session.SystemShown.Clear();
            db = Database.Load();
        }

        [TearDown]
        public void Restore()
        {
            Session.SystemVisible = savedVisible;
            Session.SystemShown.Clear();
        }

        static bool Shows(List<InterstellarSpace.Point> points, int system) => points.Exists(p => p.system == system);

        [Test]
        public void ANewGameShowsWhatTheStarMapShowsAndNothingMore()
        {
            var points = InterstellarSpace.Systems(db);
            Assert.Greater(points.Count, 0);
            foreach (var s in db.Systems)
                Assert.AreEqual(GalaxyMap.IsVisible(db, s.index) && InterstellarSpace.ArrivalStation(s) >= 0, Shows(points, s.index), s.name);
            Assert.IsFalse(Shows(points, 0), "Suteo isn't on a new game's map");
        }

        [Test]
        public void ASystemAppearsOnceItsCoordinatesAreBought()
        {
            GalaxyMap.Visibility(db);
            Assert.IsFalse(Shows(InterstellarSpace.Systems(db), 0));
            Session.SystemVisible[0] = true;   // what the lounge's coordinates and the story's reveals set
            Assert.IsTrue(Shows(InterstellarSpace.Systems(db), 0));
        }

        [Test]
        public void AnEventHidingASystemTakesItOutWhateverWasRevealed()
        {
            GalaxyMap.Visibility(db);
            var visible = db.Systems.Find(s => Session.SystemVisible[s.index] && InterstellarSpace.ArrivalStation(s) >= 0);
            Assert.IsTrue(Shows(InterstellarSpace.Systems(db), visible.index));
            GalaxyMap.SetShown(visible.index, false);
            Assert.IsFalse(Shows(InterstellarSpace.Systems(db), visible.index));
            GalaxyMap.SetShown(visible.index, null);
            Assert.IsTrue(Shows(InterstellarSpace.Systems(db), visible.index));
        }

        [Test]
        public void TheVoidIsNoSystemToFlyTo()
        {
            foreach (var p in InterstellarSpace.Systems(db)) Assert.AreNotEqual(Session.VoidOrbit, p.station, p.name);
        }

        [Test]
        public void AnArrivalIsTheGateOrbitElseTheFirstStation()
        {
            foreach (var s in db.Systems)
            {
                int at = InterstellarSpace.ArrivalStation(s);
                if (s.stations == null || s.stations.Count == 0) { Assert.AreEqual(-1, at, s.name); continue; }
                Assert.AreEqual(s.stations.Contains(s.jumpgateStation) ? s.jumpgateStation : s.stations[0], at, s.name);
            }
        }

        [Test]
        public void ALegIsAnAverageGateHop()
        {
            float total = 0f;
            int links = 0;
            foreach (var s in db.Systems)
                foreach (int t in s.jumpRoutesTo)
                    if (t > s.index && db.Systems.Find(x => x.index == t) is SystemData o)
                    {
                        total += (InterstellarSpace.Position(s) - InterstellarSpace.Position(o)).magnitude;
                        links++;
                    }
            Assert.Greater(links, 0);
            Assert.AreEqual(1f, total / links / InterstellarSpace.LegUnits, 0.05f, "one cell a hop, the Khador Drive's rate");
        }

        [Test]
        public void ACrossingCostsAboutWhatTheKhadorJumpDoes()
        {
            GalaxyMap.Visibility(db);
            for (int i = 0; i < Session.SystemVisible.Length; i++) Session.SystemVisible[i] = true;
            int pairs = 0;
            float flown = 0f, jumped = 0f;
            foreach (var a in db.Systems)
                foreach (var b in db.Systems)
                {
                    if (a.index >= b.index || InterstellarSpace.ArrivalStation(a) < 0 || InterstellarSpace.ArrivalStation(b) < 0) continue;
                    var path = GalaxyMap.SystemPath(db, a.index, b.index);
                    if (path == null) continue;
                    float units = (InterstellarSpace.Position(a) - InterstellarSpace.Position(b)).magnitude;
                    flown += InterstellarSpace.CellsFor(units, false);
                    jumped += path.Count - 1;   // GalaxyMap.EnergyCells: a cell a gate hop
                    pairs++;
                }
            Assert.Greater(pairs, 100);
            Assert.That(flown / jumped, Is.InRange(0.5f, 1.25f), "over the galaxy, in the Khador Drive's range: nothing to rebalance");
        }

        [Test]
        public void ExtremePaysDoubleLikeAKhadorJump()
        {
            Assert.AreEqual(1, InterstellarSpace.CellsFor(InterstellarSpace.LegUnits, false));
            Assert.AreEqual(2, InterstellarSpace.CellsFor(InterstellarSpace.LegUnits, true));
            Assert.AreEqual(2, InterstellarSpace.CellsFor(InterstellarSpace.LegUnits + 1f, false));
        }

        [Test]
        public void TheSpaceFitsFloats()
        {
            foreach (var s in db.Systems)
                Assert.Less(OrbitLayout.ToUnity(InterstellarSpace.Position(s)).magnitude, 100000f, s.name);
        }

        [Test]
        public void TheShipStartsOutsideTheSystemItLeftFacingItsWay()
        {
            var from = new Vector3(100000f, 0f, 0f);
            InterstellarSpace.Start(from, Vector3.zero, out var at, out var forward);
            Assert.AreEqual(InterstellarSpace.StartUnits, (at - from).magnitude, 1f);
            Assert.Greater(InterstellarSpace.StartUnits, InterstellarSpace.ArriveUnits, "not back in at once");
            Assert.AreEqual(-1f, forward.x, 0.001f);
            InterstellarSpace.Start(Vector3.zero, Vector3.zero, out _, out forward);
            Assert.AreEqual(Vector3.forward, forward, "a system at the middle still has a way to face");
        }

        [Test]
        public void AnArrivalsPoseIsAJumpsFacingTheStation()
        {
            var orbit = OrbitLayout.Build(db, db.Stations.Find(s => OrbitLayout.Build(db, s.index).hasJumpgate).index);
            orbit.ArrivalPose(-1, out var at, out var facing);
            Assert.AreEqual(0f, (OrbitLayout.ToUnity(orbit.hiddenJumpgate) - at).magnitude, 0.01f);
            Assert.AreEqual(1f, Vector3.Dot(facing * Vector3.forward, -at.normalized), 0.001f, "the view opens on the station");
        }

        [Test]
        public void AFlyInStartsAsFarOutAsItsSpeedCarriesIt()
        {
            Assert.AreEqual(100f, InterstellarSpace.CarryMeters(100, 1000f), 0.01f, "no cruise: the ship's own speed");
            Assert.AreEqual(3150f, InterstellarSpace.CarryMeters(800, 7000f), 0.01f, "x8 easing to x1 averages x4.5");
        }

        [Test]
        public void TheNearestSystemIsWhereAnEmptyHoldFallsOut()
        {
            var points = new List<InterstellarSpace.Point>
            {
                new InterstellarSpace.Point { system = 1, position = new Vector3(0f, 0f, 1000f) },
                new InterstellarSpace.Point { system = 2, position = new Vector3(0f, 0f, 200f) },
            };
            Assert.AreEqual(2, InterstellarSpace.Nearest(points, Vector3.zero).system);
            Assert.IsNull(InterstellarSpace.Nearest(new List<InterstellarSpace.Point>(), Vector3.zero));
        }

        [Test]
        public void ClearingEmptiesTheOrbitItLeft()
        {
            var o = OrbitLayout.Build(db, 78);
            InterstellarSpace.Clear(o);
            Assert.IsTrue(o.interstellar);
            Assert.IsFalse(o.hasStation || o.stationObject || o.hasJumpgate);
            Assert.AreEqual(0, o.planets.Count);
            Assert.AreEqual(0, o.asteroidCount);
            Assert.AreEqual(78, o.stationIndex, "the station stays: a save here is this orbit");
        }

        [Test]
        public void TheOrbitIdIsNoStationAndNoArena()
        {
            foreach (var s in db.Stations) Assert.AreNotEqual(InterstellarSpace.OrbitId, s.index);
            Assert.Less(InterstellarSpace.OrbitId, 100000, "NetArena.OrbitBase: an arena orbit allows fights");
        }
    }
}
