// InterstellarFlightTests.cs
// The space between the systems in the real scenes (InterstellarSpace, docs/notes/star-map.md "Interstellar space"), with a
// test mod's drive mounted: the level builds empty with the map's systems as targets, the autopilot holds the drive and
// the legs are paid in energy cells, an arrival loads the system's orbit, and an empty hold falls out at the nearest system.

using System.Collections;
using System.IO;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using GoF2Remake.Visuals;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GoF2Remake.Tests
{
    [Category("Smoke")]
    public class InterstellarFlightTests : PlayModeTest
    {
        const string Mod = "interstellar_test";

        static void MountDrive(int cells)
        {
            string dir = Path.Combine(ModManager.UserFolder, Mod);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, ModManifest.FileName), "{ \"id\": \"" + Mod + "\", \"name\": \"Test\", \"version\": \"1.0\", \"author\": \"Tests\" }");
            File.WriteAllText(Path.Combine(dir, "items.json"), "[ { \"id\": \"drive\", \"base\": 85, \"name\": \"Test Drive\", \"techLevel\": 10, \"occurrence\": 0, \"price\": [1000, 1100], " +
                              "\"stats\": { \"cruiseSpeed\": 800, \"cruiseRange\": 90000, \"cruiseChargeMs\": 100, \"energyCells\": 1, \"interstellar\": 1 } } ]");
            ModManager.Scan();
            ModManager.SetEnabled(Mod, true);
            Session.Equipment.Add(new ItemStack(ModContent.ItemIndexOf(Mod + ":drive"), 1));
            if (cells > 0) Session.Cargo.Add(new ItemStack(GalaxyMap.EnergyCellItem, cells));
            Cheats.FreeJumps = false;
        }

        IEnumerator EnterSpace(int station, int cells)
        {
            FreePlayAt(station);
            MountDrive(cells);
            Session.EnterInterstellar = true;
            yield return Load("Space");
            yield return WaitFor(() => Find<SpaceLevel>() != null && Find<SpaceLevel>().Navigation != null, 60f, "the level");
        }

        [TearDown]
        public void NoDrive() { ModManager.SetEnabled(Mod, false); }

        /// <summary>Out of the sandbox, the mods the run started with again: ModManager.Scan above replaced the list, and
        /// SceneSmokeTests' orbits, picked when the tests were found, can be theirs.</summary>
        [OneTimeTearDown]
        public void MountedModsBack() { ModManager.Scan(); }

        [UnityTest]
        public IEnumerator TheLevelIsEmptyAndShowsTheMapsSystems()
        {
            yield return EnterSpace(78, 5);
            var space = Find<SpaceLevel>();
            Assert.IsTrue(space.Layout.interstellar);
            Assert.IsFalse(Session.EnterInterstellar, "read once");
            Assert.AreEqual(78, Session.StationIndex, "a save here is the orbit left");
            Assert.AreEqual(InterstellarSpace.OrbitId, space.NetOrbitId);
            Assert.IsNull(space.Station);
            Assert.IsNull(space.Jumpgate);
            yield return Run(2f);
            var systems = space.Navigation.Targets.Where(t => t.kind == Navigation.Kind.System).ToList();
            Assert.AreEqual(InterstellarSpace.Systems(space.Database).Count, systems.Count);
            Assert.IsNotNull(Find<InterstellarField>());
            Assert.AreEqual(0, Object.FindObjectsByType<NpcShip>().Length, "no traffic");
            Assert.IsNotNull(space.Navigation.Drive, "the mounted drive");
            Assert.AreEqual((int)InterstellarSpace.LegUnits, space.Navigation.Drive.legUnits, "a leg is a hop here");
            Assert.LessOrEqual(space.Navigation.MenuEntries().Count, 8);
            Assert.IsFalse(space.Navigation.MenuEntries(true).Exists(t => t.kind == Navigation.Kind.Slipstream), "already here");
        }

        [UnityTest]
        public IEnumerator TheAutopilotHoldsTheDriveAndPaysALeg()
        {
            yield return EnterSpace(78, 5);
            var space = Find<SpaceLevel>();
            yield return Run(1f);
            var nav = space.Navigation;
            var target = nav.Targets.Where(t => t.kind == Navigation.Kind.System).OrderByDescending(t => (t.Position - space.Player.transform.position).sqrMagnitude).First();
            nav.SetAutopilot(target);
            yield return WaitFor(() => nav.DriveCruising, 10f, "the drive under the autopilot");
            Assert.AreEqual(4, GalaxyMap.CellsInCargo(), "one leg paid");
            float before = (target.Position - space.Player.transform.position).magnitude;
            yield return Run(3f);
            float speed = (before - (target.Position - space.Player.transform.position).magnitude) / 3f;
            Assert.Greater(speed, 150f, "metres a second: well past the ship's own 100");
            Assert.IsFalse(nav.CanFastForward, "the button is the drive's here");
        }

        [UnityTest]
        public IEnumerator ReachingASystemArrivesInItsOrbit()
        {
            yield return EnterSpace(78, 5);
            var space = Find<SpaceLevel>();
            yield return Run(1f);
            var target = space.Navigation.Targets.First(t => t.kind == Navigation.Kind.System && t.system != space.Layout.systemIndex);
            int station = target.station;
            space.Player.transform.position = target.Position + Vector3.up * (InterstellarSpace.ArriveUnits * 0.5f * 0.05f);
            yield return WaitFor(() => Find<SpaceLevel>() != space && Find<SpaceLevel>() != null && Find<SpaceLevel>().Layout != null, 30f, "the arrival");
            Assert.AreEqual(station, Session.StationIndex);
            Assert.IsFalse(Find<SpaceLevel>().Layout.interstellar);
            Assert.AreEqual(78, Session.PreviousStationIndex);
            Assert.IsTrue(Find<SpaceLevel>().LaunchCameraOver, "no fly-in: the flight goes on");
            Assert.IsFalse(Session.WarpArrival, "read once");
            Assert.IsNotNull(Find<WarpSky>(), "the warp eases out here");
        }

        [UnityTest]
        public IEnumerator TheOrbitStandsAheadWhereTheArrivalPutsIt()
        {
            yield return EnterSpace(78, 5);
            var space = Find<SpaceLevel>();
            yield return Run(1f);
            var target = space.Navigation.Targets.First(t => t.kind == Navigation.Kind.System && t.system != space.Layout.systemIndex);
            var ship = space.Player.transform;
            var way = (target.Position - ship.position).normalized;
            float edge = InterstellarSpace.ArriveUnits * 0.05f;
            space.Player.enabled = false;   // held just outside the edge
            ship.SetPositionAndRotation(target.Position - way * (edge + 5f), Quaternion.LookRotation(way, Vector3.up));
            yield return WaitFor(() => GameObject.Find("Approach") is GameObject a && a.transform.Find("Asteroids") != null, 10f, "the approach");
            yield return Run(2f);
            var approach = GameObject.Find("Approach").transform;
            var station = approach.Find("Station");
            var seen = station != null ? ship.InverseTransformPoint(station.position) : Vector3.zero;
            var rocks = approach.Find("Asteroids");
            var rock = rocks.childCount > 0 ? rocks.GetChild(0).localPosition : Vector3.zero;
            int count = rocks.childCount;

            ship.position += way * 10f;
            yield return WaitFor(() => Find<SpaceLevel>() != space && Find<SpaceLevel>() != null && Find<SpaceLevel>().Layout != null, 30f, "the arrival");
            var orbit = Find<SpaceLevel>();
            yield return null;
            Assert.AreEqual(count, orbit.Asteroids.childCount, "the same field");
            if (count > 0) Assert.AreEqual(0f, (rock - orbit.Asteroids.GetChild(0).position).magnitude, 1f);
            if (station != null || orbit.Station != null)
                Assert.Less((seen - orbit.Player.transform.InverseTransformPoint(orbit.Station.transform.position)).magnitude, 300f, "the swap moved the station");
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_SkyBlend"), "the orbit's own sky now");
        }

        [UnityTest]
        public IEnumerator AnEmptyHoldFallsOutAtTheNearestSystem()
        {
            yield return EnterSpace(78, 0);
            var space = Find<SpaceLevel>();
            yield return Run(1f);
            var nav = space.Navigation;
            var at = space.Player.transform.position;
            var systems = nav.Targets.Where(t => t.kind == Navigation.Kind.System).OrderBy(t => (t.Position - at).sqrMagnitude).ToList();
            int nearest = systems[0].station;
            nav.SetAutopilot(systems[systems.Count - 1]);   // (it holds the drive; the HUD polls the button every frame)
            yield return WaitFor(() => Find<SpaceLevel>() != space && Find<SpaceLevel>() != null && Find<SpaceLevel>().Layout != null, 30f, "the fall out");
            Assert.AreEqual(nearest, Session.StationIndex);
        }

        [UnityTest]
        public IEnumerator TheMenuEntryPlaysTheKhadorSequenceIntoTheSpace()
        {
            FreePlayAt(78);
            MountDrive(3);
            Session.LaunchedFromStation = true;
            yield return Load("Space");
            yield return WaitFor(() => Find<SpaceLevel>() != null && Find<SpaceLevel>().LaunchCameraOver, 60f, "the orbit");
            var first = Find<SpaceLevel>();
            yield return Run(5.5f);   // SystemJump: the level is 5 s old
            var entry = first.Navigation.MenuEntries(true).Find(t => t.kind == Navigation.Kind.Slipstream);
            Assert.IsNotNull(entry, "the mounted drive's entry");
            Assert.AreEqual("Test Drive", entry.name);
            first.Navigation.OpenMenu(true);
            first.Navigation.ChooseMenuEntry(entry);
            yield return WaitFor(() => Find<SpaceLevel>() != first && Find<SpaceLevel>() != null && Find<SpaceLevel>().Layout != null && Find<SpaceLevel>().Layout.interstellar, 30f, "interstellar space");
            Assert.AreEqual(78, Session.StationIndex);
            Assert.AreEqual(3, GalaxyMap.CellsInCargo(), "the legs are paid on the way, not at the door");
        }

        [UnityTest]
        public IEnumerator VolatileGoodsRefuseTheEntry()
        {
            FreePlayAt(78);
            MountDrive(3);
            Session.Cargo.Add(new ItemStack(204, 1));
            Session.LaunchedFromStation = true;
            yield return Load("Space");
            yield return WaitFor(() => Find<SpaceLevel>() != null && Find<SpaceLevel>().LaunchCameraOver, 60f, "the orbit");
            var first = Find<SpaceLevel>();
            var entry = first.Navigation.MenuEntries(true).Find(t => t.kind == Navigation.Kind.Slipstream);
            first.Navigation.OpenMenu(true);
            first.Navigation.ChooseMenuEntry(entry);
            yield return Run(7f);
            Assert.AreSame(first, Find<SpaceLevel>(), "still in the orbit");
        }
    }
}
