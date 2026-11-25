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

        public static int PlayerKillsForBounty { get; private set; }
        public static int NpcKillsForBounty { get; private set; }
        public static int ShipRecoverySeconds { get; private set; } = 1800;

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

        internal static void Apply()
        {
            Enabled = false;
            PlayerKillsForBounty = 5;
            NpcKillsForBounty = 100;
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
                    PlayerKillsForBounty = players;
                    NpcKillsForBounty = npcs;
                    ShipRecoverySeconds = recovery;
                }
                catch (ModJsonException e) { Debug.LogWarning($"[{mod.Id}] {e.Message}"); }
            }
        }
    }
}
