// NpcFactionWar.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using GoF2Remake.Modding;
using UnityEngine;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NpcFactionWar
    {
        public const int FirstTag = 0x20000000;
        public const int MaxShipsPerSide = 100;
        // Keep the original reserved event-tag namespace while allowing 200 distinct fleet slots.
        const int SlotsPerBattle = 256;
        const int LastBattleId = 0x1ffffe;
        public static Campaign NetworkCampaign { get; private set; }
        static string localJson;
        static Campaign local;
        public static Campaign LocalCampaign
        {
            get
            {
                if (local == null || localJson != Session.NpcFactionWarJson)
                {
                    localJson = Session.NpcFactionWarJson;
                    local = Campaign.Read(localJson);
                }
                return local;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            local = NetworkCampaign = null;
            localJson = null;
        }

        public static void ResetLocal() { local = null; localJson = null; }

        public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        public static void SetNetworkCampaign(Campaign campaign) => NetworkCampaign = campaign;
        public static void SaveLocal() => Session.NpcFactionWarJson = localJson = LocalCampaign.Snapshot();

        static Campaign Visible => !ModNpcFactionWar.Enabled ? null : NetSession.Active ? NetworkCampaign : LocalCampaign;
        public static int OrbitOwner(SystemData system, int station) => Visible?.OrbitOwner(system, station) ?? system?.raceId ?? -1;
        public static int SystemOwner(SystemData system) => Visible?.SystemOwner(system) ?? system?.raceId ?? -1;
        public static bool HasCapturedOrbit(SystemData system, int orbit) =>
            ModNpcFactionWar.Enabled && system != null && OrbitOwner(system, orbit) != system.raceId;
        public static bool IsFrontline(Database db, SystemData system) => Visible?.IsFrontline(db, system) == true;
        public static bool TryGetBattleStatus(int system, int orbit, out BattleStatus status)
        {
            status = default;
            return Visible?.TryGetStatus(system, orbit, Now, out status) == true;
        }

        // A compact server snapshot for the map. A negative pausedSeconds means the deadline is running.
        [Serializable]
        public struct BattleStatus
        {
            public int id, orbit, system, defender, attacker, shipsPerSide, defendersLost, attackersLost, defenderHull, attackerHull;
            public long expires, pausedSeconds;
            public bool Paused => pausedSeconds >= 0;
            public int DefendersAlive => shipsPerSide - defendersLost;
            public int AttackersAlive => shipsPerSide - attackersLost;
            public long Remaining(long now) => Paused ? pausedSeconds : Math.Max(0L, expires - now);
        }

        public static bool IsWarTag(int tag) => tag >= FirstTag && tag < FirstTag + SlotsPerBattle * LastBattleId;
        // Every attachment shares the reserved final slot of its engagement, never a casualty slot.
        public static bool IsAuxiliaryTag(int tag) => IsWarTag(tag) && (tag - FirstTag) % SlotsPerBattle == SlotsPerBattle - 1;
        public static int AuxiliaryTag(int tag) => IsWarTag(tag) ? tag - (tag - FirstTag) % SlotsPerBattle + SlotsPerBattle - 1 : tag;

        // Preserve the winning-side contribution requirement, but never reward a pilot who substantially
        // damaged both fleets. Zero disables only this exclusion; the normal participation rule remains.
        public static bool QualifiesForCapture(int winningContributions, int losingContributions,
            double damageAgainstDefenders, double damageAgainstAttackers,
            int thresholdPercent = ModNpcFactionWar.DefaultDoubleSidedDamageThresholdPercent)
        {
            double threshold = thresholdPercent / 100d;
            // A tiny tolerance corrects summation at the exact configured threshold.
            return winningContributions > losingContributions &&
                (thresholdPercent <= 0 || !(damageAgainstDefenders >= threshold - 1e-9d &&
                                            damageAgainstAttackers >= threshold - 1e-9d));
        }

        public readonly struct Battle
        {
            public readonly int id, orbit, system, defender, attacker, shipsPerSide, defenderHull, attackerHull;
            public readonly long expires;

            public Battle(int id, int orbit, int system, int defender, int attacker, int shipsPerSide, long expires, int defenderHull = 0, int attackerHull = 0)
            {
                this.id = id;
                this.orbit = orbit;
                this.system = system;
                this.defender = defender;
                this.attacker = attacker;
                this.shipsPerSide = shipsPerSide;
                this.expires = expires;
                this.defenderHull = defenderHull; this.attackerHull = attackerHull;
            }

            public int Tag(int slot) => FirstTag + (id - 1) * SlotsPerBattle + slot;
            public int Slot(int tag) => tag - Tag(0);
            public bool OwnsTag(int tag) => Slot(tag) >= 0 && Slot(tag) < SlotsPerBattle;
            public bool IsRosterTag(int tag) => Slot(tag) >= 0 && Slot(tag) < 2 * shipsPerSide;
        }

        [Serializable]
        sealed class HeldOrbit
        {
            public string orbit, raceKey;
            public int race;
        }

        [Serializable]
        public sealed class RewardItem
        {
            public string item;
            public int amount;
        }

        [Serializable]
        public sealed class VictoryReward
        {
            public string id, account, orbitKey, raceKey;
            public int race;
            public long credits;
            public List<RewardItem> loot = new List<RewardItem>();
        }

        [Serializable]
        sealed class Participation
        {
            public string account, raceKey;
            public int race, count;
        }

        [Serializable]
        sealed class WarSave
        {
            public List<HeldOrbit> held = new List<HeldOrbit>();
            public long nextBattle;
            public List<BattleStatus> active;
            public List<VictoryReward> rewards;
            public List<Participation> participation;
        }

        sealed class Engagement
        {
            public Battle battle;
            public readonly HashSet<int> casualties = new HashSet<int>();
            public int defendersLost, attackersLost;
            public long pausedSeconds = -1;

            public BattleStatus Status => new BattleStatus
            {
                id = battle.id, orbit = battle.orbit, system = battle.system, defender = battle.defender,
                attacker = battle.attacker, shipsPerSide = battle.shipsPerSide, expires = battle.expires,
                defendersLost = defendersLost, attackersLost = attackersLost, pausedSeconds = pausedSeconds,
                defenderHull = battle.defenderHull, attackerHull = battle.attackerHull
            };
        }

        public sealed class Campaign
        {
            readonly Dictionary<string, int> held = new Dictionary<string, int>(StringComparer.Ordinal);
            // Keep unresolved stable keys on disk when a content mod is temporarily absent.
            readonly List<HeldOrbit> dormantHeld = new List<HeldOrbit>();
            readonly List<VictoryReward> dormantRewards = new List<VictoryReward>();
            readonly List<Participation> dormantParticipation = new List<Participation>();
            readonly Dictionary<int, Engagement> battles = new Dictionary<int, Engagement>();
            readonly List<VictoryReward> rewards = new List<VictoryReward>();
            readonly Dictionary<(string account, int race), int> participation = new Dictionary<(string account, int race), int>();
            int nextBattleId;

            public int RecordParticipation(string account, int race)
            {
                var key = (account, race);
                participation.TryGetValue(key, out int previous);
                participation[key] = previous == int.MaxValue ? previous : previous + 1;
                return previous;
            }

            public void ReplaceOrbitRewards(int orbit, IEnumerable<VictoryReward> pending)
            {
                string key = Key(orbit);
                rewards.RemoveAll(r => r.orbitKey == key);
                dormantRewards.RemoveAll(r => r.orbitKey == key);
                AddOrbitRewards(orbit, pending);
            }

            // Defensive wins must not erase previously earned, unclaimed rewards.
            public void AddOrbitRewards(int orbit, IEnumerable<VictoryReward> pending)
            {
                string key = Key(orbit);
                foreach (var reward in pending)
                {
                    reward.orbitKey = key;
                    rewards.Add(reward);
                }
            }

            public VictoryReward RewardAt(string account, int orbit) =>
                string.IsNullOrEmpty(account) ? null :
                rewards.Find(r => r.account == account && r.orbitKey == Key(orbit));

            public void RemoveReward(VictoryReward reward) => rewards.Remove(reward);

            public static long CreditShare(long pool, int count, int position) =>
                count <= 0 || position < 0 || position >= count ? 0 :
                Math.Max(0L, pool) / count + (position < Math.Max(0L, pool) % count ? 1L : 0L);
            long nextBattle;

            static string Key(int orbit) => ModWorld.StationKey(orbit) ?? orbit.ToString(CultureInfo.InvariantCulture);
            static bool Major(int race) => ModNpcFactionWar.ValidRace(race);
            static string RaceKey(int race) => ModRaces.Get(race)?.key;
            static int SavedRace(string key, int race) => string.IsNullOrEmpty(key) ? race : ModRaces.TryResolve(key, null, out int resolved) ? resolved : -1;

            public static Campaign Read(string json)
            {
                var campaign = new Campaign();
                if (string.IsNullOrEmpty(json)) return campaign;
                try
                {
                    var saved = JsonUtility.FromJson<WarSave>(json);
                    if (saved == null) return campaign;
                    campaign.nextBattle = saved.nextBattle;
                    if (saved.held != null)
                        foreach (var entry in saved.held)
                            if (entry != null && !string.IsNullOrEmpty(entry.orbit))
                            {
                                int race = SavedRace(entry.raceKey, entry.race);
                                if (Major(race)) campaign.held[entry.orbit] = race;
                                else if (!string.IsNullOrEmpty(entry.raceKey)) campaign.dormantHeld.Add(entry);
                            }
                    if (saved.rewards != null)
                        foreach (var reward in saved.rewards)
                            if (reward != null && !string.IsNullOrEmpty(reward.id) &&
                                !string.IsNullOrEmpty(reward.account) && reward.account.StartsWith("profile:", StringComparison.Ordinal) &&
                                !string.IsNullOrEmpty(reward.orbitKey) && reward.credits >= 0)
                            {
                                int race = SavedRace(reward.raceKey, reward.race);
                                if (Major(race)) { reward.race = race; campaign.rewards.Add(reward); }
                                else if (!string.IsNullOrEmpty(reward.raceKey)) campaign.dormantRewards.Add(reward);
                            }
                    if (saved.participation != null)
                        foreach (var entry in saved.participation)
                            if (entry != null && entry.account != null && entry.account.StartsWith("profile:", StringComparison.Ordinal) && entry.count > 0)
                            {
                                int race = SavedRace(entry.raceKey, entry.race);
                                if (Major(race)) campaign.participation[(entry.account, race)] = entry.count;
                                else if (!string.IsNullOrEmpty(entry.raceKey)) campaign.dormantParticipation.Add(entry);
                            }
                    // Active entries are sent to clients only, not written to the profile save.
                    if (saved.active != null)
                        foreach (var entry in saved.active)
                        {
                            if (entry.id <= 0 || entry.id > LastBattleId || entry.system < 0 || !ModNpcFactionWar.IsWarOrbit(entry.orbit) ||
                                !Major(entry.defender) || !Major(entry.attacker) || entry.shipsPerSide < 4 || entry.shipsPerSide > MaxShipsPerSide ||
                                entry.defendersLost < 0 || entry.defendersLost >= entry.shipsPerSide ||
                                entry.attackersLost < 0 || entry.attackersLost >= entry.shipsPerSide) continue;
                            var battle = new Battle(entry.id, entry.orbit, entry.system, entry.defender, entry.attacker, entry.shipsPerSide, entry.expires, entry.defenderHull, entry.attackerHull);
                            campaign.battles[entry.system] = new Engagement { battle = battle, pausedSeconds = entry.pausedSeconds,
                                defendersLost = entry.defendersLost, attackersLost = entry.attackersLost };
                            campaign.nextBattleId = Math.Max(campaign.nextBattleId, entry.id);
                        }
                }
                catch (ArgumentException e) { Debug.LogWarning($"NPC faction war save could not be read: {e.Message}"); }
                return campaign;
            }

            public string Snapshot(bool includeActive = false)
            {
                var saved = new WarSave { nextBattle = nextBattle };
                foreach (var entry in held) saved.held.Add(new HeldOrbit { orbit = entry.Key, race = entry.Value, raceKey = RaceKey(entry.Value) });
                if (!includeActive) saved.held.AddRange(dormantHeld);
                // Never broadcast private reward entitlements in the public map snapshot.
                if (!includeActive)
                {
                    saved.rewards = rewards.FindAll(r => r.account.StartsWith("profile:", StringComparison.Ordinal));
                    foreach (var reward in saved.rewards) reward.raceKey = RaceKey(reward.race);
                    saved.rewards.AddRange(dormantRewards);
                }
                if (!includeActive)
                {
                    saved.participation = new List<Participation>();
                    foreach (var entry in participation)
                        if (entry.Key.account.StartsWith("profile:", StringComparison.Ordinal))
                            saved.participation.Add(new Participation { account = entry.Key.account, race = entry.Key.race, raceKey = RaceKey(entry.Key.race), count = entry.Value });
                    saved.participation.AddRange(dormantParticipation);
                }
                if (includeActive)
                {
                    saved.active = new List<BattleStatus>(battles.Count);
                    foreach (var entry in battles.Values) saved.active.Add(entry.Status);
                }
                return JsonUtility.ToJson(saved);
            }

            public bool TryGetStatus(int system, int orbit, long now, out BattleStatus status)
            {
                status = default;
                if (!battles.TryGetValue(system, out var active) || (orbit >= 0 && active.battle.orbit != orbit) ||
                    active.battle.expires < now) return false;
                status = active.Status;
                return true;
            }

            // Preserve the unoccupied time budget while a player is in the orbit. MaxValue keeps existing
            // campaign expiry/one-engagement checks valid while physical combat is in progress.
            public bool SetPaused(Battle battle, bool paused, long now, out Battle updated)
            {
                updated = battle;
                if (!battles.TryGetValue(battle.system, out var active) || active.battle.id != battle.id ||
                    active.battle.expires < now || (active.pausedSeconds >= 0) == paused) return false;
                long expires;
                if (paused)
                {
                    active.pausedSeconds = Math.Max(0L, active.battle.expires - now);
                    expires = long.MaxValue;
                }
                else
                {
                    expires = now + active.pausedSeconds;
                    active.pausedSeconds = -1;
                }
                active.battle = updated = new Battle(battle.id, battle.orbit, battle.system, battle.defender,
                    battle.attacker, battle.shipsPerSide, expires, battle.defenderHull, battle.attackerHull);
                return true;
            }

            public bool RemoveBattle(Battle battle)
            {
                return battles.TryGetValue(battle.system, out var active) && active.battle.id == battle.id && battles.Remove(battle.system);
            }

            public int OrbitOwner(SystemData system, int orbit)
            {
                if (system == null) return -1;
                return held.Count > 0 && held.TryGetValue(Key(orbit), out int race)
                    ? race : system.raceId;
            }

            public int SystemOwner(SystemData system)
            {
                if (system == null) return -1;
                if (held.Count == 0 || system.stations == null || system.stations.Count == 0)
                    return system.raceId;
                var totals = new Dictionary<int, int>();
                int orbits = 0;
                foreach (int station in system.stations)
                {
                    if (!ModNpcFactionWar.IsWarOrbit(station)) continue;
                    orbits++;
                    int race = OrbitOwner(system, station);
                    if (Major(race)) { totals.TryGetValue(race, out int count); totals[race] = count + 1; }
                }
                foreach (var pair in totals)
                    if (pair.Value > orbits / 2) return pair.Key;
                // No strict majority means the system is contested.
                return orbits == 0 ? system.raceId : -1;
            }

            public bool TryStart(Database db, int orbit, long now, out Battle battle)
            {
                battle = default;
                if (!ModNpcFactionWar.Enabled || db?.Systems == null || !ModNpcFactionWar.IsWarOrbit(orbit) ||
                    now < nextBattle || nextBattleId == LastBattleId) return false;
                var system = db.Systems.Find(s => s != null && s.stations != null && s.stations.Contains(orbit));
                if (!ModNpcFactionWar.CanContest(system)) return false;
                if (battles.TryGetValue(system.index, out var existing) && existing.battle.expires >= now) return false;
                int defender = OrbitOwner(system, orbit);
                var attackers = new List<int>();
                foreach (int enemy in ModNpcFactionWar.Opponents(defender))
                    if (Frontline(db, system, enemy)) attackers.Add(enemy);
                if (attackers.Count == 0) return false;
                int attacker = attackers[UnityEngine.Random.Range(0, attackers.Count)];
                battle = new Battle(++nextBattleId, orbit, system.index, defender, attacker,
                    ModNpcFactionWar.ShipsPerSide, now + ModNpcFactionWar.BattleSeconds,
                    ModNpcFactionWar.RollBattleship(defender), ModNpcFactionWar.RollBattleship(attacker));
                battles[system.index] = new Engagement { battle = battle };
                nextBattle = now + ModNpcFactionWar.IntervalSeconds;
                return true;
            }

            // The server schedules the campaign independently of which orbits have players.
            public bool TryStartAny(Database db, long now, Func<int, bool> allowed, out Battle battle)
            {
                battle = default;
                if (!ModNpcFactionWar.Enabled || db?.Systems == null || now < nextBattle || nextBattleId == LastBattleId) return false;
                var candidates = new List<int>();
                foreach (var system in db.Systems)
                    if (ModNpcFactionWar.CanContest(system) && system.stations != null)
                        foreach (int orbit in system.stations)
                            if (ModNpcFactionWar.IsWarOrbit(orbit) && (allowed == null || allowed(orbit))) candidates.Add(orbit);
                // Randomise without replacement; TryStart retains the frontier and one-battle-per-system checks.
                while (candidates.Count > 0)
                {
                    int index = UnityEngine.Random.Range(0, candidates.Count);
                    int orbit = candidates[index];
                    int last = candidates.Count - 1;
                    candidates[index] = candidates[last];
                    candidates.RemoveAt(last);
                    if (TryStart(db, orbit, now, out battle)) return true;
                }
                return false;
            }

            public bool IsFrontline(Database db, SystemData system)
            {
                if (db?.Systems == null || !ModNpcFactionWar.CanContest(system) || system.stations == null) return false;
                var heldFactions = new HashSet<int>();
                foreach (int station in system.stations)
                {
                    if (!ModNpcFactionWar.IsWarOrbit(station)) continue;
                    int owner = OrbitOwner(system, station);
                    if (Major(owner)) heldFactions.Add(owner);
                }
                foreach (int race in heldFactions)
                    foreach (int enemy in ModNpcFactionWar.Opponents(race))
                        if (Frontline(db, system, enemy)) return true;
                return false;
            }

            bool Frontline(Database db, SystemData system, int attacker)
            {
                if (!ModNpcFactionWar.CanContest(system)) return false;
                if (ModNpcFactionWar.IsExplicitFrontline(system)) return true;
                if (!ModNpcFactionWar.DynamicFrontlines) return false;
                if (system.stations.Exists(station => ModNpcFactionWar.IsWarOrbit(station) && OrbitOwner(system, station) == attacker))
                    return true;
                bool gateOnly = ModNpcFactionWar.GateOnlyFrontlines;
                int nearest = !gateOnly && (system.jumpRoutesTo == null || system.jumpRoutesTo.Count == 0) ? Nearest(db, system) : -1;
                foreach (var other in db.Systems)
                {
                    if (other == null || other.index == system.index || !ModNpcFactionWar.Participates(other.raceId)) continue;
                    bool adjacent = system.jumpRoutesTo?.Contains(other.index) == true || other.jumpRoutesTo?.Contains(system.index) == true;
                    if (!adjacent && (gateOnly || (other.index != nearest && other.jumpRoutesTo?.Count > 0))) continue;
                    bool foothold = other.stations?.Exists(station => ModNpcFactionWar.IsWarOrbit(station) && OrbitOwner(other, station) == attacker) == true;
                    if (!foothold && SystemOwner(other) != attacker) continue;
                    if (adjacent || (!gateOnly && (other.index == nearest || Nearest(db, other) == system.index))) return true;
                }
                return false;
            }

            static int Nearest(Database db, SystemData from)
            {
                int nearest = -1;
                double best = double.MaxValue;
                var pos = from.mapPosition;
                if (pos == null) return nearest;
                foreach (var other in db.Systems)
                {
                    if (other == null || other.index == from.index || !ModNpcFactionWar.Participates(other.raceId) || other.mapPosition == null) continue;
                    var p = other.mapPosition;
                    double x = (double)pos.x - p.x, y = (double)pos.y - p.y, z = (double)pos.z - p.z;
                    double distance = x * x + y * y + z * z;
                    if (distance > best || (distance == best && other.index >= nearest)) continue;
                    best = distance;
                    nearest = other.index;
                }
                return nearest;
            }

            public bool RecordCasualty(Battle battle, int tag, int race, long now, out int winner)
            {
                winner = -1;
                if (!battle.IsRosterTag(tag)) return false;
                if (!battles.TryGetValue(battle.system, out var active) || active.battle.id != battle.id ||
                    active.battle.expires < now) return false;
                battle = active.battle;
                int slot = battle.Slot(tag);
                if (slot < 0 || slot >= 2 * battle.shipsPerSide) return false;
                bool defending = slot < battle.shipsPerSide;
                if (race != (defending ? battle.defender : battle.attacker) || !active.casualties.Add(slot)) return false;
                if (defending) active.defendersLost++;
                else active.attackersLost++;
                if (active.defendersLost < battle.shipsPerSide && active.attackersLost < battle.shipsPerSide) return false;
                winner = defending ? battle.attacker : battle.defender;
                if (winner == battle.attacker)
                {
                    string key = Key(battle.orbit);
                    dormantHeld.RemoveAll(x => x.orbit == key);
                    held[key] = winner;
                    // Invalidate old entitlements even if the new capture generates no rewards.
                    ReplaceOrbitRewards(battle.orbit, Array.Empty<VictoryReward>());
                }
                battles.Remove(battle.system);
                return true;
            }

            public int[] Casualties(Battle battle)
            {
                if (!battles.TryGetValue(battle.system, out var active) || active.battle.id != battle.id) return Array.Empty<int>();
                var result = new int[active.casualties.Count];
                active.casualties.CopyTo(result);
                return result;
            }

            public bool TryGetBattle(int orbit, long now, out Battle battle)
            {
                foreach (var active in battles.Values)
                    if (active.battle.orbit == orbit && active.battle.expires >= now)
                    {
                        battle = active.battle;
                        return true;
                    }
                battle = default;
                return false;
            }
        }
    }
}
