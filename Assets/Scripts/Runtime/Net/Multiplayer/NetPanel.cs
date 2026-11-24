// NetPanel.cs
// Remake-only: what the station's Faction / Arena / Profile window (UI.MultiplayerWindow) shows, and how its buttons act. The window
// asks the server for a snapshot (Request -> NetState.PanelRequestRpc) while it is open (every RefreshSeconds, and
// right after a button); the server fills a State for that player (NetProfiles / NetFactions / NetArena .FillPanel) and
// sends it gzipped in chunks like a profile (NetProfiles.Pack; NetState.PanelChunkRpc). The buttons send the same chat
// commands a player can type (Command -> NetState.SendChatRpc: "/faction claim", "/duel Name voids" ...), so every rule
// stays in one place on the server; its answer comes back as a chat notice, which the window shows too.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetPanel
    {
        [Serializable]
        public class State
        {
            // Profile (NetProfiles).
            public bool profiles;            // the server keeps profiles (factions, the leaderboard and /link need them)
            public string profileId = "";
            public bool playerIdentityEnabled;
            public string playerRace, playerPortrait;
            public int devices;
            public bool controller = true, guest;
            // Faction (NetFactions).
            public bool inFaction;
            public string factionTag = "", factionName = "";
            public long bank;
            public int home = -1, rank;      // rank: 0 member, 1 officer, 2 leader
            public List<Member> members = new List<Member>();
            public List<ClaimRow> claims = new List<ClaimRow>();
            public List<string> factionInvites = new List<string>();   // "TAG|Name" of the factions inviting this player
            public List<FactionRow> factions = new List<FactionRow>();
            public int dockedStation = -1;   // where this player is docked (-1 = not docked)
            public string stationHolder = "";   // the tag of the faction holding it ("" = free)
            public bool stationClaimable;
            public int claimCost, siegeCost, maxClaims, toll;
            public string sieges = "";
            // Arena (NetArena).
            public List<Pilot> pilots = new List<Pilot>();
            public string duelFrom = "";
            public bool duelVoids;
            public int queue;                // 0 none, 1 the free-for-all queue, 2 the one with the Void fighters
            public int queueCount;
            public float queueStartsIn = -1f;
            public bool inMatch;
            public List<string> matches = new List<string>();
            public string top = "";
            // Moderation (NetModeration).
            public int role;                 // 0 player, 1 op, 2 admin
            public List<BanRow> bans = new List<BanRow>();
            // Admins (NetModeration): the server, the staff, every profile.
            public string serverStatus = "";
            public List<StaffRow> staff = new List<StaffRow>();
            public List<ProfileRow> profileRows = new List<ProfileRow>();   // every profile (admins; 'profiles' says whether the server keeps them)
            public List<SettingRow> settings = new List<SettingRow>();
        }

        /// <summary>A server setting (NetServerSettings): kind 0 number, 1 toggle, 2 text, 3 password (no value sent).</summary>
        [Serializable] public class SettingRow { public string key = "", label = "", value = "", note = ""; public int kind; public bool cli, passwordSet; }

        [Serializable] public class StaffRow { public string name = ""; public int role; public bool online; }
        [Serializable] public class ProfileRow { public string id = "", name = "", lastSeen = ""; public int role, devices; public bool online, banned; }

        [Serializable] public class Member { public string name = ""; public int rank; public bool online; }
        [Serializable] public class ClaimRow { public int station; public string name = ""; public bool home; public float daysLeft; public bool sieged; public int garrisonSize, garrisonLevel = 1, vat; }
        [Serializable] public class FactionRow { public string tag = "", name = ""; public int members, claims; }
        [Serializable] public class Pilot { public long client; public string name = "", tag = ""; public bool docked, inMatch, self; public int role; }
        [Serializable] public class BanRow { public string name = "", account = "", reason = "", by = "", left = ""; }

        public const float RefreshSeconds = 2f;

        /// <summary>The latest snapshot (null until the first arrives).</summary>
        public static State Latest { get; private set; }

        /// <summary>A new snapshot arrived (the window rebuilds what changed).</summary>
        public static event Action Changed;

        static string latestJson;
        static int inSeq = -1, inGot;
        static byte[][] inParts;
        static int outSeq;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Latest = null; latestJson = null; inSeq = -1; inParts = null; outSeq = 0; Changed = null; }

        /// <summary>The window: a fresh snapshot, please.</summary>
        public static void Request()
        {
            if (NetGame.Active && NetState.Instance != null && NetState.Instance.IsSpawned) NetState.Instance.PanelRequestRpc();
        }

        /// <summary>A button: the chat command for it (the server's answer arrives as a notice), then a fresh snapshot.</summary>
        public static void Command(string text)
        {
            if (!NetGame.Active || NetState.Instance == null || !NetState.Instance.IsSpawned) return;
            NetState.Instance.SendChatRpc(NetChat.Clean(text), false);
            Request();
        }

        // ---- the server ---------------------------------------------------------------------------------------

        /// <summary>Server: the snapshot for one player, in chunks.</summary>
        internal static void Send(ulong client)
        {
            var s = new State();
            var p = NetSquad.Find(client);
            if (p != null) s.dockedStation = p.InHangar ? p.Station : -1;
            foreach (var other in NetPlayer.All)
                if (other != null && other.IsSpawned)
                    s.pilots.Add(new Pilot { client = (long)other.OwnerClientId, name = other.DisplayName, tag = other.FactionTag, docked = other.InHangar,
                                             inMatch = NetArena.IsArenaOrbit(other.Station), self = other.OwnerClientId == client });
            NetProfiles.FillPanel(client, s);
            NetModeration.FillPanel(client, s);   // roles and the Admin tab (a fresh hosted session: the host's)
            if (NetProfiles.Enabled) NetFactions.FillPanel(client, s);
            NetArena.FillPanel(client, s);
            var parts = NetProfiles.Pack(JsonUtility.ToJson(s));
            int seq = ++outSeq;
            for (int i = 0; i < parts.Count; i++) NetState.Instance?.PanelChunk(client, seq, i, parts.Count, parts[i]);
        }

        // ---- the player -----------------------------------------------------------------------------------

        internal static void OnChunk(int seq, int part, int count, byte[] data)
        {
            if (count <= 0 || count > 64 || part < 0 || part >= count || data == null) return;
            if (seq != inSeq || inParts == null || inParts.Length != count) { inSeq = seq; inParts = new byte[count][]; inGot = 0; }
            if (inParts[part] != null) return;
            inParts[part] = data;
            if (++inGot < count) return;
            string json = NetProfiles.Unpack(inParts);
            inParts = null;
            if (json == null || json == latestJson) return;
            latestJson = json;
            try { Latest = JsonUtility.FromJson<State>(json); }
            catch (Exception e) { Debug.LogWarning("NetPanel: a snapshot didn't read: " + e.Message); return; }
            Changed?.Invoke();
        }
    }
}
