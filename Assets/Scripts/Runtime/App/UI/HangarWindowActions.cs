// HangarWindowActions.cs
// HangarWindow: the actions and the held trade arrows (part of the partial HangarWindow; its fields are in HangarWindow.cs).

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
        // ---- actions -----------------------------------------------------------------------------------------
        /// <summary>Left / right: sell / buy one unit of the selected shop item (Item::transaction). 'all' (a held arrow's repeat,
        /// HeldTrade): running out of credits after some units just stops, without the "not enough credits" message.</summary>
        public bool Trade(int direction, bool sound = true, int units = 1, bool all = false)
        {
            if (selected != null && selected.kind == RowKind.Ingredient)
            {
                // ADD sounds like selling (to the station, 0x64), TAKE like buying (to the ship, 0x65).
                bool moved = direction > 0 ? AddIngredient(units) : TakeIngredient(units);
                if (moved && sound) menu.PlayClip(direction > 0 ? menu.shopSell : menu.shopBuy);
                return moved;
            }
            if (selected == null || selected.kind != RowKind.ShopItem) return false;
            bool changed = false;
            for (int n = 0; n < units; n++)
            {
                if (direction > 0)
                {
                    var r = hangar.Buy(selected.item, out int need);
                    if (r == Hangar.Result.NoCredits)
                    {
                        if (!(all && changed)) menu.ShowToast(Localization.Get(203).Replace("#C", ItemInfo.Credits(need)));
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
            if (!changed) return false;
            if (sound) menu.PlayClip(direction > 0 ? menu.shopBuy : menu.shopSell);
            Rebuild();
            return true;
        }
        /// <summary>Enter / A / the action button: mount, demount or buy the selected ship; on a shop row buy one unit.</summary>
        public void Action()
        {
            if (selected == null) return;
            var db = level.Database;
            switch (selected.kind)
            {
                // Remake (#24): A / Enter buys (takes, adds) one unit like the right arrow, X sells (stores) one like the left;
                // the original only has the arrows, and a confirm that did nothing on a shop row read as a dead button.
                case RowKind.ShopItem:
                case RowKind.Ingredient:
                    Trade(1);
                    break;
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
                case RowKind.ShopLink:
                    // Remake: the Shop tab with the recipe's goods marked (GoToShopItem).
                    menu.PlayRelease();
                    GoToShopItem(-1);
                    break;
                case RowKind.Complete:
                    Complete();
                    break;
                case RowKind.VipCard:
                    AskBuyVip();   // remake: the Kaamo Club VIP Card
                    break;
                case RowKind.Slot when selected.equipment >= 0:
                {
                    int item = Session.Equipment[selected.equipment].item;
                    if (Session.Equipment[selected.equipment].builtIn)
                    {
                        menu.ShowToast(string.Format(Localization.Extra("shopBuiltInStays", "Built into the {0}: it stays with the ship."), ItemInfo.ShipName(Session.ShipIndex)));
                        break;
                    }
                    if (!Hangar.IsSaleable(item)) { menu.ShowToast(Localization.Get(323)); break; }
                    // HangarWindow::selectItem 0x178e20: a passenger cabin stays while the others couldn't seat the passengers aboard.
                    var cabin = level.Database.Item(item);
                    if (cabin != null && cabin.categoryId == 20 && Session.Passengers > 0
                        && Freelance.MaxPassengers(level.Database) - cabin.Stat("cabinSize") < Session.Passengers) { menu.ShowToast(Localization.Get(323)); break; }
                    hangar.Demount(selected.equipment);
                    menu.PlayClip(menu.shopDemount);
                    menu.ShowToast(Localization.Get(209).Replace("#N", ItemInfo.ItemName(item)));
                    Rebuild();   // #45: the selection stays where it was (Rebuild's nearest row), not back at the top
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
                            Rebuild();
                        });
                    }
                    else if (r == Hangar.Result.Ok && hangar.Mount(item))
                    {
                        menu.PlayClip(menu.shopMount);
                        menu.ShowToast(Localization.Get(208).Replace("#N", ItemInfo.ItemName(item)));
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
                    // Remake (#60): the question says what happens to the old ship (the original's 304 doesn't): it is traded in.
                    int oldPrice = hangar.ShipPrice(Session.ShipIndex), difference = hangar.BuyShipPrice(ship) - oldPrice;
                    string tradeIn = Localization.Get(304) + "\n\n" + string.Format(difference >= 0
                            ? Localization.Extra("shopTradeInNote", "Your {0} is traded in for {1}, so you pay {2}.")
                            : Localization.Extra("shopTradeInRefund", "Your {0} is traded in for {1}, so you get {2} back."),
                        ItemInfo.ShipName(Session.ShipIndex), ItemInfo.Credits(oldPrice), ItemInfo.Credits(Mathf.Abs(difference)))
                        + "\n\n" + Localization.Extra("shopGearToHold", "Your equipment goes into the cargo hold.");
                    if (!KaamoClub.Owned) { menu.ShowDialog(tradeIn, TradeIn); break; }
                    // 304, then 327 "sell your old ship or keep it and have it brought to your station?" (330 / 331).
                    menu.ShowDialog(Localization.Get(304), () => menu.ShowChoice(Localization.Get(327), Localization.Get(330), Localization.Get(331), TradeIn, () =>
                    {
                        var k = hangar.CanKeepAndBuyShip(ship, out int missing);
                        if (k == Hangar.Result.AlreadyStored) { menu.ShowDialog(Localization.Get(328), null, true); return; }
                        if (k == Hangar.Result.NoCredits) { menu.ShowToast(Localization.Get(203).Replace("#C", ItemInfo.Credits(missing))); return; }
                        menu.AskMoveEquipment(move => Reserved(() => hangar.KeepAndBuyShip(ship, move)));   // #83
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
            // Remake: X / the left arrow (an ingredient's TAKE).
            if (selected != null && (selected.kind == RowKind.ShopItem || selected.kind == RowKind.Ingredient)) { Trade(-1); return; }
            // Remake (players' feedback): a mounted item's MOVE, to the next slot of its type (the last one to the first).
            if (selected != null && selected.kind == RowKind.Slot && selected.equipment >= 0)
            {
                int next = hangar.NextSlot(selected.equipment);
                if (next >= 0) MoveSlot(selected.equipment, next);
                return;
            }
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
        /// <summary>Remake: the mounted item at 'equipment' to slot 'place' of its type (Hangar.MoveToSlot); it stays
        /// selected in its new row.</summary>
        void MoveSlot(int equipment, int place)
        {
            if (!hangar.MoveToSlot(equipment, place)) return;
            menu.PlayClip(menu.shopMount);   // the hangar ship's weapon models follow (StationLevel.RefreshTurret)
            Rebuild();
        }

        /// <summary>Remake: a mounted Ship tab row dragged onto another slot row of its type (mounted or empty) with the
        /// mouse or a pen moves the item there (touch keeps the list's scrolling: MOVE does it there).</summary>
        void HookSlotDrag(Row row, VisualElement e)
        {
            e.RegisterCallback<PointerDownEvent>(ev =>
            {
                if (row.equipment < 0 || ev.button != 0 || ev.pointerType == UnityEngine.UIElements.PointerType.touch || dragPointer >= 0) return;
                dragRow = row;
                dragPointer = ev.pointerId;
                dragStart = ev.position;
                dragging = false;
                dragMoved = false;
                e.CapturePointer(ev.pointerId);
            });
            e.RegisterCallback<PointerMoveEvent>(ev =>
            {
                if (ev.pointerId != dragPointer || dragRow != row) return;
                if (!dragging && ((Vector2)ev.position - dragStart).magnitude < 12f) return;
                if (!dragging) { dragging = true; e.AddToClassList("list-row--dragging"); }
                var over = RowAt(ev.position);
                var target = over != null && over != row && over.kind == RowKind.Slot && over.type == row.type && over.place >= 0 ? over : null;
                if (target == dragTarget) return;
                dragTarget?.element.RemoveFromClassList("list-row--drop-target");
                dragTarget = target;
                dragTarget?.element.AddToClassList("list-row--drop-target");
            });
            e.RegisterCallback<PointerUpEvent>(ev =>
            {
                if (ev.pointerId != dragPointer) return;
                var target = dragging ? dragTarget : null;
                dragMoved = dragging;
                EndSlotDrag(e);
                if (target != null) MoveSlot(row.equipment, target.place);
            });
            e.RegisterCallback<PointerCaptureOutEvent>(_ => { if (dragRow == row) EndSlotDrag(e); });
        }

        void EndSlotDrag(VisualElement e)
        {
            if (dragPointer >= 0 && e.HasPointerCapture(dragPointer)) e.ReleasePointer(dragPointer);
            e.RemoveFromClassList("list-row--dragging");
            dragTarget?.element.RemoveFromClassList("list-row--drop-target");
            dragRow = dragTarget = null;
            dragPointer = -1;
            dragging = false;
        }

        /// <summary>The list row under a panel position (null = none).</summary>
        Row RowAt(Vector2 position)
        {
            for (var v = list.panel?.Pick(position); v != null; v = v.parent)
            {
                var r = rows.Find(x => x.element == v);
                if (r != null) return r;
            }
            return null;
        }

        public bool StorageMode => hangar != null && hangar.Storage;
        void HookArrow(VisualElement arrow, int direction)
        {
            arrow.RegisterCallback<PointerDownEvent>(e =>
            {
                if (heldPointer >= 0 || arrow.ClassListContains("trade-arrow--disabled")) return;
                heldPointer = e.pointerId;
                heldDirection = direction;
                arrow.CapturePointer(e.pointerId);
                arrow.AddToClassList("trade-arrow--pressed");
                BeginHold(direction);
                e.StopPropagation();
            });
            arrow.RegisterCallback<PointerUpEvent>(e => { if (e.pointerId == heldPointer) ReleaseArrow(); });
            arrow.RegisterCallback<PointerCancelEvent>(e => { if (e.pointerId == heldPointer) ReleaseArrow(); });
            // A lost capture (the window closed or rebuilt mid-press) never sends the release: the arrow kept trading.
            arrow.RegisterCallback<PointerCaptureOutEvent>(e => { if (e.pointerId == heldPointer) ReleaseArrow(); });
        }
        void ReleaseArrow()
        {
            int id = heldPointer;
            heldPointer = -1;   // first: releasing the capture sends PointerCaptureOut back here
            if (id >= 0)
            {
                if (sellButton.HasPointerCapture(id)) sellButton.ReleasePointer(id);
                if (buyButton.HasPointerCapture(id)) buyButton.ReleasePointer(id);
            }
            heldDirection = 0;
            sellButton.RemoveFromClassList("trade-arrow--pressed");
            buyButton.RemoveFromClassList("trade-arrow--pressed");
            HeldTrade.SetFill(sellFill, 0f);
            HeldTrade.SetFill(buyFill, 0f);
            // The hold's trades to the multiplayer host as one message (the user's request: not one per unit).
            if (holdBatch) { holdBatch = false; hangar.EndBatch(); }
        }
        /// <summary>A press on an arrow: one unit now, the stack the hold will work through counted (HeldTrade: selling what
        /// the hold has of it, buying the station's stock as far as the hold has room, adding to a blueprint what the hold has),
        /// and on a shop row its units until the release are one batch (Hangar.BeginBatch), so a multiplayer session's host
        /// hears of the whole hold once, when the arrow is let go (ReleaseArrow).</summary>
        void BeginHold(int direction)
        {
            heldMs = 0f;
            holdDone = 1;
            holdTotal = 1;
            if (selected != null && selected.kind == RowKind.ShopItem)
            {
                holdTotal = direction < 0 ? hangar.CargoOf(selected.item)
                          : Mathf.Min(hangar.StockOf(selected.item), Mathf.Max(1, hangar.MaxLoad - hangar.Load));
                if (!holdBatch) { hangar.BeginBatch(); holdBatch = true; }
            }
            else if (selected != null && selected.kind == RowKind.Ingredient)
                holdTotal = direction > 0 ? hangar.CargoOf(selected.item) : TakeableUnits();
            // A shop row's unit sounds through HeldTrade's pitched voice (the repeats rise from here); the fill waits for the hold.
            if (selected != null && (selected.kind == RowKind.ShopItem || selected.kind == RowKind.Ingredient))
            {
                if (Trade(direction, false)) TradeTick(direction);
            }
            else Trade(direction);
        }
        void TradeTick(int direction) => HeldTrade.Tick(direction > 0 ? menu.shopBuy : menu.shopSell, holdDone, holdTotal);
        /// <summary>Keyboard / controller: the held vertical (select) and horizontal (sell / buy) direction, every frame.</summary>
        public void HoldDirections(int vertical, int horizontal, float dtMs)
        {
            if (!IsOpen) return;
            if (vertical != moveDirection)
            {
                moveDirection = vertical;
                moveMs = -350f;
                if (vertical != 0) MoveSelection(vertical, true);
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
            (horizontal < 0 ? sellButton : buyButton).AddToClassList("trade-arrow--pressed");
            BeginHold(horizontal);
        }
    }
}
