// SupernovaChallenge.cs
// The Supernova Challenge (mission type 0xb7, Reference/research/supernova_challenge.md): a timed arcade round in Luur's
// orbit (111) during the supernova, the Blue Fyre against waves of Specters. Unreachable in the Android build: no menu
// started it and its score / combo were never written (only Hud::drawChallengeModeScore's drawing survives). The remake
// offers it from the main menu and fills in the scoring around the original's HUD and combo-bonus formula.
//
// What is the original's: the start (MenuTouchWindow::startSupernovaChallenge 0x154238: Status::resetGame, campaign index
// 0x98, ship 46 with its loadout, 220 kills, station 111), the round of 181 000 ms (Level+0x130 / Objective(3, 181000)),
// the restart's Patala x10 (MGame::OnTouchEnd), the combo bonus combo x 1000 x (1 + 0.05 combo) and its blinking in the
// last 3000 ms, the pop of the combo count above 7000 ms of combo timer.
// The scoring is the iOS 1.1.19 build's (Level::enemyDied / MGame::OnUpdate, which the Android build never wrote): a kill
// scores 1000 + 2000 x the combo timer's remaining share of 7500 ms (3000 at most, 1000 once it ran out), then the timer
// is set to 7500 ms; a kill while it runs raises the combo (Status+0x204); when it runs out with a combo above 1 the bonus
// combo x 1000 x (1 + 0.05 combo) is paid and the combo is 1 again. Remake picks: the bonus is paid at the round's end
// too (the original's next frame does the same); a death ends the round with its score; the difficulty is always Normal
// (one highscore table); the highscore and best kills stay on this device (PlayerPrefs; the iOS build reports to Game
// Center, the Android stub did nothing).
// Plain C#: the orbit (World.ChallengeOrbit) and the HUD (UI.ChallengeView) read it.

using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class SupernovaChallenge
    {
        /// <summary>Level+0x130: the round's length (3:01 on the HUD's countdown).</summary>
        public const float RoundMs = 181000f;
        /// <summary>Level::enemyDied: the combo timer a kill sets (Status+0x1fc = 7500). Hud::drawChallengeModeScore pops the
        /// count above 7000 ms (1 + (timer - 7000) x 0.01).</summary>
        public const float ComboStartMs = 7500f;
        /// <summary>Hud::drawChallengeModeScore: the bonus line and the blinking in the combo's last 3000 ms.</summary>
        public const float ComboWarnMs = 3000f;
        /// <summary>Level::enemyDied: a kill's points, 1000 + 2000 x the timer's remaining share (1000 once it ran out, 3000 at
        /// a full timer).</summary>
        public const int KillPoints = 1000, KillPointsMax = 3000;
        public static int KillPointsAt(float comboMs) => comboMs <= 0f ? KillPoints : comboMs >= ComboStartMs ? KillPointsMax
                                                      : (int)(comboMs / ComboStartMs * 2000f) + KillPoints;
        /// <summary>Hud::drawChallengeModeScore: the score's 7 digits.</summary>
        public const int ScoreDigits = 7;
        public const int MaxScore = 9999999;
        /// <summary>Station 111 Luur (Galaxy::getStation(0x6f)), campaign index 0x98, ship 46 Blue Fyre.</summary>
        public const int Station = 111, CampaignIndex = 0x98, Ship = 46;
        /// <summary>The secondary: 216 Patala, x5 in the first round, x10 after a restart (MGame::OnTouchEnd).</summary>
        public const int Missile = 216;

        /// <summary>A challenge game is running (from Start until the next new game or load, Session.ResetNewGame).</summary>
        public static bool Active { get; private set; }
        /// <summary>Rounds played since the challenge was picked (1 = the first).</summary>
        public static int Round { get; private set; }
        public static float ElapsedMs { get; private set; }
        public static int Score { get; private set; }
        public static int Kills { get; private set; }
        /// <summary>Status+0x188: the combo count (1 = no combo).</summary>
        public static int Combo { get; private set; } = 1;
        /// <summary>Status+0x17c: the combo timer, ms left.</summary>
        public static float ComboMs { get; private set; }
        public static int BestCombo { get; private set; }
        /// <summary>The round is over (time out or the player's death): the score is final.</summary>
        public static bool Over { get; private set; }
        public static bool NewHighscore { get; private set; }
        /// <summary>The last combo bonus paid and how long ago (the HUD's "+N" line).</summary>
        public static int LastBonus { get; private set; }
        public static float SinceBonusMs { get; private set; } = float.MaxValue;

        public static float TimeLeftMs => Mathf.Max(0f, RoundMs - ElapsedMs);
        public static bool TimeUp => ElapsedMs >= RoundMs;

        /// <summary>The bonus a combo pays (Hud::drawChallengeModeScore: (int)(combo x 1000 x (1 + combo x 0.05))).</summary>
        public static int ComboBonus(int combo) => combo > 1 ? (int)(combo * 1000 * (1f + combo * 0.05f)) : 0;

        /// <summary>The combo line is drawn (timer > 0 and combo > 1), blinking in its last 3000 ms (off when timer % 100 &lt; 50).</summary>
        public static bool ComboShown => ComboMs > 0f && Combo > 1 && (ComboMs > ComboWarnMs || ComboMs % 100f >= 50f);
        /// <summary>The bonus the combo would pay now, shown under it in its last 3000 ms.</summary>
        public static bool BonusShown => ComboShown && ComboMs <= ComboWarnMs;
        /// <summary>The count's pop: 1 + max(0, (timer - 7000) x 0.01).</summary>
        public static float ComboScale => 1f + Mathf.Max(0f, (ComboMs - 7000f) * 0.01f);

        /// <summary>Remake: where the highscore is kept (tests point it elsewhere).</summary>
        public static string PrefsKey = "challenge_highscore";
        public static int Highscore => PlayerPrefs.GetInt(PrefsKey, 0);
        public static int BestKills => PlayerPrefs.GetInt(PrefsKey + "_kills", 0);

        /// <summary>A kill or bonus sound to play (the SV_Mode blips, 1..10; 0 = none); taken by the orbit.</summary>
        public static int PendingSound { get; private set; }
        public static int TakeSound() { int s = PendingSound; PendingSound = 0; return s; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => End();

        /// <summary>Session.ResetNewGame (a new game, a load, the main menu): the challenge is over.</summary>
        public static void End()
        {
            Active = false;
            Round = 0;
            ResetRound();
        }

        static void ResetRound()
        {
            ElapsedMs = 0f;
            Score = Kills = 0;
            Combo = 1;
            ComboMs = 0f;
            BestCombo = 0;
            Over = NewHighscore = false;
            LastBonus = 0;
            SinceBonusMs = float.MaxValue;
            PendingSound = 0;
        }

        /// <summary>MenuTouchWindow::startSupernovaChallenge 0x154238 (a first round) or MGame::OnTouchEnd's restart (again):
        /// Status::resetGame, campaign index 0x98 (no step side effects: the story mission stays resetGame's empty one), the
        /// Blue Fyre with its loadout, 220 kills (rank 9 with the index), Luur, full hull / shield / armor and gamma. Returns
        /// the scene to load.</summary>
        public static string Start(bool again)
        {
            int round = again ? Round + 1 : 1;
            Session.ResetNewGame();   // ends a challenge running: Active and Round come back below
            Session.Difficulty = Session.DifficultyNormal;   // remake: one highscore table
            Session.Economy = Economy.Android;
            Session.CampaignMission = CampaignIndex;
            Session.ShipIndex = Ship;
            // Ship::setEquipment: primaries 231 / 230 / 231, then Synchrotron Boost, Fluxed Matter Shield, T'yol, Hiroto
            // Proscan, Gamma Shield II, Ketar Repair Bot, Static Thrust, and Patala x5 (x10 on a restart).
            Session.Equipment = new List<ItemStack>
            {
                new ItemStack(231, 1), new ItemStack(230, 1), new ItemStack(231, 1),
                new ItemStack(73, 1), new ItemStack(54, 1), new ItemStack(59, 1), new ItemStack(83, 1),
                new ItemStack(206, 1), new ItemStack(75, 1), new ItemStack(76, 1),
                new ItemStack(Missile, again ? 10 : 5),
            };
            Session.Kills = 220;
            Session.StationIndex = Station;
            Session.PreviousStationIndex = -1;
            Session.LaunchedFromStation = Session.ArrivedByTravel = Session.ArrivedBySystemJump = false;
            Session.PlayerGamma = -1f;   // 100 at the level's start
            // The hints the original marks as shown (wingmen, secondaries, standing, planet jump, jump drive, cloak, gamma...).
            foreach (int h in new[] { 0x08, 0x09, 0x0a, 0x0d, 0x0e, 0x0f, 0x13, 0x15, 0x17, 0x1c, 0x20, 0x21, 0x22, 0x29 }) Session.Hints.Add(h);
            ResetRound();
            Active = true;
            Round = round;
            return "Space";
        }

        /// <summary>The round's clock and the combo timer (the orbit's frame, game time).</summary>
        public static void Tick(float dtMs)
        {
            if (!Active || Over || dtMs <= 0f) return;
            ElapsedMs += dtMs;
            if (SinceBonusMs < float.MaxValue) SinceBonusMs += dtMs;
            if (ComboMs > 0f)
            {
                ComboMs -= dtMs;
                if (ComboMs <= 0f) PayCombo();
            }
        }

        /// <summary>A Specter destroyed by the player: the points by the timer left, the combo up (a new one at 1) and its
        /// timer restarted.</summary>
        public static void OnKill()
        {
            if (!Active || Over) return;
            Kills++;
            int points = KillPointsAt(ComboMs);
            Combo = ComboMs > 0f ? Combo + 1 : 1;
            ComboMs = ComboStartMs;
            if (Combo > BestCombo) BestCombo = Combo;
            Add(points);
            // SV_Mode_01..09: the combo's steps (2 -> 01 ... 10 and more -> 09).
            if (Combo > 1) PendingSound = Mathf.Min(Combo - 1, 9);
        }

        static void PayCombo()
        {
            int bonus = ComboBonus(Combo);
            ComboMs = 0f;
            Combo = 1;
            if (bonus <= 0) return;
            Add(bonus);
            LastBonus = bonus;
            SinceBonusMs = 0f;
            PendingSound = 10;   // SV_Mode_10: the longer one
        }

        static void Add(int points) => Score = (int)System.Math.Min(MaxScore, (long)Score + points);

        /// <summary>The round ends (time out or death): a running combo pays out, the highscore is kept. Returns true for a
        /// new highscore.</summary>
        public static bool Finish()
        {
            if (!Active || Over) return NewHighscore;
            if (ComboMs > 0f) PayCombo();
            Over = true;
            NewHighscore = Score > Highscore;
            if (NewHighscore) PlayerPrefs.SetInt(PrefsKey, Score);
            if (Kills > BestKills) PlayerPrefs.SetInt(PrefsKey + "_kills", Kills);
            PlayerPrefs.Save();
            return NewHighscore;
        }

        /// <summary>The score as the HUD draws it: zero-padded to 7 digits.</summary>
        public static string Digits(int value) => Mathf.Clamp(value, 0, MaxScore).ToString().PadLeft(ScoreDigits, '0');
    }
}
