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
        public static int PirateMoveMin { get; private set; } = 600;
        public static int PirateMoveMax { get; private set; } = 1200;
        public static int PirateEscapeSeconds { get; private set; } = 12;
        public static int PirateGuardCount { get; private set; } = 0;
        public static int PirateCapitalShip { get; private set; } = -1;
        public static int ShipsPerSide { get; private set; }
        public static int IntervalSeconds { get; private set; }
        public static int BattleSeconds { get; private set; }
        public static bool DynamicFrontlines { get; private set; } = true;

        // Default protected hubs. Additional core systems can be listed by active mods.
        static readonly HashSet<int> DefaultCores = new HashSet<int> { 3, 6, 9, 15, 25, 26 };
        static readonly HashSet<int> ExtraCores = new HashSet<int>();
        static readonly HashSet<int> ExplicitFrontlines = new HashSet<int>();
        static readonly HashSet<int> ExcludedSystems = new HashSet<int>();
        public static bool IsExplicitFrontline(SystemData system) => system != null && ExplicitFrontlines.Contains(system.index);
        public static bool IsWarOrbit(int orbit) => orbit >= 0 && orbit < 100000;
        public static bool CanContest(SystemData system) => system != null && system.raceId >= 0 && system.raceId <= 3 &&
            system.securityLevel < 3 && !ExtraCores.Contains(system.index) && !ExcludedSystems.Contains(system.index) &&
            (!DefaultCores.Contains(system.index) || IsExplicitFrontline(system));

        internal static void Apply()
        {
            Enabled = false;
            PirateMoveMin = 600; PirateMoveMax = 1200; PirateEscapeSeconds = 12; PirateGuardCount = 0; PirateCapitalShip = -1;
            ShipsPerSide = 12;
            IntervalSeconds = 1200;
            BattleSeconds = 480;
            DynamicFrontlines = true;
            ExtraCores.Clear();
            ExplicitFrontlines.Clear();
            ExcludedSystems.Clear();
            foreach (var mod in ModManager.Active)
            {
                try
                {
                    var value = ModJson.Read(mod.Source, File);
                    if (value == null) continue;
                    if (!(value is JObject options)) throw new ModJsonException($"{File} must contain an object.");
                    PirateMoveMin = Mathf.Clamp(ModJson.Int(options, "pirateMoveMin", PirateMoveMin, File), 60, 3600);
                    PirateMoveMax = Mathf.Clamp(ModJson.Int(options, "pirateMoveMax", PirateMoveMax, File), PirateMoveMin, 7200);
                    PirateEscapeSeconds = Mathf.Clamp(ModJson.Int(options, "pirateEscapeSeconds", PirateEscapeSeconds, File), 3, 120);
                    PirateGuardCount = Mathf.Clamp(ModJson.Int(options, "pirateGuardCount", PirateGuardCount, File), 0, 24);
                    PirateCapitalShip = ModJson.Int(options, "pirateCapitalShip", PirateCapitalShip, File);
                    bool enabled = ModJson.Bool(options, "enabled", false, File);
                    int ships = Mathf.Clamp(ModJson.Int(options, "shipsPerSide", ShipsPerSide, File), 4, 24);
                    int interval = Mathf.Clamp(ModJson.Int(options, "intervalSeconds", IntervalSeconds, File), 60, 86400);
                    int duration = Mathf.Clamp(ModJson.Int(options, "battleSeconds", BattleSeconds, File), 60, 3600);
                    bool dynamicFrontlines = ModJson.Bool(options, "dynamicFrontlines", DynamicFrontlines, File);
                    var cores = ReadSystems(options, "coreSystems", mod.Id);
                    var frontlines = ReadSystems(options, "frontlineSystems", mod.Id);
                    var excluded = ReadSystems(options, "excludedSystems", mod.Id);
                    ExtraCores.UnionWith(cores);
                    ExplicitFrontlines.UnionWith(frontlines);
                    ExcludedSystems.UnionWith(excluded);
                    DynamicFrontlines = dynamicFrontlines;
                    Enabled |= enabled;
                    ShipsPerSide = ships;
                    IntervalSeconds = interval;
                    BattleSeconds = duration;
                }
                catch (ModJsonException e) { Debug.LogWarning($"[{mod.Id}] {e.Message}"); }
            }
        }

        static HashSet<int> ReadSystems(JObject options, string field, string modId)
        {
            var result = new HashSet<int>();
            var list = ModJson.Get(options, field);
            if (list == null) return result;
            if (!(list is JArray entries))
                throw new ModJsonException($"{File}: {field} must be an array of system IDs or keys.");
            foreach (var entry in entries)
            {
                if (entry.Type != JTokenType.Integer && entry.Type != JTokenType.String)
                {
                    Debug.LogWarning($"[{modId}] {File}: {field}: skipping invalid system {entry}.");
                    continue;
                }
                string key = entry.Type == JTokenType.Integer ? entry.ToString() : (string)entry;
                if (!string.IsNullOrEmpty(key) && (ModWorld.TryResolveSystem(key, out int id) ||
                    (!key.Contains(":") && ModWorld.TryResolveSystem(modId + ":" + key, out id))))
                    result.Add(id);
                else Debug.LogWarning($"[{modId}] {File}: {field}: unknown system {key}.");
            }
            return result;
        }
    }
}
