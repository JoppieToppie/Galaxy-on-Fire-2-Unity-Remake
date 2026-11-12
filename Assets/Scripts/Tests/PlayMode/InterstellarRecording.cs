// InterstellarRecording.cs
// A recording, not a check: flies an interstellar crossing from an orbit's quick menu to the arrival in another system
// with a test drive mounted, capturing the Game view every frame at a fixed 30 frames a second (Time.captureFramerate, so
// the frames are evenly spaced whatever the Editor's speed) into Temp/Recording/<n>.png, for a video:
//   ffmpeg -framerate 30 -i Temp/Recording/%05d.png -c:v libx264 -pix_fmt yuv420p -crf 20 interstellar.mp4
// It records only when Temp/recording_dir.txt names the folder to write to (else it is ignored, so a suite run doesn't
// record): write that file, then python3 -I Reference/tools/tests/run_tests.py --mode PlayMode --filter
// GoF2Remake.Tests.InterstellarRecording.FlyACrossingAndRecordIt (or .FlyToTheVoidAndBackAndRecordIt: the autopilot
// menu's dive into the Void, and out again to the nearest system; or .PickAPlanetAndRecordIt: by hand, the nearest system
// with planets of its own locked, its planets round its sun, one that isn't the gate locked and so picked, the cruise held
// to the arrival in that planet's orbit).

using System.Collections;
using System.IO;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GoF2Remake.Tests
{
    [Category("Recording")]
    public class InterstellarRecording : PlayModeTest
    {
        const string Mod = "interstellar_recording";
        const int Fps = 30;
        string folder;
        int frame;
        bool recording;

        static void MountDrive(int cells)
        {
            string dir = Path.Combine(ModManager.UserFolder, Mod);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, ModManifest.FileName), "{ \"id\": \"" + Mod + "\", \"name\": \"Recording\", \"version\": \"1.0\", \"author\": \"Tests\" }");
            File.WriteAllText(Path.Combine(dir, "items.json"), "[ { \"id\": \"drive\", \"base\": 85, \"name\": \"Khador Slipstream\", \"techLevel\": 10, \"occurrence\": 0, \"price\": [1000, 1100], " +
                              "\"stats\": { \"cruiseSpeed\": 800, \"cruiseRange\": 90000, \"cruiseChargeMs\": 2000, \"energyCells\": 1, \"interstellar\": 1 } } ]");
            ModManager.Scan();
            ModManager.SetEnabled(Mod, true);
            Session.Equipment.Add(new ItemStack(ModContent.ItemIndexOf(Mod + ":drive"), 1));
            Session.Cargo.Add(new ItemStack(GalaxyMap.EnergyCellItem, cells));
            Cheats.FreeJumps = false;
        }

        [TearDown]
        public void Stop()
        {
            recording = false;
            Time.captureFramerate = 0;
            ModManager.SetEnabled(Mod, false);
        }

        [OneTimeTearDown]
        public void MountedModsBack() { ModManager.Scan(); }

        void Shot()
        {
            if (recording) ScreenCapture.CaptureScreenshot(Path.Combine(folder, (frame++).ToString("D5") + ".png"));
        }

        /// <summary>Captures 'seconds' worth of frames (counted, not timed: the quick menu stops the game clock, and every
        /// frame is 1 / Fps of the video anyway).</summary>
        IEnumerator Capture(float seconds)
        {
            for (int i = 0; i < Mathf.RoundToInt(seconds * Fps); i++)
            {
                Shot();
                yield return null;
            }
        }

        IEnumerator CaptureUntil(System.Func<bool> done, float seconds, string what)
        {
            int left = Mathf.RoundToInt(seconds * Fps);
            while (!done())
            {
                if (left-- <= 0) Assert.Fail("Timed out after " + seconds + " s waiting for " + what);
                Shot();
                yield return null;
            }
        }

        void OpenFolder()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string named = Path.Combine(root, "Temp", "recording_dir.txt");
            if (!File.Exists(named)) Assert.Ignore("No Temp/recording_dir.txt: nothing to record into");
            folder = File.ReadAllText(named).Trim();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
        }

        /// <summary>From the orbit, 5 s old, through the drive's quick menu entry into the space between the systems.</summary>
        IEnumerator Slipstream(SpaceLevel first)
        {
            var entry = first.Navigation.MenuEntries(true).Find(t => t.kind == Navigation.Kind.Slipstream);
            Assert.IsNotNull(entry, "the drive's menu entry");
            first.Navigation.OpenMenu(true);
            yield return Capture(1.5f);
            first.Navigation.ChooseMenuEntry(entry);
            yield return CaptureUntil(() => Find<SpaceLevel>() != first && Find<SpaceLevel>() != null && Find<SpaceLevel>().Layout != null && Find<SpaceLevel>().Layout.interstellar, 40f, "the space between the systems");
            var space = Find<SpaceLevel>();
            yield return CaptureUntil(() => space.Navigation != null && space.Navigation.Targets.Any(t => t.kind == Navigation.Kind.System), 10f, "the systems");
            yield return Capture(2f);
        }

        /// <summary>The autopilot (it holds the drive) to 'pick''s system, through to the arrival and the warp easing out.</summary>
        IEnumerator FlyTo(System.Func<Navigation.Target, bool> pick)
        {
            var space = Find<SpaceLevel>();
            var nav = space.Navigation;
            var at = space.Player.transform.position;
            var target = nav.Targets.Where(t => t.kind == Navigation.Kind.System && t.planetOf == null && !t.hidden && t.station != Session.StationIndex && pick(t))
                                    .OrderBy(t => (t.Position - at).sqrMagnitude).First();
            nav.SetAutopilot(target);
            yield return CaptureUntil(() => Find<SpaceLevel>() != space && Find<SpaceLevel>() != null && Find<SpaceLevel>().Layout != null && !Find<SpaceLevel>().Layout.interstellar, 240f, "the arrival");
            yield return Capture(8f);   // the warp easing out through the real orbit
        }

        IEnumerator OrbitAt(int station)
        {
            FreePlayAt(station);
            MountDrive(6);
            Session.LaunchedFromStation = true;
            yield return Load("Space");
            yield return WaitFor(() => Find<SpaceLevel>() != null && Find<SpaceLevel>().LaunchCameraOver && Find<SpaceLevel>().Navigation != null, 60f, "the orbit");
            yield return Run(5.5f);   // SystemJump: the level is 5 s old before the drive may be called
            Time.captureFramerate = Fps;
            recording = true;
            yield return Capture(2f);   // the orbit, for context
        }

        [UnityTest, Timeout(900000)]   // a 1080p capture runs well under real time
        public IEnumerator FlyACrossingAndRecordIt()
        {
            OpenFolder();
            yield return OrbitAt(78);
            yield return Slipstream(Find<SpaceLevel>());
            yield return FlyTo(t => true);   // the nearest system
            recording = false;
            Time.captureFramerate = 0;
            Debug.Log("Recorded " + frame + " frames into " + folder);
        }

        [UnityTest, Timeout(1500000)]
        public IEnumerator PickAPlanetAndRecordIt()
        {
            OpenFolder();
            yield return OrbitAt(78);
            yield return Slipstream(Find<SpaceLevel>());
            var space = Find<SpaceLevel>();
            var nav = space.Navigation;
            nav.EncountersAllowed = false;   // this take is the planet: no pirates pulling the unarmed test ship out on the way
            var ship = space.Player;
            var at = ship.transform.position;
            // the nearest system whose planets are targets (two or more stations) and far enough out that its arrival isn't
            // settled yet (InterstellarSpace.ApproachUnits: there its planets are gone), and one of them that isn't its gate
            float far = InterstellarSpace.ApproachUnits * 2f * 0.05f;
            var sun = nav.Targets.Where(t => t.kind == Navigation.Kind.System && t.planetOf == null && !t.hidden && t.system != space.Layout.systemIndex
                                             && (t.Position - at).magnitude > far
                                             && nav.Targets.Count(p => p.planetOf == t && !p.disabled && p.station != t.station) > 0)
                                 .OrderBy(t => (t.Position - at).sqrMagnitude).First();
            int gate = sun.station;
            var planet = nav.Targets.First(p => p.planetOf == sun && !p.disabled && p.station != gate);
            // Flown as a pilot does, by hand (the ship steered onto a point, as the stick would; no autopilot, so the crosshair
            // locks): the nose onto the system till it locks; its AUTOPILOT to it, and within a light year of its star its zone
            // opens, its planets round it; the autopilot off, the nose onto the planet's marker till that locks, which picks it;
            // then the lock's AUTOPILOT prompt, which holds the drive, to the arrival in its orbit.
            nav.SetAutopilot(null);
            ship.autopilotTarget = () => sun.Position;
            yield return CaptureUntil(() => nav.Locked == sun, 30f, "the system's lock");
            yield return Capture(1.5f);
            // the lock's AUTOPILOT to the system: within a light year of its star its zone opens, its planets come out round it
            ship.autopilotTarget = null;
            Assert.IsTrue(nav.Interact(), "the AUTOPILOT prompt");
            yield return CaptureUntil(() => nav.Zone == sun, 240f, "the system's zone");
            yield return Capture(3f);
            Assert.IsTrue(nav.Interact(), "the autopilot off again: the crosshair locks by hand");
            ship.autopilotTarget = () => planet.Position;
            yield return CaptureUntil(() => nav.Locked == planet, 30f, "the planet's lock");
            yield return Capture(2.5f);
            Assert.AreEqual(planet.station, sun.station, "locking the planet picked it");
            ship.autopilotTarget = null;
            Assert.IsTrue(nav.Interact(), "the lock's AUTOPILOT prompt");   // the autopilot to the planet: it holds the drive here
            yield return CaptureUntil(() => Find<SpaceLevel>() != space && Find<SpaceLevel>() != null && Find<SpaceLevel>().Layout != null && !Find<SpaceLevel>().Layout.interstellar, 300f, "the arrival");
            yield return Capture(8f);   // the warp easing out through the picked planet's orbit
            Assert.AreEqual(planet.station, Session.StationIndex, "arrived at the planet picked, not the gate");
            recording = false;
            Time.captureFramerate = 0;
            Debug.Log($"Recorded {frame} frames into {folder}: {sun.name}, {planet.name} (gate {gate})");
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator FlyToTheVoidAndBackAndRecordIt()
        {
            OpenFolder();
            yield return OrbitAt(78);
            yield return Slipstream(Find<SpaceLevel>());
            var space = Find<SpaceLevel>();
            space.Navigation.OpenMenu();
            yield return Capture(1.5f);
            space.Navigation.ChooseMenuEntry(space.Navigation.VoidDive);   // the dive: no target drawn, no distance
            yield return CaptureUntil(() => Find<SpaceLevel>() != space && Find<SpaceLevel>() != null && Find<SpaceLevel>().Layout != null, 60f, "the Void");
            yield return Capture(8f);
            Assert.AreEqual(Session.VoidOrbit, Session.StationIndex);
            int frames = Mathf.RoundToInt(5.5f * Fps);   // the drive again once the level is 5 s old (counted: the Void's
            for (int i = 0; i < frames; i++) { Shot(); yield return null; }   // arrival is a warp, no camera to wait for)
            yield return Slipstream(Find<SpaceLevel>());
            int entered = Find<SpaceLevel>().Database.Stations.Find(s => s.index == 78).system;
            yield return FlyTo(t => t.system != entered);   // the nearest other system out of the Void (it starts beside 78's)
            recording = false;
            Time.captureFramerate = 0;
            Debug.Log("Recorded " + frame + " frames into " + folder);
        }
    }
}
