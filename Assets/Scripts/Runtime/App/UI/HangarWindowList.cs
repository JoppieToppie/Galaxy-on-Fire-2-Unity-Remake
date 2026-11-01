// HangarWindowList.cs
// HangarWindow: the list and its scrollbar (part of the partial HangarWindow; its fields are in HangarWindow.cs).

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
        float ListScrollMax => Mathf.Max(0f, list.contentContainer.layout.height - list.contentViewport.layout.height);
        /// <summary>Along the list's right edge; the handle's size and place follow the list's scroll (hidden when it fits).</summary>
        void UpdateListBar()
        {
            var lr = list.layout;
            float content = list.contentContainer.layout.height, view = list.contentViewport.layout.height;
            if (!IsOpen || float.IsNaN(lr.height) || float.IsNaN(content) || float.IsNaN(view) || content <= view + 1f)
            {
                listBar.style.display = DisplayStyle.None;
                return;
            }
            listBar.style.display = DisplayStyle.Flex;
            // The slim track's centre 8 units in from the list's right edge, whatever the touch area's width.
            float barW = listBar.resolvedStyle.width;
            if (float.IsNaN(barW) || barW <= 0f) barW = 12f;
            listBar.style.left = lr.xMax - 8f - barW * 0.5f;
            listBar.style.top = lr.y + 4f;
            listBar.style.height = lr.height - 8f;
            float track = lr.height - 8f;
            float thumb = Mathf.Max(barW > 20f ? 60f : 30f, track * view / content);   // a finger-sized handle on phones
            float t = Mathf.Clamp01(list.scrollOffset.y / Mathf.Max(1f, ListScrollMax));
            listThumb.style.height = thumb;
            listThumb.style.top = t * (track - thumb);
        }
        void OnListBarDown(PointerDownEvent e)
        {
            if (e.button != 0 || listBarPointer >= 0) return;
            listBarPointer = e.pointerId;
            listBar.CapturePointer(listBarPointer);
            listThumb.AddToClassList("list-bar-thumb--dragging");
            float y = e.localPosition.y, top = listThumb.layout.y, h = listThumb.layout.height;
            // On the handle: drag it from where it was grabbed; on the track: the handle's middle jumps there.
            listBarGrab = y >= top && y <= top + h ? y - top : h * 0.5f;
            ScrollListTo(y);
            e.StopPropagation();
        }
        void OnListBarMove(PointerMoveEvent e)
        {
            if (e.pointerId != listBarPointer) return;
            if (!UiPointerButtons.PrimaryHeld(e)) { EndListBarDrag(); return; }
            ScrollListTo(e.localPosition.y);
            e.StopPropagation();
        }
        void OnListBarUp(PointerUpEvent e)
        {
            if (e.pointerId != listBarPointer) return;
            EndListBarDrag();
            e.StopPropagation();
        }
        void EndListBarDrag()
        {
            int id = listBarPointer;
            listBarPointer = -1;
            listThumb.RemoveFromClassList("list-bar-thumb--dragging");
            if (id >= 0 && listBar.HasPointerCapture(id)) listBar.ReleasePointer(id);
        }
        /// <summary>The list scrolled so the handle's grabbed point is at 'y' on the track.</summary>
        void ScrollListTo(float y)
        {
            float track = listBar.layout.height, thumb = listThumb.layout.height;
            if (float.IsNaN(track) || track - thumb <= 0f) return;
            float t = Mathf.Clamp01((y - listBarGrab) / (track - thumb));
            list.scrollOffset = new Vector2(list.scrollOffset.x, t * ListScrollMax);
            UpdateListBar();
        }
        public void NextTab() => SetTab(tab == Tab.Ship ? Tab.Shop : tab == Tab.Shop ? Tab.Blueprints : Tab.Ship);
        public void PrevTab() => SetTab(tab == Tab.Ship ? Tab.Blueprints : tab == Tab.Shop ? Tab.Ship : Tab.Shop);
        // ---- list --------------------------------------------------------------------------------------------
        void Rebuild()
        {
            if (!IsOpen) return;
            tabShip.EnableInClassList("hangar-tab--active", tab == Tab.Ship);
            tabShop.EnableInClassList("hangar-tab--active", tab == Tab.Shop);
            tabBlueprints.EnableInClassList("hangar-tab--active", tab == Tab.Blueprints);
            var keep = selected;
            int keepIndex = pendingKeepIndex ?? (keep != null ? rows.IndexOf(keep) : -1);
            pendingKeepIndex = null;
            float scroll = pendingScroll ?? list.scrollOffset.y;
            pendingScroll = null;
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
                    AddProductionActions();  // COMPLETE / AUTOCOMPLETE / SHOP, persistent recipe view.
                }
                else
                {
                    AddHeader(T(273));
                    int known = 0;
                    foreach (var p in Blueprints.Products(db))
                        if (Blueprints.IsUnlocked(p.index)) { AddRow(new Row { kind = RowKind.Blueprint, item = p.index }); known++; }
                    // Rework (docs/UI-DESIGN.md "Empty states"): an empty tab says how to fill it (it was a heading over nothing).
                    if (known == 0) AddNote(Localization.Extra("blueprintsNone", "No blueprints yet. Space Lounge visitors sell them."));
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
                // Remake mods: the ship's built-in items (ships.json "integrated": "items"), in no slot.
                var builtIn = hangar.BuiltInStacks();
                if (builtIn.Count > 0)
                {
                    AddHeader(Localization.Extra("shopBuiltInHeader", "Built into the ship"), true);
                    foreach (int e in builtIn) AddRow(new Row { kind = RowKind.Slot, equipment = e, item = Session.Equipment[e].item, type = hangar.TypeOf(Session.Equipment[e].item) });
                }
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

            // The same row again; when it is gone (its last unit bought or sold) the row that took its place, or the one
            // before it at the end of the list, so the list stays where it was instead of jumping to the top.
            selected = rows.FirstOrDefault(r => r.Same(keep)) ?? NearestSelectable(keepIndex) ?? rows.FirstOrDefault(r => r.Selectable);
            // Remake: the shop opened from an ingredient's SOLD HERE badge lands on that item.
            if (focusShopItem >= 0 && tab == Tab.Shop)
            {
                var focus = rows.FirstOrDefault(r => r.kind == RowKind.ShopItem && r.item == focusShopItem);
                focusShopItem = -1;
                if (focus != null)
                {
                    selected = focus;
                    // Scrolled to once its row is laid out (the list's scroll below runs before that on a fresh list).
                    EventCallback<GeometryChangedEvent> onLaidOut = null;
                    onLaidOut = _ => { focus.element.UnregisterCallback(onLaidOut); if (selected == focus) ScrollToSelected(); };
                    focus.element.RegisterCallback(onLaidOut);
                }
            }
            foreach (var r in rows) r.element.EnableInClassList("list-row--selected", r == selected);
            list.schedule.Execute(() => { list.scrollOffset = new Vector2(0f, scroll); ScrollToSelected(); });
            ShowDetails();
            UpdateFooter();
            // Remake: the key hints say TAKE / ADD in the ingredients view (StationMenu.BuildHints asks EditingBlueprint).
            if (hintsForIngredients != EditingBlueprint)
            {
                hintsForIngredients = EditingBlueprint;
                menu.RefreshCredits();   // rebuilds the hints
            }
        }
        /// <summary>Remake: the ingredients view is shown (its trade arrows are TAKE / ADD).</summary>
        public bool EditingBlueprint => IsOpen && editing >= 0;
        /// <summary>Remake (PR #166): the ingredients view's actions, centred under the list in place of the Autocomplete row:
        /// 185 Shop / 186 Store (that tab with the recipe's goods marked), Autocomplete (195, a mod's blueprint may have none) and
        /// Complete. Each is a row of its own, so up / down reach them and Enter / A does them like any row's action; a click /
        /// tap selects it and does it.</summary>
        void AddProductionActions()
        {
            var actions = new VisualElement { pickingMode = PickingMode.Ignore };
            actions.AddToClassList("manufacturing-actions");
            list.Add(actions);
            AddProductionRow(actions, RowKind.ShopLink, Localization.Get(hangar.Storage ? 186 : 185).ToUpperInvariant(), "manufacturing-shop");   // the tab's name
            productionAutocomplete = Blueprints.CanAutocomplete(editing)
                ? AddProductionRow(actions, RowKind.Autocomplete, "", "manufacturing-autocomplete") : null;
            productionComplete = AddProductionRow(actions, RowKind.Complete, Localization.Extra("bpComplete", "Complete").ToUpperInvariant(),
                                                  "manufacturing-complete");
            RefreshProductionActions();
        }
        Label AddProductionRow(VisualElement parent, RowKind kind, string title, string cls)
        {
            var row = new Row { kind = kind, item = editing };
            var button = new Label(title) { pickingMode = PickingMode.Position };
            button.AddToClassList("detail-action");
            button.AddToClassList("gof-semibold");
            button.AddToClassList("manufacturing-action");
            button.AddToClassList(cls);
            button.RegisterCallback<ClickEvent>(e =>
            {
                e.StopPropagation();
                Select(row, true);
                if (selected == row) Action();
            });
            row.element = button;
            parent.Add(button);
            rows.Add(row);
            return button;
        }
        void RefreshProductionActions()
        {
            if (productionComplete == null || editing < 0) return;
            bool ready = CanComplete();
            productionComplete.EnableInClassList("detail-action--disabled", !ready);
            productionComplete.pickingMode = ready ? PickingMode.Position : PickingMode.Ignore;
            if (productionAutocomplete != null)
            {
                int price = Blueprints.AutoCompletePrice(level.Database, editing);
                productionAutocomplete.text = Localization.Extra("bpAutocomplete", "Autocomplete").ToUpperInvariant()
                    + "\n" + ItemInfo.Credits(price);
                productionAutocomplete.EnableInClassList("row-autocomplete--expensive", price > Session.Credits);
            }
        }
        /// <summary>Remake: every ingredient is in, counting the units waiting to be shipped (they go in first, 288).</summary>
        bool CanComplete()
        {
            if (editing < 0) return false;
            var db = level.Database;
            var parts = db.Item(editing)?.blueprint;
            if (parts == null) return false;
            var st = Blueprints.State(db, editing);
            for (int k = 0; k < parts.Count; k++)
                if (st.remaining[k] - Pending(parts[k].item) > 0) return false;
            return true;
        }
        /// <summary>Remake: Complete: the staged units shipped (288) and the run produced (Finish).</summary>
        void Complete()
        {
            if (!CanComplete()) return;
            menu.PlayRelease();
            Commit(() =>
            {
                if (editing >= 0 && Blueprints.IsCompleted(Blueprints.State(level.Database, editing))) Finish();
            });
        }
        void ClearBlueprintShopHighlight()
        {
            shopBlueprintHighlight = false;
            shopBlueprintMaterials.Clear();
        }
        /// <summary>The first selectable row from 'index' on, else the last one before it (null: none, or index &lt; 0).</summary>
        Row NearestSelectable(int index)
        {
            if (index < 0) return null;
            for (int i = index; i < rows.Count; i++) if (rows[i].Selectable) return rows[i];
            for (int i = Mathf.Min(index, rows.Count) - 1; i >= 0; i--) if (rows[i].Selectable) return rows[i];
            return null;
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
        /// <summary>A line under a header that says why the list is empty (not selectable).</summary>
        void AddNote(string text)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("list-empty");
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
            // Remake: the shop opened from a blueprint's Shop action: the recipe's goods with a cyan edge (USS).
            if (row.kind == RowKind.ShopItem && shopBlueprintHighlight && shopBlueprintMaterials.ContainsKey(row.item))
                e.AddToClassList("list-row--blueprint-material");
            // Remake: the Ship tab's hold items in the plain grey (were outlined and indented).
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
            VisualElement bpAmount = null;   // Remake: an ingredient's blueprint amount, at the row's right edge
            switch (row.kind)
            {
                case RowKind.ShopItem:
                {
                    var it = db.Item(row.item);
                    tex = ItemInfo.ItemIcon(row.item);
                    name.text = ItemInfo.TaggedItemName(row.item);
                    if (!Session.SeenItems.Contains(row.item)) sub.Add(Badge(Localization.Extra("shopNew", "NEW"), "row-badge--new"));
                    else if (hangar.IsMounted(row.item)) sub.Add(Badge(Localization.Extra("shopMounted", "MOUNTED"), "row-badge--mounted"));
                    // Remake (#62): what the player has in the hold is marked like the Ship tab's cargo rows (amber edge, IN CARGO),
                    // so their own goods stand out from the station's.
                    if (hangar.CargoOf(row.item) > 0)
                    {
                        e.AddToClassList("list-row--owned");   // Remake: tinted blue in place (the cargo outline shifted it right)
                        sub.Add(Badge(Localization.Extra("shopInCargo", "IN CARGO"), "row-badge--cargo"));
                    }
                    // The trade mode's two amounts: Remake, the station's at the row's left edge and the ship's at its right
                    // (the original's columns), under the name only the category.
                    subText.text = ItemInfo.Category(it);
                    int mountedUnits = hangar.MountedOf(row.item);
                    if (mountedUnits > 0) subText.text += "  " + MountedNote(mountedUnits);
                    if (hangar.Storage) break;   // the storage draws no prices
                    int p = hangar.BuyPriceOf(row.item);   // 0 for step 67's gifted mines
                    price.text = ItemInfo.Credits(p);
                    price.EnableInClassList("row-price--expensive", p > Session.Credits);
                    break;
                }
                case RowKind.StoredShip:
                {
                    var stored = Session.KaamoShips[row.equipment];
                    tex = ItemInfo.ShipIcon(row.ship);
                    name.text = ItemInfo.TaggedShipName(row.ship);
                    subText.text = ItemInfo.ShipRaceText(row.ship) + (stored.mods.Count > 0 ? "  (+)" : "");
                    int gear = stored.equipment?.Count ?? 0;   // remake: the items left on it (Settings.KaamoKeepsEquipment)
                    if (gear > 0) subText.text += "  ·  " + string.Format(Localization.Extra("kaamoGearCount", "{0} mounted"), gear);
                    price.text = ItemInfo.Credits(hangar.StoredPrice(row.equipment));   // the sell value
                    break;
                }
                case RowKind.ShopShip:
                {
                    tex = ItemInfo.ShipIcon(row.ship);
                    name.text = ItemInfo.TaggedShipName(row.ship);
                    subText.text = ItemInfo.ShipRaceText(row.ship) + (hangar.Stock.ModsOf(row.ship).Count > 0 ? "  (+)" : "");
                    int delta = hangar.BuyShipPrice(row.ship) - hangar.ShipPrice(Session.ShipIndex);   // trade-in difference
                    // Remake: the ship's own price on the row (the buy button still shows what it costs after the trade-in);
                    // red while the trade-in difference is more than the credits.
                    price.text = ItemInfo.Credits(hangar.BuyShipPrice(row.ship));
                    price.EnableInClassList("row-price--expensive", delta > Session.Credits);
                    break;
                }
                case RowKind.OwnShip:
                {
                    var s = db.Ship(row.ship);
                    tex = ItemInfo.ShipIcon(row.ship);
                    name.text = ItemInfo.TaggedShipName(row.ship);
                    subText.text = s != null ? $"{Localization.Get(165)} {Shop.BaseHp(db)}   {Localization.Get(166)} {hangar.Load} / {hangar.MaxLoad} t" : "";   // with the +40 hull mod
                    break;
                }
                case RowKind.Slot when row.equipment >= 0:
                {
                    var stack = Session.Equipment[row.equipment];
                    tex = ItemInfo.ItemIcon(stack.item);
                    name.text = ItemInfo.WithTag(ItemInfo.ItemName(stack.item) + (stack.amount > 1 ? $" ({stack.amount})" : ""), GameNames.ItemTag(stack.item));
                    subText.text = ItemInfo.Category(db.Item(stack.item));
                    sub.Insert(0, stack.builtIn ? Badge(Localization.Extra("shopBuiltIn", "BUILT IN"), "row-badge--mounted")
                                                : Badge(Localization.Extra("shopMounted", "MOUNTED"), "row-badge--mounted"));
                    // A fitted item in a built-in one's place: which one it stands in for (it is back when this is demounted).
                    if (!stack.builtIn && Hangar.InBuiltInPlace(db, stack)
                        && db.BuiltInItems(Session.ShipIndex).Find(b => db.Item(b.item)?.categoryId == db.Item(stack.item)?.categoryId) is ItemStack replaced)
                        subText.text = string.Format(Localization.Extra("shopReplacesBuiltIn", "Replaces the built-in {0}"), ItemInfo.ItemName(replaced.item));
                    break;
                }
                case RowKind.Slot:
                    name.text = Localization.Extra("shopEmptySlot", "Empty slot");
                    name.AddToClassList("row-name--empty");
                    break;
                case RowKind.CargoItem:
                {
                    tex = ItemInfo.ItemIcon(row.item);
                    name.text = ItemInfo.TaggedItemName(row.item);   // Remake: the amount at the row's right edge (was "(n)" here)
                    subText.text = ItemInfo.Category(db.Item(row.item));   // the sub-header says "Available in cargo"
                    sub.Insert(0, Badge(Localization.Extra("shopInCargo", "IN CARGO"), "row-badge--cargo"));
                    break;
                }
                case RowKind.Blueprint:
                {
                    tex = ItemInfo.ItemIcon(row.item);
                    name.text = ItemInfo.TaggedItemName(row.item);
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
                    name.text = (pp.quantity >= 2 ? $"{pp.quantity}x " : "") + ItemInfo.TaggedItemName(pp.item);
                    subText.text = $"{Localization.Get(275)} {db.Stations.Find(s => s.index == pp.station)?.name}";
                    break;
                }
                case RowKind.Ingredient:
                {
                    tex = ItemInfo.ItemIcon(row.item);
                    name.text = ItemInfo.TaggedItemName(row.item);
                    var st = Blueprints.State(db, editing);
                    int total = Blueprints.Total(db, editing, row.type);
                    int invested = Blueprints.Invested(db, st, row.type) + Pending(row.item);
                    // Remake: the blueprint's amount (271 + invested / total) at the row's right edge, the badges alone under
                    // the name, the hold's amount at its left.
                    subText.text = "";
                    bpAmount = new VisualElement { pickingMode = PickingMode.Ignore };
                    bpAmount.AddToClassList("row-bp-amount");
                    var bpLabel = new Label(Localization.Get(271).ToUpperInvariant()) { pickingMode = PickingMode.Ignore };
                    bpLabel.AddToClassList("row-bp-amount-label");
                    var bpValue = new Label($"{invested} / {total} t") { pickingMode = PickingMode.Ignore };
                    bpValue.AddToClassList("row-bp-amount-value");
                    bpValue.AddToClassList("gof-semibold");
                    bpAmount.Add(bpLabel);
                    // Remake: units staged for another production station; a click / tap ships them now (288). Without a
                    // pointer they go on Complete or on leaving the view.
                    if (Pending(row.item) > 0)
                    {
                        var freight = Badge(Localization.Extra("bpFreight", "Freight").ToUpperInvariant(), "row-badge--freight");
                        freight.pickingMode = PickingMode.Position;
                        freight.RegisterCallback<ClickEvent>(ev => { ev.StopPropagation(); menu.PlayRelease(); Commit(null); });
                        bpAmount.Add(freight);
                    }
                    bpAmount.Add(bpValue);
                    if (invested >= total) sub.Insert(0, Badge("✓", "row-badge--mounted"));
                    else
                    {
                        // Remake: this station sells it (buy it in the shop, then add it here).
                        int stock = hangar.StockOf(row.item);
                        if (stock > 0)
                        {
                            // Remake: "SOLD HERE | 120t" (the station's amount); a click / tap opens the shop on it.
                            var soldHere = Badge($"{Localization.Extra("bpSoldHere", "SOLD HERE")} | {stock}t", "row-badge--stock");
                            soldHere.AddToClassList("row-badge--link");
                            soldHere.pickingMode = PickingMode.Position;
                            int shopItem = row.item;
                            soldHere.RegisterCallback<ClickEvent>(ev => { ev.StopPropagation(); menu.PlayPush(); GoToShopItem(shopItem); });
                            sub.Insert(0, soldHere);
                        }
                    }
                    break;
                }
                // Autocomplete (the original's hard-coded English label): a button under the list, AddProductionActions.
            }
            if (tex != null) icon.style.backgroundImage = new StyleBackground(tex);
            // Remake: the station's and the hold's amounts at the row's edges for what can be traded (shop items, the Ship
            // tab's hold, blueprint ingredients).
            bool amounts = row.kind == RowKind.ShopItem || row.kind == RowKind.CargoItem || row.kind == RowKind.Ingredient;
            // Remake: the badges (NEW, MOUNTED, IN CARGO, SOLD HERE, ✓) after the category, on every tab; the Ship tab's
            // mounted items keep MOUNTED in front of it.
            if (row.kind == RowKind.Slot) sub.Add(subText);
            else if (subText.text.Length == 0) sub.AddToClassList("row-sub--badges");   // badges alone (ingredients): flush with the name
            else
            {
                sub.Insert(0, subText);
                sub.AddToClassList("row-sub--trailing");
            }
            // Remake: the price beside the name ("Name | 354$") instead of at the row's right edge; ships keep it there.
            bool shipPrice = row.kind == RowKind.ShopShip || row.kind == RowKind.StoredShip;
            if (price.text.Length > 0 && !shipPrice)
            {
                var nameLine = new VisualElement { pickingMode = PickingMode.Ignore };
                nameLine.AddToClassList("row-name-line");
                price.AddToClassList("row-price--inline");
                var priceSep = new Label("|") { pickingMode = PickingMode.Ignore };
                priceSep.AddToClassList("row-price-sep");
                nameLine.Add(name);
                nameLine.Add(priceSep);
                nameLine.Add(price);
                texts.Add(nameLine);
            }
            else texts.Add(name);
            texts.Add(sub);
            Label Amount(int t, string side)
            {
                var l = new Label($"{t}t") { pickingMode = PickingMode.Ignore };
                l.AddToClassList("row-qty");
                l.AddToClassList(side);
                l.AddToClassList("gof-semibold");
                return l;
            }
            // The station's (or the storage's) left, the hold's right; the Ship tab's hold items only the hold's (right), a
            // blueprint's ingredients only the hold's, on the left (the station's is in their SOLD HERE badge).
            if (row.kind == RowKind.Ingredient) e.Add(Amount(hangar.CargoOf(row.item), "row-qty--station"));
            else if (amounts && row.kind != RowKind.CargoItem) e.Add(Amount(hangar.StockOf(row.item), "row-qty--station"));
            e.Add(icon);
            e.Add(texts);
            if (amounts && row.kind != RowKind.Ingredient)
            {
                // Remake: the shop opened from a blueprint's Shop action: the recipe's whole amount of the good left of
                // the hold's (whatever is invested or in the hold already).
                var shipQuantity = Amount(hangar.CargoOf(row.item), "row-qty--ship");
                if (row.kind == RowKind.ShopItem && shopBlueprintHighlight
                    && shopBlueprintMaterials.TryGetValue(row.item, out int required))
                {
                    var quantities = new VisualElement { pickingMode = PickingMode.Ignore };
                    quantities.AddToClassList("row-qty-group");
                    var recipeQuantity = new Label(string.Format(Localization.Extra("bpRecipeAmount", "({0}t)"), required)) { pickingMode = PickingMode.Ignore };
                    recipeQuantity.AddToClassList("row-qty--blueprint-required");
                    recipeQuantity.AddToClassList("gof-semibold");
                    quantities.Add(recipeQuantity);
                    quantities.Add(shipQuantity);
                    e.Add(quantities);
                }
                else e.Add(shipQuantity);
            }
            if (shipPrice && price.text.Length > 0) e.Add(price);
            if (bpAmount != null) e.Add(bpAmount);
            // ListItemWindow's "i" (image 0x470) on the selected row: Remake, left out (the details panel shows it all; the
            // keyboard's I / the controller's Y still open the item window).
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
            // Remake (PR #166): moving between ingredients no longer commits them (it rebuilt the list under the selection, a
            // player's report: a stale Autocomplete row stayed selected and A autocompleted blueprint -1 for 0 $); units
            // staged for another production station wait for FREIGHT, Complete or leaving the view.
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
        /// <summary>Up / down to the next selectable row. Remake (players' request): with wrap, past the last row comes
        /// the first and before the first the last, so the commodities are one press up from the top of the shop; only a
        /// fresh press wraps (HoldDirections), a held key's repeat stops at the end.</summary>
        public void MoveSelection(int dir, bool wrap = false)
        {
            if (rows.Count == 0) return;
            int i = selected != null ? rows.IndexOf(selected) : -1;
            for (int n = i + dir; n >= 0 && n < rows.Count; n += dir)
                if (rows[n].Selectable) { Select(rows[n], true); return; }
            if (!wrap || i < 0) return;
            for (int n = dir > 0 ? 0 : rows.Count - 1; n != i; n += dir)
                if (rows[n].Selectable) { Select(rows[n], true); return; }
        }
        void ScrollToSelected()
        {
            if (selected?.element == null || selected.element.panel == null || float.IsNaN(selected.element.layout.height)) return;
            // The first row of a section brings its header(s) into view too (#24: with a controller the shop opened with
            // "Primary weapons" scrolled off above the first row).
            var e = selected.element;
            var parent = e.parent;
            int i = parent != null ? parent.IndexOf(e) : -1;
            VisualElement top = e;
            while (i > 0 && parent[i - 1].ClassListContains("list-header")) top = parent[--i];
            if (!list.contentContainer.Contains(e)) return;   // the list was rebuilt since (ScrollTo throws on a stranger)
            if (top != e) list.ScrollTo(top);
            list.ScrollTo(e);
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
    }
}
