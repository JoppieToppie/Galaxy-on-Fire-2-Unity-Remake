// UpdateCheck.cs
// Remake-only: on entering the main menu (after the title screen) the newest GitHub release is asked for once per run
// (api.github.com .../releases/latest, no token: 60 requests an hour per address are plenty for one per start). Release
// tags are the release's date, yyyy.MM.dd; a build's version also carries the time (BuildVersion, yyyy.MM.dd.HHmm), which
// is ignored: only a release from a later day than the build counts as an update. The main menu then shows an
// "Update available" button at the bottom centre that opens the release's page. The Editor's version is "editor", so
// nothing is checked there unless MainMenu.editorTestVersion pretends one. A failed request (offline, rate limit) is
// tried again the next time the menu is entered.
// The release's files (assets) are kept: on Windows / Linux the button updates in place when the release carries this
// platform's patch manifest (GamePatcher, CLAUDE.md "Update patcher"), else it opens the page.

using System;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class UpdateCheck
    {
        public const string Repository = PatchManifest.Repository;
        const string LatestApi = "https://api.github.com/repos/" + Repository + "/releases/latest";
        /// <summary>Where the button goes when the release didn't name its own page.</summary>
        public const string ReleasesPage = "https://github.com/" + Repository + "/releases/latest";
        const int TimeoutSeconds = 10;

        static readonly Regex DatePattern = new Regex(@"^\s*v?(\d{4})\.(\d{1,2})\.(\d{1,2})(?:\D|$)", RegexOptions.CultureInvariant);

        static bool running, done;

        /// <summary>A release from a later day than this build is out.</summary>
        public static bool Available { get; private set; }
        /// <summary>The newest release's tag (yyyy.MM.dd), once known.</summary>
        public static string LatestTag { get; private set; }
        /// <summary>The newest release's page.</summary>
        public static string ReleaseUrl { get; private set; }
        /// <summary>The answer came in (on the main thread).</summary>
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            running = done = false;
            Available = false;
            LatestTag = ReleaseUrl = null;
            assets = null;
            Changed = null;
        }

        [Serializable]
        class Release
        {
            public string tag_name;
            public string html_url;
            public bool draft;
            public bool prerelease;
            public Asset[] assets;
        }

        [Serializable]
        class Asset
        {
            public string name;
            public string browser_download_url;
            public long size;
        }

        static Asset[] assets;

        /// <summary>The newest release's file named 'name' (case-insensitive): its download URL and size, or null.</summary>
        public static (string url, long size)? ReleaseFile(string name)
        {
            if (assets == null || string.IsNullOrEmpty(name)) return null;
            foreach (var a in assets)
                if (a != null && string.Equals(a.name, name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(a.browser_download_url))
                    return (a.browser_download_url, a.size);
            return null;
        }

        /// <summary>"yyyy.MM.dd" with anything after it (the build's ".HHmm", a tag's suffix) as yyyyMMdd; false for
        /// "editor", "0.1.0" and the like.</summary>
        public static bool TryParseDate(string version, out int yyyymmdd)
        {
            yyyymmdd = 0;
            if (string.IsNullOrEmpty(version)) return false;
            var m = DatePattern.Match(version);
            if (!m.Success) return false;
            int y = int.Parse(m.Groups[1].Value), mo = int.Parse(m.Groups[2].Value), d = int.Parse(m.Groups[3].Value);
            if (mo < 1 || mo > 12 || d < 1 || d > 31) return false;
            yyyymmdd = y * 10000 + mo * 100 + d;
            return true;
        }

        /// <summary>The release 'tag' is from a later day than 'buildVersion' (both by year.month.day only).</summary>
        public static bool IsNewer(string tag, string buildVersion) =>
            TryParseDate(tag, out int released) && TryParseDate(buildVersion, out int built) && released > built;

        /// <summary>Asks GitHub for the newest release, once per run ('buildVersion': this build's version, BuildVersion.Text).</summary>
        public static void Start(string buildVersion)
        {
            if (running || done || !TryParseDate(buildVersion, out _)) return;
            running = true;
            UnityWebRequest request = null;
            try
            {
                request = UnityWebRequest.Get(LatestApi);
                request.timeout = TimeoutSeconds;
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.SetRequestHeader("X-GitHub-Api-Version", "2022-11-28");
                var op = request.SendWebRequest();
                op.completed += _ => Finish(request, buildVersion);
            }
            catch (Exception e)
            {
                running = false;
                request?.Dispose();
                Debug.Log("UpdateCheck: no check (" + e.Message + ")");
            }
        }

        static void Finish(UnityWebRequest request, string buildVersion)
        {
            running = false;
            try
            {
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.Log($"UpdateCheck: {request.responseCode} {request.error}");   // tried again on the next menu entry
                    return;
                }
                done = true;
                var release = JsonUtility.FromJson<Release>(request.downloadHandler.text);
                if (release == null || release.draft || release.prerelease || string.IsNullOrEmpty(release.tag_name)) return;
                LatestTag = release.tag_name.Trim();
                ReleaseUrl = string.IsNullOrEmpty(release.html_url) ? ReleasesPage : release.html_url;
                assets = release.assets;
                Available = IsNewer(LatestTag, buildVersion);
                if (Available) Debug.Log($"UpdateCheck: {LatestTag} is out (this build {buildVersion})");
                Changed?.Invoke();
            }
            catch (Exception e)
            {
                Debug.Log("UpdateCheck: unreadable answer (" + e.Message + ")");
            }
            finally
            {
                request.Dispose();
            }
        }
    }
}
