// ModCharacters.cs
// Remake mods: characters of their own for conversations, the radio, questions and bar mission clients (a mod's
// characters.json): a name (per language), a portrait PNG drawn in the game's 160 x 200 portrait box (4:5; 320 x 400
// stays sharp in HD, scaled to cover the box, its top kept), over the game's portrait backdrop (a dark, half-transparent
// fill: only through the PNG's transparent parts) and under its "frame" (in fact a faint scanline overlay, alpha <= 27)
// (both optional), mirrored on request, and a race / gender for dialogue identity and the default bar-visitor figure.
// "silhouette" can independently choose a built-in visitor mesh; a custom race's own races.json "visitor" GLB is used when
// silhouette is omitted. "replaces": a story character (by name or number) whose
// portrait, and name when it has one (StoryTable.SpeakerName), it takes everywhere, the game's own story included.
// A speaker names one by "mod_id:character_id", by its id or by its name (NetAdmin.ResolveSpeaker; "vega as Bob" renames
// it like a story speaker); it travels to every game as the speaker spec "-3 US rename US key" (SpeakerSpec), and each game
// draws it from its own copy of the mod (multiplayer sessions share the host's mods). Portraits load in the background
// with the mods (Preload, ModLoading), else on first use.

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModCharacters
    {
        public const string File = "characters.json";

        public sealed class Def
        {
            public ModInfo mod;
            public string id, key, portrait;
            public Dictionary<string, string> name;
            public int race;            // game race or ModRaces number
            public bool hasRace;      // false: inherit the event client's race
            public bool hasGender;    // false: inherit the event client's gender
            public string silhouette; // optional lounge visitor mesh, independent of race/name colour
            public bool male = true, mirrored, frame = true, background = true;
            public string replaces;     // a story speaker's name or number
            public int replacesSpeaker = -2;   // resolved on first use (-1 none)

            public bool HasName => name != null && name.Values.Any(n => !string.IsNullOrEmpty(n));

            /// <summary>The name in the current language (else English, else any).</summary>
            public string Name
            {
                get
                {
                    if (name == null || name.Count == 0) return id;
                    if (name.TryGetValue(Localization.Language, out var s) || name.TryGetValue("en", out s) || name.TryGetValue("", out s)) return s;
                    return name.Values.First();
                }
            }
        }

        static readonly HashSet<string> Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "id", "name", "portrait", "race", "gender", "silhouette", "mirrored", "frame", "background", "replaces" };

        static readonly Dictionary<string, Def> defs = new Dictionary<string, Def>(StringComparer.OrdinalIgnoreCase);
        static int parsedRevision = -1, loading;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { defs.Clear(); parsedRevision = -1; loading = 0; }

        /// <summary>Every character of the mods that are on.</summary>
        public static IReadOnlyCollection<Def> All()
        {
            if (parsedRevision != ModManager.Revision)
            {
                parsedRevision = ModManager.Revision;
                defs.Clear();
                foreach (var mod in ModManager.Active)
                {
                    try { Read(mod); }
                    catch (ModJsonException e) { Warn(mod, e.Message); }
                }
            }
            return defs.Values;
        }

        static void Read(ModInfo mod)
        {
            var token = ModJson.Read(mod.Source, File);
            if (token == null) return;
            if (!(token is JArray list)) throw new ModJsonException($"{File}: must be a list [ {{ ... }}, ... ]");
            foreach (var t in list)
            {
                string where = ModJson.Where(t, File);
                if (!(t is JObject o)) throw new ModJsonException($"{where}: each character must be an object {{ ... }}");
                foreach (var prop in o.Properties())
                    if (!Fields.Contains(prop.Name)) Warn(mod, $"{ModJson.Where(prop, File)}: unknown field \"{prop.Name}\" (ignored)");
                string id = ModJson.Str(o, "id"), portrait = ModJson.Str(o, "portrait");
                if (!ModManifest.ValidId(id ?? "")) throw new ModJsonException($"{where}: \"id\" is missing or uses more than a-z, 0-9, _ and -");
                if (string.IsNullOrEmpty(portrait)) throw new ModJsonException($"{where}: \"portrait\" (a PNG) is missing");
                if (!mod.Source.Exists(portrait)) throw new ModJsonException($"{where}: the portrait {portrait} isn't in the mod");
                var d = new Def
                {
                    mod = mod, id = id, key = mod.Id + ":" + id, portrait = portrait, name = ModJson.Text(o, "name"),
                    mirrored = ModJson.Bool(o, "mirrored", false, File),
                    frame = ModJson.Bool(o, "frame", true, File),
                    background = ModJson.Bool(o, "background", true, File),
                    replaces = ModJson.Str(o, "replaces"),
                    silhouette = ModJson.Str(o, "silhouette"),
                };
                string race = ModJson.Str(o, "race");
                if (race != null)
                {
                    if (!ModRaces.TryResolve(race, mod, out d.race))
                        throw new ModJsonException($"{where}: \"race\" {race} isn't a game or mod race");
                    d.hasRace = true;
                }
                if (d.silhouette != null)
                {
                    d.silhouette = d.silhouette.Trim().ToLowerInvariant();
                    if (!new[] { "terran", "vossk", "nivelian", "midorian", "void", "cyborg", "octopod", "bobolian", "grey" }.Contains(d.silhouette))
                        throw new ModJsonException($"{where}: \"silhouette\" must be terran, vossk, nivelian, midorian, void, cyborg, octopod, bobolian or grey");
                }
                string gender = ModJson.Str(o, "gender");
                if (gender != null)
                {
                    if (!gender.Equals("male", StringComparison.OrdinalIgnoreCase) && !gender.Equals("female", StringComparison.OrdinalIgnoreCase))
                        throw new ModJsonException($"{where}: \"gender\" is male or female");
                    d.hasGender = true;
                    d.male = gender.Equals("male", StringComparison.OrdinalIgnoreCase);
                }
                if (defs.ContainsKey(d.key)) throw new ModJsonException($"{where}: the id \"{id}\" is used twice");
                defs[d.key] = d;
            }
        }

        /// <summary>A character by key ("mod:id"), else by its id, else by its name in any language (null: none, or
        /// several by that id / name).</summary>
        public static Def Find(string who)
        {
            if (string.IsNullOrWhiteSpace(who)) return null;
            who = who.Trim();
            var all = All();
            if (defs.TryGetValue(who, out var exact)) return exact;
            var byId = all.Where(d => d.id.Equals(who, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byId.Count == 1) return byId[0];
            var byName = all.Where(d => d.name != null && d.name.Values.Any(n => n.Equals(who, StringComparison.OrdinalIgnoreCase))).ToList();
            return byName.Count == 1 ? byName[0] : null;
        }

        /// <summary>The character that takes this story speaker's portrait ("replaces"), or null.</summary>
        public static Def ReplacementFor(int speaker)
        {
            if (speaker < 0) return null;
            Def found = null;
            foreach (var d in All())
            {
                if (d.replaces == null) continue;
                if (d.replacesSpeaker == -2) d.replacesSpeaker = ResolveSpeaker(d.replaces);
                if (d.replacesSpeaker == speaker) found = d;   // the later mod in the load order wins
            }
            return found;
        }

        static int ResolveSpeaker(string s)
        {
            if (int.TryParse(s, out int n)) return n >= 0 && n < StoryTable.SpeakerCount ? n : -1;
            for (int i = 0; i < StoryTable.SpeakerCount; i++)
            {
                string name = StoryTable.OriginalSpeakerName(i);   // not the replaced one: that asks this
                if (!string.IsNullOrEmpty(name) && (name.Equals(s, StringComparison.OrdinalIgnoreCase) || name.Split(' ')[0].Equals(s, StringComparison.OrdinalIgnoreCase))) return i;
            }
            return -1;
        }

        /// <summary>The character's portrait texture (null: unreadable; decoded on first use when Preload hasn't).</summary>
        public static Texture2D Portrait(Def d)
        {
            if (d == null) return null;
            var t = ModMaterials.Texture(d.mod, d.portrait, false);
            if (t != null) t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        /// <summary>Starts decoding every character's portrait in the background (the main menu's start, ModLoading).</summary>
        public static async void Preload()
        {
            foreach (var d in All().ToList())
            {
                loading++;
                try { await ModMaterials.PreloadTexture(d.mod, d.portrait, false); }
                catch (Exception e) { if (!ModFileException.IsFileError(e)) Debug.LogException(e); else Debug.LogWarning($"Mods: {d.mod.Id}: {e.Message}"); }
                finally { loading--; }
            }
        }

        public static bool Ready => loading == 0;

        static void Warn(ModInfo mod, string message)
        {
            if (!mod.Warnings.Contains(message)) mod.Warnings.Add(message);
            Debug.LogWarning($"Mods: {mod.Id}: {message}");
        }
    }
}
