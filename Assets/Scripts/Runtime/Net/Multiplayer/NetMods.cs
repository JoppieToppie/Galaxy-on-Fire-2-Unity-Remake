// NetMods.cs
// Mods in multiplayer (remake). The host chooses (the Host card's "Modded content" switch, PlayerPrefs
// "mp_allow_mods", off by default; a dedicated server's -allowmods):
//   Off      the session is the original game for everyone: every player's game runs it without mods
//            (ModManager.BeginSession with none), whatever they have on for single player.
//   Allowed  the session runs the host's active mods, in the host's order (a dedicated server: every usable mod in
//            its Mods folders). A joining game must have each of them
//            installed with the same files (ModInfo.NetHash: every file but those its mod.json's "multiplayerIgnore" lists,
//            the models and textures a phone build makes lighter; on or off for single player doesn't matter): its connection
//            data lists the mods it has (Payload), the server's approval refuses it otherwise and names the missing
//            ones. NetState carries the session's list; the joining game turns exactly those on for the session
//            (ApplyFromServer), so every game numbers the mod content the same (ModContent, a fresh numbering per session).
// The server browser lists the mods (NetLobby "mods" / "modnames") and marks a game this one can't join.
// The session's mods go when the session ends (End, from NetGame.OnMainMenu).

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetMods
    {
        public const int MaxListLength = 4000;

        /// <summary>The session this game hosts runs its mods (set before hosting).</summary>
        public static bool HostAllowsMods { get; set; }

        /// <summary>The running session's mods, "id@version#hash;..." ("" = none, or not in a session).</summary>
        public static string SessionList { get; private set; } = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => SessionList = "";

        /// <summary>A joining game: what it has installed ("id#hash;..."), for the server's check.</summary>
        public static string InstalledList
        {
            get
            {
                var s = string.Join(";", ModManager.Installed.Where(m => !m.Broken).Select(m => m.Id + "#" + m.NetHash));
                return s.Length > MaxListLength ? s.Substring(0, MaxListLength) : s;
            }
        }

        /// <summary>Every session starts without mods (a client until NetState says otherwise).</summary>
        public static void BeginClient()
        {
            SessionList = "";
            ModManager.BeginSession(null);
            NetGame.ResetDb();
        }

        /// <summary>Hosting: the session runs this game's single-player mods when modded content is allowed, else none.</summary>
        public static void BeginHost()
        {
            // A dedicated server has no mod browser: every usable mod in its Mods folders (in name order, dependencies first).
            var mods = !HostAllowsMods ? new List<ModInfo>()
                : DedicatedServer.Enabled ? ModManager.Installed.Where(m => !m.Broken).OrderBy(m => m.Id, StringComparer.Ordinal).ToList()
                : ModManager.SinglePlayerActive;
            ModManager.BeginSession(mods.Select(m => m.Id));
            SessionList = string.Join(";", ModManager.UserMods.Select(m => m.NetSignature));
            if (SessionList.Length > MaxListLength)
            {
                Debug.LogWarning("NetMods: too many mods for a session; it runs without them");
                ModManager.BeginSession(null);
                SessionList = "";
            }
            NetGame.ResetDb();
            Debug.Log(SessionList.Length > 0 ? $"NetMods: the session runs {SessionList}" : "NetMods: the session runs no mods");
        }

        /// <summary>Joined: the session's mods from NetState turned on for the session.</summary>
        public static void ApplyFromServer(string list)
        {
            SessionList = list ?? "";
            var ids = Parse(SessionList).Select(e => e.id).ToList();
            ModManager.BeginSession(ids);
            NetGame.ResetDb();
            if (ids.Count > 0) Debug.Log($"NetMods: the session runs {SessionList}");
            foreach (var e in Parse(SessionList))
                if (ModManager.Find(e.id)?.NetHash != e.hash) Debug.LogWarning($"NetMods: the session's mod {e.id} differs from this game's copy");
        }

        /// <summary>The session ended: back to the single-player mods.</summary>
        public static void End()
        {
            if (!ModManager.InSession) return;
            SessionList = "";
            ModManager.EndSession();
            NetGame.ResetDb();
        }

        public struct Entry { public string id, version, hash; }

        /// <summary>"id@version#hash;..." (or "id#hash;...") split.</summary>
        public static List<Entry> Parse(string list)
        {
            var l = new List<Entry>();
            foreach (var part in (list ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int h = part.LastIndexOf('#');
                string head = h >= 0 ? part.Substring(0, h) : part, hash = h >= 0 ? part.Substring(h + 1) : "";
                int v = head.IndexOf('@');
                l.Add(new Entry { id = v >= 0 ? head.Substring(0, v) : head, version = v >= 0 ? head.Substring(v + 1) : "", hash = hash });
            }
            return l;
        }

        /// <summary>The session's mods a game with 'installed' ("id#hash;...") lacks or has other files of ("name version").</summary>
        public static List<string> Missing(string sessionList, string installed)
        {
            var have = new HashSet<string>(Parse(installed).Select(e => e.id + "#" + e.hash), StringComparer.OrdinalIgnoreCase);
            var l = new List<string>();
            foreach (var e in Parse(sessionList))
                if (!have.Contains(e.id + "#" + e.hash))
                {
                    l.Add($"{ModManager.Find(e.id)?.Name ?? e.id} {e.version}".Trim());
                }
            return l;
        }

        /// <summary>Server (NetGame.Approve): why a joining game can't play this session's mods, null = it can.</summary>
        public static string Refusal(string installed)
        {
            if (SessionList.Length == 0) return null;
            var missing = Missing(SessionList, installed);
            if (missing.Count == 0) return null;
            return string.Format(Localization.Extra("mpNeedsMods",
                "Needs mods you don't have (or another version): {0}."),
                string.Join(", ", missing));
        }

        /// <summary>The server browser: this game lacks some of a listed game's mods (names), empty = it can join.</summary>
        public static List<string> MissingForListing(string listedMods) => Missing(listedMods, InstalledList);

        /// <summary>The session's mods by name for the listing ("Plasma Arsenal 1.0, ...").</summary>
        public static string SessionNames => string.Join(", ", ModManager.UserMods.Select(m => $"{m.Name} {m.Version}"));
    }
}
