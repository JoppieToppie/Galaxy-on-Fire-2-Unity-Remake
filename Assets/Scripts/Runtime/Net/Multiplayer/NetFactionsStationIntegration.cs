using System;
using System.Collections.Generic;

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

        // All faction balances in a journal live in one file; commit them in one replacement.
        internal static bool SetBankAmounts(Dictionary<string, long> targets)
        {
            if (targets == null) return false;
            if (targets.Count == 0) return true;
            if (list?.factions == null) return false;
            var previous = new List<(Faction faction, long amount)>();
            foreach (var target in targets)
            {
                var faction = FindById(target.Key);
                if (faction == null || target.Value < 0) return false;
                previous.Add((faction, faction.bank));
            }
            foreach (var entry in previous) entry.faction.bank = targets[entry.faction.id];
            if (NetProfiles.Write(PathOf, UnityEngine.JsonUtility.ToJson(list, true))) return true;
            foreach (var entry in previous) entry.faction.bank = entry.amount;
            return false;
        }

        /// <summary>Set an absolute faction treasury balance so station transaction recovery is idempotent.</summary>
        internal static bool SetBankAmount(string factionId, long amount)
        {
            if (string.IsNullOrEmpty(factionId)) return false;
            return SetBankAmounts(new Dictionary<string, long>(StringComparer.Ordinal) { [factionId] = amount });
        }
    }
}