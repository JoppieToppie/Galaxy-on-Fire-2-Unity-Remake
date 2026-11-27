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
        // Also accept a staged config from older Mod Builder MCP versions that cannot write this root file.
        public const string StagedFile = "text/npc-faction-war.json";
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
        public static bool GateOnlyFrontlines { get; private set; }

        public sealed class LootRule
        {
            public string item;
            public int amount, chancePercent = 100, minReputation = -100, minPriorVictories;
            public int reputationChanceBonusPercent, victoryChanceBonusPercent, victoryChanceCapPercent = 100;
            public bool? scaleQuantity;

            public bool Eligible(int reputation, int priorVictories) =>
                reputation >= minReputation && priorVictories >= minPriorVictories;

            public int Chance(int reputation, int priorVictories) =>
                Mathf.Clamp(chancePercent + Mathf.Clamp(reputation, 0, 100) * reputationChanceBonusPercent / 100 +
                    (int)System.Math.Min(victoryChanceCapPercent, (long)System.Math.Max(0, priorVictories) * victoryChanceBonusPercent), 0, 100);
        }

        public sealed class RewardSettings
        {
            public long credits;
            public string distribution = "legacy";
            public int reputationBonusPercent, participationBonusPercent, participationBonusCapPercent = 100;
            public bool scaleLoot;

            public int BonusPercent(int reputation, int previousWins) =>
                Mathf.Clamp(reputation, 0, 100) * reputationBonusPercent / 100 +
                (int)System.Math.Min(participationBonusCapPercent, (long)System.Math.Max(0, previousWins) * participationBonusPercent);

            public long Scale(long amount, int bonus) => amount * (100L + bonus) / 100L;
            public List<LootRule> loot = new List<LootRule>();
        }

        static RewardSettings defaultReward = new RewardSettings { credits = 100000 };
        static readonly Dictionary<int, RewardSettings> factionRewards = new Dictionary<int, RewardSettings>();
        public static RewardSettings RewardFor(int race) =>
            factionRewards.TryGetValue(race, out var reward) ? reward : defaultReward;

        // Default protected hubs. Additional core systems can be listed by active mods.
        static readonly HashSet<int> DefaultCores = new HashSet<int> { 3, 6, 9, 15, 25, 26 };
        static readonly HashSet<int> ExtraCores = new HashSet<int>();
        static readonly HashSet<int> UnprotectedDefaultCores = new HashSet<int>();
        static readonly HashSet<int> ExplicitFrontlines = new HashSet<int>();
        static readonly HashSet<int> ExcludedSystems = new HashSet<int>();
        public static bool IsExplicitFrontline(SystemData system) => system != null && ExplicitFrontlines.Contains(system.index);
        public static bool IsWarOrbit(int orbit) => orbit >= 0 && orbit < 100000;
        public static bool CanContest(SystemData system) => system != null && system.raceId >= 0 && system.raceId <= 3 &&
            system.securityLevel < 3 && !ExtraCores.Contains(system.index) && !ExcludedSystems.Contains(system.index) &&
            (!DefaultCores.Contains(system.index) || UnprotectedDefaultCores.Contains(system.index) || IsExplicitFrontline(system));

        internal static void Apply()
        {
            Enabled = false;
            defaultReward = new RewardSettings { credits = 100000 };
            factionRewards.Clear();
            PirateMoveMin = 600; PirateMoveMax = 1200; PirateEscapeSeconds = 12; PirateGuardCount = 0; PirateCapitalShip = -1;
            ShipsPerSide = 12;
            IntervalSeconds = 1200;
            BattleSeconds = 480;
            DynamicFrontlines = true;
            GateOnlyFrontlines = false;
            UnprotectedDefaultCores.Clear();
            ExtraCores.Clear();
            ExplicitFrontlines.Clear();
            ExcludedSystems.Clear();
            foreach (var mod in ModManager.Active)
            {
                try
                {
                    var value = ModJson.Read(mod.Source, mod.Source.Exists(File) ? File : StagedFile);
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
                    bool gateOnlyFrontlines = ModJson.Bool(options, "gateOnlyFrontlines", GateOnlyFrontlines, File);
                    var unprotected = ReadSystems(options, "unprotectedDefaultCores", mod.Id);
                    var cores = ReadSystems(options, "coreSystems", mod.Id);
                    var frontlines = ReadSystems(options, "frontlineSystems", mod.Id);
                    var excluded = ReadSystems(options, "excludedSystems", mod.Id);
                    ReadRewards(options, mod.Id);
                    UnprotectedDefaultCores.UnionWith(unprotected);
                    ExtraCores.UnionWith(cores);
                    ExplicitFrontlines.UnionWith(frontlines);
                    ExcludedSystems.UnionWith(excluded);
                    DynamicFrontlines = dynamicFrontlines;
                    GateOnlyFrontlines = gateOnlyFrontlines;
                    Enabled |= enabled;
                    ShipsPerSide = ships;
                    IntervalSeconds = interval;
                    BattleSeconds = duration;
                }
                catch (ModJsonException e) { Debug.LogWarning($"[{mod.Id}] {e.Message}"); }
            }
        }

        // Numeric race IDs 0..3 refer to the existing NPC warfare factions. Faction entries override
        // the default credit pool and/or the complete loot list; omission inherits the default.
        static void ReadRewards(JObject options, string modId)
        {
            var token = ModJson.Get(options, "rewards");
            if (token == null) return;
            if (!(token is JObject root)) throw new ModJsonException($"{File}: rewards must be an object.");
            if (root["default"] != null)
                defaultReward = ReadReward(root["default"], defaultReward, modId);
            for (int race = 0; race < 4; race++)
                if (root[race.ToString(System.Globalization.CultureInfo.InvariantCulture)] is JToken entry)
                    factionRewards[race] = ReadReward(entry, RewardFor(race), modId);
        }

        static RewardSettings ReadReward(JToken token, RewardSettings fallback, string modId)
        {
            if (!(token is JObject obj)) throw new ModJsonException($"{File}: reward must be an object.");
            var reward = new RewardSettings
            {
                credits = Mathf.Clamp(ModJson.Int(obj, "credits", (int)fallback.credits, File), 0, int.MaxValue),
                distribution = ModJson.Str(obj, "distribution", fallback.distribution).ToLowerInvariant(),
                reputationBonusPercent = Mathf.Clamp(ModJson.Int(obj, "reputationBonusPercent", fallback.reputationBonusPercent, File), 0, 1000),
                participationBonusPercent = Mathf.Clamp(ModJson.Int(obj, "participationBonusPercent", fallback.participationBonusPercent, File), 0, 1000),
                participationBonusCapPercent = Mathf.Clamp(ModJson.Int(obj, "participationBonusCapPercent", fallback.participationBonusCapPercent, File), 0, 1000),
                scaleLoot = ModJson.Bool(obj, "scaleLoot", fallback.scaleLoot, File),
                loot = new List<LootRule>(fallback.loot)
            };
            if (reward.distribution != "legacy" && reward.distribution != "split" && reward.distribution != "flat")
                throw new ModJsonException($"{File}: distribution must be legacy, split or flat.");
            if (ModJson.Get(obj, "loot") == null) return reward;
            if (!(obj["loot"] is JArray entries) || entries.Count > 32)
                throw new ModJsonException($"{File}: reward loot must be an array of at most 32 entries.");
            reward.loot.Clear();
            foreach (var entry in entries)
            {
                if (!(entry is JObject row)) throw new ModJsonException($"{File}: invalid reward loot entry.");
                string key = row["item"]?.ToString();
                int amount = ModJson.Int(row, "amount", 1, File);
                if (string.IsNullOrEmpty(key) || amount < 1 || amount > 10000 ||
                    (!ModContent.TryResolveItem(key, out int item) &&
                     !ModContent.TryResolveItem(modId + ":" + key, out item)))
                    throw new ModJsonException($"{File}: unknown or invalid reward item {key}.");
                reward.loot.Add(new LootRule
                {
                    item = ModContent.ItemKey(item) ?? item.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    amount = amount,
                    chancePercent = Mathf.Clamp(ModJson.Int(row, "chancePercent", 100, File), 0, 100),
                    minReputation = Mathf.Clamp(ModJson.Int(row, "minReputation", -100, File), -100, 100),
                    minPriorVictories = Mathf.Clamp(ModJson.Int(row, "minPriorVictories", 0, File), 0, int.MaxValue),
                    reputationChanceBonusPercent = Mathf.Clamp(ModJson.Int(row, "reputationChanceBonusPercent", 0, File), 0, 100),
                    victoryChanceBonusPercent = Mathf.Clamp(ModJson.Int(row, "victoryChanceBonusPercent", 0, File), 0, 100),
                    victoryChanceCapPercent = Mathf.Clamp(ModJson.Int(row, "victoryChanceCapPercent", 100, File), 0, 100),
                    scaleQuantity = ModJson.Get(row, "scaleQuantity") == null ? (bool?)null : ModJson.Bool(row, "scaleQuantity", false, File)
                });
            }
            return reward;
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
