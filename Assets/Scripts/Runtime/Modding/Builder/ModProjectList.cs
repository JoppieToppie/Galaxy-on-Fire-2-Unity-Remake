// ModProjectList.cs
// ModProject: the translations and the list of projects (part of the partial ModProject; its fields are in ModProject.cs).

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using GoF2Remake.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    public partial class ModProject
    {
        /// <summary>Every text of the project a translation can carry: key ("items.plasma_lance.name") and its English.</summary>
        public List<(string key, string english)> TranslatableTexts()
        {
            var list = new List<(string, string)>();
            foreach (var it in Items)
            {
                string id = it.IsOverride ? it.OverrideIndex.ToString() : it.LocalId;
                if (id == null) continue;
                list.Add(($"items.{id}.name", it.Name ?? ""));
                list.Add(($"items.{id}.description", it.Description ?? ""));
            }
            foreach (var s in Ships)
            {
                string id = s.IsOverride ? s.OverrideIndex.ToString() : s.LocalId;
                if (id == null) continue;
                list.Add(($"ships.{id}.name", s.Name ?? ""));
                list.Add(($"ships.{id}.description", s.Description ?? ""));
            }
            foreach (var s in Systems) { string id = s.IsOverride ? s.OverrideRef : s.LocalId; if (id != null) list.Add(($"systems.{id}.name", s.Name ?? "")); }
            foreach (var s in Stations) { string id = s.IsOverride ? s.OverrideRef : s.LocalId; if (id != null) list.Add(($"stations.{id}.name", s.Name ?? "")); }
            foreach (var s in NpcShips) { string id = s.IsOverride ? s.OverrideRef?.Replace(':', '_') : s.LocalId; if (id != null) list.Add(($"npcships.{id}.name", s.Name ?? "")); }
            // Remake: a mod race's texts (ModRaces reads "races.<id>.name" / ".description").
            foreach (var r in Races.Where(r => r.Id != null))
            {
                list.Add(($"races.{r.Id}.name", r.Name ?? ""));
                list.Add(($"races.{r.Id}.description", r.Description ?? ""));
            }
            return list;
        }
        /// <summary>text/&lt;lang&gt;.json as key -> text (empty when there is none).</summary>
        public Dictionary<string, string> Texts(string lang)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string f = Path.Combine(Folder, TextFolder, lang + ".json");
            try
            {
                if (File.Exists(f) && JToken.Parse(File.ReadAllText(f)) is JObject o)
                    foreach (var p in o.Properties()) if (p.Value.Type == JTokenType.String) d[p.Name] = (string)p.Value;
            }
            catch (Exception e) { Debug.LogWarning($"Mod Builder: {f}: {e.Message}"); }
            return d;
        }
        /// <summary>One translation set (empty: removed); the file goes when none is left.</summary>
        public void SetText(string lang, string key, string text)
        {
            var d = Texts(lang);
            if (string.IsNullOrWhiteSpace(text)) d.Remove(key); else d[key] = text;
            string dir = Path.Combine(Folder, TextFolder), f = Path.Combine(dir, lang + ".json");
            if (d.Count == 0) { try { if (File.Exists(f)) File.Delete(f); } catch { } return; }
            Directory.CreateDirectory(dir);
            var o = new JObject();
            foreach (var kv in d.OrderBy(k => k.Key, StringComparer.Ordinal)) o[kv.Key] = kv.Value;
            File.WriteAllText(f, o.ToString(Formatting.Indented));
        }
        static string Str(JObject o, string key) => o[key] is JValue v && v.Type == JTokenType.String ? (string)v : null;
        // ---- the list ----------------------------------------------------------------------------------------------
        /// <summary>Every project, the most recently changed first.</summary>
        public static List<ModProject> List()
        {
            var l = new List<ModProject>();
            try
            {
                Directory.CreateDirectory(Root);
                foreach (var dir in Directory.GetDirectories(Root))
                {
                    if (Path.GetFileName(dir).StartsWith(".")) continue;   // an import being copied (CopyMod)
                    if (!File.Exists(Path.Combine(dir, ModManifest.FileName))) continue;
                    var p = Load(dir);
                    if (p != null) l.Add(p);
                }
            }
            catch (Exception e) { Debug.LogWarning($"Mod Builder: can't read {Root}: {e.Message}"); }
            l.Sort((a, b) => b.Modified.CompareTo(a.Modified));
            return l;
        }
        /// <summary>A name as an id (like a repository's name on GitHub / GitLab): lower case, a-z 0-9, anything else one '_'.</summary>
        public static string Slug(string name)
        {
            var chars = (name ?? "").Trim().ToLowerInvariant().Select(c => c >= 'a' && c <= 'z' || c >= '0' && c <= '9' ? c : '_').ToArray();
            string id = new string(chars).Trim('_');
            while (id.Contains("__")) id = id.Replace("__", "_");
            if (id.Length == 0) id = "my_mod";
            if (id.Length > ModManifest.MaxIdLength - 4) id = id.Substring(0, ModManifest.MaxIdLength - 4).Trim('_');
            return id;
        }
        /// <summary>The id this project's name gives: its slug, numbered ("_2") when another project or mod has that. An
        /// imported mod (KeepsId) keeps its own whatever it is called.</summary>
        public string IdFromName(string name)
        {
            if (KeepsId) return Id;
            string id = Slug(name), unique = id;
            for (int i = 2; IdRefusal(unique) != null && i < 1000; i++) unique = $"{id}_{i}";
            return unique;
        }
        /// <summary>A project's id from its name: lower case a-z 0-9 _, made unique among the projects and installed mods.</summary>
        public static string IdFor(string name)
        {
            string id = Slug(name);
            string unique = id;
            for (int i = 2; Directory.Exists(Path.Combine(Root, unique)) || ModManager.Find(unique) != null; i++) unique = $"{id}_{i}";
            return unique;
        }
        /// <summary>A new, empty project named 'name'.</summary>
        public static ModProject Create(string name, string author)
        {
            name = string.IsNullOrWhiteSpace(name) ? "My mod" : name.Trim();
            string id = IdFor(name);
            var p = new ModProject { Folder = Path.Combine(Root, id), Id = id };
            p.manifest = new JObject
            {
                ["id"] = id, ["name"] = name, ["version"] = "1.0",
                ["author"] = string.IsNullOrWhiteSpace(author) ? "" : author.Trim(),
                ["description"] = "",
            };
            Directory.CreateDirectory(p.Folder);
            p.Save();
            Debug.Log($"Mod Builder: new project \"{name}\" ({p.Folder})");
            return p;
        }
        /// <summary>Reads a project folder (null when its mod.json can't be read).</summary>
        public static ModProject Load(string folder)
        {
            try
            {
                var p = new ModProject { Folder = folder };
                p.manifest = JObject.Parse(File.ReadAllText(Path.Combine(folder, ModManifest.FileName)));
                p.Id = Str(p.manifest, "id") ?? Path.GetFileName(folder);
                p.Modified = Directory.GetLastWriteTimeUtc(folder);
                foreach (var f in new[] { ModManifest.FileName, ModContent.ShipsFile, ModContent.ItemsFile, ModCharacters.File, BuilderFile,
                                          ModWorld.SystemsFile, ModWorld.StationsFile, InteriorsFile, ModBlueprints.File, ModGameOptions.File, CampaignFile, ProjectKit.File,
                                          ModRaces.File, ModRaces.OldFile, ModContent.WeaponEffectsFile, ModNpcShips.File, ModFlight.File })
                {
                    string path = Path.Combine(folder, f);
                    if (File.Exists(path)) { var t = File.GetLastWriteTimeUtc(path); if (t > p.Modified) p.Modified = t; }
                }
                string ships = Path.Combine(folder, ModContent.ShipsFile);
                if (File.Exists(ships))
                {
                    try
                    {
                        if (JToken.Parse(File.ReadAllText(ships)) is JArray list)
                            foreach (var t in list.OfType<JObject>()) p.Ships.Add(new ProjectShip(t));
                    }
                    catch (Exception e) { p.LoadError = $"{ModContent.ShipsFile}: {e.Message}"; }
                }
                p.ReadList(ModContent.ItemsFile, o => p.Items.Add(new ProjectItem(o)));
                p.ReadList(ModCharacters.File, o => p.Characters.Add(new ProjectCharacter(o)));
                p.ReadList(ModWorld.SystemsFile, o => p.Systems.Add(new ProjectSystem(o)));
                p.ReadList(ModWorld.StationsFile, o => p.Stations.Add(new ProjectStation(o)));
                p.ReadList(InteriorsFile, o => p.Interiors.Add(new ProjectInterior(o)));
                p.ReadList(File.Exists(Path.Combine(folder, ModRaces.File)) ? ModRaces.File : ModRaces.OldFile, o => p.Races.Add(new ProjectRace(o)));
                p.ReadList(ModBlueprints.File, o => p.Blueprints.Add(new ProjectBlueprint(o)));
                p.ReadList(ProjectKit.File, o => p.Kits.Add(new ProjectKit(o)));
                p.ReadList(ModContent.WeaponEffectsFile, o => p.WeaponEffects.Add(new ProjectWeaponEffect(o)));
                p.ReadList(ModGameOptions.File, o => p.GameOptions.Add(new ProjectGameOption(o)));
                p.ReadList(ModNpcShips.File, o => p.NpcShips.Add(new ProjectNpcShip(o)));
                string campaign = Path.Combine(folder, CampaignFile);
                if (File.Exists(campaign))
                {
                    try { p.Campaign = new ProjectCampaign(JObject.Parse(File.ReadAllText(campaign))); }
                    catch (Exception e) { p.LoadError ??= $"{CampaignFile}: {e.Message}"; }
                }
                p.LoadProtectedConfigurations();
                string events = Path.Combine(folder, ProjectQuest.Folder);
                if (Directory.Exists(events))
                    foreach (var f in Directory.GetFiles(events, "*." + ProjectQuest.Extension).Concat(Directory.GetFiles(events, "*." + ProjectQuest.LegacyExtension))
                                               .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    {
                        // An old .gof2quest beside a .gof2mission of the same name: the new one counts.
                        if (f.EndsWith("." + ProjectQuest.LegacyExtension, StringComparison.OrdinalIgnoreCase)
                            && File.Exists(Path.ChangeExtension(f, ProjectQuest.Extension))) continue;
                        try { p.Quests.Add(new ProjectQuest(Path.GetFileNameWithoutExtension(f), JObject.Parse(File.ReadAllText(f)))); }
                        catch (Exception e) { p.LoadError ??= $"{ProjectQuest.Folder}/{Path.GetFileName(f)}: {e.Message}"; }
                    }
                string b = Path.Combine(folder, BuilderFile);
                if (File.Exists(b)) { try { p.builder = JObject.Parse(File.ReadAllText(b)); } catch { p.builder = new JObject(); } }
                return p;
            }
            catch (Exception e) { Debug.LogWarning($"Mod Builder: can't read the project {folder}: {e.Message}"); return null; }
        }
        /// <summary>A content file's entries (a broken file is the LoadError, and the project isn't saved over it).</summary>
        void ReadList(string file, Action<JObject> add)
        {
            string path = Path.Combine(Folder, file);
            if (!File.Exists(path)) return;
            try
            {
                if (JToken.Parse(File.ReadAllText(path)) is JArray list)
                    foreach (var t in list.OfType<JObject>()) add(t);
            }
            catch (Exception e) { LoadError ??= $"{file}: {e.Message}"; }
        }
        /// <summary>Writes mod.json, ships.json, items.json, characters.json and builder.json (each through a .tmp file).</summary>
        static readonly object saveLock = new object();

        // Separate the last saved values from the mutable editor objects.
        readonly Dictionary<string, JObject> configBaseline = new Dictionary<string, JObject>();
        readonly HashSet<string> configRemovals = new HashSet<string>();
        readonly HashSet<string> configCreates = new HashSet<string>();
        readonly List<string> saveFailures = new List<string>();
        bool collectingSaveFailures;

        IEnumerable<(string file, Func<JObject> get, Action<JObject> set)> ProtectedConfigurations()
        {
            yield return (ModFlight.File, () => FlightRules, value => FlightRules = value);
            yield return (ModPlayerInfrastructure.FileName, () => PlayerInfrastructure, value => PlayerInfrastructure = value);
            yield return (ModNpcFactionWar.File, () => NpcFactionWarRules, value => NpcFactionWarRules = value);
            yield return (ModAdvancedEconomy.File, () => AdvancedEconomyRules, value => AdvancedEconomyRules = value);
        }

        string ConfigurationPath(string file)
        {
            string path = Path.Combine(Folder, file);
            if (file == ModNpcFactionWar.File && !File.Exists(path))
            {
                string staged = Path.Combine(Folder, ModNpcFactionWar.StagedFile);
                if (File.Exists(staged)) return staged;
            }
            return path;
        }

        JObject ReadConfiguration(string file)
        {
            try { return JObject.Parse(File.ReadAllText(ConfigurationPath(file))); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            catch (JsonException e) { throw new IOException(file + ": invalid external JSON: " + e.Message, e); }
        }

        void LoadProtectedConfigurations()
        {
            foreach (var config in ProtectedConfigurations())
            {
                try
                {
                    var disk = ReadConfiguration(config.file);
                    configBaseline[config.file] = disk;
                    config.set(disk == null ? new JObject() : (JObject)disk.DeepClone());
                }
                catch (Exception e) { LoadError ??= config.file + ": " + e.Message; }
            }
        }

        public void RefreshProtectedConfigurations()
        {
            lock (saveLock)
            {
                var pending = new List<Action>();
                foreach (var config in ProtectedConfigurations())
                {
                    configBaseline.TryGetValue(config.file, out var baseline);
                    var disk = ReadConfiguration(config.file);
                    var local = config.get();
                    bool dirty = configRemovals.Contains(config.file) || configCreates.Contains(config.file) ||
                        !JToken.DeepEquals(local, baseline ?? new JObject());
                    if (JToken.DeepEquals(disk, baseline)) continue;
                    if (dirty && !JToken.DeepEquals(disk, configRemovals.Contains(config.file) ? null : local))
                        throw new IOException(config.file + ": external changes conflict with unsaved builder edits. Neither version was overwritten.");
                    pending.Add(() =>
                    {
                        configBaseline[config.file] = disk;
                        configRemovals.Remove(config.file);
                        configCreates.Remove(config.file);
                        var current = config.get();
                        current.RemoveAll();
                        if (disk != null)
                            foreach (var prop in disk.Properties()) current.Add(prop.Name, prop.Value.DeepClone());
                    });
                }
                foreach (var update in pending) update();
            }
        }

        JToken CaptureConfiguration(string file, JObject value)
        {
            configBaseline.TryGetValue(file, out var baseline);
            return configRemovals.Contains(file) || (baseline == null && !configCreates.Contains(file) && !value.HasValues)
                ? JValue.CreateNull() : value.DeepClone();
        }

        JObject RestoreConfiguration(string file, JToken value)
        {
            configBaseline.TryGetValue(file, out var baseline);
            configRemovals.Remove(file);
            configCreates.Remove(file);
            if (value is JObject obj)
            {
                if (baseline == null) configCreates.Add(file);
                return (JObject)obj.DeepClone();
            }
            if (baseline != null) configRemovals.Add(file);
            return new JObject();
        }

        void SaveProtectedConfigurations()
        {
            foreach (var config in ProtectedConfigurations())
            {
                string file = config.file;
                var local = config.get();
                configBaseline.TryGetValue(file, out var baseline);
                bool remove = configRemovals.Contains(file);
                if (!remove && !configCreates.Contains(file) && JToken.DeepEquals(local, baseline ?? new JObject())) continue;
                TrySaveFile(file, () =>
                {
                    if (!JToken.DeepEquals(ReadConfiguration(file), baseline))
                        throw new IOException("configuration changed during save");
                    string path = ConfigurationPath(file);
                    if (remove)
                    {
                        if (file == ModNpcFactionWar.File)
                            RetryFileOperation(() => File.Delete(Path.Combine(Folder, ModNpcFactionWar.StagedFile)));
                        RetryFileOperation(() => File.Delete(Path.Combine(Folder, file)));
                    }
                    else SafeWriteBytes(path, new System.Text.UTF8Encoding(false).GetBytes(local.ToString(Formatting.Indented)));
                    configBaseline[file] = remove ? null : (JObject)local.DeepClone();
                    configRemovals.Remove(file);
                    configCreates.Remove(file);
                });
            }
        }

        void TrySaveFile(string file, Action action)
        {
            try { action(); }
            catch (Exception e) when (collectingSaveFailures && (e is IOException || e is UnauthorizedAccessException))
            { saveFailures.Add(file + ": " + e.Message); }
        }

        public void Save()
        {
            lock (saveLock)
            {
                if (LoadError != null) throw new IOException("Project not saved: " + LoadError);
                RefreshProtectedConfigurations();
                saveFailures.Clear();
                collectingSaveFailures = true;
                try { SaveLocked(); }
                finally { collectingSaveFailures = false; }
                if (saveFailures.Count > 0)
                    throw new IOException("Some files could not be saved (successful files retained): " + string.Join("; ", saveFailures));
            }
        }

        void SaveLocked()
        {
            if (LoadError != null) { Debug.LogWarning($"Mod Builder: {Id} not saved: {LoadError}"); return; }
            Directory.CreateDirectory(Folder);
            Write(ModManifest.FileName, manifest.ToString(Formatting.Indented));
            var list = new JArray();
            foreach (var s in Ships) list.Add(s.ToJson());
            Write(ModContent.ShipsFile, list.ToString(Formatting.Indented));
            WriteList(ModContent.ItemsFile, Items.Select(i => i.ToJson()));
            WriteList(ModCharacters.File, Characters.Select(c => c.ToJson()));
            WriteList(ModWorld.SystemsFile, Systems.Select(c => c.ToJson()));
            WriteList(ModWorld.StationsFile, Stations.Select(c => c.ToJson()));
            WriteList(InteriorsFile, Interiors.Select(c => c.ToJson()));
            WriteList(ModRaces.File, Races.Select(c => c.ToJson()));
            if (File.Exists(Path.Combine(Folder, ModRaces.OldFile))) DeleteSavedFile(Path.Combine(Folder, ModRaces.OldFile));   // the old name, now races.json
            WriteList(ModBlueprints.File, Blueprints.Select(c => c.ToJson()));
            WriteList(ProjectKit.File, Kits.Select(c => c.ToJson()));
            WriteList(ModContent.WeaponEffectsFile, WeaponEffects.Select(c => c.ToJson()));
            WriteList(ModGameOptions.File, GameOptions.Select(c => c.ToJson()));
            WriteList(ModNpcShips.File, NpcShips.Select(c => c.ToJson()));
            string campaign = Path.Combine(Folder, CampaignFile);
            if (Campaign != null) Write(CampaignFile, Campaign.ToJson().ToString(Formatting.Indented));
            else if (File.Exists(campaign)) DeleteSavedFile(campaign);
            SaveProtectedConfigurations();
            WriteQuests();
            Write(BuilderFile, builder.ToString(Formatting.Indented));
            Modified = DateTime.UtcNow;
        }
    }
}
