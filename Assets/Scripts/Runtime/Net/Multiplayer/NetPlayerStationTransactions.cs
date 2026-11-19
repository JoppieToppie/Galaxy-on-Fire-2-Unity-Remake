using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetPlayerStations
    {
        [Serializable]
        sealed class StationCargoTarget
        {
            public int item;
            public int amount;
        }

        [Serializable]
        sealed class StationTransactionJournal
        {
            public string account;
            public string registryJson;
            public List<StationCargoTarget> cargo = new List<StationCargoTarget>();
        }

        static string TransactionPath => path == null ? null : path + ".transaction";

        /// <summary>
        /// Commits a station-registry snapshot and absolute profile cargo targets through a write-ahead journal.
        /// The journal is durable before either store changes; replay sets absolute values, so a restart between
        /// profile and registry writes cannot duplicate or lose a transfer.
        /// </summary>
        internal static bool CommitCargoTransaction(string account, params (int item, int delta)[] changes)
        {
            if (!NetGame.IsServer || registry == null || string.IsNullOrEmpty(path) ||
                string.IsNullOrEmpty(account) || changes == null || changes.Length == 0)
                return false;

            if (File.Exists(TransactionPath))
            {
                RecoverPendingTransaction();
                return false; // The caller's in-memory mutation predates recovery and must be retried against the recovered registry.
            }

            var targets = new Dictionary<int, int>();
            foreach (var change in changes)
            {
                if (change.delta == 0) continue;
                int current = targets.TryGetValue(change.item, out int staged)
                    ? staged : NetProfiles.CargoAmount(account, change.item);
                if (current < 0) return false;
                long next = (long)current + change.delta;
                if (next < 0 || next > int.MaxValue) return false;
                targets[change.item] = (int)next;
            }
            if (targets.Count == 0) return false;

            var journal = new StationTransactionJournal
            {
                account = account,
                registryJson = JsonUtility.ToJson(registry, true),
            };
            foreach (var target in targets)
                journal.cargo.Add(new StationCargoTarget { item = target.Key, amount = target.Value });

            string json = JsonUtility.ToJson(journal, true);
            if (!NetProfiles.Write(TransactionPath, json)) return false;

            if (!FinishTransaction(journal))
                Debug.LogWarning("NetPlayerStations: station transaction is journalled and will be retried during recovery.");
            return true; // Once the journal is durable, this operation is committed and must not be compensated.
        }

        static bool RecoverPendingTransaction()
        {
            string journalPath = TransactionPath;
            if (string.IsNullOrEmpty(journalPath) || !File.Exists(journalPath)) return true;
            try
            {
                var journal = JsonUtility.FromJson<StationTransactionJournal>(File.ReadAllText(journalPath));
                if (journal == null || string.IsNullOrEmpty(journal.account) ||
                    string.IsNullOrEmpty(journal.registryJson) || journal.cargo == null)
                    throw new InvalidDataException("journal is incomplete");
                if (!FinishTransaction(journal)) return false;
                Debug.Log("NetPlayerStations: recovered an interrupted station/profile transaction.");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"NetPlayerStations: transaction journal could not be recovered ({e.Message}); it has been retained for manual recovery.");
                return false;
            }
        }

        static bool FinishTransaction(StationTransactionJournal journal)
        {
            foreach (var target in journal.cargo)
                if (target == null || !NetProfiles.SetCargoAmount(journal.account, target.item, target.amount))
                    return false;

            if (!NetProfiles.Write(path, journal.registryJson)) return false;
            registry = JsonUtility.FromJson<Registry>(journal.registryJson);
            if (registry == null) return false;
            registry.stations ??= new List<PlayerStationRecord>();
            try
            {
                if (File.Exists(TransactionPath)) File.Delete(TransactionPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"NetPlayerStations: committed transaction journal cleanup deferred ({e.Message}).");
            }
            NetState.Instance?.PublishPlayerStations(Snapshot);
            return true;
        }
    }
}
