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
        void OpenGateMap(string key, Func<int, bool> systemAllowed, Func<int, bool> orbitAllowed, bool systemOnly)
        {
            if (StarMap.IsOpen) return;
            var db = NetGame.Db;
            if (stationRoot.focusController?.focusedElement is VisualElement focused) focused.Blur();
            stationRoot.AddToClassList("station-map-open");
            plannerMap = StarMap.OpenStationPlanner(db, systemAllowed, orbitAllowed, result =>
            {
                plannerMap = null;
                stationRoot.RemoveFromClassList("station-map-open");
                if (!open) return;
                var picked = db.Stations.Find(entry => entry != null && entry.index == result.station);
                if (picked != null && systemAllowed(picked.system) && orbitAllowed(picked.index))
                    drafts[key] = (systemOnly ? picked.system : picked.index).ToString();
                RebuildBody();
            }, purpose: "jumpgate", systemOnly: systemOnly);
            if (plannerMap == null)
            {
                stationRoot.RemoveFromClassList("station-map-open");
                feedback.text = "The starmap could not be opened.";
                feedbackTime = 4f;
            }
            else plannerMap.SetHelp(systemOnly ? "Select a destination system, then select it again to confirm." : "Select the origin system and an available jumpgate orbit.");
        }

        void BuildNewJumpgateControls(string id)
        {
            AddSection("BUILD A JUMPGATE", "The build timer starts once all materials are delivered.");
            AddAction("View jumpgate construction costs", "/station jumpgate-cost");
            if (selected.underConstruction)
                AddInfo("Jumpgate availability", "The sponsoring station must finish construction first.");
            else
            {
                var db = NetGame.Db;
                var orbits = db.Stations.Where(orbit => orbit != null && NetPlayerStations.CanBuildGateInOrbit(selected.system, orbit.index))
                    .Select(orbit => (orbit.index.ToString(), orbit.name + " [" + orbit.index + "]")).ToList();
                var destinations = db.Systems.Where(system => system != null && system.index != selected.system && system.stations?.Count > 0)
                    .OrderBy(system => system.name).Select(system => (system.index.ToString(), system.name)).ToList();
                if (orbits.Count == 0) AddInfo("Orbit", "No empty gate orbits in this system.");
                AddChoice("jumpgateOrbit", "Build orbit", orbits);
                if (orbits.Count > 0) AddAction("Choose gate orbit on starmap", () => OpenGateMap("jumpgateOrbit", sys => sys == selected.system, orbit => NetPlayerStations.CanBuildGateInOrbit(selected.system, orbit), false));
                AddChoice("jumpgateDestination", "Destination system", destinations);
                if (destinations.Count > 0) AddAction("Choose destination on starmap", () => OpenGateMap("jumpgateDestination", sys => sys != selected.system && db.Systems.Any(entry => entry != null && entry.index == sys && entry.stations?.Count > 0), orbit => true, true));
                AddChoice("jumpgateModel", "Jumpgate model", GateModelChoices());
                if(int.TryParse(Value("jumpgateModel"),out int gatePreviewModel)) AddConstructionRotationPreview("newGateRotation",NetPlayerStations.JumpgatePlacementAssembly(gatePreviewModel));
                AddInfo("Build time after funding", ConstructionTime(Modding.ModPlayerInfrastructure.Current.JumpgateBuildSeconds));
                if (orbits.Count > 0 && destinations.Count > 0 && !string.IsNullOrEmpty(Value("jumpgateModel")))
                AddAction("Start jumpgate construction", () =>
                {
                    string orbit = Value("jumpgateOrbit").Trim();
                    string destination = Value("jumpgateDestination").Trim();
                    if (!int.TryParse(orbit, out _) || !int.TryParse(destination, out _) ||
                        !int.TryParse(Value("jumpgateModel"), out int model))
                    {
                        feedback.text = "Choose an orbit, destination and model.";
                        feedbackTime = 4f;
                        return;
                    }
                    Send("/station jumpgate " + id + " " + orbit + " " + destination + " " + model + " " + ConstructionRotationArgs("newGateRotation"));
                });
            }

        }

        void BuildSystemGates(int system, bool securityOnly)
        {
            var systemGates = NetPlayerStations.Jumpgates?.Where(g => g != null && g.system == system &&
                    NetPlayerStations.CanViewJumpgateManagement(NetPlayer.Local, g))
                .OrderBy(g => g.orbitStationIndex).ToList();
            if (systemGates == null || systemGates.Count == 0) { AddInfo("Jumpgates", "No managed gates in this system."); return; }
            AddSection(securityOnly ? "JUMPGATE SECURITY & ACCESS" : "JUMPGATE NETWORK", securityOnly ? "Select a gate to manage its access policies." : "Select a gate to inspect construction, integrity and destination.");
            AddChoice(securityOnly ? "securityGate" : "infrastructureGate", "Jumpgate", systemGates.Select(g => (g.orbitStationIndex.ToString(), NetGame.Db.Stations.Find(x => x != null && x.index == g.orbitStationIndex)?.name ?? ("Orbit " + g.orbitStationIndex))).ToList());
            foreach (var gate in systemGates.Where(g => g.orbitStationIndex.ToString() == Value(securityOnly ? "securityGate" : "infrastructureGate")))
            {
                string orbitId = gate.orbitStationIndex.ToString();
                string orbitName = NetGame.Db?.Stations.Find(x => x != null && x.index == gate.orbitStationIndex)?.name
                    ?? ("Orbit " + orbitId);
                string destinationName = NetGame.Db?.Systems.Find(x => x != null && x.index == gate.destinationSystem)?.name
                    ?? ("System " + gate.destinationSystem);
                AddInfo("Gate · " + orbitName + " (" + orbitId + ")", "Destination: " + destinationName);
                AddAction("Refresh gate status · orbit " + orbitId, "/station jumpgate-status " + orbitId);
                long pausedAt = NetPlayerStations.JumpgateConstructionPausedAt(gate);
                if (pausedAt > 0)
                {
                    AddInfo("Gate management", "Paused until the sponsoring station is claimed.");
                    if (gate.building && gate.startedUtc > 0)
                        AddInfo("Build time left", Math.Max(0, gate.completesUtc - pausedAt).ToString("N0") + " seconds");
                    continue;
                }
                if (securityOnly && !gate.scrapped && !gate.permanentlyDestroyed)
                {
                    string modeKey = "gateAccess_" + orbitId;
                    string typeKey = "gateBlockType_" + orbitId;
                    string targetKey = "gateBlockTarget_" + orbitId;
                    string access = gate.accessMode == PlayerJumpgateAccessMode.OwningFaction ? "faction"
                        : gate.accessMode == PlayerJumpgateAccessMode.Blacklist ? "blacklist"
                        : gate.accessMode == PlayerJumpgateAccessMode.OwnerOnly ? "owner" : "everyone";
                    if (!drafts.ContainsKey(modeKey)) drafts[modeKey] = access;
                    AddChoice(modeKey, "Access policy", new List<(string, string)> { ("everyone", "Everyone"), ("faction", "Owning faction"), ("blacklist", "Blacklist"), ("owner", "Owner only") });
                    AddAction("Set gate access · orbit " + orbitId, () =>
                        Send("/station jumpgate-access " + orbitId + " " + Value(modeKey)));
                    if (Modding.ModAdvancedEconomy.RestrictedGateRouting || gate.npcEconomyAccess)
                        AddAction(gate.npcEconomyAccess ? "Revoke explicit NPC commerce permission" : "Authorize NPC commerce through restricted gate", () =>
                            Send("/station jumpgate-npc " + orbitId + (gate.npcEconomyAccess ? " off" : " on")));
                    AddChoice(typeKey, "Blacklist entry type", new List<(string, string)> { ("player", "Pilot"), ("faction", "Faction") }, _ => drafts.Remove(targetKey));
                    AddAccountField(targetKey, "Blacklist target");
                    string reasonKey = "gateBlockReason_" + orbitId;
                    AddField(reasonKey, "Reason shown to denied pilots", "");
                    AddAction("Add / update gate blacklist", () =>
                        SendWithReason($"/station jumpgate-blacklist {orbitId} add {Value(typeKey)} {Value(targetKey)}", reasonKey, " | "));
                    AddAction("Remove from gate blacklist", () =>
                        Send("/station jumpgate-blacklist " + orbitId + " remove " + Value(typeKey) + " " + Value(targetKey)));
                    if (Modding.ModPlayerInfrastructure.Current.JumpgateRacePoliciesEnabled)
                    {
                        string raceKey = "gateRace_" + orbitId;
                        string raceStateKey = "gateRaceState_" + orbitId;
                        AddRaceChoice(raceKey, "Race");
                        AddChoice(raceStateKey, "Race access", new List<(string, string)>
                        {
                            ("inherit", "Inherit faction policy"),
                            ("allow", "Allow under race policy"),
                            ("deny", "Deny under race policy")
                        });
                        string raceReasonKey = "gateRaceReason_" + orbitId;
                        if (Value(raceStateKey) == "deny") AddField(raceReasonKey, "Race denial reason", "");
                        AddAction("Apply gate race rule", () =>
                        {
                            string command = $"/station jumpgate-race {orbitId} {Value(raceKey)} {Value(raceStateKey)}";
                            if (Value(raceStateKey) == "deny") SendWithReason(command, raceReasonKey);
                            else Send(command);
                        });
                        AddAction("View gate and faction race rules", () => Send("/station jumpgate-race " + orbitId));
                        if (Modding.ModPlayerInfrastructure.Current.JumpgateRaceExemptionsEnabled)
                        {
                            string exemptionKey = "gateRaceExemption_" + orbitId;
                            AddAccountField(exemptionKey, "Race exemption target");
                            AddAction("Grant gate race exemption", () =>
                                Send("/station jumpgate-race " + orbitId + " exempt add " + Value(exemptionKey)));
                            AddAction("Revoke gate race exemption", () =>
                                Send("/station jumpgate-race " + orbitId + " exempt remove " + Value(exemptionKey)));
                        }
                    }
                }
                if (securityOnly) { if (gate.scrapped || gate.permanentlyDestroyed) AddInfo("Access", "Unavailable while the gate is destroyed or scrapped."); continue; }
                AddAction("Manage gate access", () => { drafts["securityGate"] = orbitId; ChangeManagementPage("gate-security"); });
                AddInfo("Structural integrity", gate.structuralIntegrity.ToString("N0"));
                if (gate.empShutdownUntilUtc > DateTimeOffset.UtcNow.ToUnixTimeSeconds()) AddInfo("EMP status", "Temporarily disabled");
                if (gate.scrapped || gate.permanentlyDestroyed)
                {
                    AddInfo("Gate status", gate.scrapped ? "Scrapped. Reclaim it from Orbit Projects." : "Destroyed.");
                    continue;
                }
                if (gate.startedUtc <= 0)
                {
                    AddInfo("Gate status", "Awaiting construction materials");
                    if (gate.materials != null)
                        foreach (var material in gate.materials)
                            if (material != null)
                                AddInfo(NetGame.Db?.Item(material.item)?.name ?? ("Item " + material.item),
                                    Math.Max(0, material.delivered) + "/" + Math.Max(0, material.required));
                    AddInfo("Deliveries", "Fly to this orbit and open Multiplayer → Orbit Projects.");
                }
                else if (gate.building)
                {
                    long remaining = Math.Max(0, gate.completesUtc - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    AddInfo("Gate status", "Under construction · " + remaining.ToString("N0") + " seconds remaining");
                }
                else
                {
                    AddInfo("Gate status", "Operational");
                    long nextChange = gate.destinationChangedUtc + 86400;
                    long untilChange = Math.Max(0, nextChange - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    AddInfo("Destination changes", untilChange == 0 ? "Available now" : "Available in " + untilChange.ToString("N0") + " seconds");
                    var choices = NetGame.Db.Systems.Where(candidate => candidate != null &&
                            candidate.index != gate.system && candidate.index != gate.destinationSystem && candidate.stations?.Count > 0)
                        .OrderBy(candidate => candidate.name).Select(candidate => (candidate.index.ToString(), candidate.name + " [" + candidate.index + "]")).ToList();
                    AddChoice("jumpgateNewDestination_" + orbitId, "New destination system · orbit " + orbitId, choices);
                    if (untilChange > 0 || choices.Count == 0) continue;
                    AddAction("Choose new destination on starmap", () => OpenGateMap("jumpgateNewDestination_" + orbitId, sys => sys != gate.system && sys != gate.destinationSystem && NetGame.Db.Systems.Any(entry => entry != null && entry.index == sys && entry.stations?.Count > 0), orbit => true, true));
                    AddAction("Change destination · orbit " + orbitId, () =>
                    {
                        string destination = Value("jumpgateNewDestination_" + orbitId).Trim();
                        if (!int.TryParse(destination, out _))
                        {
                            feedback.text = "Enter a valid destination system index.";
                            feedbackTime = 4f;
                            return;
                        }
                        Send("/station jumpgate-destination " + orbitId + " " + destination);
                    });
                }
            }
        }

        static List<(string value, string label)> GateModelChoices() =>
            NetPlayerStations.JumpgateModelChoices().Select(model => (model.id.ToString(), model.label)).ToList();

    }
}
