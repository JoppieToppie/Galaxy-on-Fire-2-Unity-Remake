// ValkyrieLevelsCoop.cs
// ValkyrieLevels (part of the partial class; its fields are in ValkyrieLevels.cs), remake co-op: a follower's scenery of the
// leader's 80 and 81 (docs/notes/multiplayer-coop.md "What else a follower sees"). The battlestation, its lasers and Kothar's
// station are scenery, not ships, so no copy of them reached the crew: guests saw the battlestation's turrets in empty space
// and flew through its hull. A follower builds the same scenery (BuildFollower) and plays each of its moments as the
// leader's script reaches that step (CampaignLevel.Event from CoopLevelState), never touching the camera (the leader's,
// CoopCameraMirror) or the ships (the leader's copies).

using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class ValkyrieLevels
    {
        /// <summary>The leader's script step this follower has played up to (-1 = not a follower's scenery).</summary>
        int followStep = -1;
        float followMs;

        /// <summary>A follower's scenery of the leader's level 'index' (80, 81); false = none for that level.</summary>
        public bool BuildFollower(int index)
        {
            switch (index)
            {
                case 80:
                    built = 80;
                    guns80.Clear();
                    BuildBattlestation80();
                    BuildLasers80();
                    break;
                case 81:
                    built = 81;
                    BuildBattlestation81();
                    break;
                default:
                    return false;
            }
            followStep = 0;
            followMs = 0f;
            return true;
        }

        /// <summary>Every frame: the moments of the steps the leader's script has reached since (several at once for a guest
        /// arriving late: their blasts, sounds and flashes are skipped, what they leave behind is shown).</summary>
        public void FollowTick(float dtMs, bool leaderSeen)
        {
            if (followStep < 0) return;
            followMs += dtMs;
            int target = c.Event;
            while (followStep < target)
            {
                followStep++;
                followMs = 0f;
                FollowStep(followStep, caughtUp);
            }
            if (leaderSeen) caughtUp = true;   // from the leader's first state on, each moment as it comes
            if (built == 80)
            {
                if (followStep == 9) TickBurnB80(dtMs);
                if (followStep >= 13 && followMs >= 2151f && battlestation != null && battlestation.activeSelf) battlestation.SetActive(false);
            }
            else if (built == 81 && followStep >= 1 && T >= BattlestationAppears81 && battlestation != null && !battlestation.activeSelf)
                battlestation.SetActive(true);
        }
        /// <summary>The leader's first state has been played (the steps before it were caught up quietly).</summary>
        bool caughtUp;

        /// <summary>The leader's script reached 'step': its scenery moment here ('show' false: only what stays, no blast,
        /// sound or flash).</summary>
        void FollowStep(int step, bool show)
        {
            if (built == 80)
                switch (step)
                {
                    case 1: ShowLaser80(); if (show) PlayAtCamera(assets?.deepScienceAttacked); break;
                    case 2: if (show) FireLaser80(); break;
                    case 6: if (battleLaser != null) battleLaser.SetActive(false); break;
                    case 7: if (show) ShowStationLaser80(); break;
                    case 8:
                        if (show) FireStationLaser80();
                        else if (level.Station != null) PartAnimation.HoldAllAtEnd(level.Station);   // blown apart already
                        break;
                    case 9: HitBlast80(); break;
                    case 10: if (stationLaser != null) stationLaser.SetActive(false); break;
                    case 12: if (show) JumpBlast80(); break;
                    case 13: if (show) JumpFx80(); break;
                }
            else if (built == 81 && step == 1 && show) ArriveFx81();
        }

        /// <summary>After the frame: the flames on Kothar's station and the hyperdrive fx (its own camera-free part of LateTick).</summary>
        public void FollowLateTick(float dtMs)
        {
            if (followStep < 0) return;
            if (built == 80) UpdateFlamePieces80();
            if (!fxFixed) FaceCamera(fx);
            if (fx != null && (fxMs += dtMs) >= fxLength) { Object.Destroy(fx); fx = null; }
        }
    }
}
