// ModProjectDependencies.cs
// ModProject: dependencies (mod.json) and builder.json (part of the partial ModProject; its fields are in ModProject.cs).

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
        /// <summary>The mods it depends on (ids; "id>=version" entries by their id).</summary>
        public List<string> Dependencies =>
            manifest["dependencies"] is JArray a ? a.Select(t => t.ToString()).Select(d => { int ge = d.IndexOf(">=", StringComparison.Ordinal); return (ge > 0 ? d.Substring(0, ge) : d).Trim(); })
                                                    .Where(d => d.Length > 0).ToList() : new List<string>();

        /// <summary>Makes 'mod' a dependency (its version as the lowest needed); false when it was one already.</summary>
        public bool AddDependency(string mod, string version = null)
        {
            if (string.IsNullOrEmpty(mod) || string.Equals(mod, Id, StringComparison.OrdinalIgnoreCase)) return false;
            if (Dependencies.Contains(mod, StringComparer.OrdinalIgnoreCase)) return false;
            if (!(manifest["dependencies"] is JArray a)) manifest["dependencies"] = a = new JArray();
            a.Add(string.IsNullOrEmpty(version) ? mod : mod + ">=" + version);
            return true;
        }
        public void RemoveDependency(string mod)
        {
            if (!(manifest["dependencies"] is JArray a)) return;
            foreach (var t in a.ToList())
            {
                string d = t.ToString();
                int ge = d.IndexOf(">=", StringComparison.Ordinal);
                if (string.Equals((ge > 0 ? d.Substring(0, ge) : d).Trim(), mod, StringComparison.OrdinalIgnoreCase)) t.Remove();
            }
            if (a.Count == 0) manifest.Remove("dependencies");
        }
        /// <summary>The mods a key in the project's files names ("other_mod:..."), not its own and not "gof2:" / "quest:": what
        /// ModRefs should have made a dependency.</summary>
        public HashSet<string> ModsNamed()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var re = new System.Text.RegularExpressions.Regex("\"([a-z0-9_-]{1,40}):[a-z0-9_-]+\"");
            foreach (var t in CaptureState().Properties().Where(p => p.Name != "manifest" && p.Name != "builder"))
                foreach (System.Text.RegularExpressions.Match m in re.Matches(t.Value.ToString(Formatting.None)))
                {
                    string mod = m.Groups[1].Value;
                    if (mod != Id && mod != GameModels.Prefix.TrimEnd(':') && mod != "id" && mod != "override") set.Add(mod);
                }
            return set;
        }
        /// <summary>Copies a GLB into the project's models folder; its path inside the project ("models/<name>.glb").</summary>
        public string AddModel(string sourceFile, string baseName)
        {
            string dir = Path.Combine(Folder, ModelsFolder);
            Directory.CreateDirectory(dir);
            string name = baseName + ".glb";
            for (int i = 2; File.Exists(Path.Combine(dir, name)); i++) name = $"{baseName}_{i}.glb";
            File.Copy(sourceFile, Path.Combine(dir, name), true);
            return ModelsFolder + "/" + name;
        }
        /// <summary>Copies a PNG into the project's textures folder; its path inside the project ("textures/<name>.png").</summary>
        public string AddTexture(string sourceFile, string baseName)
        {
            string dir = Path.Combine(Folder, "textures");
            Directory.CreateDirectory(dir);
            string name = baseName + ".png";
            File.Copy(sourceFile, Path.Combine(dir, name), true);
            return "textures/" + name;
        }
        /// <summary>A new ship from a model file in the project: sensible stats, no hardpoints yet (the builder places
        /// defaults once the model is built).</summary>
        public ProjectShip AddNewShip(string id, string name, string modelPath)
        {
            var s = new ProjectShip(new JObject
            {
                ["id"] = id, ["name"] = name, ["description"] = "",
                ["race"] = 0, ["armor"] = 300, ["cargo"] = 60, ["handling"] = 110,
                ["price"] = 400000,
                ["slots"] = new JObject { ["primary"] = 2, ["secondary"] = 1, ["turret"] = 0, ["equipment"] = 8 },
                ["model"] = modelPath, ["modelLength"] = 1000, ["modelYaw"] = 0, ["hangarHeight"] = 250,
                ["mounts"] = new JArray(),
            }) { NeedsLayout = true };
            Ships.Add(s);
            return s;
        }
        /// <summary>An override of an original ship: its stats and hardpoints copied, so they can be changed.</summary>
        public ProjectShip AddOverride(Database db, int ship)
        {
            var s = new ProjectShip(new JObject { ["override"] = ship });
            s.FillFromGame(db.Ship(ship));
            s.Mounts.AddRange((db.WeaponMounts.Find(w => w.ship == ship)?.mounts ?? new List<WeaponMount>()).Select(ProjectShip.Copy));
            Ships.Add(s);
            return s;
        }
        // ---- builder.json --------------------------------------------------------------------------------------
        JObject Section(string key)
        {
            if (!(builder[key] is JObject o)) builder[key] = o = new JObject();
            return o;
        }
        /// <summary>A ship's test loadout (item, amount), null = none chosen yet.</summary>
        public List<ItemStack> Loadout(ProjectShip s)
        {
            if (!(Section("loadouts")[s.Ref] is JArray a)) return null;
            var l = new List<ItemStack>();
            foreach (var e in a.OfType<JArray>()) if (e.Count >= 2) l.Add(new ItemStack((int)e[0], (int)e[1]));
            return l;
        }
        public void SetLoadout(ProjectShip s, List<ItemStack> items)
        {
            var a = new JArray();
            foreach (var i in items) a.Add(new JArray(i.item, i.amount));
            Section("loadouts")[s.Ref] = a;
        }
        /// <summary>The icon the builder made for a ship (ShipIconMaker): its file and the look it was made from, null = none.
        /// A ships.json "icon" that isn't this file is the modder's own and is never replaced.</summary>
        public (string file, string key)? GeneratedIcon(ProjectShip s) =>
            Section("icons")[s.Ref] is JObject o ? ((string)o["file"], (string)o["key"]) : ((string, string)?)null;
        /// <summary>Writes a generated icon to icons/&lt;id&gt;.png and makes it the ship's icon. The PNG's hash goes with it,
        /// so a file the modder put in its place later (their own art under the same name) is told apart (CustomIcon).</summary>
        public void SetGeneratedIcon(ProjectShip s, byte[] png, string key)
        {
            string dir = Path.Combine(Folder, "icons");
            Directory.CreateDirectory(dir);
            string file = "icons/" + (s.LocalId ?? "ship") + ".png";
            File.WriteAllBytes(Path.Combine(Folder, file), png);
            s.Icon = file;
            Section("icons")[s.Ref] = new JObject { ["file"] = file, ["key"] = key, ["hash"] = IconHash(png) };
        }
        /// <summary>Whether the ship's icon is the modder's own, never drawn over except by Recreate icon: a ships.json "icon"
        /// that isn't the builder's file, or the builder's file whose bytes are no longer the ones it wrote (replaced by hand).
        /// A record from before hashes were kept can't tell, so its existing file counts as the modder's too.</summary>
        public bool CustomIcon(ProjectShip s)
        {
            if (string.IsNullOrEmpty(s.Icon)) return false;
            var made = GeneratedIcon(s);
            if (made == null || made.Value.file != s.Icon) return true;
            string path = Path.Combine(Folder, s.Icon);
            if (!File.Exists(path)) return false;   // nothing there to keep
            string hash = Section("icons")[s.Ref]?["hash"]?.ToString();
            if (string.IsNullOrEmpty(hash)) return true;
            try { return IconHash(File.ReadAllBytes(path)) != hash; }
            catch (IOException) { return true; }
        }
        static string IconHash(byte[] png)
        {
            using var sha = System.Security.Cryptography.SHA1.Create();
            return Convert.ToBase64String(sha.ComputeHash(png));
        }
        /// <summary>The turn the builder's icon of a ship is drawn with (0 / 90 / 180 / 270 degrees about its up axis, the Ship
        /// tab's buttons beside Recreate icon), for a model whose nose doesn't come out at the bottom left.</summary>
        public int IconTurn(ProjectShip s) => Section("iconTurns")[s.Ref] is JValue v && v.Type == JTokenType.Integer ? (int)v : 0;
        public void SetIconTurn(ProjectShip s, int degrees)
        {
            degrees = ((degrees % 360) + 360) % 360;
            if (degrees == 0) Section("iconTurns").Remove(s.Ref);
            else Section("iconTurns")[s.Ref] = degrees;
        }
        public bool Flag(string key, bool fallback) => builder[key] is JValue v && v.Type == JTokenType.Boolean ? (bool)v : fallback;
        public void SetFlag(string key, bool on) => builder[key] = on;
        /// <summary>The station the builder sits at and test-flies from (ModBuilder.Station), -1 = the default.</summary>
        public int BuilderStation
        {
            get => builder["station"] is JValue v && v.Type == JTokenType.Integer ? (int)v : -1;
            set => builder["station"] = value;
        }
        /// <summary>The ship last worked on (its Ref), null = none.</summary>
        public string LastShip { get => Str(builder, "lastShip"); set => builder["lastShip"] = value; }
        /// <summary>A loadout key moves with a renamed ship.</summary>
        public void RenameRef(string from, string to)
        {
            var l = Section("loadouts");
            if (l[from] != null) { l[to] = l[from]; l.Remove(from); }
            var ic = Section("icons");
            if (ic[from] != null) { ic[to] = ic[from]; ic.Remove(from); }
            var it = Section("iconTurns");
            if (it[from] != null) { it[to] = it[from]; it.Remove(from); }
            if (LastShip == from) LastShip = to;
        }
    }
}
