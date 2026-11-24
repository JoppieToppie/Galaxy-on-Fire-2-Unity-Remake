// ModCampaigns.cs
// Remake mods: a mod's own campaign (campaign.json), a New Game entry of its own under the main menu's three campaigns:
//   {
//     "name": "Galaxy on Fire" | { "en": ..., "de": ... },  "description": ...,  "image": "campaign.png" (the card's art, like
//     the campaign cards: 290 x 448 or a multiple), "imageHover": "campaign_hover.png" (shown while selected), "showTitle": true
//     (the name on a plate at the card's foot; false when the art has its own title),
//     "startStation": "mod_id:station_id" | number,  "startShip": "mod_id:ship_id" | number,  "credits": 5000,
//     "equipment": [item, { "item": item, "amount": n }, ...]  (mounted; a secondary weapon's amount = its ammo),
//     "cargo": [{ "item": item, "amount": n }, ...],  "standing": [terran_vossk, nivelian_midorian]  (-100..100),
//     "raceStanding": { "race": -100..100, "mod_id:race": ... }  (the start toward mods' races, ModRaces; default each
//                              race's own "standing"),
//     "quest": "graph_name"  (an event graph started as a quest when the game begins: the campaign's story),
//     "galaxy": "mod" | "all"  ("mod": only this mod's systems (and its dependencies') exist for the game: the map, the
//                               missions, the ticker; default "all"),
//     "items": "mod" | "all"   ("mod": shops and bar sellers offer only mods' items),
//     "ships": "mod" | "all"   ("mod": dealers sell only mods' ships),
//     "trafficShips": { "terran": [ship, ...], "vossk": [...], "nivelian": [...], "midorian": [...], "pirate": [...] }
//                              (the ships NPC fighters of that race fly; default the game's)
//     "flightStyle": "free" | "original"  (the campaign's games always fly so, ModFlight; default the player's option)
//     "economy": "original" | "default"  (the campaign's economy: the menu doesn't ask; default the player picks)
//     "medals": [12, "champion", "other_mod:medal"]  (the only medals its games award and show: the game's by number 0-44,
//                              mods' by key, a bare id this mod's; default all)
//   }
// The game runs as a free-play game (Session.FreePlay: the GoF2 story is off) with Session.ModCampaign = the mod's id (saved);
// a save of it needs the mod (ModSaves warns). Starting: Status::resetGame's state, then the campaign's start, docked at its
// station; the quest starts once the station has loaded (Session.GraphQuests + EventRunner.RestorePending).

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModCampaigns
    {
        public const string File = "campaign.json";

        static readonly HashSet<string> Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "name", "description", "image", "imageHover", "showTitle", "startStation", "startShip", "credits", "equipment", "cargo", "standing", "quest",
            "galaxy", "items", "ships", "trafficShips", "flightStyle", "raceStanding", "economy", "medals",
        };

        public class Def
        {
            public ModInfo mod;
            public Dictionary<string, string> name, description;
            public string image, imageHover, startStation, startShip, quest;
            public bool showTitle = true;
            public int credits;
            public List<(string item, int amount)> equipment = new List<(string, int)>(), cargo = new List<(string, int)>();
            public int[] standing;
            public readonly Dictionary<string, int> raceStanding = new Dictionary<string, int>(StringComparer.Ordinal);   // mod race reference -> -100..100
            public bool modGalaxy, modItems, modShips;
            public int flightStyle = -1;   // "flightStyle": the campaign's games fly so (ModFlight), -1 = the player's option
            /// <summary>"economy": "original" (the Android tables) / "default": the campaign's own, the menu doesn't ask (#313).</summary>
            public Economy? economy;
            /// <summary>"medals": the only medals its games award and show (the game's 0-44 by number, mods' by key; a bare id
            /// is this mod's); null: all (#313).</summary>
            public List<string> medals;
            public readonly Dictionary<int, List<string>> trafficShips = new Dictionary<int, List<string>>();
            public string Id => mod.Id;
            public string Name => ModManifest.Pick(name) ?? mod.Name;
            public string Description => ModManifest.Pick(description) ?? mod.Manifest?.Description ?? "";
        }

        static readonly List<Def> defs = new List<Def>();
        static int parsedRevision = -1;
        static readonly HashSet<string> allowedMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static string allowedFor;
        static int allowedRevision = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { defs.Clear(); parsedRevision = -1; allowedMods.Clear(); allowedFor = null; allowedRevision = -1; }

        /// <summary>The campaigns of the mods that are on.</summary>
        public static List<Def> All()
        {
            if (parsedRevision != ModManager.Revision)
            {
                parsedRevision = ModManager.Revision;
                defs.Clear();
                foreach (var mod in ModManager.Active)
                {
                    try { var d = Read(mod); if (d != null) defs.Add(d); }
                    catch (ModJsonException e) { Warn(mod, e.Message); }
                }
            }
            return defs;
        }

        static Def Read(ModInfo mod)
        {
            if (!(ModJson.Read(mod.Source, File) is JToken token)) return null;
            if (!(token is JObject o)) throw new ModJsonException($"{File}: must be an object {{ ... }}");
            foreach (var prop in o.Properties())
                if (!Fields.Contains(prop.Name)) Warn(mod, $"{ModJson.Where(prop, File)}: unknown field \"{prop.Name}\" (ignored)");
            var d = new Def
            {
                mod = mod, name = ModJson.Text(o, "name"), description = ModJson.Text(o, "description"), image = ModJson.Str(o, "image"),
                imageHover = ModJson.Str(o, "imageHover"), showTitle = ModJson.Bool(o, "showTitle", true, File),
                startStation = ModJson.Str(o, "startStation"), startShip = ModJson.Str(o, "startShip"), quest = ModJson.Str(o, "quest"),
                credits = Mathf.Max(0, ModJson.Int(o, "credits", 0, File)),
                modGalaxy = string.Equals(ModJson.Str(o, "galaxy", "all"), "mod", StringComparison.OrdinalIgnoreCase),
                modItems = string.Equals(ModJson.Str(o, "items", "all"), "mod", StringComparison.OrdinalIgnoreCase),
                modShips = string.Equals(ModJson.Str(o, "ships", "all"), "mod", StringComparison.OrdinalIgnoreCase),
            };
            d.flightStyle = ModFlight.ReadStyle(o, File);
            switch (ModJson.Str(o, "economy")?.Trim().ToLowerInvariant())
            {
                case null: case "": break;
                case "original": case "android": d.economy = Economy.Android; break;
                case "default": case "remake": d.economy = Economy.Default; break;
                default: Warn(mod, $"{File}: economy \"{ModJson.Str(o, "economy")}\" is none of \"original\" / \"default\" (the player picks)"); break;
            }
            if (string.IsNullOrEmpty(d.startStation)) throw new ModJsonException($"{File}: \"startStation\" is missing (where the game begins)");
            if (string.IsNullOrEmpty(d.startShip)) throw new ModJsonException($"{File}: \"startShip\" is missing (the ship the player begins with)");
            if (ModJson.Get(o, "medals") is JToken md)
            {
                if (!(md is JArray ma)) throw new ModJsonException($"{File}: \"medals\" must be a list [0, \"champion\", \"mod_id:medal\"]");
                d.medals = ma.Select(x => ((string)x)?.Trim()).Where(x => !string.IsNullOrEmpty(x))
                             .Select(x => int.TryParse(x, out _) || x.Contains(':') ? x : mod.Id + ":" + x).ToList();
            }
            if (ModJson.Get(o, "equipment") is JArray eq)
                foreach (var t in eq) d.equipment.Add(t is JObject e ? ((string)e["item"], e["amount"] != null ? (int)e["amount"] : 1) : ((string)t, 1));
            if (ModJson.Get(o, "cargo") is JArray ca)
                foreach (var t in ca) if (t is JObject c) d.cargo.Add(((string)c["item"], c["amount"] != null ? (int)c["amount"] : 1));
            if (ModJson.Get(o, "standing") is JArray st && st.Count == 2) d.standing = new[] { Mathf.Clamp((int)st[0], -100, 100), Mathf.Clamp((int)st[1], -100, 100) };
            if (ModJson.Get(o, "raceStanding") is JToken fs)
            {
                // Remake: the start toward mods' races (resolved when the game starts: the mod races are numbered then)
                if (!(fs is JObject fo)) throw new ModJsonException($"{File}: \"raceStanding\" must be an object {{ \"race\": 50 }}");
                foreach (var prop in fo.Properties())
                {
                    if (prop.Value.Type != JTokenType.Integer && prop.Value.Type != JTokenType.Float)
                        throw new ModJsonException($"{File}: raceStanding \"{prop.Name}\" must be a number from -100 to 100");
                    d.raceStanding[prop.Name] = Mathf.Clamp(Mathf.RoundToInt((float)prop.Value), -100, 100);
                }
            }
            if (ModJson.Get(o, "trafficShips") is JObject ts)
                foreach (var prop in ts.Properties())
                {
                    int race = RaceOf(prop.Name);
                    if (race < 0) { Warn(mod, $"{File}: trafficShips: unknown race \"{prop.Name}\" (terran, vossk, nivelian, midorian, pirate)"); continue; }
                    d.trafficShips[race] = prop.Value is JArray arr ? arr.Select(x => (string)x).Where(x => !string.IsNullOrEmpty(x)).ToList() : new List<string>();
                }
            return d;
        }

        static int RaceOf(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "terran": return 0;
                case "vossk": return 1;
                case "nivelian": return 2;
                case "midorian": return 3;
                case "pirate": case "pirates": return 8;
            }
            return -1;
        }

        static void Warn(ModInfo mod, string message)
        {
            if (!mod.Warnings.Contains(message)) mod.Warnings.Add(message);
            Debug.LogWarning($"Mods: {mod.Id}: {message}");
        }

        /// <summary>The campaign this game is (Session.ModCampaign; null: a GoF2 game, or its mod is off).</summary>
        public static Def Current => string.IsNullOrEmpty(Session.ModCampaign) ? null : All().Find(c => string.Equals(c.Id, Session.ModCampaign, StringComparison.OrdinalIgnoreCase));

        /// <summary>The medal (a combined number: the game's 0-44, then the mods', ModMedals.At) is awarded and shown in this
        /// game: always, unless the campaign names its "medals" (#313).</summary>
        public static bool MedalAllowed(int medal)
        {
            var list = Current?.medals;
            if (list == null) return true;
            if (medal < Achievements.Count) return list.Contains(medal.ToString());
            var m = ModMedals.At(medal);
            return m != null && list.Exists(k => string.Equals(k, m.key, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The campaign's own galaxy only ("galaxy": "mod").</summary>
        public static bool ModGalaxy => Current?.modGalaxy == true;

        /// <summary>The campaign's mod and the mods it depends on (their systems are its galaxy).</summary>
        static HashSet<string> Allowed(Def c)
        {
            if (allowedFor == c.Id && allowedRevision == ModManager.Revision) return allowedMods;
            allowedFor = c.Id;
            allowedRevision = ModManager.Revision;
            allowedMods.Clear();
            var queue = new Queue<ModInfo>();
            queue.Enqueue(c.mod);
            while (queue.Count > 0)
            {
                var m = queue.Dequeue();
                if (m == null || !allowedMods.Add(m.Id) || m.Manifest == null) continue;
                foreach (var dep in m.Manifest.dependencies) queue.Enqueue(ModManager.Find(dep));
            }
            return allowedMods;
        }

        /// <summary>The system exists for this game (always, unless the campaign keeps only its own galaxy).</summary>
        public static bool SystemAllowed(int system)
        {
            var c = Current;
            if (c == null || !c.modGalaxy) return true;
            var owner = ModWorld.SystemOwner(system);
            return owner != null && Allowed(c).Contains(owner.Id);
        }

        public static bool StationAllowed(Database db, int station)
        {
            if (!ModGalaxy) return true;
            var st = db.Stations.Find(s => s.index == station);
            return st != null && st.system >= 0 && SystemAllowed(st.system);
        }

        /// <summary>A random station of the campaign's galaxy (missions' targets; -1: none).</summary>
        public static int RandomStation(Database db)
        {
            var list = db.Stations.Where(s => s.system >= 0 && SystemAllowed(s.system)).ToList();
            return list.Count > 0 ? list[UnityEngine.Random.Range(0, list.Count)].index : -1;
        }

        /// <summary>Shops and bar sellers may offer it.</summary>
        public static bool ItemAllowed(Database db, int item)
        {
            var c = Current;
            if (c == null || !c.modItems) return true;
            var it = db.Item(item);
            return it != null && it.modded;
        }

        /// <summary>Dealers may sell it.</summary>
        public static bool ShipAllowed(int ship)
        {
            var c = Current;
            return c == null || !c.modShips || ModContent.IsModShip(ship);
        }

        /// <summary>The campaign names its own ships for NPC fighters of the race ("trafficShips"): ModTraffic leaves them.</summary>
        public static bool HasTrafficShips(int race)
        {
            var c = Current;
            return c != null && c.trafficShips.TryGetValue(race == 8 || race > 3 ? 8 : race, out var refs) && refs.Count > 0;
        }

        /// <summary>The ship an NPC fighter of the race flies ("trafficShips"; else the game's pick).</summary>
        public static int TrafficShip(int race, int fallback)
        {
            var c = Current;
            if (c == null || !c.trafficShips.TryGetValue(race == 8 || race > 3 ? 8 : race, out var refs) || refs.Count == 0) return fallback;
            for (int tries = 0; tries < 8; tries++)
                if (ModContent.TryResolveShip(refs[UnityEngine.Random.Range(0, refs.Count)], out int ship) && Database.Shared.Ship(ship) != null) return ship;
            return fallback;
        }

        /// <summary>The entry's picture (the mod's PNG; null: none).</summary>
        public static Texture2D Image(Def c) => string.IsNullOrEmpty(c.image) ? null : ModMaterials.Texture(c.mod, c.image, false, true);
        public static Texture2D ImageHover(Def c) => string.IsNullOrEmpty(c.imageHover) ? null : ModMaterials.Texture(c.mod, c.imageHover, false, true);

        /// <summary>MainMenu: a new game of the campaign (after Session.ResetNewGame and the difficulty / economy): its start
        /// state, docked at its station; the scene to load ("Station"). Null with 'error' when it can't start.</summary>
        public static string Start(Database db, Def c, out string error)
        {
            error = null;
            if (!ModWorld.TryResolveStation(c.startStation, out int station) || db.Stations.Find(s => s.index == station) == null)
            { error = $"{c.Name}: startStation \"{c.startStation}\" is no station"; return null; }
            if (!ModContent.TryResolveShip(c.startShip, out int ship) || db.Ship(ship) == null)
            { error = $"{c.Name}: startShip \"{c.startShip}\" is no ship"; return null; }
            Session.ModCampaign = c.Id;
            Session.FreePlay = true;              // the GoF2 story is off
            Session.CampaignMission = 20;          // past the tutorial's locks, like free play
            Session.StationIndex = station;
            Session.PreviousStationIndex = -1;
            Session.ShipIndex = ship;
            Session.Credits = c.credits;
            Session.Equipment = new List<ItemStack>();
            foreach (var (item, amount) in c.equipment)
                if (ModContent.TryResolveItem(item, out int i) && db.Item(i) != null) Session.Equipment.Add(new ItemStack(i, Mathf.Max(1, amount)));
                else Warn(c.mod, $"{File}: equipment \"{item}\" is no item");
            Session.Cargo = new List<ItemStack>();
            foreach (var (item, amount) in c.cargo)
                if (ModContent.TryResolveItem(item, out int i) && db.Item(i) != null) Session.Cargo.Add(new ItemStack(i, Mathf.Max(1, amount)));
                else Warn(c.mod, $"{File}: cargo \"{item}\" is no item");
            if (c.standing != null) Session.Standing = new[] { c.standing[0], c.standing[1] };
            foreach (var kv in c.raceStanding)
                if (ModRaces.TryResolve(kv.Key, c.mod, out int fr) && ModRaces.Is(fr)) ModRaces.SetStanding(fr, kv.Value);
                else Warn(c.mod, $"{File}: raceStanding \"{kv.Key}\" is no mod race (the game's four are \"standing\")");
            Session.VisitedStations = new HashSet<int> { station };
            Session.SystemVisible = null;
            GalaxyMap.Visibility(db);              // the campaign's galaxy (GalaxyMap hides the rest)
            var sys = db.Stations.Find(s => s.index == station)?.system ?? -1;
            if (sys >= 0 && sys < Session.SystemVisible.Length) Session.SystemVisible[sys] = true;
            if (!string.IsNullOrEmpty(c.quest))
            {
                // The campaign's story: a quest from the start, run once the station is up (like a loaded save's).
                Session.GraphQuests.Add(new GraphQuestState { name = c.quest, kind = GraphQuestState.KindQuest, title = c.Name });
                Data.GraphQuestState.RestorePending = true;
            }
            Debug.Log($"Mods: campaign {c.Id} starts at station {station}, ship {ship}, {c.credits} credits");
            return "Station";
        }
    }
}
