// NetProfiles.cs
// Remake-only: player profiles kept by a dedicated server (DedicatedServer; -noprofiles switches them off), so a player's
// credits, ship, equipment, cargo, Kaamo Club and squad survive between sessions on that server.
//   Identity: a profile (account) is proven by a random secret token the server gives each device on its first visit
//     (only its SHA-256 hash is stored); the device's own id is a label (NetProfileClient sends a hash of it). A device
//     without a known token gets a new profile. No limit on their number: a profile nobody signed in to for PruneDays (30)
//     is pruned (Prune: at the start and every hour; never one that is online or staff), moved to Pruned/ (the newest
//     KeepPruned kept) so a mistake can be undone by hand.
//   Linking: "/link" in the chat gives a 6-letter code for 5 minutes; another device types "/link CODE" and from then on
//     signs in to the same profile with its own token. A device whose own profile has progress needs "/link CODE force".
//   Several devices of one profile online: the first is the controller, the others observers (docked only: no launch,
//     hangar, lounge or map, NetProfileClient.Refusal; their uploads are ignored). "/control" on an observer takes over
//     once the controller is docked: the controller uploads first (RequestUpload), the observer then gets that profile.
//     A controller that leaves hands control to the profile's next device online. One connection per device: signing
//     in again from the same device drops the older connection.
//   Storage: <persistentDataPath>/ServerProfiles (or -profiledir): accounts.json (the accounts, the server's id) and one
//     <account>.json per profile, a SaveData without the shop memory (SaveGame.ProfileJson). Written atomically, the
//     previous file kept as .bak.
//   Trust: the client's game runs the economy, so an upload is what the client says. Every upload is parsed and checked
//     like an imported save (SaveGame.TryParse: indices, no negative amounts). With the Debug menu allowed (-allowdebug)
//     that is all (client-authoritative); without it an upload is also turned away when the profile's worth (credits +
//     ships + items at their lowest price, Worth) grew faster than EarnBurst + EarnPerMinute per minute online
//     (-maxearn), or when it flies a hull nobody can own (13 / 14 / 15). Not a real server authority: that needs the
//     trades and rewards decided by the server (the plan's phase 2). Kaamo upgrades past Session.MaxModLevel per mod and
//     hull (a day's builds stacked them without a cap) are cut back on every upload and at every sign-in (ClampMods).
//     A refused upload is logged once per RejectLogSeconds per profile (with the count since), and the online staff
//     (ops and up) hear about it as often.
//   Reconnects: a profile (or a device without one) signing in more than ReconnectMax times in ReconnectWindow is
//     turned away for ReconnectBlockSeconds (a relog loop floods the log and the others' chat, and re-runs whatever the
//     sign-in gives).
//   Squads: an account remembers its squad (a key); joining / leaving one updates it, a disconnect doesn't, and a
//     controller signing in joins a squadmate who is online (NetState.RestoreSquad).
// Profiles go over the network gzipped in 4000-byte chunks (Pack / Unpack): Unity Transport's default payload limit is
// 6144 bytes per message.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static partial class NetProfiles
    {
        /// <summary>A profile unused this long is pruned (Prune).</summary>
        public const int PruneDays = 30;
        const int KeepPruned = 50;                  // the newest pruned profile files kept in Pruned/
        const float PruneEverySeconds = 3600f;
        public const int DefaultEarnPerMinute = 1_000_000;
        const long EarnBurst = 2_000_000;
        public const int ChunkBytes = 4000;
        const int MaxChunks = 64;                    // 256 KB gzipped: far more than a profile needs
        const int MaxJsonBytes = 4 * 1024 * 1024;
        const float LinkCodeSeconds = 300f, HandoverSeconds = 5f;
        const string LinkAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // no 0 / O, 1 / I
        const int MaxDeviceLength = 64;
        const int ReconnectMax = 8;
        const float ReconnectWindow = 600f, ReconnectBlockSeconds = 600f, RejectLogSeconds = 600f;

        [Serializable] class Device { public string device, tokenHash, added; }

        [Serializable]
        class Account
        {
            public string id, name, squad, created, lastSeen;
            public List<Device> devices = new List<Device>();
            public long worth;     // at the last accepted upload (Worth)
            public float playSeconds;
            public int arenaKills, arenaDeaths, arenaWins;   // NetArena's matches (the leaderboard, /top)
            public int role;   // NetModeration: 0 player, 1 op, 2 admin
        }

        [Serializable] class AccountIndex { public string serverId; public List<Account> accounts = new List<Account>(); }

        /// <summary>One connected player: their profile (null = a guest) and whether this device controls it.</summary>
        class Login
        {
            public ulong client;
            public Account account;
            public string device;
            public bool controller;
            public float acceptedAt;   // real time of the login or the last accepted upload (the earning allowance)
        }

        class Incoming { public int seq, count, got; public byte[][] parts; }

        static bool configured;
        static string folder;
        static AccountIndex index;
        static readonly Dictionary<ulong, Login> logins = new Dictionary<ulong, Login>();
        static readonly Dictionary<ulong, Incoming> incoming = new Dictionary<ulong, Incoming>();
        static readonly Dictionary<string, List<float>> recentLogins = new Dictionary<string, List<float>>();   // profile / device -> sign-in times
        static readonly Dictionary<string, float> reconnectBlocked = new Dictionary<string, float>();           // profile / device -> until
        static readonly Dictionary<string, (float at, int count)> rejectLog = new Dictionary<string, (float, int)>();   // profile -> last logged refusal
        static readonly Dictionary<string, (string account, float until)> links = new Dictionary<string, (string, float)>();
        // A /control in progress, per account: the old controller uploads, then the new one gets the profile.
        static readonly Dictionary<string, (ulong from, ulong to, float until)> handovers = new Dictionary<string, (ulong, ulong, float)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            configured = false;
            folder = null;
            index = null;
            nextPrune = 0f;
            EarnPerMinute = DefaultEarnPerMinute;
            logins.Clear();
            incoming.Clear();
            recentLogins.Clear(); reconnectBlocked.Clear(); rejectLog.Clear();
            links.Clear();
            handovers.Clear();
        }

        static float nextPrune;

        /// <summary>Without the Debug menu: how much worth a profile may gain per minute online (-maxearn), on top of EarnBurst.</summary>
        public static int EarnPerMinute { get; internal set; } = DefaultEarnPerMinute;

        /// <summary>This process keeps profiles: a dedicated server started without -noprofiles, or a persistent world hosted
        /// from the menu (NetGame.PersistentHost: the host signs in to its own profile like any player).</summary>
        public static bool Enabled => configured && (NetGame.Dedicated || NetGame.PersistentHost);

        /// <summary>The hosting player's own game (a persistent hosted world): always a profile, always the master admin,
        /// never banned, its uploads never doubted (it runs the world).</summary>
        static bool IsHostClient(ulong client) => NetGame.PersistentHost && client == Unity.Netcode.NetworkManager.ServerClientId;

        /// <summary>This server's id (accounts.json), the key under which a client keeps its token; "" = none.</summary>
        public static string ServerId => index != null ? index.serverId : "";

        /// <summary>DedicatedServer.Boot: the command line's choices.</summary>
        public static void Configure(bool on, int earnPerMinute, string profileDir)
        {
            configured = on;
            EarnPerMinute = Mathf.Max(0, earnPerMinute);
            folder = string.IsNullOrEmpty(profileDir) ? Path.Combine(GoF2Remake.GamePaths.Data, "ServerProfiles") : profileDir;
        }

        /// <summary>NetGame.StartServer, before NetState spawns: the accounts loaded (and the server's id made once).</summary>
        public static void Start()
        {
            logins.Clear(); incoming.Clear(); links.Clear(); handovers.Clear();
            recentLogins.Clear(); reconnectBlocked.Clear(); rejectLog.Clear();
            if (!configured) return;
            try
            {
                Directory.CreateDirectory(folder);
                string path = IndexPath;
                index = File.Exists(path) ? JsonUtility.FromJson<AccountIndex>(File.ReadAllText(path)) : null;
            }
            catch (Exception e) { Debug.LogError($"NetProfiles: accounts.json unreadable ({e.Message}); starting a new list (the old file is kept as .bak)."); BackUp(IndexPath); index = null; }
            if (index == null) index = new AccountIndex();
            if (index.accounts == null) index.accounts = new List<Account>();
            if (string.IsNullOrEmpty(index.serverId)) index.serverId = RandomHex(16);
            SaveIndex();
            NetFactions.Load();
            NetModeration.Load();
            Debug.Log($"Server: player profiles on: {index.accounts.Count} in {folder}.");
            Prune();
        }

        static string IndexPath => Path.Combine(folder, "accounts.json");

        /// <summary>Where the profiles are kept (NetFactions keeps factions.json there too).</summary>
        internal static string Folder => folder;

        /// <summary>A connected player's profile id, null = a guest or not signed in.</summary>
        internal static string AccountOf(ulong client) => logins.TryGetValue(client, out var l) && l.account != null ? l.account.id : null;

        /// <summary>A profile's last pilot name, null = unknown.</summary>
        internal static string AccountName(string id)
        {
            var a = index?.accounts.Find(x => x.id == id);
            return a == null || string.IsNullOrEmpty(a.name) ? null : a.name;
        }

        /// <summary>The connected devices of a profile.</summary>
        internal static IEnumerable<ulong> ClientsOf(string id)
        {
            var result = new List<ulong>();
            foreach (var l in logins.Values) if (l.account != null && l.account.id == id) result.Add(l.client);
            return result;
        }

        /// <summary>The player's device controls its profile (a signed-in controller, not a guest or an observer).</summary>
        internal static bool Controls(ulong client) => logins.TryGetValue(client, out var l) && l.account != null && l.controller;

        /// <summary>Credits the server moved in or out of a profile (the faction bank): its recorded worth moves too, so the
        /// next upload is measured against what the game should have now.</summary>
        internal static void AdjustWorth(string id, long delta)
        {
            var a = index?.accounts.Find(x => x.id == id);
            if (a == null) return;
            a.worth += delta;
            SaveIndex();
        }

        // Credits the server moves itself (the faction bank, tolls, trades) are checked against the profile's saved game,
        // not the player's own: a game is client-authoritative, and one that only claimed to pay (an edited credit count)
        // fed a faction bank billions while its own uploads were turned away. The saved game is the last accepted upload,
        // so the server moves the credits in it too: closing the game before the next upload no longer gets them back.

        /// <summary>The credits a profile may spend through the server: its saved game's. The hosting player's own game
        /// (never doubted) any amount.</summary>
        internal static long SpendableCredits(ulong client, string id)
        {
            if (IsHostClient(client)) return long.MaxValue;
            return ReadSave(id)?.credits ?? 0;
        }

        /// <summary>Credits the server moved out of (negative) or into a profile: its saved game and its recorded worth.
        /// False, nothing changed, when the saved game hasn't that many. The hosting player's own game only moves the worth
        /// (its profile is written when it leaves).</summary>
        internal static bool MoveCredits(ulong client, string id, long delta)
        {
            if (id == null) return false;
            if (IsHostClient(client)) { AdjustWorth(id, delta); return true; }
            var save = ReadSave(id);
            if (save == null)
            {
                if (delta < 0) return false;
                AdjustWorth(id, delta);   // no saved game yet: its first upload carries the credits
                return true;
            }
            long now = Session.SaturatingAdd(save.credits, delta);
            if (now < 0) return false;
            save.credits = now;
            if (!Write(ProfilePath(id), JsonUtility.ToJson(save, false))) return false;
            AdjustWorth(id, delta);
            return true;
        }

        static SaveData ReadSave(string id)
        {
            if (id == null || folder == null) return null;
            try
            {
                string path = ProfilePath(id);
                return File.Exists(path) ? JsonUtility.FromJson<SaveData>(File.ReadAllText(path)) : null;
            }
            catch (Exception e) { Debug.LogError($"NetProfiles: profile {id} unreadable: {e.Message}"); return null; }
        }

        /// <summary>A profile's worth at its last accepted upload (credits + ships + items), 0 = unknown.</summary>
        internal static long WorthOf(string id) => id == null ? 0 : index?.accounts.Find(x => x.id == id)?.worth ?? 0;

        /// <summary>A profile's role (NetModeration), 0 for none / a guest.</summary>
        internal static int RoleOf(string id) => id == null ? 0 : index?.accounts.Find(x => x.id == id)?.role ?? 0;

        internal static void SetRole(string id, int role)
        {
            var a = index?.accounts.Find(x => x.id == id);
            if (a == null || a.role == role) return;
            a.role = role;
            SaveIndex();
        }

        /// <summary>A profile by its id or its last pilot name (null = none).</summary>
        internal static string FindAccount(string idOrName)
        {
            if (index == null || string.IsNullOrEmpty(idOrName)) return null;
            var a = index.accounts.Find(x => x.id == idOrName) ?? index.accounts.Find(x => string.Equals(x.name, idOrName, StringComparison.OrdinalIgnoreCase));
            return a?.id;
        }

        /// <summary>The device labels a profile signed in from (a ban holds them all).</summary>
        internal static List<string> DevicesOf(string id)
        {
            var a = index?.accounts.Find(x => x.id == id);
            var list = new List<string>();
            if (a != null) foreach (var d in a.devices) if (!string.IsNullOrEmpty(d.device) && !list.Contains(d.device)) list.Add(d.device);
            return list;
        }

        /// <summary>The admins' window: every profile (the most recent first, at most 200).</summary>
        internal static void FillProfiles(NetPanel.State s)
        {
            if (index == null) return;
            var all = new List<Account>(index.accounts);
            all.Sort((a, b) => string.CompareOrdinal(b.lastSeen ?? "", a.lastSeen ?? ""));
            for (int i = 0; i < all.Count && i < 200; i++)
            {
                var a = all[i];
                s.profileRows.Add(new NetPanel.ProfileRow { id = a.id, name = a.name ?? "", role = a.role, devices = a.devices.Count, online = IsOnline(a.id),
                                                         lastSeen = (a.lastSeen ?? "").Length >= 10 ? a.lastSeen.Substring(0, 10) : "" });
            }
        }

        /// <summary>A connected game's device label (a guest's too).</summary>
        internal static string DeviceOf(ulong client) => logins.TryGetValue(client, out var l) ? l.device : null;

        /// <summary>The admins and ops: name, role, online.</summary>
        internal static List<(string name, int role, bool online)> Staff()
        {
            var list = new List<(string, int, bool)>();
            if (index != null)
                foreach (var a in index.accounts)
                    if (a.role > 0) list.Add((string.IsNullOrEmpty(a.name) ? a.id : a.name, a.role, IsOnline(a.id)));
            return list;
        }

        internal static bool IsOnline(string id)
        {
            foreach (var l in logins.Values) if (l.account != null && l.account.id == id) return true;
            return false;
        }
        static string ProfilePath(string id) => Path.Combine(folder, id + ".json");

        // ---- files and helpers ------------------------------------------------------------------------------

        static void SaveIndex()
        {
            if (index != null) Write(IndexPath, JsonUtility.ToJson(index, true));
        }

        /// <summary>Through a temporary file, the previous version kept as .bak (a crash mid-write loses nothing).</summary>
        internal static bool Write(string path, string text)
        {
            try
            {
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, text);
                if (File.Exists(path)) File.Copy(path, path + ".bak", true);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                return true;
            }
            catch (Exception e) { Debug.LogError($"NetProfiles: writing {path} failed: {e.Message}"); return false; }
        }

        static void BackUp(string path)
        {
            try { if (File.Exists(path)) { File.Copy(path, path + ".bak", true); File.Delete(path); } }
            catch (Exception e) { Debug.LogError($"NetProfiles: moving {path} aside failed: {e.Message}"); }
        }

        static string AddDevice(Account account, string device)
        {
            string token = RandomToken();
            account.devices.Add(new Device { device = device, tokenHash = Hash(token), added = Now() });
            return token;
        }

        static string NewAccountId()
        {
            string id;
            do id = RandomHex(8); while (index.accounts.Exists(a => a.id == id));
            return id;
        }

        static string CleanDevice(string device)
        {
            var sb = new StringBuilder();
            foreach (char c in device ?? "") if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '#') sb.Append(c);
            string d = sb.ToString();
            return d.Length > MaxDeviceLength ? d.Substring(0, MaxDeviceLength) : d;
        }

        static string Now() => DateTime.UtcNow.ToString("o");

        static string RandomToken()
        {
            var bytes = new byte[32];
            using (var r = RandomNumberGenerator.Create()) r.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        static string RandomHex(int bytes)
        {
            var b = new byte[bytes];
            using (var r = RandomNumberGenerator.Create()) r.GetBytes(b);
            return Hex(b);
        }

        /// <summary>SHA-256 as lower-case hex (tokens are stored only this way; NetProfileClient hashes the device id too).</summary>
        public static string Hash(string text)
        {
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? "")));
        }

        static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (byte x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>A profile's JSON gzipped and cut into network-sized chunks (none for an empty text).</summary>
        public static List<byte[]> Pack(string json)
        {
            var parts = new List<byte[]>();
            if (string.IsNullOrEmpty(json)) return parts;
            byte[] packed;
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, true))
                {
                    byte[] raw = Encoding.UTF8.GetBytes(json);
                    gz.Write(raw, 0, raw.Length);
                }
                packed = ms.ToArray();
            }
            for (int i = 0; i < packed.Length; i += ChunkBytes)
            {
                var chunk = new byte[Math.Min(ChunkBytes, packed.Length - i)];
                Buffer.BlockCopy(packed, i, chunk, 0, chunk.Length);
                parts.Add(chunk);
            }
            if (parts.Count > MaxChunks) Debug.LogError($"NetProfiles: a profile of {packed.Length} bytes gzipped is too big to send.");
            return parts;
        }

        /// <summary>The chunks back into the JSON (null = broken or too big).</summary>
        public static string Unpack(byte[][] parts)
        {
            try
            {
                using (var packed = new MemoryStream())
                {
                    foreach (var p in parts) { if (p == null) return null; packed.Write(p, 0, p.Length); }
                    packed.Position = 0;
                    using (var gz = new GZipStream(packed, CompressionMode.Decompress))
                    using (var output = new MemoryStream())
                    {
                        var buffer = new byte[16384];
                        int n;
                        while ((n = gz.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            output.Write(buffer, 0, n);
                            if (output.Length > MaxJsonBytes) return null;
                        }
                        return Encoding.UTF8.GetString(output.ToArray());
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("NetProfiles: a profile didn't unpack: " + e.Message); return null; }
        }
    }
}
