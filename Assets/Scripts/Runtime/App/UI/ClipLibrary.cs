// ClipLibrary.cs
// Remake-only: the screen recorder's clips on disk (ScreenRecorder records them, ClipsView shows them; docs/notes/platforms.md
// "Screen recorder"): the MP4s in the clips folder (desktop: the Videos library's "Galaxy on Fire 2", else <data>/Clips;
// Android: <data>/Clips, which ClipProvider shares), each with a thumbnail and its length in <data>/ClipThumbs
// (<name>.jpg, <name>.json), listed newest first, renamed and deleted with them. The clip's size from the settings
// (Size) is here too, so it can be tested. Plain C#.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.UI
{
    public static class ClipLibrary
    {
        public const string Extension = ".mp4";

        public sealed class Clip
        {
            public string name;       // the file name without ".mp4"
            public string path;
            public long bytes;
            public DateTime when;
            public float seconds = -1f;   // -1: not known (made elsewhere, or the game stopped before it was written)
            public string thumbnail;  // a JPG, or null
        }

        [Serializable] class Info { public float seconds; }

        /// <summary>Where clips are recorded. Tests (GamePaths.Override) and phones keep them in the game's data folder.</summary>
        public static string Folder
        {
            get
            {
#if UNITY_STANDALONE || UNITY_EDITOR
                if (string.IsNullOrEmpty(GamePaths.Override))
                {
                    string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                    if (!string.IsNullOrEmpty(videos)) return Path.Combine(videos, "Galaxy on Fire 2");
                }
#endif
                return Path.Combine(GamePaths.Data, "Clips");
            }
        }

        public static string ThumbFolder => Path.Combine(GamePaths.Data, "ClipThumbs");

        /// <summary>A free path for a new clip: "<prefix>_<date>_<time>.mp4" (prefix file-safe), the folder made.</summary>
        public static string NewPath(string prefix)
        {
            Directory.CreateDirectory(Folder);
            string stem = $"{SafeName(prefix, "GoF2")}_{DateTime.Now:yyyyMMdd_HHmmss}";
            string path = Path.Combine(Folder, stem + Extension);
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(Folder, $"{stem}_{n}{Extension}");
            return path;
        }

        /// <summary>Letters, digits, spaces, - and _ (others become _), at most 80; 'fallback' when nothing is left.</summary>
        public static string SafeName(string name, string fallback)
        {
            var sb = new StringBuilder();
            foreach (char c in (name ?? "").Trim()) sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ' ' ? c : '_');
            string s = sb.ToString().Trim(' ', '_');
            return s.Length == 0 ? fallback : s.Length > 80 ? s.Substring(0, 80) : s;
        }

        /// <summary>The clips, newest first.</summary>
        public static List<Clip> List()
        {
            var list = new List<Clip>();
            if (!Directory.Exists(Folder)) return list;
            foreach (var f in new DirectoryInfo(Folder).GetFiles("*" + Extension))
            {
                string name = Path.GetFileNameWithoutExtension(f.Name);
                var clip = new Clip { name = name, path = f.FullName, bytes = f.Length, when = f.LastWriteTime };
                string thumb = Path.Combine(ThumbFolder, name + ".jpg");
                if (File.Exists(thumb)) clip.thumbnail = thumb;
                string info = Path.Combine(ThumbFolder, name + ".json");
                try { if (File.Exists(info)) clip.seconds = JsonUtility.FromJson<Info>(File.ReadAllText(info)).seconds; }
                catch (Exception) { }
                list.Add(clip);
            }
            list.Sort((a, b) => b.when.CompareTo(a.when));
            return list;
        }

        /// <summary>The clip's length and thumbnail (JPG bytes, null: none) beside the others'.</summary>
        public static void SaveInfo(string name, float seconds, byte[] jpg)
        {
            Directory.CreateDirectory(ThumbFolder);
            File.WriteAllText(Path.Combine(ThumbFolder, name + ".json"), JsonUtility.ToJson(new Info { seconds = seconds }));
            if (jpg != null) File.WriteAllBytes(Path.Combine(ThumbFolder, name + ".jpg"), jpg);
        }

        /// <summary>The clip, its thumbnail and its length gone for good; false when the file couldn't be deleted.</summary>
        public static bool Delete(Clip clip)
        {
            try
            {
                File.Delete(clip.path);
                foreach (var ext in new[] { ".jpg", ".json" })
                {
                    string side = Path.Combine(ThumbFolder, clip.name + ext);
                    if (File.Exists(side)) File.Delete(side);
                }
                return true;
            }
            catch (Exception e) { Debug.LogWarning("Clips: " + e.Message); return false; }
        }

        /// <summary>Renames the clip (and its thumbnail and length); null, or why not (an empty or taken name, the file busy).</summary>
        public static string Rename(Clip clip, string newName)
        {
            string name = SafeName(newName, "");
            if (name.Length == 0) return Localization.Extra("clipsNameEmpty", "Type a name.");
            if (string.Equals(name, clip.name, StringComparison.Ordinal)) return null;
            string path = Path.Combine(Path.GetDirectoryName(clip.path), name + Extension);
            if (File.Exists(path)) return string.Format(Localization.Extra("clipsNameTaken", "There is already a clip called {0}."), name);
            try
            {
                File.Move(clip.path, path);
                foreach (var ext in new[] { ".jpg", ".json" })
                {
                    string side = Path.Combine(ThumbFolder, clip.name + ext);
                    if (File.Exists(side)) File.Move(side, Path.Combine(ThumbFolder, name + ext));
                }
            }
            catch (Exception e) { return e.Message; }
            clip.name = name;
            clip.path = path;
            return null;
        }

        // ---- the clip's size from the settings ------------------------------------------------------------------

        /// <summary>The clip's picture for a screen of 'screenW' x 'screenH': Settings.RecordHeights' height (never above the
        /// screen's), the screen's shape, both multiples of 8 (the encoders' and the shader's packing).</summary>
        public static Vector2Int Size(int screenW, int screenH, int resolution)
        {
            screenW = Mathf.Max(8, screenW);
            screenH = Mathf.Max(8, screenH);
            int h = Data.Settings.RecordHeights[Mathf.Clamp(resolution, 0, Data.Settings.RecordHeights.Length - 1)];
            if (h <= 0 || h > screenH) h = screenH;
            int w = Mathf.RoundToInt(h * (float)screenW / screenH);
            return new Vector2Int(Mathf.Max(8, w / 8 * 8), Mathf.Max(8, h / 8 * 8));
        }

        /// <summary>The video bit rate (kbit/s) for that picture: the quality's bits per pixel per frame, 1.5 to 120 Mbit/s.</summary>
        public static int BitrateKbps(Vector2Int size, int fps, int quality)
        {
            float bpp = Data.Settings.RecordBitsPerPixel[Mathf.Clamp(quality, 0, Data.Settings.RecordBitsPerPixel.Length - 1)];
            return Mathf.Clamp(Mathf.RoundToInt(size.x * (float)size.y * fps * bpp / 1000f), 1500, 120000);
        }

        public static string Duration(float seconds) =>
            seconds < 0f ? "" : seconds >= 3600f ? TimeSpan.FromSeconds(seconds).ToString(@"h\:mm\:ss") : TimeSpan.FromSeconds(seconds).ToString(@"m\:ss");

        public static string Bytes(long bytes) =>
            bytes >= 1L << 30 ? (bytes / (double)(1L << 30)).ToString("0.0", CultureInfo.InvariantCulture) + " GB"
            : bytes >= 1L << 20 ? (bytes / (double)(1L << 20)).ToString("0", CultureInfo.InvariantCulture) + " MB"
            : Math.Max(1L, bytes / 1024).ToString(CultureInfo.InvariantCulture) + " KB";
    }
}
