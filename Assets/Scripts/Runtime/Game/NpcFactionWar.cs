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
        const int SlotsPerBattle = 64;
        const int LastBattleId = 0x7ffffe;
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
        public static bool IsWarTag(int tag) => tag >= FirstTag && tag < FirstTag + SlotsPerBattle * LastBattleId;

        public readonly struct Battle
        {
            public readonly int id, orbit, system, defender, attacker, shipsPerSide;
            public readonly long expires;

            public Battle(int id, int orbit, int system, int defender, int attacker, int shipsPerSide, long expires)
            {
                this.id = id;
                this.orbit = orbit;
                this.system = system;
                this.defender = defender;
                this.attacker = attacker;
                this.shipsPerSide = shipsPerSide;
                this.expires = expires;
            }

            public int Tag(int slot) => FirstTag + (id - 1) * SlotsPerBattle + slot;
            public int Slot(int tag) => tag - Tag(0);
        }

        [Serializable]
        sealed class HeldOrbit
        {
            public string orbit;
            public int race;
        }

        [Serializable]
        sealed class WarSave
        {
            public List<HeldOrbit> held = new List<HeldOrbit>();
            public long nextBattle;
        }

        sealed class Engagement
        {
            public Battle battle;
            public readonly HashSet<int> casualties = new HashSet<int>();
            public int defendersLost, attackersLost;
        }

        public sealed class Campaign
        {
            readonly Dictionary<string, int> held = new Dictionary<string, int>(StringComparer.Ordinal);
            readonly Dictionary<int, Engagement> battles = new Dictionary<int, Engagement>();
            int nextBattleId;
            long nextBattle;

            static string Key(int orbit) => ModWorld.StationKey(orbit) ?? orbit.ToString(CultureInfo.InvariantCulture);
            static bool Major(int race) => race >= 0 && race <= 3;

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
                            if (entry != null && !string.IsNullOrEmpty(entry.orbit) && Major(entry.race))
                                campaign.held[entry.orbit] = entry.race;
                }
                catch (ArgumentException e) { Debug.LogWarning($"NPC faction war save could not be read: {e.Message}"); }
                return campaign;
            }

            public string Snapshot()
            {
                var saved = new WarSave { nextBattle = nextBattle };
                foreach (var entry in held) saved.held.Add(new HeldOrbit { orbit = entry.Key, race = entry.Value });
                return JsonUtility.ToJson(saved);
            }

            public int OrbitOwner(SystemData system, int orbit)
            {
                if (system == null) return -1;
                return ModNpcFactionWar.CanContest(system) && held.Count > 0 && held.TryGetValue(Key(orbit), out int race)
                    ? race : system.raceId;
            }

            public int SystemOwner(SystemData system)
            {
                if (system == null) return -1;
                if (held.Count == 0 || system.stations == null || system.stations.Count == 0 || !ModNpcFactionWar.CanContest(system))
                    return system.raceId;
                var totals = new int[4];
                int orbits = 0;
                foreach (int station in system.stations)
                {
                    if (!ModNpcFactionWar.IsWarOrbit(station)) continue;
                    orbits++;
                    int race = OrbitOwner(system, station);
                    if (Major(race)) totals[race]++;
                }
                for (int race = 0; race < totals.Length; race++)
                    if (totals[race] > orbits / 2) return race;
                // No strict majority means the system is contested.
                return orbits == 0 ? system.raceId : -1;
            }

            public bool TryStart(Database db, int orbit, long now, out Battle battle)
            {
                battle = default;
                if (!ModNpcFactionWar.Enabled || db?.Systems == null || !ModNpcFactionWar.IsWarOrbit(orbit) ||
                    now < nextBattle || nextBattleId == LastBattleId) return false;
                var system = db.Systems.Find(s => s.stations != null && s.stations.Contains(orbit));
                if (!ModNpcFactionWar.CanContest(system)) return false;
                if (battles.TryGetValue(system.index, out var existing) && existing.battle.expires >= now) return false;
                int defender = OrbitOwner(system, orbit);
                int attacker = defender ^ 1;
                if (!Major(defender) || !Frontline(db, system, attacker)) return false;
                battle = new Battle(++nextBattleId, orbit, system.index, defender, attacker,
                    ModNpcFactionWar.ShipsPerSide, now + ModNpcFactionWar.BattleSeconds);
                battles[system.index] = new Engagement { battle = battle };
                nextBattle = now + ModNpcFactionWar.IntervalSeconds;
                return true;
            }

            public bool IsFrontline(Database db, SystemData system)
            {
                if (db?.Systems == null || !ModNpcFactionWar.CanContest(system) || system.stations == null) return false;
                int heldFactions = 0;
                foreach (int station in system.stations)
                {
                    if (!ModNpcFactionWar.IsWarOrbit(station)) continue;
                    int owner = OrbitOwner(system, station);
                    if (Major(owner)) heldFactions |= 1 << owner;
                }
                for (int race = 0; race < 4; race++)
                    if ((heldFactions & (1 << race)) != 0 && Frontline(db, system, race ^ 1)) return true;
                return false;
            }

            bool Frontline(Database db, SystemData system, int attacker)
            {
                if (system.stations.Exists(station => ModNpcFactionWar.IsWarOrbit(station) && OrbitOwner(system, station) == attacker))
                    return true;
                foreach (var other in db.Systems)
                {
                    if (other == null || other.index == system.index || !Major(other.raceId)) continue;
                    bool adjacent = system.jumpRoutesTo?.Contains(other.index) == true || other.jumpRoutesTo?.Contains(system.index) == true;
                    if (!adjacent && !UngatedNeighbors(db, system, other)) continue;
                    if (SystemOwner(other) == attacker) return true;
                    if (other.stations?.Exists(station => ModNpcFactionWar.IsWarOrbit(station) && OrbitOwner(other, station) == attacker) == true)
                        return true;
                }
                return false;
            }

            static bool UngatedNeighbors(Database db, SystemData a, SystemData b) =>
                ((a.jumpRoutesTo == null || a.jumpRoutesTo.Count == 0) && Nearest(db, a) == b.index) ||
                ((b.jumpRoutesTo == null || b.jumpRoutesTo.Count == 0) && Nearest(db, b) == a.index);

            static int Nearest(Database db, SystemData from)
            {
                int nearest = -1;
                double best = double.MaxValue;
                var pos = from.mapPosition;
                if (pos == null) return nearest;
                foreach (var other in db.Systems)
                {
                    if (other == null || other.index == from.index || !Major(other.raceId) || other.mapPosition == null) continue;
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
                if (!battles.TryGetValue(battle.system, out var active) || active.battle.id != battle.id ||
                    active.battle.expires < now) return false;
                int slot = battle.Slot(tag);
                if (slot < 0 || slot >= 2 * battle.shipsPerSide) return false;
                bool defending = slot < battle.shipsPerSide;
                if (race != (defending ? battle.defender : battle.attacker) || !active.casualties.Add(slot)) return false;
                if (defending) active.defendersLost++;
                else active.attackersLost++;
                if (active.defendersLost < battle.shipsPerSide && active.attackersLost < battle.shipsPerSide) return false;
                winner = defending ? battle.attacker : battle.defender;
                if (winner == battle.attacker) held[Key(battle.orbit)] = winner;
                battles.Remove(battle.system);
                return true;
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
