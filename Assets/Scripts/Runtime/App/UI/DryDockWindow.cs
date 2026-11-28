// DryDockWindow.cs
// Player-built station dry-dock shipyard UI. The replicated station record supplies read-only status;
// every construction request is still validated and executed by the authoritative server.

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Multiplayer;
using GoF2Remake.Modding;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public sealed class DryDockWindow
    {
        readonly StationMenu menu;
        readonly StationLevel level;
        readonly VisualElement safeArea, panel, list, detail;
        readonly Label title, statusText, feedbackText, detailTitle, detailText, recipeText;
        readonly Button closeButton, buildButton;
        readonly List<(int productId, int shipIndex, string name, string className)> blueprints =
            new List<(int, int, string, string)>();
        PlayerStationRecord station;
        int selectedProduct = -1, selectedStation = -1;
        float refreshIn;
        bool requestPending, isOpen;

        float feedbackRemaining, requestDeadline;

        public bool IsOpen => isOpen;

        public VisualElement[] NavItems()
        {
            var items = new List<VisualElement> { closeButton };
            foreach (var child in list.Children()) if (child is Button) items.Add(child);
            items.Add(buildButton);
            return items.ToArray();
        }

        public DryDockWindow(StationMenu menu, StationLevel level, VisualElement root)
        {
            this.menu = menu;
            this.level = level;
            safeArea = root.Q("safeArea") ?? root;

            panel = new VisualElement { name = "dryDockWindow" };
            panel.AddToClassList("drydock-window");
            panel.style.display = DisplayStyle.None;
            safeArea.Add(panel);

            var header = new VisualElement();
            header.AddToClassList("drydock-header");
            title = new Label("DRY DOCK / SHIPYARD");
            title.AddToClassList("drydock-title");
            header.Add(title);
            closeButton = new Button(Close) { text = "BACK" };
            closeButton.AddToClassList("drydock-close");
            closeButton.clicked += menu.PlayRelease;
            closeButton.RegisterCallback<PointerDownEvent>(_ => menu.PlayPush(), TrickleDown.TrickleDown);
            header.Add(closeButton);
            panel.Add(header);

            var content = new VisualElement();
            content.AddToClassList("drydock-content");
            list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("drydock-blueprint-list");
            content.Add(list);
            detail = new VisualElement();
            detail.AddToClassList("drydock-details");
            detailTitle = new Label("SELECT A BLUEPRINT");
            detailTitle.AddToClassList("drydock-detail-title");
            detail.Add(detailTitle);
            detailText = new Label();
            detailText.AddToClassList("drydock-detail-text");
            detail.Add(detailText);
            recipeText = new Label();
            recipeText.AddToClassList("drydock-recipe");
            detail.Add(recipeText);
            statusText = new Label();
            statusText.AddToClassList("drydock-status");
            detail.Add(statusText);
            feedbackText = new Label();
            feedbackText.AddToClassList("drydock-feedback");
            detail.Add(feedbackText);
            buildButton = new Button(StartBuild) { text = "BEGIN CONSTRUCTION" };
            buildButton.AddToClassList("drydock-build");
            buildButton.clicked += menu.PlayRelease;
            buildButton.RegisterCallback<PointerDownEvent>(_ => menu.PlayPush(), TrickleDown.TrickleDown);
            detail.Add(buildButton);
            content.Add(detail);
            panel.Add(content);

            var footer = new Label("Construction materials are reserved from station storage when the build begins.");
            footer.AddToClassList("drydock-footer");
            panel.Add(footer);


        }

        public void Dispose()
        {

            panel?.RemoveFromHierarchy();
        }

        public void Update()
        {
            if (level == null || level.Layout == null || !NetGame.Active)
            {
                if (IsOpen) Close();
                return;
            }

            int stationIndex = level.Layout.stationIndex;
            var current = NetPlayerStations.Stations?.FirstOrDefault(s => s != null && s.stationIndex == stationIndex);
            bool hasDock = current != null && !current.retired && !current.destroyed && !current.wreckClaimOpen &&
                current.offeredForClaimUtc == 0 && !current.underConstruction && !NetPlayerStations.StationClosedByCombat(stationIndex) &&
                (current.addons?.Exists(a => a != null && a.kind == "drydock" && a.level > 0) ?? false);
            if (!hasDock)
            {
                if (IsOpen) Close();
                station = null;
                selectedStation = -1;
                return;
            }

            if (selectedStation != current.id)
            {
                station = current;
                selectedStation = current.id;
                if (IsOpen) RebuildBlueprints();
            }
            else station = current;

            if (!IsOpen) return;
            if (requestPending && (Time.realtimeSinceStartup >= requestDeadline || station.dryDockBuilding))
            {
                requestPending = false;
                if (!station.dryDockBuilding)
                {
                    feedbackText.text = "No confirmation received. Check the current status before retrying.";
                    feedbackRemaining = 8f;
                }
            }
            if (feedbackRemaining > 0f && (feedbackRemaining -= Time.unscaledDeltaTime) <= 0f) feedbackText.text = "";
            if ((refreshIn -= Time.unscaledDeltaTime) <= 0f)
            {
                refreshIn = 0.5f;
                RefreshStatus();
            }
        }

        internal void Open()
        {
            Update();
            if (station == null || !NetGame.Active) return;
            menu.CloseHangar();
            panel.style.display = DisplayStyle.Flex;
            isOpen = true;
            panel.BringToFront();
            refreshIn = 0f;
            requestPending = false;
            RebuildBlueprints();
            RefreshStatus();
        }

        internal void Close()
        {
            if (!IsOpen) return;
            panel.style.display = DisplayStyle.None;
            isOpen = false;
            requestPending = false;
        }

        void RebuildBlueprints()
        {
            list.Clear();
            blueprints.Clear();
            int previousProduct = selectedProduct;
            selectedProduct = -1;
            var db = level?.Database;
            if (db == null || station == null) return;

            foreach (var product in Blueprints.Products(db))
            {
                int ship = ModBlueprints.ShipOf(product.index);
                if (ship < 0 || !ModBlueprints.RequiresDryDock(product.index, db) ||
                    ModContent.IsBlueprintRestricted(product.index) || !ModContent.ItemAvailableFor(product.index, "blueprints")) continue;
                var hull = db.Ship(ship);
                string name = hull?.name ?? ("Ship " + ship);
                string className = ShipClassRules.ClassLabel(hull) ?? "Dry-dock restricted";
                blueprints.Add((product.index, ship, name, className));
            }
            blueprints.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));

            foreach (var item in blueprints)
            {
                bool unlocked = Blueprints.IsUnlocked(item.productId) && !NetPlayerStations.IsBlueprintConsumed(item.productId);
                var row = new Button(() => Select(item.productId))
                {
                    text = item.name + "\n" + item.className + (unlocked ? "" : "  ·  LOCKED")
                };
                row.AddToClassList("drydock-blueprint-row");
                if (!unlocked) row.AddToClassList("drydock-blueprint-row--locked");
                if (selectedProduct == item.productId) row.AddToClassList("drydock-blueprint-row--selected");
                list.Add(row);
            }

            if (blueprints.Count == 0)
            {
                list.Add(new Label("No dry-dock-restricted ship blueprints are defined for this world."));
                detailTitle.text = "NO BLUEPRINTS AVAILABLE";
                detailText.text = "Add ship blueprints to the mod's blueprints.json. Frigates, freighters, cruisers and carriers automatically require a dry dock.";
                recipeText.text = "";
                buildButton.SetEnabled(false);
                return;
            }

            Select(blueprints.Any(item => item.productId == previousProduct) ? previousProduct : blueprints[0].productId);
        }

        void Select(int productId)
        {
            int index = blueprints.FindIndex(x => x.productId == productId);
            if (index < 0) { selectedProduct = -1; buildButton.SetEnabled(false); return; }
            selectedProduct = productId;
            var item = blueprints[index];
            detailTitle.text = item.name;
            bool unlocked = Blueprints.IsUnlocked(productId) && !NetPlayerStations.IsBlueprintConsumed(productId);
            var definition = ModBlueprints.Of(productId);
            detailText.text = item.className + "\nBlueprint item ID: " + productId +
                "\nBlueprint status: " + (unlocked ? "UNLOCKED" : "LOCKED") +
                "\nBlueprint consumption: " + (definition != null && definition.consumeBlueprint ? "consumed at start" : "retained");
            var product = level.Database.Item(productId);
            var lines = new List<string> { "REQUIRED MATERIALS" };
            if (product?.blueprint == null || product.blueprint.Count == 0)
                lines.Add("No valid recipe is defined.");
            else
            {
                var required = new SortedDictionary<int, long>();
                foreach (var part in product.blueprint)
                    if (part != null && part.amount > 0)
                        required[part.item] = required.TryGetValue(part.item, out long amount) ? amount + part.amount : part.amount;
                foreach (var pair in required)
                    lines.Add((level.Database.Item(pair.Key)?.name ?? ("Item " + pair.Key)) + "  ×  " + pair.Value.ToString("N0"));
            }
            recipeText.text = string.Join("\n", lines);
            var dockStatus = NetPlayerStations.GetDryDockVisualStatus(station.id);
            bool hasCapacity = dockStatus != null && dockStatus.storedHulls < dockStatus.hullCapacity;
            buildButton.SetEnabled(unlocked && hasCapacity && !requestPending && dockStatus?.operational == true && !(dockStatus?.building ?? true)
                && product?.blueprint != null && product.blueprint.Count > 0);
            foreach (var child in list.Children())
            {
                if (child is Button row)
                    row.EnableInClassList("drydock-blueprint-row--selected", row.text.StartsWith(item.name + "\n", StringComparison.Ordinal));
            }
        }

        void RefreshStatus()
        {
            if (station == null) return;
            var state = NetPlayerStations.GetDryDockVisualStatus(station.id);
            if (state == null)
            {
                statusText.text = "Dry-dock status is unavailable.";
                buildButton.SetEnabled(false);
                return;
            }

            if (state.building)
            {
                long seconds = state.remainingSeconds;
                var remaining = TimeSpan.FromSeconds(Math.Max(0, seconds));
                statusText.text = "CONSTRUCTION IN PROGRESS\n" + state.blueprintName + "\n" +
                    (state.progress * 100f).ToString("0.0") + "% complete  ·  " +
                    remaining.ToString(@"d\.hh\:mm\:ss") + " remaining\n" +
                    "Blueprint " + (state.consumesBlueprint ? "will be consumed" : "will be retained") + "\n" +
                    "Expected completion: " + DateTimeOffset.FromUnixTimeSeconds(state.completesUtc).ToLocalTime().ToString("g");
                buildButton.text = "DRY DOCK OCCUPIED";
                buildButton.SetEnabled(false);
            }
            else
            {
                bool full = state.storedHulls >= state.hullCapacity;
                statusText.text = (full ? "DRY DOCK FULL" : "DRY DOCK IDLE") +
                    "\nStored hulls: " + state.storedHulls + " / " + state.hullCapacity;
                buildButton.text = full ? "DRY DOCK FULL" : "BEGIN CONSTRUCTION";
                if (selectedProduct >= 0) Select(selectedProduct);
                if (full) buildButton.SetEnabled(false);
            }
        }

        void StartBuild()
        {
            if (station == null || selectedProduct < 0 || requestPending ||
                (NetPlayerStations.GetDryDockVisualStatus(station.id)?.building ?? true)) return;
            requestPending = true;
            requestDeadline = Time.realtimeSinceStartup + 10f;
            buildButton.SetEnabled(false);
            statusText.text = "Sending construction request to the server…";
            if (NetPlayer.Local?.RequestStationUiCommand("/station drydock-build " + station.id + " " + selectedProduct, OnBuildReply) != true)
                OnBuildReply("A station request is already pending, or the server is unavailable.");
        }

        void OnBuildReply(string result)
        {
            if (!IsOpen) return;
            feedbackText.text = string.IsNullOrEmpty(result) ? "Request completed." : result;
            feedbackRemaining = 5f;
            requestPending = false;
            RefreshStatus();
            if (station != null && NetPlayerStations.GetDryDockVisualStatus(station.id)?.building == true)
                RebuildBlueprints();
            else
            {
                if (selectedProduct >= 0) Select(selectedProduct);
            }
        }
    }
}
