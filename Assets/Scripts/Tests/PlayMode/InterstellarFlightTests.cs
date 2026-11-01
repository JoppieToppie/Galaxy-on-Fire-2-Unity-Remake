// InterstellarFlightTests.cs
// The space between the systems in the real scenes (InterstellarSpace, docs/notes/star-map.md "Interstellar space"), with a
// test mod's drive mounted: the level builds empty with the map's systems as targets, the autopilot holds the drive and
// the legs are paid in energy cells, an arrival loads the system's orbit, and an empty hold falls out at the nearest system;
// the Void is a dive from the autopilot menu, and the drive flies out of it.

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
            Session.Difficulty = Session.DifficultyNormal;   // Extreme, left by an edit-mode test, pays a leg twice
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
            Assert.IsNull(space.Fog, "no fog: nothing but distance");
            Assert.IsTrue(space.WarpHolds && space.Weapons.Blocked, "no firing in the warp");
            Assert.AreEqual(1f, Shader.GetGlobalFloat("_WarpLevel"), "the warp is always full here");
            Assert.IsNotNull(GameObject.Find("Warp web"), "the vein web, over the planets");
            Assert.IsNotNull(space.Navigation.Drive, "the mounted drive");
            Assert.AreEqual((int)InterstellarSpace.LegUnits, space.Navigation.Drive.legUnits, "a leg is a hop here");
            Assert.LessOrEqual(space.Navigation.MenuEntries().Count, 8);
            Assert.IsFalse(space.Navigation.MenuEntries(true).Exists(t => t.kind == Navigation.Kind.Slipstream), "already here");
        }

        [UnityTest]
        public IEnumerator TheGamesOwnDriveDoesBothCruisesWithNoModOn()
        {
            FreePlayAt(78);
            Session.Equipment.Add(new ItemStack(ModContent.ItemIndexOf("gof2:khador_slipstream"), 1));
            Session.Cargo.Add(new ItemStack(GalaxyMap.EnergyCellItem, 5));
            Assert.IsEmpty(ModManager.UserMods);
            Session.LaunchedFromStation = true;
            yield return Load("Space");
            yield return WaitFor(() => Find<SpaceLevel>() != null && Find<SpaceLevel>().Navigation != null, 60f, "the orbit");
            Assert.IsNotNull(Find<SpaceLevel>().Navigation.Drive, "a cruise in the orbit");
            Assert.IsFalse(Find<SpaceLevel>().Layout.interstellar);
            Session.EnterInterstellar = true;
            yield return Load("Space");
            yield return WaitFor(() => Find<SpaceLevel>() != null && Find<SpaceLevel>().Navigation != null, 60f, "the space between the systems");
            Assert.IsTrue(Find<SpaceLevel>().Layout.interstellar);
            Assert.IsNotNull(Find<SpaceLevel>().Navigation.Drive);
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
            var fog = Find<SpaceLevel>().Fog.GetComponent<MeshRenderer>().sharedMaterial;
            float full = Find<SpaceLevel>().Layout.dustFogTint.a;
            Assert.Less(fog.GetColor("_Color").a, full * 0.5f, "the fog eases in with the arrival");
            var arrived = Find<SpaceLevel>();
            yield return null;
            Assert.IsTrue(arrived.WarpHolds && arrived.Weapons.Blocked && arrived.Traffic.HoldFire && arrived.Navigation.ArrivalHeld, "the warp holds everything");
            arrived.Navigation.SetAutopilot(arrived.Navigation.Targets[0]);
            Assert.IsNull(arrived.Navigation.AutopilotTarget, "no autopilot till it ends");
            Assert.IsTrue(arrived.Navigation.RadarOff, "no markers on the station or gate till it ends");
            var stationFull = arrived.Station != null ? arrived.Station.transform : null;
            float stationSize = stationFull != null ? stationFull.localScale.x : 0f;
            var ships = arrived.Traffic.Ships;
            Assert.IsFalse(ships.Exists(s => Traffic.Enemy(s.Spec)), "no enemies until the warp has ended");
            yield return Run(1f);
            Assert.Greater(ships.Count, 0, "the normal traffic is here");
            Assert.IsTrue(ships.TrueForAll(s => s.RadarHidden || s.Spec.radarHidden), "and off the radar until then");
            yield return WaitFor(() => Mathf.Approximately(fog.GetColor("_Color").a, full), 10f, "the orbit's full fog");
            yield return null;
            Assert.IsFalse(ships.Exists(s => s.RadarHidden && !s.Spec.radarHidden), "on the radar once the warp has ended");
            Assert.IsFalse(arrived.WarpHolds || arrived.Weapons.Blocked || arrived.Traffic.HoldFire || arrived.Navigation.RadarOff, "and everything free again");
            if (stationFull != null) Assert.Greater(stationFull.localScale.x, stationSize * 1.5f, "the station grew to its own size");
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
            var ahead = GameObject.Find("Arrival ships");
            Assert.IsNotNull(ahead, "the normal traffic is out there before the arrival");
            int shipsAhead = ahead.transform.childCount;
            Assert.Greater(shipsAhead, 0);
            var station = approach.Find("Station");
            var seen = station != null ? ship.InverseTransformPoint(station.position) : Vector3.zero;
            var rocks = approach.Find("Asteroids");
            var rock = rocks.childCount > 0 ? rocks.GetChild(0).localPosition : Vector3.zero;
            int count = rocks.childCount;

            ship.position += way * 10f;
            yield return WaitFor(() => Find<SpaceLevel>() != space && Find<SpaceLevel>() != null && Find<SpaceLevel>().Layout != null, 30f, "the arrival");
            var orbit = Find<SpaceLevel>();
            yield return null;
            var stationAfter = orbit.Station != null ? orbit.Player.transform.InverseTransformPoint(orbit.Station.transform.position) : Vector3.zero;
            yield return Run(1f);
            Assert.IsNull(GameObject.Find("Arrival ships"), "all taken over");
            Assert.IsFalse(GameObject.Find("Ship ahead"), "no stand-in left behind");
            Assert.GreaterOrEqual(orbit.Traffic.Ships.Count, shipsAhead, "the same ships flying on");
            Assert.AreEqual(count, orbit.Asteroids.childCount, "the same field");
            if (count > 0) Assert.AreEqual(0f, (rock - orbit.Asteroids.GetChild(0).position).magnitude, 1f);
            if (station != null || orbit.Station != null)
                Assert.Less((seen - stationAfter).magnitude, 300f, "the swap moved the station");
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_SkyBlend"), "the orbit's own sky now");
        }

        [UnityTest]
        public IEnumerator TheAutopilotFliesPastOtherSystemsToItsOwn()
        {
            yield return EnterSpace(78, 5);
            var space = Find<SpaceLevel>();
            yield return Run(1f);
            var systems = space.Navigation.Targets.Where(t => t.kind == Navigation.Kind.System && t.system != space.Layout.systemIndex).ToList();
            var goal = systems[0];
            var passed = systems[1];
            space.Navigation.SetAutopilot(goal);
            space.Player.transform.position = passed.Position + Vector3.up * (InterstellarSpace.ArriveUnits * 0.5f * 0.05f);
            yield return Run(1f);
            Assert.AreSame(space, Find<SpaceLevel>(), "flown past");
            space.Player.transform.position = goal.Position + Vector3.up * (InterstellarSpace.ArriveUnits * 0.5f * 0.05f);
            yield return WaitFor(() => Find<SpaceLevel>() != space && Find<SpaceLevel>() != null && Find<SpaceLevel>().Layout != null, 30f, "the arrival");
            Assert.AreEqual(goal.station, Session.StationIndex);
        }

        [UnityTest]
        public IEnumerator TheMenusDiveFallsIntoTheVoidUnseen()
        {
            yield return EnterSpace(78, 5);
            var space = Find<SpaceLevel>();
            yield return Run(1f);
            var nav = space.Navigation;
            Assert.IsFalse(nav.Targets.Exists(t => t.station == Session.VoidOrbit), "no place: never drawn, locked or measured");
            var entries = nav.MenuEntries();
            Assert.LessOrEqual(entries.Count, 8);
            var dive = entries[entries.Count - 1];
            Assert.AreSame(nav.VoidDive, dive, "the menu's last entry");
            Assert.AreEqual(Localization.Get(415), dive.name);
            nav.OpenMenu();
            nav.ChooseMenuEntry(dive);
            Assert.AreSame(dive, nav.AutopilotTarget);
            Assert.That(nav.VoidDiveMs, Is.InRange(InterstellarSpace.DiveMinMs, InterstellarSpace.DiveMaxMs));
            yield return WaitFor(() => nav.DriveCruising, 10f, "the drive under the autopilot");
            float left = nav.VoidDiveMs;
            yield return Run(1f);
            Assert.Less(nav.VoidDiveMs, left - 500f, "the dive runs down on the drive");
            nav.VoidDiveMs = 3000f;   // (the rest of it)
            yield return WaitFor(() => GameObject.Find("Approach") is GameObject a && a.transform.Find("Station") != null, 10f, "the Void seeping in");
            Assert.IsTrue(Session.WarpArrivalPoint.HasValue, "the random arrival point kept");
            var point = Session.WarpArrivalPoint.Value;
            yield return WaitFor(() => Find<SpaceLevel>() != space && Find<SpaceLevel>() != null && Find<SpaceLevel>().Layout != null, 30f, "the fall into the Void");
            var orbit = Find<SpaceLevel>();
            Assert.AreEqual(Session.VoidOrbit, Session.StationIndex);
            Assert.IsTrue(orbit.Layout.alienOrbit);
            Assert.AreEqual(point, orbit.Layout.hiddenJumpgate, "the arrival the approach was built around");
            Assert.IsNull(Session.WarpArrivalPoint, "read once");
            Assert.AreEqual(78, Session.VoidReturnStation, "the Khador Drive goes back to the orbit the crossing left");
            Assert.AreEqual(4, GalaxyMap.CellsInCargo(), "one leg");
            yield return null;
            Assert.IsNotNull(orbit.Station, "the Void station");
        }

        [UnityTest]
        public IEnumerator TurningTheAutopilotOffEndsTheDive()
        {
            yield return EnterSpace(78, 5);
            var space = Find<SpaceLevel>();
            yield return Run(1f);
            var nav = space.Navigation;
            nav.OpenMenu();
            nav.ChooseMenuEntry(nav.VoidDive);
            yield return Run(1f);
            nav.SetAutopilot(null);
            float left = nav.VoidDiveMs;
            yield return Run(2f);
            Assert.AreEqual(left, nav.VoidDiveMs, "stopped");
            Assert.AreSame(space, Find<SpaceLevel>());
        }

        [UnityTest]
        public IEnumerator TheDriveFliesOutOfTheVoid()
        {
            FreePlayAt(Session.VoidOrbit);
            Session.VoidReturnStation = 78;
            MountDrive(5);
            Session.ArrivedByTravel = true;
            Session.PreviousStationIndex = 78;
            yield return Load("Space");
            yield return WaitFor(() => Find<SpaceLevel>() != null && Find<SpaceLevel>().Navigation != null, 60f, "the Void");
            var first = Find<SpaceLevel>();
            Assert.IsTrue(first.Layout.alienOrbit);
            yield return WaitFor(() => first.LaunchCameraOver, 30f, "the arrival camera");
            yield return Run(5.5f);   // SystemJump: the level is 5 s old
            var entry = first.Navigation.MenuEntries(true).Find(t => t.kind == Navigation.Kind.Slipstream);
            Assert.IsNotNull(entry, "the drive's entry in the Void too");
            first.Navigation.OpenMenu(true);
            first.Navigation.ChooseMenuEntry(entry);
            yield return WaitFor(() => Find<SpaceLevel>() != first && Find<SpaceLevel>() != null && Find<SpaceLevel>().Layout != null && Find<SpaceLevel>().Layout.interstellar, 30f, "interstellar space");
            var space = Find<SpaceLevel>();
            Assert.AreEqual(Session.VoidOrbit, Session.StationIndex, "a save here is the Void");
            Assert.IsFalse(space.Layout.alienOrbit, "the Void's rules stay behind");
            float fromEntry = (space.Player.transform.position - OrbitLayout.ToUnity(InterstellarSpace.PositionOf(space.Database, 78))).magnitude / 0.05f;
            Assert.AreEqual(InterstellarSpace.StartUnits, fromEntry, 10f, "beside the system the Void was entered from");
            yield return Run(1f);
            var target = space.Navigation.Targets.Where(t => t.kind == Navigation.Kind.System)
                                                 .OrderBy(t => (t.Position - space.Player.transform.position).sqrMagnitude).First();
            space.Navigation.SetAutopilot(target);
            Assert.AreSame(target, space.Navigation.AutopilotTarget, "the autopilot flies to the systems");
            space.Player.transform.position = target.Position + Vector3.up * (InterstellarSpace.ArriveUnits * 0.5f * 0.05f);
            yield return WaitFor(() => Find<SpaceLevel>() != space && Find<SpaceLevel>() != null && Find<SpaceLevel>().Layout != null, 30f, "the arrival");
            Assert.AreEqual(target.station, Session.StationIndex);
            Assert.AreEqual(Session.VoidOrbit, Session.PreviousStationIndex);
            Assert.AreEqual(78, Session.VoidReturnStation, "untouched");
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
