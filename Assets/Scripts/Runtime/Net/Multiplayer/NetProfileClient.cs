// NetProfileClient.cs
// Remake-only: a player's side of the server-kept profiles (NetProfiles). On a server with profiles (NetState.ProfilesOn)
// the game doesn't start at once when it connects: it signs in (NetState.LoginRpc: its token for this server, if it has
// one, and a hash of the device id), waits for the profile, applies it (SaveGame.ApplyProfile; an empty one = the fresh
// free-play start NetGame.PrepareSession made) and only then enters the world (NetGame.EnterWorld).
// The token is kept per server id in PlayerPrefs ("mp_token_<server>", a -mpname suffix so two games on one machine are
// two players). While this device controls its profile it uploads it (SaveGame.ProfileJson, gzipped in chunks): on every
// docking (SaveGame.AutoSave), every UploadSeconds while playing, and when leaving the session. An observer (another
// device controls the profile) stays docked: Refusal is the station menu's answer to Launch, Hangar, Lounge and Map.
// Promoted later (/control, the controller left) or linked to another profile (/link CODE), the new profile arrives and
// the station reloads with it.

using System;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetProfileClient
    {
        const float UploadSeconds = 60f;
        const string StationScene = "Station";

        static bool waiting;      // signed in, the profile not here yet (the world not entered)
        static int seed;
        static float uploadTimer = UploadSeconds;
        static int outSeq;
        // The profile coming in.
        static int inSeq = -1, inGot;
        static byte[][] inParts;
        static string inToken;
        static bool inController, inGuest;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            waiting = false;
            uploadTimer = UploadSeconds;
            outSeq = 0;
            inSeq = -1; inGot = 0; inParts = null;
            Controller = true;
            Guest = false;
            serverId = null;
        }

        static string serverId;

        /// <summary>The session's server keeps profiles.</summary>
        public static bool Active => NetGame.Active && NetState.Instance != null && NetState.Instance.ProfilesOn;

        /// <summary>This device controls its profile (always true without profiles).</summary>
        public static bool Controller { get; private set; } = true;

        /// <summary>The server had no room for a profile: nothing of this game is saved.</summary>
        public static bool Guest { get; private set; }

        /// <summary>The station menu: why an observer can't leave the main view (null = it can).</summary>
        public static string Refusal => Active && !Controller
            ? Localization.Extra("mpObserverRefusal", "Another device of yours controls this profile. Type /control in the chat once it is docked.")
            : null;

        /// <summary>NetState reached this client on a server with profiles: sign in, the world waits for the profile.</summary>
        public static void Begin(string server, int worldSeed)
        {
            serverId = server ?? "";
            seed = worldSeed;
            waiting = true;
            Controller = true;
            Guest = false;
            inSeq = -1;
            NetState.Instance.LoginRpc(PlayerPrefs.GetString(TokenKey, ""), DeviceId(), NetGame.Clean(NetGame.PlayerName));
        }

        static string TokenKey
        {
            get
            {
                string key = "mp_token_" + serverId;
                string name = NetGame.NameOverride;
                return string.IsNullOrEmpty(name) ? key : key + "_" + NetProfiles.Hash(name).Substring(0, 8);
            }
        }

        /// <summary>A label for this device: a hash of its id (the hardware id itself never leaves the device), the -mpname
        /// suffix for two games on one machine. Platforms without an id get a random one, kept.</summary>
        static string DeviceId()
        {
            string id = SystemInfo.deviceUniqueIdentifier;
            if (string.IsNullOrEmpty(id) || id == SystemInfo.unsupportedIdentifier)
            {
                id = PlayerPrefs.GetString("mp_device", "");
                if (id.Length == 0) { id = Guid.NewGuid().ToString("N"); PlayerPrefs.SetString("mp_device", id); PlayerPrefs.Save(); }
            }
            string label = NetProfiles.Hash(id).Substring(0, 16);
            string name = NetGame.NameOverride;
            return string.IsNullOrEmpty(name) ? label : label + "#" + NetProfiles.Hash(name).Substring(0, 6);
        }

        // ---- the profile coming in --------------------------------------------------------------------------

        internal static void OnProfileHeader(int seq, int count, string token, bool controller, bool guest, int home)
        {
            inSeq = seq;
            inGot = 0;
            inParts = new byte[Mathf.Max(0, count)][];
            inToken = token;
            inController = controller;
            inGuest = guest;
            if (count <= 0) Complete(null);
        }

        internal static void OnProfileChunk(int seq, int part, byte[] data)
        {
            if (seq != inSeq || inParts == null || part < 0 || part >= inParts.Length || inParts[part] != null) return;
            inParts[part] = data;
            if (++inGot == inParts.Length) Complete(NetProfiles.Unpack(inParts));
        }

        static void Complete(string json)
        {
            inParts = null;
            if (!string.IsNullOrEmpty(inToken)) { PlayerPrefs.SetString(TokenKey, inToken); PlayerPrefs.Save(); }
            Controller = inController;
            Guest = inGuest;
            bool applied = false;
            if (!string.IsNullOrEmpty(json))
            {
                if (SaveGame.TryParse(json, NetGame.Db, out var save, out string problem)) { SaveGame.ApplyProfile(save); applied = true; }
                else Debug.LogWarning("NetProfileClient: the server's profile didn't load: " + problem);
            }
            uploadTimer = UploadSeconds;
            if (waiting)
            {
                waiting = false;
                // The game starts docked where the profile last docked (SaveGame.ApplyProfile; a new pilot: Dis). Not the
                // faction's home any more: signing out and in was a free trip home.
                NetGame.EnterWorld(seed);
                return;
            }
            // Already playing (promoted, linked): the station again with the new profile.
            if (applied)
            {
                Session.DockedFromSpace = false;
                Session.LaunchedFromStation = false;
                SceneManager.LoadScene(StationScene);
            }
        }

        /// <summary>The server: this device controls its profile now, or not any more.</summary>
        internal static void OnRole(bool controller) => Controller = controller;

        // ---- uploads ----------------------------------------------------------------------------------------

        /// <summary>Sends the profile to the server (a controller only; 'handover' = asked to by the server while handing
        /// control to another device of the profile).</summary>
        public static void Upload(bool handover = false)
        {
            if (!Active || waiting || Guest || (!Controller && !handover) || NetState.Instance == null || !NetState.Instance.IsSpawned) return;
            if (SaveGame.HostSaves) return;   // the co-op host's game is in its save slots
            if (NetArenaClient.InMatch) return;   // nothing of an arena match is saved (its start uploaded the profile)
            uploadTimer = UploadSeconds;
            var parts = NetProfiles.Pack(SaveGame.ProfileJson());
            int seq = ++outSeq;
            for (int i = 0; i < parts.Count; i++) NetState.Instance.UploadChunkRpc(seq, i, parts.Count, parts[i]);
        }

        /// <summary>A persistent hosted world's host leaving: its game straight into its profile (NetProfiles.SaveHost). The
        /// co-op host's into the auto-save when docked (a save is made docked only, as in single player).</summary>
        public static void SaveHostNow()
        {
            if (!Active || waiting || Guest || !Controller || NetArenaClient.InMatch) return;
            if (SaveGame.HostSaves)
            {
                if (SceneManager.GetActiveScene().name == StationScene) SaveGame.AutoSave();
                return;
            }
            NetProfiles.SaveHost(SaveGame.ProfileJson());
        }

        /// <summary>NetPlayer (the owner, every frame): the periodic upload.</summary>
        public static void Tick()
        {
            if (!Active || waiting || !Controller) return;
            if ((uploadTimer -= Time.unscaledDeltaTime) <= 0f) Upload();
        }
    }
}
