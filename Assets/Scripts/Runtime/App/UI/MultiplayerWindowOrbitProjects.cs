using System;
using System.Collections.Generic;
using GoF2Remake.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public sealed partial class MultiplayerWindow
    {
        int projectOrbit = -1;
        string projectSnapshot;
        StationProjectActions projectActions;
        int wreckControlProgress;
        Label wreckCountdown, gateCountdown;
        PlayerStationWreckClaim timedClaim;
        long gateCompletesUtc, nextProjectClockUtc;
        int rebuildModel = -1;
        int confirmOrbitalAdminRemoval;
        long nextMaintenanceRefreshUtc = long.MaxValue;
        readonly Dictionary<(int id, bool maintenance), (string item, string quantity)> orbitalDelivery =
            new Dictionary<(int, bool), (string, string)>();
        int reclaimModel, reclaimDestination = -1;
        string wreckMaterial = "", wreckAmount = "1", gateMaterial = "", gateAmount = "1";

        void UpdateOrbitProjects()
        {
            var pilot = NetPlayer.Local;
            int orbit = pilot?.InSpace == true ? pilot.Station : -1;
            var actions = pilot?.AvailableStationActions ?? StationProjectActions.None;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!Busy && (now >= nextMaintenanceRefreshUtc || orbit != projectOrbit || actions != projectActions ||
                (pilot?.WreckControlProgress ?? 0) != wreckControlProgress ||
                !ReferenceEquals(projectSnapshot, NetState.Instance?.PlayerStations)))
                Rebuild();

            if (now < nextProjectClockUtc) return;
            nextProjectClockUtc = now + 1;
            if (wreckCountdown != null)
            {
                long seconds = Math.Max(0, timedClaim.ExclusiveAt - now * 1000 + 999) / 1000;
                wreckCountdown.text = $"Claim held by {timedClaim.claimantName} · exclusive in {seconds / 60}:{seconds % 60:00}";
            }
            if (gateCountdown != null)
                gateCountdown.text = $"{Math.Max(0, gateCompletesUtc - now):N0} seconds remaining";
        }

        void BuildOrbitProjects()
        {
            var pilot = NetPlayer.Local;
            projectSnapshot = NetState.Instance?.PlayerStations;
            projectActions = pilot?.AvailableStationActions ?? StationProjectActions.None;
            wreckControlProgress = pilot?.WreckControlProgress ?? 0;
            wreckCountdown = gateCountdown = null;
            timedClaim = null;
            nextProjectClockUtc = 0;
            nextMaintenanceRefreshUtc = long.MaxValue;
            int orbit = pilot?.InSpace == true ? pilot.Station : -1;
            if (orbit != projectOrbit)
            {
                projectOrbit = orbit;
                orbitalDelivery.Clear();
                confirmOrbitalAdminRemoval = 0;
                rebuildModel = -1;
                reclaimModel = 0;
                reclaimDestination = -1;
                wreckMaterial = gateMaterial = "";
                wreckAmount = gateAmount = "1";
            }

            if (pilot?.InSpace != true) return;
            var station = NetPlayerStations.StationAt(orbit);
            var gate = NetPlayerStations.FindJumpgate(orbit);
            if (station?.wreckClaimOpen == true)
                BuildWreckProject(station, pilot);
            else if (station?.reconstructing == true)
            {
                Section("Reconstructing " + station.name);
                BuildMaterialProgress(station.materials);
                var row = ProjectDeliveryFields(station.materials, wreckMaterial, wreckAmount, value => wreckMaterial = value, value => wreckAmount = value);
                if (row != null)
                {
                    row.Add(Btn("Deliver cargo", () => SendProjectDelivery("contribute", station.id, wreckMaterial, wreckAmount), null));
                    body.Add(row);
                }
                BuildReconstructionOrders(station);
            }

            if (gate?.building == true)
                BuildGateProject(gate, pilot.AvailableStationActions);
            else if (gate?.scrapped == true)
                BuildGateReclaim(gate, pilot.AvailableStationActions);
            BuildOrbitalProjects(orbit);
            var stationSalvage = NetPlayerStations.StationSalvageAt(orbit);
            var gateSalvage = NetPlayerStations.JumpgateSalvageAt(orbit);
            if (stationSalvage?.Count > 0) BuildSalvageReadout("Station salvage", stationSalvage);
            if (gateSalvage?.Count > 0) BuildSalvageReadout("Jumpgate salvage", gateSalvage);
            if (body.childCount == 0) body.Add(Text("No active projects or salvage in this orbit.", 16, Dim));

            if (station?.destroyed == true && !station.retired && NetCommands.IsAdmin(pilot))
                body.Add(Btn("Release destroyed station orbit", () => Send("/station retire " + station.id), null));
        }

        void BuildOrbitalProjects(int orbit)
        {
            if (NetPlayerStations.OrbitalStructures == null) return;
            foreach (var structure in NetPlayerStations.OrbitalStructures)
            {
                if (structure == null || structure.orbit != orbit) continue;
                int id = structure.id;
                Section($"{structure.name} #{id}");
                if (NetCommands.IsAdmin(NetPlayer.Local))
                {
                    if (confirmOrbitalAdminRemoval == id)
                    {
                        body.Add(Btn("Confirm dismantling", () =>
                        {
                            confirmOrbitalAdminRemoval = 0;
                            Send("/station orbital-remove " + id);
                        }, null));
                        body.Add(Btn("Keep structure", () => { confirmOrbitalAdminRemoval = 0; Rebuild(); }, null));
                    }
                    else body.Add(Btn("Dismantle structure", () => { confirmOrbitalAdminRemoval = id; Rebuild(); }, null));
                }
                if (structure.operational)
                {
                    string integrity = structure.integrity.ToString(structure.integrity < 1f ? "N2" : "N0");
                    body.Add(Text($"Integrity {integrity}/{structure.maxIntegrity:N0}", 16, Dim));
                    if (structure.defense) body.Add(Text($"Defense range {structure.range / 1000f:0.##} km", 16, Dim));
                    if (structure.maintenanceDueUtc > 0)
                    {
                        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        if (structure.MaintenanceDue(now))
                        {
                            string status = structure.CanLoseIntegrityToMaintenance
                                ? "Offline · maintenance due · integrity slowly decaying"
                                : "Offline · maintenance due";
                            body.Add(Text(status, 16, Dim));
                            BuildOrbitalDelivery(structure, true);
                        }
                        else
                        {
                            nextMaintenanceRefreshUtc = Math.Min(nextMaintenanceRefreshUtc, structure.maintenanceDueUtc);
                            body.Add(Text($"Next maintenance {DateTimeOffset.FromUnixTimeSeconds(structure.maintenanceDueUtc):u}", 16, Dim));
                        }
                    }
                    continue;
                }
                if (structure.completesUtc > 0)
                {
                    body.Add(Text("Materials funded. Construction is underway.", 16, Dim));
                    continue;
                }
                BuildOrbitalDelivery(structure, false);
            }
        }

        void BuildOrbitalDelivery(PlayerOrbitalStructure structure, bool maintenance)
        {
            var materials = maintenance ? structure.maintenanceMaterials : structure.materials;
            if (materials == null) return;
            BuildMaterialProgress(materials);
            var key = (structure.id, maintenance);
            var next = materials.Find(material => material != null && material.delivered < material.required);
            if (!orbitalDelivery.TryGetValue(key, out var draft))
                orbitalDelivery[key] = draft = (next?.item.ToString() ?? "", "1");
            var row = ProjectDeliveryFields(materials, draft.item, draft.quantity,
                value => orbitalDelivery[key] = (value, orbitalDelivery[key].quantity),
                value => orbitalDelivery[key] = (orbitalDelivery[key].item, value));
            if (row == null) return;
            row.Add(Btn(maintenance ? "Deliver maintenance cargo" : "Deliver cargo", () => SendProjectDelivery(
                maintenance ? "orbital-maintain" : "orbital-deliver", structure.id, orbitalDelivery[key].item, orbitalDelivery[key].quantity), null));
            row.Add(Btn("Use sponsor stock", () => SendProjectDelivery(
                maintenance ? "orbital-maintain-stock" : "orbital-fund", structure.id, orbitalDelivery[key].item, orbitalDelivery[key].quantity), null));
            body.Add(row);
        }

        void BuildReconstructionOrders(PlayerStationRecord station)
        {
            if (station.buyOrders == null || station.buyOrders.Count == 0) return;
            Section("Reconstruction buy orders");
            for (int i = 0; i < station.buyOrders.Count; i++)
            {
                int orderIndex = i;
                var order = station.buyOrders[i];
                if (order == null || order.remaining <= 0) continue;
                string name = NetGame.Db?.Item(order.item)?.name ?? ("Item " + order.item);
                var row = Row();
                row.Add(Text($"{name} · {order.remaining:N0} at {order.unitPrice:N0} cr", 16, Dim));
                row.Add(Btn("Sell entered quantity", () => SendProjectDelivery("sell-order", station.id,
                    orderIndex.ToString(), wreckAmount), null));
                body.Add(row);
            }
        }

        void BuildWreckProject(PlayerStationRecord station, NetPlayer pilot)
        {
            Section(station.name + " wreck");
            if (pilot.WreckControlContested)
                body.Add(Text("Control contested within 5 km. Takeover progress is paused.", 16, Dim));
            int earned = pilot.WreckControlEarnedSeconds;
            if (earned >= 0)
                body.Add(Text($"Your side's takeover · {earned / 60}:{earned % 60:00} / 5:00" +
                    (pilot.WreckControlAdvancing ? "" : " · paused"), 16, Dim));

            var claim = station.wreckClaim;
            if (claim == null || claim.claimedUtcMilliseconds <= 0)
            {
                body.Add(Text("Unclaimed. Fly within 5 km to claim uncontested.", 16, Dim));
                return;
            }

            if (claim.exclusive)
                body.Add(Text("Exclusive claim held by " + claim.claimantName, 16, Dim));
            else
            {
                timedClaim = claim;
                wreckCountdown = Text("", 16, Dim);
                body.Add(wreckCountdown);
            }
            if (!pilot.AvailableStationActions.HasFlag(StationProjectActions.RebuildWreck)) return;

            var labels = new List<string> { "Use the wreck's model" };
            var indices = new List<int> { -1 };
            var stations = NetGame.Db?.Stations;
            if (stations != null)
                foreach (var model in stations)
                {
                    if (model == null || !NetPlayerStations.CanUseRebuildAppearance(station, model.index)) continue;
                    labels.Add(model.name + " [" + model.index + "]");
                    indices.Add(model.index);
                }
            var selector = new DropdownField("Station model", labels, Math.Max(0, indices.IndexOf(rebuildModel)));
            selector.RegisterValueChangedCallback(_ =>
            {
                if (selector.index >= 0) rebuildModel = indices[selector.index];
            });
            body.Add(selector);
            body.Add(Btn("Start reconstruction", () =>
                Send("/station rebuild " + station.id + (indices.Contains(rebuildModel) && rebuildModel >= 0 ? " " + rebuildModel : "")),
                "squad-button--accept"));
        }

        void BuildGateReclaim(PlayerJumpgateRecord gate, StationProjectActions actions)
        {
            Section("Scrapped jumpgate");
            body.Add(Text($"Scrap integrity · {NetPlayerStations.JumpgateScrapIntegrity(gate):N0}", 16, Dim));
            if (NetPlayerStations.JumpgateConstructionPausedAt(gate) > 0)
            {
                body.Add(Text("Reclamation is paused until the sponsoring station is claimed.", 16, Dim));
                return;
            }
            bool faction = actions.HasFlag(StationProjectActions.ClaimGateForFaction);
            bool individual = actions.HasFlag(StationProjectActions.ClaimGateIndividually);
            if (!faction && !individual)
            {
                body.Add(Text("Reclamation requires a faction membership or an operational station you own.", 16, Dim));
                return;
            }

            var models = new List<(int id, string label)> { (0, "Keep the existing gate model") };
            models.AddRange(NetPlayerStations.JumpgateModelChoices());
            var destinations = new List<(int id, string label)> { (-1, "Keep the current destination") };
            foreach (var system in NetGame.Db.Systems)
                if (system != null && system.index != gate.system && system.stations?.Count > 0)
                    destinations.Add((system.index, system.name + " [" + system.index + "]"));

            AddReclaimChoice("Jumpgate model", models, reclaimModel, value => reclaimModel = value);
            AddReclaimChoice("Destination system", destinations, reclaimDestination, value => reclaimDestination = value);
            if (faction) AddGateReclaimAction(gate, "faction", "Reclaim for my faction");
            if (individual) AddGateReclaimAction(gate, "individual", "Reclaim for myself");
        }

        void AddReclaimChoice(string label, List<(int id, string label)> choices, int current, Action<int> apply)
        {
            var labels = choices.ConvertAll(option => option.label);
            int index = Math.Max(0, choices.FindIndex(option => option.id == current));
            apply(choices[index].id);
            var choice = new DropdownField(label, labels, index);
            choice.RegisterValueChangedCallback(_ =>
            {
                if (choice.index >= 0) apply(choices[choice.index].id);
            });
            body.Add(choice);
        }

        void AddGateReclaimAction(PlayerJumpgateRecord gate, string ownership, string label) =>
            body.Add(Btn(label, () => Send($"/station jumpgate-claim {gate.orbitStationIndex} {ownership} {reclaimDestination} {reclaimModel}"),
                "squad-button--accept"));

        void BuildGateProject(PlayerJumpgateRecord gate, StationProjectActions actions)
        {
            Section("Jumpgate construction");
            body.Add(Text(NetPlayerStations.JumpgateModelLabel(gate), 16, Dim));
            BuildMaterialProgress(gate.materials);
            long pausedAt = NetPlayerStations.JumpgateConstructionPausedAt(gate);
            if (pausedAt > 0)
            {
                body.Add(Text("Paused until the sponsoring station is claimed.", 16, Dim));
                return;
            }
            if (gate.startedUtc > 0)
            {
                gateCompletesUtc = gate.completesUtc;
                gateCountdown = Text("", 16, Dim);
                body.Add(gateCountdown);
                return;
            }
            var firstOutstanding = gate.materials?.Find(material => material != null && material.required > material.delivered);
            if (string.IsNullOrEmpty(gateMaterial) && firstOutstanding != null) gateMaterial = firstOutstanding.item.ToString();
            var row = ProjectDeliveryFields(gate.materials, gateMaterial, gateAmount, value => gateMaterial = value, value => gateAmount = value);
            if (row == null) return;
            row.Add(Btn("Deliver cargo", () => SendProjectDelivery("jumpgate-contribute", gate.orbitStationIndex, gateMaterial, gateAmount), null));
            if (actions.HasFlag(StationProjectActions.FundJumpgate))
                row.Add(Btn("Use donated stock", () => SendProjectDelivery("jumpgate-fund-stock", gate.orbitStationIndex, gateMaterial, gateAmount), null));
            body.Add(row);
        }

        void BuildSalvageReadout(string title, IReadOnlyList<PlayerStationMaterial> materials)
        {
            Section(title);
            body.Add(Text("Use your tractor beam when in reach.", 16, Dim));
            foreach (var material in materials)
                if (material != null && material.required > 0)
                    body.Add(Text($"{NetGame.Db?.Item(material.item)?.name ?? ("Item " + material.item)} · {material.required:N0}", 16, Dim));
        }

        void BuildMaterialProgress(List<PlayerStationMaterial> materials)
        {
            if (materials == null) return;
            foreach (var material in materials)
            {
                if (material == null) continue;
                string name = NetGame.Db?.Item(material.item)?.name ?? "Item";
                body.Add(Text($"{name} [{material.item}] · {material.delivered:N0}/{material.required:N0}", 16, Dim));
            }
        }

        VisualElement ProjectDeliveryFields(List<PlayerStationMaterial> materials, string material, string amount, Action<string> updateMaterial, Action<string> updateAmount)
        {
            var outstanding = materials?.FindAll(entry => entry != null && entry.delivered < entry.required);
            if (outstanding == null || outstanding.Count == 0) return null;
            var labels = outstanding.ConvertAll(entry => (NetGame.Db?.Item(entry.item)?.name ?? ("Item " + entry.item)) +
                " [" + entry.item + "] · " + (entry.required - entry.delivered).ToString("N0") + " needed");
            int index = Math.Max(0, outstanding.FindIndex(entry => entry.item.ToString() == material));
            updateMaterial(outstanding[index].item.ToString());
            var row = Row();
            var picker = new DropdownField(labels, index);
            picker.style.minWidth = 180;
            picker.style.flexGrow = 1;
            picker.RegisterValueChangedCallback(_ =>
            {
                if (picker.index >= 0 && picker.index < outstanding.Count)
                    updateMaterial(outstanding[picker.index].item.ToString());
            });
            row.Add(picker);
            row.Add(Field("Units", amount, 3, 110, updateAmount, TouchScreenKeyboardType.NumberPad));
            return row;
        }

        void SendProjectDelivery(string command, int project, string itemText, string amountText)
        {
            if (!int.TryParse(itemText, out int item) || item < 0 || !int.TryParse(amountText, out int amount) ||
                amount < 1 || amount > 500)
            {
                status.text = "Enter a valid item ID and 1–500 units.";
                return;
            }
            Send($"/station {command} {project} {item} {amount}");
        }
    }
}
