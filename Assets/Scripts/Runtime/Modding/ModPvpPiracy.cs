// Opt-in multiplayer piracy and faction bounties, from a mod's piracy.json.
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

        internal static void Apply()
        {
            Enabled = false;
            PlayerKillsForBounty = 5;
            NpcKillsForBounty = 100;
            ShipRecoverySeconds = 1800;
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
