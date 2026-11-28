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
        void BuildOrbitalStructureControls(string id)
        {
            var settings = Modding.ModPlayerInfrastructure.Current;
            string limit = settings.OrbitalDefenseLimit > 0
                ? $"Defense limit per orbit ({settings.OrbitalDefenseLimit}). "
                : "New defense orders are disabled. ";
            AddSection("ORBITAL STRUCTURES", limit + $"Other structure limit per orbit ({settings.OtherOrbitalStructureLimit}). " +
                "Reserve all positions in the orbit editor before delivering materials from Multiplayer → Orbit Projects.");
            if (settings.Enabled)
            {
                var db = NetGame.Db;
                var system = db.Systems.Find(entry => entry != null && entry.index == selected.system);
                var orbits = db.Stations.Where(orbit => orbit != null && system?.stations?.Contains(orbit.index) == true)
                    .Select(orbit => (orbit.index.ToString(), orbit.name + " [" + orbit.index + "]")).ToList();
                var availablePresets = settings.OrbitalStructures.Where(preset =>
                    (preset.Defense ? settings.OrbitalDefenseLimit : settings.OtherOrbitalStructureLimit) > 0 &&
                    NetPlayerStations.OrbitalPresetAvailable(preset, system?.raceId ?? -1)).ToList();
                var presets = availablePresets.Select(preset => (preset.Id, preset.Name + (preset.Defense ? " · Defense" : ""))).ToList();
                AddChoice("structureOrbit", "Build orbit", orbits);
                var cost = new Label();
                cost.AddToClassList("player-station-note");
                void ShowPreset(string presetId)
                {
                    var preset = availablePresets.Find(definition => definition.Id == presetId);
                    cost.text = preset == null ? "" : string.Join(", ", preset.Materials.Select(material =>
                        $"{material.amount:N0} × {db.Item(material.item)?.name ?? material.item.ToString()}"));
                    if (preset?.Defense == true) cost.text += $" · {preset.Range / 1000f:0.##} km range";
                    if (preset?.MaintenanceIntervalSeconds > 0)
                        cost.text += " · maintenance every " + TimeSpan.FromSeconds(preset.MaintenanceIntervalSeconds).ToString("c");
                }
                AddChoice("orbitalPreset", "Structure preset", presets, ShowPreset);
                ShowPreset(Value("orbitalPreset"));
                if (presets.Count > 0) body.Add(cost);
                if (orbits.Count > 0 && presets.Count > 0)
                    AddAction("Order orbital construction", () =>
                        Send("/station orbital-build " + id + " " + Value("structureOrbit") + " " + Value("orbitalPreset")));
            }
            foreach (var plan in selected.placements ?? new List<PlayerStationPlacement>())
            {
                if (plan == null || !NetPlayerStations.TryDefenseSlot(plan.kind, out int slot)) continue;
                if (NetPlayerStations.OrbitalStructures?.Any(p => p != null && p.orbit == selected.stationIndex && p.defense && p.slot == slot) == true) continue;
                AddInfo("Defense " + (slot + 1), "Planned · " + plan.preset);
                if (settings.Enabled) AddAction("Order Defense " + (slot + 1) + " construction", "/station defense-build " + id + " " + (slot + 1));
            }
            if (NetPlayerStations.OrbitalStructures == null) return;
            foreach (var structure in NetPlayerStations.OrbitalStructures)
            {
                if (structure == null || structure.sponsorStationId != selected.id) continue;
                AddInfo($"{structure.name} #{structure.id}", $"Orbit {structure.orbit} · " + (structure.operational ? "Operational" : structure.completesUtc > 0 ? "Building" : "Planned / awaiting materials"));
                if (!structure.operational && structure.completesUtc <= 0)
                    AddAction("Deliver remaining materials from your storage", "/station orbital-finish-storage " + structure.id);
                if (confirmOrbitalRemoval == structure.id)
                {
                    AddAction("Confirm dismantling", () =>
                    {
                        confirmOrbitalRemoval = 0;
                        Send("/station orbital-remove " + structure.id);
                        RebuildBody();
                    });
                    AddAction("Keep structure", () => { confirmOrbitalRemoval = 0; RebuildBody(); });
                }
                else AddAction("Dismantle " + structure.name, () => { confirmOrbitalRemoval = structure.id; RebuildBody(); });
            }
        }

        void BuildFacilityControls(string id)
        {
            switch (managementPage)
            {
                case "refinery":
                    AddSection("REFINERY", "Convert stored input materials using the station's configured recipes.");
                    AddItemChoice("oreItem", "Input material", NetPlayerStations.RefinableOreItems());
                    if (!int.TryParse(Value("oreItem"), out int ore) ||
                        !Modding.ModPlayerInfrastructure.Current.RefineryRecipes.TryGetValue(ore, out var recipe)) break;
                    AddInfo("Output per batch", recipe.OutputUnits + " × " + (NetGame.Db.Item(recipe.Output)?.name ?? "Unavailable item"));
                    AddNumber("oreAmount", "Input units", recipe.InputUnits, recipe.InputUnits, 500, recipe.InputUnits);
                    AddAction("Refine ore", () => Send("/station refine " + id + " " + Value("oreItem") + " " + Value("oreAmount")));
                    break;
                case "factory":
                    AddSection("FACTORY", "Manufacture batches from station storage.");
                    AddCatalogueChoice("recipe", "Recipe", "recipe");
                    AddNumber("batches", "Batches per cycle", 1, 1, 100);
                    if (!string.IsNullOrEmpty(Value("recipe")))
                    {
                        AddAction("Manufacture once", () => Send("/station manufacture " + id + " " + Value("recipe") + " " + Value("batches")));
                        AddChoice("repeatMode", "Repeat mode", new List<(string, string)> { ("continuous", "Continuous"), ("fixed", "Fixed total") });
                        if (Value("repeatMode") == "fixed") AddNumber("repeatTotal", "Total batches", 10, 1, 100000);
                        AddAction("Schedule repeat production", () => Send("/station repeat " + id + " " + Value("recipe") + " " +
                            (Value("repeatMode") == "fixed" ? Value("repeatTotal") : "continuous") + " " + Value("batches")));
                    }
                    AddAction("Factory queue status", "/station factory-status " + id);
                    AddCatalogueChoice("repeatOrder", "Active repeat order", "repeat");
                    if (!string.IsNullOrEmpty(Value("repeatOrder"))) AddAction("Cancel selected order", () => Send("/station repeat-cancel " + id + " " + Value("repeatOrder")));
                    break;
                case "hangar":
                    AddSection("SHIP HANGAR", "Store small and medium bare hulls. Removable equipment and cargo move into station storage.");
                    AddInfo("Hull capacity", NetPlayerStations.ShipHangarCapacity(selected).ToString());
                    AddCatalogueChoice("fleetIndex", "Your personal fleet", "smallFleet");
                    if (!string.IsNullOrEmpty(Value("fleetIndex"))) AddAction("Store selected hull", () => Send("/station store-ship " + id + " " + Value("fleetIndex")));
                    AddCatalogueChoice("hangarIndex", "Your hulls stored here", "hangar");
                    if (!string.IsNullOrEmpty(Value("hangarIndex"))) AddAction("Retrieve selected hull", () => Send("/station retrieve-ship " + id + " " + Value("hangarIndex")));
                    break;
                case "drydock":
                    AddSection("DRY DOCK & SHIPYARD", "Storage and construction for large hulls.");
                    if (menu != null) AddAction("Open dry-dock shipyard", () => { Close(); menu.OpenPlayerDryDock(); });
                    AddCatalogueChoice("dryFleetIndex", "Your large hulls", "largeFleet");
                    if (!string.IsNullOrEmpty(Value("dryFleetIndex"))) AddAction("Store selected large hull", () => Send("/station drydock-store " + id + " " + Value("dryFleetIndex")));
                    AddCatalogueChoice("dryDockIndex", "Your hulls in dry dock", "drydock");
                    if (!string.IsNullOrEmpty(Value("dryDockIndex"))) AddAction("Retrieve selected hull", () => Send("/station drydock-retrieve " + id + " " + Value("dryDockIndex")));
                    AddAction("View construction status", "/station drydock " + id);
                    break;
            }
        }

    }
}
