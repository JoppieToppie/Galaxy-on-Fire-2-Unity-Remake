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
        public enum Kind { Breaking, Territory, War, Arena, Defense, Faction, Galnet, Pilot, StationAnnouncement, StationBroadcast, FactionBroadcast, Commerce, Freight }

        public sealed class Item
        {
            public long id, time;   // time: Unix seconds
            public Kind kind;
            public int station = -1;
            public string text = "";
            // Server-only audit metadata; never included in the client news packet.
            public string actor, authority;
        }

        const int Keep = 40, SendOnJoin = 15, TickerItems = 8;
        const long MaxAgeSeconds = 3 * 86400;
        const float TickerHours = 24f, FreshMinutes = 15f;
        public const int MinDefenseKills = 3;
        public const int MaxTextLength = 200;
        public const int FreightDistressTextLimit = 1024;
        public const int StationAnnouncementLimit = 200;
        public const int OfficialMessageLimit = 140; // faction broadcasts
        internal const string FreightDistressPrefix = "Distress call received from ";
        public const int OfficialCooldownSeconds = 300;

        [Serializable] class SavedItem { public long id, time; public int kind, station; public string text = "", actor, authority; }
        [Serializable] class SavedCooldown { public string authority; public long until; }
        [Serializable] class SavedList
        {
            public List<SavedItem> items = new List<SavedItem>();
            public List<SavedCooldown> officialCooldowns = new List<SavedCooldown>();
            public List<SavedItem> officialAudit = new List<SavedItem>();
        }

        // The server's list (also the host's) and this player's (what arrived).
        static readonly List<Item> serverItems = new List<Item>();
        static readonly List<Item> items = new List<Item>();
        static readonly Dictionary<string, long> throttled = new Dictionary<string, long>();
        static readonly Dictionary<string, long> officialCooldowns = new Dictionary<string, long>();
        static readonly List<SavedItem> officialAudit = new List<SavedItem>();
        static long nextId;

        /// <summary>The news this player has changed (the docked station rebuilds its ticker).</summary>
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { serverItems.Clear(); items.Clear(); throttled.Clear(); officialCooldowns.Clear(); officialAudit.Clear(); nextId = 0; Changed = null; }

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
            officialCooldowns.Clear(); officialAudit.Clear();
            nextId = 0;
            string path = PathOf;
            try
            {
                if (path != null && File.Exists(path))
                {
                    var saved = JsonUtility.FromJson<SavedList>(File.ReadAllText(path));
                    if (saved?.items != null)
                        foreach (var s in saved.items)
                            serverItems.Add(new Item { id = s.id, time = s.time, kind = (Kind)Mathf.Clamp(s.kind, 0, (int)Kind.Freight), station = s.station, text = s.text ?? "", actor = s.actor, authority = s.authority });
                    if (saved?.officialCooldowns != null)
                        foreach (var c in saved.officialCooldowns)
                            if (c != null && !string.IsNullOrEmpty(c.authority) && c.until > UnixNow) officialCooldowns[c.authority] = c.until;
                    if (saved?.officialAudit != null) officialAudit.AddRange(saved.officialAudit);
                }
            }
            catch (Exception e) { Debug.LogWarning($"Server: news.json unreadable ({e.Message}); starting without news."); serverItems.Clear(); }
            // Legacy freight notices were global and recorded the destination only.
            foreach (var it in serverItems) nextId = Math.Max(nextId, it.id);
            serverItems.RemoveAll(i => i.kind == Kind.Commerce && IsLegacyFreight(i.text));
            Prune();
            foreach (var it in serverItems) nextId = Math.Max(nextId, it.id);
            foreach (var audit in officialAudit) if (audit != null) nextId = Math.Max(nextId, audit.id);
        }

        internal static bool IsLegacyFreight(string text) =>
            !string.IsNullOrEmpty(text) && (text.StartsWith("Delayed freight bound for ", StringComparison.Ordinal) ||
                text.StartsWith("Shipment of ", StringComparison.Ordinal) && text.Contains(" delayed near ") ||
                text.StartsWith("Freight bound for ", StringComparison.Ordinal) && text.Contains("Player pirate kills:"));

        internal static int FreightSystem(int station) =>
            station < 0 ? -1 : NetGame.Db?.Stations.Find(s => s.index == station)?.system ?? -1;

        internal static bool VisibleInSystem(Kind kind, int eventSystem, int viewerSystem) =>
            kind != Kind.Freight || eventSystem >= 0 && eventSystem == viewerSystem;

        internal static bool NeedsDestinationCopy(int incidentSystem, int destinationSystem) =>
            destinationSystem >= 0 && destinationSystem != incidentSystem;

        static bool VisibleAt(Item item, int station) =>
            item.kind != Kind.Freight || VisibleInSystem(item.kind, FreightSystem(item.station), FreightSystem(station));

        // The incident and destination market are affected; no transit-only systems.
        internal static void PostFreight(string text, int incident, int destination)
        {
            int incidentSystem = FreightSystem(incident), destinationSystem = FreightSystem(destination);
            if (incidentSystem >= 0) Post(Kind.Freight, text, incident);
            if (NeedsDestinationCopy(incidentSystem, destinationSystem)) Post(Kind.Freight, text, destination);
        }

        // A distress request belongs to the incident system, even if no pilot is currently there.
        // Single-player freight history is station-scoped, so publish a copy to each station in that system.
        internal static bool PostFreightDistress(Database db, int incident, int raidTag, string text)
        {
            int system = db?.Stations.Find(s => s.index == incident)?.system ?? -1;
            if (system < 0) return false;
            string key = "freight-distress-" + raidTag;
            if (!LocalPublishingAllowed) return Post(Kind.Freight, text, incident, key, 86400f);
            bool posted = false;
            foreach (var station in db.Stations)
                if (station.system == system)
                    posted |= Post(Kind.Freight, text, station.index, key + "-" + station.index, 86400f);
            return posted;
        }

        static void Prune()
        {
            long now = UnixNow;
            serverItems.RemoveAll(i => now - i.time > MaxAgeSeconds);
            serverItems.Sort((a, b) => a.time.CompareTo(b.time));
            var freightCounts = new Dictionary<int, int>();
            int globalCount = 0;
            for (int i = serverItems.Count - 1; i >= 0; i--)
            {
                var item = serverItems[i];
                if (item.kind != Kind.Freight)
                {
                    if (++globalCount > Keep) serverItems.RemoveAt(i);
                    continue;
                }
                int system = FreightSystem(item.station);
                freightCounts.TryGetValue(system, out int count);
                if (system < 0 || count >= SendOnJoin) serverItems.RemoveAt(i);
                else freightCounts[system] = count + 1;
            }
        }

        static bool Save()
        {
            string path = PathOf;
            if (path == null) return false;
            var saved = new SavedList();
            foreach (var i in serverItems) saved.items.Add(ToSaved(i));
            foreach (var c in officialCooldowns)
                if (c.Value > UnixNow) saved.officialCooldowns.Add(new SavedCooldown { authority = c.Key, until = c.Value });
            saved.officialAudit.AddRange(officialAudit);
            return NetProfiles.Write(path, JsonUtility.ToJson(saved, true));
        }

        static SavedItem ToSaved(Item i) => new SavedItem { id = i.id, time = i.time, kind = (int)i.kind, station = i.station, text = i.text, actor = i.actor, authority = i.authority };

        internal static long OfficialReadyUtc(string authority) => authority != null && officialCooldowns.TryGetValue(authority, out long until) ? until : 0;

        // Called only after the station/faction command has authenticated the publishing authority.
        internal static string PostOfficial(Kind kind, string authority, string name, string actor, string message, int station = -1)
        {
            if (!IsServer || PathOf == null || string.IsNullOrEmpty(authority) || string.IsNullOrEmpty(actor)) return "Official broadcasts require a persistent controlled profile.";
            if (kind != Kind.StationBroadcast && kind != Kind.FactionBroadcast) return "Invalid broadcast authority.";
            message = (message ?? "").Trim();
            int limit = kind == Kind.StationBroadcast ? StationAnnouncementLimit : OfficialMessageLimit;
            if (message.Length == 0 || message.Length > limit) return $"Write a broadcast of 1–{limit} characters.";
            long now = UnixNow, previous = OfficialReadyUtc(authority);
            if (previous > now) return $"Broadcast cooldown: {previous - now} seconds remaining for this authority.";
            string content = Clean(kind, Safe(name + ": " + message));
            var item = new Item { id = ++nextId, time = now, kind = kind, station = station, text = content, actor = actor, authority = authority };
            var previousItems = new List<Item>(serverItems);
            officialCooldowns[authority] = now + OfficialCooldownSeconds;
            serverItems.Add(item); Prune();
            var audit = ToSaved(item); officialAudit.Add(audit);
            if (!Save())
            {
                officialAudit.Remove(audit);
                serverItems.Clear(); serverItems.AddRange(previousItems);
                if (previous > 0) officialCooldowns[authority] = previous; else officialCooldowns.Remove(authority);
                return "The broadcast could not be saved; no cooldown was consumed.";
            }
            NetState.Instance.BroadcastNews(Pack(item));
            Debug.Log($"Server: official broadcast [{kind}] {authority} by {actor}: {content}");
            return "Official broadcast published to GalNet.";
        }

        static bool IsServer => NetState.Instance != null && NetState.Instance.IsSpawned && NetState.Instance.IsServer;

        /// <summary>Server: a news item for everyone. 'throttleKey': nothing more under that key for 'throttleSeconds' (one
        /// item per raid, per station...). False = not posted (no session, or throttled).</summary>
        public static bool Post(Kind kind, string text, int station = -1, string throttleKey = null, float throttleSeconds = 0f)
        {
            if ((int)kind < 0 || (int)kind > (int)Kind.Freight) return false;
            if (!IsServer) return LocalPublishingAllowed && PostLocal(kind, text, station, throttleKey, throttleSeconds);
            if (kind == Kind.Freight && FreightSystem(station) < 0) return false;
            text = Clean(kind, Safe(text));
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
            if (kind == Kind.Freight)
            {
                string packed = Pack(item);
                foreach (var pilot in NetPlayer.All)
                    if (pilot != null && pilot.IsSpawned && VisibleAt(item, pilot.Station))
                        NetState.Instance.SendNews(pilot.OwnerClientId, packed, false);
            }
            else NetState.Instance.BroadcastNews(Pack(item));
            Debug.Log($"Server: news [{kind}] {text}");
            return true;
        }

        /// <summary>Server: a joining player gets the recent news (reliable RPCs to one client arrive in order).</summary>
        internal static void SendAll(ulong client)
        {
            if (!IsServer) return;
            Prune();
            int station = -1;
            foreach (var pilot in NetPlayer.All)
                if (pilot != null && pilot.IsSpawned && pilot.OwnerClientId == client) { station = pilot.Station; break; }
            var selected = new List<Item>();
            int global = 0, local = 0;
            for (int i = serverItems.Count - 1; i >= 0; i--)
            {
                var item = serverItems[i];
                if (!VisibleAt(item, station)) continue;
                if (item.kind == Kind.Freight) { if (local++ >= SendOnJoin) continue; }
                else if (global++ >= SendOnJoin) continue;
                selected.Add(item);
            }
            NetState.Instance.SendNews(client, "", true);
            for (int i = selected.Count - 1; i >= 0; i--)
                NetState.Instance.SendNews(client, Pack(selected[i]), false);
        }

        /// <summary>/news clear: the list emptied, for everyone.</summary>
        internal static void Clear()
        {
            if (!IsServer)
            {
                if (!LocalPublishingAllowed) return;
                Session.ScriptData.Remove(LocalNewsKey);
                Session.ScriptData.Remove(LegacyLocalNewsKey);
                Changed?.Invoke();
                return;
            }
            serverItems.Clear();
            Save();
            foreach (ulong c in NetGame.ClientIds) NetState.Instance.SendNews(c, "", true);
        }

        /// <summary>One line with a type-specific cap, including room for station attribution.</summary>
        static string Clean(Kind kind, string text)
        {
            text = (text ?? "").Replace('\n', ' ').Replace('\r', ' ').Replace('|', '/').Trim();
            int limit = MaxTextLength;
            if (kind == Kind.Freight && text.StartsWith(FreightDistressPrefix, StringComparison.Ordinal))
                limit = FreightDistressTextLimit;
            else if (kind == Kind.StationAnnouncement)
                limit = StationAnnouncementLimit + NetPlayerStations.StationNameMaxLength + 3; // [name] + space
            else if (kind == Kind.StationBroadcast)
                limit = StationAnnouncementLimit + NetPlayerStations.StationNameMaxLength + 2; // name + colon + space
            return text.Length > limit ? text.Substring(0, limit) : text;
        }

        /// <summary>A player's own text (names, faction names, an admin's item) for the rich-text ticker: no tags.</summary>
        public static string Safe(string text) => (text ?? "").Replace('<', '‹').Replace('>', '›');

        /// <summary>"[TAG] Name".</summary>
        internal static string FactionName(string tag, string name) => $"[{Safe(tag)}] {Safe(name)}";

        /// <summary>"Var Hastra (Mido)".</summary>
        internal static string Place(int station)
        {
            if (station == Session.VoidOrbit) return "the Void";
            var st = (LocalPublishingAllowed ? Database.Shared : NetGame.Db)?.Stations.Find(s => s.index == station);
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

        internal static Item Unpack(string packed)
        {
            var p = (packed ?? "").Split(new[] { '|' }, 5);
            if (p.Length < 5 || !long.TryParse(p[0], out long id) || !int.TryParse(p[1], out int kind) || !int.TryParse(p[2], out int station)
                || !long.TryParse(p[3], out long time)) return null;
            var newsKind = (Kind)Mathf.Clamp(kind, 0, (int)Kind.Freight);
            return new Item { id = id, kind = newsKind, station = station, time = time, text = Clean(newsKind, p[4]) };
        }

        /// <summary>NetState: an item from the server ("" with 'reset' = the list starts over).</summary>
        internal static void OnReceive(string packed, bool reset)
        {
            if (reset) items.Clear();
            var item = string.IsNullOrEmpty(packed) ? null : Unpack(packed);
            if (item != null && !items.Exists(i => i.id == item.id)) items.Add(item);
            items.Sort((a, b) => b.time.CompareTo(a.time));
            int global = 0, local = 0;
            for (int i = 0; i < items.Count;)
            {
                bool overflow = items[i].kind == Kind.Freight ? ++local > SendOnJoin : ++global > Keep;
                if (overflow) items.RemoveAt(i); else i++;
            }
            Changed?.Invoke();
        }

        // ---- the ticker ----------------------------------------------------------------------------------------

        static readonly (string label, string color)[] Kickers =
        {
            ("BREAKING", "#ff5a4f"), ("TERRITORY", "#f5a524"), ("WAR", "#ff7a1a"), ("ARENA", "#38bdf8"),
            ("DEFENCE", "#4ade80"), ("FACTIONS", "#c084fc"), ("GALNET", "#e2e8f0"), ("PILOTS", "#94a3b8"), ("STATION ANNOUNCEMENT", "#67e8f9"), ("STATION BROADCAST", "#e4ba78"), ("FACTION BROADCAST", "#c084fc"), ("COMMERCE", "#f5a524"), ("LOCAL FREIGHT", "#f5a524"),
        };

        /// <summary>The session's news for the ticker (rich text, each item followed by NewsTicker.Separator), "" = none:
        /// the last day's items, newest first; the fresh ones say BREAKING.</summary>
        // Store news in the active save, not in static memory or multiplayer profiles.
        const string LocalNewsKey = "__native_news_v1";
        const string LegacyLocalNewsKey = "__native_freight_news";
        public static bool LocalPublishingAllowed => !NetGame.SessionGame && !NetGame.Active &&
            !NetSession.SessionGame && !NetSession.Active && !CoopCampaign.Active;

        [Serializable] sealed class LocalFreightNews { public string destination, text; public long time; }
        [Serializable] sealed class LocalFreightList { public List<LocalFreightNews> items = new List<LocalFreightNews>(); }
        [Serializable] sealed class LocalNewsItem
        {
            public int kind;
            public string station, text;
            public long time;
        }
        [Serializable] sealed class LocalNewsList
        {
            public List<LocalNewsItem> items = new List<LocalNewsItem>();
            public List<SavedCooldown> cooldowns = new List<SavedCooldown>();
        }

        static LocalNewsList ReadLocalNews()
        {
            try
            {
                if (Session.ScriptData.TryGetValue(LocalNewsKey, out string json))
                {
                    var saved = JsonUtility.FromJson<LocalNewsList>(json) ?? new LocalNewsList();
                    saved.items ??= new List<LocalNewsItem>();
                    saved.cooldowns ??= new List<SavedCooldown>();
                    return saved;
                }
                // Read legacy reports immediately; migrate on the next successful publication.
                var result = new LocalNewsList();
                if (Session.ScriptData.TryGetValue(LegacyLocalNewsKey, out string legacyJson))
                {
                    var legacy = JsonUtility.FromJson<LocalFreightList>(legacyJson);
                    if (legacy?.items != null)
                        foreach (var item in legacy.items)
                            if (item != null && !string.IsNullOrEmpty(item.destination))
                                result.items.Add(new LocalNewsItem { kind = (int)Kind.Freight, station = item.destination,
                                    text = item.text, time = item.time });
                }
                return result;
            }
            catch (Exception e) { Debug.LogWarning("Local news unreadable: " + e.Message); }
            return new LocalNewsList();
        }

        static string DestinationKey(int station) => GoF2Remake.Modding.ModWorld.StationKey(station) ??
            station.ToString(System.Globalization.CultureInfo.InvariantCulture);

        static bool StationScoped(Kind kind) => kind == Kind.Freight || kind == Kind.StationAnnouncement;

        static bool LocalVisible(LocalNewsItem item, string station) =>
            !StationScoped((Kind)item.kind) || item.station == station;

        static void PruneLocal(LocalNewsList list, long now)
        {
            list.items.RemoveAll(i => i == null || i.kind < 0 || i.kind > (int)Kind.Freight ||
                string.IsNullOrEmpty(i.text) || now - i.time > MaxAgeSeconds ||
                StationScoped((Kind)i.kind) && string.IsNullOrEmpty(i.station));
            // Items are inserted newest-first; preserve order for same-second headlines.
            int global = 0;
            var stationCounts = new Dictionary<(int, string), int>();
            list.items.RemoveAll(i =>
            {
                if (!StationScoped((Kind)i.kind)) return ++global > Keep;
                var key = (i.kind, i.station);
                stationCounts.TryGetValue(key, out int count);
                stationCounts[key] = count + 1;
                return count >= SendOnJoin;
            });
            if (list.items.Count > 2048) list.items.RemoveRange(2048, list.items.Count - 2048);
            list.cooldowns.RemoveAll(c => c == null || c.until <= now || string.IsNullOrEmpty(c.authority));
        }

        static bool PostLocal(Kind kind, string text, int station, string throttleKey, float throttleSeconds)
        {
            // Local station-scoped news belongs to the specific destination, not neighbouring orbits.
            if (StationScoped(kind) && (station < 0 || Database.Shared?.Stations.Find(s => s.index == station) == null))
                return false;
            text = Clean(kind, Safe(text));
            if (text.Length == 0) return false;
            var list = ReadLocalNews();
            long now = UnixNow;
            PruneLocal(list, now);
            if (!string.IsNullOrEmpty(throttleKey) && throttleSeconds > 0f)
            {
                if (list.cooldowns.Exists(c => c.authority == throttleKey && c.until > now)) return false;
                if (list.cooldowns.Count >= 2048) list.cooldowns.RemoveAt(0);
                list.cooldowns.Add(new SavedCooldown { authority = throttleKey, until = now + (long)throttleSeconds });
            }
            var item = new LocalNewsItem { kind = (int)kind, station = station >= 0 ? DestinationKey(station) : null,
                text = text, time = now };
            list.items.Insert(0, item);
            PruneLocal(list, now);
            Session.ScriptData[LocalNewsKey] = JsonUtility.ToJson(list);
            Session.ScriptData.Remove(LegacyLocalNewsKey);
            Debug.Log($"Local: news [{kind}] {text}");
            if (LocalVisible(item, DestinationKey(Session.StationIndex))) Changed?.Invoke();
            return true;
        }

        // Backwards-compatible entry point for mods. The same publisher now owns local freight news.
        internal static void PostLocalFreight(string report, int destination)
        {
            if (LocalPublishingAllowed) Post(Kind.Freight, report, destination);
        }

        static string LocalTicker(int station)
        {
            var sb = new StringBuilder();
            long now = UnixNow;
            string key = DestinationKey(station);
            int count = 0;
            foreach (var item in ReadLocalNews().items)
            {
                if (item == null || item.kind < 0 || item.kind > (int)Kind.Freight ||
                    !LocalVisible(item, key) || now - item.time > TickerHours * 3600f) continue;
                if (count++ >= TickerItems) break;
                var kind = (Kind)item.kind;
                var (label, color) = Kickers[item.kind];
                float minutes = Math.Max(0f, (now - item.time) / 60f);
                bool fresh = minutes < FreshMinutes && kind != Kind.Pilot && kind != Kind.Galnet && kind != Kind.Freight;
                sb.Append(fresh ? $"<b><color=#ff5a4f>+++ BREAKING</color> <color={color}>{label} +++</color></b> "
                                : $"<b><color={color}>+++ {label} +++</color></b> ");
                sb.Append(item.station == key ? $"<b>{Safe(item.text)}</b>" : Safe(item.text));
                sb.Append(" <color=#7f8a99><size=80%>· ").Append(Age(minutes))
                    .Append("</size></color>").Append(NewsTicker.Separator);
            }
            return sb.ToString();
        }

        public static string Ticker(int dockedStation)
        {
            if (LocalPublishingAllowed) return LocalTicker(dockedStation);
            if (!NetGame.Active || items.Count == 0) return "";
            long now = UnixNow;
            var sb = new StringBuilder();
            int shown = 0;
            foreach (var it in items)
            {
                float ageMinutes = (now - it.time) / 60f;
                if (ageMinutes > TickerHours * 60f || shown >= TickerItems) break;
                if (it.kind == Kind.StationAnnouncement && it.station != dockedStation) continue;
                if (!VisibleAt(it, dockedStation)) continue;
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
            if (LocalPublishingAllowed)
            {
                string key = DestinationKey(orbit);
                foreach (var item in ReadLocalNews().items)
                    if (item != null && item.kind == (int)Kind.StationAnnouncement && item.station == key &&
                        now - item.time <= MaxAgeSeconds)
                        lines.Add(Age(Math.Max(0f, (now - item.time) / 60f)) + " · " + Safe(item.text));
                return lines;
            }
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
