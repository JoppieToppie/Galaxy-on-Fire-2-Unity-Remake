// ModdingReferenceBuilder.cs
// GoF2 > Build > Modding AI Reference: Modding/ai/gof2-modding/reference.md, every number and name of the original game a
// mod file or an event graph can refer to (items with their stats and raw attributes, item categories, the attribute
// numbers, blueprint recipes, ships with their models, systems with their skies, stations with their planets and models,
// story steps, story speakers, every assembled model with the texture names a skin replaces, the backdrop textures, music
// tracks, sound effects, the event commands' sound / music words, the built-in event graphs; the Android economy), and
// voices.md beside it (every voice clip with its speaker and English line, for "[voice <clip>]"), missions.md + mission.schema.json
// (the .gof2mission step format, Events.QuestSchema). The AI modding guide
// (Modding/ai/gof2-modding/SKILL.md) points assistants to both. Run it again after changing the game data. Mods' content is
// left out (only the original numbers).

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GoF2Remake.Data;
using UnityEditor;
using UnityEngine;

namespace GoF2Remake.EditorTools
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]   // read-only tables
    public static class ModdingReferenceBuilder
    {
        const string OutputPath = "Modding/ai/gof2-modding/reference.md";
        const string VoicesPath = "Modding/ai/gof2-modding/voices.md";
        const string EventsPath = "Modding/ai/gof2-modding/events.md";
        const string ScriptingPath = "Modding/ai/gof2-modding/scripting.md";
        const string GuidePath = "Modding/ai/gof2-modding/guide.md";
        const string QuestsPath = "Modding/ai/gof2-modding/missions.md";
        const string QuestSchemaPath = "Modding/ai/gof2-modding/mission.schema.json";
        const string GuideSource = "Modding/README.md";

        static readonly string[] RaceNames = { "Terran", "Vossk", "Nivelian", "Midorian", "?", "?", "?", "?", "Pirate", "Void", "Specter" };
        static string Race(int r) => r >= 0 && r < RaceNames.Length ? RaceNames[r] : "-";
        static string Clean(string s) => (s ?? "").Replace("�", "?").Replace("|", "/").Replace("\r", "").Replace("\n", " ").Trim();

        // ModTextures' material slots: a texture in one of these is what a skin (textures/<name>.png) replaces.
        static readonly string[] TextureSlots = { "_BaseMap", "_MainTex", "_BumpMap", "_MetallicGlossMap", "_EmissionMap" };

        // What the skies, suns and planets look like (described from the textures: Resources/GoF2Sky/nebula_0NN, the suns and
        // planets in Assets/Textures; written by hand, so check them when a texture changes).
        static readonly string[] SkyLooks =
        {
            "green-teal nebula clouds", "dark red with a bright fiery red-orange streak", "faint dim blue-green and brown wisps (dark sky)",
            "bright blue nebula clouds", "faint grey-blue wisps with a bright white band", "red-orange fiery clouds",
            "dim green and pink clouds with bright white bands", "bright white-gold and grey-green clouds", "green and golden-yellow clouds",
            "dim grey-violet wisps (dark sky)", "violet and purple clouds (the Void)", "the whole sky orange-gold haze",
            "the whole sky blue haze", "mostly dark, faint grey wisps and one bright white streak", "yellow-green clouds",
            "the whole sky orange with bright gold clusters (the supernova)", "pale turquoise and white storm haze",
            "white glare over a turquoise cloud horizon", "white glare over a golden-yellow cloud horizon",
        };

        // OrbitLayout.SunTextures: the sun of each sky.
        static readonly string[] SunOfSky =
        {
            "sun_000", "sun_001", "sun_002", "sun_003", "sun_004", "sun_005", "sun_006", "sun_007", "sun_008", "sun_009",
            "sun_010", "sun_001", "sun_008", "sun_004", "sun_000", "sn_supernova", "sun_007", "sun_002", "sun_006",
        };

        static readonly Dictionary<string, string> SunLooks = new Dictionary<string, string>
        {
            ["sun_000"] = "green", ["sun_001"] = "orange", ["sun_002"] = "pale green", ["sun_003"] = "magenta", ["sun_004"] = "small white",
            ["sun_005"] = "red", ["sun_006"] = "pale turquoise", ["sun_007"] = "yellow", ["sun_008"] = "cyan-blue", ["sun_009"] = "white",
            ["sun_010"] = "blue", ["sn_supernova"] = "the supernova",
        };

        static readonly string[] PlanetLooks =
        {
            "Earth-like: green-brown land, white clouds", "banded tan and brown gas giant", "murky olive-brown world",
            "pale lilac cloudy world", "blue-green ocean world with clouds", "molten lava world, glowing orange cracks",
            "green jungle / swamp world", "rust-red desert world (Mars-like)", "blue ocean world with green continents",
            "small gas giant with tilted rings", "green and turquoise banded gas giant", "dark red rocky world",
            "orange and cream banded gas giant (Jupiter-like)", "dark world with glowing orange city lights / cracks",
            "dark moon, a thin lit crescent", "grey-brown cloudy rocky world", "small tan gas giant with tilted rings",
            "deep blue ocean world with clouds", "dark grey dim world", "pale blue-grey ice / ocean world",
            "dark olive-gold rocky world", "green planet with wide golden rings", "cratered tan-grey moon",
            "the Void's world: violet cell pattern", "scorched burning lava world (the supernova)", "blue Earth-like world with a moon beside it",
            "dark blue-green ocean world with a bright atmosphere rim",
        };

        /// <summary>The model a station's orbit shows (OrbitBuilder.StationAssembly without the story's states: the finished
        /// game's), what a mod station's looksLike borrows.</summary>
        static string StationModel(int station, int race, List<AssemblyData> stationModels)
        {
            switch (station)
            {
                case 100: return "v_station_deep_science_damaged (v_station_deep_science before the Valkyrie add-on is won)";
                case 101: return "v_station_battlestation_anim";
                case 108: return "station_kaamo_club";
                case 109: case 110: return "sn_station_midorian_wrecked";
                case 111: return "sn_station_midorian_wrecked (sn_burning_station_luur during the Supernova story)";
            }
            string prefix = $"station_{station:000}_";
            var entry = stationModels.FirstOrDefault(a => a.name.StartsWith(prefix) || a.name.StartsWith("v_" + prefix) || a.name.StartsWith("sn_" + prefix));
            return entry != null ? entry.name : race == 1 ? "station_vossk" : "";
        }

        /// <summary>A model's hull size in game units (length x width x height), the additive glow layers left out.</summary>
        static string HullSize(AssemblyData a)
        {
            var prefab = Resources.Load<GameObject>(Visuals.AssembledObject.PrefabPath(a));
            if (prefab == null) return "";
            var go = Object.Instantiate(prefab);
            try
            {
                Bounds? b = null;
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r.name.Contains("_add") || r.name.Contains("glow")) continue;
                    if (b == null) b = r.bounds; else { var x = b.Value; x.Encapsulate(r.bounds); b = x; }
                }
                if (b == null) return "";
                var s = b.Value.size / ImportSettings.ModelScale;
                return $"{Mathf.RoundToInt(s.z)} x {Mathf.RoundToInt(s.x)} x {Mathf.RoundToInt(s.y)}";
            }
            finally { Object.DestroyImmediate(go); }
        }

        [MenuItem("GoF2/Build/Modding AI Reference", priority = 210)]
        public static void Build()
        {
            var savedEconomy = Session.Economy;
            Session.Economy = Economy.Android;
            var db = Database.Load();
            Session.Economy = Economy.Default;
            var dbDefault = Database.Load();
            Session.Economy = savedEconomy;
            string savedLanguage = Localization.IsLoaded ? Localization.Language : null;
            LoadLanguage("en");
            try
            {
                WriteReference(db, dbDefault);
                WriteVoices(db);
                Write(EventsPath, new StringBuilder(ModdingEventsReference.Build()));
                // The mod scripts' reference (Lua): the game table, the hook classes, the classes scripts reach.
                Write(ScriptingPath, new StringBuilder(ModdingScriptingReference.Build()));
                // The .gof2mission format from the game's own step table (Events.QuestSchema).
                Write(QuestsPath, new StringBuilder(Events.QuestSchema.Markdown()));
                Write(QuestSchemaPath, new StringBuilder(Events.QuestSchema.Build().ToString(Newtonsoft.Json.Formatting.Indented) + "\n"));
                WriteGuide();
            }
            finally
            {
                if (savedLanguage != null && savedLanguage != "en") LoadLanguage(savedLanguage);
                EditorUtility.ClearProgressBar();
                EditorUtility.UnloadUnusedAssetsImmediate();
            }
        }

        /// <summary>For tests and the command line (-executeMethod).</summary>
        public static void Batch() => Build();

        static void LoadLanguage(string code)
        {
            var t = LanguageTables.Load();
            if (t == null || t.codes == null) return;
            int i = System.Array.IndexOf(t.codes, code);
            if (i >= 0 && i < t.tables.Length && t.tables[i] != null) Localization.Load(code, t.tables[i]);
        }

        /// <summary>guide.md: the full modders' guide (Modding/README.md) copied into the skill folder, so an assistant has
        /// every field and rule SKILL.md condenses.</summary>
        static void WriteGuide()
        {
            if (!File.Exists(GuideSource)) return;
            var sb = new StringBuilder();
            sb.AppendLine("<!-- Copied from Modding/README.md by GoF2 > Build > Modding AI Reference: edit that file, not this one. -->");
            sb.AppendLine();
            sb.Append(File.ReadAllText(GuideSource));
            Write(GuidePath, sb);
        }

        static void WriteReference(Database db, Database dbDefault)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Galaxy on Fire 2 Remake: game data reference for modders");
            sb.AppendLine();
            sb.AppendLine("Generated from the game's own tables (GoF2 > Build > Modding AI Reference; the Android economy, the Default economy's");
            sb.AppendLine("prices differ). Use these numbers and names in mod files and event graphs: `base`, `override`, ingredients, `alwaysSoldAt`,");
            sb.AppendLine("`system`, `looksLike`, `planet`, `sky`, `startStation`, `startShip`, `replaces`, speakers, `textures/<name>.png`,");
            sb.AppendLine("`music/<name>.ogg`, `sounds/<name>.ogg`, `/spawn` objects... Mod content is referred to by key instead (`mod_id:id`).");
            sb.AppendLine("Beside this file: `voices.md` (every voice clip with its line), `events.md` (every event graph node with its options and");
            sb.AppendLine("ports, every command), `missions.md` + `mission.schema.json` (the .gof2mission step format), `guide.md` (the full modders' guide:");
            sb.AppendLine("every field of every mod file).");
            sb.AppendLine();
            sb.AppendLine("Contents: Races, Items, Item categories, Item stats (attribute numbers), Blueprints, Ships, Ship hardpoints, Default");
            sb.AppendLine("economy, Systems, Skies, Stations, Planets, Story steps, Story speakers, Models and their textures, Backdrop textures,");
            sb.AppendLine("Music, Sound effects, Event sounds and music, Built-in event graphs.");
            sb.AppendLine();
            sb.AppendLine("## Races");
            sb.AppendLine();
            sb.AppendLine("0 Terran, 1 Vossk, 2 Nivelian, 3 Midorian, 8 Pirate, 9 Void, 10 Specter. Systems and stations belong to 0-3.");
            sb.AppendLine("By name (characters, campaigns, event graphs): terran, vossk, nivelian, midorian, pirate, void, specter.");
            sb.AppendLine();

            // ---- Items ----
            var readable = new Dictionary<int, string>();
            foreach (var kv in Modding.ItemStats.Readable()) if (!readable.ContainsKey(kv.Value)) readable[kv.Value] = kv.Key;
            var originals = db.Items.Where(i => i.index < 233 && !i.modded).ToList();

            sb.AppendLine("## Items");
            sb.AppendLine();
            sb.AppendLine("Type: primary / secondary / turret / equipment / commodity. Tech = tech level (1-10). Occ = occurrence (how often shops");
            sb.AppendLine("stock it, 0-90). Price = min-max (credits). Stats = every attribute the item has, by the stat name items.json's `stats`");
            sb.AppendLine("takes; `attr N` = an attribute without a name (set it with `\"attributes\": { \"N\": value }`); `vosskOnly` and");
            sb.AppendLine("`alwaysSoldAt <station>` are items.json fields of their own. Copy them when basing an item on it. BP = has a blueprint");
            sb.AppendLine("recipe in the original (see Blueprints).");
            sb.AppendLine();
            sb.AppendLine("| # | Name | Type | Category | Tech | Occ | Price | Stats | BP |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var it in originals)
            {
                var stats = new List<string>();
                for (int k = 0; k < it.attrKeys.Length; k++)
                {
                    int id = it.attrKeys[k];
                    int v = it.attrValues[k];
                    if (id < 9) continue;   // 0-8: the item's own header (index, type, category, tech level, prices...)
                    if (id == 60) { if (v != 0) stats.Add("vosskOnly"); continue; }   // items.json fields of their own
                    if (id == 61) { stats.Add("alwaysSoldAt " + v); continue; }
                    if (readable.TryGetValue(id, out var n)) stats.Add(n + " " + v);
                    else if (v != 0) stats.Add("attr " + id + " " + v);
                }
                if (stats.Count == 0) foreach (var s in it.statList) if (!s.key.EndsWith("_guess")) stats.Add(s.key + " " + s.value);
                sb.AppendLine($"| {it.index} | {Clean(GameNames.Item(it.index))} | {it.type} | {Clean(UI.ItemInfo.Category(it))} | {it.techLevel} | {it.occurrence} | {it.minPrice}-{it.maxPrice} | {Clean(string.Join(", ", stats))} | {(it.blueprint != null && it.blueprint.Count > 0 ? "yes" : "")} |");
            }
            sb.AppendLine();

            sb.AppendLine("## Item categories");
            sb.AppendLine();
            sb.AppendLine("A new item keeps its `base` item's category (and works like it): pick a base of the category you want.");
            sb.AppendLine();
            sb.AppendLine("| Category # | Name | Type | Items |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var g in originals.GroupBy(i => i.categoryId).OrderBy(g => g.Key))
            {
                var first = g.First();
                sb.AppendLine($"| {g.Key} | {Clean(UI.ItemInfo.Category(first))} | {first.type} | {string.Join(", ", g.Select(i => i.index))} |");
            }
            sb.AppendLine();

            sb.AppendLine("## Item stats (attribute numbers)");
            sb.AppendLine();
            sb.AppendLine("The stat names items.json's `stats` takes and their attribute numbers (`attributes` takes the numbers). 100+ are the");
            var unnamed = originals.SelectMany(i => i.attrKeys).Where(id => id >= 9 && id != 60 && id != 61 && !readable.ContainsKey(id)).Distinct().OrderBy(id => id);
            sb.AppendLine($"remake's own. The original's attributes without a name: {string.Join(", ", unnamed)} (set only through raw");
            sb.AppendLine("`attributes`); 60 = Vossk-made (`vosskOnly`), 61 = always sold at that station (`alwaysSoldAt`).");
            sb.AppendLine();
            sb.AppendLine("| Attr | Stat name | Used by items |");
            sb.AppendLine("|---|---|---|");
            foreach (var kv in readable.OrderBy(kv => kv.Key))
            {
                var users = originals.Where(i => i.HasAttr(kv.Key)).Select(i => i.index.ToString()).ToList();
                sb.AppendLine($"| {kv.Key} | {kv.Value} | {(users.Count > 40 ? users.Count + " items" : string.Join(", ", users))} |");
            }
            sb.AppendLine();

            sb.AppendLine("## Blueprints");
            sb.AppendLine();
            sb.AppendLine("The original blueprint recipes (product, then amount x ingredient). blueprints.json can give an item another recipe.");
            sb.AppendLine();
            sb.AppendLine("| Product # | Product | Ingredients |");
            sb.AppendLine("|---|---|---|");
            foreach (var it in originals)
            {
                if (it.blueprint == null || it.blueprint.Count == 0) continue;
                var parts = it.blueprint.Select(p => $"{p.amount} x {p.item} {Clean(GameNames.Item(p.item))}");
                sb.AppendLine($"| {it.index} | {Clean(GameNames.Item(it.index))} | {string.Join(", ", parts)} |");
            }
            sb.AppendLine();

            // ---- Ships ----
            sb.AppendLine("## Ships");
            sb.AppendLine();
            sb.AppendLine("Armor = hull points. Handling: 100 = average. Slots = primary / secondary / turret / equipment. Hangar = `hangarHeight`");
            sb.AppendLine("(pivot above the pad, game units). Size = length x width x height of the hull in game units (20 = 1 m; a new ship's");
            sb.AppendLine("`modelLength` compares with Length). Model = its assembled model's name: the prefix of its texture names and the folder");
            sb.AppendLine("of one-ship skins (`textures/<model>/<name>.png`); its textures are listed under Models. Ships 13, 14 and 15 are the");
            sb.AppendLine("freighters and the Terran battleship (not sold, only flown by NPCs).");
            sb.AppendLine();
            sb.AppendLine("| # | Name | Race | Armor | Cargo t | Handling | Price | Slots | Hangar | Size (L x W x H) | Model |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var s in db.Ships)
            {
                if (s.index >= 64) break;
                int race = s.index == 13 ? 1 : s.index < Shop.ShipRace.Length ? Shop.ShipRace[s.index] : -1;
                var sl = s.slots ?? new ShipSlots();
                string shipName = s.index == 13 ? "Vossk freighter (not sold)" : s.index == 14 ? "Terran battleship (not sold)" : s.index == 15 ? "freighter of each race (not sold)" : Clean(GameNames.Ship(s.index));
                var models = s.index == 15
                    ? new[] { "terran", "vossk", "nivelian", "midorian" }.Select(r => db.ShipAssembly(15, r, true)).Where(a => a != null).GroupBy(a => a.name).Select(g => g.First()).ToList()
                    : new[] { db.ShipAssembly(s.index, null, true) }.Where(a => a != null).ToList();
                string size = models.Count > 0 ? HullSize(models[0]) : "";
                sb.AppendLine($"| {s.index} | {shipName} | {(s.index == 15 ? "each" : Race(race))} | {s.armor} | {s.cargo} | {s.handling} | {s.price} | {sl.primary}/{sl.secondary}/{sl.turret}/{sl.equipment} | {Mathf.RoundToInt(World.StationTables.ShipY(s.index))} | {size} | {string.Join(", ", models.Select(a => a.name))} |");
            }
            sb.AppendLine();

            sb.AppendLine("## Ship hardpoints");
            sb.AppendLine();
            sb.AppendLine("Every original ship's mounts as ships.json `mounts` takes them (game units from the ship's centre: x right, y up, z");
            sb.AppendLine("forward; slotType 0 primary, 1 secondary, 2 turret, 3 engine exhaust with `turretAngles[0]` its particle size). An");
            sb.AppendLine("`override` with `mounts` replaces the whole list: copy the line and change what you need. Also a guide for placing a");
            sb.AppendLine("new ship's mounts (compare the hull sizes above).");
            sb.AppendLine();
            foreach (var set in db.WeaponMounts.Where(m => m.ship >= 0 && m.ship < 64 && m.mounts != null).OrderBy(m => m.ship))
            {
                var parts = set.mounts.Select(m =>
                {
                    string p = m.position_engine != null ? string.Join(", ", m.position_engine) : "0, 0, 0";
                    string extra = m.slotType == 3 && m.turretAngles != null && m.turretAngles.Length > 0
                        ? $", \"turretAngles\": [{string.Join(", ", m.turretAngles.Select(a => a.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)))}]" : "";
                    if (m.upsideDown) extra += ", \"upsideDown\": true";
                    return $"{{ \"slotType\": {m.slotType}, \"position_engine\": [{p}]{extra} }}";
                });
                sb.AppendLine($"- **{set.ship} {Clean(GameNames.Ship(set.ship))}**: `[{string.Join(", ", parts)}]`");
            }
            sb.AppendLine();

            // ---- Default economy ----
            sb.AppendLine("## Default economy");
            sb.AppendLine();
            sb.AppendLine("The PC / Mac / iPhone numbers where they differ from the Android ones above (Android -> Default). Put them in an item's");
            sb.AppendLine("`defaultEconomy` block or a ship's `priceDefault` when your mod should match both economies.");
            sb.AppendLine();
            sb.AppendLine("| Item # | Name | Changes |");
            sb.AppendLine("|---|---|---|");
            foreach (var a in originals)
            {
                var d = dbDefault.Items.FirstOrDefault(x => x.index == a.index);
                if (d == null) continue;
                var ch = new List<string>();
                if (a.minPrice != d.minPrice || a.maxPrice != d.maxPrice) ch.Add($"price {a.minPrice}-{a.maxPrice} -> {d.minPrice}-{d.maxPrice}");
                if (a.techLevel != d.techLevel) ch.Add($"techLevel {a.techLevel} -> {d.techLevel}");
                if (a.occurrence != d.occurrence) ch.Add($"occurrence {a.occurrence} -> {d.occurrence}");
                foreach (int id in a.attrKeys.Concat(d.attrKeys).Where(id => id >= 9).Distinct().OrderBy(id => id))
                {
                    int va = a.Attr(id), vd = d.Attr(id);
                    if (va != vd) ch.Add($"{(readable.TryGetValue(id, out var n) ? n : id == 60 ? "vosskOnly" : id == 61 ? "alwaysSoldAt" : "attr " + id)} {va} -> {vd}");
                }
                string Recipe(ItemData x) => x.blueprint == null ? "" : string.Join(", ", x.blueprint.Select(p => $"{p.amount} x {p.item}"));
                if (Recipe(a) != Recipe(d)) ch.Add($"blueprint: {Recipe(d)}");
                if (ch.Count > 0) sb.AppendLine($"| {a.index} | {Clean(GameNames.Item(a.index))} | {Clean(string.Join("; ", ch))} |");
            }
            sb.AppendLine();
            sb.AppendLine("| Ship # | Name | Changes |");
            sb.AppendLine("|---|---|---|");
            foreach (var a in db.Ships.Where(x => x.index < 64))
            {
                var d = dbDefault.Ships.FirstOrDefault(x => x.index == a.index);
                if (d == null) continue;
                var ch = new List<string>();
                if (a.price != d.price) ch.Add($"price {a.price} -> {d.price}");
                if (a.armor != d.armor) ch.Add($"armor {a.armor} -> {d.armor}");
                if (a.cargo != d.cargo) ch.Add($"cargo {a.cargo} -> {d.cargo}");
                if (!Mathf.Approximately(a.handling, d.handling)) ch.Add($"handling {a.handling} -> {d.handling}");
                var sa = a.slots ?? new ShipSlots(); var sd = d.slots ?? new ShipSlots();
                if (sa.primary != sd.primary || sa.secondary != sd.secondary || sa.turret != sd.turret || sa.equipment != sd.equipment)
                    ch.Add($"slots {sa.primary}/{sa.secondary}/{sa.turret}/{sa.equipment} -> {sd.primary}/{sd.secondary}/{sd.turret}/{sd.equipment}");
                if (ch.Count > 0) sb.AppendLine($"| {a.index} | {Clean(GameNames.Ship(a.index))} | {string.Join("; ", ch)} |");
            }
            sb.AppendLine();

            // ---- Systems and stations ----
            sb.AppendLine("## Systems");
            sb.AppendLine();
            sb.AppendLine("Security 0 lawless .. 3 secure. Visible = on the star map from a new game's start. Gates = the systems its jumpgate");
            sb.AppendLine("reaches; Gate station = the station whose orbit has the jumpgate. Sky = the nebula and sun it uses (a mod system's `sky`,");
            sb.AppendLine("0-18; see Skies).");
            sb.AppendLine();
            sb.AppendLine("| # | Name | Race | Security | Visible | Map position | Sky | Gates | Gate station | Stations |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (var y in db.Systems)
            {
                if (y.index >= 34) break;
                var st = db.Stations.FindAll(x => x.system == y.index && x.index < 135).ConvertAll(x => x.index.ToString());
                string pos = y.mapPosition != null ? $"{y.mapPosition.x}, {y.mapPosition.y}, {y.mapPosition.z}" : "";
                string gates = y.jumpRoutesTo != null ? string.Join(", ", y.jumpRoutesTo) : "";
                string gate = y.jumpgateStation >= 0 && y.jumpRoutesTo != null && y.jumpRoutesTo.Count > 0 ? y.jumpgateStation.ToString() : "";
                sb.AppendLine($"| {y.index} | {Clean(y.name)} | {Race(y.raceId)} | {y.securityLevel} | {(y.initiallyVisible ? "yes" : "")} | {pos} | {y.textureIndex} | {gates} | {gate} | {string.Join(", ", st)} |");
            }
            sb.AppendLine();
            sb.AppendLine("Special places: -1 = the Void's home orbit (the alien orbit, \"void\" in event graphs).");
            sb.AppendLine();

            sb.AppendLine("## Skies");
            sb.AppendLine();
            sb.AppendLine("What a system's `sky` number looks like: the nebula layer (`nebula_0NN`, drawn over one of three star layers) and the");
            sb.AppendLine("sun. A mod system can keep the sky and replace only the sun (`sunTexture`) or the nebula (`skybox`).");
            sb.AppendLine();
            sb.AppendLine("| Sky | Nebula | Sun | Used by systems |");
            sb.AppendLine("|---|---|---|---|");
            for (int k = 0; k < SkyLooks.Length; k++)
            {
                var users = db.Systems.Where(y => y.index < 34 && y.textureIndex == k).Select(y => $"{y.index} {Clean(y.name)}");
                string sun = k < SunOfSky.Length ? SunOfSky[k] : "";
                sb.AppendLine($"| {k} | {SkyLooks[k]} | {sun}{(SunLooks.TryGetValue(sun, out var look) ? " (" + look + ")" : "")} | {string.Join(", ", users)} |");
            }
            sb.AppendLine();

            var stationModels = db.Assemblies.Where(a => a.category == "stations" && a.pack != Modding.ModContent.Pack).ToList();
            sb.AppendLine("## Stations");
            sb.AppendLine();
            sb.AppendLine("78 Var Hastra (Mido) is where a new game starts. 108 is the Kaamo Club. Tech = which items its shop can sell. Planet = its");
            sb.AppendLine("planet texture in the other orbits (a mod station's `planet`). Race = its system's (the hangar and bar it docks into). Model =");
            sb.AppendLine("its assembled model: what `looksLike` borrows (only the model and its collision: a station's special rules, like the");
            sb.AppendLine("Kaamo Club's storage and siege, belong to the original station's number and don't come with the look).");
            sb.AppendLine();
            sb.AppendLine("| # | Name | System | Race | Tech | Planet | Model |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var t in db.Stations)
            {
                if (t.index >= 135) break;
                int race = t.system >= 0 && t.system < db.Systems.Count ? db.Systems[t.system].raceId : -1;
                sb.AppendLine($"| {t.index} | {Clean(t.name)} | {t.system} {Clean(t.systemName)} | {Race(race)} | {t.techLevel} | {t.textureIndex} | {StationModel(t.index, race, stationModels)} |");
            }
            sb.AppendLine();

            sb.AppendLine("## Planets");
            sb.AppendLine();
            sb.AppendLine("A station's `planet` number: its texture (`_big` close up, `_small` far away; a skin replaces both) and what it looks like.");
            sb.AppendLine();
            sb.AppendLine("| Planet | Texture | Looks like | Used by stations |");
            sb.AppendLine("|---|---|---|---|");
            for (int k = 0; k < PlanetLooks.Length; k++)
            {
                if (PlanetLooks[k] == null) continue;
                string tex = k < 20 ? $"planet_{k:000}" : k < 23 ? $"v_planet_{k:000}" : k == 23 ? "planet_void" : $"sn_planet_{k:000}";
                var users = db.Stations.Where(t => t.index < 135 && t.textureIndex == k).Select(t => t.index.ToString());
                sb.AppendLine($"| {k} | {tex}_big / _small | {PlanetLooks[k]} | {string.Join(", ", users)} |");
            }
            sb.AppendLine();

            // ---- Story ----
            sb.AppendLine("## Story steps");
            sb.AppendLine();
            sb.AppendLine("The campaign index (`campaign` in conditions, `minCampaign` of lounge sellers). 0-44 the main story, 45-83 Valkyrie, 84-162");
            sb.AppendLine("Supernova. Station = the step's target.");
            sb.AppendLine();
            sb.AppendLine("| Step | Title | Station | Summary |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var step in StoryTable.Steps)
            {
                var sum = StepSummaries.Get(step.index);
                string st = step.station >= 0 && step.station < db.Stations.Count ? $"{step.station} {Clean(db.Stations[step.station].name)}" : "";
                sb.AppendLine($"| {step.index} | {Clean(sum?.title)} | {st} | {Clean(sum?.summary)} |");
            }
            sb.AppendLine();

            sb.AppendLine("## Story speakers");
            sb.AppendLine();
            sb.AppendLine("Speakers with the game's portraits: a speaker in dialog / radio lines and event graphs (by name or number), what a");
            sb.AppendLine("character's `replaces` takes. 16 Info and 17 Story are the tutorial pages and the narrator; 19 and 56 talk in the alien font.");
            sb.AppendLine();
            sb.AppendLine("| # | Name |");
            sb.AppendLine("|---|---|");
            for (int i = 0; i < StoryTable.SpeakerCount; i++)
                sb.AppendLine($"| {i} | {Clean(StoryTable.OriginalSpeakerName(i))} |");
            sb.AppendLine();

            // ---- Models and textures ----
            sb.AppendLine("## Models and their textures");
            sb.AppendLine();
            sb.AppendLine("Every assembled object of the game (the names `/spawn` and the Spawn node take for scenery, and the folder name of");
            sb.AppendLine("one-model skins), with the textures it draws: a mod's `textures/<texture>.png` replaces that texture on every model using");
            sb.AppendLine("it, `textures/<model>/<texture>.png` on that model only, `textures/ships/<ship id>/<texture>.png` on one new ship on that");
            sb.AppendLine("model only (`\"model\": \"gof2:<name>\"`). Keep the original's UV layout. `_normal_specular` = normal map,");
            sb.AppendLine("`_metallic_smoothness` = the remake's metallic map; `glow_<race>` / `ship_engine_glow` and the like are shared lights.");
            sb.AppendLine("Any of them is also a model a mod can use without a file: `\"model\": \"gof2:<name>\"` in ships.json, stations.json and");
            sb.AppendLine("items.json fx (see the guide's \"The game's own models\").");
            sb.AppendLine();
            var cats = db.Assemblies.Where(a => a.pack != Modding.ModContent.Pack).GroupBy(a => a.category).ToList();
            int done = 0, total = cats.Sum(g => g.Count());
            foreach (var g in cats)
            {
                sb.AppendLine($"### {g.Key}");
                sb.AppendLine();
                sb.AppendLine("| Model | Pack | Textures |");
                sb.AppendLine("|---|---|---|");
                foreach (var a in g)
                {
                    if (done++ % 10 == 0) EditorUtility.DisplayProgressBar("Modding AI Reference", a.name, (float)done / total);
                    sb.AppendLine($"| {a.name} | {a.pack} | {string.Join(", ", TexturesOf(a))} |");
                }
                sb.AppendLine();
            }

            sb.AppendLine("## Backdrop textures");
            sb.AppendLine();
            sb.AppendLine("Planets, suns and rings in the sky (replace with `textures/<name>.png`, one whole disc, transparent edge). A station's");
            sb.AppendLine("`planet` N uses `planet_0NN_big` / `_small` (24-26 the Supernova's `sn_planet_0NN_*`).");
            sb.AppendLine();
            sb.AppendLine(string.Join(", ", Directory.GetFiles("Assets/Textures", "*.png", SearchOption.AllDirectories)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => n.StartsWith("planet_") || n.StartsWith("sun_") || n.StartsWith("sn_planet") || n.StartsWith("sn_sun") || n.StartsWith("v_planet") || n.StartsWith("v_sun"))
                .Distinct().OrderBy(n => n)));
            sb.AppendLine();

            // ---- Audio ----
            sb.AppendLine("## Music");
            sb.AppendLine();
            sb.AppendLine("The game's tracks: `music/<name>.ogg` with one of these names replaces it everywhere (any other name is a new track for");
            sb.AppendLine("`spaceMusic`, `stationMusic`, `music` and `/music`). Space_<race> plays in flight, Station_<race> docked, Space_Battle_* in");
            sb.AppendLine("combat, Space_NoCombat_Void in the main menu.");
            sb.AppendLine();
            foreach (var folder in new[] { "MUSIC", "DLC_MUSIC", "DLC2_MUSIC" })
                sb.AppendLine($"- {folder}: {string.Join(", ", AudioNames(folder))}");
            sb.AppendLine();

            sb.AppendLine("## Sound effects");
            sb.AppendLine();
            sb.AppendLine("`sounds/<name>.ogg` (or .wav / .mp3) with one of these names replaces that sound everywhere. Voices are in `voices.md`.");
            sb.AppendLine();
            foreach (var folder in new[] { "SFX_GENERAL", "SFX_SPACE", "SFX_STATION_MAINVIEW", "SFX_STATION_HANGAR", "SFX_STATION_LOUNGE", "DLC_SFX", "DLC2_SFX", "CUTSCENES", "SVMODE" })
                sb.AppendLine($"- {folder}: {string.Join(", ", AudioNames(folder))}");
            sb.AppendLine();

            sb.AppendLine("## Event sounds and music");
            sb.AppendLine();
            sb.AppendLine("The words of the event commands `/sound <word>` and `/music <word>` (the Play Sound / Play Music nodes' lists); `/music` also");
            sb.AppendLine("takes a mod's own track name.");
            sb.AppendLine();
            sb.AppendLine("- Sounds: " + Events.EventAudio.Names<Events.EventSound>());
            sb.AppendLine("- Music: " + Events.EventAudio.Names<Events.EventMusic>() + ", stop");
            sb.AppendLine();

            sb.AppendLine("## Built-in event graphs");
            sb.AppendLine();
            sb.AppendLine("Usable with `/event <name>` and Start Event; a mod's graph of the same name replaces it.");
            sb.AppendLine();
            sb.AppendLine(string.Join(", ", Resources.LoadAll<TextAsset>("GoF2Events").Select(t => t.name).OrderBy(n => n)));

            Write(OutputPath, sb);
        }

        /// <summary>The names of the textures a model's materials draw (what ModTextures matches), sorted.</summary>
        static IEnumerable<string> TexturesOf(AssemblyData a)
        {
            var names = new SortedSet<string>();
            var prefab = Resources.Load<GameObject>(Visuals.AssembledObject.PrefabPath(a));
            if (prefab == null) return names;
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    foreach (var slot in TextureSlots)
                        if (m.HasProperty(slot) && m.GetTexture(slot) is Texture t && t != null && !t.name.StartsWith("unity_") && t.name.Length > 0)
                            names.Add(t.name);
                }
            return names;
        }

        static IEnumerable<string> AudioNames(string folder)
        {
            var dir = Path.Combine("Assets/Audio", folder);
            if (!Directory.Exists(dir)) return Enumerable.Empty<string>();
            return Directory.GetFiles(dir, "*.ogg").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n);
        }

        // ---- voices.md ----

        static void WriteVoices(Database db)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Galaxy on Fire 2 Remake: voice clips");
            sb.AppendLine();
            sb.AppendLine("Generated by GoF2 > Build > Modding AI Reference. Every original voice clip: a dialog page or radio line of an event graph");
            sb.AppendLine("plays one with `[voice <clip>]` in its text (English or German by the player's voice option). A mod's");
            sb.AppendLine("`voices/<clip>.ogg` (or `voices/en/`, `voices/de/`) with an original clip's name replaces it. Speakers: see reference.md.");
            sb.AppendLine();
            sb.AppendLine("## Story and radio lines");
            sb.AppendLine();
            sb.AppendLine("| Clip | Step | Speaker | Line (English) |");
            sb.AppendLine("|---|---|---|---|");
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            void Line(string clip, string step, int speaker, int text)
            {
                if (string.IsNullOrEmpty(clip) || !seen.Add(clip)) return;
                string who = speaker >= 0 ? $"{speaker} {Clean(StoryTable.OriginalSpeakerName(speaker))}" : "";
                sb.AppendLine($"| {clip} | {step} | {who} | {(text >= 0 ? Clean(Localization.Get(text)) : "")} |");
            }
            foreach (var s in StoryTable.Steps)
            {
                foreach (var p in s.briefing) Line(p.voice, s.index.ToString(), p.speaker, p.text);
                foreach (var p in s.success) Line(p.voice, s.index.ToString(), p.speaker, p.text);
                if (s.radio != null) foreach (var r in s.radio) Line(r.voice, s.index.ToString(), r.speaker, r.text);
            }
            var table = Resources.Load<TextAsset>("GoF2Data/voice_table");
            if (table != null)
            {
                var vt = JsonUtility.FromJson<VoiceTable>(table.text);
                if (vt?.items != null) foreach (var v in vt.items) Line(v.voice, "", -1, v.text);
            }
            sb.AppendLine();
            sb.AppendLine("## Other clips");
            sb.AppendLine();
            sb.AppendLine("Generic lines by race (radio chatter, freelance briefings, lounge greetings) and lines the tables above don't name.");
            sb.AppendLine();
            foreach (var folder in new[] { "VOICE_eng", "DLC_VOICE_eng", "DLC2_VOICE_eng", "GENERIC_eng", "LOUNGE_eng" })
            {
                var rest = AudioNames(folder).Where(n => !seen.Contains(n)).ToList();
                foreach (var n in rest) seen.Add(n);
                if (rest.Count > 0) sb.AppendLine($"- {folder.Replace("_eng", "")}: {string.Join(", ", rest)}");
            }
            Write(VoicesPath, sb);
        }

        [System.Serializable] class VoiceTable { public List<VoiceEntry> items; }
        [System.Serializable] class VoiceEntry { public int text; public string voice; }

        static void Write(string relative, StringBuilder sb)
        {
            var path = Path.GetFullPath(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString().Replace("\r\n", "\n"));
            Debug.Log($"GoF2: {path} written");
        }
    }
}
