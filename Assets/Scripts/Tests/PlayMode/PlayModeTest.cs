// PlayModeTest.cs
// The base of the play mode tests: the game's data folder in a temporary sandbox (GamePaths), a fresh new game, and
// helpers to load a scene and wait for something. Any error or exception the game logs fails the test (the Test
// Framework's LogAssert), so these tests catch a scene that throws while it builds or runs.

using System;
using System.Collections;
using System.IO;
using GoF2Remake.Data;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public abstract class PlayModeTest
    {
        protected string Sandbox { get; private set; }
        protected static Database Db => Database.Shared;

        [UnitySetUp]
        public IEnumerator EnterSandbox()
        {
            Sandbox = Path.Combine(Path.GetTempPath(), "GoF2PlayTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Sandbox);
            GamePaths.Override = Sandbox;
            Application.runInBackground = true;
            Time.timeScale = 1f;
            Session.ResetNewGame();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator LeaveSandbox()
        {
            if (Multiplayer.NetGame.Active) Multiplayer.NetGame.ShutdownNow();
            AudioListener.pause = false;
            Time.timeScale = 1f;
            // An empty scene, so the next test starts without the last one's level running.
            var empty = SceneManager.CreateScene("Empty " + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s != empty && s.isLoaded) yield return SceneManager.UnloadSceneAsync(s);
            }
            yield return null;
            Session.ResetNewGame();
            GamePaths.Override = null;
            try { if (Directory.Exists(Sandbox)) Directory.Delete(Sandbox, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        /// <summary>The Khador Slipstream fitted (the game's own cruise drive, "gof2:khador_slipstream", numbered per player):
        /// returns its item.</summary>
        protected static int FitSlipstream()
        {
            int drive = Modding.ModContent.ItemIndexOf("gof2:khador_slipstream");
            Session.Equipment.Add(new ItemStack(drive, 1));
            return drive;
        }

        /// <summary>Loads a scene by name and waits until it is the active scene and has run a few frames.</summary>
        protected static IEnumerator Load(string scene)
        {
            SceneManager.LoadScene(scene);
            yield return WaitFor(() => SceneManager.GetActiveScene().name == scene, 30f, "scene " + scene);
            for (int i = 0; i < 5; i++) yield return null;
        }

        /// <summary>Waits until 'done' holds (real time); fails the test after 'seconds'.</summary>
        protected static IEnumerator WaitFor(Func<bool> done, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!done())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail($"Timed out after {seconds} s waiting for {what}.");
                yield return null;
            }
        }

        /// <summary>Lets the game run for 'seconds' of real time.</summary>
        protected static IEnumerator Run(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        /// <summary>A free-play game docked at 'station' (index 20, past the tutorial locks).</summary>
        protected static void FreePlayAt(int station)
        {
            Session.ResetNewGame();
            Session.FreePlay = true;
            Session.CampaignMission = 20;
            Session.StationIndex = station;
            Session.Credits = 100000;
        }

        protected static T Find<T>() where T : UnityEngine.Object => UnityEngine.Object.FindAnyObjectByType<T>();

        const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;

        /// <summary>Calls a (private) method, e.g. a station menu button's.</summary>
        protected static object Invoke(object target, string method, params object[] args)
        {
            var m = target.GetType().GetMethod(method, Any);
            Assert.IsNotNull(m, $"{target.GetType().Name}.{method} not found");
            return m.Invoke(target, args.Length == 0 ? null : args);
        }

        /// <summary>Reads a (private) property or field.</summary>
        protected static T Get<T>(object target, string member)
        {
            var p = target.GetType().GetProperty(member, Any);
            if (p != null) return (T)p.GetValue(target);
            var f = target.GetType().GetField(member, Any);
            Assert.IsNotNull(f, $"{target.GetType().Name}.{member} not found");
            return (T)f.GetValue(target);
        }
    }
}
