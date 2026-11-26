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
//     "tag": "GoF3",                     a dim tag after the names of what the mod adds (ships, items, systems,
//                                        stations) in the lists and on the star map (or per language; ModContent.TagOf)
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
//     "multipleSecondaryMounts": true   opt in to duplicate secondary hardpoints (shared ammo), off by default; part of
//                                        the multiplayer mod signature
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
        /// <summary>Opt-in gameplay feature: fit one secondary item to multiple secondary hardpoints. Off by default.</summary>
        public bool multipleSecondaryMounts;
        /// <summary>Opt in to rank- and difficulty-scaled shields for ordinary fighter NPCs.</summary>
        public bool fighterNpcShields;
        /// <summary>-1: default (five seconds with fighter NPC shields, otherwise vanilla); 0: no delay.</summary>
        public int shieldRechargeDelayMs = -1;
        /// <summary>Target signature by canonical ship class, as a percentage of the observing scanner range.</summary>
        public Dictionary<string, int> scannerClassDetectionPercent = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        /// <summary>-1: default 80%; 100 disables the fitted generator signature bonus.</summary>
        public int scannerGeneratorDetectionPercent = -1;
        public bool npcScannerDetection;
        public Dictionary<string, int> npcScannerRangeUnits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> name, description, credits;
        /// <summary>Remake (TorchOP's request, 2026-10-23): the tag shown after the names of what the mod adds, null = none.</summary>
        public Dictionary<string, string> tag;
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
                tag = ModJson.Text(o, "tag"),
                version = ModJson.Str(o, "version", "1.0"),
                author = ModJson.Str(o, "author"),
                website = ModJson.Str(o, "website"),
                preview = ModJson.Str(o, "preview", "preview.png"),
                multipleSecondaryMounts = ModJson.Bool(o, "multipleSecondaryMounts", false, FileName),
                fighterNpcShields = ModJson.Bool(o, "fighterNpcShields", false, FileName),
                npcScannerDetection = ModJson.Bool(o, "npcScannerDetection", false, FileName),
                shieldRechargeDelayMs = Math.Max(-1, Math.Min(60000, ModJson.Int(o, "shieldRechargeDelayMs", -1, FileName))),
                dependencies = ModJson.Strings(o, "dependencies"),
                multiplayerIgnore = ModJson.Strings(o, "multiplayerIgnore"),
            };
            if (ModJson.Get(o, "npcScannerRangeUnits") is JToken ranges)
            {
                if (!(ranges is JObject classesByRange)) throw new ModJsonException($"{FileName}: npcScannerRangeUnits must be an object");
                foreach (var entry in classesByRange.Properties())
                {
                    if (!ShipClassRules.IsCanonicalClass(entry.Name) || entry.Value.Type != JTokenType.Integer ||
                        (long)entry.Value < 1 || (long)entry.Value > int.MaxValue)
                        throw new ModJsonException($"{ModJson.Where(entry, FileName)}: npcScannerRangeUnits requires a canonical class and positive integer range");
                    m.npcScannerRangeUnits[entry.Name.ToLowerInvariant()] = (int)entry.Value;
                }
            }
            if (ModJson.Get(o, "scannerGeneratorDetectionPercent") is JToken generator)
            {
                if (generator.Type != JTokenType.Integer || (long)generator < 1 || (long)generator > 100)
                    throw new ModJsonException($"{ModJson.Where(generator, FileName)}: scannerGeneratorDetectionPercent must be an integer from 1 to 100");
                m.scannerGeneratorDetectionPercent = (int)generator;
            }
            if (ModJson.Get(o, "scannerClassDetectionPercent") is JToken detection)
            {
                if (!(detection is JObject classes)) throw new ModJsonException($"{FileName}: scannerClassDetectionPercent must be an object");
                foreach (var entry in classes.Properties())
                {
                    if (!ShipClassRules.IsCanonicalClass(entry.Name)) throw new ModJsonException($"{ModJson.Where(entry, FileName)}: unknown scanner target class {entry.Name}");
                    if (entry.Value.Type != JTokenType.Integer || (long)entry.Value < 1 || (long)entry.Value > 1000)
                        throw new ModJsonException($"{ModJson.Where(entry, FileName)}: detection percentage must be an integer from 1 to 1000");
                    m.scannerClassDetectionPercent[entry.Name.ToLowerInvariant()] = (int)entry.Value;
                }
            }
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
