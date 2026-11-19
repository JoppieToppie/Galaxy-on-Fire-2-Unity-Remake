using System;
using System.Linq;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetFactions
    {
        internal static long BankAmount(string factionId)
        {
            if (string.IsNullOrEmpty(factionId) || list?.factions == null) return -1;
            var faction = list.factions.Find(x => x != null && string.Equals(x.id, factionId, StringComparison.Ordinal));
            return faction?.bank ?? -1;
        }

        /// <summary>Set an absolute faction treasury balance so station transaction recovery is idempotent.</summary>
        internal static bool SetBankAmount(string factionId, long amount)
        {
            if (string.IsNullOrEmpty(factionId) || amount < 0 || list?.factions == null) return false;
            var faction = list.factions.Find(x => x != null && string.Equals(x.id, factionId, StringComparison.Ordinal));
            if (faction == null) return false;
            faction.bank = amount;
            Save();
            return BankAmount(factionId) == amount;
        }
    }
}