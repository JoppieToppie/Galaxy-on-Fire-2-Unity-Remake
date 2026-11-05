// AndroidUpdateInstaller.cs
// Remake-only: Android's half of the in-game updater (GamePatcher, CLAUDE.md "Update patcher" in docs/notes/platforms.md).
// The rebuilt APK (persistentDataPath/_update/files/GoF2Remake.apk) goes to Android's package installer through the Java
// plugin UpdateInstaller (Assets/Plugins/Android/UpdateInstaller.java), which shares it with the installer through its own
// content provider (UpdateApkProvider). Android asks the player to confirm, and once per app to allow it to install apps
// (CanInstall false: OpenInstallSettings opens that switch). The manifest entries (REQUEST_INSTALL_PACKAGES, the provider)
// come from the Editor's AndroidUpdateInstallerManifest. Anywhere else: not available.

using UnityEngine;

namespace GoF2Remake.UI
{
    public static class AndroidUpdateInstaller
    {
        const string JavaClass = "com.joppietoppie.gof2remake.UpdateInstaller";

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject Activity()
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            return player.GetStatic<AndroidJavaObject>("currentActivity");
        }

        static T Call<T>(string method, params object[] args)
        {
            using var c = new AndroidJavaClass(JavaClass);
            using var activity = Activity();
            var all = new object[args.Length + 1];
            all[0] = activity;
            args.CopyTo(all, 1);
            return c.CallStatic<T>(method, all);
        }
#endif

        /// <summary>The plugin is in this build (an Android player).</summary>
        public static bool Available
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                try { using var c = new AndroidJavaClass(JavaClass); return c.GetRawClass() != System.IntPtr.Zero; }
                catch (System.Exception) { return false; }
#else
                return false;
#endif
            }
        }

        /// <summary>Android lets this app install apps (the player allowed it, or Android before 8 doesn't ask).</summary>
        public static bool CanInstall
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                try { return Call<bool>("canInstall"); }
                catch (System.Exception e) { Debug.LogWarning("AndroidUpdateInstaller: " + e.Message); return false; }
#else
                return false;
#endif
            }
        }

        /// <summary>Opens Android's "Install unknown apps" switch for this app.</summary>
        public static void OpenInstallSettings()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try { Call<bool>("openInstallSettings"); }
            catch (System.Exception e) { Debug.LogWarning("AndroidUpdateInstaller: " + e.Message); }
#endif
        }

        /// <summary>Hands 'apkPath' to the installer; null, or why it couldn't.</summary>
        public static string Install(string apkPath)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try { return Call<string>("install", apkPath); }
            catch (System.Exception e) { return e.Message; }
#else
            return "not Android";
#endif
        }
    }
}
