// UiFont.cs
// Remake (the user's request, 2026-10-24): Options > Accessibility "Interface font", a font of the player's choice for the
// whole interface instead of Inter (the theme's; Klavika until 2026-11-15, which players who own it import here): a font file they imported ("Import font file...", copied into
// persistentDataPath/Fonts) or one installed on the device (FontEngine.GetSystemFontNames). UiLayout.Configure puts it
// inline on every panel's root and the panel's top element, so every text inherits it: the theme sets the font only on
// :root, and bold text is -unity-font-style: bold (.gof-semibold too), which takes the family's own Bold face when it has
// one (an imported file of the same family, the installed "Bold" style), else the asset's faux bold. Letters the font
// lacks fall back to Inter. A font that can't be loaded leaves Inter, with the reason on the row.
// An imported file in use is kept open by the font engine: removing it deletes it at the next start when it can't now.

using System;
using System.Collections.Generic;
using System.IO;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class UiFont
    {
        /// <summary>Settings.UiFont's forms: "" the theme's Inter, "file:<file name>" an imported file, "system:<family> -
        /// <style>" an installed font (FontEngine's names).</summary>
        public const string FilePrefix = "file:", SystemPrefix = "system:";
        /// <summary>The font files the importer takes (a .ttc's first face).</summary>
        public static readonly string[] Extensions = { "ttf", "otf", "ttc" };
        /// <summary>The theme's font, the fallback of every chosen one (the asset the theme loads, found by name).</summary>
        const string ThemeFont = "Inter-Regular-SDF";
        const int PointSize = 90, Padding = 9, Atlas = 1024;

        public static string Folder => Path.Combine(GamePaths.Data, "Fonts");
        static string PendingFile => Path.Combine(Folder, "remove-at-start.txt");

        /// <summary>Why the chosen font isn't in use (missing, unreadable), or the last import's problem; null when fine.</summary>
        public static string Problem { get; private set; }
        /// <summary>Changes whenever the font in use may have: UiLayout.Revision takes it, so every screen applies it again.</summary>
        public static int Revision => unchecked((Settings.UiFont ?? "").GetHashCode() * 31 + importSerial);
        static int importSerial;
        static List<Choice> choices;
        static int choicesSerial;

        static string loadedKey;
        static FontAsset loaded, themeFont;
        static readonly Dictionary<string, FontAsset> assets = new Dictionary<string, FontAsset>();
        static string[] systemNames;
        static bool pendingDone;

        public struct Choice { public string key, name; }

        /// <summary>Modified: Play mode runs without a domain reload, and the font assets of the last session are gone
        /// (destroyed with it), so the caches start over.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            loadedKey = null;
            loaded = null;
            themeFont = null;
            assets.Clear();
            choices = null;
            Problem = null;
        }

        /// <summary>The font in use, null for the theme's. Loaded once per choice.</summary>
        public static FontAsset Current
        {
            get
            {
                string key = Settings.UiFont ?? "";
                // Modified: a font made in an earlier Play session is destroyed when Play mode ends, but the statics
                // live on (no domain reload): the dead asset compared as null and the theme's font showed while the
                // option still named the chosen one. Loaded again then.
                if (key == loadedKey && (key.Length == 0 || loaded != null || Problem != null)) return loaded;
                loadedKey = key;
                loaded = null;
                if (key.Length == 0) { Problem = null; return null; }
                loaded = Load(key, out string problem);
                Problem = problem;
                return loaded;
            }
        }

        /// <summary>Puts the chosen font (or the theme's back) on a panel's root and its top element.</summary>
        public static void Apply(VisualElement root)
        {
            if (root == null) return;
            var font = Current;
            var style = font != null ? new StyleFontDefinition(FontDefinition.FromSDFFont(font)) : new StyleFontDefinition(StyleKeyword.Null);
            root.style.unityFontDefinition = style;
            var top = root.panel?.visualTree;
            if (top != null && top != root) top.style.unityFontDefinition = style;
        }

        /// <summary>What the option offers, the theme's first (its name null: the caller's text): the imported files, then
        /// the installed families (each family's Regular, else its first style).</summary>
        public static List<Choice> Choices()
        {
            if (choices != null && choicesSerial == importSerial) return choices;
            choicesSerial = importSerial;
            var list = choices = new List<Choice> { new Choice { key = "", name = null } };
            foreach (var file in ImportedFiles())
            {
                // Named by the font itself ("LDFComicSans Bold"), the file name when it can't be read.
                var face = FromFile(Path.Combine(Folder, file));
                string name = face != null && !string.IsNullOrEmpty(face.faceInfo.familyName)
                    ? face.faceInfo.familyName + (IsPlainStyle(face.faceInfo.styleName) ? "" : " " + face.faceInfo.styleName)
                    : Path.GetFileNameWithoutExtension(file);
                list.Add(new Choice { key = FilePrefix + file, name = name });
            }
            var families = new Dictionary<string, string>();
            var order = new List<string>();
            foreach (var n in SystemNames())
            {
                Split(n, out string family, out string style);
                if (!families.TryGetValue(family, out var chosen)) { families[family] = n; order.Add(family); }
                else if (style == "Regular" && !chosen.EndsWith(" - Regular", StringComparison.Ordinal)) families[family] = n;
            }
            foreach (var family in order)
            {
                string n = families[family];
                Split(n, out _, out string style);
                list.Add(new Choice { key = SystemPrefix + n, name = style == "Regular" ? family : family + " " + style });
            }
            return list;
        }

        /// <summary>The imported font files (names), sorted; the ones waiting to be deleted left out.</summary>
        public static List<string> ImportedFiles()
        {
            DeletePending();
            var list = new List<string>();
            try
            {
                if (!Directory.Exists(Folder)) return list;
                var pending = Pending();
                foreach (var path in Directory.GetFiles(Folder))
                {
                    string name = Path.GetFileName(path);
                    if (IsFontFile(name) && !pending.Contains(name)) list.Add(name);
                }
            }
            catch (Exception e) { Debug.LogWarning("UiFont: can't list " + Folder + ": " + e.Message); }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        static bool IsPlainStyle(string style) => string.IsNullOrEmpty(style) || style == "Regular" || style == "Normal" || style == "Book";

        public static bool IsFontFile(string name)
        {
            string ext = Path.GetExtension(name ?? "").TrimStart('.').ToLowerInvariant();
            return Array.IndexOf(Extensions, ext) >= 0;
        }

        /// <summary>The system's file picker; a readable font file is copied into the Fonts folder and chosen. 'done' gets
        /// the problem, or null (also when cancelled).</summary>
        public static async void Import(string title, Action<string> done)
        {
            string problem = null;
            try
            {
                var poll = ModImport.Pick(title, "Fonts", Extensions);
                ModImport.Picked picked;
                while ((picked = poll()) == null) await Awaitable.NextFrameAsync();
                if (picked.cancelled) { done?.Invoke(null); return; }
                problem = picked.error ?? Install(picked.path, picked.name);
            }
            catch (Exception e) { problem = e.Message; }
            Problem = problem;
            importSerial++;
            done?.Invoke(problem);
        }

        /// <summary>Checks 'path' is a font the engine reads and copies it into the Fonts folder as 'name' (a free name if
        /// that one is taken), then chooses it. The problem, or null.</summary>
        public static string Install(string path, string name)
        {
            if (!IsFontFile(name))
                return Localization.Extra("uiFontNotFont", "That isn't a font file (.ttf, .otf or .ttc).");
            bool readable = FontEngine.LoadFontFace(path) == FontEngineError.Success;
            FontEngine.UnloadFontFace();   // the picked copy free to delete; the font assets load their own faces
            if (!readable) return Localization.Extra("uiFontUnreadable", "This font file can't be read.");
            Directory.CreateDirectory(Folder);
            string baseName = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
            string target = Path.Combine(Folder, name);
            for (int i = 2; File.Exists(target) || Pending().Contains(Path.GetFileName(target)); i++)
                target = Path.Combine(Folder, baseName + "_" + i + ext);
            File.Copy(path, target);
            try { File.Delete(path); } catch { }
            Settings.UiFont = FilePrefix + Path.GetFileName(target);
            return null;
        }

        /// <summary>The imported font in use goes (the theme's font back); a file the engine still holds open is deleted at
        /// the next start.</summary>
        public static void RemoveCurrent()
        {
            string key = Settings.UiFont ?? "";
            if (!key.StartsWith(FilePrefix, StringComparison.Ordinal)) return;
            string name = key.Substring(FilePrefix.Length);
            Settings.UiFont = "";
            try { File.Delete(Path.Combine(Folder, name)); }
            catch (Exception)
            {
                try { File.AppendAllText(PendingFile, name + "\n"); } catch { }
            }
            importSerial++;
        }

        static FontAsset Load(string key, out string problem)
        {
            problem = null;
            FontAsset regular = null, bold = null, italic = null, boldItalic = null;
            if (key.StartsWith(FilePrefix, StringComparison.Ordinal))
            {
                string name = key.Substring(FilePrefix.Length);
                string path = Path.Combine(Folder, name);
                if (!File.Exists(path) || Pending().Contains(name))
                {
                    problem = string.Format(Localization.Extra("uiFontMissing", "The font file {0} is gone."), name);
                    return null;
                }
                regular = FromFile(path);
                if (regular != null)
                {
                    // The family's other imported faces give bold and italic: the same family, or one named after it plus
                    // the style ("LDFComicSansBold" / Bold beside "LDFComicSans").
                    string family = regular.faceInfo.familyName;
                    foreach (var other in ImportedFiles())
                    {
                        if (other == name) continue;
                        var face = FromFile(Path.Combine(Folder, other));
                        if (face == null) continue;
                        string f = face.faceInfo.familyName ?? "";
                        if (f != family && !(f.StartsWith(family, StringComparison.OrdinalIgnoreCase) &&
                                             face.faceInfo.styleName.Replace(" ", "").IndexOf(f.Substring(family.Length).Trim(), StringComparison.OrdinalIgnoreCase) >= 0))
                            continue;
                        Pick(face.faceInfo.styleName, face, ref bold, ref italic, ref boldItalic);
                    }
                }
            }
            else if (key.StartsWith(SystemPrefix, StringComparison.Ordinal))
            {
                Split(key.Substring(SystemPrefix.Length), out string family, out string style);
                regular = FromSystem(family, style);
                if (regular != null)
                    foreach (var n in SystemNames())
                    {
                        Split(n, out string f, out string s);
                        if (f == family && s != style) Pick(s, null, ref bold, ref italic, ref boldItalic, () => FromSystem(f, s));
                    }
            }
            if (regular == null)
            {
                problem ??= Localization.Extra("uiFontUnreadable", "This font file can't be read.");
                return null;
            }
            var table = regular.fontWeightTable;
            if (table != null && table.Length > 7)
            {
                if (italic != null) table[4].italicTypeface = italic;
                if (bold != null) table[7].regularTypeface = bold;
                if (boldItalic != null) table[7].italicTypeface = boldItalic;
            }
            return regular;
        }

        /// <summary>Files 'face' (or what 'make' loads) as the bold / italic / bold italic face by its style name.</summary>
        static void Pick(string style, FontAsset face, ref FontAsset bold, ref FontAsset italic, ref FontAsset boldItalic, Func<FontAsset> make = null)
        {
            string s = (style ?? "").ToLowerInvariant();
            bool isBold = s.Contains("bold") && !s.Contains("semi") && !s.Contains("demi") && !s.Contains("extra") && !s.Contains("ultra");
            bool isItalic = s.Contains("italic") || s.Contains("oblique");
            bool plainItalic = isItalic && (s == "italic" || s == "oblique");
            if (isBold && isItalic) { if (boldItalic == null) boldItalic = face ?? make?.Invoke(); }
            else if (isBold && s == "bold") { if (bold == null) bold = face ?? make?.Invoke(); }
            else if (plainItalic) { if (italic == null) italic = face ?? make?.Invoke(); }
        }

        /// <summary>A font file's asset, through a Font object of the file (the Font constructor takes a path to a file;
        /// FontAsset.CreateFontAsset(path, ...) makes an asset without one, which the text generator refuses: "FontAsset
        /// is invalid. Please assign a Source Font File.", every text blank, and as DynamicOS it looks the family up among
        /// the installed fonts).</summary>
        static FontAsset FromFile(string path) => Cached("file:" + path, () =>
        {
            var font = new Font(path);
            return FontAsset.CreateFontAsset(font, PointSize, Padding, GlyphRenderMode.SDFAA, Atlas, Atlas, AtlasPopulationMode.Dynamic, true);
        });

        static FontAsset FromSystem(string family, string style) => Cached("system:" + family + " - " + style, () =>
            FontAsset.CreateFontAsset(family, style, PointSize));

        static FontAsset Cached(string key, Func<FontAsset> make)
        {
            if (assets.TryGetValue(key, out var fa) && fa != null) return fa;
            try { fa = make(); }
            catch (Exception e) { Debug.LogWarning("UiFont: " + key + ": " + e.Message); fa = null; }
            if (fa != null)
            {
                var theme = ThemeAsset();
                if (theme != null) fa.fallbackFontAssetTable = new List<FontAsset> { theme };
            }
            assets[key] = fa;
            return fa;
        }

        static FontAsset ThemeAsset()
        {
            if (themeFont != null) return themeFont;
            foreach (var fa in Resources.FindObjectsOfTypeAll<FontAsset>())
                if (fa.name == ThemeFont) { themeFont = fa; break; }
            return themeFont;
        }

        static string[] SystemNames()
        {
            if (systemNames != null) return systemNames;
            try { systemNames = FontEngine.GetSystemFontNames() ?? Array.Empty<string>(); }
            catch (Exception) { systemNames = Array.Empty<string>(); }
            Array.Sort(systemNames, StringComparer.OrdinalIgnoreCase);
            return systemNames;
        }

        /// <summary>"Arial - Bold Italic" into its family and style ("Regular" without a style).</summary>
        static void Split(string name, out string family, out string style)
        {
            int i = name.LastIndexOf(" - ", StringComparison.Ordinal);
            family = i > 0 ? name.Substring(0, i) : name;
            style = i > 0 ? name.Substring(i + 3) : "Regular";
        }

        static HashSet<string> Pending()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(PendingFile))
                    foreach (var line in File.ReadAllLines(PendingFile)) if (line.Trim().Length > 0) set.Add(line.Trim());
            }
            catch { }
            return set;
        }

        /// <summary>Once a run, before any font is loaded: the removed files the engine held open last time.</summary>
        static void DeletePending()
        {
            if (pendingDone) return;
            pendingDone = true;
            var pending = Pending();
            if (pending.Count == 0) return;
            var left = new List<string>();
            foreach (var name in pending)
            {
                try { File.Delete(Path.Combine(Folder, name)); }
                catch { left.Add(name); }
            }
            try
            {
                if (left.Count == 0) File.Delete(PendingFile);
                else File.WriteAllLines(PendingFile, left);
            }
            catch { }
        }
    }
}
