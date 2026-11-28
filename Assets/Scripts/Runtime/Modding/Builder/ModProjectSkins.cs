// ModProjectSkins.cs
// ModProject: ships, skins, items and characters (part of the partial ModProject; its fields are in ModProject.cs).

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
        /// <summary>A new ship id (a-z 0-9 _) from a name, unique in the project.</summary>
        public string ShipIdFor(string name) => UniqueId(name, "ship", id => Ships.Any(s => s.LocalId == id));
        public string ItemIdFor(string name) => UniqueId(name, "item", id => Items.Any(s => s.LocalId == id));
        public string CharacterIdFor(string name) => UniqueId(name, "character", id => Characters.Any(s => s.Id == id));
        internal static string UniqueId(string name, string fallback, Func<string, bool> taken)
        {
            var chars = (name ?? "").Trim().ToLowerInvariant().Select(c => c >= 'a' && c <= 'z' || c >= '0' && c <= '9' ? c : '_').ToArray();
            string id = new string(chars).Trim('_');
            while (id.Contains("__")) id = id.Replace("__", "_");
            if (id.Length == 0) id = fallback;
            if (id.Length > 32) id = id.Substring(0, 32).Trim('_');
            string unique = id;
            for (int i = 2; taken(unique); i++) unique = $"{id}_{i}";
            return unique;
        }
        /// <summary>Copies a file into one of the project's folders under a name not used yet (a new name for every copy, so
        /// nothing that loaded the old file shows it again); its path inside the project ("portraits/x.png").</summary>
        public string AddFile(string sourceFile, string folder, string baseName, string extension)
        {
            string dir = Path.Combine(Folder, folder);
            Directory.CreateDirectory(dir);
            string name = baseName + extension;
            for (int i = 2; File.Exists(Path.Combine(dir, name)) || issued.Contains(Path.Combine(dir, name)); i++) name = $"{baseName}_{i}{extension}";
            issued.Add(Path.Combine(dir, name));
            File.Copy(sourceFile, Path.Combine(dir, name), true);
            return folder + "/" + name;
        }
        /// <summary>Deletes a file of the project unless 'stillUsed' (a portrait something else still names).</summary>
        public void DeleteFile(string path, bool stillUsed)
        {
            if (string.IsNullOrEmpty(path) || stillUsed) return;
            try { File.Delete(Path.Combine(Folder, path)); } catch { }
        }
        /// <summary>The game ships the project re-skins: the ones added in the builder (builder.json "skins") and any whose
        /// texture a file in textures/ replaces ("ship_028_terran_diffuse" is ship 28's; v_ / sn_ add-on ships too). Also the
        /// project's new ships on a game model with textures of their own (textures/ships/&lt;id&gt;/, or their id in "skins"),
        /// by the number the game gave them (none while the project can't be read).</summary>
        public List<int> SkinShips()
        {
            var set = new SortedSet<int>();
            if (builder["skins"] is JArray a) foreach (var t in a) if (t.Type == JTokenType.Integer) set.Add((int)t);
            foreach (var s in Ships)
            {
                if (!OwnsSkinOf(s)) continue;
                bool listed = builder["skins"] is JArray l && l.Any(t => t.Type == JTokenType.String && (string)t == s.LocalId);
                if (!listed && SkinTextureFiles(ShipSkinScope(s)).Count == 0) continue;
                int i = ModContent.ShipIndexOf(Key(s.LocalId));
                if (i >= 0) set.Add(i);
            }
            string dir = Path.Combine(Folder, ModContent.TexturesFolder);
            var shipName = new System.Text.RegularExpressions.Regex(@"^(?:v_|sn_)?ship_(\d{3})_");
            if (Directory.Exists(dir))
            {
                foreach (var f in Directory.GetFiles(dir))
                {
                    var m = shipName.Match(Path.GetFileNameWithoutExtension(f));
                    if (m.Success && ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())) set.Add(int.Parse(m.Groups[1].Value));
                }
                // a ship's own textures: textures/<its assembly>/
                foreach (var d in Directory.GetDirectories(dir))
                {
                    var m = shipName.Match(Path.GetFileName(d));
                    if (m.Success && Directory.GetFiles(d).Any(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())))
                        set.Add(int.Parse(m.Groups[1].Value));
                }
            }
            return set.ToList();
        }
        /// <summary>The project's replacements of game textures that aren't a ship's (stations, planets, suns, rings...):
        /// the texture names in textures/.</summary>
        public List<string> OtherTextures()
        {
            var list = new List<string>();
            string dir = Path.Combine(Folder, ModContent.TexturesFolder);
            if (!Directory.Exists(dir)) return list;
            var shipName = new System.Text.RegularExpressions.Regex(@"^(?:v_|sn_)?ship_(\d{3})_");
            foreach (var f in Directory.GetFiles(dir))
            {
                string n = Path.GetFileNameWithoutExtension(f);
                if (!shipName.IsMatch(n) && ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())) list.Add(n);
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }
        /// <summary>backdrop.json (the retextured planets drawn as stars), null = none.</summary>
        public JObject Backdrop
        {
            get
            {
                string f = Path.Combine(Folder, "backdrop.json");
                try { return File.Exists(f) ? JToken.Parse(File.ReadAllText(f)) as JObject : null; }
                catch { return null; }
            }
            set
            {
                string f = Path.Combine(Folder, "backdrop.json");
                if (value == null || !value.HasValues) { try { if (File.Exists(f)) File.Delete(f); } catch { } return; }
                SafeWriteBytes(f, new System.Text.UTF8Encoding(false).GetBytes(value.ToString(Formatting.Indented)));
            }
        }
        public void AddSkin(int ship)
        {
            if (!(builder["skins"] is JArray a)) builder["skins"] = a = new JArray();
            // The project's own ship by its id (its number is only this run's).
            if (OwnSkinShip(ship) is ProjectShip own) { if (!a.Any(t => t.Type == JTokenType.String && (string)t == own.LocalId)) a.Add(own.LocalId); return; }
            if (!a.Any(t => t.Type == JTokenType.Integer && (int)t == ship)) a.Add(ship);
        }
        public void RemoveSkin(int ship)
        {
            if (!(builder["skins"] is JArray a)) return;
            string id = OwnSkinShip(ship)?.LocalId;
            foreach (var t in a.Where(t => t.Type == JTokenType.Integer && (int)t == ship || id != null && t.Type == JTokenType.String && (string)t == id).ToList())
                t.Remove();
        }
        /// <summary>A new ship of the project that flies one of the game's models: it can have textures of its own.</summary>
        public static bool OwnsSkinOf(ProjectShip s) => s != null && !s.IsOverride && s.HasGameModel && !string.IsNullOrEmpty(s.LocalId);
        /// <summary>The project's new ship on a game model with game number 'ship' (null: none, or a game ship).</summary>
        public ProjectShip OwnSkinShip(int ship) =>
            ship < 0 ? null : Ships.FirstOrDefault(s => OwnsSkinOf(s) && ModContent.ShipIndexOf(Key(s.LocalId)) == ship);
        /// <summary>Where a new ship's own textures go under textures/ ("ships/&lt;id&gt;", ModContent.ShipTexturesFolder; the
        /// SkinTexture / SetSkinTexture scope), null when it can't have any.</summary>
        public static string ShipSkinScope(ProjectShip s) =>
            OwnsSkinOf(s) ? ModContent.ShipTexturesFolder.Substring(ModContent.TexturesFolder.Length + 1) + "/" + s.LocalId : null;
        /// <summary>The pictures under textures/&lt;scope&gt;/ (texture names), empty for none.</summary>
        public List<string> SkinTextureFiles(string scope)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(scope)) return list;
            string dir = Path.Combine(Folder, ModContent.TexturesFolder, scope);
            if (!Directory.Exists(dir)) return list;
            foreach (var f in Directory.GetFiles(dir))
                if (ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())) list.Add(Path.GetFileNameWithoutExtension(f));
            return list;
        }
        /// <summary>The project's replacement of a game texture (textures/&lt;name&gt;.png or .jpg; with a scope, the one
        /// for that assembly's objects only, textures/&lt;scope&gt;/&lt;name&gt;), null = none.</summary>
        public string SkinTexture(string textureName, string scope = null)
        {
            string folder = scope == null ? ModContent.TexturesFolder : ModContent.TexturesFolder + "/" + scope;
            foreach (var ext in ImageExtensions)
            {
                string rel = folder + "/" + textureName + ext;
                if (File.Exists(Path.Combine(Folder, rel))) return rel;
            }
            return null;
        }
        /// <summary>A PNG as the replacement of a game texture, for every object or (scope) one assembly's only (any other
        /// one of that name there goes).</summary>
        public void SetSkinTexture(string textureName, string sourceFile, string scope = null)
        {
            RemoveSkinTexture(textureName, scope);
            string dir = scope == null ? Path.Combine(Folder, ModContent.TexturesFolder) : Path.Combine(Folder, ModContent.TexturesFolder, scope);
            Directory.CreateDirectory(dir);
            File.Copy(sourceFile, Path.Combine(dir, textureName + ".png"), true);
        }
        public void RemoveSkinTexture(string textureName, string scope = null)
        {
            string dir = scope == null ? Path.Combine(Folder, ModContent.TexturesFolder) : Path.Combine(Folder, ModContent.TexturesFolder, scope);
            foreach (var ext in ImageExtensions)
            {
                string path = Path.Combine(dir, textureName + ext);
                if (File.Exists(path)) File.Delete(path);
            }
            if (scope != null && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
        // ---- items and characters ------------------------------------------------------------------------------
        /// <summary>A new item: a copy of the game item 'baseItem' under its own name.</summary>
        public ProjectItem AddItem(int baseItem, string name)
        {
            var it = new ProjectItem(new JObject { ["id"] = ItemIdFor(name), ["base"] = baseItem, ["name"] = name, ["description"] = "" });
            Items.Add(it);
            return it;
        }
        /// <summary>A change to the game item 'item' (nothing changed until a field is given).</summary>
        public ProjectItem AddItemOverride(int item)
        {
            var it = new ProjectItem(new JObject { ["override"] = item });
            Items.Add(it);
            return it;
        }
        /// <summary>A new character with its portrait (a path in the project).</summary>
        public ProjectCharacter AddCharacter(string name, string portrait)
        {
            var c = new ProjectCharacter(new JObject
            {
                ["id"] = CharacterIdFor(name), ["name"] = name, ["portrait"] = portrait, ["race"] = "terran", ["gender"] = "male",
            });
            Characters.Add(c);
            return c;
        }
    }
}
