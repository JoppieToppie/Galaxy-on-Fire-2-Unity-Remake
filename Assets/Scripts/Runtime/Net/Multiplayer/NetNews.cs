// NetNews.cs
// Remake-only: the session's own news on the station's ticker (NewsTicker / StationMenu), the GalNet of a multiplayer
// sector. The server writes an item when something happens that the others would want to hear about, keeps the latest
// Keep items (a dedicated server with profiles keeps them in news.json beside the profiles, so they outlive a restart)
// and sends each new one to everyone (NetState.NewsRpc); a joining player gets the recent ones (SendAll, from
// NetPlayer.OnNetworkSpawn). Every player's ticker shows the items of the last TickerHours first, newest first, each with
// a coloured "+++ KIND +++" kicker (BREAKING for the fresh ones) and its age, then the game's own random news.
// What makes the news (all on the server):
//   territory   a faction claims, gives up or loses (lapses) a station (NetFactions);
//   war         a siege is declared, begins, the station is taken or held (NetFactions);
//   factions       a faction is founded or disbanded (NetFactions);
//   arena       a duel's winner and score, a free-for-all's winner (NetArena; not draws or forfeits);
//   defence     players repel a raider attack on an orbit (DefenseReportRpc: the orbit's authority reports who downed the
//               raiders once none is left, at least MinDefenseKills; checked: the sender runs that orbit, the pilots are
//               there) or break the pirate siege of the Kaamo Club (NetState.SiegeWonRpc);
//   pilots      a new profile signs in (NetProfiles);
//   galnet      an admin's own item (/news <text>, the console's news; "/news clear" empties the list).
// Names are the players' own text: the ticker is a rich-text Label, so '<' and '>' are swapped for look-alikes (Safe).
// Items are written in English on the server (players of other languages see them in English).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetNews
    {
        public enum Kind { Breaking, Territory, War, Arena, Defense, Faction, Galnet, Pilot, StationAnnouncement }

        public sealed class Item
        {
            public long id, time;   // time: Unix seconds
            public Kind kind;
            public int station = -1;
            public string text = "";
        }

        const int Keep = 40, SendOnJoin = 15, TickerItems = 8;
        const long MaxAgeSeconds = 3 * 86400;
        const float TickerHours = 24f, FreshMinutes = 15f;
        public const int MinDefenseKills = 3;
        public const int MaxTextLength = 200;

        [Serializable] class SavedItem { public long id, time; public int kind, station; public string text = ""; }
        [Serializable] class SavedList { public List<SavedItem> items = new List<SavedItem>(); }

        // The server's list (also the host's) and this player's (what arrived).
        static readonly List<Item> serverItems = new List<Item>();
        static readonly List<Item> items = new List<Item>();
        static readonly Dictionary<string, long> throttled = new Dictionary<string, long>();
        static long nextId;

        /// <summary>The news this player has changed (the docked station rebuilds its ticker).</summary>
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { serverItems.Clear(); items.Clear(); throttled.Clear(); nextId = 0; Changed = null; }

        static long UnixNow => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        /// <summary>NetGame: a new session (both sides): this player's list empty.</summary>
        public static void Reset()
        {
            items.Clear();
            Changed?.Invoke();
        }

        // ---- the server ----------------------------------------------------------------------------------------

        static string PathOf => NetProfiles.Enabled && !string.IsNullOrEmpty(NetProfiles.Folder) ? Path.Combine(NetProfiles.Folder, "news.json") : null;

        /// <summary>NetState spawned on the server: the saved news (a dedicated server with profiles), else none.</summary>
        internal static void ServerStart()
        {
            serverItems.Clear();
            throttled.Clear();
            nextId = 0;
            string path = PathOf;
            try
            {
                if (path != null && File.Exists(path))
                {
                    var saved = JsonUtility.FromJson<SavedList>(File.ReadAllText(path));
                    if (saved?.items != null)
                        foreach (var s in saved.items)
                            serverItems.Add(new Item { id = s.id, time = s.time, kind = (Kind)Mathf.Clamp(s.kind, 0, (int)Kind.StationAnnouncement), station = s.station, text = s.text ?? "" });
                }
            }
            catch (Exception e) { Debug.LogWarning($"Server: news.json unreadable ({e.Message}); starting without news."); serverItems.Clear(); }
            Prune();
            foreach (var it in serverItems) nextId = Math.Max(nextId, it.id);
        }

        static void Prune()
        {
            long now = UnixNow;
            serverItems.RemoveAll(i => now - i.time > MaxAgeSeconds);
            serverItems.Sort((a, b) => a.time.CompareTo(b.time));
            if (serverItems.Count > Keep) serverItems.RemoveRange(0, serverItems.Count - Keep);
        }

        static void Save()
        {
            string path = PathOf;
            if (path == null) return;
            var saved = new SavedList();
            foreach (var i in serverItems) saved.items.Add(new SavedItem { id = i.id, time = i.time, kind = (int)i.kind, station = i.station, text = i.text });
            NetProfiles.Write(path, JsonUtility.ToJson(saved, true));
        }

        static bool IsServer => NetState.Instance != null && NetState.Instance.IsSpawned && NetState.Instance.IsServer;

        /// <summary>Server: a news item for everyone. 'throttleKey': nothing more under that key for 'throttleSeconds' (one
        /// item per raid, per station...). False = not posted (no session, or throttled).</summary>
        public static bool Post(Kind kind, string text, int station = -1, string throttleKey = null, float throttleSeconds = 0f)
        {
            if (!IsServer) return false;
            text = Clean(text);
            if (text.Length == 0) return false;
            long now = UnixNow;
            if (throttleKey != null)
            {
                if (throttled.TryGetValue(throttleKey, out long until) && now < until) return false;
                throttled[throttleKey] = now + (long)throttleSeconds;
            }
            var item = new Item { id = ++nextId, time = now, kind = kind, station = station, text = text };
            serverItems.Add(item);
            Prune();
            Save();
            NetState.Instance.BroadcastNews(Pack(item));
            Debug.Log($"Server: news [{kind}] {text}");
            return true;
        }

        /// <summary>Server: a joining player gets the recent news (reliable RPCs to one client arrive in order).</summary>
        internal static void SendAll(ulong client)
        {
            if (!IsServer) return;
            Prune();
            int from = Math.Max(0, serverItems.Count - SendOnJoin);
            NetState.Instance.SendNews(client, "", true);   // the list starts over
            for (int i = from; i < serverItems.Count; i++) NetState.Instance.SendNews(client, Pack(serverItems[i]), false);
        }

        /// <summary>/news clear: the list emptied, for everyone.</summary>
        internal static void Clear()
        {
            if (!IsServer) return;
            serverItems.Clear();
            Save();
            foreach (ulong c in NetGame.ClientIds) NetState.Instance.SendNews(c, "", true);
        }

        /// <summary>One line, no tags, at most MaxTextLength.</summary>
        static string Clean(string text)
        {
            text = (text ?? "").Replace('\n', ' ').Replace('\r', ' ').Replace('|', '/').Trim();
            return text.Length > MaxTextLength ? text.Substring(0, MaxTextLength) : text;
        }

        /// <summary>A player's own text (names, faction names, an admin's item) for the rich-text ticker: no tags.</summary>
        public static string Safe(string text) => (text ?? "").Replace('<', '‹').Replace('>', '›');

        /// <summary>"[TAG] Name".</summary>
        internal static string FactionName(string tag, string name) => $"[{Safe(tag)}] {Safe(name)}";

        /// <summary>"Var Hastra (Mido)".</summary>
        internal static string Place(int station)
        {
            if (station == Session.VoidOrbit) return "the Void";
            var st = NetGame.Db?.Stations.Find(s => s.index == station);
            if (st == null) return $"station {station}";
            return string.IsNullOrEmpty(st.systemName) ? st.name : $"{st.name} ({st.systemName})";
        }

        /// <summary>"A", "A and B", "A, B and C", "A, B, C and 2 others".</summary>
        internal static string Names(IList<string> names)
        {
            var n = new List<string>();
            foreach (var s in names) { if (string.IsNullOrEmpty(s)) continue; string safe = Safe(s); if (!n.Contains(safe)) n.Add(safe); }
            if (n.Count == 0) return "Unknown pilots";
            if (n.Count == 1) return n[0];
            if (n.Count <= 3) return string.Join(", ", n.GetRange(0, n.Count - 1)) + " and " + n[n.Count - 1];
            return $"{n[0]}, {n[1]}, {n[2]} and {n.Count - 3} other{(n.Count - 3 == 1 ? "" : "s")}";
        }

        /// <summary>What a raider race is called in a headline.</summary>
        internal static string Raiders(int race)
        {
            switch (race)
            {
                case GoF2Remake.Flight.Standing.Pirate: return "pirate";
                case GoF2Remake.Flight.Standing.Void: return "Void";
                case GoF2Remake.Flight.Standing.Specter: return "Specter";
                default: return race >= 0 && race < 4 ? Localization.Get(406 + race) : "hostile";
            }
        }

        /// <summary>Server: an orbit's raiders are all down (DefenseReportRpc, already checked): who downed them.</summary>
        internal static void Defended(int station, int race, int kills, List<string> pilots)
        {
            string who = Names(pilots);
            string verb = pilots.Count == 1 ? "repels" : "repel";
            Post(Kind.Defense, $"{who} {verb} a {Raiders(race)} raid on {Place(station)}: {kills} ships down", station, $"defense:{station}", 600f);
        }

        // ---- the network ---------------------------------------------------------------------------------------

        static string Pack(Item i) => $"{i.id}|{(int)i.kind}|{i.station}|{i.time}|{i.text}";

        static Item Unpack(string packed)
        {
            var p = (packed ?? "").Split(new[] { '|' }, 5);
            if (p.Length < 5 || !long.TryParse(p[0], out long id) || !int.TryParse(p[1], out int kind) || !int.TryParse(p[2], out int station)
                || !long.TryParse(p[3], out long time)) return null;
            return new Item { id = id, kind = (Kind)Mathf.Clamp(kind, 0, (int)Kind.StationAnnouncement), station = station, time = time, text = Clean(p[4]) };
        }

        /// <summary>NetState: an item from the server ("" with 'reset' = the list starts over).</summary>
        internal static void OnReceive(string packed, bool reset)
        {
            if (reset) items.Clear();
            var item = string.IsNullOrEmpty(packed) ? null : Unpack(packed);
            if (item != null && !items.Exists(i => i.id == item.id)) items.Add(item);
            items.Sort((a, b) => b.time.CompareTo(a.time));
            if (items.Count > Keep) items.RemoveRange(Keep, items.Count - Keep);
            Changed?.Invoke();
        }

        // ---- the ticker ----------------------------------------------------------------------------------------

        static readonly (string label, string color)[] Kickers =
        {
            ("BREAKING", "#ff5a4f"), ("TERRITORY", "#f5a524"), ("WAR", "#ff7a1a"), ("ARENA", "#38bdf8"),
            ("DEFENCE", "#4ade80"), ("FACTIONS", "#c084fc"), ("GALNET", "#e2e8f0"), ("PILOTS", "#94a3b8"), ("STATION ANNOUNCEMENT", "#67e8f9"),
        };

        /// <summary>The session's news for the ticker (rich text, each item followed by NewsTicker.Separator), "" = none:
        /// the last day's items, newest first; the fresh ones say BREAKING.</summary>
        public static string Ticker(int dockedStation)
        {
            if (!NetGame.Active || items.Count == 0) return "";
            long now = UnixNow;
            var sb = new StringBuilder();
            int shown = 0;
            foreach (var it in items)
            {
                float ageMinutes = (now - it.time) / 60f;
                if (ageMinutes > TickerHours * 60f || shown >= TickerItems) break;
                if (it.kind == Kind.StationAnnouncement && it.station != dockedStation) continue;
                if (shown == 0) sb.Append("<b><color=#f5a524>GALNET</color></b> <color=#7f8a99>sector news</color>").Append(NewsTicker.Separator);
                shown++;
                var (label, color) = Kickers[(int)it.kind];
                bool fresh = ageMinutes < FreshMinutes && it.kind != Kind.Pilot && it.kind != Kind.Galnet;
                sb.Append(fresh ? $"<b><color=#ff5a4f>+++ BREAKING</color> <color={color}>{label} +++</color></b> "
                                : $"<b><color={color}>+++ {label} +++</color></b> ");
                sb.Append(it.station == dockedStation && it.station >= 0 ? $"<b>{it.text}</b>" : it.text);
                sb.Append(" <color=#7f8a99><size=80%>· ").Append(Age(ageMinutes)).Append("</size></color>");
                sb.Append(NewsTicker.Separator);
            }
            return sb.ToString();
        }

        internal static List<string> StationAnnouncements(int orbit)
        {
            var lines = new List<string>();
            long now = UnixNow;
            foreach (var item in items)
                if (item.kind == Kind.StationAnnouncement && item.station == orbit && now - item.time <= MaxAgeSeconds)
                    lines.Add(Age(Math.Max(0f, (now - item.time) / 60f)) + " · " + item.text);
            return lines;
        }

        static string Age(float minutes)
        {
            if (minutes < 1f) return "just now";
            if (minutes < 60f) return $"{Mathf.FloorToInt(minutes)} min ago";
            int h = Mathf.FloorToInt(minutes / 60f);
            return h == 1 ? "1 hour ago" : $"{h} hours ago";
        }
    }
}
