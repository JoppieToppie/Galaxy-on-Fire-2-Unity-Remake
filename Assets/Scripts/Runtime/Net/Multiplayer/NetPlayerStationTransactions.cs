using System;
using System.Collections.Generic;
using System.IO;
using GoF2Remake.Data;
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
        sealed class StationFactionBankTarget
        {
            public string factionId;
            public long amount;
        }

        [Serializable]
        sealed class StationShipTarget
        {
            public string account;
            public List<StoredShip> ships = new List<StoredShip>();
        }

        [Serializable]
        sealed class StationTransactionJournal
        {
            // Legacy single-profile field: retained so an interrupted journal from the first version can recover.
            public string account;
            public string registryJson;
            public List<StationCargoTarget> cargo = new List<StationCargoTarget>();
            public List<StationCreditTarget> credits = new List<StationCreditTarget>();
            public List<StationFactionBankTarget> factionBanks = new List<StationFactionBankTarget>();
            public List<StationShipTarget> storedShips = new List<StationShipTarget>();
        }

        static string TransactionPath => path == null ? null : path + ".transaction";

        internal static bool CommitCargoTransaction(string account, params (int item, int delta)[] changes)
        {
            var cargo = new List<(string account, int item, int delta)>();
            if (changes != null)
                foreach (var change in changes) cargo.Add((account, change.item, change.delta));
            return CommitProfileTransaction(cargo, null, null);
        }

        /// <summary>
        /// Atomically removes construction cargo, pays the contributor, debits the owning faction treasury,
        /// and commits the updated station ledger through the same write-ahead journal.
        /// </summary>
        internal static bool CommitConstructionContribution(string account, string factionId, int item, int amount, long payout)
        {
            if (string.IsNullOrEmpty(factionId) || payout <= 0) return false;
            return CommitProfileTransaction(
                new List<(string account, int item, int delta)> { (account, item, -amount) },
                new List<(string account, long delta)> { (account, payout) },
                new List<(string factionId, long delta)> { (factionId, -payout) });
        }

        /// <summary>One durable settlement for a marketplace buyer's cargo/credits and the seller's credits.</summary>
        internal static bool CommitMarketplaceTransaction(string buyer, string seller, int item, int amount, long total)
        {
            return CommitProfileTransaction(
                new List<(string account, int item, int delta)> { (buyer, item, amount) },
                new List<(string account, long delta)> { (buyer, -total), (seller, total) }, null);
        }

        internal static bool CommitMarketplaceFactionTransaction(string buyer, string factionId, int item, int amount, long total)
        {
            return CommitProfileTransaction(
                new List<(string account, int item, int delta)> { (buyer, item, amount) },
                new List<(string account, long delta)> { (buyer, -total) },
                new List<(string factionId, long delta)> { (factionId, total) });
        }

        /// <summary>
        /// Commits station state and absolute profile targets through a write-ahead journal. Replaying targets is
        /// idempotent: a crash between any profile write and the registry write cannot apply a transfer twice.
        /// </summary>
        internal static bool CommitStoredShipTransaction(string account, List<StoredShip> targetShips)
        {
            if (string.IsNullOrEmpty(account) || targetShips == null || targetShips.Count > 100 ||
                targetShips.Exists(x => x == null || x.ship < 0)) return false;
            return CommitProfileTransaction(null, null, null,
                new List<(string account, List<StoredShip> ships)> { (account, new List<StoredShip>(targetShips)) });
        }

        static bool CommitProfileTransaction(
            List<(string account, int item, int delta)> cargoChanges,
            List<(string account, long delta)> creditChanges,
            List<(string factionId, long delta)> factionBankChanges,
            List<(string account, List<StoredShip> ships)> shipChanges = null)
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


            var factionBankTargets = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var change in factionBankChanges ?? new List<(string, long)>())
            {
                if (string.IsNullOrEmpty(change.factionId) || change.delta == 0) return false;
                long current = factionBankTargets.TryGetValue(change.factionId, out long staged)
                    ? staged : NetFactions.BankAmount(change.factionId);
                if (current < 0) return false;
                if (change.delta > 0 && current > long.MaxValue - change.delta) return false;
                long next = current + change.delta;
                if (next < 0) return false;
                factionBankTargets[change.factionId] = next;
            }

            var shipTargets = new Dictionary<string, List<StoredShip>>(StringComparer.Ordinal);
            foreach (var change in shipChanges ?? new List<(string, List<StoredShip>)>())
            {
                if (string.IsNullOrEmpty(change.account) || change.ships == null || change.ships.Count > 100 ||
                    change.ships.Exists(x => x == null || x.ship < 0)) return false;
                shipTargets[change.account] = new List<StoredShip>(change.ships);
            }
            if (cargoTargets.Count == 0 && creditTargets.Count == 0 && factionBankTargets.Count == 0 && shipTargets.Count == 0) return false;
            var journal = new StationTransactionJournal { registryJson = JsonUtility.ToJson(registry, true) };
            foreach (var target in cargoTargets)
                journal.cargo.Add(new StationCargoTarget { account = target.Key.account, item = target.Key.item, amount = target.Value });
            foreach (var target in creditTargets)
                journal.credits.Add(new StationCreditTarget { account = target.Key, amount = target.Value });
            foreach (var target in factionBankTargets)
                journal.factionBanks.Add(new StationFactionBankTarget { factionId = target.Key, amount = target.Value });
            foreach (var target in shipTargets)
                journal.storedShips.Add(new StationShipTarget { account = target.Key, ships = target.Value });

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
                journal.factionBanks ??= new List<StationFactionBankTarget>();
                journal.storedShips ??= new List<StationShipTarget>();
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
            foreach (var target in journal.factionBanks)
                if (target == null || !NetFactions.SetBankAmount(target.factionId, target.amount))
                    return false;
            foreach (var target in journal.storedShips)
                if (target == null || string.IsNullOrEmpty(target.account) || target.ships == null ||
                    !NetProfiles.SetStoredShips(target.account, target.ships))
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
