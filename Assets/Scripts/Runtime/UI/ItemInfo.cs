// ItemInfo.cs
// What the shop shows about items and ships (ListItemWindow::set 0x159468, Layout::formatCredits 0xe5bd4):
// icons (Resources/GoF2Icons, made by GoF2 > Build > Item Icons), credits formatting, the stat rows of an item or a ship,
// and the description with the known price range. Labels, units and formulas: Reference/research/shop.md 2.4.

using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ItemInfo
    {
        static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();

        /// <summary>The item's shop icon: a mod's own ("icon" in items.json), else its base item's (Modding.ModContent.ItemLook).</summary>
        public static Texture2D ItemIcon(int item)
        {
            int bpShip = Modding.ModBlueprints.ShipOf(item);   // a mod's ship blueprint: its ship's icon
            if (bpShip >= 0) return ShipIcon(bpShip);
            var own = Modding.ModContent.ItemIcon(item);   // a mod's own icon (items.json "icon")
            return own != null ? own : Icon($"item_{Modding.ModContent.ItemLook(item):000}");
        }
        /// <summary>The ship's shop icon; a mod's ship its own ("icon" in ships.json), else the Phantom's.</summary>
        public static Texture2D ShipIcon(int ship) =>
            Modding.ModContent.IsModShip(ship) ? Modding.ModShips.Icon(ship) ?? Icon("ship_010") : Icon($"ship_{ship:000}");

        /// <summary>The icon of a hull with no shop image of its own (the Debug Ships tab's freighters and capital ships, by
        /// World.PlayerHull's key; "GoF2 > Build > Hull Icons"), null without one.</summary>
        public static Texture2D HullIcon(string key) => Icon("hull_" + key);

        static Texture2D Icon(string name)
        {
            if (!icons.TryGetValue(name, out var t)) icons[name] = t = Resources.Load<Texture2D>("GoF2Icons/" + name);
            return t;
        }

        /// <summary>Layout::formatCredits: thousands separated, "$" after ("," in English, "." in the other languages).</summary>
        public static string Credits(int value)
        {
            string sep = Localization.Language == "en" ? "," : ".";
            string digits = Mathf.Abs(value).ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", sep);
            return (value < 0 ? "-" : "") + digits + "$";
        }

        public static string ItemName(int item) => GameNames.Item(item);
        public static string ShipName(int ship) => GameNames.Ship(ship);

        /// <summary>The ship's race under its name ("" = none, Shop.ShipMakerRace): the four races, Grey and pirates.</summary>
        public static string ShipRaceText(int ship)
        {
            int race = Shop.ShipMakerRace(ship);
            return race >= 0 && (race <= 3 || race == 7 || race == 8) ? Localization.Get(406 + race) : "";
        }
        public static string Category(ItemData it) => Modding.ModBlueprints.ShipOf(it.index) >= 0
            ? Localization.Extra("bpShipBlueprint", "Ship blueprint")   // a mod's ship blueprint (its hidden item)
            : Localization.Get(221 + it.categoryId);

        /// <summary>Attributes the details never list (index, type, price systems, occurrence, prices, Vossk flag, home station).</summary>
        static readonly HashSet<int> Hidden = new HashSet<int> { 0, 1, 4, 5, 6, 7, 8, 60, 61, 100, 102, 103 };

        /// <summary>ListItemWindow::set, items: label (DAT_0025875c) and value + unit (LISTITEMWINDOW_UNITS) per attribute.</summary>
        public static List<(string label, string value)> ItemStats(ItemData it)
        {
            var rows = new List<(string, string)>();
            string T(int id) => Localization.Get(id);
            string YesNo(int v) => T(v != 0 ? 134 : 135);
            bool beam = it.index >= 9 && it.index <= 11 || it.index == 228;
            for (int n = 0; n < it.attrKeys.Length; n++)
            {
                int k = it.attrKeys[n], v = it.attrValues[n];
                if (Hidden.Contains(k) || (k == 13 && beam)) continue;
                switch (k)
                {
                    case 2: rows.Add((T(320), Category(it))); break;
                    case 3: rows.Add((T(133), v.ToString())); break;
                    case 9: rows.Add((T(146), v.ToString())); break;
                    case 10: rows.Add((T(138), v.ToString())); break;
                    case 11:
                        rows.Add((T(149), $"{v} ms"));
                        if ((it.TypeId == 0 || it.TypeId == 2 || it.categoryId == 39) && v > 0)
                        {
                            int dmg = it.HasAttr(9) ? it.Attr(9) : it.Attr(10);
                            rows.Add((T(188), (dmg / (float)v * 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)));
                        }
                        break;
                    case 12:
                        if (it.categoryId == 11) rows.Add((T(157), $"{v / 1000} s"));   // mines: how long they stay
                        else
                        {
                            int range = (int)(v / 3600f * it.Attr(13) * 250f);
                            int shown = range - range % 100 + (range % 100 == 50 ? 50 : 0);
                            rows.Add((T(151), $"{shown} m"));
                        }
                        break;
                    case 13: rows.Add((T(156), $"{v * 250} km/h")); break;
                    case 14: rows.Add((T(139), $"{v} m")); break;
                    case 15: rows.Add((T(158), YesNo(v))); break;
                    case 16: rows.Add((T(153), YesNo(v))); break;
                    case 30: rows.Add((T(142), YesNo(v))); break;
                    case 31: rows.Add((T(143), YesNo(v))); break;
                    case 57: rows.Add((T(162), YesNo(v))); break;
                    case 58: rows.Add((T(159), YesNo(v))); break;
                    case 23: rows.Add((T(153), v == 0 ? T(135) : v == 1 ? T(154) : T(155))); break;
                    case 17: rows.Add((T(164), v.ToString())); break;
                    case 18: rows.Add((T(148), v.ToString())); break;
                    case 20: rows.Add((T(165), v.ToString())); break;
                    case 19: case 26: case 36: case 37: case 43: rows.Add((T(149), $"{v} ms")); break;
                    case 21: rows.Add((T(140), $"{v} %")); break;
                    case 22: case 25: case 28: case 46: case 54: case 56: rows.Add((T(157), $"{v} %")); break;
                    case 24: case 29: rows.Add((T(141), $"{v} ms")); break;
                    case 27: rows.Add((T(152), $"{v} ms")); break;
                    case 32: if (it.Attr(100) != 1) rows.Add((T(164), $"{v} %")); break;   // a mining beam has no drill handling
                    case 101: if (it.Attr(100) == 1) rows.Add((T(151), $"{v / 20} m")); break;   // remake: the mining beam's reach
                    case 33: rows.Add((T(144), $"{v} %")); break;
                    case 34: rows.Add((T(145), v.ToString())); break;
                    case 48: rows.Add((T(145), $"{v} t")); break;
                    case 35: case 41: case 42: rows.Add((T(157), $"{v} ms")); break;
                    case 38: rows.Add((T(150), v.ToString())); break;
                    case 39: rows.Add((T(147), $"+{v} %")); break;
                    case 40: rows.Add((T(146), $"+{v} %")); break;
                    case 44: case 47: case 51: case 53: rows.Add((T(151), $"{v} m")); break;
                    case 49: rows.Add((T(156), $"{v * 50} km/h")); break;
                    case 50: rows.Add((T(139), $"{v} %")); break;
                    case 52: rows.Add((T(160), $"{v} %")); break;
                    case 55: rows.Add((T(161), v.ToString())); break;
                    case 59: rows.Add((T(163), $"{v} t")); break;
                }
            }
            return rows;
        }

        /// <summary>ListItemWindow::set, ships: armor, cargo hold, slots per type, handling, price.</summary>
        /// <summary>'mods': the hull's Kaamo Club mods (the player's own ship, a traded-in or stored hull): their values added
        /// with "(+)", like the item window's (ListItemWindow::set).</summary>
        public static List<(string label, string value)> ShipStats(ShipData s, int price, IEnumerable<int> mods = null)
        {
            var rows = new List<(string, string)>();
            foreach (var (label, value, _) in ShipStatsCompared(s, price, mods, null, null)) rows.Add((label, value));
            return rows;
        }

        /// <summary>ShipStats with each row compared to 'current' (with its mods 'currentMods'), as ListItemWindow::set's
        /// arrows 0x512 / 0x513 / 0x514: -1 worse, 1 better, 0 equal, 2 none (no current ship; the price). Remake (#62): the
        /// hangar's details panel shows them too (the original only in its item window), with the Kaamo upgrades counted on
        /// both sides; a slot row shows when either ship has such slots.</summary>
        public static List<(string label, string value, int compare)> ShipStatsCompared(ShipData s, int price, IEnumerable<int> mods,
            ShipData current, IEnumerable<int> currentMods)
        {
            string T(int id) => Localization.Get(id);
            int Lv(int mod) => Session.ModLevel(mods, mod);
            int CurLv(int mod) => Session.ModLevel(currentMods, mod);
            string Plus(int mod) => Lv(mod) > 0 ? " (+)" : "";
            int Cmp(float v, float c) => current == null ? 2 : v < c ? -1 : v > c ? 1 : 0;
            var rows = new List<(string, string, int)>();
            int armor = s.armor + 40 * Lv(0), cargo = s.cargo + 30 * Lv(1), equipment = s.slots.equipment + Lv(2);
            int handling = Mathf.RoundToInt(s.handling) + 20 * Lv(3);
            rows.Add((T(165), armor + Plus(0), current == null ? 2 : Cmp(armor, current.armor + 40 * CurLv(0))));
            rows.Add((T(166), $"{cargo} t" + Plus(1), current == null ? 2 : Cmp(cargo, current.cargo + 30 * CurLv(1))));
            void Slots(int id, int n, int cur, string plus)
            {
                if (n > 0 || current != null && cur > 0) rows.Add((T(id), n + plus, Cmp(n, cur)));
            }
            Slots(265, s.slots.primary, current?.slots.primary ?? 0, "");
            Slots(266, s.slots.secondary, current?.slots.secondary ?? 0, "");
            Slots(267, s.slots.turret, current?.slots.turret ?? 0, "");
            Slots(269, equipment, current != null ? current.slots.equipment + CurLv(2) : 0, Plus(2));
            rows.Add((T(164), handling + Plus(3), current == null ? 2 : Cmp(handling, Mathf.RoundToInt(current.handling) + 20 * CurLv(3))));
            rows.Add((T(132), Credits(price), 2));
            return rows;
        }

        /// <summary>Item description (1041 + index) and "Known price range:" with the lowest and highest price seen.</summary>
        public static string ItemText(Database db, ItemData it, int currentSystem)
        {
            string text = GameNames.ItemDescription(it.index);
            bool lo = Session.LowestKnownPrice.TryGetValue(it.index, out var low);
            bool hi = Session.HighestKnownPrice.TryGetValue(it.index, out var high);
            if (!lo && !hi) return text;
            string Where(int system) => system == currentSystem ? Localization.Get(215) : db.Systems.Find(s => s.index == system)?.name ?? "";
            text += $"\n\n{Localization.Get(214)}";
            if (lo) text += $"\n-> {Credits(low.price)} ({Where(low.system)})";
            if (hi) text += $"\n-> {Credits(high.price)} ({Where(high.system)})";
            return text;
        }

        /// <summary>What one level of a Kaamo Club mod adds: 0 +40 hull, 1 +30 t cargo, 2 +1 equipment slot, 3 handling +20.</summary>
        public static int ModAmount(int mod) => mod switch { 0 => 40, 1 => 30, 2 => 1, _ => 20 };

        /// <summary>Remake (players' suggestion): a Kaamo Club mechanic's ship mod as one line for the ship's details, with
        /// what its 'level' (how many times it is fitted, Settings.KaamoStacking) adds in all.</summary>
        public static string ModLine(int mod, int level = 1)
        {
            string line = string.Format(mod switch
            {
                0 => Localization.Extra("kaamoModArmor", "Kaamo Club armor upgrade applied (+{0})"),
                1 => Localization.Extra("kaamoModCargo", "Kaamo Club cargo upgrade applied (+{0} t)"),
                2 => Localization.Extra("kaamoModSlot", "Kaamo Club extra equipment slot upgrade applied (+{0})"),
                _ => Localization.Extra("kaamoModHandling", "Kaamo Club handling upgrade applied (+{0})"),
            }, ModAmount(mod) * level);
            return level > 1 ? line + "  ·  " + string.Format(Localization.Extra("kaamoModLevel", "level {0}"), level) : line;
        }

        /// <summary>Fills 'into' (cleared, hidden without mods) with a line per mod, in the mods' order 0..3: the block at the
        /// bottom of a ship's details (hangar, carrier, item window).</summary>
        public static void FillModLines(UnityEngine.UIElements.VisualElement into, IEnumerable<int> mods)
        {
            into.Clear();
            var sorted = new List<int>();
            if (mods != null) foreach (int m in mods) if (m >= 0 && !sorted.Contains(m)) sorted.Add(m);
            sorted.Sort();
            into.style.display = sorted.Count > 0 ? UnityEngine.UIElements.DisplayStyle.Flex : UnityEngine.UIElements.DisplayStyle.None;
            foreach (int m in sorted)
            {
                var line = new UnityEngine.UIElements.Label("+ " + ModLine(m, Session.ModLevel(mods, m))) { pickingMode = UnityEngine.UIElements.PickingMode.Ignore };
                line.AddToClassList("kaamo-mod-line");
                into.Add(line);
            }
        }

        /// <summary>Remake (Settings.KaamoKeepsEquipment): a stored hull's mounted items after its mod lines, "Mounted:" and one
        /// line each ("name (amount)" for a secondary's ammo); the block shows when there is any.</summary>
        public static void AddEquipmentLines(UnityEngine.UIElements.VisualElement into, IEnumerable<ItemStack> equipment)
        {
            if (equipment == null) return;
            bool any = false;
            foreach (var e in equipment)
            {
                if (e == null) continue;
                if (!any)
                {
                    var head = new UnityEngine.UIElements.Label(Localization.Extra("kaamoGearHeader", "Mounted on this ship:")) { pickingMode = UnityEngine.UIElements.PickingMode.Ignore };
                    head.AddToClassList("kaamo-mod-line");
                    into.Add(head);
                    any = true;
                }
                var line = new UnityEngine.UIElements.Label("  " + ItemName(e.item) + (e.amount > 1 ? $" ({e.amount})" : "")) { pickingMode = UnityEngine.UIElements.PickingMode.Ignore };
                line.AddToClassList("kaamo-mod-line");
                into.Add(line);
            }
            if (any) into.style.display = UnityEngine.UIElements.DisplayStyle.Flex;
        }

        /// <summary>The block FillModLines fills (styles .kaamo-mods in GoF2Common.uss).</summary>
        public static UnityEngine.UIElements.VisualElement NewModBlock()
        {
            var block = new UnityEngine.UIElements.VisualElement { pickingMode = UnityEngine.UIElements.PickingMode.Ignore };
            block.AddToClassList("kaamo-mods");
            block.style.display = UnityEngine.UIElements.DisplayStyle.None;
            return block;
        }
    }
}
