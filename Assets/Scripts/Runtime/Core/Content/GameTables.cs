// GameTables.cs
// Feature: docs/notes/player-assets.md "Data tables": the data tables (Resources/GoF2Data) that come from Galaxy on Fire 2's
// own .bin files, made again from the player's copy: a port of Reference/tools/asset_conversion/parse_bins.py (agents,
// wanted, names, ticker, ship / station parts, collisions, weapon mounts, docking points), Reference/tools/parse.py +
// export.py (items, ships, systems, stations, with the project's items.json shape: "statList", "blueprint": [] for none)
// and shop/build_item_attributes.py (item_attributes). Plain C#, no Unity types. Their text (names, descriptions) comes
// from the English .lang table, as the scripts take it.
//
// Not here: the recovered fact tables and the remake's own (they ship), the other edition's economy (economy_default /
// weapons_hd_default from iOS' items / ships / weapons_hd.bin: a gap pack), the remake's text corrections (an overlay).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace GoF2Remake.Data
{
#if UNITY_5_3_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]   // (only constant tables: nothing to reset)
#endif
    public static class GameTables
    {
        /// <summary>The tables (file name without .json -> its JSON) from the copy's data files. 'read' gives a file's bytes by
        /// its path below the data folder ("bin/agents.bin", "assets/valkyrie/bin/v_collisions.bin"), null when the copy lacks
        /// it; 'files' lists those paths (for the per-pack tables); 'english' is the English .lang table. A table whose file
        /// the copy lacks is left out.</summary>
        public static Dictionary<string, string> Build(Func<string, byte[]> read, IEnumerable<string> files, IList<string> english)
        {
            var o = new Dictionary<string, string>();
            var L = english;
            string Txt(int i) => i >= 0 && i < L.Count ? L[i] : null;
            string Race(int r) => r >= 0 && r < 10 ? Txt(406 + r) : null;
            void Put(string name, object v) => o[name] = TableJson.Write(v);
            var all = files.ToList();

            var b = read("bin/agents.bin");
            if (b != null)
            {
                var r = new R(b); var list = new List<object>();
                for (int k = 0; k < 27; k++)
                {
                    string name = r.Strb(); var v = r.Ibs(9);
                    var a = new JsonObj { { "index", v[0] }, { "name", name }, { "station", v[1] }, { "system", v[2] }, { "race", v[3] },
                        { "raceName", Race(v[3]) }, { "male", v[4] == 1 }, { "sellItemSystem", v[5] }, { "sellBlueprint", v[6] },
                        { "sellMod", v[7] }, { "sellItemPrice", v[8] } };
                    if (r.Ib() > 0) a.Add("portraitParts", r.Cs(5));
                    list.Add(a);
                }
                Put("agents", list);
            }
            b = read("bin/wanted.bin");
            if (b != null)
            {
                var r = new R(b); var list = new List<object>();
                for (int k = 0; k < 25; k++)
                {
                    string name = r.Strb(); var v = r.Ibs(13);
                    var w = new JsonObj { { "index", v[0] }, { "name", name }, { "board", v[1] }, { "race", v[2] }, { "raceName", Race(v[2]) },
                        { "male", v[3] == 1 }, { "ship", v[4] }, { "weapon", v[5] }, { "hitpoints", v[6] }, { "loot", v[7] },
                        { "lootAmount", v[8] }, { "reward", v[9] }, { "requiredBounties", v[10] }, { "requiredMission", v[11] },
                        { "numWingmen", v[12] } };
                    if (r.Ib() > 0) w.Add("portraitParts", r.Cs(5));
                    list.Add(w);
                }
                Put("wanted", list);
            }
            var names = new JsonObj();
            foreach (var f in all.Where(f => f.StartsWith("bin/names_") && f.EndsWith(".bin")).OrderBy(f => f, StringComparer.Ordinal))
            {
                var r = new R(read(f)); int n = r.Ib(); var l = new List<object>();
                for (int k = 0; k < n; k++) l.Add(r.Strb());
                names.Add(f.Substring(10, f.Length - 14), l);
            }
            if (names.Count > 0) Put("names", names);
            b = read("bin/ticker.bin");
            if (b != null)
            {
                var r = new R(b); var list = new List<object>();
                for (int k = 0; k < 59; k++)
                {
                    int flag = r.Ib(); var c = new List<object> { r.Il() != 0, r.Il() != 0, r.Il() != 0, r.Il() != 0 }; int p1 = r.Ib(), p2 = r.Ib();
                    list.Add(new JsonObj { { "index", k }, { "flag", flag != 0 }, { "conditions", c }, { "param1", p1 }, { "param2", p2 } });
                }
                Put("ticker", list);
            }
            b = read("bin/shipparts.bin");
            if (b != null)
            {
                var r = new R(b); var list = new List<object>();
                while (r.Left > 0)
                {
                    int ship = r.C(), n = r.C(); var parts = new List<object>();
                    for (int k = 0; k < n; k++)
                    {
                        int t = r.Sb(); var pos = new List<object> { r.Ib(), r.Ib(), r.Ib() }; var rest = new List<object>();
                        for (int q = 0; q < 6; q++) rest.Add(r.Sb());
                        parts.Add(new JsonObj { { "type", t }, { "position", pos }, { "values", rest } });
                    }
                    list.Add(new JsonObj { { "ship", ship - 1 }, { "shipName", ship > 0 && ship <= 64 ? Txt(913 + ship - 1) : null }, { "parts", parts } });
                }
                Put("shipparts", list);
            }
            b = read("bin/stationparts.bin");
            if (b != null)
            {
                var r = new R(b); var list = new List<object>();
                while (r.Left > 0)
                {
                    int sid = r.C(), x = r.Sb(), n = r.C(); var parts = new List<object>();
                    for (int k = 0; k < n; k++)
                    {
                        int t = r.Sb(); var pos = new List<object> { r.Ib(), r.Ib(), r.Ib() };
                        var rest = new List<object> { r.Sb(), r.Sb(), r.Sb() };
                        parts.Add(new JsonObj { { "type", t }, { "position", pos }, { "values", rest } });
                    }
                    list.Add(new JsonObj { { "station", sid - 1 }, { "headerValue", x }, { "parts", parts } });
                }
                Put("stationparts", list);
            }
            foreach (var (name, path) in new[] { ("collision", "bin/collision.bin"), ("wreck_collisions", "bin/wreck_collisions.bin"),
                         ("static_collisions", "bin/static_collisions.bin"), ("collision_test", "bin/collision_test.bin"),
                         ("v_collisions", "assets/valkyrie/bin/v_collisions.bin") })
            {
                b = read(path);
                if (b == null) continue;
                var r = new R(b); var list = new List<object>();
                while (r.Left >= 8)
                {
                    int id = r.Il(), n = r.Il() + 1; var vals = new List<object>();
                    for (int k = 0; k < n; k++) vals.Add(r.Il());
                    list.Add(new JsonObj { { "id", id }, { "values", vals } });
                }
                Put(name, list);
            }
            object Weapons(byte[] data)
            {
                var r = new R(data); var list = new List<object>();
                while (r.Left >= 4)
                {
                    int ship = r.Sl(), n = r.Sl(); var ws = new List<object>();
                    for (int k = 0; k < n; k++)
                    {
                        int t = r.Sl(), a = r.Sl(), bb = r.Sl(), c = r.Sl();
                        var w = new JsonObj { { "slotType", t }, { "position_file", new List<object> { a, bb, c } }, { "position_engine", new List<object> { a, c, -bb } } };
                        if (t == 3) w.Add("turretAngles", new List<object> { r.Fl(), r.Fl(), r.Fl() });
                        ws.Add(w);
                    }
                    list.Add(new JsonObj { { "ship", ship }, { "shipName", ship >= 0 && ship < 64 ? Txt(913 + ship) : null }, { "mounts", ws } });
                }
                return list;
            }
            foreach (var fn in new[] { "weapons_hd", "weapons_sd" })
                if ((b = read($"bin/{fn}.bin")) != null) Put(fn, Weapons(b));
            foreach (var f in all.Where(f => f.StartsWith("assets/") && f.Contains("/bin/") && f.EndsWith("_weapons.bin")))
                Put(Stem(f), Weapons(read(f)));
            object Docks(byte[] data)
            {
                var r = new R(data); var list = new List<object>();
                while (r.Left >= 4)
                {
                    int sid = r.Usl(), n = r.Usl(); var pts = new List<object>();
                    for (int k = 0; k < n; k++)
                    {
                        int id = r.Usl(); float px = r.Fl(), py = r.Fl(), pz = r.Fl();
                        var rot = new List<object> { r.Fl(), r.Fl(), r.Fl() }; var ex = new List<object> { r.Fl(), r.Fl(), r.Fl() };
                        pts.Add(new JsonObj { { "id", id }, { "position_file", new List<object> { px, py, pz } },
                            { "position_engine", new List<object> { px, pz, -py } }, { "rotationDeg_file", rot }, { "extra", ex } });
                    }
                    list.Add(new JsonObj { { "station", sid }, { "points", pts } });
                }
                return list;
            }
            foreach (var fn in new[] { "docks_hd", "docks" })
                if ((b = read($"bin/{fn}.bin")) != null) Put(fn, Docks(b));
            foreach (var f in all.Where(f => f.StartsWith("assets/") && f.Contains("/bin/") && f.Contains("docking_points") && f.EndsWith(".bin")))
                Put(Stem(f), Docks(read(f)));

            // ---- parse.py + export.py
            var items = read("bin/items.bin");
            if (items != null)
            {
                var r = new R(items); var list = new List<object>(); var attrList = new List<object>();
                for (int idx = 0; idx < 233; idx++)
                {
                    var ing = r.Arr(); var qty = r.Arr(); var at = r.Arr();
                    var keys = new List<int>(); var a = new Dictionary<int, int>();
                    for (int k = 0; k + 1 < at.Count; k += 2) { if (!a.ContainsKey(at[k])) keys.Add(at[k]); a[at[k]] = at[k + 1]; }
                    int A(int k) => a.TryGetValue(k, out var v) ? v : 0;
                    var stats = new JsonObj(); var statList = new List<object>();
                    foreach (var k in keys)
                    {
                        if (k < 9) continue;
                        string sn = AttrName(k);
                        stats.Set(sn, a[k]);
                    }
                    foreach (var kv in stats) statList.Add(new JsonObj { { "key", kv.Key }, { "value", kv.Value } });
                    var bp = new List<object>();
                    for (int k = 0; k < Math.Min(ing.Count, qty.Count); k++)
                        bp.Add(new JsonObj { { "item", ing[k] }, { "name", Txt(1274 + ing[k]) }, { "amount", qty[k] } });
                    var raw = new JsonObj();
                    foreach (var k in keys) raw.Add(k.ToString(CultureInfo.InvariantCulture), a[k]);
                    list.Add(new JsonObj { { "index", idx }, { "name", Txt(1274 + idx) }, { "description", Txt(1041 + idx) },
                        { "type", Types[A(1)] }, { "category", Txt(221 + A(2)) }, { "categoryId", A(2) }, { "techLevel", A(3) },
                        { "occurrence", A(6) }, { "minPrice", A(7) }, { "maxPrice", A(8) }, { "lowestPriceSystem", A(4) },
                        { "highestPriceSystem", A(5) }, { "stats", stats }, { "blueprint", bp }, { "rawAttributes", raw }, { "statList", statList } });
                    var sk = keys.OrderBy(k => k).ToList();
                    attrList.Add(new JsonObj { { "index", idx }, { "keys", sk.Cast<object>().ToList() }, { "values", sk.Select(k => (object)a[k]).ToList() } });
                }
                Put("items", list);
                o["item_attributes"] = TableJson.Write(attrList, compact: true);
            }
            var ships = read("bin/ships.bin");
            if (ships != null)
            {
                var r = new R(ships); var list = new List<object>();
                for (int k = 0; k < 64; k++)
                {
                    var s = r.Ibs(9);
                    list.Add(new JsonObj { { "index", s[0] }, { "name", Txt(913 + s[0]) }, { "description", Txt(977 + s[0]) }, { "armor", s[1] },
                        { "cargo", s[2] }, { "price", s[3] }, { "slots", new JsonObj { { "primary", s[4] }, { "secondary", s[5] }, { "turret", s[6] }, { "equipment", s[7] } } },
                        { "handling", s[8] }, { "handlingMultiplier", s[8] / 100.0 } });
                }
                Put("ships", list);
            }
            var systemNames = new List<string>();
            var sys = read("bin/systems.bin");
            if (sys != null)
            {
                var r = new R(sys); var list = new List<object>();
                for (int k = 0; k < 34; k++)
                {
                    string name = r.Strb(); var v = r.Ibs(8);
                    int n0 = r.Ib(); var a0 = new List<object>(); for (int q = 0; q < n0; q++) a0.Add(r.Ib());
                    var a1 = r.Arr(); var a2 = r.Arr(); var a3 = r.Arr();
                    systemNames.Add(name);
                    list.Add(new JsonObj { { "index", k }, { "name", name }, { "securityLevel", v[0] }, { "initiallyVisible", v[1] == 1 },
                        { "race", v[2] >= 0 && v[2] < 10 ? (object)Txt(406 + v[2]) : v[2] }, { "raceId", v[2] },
                        { "mapPosition", new JsonObj { { "x", v[3] }, { "y", v[4] }, { "z", v[5] } } }, { "jumpgateStation", v[6] },
                        { "textureIndex", v[7] }, { "unknownTriple", a0 }, { "stations", a1.Cast<object>().ToList() },
                        { "jumpRoutesTo", a2.Cast<object>().ToList() }, { "forbiddenGoodsOrUnknown", a3.Cast<object>().ToList() } });
                }
                Put("systems", list);
            }
            var stations = read("bin/stations.bin");
            if (stations != null)
            {
                var r = new R(stations); var list = new List<object>();
                while (r.Left > 0)
                {
                    string name = r.Strb(); var v = r.Ibs(4);
                    list.Add(new JsonObj { { "index", v[0] }, { "name", name }, { "system", v[1] },
                        { "systemName", v[1] >= 0 && v[1] < systemNames.Count ? systemNames[v[1]] : null }, { "techLevel", v[2] }, { "textureIndex", v[3] } });
                }
                Put("stations", list);
            }
            return o;
        }

        static string Stem(string path)
        {
            string f = path.Substring(path.LastIndexOf('/') + 1);
            return f.Substring(0, f.Length - 4);
        }

        static readonly string[] Types = { "primary", "secondary", "turret", "equipment", "commodity" };

        static readonly Dictionary<int, string> Attr = new Dictionary<int, string>
        {
            { 0, "index" }, { 1, "type" }, { 2, "category" }, { 3, "techLevel" }, { 4, "lowestPriceSystem" }, { 5, "highestPriceSystem" },
            { 6, "occurrence" }, { 7, "minPrice" }, { 8, "maxPrice" }, { 9, "damage" }, { 10, "empDamage" }, { 11, "loadingTimeMs" },
            { 12, "range" }, { 13, "projectileSpeed" }, { 14, "magnitude" }, { 15, "steerable" }, { 16, "automatic" }, { 17, "handling" },
            { 18, "shieldCapacity" }, { 19, "shieldRegenTime" }, { 20, "armor" }, { 21, "empDefense" }, { 22, "cargoBonus" },
            { 23, "automatic_2" }, { 24, "timeToLock" }, { 25, "boostSpeed" }, { 26, "boostRechargeMs" }, { 27, "boostDurationMs" },
            { 28, "agility" }, { 29, "timeToLock_2" }, { 30, "showClassAAsteroids" }, { 31, "radarShowsCargo" }, { 32, "handling_2" },
            { 33, "miningYield" }, { 34, "cabinSize" }, { 35, "effect_35" }, { 36, "loadingSpeed_36" }, { 37, "loadingSpeed_37" },
            { 38, "energyConsumption" }, { 39, "fireRateFactor" }, { 40, "damageFactor" }, { 41, "effect_41" }, { 42, "effect_42" },
            { 43, "loadingSpeed_43" }, { 44, "range_44" }, { 45, "showOnRadar" }, { 46, "effect_46" }, { 47, "range_47" },
            { 48, "cabinSize_48" }, { 49, "speed_49" }, { 50, "magnitude_50" }, { 51, "range_51" }, { 52, "gammaShielding" },
            { 53, "range_53" }, { 54, "effect_54" }, { 55, "count" }, { 56, "effect_56" }, { 57, "showInfo" }, { 58, "showOnRadar_58" },
            { 59, "plasmaConsumption" }, { 60, "raceSpecific_guess" }, { 61, "iconIndex_guess" },
        };

        static string AttrName(int k) => Attr.TryGetValue(k, out var n) ? n : "attr_" + k.ToString(CultureInfo.InvariantCulture);

        // ---- reading: the original's two byte orders (ReadSwitched big-endian, plain Read little-endian) ----------

        sealed class R
        {
            readonly byte[] b; int o;
            public R(byte[] b) { this.b = b; }
            public int Left => b.Length - o;
            void Need(int n) { if (o + n > b.Length) throw new FormatException("a data file ends early"); }
            public int Ib() { Need(4); int v = b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]; o += 4; return v; }
            public int Sb() { Need(2); int v = (short)(b[o] << 8 | b[o + 1]); o += 2; return v; }
            public int Il() { Need(4); int v = BitConverter.ToInt32(b, o); o += 4; return v; }
            public int Sl() { Need(2); int v = BitConverter.ToInt16(b, o); o += 2; return v; }
            public int Usl() { Need(2); int v = BitConverter.ToUInt16(b, o); o += 2; return v; }
            public float Fl() { Need(4); float v = BitConverter.ToSingle(b, o); o += 4; return v; }
            public int C() { Need(1); return (sbyte)b[o++]; }
            public string Strb()
            {
                Need(2); int n = b[o] << 8 | b[o + 1]; o += 2; Need(n);
                string s = Encoding.UTF8.GetString(b, o, n); o += n; return s;   // (invalid bytes become U+FFFD, as Python's 'replace')
            }
            public int[] Ibs(int n) { var a = new int[n]; for (int i = 0; i < n; i++) a[i] = Ib(); return a; }
            public List<object> Cs(int n) { var a = new List<object>(); for (int i = 0; i < n; i++) a.Add(C()); return a; }
            public List<int> Arr() { int n = Ib(); var a = new List<int>(); for (int i = 0; i < n; i++) a.Add(Ib()); return a; }
        }
    }

    /// <summary>An ordered JSON object (keys in insertion order, as the Python scripts' dicts).</summary>
    public sealed class JsonObj : List<KeyValuePair<string, object>>
    {
        public void Add(string key, object value) => Add(new KeyValuePair<string, object>(key, value));
        /// <summary>Sets a key's value, keeping its first place (a Python dict's assignment).</summary>
        public void Set(string key, object value)
        {
            int i = FindIndex(kv => kv.Key == key);
            if (i >= 0) this[i] = new KeyValuePair<string, object>(key, value); else Add(key, value);
        }
    }

    /// <summary>A small JSON writer for the tables: Obj, lists, strings, numbers, booleans, null; indented one space a level
    /// as json.dump(indent=1) writes them (compact: no spaces, as separators=(',', ':')).</summary>
    public static class TableJson
    {
        public static string Write(object v, bool compact = false)
        {
            var s = new StringBuilder();
            W(s, v, 0, compact);
            return s.ToString();
        }

        static void W(StringBuilder s, object v, int depth, bool compact)
        {
            switch (v)
            {
                case null: s.Append("null"); return;
                case bool b: s.Append(b ? "true" : "false"); return;
                case string t: Str(s, t); return;
                case int i: s.Append(i.ToString(CultureInfo.InvariantCulture)); return;
                case long l: s.Append(l.ToString(CultureInfo.InvariantCulture)); return;
                case float f: Num(s, f); return;
                case double d: Num(s, d); return;
                case JsonObj o:
                {
                    if (o.Count == 0) { s.Append("{}"); return; }
                    s.Append('{');
                    for (int k = 0; k < o.Count; k++)
                    {
                        if (k > 0) s.Append(',');
                        Nl(s, depth + 1, compact); Str(s, o[k].Key); s.Append(compact ? ":" : ": "); W(s, o[k].Value, depth + 1, compact);
                    }
                    Nl(s, depth, compact); s.Append('}');
                    return;
                }
                case System.Collections.IList list:
                {
                    if (list.Count == 0) { s.Append("[]"); return; }
                    s.Append('[');
                    for (int k = 0; k < list.Count; k++)
                    {
                        if (k > 0) s.Append(',');
                        Nl(s, depth + 1, compact); W(s, list[k], depth + 1, compact);
                    }
                    Nl(s, depth, compact); s.Append(']');
                    return;
                }
                default: throw new ArgumentException("can't write " + v.GetType().Name);
            }
        }

        static void Nl(StringBuilder s, int depth, bool compact)
        {
            if (compact) return;
            s.Append('\n').Append(' ', depth);
        }

        static void Num(StringBuilder s, double d)
        {
            if (double.IsNaN(d)) { s.Append("NaN"); return; }
            if (double.IsInfinity(d)) { s.Append(d > 0 ? "Infinity" : "-Infinity"); return; }
            string t = d.ToString("R", CultureInfo.InvariantCulture);
            s.Append(t.Contains(".") || t.Contains("E") ? t : t + ".0");
        }

        static void Str(StringBuilder s, string t)
        {
            s.Append('"');
            foreach (char c in t)
            {
                switch (c)
                {
                    case '"': s.Append("\\\""); break;
                    case '\\': s.Append("\\\\"); break;
                    case '\n': s.Append("\\n"); break;
                    case '\r': s.Append("\\r"); break;
                    case '\t': s.Append("\\t"); break;
                    default:
                        if (c < 0x20) s.Append("\\u").Append(((int)c).ToString("x4")); else s.Append(c);
                        break;
                }
            }
            s.Append('"');
        }
    }
}
