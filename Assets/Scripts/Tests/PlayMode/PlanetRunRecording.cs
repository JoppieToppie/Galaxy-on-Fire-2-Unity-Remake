// PlanetRunRecording.cs
// A recording, not a check (as InterstellarRecording): the Khador Slipstream flown to each other station's planet in turn, the
// jump on a locked planet replaced by the run, the orbit building ahead, the arrival. Each run in its own folder (run0, run1...)
// of the one Temp/recording_dir.txt names (else ignored): python3 -I Reference/tools/tests/run_tests.py --mode PlayMode --filter GoF2Remake.Tests.PlanetRunRecording.

using System.Collections;
using System.IO;
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
    public class PlanetRunRecording : PlayModeTest
    {
        const int Fps = 30, Station = 95;
        string folder;
        int frame;
        bool recording;

        [TearDown]
        public void Stop()
        {
            recording = false;
            Time.captureFramerate = 0;
        }

        void Shot()
        {
            if (recording) ScreenCapture.CaptureScreenshot(Path.Combine(folder, (frame++).ToString("D5") + ".png"));
        }

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

        [UnityTest]
        public IEnumerator FlyToAPlanetAndRecordIt([Values(0, 1, 2, 3)] int run)
        {
            string named = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", "recording_dir.txt");
            if (!File.Exists(named)) Assert.Ignore("No Temp/recording_dir.txt: nothing to record into");
            folder = Path.Combine(File.ReadAllText(named).Trim(), "run" + run);
            frame = 0;
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);

            FreePlayAt(Station);
            Session.Equipment.Add(new ItemStack(ModContent.ItemIndexOf("gof2:khador_slipstream"), 1));
            Session.Cargo.Add(new ItemStack(GalaxyMap.EnergyCellItem, 8));
            Cheats.FreeJumps = false;
            Session.LaunchedFromStation = true;
            yield return Load("Space");
            yield return WaitFor(() => Find<SpaceLevel>() != null && Find<SpaceLevel>().LaunchCameraOver && Find<SpaceLevel>().Navigation != null, 60f, "the orbit");
            yield return WaitFor(() => !Find<SpaceLevel>().Navigation.HostilesPresent, 60f, "a quiet orbit");
            yield return Run(2f);
            var space = Find<SpaceLevel>();
            var nav = space.Navigation;
            var planets = nav.Targets.FindAll(t => t.kind == Navigation.Kind.Planet && !t.hidden);
            if (run >= planets.Count) Assert.Ignore("This orbit has " + planets.Count + " planets");
            var planet = planets[run];
            var ship = space.Player.transform;
            ship.position = new Vector3(0f, 4000f, 0f);
            for (int i = 0; i < 3; i++)
            {
                yield return Run(0.7f);
                ship.rotation = Quaternion.LookRotation(planet.Position - Camera.main.transform.position, Vector3.up);
            }
            yield return WaitFor(() => nav.Locked == planet, 10f, "the lock");
            Time.captureFramerate = Fps;
            recording = true;
            yield return Capture(2f);   // the planet locked
            Assert.IsTrue(nav.Interact(), "the jump");
            yield return CaptureUntil(() => Find<SpaceLevel>() != space && Find<SpaceLevel>() != null && Find<SpaceLevel>().Layout != null, 60f, "the arrival");
            yield return Capture(9f);   // the warp easing out through the real orbit
            recording = false;
            Time.captureFramerate = 0;
            File.WriteAllText(Path.Combine(folder, "to.txt"), planet.name);
            Debug.Log("Recorded " + frame + " frames into " + folder);
        }
    }
}
