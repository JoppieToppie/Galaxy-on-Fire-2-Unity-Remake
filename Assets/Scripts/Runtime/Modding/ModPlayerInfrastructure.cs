using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace GoF2Remake.Modding
{
    public sealed class ModPlayerInfrastructure
    {
        public const string FileName = "playerInfrastructure.json";
        public const float DefaultScrapIntegrity = 5000f;
        public const int DefaultOrbitalDefenseLimit = 3;
        public const int MaxOrbitalDefenseLimit = 12;
        public const float DefaultOrbitalDecayPercentPerDay = 1f;
        public const int DefaultDefenseMaintenanceSeconds = 7 * 86400;
        public const float MinimumOrbitalDecayIntegrityPercent = 15f;
        public const int MaxOtherOrbitalStructureLimit = 8;
        public const float MaxStructureScale = 6f;

        public bool Enabled { get; private set; } = true;
        public bool JumpgatesEnabled { get; private set; } = true;
        public int StationRaceMarketLimitPercent { get; private set; }
        public bool StationRaceRevenueToOwner { get; private set; }
        public bool StationRacePoliciesEnabled { get; private set; }
        public bool StationRaceExemptionsEnabled { get; private set; }
        public bool PlayerIdentityEnabled { get; private set; }
        public bool JumpgateRacePoliciesEnabled { get; private set; }
        public bool JumpgateRaceExemptionsEnabled { get; private set; }
        internal bool RestrictStationModels { get; private set; }
        public bool IncludeStockStations { get; private set; } = true;
        public bool IncludeStockJumpgates { get; private set; } = true;
        public const int DefaultStationBuildSeconds = 12 * 60 * 60;
        public const int MaxBuildSeconds = 30 * 24 * 60 * 60;
        internal static readonly string[] TimedModules = { "storage", "refinery", "factory", "hangar", "drydock", "dockarm" };
        public const int DefaultStorageCapacityPerLevel = 2000;
        public const int DefaultStorageItemTypes = 64;
        public const int DefaultStationClaimPrice = 25_000_000;
        public const int DefaultStationRenamePrice = 300_000;
        public int StorageCapacityPerLevel { get; private set; } = DefaultStorageCapacityPerLevel;
        public int StorageItemTypes { get; private set; } = DefaultStorageItemTypes;
        public int StationClaimPrice { get; private set; } = DefaultStationClaimPrice;
        public int StationRenamePrice { get; private set; } = DefaultStationRenamePrice;
        public bool SiegeRespawnFactionOfficers { get; private set; } = true;
        public bool SiegeRespawnStationAdministrators { get; private set; } = true;
        public bool SiegeRespawnOriginalBuilders { get; private set; } = true;
        public int StationBuildSeconds { get; private set; } = DefaultStationBuildSeconds;
        public int JumpgateBuildSeconds { get; private set; }
        public IReadOnlyDictionary<int, (int Output, int InputUnits, int OutputUnits)> RefineryRecipes { get; private set; } =
            StockRefineryRecipes();
        static Dictionary<int, (int, int, int)> StockRefineryRecipes()
        {
            var recipes = new Dictionary<int, (int, int, int)>();
            for (int ore = 154; ore <= 164; ore++) recipes.Add(ore, (ore + 11, 10, 1));
            recipes.Add(217, (218, 10, 1));
            return recipes;
        }

        internal sealed class FactoryDefinition
        {
            public int Output, OutputUnits;
            public List<(int item, int amount)> Inputs;
        }

        internal IReadOnlyDictionary<int, FactoryDefinition> FactoryRecipes { get; private set; } =
            new Dictionary<int, FactoryDefinition>();

        readonly Dictionary<string, int> moduleBuildSeconds = new Dictionary<string, int>();
        public int ModuleBuildSeconds(string kind) => moduleBuildSeconds.TryGetValue(kind, out int seconds) ? seconds : 0;

        public int OrbitalDefenseLimit { get; private set; } = DefaultOrbitalDefenseLimit;
        public int OtherOrbitalStructureLimit { get; private set; } = MaxOtherOrbitalStructureLimit;
        public float MinimumStationScale { get; private set; } = 1f;
        public float MinimumJumpgateScale { get; private set; } = 1f;
        public float ScrapIntegrity { get; private set; } = DefaultScrapIntegrity;
        public IReadOnlyList<string> StationModels { get; private set; } = Array.Empty<string>();
        public IReadOnlyDictionary<string, string> StationModelImages { get; private set; } = new Dictionary<string, string>();
        static ModInfo currentRulesMod;

        internal static (string id, UnityEngine.Texture2D texture) StationModelImage(int model)
        {
            ResolveActiveRules();
            foreach (var entry in current.StationModelImages)
            {
                if (!(int.TryParse(entry.Key, out int numeric) && numeric == model) &&
                    !(ModWorld.TryResolveStation(entry.Key, out int index) && index == model)) continue;
                var texture = GameModels.Is(entry.Value) ? GameModels.Icon(entry.Value) :
                    currentRulesMod != null ? ModMaterials.Texture(currentRulesMod, entry.Value, false, true) : null;
                return (entry.Value, texture);
            }
            return (null, null);
        }
        public IReadOnlyList<OrbitalDefinition> OrbitalStructures { get; private set; } = StockStructures;
        public IReadOnlyList<string> JumpgateModels { get; private set; } = Array.Empty<string>();

        public sealed class OrbitalDefinition
        {
            public string Id, Name, Assembly, StationModel;
            public float ModelScale, HitRadius, Integrity, Range, Damage;
            public int BuildSeconds, FireIntervalSeconds;
            public bool Defense;
            public bool TargetPlayers = true, TargetNpcs = true;
            public IReadOnlyList<string> SponsorRaces = Array.Empty<string>();
            public List<(int item, int amount)> Materials;
            public int MaintenanceIntervalSeconds;
            public float MaintenanceDecayPercentPerDay = DefaultOrbitalDecayPercentPerDay;
            public float MinimumMaintenanceIntegrityPercent = MinimumOrbitalDecayIntegrityPercent;
            public List<(int item, int amount)> MaintenanceMaterials = new List<(int, int)>();
        }

        internal static readonly string[] SentryAssemblies = { "sn_sentry_gun_001", "sn_sentry_gun_002", "sn_sentry_gun_003" };
        // Deployable sentries use a 0.5 model scale. Station defenses are 35 times that size.
        internal const float DefenseModelScale = .5f * 35f;

        internal static string DefenseAssembly(string assembly) =>
            Array.IndexOf(SentryAssemblies, assembly) >= 0 ? assembly : SentryAssemblies[0];

        static OrbitalDefinition StockDefense(int model) => new OrbitalDefinition
        {
            Id = model == 0 ? "orbital-defense" : "orbital-defense-" + (model + 1),
            Name = "Orbital Sentry " + (model + 1), Defense = true, Assembly = SentryAssemblies[model],
            ModelScale = DefenseModelScale, HitRadius = 250f, Integrity = 12000f, Range = 5000f, Damage = 120f,
            FireIntervalSeconds = 3, Materials = new List<(int, int)> { (156, 200), (155, 100), (118, 80), (127, 50) },
            MaintenanceIntervalSeconds = DefaultDefenseMaintenanceSeconds,
            MaintenanceMaterials = new List<(int, int)> { (156, 20), (155, 10), (118, 8), (127, 5) }
        };

        static readonly OrbitalDefinition[] StockStructures = { StockDefense(0), StockDefense(1), StockDefense(2) };

        static readonly ModPlayerInfrastructure Defaults = new ModPlayerInfrastructure();
        static ModPlayerInfrastructure current = Defaults;
        static int currentRevision = -1;
        static (float Station, float Jumpgate) minimumScales = (1f, 1f);

        public static ModPlayerInfrastructure Current
        {
            get
            {
                ResolveActiveRules();
                return current;
            }
        }

        public static (float Station, float Jumpgate) ActiveMinimumScales
        {
            get
            {
                ResolveActiveRules();
                return minimumScales;
            }
        }

        static void ResolveActiveRules()
        {
            var mods = ModManager.Active;
            if (currentRevision == ModManager.Revision) return;
            current = Defaults;
            currentRulesMod = null;
            minimumScales = (1f, 1f);
            foreach (var mod in mods)
            {
                if (!(ModContent.Parse(mod).playerInfrastructure is ModPlayerInfrastructure rules)) continue;
                current = rules;
                currentRulesMod = mod;
                minimumScales.Station = Math.Max(minimumScales.Station, rules.MinimumStationScale);
                minimumScales.Jumpgate = Math.Max(minimumScales.Jumpgate, rules.MinimumJumpgateScale);
            }
            currentRevision = ModManager.Revision;
        }

        internal static ModPlayerInfrastructure Read(ModInfo mod)
        {
            if (!mod.Source.Exists(FileName)) return null;
            return Parse(ModJson.Read(mod.Source, FileName));
        }

        internal static ModPlayerInfrastructure Parse(JToken token)
        {
            if (!(token is JObject settings)) throw new ModJsonException(FileName + " must contain a JSON object.");
            float integrity = ModJson.Float(settings, "scrapIntegrity", DefaultScrapIntegrity, FileName);
            if (float.IsNaN(integrity) || float.IsInfinity(integrity) || integrity < 1 || integrity > 10000000)
                throw new ModJsonException(FileName + " scrapIntegrity must be between 1 and 10,000,000.");
            var rules = new ModPlayerInfrastructure
            {
                RestrictStationModels = true,
                Enabled = ReadToggle(settings, "enabled"),
                JumpgatesEnabled = ReadToggle(settings, "jumpgatesEnabled"),
                StationRaceMarketLimitPercent = StructureInt(settings, "stationRaceMarketLimitPercent", 0, 0, 100),
                StationRaceRevenueToOwner = settings["stationRaceRevenueToOwner"] != null &&
                    ReadToggle(settings, "stationRaceRevenueToOwner"),
                StationRacePoliciesEnabled = settings["stationRacePoliciesEnabled"] != null &&
                    ReadToggle(settings, "stationRacePoliciesEnabled"),
                StationRaceExemptionsEnabled = settings["stationRaceExemptionsEnabled"] != null &&
                    ReadToggle(settings, "stationRaceExemptionsEnabled"),
                PlayerIdentityEnabled = settings["playerIdentityEnabled"] != null && ReadToggle(settings, "playerIdentityEnabled"),
                JumpgateRacePoliciesEnabled = settings["jumpgateRacePoliciesEnabled"] != null &&
                    ReadToggle(settings, "jumpgateRacePoliciesEnabled"),
                JumpgateRaceExemptionsEnabled = settings["jumpgateRaceExemptionsEnabled"] != null &&
                    ReadToggle(settings, "jumpgateRaceExemptionsEnabled"),
                IncludeStockStations = ReadToggle(settings, "includeStockStations"),
                IncludeStockJumpgates = ReadToggle(settings, "includeStockJumpgates"),
                ScrapIntegrity = integrity,
                StorageCapacityPerLevel = StructureInt(settings, "storageCapacityPerLevel", DefaultStorageCapacityPerLevel, 1, 1000000),
                StorageItemTypes = StructureInt(settings, "storageItemTypes", DefaultStorageItemTypes, 1, 4096),
                StationClaimPrice = StructureInt(settings, "stationClaimPrice", DefaultStationClaimPrice, 0, int.MaxValue),
                StationRenamePrice = StructureInt(settings, "stationRenamePrice", DefaultStationRenamePrice, 0, int.MaxValue),
                SiegeRespawnFactionOfficers = ReadToggle(settings, "siegeRespawnFactionOfficers"),
                SiegeRespawnStationAdministrators = ReadToggle(settings, "siegeRespawnStationAdministrators"),
                SiegeRespawnOriginalBuilders = ReadToggle(settings, "siegeRespawnOriginalBuilders"),
                StationBuildSeconds = StructureInt(settings, "stationBuildSeconds", DefaultStationBuildSeconds, 0, MaxBuildSeconds),
                JumpgateBuildSeconds = StructureInt(settings, "jumpgateBuildSeconds", 0, 0, MaxBuildSeconds),
                MinimumStationScale = ReadMinimumScale(settings, "minimumStationScale"),
                MinimumJumpgateScale = ReadMinimumScale(settings, "minimumJumpgateScale"),
                OrbitalDefenseLimit = StructureInt(settings, "orbitalDefenseLimit", DefaultOrbitalDefenseLimit, 0, MaxOrbitalDefenseLimit),
                OtherOrbitalStructureLimit = StructureInt(settings, "otherOrbitalStructureLimit", MaxOtherOrbitalStructureLimit, 0, MaxOtherOrbitalStructureLimit),
                OrbitalStructures = ReadStructures(settings),
                StationModels = ReadReferences(settings, "stationModels"),
                StationModelImages = ReadModelImages(settings),
                JumpgateModels = ReadReferences(settings, "jumpgateModels")
            };
            if (settings["moduleBuildSeconds"] != null)
            {
                if (!(settings["moduleBuildSeconds"] is JObject modules))
                    throw new ModJsonException(FileName + " moduleBuildSeconds must be an object.");
                foreach (var property in modules.Properties())
                {
                    if (Array.IndexOf(TimedModules, property.Name) < 0)
                        throw new ModJsonException("Unknown station module " + property.Name);
                    rules.moduleBuildSeconds[property.Name] = StructureInt(modules, property.Name, 0, 0, MaxBuildSeconds);
                }
            }
            if (settings["refineryRecipes"] != null)
            {
                if (!(settings["refineryRecipes"] is JArray definitions) || definitions.Count > 256)
                    throw new ModJsonException(FileName + " refineryRecipes must contain up to 256 recipes.");
                var recipes = new Dictionary<int, (int, int, int)>();
                foreach (var entry in definitions)
                {
                    if (!(entry is JObject recipe)) throw new ModJsonException("A refinery recipe must be an object.");
                    int input = StructureInt(recipe, "input", -1, 0, int.MaxValue);
                    int output = StructureInt(recipe, "output", -1, 0, int.MaxValue);
                    if (input < 0 || output < 0 || input == output || recipes.ContainsKey(input))
                        throw new ModJsonException("Refinery recipes need unique inputs and distinct output item IDs.");
                    recipes.Add(input, (output, StructureInt(recipe, "inputUnits", 10, 1, 500),
                        StructureInt(recipe, "outputUnits", 1, 1, 500)));
                }
                rules.RefineryRecipes = recipes;
            }
            if (settings["factoryRecipes"] != null)
            {
                if (!(settings["factoryRecipes"] is JArray factory) || factory.Count > 256)
                    throw new ModJsonException(FileName + " factoryRecipes must contain up to 256 recipes.");
                var recipes = new Dictionary<int, FactoryDefinition>();
                foreach (var entry in factory)
                {
                    if (!(entry is JObject recipe)) throw new ModJsonException("A factory recipe must be an object.");
                    int id = StructureInt(recipe, "id", 0, 1, int.MaxValue);
                    if (recipes.ContainsKey(id)) throw new ModJsonException("Duplicate factory recipe ID " + id);
                    recipes.Add(id, new FactoryDefinition
                    {
                        Output = StructureInt(recipe, "output", -1, 0, int.MaxValue),
                        OutputUnits = StructureInt(recipe, "outputUnits", 1, 1, 100000),
                        Inputs = ReadStructureMaterials(recipe, "Factory recipe " + id)
                    });
                }
                rules.FactoryRecipes = recipes;
            }
            return rules;
        }

        static float ReadMinimumScale(JObject settings, string key)
        {
            float scale = ModJson.Float(settings, key, 1f, FileName);
            if (float.IsNaN(scale) || scale < 1f || scale > MaxStructureScale)
                throw new ModJsonException($"{FileName} {key} must be between 1 and {MaxStructureScale}.");
            return scale;
        }

        static IReadOnlyList<OrbitalDefinition> ReadStructures(JObject settings)
        {
            var result = new List<OrbitalDefinition>();
            if (ReadToggle(settings, "includeStockOrbitalDefense")) result.AddRange(StockStructures);
            if (settings["orbitalStructures"] == null) return result;
            if (!(settings["orbitalStructures"] is JArray entries) || entries.Count > 64)
                throw new ModJsonException(FileName + " orbitalStructures must contain up to 64 definitions.");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                if (!(entry is JObject value)) throw new ModJsonException("An orbital structure definition must be an object.");
                string id = StructureText(value, "id", 48);
                foreach (char character in id)
                    if (!(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_'))
                        throw new ModJsonException("Orbital structure IDs use lowercase letters, digits, hyphens and underscores.");
                if (!ids.Add(id)) throw new ModJsonException("Duplicate orbital structure ID " + id);
                bool defense = value["defense"] != null && ReadToggle(value, "defense");
                string assembly = value["assembly"] == null ? null : StructureText(value, "assembly", 128);
                string station = value["stationModel"] == null ? null : StructureText(value, "stationModel", 128);
                if (defense)
                {
                    assembly = DefenseAssembly(assembly);
                    station = null;
                }
                else if ((assembly == null) == (station == null)) throw new ModJsonException(id + " needs one assembly or stationModel.");
                int maintenanceInterval = 0;
                float maintenanceDecayPercentPerDay = DefaultOrbitalDecayPercentPerDay;
                float minimumMaintenanceIntegrityPercent = MinimumOrbitalDecayIntegrityPercent;
                List<(int item, int amount)> maintenanceCosts = null;
                if (value["maintenance"] != null)
                {
                    if (!defense || !(value["maintenance"] is JObject maintenance))
                        throw new ModJsonException(id + " maintenance must be an object on a defense preset.");
                    maintenanceInterval = StructureInt(maintenance, "intervalSeconds", 0, 60, 31536000);
                    maintenanceDecayPercentPerDay = StructureNumber(maintenance, "decayPercentPerDay", DefaultOrbitalDecayPercentPerDay, 0f, 100f);
                    minimumMaintenanceIntegrityPercent = StructureNumber(maintenance, "minimumIntegrityPercent",
                        MinimumOrbitalDecayIntegrityPercent, MinimumOrbitalDecayIntegrityPercent, 100f);
                    maintenanceCosts = ReadStructureMaterials(maintenance, id + " maintenance");
                }
                result.RemoveAll(definition => string.Equals(definition.Id, id, StringComparison.OrdinalIgnoreCase));
                result.Add(new OrbitalDefinition
                {
                    Id = id, Name = StructureText(value, "name", 64), Assembly = assembly, StationModel = station, Defense = defense,
                    TargetPlayers = defense && ReadToggle(value, "targetPlayers"),
                    TargetNpcs = defense && ReadToggle(value, "targetNpcs"),
                    SponsorRaces = ReadReferences(value, "sponsorRaces"),
                    ModelScale = defense ? DefenseModelScale : StructureNumber(value, "modelScale", 1f, 0.01f, 10f),
                    HitRadius = StructureNumber(value, "hitRadius", 250f, 10f, 2000f),
                    Integrity = StructureNumber(value, "integrity", 12000f, 1, 10000000),
                    BuildSeconds = StructureInt(value, "buildSeconds", 0, 0, 2592000),
                    Range = defense ? StructureNumber(value, "range", 5000f, 50, 20000) : 0,
                    Damage = defense ? StructureNumber(value, "damage", 120f, 1, 10000) : 0,
                    FireIntervalSeconds = StructureInt(value, "fireIntervalSeconds", 3, 1, 120),
                    Materials = ReadStructureMaterials(value, id),
                    MaintenanceIntervalSeconds = maintenanceInterval,
                    MaintenanceDecayPercentPerDay = maintenanceDecayPercentPerDay,
                    MinimumMaintenanceIntegrityPercent = minimumMaintenanceIntegrityPercent,
                    MaintenanceMaterials = maintenanceCosts ?? new List<(int, int)>()
                });
            }
            return result;
        }

        static List<(int item, int amount)> ReadStructureMaterials(JObject value, string id)
        {
            if (!(value["materials"] is JArray materials) || materials.Count == 0 || materials.Count > 32)
                throw new ModJsonException(id + " needs 1–32 material entries.");
            var costs = new List<(int item, int amount)>();
            foreach (var material in materials)
            {
                if (!(material is JObject cost)) throw new ModJsonException(id + " materials must be objects.");
                int item = StructureInt(cost, "item", -1, 0, int.MaxValue);
                int amount = StructureInt(cost, "amount", 0, 1, 100000);
                if (costs.Exists(part => part.item == item)) throw new ModJsonException(id + " repeats a material item.");
                costs.Add((item, amount));
            }
            return costs;
        }

        static string StructureText(JObject value, string key, int limit)
        {
            string result = value[key]?.Type == JTokenType.String ? ((string)value[key]).Trim() : "";
            if (result.Length == 0 || result.Length > limit || result.IndexOfAny(new[] { '\r', '\n', '<', '>' }) >= 0)
                throw new ModJsonException("Invalid orbital structure " + key);
            return result;
        }

        static int StructureInt(JObject value, string key, int fallback, int min, int max)
        {
            long result = fallback;
            if (value[key] != null && (value[key].Type != JTokenType.Integer || !long.TryParse(value[key].ToString(), out result)))
                throw new ModJsonException("Orbital structure " + key + " must be an integer.");
            if (result < min || result > max) throw new ModJsonException("Orbital structure " + key + " is out of range.");
            return (int)result;
        }

        static float StructureNumber(JObject value, string key, float fallback, float min, float max)
        {
            float result = ModJson.Float(value, key, fallback, FileName);
            if (float.IsNaN(result) || result < min || result > max)
                throw new ModJsonException("Orbital structure " + key + " is out of range.");
            return result;
        }

        static bool ReadToggle(JObject settings, string field)
        {
            var value = settings[field];
            if (value == null) return true;
            if (value.Type != JTokenType.Boolean) throw new ModJsonException(FileName + " " + field + " must be true or false.");
            return (bool)value;
        }

        static IReadOnlyDictionary<string, string> ReadModelImages(JObject settings)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (settings["stationModelImages"] == null) return result;
            if (!(settings["stationModelImages"] is JObject images) || images.Count > 256)
                throw new ModJsonException(FileName + " stationModelImages must be an object with up to 256 entries.");
            foreach (var entry in images.Properties())
            {
                string key = entry.Name.Trim();
                string value = entry.Value.Type == JTokenType.String ? ((string)entry.Value).Trim() : "";
                if (key.Length == 0 || key.Length > 128 || value.Length == 0 || value.Length > 256 || result.ContainsKey(key))
                    throw new ModJsonException(FileName + " contains an invalid station model image reference.");
                result.Add(key, value);
            }
            return result;
        }

        static IReadOnlyList<string> ReadReferences(JObject settings, string field)
        {
            if (settings[field] == null) return Array.Empty<string>();
            if (!(settings[field] is JArray values) || values.Count > 256)
                throw new ModJsonException(FileName + " " + field + " must contain up to 256 references.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var value in values)
            {
                string reference = value.Type == JTokenType.String ? ((string)value).Trim() : "";
                if (string.IsNullOrEmpty(reference) || reference.Length > 128 || !seen.Add(reference))
                    throw new ModJsonException(FileName + " " + field + " requires distinct, non-empty references up to 128 characters.");
                result.Add(reference);
            }
            return result;
        }
    }
}
