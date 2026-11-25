// Opt-in territorial warfare between the four major NPC factions.
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModNpcFactionWar
    {
        public const string File = "npc-faction-war.json";
        public static bool Enabled { get; private set; }
        public static int ShipsPerSide { get; private set; }
        public static int IntervalSeconds { get; private set; }
        public static int BattleSeconds { get; private set; }

        // Default protected hubs. Additional core systems can be listed by active mods.
        static readonly HashSet<int> DefaultCores = new HashSet<int> { 3, 6, 9, 15, 25, 26 };
        static readonly HashSet<int> ExtraCores = new HashSet<int>();
        public static bool IsWarOrbit(int orbit) => orbit >= 0 && orbit < 100000;
        public static bool CanContest(SystemData system) => system != null && system.raceId >= 0 && system.raceId <= 3 &&
            system.securityLevel < 3 && !DefaultCores.Contains(system.index) && !ExtraCores.Contains(system.index);

        internal static void Apply()
        {
            Enabled = false;
            ShipsPerSide = 12;
            IntervalSeconds = 1200;
            BattleSeconds = 480;
            ExtraCores.Clear();
            foreach (var mod in ModManager.Active)
            {
                try
                {
                    var value = ModJson.Read(mod.Source, File);
                    if (value == null) continue;
                    if (!(value is JObject options)) throw new ModJsonException($"{File} must contain an object.");
                    bool enabled = ModJson.Bool(options, "enabled", false, File);
                    int ships = Mathf.Clamp(ModJson.Int(options, "shipsPerSide", ShipsPerSide, File), 4, 24);
                    int interval = Mathf.Clamp(ModJson.Int(options, "intervalSeconds", IntervalSeconds, File), 60, 86400);
                    int duration = Mathf.Clamp(ModJson.Int(options, "battleSeconds", BattleSeconds, File), 60, 3600);
                    var coreList = ModJson.Get(options, "coreSystems");
                    if (coreList != null && !(coreList is JArray))
                        throw new ModJsonException($"{File}: coreSystems must be an array of system IDs or keys.");
                    if (coreList is JArray cores)
                        foreach (var core in cores)
                        {
                            if (core.Type != JTokenType.Integer && core.Type != JTokenType.String)
                            {
                                Debug.LogWarning($"[{mod.Id}] {File}: skipping invalid core system {core}.");
                                continue;
                            }
                            string key = core.Type == JTokenType.Integer ? core.ToString() : (string)core;
                            if (ModWorld.TryResolveSystem(key, out int id)) ExtraCores.Add(id);
                            else if (!string.IsNullOrEmpty(key) && !key.Contains(":") &&
                                ModWorld.TryResolveSystem(mod.Id + ":" + key, out id)) ExtraCores.Add(id);
                            else Debug.LogWarning($"[{mod.Id}] {File}: unknown core system {key}.");
                        }
                    Enabled |= enabled;
                    ShipsPerSide = ships;
                    IntervalSeconds = interval;
                    BattleSeconds = duration;
                }
                catch (ModJsonException e) { Debug.LogWarning($"[{mod.Id}] {e.Message}"); }
            }
        }
    }
}
