// ModProjectUndo.cs
// ModProject: undo / redo (ModProjectHistory) (part of the partial ModProject; its fields are in ModProject.cs).

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
        /// <summary>Everything the project's JSON files hold, as it is in memory now (the mod, ships with their hardpoints,
        /// items, characters, systems, stations, rooms, races, blueprints, new-game options, the campaign, the builder's quests,
        /// builder.json).</summary>
        public JObject CaptureState()
        {
            var b = (JObject)builder.DeepClone();
            foreach (var k in KeptBuilderKeys) b.Remove(k);
            var quests = new JObject();
            foreach (var q in Quests) quests[q.Name] = q.ToJson();
            return new JObject
            {
                ["manifest"] = manifest.DeepClone(),
                ["ships"] = new JArray(Ships.Select(s => s.ToJson())),
                ["items"] = new JArray(Items.Select(i => i.ToJson())),
                ["characters"] = new JArray(Characters.Select(c => c.ToJson())),
                ["systems"] = new JArray(Systems.Select(c => c.ToJson())),
                ["stations"] = new JArray(Stations.Select(c => c.ToJson())),
                ["interiors"] = new JArray(Interiors.Select(c => c.ToJson())),
                ["races"] = new JArray(Races.Select(c => c.ToJson())),
                ["blueprints"] = new JArray(Blueprints.Select(c => c.ToJson())),
                ["kits"] = new JArray(Kits.Select(c => c.ToJson())),
                ["weaponEffects"] = new JArray(WeaponEffects.Select(c => c.ToJson())),
                ["gameOptions"] = new JArray(GameOptions.Select(c => c.ToJson())),
                ["npcShips"] = new JArray(NpcShips.Select(c => c.ToJson())),
                ["campaign"] = Campaign != null ? (JToken)Campaign.ToJson() : JValue.CreateNull(),
                ["quests"] = quests,
                ["builder"] = b,
            };
        }
        static IEnumerable<JObject> Objects(JObject state, string key) =>
            (state[key] as JArray)?.OfType<JObject>().Select(o => (JObject)o.DeepClone()) ?? Enumerable.Empty<JObject>();
        /// <summary>Puts a CaptureState back (new entry objects: look the current ones up again by their Ref); the project's
        /// id and the builder.json keys in KeptBuilderKeys stay as they are now.</summary>
        public void RestoreState(JObject state)
        {
            string id = (string)manifest["id"];
            manifest = (JObject)state["manifest"].DeepClone();
            if (id != null) manifest["id"] = id;
            Ships.Clear();
            foreach (var o in ((JArray)state["ships"]).OfType<JObject>()) Ships.Add(new ProjectShip((JObject)o.DeepClone()));
            Items.Clear();
            foreach (var o in ((JArray)state["items"]).OfType<JObject>()) Items.Add(new ProjectItem((JObject)o.DeepClone()));
            Characters.Clear();
            foreach (var o in ((JArray)state["characters"]).OfType<JObject>()) Characters.Add(new ProjectCharacter((JObject)o.DeepClone()));
            Systems.Clear(); Systems.AddRange(Objects(state, "systems").Select(o => new ProjectSystem(o)));
            Stations.Clear(); Stations.AddRange(Objects(state, "stations").Select(o => new ProjectStation(o)));
            Interiors.Clear(); Interiors.AddRange(Objects(state, "interiors").Select(o => new ProjectInterior(o)));
            Races.Clear(); Races.AddRange(Objects(state, "races").Select(o => new ProjectRace(o)));
            Blueprints.Clear(); Blueprints.AddRange(Objects(state, "blueprints").Select(o => new ProjectBlueprint(o)));
            Kits.Clear(); Kits.AddRange(Objects(state, "kits").Select(o => new ProjectKit(o)));
            WeaponEffects.Clear(); WeaponEffects.AddRange(Objects(state, "weaponEffects").Select(o => new ProjectWeaponEffect(o)));
            GameOptions.Clear(); GameOptions.AddRange(Objects(state, "gameOptions").Select(o => new ProjectGameOption(o)));
            NpcShips.Clear(); NpcShips.AddRange(Objects(state, "npcShips").Select(o => new ProjectNpcShip(o)));
            Campaign = state["campaign"] is JObject c ? new ProjectCampaign((JObject)c.DeepClone()) : null;
            Quests.Clear();
            if (state["quests"] is JObject qs)
                foreach (var prop in qs.Properties()) if (prop.Value is JObject qo) Quests.Add(new ProjectQuest(prop.Name, (JObject)qo.DeepClone()));
            var b = (JObject)state["builder"].DeepClone();
            foreach (var k in KeptBuilderKeys) if (builder[k] != null) b[k] = builder[k].DeepClone();
            builder = b;
        }
        /// <summary>A content file, written once there is something in it or it was there already.</summary>
        void WriteList(string file, IEnumerable<JObject> entries)
        {
            var list = new JArray(entries);
            if (list.Count == 0 && !File.Exists(Path.Combine(Folder, file))) return;
            Write(file, list.ToString(Formatting.Indented));
        }
        void Write(string file, string text)
        {
            string path = Path.Combine(Folder, file), tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        /// <summary>Why 'id' can't be this project's new mod id (null = it can): the mod.json rules, and no other project
        /// or installed mod (but this project's own export) may have it.</summary>
        public string IdRefusal(string id)
        {
            if (!ModManifest.ValidId(id)) return $"an id may only use a-z, 0-9, _ and - (at most {ModManifest.MaxIdLength})";
            if (string.Equals(id, Id, StringComparison.OrdinalIgnoreCase)) return null;
            foreach (var dir in Directory.Exists(Root) ? Directory.GetDirectories(Root) : new string[0])
            {
                if (string.Equals(Path.GetFullPath(dir), Path.GetFullPath(Folder), StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(Path.GetFileName(dir), id, StringComparison.OrdinalIgnoreCase)) return $"another project uses \"{id}\"";
                var other = File.Exists(Path.Combine(dir, ModManifest.FileName)) ? Load(dir) : null;
                if (other != null && string.Equals(other.Id, id, StringComparison.OrdinalIgnoreCase)) return $"the project \"{other.Name}\" uses \"{id}\"";
            }
            var mod = ModManager.Find(id);
            if (mod != null && !PreviousIds.Contains(mod.Id, StringComparer.OrdinalIgnoreCase))
                return $"the installed mod \"{mod.Name}\" uses \"{id}\"";
            return null;
        }
        /// <summary>A mod made elsewhere (CopyMod, builder.json "keepId"): its id doesn't follow the name. Saves and
        /// multiplayer sessions name its content by "id:name", so a new id would take its ships and items out of them, and
        /// other mods may depend on it.</summary>
        public bool KeepsId => builder["keepId"] is JValue v && v.Type == JTokenType.Boolean && (bool)v;
        /// <summary>The ids this project had before (builder.json "previousIds"): an export replaces a mod installed under one.</summary>
        public List<string> PreviousIds =>
            builder["previousIds"] is JArray a ? a.Select(t => t.ToString()).ToList() : new List<string>();
        /// <summary>Gives the mod a new id: mod.json, every "old:" key the project's own files name (ships, items,
        /// characters, event graphs refer to their mod's content as "id:name"), the folder (renamed when it was named after
        /// the id), the old id kept in builder.json so the next export replaces the mod installed under it. Returns the
        /// project read again from its new place (this object is done with), or null with the error.</summary>
        public ModProject ChangeId(string id, out string error)
        {
            error = IdRefusal(id);
            if (error != null) return null;
            if (string.Equals(id, Id, StringComparison.Ordinal)) return this;
            string old = Id;
            try
            {
                var prev = PreviousIds;
                if (!prev.Contains(old, StringComparer.OrdinalIgnoreCase)) prev.Add(old);
                prev.RemoveAll(p => string.Equals(p, id, StringComparison.OrdinalIgnoreCase));
                builder["previousIds"] = new JArray(prev);
                manifest["id"] = id;
                Save();
                // "old:..." references in the project's own files (not a longer id that only ends in it).
                var re = new System.Text.RegularExpressions.Regex("(?<![A-Za-z0-9_-])" + System.Text.RegularExpressions.Regex.Escape(old) + ":");
                foreach (var f in Directory.GetFiles(Folder, "*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".json" && ext != ".gof2event" && ext != ".gof2netevent" && ext != ".gof2mission" && ext != ".gof2quest") continue;
                    if (Path.GetFileName(f) == ModManifest.FileName || Path.GetFileName(f) == BuilderFile) continue;
                    string text = File.ReadAllText(f), changed = re.Replace(text, id + ":");
                    if (changed != text) File.WriteAllText(f, changed);
                }
                string folder = Folder;
                if (string.Equals(Path.GetFileName(Folder), old, StringComparison.OrdinalIgnoreCase))
                {
                    string target = Path.Combine(Root, id);
                    if (!Directory.Exists(target)) { Directory.Move(Folder, target); folder = target; }
                }
                Debug.Log($"Mod Builder: the project \"{Name}\" is now the mod \"{id}\" (was \"{old}\")");
                return Load(folder);
            }
            catch (Exception e) { error = e.Message; Debug.LogWarning($"Mod Builder: changing {old}'s id failed: {e.Message}"); return null; }
        }
    }
}
