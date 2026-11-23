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
        int rebuildModel = -1;
        string wreckMaterial = "", wreckAmount = "1", gateMaterial = "", gateAmount = "1";

        void BuildOrbitProjects()
        {
            var pilot = NetPlayer.Local;
            if (pilot == null || !pilot.InSpace) return;
            int orbit = pilot.Station;
            if (orbit != projectOrbit)
            {
                projectOrbit = orbit;
                rebuildModel = -1;
                wreckMaterial = gateMaterial = "";
                wreckAmount = gateAmount = "1";
            }

            var station = NetPlayerStations.StationAt(orbit);
            var gate = NetPlayerStations.FindJumpgate(orbit);
            if (station?.wreckClaimOpen == true)
                BuildWreckProject(station, pilot.AvailableStationActions);
            else if (station?.reconstructing == true)
            {
                Section("Reconstructing " + station.name);
                BuildMaterialProgress(station.materials);
                var row = ProjectDeliveryFields(wreckMaterial, wreckAmount, value => wreckMaterial = value, value => wreckAmount = value);
                row.Add(Btn("Deliver cargo", () => SendProjectDelivery("contribute", station.id, wreckMaterial, wreckAmount), null));
                body.Add(row);
                BuildReconstructionOrders(station);
            }

            if (gate?.building == true)
                BuildGateProject(gate, pilot.AvailableStationActions);
            else if (station?.wreckClaimOpen != true && station?.reconstructing != true)
                body.Add(Text("No active construction projects in this orbit.", 16, Dim));
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

        void BuildWreckProject(PlayerStationRecord station, StationProjectActions actions)
        {
            Section(station.name + " wreck");
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
                long seconds = Math.Max(0, claim.ExclusiveAt - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 999) / 1000;
                body.Add(Text($"Claim held by {claim.claimantName} · exclusive in {seconds / 60}:{seconds % 60:00}", 16, Dim));
            }
            if (!actions.HasFlag(StationProjectActions.RebuildWreck)) return;

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

        void BuildGateProject(PlayerJumpgateRecord gate, StationProjectActions actions)
        {
            Section("Jumpgate construction");
            BuildMaterialProgress(gate.materials);
            long pausedAt = NetPlayerStations.JumpgateConstructionPausedAt(gate);
            if (pausedAt > 0)
            {
                body.Add(Text("Paused until the sponsoring station is claimed.", 16, Dim));
                return;
            }
            if (gate.startedUtc > 0)
            {
                long seconds = Math.Max(0, gate.completesUtc - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                body.Add(Text($"{seconds:N0} seconds remaining", 16, Dim));
                return;
            }
            var row = ProjectDeliveryFields(gateMaterial, gateAmount, value => gateMaterial = value, value => gateAmount = value);
            row.Add(Btn("Deliver cargo", () => SendProjectDelivery("jumpgate-contribute", gate.orbitStationIndex, gateMaterial, gateAmount), null));
            if (actions.HasFlag(StationProjectActions.FundJumpgate))
                row.Add(Btn("Use donated stock", () => SendProjectDelivery("jumpgate-fund-stock", gate.orbitStationIndex, gateMaterial, gateAmount), null));
            body.Add(row);
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

        VisualElement ProjectDeliveryFields(string material, string amount, Action<string> updateMaterial, Action<string> updateAmount)
        {
            var row = Row();
            row.Add(Field("Item ID", material, 10, 130, updateMaterial, TouchScreenKeyboardType.NumberPad));
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
