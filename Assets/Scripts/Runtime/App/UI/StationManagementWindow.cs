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
    public sealed partial class StationManagementWindow
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
        bool actionRefreshPending;
        float actionSnapshotDeadline;
        string observedStationSnapshot;
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
        readonly Dictionary<string, Vector3Int> placementRotationDrafts = new Dictionary<string, Vector3Int>();
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
            var original = Resources.Load<StyleSheet>("GoF2UI/OriginalStyle");
            if (original != null) overlay.styleSheets.Add(original);
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
            // Command replies and the replicated registry can arrive in either order.
            // Observe the existing snapshot identity; do not poll or deserialize it again.
            string snapshot = NetState.Instance?.PlayerStations;
            if (!ReferenceEquals(snapshot, observedStationSnapshot))
            {
                observedStationSnapshot = snapshot;
                // Public claims and ownership transfers must also refresh if another
                // station update arrived between the command and its result, or if the
                // ownership snapshot arrived after the normal post-command window.
                var live = selected == null ? null : NetPlayerStations.StationAt(selected.stationIndex);
                bool authorityChanged = selected != null && live?.id == selected.id &&
                    (live.offeredForClaimUtc != selected.offeredForClaimUtc ||
                     live.underConstruction != selected.underConstruction ||
                     live.ownershipType != selected.ownershipType ||
                     !string.Equals(live.ownerName, selected.ownerName, StringComparison.Ordinal) ||
                     !string.Equals(live.ownerFactionId, selected.ownerFactionId, StringComparison.Ordinal));
                if (authorityChanged) managementCatalogue = null;
                if (authorityChanged || Time.unscaledTime < actionSnapshotDeadline)
                {
                    actionSnapshotDeadline = 0;
                    actionRefreshPending = true;
                    refresh = 0;
                    nextManagementQuery = 0;
                }
            }
            if (!marketplaceOnly && managementPage == "build" && Time.unscaledTime >= nextPlannerQuery && Modding.ModPlayerInfrastructure.Current.Enabled) RefreshPlannerLocations();
            if (feedbackTime > 0 && (feedbackTime -= Time.unscaledDeltaTime) <= 0) feedback.text = "";
            if (placementRoot != null && managementPage != "placement")
            {
                if (Time.unscaledTime >= nextManagementQuery) RefreshManagementCatalogue();
                if (actionRefreshPending && !placementDragging) { actionRefreshPending=false; RebuildStationList(); RebuildBody(); }
                return;
            }
            if (placementRoot != null)
            {
                if (Time.unscaledTime >= nextManagementQuery) RefreshManagementCatalogue();
                // Placement owns its own reply/snapshot synchronization and must not be rebuilt while dragging.
                actionRefreshPending = false;
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
            if (StarMap.IsOpen || modelPickerKey != null || choicePickerKey != null) return;
            bool actionRefresh = actionRefreshPending;
            if (!actionRefresh && (Time.unscaledTime - pressedAt < 5f || TextFieldKeys.InTextField(panel.focusController?.focusedElement))) return;

            bool rebuild = actionRefresh || (refresh -= Time.unscaledDeltaTime) <= 0;
            // Rebind the selected record before querying permissions. A claimed station may
            // still have been marked public in the previous client snapshot.
            if (rebuild || Time.unscaledTime >= nextManagementQuery) RebuildStationList();
            actionRefreshPending = false;
            if (Time.unscaledTime >= nextManagementQuery) RefreshManagementCatalogue(actionRefresh);
            if (rebuild)
            {
                refresh = 2f;
                RebuildBody(); // Retain the existing background refresh for live construction and procurement.
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
            actionRefreshPending = false; actionSnapshotDeadline = 0;
            observedStationSnapshot = NetState.Instance?.PlayerStations;
            plannerOrbits = null;
            plannerRequestPending = false;
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
            placementRotationDrafts.Clear();
            placementCameraKey = null;
            placementRequestVersion++;
            placementOrderPending = false;
            placementAwaitingSnapshot = false;
            overlay.style.display = DisplayStyle.None;
            open = false;
            actionRefreshPending = false; actionSnapshotDeadline = 0; observedStationSnapshot = null;
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
            if (selected.underConstruction || selected.offeredForClaimUtc > 0) managementCatalogue = null;
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
                        AddSection("STATION NAME", "Renaming costs " + NetPlayerStations.StationRenamePrice.ToString("N0") + " credits from your personal balance.");
                        AddInfo("Current name", selected.name);
                        AddField("renameStationName", "New station name", selected.name);
                        if (Value("renameConfirmed") == "true")
                        {
                            AddInfo("Confirm rename", "The fee is charged only if the rename succeeds.");
                            AddAction("Confirm rename — " + NetPlayerStations.StationRenamePrice.ToString("N0") + " CR", () =>
                            {
                                drafts.Remove("renameConfirmed");
                                Send("/station rename " + id + " " + Value("renameStationName"));
                                RebuildBody();
                            });
                            AddAction("Cancel rename", () => { drafts.Remove("renameConfirmed"); RebuildBody(); });
                        }
                        else AddAction("Rename station", () =>
                        {
                            if (string.IsNullOrWhiteSpace(Value("renameStationName")) || Value("renameStationName") == selected.name)
                            {
                                feedback.text = "Enter a different station name."; feedbackTime = 8f; return;
                            }
                            drafts["renameConfirmed"] = "true"; RebuildBody();
                        });
                        AddSection("OWNERSHIP TRANSFER", "");
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

    }
}
