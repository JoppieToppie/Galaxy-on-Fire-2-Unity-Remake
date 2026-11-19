using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Multiplayer;
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
        readonly VisualElement safeArea, panel, stationList, body;
        readonly Label heading, feedback;
        readonly Button openButton, closeButton;
        readonly Dictionary<string, TextField> fields = new Dictionary<string, TextField>();
        PlayerStationRecord selected;
        bool open;
        float refresh;
        float feedbackTime;

        public bool IsOpen => open;
        public VisualElement[] NavItems() => panel.Query<Button>().ToList().Cast<VisualElement>().ToArray();

        public StationManagementWindow(StationMenu menu, StationLevel level, VisualElement root)
        {
            this.menu = menu; this.level = level;
            safeArea = root.Q("safeArea") ?? root;
            openButton = new Button(Open) { name = "playerStationsButton", text = "STATION MANAGEMENT" };
            openButton.AddToClassList("station-button");
            openButton.AddToClassList("gof-semibold");
            var buttons = root.Q("sidePanel")?.Q(className: "station-buttons");
            if (buttons != null) buttons.Add(openButton); else safeArea.Add(openButton);
            openButton.style.display = DisplayStyle.None;
            openButton.clicked += menu.PlayRelease;
            openButton.RegisterCallback<PointerDownEvent>(_ => menu.PlayPush(), TrickleDown.TrickleDown);

            panel = new VisualElement { name = "playerStationManagement" };
            panel.AddToClassList("player-station-window");
            panel.style.display = DisplayStyle.None;
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
            NetChat.Added += OnChat;
        }

        public void Dispose()
        {
            NetChat.Added -= OnChat;
            openButton?.RemoveFromHierarchy();
            panel?.RemoveFromHierarchy();
        }

        public void Update()
        {
            bool available = NetGame.Active && level?.Layout != null && NetPlayerStations.Stations != null;
            openButton.style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
            if (!available) { if (open) Close(); return; }
            if (!open) return;
            if (feedbackTime > 0 && (feedbackTime -= Time.unscaledDeltaTime) <= 0) feedback.text = "";
            if ((refresh -= Time.unscaledDeltaTime) <= 0)
            {
                refresh = 2f;
                RebuildStationList();
            }
        }

        void Open()
        {
            if (!NetGame.Active) return;
            menu.CloseHangar();
            panel.style.display = DisplayStyle.Flex;
            panel.BringToFront();
            open = true;
            refresh = 0;
            RebuildStationList();
            RebuildBody();
        }

        public void Close()
        {
            if (!open) return;
            panel.style.display = DisplayStyle.None;
            open = false;
        }

        void RebuildStationList()
        {
            stationList.Clear();
            // Management is contextual, not a world directory: only expose the player station at the location\n            // where this management screen is open. The server still enforces owner/faction permissions per command.\n            int currentStation = level?.Layout != null ? level.Layout.stationIndex : -1;\n            var records = NetPlayerStations.Stations?.Where(x => x != null && x.stationIndex == currentStation).OrderBy(x => x.id).ToList();
            if (records == null || records.Count == 0)
            {
                stationList.Add(new Label("No player-built station is available to manage at this location."));
                selected = null;
                return;
            }
            foreach (var record in records)
            {
                string state = record.underConstruction ? "CONSTRUCTION" : "OPERATIONAL";
                var button = new Button(() => SelectStation(record))
                { text = record.name + "\n#" + record.id + "  ·  " + state };
                button.AddToClassList("player-station-row");
                button.EnableInClassList("player-station-row--selected", selected?.id == record.id);
                stationList.Add(button);
            }
            int previousId = selected?.id ?? -1;
            selected = records.FirstOrDefault(x => x.id == previousId)
                ?? records.FirstOrDefault(x => x.stationIndex == level.Layout.stationIndex) ?? records[0];
            if (previousId != selected.id) RebuildBody();
        }

        void SelectStation(PlayerStationRecord record)
        {
            selected = record;
            RebuildStationList();
            RebuildBody();
        }

        void RebuildBody()
        {
            body.Clear(); fields.Clear();
            AddSection("STATION OVERVIEW", "Station identity, ownership and construction progress.");
            if (selected != null)
            {
                AddInfo("Station", selected.name + "  (#" + selected.id + ")");
                AddInfo("Owner", selected.ownerName ?? "Unknown");
                AddInfo("State", selected.underConstruction ? "Under construction" : "Operational");
                AddInfo("System index", selected.system.ToString());
                string installed = selected.addons == null || selected.addons.Count == 0
                    ? "None" : string.Join(", ", selected.addons.Where(a => a != null && a.level > 0).Select(a => ModuleLabel(a.kind)));
                AddInfo("Installed facilities", installed);
                AddAction("Refresh station project status", "/station status " + selected.id);
                AddAction("Show marketplace", "/station market " + selected.id);
                AddAction("Show station storage", "/station storage " + selected.id);
                AddAction("Show ship hangar", "/station ships " + selected.id);
                AddAction("Show dry-dock status", "/station drydock " + selected.id);
            }

            AddSection("BUILD A STATION", "Start a private construction project by entering a known system index. Deposits are made while docked at any station.");
            AddField("system", "System index", "0");
            AddField("name", "New station name", "Frontier Outpost");
            AddAction("Start station project", () => Send("/station start " + Value("system") + " " + Value("name")));
            AddField("project", "Project ID", selected?.id.ToString() ?? "1");
            AddAction("Check project status", () => Send("/station status " + Value("project")));
            AddField("contributeItem", "Material item ID", "156");
            AddField("contributeAmount", "Amount to contribute", "10");
            AddAction("Contribute construction materials", () => Send("/station contribute " + Value("project") + " " + Value("contributeItem") + " " + Value("contributeAmount")));

            if (selected == null) return;
            string id = selected.id.ToString();
            AddSection("STATION MODULES", "Install facilities using materials from your controlling profile while docked at the station.");
            AddAction("Module costs and requirements", "/station modules");
            foreach (string module in new[] { "storage", "refinery", "factory", "hangar", "drydock" })
                AddAction("Install " + ModuleLabel(module), "/station install " + id + " " + module);

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

            AddSection("SHIP HANGAR", "The ordinary hangar stores small and medium bare hulls. Equipment and cargo are transferred into shared station storage; hull upgrades stay with the ship.");
            AddAction("View your personal stored fleet", "/station fleet");
            AddAction("List station hangar ships", "/station ships " + id);
            AddField("fleetIndex", "Personal fleet index", "1");
            AddAction("Store a ship from your fleet", () => Send("/station store-ship " + id + " " + Value("fleetIndex")));
            AddField("hangarIndex", "Station hangar index", "1");
            AddAction("Retrieve a ship to your fleet", () => Send("/station retrieve-ship " + id + " " + Value("hangarIndex")));

            AddSection("DRY DOCK & SHIPYARD", "Dedicated large-hull storage and timed construction of dry-dock-restricted ship blueprints.");
            AddAction("Open dedicated dry-dock shipyard", () => { Close(); NetChat.Notice("Use the DRY DOCK button in the station menu to open the shipyard."); });
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
            AddAction("Create listing", () => Send("/station list-item " + id + " " + Value("marketItem") + " " + Value("marketAmount") + " " + Value("marketPrice")));
            AddField("listingIndex", "Listing index", "0");
            AddField("buyAmount", "Purchase amount", "1");
            AddAction("Buy listing", () => Send("/station buy " + id + " " + Value("listingIndex") + " " + Value("buyAmount")));
            AddAction("Cancel your listing", () => Send("/station delist " + id + " " + Value("listingIndex")));

            AddSection("WORLD DIRECTORY", "Browse all player-built stations and their operational state.");
            AddAction("Refresh station directory", "/station list");
            AddAction("List available systems", "/station systems");
        }

        void AddSection(string title, string description)
        {
            var section = new VisualElement(); section.AddToClassList("player-station-section");
            var label = new Label(title); label.AddToClassList("player-station-section-title"); section.Add(label);
            var note = new Label(description); note.AddToClassList("player-station-note"); section.Add(note);
            body.Add(section);
        }

        void AddInfo(string label, string value)
        {
            var row = new VisualElement(); row.AddToClassList("player-station-info");
            row.Add(new Label(label)); row.Add(new Label(value ?? "")); body.Add(row);
        }

        void AddField(string key, string label, string initial)
        {
            var field = new TextField(label) { value = initial ?? "" };
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

        string Value(string key) => fields.TryGetValue(key, out var field) ? (field.value ?? "").Trim() : "";
        void Send(string command)
        {
            if (string.IsNullOrWhiteSpace(command) || command.Contains("  ")) return;
            NetPanel.Command(command);
            feedback.text = "Request sent. The server response will appear here.";
            feedbackTime = 8f;
        }

        void OnChat(NetChat.Message message)
        {
            if (!open || message == null || message.channel != NetChat.Channel.Notice) return;
            feedback.text = message.text;
            feedbackTime = 8f;
            refresh = 0;
        }

        static string ModuleLabel(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Unknown module";
            return id == "drydock" ? "Dry Dock" : id == "hangar" ? "Ship Hangar" :
                char.ToUpperInvariant(id[0]) + id.Substring(1);
        }
    }
}
