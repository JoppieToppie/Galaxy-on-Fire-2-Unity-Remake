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
        VisualElement DashboardCard(VisualElement parent, string title)
        {
            var card = new VisualElement();
            card.AddToClassList("player-station-dashboard-card");
            var caption = new Label(title) { enableRichText = false };
            caption.AddToClassList("player-station-section-title");
            card.Add(caption); parent.Add(card);
            return card;
        }

        void DashboardMetric(VisualElement grid, string title, string value)
        {
            var card = DashboardCard(grid, title);
            card.AddToClassList("player-station-metric");
            var label = new Label(value) { enableRichText = false };
            label.AddToClassList("player-station-metric-value");
            card.Add(label);
        }

        void BuildOverviewDashboard(string id, bool docked)
        {
            var station = selected;
            var db = NetGame.Db;
            var system = db?.Systems?.Find(entry => entry != null && entry.index == station.system);
            var orbit = db?.Stations?.Find(entry => entry != null && entry.index == station.stationIndex);
            var hero = new VisualElement(); hero.AddToClassList("player-station-dashboard-hero"); body.Add(hero);
            var visual = DashboardCard(hero, station.name);
            visual.AddToClassList("player-station-identity-card");
            int model = station.looksLike >= 0 ? station.looksLike : station.stationIndex;
            var image = Modding.ModPlayerInfrastructure.StationModelImage(model);
            if (image.texture != null)
            {
                var preview = new VisualElement();
                preview.AddToClassList("player-station-hero-preview");
                preview.style.backgroundImage = new StyleBackground(image.texture);
                visual.Add(preview);
            }
            var location = new Label((system?.name ?? "Unknown system") + " / " + (orbit?.name ?? "Unknown orbit")) { enableRichText = false };
            location.AddToClassList("player-station-note"); visual.Add(location);
            var info = DashboardCard(hero, "STATION STATUS");
            info.AddToClassList("player-station-status-card");
            AddInfoTo(info, "State", "Operational");
            AddInfoTo(info, "Owner", station.ownerName ?? "Unknown");
            AddInfoTo(info, "Ownership", station.ownershipType == PlayerStationOwnershipType.Faction ? "Faction" : "Individual");
            AddInfoTo(info, "System", system?.name ?? "Unknown system");
            AddInfoTo(info, "Orbit", orbit?.name ?? "Unknown orbit");
            string role = managementCatalogue == null ? (docked ? "Loading permissions" : "Not docked") :
                managementCatalogue.canOwnership ? "Owner authority" :
                managementCatalogue.canManage ? "Administrator" : "Visitor / delegated access";
            AddInfoTo(info, "Access", role);
            var metrics = new VisualElement(); metrics.AddToClassList("player-station-metric-grid"); body.Add(metrics);
            int facilities = station.addons?.Count(a => a != null && a.level > 0) ?? 0;
            int defenses = NetPlayerStations.OrbitalStructures?.Count(entry => entry != null && entry.orbit == station.stationIndex && entry.defense) ?? 0;
            string integrity = station.stationIntegrityCapacity > 0 ?
                Mathf.Clamp(station.stationIntegrity / station.stationIntegrityCapacity * 100f, 0f, 100f).ToString("0") + "%" : "Unreported";
            DashboardMetric(metrics, "INTEGRITY", integrity);
            DashboardMetric(metrics, "FACILITIES", facilities.ToString());
            DashboardMetric(metrics, "DEFENSES", $"{defenses} / {Modding.ModPlayerInfrastructure.Current.OrbitalDefenseLimit}");
            long escrow = station.buyOrders?.Where(order => order != null && order.remaining > 0).Sum(order => order.reservedCredits) ?? 0L;
            DashboardMetric(metrics, "PROCUREMENT ESCROW", escrow.ToString("N0") + " CR");
            if (docked && (managementCatalogue?.canManageMarket == true || managementCatalogue?.canOwnership == true))
                DashboardMetric(metrics, "STATION BANK", managementCatalogue.stationBankCredits.ToString("N0") + " CR");

            AddSection("ACTIVITY & PROJECTS", "Live construction status and station announcements.");
            bool hasProjects = false;
            if (!string.IsNullOrEmpty(station.moduleConstructionKind))
            {
                hasProjects = true;
                AddInfo("Module construction", ModuleLabel(station.moduleConstructionKind) + (station.moduleBuildCompletesUtc > 0 ?
                    " · " + ConstructionTime(Math.Max(1, station.moduleBuildCompletesUtc - DateTimeOffset.UtcNow.ToUnixTimeSeconds())) : " · Awaiting materials"));
                AddAction("View construction procurement", () => ChangeManagementPage("orders"));
            }
            if (station.dryDockBuilding)
            {
                hasProjects = true;
                AddInfo("Dry dock", station.dryDockPausedUtc > 0 ? "Construction paused — rebuild dry dock" : station.dryDockCompletesUtc > 0 ? ConstructionTime(Math.Max(1, station.dryDockCompletesUtc - DateTimeOffset.UtcNow.ToUnixTimeSeconds())) : "Construction underway");
            }
            if (!hasProjects) AddInfo("Construction", "No active module or dry-dock project");
            var news = NetNews.StationAnnouncements(station.stationIndex);
            if (news.Count > 0)
            {
                AddInfo("Latest station news", news[0]);
                if (docked) AddAction("Read station news", () => ChangeManagementPage("news"));
            }
            if (NetPlayer.Local?.CanViewStationLedgerInUi(station.id) == true)
                AddAction("View construction contribution ledger", "/station contributions " + id);
            if (!docked) AddInfo("Facilities", "Dock at this station to manage or use its facilities.");
            else if (managementCatalogue?.canConstruction == true)
                AddAction("Manage infrastructure", () => ChangeManagementPage("modules"));
        }

        void BuildModuleDashboard(string id)
        {
            var station = selected;
            bool Installed(string module) => station.addons?.Any(a => a != null && a.kind == module && a.level > 0) == true;
            bool Building(string module) => station.moduleConstructionKind == module;
            string Status(string module)
            {
                var addon = station.addons?.Find(a => a != null && a.kind == module);
                if (Installed(module)) return NetPlayerStations.StructuralModuleKind(module)
                    ? $"OPERATIONAL · {NetPlayerStations.ModuleRemainingIntegrity(addon):N0}/{NetPlayerStations.ModuleIntegrityCapacity(module):N0} integrity" : "OPERATIONAL";
                if (!Building(module)) return addon != null && NetPlayerStations.StructuralModuleKind(module) ? "DESTROYED · REBUILD REQUIRED" : "NOT INSTALLED";
                return station.moduleBuildCompletesUtc > 0 ? "BUILDING · " + ConstructionTime(Math.Max(1, station.moduleBuildCompletesUtc - DateTimeOffset.UtcNow.ToUnixTimeSeconds())) : "AWAITING MATERIALS";
            }
            if (moduleDetail == null)
            {
                AddSection("STATION MODULES", "Choose a facility to inspect its status, requirements or management actions.");
                var grid = new VisualElement(); grid.AddToClassList("player-station-module-grid"); body.Add(grid);
                foreach (string module in new[] { "storage", "hangar", "refinery", "factory", "drydock", "dockarm" })
                {
                    string key = module;
                    var card = DashboardCard(grid, ModuleLabel(key).ToUpperInvariant());
                    card.AddToClassList("player-station-module-card");
                    var status = new Label(Status(key));
                    status.AddToClassList("player-station-module-state");
                    status.EnableInClassList("player-station-operational", Installed(key));
                    status.EnableInClassList("player-station-building", Building(key));
                    card.Add(status);
                    var button = new Button(() => { moduleDetail = key; RebuildBody(Vector2.zero); }) { text = Installed(key) ? "Manage facility" : Building(key) ? "View construction" : "Installation options" };
                    button.AddToClassList("player-station-action"); card.Add(button);
                }
                AddAction("Module costs and requirements", "/station modules");
                AddAction("Open 3D orbit placement editor", () => ChangeManagementPage("placement"));
                if (NetPlayer.Local?.CanUseStationStockInUi == true) BuildStationStock();
                return;
            }
            string current = moduleDetail;
            AddAction("Back to all modules", () => { moduleDetail = null; RebuildBody(Vector2.zero); });
            AddSection(ModuleLabel(current), Status(current));
            if (Installed(current))
            {
                if (current == "storage")
                {
                    var storage = managementCatalogue?.storage;
                    if (storage != null && storage.canDeposit) AddInfo("Capacity", $"{storage.used:N0} / {storage.capacity:N0} t");
                    if (menu != null) AddAction("Open hangar storage", () => { Close(); menu.OpenOutfitting(); });
                }
                else if (current == "dockarm") AddAction("Open orbital placement", () => ChangeManagementPage("placement"));
                else if (current == "hangar" || current == "drydock" || managementCatalogue?.canManage == true)
                    AddAction("Open facility controls", () => ChangeManagementPage(current));
                return;
            }
            if (Building(current))
            {
                if (station.moduleBuildCompletesUtc <= 0)
                {
                    AddInfo("Funding", "Donated construction stock is used automatically once all outstanding materials are available.");
                    foreach (var material in station.moduleConstructionMaterials ?? new List<PlayerStationMaterial>())
                        if (material != null) AddInfo(NetGame.Db.Item(material.item)?.name ?? ("Item " + material.item), $"{material.delivered:N0} / {material.required:N0} delivered");
                    if (NetPlayer.Local?.CanUseStationStockInUi == true) BuildStationStock();
                    AddAction("Install remaining materials from cargo", "/station install " + id + " " + current);
                    AddAction("Install remaining materials from personal storage", "/station install-storage " + id + " " + current);
                }
                AddAction("Manage active module procurement", () => ChangeManagementPage("orders"));
                return;
            }
            int duration = Modding.ModPlayerInfrastructure.Current.ModuleBuildSeconds(current);
            if (duration > 0) AddInfo("Build duration", ConstructionTime(duration));
            AddAction("Install from cargo", "/station install " + id + " " + current);
            AddAction("Install from personal storage", "/station install-storage " + id + " " + current);
            if (string.IsNullOrEmpty(station.moduleConstructionKind))
                AddAction("Open construction procurement project", "/station module-project " + id + " " + current);
            else AddInfo("Construction", "Another module project is already active.");
        }

    }
}
