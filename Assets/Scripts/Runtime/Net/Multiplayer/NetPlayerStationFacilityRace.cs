using System;
using System.Collections.Generic;
using System.Text;
using GoF2Remake.Modding;

namespace GoF2Remake.Multiplayer
{
    [Serializable]
    public sealed class StationFacilityRaceRule
    {
        public string facility, race, reason;
        public PlayerStationPermissionState state;
        public int percent;
    }

    public static partial class NetPlayerStations
    {
        internal static IReadOnlyList<string> RacePolicyFacilities { get; } =
            Array.AsReadOnly(new[] { "docking", "storage", "hangar", "drydock", "refinery", "factory", "market" });

        static bool AllowsStationRace(PlayerStationRecord station, string account, string facility)
        {
            var settings = ModPlayerInfrastructure.Current;
            if (!settings.StationRacePoliciesEnabled || station == null || station.offeredForClaimUtc > 0 ||
                station.underConstruction || string.IsNullOrEmpty(account)) return true;
            var ownerFaction = station.ownershipType == PlayerStationOwnershipType.Faction
                ? NetFactions.FindById(station.ownerFactionId) : null;
            if (IsStationOwnerAccount(station, account) || ownerFaction?.leader == account ||
                (facility == "docking" && HasBuilderRights(station, account))) return true;
            if (settings.StationRaceExemptionsEnabled && station.facilityRaceExemptions?.Contains(account) == true)
                return true;
            return FacilityRaceAllowed(NetProfiles.StationPolicyRaceOf(account), facility,
                station.facilityRaceRules, ownerFaction?.stationRaceRules);
        }

        internal static bool FacilityRaceAllowed(string race, string facility,
            List<StationFacilityRaceRule> local, List<StationFacilityRaceRule> defaults)
        {
            if (string.IsNullOrEmpty(race)) return true;
            var state = local?.Find(rule => rule != null && rule.facility == facility && rule.race == race)?.state
                ?? PlayerStationPermissionState.Inherit;
            if (state == PlayerStationPermissionState.Inherit)
                state = defaults?.Find(rule => rule != null && rule.facility == facility && rule.race == race)?.state
                    ?? PlayerStationPermissionState.Inherit;
            return state != PlayerStationPermissionState.Deny;
        }

        static string FacilityRaceRefusal(PlayerStationRecord station, string account, string facility, string fallback)
        {
            if (station == null || AllowsStationRace(station, account, facility)) return fallback;
            string race = NetProfiles.StationPolicyRaceOf(account);
            var rule = station.facilityRaceRules?.Find(entry => entry != null && entry.facility == facility && entry.race == race);
            if ((rule == null || rule.state == PlayerStationPermissionState.Inherit) && station.ownershipType == PlayerStationOwnershipType.Faction)
                rule = NetFactions.FindById(station.ownerFactionId)?.stationRaceRules?.Find(entry => entry != null && entry.facility == facility && entry.race == race);
            return WithBlacklistReason(fallback, rule?.reason);
        }

        static string FacilityUseRefusal(PlayerStationRecord station, string account, string facility, string fallback) =>
            !AllowsStationRace(station, account, "docking")
                ? FacilityRaceRefusal(station, account, "docking", fallback)
                : FacilityRaceRefusal(station, account, facility, fallback);

        static int MarketPolicyPercent(PlayerStationRecord station, string account, string facility)
        {
            var settings = ModPlayerInfrastructure.Current;
            var pilotRule = station.marketplacePilotRules?.Find(rule => rule != null && rule.account == account && rule.operation == facility);
            if (pilotRule != null) return Math.Clamp(pilotRule.percent, facility == "purchase" ? -PilotMarketLimit : 0, PilotMarketLimit);
            int limit = settings.StationRaceMarketLimitPercent;
            if (!settings.StationRacePoliciesEnabled || limit == 0 ||
                (settings.StationRaceExemptionsEnabled && station.facilityRaceExemptions?.Contains(account) == true)) return 0;
            string race = NetProfiles.StationPolicyRaceOf(account);
            if (string.IsNullOrEmpty(race)) return 0;
            bool Matches(StationFacilityRaceRule rule) => rule != null && rule.race == race &&
                rule.facility == facility && rule.state == PlayerStationPermissionState.Allow;
            var local = station.facilityRaceRules?.Find(Matches);
            var defaults = local == null && station.ownershipType == PlayerStationOwnershipType.Faction
                ? NetFactions.FindById(station.ownerFactionId)?.stationRaceRules : null;
            return Math.Clamp(local?.percent ?? defaults?.Find(Matches)?.percent ?? 0,
                facility == "purchase" ? -limit : 0, limit);
        }

        internal static (long charge, long proceeds, long ownerRevenue) RaceMarketSettlement(
            long price, long tax, int purchasePercent, int salePercent, bool payOwner)
        {
            long surcharge = purchasePercent > 0 ? price * purchasePercent / 100 : 0;
            long discount = purchasePercent < 0 ? Math.Min(tax, price * -(long)purchasePercent / 100) : 0;
            long deduction = price * salePercent / 100;
            return (price + tax + surcharge - discount, price - deduction,
                tax - discount + (payOwner ? surcharge + deduction : 0));
        }

        static (long charge, long proceeds, long ownerRevenue) StationMarketSettlement(
            PlayerStationRecord station, string buyer, string seller, long price, long tax) =>
            RaceMarketSettlement(price, tax, MarketPolicyPercent(station, buyer, "purchase"),
                MarketPolicyPercent(station, seller, "sale"), ModPlayerInfrastructure.Current.StationRaceRevenueToOwner);

        internal static bool AllowsFacilityUse(PlayerStationRecord station, string account, string facility) =>
            AllowsStationRace(station, account, "docking") && AllowsStationRace(station, account, facility);

        internal static bool HasRaceProtectedProperty(PlayerStationRecord station, string account) =>
            !string.IsNullOrEmpty(account) &&
            (station.storageLots?.Exists(lot => lot != null && lot.amount > 0 && lot.ownerProfile == account) == true ||
             station.storedShipOwners?.Contains(account) == true || station.dryDockShipOwners?.Contains(account) == true ||
             station.listings?.Exists(listing => listing != null && listing.amount > 0 && listing.sellerProfile == account) == true ||
             station.shipListings?.Exists(listing => listing?.hull != null && listing.sellerProfile == account) == true ||
             station.buyOrders?.Exists(order => order != null && order.reservedCredits > 0 && order.fundedByProfile == account) == true ||
             (station.dryDockBuilding && station.dryDockBuilderProfile == account));

        internal static bool TryReadFacilityRaceRule(string text, out StationFacilityRaceRule rule)
        {
            var words = text.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            rule = null;
            if (words.Length != 2) return false;
            string facility = words[0].ToLowerInvariant();
            if (facility == "purchase" || facility == "sale")
            {
                var args = words[1].Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
                if (args.Length < 2 || !TryPolicyRaceKey(args[0], out string key)) return false;
                int limit = ModPlayerInfrastructure.Current.StationRaceMarketLimitPercent;
                bool inherit = args[1].Equals("inherit", StringComparison.OrdinalIgnoreCase);
                int percent = 0;
                if (!inherit && (limit == 0 || !int.TryParse(args[1], out percent) ||
                    percent < (facility == "purchase" ? -limit : 0) || percent > limit)) return false;
                rule = new StationFacilityRaceRule { facility = facility, race = key, percent = percent,
                    reason = !inherit && args.Length == 3 ? NormalizeBlacklistReason(args[2]) : null,
                    state = inherit ? PlayerStationPermissionState.Inherit : PlayerStationPermissionState.Allow };
                return true;
            }
            bool supported = false;
            foreach (string candidate in RacePolicyFacilities)
                if (candidate == facility) { supported = true; break; }
            if (!supported || !TryReadRaceAccess(words[1], out string race, out var state, out string reason)) return false;
            rule = new StationFacilityRaceRule { facility = facility, race = race, state = state, reason = reason };
            return true;
        }

        internal static List<StationFacilityRaceRule> WithFacilityRaceRule(List<StationFacilityRaceRule> rules,
            StationFacilityRaceRule rule)
        {
            var updated = rules == null ? new List<StationFacilityRaceRule>() : new List<StationFacilityRaceRule>(rules);
            updated.RemoveAll(entry => entry != null && entry.facility == rule.facility && entry.race == rule.race);
            if (rule.state != PlayerStationPermissionState.Inherit) updated.Add(rule);
            return updated.Count == 0 ? null : updated;
        }

        internal static string StationFacilityRaceCommand(NetPlayer pilot, string arg)
        {
            if (!NetGame.IsServer || !ModPlayerInfrastructure.Current.StationRacePoliciesEnabled)
                return "Station race policies are disabled.";
            const string usage = "/station race-policy <id> [<facility> <race> <inherit|allow|deny|percent>]";
            var words = arg.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || !int.TryParse(words[0], out int id)) return usage;
            var station = Find(id);
            if (!IsStationContext(pilot, station, out string account))
                return "Dock here with station authority to manage race policies.";
            bool owner = IsOwnershipAuthority(station, account);
            bool marketRule = words.Length > 1 && (words[1].StartsWith("purchase ", StringComparison.OrdinalIgnoreCase) ||
                words[1].StartsWith("sale ", StringComparison.OrdinalIgnoreCase));
            if (!owner && (!(words.Length == 1 || marketRule) || !CanManageMarketPolicy(station, account)))
                return "Station ownership authority is required for race access, and finance authority for race taxes.";
            if (words.Length == 1)
            {
                var summary = new StringBuilder("Station and faction race rules (first 16 per scope)");
                AppendFacilityRules(summary, "Station", station.facilityRaceRules);
                if (station.ownershipType == PlayerStationOwnershipType.Faction)
                    AppendFacilityRules(summary, "Faction", NetFactions.FindById(station.ownerFactionId)?.stationRaceRules);
                return summary.ToString();
            }
            if (ProfileWritesBlocked) return "Station changes are paused during transaction recovery.";
            if (words[1].StartsWith("exempt ", StringComparison.OrdinalIgnoreCase))
                return SetStationRaceExemption(station, words[1].Substring("exempt ".Length));
            if (!TryReadFacilityRaceRule(words[1], out var rule)) return usage;
            var previous = station.facilityRaceRules;
            station.facilityRaceRules = WithFacilityRaceRule(previous, rule);
            if (!Save())
            {
                station.facilityRaceRules = previous;
                return "The station race rule could not be saved.";
            }
            EjectDeniedVisitors(station);
            return "Station race rule saved.";
        }

        static string SetStationRaceExemption(PlayerStationRecord station, string arg)
        {
            if (!ModPlayerInfrastructure.Current.StationRaceExemptionsEnabled) return "Station race exemptions are disabled.";
            var words = arg.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length != 2 || (words[0] != "add" && words[0] != "remove"))
                return "/station race-policy <id> exempt <add|remove> <pilot name or profile ID>";
            string account = ResolveStationAccount(words[1]);
            if (string.IsNullOrEmpty(account)) return "No matching server profile.";
            var previous = station.facilityRaceExemptions;
            var updated = previous == null ? new List<string>() : new List<string>(previous);
            updated.RemoveAll(entry => entry == account);
            if (words[0] == "add") updated.Add(account);
            station.facilityRaceExemptions = updated.Count == 0 ? null : updated;
            if (!Save())
            {
                station.facilityRaceExemptions = previous;
                return "The station race exemption could not be saved.";
            }
            EjectDeniedVisitors(station);
            return "Station race exemption saved.";
        }

        static void AppendFacilityRules(StringBuilder lines, string scope, List<StationFacilityRaceRule> rules)
        {
            if (rules == null || rules.Count == 0) { lines.Append('\n').Append(scope).Append(" · Inherit"); return; }
            for (int i = 0; i < rules.Count && i < 16; i++)
            {
                var rule = rules[i];
                if (rule != null) lines.Append('\n').Append(scope).Append(" · ").Append(rule.facility)
                    .Append(" · ").Append(rule.race).Append(" · ").Append(WithBlacklistReason(rule.facility == "purchase" || rule.facility == "sale" ? rule.percent + "%" : rule.state.ToString(), rule.reason));
            }
        }
    }
}
