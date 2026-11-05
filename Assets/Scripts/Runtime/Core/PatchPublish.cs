// PatchPublish.cs
// Remake-only (the in-game updater, CLAUDE.md "Update patcher" in docs/notes/platforms.md), the release side: cuts a
// Windows or Linux release build, or the Android APK, into chunks (PatchChunker), writes the chunks no earlier release
// published into one new pack file beside the release's zips (GoF2Remake-<platform>-<tag>.pack: raw deflate, kept unpacked
// when that's no smaller, one after another) and writes the build's PatchManifest (GoF2Remake-<platform>-<tag>.patch.json)
// there too; both go on the GitHub release, where the game finds them. A copy of every manifest stays in the history
// folder: later releases take each published chunk's pack and offset from it (a chunk is published once for every
// platform and release) and the files earlier builds had ('removed'). The Editor's PatchPublisherWindow fills the history
// from the earlier releases' .patch.json files on GitHub first, so it survives a cleared Build folder.
// Plain C# so the tests run it; the Editor's PatchPublisherWindow (GoF2 > Build > Update Patch) runs it on a thread.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GoF2Remake.UI
{
    public static class PatchPublish
    {
        /// <summary>A release build: the platform's player beside its _Data folder, or an APK.</summary>
        public sealed class Build
        {
            public string Dir, Platform, Exe, Version;
            /// <summary>Its files: manifest path, file on disk.</summary>
            public List<(string rel, string full)> Files;
            public bool Linux => Platform == "Linux";
        }

        public sealed class Result
        {
            public string ManifestPath, PackPath;
            public PatchManifest Manifest;
            public int NewChunks;
            public long NewBytes, TotalBytes;
            public override string ToString() =>
                $"{Manifest.platform} {Manifest.version}: {Manifest.files.Length} files, {Manifest.chunks.Length} chunks, " +
                $"{NewChunks} new ({NewBytes / 1048576.0:0.0} MB packed of {TotalBytes / 1048576.0:0} MB) -> {ManifestPath}";
        }

        static readonly Regex VersionPattern = new Regex(@"20\d\d\.\d\d\.\d\d\.\d{4}", RegexOptions.CultureInvariant);

        static string FindVersion(byte[] bytes)
        {
            var chars = new char[bytes.Length];
            for (int i = 0; i < bytes.Length; i++) chars[i] = (char)bytes[i];
            var m = VersionPattern.Match(new string(chars));
            return m.Success ? m.Value : null;
        }

        /// <summary>What 'path' is: a folder holding a Windows / Linux player beside its _Data folder, or an Android APK; its
        /// platform, player and version (from globalgamemanagers, where Unity keeps Application.version). Null when neither.</summary>
        public static Build Inspect(string path)
        {
            if (File.Exists(path) && path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            {
                string version = null;
                using (var zip = new ZipArchive(File.OpenRead(path), ZipArchiveMode.Read))
                {
                    var entry = zip.GetEntry("assets/bin/Data/globalgamemanagers");
                    if (entry != null)
                        using (var s = entry.Open())
                        {
                            var ms = new MemoryStream();
                            s.CopyTo(ms);
                            version = FindVersion(ms.ToArray());
                        }
                }
                string full = Path.GetFullPath(path);
                return new Build
                {
                    Dir = Path.GetDirectoryName(full), Platform = "Android", Version = version,
                    Files = new List<(string, string)> { (PatchManifest.ApkName, full) },
                };
            }
            if (!Directory.Exists(path)) return null;
            foreach (var data in Directory.GetDirectories(path, "*_Data"))
            {
                string name = Path.GetFileName(data);
                name = name.Substring(0, name.Length - "_Data".Length);
                string platform = File.Exists(Path.Combine(path, name + ".exe")) ? "Windows"
                    : File.Exists(Path.Combine(path, name + ".x86_64")) ? "Linux" : null;
                if (platform == null) continue;
                string ggm = Path.Combine(data, "globalgamemanagers");
                string dir = Path.GetFullPath(path);
                return new Build
                {
                    Dir = dir, Platform = platform, Exe = name + (platform == "Windows" ? ".exe" : ".x86_64"),
                    Version = File.Exists(ggm) ? FindVersion(File.ReadAllBytes(ggm)) : null,
                    Files = ShippedFiles(dir).Select(rel => (rel, Path.Combine(dir, rel))).ToList(),
                };
            }
            return null;
        }

        /// <summary>The files a release ships: everything but IL2CPP's and Burst's debug folders.</summary>
        public static List<string> ShippedFiles(string dir)
        {
            var list = new List<string>();
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(dir, f).Replace('\\', '/');
                if (rel.Contains("DontShip") || rel.Contains("DoNotShip")) continue;
                if (rel.Split('/')[0] == PatchInstall.FolderName) continue;
                list.Add(rel);
            }
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        static bool Executable(string rel, bool linux)
        {
            if (!linux) return false;
            string name = rel.Substring(rel.LastIndexOf('/') + 1);
            return name.EndsWith(".x86_64") || name.EndsWith(".so") || name.EndsWith(".sh") || !name.Contains('.');
        }

        /// <summary>The history's manifests (all platforms and releases).</summary>
        static IEnumerable<(string file, PatchManifest manifest)> History(string history)
        {
            foreach (var file in Directory.GetFiles(history, "*.json"))
            {
                PatchManifest m;
                try { m = UnityEngine.JsonUtility.FromJson<PatchManifest>(File.ReadAllText(file)); }
                catch (Exception) { continue; }
                if (m?.chunks == null || m.packs == null || m.format != PatchManifest.CurrentFormat) continue;
                yield return (file, m);
            }
        }

        /// <summary>Every chunk earlier publishes put in a pack: id -> pack reference, offset, packed size. Chunks in
        /// 'newPack' are left out: publishing a release's platform again writes that pack anew.</summary>
        static Dictionary<string, (string pack, long offset, int packed)> Published(string history, string newPack)
        {
            var known = new Dictionary<string, (string, long, int)>();
            foreach (var (_, m) in History(history))
                foreach (var c in m.chunks)
                    if (c != null && c.pack >= 0 && c.pack < m.packs.Length && m.packs[c.pack] != newPack && !known.ContainsKey(c.id))
                        known[c.id] = (m.packs[c.pack], c.offset, c.packed);
            return known;
        }

        /// <summary>The release tag of a build version (its date): yyyy.MM.dd.</summary>
        public static string TagOf(string version) => version.Substring(0, 10);

        /// <summary>Publishes 'build' as release 'tag' (null: the version's date): its pack and manifest into 'outDir', the
        /// manifest also into 'history'. 'packBase': where the packs are read (GitHub's release downloads).</summary>
        public static Result Run(Build build, string history, string outDir, string tag = null, string packBase = PatchManifest.ReleaseDownloads,
            Action<string, float> progress = null, CancellationToken cancel = default)
        {
            if (build?.Version == null) throw new InvalidOperationException("no build version found in " + build?.Dir);
            tag ??= TagOf(build.Version);
            if (!PatchManifest.IsSafeName(tag)) throw new ArgumentException("not a release tag: " + tag);
            if (!packBase.EndsWith("/")) packBase += "/";
            Directory.CreateDirectory(history);
            Directory.CreateDirectory(outDir);
            string newPack = PatchManifest.PackRef(build.Platform, tag);
            var known = Published(history, newPack);

            long total = build.Files.Sum(f => new FileInfo(f.full).Length), done = 0;
            var chunks = new List<PatchChunk>();
            var packs = new List<string>();
            var packIndex = new Dictionary<string, int>();
            int PackIndex(string name)
            {
                if (!packIndex.TryGetValue(name, out int p)) { packIndex[name] = p = packs.Count; packs.Add(name); }
                return p;
            }
            var index = new Dictionary<string, int>();
            var manifestFiles = new List<PatchFile>();
            // Chunks no pack has yet: where to read them.
            var missing = new List<(int index, string file, long offset, int size)>();
            using (var sha = SHA256.Create())
                foreach (var (rel, full) in build.Files)
                {
                    var ids = new List<int>();
                    int bits = PatchChunker.BitsFor(rel);
                    long before = done;
                    using (var s = File.OpenRead(full))
                        PatchChunker.Split(s, (offset, buffer, start, length) =>
                        {
                            string id = PatchChunker.Id(sha, buffer, start, length);
                            if (!index.TryGetValue(id, out int i))
                            {
                                index[id] = i = chunks.Count;
                                var c = new PatchChunk { id = id, size = length };
                                if (known.TryGetValue(id, out var k)) { c.pack = PackIndex(k.pack); c.offset = k.offset; c.packed = k.packed; }
                                else missing.Add((i, full, offset, length));
                                chunks.Add(c);
                            }
                            ids.Add(i);
                        }, read => progress?.Invoke("Cutting " + rel, 0.4f * (before + read) / Math.Max(1, total)), cancel, bits);
                    long size = new FileInfo(full).Length;
                    done += size;
                    manifestFiles.Add(new PatchFile
                    {
                        path = rel, size = size, exec = Executable(rel, build.Linux), chunks = ids.ToArray(),
                        bits = bits == PatchChunker.DefaultBits ? 0 : bits,
                    });
                }

            // The new chunks, packed on all cores a batch at a time, appended in order.
            long newBytes = 0;
            string packPath = null;
            if (missing.Count > 0)
            {
                packPath = Path.Combine(outDir, PatchManifest.PackName(build.Platform, tag));
                int p = PackIndex(newPack);
                string temp = packPath + ".part";
                using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                {
                    const int Batch = 64;
                    var packed = new byte[Batch][];
                    for (int b = 0; b < missing.Count; b += Batch)
                    {
                        int n = Math.Min(Batch, missing.Count - b);
                        Parallel.For(0, n, new ParallelOptions { CancellationToken = cancel }, j =>
                        {
                            var m = missing[b + j];
                            var raw = new byte[m.size];
                            using (var s = File.OpenRead(m.file))
                            {
                                s.Position = m.offset;
                                int got = 0;
                                while (got < m.size)
                                {
                                    int read = s.Read(raw, got, m.size - got);
                                    if (read <= 0) throw new IOException(m.file + " changed while publishing");
                                    got += read;
                                }
                            }
                            var deflated = PatchInstall.Deflate(raw, 0, m.size);
                            packed[j] = deflated.Length < m.size ? deflated : raw;
                        });
                        for (int j = 0; j < n; j++)
                        {
                            var c = chunks[missing[b + j].index];
                            c.pack = p;
                            c.offset = output.Position;
                            c.packed = packed[j].Length < c.size ? packed[j].Length : 0;
                            output.Write(packed[j], 0, packed[j].Length);
                            newBytes += packed[j].Length;
                            packed[j] = null;
                        }
                        progress?.Invoke("Packing new chunks", 0.4f + 0.6f * (b + n) / missing.Count);
                    }
                }
                if (File.Exists(packPath)) File.Delete(packPath);
                File.Move(temp, packPath);
            }

            // Earlier versions' files that this one no longer has.
            var current = new HashSet<string>(build.Files.Select(f => f.rel));
            var removed = new SortedSet<string>(StringComparer.Ordinal);
            var superseded = new List<string>();
            foreach (var (file, m) in History(history))
            {
                if (m.files == null || m.platform != build.Platform) continue;
                // An earlier publish of this release and platform (a rebuild): its pack is the one just written anew.
                if (m.packs.Contains(newPack) && m.version != build.Version) superseded.Add(file);
                if (m.version == build.Version) continue;
                foreach (var f in m.files) if (!current.Contains(f.path)) removed.Add(f.path);
                if (m.removed != null) foreach (var r in m.removed) if (!current.Contains(r)) removed.Add(r);
            }

            var manifest = new PatchManifest
            {
                version = build.Version,
                platform = build.Platform,
                packBase = packBase,
                packs = packs.ToArray(),
                chunks = chunks.ToArray(),
                files = manifestFiles.ToArray(),
                removed = removed.ToArray(),
            };
            string problem = manifest.Problem(build.Platform);
            if (problem != null) throw new InvalidOperationException("the manifest would be refused: " + problem);
            string json = UnityEngine.JsonUtility.ToJson(manifest);
            foreach (var file in superseded) File.Delete(file);
            File.WriteAllText(Path.Combine(history, HistoryName(build.Platform, build.Version)), json);
            string outPath = Path.Combine(outDir, ManifestAssetName(build.Platform, tag));
            File.WriteAllText(outPath, json);
            progress?.Invoke("Done", 1f);
            return new Result
            {
                ManifestPath = outPath, PackPath = packPath, Manifest = manifest, NewChunks = missing.Count, NewBytes = newBytes,
                TotalBytes = total,
            };
        }

        /// <summary>The manifest's name among the GitHub release's files (what GamePatcher looks for).</summary>
        public static string ManifestAssetName(string platform, string tag) => $"GoF2Remake-{platform}-{tag}.patch.json";

        /// <summary>A manifest's name in the history folder.</summary>
        public static string HistoryName(string platform, string version) => $"{platform}-{version}.json";

        /// <summary>Adds a release's manifest (as downloaded from GitHub) to the history, named by its own platform and
        /// version. False when it isn't one.</summary>
        public static bool AddToHistory(string history, string json)
        {
            PatchManifest m;
            try { m = UnityEngine.JsonUtility.FromJson<PatchManifest>(json); }
            catch (Exception) { return false; }
            if (m == null || m.Problem(null) != null) return false;
            Directory.CreateDirectory(history);
            File.WriteAllText(Path.Combine(history, HistoryName(m.platform, m.version)), json);
            return true;
        }

        // ---- UWP --------------------------------------------------------------------------------------------------------
        // A packaged app can't patch itself: Windows' App Installer updates it from an .appinstaller file that names the
        // package's web address, and downloads only the package's changed blocks (its block map). PublishUwp copies the
        // release's .msix beside the zips as GoF2Remake-UWP-<tag>.msix and writes GoF2Remake-UWP.appinstaller; both go on
        // the GitHub release. The .appinstaller's own address is the latest release's (releases/latest/download/...), so
        // Windows' own update check (OnLaunch) always finds the newest; the package's is this release's.

        public const string AppInstallerNamespace = "http://schemas.microsoft.com/appx/appinstaller/2018";

        public sealed class UwpResult
        {
            public string AppInstallerPath, PackagePath, Version, Tag;
            public override string ToString() => $"UWP {Version}: {Path.GetFileName(PackagePath)} + {AppInstallerPath}";
        }

        /// <summary>The package's identity (AppxManifest.xml inside the .msix): name, publisher, version, architecture.</summary>
        public static (string name, string publisher, string version, string arch) MsixIdentity(string msix)
        {
            using var zip = new ZipArchive(File.OpenRead(msix), ZipArchiveMode.Read);
            var entry = zip.GetEntry("AppxManifest.xml") ?? throw new InvalidDataException("no AppxManifest.xml in " + msix);
            System.Xml.Linq.XDocument doc;
            using (var s = entry.Open()) doc = System.Xml.Linq.XDocument.Load(s);
            var identity = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Identity")
                ?? throw new InvalidDataException("no Identity in " + msix);
            string Attr(string n) => identity.Attribute(n)?.Value;
            return (Attr("Name"), Attr("Publisher"), Attr("Version"), Attr("ProcessorArchitecture") ?? "x64");
        }

        /// <summary>yyyy.M.d.HHmm (a package version) -> the release tag yyyy.MM.dd.</summary>
        public static string TagOfPackageVersion(string version)
        {
            var p = version.Split('.');
            return p.Length >= 3 ? $"{int.Parse(p[0]):0000}.{int.Parse(p[1]):00}.{int.Parse(p[2]):00}" : version;
        }

        /// <summary>The .appinstaller's name among the release's files (no tag: the latest release's is always at the same
        /// address).</summary>
        public const string AppInstallerName = "GoF2Remake-UWP.appinstaller";

        public static string AppInstallerXml(string selfUrl, string packageUrl, (string name, string publisher, string version, string arch) id)
        {
            System.Xml.Linq.XNamespace ns = AppInstallerNamespace;
            var doc = new System.Xml.Linq.XDocument(new System.Xml.Linq.XDeclaration("1.0", "utf-8", null),
                new System.Xml.Linq.XElement(ns + "AppInstaller",
                    new System.Xml.Linq.XAttribute("Version", id.version),
                    new System.Xml.Linq.XAttribute("Uri", selfUrl),
                    new System.Xml.Linq.XElement(ns + "MainPackage",
                        new System.Xml.Linq.XAttribute("Name", id.name),
                        new System.Xml.Linq.XAttribute("Publisher", id.publisher),
                        new System.Xml.Linq.XAttribute("Version", id.version),
                        new System.Xml.Linq.XAttribute("ProcessorArchitecture", id.arch),
                        new System.Xml.Linq.XAttribute("Uri", packageUrl)),
                    new System.Xml.Linq.XElement(ns + "UpdateSettings",
                        new System.Xml.Linq.XElement(ns + "OnLaunch", new System.Xml.Linq.XAttribute("HoursBetweenUpdateChecks", "24")))));
            var sw = new Utf8StringWriter();
            doc.Save(sw);
            return sw.ToString();
        }

        sealed class Utf8StringWriter : StringWriter
        {
            public override System.Text.Encoding Encoding => new System.Text.UTF8Encoding(false);
        }

        /// <summary>Copies the UWP package 'msix' into 'outDir' as GoF2Remake-UWP-<tag>.msix (tag null: the package version's
        /// date) and writes GoF2Remake-UWP.appinstaller beside it, for the GitHub release.</summary>
        public static UwpResult PublishUwp(string msix, string outDir, string tag = null)
        {
            var id = MsixIdentity(msix);
            if (string.IsNullOrEmpty(id.name) || string.IsNullOrEmpty(id.publisher) || string.IsNullOrEmpty(id.version))
                throw new InvalidDataException("incomplete package identity in " + msix);
            tag ??= TagOfPackageVersion(id.version);
            if (!PatchManifest.IsSafeName(tag)) throw new ArgumentException("not a release tag: " + tag);
            Directory.CreateDirectory(outDir);
            string packageFile = $"GoF2Remake-UWP-{tag}{Path.GetExtension(msix)}";
            string target = Path.Combine(outDir, packageFile);
            if (!string.Equals(Path.GetFullPath(msix), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(msix, target + ".part", true);
                if (File.Exists(target)) File.Delete(target);
                File.Move(target + ".part", target);
            }
            string xml = AppInstallerXml($"https://github.com/{PatchManifest.Repository}/releases/latest/download/{AppInstallerName}",
                PatchManifest.ReleaseDownloads + tag + "/" + packageFile, id);
            string outPath = Path.Combine(outDir, AppInstallerName);
            File.WriteAllText(outPath, xml);
            return new UwpResult { AppInstallerPath = outPath, PackagePath = target, Version = id.version, Tag = tag };
        }
    }
}
