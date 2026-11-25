// NetFactionBounties.cs
// Server-owned kill counts and wanted notices. The issuer and wanted pilot are keyed by stable IDs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetFactions
    {
        const int PvpKillsForBounty = 10, PveKillsPerPvpKill = 10;
        const int PveKillsForBounty = PvpKillsForBounty * PveKillsPerPvpKill;
        const long BountyWindowSeconds = 7 * 86400, BountyDurationSeconds = 7 * 86400;

        [Serializable]
        sealed class BountyRecord
        {
            public string account, issuer;
            public List<long> pvpKills = new List<long>(), pveKills = new List<long>();
            public List<long> kills; // Legacy records did not distinguish PvP from PvE.
            public long wantedUntil;
        }

        [Serializable]
        sealed class BountyFile
        {
            public List<BountyRecord> records = new List<BountyRecord>();
        }

        static BountyFile bountyFile;
        static bool bountyDirty;
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
                bountyFile.records.RemoveAll(r => r == null || string.IsNullOrEmpty(r.account) || string.IsNullOrEmpty(r.issuer));
                foreach (var record in bountyFile.records)
                {
                    record.pvpKills ??= new List<long>();
                    record.pveKills ??= new List<long>();
                    if (record.kills == null || record.kills.Count == 0) continue;
                    // Player factions previously counted only PvP. An NPC issuer's old kills may be PvE.
                    var history = record.issuer.StartsWith("faction:", StringComparison.Ordinal) ? record.pvpKills : record.pveKills;
                    history.AddRange(record.kills);
                    record.kills = null;
                    bountyDirty = true;
                }
                TickBounties();
            }
            catch (Exception e)
            {
                bountyFile = null;
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
            bountyDirty |= bountyFile.records.RemoveAll(r => r.wantedUntil <= now &&
                !r.pvpKills.Exists(k => k > now - BountyWindowSeconds) &&
                !r.pveKills.Exists(k => k > now - BountyWindowSeconds)) > 0;
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
            if (bountyFile == null || bountyDirty) yield break;
            long now = UnixNow;
            foreach (var r in bountyFile.records)
                if (r.account == account && r.wantedUntil > now && BountyIssuerName(r.issuer) != null)
                    yield return r.issuer;
        }

        static bool AddBountyKill(string account, string issuer, long now, bool pvp)
        {
            var record = bountyFile.records.Find(r => r.account == account && r.issuer == issuer);
            if (record == null)
                bountyFile.records.Add(record = new BountyRecord { account = account, issuer = issuer });
            if (record.wantedUntil > now) return false;

            record.pvpKills.RemoveAll(k => k <= now - BountyWindowSeconds);
            record.pveKills.RemoveAll(k => k <= now - BountyWindowSeconds);
            (pvp ? record.pvpKills : record.pveKills).Add(now);
            bool issued = (long)record.pvpKills.Count * PveKillsPerPvpKill + record.pveKills.Count >= PveKillsForBounty;
            if (issued)
            {
                record.pvpKills.Clear();
                record.pveKills.Clear();
                record.wantedUntil = now + BountyDurationSeconds;
            }
            bountyDirty = true;
            return issued;
        }

        internal static void RecordBountyPvpKill(NetPlayer victim, NetPlayer killer)
        {
            if (bountyFile == null || list == null || !NetProfiles.Controls(killer.OwnerClientId)) return;
            string account = NetProfiles.AccountOf(killer.OwnerClientId);
            string victimAccount = NetProfiles.AccountOf(victim.OwnerClientId);
            if (account == victimAccount) return;
            var victimFaction = Of(victimAccount);
            var killerFaction = Of(account);
            var siege = SiegeAt(victim.Station);
            if (siege != null && siege.started && victimFaction != null && killerFaction != null &&
                ((siege.attacker == victimFaction.id && siege.defender == killerFaction.id) ||
                 (siege.defender == victimFaction.id && siege.attacker == killerFaction.id))) return;

            long now = UnixNow;
            var issuedBy = new List<string>();
            foreach (var faction in list.factions)
                if (faction == victimFaction ||
                    (victimFaction != null && (faction.bountyProtectedFactions?.Contains(victimFaction.id) ?? false)) ||
                    (victimAccount != null && (faction.bountyProtectedPilots?.Contains(victimAccount) ?? false)))
                    CountKill("faction:" + faction.id);

            // Reputation is the existing NPC friendship rule. A temporary signature is not earned standing.
            for (int race = 0; race < 4; race++)
                if (Standing.IsFriendWith(race, victim.Standing0, victim.Standing1, -1))
                    CountKill(NpcBountyIssuer(race));
            foreach (var race in Modding.ModRaces.All)
                if (Standing.IsFriendWith(race.race, victim.Standing0, victim.Standing1, -1, victim.ModRaceStandings))
                    CountKill("npc:" + race.key);

            if (SaveBounties() && issuedBy.Count > 0)
                NetState.Instance?.Notify(killer.OwnerClientId, $"Bounties issued on you by {string.Join(", ", issuedBy)}.");

            void CountKill(string issuer)
            {
                if (AddBountyKill(account, issuer, now, pvp: true)) issuedBy.Add(BountyIssuerName(issuer));
            }
        }

        internal static void RecordBountyNpcKill(int race, ulong client)
        {
            if (bountyFile == null || !NetProfiles.Controls(client)) return;
            string issuer = NpcBountyIssuer(race);
            var killer = issuer == null ? null : NetSquad.Find(client);
            if (killer == null || NetArena.InMatch(client)) return;
            bool issued = AddBountyKill(NetProfiles.AccountOf(client), issuer, UnixNow, pvp: false);
            if (SaveBounties() && issued)
                NetState.Instance?.Notify(client, $"{BountyIssuerName(issuer)} has issued a bounty on you.");
        }

        static string BountiesText()
        {
            if (bountyFile == null || !SaveBounties()) return "Faction bounties are temporarily unavailable.";
            var result = new StringBuilder();
            long now = UnixNow;
            int shown = 0, total = 0;
            foreach (var r in bountyFile.records)
            {
                if (r.wantedUntil <= now) continue;
                string issuer = BountyIssuerName(r.issuer);
                string pilot = NetProfiles.AccountName(r.account);
                if (issuer == null || string.IsNullOrEmpty(pilot)) continue;
                total++;
                if (shown >= 10) continue;
                if (shown++ > 0) result.AppendLine();
                result.Append($"{pilot} is wanted by {issuer} ({(r.wantedUntil - now) / 3600.0:0.#} hours remaining).");
            }
            if (total > shown) result.AppendLine().Append($"Showing {shown} of {total} active bounties.");
            return shown == 0 ? "There are no active faction bounties." : result.ToString();
        }

        static string SetBountyProtection(ulong client, string account, string arg, bool protect)
        {
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
