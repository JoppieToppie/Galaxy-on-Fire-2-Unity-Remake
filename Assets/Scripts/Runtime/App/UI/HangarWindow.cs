// HangarWindow.cs
// The station's Hangar window (HangarWindow 0x171630, HangarList 0x142b88, ListItemWindow 0x159468), driven by
// StationMenu over the 3D hangar. Rules live in Hangar; this is the list, the details and the controls.
//   Shop tab (185): header 173 "Ships" + the dealer's ships, then per type (265 / 266 / 267 / 269 / 270) everything in
//             cargo or in stock. Selected item: station stock (left, sell arrow) and cargo (right, buy arrow), price.
//   Ship tab (183): your ship, then per slot type its slots (mounted item or empty) and the cargo items of that type
//             that could be mounted. Mount (281) / Demount (282), one-per-ship categories swap (287).
// Details: icon on its type frame, stats (ItemInfo), description and known price range.
// Controls: tap / click a row; up / down select (remake: a fresh press wraps round the ends), left / right sell / buy
// (held arrows repeat like HangarWindow::update; remake, HeldTrade: unit by unit, faster and faster, the whole stack in two
// seconds, the Sell all / Buy all; in a multiplayer session the whole hold reaches the host as one message on release),
// Enter / A the row's action, Q / E or LB / RB switch tabs. Sounds: 0x7c row, 0x65 buy (Button_to_ship), 0x64 sell
// (Button_to_station), 0x62 mount, 0x60 demount.
//   Blueprints tab (272, HangarList::initBlueprintTab 0x143208; blueprints_mods.md 1.5): 273 "Available blueprints" with
//             the unlocked ones (progress "N%  (station)", highlighted while the hold carries a missing ingredient) and
//             274 "Products finished" waiting elsewhere. Edit (283) opens the ingredients (tab 4): the right arrow moves
//             one unit from the hold into the blueprint (held arrows repeat); the first unit asks 212 "Start production
//             at this station?" (528 for 210 / 223 without gate routes); at another station than the production station
//             the units wait (FREIGHT) and go in for 200 $ per unit (288; volatile goods 289) on FREIGHT, Complete or
//             leaving the view; Autocomplete (hard-coded English) for int(qty * maxPrice * 1.25) (195). A finished run
//             goes to the hold here (211) or waits at the production station (210).
//             Remake (PR #166): at the production station a unit goes in at once and the left arrow (TAKE) takes the
//             player's own units back (Blueprints.Withdraw, never the story's); a full blueprint waits for Complete; the
//             actions under the list (Shop, Autocomplete, Complete) are rows (up / down, Enter / A).
//   Kaamo Club (Reference/research/kaamo_club.md 6, HangarWindow+0x11d = the owned club): the Shop tab becomes 186
//             "Store": the storage (left) and the hold (right), no prices, free transfers (unsaleable goods refused, 323),
//             mounted items aren't listed (demount first); the stored hulls under 173 "Ships" with their sell value and the
//             row buttons 332 "Use" (336 passengers / 329 same type / 333 -> switch) and 330 "Sell" (334). Buying a ship
//             elsewhere while owning the club asks 327 "sell or keep?" (330 Sell = trade-in, 331 Keep: 328 when the old
//             type is already stored, else the full price and the old hull goes to the club).
// Not yet: the full-screen details window.

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class HangarWindow
    {
        public enum Tab { Ship, Shop, Blueprints, Ships }   // Ships: the Kaamo Club VIP Card's tab (HangarWindowKaamoVip)
        enum RowKind { Header, ShopItem, ShopShip, OwnShip, Slot, CargoItem, Blueprint, Pending, Ingredient, Autocomplete, StoredShip, ShopLink, Complete, VipCard }

        class Row
        {
            public RowKind kind;
            public int item = -1, ship = -1, equipment = -1, type = -1;
            /// <summary>A Ship tab slot row's slot among its type's (Hangar.SlotsOfType), -1 for any other row.</summary>
            public int place = -1;
            public VisualElement element;
            public bool Selectable => kind != RowKind.Header;
            public bool Same(Row o) => o != null && o.kind == kind && o.item == item && o.ship == ship && o.type == type
                                       && (kind != RowKind.Slot || o.equipment == equipment);
        }

        readonly StationMenu menu;
        readonly StationLevel level;
        readonly VisualElement window, details, detailIcon, detailStats, tradeBox, sellButton, buyButton;
        /// <summary>Remake: where the buy arrow sits in the trade box (an ingredient moves it under the blueprint's amount).</summary>
        readonly VisualElement buyHome, sellHome;
        readonly int sellHomeIndex;
        readonly int buyHomeIndex;
        readonly ScrollView list, detailScroll;
        readonly VisualElement detailMods = ItemInfo.NewModBlock();   // a ship's Kaamo Club mods, under its description
        readonly Label detailName, detailSub, detailText, tradeStock, tradeCargo, tradeMounted, tradePrice, cargoLabel, creditsLabel, tradeStockLabel, tradeCargoLabel, sellLabel, buyLabel;
        readonly Button tabShip, tabShop, tabBlueprints, actionButton, actionButton2;
        readonly Button closeButton;   // Modified: RefreshTexts relabels it (root is the constructor's, not a field)
        /// <summary>Tab 4: the blueprint whose ingredients are listed (-1 = the blueprint list).</summary>
        int editing = -1;
        /// <summary>Item+0x3c blueprintAmount: units moved from the hold but not committed yet, per ingredient.</summary>
        readonly Dictionary<int, int> pendingUnits = new Dictionary<int, int>();
        /// <summary>212 "Start production at this station?" answered Yes for this blueprint view.</summary>
        bool startConfirmed;
        /// <summary>Remake: the Shop tab opened from a blueprint's Shop action marks the recipe's goods with their total
        /// amounts (item -> units); until the tab is left.</summary>
        readonly Dictionary<int, int> shopBlueprintMaterials = new Dictionary<int, int>();
        bool shopBlueprintHighlight;
        /// <summary>Remake: the ingredients view's Autocomplete and Complete buttons (rows of their own, AddProductionActions).</summary>
        Label productionComplete, productionAutocomplete;
        /// <summary>Remake: the key hints were last built for the ingredients view (TAKE / ADD).</summary>
        bool hintsForIngredients, hintsForShipTab;
        readonly List<Row> rows = new List<Row>();
        Hangar hangar;
        Row selected;
        Tab tab = Tab.Shop;   // HangarWindow's static lastTab starts at 1 (Shop)
        // Remake: each tab keeps its own scroll and selected row while the window is open (switching tabs carried the list's
        // scroll over to the other tab, opened part-way down); forgotten on Open.
        readonly Dictionary<Tab, float> tabScroll = new Dictionary<Tab, float>();
        readonly Dictionary<Tab, Row> tabSelected = new Dictionary<Tab, Row>();
        readonly Dictionary<Tab, int> tabSelectedIndex = new Dictionary<Tab, int>();
        float? pendingScroll;
        int? pendingKeepIndex;

        // Held trade arrow or key (HangarWindow::update 0x172626); heldPointer -2 = a key / the D-pad.
        int heldDirection, heldPointer = -1;
        /// <summary>Remake (players' feedback): a mounted item dragged to another slot of its type with the mouse or a pen
        /// (HookSlotDrag): the row pressed, the pointer, where it went down, the row under it; dragMoved eats the click that
        /// ends the drag.</summary>
        Row dragRow, dragTarget;
        int dragPointer = -1;
        Vector2 dragStart;
        bool dragging, dragMoved;
        float heldMs;
        int holdTotal, holdDone;   // HeldTrade: the stack counted at the press, the units traded since
        const int KeyHold = -2;

        // Held up / down (list selection): first repeat after 350 ms, then every 80 ms.
        int moveDirection, keyDirection;
        float moveMs;

        public bool IsOpen { get; private set; }

        /* Original: the window's name on the header bar and the details' "Info" tab (hidden in the Remake,
           OriginalHangar.uss); Back's place on the header bar, for the Remake (Open puts it in the footer bar). */
        readonly Label ogTitle, ogInfo;
        readonly VisualElement closeTop;

        public HangarWindow(StationMenu menu, StationLevel level, VisualElement root)
        {
            this.menu = menu;
            this.level = level;
            window = root.Q("hangarWindow");
            list = root.Q<ScrollView>("hangarList");
            // Remake: a scrollbar along the list's right edge, outside the list (its DragScroll takes the list's own presses).
            listBar = new VisualElement();
            listBar.AddToClassList("list-bar");
            // Remake: the bar is the touch area (wider on phones, layout-phone), the slim track and handle drawn inside it.
            var listTrack = new VisualElement { pickingMode = PickingMode.Ignore };
            listTrack.AddToClassList("list-bar-track");
            listBar.Add(listTrack);
            listThumb = new VisualElement();
            listThumb.AddToClassList("list-bar-thumb");
            listBar.Add(listThumb);
            list.parent.Add(listBar);
            listBar.RegisterCallback<PointerDownEvent>(OnListBarDown);
            listBar.RegisterCallback<PointerMoveEvent>(OnListBarMove);
            listBar.RegisterCallback<PointerUpEvent>(OnListBarUp);
            listBar.RegisterCallback<PointerCaptureOutEvent>(e => { if (e.pointerId == listBarPointer) EndListBarDrag(); });
            listBar.RegisterCallback<PointerCancelEvent>(e => { if (e.pointerId == listBarPointer) EndListBarDrag(); });
            listBar.RegisterCallback<DetachFromPanelEvent>(_ => EndListBarDrag());
            listBar.schedule.Execute(UpdateListBar).Every(16);
            details = root.Q("hangarDetails");
            detailIcon = root.Q("detailIcon");
            detailStats = root.Q("detailStats");
            detailScroll = root.Q<ScrollView>("detailScroll");
            detailName = root.Q<Label>("detailName");
            detailSub = root.Q<Label>("detailSub");
            detailText = root.Q<Label>("detailText");
            tradeBox = root.Q("tradeBox");
            sellButton = root.Q("sellButton");
            buyButton = root.Q("buyButton");
            sellHome = sellButton.parent;
            sellHomeIndex = sellHome.IndexOf(sellButton);
            buyHome = buyButton.parent;
            buyHomeIndex = buyHome.IndexOf(buyButton);
            tradeStock = root.Q<Label>("tradeStock");
            tradeCargo = root.Q<Label>("tradeCargo");
            tradeMounted = root.Q<Label>("tradeMounted");
            tradePrice = root.Q<Label>("tradePrice");
            cargoLabel = root.Q<Label>("cargoLabel");
            creditsLabel = root.Q<Label>("creditsLabel");
            // Remake: a multiplayer session's shared stock counts down to its next restock, between the hold and the credits.
            restockLabel = new Label { pickingMode = PickingMode.Ignore };
            restockLabel.AddToClassList("footer-restock");
            restockLabel.AddToClassList("gof-semibold");
            restockLabel.style.display = DisplayStyle.None;
            creditsLabel.parent.Insert(creditsLabel.parent.IndexOf(creditsLabel), restockLabel);
            tabShip = root.Q<Button>("tabShip");
            tabShop = root.Q<Button>("tabShop");
            tabBlueprints = root.Q<Button>("tabBlueprints");
            actionButton = root.Q<Button>("actionButton");
            actionButton2 = root.Q<Button>("actionButton2");
            tradeStockLabel = root.Q<Label>("tradeStockLabel");
            tradeCargoLabel = root.Q<Label>("tradeCargoLabel");

            string T(int id) => Localization.Get(id).UiCaps();
            tabShip.text = T(183);
            tabShop.text = T(185);
            tabBlueprints.text = T(272);
            sellLabel = root.Q<Label>("sellLabel");
            buyLabel = root.Q<Label>("buyLabel");
            closeButton = root.Q<Button>("hangarClose");
            closeButton.text = Localization.Extra("hudBack", "BACK").OgCase();

            // Modified: the main menu's click sound (the release) on the tabs.
            tabShip.clicked += () => { menu.PlayRelease(); SetTab(Tab.Ship); };
            tabShop.clicked += () => { menu.PlayRelease(); SetTab(Tab.Shop); };
            tabBlueprints.clicked += () => { menu.PlayRelease(); SetTab(Tab.Blueprints); };
            root.Q<Button>("hangarClose").clicked += () => { menu.PlayRelease(); if (!Back()) menu.CloseHangar(); };
            // Modified: a "?" beside Back with the help of the tab shown (OptionInfo): Ship 623, Shop 622, Blueprints 625.
            var hangarClose = root.Q<Button>("hangarClose");
            var help = OptionInfo.Icon(() => Localization.Get(tab == Tab.Ship ? 623 : tab == Tab.Blueprints ? 625 : 622));
            help.AddToClassList("window-help");
            help.style.position = Position.Relative;
            help.style.width = help.style.height = 34f;
            hangarClose.parent.Insert(hangarClose.parent.IndexOf(hangarClose), help);
            closeTop = hangarClose.parent;
            ogTitle = new Label { pickingMode = PickingMode.Ignore };
            ogTitle.AddToClassList("hangar-og-title");
            closeTop.Insert(0, ogTitle);
            BuildVipTools(root);   // remake: the Kaamo Club VIP Card's Ships tab, search box and sort button
            ogInfo = new Label { pickingMode = PickingMode.Ignore };
            ogInfo.AddToClassList("hangar-og-info");
            details.Insert(0, ogInfo);
            actionButton.clicked += Action;
            if (actionButton2 != null) actionButton2.clicked += SecondaryAction;
            // Remake: inside a blueprint the trade arrows are TAKE / ADD.
            HookArrow(sellButton, -1);
            HookArrow(buyButton, 1);
            sellFill = HeldTrade.AddFill(sellButton);   // remake: a held arrow fills toward the whole stack (HeldTrade)
            buyFill = HeldTrade.AddFill(buyButton);
            foreach (var b in new VisualElement[] { tabShip, tabShop, tabBlueprints, actionButton, actionButton2 }) if (b != null) b.focusable = false;
            list.focusable = detailScroll.focusable = false;
            detailText.parent?.Insert(detailText.parent.IndexOf(detailText) + 1, detailMods);
            // Remake: a ship's description under its view gets the item window's title bar (280 "Description").
            detailDescTitle = new Label { pickingMode = PickingMode.Ignore };
            detailDescTitle.AddToClassList("info-desc-title");
            detailDescTitle.AddToClassList("gof-semibold");
            detailDescTitle.AddToClassList("detail-desc-title");
            detailDescTitle.style.display = DisplayStyle.None;
            detailText.parent?.Insert(detailText.parent.IndexOf(detailText), detailDescTitle);
            // Remake (#82): a ship's details show it turning in 3D under the name (the item window's view, which a click /
            // tap on it still opens, as "i" does); the shop icon gives it the room.
            // Remake: a row under the name, the ship's stats on the left and its view on the right (half the panel).
            var head = detailIcon.parent;
            previewRow = new VisualElement();
            previewRow.AddToClassList("detail-preview-row");
            previewStats = new VisualElement { pickingMode = PickingMode.Ignore };
            previewStats.AddToClassList("detail-preview-stats");
            previewRow.Add(previewStats);
            // Remake: the view's column, with a ship's buy / use buttons centred under it.
            previewSide = new VisualElement();
            previewSide.AddToClassList("detail-preview-side");
            previewSide.Add(shipPreview.Element);
            actionArea = new VisualElement();
            actionArea.AddToClassList("detail-action-area");
            previewSide.Add(actionArea);
            previewSide.RegisterCallback<GeometryChangedEvent>(_ => FitActions());
            previewRow.Add(previewSide);
            previewRow.style.display = DisplayStyle.None;
            head.parent.Insert(head.parent.IndexOf(head) + 1, previewRow);
            // Remake: any other item's stats (left) beside its description (right), at the bottom where the description was.
            detailsPanel = detailScroll.parent;
            itemRow = new VisualElement();
            itemRow.AddToClassList("detail-item-row");
            itemStats = new ScrollView();
            itemStats.AddToClassList("detail-item-stats");
            itemStats.focusable = false;
            itemRow.Add(itemStats);
            // Remake: the right half: the description, then the item's action (Mount / Demount / Edit ...) under all of it.
            itemRight = new VisualElement();
            itemRight.AddToClassList("detail-item-right");
            itemRow.Add(itemRight);
            rowActions = new VisualElement { pickingMode = PickingMode.Ignore };
            rowActions.AddToClassList("row-actions");
            itemRow.style.display = DisplayStyle.None;
            detailsPanel?.Add(itemRow);
            // Remake: a short panel (a phone at a larger interface size) scrolls a ship's view and stats with its description.
            detailScroll.RegisterCallback<GeometryChangedEvent>(_ => FitDescription());
            shipPreview.Element.AddToClassList("detail-preview");
            // Remake: framed like the item window's view (its proportions, distance and floor under the ship).
            shipPreview.KeepAspect = true;
            shipPreview.FloorUnderShip = true;
            shipPreview.FloorHeightRatio = 160f / 318f;   // the item window's floor against its box
            shipPreview.AutoSpin = 1.2f;   // 0.3 rad/s
            shipPreview.Clicked += OpenShipView;   // Remake: the ship alone, big (the "i" button still opens the full info)
            shipPreview.Hide();
        }

        /// <summary>The details panel's turning ship (a stage of its own beside the item window's).</summary>
        readonly ShipPreview shipPreview = new ShipPreview(new Vector3(0f, -24000f, 0f), 1268, 636, 0.9f);   // Remake: the item window's view (was 1400 x 460, 0.6)
        /// <summary>Remake: the ship's stats (left) beside its view (right).</summary>
        readonly VisualElement previewRow, previewStats, previewSide, actionArea;
        readonly Label detailDescTitle;
        /// <summary>Remake: the details panel; any other item's row of stats (left) and description (right).</summary>
        readonly VisualElement detailsPanel, itemRow;
        readonly ScrollView itemStats;
        readonly VisualElement itemRight;
        /// <summary>Remake (a player's feedback 2026-11-03): any other row's actions (Mount / Demount / Move, Edit...) in the
        /// list right under the selected row, as the original's "Actions" rows (HangarList::fillBuyList); PlaceRowActions.</summary>
        readonly VisualElement rowActions;
        /// <summary>Remake: a ship's view and stats sit inside the description's scroll (FitDescription).</summary>
        bool previewScrolls;
        /// <summary>Remake: the least height of a ship's description (its title and about three lines) before its view and
        /// stats scroll with it; DescSlack keeps it from flipping back and forth at the edge.</summary>
        const float MinDescHeight = 130f, DescSlack = 40f;

        /// <summary>Remake: a click / tap on the turning ship shows it alone in the item window's box.</summary>
        void OpenShipView()
        {
            int ship = PreviewShip();
            if (ship < 0 || menu.InfoWindow == null) return;
            menu.InfoWindow.ShowShipView(level.Database, ship);
            menu.PlayClip(Flight.CombatAudio.Load()?.buttonInfo);
        }

        /// <summary>The ship the selected row shows in 3D (a ship row or a mod's ship blueprint), -1 = the icon.</summary>
        int PreviewShip()
        {
            if (selected == null || !IsOpen) return -1;
            int item = selected.kind == RowKind.Slot && selected.equipment >= 0 ? Session.Equipment[selected.equipment].item : selected.item;
            if (item >= 0) return tab == Tab.Blueprints ? Modding.ModBlueprints.ShipOf(item) : -1;
            return selected.ship;
        }

        void UpdatePreview()
        {
            int ship = PreviewShip();
            if (ship >= 0) shipPreview.Show(level.Database, ship); else shipPreview.Hide();
            detailIcon.style.display = shipPreview.Shown ? DisplayStyle.None : DisplayStyle.Flex;
            // Remake: a ship's stats sit beside its view and its description right under them, above the trade / buy
            // controls; other items keep both at the bottom.
            bool ship3d = shipPreview.Shown;
            previewRow.style.display = ship3d ? DisplayStyle.Flex : DisplayStyle.None;
            detailDescTitle.text = Localization.Get(280).UiCaps();
            detailDescTitle.style.display = DisplayStyle.Flex;
            // A ship's head is empty: its view and stats say it (the item window's hint 643 stood there until 2026-10-18).
            detailName.style.display = ship3d ? DisplayStyle.None : DisplayStyle.Flex;
            detailSub.style.display = ship3d ? DisplayStyle.None : DisplayStyle.Flex;
            // Remake: the stats beside the ship's view, or beside any other item's description (left of it).
            var statsHome = ship3d ? previewStats : itemStats.contentContainer;
            if (detailStats.parent != statsHome) statsHome.Add(detailStats);
            var panel = detailsPanel;
            if (panel != null)
            {
                // The description: under the ship's row, or the right half of the item's row at the bottom.
                if (ship3d) PlacePreviewRow();
                else
                {
                    if (previewRow.parent != panel) PlacePreviewRow();
                    if (detailScroll.parent != itemRight) itemRight.Insert(0, detailScroll);
                }
                itemRow.style.display = ship3d ? DisplayStyle.None : DisplayStyle.Flex;
                detailScroll.EnableInClassList("detail-scroll--side", !ship3d);
                // The action buttons: centred in the space under the ship's view, else in the list under the selected row
                // (PlaceRowActions, once ShowDetails has picked them; they sat under the item's description until 2026-11-05).
                foreach (var b in new[] { actionButton, actionButton2 })
                {
                    if (b == null) continue;
                    if (ship3d && b.parent != actionArea) actionArea.Add(b);
                    else if (!ship3d && b.parent != rowActions) rowActions.Add(b);
                    b.EnableInClassList("detail-action--side", ship3d);
                    b.EnableInClassList("detail-action--row", !ship3d);
                }
            }
            FitActions();
        }

        /// <summary>Remake: a ship's row (view and stats) under its head and the description under it; or, on a panel too
        /// short for both (a phone at 100 % interface size or more: the row took all the height and the description and
        /// its Kaamo Club mods were squeezed to nothing), the row at the top of the description's scroll.</summary>
        void PlacePreviewRow()
        {
            var panel = detailsPanel;
            if (panel == null) return;
            bool inScroll = previewScrolls && shipPreview.Shown;
            int at = panel.IndexOf(detailIcon.parent) + 1;
            if (inScroll)
            {
                if (previewRow.parent != detailScroll.contentContainer || detailScroll.contentContainer.IndexOf(previewRow) != 0)
                    detailScroll.contentContainer.Insert(0, previewRow);
            }
            else if (previewRow.parent != panel || panel.IndexOf(previewRow) != at) panel.Insert(at, previewRow);
            if (!shipPreview.Shown) return;
            int scrollAt = inScroll ? at : at + 1;
            if (detailScroll.parent != panel || panel.IndexOf(detailScroll) != scrollAt) panel.Insert(scrollAt, detailScroll);
        }

        /// <summary>Remake: moves a ship's row into the description's scroll when the description gets under MinDescHeight,
        /// and back out when it would keep that much (and DescSlack) beside it.</summary>
        void FitDescription()
        {
            if (!shipPreview.Shown || detailScroll.parent != detailsPanel) return;
            float h = detailScroll.layout.height;
            if (float.IsNaN(h)) return;
            bool want = previewScrolls
                ? h - previewRow.layout.height - previewRow.resolvedStyle.marginTop < MinDescHeight + DescSlack
                : h < MinDescHeight;
            if (want == previewScrolls) return;
            previewScrolls = want;
            PlacePreviewRow();
        }

        // ---- open / close ------------------------------------------------------------------------------------

        /// <summary>Modified: the labels set while this window was built, read again from the table loaded now
        /// (StationMenu.RefreshTexts on Localization.Changed). What Open / Rebuild writes already reads it as it goes.</summary>
        public void RefreshTexts()
        {
            string T(int id) => Localization.Get(id).UiCaps();
            tabShip.text = T(183);
            tabShop.text = T(185);
            tabBlueprints.text = T(272);
            if (closeButton != null) closeButton.text = Localization.Extra("hudBack", "BACK").OgCase();
            if (detailDescTitle != null) detailDescTitle.text = Localization.Get(280).UiCaps();
            RefreshVipTexts();
            if (IsOpen)
            {
                // Not SetTab: it commits pending trades and auto-equips (game actions, not a re-label). Rebuild only lays
                // the rows out again, keeping the selection and the scroll.
                tabShop.text = Localization.Get(hangar.Storage ? 186 : 185).UiCaps();   // "Store" at the owned club
                Rebuild();
            }
        }

        public void Open()
        {
            IsOpen = true;
            PlaceBack();
            hangar = new Hangar(level.Database, level.Stock);   // prices are recomputed on every open, like the original
            tabShop.text = Localization.Get(hangar.Storage ? 186 : 185).UiCaps();   // "Store" at the owned club
            ResetVipTools();
            ClearBlueprintShopHighlight();
            selected = null;
            tabScroll.Clear();
            tabSelected.Clear();
            tabSelectedIndex.Clear();
            Rebuild();
        }

        /* Original (f212 / f208): Back at the footer bar's left end, the cargo and credits beside it; the Remake keeps it on
           the header bar. Checked on every open, so a change of the interface style takes effect the next time. */
        void PlaceBack()
        {
            ogTitle.text = Localization.Get(167);
            ogInfo.text = Localization.Get(390);
            var footer = cargoLabel.parent;
            if (Settings.OriginalUi) { if (closeButton.parent != footer) footer.Insert(0, closeButton); }
            else if (closeButton.parent != closeTop) closeTop.Add(closeButton);
        }

        /// <summary>Multiplayer: the shared stock changed (another player's trade, the reset): the list again.</summary>
        public void StockChanged()
        {
            if (!IsOpen) return;
            if (editing >= 0 || HasPending) { stockDirty = true; return; }   // after the blueprint view (Back / SetTab)
            stockDirty = false;
            hangar = new Hangar(level.Database, level.Stock);
            Rebuild();
        }

        bool stockDirty;

        /// <summary>A change that waited (StockChanged during the blueprint view): the list with its prices again.</summary>
        void CatchUpStock()
        {
            if (!stockDirty || !IsOpen) return;
            stockDirty = false;
            hangar = new Hangar(level.Database, level.Stock);
        }

        public void Close()
        {
            if (!IsOpen) return;
            AutoEquip();
            if (HasPending) Revert();
            if (searchFocused) { searchField.Blur(); SetSearchFocus(false); }
            IsOpen = false;
            ClearBlueprintShopHighlight();
            editing = -1;
            ReleaseArrow();
            UpdatePreview();
        }

        /// <summary>HangarWindow::readyToClose: an uncommitted shipment to another station asks first.</summary>
        public bool ReadyToClose()
        {
            if (!HasPending) return true;
            Commit(() => menu.CloseHangar());   // 288 Yes ships them, No takes them back to the hold; then it closes
            return false;
        }

        /// <summary>HangarWindow::OnTouchEnd case 0: the selected row's details in the full-screen window, sound 97.</summary>
        public void OpenInfo()
        {
            if (selected == null || menu.InfoWindow == null) return;
            var db = level.Database;
            int item = selected.kind == RowKind.Slot && selected.equipment >= 0 ? Session.Equipment[selected.equipment].item : selected.item;
            if (item >= 0) menu.InfoWindow.ShowItem(db, item, hangar.SystemIndex, selected.kind == RowKind.ShopItem && !hangar.Storage, hangar.PriceOf(item));
            else if (selected.ship >= 0) menu.InfoWindow.ShowShip(db, selected.ship, hangar.ShipPrice(selected.ship));
            else return;
            menu.PlayClip(Flight.CombatAudio.Load()?.buttonInfo);
        }

        /// <summary>Back inside the window: the ingredient list returns to the blueprint list (after committing).</summary>
        public bool Back()
        {
            if (editing < 0) return false;
            if (HasPending) { Commit(() => Back()); return true; }
            editing = -1; selected = null; CatchUpStock(); Rebuild();
            return true;
        }

        public void SetTab(Tab t)
        {
            if (t == Tab.Ships && !VipTools) return;   // the VIP Card's tab, only with its perks at the club
            if (tab == t && rows.Count > 0 && editing < 0) return;
            if (tab == Tab.Shop && t != Tab.Shop) ClearBlueprintShopHighlight();
            // Confirm freight for the entire pending shipment before leaving the recipe.
            if (HasPending) { Commit(() => SetTab(t)); return; }
            AutoEquip();
            if (editing < 0)   // Remake: this tab's place
            {
                tabScroll[tab] = list.scrollOffset.y;
                tabSelected[tab] = selected;
                tabSelectedIndex[tab] = selected != null ? rows.IndexOf(selected) : -1;
            }
            tab = t;
            editing = -1;
            // Remake: the new tab where it was left (its first visit: the top), not at the old tab's scroll.
            selected = tabSelected.TryGetValue(t, out var keepRow) ? keepRow : null;
            pendingScroll = tabScroll.TryGetValue(t, out float keepScroll) ? keepScroll : 0f;
            // Its row gone meanwhile (the last unit sold or bought): the nearest one, as within a tab (same category).
            pendingKeepIndex = tabSelectedIndex.TryGetValue(t, out int keepIndexOfTab) ? keepIndexOfTab : -1;
            CatchUpStock();
            Rebuild();
        }

        /// <summary>Remake: an ingredient's SOLD HERE badge: the Shop tab on that item (the blueprint's pending units are
        /// committed first, as on any tab switch).</summary>
        int focusShopItem = -1;
        void GoToShopItem(int item)
        {
            if (editing < 0) return;
            shopBlueprintMaterials.Clear();
            var parts = level.Database.Item(editing)?.blueprint;
            if (parts == null) return;
            // Recipe totals, not outstanding quantities: unchanged by cargo or manufacturing progress.
            foreach (var part in parts)
            {
                if (shopBlueprintMaterials.TryGetValue(part.item, out int total))
                    shopBlueprintMaterials[part.item] = total + part.amount;
                else
                    shopBlueprintMaterials.Add(part.item, part.amount);
            }
            shopBlueprintHighlight = true;
            focusShopItem = item;   // -1 = ordinary SHOP; otherwise select this SOLD HERE commodity
            SetTab(Tab.Shop);
        }

        // ---- the list's scrollbar (remake) ----
        readonly VisualElement listBar, listThumb;
        int listBarPointer = -1;
        float listBarGrab;   // where on the handle it was grabbed (panel units from its top)

        // ---- details -----------------------------------------------------------------------------------------

        readonly Label restockLabel;
        bool restockVisible;

        // ---- held trade arrows ---------------------------------------------------------------------------------

        bool holdBatch;
        VisualElement sellFill, buyFill;

        /// <summary>HangarWindow::update's repeat of a held arrow; remake (HeldTrade): unit by unit, faster and faster, the whole
        /// stack counted at the press in two seconds (the separate Sell all / Buy all went) while the arrow fills; running out
        /// of credits just stops.</summary>
        public void Update(float dtMs)
        {
            UpdateRestock();
            if (!IsOpen || heldDirection == 0) return;
            int dir = heldDirection;
            int units = HeldTrade.Step(ref heldMs, ref holdDone, holdTotal, dtMs);
            HeldTrade.SetFill(dir < 0 ? sellFill : buyFill, HeldTrade.Progress(holdDone, holdTotal));
            if (units > 0 && Trade(dir, false, units, true)) TradeTick(dir);   // each unit's button sound, pitched up
        }
    }
}
