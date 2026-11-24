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
        public const float MinimumOrbitalDecayIntegrityPercent = 15f;
        public const int MaxOtherOrbitalStructureLimit = 8;
        public const float MaxStructureScale = 6f;

        public bool Enabled { get; private set; } = true;
        public bool JumpgatesEnabled { get; private set; } = true;
        internal bool RestrictStationModels { get; private set; }
        public bool IncludeStockStations { get; private set; } = true;
        public bool IncludeStockJumpgates { get; private set; } = true;
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

        static readonly OrbitalDefinition[] StockStructures =
        {
            new OrbitalDefinition
            {
                Id = "orbital-defense", Name = "Orbital Defense", Defense = true, Assembly = "turret_002_static",
                ModelScale = 1f, HitRadius = 250f, Integrity = 12000f, Range = 5000f, Damage = 120f,
                FireIntervalSeconds = 3, Materials = new List<(int, int)> { (156, 200), (155, 100), (118, 80), (127, 50) }
            }
        };

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
            return new ModPlayerInfrastructure
            {
                RestrictStationModels = true,
                Enabled = ReadToggle(settings, "enabled"),
                JumpgatesEnabled = ReadToggle(settings, "jumpgatesEnabled"),
                IncludeStockStations = ReadToggle(settings, "includeStockStations"),
                IncludeStockJumpgates = ReadToggle(settings, "includeStockJumpgates"),
                ScrapIntegrity = integrity,
                MinimumStationScale = ReadMinimumScale(settings, "minimumStationScale"),
                MinimumJumpgateScale = ReadMinimumScale(settings, "minimumJumpgateScale"),
                OrbitalDefenseLimit = StructureInt(settings, "orbitalDefenseLimit", DefaultOrbitalDefenseLimit, 0, MaxOrbitalDefenseLimit),
                OtherOrbitalStructureLimit = StructureInt(settings, "otherOrbitalStructureLimit", MaxOtherOrbitalStructureLimit, 0, MaxOtherOrbitalStructureLimit),
                OrbitalStructures = ReadStructures(settings),
                StationModels = ReadReferences(settings, "stationModels"),
                StationModelImages = ReadModelImages(settings),
                JumpgateModels = ReadReferences(settings, "jumpgateModels")
            };
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
                if ((assembly == null) == (station == null)) throw new ModJsonException(id + " needs one assembly or stationModel.");
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
                    ModelScale = StructureNumber(value, "modelScale", 1f, 0.01f, 10f),
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
