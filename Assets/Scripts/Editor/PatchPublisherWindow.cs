// PatchPublisherWindow.cs
// GoF2 > Build > Update Patch: makes the in-game updater's release files (CLAUDE.md "Update patcher" in
// docs/notes/platforms.md), all into Build/Release beside the zips, to upload with them to the GitHub release:
// - Windows / Linux: Build/Release/Windows/GoF2Remake and Build/Release/Linux/GoF2Remake (whichever exist), and Android: the
//   newest .apk in Build/Release (or Build/Release/Android). PatchPublish cuts each into chunks and writes
//   GoF2Remake-<platform>-<tag>.pack (the chunks no earlier release has) and GoF2Remake-<platform>-<tag>.patch.json.
// - UWP: the newest .msix under Build/Release/UWP as GoF2Remake-UWP-<tag>.msix, with GoF2Remake-UWP.appinstaller.
// What earlier releases published comes from their .patch.json files on GitHub, fetched into Library/GoF2PatchHistory
// first (FetchHistory), so a cleared Build folder or another machine still adds only the new chunks.
// The release tag defaults to the builds' version date. From an eval: StartAll(tag or null), then poll Busy / LastResult.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using GoF2Remake.UI;
using UnityEditor;
using UnityEngine;

namespace GoF2Remake.EditorTools
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class PatchPublisherWindow : EditorWindow
    {
        static readonly string[] FolderPlatforms = { "Windows", "Linux" };
        const string OutDir = "Build/Release";
        public const string HistoryDir = "Library/GoF2PatchHistory";

        public static volatile bool Busy;
        public static volatile string LastResult = "";
        static volatile string status = "";
        static volatile float progress;
        string tagField = "";

        [MenuItem("GoF2/Build/Update Patch", priority = 234)]
        static void Open() => GetWindow<PatchPublisherWindow>(true, "Update Patch");

        static string Newest(string folder, string pattern) =>
            !Directory.Exists(folder) ? null
            : Directory.GetFiles(folder, pattern, SearchOption.AllDirectories)
                .Where(f => !f.Replace('\\', '/').Contains("/Dependencies/"))
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();

        /// <summary>The chunked builds found: Windows / Linux folders, the Android APK.</summary>
        static List<PatchPublish.Build> Builds()
        {
            var list = new List<PatchPublish.Build>();
            foreach (var p in FolderPlatforms)
            {
                var b = PatchPublish.Inspect(Path.Combine(OutDir, p, "GoF2Remake"));
                if (b != null) list.Add(b);
            }
            string apk = new[] { Newest(Path.Combine(OutDir, "Android"), "*.apk"), Directory.Exists(OutDir) ? Directory.GetFiles(OutDir, "*.apk").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null }
                .Where(f => f != null).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (apk != null)
            {
                try { var b = PatchPublish.Inspect(apk); if (b != null) list.Add(b); }
                catch (Exception e) { Debug.LogWarning("Update Patch: can't read " + apk + ": " + e.Message); }
            }
            return list;
        }

        /// <summary>The UWP package found, or null.</summary>
        static string UwpPackage()
        {
            string msix = Newest(Path.Combine(OutDir, "UWP"), "*.msix") ?? Newest(Path.Combine(OutDir, "UWP"), "*.appx");
            return msix;
        }

        [Serializable] class ReleaseList { public Release[] items; }
        [Serializable] class Release { public string tag_name; public bool draft; public Asset[] assets; }
        [Serializable] class Asset { public string name; public string browser_download_url; }

        /// <summary>Downloads the .patch.json files of the GitHub releases into the history (those not fetched before).
        /// Returns how many were added.</summary>
        public static async Task<int> FetchHistory()
        {
            Directory.CreateDirectory(HistoryDir);
            string fetchedList = Path.Combine(HistoryDir, "fetched.txt");
            var fetched = new HashSet<string>(File.Exists(fetchedList) ? File.ReadAllLines(fetchedList) : new string[0]);
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("GoF2Remake-UpdatePatch");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            string json = await http.GetStringAsync($"https://api.github.com/repos/{PatchManifest.Repository}/releases?per_page=100");
            var list = JsonUtility.FromJson<ReleaseList>("{\"items\":" + json + "}");
            int added = 0;
            foreach (var r in list.items ?? new Release[0])
            {
                if (r == null || r.draft || r.assets == null) continue;
                foreach (var a in r.assets)
                {
                    if (a?.name == null || !a.name.EndsWith(".patch.json") || fetched.Contains(r.tag_name + "/" + a.name)) continue;
                    string text = await http.GetStringAsync(a.browser_download_url);
                    if (PatchPublish.AddToHistory(HistoryDir, text)) added++;
                    fetched.Add(r.tag_name + "/" + a.name);
                }
            }
            File.WriteAllLines(fetchedList, fetched.OrderBy(s => s, StringComparer.Ordinal));
            return added;
        }

        /// <summary>Makes every release file for the builds found, on a thread ('tag' null: the builds' version date).
        /// False when busy or there's nothing to publish.</summary>
        public static bool StartAll(string tag = null)
        {
            if (Busy) return false;
            var builds = Builds();
            string msix = UwpPackage();
            if (builds.Count == 0 && msix == null)
            {
                LastResult = "No release builds in Build/Release.";
                Debug.LogWarning("Update Patch: " + LastResult);
                return false;
            }
            if (string.IsNullOrWhiteSpace(tag)) tag = null;
            Busy = true;
            LastResult = "";
            Task.Run(async () =>
            {
                var lines = new List<string>();
                try
                {
                    status = "Fetching earlier releases' manifests from GitHub";
                    progress = 0f;
                    try { lines.Add($"History: {await FetchHistory()} manifests fetched from GitHub"); }
                    catch (Exception e) { lines.Add("History: GitHub not reached (" + e.Message + "), using what " + HistoryDir + " has"); }
                    foreach (var b in builds)
                    {
                        var r = PatchPublish.Run(b, HistoryDir, OutDir, tag, progress: (s, f) => { status = b.Platform + ": " + s; progress = f; });
                        lines.Add(r.ToString());
                    }
                    if (msix != null)
                    {
                        status = "UWP: copying the package";
                        progress = 0.5f;
                        lines.Add(PatchPublish.PublishUwp(msix, OutDir, tag).ToString());
                    }
                    lines.Add("Upload with the zips: the .pack, .patch.json, .msix and .appinstaller files in Build/Release.");
                    LastResult = string.Join("\n", lines);
                    Debug.Log("Update Patch: " + LastResult);
                }
                catch (Exception e)
                {
                    LastResult = "Failed: " + e.Message;
                    Debug.LogError("Update Patch: " + e);
                }
                finally
                {
                    Busy = false;
                    status = "";
                }
            });
            return true;
        }

        void OnInspectorUpdate() => Repaint();

        void OnGUI()
        {
            EditorGUILayout.HelpBox("Makes the in-game updater's release files in Build/Release: per platform a .pack (the chunks no " +
                "earlier release has) and a .patch.json, and for UWP the .msix with GoF2Remake-UWP.appinstaller. Upload them with the " +
                "zips to the GitHub release; players with an older build then download only what changed.", MessageType.None);
            using (new EditorGUI.DisabledScope(Busy))
            {
                var builds = Builds();
                string msix = UwpPackage();
                if (builds.Count == 0 && msix == null) EditorGUILayout.LabelField("No release builds in Build/Release.");
                foreach (var b in builds) EditorGUILayout.LabelField(b.Platform, $"{b.Version ?? "no version found"}  ({b.Files[0].full})");
                if (msix != null) EditorGUILayout.LabelField("UWP", msix);
                string guess = builds.Select(b => b.Version).FirstOrDefault(v => v != null);
                tagField = EditorGUILayout.TextField(new GUIContent("Release tag", "Empty: the builds' version date"), tagField);
                if (string.IsNullOrWhiteSpace(tagField) && guess != null) EditorGUILayout.LabelField(" ", "will be " + PatchPublish.TagOf(guess));
                int history = Directory.Exists(HistoryDir) ? Directory.GetFiles(HistoryDir, "*.json").Length : 0;
                EditorGUILayout.LabelField("Earlier releases", $"{history} manifests in {HistoryDir} (fetched from GitHub on Publish)");
                EditorGUILayout.Space();
                if (GUILayout.Button("Publish", GUILayout.Height(28))) StartAll(tagField);
            }
            if (Busy) EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 20), progress, status);
            if (!string.IsNullOrEmpty(LastResult)) EditorGUILayout.HelpBox(LastResult, MessageType.Info);
        }
    }
}
