// OriginalConverter.cs
// Feature: docs/notes/player-assets.md "Plan" 3-4: the player's own copy of Galaxy on Fire 2 (the iOS .ipa, the Android
// OBB or XAPK; several layered, later ones over earlier ones) converted into the game's own content cache, in the project's
// own layout so the content layer finds each file where the project keeps it:
//   Textures/<rel>.png          (AeiImage; <rel> as Reference/tools/asset_conversion/run_tex.py names them)
//   Models/<rel>.gof2mesh.json  (AemMesh's sidecar) + Models/<rel>.aem (the mesh file itself, built into a Unity mesh at run
//                               time: the converter keeps the original, a few hundred bytes to a few MB each)
//   Audio/<bank>/<name>.mp3|.wav (FsbBank; <bank> without FMOD_GOF2_)
//   Localization/text_<lang>.json (LangFile; the packs' string overlay applied)
//   Resources/GoF2Data/<table>.json (GameTables, from the copy's .bin files and its English text, the overlay applied)
//   Resources/GoF2Hud/*.png, Resources/GoF2Icons/item_NNN.png / ship_NNN.png (DerivedImages, cut from the interface atlases)
//   _manifest.tsv               (each file: its source package and entry, and the format it was read from)
// Plain C#, no Unity types: the game runs it on a worker thread, the tools run it from the command line.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace GoF2Remake.Data
{
    public sealed class OriginalConverter
    {
        /// <summary>The cache's format: raised whenever the conversion makes something differently (an importer fixed, a file
        /// added), so a cache made by an older game counts as stale (GameContent.Stale) and is converted again; the GPU caches
        /// made from it (OriginalTextures, OriginalSkies) are keyed by it too.</summary>
        public const int FormatVersion = 1;

        /// <summary>The manifest's first line.</summary>
        public static string ManifestHeader => "# gof2 original cache v" + FormatVersion;

        /// <summary>One converted file: its cache path, the package and entry it came from, the source format.</summary>
        public struct Item { public string Path, Package, Entry, Format; }

        public readonly List<Item> Items = new List<Item>();
        public readonly List<string> Problems = new List<string>();
        readonly string root;
        readonly Dictionary<string, int> index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Called with (done, total, entry) as entries are converted (any thread).</summary>
        public Action<int, int, string> Progress;

        /// <summary>Called as the conversion moves on: "read <package file>", then "tables" (the text, data tables and
        /// interface images, made once every package is read).</summary>
        public Action<string> Phase;

        /// <summary>The packs' string overlay: language code (text_<code>) -> string id -> text, put over the copy's own
        /// strings (the remake's corrections; an edition's differing strings); applied before the data tables are made,
        /// whose names and descriptions come from the English table.</summary>
        public Dictionary<string, Dictionary<int, string>> TextOverlay;

        readonly Dictionary<string, (byte[] bytes, string package, string entry)> bins =
            new Dictionary<string, (byte[], string, string)>(StringComparer.Ordinal);
        readonly Dictionary<string, (List<string> strings, string package, string entry)> langs =
            new Dictionary<string, (List<string>, string, string)>(StringComparer.Ordinal);

        public OriginalConverter(string cacheRoot) { root = cacheRoot; }

        /// <summary>Converts every package in order (a later one's file replaces an earlier one's), then writes the
        /// manifest. A package may be a zip (.ipa, .obb, .xapk: an XAPK's OBB is read from inside it) or a folder.</summary>
        public void Run(IEnumerable<string> packages)
        {
            foreach (var p in packages) { Phase?.Invoke("read " + Path.GetFileName(p.TrimEnd('/', '\\'))); Package(p); }
            Phase?.Invoke("tables");
            Finish();
            var m = new StringBuilder(ManifestHeader + "\npath\tpackage\tentry\tformat\n");
            foreach (var it in Items) m.Append(it.Path).Append('\t').Append(it.Package).Append('\t').Append(it.Entry).Append('\t').Append(it.Format).Append('\n');
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "_manifest.tsv"), m.ToString());
        }

        /// <summary>The text tables and the data tables, once every package is read (a later package's files win).</summary>
        void Finish()
        {
            Images();
            foreach (var kv in langs)
            {
                // an edition whose tables are laid out differently (the macOS app's): its strings at the Android ids the
                // remake uses (LangMaps, generated by lang_map.py), the ids it hasn't left for the overlay
                var strings = LangMaps.ToAndroidIds(EditionOf(kv.Value.entry), kv.Value.strings) ?? new List<string>(kv.Value.strings);
                if (TextOverlay != null && TextOverlay.TryGetValue(kv.Key, out var over))
                    foreach (var o in over)
                    {
                        while (strings.Count <= o.Key) strings.Add("");
                        strings[o.Key] = o.Value;
                    }
                if (kv.Key == "en") english = strings;
                Write($"Localization/text_{kv.Key}.json", Encoding.UTF8.GetBytes(LangFile.ToJson(strings)), kv.Value.package, kv.Value.entry, ".lang");
            }
            if (bins.Count == 0) return;
            if (english == null) { Problems.Add("no English text (gb.lang): the data tables need it"); return; }
            try
            {
                var tables = GameTables.Build(p => bins.TryGetValue(p, out var b) ? b.bytes : null, bins.Keys, english);
                foreach (var t in tables)
                {
                    var src = bins.TryGetValue(TableSource(t.Key), out var b) ? b : (null, "", "");
                    Write($"Resources/GoF2Data/{t.Key}.json", Encoding.UTF8.GetBytes(t.Value), src.Item2, src.Item3, "GameTables");
                }
            }
            catch (FormatException ex) { Problems.Add("data tables: " + ex.Message); }
        }

        List<string> english;

        /// <summary>The edition an entry comes from, where it matters (LangMaps): the macOS app's files are under
        /// X.app/Contents/Resources.</summary>
        static string EditionOf(string entry) => entry.Replace('\\', '/').Contains(".app/Contents/") ? "macos" : null;

        // the interface atlases DerivedImages cuts from (by project rel: "textures/gof2_interface_iphone4"), kept decoded
        static readonly HashSet<string> atlasNames = new HashSet<string>(System.Linq.Enumerable.Select(DerivedImages.AtlasNames(), n => "textures/" + n));
        readonly Dictionary<string, DerivedImages.Atlas> atlases = new Dictionary<string, DerivedImages.Atlas>(StringComparer.Ordinal);

        void Images()
        {
            DerivedImages.Atlas A(string name) => atlases.TryGetValue("textures/" + name, out var a) ? a : null;
            foreach (var set in new[] { DerivedImages.HudImages(A), DerivedImages.ItemIcons(A) })
                foreach (var (path, rgba, w, h) in set)
                    Write(path + ".png", PngWriter.Encode(rgba, w, h, dropOpaqueAlpha: false), "", path, "cut from the atlases");
            atlases.Clear();
        }

        /// <summary>The .bin a table is made from (for the manifest).</summary>
        static string TableSource(string table)
        {
            switch (table)
            {
                case "item_attributes": return "bin/items.bin";
                case "names": return "bin/names_terran.bin";
                case "v_collisions": return "assets/valkyrie/bin/v_collisions.bin";
            }
            return table.StartsWith("sn_") ? $"assets/supernova/bin/{table}.bin" : table.StartsWith("v_") ? $"assets/valkyrie/bin/{table}.bin" : $"bin/{table}.bin";
        }

        void Package(string path)
        {
            if (Directory.Exists(path))
            {
                foreach (var f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    string rel = f.Substring(path.Length).TrimStart('/', '\\').Replace('\\', '/');
                    if (rel.EndsWith(".obb", StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".xapk", StringComparison.OrdinalIgnoreCase))
                        Package(f);
                    else Entry(Path.GetFileName(path), rel, () => File.ReadAllBytes(f));
                }
                return;
            }
            using (var fs = File.OpenRead(path))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
                Zip(Path.GetFileName(path), zip, fs);
        }

        /// <summary>The entries of 'zip' ('raw' the stream it reads, for an archive stored inside it).</summary>
        void Zip(string package, ZipArchive zip, Stream raw)
        {
            int done = 0, total = zip.Entries.Count;
            foreach (var e in zip.Entries)
            {
                done++;
                if (e.FullName.EndsWith(".obb", StringComparison.OrdinalIgnoreCase))
                {
                    // an XAPK: its OBB inside, read where it lies when stored (ZipSlice: no 1.4 GB copy in memory), else
                    // through a temporary file
                    string name = package + "/" + Path.GetFileName(e.FullName);
                    var slice = ZipSlice.Stored(raw, e.FullName);
                    if (slice != null)
                    {
                        var sub = new ZipSlice.SubStream(raw, slice.Value.offset, slice.Value.length);
                        using (var inner = new ZipArchive(sub, ZipArchiveMode.Read)) Zip(name, inner, sub);
                    }
                    else
                    {
                        Directory.CreateDirectory(root);
                        string tmp = Path.Combine(root, "_package.tmp");
                        try
                        {
                            using (var s = e.Open()) using (var f = File.Create(tmp)) s.CopyTo(f);
                            using (var f = File.OpenRead(tmp)) using (var inner = new ZipArchive(f, ZipArchiveMode.Read)) Zip(name, inner, f);
                        }
                        finally { if (File.Exists(tmp)) File.Delete(tmp); }
                    }
                    continue;
                }
                Progress?.Invoke(done, total, e.FullName);
                Entry(package, e.FullName, () => { using (var s = e.Open()) using (var ms = new MemoryStream()) { s.CopyTo(ms); return ms.ToArray(); } });
            }
        }

        /// <summary>An entry's path below the game's data folder ("data/textures/x.aei", "data/assets/main/3d/..."), null
        /// when it isn't one of the game's: Android ".../assets/data/...", iOS ".app/data/...", the .lang files beside it.</summary>
        public static string DataPath(string entry)
        {
            string e = entry.Replace('\\', '/');
            int i = e.IndexOf("/assets/data/", StringComparison.Ordinal);
            if (i >= 0) return e.Substring(i + "/assets/".Length);
            i = e.IndexOf(".app/", StringComparison.Ordinal);
            if (i >= 0)
            {
                // iOS: X.app/data/...; macOS: X.app/Contents/Resources/data/... (its languages beside data/)
                string r = e.Substring(i + ".app/".Length);
                return r.StartsWith("Contents/Resources/", StringComparison.Ordinal) ? r.Substring("Contents/Resources/".Length) : r;
            }
            return e.StartsWith("data/") ? e : Path.GetFileName(e);
        }

        /// <summary>A texture's or mesh's path in the project ("main/ships/ship_026_terran_diffuse"), from its data path.</summary>
        public static string ProjectRel(string dataPath)
        {
            string r = dataPath.StartsWith("data/") ? dataPath.Substring(5) : dataPath;
            if (r.StartsWith("assets/")) r = r.Substring(7);
            r = r.Replace("3d/textures/low/etc/", "").Replace("3d/textures/low/pvrtc/", "").Replace("3d/textures/high/dx5/", "")
                 .Replace("3d/textures/", "").Replace("3d/meshes/", "");
            int dot = r.LastIndexOf('.');
            return dot > 0 ? r.Substring(0, dot) : r;
        }

        void Entry(string package, string entry, Func<byte[]> read)
        {
            string ext = Path.GetExtension(entry).ToLowerInvariant();
            if (ext != ".aei" && ext != ".aem" && ext != ".fsb" && ext != ".lang" && ext != ".bin") return;
            string data = DataPath(entry);
            // the macOS app's second, smaller set (high/dx5 is the one converted)
            if (data.Contains("/3d/textures/low/dx5/")) return;
            if (ext == ".bin")
            {
                if (data.StartsWith("data/")) bins[data.Substring(5)] = (read(), package, entry);   // (the tables, in Finish)
                return;
            }
            try
            {
                switch (ext)
                {
                    case ".aei":
                    {
                        var img = AeiImage.Read(read());
                        string rel = ProjectRel(data);
                        if (atlasNames.Contains(rel)) atlases[rel] = new DerivedImages.Atlas { Rgba = img.Rgba, Width = img.Width, Height = img.Height };
                        Write("Textures/" + rel + ".png", PngWriter.Encode(img.Rgba, img.Width, img.Height), package, entry, img.Format);
                        // the hulls' metallic / smoothness map, made from the normal map's specular alpha
                        // (Reference/tools/asset_conversion/split_spec.py: metallic 0, smoothness = that alpha)
                        if (rel.EndsWith("_normal_specular"))
                        {
                            var ms = new byte[img.Rgba.Length];
                            for (int i = 3; i < ms.Length; i += 4) ms[i] = img.Rgba[i];
                            Write("Textures/" + rel.Replace("_normal_specular", "_metallic_smoothness") + ".png",
                                  PngWriter.Encode(ms, img.Width, img.Height, dropOpaqueAlpha: false), package, entry, "metallic from " + img.Format);
                        }
                        break;
                    }
                    case ".aem":
                    {
                        var bytes = read();
                        var mesh = AemMesh.Read(bytes);
                        string rel = ProjectRel(data), name = Path.GetFileName(rel);
                        Write("Models/" + rel + ".aem", bytes, package, entry, "AEM v" + mesh.Version);
                        Write("Models/" + rel + ".gof2mesh.json", Encoding.UTF8.GetBytes(mesh.Sidecar(name, data.StartsWith("data/") ? data.Substring(5) : data)), package, entry, "sidecar");
                        break;
                    }
                    case ".fsb":
                    {
                        var bank = FsbBank.Read(read());
                        string b = Path.GetFileNameWithoutExtension(entry);
                        b = b.StartsWith("FMOD_GOF2_") ? b.Substring(10) : b;
                        if (bank.Extension == null) { Problems.Add($"{entry}: FSB5 codec {bank.Codec} isn't read"); break; }
                        foreach (var s in bank.Samples)
                            Write($"Audio/{b}/{s.Name}{bank.Extension}", bank.Extract(s), package, entry, "FSB5 codec " + bank.Codec);
                        break;
                    }
                    case ".lang":
                    {
                        string stem = Path.GetFileNameWithoutExtension(entry);
                        if (!LangFile.Codes.TryGetValue(stem, out var code)) { Problems.Add($"{entry}: unknown language"); break; }
                        langs[code] = (LangFile.Read(read()), package, entry);   // (written in Finish, with the overlay)
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is FormatException || ex is NotSupportedException || ex is InvalidDataException)
            {
                Problems.Add($"{entry}: {ex.Message}");
            }
        }

        void Write(string rel, byte[] bytes, string package, string entry, string format)
        {
            string full = Path.Combine(root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, bytes);
            var it = new Item { Path = rel, Package = package, Entry = entry, Format = format };
            if (index.TryGetValue(rel, out int at)) Items[at] = it;   // a later package's replaces it
            else { index[rel] = Items.Count; Items.Add(it); }
        }
    }

    /// <summary>RGBA8 (rows top first) as a PNG: one IDAT, no filtering, zlib around DeflateStream.</summary>
#if UNITY_5_3_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]   // (only constant tables: nothing to reset)
#endif
    public static class PngWriter
    {
        public static byte[] Encode(byte[] rgba, int width, int height, bool dropOpaqueAlpha = true)
        {
            bool alpha = true;
            if (dropOpaqueAlpha)
            {
                alpha = false;
                for (int i = 3; i < rgba.Length; i += 4) if (rgba[i] != 255) { alpha = true; break; }
            }
            int bpp = alpha ? 4 : 3, stride = width * bpp + 1;
            var raw = new byte[stride * height];
            for (int y = 0; y < height; y++)
            {
                int o = y * stride; raw[o++] = 0;
                int s = y * width * 4;
                if (alpha) { Buffer.BlockCopy(rgba, s, raw, o, width * 4); continue; }
                for (int x = 0; x < width; x++, s += 4) { raw[o++] = rgba[s]; raw[o++] = rgba[s + 1]; raw[o++] = rgba[s + 2]; }
            }
            var ms = new MemoryStream();
            ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
            var ihdr = new byte[13];
            Be(ihdr, 0, width); Be(ihdr, 4, height); ihdr[8] = 8; ihdr[9] = (byte)(alpha ? 6 : 2);
            Chunk(ms, "IHDR", ihdr);
            var z = new MemoryStream();
            z.WriteByte(0x78); z.WriteByte(0x01);
            using (var d = new DeflateStream(z, CompressionLevel.Fastest, true)) d.Write(raw, 0, raw.Length);
            var ad = new byte[4]; Be(ad, 0, (int)Adler32(raw)); z.Write(ad, 0, 4);
            Chunk(ms, "IDAT", z.ToArray());
            Chunk(ms, "IEND", new byte[0]);
            return ms.ToArray();
        }

        static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4]; Be(len, 0, data.Length); s.Write(len, 0, 4);
            var t = Encoding.ASCII.GetBytes(type); s.Write(t, 0, 4); s.Write(data, 0, data.Length);
            uint c = Crc(Crc(0xFFFFFFFF, t), data) ^ 0xFFFFFFFF;
            var cb = new byte[4]; Be(cb, 0, (int)c); s.Write(cb, 0, 4);
        }

        static void Be(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

        static readonly uint[] Table = MakeTable();
        static uint[] MakeTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++) { uint k = n; for (int i = 0; i < 8; i++) k = (k & 1) != 0 ? 0xEDB88320 ^ (k >> 1) : k >> 1; t[n] = k; }
            return t;
        }

        static uint Crc(uint c, byte[] d)
        {
            foreach (var b in d) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
            return c;
        }

        static uint Adler32(byte[] d)
        {
            uint a = 1, b = 0;
            for (int i = 0; i < d.Length; )
            {
                int n = Math.Min(5552, d.Length - i);
                for (int k = 0; k < n; k++, i++) { a += d[i]; b += a; }
                a %= 65521; b %= 65521;
            }
            return b << 16 | a;
        }
    }
}
