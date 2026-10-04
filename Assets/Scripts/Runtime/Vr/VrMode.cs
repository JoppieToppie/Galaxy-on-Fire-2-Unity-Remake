// VrMode.cs
// Remake: PC VR through OpenXR, only when asked for: the -vr launch flag (or -vrsim: the same VR layout on the desktop
// without a headset, for testing: the mouse looks around and points; in the Editor the GOF2_VR environment variable "on" /
// "sim" or PlayerPrefs debug_vr 1 = on, 2 = sim). XR Plug-in Management doesn't start with the player
// ("GoF2 > Setup > Configure VR (OpenXR)" turns Initialize on Startup off): Start() initialises the OpenXR loader and its
// subsystems itself before the first scene; without a headset or runtime it logs why and the game runs flat.
// Each scene then gets a VrRig (the stereo camera following the scene's own camera, the controllers, the floating UI
// screen and its laser pointer; VrRig.Attach on sceneLoaded).

using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;

namespace GoF2Remake.Vr
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class VrMode
    {
        /// <summary>The VR layout is on (a headset, or the desktop simulation).</summary>
        public static bool Enabled { get; private set; }
        /// <summary>The desktop simulation: no XR, the mouse is the head and the laser.</summary>
        public static bool Simulated { get; private set; }
        /// <summary>A headset renders: OpenXR started.</summary>
        public static bool Headset => Enabled && !Simulated;

        static bool xrStarted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Enabled = Simulated = false;
            xrStarted = false;
        }

        static bool Flag(string flag) =>
            Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

        /// <summary>What was asked for: 0 off, 1 a headset, 2 the simulation.</summary>
        static int Requested()
        {
            if (Flag("-vrsim")) return 2;
            if (Flag("-vr")) return 1;
#if UNITY_EDITOR
            string env = Environment.GetEnvironmentVariable("GOF2_VR");
            if (string.Equals(env, "on", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(env, "sim", StringComparison.OrdinalIgnoreCase)) return 2;
            return PlayerPrefs.GetInt("debug_vr", 0);
#else
            return 0;
#endif
        }

        /// <summary>Bootstrap, before the first scene: the VR layout when asked for, OpenXR for a headset.</summary>
        public static void Start()
        {
            int want = Requested();
            if (want == 0) return;
            if (want == 1 && !StartXr())
            {
                Debug.LogWarning("VrMode: no OpenXR headset or runtime found (start SteamVR / the Oculus app, or use -vrsim): VR off.");
                return;
            }
            Enabled = true;
            Simulated = want == 2;
            Debug.Log(Simulated ? "VrMode: the VR layout, simulated on the desktop (-vrsim)." : "VrMode: OpenXR started.");
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Application.quitting -= Stop;
            Application.quitting += Stop;
        }

        static bool StartXr()
        {
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager == null) return false;
            if (manager.activeLoader == null) manager.InitializeLoaderSync();
            if (manager.activeLoader == null) return false;
            manager.StartSubsystems();
            xrStarted = true;
            // Seated: the tracking origin is where the head starts (the rig sits the eyes on the scene camera).
            var inputs = new System.Collections.Generic.List<UnityEngine.XR.XRInputSubsystem>();
            SubsystemManager.GetSubsystems(inputs);
            foreach (var input in inputs)
            {
                input.TrySetTrackingOriginMode(UnityEngine.XR.TrackingOriginModeFlags.Device);
                input.TryRecenter();
            }
            return true;
        }

        static void Stop()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Application.quitting -= Stop;
            if (!xrStarted) return;
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager != null && manager.activeLoader != null)
            {
                manager.StopSubsystems();
                manager.DeinitializeLoader();
            }
            xrStarted = false;
        }

        /// <summary>The first scene (its sceneLoaded can come before Start subscribes): its rig.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AfterFirstScene()
        {
            if (Enabled && VrRig.Current == null) VrRig.Attach(SceneManager.GetActiveScene());
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (Enabled && mode == LoadSceneMode.Single) VrRig.Attach(scene);
        }
    }
}
