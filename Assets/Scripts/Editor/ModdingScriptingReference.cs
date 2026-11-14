// ModdingScriptingReference.cs
// Part of GoF2 > Build > Modding AI Reference: Modding/ai/gof2-modding/scripting.md, the mod scripts' reference (Lua,
// Modding.ModScripts): how script files run, the "game" table (ModScripts.ApiHelp, the text the Mod Builder's editor
// shows), the hook classes with each delegate's signature and its summary from the source (GameHooks, NetSession,
// SpaceHooks, UiHooks: what game.hook most often takes), the members of the classes scripts use most
// (ScriptAccess.Describe, the same lines game.describe prints) and the name of every class scripts can reach. Made from
// the code, so it follows the game: a renamed member shows here after the next build of the reference.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using GoF2Remake.Modding;

namespace GoF2Remake.EditorTools
{
    public static class ModdingScriptingReference
    {
        /// <summary>The hook classes and their source files.</summary>
        static readonly (string name, string file)[] HookClasses =
        {
            ("GameHooks", "Assets/Scripts/Runtime/Game/GameHooks.cs"), ("SpaceHooks", "Assets/Scripts/Runtime/Space/World/SpaceHooks.cs"),
            ("UiHooks", "Assets/Scripts/Runtime/Space/UI/UiHooks.cs"), ("NetSession", "Assets/Scripts/Runtime/Game/NetSession.cs"),
        };

        /// <summary>The classes listed with their members (the others by name only: game.describe prints any of them).</summary>
        static readonly string[] MainClasses =
        {
            "Session", "Database", "ItemData", "ShipData", "SystemData", "StationData", "Agent", "FreelanceMission", "Settings", "Localization",
            "Standing", "Achievements", "SpaceLevel", "StationLevel", "Traffic", "NpcShip", "SpawnSpec", "Target", "PlayerHealth", "ShipController",
            "Gun", "Navigation", "ModManager", "ModInfo",
        };

        public static string Build()
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!-- Made by GoF2 > Build > Modding AI Reference (ModdingScriptingReference) from the game's code: don't edit. -->");
            sb.AppendLine();
            sb.AppendLine("# Mod scripts (Lua): reference");
            sb.AppendLine();
            sb.AppendLine("A mod may carry Lua scripts in `scripts/` (Lua 5.2, run by MoonSharp). Where the JSON files and the mission");
            sb.AppendLine("graphs describe content, a script is code: it hears the game's events, reads and changes the game's own objects");
            sb.AppendLine("and can replace the functions the game's parts call each other through. `guide.md` \"Scripts\" explains it with");
            sb.AppendLine("examples; this file is the reference. Rules that decide whether a script works:");
            sb.AppendLine();
            sb.AppendLine("- Every `.lua` directly in `scripts/` runs when the mod starts (`main.lua` first, the others by name). Files in");
            sb.AppendLine("  subfolders run only through `require(\"lib.util\")` (= `scripts/lib/util.lua`).");
            sb.AppendLine("- One Lua state per mod. Globals are the mod's own; other mods are reached with `game.emit` / `game.on`.");
            sb.AppendLine("- Scripts start in the main menu already and keep running through scene changes. They start again when the");
            sb.AppendLine("  active mods change. Keep what must survive in `game.saved` (it is stored in the player's save file).");
            sb.AppendLine("- C# members are used with their C# names: `Session.Credits`, `db:Item(5)` (`:` for methods of an object,");
            sb.AppendLine("  `.` for statics and fields). Every class listed at the end is a global by its name. C# lists and");
            sb.AppendLine("  dictionaries are objects, not Lua tables: `list.Count`, `game.get(list, 0)`, `game.table(list)` for a Lua");
            sb.AppendLine("  copy. Arrays arrive as Lua tables (from 1). A Lua function works wherever a delegate is taken.");
            sb.AppendLine("- The lists below are the public members. Private ones are reached with `game.field` / `game.setField` /");
            sb.AppendLine("  `game.call` (`print(game.describe(x, true))` lists them). No `io`, no `os` but the clock, no .NET, no");
            sb.AppendLine("  files but the mod's own.");
            sb.AppendLine("- A script error never stops the game: it is a warning of the mod (the Mods screen, the Mod Builder's console).");
            sb.AppendLine("- In multiplayer the scripts run in every player's game. `game.multiplayer()` says which one this is; only the");
            sb.AppendLine("  host's game may run `game.command`.");
            sb.AppendLine();
            sb.AppendLine("## The game table");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(ModScripts.ApiHelp.Replace("\r\n", "\n"));
            sb.AppendLine("```");
            sb.AppendLine();

            sb.AppendLine("## Hook classes");
            sb.AppendLine();
            sb.AppendLine("The game's layers call each other through these static delegates. `game.hook(\"Class\", \"Member\", function(original, ...) end)`");
            sb.AppendLine("replaces one (call `original(...)` for the game's own behaviour and return what the game should get);");
            sb.AppendLine("`game.listen` adds a function beside it. Both are undone when the script stops.");
            sb.AppendLine();
            foreach (var (name, file) in HookClasses)
            {
                var type = ScriptAccess.Find(name, out _);
                if (type == null) continue;
                var summaries = Summaries(file);
                sb.AppendLine("### " + name);
                sb.AppendLine();
                foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Static).OrderBy(f => f.MetadataToken))
                {
                    if (!typeof(Delegate).IsAssignableFrom(f.FieldType)) continue;
                    var invoke = f.FieldType.GetMethod("Invoke");
                    bool usable = ScriptAccess.Usable(f.FieldType);
                    string signature = "(" + string.Join(", ", invoke.GetParameters().Select(p => (p.IsOut ? "out " : "") + p.Name + ": " + ScriptAccess.TypeName(p.ParameterType))) + ")"
                                       + (invoke.ReturnType == typeof(void) ? "" : ": " + ScriptAccess.TypeName(invoke.ReturnType));
                    sb.Append("- `").Append(f.Name).Append(signature).Append('`');
                    if (!usable) sb.Append(" (not for scripts: an argument scripts can't hold)");
                    else if (invoke.GetParameters().Any(p => p.ParameterType.IsByRef)) sb.Append(" (a hook returns the out values after its result)");
                    if (summaries.TryGetValue(f.Name, out string about)) sb.Append(": ").Append(about);
                    sb.AppendLine();
                }
                sb.AppendLine();
            }

            sb.AppendLine("## The classes scripts use most");
            sb.AppendLine();
            sb.AppendLine("`static` members are used on the class (`Session.Credits`), the others on an object of it. `name: type` is a field or");
            sb.AppendLine("property, `Name(arguments): result` a method. In a script, `print(game.describe(\"Class\"))` prints the same for any class.");
            sb.AppendLine();
            foreach (string name in MainClasses)
            {
                var type = ScriptAccess.Find(name, out _);
                if (type == null) continue;
                sb.AppendLine("### " + name + " (`" + type.FullName.Replace('+', '.') + "`)");
                sb.AppendLine();
                sb.AppendLine("```");
                foreach (string line in ScriptAccess.Describe(type)) sb.AppendLine(line);
                sb.AppendLine("```");
                sb.AppendLine();
            }

            sb.AppendLine("## Every class scripts can reach");
            sb.AppendLine();
            sb.AppendLine("By namespace; `import(\"Name\")` (or the full name where two classes share one) gives the class. The game's:");
            sb.AppendLine();
            var game = ScriptAccess.AllowedTypes(true).Where(t => !t.IsNested).ToList();
            foreach (var group in game.GroupBy(t => t.Namespace ?? "").OrderBy(g => g.Key, StringComparer.Ordinal))
                sb.AppendLine("- `" + group.Key + "`: " + string.Join(", ", group.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal))).AppendLine();
            sb.AppendLine("Unity's: the public classes of these namespaces (https://docs.unity3d.com/ScriptReference/), except those that");
            sb.AppendLine("reach outside the game (Application, PlayerPrefs, the clipboard, asset bundles, web requests, the camera and microphone):");
            sb.AppendLine();
            var own = new HashSet<Type>(ScriptAccess.AllowedTypes(true));
            var unity = ScriptAccess.AllowedTypes().Where(t => !own.Contains(t) && !t.IsNested).ToList();
            foreach (var group in unity.GroupBy(t => t.Namespace ?? "").OrderBy(g => g.Key, StringComparer.Ordinal))
                sb.AppendLine("- `" + group.Key + "` (" + group.Count() + " classes)");
            return sb.ToString();
        }

        /// <summary>The summary comment above each public static member of a hook class's source file, by member name
        /// ("A, B, C;" declarations give each name the same text).</summary>
        static Dictionary<string, string> Summaries(string file)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!File.Exists(file)) return map;
            var comment = new StringBuilder();
            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw.Trim();
                if (line.StartsWith("///", StringComparison.Ordinal)) { comment.Append(' ').Append(line.Substring(3).Trim()); continue; }
                if (line.StartsWith("public static", StringComparison.Ordinal) && comment.Length > 0)
                {
                    string about = Regex.Replace(comment.ToString(), "<[^>]+>", "").Replace("&lt;", "<").Replace("&gt;", ">").Trim();
                    about = Regex.Replace(about, "\\s+", " ");
                    // The declared names: "public static Func<int, int> A, B;" -> what follows the last '>' (else the type word).
                    string head = line.Split(new[] { ';', '=', '{' }, 2)[0];
                    int close = head.LastIndexOf('>');
                    string tail = close >= 0 ? head.Substring(close + 1) : string.Join(" ", head.Split(' ').Skip(3));
                    foreach (Match m in Regex.Matches(tail, "[A-Za-z_][A-Za-z0-9_]*")) map[m.Value] = about;
                }
                if (!line.StartsWith("[", StringComparison.Ordinal)) comment.Clear();
            }
            return map;
        }
    }
}
