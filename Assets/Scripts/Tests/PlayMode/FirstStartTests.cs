// FirstStartTests.cs
// Feature: docs/notes/player-assets.md "First start": a build without the original's content (the test bed: a sparse
// checkout without them) and without a converted copy starts on the setup screen, not the menu; given the player's copy
// ($GOF2_ORIGINAL_PACKAGE: the Android XAPK) it converts it and goes on to the menu. Only where nothing is built in (in the
// full project the screen never shows). The conversion writes ~4 GB into the sandbox: run with TMPDIR on a disk.

using System;
using System.Collections;
using System.IO;
using GoF2Remake.Data;
using GoF2Remake.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GoF2Remake.Tests
{
    [Category("Smoke")]
    public class FirstStartTests : PlayModeTest
    {
        [UnityTest]
        public IEnumerator WithoutACopyTheSetupComesFirstThenTheMenu()
        {
            if (OriginalSetup.BuiltIn) Assert.Ignore("this project has the original's content built in");
            GameContent.Root = Path.Combine(Sandbox, "Original");
            GameContent.HdRoot = Path.Combine(Sandbox, "HD");
            int asked = PlayerPrefs.GetInt(OriginalSetup.HdAskedPref, 0);
            try
            {
                Assert.IsTrue(OriginalSetup.Needed);
                SceneManager.LoadScene("Splash");
                float t0 = Time.realtimeSinceStartup;
                while (OriginalSetupView.Current == null && Time.realtimeSinceStartup - t0 < 10f) yield return null;
                Assert.IsNotNull(OriginalSetupView.Current, "the setup screen");
                yield return new WaitForSecondsRealtime(1f);
                Assert.IsNull(UnityEngine.Object.FindAnyObjectByType<MainMenu>(), "no menu before the copy");

                string package = Environment.GetEnvironmentVariable("GOF2_ORIGINAL_PACKAGE");
                if (string.IsNullOrEmpty(package) || !File.Exists(package)) yield break;   // (the screen only)
                OriginalSetupView.Current.ConvertNow(package);
                var job = OriginalSetupView.Current.Job;
                Assert.IsNotNull(job);
                t0 = Time.realtimeSinceStartup;
                while (!job.Finished && Time.realtimeSinceStartup - t0 < 600f) yield return null;
                Assert.IsTrue(job.Finished, "converted within 10 minutes");
                Assert.IsNull(job.Error, job.Error);
                TestContext.WriteLine($"converted {job.Files} files in {job.Seconds:0} s, {job.Problems.Count} problems");
                Assert.IsTrue(GameContent.Installed);
                // Continue (once the result shows): on to the logos and the menu
                var view = OriginalSetupView.Current;
                t0 = Time.realtimeSinceStartup;
                while (!view.Continue() && Time.realtimeSinceStartup - t0 < 5f) yield return null;
                Assert.IsTrue(Time.realtimeSinceStartup - t0 < 5f, "the result shown, Continue pressed");
                // the HD question once (docs/notes/player-assets.md "HD resources"): not now
                if (view.AskingHd) Assert.IsTrue(view.NotNow());
                t0 = Time.realtimeSinceStartup;
                while (UnityEngine.Object.FindAnyObjectByType<MainMenu>() == null && Time.realtimeSinceStartup - t0 < 60f) yield return null;
                Assert.IsNotNull(UnityEngine.Object.FindAnyObjectByType<MainMenu>(), "the menu after the conversion");
                yield return new WaitForSecondsRealtime(4f);
                Assert.IsNotNull(UnityEngine.Object.FindAnyObjectByType<GoF2Remake.Visuals.MenuBackground>(), "its backdrop");
            }
            finally
            {
                // (the splash outlives its scene: gone with the test, the screen with it)
                foreach (var s in UnityEngine.Object.FindObjectsByType<StartupSplash>(FindObjectsInactive.Include)) UnityEngine.Object.Destroy(s.gameObject);
                GameContent.Root = null;
                GameContent.HdRoot = null;
                PlayerPrefs.SetInt(OriginalSetup.HdAskedPref, asked);
                GameContent.Refresh();
            }
        }
    }
}
