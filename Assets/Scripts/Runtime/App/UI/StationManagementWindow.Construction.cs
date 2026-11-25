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
        void BuildConstructionProcurement(string id, bool allowManagement, bool canManageModule = false)
        {
            string module = selected.moduleConstructionKind;
            long deadline = selected.underConstruction ? selected.stationBuildCompletesUtc : selected.moduleBuildCompletesUtc;
            if (deadline > 0)
            {
                AddInfo("Build time remaining", ConstructionTime(Math.Max(1, deadline - DateTimeOffset.UtcNow.ToUnixTimeSeconds())));
                return;
            }
            if (!selected.underConstruction && string.IsNullOrEmpty(module)) return;
            AddSection("CONSTRUCTION PROCUREMENT", "Suppliers are paid from escrow.");
            if (!string.IsNullOrEmpty(module))
            {
                AddInfo("Module project", ModuleLabel(module));
                if (selected.moduleConstructionMaterials != null)
                    foreach (var material in selected.moduleConstructionMaterials)
                        if (material != null)
                            AddInfo(NetGame.Db?.Item(material.item)?.name ?? ("Item " + material.item),
                                $"{material.delivered:N0}/{material.required:N0}");
            }
            var activeOrders = selected.buyOrders ?? new List<PlayerStationBuyOrder>();
            if (canManageModule && !string.IsNullOrEmpty(module) && activeOrders.Count == 0 &&
                selected.moduleConstructionMaterials?.Any(material => material != null && material.delivered > 0) != true)
                AddAction("Cancel module project", "/station module-project " + id + " cancel");
            if (activeOrders.Count == 0)
                AddInfo("Active buy orders", allowManagement
                    ? "None. Create an order below to invite other pilots to supply materials."
                    : "No active public construction orders.");
            else
            {
                AddInfo("Active buy orders", activeOrders.Count.ToString());
                for (int i = 0; i < activeOrders.Count; i++)
                {
                    var order = activeOrders[i];
                    if (order == null || order.remaining <= 0) continue;
                    string itemName = NetGame.Db?.Item(order.item)?.name ?? ("Item " + order.item);
                    AddInfo("BUY ORDER #" + order.id, itemName + "  ·  " + order.remaining.ToString("N0") +
                        " remaining at " + order.unitPrice.ToString("N0") + " cr each");
                    AddInfo("Escrow", order.reservedCredits.ToString("N0") + " credits reserved");
                    int orderIndex = order.id;
                    if (orderIndex <= 0) continue;
                    if (selected.underConstruction ? LocalDeliveryProject()?.id == selected.id :
                        !inFlight && CurrentOrbit == selected.stationIndex)
                    {
                        if (selected.underConstruction) AddField("procureSellAmount" + orderIndex, "Units to sell into order #" + orderIndex, "1");
                        else AddNumber("procureSellAmount" + orderIndex, "Units to sell", 1, 1, Math.Min(500, order.remaining));
                        AddAction("Sell " + itemName + " to order #" + orderIndex, () =>
                            Send("/station sell-order-id " + id + " " + orderIndex + " " + Value("procureSellAmount" + orderIndex)));
                    }
                    if (allowManagement)
                        AddAction("Cancel order #" + orderIndex + " and refund unused escrow", () =>
                            Send("/station cancel-order-id " + id + " " + orderIndex));
                }
            }
            if (selected.underConstruction && LocalDeliveryProject()?.id != selected.id)
                AddInfo("Delivery", "Dock at this construction site to fulfil orders.");
            if (!allowManagement) return;
            AddInfo("Procurement funding", selected.ownershipType == PlayerStationOwnershipType.Faction
                ? "Faction treasury. Orders require leader/officer authority." : "Station owner's personal credits.");
            var requirements = selected.underConstruction ? selected.materials : selected.moduleConstructionMaterials;
            var outstanding = new HashSet<int>(requirements?.Where(material => material != null && material.required > material.delivered)
                .Select(material => material.item) ?? Enumerable.Empty<int>());
            if (outstanding.Count == 0) { AddInfo("Materials", "No outstanding requirements."); return; }
            var materialChoices = new List<(string value, string label)> { ("", "Select a required material") };
            materialChoices.AddRange(requirements.Where(material => material != null &&
                outstanding.Contains(material.item) && NetGame.Db?.Item(material.item) != null)
                .OrderBy(material => NetGame.Db.Item(material.item).name, StringComparer.CurrentCultureIgnoreCase)
                .Select(material => (material.item.ToString(), NetGame.Db.Item(material.item).name +
                    " · " + (material.required - material.delivered).ToString("N0") + " needed")));
            AddChoice("buyOrderItem", "Required material", materialChoices, items: true);
            if (string.IsNullOrEmpty(Value("buyOrderItem"))) return;
            if (selected.underConstruction)
            {
                AddField("buyOrderAmount", "Units to purchase (1–500)", "10");
                AddField("buyOrderPrice", "Offer per unit (credits)", "100");
            }
            else
            {
                AddNumber("buyOrderAmount", "Units to purchase", 10, 1, 500);
                AddNumber("buyOrderPrice", "Offer per unit (credits)", 100, 1, 1000000000);
            }
            AddAction("Create construction buy order", () => Send("/station buy-order " + id + " " +
                Value("buyOrderItem") + " " + Value("buyOrderAmount") + " " + Value("buyOrderPrice")));
        }

        void BuildNewStationControls()
        {
            AddSection("BUILD A STATION", "");
            var db = NetGame.Db;
            if (db?.Systems == null) return;
            if (plannerOrbits == null)
            {
                AddInfo("Build location", plannerRequestPending ? "Waiting for server-approved construction locations…" : "Construction locations are unavailable.");
                return;
            }
            bool Available(int orbit) => plannerOrbits?.Contains(orbit) == true && NetPlayerStations.CanBuildStationInOrbit(orbit);
            var systems = db.Systems.Where(system => system != null && system.stations?.Any(Available) == true)
                .OrderBy(system => system.name).Select(system => (system.index.ToString(), system.name)).ToList();
            if (systems.Count == 0) AddInfo("Build location", "No revealed system has a vacant orbit available for construction.");
            else
            {
                var choices = new List<(string value, string label)> { ("", "Select a system") };
                choices.AddRange(systems);
                AddChoice("system", "Revealed build system", choices, _ => drafts.Remove("buildOrbit"));
                var orbitChoices = new List<(string value, string label)> { ("", "Select a vacant orbit") };
                if (int.TryParse(Value("system"), out int chosenSystem))
                    orbitChoices.AddRange(db.Stations.Where(station => station != null && station.system == chosenSystem && Available(station.index))
                        .OrderBy(station => station.name).Select(station => (station.index.ToString(), station.name)));
                AddChoice("buildOrbit", "Vacant orbit", orbitChoices);
                AddAction("Choose system and orbit on star map", () =>
                {
                    if (StarMap.IsOpen) return;
                    var allowed = new HashSet<int>(systems.Select(system => int.Parse(system.Item1)));
                    if (stationRoot.focusController?.focusedElement is VisualElement focused) focused.Blur();
                    stationRoot.AddToClassList("station-map-open");
                    var map = StarMap.OpenStationPlanner(db, allowed.Contains, Available, result =>
                    {
                        plannerMap = null;
                        stationRoot.RemoveFromClassList("station-map-open");
                        if (!open) return;
                        var picked = db.Stations.Find(station => station != null && station.index == result.station);
                        if (picked != null && allowed.Contains(picked.system) && Available(picked.index))
                        { drafts["system"] = picked.system.ToString(); drafts["buildOrbit"] = picked.index.ToString(); }
                        RebuildBody();
                        panel.schedule.Execute(() =>
                        {
                            if (open && !StarMap.IsOpen && InputMode.Current != InputKind.Touch && !InputMode.MousePointing)
                                body.Query<Button>().ToList().Find(button => button.text == "Choose system and orbit on star map")?.Focus();
                        });
                    });
                    plannerMap = map;
                    if (map == null)
                    {
                        stationRoot.RemoveFromClassList("station-map-open");
                        feedback.text = "The star map could not be opened.";
                        feedbackTime = 8f;
                    }
                    else map.SetHelp("Select a revealed system and a vacant orbit. You can reserve the site before travelling there.");
                });
                AddInfo("Orbit placement", "The selected planet keeps its orbit. Construction reserves its vacant station site.");
            }
            AddInfo("Eligible build systems", $"{systems.Count}. Revealed systems with a vacant orbit. You can reserve a site before travelling there.");
            AddStationModelPicker("newStationModel");
            AddField("newStationName", "Station name", "");
            if (systems.Count > 0 && !string.IsNullOrEmpty(Value("newStationModel")))
                AddAction("Start station project", () =>
                {
                    if (!int.TryParse(Value("system"), out int systemId) || !systems.Any(system => system.Item1 == systemId.ToString()))
                    { feedback.text = "Select a discovered build system first."; feedbackTime = 8f; return; }
                    if (!int.TryParse(Value("buildOrbit"), out int orbit) || !Available(orbit) ||
                        db.Stations.Find(station => station != null && station.index == orbit)?.system != systemId)
                    { feedback.text = "Select a vacant orbit first."; feedbackTime = 8f; return; }
                    SendStationName("start-orbit " + systemId + " " + orbit + " " + Value("newStationModel"), Value("newStationName"));
                });
        }

        static string ConstructionTime(long seconds) =>
            seconds <= 0 ? "Immediate" : TimeSpan.FromSeconds(seconds).ToString("c");

        void BuildStationConstruction(PlayerStationRecord project)
        {
            string id = project.id.ToString();
            AddSection("SELECTED STATION PROJECT", "Deliver materials directly to this construction project.");
            AddInfo("Project", project.name);
            if (project.stationBuildCompletesUtc > 0)
                AddInfo("Build time remaining", ConstructionTime(Math.Max(1, project.stationBuildCompletesUtc - DateTimeOffset.UtcNow.ToUnixTimeSeconds())));
            else AddInfo("Build time after funding", ConstructionTime(Modding.ModPlayerInfrastructure.Current.StationBuildSeconds));
            if (project.materials != null)
                foreach (var material in project.materials)
                    if (material != null)
                        AddInfo((NetGame.Db.Item(material.item)?.name ?? "Unavailable material"),
                            material.delivered + "/" + material.required);
            AddStationLocation(project);
            var remaining = project.materials?.Where(material => material != null && material.required > material.delivered &&
                NetGame.Db?.Item(material.item) != null)
                .OrderBy(material => NetGame.Db.Item(material.item).name, StringComparer.CurrentCultureIgnoreCase)
                .Select(material => (material.item.ToString(), NetGame.Db.Item(material.item).name +
                    " · " + (material.required - material.delivered).ToString("N0") + " needed")).ToList()
                ?? new List<(string, string)>();
            if (remaining.Count > 0)
            {
                AddChoice("contributeItem", "Required material", remaining);
                AddField("contributeAmount", "Units", "10");
                AddAction("Deliver construction cargo", () =>
                    Send("/station contribute " + id + " " + Value("contributeItem") + " " + Value("contributeAmount")));
            }
            if (NetPlayer.Local?.CanViewStationLedgerInUi(project.id) == true)
                AddAction("View contribution ledger", "/station contributions " + id);
        }

        void BuildStationStock()
        {
            AddSection("DONATED CONSTRUCTION STOCK", "");
            AddAction("View construction stock", "/station faction-materials");
            var required = selected.underConstruction ? selected.materials : selected.moduleConstructionMaterials;
            var allowed = new HashSet<int>(required?.Where(entry => entry != null && entry.required > entry.delivered)
                .Select(entry => entry.item) ?? Enumerable.Empty<int>());
            if (allowed.Count == 0) { AddInfo("Materials", "No outstanding materials for the current station or module build."); return; }
            AddItemChoice("stockItem", "Required construction material", allowed);
            if (!int.TryParse(Value("stockItem"), out int item) || !allowed.Contains(item)) return;
            int remaining = required.Where(entry => entry != null && entry.item == item).Sum(entry => Math.Max(0, entry.required - entry.delivered));
            AddNumber("stockQuantity", "Units", 1, 1, Math.Min(500, remaining));
            AddAction("Donate from cargo", () => Send("/station faction-donate " + Value("stockItem") + " " + Value("stockQuantity")));
        }

    }
}
