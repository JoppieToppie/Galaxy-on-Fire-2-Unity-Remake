// ModdingGuideBundle.cs
// Every player build carries the AI modding guide (Modding/ai/gof2-modding, Modding.ModdingGuide) for the Mod Builder's MCP
// server's read_guide tool: copied into Assets/Resources/GoF2ModdingGuide before the build and removed again once it has
// finished or failed (EditorApplication.delayCall runs after BuildPipeline.BuildPlayer returns), like BuildVersionStamp's
// fingerprint, so the repository keeps one copy (the folder is git-ignored meanwhile). The Editor reads the repository's
// folder itself.

using System.IO;
using GoF2Remake.Modding;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace GoF2Remake.EditorTools
{
    public class ModdingGuideBundle : IPreprocessBuildWithReport
    {
        const string Folder = "Assets/Resources/" + ModdingGuide.ResourcesFolder;

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            int copied = Copy();
            UnityEngine.Debug.Log($"GoF2: {copied} modding guide file(s) into the build");
            EditorApplication.delayCall += () =>
            {
                AssetDatabase.DeleteAsset(Folder);
                AssetDatabase.SaveAssets();
            };
        }

        /// <summary>Copies the guide's files into the Resources folder and imports them; the count copied.</summary>
        internal static int Copy()
        {
            Directory.CreateDirectory(Folder);
            int copied = 0;
            foreach (var (file, _) in ModdingGuide.Files)
            {
                string source = Path.Combine(ModdingGuide.RepositoryFolder, file);
                if (!File.Exists(source)) { UnityEngine.Debug.LogWarning("GoF2: the modding guide has no " + source); continue; }
                string target = Folder + "/" + file;
                File.Copy(source, target, true);
                AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
                copied++;
            }
            return copied;
        }
    }
}
