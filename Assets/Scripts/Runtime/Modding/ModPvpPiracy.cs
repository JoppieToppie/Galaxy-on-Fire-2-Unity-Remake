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

        internal static void Apply()
        {
            Enabled = false;
            foreach (var mod in ModManager.Active)
            {
                try
                {
                    var config = ModJson.Read(mod.Source, File);
                    if (config == null) continue;
                    if (config is JObject options && ModJson.Get(options, "enabled") is JValue value &&
                        value.Type == JTokenType.Boolean)
                        Enabled |= (bool)value;
                    else Debug.LogWarning($"[{mod.Id}] {File} needs an explicit boolean enabled field.");
                }
                catch (ModJsonException e) { Debug.LogWarning($"[{mod.Id}] {e.Message}"); }
            }
        }
    }
}
