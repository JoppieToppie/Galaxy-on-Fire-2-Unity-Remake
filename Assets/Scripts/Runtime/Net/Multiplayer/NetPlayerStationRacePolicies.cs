using System;
using System.Collections.Generic;
using System.Text;
using GoF2Remake.Modding;

namespace GoF2Remake.Multiplayer
{
    [Serializable]
    public sealed class StationRaceAccessRule
    {
        public string race;
        public PlayerStationPermissionState state;
    }

    public static partial class NetPlayerStations
    {
        // Store stock race numbers and qualified mod keys, never load-order-dependent mod numbers.
        internal static bool TryPolicyRaceKey(string reference, out string key)
        {
            key = null;
            if (!ModRaces.TryResolve(reference, null, out int race)) return false;
            key = PlayerIdentity.RaceKey(race);
            return !string.IsNullOrEmpty(key);
        }

        internal static bool TryReadRaceAccess(string text, out string race, out PlayerStationPermissionState state)
        {
            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            race = null;
            state = PlayerStationPermissionState.Inherit;
            if (words.Length != 2 || !TryPolicyRaceKey(words[0], out race)) return false;
            switch (words[1].ToLowerInvariant())
            {
                case "inherit": return true;
                case "allow": state = PlayerStationPermissionState.Allow; return true;
                case "deny": state = PlayerStationPermissionState.Deny; return true;
                default: return false;
            }
        }

        internal static List<StationRaceAccessRule> WithRaceAccess(List<StationRaceAccessRule> rules,
            string race, PlayerStationPermissionState state)
        {
            var updated = rules == null ? new List<StationRaceAccessRule>() : new List<StationRaceAccessRule>(rules);
            updated.RemoveAll(rule => rule != null && string.Equals(rule.race, race, StringComparison.Ordinal));
            if (state != PlayerStationPermissionState.Inherit)
                updated.Add(new StationRaceAccessRule { race = race, state = state });
            return updated.Count == 0 ? null : updated;
        }

        internal static bool RaceAccessAllowed(string race, List<StationRaceAccessRule> local,
            List<StationRaceAccessRule> defaults)
        {
            if (string.IsNullOrEmpty(race)) return true;
            var state = local?.Find(rule => rule?.race == race)?.state ?? PlayerStationPermissionState.Inherit;
            if (state == PlayerStationPermissionState.Inherit)
                state = defaults?.Find(rule => rule?.race == race)?.state ?? PlayerStationPermissionState.Inherit;
            return state != PlayerStationPermissionState.Deny;
        }

        static List<StationRaceAccessRule> JumpgateRaceDefaults(PlayerJumpgateRecord gate) =>
            gate.ownershipType == PlayerStationOwnershipType.Faction
                ? NetFactions.FindById(gate.ownerFactionId)?.jumpgateRaceAccess : null;

        static bool AllowsJumpgateRace(PlayerJumpgateRecord gate, string account)
        {
            if (!ModPlayerInfrastructure.Current.JumpgateRacePoliciesEnabled) return true;
            if (ModPlayerInfrastructure.Current.JumpgateRaceExemptionsEnabled &&
                !string.IsNullOrEmpty(account) && gate.raceAccessExemptions?.Contains(account) == true) return true;
            var defaults = JumpgateRaceDefaults(gate);
            if (gate.raceAccess == null && defaults == null) return true;
            return RaceAccessAllowed(NetProfiles.StationPolicyRaceOf(account), gate.raceAccess, defaults);
        }

        internal static string SetPolicyProfileRace(NetPlayer pilot, string arg)
        {
            if (!NetGame.IsServer || pilot == null || !pilot.IsSpawned || !NetCommands.IsAdmin(pilot) ||
                !NetProfiles.Controls(pilot.OwnerClientId)) return "Only a server administrator can assign a profile race.";
            if (ProfileWritesBlocked) return "Profile changes are paused during station transaction recovery.";
            var words = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string race = null;
            if (words.Length != 2 || (!string.Equals(words[1], "clear", StringComparison.OrdinalIgnoreCase) &&
                !TryPolicyRaceKey(words[1], out race))) return "/station profile-race <profile ID> <race|clear>";
            return NetProfiles.TrySetStationPolicyRace(words[0], race)
                ? "Profile race saved." : "The race could not be saved. Completed pilot identities cannot be changed.";
        }

        internal static string SetJumpgateRace(NetPlayer pilot, string arg)
        {
            if (!NetGame.IsServer || !ModPlayerInfrastructure.Current.JumpgateRacePoliciesEnabled)
                return "Jumpgate race policies are disabled.";
            var words = arg.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || !int.TryParse(words[0], out int orbit))
                return "/station jumpgate-race <orbit> [<race> <inherit|allow|deny>]";
            var gate = FindJumpgate(orbit);
            if (gate == null || !CanManageJumpgate(pilot, gate, out _) || !JumpgateManagementDocked(pilot, gate))
                return "Dock in the gate's origin system with gate management authority.";
            if (words.Length == 1)
            {
                var lines = new StringBuilder("Jumpgate race rules (up to 12 per scope)");
                AppendRaceAccess(lines, "Gate", gate.raceAccess);
                AppendRaceAccess(lines, "Faction", JumpgateRaceDefaults(gate));
                return lines.ToString();
            }
            if (ProfileWritesBlocked) return "Gate policy changes are paused during station transaction recovery.";
            if (words[1].StartsWith("exempt ", StringComparison.OrdinalIgnoreCase))
                return SetJumpgateRaceExemption(gate, words[1].Substring("exempt ".Length));
            if (!TryReadRaceAccess(words[1], out string race, out var state))
                return "/station jumpgate-race <orbit> <race> <inherit|allow|deny>";
            var previous = gate.raceAccess;
            gate.raceAccess = WithRaceAccess(previous, race, state);
            if (Save()) return "Jumpgate race rule saved.";
            gate.raceAccess = previous;
            return "The jumpgate race rule could not be saved.";
        }

        static string SetJumpgateRaceExemption(PlayerJumpgateRecord gate, string arg)
        {
            if (!ModPlayerInfrastructure.Current.JumpgateRaceExemptionsEnabled)
                return "Gate managers cannot grant race exemptions under this mod configuration.";
            var words = arg.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length != 2 || (words[0] != "add" && words[0] != "remove"))
                return "/station jumpgate-race <orbit> exempt <add|remove> <pilot name or profile ID>";
            string account = ResolveStationAccount(words[1]);
            if (string.IsNullOrEmpty(account)) return "No matching server profile.";
            var previous = gate.raceAccessExemptions;
            var updated = previous == null ? new List<string>() : new List<string>(previous);
            updated.RemoveAll(entry => entry == account);
            if (words[0] == "add") updated.Add(account);
            gate.raceAccessExemptions = updated.Count == 0 ? null : updated;
            if (Save()) return "Jumpgate race exemption saved.";
            gate.raceAccessExemptions = previous;
            return "The jumpgate race exemption could not be saved.";
        }

        static void AppendRaceAccess(StringBuilder lines, string scope, List<StationRaceAccessRule> rules)
        {
            if (rules == null || rules.Count == 0) { lines.Append('\n').Append(scope).Append(" · Inherit"); return; }
            for (int i = 0; i < rules.Count && i < 12; i++)
            {
                var rule = rules[i];
                if (rule != null) lines.Append('\n').Append(scope).Append(" · ").Append(rule.race).Append(" · ").Append(rule.state);
            }
        }
    }
}
