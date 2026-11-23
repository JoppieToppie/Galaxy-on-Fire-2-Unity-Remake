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
        internal Button MenuButton => openButton;
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
            if (TextFieldKeys.InTextField(panel.focusController?.focusedElement)) return;
            if ((refresh -= Time.unscaledDeltaTime) <= 0)
            {
                refresh = 2f;
                RebuildStationList();
                RebuildBody(); // Keep public procurement quantities and escrow current while the window remains open.
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
            // Management is contextual, not a world directory: only expose the player station at the location
            // where this management screen is open. The server still enforces owner/faction permissions per command.
            int currentStation = level?.Layout != null ? level.Layout.stationIndex : -1;
            var records = NetPlayerStations.Stations?.Where(x => x != null && x.stationIndex == currentStation && (x.offeredForClaimUtc > 0 || NetPlayerStations.CanViewManagement(x) || (x.buyOrders != null && x.buyOrders.Any(order => order != null && order.remaining > 0)))).OrderBy(x => x.id).ToList();
            if (records == null || records.Count == 0)
            {
                stationList.Add(new Label("No managed station or public construction buy orders are available at this location."));
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
                if (selected.offeredForClaimUtc > 0)
                {
                    AddInfo("Claim price", NetPlayerStations.StationClaimPrice.ToString("N0") + " credits");
                    AddAction("Claim station", "/station claim " + selected.id);
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
                AddField("transferKind", "Transfer recipient type (player/faction)", "player");
                AddField("transferAccount", "Recipient profile account ID", "");
                AddAction("Propose ownership transfer", () => Send("/station transfer " + selected.id + " " + Value("transferKind") + " " + Value("transferAccount")));
                AddAction("Accept pending transfer", "/station transfer-accept " + selected.id);
                AddAction("Cancel pending transfer", "/station transfer-cancel " + selected.id);
                AddInfo("System index", selected.system.ToString());
                string installed = selected.addons == null || selected.addons.Count == 0
                    ? "None" : string.Join(", ", selected.addons.Where(a => a != null && a.level > 0).Select(a => ModuleLabel(a.kind)));
                AddInfo("Installed facilities", installed);
                AddAction("Refresh station project status", "/station status " + selected.id);
                AddAction("Show marketplace", "/station market " + selected.id);
                AddAction("Show station storage", "/station storage " + selected.id);
                AddAction("Show ship hangar", "/station ships " + selected.id);
                AddAction("Show dry-dock status", "/station drydock " + selected.id);
                if (!NetPlayerStations.CanViewManagement(selected))
                {
                    BuildConstructionProcurement(selected.id.ToString(), false);
                    return;
                }
            }

            if (selected != null)
            {
                AddField("stationName", "New station name", selected.name);
                AddAction("Rename station", () => Send("/station rename " + selected.id + " " + Value("stationName")));
                if (!selected.underConstruction)
                    AddInfo("Public claim", "Owners can use /station retire " + selected.id + " to offer this station for 25,000,000 credits.");
                AddSection("MARKETPLACE", "Ship sales use the personal stored fleet and the station's sales tax.");
                AddField("salesTax", "Sales tax (0–10%)", selected.marketplaceTaxPercent.ToString());
                AddAction("Set marketplace tax", () => Send("/station tax " + selected.id + " " + Value("salesTax")));
                AddAction("List personal stored hulls", "/station fleet");
                AddField("marketFleetIndex", "Personal stored hull number", "1");
                AddField("marketHullPrice", "Hull price in credits", "");
                AddAction("List hull for sale", () => Send("/station list-ship " + selected.id + " " +
                    Value("marketFleetIndex") + " " + Value("marketHullPrice")));
                AddField("marketShipListing", "Ship listing number", "1");
                AddAction("Buy listed hull", () => Send("/station buy-ship " + selected.id + " " + Value("marketShipListing")));
                AddAction("Cancel my hull listing", () => Send("/station delist-ship " + selected.id + " " + Value("marketShipListing")));
                AddAction("View station construction stock", "/station faction-materials");
                AddField("factionDonationItem", "Donated material item ID", "156");
                AddField("factionDonationAmount", "Amount to donate", "25");
                AddAction("Donate to this station’s construction stock", () => Send("/station faction-donate " +
                    Value("factionDonationItem") + " " + Value("factionDonationAmount")));
                AddSection("PLAYER-BUILT JUMPGATES", "Sponsor construction from this station, but place the gate in any orbital location in this system. Each orbit can contain only one gate. Owners and faction managers can change an operational gate's destination once every 24 hours.");
                AddField("jumpgateOrbit", "Orbit station index (see orbital list)", selected.stationIndex.ToString());
                AddAction("List this system's orbital locations", "/station jumpgate-orbits " + selected.system);
                AddField("jumpgateDestination", "Initial destination system index", "0");
                AddField("jumpgateBuildSeconds", "Optional build time in seconds (0 = no timer)", "0");
                AddAction("Start jumpgate project in selected orbit", () => Send("/station jumpgate " + selected.id + " " +
                    Value("jumpgateOrbit") + " " + Value("jumpgateDestination") + " " + Value("jumpgateBuildSeconds")));
                AddAction("View jumpgate project status", () => Send("/station jumpgate-status " + Value("jumpgateOrbit")));
                AddField("jumpgateNewDestination", "New destination system index", "0");
                AddAction("Change destination (24-hour cooldown)", () => Send("/station jumpgate-destination " +
                    Value("jumpgateOrbit") + " " + Value("jumpgateNewDestination")));
                AddField("jumpgateItem", "Contribution material item ID", "156");
                AddField("jumpgateAmount", "Contribution amount (up to 500)", "25");
                AddAction("Contribute jumpgate materials", () => Send("/station jumpgate-contribute " + Value("jumpgateOrbit") + " " +
                    Value("jumpgateItem") + " " + Value("jumpgateAmount")));
            }

            AddSection("BUILD A STATION", "Start a private construction project by entering a known system index. Deposits are made while docked at any station. Faction-owned projects pay each contributor immediately from the faction bank; individual projects do not create faction-bank obligations.");
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

            AddSection("JUMPGATE NETWORK", "Use this station as the sponsor to build a gate at any existing orbital location in this system. Each orbit can hold one player-built gate; each gate has one destination. Cost: 500 Iron, 250 Titanium, 200 Electronics and 150 Microchips. A return route requires another gate in the destination system.");
            AddAction("List orbital locations in this system", "/station jumpgate-orbits " + selected.system);

            var systemGates = NetPlayerStations.Jumpgates?.Where(g => g != null && g.system == selected.system &&
                    NetPlayerStations.CanViewJumpgateManagement(NetPlayer.Local, g))
                .OrderBy(g => g.orbitStationIndex).ToList();
            if (systemGates == null || systemGates.Count == 0)
                AddInfo("Your gates in this system", "None currently built or under construction.");
            else
            {
                foreach (var gate in systemGates)
                {
                    string orbitId = gate.orbitStationIndex.ToString();
                    string orbitName = NetGame.Db?.Stations.Find(x => x != null && x.index == gate.orbitStationIndex)?.name
                        ?? ("Orbit " + orbitId);
                    string destinationName = NetGame.Db?.Systems.Find(x => x != null && x.index == gate.destinationSystem)?.name
                        ?? ("System " + gate.destinationSystem);
                    AddInfo("Gate · " + orbitName + " (" + orbitId + ")", "Destination: " + destinationName);
                    AddAction("Refresh gate status · orbit " + orbitId, "/station jumpgate-status " + orbitId);
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
                        AddField(typeKey, "Blacklist entry type · player or faction", "player");
                        AddField(targetKey, "Blacklist account or faction ID", "");
                        AddAction("Add to gate blacklist", () =>
                            Send("/station jumpgate-blacklist " + orbitId + " add " + Value(typeKey) + " " + Value(targetKey)));
                        AddAction("Remove from gate blacklist", () =>
                            Send("/station jumpgate-blacklist " + orbitId + " remove " + Value(typeKey) + " " + Value(targetKey)));
                    }
                    if (gate.startedUtc <= 0)
                    {
                        AddInfo("Gate status", "Awaiting construction materials");
                        if (gate.materials != null)
                            foreach (var material in gate.materials)
                                if (material != null)
                                    AddInfo(NetGame.Db?.Item(material.item)?.name ?? ("Item " + material.item),
                                        Math.Max(0, material.delivered) + "/" + Math.Max(0, material.required));
                        AddField("jumpgateItem_" + orbitId, "Contribution item ID · orbit " + orbitId, "156");
                        AddField("jumpgateAmount_" + orbitId, "Contribution amount · orbit " + orbitId, "10");
                        AddAction("Contribute to orbit " + orbitId, () => Send("/station jumpgate-contribute " + orbitId + " " +
                            Value("jumpgateItem_" + orbitId) + " " + Value("jumpgateAmount_" + orbitId)));
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
                        AddField("jumpgateNewDestination_" + orbitId, "New destination system · orbit " + orbitId, gate.destinationSystem.ToString());
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

            AddSection("BUILD A JUMPGATE", "The selected station sponsors the project and determines its owner. Choose an existing orbit in this system; the original system jumpgate orbit cannot be used. The optional timer starts only after all materials are funded. Use 0 for no timer.");
            if (selected.underConstruction)
                AddInfo("Jumpgate availability", "The sponsoring station must finish construction first.");
            else
            {
                AddField("jumpgateOrbit", "Existing orbit station index", "");
                AddField("jumpgateDestination", "Destination system index", "");
                AddField("jumpgateBuildSeconds", "Build time in seconds (0 = no timer)", "0");
                AddAction("Start jumpgate construction", () =>
                {
                    string orbit = Value("jumpgateOrbit").Trim();
                    string destination = Value("jumpgateDestination").Trim();
                    string buildSeconds = Value("jumpgateBuildSeconds").Trim();
                    if (!int.TryParse(orbit, out _) || !int.TryParse(destination, out _) || !int.TryParse(buildSeconds, out _))
                    {
                        feedback.text = "Enter valid orbit, destination system and build-time indexes.";
                        feedbackTime = 4f;
                        return;
                    }
                    Send("/station jumpgate " + id + " " + orbit + " " + destination + " " + buildSeconds);
                });
            }

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
            AddField("listingIndex", "Listing index", "0");
            AddField("buyAmount", "Purchase amount", "1");
            AddAction("Buy listing", () => Send("/station buy " + id + " " + Value("listingIndex") + " " + Value("buyAmount")));
            AddAction("Cancel your listing", () => Send("/station delist " + id + " " + Value("listingIndex")));
            BuildConstructionProcurement(id, true);


        }

        void BuildConstructionProcurement(string id, bool allowManagement)
        {
            AddSection("CONSTRUCTION PROCUREMENT", "The station owner or an authorised faction officer can reserve funds for missing construction materials. Any pilot docked here can sell into an active order. Payment comes from escrow, so sellers do not also receive a contribution payout.");
            var activeOrders = selected?.buyOrders ?? new List<PlayerStationBuyOrder>();
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
                    AddField("procureSellAmount" + i, "Units to sell into order #" + i, "1");
                    int orderIndex = i;
                    AddAction("Sell " + itemName + " to order #" + i, () =>
                        Send("/station sell-order " + id + " " + orderIndex + " " + Value("procureSellAmount" + orderIndex)));
                    if (allowManagement)
                        AddAction("Cancel order #" + i + " and refund unused escrow", () =>
                            Send("/station cancel-order " + id + " " + orderIndex));
                }
            }
            if (!allowManagement) return;
            if (selected.underConstruction)
            {
                AddInfo("Procurement funding", selected.ownershipType == PlayerStationOwnershipType.Faction
                    ? "Faction treasury. Orders require leader/officer authority."
                    : "Station owner's personal credits.");
                AddField("buyOrderItem", "Required material item ID", "156");
                AddField("buyOrderAmount", "Units to purchase (1–500)", "10");
                AddField("buyOrderPrice", "Offer per unit (credits)", "100");
                AddAction("Create construction buy order", () => Send("/station buy-order " + id + " " +
                    Value("buyOrderItem") + " " + Value("buyOrderAmount") + " " + Value("buyOrderPrice")));
            }
            else
                AddInfo("Procurement status", "Construction is complete. No further material orders can be created.");
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
