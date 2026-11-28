// Opt-in multiplayer piracy and faction bounties, from a mod's piracy.json.
using GoF2Remake.Data;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModPvpPiracy
    {
        public const string File = "piracy.json";
        public static bool Enabled { get; private set; }
        public static bool IndependentReputation { get; private set; }
        public static int SecureKillStandingPenalty { get; private set; } = 5;

        public static int PlayerKillsForBounty { get; private set; }
        public static int NpcKillsForBounty { get; private set; } = 5;
        public static int MaximumNpcBounty { get; private set; } = 300000;
        public static int NpcBountyIncrement { get; private set; } = 10000;
        public static int ShipRecoverySeconds { get; private set; } = 1800;

        // Pirate crews are quicker to retaliate than ordinary factions.
        public static float PirateHullTolerance(bool extreme) => extreme ? 0.05f : 0.10f;
        public static float PirateEmpTolerance(bool extreme) => extreme ? 0.10f : 0.15f;

        public const int DefaultEmergencyShip = 10;
        public static string EmergencyShipReference { get; private set; } = "10";
        static string warnedEmergencyShip;

        // Resolve only after the database and active mod ship numbering have been loaded.
        public static int EmergencyShip(Database db)
        {
            bool Valid(int index) => index >= 0 && index != 13 && index != 14 && index != 15 &&
                db?.Ship(index) != null && !ModContent.IsMissingShip(index);
            int ship;
            bool resolved = int.TryParse(EmergencyShipReference, out ship) ||
                (ship = ModContent.ShipIndexOf(EmergencyShipReference)) >= 0;
            if (resolved && Valid(ship)) return ship;
            if (warnedEmergencyShip != EmergencyShipReference)
            {
                warnedEmergencyShip = EmergencyShipReference;
                Debug.LogWarning($"{File}: emergencyShip '{EmergencyShipReference}' is unavailable or not player-ownable; using the default Phantom.");
            }
            return Valid(DefaultEmergencyShip) ? DefaultEmergencyShip : -1;
        }

        // Shared with the builder: reject malformed settings before replacing the file.
        public static void Validate(JObject options)
        {
            var flag = ModJson.Get(options, "enabled");
            if (flag != null && flag.Type != JTokenType.Boolean)
                throw new ModJsonException($"{File}: enabled must be true or false.");
            if (ModJson.Get(options, "independentReputation") is JToken reputation && reputation.Type != JTokenType.Boolean)
                throw new ModJsonException($"{File}: independentReputation must be true or false.");
            foreach (var key in new[] { "secureKillStandingPenalty", "playerKillsForBounty", "npcKillsForBounty", "maximumNpcBounty", "npcBountyIncrement", "shipRecoverySeconds" })
                if (ModJson.Get(options, key) is JToken value && value.Type != JTokenType.Integer)
                    throw new ModJsonException($"{File}: {key} must be an integer.");
            ModJson.Int(options, "playerKillsForBounty", 5, File);
            ModJson.Int(options, "npcKillsForBounty", 5, File);
            ModJson.Int(options, "shipRecoverySeconds", 1800, File);
            var emergency = ModJson.Get(options, "emergencyShip");
            if (emergency != null)
            {
                if (emergency.Type != JTokenType.Integer && emergency.Type != JTokenType.String)
                    throw new ModJsonException($"{File}: emergencyShip must be a ship index or mod:id reference.");
                string reference = emergency.ToString().Trim();
                if (string.IsNullOrEmpty(reference) || (int.TryParse(reference, out int index) ? index < 0 : !reference.Contains(":")))
                    throw new ModJsonException($"{File}: emergencyShip must be a non-negative ship index or mod:id reference.");
            }
        }

        internal static void Apply()
        {
            Enabled = false;
            IndependentReputation = false;
            SecureKillStandingPenalty = 5;
            PlayerKillsForBounty = 5;
            NpcKillsForBounty = 5;
            MaximumNpcBounty = 300000;
            NpcBountyIncrement = 10000;
            ShipRecoverySeconds = 1800;
            EmergencyShipReference = "10";
            warnedEmergencyShip = null;
            foreach (var mod in ModManager.Active)
            {
                try
                {
                    var config = ModJson.Read(mod.Source, File);
                    if (config == null) continue;
                    if (!(config is JObject options)) throw new ModJsonException($"{File} must contain an object.");
                    Validate(options);
                    var flag = ModJson.Get(options, "enabled");
                    if (flag != null && flag.Type != JTokenType.Boolean)
                        throw new ModJsonException($"{File}: enabled must be true or false.");
                    int players = Mathf.Clamp(ModJson.Int(options, "playerKillsForBounty", PlayerKillsForBounty, File), 1, 1000);
                    int npcs = Mathf.Clamp(ModJson.Int(options, "npcKillsForBounty", NpcKillsForBounty, File), 1, 10000);
                    int recovery = Mathf.Clamp(ModJson.Int(options, "shipRecoverySeconds", ShipRecoverySeconds, File), 1, 604800);
                    var emergency = ModJson.Get(options, "emergencyShip");
                    string reference = EmergencyShipReference;
                    if (emergency != null)
                    {
                        if (emergency.Type != JTokenType.Integer && emergency.Type != JTokenType.String)
                            throw new ModJsonException($"{File}: emergencyShip must be a ship index or mod:id reference.");
                        reference = emergency.ToString().Trim();
                        if (string.IsNullOrEmpty(reference) || (int.TryParse(reference, out int index) ? index < 0 : !reference.Contains(":")))
                            throw new ModJsonException($"{File}: emergencyShip must be a non-negative ship index or mod:id reference.");
                    }
                    EmergencyShipReference = reference;
                    Enabled |= flag != null && (bool)flag;
                    IndependentReputation |= ModJson.Bool(options, "independentReputation", false, File);
                    SecureKillStandingPenalty = Mathf.Clamp(ModJson.Int(options, "secureKillStandingPenalty", SecureKillStandingPenalty, File), 1, 100);
                    PlayerKillsForBounty = players;
                    NpcKillsForBounty = npcs;
                    MaximumNpcBounty = Mathf.Max(1, ModJson.Int(options, "maximumNpcBounty", MaximumNpcBounty, File));
                    NpcBountyIncrement = Mathf.Max(1, ModJson.Int(options, "npcBountyIncrement", NpcBountyIncrement, File));
                    ShipRecoverySeconds = recovery;
                }
                catch (ModJsonException e) { Debug.LogWarning($"[{mod.Id}] {e.Message}"); }
            }
        }
    }
}
