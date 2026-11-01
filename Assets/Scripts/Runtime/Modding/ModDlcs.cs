// ModDlcs.cs
// Remake mods: a mod's DLC (dlc.json): a story that goes on after the game's own end, like Valkyrie after the main game and
// Supernova after Valkyrie:
//   {
//     "name": "The Kepler Incident" | { "en": ..., "de": ... },  "description": ...,  "image": "dlc.png" (the card's art, 290 x 448
//     or a multiple), "imageHover": "dlc_hover.png", "showTitle": true,
//     "quest": "graph_name"  (required: the DLC's story, an event graph started as a quest),
//     "index": 1  (optional: its place among the DLCs, 1 = right after Supernova; default after those that give one),
//     "intro": "text" | { "en": ... },  "introSpeaker": 6  (optional: the call that opens it, a story speaker's one page),
//     "startStation", "startShip", "credits", "equipment", "cargo"  (optional, its card's new game: the finished game's own
//                              end otherwise: docked at Maissa in Supernova's ship and gear)
//   }
// The DLCs play one after the other in one order (All): the player's own (the Mods browser's DLC place, Earlier / Later,
// PlayerPrefs "dlc_order"), else each one's "index", then the load order. Their cards sit with the game's three campaigns,
// after Supernova, in that order, before the mods' own campaigns. Picking one starts a game at the end of the whole story
// (Supernova's start, then every step to 162) with its quest pending (the DLCs before it count as played). In a game that
// has reached the end (Story.Index 162, not free play), the first DLC not played yet starts in free flight like the
// add-ons' calls (StorySpace: its intro, then the quest), the next once that one's quest is done. Single player only
// (quests don't run in a multiplayer session).

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModDlcs
    {
        public const string File = "dlc.json";
        /// <summary>Where the game's own story ends (step 162 docks the player at Maissa).</summary>
        public const int EndStation = 93;

        static readonly HashSet<string> Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "name", "description", "image", "imageHover", "showTitle", "quest", "index", "intro", "introSpeaker",
            "startStation", "startShip", "credits", "equipment", "cargo",
        };

        public class Def
        {
            public ModInfo mod;
            public Dictionary<string, string> name, description, intro;
            public string image, imageHover, quest, startStation, startShip;
            public bool showTitle = true;
            public int introSpeaker;
            /// <summary>The modder's place among the DLCs (1 = first after Supernova), int.MaxValue = none given.</summary>
            public int index = int.MaxValue;
            public int credits = -1;   // -1 = what the finished game has
            public List<(string item, int amount)> equipment, cargo;   // null = the finished game's
            public string Id => mod.Id;
            public string Name => ModManifest.Pick(name) ?? mod.Name;
            public string Description => ModManifest.Pick(description) ?? mod.Manifest?.Description ?? "";
            public string Intro => ModManifest.Pick(intro);
        }

        static readonly List<Def> defs = new List<Def>();
        static int parsedRevision = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { defs.Clear(); parsedRevision = -1; orderedFor = null; }

        /// <summary>PlayerPrefs: the player's DLC order (mod ids, one per line), set in the Mods browser.</summary>
        public const string OrderKey = "dlc_order";

        /// <summary>The DLCs of the mods that are on, in the order they play: the player's order (OrderKey) for those in it,
        /// then by "index", then in load order.</summary>
        public static List<Def> All()
        {
            if (parsedRevision == ModManager.Revision && orderedFor == PlayerPrefs.GetString(OrderKey, "")) return defs;
            parsedRevision = ModManager.Revision;
            orderedFor = PlayerPrefs.GetString(OrderKey, "");
            var read = new List<Def>();
            foreach (var mod in ModManager.Active)
            {
                try { var d = Read(mod); if (d != null) read.Add(d); }
                catch (ModJsonException e) { Warn(mod, e.Message); }
            }
            defs.Clear();
            defs.AddRange(Order(read, PlayerOrder()));
            return defs;
        }

        static string orderedFor;

        static List<string> PlayerOrder() =>
            PlayerPrefs.GetString(OrderKey, "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

        /// <summary>The play order: those in the player's order first, as it has them; the rest by their "index", then as
        /// read (load order). Plain, for the tests.</summary>
        public static List<Def> Order(List<Def> read, List<string> playerOrder)
        {
            int Rank(Def d)
            {
                int i = playerOrder == null ? -1 : playerOrder.FindIndex(x => string.Equals(x, d.Id, StringComparison.OrdinalIgnoreCase));
                return i;
            }
            return read.Select((d, n) => (d, n))
                       .OrderBy(x => Rank(x.d) < 0 ? 1 : 0)
                       .ThenBy(x => Rank(x.d) < 0 ? 0 : Rank(x.d))
                       .ThenBy(x => x.d.index)
                       .ThenBy(x => x.n)
                       .Select(x => x.d).ToList();
        }

        /// <summary>The Mods browser: a DLC one place earlier (-1) or later (+1) in the play order (the whole order saved).</summary>
        public static void Move(string id, int delta)
        {
            var order = All().Select(d => d.Id).ToList();
            int i = order.FindIndex(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
            int j = i + Math.Sign(delta);
            if (i < 0 || j < 0 || j >= order.Count) return;
            (order[i], order[j]) = (order[j], order[i]);
            PlayerPrefs.SetString(OrderKey, string.Join("\n", order));
            PlayerPrefs.Save();
        }

        /// <summary>A mod's DLC (it is on and has a dlc.json), null = none.</summary>
        public static Def Of(string modId) => All().Find(d => string.Equals(d.Id, modId, StringComparison.OrdinalIgnoreCase));

        /// <summary>A DLC's place in the play order, 1 = right after Supernova (0 = not a DLC that is on).</summary>
        public static int Place(string modId) => All().FindIndex(d => string.Equals(d.Id, modId, StringComparison.OrdinalIgnoreCase)) + 1;

        static Def Read(ModInfo mod)
        {
            if (!(ModJson.Read(mod.Source, File) is JToken token)) return null;
            if (!(token is JObject o)) throw new ModJsonException($"{File}: must be an object {{ ... }}");
            foreach (var prop in o.Properties())
                if (!Fields.Contains(prop.Name)) Warn(mod, $"{ModJson.Where(prop, File)}: unknown field \"{prop.Name}\" (ignored)");
            var d = new Def
            {
                mod = mod, name = ModJson.Text(o, "name"), description = ModJson.Text(o, "description"), intro = ModJson.Text(o, "intro"),
                image = ModJson.Str(o, "image"), imageHover = ModJson.Str(o, "imageHover"), showTitle = ModJson.Bool(o, "showTitle", true, File),
                quest = ModJson.Str(o, "quest"),
                startStation = ModJson.Str(o, "startStation"), startShip = ModJson.Str(o, "startShip"),
                introSpeaker = Mathf.Max(0, ModJson.Int(o, "introSpeaker", 0, File)),
                credits = ModJson.Get(o, "credits") != null ? Mathf.Max(0, ModJson.Int(o, "credits", 0, File)) : -1,
            };
            if (string.IsNullOrEmpty(d.quest)) throw new ModJsonException($"{File}: \"quest\" is missing (the DLC's story, an event graph)");
            if (ModJson.Get(o, "index") != null) d.index = Mathf.Max(1, ModJson.Int(o, "index", 1, File));
            if (ModJson.Get(o, "equipment") is JArray eq)
            {
                d.equipment = new List<(string, int)>();
                foreach (var t in eq) d.equipment.Add(t is JObject e ? ((string)e["item"], e["amount"] != null ? (int)e["amount"] : 1) : ((string)t, 1));
            }
            if (ModJson.Get(o, "cargo") is JArray ca)
            {
                d.cargo = new List<(string, int)>();
                foreach (var t in ca) if (t is JObject c) d.cargo.Add(((string)c["item"], c["amount"] != null ? (int)c["amount"] : 1));
            }
            return d;
        }

        static void Warn(ModInfo mod, string message)
        {
            if (!mod.Warnings.Contains(message)) mod.Warnings.Add(message);
            Debug.LogWarning($"Mods: {mod.Id}: {message}");
        }

        public static Texture2D Image(Def d) => string.IsNullOrEmpty(d.image) ? null : ModMaterials.Texture(d.mod, d.image, false, true);
        public static Texture2D ImageHover(Def d) => string.IsNullOrEmpty(d.imageHover) ? null : ModMaterials.Texture(d.mod, d.imageHover, false, true);

        // ---- this game's DLCs ---------------------------------------------------------------------------------------

        /// <summary>A DLC's story is played in this game (its quest done).</summary>
        public static bool Played(Def d) => d != null && Session.GraphQuestsDone.Contains(d.quest);

        /// <summary>A DLC's story is under way in this game.</summary>
        public static bool Running(Def d) => d != null && Session.GraphQuests.Exists(q => q.name == d.quest);

        /// <summary>The game's own story is over: step 162 reached in a story game (not free play, not a mod campaign).</summary>
        public static bool StoryFinished => !Session.FreePlay && Story.Index >= Story.LastIndex && string.IsNullOrEmpty(Session.ModCampaign);

        /// <summary>The DLC that starts next in this game (StorySpace, in free flight), null = none: the story is finished, no DLC
        /// runs, and this is the first in the play order not played yet.</summary>
        public static Def Next()
        {
            if (!StoryFinished) return null;
            var all = All();
            if (all.Exists(Running)) return null;   // one at a time
            return all.Find(d => !Played(d));
        }

        /// <summary>The DLCs that play before 'd' (earlier in the play order).</summary>
        public static List<Def> Before(Def d)
        {
            var all = All();
            int i = all.IndexOf(d);
            return i <= 0 ? new List<Def>() : all.GetRange(0, i);
        }

        /// <summary>MainMenu: a new game from the DLC's card (after Session.ResetNewGame and the difficulty / economy): the end of
        /// the whole story (Supernova's start, every step to 162), docked at Maissa or the DLC's start, the DLCs before it
        /// played, its quest pending; the scene to load ("Station"). Null with 'error' when it can't start.</summary>
        public static string Start(Database db, Def d, out string error)
        {
            error = null;
            int station = EndStation, ship = -1;
            if (!string.IsNullOrEmpty(d.startStation) && (!ModWorld.TryResolveStation(d.startStation, out station) || db.Stations.Find(s => s.index == station) == null))
            { error = $"{d.Name}: startStation \"{d.startStation}\" is no station"; return null; }
            if (!string.IsNullOrEmpty(d.startShip) && (!ModContent.TryResolveShip(d.startShip, out ship) || db.Ship(ship) == null))
            { error = $"{d.Name}: startShip \"{d.startShip}\" is no ship"; return null; }
            Story.StartCampaign(db, Campaign.Supernova);   // Supernova's start: its ship and gear at step 84
            while (Story.Index < Story.LastIndex)
            {
                int before = Story.Index;
                Story.Advance(db);                          // every step's own effects (systems, blueprints, items)
                if (Story.Index <= before) break;
            }
            Session.StationIndex = station;
            Session.PreviousStationIndex = -1;
            Session.LaunchedFromStation = Session.ArrivedByTravel = false;
            Session.VisitedStations.Add(station);
            if (ship >= 0) Session.ShipIndex = ship;
            if (d.credits >= 0) Session.Credits = d.credits;
            if (d.equipment != null)
            {
                Session.Equipment = new List<ItemStack>();
                foreach (var (item, amount) in d.equipment)
                    if (ModContent.TryResolveItem(item, out int i) && db.Item(i) != null) Session.Equipment.Add(new ItemStack(i, Mathf.Max(1, amount)));
                    else Warn(d.mod, $"{File}: equipment \"{item}\" is no item");
            }
            if (d.cargo != null)
            {
                Session.Cargo = new List<ItemStack>();
                foreach (var (item, amount) in d.cargo)
                    if (ModContent.TryResolveItem(item, out int i) && db.Item(i) != null) Session.Cargo.Add(new ItemStack(i, Mathf.Max(1, amount)));
                    else Warn(d.mod, $"{File}: cargo \"{item}\" is no item");
            }
            foreach (var b in Before(d)) Session.GraphQuestsDone.Add(b.quest);   // the DLCs before it count as played
            Session.GraphQuests.Add(new GraphQuestState { name = d.quest, kind = GraphQuestState.KindQuest, title = d.Name });
            GraphQuestState.RestorePending = true;   // started once the station is up (like a loaded save's)
            Debug.Log($"Mods: DLC {d.Id} starts at step {Story.Index}, station {station}");
            return "Station";
        }
    }
}
