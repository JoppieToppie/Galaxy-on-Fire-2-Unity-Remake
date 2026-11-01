// CampaignLevelCoop.cs
// CampaignLevel: the co-op campaign (docs/notes/multiplayer-coop.md; part of the partial CampaignLevel, its fields are in
// CampaignLevel.cs). The leader (the host) runs the story level as in single player; its ships are the crew's
// (NpcShip.StoryShip: shown to everyone in the orbit, hostile ones tougher by CoopCampaign.HullFactor), and a few times a
// second it tells the crew in that orbit what its level shows (CoopLevelState: the cutscene and its camera, the clock and
// time limit, the route, the fade, the radio lines). While a crew member there reads a story conversation the level holds
// (Held: no clock, script or radio, its ships stopped, the crew can't be hurt), as single player pauses the game.
// A guest's level is a follower (SetupFollower): nothing built or scripted, the leader's state and radio shown, the
// leader's cutscene camera watched with the controls held, and afterwards back beside the leader when it moved far.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class CampaignLevel
    {
        const float CoopSendMs = 250f, CoopCameraMs = 100f, CoopLeaderLostSeconds = 2f, CoopWaitNoticeMs = 20000f;
        /// <summary>After the leader's cutscene a follower farther than this from the leader is put beside it (m).</summary>
        const float CoopRegroupMeters = 2500f;

        /// <summary>The orbit this level is built in.</summary>
        public int CoopStation => level != null && level.Layout != null ? level.Layout.stationIndex : -1;

        /// <summary>The leader's ship for the crew: shown to them, never handed over, a hostile one tougher with more players.</summary>
        void MarkStoryShip(NpcShip s)
        {
            s.StoryShip = true;
            float factor = CoopCampaign.HullFactor(CoopCampaign.CrewSize);
            if (factor > 1f && !s.alwaysFriend && s.Hp.maxHull > 0 && s.Hp.maxHull < 1000000)
                s.SetHull(Mathf.RoundToInt(s.Hp.maxHull * factor));
        }

        // ---- the leader --------------------------------------------------------------------------------------------

        /// <summary>The leader, every frame: the hold, the state and the cutscene camera out. True = held this frame.</summary>
        bool LeaderTick(float dtMs)
        {
            bool hold = !Cutscene && SpaceHooks.CoopHold != null && SpaceHooks.CoopHold(CoopStation);
            if (hold != Held) SetHeld(hold);
            if ((coopSendMs -= dtMs) <= 0f || Cutscene != coopWasCutscene)
            {
                coopSendMs = CoopSendMs;
                coopWasCutscene = Cutscene;
                // A story orbit has no traffic of its own: what else its scripts spawned through the traffic (fixed objects,
                // turrets, the plasma array) is the level's too. Not the players' wingmen.
                if (traffic != null)
                    foreach (var s in traffic.Ships)
                        if (s != null && !s.StoryShip && !s.IsWingman && !s.MissionShip && !s.Gone) MarkStoryShip(s);
                SpaceHooks.CoopLevelState?.Invoke(new CoopLevelState
                {
                    station = CoopStation, index = BuiltIndex, cutscene = Cutscene, invulnerable = PlayerInvulnerable, hold = Held,
                    missionMs = MissionMs, timeLimitMs = TimeLimitMs, routeIndex = PlayerRoute != null ? PlayerRoute.index : 0,
                    fadeAlpha = FadeAlpha, fadeColor = FadeColor,
                });
                // The route when it changes, and every 2 s for a crew member arriving later.
                if (PlayerRoute != coopRouteSent || ++coopRouteIndexSent >= 8)
                {
                    coopRouteSent = PlayerRoute;
                    coopRouteIndexSent = 0;
                    SpaceHooks.CoopRoute?.Invoke(CoopStation, PlayerRoute != null ? new List<Vector3>(PlayerRoute.points) : new List<Vector3>());
                }
            }
            if (Cutscene && (coopCameraMs -= dtMs) <= 0f && Camera.main != null)
            {
                coopCameraMs = CoopCameraMs;
                var cam = Camera.main;
                SpaceHooks.CoopCamera?.Invoke(CoopStation, cam.transform.position, cam.transform.rotation, cam.fieldOfView);
            }
            return Held;
        }

        /// <summary>The hold on / off: the level's ships stop where they are (and fly on afterwards as they were).</summary>
        void SetHeld(bool on)
        {
            Held = on;
            if (on)
            {
                coopFrozen.Clear();
                foreach (var s in Ships)
                    if (s != null && !s.Gone && s.Target.Alive) { coopFrozen[s] = s.frozen; s.frozen = true; }
            }
            else
            {
                foreach (var pair in coopFrozen) if (pair.Key != null) pair.Key.frozen = pair.Value;
                coopFrozen.Clear();
            }
        }

        // ---- a follower --------------------------------------------------------------------------------------------

        /// <summary>A co-op guest's story level: the step's radio lines (shown when the leader's show), nothing built.</summary>
        public void SetupFollower(SpaceLevel spaceLevel, Traffic npcTraffic)
        {
            level = spaceLevel;
            traffic = npcTraffic;
            Current = this;
            Follower = true;
            BuiltIndex = Story.Index;
            music = gameObject.AddComponent<AudioSource>();
            music.playOnAwake = false;
            music.spatialBlend = 0f;
            Radio = new Radio(Story.Step?.radio) { Remote = true };
            gameObject.AddComponent<CoopCameraMirror>().Setup(this);
            // The prologue and the rescue (0 / 1): this game plays their cutscenes around its own Phantom, paced by the
            // leader's radio lines; the pirates and Gunant's salvager are the leader's (IntroCutscenes' follower).
            if (BuiltIndex == 0 || BuiltIndex == 1)
            {
                intro = new IntroCutscenes(this, level, BuiltIndex, true);
                intro.Build();
                traffic.MusicMuted = MusicOwned;
            }
            Debug.Log($"CampaignLevel: following the leader's level of step {BuiltIndex}");
        }

        /// <summary>A prologue / rescue follower: the leader has gone on (its step caught up here): the prologue's end loads
        /// the rescue, the rescue's docks at Var Hastra. True = this level is over.</summary>
        bool FollowIntroOn()
        {
            if (Story.Index <= BuiltIndex || level == null || level.Leaving) return level != null && level.Leaving;
            Debug.Log($"CampaignLevel: the leader left the {(BuiltIndex == 0 ? "prologue" : "rescue")}; following");
            if (BuiltIndex == 0)
            {
                Session.LaunchedFromStation = Session.ArrivedByTravel = false;
                level.Reload();
            }
            else level.Dock();
            return true;
        }

        void FollowerTick(float dtMs)
        {
            bool leaderSeen = coopLeaderSeenAt >= 0f && Time.unscaledTime - coopLeaderSeenAt < CoopLeaderLostSeconds;
            if (!leaderSeen)
            {
                // The leader isn't running it here (not arrived yet, left, docked): nothing held, a note now and then.
                if (Cutscene) EndFollowedCutscene();
                PlayerInvulnerable = Held = false;
                FadeAlpha = 0f;
                bool leaderHere = SpaceHooks.CoopLeaderHere != null && SpaceHooks.CoopLeaderHere(CoopStation);
                if (!leaderHere && level.LaunchCameraOver && (coopWaitNoticeMs -= dtMs) <= 0f)
                {
                    coopWaitNoticeMs = CoopWaitNoticeMs;
                    traffic?.Warn(Localization.Extra("coopWaitLeader", "The mission starts here once the host arrives."));
                }
            }
            if (!level.Dialogue && level.LaunchCameraOver) Radio?.Update(dtMs, this);
        }

        /// <summary>NetCoop: the leader's level state for this orbit.</summary>
        public void OnLeaderState(CoopLevelState s)
        {
            if (!Follower || s.station != CoopStation) return;
            coopLeaderSeenAt = Time.unscaledTime;
            if (intro != null) return;   // the prologue / rescue runs its own cutscenes here
            if (s.index != BuiltIndex)
            {
                // The leader's level went on to the next step on the same ships (4 -> 5): that step's radio.
                BuiltIndex = s.index;
                Radio = new Radio(StoryTable.Step(s.index)?.radio) { Remote = true };
            }
            bool wasCutscene = Cutscene;
            Cutscene = s.cutscene;
            if (wasCutscene && !s.cutscene) EndFollowedCutscene();
            Held = s.hold;
            PlayerInvulnerable = s.invulnerable || s.cutscene || s.hold;
            MissionMs = s.missionMs;
            TimeLimitMs = s.timeLimitMs;
            if (PlayerRoute != null && PlayerRoute.points.Count > 0) PlayerRoute.index = Mathf.Clamp(s.routeIndex, 0, PlayerRoute.points.Count);
            FadeAlpha = s.fadeAlpha;
            FadeColor = s.fadeColor;
        }

        /// <summary>NetCoop: the leader's route (game units) for this orbit.</summary>
        public void OnLeaderRoute(int station, List<Vector3> points)
        {
            if (!Follower || station != CoopStation) return;
            if (points == null || points.Count == 0) { if (PlayerRoute != null) SetPlayerRoute(null); return; }
            if (PlayerRoute != null && SamePoints(PlayerRoute.points, points)) return;   // the periodic resend
            var route = new Route(false);
            route.points.AddRange(points);
            SetPlayerRoute(route);
        }

        static bool SamePoints(List<Vector3> a, List<Vector3> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if ((a[i] - b[i]).sqrMagnitude > 1f) return false;
            return true;
        }

        /// <summary>NetCoop: the leader's level showed radio line 'line' of step 'index' here.</summary>
        public void OnLeaderRadio(int station, int index, int line)
        {
            if (!Follower || station != CoopStation) return;
            if (index != BuiltIndex) { if (intro != null) return; BuiltIndex = index; Radio = new Radio(StoryTable.Step(index)?.radio) { Remote = true }; }
            Radio?.ShowRemote(line);
        }

        /// <summary>NetCoop: the leader's cutscene camera.</summary>
        public void OnLeaderCamera(int station, Vector3 position, Quaternion rotation, float fov)
        {
            if (!Follower || station != CoopStation) return;
            coopCamPos = position;
            coopCamRot = rotation;
            coopCamFov = fov;
            coopCamAt = Time.unscaledTime;
        }

        /// <summary>NetCoop: the leader's level moved it on from 'from': a follower there goes along (the same ride or jump, or
        /// docked where the story docked the leader).</summary>
        public void OnLeaderTravel(int from, int to, bool dock, bool streamOut)
        {
            if (!Follower || from != CoopStation || level == null || level.Leaving) return;
            Debug.Log($"CampaignLevel: following the leader from {from} to {to}{(dock ? " (docked)" : "")}");
            if (dock) { Session.StationIndex = to; level.Dock(); }
            else level.TravelTo(to, streamOut);
        }

        /// <summary>CoopCameraMirror: the leader's cutscene camera to show now (a follower during the leader's cutscene).</summary>
        public bool TryCoopCamera(out Vector3 position, out Quaternion rotation, out float fov)
        {
            position = coopCamPos;
            rotation = coopCamRot;
            fov = coopCamFov;
            return Follower && intro == null && Cutscene && coopCamAt >= 0f && Time.unscaledTime - coopCamAt < CoopLeaderLostSeconds;
        }

        /// <summary>The leader's cutscene is over: a follower it left far behind (a ride, a warp, the wormhole) comes to its side.</summary>
        void EndFollowedCutscene()
        {
            Cutscene = false;
            coopCamAt = -1f;
            if (level == null || level.Player == null || SpaceHooks.CoopLeaderPose == null) return;
            if (!SpaceHooks.CoopLeaderPose(CoopStation, out var pos, out var rot)) return;
            var me = level.Player.transform;
            if ((me.position - pos).magnitude < CoopRegroupMeters) return;
            // Beside and behind the leader: rows of four, each row farther back.
            int n = (int)(NetSession.LocalId % 64), row = n / 4;
            var slot = new Vector3((n % 4 - 1.5f) * 60f, 20f + 30f * row, -120f - 90f * row);
            me.SetPositionAndRotation(pos + rot * slot, rot);
            Debug.Log("CampaignLevel: back beside the leader after its cutscene");
        }
    }
}
