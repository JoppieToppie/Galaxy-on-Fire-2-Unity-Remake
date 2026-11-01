// ModBuilderMcpGuide.cs
// The Mod Builder MCP server's copy of the AI modding guide (part of ModBuilderMcp; the texts: Modding.ModdingGuide), so an
// assistant connected to the game reads the formats without the repository or a skill set up beforehand:
//   read_guide       the guide's files in parts: the list (no arguments), a file's outline of headings (a short file whole),
//                    one section, a search over one file or all, or a range of lines; at most ModdingGuide.MaxChars a call
//   resources        the same files as MCP resources (resources/list, resources/read: gof2-guide://<file>), whole, for the
//                    clients that attach resources themselves; answered on the HTTP thread from the texts Start preloaded

using System.Collections.Generic;
using System.Linq;
using System.Text;
using GoF2Remake.Modding;
using Newtonsoft.Json.Linq;

namespace GoF2Remake.UI
{
    public static partial class ModBuilderMcp
    {
        const string GuideScheme = "gof2-guide://";

        static List<Tool> GuideTools() => new List<Tool>
        {
            new Tool
            {
                name = "read_guide", title = "Read the modding guide", readOnly = true,
                description = "The game's modding guide for AI assistants: the mod file formats, rules, balancing numbers and every original number and name a mod refers to. Without arguments: its files (start with SKILL.md). With 'file': that file's outline of headings with their line numbers (a short file whole); add 'section' for the text under one heading, or 'line' (and 'lines') for a range. 'search' finds the lines holding every word, in 'file' or in every file, with the line and heading of each. Answers are cut at about 40,000 characters, saying where the text goes on.",
                input = Input(("file", Enum(ModdingGuide.Files.Select(f => f.file).ToArray()), "the guide file", false),
                              ("section", "string", "a heading of the file (its words; the first heading containing them)", false),
                              ("search", "string", "words to find (every one on the same line, any case)", false),
                              ("line", "integer", "the first line of a range (1-based)", false), ("lines", "integer", "how many lines (default 200)", false)),
                run = ReadGuide,
            },
        };

        static void ReadGuide(JObject a, Call c)
        {
            string fileArg = Str(a, "file"), section = Str(a, "section"), search = Str(a, "search");
            string file = ModdingGuide.Find(fileArg);
            if (fileArg != null && file == null) { c.Fail($"No guide file \"{fileArg}\": {string.Join(", ", ModdingGuide.Files.Select(f => f.file))}."); return; }
            if (file != null && !ModdingGuide.Has(file)) { c.Fail($"This game has no {file} (a build made without the guide)."); return; }
            if (!string.IsNullOrWhiteSpace(search))
            {
                var hits = ModdingGuide.Search(search, file, 40);
                if (hits.Count == 0) { c.Text($"Nothing in {file ?? "the guide"} holds every word of \"{search}\": try fewer or other words."); return; }
                var sb = new StringBuilder($"{hits.Count}{(hits.Count == 40 ? "+" : "")} line(s) with \"{search}\" (read one with file + line, or file + section):\n");
                foreach (var (f, line, heading, text) in hits)
                    sb.Append($"\n{f}:{line}{(heading.Length > 0 ? " [" + heading + "]" : "")}  {text}");
                c.Text(sb.ToString());
                return;
            }
            if (file == null)
            {
                var sb = new StringBuilder("The modding guide (read_guide with 'file'; start with SKILL.md):\n");
                foreach (var (f, about) in ModdingGuide.Files)
                {
                    int size = ModdingGuide.Size(f);
                    sb.Append($"\n- {f} ({(size > 0 ? $"{(size + 512) / 1024} KB" : "not in this game")}): {about}");
                }
                c.Text(sb.ToString());
                return;
            }
            if (!string.IsNullOrWhiteSpace(section))
            {
                string text = ModdingGuide.Section(file, section, out int from);
                if (text == null) { c.Fail($"{file} has no heading with \"{section}\": read_guide with just the file lists them."); return; }
                c.Text($"{file}, line {from}:\n\n{text}");
                return;
            }
            if (a["line"] != null)
            {
                int line = Int(a, "line", 1);
                c.Text($"{file}, from line {line}:\n\n{ModdingGuide.Range(file, line, Int(a, "lines", 200))}");
                return;
            }
            if (ModdingGuide.Size(file) <= ModdingGuide.MaxChars) { c.Text(ModdingGuide.Text(file)); return; }
            var outline = new StringBuilder($"{file} ({(ModdingGuide.Size(file) + 512) / 1024} KB) is long: read a section ('section': a heading's words) or lines ('line'). Its headings (line: heading):\n");
            foreach (var (line, level, title) in ModdingGuide.Headings(file))
            {
                string row = $"\n{line}: {new string(' ', (level - 1) * 2)}{title}";
                if (outline.Length + row.Length > ModdingGuide.MaxChars) { outline.Append("\n[... more headings: search finds the rest]"); break; }
                outline.Append(row);
            }
            c.Text(outline.ToString());
        }

        static string GuideMime(string file) => file.EndsWith(".json") ? "application/json" : "text/markdown";

        /// <summary>resources/list: the guide's files.</summary>
        static JObject GuideResources() => new JObject
        {
            ["resources"] = new JArray(ModdingGuide.Files.Where(f => ModdingGuide.Has(f.file)).Select(f => new JObject
            {
                ["uri"] = GuideScheme + f.file, ["name"] = f.file, ["title"] = "Modding guide: " + f.file, ["description"] = f.about,
                ["mimeType"] = GuideMime(f.file), ["size"] = ModdingGuide.Size(f.file),
            })),
        };

        /// <summary>resources/read: one file whole; null when the uri names none.</summary>
        static JObject GuideResource(string uri)
        {
            string file = uri != null && uri.StartsWith(GuideScheme) ? ModdingGuide.Find(uri.Substring(GuideScheme.Length)) : null;
            string text = file != null ? ModdingGuide.Text(file) : null;
            if (text == null) return null;
            return new JObject { ["contents"] = new JArray(new JObject { ["uri"] = GuideScheme + file, ["mimeType"] = GuideMime(file), ["text"] = text }) };
        }
    }
}
