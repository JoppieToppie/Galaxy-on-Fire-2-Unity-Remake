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
    /// <summary>Graphical control centre for every currently implemented player-station feature.
    /// The server remains authoritative: all mutations use the same /station commands as chat.</summary>
    public sealed class StationManagementWindow
    {
        readonly StationMenu menu;
        readonly StationLevel level;
        readonly VisualElement safeArea, panel;
        readonly ScrollView stationList;
        readonly ScrollView body;
        readonly Label heading, feedback;
        readonly Button openButton, closeButton;
        readonly Dictionary<string, string> drafts = new Dictionary<string, string>();
        readonly Dictionary<string, TextField> fields = new Dictionary<string, TextField>();
        PlayerStationRecord selected;
        bool open;
        int confirmOrbitalRemoval;
        int confirmRetirementId = -1;
        int confirmBuilderRelinquish = -1;
        float refresh;
        float nextPlannerQuery;
        HashSet<int> plannerOrbits;
        bool plannerRequestPending;
        StarMap plannerMap;
        NetPlayerStations.MarketUiCatalogue marketCatalogue;
        int marketPage;
        float nextMarketQuery;
        bool compact;
        float feedbackTime;
        float pressedAt = -10f;

        public bool IsOpen => open;
        internal Button MenuButton => openButton;
        internal void FocusFirst() => closeButton.Focus();
        public VisualElement[] NavItems() => panel.Query<VisualElement>().ToList()
            .Where(element => element.ClassListContains("player-station-action") || element.ClassListContains("player-station-row") ||
                element.ClassListContains("player-station-close") || element.ClassListContains("player-station-field"))
            .Where(element => element.focusable).ToArray();

        public StationManagementWindow(StationMenu menu, StationLevel level, VisualElement root)
        {
            this.menu = menu; this.level = level;
            safeArea = root.Q("safeArea") ?? root;
            openButton = new Button(Open) { name = "playerStationsButton", text = "STATION MANAGEMENT" };
            openButton.AddToClassList("station-button");
            openButton.AddToClassList("gof-semibold");
            var buttons = root.Q("sidePanel")?.Q(className: "station-buttons");
            var inspectButton = buttons?.Q<Button>("inspectButton");
            if (inspectButton != null) buttons.Insert(buttons.IndexOf(inspectButton), openButton);
            else if (buttons != null) buttons.Add(openButton);
            else safeArea.Add(openButton);
            openButton.style.display = DisplayStyle.None;
            openButton.clicked += menu.PlayRelease;
            openButton.RegisterCallback<PointerDownEvent>(_ => menu.PlayPush(), TrickleDown.TrickleDown);

            panel = new VisualElement { name = "playerStationManagement" };
            panel.AddToClassList("player-station-window");
            panel.style.display = DisplayStyle.None;
            panel.RegisterCallback<PointerDownEvent>(_ => pressedAt = Time.unscaledTime, TrickleDown.TrickleDown);
            panel.RegisterCallback<PointerUpEvent>(_ => pressedAt = -10f, TrickleDown.TrickleDown);
            safeArea.Add(panel);

            var top = new VisualElement(); top.AddToClassList("player-station-header");
            heading = new Label("PLAYER STATIONS"); heading.AddToClassList("player-station-title"); top.Add(heading);
            closeButton = new Button(Close) { text = "BACK" }; closeButton.AddToClassList("player-station-close");
            closeButton.clicked += menu.PlayRelease;
            closeButton.RegisterCallback<PointerDownEvent>(_ => menu.PlayPush(), TrickleDown.TrickleDown);
            top.Add(closeButton); panel.Add(top);

            var split = new VisualElement(); split.AddToClassList("player-station-content");
            stationList = new ScrollView(ScrollViewMode.Vertical); stationList.AddToClassList("player-station-list");
            split.Add(stationList);
            body = new ScrollView(ScrollViewMode.Vertical); body.AddToClassList("player-station-body");
            split.Add(body); panel.Add(split);
            feedback = new Label(); feedback.AddToClassList("player-station-feedback"); panel.Add(feedback);
            panel.RegisterCallback<GeometryChangedEvent>(_ => ApplyResponsiveLayout());
            panel.RegisterCallback<FocusInEvent>(evt =>
            {
                if (evt.target is VisualElement target)
                {
                    if (body.Contains(target)) body.ScrollTo(target);
                    else if (stationList.Contains(target)) stationList.ScrollTo(target);
                }
            });

        }

        public void Dispose()
        {
            Close();

            openButton?.RemoveFromHierarchy();
            panel?.RemoveFromHierarchy();
        }

        void ApplyResponsiveLayout()
        {
            bool small = InputMode.Current == InputKind.Touch || panel.resolvedStyle.width < 850f;
            if (compact == small) return;
            compact = small;
            panel.EnableInClassList("player-station-compact", small);
            stationList.mode = small ? ScrollViewMode.Horizontal : ScrollViewMode.Vertical;
            stationList.contentContainer.style.flexDirection = small ? FlexDirection.Row : FlexDirection.Column;
        }

        bool CanOpen => NetGame.Active && level?.Layout != null;

        static string StationDataStatus()
        {
            var state = NetState.Instance;
            if (NetGame.IsServer ? !NetGame.PersistentHost && !NetGame.Dedicated
                : state != null && !state.ProfilesOn && !state.Dedicated)
                return "Player-built stations require a persistent world. When hosting, set World to Persistent.";
            if (NetPlayerStations.Stations == null || (!NetGame.IsServer && string.IsNullOrEmpty(state?.PlayerStations)))
                return "Player station data is unavailable. Check that the server has loaded its station registry.";
            return null;
        }

        public void Update()
        {
            openButton.style.display = CanOpen ? DisplayStyle.Flex : DisplayStyle.None;
            if (!CanOpen) { if (open) Close(); return; }
            if (!open) return;
            ApplyResponsiveLayout();
            if (Time.unscaledTime >= nextMarketQuery) RefreshMarketCatalogue();
            if (Time.unscaledTime >= nextPlannerQuery && Modding.ModPlayerInfrastructure.Current.Enabled) RefreshPlannerLocations();
            if (feedbackTime > 0 && (feedbackTime -= Time.unscaledDeltaTime) <= 0) feedback.text = "";
            if (StarMap.IsOpen || Time.unscaledTime - pressedAt < 5f || TextFieldKeys.InTextField(panel.focusController?.focusedElement)) return;
            if ((refresh -= Time.unscaledDeltaTime) <= 0)
            {
                refresh = 2f;
                RebuildStationList();
                RebuildBody(); // Keep public procurement quantities and escrow current while the window remains open.
            }
        }

        void Open()
        {
            if (!CanOpen) return;
            menu.CloseHangar();
            panel.style.display = DisplayStyle.Flex;
            panel.BringToFront();
            open = true;
            refresh = 0;
            plannerOrbits = null;
            plannerRequestPending = false;
            RefreshPlannerLocations();
            RebuildStationList();
            marketPage = 0;
            marketCatalogue = null;
            RefreshMarketCatalogue();
            RebuildBody();
            if (InputMode.Current != InputKind.Touch && !InputMode.MousePointing) closeButton.Focus();
        }

        public void Close()
        {
            if (!open) return;
            panel.style.display = DisplayStyle.None;
            open = false;
            if (plannerMap != null && StarMap.Current == plannerMap) plannerMap.CloseBuilder();
            plannerMap = null;
            confirmRetirementId = -1;
            confirmOrbitalRemoval = 0;
            drafts.Clear();
            plannerOrbits = null;
            plannerRequestPending = false;
            marketCatalogue = null;
            confirmBuilderRelinquish = -1;
        }

        void RebuildStationList()
        {
            stationList.Clear();
            if (StationDataStatus() != null)
            {
                selected = null;
                return;
            }
            // Away from a project, show it only to its funders or when it has public orders.
            int currentStation = level?.Layout != null ? level.Layout.stationIndex : -1;
            var records = NetPlayerStations.Stations?.Where(x => x != null && !x.retired && !x.destroyed && !x.wreckClaimOpen &&
                (x.stationIndex == currentStation || x.underConstruction &&
                    (NetPlayer.Local?.CanFundStationProjectInUi(x.id) == true ||
                        !x.reconstructing && x.buyOrders?.Any(order => order != null && order.remaining > 0) == true)))
                .OrderBy(x => x.stationIndex != currentStation).ThenBy(x => x.id).ToList();
            if (records == null || records.Count == 0)
            {
                stationList.Add(new Label("No local station or accessible construction orders."));
                selected = null;
                return;
            }
            foreach (var record in records)
            {
                string state = record.offeredForClaimUtc > 0 ? "OPEN TO CLAIM" :
                    record.underConstruction ? "CONSTRUCTION" : "OPERATIONAL";
                var button = new Button(() => SelectStation(record))
                { text = record.name + "\n#" + record.id + "  ·  " + state };
                button.AddToClassList("player-station-row");
                button.EnableInClassList("player-station-row--selected", selected?.id == record.id);
                stationList.Add(button);
            }
            int previousId = selected?.id ?? -1;
            selected = records.FirstOrDefault(x => x.id == previousId)
                ?? records.FirstOrDefault(x => x.stationIndex == level.Layout.stationIndex) ?? records[0];
            if (previousId != selected.id) drafts.Clear();
        }

        void SelectStation(PlayerStationRecord record)
        {
            if (selected?.id != record.id)
            {
                confirmRetirementId = -1;
                confirmBuilderRelinquish = -1;
                confirmOrbitalRemoval = 0;
                marketCatalogue = null;
                marketPage = 0;
                nextMarketQuery = 0;
                drafts.Clear();
                body.scrollOffset = Vector2.zero;
            }
            selected = record;
            RebuildStationList();
            RebuildBody();
        }

        void RebuildBody()
        {
            var before = body.scrollOffset;
            var focused = panel.focusController?.focusedElement as VisualElement;
            string focusLabel = (focused as Button)?.text;
            body.Clear(); fields.Clear();
            body.schedule.Execute(() =>
            {
                body.scrollOffset = before;
                if (focusLabel != null && panel.focusController?.focusedElement == null)
                    body.Query<Button>().ToList().Find(button => button.text == focusLabel)?.Focus();
            }).ExecuteLater(1);
            var dataStatus = StationDataStatus();
            if (dataStatus != null)
            {
                AddSection("STATION MANAGEMENT", dataStatus);
                return;
            }

            BuildStationPlanner();
            var current = NetGame.Db.Stations.Find(station => station != null && station.index == level.Layout.stationIndex);
            if (current != null) BuildSystemGates(current.system);
            if (selected == null) return;
            AddSection("STATION OVERVIEW", "");
            if (selected != null)
            {
                AddInfo("Station", selected.name + "  (#" + selected.id + ")");
                if (selected.offeredForClaimUtc > 0)
                {
                    AddInfo("Claim price", NetPlayerStations.StationClaimPrice.ToString("N0") + " credits");
                    AddAction("Claim station", "/station claim " + selected.id);
                    return;
                }
                if (selected.underConstruction)
                {
                    BuildStationConstruction(selected);
                    return;
                }
                AddInfo("Ownership", selected.ownershipType == PlayerStationOwnershipType.Faction ? "Faction-owned" : "Individually owned");
                AddInfo("Owner", selected.ownershipType == PlayerStationOwnershipType.Faction
                    ? (selected.ownerName ?? selected.ownerFactionId ?? "Unknown faction") : (selected.ownerName ?? "Unknown"));
                AddInfo("State", selected.underConstruction ? "Under construction" : "Operational");
                AddInfo("Construction settlement", selected.ownershipType == PlayerStationOwnershipType.Faction
                    ? "Deposits are paid immediately from the faction bank at the fixed catalogue minimum value. If the bank cannot cover a payout, the deposit is rejected and your cargo stays with you."
                    : "Deposits are personal contributions. Individually owned stations do not draw from faction funds.");
                AddAction("View construction contribution ledger", "/station contributions " + selected.id);
                AddAction("View ownership transfer status", "/station transfer-status " + selected.id);
                AddChoice("transferKind", "Transfer recipient type", new List<(string, string)> { ("player", "Individual pilot"), ("faction", "Pilot’s faction") });
                AddAccountField("transferAccount", "Recipient pilot");
                AddAction("Propose ownership transfer", () => Send("/station transfer " + selected.id + " " + Value("transferKind") + " " + Value("transferAccount")));
                AddAction("Accept pending transfer", "/station transfer-accept " + selected.id);
                AddAction("Cancel pending transfer", "/station transfer-cancel " + selected.id);
                if (selected.ownershipType == PlayerStationOwnershipType.Individual && NetCommands.IsAdmin(NetPlayer.Local))
                {
                    AddField("recoveryNote", "Recovery verification note", "");
                    AddAction("Authorise successor recovery", () => Send("/station recover " + selected.id + " " + Value("recoveryNote")));
                }
                AddInfo("System index", selected.system.ToString());
                string installed = selected.addons == null || selected.addons.Count == 0
                    ? "None" : string.Join(", ", selected.addons.Where(a => a != null && a.level > 0).Select(a => ModuleLabel(a.kind)));
                AddInfo("Installed facilities", installed);
                AddAction("Refresh station project status", "/station status " + selected.id);
                AddAction("Show marketplace", "/station market " + selected.id);
                AddAction("Show station storage", "/station storage " + selected.id);
                AddAction("Show ship hangar", "/station ships " + selected.id);
                AddAction("Show dry-dock status", "/station drydock " + selected.id);
                if (NetPlayer.Local?.AvailableStationActions.HasFlag(StationProjectActions.OfferForClaim) == true)
                {
                    int stationId = selected.id;
                    if (confirmRetirementId == stationId)
                    {
                        AddInfo("Public claim", $"Anyone with {NetPlayerStations.StationClaimPrice:N0} credits can claim the station. Private facilities and sponsored gate construction pause.");
                        AddAction("Confirm retirement", () =>
                        {
                            confirmRetirementId = -1;
                            Send("/station retire " + stationId);
                            RebuildBody();
                        });
                        AddAction("Keep station", () => { confirmRetirementId = -1; RebuildBody(); });
                    }
                    else AddAction("Retire station for public claim", () => { confirmRetirementId = stationId; RebuildBody(); });
                }
                if (!NetPlayerStations.CanViewManagement(selected) &&
                    NetPlayer.Local?.AvailableStationActions.HasFlag(StationProjectActions.ViewStationManagement) != true)
                {
                    BuildVisitorMarket(selected.id.ToString());
                    if (selected.addons?.Any(addon => addon != null && addon.kind == "storage") == true)
                        BuildVisitorStorage(selected.id.ToString());
                    BuildConstructionProcurement(selected.id.ToString(), false);
                    if (NetPlayer.Local?.CanUseStationStockInUi == true) BuildStationStock();
                    return;
                }
            }

            if (selected != null)
            {
                AddField("stationName", "New station name", selected.name);
                AddAction("Rename station", () => SendStationName("rename " + selected.id, Value("stationName")));
                AddSection("MARKETPLACE", "Ship sales use the personal stored fleet and the station's sales tax.");
                AddField("salesTax", "Sales tax (0–10%)", selected.marketplaceTaxPercent.ToString());
                AddAction("Set marketplace tax", () => Send("/station tax " + selected.id + " " + Value("salesTax")));
                AddAction("List personal stored hulls", "/station fleet");
                AddField("marketFleetIndex", "Personal stored hull number", "1");
                AddField("marketHullPrice", "Hull price in credits", "");
                AddAction("List hull for sale", () => Send("/station list-ship " + selected.id + " " +
                    Value("marketFleetIndex") + " " + Value("marketHullPrice")));

                if (NetPlayer.Local?.CanUseStationStockInUi == true) BuildStationStock();
                BuildStationGovernance(selected);
            }

            if (selected == null) return;
            string id = selected.id.ToString();
            AddSection("STATION MODULES", "Install facilities using materials from your controlling profile while docked at the station.");
            AddAction("Module costs and requirements", "/station modules");
            if (!selected.underConstruction)
            {
                foreach (string module in new[] { "storage", "refinery", "factory", "hangar", "drydock" })
                {
                    if (selected.addons?.Any(addon => addon != null && addon.kind == module) == true) continue;
                    AddAction("Install " + ModuleLabel(module) + " from cargo", "/station install " + id + " " + module);
                    if (string.IsNullOrEmpty(selected.moduleConstructionKind))
                        AddAction("Open " + ModuleLabel(module) + " project", "/station module-project " + id + " " + module);
                }
            }

            if (Modding.ModPlayerInfrastructure.Current.Enabled && Modding.ModPlayerInfrastructure.Current.JumpgatesEnabled)
                BuildNewJumpgateControls(id);

            BuildOrbitalStructureControls(id);
            BuildFacilityControls(id);
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
                AddChoice("jumpgateDestination", "Destination system", destinations);
                AddChoice("jumpgateModel", "Jumpgate model", GateModelChoices());
                AddField("jumpgateBuildSeconds", "Build time in seconds (0 = no timer)", "0");
                if (orbits.Count > 0 && destinations.Count > 0 && !string.IsNullOrEmpty(Value("jumpgateModel")))
                AddAction("Start jumpgate construction", () =>
                {
                    string orbit = Value("jumpgateOrbit").Trim();
                    string destination = Value("jumpgateDestination").Trim();
                    string buildSeconds = Value("jumpgateBuildSeconds").Trim();
                    if (!int.TryParse(orbit, out _) || !int.TryParse(destination, out _) ||
                        !int.TryParse(buildSeconds, out _) || !int.TryParse(Value("jumpgateModel"), out int model))
                    {
                        feedback.text = "Choose an orbit, destination and model, and enter a build time.";
                        feedbackTime = 4f;
                        return;
                    }
                    Send("/station jumpgate " + id + " " + orbit + " " + destination + " " + buildSeconds + " " + model);
                });
            }

        }

        void BuildOrbitalStructureControls(string id)
        {
            var settings = Modding.ModPlayerInfrastructure.Current;
            string limit = settings.OrbitalDefenseLimit > 0
                ? $"Defense limit per orbit ({settings.OrbitalDefenseLimit}). "
                : "New defense orders are disabled. ";
            AddSection("ORBITAL STRUCTURES", limit + $"Other structure limit per orbit ({settings.OtherOrbitalStructureLimit}). " +
                "Deliver materials from Multiplayer → Orbit Projects.");
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
            if (NetPlayerStations.OrbitalStructures == null) return;
            foreach (var structure in NetPlayerStations.OrbitalStructures)
            {
                if (structure == null || structure.sponsorStationId != selected.id) continue;
                AddInfo($"{structure.name} #{structure.id}", $"Orbit {structure.orbit} · " + (structure.operational ? "Operational" : "Construction"));
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
            AddSection("STORAGE & ACCESS", "Move goods between your cargo hold and shared station storage. Access can be restricted to the owner or shared with current faction members.");
            AddField("storageItem", "Item ID", "156");
            AddField("storageAmount", "Amount", "10");
            AddAction("Deposit cargo", () => Send("/station deposit " + id + " " + Value("storageItem") + " " + Value("storageAmount")));
            AddAction("Withdraw cargo", () => Send("/station withdraw " + id + " " + Value("storageItem") + " " + Value("storageAmount")));
            AddAction("Allow current faction access", "/station access " + id + " faction");
            AddAction("Set owner-only access", "/station access " + id + " owner");

            AddSection("REFINERY", "Convert supported raw ore stored at the station into its matching core.");
            AddField("oreItem", "Raw ore item ID", "156");
            AddField("oreAmount", "Ore units (multiples of 10)", "10");
            AddAction("Refine ore", () => Send("/station refine " + id + " " + Value("oreItem") + " " + Value("oreAmount")));

            AddSection("FACTORY", "Manufacture batches from station storage using the available recipes.");
            AddAction("View factory recipes", "/station recipes");
            AddField("recipe", "Recipe ID", "0");
            AddField("batches", "Batches (1–100)", "1");
            AddAction("Manufacture", () => Send("/station manufacture " + id + " " + Value("recipe") + " " + Value("batches")));
            AddAction("Factory and repeat-order status", "/station factory-status " + id);
            AddField("repeatTotal", "Total batches or continuous", "continuous");
            AddAction("Schedule repeat production", () => Send("/station repeat " + id + " " + Value("recipe") +
                " " + Value("repeatTotal") + " " + Value("batches")));
            AddField("repeatOrder", "Repeat order ID", "");
            AddAction("Cancel repeat order", () => Send("/station repeat-cancel " + id + " " + Value("repeatOrder")));

            AddSection("SHIP HANGAR", "The ordinary hangar stores small and medium bare hulls. Equipment and cargo are transferred into shared station storage; hull upgrades stay with the ship.");
            AddAction("View your personal stored fleet", "/station fleet");
            AddAction("List station hangar ships", "/station ships " + id);
            AddField("fleetIndex", "Personal fleet index", "1");
            AddAction("Store a ship from your fleet", () => Send("/station store-ship " + id + " " + Value("fleetIndex")));
            AddField("hangarIndex", "Station hangar index", "1");
            AddAction("Retrieve a ship to your fleet", () => Send("/station retrieve-ship " + id + " " + Value("hangarIndex")));

            AddSection("DRY DOCK & SHIPYARD", "Dedicated large-hull storage and timed construction of dry-dock-restricted ship blueprints.");
            if (!selected.underConstruction && (selected.addons?.Any(a => a != null && a.kind == "drydock" && a.level > 0) ?? false))
                AddAction("Open dry-dock shipyard", () => { Close(); menu.OpenPlayerDryDock(); });
            AddAction("List dry-dock hulls and build status", "/station drydock " + id);
            AddField("dryFleetIndex", "Personal fleet index", "1");
            AddAction("Store a large hull", () => Send("/station drydock-store " + id + " " + Value("dryFleetIndex")));
            AddField("dryDockIndex", "Dry-dock hull index", "1");
            AddAction("Retrieve a large hull", () => Send("/station drydock-retrieve " + id + " " + Value("dryDockIndex")));
            AddInfo("Ship construction", "Select a blueprint and start construction in the dedicated DRY DOCK shipyard window.");

            AddSection("MARKETPLACE", "Listings escrow goods from station storage. Buyers must be docked at this station; purchases transfer credits and goods through the server.");
            AddAction("View active listings", "/station market " + id);
            AddField("marketItem", "Sale item ID", "156");
            AddField("marketAmount", "Units to list (1–500)", "1");
            AddField("marketPrice", "Price per unit (credits)", "100");
            AddField("marketPayout", "Listing proceeds (personal/faction)", "personal");
            AddAction("Create listing", () => Send("/station list-item " + id + " " + Value("marketItem") + " " + Value("marketAmount") + " " + Value("marketPrice") + " " + Value("marketPayout")));
            BuildMarketCatalogue(id);
            BuildConstructionProcurement(id, NetPlayer.Local?.CanFundStationProjectInUi(selected.id) == true, canManageModule: true);


        }

        void BuildConstructionProcurement(string id, bool allowManagement, bool canManageModule = false)
        {
            string module = selected.moduleConstructionKind;
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
                    AddInfo("BUY ORDER #" + i, itemName + "  ·  " + order.remaining.ToString("N0") +
                        " remaining at " + order.unitPrice.ToString("N0") + " cr each");
                    AddInfo("Escrow", order.reservedCredits.ToString("N0") + " credits reserved");
                    int orderIndex = i;
                    if (!selected.reconstructing)
                    {
                        AddField("procureSellAmount" + i, "Units to sell into order #" + i, "1");
                        AddAction("Sell " + itemName + " to order #" + i, () =>
                            Send("/station sell-order " + id + " " + orderIndex + " " + Value("procureSellAmount" + orderIndex)));
                    }
                    if (allowManagement)
                        AddAction("Cancel order #" + i + " and refund unused escrow", () =>
                            Send("/station cancel-order " + id + " " + orderIndex));
                }
            }
            if (!allowManagement) return;
            AddInfo("Procurement funding", selected.ownershipType == PlayerStationOwnershipType.Faction
                ? "Faction treasury. Orders require leader/officer authority." : "Station owner's personal credits.");
            var requirements = selected.underConstruction ? selected.materials : selected.moduleConstructionMaterials;
            var firstOutstanding = requirements?.Find(material => material != null && material.required > material.delivered);
            AddField("buyOrderItem", "Required material item ID", firstOutstanding?.item.ToString() ?? "");
            AddField("buyOrderAmount", "Units to purchase (1–500)", "10");
            AddField("buyOrderPrice", "Offer per unit (credits)", "100");
            AddAction("Create construction buy order", () => Send("/station buy-order " + id + " " +
                Value("buyOrderItem") + " " + Value("buyOrderAmount") + " " + Value("buyOrderPrice")));
        }

        void AddSection(string title, string description)
        {
            var section = new VisualElement(); section.AddToClassList("player-station-section");
            var label = new Label(title); label.AddToClassList("player-station-section-title"); section.Add(label);
            if (!string.IsNullOrEmpty(description))
            {
                var note = new Label(description);
                note.AddToClassList("player-station-note");
                section.Add(note);
            }
            body.Add(section);
        }

        void AddInfo(string label, string value)
        {
            var row = new VisualElement(); row.AddToClassList("player-station-info");
            row.Add(new Label(label)); row.Add(new Label(value ?? "")); body.Add(row);
        }

        void BuildSystemGates(int system)
        {
            var systemGates = NetPlayerStations.Jumpgates?.Where(g => g != null && g.system == system &&
                    NetPlayerStations.CanViewJumpgateManagement(NetPlayer.Local, g))
                .OrderBy(g => g.orbitStationIndex).ToList();
            if (systemGates == null || systemGates.Count == 0) return;
            AddSection("JUMPGATE NETWORK", "");
            foreach (var gate in systemGates)
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
                if (!gate.scrapped && !gate.permanentlyDestroyed)
                {
                    string modeKey = "gateAccess_" + orbitId;
                    string typeKey = "gateBlockType_" + orbitId;
                    string targetKey = "gateBlockTarget_" + orbitId;
                    string access = gate.accessMode == PlayerJumpgateAccessMode.OwningFaction ? "faction"
                        : gate.accessMode == PlayerJumpgateAccessMode.Blacklist ? "blacklist"
                        : gate.accessMode == PlayerJumpgateAccessMode.OwnerOnly ? "owner" : "everyone";
                    AddField(modeKey, "Access · everyone, faction, blacklist or owner", access);
                    AddAction("Set gate access · orbit " + orbitId, () =>
                        Send("/station jumpgate-access " + orbitId + " " + Value(modeKey)));
                    AddChoice(typeKey, "Blacklist entry type", new List<(string, string)> { ("player", "Pilot"), ("faction", "Pilot’s faction") });
                    AddAccountField(targetKey, "Blacklist target");
                    AddAction("Add to gate blacklist", () =>
                        Send("/station jumpgate-blacklist " + orbitId + " add " + Value(typeKey) + " " + Value(targetKey)));
                    AddAction("Remove from gate blacklist", () =>
                        Send("/station jumpgate-blacklist " + orbitId + " remove " + Value(typeKey) + " " + Value(targetKey)));
                }
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

        void AddChoice(string key, string label, List<(string value, string label)> options, Action<string> changed = null)
        {
            if (options.Count == 0)
            {
                drafts.Remove(key);
                return;
            }
            int selectedIndex = drafts.TryGetValue(key, out string saved) ? options.FindIndex(option => option.value == saved) : -1;
            selectedIndex = Math.Max(0, selectedIndex);
            drafts[key] = options[selectedIndex].value;
            var field = new DropdownField(label, options.Select(option => option.label).ToList(), selectedIndex);
            field.AddToClassList("player-station-field");
            field.RegisterValueChangedCallback(_ =>
            {
                if (field.index < 0) return;
                drafts[key] = options[field.index].value;
                changed?.Invoke(drafts[key]);
            });
            body.Add(field);
        }

        static List<(string value, string label)> StationModelChoices()
        {
            var db = NetGame.Db;
            var models = db.Stations.Where(station => station != null && NetPlayerStations.IsStationModelPreset(station.index))
                .Select(station => (station, race: db.Systems.Find(system => system != null && system.index == station.system)?.raceId ?? -1))
                .OrderBy(entry => entry.race).ThenBy(entry => entry.station.index);
            var result = new List<(string, string)>();
            var counts = new Dictionary<int, int>();
            var assemblies = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in models)
            {
                string assembly = OrbitBuilder.StationPresetAssembly(db, entry.station);
                if (string.IsNullOrEmpty(assembly)) continue;
                // Identical stock models are shown once per race; distinct custom presets retain their identities.
                if (!Modding.ModWorld.IsModStation(entry.station.index) && Modding.ModWorld.ModelOf(entry.station.index) == null &&
                    !assemblies.Add(entry.race + ":" + assembly)) continue;
                counts.TryGetValue(entry.race, out int number); counts[entry.race] = ++number;
                string race = entry.race >= 0 ? GoF2Remake.Flight.Standing.RaceName(entry.race) : "Unassigned";
                result.Add((entry.station.index.ToString(), $"{race} {number} · {entry.station.name} [#{entry.station.index}]"));
            }
            return result;
        }

        static List<(string value, string label)> GateModelChoices() =>
            NetPlayerStations.JumpgateModelChoices().Select(model => (model.id.ToString(), model.label)).ToList();

        void BuildStationPlanner()
        {
            if (Modding.ModPlayerInfrastructure.Current.Enabled)
                BuildNewStationControls();

            AddField("sharedProject", "Shared project ID", "");
            AddField("sharedMaterial", "Material item ID", "");
            AddField("sharedAmount", "Units", "1");
            AddAction("Deliver to a shared project", () =>
                Send("/station contribute " + Value("sharedProject") + " " + Value("sharedMaterial") + " " + Value("sharedAmount")));
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
            if (systems.Count == 0) AddInfo("Build location", "No reachable discovered system has a vacant orbit.");
            else
            {
                var choices = new List<(string value, string label)> { ("", "Select a system") };
                choices.AddRange(systems);
                AddChoice("system", "Reachable build system", choices, _ => { drafts.Remove("buildOrbit"); RebuildBody(); });
                var orbitChoices = new List<(string value, string label)> { ("", "Select a vacant orbit") };
                if (int.TryParse(Value("system"), out int chosenSystem))
                    orbitChoices.AddRange(db.Stations.Where(station => station != null && station.system == chosenSystem && Available(station.index))
                        .OrderBy(station => station.name).Select(station => (station.index.ToString(), station.name)));
                AddChoice("buildOrbit", "Vacant orbit", orbitChoices);
                AddAction("Choose system and orbit on star map", () =>
                {
                    if (StarMap.IsOpen) return;
                    var allowed = new HashSet<int>(systems.Select(system => int.Parse(system.Item1)));
                    panel.style.display = DisplayStyle.None;
                    var map = StarMap.OpenStationPlanner(db, allowed.Contains, Available, result =>
                    {
                        plannerMap = null;
                        if (!open) return;
                        panel.style.display = DisplayStyle.Flex;
                        var picked = db.Stations.Find(station => station != null && station.index == result.station);
                        if (picked != null && allowed.Contains(picked.system) && Available(picked.index))
                        { drafts["system"] = picked.system.ToString(); drafts["buildOrbit"] = picked.index.ToString(); }
                        RebuildBody();
                    });
                    plannerMap = map;
                    if (map == null) { panel.style.display = DisplayStyle.Flex; feedback.text = "The star map could not be opened."; feedbackTime = 8f; }
                    else map.SetHelp("Select a reachable system, then a vacant orbit. Occupied or reserved orbits cannot be confirmed.");
                });
                AddInfo("Orbit placement", "The selected planet keeps its orbit. Construction reserves its vacant station site.");
            }
            var modelPreview = new VisualElement();
            modelPreview.style.height = 120; modelPreview.style.width = 200;
            modelPreview.style.alignSelf = Align.Center;
            var modelImageLabel = new Label();
            modelImageLabel.AddToClassList("player-station-note");
            void Preview(string model)
            {
                if (!int.TryParse(model, out int index)) return;
                var image = Modding.ModPlayerInfrastructure.StationModelImage(index);
                modelPreview.style.backgroundImage = image.texture != null ? new StyleBackground(image.texture) : StyleKeyword.None;
                modelPreview.style.display = image.texture != null ? DisplayStyle.Flex : DisplayStyle.None;
                modelImageLabel.text = string.IsNullOrEmpty(image.id) ? "No thumbnail assigned for this model." : "Image ID: " + image.id;
            }
            AddChoice("newStationModel", "Station model", StationModelChoices(), Preview);
            Preview(Value("newStationModel"));
            body.Add(modelPreview); body.Add(modelImageLabel);
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

        void BuildStationConstruction(PlayerStationRecord project)
        {
            string id = project.id.ToString();
            AddInfo("Project", project.name);
            if (project.materials != null)
                foreach (var material in project.materials)
                    if (material != null)
                        AddInfo((NetGame.Db.Item(material.item)?.name ?? "Item") + " [" + material.item + "]",
                            material.delivered + "/" + material.required);
            if (project.reconstructing)
                AddInfo("Deliveries", "Fly within 5 km of the reconstruction site and open Multiplayer → Orbit Projects.");
            else
            {
                var remaining = project.materials?.Where(material => material != null && material.required > material.delivered)
                    .Select(material => (material.item.ToString(), (NetGame.Db.Item(material.item)?.name ?? "Item " + material.item) +
                        " · " + (material.required - material.delivered).ToString("N0") + " remaining")).ToList()
                    ?? new List<(string, string)>();
                AddChoice("contributeItem", "Required material", remaining);
                AddField("contributeAmount", "Units", "10");
                if (remaining.Count > 0) AddAction("Deliver construction cargo", () =>
                    Send("/station contribute " + id + " " + Value("contributeItem") + " " + Value("contributeAmount")));
            }
            AddAction("View contribution ledger", "/station contributions " + id);
            AddInfo("Shared project ID", project.id.ToString());
            BuildConstructionProcurement(id, NetPlayer.Local?.CanFundStationProjectInUi(project.id) == true);
        }

        void BuildVisitorMarket(string id)
        {
            AddSection("MARKETPLACE", "");
            AddAction("View item and hull listings", "/station market " + id);
            BuildMarketCatalogue(id);
        }

        void RefreshMarketCatalogue()
        {
            nextMarketQuery = Time.unscaledTime + 8f;
            if (selected == null || selected.underConstruction || selected.stationIndex != level.Layout.stationIndex ||
                selected.offeredForClaimUtc > 0) return;
            int stationId = selected.id;
            NetPlayer.Local?.RequestStationMarket(stationId, marketPage, catalogue =>
            {
                if (!open || selected?.id != stationId || catalogue == null) return;
                marketCatalogue = catalogue;
                marketPage = catalogue.page;
                refresh = 0;
            });
        }

        void BuildMarketCatalogue(string id)
        {
            if (marketCatalogue == null || marketCatalogue.stationId.ToString() != id)
            {
                AddInfo("Listings", "Waiting for the server marketplace catalogue…");
                AddAction("Refresh listings", RefreshMarketCatalogue);
                return;
            }
            var entries = marketCatalogue.rows ?? new List<NetPlayerStations.MarketUiRow>();
            if (entries.Count == 0) { AddInfo("Listings", "No available listings."); return; }
            if (marketCatalogue.pageCount > 1)
            {
                AddInfo("Marketplace page", (marketPage + 1) + " / " + marketCatalogue.pageCount);
                if (marketPage > 0) AddAction("Previous page", () => { marketPage--; drafts.Remove("marketSelection"); RefreshMarketCatalogue(); });
                if (marketPage + 1 < marketCatalogue.pageCount) AddAction("Next page", () => { marketPage++; drafts.Remove("marketSelection"); RefreshMarketCatalogue(); });
            }
            string Key(NetPlayerStations.MarketUiRow entry) => (entry.hull ? "h" : "i") + entry.id;
            var choices = new List<(string value, string label)> { ("", "Select a listing") };
            foreach (var entry in entries)
            {
                string name = entry.hull ? NetGame.Db.Ship(entry.item)?.name : NetGame.Db.Item(entry.item)?.name;
                choices.Add((Key(entry), (name ?? "Unknown") + " · " + entry.amount + " × " + entry.price.ToString("N0") + " cr" + (entry.mine ? " (yours)" : "") + " [" + Key(entry) + "]"));
            }
            AddChoice("marketSelection", "Listing", choices, _ => RebuildBody());
            var chosen = entries.Find(entry => Key(entry) == Value("marketSelection"));
            if (chosen == null) return;
            if (chosen.mine)
                AddAction("Cancel my listing", () => Send("/station " + (chosen.hull ? "delist-ship" : "cancel-listing") + " " + id + " " + chosen.id));
            else if (chosen.hull)
                AddAction("Buy selected hull", () => Send("/station buy-ship " + id + " " + chosen.id));
            else
            {
                AddField("marketBuyQuantity", "Units (up to " + chosen.amount + ")", "1");
                AddAction("Buy selected goods", () => Send("/station buy-listing " + id + " " + chosen.id + " " + Value("marketBuyQuantity")));
            }
        }

        void BuildVisitorStorage(string id)
        {
            AddSection("PERSONAL STORAGE", "");
            AddAction("View stored holdings", "/station storage " + id);
            AddField("visitorStoredItem", "Item ID", "");
            AddField("visitorStoredQuantity", "Units", "1");
            AddAction("Withdraw own items", () => Send("/station withdraw " + id + " " +
                Value("visitorStoredItem") + " " + Value("visitorStoredQuantity")));
        }

        void BuildStationStock()
        {
            AddSection("DONATED CONSTRUCTION STOCK", "");
            AddAction("View construction stock", "/station faction-materials");
            AddField("stockItem", "Material item ID", "");
            AddField("stockQuantity", "Units", "1");
            AddAction("Donate from cargo", () => Send("/station faction-donate " + Value("stockItem") + " " + Value("stockQuantity")));
        }

        void BuildStationGovernance(PlayerStationRecord station)
        {
            string id = station.id.ToString();
            AddSection("APPEARANCE AND GOVERNANCE", "");
            AddChoice("stationAppearance", "Station model", StationModelChoices());
            AddAction("Change station model", () => Send("/station appearance " + id + " " + Value("stationAppearance")));
            AddAction("View station history", "/station history " + id);
            AddAccountField("governanceAccount", "Target pilot");
            AddChoice("governanceRole", "Station role", Enum.GetNames(typeof(PlayerStationRole))
                .Append("none").Select(value => (value, value)).ToList());
            AddAction("Set station role", () => Send("/station role " + id + " " + Value("governanceAccount") + " " + Value("governanceRole")));
            AddChoice("governancePermission", "Permission", Enum.GetNames(typeof(PlayerStationPermission)).Select(value => (value, value)).ToList());
            AddChoice("governanceOverride", "Account override", new[] { "inherit", "allow", "deny" }.Select(value => (value, value)).ToList());
            AddAction("Set account override", () => Send("/station permission " + id + " " + Value("governanceAccount") +
                " " + Value("governancePermission") + " " + Value("governanceOverride")));
            if (station.ownershipType == PlayerStationOwnershipType.Faction)
            {
                AddChoice("governanceRank", "Faction rank", new[] { "member", "officer" }.Select(value => (value, value)).ToList());
                AddChoice("governanceDefault", "Default permission", new[] { "allow", "deny" }.Select(value => (value, value)).ToList());
                AddAction("Set faction default", () => Send("/station default " + id + " " + Value("governanceRank") +
                    " " + Value("governancePermission") + " " + Value("governanceDefault")));
                AddChoice("successorType", "Successor type", new[] { "player", "faction" }.Select(value => (value, value)).ToList());
                AddAction("Set faction successor", () => Send("/station successor " + id + " " + Value("successorType") +
                    " " + Value("governanceAccount")));
                AddAction("Clear faction successor", "/station successor " + id + " clear");
                AddAction("Set successor delegate", () => Send("/station successor-delegate " + id + " " + Value("governanceAccount")));
                AddAction("Clear successor delegate", "/station successor-delegate " + id + " none");
            }
            else
            {
                AddAction("Set individual successor", () => Send("/station individual-successor " + id + " " + Value("governanceAccount")));
                AddAction("Clear individual successor", "/station individual-successor " + id + " clear");
            }
            AddField("banReason", "Ban reason (optional)", "");
            AddAction("Ban account", () => Send(("/station ban " + id + " " + Value("governanceAccount") + " " + Value("banReason")).TrimEnd()));
            AddAction("Unban account", () => Send("/station unban " + id + " " + Value("governanceAccount")));
            if (NetPlayer.Local?.CanRelinquishBuilderRightsInUi == true)
            {
                if (confirmBuilderRelinquish == station.id)
                {
                    AddAction("Confirm permanent relinquishment", () =>
                    {
                        confirmBuilderRelinquish = -1;
                        Send("/station builder-relinquish " + id);
                        RebuildBody();
                    });
                    AddAction("Keep builder rights", () => { confirmBuilderRelinquish = -1; RebuildBody(); });
                }
                else AddAction("Relinquish builder rights", () => { confirmBuilderRelinquish = station.id; RebuildBody(); });
            }
        }

        void AddAccountField(string key, string label)
        {
            AddField(key, label + " (or offline account/faction reference)", "");
            var pilots = NetPlayer.All.Where(pilot => pilot != null && pilot.IsSpawned && !pilot.Observer)
                .OrderBy(pilot => pilot.DisplayName).ToList();
            if (pilots.Count == 0) return;
            var labels = new List<string> { "Select an online pilot…" };
            labels.AddRange(pilots.Select(pilot => pilot.TaggedName + " [#" + pilot.OwnerClientId + "]"));
            var picker = new DropdownField(label, labels, 0);
            picker.AddToClassList("player-station-field");
            picker.RegisterValueChangedCallback(_ =>
            {
                if (picker.index > 0 && picker.index <= pilots.Count)
                    fields[key].value = "online:" + pilots[picker.index - 1].OwnerClientId;
            });
            body.Add(picker);
        }

        void AddField(string key, string label, string initial)
        {
            var field = new TextField(label) { value = drafts.TryGetValue(key, out string draft) ? draft : initial ?? "" };
            if (key == "newStationName" || key == "stationName") field.maxLength = NetPlayerStations.StationNameMaxLength;
            field.RegisterValueChangedCallback(change => drafts[key] = change.newValue);
            field.AddToClassList("player-station-field");
            fields[key] = field; body.Add(field);
        }

        void AddAction(string label, string command) => AddAction(label, () => Send(command));

        void AddAction(string label, Action action)
        {
            var button = new Button(action) { text = label };
            button.AddToClassList("player-station-action");
            button.clicked += menu.PlayRelease;
            button.RegisterCallback<PointerDownEvent>(_ => menu.PlayPush(), TrickleDown.TrickleDown);
            body.Add(button);
        }

        string Value(string key) => fields.TryGetValue(key, out var field) ? (field.value ?? "").Trim() :
            drafts.TryGetValue(key, out var choice) ? choice : "";
        void SendStationName(string action, string name)
        {
            if (!NetPlayerStations.ValidStationName(name))
            {
                feedback.text = NetPlayerStations.StationNameRules;
                feedbackTime = 8f;
                return;
            }
            Dispatch("/station " + action + " " + name);
        }

        void Send(string command)
        {
            if (string.IsNullOrWhiteSpace(command) || command.Contains("  "))
            {
                feedback.text = "Complete the required fields before submitting.";
                feedbackTime = 8f;
                return;
            }
            Dispatch(command);
        }

        void Dispatch(string command)
        {
            feedback.text = "Request sent. The server response will appear here.";
            feedbackTime = 8f;
            if (NetPlayer.Local?.RequestStationUiCommand(command.TrimEnd(), result =>
            {
                if (!open) return;
                feedback.text = string.IsNullOrEmpty(result) ? "Request completed." : result;
                feedbackTime = 12f;
                refresh = 0;
                nextPlannerQuery = 0;
                nextMarketQuery = 0;
            }) != true)
            {
                feedback.text = "A station request is already pending, or the server is unavailable.";
                feedbackTime = 8f;
            }
        }

        static string ModuleLabel(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Unknown module";
            return id == "drydock" ? "Dry Dock" : id == "hangar" ? "Ship Hangar" :
                char.ToUpperInvariant(id[0]) + id.Substring(1);
        }
    }
}
