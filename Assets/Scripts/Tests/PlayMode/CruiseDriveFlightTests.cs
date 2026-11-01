// CruiseDriveFlightTests.cs
// The cruise drive in a real orbit (NavigationCruiseDrive; docs/notes/navigation.md "Cruise drive"), with a test mod's drive
// mounted: the held button charges and cruises and the release drops it; a station ahead is seen and stops it short, under
// the Exact / Mixed setting (its hull) and the Original one (its volumes); an asteroid ahead drops it before the rock; a
// signal of the encounters can be locked and flown to, and spawns; an ambush spawned ahead drops it for hostiles.

using System.Collections;
using System.IO;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using GoF2Remake.UI;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GoF2Remake.Tests
{
    [Category("Smoke")]
    public class CruiseDriveFlightTests : PlayModeTest
    {
        const string Mod = "cruise_test";
        const float M = 0.05f;   // metres a game unit
        int collisionBefore;

        static void MountDrive(int cells)
        {
            string dir = Path.Combine(ModManager.UserFolder, Mod);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, ModManifest.FileName), "{ \"id\": \"" + Mod + "\", \"name\": \"Test\", \"version\": \"1.0\", \"author\": \"Tests\" }");
            File.WriteAllText(Path.Combine(dir, "items.json"), "[ { \"id\": \"drive\", \"base\": 85, \"name\": \"Test Drive\", \"techLevel\": 10, \"occurrence\": 0, \"price\": [1000, 1100], " +
                              "\"stats\": { \"cruiseSpeed\": 400, \"cruiseRange\": 60000, \"cruiseChargeMs\": 100, \"energyCells\": 1 } } ]");
            ModManager.Scan();
            ModManager.SetEnabled(Mod, true);
            Session.Equipment.Add(new ItemStack(ModContent.ItemIndexOf(Mod + ":drive"), 1));
            if (cells > 0) Session.Cargo.Add(new ItemStack(GalaxyMap.EnergyCellItem, cells));
            Cheats.FreeJumps = false;
        }

        /// <summary>A station of a secure system (Augmenta, security 3): no raiders to call hostiles while the tests fly.</summary>
        static int QuietStation => Db.Stations.First(s => s.system == 19).index;

        IEnumerator EnterOrbit(int station, int cells)
        {
            FreePlayAt(station);
            MountDrive(cells);
            Session.LaunchedFromStation = true;
            yield return Load("Space");
            yield return WaitFor(() => Find<SpaceLevel>() != null && Find<SpaceLevel>().LaunchCameraOver && Find<SpaceLevel>().Navigation != null, 60f, "the orbit");
            // The launch's chatter counts as hostiles for the fast-forward's rule (HostilesPresent) for a few seconds.
            yield return WaitFor(() => !Find<SpaceLevel>().Navigation.HostilesPresent, 60f, "a quiet orbit");
            yield return Run(1f);
        }

        [SetUp]
        public void KeepCollisionSetting() { collisionBefore = Settings.ShipCollision; }

        [TearDown]
        public void NoDrive()
        {
            Settings.ShipCollision = collisionBefore;
            ModManager.SetEnabled(Mod, false);
        }

        /// <summary>Out of the sandbox, the mods the run started with again (ModManager.Scan above replaced the list).</summary>
        [OneTimeTearDown]
        public void MountedModsBack() { ModManager.Scan(); }

        /// <summary>The ship put at 'at' (metres), facing 'towards'.</summary>
        static Transform Point(SpaceLevel space, Vector3 at, Vector3 towards)
        {
            var ship = space.Player.transform;
            ship.SetPositionAndRotation(at, Quaternion.LookRotation((towards - at).normalized, Vector3.up));
            return ship;
        }

        static Vector3 ToGame(Vector3 unity) => new Vector3(unity.x, unity.y, -unity.z) / M;

        /// <summary>The fast-forward button, as the HUD's touch button (the HUD passes it to Navigation every frame).</summary>
        static void Hold(bool held)
        {
            var hud = Find<FlightHud>();
            Assert.IsNotNull(hud, "the flight HUD");
            Get<NavigationView>(hud, "navView").SetFastForwardPressed(held);
        }

        static string Why(Navigation nav)
        {
            var block = Get<CruiseDrive.Stop>(nav, "DriveBlock");
            return $"block {block}, drive {nav.Drive?.State}/{nav.Drive?.LastStop}, ahead {Invoke(nav, "ObstacleAheadUnits")} units, " +
                   $"hostiles {nav.HostilesPresent}, jumping {nav.Jumping}, autopilot {nav.Autopilot}, halted {Navigation.InputHalted}, " +
                   $"docking {nav.Docking?.State}, extender {TimeExtender.Active}, locked {nav.Locked?.kind}";
        }

        [UnityTest]
        public IEnumerator TheHeldButtonChargesAndCruisesAndTheReleaseDropsIt()
        {
            yield return EnterOrbit(QuietStation, 5);
            var space = Find<SpaceLevel>();
            var nav = space.Navigation;
            Assert.IsNotNull(nav.Drive, "the mounted drive");
            Assert.IsTrue(nav.CanUseDrive, Why(nav));
            var ship = Point(space, new Vector3(0f, 4000f, 0f), new Vector3(0f, 9000f, 0f));   // up, away from everything
            Hold(true);
            yield return WaitFor(() => nav.DriveCruising, 5f, "the cruise (" + Why(nav) + ")");
            Assert.AreEqual(4, GalaxyMap.CellsInCargo(), "one leg paid");
            float before = ship.position.y;
            yield return Run(2f);
            Assert.Greater((ship.position.y - before) / 2f, 150f, "metres a second: well past the ship's own");
            Hold(false);
            yield return WaitFor(() => !nav.DriveCruising, 3f, "the drop");
            Assert.AreEqual(CruiseDrive.Stop.Released, nav.Drive.LastStop);
        }

        IEnumerator StationAhead()
        {
            var space = Find<SpaceLevel>();
            var nav = space.Navigation;
            Assert.IsNotNull(space.Station, "an orbit with a station");
            var at = space.Station.transform.position;
            var ship = Point(space, at + new Vector3(0f, 0f, -4000f), at);
            // A signal far aside, locked, so the station isn't the locked landmark (that rule drops the cruise 1 km out,
            // before the hull would): the hull / volumes are what stop it here.
            Invoke(nav, "PlaceSignal", CruiseEncounters.BuiltIn().First(e => e.kind == CruiseEncounters.Flyby), ToGame(at + new Vector3(30000f, 0f, -4000f)));
            typeof(Navigation).GetProperty("Locked").SetValue(nav, nav.Targets.First(t => t.kind == Navigation.Kind.Waypoint));
            Assert.IsFalse(nav.LandmarkLocked);
            Hold(true);
            yield return WaitFor(() => nav.DriveCruising, 5f, "the cruise (" + Why(nav) + ")");
            yield return WaitFor(() => !nav.DriveCruising, 40f, "the drop at the station");
            // The scanner locks the station back as the landmark on the way in, and that rule (1 km) fires before the hull's
            // 400 m would; what this checks is that the hull / volumes were seen ahead within the slow-down distance.
            Assert.That(nav.Drive.LastStop, Is.EqualTo(CruiseDrive.Stop.Obstacle).Or.EqualTo(CruiseDrive.Stop.Arrived), Why(nav));
            Assert.Less((float)Invoke(nav, "ObstacleAheadUnits"), CruiseDrive.SlowUnits, "the station seen ahead: " + Why(nav));
            Assert.Greater((ship.position - at).magnitude, 300f, "stopped short of the station");
            Assert.IsFalse(space.Player.GetComponent<PlayerCollision>()?.TouchingStation ?? false, "not scraping it");
        }

        [UnityTest]
        public IEnumerator AStationAheadIsSeenByItsHullAndStopsTheCruise()
        {
            Settings.ShipCollision = SessionRules.CollisionMixed;
            yield return EnterOrbit(QuietStation, 5);
            yield return StationAhead();
        }

        [UnityTest]
        public IEnumerator AStationAheadIsSeenByItsVolumesWithTheOriginalCollisionAndStopsTheCruise()
        {
            Settings.ShipCollision = SessionRules.CollisionOriginal;
            yield return EnterOrbit(QuietStation, 5);
            yield return StationAhead();
        }

        [UnityTest]
        public IEnumerator AnAsteroidAheadDropsTheCruiseBeforeTheRock()
        {
            yield return EnterOrbit(QuietStation, 5);
            var space = Find<SpaceLevel>();
            var nav = space.Navigation;
            var rock = space.Asteroids != null ? space.Asteroids.GetComponentsInChildren<Target>().FirstOrDefault(t => t.isAsteroid && t.Alive) : null;
            Assert.IsNotNull(rock, "an asteroid in the orbit");
            var at = rock.transform.position;
            var outward = (at - OrbitLayout.ToUnity(space.Layout.asteroidCentre)).normalized;
            if (outward.sqrMagnitude < 0.5f) outward = Vector3.up;
            var ship = Point(space, at + outward * 2500f, at);   // from outside the field, so no other rock is in the way
            Hold(true);
            yield return WaitFor(() => nav.DriveCruising, 5f, "the cruise (" + Why(nav) + ")");
            yield return WaitFor(() => !nav.DriveCruising, 30f, "the drop at the asteroid");
            Assert.AreEqual(CruiseDrive.Stop.Obstacle, nav.Drive.LastStop, Why(nav));
            Assert.IsTrue(rock.Alive, "the rock was not run into");
            Assert.Greater((ship.position - at).magnitude, rock.radius + 150f, "stopped short of it");
        }

        [UnityTest]
        public IEnumerator ASignalCanBeLockedAndFlownToAndSpawns()
        {
            yield return EnterOrbit(QuietStation, 5);
            var space = Find<SpaceLevel>();
            var nav = space.Navigation;
            var ship = Point(space, new Vector3(0f, 4000f, 0f), new Vector3(0f, 9000f, 0f));
            var entry = CruiseEncounters.BuiltIn().First(e => e.kind == CruiseEncounters.Trader);
            Invoke(nav, "PlaceSignal", entry, ToGame(ship.position + ship.forward * 900f));
            Assert.IsTrue(nav.SignalPlaced);
            var signal = nav.Targets.First(t => t.kind == Navigation.Kind.Waypoint);
            Assert.AreEqual(Navigation.EncounterName(entry), signal.name);
            int ships = Object.FindObjectsByType<NpcShip>().Length;
            nav.SetAutopilot(signal);
            Assert.AreSame(signal, nav.AutopilotTarget);
            yield return WaitFor(() => !nav.SignalPlaced, 30f, "the signal reached");
            yield return Run(1f);
            Assert.Greater(Object.FindObjectsByType<NpcShip>().Length, ships, "the convoy spawned");
            Assert.IsFalse(nav.Targets.Any(t => t.kind == Navigation.Kind.Waypoint), "the waypoint is gone");
        }

        [UnityTest]
        public IEnumerator AnAmbushSpawnedAheadDropsTheCruise()
        {
            yield return EnterOrbit(QuietStation, 5);
            var space = Find<SpaceLevel>();
            var nav = space.Navigation;
            Assert.IsNotNull(nav.SpawnEncounter, "the level's spawner");
            var ship = Point(space, new Vector3(0f, 4000f, 0f), new Vector3(0f, 9000f, 0f));
            Hold(true);
            yield return WaitFor(() => nav.DriveCruising, 5f, "the cruise (" + Why(nav) + ")");
            var entry = CruiseEncounters.BuiltIn().First(e => e.kind == CruiseEncounters.Pirates);
            var fwd = ship.forward;
            nav.SpawnEncounter(entry, ToGame(ship.position + fwd * 1100f), new Vector3(fwd.x, fwd.y, -fwd.z));
            yield return WaitFor(() => !nav.DriveCruising, 10f, "the drop for hostiles");
            Assert.AreEqual(CruiseDrive.Stop.Hostiles, nav.Drive.LastStop);
            Assert.Greater(space.Traffic.HostileCount, 0, "the raiders are there");
        }
    }
}
