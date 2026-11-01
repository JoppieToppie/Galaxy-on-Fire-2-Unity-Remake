// HangarWindowDetails.cs
// HangarWindow: the details panel (part of the partial HangarWindow; its fields are in HangarWindow.cs).

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
        void ShowDetails()
        {
            var db = level.Database;
            detailStats.Clear();
            detailText.text = "";
            ItemInfo.FillModLines(detailMods, null);
            UpdatePreview();
            details.style.visibility = selected == null ? Visibility.Hidden : Visibility.Visible;
            tradeBox.AddToClassList("trade-box--hidden");
            actionButton.AddToClassList("detail-action--hidden");
            actionButton.RemoveFromClassList("detail-action--disabled");
            actionButton2?.AddToClassList("detail-action--hidden");
            // Remake: the trade arrows back in place (an ingredient moves ADD under the blueprint's amount, hides the left one).
            sellButton.style.display = DisplayStyle.Flex;
            if (sellButton.parent != sellHome) sellHome.Insert(Mathf.Min(sellHomeIndex, sellHome.childCount), sellButton);
            if (buyButton.parent != buyHome) buyHome.Insert(Mathf.Min(buyHomeIndex, buyHome.childCount), buyButton);
            buyButton.RemoveFromClassList("trade-arrow--under");
            sellLabel.text = Localization.Extra("shopSell", "SELL").ToUpperInvariant();
            tradeBox.RemoveFromClassList("trade-box--ingredient");
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
                    int stock = hangar.StockOf(item), cargo = hangar.CargoOf(item), price = hangar.BuyPriceOf(item);
                    tradeStock.text = $"{stock} t";
                    tradeCargo.text = $"{cargo} t";
                    // Remake: the hold's amount leaves out what is mounted (a launcher's remaining missiles): shown under it.
                    int mounted = hangar.MountedOf(item);
                    if (tradeMounted != null)
                    {
                        tradeMounted.text = mounted > 0 ? MountedNote(mounted) : "";
                        tradeMounted.style.display = mounted > 0 ? DisplayStyle.Flex : DisplayStyle.None;   // Modified: under the price, only when there is one
                    }
                    tradePrice.text = store ? "" : ItemInfo.Credits(price);
                    tradePrice.EnableInClassList("trade-price--expensive", !store && price > Session.Credits);
                    sellButton.EnableInClassList("trade-arrow--disabled", cargo <= 0);
                    buyButton.EnableInClassList("trade-arrow--disabled", stock <= 0);
                }
                else if (selected.kind == RowKind.Slot)
                    ShowAction(T(282).ToUpperInvariant(), !(selected.equipment >= 0 && Session.Equipment[selected.equipment].builtIn));
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
                detailName.text = ItemInfo.TaggedShipName(selected.ship);
                detailSub.text = ItemInfo.ShipRaceText(selected.ship);
                var hullMods = selected.kind == RowKind.OwnShip ? Session.ShipMods
                    : selected.kind == RowKind.ShopShip ? hangar.Stock.ModsOf(selected.ship)
                    : selected.kind == RowKind.StoredShip ? Session.KaamoShips[selected.equipment].mods : null;
                // Remake (#62): a dealer or stored hull compared with the ship flown (the original's arrows, its item window only).
                var current = selected.kind == RowKind.OwnShip ? null : db.Ship(Session.ShipIndex);
                if (s != null)
                    foreach (var (label, value, compare) in ItemInfo.ShipStatsCompared(s, hangar.ShipPrice(selected.ship), hullMods, current, Session.ShipMods))
                        AddStat(label, value, compare);
                detailText.text = GameNames.ShipDescription(selected.ship);
                if (selected.kind == RowKind.OwnShip) ItemInfo.FillModLines(detailMods, Session.ShipMods);
                if (selected.kind == RowKind.ShopShip)
                {
                    int delta = hangar.BuyShipPrice(selected.ship) - hangar.ShipPrice(Session.ShipIndex);
                    ShowAction($"{T(301).ToUpperInvariant()}   {ItemInfo.Credits(delta)}", true);
                    ItemInfo.FillModLines(detailMods, hangar.Stock.ModsOf(selected.ship));   // a traded-in hull's mods
                }
                else if (selected.kind == RowKind.StoredShip)
                {
                    // Row buttons 1 "Use" (332) and 10 "Sell" (330).
                    ShowAction(T(332).ToUpperInvariant(), selected.ship != Session.ShipIndex);
                    if (actionButton2 != null)
                    {
                        SetActionText(actionButton2, $"{T(330).ToUpperInvariant()}   {ItemInfo.Credits(hangar.StoredPrice(selected.equipment))}");
                        actionButton2.RemoveFromClassList("detail-action--hidden");
                    }
                    ItemInfo.FillModLines(detailMods, Session.KaamoShips[selected.equipment].mods);
                    ItemInfo.AddEquipmentLines(detailMods, Session.KaamoShips[selected.equipment].equipment);
                }
            }
            else
            {
                // Empty slot. Modified: nothing in the details panel (it repeated the row's "Empty slot" and type over an
                // empty Description heading): the panel is hidden, as with nothing selected.
                detailIcon.style.backgroundImage = StyleKeyword.None;
                detailName.text = "";
                detailSub.text = "";
                details.style.visibility = Visibility.Hidden;
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
            int bpShip = Modding.ModBlueprints.ShipOf(item);
            if (bpShip >= 0 && db.Ship(bpShip) is ShipData ship)
            {
                // Remake mods: a ship blueprint shows the ship it builds.
                detailSub.text = $"{Localization.Extra("bpShipBlueprint", "Ship blueprint")}  ·  {ItemInfo.ShipRaceText(bpShip)}";
                foreach (var (label, value) in ItemInfo.ShipStats(ship, ship.price)) AddStat(label, value);
                detailText.text = GameNames.ShipDescription(bpShip);
                var bp = Modding.ModBlueprints.Of(item);
                if (bp != null && bp.requiresShip >= 0)
                    detailText.text += "\n\n" + string.Format(Localization.Extra("bpRebuilds", "Rebuilds your {0}: build it while flying one."), GameNames.Ship(bp.requiresShip));
            }
            else
            {
                detailSub.text = $"{ItemInfo.Category(it)}  ·  {T(133)} {it.techLevel}";
                foreach (var (label, value) in ItemInfo.ItemStats(it)) AddStat(label, value);
                detailText.text = ItemInfo.ItemText(db, it, hangar.SystemIndex);
            }
            switch (selected.kind)
            {
                case RowKind.Blueprint:
                    ShowAction(T(283).ToUpperInvariant(), true);   // Edit
                    break;
                case RowKind.Autocomplete:
                case RowKind.ShopLink:
                case RowKind.Complete:
                    // Remake: the product's details, no second button: the list's button is the action (Enter / A do it).
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
                    // Remake (PR #166): laid out like the shop: the hold, TAKE, ADD, the blueprint. TAKE gives back units
                    // waiting to be shipped, or at the production station the player's own (Blueprints.Withdrawable: never
                    // the story's pre-invested goods).
                    tradeBox.AddToClassList("trade-box--ingredient");
                    sellLabel.text = "‹ " + Localization.Extra("shopTake", "TAKE").ToUpperInvariant();
                    buyLabel.text = Localization.Extra("bpAdd", "ADD").ToUpperInvariant() + " ›";
                    tradeStock.text = $"{cargo} t";
                    tradeCargo.text = $"{invested} / {total} t";
                    tradePrice.text = "";
                    if (tradeMounted != null) tradeMounted.style.display = DisplayStyle.None;   // Modified: no mounted count here
                    sellButton.EnableInClassList("trade-arrow--disabled", TakeableUnits() <= 0 || hangar.Load >= hangar.MaxLoad);
                    buyButton.EnableInClassList("trade-arrow--disabled", cargo <= 0 || invested >= total);
                    break;
                }
            }
            detailScroll.scrollOffset = Vector2.zero;
        }
        /// <summary>Remake: the selected ingredient's units TAKE can give back: those waiting to be shipped, and at the
        /// production station the player's own invested ones (Blueprints.Withdrawable).</summary>
        int TakeableUnits()
        {
            if (selected == null || selected.kind != RowKind.Ingredient || editing < 0) return 0;
            var db = level.Database;
            var st = Blueprints.State(db, editing);
            return Pending(selected.item) + (st.station == StationIndex ? Blueprints.Withdrawable(db, st, selected.type) : 0);
        }
        /// <summary>Remake (PR #166, the original has no way back): units of the selected ingredient back into the hold, the
        /// staged ones first, then the player's own invested ones (Blueprints.Withdraw).</summary>
        bool TakeIngredient(int units)
        {
            if (selected == null || selected.kind != RowKind.Ingredient || units <= 0) { ReleaseArrow(); return false; }
            var db = level.Database;
            int free = Mathf.Max(0, hangar.MaxLoad - hangar.Load);
            int staged = Pending(selected.item);
            int n = Mathf.Min(units, Mathf.Min(free, TakeableUnits()));
            if (n <= 0) { ReleaseArrow(); return false; }
            int local = Mathf.Min(n, staged);
            if (local > 0)
            {
                if (staged == local) pendingUnits.Remove(selected.item);
                else pendingUnits[selected.item] = staged - local;
                Shop.AddToCargo(selected.item, local);
            }
            int invested = n - local;
            if (invested > 0)
            {
                if (!Blueprints.Withdraw(db, editing, selected.type, invested, StationIndex))
                {
                    ReleaseArrow();
                    Rebuild();
                    return local > 0;
                }
                Shop.AddToCargo(selected.item, invested);
            }
            Rebuild();
            return true;
        }
        int Pending(int ingredient) => pendingUnits.TryGetValue(ingredient, out int n) ? n : 0;
        bool HasPending { get { foreach (var kv in pendingUnits) if (kv.Value > 0) return true; return false; } }
        int StationIndex => level.Station != null ? level.Station.index : Session.StationIndex;
        /// <summary>Item::transactionBlueprint: units from the hold into the blueprint. The first unit of an unstarted
        /// blueprint asks 212 "Start production at this station?" (528 for 210 / 223 without gate routes). Remake (PR #166):
        /// at the production station (or starting it here) they go in at once, as the player's own (Blueprints.Withdraw may
        /// give them back); at another station they wait to be shipped (Commit, 288).</summary>
        bool AddIngredient(int units)
        {
            var db = level.Database;
            int item = selected.item;
            // Remake mods: a skin's blueprint ("requiresShip") is only built while flying that ship.
            string refusal = Modding.ModBlueprints.RequiresShipRefusal(editing);
            if (refusal != null) { ReleaseArrow(); menu.ShowToast(refusal); return false; }
            var st = Blueprints.State(db, editing);
            if (Blueprints.IsEmpty(st) && !startConfirmed && !HasPending)
            {
                ReleaseArrow();
                if (!Blueprints.CanStartIn(db, editing, StationIndex)) { menu.ShowToast(Localization.Get(528)); return false; }
                menu.ShowDialog(Localization.Get(212), () =>   // Start production here?
                {
                    startConfirmed = true;
                    if (IsOpen && selected != null && selected.kind == RowKind.Ingredient) Trade(1);
                });
                return false;
            }
            int moved = 0;
            for (int n = 0; n < units; n++)
            {
                int left = st.remaining[selected.type] - Pending(item);
                if (left <= 0 || hangar.CargoOf(item) <= 0) break;
                Shop.RemoveFromCargo(item, 1);
                if (st.station < 0 || st.station == StationIndex)
                    Blueprints.Invest(db, editing, item, 1, StationIndex, true);
                else
                    pendingUnits[item] = Pending(item) + 1;
                moved++;
            }
            if (moved == 0) { ReleaseArrow(); return false; }
            Rebuild();
            return true;
        }
        /// <summary>setSellMode(false): the pending units go into the blueprint; at another station than the production
        /// station for 200 $ per unit (288), volatile goods never (289). A refusal (289, too little money) or No takes them
        /// back to the hold, so the next tab switch or Back doesn't ask again. Remake (PR #166): a full blueprint waits for
        /// Complete.</summary>
        void Commit(System.Action after)
        {
            var db = level.Database;
            if (!HasPending || editing < 0) { after?.Invoke(); return; }
            var st = Blueprints.State(db, editing);
            int units = 0;
            bool volatileGoods = false;
            foreach (var kv in pendingUnits)
            {
                units += kv.Value;
                if (kv.Value > 0 && Blueprints.IsVolatile(kv.Key)) volatileGoods = true;
            }
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
                menu.ShowChoice(Localization.Get(288).Replace("#S", where).Replace("#C", ItemInfo.Credits(cost)),
                    Localization.Get(134), Localization.Get(135),
                    () =>
                    {
                        if (cost > Session.Credits)
                        {
                            Revert();
                            menu.ShowToast(Localization.Get(203).Replace("#C", ItemInfo.Credits(cost - Session.Credits)));
                            return;
                        }
                        Session.Credits -= cost;
                        Apply();
                        after?.Invoke();
                    },
                    () =>
                    {
                        Revert();   // No: the staged units back to the hold, and on (the tab, Back, closing)
                        after?.Invoke();
                    });
                return;
            }
            Apply();
            after?.Invoke();
        }
        void Apply()
        {
            var db = level.Database;
            foreach (var kv in pendingUnits)
                if (kv.Value > 0) Blueprints.Invest(db, editing, kv.Key, kv.Value, StationIndex, true);
            pendingUnits.Clear();
            startConfirmed = false;
            Rebuild();
        }
        void Revert()
        {
            foreach (var kv in pendingUnits) Shop.AddToCargo(kv.Key, kv.Value);
            pendingUnits.Clear();
            startConfirmed = false;
            Rebuild();
        }
        /// <summary>A completed run: 211 to the hold or 210 waiting at the production station. Remake (PR #166): the view stays
        /// on the blueprint for another run (it went to the Shop tab / the blueprint list). Remake (a player's suggestion): a
        /// run made at its production station whose product has a free slot (Hangar.OffersMountAfterBuild) asks 211 +
        /// "Mount it now?"; Yes mounts it (208) and shows the Ship tab, No leaves it in the hold.</summary>
        void Finish()
        {
            var db = level.Database;
            int product = editing;
            int station = Blueprints.State(db, product).station;
            bool here = Blueprints.Produce(db, product, StationIndex);
            hangar = new Hangar(db, level.Stock);   // the new product needs its price
            string name = ItemInfo.ItemName(product);
            if (here && Modding.ModBlueprints.ShipOf(product) >= 0)
            {
                // Remake mods: a ship blueprint's ship, taken here like a bought ship.
                string text = hangar.DeliverBuiltShips();
                level.ReplacePlayerShip(Session.ShipIndex);
                hangar = new Hangar(db, level.Stock);
                menu.ShowDialog(text ?? Localization.Get(211).Replace("#N", name), null, true);
                editing = -1;
                SetTab(Tab.Ship);
                return;
            }
            Rebuild();
            if (!here)
            {
                string where = db.Stations.Find(s => s.index == station)?.name ?? "";
                menu.ShowDialog(Localization.Get(210).Replace("#N", name).Replace("#S", where), null, true);
                return;
            }
            string done = Localization.Get(211).Replace("#N", name);
            if (hangar.OffersMountAfterBuild(product))
                menu.ShowChoice(done + "\n\n" + Localization.Extra("blueprintMountAsk", "Mount it now?"),
                                Localization.Get(134), Localization.Get(135), () =>
                {
                    if (!hangar.Mount(product)) return;
                    menu.PlayClip(menu.shopMount);
                    menu.ShowToast(Localization.Get(208).Replace("#N", name));
                    SetTab(Tab.Ship);
                }, null);
            else menu.ShowDialog(done, null, true);
        }
        /// <summary>Autocomplete (button 23): 195 "Autocomplete blueprint for #C?"; an empty blueprint takes this station.</summary>
        void AskAutocomplete()
        {
            var db = level.Database;
            if (editing < 0 || db.Item(editing) == null || !Blueprints.CanAutocomplete(editing)) return;   // only in the ingredients view
            string refusal = Modding.ModBlueprints.RequiresShipRefusal(editing);
            if (refusal != null) { menu.ShowToast(refusal); return; }
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
        /// <summary>Remake: a button's text, kept whole for FitActions.</summary>
        void SetActionText(Button b, string text)
        {
            b.userData = text;
            b.text = text;
            FitActions();
        }
        /// <summary>Remake: the buttons under a ship's view fit their text, centred; one wider than the view's column breaks
        /// before its last word (the price), so only the amount goes to a second line.</summary>
        void FitActions()
        {
            foreach (var b in new[] { actionButton, actionButton2 })
            {
                if (b == null || !(b.userData is string full)) continue;
                string shown = full;
                float avail = previewSide.resolvedStyle.width;
                if (b.parent == actionArea && !float.IsNaN(avail) && avail > 0f)
                {
                    var size = b.MeasureTextSize(full, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined);
                    var st = b.resolvedStyle;
                    float frame = st.paddingLeft + st.paddingRight + st.borderLeftWidth + st.borderRightWidth;
                    int cut = full.TrimEnd().LastIndexOf(' ');
                    if (size.x + frame > avail && cut > 0) shown = full.Substring(0, cut).TrimEnd() + "\n" + full.Substring(cut + 1).Trim();
                }
                if (b.text != shown) b.text = shown;
            }
        }
        void ShowAction(string text, bool enabled)
        {
            SetActionText(actionButton, text);
            actionButton.RemoveFromClassList("detail-action--hidden");
            actionButton.EnableInClassList("detail-action--disabled", !enabled);
        }
        /// <summary>A stat row; 'compare' -1 / 1 / 0 adds the worse / better / equal arrow (ItemInfo.ShipStatsCompared), 2 none.</summary>
        void AddStat(string label, string value, int compare = 2)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("stat-row");
            var l = new Label(label) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("stat-label");
            var v = new Label(value) { pickingMode = PickingMode.Ignore };
            v.AddToClassList("stat-value");
            v.AddToClassList("gof-semibold");
            row.Add(l);
            if (compare != 2)
            {
                var right = new VisualElement { pickingMode = PickingMode.Ignore };
                right.AddToClassList("stat-compare");
                var a = new VisualElement { pickingMode = PickingMode.Ignore };
                a.AddToClassList("stat-arrow");
                var tex = Resources.Load<Texture2D>("GoF2Hud/" + (compare < 0 ? "compare_worse" : compare > 0 ? "compare_better" : "compare_equal"));
                if (tex != null) a.style.backgroundImage = new StyleBackground(tex);
                right.Add(v);
                right.Add(a);
                row.Add(right);
            }
            else row.Add(v);
            detailStats.Add(row);
        }
        /// <summary>The Shop tab in a session: when the next shipment is expected (the shared stock's re-roll) and, while one
        /// of them is short, the rare goods' next one (NetStock.RestockLeft). An estimate, like a delivery's: the shipments
        /// really come a little early or late (NetStock.Shipment), so it shows "~N min", "soon" in the last 90 s and "any
        /// moment" once the scheduled time has passed; written only when the text changes.</summary>
        void UpdateRestock()
        {
            float reset = -1f, rare = -1f;
            bool show = IsOpen && tab == Tab.Shop && !hangar.Storage
                        && GoF2Remake.Multiplayer.NetStock.RestockLeft(hangar.Station, out reset, out rare);
            if (show != restockVisible)
            {
                restockVisible = show;
                restockLabel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (!show) return;
            string text = $"{Localization.Extra("hangarShipment", "Next shipment").ToUpperInvariant()}  {Eta(reset)}";
            if (rare >= 0f) text += $"   {Localization.Extra("hangarRareRestock", "Rare goods").ToUpperInvariant()}  {Eta(rare)}";
            if (restockLabel.text != text) restockLabel.text = text;
        }
        /// <summary>A shipment's estimate from its scheduled seconds.</summary>
        static string Eta(float seconds)
        {
            if (seconds <= 0f) return Localization.Extra("hangarEtaDue", "any moment").ToUpperInvariant();
            if (seconds <= 90f) return Localization.Extra("hangarEtaSoon", "soon").ToUpperInvariant();
            return string.Format(Localization.Extra("hangarEtaMinutes", "~{0} min"), Mathf.CeilToInt(seconds / 60f)).ToUpperInvariant();
        }
        void UpdateFooter()
        {
            UpdateRestock();
            cargoLabel.text = $"{Localization.Get(184).ToUpperInvariant()}  {hangar.Load} / {hangar.MaxLoad} t";
            cargoLabel.EnableInClassList("footer-cargo--over", hangar.Overloaded);
            creditsLabel.text = ItemInfo.Credits(Session.Credits);
        }
    }
}
