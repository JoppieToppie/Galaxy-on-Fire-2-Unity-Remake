// A cruise to another station's planet (Navigation.RunPlanet, World.PlanetApproach): flown by hand with the nose on it, or in
// place of the planet jump with the Khador Slipstream mounted, it comes out in that orbit with the warp still on.

using System.Collections;
using System.Reflection;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.UI;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GoF2Remake.Tests
{
    public class PlanetRunTests : PlayModeTest
    {
        /// <summary>Gome C, of a secure system of five stations (Augmenta, security 3): no raiders to call hostiles while it flies.</summary>
        const int Station = 95;

        /// <summary>Launched from the station with the game's own drive mounted, and proved so.</summary>
        IEnumerator EnterOrbit(int cells)
        {
            int drive = -1;
            // Even a secure orbit may roll a raider or two (TrafficPlan), which never leave: a new roll, then.
            for (int rolls = 0; ; rolls++)
            {
                FreePlayAt(Station);
                Session.Difficulty = Session.DifficultyNormal;
                drive = FitSlipstream();
                if (cells > 0) Session.Cargo.Add(new ItemStack(GalaxyMap.EnergyCellItem, cells));
                Cheats.FreeJumps = false;
                Session.LaunchedFromStation = true;
                yield return Load("Space");
                yield return WaitFor(() => Find<SpaceLevel>() != null && Find<SpaceLevel>().LaunchCameraOver && Find<SpaceLevel>().Navigation != null, 60f, "the orbit");
                float until = Time.realtimeSinceStartup + 12f;
                while (Find<SpaceLevel>().Navigation.HostilesPresent && Time.realtimeSinceStartup < until) yield return null;
                if (!Find<SpaceLevel>().Navigation.HostilesPresent) break;
                Assert.Less(rolls, 5, "a quiet orbit");
                yield return Load("MainMenu");
            }
            yield return Run(1f);
            var space = Find<SpaceLevel>();
            Assert.AreEqual(drive, Shop.MountedCruiseDrive(space.Database)?.index, "the Khador Slipstream is mounted");
            Assert.IsNotNull(InterstellarSpace.Drive(space.Database), "and flies between the systems");
            Assert.IsNotNull(space.Navigation.Drive, "the level has its drive");
        }

        /// <summary>The fast-forward button, as the HUD's touch button: the HUD passes it to Navigation every frame.</summary>
        static void Hold(bool held)
        {
            var hud = Find<FlightHud>();
            Assert.IsNotNull(hud, "the flight HUD");
            var view = (NavigationView)typeof(FlightHud).GetField("navView", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(hud);
            view.SetFastForwardPressed(held);
        }

        /// <summary>The orbit's first planet: its backdrop quad stands about the camera, not the ship.</summary>
        static Navigation.Target Planet(SpaceLevel space)
        {
            var planet = space.Navigation.Targets.Find(t => t.kind == Navigation.Kind.Planet && !t.hidden);
            Assert.IsNotNull(planet, "a planet in this orbit");
            return planet;
        }

        /// <summary>Up, clear of the station and the field, with the nose on the planet (the camera caught up, twice over).</summary>
        static IEnumerator AimAt(SpaceLevel space, Navigation.Target planet)
        {
            var ship = space.Player.transform;
            ship.position = new Vector3(0f, 4000f, 0f);
            for (int i = 0; i < 3; i++)
            {
                yield return Run(0.7f);
                ship.rotation = Quaternion.LookRotation(planet.Position - Camera.main.transform.position, Vector3.up);
            }
            yield return Run(0.3f);
        }

        static IEnumerator FliesThereAtCruiseSpeed(SpaceLevel from, Navigation.Target planet)
        {
            var nav = from.Navigation;
            yield return WaitFor(() => nav.DriveCruising, 10f, "the drive running");
            Assert.AreEqual(planet, nav.RunPlanet);
            Assert.IsFalse(nav.Jumping, "not the original's scene");
            var ship = from.Player.transform;
            var at = ship.position;
            yield return Run(2f);
            Assert.Greater((ship.position - at).magnitude / 2f, 150f, "metres a second: well past the ship's own 100");
            yield return WaitFor(() => GameObject.Find("Approach") != null, 15f, "the orbit building ahead");
            Assert.Greater(Shader.GetGlobalFloat("_SkyBlend"), -1f);
            yield return WaitFor(() => Find<SpaceLevel>() != from && Find<SpaceLevel>() != null && Find<SpaceLevel>().Navigation != null, 40f, "the arrival");
            Assert.AreEqual(planet.station, Session.StationIndex);
            Assert.AreEqual(Station, Session.PreviousStationIndex);
            var there = Find<SpaceLevel>();
            Assert.IsTrue(there.LocalArrival);
            Assert.IsTrue(there.WarpHolds, "the warp eases out of it");
            Assert.IsTrue(there.Navigation.ArrivalHeld);
            Assert.IsNull(GameObject.Find("Warp web"), "an orbit's plain warp: the web is between the systems");
            yield return WaitFor(() => !Find<SpaceLevel>().WarpHolds, 15f, "the warp's end");
            Assert.Less(Find<SpaceLevel>().Player.transform.position.magnitude, 8000f, "out where arrivals are");
        }

        [UnityTest]
        public IEnumerator HoldingTheCruiseAtAPlanetLoadsItsOrbitIn()
        {
            yield return EnterOrbit(8);
            var space = Find<SpaceLevel>();
            var planet = Planet(space);
            yield return AimAt(space, planet);
            Assert.IsTrue(space.Navigation.CanUseDrive);
            Hold(true);
            yield return null;
            yield return null;
            yield return FliesThereAtCruiseSpeed(space, planet);
            Hold(false);
            Assert.AreEqual(8, GalaxyMap.CellsInCargo(), "free, as a planet jump is");
        }

        [UnityTest]
        public IEnumerator TheHudGoesOnAcrossTheSwap()
        {
            yield return EnterOrbit(8);
            var space = Find<SpaceLevel>();
            var nav = space.Navigation;
            var hud = Find<FlightHud>();
            var planet = Planet(space);
            yield return AimAt(space, planet);
            yield return WaitFor(() => nav.Locked == planet, 10f, "the lock");
            Assert.IsTrue(nav.Interact());
            yield return WaitFor(() => Find<SpaceLevel>() != space && Find<SpaceLevel>() != null, 60f, "the arrival");
            yield return Run(0.5f);
            var huds = Object.FindObjectsByType<FlightHud>(FindObjectsInactive.Include);
            Assert.AreEqual(1, huds.Length, "one HUD");
            Assert.AreSame(hud, huds[0], "the same one");
            Assert.AreEqual(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), hud.gameObject.scene, "back in the level's scene");
            Assert.AreSame(Find<SpaceLevel>(), typeof(FlightHud).GetField("level", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hud), "on the new level");
        }

        [UnityTest]
        public IEnumerator TheJumpOnALockedPlanetIsTheRunInstead()
        {
            yield return EnterOrbit(8);
            var space = Find<SpaceLevel>();
            var nav = space.Navigation;
            var planet = Planet(space);
            yield return AimAt(space, planet);
            yield return WaitFor(() => nav.Locked == planet, 10f, "the lock");
            Assert.IsTrue(nav.Interact());
            Assert.IsFalse(nav.Jumping, "not the original's scene");
            Assert.AreEqual(planet, nav.RunPlanet);
            Assert.AreEqual(planet, nav.AutopilotTarget, "the autopilot flies it");
            Assert.IsTrue(nav.DriveCruising, "at once: no charge");
            yield return FliesThereAtCruiseSpeed(space, planet);
            Assert.AreEqual(8, GalaxyMap.CellsInCargo(), "free, as a planet jump is");
        }

        [UnityTest]
        public IEnumerator ItNeedsNoEnergyCells()
        {
            yield return EnterOrbit(0);
            var space = Find<SpaceLevel>();
            var nav = space.Navigation;
            var planet = Planet(space);
            yield return AimAt(space, planet);
            yield return WaitFor(() => nav.Locked == planet, 10f, "the lock");
            Assert.IsTrue(nav.Interact());
            Assert.IsFalse(nav.Jumping);
            yield return FliesThereAtCruiseSpeed(space, planet);
        }

        [UnityTest]
        public IEnumerator TheAutopilotToAProgrammedStationFliesTheRun()
        {
            yield return EnterOrbit(8);
            var space = Find<SpaceLevel>();
            var nav = space.Navigation;
            var planet = Planet(space);
            Session.ProgrammedStation = planet.station;
            Assert.IsTrue(nav.ContinueToProgrammedStation(), "autopilot to the programmed station");
            Assert.AreEqual(planet, nav.AutopilotTarget);
            yield return WaitFor(() => nav.RunPlanet == planet || nav.Jumping, 30f, "the autopilot reaching the planet's lock");
            Assert.IsFalse(nav.Jumping, "not the original's scene");
            yield return FliesThereAtCruiseSpeed(space, planet);
            Assert.AreEqual(8, GalaxyMap.CellsInCargo(), "free, as a planet jump is");
        }

        [UnityTest]
        public IEnumerator WithoutTheDriveItIsThePlainJump()
        {
            yield return EnterOrbit(0);
            Session.Equipment.Clear();
            var space = Find<SpaceLevel>();
            var nav = space.Navigation;
            var planet = Planet(space);
            yield return AimAt(space, planet);
            yield return WaitFor(() => nav.Locked == planet, 10f, "the lock");
            Assert.IsTrue(nav.Interact());
            Assert.IsTrue(nav.Jumping);
            Assert.IsNull(nav.RunPlanet);
        }

        [UnityTest]
        public IEnumerator APlainKhadorDriveJumps()
        {
            yield return EnterOrbit(0);
            Session.Equipment.Clear();
            Session.Equipment.Add(new ItemStack(GalaxyMap.KhadorDriveItem, 1));
            var space = Find<SpaceLevel>();
            var nav = space.Navigation;
            var planet = Planet(space);
            yield return AimAt(space, planet);
            yield return WaitFor(() => nav.Locked == planet, 10f, "the lock");
            Assert.IsTrue(nav.Interact());
            Assert.IsTrue(nav.Jumping, "the original's scene");
            Assert.IsNull(nav.RunPlanet);
        }

        [UnityTest]
        public IEnumerator AScriptedOrbitIsReachedByThePlainJump()
        {
            yield return EnterOrbit(8);
            var space = Find<SpaceLevel>();
            var nav = space.Navigation;
            var planet = Planet(space);
            var quiet = SpaceHooks.QuietOrbit;   // an event quest's orbit, as a story or freelance mission's would be
            SpaceHooks.QuietOrbit = st => st == planet.station;
            try
            {
                Assert.IsTrue(Navigation.ScriptedArrival(planet.station));
                yield return AimAt(space, planet);
                Hold(true);
                yield return Run(2f);
                Assert.IsNull(nav.RunPlanet, "the cruise doesn't take it as a run");
                Hold(false);
                yield return WaitFor(() => !nav.DriveCruising, 10f, "the cruise's end");
                yield return AimAt(space, planet);
                yield return WaitFor(() => nav.Locked == planet, 10f, "the lock");
                Assert.IsTrue(nav.Interact());
                Assert.IsTrue(nav.Jumping, "the original's scene");
                Assert.IsNull(nav.RunPlanet);
            }
            finally { SpaceHooks.QuietOrbit = quiet; }
        }

        [UnityTest]
        public IEnumerator TurningAwayEndsTheRunAndTheOrbitIsAsItWas()
        {
            yield return EnterOrbit(8);
            var space = Find<SpaceLevel>();
            var nav = space.Navigation;
            var planet = Planet(space);
            yield return AimAt(space, planet);
            Hold(true);
            yield return WaitFor(() => nav.RunPlanet == planet, 10f, "the run");
            yield return WaitFor(() => GameObject.Find("Approach") != null, 15f, "the orbit building ahead");
            yield return Run(1f);
            space.Player.transform.Rotate(0f, 90f, 0f, UnityEngine.Space.Self);
            yield return WaitFor(() => nav.RunPlanet == null, 3f, "the run's end");
            Hold(false);
            yield return WaitFor(() => GameObject.Find("Approach") == null, 10f, "the orbit gone again");
            yield return WaitFor(() => space.Backdrop.flareShown == 1f, 2f, "this orbit as it was");
            Assert.AreEqual(Station, Session.StationIndex);
        }
    }
}
