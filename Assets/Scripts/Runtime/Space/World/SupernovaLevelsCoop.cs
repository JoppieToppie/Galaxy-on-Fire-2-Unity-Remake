// SupernovaLevelsCoop.cs
// SupernovaLevels (part of the partial class; its fields are in SupernovaLevels.cs), remake co-op: a follower's scenery of the
// leader's 157 (docs/notes/multiplayer-coop.md "What else a follower sees"). The Valkyrie, its plasma gun, its burning
// stages and the beam are scenery, not ships, so the crew saw none of the final battle's centrepiece. A follower builds
// them (BuildFollower), places the Valkyrie where the leader's is (CoopLevelState.sceneryAt, moved on between the states)
// and plays the script's moments as the leader's script reaches each step (CampaignLevel.Event), never touching the camera
// (the leader's, CoopCameraMirror) or the ships (the leader's copies). The fade is the leader's (CoopLevelState).

using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class SupernovaLevels
    {
        /// <summary>The leader's script step this follower has played up to (-1 = not a follower's scenery).</summary>
        int followStep = -1;
        float followMs, followBlastMs = -1f, followBurnMs = -1f, leaderAtMs;
        bool followCaughtUp;
        Vector3 leaderValkyrieAt;
        bool hasLeaderValkyrieAt;

        /// <summary>The leader's moving scenery, for CoopLevelState (157: the Valkyrie's place, the original's coordinates);
        /// false = none.</summary>
        public bool CoopSceneryAt(out Vector3 at)
        {
            at = valkyrieAt;
            return built == 157 && valkyrieGo != null;
        }

        /// <summary>A follower's scenery of the leader's level 'index' (157); false = none for that level.</summary>
        public bool BuildFollower(int index)
        {
            if (index != 157) return false;
            built = 157;
            BuildValkyrie157();
            followStep = 0;
            return true;
        }

        /// <summary>CoopLevelState: where the leader's Valkyrie is now.</summary>
        public void LeaderSceneryAt(Vector3 at)
        {
            leaderValkyrieAt = at;
            hasLeaderValkyrieAt = true;
            leaderAtMs = 0f;
        }

        /// <summary>Every frame: the Valkyrie toward the leader's (moved on at its speed since that state), and the moments of
        /// the steps the leader's script has reached (a guest arriving late: no blasts or sounds, what they leave shown).</summary>
        public void FollowTick(float dtMs, bool leaderSeen)
        {
            if (followStep < 0) return;
            followMs += dtMs;
            leaderAtMs += dtMs;
            int target = c.Event;
            while (followStep < target)
            {
                followStep++;
                followMs = 0f;
                FollowStep157(followStep, followCaughtUp);
            }
            if (leaderSeen) followCaughtUp = true;
            FollowValkyrie(dtMs);
            switch (followStep)
            {
                case 7:
                    // The Valkyrie backs away once Alice's line is over: its jump sound as it starts.
                    if (!valkyrieSound && sn != null && hasLeaderValkyrieAt && leaderValkyrieAt.z > 20000f + 1f)   // (it starts at z 20 000)
                    {
                        valkyrieSound = true;
                        Sfx.PlayAt(sn.carrierJump, Camera.main != null ? Camera.main.transform.position : Player.position);   // 0x8cb
                    }
                    break;
                case 9:
                    // Tick157 state 9's blasts and burning stage 1 (after radio 13).
                    if (followBlastMs < 0f && Over(13))
                    {
                        Blast(VAt(new Vector3(-2000, 1000, -8000)), combat?.explosionMid, 5f);
                        followBlastMs = 0f;
                        followBurnMs = 300f;
                    }
                    if (followBurnMs > 0f && (followBurnMs -= dtMs) <= 0f && stage1 != null)
                    {
                        stage1.transform.SetPositionAndRotation(At(valkyrieAt), valkyrieRot);
                        stage1.SetActive(true);
                    }
                    if (followBlastMs >= 0f && (followBlastMs += dtMs) > 6000f)
                    {
                        followBlastMs = -2000f;
                        Blast(VAt(new Vector3(-6000, 2000, -10000)), combat?.explosionMid, 5f);
                    }
                    if (stage1 != null && stage1.activeSelf) stage1.transform.position = At(valkyrieAt);
                    break;
                case 10:
                    if (followMs >= 9000f && followMs - dtMs < 9000f) Blast(VAt(-LightGame * 3000f), combat?.explosionBig, 10f);
                    break;
                case 11:
                    // The beam once Alice's last line has 4 s left (Tick157 state 11, from 6001 ms).
                    if (followMs > 6000f && !beamOn && AliceNearlyDone())
                    {
                        beamOn = true;
                        if (beam != null) { beam.SetActive(true); SetBeamAnimating(true); }
                        if (sn != null) Sfx.PlayAt(sn.valkyrieBeam, Camera.main != null ? Camera.main.transform.position : Player.position);   // 0x8c7
                    }
                    break;
                case 12:
                    if (followMs >= 6000f) SetBeamAnimating(false);
                    if (followMs >= 5000f && followMs - dtMs < 5000f)
                        foreach (var r in Object.FindObjectsByType<SkyLayers>(FindObjectsInactive.Exclude)) r.gameObject.SetActive(false);
                    if (followMs >= 5001f && followMs < 6000f && level.Backdrop != null)
                        level.Backdrop.sunScaleFactor *= Mathf.Pow(followMs <= 5499f ? 0.8f : 1.7f, dtMs / OriginalFrame.Ms);
                    if (followMs >= 5500f && followMs - dtMs < 5500f) Blast(VAt(new Vector3(0, 0, -12000) - LightGame * 3000f), combat?.explosionBig, 25f);
                    if (followMs >= 5700f && followMs - dtMs < 5700f)
                    {
                        if (beam != null) beam.SetActive(false);
                        if (valkyrieGo != null) valkyrieGo.SetActive(false);
                        if (valkyrieGun != null) valkyrieGun.SetActive(false);
                        if (sn != null && Camera.main != null) Sfx.PlayAt(sn.explosion, Camera.main.transform.position);   // 0x8c8
                    }
                    break;
            }
        }

        /// <summary>The leader's script reached 'step' (157): what it shows from then on.</summary>
        void FollowStep157(int step, bool show)
        {
            switch (step)
            {
                case 9:
                    followBlastMs = followBurnMs = -1f;
                    break;
                case 10:
                    if (stage2 != null) { stage2.transform.SetPositionAndRotation(At(valkyrieAt), valkyrieRot); stage2.SetActive(true); }
                    if (stage1 != null) stage1.SetActive(false);
                    break;
                case 11:
                    beamOn = false;
                    // The beam (0x4a75 + 0x4a76 / 0x4a77) at the Valkyrie facing (0, 0, -1), up (0, 1, 0), hidden until it fires.
                    beam = Scenery("sn_plasma_gun_fx_valkyrie_beam_anim_add", OrbitScale.Place(valkyrieAt), Quaternion.LookRotation(Dir(new Vector3(0, 0, -1)), Vector3.up), "Plasma beam");
                    if (beam != null) { OrbitScale.Structure(beam.transform); beam.SetActive(false); SetBeamAnimating(false); }
                    if (!show) followMs = 6000f;   // arrived late: the beam fires as soon as Alice's line allows
                    break;
            }
        }

        /// <summary>The Valkyrie toward the leader's: its last place moved on at the leader's own speed since (states 8 / 9:
        /// 10 u/ms; 7 accelerates to it), never faster than 20 u/ms, so the 4 states a second don't show as jumps.</summary>
        void FollowValkyrie(float dtMs)
        {
            if (!hasLeaderValkyrieAt || valkyrieGo == null || !valkyrieGo.activeSelf) return;
            float speed = followStep == 8 || followStep == 9 ? 10f : followStep == 7 ? 5f : 0f;
            var targetAt = leaderValkyrieAt + new Vector3(0f, 0f, speed * leaderAtMs);
            if ((targetAt - valkyrieAt).sqrMagnitude > 40000f * 40000f) valkyrieAt = targetAt;   // far off (arrived late): there at once
            else valkyrieAt = Vector3.MoveTowards(valkyrieAt, targetAt, 20f * dtMs);
            MoveValkyrie(0f);
        }
    }
}
