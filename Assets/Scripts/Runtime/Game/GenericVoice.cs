// GenericVoice.cs
// Globals::getDialogueSoundId 0xfa110 (Reference/research/dialogue_cutscenes.md 5.3) as voice-line names for
// StoryAssets.Voice:
//   1. the text -> voice table 0x255210 (Resources/GoF2Data/voice_table.json, Reference/tools/dialogue/build_voice_table.py):
//      story pages and radio, and named messages (443 / 444 the Nivelian hints, 433, 449 / 450 the black market, 458 ...)
//   2. no entry: the speaking agent's race / gender picks a GENERIC set, the text its line:
//        race 0 / 5 and Midorians (3) not of body 2   TERRANMALE, females TERRANFEMALE (freelance lines 370-389 only)
//        1 VOSSK, 2 and Midorians of body 2 (or no portrait) NIVELIAN, 4 MULTIPOD, 6 BOBOLAN, 7 GREY
//        8 pirates                                     435-440 only: MSG_RADIO_PIRATE_STATION_0..2 / _DESTROYED_0..2
//      text     370 WON_CHALLENGE   371 LOST_CHALLENGE   372 START_CHALLENGE   373-377 WON_1..5   378 JUNK_START
//               379-383 START_1..5  384-388 LOST_1..5    389 BACK_FOR_REWARD   (all MISSION_RADIO_*)
//               426-428 MSG_FRIEND_TURNED_ENEMY_0..2     429-431 MSG_FRIENDS_ALARMED_0..2
//               445-447 MSG_ENTER_HOSTILE_ORBIT_0..2     313 MSG_WINGMEN_TIMEUP
//      (Nivelian speakers also voice the pirate-station notes 432 / 434 / 442.) A set without the recording stays silent.
// The event ids of the switch were checked against the FEV names (fev_events.py; ids from 660 on are 4 past the names).

using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class GenericVoice
    {
        [Serializable] class Row { public int text; public string voice; }
        [Serializable] class Table { public List<Row> items; }

        static Dictionary<int, string> table;

        static Dictionary<int, string> Map
        {
            get
            {
                if (table != null) return table;
                table = new Dictionary<int, string>();
                var ta = GoF2Remake.Data.GameContent.Text("GoF2Data/voice_table");
                var t = ta != null ? JsonUtility.FromJson<Table>(ta) : null;
                if (t?.items != null) foreach (var r in t.items) table[r.text] = r.voice;
                return table;
            }
        }

        /// <summary>The voice line of 'text' said by an agent of 'race' (-1: none, table only), or null (silent).</summary>
        public static string For(int text, int race = -1, bool male = true, int[] portrait = null)
        {
            if (Map.TryGetValue(text, out var v)) return v;
            if (race == 8) return text >= 435 && text <= 437 ? $"MSG_RADIO_PIRATE_STATION_{text - 435}"
                                : text >= 438 && text <= 440 ? $"MSG_RADIO_PIRATE_STATION_DESTROYED_{text - 438}" : null;
            string set = Set(race, male, portrait);
            if (set == null) return null;
            if (set == "NIVELIAN")
            {
                if (text == 432) return "MSG_PIRATE_STATION_NO_TRADING";
                if (text == 434) return "MSG_PIRATE_STATION_NO_ENTRANCE";
                if (text == 442) return "MSG_PIRATE_STATION_REWARD";
            }
            if (set == "TERRANFEMALE" && (text < 370 || text > 389)) return null;
            string line = Line(text);
            if (line == null) return null;
            if (set == "MULTIPOD" && line == "MISSION_RADIO_BACK_FOR_REWARD") return "MULTIPOD_MULTIPOD_MISSION_RADIO_BACK_FOR_REWARD";
            return $"{set}_{line}";
        }

        /// <summary>An agent's set (getDialogueSoundId's race switch).</summary>
        static string Set(int race, bool male, int[] portrait)
        {
            switch (race)
            {
                case 0: case 5: return male ? "TERRANMALE" : "TERRANFEMALE";
                case 1: return "VOSSK";
                case 2: return "NIVELIAN";
                case 3:
                    if (portrait == null || portrait.Length == 0 || portrait[0] == 2) return "NIVELIAN";
                    return male ? "TERRANMALE" : "TERRANFEMALE";
                case 4: return "MULTIPOD";
                case 6: return "BOBOLAN";
                case 7: return "GREY";
                default: return null;
            }
        }

        static string Line(int text)
        {
            if (text == 370) return "MISSION_RADIO_WON_CHALLENGE";
            if (text == 371) return "MISSION_RADIO_LOST_CHALLENGE";
            if (text == 372) return "MISSION_RADIO_START_CHALLENGE";
            if (text >= 373 && text <= 377) return $"MISSION_RADIO_WON_{text - 372}";
            if (text == 378) return "MISSION_RADIO_JUNK_START";
            if (text >= 379 && text <= 383) return $"MISSION_RADIO_START_{text - 378}";
            if (text >= 384 && text <= 388) return $"MISSION_RADIO_LOST_{text - 383}";
            if (text == 389) return "MISSION_RADIO_BACK_FOR_REWARD";
            if (text >= 426 && text <= 428) return $"MSG_FRIEND_TURNED_ENEMY_{text - 426}";
            if (text >= 429 && text <= 431) return $"MSG_FRIENDS_ALARMED_{text - 429}";
            if (text >= 445 && text <= 447) return $"MSG_ENTER_HOSTILE_ORBIT_{text - 445}";
            if (text == 313) return "MSG_WINGMEN_TIMEUP";
            return null;
        }
    }
}
