// SceneSmokeTests.cs
// The three scenes run without errors: the main menu with its live backdrop, a station of every hangar type with each of
// its windows, the flight level after a launch, docking back, and the auto-save landing in the sandbox (CLAUDE.md "Space
// scene", "Station scene", "Save games").

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.UI;
using GoF2Remake.World;
using GoF2Remake.Visuals;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GoF2Remake.Tests
{
    [Category("Smoke")]
    public class SceneSmokeTests : PlayModeTest
    {
        [UnityTest]
        public IEnumerator MainMenuBuildsItsBackdrop()
        {
            yield return Load("MainMenu");
            Assert.IsNotNull(Find<MainMenu>(), "MainMenu");
            yield return Run(4f);
            var level = Find<MenuBackground>();
            Assert.IsNotNull(level, "MenuBackground");
            Assert.IsTrue(Object.FindObjectsByType<NpcShip>().Length > 0 || Find<Traffic>() != null, "the backdrop's traffic");
        }

        /// <summary>The Splash scene (CLAUDE.md "Startup splash"): both logos over the menu scene loading behind them, the
        /// menu theme from the start and carried into the menu, then the title.</summary>
        [UnityTest]
        public IEnumerator SplashShowsTheLogosThenTheMenu()
        {
            float start = Time.realtimeSinceStartup;
            yield return Load("Splash");
            Assert.IsTrue(StartupSplash.Running, "the logos run");
            var music = GameObject.Find("Splash music")?.GetComponent<AudioSource>();
            Assert.IsTrue(music != null && music.isPlaying, "the menu theme plays with the first logo");
            yield return WaitFor(() => SceneManager.GetActiveScene().name == "MainMenu", 30f, "the menu scene");
            Assert.IsTrue(StartupSplash.Running, "the menu came in while a logo was shown");
            yield return null;   // the splash's LateUpdate has seen the menu's sources
            Assert.IsFalse(music.mute, "the theme is heard under the logos");
            foreach (var s in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude))
                if (s != music) Assert.IsTrue(s.mute, $"only the theme is heard under the logos: {s.name}");
            yield return WaitFor(() => !StartupSplash.Running, 30f, "the end of the logos");
            yield return null;
            foreach (var s in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude))
                Assert.IsFalse(s.mute, $"unmuted after the logos: {s.name}");
            Assert.GreaterOrEqual(Time.realtimeSinceStartup - start, 5f, "two logos of 3 s each");
            var menu = Find<MainMenu>();
            Assert.IsNotNull(menu, "MainMenu");
            Assert.AreSame(music, menu.musicSource, "the menu took the splash's theme over");
            Assert.IsTrue(music.isPlaying, "the theme still plays");
            Assert.AreEqual(1, Object.FindObjectsByType<AudioListener>().Length, "one listener");
        }

        /// <summary>One station per hangar (StationTables.HangarIndex): the four races, the deep science station, the Kaamo Club.</summary>
        static IEnumerable<int> HangarStations()
        {
            var db = Database.Load();
            var pirateBases = new HashSet<int> { 1, 33, 47, 86 };
            for (int race = 0; race < 4; race++)
            {
                var st = db.Stations.FirstOrDefault(s => s.index < 100 && !pirateBases.Contains(s.index)
                                                          && db.Systems[s.system].raceId == race && !OrbitLayout.IsEmptyOrbit(s.index, 20));
                if (st != null) yield return st.index;
            }
            yield return 100;
            yield return 108;
        }

        [UnityTest]
        public IEnumerator StationOfEveryHangarTypeLoads([ValueSource(nameof(HangarStations))] int station)
        {
            FreePlayAt(station);
            yield return Load("Station");
            var level = Find<StationLevel>();
            Assert.IsNotNull(level, "StationLevel");
            Assert.IsNotNull(Find<StationMenu>(), "StationMenu");
            yield return Run(2f);
            level.SetView(StationView.Lounge, true);
            yield return Run(1.5f);
            level.SetView(StationView.Hangar, true);
            yield return Run(1f);
        }

        [UnityTest]
        public IEnumerator StationWindowsOpenAndClose()
        {
            FreePlayAt(78);
            yield return Load("Station");
            var menu = Find<StationMenu>();
            yield return WaitFor(() => !Get<bool>(menu, "UiBlocked") && !Get<bool>(menu, "ArrivalPending"), 15f, "the station menu");
            foreach (var open in new[] { "OpenHangar", "OpenLounge", "OpenMissions", "OpenStatus", "OpenSystemMenu" })
            {
                Invoke(menu, open);
                yield return Run(0.6f);
                Invoke(menu, "Back");
                yield return Run(0.4f);
            }
        }

        [UnityTest]
        public IEnumerator StarMapOpensInEveryMode()
        {
            FreePlayAt(78);
            yield return Load("Station");
            foreach (StarMapMode mode in System.Enum.GetValues(typeof(StarMapMode)))
            {
                bool closed = false;
                var map = StarMap.Open(Db, mode, mode == StarMapMode.Khador, _ => closed = true);
                Assert.IsNotNull(map, mode.ToString());
                yield return Run(1f);
                Assert.IsTrue(StarMap.IsOpen);
                Invoke(map, "Close", new StarMapResult { station = -1 });
                yield return Run(0.3f);
                Assert.IsTrue(closed, mode + " closed callback");
                Assert.IsFalse(StarMap.IsOpen);
            }
        }

        [UnityTest]
        public IEnumerator LaunchFlyAndDockBack()
        {
            FreePlayAt(78);
            yield return Load("Station");
            Find<StationLevel>().Launch();
            yield return WaitFor(() => Find<SpaceLevel>() != null && Find<SpaceLevel>().Player != null, 60f, "the flight level");
            var space = Find<SpaceLevel>();
            Assert.AreEqual(78, space.Layout.stationIndex);
            yield return Run(5f);
            Assert.IsTrue(space.Player.gameObject.activeInHierarchy, "the player ship");
            space.Dock();
            yield return WaitFor(() => Find<StationLevel>() != null, 30f, "the station after docking");
            yield return Run(2f);
            Assert.AreEqual(78, Session.StationIndex);
        }

        [UnityTest]
        public IEnumerator DockingAutoSavesIntoTheSandbox()
        {
            FreePlayAt(70);
            yield return Load("Station");
            yield return Run(3f);
            SaveGame.AutoSave();
            string saves = Path.Combine(Sandbox, "Saves");
            Assert.IsTrue(Directory.Exists(saves) && Directory.GetFiles(saves).Length > 0, "the auto-save in the sandbox");
            long credits = Session.Credits;
            Session.Credits = 1;
            Assert.IsTrue(SaveGame.Load(SaveGame.AutoSaveSlot), "loading it back");
            Assert.AreEqual(credits, Session.Credits);
            Assert.AreEqual(70, Session.StationIndex);
        }

        [UnityTest]
        public IEnumerator EveryStationOrbitBuilds([ValueSource(nameof(OrbitSample))] int station)
        {
            FreePlayAt(station);
            Session.LaunchedFromStation = true;
            yield return Load("Space");
            var space = Find<SpaceLevel>();
            Assert.IsNotNull(space, "SpaceLevel");
            yield return Run(3f);
        }

        /// <summary>A spread of orbits: every system's gate station, the Kaamo Club, Loma, the supernova and Void systems.</summary>
        static IEnumerable<int> OrbitSample()
        {
            var db = Database.Load();
            var picks = new SortedSet<int> { 10, 27, 57, 100, 101, 102, 108, 109, 111, 120 };
            foreach (var s in db.Systems) if (s.jumpgateStation >= 0 && s.jumpgateStation < db.Stations.Count) picks.Add(s.jumpgateStation);
            return picks;
        }
    }
}
