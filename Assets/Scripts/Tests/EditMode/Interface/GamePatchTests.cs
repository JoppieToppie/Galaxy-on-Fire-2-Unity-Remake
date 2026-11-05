// GamePatchTests.cs
// CLAUDE.md "Update patcher" (docs/notes/platforms.md): the chunker cuts by content (an insertion moves the cuts, every
// chunk away from it keeps its id), chunks stay within their limits, a manifest is checked before use, and PatchInstall
// turns an old build's folder into the new one from local chunks plus only the downloaded ones (staged in _update, applied
// by the script). Expected values by hand: the data is seeded random bytes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using GoF2Remake.UI;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class GamePatchTests : SandboxTest
    {
        static byte[] RandomBytes(int length, int seed)
        {
            var data = new byte[length];
            new Random(seed).NextBytes(data);
            return data;
        }

        static List<(long offset, int length, string id)> Chunks(byte[] data)
        {
            var list = new List<(long, int, string)>();
            using var sha = SHA256.Create();
            PatchChunker.Split(new MemoryStream(data), (o, b, s, l) => list.Add((o, l, PatchChunker.Id(sha, b, s, l))));
            return list;
        }

        [Test]
        public void ChunksCoverTheFileWithinTheirLimits()
        {
            var data = RandomBytes(9_000_000, 1);
            var chunks = Chunks(data);
            long next = 0;
            for (int i = 0; i < chunks.Count; i++)
            {
                Assert.AreEqual(next, chunks[i].offset);
                Assert.LessOrEqual(chunks[i].length, PatchChunker.MaxSize);
                if (i < chunks.Count - 1) Assert.GreaterOrEqual(chunks[i].length, PatchChunker.MinSize(PatchChunker.DefaultBits));
                next += chunks[i].length;
            }
            Assert.AreEqual(data.Length, next);
            Assert.That(chunks.Count, Is.InRange(6, 40), "about 512 KB each");
            Assert.AreEqual(chunks.Select(c => c.id), Chunks(data).Select(c => c.id), "the same bytes cut the same way");
        }

        [Test]
        public void AnInsertionKeepsTheOtherChunks()
        {
            var data = RandomBytes(12_000_000, 2);
            var changed = new byte[data.Length + 3000];
            Buffer.BlockCopy(data, 0, changed, 0, 6_000_000);
            new Random(3).NextBytes(new Span<byte>(changed, 6_000_000, 3000));
            Buffer.BlockCopy(data, 6_000_000, changed, 6_003_000, data.Length - 6_000_000);

            var before = new HashSet<string>(Chunks(data).Select(c => c.id));
            var after = Chunks(changed);
            long newBytes = after.Where(c => !before.Contains(c.id)).Sum(c => (long)c.length);
            Assert.Less(newBytes, 3 * PatchChunker.MaxSize, "only the chunks around the insertion are new");
            Assert.Greater(after.Count(c => before.Contains(c.id)), after.Count - 4);
        }

        [Test]
        public void SerializedFilesAreCutSmaller()
        {
            Assert.AreEqual(PatchChunker.SmallBits, PatchChunker.BitsFor("GoF2Remake_Data/resources.assets"));
            Assert.AreEqual(PatchChunker.SmallBits, PatchChunker.BitsFor("GoF2Remake_Data/sharedassets0.assets"));
            Assert.AreEqual(PatchChunker.SmallBits, PatchChunker.BitsFor("GoF2Remake_Data/globalgamemanagers"));
            Assert.AreEqual(PatchChunker.SmallBits, PatchChunker.BitsFor("GoF2Remake_Data/level3"));
            Assert.AreEqual(PatchChunker.DefaultBits, PatchChunker.BitsFor("GoF2Remake_Data/resources.assets.resS"));
            Assert.AreEqual(PatchChunker.DefaultBits, PatchChunker.BitsFor("GameAssembly.dll"));
            Assert.AreEqual(PatchChunker.MaxSize, PatchChunker.MaxSizeFor(PatchChunker.DefaultBits));

            var data = RandomBytes(2_000_000, 16);
            var list = new List<int>();
            PatchChunker.Split(new MemoryStream(data), (o, b, s, l) => list.Add(l), bits: PatchChunker.SmallBits);
            Assert.That(list.Count, Is.InRange(40, 300), "about 16 KB each");
            for (int i = 0; i < list.Count - 1; i++)
                Assert.That(list[i], Is.InRange(PatchChunker.MinSize(PatchChunker.SmallBits), PatchChunker.MaxSizeFor(PatchChunker.SmallBits)));
        }

        [Test]
        public void SmallAndEmptyFiles()
        {
            Assert.AreEqual(0, Chunks(new byte[0]).Count);
            var small = Chunks(RandomBytes(1000, 4));
            Assert.AreEqual(1, small.Count);
            Assert.AreEqual(1000, small[0].length);
        }

        // ---- helpers: a build folder, publishing it, reading a chunk out of its pack as the game's range download does ----

        /// <summary>A Windows / Linux build folder: the player, Game_Data/globalgamemanagers carrying 'version', 'files'.</summary>
        static void WriteBuild(string dir, string version, Dictionary<string, byte[]> files, bool linux = false)
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(Path.Combine(dir, "Game_Data"));
            var ggm = new List<byte>(RandomBytes(200, 12)) { 0, 0 };
            ggm.AddRange(System.Text.Encoding.ASCII.GetBytes(version));
            ggm.AddRange(new byte[] { 0, 7 });
            File.WriteAllBytes(Path.Combine(dir, "Game_Data", "globalgamemanagers"), ggm.ToArray());
            File.WriteAllBytes(Path.Combine(dir, linux ? "Game.x86_64" : "Game.exe"), RandomBytes(1000, 14));
            foreach (var kv in files)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dir, kv.Key)));
                File.WriteAllBytes(Path.Combine(dir, kv.Key), kv.Value);
            }
        }

        /// <summary>The publisher's history of earlier releases' manifests, and Build/Release (packs, manifests).</summary>
        string Store => Path.Combine(Sandbox, "History");
        string Out => Path.Combine(Sandbox, "Out");

        PatchPublish.Result Publish(string version, Dictionary<string, byte[]> files, bool linux = false)
        {
            string build = Path.Combine(Sandbox, "Build");
            WriteBuild(build, version, files, linux);
            return PatchPublish.Run(PatchPublish.Inspect(build), Store, Out, packBase: "https://example.invalid/releases/download/");
        }

        /// <summary>The bytes of 'range' from the store's pack (what the HTTP range request returns).</summary>
        byte[] Fetch(PatchManifest m, PatchRange range)
        {
            string pack = m.packs[range.pack];
            using var s = File.OpenRead(Path.Combine(Out, pack.Substring(pack.IndexOf('/') + 1)));
            s.Position = range.start;
            var data = new byte[range.Length];
            int n = 0;
            while (n < data.Length) n += s.Read(data, n, data.Length - n);
            return data;
        }

        /// <summary>Downloads every range of 'install' from the store and hands its chunks over (GamePatcher.Download).</summary>
        void DownloadAll(PatchInstall install)
        {
            foreach (var r in install.Ranges)
            {
                var data = Fetch(install.Manifest, r);
                foreach (int i in r.chunks)
                {
                    var c = install.Manifest.chunks[i];
                    Assert.IsTrue(install.Accept(i, data, (int)(c.offset - r.start), c.DownloadSize), c.id);
                }
            }
        }

        // ---- tests ----------------------------------------------------------------------------------------------------

        [Test]
        public void DownloadTexts()
        {
            // The download dialog's sizes and time left (MainMenuUpdate): a clock reads the same in every language.
            Assert.AreEqual("0:00", MainMenu.ClockText(-3f));
            Assert.AreEqual("0:43", MainMenu.ClockText(42.2f), "rounded up: never 0:00 while bytes are left");
            Assert.AreEqual("12:05", MainMenu.ClockText(725f));
            Assert.AreEqual("1:02:09", MainMenu.ClockText(3729f));
            Assert.AreEqual("0.5 MB", MainMenu.SizeText(512 * 1024));
            Assert.AreEqual("376 MB", MainMenu.SizeText(394_142_360));
            Assert.AreEqual("1.5 GB", MainMenu.SizeText(1_564_978_043));
        }

        [Test]
        public void ManifestChecks()
        {
            Assert.IsTrue(PatchManifest.IsSafePath("GoF2Remake_Data/resources.assets.resS"));
            Assert.IsTrue(PatchManifest.IsSafePath("GoF2Remake.exe"));
            foreach (var bad in new[] { "", "/etc/passwd", "../x", "a/../../x", "C:/x", "a\\b", "_update/x", "a//b", "a&b", "%TEMP%", "./x" })
                Assert.IsFalse(PatchManifest.IsSafePath(bad), bad);
            Assert.IsTrue(PatchManifest.IsSafeName("GoF2Remake-Windows-2026.11.05.pack"));
            Assert.IsTrue(PatchManifest.IsSafePackRef("2026.11.05/GoF2Remake-Windows-2026.11.05.pack"));
            foreach (var bad in new[] { "x.pack", "/x.pack", "a/", "a/b/c", "../x.pack", "a/.x" })
                Assert.IsFalse(PatchManifest.IsSafePackRef(bad), bad);
            Assert.AreEqual("2026.11.05/GoF2Remake-Android-2026.11.05.pack", PatchManifest.PackRef("Android", "2026.11.05"));
            Assert.AreEqual("https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/releases/download/", PatchManifest.ReleaseDownloads);
            foreach (var bad in new[] { "", "../x.pack", "a/b", ".hidden", "a b", "a?b" })
                Assert.IsFalse(PatchManifest.IsSafeName(bad), bad);
            Assert.IsTrue(PatchChunker.IsId("0123456789abcdef0123456789abcdef"));
            Assert.IsFalse(PatchChunker.IsId("0123456789ABCDEF0123456789abcdef"));
            Assert.IsFalse(PatchChunker.IsId("../../../../../../../../../../x"));

            var m = Publish("2026.11.05.1200", new Dictionary<string, byte[]> { ["a.bin"] = RandomBytes(300_000, 5) }).Manifest;
            Assert.IsNull(m.Problem("Windows"));
            Assert.AreEqual("platform Windows", m.Problem("Linux"));
            var f = m.files.First(x => x.path == "a.bin");
            f.size++;
            Assert.AreEqual("file size a.bin", m.Problem("Windows"));
            f.size--;
            f.chunks = new[] { 9999 };
            Assert.AreEqual("file a.bin", m.Problem("Windows"));
            f.chunks = new[] { 0 };
            m.chunks[0].pack = 5;
            StringAssert.StartsWith("chunk ", m.Problem("Windows"));
            m.chunks[0].pack = 0;
            m.packs[0] = "../../etc";
            StringAssert.StartsWith("pack ", m.Problem("Windows"));
            m.packs[0] = "t/x.pack";
            m.packBase = "ftp://nas/";
            Assert.AreEqual("pack server", m.Problem("Windows"));
        }

        [Test]
        public void RangesJoinNeighbours()
        {
            var m = new PatchManifest
            {
                packs = new[] { "a.pack", "b.pack" },
                chunks = new[]
                {
                    new PatchChunk { id = "0", size = 100, pack = 0, offset = 0 },
                    new PatchChunk { id = "1", size = 100, pack = 0, offset = 100 },
                    new PatchChunk { id = "2", size = 100, pack = 0, offset = 10_000 },        // a gap of 9,800
                    new PatchChunk { id = "3", size = 100, pack = 1, offset = 0 },             // another pack
                    new PatchChunk { id = "4", size = 50, packed = 40, pack = 0, offset = 200 },
                },
            };
            var ranges = PatchInstall.PlanRanges(m, new[] { 2, 3, 0, 4 }, maxGap: 1000, maxRange: 1_000_000);
            Assert.AreEqual(3, ranges.Count);
            Assert.AreEqual((0, 0L, 240L), (ranges[0].pack, ranges[0].start, ranges[0].end), "0 and 4 with chunk 1's bytes between");
            CollectionAssert.AreEqual(new[] { 0, 4 }, ranges[0].chunks);
            Assert.AreEqual((0, 10_000L, 10_100L), (ranges[1].pack, ranges[1].start, ranges[1].end));
            Assert.AreEqual((1, 0L, 100L), (ranges[2].pack, ranges[2].start, ranges[2].end));
            Assert.AreEqual(2, PatchInstall.PlanRanges(m, new[] { 0, 1 }, maxGap: 1000, maxRange: 150).Count, "a range stays under its limit");
            Assert.AreEqual(1, PatchInstall.PlanRanges(m, new[] { 0, 2 }, maxGap: 10_000, maxRange: 1_000_000).Count);
        }

        [Test]
        public void AnOldFolderBecomesTheNewBuild()
        {
            var big = RandomBytes(6_000_000, 6);
            var bigNew = new byte[big.Length + 70_000];
            Buffer.BlockCopy(big, 0, bigNew, 0, 2_000_000);
            new Random(7).NextBytes(new Span<byte>(bigNew, 2_000_000, 70_000));
            Buffer.BlockCopy(big, 2_000_000, bigNew, 2_070_000, big.Length - 2_000_000);
            var same = RandomBytes(500_000, 8);
            var text = new byte[400_000];   // packs well
            var oldFiles = new Dictionary<string, byte[]> { ["Game_Data/big.resS"] = big, ["same.dll"] = same, ["old.bin"] = new byte[10] };
            var newFiles = new Dictionary<string, byte[]> { ["Game_Data/big.resS"] = bigNew, ["same.dll"] = same, ["Game_Data/text.bin"] = text };
            var first = Publish("2026.11.02.0755", oldFiles);
            var second = Publish("2026.11.05.1200", newFiles);
            var m = second.Manifest;
            Assert.IsNull(m.Problem("Windows"));
            CollectionAssert.AreEqual(new[] { "old.bin" }, m.removed);
            CollectionAssert.AreEquivalent(new[] { "2026.11.02/GoF2Remake-Windows-2026.11.02.pack", "2026.11.05/GoF2Remake-Windows-2026.11.05.pack" }, m.packs,
                "unchanged chunks stay in the first release's pack");
            Assert.Less(second.NewBytes, 3 * PatchChunker.MaxSize + 20_000, "the second pack holds only the new chunks");

            string game = Path.Combine(Sandbox, "Game");
            WriteBuild(game, "2026.11.02.0755", oldFiles);
            File.WriteAllBytes(Path.Combine(game, "save.dat"), new byte[5]);   // not the build's: left alone

            var install = new PatchInstall(game, m);
            install.Scan(CancellationToken.None);
            CollectionAssert.AreEquivalent(new[] { "Game_Data/big.resS", "Game_Data/text.bin", "Game_Data/globalgamemanagers" },
                install.Changed.Select(f => f.path));
            CollectionAssert.AreEqual(new[] { "old.bin" }, install.Removing);
            Assert.Less(install.DownloadBytes, 3 * PatchChunker.MaxSize + 20_000, "the insertion's chunks and the packed zeros");
            Assert.IsTrue(install.Ranges.All(r => m.packs[r.pack] == "2026.11.05/GoF2Remake-Windows-2026.11.05.pack"), "all new: one pack");
            Assert.LessOrEqual(install.Ranges.Count, 2, "neighbours in one request");
            Assert.IsFalse(install.UpToDate);

            // A damaged download is refused; the right one is kept.
            Assert.IsFalse(install.Accept(install.Needed[0], new byte[] { 1, 2, 3 }));
            DownloadAll(install);

            install.Assemble(CancellationToken.None);
            CollectionAssert.AreEqual(bigNew, File.ReadAllBytes(install.StagedPath("Game_Data/big.resS")));
            CollectionAssert.AreEqual(text, File.ReadAllBytes(install.StagedPath("Game_Data/text.bin")));
            Assert.IsFalse(File.Exists(install.StagedPath("same.dll")), "unchanged files stay where they are");
            Assert.IsFalse(Directory.Exists(install.ChunkDir), "the chunks are dropped once assembled");

            string script = File.ReadAllText(install.WriteApplyScript(true, 4242, "Game.exe"));
            StringAssert.Contains("PID eq 4242", script);
            StringAssert.Contains("call :move \"files\\Game_Data\\big.resS\" \"..\\Game_Data\\big.resS\"", script);
            StringAssert.Contains("del /f /q \"..\\old.bin\"", script);
            StringAssert.Contains("start \"\" \"Game.exe\"", script);
            StringAssert.DoesNotContain("same.dll", script);
            StringAssert.DoesNotContain("save.dat", script);
            string sh = File.ReadAllText(install.WriteApplyScript(false, 4242, "Game.x86_64"));
            StringAssert.Contains("move \"files/Game_Data/text.bin\" \"../Game_Data/text.bin\"", sh);
            StringAssert.Contains("rm -f \"../old.bin\"", sh);

            // What the script does, here by hand: the folder is then the new build, and scanning again finds nothing to do.
            foreach (var f in install.Changed)
            {
                string to = Path.Combine(game, f.path);
                File.Delete(to);
                File.Move(install.StagedPath(f.path), to);
            }
            File.Delete(Path.Combine(game, "old.bin"));
            var again = new PatchInstall(game, m);
            again.Scan(CancellationToken.None);
            Assert.IsTrue(again.UpToDate);
            Assert.AreEqual(0, again.DownloadBytes);
        }

        [Test]
        public void AnInterruptedDownloadKeepsItsChunks()
        {
            string game = Path.Combine(Sandbox, "Game");
            Directory.CreateDirectory(game);
            var m = Publish("2026.11.05.1200", new Dictionary<string, byte[]> { ["a.bin"] = RandomBytes(3_000_000, 9) }).Manifest;
            var install = new PatchInstall(game, m);
            install.Scan(CancellationToken.None);
            int total = install.Needed.Count;
            Assert.Greater(total, 1);
            var r = install.Ranges[0];
            var data = Fetch(m, r);
            var c0 = m.chunks[r.chunks[0]];
            Assert.IsTrue(install.Accept(r.chunks[0], data, (int)(c0.offset - r.start), c0.DownloadSize));

            var resumed = new PatchInstall(game, m);
            resumed.Scan(CancellationToken.None);
            Assert.AreEqual(total - 1, resumed.Needed.Count, "the chunk downloaded before counts");

            // Another version's leftovers go, unless that version has them too (the player, the same in both).
            var other = Publish("2026.11.06.1200", new Dictionary<string, byte[]> { ["a.bin"] = RandomBytes(3_000_000, 10) }).Manifest;
            var next = new PatchInstall(game, other);
            next.Scan(CancellationToken.None);
            var otherIds = new HashSet<string>(other.chunks.Select(c => c.id));
            Assert.IsTrue(Directory.GetFiles(next.ChunkDir).All(f => otherIds.Contains(Path.GetFileName(f))));
            if (!otherIds.Contains(c0.id)) Assert.AreEqual(0, Directory.GetFiles(next.ChunkDir).Length);
        }

        [Test]
        public void PublishingAReleaseBuild()
        {
            var data = RandomBytes(2_500_000, 13);
            var first = Publish("2026.11.02.0755", new Dictionary<string, byte[]>
            {
                ["Game_Data/data.resS"] = data,
                ["Game_Data/level9"] = new byte[300_000],
                ["Game_BackUpThisFolder_ButDontShipItWithYourGame/debug.pdb"] = new byte[10],
            }, linux: true);
            var b = PatchPublish.Inspect(Path.Combine(Sandbox, "Build"));
            Assert.AreEqual("Linux", b.Platform);
            Assert.AreEqual("Game.x86_64", b.Exe);
            Assert.AreEqual("2026.11.02.0755", b.Version);
            Assert.AreEqual(Path.Combine(Out, "GoF2Remake-Linux-2026.11.02.patch.json"), first.ManifestPath);
            Assert.AreEqual(Path.Combine(Out, "GoF2Remake-Linux-2026.11.02.pack"), first.PackPath);
            Assert.IsTrue(File.Exists(Path.Combine(Store, "Linux-2026.11.02.0755.json")), "the history keeps a copy");

            var m = UnityEngine.JsonUtility.FromJson<PatchManifest>(File.ReadAllText(first.ManifestPath));
            Assert.IsNull(m.Problem("Linux"));
            Assert.AreEqual("https://example.invalid/releases/download/", m.packBase);
            CollectionAssert.AreEqual(new[] { "Game.x86_64", "Game_Data/data.resS", "Game_Data/globalgamemanagers", "Game_Data/level9" },
                m.files.Select(f => f.path), "the debug folder isn't shipped");
            Assert.IsTrue(m.files[0].exec);
            Assert.IsFalse(m.files[1].exec);
            Assert.AreEqual(PatchChunker.SmallBits, m.files[2].Bits, "globalgamemanagers in small chunks");
            Assert.AreEqual(PatchChunker.DefaultBits, m.files[1].Bits);
            long end = 0;
            foreach (var c in m.chunks.OrderBy(c => c.offset))
            {
                Assert.AreEqual(end, c.offset, "the pack is the chunks one after another");
                end += c.DownloadSize;
            }
            Assert.AreEqual(new FileInfo(first.PackPath).Length, end);
            var zeros = m.files.First(f => f.path == "Game_Data/level9").chunks.Distinct().Select(i => m.chunks[i]).ToList();
            Assert.LessOrEqual(zeros.Count, 2, "repeated zero chunks are stored once");
            Assert.Less(zeros.Sum(c => c.DownloadSize), 2_000, "zeros pack");

            // The next release: one file dropped, the data grown at its end. Only the new chunks go into its pack, and the
            // dropped file is in 'removed'. Publishing the same build again replaces its pack instead of adding to it.
            var grown = data.Concat(RandomBytes(50_000, 15)).ToArray();
            var second = Publish("2026.11.05.1200", new Dictionary<string, byte[]> { ["Game_Data/data.resS"] = grown }, linux: true);
            Assert.LessOrEqual(second.NewChunks, 3, "the last data chunk and the version file");
            CollectionAssert.AreEqual(new[] { "Game_Data/level9" }, second.Manifest.removed);
            var again = Publish("2026.11.05.1200", new Dictionary<string, byte[]> { ["Game_Data/data.resS"] = grown }, linux: true);
            Assert.AreEqual(second.NewChunks, again.NewChunks);
            Assert.AreEqual(2, Directory.GetFiles(Out, "*.pack").Length);

            // A Windows build of the same data shares the Linux chunks.
            var windows = Publish("2026.11.05.1200", new Dictionary<string, byte[]> { ["Game_Data/data.resS"] = grown });
            Assert.IsTrue(windows.Manifest.packs.Contains("2026.11.02/GoF2Remake-Linux-2026.11.02.pack"));

            // A game folder of the first release becomes the second from the packs.
            string game = Path.Combine(Sandbox, "Game");
            WriteBuild(game, "2026.11.02.0755", new Dictionary<string, byte[]> { ["Game_Data/data.resS"] = data, ["Game_Data/level9"] = new byte[300_000] }, linux: true);
            var install = new PatchInstall(game, second.Manifest);
            install.Scan(CancellationToken.None);
            CollectionAssert.AreEqual(new[] { "Game_Data/level9" }, install.Removing);
            DownloadAll(install);
            install.Assemble(CancellationToken.None);
            CollectionAssert.AreEqual(grown, File.ReadAllBytes(install.StagedPath("Game_Data/data.resS")));
        }

        [Test]
        public void AnApkIsRebuiltFromTheInstalledOne()
        {
            // An "APK": a zip of the version file and data, as Unity's Android build carries them.
            byte[] Apk(string version, byte[] data)
            {
                var ms = new MemoryStream();
                using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
                {
                    using (var s = zip.CreateEntry("assets/bin/Data/globalgamemanagers").Open())
                    {
                        var v = System.Text.Encoding.ASCII.GetBytes("xx" + version + "yy");
                        s.Write(v, 0, v.Length);
                    }
                    using (var s = zip.CreateEntry("assets/bin/Data/data", System.IO.Compression.CompressionLevel.NoCompression).Open())
                        s.Write(data, 0, data.Length);
                }
                return ms.ToArray();
            }
            var data = RandomBytes(3_000_000, 17);
            var changed = (byte[])data.Clone();
            new Random(18).NextBytes(new Span<byte>(changed, 1_000_000, 20_000));
            string dir = Path.Combine(Sandbox, "Apks");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "old.apk"), Apk("2026.11.02.0755", data));
            File.WriteAllBytes(Path.Combine(dir, "new.apk"), Apk("2026.11.05.1200", changed));

            var b = PatchPublish.Inspect(Path.Combine(dir, "old.apk"));
            Assert.AreEqual("Android", b.Platform);
            Assert.AreEqual("2026.11.02.0755", b.Version);
            PatchPublish.Run(b, Store, Out, packBase: "https://example.invalid/releases/download/");
            var r = PatchPublish.Run(PatchPublish.Inspect(Path.Combine(dir, "new.apk")), Store, Out, packBase: "https://example.invalid/releases/download/");
            Assert.IsNull(r.Manifest.Problem("Android"));
            Assert.AreEqual(PatchManifest.ApkName, r.Manifest.files.Single().path);

            // The game: the installed APK is read where Android keeps it, the work folder is in the app's data.
            string installed = Path.Combine(dir, "old.apk"), work = Path.Combine(Sandbox, "AppData", PatchInstall.FolderName);
            var install = new PatchInstall(null, r.Manifest, work, p => p == PatchManifest.ApkName ? installed : null);
            install.Scan(CancellationToken.None);
            Assert.Less(install.DownloadBytes, new FileInfo(Path.Combine(dir, "new.apk")).Length / 2,
                "only the chunks around the changed stretch and the zip's directory");
            DownloadAll(install);
            install.Assemble(CancellationToken.None);
            CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(dir, "new.apk")), File.ReadAllBytes(install.StagedPath(PatchManifest.ApkName)),
                "byte for byte the published APK (its signature stays valid)");
            Assert.IsTrue(PatchInstall.CleanUpAppliedIn(work, "2026.11.05.1200"));
        }

        [Test]
        public void UwpPackagesGetAnAppInstallerFile()
        {
            string msix = Path.Combine(Sandbox, "Galaxy on Fire 2_2026.11.5.1200_x64.msix");
            using (var zip = new System.IO.Compression.ZipArchive(File.Create(msix), System.IO.Compression.ZipArchiveMode.Create))
            using (var w = new StreamWriter(zip.CreateEntry("AppxManifest.xml").Open()))
                w.Write("<?xml version=\"1.0\" encoding=\"utf-8\"?><Package xmlns=\"http://schemas.microsoft.com/appx/manifest/foundation/windows10\">" +
                        "<Identity Name=\"JoppieToppie.GoF2Remake\" Publisher=\"CN=JoppieToppie\" Version=\"2026.11.5.1200\" ProcessorArchitecture=\"x64\" />" +
                        "</Package>");
            var r = PatchPublish.PublishUwp(msix, Out);
            Assert.AreEqual("2026.11.05", r.Tag);
            Assert.AreEqual(Path.Combine(Out, "GoF2Remake-UWP.appinstaller"), r.AppInstallerPath);
            Assert.AreEqual(Path.Combine(Out, "GoF2Remake-UWP-2026.11.05.msix"), r.PackagePath);
            Assert.IsTrue(File.Exists(r.PackagePath));
            var doc = System.Xml.Linq.XDocument.Parse(File.ReadAllText(r.AppInstallerPath));
            System.Xml.Linq.XNamespace ns = PatchPublish.AppInstallerNamespace;
            Assert.AreEqual("https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/releases/latest/download/GoF2Remake-UWP.appinstaller",
                doc.Root.Attribute("Uri").Value, "Windows' own check always finds the newest release's");
            var main = doc.Root.Element(ns + "MainPackage");
            Assert.AreEqual("JoppieToppie.GoF2Remake", main.Attribute("Name").Value);
            Assert.AreEqual("CN=JoppieToppie", main.Attribute("Publisher").Value);
            Assert.AreEqual("2026.11.5.1200", main.Attribute("Version").Value);
            Assert.AreEqual("https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/releases/download/2026.11.05/GoF2Remake-UWP-2026.11.05.msix",
                main.Attribute("Uri").Value);
        }

        [Test]
        public void ARebuiltReleaseReplacesItsPack()
        {
            // A fix during a release: the same tag, a new build. Its pack is written anew (same name on the release), the
            // superseded manifest leaves the history, and nothing points into the old pack's offsets.
            var data = RandomBytes(2_000_000, 19);
            var first = Publish("2026.11.05.0900", new Dictionary<string, byte[]> { ["a.bin"] = data });
            var rebuilt = Publish("2026.11.05.1300", new Dictionary<string, byte[]> { ["a.bin"] = data, ["b.bin"] = RandomBytes(500_000, 20) });
            Assert.AreEqual(first.PackPath, rebuilt.PackPath);
            CollectionAssert.AreEqual(new[] { "2026.11.05/GoF2Remake-Windows-2026.11.05.pack" }, rebuilt.Manifest.packs);
            Assert.AreEqual(rebuilt.Manifest.chunks.Length, rebuilt.NewChunks, "every chunk packed again");
            CollectionAssert.AreEqual(new[] { "Windows-2026.11.05.1300.json" }, Directory.GetFiles(Store).Select(Path.GetFileName));

            // ... and a game folder becomes that build from the new pack.
            string game = Path.Combine(Sandbox, "Game");
            Directory.CreateDirectory(game);
            var install = new PatchInstall(game, rebuilt.Manifest);
            install.Scan(CancellationToken.None);
            DownloadAll(install);
            install.Assemble(CancellationToken.None);
            CollectionAssert.AreEqual(data, File.ReadAllBytes(install.StagedPath("a.bin")));
        }

        [Test]
        public void AnAppliedUpdateCleansUp()
        {
            string game = Path.Combine(Sandbox, "Game");
            Directory.CreateDirectory(game);
            var m = Publish("2026.11.05.1200", new Dictionary<string, byte[]> { ["a.bin"] = RandomBytes(1000, 11) }).Manifest;
            new PatchInstall(game, m).Scan(CancellationToken.None);
            Assert.IsFalse(PatchInstall.CleanUpApplied(game, "2026.11.02.0755"), "an older game: the update is still to come");
            Assert.IsTrue(Directory.Exists(Path.Combine(game, PatchInstall.FolderName)));
            Assert.IsTrue(PatchInstall.CleanUpApplied(game, "2026.11.05.1200"));
            Assert.IsFalse(Directory.Exists(Path.Combine(game, PatchInstall.FolderName)));
        }
    }
}
