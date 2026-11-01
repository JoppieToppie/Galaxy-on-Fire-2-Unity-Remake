// ModProjectOtherKinds.cs
// ModProject: the other kinds and plain audio files (part of the partial ModProject; its fields are in ModProject.cs).

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
        // ---- the other kinds ------------------------------------------------------------------------------------
        public string SystemIdFor(string name) => UniqueId(name, "system", id => Systems.Any(s => s.LocalId == id));
        public string StationIdFor(string name) => UniqueId(name, "station", id => Stations.Any(s => s.LocalId == id));
        public string InteriorIdFor(string name) => UniqueId(name, "room", id => Interiors.Any(s => s.Id == id));
        public string BlueprintIdFor(string name) => UniqueId(name, "blueprint", id => Blueprints.Any(s => s.Id == id));
        public string KitIdFor(string name) => UniqueId(name, "kit", id => Kits.Any(s => s.Id == id));
        public string WeaponEffectIdFor(string name) => UniqueId(name, "effect", id => WeaponEffects.Any(s => s.Id == id));
        public string GameOptionIdFor(string name) => UniqueId(name, "option", id => GameOptions.Any(s => s.Id == id));
        public string NpcShipIdFor(string name) => UniqueId(name, "npc", id => NpcShips.Any(s => s.LocalId == id));
        /// <summary>A new NPC ship of 'kind' (ModNpcShips.Capital / Freighter / Boss) on a game hull ('looksLike') or a ship's.</summary>
        public ProjectNpcShip AddNpcShip(string name, string kind, string looksLike, string ship)
        {
            var o = new JObject { ["id"] = NpcShipIdFor(name), ["kind"] = kind, ["name"] = name };
            if (!string.IsNullOrEmpty(ship)) o["ship"] = ProjectEntry.RefToken(ship);
            else if (!string.IsNullOrEmpty(looksLike)) o["looksLike"] = looksLike;
            o["spawn"] = new JObject { ["chance"] = 20 };
            var s = new ProjectNpcShip(o);
            NpcShips.Add(s);
            return s;
        }
        /// <summary>A change to one of the game's NPC ships (ModNpcShips.GameKeys) or another mod's ("mod:id").</summary>
        public ProjectNpcShip AddNpcShipOverride(string key)
        {
            var s = new ProjectNpcShip(new JObject { ["override"] = key });
            NpcShips.Add(s);
            return s;
        }
        /// <summary>A quest's file name: one word (a-z 0-9 _), unique among the project's quests and event graphs.</summary>
        public string QuestNameFor(string name) => UniqueId(name, "quest", id => Quests.Any(q => string.Equals(q.Name, id, StringComparison.OrdinalIgnoreCase))
                                                                          || File.Exists(Path.Combine(Folder, ProjectQuest.Folder, id + ".gof2event")));
        /// <summary>The key other content names one of the project's own entries by ("my_mod:kepler").</summary>
        public string Key(string localId) => Id + ":" + localId;
        public ProjectSystem AddSystem(string name, Vector3 position, int race)
        {
            var s = new ProjectSystem(new JObject { ["id"] = SystemIdFor(name), ["name"] = name, ["race"] = race, ["security"] = 2 });
            s.Position = position;
            Systems.Add(s);
            return s;
        }
        /// <summary>A change to one of the game's (or another mod's) systems.</summary>
        public ProjectSystem AddSystemOverride(string system)
        {
            var s = new ProjectSystem(new JObject { ["override"] = ProjectEntry.RefToken(system) });
            Systems.Add(s);
            return s;
        }
        public ProjectStation AddStation(string name, string system, int techLevel, int planet)
        {
            var s = new ProjectStation(new JObject { ["id"] = StationIdFor(name), ["name"] = name, ["system"] = ProjectEntry.RefToken(system),
                                                     ["techLevel"] = techLevel, ["planet"] = planet });
            Stations.Add(s);
            return s;
        }
        public ProjectStation AddStationOverride(string station)
        {
            var s = new ProjectStation(new JObject { ["override"] = ProjectEntry.RefToken(station) });
            Stations.Add(s);
            return s;
        }
        public ProjectInterior AddInterior(string name, bool bar, string model)
        {
            var r = new ProjectInterior(new JObject { ["id"] = InteriorIdFor(name), ["type"] = bar ? "bar" : "hangar", ["model"] = model });
            Interiors.Add(r);
            return r;
        }
        public ProjectBlueprint AddBlueprint(string name, bool ship, string product)
        {
            var b = new ProjectBlueprint(new JObject { ["id"] = BlueprintIdFor(name) });
            b.SetProduct(ship, product);
            b.Ingredients = new List<ProjectStack>();
            Blueprints.Add(b);
            return b;
        }
        /// <summary>A new kit with no parts yet (the game skips it until it has one).</summary>
        public ProjectKit AddKit(string name)
        {
            var k = new ProjectKit(new JObject { ["id"] = KitIdFor(name), ["name"] = name, ["scale"] = 1, ["slots"] = new JArray("body", "wings", "rear"), ["parts"] = new JArray() });
            Kits.Add(k);
            return k;
        }
        public ProjectKit FindKit(string id) => Kits.Find(k => string.Equals(k.Id, id, StringComparison.OrdinalIgnoreCase));
        /// <summary>A new weapon effect: the game weapon 'baseItem''s look until its parts and sounds are given.</summary>
        public ProjectWeaponEffect AddWeaponEffect(int baseItem, string name)
        {
            var e = new ProjectWeaponEffect(new JObject { ["id"] = WeaponEffectIdFor(name), ["name"] = name, ["base"] = baseItem });
            WeaponEffects.Add(e);
            return e;
        }
        public ProjectWeaponEffect FindWeaponEffect(string id) => WeaponEffects.Find(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
        /// <summary>Removes the effect and the "fx" of the items that took it (they look like their base again).</summary>
        public void RemoveWeaponEffect(ProjectWeaponEffect e)
        {
            WeaponEffects.Remove(e);
            foreach (var it in Items) if (string.Equals(it.EffectId, e.Id, StringComparison.OrdinalIgnoreCase)) it.EffectId = null;
        }
        /// <summary>A new customizable ship flying 'kit' (this project's): sensible stats, its type 'kitType', the default
        /// build each slot's first part (the game's Normalize does that for a slot left out).</summary>
        public ProjectShip AddKitShip(string id, string name, ProjectKit kit, string kitType)
        {
            var s = new ProjectShip(new JObject
            {
                ["id"] = id, ["name"] = name, ["description"] = "",
                ["race"] = 0, ["armor"] = 300, ["cargo"] = 60, ["handling"] = 110,
                ["price"] = 400000,
                ["slots"] = new JObject { ["primary"] = 2, ["secondary"] = 1, ["turret"] = 0, ["equipment"] = 8 },
                ["kit"] = kit.Id, ["kitType"] = kitType, ["build"] = new JObject { ["tier"] = 0 },
                ["mounts"] = new JArray(),
            });
            Ships.Add(s);
            return s;
        }
        public ProjectGameOption AddGameOption(string name)
        {
            var o = new ProjectGameOption(new JObject { ["id"] = GameOptionIdFor(name), ["name"] = name });
            GameOptions.Add(o);
            return o;
        }
        public ProjectQuest AddQuest(string title, bool mission)
        {
            var q = new ProjectQuest(QuestNameFor(title), new JObject { ["title"] = title, ["kind"] = mission ? "mission" : "quest" });
            Quests.Add(q);
            return q;
        }
        /// <summary>A quest from a whole .gof2quest object (an event graph converted to steps, QuestFile.FromScript).</summary>
        public ProjectQuest AddQuest(string name, JObject json)
        {
            var q = new ProjectQuest(QuestNameFor(string.IsNullOrWhiteSpace(name) ? (string)json["title"] ?? "quest" : name), json);
            Quests.Add(q);
            return q;
        }
        /// <summary>An event graph made in the Unity Editor copied into events/ as it is (it runs like the quests).</summary>
        public string AddEventGraph(string sourceFile)
        {
            string name = UniqueId(Path.GetFileNameWithoutExtension(sourceFile), "event",
                id => Quests.Any(q => string.Equals(q.Name, id, StringComparison.OrdinalIgnoreCase)) || EventGraphs().Any(g => string.Equals(g, id, StringComparison.OrdinalIgnoreCase)));
            string dir = Path.Combine(Folder, ProjectQuest.Folder);
            Directory.CreateDirectory(dir);
            File.Copy(sourceFile, Path.Combine(dir, name + ".gof2event"), true);
            return name;
        }
        /// <summary>An event graph's file in the project (.gof2event, or the old .gof2netevent), or null.</summary>
        public string EventGraphPath(string name)
        {
            foreach (string ext in new[] { ".gof2event", ".gof2netevent" })
            {
                string f = Path.Combine(Folder, ProjectQuest.Folder, name + ext);
                if (File.Exists(f)) return f;
            }
            return null;
        }
        public void RemoveEventGraph(string name)
        {
            string f = EventGraphPath(name);
            if (f != null) try { File.Delete(f); } catch { }
        }
        /// <summary>events/&lt;name&gt;.gof2mission for every mission; a builder mission file no longer in the list goes, and so does
        /// the old .gof2quest a mission was read from (written back as a .gof2mission).</summary>
        void WriteQuests()
        {
            string dir = Path.Combine(Folder, ProjectQuest.Folder);
            if (Quests.Count > 0) Directory.CreateDirectory(dir);
            foreach (var q in Quests) Write(ProjectQuest.Folder + "/" + q.Name + "." + ProjectQuest.Extension, q.ToJson().ToString(Formatting.Indented));
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir, "*." + ProjectQuest.Extension))
                if (!Quests.Any(q => string.Equals(q.Name, Path.GetFileNameWithoutExtension(f), StringComparison.OrdinalIgnoreCase)))
                    try { File.Delete(f); } catch { }
            foreach (var f in Directory.GetFiles(dir, "*." + ProjectQuest.LegacyExtension))
                try { File.Delete(f); } catch { }
        }
        /// <summary>The event graphs made in the Unity Editor (events/*.gof2event): listed, not edited by the builder.</summary>
        public List<string> EventGraphs()
        {
            string dir = Path.Combine(Folder, ProjectQuest.Folder);
            if (!Directory.Exists(dir)) return new List<string>();
            return Directory.GetFiles(dir, "*.gof2event").Concat(Directory.GetFiles(dir, "*.gof2netevent"))
                            .Select(Path.GetFileNameWithoutExtension).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }
        /// <summary>The audio files of a folder ("music", "sounds", "voices", "voices/de"): name without extension -> path in the
        /// project.</summary>
        public List<(string name, string path)> AudioFiles(string folder)
        {
            string dir = Path.Combine(Folder, folder);
            if (!Directory.Exists(dir)) return new List<(string, string)>();
            return Directory.GetFiles(dir).Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                            .Select(f => (Path.GetFileNameWithoutExtension(f), folder + "/" + Path.GetFileName(f))).ToList();
        }
        /// <summary>An audio file into a folder under 'name' (any other file of that name there goes: one per name).</summary>
        public string SetAudio(string folder, string name, string sourceFile)
        {
            RemoveAudio(folder, name);
            string dir = Path.Combine(Folder, folder);
            Directory.CreateDirectory(dir);
            string ext = Path.GetExtension(sourceFile).ToLowerInvariant();
            if (!AudioExtensions.Contains(ext)) ext = ".ogg";
            string dest = Path.Combine(dir, name + ext);
            File.Copy(sourceFile, dest, true);
            File.SetLastWriteTimeUtc(dest, DateTime.UtcNow);   // a copy keeps its source's date: the game's clip cache sees the change by it (ModAudio)
            return folder + "/" + name + ext;
        }
        public void RemoveAudio(string folder, string name)
        {
            string dir = Path.Combine(Folder, folder);
            foreach (var ext in AudioExtensions)
            {
                string f = Path.Combine(dir, name + ext);
                if (File.Exists(f)) File.Delete(f);
            }
        }
    }
}
