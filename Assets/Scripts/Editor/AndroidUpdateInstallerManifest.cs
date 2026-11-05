// AndroidUpdateInstallerManifest.cs
// The in-game updater on Android (GamePatcher, AndroidUpdateInstaller; docs/notes/platforms.md "Update patcher") hands the
// rebuilt APK to the package installer: that needs the REQUEST_INSTALL_PACKAGES permission and the content provider that
// shares the APK (Assets/Plugins/Android/UpdateApkProvider.java) in the manifest. Added to the generated Gradle project's
// unityLibrary manifest here, like AndroidModImportActivity's activities.

using System.IO;
using UnityEditor.Android;
using UnityEngine;

namespace GoF2Remake.EditorTools
{
    public class AndroidUpdateInstallerManifest : IPostGenerateGradleAndroidProject
    {
        const string Permission = "<uses-permission android:name=\"android.permission.REQUEST_INSTALL_PACKAGES\" />";
        const string Provider = "com.joppietoppie.gof2remake.UpdateApkProvider";

        public int callbackOrder => 1;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            // 'path' is the unityLibrary module.
            string manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifest))
            {
                Debug.LogWarning("AndroidUpdateInstallerManifest: no manifest at " + manifest + ", the in-game update can't install");
                return;
            }
            string text = File.ReadAllText(manifest);
            if (!text.Contains("REQUEST_INSTALL_PACKAGES"))
            {
                int app = text.IndexOf("<application", System.StringComparison.Ordinal);
                if (app < 0) { Debug.LogWarning("AndroidUpdateInstallerManifest: no <application> in the manifest"); return; }
                text = text.Insert(app, Permission + "\n  ");
            }
            if (!text.Contains("\"" + Provider + "\""))
            {
                int end = text.IndexOf("</application>", System.StringComparison.Ordinal);
                if (end < 0) { Debug.LogWarning("AndroidUpdateInstallerManifest: no </application> in the manifest"); return; }
                text = text.Insert(end, "    <provider android:name=\"" + Provider + "\"\n"
                                      + "              android:authorities=\"${applicationId}.gof2update\"\n"
                                      + "              android:exported=\"false\"\n"
                                      + "              android:grantUriPermissions=\"true\" />\n  ");
            }
            File.WriteAllText(manifest, text);
        }
    }
}
