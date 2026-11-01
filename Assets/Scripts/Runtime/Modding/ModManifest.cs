// ModManifest.cs
// mod.json, the file that makes a folder or archive a mod:
//   {
//     "id": "plasma_arsenal",            required: a-z, 0-9, _ and -, at most 40; the mod's lasting name (saves, the
//                                        multiplayer check and other mods refer to it: never change it)
//     "name": "Plasma Arsenal",          shown in the mod browser (or { "en": "...", "de": "..." })
//     "version": "1.2",                  shown, and compared in multiplayer
//     "author": "Someone",
//     "description": "What it adds.",    shown in the browser (or per language)
//     "preview": "preview.png",          the browser's image (PNG / JPG, 16:9 looks best); default preview.png
//     "website": "https://...",
//     "credits": "Model: ... (license)",  shown in the browser (or per language): who made what the mod uses
//     "shipTag": "GoF3",                 a dim tag after the names of the mod's new ships in the hangar and shop lists
//                                        (or per language); a ship's own "tag" in ships.json wins (ModContent.ShipTag)
//     "dependencies": ["other_mod", "ship_pack>=1.2"]   mods that must be on too (and are applied first), with an
//                                        optional lowest version; their content can be used by its key ("ship_pack:falcon":
//                                        an item's / ship's "override" or "base", the event graphs' ship, item and station
//                                        names), the same whatever number it got in this game
//     "multiplayerIgnore": ["parts/", "maps/*.png", "preview.png"]
//                                        files multiplayer doesn't compare (ModInfo.NetHash): what only changes how the mod
//                                        looks (models, textures, icons) or isn't read by the game (sources, notes), so a
//                                        lighter phone build plays with the PC one; "folder/" = everything in it, "*" any
//                                        characters. The data always counts: mod.json's id / version / dependencies / this
//                                        list, the .json files at the mod's root and everything in events/
//   }

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace GoF2Remake.Modding
{
    public class ModManifest
    {
        public const string FileName = "mod.json";
        public const int MaxIdLength = 40;
        static readonly Regex IdPattern = new Regex("^[a-z0-9_-]+$");

        public string id, version = "1.0", author, website, preview = "preview.png";
        public Dictionary<string, string> name, description, credits;
        /// <summary>Remake (TorchOP's request, 2026-10-23): the tag shown after the names of the mod's new ships, null = none.</summary>
        public Dictionary<string, string> shipTag;
        public List<string> dependencies = new List<string>();
        /// <summary>Files left out of the multiplayer check (see the header): "folder/", or a pattern with "*".</summary>
        public List<string> multiplayerIgnore = new List<string>();
        Regex ignorePattern;
        /// <summary>The lowest version a dependency must have (by id; only those that give one).</summary>
        public Dictionary<string, string> dependencyVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string Name => Pick(name) ?? id;
        public string Description => Pick(description) ?? "";
        public string Credits => Pick(credits) ?? "";

        /// <summary>The current language's text, else English, else the plain one, else the first.</summary>
        public static string Pick(Dictionary<string, string> t)
        {
            if (t == null || t.Count == 0) return null;
            if (t.TryGetValue(Data.Localization.Language, out var s) || t.TryGetValue("en", out s) || t.TryGetValue("", out s)) return s;
            foreach (var v in t.Values) return v;
            return null;
        }

        /// <summary>Version a is at least b, compared number by number ("1.10" > "1.9"; other parts as text).</summary>
        public static bool VersionAtLeast(string a, string b)
        {
            var x = (a ?? "0").Split('.', '-', ' ');
            var y = (b ?? "0").Split('.', '-', ' ');
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                string p = i < x.Length ? x[i] : "0", q = i < y.Length ? y[i] : "0";
                int c = int.TryParse(p, out int pi) && int.TryParse(q, out int qi) ? pi.CompareTo(qi) : string.Compare(p, q, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c > 0;
            }
            return true;
        }

        /// <summary>The dependency 'mod' is too old for this mod (null: it is fine).</summary>
        public string TooOld(ModInfo mod)
        {
            if (mod == null || !dependencyVersions.TryGetValue(mod.Id, out string need)) return null;
            return VersionAtLeast(mod.Version, need) ? null : need;
        }

        /// <summary>Whether multiplayer leaves a file out of its check (a path inside the mod, '/' separated). Never the data:
        /// mod.json (its id / version / dependencies count by themselves), the .json files at the root, anything in events/.</summary>
        public bool MultiplayerIgnores(string file)
        {
            if (multiplayerIgnore == null || multiplayerIgnore.Count == 0) return false;
            string f = file.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
            if (f.StartsWith("events/", StringComparison.Ordinal)) return false;
            if (f.IndexOf('/') < 0 && f.EndsWith(".json", StringComparison.Ordinal)) return false;
            if (ignorePattern == null)
            {
                var alts = new List<string>();
                foreach (var raw in multiplayerIgnore)
                {
                    string p = (raw ?? "").Replace('\\', '/').Trim().TrimStart('/').ToLowerInvariant();
                    if (p.Length == 0) continue;
                    if (p.EndsWith("/")) p += "*";
                    alts.Add(Regex.Escape(p).Replace("\\*", ".*"));
                }
                ignorePattern = new Regex(alts.Count == 0 ? "(?!)" : "^(?:" + string.Join("|", alts) + ")$", RegexOptions.CultureInvariant);
            }
            return ignorePattern.IsMatch(f);
        }

        public static bool ValidId(string id) => !string.IsNullOrEmpty(id) && id.Length <= MaxIdLength && IdPattern.IsMatch(id);

        /// <summary>mod.json read; throws ModJsonException on a broken file or a missing / invalid id.</summary>
        public static ModManifest Read(ModSource source)
        {
            if (!(ModJson.Read(source, FileName) is JObject o)) throw new ModJsonException($"{FileName}: must be an object {{ ... }}");
            var m = new ModManifest
            {
                id = ModJson.Str(o, "id"),
                name = ModJson.Text(o, "name"),
                description = ModJson.Text(o, "description"),
                credits = ModJson.Text(o, "credits"),
                shipTag = ModJson.Text(o, "shipTag"),
                version = ModJson.Str(o, "version", "1.0"),
                author = ModJson.Str(o, "author"),
                website = ModJson.Str(o, "website"),
                preview = ModJson.Str(o, "preview", "preview.png"),
                dependencies = ModJson.Strings(o, "dependencies"),
                multiplayerIgnore = ModJson.Strings(o, "multiplayerIgnore"),
            };
            // "id>=version": the id in the list, the version aside.
            for (int i = 0; i < m.dependencies.Count; i++)
            {
                string d = m.dependencies[i].Trim();
                int ge = d.IndexOf(">=", StringComparison.Ordinal);
                if (ge > 0) { m.dependencyVersions[d.Substring(0, ge).Trim()] = d.Substring(ge + 2).Trim(); d = d.Substring(0, ge).Trim(); }
                m.dependencies[i] = d;
            }
            if (string.IsNullOrEmpty(m.id)) throw new ModJsonException($"{FileName}: \"id\" is missing");
            if (!ValidId(m.id))
                throw new ModJsonException($"{FileName}: the id \"{m.id}\" may only use a-z, 0-9, _ and - (at most {MaxIdLength})");
            return m;
        }
    }
}
