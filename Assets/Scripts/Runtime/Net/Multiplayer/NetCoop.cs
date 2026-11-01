// NetCoop.cs
// Remake multiplayer: the co-op campaign's networking (docs/notes/multiplayer-coop.md; the story rules: CoopCampaign).
//   A co-op session is the host's campaign for its crew (NetGame.CoopSession, the main menu's Co-op campaign; 4 players by
//     default, NetGame.CoopMaxPlayers): kept
//     in NetGame.CoopFolder like a persistent world (every crew member's profile, the host's holds the story), its world
//     the story's own (not the finished game's), every player in one squad (the crew: no friendly fire, shared bar
//     missions, distress calls), the host the leader (its story is published, NetState.coopStory; it runs the story
//     levels). The host leaving ends the session as always.
//   Each game: the leader publishes its story when it changes, a guest catches up and sends its progress (Tick).
//   The levels' hooks (SpaceHooks.Coop*): the leader's story level's state, route, radio, cutscene camera and moves go to
//     the crew in that orbit (NetState); a guest's story conversation holds the leader's level; a guest destroyed in the
//     leader's story orbit comes back beside the leader (RespawnDue / Respawn), anywhere else docked as usual.
//   The menu reads the campaign's summary (coop.json, written by the host: its step, campaign, crew, when).

using System;
using System.IO;
using GoF2Remake.Data;
using GoF2Remake.Events;
using GoF2Remake.World;
using Unity.Netcode;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetCoop
    {
        const float StorySeconds = 0.1f, SquadSeconds = 1f, SummarySeconds = 30f, RespawnMs = 5000f;
        static float storyTimer, squadTimer, summaryTimer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { storyTimer = squadTimer = summaryTimer = 0f; }

        /// <summary>The last session this game was in was a co-op campaign (the menu reopens the Co-op campaign screen after it).</summary>
        public static bool LastSessionWasCoop { get; internal set; }

        /// <summary>The session is a co-op campaign.</summary>
        public static bool Active => NetGame.Active && NetState.Instance != null && NetState.Instance.IsSpawned && NetState.Instance.Coop;
        /// <summary>This game leads it (the host; a dedicated server never runs one).</summary>
        public static bool Leader => Active && NetGame.IsServer && !NetGame.Dedicated;
        public static int CrewSize
        {
            get
            {
                int n = 0;
                foreach (var p in NetPlayer.All) if (p != null && p.IsSpawned) n++;
                return Mathf.Max(1, n);
            }
        }

        // ---- the lobby: a new campaign waits for its crew -------------------------------------------------------

        /// <summary>The host starts a new co-op campaign: its session opens with the lobby (NetState.CoopStarted false).</summary>
        internal static bool LobbyWanted => NetGame.CoopSession && NetGame.CoopNewCampaign >= 0;

        /// <summary>This game's world waits for the host's Start (NetGame.EnterWorld).</summary>
        internal static bool WaitsInLobby => Active && !NetState.Instance.CoopStarted;

        /// <summary>The co-op campaign's world opens in flight: step 0, the prologue (every crew member a Phantom in the belt).</summary>
        internal static bool StartsInFlight => Active && !Session.FreePlay && Story.Index == 0;

        /// <summary>The lobby's crew (names, the host first).</summary>
        public static System.Collections.Generic.List<string> CrewNames()
        {
            var list = new System.Collections.Generic.List<string>();
            var l = LeaderPlayer;
            if (l != null) list.Add(l.DisplayName);
            foreach (var p in NetPlayer.All) if (p != null && p.IsSpawned && p != l) list.Add(p.DisplayName);
            return list;
        }

        /// <summary>The host's Start in the lobby: everyone into the world at once.</summary>
        public static void StartCampaign()
        {
            if (!Leader || !WaitsInLobby) return;
            NetState.Instance.SetCoopStarted();
            OnStarted();
        }

        /// <summary>The campaign began (NetState.CoopStarted): a game waiting in the lobby enters the world.</summary>
        internal static void OnStarted()
        {
            if (NetGame.InLobby) NetGame.EnterWorld();
        }

        /// <summary>The leader's player (the host's).</summary>
        public static NetPlayer LeaderPlayer => NetSquad.Find(NetworkManager.ServerClientId);

        // ---- starting -----------------------------------------------------------------------------------------

        /// <summary>NetGame.StartHost: the co-op campaign's game. 'newCampaign' >= 0: a new one of that Campaign (the old folder
        /// kept as .old); -1: the one in CoopFolder (its host profile holds the story; with none yet, a new main campaign).</summary>
        internal static void PrepareHost(int newCampaign)
        {
            string folder = NetGame.CoopFolder;
            if (newCampaign >= 0 && Directory.Exists(folder))
            {
                try
                {
                    string old = folder + ".old";
                    if (Directory.Exists(old)) Directory.Delete(old, true);
                    Directory.Move(folder, old);
                    Debug.Log($"NetCoop: the last co-op campaign is kept in {old}");
                }
                catch (Exception e) { Debug.LogWarning("NetCoop: couldn't put the last co-op campaign aside: " + e.Message); }
            }
            // A new game of the campaign; a saved one replaces it when the host's profile arrives (SaveGame.ApplyProfile).
            CoopCampaign.NewCampaign(NetGame.Db, (Campaign)Mathf.Clamp(newCampaign < 0 ? 0 : newCampaign, 0, 2));
            NetGame.ResetDb();   // the tables of the story's world (the finished world's Cronus price no more)
            CoopCampaign.SetSettings(NetGame.CoopStrength, NetGame.CoopSplitRewards);   // the Host card's
            CoopCampaign.SetCrewLimit(NetGame.MaxPlayers);
        }

        const string CoopUsage = "/coop strength 0|25|50|100, /coop rewards shared|full";

        /// <summary>The co-op settings as a line for the chat.</summary>
        static string SettingsText() => string.Format(Localization.Extra("coopSettingsNow", "Enemies: +{0}% per extra player. Story credits: {1}."),
            CoopCampaign.Strength,
            CoopCampaign.SplitRewards ? Localization.Extra("mpCoopRewardsShared", "Shared") : Localization.Extra("mpCoopRewardsFull", "Each in full"));

        /// <summary>/coop [strength 0 | 25 | 50 | 100] [rewards shared | full]: the host's co-op settings while playing (kept for
        /// the next session too); no argument shows them. Everyone hears a change.</summary>
        internal static string Command(string args, IPilot by)
        {
            if (!Active) return Localization.Extra("coopNotCoop", "This session isn't a co-op campaign.");
            var words = (args ?? "").Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return SettingsText() + " " + CoopUsage;
            if (words.Length % 2 != 0) return CoopUsage;
            int strength = CoopCampaign.Strength;
            bool split = CoopCampaign.SplitRewards;
            for (int i = 0; i + 1 < words.Length; i += 2)
            {
                string value = words[i + 1].TrimStart('+').TrimEnd('%');
                if (words[i] == "strength" && int.TryParse(value, out int pct) && pct >= 0 && pct <= 200) strength = pct;
                else if (words[i] == "rewards" && (value == "shared" || value == "split")) split = true;
                else if (words[i] == "rewards" && value == "full") split = false;
                else return CoopUsage;
            }
            CoopCampaign.SetSettings(strength, split);
            NetGame.CoopStrength = strength;
            NetGame.CoopSplitRewards = split;
            string text = SettingsText();
            NetState.Instance?.Announce(text);
            return text;
        }

        /// <summary>NetState reached a guest: the crew's story first (an empty profile keeps it), then its own profile.</summary>
        internal static void OnJoined(string story)
        {
            CoopCampaign.Reset();
            CoopCampaign.PrepareGuest(CoopCampaign.FromJson(story), NetGame.Db);
            NetGame.ResetDb();
            Debug.Log($"NetCoop: joined the co-op campaign at step {Story.Index}");
        }

        /// <summary>The leader's story changed (every game; the leader's own is ignored).</summary>
        internal static void OnStory(string story)
        {
            if (NetGame.IsServer) return;
            CoopCampaign.ApplyLeader(story, NetGame.Db);
        }

        // ---- every frame (NetState.Update) ------------------------------------------------------------------

        internal static void Tick(NetState state)
        {
            if (!Active) return;
            LastSessionWasCoop = true;
            if ((storyTimer -= Time.unscaledDeltaTime) <= 0f)   // ten times a second (the State is serialized to compare)
            {
                storyTimer = StorySeconds;
                if (Leader) CoopCampaign.LeaderTick(); else CoopCampaign.GuestTick(NetGame.Db);
            }
            if (!Leader) return;
            if ((squadTimer -= Time.unscaledDeltaTime) <= 0f) { squadTimer = SquadSeconds; KeepCrewSquad(state); }
            if ((summaryTimer -= Time.unscaledDeltaTime) <= 0f) { summaryTimer = SummarySeconds; WriteSummary(); }
        }

        /// <summary>Server: every player is in the crew's squad (squads form only in a hangar otherwise).</summary>
        static void KeepCrewSquad(NetState state)
        {
            var leader = LeaderPlayer;
            if (leader == null) return;
            foreach (var p in NetPlayer.All)
                if (p != null && p.IsSpawned && p != leader && !NetSquad.Same(p, leader)) state.RestoreSquad(leader, p);
        }

        // ---- the hooks (NetHooksInstaller) ---------------------------------------------------------------------

        internal static void ReportWon(int index)
        {
            var state = NetState.Instance;
            if (state == null || !state.IsSpawned) return;
            if (NetGame.IsServer) state.CoopWon(index);
            else state.CoopWonRpc(index);
        }

        internal static void ReportAdvanced(int from) { if (NetState.Instance != null && !NetGame.IsServer) NetState.Instance.CoopAdvancedRpc(from); }
        internal static void ReportProgress(string json) { if (NetState.Instance != null && !NetGame.IsServer) NetState.Instance.CoopProgressRpc(json); }
        internal static void ReportFreelance(long id) { if (NetState.Instance != null && !NetGame.IsServer) NetState.Instance.CoopFreelanceRpc(id); }
        internal static void Publish(string json) { if (NetGame.IsServer) NetState.Instance?.SetCoopStory(json); }

        internal static void Dialog(bool open)
        {
            var state = NetState.Instance;
            if (!Active || state == null) return;
            if (NetGame.IsServer) state.SetCoopDialog(NetGame.LocalId, open);
            else state.CoopDialogRpc(open);
        }

        internal static bool Hold(int station) => Leader && NetState.Instance.CoopHoldIn(station);

        internal static void LevelFailed(int station) { if (Leader) NetState.Instance.CoopLevelFailed(station); }
        internal static void LevelState(CoopLevelState s) { if (Leader) NetState.Instance.SendCoopLevel(s); }
        internal static void Route(int station, System.Collections.Generic.List<Vector3> points) { if (Leader) NetState.Instance.SendCoopRoute(station, points); }
        internal static void Radio(int station, int index, int line) { if (Leader) NetState.Instance.SendCoopRadio(station, index, line); }
        internal static void Camera(int station, Vector3 pos, Quaternion rot, float fov) { if (Leader) NetState.Instance.SendCoopCamera(station, pos, rot, fov); }
        internal static void Travel(int from, int to, bool dock, bool streamOut) { if (Leader) NetState.Instance.SendCoopTravel(from, to, dock, streamOut); }

        /// <summary>The leader flies in 'station'.</summary>
        internal static bool LeaderHere(int station)
        {
            var l = LeaderPlayer;
            return Active && l != null && l.InSpace && l.Station == station;
        }

        internal static bool LeaderPose(int station, out Vector3 position, out Quaternion rotation)
        {
            var l = LeaderPlayer;
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (!LeaderHere(station) || l.IsOwner) return false;
            position = l.transform.position;
            rotation = l.transform.rotation;
            return true;
        }

        // ---- the leader's word at a guest ----------------------------------------------------------------------

        /// <summary>The leader's story level in 'station' failed: a guest there starts again docked; anyone else hears it.</summary>
        internal static void OnLevelFailed(int station)
        {
            var c = CampaignLevel.Current;
            if (c != null && c.Follower && c.CoopStation == station && c.Level != null && c.Level.StorySpace != null) c.Level.StorySpace.CoopFailed();
            else NetChat.Notice(Localization.Extra("coopFailed", "The crew's mission failed: it starts again."));
        }

        /// <summary>The leader's story level moved it on from 'from': a guest following it there goes along (first at the
        /// leader's step, so the new orbit is built as the story's).</summary>
        internal static void OnLeaderTravel(int from, int to, bool dock, bool streamOut, int leaderIndex)
        {
            CoopCampaign.CatchUpTo(leaderIndex, NetGame.Db);
            CampaignLevel.Current?.OnLeaderTravel(from, to, dock, streamOut);
        }

        // ---- a guest destroyed in the leader's story orbit -----------------------------------------------------------

        /// <summary>This guest's ship comes back beside the leader (the leader alive in this story orbit) instead of docking.</summary>
        public static bool RespawnsInSpace
        {
            get
            {
                if (!Active || Leader) return false;
                var c = CampaignLevel.Current;
                var l = LeaderPlayer;
                return c != null && c.Follower && l != null && l.InSpace && l.Station == c.CoopStation && l.Hull > 0f;
            }
        }

        internal static bool RespawnDue(float deathMs) => deathMs >= RespawnMs && RespawnsInSpace;

        internal static void Respawn()
        {
            var c = CampaignLevel.Current;
            var l = LeaderPlayer;
            if (c == null || l == null) return;
            var p = l.transform.position;
            var gamePos = new Vector3(p.x, p.y, -p.z) / OrbitLayout.MetersPerUnit;
            var behind = l.transform.forward * -1f;
            gamePos += new Vector3(behind.x, behind.y, -behind.z) * 3000f;   // a little behind the leader (game units)
            NetTeleport.Respawn(c.CoopStation, gamePos, 1500f);
        }

        // ---- the menu's summary ---------------------------------------------------------------------------------

        [Serializable]
        public class Summary
        {
            public int index = -1, campaign;
            public string savedAtUtc = "", crew = "";
        }

        static string SummaryPath => Path.Combine(NetGame.CoopFolder, "coop.json");

        /// <summary>The co-op campaign in CoopFolder, null = none.</summary>
        public static Summary ReadSummary()
        {
            try
            {
                if (!File.Exists(SummaryPath)) return null;
                var s = JsonUtility.FromJson<Summary>(File.ReadAllText(SummaryPath));
                return s != null && s.index >= 0 ? s : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>The host: the campaign's step and crew for the menu (now and then, and when the session ends).</summary>
        public static void WriteSummary()
        {
            if (!Leader) return;
            var names = new System.Collections.Generic.List<string>();
            foreach (var p in NetPlayer.All) if (p != null && p.IsSpawned) names.Add(p.DisplayName);
            var s = new Summary { index = Story.Index, campaign = (int)Session.Campaign, savedAtUtc = DateTime.UtcNow.ToString("o"), crew = string.Join(", ", names) };
            try
            {
                Directory.CreateDirectory(NetGame.CoopFolder);
                File.WriteAllText(SummaryPath, JsonUtility.ToJson(s));
            }
            catch (Exception e) { Debug.LogWarning("NetCoop: couldn't write the summary: " + e.Message); }
        }
    }
}
