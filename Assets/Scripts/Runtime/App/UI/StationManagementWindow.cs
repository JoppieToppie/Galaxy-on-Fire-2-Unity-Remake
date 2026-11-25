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
        readonly bool inFlight;
        readonly VisualElement stationRoot, overlay, panel;
        readonly ScrollView stationList;
        readonly ScrollView body;
        readonly VisualElement pageBar;
        NetPlayerStations.ManagementCatalogue managementCatalogue;
        int directoryPage;
        float nextManagementQuery;
        string pageBarSignature;
        readonly Label heading, feedback;
        readonly Button openButton, closeButton;
        readonly Dictionary<string, string> drafts = new Dictionary<string, string>();
        readonly Dictionary<string, TextField> fields = new Dictionary<string, TextField>();
        PlayerStationRecord selected;
        bool open;
        int deliveryProjectId = -1;
        string managementPage = "overview";
        int confirmOrbitalRemoval;
        int confirmRetirementId = -1;
        int confirmBuilderRelinquish = -1;
        float refresh;
        float nextPlannerQuery;
        HashSet<int> plannerOrbits;
        bool plannerRequestPending;
        StarMap plannerMap;
        string modelPickerKey;
        Vector2 modelPickerReturnScroll;
        NetPlayerStations.MarketUiCatalogue marketCatalogue;
        int marketPage;
        float nextMarketQuery;
        bool compact;
        float feedbackTime;
        float pressedAt = -10f;

        public bool IsOpen => open;
        internal bool IsTyping => open && TextFieldKeys.InTextField(panel.focusController?.focusedElement);
        internal Button MenuButton => openButton;
        internal void FocusFirst() => closeButton.Focus();
        public VisualElement[] NavItems() => panel.Query<VisualElement>().ToList()
            .Where(element => element.ClassListContains("player-station-action") || element.ClassListContains("player-station-row") ||
                element.ClassListContains("player-station-close") || element.ClassListContains("player-station-field"))
            .Where(element => element.focusable).ToArray();

        public StationManagementWindow(StationMenu menu, StationLevel level, VisualElement root, bool flight = false)
        {
            this.menu = menu; this.level = level; inFlight = flight;
            stationRoot = root;
            var safeArea = root.Q("safeArea") ?? root;
            openButton = new Button(Open) { name = "playerStationsButton", text = "STATION MANAGEMENT" };
            openButton.AddToClassList("station-button");
            openButton.AddToClassList("gof-semibold");
            var buttons = root.Q("sidePanel")?.Q(className: "station-buttons");
            var inspectButton = buttons?.Q<Button>("inspectButton");
            if (!inFlight)
            {
                if (inspectButton != null) buttons.Insert(buttons.IndexOf(inspectButton), openButton);
                else if (buttons != null) buttons.Add(openButton);
                else safeArea.Add(openButton);
            }
            openButton.style.display = DisplayStyle.None;
            openButton.clicked += () => menu?.PlayRelease();
            openButton.RegisterCallback<PointerDownEvent>(_ => menu?.PlayPush(), TrickleDown.TrickleDown);

            overlay = new VisualElement { name = "playerStationOverlay" };
            overlay.AddToClassList("player-station-overlay");
            if (inFlight)
            {
                var sheet = Resources.Load<StyleSheet>("GoF2Net/PlayerStationWindow");
                if (sheet != null) overlay.styleSheets.Add(sheet);
            }
            overlay.style.display = DisplayStyle.None;
            safeArea.Add(overlay);
            panel = new VisualElement { name = "playerStationManagement" };
            panel.AddToClassList("player-station-window");
            panel.RegisterCallback<PointerDownEvent>(_ => pressedAt = Time.unscaledTime, TrickleDown.TrickleDown);
            panel.RegisterCallback<PointerUpEvent>(_ => pressedAt = -10f, TrickleDown.TrickleDown);
            overlay.Add(panel);

            var top = new VisualElement(); top.AddToClassList("player-station-header");
            heading = new Label("PLAYER STATIONS"); heading.AddToClassList("player-station-title"); top.Add(heading);
            closeButton = new Button(Back) { text = "BACK" }; closeButton.AddToClassList("player-station-close");
            closeButton.clicked += () => menu?.PlayRelease();
            closeButton.RegisterCallback<PointerDownEvent>(_ => menu?.PlayPush(), TrickleDown.TrickleDown);
            top.Add(closeButton); panel.Add(top);

            var split = new VisualElement(); split.AddToClassList("player-station-content");
            stationList = new ScrollView(ScrollViewMode.Vertical); stationList.AddToClassList("player-station-list");
            split.Add(stationList);
            body = new ScrollView(ScrollViewMode.Vertical); body.AddToClassList("player-station-body");
            var workspace = new VisualElement();
            workspace.style.flexGrow = 1; workspace.style.flexBasis = 0; workspace.style.minWidth = 0; workspace.style.minHeight = 0;
            pageBar = new VisualElement(); pageBar.style.flexDirection = FlexDirection.Row; pageBar.style.flexWrap = Wrap.Wrap;
            pageBar.style.marginBottom = 8; pageBar.style.flexShrink = 0;
            workspace.Add(pageBar); workspace.Add(body); split.Add(workspace); panel.Add(split);
            feedback = new Label(); feedback.AddToClassList("player-station-feedback"); panel.Add(feedback);
            panel.RegisterCallback<GeometryChangedEvent>(_ => ApplyResponsiveLayout());
            panel.RegisterCallback<PointerCancelEvent>(_ => pressedAt = -10f, TrickleDown.TrickleDown);
            if (inFlight)
            {
                panel.RegisterCallback<NavigationMoveEvent>(NavigateFlight, TrickleDown.TrickleDown);
                UiFocus.SkipScrollBars(panel);
            }
            panel.RegisterCallback<FocusInEvent>(evt =>
            {
                if (evt.target is VisualElement target)
                {
                    if (target != body.contentContainer && body.contentContainer.Contains(target)) body.ScrollTo(target);
                    else if (target != stationList.contentContainer && stationList.contentContainer.Contains(target)) stationList.ScrollTo(target);
                }
            });

        }

        void NavigateFlight(NavigationMoveEvent evt)
        {
            if (!open || StarMap.IsOpen) return;
            var from = panel.focusController?.focusedElement as VisualElement;
            if (TextFieldKeys.IsTyping(evt, from)) return;
            bool Usable(VisualElement element) => element.enabledInHierarchy && element.visible &&
                element.resolvedStyle.display != DisplayStyle.None && element.worldBound.width > 0.5f && element.worldBound.height > 0.5f;
            var items = NavItems().Where(Usable).ToList();
            if (items.Count == 0) return;
            int dx = evt.direction == NavigationMoveEvent.Direction.Left ? -1 : evt.direction == NavigationMoveEvent.Direction.Right ? 1 : 0;
            int dy = evt.direction == NavigationMoveEvent.Direction.Up ? -1 : evt.direction == NavigationMoveEvent.Direction.Down ? 1 : 0;
            var next = from == null || !items.Contains(from) ? items[0] : SpatialNav.Next(items, from, dx, dy, Usable);
            next?.Focus();
            evt.StopPropagation();
            panel.focusController?.IgnoreEvent(evt);
        }

        public void Dispose()
        {
            Close();

            openButton?.RemoveFromHierarchy();
            overlay?.RemoveFromHierarchy();
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

        int CurrentOrbit => inFlight ? NetPlayer.Local?.Station ?? -1 : level?.Layout?.stationIndex ?? -1;
        internal bool CanOpen => NetGame.Active && CurrentOrbit >= 0 && (!inFlight || NetPlayer.Local?.InSpace == true);

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
            openButton.style.display = !inFlight && CanOpen ? DisplayStyle.Flex : DisplayStyle.None;
            if (!CanOpen) { if (open) Close(); return; }
            if (!open) return;
            if (inFlight) overlay.style.display = StarMap.IsOpen ? DisplayStyle.None : DisplayStyle.Flex;
            ApplyResponsiveLayout();
            int nearby = LocalDeliveryProject()?.id ?? -1;
            if (nearby != deliveryProjectId) { deliveryProjectId = nearby; refresh = 0; }
            if (Time.unscaledTime >= nextManagementQuery) RefreshManagementCatalogue();
            if (Time.unscaledTime >= nextMarketQuery && managementPage == "market") RefreshMarketCatalogue();
            if (Time.unscaledTime >= nextPlannerQuery && Modding.ModPlayerInfrastructure.Current.Enabled) RefreshPlannerLocations();
            if (feedbackTime > 0 && (feedbackTime -= Time.unscaledDeltaTime) <= 0) feedback.text = "";
            if (StarMap.IsOpen || modelPickerKey != null || Time.unscaledTime - pressedAt < 5f || TextFieldKeys.InTextField(panel.focusController?.focusedElement)) return;
            if ((refresh -= Time.unscaledDeltaTime) <= 0)
            {
                refresh = 2f;
                RebuildStationList();
                RebuildBody(); // Keep public procurement quantities and escrow current while the window remains open.
            }
        }

        internal void Open()
        {
            if (!CanOpen || open) return;
            menu?.CloseHangar();
            overlay.style.display = DisplayStyle.Flex;
            overlay.BringToFront();
            open = true;
            managementPage = "overview";
            refresh = 0;
            plannerOrbits = null;
            plannerRequestPending = false;
            RefreshPlannerLocations();
            RebuildStationList();
            marketPage = 0;
            marketCatalogue = null;
            managementCatalogue = null; directoryPage = 0; nextManagementQuery = 0;
            RefreshManagementCatalogue();
            RefreshMarketCatalogue();
            RebuildBody();
            if (InputMode.Current != InputKind.Touch && !InputMode.MousePointing) closeButton.Focus();
        }

        internal void Back()
        {
            if (modelPickerKey != null) CloseModelPicker();
            else Close();
        }

        public void Close()
        {
            if (!open) return;
            overlay.style.display = DisplayStyle.None;
            open = false;
            if (panel.focusController?.focusedElement is VisualElement focused && panel.Contains(focused)) focused.Blur();
            if (plannerMap != null)
            {
                stationRoot.RemoveFromClassList("station-map-open");
                if (StarMap.Current == plannerMap) plannerMap.CloseBuilder();
                plannerMap = null;
            }
            modelPickerKey = null;
            panel.RemoveFromClassList("player-station-picking-model");
            heading.text = "PLAYER STATIONS";
            confirmRetirementId = -1;
            confirmOrbitalRemoval = 0;
            drafts.Clear();
            plannerOrbits = null;
            plannerRequestPending = false;
            marketCatalogue = null;
            managementCatalogue = null; pageBarSignature = null; pageBar.Clear();
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
            int currentStation = CurrentOrbit;
            var records = NetPlayerStations.Stations?.Where(x => x != null && !x.retired && !x.destroyed && !x.wreckClaimOpen &&
                (x.stationIndex == currentStation || NetPlayer.Local?.CanViewStationLedgerInUi(x.id) == true || x.underConstruction &&
                    (NetPlayer.Local?.CanFundStationProjectInUi(x.id) == true ||
                        x.stationIndex == currentStation || !x.reconstructing && x.buyOrders?.Any(order => order != null && order.remaining > 0) == true)))
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
                ?? records.FirstOrDefault(x => x.stationIndex == CurrentOrbit) ?? records[0];
            if (previousId != selected.id) { drafts.Clear(); managementCatalogue = null; directoryPage = 0; nextManagementQuery = 0; managementPage = "overview"; }
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
                managementCatalogue = null; directoryPage = 0; nextManagementQuery = 0;
                managementPage = "overview";
                drafts.Clear();
                body.scrollOffset = Vector2.zero;
            }
            selected = record;
            RebuildStationList();
            RefreshManagementCatalogue();
            RebuildBody();
        }

        PlayerStationRecord LocalDeliveryProject()
        {
            var pilot = NetPlayer.Local;
            if (pilot?.InSpace != true) return null;
            var project = NetPlayerStations.StationAt(pilot.Station);
            return NetPlayerStations.CanDeliverAtConstructionSite(pilot, project) ? project : null;
        }

        void AddStationLocation(PlayerStationRecord record)
        {
            var db = NetGame.Db;
            var system = db?.Systems?.Find(entry => entry != null && entry.index == record.system);
            var orbit = db?.Stations?.Find(entry => entry != null && entry.index == record.stationIndex);
            AddInfo("System", system?.name ?? "Unknown system");
            AddInfo("Orbit", orbit?.name ?? "Unknown orbit");
        }

        void RebuildBody(Vector2? scrollPosition = null)
        {
            var before = scrollPosition ?? body.scrollOffset;
            var focused = panel.focusController?.focusedElement as VisualElement;
            string focusLabel = (focused as Button)?.text;
            body.Clear(); fields.Clear();
            body.schedule.Execute(() =>
            {
                body.scrollOffset = before;
                if (focusLabel != null && panel.focusController?.focusedElement == null)
                    body.Query<Button>().ToList().Find(button => button.text == focusLabel)?.Focus();
            }).ExecuteLater(1);
            RebuildPageBar();
            var dataStatus = StationDataStatus();
            if (dataStatus != null)
            {
                AddSection("STATION MANAGEMENT", dataStatus);
                return;
            }

            var localProject = LocalDeliveryProject();
            if (managementPage == "build")
            {
                if (Modding.ModPlayerInfrastructure.Current.Enabled) BuildNewStationControls();
                return;
            }
            if (managementPage == "contribute")
            {
                if (localProject != null) BuildStationConstruction(localProject);
                else AddInfo("Delivery", "Dock at a station construction site to contribute materials.");
                return;
            }
            if (managementPage == "orders")
            {
                if (selected == null) AddInfo("Project", "Select a construction project from the station list.");
                else if (!selected.underConstruction && string.IsNullOrEmpty(selected.moduleConstructionKind))
                    AddInfo("Project", "This station has no active construction project.");
                else BuildConstructionProcurement(selected.id.ToString(), NetPlayer.Local?.CanFundStationProjectInUi(selected.id) == true,
                    canManageModule: !selected.underConstruction && (managementCatalogue?.canConstruction == true ||
                        NetPlayer.Local?.AvailableStationActions.HasFlag(StationProjectActions.ViewStationManagement) == true));
                return;
            }
            if (managementPage == "orbit") { AddSection("ORBIT PROJECTS", "Deliver materials, manage wrecks and inspect orbital projects."); AddAction("Open orbit projects", MultiplayerWindow.OpenOrbitProjectsAny); return; }
            if (managementPage == "gates")
            {
                int system = selected?.system ?? NetGame.Db.Stations.Find(station => station != null && station.index == CurrentOrbit)?.system ?? -1;
                BuildSystemGates(system);
                if (selected != null && !selected.underConstruction && managementCatalogue?.canConstruction == true &&
                    Modding.ModPlayerInfrastructure.Current.JumpgatesEnabled) BuildNewJumpgateControls(selected.id.ToString());
                return;
            }
            if (selected == null) { AddSection("STATION OVERVIEW", "Select a station or begin a construction project."); return; }
            string id = selected.id.ToString();
            bool operational = !selected.underConstruction && selected.offeredForClaimUtc == 0;
            bool docked = !inFlight && selected.stationIndex == CurrentOrbit;
            bool manager = managementCatalogue?.canManage == true;
            bool ownership = managementCatalogue?.canOwnership == true;
            bool HasModule(string module) => selected.addons?.Any(addon => addon != null && addon.kind == module && addon.level > 0) == true;
            if (managementPage == "overview" || !operational || !docked)
            {
                AddSection("STATION OVERVIEW", "");
                AddInfo("Station", selected.name);
                AddStationLocation(selected);
                if (selected.offeredForClaimUtc > 0)
                {
                    AddInfo("Claim price", NetPlayerStations.StationClaimPrice.ToString("N0") + " credits");
                    AddAction("Claim station", "/station claim " + id);
                    return;
                }
                if (selected.underConstruction)
                {
                    AddInfo("State", "Under construction");
                    if (localProject?.id == selected.id) AddAction("Contribute to station", () => ChangeManagementPage("contribute"));
                    else AddInfo("Delivery", "Dock at this construction site to deliver materials.");
                    if (NetPlayer.Local?.CanViewStationLedgerInUi(selected.id) == true)
                        AddAction("View construction contribution ledger", "/station contributions " + id);
                    AddAction("Open resource buy orders", () => ChangeManagementPage("orders"));
                    return;
                }
                AddInfo("Ownership", selected.ownershipType == PlayerStationOwnershipType.Faction ? "Faction-owned" : "Individually owned");
                AddInfo("Owner", selected.ownerName ?? "Unknown");
                AddInfo("State", "Operational");
                AddInfo("Installed facilities", selected.addons == null || selected.addons.Count == 0 ? "None" :
                    string.Join(", ", selected.addons.Where(a => a != null && a.level > 0).Select(a => ModuleLabel(a.kind))));
                if (!string.IsNullOrEmpty(selected.moduleConstructionKind)) AddInfo("Active module project", ModuleLabel(selected.moduleConstructionKind));
                if (NetPlayer.Local?.CanViewStationLedgerInUi(selected.id) == true)
                    AddAction("View construction contribution ledger", "/station contributions " + id);
                if (!docked) AddInfo("Facilities", "Dock at this station to manage or use its facilities.");
                else AddAction("Refresh station project status", "/station status " + id);
                return;
            }
            switch (managementPage)
            {
                case "news":
                    AddSection("STATION NEWS", "Recent announcements from the owning faction.");
                    var announcements = NetNews.StationAnnouncements(selected.stationIndex);
                    if (announcements.Count == 0) AddInfo("Announcements", "No recent announcements.");
                    foreach (string announcement in announcements) AddInfo("Announcement", announcement);
                    if (managementCatalogue?.canAnnounce == true)
                    {
                        AddField("stationAnnouncement", "New announcement", "");
                        AddAction("Publish to station news", () => Send(("/station announce " + id + " " + Value("stationAnnouncement")).TrimEnd()));
                    }
                    break;
                case "ownership":
                    AddSection("OWNERSHIP & SUCCESSION", "Transfer ownership, nominate successors and manage builder rights.");
                    AddAction("View ownership transfer status", "/station transfer-status " + id);
                    AddAction("Accept pending transfer", "/station transfer-accept " + id);
                    if (ownership)
                    {
                        AddChoice("transferKind", "Recipient type", new List<(string, string)> { ("player", "Individual pilot"), ("faction", "Faction") }, _ => { drafts.Remove("transferAccount"); RebuildBody(); });
                        AddAccountField("transferAccount", "Recipient");
                        AddAction("Propose ownership transfer", () => Send("/station transfer " + id + " " + Value("transferKind") + " " + Value("transferAccount")));
                        AddAction("Cancel pending transfer", "/station transfer-cancel " + id);
                    }
                    BuildSuccessionControls(selected);
                    if (NetPlayer.Local?.AvailableStationActions.HasFlag(StationProjectActions.OfferForClaim) == true)
                    {
                        if (confirmRetirementId == selected.id)
                        {
                            AddInfo("Public claim", $"Anyone with {NetPlayerStations.StationClaimPrice:N0} credits can claim the station. Private facilities pause.");
                            AddAction("Confirm retirement", () => { confirmRetirementId = -1; Send("/station retire " + id); RebuildBody(); });
                            AddAction("Keep station", () => { confirmRetirementId = -1; RebuildBody(); });
                        }
                        else AddAction("Retire station for public claim", () => { confirmRetirementId = selected.id; RebuildBody(); });
                    }
                    break;
                case "appearance":
                    AddSection("STATION APPEARANCE", "Choose an available model preset.");
                    AddStationModelPicker("stationAppearance");
                    if (!string.IsNullOrEmpty(Value("stationAppearance"))) AddAction("Apply station model", () => Send("/station appearance " + id + " " + Value("stationAppearance")));
                    break;
                case "security": BuildStationGovernance(selected); break;
                case "modules":
                    AddSection("STATION MODULES", "Install facilities from cargo or open a procurement project.");
                    AddAction("Module costs and requirements", "/station modules");
                    foreach (string module in new[] { "storage", "refinery", "factory", "hangar", "drydock" })
                    {
                        if (HasModule(module)) { AddInfo(ModuleLabel(module), "Installed"); continue; }
                        AddAction("Install " + ModuleLabel(module) + " from cargo", "/station install " + id + " " + module);
                        if (string.IsNullOrEmpty(selected.moduleConstructionKind)) AddAction("Open " + ModuleLabel(module) + " project", "/station module-project " + id + " " + module);
                    }
                    if (!string.IsNullOrEmpty(selected.moduleConstructionKind)) AddAction("Manage active module procurement", () => ChangeManagementPage("orders"));
                    if (NetPlayer.Local?.CanUseStationStockInUi == true) BuildStationStock();
                    break;
                case "defenses": BuildOrbitalStructureControls(id); break;
                case "market":
                    if (manager)
                    {
                        AddSection("MARKETPLACE SETTINGS", "");
                        AddNumber("salesTax", "Sales tax (%)", selected.marketplaceTaxPercent, 0, 10);
                        AddAction("Set marketplace tax", () => Send("/station tax " + id + " " + Value("salesTax")));
                        AddCatalogueChoice("marketFleetIndex", "Personal stored hull", "fleet");
                        AddNumber("marketHullPrice", "Hull price (credits)", 10000, 1, 1000000000);
                        if (!string.IsNullOrEmpty(Value("marketFleetIndex"))) AddAction("List selected hull for sale", () => Send("/station list-ship " + id + " " + Value("marketFleetIndex") + " " + Value("marketHullPrice")));
                        BuildFacilityControls(id);
                    }
                    else BuildVisitorMarket(id);
                    break;
                case "storage": if (manager) BuildFacilityControls(id); else BuildVisitorStorage(id); break;
                case "refinery": case "factory": BuildFacilityControls(id); break;
                case "hangar": case "drydock": BuildFacilityControls(id); break;
                default: ChangeManagementPage("overview"); break;
            }
        }

        void RebuildPageBar()
        {
            var pages = new List<(string key, string label)> { ("overview", "Overview") };
            if (Modding.ModPlayerInfrastructure.Current.Enabled) pages.Add(("build", "Build station"));
            if (LocalDeliveryProject() != null) pages.Add(("contribute", "Contribute"));
            pages.Add(("orders", "Buy orders"));
            if (inFlight) pages.Add(("orbit", "Orbit projects"));
            if (Modding.ModPlayerInfrastructure.Current.JumpgatesEnabled) pages.Add(("gates", "Jumpgates"));
            if (selected != null && !selected.underConstruction && selected.offeredForClaimUtc <= 0 && !inFlight && selected.stationIndex == CurrentOrbit)
            {
                bool HasModule(string kind) => selected.addons?.Any(addon => addon != null && addon.kind == kind && addon.level > 0) == true;
                pages.Add(("news", "News")); pages.Add(("market", "Market"));
                if (HasModule("storage")) pages.Add(("storage", "Storage"));
                if (HasModule("hangar")) pages.Add(("hangar", "Hangar"));
                if (HasModule("drydock")) pages.Add(("drydock", "Dry dock"));
                if (managementCatalogue?.canManage == true)
                {
                    if (HasModule("refinery")) pages.Add(("refinery", "Refinery"));
                    if (HasModule("factory")) pages.Add(("factory", "Factory"));
                }
                if (managementCatalogue?.canOwnership == true || NetPlayer.Local?.CanRelinquishBuilderRightsInUi == true)
                    pages.Add(("ownership", "Ownership"));
                else pages.Add(("ownership", "Transfers"));
                if (managementCatalogue?.canSecurity == true || managementCatalogue?.canOwnership == true) pages.Add(("security", "Security"));
                if (managementCatalogue?.canAppearance == true) pages.Add(("appearance", "Appearance"));
                if (managementCatalogue?.canConstruction == true) { pages.Add(("modules", "Modules")); pages.Add(("defenses", "Defenses")); }
            }
            if (!pages.Any(page => page.key == managementPage)) managementPage = "overview";
            string signature = managementPage + ":" + string.Join(",", pages.Select(page => page.key + page.label));
            pageBar.style.display = modelPickerKey == null ? DisplayStyle.Flex : DisplayStyle.None;
            if (signature == pageBarSignature) return;
            pageBarSignature = signature;
            pageBar.Clear();
            foreach (var page in pages)
            {
                var button = new Button(() => ChangeManagementPage(page.key)) { text = page.label };
                button.AddToClassList("player-station-action");
                button.EnableInClassList("player-station-row--selected", managementPage == page.key);
                button.style.flexGrow = 0; button.style.minHeight = 36; button.style.marginRight = 4; button.style.marginBottom = 4;
                pageBar.Add(button);
            }
        }

        void RefreshManagementCatalogue()
        {
            nextManagementQuery = Time.unscaledTime + 8f;
            if (!open || inFlight || selected == null || selected.underConstruction || selected.stationIndex != CurrentOrbit || selected.offeredForClaimUtc > 0) return;
            int stationId = selected.id;
            NetPlayer.Local?.RequestManagementCatalogue(stationId, directoryPage, catalogue =>
            {
                if (!open || selected?.id != stationId || catalogue == null) return;
                managementCatalogue = catalogue;
                directoryPage = catalogue.directoryPage;
                refresh = 0;
            });
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
            AddItemChoice("storageItem", "Item");
            AddField("storageAmount", "Amount", "10");
            AddAction("Deposit cargo", () => Send("/station deposit " + id + " " + Value("storageItem") + " " + Value("storageAmount")));
            AddAction("Withdraw cargo", () => Send("/station withdraw " + id + " " + Value("storageItem") + " " + Value("storageAmount")));
            AddAction("Allow current faction access", "/station access " + id + " faction");
            AddAction("Set owner-only access", "/station access " + id + " owner");

            AddSection("REFINERY", "Convert supported raw ore stored at the station into its matching core.");
            AddItemChoice("oreItem", "Raw ore", NetPlayerStations.RefinableOreItems());
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
            if (!inFlight && !selected.underConstruction && (selected.addons?.Any(a => a != null && a.kind == "drydock" && a.level > 0) ?? false))
                AddAction("Open dry-dock shipyard", () => { Close(); menu.OpenPlayerDryDock(); });
            AddAction("List dry-dock hulls and build status", "/station drydock " + id);
            AddField("dryFleetIndex", "Personal fleet index", "1");
            AddAction("Store a large hull", () => Send("/station drydock-store " + id + " " + Value("dryFleetIndex")));
            AddField("dryDockIndex", "Dry-dock hull index", "1");
            AddAction("Retrieve a large hull", () => Send("/station drydock-retrieve " + id + " " + Value("dryDockIndex")));
            AddInfo("Ship construction", "Select a blueprint and start construction in the dedicated DRY DOCK shipyard window.");

            AddSection("MARKETPLACE", "Listings escrow goods from station storage. Buyers must be docked at this station; purchases transfer credits and goods through the server.");
            AddAction("View active listings", "/station market " + id);
            AddItemChoice("marketItem", "Item to sell");
            AddField("marketAmount", "Units to list (1–500)", "1");
            AddField("marketPrice", "Price per unit (credits)", "100");
            AddField("marketPayout", "Listing proceeds (personal/faction)", "personal");
            AddAction("Create listing", () => Send("/station list-item " + id + " " + Value("marketItem") + " " + Value("marketAmount") + " " + Value("marketPrice") + " " + Value("marketPayout")));
            BuildMarketCatalogue(id);
            if (!string.IsNullOrEmpty(selected.moduleConstructionKind))
                AddAction("Open resource buy orders", () => ChangeManagementPage("orders"));


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
                    AddInfo("BUY ORDER #" + order.id, itemName + "  ·  " + order.remaining.ToString("N0") +
                        " remaining at " + order.unitPrice.ToString("N0") + " cr each");
                    AddInfo("Escrow", order.reservedCredits.ToString("N0") + " credits reserved");
                    int orderIndex = order.id;
                    if (orderIndex <= 0) continue;
                    if (selected.underConstruction ? LocalDeliveryProject()?.id == selected.id :
                        !inFlight && CurrentOrbit == selected.stationIndex)
                    {
                        AddField("procureSellAmount" + orderIndex, "Units to sell into order #" + orderIndex, "1");
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
            AddChoice("buyOrderItem", "Required material", requirements.Where(material => material != null &&
                outstanding.Contains(material.item) && NetGame.Db?.Item(material.item) != null)
                .OrderBy(material => NetGame.Db.Item(material.item).name, StringComparer.CurrentCultureIgnoreCase)
                .Select(material => (material.item.ToString(), NetGame.Db.Item(material.item).name +
                    " · " + (material.required - material.delivered).ToString("N0") + " needed")).ToList());
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
                    string reasonKey = "gateBlockReason_" + orbitId;
                    AddField(reasonKey, "Reason shown to denied pilots (optional)", "");
                    AddAction("Add / update gate blacklist", () =>
                        Send("/station jumpgate-blacklist " + orbitId + " add " + Value(typeKey) + " " + Value(targetKey) +
                            (string.IsNullOrWhiteSpace(Value(reasonKey)) ? "" : " | " + Value(reasonKey))));
                    AddAction("Remove from gate blacklist", () =>
                        Send("/station jumpgate-blacklist " + orbitId + " remove " + Value(typeKey) + " " + Value(targetKey)));
                    if (Modding.ModPlayerInfrastructure.Current.JumpgateRacePoliciesEnabled)
                    {
                        string raceKey = "gateRace_" + orbitId;
                        string raceStateKey = "gateRaceState_" + orbitId;
                        AddField(raceKey, "Race · name or qualified mod ID", "");
                        AddChoice(raceStateKey, "Race access", new List<(string, string)>
                        {
                            ("inherit", "Inherit faction policy"),
                            ("allow", "Allow under race policy"),
                            ("deny", "Deny under race policy")
                        });
                        string raceReasonKey = "gateRaceReason_" + orbitId;
                        AddField(raceReasonKey, "Race denial reason (optional)", "");
                        AddAction("Apply gate race rule", () =>
                            Send(("/station jumpgate-race " + orbitId + " " + Value(raceKey) + " " + Value(raceStateKey) + " " + Value(raceReasonKey)).TrimEnd()));
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

        void AddItemChoice(string key, string label, HashSet<int> allowed = null)
        {
            var items = NetGame.Db?.Items;
            var all = items == null ? new List<ItemData>() : items
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.name) &&
                    (allowed == null || allowed.Contains(item.index)))
                .OrderBy(item => item.name, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.index).ToList();
            var names = all.GroupBy(item => item.name).ToDictionary(group => group.Key, group => group.Count());
            var options = all.Select(item => (value: item.index.ToString(), label: names[item.name] == 1 ? item.name :
                item.name + " (" + item.type + ", " + (all.Where(other => other.name == item.name).ToList().IndexOf(item) + 1) + ")")).ToList();
            var search = new TextField("Search " + label.ToLowerInvariant()) { value = Value(key + "Search") };
            search.AddToClassList("player-station-field");
            body.Add(search);
            var field = new DropdownField(label);
            field.AddToClassList("player-station-field");
            body.Add(field);
            var preview = new VisualElement { pickingMode = PickingMode.Ignore };
            preview.style.width = 90;
            preview.style.height = 44;
            body.Add(preview);
            var visible = new List<(string value, string label)>();
            void RefreshPreview()
            {
                preview.style.backgroundImage = int.TryParse(Value(key), out int item)
                    ? new StyleBackground(ItemInfo.ItemIcon(item)) : new StyleBackground();
            }
            void Filter(string query)
            {
                visible = new List<(string value, string label)> { ("", "Select an item") };
                visible.AddRange(options.Where(option => string.IsNullOrWhiteSpace(query) ||
                    option.label.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0));
                int index = Math.Max(0, visible.FindIndex(option => option.value == Value(key)));
                field.choices = visible.Select(option => option.label).ToList();
                field.SetValueWithoutNotify(visible[index].label);
                drafts[key] = visible[index].value;
                RefreshPreview();
            }
            field.RegisterValueChangedCallback(_ =>
            {
                if (field.index < 0 || field.index >= visible.Count) return;
                drafts[key] = visible[field.index].value;
                RefreshPreview();
            });
            search.RegisterValueChangedCallback(change =>
            {
                drafts[key + "Search"] = change.newValue;
                Filter(change.newValue);
            });
            Filter(search.value);
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

        static List<(string value, string label, string race)> StationModelChoices()
        {
            var db = NetGame.Db;
            var models = db.Stations.Where(station => station != null && NetPlayerStations.IsStationModelPreset(station.index))
                .Select(station => (station, race: db.Systems.Find(system => system != null && system.index == station.system)?.raceId ?? -1))
                .OrderBy(entry => entry.race).ThenBy(entry => entry.station.index);
            var result = new List<(string, string, string)>();
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
                result.Add((entry.station.index.ToString(), $"{race} {number} · {entry.station.name} [#{entry.station.index}]", race));
            }
            return result;
        }

        void AddStationModelPicker(string key)
        {
            var models = StationModelChoices();
            if (models.Count == 0)
            {
                drafts.Remove(key);
                AddInfo("Station model", "No station models are configured.");
                return;
            }

            var chosen = models.Find(model => model.value == Value(key));
            if (chosen.value == null) drafts.Remove(key);
            AddAction(chosen.value == null ? "Choose station model" : "Station model: " + chosen.label,
                () => OpenModelPicker(key, models));
            if (!int.TryParse(chosen.value, out int index)) return;
            var image = Modding.ModPlayerInfrastructure.StationModelImage(index);
            if (image.texture != null)
            {
                var preview = new VisualElement();
                preview.AddToClassList("player-station-model-preview");
                preview.style.backgroundImage = new StyleBackground(image.texture);
                body.Add(preview);
            }
            if (!string.IsNullOrEmpty(image.id)) AddInfo("Image ID", image.id);
        }

        void OpenModelPicker(string key, List<(string value, string label, string race)> models)
        {
            modelPickerKey = key;
            pageBar.style.display = DisplayStyle.None;
            modelPickerReturnScroll = body.scrollOffset;
            panel.AddToClassList("player-station-picking-model");
            heading.text = "CHOOSE STATION MODEL";
            string selectedModel = Value(key);
            body.Clear();
            fields.Clear();
            var races = new List<string> { "All races" }; races.AddRange(models.Select(model => model.race).Distinct().OrderBy(race => race));
            var raceFilter = new DropdownField("Filter by race", races, 0);
            raceFilter.AddToClassList("player-station-field"); body.Add(raceFilter);
            var results = new VisualElement();
            body.Add(results);

            void ShowMatches(string query)
            {
                results.Clear();
                foreach (var group in models.Where(model => query == "All races" || model.race == query).GroupBy(model => model.race))
                {
                    var label = new Label(group.Key);
                    label.AddToClassList("player-station-section-title");
                    results.Add(label);
                    foreach (var model in group)
                    {
                        var button = new Button(() =>
                        {
                            drafts[key] = model.value;
                            CloseModelPicker();
                        }) { text = model.label };
                        button.AddToClassList("player-station-row");
                        button.EnableInClassList("player-station-row--selected", model.value == selectedModel);
                        button.clicked += () => menu?.PlayRelease();
                        results.Add(button);
                    }
                }
                if (results.childCount == 0)
                {
                    var empty = new Label("No station models match your search.");
                    empty.AddToClassList("player-station-note");
                    results.Add(empty);
                }
            }

            raceFilter.RegisterValueChangedCallback(change => ShowMatches(change.newValue));
            ShowMatches("All races");
            body.scrollOffset = Vector2.zero;
            if (InputMode.Current != InputKind.Touch && !InputMode.MousePointing)
                results.Q<Button>()?.Focus();
        }

        void CloseModelPicker()
        {
            modelPickerKey = null;
            pageBar.style.display = DisplayStyle.Flex;
            panel.RemoveFromClassList("player-station-picking-model");
            heading.text = "PLAYER STATIONS";
            RebuildBody(modelPickerReturnScroll);
            if (InputMode.Current != InputKind.Touch && !InputMode.MousePointing)
                body.Query<Button>().ToList().Find(button => button.text.StartsWith("Station model:", StringComparison.Ordinal) ||
                    button.text == "Choose station model")?.Focus();
        }

        static List<(string value, string label)> GateModelChoices() =>
            NetPlayerStations.JumpgateModelChoices().Select(model => (model.id.ToString(), model.label)).ToList();

        void ChangeManagementPage(string page)
        {
            if (managementPage == page) return;
            managementPage = page;
            if (page == "market") RefreshMarketCatalogue();
            if (page == "security" || page == "ownership" || page == "gates" || page == "factory" || page == "hangar" || page == "drydock") RefreshManagementCatalogue();
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
                AddChoice("system", "Revealed build system", choices, _ => { drafts.Remove("buildOrbit"); RebuildBody(); });
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

        void BuildStationConstruction(PlayerStationRecord project)
        {
            string id = project.id.ToString();
            AddSection("SELECTED STATION PROJECT", "Deliver materials directly to this construction project.");
            AddInfo("Project", project.name);
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

        void BuildVisitorMarket(string id)
        {
            AddSection("MARKETPLACE", "");
            AddAction("View item and hull listings", "/station market " + id);
            BuildMarketCatalogue(id);
        }

        void RefreshMarketCatalogue()
        {
            nextMarketQuery = Time.unscaledTime + 8f;
            if (inFlight || selected == null || selected.underConstruction || selected.stationIndex != CurrentOrbit ||
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
            if (inFlight) { AddInfo("Listings", "Dock at the station to browse and purchase marketplace goods."); return; }
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
            AddItemChoice("visitorStoredItem", "Stored item");
            AddField("visitorStoredQuantity", "Units", "1");
            AddAction("Withdraw own items", () => Send("/station withdraw " + id + " " +
                Value("visitorStoredItem") + " " + Value("visitorStoredQuantity")));
        }

        void BuildStationStock()
        {
            AddSection("DONATED CONSTRUCTION STOCK", "");
            AddAction("View construction stock", "/station faction-materials");
            AddItemChoice("stockItem", "Material");
            AddField("stockQuantity", "Units", "1");
            AddAction("Donate from cargo", () => Send("/station faction-donate " + Value("stockItem") + " " + Value("stockQuantity")));
        }

        void BuildStationGovernance(PlayerStationRecord station)
        {
            string id = station.id.ToString();
            AddSection("APPEARANCE AND GOVERNANCE", "");
            AddStationModelPicker("stationAppearance");
            if (!string.IsNullOrEmpty(Value("stationAppearance")))
                AddAction("Change station model", () => Send("/station appearance " + id + " " + Value("stationAppearance")));
            AddAction("View station history", "/station history " + id);
            if (Modding.ModPlayerInfrastructure.Current.StationRacePoliciesEnabled)
            {
                AddSection("RACE POLICIES", "");
                AddChoice("raceFacility", "Facility", NetPlayerStations.RacePolicyFacilities.Select(value => (value, value)).ToList());
                AddField("stationPolicyRace", "Race name or qualified mod ID", "");
                AddChoice("racePolicyState", "Race access", new List<(string, string)>
                {
                    ("inherit", "Inherit faction policy"), ("allow", "Allow under race policy"), ("deny", "Deny under race policy")
                });
                AddField("raceDenialReason", "Race denial reason (optional)", "");
                AddAction("Apply station race rule", () => Send(("/station race-policy " + id + " " +
                    Value("raceFacility") + " " + Value("stationPolicyRace") + " " + Value("racePolicyState") + " " + Value("raceDenialReason")).TrimEnd()));
                if (Modding.ModPlayerInfrastructure.Current.StationRaceMarketLimitPercent > 0)
                {
                    AddChoice("raceMarketAction", "Price adjustment", new List<(string, string)>
                        { ("purchase", "Purchase surcharge or tax discount"), ("sale", "Seller proceeds deduction") });
                    AddField("raceMarketPercent", "Percent or inherit", "0");
                    AddAction("Apply race price adjustment", () => Send("/station race-policy " + id + " " +
                        Value("raceMarketAction") + " " + Value("stationPolicyRace") + " " + Value("raceMarketPercent")));
                }
                AddAction("View station and faction race rules", "/station race-policy " + id);
                if (Modding.ModPlayerInfrastructure.Current.StationRaceExemptionsEnabled)
                {
                    AddAccountField("stationRaceExemption", "Race exemption target");
                    AddAction("Grant race exemption", () => Send("/station race-policy " + id + " exempt add " + Value("stationRaceExemption")));
                    AddAction("Revoke race exemption", () => Send("/station race-policy " + id + " exempt remove " + Value("stationRaceExemption")));
                }
            }
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
            AddField("banReason", "Reason shown to denied pilot (optional)", "");
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
            button.clicked += () => menu?.PlayRelease();
            button.RegisterCallback<PointerDownEvent>(_ => menu?.PlayPush(), TrickleDown.TrickleDown);
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
            if (string.IsNullOrWhiteSpace(command) || command.TrimEnd() != command || command.Contains("  "))
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
                nextManagementQuery = 0;
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
