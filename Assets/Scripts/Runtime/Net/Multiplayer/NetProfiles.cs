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
            // Gate interactions permanently reveal their destination for this profile, even if a client uploads
            // an older save snapshot before the discovery RPC's local update arrives.
            public List<int> gateDiscoveredSystems = new List<int>();
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
            public int acceptedUploadSequence, lastAcceptedUploadSequence;
            public long economyRevision;
            public bool economyRevisionDirty;
        }

        class Incoming { public int seq, count, got; public long economyRevision; public byte[][] parts; }

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
        internal static bool IsHostClient(ulong client) => NetGame.PersistentHost && client == Unity.Netcode.NetworkManager.ServerClientId;

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
        /// <summary>Send the server's just-updated profile back to its controller while it is docked.</summary>
        internal static void RefreshProfile(ulong client)
        {
            if (logins.TryGetValue(client, out var login) && login.account != null && login.controller)
                SendProfile(login, null);
        }

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
            if (id == null || NetPlayerStations.ProfileWritesBlocked) return false;
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
            InvalidateEconomyUploads(id);
            return true;
        }

        /// <summary>Server-moved cargo for a docked station project or marketplace. The active host's live hold is the
        /// authority for its own save; another player's last uploaded profile is changed before its game is updated.</summary>
        internal static bool MoveCargo(ulong client, string id, int item, int delta)
        {
            if (id == null || item < 0 || delta == 0 || NetGame.Db.Item(item) == null ||
                NetPlayerStations.ProfileWritesBlocked) return false;
            if (IsHostClient(client))
            {
                int have = Shop.CargoOf(item);
                if (delta < 0 && have < -delta) return false;
                if (delta < 0) Shop.RemoveFromCargo(item, -delta);
                else Shop.AddToCargo(item, delta);
                GoF2Remake.UI.UiHooks.StationCargoChanged?.Invoke();
                return true;
            }
            var save = ReadSave(id);
            if (save == null) return false;
            save.cargo ??= new List<ItemStack>();
            var stack = save.cargo.Find(s => s != null && s.item == item);
            int haveSaved = stack?.amount ?? 0;
            long next = (long)haveSaved + delta;
            if (next < 0 || next > int.MaxValue) return false;
            if (stack == null && next > 0) save.cargo.Add(stack = new ItemStack(item, (int)next));
            else if (stack != null)
            {
                stack.amount = (int)next;
                if (stack.amount == 0) save.cargo.Remove(stack);
            }
            if (!Write(ProfilePath(id), JsonUtility.ToJson(save, false))) return false;
            int unitValue = Math.Max(0, NetGame.Db.Item(item)?.minPrice ?? 0);
            AdjustWorth(id, (long)unitValue * delta);
            InvalidateEconomyUploads(id);
            return true;
        }

        internal static GameHooks.ModJumpResult? CommitModJump(ulong client, int origin, int destination,
            int driveItem, int sequence)
        {
            if (!Enabled || !NetGuard.InOrbit(client, origin) || !NetGuard.Station(origin) ||
                !NetGuard.Station(destination) || !ConsumeActionUpload(client, sequence)) return null;

            var login = logins[client];
            var save = ReadSave(login.account.id);
            var pilot = NetSquad.Find(client);
            if (pilot == null || pilot.Hull <= 0f || save == null || save.station != origin ||
                save.ship != pilot.ShipIndex || save.equipment == null || save.cargo == null)
                return null;

            var db = NetGame.Db;
            var drive = Modding.ModJumpFx.ActiveDrive(save.ship, save.equipment);
            if (drive?.hasJumpRules != true || drive.item != driveItem ||
                save.cargo.Exists(stack => stack != null && stack.amount > 0 && (stack.item == 209 || stack.item == 204)))
                return null;

            bool Visible(int system)
            {
                if (system < 0) return false;
                return (save.systemVisible != null && system < save.systemVisible.Length && save.systemVisible[system]) ||
                    login.account.gateDiscoveredSystems?.Contains(system) == true ||
                    db.Systems.Find(entry => entry.index == system)?.initiallyVisible == true || system == GalaxyMap.LomaSystem;
            }

            int from = db.Stations.Find(station => station.index == origin)?.system ?? -1;
            int to = db.Stations.Find(station => station.index == destination)?.system ?? -1;
            if (from < 0 || from == to || !Visible(to) ||
                !NetPlayerStations.CanVisitStation(destination, login.account.id)) return null;

            // Network profiles play on Normal. Fuel and the outcome are derived on the server.
            int cells = GalaxyMap.EnergyCells(db, from, to, false, drive, drive.freeJumps, out _);
            var fuel = save.cargo.Find(stack => stack != null && stack.item == GalaxyMap.EnergyCellItem);
            if (cells > (fuel?.amount ?? 0)) return null;

            var path = GalaxyMap.SystemPath(db, from, to);
            float risk = Modding.ModJumpFx.FailureProbability(drive, path != null ? path.Count - 1 : 0,
                path != null, false);
            int arrival = destination, lostItem = -1, hullLimit = -1;
            bool misjump = false;
            if (UnityEngine.Random.value < risk)
            {
                var candidates = World.SystemJump.MisjumpDestinations(db, origin, destination, Session.WorldIndex,
                    Visible, requireConnected: true);
                candidates.RemoveAll(station => !NetPlayerStations.CanVisitStation(station, login.account.id));
                if (candidates.Count > 0)
                {
                    misjump = true;
                    arrival = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                    if (UnityEngine.Random.value < drive.failEquipmentLossChance)
                    {
                        var equipment = save.equipment.FindAll(item => item != null && item.amount > 0 && !item.builtIn &&
                            db.Item(item.item)?.TypeId == 3 && (drive.failEquipmentIncludesDrive || item.item != drive.item));
                        if (equipment.Count > 0)
                        {
                            var lost = equipment[UnityEngine.Random.Range(0, equipment.Count)];
                            lostItem = lost.item;
                            if (--lost.amount == 0) save.equipment.Remove(lost);
                        }
                    }
                    int maximum = Mathf.Max(1, Modding.ModShipKits.Armor(db.Ship(save.ship), 100) +
                        40 * Session.ModLevel(save.shipMods, 0));
                    int hull = Mathf.Clamp(Mathf.RoundToInt(pilot.Hull * maximum), 1, maximum);
                    int remaining = Modding.ModJumpFx.HullAfterFailure(hull, maximum, drive.failHullDamage, drive.failMinimumHull);
                    if (remaining < hull) hullLimit = remaining;
                }
            }

            if (cells > 0)
            {
                fuel.amount -= cells;
                if (fuel.amount == 0) save.cargo.Remove(fuel);
            }
            save.previousStation = origin;
            save.station = arrival;
            if (!Write(ProfilePath(login.account.id), JsonUtility.ToJson(save, false))) return null;
            login.account.worth = Worth(save);
            SaveIndex();
            InvalidateEconomyUploads(login.account.id);
            return new GameHooks.ModJumpResult(arrival, cells, lostItem, hullLimit, misjump);
        }

        // Invalidate uploads immediately; publish the barrier on the next server tick, after
        // synchronous transaction callers have sent their existing reliable economy updates.
        internal static void InvalidateEconomyUploads(string account)
        {
            if (string.IsNullOrEmpty(account)) return;
            foreach (var login in logins.Values)
            {
                if (login.account == null || login.account.id != account || IsHostClient(login.client)) continue;
                login.economyRevision++;
                login.economyRevisionDirty = true;
                login.acceptedUploadSequence = 0;
                incoming.Remove(login.client);
            }
        }

        internal static bool ConsumeActionUpload(ulong client, int sequence)
        {
            if (NetPlayerStations.ProfileWritesBlocked || sequence == 0 || !logins.TryGetValue(client, out var login) ||
                !login.controller || login.account == null || login.acceptedUploadSequence != sequence) return false;
            login.acceptedUploadSequence = 0;
            return true;
        }

        internal static bool HasScanEquipment(NetPlayer pilot)
        {
            var db = NetGame.Db;
            if (pilot == null || db?.Ship(pilot.ShipIndex) == null) return false;
            if (pilot.OwnerClientId == Unity.Netcode.NetworkManager.ServerClientId && NetPlayer.Local == pilot)
                return Session.ShipIndex == pilot.ShipIndex && Session.Equipment.Exists(m =>
                    m != null && m.amount > 0 && Shop.KindOf(db.Item(m.item)) == 17 &&
                    Modding.ModContent.ItemAvailableFor(m.item, "equipment"));
            if (!Enabled) return true; // ordinary sessions have no server-side equipment snapshot
            if (!Controls(pilot.OwnerClientId)) return false;
            var account = AccountOf(pilot.OwnerClientId);
            if (string.IsNullOrEmpty(account)) return false;
            var save = ReadSave(account);
            return save != null && save.ship == pilot.ShipIndex && save.equipment != null &&
                save.equipment.Exists(m => m != null && m.amount > 0 && Shop.KindOf(db.Item(m.item)) == 17 &&
                    Modding.ModContent.ItemAvailableFor(m.item, "equipment"));
        }

        internal static float WreckSalvageTractorReach(NetPlayer pilot)
        {
            var db = NetGame.Db;
            if (db?.Ship(pilot.ShipIndex) == null) return 0;
            if (IsHostClient(pilot.OwnerClientId))
                return pilot.ShipIndex == Session.ShipIndex
                    ? Flight.CombatRadar.TractorReachUnits(Shop.FirstMounted(db, 13)) * World.OrbitLayout.MetersPerUnit
                    : 0;

            var save = ReadSave(AccountOf(pilot.OwnerClientId));
            if (save == null || save.ship != pilot.ShipIndex || save.equipment == null) return 0;
            foreach (var mount in save.equipment)
            {
                if (mount == null) continue;
                var item = db.Item(mount.item);
                if (item != null && Shop.KindOf(item) == 13)
                    return Flight.CombatRadar.TractorReachUnits(item) * World.OrbitLayout.MetersPerUnit;
            }
            return 0;
        }

        internal static int WreckSalvageCargoRoom(NetPlayer pilot, string account)
        {
            var db = NetGame.Db;
            var ship = db?.Ship(pilot.ShipIndex);
            if (ship == null) return 0;
            if (IsHostClient(pilot.OwnerClientId))
                return Session.ShipIndex == ship.index ? Math.Max(0, Shop.FreeCargo(db)) : 0;

            // Only the profile snapshot accepted immediately before the pickup is used here.
            var save = ReadSave(account);
            if (save == null || save.ship != ship.index) return 0;
            int baseCargo = ship.cargo;
            var kit = Modding.ModShipKits.CustomizationOf(ship.index);
            if (kit != null)
            {
                string prefix = Modding.ModShipKits.BuildKey(ship.index) + "|";
                string buildText = null;
                if (save.shipBuilds != null)
                    foreach (string entry in save.shipBuilds)
                        if (entry != null && entry.StartsWith(prefix, StringComparison.Ordinal))
                            buildText = entry.Substring(prefix.Length);
                var build = Modding.ModShipKits.RemoteBuild(ship.index, buildText);
                if (build == null) return 0;
                float bonus = Modding.ModShipKits.EffectOf(kit, build).cargo;
                baseCargo = Mathf.Max(0, Mathf.RoundToInt(ship.cargo * (1f + bonus / 100f)));
            }

            int capacity = Shop.CargoCapacity(db, baseCargo, Session.ModLevel(save.shipMods, 1), save.equipment);
            long load = 0;
            if (save.cargo != null)
                foreach (var stack in save.cargo)
                    if (stack != null) load += Math.Max(0, stack.amount);
            return (int)Math.Max(0L, capacity - load);
        }

        /// <summary>Absolute cargo count used by the station transaction journal. Reapplying a target is safe after a crash.</summary>
        internal static int CargoAmount(string id, int item)
        {
            if (id == null || item < 0) return -1;
            foreach (var login in logins.Values)
                if (login.account != null && login.account.id == id && IsHostClient(login.client))
                    return Shop.CargoOf(item);
            var save = ReadSave(id);
            if (save == null) return -1;
            return save.cargo?.Find(x => x != null && x.item == item)?.amount ?? 0;
        }

        /// <summary>Set, rather than increment, a saved cargo count. This makes journal replay idempotent.</summary>
        internal static bool SetCargoAmount(string id, int item, int amount)
        {
            if (id == null || item < 0 || amount < 0 || NetGame.Db?.Item(item) == null) return false;
            var save = ReadSave(id);
            if (save == null) return false;
            bool host = false;
            int liveBefore = 0;
            foreach (var login in logins.Values)
                if (login.account != null && login.account.id == id && IsHostClient(login.client))
                {
                    host = true;
                    liveBefore = Shop.CargoOf(item);
                    break;
                }
            save.cargo ??= new List<ItemStack>();
            var stack = save.cargo.Find(x => x != null && x.item == item);
            int previous = host ? liveBefore : stack?.amount ?? 0;
            if ((stack?.amount ?? 0) != amount)
            {
                if (stack == null && amount > 0) save.cargo.Add(new ItemStack(item, amount));
                else if (stack != null)
                {
                    stack.amount = amount;
                    if (amount == 0) save.cargo.Remove(stack);
                }
                if (!Write(ProfilePath(id), JsonUtility.ToJson(save, false)))
                    return false;
            }

            if (host)
            {
                if (liveBefore < amount) Shop.AddToCargo(item, amount - liveBefore);
                else if (liveBefore > amount) Shop.RemoveFromCargo(item, liveBefore - amount);
                GoF2Remake.UI.UiHooks.StationCargoChanged?.Invoke();
            }
            int unitValue = Math.Max(0, NetGame.Db.Item(item)?.minPrice ?? 0);
            AdjustWorth(id, (long)unitValue * (amount - previous));
            return true;
        }

        /// <summary>The durable credits available to a station transaction (the live host's balance when applicable).</summary>
        internal static long CreditAmount(string id)
        {
            if (id == null) return -1;
            foreach (var login in logins.Values)
                if (login.account != null && login.account.id == id && IsHostClient(login.client))
                    return Session.Credits;
            return ReadSave(id)?.credits ?? -1;
        }

        /// <summary>Set an absolute saved credit balance so a station journal can safely replay a purchase.</summary>
        internal static bool SetCreditsAmount(string id, long amount)
        {
            if (id == null || amount < 0) return false;
            var save = ReadSave(id);
            if (save == null) return false;
            bool host = false;
            long liveBefore = 0;
            foreach (var login in logins.Values)
                if (login.account != null && login.account.id == id && IsHostClient(login.client))
                {
                    host = true;
                    liveBefore = Session.Credits;
                    break;
                }
            long previous = host ? liveBefore : save.credits;
            save.credits = amount;
            if (!Write(ProfilePath(id), JsonUtility.ToJson(save, false))) return false;
            if (host) Session.Credits = amount;
            AdjustWorth(id, amount - previous);
            return true;
        }

        internal static bool HasDiscoveredSystem(string id, int system)
        {
            if (string.IsNullOrEmpty(id) || system < 0 || NetGame.Db?.Systems?.Find(x => x != null && x.index == system) == null)
                return false;
            foreach (var login in logins.Values)
                if (login.account != null && string.Equals(login.account.id, id, StringComparison.Ordinal) && IsHostClient(login.client))
                {
                    GalaxyMap.Visibility(NetGame.Db);
                    if (Session.SystemVisible != null && system < Session.SystemVisible.Length && Session.SystemVisible[system]) return true;
                    return index?.accounts?.Find(x => x != null && string.Equals(x.id, id, StringComparison.Ordinal))
                        ?.gateDiscoveredSystems?.Contains(system) == true;
                }
            var save = ReadSave(id);
            if (save?.systemVisible != null && system < save.systemVisible.Length && save.systemVisible[system]) return true;
            if (index?.accounts?.Find(x => x != null && string.Equals(x.id, id, StringComparison.Ordinal))
                ?.gateDiscoveredSystems?.Contains(system) == true) return true;
            var definition = NetGame.Db.Systems.Find(x => x != null && x.index == system);
            return definition?.initiallyVisible == true || system == GalaxyMap.LomaSystem;
        }

        internal static bool CanReachConstructionSystem(NetPlayer pilot, int destination) => ConstructionReachableSystems(pilot).Contains(destination);

        internal static HashSet<int> ConstructionReachableSystems(NetPlayer pilot)
        {
            if (pilot == null || !Controls(pilot.OwnerClientId)) return new HashSet<int>();
            var db = NetGame.Db;
            int origin = db?.Stations?.Find(station => station != null && station.index == pilot.Station)?.system ?? -1;
            if (origin < 0) return new HashSet<int>();
            string account = AccountOf(pilot.OwnerClientId);
            var save = ReadSave(account);
            bool host = IsHostClient(pilot.OwnerClientId);
            var visible = host ? GalaxyMap.Visibility(db) : save?.systemVisible;
            var gateDiscoveries = index?.accounts?.Find(entry => entry != null && entry.id == account)?.gateDiscoveredSystems;
            bool Discovered(int system) => system >= 0 && ((visible != null && system < visible.Length && visible[system]) ||
                gateDiscoveries?.Contains(system) == true || db.Systems.Find(entry => entry != null && entry.index == system)?.initiallyVisible == true || system == GalaxyMap.LomaSystem);
            string faction = NetFactions.Of(account)?.id;
            var reachable = NetPlayerStations.ReachableConstructionSystems(origin, Discovered,
                gate => NetPlayerStations.ConstructionGateAllowed(gate, account, faction));
            reachable.RemoveWhere(system => !Discovered(system));
            var drive = host ? Modding.ModJumpFx.ActiveDrive() : save == null ? null : Modding.ModJumpFx.ActiveDrive(save.ship, save.equipment);
            if (drive?.hasJumpRules != true || (!host && save.ship != pilot.ShipIndex)) return reachable;
            if (host ? GalaxyMap.HasVolatileGoods : save.cargo?.Exists(stack => stack != null && stack.amount > 0 && (stack.item == 209 || stack.item == 204)) == true) return reachable;
            int fuel = host ? GalaxyMap.CellsInCargo() : save.cargo?.Find(stack => stack != null && stack.item == GalaxyMap.EnergyCellItem)?.amount ?? 0;
            foreach (var system in db.Systems)
                if (system != null && !reachable.Contains(system.index) && Discovered(system.index) &&
                    GalaxyMap.EnergyCells(db, origin, system.index, false, drive, drive.freeJumps, out _) <= fuel)
                    reachable.Add(system.index);
            return reachable;
        }

        internal static bool HasFactionDiscoveredSystem(string factionId, int system)
        {
            var faction = NetFactions.FindById(factionId);
            if (faction?.members == null) return false;
            foreach (string member in faction.members)
                if (HasDiscoveredSystem(member, system)) return true;
            return false;
        }

        internal static bool DiscoverSystem(string id, int system)
        {
            if (NetPlayerStations.ProfileWritesBlocked || string.IsNullOrEmpty(id) || NetGame.Db?.Systems == null ||
                NetGame.Db.Systems.Find(x => x != null && x.index == system) == null) return false;
            var account = index?.accounts?.Find(x => x != null && string.Equals(x.id, id, StringComparison.Ordinal));
            if (account == null) return false;
            account.gateDiscoveredSystems ??= new List<int>();
            if (!account.gateDiscoveredSystems.Contains(system))
            {
                account.gateDiscoveredSystems.Add(system);
                if (!Write(IndexPath, JsonUtility.ToJson(index, true)))
                {
                    account.gateDiscoveredSystems.Remove(system);
                    return false;
                }
            }
            var save = ReadSave(id);
            bool host = false;
            foreach (var login in logins.Values)
                if (login.account != null && string.Equals(login.account.id, id, StringComparison.Ordinal) && IsHostClient(login.client))
                { host = true; break; }
            if (save == null && host)
            {
                try { save = JsonUtility.FromJson<SaveData>(SaveGame.ProfileJson()); }
                catch (Exception e) { Debug.LogError($"NetProfiles: could not seed host discovery for {id}: {e.Message}"); }
            }
            if (save != null)
            {
                MergeGateDiscoveries(save, account);
                if (!Write(ProfilePath(id), JsonUtility.ToJson(save, false)))
                    Debug.LogWarning("NetProfiles: jumpgate discovery remains persisted in the account index; profile snapshot refresh failed.");
            }
            if (host) GalaxyMap.Reveal(NetGame.Db, system);
            return true;
        }

        static void MergeGateDiscoveries(SaveData save, Account account)
        {
            if (save == null || NetGame.Db?.Systems == null) return;
            int count = 0;
            foreach (var system in NetGame.Db.Systems) if (system != null) count = Math.Max(count, system.index + 1);
            if (count <= 0) return;
            bool[] visible = save.systemVisible;
            if (visible == null || visible.Length != count)
            {
                int oldLength = visible?.Length ?? 0;
                var resized = new bool[count];
                if (visible != null) Array.Copy(visible, resized, Math.Min(oldLength, count));
                foreach (var definition in NetGame.Db.Systems)
                    if (definition != null && definition.index >= oldLength && definition.index < count)
                        resized[definition.index] = definition.initiallyVisible;
                visible = resized;
            }
            if (GalaxyMap.LomaSystem >= 0 && GalaxyMap.LomaSystem < visible.Length) visible[GalaxyMap.LomaSystem] = true;
            if (account?.gateDiscoveredSystems != null)
                foreach (int discovered in account.gateDiscoveredSystems)
                    if (discovered >= 0 && discovered < visible.Length) visible[discovered] = true;
            save.systemVisible = visible;
        }

        static SaveData ReadSave(string id)
        {
            if (id == null || folder == null) return null;
            try
            {
                string path = ProfilePath(id);
                if (!File.Exists(path)) return null;
                var save = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
                var account = index?.accounts?.Find(x => x != null && string.Equals(x.id, id, StringComparison.Ordinal));
                if (save != null && account != null) MergeGateDiscoveries(save, account);
                return save;
            }
            catch (Exception e) { Debug.LogError($"NetProfiles: profile {id} unreadable: {e.Message}"); return null; }
        }
        internal static List<StoredShip> StoredShipsOf(string id, ulong client)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (IsHostClient(client)) { Session.KaamoShips ??= new List<StoredShip>(); return Session.KaamoShips; }
            return ReadSave(id)?.kaamoShips;
        }

        internal static bool RemoveStoredShip(string id, ulong client, int index, out StoredShip ship)
        {
            ship = null;
            if (string.IsNullOrEmpty(id)) return false;
            if (IsHostClient(client))
            {
                Session.KaamoShips ??= new List<StoredShip>();
                if (index < 0 || index >= Session.KaamoShips.Count || Session.KaamoShips[index] == null) return false;
                ship = Session.KaamoShips[index]; Session.KaamoShips.RemoveAt(index); return true;
            }
            var save = ReadSave(id);
            if (save?.kaamoShips == null || index < 0 || index >= save.kaamoShips.Count || save.kaamoShips[index] == null) return false;
            ship = save.kaamoShips[index]; save.kaamoShips.RemoveAt(index);
            if (!Write(ProfilePath(id), JsonUtility.ToJson(save, false))) { ship = null; return false; }
            return true;
        }

        internal static bool AddStoredShip(string id, ulong client, StoredShip ship)
        {
            if (string.IsNullOrEmpty(id) || ship == null || ship.ship < 0) return false;
            if (IsHostClient(client))
            {
                Session.KaamoShips ??= new List<StoredShip>();
                if (Session.KaamoShips.Count >= 100) return false;
                Session.KaamoShips.Add(ship); return true;
            }
            var save = ReadSave(id);
            if (save == null) return false;
            save.kaamoShips ??= new List<StoredShip>();
            if (save.kaamoShips.Count >= 100) return false;
            save.kaamoShips.Add(ship);
            return Write(ProfilePath(id), JsonUtility.ToJson(save, false));
        }

        /// <summary>Apply one journal account's absolute targets in a single durable profile write.</summary>
        internal static bool ApplyStationProfileTargets(string id, Dictionary<int, int> cargo, long? credits, List<StoredShip> ships)
        {
            if (string.IsNullOrEmpty(id) || (credits.HasValue && credits.Value < 0)) return false;
            if (ships != null && (ships.Count > 100 || ships.Exists(x => x == null || x.ship < 0))) return false;
            foreach (var pair in cargo)
                if (pair.Key < 0 || pair.Value < 0 || NetGame.Db?.Item(pair.Key) == null) return false;

            bool host = false;
            foreach (var login in logins.Values)
                if (login.account != null && login.account.id == id && IsHostClient(login.client))
                { host = true; break; }
            var save = ReadSave(id);
            if (save == null && host)
            {
                try { save = JsonUtility.FromJson<SaveData>(SaveGame.ProfileJson()); }
                catch (Exception e) { Debug.LogError($"NetProfiles: could not seed station profile: {e.Message}"); }
            }
            if (save == null) return false;
            save.cargo ??= new List<ItemStack>();
            foreach (var pair in cargo)
            {
                var stack = save.cargo.Find(x => x != null && x.item == pair.Key);
                if (stack == null && pair.Value > 0) save.cargo.Add(new ItemStack(pair.Key, pair.Value));
                else if (stack != null)
                {
                    stack.amount = pair.Value;
                    if (pair.Value == 0) save.cargo.Remove(stack);
                }
            }
            if (credits.HasValue) save.credits = credits.Value;
            if (ships != null) save.kaamoShips = new List<StoredShip>(ships);
            if (!Write(ProfilePath(id), JsonUtility.ToJson(save, false))) return false;

            if (host)
            {
                foreach (var pair in cargo)
                {
                    int before = Shop.CargoOf(pair.Key);
                    if (before < pair.Value) Shop.AddToCargo(pair.Key, pair.Value - before);
                    else if (before > pair.Value) Shop.RemoveFromCargo(pair.Key, before - pair.Value);
                }
                if (credits.HasValue)
                {
                    Session.Credits = credits.Value;
                    GoF2Remake.UI.UiHooks.RefreshStationCredits?.Invoke();
                }
                if (ships != null)
                {
                    Session.KaamoShips ??= new List<StoredShip>();
                    Session.KaamoShips.Clear();
                    Session.KaamoShips.AddRange(ships);
                }
                if (cargo.Count > 0) GoF2Remake.UI.UiHooks.StationCargoChanged?.Invoke();
            }
            // Recompute from the durable target, rather than applying a delta twice on replay.
            var account = index?.accounts.Find(x => x.id == id);
            if (account != null)
            {
                account.worth = Worth(save);
                SaveIndex();
            }
            return true;
        }

        /// <summary>Replace a profile's complete stored-hull list with an absolute target for journal replay.</summary>
        internal static bool SetStoredShips(string id, List<StoredShip> ships)
        {
            if (string.IsNullOrEmpty(id)) return false;
            var save = ReadSave(id);
            bool host = false;
            foreach (var login in logins.Values)
                if (login.account != null && string.Equals(login.account.id, id, StringComparison.Ordinal) && IsHostClient(login.client))
                { host = true; break; }
            // The host's first profile upload may not have created a server-side file yet. Seed it from
            // the live game profile so the transaction journal always has a durable replay target.
            if (save == null && host)
            {
                try { save = JsonUtility.FromJson<SaveData>(SaveGame.ProfileJson()); }
                catch (Exception e) { Debug.LogError($"NetProfiles: could not seed the host profile for a stored-ship transaction: {e.Message}"); }
            }
            if (save == null) return false;
            var target = ships != null ? new List<StoredShip>(ships) : new List<StoredShip>();
            if (target.Count > 100 || target.Exists(x => x == null || x.ship < 0)) return false;
            save.kaamoShips = new List<StoredShip>(target);
            if (!Write(ProfilePath(id), JsonUtility.ToJson(save, false))) return false;
            foreach (var login in logins.Values)
                if (login.account != null && string.Equals(login.account.id, id, StringComparison.Ordinal) &&
                    IsHostClient(login.client))
                {
                    Session.KaamoShips ??= new List<StoredShip>();
                    Session.KaamoShips.Clear();
                    Session.KaamoShips.AddRange(target);
                    break;
                }
            return true;
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
                // Flush file contents before the atomic rename, including transaction intent files.
                using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true))
                        writer.Write(text);
                    stream.Flush(flushToDisk: true);
                }
                // Replace the destination atomically. Deleting it before moving the temporary
                // file leaves a crash window with no primary profile or transaction journal.
                if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
                else File.Move(tmp, path);
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
