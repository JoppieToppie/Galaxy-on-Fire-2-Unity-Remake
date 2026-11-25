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
        bool marketplaceOnly;
        string choicePickerKey;
        Vector2 choiceReturnScroll;
        int bodyRevision;
        Vector2 modelPickerReturnScroll;
        int listingItem;
        GameObject placementRoot;
        RenderTexture placementTexture;
        bool placementDragging;
        float nextPlacementRefresh;
        string placementSnapshot;
        int placementOrbit;
        readonly Dictionary<string, Vector3Int> placementDrafts = new Dictionary<string, Vector3Int>();
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
            compact = small;
            panel.EnableInClassList("player-station-compact", small);
            stationList.mode = small ? ScrollViewMode.Horizontal : ScrollViewMode.Vertical;
            stationList.contentContainer.style.flexDirection = small ? FlexDirection.Row : FlexDirection.Column;
            pageBar.style.flexWrap = Wrap.Wrap;
            body.style.flexGrow = 1; body.style.flexBasis = 0; body.style.minHeight = 0; body.style.minWidth = 0;
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
            if (!marketplaceOnly && Time.unscaledTime >= nextPlannerQuery && Modding.ModPlayerInfrastructure.Current.Enabled) RefreshPlannerLocations();
            if (feedbackTime > 0 && (feedbackTime -= Time.unscaledDeltaTime) <= 0) feedback.text = "";
            if (placementRoot != null)
            {
                if (!placementDragging && Time.unscaledTime >= nextPlacementRefresh)
                {
                    nextPlacementRefresh = Time.unscaledTime + 1f;
                    var live = NetPlayerStations.StationAt(CurrentOrbit);
                    if (live?.id == selected?.id && PlacementSnapshotOf(live, placementOrbit) != placementSnapshot)
                    {
                        selected = live;
                        RebuildBody();
                    }
                }
                return;
            }
            if (StarMap.IsOpen || modelPickerKey != null || choicePickerKey != null || Time.unscaledTime - pressedAt < 5f || TextFieldKeys.InTextField(panel.focusController?.focusedElement)) return;
            if ((refresh -= Time.unscaledDeltaTime) <= 0)
            {
                refresh = 2f;
                RebuildStationList();
                RebuildBody(); // Keep public procurement quantities and escrow current while the window remains open.
            }
        }

        internal void Open() => Open(marketplace: false);
        internal void OpenListing(int item)
        {
            if (open) return;
            listingItem = item;
            Open(marketplace: true);
        }

        void Open(bool marketplace)
        {
            if (!CanOpen || open) return;
            marketplaceOnly = marketplace;
            stationList.style.display = marketplace ? DisplayStyle.None : StyleKeyword.Null;
            heading.text = marketplace ? "CREATE LISTING" : "PLAYER STATIONS";
            menu?.CloseHangar();
            overlay.style.display = DisplayStyle.Flex;
            overlay.BringToFront();
            open = true;
            managementPage = marketplaceOnly ? "market" : "overview";
            refresh = 0;
            plannerOrbits = null;
            plannerRequestPending = false;
            if (!marketplaceOnly) RefreshPlannerLocations();
            RebuildStationList();
            managementCatalogue = null; directoryPage = 0; nextManagementQuery = 0;
            RefreshManagementCatalogue();
            RebuildBody();
            if (InputMode.Current != InputKind.Touch && !InputMode.MousePointing) closeButton.Focus();
        }

        internal void Back()
        {
            if (choicePickerKey != null) CloseChoicePicker();
            else if (modelPickerKey != null) CloseModelPicker();
            else if (marketplaceOnly) { Close(); menu?.OpenOutfitting(); }
            else if (managementPage != "overview") ChangeManagementPage("overview");
            else Close();
        }

        public void Close()
        {
            if (!open) return;
            DestroyPlacementPreview();
            placementDrafts.Clear();
            overlay.style.display = DisplayStyle.None;
            open = false;
            marketplaceOnly = false;
            if (panel.focusController?.focusedElement is VisualElement focused && panel.Contains(focused)) focused.Blur();
            if (plannerMap != null)
            {
                stationRoot.RemoveFromClassList("station-map-open");
                if (StarMap.Current == plannerMap) plannerMap.CloseBuilder();
                plannerMap = null;
            }
            choicePickerKey = null;
            modelPickerKey = null;
            panel.RemoveFromClassList("player-station-picking-model");
            heading.text = "PLAYER STATIONS";
            confirmRetirementId = -1;
            confirmOrbitalRemoval = 0;
            drafts.Clear();
            plannerOrbits = null;
            plannerRequestPending = false;
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
            if (marketplaceOnly)
            {
                selected = NetPlayerStations.StationAt(CurrentOrbit);
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
            if (choicePickerKey != null || modelPickerKey != null) return;
            DestroyPlacementPreview();
            int revision = ++bodyRevision;
            var before = scrollPosition ?? body.scrollOffset;
            var focused = panel.focusController?.focusedElement as VisualElement;
            string focusLabel = (focused as Button)?.text;
            body.Clear(); fields.Clear();
            body.schedule.Execute(() =>
            {
                if (revision != bodyRevision || choicePickerKey != null || modelPickerKey != null) return;
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

            if (marketplaceOnly)
            {
                if (selected == null || selected.underConstruction || selected.offeredForClaimUtc > 0)
                    AddInfo("Marketplace", "The marketplace is closed.");
                else BuildListingForm(selected.id.ToString());
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
                AddDirectoryPager();
                BuildSystemGates(system);
                if (selected != null && !selected.underConstruction && managementCatalogue?.canConstruction == true &&
                    Modding.ModPlayerInfrastructure.Current.JumpgatesEnabled) BuildNewJumpgateControls(selected.id.ToString());
                return;
            }
            if (selected == null) { AddSection("STATION OVERVIEW", "Select a station or begin a construction project."); return; }
            string id = selected.id.ToString();
            bool operational = !selected.underConstruction && selected.offeredForClaimUtc == 0;
            bool docked = !inFlight && selected.stationIndex == CurrentOrbit;
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
                    if (selected.stationBuildCompletesUtc > 0)
                        AddInfo("Build time remaining", ConstructionTime(Math.Max(1, selected.stationBuildCompletesUtc - DateTimeOffset.UtcNow.ToUnixTimeSeconds())));
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
                        AddChoice("transferKind", "Recipient type", new List<(string, string)> { ("player", "Individual pilot"), ("faction", "Faction") }, _ => drafts.Remove("transferAccount"));
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
                case "security": case "blacklist": case "race-policies": BuildStationGovernance(selected); break;
                case "modules":
                    AddSection("STATION MODULES", "Install facilities from cargo or open a procurement project.");
                    AddAction("Module costs and requirements", "/station modules");
                    AddAction("Open 3D orbit placement editor", () => ChangeManagementPage("placement"));
                    foreach (string module in new[] { "storage", "refinery", "factory", "hangar", "drydock", "dockarm" })
                    {
                        if (HasModule(module)) { AddInfo(ModuleLabel(module), "Installed"); continue; }
                        if (selected.moduleConstructionKind == module && selected.moduleBuildCompletesUtc > 0)
                        {
                            AddInfo(ModuleLabel(module), "Building · " + ConstructionTime(Math.Max(1, selected.moduleBuildCompletesUtc - DateTimeOffset.UtcNow.ToUnixTimeSeconds())));
                            continue;
                        }
                        int duration = Modding.ModPlayerInfrastructure.Current.ModuleBuildSeconds(module);
                        if (duration > 0) AddInfo(ModuleLabel(module) + " build time", ConstructionTime(duration));
                        AddAction("Install " + ModuleLabel(module) + " from cargo", "/station install " + id + " " + module);
                        AddAction("Install " + ModuleLabel(module) + " from your storage", "/station install-storage " + id + " " + module);
                        if (string.IsNullOrEmpty(selected.moduleConstructionKind)) AddAction("Open " + ModuleLabel(module) + " project", "/station module-project " + id + " " + module);
                    }
                    if (!string.IsNullOrEmpty(selected.moduleConstructionKind)) AddAction("Manage active module procurement", () => ChangeManagementPage("orders"));
                    if (NetPlayer.Local?.CanUseStationStockInUi == true) BuildStationStock();
                    break;
                case "defenses":
                    AddAction("Open 3D orbit placement editor", () => ChangeManagementPage("placement"));
                    BuildOrbitalStructureControls(id);
                    break;
                case "placement": BuildOrbitPlacement(); break;
                case "market": BuildMarketSettings(id); break;
                case "refinery": case "factory": BuildFacilityControls(id); break;
                case "hangar": case "drydock": BuildFacilityControls(id); break;
                default: ChangeManagementPage("overview"); break;
            }
        }

        void RebuildPageBar()
        {
            if (marketplaceOnly)
            {
                pageBar.style.display = DisplayStyle.None;
                return;
            }
            var pages = new List<(string key, string label)> { ("overview", "Overview") };
            if (Modding.ModPlayerInfrastructure.Current.Enabled) pages.Add(("build", "Build station"));
            if (LocalDeliveryProject() != null) pages.Add(("contribute", "Contribute"));
            pages.Add(("orders", "Buy orders"));
            if (inFlight) pages.Add(("orbit", "Orbit projects"));
            if (Modding.ModPlayerInfrastructure.Current.JumpgatesEnabled) pages.Add(("gates", "Jumpgates"));
            if (selected != null && !selected.underConstruction && selected.offeredForClaimUtc <= 0 && !inFlight && selected.stationIndex == CurrentOrbit)
            {
                bool HasModule(string kind) => selected.addons?.Any(addon => addon != null && addon.kind == kind && addon.level > 0) == true;
                pages.Add(("news", "News"));
                if (managementCatalogue?.canManageMarket == true || managementCatalogue?.canSecurity == true)
                    pages.Add(("market", "Marketplace policy"));
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
                if (managementCatalogue?.canSecurity == true || managementCatalogue?.canOwnership == true)
                {
                    pages.Add(("security", "Permissions"));
                    pages.Add(("blacklist", "Blacklist"));
                }
                if ((managementCatalogue?.canOwnership == true || managementCatalogue?.canManageMarket == true) &&
                    Modding.ModPlayerInfrastructure.Current.StationRacePoliciesEnabled)
                    pages.Add(("race-policies", "Race policies"));
                if (managementCatalogue?.canConstruction == true) { pages.Add(("modules", "Modules")); pages.Add(("defenses", "Defenses")); pages.Add(("placement", "Orbit editor")); }
            }
            if (!pages.Any(page => page.key == managementPage)) managementPage = "overview";
            string Group(string key) => key switch
            {
                "overview" or "news" => "Station",
                "hangar" or "drydock" or "refinery" or "factory" => "Services",
                "ownership" or "security" or "blacklist" or "race-policies" or "market" or "modules" => "Management",
                _ => "Projects"
            };
            string activeGroup = Group(managementPage);
            string signature = managementPage + ":" + string.Join(",", pages.Select(page => page.key + page.label));
            pageBar.style.display = modelPickerKey == null ? DisplayStyle.Flex : DisplayStyle.None;
            if (signature == pageBarSignature) return;
            pageBarSignature = signature;
            pageBar.Clear();
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
                var button = new Button(() => ChangeManagementPage(key)) { text = title };
                button.AddToClassList("player-station-action");
                button.EnableInClassList("player-station-row--selected", active);
                button.style.minHeight = 36;
                button.style.marginRight = 4;
                row.Add(button);
            }
            var groups = NavigationRow();
            foreach (var group in pages.GroupBy(page => Group(page.key)))
                Tab(groups, group.Key, group.First().key, group.Key == activeGroup);

            var visiblePages = pages.Where(page => Group(page.key) == activeGroup).ToList();
            if (visiblePages.Count > 1)
            {
                var tabs = NavigationRow();
                foreach (var page in visiblePages)
                    Tab(tabs, page.label, page.key, managementPage == page.key);
            }
        }

        void RefreshManagementCatalogue()
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
                    Send("/station jumpgate " + id + " " + orbit + " " + destination + " " + model);
                });
            }

        }

        void DestroyPlacementPreview()
        {
            if (placementRoot != null)
            {
                placementRoot.SetActive(false);
                UnityEngine.Object.Destroy(placementRoot);
            }
            placementRoot = null;
            placementDragging = false;
            if (placementTexture != null)
            {
                placementTexture.Release();
                UnityEngine.Object.Destroy(placementTexture);
            }
            placementTexture = null;
        }

        static string PlacementSnapshotOf(PlayerStationRecord station, int orbit)
        {
            if (station == null) return "";
            var modules = station.placements?.Where(p => p != null)
                .Select(p => $"{p.kind}:{p.preset}:{p.x}:{p.y}:{p.z}") ?? Enumerable.Empty<string>();
            var orbital = NetPlayerStations.OrbitalStructures?.Where(p => p != null && p.orbit == orbit)
                .Select(p => $"{p.id}:{p.operational}:{p.placementX}:{p.placementY}:{p.placementZ}") ?? Enumerable.Empty<string>();
            return string.Join("|", modules.Concat(orbital));
        }

        void BuildOrbitPlacement()
        {
            var db = NetGame.Db;
            var station = selected;
            var types = new List<(string, string)> { ("orbital", "Orbital defense or other structure"), ("refinery", "Refinery"), ("drydock", "Dry dock") };
            for (int i = 1; i <= 4; i++) types.Add(("dockarm-" + i, "Docking arm " + i));
            AddChoice("placementType", "Project", types, _ => drafts.Remove("placementPreset"));
            string kind = Value("placementType");
            bool orbital = kind == "orbital";
            var system = db.Systems.Find(entry => entry.index == station.system);
            int previewOrbit = station.stationIndex;
            if (orbital && system?.stations != null)
            {
                var orbits = db.Stations.Where(entry => entry != null && system.stations.Contains(entry.index))
                    .OrderBy(entry => entry.index != station.stationIndex).ThenBy(entry => entry.name)
                    .Select(entry => (entry.index.ToString(), entry.name)).ToList();
                AddChoice("placementOrbit", "Target orbit", orbits);
                if (int.TryParse(Value("placementOrbit"), out int chosen)) previewOrbit = chosen;
            }
            placementOrbit = previewOrbit;
            placementSnapshot = PlacementSnapshotOf(station, previewOrbit);
            var layout = OrbitLayout.Build(db, previewOrbit);
            if (layout == null) { AddInfo("Orbit editor", "The orbit preview is unavailable."); return; }
            int race = system?.raceId ?? -1;
            var settings = Modding.ModPlayerInfrastructure.Current;
            var presets = (orbital ? settings.OrbitalStructures.Where(preset =>
                NetPlayerStations.OrbitalPresetAvailable(preset, race) &&
                (preset.Defense ? settings.OrbitalDefenseLimit : settings.OtherOrbitalStructureLimit) > 0) :
                NetPlayerStations.ModuleExteriorChoices(station, kind)).ToList();
            var existing = station.placements?.Find(entry => entry?.kind == kind);
            if (!drafts.ContainsKey("placementPreset") && existing != null) drafts["placementPreset"] = existing.preset;
            AddChoice("placementPreset", "Exterior model", presets.Select(preset => (preset.Id, preset.Name)).ToList());
            var preset = presets.Find(value => value.Id == Value("placementPreset"));
            if (preset == null) return;

            Vector3 stationPosition = NetPlayerStations.StationPosition(previewOrbit);
            string draftKey = station.id + ":" + previewOrbit + ":" + kind;
            if (!placementDrafts.TryGetValue(draftKey, out Vector3Int position))
                position = existing != null ? new Vector3Int(existing.x, existing.y, existing.z) :
                    new Vector3Int(0, 0, orbital ? 8000 : Mathf.Clamp(Mathf.RoundToInt(layout.stationReach * OrbitLayout.MetersPerUnit * layout.StationScale), 300, 9000));

            AddSection("ORBIT PLACEMENT", "Drag the marker or its arrows. Drag empty space to rotate, scroll to zoom, and hold Ctrl for snapping.");
            var viewport = new VisualElement();
            viewport.style.height = compact ? 270 : 360;
            viewport.style.flexShrink = 0;
            body.Add(viewport);
            var coordinateLabel = new Label();
            coordinateLabel.AddToClassList("player-station-note");
            body.Add(coordinateLabel);
            AddAction(orbital ? "Order construction here" : "Save planned position", () =>
            {
                Vector3Int point = orbital ? Vector3Int.RoundToInt(stationPosition) + position : position;
                string command = orbital ? $"/station orbital-place {station.id} {previewOrbit}" : $"/station place {station.id} {kind}";
                Send($"{command} {point.x} {point.y} {point.z} {preset.Id}");
            });

            const int previewLayer = 31;
            placementRoot = new GameObject("Station placement preview");
            placementRoot.transform.position = new Vector3(0, -1000000f, 0);
            var frame = new GameObject("Placement coordinates").transform;
            frame.SetParent(placementRoot.transform, false);
            var model = OrbitBuilder.SpawnStation(db, layout, frame);
            if (model != null) model.transform.localPosition = Vector3.zero;
            var ghost = OrbitBuilder.Spawn(db, NetPlayerStations.OrbitalStructureAssembly(preset.Assembly, preset.StationModel, db),
                Vector3.zero, Quaternion.identity, "Proposed structure", frame);
            if (ghost != null) ghost.transform.localScale *= preset.ModelScale;
            void SetLayer(Transform rootTransform)
            {
                foreach (var part in rootTransform.GetComponentsInChildren<Transform>(true))
                    if (part.gameObject.layer != ModBuilderGizmo.PickLayer) part.gameObject.layer = previewLayer;
            }
            SetLayer(placementRoot.transform);
            foreach (var collider in frame.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            var cameraObject = new GameObject("Placement camera");
            cameraObject.transform.SetParent(placementRoot.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.025f, .045f, .07f);
            camera.cullingMask = 1 << previewLayer;
            camera.nearClipPlane = 1;
            camera.farClipPlane = 240000;
            placementTexture = new RenderTexture(1024, 512, 24);
            placementTexture.Create();
            camera.targetTexture = placementTexture;
            viewport.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(placementTexture));
            var lightObject = new GameObject("Placement light");
            lightObject.transform.SetParent(placementRoot.transform, false);
            lightObject.transform.rotation = Quaternion.Euler(35, -45, 0);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.cullingMask = 1 << previewLayer;
            light.intensity = 1.4f;

            int[] Units(Vector3 point) => new[] { Mathf.RoundToInt(-point.x / OrbitLayout.MetersPerUnit),
                Mathf.RoundToInt(point.y / OrbitLayout.MetersPerUnit), Mathf.RoundToInt(point.z / OrbitLayout.MetersPerUnit) };
            var mounts = new List<WeaponMount>();
            foreach (var entry in station.placements ?? new List<PlayerStationPlacement>())
                if (previewOrbit == station.stationIndex && entry != null && entry.kind != kind)
                    mounts.Add(new WeaponMount { slotType = 8, position_engine = Units(new Vector3(entry.x, entry.y, entry.z)) });
            if (NetPlayerStations.OrbitalStructures != null)
                foreach (var structure in NetPlayerStations.OrbitalStructures)
                    if (structure != null && structure.orbit == previewOrbit)
                        mounts.Add(new WeaponMount { slotType = 2, position_engine = Units(NetPlayerStations.OrbitalStructurePosition(structure) - stationPosition) });
            int active = mounts.Count;
            mounts.Add(new WeaponMount { slotType = orbital ? 2 : 8, position_engine = Units(position) });
            var handles = new GameObject("Placement handles");
            handles.transform.SetParent(placementRoot.transform, false);
            var gizmo = handles.AddComponent<ModBuilderGizmo>();
            gizmo.cam = camera;
            gizmo.Selected = active;
            gizmo.Bind(frame, mounts);
            SetLayer(handles.transform);
            float distance = Mathf.Max(1800f, position.magnitude * 1.8f);
            float yaw = 34, pitch = 29;
            void SetCamera()
            {
                camera.transform.position = frame.position + Quaternion.Euler(pitch, yaw, 0) * new Vector3(0, 0, -distance);
                camera.transform.LookAt(frame.position);
            }
            void UpdatePoint()
            {
                position = Vector3Int.RoundToInt(frame.InverseTransformPoint(gizmo.WorldOf(mounts[active])));
                placementDrafts[draftKey] = position;
                if (ghost != null) ghost.transform.localPosition = position;
                coordinateLabel.text = $"X {position.x:N0} m   Y {position.y:N0} m   Z {position.z:N0} m";
            }
            Vector2 ScreenPoint(Vector2 panelPosition)
            {
                Vector2 p = viewport.WorldToLocal(panelPosition);
                return new Vector2(p.x / viewport.layout.width * camera.pixelWidth,
                    (1f - p.y / viewport.layout.height) * camera.pixelHeight);
            }
            SetCamera();
            UpdatePoint();
            bool orbiting = false, movingInPlane = false;
            Plane dragPlane = default;
            Vector3 grabOffset = Vector3.zero;
            viewport.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                var point = ScreenPoint(evt.position);
                bool axis = gizmo.PickAxis(point) >= 0;
                bool marker = gizmo.PickMarker(point) == active;
                if (orbital && marker && !axis)
                {
                    var markerWorld = gizmo.WorldOf(mounts[active]);
                    dragPlane = new Plane(camera.transform.forward, markerWorld);
                    var ray = camera.ScreenPointToRay(point);
                    movingInPlane = dragPlane.Raycast(ray, out float hit);
                    if (movingInPlane) grabOffset = markerWorld - ray.GetPoint(hit);
                }
                orbiting = !movingInPlane && (!(axis || marker) || !gizmo.BeginDrag(point));
                placementDragging = true;
                viewport.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            viewport.RegisterCallback<PointerMoveEvent>(evt =>
            {
                var point = ScreenPoint(evt.position);
                if (movingInPlane)
                {
                    var ray = camera.ScreenPointToRay(point);
                    if (dragPlane.Raycast(ray, out float hit))
                    {
                        Vector3 desired = frame.InverseTransformPoint(ray.GetPoint(hit) + grabOffset);
                        if (evt.ctrlKey) desired = new Vector3(Mathf.Round(desired.x / 100f), Mathf.Round(desired.y / 100f), Mathf.Round(desired.z / 100f)) * 100f;
                        mounts[active].position_engine = Units(desired);
                        UpdatePoint();
                    }
                }
                else if (gizmo.Dragging)
                {
                    gizmo.Drag(point, evt.ctrlKey, Mathf.RoundToInt(100f / OrbitLayout.MetersPerUnit));
                    UpdatePoint();
                }
                else if (orbiting)
                {
                    yaw += evt.deltaPosition.x * .4f;
                    pitch = Mathf.Clamp(pitch - evt.deltaPosition.y * .4f, -80, 80);
                    SetCamera();
                }
                else gizmo.Hover(point);
            });
            viewport.RegisterCallback<PointerUpEvent>(evt =>
            {
                gizmo.EndDrag();
                orbiting = movingInPlane = placementDragging = false;
                if (viewport.HasPointerCapture(evt.pointerId)) viewport.ReleasePointer(evt.pointerId);
            });
            viewport.RegisterCallback<PointerCaptureOutEvent>(_ => { gizmo.EndDrag(); orbiting = movingInPlane = placementDragging = false; });
            viewport.RegisterCallback<WheelEvent>(evt =>
            {
                distance = Mathf.Clamp(distance * Mathf.Pow(1.12f, evt.delta.y), 300, 170000);
                SetCamera();
                evt.StopPropagation();
            });
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
            row.style.flexWrap = Wrap.Wrap; row.style.minHeight = 32; row.style.flexShrink = 0;
            var name = new Label(label); name.style.whiteSpace = WhiteSpace.Normal; name.style.flexShrink = 1;
            var detail = new Label(value ?? ""); detail.enableRichText = false; detail.style.whiteSpace = WhiteSpace.Normal;
            detail.style.flexShrink = 1; detail.style.minWidth = 0; detail.style.flexGrow = 1;
            row.Add(name); row.Add(detail); body.Add(row);
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
                    if (!drafts.ContainsKey(modeKey)) drafts[modeKey] = access;
                    AddChoice(modeKey, "Access policy", new List<(string, string)> { ("everyone", "Everyone"), ("faction", "Owning faction"), ("blacklist", "Blacklist"), ("owner", "Owner only") });
                    AddAction("Set gate access · orbit " + orbitId, () =>
                        Send("/station jumpgate-access " + orbitId + " " + Value(modeKey)));
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
            var options = new List<(string value, string label)> { ("", "Select an item") };
            options.AddRange(all.Select(item => (item.index.ToString(), names[item.name] == 1 ? item.name :
                item.name + " (" + item.type + ", " + (all.Where(other => other.name == item.name).ToList().IndexOf(item) + 1) + ")")));
            AddChoice(key, label, options, null, true);
        }

        static void StyleChoice(Button button, bool selected, bool focused = false)
        {
            var border = focused ? Color.white : selected ? new Color(.52f,.87f,1f) : new Color(.20f,.34f,.44f);
            button.style.backgroundColor = selected ? new Color(.12f,.29f,.39f) : new Color(.045f,.10f,.15f);
            button.style.borderTopColor = button.style.borderBottomColor = button.style.borderLeftColor = button.style.borderRightColor = border;
            button.style.borderTopWidth = button.style.borderBottomWidth = button.style.borderLeftWidth = button.style.borderRightWidth = 2;
            button.style.color = Color.white;
            button.style.whiteSpace = WhiteSpace.Normal;
            button.style.minHeight = 48; button.style.height = StyleKeyword.Auto; button.style.flexShrink = 0;
            button.style.marginBottom = 6;
            button.style.alignSelf = Align.Stretch;
            button.style.minWidth = 0;
            button.style.maxWidth = StyleKeyword.None;
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
        }

        void AddChoice(string key, string label, List<(string value, string label)> options, Action<string> changed = null, bool items = false)
        {
            if (options.Count == 0) { drafts.Remove(key); AddInfo(label, "No choices available."); return; }
            int index = drafts.TryGetValue(key, out string saved) ? options.FindIndex(option => option.value == saved) : -1;
            index = Math.Max(0, index);
            drafts[key] = options[index].value;
            var caption = new Label(label); caption.AddToClassList("player-station-note"); caption.style.whiteSpace = WhiteSpace.Normal; body.Add(caption);
            var button = new Button { text = options[index].label + "  ▾", name = "choice-" + key, enableRichText = false };
            button.AddToClassList("player-station-field"); button.AddToClassList("player-station-action");
            StyleChoice(button, false);
            button.clicked += () => OpenChoicePicker(key, label, options, changed, items);
            body.Add(button);
            if (items && int.TryParse(drafts[key], out int item))
            {
                var preview = new VisualElement { pickingMode = PickingMode.Ignore };
                preview.style.width = 90; preview.style.height = 44;
                preview.style.backgroundImage = new StyleBackground(ItemInfo.ItemIcon(item)); body.Add(preview);
            }
        }

        void OpenChoicePicker(string key, string label, List<(string value, string label)> options, Action<string> changed, bool items)
        {
            DestroyPlacementPreview();
            choicePickerKey = key;
            choiceReturnScroll = body.scrollOffset;
            heading.text = label.ToUpperInvariant();
            pageBar.style.display = DisplayStyle.None;
            panel.AddToClassList("player-station-picking-model");
            body.Clear(); fields.Clear();
            var toolbar = new VisualElement(); toolbar.style.flexDirection = FlexDirection.Row; toolbar.style.flexWrap = Wrap.Wrap; body.Add(toolbar);
            var results = new VisualElement(); body.Add(results);
            const int pageSize = 24;
            int page = Math.Max(0, options.FindIndex(option => option.value == Value(key))) / pageSize;
            int pages = Math.Max(1, (options.Count + pageSize - 1) / pageSize);
            void Show()
            {
                toolbar.Clear(); results.Clear();
                void Tool(string text, Action action, bool enabled)
                {
                    var b = new Button(action) { text = text }; b.AddToClassList("player-station-action"); StyleChoice(b, false); b.SetEnabled(enabled); toolbar.Add(b);
                }
                if (pages > 1)
                {
                    Tool("Previous", () => { page--; Show(); }, page > 0);
                    var count = new Label($"{page + 1} / {pages}");
                    count.style.unityTextAlign = TextAnchor.MiddleCenter;
                    count.style.flexGrow = 1;
                    toolbar.Add(count);
                    Tool("Next", () => { page++; Show(); }, page + 1 < pages);
                }
                foreach (var option in options.Skip(page * pageSize).Take(pageSize))
                {
                    bool selected = Value(key) == option.value;
                    var b = new Button(() =>
                    {
                        drafts[key] = option.value;
                        changed?.Invoke(option.value);
                        CloseChoicePicker();
                    }) { text = option.label, enableRichText = false };
                    if (selected) b.name = "selected-choice";
                    b.AddToClassList("player-station-row"); StyleChoice(b, selected);
                    if (items && int.TryParse(option.value, out int item))
                    {
                        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                        b.style.paddingLeft = 64;
                        icon.style.position = Position.Absolute;
                        icon.style.left = 8;
                        icon.style.top = 6;
                        icon.style.bottom = 6;
                        icon.style.width = 48;
                        icon.style.backgroundImage = new StyleBackground(ItemInfo.ItemIcon(item));
                        b.Add(icon);
                    }
                    b.RegisterCallback<FocusInEvent>(_ => StyleChoice(b, selected, true));
                    b.RegisterCallback<FocusOutEvent>(_ => StyleChoice(b, selected));
                    results.Add(b);
                }
                body.scrollOffset = Vector2.zero;
                if (InputMode.Current == InputKind.Gamepad)
                    (results.Q<Button>("selected-choice") ?? results.Q<Button>())?.Focus();
            }
            Show();
        }

        void CloseChoicePicker()
        {
            string key = choicePickerKey; choicePickerKey = null;
            panel.RemoveFromClassList("player-station-picking-model");
            heading.text = marketplaceOnly ? "MARKETPLACE" : "PLAYER STATIONS";
            RebuildBody(choiceReturnScroll);
            if (InputMode.Current == InputKind.Gamepad) body.Q<Button>("choice-" + key)?.Focus();
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
            var raceFilter = new VisualElement(); raceFilter.style.flexDirection = FlexDirection.Row; raceFilter.style.flexWrap = Wrap.Wrap; body.Add(raceFilter);
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

            foreach (string race in races)
            {
                var filter = new Button(() => ShowMatches(race)) { text = race };
                filter.AddToClassList("player-station-action"); StyleChoice(filter, false); raceFilter.Add(filter);
            }
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

        void BuildListingForm(string id)
        {
            if (listingItem < 0)
            {
                AddCatalogueChoice("marketFleetIndex", "Personal stored hull", "fleet");
                AddNumber("marketHullPrice", "Hull price (credits)", 10000, 1, 1000000000);
                if (!string.IsNullOrEmpty(Value("marketFleetIndex")))
                    AddAction("List selected hull", () => Send("/station list-ship " + id + " " + Value("marketFleetIndex") + " " + Value("marketHullPrice")));
                return;
            }
            var item = NetGame.Db?.Item(listingItem);
            int available = Shop.CargoOf(listingItem);
            if (item == null || available <= 0) { AddInfo("Cargo", "This item is no longer in your cargo hold."); return; }
            AddInfo("Item", item.name);
            AddNumber("marketAmount", "Units", 1, 1, Math.Min(500, available));
            AddNumber("marketPrice", "Price per unit (credits)", Math.Max(1, item.minPrice), 1, 1000000000);
            AddAction("List from cargo", () => Send($"/station list-cargo {id} {listingItem} {Value("marketAmount")} {Value("marketPrice")}"));
        }

        void BuildMarketSettings(string id)
        {
            if (managementCatalogue?.canManageMarket == true)
            {
                AddSection("MARKETPLACE TAX", "");
                AddNumber("salesTax", "Sales tax (%)", selected.marketplaceTaxPercent, 0, 10);
                AddAction("Set marketplace tax", () => Send("/station tax " + id + " " + Value("salesTax")));
            }
            AddDirectoryPager();
            AddAccountField("marketPilot", "Pilot");
            AddAction("View pilot marketplace rules", "/station pilot-market-rules " + id);
            if (managementCatalogue?.canSecurity == true || managementCatalogue?.canOwnership == true)
            {
                AddSection("PILOT MARKETPLACE ACCESS", "");
                AddField("marketAccessReason", "Reason for denying access", "");
                AddAction("Deny marketplace access", () => SendWithReason(
                    $"/station pilot-market-access {id} {Value("marketPilot")} deny", "marketAccessReason"));
                AddAction("Restore inherited access", () => Send($"/station pilot-market-access {id} {Value("marketPilot")} inherit"));
            }
            if (managementCatalogue?.canManageMarket != true) return;
            AddSection("PILOT TAX", "");
            AddChoice("marketPilotOperation", "Transaction", new List<(string, string)> { ("purchase", "Purchase"), ("sale", "Sale") },
                _ => drafts.Remove("marketPilotPercent"));
            int limit = NetPlayerStations.PilotMarketLimit;
            AddNumber("marketPilotPercent", "Percentage", 0, Value("marketPilotOperation") == "purchase" ? -limit : 0, limit);
            AddField("marketPilotReason", "Reason for the surcharge or deduction", "");
            string Command() => $"/station pilot-tax {id} {Value("marketPilot")} {Value("marketPilotOperation")}";
            AddAction("Apply pilot tax", () =>
            {
                string percent = Value("marketPilotPercent");
                if (int.TryParse(percent, out int rate) && rate > 0) SendWithReason(Command() + " " + percent, "marketPilotReason");
                else Send(Command() + " " + percent);
            });
            AddAction("Remove pilot tax override", () => Send(Command() + " inherit"));
            if (Modding.ModPlayerInfrastructure.Current.StationRacePoliciesEnabled)
                AddAction("Race policies", () => ChangeManagementPage("race-policies"));
        }

        void BuildStationStock()
        {
            AddSection("DONATED CONSTRUCTION STOCK", "");
            AddAction("View construction stock", "/station faction-materials");
            AddItemChoice("stockItem", "Material");
            AddNumber("stockQuantity", "Units", 1, 1, 10000);
            AddAction("Donate from cargo", () => Send("/station faction-donate " + Value("stockItem") + " " + Value("stockQuantity")));
        }

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
                AddSection("RACE POLICIES", "");
                if (owner)
                    AddChoice("raceFacility", "Facility", NetPlayerStations.RacePolicyFacilities.Select(value => (value, value)).ToList());
                AddRaceChoice("stationPolicyRace", "Race");
                if (owner)
                {
                AddChoice("racePolicyState", "Race access", new List<(string, string)> { ("inherit", "Inherit faction policy"), ("allow", "Allow"), ("deny", "Deny") });
                if (Value("racePolicyState") == "deny") AddField("raceDenialReason", "Denial reason", "");
                AddAction("Apply race access rule", () =>
                {
                    string command = $"/station race-policy {id} {Value("raceFacility")} {Value("stationPolicyRace")} {Value("racePolicyState")}";
                    if (Value("racePolicyState") == "deny") SendWithReason(command, "raceDenialReason");
                    else Send(command);
                });
                }
                if (managementCatalogue?.canManageMarket == true && Modding.ModPlayerInfrastructure.Current.StationRaceMarketLimitPercent > 0)
                {
                    AddChoice("raceMarketAction", "Price adjustment", new List<(string, string)> { ("purchase", "Purchase surcharge or discount"), ("sale", "Seller proceeds deduction") }, _ => drafts.Remove("raceMarketPercent"));
                    AddChoice("raceMarketMode", "Adjustment", new List<(string, string)> { ("inherit", "Inherit faction policy"), ("custom", "Set percentage") });
                    int limit = Modding.ModPlayerInfrastructure.Current.StationRaceMarketLimitPercent;
                    bool custom = Value("raceMarketMode") == "custom";
                    if (custom)
                    {
                        AddNumber("raceMarketPercent", "Percentage", 0, Value("raceMarketAction") == "purchase" ? -limit : 0, limit);
                        AddField("raceMarketReason", "Reason for the price adjustment", "");
                    }
                    AddAction("Apply race price adjustment", () =>
                    {
                        string adjustment = custom ? Value("raceMarketPercent") : "inherit";
                        string command = $"/station race-policy {id} {Value("raceMarketAction")} {Value("stationPolicyRace")} {adjustment}";
                        if (custom && int.TryParse(adjustment, out int percent) && percent > 0)
                            SendWithReason(command, "raceMarketReason");
                        else Send(command);
                    });
                }
                AddAction("View station and faction race rules", "/station race-policy " + id);
                if (owner && Modding.ModPlayerInfrastructure.Current.StationRaceExemptionsEnabled)
                {
                    AddAccountField("stationRaceExemption", "Exemption target");
                    AddAction("Grant exemption", () => Send("/station race-policy " + id + " exempt add " + Value("stationRaceExemption")));
                    AddAction("Revoke exemption", () => Send("/station race-policy " + id + " exempt remove " + Value("stationRaceExemption")));
                }
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
            if (owner && station.ownershipType == PlayerStationOwnershipType.Individual)
            {
                AddSection("FACTION SHARING", "Set faction defaults for docking, storage and the ship hangar.");
                AddAction("Share with your faction", "/station access " + id + " faction");
                AddAction("Set owner-only defaults", "/station access " + id + " owner");
            }
            if (owner && station.ownershipType == PlayerStationOwnershipType.Faction)
            {
                AddChoice("governanceRank", "Faction rank", new[] { "member", "officer" }.Select(value => (value, value)).ToList());
                AddChoice("governanceDefault", "Default permission", new[] { "allow", "deny" }.Select(value => (value, value)).ToList());
                AddAction("Set faction default", () => Send("/station default " + id + " " + Value("governanceRank") + " " + Value("governancePermission") + " " + Value("governanceDefault")));
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

        void AddDirectoryPager()
        {
            if (managementCatalogue == null || managementCatalogue.directoryPages < 2) return;
            AddInfo("Pilot and faction directory", $"Page {directoryPage + 1} of {managementCatalogue.directoryPages}");
            void Move(int delta)
            {
                directoryPage += delta;
                RefreshManagementCatalogue();
            }
            if (directoryPage > 0) AddAction("Previous directory page", () => Move(-1));
            if (directoryPage + 1 < managementCatalogue.directoryPages) AddAction("Next directory page", () => Move(1));
        }

        void AddCatalogueChoice(string key, string label, string group)
        {
            var options = new List<(string value, string label)> { ("", "Select " + label.ToLowerInvariant() + "…") };
            if (managementCatalogue?.stationId == selected?.id)
                options.AddRange(managementCatalogue.options.Where(option => option.group == group).Select(option => (option.value, option.label)));
            AddChoice(key, label, options);
            if (options.Count == 1) AddInfo(label, managementCatalogue == null ? "Waiting for server data." : "No eligible entries available.");
        }

        void AddAccountField(string key, string label)
        {
            string group = key == "transferAccount" && Value("transferKind") == "faction" ||
                key == "successorAccount" && Value("successorType") == "faction" ? "factionLeader" :
                key.StartsWith("gateBlockTarget_", StringComparison.Ordinal) &&
                Value(key.Replace("Target_", "Type_")) == "faction" ? "faction" : "pilot";
            AddCatalogueChoice(key, label, group);
        }

        void AddRaceChoice(string key, string label)
        {
            var races = Modding.ModRefs.RaceWords.Select(race => (value: PlayerIdentity.RaceKey(race.race), label: race.name))
                .Concat(Modding.ModRaces.All.Select(race => (value: race.key, label: Modding.ModRaces.Name(race.race))))
                .GroupBy(race => race.value).Select(group => group.First()).OrderBy(race => race.label).ToList();
            races.Insert(0, ("", "Choose race…"));
            AddChoice(key, label, races);
        }

        // Button steppers preserve the complete numeric range without a keyboard or arbitrary preset values.
        void AddNumber(string key, string label, long initial, long min, long max, long quantum = 1)
        {
            long value = drafts.TryGetValue(key, out string existing) && long.TryParse(existing, out long parsed) ? parsed : initial;
            value = Math.Clamp(value, min, max);
            value = min + (value - min) / quantum * quantum;
            drafts[key] = value.ToString();
            var caption = new Label(label); caption.AddToClassList("player-station-note"); body.Add(caption);
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center;
            var current = new Label(value.ToString("N0")); current.style.flexGrow = 1; current.style.unityTextAlign = TextAnchor.MiddleCenter;
            long[] steps = new[] { quantum, quantum * 10, quantum * 100, quantum * 1000, quantum * 10000, quantum * 100000, quantum * 1000000, quantum * 10000000, quantum * 100000000 }.Where(step => step <= Math.Max(quantum, max - min)).ToArray();
            int stepIndex = drafts.TryGetValue(key + "_step", out string storedStep) && int.TryParse(storedStep, out int savedStep) ? Math.Clamp(savedStep, 0, steps.Length - 1) : 0;
            var picker = new Button { text = "Step: " + steps[stepIndex].ToString("N0") }; picker.AddToClassList("player-station-action");
            picker.clicked += () => { stepIndex = (stepIndex + 1) % steps.Length; drafts[key + "_step"] = stepIndex.ToString(); picker.text = "Step: " + steps[stepIndex].ToString("N0"); };
            void Set(long next)
            {
                value = Math.Clamp(next, min, max);
                value = min + (value - min) / quantum * quantum;
                drafts[key] = value.ToString(); current.text = value.ToString("N0");
            }
            Button Control(string title, Action action)
            {
                var button = new Button(action) { text = title };
                button.AddToClassList("player-station-action"); button.style.minWidth = 42; button.style.minHeight = 40;
                return button;
            }
            row.Add(Control("Min", () => Set(min)));
            row.Add(Control("−", () => Set(value - steps[stepIndex])));
            row.Add(current);
            row.Add(Control("+", () => Set(value + steps[stepIndex])));
            row.Add(Control("Max", () => Set(max)));
            body.Add(row);
            body.Add(picker);
        }

        void AddField(string key, string label, string initial)
        {
            var field = new TextField(label) { value = drafts.TryGetValue(key, out string draft) ? draft : initial ?? "" };
            if (key == "newStationName") field.maxLength = NetPlayerStations.StationNameMaxLength;
            else if (key == "stationAnnouncement" || key.IndexOf("Reason", StringComparison.OrdinalIgnoreCase) >= 0)
            { field.maxLength = 160; field.multiline = true; field.style.minHeight = 76; }
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
            button.style.whiteSpace = WhiteSpace.Normal; button.style.height = StyleKeyword.Auto; button.style.minHeight = 42; button.style.flexShrink = 0;
            body.Add(button);
        }

        string Value(string key) => fields.TryGetValue(key, out var field) ? System.Text.RegularExpressions.Regex.Replace((field.value ?? "").Trim(), @"\s+", " ") :
            drafts.TryGetValue(key, out var choice) ? choice : "";
        void SendStationName(string action, string name)
        {
            string nameError = NetPlayerStations.StationNameError(name);
            if (nameError != null)
            {
                feedback.text = nameError;
                feedbackTime = 8f;
                return;
            }
            Dispatch("/station " + action + " " + name);
        }

        void SendWithReason(string command, string reasonKey, string separator = " ")
        {
            string reason = System.Text.RegularExpressions.Regex.Replace(
                NetPlayerStations.NormalizeBlacklistReason(Value(reasonKey)), @"\s+", " ");
            if (string.IsNullOrWhiteSpace(reason))
            {
                feedback.text = "Enter a reason before applying this restriction.";
                feedbackTime = 8f;
                return;
            }
            if (command.TrimEnd() != command)
            {
                Send(command);
                return;
            }
            Send(command + separator + reason);
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
            return id == "drydock" ? "Dry Dock" : id == "hangar" ? "Ship Hangar" : id == "dockarm" ? "Docking arms" :
                char.ToUpperInvariant(id[0]) + id.Substring(1);
        }
    }
}
