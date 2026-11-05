// PatchManifest.cs
// Remake-only (the in-game updater, CLAUDE.md "Update patcher" in docs/notes/platforms.md): what one platform's build of
// one version is made of. Every file is a list of chunks (PatchChunker). The chunks live in pack files attached to the
// GitHub releases, one per release and platform (packBase + packs[chunk.pack], a pack being "<tag>/GoF2Remake-<platform>-
// <tag>.pack" under ReleaseDownloads), each holding the chunks that release added (raw deflate, or stored when that's no
// smaller) one after another; a chunk is read with an HTTP range request at its offset (GitHub's download link redirects
// to its file host, which answers ranges with 206), neighbouring chunks in one request. The manifest itself is attached
// to the release too (GoF2Remake-<platform>-<tag>.patch.json), and every chunk is checked against its hash.
// Windows / Linux: the build's files. Android: one file, the APK (ApkName), rebuilt whole and handed to the installer.
// Written by PatchPublish, read by PatchInstall (JsonUtility on both sides).

using System;

namespace GoF2Remake.UI
{
    [Serializable]
    public class PatchManifest
    {
        public const int CurrentFormat = 2;
        /// <summary>The Android manifest's one file.</summary>
        public const string ApkName = "GoF2Remake.apk";
        /// <summary>The GitHub repository the releases (and their packs) are in.</summary>
        public const string Repository = "JoppieToppie/Galaxy-on-Fire-2-Unity-Remake";
        /// <summary>A release file is at ReleaseDownloads + "<tag>/<name>".</summary>
        public const string ReleaseDownloads = "https://github.com/" + Repository + "/releases/download/";

        /// <summary>The pack a release adds for a platform: its name among the release's files, and its reference
        /// ("<tag>/<name>", under ReleaseDownloads).</summary>
        public static string PackName(string platform, string tag) => $"GoF2Remake-{platform}-{tag}.pack";
        public static string PackRef(string platform, string tag) => tag + "/" + PackName(platform, tag);

        public int format = CurrentFormat;
        /// <summary>The build's version (Application.version, yyyy.MM.dd.HHmm).</summary>
        public string version;
        /// <summary>"Windows", "Linux" or "Android", as in the release files' names.</summary>
        public string platform;
        /// <summary>Where the pack files are: a URL ending in '/' (ReleaseDownloads).</summary>
        public string packBase = ReleaseDownloads;
        /// <summary>The pack files, "<tag>/<name>" under packBase (PatchChunk.pack indexes them).</summary>
        public string[] packs;
        public PatchChunk[] chunks;
        public PatchFile[] files;
        /// <summary>Files earlier versions shipped that this one no longer has (deleted on applying).</summary>
        public string[] removed;

        /// <summary>The whole build's size, unpacked.</summary>
        public long TotalSize
        {
            get
            {
                long total = 0;
                if (files != null) foreach (var f in files) total += f.size;
                return total;
            }
        }

        public string PackUrl(int pack) => packBase + packs[pack];

        /// <summary>What's wrong with it for this game, or null (a format this game can't read, a path outside the game's
        /// folder, a bad chunk id, pack or index).</summary>
        public string Problem(string expectedPlatform)
        {
            if (format != CurrentFormat) return "format " + format;
            if (string.IsNullOrEmpty(version)) return "no version";
            if (expectedPlatform != null && platform != expectedPlatform) return "platform " + platform;
            if (string.IsNullOrEmpty(packBase) || !packBase.EndsWith("/") || !(packBase.StartsWith("https://") || packBase.StartsWith("http://")))
                return "pack server";
            if (packs == null || chunks == null || files == null || files.Length == 0) return "empty";
            foreach (var p in packs)
                if (!IsSafePackRef(p)) return "pack " + p;
            foreach (var c in chunks)
                if (c == null || !PatchChunker.IsId(c.id) || c.size <= 0 || c.size > PatchChunker.MaxSize || c.packed < 0
                    || c.pack < 0 || c.pack >= packs.Length || c.offset < 0)
                    return "chunk " + c?.id;
            foreach (var f in files)
            {
                if (f == null || !IsSafePath(f.path) || f.chunks == null || !PatchChunker.ValidBits(f.Bits)) return "file " + f?.path;
                long sum = 0;
                foreach (int i in f.chunks)
                {
                    if (i < 0 || i >= chunks.Length) return "file " + f.path;
                    sum += chunks[i].size;
                }
                if (sum != f.size) return "file size " + f.path;
            }
            if (removed != null)
                foreach (var r in removed)
                    if (!IsSafePath(r)) return "removed " + r;
            return null;
        }

        /// <summary>A pack reference: "<tag>/<name>", both safe names.</summary>
        public static bool IsSafePackRef(string pack)
        {
            int slash = pack?.IndexOf('/') ?? -1;
            return slash > 0 && IsSafeName(pack.Substring(0, slash)) && IsSafeName(pack.Substring(slash + 1));
        }

        /// <summary>A file name for the server: letters, digits, '.', '-', '_' only, not starting with '.'.</summary>
        public static bool IsSafeName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 120 || name[0] == '.') return false;
            foreach (char c in name)
                if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '.' || c == '-' || c == '_')) return false;
            return true;
        }

        /// <summary>A path inside the game's folder: relative, '/'-separated, no "..", no drive, not the update folder.</summary>
        public static bool IsSafePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > 400) return false;
            if (path.StartsWith("/") || path.Contains("\\") || path.Contains(":") || path.Contains("\0")) return false;
            foreach (var part in path.Split('/'))
                if (part.Length == 0 || part == "." || part == "..") return false;
            if (path.Split('/')[0].Equals(PatchInstall.FolderName, StringComparison.OrdinalIgnoreCase)) return false;
            foreach (char c in path)
                if (c < 32 || c == '"' || c == '%' || c == '!' || c == '`' || c == '$' || c == '&' || c == '|' || c == '<' || c == '>' || c == '^')
                    return false;   // the apply scripts quote paths; nothing a shell would read
            return true;
        }
    }

    [Serializable]
    public class PatchChunk
    {
        /// <summary>PatchChunker.Id of the unpacked bytes.</summary>
        public string id;
        /// <summary>Unpacked length.</summary>
        public int size;
        /// <summary>Packed (raw deflate) length in its pack; 0 = stored unpacked.</summary>
        public int packed;
        /// <summary>Its pack file (PatchManifest.packs) and where in it it starts.</summary>
        public int pack;
        public long offset;

        public int DownloadSize => packed > 0 ? packed : size;
    }

    [Serializable]
    public class PatchFile
    {
        /// <summary>Relative to the game's folder, '/'-separated (GoF2Remake_Data/resources.assets.resS).</summary>
        public string path;
        public long size;
        /// <summary>Executable (Linux: the player, .so libraries).</summary>
        public bool exec;
        /// <summary>Indexes into PatchManifest.chunks, in order.</summary>
        public int[] chunks;
        /// <summary>The chunk size it is cut at (PatchChunker bits); 0 = PatchChunker.DefaultBits.</summary>
        public int bits;

        public int Bits => bits == 0 ? PatchChunker.DefaultBits : bits;
    }

    /// <summary>One download: a stretch of a pack holding the chunks in 'chunks' (and maybe a little in between).</summary>
    public sealed class PatchRange
    {
        public int pack;
        public long start, end;   // [start, end)
        public readonly System.Collections.Generic.List<int> chunks = new System.Collections.Generic.List<int>();
        public long Length => end - start;
    }
}
