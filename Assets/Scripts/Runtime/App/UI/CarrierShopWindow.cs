// CarrierShopWindow.cs
// Remake (World.CapitalShips): the resupply window of the carrier or the Vossk battleship in the flight HUD while docked at
// it (ObjectDocking.Resupply), built like the hangar's Shop tab (HangarWindow, its HangarWindow.uss) so it reads as the same
// shop:
//   top      the ship's tab ("Carrier" / "Battleship") and Undock (closing the window undocks)
//   list     headers and rows with the shop icons, a line under each name and the unit price: Repairs (hull and armor),
//            266 "Secondary weapons" (each mounted secondary: its rounds; then the secondaries it sells), 265 / 267 / 269 the
//            primary weapons, turrets and equipment it sells (its race's gear, into the hold), the energy cells' category
//   details  icon, name, category and tech level; the trade box (what the ship has left in store, the buy arrow, the unit
//            price, Ship: what you have), Buy all; the item's stats and description; the repair's Repair button and hull /
//            armor / shield; rows it can't sell carry SOLD OUT / NOT STOCKED (a blueprint product, CapitalShips.Store)
//   footer   the hold and the credits
// Controls: tap / click a row, the buy arrow (held: repeats after 300 ms, every 90 ms), Buy all; up / down (W / S, the
// D-pad, the left stick) select, right / D / Enter / A buy one (held repeats), Shift + right or X buy all, Esc / Backspace /
// B / Menu undock. Sounds: the button push on a row, Button_to_ship (0x65) on a purchase, the station hangar's ambience under the
// window (FlightHud), the button release on undocking; the deck takes the ship without a sound for now (nothing fitting yet).
// Rules (prices, rooms, the buy itself) are CapitalShips'.

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public class CarrierShopWindow
    {
        readonly Database db;
        readonly PlayerHealth health;
        readonly Action<bool> playButton;
        readonly Action<AudioClip> playUi;
        readonly Action<string> message;
        readonly Action closed;

        readonly VisualElement window, details, detailIcon, detailStats, tradeBox, sellArrow, buyArrow, buyFill;
        readonly ScrollView list, detailScroll;
        readonly Label detailName, detailSub, detailText, tradeLeft, tradeRight, tradeNote, tradePrice, cargoLabel, creditsLabel;
        readonly VisualElement detailMods = ItemInfo.NewModBlock();   // the ship's Kaamo Club mods, under the repair's text
        readonly Button actionButton, tab;
        readonly Label storeLabel;
        NpcShip host;
        CapitalShips.Store store;

        readonly List<(CapitalShips.Offer offer, VisualElement row)> rows = new List<(CapitalShips.Offer, VisualElement)>();
        int selected;
        int openedFrame;
        // A held buy (the arrow, a key or the D-pad): its repeat, like the hangar's held trade arrows.
        int heldPointer = -1;
        bool heldKey;
        float heldMs;
        int holdTotal, holdDone;   // HeldTrade: what there is room for at the press, the units bought since
        int moveDirection;
        float moveMs;

        const float MoveAfterMs = 350f, MoveEveryMs = 80f;

        public bool IsOpen { get; private set; }
        /// <summary>Modified: the menus' hover sound on the window's tab (FlightHud; UiHover: tabs and side menus only).</summary>
        public Action HoverSound;

        static string T(int id) => Localization.Get(id);
        static string X(string key, string english) => Localization.Extra(key, english);

        public CarrierShopWindow(VisualElement root, Database database, PlayerHealth playerHealth, Action<bool> button, Action<AudioClip> ui,
                                 Action<string> hudMessage, Action onClosed)
        {
            db = database;
            health = playerHealth;
            playButton = button;
            playUi = ui;
            message = hudMessage;
            closed = onClosed;

            window = El("hangar-window", "carrier-shop");
            UiHover.Hook(window, () => HoverSound?.Invoke());   // Modified
            window.style.display = DisplayStyle.None;
            var top = El("hangar-top");
            tab = new Button { text = CapitalShips.CarrierName.ToUpperInvariant(), focusable = false };
            tab.AddToClassList("hangar-tab");
            tab.AddToClassList("hangar-tab--active");
            tab.AddToClassList("gof-semibold");
            top.Add(tab);
            top.Add(El("hangar-top-spacer"));
            var close = new Button(() => { playButton?.Invoke(false); Close(); }) { text = X("hudUndock", "UNDOCK"), focusable = false };
            close.AddToClassList("hangar-close");
            close.AddToClassList("gof-semibold");
            top.Add(close);
            window.Add(top);

            var body = El("hangar-body");
            body.pickingMode = PickingMode.Ignore;
            list = new ScrollView { focusable = false };
            list.AddToClassList("hangar-list");
            body.Add(list);

            details = El("hangar-details");
            var head = El("detail-head");
            detailIcon = El("detail-icon");
            var titles = El("detail-titles");
            detailName = Lbl("detail-name", true);
            detailSub = Lbl("detail-sub");
            titles.Add(detailName);
            titles.Add(detailSub);
            head.Add(detailIcon);
            head.Add(titles);
            details.Add(head);

            tradeBox = El("trade-box");
            var left = El("trade-side");
            storeLabel = Lbl("trade-side-label");
            storeLabel.text = CapitalShips.CarrierName.ToUpperInvariant();
            tradeLeft = Lbl("trade-side-value", true);
            tradeLeft.text = "∞";
            left.Add(storeLabel);
            left.Add(tradeLeft);
            sellArrow = El("trade-arrow");
            sellArrow.style.visibility = Visibility.Hidden;   // the carrier only sells
            buyArrow = El("trade-arrow", "trade-arrow--buy");
            buyArrow.pickingMode = PickingMode.Position;
            var buyLabel = Lbl("trade-arrow-label", true);
            buyLabel.text = X("shopBuy", "BUY") + " ›";
            buyArrow.Add(buyLabel);
            tradePrice = Lbl("trade-price", true);
            var right = El("trade-side", "trade-side--right");
            var rightLabel = Lbl("trade-side-label");
            rightLabel.text = T(183).ToUpperInvariant();
            tradeRight = Lbl("trade-side-value", true);
            tradeNote = Lbl("trade-side-note");
            right.Add(rightLabel);
            right.Add(tradeRight);
            right.Add(tradeNote);
            tradeBox.Add(left);
            tradeBox.Add(sellArrow);
            tradeBox.Add(tradePrice);
            tradeBox.Add(buyArrow);
            tradeBox.Add(right);
            details.Add(tradeBox);

            buyFill = HeldTrade.AddFill(buyArrow);   // remake: held, BUY speeds up to all there is room for in two seconds (HeldTrade)

            actionButton = new Button(() => Buy(1)) { focusable = false };
            actionButton.AddToClassList("detail-action");
            actionButton.AddToClassList("gof-semibold");
            details.Add(actionButton);

            detailScroll = new ScrollView { focusable = false };
            detailScroll.AddToClassList("detail-scroll");
            detailStats = El("detail-stats");
            detailText = Lbl("detail-text");
            detailScroll.Add(detailStats);
            detailScroll.Add(detailText);
            detailScroll.Add(detailMods);
            details.Add(detailScroll);
            body.Add(details);
            window.Add(body);

            var footer = El("hangar-footer");
            cargoLabel = Lbl("footer-cargo", true);
            creditsLabel = Lbl("footer-credits", true);
            footer.Add(cargoLabel);
            footer.Add(creditsLabel);
            window.Add(footer);
            root.Add(window);

            buyArrow.RegisterCallback<PointerDownEvent>(e =>
            {
                if (heldPointer >= 0 || buyArrow.ClassListContains("trade-arrow--disabled")) return;
                heldPointer = e.pointerId;
                buyArrow.CapturePointer(e.pointerId);
                buyArrow.AddToClassList("trade-arrow--pressed");
                BeginHold();
                e.StopPropagation();
            });
            buyArrow.RegisterCallback<PointerUpEvent>(e => { if (e.pointerId == heldPointer) ReleaseArrow(); });
            buyArrow.RegisterCallback<PointerCancelEvent>(e => { if (e.pointerId == heldPointer) ReleaseArrow(); });
            buyArrow.RegisterCallback<PointerCaptureOutEvent>(e => { if (e.pointerId == heldPointer) ReleaseArrow(); });
        }

        static VisualElement El(params string[] classes)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            foreach (var c in classes) e.AddToClassList(c);
            return e;
        }

        static Label Lbl(string cls, bool semibold = false)
        {
            var l = new Label { pickingMode = PickingMode.Ignore };
            l.AddToClassList(cls);
            if (semibold) l.AddToClassList("gof-semibold");
            return l;
        }

        // ---- open / close ------------------------------------------------------------------------------------

        /// <summary>Opens on the ship docked at ('ship': its name, its store).</summary>
        public void Open(NpcShip ship)
        {
            host = ship;
            store = ship != null ? ship.GetComponent<CapitalShip>()?.store : null;
            string name = CapitalShips.HostName(ship).ToUpperInvariant();
            tab.text = name;
            storeLabel.text = name;
            IsOpen = true;
            openedFrame = Time.frameCount;
            selected = -1;
            window.style.display = DisplayStyle.Flex;
            Rebuild();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            ReleaseArrow();
            heldKey = false;
            window.style.display = DisplayStyle.None;
            closed?.Invoke();
        }

        /// <summary>Hidden at once without the undock (the docking ended some other way: the carrier destroyed, a jump).</summary>
        public void Hide()
        {
            if (!IsOpen) return;
            IsOpen = false;
            ReleaseArrow();
            window.style.display = DisplayStyle.None;
        }

        // ---- list --------------------------------------------------------------------------------------------

        void Rebuild()
        {
            float scroll = list.scrollOffset.y;
            var keep = selected >= 0 && selected < rows.Count ? rows[selected].offer : null;
            list.Clear();
            rows.Clear();
            var offers = CapitalShips.ResupplyOffers(db, health, store);
            string group = null;
            foreach (var o in offers)
            {
                string header = HeaderOf(o);
                if (group != header) { group = header; AddHeader(header); }
                AddRow(o);
            }
            selected = keep == null ? -1 : rows.FindIndex(r => r.offer.kind == keep.kind && r.offer.item == keep.item);
            if (selected < 0) selected = rows.Count > 0 ? 0 : -1;
            for (int i = 0; i < rows.Count; i++) rows[i].row.EnableInClassList("list-row--selected", i == selected);
            list.schedule.Execute(() => { list.scrollOffset = new Vector2(0f, scroll); ScrollToSelected(); });
            ShowDetails();
            cargoLabel.text = $"{T(184).ToUpperInvariant()}  {Shop.CargoLoad()} / {Shop.MaxLoad(db)} t";
            creditsLabel.text = ItemInfo.Credits(Session.Credits);
        }

        /// <summary>The row's group: Repairs, the hangar's type headers (265-269) for the rounds and the gear, the energy
        /// cells' category.</summary>
        string HeaderOf(CapitalShips.Offer o)
        {
            if (o.kind == CapitalShips.OfferKind.Repair) return X("resupplyRepairs", "Repairs");
            if (o.kind == CapitalShips.OfferKind.Cargo) return ItemInfo.Category(db.Item(o.item));
            int type = db.Item(o.item)?.TypeId ?? 1;
            return T(type == 0 ? 265 : type == 1 ? 266 : type == 2 ? 267 : 269);
        }

        void AddHeader(string text)
        {
            var l = new Label(text.ToUpperInvariant()) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("list-header");
            l.AddToClassList("gof-semibold");
            list.Add(l);
        }

        void AddRow(CapitalShips.Offer o)
        {
            var e = new VisualElement();
            e.AddToClassList("list-row");
            var icon = El("row-icon");
            var texts = El("row-texts");
            var name = Lbl("row-name");
            var sub = El("row-sub");
            var subText = Lbl("row-sub-text");
            var price = Lbl("row-price", true);
            Texture2D tex;
            if (o.kind == CapitalShips.OfferKind.Repair)
            {
                tex = ItemInfo.ShipIcon(Session.ShipIndex);
                name.text = X("resupplyRepair", "Repair hull and armor");
                var hp = health != null ? health.Hp : null;
                subText.text = hp == null ? "" : $"{X("statHull", "Hull")} {hp.hull} / {hp.maxHull}  ·  {X("statArmor", "Armor")} {hp.armor} / {hp.maxArmor}";
                if (o.room > 0)
                {
                    price.text = ItemInfo.Credits(o.unitPrice);
                    price.EnableInClassList("row-price--expensive", o.unitPrice > Session.Credits);
                }
                else sub.Add(Badge(X("resupplyIntact", "INTACT"), "row-badge--mounted"));
            }
            else
            {
                var it = db.Item(o.item);
                tex = ItemInfo.ItemIcon(o.item);
                name.text = ItemInfo.TaggedItemName(o.item);
                if (o.notStocked) sub.Add(Badge(X("resupplyBadgeNotStocked", "NOT STOCKED"), "row-badge--cargo"));
                else if (o.stock == 0) sub.Add(Badge(X("resupplyBadgeSoldOut", "SOLD OUT"), "row-badge--cargo"));
                if (o.kind == CapitalShips.OfferKind.Ammo)
                {
                    sub.Add(Badge(X("shopMounted", "MOUNTED"), "row-badge--mounted"));
                    subText.text = $"{ItemInfo.Category(it)}  ·  {T(183)} {o.have}";
                }
                else subText.text = $"{ItemInfo.Category(it)}  ·  {T(183)} {o.have} t";
                price.text = ItemInfo.Credits(o.unitPrice);
                price.EnableInClassList("row-price--expensive", o.unitPrice > Session.Credits);
            }
            if (tex != null) icon.style.backgroundImage = new StyleBackground(tex);
            sub.Add(subText);
            texts.Add(name);
            texts.Add(sub);
            e.Add(icon);
            e.Add(texts);
            if (price.text.Length > 0) e.Add(price);
            int index = rows.Count;
            e.RegisterCallback<ClickEvent>(ev =>
            {
                Select(index, true);
                if (ev.clickCount >= 2) Buy(1);   // like the hangar's double click on its rows
            });
            list.Add(e);
            rows.Add((o, e));
        }

        static Label Badge(string text, string cls)
        {
            var b = new Label(text) { pickingMode = PickingMode.Ignore };
            b.AddToClassList("row-badge");
            b.AddToClassList(cls);
            b.AddToClassList("gof-semibold");
            return b;
        }

        void Select(int index, bool sound)
        {
            if (index < 0 || index >= rows.Count || index == selected) return;
            selected = index;
            for (int i = 0; i < rows.Count; i++) rows[i].row.EnableInClassList("list-row--selected", i == selected);
            if (sound) playButton?.Invoke(true);
            ScrollToSelected();
            ShowDetails();
        }

        void ScrollToSelected()
        {
            if (selected >= 0 && selected < rows.Count) list.ScrollTo(rows[selected].row);
        }

        void ShowDetails()
        {
            detailStats.Clear();
            detailText.text = "";
            ItemInfo.FillModLines(detailMods, null);
            details.style.visibility = selected < 0 ? Visibility.Hidden : Visibility.Visible;
            if (selected < 0) return;
            var o = rows[selected].offer;
            if (o.kind == CapitalShips.OfferKind.Repair)
            {
                detailIcon.style.backgroundImage = new StyleBackground(ItemInfo.ShipIcon(Session.ShipIndex));
                detailName.text = X("resupplyRepair", "Repair hull and armor");
                detailSub.text = ItemInfo.ShipName(Session.ShipIndex);
                tradeBox.style.display = DisplayStyle.None;
                actionButton.style.display = DisplayStyle.Flex;
                actionButton.text = o.room > 0 ? $"{X("resupplyRepairButton", "REPAIR")}   {ItemInfo.Credits(o.unitPrice)}" : X("resupplyIntact", "INTACT");
                actionButton.EnableInClassList("detail-action--disabled", o.room <= 0 || o.unitPrice > Session.Credits);
                var hp = health != null ? health.Hp : null;
                if (hp != null)
                {
                    AddStat(X("statHull", "Hull"), $"{hp.hull} / {hp.maxHull}");
                    AddStat(X("statArmor", "Armor"), $"{hp.armor} / {hp.maxArmor}");
                    if (hp.maxShield > 0) AddStat(X("statShield", "Shield"), $"{Mathf.RoundToInt(hp.shield)} / {hp.maxShield}");
                }
                detailText.text = host != null && host.Spec.capital == SpawnSpec.CapitalVossk
                    ? X("resupplyRepairTextVossk", "The deck crew patch your hull and replace your armor. Shields recharge by themselves.")
                    : X("resupplyRepairText", "The deck crew patch your hull and replace your armor. Shields recharge by themselves.");
                ItemInfo.FillModLines(detailMods, Session.ShipMods);
                return;
            }
            var it = db.Item(o.item);
            detailIcon.style.backgroundImage = new StyleBackground(ItemInfo.ItemIcon(o.item));
            detailName.text = ItemInfo.TaggedItemName(o.item);
            detailSub.text = $"{ItemInfo.Category(it)}  ·  {T(133)} {it.techLevel}";
            actionButton.style.display = DisplayStyle.None;
            tradeBox.style.display = DisplayStyle.Flex;
            tradeLeft.text = o.stock < 0 ? "∞" : o.kind != CapitalShips.OfferKind.Ammo ? $"{o.stock} t" : o.stock.ToString();
            tradeRight.text = o.kind != CapitalShips.OfferKind.Ammo ? $"{o.have} t" : o.have.ToString();
            tradeNote.text = o.kind == CapitalShips.OfferKind.Ammo ? X("shopMounted", "MOUNTED").ToLowerInvariant() : "";
            tradePrice.text = ItemInfo.Credits(o.unitPrice);
            tradePrice.EnableInClassList("trade-price--expensive", o.unitPrice > Session.Credits);
            bool canBuy = o.room > 0 && o.unitPrice <= Session.Credits;
            buyArrow.EnableInClassList("trade-arrow--disabled", !canBuy);
            foreach (var (label, value) in ItemInfo.ItemStats(it)) AddStat(label, value);
            detailText.text = ItemInfo.ItemText(db, it, Shop.SystemOf(db, Session.StationIndex));
            detailScroll.scrollOffset = Vector2.zero;
        }

        void AddStat(string label, string value)
        {
            var row = El("stat-row");
            var l = Lbl("stat-label");
            l.text = label;
            var v = Lbl("stat-value", true);
            v.text = value;
            row.Add(l);
            row.Add(v);
            detailStats.Add(row);
        }

        // ---- buying ------------------------------------------------------------------------------------------

        /// <summary>'units' of the selected row (the repair: all of it); the HUD message; the list again.</summary>
        void Buy(int units)
        {
            if (selected < 0 || selected >= rows.Count) return;
            var o = rows[selected].offer;
            string msg = CapitalShips.Buy(db, health, store, o, units, out int bought);
            // A held BUY: each unit's sound pitched up as the room fills (HeldTrade.Tick); a single press or Enter as before.
            if (bought > 0 && (heldPointer >= 0 || heldKey)) HeldTrade.Tick(CombatAudio.Load()?.shopBuy, holdDone, holdTotal);
            else if (bought > 0) playUi?.Invoke(CombatAudio.Load()?.shopBuy);
            else ReleaseArrow();   // nothing more to buy: a held arrow stops
            if (!string.IsNullOrEmpty(msg) && (bought == 0 || heldPointer < 0 && !heldKey)) message?.Invoke(msg);
            Rebuild();
        }

        /// <summary>A press on BUY: one unit now, and what there is room for counted for the hold (HeldTrade).</summary>
        void BeginHold()
        {
            heldMs = 0f;
            holdDone = 1;
            holdTotal = selected >= 0 && selected < rows.Count ? Mathf.Max(1, rows[selected].offer.room) : 1;
            Buy(1);
        }

        void ReleaseArrow()
        {
            if (heldPointer >= 0 && buyArrow.HasPointerCapture(heldPointer)) buyArrow.ReleasePointer(heldPointer);
            heldPointer = -1;
            buyArrow.RemoveFromClassList("trade-arrow--pressed");
            HeldTrade.SetFill(buyFill, 0f);
        }

        // ---- per frame ---------------------------------------------------------------------------------------

        /// <summary>The window's keys and the held repeats (real time: the game is paused under it).</summary>
        public void Tick()
        {
            if (!IsOpen) return;
            float dtMs = Time.unscaledDeltaTime * 1000f;
            var kb = GoF2Remake.Multiplayer.NetChat.Keys;
            var pad = Gamepad.current;
            if (Time.frameCount - openedFrame < 2) return;   // the press that docked may still read as this frame's

            if ((kb != null && (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame))
                || (pad != null && (pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame)))
            {
                playButton?.Invoke(false);
                Close();
                return;
            }

            // Up / down: a press moves at once, held it repeats.
            int dir = 0;
            if (kb != null && (kb.upArrowKey.isPressed || kb.wKey.isPressed)) dir = -1;
            if (kb != null && (kb.downArrowKey.isPressed || kb.sKey.isPressed)) dir = 1;
            if (pad != null)
            {
                float y = pad.dpad.ReadValue().y + pad.leftStick.ReadValue().y;
                if (y > 0.5f) dir = -1; else if (y < -0.5f) dir = 1;
            }
            if (dir != moveDirection) { moveDirection = dir; moveMs = 0f; if (dir != 0) Move(dir, true); }
            else if (dir != 0)
            {
                moveMs += dtMs;
                if (moveMs >= MoveAfterMs) { moveMs -= MoveEveryMs; Move(dir); }
            }

            bool rightHeld = (kb != null && (kb.rightArrowKey.isPressed || kb.dKey.isPressed))
                             || (pad != null && (pad.dpad.right.isPressed || pad.buttonSouth.isPressed));
            bool rightPressed = (kb != null && (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame))
                                || (pad != null && (pad.dpad.right.wasPressedThisFrame || pad.buttonSouth.wasPressedThisFrame));
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) { Buy(1); return; }
            if (rightPressed) { heldKey = true; BeginHold(); return; }
            if (!rightHeld) heldKey = false;
            if ((heldKey || heldPointer >= 0) && selected >= 0 && rows[selected].offer.kind != CapitalShips.OfferKind.Repair)
            {
                // Remake (HeldTrade): unit by unit, faster and faster, all there was room for in two seconds while BUY fills.
                int units = HeldTrade.Step(ref heldMs, ref holdDone, holdTotal, dtMs);
                HeldTrade.SetFill(buyFill, HeldTrade.Progress(holdDone, holdTotal));
                if (units > 0) Buy(units);
            }
        }

        /// <summary>Like the hangar's list (HangarWindow.MoveSelection): a fresh press wraps round the ends, a held
        /// key's repeat stops there.</summary>
        void Move(int dir, bool wrap = false)
        {
            if (rows.Count == 0) return;
            int next = selected + dir;
            if (wrap && selected >= 0 && (next < 0 || next >= rows.Count)) next = next < 0 ? rows.Count - 1 : 0;
            Select(Mathf.Clamp(next, 0, rows.Count - 1), true);
        }
    }
}
