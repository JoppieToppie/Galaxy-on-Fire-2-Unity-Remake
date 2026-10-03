// HangarWindow.cs
// The station's Hangar window (HangarWindow 0x171630, HangarList 0x142b88, ListItemWindow 0x159468), driven by
// StationMenu over the 3D hangar. Rules live in Hangar; this is the list, the details and the controls.
//   Shop tab (185): header 173 "Ships" + the dealer's ships, then per type (265 / 266 / 267 / 269 / 270) everything in
//             cargo or in stock. Selected item: station stock (left, sell arrow) and cargo (right, buy arrow), price.
//   Ship tab (183): your ship, then per slot type its slots (mounted item or empty) and the cargo items of that type
//             that could be mounted. Mount (281) / Demount (282), one-per-ship categories swap (287).
// Details: icon on its type frame, stats (ItemInfo), description and known price range.
// Controls: tap / click a row; up / down select, left / right sell / buy (held arrows repeat like HangarWindow::update:
// after 200 ms, every 30 ms after 1.5 s, 5 units at a time after 4 s), Enter / A the row's action, Q / E or LB / RB
// switch tabs. Sounds: 0x7c row, 0x65 buy (Button_to_ship), 0x64 sell (Button_to_station), 0x62 mount, 0x60 demount.
//   Blueprints tab (272, HangarList::initBlueprintTab 0x143208; blueprints_mods.md 1.5): 273 "Available blueprints" with
//             the unlocked ones (progress "N%  (station)", highlighted while the hold carries a missing ingredient) and
//             274 "Products finished" waiting elsewhere. Edit (283) opens the ingredients (tab 4): the right arrow moves
//             one unit from the hold into the blueprint (held arrows repeat), invested goods can't be taken back; the
//             first unit asks 212 "Start production at this station?" (528 for 210 / 223 without gate routes); leaving
//             the ingredient commits it, at another station than the production station for 200 $ per unit (288;
//             volatile goods 289); Autocomplete (hard-coded English) for int(qty * maxPrice * 1.25) (195). A finished
//             run goes to the hold here (211) or waits at the production station (210).
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
    public class HangarWindow
    {
        public enum Tab { Ship, Shop, Blueprints }
        enum RowKind { Header, ShopItem, ShopShip, OwnShip, Slot, CargoItem, Blueprint, Pending, Ingredient, Autocomplete, StoredShip }

        class Row
        {
            public RowKind kind;
            public int item = -1, ship = -1, equipment = -1, type = -1;
            public VisualElement element;
            public bool Selectable => kind != RowKind.Header;
            public bool Same(Row o) => o != null && o.kind == kind && o.item == item && o.ship == ship && o.type == type
                                       && (kind != RowKind.Slot || o.equipment == equipment);
        }

        readonly StationMenu menu;
        readonly StationLevel level;
        readonly VisualElement window, details, detailIcon, detailStats, tradeBox, sellButton, buyButton;
        readonly ScrollView list, detailScroll;
        readonly Label detailName, detailSub, detailText, tradeStock, tradeCargo, tradeMounted, tradePrice, cargoLabel, creditsLabel, tradeStockLabel, tradeCargoLabel, sellLabel, buyLabel;
        readonly Button tabShip, tabShop, tabBlueprints, actionButton, actionButton2;
        /// <summary>Tab 4: the blueprint whose ingredients are listed (-1 = the blueprint list).</summary>
        int editing = -1;
        /// <summary>Item+0x3c blueprintAmount: units moved from the hold but not committed yet, per ingredient.</summary>
        readonly Dictionary<int, int> pendingUnits = new Dictionary<int, int>();
        bool startConfirmed;
        readonly List<Row> rows = new List<Row>();
        Hangar hangar;
        Row selected;
        Tab tab = Tab.Shop;   // HangarWindow's static lastTab starts at 1 (Shop)

        // Held trade arrow or key (HangarWindow::update 0x172626); heldPointer -2 = a key / the D-pad.
        int heldDirection, heldPointer = -1;
        float heldMs, repeatMs;
        const int KeyHold = -2;

        // Held up / down (list selection): first repeat after 350 ms, then every 80 ms.
        int moveDirection, keyDirection;
        float moveMs;

        public bool IsOpen { get; private set; }

        public HangarWindow(StationMenu menu, StationLevel level, VisualElement root)
        {
            this.menu = menu;
            this.level = level;
            window = root.Q("hangarWindow");
            list = root.Q<ScrollView>("hangarList");
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
            tradeStock = root.Q<Label>("tradeStock");
            tradeCargo = root.Q<Label>("tradeCargo");
            tradeMounted = root.Q<Label>("tradeMounted");
            tradePrice = root.Q<Label>("tradePrice");
            cargoLabel = root.Q<Label>("cargoLabel");
            creditsLabel = root.Q<Label>("creditsLabel");
            tabShip = root.Q<Button>("tabShip");
            tabShop = root.Q<Button>("tabShop");
            tabBlueprints = root.Q<Button>("tabBlueprints");
            actionButton = root.Q<Button>("actionButton");
            actionButton2 = root.Q<Button>("actionButton2");
            tradeStockLabel = root.Q<Label>("tradeStockLabel");
            tradeCargoLabel = root.Q<Label>("tradeCargoLabel");

            string T(int id) => Localization.Get(id).ToUpperInvariant();
            tabShip.text = T(183);
            tabShop.text = T(185);
            tabBlueprints.text = T(272);
            sellLabel = root.Q<Label>("sellLabel");
            buyLabel = root.Q<Label>("buyLabel");
            root.Q<Button>("hangarClose").text = Localization.Extra("hudBack", "BACK");

            tabShip.clicked += () => { menu.PlayPush(); SetTab(Tab.Ship); };
            tabShop.clicked += () => { menu.PlayPush(); SetTab(Tab.Shop); };
            tabBlueprints.clicked += () => { menu.PlayPush(); SetTab(Tab.Blueprints); };
            root.Q<Button>("hangarClose").clicked += () => { menu.PlayRelease(); if (!Back()) menu.CloseHangar(); };
            actionButton.clicked += Action;
            if (actionButton2 != null) actionButton2.clicked += SecondaryAction;
            HookArrow(sellButton, -1);
            HookArrow(buyButton, 1);
            foreach (var b in new VisualElement[] { tabShip, tabShop, tabBlueprints, actionButton, actionButton2 }) if (b != null) b.focusable = false;
            list.focusable = detailScroll.focusable = false;
        }

        // ---- open / close ------------------------------------------------------------------------------------

        public void Open()
        {
            IsOpen = true;
            hangar = new Hangar(level.Database, level.Stock);   // prices are recomputed on every open, like the original
            tabShop.text = Localization.Get(hangar.Storage ? 186 : 185).ToUpperInvariant();   // "Store" at the owned club
            selected = null;
            Rebuild();
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
            IsOpen = false;
            editing = -1;
            ReleaseArrow();
        }

        /// <summary>HangarWindow::readyToClose: an uncommitted shipment to another station asks first.</summary>
        public bool ReadyToClose()
        {
            if (!HasPending) return true;
            Commit(() => menu.CloseHangar());
            return false;
        }

        /// <summary>Back inside the window: the ingredient list returns to the blueprint list (after committing).</summary>
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

        public bool Back()
        {
            if (editing < 0) return false;
            Commit(() => { editing = -1; selected = null; CatchUpStock(); Rebuild(); });
            return true;
        }

        public void SetTab(Tab t)
        {
            if (tab == t && rows.Count > 0 && editing < 0) return;
            if (HasPending) { Commit(() => SetTab(t)); return; }
            AutoEquip();
            tab = t;
            editing = -1;
            selected = null;
            CatchUpStock();
            Rebuild();
        }

        public void NextTab() => SetTab(tab == Tab.Ship ? Tab.Shop : tab == Tab.Shop ? Tab.Blueprints : Tab.Ship);

        // ---- list --------------------------------------------------------------------------------------------

        void Rebuild()
        {
            if (!IsOpen) return;
            tabShip.EnableInClassList("hangar-tab--active", tab == Tab.Ship);
            tabShop.EnableInClassList("hangar-tab--active", tab == Tab.Shop);
            tabBlueprints.EnableInClassList("hangar-tab--active", tab == Tab.Blueprints);
            var keep = selected;
            float scroll = list.scrollOffset.y;
            rows.Clear();
            list.Clear();
            string T(int id) => Localization.Get(id).ToUpperInvariant();
            int[] typeHeaders = { 265, 266, 267, 269, 270 };

            if (tab == Tab.Shop)
            {
                if (hangar.Storage)
                {
                    // Station::getShips(108): the parked hulls.
                    if (Session.KaamoShips.Count > 0) AddHeader(T(173));
                    for (int i = 0; i < Session.KaamoShips.Count; i++)
                        AddRow(new Row { kind = RowKind.StoredShip, ship = Session.KaamoShips[i].ship, equipment = i });
                }
                else if (hangar.Stock.ships.Count > 0)
                {
                    AddHeader(T(173));
                    foreach (int s in hangar.Stock.ships) AddRow(new Row { kind = RowKind.ShopShip, ship = s });
                }
                var items = hangar.ShopItems();
                for (int type = 0; type <= 4; type++)
                {
                    var ofType = items.Where(i => hangar.TypeOf(i) == type).ToList();
                    if (ofType.Count == 0) continue;
                    AddHeader(T(typeHeaders[type]));
                    foreach (int i in ofType) AddRow(new Row { kind = RowKind.ShopItem, item = i, type = type });
                }
            }
            else if (tab == Tab.Blueprints)
            {
                var db = level.Database;
                if (editing >= 0)
                {
                    // Tab 4 (HangarList::fillIngredientsList 0x14378c): the product, its ingredients, Autocomplete.
                    AddHeader(ItemInfo.ItemName(editing).ToUpperInvariant());
                    var parts = db.Item(editing).blueprint;
                    for (int k = 0; k < parts.Count; k++) AddRow(new Row { kind = RowKind.Ingredient, item = parts[k].item, type = k });
                    AddRow(new Row { kind = RowKind.Autocomplete, item = editing });
                }
                else
                {
                    AddHeader(T(273));
                    foreach (var p in Blueprints.Products(db))
                        if (Blueprints.IsUnlocked(p.index)) AddRow(new Row { kind = RowKind.Blueprint, item = p.index });
                    if (Session.PendingProducts.Count > 0)
                    {
                        AddHeader(T(274));
                        for (int i = 0; i < Session.PendingProducts.Count; i++)
                            AddRow(new Row { kind = RowKind.Pending, item = Session.PendingProducts[i].item, equipment = i });
                    }
                }
            }
            else
            {
                AddHeader(T(183));
                AddRow(new Row { kind = RowKind.OwnShip, ship = Session.ShipIndex });
                for (int type = 0; type <= 3; type++)
                {
                    int slots = hangar.SlotCount(type);
                    var mounted = hangar.MountedOfType(type);
                    var cargo = Session.Cargo.Where(s => s.amount > 0 && hangar.TypeOf(s.item) == type).Select(s => s.item).ToList();
                    if (slots == 0 && mounted.Count == 0 && cargo.Count == 0) continue;
                    AddHeader(T(typeHeaders[type]));
                    foreach (int e in mounted) AddRow(new Row { kind = RowKind.Slot, equipment = e, item = Session.Equipment[e].item, type = type });
                    for (int n = mounted.Count; n < slots; n++) AddRow(new Row { kind = RowKind.Slot, type = type, equipment = -1 - n });
                    // Remake: the hold's candidates under their own sub-header (284 "Available in cargo"), styled apart from
                    // the mounted slots.
                    if (cargo.Count > 0)
                        AddHeader(Localization.Get(284) + (InputMode.Current == InputKind.Touch ? Localization.Extra("shopDoubleTapMount", "  ·  double-tap to mount")
                                                         : InputMode.Current == InputKind.KeyboardMouse ? Localization.Extra("shopDoubleClickMount", "  ·  double-click to mount") : ""), true);
                    foreach (int i in cargo) AddRow(new Row { kind = RowKind.CargoItem, item = i, type = type });
                }
            }

            selected = rows.FirstOrDefault(r => r.Same(keep)) ?? rows.FirstOrDefault(r => r.Selectable);
            foreach (var r in rows) r.element.EnableInClassList("list-row--selected", r == selected);
            list.schedule.Execute(() => { list.scrollOffset = new Vector2(0f, scroll); ScrollToSelected(); });
            ShowDetails();
            UpdateFooter();
        }

        void AddHeader(string text, bool sub = false)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("list-header");
            if (sub) l.AddToClassList("list-header--sub");
            l.AddToClassList("gof-semibold");
            list.Add(l);
            rows.Add(new Row { kind = RowKind.Header, element = l });
        }

        /// <summary>Remake: "+N mounted" (a secondary's N is its remaining ammo).</summary>
        static string MountedNote(int units) => string.Format(Localization.Extra("shopMountedCount", "+{0} mounted"), units);

        void AddRow(Row row)
        {
            var db = level.Database;
            var e = new VisualElement();
            e.AddToClassList("list-row");
            if (row.kind == RowKind.CargoItem) e.AddToClassList("list-row--cargo");
            if (row.kind == RowKind.Slot && row.equipment >= 0) e.AddToClassList("list-row--mounted");   // remake: tinted, a cyan edge
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("row-icon");
            var texts = new VisualElement { pickingMode = PickingMode.Ignore };
            texts.AddToClassList("row-texts");
            var name = new Label { pickingMode = PickingMode.Ignore };
            name.AddToClassList("row-name");
            var sub = new VisualElement { pickingMode = PickingMode.Ignore };
            sub.AddToClassList("row-sub");
            var subText = new Label { pickingMode = PickingMode.Ignore };
            subText.AddToClassList("row-sub-text");
            var price = new Label { pickingMode = PickingMode.Ignore };
            price.AddToClassList("row-price");
            price.AddToClassList("gof-semibold");

            Texture2D tex = null;
            switch (row.kind)
            {
                case RowKind.ShopItem:
                {
                    var it = db.Item(row.item);
                    tex = ItemInfo.ItemIcon(row.item);
                    name.text = ItemInfo.ItemName(row.item);
                    if (!Session.SeenItems.Contains(row.item)) sub.Add(Badge(Localization.Extra("shopNew", "NEW"), "row-badge--new"));
                    else if (hangar.IsMounted(row.item)) sub.Add(Badge(Localization.Extra("shopMounted", "MOUNTED"), "row-badge--mounted"));
                    // The trade mode's two amounts with their labels (136 "Station", 183 "Ship"), on every row.
                    subText.text = $"{ItemInfo.Category(it)}  ·  {Localization.Get(136)} {hangar.StockOf(row.item)} t  |  {Localization.Get(183)} {hangar.CargoOf(row.item)} t";
                    int mountedUnits = hangar.MountedOf(row.item);
                    if (mountedUnits > 0) subText.text += "  " + MountedNote(mountedUnits);
                    if (hangar.Storage) break;   // the storage draws no prices
                    int p = hangar.PriceOf(row.item);
                    price.text = ItemInfo.Credits(p);
                    price.EnableInClassList("row-price--expensive", p > Session.Credits);
                    break;
                }
                case RowKind.StoredShip:
                {
                    var stored = Session.KaamoShips[row.equipment];
                    tex = ItemInfo.ShipIcon(row.ship);
                    name.text = ItemInfo.ShipName(row.ship);
                    subText.text = ItemInfo.ShipRaceText(row.ship) + (stored.mods.Count > 0 ? "  (+)" : "");
                    price.text = ItemInfo.Credits(hangar.StoredPrice(row.equipment));   // the sell value
                    break;
                }
                case RowKind.ShopShip:
                {
                    tex = ItemInfo.ShipIcon(row.ship);
                    name.text = ItemInfo.ShipName(row.ship);
                    subText.text = ItemInfo.ShipRaceText(row.ship) + (hangar.Stock.ModsOf(row.ship).Count > 0 ? "  (+)" : "");
                    int delta = hangar.ShipPrice(row.ship) - hangar.ShipPrice(Session.ShipIndex);   // trade-in difference
                    price.text = ItemInfo.Credits(delta);
                    price.EnableInClassList("row-price--expensive", delta > Session.Credits);
                    break;
                }
                case RowKind.OwnShip:
                {
                    var s = db.Ship(row.ship);
                    tex = ItemInfo.ShipIcon(row.ship);
                    name.text = ItemInfo.ShipName(row.ship);
                    subText.text = s != null ? $"{Localization.Get(165)} {s.armor}   {Localization.Get(166)} {hangar.Load} / {hangar.MaxLoad} t" : "";
                    break;
                }
                case RowKind.Slot when row.equipment >= 0:
                {
                    var stack = Session.Equipment[row.equipment];
                    tex = ItemInfo.ItemIcon(stack.item);
                    name.text = ItemInfo.ItemName(stack.item) + (stack.amount > 1 ? $" ({stack.amount})" : "");
                    subText.text = ItemInfo.Category(db.Item(stack.item));
                    sub.Insert(0, Badge(Localization.Extra("shopMounted", "MOUNTED"), "row-badge--mounted"));
                    break;
                }
                case RowKind.Slot:
                    name.text = Localization.Extra("shopEmptySlot", "Empty slot");
                    name.AddToClassList("row-name--empty");
                    break;
                case RowKind.CargoItem:
                {
                    tex = ItemInfo.ItemIcon(row.item);
                    int n = hangar.CargoOf(row.item);
                    name.text = ItemInfo.ItemName(row.item) + (n > 1 ? $" ({n})" : "");
                    subText.text = ItemInfo.Category(db.Item(row.item));   // the sub-header says "Available in cargo"
                    sub.Insert(0, Badge(Localization.Extra("shopInCargo", "IN CARGO"), "row-badge--cargo"));
                    break;
                }
                case RowKind.Blueprint:
                {
                    tex = ItemInfo.ItemIcon(row.item);
                    name.text = ItemInfo.ItemName(row.item);
                    var st = Blueprints.State(db, row.item);
                    float rate = Blueprints.CompletionRate(db, st);
                    if (rate > 0f)
                    {
                        string where = st.station >= 0 ? $"  ({db.Stations.Find(s => s.index == st.station)?.name})" : "";
                        subText.text = $"{(int)(rate * 100f)}%{where}";
                        var bar = new VisualElement { pickingMode = PickingMode.Ignore };
                        bar.AddToClassList("bp-bar");
                        var fill = new VisualElement { pickingMode = PickingMode.Ignore };
                        fill.AddToClassList("bp-bar-fill");
                        fill.style.width = Length.Percent(rate * 100f);
                        bar.Add(fill);
                        texts.Add(bar);
                    }
                    else subText.text = ItemInfo.Category(db.Item(row.item));
                    name.EnableInClassList("row-name--helps", Blueprints.CargoHelps(db, st));
                    break;
                }
                case RowKind.Pending:
                {
                    var pp = Session.PendingProducts[row.equipment];
                    tex = ItemInfo.ItemIcon(pp.item);
                    name.text = (pp.quantity >= 2 ? $"{pp.quantity}x " : "") + ItemInfo.ItemName(pp.item);
                    subText.text = $"{Localization.Get(275)} {db.Stations.Find(s => s.index == pp.station)?.name}";
                    break;
                }
                case RowKind.Ingredient:
                {
                    tex = ItemInfo.ItemIcon(row.item);
                    name.text = ItemInfo.ItemName(row.item);
                    var st = Blueprints.State(db, editing);
                    int total = Blueprints.Total(db, editing, row.type);
                    int invested = Blueprints.Invested(db, st, row.type) + Pending(row.item);
                    subText.text = $"{Localization.Get(183)} {hangar.CargoOf(row.item)} t   |   {Localization.Get(271)} {invested} / {total} t";
                    if (invested >= total) sub.Insert(0, Badge("✓", "row-badge--mounted"));
                    else
                    {
                        // Remake: this station sells it (buy it in the shop, then add it here).
                        int stock = hangar.StockOf(row.item);
                        if (stock > 0)
                        {
                            sub.Insert(0, Badge(Localization.Extra("bpSoldHere", "SOLD HERE"), "row-badge--stock"));
                            subText.text += $"   |   {Localization.Get(136)} {stock} t";
                        }
                    }
                    break;
                }
                case RowKind.Autocomplete:
                    name.text = Localization.Extra("bpAutocomplete", "Autocomplete");   // a hard-coded English label in the original
                    subText.text = ItemInfo.ItemName(row.item);
                    price.text = ItemInfo.Credits(Blueprints.AutoCompletePrice(db, row.item));
                    price.EnableInClassList("row-price--expensive", Blueprints.AutoCompletePrice(db, row.item) > Session.Credits);
                    break;
            }
            if (tex != null) icon.style.backgroundImage = new StyleBackground(tex);
            sub.Add(subText);
            texts.Add(name);
            texts.Add(sub);
            e.Add(icon);
            e.Add(texts);
            if (price.text.Length > 0) e.Add(price);
            // ListItemWindow's "i" (image 0x470) on the selected row.
            if (row.kind != RowKind.Header && row.kind != RowKind.Autocomplete && (row.item >= 0 || row.ship >= 0 || row.equipment >= 0))
            {
                var info = new Button(() => { Select(row, false); OpenInfo(); }) { text = "i", focusable = false };
                info.AddToClassList("row-info");
                info.AddToClassList("gof-semibold");
                e.Add(info);
            }
            e.RegisterCallback<ClickEvent>(ev =>
            {
                Select(row, true);
                // Remake: a double click / double tap on the Ship tab mounts a hold item or demounts a mounted one (the action
                // button's Mount 281 / Demount 282, when it is enabled).
                if (ev.clickCount >= 2 && selected == row && (row.kind == RowKind.CargoItem || (row.kind == RowKind.Slot && row.equipment >= 0))
                    && !actionButton.ClassListContains("detail-action--hidden") && !actionButton.ClassListContains("detail-action--disabled"))
                    Action();
            });
            row.element = e;
            list.Add(e);
            rows.Add(row);
        }

        static Label Badge(string text, string cls)
        {
            var b = new Label(text) { pickingMode = PickingMode.Ignore };
            b.AddToClassList("row-badge");
            b.AddToClassList(cls);
            b.AddToClassList("gof-semibold");
            return b;
        }

        void Select(Row row, bool sound)
        {
            if (row == null || !row.Selectable || row == selected) return;
            if (HasPending) { Commit(() => Select(row, sound)); return; }   // leaving an ingredient commits it
            if (sound) menu.PlayPush();
            ReleaseArrow();
            selected = row;
            foreach (var r in rows) r.element.EnableInClassList("list-row--selected", r == selected);
            if (AutoEquip()) return;   // rebuilt
            ScrollToSelected();
            ShowDetails();
            SelectionHint(row);
        }

        /// <summary>HangarWindow::selectItem / setSellMode: once each, the first selected slot (587, hint 0x1f), the first
        /// item to buy (588, 0x1d) and the first non-commodity cargo item to sell (589, 0x1e).</summary>
        void SelectionHint(Row row)
        {
            if (menu.IsDialogOpen || !Settings.TutorialHints) return;   // one window at a time; the tutorial popups option
            int text = -1;
            if (tab == Tab.Ship && row.kind == RowKind.Slot && Session.Hints.Add(0x1f)) text = 587;
            else if (tab == Tab.Shop && row.kind == RowKind.ShopItem && Session.Hints.Add(0x1d)) text = 588;
            else if (tab == Tab.Shop && row.kind == RowKind.CargoItem && (level.Database.Item(row.item)?.TypeId ?? 4) != 4 && Session.Hints.Add(0x1e)) text = 589;
            if (text >= 0) menu.ShowDialog(Localization.Get(text), null, true);
        }

        public void MoveSelection(int dir)
        {
            if (rows.Count == 0) return;
            int i = selected != null ? rows.IndexOf(selected) : -1;
            for (int n = i + dir; n >= 0 && n < rows.Count; n += dir)
                if (rows[n].Selectable) { Select(rows[n], true); return; }
        }

        void ScrollToSelected()
        {
            if (selected?.element != null && selected.element.panel != null && !float.IsNaN(selected.element.layout.height)) list.ScrollTo(selected.element);
        }

        /// <summary>autoEquipSecondaryWeapons, when the selection moves on: missiles bought for a mounted launcher join it.</summary>
        bool AutoEquip()
        {
            if (hangar == null) return false;
            var moved = hangar.AutoEquipSecondaries();
            if (moved.Count == 0) return false;
            menu.ShowToast(Localization.Get(208).Replace("#N", ItemInfo.ItemName(moved[0])));
            Rebuild();
            return true;
        }

        // ---- details -----------------------------------------------------------------------------------------

        void ShowDetails()
        {
            var db = level.Database;
            detailStats.Clear();
            detailText.text = "";
            details.style.visibility = selected == null ? Visibility.Hidden : Visibility.Visible;
            tradeBox.AddToClassList("trade-box--hidden");
            actionButton.AddToClassList("detail-action--hidden");
            actionButton.RemoveFromClassList("detail-action--disabled");
            actionButton2?.AddToClassList("detail-action--hidden");
            if (selected == null) return;
            string T(int id) => Localization.Get(id);

            int item = selected.kind == RowKind.Slot && selected.equipment >= 0 ? Session.Equipment[selected.equipment].item : selected.item;
            if (item >= 0 && tab == Tab.Blueprints)
            {
                ShowBlueprintDetails(item);
                return;
            }
            if (item >= 0)
            {
                var it = db.Item(item);
                detailIcon.style.backgroundImage = new StyleBackground(ItemInfo.ItemIcon(item));
                detailName.text = ItemInfo.ItemName(item);
                detailSub.text = $"{ItemInfo.Category(it)}  ·  {T(133)} {it.techLevel}";
                foreach (var (label, value) in ItemInfo.ItemStats(it)) AddStat(label, value);
                detailText.text = ItemInfo.ItemText(db, it, hangar.SystemIndex);

                if (selected.kind == RowKind.ShopItem)
                {
                    tradeBox.RemoveFromClassList("trade-box--hidden");
                    tradeStockLabel.text = T(136).ToUpperInvariant();
                    tradeCargoLabel.text = T(183).ToUpperInvariant();
                    bool store = hangar.Storage;
                    sellLabel.text = "‹ " + (store ? Localization.Extra("shopStore", "STORE") : Localization.Extra("shopSell", "SELL"));
                    buyLabel.text = (store ? Localization.Extra("shopTake", "TAKE") : Localization.Extra("shopBuy", "BUY")) + " ›";
                    int stock = hangar.StockOf(item), cargo = hangar.CargoOf(item), price = hangar.PriceOf(item);
                    tradeStock.text = $"{stock} t";
                    tradeCargo.text = $"{cargo} t";
                    // Remake: the hold's amount leaves out what is mounted (a launcher's remaining missiles): shown under it.
                    int mounted = hangar.MountedOf(item);
                    if (tradeMounted != null) tradeMounted.text = mounted > 0 ? MountedNote(mounted) : "";
                    tradePrice.text = store ? "" : ItemInfo.Credits(price);
                    tradePrice.EnableInClassList("trade-price--expensive", !store && price > Session.Credits);
                    sellButton.EnableInClassList("trade-arrow--disabled", cargo <= 0);
                    buyButton.EnableInClassList("trade-arrow--disabled", stock <= 0);
                }
                else if (selected.kind == RowKind.Slot)
                    ShowAction(T(282).ToUpperInvariant(), true);
                else if (selected.kind == RowKind.CargoItem)
                {
                    var r = hangar.CanMount(item, out _);
                    ShowAction(T(281).ToUpperInvariant(), r == Hangar.Result.Ok || r == Hangar.Result.Swap);
                }
            }
            else if (selected.ship >= 0)
            {
                var s = db.Ship(selected.ship);
                detailIcon.style.backgroundImage = new StyleBackground(ItemInfo.ShipIcon(selected.ship));
                detailName.text = ItemInfo.ShipName(selected.ship);
                detailSub.text = ItemInfo.ShipRaceText(selected.ship);
                if (s != null) foreach (var (label, value) in ItemInfo.ShipStats(s, hangar.ShipPrice(selected.ship))) AddStat(label, value);
                detailText.text = ItemInfo.ShipDescription(selected.ship);
                if (selected.kind == RowKind.ShopShip)
                {
                    int delta = hangar.ShipPrice(selected.ship) - hangar.ShipPrice(Session.ShipIndex);
                    ShowAction($"{T(301).ToUpperInvariant()}   {ItemInfo.Credits(delta)}", true);
                    foreach (int mod in hangar.Stock.ModsOf(selected.ship)) AddStat(ModName(mod), "(+)");   // a traded-in hull's mods
                }
                else if (selected.kind == RowKind.StoredShip)
                {
                    // Row buttons 1 "Use" (332) and 10 "Sell" (330).
                    ShowAction(T(332).ToUpperInvariant(), selected.ship != Session.ShipIndex);
                    if (actionButton2 != null)
                    {
                        actionButton2.text = $"{T(330).ToUpperInvariant()}   {ItemInfo.Credits(hangar.StoredPrice(selected.equipment))}";
                        actionButton2.RemoveFromClassList("detail-action--hidden");
                    }
                    if (Session.KaamoShips[selected.equipment].mods.Count > 0)
                        foreach (int mod in Session.KaamoShips[selected.equipment].mods) AddStat(ModName(mod), "(+)");
                }
            }
            else
            {
                // Empty slot.
                detailIcon.style.backgroundImage = StyleKeyword.None;
                detailName.text = Localization.Extra("shopEmptySlot", "Empty slot");
                detailSub.text = T(new[] { 265, 266, 267, 269 }[Mathf.Clamp(selected.type, 0, 3)]);
            }
            detailScroll.scrollOffset = Vector2.zero;
            if (selected.item >= 0) Session.SeenItems.Add(selected.item);   // the original marks inspected items
        }

        void ShowBlueprintDetails(int item)
        {
            var db = level.Database;
            string T(int id) => Localization.Get(id);
            var it = db.Item(item);
            detailIcon.style.backgroundImage = new StyleBackground(ItemInfo.ItemIcon(item));
            detailName.text = ItemInfo.ItemName(item);
            detailSub.text = $"{ItemInfo.Category(it)}  ·  {T(133)} {it.techLevel}";
            foreach (var (label, value) in ItemInfo.ItemStats(it)) AddStat(label, value);
            detailText.text = ItemInfo.ItemText(db, it, hangar.SystemIndex);
            switch (selected.kind)
            {
                case RowKind.Blueprint:
                    ShowAction(T(283).ToUpperInvariant(), true);   // Edit
                    break;
                case RowKind.Autocomplete:
                    ShowAction($"{Localization.Extra("bpAutocomplete", "Autocomplete").ToUpperInvariant()}   {ItemInfo.Credits(Blueprints.AutoCompletePrice(db, editing))}", true);
                    break;
                case RowKind.Ingredient:
                {
                    // Trade mode: the hold (183 "Ship") on the left, the blueprint (271) on the right; right arrow = add.
                    var st = Blueprints.State(db, editing);
                    int total = Blueprints.Total(db, editing, selected.type);
                    int invested = Blueprints.Invested(db, st, selected.type) + Pending(item);
                    int cargo = hangar.CargoOf(item);
                    tradeBox.RemoveFromClassList("trade-box--hidden");
                    tradeStockLabel.text = T(183).ToUpperInvariant();
                    tradeCargoLabel.text = T(271).ToUpperInvariant();
                    sellLabel.text = "‹";
                    buyLabel.text = Localization.Extra("bpAdd", "ADD") + " ›";
                    tradeStock.text = $"{cargo} t";
                    tradeCargo.text = $"{invested} / {total} t";
                    tradePrice.text = "";
                    sellButton.EnableInClassList("trade-arrow--disabled", true);   // invested goods can't be taken back
                    buyButton.EnableInClassList("trade-arrow--disabled", cargo <= 0 || invested >= total);
                    break;
                }
            }
            detailScroll.scrollOffset = Vector2.zero;
        }

        int Pending(int ingredient) => pendingUnits.TryGetValue(ingredient, out int n) ? n : 0;
        bool HasPending { get { foreach (var kv in pendingUnits) if (kv.Value > 0) return true; return false; } }
        int StationIndex => level.Station != null ? level.Station.index : Session.StationIndex;

        /// <summary>Item::transactionBlueprint: one unit from the hold into the blueprint (not committed yet).</summary>
        void AddIngredient(int units)
        {
            var db = level.Database;
            int item = selected.item;
            var st = Blueprints.State(db, editing);
            if (Blueprints.IsEmpty(st) && !startConfirmed && !HasPending)
            {
                ReleaseArrow();
                if (!Blueprints.CanStartIn(db, editing, StationIndex)) { menu.ShowToast(Localization.Get(528)); return; }
                menu.ShowDialog(Localization.Get(212), () => { startConfirmed = true; AddIngredient(1); });   // Start production here?
                return;
            }
            int moved = 0;
            for (int n = 0; n < units; n++)
            {
                int left = st.remaining[selected.type] - Pending(item);
                if (left <= 0 || hangar.CargoOf(item) <= 0) break;
                Shop.RemoveFromCargo(item, 1);
                pendingUnits[item] = Pending(item) + 1;
                moved++;
            }
            if (moved == 0) { ReleaseArrow(); return; }
            menu.PlayClip(menu.shopSell);
            Rebuild();
        }

        /// <summary>setSellMode(false): the pending units go into the blueprint; at another station than the production
        /// station for 200 $ per unit (288), volatile goods never (289); a completed run is produced.</summary>
        void Commit(System.Action after)
        {
            var db = level.Database;
            if (!HasPending || editing < 0) { pendingUnits.Clear(); after?.Invoke(); return; }
            var st = Blueprints.State(db, editing);
            int units = 0; bool volatileGoods = false;
            foreach (var kv in pendingUnits) { units += kv.Value; if (kv.Value > 0 && Blueprints.IsVolatile(kv.Key)) volatileGoods = true; }
            bool elsewhere = !Blueprints.IsEmpty(st) && st.station >= 0 && st.station != StationIndex;
            if (elsewhere && volatileGoods)
            {
                Revert();
                menu.ShowDialog(Localization.Get(289), null, true);
                return;
            }
            if (elsewhere)
            {
                int cost = Blueprints.ShippingPerUnit * units;
                string where = db.Stations.Find(s => s.index == st.station)?.name ?? "";
                menu.ShowDialog(Localization.Get(288).Replace("#S", where).Replace("#C", ItemInfo.Credits(cost)), () =>
                {
                    if (cost > Session.Credits)
                    {
                        int need = cost - Session.Credits;
                        Revert();
                        menu.ShowToast(Localization.Get(203).Replace("#C", ItemInfo.Credits(need)));
                        return;
                    }
                    Session.Credits -= cost;
                    Apply();
                    after?.Invoke();
                });
                // "No" reverts: the dialog's No only closes it, so revert now and re-apply on Yes.
                pendingSnapshot = new Dictionary<int, int>(pendingUnits);
                Revert();
                return;
            }
            Apply();
            after?.Invoke();
        }

        Dictionary<int, int> pendingSnapshot;

        void Apply()
        {
            var db = level.Database;
            var units = pendingSnapshot ?? pendingUnits;
            if (pendingSnapshot != null)
                foreach (var kv in pendingSnapshot) Shop.RemoveFromCargo(kv.Key, kv.Value);   // taken back out of the hold
            foreach (var kv in units) Blueprints.Invest(db, editing, kv.Key, kv.Value, StationIndex);
            pendingUnits.Clear();
            pendingSnapshot = null;
            startConfirmed = false;
            if (Blueprints.IsCompleted(Blueprints.State(db, editing))) Finish();
            else Rebuild();
        }

        void Revert()
        {
            foreach (var kv in pendingUnits) Shop.AddToCargo(kv.Key, kv.Value);
            pendingUnits.Clear();
            startConfirmed = false;
            Rebuild();
        }

        /// <summary>A completed run: 211 to the hold (then the Shop tab) or 210 waiting at the production station.</summary>
        void Finish()
        {
            var db = level.Database;
            int product = editing;
            int station = Blueprints.State(db, product).station;
            bool here = Blueprints.Produce(db, product, StationIndex);
            hangar = new Hangar(db, level.Stock);   // the new product needs its price
            string name = ItemInfo.ItemName(product);
            if (here)
            {
                menu.ShowDialog(Localization.Get(211).Replace("#N", name), null, true);
                editing = -1;
                SetTab(Tab.Shop);
            }
            else
            {
                string where = db.Stations.Find(s => s.index == station)?.name ?? "";
                menu.ShowDialog(Localization.Get(210).Replace("#N", name).Replace("#S", where), null, true);
                editing = -1;
                selected = null;
                Rebuild();
            }
        }

        /// <summary>Autocomplete (button 23): 195 "Autocomplete blueprint for #C?"; an empty blueprint takes this station.</summary>
        void AskAutocomplete()
        {
            var db = level.Database;
            int price = Blueprints.AutoCompletePrice(db, editing);
            if (price > Session.Credits) { menu.ShowToast(Localization.Get(203).Replace("#C", ItemInfo.Credits(price - Session.Credits))); return; }
            if (!Blueprints.CanStartIn(db, editing, StationIndex) && Blueprints.IsEmpty(Blueprints.State(db, editing)))
            { menu.ShowToast(Localization.Get(528)); return; }
            menu.ShowDialog(Localization.Get(195).Replace("#C", ItemInfo.Credits(price)), () =>
            {
                Revert();
                var st = Blueprints.State(db, editing);
                if (Blueprints.IsEmpty(st) || st.station < 0) st.station = StationIndex;
                for (int k = 0; k < st.remaining.Count; k++) st.remaining[k] = 0;   // BluePrint::complete
                Session.Credits -= price;
                Finish();
            });
        }

        void ShowAction(string text, bool enabled)
        {
            actionButton.text = text;
            actionButton.RemoveFromClassList("detail-action--hidden");
            actionButton.EnableInClassList("detail-action--disabled", !enabled);
        }

        void AddStat(string label, string value)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("stat-row");
            var l = new Label(label) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("stat-label");
            var v = new Label(value) { pickingMode = PickingMode.Ignore };
            v.AddToClassList("stat-value");
            v.AddToClassList("gof-semibold");
            row.Add(l);
            row.Add(v);
            detailStats.Add(row);
        }

        void UpdateFooter()
        {
            cargoLabel.text = $"{Localization.Get(184).ToUpperInvariant()}  {hangar.Load} / {hangar.MaxLoad} t";
            cargoLabel.EnableInClassList("footer-cargo--over", hangar.Overloaded);
            creditsLabel.text = ItemInfo.Credits(Session.Credits);
        }

        // ---- actions -----------------------------------------------------------------------------------------

        /// <summary>Left / right: sell / buy one unit of the selected shop item (Item::transaction).</summary>
        public void Trade(int direction, bool sound = true, int units = 1)
        {
            if (selected != null && selected.kind == RowKind.Ingredient)
            {
                if (direction > 0) AddIngredient(units); else ReleaseArrow();
                return;
            }
            if (selected == null || selected.kind != RowKind.ShopItem) return;
            bool changed = false;
            for (int n = 0; n < units; n++)
            {
                if (direction > 0)
                {
                    var r = hangar.Buy(selected.item, out int need);
                    if (r == Hangar.Result.NoCredits)
                    {
                        menu.ShowToast(Localization.Get(203).Replace("#C", ItemInfo.Credits(need)));
                        ReleaseArrow();
                        break;
                    }
                    if (r != Hangar.Result.Ok) { ReleaseArrow(); break; }
                }
                else
                {
                    var r = hangar.Sell(selected.item);
                    if (r == Hangar.Result.NotSaleable) menu.ShowToast(Localization.Get(323));
                    if (r != Hangar.Result.Ok) { ReleaseArrow(); break; }
                }
                changed = true;
            }
            if (!changed) return;
            if (sound) menu.PlayClip(direction > 0 ? menu.shopBuy : menu.shopSell);
            Rebuild();
        }

        /// <summary>Enter / A / the action button: mount, demount or buy the selected ship.</summary>
        public void Action()
        {
            if (selected == null) return;
            var db = level.Database;
            switch (selected.kind)
            {
                case RowKind.Blueprint:
                    // Edit (283) -> tab 4, the ingredients.
                    menu.PlayRelease();
                    editing = selected.item;
                    pendingUnits.Clear();
                    startConfirmed = false;
                    selected = null;
                    Rebuild();
                    break;
                case RowKind.Autocomplete:
                    AskAutocomplete();
                    break;
                case RowKind.Slot when selected.equipment >= 0:
                {
                    int item = Session.Equipment[selected.equipment].item;
                    if (!Hangar.IsSaleable(item)) { menu.ShowToast(Localization.Get(323)); break; }
                    hangar.Demount(selected.equipment);
                    menu.PlayClip(menu.shopDemount);
                    menu.ShowToast(Localization.Get(209).Replace("#N", ItemInfo.ItemName(item)));
                    selected = null;
                    Rebuild();
                    break;
                }
                case RowKind.CargoItem:
                {
                    int item = selected.item;
                    var r = hangar.CanMount(item, out int swapWith);
                    if (r == Hangar.Result.Swap)
                    {
                        string text = Localization.Get(287).Replace("#ITEM1", ItemInfo.ItemName(Session.Equipment[swapWith].item))
                                                               .Replace("#ITEM2", ItemInfo.ItemName(item));
                        menu.ShowDialog(text, () =>
                        {
                            if (!hangar.Swap(swapWith, item)) return;
                            menu.PlayClip(menu.shopMount);
                            selected = null;
                            Rebuild();
                        });
                    }
                    else if (r == Hangar.Result.Ok && hangar.Mount(item))
                    {
                        menu.PlayClip(menu.shopMount);
                        menu.ShowToast(Localization.Get(208).Replace("#N", ItemInfo.ItemName(item)));
                        selected = null;
                        Rebuild();
                    }
                    break;
                }
                case RowKind.ShopShip:
                {
                    int ship = selected.ship;
                    var r = hangar.CanBuyShip(ship, out int need);
                    if (r == Hangar.Result.Passengers) { menu.ShowToast(Localization.Get(336)); break; }
                    if (r == Hangar.Result.SameShip) { menu.ShowToast(Localization.Get(329)); break; }
                    if (r == Hangar.Result.NoCredits) { menu.ShowToast(Localization.Get(203).Replace("#C", ItemInfo.Credits(need))); break; }
                    void Bought()
                    {
                        level.ReplacePlayerShip(ship);
                        menu.ShowToast(Localization.Get(303).Replace("#N", db.Ship(ship)?.name ?? ItemInfo.ShipName(ship)));
                        selected = null;
                        Rebuild();
                    }
                    // Multiplayer: the dealer's ship is reserved with the host first (another pilot may be buying it).
                    void SoldOut()
                    {
                        menu.ShowToast(Localization.Extra("mpShipSoldOut", "Sold: another pilot bought this ship."));
                        selected = null;
                        Rebuild();
                    }
                    void Reserved(System.Func<bool> trade) => GoF2Remake.Multiplayer.NetStock.ReserveShip(hangar.Station, ship, () =>
                    {
                        if (trade()) Bought();
                        else GoF2Remake.Multiplayer.NetStock.ShipChanged(hangar.Station, -1, ship);   // not bought after all: back
                    }, SoldOut);
                    void TradeIn() => Reserved(() => hangar.BuyShip(ship));
                    if (!KaamoClub.Owned) { menu.ShowDialog(Localization.Get(304), TradeIn); break; }
                    // 304, then 327 "sell your old ship or keep it and have it brought to your station?" (330 / 331).
                    menu.ShowDialog(Localization.Get(304), () => menu.ShowChoice(Localization.Get(327), Localization.Get(330), Localization.Get(331), TradeIn, () =>
                    {
                        var k = hangar.CanKeepAndBuyShip(ship, out int missing);
                        if (k == Hangar.Result.AlreadyStored) { menu.ShowDialog(Localization.Get(328), null, true); return; }
                        if (k == Hangar.Result.NoCredits) { menu.ShowToast(Localization.Get(203).Replace("#C", ItemInfo.Credits(missing))); return; }
                        Reserved(() => hangar.KeepAndBuyShip(ship));
                    }));
                    break;
                }
                case RowKind.StoredShip:
                {
                    int index = selected.equipment;
                    var r = hangar.CanUseStored(index);
                    if (r == Hangar.Result.Passengers) { menu.ShowToast(Localization.Get(336)); break; }
                    if (r == Hangar.Result.SameShip) { menu.ShowToast(Localization.Get(329)); break; }
                    if (r != Hangar.Result.Ok) break;
                    menu.ShowDialog(Localization.Get(333), () =>
                    {
                        if (!hangar.UseStored(index)) return;
                        level.ReplacePlayerShip(Session.ShipIndex);
                        level.RefreshParkedShips();
                        selected = null;
                        Rebuild();
                    });
                    break;
                }
            }
        }

        /// <summary>The second row button (X / controller X): Sell a stored hull (330 -> 334).</summary>
        public void SecondaryAction()
        {
            if (selected == null || selected.kind != RowKind.StoredShip) return;
            int index = selected.equipment;
            menu.PlayRelease();
            menu.ShowDialog(Localization.Get(334), () =>
            {
                if (!hangar.SellStored(index)) return;
                level.RefreshParkedShips();
                selected = null;
                Rebuild();
            });
        }

        /// <summary>The Kaamo mechanics' mods (907-910 texts' subjects): +40 hull, +30 t cargo, +1 slot, handling.</summary>
        static string ModName(int mod) => mod switch
        {
            0 => Localization.Get(165),   // Armor
            1 => Localization.Get(166),   // Cargo hold
            2 => Localization.Get(269),   // Equipment
            _ => Localization.Get(164),   // Handling
        };

        public bool StorageMode => hangar != null && hangar.Storage;

        // ---- held trade arrows ---------------------------------------------------------------------------------

        void HookArrow(VisualElement arrow, int direction)
        {
            arrow.RegisterCallback<PointerDownEvent>(e =>
            {
                if (heldPointer >= 0 || arrow.ClassListContains("trade-arrow--disabled")) return;
                heldPointer = e.pointerId;
                heldDirection = direction;
                heldMs = repeatMs = 0f;
                arrow.CapturePointer(e.pointerId);
                arrow.AddToClassList("trade-arrow--pressed");
                Trade(direction);
                e.StopPropagation();
            });
            arrow.RegisterCallback<PointerUpEvent>(e => { if (e.pointerId == heldPointer) ReleaseArrow(); });
            arrow.RegisterCallback<PointerCancelEvent>(e => { if (e.pointerId == heldPointer) ReleaseArrow(); });
        }

        void ReleaseArrow()
        {
            if (heldPointer >= 0)
            {
                if (sellButton.HasPointerCapture(heldPointer)) sellButton.ReleasePointer(heldPointer);
                if (buyButton.HasPointerCapture(heldPointer)) buyButton.ReleasePointer(heldPointer);
            }
            heldPointer = -1;
            heldDirection = 0;
            sellButton.RemoveFromClassList("trade-arrow--pressed");
            buyButton.RemoveFromClassList("trade-arrow--pressed");
        }

        /// <summary>Keyboard / controller: the held vertical (select) and horizontal (sell / buy) direction, every frame.</summary>
        public void HoldDirections(int vertical, int horizontal, float dtMs)
        {
            if (!IsOpen) return;
            if (vertical != moveDirection)
            {
                moveDirection = vertical;
                moveMs = -350f;
                if (vertical != 0) MoveSelection(vertical);
            }
            else if (vertical != 0 && (moveMs += dtMs) >= 80f)
            {
                moveMs = 0f;
                MoveSelection(vertical);
            }

            // A new press starts a hold (repeated in Update); a failed trade ends it until the key is pressed again.
            if (horizontal == keyDirection) return;
            keyDirection = horizontal;
            if (heldPointer == KeyHold) ReleaseArrow();
            if (horizontal == 0 || heldPointer >= 0) return;   // released, or a finger / the mouse holds an arrow
            heldPointer = KeyHold;
            heldDirection = horizontal;
            heldMs = repeatMs = 0f;
            (horizontal < 0 ? sellButton : buyButton).AddToClassList("trade-arrow--pressed");
            Trade(horizontal);
        }

        /// <summary>HangarWindow::update: repeat after 200 ms, every 30 ms once held 1.5 s, 5 units per repeat after 4 s.</summary>
        public void Update(float dtMs)
        {
            if (!IsOpen || heldDirection == 0) return;
            heldMs += dtMs;
            repeatMs += dtMs;
            float interval = heldMs > 1500f ? 30f : 200f;
            if (repeatMs < interval) return;
            repeatMs = 0f;
            Trade(heldDirection, false, heldMs > 4000f ? 5 : 1);
        }
    }
}
