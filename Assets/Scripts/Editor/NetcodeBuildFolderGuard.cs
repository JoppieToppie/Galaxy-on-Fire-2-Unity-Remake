// NetcodeBuildFolderGuard.cs
// Netcode's GhostBehaviourBuildProcessor writes its script order to Assets/netcode-build-assets-temp/script-order.asset
// before every build, creating the folder only when the AssetDatabase doesn't list it, and its post-build step deletes the
// folder from the disk alone (FileUtil.DeleteFileOrDirectory), so the AssetDatabase still lists it. A second build without
// a refresh in between (a release's Windows build right after the Windows dedicated server, 2026-11-02) then failed to
// create the asset ("Parent directory must exist"), shipped without the sort order and threw IndexOutOfRangeException in
// GhostBehaviourTypeManager.InitializeGhostBehaviourInfos at startup. This refreshes the AssetDatabase first when the folder
// is listed but gone.

using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace GoF2Remake.EditorTools
{
    public class NetcodeBuildFolderGuard : IPreprocessBuildWithReport
    {
        const string Folder = "Assets/netcode-build-assets-temp";

        // Before GhostBehaviourBuildProcessor (1000).
        public int callbackOrder => 999;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (AssetDatabase.IsValidFolder(Folder) && !Directory.Exists(Folder))
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                UnityEngine.Debug.Log("GoF2: " + Folder + " was listed but deleted (the last build's clean-up); the AssetDatabase was refreshed.");
            }
        }
    }
}
