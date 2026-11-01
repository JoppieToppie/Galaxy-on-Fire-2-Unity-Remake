// ModdingGuide.cs
// Remake-only: the AI modding guide (Modding/ai/gof2-modding: SKILL.md, guide.md, reference.md, missions.md,
// mission.schema.json, events.md, voices.md) for the Mod Builder's MCP server (UI.ModBuilderMcp's read_guide tool and its
// resources), so an assistant connected to the game reads the formats without the repository. The Editor reads the
// repository's folder (always the current text); a player build reads Resources/GoF2ModdingGuide, which the build copies
// from that folder and removes again (EditorTools.ModdingGuideBundle, git-ignored). The guide's files run to 220 KB each, so
// the tool hands them out in parts: an outline of the headings, one section, a search, or a range of lines.
// Load on the main thread (Preload; Resources.Load): the texts are then read from any thread.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]   // the guide's texts, loaded once
    public static class ModdingGuide
    {
        /// <summary>The Resources folder a build carries the guide in.</summary>
        public const string ResourcesFolder = "GoF2ModdingGuide";
        /// <summary>The guide in the repository, from the project folder.</summary>
        public const string RepositoryFolder = "Modding/ai/gof2-modding";
        /// <summary>The most text one answer gives.</summary>
        public const int MaxChars = 40000;

        /// <summary>The guide's files in the order to read them, with what each holds.</summary>
        public static readonly (string file, string about)[] Files =
        {
            ("SKILL.md", "Start here: the complete mod file format with its rules, balancing numbers and a checklist."),
            ("guide.md", "The full modders' guide (the repository's Modding/README.md): every file and field in detail."),
            ("reference.md", "Every number and name of the original game a mod refers to: items and stats, blueprints, ships, systems, stations, story steps and speakers, models and their textures, music, sounds."),
            ("missions.md", "The .gof2mission format: missions, bar missions and events as steps."),
            ("mission.schema.json", "The same mission format as a JSON Schema (get_mission_schema gives it too)."),
            ("events.md", "Every event graph node and command."),
            ("voices.md", "Every voice clip with its speaker and English line, for voice-over."),
        };

        /// <summary>Tests: a folder to read the guide from instead.</summary>
        internal static string FolderOverride;

        static readonly object gate = new object();
        static Dictionary<string, string[]> lines;

        /// <summary>Loads every file (main thread); after that any thread may read.</summary>
        public static void Preload()
        {
            lock (gate)
            {
                if (lines != null) return;
                var l = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                foreach (var (file, _) in Files)
                {
                    string text = Load(file);
                    if (text != null) l[file] = text.Replace("\r\n", "\n").Split('\n');
                }
                lines = l;
            }
        }

        /// <summary>Preload has run (the MCP server's HTTP thread reads only then).</summary>
        public static bool Loaded => lines != null;

        /// <summary>Drops the loaded texts (tests; the next read loads them again).</summary>
        public static void Forget() { lock (gate) lines = null; }

        static string Load(string file)
        {
            string folder = FolderOverride ?? (Application.isEditor ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", RepositoryFolder)) : null);
            if (folder != null)
            {
                string path = Path.Combine(folder, file);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            return Resources.Load<TextAsset>(ResourcesFolder + "/" + Path.GetFileNameWithoutExtension(file))?.text;
        }

        static string[] Lines(string file)
        {
            if (lines == null) Preload();
            return lines.TryGetValue(file ?? "", out var l) ? l : null;
        }

        /// <summary>A guide file's name as Files spells it (case and a missing ".md" forgiven), else null.</summary>
        public static string Find(string file)
        {
            if (string.IsNullOrWhiteSpace(file)) return null;
            string f = file.Trim().Replace('\\', '/');
            f = f.Substring(f.LastIndexOf('/') + 1);
            foreach (var (name, _) in Files)
                if (string.Equals(name, f, StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileNameWithoutExtension(name), f, StringComparison.OrdinalIgnoreCase))
                    return name;
            return null;
        }

        /// <summary>Whether the file is there (a build without the guide has none).</summary>
        public static bool Has(string file) => Lines(file) != null;

        /// <summary>A file's whole text, null when it isn't there.</summary>
        public static string Text(string file)
        {
            var l = Lines(file);
            return l != null ? string.Join("\n", l) : null;
        }

        /// <summary>A file's size in characters (0: not there).</summary>
        public static int Size(string file) => Lines(file)?.Sum(x => x.Length + 1) ?? 0;

        /// <summary>A file's headings: (line, level, title), outside code blocks.</summary>
        public static List<(int line, int level, string title)> Headings(string file)
        {
            var result = new List<(int, int, string)>();
            var l = Lines(file);
            if (l == null) return result;
            bool code = false;
            for (int i = 0; i < l.Length; i++)
            {
                string s = l[i];
                if (s.StartsWith("```")) { code = !code; continue; }
                if (code || !s.StartsWith("#")) continue;
                int level = 0;
                while (level < s.Length && s[level] == '#') level++;
                if (level > 6 || level >= s.Length || s[level] != ' ') continue;
                result.Add((i + 1, level, s.Substring(level).Trim()));
            }
            return result;
        }

        /// <summary>The section under the heading 'title' (the same text, else the first heading containing it, any case) up to
        /// the next heading of its level or above; null when no heading matches.</summary>
        public static string Section(string file, string title, out int fromLine)
        {
            fromLine = 0;
            var l = Lines(file);
            if (l == null || string.IsNullOrWhiteSpace(title)) return null;
            string want = title.Trim().TrimStart('#').Trim();
            var h = Headings(file);
            int at = h.FindIndex(x => string.Equals(x.title, want, StringComparison.OrdinalIgnoreCase));
            if (at < 0) at = h.FindIndex(x => x.title.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0);
            if (at < 0) return null;
            int end = l.Length;
            for (int k = at + 1; k < h.Count; k++)
                if (h[k].level <= h[at].level) { end = h[k].line - 1; break; }
            fromLine = h[at].line;
            return Range(file, fromLine, end - fromLine + 1);
        }

        /// <summary>'count' lines from 'line' (1-based), cut at MaxChars with a note where it goes on.</summary>
        public static string Range(string file, int line, int count)
        {
            var l = Lines(file);
            if (l == null) return null;
            int from = Mathf.Clamp(line, 1, Math.Max(1, l.Length)) - 1;
            int to = Math.Min(l.Length, from + Math.Max(1, count));
            var sb = new StringBuilder();
            for (int i = from; i < to; i++)
            {
                if (sb.Length + l[i].Length + 1 > MaxChars && sb.Length > 0)
                {
                    sb.Append($"\n[... cut at {MaxChars} characters: it goes on at line {i + 1} of {l.Length}]");
                    return sb.ToString();
                }
                sb.Append(l[i]).Append('\n');
            }
            if (to < l.Length && count > 0 && to == from + count) sb.Append($"[line {to} of {l.Length}]");
            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>Lines holding every word of 'query' (any case) in one file or all: the file, the line, the heading it is
        /// under and the text (around the first word in a long line), at most 'max'.</summary>
        public static List<(string file, int line, string heading, string text)> Search(string query, string file = null, int max = 30)
        {
            var hits = new List<(string, int, string, string)>();
            var words = (query ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return hits;
            foreach (var (name, _) in Files)
            {
                if (file != null && name != file) continue;
                var l = Lines(name);
                if (l == null) continue;
                var h = Headings(name);
                int hi = -1;
                for (int i = 0; i < l.Length && hits.Count < max; i++)
                {
                    while (hi + 1 < h.Count && h[hi + 1].line <= i + 1) hi++;
                    string s = l[i];
                    if (!words.All(w => s.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                    hits.Add((name, i + 1, hi >= 0 ? h[hi].title : "", Around(s, words[0], 400)));
                }
            }
            return hits;
        }

        /// <summary>A long line cut to 'width' characters around 'word'.</summary>
        static string Around(string s, string word, int width)
        {
            s = s.Trim();
            if (s.Length <= width) return s;
            int at = Math.Max(0, s.IndexOf(word, StringComparison.OrdinalIgnoreCase));
            int start = Mathf.Clamp(at - width / 3, 0, s.Length - width);
            return (start > 0 ? "…" : "") + s.Substring(start, width) + (start + width < s.Length ? "…" : "");
        }
    }
}
