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
        void RebuildPageBar()
        {
            if (marketplaceOnly)
            {
                pageBar.style.display = DisplayStyle.None; primaryNav.style.display = DisplayStyle.None; stationList.style.display = DisplayStyle.None;
                return;
            }
            var pages = new List<(string key, string label)> { ("overview", "Overview"), ("directory", "Station directory") };
            if (Modding.ModPlayerInfrastructure.Current.Enabled) pages.Add(("build", "Build station"));
            if (LocalDeliveryProject() != null) pages.Add(("contribute", "Contribute"));
            pages.Add(("orders", "Buy orders"));
            if (inFlight) pages.Add(("orbit", "Orbit projects"));
            if (Modding.ModPlayerInfrastructure.Current.JumpgatesEnabled)
            {
                pages.Add(("gates", "Jumpgates"));
                int gateSystem = selected?.system ?? NetGame.Db.Stations.Find(entry => entry != null && entry.index == CurrentOrbit)?.system ?? -1;
                if (NetPlayerStations.Jumpgates?.Any(g => g != null && g.system == gateSystem && NetPlayerStations.CanViewJumpgateManagement(NetPlayer.Local, g)) == true)
                    pages.Add(("gate-security", "Jumpgate access"));
            }
            if (selected != null && !selected.underConstruction && selected.offeredForClaimUtc <= 0 && !inFlight && selected.stationIndex == CurrentOrbit)
            {
                bool HasModule(string kind) => selected.addons?.Any(addon => addon != null && addon.kind == kind && addon.level > 0) == true;
                pages.Add(("news", "News"));
                pages.Add(("bank", "Station bank"));
                if (managementCatalogue?.canManageMarket == true || managementCatalogue?.canSecurity == true)
                    pages.Add(("market", "Marketplace policy"));
                pages.Add(("hangar", "Hangar"));
                if (HasModule("drydock")) pages.Add(("drydock", "Dry dock"));
                if (managementCatalogue?.canManage == true)
                {
                    if (HasModule("refinery")) pages.Add(("refinery", "Refinery"));
                    if (HasModule("factory")) pages.Add(("factory", "Factory"));
                }
                if (managementCatalogue?.canOwnership == true || NetPlayer.Local?.CanRelinquishBuilderRightsInUi == true)
                    pages.Add(("ownership", "Ownership"));
                else pages.Add(("ownership", "Transfers"));
                if (managementCatalogue?.canSecurity == true || managementCatalogue?.canOwnership == true)
                {
                    pages.Add(("security", "Pilots"));
                    if (managementCatalogue?.canOwnership == true) pages.Add(("faction-access", "Factions"));
                    pages.Add(("blacklist", "Blacklist"));
                }
                if ((managementCatalogue?.canOwnership == true || managementCatalogue?.canManageMarket == true) &&
                    Modding.ModPlayerInfrastructure.Current.StationRacePoliciesEnabled)
                    pages.Add(("race-policies", "Race policies"));
                if (managementCatalogue?.canConstruction == true) { pages.Add(("modules", "Modules")); pages.Add(("defenses", "Defenses")); pages.Add(("placement", "Orbit editor")); }
            }
            if (!pages.Any(page => page.key == managementPage)) managementPage = "overview";
            stationList.style.display = managementPage == "directory" || managementPage == "orders" ? DisplayStyle.Flex : DisplayStyle.None;
            heading.text = selected == null ? "PLAYER STATIONS" : "STATION MANAGEMENT / " + selected.name;
            string Group(string key) => key switch
            {
                "overview" => "Overview",
                "modules" or "defenses" or "placement" or "gates" => "Infrastructure",
                "security" or "faction-access" or "blacklist" or "race-policies" or "gate-security" => "Security & Access",
                "bank" or "market" or "orders" => "Economy",
                "ownership" or "news" => "Governance",
                "hangar" or "drydock" or "refinery" or "factory" => "Facilities",
                _ => "Directory"
            };
            string activeGroup = Group(managementPage);
            string signature = managementPage + ":" + string.Join(",", pages.Select(page => page.key + page.label));
            pageBar.style.display = modelPickerKey == null ? DisplayStyle.Flex : DisplayStyle.None;
            primaryNav.style.display = modelPickerKey == null ? DisplayStyle.Flex : DisplayStyle.None;
            if (signature == pageBarSignature) return;
            pageBarSignature = signature;
            pageBar.Clear(); primaryNav.Clear();
            pageBar.style.flexDirection = FlexDirection.Column;
            VisualElement NavigationRow()
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.flexWrap = Wrap.Wrap;
                pageBar.Add(row);
                return row;
            }
            void Tab(VisualElement row, string title, string key, bool active)
            {
                var button = new Button(() => ChangeManagementPage(key)) { name = (row == primaryNav ? "managementGroup-" : "managementPage-") + key, text = title };
                button.AddToClassList("player-station-action");
                button.EnableInClassList("player-station-row--selected", active);
                button.AddToClassList(row == primaryNav ? "player-station-primary-tab" : "player-station-secondary-tab");
                if (row != primaryNav) button.style.minHeight = 36;
                button.clicked += () => menu?.PlayRelease();
                row.Add(button);
            }
            var groups = primaryNav;
            string[] groupOrder = { "Overview", "Infrastructure", "Security & Access", "Economy", "Governance", "Facilities", "Directory" };
            foreach (string groupName in groupOrder)
            {
                var entries = pages.Where(page => Group(page.key) == groupName).ToList();
                if (entries.Count == 0) continue;
                string first = entries.Any(page => page.key == "modules") ? "modules" : entries[0].key;
                Tab(groups, groupName, first, groupName == activeGroup);
            }

            var visiblePages = pages.Where(page => Group(page.key) == activeGroup).ToList();
            if (visiblePages.Count > 1)
            {
                var tabs = NavigationRow();
                tabs.AddToClassList("player-station-secondary-nav");
                foreach (var page in visiblePages)
                    Tab(tabs, page.label, page.key, managementPage == page.key);
            }
        }

        void RefreshManagementCatalogue(bool afterAction = false)
        {
            nextManagementQuery = Time.unscaledTime + 8f;
            if (choicePickerKey != null || modelPickerKey != null) return;
            if (!open || inFlight || selected == null || selected.underConstruction || selected.stationIndex != CurrentOrbit || selected.offeredForClaimUtc > 0) return;
            int stationId = selected.id;
            NetPlayer.Local?.RequestManagementCatalogue(stationId, directoryPage, catalogue =>
            {
                if (!open || selected?.id != stationId || catalogue == null) return;
                managementCatalogue = catalogue;
                directoryPage = catalogue.directoryPage;
                refresh = 0;
                if (afterAction) actionRefreshPending = true;
            });
        }

        void ChangeManagementPage(string page)
        {
            if (managementPage == page) return;
            managementPage = page; moduleDetail = null;
            if (page == "build" && Modding.ModPlayerInfrastructure.Current.Enabled) RefreshPlannerLocations();
            if (page == "bank" || page == "security" || page == "ownership" || page == "gates" || page == "factory" || page == "hangar" || page == "drydock" || page == "modules") RefreshManagementCatalogue();
            RebuildBody(Vector2.zero);
        }

        void RefreshPlannerLocations()
        {
            nextPlannerQuery = Time.unscaledTime + 8f;
            plannerRequestPending = true;
            if (NetPlayer.Local?.RequestStationPlannerLocations(orbits =>
            {
                if (!open) return;
                plannerRequestPending = false;
                var updated = new HashSet<int>(orbits);
                if (plannerOrbits == null || !plannerOrbits.SetEquals(updated)) refresh = 0;
                plannerOrbits = updated;
            }) != true) plannerRequestPending = false;
        }

    }
}
