// GameModels.cs
// Remake-only: a mod's "model" may name one of the game's own models instead of a glTF / GLB file in the mod:
// "gof2:<model name>" (assemblies.json's names, e.g. "gof2:ship_005_terran", "gof2:turret_002_ship_mounted",
// "gof2:station_078_midorian"; Modding/ai/gof2-modding/reference.md lists them all), so a new ship, station, turret or
// weapon effect needs no 3D file of its own. The loaders (ModShips / ModContent for ships, ModStations, ModWeapons) take
// the game's prefab (Visuals.AssembledObject.LoadPrefab) where they would read the file. Plain C#.

using System;
using GoF2Remake.Data;

namespace GoF2Remake.Modding
{
    public static class GameModels
    {
        public const string Prefix = "gof2:";

        /// <summary>"gof2:<name>": true with the model's name (trimmed); a file path: false.</summary>
        public static bool Is(string model, out string name)
        {
            name = null;
            if (string.IsNullOrEmpty(model) || !model.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;
            name = model.Substring(Prefix.Length).Trim();
            return true;
        }

        public static bool Is(string model) => Is(model, out _);

        /// <summary>The model reference for a game model's name ("gof2:" + name).</summary>
        public static string Ref(string name) => Prefix + name;

        /// <summary>The game's model by name (null: no such assembly in the game's own table).</summary>
        public static AssemblyData Find(Database db, string name) =>
            db == null || string.IsNullOrEmpty(name) ? null : db.Assemblies.Find(a => a.name == name && a.pack != ModContent.Pack);

        /// <summary>One of the game's single-mesh props that has no assembly (null = none): "beer" (0x37d0) and "bra" (0x37d1),
        /// the ending's menu-backdrop statics (Level::createScene, StoryAssets.menuStatics), the only way a build reaches them.
        /// For weapon effects ("gof2:bra" as a projectile: players asked for the exploding bra, 2026-10-17).</summary>
        public static UnityEngine.GameObject Prop(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var story = StoryAssets.Load();
            if (story == null || story.menuStatics == null) return null;
            foreach (var p in story.menuStatics)
                if (p != null && string.Equals(p.name, name, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        /// <summary>An "icon" that is one of the game's shop icons ("gof2:item_046", "gof2:ship_005": Resources/GoF2Icons),
        /// null = not one of them / no such icon.</summary>
        public static UnityEngine.Texture2D Icon(string icon) =>
            Is(icon, out string name) && name.Length > 0 ? GoF2Remake.Data.OriginalTextures.Image("GoF2Icons/" + name) : null;

        /// <summary>The original ship (0..63) whose model this is (any race's variant of its group), -1 = none: its
        /// hardpoints, hangar height and icon go with the model.</summary>
        public static int ShipOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            if (name.StartsWith("v_")) name = name.Substring(2);
            else if (name.StartsWith("sn_")) name = name.Substring(3);
            // Database.ShipAssembly's names: "ship_NNN_<race>".
            if (name.Length < 9 || !name.StartsWith("ship_") || name[8] != '_') return -1;
            return int.TryParse(name.Substring(5, 3), out int i) && i >= 0 && i < 64 ? i : -1;
        }
    }
}
