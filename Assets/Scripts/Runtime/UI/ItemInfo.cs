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

        public static Texture2D ItemIcon(int item) => Icon($"item_{item:000}");
        public static Texture2D ShipIcon(int ship) => Icon($"ship_{ship:000}");

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

        public static string ItemName(int item) => Localization.Get(1274 + item);
        public static string ShipName(int ship) => CustomShips.ShipName(ship);   // 913 + ship; the custom ships their own
        public static string ShipDescription(int ship) => CustomShips.ShipDescription(ship);   // 977 + ship

        /// <summary>The ship's race under its name ("" = none, Shop.ShipMakerRace): the four races, Grey and pirates.</summary>
        public static string ShipRaceText(int ship)
        {
            int race = Shop.ShipMakerRace(ship);
            return race >= 0 && (race <= 3 || race == 7 || race == 8) ? Localization.Get(406 + race) : "";
        }
        public static string Category(ItemData it) => Localization.Get(221 + it.categoryId);

        /// <summary>Attributes the details never list (index, type, price systems, occurrence, prices, Vossk flag, home station).</summary>
        static readonly HashSet<int> Hidden = new HashSet<int> { 0, 1, 4, 5, 6, 7, 8, 60, 61 };

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
                    case 32: rows.Add((T(164), $"{v} %")); break;
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
        public static List<(string label, string value)> ShipStats(ShipData s, int price)
        {
            string T(int id) => Localization.Get(id);
            var rows = new List<(string, string)>
            {
                (T(165), s.armor.ToString()),
                (T(166), $"{s.cargo} t"),
            };
            if (s.slots.primary > 0) rows.Add((T(265), s.slots.primary.ToString()));
            if (s.slots.secondary > 0) rows.Add((T(266), s.slots.secondary.ToString()));
            if (s.slots.turret > 0) rows.Add((T(267), s.slots.turret.ToString()));
            if (s.slots.equipment > 0) rows.Add((T(269), s.slots.equipment.ToString()));
            rows.Add((T(164), Mathf.RoundToInt(s.handling).ToString()));
            rows.Add((T(132), Credits(price)));
            return rows;
        }

        /// <summary>Item description (1041 + index) and "Known price range:" with the lowest and highest price seen.</summary>
        public static string ItemText(Database db, ItemData it, int currentSystem)
        {
            string text = Localization.Get(1041 + it.index);
            bool lo = Session.LowestKnownPrice.TryGetValue(it.index, out var low);
            bool hi = Session.HighestKnownPrice.TryGetValue(it.index, out var high);
            if (!lo && !hi) return text;
            string Where(int system) => system == currentSystem ? Localization.Get(215) : db.Systems.Find(s => s.index == system)?.name ?? "";
            text += $"\n\n{Localization.Get(214)}";
            if (lo) text += $"\n-> {Credits(low.price)} ({Where(low.system)})";
            if (hi) text += $"\n-> {Credits(high.price)} ({Where(high.system)})";
            return text;
        }
    }
}
