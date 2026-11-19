// DryDockWindow.cs
// Player-built station dry-dock shipyard UI. The replicated station record supplies read-only status;
// every construction request is still validated and executed by the authoritative server.

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Multiplayer;
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
        readonly Label title, statusText, detailTitle, detailText, recipeText;
        readonly Button openButton, closeButton, buildButton;
        readonly List<(int productId, int shipIndex, string name, string className)> blueprints =
            new List<(int, int, string, string)>();
        PlayerStationRecord station;
        int selectedProduct = -1, selectedStation = -1;
        float refreshIn;
        bool requestPending;

        public bool IsOpen => panel != null && panel.style.display == DisplayStyle.Flex;

        public DryDockWindow(StationMenu menu, StationLevel level, VisualElement root)
        {
            this.menu = menu;
            this.level = level;
            safeArea = root.Q("safeArea") ?? root;

            openButton = new Button(Open) { name = "dryDockButton", text = "DRY DOCK" };
            openButton.AddToClassList("station-button");
            openButton.AddToClassList("gof-semibold");
            openButton.AddToClassList("drydock-menu-button");
            var buttons = root.Q("sidePanel")?.Q(className: "station-buttons");
            if (buttons != null)
            {
                int before = buttons.IndexOf(root.Q("inspectButton"));
                buttons.Insert(before >= 0 ? before : buttons.childCount, openButton);
            }
            else safeArea.Add(openButton);
            openButton.style.display = DisplayStyle.None;
            openButton.clicked += menu.PlayRelease;
            openButton.RegisterCallback<PointerDownEvent>(_ => menu.PlayPush(), TrickleDown.TrickleDown);

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

            NetChat.Added += OnChat;
        }

        public void Update()
        {
            if (level == null || level.Layout == null || !NetGame.Active)
            {
                openButton.style.display = DisplayStyle.None;
                if (IsOpen) Close();
                return;
            }

            int stationIndex = level.Layout.stationIndex;
            var current = NetPlayerStations.Stations?.FirstOrDefault(s => s != null && s.stationIndex == stationIndex);
            bool hasDock = current != null && !current.underConstruction &&
                (current.addons?.Exists(a => a != null && a.kind == "drydock" && a.level > 0) ?? false);
            openButton.style.display = hasDock ? DisplayStyle.Flex : DisplayStyle.None;
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
            if ((refreshIn -= Time.unscaledDeltaTime) <= 0f)
            {
                refreshIn = 0.5f;
                RefreshStatus();
            }
        }

        void Open()
        {
            if (station == null || !NetGame.Active) return;
            menu.CloseHangar();
            panel.style.display = DisplayStyle.Flex;
            panel.BringToFront();
            refreshIn = 0f;
            requestPending = false;
            RebuildBlueprints();
            RefreshStatus();
        }

        void Close()
        {
            if (!IsOpen) return;
            panel.style.display = DisplayStyle.None;
            requestPending = false;
        }

        void RebuildBlueprints()
        {
            list.Clear();
            blueprints.Clear();
            selectedProduct = -1;
            var db = level?.Database;
            if (db == null || station == null) return;

            foreach (var product in Blueprints.Products(db))
            {
                int ship = Modding.ModBlueprints.ShipOf(product.index);
                if (ship < 0 || !Modding.ModBlueprints.RequiresDryDock(product.index, db)) continue;
                var hull = db.Ship(ship);
                string name = hull?.name ?? ("Ship " + ship);
                string className = Modding.ShipClassRules.ClassLabel(hull) ?? "Dry-dock restricted";
                blueprints.Add((product.index, ship, name, className));
            }
            blueprints.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));

            foreach (var item in blueprints)
            {
                bool unlocked = Blueprints.IsUnlocked(item.productId);
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

            Select(blueprints[0].productId);
        }

        void Select(int productId)
        {
            selectedProduct = productId;
            var item = blueprints.FirstOrDefault(x => x.productId == productId);
            if (item.productId < 0) return;
            detailTitle.text = item.name;
            bool unlocked = Blueprints.IsUnlocked(productId);
            detailText.text = item.className + "\nBlueprint item ID: " + productId +
                "\nBlueprint status: " + (unlocked ? "UNLOCKED" : "LOCKED");
            var product = level.Database.Item(productId);
            var lines = new List<string> { "REQUIRED MATERIALS" };
            if (product?.blueprint == null || product.blueprint.Count == 0)
                lines.Add("No valid recipe is defined.");
            else
                foreach (var part in product.blueprint)
                    lines.Add((level.Database.Item(part.item)?.name ?? ("Item " + part.item)) + "  ×  " + part.amount);
            recipeText.text = string.Join("\n", lines);
            buildButton.SetEnabled(unlocked && !requestPending && !(NetPlayerStations.GetDryDockVisualStatus(station.id)?.building ?? false)
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
                    remaining.ToString(@"d\.hh\:mm\:ss") + " remaining";
                buildButton.text = "DRY DOCK OCCUPIED";
                buildButton.SetEnabled(false);
            }
            else
            {
                statusText.text = "DRY DOCK IDLE\nStored hulls: " + state.storedHulls + " / " + state.hullCapacity;
                buildButton.text = "BEGIN CONSTRUCTION";
                if (selectedProduct >= 0) Select(selectedProduct);
            }
        }

        void StartBuild()
        {
            if (station == null || selectedProduct < 0 || requestPending ||
                (NetPlayerStations.GetDryDockVisualStatus(station.id)?.building ?? true)) return;
            requestPending = true;
            buildButton.SetEnabled(false);
            statusText.text = "Sending construction request to the server…";
            NetPanel.Command("/station drydock-build " + station.id + " " + selectedProduct);
        }

        void OnChat(NetChat.Message message)
        {
            if (!IsOpen || message == null || message.channel != NetChat.Channel.Notice) return;
            statusText.text = message.text;
            requestPending = false;
            RefreshStatus();
            if (station != null && NetPlayerStations.GetDryDockVisualStatus(station.id)?.building == true)
                RebuildBlueprints();
            else if (selectedProduct >= 0) Select(selectedProduct);
        }
    }
}
