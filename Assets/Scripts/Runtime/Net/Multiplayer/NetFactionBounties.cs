// NetFactionBounties.cs
// Server-owned kill counts and wanted notices. The issuer and wanted pilot are keyed by stable IDs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetFactions
    {
        const long BountyWindowSeconds = 7 * 86400, BountyDurationSeconds = 7 * 86400;

        [Serializable]
        sealed class BountyRecord
        {
            public string account, issuer;
            public List<long> pvpKills = new List<long>(), pveKills = new List<long>();
            public List<long> kills; // Legacy records did not distinguish PvP from PvE.
            public long wantedUntil;
            public long reward;
            public List<int> issuingSystems = new List<int>();
        }

        [Serializable]
        sealed class PiracyRecord
        {
            public string account;
            public List<long> kills = new List<long>();
            public bool pirate;
        }

        [Serializable]
        sealed class BountyFile
        {
            public List<BountyRecord> records = new List<BountyRecord>();
            public List<PiracyRecord> piracy = new List<PiracyRecord>();
            public List<WantedSighting> sightings = new List<WantedSighting>();
        }

        const long SightingLifetime = 900;
        [Serializable]
        sealed class WantedSighting
        {
            public string account, reporter;
            public int visitSystem = -1, station = -1;
            public bool reported;
            public long seenAt;
        }
        static readonly Dictionary<string, WantedSighting> sightingsByAccount = new Dictionary<string, WantedSighting>(StringComparer.Ordinal);

        static bool WantedAccount(string account, long now) =>
            (piracyByAccount.TryGetValue(account, out var piracy) && piracy.pirate) ||
            (bountiesByAccount.TryGetValue(account, out var records) &&
             records.Exists(record => record.wantedUntil > now && BountyIssuerName(record.issuer) != null));

        internal static void ObserveWantedSystem(NetPlayer pilot)
        {
            if (!PiracyEnabled || bountyFile == null || pilot == null || !NetProfiles.Controls(pilot.OwnerClientId) ||
                NetArena.InMatch(pilot.OwnerClientId)) return;
            string account = NetProfiles.AccountOf(pilot.OwnerClientId);
            var system = PiracySystemAt(pilot.Station);
            if (account == null || system == null) return;
            if (!sightingsByAccount.TryGetValue(account, out var sighting))
            {
                if (!WantedAccount(account, UnixNow)) return;
                sighting = new WantedSighting { account = account };
                bountyFile.sightings.Add(sighting);
                sightingsByAccount.Add(account, sighting);
            }
            if (sighting.visitSystem == system.index) return;
            sighting.visitSystem = system.index;
            sighting.reported = false;
            bountyDirty = true;
            SaveBounties();
        }

        internal static void TrackWantedSightings(IEnumerable<NetProxy> npcs)
        {
            if (!PiracyEnabled || bountyFile == null) return;
            long now = UnixNow;
            var candidates = new Dictionary<int, List<NetPlayer>>();
            foreach (var pilot in NetPlayer.All)
            {
                if (pilot == null || !pilot.IsSpawned) continue;
                ObserveWantedSystem(pilot);
                if (!pilot.InSpace || !pilot.ScanVisible || !NetProfiles.Controls(pilot.OwnerClientId) ||
                    NetArena.InMatch(pilot.OwnerClientId)) continue;
                string account = NetProfiles.AccountOf(pilot.OwnerClientId);
                if (account == null || !WantedAccount(account, now) ||
                    !sightingsByAccount.TryGetValue(account, out var sighting) || sighting.reported) continue;
                if (!candidates.TryGetValue(pilot.Station, out var group))
                    candidates.Add(pilot.Station, group = new List<NetPlayer>());
                group.Add(pilot);
            }
            if (candidates.Count == 0) return;
            foreach (var npc in npcs)
            {
                if (npc == null || !npc.IsSpawned || npc.Race < 0 || npc.Race == Standing.Pirate ||
                    npc.IsWingman || !npc.ScanVisible || !candidates.TryGetValue(npc.Station, out var group)) continue;
                var spec = npc.Spec?.spec;
                if (spec == null) continue;
                float range = CombatRadar.NpcSensorRange(NetGame.Db, spec.ship,
                    npc.BoundShip != null ? npc.BoundShip.detectRange : -1f, CombatRadar.DefaultNpcScannerRangeUnits);
                if (range <= 0f) continue;
                for (int i = group.Count - 1; i >= 0; i--)
                {
                    var pilot = group[i];
                    if (!NpcReportsPilot(npc, pilot) || !CombatRadar.DetectsShipAtDistance(NetGame.Db,
                        (npc.WorldPosition - pilot.Position).sqrMagnitude, range, pilot.ShipIndex, pilot.HasFittedStealthGenerator)) continue;
                    var sighting = sightingsByAccount[NetProfiles.AccountOf(pilot.OwnerClientId)];
                    sighting.reported = true;
                    sighting.station = pilot.Station;
                    sighting.seenAt = now;
                    sighting.reporter = Standing.RaceName(npc.Race);
                    bountyDirty = true;
                    group.RemoveAt(i);
                }
                if (group.Count == 0) candidates.Remove(npc.Station);
                if (candidates.Count == 0) break;
            }
            SaveBounties();
        }

        static bool NpcReportsPilot(NetProxy npc, NetPlayer pilot)
        {
            if (npc.Race == Standing.Pirate) return false;
            var spec = npc.Spec?.spec;
            if (spec == null || spec.alwaysFriend) return false;
            if (pilot.IsPirate) return true;
            foreach (ulong aggressor in npc.Aggressors)
                if (NetSquad.SameClient(aggressor, pilot)) return true;
            if (spec.alwaysNeutral && !npc.Status.Has(NetProxyStatus.Turned)) return false;
            if (npc.Status.Has(NetProxyStatus.AlwaysHostile) || spec.alwaysEnemy) return true;
            var system = PiracySystemAt(pilot.Station);
            if (system != null && system.raceId == npc.Race)
            {
                int territory = spec.eventTag == NetOrbit.GarrisonTag || spec.eventTag == NetOrbit.SiegeDefenderTag
                    ? NetFactionsClient.GarrisonRelation(pilot.Station, pilot.FactionTag)
                    : NetFactionsClient.Relation(pilot.Station, pilot.FactionTag, pilot.TollStation);
                if (territory != 0) return territory < 0;
            }
            if (Standing.IsEnemyWith(npc.Race, pilot.Standing0, pilot.Standing1, pilot.Signature, pilot.ModRaceStandings)) return true;
            var owner = NetSquad.Find(npc.OwnerClientId);
            return npc.Status.relation == NetProxy.Hostile && (owner == pilot || (owner != null && NetSquad.Same(owner, pilot)));
        }

        [Serializable]
        public sealed class PlayerWantedEntry
        {
            public string name, issuers, reporter;
            public bool pirate;
            public long reward;
            public int station = -1;
            public long seenAt;
        }

        [Serializable]
        public sealed class PlayerWantedSnapshot
        {
            public List<PlayerWantedEntry> entries = new List<PlayerWantedEntry>();
        }

        internal static PlayerWantedSnapshot PlayerWantedBoard(int station)
        {
            var result = new PlayerWantedSnapshot();
            if (!PiracyEnabled || bountyFile == null) return result;
            var accounts = new HashSet<string>(piracyByAccount.Keys, StringComparer.Ordinal);
            accounts.UnionWith(bountiesByAccount.Keys);
            long now = UnixNow;
            foreach (string account in accounts)
            {
                bool pirate = piracyByAccount.TryGetValue(account, out var piracy) && piracy.pirate;
                var issuers = new List<string>();
                long reward = pirate ? Modding.ModPvpPiracy.MaximumNpcBounty : 0;
                long factionReward = 0;
                int system = PiracySystemAt(station)?.index ?? -1;
                if (bountiesByAccount.TryGetValue(account, out var records))
                    foreach (var record in records)
                    {
                        if (record.wantedUntil <= now) continue;
                        bool npc = record.issuer.StartsWith("npc:", StringComparison.Ordinal);
                        if (npc && !pirate && !record.issuingSystems.Contains(system)) continue;
                        if (npc) reward = Math.Max(reward, NpcReward(record));
                        else factionReward = Session.SaturatingAdd(factionReward, record.reward);
                        string issuer = BountyIssuerName(record.issuer);
                        if (!string.IsNullOrEmpty(issuer)) issuers.Add(issuer);
                    }
                if (!pirate && issuers.Count == 0) continue;
                string name = NetProfiles.AccountName(account);
                if (string.IsNullOrEmpty(name)) continue;
                issuers.Sort(StringComparer.OrdinalIgnoreCase);
                var entry = new PlayerWantedEntry { name = name, pirate = pirate, reward = Session.SaturatingAdd(reward, factionReward), issuers = string.Join(", ", issuers) };
                if (sightingsByAccount.TryGetValue(account, out var sighting) && sighting.seenAt > 0 &&
                    now - sighting.seenAt < SightingLifetime && now >= sighting.seenAt)
                {
                    entry.station = sighting.station;
                    entry.seenAt = sighting.seenAt;
                    entry.reporter = sighting.reporter;
                }
                result.entries.Add(entry);
            }
            result.entries.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.name, b.name));
            return result;
        }

        const int MaxPiracyWindowSeconds = 365 * 86400;
        const int MaxPiracyKillThreshold = 1000;
        static bool PiracyEnabled => Modding.ModPvpPiracy.Enabled && NetProfiles.Enabled;

        static BountyFile bountyFile;
        static bool bountyDirty;
        static readonly Dictionary<string, PiracyRecord> piracyByAccount = new Dictionary<string, PiracyRecord>(StringComparer.Ordinal);
        static readonly Dictionary<string, List<BountyRecord>> bountiesByAccount = new Dictionary<string, List<BountyRecord>>(StringComparer.Ordinal);

        static void IndexBounty(BountyRecord record)
        {
            if (!bountiesByAccount.TryGetValue(record.account, out var records))
                bountiesByAccount.Add(record.account, records = new List<BountyRecord>());
            records.Add(record);
        }

        static void ReindexBounties()
        {
            sightingsByAccount.Clear();
            piracyByAccount.Clear();
            bountiesByAccount.Clear();
            if (bountyFile == null) return;
            foreach (var sighting in bountyFile.sightings)
                if (!sightingsByAccount.ContainsKey(sighting.account)) sightingsByAccount.Add(sighting.account, sighting);
            foreach (var record in bountyFile.piracy)
                if (!piracyByAccount.ContainsKey(record.account)) piracyByAccount.Add(record.account, record);
            foreach (var record in bountyFile.records) IndexBounty(record);
        }

        static BountyRecord FindBounty(string account, string issuer) =>
            account != null && bountiesByAccount.TryGetValue(account, out var records) ? records.Find(r => r.issuer == issuer) : null;

        static string BountyPath => Path.Combine(NetProfiles.Folder, "faction-bounties.json");

        static void LoadBounties()
        {
            bountyDirty = false;
            try
            {
                bountyFile = File.Exists(BountyPath)
                    ? JsonUtility.FromJson<BountyFile>(File.ReadAllText(BountyPath))
                    : new BountyFile();
                if (bountyFile?.records == null) throw new InvalidDataException("Missing bounty records.");
                bountyFile.sightings ??= new List<WantedSighting>();
                bountyFile.sightings.RemoveAll(x => x == null || string.IsNullOrEmpty(x.account));
                bountyFile.piracy ??= new List<PiracyRecord>();
                bountyFile.piracy.RemoveAll(r => r == null || string.IsNullOrEmpty(r.account));
                foreach (var r in bountyFile.piracy) r.kills ??= new List<long>();
                bountyFile.records.RemoveAll(r => r == null || string.IsNullOrEmpty(r.account) || string.IsNullOrEmpty(r.issuer));
                foreach (var record in bountyFile.records)
                {
                    record.issuingSystems ??= new List<int>();
                    record.pvpKills ??= new List<long>();
                    record.pveKills ??= new List<long>();
                    if (record.kills == null || record.kills.Count == 0) continue;
                    // Player factions previously counted only PvP. An NPC issuer's old kills may be PvE.
                    var history = record.issuer.StartsWith("faction:", StringComparison.Ordinal) ? record.pvpKills : record.pveKills;
                    history.AddRange(record.kills);
                    record.kills = null;
                    bountyDirty = true;
                }
                ReindexBounties();
                TickBounties();
            }
            catch (Exception e)
            {
                bountyFile = null;
                ReindexBounties();
                Debug.LogError($"NetFactions: could not load faction bounties ({e.Message}).");
            }
        }

        static bool SaveBounties()
        {
            if (bountyDirty) bountyDirty = !NetProfiles.Write(BountyPath, JsonUtility.ToJson(bountyFile, true));
            return !bountyDirty;
        }

        static void TickBounties()
        {
            if (bountyFile == null) return;
            long now = UnixNow;
            int before = bountyFile.records.Count + bountyFile.piracy.Count;
            bountyDirty |= bountyFile.records.RemoveAll(r => r.wantedUntil <= now &&
                !r.pvpKills.Exists(k => k > now - BountyWindowSeconds) &&
                !r.pveKills.Exists(k => k > now - BountyWindowSeconds)) > 0;
            bountyDirty |= bountyFile.piracy.RemoveAll(r => !r.pirate && !r.kills.Exists(k => k >= now - MaxPiracyWindowSeconds)) > 0;
            if (before != bountyFile.records.Count + bountyFile.piracy.Count) ReindexBounties();
            SaveBounties();
        }

        static string NpcBountyIssuer(int race)
        {
            var mod = Modding.ModRaces.Get(race);
            if (mod != null) return mod.hostile ? null : "npc:" + mod.key;
            return race >= 0 && race < Standing.Pirate ? "npc:" + race.ToString(CultureInfo.InvariantCulture) : null;
        }

        static string BountyIssuerName(string issuer)
        {
            if (issuer.StartsWith("faction:", StringComparison.Ordinal))
            {
                var faction = FindById(issuer.Substring("faction:".Length));
                return faction == null ? null : $"[{faction.tag}] {faction.name}";
            }
            if (issuer.StartsWith("npc:", StringComparison.Ordinal) &&
                Modding.ModRaces.TryResolve(issuer.Substring("npc:".Length), null, out int race))
                return Standing.RaceName(race);
            return null;
        }

        /// <summary>Issuer IDs for active bounties on a persistent profile, for future claim and enforcement rules.</summary>
        internal static IEnumerable<string> BountyIssuersOf(string account)
        {
            if (!PiracyEnabled || bountyFile == null || bountyDirty) yield break;
            long now = UnixNow;
            if (account == null || !bountiesByAccount.TryGetValue(account, out var records)) yield break;
            foreach (var r in records)
                if (r.wantedUntil > now && BountyIssuerName(r.issuer) != null)
                    yield return r.issuer;
        }

        static long NpcReward(BountyRecord record) =>
            Math.Min((long)Modding.ModPvpPiracy.MaximumNpcBounty,
                (long)(record.pveKills.Count / Math.Max(1, Modding.ModPvpPiracy.NpcKillsForBounty)) * Modding.ModPvpPiracy.NpcBountyIncrement);

        static bool AddBountyKill(string account, string issuer, long now, bool pvp, int weight = 1, int station = -1)
        {
            if (account == null || issuer == null) return false;
            var record = FindBounty(account, issuer);
            if (record == null)
            {
                bountyFile.records.Add(record = new BountyRecord { account = account, issuer = issuer });
                IndexBounty(record);
            }
            bool active = record.wantedUntil > now;
            var history = pvp ? record.pvpKills : record.pveKills;
            if (history.Count < int.MaxValue) history.Add(now);
            if (issuer.StartsWith("npc:", StringComparison.Ordinal))
            {
                if (NpcReward(record) > 0)
                {
                    record.wantedUntil = long.MaxValue;
                    int system = PiracySystemAt(station)?.index ?? -1;
                    if (system >= 0 && !record.issuingSystems.Contains(system)) record.issuingSystems.Add(system);
                }
            }
            else if (record.pvpKills.Count >= Modding.ModPvpPiracy.PlayerKillsForBounty)
                record.wantedUntil = long.MaxValue;
            bountyDirty = true;
            return !active && record.wantedUntil > now;
        }

        internal static bool SecureForBounties(int station, ulong? victim = null)
        {
            if (!PiracyEnabled) return false;
            var system = PiracySystemAt(station);
            return GalaxyMap.SecurityOf(system, KaamoStateAt(system, victim)) >= 3;
        }

        static int KaamoStateAt(SystemData system, ulong? client)
        {
            // Only Shima's effective security depends on per-pilot progress.
            if (system?.index != GalaxyMap.ShimaSystem) return 0;
            return client.HasValue ? NetProfiles.KaamoStateFor(client.Value) : Session.KaamoState;
        }

        internal static bool DropsEquipmentOnDeath(int station, bool pirate, ulong? client = null)
        {
            if (!Modding.ModPvpPiracy.Enabled || NetState.Instance?.ProfilesOn != true || NetArena.IsArenaOrbit(station))
                return false;
            var system = PiracySystemAt(station);
            return GalaxyMap.DeathPenaltiesApply(system, KaamoStateAt(system, client), pirate);
        }

        static SystemData PiracySystemAt(int station)
        {
            var db = NetGame.Db;
            var orbit = db?.Stations?.Find(s => s != null && s.index == station);
            return orbit == null ? null : db.Systems?.Find(s => s != null && s.index == orbit.system);
        }

        static bool ProtectsForBounties(Faction faction, Faction pilotFaction, string account) =>
            faction == pilotFaction ||
            (pilotFaction != null && (faction.bountyProtectedFactions?.Contains(pilotFaction.id) ?? false)) ||
            (account != null && (faction.bountyProtectedPilots?.Contains(account) ?? false));

        static bool HasPiracyExemption(Faction killerFaction, Faction victimFaction, string killer, string victim)
        {
            if (killerFaction != null && victimFaction != null && killerFaction != victimFaction &&
                (killerFaction.piracyRivals?.Contains(victimFaction.id) ?? false) &&
                (victimFaction.piracyRivals?.Contains(killerFaction.id) ?? false)) return true;

            foreach (var faction in list.factions)
                if (ProtectsForBounties(faction, killerFaction, killer) &&
                    (faction.piracyHostilePilots?.Contains(victim) ?? false)) return true;
            return false;
        }

        internal static void RefreshPiracyFlags()
        {
            foreach (var pilot in NetPlayer.All)
                if (pilot != null && pilot.IsSpawned) RefreshPiracyFlag(pilot);
        }

        static void RefreshPiracyFlag(NetPlayer pilot)
        {
            if (!PiracyEnabled || bountyFile == null || !NetProfiles.Controls(pilot.OwnerClientId))
            {
                pilot.SetPiracyFlags(false, false);
                return;
            }
            string account = NetProfiles.AccountOf(pilot.OwnerClientId);
            bool pirate = account != null && piracyByAccount.TryGetValue(account, out var piracy) && piracy.pirate;
            long now = UnixNow;
            bool wanted = account != null && bountiesByAccount.TryGetValue(account, out var records) && records.Exists(r => r.wantedUntil > now &&
                BountyIssuerName(r.issuer) != null);
            pilot.SetPiracyFlags(pirate, wanted);
        }

        // Only called before a newly created identity is completed. Never on login or respawn.
        // Replaying an interrupted creation is safe; this grants no bounty or kill history.
        internal static bool GrantStartingPiracy(string account)
        {
            if (!PiracyEnabled || bountyFile == null || string.IsNullOrEmpty(account) ||
                NetPlayerStations.ProfileWritesBlocked || !SaveBounties()) return false;
            bool added = !piracyByAccount.TryGetValue(account, out var record);
            if (added)
            {
                record = new PiracyRecord { account = account };
                bountyFile.piracy.Add(record);
                piracyByAccount.Add(account, record);
            }
            if (record.pirate) return true;
            record.pirate = true;
            bountyDirty = true;
            if (SaveBounties()) return true;
            record.pirate = false;
            if (added)
            {
                bountyFile.piracy.Remove(record);
                piracyByAccount.Remove(account);
            }
            bountyDirty = false;
            return false;
        }

        internal static int PiracyKillWeight(bool secure) => secure ? 2 : 1;

        static bool TrackPiracyKill(string account, long now, bool secure, out int count, out int threshold, out int windowSeconds)
        {
            count = threshold = windowSeconds = 0;
            if (!piracyByAccount.TryGetValue(account, out var record))
            {
                bountyFile.piracy.Add(record = new PiracyRecord { account = account });
                piracyByAccount.Add(account, record);
            }
            if (record.pirate) return false;

            threshold = GameHooks.PiracyKillThreshold?.Invoke() ?? GameHooks.DefaultPiracyKillThreshold;
            threshold = threshold > 0 ? Math.Min(threshold, MaxPiracyKillThreshold) : GameHooks.DefaultPiracyKillThreshold;
            windowSeconds = GameHooks.PiracyWindowSeconds?.Invoke() ?? GameHooks.DefaultPiracyWindowSeconds;
            windowSeconds = windowSeconds > 0 ? Math.Min(windowSeconds, MaxPiracyWindowSeconds) : GameHooks.DefaultPiracyWindowSeconds;

            // Retain enough timestamps to apply a changed rule on the next qualifying kill.
            record.kills.RemoveAll(k => k < now - MaxPiracyWindowSeconds);
            for (int i = 0; i < PiracyKillWeight(secure); i++) record.kills.Add(now);
            if (record.kills.Count > MaxPiracyKillThreshold)
                record.kills.RemoveRange(0, record.kills.Count - MaxPiracyKillThreshold);
            long cutoff = now - windowSeconds;
            foreach (long kill in record.kills)
                if (kill >= cutoff) count++;
            if (count >= threshold)
            {
                record.pirate = true;
                record.kills.Clear();
            }
            bountyDirty = true;
            return record.pirate;
        }

        internal static bool ClaimBounties(NetPlayer victim, NetPlayer hunter)
        {
            if (!PiracyEnabled || bountyFile == null || !NetProfiles.Controls(hunter.OwnerClientId)) return false;
            string account = NetProfiles.AccountOf(victim.OwnerClientId);
            if (account == null || account == NetProfiles.AccountOf(hunter.OwnerClientId)) return false;
            long now = UnixNow;
            bool pirate = piracyByAccount.TryGetValue(account, out var piracy) && piracy.pirate;
            bountiesByAccount.TryGetValue(account, out var records);
            var claimed = records == null ? new List<BountyRecord>() : new List<BountyRecord>(records);
            long reward = pirate ? Modding.ModPvpPiracy.MaximumNpcBounty : 0;
            long factionReward = 0;
            foreach (var record in claimed)
            {
                if (record.wantedUntil <= now || BountyIssuerName(record.issuer) == null) continue;
                if (record.issuer.StartsWith("npc:", StringComparison.Ordinal)) reward = Math.Max(reward, NpcReward(record));
                else factionReward = Session.SaturatingAdd(factionReward, record.reward);
            }
            reward = Session.SaturatingAdd(reward, factionReward);
            if (reward <= 0 || !SaveBounties()) return false;
            var hunterAccount = NetProfiles.AccountOf(hunter.OwnerClientId);
            foreach (var record in claimed) bountyFile.records.Remove(record);
            if (pirate) piracy.pirate = false;
            bountyDirty = true;
            if (!SaveBounties())
            {
                bountyFile.records.AddRange(claimed);
                if (pirate) piracy.pirate = true;
                bountyDirty = false;
                return false;
            }
            if (!NetProfiles.MoveCredits(hunter.OwnerClientId, hunterAccount, reward))
            {
                bountyFile.records.AddRange(claimed);
                if (pirate) piracy.pirate = true;
                bountyDirty = true;
                SaveBounties();
                ReindexBounties();
                return false;
            }
            ReindexBounties();
            RefreshPiracyFlag(victim);
            NetState.Instance?.SendStationCreditDelta(hunter.OwnerClientId, reward, "Bounty reward");
            NetState.Instance?.Notify(hunter.OwnerClientId, $"Claimed {reward:N0} credits on {victim.DisplayName}.");
            return true;
        }

        internal static void RecordBountyPvpKill(NetPlayer victim, NetPlayer killer, bool secure)
        {
            if (!PiracyEnabled || bountyFile == null || list == null || !NetProfiles.Controls(killer.OwnerClientId)) return;
            string account = NetProfiles.AccountOf(killer.OwnerClientId);
            string victimAccount = NetProfiles.AccountOf(victim.OwnerClientId);
            if (account == victimAccount) return;
            var victimFaction = Of(victimAccount);
            var killerFaction = Of(account);
            var siege = SiegeAt(victim.Station);
            if (!secure && (victim.IsPirate || HasPiracyExemption(killerFaction, victimFaction, account, victimAccount) ||
                (siege != null && siege.started && victimFaction != null && killerFaction != null &&
                ((siege.attacker == victimFaction.id && siege.defender == killerFaction.id) ||
                 (siege.defender == victimFaction.id && siege.attacker == killerFaction.id))))) return;

            long now = UnixNow;
            bool becamePirate = TrackPiracyKill(account, now, secure, out int count, out int threshold, out int windowSeconds);
            var issuedBy = new List<string>();
            foreach (var faction in list.factions)
                if (ProtectsForBounties(faction, victimFaction, victimAccount)) CountKill("faction:" + faction.id);

            // Reputation is the existing NPC friendship rule. A temporary signature is not earned standing.
            for (int race = 0; race < 4; race++)
                if (Standing.IsFriendWith(race, victim.Standing0, victim.Standing1, -1))
                    CountKill(NpcBountyIssuer(race));
            foreach (var race in Modding.ModRaces.All)
                if (Standing.IsFriendWith(race.race, victim.Standing0, victim.Standing1, -1, victim.ModRaceStandings))
                    CountKill("npc:" + race.key);

            if (SaveBounties())
            {
                RefreshPiracyFlag(killer);
                if (count > 0)
                    Modding.ModScripts.Raise("piracyKillRegistered", (double)killer.OwnerClientId, (double)victim.OwnerClientId,
                        victim.Station, count, threshold, windowSeconds);
                if (becamePirate)
                {
                    NetState.Instance?.Notify(killer.OwnerClientId, "You have been flagged as a pirate.");
                    Modding.ModScripts.Raise("piracyStatusChanged", (double)killer.OwnerClientId, true);
                }
                if (issuedBy.Count > 0)
                    NetState.Instance?.Notify(killer.OwnerClientId, $"Bounties issued on you by {string.Join(", ", issuedBy)}.");
            }

            void CountKill(string issuer)
            {
                if (AddBountyKill(account, issuer, now, pvp: true, weight: PiracyKillWeight(secure), station: victim.Station)) issuedBy.Add(BountyIssuerName(issuer));
            }
        }

        internal static void RecordBountyNpcKill(int race, ulong client)
        {
            if (!PiracyEnabled || bountyFile == null || !NetProfiles.Controls(client)) return;
            string issuer = NpcBountyIssuer(race);
            var killer = issuer == null ? null : NetSquad.Find(client);
            if (killer == null || NetArena.InMatch(client)) return;
            bool issued = AddBountyKill(NetProfiles.AccountOf(client), issuer, UnixNow, pvp: false, station: killer.Station);
            if (SaveBounties() && issued)
            {
                RefreshPiracyFlag(killer);
                NetState.Instance?.Notify(client, $"{BountyIssuerName(issuer)} has issued a bounty on you.");
            }
        }

        internal static bool TryCompleteNpcDiplomacy(NetPlayer pilot, int station, int race, int quotedPrice,
            int uploadSequence, out int price, out int forgiven, out string refusal)
        {
            price = forgiven = 0;
            refusal = "Diplomacy is available while docked on your controlling profile.";
            if (!PiracyEnabled || pilot == null) return false;
            ulong client = pilot.OwnerClientId;
            if (!NetProfiles.Controls(client) || !NetGuard.Station(station) || !NetGuard.DockedAt(client, station) ||
                NetArena.InMatch(client) || NetPlayerStations.ProfileWritesBlocked) return false;

            var mod = Modding.ModRaces.Get(race);
            string issuer = NpcBountyIssuer(race);
            refusal = "A diplomat cannot represent that faction at this station.";
            if (issuer == null || (mod == null && (race < 0 || race > 3))) return false;
            if (mod != null)
            {
                var owner = Modding.ModRaces.Get(PiracySystemAt(station)?.raceId ?? -1);
                if (!mod.diplomats || owner == null || mod.mod != owner.mod) return false;
            }

            bool host = NetProfiles.IsHostClient(client);
            int standing = host ? Standing.RawToward(race) : Standing.TowardWith(race, pilot.Standing0, pilot.Standing1, -1, pilot.ModRaceStandings);
            refusal = "Diplomats can reconcile hostile standing or pardon a pirate.";
            var wantedRecord = FindBounty(NetProfiles.AccountOf(client), issuer);
            if (standing > -71 && !pilot.IsPirate && !(wantedRecord != null && wantedRecord.wantedUntil > UnixNow)) return false;
            price = AgentGenerator.DiplomatCostsForStanding(standing, pilot.IsPirate);
            refusal = "The diplomacy quote has changed. Ask the diplomat for a new quote.";
            if (price != quotedPrice) return false;
            refusal = "Your profile has not synced. Try the diplomacy deal again.";
            if (!host && !NetProfiles.ConsumeActionUpload(client, uploadSequence)) return false;
            refusal = "The bounty records are unavailable. Try the diplomacy deal again.";
            if (bountyFile == null || !SaveBounties()) return false;

            string account = NetProfiles.AccountOf(client);
            refusal = "The server could not collect the diplomacy fee.";
            if ((host && Session.Credits < price) || !NetProfiles.MoveCredits(client, account, -price)) return false;

            var record = FindBounty(account, issuer);
            var piracy = piracyByAccount.TryGetValue(account, out var indexedPiracy) && indexedPiracy.pirate ? indexedPiracy : null;
            var pveKills = record?.pveKills;
            if (record != null || piracy != null)
            {
                if (pveKills?.Count > 0)
                {
                    long cutoff = UnixNow - BountyWindowSeconds;
                    forgiven = pveKills.FindAll(k => k > cutoff).Count;
                    
                }
                if (record != null) bountyFile.records.Remove(record);
                if (piracy != null) piracy.pirate = false;
                bountyDirty = true;
                if (!SaveBounties())
                {
                    if (record != null) bountyFile.records.Add(record);
                    if (piracy != null) piracy.pirate = true;
                    bountyDirty = false;
                    if (!NetProfiles.MoveCredits(client, account, price))
                    {
                        NetState.Instance?.SendStationCreditDelta(client, -price, "Diplomacy fee");
                        Debug.LogError($"NetFactions: could not refund the failed diplomacy deal for {account}.");
                        refusal = "Diplomacy could not be saved and the fee refund failed.";
                    }
                    else refusal = "Diplomacy could not be saved. The fee was refunded.";
                    return false;
                }
            }

            ReindexBounties();
            RefreshPiracyFlag(pilot);
            if (piracy != null)
            {
                NetState.Instance?.Notify(client, "Your pirate status has been revoked. Active faction bounties remain.");
                Modding.ModScripts.Raise("piracyStatusChanged", (double)client, false);
            }
            refusal = null;
            return true;
        }

        static string BountiesText(ulong client)
        {
            if (!PiracyEnabled) return "Faction bounties are disabled by the active mods.";
            if (bountyFile == null || !SaveBounties()) return "Faction bounties are temporarily unavailable.";
            var result = new StringBuilder();
            long now = UnixNow;
            int shown = 0, total = 0;
            foreach (var r in bountyFile.records)
            {
                if (r.wantedUntil <= now) continue;
                if (r.issuer.StartsWith("npc:", StringComparison.Ordinal) && !r.issuingSystems.Contains(PiracySystemAt(NetSquad.Find(client)?.Station ?? -1)?.index ?? -1)) continue;
                string issuer = BountyIssuerName(r.issuer);
                string pilot = NetProfiles.AccountName(r.account);
                if (issuer == null || string.IsNullOrEmpty(pilot)) continue;
                total++;
                if (shown >= 10) continue;
                if (shown++ > 0) result.AppendLine();
                result.Append($"{pilot} is wanted by {issuer} ({(r.issuer.StartsWith("npc:", StringComparison.Ordinal) ? NpcReward(r) : r.reward):N0} credits).");
            }
            if (total > shown) result.AppendLine().Append($"Showing {shown} of {total} active bounties.");
            return shown == 0 ? "There are no active faction bounties." : result.ToString();
        }

        static string SetFactionBounty(ulong client, string account, string arg)
        {
            var faction = Of(account);
            if (!PiracyEnabled || bountyFile == null || !NetProfiles.Controls(client) || faction == null || !IsOfficer(faction, account))
                return "Only faction officers may fund bounties.";
            int split = arg.LastIndexOf(' ');
            if (split <= 0 || !long.TryParse(arg.Substring(split + 1), out long amount) || amount <= 0)
                return "Usage: /faction bounty PILOT AMOUNT";
            string target = NetProfiles.FindAccount(arg.Substring(0, split).Trim());
            if (target == null || target == account || Of(target) == faction) return "Choose a pilot outside your faction.";
            string issuer = "faction:" + faction.id;
            var record = FindBounty(target, issuer);
            if (record == null || record.wantedUntil <= UnixNow) return "That pilot is not wanted by your faction.";
            if (amount <= record.reward) return "The new bounty must exceed the existing funded bounty.";
            long cost = amount - record.reward;
            if (faction.bank < cost || !SaveBounties()) return "Insufficient faction funds or bounty storage unavailable.";
            faction.bank -= cost;
            if (!Save()) { faction.bank += cost; return "Could not save faction treasury."; }
            long previous = record.reward;
            record.reward = amount;
            bountyDirty = true;
            if (!SaveBounties())
            {
                record.reward = previous;
                bountyDirty = false;
                faction.bank += cost;
                Save();
                return "Could not save bounty.";
            }
            return $"Funded bounty: {amount:N0} credits.";
        }

        static string SetPiracyRelation(ulong client, string account, string target, bool rival, bool enabled)
        {
            if (!PiracyEnabled) return "Piracy is disabled by the active mods.";
            var faction = Of(account);
            if (!NetProfiles.Controls(client) || faction == null || !IsOfficer(faction, account))
                return "Only faction leaders and officers using a controlled profile can change piracy relations.";
            string id = rival ? ByTag(target)?.id : NetProfiles.FindAccount(target);
            if (id == null || id == faction.id) return "Choose another faction tag or a valid pilot name.";
            var relations = rival ? (faction.piracyRivals ??= new List<string>()) : (faction.piracyHostilePilots ??= new List<string>());
            if (relations.Contains(id) == enabled) return "That piracy relation is already set.";
            if (enabled && relations.Count >= 100) return "This faction has reached its piracy relation limit.";
            if (enabled) relations.Add(id);
            else relations.Remove(id);
            if (Save()) return "Piracy relation updated.";
            if (enabled) relations.Remove(id);
            else relations.Add(id);
            return "The server could not save that piracy relation.";
        }

        static string SetBountyProtection(ulong client, string account, string arg, bool protect)
        {
            if (!PiracyEnabled) return "Faction bounties are disabled by the active mods.";
            var faction = Of(account);
            if (!NetProfiles.Controls(client) || faction == null || !IsOfficer(faction, account))
                return "Only faction leaders and officers using a controlled profile can change bounty protection.";

            var parts = arg.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || (parts[0] != "faction" && parts[0] != "pilot"))
                return "Usage is /faction protect or unprotect, followed by faction TAG or pilot NAME.";
            string target = parts[1].Trim();
            bool isFaction = parts[0] == "faction";
            string id = isFaction ? ByTag(target)?.id : NetProfiles.FindAccount(target);
            if (id == null) return "No matching faction or pilot profile was found.";
            if (isFaction && id == faction.id) return "Your faction members are already protected.";
            var protectedIds = isFaction
                ? (faction.bountyProtectedFactions ??= new List<string>())
                : (faction.bountyProtectedPilots ??= new List<string>());
            if (protectedIds.Contains(id) == protect)
                return protect ? "Bounty protection is already active." : "Bounty protection is already inactive.";
            if (protect && protectedIds.Count >= 100)
                return "This faction has reached its bounty protection limit.";
            if (protect) protectedIds.Add(id);
            else protectedIds.Remove(id);
            if (Save()) return protect ? "Bounty protection added." : "Bounty protection removed.";
            if (protect) protectedIds.Remove(id);
            else protectedIds.Add(id);
            return "The server could not save the bounty protection change.";
        }
    }
}
