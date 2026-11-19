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
            public string account;
            public int item;
            public int amount;
        }

        [Serializable]
        sealed class StationCreditTarget
        {
            public string account;
            public long amount;
        }

        [Serializable]
        sealed class StationTransactionJournal
        {
            // Legacy single-profile field: retained so an interrupted journal from the first version can recover.
            public string account;
            public string registryJson;
            public List<StationCargoTarget> cargo = new List<StationCargoTarget>();
            public List<StationCreditTarget> credits = new List<StationCreditTarget>();
        }

        static string TransactionPath => path == null ? null : path + ".transaction";

        internal static bool CommitCargoTransaction(string account, params (int item, int delta)[] changes)
        {
            var cargo = new List<(string account, int item, int delta)>();
            if (changes != null)
                foreach (var change in changes) cargo.Add((account, change.item, change.delta));
            return CommitProfileTransaction(cargo, null);
        }

        /// <summary>One durable settlement for a marketplace buyer's cargo/credits and the seller's credits.</summary>
        internal static bool CommitMarketplaceTransaction(string buyer, string seller, int item, int amount, long total)
        {
            return CommitProfileTransaction(
                new List<(string account, int item, int delta)> { (buyer, item, amount) },
                new List<(string account, long delta)> { (buyer, -total), (seller, total) });
        }

        /// <summary>
        /// Commits station state and absolute profile targets through a write-ahead journal. Replaying targets is
        /// idempotent: a crash between any profile write and the registry write cannot apply a transfer twice.
        /// </summary>
        static bool CommitProfileTransaction(
            List<(string account, int item, int delta)> cargoChanges,
            List<(string account, long delta)> creditChanges)
        {
            if (!NetGame.IsServer || registry == null || string.IsNullOrEmpty(path)) return false;
            if (File.Exists(TransactionPath))
            {
                RecoverPendingTransaction();
                return false; // The caller's mutation predates recovery; retry against the recovered registry.
            }

            var cargoTargets = new Dictionary<(string account, int item), int>();
            foreach (var change in cargoChanges ?? new List<(string, int, int)>())
            {
                if (string.IsNullOrEmpty(change.account) || change.item < 0 || change.delta == 0) return false;
                var key = (change.account, change.item);
                int current = cargoTargets.TryGetValue(key, out int staged)
                    ? staged : NetProfiles.CargoAmount(change.account, change.item);
                if (current < 0) return false;
                long next = (long)current + change.delta;
                if (next < 0 || next > int.MaxValue) return false;
                cargoTargets[key] = (int)next;
            }

            var creditTargets = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var change in creditChanges ?? new List<(string, long)>())
            {
                if (string.IsNullOrEmpty(change.account) || change.delta == 0) return false;
                long current = creditTargets.TryGetValue(change.account, out long staged)
                    ? staged : NetProfiles.CreditAmount(change.account);
                if (current < 0) return false;
                if (change.delta > 0 && current > long.MaxValue - change.delta) return false;
                long next = current + change.delta;
                if (next < 0) return false;
                creditTargets[change.account] = next;
            }

            if (cargoTargets.Count == 0 && creditTargets.Count == 0) return false;
            var journal = new StationTransactionJournal { registryJson = JsonUtility.ToJson(registry, true) };
            foreach (var target in cargoTargets)
                journal.cargo.Add(new StationCargoTarget { account = target.Key.account, item = target.Key.item, amount = target.Value });
            foreach (var target in creditTargets)
                journal.credits.Add(new StationCreditTarget { account = target.Key, amount = target.Value });

            if (!NetProfiles.Write(TransactionPath, JsonUtility.ToJson(journal, true))) return false;
            if (!FinishTransaction(journal))
                Debug.LogWarning("NetPlayerStations: station transaction is journalled and will be retried during recovery.");
            return true; // A durable journal commits the intent; callers must not compensate it.
        }

        static bool RecoverPendingTransaction()
        {
            string journalPath = TransactionPath;
            if (string.IsNullOrEmpty(journalPath) || !File.Exists(journalPath)) return true;
            try
            {
                var journal = JsonUtility.FromJson<StationTransactionJournal>(File.ReadAllText(journalPath));
                if (journal == null || string.IsNullOrEmpty(journal.registryJson))
                    throw new InvalidDataException("journal is incomplete");
                journal.cargo ??= new List<StationCargoTarget>();
                journal.credits ??= new List<StationCreditTarget>();
                // Upgrade a pending journal from the original one-account cargo format in place.
                foreach (var target in journal.cargo)
                    if (target != null && string.IsNullOrEmpty(target.account)) target.account = journal.account;
                if (journal.cargo.Exists(x => x == null || string.IsNullOrEmpty(x.account)))
                    throw new InvalidDataException("journal contains a cargo target without an account");
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
                if (target == null || !NetProfiles.SetCargoAmount(target.account, target.item, target.amount))
                    return false;
            foreach (var target in journal.credits)
                if (target == null || !NetProfiles.SetCreditsAmount(target.account, target.amount))
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
