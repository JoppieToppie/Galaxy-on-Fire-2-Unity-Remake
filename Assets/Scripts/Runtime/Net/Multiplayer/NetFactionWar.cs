// NetFactionWar.cs
using System.Collections.Generic;
using System.IO;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using Unity.Netcode;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    public partial class NetState
    {
        NpcFactionWar.Campaign factionWar;
        sealed class FleetRoster
        {
            public NpcFactionWar.Battle battle;
            public ulong creator = ulong.MaxValue;
            public readonly HashSet<int> casualties = new HashSet<int>();
            public long nextLossAt;
            public int lossInterval;
            public long pausedLossSeconds;
            public bool finished;
            public float startedAt;
            public readonly Dictionary<string, int> againstDefenders = new Dictionary<string, int>();
            public readonly Dictionary<string, int> againstAttackers = new Dictionary<string, int>();
            public readonly Dictionary<string, double> hullAgainstDefenders = new Dictionary<string, double>();
            public readonly Dictionary<string, double> hullAgainstAttackers = new Dictionary<string, double>();
            public readonly Dictionary<string, int> attackerReputation = new Dictionary<string, int>();
            public readonly Dictionary<string, int> defenderReputation = new Dictionary<string, int>();
            public readonly Dictionary<int, ulong> ships = new Dictionary<int, ulong>();
        }

        readonly Dictionary<int, FleetRoster> fleetByOrbit = new Dictionary<int, FleetRoster>();
        readonly Dictionary<NetPlayer, string> sessionRewardKeys = new Dictionary<NetPlayer, string>();
        readonly Dictionary<ulong, float> nextRewardUpload = new Dictionary<ulong, float>();
        bool factionWarDirty, factionWarViewDirty;
        float retryWarSaveAt;
        float nextWarScheduleAt;
        readonly List<int> retiredWarOrbits = new List<int>();

        string WarPath => NetProfiles.Enabled ? Path.Combine(NetProfiles.Folder, "npc-faction-war.json") : null;

        void EnsureFactionWar()
        {
            if (!IsServer || !ModNpcFactionWar.Enabled || factionWar != null) return;
            string snapshot = null;
            try
            {
                if (WarPath != null && File.Exists(WarPath)) snapshot = File.ReadAllText(WarPath);
            }
            catch (System.Exception e) when (e is IOException || e is System.UnauthorizedAccessException)
            {
                Debug.LogError($"NPC faction war could not be loaded: {e.Message}");
                return;
            }
            factionWar = NpcFactionWar.Campaign.Read(snapshot);
            NpcFactionWar.SetNetworkCampaign(factionWar);
        }

        void FlushFactionWar()
        {
            if (!factionWarDirty || Time.unscaledTime < retryWarSaveAt) return;
            if (WarPath == null || NetProfiles.Write(WarPath, factionWar.Snapshot())) factionWarDirty = false;
            else retryWarSaveAt = Time.unscaledTime + 10f;
        }

        [Rpc(SendTo.Server)]
        void RequestFactionWarSnapshotRpc(RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Request)) return;
            EnsureFactionWar();
            if (factionWar != null)
                FactionWarSnapshotRpc(factionWar.Snapshot(includeActive: true), RpcTarget.Single(client, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void BroadcastFactionWarRpc(string json)
        {
            if (!IsServer) NpcFactionWar.SetNetworkCampaign(NpcFactionWar.Campaign.Read(json));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void FactionWarSnapshotRpc(string json, RpcParams rpc = default)
        {
            if (!IsServer) NpcFactionWar.SetNetworkCampaign(NpcFactionWar.Campaign.Read(json));
        }

        bool WarOrbitOccupied(int orbit)
        {
            foreach (var pilot in NetPlayer.All)
                if (pilot != null && pilot.IsSpawned && pilot.InSpace && pilot.Station == orbit) return true;
            return false;
        }

        void SetWarPaused(FleetRoster roster, bool paused, long now)
        {
            if (!factionWar.SetPaused(roster.battle, paused, now, out var updated)) return;
            roster.battle = updated;
            if (paused) roster.pausedLossSeconds = System.Math.Max(0L, roster.nextLossAt - now);
            else roster.nextLossAt = now + roster.pausedLossSeconds;
            factionWarViewDirty = true;
        }

        // Called from the server sweep even on an empty dedicated server. The clock and losses are
        // server-owned; both stop running in the background whenever a player occupies the battle orbit.
        void TickFactionWar()
        {
            if (!IsServer || !ModNpcFactionWar.Enabled) return;
            long now = NpcFactionWar.Now;
            if (factionWar != null)
            {
                retiredWarOrbits.Clear();
                foreach (var pair in fleetByOrbit)
                {
                    var roster = pair.Value;
                    var battle = roster.battle;
                    if (roster.finished || battle.expires < now)
                    {
                        RetireWarFleet(roster, true);
                        factionWar.RemoveBattle(battle);
                        retiredWarOrbits.Add(pair.Key);
                        factionWarViewDirty = true;
                        continue;
                    }
                    bool occupied = WarOrbitOccupied(battle.orbit);
                    SetWarPaused(roster, occupied, now);
                    if (occupied) continue; // physical combat only, even if the orbit runner is still loading
                    if (roster.creator != ulong.MaxValue)
                    {
                        RetireWarFleet(roster, false);
                        roster.creator = ulong.MaxValue;
                        roster.ships.Clear();
                    }
                    if (now < roster.nextLossAt) continue;
                    roster.nextLossAt = now + roster.lossInterval;
                    // Symmetric abstract attrition. An unresolved engagement still times out.
                    if (UnityEngine.Random.value < 0.25f) continue;
                    int selected = -1, count = 0;
                    for (int slot = 0; slot < 2 * battle.shipsPerSide; slot++)
                        if (!roster.casualties.Contains(slot) && UnityEngine.Random.Range(0, ++count) == 0) selected = slot;
                    if (selected >= 0) RecordWarLoss(roster, selected, now);
                }
                foreach (int orbit in retiredWarOrbits) fleetByOrbit.Remove(orbit);
            }

            if (Time.unscaledTime >= nextWarScheduleAt)
            {
                nextWarScheduleAt = Time.unscaledTime + 20f;
                EnsureFactionWar();
                if (factionWar != null && factionWar.TryStartAny(NetGame.Db, now, orbit =>
                    !fleetByOrbit.ContainsKey(orbit) && !NetArena.IsArenaOrbit(orbit) &&
                    !NetPlayerStations.StationClosedByCombat(orbit), out var battle))
                {
                    int interval = Mathf.Max(1, ModNpcFactionWar.BattleSeconds / (2 * battle.shipsPerSide));
                    var roster = new FleetRoster { battle = battle, lossInterval = interval, nextLossAt = now + interval };
                    fleetByOrbit[battle.orbit] = roster;
                    SetWarPaused(roster, WarOrbitOccupied(battle.orbit), now);
                    factionWarDirty = factionWarViewDirty = true;
                    NetNews.Post(NetNews.Kind.War, $"{Standing.RaceName(battle.attacker)} and {Standing.RaceName(battle.defender)} fleets are fighting at {NetNews.Place(battle.orbit)}.", battle.orbit);
                    ModScripts.Raise("npcFactionBattleStarted", battle.orbit, battle.system, battle.defender, battle.attacker, battle.shipsPerSide);
                }
            }
            TickVictoryRewards();
            // Coalesce all casualties/clock transitions in this sweep into one small reliable update.
            if (factionWarViewDirty && factionWar != null)
            {
                factionWarViewDirty = false;
                BroadcastFactionWarRpc(factionWar.Snapshot(includeActive: true));
            }
        }

        void RetireWarFleet(FleetRoster roster, bool notify)
        {
            var battle = roster.battle;
            if (notify)
                foreach (var pilot in NetPlayer.All)
                    if (pilot != null && pilot.IsSpawned && pilot.InSpace && pilot.Station == battle.orbit)
                        EndFactionBattleRpc(battle.id, battle.orbit, battle.shipsPerSide,
                            RpcTarget.Single(pilot.OwnerClientId, RpcTargetUse.Temp));
            // Completed or expired engagements release their live fleet to the occupied orbit.
            // Only an empty orbit needs proxy cleanup for abstract simulation.
            if (notify && WarOrbitOccupied(battle.orbit)) return;
            // Drop the old proxies when the orbit empties. A returning runner must only spawn the surviving slots.
            var remove = new List<NetworkObject>();
            foreach (var obj in NetworkManager.SpawnManager.SpawnedObjectsList)
            {
                if (obj == null || !obj.IsSpawned) continue;
                var ship = obj.GetComponent<NetProxy>();
                if (ship == null || ship.Station != battle.orbit) continue;
                if (battle.OwnsTag(ship.EventTag)) remove.Add(obj);
            }
            foreach (var obj in remove)
                if (obj.IsSpawned) obj.Despawn();
        }

        [Rpc(SendTo.Server)]
        internal void RequestFactionBattleRpc(int orbit, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!ModNpcFactionWar.Enabled || !NetRateLimit.Allow(client, NetRateLimit.Kind.Request) ||
                !NetGuard.InOrbit(client, orbit) || RunnerOf(orbit)?.OwnerClientId != client ||
                NetArena.IsArenaOrbit(orbit) || NetPlayerStations.StationClosedByCombat(orbit)) return;
            if (!fleetByOrbit.TryGetValue(orbit, out var roster) || roster.finished || roster.battle.expires < NpcFactionWar.Now) return;
            // A request attaches a local simulation to a server-started battle; it never starts a campaign engagement.
            SetWarPaused(roster, true, NpcFactionWar.Now);
            if (roster.creator == ulong.MaxValue)
            {
                roster.creator = client;
                roster.startedAt = Time.unscaledTime;
            }
            else if (roster.creator != client) return;
            var battle = roster.battle;
            StartFactionBattleRpc(battle.id, battle.orbit, battle.system, battle.defender, battle.attacker, battle.shipsPerSide,
                battle.expires, battle.defenderHull, battle.attackerHull, new List<int>(roster.casualties).ToArray(), RpcTarget.Single(client, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void StartFactionBattleRpc(int id, int orbit, int system, int defender, int attacker, int ships, long expires, int defenderHull, int attackerHull,
            int[] casualties, RpcParams rpc = default) =>
            NetOrbit.Current?.BeginFactionBattle(new NpcFactionWar.Battle(id, orbit, system, defender, attacker, ships, expires, defenderHull, attackerHull), casualties);

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void EndFactionBattleRpc(int id, int orbit, int ships, RpcParams rpc = default) =>
            NetOrbit.Current?.EndFactionBattle(new NpcFactionWar.Battle(id, orbit, -1, -1, -1, ships, 0));

        internal bool IsFactionWarShip(NetProxy ship, int race)
        {
            if (!IsServer || !ModNpcFactionWar.Enabled || ship == null || ship.IsJunk || ship.IsWingman ||
                ship.Spec?.spec == null || ship.Spec.spec.capitalPart || ship.Spec.spec.group == NpcGroup.Turret ||
                !fleetByOrbit.TryGetValue(ship.Station, out var roster) ||
                roster.finished || roster.creator == ulong.MaxValue || roster.creator != ship.Creator || ship.SpawnedAt < roster.startedAt) return false;
            var battle = roster.battle;
            int slot = battle.Slot(ship.EventTag);
            if (!battle.IsRosterTag(ship.EventTag) || race != (slot < battle.shipsPerSide ? battle.defender : battle.attacker))
                return false;
            if (roster.ships.TryGetValue(slot, out ulong id)) return id == ship.NetworkObjectId;
            if (roster.casualties.Contains(slot)) return false;
            roster.ships.Add(slot, ship.NetworkObjectId);
            return true;
        }

        string RewardAccount(NetPlayer pilot)
        {
            if (pilot == null || pilot.Observer) return null;
            if (NetProfiles.Controls(pilot.OwnerClientId))
                return "profile:" + NetProfiles.AccountOf(pilot.OwnerClientId);
            if (NetProfiles.Enabled) return null; // guests cannot write a persistent profile
            if (!sessionRewardKeys.TryGetValue(pilot, out string key))
                sessionRewardKeys[pilot] = key = "session:" + System.Guid.NewGuid().ToString("N");
            return key;
        }

        // Only registered NPC fleet hulls can reach this ledger; NetPlayer PvP has no entry point.
        // Accumulate actual hull lost per victim, in units of that victim's maximum hull.
        internal void RecordFactionWarDamage(NetProxy ship, ulong shooter, double hullFraction)
        {
            if (factionWar == null || !(hullFraction > 0d && hullFraction <= 1d) ||
                ship == null || !IsFactionWarShip(ship, ship.Race)) return;
            var roster = fleetByOrbit[ship.Station];
            if (roster.finished || roster.battle.expires < NpcFactionWar.Now) return;
            int slot = roster.battle.Slot(ship.EventTag);
            if (roster.casualties.Contains(slot)) return;
            var pilot = NetSquad.Find(shooter);
            if (pilot == null || !pilot.IsSpawned || !pilot.InSpace || pilot.Station != ship.Station) return;
            string account = RewardAccount(pilot);
            if (account == null) return;
            var damage = slot < roster.battle.shipsPerSide ? roster.hullAgainstDefenders : roster.hullAgainstAttackers;
            damage.TryGetValue(account, out double total);
            damage[account] = total + hullFraction;
        }

        void RecordWarContribution(FleetRoster roster, NetProxy ship, ulong killer)
        {
            var scores = ship.Race == roster.battle.defender ? roster.againstDefenders : roster.againstAttackers;
            foreach (var pilot in NetPlayer.All)
            {
                if (pilot == null || !pilot.IsSpawned || !pilot.InSpace || pilot.Station != ship.Station) continue;
                ulong client = pilot.OwnerClientId;
                // The orbit owner's local kills use the game's existing owner-authoritative kill report.
                // Other players must have a server-checked hit on the destroyed NPC.
                if (!(client == killer && client == ship.OwnerClientId) &&
                    !HitRecently(ship.NetworkObjectId, client, 15f)) continue;
                string account = RewardAccount(pilot);
                if (account == null) continue;
                scores.TryGetValue(account, out int score);
                scores[account] = score + 1;
                roster.attackerReputation[account] = NetProfiles.ProfileStanding(pilot, roster.battle.attacker);
                roster.defenderReputation[account] = NetProfiles.ProfileStanding(pilot, roster.battle.defender);
            }
        }

        void CreateVictoryRewards(FleetRoster roster, int winner)
        {
            var settings = ModNpcFactionWar.RewardFor(winner);
            var eligible = new List<string>();
            var winningContributions = winner == roster.battle.attacker ? roster.againstDefenders : roster.againstAttackers;
            var losingContributions = winner == roster.battle.attacker ? roster.againstAttackers : roster.againstDefenders;
            var winningReputation = winner == roster.battle.attacker ? roster.attackerReputation : roster.defenderReputation;
            foreach (var entry in winningContributions)
            {
                losingContributions.TryGetValue(entry.Key, out int opposite);
                roster.hullAgainstDefenders.TryGetValue(entry.Key, out double defenderDamage);
                roster.hullAgainstAttackers.TryGetValue(entry.Key, out double attackerDamage);
                if (NpcFactionWar.QualifiesForCapture(entry.Value, opposite, defenderDamage, attackerDamage,
                    ModNpcFactionWar.DoubleSidedDamageThresholdPercent))
                    eligible.Add(entry.Key);
            }
            eligible.Sort(System.StringComparer.Ordinal);
            var pending = new List<NpcFactionWar.VictoryReward>();
            var priorWins = new int[eligible.Count];
            var reputations = new int[eligible.Count];
            var lootByPilot = new List<NpcFactionWar.RewardItem>[eligible.Count];
            for (int i = 0; i < eligible.Count; i++)
            {
                priorWins[i] = factionWar.RecordParticipation(eligible[i], winner);
                winningReputation.TryGetValue(eligible[i], out reputations[i]);
                lootByPilot[i] = new List<NpcFactionWar.RewardItem>();
            }
            foreach (var item in settings.loot)
            {
                var qualified = new List<int>();
                for (int i = 0; i < eligible.Count; i++)
                {
                    if (!item.Eligible(reputations[i], priorWins[i])) continue;
                    int chance = item.Chance(reputations[i], priorWins[i]);
                    if (chance == 100 || (chance > 0 && UnityEngine.Random.Range(0, 100) < chance)) qualified.Add(i);
                }
                // Randomize remainder recipients for scarce or gated drops, preserving legacy order for ordinary guaranteed loot.
                if (settings.distribution == "split" && (item.chancePercent != 100 || item.minReputation != -100 || item.minPriorVictories != 0))
                    for (int j = qualified.Count - 1; j > 0; j--)
                    {
                        int k = UnityEngine.Random.Range(0, j + 1);
                        int temp = qualified[j]; qualified[j] = qualified[k]; qualified[k] = temp;
                    }
                for (int j = 0; j < qualified.Count; j++)
                {
                    int i = qualified[j];
                    long amount = settings.distribution == "split" ? NpcFactionWar.Campaign.CreditShare(item.amount, qualified.Count, j) : item.amount;
                    if (item.scaleQuantity ?? settings.scaleLoot)
                        amount = settings.Scale(amount, settings.BonusPercent(reputations[i], priorWins[i]));
                    if (amount > 0) lootByPilot[i].Add(new NpcFactionWar.RewardItem { item = item.item, amount = (int)amount });
                }
            }
            for (int i = 0; i < eligible.Count; i++)
            {
                int previous = priorWins[i];
                int reputation = reputations[i];
                int bonus = settings.BonusPercent(reputation, previous);
                long credits = settings.Scale(settings.distribution == "flat" ? settings.credits :
                    NpcFactionWar.Campaign.CreditShare(settings.credits, eligible.Count, i), bonus);
                var loot = lootByPilot[i];
                if (credits == 0 && loot.Count == 0) continue;
                pending.Add(new NpcFactionWar.VictoryReward
                {
                    id = System.Guid.NewGuid().ToString("N"), account = eligible[i], race = winner,
                    credits = credits, loot = loot
                });
            }
            factionWar.AddOrbitRewards(roster.battle.orbit, pending);
        }

        void NotifyVictoryRewards(int orbit)
        {
            foreach (var pilot in NetPlayer.All)
            {
                if (pilot == null || !pilot.IsSpawned) continue;
                var reward = factionWar.RewardAt(RewardAccount(pilot), orbit);
                if (reward == null) continue;
                VictoryRewardAvailableRpc(orbit, reward.race, reward.credits,
                    RpcTarget.Single(pilot.OwnerClientId, RpcTargetUse.Temp));
            }
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void VictoryRewardAvailableRpc(int orbit, int race, long credits, RpcParams rpc = default)
        {
            NetChat.Notice($"Victory reward available at {NetNews.Place(orbit)}. Dock there to collect your share.");
            ModScripts.Raise("npcFactionWarRewardAvailable", orbit, race, credits);
        }

        void TickVictoryRewards()
        {
            if (factionWar == null || factionWarDirty) return;
            foreach (var pilot in NetPlayer.All)
            {
                if (pilot == null || !pilot.IsSpawned || pilot.Where != NetPlayer.Place.Hangar ||
                    !pilot.ArrivedFlying || pilot.Observer || pilot.HasRecordedDeath) continue;
                var reward = factionWar.RewardAt(RewardAccount(pilot), pilot.Station);
                if (reward == null) continue;
                ulong client = pilot.OwnerClientId;
                if (NetProfiles.Enabled)
                {
                    // Ask for a current docked snapshot, not the stale profile from before combat.
                    if (nextRewardUpload.TryGetValue(client, out float next) && Time.unscaledTime < next) continue;
                    nextRewardUpload[client] = Time.unscaledTime + 15f;
                    VictoryRewardUploadRpc(RpcTarget.Single(client, RpcTargetUse.Temp));
                }
                else
                {
                    // Nonpersistent sessions have no saved profile. Consume the entitlement in memory.
                    if (!TryResolveVictoryLoot(reward, out var loot)) continue;
                    factionWar.RemoveReward(reward);
                    SendVictoryReward(client, reward, loot);
                }
            }
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void VictoryRewardUploadRpc(RpcParams rpc = default)
        {
            if (NetProfiles.IsHostClient(Unity.Netcode.NetworkManager.ServerClientId) && IsServer)
                NetProfileClient.SaveHostNow();
            else NetProfileClient.Upload();
        }

        internal static bool TryResolveVictoryLoot(NpcFactionWar.VictoryReward reward, out List<ItemStack> loot)
        {
            loot = new List<ItemStack>();
            if (reward.loot == null) return true;
            foreach (var row in reward.loot)
            {
                if (row == null || row.amount <= 0 || !ModContent.TryResolveItem(row.item, out int item) ||
                    ModContent.IsMissingItem(item) || NetGame.Db.Item(item) == null) return false;
                var existing = loot.Find(s => s.item == item);
                if (existing == null) loot.Add(new ItemStack(item, row.amount));
                else
                {
                    if ((long)existing.amount + row.amount > int.MaxValue) return false;
                    existing.amount += row.amount;
                }
            }
            return true;
        }

        // Invoked only after the profile's docked upload was accepted and persisted.
        internal void ClaimVictoryReward(NetPlayer pilot, SaveData save)
        {
            if (!IsServer || factionWar == null || factionWarDirty || pilot == null || pilot.Observer ||
                pilot.Where != NetPlayer.Place.Hangar || !pilot.ArrivedFlying || pilot.HasRecordedDeath ||
                save == null || save.station != pilot.Station || save.lastDockedStation != pilot.Station ||
                !NetProfiles.Controls(pilot.OwnerClientId)) return;
            var reward = factionWar.RewardAt(RewardAccount(pilot), pilot.Station);
            if (reward == null) return;
            if (save.factionWarRewardReceipts?.Contains(reward.id) == true)
            {
                factionWar.RemoveReward(reward);
                factionWarDirty = true;
                return;
            }
            if (!TryResolveVictoryLoot(reward, out var loot)) return;
            if (!NetProfiles.PayVictoryReward(pilot, save, reward, loot)) return;
            factionWar.RemoveReward(reward);
            factionWarDirty = true;
            SendVictoryReward(pilot.OwnerClientId, reward, loot);
        }

        void SendVictoryReward(ulong client, NpcFactionWar.VictoryReward reward, List<ItemStack> loot)
        {
            var items = new int[loot.Count];
            var amounts = new int[loot.Count];
            for (int i = 0; i < loot.Count; i++) { items[i] = loot[i].item; amounts[i] = loot[i].amount; }
            VictoryRewardPaidRpc(reward.id, reward.race, reward.credits, items, amounts,
                RpcTarget.Single(client, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void VictoryRewardPaidRpc(string id, int race, long credits, int[] items, int[] amounts, RpcParams rpc = default)
        {
            if (Session.FactionWarRewardReceipts.Contains(id)) return;
            Session.FactionWarRewardReceipts.Add(id);
            Session.Credits = Session.SaturatingAdd(Session.Credits, credits);
            for (int i = 0; i < items.Length; i++) Shop.AddToCargo(items[i], amounts[i]);
            GoF2Remake.UI.UiHooks.RefreshStationCredits?.Invoke();
            GoF2Remake.UI.UiHooks.StationCargoChanged?.Invoke();
            NetChat.Notice($"Victory reward collected: {credits:N0} credits and {items.Length} equipment type(s).");
            ModScripts.Raise("npcFactionWarRewardClaimed", Session.StationIndex, race, credits);
        }

        internal void RecordFactionWarCasualty(NetProxy ship, ulong killer)
        {
            if (factionWar == null || !IsFactionWarShip(ship, ship.Race)) return;
            var roster = fleetByOrbit[ship.Station];
            if (roster.finished) return;
            int slot = roster.battle.Slot(ship.EventTag);
            if (roster.casualties.Contains(slot)) return;
            RecordWarContribution(roster, ship, killer);
            RecordWarLoss(roster, slot, NpcFactionWar.Now);
        }

        void RecordWarLoss(FleetRoster roster, int slot, long now)
        {
            var battle = roster.battle;
            if (roster.finished || battle.expires < now || slot < 0 || slot >= 2 * battle.shipsPerSide ||
                roster.casualties.Contains(slot)) return;
            roster.casualties.Add(slot);
            int race = slot < battle.shipsPerSide ? battle.defender : battle.attacker;
            factionWarViewDirty = true;
            if (!factionWar.RecordCasualty(battle, battle.Tag(slot), race, now, out int winner)) return;
            roster.finished = true;

            bool captured = winner == battle.attacker;
            CreateVictoryRewards(roster, winner);
            factionWarDirty = true;
            FlushFactionWar();
            NetNews.Post(NetNews.Kind.War, captured
                ? $"{Standing.RaceName(winner)} captured {NetNews.Place(battle.orbit)}."
                : $"{Standing.RaceName(winner)} repelled the attack at {NetNews.Place(battle.orbit)}.", battle.orbit);
            string notice = captured ? $"{Standing.RaceName(winner)} captured this orbit." :
                $"{Standing.RaceName(winner)} repelled the attack on this orbit.";
            foreach (var pilot in NetPlayer.All)
                if (pilot != null && pilot.IsSpawned && pilot.InSpace && pilot.Station == battle.orbit)
                    Notify(pilot.OwnerClientId, notice);
            NotifyVictoryRewards(battle.orbit);
            ModScripts.Raise("npcFactionBattleResolved", battle.orbit, battle.system, winner, captured);
        }
    }
}
