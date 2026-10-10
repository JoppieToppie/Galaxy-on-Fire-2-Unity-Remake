// ItemInfoWindow.cs
// The full-screen item / ship details (ListItemWindow: set 0x159468, draw 0x15b620, update 0x15bee8, render 0x15c054,
// OnTouch* 0x15c100..; Reference/research/shop.md 2.4). Opened by the hangar list's info button on the selected row
// (HangarWindow::OnTouchEnd case 0: sound 97 Button_Info) and by the lounge's "Let me see it" (776, no price).
//   header 390 "Info", help 643; a 1300 x 800 box (full screen on phones) over the dimmed hangar
//   left   the name box (icon + 1274 + item / 913 + ship), the stat rows (label left, value right)
//   ships  every row but the price with a comparison arrow against the current ship: 0x512 worse, 0x513 better, 0x514
//          equal; the right half the 3D ship (Globals::getShipGroup(idx, race, true): the player variant) in a 634 x 318
//          box over the floor glow 0x50b (drawn once and mirrored), its own camera (0.92 rad, (1362, 1690, -5257) at
//          1920 x 1080, turned (pi/8, pi, -0.1)), light (-5, 1, -5), then 280 "Description" and 977 + ship
//   items  280 "Description", 1041 + item and the known price range (ItemInfo.ItemText); 132 Price only with showPrice
//   turning yaw only, 120 px = 1 rad, starting at 260 px; a drag starts on the right half above the 3D box's bottom; a
//          release over 3 px keeps turning x0.9 per frame, stops at 1 px
//   close  the footer's Back (170)
// Remake input: mouse / touch drag, A / D or the right stick turn; Esc / B back. Plain class owned by StationMenu.

using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public class ItemInfoWindow
    {
        const float FrameMs = 1000f / 30f;
        static readonly Vector3 StagePosition = new Vector3(0f, -20000f, 0f);

        readonly StationMenu menu;
        readonly VisualElement backdrop, icon, stats;
        readonly ShipPreview preview = new ShipPreview(StagePosition, 1268, 636, 0.9f);
        readonly Label header, help, nameLabel, subLabel, descTitle, text;
        readonly ScrollView textScroll;
        readonly VisualElement modLines = ItemInfo.NewModBlock();

        public bool IsOpen { get; private set; }

        static string T(int id) => Localization.Get(id);
        static Texture2D Hud(string n) => Resources.Load<Texture2D>("GoF2Hud/" + n);

        public ItemInfoWindow(StationMenu owner, VisualElement root)
        {
            menu = owner;
            backdrop = new VisualElement();
            backdrop.AddToClassList("info-backdrop");
            var box = new VisualElement();
            box.AddToClassList("info-box");
            header = new Label { pickingMode = PickingMode.Ignore };
            header.AddToClassList("info-header");
            header.AddToClassList("gof-semibold");
            help = new Label { pickingMode = PickingMode.Ignore };
            help.AddToClassList("info-help");
            var columns = new VisualElement();
            columns.AddToClassList("info-columns");

            var left = new VisualElement();
            left.AddToClassList("info-left");
            var nameBox = new VisualElement();
            nameBox.AddToClassList("info-name-box");
            icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("info-icon");
            var names = new VisualElement { pickingMode = PickingMode.Ignore };
            nameLabel = new Label { pickingMode = PickingMode.Ignore };
            nameLabel.AddToClassList("info-name");
            nameLabel.AddToClassList("gof-semibold");
            subLabel = new Label { pickingMode = PickingMode.Ignore };
            subLabel.AddToClassList("info-sub");
            names.Add(nameLabel);
            names.Add(subLabel);
            nameBox.Add(icon);
            nameBox.Add(names);
            stats = new ScrollView();
            stats.AddToClassList("info-stats");
            left.Add(nameBox);
            left.Add(stats);

            var right = new VisualElement();
            right.AddToClassList("info-right");
            descTitle = new Label { pickingMode = PickingMode.Ignore };
            descTitle.AddToClassList("info-desc-title");
            descTitle.AddToClassList("gof-semibold");
            textScroll = new ScrollView();
            textScroll.AddToClassList("info-text-scroll");
            text = new Label();
            text.AddToClassList("info-text");
            textScroll.Add(text);
            textScroll.Add(modLines);
            preview.Element.AddToClassList("info-preview");
            right.Add(preview.Element);
            right.Add(descTitle);
            right.Add(textScroll);
            columns.Add(left);
            columns.Add(right);

            var footer = new VisualElement();
            footer.AddToClassList("info-footer");
            var back = new Button(() => { menu.PlayRelease(); Close(); }) { focusable = false };
            back.AddToClassList("info-back");
            back.AddToClassList("gof-semibold");
            back.RegisterCallback<PointerDownEvent>(_ => menu.PlayPush(), TrickleDown.TrickleDown);
            footer.Add(back);
            box.Add(header);
            box.Add(help);
            box.Add(columns);
            box.Add(footer);
            backdrop.Add(box);
            root.Add(backdrop);

            back.text = T(170).ToUpperInvariant();
        }

        // ---- content --------------------------------------------------------------------------------------

        void Begin()
        {
            header.text = T(390).ToUpperInvariant();
            help.text = T(643);
            descTitle.text = T(280).ToUpperInvariant();
            stats.Clear();
            ItemInfo.FillModLines(modLines, null);
            textScroll.scrollOffset = Vector2.zero;
            IsOpen = true;
            backdrop.AddToClassList("info-backdrop--shown");
        }

        void Row(string label, string value, int compare = 2)
        {
            var r = new VisualElement();
            r.AddToClassList("info-row");
            var l = new Label(label) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("info-row-label");
            var v = new Label(value) { pickingMode = PickingMode.Ignore };
            v.AddToClassList("info-row-value");
            v.AddToClassList("gof-semibold");
            r.Add(l);
            r.Add(v);
            if (compare != 2)
            {
                var a = new VisualElement { pickingMode = PickingMode.Ignore };
                a.AddToClassList("info-row-arrow");
                var tex = Hud(compare < 0 ? "compare_worse" : compare > 0 ? "compare_better" : "compare_equal");
                if (tex != null) a.style.backgroundImage = new StyleBackground(tex);
                r.Add(a);
            }
            stats.Add(r);
        }

        /// <summary>ListItemWindow::set(item): the attribute table, the price (showPrice) and the description with the
        /// known price range.</summary>
        public void ShowItem(Database db, int item, int currentSystem, bool showPrice, int price)
        {
            var it = db.Item(item);
            if (it == null) return;
            Begin();
            preview.Hide();
            icon.style.backgroundImage = new StyleBackground(ItemInfo.ItemIcon(item));
            nameLabel.text = ItemInfo.ItemName(item);
            subLabel.text = $"{ItemInfo.Category(it)}  ·  {T(133)} {it.techLevel}";
            foreach (var (label, value) in ItemInfo.ItemStats(it)) Row(label, value);
            if (showPrice && price > 0) Row(T(132), ItemInfo.Credits(price));
            text.text = ItemInfo.ItemText(db, it, currentSystem);
        }

        /// <summary>ListItemWindow::set(ship): the ship rows with comparison arrows, the 3D model, the description.</summary>
        public void ShowShip(Database db, int ship, int price, bool showPrice = true)
        {
            var s = db.Ship(ship);
            if (s == null) return;
            Begin();
            icon.style.backgroundImage = new StyleBackground(ItemInfo.ShipIcon(ship));
            nameLabel.text = ItemInfo.ShipName(ship);
            subLabel.text = ItemInfo.ShipRaceText(ship);
            var cur = db.Ship(Session.ShipIndex);
            int Cmp(float v, float c) => cur == null ? 2 : v < c ? -1 : v > c ? 1 : 0;
            bool mine = ship == Session.ShipIndex;
            int Lv(int mod) => mine ? Session.ModLevel(mod) : 0;
            string Plus(int mod) => Lv(mod) > 0 ? " (+)" : "";
            Row(T(165), (s.armor + 40 * Lv(0)) + Plus(0), Cmp(s.armor, cur?.armor ?? 0));
            Row(T(166), $"{s.cargo + 30 * Lv(1)} t" + Plus(1), Cmp(s.cargo, cur?.cargo ?? 0));
            Row(T(265), s.slots.primary.ToString(), Cmp(s.slots.primary, cur?.slots.primary ?? 0));
            Row(T(266), s.slots.secondary.ToString(), Cmp(s.slots.secondary, cur?.slots.secondary ?? 0));
            Row(T(267), s.slots.turret.ToString(), Cmp(s.slots.turret, cur?.slots.turret ?? 0));
            Row(T(269), (s.slots.equipment + Lv(2)) + Plus(2), Cmp(s.slots.equipment, cur?.slots.equipment ?? 0));
            Row(T(164), (Mathf.RoundToInt(s.handling) + 20 * Lv(3)) + Plus(3), Cmp(s.handling, cur?.handling ?? 0));
            if (showPrice) Row(T(132), ItemInfo.Credits(price));
            text.text = GameNames.ShipDescription(ship);
            if (mine) ItemInfo.FillModLines(modLines, Session.ShipMods);   // the Kaamo Club mods under the description
            preview.Show(db, ship);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            backdrop.RemoveFromClassList("info-backdrop--shown");
            preview.Hide();
        }

        /// <summary>Every frame while open: the keys / stick turning (ShipPreview turns the fling itself).</summary>
        public void Tick()
        {
            if (!IsOpen || !preview.Shown) return;
            float frames = Time.unscaledDeltaTime * 1000f / FrameMs;
            var kb = GoF2Remake.Multiplayer.NetChat.Keys;   // null while a multiplayer chat line is typed
            var pad = Gamepad.current;
            float turn = 0f;
            if (kb != null) turn += (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
            if (pad != null) turn += pad.rightStick.ReadValue().x + pad.leftStick.ReadValue().x;
            preview.Turn(-Mathf.Clamp(turn, -1f, 1f) * 8f * frames);   // like dragging that way
        }
    }
}
