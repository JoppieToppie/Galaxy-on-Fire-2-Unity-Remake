// GameContent.cs
// Feature: docs/notes/player-assets.md "The content layer": where the game's own content comes from. The player's own copy
// of Galaxy on Fire 2, converted into a cache (OriginalConverter, the separate packs layered over it), in the project's own
// layout below GamePaths.Data/Original; while a build still carries the originals (the Editor, builds before the switch),
// Resources is the fallback. Every reader of an original goes through here, so the two sources stay one path.
// The HD resources (the HD pack: HD textures, upscaled skies, 3D planets) live in a folder of their own (GamePaths.Data/HD)
// and are read before the copy's files while they are on (Options > Graphics "HD resources"); a player short of space has
// them written over the copy instead (OriginalPacks.InstallHd), and then there is no folder (docs/notes/player-assets.md
// "HD resources").

using System;
using System.IO;
using UnityEngine;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class GameContent
    {
        /// <summary>The converted cache's folder (GamePaths.Data/Original), or the folder set for tests.</summary>
        public static string Root
        {
            get => root ?? DevCache ?? Path.Combine(GamePaths.Data, "Original");
            set { root = value; installed = null; }
        }
        static string root;

        /// <summary>The Editor and development builds: $GOF2_ORIGINAL_CACHE names a converted copy to run from (the test bed's
        /// Play mode and its play-mode tests, whose sandboxed data folder has none; PlayerAssetsCheck in a build). A runtime
        /// check: Unity 7 no longer defines DEVELOPMENT_BUILD for scripts.</summary>
        static string DevCache
        {
            get
            {
                if (!Application.isEditor && !Debug.isDebugBuild) return null;
                string d = System.Environment.GetEnvironmentVariable("GOF2_ORIGINAL_CACHE");
                return string.IsNullOrEmpty(d) ? null : d;
            }
        }
        static bool? installed;

        /// <summary>The HD resources' own folder (GamePaths.Data/HD), or the folder set for tests ($GOF2_HD_CACHE in the
        /// Editor and development builds, like DevCache).</summary>
        public static string HdRoot
        {
            get
            {
                if (hdRoot != null) return hdRoot;
                if (Application.isEditor || Debug.isDebugBuild)
                {
                    string d = System.Environment.GetEnvironmentVariable("GOF2_HD_CACHE");
                    if (!string.IsNullOrEmpty(d)) return d;
                }
                return Path.Combine(GamePaths.Data, "HD");
            }
            set { hdRoot = value; hdActive = null; }
        }
        static string hdRoot;

        /// <summary>The PlayerPrefs key of Options > Graphics "Use the HD resources" (Settings.HdResources' storage: a float,
        /// 1 on, the default; 0 off).</summary>
        public const string HdPref = "gof2.hdResources";

        /// <summary>The HD folder is there (its _packs.tsv, written once its files are).</summary>
        public static bool HdInstalled => File.Exists(Path.Combine(HdRoot, "_packs.tsv"));

        /// <summary>The HD folder's files are read before the copy's: installed and on. Read once (a change of the option
        /// counts from the next start, or the next Refresh).</summary>
        public static bool HdActive => hdActive ??= HdInstalled && PlayerPrefs.GetFloat(HdPref, 1f) > 0.5f;
        static bool? hdActive;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { root = null; installed = null; hdRoot = null; hdActive = null; }

        /// <summary>Whether a converted copy of this converter's format is there (its manifest, which the converter writes
        /// last, starting with OriginalConverter.ManifestHeader).</summary>
        public static bool Installed => installed ??= FirstLine(Path.Combine(Root, "_manifest.tsv")) == OriginalConverter.ManifestHeader;

        /// <summary>A converted copy is there but made by an older game (another format): convert it again.</summary>
        public static bool Stale
        {
            get
            {
                string first = FirstLine(Path.Combine(Root, "_manifest.tsv"));
                return first != null && first != OriginalConverter.ManifestHeader;
            }
        }

        /// <summary>The folder the GPU-ready caches made from the converted copy go to (dropped with it).</summary>
        public static string GpuCache => Path.Combine(Root, "_gpu", "v" + OriginalConverter.FormatVersion);

        static string FirstLine(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using var r = new StreamReader(path);
                return r.ReadLine();
            }
            catch (IOException) { return null; }
        }

        /// <summary>Forget what was found (after a conversion or a pack installed); the content caches start again
        /// (Refreshed).</summary>
        public static void Refresh()
        {
            installed = null;
            hdActive = null;
            Refreshed?.Invoke();
        }

        /// <summary>After Refresh: what was read from the cache (or found missing in it) is to be read again. The caches
        /// subscribe as they reset their statics (never cleared here: their order is undefined).</summary>
        public static event System.Action Refreshed;

        /// <summary>A file of the cache by its path there ("Resources/GoF2Data/items.json"): the HD folder's while it is on,
        /// else the copy's; null when neither has it.</summary>
        public static string CachePath(string rel)
        {
            if (!Installed) return null;
            if (HdActive)
            {
                string h = Path.Combine(HdRoot, rel);
                if (File.Exists(h)) return h;
            }
            string p = Path.Combine(Root, rel);
            return File.Exists(p) ? p : null;
        }

        /// <summary>A text table by its Resources path ("GoF2Data/items", no extension): the cache's .json, else the
        /// TextAsset in Resources. Null when neither has it.</summary>
        public static string Text(string resourcePath)
        {
            using var timing = ContentTimings.Measure("table text");
            var p = CachePath("Resources/" + resourcePath + ".json");
            if (p != null) return File.ReadAllText(p);
            var ta = Resources.Load<TextAsset>(resourcePath);
            if (ta == null) return null;
            string text = ta.text;
            Resources.UnloadAsset(ta);
            return text;
        }

        /// <summary>A binary table by its Resources path (a .bytes TextAsset there; the same name + .bytes in the cache).</summary>
        public static byte[] Bytes(string resourcePath)
        {
            var p = CachePath("Resources/" + resourcePath + ".bytes");
            if (p != null) return File.ReadAllBytes(p);
            var ta = Resources.Load<TextAsset>(resourcePath);
            if (ta == null) return null;
            var b = ta.bytes;
            Resources.UnloadAsset(ta);
            return b;
        }

        /// <summary>The text table of a language (text_<code>.json: the original's strings, the packs' overlay applied), null
        /// when the cache has none for it (the caller then uses the build's own table, LanguageTables).</summary>
        public static string LanguageText(string code)
        {
            var p = CachePath("Localization/text_" + code + ".json");
            return p != null ? File.ReadAllText(p) : null;
        }
    }
}
