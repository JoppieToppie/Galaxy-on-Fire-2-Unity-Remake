// ModBuilderMcpContent.cs
// The Mod Builder MCP server's tools for what the builder gained after the first tools (part of ModBuilderMcp):
//   search_content's "sound" and "voice"   the game's sound effects (Resources/GoF2ModBuilder/SoundNames, what a sounds/<name>
//                    replaces) and voice lines (StoryTable's pages and radio, the voice table, StoryAssets.VoiceNames: what a
//                    voices/<clip> or voices/<language>/<clip> replaces), with the project's own new ones
//   write_file of a models/<name>.aem   the game's own model format converted to .glb (Modding.AemImport), as the builder's
//                    pickers do; the answer gives its length for a ship's modelLength
//   optimize         the Optimize window (Modding.ModOptimizer): the project's models and textures made smaller, one undo step
//   import_mod       an installed mod as a new project (ModProject.CopyMod, as the projects page's Import)
//   replace_game_character   a character standing in for a story speaker, from that speaker's own portrait
//                    (Portrait.RenderSpeaker) and name, as the Characters kind's "Replace a game character"

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.UI
{
    public static partial class ModBuilderMcp
    {
        static List<Tool> ContentTools() => new List<Tool>
        {
            new Tool
            {
                name = "optimize", title = "Optimize models and textures",
                description = "Makes the project's .glb models and image files smaller, like the builder's Optimize window: textures scaled down to 'maxSize' (1024, 2048, 4096) and a model's opaque textures saved as JPEG at 'quality' (high 92, normal 85, small 70), 'triangles' the share of a model's triangles kept (1, 0.75, 0.5, 0.25; seams and open edges are kept). A file that wouldn't get smaller is left alone. With 'list' true it only lists the files it can work on, with their sizes and triangles. One undo step.",
                input = Input(("list", "boolean", "only list the files (nothing changes)", false),
                              ("files", new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" } }, "which files (paths as 'list' gives them; empty: every file of 1 MB or more)", false),
                              ("maxSize", "integer", "the largest texture side (default 1024)", false), ("quality", "integer", "JPEG quality 50..100 (default 85)", false),
                              ("jpeg", "boolean", "a model's opaque textures as JPEG (default true)", false), ("triangles", "number", "the share of triangles kept, 0.1..1 (default 1)", false)),
                run = Optimize, timeoutSeconds = 600f,
                ask = a => Flag(a, "list", false) ? null : "optimize the project's models and textures (smaller files; undo puts them back)",
            },
            new Tool
            {
                name = "import_mod", title = "Make a project of an installed mod",
                description = "Copies an installed mod (the Mod Browser's list: its id) into a new Mod Builder project, which keeps the mod's id so Export as mod replaces the installed copy. A project of that id is only replaced with 'replace' true. 'open' opens it at once (the open project is saved first).",
                input = Input(("id", "string", "the installed mod's id (empty: the list of installed mods)", false), ("replace", "boolean", "replace a project with that id", false),
                              ("open", "boolean", "open it once copied", false)),
                run = ImportMod, timeoutSeconds = 180f,
                ask = a => string.IsNullOrEmpty(Str(a, "id")) ? null : $"make a project of the installed mod \"{Str(a, "id")}\"{(Flag(a, "replace", false) ? ", replacing its project" : "")}",
            },
            new Tool
            {
                name = "replace_game_character", title = "Replace a story character",
                description = "Adds a character that stands in for one of the game's story speakers (search_content kind character lists them), starting from that speaker's own portrait (written to portraits/) and name, so it works at once: change its picture (write_file a portrait and set \"portrait\") or name with patch_entry. A speaker the project already replaces gives that character back.",
                input = Input(("speaker", "string", "the story speaker: its name or number", true), ("name", "string", "the character's name (default the speaker's)", false)),
                run = ReplaceGameCharacter, timeoutSeconds = 60f, ask = a => $"add a character replacing the story's {Str(a, "speaker")}",
            },
        };

        // ---- search_content's sounds and voice lines ----------------------------------------------------------------------

        /// <summary>search_content's kinds past ModRefs' own: the game's sound effects and voice lines.</summary>
        static readonly string[] AudioRefKinds = { "sound", "voice" };

        /// <summary>The game's sound effects as "folder/name" (what a mod's sounds/<name> replaces).</summary>
        static List<string> GameSoundLines()
        {
            var asset = Resources.Load<TextAsset>("GoF2ModBuilder/SoundNames");
            return asset == null ? new List<string>()
                : asset.text.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();
        }

        [Serializable] class VoiceTable { public List<VoiceEntry> items; }
        [Serializable] class VoiceEntry { public int text; public string voice; }

        static List<(string value, string name, string about)> AudioRefs(string kind)
        {
            var l = new List<(string, string, string)>();
            if (kind == "sound")
            {
                var game = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in GameSoundLines())
                {
                    int slash = line.IndexOf('/');
                    string name = slash > 0 ? line.Substring(slash + 1) : line;
                    game.Add(name);
                    l.Add((name, name, "game sound (" + (slash > 0 ? line.Substring(0, slash) : "") + "): sounds/" + name + ".ogg replaces it"));
                }
                if (Project != null)
                    foreach (var (name, _) in Project.AudioFiles(ModProject.SoundsFolder))
                        if (!game.Contains(name)) l.Add((name, name, "this project's new sound (a mission's Sound step plays it)"));
                return l;
            }
            // Voice lines: the story's pages and radio, then the voice table, then the rest of the game's clips.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Line(string clip, string step, int speaker, int text)
            {
                if (string.IsNullOrEmpty(clip) || !seen.Add(clip)) return;
                string who = speaker >= 0 ? StoryTable.OriginalSpeakerName(speaker) : "";
                string line = text >= 0 ? Localization.Get(text) : "";
                if (line.Length > 160) line = line.Substring(0, 157) + "…";
                l.Add((clip, string.IsNullOrEmpty(who) ? clip : who, (step != "" ? "step " + step + ": " : "") + line));
            }
            foreach (var s in StoryTable.Steps)
            {
                foreach (var p in s.briefing) Line(p.voice, s.index.ToString(), p.speaker, p.text);
                foreach (var p in s.success) Line(p.voice, s.index.ToString(), p.speaker, p.text);
                if (s.radio != null) foreach (var r in s.radio) Line(r.voice, s.index.ToString(), r.speaker, r.text);
            }
            var table = Resources.Load<TextAsset>("GoF2Data/voice_table");
            if (table != null)
            {
                var vt = JsonUtility.FromJson<VoiceTable>(table.text);
                if (vt?.items != null) foreach (var v in vt.items) Line(v.voice, "", -1, v.text);
            }
            var assets = StoryAssets.Load();
            if (assets != null) foreach (var n in assets.VoiceNames()) Line(n, "", -1, -1);
            if (Project != null)
            {
                void Own(string folder, string language)
                {
                    foreach (var (name, _) in Project.AudioFiles(folder))
                        if (!seen.Contains(name)) l.Add((name, name, $"this project's new line ({language})"));
                }
                Own(ModProject.VoicesFolder, "every language");
                foreach (var (code, langName) in LanguageTables.Offered) Own(ModProject.VoicesFolder + "/" + code, langName);
            }
            return l;
        }

        // ---- .aem models --------------------------------------------------------------------------------------------------

        /// <summary>write_file of an .aem (the game's own model format) into models/: converted to .glb beside where it was
        /// asked for, as the builder's model pickers do; the answer gives the .glb's path and its length (a ship's
        /// modelLength, game units).</summary>
        static void WriteAem(Call c, ModBuilderScreen screen, string rel, byte[] bytes)
        {
            if (!rel.Replace('\\', '/').StartsWith(ModProject.ModelsFolder + "/", StringComparison.OrdinalIgnoreCase)) { c.Fail("An .aem model goes into models/."); return; }
            if (!AemImport.Is(bytes)) { c.Fail("Not a Galaxy on Fire 2 model (.aem)."); return; }
            var model = AemImport.Read(bytes, out string error);
            if (model == null) { c.Fail("The .aem couldn't be read: " + error); return; }
            string glbRel = Path.ChangeExtension(rel, ".glb").Replace('\\', '/');
            string full = ProjectPath(glbRel, true, out string why);
            if (full == null) { c.Fail(why); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, AemImport.ToGlb(model, Path.GetFileNameWithoutExtension(rel)));
            screen.ExternalFilesChanged("converted " + rel);
            c.Wait(() => !screen.ReloadBusy, () => c.Json(new JObject
            {
                ["ok"] = true, ["path"] = glbRel, ["converted"] = "the .aem was converted to .glb (only the .glb is kept)",
                ["triangles"] = model.Triangles, ["lengthUnits"] = Math.Round(model.Length, 1),
                ["note"] = "A ship on it: \"model\": \"" + glbRel + "\" with \"modelLength\": " + Math.Round(model.Length, 1) + " keeps the size it had in the game.",
            }, "converted " + rel));
        }

        // ---- optimize -----------------------------------------------------------------------------------------------------

        static void Optimize(JObject a, Call c)
        {
            if (Project == null) { c.Fail("The Mod Builder isn't open."); return; }
            var candidates = ModOptimizer.Candidates(Project.Folder);
            long Size(string rel) { try { return new FileInfo(Path.Combine(Project.Folder, rel)).Length; } catch { return 0; } }
            if (Flag(a, "list", false))
            {
                var list = new JArray();
                foreach (var f in candidates)
                {
                    var o = new JObject { ["path"] = f, ["bytes"] = Size(f) };
                    if (f.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) o["triangles"] = ModOptimizer.CountTriangles(Path.Combine(Project.Folder, f));
                    list.Add(o);
                }
                c.Json(new JObject { ["files"] = list, ["modBytes"] = Project.ModSize() }, $"{list.Count} files");
                return;
            }
            var screen = Screen(out string why);
            if (screen == null) { c.Fail(why); return; }
            List<string> files;
            if (a["files"] is JArray picked && picked.Count > 0)
            {
                files = new List<string>();
                foreach (var t in picked)
                {
                    string f = ((string)t ?? "").Replace('\\', '/').Trim('/');
                    string match = candidates.FirstOrDefault(x => string.Equals(x, f, StringComparison.OrdinalIgnoreCase));
                    if (match == null) { c.Fail($"Not a file the optimizer works on: {f} (optimize with 'list' lists them)."); return; }
                    files.Add(match);
                }
            }
            else files = candidates.Where(f => Size(f) >= 1024 * 1024).ToList();
            if (files.Count == 0) { c.Text("Nothing to optimize: no model or image of 1 MB or more (name the files to do smaller ones)."); return; }
            var options = new ModOptimizer.Options
            {
                maxSize = Mathf.Clamp(Int(a, "maxSize", 1024), 64, 8192),
                quality = Mathf.Clamp(Int(a, "quality", 85), 50, 100),
                jpeg = Flag(a, "jpeg", true),
                triangles = Mathf.Clamp(Num(a, "triangles", 1f), 0.1f, 1f),
            };
            screen.ExternalSaveNow();
            ModBuilder.History?.Commit();   // the files as they are now: what undo goes back to
            var job = new ModOptimizer(Project.Folder, files, options);
            c.Wait(() => { job.Step(); return job.Finished; }, () =>
            {
                bool any = job.Results.Any(r => r.error == null && r.after < r.before);
                var results = new JArray();
                foreach (var r in job.Results)
                {
                    var o = new JObject { ["path"] = r.path, ["before"] = r.before, ["after"] = r.after };
                    if (r.trianglesBefore > 0) { o["trianglesBefore"] = r.trianglesBefore; o["trianglesAfter"] = r.trianglesAfter; }
                    if (r.error != null) o["error"] = r.error;
                    results.Add(o);
                }
                var answer = new JObject { ["before"] = job.Before, ["after"] = job.After, ["files"] = results };
                if (!any) { answer["note"] = "Nothing got smaller: the files are as they were."; c.Json(answer, "nothing got smaller"); return; }
                Visuals.ModLodCache.Forget(Project.Id);   // the levels of detail are made again from the optimized models
                screen.ExternalFilesChanged("optimized the models and textures");
                c.Wait(() => !screen.ReloadBusy, () =>
                {
                    answer["modBytes"] = Project.ModSize();
                    c.Json(answer, $"optimized: {job.Before / 1048576f:0.0} → {job.After / 1048576f:0.0} MB");
                });
            });
        }

        // ---- import_mod ---------------------------------------------------------------------------------------------------

        static void ImportMod(JObject a, Call c)
        {
            string id = Str(a, "id");
            var installed = ModManager.Installed;
            if (string.IsNullOrEmpty(id))
            {
                var list = new JArray();
                foreach (var m in installed.Where(m => m.Manifest != null))
                    list.Add(new JObject { ["id"] = m.Manifest.id, ["name"] = m.Name, ["version"] = m.Manifest.version,
                                           ["hasProject"] = ModProject.List().Any(p => string.Equals(p.Id, m.Manifest.id, StringComparison.OrdinalIgnoreCase)) });
                c.Json(new JObject { ["installed"] = list }, $"{list.Count} installed mods");
                return;
            }
            var mod = installed.FirstOrDefault(m => m.Manifest != null && string.Equals(m.Manifest.id, id, StringComparison.OrdinalIgnoreCase));
            if (mod == null || mod.Source == null) { c.Fail($"No installed mod \"{id}\" (import_mod without an id lists them)."); return; }
            var existing = ModProject.List().FirstOrDefault(p => string.Equals(p.Id, mod.Manifest.id, StringComparison.OrdinalIgnoreCase));
            if (existing != null && !Flag(a, "replace", false)) { c.Fail($"There is a project of \"{mod.Name}\" already (\"{existing.Name}\"): give 'replace' true to replace it (its changes are lost)."); return; }
            if (existing != null && Project != null && existing.Folder == Project.Folder) { c.Fail("That project is the one open: open another first."); return; }
            string root = ModProject.Root, replace = existing?.Folder, error = null, folder = null;
            var src = mod.Source;
            var copy = System.Threading.Tasks.Task.Run(() => folder = ModProject.CopyMod(src, root, mod.Manifest.id, replace, out error));
            c.Wait(() => copy.IsCompleted, () =>
            {
                if (folder == null) { c.Fail("The mod couldn't be imported: " + (error ?? copy.Exception?.GetBaseException().Message)); return; }
                var p = ModProject.List().FirstOrDefault(x => string.Equals(Path.GetFullPath(x.Folder), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase));
                if (p == null) { c.Fail("The copy was made but can't be read as a project: " + folder); return; }
                if (p.LoadError != null) { c.Json(new JObject { ["id"] = p.Id, ["name"] = p.Name, ["error"] = "Some of it can't be read, so it can't be opened until fixed in its folder: " + p.LoadError }, "imported with errors"); return; }
                if (!Flag(a, "open", false)) { c.Json(new JObject { ["id"] = p.Id, ["name"] = p.Name, ["folder"] = p.Folder }, "imported " + p.Name); return; }
                SwitchTo(c, p);
            });
        }

        // ---- replace_game_character ---------------------------------------------------------------------------------------

        static void ReplaceGameCharacter(JObject a, Call c)
        {
            var screen = Screen(out string why);
            if (screen == null) { c.Fail(why); return; }
            string asked = (Str(a, "speaker") ?? "").Trim();
            var names = Enumerable.Range(0, StoryTable.SpeakerCount).Select(StoryTable.OriginalSpeakerName).ToList();
            int speaker = -1;
            if (int.TryParse(asked, out int n) && n >= 0 && n < names.Count) speaker = n;
            else speaker = names.FindIndex(x => string.Equals(x, asked, StringComparison.OrdinalIgnoreCase));
            if (speaker < 0) speaker = names.FindIndex(x => !string.IsNullOrEmpty(x) && x.IndexOf(asked, StringComparison.OrdinalIgnoreCase) >= 0 && asked.Length >= 3);
            if (speaker < 0 || string.IsNullOrWhiteSpace(names[speaker])) { c.Fail($"No story speaker \"{asked}\" (search_content with kind character lists them)."); return; }
            string speakerName = names[speaker];
            // A name several generic speakers share is replaced by number, as the builder does.
            string value = names.Count(x => x == speakerName) == 1 ? speakerName : speaker.ToString();
            var existing = Project.Characters.FirstOrDefault(o => o.Replaces == value);
            if (existing != null) { c.Json(new JObject { ["already"] = true, ["character"] = existing.Json }, $"{speakerName} is already replaced"); return; }
            var picture = Portrait.RenderSpeaker(speaker);
            if (picture == null) { c.Fail("The game's portraits couldn't be read."); return; }
            string tmp = Path.Combine(Application.temporaryCachePath, "speaker_portrait.png");
            try { File.WriteAllBytes(tmp, picture.EncodeToPNG()); }
            finally { UnityEngine.Object.Destroy(picture); }
            string name = string.IsNullOrWhiteSpace(Str(a, "name")) ? speakerName : Str(a, "name").Trim();
            string id = Project.CharacterIdFor(name);
            var ch = Project.AddCharacter(name, Project.AddFile(tmp, "portraits", id, ".png"));
            ch.Replaces = value;
            screen.ExternalFilesChanged($"a character replacing {speakerName}");
            c.Wait(() => !screen.ReloadBusy, () => c.Json(new JObject { ["character"] = ch.Json, ["replaces"] = speakerName }, "replacing " + speakerName));
        }
    }
}
