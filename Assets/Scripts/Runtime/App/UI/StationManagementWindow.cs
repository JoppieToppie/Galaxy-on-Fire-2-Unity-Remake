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
        readonly VisualElement pageBar, primaryNav;
        NetPlayerStations.ManagementCatalogue managementCatalogue;
        int directoryPage;
        float nextManagementQuery;
        string pageBarSignature, stationListSignature;
        string moduleDetail;
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
        string placementCameraKey;
        float placementYaw, placementPitch, placementDistance;
        int placementRequestVersion;
        bool placementOrderPending;
        bool placementAwaitingSnapshot;
        float placementRequestDeadline;
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
            // The same resource stylesheet drives the docked and in-flight interface.
            var sheet = Resources.Load<StyleSheet>("GoF2Net/PlayerStationWindow");
            if (sheet != null) overlay.styleSheets.Add(sheet);
            overlay.style.display = DisplayStyle.None;
            safeArea.Add(overlay);
            panel = new VisualElement { name = "playerStationManagement" };
            panel.AddToClassList("player-station-window");
            panel.RegisterCallback<PointerDownEvent>(_ => pressedAt = Time.unscaledTime, TrickleDown.TrickleDown);
            panel.RegisterCallback<PointerUpEvent>(_ => pressedAt = -10f, TrickleDown.TrickleDown);
            overlay.Add(panel);

            var top = new VisualElement(); top.AddToClassList("player-station-header");
            heading = new Label("PLAYER STATIONS") { enableRichText = false }; heading.AddToClassList("player-station-title"); top.Add(heading);
            closeButton = new Button(Back) { text = "BACK" }; closeButton.AddToClassList("player-station-close");
            closeButton.clicked += () => menu?.PlayRelease();
            closeButton.RegisterCallback<PointerDownEvent>(_ => menu?.PlayPush(), TrickleDown.TrickleDown);
            top.Add(closeButton); panel.Add(top);

            var shell = new VisualElement(); shell.AddToClassList("player-station-shell");
            primaryNav = new VisualElement(); primaryNav.AddToClassList("player-station-primary-nav"); shell.Add(primaryNav);
            var split = new VisualElement(); split.AddToClassList("player-station-content");
            stationList = new ScrollView(ScrollViewMode.Vertical); stationList.AddToClassList("player-station-list");
            split.Add(stationList);
            body = new ScrollView(ScrollViewMode.Vertical); body.AddToClassList("player-station-body");
            var workspace = new VisualElement();
            workspace.AddToClassList("player-station-workspace");
            workspace.style.flexGrow = 1; workspace.style.flexBasis = 0; workspace.style.minWidth = 0; workspace.style.minHeight = 0;
            pageBar = new VisualElement(); pageBar.style.flexDirection = FlexDirection.Row; pageBar.style.flexWrap = Wrap.Wrap;
            pageBar.style.marginBottom = 8; pageBar.style.flexShrink = 0;
            workspace.Add(pageBar); workspace.Add(body); split.Add(workspace); shell.Add(split); panel.Add(shell);
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
            bool changed = compact != small;
            compact = small;
            panel.EnableInClassList("player-station-compact", small);
            stationList.mode = small ? ScrollViewMode.Horizontal : ScrollViewMode.Vertical;
            stationList.contentContainer.style.flexDirection = small ? FlexDirection.Row : FlexDirection.Column;
            pageBar.style.flexWrap = Wrap.Wrap;
            body.style.flexGrow = 1; body.style.flexBasis = 0; body.style.minHeight = 0; body.style.minWidth = 0;
            if (changed && open && placementRoot == null && modelPickerKey == null && choicePickerKey == null) refresh = 0;
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
                if (placementOrderPending && !placementDragging && Time.unscaledTime >= placementRequestDeadline)
                {
                    placementOrderPending = false;
                    placementAwaitingSnapshot = false;
                    placementRequestVersion++;
                    RebuildBody();
                    return;
                }
                if (!placementDragging && Time.unscaledTime >= nextPlacementRefresh)
                {
                    nextPlacementRefresh = Time.unscaledTime + 1f;
                    var live = NetPlayerStations.StationAt(CurrentOrbit);
                    if (live?.id == selected?.id && PlacementSnapshotOf(live, placementOrbit) != placementSnapshot)
                    {
                        selected = live;
                        if (placementAwaitingSnapshot) { placementAwaitingSnapshot = false; placementOrderPending = false; }
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
            selected = null; stationListSignature = null; moduleDetail = null;
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
            else if (managementPage == "modules" && moduleDetail != null) { moduleDetail = null; RebuildBody(Vector2.zero); }
            else if (managementPage != "overview") ChangeManagementPage("overview");
            else Close();
        }

        public void Close()
        {
            if (!open) return;
            DestroyPlacementPreview();
            placementDrafts.Clear();
            placementCameraKey = null;
            placementRequestVersion++;
            placementOrderPending = false;
            placementAwaitingSnapshot = false;
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
            managementCatalogue = null; pageBarSignature = null; pageBar.Clear(); primaryNav.Clear();
            stationListSignature = null; moduleDetail = null;
            confirmBuilderRelinquish = -1;
        }

        void RebuildStationList()
        {
            if (StationDataStatus() != null)
            {
                selected = null; stationListSignature = null; stationList.Clear();
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
                if (stationListSignature != "empty") { stationList.Clear(); stationList.Add(new Label("No local station or accessible construction orders.")); }
                stationListSignature = "empty"; selected = null;
                return;
            }
            int previousId = selected?.id ?? -1;
            selected = records.FirstOrDefault(x => x.id == previousId)
                ?? records.FirstOrDefault(x => x.stationIndex == CurrentOrbit) ?? records[0];
            if (previousId != selected.id)
            {
                drafts.Clear(); managementCatalogue = null; directoryPage = 0; nextManagementQuery = 0;
                if (managementPage != "directory" && managementPage != "orders") managementPage = "overview";
                moduleDetail = null;
            }
            string signature = selected.id + ":" + string.Join("|", records.Select(record =>
                record.id + ":" + record.name + ":" + record.underConstruction + ":" + record.offeredForClaimUtc));
            if (signature == stationListSignature) return;
            stationListSignature = signature;
            stationList.Clear();
            foreach (var record in records)
            {
                string state = record.offeredForClaimUtc > 0 ? "OPEN TO CLAIM" :
                    record.underConstruction ? "CONSTRUCTION" : "OPERATIONAL";
                int stationId = record.id;
                var button = new Button(() =>
                {
                    var live = NetPlayerStations.Stations?.FirstOrDefault(entry => entry != null && entry.id == stationId);
                    if (live != null) SelectStation(live);
                }) { text = record.name + "\n" + state, enableRichText = false };
                button.AddToClassList("player-station-row");
                button.EnableInClassList("player-station-row--selected", selected.id == record.id);
                stationList.Add(button);
            }
        }

        void SelectStation(PlayerStationRecord record)
        {
            if (selected?.id != record.id)
            {
                confirmRetirementId = -1;
                confirmBuilderRelinquish = -1;
                confirmOrbitalRemoval = 0;
                managementCatalogue = null; directoryPage = 0; nextManagementQuery = 0;
                if (managementPage != "directory" && managementPage != "orders") managementPage = "overview";
                moduleDetail = null;
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
            if (managementPage == "directory")
            {
                AddSection("STATION DIRECTORY", "Select an accessible station or construction project from the directory.");
                if (selected != null) { AddInfo("Selected station", selected.name); AddStationLocation(selected); AddAction("Open overview", () => ChangeManagementPage("overview")); }
                if (Modding.ModPlayerInfrastructure.Current.Enabled) AddAction("Plan new station", () => ChangeManagementPage("build"));
                return;
            }
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
            if (managementPage == "gates" || managementPage == "gate-security")
            {
                int system = selected?.system ?? NetGame.Db.Stations.Find(station => station != null && station.index == CurrentOrbit)?.system ?? -1;
                AddDirectoryPager();
                BuildSystemGates(system, managementPage == "gate-security");
                if (managementPage == "gates" && selected != null && !selected.underConstruction && managementCatalogue?.canConstruction == true &&
                    Modding.ModPlayerInfrastructure.Current.JumpgatesEnabled) BuildNewJumpgateControls(selected.id.ToString());
                return;
            }
            if (selected == null) { AddSection("STATION OVERVIEW", "Select a station or begin a construction project."); return; }
            string id = selected.id.ToString();
            bool operational = !selected.underConstruction && selected.offeredForClaimUtc == 0;
            bool docked = !inFlight && selected.stationIndex == CurrentOrbit;
            bool ownership = managementCatalogue?.canOwnership == true;
            if (managementPage == "overview" || !operational || !docked)
            {
                if (!operational) { AddSection(selected.name, selected.underConstruction ? "CONSTRUCTION PROJECT" : "OPEN TO CLAIM"); AddStationLocation(selected); }
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
                BuildOverviewDashboard(id, docked);
                return;
            }
            switch (managementPage)
            {
                case "news":
                    AddSection("LOCAL STATION NEWS", "Announcements published by the station authority. Visible at this station.");
                    var announcements = NetNews.StationAnnouncements(selected.stationIndex);
                    if (announcements.Count == 0) AddInfo("Announcements", "No recent announcements.");
                    foreach (string announcement in announcements) AddInfo("Announcement", announcement);
                    if (managementCatalogue?.canAnnounce == true)
                    {
                        AddField("stationAnnouncement", "New announcement", "");
                        AddAction("Publish to station news", () => Send("/station announce " + id + " " + Value("stationAnnouncement")));
                        BuildOfficialStationBroadcast(id);
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
                case "security": case "faction-access": case "blacklist": case "race-policies": BuildStationGovernance(selected); break;
                case "modules": BuildModuleDashboard(id); break;
                case "defenses":
                    AddAction("Open 3D orbit placement editor", () => ChangeManagementPage("placement"));
                    BuildOrbitalStructureControls(id);
                    break;
                case "placement": BuildOrbitPlacement(); break;
                case "bank": BuildStationBank(id); break;
                case "market": BuildMarketSettings(id); break;
                case "refinery": case "factory": BuildFacilityControls(id); break;
                case "hangar": case "drydock": BuildFacilityControls(id); break;
                default: ChangeManagementPage("overview"); break;
            }
        }

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
                AddInfo("Dry dock", station.dryDockCompletesUtc > 0 ? ConstructionTime(Math.Max(1, station.dryDockCompletesUtc - DateTimeOffset.UtcNow.ToUnixTimeSeconds())) : "Construction underway");
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
                if (Installed(module)) return "OPERATIONAL";
                if (!Building(module)) return "NOT INSTALLED";
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
                var button = new Button(() => ChangeManagementPage(key)) { text = title };
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
            int firstControl = body.contentContainer.childCount;
            var types = new List<(string, string)> { ("orbital", "Orbital defense"), ("factory", "Factory"), ("refinery", "Refinery"), ("drydock", "Dry dock") };
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
                preset.Defense && NetPlayerStations.OrbitalPresetAvailable(preset, race) &&
                settings.OrbitalDefenseLimit > 0) :
                NetPlayerStations.ModuleExteriorChoices(station, kind)).ToList();
            var existing = station.placements?.Find(entry => entry?.kind == kind);
            if (!drafts.ContainsKey("placementPreset") && existing != null) drafts["placementPreset"] = existing.preset;
            AddChoice("placementPreset", "Exterior model", presets.Select(preset => (preset.Id, preset.Name)).ToList());
            var preset = presets.Find(value => value.Id == Value("placementPreset"));
            if (preset == null) return;

            Vector3 stationPosition = NetPlayerStations.StationPosition(previewOrbit);
            string draftKey = station.id + ":" + previewOrbit + ":" + kind;
            bool hasDraft = placementDrafts.TryGetValue(draftKey, out Vector3Int position);
            if (!hasDraft)
                position = existing != null ? new Vector3Int(existing.x, existing.y, existing.z) :
                    new Vector3Int(0, 0, orbital ? 8000 : Mathf.Clamp(Mathf.RoundToInt(layout.stationReach * OrbitLayout.MetersPerUnit * layout.StationScale), 300, 9000));

            Vector3Int Absolute(Vector3Int point) => Vector3Int.RoundToInt(stationPosition) + point;
            string PlacementError(Vector3Int point) =>
                NetPlayerStations.OrbitalPlacementError(previewOrbit, layout, Absolute(point), preset.HitRadius);

            bool TryNextClearPosition(Vector3Int start, out Vector3Int next)
            {
                float angle = Mathf.Atan2(start.x, start.z) * Mathf.Rad2Deg;
                float baseRadius = Mathf.Max(8000f, NetPlayerStations.OrbitalStationClearance(layout, preset.HitRadius) + 500f);
                const int positionsPerRing = 12;
                for (int ring = 0; ring < 5; ring++)
                {
                    float radius = baseRadius + ring * Mathf.Max(2500f, preset.HitRadius * 2f + 500f);
                    for (int slot = 1; slot <= positionsPerRing; slot++)
                    {
                        Vector3 offset = Quaternion.Euler(0, angle + slot * (360f / positionsPerRing), 0) * new Vector3(0, 0, radius);
                        next = Vector3Int.RoundToInt(offset);
                        if (next != start && PlacementError(next) == null) return true;
                    }
                }
                next = start;
                return false;
            }

            int slotLimit = preset.Defense ? settings.OrbitalDefenseLimit : settings.OtherOrbitalStructureLimit;
            int slotsTaken = orbital ? NetPlayerStations.OrbitalStructures?.Count(entry =>
                entry != null && entry.orbit == previewOrbit && entry.defense == preset.Defense) ?? 0 : 0;
            if (orbital && PlacementError(position) != null && TryNextClearPosition(position, out var initialClear))
                position = initialClear;

            var configuration = body.contentContainer.Children().Skip(firstControl).ToList();
            AddSection("ORBIT PLACEMENT", orbital
                ? "Confirm each position to reserve a construction slot without delivering materials. Amber markers show reserved positions, including unbuilt projects. Drag empty space to rotate, scroll to zoom, and hold Ctrl to snap."
                : "Drag the marker or its arrows. Drag empty space to rotate, scroll to zoom, and hold Ctrl to snap.");
            var editor = new VisualElement(); editor.AddToClassList("player-station-orbit-layout"); body.Add(editor);
            var scene = new VisualElement(); scene.AddToClassList("player-station-orbit-scene"); editor.Add(scene);
            var controls = new VisualElement(); controls.AddToClassList("player-station-orbit-controls"); editor.Add(controls);
            foreach (var element in configuration) { element.RemoveFromHierarchy(); controls.Add(element); }
            if (orbital)
            {
                var reserved = NetPlayerStations.OrbitalStructures?.Where(entry => entry != null && entry.orbit == previewOrbit && entry.defense == preset.Defense).ToList();
                int built = reserved?.Count(entry => entry.operational) ?? 0;
                AddInfoTo(controls, preset.Defense ? "Defense slots" : "Structure slots", $"{slotsTaken} / {slotLimit} reserved");
                AddInfoTo(controls, "Operational", built.ToString());
                AddInfoTo(controls, "Unbuilt / planned", (slotsTaken - built).ToString());
                if (reserved != null)
                    foreach (var entry in reserved.OrderBy(entry => entry.slot))
                        AddInfoTo(controls, $"Slot {entry.slot + 1}", entry.name + " · " + (entry.operational ? "Operational" : entry.completesUtc > 0 ? "Building" : "Awaiting materials"));
            }
            var cameraControls = new VisualElement(); cameraControls.style.flexDirection = FlexDirection.Row; cameraControls.style.flexWrap = Wrap.Wrap; scene.Add(cameraControls);
            var snap = new Toggle("Snap to 100 m") { value = Value("placementSnap") == "true" }; snap.AddToClassList("player-station-field"); controls.Add(snap);
            snap.RegisterValueChangedCallback(change => drafts["placementSnap"] = change.newValue ? "true" : "false");
            var placementActions = new VisualElement();
            placementActions.style.flexDirection = FlexDirection.Row;
            placementActions.style.flexWrap = Wrap.Wrap;
            controls.Add(placementActions);
            var viewport = new VisualElement();
            viewport.AddToClassList("player-station-orbit-viewport");
            viewport.style.height = compact ? 300 : Mathf.Clamp(panel.resolvedStyle.height * 0.55f, 360f, 720f);
            viewport.style.flexShrink = 0;
            scene.Add(viewport);
            var coordinateLabel = new Label();
            coordinateLabel.AddToClassList("player-station-note");
            controls.Add(coordinateLabel);
            var placementStatus = new Label();
            placementStatus.AddToClassList("player-station-note");
            placementStatus.style.whiteSpace = WhiteSpace.Normal;
            if (orbital) controls.Add(placementStatus);

            const int previewLayer = 31;
            placementRoot = new GameObject("Station placement preview");
            placementRoot.transform.position = new Vector3(0, -1000000f, 0);
            var frame = new GameObject("Placement coordinates").transform;
            frame.SetParent(placementRoot.transform, false);
            var model = OrbitBuilder.SpawnStation(db, layout, frame);
            if (model != null) model.transform.localPosition = Vector3.zero;
            var ghost = OrbitBuilder.Spawn(db, NetPlayerStations.OrbitalStructureAssembly(preset.Assembly, preset.StationModel, db),
                Vector3.zero, Quaternion.identity, "Proposed structure", placementRoot.transform);
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
                        mounts.Add(new WeaponMount { slotType = 1, position_engine = Units(NetPlayerStations.OrbitalStructurePosition(structure) - stationPosition) });
            int active = mounts.Count;
            mounts.Add(new WeaponMount { slotType = orbital ? 2 : 8, position_engine = Units(position) });
            var handles = new GameObject("Placement handles");
            handles.transform.SetParent(placementRoot.transform, false);
            var gizmo = handles.AddComponent<ModBuilderGizmo>();
            gizmo.cam = camera;
            gizmo.Selected = active;
            gizmo.Bind(frame, mounts);
            SetLayer(handles.transform);
            if (placementCameraKey != draftKey)
            {
                placementCameraKey = draftKey;
                placementDistance = Mathf.Max(1800f, position.magnitude * 1.8f);
                placementYaw = 34;
                placementPitch = 29;
            }
            void SetCamera()
            {
                camera.transform.position = frame.position + Quaternion.Euler(placementPitch, placementYaw, 0) * new Vector3(0, 0, -placementDistance);
                camera.transform.LookAt(frame.position);
            }
            void CameraPreset(string title, float pitch, float yaw)
            {
                var button = new Button(() => { placementPitch = pitch; placementYaw = yaw; SetCamera(); }) { text = title };
                button.AddToClassList("player-station-action"); cameraControls.Add(button);
            }
            CameraPreset("Perspective", 29f, 34f);
            CameraPreset("Top", 89f, 0f);
            CameraPreset("Side", 0f, 90f);
            var orderButton = new Button { text = orbital ? (preset.Defense ? "CONFIRM DEFENSE PLACEMENT" : "CONFIRM STRUCTURE PLACEMENT") : "Save planned position" };
            void AddPlacementControl(Button button)
            {
                StyleChoice(button, false);
                button.AddToClassList("player-station-action");
                button.style.flexGrow = 1;
                button.clicked += () => menu?.PlayRelease();
                placementActions.Add(button);
            }
            void UpdatePoint()
            {
                position = Vector3Int.RoundToInt(frame.InverseTransformPoint(gizmo.WorldOf(mounts[active])));
                placementDrafts[draftKey] = position;
                if (ghost != null) ghost.transform.position = frame.TransformPoint(position);
                coordinateLabel.text = $"X {position.x:N0} m   Y {position.y:N0} m   Z {position.z:N0} m";
                if (!orbital) return;
                string error = PlacementError(position);
                placementStatus.text = placementAwaitingSnapshot ? "Placement saved. Synchronizing reserved slots…" : slotsTaken >= slotLimit ? "All slots of this type are reserved." :
                    error ?? "Position clear. Confirm to reserve; materials can be delivered later.";
                orderButton.SetEnabled(error == null && slotsTaken < slotLimit && !placementOrderPending);
            }
            if (orbital)
                AddPlacementControl(new Button(() =>
                {
                    if (TryNextClearPosition(position, out var clear))
                    {
                        mounts[active].position_engine = Units(clear);
                        UpdatePoint();
                    }
                    else placementStatus.text = "No clear position found nearby. Try moving the marker manually.";
                }) { text = "Next clear position" });
            AddPlacementControl(new Button(() =>
            {
                placementDistance = Mathf.Max(1800f, position.magnitude * 1.8f);
                SetCamera();
            }) { text = "Frame orbit" });
            AddPlacementControl(orderButton);
            orderButton.clicked += () =>
            {
                if (orbital && (placementOrderPending || slotsTaken >= slotLimit || PlacementError(position) != null)) return;
                Vector3Int point = orbital ? Absolute(position) : position;
                string command = orbital ? $"/station orbital-place {station.id} {previewOrbit}" : $"/station place {station.id} {kind}";
                string request = $"{command} {point.x} {point.y} {point.z} {preset.Id}";
                if (!orbital) { Send(request); return; }
                int version = ++placementRequestVersion;
                string submittedSnapshot = PlacementSnapshotOf(station, previewOrbit);
                placementAwaitingSnapshot = false;
                placementOrderPending = true;
                placementRequestDeadline = Time.unscaledTime + 10f;
                orderButton.SetEnabled(false);
                if (!Dispatch(request, result =>
                {
                    if (version != placementRequestVersion) return;
                    bool accepted = result?.StartsWith("Started ", StringComparison.Ordinal) == true;
                    // The reply and replicated registry may arrive in either order. Do not allow
                    // another confirmation against stale slots or stale collision geometry.
                    var live = NetPlayerStations.StationAt(CurrentOrbit);
                    placementAwaitingSnapshot = accepted && PlacementSnapshotOf(live, previewOrbit) == submittedSnapshot;
                    placementOrderPending = placementAwaitingSnapshot;
                    if (placementAwaitingSnapshot) placementRequestDeadline = Time.unscaledTime + 10f;
                    if (live?.id == selected?.id) selected = live;
                    if (managementPage == "placement" && !placementDragging) RebuildBody();
                }))
                {
                    placementOrderPending = false;
                    UpdatePoint();
                }
            };
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
                        if (evt.ctrlKey || snap.value) desired = new Vector3(Mathf.Round(desired.x / 100f), Mathf.Round(desired.y / 100f), Mathf.Round(desired.z / 100f)) * 100f;
                        mounts[active].position_engine = Units(desired);
                        UpdatePoint();
                    }
                }
                else if (gizmo.Dragging)
                {
                    gizmo.Drag(point, evt.ctrlKey || snap.value, Mathf.RoundToInt(100f / OrbitLayout.MetersPerUnit));
                    UpdatePoint();
                }
                else if (orbiting)
                {
                    placementYaw += evt.deltaPosition.x * .4f;
                    placementPitch = Mathf.Clamp(placementPitch - evt.deltaPosition.y * .4f, -80, 80);
                    SetCamera();
                }
                else gizmo.Hover(point);
            });
            viewport.RegisterCallback<PointerUpEvent>(evt =>
            {
                gizmo.EndDrag();
                orbiting = movingInPlane = placementDragging = false;
                UpdatePoint();
                if (viewport.HasPointerCapture(evt.pointerId)) viewport.ReleasePointer(evt.pointerId);
            });
            viewport.RegisterCallback<PointerCaptureOutEvent>(_ => { gizmo.EndDrag(); orbiting = movingInPlane = placementDragging = false; });
            viewport.RegisterCallback<WheelEvent>(evt =>
            {
                placementDistance = Mathf.Clamp(placementDistance * Mathf.Pow(1.12f, evt.delta.y), 300, 170000);
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
            var label = new Label(title) { enableRichText = false }; label.AddToClassList("player-station-section-title"); section.Add(label);
            if (!string.IsNullOrEmpty(description))
            {
                var note = new Label(description);
                note.AddToClassList("player-station-note");
                section.Add(note);
            }
            body.Add(section);
        }

        void AddInfo(string label, string value) => AddInfoTo(body, label, value);

        void AddInfoTo(VisualElement parent, string label, string value)
        {
            var row = new VisualElement(); row.AddToClassList("player-station-info");
            row.style.flexWrap = Wrap.Wrap; row.style.minHeight = 32; row.style.flexShrink = 0;
            var name = new Label(label.TrimEnd().TrimEnd(':') + ":") { enableRichText = false };
            name.style.whiteSpace = WhiteSpace.Normal; name.style.flexShrink = 0; name.style.marginRight = 8;
            var detail = new Label(value ?? ""); detail.enableRichText = false; detail.style.whiteSpace = WhiteSpace.Normal;
            detail.style.flexShrink = 1; detail.style.minWidth = 0; detail.style.flexGrow = 1;
            row.Add(name); row.Add(detail); parent.Add(row);
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
            pageBar.style.display = DisplayStyle.None; primaryNav.style.display = DisplayStyle.None; stationList.style.display = DisplayStyle.None;
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
            pageBar.style.display = DisplayStyle.None; primaryNav.style.display = DisplayStyle.None; stationList.style.display = DisplayStyle.None;
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
            managementPage = page; moduleDetail = null;
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
            AddField("marketPilotReason", "Reason (optional)", "");
            string Command() => $"/station pilot-tax {id} {Value("marketPilot")} {Value("marketPilotOperation")}";
            AddAction("Apply pilot tax", () =>
            {
                string percent = Value("marketPilotPercent");
                SendWithOptionalReason(Command() + " " + percent, "marketPilotReason");
            });
            AddAction("Remove pilot tax override", () => Send(Command() + " inherit"));
            if (Modding.ModPlayerInfrastructure.Current.StationRacePoliciesEnabled)
                AddAction("Race policies", () => ChangeManagementPage("race-policies"));
        }

        void BuildStationBank(string id)
        {
            AddSection("STATION BANK", "Independent station funds. Marketplace tax revenue and voluntary credit donations are deposited here.");
            if (managementCatalogue?.canManageMarket == true || managementCatalogue?.canOwnership == true)
                AddInfo("Balance", managementCatalogue.stationBankCredits.ToString("N0") + " credits");
            long available = Math.Min(int.MaxValue, (long)Session.Credits);
            AddInfo("Your credits", available.ToString("N0"));
            if (available <= 0) return;
            AddNumber("bankDonation", "Credits to donate", Math.Min(1000L, available), 1, available);
            AddAction("Donate credits to station bank", () => Send("/station bank-donate " + id + " " + Value("bankDonation")));
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
            AddNumber("stockQuantity", "Units", 1, 1, Math.Min(10000, remaining));
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
                BuildRacePolicyDashboard(station, owner);
                return;
            }
            if (managementPage == "faction-access")
            {
                BuildFactionAccess(station, owner);
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
        }

        void BuildFactionAccess(PlayerStationRecord station, bool owner)
        {
            if (!owner) return;
            string id = station.id.ToString();
            AddSection("FACTION ACCESS", "Manage inherited permissions separately from individual pilot overrides.");
            if (station.ownershipType == PlayerStationOwnershipType.Individual)
            {
                AddAction("Share with your faction", "/station access " + id + " faction");
                AddAction("Set owner-only defaults", "/station access " + id + " owner");
                return;
            }
            AddChoice("governanceRank", "Faction rank", new[] { "member", "officer" }.Select(value => (value, value)).ToList());
            AddChoice("governanceFactionPermission", "Permission", Enum.GetNames(typeof(PlayerStationPermission)).Select(value => (value, value)).ToList());
            AddChoice("governanceDefault", "Default permission", new[] { "allow", "deny" }.Select(value => (value, value)).ToList());
            AddAction("Set faction default", () => Send("/station default " + id + " " + Value("governanceRank") + " " + Value("governanceFactionPermission") + " " + Value("governanceDefault")));
        }

        static string FacilityPolicyLabel(string facility) => facility switch
        {
            "docking" => "Docking", "storage" => "Storage", "hangar" => "Hangar",
            "drydock" => "Dry dock", "refinery" => "Refinery", "factory" => "Factory", "market" => "Marketplace",
            "purchase" => "Purchase adjustment", "sale" => "Sale deduction", _ => facility
        };

        void BuildRacePolicyDashboard(PlayerStationRecord station, bool owner)
        {
            string id = station.id.ToString();
            AddSection("RACE POLICIES", "Select a race to inspect inherited and station-specific policies. Changes apply only when confirmed.");
            var races = Modding.ModRefs.RaceWords.Where(entry => PlayerIdentity.IsSelectableRace(entry.race)).Select(entry => (value: PlayerIdentity.RaceKey(entry.race), label: entry.name))
                .Concat(Modding.ModRaces.All.Where(entry => PlayerIdentity.IsSelectableRace(entry.race)).Select(entry => (value: entry.key, label: Modding.ModRaces.Name(entry.race))))
                .GroupBy(race => race.value).Select(group => group.First()).OrderBy(race => race.label).ToList();
            if (races.Count == 0) { AddInfo("Race directory", "No races are configured."); return; }
            string race = Value("stationPolicyRace");
            if (!races.Any(entry => entry.value == race))
            {
                race = races[0].value; drafts["stationPolicyRace"] = race;
            }
            var layout = new VisualElement(); layout.AddToClassList("player-station-policy-layout"); body.Add(layout);
            var directory = new VisualElement(); directory.AddToClassList("player-station-race-directory"); layout.Add(directory);
            var directoryTitle = new Label("RACES"); directoryTitle.AddToClassList("player-station-section-title"); directory.Add(directoryTitle);
            foreach (var entry in races)
            {
                string key = entry.value;
                var button = new Button(() =>
                {
                    drafts["stationPolicyRace"] = key;
                    foreach (string draftKey in new[] { "raceFacility", "racePolicyState", "raceDenialReason", "raceMarketAction", "raceMarketMode", "raceMarketPercent", "raceMarketReason" })
                        drafts.Remove(draftKey);
                    RebuildBody(body.scrollOffset);
                }) { text = entry.label, enableRichText = false };
                button.AddToClassList("player-station-row");
                button.EnableInClassList("player-station-row--selected", key == race);
                directory.Add(button);
            }
            var matrix = DashboardCard(layout, races.First(entry => entry.value == race).label.ToUpperInvariant() + " POLICY");
            matrix.AddToClassList("player-station-policy-matrix");
            StationFacilityRaceRule LocalRule(string facility) => managementCatalogue?.stationRaceRules?.Find(rule => rule != null && rule.race == race && rule.facility == facility);
            StationFacilityRaceRule FactionRule(string facility) => managementCatalogue?.factionRaceRules?.Find(rule => rule != null && rule.race == race && rule.facility == facility);
            string AccessSummary(string facility)
            {
                var local = LocalRule(facility);
                var inherited = FactionRule(facility);
                if (local != null && local.state != PlayerStationPermissionState.Inherit)
                    return local.state == PlayerStationPermissionState.Deny ? "Denied · station" : "Allowed · station";
                return inherited?.state == PlayerStationPermissionState.Deny ? "Denied · inherited" : "Allowed · inherited";
            }
            if (owner)
            {
                foreach (string facility in NetPlayerStations.RacePolicyFacilities)
                {
                    string key = facility;
                    var row = new VisualElement(); row.AddToClassList("player-station-policy-row");
                    var label = new Label(FacilityPolicyLabel(key)); label.AddToClassList("player-station-policy-label"); row.Add(label);
                    var state = new Label(AccessSummary(key)); state.AddToClassList("player-station-policy-state");
                    state.EnableInClassList("player-station-policy-denied", state.text.StartsWith("Denied", StringComparison.Ordinal)); row.Add(state);
                    var edit = new Button(() =>
                    {
                        drafts["raceFacility"] = key;
                        drafts["racePolicyState"] = (LocalRule(key)?.state ?? PlayerStationPermissionState.Inherit).ToString().ToLowerInvariant();
                        drafts["raceDenialReason"] = LocalRule(key)?.reason ?? "";
                        RebuildBody(body.scrollOffset);
                        body.schedule.Execute(() => body.Q<Button>("choice-racePolicyState")?.Focus());
                    }) { text = "Edit" };
                    edit.AddToClassList("player-station-action"); row.Add(edit); matrix.Add(row);
                }
                var gate = new Label("Jumpgate access is managed under Infrastructure / Jumpgates.");
                gate.AddToClassList("player-station-note"); matrix.Add(gate);
            }
            if (managementCatalogue?.canManageMarket == true && Modding.ModPlayerInfrastructure.Current.StationRaceMarketLimitPercent > 0)
            {
                foreach (string operation in new[] { "purchase", "sale" })
                {
                    string key = operation;
                    var local = LocalRule(key); var inherited = FactionRule(key);
                    var row = new VisualElement(); row.AddToClassList("player-station-policy-row");
                    var label = new Label(FacilityPolicyLabel(key)); label.AddToClassList("player-station-policy-label"); row.Add(label);
                    var value = new Label($"{local?.percent ?? inherited?.percent ?? 0:+0;-0;0}% · {(local != null ? "station" : "inherited")}");
                    value.AddToClassList("player-station-policy-state"); row.Add(value);
                    var edit = new Button(() =>
                    {
                        drafts["raceMarketAction"] = key;
                        drafts["raceMarketMode"] = local != null ? "custom" : "inherit";
                        drafts["raceMarketPercent"] = (local?.percent ?? inherited?.percent ?? 0).ToString();
                        drafts["raceMarketReason"] = local?.reason ?? "";
                        RebuildBody(body.scrollOffset);
                        body.schedule.Execute(() => body.Q<Button>("choice-raceMarketMode")?.Focus());
                    }) { text = "Edit" };
                    edit.AddToClassList("player-station-action"); row.Add(edit); matrix.Add(row);
                }
            }
            if (owner && NetPlayerStations.RacePolicyFacilities.Contains(Value("raceFacility")))
            {
                string facility = Value("raceFacility");
                AddSection("EDIT " + FacilityPolicyLabel(facility).ToUpperInvariant(), "Changes are validated by the station server.");
                AddChoice("racePolicyState", "Access", new List<(string, string)> { ("inherit", "Inherit faction policy"), ("allow", "Allow"), ("deny", "Deny") });
                if (Value("racePolicyState") == "deny") AddField("raceDenialReason", "Denial reason", "");
                AddAction("Apply access rule", () =>
                {
                    string command = $"/station race-policy {id} {facility} {race} {Value("racePolicyState")}";
                    if (Value("racePolicyState") == "deny") SendWithReason(command, "raceDenialReason");
                    else Send(command);
                });
            }
            if (managementCatalogue?.canManageMarket == true && Modding.ModPlayerInfrastructure.Current.StationRaceMarketLimitPercent > 0 &&
                (Value("raceMarketAction") == "purchase" || Value("raceMarketAction") == "sale"))
            {
                string operation = Value("raceMarketAction");
                AddSection("EDIT " + FacilityPolicyLabel(operation).ToUpperInvariant(), "");
                AddChoice("raceMarketMode", "Adjustment", new List<(string, string)> { ("inherit", "Inherit faction policy"), ("custom", "Set percentage") });
                bool custom = Value("raceMarketMode") == "custom";
                if (custom)
                {
                    int limit = Modding.ModPlayerInfrastructure.Current.StationRaceMarketLimitPercent;
                    AddNumber("raceMarketPercent", "Percentage", 0, operation == "purchase" ? -limit : 0, limit);
                    AddField("raceMarketReason", "Reason (optional)", "");
                }
                AddAction("Apply price adjustment", () =>
                {
                    string adjustment = custom ? Value("raceMarketPercent") : "inherit";
                    string command = $"/station race-policy {id} {operation} {race} {adjustment}";
                    if (custom) SendWithOptionalReason(command, "raceMarketReason");
                    else Send(command);
                });
            }
            if (owner && Modding.ModPlayerInfrastructure.Current.StationRaceExemptionsEnabled)
            {
                AddSection("INDIVIDUAL EXEMPTIONS", "Exempt a pilot from the station's race policies.");
                AddAccountField("stationRaceExemption", "Exemption target");
                AddAction("Grant exemption", () => Send("/station race-policy " + id + " exempt add " + Value("stationRaceExemption")));
                AddAction("Revoke exemption", () => Send("/station race-policy " + id + " exempt remove " + Value("stationRaceExemption")));
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
            var races = Modding.ModRefs.RaceWords.Where(entry => PlayerIdentity.IsSelectableRace(entry.race)).Select(race => (value: PlayerIdentity.RaceKey(race.race), label: race.name))
                .Concat(Modding.ModRaces.All.Where(entry => PlayerIdentity.IsSelectableRace(entry.race)).Select(race => (value: race.key, label: Modding.ModRaces.Name(race.race))))
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

        void BuildOfficialStationBroadcast(string id)
        {
            AddSection("GALACTIC NEWS BROADCAST", "Official announcements appear on the existing GalNet ticker galaxy-wide. Your station shares one five-minute cooldown.");
            const string key = "stationBroadcast";
            var composer = new VisualElement();
            composer.AddToClassList("player-station-composer");
            var field = new TextField("Announcement") { maxLength = NetNews.OfficialMessageLimit, multiline = true, value = drafts.TryGetValue(key, out var draft) ? draft : "" };
            field.AddToClassList("player-station-field");
            field.style.minHeight = 80;
            composer.Add(field);
            var counter = new Label();
            counter.AddToClassList("player-station-note");
            composer.Add(counter);
            var preview = new Label();
            preview.enableRichText = false;
            preview.AddToClassList("player-station-preview");
            composer.Add(preview);
            var cooldown = new Label();
            cooldown.AddToClassList("player-station-note");
            composer.Add(cooldown);
            bool pending = false;
            var publish = new Button(() =>
            {
                string message = System.Text.RegularExpressions.Regex.Replace((field.value ?? "").Trim(), @"\s+", " ");
                if (message.Length == 0 || pending) return;
                pending = Dispatch("/station broadcast " + id + " " + message, result =>
                {
                    pending = false;
                    if (result == "Official broadcast published to GalNet.")
                    {
                        drafts.Remove(key);
                        field.SetValueWithoutNotify("");
                        if (managementCatalogue != null) managementCatalogue.stationBroadcastReadyUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + NetNews.OfficialCooldownSeconds;
                    }
                });
            }) { text = "PUBLISH GALNET BROADCAST" };
            publish.AddToClassList("player-station-action");
            composer.Add(publish);
            void UpdateComposer()
            {
                string message = field.value ?? "";
                counter.text = $"{message.Length} / {NetNews.OfficialMessageLimit} characters";
                preview.text = selected?.name + ": " + message;
                long remaining = Math.Max(0L, (managementCatalogue?.stationBroadcastReadyUtc ?? 0L) - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                cooldown.text = remaining > 0 ? $"Station cooldown: {remaining / 60:00}:{remaining % 60:00}" : "Station broadcast available";
                publish.SetEnabled(!pending && remaining == 0 && !string.IsNullOrWhiteSpace(message) && managementCatalogue?.canAnnounce == true);
            }
            field.RegisterValueChangedCallback(change => { drafts[key] = change.newValue; UpdateComposer(); });
            body.Add(composer);
            UpdateComposer();
            composer.schedule.Execute(UpdateComposer).Every(1000);
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

        void SendWithOptionalReason(string command, string reasonKey)
        {
            string reason = System.Text.RegularExpressions.Regex.Replace(NetPlayerStations.NormalizeBlacklistReason(Value(reasonKey)) ?? "", @"\s+", " ").Trim();
            Send(string.IsNullOrEmpty(reason) ? command : command + " " + reason);
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

        bool Dispatch(string command, Action<string> onResult = null)
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
                onResult?.Invoke(result);
            }) == true) return true;
            feedback.text = "A station request is already pending, or the server is unavailable.";
            feedbackTime = 8f;
            return false;
        }

        static string ModuleLabel(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Unknown module";
            return id == "drydock" ? "Dry Dock" : id == "hangar" ? "Ship Hangar" : id == "dockarm" ? "Docking arms" :
                char.ToUpperInvariant(id[0]) + id.Substring(1);
        }
    }
}
