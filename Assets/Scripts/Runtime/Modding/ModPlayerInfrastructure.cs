using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace GoF2Remake.Modding
{
    public sealed class ModPlayerInfrastructure
    {
        public const string FileName = "playerInfrastructure.json";
        public const float DefaultScrapIntegrity = 5000f;

        public bool Enabled { get; private set; } = true;
        public bool JumpgatesEnabled { get; private set; } = true;
        internal bool RestrictStationModels { get; private set; }
        public bool IncludeStockStations { get; private set; } = true;
        public bool IncludeStockJumpgates { get; private set; } = true;
        public float ScrapIntegrity { get; private set; } = DefaultScrapIntegrity;
        public IReadOnlyList<string> StationModels { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<string> JumpgateModels { get; private set; } = Array.Empty<string>();

        static readonly ModPlayerInfrastructure Defaults = new ModPlayerInfrastructure();
        static ModPlayerInfrastructure current = Defaults;
        static int currentRevision = -1;

        public static ModPlayerInfrastructure Current
        {
            get
            {
                var mods = ModManager.Active;
                if (currentRevision == ModManager.Revision) return current;
                current = Defaults;
                foreach (var mod in mods)
                    if (ModContent.Parse(mod).playerInfrastructure is ModPlayerInfrastructure rules)
                        current = rules;
                currentRevision = ModManager.Revision;
                return current;
            }
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
                StationModels = ReadReferences(settings, "stationModels"),
                JumpgateModels = ReadReferences(settings, "jumpgateModels")
            };
        }

        static bool ReadToggle(JObject settings, string field)
        {
            var value = settings[field];
            if (value == null) return true;
            if (value.Type != JTokenType.Boolean) throw new ModJsonException(FileName + " " + field + " must be true or false.");
            return (bool)value;
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
