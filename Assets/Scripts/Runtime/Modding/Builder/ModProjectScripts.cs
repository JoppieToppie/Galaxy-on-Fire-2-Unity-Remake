// ModProjectScripts.cs
// Part of ModProject (the Mod Builder's project): its Lua script files (scripts/*.lua and its subfolders; ModScripts
// runs them, docs/notes/mods.md "Mod scripts"). Plain files in the project folder, named by their path under scripts/
// ("main.lua", "lib/util.lua"); the undo history tracks the folder (ModProjectHistory.TrackedFolders).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GoF2Remake.Modding
{
    public partial class ModProject
    {
        /// <summary>What a new project's first script starts as.</summary>
        public const string ScriptTemplate =
            "-- main.lua runs when the mod starts. Every .lua file directly in scripts/ runs too;\n" +
            "-- files in subfolders are modules: local util = require(\"lib.util\")\n\n" +
            "game.log(\"Hello from \" .. game.mod.name)\n\n" +
            "game.on(\"station\", function(level)\n" +
            "    game.message(\"Docked with \" .. Session.Credits .. \" credits\")\n" +
            "end)\n";

        string ScriptsDir => Path.Combine(Folder, ModScripts.Folder);

        /// <summary>The script files by their path under scripts/ ("main.lua", "lib/util.lua"): main.lua first, then the
        /// files that run at start, then the modules in subfolders.</summary>
        public List<string> ScriptFiles()
        {
            if (!Directory.Exists(ScriptsDir)) return new List<string>();
            return Directory.GetFiles(ScriptsDir, "*" + ModScripts.Extension, SearchOption.AllDirectories)
                .Select(f => f.Substring(ScriptsDir.Length).Replace('\\', '/').TrimStart('/'))
                .OrderBy(n => string.Equals(n, ModScripts.MainFile, StringComparison.OrdinalIgnoreCase) ? 0 : n.Contains("/") ? 2 : 1)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>A script's place in the project: only inside scripts/, null for a name that leaves it.</summary>
        string ScriptPath(string name)
        {
            name = (name ?? "").Replace('\\', '/').Trim('/');
            if (name.Length == 0 || name.Split('/').Any(p => p == ".." || p == "." || p.Length == 0)) return null;
            return Path.Combine(ScriptsDir, name.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>A script's text ("" when it can't be read).</summary>
        public string ReadScript(string name)
        {
            string path = ScriptPath(name);
            try { return path != null && File.Exists(path) ? File.ReadAllText(path) : ""; }
            catch (IOException) { return ""; }
        }

        /// <summary>The script's text written (UTF-8 without a byte order mark); false with nothing changed on disk.</summary>
        public bool WriteScript(string name, string text)
        {
            string path = ScriptPath(name);
            if (path == null) return false;
            text ??= "";
            if (File.Exists(path) && File.ReadAllText(path) == text) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return true;
        }

        /// <summary>What the player typed as a script's name, as its path under scripts/: lower case letters, digits, _ and
        /// -, folders with /, ".lua" at the end ("Lib/My Util" -> "lib/my_util.lua"); null when nothing is left. No dots in
        /// the name: require reads them as folders.</summary>
        public static string ScriptName(string typed)
        {
            typed = (typed ?? "").Trim().Replace('\\', '/');
            if (typed.EndsWith(ModScripts.Extension, StringComparison.OrdinalIgnoreCase)) typed = typed.Substring(0, typed.Length - ModScripts.Extension.Length);
            var parts = new List<string>();
            foreach (string part in typed.Split('/'))
            {
                var sb = new StringBuilder();
                foreach (char c in part.Trim().ToLowerInvariant())
                    sb.Append(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_' || c == '-' ? c : c == ' ' || c == '.' ? '_' : '\0');
                string clean = sb.ToString().Replace("\0", "").Trim('_');
                if (clean.Length > 0) parts.Add(clean);
            }
            return parts.Count == 0 ? null : string.Join("/", parts) + ModScripts.Extension;
        }

        /// <summary>A new script (the first one: main.lua with the template, whatever was typed); its name, null with the
        /// reason when the name is no good or taken.</summary>
        public string AddScript(string typed, out string error)
        {
            error = null;
            var have = ScriptFiles();
            string name = have.Count == 0 && string.IsNullOrWhiteSpace(typed) ? ModScripts.MainFile : ScriptName(typed);
            if (name == null) { error = "A script needs a name: letters, digits, _ and -, folders with /."; return null; }
            if (have.Any(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase))) { error = $"There is a script \"{name}\" already."; return null; }
            WriteScript(name, string.Equals(name, ModScripts.MainFile, StringComparison.OrdinalIgnoreCase) ? ScriptTemplate
                : name.Contains("/") ? $"-- {name}: a module. Other scripts load it with require(\"{name.Substring(0, name.Length - ModScripts.Extension.Length).Replace('/', '.')}\").\n\nlocal M = {{}}\n\nreturn M\n"
                : $"-- {name} runs when the mod starts, after main.lua.\n");
            return name;
        }

        /// <summary>A script under another name (a move between folders too); the new name, null with the reason.</summary>
        public string RenameScript(string name, string typed, out string error)
        {
            error = null;
            string to = ScriptName(typed), from = ScriptPath(name);
            if (to == null) { error = "A script needs a name: letters, digits, _ and -, folders with /."; return null; }
            if (string.Equals(to, name, StringComparison.OrdinalIgnoreCase)) return name;
            if (ScriptFiles().Any(f => string.Equals(f, to, StringComparison.OrdinalIgnoreCase))) { error = $"There is a script \"{to}\" already."; return null; }
            if (from == null || !File.Exists(from)) { error = $"No script \"{name}\"."; return null; }
            string target = ScriptPath(to);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Move(from, target);
            PruneScriptFolders();
            return to;
        }

        public void RemoveScript(string name)
        {
            string path = ScriptPath(name);
            try { if (path != null && File.Exists(path)) File.Delete(path); } catch (IOException) { }
            PruneScriptFolders();
        }

        /// <summary>Empty folders under scripts/ (and scripts/ itself) go with their last file.</summary>
        void PruneScriptFolders()
        {
            if (!Directory.Exists(ScriptsDir)) return;
            foreach (string dir in Directory.GetDirectories(ScriptsDir, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).Append(ScriptsDir))
                try { if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch (IOException) { }
        }
    }
}
