// NewsTicker.cs
// The station main view's news ticker (NewsTicker 0x1889d4 / replaceTokens 0x188ea0 / update 0x18a8bc; Reference/
// research/lounge_ui.md 4). Plain C#: the items of ticker.json (text 3262 + index):
//   1. every story item whose campaign window holds (param1 > 0, param1 <= campaign <= param2) for the system's race
//   2. then 2 random items (100 tries): always-items (param2 >= 161, param1 <= campaign), 50 % each, for the race, not
//      picked yet; item 13 (the lounge drink) not in systems past 21; 'flag' items at most once per 10 minutes of play
//   joined with "    +++    "; scrolls left at 50 px/s (the station repeats it to keep the strip full).
//   Remake mods: in a system whose stations have a stations.json "description", up to 3 of those take the random items' place.
// Tokens: #PLANET_NAME / #SYSTEM_NAME (a random station), #DRINK_NAME (1406 + system; its #SYSTEM_NAME = that system),
// #CHILD_NAME (a first name), #SHIP_NAME (913 + a random fighter), #PLATFORM_NUMBER (A-F + 0-9), #CATASTROPHE (3237 +
// rnd 5), #VICTIMS, #BERGER_LASER (1279 + rnd 4), #VOSSK_SHIP_OR_ITEM / #VOSSK_REVENUE (3242), #PROFESSION (3243 + rnd 4),
// #NAME, #ACTIVITY (3247 + rnd 4), #RACE_NAME (406 + rnd 4), #CRIME (3251 + rnd 5), #NIVELIAN_PLANET, #DEMONSTRATORS,
// #POLL_PERCENTAGE, #POLL_TOPIC (3256 + rnd 6), #CASUALTIES. The numbers (the iOS build's replaceTokens, whose int arguments
// the Android decompile lost): #VICTIMS (rnd 99000 + 100000) to the thousand, #DEMONSTRATORS (rnd 990000 + 1000000) to
// the thousand, #VOSSK_REVENUE playing time in ms / 1 000 000 + 12, #POLL_PERCENTAGE rnd 80 + 10, #CASUALTIES rnd 48.

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Random = UnityEngine.Random;

namespace GoF2Remake.Data
{
    [Serializable]
    public class TickerItem
    {
        public int index;
        public bool flag;
        public bool[] conditions = new bool[4];
        public int param1, param2;
    }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NewsTicker
    {
        public const float ScrollPxPerSecond = 50f;
        public const string Separator = "    +++    ";
        static List<TickerItem> items;
        static float lastFlagTime = -1e9f;

        [Serializable] class Wrapper { public List<TickerItem> list; }

        static List<TickerItem> Items
        {
            get
            {
                if (items != null) return items;
                var ta = Resources.Load<TextAsset>("GoF2Data/ticker");
                items = ta != null ? JsonUtility.FromJson<Wrapper>("{\"list\":" + ta.text + "}").list : new List<TickerItem>();
                return items ??= new List<TickerItem>();
            }
        }

        /// <summary>No ticker at stations 101 and 108 or in Loma (system 25).</summary>
        public static bool ShownAt(int station, int system) => station != 101 && station != 108 && system != 25;

        /// <summary>NewsTicker::NewsTicker: the joined text for this station's race and the campaign index.</summary>
        public static string Build(Database db, int system, int race)
        {
            int campaign = Session.CampaignMission;
            race = Mathf.Clamp(race, 0, 3);
            var picked = new List<TickerItem>();
            foreach (var it in Items)
                if (it.param1 > 0 && it.param1 <= campaign && campaign <= it.param2 && Allowed(it, race)) picked.Add(it);
            // Remake mods: a system whose stations have stations.json "description"s runs their lore instead of the game's 2
            // random items (those name the game's own places): up to 3, "Name (subtitle): description".
            var lore = Modding.ModWorld.StationsWithLore(db, system);
            var loreText = new StringBuilder();
            for (int n = 0; n < 3 && lore.Count > 0; n++)
            {
                int k = Random.Range(0, lore.Count), st = lore[k];
                lore.RemoveAt(k);
                string name = db.Stations.Find(x => x.index == st)?.name ?? "";
                string sub = Modding.ModWorld.Subtitle(st);
                loreText.Append(string.IsNullOrEmpty(sub) ? name : $"{name} ({sub})").Append(": ")
                        .Append(Modding.ModWorld.Description(st)).Append(Separator);
            }
            int random = loreText.Length > 0 ? 2 : 0;
            for (int tries = 0; tries < 100 && random < 2 && Items.Count > 0; tries++)
            {
                var it = Items[Random.Range(0, Items.Count)];
                if (it.param2 < 161 || it.param1 > campaign || Random.Range(0, 2) == 0 || !Allowed(it, race) || picked.Contains(it)) continue;
                if (it.index == 13 && system > 21) continue;
                if (it.flag)
                {
                    if (Session.PlaySeconds - lastFlagTime < 600f) continue;   // Status+0x160 / +0x170: once per 10 minutes
                    lastFlagTime = Session.PlaySeconds;
                }
                picked.Add(it);
                random++;
            }
            var sb = new StringBuilder();
            foreach (var it in picked) sb.Append(ReplaceTokens(db, Localization.Get(3262 + it.index), system)).Append(Separator);
            sb.Append(loreText);
            return sb.ToString();
        }

        static bool Allowed(TickerItem it, int race) => it.conditions == null || race >= it.conditions.Length || it.conditions[race];

        static string T(int id) => Localization.Get(id);

        /// <summary>NewsTicker::replaceTokens.</summary>
        static string ReplaceTokens(Database db, string s, int system)
        {
            if (s.IndexOf('#') < 0) return s;
            var station = db.Stations[Random.Range(0, db.Stations.Count)];
            for (int tries = 0; (station.system < 0 || !Modding.ModCampaigns.StationAllowed(db, station.index)) && tries < 500; tries++)
                station = db.Stations[Random.Range(0, db.Stations.Count)];   // not a mod's missing station, not outside a campaign's galaxy
            if (s.Contains("#DRINK_NAME"))
            {
                // A drink of this system (1406 + system), else a random drink system's.
                int drinkSystem = system <= 21 ? system : Random.Range(0, 22);
                s = s.Replace("#DRINK_NAME", T(1406 + drinkSystem))
                     .Replace("#SYSTEM_NAME", db.Systems.Find(x => x.index == drinkSystem)?.name ?? "");
            }
            s = s.Replace("#PLANET_NAME", station.name).Replace("#SYSTEM_NAME", station.systemName);
            if (s.Contains("#CHILD_NAME"))
            {
                string n = AgentGenerator.RandomName(Random.Range(0, 3) == 0 ? 2 : 0, Random.Range(0, 2) == 0);
                int sp = n.IndexOf(' ');
                s = s.Replace("#CHILD_NAME", sp > 0 ? n.Substring(0, sp) : n);
            }
            if (s.Contains("#SHIP_NAME")) s = s.Replace("#SHIP_NAME", GameNames.Ship(Flight.FighterPool.Random(Random.Range(0, 4))));
            s = s.Replace("#PLATFORM_NUMBER", $"{(char)('A' + Random.Range(0, 6))}{Random.Range(0, 10)}")
                 .Replace("#CATASTROPHE", T(3237 + Random.Range(0, 5)))
                 .Replace("#VICTIMS", ((Random.Range(0, 99000) + 100000) / 1000 * 1000).ToString("#,0"))
                 .Replace("#BERGER_LASER", T(1279 + Random.Range(0, 4)));
            if (s.Contains("#VOSSK_SHIP_OR_ITEM"))
                s = s.Replace("#VOSSK_SHIP_OR_ITEM", Random.Range(0, 100) < 50 ? T(922) : GameNames.Item(Random.Range(0, 60)))
                     .Replace("#VOSSK_REVENUE", $"{(int)(Session.PlaySeconds / 1000f) + 12} {T(3242)}");
            s = s.Replace("#PROFESSION", T(3243 + Random.Range(0, 4)))
                 .Replace("#NAME", AgentGenerator.RandomName(Random.Range(0, 8), true))
                 .Replace("#ACTIVITY", T(3247 + Random.Range(0, 4)))
                 .Replace("#RACE_NAME", T(406 + Random.Range(0, 4)))
                 .Replace("#CRIME", T(3251 + Random.Range(0, 5)))
                 .Replace("#NIVELIAN_PLANET", RandomStationOfRace(db, 2))
                 .Replace("#DEMONSTRATORS", ((Random.Range(0, 990000) + 1000000) / 1000 * 1000).ToString("#,0"))
                 .Replace("#POLL_PERCENTAGE", (Random.Range(0, 80) + 10).ToString())
                 .Replace("#POLL_TOPIC", T(3256 + Random.Range(0, 6)))
                 .Replace("#CASUALTIES", Random.Range(0, 48).ToString());
            return s;
        }

        static string RandomStationOfRace(Database db, int race)
        {
            var systems = db.Systems.FindAll(x => x.raceId == race && x.stations.Count > 0);
            if (systems.Count == 0) return "";
            var sys = systems[Random.Range(0, systems.Count)];
            int st = sys.stations[Random.Range(0, sys.stations.Count)];
            return db.Stations.Find(x => x.index == st)?.name ?? "";
        }
    }
}
