// ModRaces.cs
// Remake mods: races of a mod's own (its races.json), beside the game's (Terran, Vossk, Nivelian, Midorian, the pirates,
// the Void, the Specters). Each mod race is a race number past the game's (FirstRace and up: the game's are 0-10), numbered
// like systems (the registry in single player, afresh in a session), so every "race" in the game (a system's owner, an NPC
// ship's side, a lounge visitor's diplomat) can be one. A mod race has its own standing with the player
// (Session.ModRaceStanding, one value -100..100 like one end of a game axis: hostile at -71 and below, a friend at 71 and
// above), its relations to the other races (who fights whom, the NPCs' rule; Standing.RacesHostile), and a "look": the
// game race whose hangar, bar, visitors, music and NPC guns it borrows where the game has a table per race (Look).
// (Not the multiplayer factions, NetFactions: players' groups. Mods called these "factions" until 2026-10-28: the old
// factions.json, the save's factionStanding and the registry's "factions" are still read.)
//
// races.json: [ { "id", "name" (or per language), "description", "icon" (a PNG emblem, like the game's race emblems),
//   "color" [r, g, b] 0..1, "look": terran | vossk | nivelian | midorian | pirates | void (or 0-3, 8, 9; default terran),
//   "standing": the player's start (-100..100, default 0), "hostile": true (always the player's enemy, whatever the
//   standing, like the game's pirates; and the enemy of every race it doesn't name a friend), "diplomats": false (no
//   lounge diplomat restores it; default true, false for a hostile one), "relations": { "<a mod race's id, mod_id:id, or a
//   game race word: terran, vossk, nivelian, midorian, pirates, void, specters>": "friend" | "neutral" | "enemy" },
//   "gun": an item (its NPC fighters' gun look and sound; default the look race's) } ]
// A relation one mod race leaves out is the other's; neither names it: enemies if either is "hostile" or the other side is
// the game's pirates, Void or Specters (who fight everyone), else neutral.

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModRaces
    {
        public const string File = "races.json";
        /// <summary>The file's name before mod races were called so (still read when a mod has no races.json).</summary>
        public const string OldFile = "factions.json";
        /// <summary>The first race number a mod race gets (the game's races are 0-10; the rest left free).</summary>
        public const int FirstRace = 32;
        public const int Enemy = -1, Neutral = 0, Friend = 1;

        public class Def
        {
            public ModInfo mod;
            public JObject json;
            public string where, localId, key, icon, gun;
            public Dictionary<string, string> name, description;
            public Color color = Color.white;
            public int look, start, race = -1, gunItem = -1;
            public bool hostile, diplomats = true;
            /// <summary>"relations" as written (race references -> -1 / 0 / 1); resolved in Relations.</summary>
            public Dictionary<string, int> relationRefs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            /// <summary>Resolved: race number -> -1 / 0 / 1.</summary>
            public readonly Dictionary<int, int> relations = new Dictionary<int, int>();
        }

        static readonly HashSet<string> Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "id", "name", "description", "icon", "color", "look", "standing", "hostile", "diplomats", "relations", "gun" };

        static int mappedRevision = -1;
        static readonly Dictionary<string, Def> byKey = new Dictionary<string, Def>(StringComparer.Ordinal);
        static readonly Dictionary<int, Def> byRace = new Dictionary<int, Def>();
        static readonly Dictionary<int, string> missing = new Dictionary<int, string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            mappedRevision = -1; byKey.Clear(); byRace.Clear(); missing.Clear();
            foreach (var e in emblems.Values) Gone(e.tex);
            emblems.Clear();
        }

        // ---- parsing (ModContent.Parse) --------------------------------------------------------------------------------

        public static void Parse(ModInfo mod, List<Def> into)
        {
            string File = !mod.Source.Exists(ModRaces.File) && mod.Source.Exists(OldFile) ? OldFile : ModRaces.File;
            var token = ModJson.Read(mod.Source, File);
            if (token == null) return;
            if (!(token is JArray list)) throw new ModJsonException($"{File}: must be a list [ {{ ... }}, ... ]");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in list)
            {
                string where = ModJson.Where(t, File);
                if (!(t is JObject o)) throw new ModJsonException($"{where}: each entry must be an object {{ ... }}");
                var d = new Def { mod = mod, json = o, where = where, localId = ModJson.Str(o, "id"), name = ModJson.Text(o, "name"),
                                  description = ModJson.Text(o, "description"), icon = ModJson.Str(o, "icon"), gun = ModJson.Str(o, "gun") };
                if (d.localId == null) throw new ModJsonException($"{where}: a race needs \"id\"");
                if (!ModManifest.ValidId(d.localId)) throw new ModJsonException($"{where}: the id \"{d.localId}\" may only use a-z, 0-9, _ and -");
                if (!ids.Add(d.localId)) throw new ModJsonException($"{where}: the id \"{d.localId}\" is used twice");
                if (d.name == null) throw new ModJsonException($"{where}: a race needs \"name\"");
                d.key = mod.Id + ":" + d.localId;
                foreach (var prop in o.Properties())
                    if (!Fields.Contains(prop.Name)) mod.Warnings.Add($"{ModJson.Where(prop, File)}: unknown field \"{prop.Name}\" (ignored)");
                if (d.icon != null && !mod.Source.Exists(d.icon)) throw new ModJsonException($"{where}: the icon {d.icon} isn't in the mod");
                if (ModJson.Get(o, "color") is JToken c)
                {
                    if (!(c is JArray a) || a.Count != 3) throw new ModJsonException($"{where}: \"color\" must be [r, g, b] (0..1)");
                    d.color = new Color(Mathf.Clamp01((float)a[0]), Mathf.Clamp01((float)a[1]), Mathf.Clamp01((float)a[2]));
                }
                d.look = ModJson.Has(o, "look") ? GameRace(ModJson.Get(o, "look")) : 0;
                if (d.look < 0 || (d.look > 3 && d.look != 8 && d.look != 9))
                    throw new ModJsonException($"{where}: \"look\" must be terran, vossk, nivelian, midorian, pirates or void");
                d.start = Mathf.Clamp(ModJson.Int(o, "standing", 0, File), -100, 100);
                d.hostile = ModJson.Bool(o, "hostile", false, File);
                d.diplomats = ModJson.Bool(o, "diplomats", !d.hostile, File);
                if (ModJson.Get(o, "relations") is JToken rel)
                {
                    if (!(rel is JObject ro)) throw new ModJsonException($"{where}: \"relations\" must be {{ \"race\": \"friend\" | \"neutral\" | \"enemy\" }}");
                    foreach (var p in ro.Properties())
                    {
                        int v = ((string)p.Value ?? "").Trim().ToLowerInvariant() switch { "friend" => Friend, "neutral" => Neutral, "enemy" => Enemy, _ => 2 };
                        if (v == 2) throw new ModJsonException($"{ModJson.Where(p, File)}: a relation is \"friend\", \"neutral\" or \"enemy\"");
                        d.relationRefs[p.Name] = v;
                    }
                }
                into.Add(d);
            }
        }

        /// <summary>A game race by its word or number: terran / vossk / nivelian / midorian / pirates / void / specters, 0-10;
        /// -1 otherwise.</summary>
        public static int GameRace(JToken t)
        {
            if (t == null) return -1;
            if (t.Type == JTokenType.Integer) { int v = (int)t; return v >= 0 && v <= 10 ? v : -1; }
            return GameRaceWord((string)t);
        }

        public static int GameRaceWord(string w) => (w ?? "").Trim().ToLowerInvariant() switch
        {
            "terran" or "terrans" => 0, "vossk" => 1, "nivelian" or "nivelians" => 2, "midorian" or "midorians" => 3,
            "pirate" or "pirates" => 8, "void" => 9, "specter" or "specters" => 10, _ => -1,
        };

        // ---- numbering and relations -----------------------------------------------------------------------------------

        static void EnsureMapping()
        {
            if (mappedRevision == ModManager.Revision) return;
            mappedRevision = ModManager.Revision;
            byKey.Clear(); byRace.Clear(); missing.Clear();
            bool session = ModManager.InSession;
            int next = FirstRace;
            foreach (var mod in ModManager.Active)
                foreach (var d in ModContent.Parse(mod).races)
                {
                    d.race = session ? next++ : ModRegistry.Assign(ModRegistry.Kind.Race, d.key, FirstRace);
                    byKey[d.key] = d; byRace[d.race] = d;
                }
            if (!session)
                foreach (var kv in ModRegistry.All(ModRegistry.Kind.Race))
                    if (!byKey.ContainsKey(kv.Key)) missing[kv.Value] = kv.Key;
            // the relations and guns by number, now that every mod race has one
            foreach (var d in byKey.Values)
            {
                d.relations.Clear();
                foreach (var r in d.relationRefs)
                {
                    if (TryResolve(r.Key, d.mod, out int race)) d.relations[race] = r.Value;
                    else d.mod.Warnings.Add($"{d.where}: relation to \"{r.Key}\": no such race (ignored)");
                }
                d.gunItem = -1;
                if (d.gun != null && ModContent.TryResolveItem(d.gun.IndexOf(':') < 0 && !int.TryParse(d.gun, out _) ? d.mod.Id + ":" + d.gun : d.gun, out int gi))
                    d.gunItem = gi;
            }
        }

        /// <summary>A race by reference: "mod_id:id", a race id of 'mod' (or of any mod that is on), a game race word or
        /// number, or a mod race's number.</summary>
        public static bool TryResolve(string reference, ModInfo mod, out int race)
        {
            race = -1;
            if (string.IsNullOrWhiteSpace(reference)) return false;
            EnsureMapping();
            if (int.TryParse(reference, out race)) return race >= 0 && race <= 10 || byRace.ContainsKey(race);
            if ((race = GameRaceWord(reference)) >= 0) return true;
            if (reference.IndexOf(':') >= 0) { if (byKey.TryGetValue(reference, out var d)) { race = d.race; return true; } return false; }
            if (mod != null && byKey.TryGetValue(mod.Id + ":" + reference, out var own)) { race = own.race; return true; }
            var any = byKey.Values.FirstOrDefault(x => x.localId == reference);
            if (any != null) { race = any.race; return true; }
            race = -1;
            return false;
        }

        /// <summary>A race number that is a mod race (of a mod that is on, or a placeholder of one that is off).</summary>
        public static bool Is(int race) => race >= FirstRace;

        public static Def Get(int race) { if (race < FirstRace) return null; EnsureMapping(); return byRace.TryGetValue(race, out var d) ? d : null; }
        public static Def Get(string key) { EnsureMapping(); return key != null && byKey.TryGetValue(key, out var d) ? d : null; }

        /// <summary>Every mod race of the mods that are on, in load order.</summary>
        public static IEnumerable<Def> All { get { EnsureMapping(); return byKey.Values.ToList(); } }

        /// <summary>The game race whose tables (hangar, bar, visitors, music, guns) a race borrows: a mod race's "look", one
        /// whose mod is off Terran's, a game race itself.</summary>
        public static int Look(int race) => race < FirstRace ? race : Get(race)?.look ?? 0;

        /// <summary>A mod race's name in the current language (its key for one whose mod is off).</summary>
        public static string Name(int race)
        {
            var d = Get(race);
            if (d == null) { EnsureMapping(); return missing.TryGetValue(race, out var k) ? k : ""; }
            return TextOf(d, d.name, "name");
        }

        public static string Description(int race)
        {
            var d = Get(race);
            return d?.description == null ? "" : TextOf(d, d.description, "description");
        }

        static string TextOf(Def d, Dictionary<string, string> t, string kind)
        {
            string lang = Localization.Language;
            var text = ModContent.Parse(d.mod).text;
            if (t.TryGetValue(lang, out var s)) return s;
            if (text.TryGetValue(lang, out var table) && (table.TryGetValue($"races.{d.localId}.{kind}", out s)
                                                           || table.TryGetValue($"factions.{d.localId}.{kind}", out s))) return s;
            if (t.TryGetValue("en", out s) || t.TryGetValue("", out s)) return s;
            return t.Values.FirstOrDefault() ?? d.key;
        }

        /// <summary>A mod race's emblem ("icon", a PNG in its mod), null without one (callers show the look race's).</summary>
        public static Texture2D Icon(int race)
        {
            var d = Get(race);
            return d?.icon == null ? null : Emblem(d.mod, d.icon);
        }

        static void Gone(Texture2D t) { if (t == null) return; if (Application.isPlaying) UnityEngine.Object.Destroy(t); else UnityEngine.Object.DestroyImmediate(t); }

        class EmblemEntry { public int revision; public byte[] bytes; public Texture2D tex; }
        static readonly Dictionary<string, EmblemEntry> emblems = new Dictionary<string, EmblemEntry>();

        /// <summary>An emblem PNG of a mod (null: none or unreadable). Its own cache, not ModMaterials': that one destroys its
        /// textures at every mods revision (each Mod Builder reload), which blanked the emblems a race list or the builder's
        /// star map still showed (Discord 2026-11-11). The same texture is kept while the file's bytes are unchanged.</summary>
        public static Texture2D Emblem(ModInfo mod, string path)
        {
            if (mod == null || string.IsNullOrEmpty(path)) return null;
            string key = mod.Id + "|" + ModSource.Normalise(path).ToLowerInvariant();
            if (emblems.TryGetValue(key, out var e) && e.revision == ModManager.Revision) return e.tex;
            byte[] bytes = null;
            try { bytes = mod.Source.ReadBytes(path); }
            catch (Exception x) when (ModFileException.IsFileError(x)) { }
            if (e != null && bytes != null && e.bytes != null && bytes.AsSpan().SequenceEqual(e.bytes)) { e.revision = ModManager.Revision; return e.tex; }
            // shortcut: a changed file's old texture is destroyed; a screen still showing it blanks until it is built again.
            Gone(e?.tex);
            var t = bytes == null ? null : ImageImport.Decode(bytes, path, false, true, out _);
            if (t != null) { t.name = mod.Id + ":" + path; t.filterMode = FilterMode.Trilinear; t.anisoLevel = 8; }
            emblems[key] = new EmblemEntry { revision = ModManager.Revision, bytes = bytes, tex = t };
            return t;
        }

        /// <summary>How race 'a' stands toward race 'b' (-1 enemy, 0 neutral, 1 friend), at least one of them a mod race.</summary>
        public static int Relation(int a, int b)
        {
            if (a == b) return Friend;
            var da = Get(a); var db = Get(b);
            if (da != null && da.relations.TryGetValue(b, out int r)) return r;
            if (db != null && db.relations.TryGetValue(a, out r)) return r;
            if ((da != null && da.hostile) || (db != null && db.hostile)) return Enemy;
            int game = da != null ? b : a;   // the other side, when it is a game race
            if (game == 8 || game == 9 || game == 10) return Enemy;
            return Neutral;
        }

        /// <summary>The player's standing toward a mod race (its "standing" until it changes; Session.ModRaceStanding).</summary>
        public static int Standing(int race) => StandingIn(race, Session.ModRaceStanding);

        /// <summary>Standing in a given set (mod race key -> value; multiplayer: another player's, NetPlayer.ModRaceStandings).</summary>
        public static int StandingIn(int race, IReadOnlyDictionary<string, int> standings)
        {
            var d = Get(race);
            if (d == null) return 0;
            return standings != null && standings.TryGetValue(d.key, out int v) ? v : d.start;
        }

        public static void SetStanding(int race, int value)
        {
            var d = Get(race);
            if (d == null) return;
            Session.ModRaceStanding[d.key] = Mathf.Clamp(value, -100, 100);
            StandingRevision++;
        }

        /// <summary>Counts SetStanding's changes (multiplayer: NetModRaceStandings shares them again).</summary>
        public static int StandingRevision { get; private set; }

        /// <summary>The mod races of a system's raids: its "raiders", else the hostile mod races that are the owner's enemies,
        /// else none (the game's pirates).</summary>
        public static List<int> EnemiesOf(int race)
        {
            var l = new List<int>();
            foreach (var d in All) if (d.hostile && d.race != race && Relation(d.race, race) == Enemy) l.Add(d.race);
            return l;
        }
    }
}
