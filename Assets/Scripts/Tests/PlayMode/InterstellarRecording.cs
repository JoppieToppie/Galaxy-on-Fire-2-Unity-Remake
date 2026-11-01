// InterstellarRecording.cs
// A recording, not a check: flies an interstellar crossing from an orbit's quick menu to the arrival in another system
// with a test drive mounted, capturing the Game view every frame at a fixed 30 frames a second (Time.captureFramerate, so
// the frames are evenly spaced whatever the Editor's speed) into Temp/Recording/<n>.png, for a video:
//   ffmpeg -framerate 30 -i Temp/Recording/%05d.png -c:v libx264 -pix_fmt yuv420p -crf 20 interstellar.mp4
// It records only when Temp/recording_dir.txt names the folder to write to (else it is ignored, so a suite run doesn't
// record): write that file, then python3 -I Reference/tools/tests/run_tests.py --mode PlayMode --filter
// GoF2Remake.Tests.InterstellarRecording.

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

        [UnityTest]
        public IEnumerator FlyACrossingAndRecordIt()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string named = Path.Combine(root, "Temp", "recording_dir.txt");
            if (!File.Exists(named)) Assert.Ignore("No Temp/recording_dir.txt: nothing to record into");
            folder = File.ReadAllText(named).Trim();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);

            FreePlayAt(78);
            MountDrive(6);
            Session.LaunchedFromStation = true;
            yield return Load("Space");
            yield return WaitFor(() => Find<SpaceLevel>() != null && Find<SpaceLevel>().LaunchCameraOver && Find<SpaceLevel>().Navigation != null, 60f, "the orbit");
            var first = Find<SpaceLevel>();
            yield return Run(5.5f);   // SystemJump: the level is 5 s old before the drive may be called
            Time.captureFramerate = Fps;
            recording = true;
            yield return Capture(2f);   // the orbit, for context

            var entry = first.Navigation.MenuEntries(true).Find(t => t.kind == Navigation.Kind.Slipstream);
            Assert.IsNotNull(entry, "the drive's menu entry");
            first.Navigation.OpenMenu(true);
            yield return Capture(1.5f);
            first.Navigation.ChooseMenuEntry(entry);
            yield return CaptureUntil(() => Find<SpaceLevel>() != first && Find<SpaceLevel>() != null && Find<SpaceLevel>().Layout != null && Find<SpaceLevel>().Layout.interstellar, 40f, "the space between the systems");
            var space = Find<SpaceLevel>();
            yield return CaptureUntil(() => space.Navigation != null && space.Navigation.Targets.Any(t => t.kind == Navigation.Kind.System), 10f, "the systems");
            yield return Capture(2f);

            var nav = space.Navigation;
            var at = space.Player.transform.position;
            var target = nav.Targets.Where(t => t.kind == Navigation.Kind.System && !t.hidden && t.system != space.Layout.systemIndex)
                                    .OrderBy(t => (t.Position - at).sqrMagnitude).First();
            nav.SetAutopilot(target);   // the autopilot to the nearest system: it holds the drive
            yield return CaptureUntil(() => Find<SpaceLevel>() != space && Find<SpaceLevel>() != null && Find<SpaceLevel>().Layout != null && !Find<SpaceLevel>().Layout.interstellar, 240f, "the arrival");
            yield return Capture(8f);   // the warp easing out through the real orbit
            recording = false;
            Time.captureFramerate = 0;
            Debug.Log("Recorded " + frame + " frames into " + folder);
        }
    }
}
