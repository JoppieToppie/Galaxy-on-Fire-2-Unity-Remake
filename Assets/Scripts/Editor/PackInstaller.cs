// PackInstaller.cs  (Editor only)
// Joins the split asset pack in <project>/GoF2_ImportParts/ (GoF2_AssetPack.zip.001, .002, ...)
// and extracts it into Assets. Offered automatically once all parts are present, or run it via
// menu "GoF2/Setup/Install Asset Pack".

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GoF2Remake.EditorTools
{
    [InitializeOnLoad]
    public static class PackInstaller
    {
        const string PartsDir = "GoF2_ImportParts";
        const string ManifestName = "GoF2_AssetPack_manifest.txt";   // lines: "<file> <bytes>"
        const string DoneMarker = "Assets/.pack_installed";

        static PackInstaller()
        {
            EditorApplication.delayCall += () =>
            {
                if (File.Exists(DoneMarker) || !File.Exists(Path.Combine(PartsDir, ManifestName))) return;
                if (!PartsComplete(out _)) return;
                if (EditorUtility.DisplayDialog("GoF2 asset pack",
                        "All parts of the GoF2 asset pack are present. Install them into Assets now?\n\n(About 1.9 GB; the first import takes a while.)",
                        "Install", "Later"))
                    Install();
            };
        }

        static bool PartsComplete(out string[] parts)
        {
            parts = new string[0];
            var manifest = Path.Combine(PartsDir, ManifestName);
            if (!File.Exists(manifest)) return false;
            var lines = File.ReadAllLines(manifest).Where(l => l.Trim().Length > 0).ToArray();
            parts = new string[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                var bits = lines[i].Split(' ');
                var path = Path.Combine(PartsDir, bits[0]);
                long size = long.Parse(bits[1]);
                if (!File.Exists(path) || new FileInfo(path).Length != size) return false;
                parts[i] = path;
            }
            return parts.Length > 0;
        }

        [MenuItem("GoF2/Setup/Install Asset Pack", priority = 400)]
        public static void Install()
        {
            if (!PartsComplete(out var parts))
            {
                EditorUtility.DisplayDialog("GoF2 asset pack", "Some parts in " + PartsDir + " are missing or incomplete.", "OK");
                return;
            }
            string joined = Path.Combine("Library", "GoF2_AssetPack.zip");
            try
            {
                using (var outStream = File.Create(joined))
                {
                    for (int i = 0; i < parts.Length; i++)
                    {
                        EditorUtility.DisplayProgressBar("GoF2 asset pack", "Joining parts " + (i + 1) + "/" + parts.Length, (float)i / parts.Length * 0.3f);
                        using (var inStream = File.OpenRead(parts[i])) inStream.CopyTo(outStream);
                    }
                }

                using (var zipStream = File.OpenRead(joined))
                using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Read))
                {
                    int n = 0, total = zip.Entries.Count;
                    string root = Path.GetFullPath(".");
                    foreach (var e in zip.Entries)
                    {
                        if (n++ % 50 == 0)
                            EditorUtility.DisplayProgressBar("GoF2 asset pack", "Extracting " + e.FullName, 0.3f + 0.7f * n / total);
                        var dest = Path.GetFullPath(Path.Combine(root, e.FullName));
                        if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;   // safety
                        if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(dest); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        using (var src = e.Open())
                        using (var dst = File.Create(dest)) src.CopyTo(dst);
                    }
                }
                File.WriteAllText(DoneMarker, DateTime.Now.ToString("o"));
            }
            catch (Exception ex)
            {
                Debug.LogError("GoF2: installing the asset pack failed: " + ex);
                return;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (File.Exists(joined)) File.Delete(joined);
            }

            AssetDatabase.Refresh();
            if (EditorUtility.DisplayDialog("GoF2 asset pack",
                    "Installed. Unity will now import the assets (this can take a while).\n\nDelete the " + PartsDir + " folder to free about 1.9 GB?",
                    "Delete parts", "Keep"))
                Directory.Delete(PartsDir, true);
            Debug.Log("GoF2: asset pack installed. When importing has finished, run GoF2 > Build > Materials And Prefabs.");
        }
    }
}
