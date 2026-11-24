// ModCatalog.cs
// Remake-only (2026-11-19, players asked to browse and download mods in the game; Discord "Mod browser browsing"): the
// community mod catalog, github.com/JoppieToppie/gof2r-mods. Its catalog.json (written by that repository's workflows from
// one submission file per mod) lists each mod with its texts, author, version, size, SHA-256, views and downloads, and
// links into that repository's releases: <id>.zip (the mod), <id>-thumb.jpg (the list's picture) and <id>-preview.jpg
// (the detail's picture). GitHub counts every fetch of a release asset; the catalog's Stats workflow sums them, so
// fetching the zip is a download and fetching the preview a view (once per run and mod: the image stays in memory).
// Nothing here goes through GitHub's API (no rate limit): catalog.json from raw.githubusercontent.com, the files from the
// release links. Only links into those releases are followed (Trusted), and a download must match the catalog's size and
// SHA-256 before the Mod Browser (ModBrowserOnline) hands it to ModImport like an imported file.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModCatalog
    {
        public const string Repository = "JoppieToppie/gof2r-mods";
        /// <summary>The catalog's page: how to submit a mod.</summary>
        public const string Page = "https://github.com/" + Repository;
        public const string CatalogUrl = "https://raw.githubusercontent.com/" + Repository + "/main/catalog.json";
        /// <summary>Every file the game fetches for a mod comes from the catalog's own releases.</summary>
        public const string ReleasePrefix = "https://github.com/" + Repository + "/releases/download/";
        public const int Format = 1;
        const int CatalogTimeout = 15, ImageTimeout = 20, MaxImagesAtOnce = 4;

        /// <summary>One mod of the catalog.</summary>
        public class Entry
        {
            public string id, version, author, owner, website, sha256, published, updated, download, preview, thumb;
            public Dictionary<string, string> name, description, credits;
            public List<string> dependencies = new List<string>();
            /// <summary>The lowest version a dependency must have (by id), as mod.json's "id>=version".</summary>
            public Dictionary<string, string> dependencyVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public bool scripts;
            public long size, views, downloads;
            public int files;
            public string Name => ModManifest.Pick(name) ?? id;
            public string Description => ModManifest.Pick(description) ?? "";
            public string Credits => ModManifest.Pick(credits) ?? "";
        }

        public enum State { None, Loading, Ready, Failed }

        public static State Status { get; private set; }
        /// <summary>Why the last fetch failed (State.Failed).</summary>
        public static string Error { get; private set; }
        public static IReadOnlyList<Entry> Mods { get; private set; } = Array.Empty<Entry>();
        /// <summary>When the catalog was last written (its "updated"), null when unknown.</summary>
        public static string Updated { get; private set; }
        /// <summary>The catalog came in or failed (main thread).</summary>
        public static event Action Changed;

        static readonly Dictionary<string, Texture2D> images = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, List<Action<Texture2D>>> waiting = new Dictionary<string, List<Action<Texture2D>>>();
        static readonly Queue<string> imageQueue = new Queue<string>();
        static int imagesLoading;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Status = State.None;
            Error = Updated = null;
            Mods = Array.Empty<Entry>();
            Changed = null;
            images.Clear();
            waiting.Clear();
            imageQueue.Clear();
            imagesLoading = 0;
            Current = null;
        }

        // ---- the catalog ----

        /// <summary>A link the game may fetch: one of the catalog's own release files.</summary>
        public static bool Trusted(string url) =>
            !string.IsNullOrEmpty(url) && url.StartsWith(ReleasePrefix, StringComparison.Ordinal) && url.IndexOf("..", StringComparison.Ordinal) < 0;

        /// <summary>catalog.json read: its mods (those with a valid id, version and trusted download), or null with the error.</summary>
        public static List<Entry> Parse(string json, out string error, out string updated)
        {
            error = updated = null;
            JObject root;
            try
            {
                using var reader = new JsonTextReader(new StringReader(json ?? "")) { DateParseHandling = DateParseHandling.None };
                root = JToken.ReadFrom(reader) as JObject;
            }
            catch (Exception e) { error = "unreadable catalog (" + e.Message + ")"; return null; }
            if (root == null) { error = "unreadable catalog"; return null; }
            int format = root.Value<int?>("format") ?? 0;
            if (format > Format) { error = "the catalog is newer than this game: update the game"; return null; }
            updated = ModJson.Str(root, "updated");
            var list = new List<Entry>();
            if (root["mods"] is JArray mods)
                foreach (var t in mods)
                {
                    if (!(t is JObject o)) continue;
                    var e = new Entry
                    {
                        id = ModJson.Str(o, "id"),
                        version = ModJson.Str(o, "version", "1.0"),
                        author = ModJson.Str(o, "author"),
                        owner = ModJson.Str(o, "owner"),
                        website = ModJson.Str(o, "website"),
                        sha256 = ModJson.Str(o, "sha256")?.ToLowerInvariant(),
                        published = ModJson.Str(o, "published"),
                        updated = ModJson.Str(o, "updated"),
                        download = ModJson.Str(o, "download"),
                        preview = ModJson.Str(o, "preview"),
                        thumb = ModJson.Str(o, "thumb"),
                        name = ModJson.Text(o, "name"),
                        description = ModJson.Text(o, "description"),
                        credits = ModJson.Text(o, "credits"),
                        dependencies = ModJson.Strings(o, "dependencies"),
                        scripts = o.Value<bool?>("scripts") ?? false,
                        size = o.Value<long?>("size") ?? 0,
                        files = o.Value<int?>("files") ?? 0,
                        views = Math.Max(0, o.Value<long?>("views") ?? 0),
                        downloads = Math.Max(0, o.Value<long?>("downloads") ?? 0),
                    };
                    if (!ModManifest.ValidId(e.id) || string.IsNullOrEmpty(e.version) || !Trusted(e.download)
                        || e.sha256 == null || e.sha256.Length != 64 || e.size <= 0) continue;
                    if (!Trusted(e.preview)) e.preview = null;
                    if (!Trusted(e.thumb)) e.thumb = null;
                    if (e.website != null && !e.website.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                        && !e.website.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) e.website = null;
                    for (int i = 0; i < e.dependencies.Count; i++)
                    {
                        string d = e.dependencies[i].Trim();
                        int ge = d.IndexOf(">=", StringComparison.Ordinal);
                        if (ge > 0) { e.dependencyVersions[d.Substring(0, ge).Trim()] = d.Substring(ge + 2).Trim(); d = d.Substring(0, ge).Trim(); }
                        e.dependencies[i] = d;
                    }
                    if (list.Exists(x => x.id == e.id)) continue;
                    list.Add(e);
                }
            return list;
        }

        /// <summary>Asks for catalog.json (once per run; 'again': the Refresh button). Changed is raised when it answers.</summary>
        public static void Fetch(bool again = false)
        {
            if (Status == State.Loading || Status == State.Ready && !again) return;
            Status = State.Loading;
            Error = null;
            UnityWebRequest request = null;
            try
            {
                // A query of the time: raw.githubusercontent.com caches a file for five minutes, Refresh wants the newest.
                request = UnityWebRequest.Get(again ? CatalogUrl + "?t=" + DateTime.UtcNow.Ticks : CatalogUrl);
                request.timeout = CatalogTimeout;
                var op = request.SendWebRequest();
                op.completed += _ => Fetched(request);
            }
            catch (Exception e)
            {
                request?.Dispose();
                Failed(e.Message);
            }
        }

        static void Fetched(UnityWebRequest request)
        {
            try
            {
                if (request.result != UnityWebRequest.Result.Success) { Failed(request.error); return; }
                var list = Parse(request.downloadHandler.text, out string error, out string updated);
                if (list == null) { Failed(error); return; }
                Mods = list;
                Updated = updated;
                Status = State.Ready;
                Debug.Log($"ModCatalog: {list.Count} mods");
                Changed?.Invoke();
            }
            finally { request.Dispose(); }
        }

        static void Failed(string why)
        {
            Status = State.Failed;
            Error = string.IsNullOrEmpty(why) ? "no answer" : why;
            Debug.Log("ModCatalog: " + Error);
            Changed?.Invoke();
        }

        public static Entry Find(string id)
        {
            foreach (var e in Mods) if (e.id == id) return e;
            return null;
        }

        // ---- installed or not ----

        public enum Installed { No, Same, Older, Newer }

        /// <summary>The installed version against the catalog's: not installed, the same, older (an update is out) or newer.</summary>
        public static Installed Compare(string catalogVersion, string installedVersion)
        {
            if (installedVersion == null) return Installed.No;
            bool atLeast = ModManifest.VersionAtLeast(installedVersion, catalogVersion), atMost = ModManifest.VersionAtLeast(catalogVersion, installedVersion);
            return atLeast && atMost ? Installed.Same : atLeast ? Installed.Newer : Installed.Older;
        }

        public static Installed StateOf(Entry e) => Compare(e.version, ModManager.Find(e.id)?.Version);

        /// <summary>The catalog's newer version of an installed mod (null: none, or the catalog isn't loaded).</summary>
        public static Entry UpdateFor(ModInfo mod) =>
            mod != null && Find(mod.Id) is Entry e && Compare(e.version, mod.Version) == Installed.Older ? e : null;

        /// <summary>What installing 'e' needs first: its dependencies (and theirs) that are missing or too old here and are in
        /// the catalog, dependencies first; 'notInCatalog' the ones it can't get.</summary>
        public static List<Entry> Needed(Entry e, out List<string> notInCatalog)
        {
            var result = new List<Entry>();
            var missing = new List<string>();
            var seen = new HashSet<string> { e.id };
            void Walk(Entry of)
            {
                foreach (string d in of.dependencies)
                {
                    if (!seen.Add(d)) continue;
                    var installed = ModManager.Find(d);
                    bool fine = installed != null && (!of.dependencyVersions.TryGetValue(d, out string need) || ModManifest.VersionAtLeast(installed.Version, need));
                    var c = Find(d);
                    if (c == null) { if (!fine) missing.Add(d); continue; }
                    Walk(c);
                    if (!fine) result.Add(c);
                }
            }
            Walk(e);
            notInCatalog = missing;
            return result;
        }

        // ---- images ----

        /// <summary>A catalog image (a thumbnail or a preview) as a texture, kept for the run; 'done' gets null on a failure.
        /// At most four load at once.</summary>
        public static void Image(string url, Action<Texture2D> done)
        {
            if (!Trusted(url)) { done?.Invoke(null); return; }
            if (images.TryGetValue(url, out var tex)) { done?.Invoke(tex); return; }
            if (waiting.TryGetValue(url, out var list)) { if (done != null) list.Add(done); return; }
            waiting[url] = new List<Action<Texture2D>>();
            if (done != null) waiting[url].Add(done);
            imageQueue.Enqueue(url);
            NextImage();
        }

        /// <summary>The image is in memory already (a preview shown before counts no new view).</summary>
        public static bool HasImage(string url) => url != null && images.ContainsKey(url);

        static void NextImage()
        {
            while (imagesLoading < MaxImagesAtOnce && imageQueue.Count > 0)
            {
                string url = imageQueue.Dequeue();
                imagesLoading++;
                var request = UnityWebRequest.Get(url);
                request.timeout = ImageTimeout;
                request.SendWebRequest().completed += _ =>
                {
                    imagesLoading--;
                    Texture2D tex = null;
                    try
                    {
                        if (request.result == UnityWebRequest.Result.Success)
                        {
                            tex = new Texture2D(2, 2, TextureFormat.RGB24, false) { name = "ModCatalog " + Path.GetFileName(url), wrapMode = TextureWrapMode.Clamp };
                            if (!tex.LoadImage(request.downloadHandler.data, true)) { UnityEngine.Object.Destroy(tex); tex = null; }
                        }
                    }
                    catch (Exception) { tex = null; }
                    finally { request.Dispose(); }
                    if (tex != null) images[url] = tex;   // a failure isn't kept: asked again next time
                    if (waiting.TryGetValue(url, out var callbacks))
                    {
                        waiting.Remove(url);
                        foreach (var cb in callbacks) { try { cb(tex); } catch (Exception e) { Debug.LogException(e); } }
                    }
                    NextImage();
                };
            }
        }

        // ---- downloads ----

        /// <summary>A mod's download: its progress, then the verified file (path) or why not (error).</summary>
        public class Download
        {
            public Entry entry;
            public string path, error;
            public volatile bool done;
            public bool cancelled, verifying;
            internal UnityWebRequest request;
            /// <summary>0..1 of the file (by the catalog's size: GitHub's redirect hides the length until the file starts).</summary>
            public float Progress => entry.size > 0 ? Mathf.Clamp01(Received / (float)entry.size) : 0f;
            public long Received => request != null ? (long)request.downloadedBytes : done || verifying ? entry.size : 0;
        }

        /// <summary>The download running now (one at a time), or the last one until the next starts.</summary>
        public static Download Current { get; private set; }
        public static bool Busy => Current != null && !Current.done;

        static string DownloadFolder => Path.Combine(Application.temporaryCachePath, "ModDownload");

        /// <summary>Downloads the mod's zip into the cache, then checks its size and SHA-256 (on a worker thread); null when
        /// another download runs. Poll the returned Download until done.</summary>
        public static Download Start(Entry e)
        {
            if (Busy || e == null || !Trusted(e.download)) return null;
            var d = new Download { entry = e };
            Current = d;
            try
            {
                Directory.CreateDirectory(DownloadFolder);
                string path = Path.Combine(DownloadFolder, e.id + ".zip");
                if (File.Exists(path)) File.Delete(path);
                var request = new UnityWebRequest(e.download, UnityWebRequest.kHttpVerbGET)
                {
                    downloadHandler = new DownloadHandlerFile(path) { removeFileOnAbort = true },
                    timeout = 0,   // a big mod on a slow line: no overall limit (Cancel stops it)
                };
                d.request = request;
                request.SendWebRequest().completed += _ =>
                {
                    bool ok = request.result == UnityWebRequest.Result.Success;
                    string why = request.error;
                    request.Dispose();
                    d.request = null;
                    if (d.cancelled) { Discard(path); d.done = true; return; }
                    if (!ok) { Discard(path); d.error = why; d.done = true; return; }
                    d.verifying = true;
                    Task.Run(() => Verify(path, e.size, e.sha256)).ContinueWith(t =>
                    {
                        // Back on the main thread through the poll: only plain fields are set here.
                        string bad = t.IsFaulted ? t.Exception?.GetBaseException().Message : t.Result;
                        if (bad != null) { Discard(path); d.error = bad; }
                        else d.path = path;
                        d.verifying = false;
                        System.Threading.Thread.MemoryBarrier();   // path / error before done
                        d.done = true;
                    });
                };
            }
            catch (Exception ex)
            {
                d.error = ex.Message;
                d.done = true;
            }
            return d;
        }

        /// <summary>Stops the running download (its file deleted).</summary>
        public static void Cancel()
        {
            var d = Current;
            if (d == null || d.done) return;
            d.cancelled = true;
            d.request?.Abort();
        }

        /// <summary>A downloaded file against the catalog: null when its size and SHA-256 match, else why not.</summary>
        public static string Verify(string path, long size, string sha256)
        {
            var info = new FileInfo(path);
            if (!info.Exists) return "the file is missing";
            if (size > 0 && info.Length != size) return $"the file is {info.Length} bytes, the catalog says {size}";
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            string hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            return string.Equals(hash, sha256, StringComparison.OrdinalIgnoreCase) ? null : "the file doesn't match the catalog (damaged or changed)";
        }

        static void Discard(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        // ---- numbers ----

        /// <summary>A count, short: 999, 1.2k, 12k, 1.2M.</summary>
        public static string Count(long n)
        {
            var c = CultureInfo.InvariantCulture;
            if (n < 1000) return Math.Max(0, n).ToString(c);
            if (n < 10000) return (Math.Floor(n / 100.0) / 10).ToString("0.#", c) + "k";
            if (n < 1000000) return (n / 1000).ToString(c) + "k";
            if (n < 10000000) return (Math.Floor(n / 100000.0) / 10).ToString("0.#", c) + "M";
            return (n / 1000000).ToString(c) + "M";
        }

        /// <summary>A file size: 512 B, 160 KB, 1.2 MB, 1.1 GB.</summary>
        public static string Size(long bytes)
        {
            var c = CultureInfo.InvariantCulture;
            if (bytes < 1024) return Math.Max(0, bytes) + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024) + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString(bytes < 10L * 1024 * 1024 ? "0.0" : "0", c) + " MB";
            return (bytes / (1024.0 * 1024 * 1024)).ToString("0.0", c) + " GB";
        }
    }
}
