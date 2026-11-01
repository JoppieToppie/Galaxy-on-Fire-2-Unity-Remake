// ModRegistry.cs
// The table numbers single player gives mod content (persistentDataPath/mod_registry.json, beside the Mods folder so that
// folder holds only mods; an older Mods/registry.json is moved there). The game refers to items,
// ships, systems and stations by their number everywhere (saves too), and the original tables are numbered 0..N-1, so a
// mod's new entries get the numbers after them. Each "mod:id" key keeps its number for good, whatever is turned on later
// or in which order, and a number is never handed out twice: a save holding a modded item finds it at the same number,
// and a mod turned off leaves a placeholder at its numbers (ModContent) until it comes back.
// So every number past the original tables belongs to this file, on every player's game differently, and the base game can't
// claim one: what the remake adds goes through here as well, keyed "gof2:<id>" (ModManager.BaseContent).
// Multiplayer sessions don't save, so they number their mods afresh in the session's order (the same on every player's
// game) and never touch this file.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModRegistry
    {
        public enum Kind { Item, Ship, System, Station, Race }

        static Dictionary<Kind, Dictionary<string, int>> table;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => table = null;

        static string FilePath => Path.Combine(GoF2Remake.GamePaths.Data, "mod_registry.json");
        static string OldPath => Path.Combine(ModManager.UserFolder, "registry.json");

        static Dictionary<Kind, Dictionary<string, int>> Table
        {
            get
            {
                if (table != null) return table;
                table = new Dictionary<Kind, Dictionary<string, int>>();
                foreach (Kind k in Enum.GetValues(typeof(Kind))) table[k] = new Dictionary<string, int>(StringComparer.Ordinal);
                try
                {
                    if (!File.Exists(FilePath) && File.Exists(OldPath)) File.Move(OldPath, FilePath);
                    if (File.Exists(FilePath) && JToken.Parse(File.ReadAllText(FilePath)) is JObject o)
                        foreach (Kind k in Enum.GetValues(typeof(Kind)))
                            if ((o[Key(k)] ?? (k == Kind.Race ? o["factions"] : null)) is JObject entries)   // (mod races' old name)
                                foreach (var p in entries.Properties())
                                    if (p.Value.Type == JTokenType.Integer) table[k][p.Name] = (int)p.Value;
                }
                catch (Exception e) { Debug.LogWarning($"Mods: registry.json unreadable ({e.Message}); numbering anew"); }
                return table;
            }
        }

        static string Key(Kind k) => k switch { Kind.Item => "items", Kind.Ship => "ships", Kind.System => "systems", Kind.Race => "races", _ => "stations" };

        /// <summary>Every key of a kind with its number.</summary>
        public static IReadOnlyDictionary<string, int> All(Kind kind) => Table[kind];

        public static bool TryGet(Kind kind, string key, out int index) => Table[kind].TryGetValue(key, out index);

        /// <summary>The key's number; a new key gets the first number after the original table and every number given out.</summary>
        public static int Assign(Kind kind, string key, int originalCount)
        {
            var t = Table[kind];
            if (t.TryGetValue(key, out int i)) return i;
            int next = originalCount;
            foreach (var v in t.Values) next = Math.Max(next, v + 1);
            t[key] = next;
            Save();
            return next;
        }

        /// <summary>A save made on another device names a key this one doesn't know: it takes the save's number when that is
        /// still free (false = taken, the save's entry can't keep its number).</summary>
        public static bool Adopt(Kind kind, string key, int index, int originalCount)
        {
            var t = Table[kind];
            if (t.TryGetValue(key, out int have)) return have == index;
            if (index < originalCount || t.ContainsValue(index)) return false;
            t[key] = index;
            Save();
            return true;
        }

        static void Save()
        {
            try
            {
                var o = new JObject();
                foreach (var kv in Table)
                {
                    var e = new JObject();
                    foreach (var p in kv.Value) e[p.Key] = p.Value;
                    o[Key(kv.Key)] = e;
                }
                File.WriteAllText(FilePath, o.ToString());
            }
            catch (Exception e) { Debug.LogWarning($"Mods: can't write registry.json: {e.Message}"); }
        }
    }
}
