// ModRefs.cs
// Remake-only: what the Mod Builder's pickers offer wherever a mod names other content (a blueprint's product and
// ingredients, a campaign's start ship and station, a quest's speakers, stations and items, a station's system, a
// system's gates...): the game's own, the project's own (by its key "<project id>:<id>") and every other installed mod's
// (by its key). Picking another mod's content makes that mod a dependency of the project (Use: mod.json "dependencies",
// with the version installed now as the lowest), so it is on whenever this mod is (and in the builder at once:
// ModManager.SetBuilderProject turns a project's dependencies on). The other mods' content is read from their files
// (ModContent.Parse, characters.json, interiors.json, events/, music/), whether they are on or not. Remake: races are a
// kind too (RefKind.Race): the game's by word (what races.json relations, systems.json locals / raiders and ships.json
// traffic races take), the project's and other mods' own races (ModRaces) by key. Plain C#.

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using Newtonsoft.Json.Linq;

namespace GoF2Remake.Modding
{
    public enum RefKind { Item, Ship, System, Station, Character, Quest, Music, Interior, Race, NpcShip }

    /// <summary>One thing a picker offers.</summary>
    public sealed class RefEntry
    {
        /// <summary>What the mod's file takes: a game number ("12"), a key ("other_mod:falcon"), a quest's or track's name,
        /// a story speaker's name.</summary>
        public string value;
        public string name, sub;
        /// <summary>"Game", the project's name, or the other mod's name (the picker's sections).</summary>
        public string group;
        /// <summary>The other mod it comes from (a dependency to add), null = the game's or the project's own.</summary>
        public string modId, modVersion;
        /// <summary>The game number for its icon (items, ships: the original it looks like), -1 = none.</summary>
        public int icon = -1;
        /// <summary>A story speaker's number (its portrait), -1 = none.</summary>
        public int speaker = -1;
        /// <summary>The project's own.</summary>
        public bool own;
        /// <summary>Items: the original's type (0 primary, 1 secondary, 2 turret, 3 equipment, 4 commodity), -1 unknown.</summary>
        public int type = -1;
    }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModRefs
    {
        public const string GameGroup = "Game";

        /// <summary>The installed mods a project may use (not itself, not a mod it was before, not a broken one).</summary>
        public static List<ModInfo> OtherMods(ModProject p) =>
            ModManager.Installed.Where(m => m.Manifest != null && !m.Broken && p != null && !string.Equals(m.Id, p.Id, StringComparison.OrdinalIgnoreCase)
                                            && !p.PreviousIds.Contains(m.Id, StringComparer.OrdinalIgnoreCase)).ToList();

        /// <summary>Everything of a kind a picker offers: the game's, the project's own, then each other mod's.</summary>
        public static List<RefEntry> List(RefKind kind, ModProject p)
        {
            var l = new List<RefEntry>();
            Game(kind, l);
            if (p != null) Own(kind, p, l);
            if (p != null)
                foreach (var m in OtherMods(p))
                {
                    try { Mod(kind, m, l); }
                    catch (Exception e) { UnityEngine.Debug.LogWarning($"Mod Builder: {m.Id}: {e.Message}"); }
                }
            return l;
        }

        /// <summary>A picked entry is used: another mod's becomes a dependency. True when the dependencies changed (the caller
        /// lets the game read the project again, which turns the dependency on in the builder).</summary>
        public static bool Use(ModProject p, RefEntry e)
        {
            if (p == null || e == null || string.IsNullOrEmpty(e.modId)) return false;
            return p.AddDependency(e.modId, e.modVersion);
        }

        /// <summary>The mod a key belongs to ("other_mod:x" -> "other_mod"), null for a number, a name or a game model.</summary>
        public static string ModOf(string value)
        {
            if (string.IsNullOrEmpty(value) || GameModels.Is(value)) return null;
            int c = value.IndexOf(':');
            if (c <= 0) return null;
            string mod = value.Substring(0, c);
            return ModManifest.ValidId(mod) ? mod : null;
        }

        /// <summary>A stored reference for people: its name and where it comes from ("Falcon (Ship Pack)").</summary>
        public static string Label(RefKind kind, string value, ModProject p)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (kind == RefKind.Race) value = RaceValue(value, p);
            var e = List(kind, p).FirstOrDefault(x => string.Equals(x.value, value, StringComparison.OrdinalIgnoreCase));
            if (e == null) return value;
            return e.group == GameGroup || e.own ? e.name : $"{e.name} ({e.group})";
        }

        /// <summary>The entry of a stored reference (null: not found, e.g. a mod that isn't installed).</summary>
        public static RefEntry Find(RefKind kind, string value, ModProject p)
        {
            if (string.IsNullOrEmpty(value)) return null;
            if (kind == RefKind.Race) value = RaceValue(value, p);
            return List(kind, p).FirstOrDefault(x => string.Equals(x.value, value, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The game's races as races.json and the race lists name them (ModRaces.GameRaceWord), with their numbers.</summary>
        public static readonly (string word, int race, string name)[] RaceWords =
        {
            ("terran", 0, "Terran"), ("vossk", 1, "Vossk"), ("nivelian", 2, "Nivelian"), ("midorian", 3, "Midorian"),
            ("pirates", 8, "Pirate"), ("void", 9, "Void"), ("specters", 10, "Specters"),
        };

        /// <summary>A race reference as the Race kind's entries have it: a game race's number or word as its word, a mod race
        /// id of the project as its key.</summary>
        public static string RaceValue(string value, ModProject p)
        {
            if (string.IsNullOrEmpty(value)) return value;
            int race = int.TryParse(value, out int n) ? n : ModRaces.GameRaceWord(value);
            foreach (var r in RaceWords) if (r.race == race) return r.word;
            var own = p?.OwnRace(value);
            return own != null ? p.Key(own.Id) : value;
        }

        // ---- the game -------------------------------------------------------------------------------------------

        /// <summary>The race words (stations / systems / traffic): 0 Terran ... 3 Midorian.</summary>
        public static readonly string[] RaceNames = { "Terran", "Vossk", "Nivelian", "Midorian" };

        static void Game(RefKind kind, List<RefEntry> l)
        {
            var db = Database.Shared;
            switch (kind)
            {
                case RefKind.Item:
                    for (int i = 0; i < GameNames.OriginalItems && i < db.Items.Count; i++)
                    {
                        var it = db.Item(i);
                        if (it == null) continue;
                        l.Add(new RefEntry { value = i.ToString(), name = GameNames.Item(i), sub = it.category, group = GameGroup, icon = i, type = it.TypeId });
                    }
                    break;
                case RefKind.Ship:
                    for (int i = 0; i < GameNames.OriginalShips; i++)
                    {
                        if (i == 13 || i == 14 || i == 15 || db.Ship(i) == null || db.ShipAssembly(i, original: true) == null) continue;
                        l.Add(new RefEntry { value = i.ToString(), name = GameNames.Ship(i), group = GameGroup, icon = i });
                    }
                    break;
                case RefKind.System:
                    int systems = ModWorld.OriginalSystemCount > 0 ? ModWorld.OriginalSystemCount : 34;
                    foreach (var s in db.Systems.Where(s => s.index < systems && !string.IsNullOrEmpty(s.name)))
                        l.Add(new RefEntry { value = s.index.ToString(), name = s.name, sub = RaceName(s.raceId), group = GameGroup });
                    break;
                case RefKind.Station:
                    int stations = ModWorld.OriginalStationCount > 0 ? ModWorld.OriginalStationCount : 135;
                    foreach (var s in db.Stations.Where(s => s.index < stations && !string.IsNullOrEmpty(s.name)))
                        l.Add(new RefEntry { value = s.index.ToString(), name = s.name, sub = db.Systems.ElementAtOrDefault(s.system)?.name, group = GameGroup });
                    break;
                case RefKind.Character:
                    l.Add(new RefEntry { value = "player", name = "Keith T. Maxwell (the player)", group = GameGroup, speaker = 0 });
                    var names = Enumerable.Range(0, StoryTable.SpeakerCount).Select(StoryTable.OriginalSpeakerName).ToList();
                    var seen = new HashSet<string>();
                    for (int i = 0; i < names.Count; i++)
                    {
                        string n = names[i];
                        if (string.IsNullOrWhiteSpace(n) || !seen.Add(n)) continue;
                        l.Add(new RefEntry { value = names.Count(x => x == n) == 1 ? n : i.ToString(), name = n, group = GameGroup, speaker = i });
                    }
                    break;
                case RefKind.Music:
                    foreach (var t in GameMusic) l.Add(new RefEntry { value = t, name = t, group = GameGroup });
                    break;
                case RefKind.Race:
                    foreach (var (word, race, name) in RaceWords)
                        l.Add(new RefEntry { value = word, name = Localization.Extra("race" + name, name), sub = race.ToString(), group = GameGroup });
                    break;
                case RefKind.NpcShip:   // npcships.json's game types (ModNpcShips.GameKeys)
                    foreach (var (key, name) in GameNpcShips) l.Add(new RefEntry { value = key, name = name, sub = key, group = GameGroup });
                    break;
            }
        }

        /// <summary>The game's NPC ship types (ModNpcShips.GameKeys) with their names.</summary>
        public static readonly (string key, string name)[] GameNpcShips =
        {
            (ModNpcShips.Battleship, "Terran battleship"), (ModNpcShips.Carrier, "Terran carrier"), (ModNpcShips.VosskBattleship, "Vossk battleship"),
            (ModNpcShips.FreighterKey, "Freighter"), (ModNpcShips.PirateBoss, "Pirate boss"),
        };

        static string RaceName(int race) => race >= 0 && race < RaceNames.Length ? RaceNames[race] : "";

        /// <summary>The game's music tracks (Assets/Audio/MUSIC, DLC_MUSIC, DLC2_MUSIC): what a mod's music/ file of that name
        /// replaces.</summary>
        public static readonly string[] GameMusic =
        {
            "Space_Terraner", "Space_Vossk", "Space_Nivelianer", "Space_Midorianer", "Station_Terraner", "Station_Vossk", "Station_Nivelianer",
            "Station_Midorianer", "Station_Valkyrie", "Space_Battle_Low", "Space_Battle_Medium", "Space_Battle_Full", "Space_Battle_Void",
            "Music_Space_Battle_Full_Loop", "Space_NoCombat_Void", "Space_NoCombat_Valkyrie", "HomeBase_NoCombat", "HomeBase_Station",
            "IntroAtmo_02", "TimeShift_Start", "Errkt_CutSeq_01", "OutroSong_02b", "DeepScienceAttacked_02", "ValkyrieBattlemode_CutSeq_01",
            "20120527_GOF2_Addon_GammarRaySystems", "20120527_GOF2_Addon_StealthFighter1", "20120527_GOF2_Addon_StealthFighter2",
            "20120527_GOF2_Addon_WantedBoardCriminal", "DeepScience_Station_02", "GOF2SN_CS_126_Trunt1_02", "GOF2_DeepScience_Space",
            "GOF2_Mission_102_CutScene", "GOF2_Mission_102_CutScene_Loop", "GOF2_Mission_102_CutScene_Loop_2", "GOF2_SN_CS_1_02",
        };

        // ---- the project's own ------------------------------------------------------------------------------------

        static void Own(RefKind kind, ModProject p, List<RefEntry> l)
        {
            string g = p.Name;
            switch (kind)
            {
                case RefKind.Item:
                    foreach (var it in p.Items.Where(i => !i.IsOverride && i.LocalId != null))
                        l.Add(new RefEntry { value = p.Key(it.LocalId), name = it.Name ?? it.LocalId, group = g, own = true, icon = it.Original,
                                             type = Database.Shared.Item(it.Original)?.TypeId ?? -1 });
                    break;
                case RefKind.Ship:
                    foreach (var s in p.Ships.Where(s => !s.IsOverride && s.LocalId != null))
                        l.Add(new RefEntry { value = p.Key(s.LocalId), name = s.Name ?? s.LocalId, group = g, own = true });
                    break;
                case RefKind.System:
                    foreach (var s in p.Systems.Where(s => !s.IsOverride && s.LocalId != null))
                        l.Add(new RefEntry { value = p.Key(s.LocalId), name = s.Name ?? s.LocalId, group = g, own = true,
                                             sub = s.Race >= 0 ? RaceName(s.Race) : p.OwnRace(s.ModRace)?.Name ?? s.ModRace });
                    break;
                case RefKind.Station:
                    foreach (var s in p.Stations.Where(s => !s.IsOverride && s.LocalId != null))
                        l.Add(new RefEntry { value = p.Key(s.LocalId), name = s.Name ?? s.LocalId, group = g, own = true });
                    break;
                case RefKind.Character:
                    foreach (var c in p.Characters.Where(c => c.Id != null))
                        l.Add(new RefEntry { value = p.Key(c.Id), name = c.Name ?? c.Id, group = g, own = true });
                    break;
                case RefKind.Quest:
                    foreach (var q in p.Quests) l.Add(new RefEntry { value = q.Name, name = q.Title ?? q.Name, sub = q.Name, group = g, own = true });
                    foreach (var e in p.EventGraphs()) l.Add(new RefEntry { value = e, name = e, sub = "event graph", group = g, own = true });
                    break;
                case RefKind.Music:
                    foreach (var (name, _) in p.AudioFiles(ModProject.MusicFolder))
                        if (!GameMusic.Contains(name, StringComparer.OrdinalIgnoreCase)) l.Add(new RefEntry { value = name, name = name, group = g, own = true });
                    break;
                case RefKind.Interior:
                    foreach (var r in p.Interiors.Where(r => r.Id != null))
                        l.Add(new RefEntry { value = p.Key(r.Id), name = r.Id, sub = r.IsBar ? "bar" : "hangar", group = g, own = true });
                    break;
                case RefKind.Race:
                    foreach (var r in p.Races.Where(r => r.Id != null))
                        l.Add(new RefEntry { value = p.Key(r.Id), name = r.Name ?? r.Id, sub = ProjectRace.Looks[r.Look] + (r.Hostile ? ", hostile" : ""), group = g, own = true });
                    break;
                case RefKind.NpcShip:
                    foreach (var n in p.NpcShips.Where(n => !n.IsOverride && n.LocalId != null))
                        l.Add(new RefEntry { value = p.Key(n.LocalId), name = n.Name ?? n.LocalId, sub = n.Kind, group = g, own = true });
                    break;
            }
        }

        // ---- other mods ---------------------------------------------------------------------------------------------

        static void Mod(RefKind kind, ModInfo m, List<RefEntry> l)
        {
            string g = m.Name;
            RefEntry E(string value, string name, string sub = null) =>
                new RefEntry { value = value, name = name, sub = sub, group = g, modId = m.Id, modVersion = m.Manifest?.version };
            switch (kind)
            {
                case RefKind.Item:
                    foreach (var d in ModContent.Parse(m).items.Where(d => d.localId != null))
                    {
                        var e = E(d.key, ModManifest.Pick(d.name) ?? d.localId);
                        if (int.TryParse(d.baseRef, out int b)) { e.icon = b; e.type = Database.Shared.Item(b)?.TypeId ?? -1; }
                        l.Add(e);
                    }
                    break;
                case RefKind.Ship:
                    foreach (var d in ModContent.Parse(m).ships.Where(d => d.localId != null)) l.Add(E(d.key, ModManifest.Pick(d.name) ?? d.localId));
                    break;
                case RefKind.System:
                    foreach (var d in ModContent.Parse(m).systems.Where(d => d.localId != null)) l.Add(E(d.key, ModManifest.Pick(d.name) ?? d.localId));
                    break;
                case RefKind.Station:
                    foreach (var d in ModContent.Parse(m).stations.Where(d => d.localId != null)) l.Add(E(d.key, ModManifest.Pick(d.name) ?? d.localId));
                    break;
                case RefKind.Character:
                    foreach (var o in Entries(m, ModCharacters.File))
                    {
                        string id = (string)o["id"];
                        if (id == null) continue;
                        l.Add(E(m.Id + ":" + id, NameOf(o) ?? id));
                    }
                    break;
                case RefKind.Interior:
                    foreach (var o in Entries(m, ProjectInteriorsFile))
                    {
                        string id = (string)o["id"];
                        if (id != null) l.Add(E(m.Id + ":" + id, id, (string)o["type"]));
                    }
                    break;
                case RefKind.Quest:
                    foreach (string f in m.Source.FilesIn("events", ".gof2event", ".gof2netevent", "." + ProjectQuest.Extension, "." + ProjectQuest.LegacyExtension))
                        l.Add(E(System.IO.Path.GetFileNameWithoutExtension(f), System.IO.Path.GetFileNameWithoutExtension(f)));
                    break;
                case RefKind.Music:
                    foreach (string f in m.Source.FilesIn(ModProject.MusicFolder, ModProject.AudioExtensions))
                    {
                        string n = System.IO.Path.GetFileNameWithoutExtension(f);
                        if (!GameMusic.Contains(n, StringComparer.OrdinalIgnoreCase)) l.Add(E(n, n));
                    }
                    break;
                case RefKind.NpcShip:
                    foreach (var d in ModContent.Parse(m).npcShips.Where(d => d.localId != null))
                        l.Add(E(d.key, (d.name != null ? ModManifest.Pick(d.name) : null) ?? d.localId, (string)d.json?["kind"]));
                    break;
                case RefKind.Race:
                    foreach (var d in ModContent.Parse(m).races.Where(d => d.localId != null))
                        l.Add(E(d.key, ModManifest.Pick(d.name) ?? d.localId, ProjectRace.Looks[Math.Max(0, Array.IndexOf(ProjectRace.LookRaces, d.look))]
                                                                            + (d.hostile ? ", hostile" : "")));
                    break;
            }
        }

        const string ProjectInteriorsFile = "interiors.json";

        static IEnumerable<JObject> Entries(ModInfo m, string file)
        {
            JToken t;
            try { t = ModJson.Read(m.Source, file); }
            catch (ModJsonException) { yield break; }
            if (t is JArray a) foreach (var o in a.OfType<JObject>()) yield return o;
        }

        static string NameOf(JObject o) =>
            o["name"] is JValue v && v.Type == JTokenType.String ? (string)v
            : o["name"] is JObject n ? (string)(n["en"] ?? n.Properties().FirstOrDefault()?.Value) : null;
    }
}
