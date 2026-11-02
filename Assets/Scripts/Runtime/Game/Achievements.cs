// Achievements.cs
// Medals (Achievements, checkForNewMedal 0x181ec0 on docking / countMedals 0x182444; Reference/research/lounge_ui.md 6.3):
// 45 medals, grade 0 none, 1 gold, 2 silver, 3 bronze; a counter against the thresholds [gold, silver, bronze]
// (0x259860, -1 = grade not used), the best grade is kept. Medal names 1507 + i, hints 1552 + i (# = the threshold of the
// earned grade). 0 Veteran is preset gold; 35 Champion comes with all other base medals; 36-44 are the Supernova medals.
// 22 Harum-Scarum: docking from campaign 8 with no weapon (types 0-2) or no equipment item (type 3) mounted
// (initCheckEquipmentAndWeapons 0x182388). The elite medals 38 / 40 / 41 / 42 / 44 are flags set in flight when their
// counter reaches the gold threshold (Session.OreStreak etc., Elite()). Plain C#.

using System.Collections.Generic;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.Data
{
    public static class Achievements
    {
        public const int Count = 45, BaseCount = 36;

        /// <summary>DAT_00259860: [gold, silver, bronze] per medal.</summary>
        static readonly int[,] Thresholds =
        {
            { 0, -1, -1 }, { 5, 15, 30 }, { 11, 8, 5 }, { 11, 8, 5 }, { 250, 100, 50 }, { 200, 100, 25 }, { 1000, 500, 100 },
            { 25, 10, 3 }, { 1000, 100, 25 }, { 22, 16, 5 }, { 150, 100, 30 }, { 100, 50, 25 }, { 22, 10, 5 }, { 13, 6, 3 },
            { 13, 6, 3 }, { 20, 10, 5 }, { 50, 25, 5 }, { 100, 50, 10 }, { 50, 20, 5 }, { 5, 3, 2 }, { 50, 20, 5 },
            { 25, 10, 5 }, { 0, -1, -1 }, { 4, 3, 2 }, { 500, 200, 50 }, { 1000000, 500000, 125000 }, { 100, 50, 20 },
            { 20, 10, 3 }, { 1, -1, -1 }, { 250, 150, 50 }, { 0, -1, -1 }, { 500, 250, 100 }, { 50, -1, -1 }, { 10, -1, -1 },
            { 12, -1, -1 }, { 0, -1, -1 }, { 3000, -1, -1 }, { 50, -1, -1 }, { 10, -1, -1 }, { 20, -1, -1 }, { 100, -1, -1 },
            { 3, -1, -1 }, { 15, -1, -1 }, { 5, -1, -1 }, { 8, -1, -1 },
        };

        /// <summary>Achievements::getValue(i, grade): the threshold shown in the hint.</summary>
        public static int Threshold(int medal, int grade) => grade >= 1 && grade <= 3 ? Thresholds[medal, grade - 1] : 0;

        public static int Grade(int medal) => Session.Medals != null && medal < Session.Medals.Length ? Session.Medals[medal] : 0;

        /// <summary>The medal's counter, or null when the remake doesn't track it (the medal can't be earned yet).</summary>
        static int? Counter(Database db, int medal)
        {
            switch (medal)
            {
                case 1: return Session.LastArrivalHullPercent;                        // Survivor (<=)
                case 2: return Session.OreTypesMined.Count;
                case 3: return Session.CoreTypesMined.Count;
                case 4: return Session.Kills;
                case 5: return Session.ContainersDelivered;
                case 6: return Session.OreMined;
                case 7: return Session.CoresMined;
                case 8: return Session.BoozeBought;                // Personal Need: Status+0xa8
                case 9: return Session.BoozeTypes.Count;           // Barkeeper: Status+0xac
                case 21: return Session.AlienRemainsCollected;     // Alien Hunter: Status+0xcc
                case 10: return Session.JunkDestroyed;
                case 11: return Session.VisitedStations.Count;
                case 12: return SystemsVisited(db);
                case 13: return Session.UnlockedBlueprints.Count;
                case 14: return Session.Blueprints.FindAll(b => b.timesProduced > 0).Count;
                case 15: return (int)(Session.PlaySeconds / 3600f);
                case 16: return Session.FreelanceCompleted;
                case 17: return Session.JumpgatesUsed;
                case 18: return Session.PassengersDelivered;
                case 23: { int n = 0; foreach (var e in Session.Equipment) if (db.Item(e.item)?.TypeId == 0) n++; return n; }
                case 24: return Session.CratesSalvaged;
                case 25: return Session.HighestCredits;
                case 26: return Session.AgentsTalkedTo;
                case 27: return Session.WingmenHired;
                case 28: { int n = 0; for (int r = 0; r < 4; r++) if (Standing.IsEnemy(r)) n++; return n; }
                case 29: return Session.AsteroidsDestroyed;
                case 30: return Session.CampaignMission >= Story.GameWonIndex && !Session.FreePlay ? 1 : (int?)null;
                case 31: return Shop.FreeCargo(db);
                case 36: return Shop.MaxLoad(db);   // Ship::getMaxLoad
                case 32: return Session.OffersDeclined;
                case 33: return Session.AcceptedBlindRisk;
                case 34: return Session.AcceptedBlindMap;
                case 19: return (int)(Session.CloakMs / 60000);   // Ninja: minutes cloaked
                case 20: return Session.BombsDetonated;
                case 43: return Session.GraveRiserKills;
                case 37: return Session.KaamoShips.Count;   // Ship Collector: stored hulls (one per type)
                case 39: return Session.BattleshipsDestroyed;
                case 22: return Session.ArrivedWithoutGear ? 1 : 0;
                case 38: case 40: case 41: case 42: case 44: return Session.EliteFlags.Contains(medal) ? Thresholds[medal, 0] : 0;
            }
            return null;
        }

        /// <summary>initCheckEquipmentAndWeapons: from campaign 8, no mounted weapon (types 0-2) or no equipment item (type 3).</summary>
        public static bool NoWeaponOrEquipment(Database db)
        {
            if (!Session.FreePlay && Session.CampaignMission < 8) return false;
            int weapons = 0, equipment = 0;
            foreach (var e in Session.Equipment)
            {
                int type = db.Item(e.item)?.TypeId ?? 4;
                if (type == 3) equipment++; else if (type != 4) weapons++;
            }
            return weapons == 0 || equipment == 0;
        }

        /// <summary>An elite medal's in-flight counter (Level::enemyDied, MiningGame::update, Gun::calcCharacterCollision /
        /// ignite, Player::damageEmp): at the gold threshold its Status flag is set; checkForNewMedal awards it on docking.</summary>
        public static void Elite(int medal, int count)
        {
            if (NetSession.Active) return;   // no medals in multiplayer
            Progress(medal, count);
            if (Grade(medal) == 0 && count >= Thresholds[medal, 0]) Session.EliteFlags.Add(medal);
        }

        /// <summary>Hud::hudEventMedal 0x18f36c: a Supernova medal's progress on the flight HUD, "name: n%" (n = the counter
        /// against the gold threshold), while the medal isn't earned. When, as its counter's caller has it: 38 Ore Athlete
        /// (MiningGame::update) from 30 % at each 10 %, 39 Destroyer Destroyer (PlayerFixedObject::update) at every kill
        /// below twice the threshold, 40 Blindfolded Killer (Level::enemyDied) at each 10 %, 41-44 (Gun::calcCharacterCollision,
        /// Player::damageEmp, Level::enemyDied, Gun::ignite) once the threshold is reached. Remake: n stops at 100.</summary>
        public static void Progress(int medal, int count)
        {
            string text = ProgressText(medal, count);
            if (text != null) GameHooks.FlightMessage?.Invoke(text);
        }

        /// <summary>Progress's line, or null when the original shows none for this count.</summary>
        public static string ProgressText(int medal, int count)
        {
            if (NetSession.Active || medal < BaseCount || medal >= Count || Has(medal)) return null;
            int max = Thresholds[medal, 0];
            if (max <= 0 || count <= 0) return null;
            int percent = (int)(count / (float)max * 100f);
            bool show;
            switch (medal)
            {
                case 38: show = percent % 10 == 0 && percent >= 30; break;
                case 39: show = count / max < 2; break;
                case 40: show = percent % 10 == 0; break;
                case 41: case 42: case 43: case 44: show = count >= max; break;
                default: show = false; break;   // 36 / 37: counted on docking
            }
            return show ? $"{Localization.Get(1507 + medal)}: {Mathf.Min(percent, 100)}%" : null;
        }

        /// <summary>Achievements::hasMedal(i, 1): the counters stop once the medal is earned.</summary>
        public static bool Has(int medal) => Grade(medal) != 0;

        /// <summary>The grade a medal earns next: from none the lowest grade it has (bronze, or gold for the one-grade
        /// medals), then one up; 0 once gold.</summary>
        public static int NextGradeOf(int medal)
        {
            int grade = Grade(medal);
            if (grade == 1) return 0;
            for (int g = grade == 0 ? 3 : grade - 1; g >= 1; g--) if (Thresholds[medal, g - 1] >= 0) return g;
            return 0;
        }

        /// <summary>Remake (the Status window's progress bars, a player's request 2026-11-02): the medal's counter against
        /// the threshold of its next grade. False when the medal is gold, isn't counted up (0 Veteran, 1 Survivor's "at
        /// most", the one-off 22 / 28 / 30, the elite streaks 38 / 40 / 41 / 42 / 44 that live in one flight) or the remake
        /// doesn't track it. 35 Champion counts the other base medals earned. A strict medal ("more than #") stays short
        /// of full until its counter passes the threshold.</summary>
        public static bool NextGrade(Database db, int medal, out int grade, out int count, out int target, out float fraction)
        {
            grade = NextGradeOf(medal);
            count = target = 0;
            fraction = 0f;
            if (grade == 0 || medal == 1 || medal == 38 || medal == 40 || medal == 41 || medal == 42 || medal == 44) return false;
            target = Thresholds[medal, grade - 1];
            int? c;
            if (medal == 35)
            {
                int earned = 0;
                for (int i = 0; i < BaseCount; i++) if (i != 35 && Grade(i) != 0) earned++;
                c = earned;
                target = BaseCount - 1;
            }
            else c = Counter(db, medal);
            if (c == null || target <= 1) { target = 0; return false; }
            count = Mathf.Max(0, c.Value);
            fraction = Mathf.Clamp01(count / (float)target);
            if (Strict(medal) && count <= target) fraction = Mathf.Min(fraction, 0.99f);
            return true;
        }

        /// <summary>The medals earned (any grade) and the gold ones, of all 45.</summary>
        public static (int earned, int gold) Earned()
        {
            int earned = 0, gold = 0;
            for (int i = 0; i < Count; i++)
            {
                int g = Grade(i);
                if (g != 0) earned++;
                if (g == 1) gold++;
            }
            return (earned, gold);
        }

        static int SystemsVisited(Database db)
        {
            var systems = new HashSet<int>();
            foreach (int st in Session.VisitedStations) systems.Add(db.Stations.Find(x => x.index == st)?.system ?? -1);
            systems.Remove(-1);
            return systems.Count;
        }

        /// <summary>checkForNewMedal + applyNewMedals (on docking): the medals whose grade improved.</summary>
        public static List<int> Check(Database db)
        {
            if (Session.Medals == null || Session.Medals.Length != Count) Session.Medals = new int[Count];
            var improved = new List<int>();
            if (NetSession.Active) return improved;   // multiplayer: no medals (nor their rewards)
            if (Session.Medals[0] == 0) Session.Medals[0] = 1;   // Veteran: preset gold (Achievements::init)
            for (int i = 1; i < Count; i++)
            {
                if (i == 35) continue;
                var c = Counter(db, i);
                if (c == null) continue;
                int grade = 0;
                for (int g = 1; g <= 3 && grade == 0; g++)
                {
                    int t = Thresholds[i, g - 1];
                    if (t < 0) continue;
                    bool met = i == 1 ? c.Value <= t
                             : i == 30 || i == 22 || i == 35 ? c.Value > t - 1 && c.Value > 0
                             : Strict(i) ? c.Value > t
                             : c.Value >= t && (t > 0 || c.Value > 0);
                    if (met) grade = g;
                }
                if (grade != 0 && (Session.Medals[i] == 0 || grade < Session.Medals[i])) { Session.Medals[i] = grade; improved.Add(i); }
            }
            // countMedals: all other base medals earned -> 35 Champion.
            int earned = 0;
            for (int i = 0; i < BaseCount; i++) if (i != 35 && Session.Medals[i] != 0) earned++;
            if (earned == BaseCount - 1 && Session.Medals[35] == 0) { Session.Medals[35] = 1; improved.Add(35); }
            return improved;
        }

        /// <summary>checkForNewMedal: these medals `break` on counter > threshold ("more than" in their hints).</summary>
        static bool Strict(int medal) => medal switch
        {
            5 or 6 or 7 or 8 or 10 or 16 or 18 or 20 or 21 or 26 or 27 or 29 or 31 or 32 or 33 or 34 or 36 => true,
            _ => false,
        };

        /// <summary>gotAllGoldMedals: every base medal gold (the VoidX at Thynome).</summary>
        public static bool GotAllGoldMedals
        {
            get
            {
                if (Session.Medals == null) return false;
                for (int i = 0; i < BaseCount; i++) if (Session.Medals[i] != 1) return false;
                return true;
            }
        }

        /// <summary>gotAllSupernovaMedals: all gold, plus the nine elite medals.</summary>
        public static bool GotAllSupernovaMedals
        {
            get
            {
                if (!GotAllGoldMedals) return false;
                for (int i = BaseCount; i < Count; i++) if (Session.Medals[i] == 0) return false;
                return true;
            }
        }

        public static bool GotAllMedals
        {
            get
            {
                if (Session.Medals == null) return false;
                for (int i = 0; i < BaseCount; i++) if (Session.Medals[i] == 0) return false;
                return true;
            }
        }

        /// <summary>StatusWindow's hint: 1552 + i with # = the earned grade's threshold. Modified: a medal not earned yet
        /// (the original shows no hint for it, but the elite ones' with gold) gives the threshold of the grade it earns next.
        /// <paramref name="progress"/> (the Status window's "Progress: n / t") goes right under the requirement.</summary>
        public static string Hint(int medal, string progress = null)
        {
            int grade = Grade(medal);
            int shown = grade != 0 ? grade : Mathf.Max(1, NextGradeOf(medal));
            string text = Localization.Get(1552 + medal).Replace("#", Threshold(medal, shown).ToString("#,0"));
            if (progress != null) text += "\n" + progress;
            // getMedalHintText: the silver Barkeeper lists the drinks still missing (276 + 1406 + j). Modified: below
            // silver too (none or bronze), so the list shows what is still missing at every grade short of gold; there up
            // to 22 drinks, so one paragraph of names instead of a line each (it pushed the medal grid out of the window).
            if (medal == 9 && grade != 1)
            {
                var missing = new List<string>();
                for (int j = 0; j < 22; j++) if (!Session.BoozeTypes.Contains(132 + j)) missing.Add(Localization.Get(1406 + j));
                text += "\n\n" + Localization.Get(276)
                      + (grade == 2 ? "\n- " + string.Join("\n- ", missing) : " " + string.Join(", ", missing));
            }
            return text;
        }
    }
}
