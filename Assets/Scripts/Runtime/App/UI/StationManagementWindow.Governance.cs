using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Multiplayer;
using GoF2Remake.Data;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public sealed partial class StationManagementWindow
    {
        void BuildStationGovernance(PlayerStationRecord station)
        {
            string id = station.id.ToString();
            AddDirectoryPager();
            bool owner = managementCatalogue?.canOwnership == true;
            if (managementPage == "blacklist")
            {
                AddSection("STATION BLACKLIST", "");
                AddChoice("banTargetType", "Target type", new List<(string, string)> { ("pilot", "Pilot"), ("faction", "Faction") },
                    _ => drafts.Remove("banTarget"));
                bool faction = Value("banTargetType") == "faction";
                AddCatalogueChoice("banTarget", faction ? "Faction" : "Pilot", faction ? "faction" : "pilot");
                AddField("banReason", "Reason shown to denied pilots", "");
                AddAction("Add or update blacklist entry", () =>
                    SendWithReason($"/station {(faction ? "ban-faction" : "ban")} {id} {Value("banTarget")}", "banReason"));
                AddAction("Remove blacklist entry", () =>
                    Send($"/station {(faction ? "unban-faction" : "unban")} {id} {Value("banTarget")}"));
                if (owner) AddAction("View station history", "/station history " + id);
                return;
            }
            if (managementPage == "race-policies")
            {
                BuildRacePolicyDashboard(station, owner);
                return;
            }
            if (managementPage == "faction-access")
            {
                BuildFactionAccess(station, owner);
                return;
            }
            AddSection("PILOT ACCESS", "");
            AddAccountField("governanceAccount", "Target pilot");
            if (owner)
            {
                AddChoice("governanceRole", "Station role", Enum.GetNames(typeof(PlayerStationRole)).Append("none").Select(value => (value, value)).ToList());
                AddAction("Set station role", () => Send("/station role " + id + " " + Value("governanceAccount") + " " + Value("governanceRole")));
            }
            AddChoice("governancePermission", "Permission", Enum.GetNames(typeof(PlayerStationPermission)).Select(value => (value, value)).ToList());
            AddChoice("governanceOverride", "Account override", new[] { "inherit", "allow", "deny" }.Select(value => (value, value)).ToList());
            if (Value("governanceOverride") == "deny") AddField("permissionReason", "Reason for denying permission", "");
            AddAction("Set account override", () =>
            {
                string command = $"/station permission {id} {Value("governanceAccount")} {Value("governancePermission")} {Value("governanceOverride")}";
                if (Value("governanceOverride") == "deny") SendWithReason(command, "permissionReason");
                else Send(command);
            });
        }

        void BuildFactionAccess(PlayerStationRecord station, bool owner)
        {
            if (!owner) return;
            string id = station.id.ToString();
            AddSection("FACTION ACCESS", "Manage inherited permissions separately from individual pilot overrides.");
            if (station.ownershipType == PlayerStationOwnershipType.Individual)
            {
                AddAction("Share with your faction", "/station access " + id + " faction");
                AddAction("Set owner-only defaults", "/station access " + id + " owner");
                return;
            }
            AddChoice("governanceRank", "Faction rank", new[] { "member", "officer" }.Select(value => (value, value)).ToList());
            AddChoice("governanceFactionPermission", "Permission", Enum.GetNames(typeof(PlayerStationPermission)).Select(value => (value, value)).ToList());
            AddChoice("governanceDefault", "Default permission", new[] { "allow", "deny" }.Select(value => (value, value)).ToList());
            AddAction("Set faction default", () => Send("/station default " + id + " " + Value("governanceRank") + " " + Value("governanceFactionPermission") + " " + Value("governanceDefault")));
        }

        static string FacilityPolicyLabel(string facility) => facility switch
        {
            "docking" => "Docking", "storage" => "Storage", "hangar" => "Hangar",
            "drydock" => "Dry dock", "refinery" => "Refinery", "factory" => "Factory", "market" => "Marketplace",
            "purchase" => "Purchase adjustment", "sale" => "Sale deduction", _ => facility
        };

        void BuildRacePolicyDashboard(PlayerStationRecord station, bool owner)
        {
            string id = station.id.ToString();
            AddSection("RACE POLICIES", "Select a race to inspect inherited and station-specific policies. Changes apply only when confirmed.");
            var races = Modding.ModRefs.RaceWords.Where(entry => PlayerIdentity.IsSelectableRace(entry.race)).Select(entry => (value: PlayerIdentity.RaceKey(entry.race), label: entry.name))
                .Concat(Modding.ModRaces.All.Where(entry => PlayerIdentity.IsSelectableRace(entry.race)).Select(entry => (value: entry.key, label: Modding.ModRaces.Name(entry.race))))
                .GroupBy(race => race.value).Select(group => group.First()).OrderBy(race => race.label).ToList();
            if (races.Count == 0) { AddInfo("Race directory", "No races are configured."); return; }
            string race = Value("stationPolicyRace");
            if (!races.Any(entry => entry.value == race))
            {
                race = races[0].value; drafts["stationPolicyRace"] = race;
            }
            var layout = new VisualElement(); layout.AddToClassList("player-station-policy-layout"); body.Add(layout);
            var directory = new VisualElement(); directory.AddToClassList("player-station-race-directory"); layout.Add(directory);
            var directoryTitle = new Label("RACES"); directoryTitle.AddToClassList("player-station-section-title"); directory.Add(directoryTitle);
            foreach (var entry in races)
            {
                string key = entry.value;
                var button = new Button(() =>
                {
                    drafts["stationPolicyRace"] = key;
                    foreach (string draftKey in new[] { "raceFacility", "racePolicyState", "raceDenialReason", "raceMarketAction", "raceMarketMode", "raceMarketPercent", "raceMarketReason" })
                        drafts.Remove(draftKey);
                    RebuildBody(body.scrollOffset);
                }) { text = entry.label, enableRichText = false };
                button.AddToClassList("player-station-row");
                button.EnableInClassList("player-station-row--selected", key == race);
                directory.Add(button);
            }
            var matrix = DashboardCard(layout, races.First(entry => entry.value == race).label.ToUpperInvariant() + " POLICY");
            matrix.AddToClassList("player-station-policy-matrix");
            StationFacilityRaceRule LocalRule(string facility) => managementCatalogue?.stationRaceRules?.Find(rule => rule != null && rule.race == race && rule.facility == facility);
            StationFacilityRaceRule FactionRule(string facility) => managementCatalogue?.factionRaceRules?.Find(rule => rule != null && rule.race == race && rule.facility == facility);
            string AccessSummary(string facility)
            {
                var local = LocalRule(facility);
                var inherited = FactionRule(facility);
                if (local != null && local.state != PlayerStationPermissionState.Inherit)
                    return local.state == PlayerStationPermissionState.Deny ? "Denied · station" : "Allowed · station";
                return inherited?.state == PlayerStationPermissionState.Deny ? "Denied · inherited" : "Allowed · inherited";
            }
            if (owner)
            {
                foreach (string facility in NetPlayerStations.RacePolicyFacilities)
                {
                    string key = facility;
                    var row = new VisualElement(); row.AddToClassList("player-station-policy-row");
                    var label = new Label(FacilityPolicyLabel(key)); label.AddToClassList("player-station-policy-label"); row.Add(label);
                    var state = new Label(AccessSummary(key)); state.AddToClassList("player-station-policy-state");
                    state.EnableInClassList("player-station-policy-denied", state.text.StartsWith("Denied", StringComparison.Ordinal)); row.Add(state);
                    var edit = new Button(() =>
                    {
                        drafts["raceFacility"] = key;
                        drafts["racePolicyState"] = (LocalRule(key)?.state ?? PlayerStationPermissionState.Inherit).ToString().ToLowerInvariant();
                        drafts["raceDenialReason"] = LocalRule(key)?.reason ?? "";
                        RebuildBody(body.scrollOffset);
                        body.schedule.Execute(() => body.Q<Button>("choice-racePolicyState")?.Focus());
                    }) { text = "Edit" };
                    edit.AddToClassList("player-station-action"); row.Add(edit); matrix.Add(row);
                }
                var gate = new Label("Jumpgate access is managed under Infrastructure / Jumpgates.");
                gate.AddToClassList("player-station-note"); matrix.Add(gate);
            }
            if (managementCatalogue?.canManageMarket == true && Modding.ModPlayerInfrastructure.Current.StationRaceMarketLimitPercent > 0)
            {
                foreach (string operation in new[] { "purchase", "sale" })
                {
                    string key = operation;
                    var local = LocalRule(key); var inherited = FactionRule(key);
                    var row = new VisualElement(); row.AddToClassList("player-station-policy-row");
                    var label = new Label(FacilityPolicyLabel(key)); label.AddToClassList("player-station-policy-label"); row.Add(label);
                    var value = new Label($"{local?.percent ?? inherited?.percent ?? 0:+0;-0;0}% · {(local != null ? "station" : "inherited")}");
                    value.AddToClassList("player-station-policy-state"); row.Add(value);
                    var edit = new Button(() =>
                    {
                        drafts["raceMarketAction"] = key;
                        drafts["raceMarketMode"] = local != null ? "custom" : "inherit";
                        drafts["raceMarketPercent"] = (local?.percent ?? inherited?.percent ?? 0).ToString();
                        drafts["raceMarketReason"] = local?.reason ?? "";
                        RebuildBody(body.scrollOffset);
                        body.schedule.Execute(() => body.Q<Button>("choice-raceMarketMode")?.Focus());
                    }) { text = "Edit" };
                    edit.AddToClassList("player-station-action"); row.Add(edit); matrix.Add(row);
                }
            }
            if (owner && NetPlayerStations.RacePolicyFacilities.Contains(Value("raceFacility")))
            {
                string facility = Value("raceFacility");
                AddSection("EDIT " + FacilityPolicyLabel(facility).ToUpperInvariant(), "Changes are validated by the station server.");
                AddChoice("racePolicyState", "Access", new List<(string, string)> { ("inherit", "Inherit faction policy"), ("allow", "Allow"), ("deny", "Deny") });
                if (Value("racePolicyState") == "deny") AddField("raceDenialReason", "Denial reason", "");
                AddAction("Apply access rule", () =>
                {
                    string command = $"/station race-policy {id} {facility} {race} {Value("racePolicyState")}";
                    if (Value("racePolicyState") == "deny") SendWithReason(command, "raceDenialReason");
                    else Send(command);
                });
            }
            if (managementCatalogue?.canManageMarket == true && Modding.ModPlayerInfrastructure.Current.StationRaceMarketLimitPercent > 0 &&
                (Value("raceMarketAction") == "purchase" || Value("raceMarketAction") == "sale"))
            {
                string operation = Value("raceMarketAction");
                AddSection("EDIT " + FacilityPolicyLabel(operation).ToUpperInvariant(), "");
                AddChoice("raceMarketMode", "Adjustment", new List<(string, string)> { ("inherit", "Inherit faction policy"), ("custom", "Set percentage") });
                bool custom = Value("raceMarketMode") == "custom";
                if (custom)
                {
                    int limit = Modding.ModPlayerInfrastructure.Current.StationRaceMarketLimitPercent;
                    AddNumber("raceMarketPercent", "Percentage", 0, operation == "purchase" ? -limit : 0, limit);
                    AddField("raceMarketReason", "Reason (optional)", "");
                }
                AddAction("Apply price adjustment", () =>
                {
                    string adjustment = custom ? Value("raceMarketPercent") : "inherit";
                    string command = $"/station race-policy {id} {operation} {race} {adjustment}";
                    if (custom) SendWithOptionalReason(command, "raceMarketReason");
                    else Send(command);
                });
            }
            if (owner && Modding.ModPlayerInfrastructure.Current.StationRaceExemptionsEnabled)
            {
                AddSection("INDIVIDUAL EXEMPTIONS", "Exempt a pilot from the station's race policies.");
                AddAccountField("stationRaceExemption", "Exemption target");
                AddAction("Grant exemption", () => Send("/station race-policy " + id + " exempt add " + Value("stationRaceExemption")));
                AddAction("Revoke exemption", () => Send("/station race-policy " + id + " exempt remove " + Value("stationRaceExemption")));
            }
        }

        void BuildSuccessionControls(PlayerStationRecord station)
        {
            string id = station.id.ToString();
            AddDirectoryPager();
            if (managementCatalogue?.canOwnership == true || (managementCatalogue?.canManage == true && station.ownershipType == PlayerStationOwnershipType.Faction))
            {
                AddSection("SUCCESSION", "");
                if (station.ownershipType == PlayerStationOwnershipType.Faction)
                {
                    AddChoice("successorType", "Successor type", new[] { "player", "faction" }.Select(value => (value, value)).ToList(), _ => drafts.Remove("successorAccount"));
                    AddAccountField("successorAccount", "Successor");
                    AddAction("Set faction successor", () => Send("/station successor " + id + " " + Value("successorType") + " " + Value("successorAccount")));
                    AddAction("Clear faction successor", "/station successor " + id + " clear");
                    if (managementCatalogue?.canOwnership == true)
                    {
                        AddAccountField("delegateAccount", "Successor delegate");
                        AddAction("Set successor delegate", () => Send("/station successor-delegate " + id + " " + Value("delegateAccount")));
                        AddAction("Clear successor delegate", "/station successor-delegate " + id + " none");
                    }
                }
                else
                {
                    AddAccountField("successorAccount", "Successor");
                    AddAction("Set individual successor", () => Send("/station individual-successor " + id + " " + Value("successorAccount")));
                    AddAction("Clear individual successor", "/station individual-successor " + id + " clear");
                }
            }
            if (NetPlayer.Local?.CanRelinquishBuilderRightsInUi == true)
            {
                if (confirmBuilderRelinquish == station.id)
                {
                    AddAction("Confirm permanent relinquishment", () => { confirmBuilderRelinquish = -1; Send("/station builder-relinquish " + id); RebuildBody(); });
                    AddAction("Keep builder rights", () => { confirmBuilderRelinquish = -1; RebuildBody(); });
                }
                else AddAction("Relinquish builder rights", () => { confirmBuilderRelinquish = station.id; RebuildBody(); });
            }
        }

        void BuildOfficialStationBroadcast(string id)
        {
            AddSection("GALACTIC NEWS BROADCAST", "Official announcements appear on the existing GalNet ticker galaxy-wide. Your station shares one five-minute cooldown.");
            const string key = "stationBroadcast";
            var composer = new VisualElement();
            composer.AddToClassList("player-station-composer");
            var field = new TextField("Announcement") { maxLength = NetNews.StationAnnouncementLimit, multiline = true, value = drafts.TryGetValue(key, out var draft) ? draft : "" };
            field.AddToClassList("player-station-field");
            field.style.minHeight = 80;
            composer.Add(field);
            var counter = new Label();
            counter.AddToClassList("player-station-note");
            composer.Add(counter);
            var preview = new Label();
            preview.enableRichText = false;
            preview.AddToClassList("player-station-preview");
            composer.Add(preview);
            var cooldown = new Label();
            cooldown.AddToClassList("player-station-note");
            composer.Add(cooldown);
            bool pending = false;
            var publish = new Button(() =>
            {
                string message = System.Text.RegularExpressions.Regex.Replace((field.value ?? "").Trim(), @"\s+", " ");
                if (message.Length == 0 || pending) return;
                pending = Dispatch("/station broadcast " + id + " " + message, result =>
                {
                    pending = false;
                    if (result == "Official broadcast published to GalNet.")
                    {
                        drafts.Remove(key);
                        field.SetValueWithoutNotify("");
                        if (managementCatalogue != null) managementCatalogue.stationBroadcastReadyUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + NetNews.OfficialCooldownSeconds;
                    }
                });
            }) { text = "PUBLISH GALNET BROADCAST" };
            publish.AddToClassList("player-station-action");
            composer.Add(publish);
            void UpdateComposer()
            {
                string message = field.value ?? "";
                counter.text = $"{message.Length} / {NetNews.StationAnnouncementLimit} characters";
                preview.text = selected?.name + ": " + message;
                long remaining = Math.Max(0L, (managementCatalogue?.stationBroadcastReadyUtc ?? 0L) - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                cooldown.text = remaining > 0 ? $"Station cooldown: {remaining / 60:00}:{remaining % 60:00}" : "Station broadcast available";
                publish.SetEnabled(!pending && remaining == 0 && !string.IsNullOrWhiteSpace(message) && managementCatalogue?.canAnnounce == true);
            }
            field.RegisterValueChangedCallback(change => { drafts[key] = change.newValue; UpdateComposer(); });
            body.Add(composer);
            UpdateComposer();
            composer.schedule.Execute(UpdateComposer).Every(1000);
        }

    }
}
