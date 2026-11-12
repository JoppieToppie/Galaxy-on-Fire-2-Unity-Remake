// InterstellarSpaceTests.cs
// Feature: the space between the systems (InterstellarSpace, docs/notes/star-map.md "Interstellar space"): the star map's
// rule decides what shows (nothing the player hasn't unlocked, however it is unlocked), a crossing costs what the gate
// route's Khador jump does, the space fits floats, and an arrival is always a real orbit; the Void is no place, but a short
// dive off the galaxy's disc once the story has been there.

using System.Collections.Generic;
using System.Linq;
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

        // The user (2026-11-04): Elite Dangerous's units between the systems, so the numbers fall fast on the way in.
        [Test]
        public void DistancesReadInEliteDangerousUnits()
        {
            float ly = InterstellarSpace.UnitsPerLightYear;
            double perLs = InterstellarSpace.MetersPerLightSecond / InterstellarSpace.MetersPerLightYear;
            Assert.AreEqual("12.50 ly", InterstellarSpace.FormatSpaceDistance(12.5f * ly));
            Assert.AreEqual("2,000 ly", InterstellarSpace.FormatSpaceDistance(2000f * ly));
            Assert.AreEqual("2.00 ly", InterstellarSpace.FormatSpaceDistance(2f * 75f * ly, 75f), "at the zone's scale");
            StringAssert.EndsWith(" Ls", InterstellarSpace.FormatSpaceDistance(0.5f * ly), "under a light year: light seconds");
            Assert.AreEqual("50.00 Ls", InterstellarSpace.FormatSpaceDistance((float)(50 * perLs * ly)));
            Assert.AreEqual("150.0 Mm", InterstellarSpace.FormatSpaceDistance((float)(150e6 / InterstellarSpace.MetersPerLightYear * ly)));
            Assert.AreEqual("42.0 km", InterstellarSpace.FormatSpaceDistance((float)(42e3 / InterstellarSpace.MetersPerLightYear * ly)));
            Assert.AreEqual("0 m", InterstellarSpace.FormatSpaceDistance(0f));
        }

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
        public void TheVoidIsNoPlaceOnTheMap()
        {
            Session.FreePlay = true;
            Assert.IsTrue(InterstellarSpace.VoidShown, "free play offers the dive");
            foreach (var p in InterstellarSpace.Systems(db)) Assert.AreNotEqual(Session.VoidOrbit, p.station, p.name);
        }

        [Test]
        public void TheDiveIsOfferedOnceTheStoryHasBeenInTheVoid()
        {
            Session.FreePlay = false;
            Session.CampaignMission = 24;
            Assert.IsFalse(InterstellarSpace.VoidShown, "not before the wormhole ride into it");
            Session.CampaignMission = 25;
            Assert.IsTrue(InterstellarSpace.VoidShown);
        }

        [Test]
        public void ADiveGoesOffTheDiscOnTheShipsSide()
        {
            var way = InterstellarSpace.DiveWay(new Vector3(0f, 0f, 1000f), Vector3.right);
            Assert.Greater(way.z, 0.8f, "off the disc, away from it");
            Assert.Greater(way.x, 0.1f, "leaning the ship's way");
            Assert.AreEqual(1f, way.magnitude, 0.001f);
            Assert.Less(InterstellarSpace.DiveWay(new Vector3(0f, 0f, -1000f), Vector3.right).z, -0.8f, "the other side: the other way");
            Assert.AreEqual(Vector3.forward, InterstellarSpace.DiveWay(Vector3.zero, Vector3.forward), "straight off it");
        }

        [Test]
        public void ADiveIsTenToFifteenSecondsOnTheDrive()
        {
            Assert.AreEqual(10000f, InterstellarSpace.DiveMinMs);
            Assert.AreEqual(15000f, InterstellarSpace.DiveMaxMs);
            Assert.AreEqual(InterstellarSpace.ArriveUnits, InterstellarSpace.DiveUnits(800, 0f), "its end is the edge");
            Assert.AreEqual(InterstellarSpace.ArriveUnits + 16000f * 10f, InterstellarSpace.DiveUnits(800, 10000f), 1f, "800 % of 100 m/s");
            Assert.LessOrEqual(InterstellarSpace.CellsFor(InterstellarSpace.DiveUnits(800, InterstellarSpace.DiveMaxMs) - InterstellarSpace.ArriveUnits, false), 1,
                               "a cell at most, under the Khador Drive's two");
        }

        [Test]
        public void ALeaveStartsFromTheOrbitsSystemOrWhereTheVoidWasEntered()
        {
            Session.VoidReturnStation = 78;
            Assert.AreEqual(InterstellarSpace.PositionOf(db, 78), InterstellarSpace.PositionOf(db, Session.VoidOrbit));
            var st = db.Stations.Find(s => s.index == 78);
            Assert.AreEqual(InterstellarSpace.Position(db, db.Systems.Find(s => s.index == st.system)), InterstellarSpace.PositionOf(db, 78));
        }

        [Test]
        public void LeavingTheVoidDropsItsRulesBetweenTheSystems()
        {
            var o = OrbitLayout.Build(db, Session.VoidOrbit);
            Assert.IsTrue(o.alienOrbit);
            InterstellarSpace.Clear(o);
            Assert.IsFalse(o.alienOrbit, "its no-autopilot rule would hold every system target");
            Assert.AreEqual(Session.VoidOrbit, o.stationIndex, "a save here is the Void");
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
                        total += (InterstellarSpace.Position(db, s) - InterstellarSpace.Position(db, o)).magnitude;
                        links++;
                    }
            Assert.Greater(links, 0);
            Assert.AreEqual(1f, total / links / InterstellarSpace.LegUnits, 0.001f, "one cell a hop, the Khador Drive's rate (the layout's scale)");
        }

        [Test]
        public void TheSystemsAreSpreadOutTheMapsWay()
        {
            var laid = new List<(SystemData s, Vector3 at)>();
            foreach (var s in db.Systems)
                if (InterstellarSpace.ArrivalStation(s) >= 0) laid.Add((s, InterstellarSpace.Position(db, s)));
            Assert.Greater(laid.Count, 20);
            float least = float.MaxValue;
            for (int i = 0; i < laid.Count; i++)
                for (int j = i + 1; j < laid.Count; j++) least = Mathf.Min(least, (laid[i].at - laid[j].at).magnitude);
            Assert.Greater(least, InterstellarSpace.LegUnits * 0.65f, "no two systems crowd each other (the map's closest were 0.4 legs)");
            var heights = laid.ConvertAll(l => InterstellarSpace.Height(l.s.index));
            Assert.That(heights, Is.All.InRange(-1f, 1f));
            Assert.Greater(heights.Max() - heights.Min(), 1f, "lifted off the disc both ways");
            // the map's arrangement holds: east stays east of west, top above bottom
            var map = laid.ConvertAll(l => GalaxyMap.SunPosition(l.s));
            int kept = 0, pairs = 0;
            for (int i = 0; i < laid.Count; i++)
                for (int j = i + 1; j < laid.Count; j++)
                {
                    if (Mathf.Abs(map[i].x - map[j].x) < 3000f) continue;
                    pairs++;
                    if ((map[i].x < map[j].x) == (laid[i].at.x < laid[j].at.x)) kept++;
                }
            Assert.Greater(kept, pairs * 0.97f, "far apart on the map, the same way round in space");
            var first = InterstellarSpace.Systems(db)[0];
            Assert.AreEqual(InterstellarSpace.Position(db, db.Systems.Find(x => x.index == first.system)), first.position, "the points are the layout's");
        }

        [Test]
        public void ASystemArrivesAtThePickedPlanetElseTheDestinationElseTheGate()
        {
            var sys = db.Systems.Find(s => s.stations.Count >= 3 && InterstellarSpace.ArrivalStation(s) >= 0);
            Assert.IsNotNull(sys);
            int gate = InterstellarSpace.ArrivalStation(sys);
            int other = sys.stations.Find(st => st != gate), third = sys.stations.FindLast(st => st != gate && st != other);
            var elsewhere = db.Systems.Find(s => s.index != sys.index && s.stations.Count > 0).stations[0];
            Assert.AreEqual(gate, InterstellarSpace.ArrivalFor(db, sys.index, gate, -1, -1), "nothing chosen: the gate orbit");
            Assert.AreEqual(other, InterstellarSpace.ArrivalFor(db, sys.index, gate, -1, other), "the programmed destination there");
            Assert.AreEqual(gate, InterstellarSpace.ArrivalFor(db, sys.index, gate, -1, elsewhere), "a destination in another system");
            Assert.AreEqual(gate, InterstellarSpace.ArrivalFor(db, sys.index, gate, -1, other, st => st == other), "the story refuses it");
            Assert.AreEqual(third, InterstellarSpace.ArrivalFor(db, sys.index, gate, third, other), "a pick in flight over the destination");
        }

        [Test]
        public void TheDistancesBetweenTheSystemsShowUntilTurnedOff()
        {
            Settings.Reset(new[] { "interstellarDistance" });
            Assert.IsTrue(Settings.InterstellarDistance);
            CollectionAssert.Contains(Settings.GameplayKeys, "interstellarDistance", "reset with the Gameplay tab");
            Settings.InterstellarDistance = false;
            Assert.IsFalse(Settings.InterstellarDistance);
            Settings.Reset(new[] { "interstellarDistance" });
        }

        [Test]
        public void TheSlipstreamJumpsAsTheKhadorDriveDoes()
        {
            var saved = Session.Equipment;
            bool wasOn = Settings.KhadorSlipstream;
            try
            {
                Settings.KhadorSlipstream = true;   // (Options > Gameplay; off in the Original experience)
                Session.Equipment = new List<ItemStack>();
                Assert.IsFalse(GalaxyMap.HasSlipstream(db), "nothing mounted");
                int slipstream = GoF2Remake.Modding.ModContent.ItemIndexOf("gof2:khador_slipstream");
                Assert.GreaterOrEqual(slipstream, 0);
                Session.Equipment.Add(new ItemStack(slipstream, 1));
                Assert.IsTrue(GalaxyMap.HasSlipstream(db));
                Assert.IsTrue(GalaxyMap.HasKhadorDrive(db), "the drive it replaces: the Khador jump, the Void's question");
                Assert.IsTrue(GalaxyMap.HasJumpDrive(db), "the maps in jump mode, the quick menu's Khador entry");
                Settings.KhadorSlipstream = false;
                Assert.IsFalse(GalaxyMap.HasKhadorDrive(db), "switched off it does nothing (Shop.SlipstreamOff)");
            }
            finally { Session.Equipment = saved; Settings.KhadorSlipstream = wasOn; }
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
                    float units = (InterstellarSpace.Position(db, a) - InterstellarSpace.Position(db, b)).magnitude;
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
                Assert.Less(OrbitLayout.ToUnity(InterstellarSpace.Position(db, s)).magnitude, 100000f, s.name);
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
        public void PlanetsGrowFromASpeckToFullSizeOnTheWayIn()
        {
            Assert.AreEqual(InterstellarSpace.PlanetFarSize, InterstellarSpace.ApproachSize(InterstellarSpace.ApproachUnits * 2f), 0.001f, "a speck out there");
            Assert.Greater(InterstellarSpace.ApproachSize(InterstellarSpace.ArriveUnits * 1.5f), InterstellarSpace.ApproachSize(InterstellarSpace.ArriveUnits * 2f), "growing");
            Assert.AreEqual(InterstellarSpace.ArrivalSize(1f), InterstellarSpace.ApproachSize(InterstellarSpace.ArriveUnits), 0.001f, "the same size across the load");
            Assert.AreEqual(1f, InterstellarSpace.ArrivalSize(0f), 0.001f, "full size on arrival");
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
