// SteamDeckRelaunch.cs
// Remake (#232: 0 FPS on a Steam Deck when started from Steam): Steam sets SteamDeck=1 for every launch on the Deck, and
// Unity's Linux player (7000.0.0a12) then picks a Steam Deck presentation path that wedges its render loop (GPU idle, the
// main thread waiting on a swap that never completes). The engine reads the variable before any game code runs, so it
// can't just be unset here: the Linux player instead restarts itself once without it, as early as managed code runs
// (SubsystemRegistration): marks the environment GOF2_STEAMDECK=1 (the Deck detection, UiLayout and InputDeviceLog, still
// sees a Deck), unsets SteamDeck and execv's /proc/self/exe with the original command line. Same process id, so Steam
// keeps tracking the game. The marker is also the loop guard: a process that has it never restarts again. Any failure
// just carries on as before. Not in the Editor, a dedicated server (UNITY_SERVER, batch mode, -server) or other
// platforms. GOF2_KEEP_STEAMDECK=1 turns the restart off (to test whether a newer Unity has fixed the player).
// Without the restart, the player-side workaround is the Steam launch option  env -u SteamDeck %command%

#if UNITY_STANDALONE_LINUX && !UNITY_EDITOR && !UNITY_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
#endif
using UnityEngine;

namespace GoF2Remake
{
    public static class SteamDeckRelaunch
    {
        /// <summary>Set in the restarted process's environment: it was started by Steam on a Steam Deck.</summary>
        public const string Marker = "GOF2_STEAMDECK";

#if UNITY_STANDALONE_LINUX && !UNITY_EDITOR && !UNITY_SERVER
        [DllImport("libc", SetLastError = true)]
        static extern int setenv(string name, string value, int overwrite);

        [DllImport("libc", SetLastError = true)]
        static extern int unsetenv(string name);

        [DllImport("libc", SetLastError = true)]
        static extern int execv(string path, string[] argv);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Relaunch()
        {
            try
            {
                string deck = Environment.GetEnvironmentVariable("SteamDeck");
                if (string.IsNullOrEmpty(deck) || deck == "0") return;
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Marker))) return;   // already restarted once
                if (Environment.GetEnvironmentVariable("GOF2_KEEP_STEAMDECK") == "1") return;
                if (Application.isBatchMode || Multiplayer.DedicatedServer.Enabled) return;

                string[] args = CommandLine();
                if (args == null || args.Length == 0) return;
                var argv = new string[args.Length + 1];   // execv wants a null-terminated array
                Array.Copy(args, argv, args.Length);

                if (setenv(Marker, "1", 1) != 0) return;
                if (unsetenv("SteamDeck") != 0) { unsetenv(Marker); return; }
                Debug.Log("SteamDeckRelaunch: SteamDeck is set (Unity's Steam Deck path stalls the render loop, #232), restarting without it");
                execv("/proc/self/exe", argv);
                // Only gets here when execv failed: put the environment back and run as before.
                Debug.LogWarning($"SteamDeckRelaunch: execv failed (errno {Marshal.GetLastWin32Error()}), carrying on with SteamDeck set");
                setenv("SteamDeck", deck, 1);
                unsetenv(Marker);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"SteamDeckRelaunch: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>The original argv, argv[0] included: /proc/self/cmdline (exact, NUL-separated), else the runtime's copy.</summary>
        static string[] CommandLine()
        {
            try
            {
                byte[] raw = File.ReadAllBytes("/proc/self/cmdline");
                var list = new List<string>();
                int start = 0;
                for (int i = 0; i < raw.Length; i++)
                {
                    if (raw[i] != 0) continue;
                    list.Add(Encoding.UTF8.GetString(raw, start, i - start));
                    start = i + 1;
                }
                if (start < raw.Length) list.Add(Encoding.UTF8.GetString(raw, start, raw.Length - start));
                if (list.Count > 0) return list.ToArray();
            }
            catch (Exception) { }
            return Environment.GetCommandLineArgs();
        }
#endif
    }
}
